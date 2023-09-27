using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;
using Unity.MemoryProfiler.Editor.EnumerationUtilities;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using Unity.MemoryProfiler.Editor.UI;

namespace Unity.MemoryProfiler.Editor
{
    interface ISnapshotDataService
    {
        void SyncSnapshotsFolder();
        void UnloadAll();
        string GetSnapshotFolderPath();
    }

    internal class SnapshotDataService : IDisposable, ISnapshotDataService
    {
        const string k_SnapshotFileExtension = ".snap";
        const string k_SessionNameTempalte = "Session {0}";

        static ProfilerMarker s_CrawlManagedData = new ProfilerMarker("CrawlManagedData");

        bool m_CompareMode;
        CachedSnapshot m_BaseSnapshot;
        CachedSnapshot m_ComparedSnapshot;

        string m_SnapshotsFolderPath;
        List<SnapshotFileModel> m_AllSnapshots;
        Dictionary<uint, string> m_SessionNames;
        AsyncWorker<List<SnapshotFileModel>> m_BuildAllSnapshotsWorker;

        public SnapshotDataService()
        {
            m_AllSnapshots = new List<SnapshotFileModel>();
            m_SessionNames = new Dictionary<uint, string>();
            m_SnapshotsFolderPath = MemoryProfilerSettings.AbsoluteMemorySnapshotStoragePath;

            SyncSnapshotsFolder();
        }

        public bool CompareMode
        {
            get { return m_CompareMode; }
            set
            {
                if (m_CompareMode == value)
                    return;

                m_CompareMode = value;
                CompareModeChanged?.Invoke();
            }
        }

        public CachedSnapshot Base => m_BaseSnapshot;
        public CachedSnapshot Compared => m_ComparedSnapshot;
        public IReadOnlyList<SnapshotFileModel> AllSnapshots => m_AllSnapshots;
        public IReadOnlyDictionary<uint, string> SessionNames => m_SessionNames;

        public event Action CompareModeChanged;
        public event Action LoadedSnapshotsChanged;
        public event Action AllSnapshotsChanged;

        public void Load(string filePath)
        {
            if (IsOpen(filePath))
                return;

            var reader = new FileReader();
            ReadError err = reader.Open(filePath);
            if (err != ReadError.Success)
            {
                // Close and dispose the reader
                reader.Close();
                return;
            }

            if (m_CompareMode)
            {
                if (m_BaseSnapshot != null)
                {
                    UnloadSnapshot(ref m_ComparedSnapshot);
                    m_ComparedSnapshot = LoadSnapshot(reader);
                }
                else
                    m_BaseSnapshot = LoadSnapshot(reader);
            }
            else
            {
                UnloadSnapshot(ref m_BaseSnapshot);
                m_BaseSnapshot = LoadSnapshot(reader);
            }

            LoadedSnapshotsChanged?.Invoke();
        }

        public void Unload(string filePath)
        {
            if (PathHelpers.IsSamePath(m_BaseSnapshot?.FullPath, filePath))
            {
                UnloadSnapshot(ref m_BaseSnapshot);
                // Swap snapshots to make compared snapshot as base
                (m_BaseSnapshot, m_ComparedSnapshot) = (m_ComparedSnapshot, m_BaseSnapshot);
            }
            else if (PathHelpers.IsSamePath(m_ComparedSnapshot?.FullPath, filePath))
                UnloadSnapshot(ref m_ComparedSnapshot);
            else
                Debug.LogError("Trying to unload file which isn't loaded: " + filePath);

            LoadedSnapshotsChanged?.Invoke();
        }

        public void UnloadAll()
        {
            if ((m_BaseSnapshot == null) && (m_ComparedSnapshot == null))
                return;

            UnloadSnapshot(ref m_BaseSnapshot);
            UnloadSnapshot(ref m_ComparedSnapshot);

            LoadedSnapshotsChanged?.Invoke();
        }

        public void Swap()
        {
            (m_BaseSnapshot, m_ComparedSnapshot) = (m_ComparedSnapshot, m_BaseSnapshot);

            LoadedSnapshotsChanged?.Invoke();
        }

        public bool IsOpen(string filePath)
        {
            return PathHelpers.IsSamePath(m_BaseSnapshot?.FullPath, filePath) ||
                PathHelpers.IsSamePath(m_ComparedSnapshot?.FullPath, filePath);
        }

        public bool ValidateName(string fileName)
        {
            return fileName.IndexOfAny(Path.GetInvalidFileNameChars()) == -1;
        }

        public bool CanRename(string sourceFilePath, string targetFileName)
        {
            var targetFilePath = Path.Combine(Path.GetDirectoryName(sourceFilePath), targetFileName + k_SnapshotFileExtension);
            if (File.Exists(targetFilePath))
                return false;

            return true;
        }

        public bool Rename(string sourceFilePath, string targetFileName)
        {
            var targetFilePath = Path.Combine(Path.GetDirectoryName(sourceFilePath), targetFileName + k_SnapshotFileExtension);
            if (File.Exists(targetFilePath))
            {
                Debug.LogError($"Can't rename {sourceFilePath} to {targetFileName}, file with the same name is already exist!");
                return false;
            }

            if (IsOpen(sourceFilePath))
                Unload(sourceFilePath);

            ScreenshotsManager.SnapshotRenamed(sourceFilePath, targetFilePath);
            File.Move(sourceFilePath, targetFilePath);

            SyncSnapshotsFolder();

            return true;
        }

        public bool Import(string filePath)
        {
            using var importSnapshotEvent = MemoryProfilerAnalytics.BeginImportSnapshotEvent();

            var ret = ImportSnapshot(filePath);
            SyncSnapshotsFolder();

            return ret;
        }

        public bool Delete(string filePath)
        {
            if (IsOpen(filePath))
                Unload(filePath);

            if (!File.Exists(filePath))
                return false;

            ScreenshotsManager.SnapshotDeleted(filePath);
            File.Delete(filePath);

            SyncSnapshotsFolder();

            return true;
        }

        public void SyncSnapshotsFolder()
        {
            m_BuildAllSnapshotsWorker?.Dispose();
            m_BuildAllSnapshotsWorker = null;

            m_BuildAllSnapshotsWorker = new AsyncWorker<List<SnapshotFileModel>>();
            m_BuildAllSnapshotsWorker.Execute(() =>
            {
                try
                {
                    return BuildSnapshotsInfo(m_SnapshotsFolderPath, AllSnapshots);
                }
                catch (System.Threading.ThreadAbortException)
                {
                    // We expect a ThreadAbortException to be thrown when cancelling an in-progress builder. Do not log an error to the console.
                    return null;
                }
                catch (Exception _e)
                {
                    Debug.LogError($"{_e.Message}\n{_e.StackTrace}");
                    return null;
                }
            }, (result) =>
            {
                // Dispose asynchronous worker.
                m_BuildAllSnapshotsWorker?.Dispose();
                m_BuildAllSnapshotsWorker = null;

                // Update on success
                if (result != null)
                {
                    m_AllSnapshots = result;
                    UpdateSessionIds();
                    AllSnapshotsChanged?.Invoke();
                }
            });
        }

        /// <summary>
        /// A utility function that makes a sorted list of snapshots sessions and dictionary of sorted list of snapshots inside each session
        /// </summary>
        /// <param name="snapshots">List of all snapshots to process</param>
        /// <param name="sortedSessionIds">Returned list of sorted sessions</param>
        /// <param name="sessionsMap">Returned dictionary of lists for each session id</param>
        /// <returns>True if successeful</returns>
        public static bool MakeSortedSessionsListIds(in IReadOnlyList<SnapshotFileModel> snapshots, out List<uint> sortedSessionIds, out Dictionary<uint, IGrouping<uint, SnapshotFileModel>> sessionsMap)
        {
            if (snapshots.Count <= 0)
            {
                sortedSessionIds = null;
                sessionsMap = null;
                return false;
            }

            // Pre-sort snapshots
            var sortedSnapshots = new List<SnapshotFileModel>(snapshots);
            sortedSnapshots.Sort((l, r) => l.Timestamp.CompareTo(r.Timestamp));

            // Group snapshots by sessionId
            var _sessionsMap = sortedSnapshots.ToLookup(x => x.SessionId).ToDictionary(x => x.Key);

            // Sort sessionId list so that generated names order is the same as visual order in UI
            var _sortedSessionIds = _sessionsMap.Keys.ToList();
            _sortedSessionIds.Sort((l, r) => _sessionsMap[l].First().Timestamp.CompareTo(_sessionsMap[r].First().Timestamp));

            sessionsMap = _sessionsMap;
            sortedSessionIds = _sortedSessionIds;
            return true;
        }

        static CachedSnapshot LoadSnapshot(FileReader file)
        {
            if (!file.HasOpenFile)
                return null;

            using var loadSnapshotEvent = MemoryProfilerAnalytics.BeginLoadSnapshotEvent();

            ProgressBarDisplay.ShowBar(string.Format("Opening snapshot: {0}", System.IO.Path.GetFileNameWithoutExtension(file.FullPath)));
            var cachedSnapshot = new CachedSnapshot(file);
            using (s_CrawlManagedData.Auto())
            {
                var crawling = ManagedDataCrawler.Crawl(cachedSnapshot);
                crawling.MoveNext(); //start execution

                var status = crawling.Current as EnumerationStatus;
                float progressPerStep = 1.0f / status.StepCount;
                while (crawling.MoveNext())
                {
                    ProgressBarDisplay.UpdateProgress(status.CurrentStep * progressPerStep, status.StepStatus);
                }
            }

            loadSnapshotEvent.SetResult(cachedSnapshot);

            ProgressBarDisplay.ClearBar();

            return cachedSnapshot;
        }

        static void UnloadSnapshot(ref CachedSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            snapshot.Dispose();
            snapshot = null;
        }

        /// <summary>
        /// Maps all sessions to the unique SessionId and generates session names
        /// </summary>
        void UpdateSessionIds()
        {
            m_SessionNames = new Dictionary<uint, string>();

            if (!MakeSortedSessionsListIds(m_AllSnapshots, out var sortedSessionIds, out var sessionsMap))
                return;

            // Make session name based on the sorted order
            uint generatedSessionId = 1;
            foreach (var sessionId in sortedSessionIds)
            {
                var children = sessionsMap[sessionId];
                var sessionName = string.Format(k_SessionNameTempalte, generatedSessionId);
                m_SessionNames[sessionId] = sessionName;
                generatedSessionId++;
            }
        }

        static List<SnapshotFileModel> BuildSnapshotsInfo(string snapshotsPath, IReadOnlyList<SnapshotFileModel> snapshotInfos)
        {
            var snapshotsMap = snapshotInfos.ToDictionary(x => x.FullPath, x => x);

            var results = new List<SnapshotFileModel>();
            foreach (var snapshotFile in GetSnapshotFiles(snapshotsPath))
            {
                if (!snapshotsMap.TryGetValue(snapshotFile, out var snapshotsFile))
                {
                    var builder = new SnapshotFileModelBuilder(snapshotFile);
                    snapshotsFile = builder.Build();
                }

                if (snapshotsFile != null)
                    results.Add(snapshotsFile);
            }

            return results;
        }

        static IEnumerable<string> GetSnapshotFiles(string rootPath)
        {
            if (!Directory.Exists(rootPath))
                yield break;

            var directory = new DirectoryInfo(rootPath);
            var filesEnum = directory.GetFiles('*' + k_SnapshotFileExtension, SearchOption.TopDirectoryOnly);
            foreach (var file in filesEnum)
                yield return file.FullName;
        }

        bool ImportSnapshot(string sourceFilePath)
        {
            var snapshotFolderPath = GetSnapshotFolderPath();
            var targetFilePath = Path.Combine(snapshotFolderPath, Path.GetFileNameWithoutExtension(sourceFilePath) + k_SnapshotFileExtension);
            if (File.Exists(targetFilePath))
                return false;

            ScreenshotsManager.SnapshotImported(sourceFilePath, targetFilePath);
            File.Copy(sourceFilePath, targetFilePath);

            return true;
        }

        public void Dispose()
        {
            // Unload without notify
            LoadedSnapshotsChanged = null;
            UnloadAll();
        }

        public string GetSnapshotFolderPath()
        {
            var snapshotFolderPath = MemoryProfilerSettings.AbsoluteMemorySnapshotStoragePath;
            if (!Directory.Exists(snapshotFolderPath) && MemoryProfilerSettings.UsingDefaultMemorySnapshotStoragePath())
            {
                // If the path points to the default folder but doesn't exist, create it.
                // We don't create non default path folders though. As that could lead to unexpected behaviour for the User.
                Directory.CreateDirectory(snapshotFolderPath);
            }
            return snapshotFolderPath;
        }
    }
}

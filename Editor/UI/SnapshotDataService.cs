using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;
using Unity.MemoryProfiler.Editor.EnumerationUtilities;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using Unity.MemoryProfiler.Editor.Managed;
using Unity.MemoryProfiler.Editor.UI;
using UnityEditor;
using System.Threading.Tasks;
using System.Threading;

namespace Unity.MemoryProfiler.Editor
{
    interface ISnapshotDataService
    {
        void SetSnapshotsFolderDirty();
        void UnloadAll();
        string GetSnapshotFolderPath();
    }

    internal class SnapshotDataService : IDisposable, ISnapshotDataService
    {
        const string k_SnapshotFileExtension = ".snap";

        static ProfilerMarker s_CrawlManagedData = new ProfilerMarker("CrawlManagedData");

        bool m_CompareMode;
        CachedSnapshot m_BaseSnapshot;
        CachedSnapshot m_ComparedSnapshot;

        SnapshotFileListModel m_SnapshotFileListModel;
        AsyncWorker<SnapshotFileListModel> m_BuildAllSnapshotsWorker;
        FileSystemWatcherUnity m_SnapshotFolderWatcher;
        bool m_SnapshotFolderIsDirty;
        bool m_SnapshotFolderWasDeleted;
        [NonSerialized]
        bool m_Disposed;

        public bool SnapshotListModelIsUpToDate
        {
            get
            {
                if (m_SnapshotFolderIsDirty || m_SnapshotFileListModel == null || m_SnapshotFolderWatcher == null)
                {
                    return false;
                }
                m_SnapshotFolderWatcher.Refresh();
                return m_SnapshotFileListModel.SnapshotDirectoryLastWriteTimestampUtc >= m_SnapshotFolderWatcher.Directory.LastWriteTimeUtc;
            }
        }

        public SnapshotDataService()
        {
            var allSnapshots = new List<SnapshotFileModel>();
            // Build an empty list model
            var listBuilder = new SnapshotFileListModelBuilder(allSnapshots, DateTime.MinValue);
            m_SnapshotFileListModel = listBuilder.Build();

            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            MemoryProfilerSettings.SnapshotStoragePathChanged += SetupSnapshotFolderWatcher;

            SetupSnapshotFolderWatcher();
            SyncSnapshotsFolder();
        }

        void Update()
        {
            if (m_Disposed)
            {
                // Disposing might happen while the Update calls are being cycled through,
                // so the previous deregistration might not have affected the list of event subscribers currently being called yet.
                // Just for good measure, deregister again here though.
                EditorApplication.update -= Update;
                return;
            }

            if (m_SnapshotFolderWasDeleted)
            {
                // delayed folder restoring on main thread
                m_SnapshotFolderWasDeleted = false;
                SetupSnapshotFolderWatcher();
            }

            if (m_SnapshotFolderIsDirty)
            {
                SyncSnapshotsFolder();
            }
        }

        void DirectoryChanged(FileSystemWatcherUnity.State state, object exception)
        {
            switch (state)
            {
                case FileSystemWatcherUnity.State.None:
                    break;
                case FileSystemWatcherUnity.State.Changed:
                    SetSnapshotsFolderDirty();
                    break;
                case FileSystemWatcherUnity.State.DirectoryDeleted:
                    // Delay setting up a new watcher to the Update cycle so it happens on the main thread
                    // Otherwise this method (as it's called by the task worker thread) will silently fail in EditorPrefs.GetString when fetching the directory to recreate
                    m_SnapshotFolderWasDeleted = true;

                    break;
                case FileSystemWatcherUnity.State.Exception:
                    Debug.LogException(exception as Exception);
                    SetupSnapshotFolderWatcher();
                    break;
                default:
                    break;
            }
        }

        void SetupSnapshotFolderWatcher()
        {
            var path = GetOrCreateSnapshotFolderPath();

            m_SnapshotFolderWatcher?.Dispose();
            m_SnapshotFolderWatcher = new FileSystemWatcherUnity(path);
            m_SnapshotFolderWatcher.Changed += DirectoryChanged;

            // Always assume the directory is dirty after setup
            m_SnapshotFolderIsDirty = true;
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
        public IReadOnlyList<SnapshotFileModel> AllSnapshots => m_SnapshotFileListModel.AllSnapshots;
        public IReadOnlyDictionary<uint, string> SessionNames => m_SnapshotFileListModel.SessionNames;
        public SnapshotFileListModel FullSnapshotList => m_SnapshotFileListModel;

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

        public void SetSnapshotsFolderDirty()
        {
            m_SnapshotFolderIsDirty = true;
        }

        public void SyncSnapshotsFolder()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(SnapshotDataService));

            m_BuildAllSnapshotsWorker?.Dispose();
            m_BuildAllSnapshotsWorker = null;

            m_BuildAllSnapshotsWorker = new AsyncWorker<SnapshotFileListModel>();
            // Check and store updated state
            m_SnapshotFolderWatcher.Refresh();
            // grab a copy of the directory so that a directory change on main thread will not bleed into the worker thread
            var snapshotDirectory = m_SnapshotFolderWatcher.Directory;
            // pre-declare directory as clean, because it will be once the worker is done
            m_SnapshotFolderIsDirty = false;
            m_BuildAllSnapshotsWorker.Execute((token) =>
            {
                try
                {
                    return BuildSnapshotsInfo(token, snapshotDirectory, AllSnapshots);
                }
                catch (TaskCanceledException)
                {
                    // We expect a TaskCanceledException to be thrown when cancelling an in-progress builder. Do not log an error to the console.
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
                    if (result.Equals(m_SnapshotFileListModel))
                    {
                        m_SnapshotFileListModel.UpdateTimeStamp(result.SnapshotDirectoryLastWriteTimestampUtc);
                    }
                    else
                    {
                        // only really update if there is an actual change
                        m_SnapshotFileListModel = result;
                        AllSnapshotsChanged?.Invoke();
                    }
                }
            });
        }

        static CachedSnapshot LoadSnapshot(FileReader file)
        {
            if (!file.HasOpenFile)
                return null;

            using var loadSnapshotEvent = MemoryProfilerAnalytics.BeginLoadSnapshotEvent();

            ProgressBarDisplay.ShowBar(string.Format("Opening snapshot: {0}", System.IO.Path.GetFileNameWithoutExtension(file.FullPath)));
            try
            {
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
                return cachedSnapshot;
            }
            // Don't let an exception prevent the progress bar from being cleared.
            // Otherwise debugging the Editor when exceptions happen becomes really annoying as it's hard to get rid of manually,
            // and also it'll muddy bug reports as people will overlook the logged Exception and report infinite opening times instead.
            finally
            {
                ProgressBarDisplay.ClearBar();
            }
        }

        static void UnloadSnapshot(ref CachedSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            snapshot.Dispose();
            snapshot = null;
        }

        static SnapshotFileListModel BuildSnapshotsInfo(CancellationToken token, DirectoryInfo snapshotsDirectory, IReadOnlyList<SnapshotFileModel> snapshotInfos)
        {
            var snapshotsMap = snapshotInfos.ToDictionary(x => x.FullPath, x => x);

            var allSnapshots = new List<SnapshotFileModel>();
            foreach (var snapshotFile in GetSnapshotFiles(snapshotsDirectory))
            {
                if (token.IsCancellationRequested)
                    return null;
                if (!snapshotsMap.TryGetValue(snapshotFile, out var snapshotsFile))
                {
                    var builder = new SnapshotFileModelBuilder(snapshotFile);
                    snapshotsFile = builder.Build();
                }

                if (snapshotsFile != null)
                    allSnapshots.Add(snapshotsFile);
            }

            if (token.IsCancellationRequested)
                return null;

            var snapshotFileListBuilder = new SnapshotFileListModelBuilder(allSnapshots, snapshotsDirectory.LastWriteTimeUtc);
            var snapshotFileListModel = snapshotFileListBuilder.Build();
            return snapshotFileListModel;
        }

        static IEnumerable<string> GetSnapshotFiles(DirectoryInfo directory)
        {
            try
            {
                if (!directory.Exists)
                    yield break;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to fetch snapshots from: {directory.FullName}\n{e}");
                yield break;
            }

            var filesEnum = directory.GetFiles('*' + k_SnapshotFileExtension, SearchOption.AllDirectories);
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
            m_Disposed = true;
            // Unload without notify
            LoadedSnapshotsChanged = null;

            m_BuildAllSnapshotsWorker?.Dispose();
            m_BuildAllSnapshotsWorker = null;

            m_SnapshotFolderWatcher?.Dispose();
            m_SnapshotFolderWatcher = null;

            EditorApplication.update -= Update;
            MemoryProfilerSettings.SnapshotStoragePathChanged -= SetSnapshotsFolderDirty;
            UnloadAll();
        }

        public string GetSnapshotFolderPath()
        {
            if (m_SnapshotFolderWatcher != null && m_SnapshotFolderWatcher.Directory != null)
            {
                m_SnapshotFolderWatcher.Directory.Refresh();
                if (m_SnapshotFolderWatcher.Directory.Exists && m_SnapshotFolderWatcher.Directory.FullName == MemoryProfilerSettings.AbsoluteMemorySnapshotStoragePath)
                    return m_SnapshotFolderWatcher.Directory.FullName;
            }
            SetupSnapshotFolderWatcher();
            return m_SnapshotFolderWatcher.Directory.FullName;
        }

        string GetOrCreateSnapshotFolderPath()
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

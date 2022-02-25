using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;
using System;
using System.Linq;
using Unity.MemoryProfiler.Editor;
using System.Text;
using Unity.MemoryProfiler.Editor.Extensions.String;
using Unity.MemoryProfiler.Editor.UIContentData;
using Unity.MemoryProfiler.Editor.Diagnostics;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor
{
    [Serializable]
    internal class SessionInfo : IComparable<SessionInfo>
    {
        public readonly uint SessionId;
        public readonly string ProductName;
        public readonly List<SnapshotFileData> m_Snapshots;
        public int Count => m_Snapshots.Count;
        public SnapshotFileData this[int index] => m_Snapshots[index];
        public IEnumerable<SnapshotFileData> Snapshots => m_Snapshots;

        public DynamicVisualElements DynamicUIElements;

        DateTime m_DateTime;

        public string SessionName
        {
            get => m_SessionName;
            set
            {
                if (m_SessionName != value || value != DynamicUIElements.Foldout.text)
                {
                    m_SessionName = value;
                    SessionNameChanged(value);
                    DynamicUIElements.UpdateFoldoutUI(this);
                }
            }
        }
        private string m_SessionName;
        public string UnityVersion
        {
            get => m_UnityVersion;
            set
            {
                if (m_UnityVersion != value)
                {
                    m_UnityVersion = value;
                    DynamicUIElements.UpdateFoldoutUI(this);
                }
            }
        }
        public string m_UnityVersion;

        public event Action<string> SessionNameChanged = delegate {};

        internal struct DynamicVisualElements
        {
            public VisualElement Root;
            public Foldout Foldout;
            public void UpdateFoldoutUI(SessionInfo session)
            {
                if (Foldout == null)
                    return;
                Foldout.text = string.Format(TextContent.SessionFoldoutLabel.text, session.SessionName, session.ProductName);
                Foldout.tooltip = string.Format(TextContent.SessionFoldoutLabel.tooltip, Foldout.text, session.UnityVersion, session.SessionId);
            }
        }

        public SessionInfo(SnapshotFileData firstSnapshot)
        {
            SessionId = firstSnapshot.GuiData.SessionId;
            m_SessionName = firstSnapshot.GuiData.ProductName;
            UnityVersion = firstSnapshot.GuiData.UnityVersion;
            ProductName = firstSnapshot.GuiData.ProductName;
            m_DateTime = firstSnapshot.GuiData.UtcDateTime;
            m_Snapshots = new List<SnapshotFileData>
            {
                firstSnapshot
            };
        }

        public void Add(SnapshotFileData snapshot)
        {
            // take the date from the earliest snapshot as date for the session
            if (m_DateTime.CompareTo(snapshot.GuiData.UtcDateTime) > 0)
                m_DateTime = snapshot.GuiData.UtcDateTime;

            for (int i = 0; i < m_Snapshots.Count; i++)
            {
                if (m_Snapshots[i].CompareTo(snapshot) > 0)
                {
                    m_Snapshots.Insert(i, snapshot);
                    return;
                }
            }
            m_Snapshots.Add(snapshot);
            // The list is always sorted
            //Sort();
        }

        public bool Remove(SnapshotFileData snapshot)
        {
            return m_Snapshots.Remove(snapshot);
        }

        public int CompareTo(SessionInfo other)
        {
            return m_DateTime.CompareTo(other.m_DateTime);
        }

        public void Sort()
        {
            // The list is always sorted
            //m_Snapshots.Sort();
        }
    }

    [Serializable]
    internal struct SessionListEnumerator
    {
        public SessionInfo SessionInfo;
        public SnapshotFileData Snapshot;
    }

    internal class SnapshotCollectionEnumerator : IEnumerator<SessionListEnumerator>
    {
        int m_SessionIndex;
        int m_SnapshotIndex;
        List<SessionInfo> m_Files;

        public SessionListEnumerator Current
        {
            get
            {
                return new SessionListEnumerator { SessionInfo = m_Files[m_SessionIndex], Snapshot = m_Files[m_SessionIndex][m_SnapshotIndex] };
            }
        }
        object IEnumerator.Current { get { return Current; } }
        public int Count { get { return m_Files.Count; } }

        internal SnapshotCollectionEnumerator(List<SessionInfo> files)
        {
            m_Files = files;
            Reset();
        }

        public void Dispose()
        {
            m_Files = null;
        }

        public bool MoveNext()
        {
            if (m_Files == null)
                return false;
            if (m_SessionIndex == -1 || (m_SessionIndex < m_Files.Count && m_Files[m_SessionIndex].Count <= m_SnapshotIndex + 1))
            {
                ++m_SessionIndex;
                m_SnapshotIndex = 0;
            }
            else
            {
                ++m_SnapshotIndex;
            }
            return m_SessionIndex < m_Files.Count && m_Files[m_SessionIndex].Count > m_SnapshotIndex;
        }

        public void Reset()
        {
            m_SessionIndex = -1;
            m_SnapshotIndex = 0;
        }
    }

    internal enum ImportMode
    {
        Copy,
        Move
    }

    internal class SnapshotCollection : IDisposable
    {
        DirectoryInfo m_Info;
        List<SessionInfo> m_Sessions = new List<SessionInfo>();
        Dictionary<uint, SessionInfo> m_SessionsDictionary = new Dictionary<uint, SessionInfo>();
        public event Action<SnapshotCollectionEnumerator> collectionRefresh = delegate {};
        public event Action<SessionInfo> sessionDeleted = delegate {};
        public event Action<uint, string> SessionNameChanged = delegate {};
        public event Action SnapshotCountIncreased = delegate {};
        public event Action<SnapshotFileData> SnapshotTakenAndAdded = delegate {};

        public int SnapshotCount { get; private set; }

        public string Name { get { return m_Info.Name; } }

        public SnapshotCollection(string collectionPath)
        {
            m_Info = new DirectoryInfo(collectionPath);
            m_Sessions = new List<SessionInfo>();
            m_SessionsDictionary = new Dictionary<uint, SessionInfo>();

            if (!m_Info.Exists)
                m_Info.Create();

            RefreshFileListInternal(m_Info);
        }

        internal void RefreshScreenshots()
        {
            foreach (var session in m_Sessions)
            {
                foreach (var snapshot in session.Snapshots)
                {
                    snapshot.RefreshScreenshot();
                }
            }
        }

        void RefreshFileListInternal(DirectoryInfo info)
        {
            Cleanup();
            m_Sessions.Clear();
            m_SessionsDictionary.Clear();
            var fileEnumerator = info.GetFiles('*' + FileExtensionContent.SnapshotFileExtension, SearchOption.AllDirectories);
            var oldSnapshotCount = SnapshotCount;
            SnapshotCount = 0;
            for (int i = 0; i < fileEnumerator.Length; ++i)
            {
                FileInfo fInfo = fileEnumerator[i];
                if (fInfo.Length != 0)
                {
                    try
                    {
                        var fileData = new SnapshotFileData(fInfo);
                        AddSnapshotToSessionsList(fileData);
                        ++SnapshotCount;
                    }
                    catch (IOException e)
                    {
                        Debug.LogError("Failed to load snapshot, error: " + e.Message);
                    }
                }
            }
            if (SnapshotCount > oldSnapshotCount)
                SnapshotCountIncreased();

            m_Sessions.Sort();

            foreach (var session in m_SessionsDictionary)
            {
                session.Value.Sort();
            }

            if (collectionRefresh != null)
            {
                using (var it = GetEnumerator())
                    collectionRefresh(it);
            }
        }

        void AddSnapshotToSessionsList(SnapshotFileData fileData)
        {
            if (!m_SessionsDictionary.ContainsKey(fileData.GuiData.SessionId))
            {
                var session = new SessionInfo(fileData);
                m_Sessions.Add(session);
                m_SessionsDictionary[fileData.GuiData.SessionId] = session;
            }
            else
            {
                m_SessionsDictionary[fileData.GuiData.SessionId].Add(fileData);
            }
            fileData.GuiData.SessionName = m_SessionsDictionary[fileData.GuiData.SessionId].SessionName;
            var id = fileData.GuiData.SessionId;
            m_SessionsDictionary[fileData.GuiData.SessionId].SessionNameChanged += (s) => SessionNameChanged(id, s);
        }

        void RemoveSnapshotFromSessionsList(SnapshotFileData fileData)
        {
            var session = fileData.GuiData.SessionId;
            m_SessionsDictionary[session].Remove(fileData);
            if (m_SessionsDictionary[session].Count == 0)
            {
                // get rid of the session, if that was the last snapshot in it.
                m_Sessions.Remove(m_SessionsDictionary[session]);
                m_SessionsDictionary.Remove(session);
            }
        }

        public bool RenameSnapshot(SnapshotFileData snapshot, string name)
        {
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                EditorUtility.DisplayDialog("Error", string.Format("Filename '{0}' contains invalid characters", name), "OK");
                return false;
            }
            int nameStart = snapshot.FileInfo.FullName.LastIndexOf(snapshot.FileInfo.Name);
            string targetPath = snapshot.FileInfo.FullName.Substring(0, nameStart) + name + FileExtensionContent.SnapshotFileExtension;

            if (targetPath == snapshot.FileInfo.FullName)
            {
                snapshot.GuiData.VisualElement.SnapshotNameLabel.text = snapshot.GuiData.Name;
                snapshot.GuiData.VisualElement.RenamingFieldVisible = false;
                return false;
            }

            var dir = snapshot.FileInfo.FullName.Substring(0, nameStart);
            if (Directory.GetFiles(dir).Contains(string.Format("{0}{1}{2}", dir, name, FileExtensionContent.SnapshotFileExtension)))
            {
                EditorUtility.DisplayDialog("Error", string.Format("Filename '{0}' already exists", name), "OK");
                return false;
            }

            snapshot.GuiData.Name = name;
            snapshot.GuiData.VisualElement.SnapshotNameLabel.text = name;
            snapshot.GuiData.VisualElement.RenamingFieldVisible = false;

#if UNITY_2019_3_OR_NEWER
            if (snapshot.GuiData.GuiTexture != null)
            {
                string possibleSSPath = Path.ChangeExtension(snapshot.FileInfo.FullName, FileExtensionContent.SnapshotScreenshotFileExtension);
                if (File.Exists(possibleSSPath))
                {
                    File.Move(possibleSSPath, Path.ChangeExtension(targetPath, FileExtensionContent.SnapshotScreenshotFileExtension));
                }
            }
#endif
            //move snapshot after screenshot
            snapshot.FileInfo.MoveTo(targetPath);
            m_Info.Refresh();
            return true;
        }

        public void RemoveSnapshotFromCollection(SnapshotFileData snapshot, bool removeEmptySessions = true)
        {
            var sessionIsEmpty = false;
            var sessionId = snapshot.GuiData.SessionId;
            if (m_SessionsDictionary.ContainsKey(sessionId))
            {
                m_SessionsDictionary[sessionId].Remove(snapshot);
                if (m_SessionsDictionary[sessionId].Count == 0)
                    sessionIsEmpty = true;
            }
            snapshot.FileInfo.Delete();
#if UNITY_2019_3_OR_NEWER
            string possibleSSPath = Path.ChangeExtension(snapshot.FileInfo.FullName, FileExtensionContent.SnapshotScreenshotFileExtension);
            if (File.Exists(possibleSSPath))
            {
                File.Delete(possibleSSPath);
            }
#endif
            if (sessionIsEmpty && removeEmptySessions)
                RemoveSessionFromCollection(m_SessionsDictionary[sessionId]);
            m_Info.Refresh();
        }

        public void RemoveSessionFromCollection(SessionInfo sessionInfo)
        {
            foreach (var snapshot in sessionInfo.Snapshots)
            {
                RemoveSnapshotFromCollection(snapshot, removeEmptySessions: false);
            }
            if (m_SessionsDictionary.ContainsKey(sessionInfo.SessionId))
            {
                Checks.CheckEquals(sessionInfo.Snapshots == null || sessionInfo.Count == 0, true);
                m_Sessions.Remove(sessionInfo);
                m_SessionsDictionary.Remove(sessionInfo.SessionId);
                sessionDeleted(sessionInfo);
            }
            m_Info.Refresh();
        }

        public void RemoveSnapshotFromCollection(SnapshotCollectionEnumerator iter)
        {
            if (iter.Current.Snapshot == null)
            {
                RemoveSessionFromCollection(iter.Current.SessionInfo);
            }
            else
            {
                RemoveSnapshotFromCollection(iter.Current.Snapshot);
            }
        }

        public SnapshotFileData AddSnapshotToCollection(string path, ImportMode mode)
        {
            FileInfo file = new FileInfo(path);

            StringBuilder newPath = new StringBuilder(256);
            newPath.Append(Path.Combine(MemoryProfilerSettings.AbsoluteMemorySnapshotStoragePath, Path.GetFileNameWithoutExtension(file.Name)));
            string finalPath = string.Format("{0}{1}", newPath.ToString(), FileExtensionContent.SnapshotFileExtension);
            bool samePath = finalPath.ToLowerInvariant() == path.ToLowerInvariant();
            if (File.Exists(finalPath) && !samePath)
            {
                string searchStr = string.Format("{0}*{1}",
                    Path.GetFileNameWithoutExtension(file.Name), FileExtensionContent.SnapshotFileExtension);

                var files = m_Info.GetFiles(searchStr);

                int snapNum = 1;
                StringBuilder postFixStr = new StringBuilder("(1)");
                for (int i = 0; i < files.Length; ++i)
                {
                    if (files[i].Name.Contains(postFixStr.ToString()))
                    {
                        ++snapNum;
                        postFixStr.Clear();
                        postFixStr.Append('(');
                        postFixStr.Append(snapNum);
                        postFixStr.Append(')');
                    }
                }

                newPath.Append(' ');
                newPath.Append(postFixStr);
                newPath.Append(FileExtensionContent.SnapshotFileExtension);
                finalPath = newPath.ToString();
            }


            string originalSSPath = path.Replace(FileExtensionContent.SnapshotFileExtension, FileExtensionContent.SnapshotScreenshotFileExtension);
            bool ssExists = File.Exists(originalSSPath);
            switch (mode)
            {
                case ImportMode.Copy:
                    file = file.CopyTo(finalPath, false);
                    if (ssExists)
                        File.Copy(originalSSPath, finalPath.Replace(FileExtensionContent.SnapshotFileExtension, FileExtensionContent.SnapshotScreenshotFileExtension));
                    break;
                case ImportMode.Move:
                    file.MoveTo(finalPath);
                    if (ssExists)
                        File.Move(originalSSPath, finalPath.Replace(FileExtensionContent.SnapshotFileExtension, FileExtensionContent.SnapshotScreenshotFileExtension));
                    break;
            }

            ResetSnapshotRootDirectionInfo();

            var snapFileData = new SnapshotFileData(file);

            AddSnapshotToSessionsList(snapFileData);
            ++SnapshotCount;
            SnapshotCountIncreased();
            var sessionId = snapFileData.GuiData.SessionId;

            m_Info.Refresh();
            m_Sessions.Sort();
            m_SessionsDictionary[sessionId].Sort();

            if (collectionRefresh != null)
            {
                using (var it = GetEnumerator())
                    collectionRefresh(it);
            }

            SnapshotTakenAndAdded(snapFileData);
            return snapFileData;
        }

        internal bool SnapshotExists(string path)
        {
            using (var snapshots = GetEnumerator())
            {
                while (snapshots.MoveNext())
                {
                    //get full path used to normalize seperators
                    if (snapshots.Current.Snapshot != null && Path.GetFullPath(snapshots.Current.Snapshot.FileInfo.FullName) == Path.GetFullPath(path))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public void RefreshCollection()
        {
            RefreshCollection(false);
        }

        void RefreshCollection(bool forceRefresh)
        {
            if (CheckSnapshotRootDirectoryForChanges() || forceRefresh)
            {
                ResetSnapshotRootDirectionInfo();
                RefreshFileListInternal(m_Info);
            }
        }

        DirectoryInfo GetSnapshotRootDirectory()
        {
            var rootDir = new DirectoryInfo(m_Info.FullName);
            if (!rootDir.Exists)
                rootDir.Create();
            return rootDir;
        }

        public bool CheckSnapshotRootDirectoryForChanges()
        {
            var rootDir = GetSnapshotRootDirectory();
            return rootDir.LastWriteTime != m_Info.LastWriteTime;
        }

        void ResetSnapshotRootDirectionInfo()
        {
            m_Info = new DirectoryInfo(m_Info.FullName);
        }

        public SnapshotCollectionEnumerator GetEnumerator()
        {
            return new SnapshotCollectionEnumerator(m_Sessions);
        }

        void Cleanup()
        {
            m_SessionsDictionary.Clear();
            if (m_Sessions.Count > 0)
            {
                for (int i = 0; i < m_Sessions.Count; ++i)
                {
                    if (m_Sessions[i].Snapshots != null && m_Sessions[i].Count > 0)
                    {
                        for (int j = 0; j < m_Sessions[i].Count; j++)
                        {
                            m_Sessions[i][j].Dispose();
                        }
                    }
                }

                m_Sessions.Clear();
            }
        }

        public void Dispose()
        {
            Cleanup();
        }
    }
}

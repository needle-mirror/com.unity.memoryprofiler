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
#if UNITY_2019_1_OR_NEWER
using UnityEngine.UIElements;
#else
using UnityEngine.Experimental.UIElements;
#endif
using UnityEditorInternal;

namespace Unity.MemoryProfiler.Editor
{
    internal class SnapshotCollectionEnumerator : IEnumerator<SnapshotFileData>
    {
        int m_Index;
        List<SnapshotFileData> m_Files;

        public SnapshotFileData Current { get { return m_Files[m_Index]; } }
        object IEnumerator.Current { get { return Current; } }
        public int Count { get { return m_Files.Count; } }

        internal SnapshotCollectionEnumerator(List<SnapshotFileData> files)
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

            ++m_Index;
            return m_Index < m_Files.Count;
        }

        public void Reset()
        {
            m_Index = -1;
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
        List<SnapshotFileData> m_Snapshots;
        public Action<SnapshotCollectionEnumerator> collectionRefresh;

        public string Name { get { return m_Info.Name; } }

        public SnapshotCollection(string collectionPath)
        {
            m_Info = new DirectoryInfo(collectionPath);
            m_Snapshots = new List<SnapshotFileData>();

            if (!m_Info.Exists)
                m_Info.Create();

            RefreshFileListInternal(m_Info);
        }

        internal void RefreshScreenshots()
        {
            foreach (var snapshot in m_Snapshots)
            {
                snapshot.RefreshScreenshot();
            }
        }

        void RefreshFileListInternal(DirectoryInfo info)
        {
            Cleanup();
            m_Snapshots = new List<SnapshotFileData>();
            var fileEnumerator = info.GetFiles('*' + MemoryProfilerWindow.k_SnapshotFileExtension, SearchOption.AllDirectories);
            for (int i = 0; i < fileEnumerator.Length; ++i)
            {
                FileInfo fInfo = fileEnumerator[i];
                if (fInfo.Length != 0)
                {
                    try
                    {
                        m_Snapshots.Add(new SnapshotFileData(fInfo));
                    }
                    catch (IOException e)
                    {
                        Debug.LogError("Failed to load snapshot, error: " + e.Message);
                    }
                }
            }

            m_Snapshots.Sort();

            if (collectionRefresh != null)
            {
                using (var it = GetEnumerator())
                    collectionRefresh(it);
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
            string targetPath = snapshot.FileInfo.FullName.Substring(0, nameStart) + name + MemoryProfilerWindow.k_SnapshotFileExtension;

            if (targetPath == snapshot.FileInfo.FullName)
            {
                snapshot.GuiData.dynamicVisualElements.snapshotNameLabel.text = snapshot.GuiData.name.text;
                snapshot.GuiData.RenamingFieldVisible = false;
                return false;
            }

            var dir = snapshot.FileInfo.FullName.Substring(0, nameStart);
            if (Directory.GetFiles(dir).Contains(string.Format("{0}{1}{2}", dir, name, MemoryProfilerWindow.k_SnapshotFileExtension)))
            {
                EditorUtility.DisplayDialog("Error", string.Format("Filename '{0}' already exists", name), "OK");
                return false;
            }

            snapshot.GuiData.name = new GUIContent(name);
            snapshot.GuiData.dynamicVisualElements.snapshotNameLabel.text = name;
            snapshot.GuiData.RenamingFieldVisible = false;

#if UNITY_2019_3_OR_NEWER
            if (snapshot.GuiData.guiTexture != null)
            {
                string possibleSSPath = Path.ChangeExtension(snapshot.FileInfo.FullName, ".png");
                if (File.Exists(possibleSSPath))
                {
                    File.Move(possibleSSPath, Path.ChangeExtension(targetPath, ".png"));
                }
            }
#endif
            //move snapshot after screenshot
            snapshot.FileInfo.MoveTo(targetPath);
            m_Info.Refresh();
            return true;
        }

        public void RemoveSnapshotFromCollection(SnapshotFileData snapshot)
        {
            snapshot.FileInfo.Delete();
            m_Snapshots.Remove(snapshot);
#if UNITY_2019_3_OR_NEWER
            string possibleSSPath = Path.ChangeExtension(snapshot.FileInfo.FullName, ".png");
            if (File.Exists(possibleSSPath))
            {
                File.Delete(possibleSSPath);
            }
#endif
            m_Info.Refresh();
        }

        public void RemoveSnapshotFromCollection(SnapshotCollectionEnumerator iter)
        {
            RemoveSnapshotFromCollection(iter.Current);
        }

        public SnapshotFileData AddSnapshotToCollection(string path, ImportMode mode)
        {
            FileInfo file = new FileInfo(path);

            StringBuilder newPath = new StringBuilder(256);
            newPath.Append(Path.Combine(MemoryProfilerSettings.AbsoluteMemorySnapshotStoragePath, Path.GetFileNameWithoutExtension(file.Name)));
            string finalPath = string.Format("{0}{1}", newPath.ToString(), MemoryProfilerWindow.k_SnapshotFileExtension);
            bool samePath = finalPath.ToLowerInvariant() == path.ToLowerInvariant();
            if (File.Exists(finalPath) && !samePath)
            {
                string searchStr = string.Format("{0}*{1}",
                    Path.GetFileNameWithoutExtension(file.Name), MemoryProfilerWindow.k_SnapshotFileExtension);

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
                newPath.Append(MemoryProfilerWindow.k_SnapshotFileExtension);
                finalPath = newPath.ToString();
            }


            string originalSSPath = path.Replace(MemoryProfilerWindow.k_SnapshotFileExtension, ".png");
            bool ssExists = File.Exists(originalSSPath);
            switch (mode)
            {
                case ImportMode.Copy:
                    file = file.CopyTo(finalPath, false);
                    if (ssExists)
                        File.Copy(originalSSPath, finalPath.Replace(MemoryProfilerWindow.k_SnapshotFileExtension, ".png"));
                    break;
                case ImportMode.Move:
                    file.MoveTo(finalPath);
                    if (ssExists)
                        File.Move(originalSSPath, finalPath.Replace(MemoryProfilerWindow.k_SnapshotFileExtension, ".png"));
                    break;
            }


            var snapFileData = new SnapshotFileData(file);
            m_Snapshots.Add(snapFileData);
            m_Info.Refresh();
            m_Snapshots.Sort();

            if (collectionRefresh != null)
            {
                using (var it = GetEnumerator())
                    collectionRefresh(it);
            }

            return snapFileData;
        }

        public void RefreshCollection()
        {
            DirectoryInfo rootDir = new DirectoryInfo(m_Info.FullName);
            if (!rootDir.Exists)
                rootDir.Create();

            if (rootDir.LastWriteTime != m_Info.LastWriteTime)
            {
                m_Info = new DirectoryInfo(m_Info.FullName);
                RefreshFileListInternal(m_Info);
            }
        }

        public SnapshotCollectionEnumerator GetEnumerator()
        {
            return new SnapshotCollectionEnumerator(m_Snapshots);
        }

        void Cleanup()
        {
            if (m_Snapshots != null && m_Snapshots.Count > 0)
            {
                for (int i = 0; i < m_Snapshots.Count; ++i)
                    m_Snapshots[i].Dispose();

                m_Snapshots.Clear();
            }
            m_Snapshots = null;
        }

        public void Dispose()
        {
            Cleanup();
        }
    }
}

using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.EditorCoroutines.Editor;
using System.Collections;
using Unity.MemoryProfiler.Editor.UIContentData;
using Unity.MemoryProfiler.Editor.UI;
using Unity.MemoryProfiler.Editor.Format;

namespace Unity.MemoryProfiler.Editor
{
    internal class GUITexture2DAsset
    {
        public enum SourceType
        {
            None,
            Image,
            Snapshot
        }

        public void SetTexture(Texture2D tex, SourceType src, long timeStamp)
        {
            if (Texture != null)
                UnityEngine.Object.DestroyImmediate(Texture);
            Texture = tex;
            Source = src;
            TimeStampTicks = timeStamp;
        }

        public Texture2D Texture { get; private set; }
        public SourceType Source { get; private set; }

        public long TimeStampTicks { get; private set; }
    }

    internal class SnapshotFileGUIData
    {
        public Texture MetaScreenshot { get { return GuiTexture.Texture; } }
        public readonly uint SessionId;
        public readonly string Date;
        public readonly string ProductName;
        public readonly string UnityVersion;
        public readonly DateTime UtcDateTime;
        public readonly string MetaContent;
        public readonly string MetaPlatform;
        public readonly string MetaPlatformExtra;
        public readonly string SnapshotDate;

        public string Name;

        public string SessionName = TextContent.UnknownSession;
        public RuntimePlatform RuntimePlatform = (RuntimePlatform)(-1);
        public GUITexture2DAsset GuiTexture;
        public SnapshotListItem VisualElement;
        public ProfileTargetInfo? TargetInfo;
        public ProfileTargetMemoryStats? MemoryStats;

        public enum State
        {
            Closed,
            Open,
            OpenA,
            OpenB,
            InView,
        }

        public State CurrentState
        {
            get
            {
                return m_CurrentState;
            }
            private set
            {
                if (value != m_CurrentState)
                {
                    VisualElement.CurrentState = value;
                    m_CurrentState = value;
                }
            }
        }
        State m_CurrentState = State.Closed;

        public void SetCurrentState(bool open, bool first, bool compareMode)
        {
            if (!open)
            {
                CurrentState = State.Closed;
                return;
            }
            if (first)
            {
                if (compareMode)
                {
                    CurrentState = State.OpenA;
                }
                else
                {
                    CurrentState = State.InView;
                }
            }
            else
            {
                if (compareMode)
                {
                    CurrentState = State.OpenB;
                }
                else
                {
                    CurrentState = State.Open;
                }
            }
        }

        public SnapshotFileGUIData(FileReader reader, string name, MetaData snapshotMetadata)
        {
            Checks.CheckEquals(true, reader.HasOpenFile);
            unsafe
            {
                long ticks;
                reader.ReadUnsafe(EntryType.Metadata_RecordDate, &ticks, UnsafeUtility.SizeOf<long>(), 0, 1);
                UtcDateTime = new DateTime(ticks);
            }

            Date = UtcDateTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

            Name = name;
            MetaContent = snapshotMetadata.Content;
            MetaPlatform = snapshotMetadata.Platform;
            MetaPlatformExtra = snapshotMetadata.PlatformExtra;
            SessionId = snapshotMetadata.SessionGUID;
            UnityVersion = snapshotMetadata.UnityVersion;
            ProductName = snapshotMetadata.ProductName;
            TargetInfo = snapshotMetadata.TargetInfo;
            MemoryStats = snapshotMetadata.TargetMemoryStats;
        }
    }

    //Add GetHashCode() override if we ever want to hash these
#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()

    [Serializable]
    internal class SnapshotFileData : IDisposable, IComparable<SnapshotFileData>
#pragma warning restore CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
    {
        public FileInfo FileInfo;
        SnapshotFileGUIData m_GuiData;

        public SnapshotFileGUIData GuiData { get { return m_GuiData; } }

        public SnapshotFileData(FileInfo info)
        {
            FileInfo = info;
            using (var snapReader = LoadSnapshot())
            {
                MetaData snapshotMetadata = new MetaData(snapReader);
                m_GuiData = new SnapshotFileGUIData(snapReader, Path.GetFileNameWithoutExtension(FileInfo.Name), snapshotMetadata);

                RuntimePlatform runtimePlatform;
                if (TryGetRuntimePlatform(snapshotMetadata.Platform, out runtimePlatform))
                    m_GuiData.RuntimePlatform = runtimePlatform;

                m_GuiData.GuiTexture = new GUITexture2DAsset();
                if (snapshotMetadata.Screenshot != null)
                    m_GuiData.GuiTexture.SetTexture(snapshotMetadata.Screenshot, GUITexture2DAsset.SourceType.Snapshot, 0);

                RefreshScreenshot();
            }
        }

        bool TryGetRuntimePlatform(string platformName, out RuntimePlatform runtimePlatform)
        {
            bool success = (!string.IsNullOrEmpty(platformName)) && Enum.IsDefined(typeof(RuntimePlatform), platformName);
            if (success)
                runtimePlatform = (RuntimePlatform)Enum.Parse(typeof(RuntimePlatform), platformName);
            else
                runtimePlatform = default(RuntimePlatform);
            return success;
        }

        public FileReader LoadSnapshot()
        {
            var reader = new FileReader();
            ReadError err = reader.Open(FileInfo.FullName);
            //Todo: print error message handle

            return reader;
        }

        internal void RefreshScreenshot()
        {
            string possibleSSPath = Path.ChangeExtension(FileInfo.FullName, ".png");
            var ssInfo = new FileInfo(possibleSSPath);
            var texAsset = m_GuiData.GuiTexture;
            if (!ssInfo.Exists && texAsset.Source == GUITexture2DAsset.SourceType.Image)
                texAsset.SetTexture(null, GUITexture2DAsset.SourceType.None, 0);

            if (ssInfo.Exists && texAsset.Source != GUITexture2DAsset.SourceType.Snapshot
                && (ssInfo.LastWriteTime.Ticks != m_GuiData.GuiTexture.TimeStampTicks || texAsset.Texture == null))
            {
                s_PendingTextureLoads.Enqueue(new ScreenshotLoadWork
                {
                    Path = possibleSSPath,
                    TextureAsset = texAsset,
                    SnapshotGuiInfo = m_GuiData,
                    Name = ssInfo.Name,
                    Ticks = ssInfo.LastWriteTime.Ticks,
                });

                if (s_ScreenshotLoader == null)
                {
                    s_ScreenshotLoader = EditorCoroutineUtility.StartCoroutine(LoadeScreenshotsTimeSliced(), this);
                }
            }

            //HACK: call this bit here to make sure we can update our screen shots, as it seems that changing the editor scene will get the current snapshot imagines collected
            // as they are not unity scene root objects
            if (m_GuiData.VisualElement != null &&  m_GuiData.VisualElement.screenshot != null)
                m_GuiData.VisualElement.screenshot.image = m_GuiData.GuiTexture.Texture;
        }

        struct ScreenshotLoadWork
        {
            public string Path;
            public string Name;
            public long Ticks;
            public GUITexture2DAsset TextureAsset;
            public SnapshotFileGUIData SnapshotGuiInfo;
        }

        static Queue<ScreenshotLoadWork> s_PendingTextureLoads = new Queue<ScreenshotLoadWork>();

        static EditorCoroutine s_ScreenshotLoader;

        static IEnumerator LoadeScreenshotsTimeSliced()
        {
            while (s_PendingTextureLoads.Count > 0)
            {
                yield return null;
                var work = s_PendingTextureLoads.Dequeue();
                var tex = new Texture2D(1, 1, TextureFormat.RGB24, false);
                var data = File.ReadAllBytes(work.Path);
                tex.LoadImage(data, false);
                // TODO: here or better still on receiving the screenshot, make sure the dimensions aren't total overkill and if they are, downscale, and save the smaller resolution
                if (tex.width % 4 == 0 && tex.height % 4 == 0)
                    tex.Compress(false);
                tex.Apply(false, true);
                tex.name = work.Name;
                work.TextureAsset.SetTexture(tex, GUITexture2DAsset.SourceType.Image, work.Ticks);

                if (work.SnapshotGuiInfo != null && work.SnapshotGuiInfo.VisualElement.screenshot != null)
                    work.SnapshotGuiInfo.VisualElement.screenshot.image = tex;
            }
            s_ScreenshotLoader = null;
        }

        public void Dispose()
        {
            m_GuiData.GuiTexture.SetTexture(null, GUITexture2DAsset.SourceType.None, 0);
        }

        public int CompareTo(SnapshotFileData other)
        {
            return m_GuiData.UtcDateTime.Ticks.CompareTo(other.m_GuiData.UtcDateTime.Ticks);
        }

        public static bool operator==(SnapshotFileData lhs, SnapshotFileData rhs)
        {
            if (ReferenceEquals(lhs, rhs))
                return true;

            if (ReferenceEquals(lhs, null) || ReferenceEquals(rhs, null))
                return false;

            return lhs.FileInfo.FullName.Equals(rhs.FileInfo.FullName);
        }

        public static bool operator!=(SnapshotFileData lhs, SnapshotFileData rhs)
        {
            return !(lhs == rhs);
        }

        public override bool Equals(object obj)
        {
            return this == obj as SnapshotFileData;
        }
    }
}

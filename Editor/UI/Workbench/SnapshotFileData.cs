using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
#if UNITY_2019_1_OR_NEWER
using UnityEngine.UIElements;
#else
using UnityEngine.Experimental.UIElements;
#endif
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
        internal GUIContent name;
        internal GUIContent metaInfo;
        internal GUIContent date;
        internal GUIContent platform;
        internal RuntimePlatform runtimePlatform = (RuntimePlatform)(-1);
        internal GUITexture2DAsset guiTexture;
        internal DynamicVisualElements dynamicVisualElements;

        internal struct DynamicVisualElements
        {
            internal VisualElement snapshotListItem;
            internal VisualElement optionDropdownButton;
            internal Button openButton;
            internal Button closeButton;
            internal Label snapshotNameLabel;
            internal Label snapshotDateLabel;
            internal TextField snapshotRenameField;
            internal Image screenshot;
        }

        public enum State
        {
            Closed,
            Open,
            InView,
        }

        const string k_OpenClassName = "snapshotIsOpen";
        const string k_InViewClassName = "snapshotIsInView";
        const string k_HiddenFromLayout = "hiddenFromLayout";
        const string k_NotHiddenFromLayout = "notHiddenFromLayout";

        public State CurrentState
        {
            get
            {
                return m_CurrentState;
            }
            set
            {
                if (value != m_CurrentState)
                {
                    switch (value)
                    {
                        case State.Closed:
                            if (m_CurrentState == State.InView)
                            {
                                dynamicVisualElements.snapshotNameLabel.RemoveFromClassList(k_InViewClassName);
                                dynamicVisualElements.snapshotDateLabel.RemoveFromClassList(k_InViewClassName);
                            }
                            else if (m_CurrentState == State.Open)
                            {
                                dynamicVisualElements.snapshotNameLabel.RemoveFromClassList(k_OpenClassName);
                                dynamicVisualElements.snapshotDateLabel.RemoveFromClassList(k_OpenClassName);
                            }
                            break;
                        case State.Open:
                            if (m_CurrentState == State.InView)
                            {
                                dynamicVisualElements.snapshotNameLabel.RemoveFromClassList(k_InViewClassName);
                                dynamicVisualElements.snapshotDateLabel.RemoveFromClassList(k_InViewClassName);
                            }
                            dynamicVisualElements.snapshotNameLabel.AddToClassList(k_OpenClassName);
                            dynamicVisualElements.snapshotDateLabel.AddToClassList(k_OpenClassName);
                            break;
                        case State.InView:
                            if (m_CurrentState == State.Open)
                            {
                                dynamicVisualElements.snapshotNameLabel.RemoveFromClassList(k_OpenClassName);
                                dynamicVisualElements.snapshotDateLabel.RemoveFromClassList(k_OpenClassName);
                            }
                            dynamicVisualElements.snapshotNameLabel.AddToClassList(k_InViewClassName);
                            dynamicVisualElements.snapshotDateLabel.AddToClassList(k_InViewClassName);
                            break;
                        default:
                            break;
                    }
                    m_CurrentState = value;
                }
            }
        }
        State m_CurrentState = State.Closed;

        public bool RenamingFieldVisible
        {
            get
            {
                return m_RenamingFieldVisible;
            }
            set
            {
                if (value != m_RenamingFieldVisible)
                {
                    dynamicVisualElements.snapshotRenameField.visible = value;
                    dynamicVisualElements.snapshotRenameField.AddToClassList(value ? k_NotHiddenFromLayout : k_HiddenFromLayout);
                    dynamicVisualElements.snapshotRenameField.RemoveFromClassList(!value ? k_NotHiddenFromLayout : k_HiddenFromLayout);
                    dynamicVisualElements.snapshotNameLabel.visible = !value;
                    dynamicVisualElements.snapshotNameLabel.AddToClassList(!value ? k_NotHiddenFromLayout : k_HiddenFromLayout);
                    dynamicVisualElements.snapshotNameLabel.RemoveFromClassList(value ? k_NotHiddenFromLayout : k_HiddenFromLayout);
                    // no opening or option meddling while renaming!
                    dynamicVisualElements.openButton.SetEnabled(!value);
                    dynamicVisualElements.optionDropdownButton.SetEnabled(!value);
                    m_RenamingFieldVisible = value;
                    dynamicVisualElements.snapshotRenameField.SetValueWithoutNotify(dynamicVisualElements.snapshotNameLabel.text);
                    if (value)
                    {
#if UNITY_2019_1_OR_NEWER
                        EditorApplication.delayCall += () => { dynamicVisualElements.snapshotRenameField.Q("unity-text-input").Focus(); };
#else
                        dynamicVisualElements.snapshotRenameField.Focus();
#endif
                    }
                }
            }
        }
        bool m_RenamingFieldVisible;
        private DateTime recordDate;

        public SnapshotFileGUIData(DateTime recordDate)
        {
            UtcDateTime = recordDate;
        }

        public readonly DateTime UtcDateTime;
        public GUIContent Name { get { return name; } }
        public GUIContent MetaContent { get { return metaInfo; } }
        public GUIContent MetaPlatform { get { return platform; } }
        public GUIContent SnapshotDate { get { return date; } }
        public Texture MetaScreenshot { get { return guiTexture.Texture; } }
    }

    //Add GetHashCode() override if we ever want to hash these
#pragma warning disable CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
    internal class SnapshotFileData : IDisposable, IComparable<SnapshotFileData>
#pragma warning restore CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
#pragma warning restore CS0659 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
    {
        public FileInfo FileInfo;
        SnapshotFileGUIData m_GuiData;
        long recordDateTicks;

        public SnapshotFileGUIData GuiData { get { return m_GuiData; } }

        public SnapshotFileData(FileInfo info)
        {
            FileInfo = info;
            using (var snapshot = LoadSnapshot())
            {
                MetaData snapshotMetadata = snapshot.metadata;
                var recordDate = snapshot.recordDate;
                recordDateTicks = recordDate.Ticks;

                m_GuiData = new SnapshotFileGUIData(recordDate);

                m_GuiData.name = new GUIContent(Path.GetFileNameWithoutExtension(FileInfo.Name));
                m_GuiData.metaInfo = new GUIContent(snapshotMetadata.content);
                m_GuiData.platform = new GUIContent(snapshotMetadata.platform);

                RuntimePlatform runtimePlatform;
                if (TryGetRuntimePlatform(snapshotMetadata.platform, out runtimePlatform))
                    m_GuiData.runtimePlatform = runtimePlatform;

                m_GuiData.date = new GUIContent(m_GuiData.UtcDateTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));

                m_GuiData.guiTexture = new GUITexture2DAsset();
                if (snapshotMetadata.screenshot != null)
                    m_GuiData.guiTexture.SetTexture(snapshotMetadata.screenshot, GUITexture2DAsset.SourceType.Snapshot, 0);

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

        public QueriedMemorySnapshot LoadSnapshot()
        {
            return QueriedMemorySnapshot.Load(FileInfo.FullName);
        }

        internal void RefreshScreenshot()
        {
            string possibleSSPath = Path.ChangeExtension(FileInfo.FullName, ".png");
            var ssInfo = new FileInfo(possibleSSPath);
            var texAsset = m_GuiData.guiTexture;
            if (!ssInfo.Exists && texAsset.Source == GUITexture2DAsset.SourceType.Image)
                texAsset.SetTexture(null, GUITexture2DAsset.SourceType.None, 0);

            if (ssInfo.Exists && texAsset.Source != GUITexture2DAsset.SourceType.Snapshot
                && (ssInfo.LastWriteTime.Ticks != m_GuiData.guiTexture.TimeStampTicks || texAsset.Texture == null))
            {
                var texData = File.ReadAllBytes(possibleSSPath);
                var tex = new Texture2D(1, 1);
                tex.LoadImage(texData);
                tex.Apply(false, true);
                tex.name = ssInfo.Name;
                texAsset.SetTexture(tex, GUITexture2DAsset.SourceType.Image, ssInfo.LastWriteTime.Ticks);
            }

            //HACK: call this bit here to make sure we can update our screen shots, as it seems that changing the editor scene will get the current snapshot imagines collected
            // as they are not unity scene root objects
            if (m_GuiData.dynamicVisualElements.screenshot != null)
                m_GuiData.dynamicVisualElements.screenshot.image = m_GuiData.guiTexture.Texture;
        }

        public void Dispose()
        {
            m_GuiData.guiTexture.SetTexture(null, GUITexture2DAsset.SourceType.None, 0);
        }

        public int CompareTo(SnapshotFileData other)
        {
            return recordDateTicks.CompareTo(other.recordDateTicks);
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

#if UNITY_2021_2_OR_NEWER
using System.Text;
using Unity.MemoryProfiler.Editor.UI;
using Unity.Profiling.Editor;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine.Networking.PlayerConnection;
using UnityEditor.Networking.PlayerConnection;
using UnityEngine.UIElements;
using PackagedMemoryUsageBreakdown = Unity.MemoryProfiler.Editor.UI.MemoryUsageBreakdown;
using Unity.Profiling;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace Unity.MemoryProfiler.Editor.MemoryProfilerModule
{
    internal class MemoryProfilerModuleViewController : ProfilerModuleViewController
    {
        static class ResourcePath
        {
            public const string MemoryModuleUxmlPath = UIContentData.ResourcePaths.UxmlFilesPath + "MemoryProfilerModule/MemoryModule.uxml";
        }
        static class Content
        {
            public static readonly string NoFrameDataAvailable = L10n.Tr("No frame data available. Select a frame from the charts above to see its details here.");
            public static readonly string Textures = L10n.Tr("Textures");
            public static readonly string Meshes = L10n.Tr("Meshes");
            public static readonly string Materials = L10n.Tr("Materials");
            public static readonly string AnimationClips = L10n.Tr("Animation Clips");
            public static readonly string Assets = L10n.Tr("Assets");
            public static readonly string GameObjects = L10n.Tr("Game Objects");
            public static readonly string SceneObjects = L10n.Tr("Scene Objects");
            public static readonly string GCAlloc = L10n.Tr("GC allocated in frame");
            public static readonly string OpenMemoryProfiler = L10n.Tr("Open Memory Profiler");
            public const string SystemUsedMemoryCounterName = "System Used Memory";
        }

        struct ObjectTableRow
        {
            public Label Count;
            public Label Size;
        }

        MemoryProfilerModuleOverride m_OverrideMemoryProfilerModule;
        
        // all UI Element and similar references should be grouped into this class.
        // Since the things this reference get recreated every time the module is selected, these references shouldn't linger beyond the Dispose()
        UIState m_UIState = null;
        class UIState
        {
            public VisualElement ViewArea;
            public VisualElement NoDataView;
            public VisualElement SimpleView;
            public VisualElement DetailedView;
            public UnityEngine.UIElements.Button DetailedMenu;
            public Label DetailedMenuLabel;
            public UnityEngine.UIElements.Button InstallPackageButton;
            public VisualElement EditorWarningLabel;
            public PackagedMemoryUsageBreakdown TopLevelBreakdown;
            public PackagedMemoryUsageBreakdown Breakdown;
            // if no memory counter data is available (i.e. the recording is from a pre 2020.2 Unity version) this whole section can't be populated with info
            public VisualElement CounterBasedUI;
            public TextField Text;

            public ObjectTableRow TexturesRow;
            public ObjectTableRow MeshesRow;
            public ObjectTableRow MaterialsRow;
            public ObjectTableRow AnimationClipsRow;
            public ObjectTableRow AssetsRow;
            public ObjectTableRow GameObjectsRow;
            public ObjectTableRow SceneObjectsRow;
            public ObjectTableRow GCAllocRow;
        }

        List<ulong[]> m_Used;
        List<ulong[]> m_Reserved;

        ulong[] m_TotalUsed;
        ulong[] m_TotalReserved;
        StringBuilder m_SimplePaneStringBuilder;

        bool m_AddedSimpleDataView = false;


        ulong m_MaxSystemUsedMemory = 0;

        IConnectionState m_ConnectionState;

        public MemoryProfilerModuleViewController(ProfilerWindow profilerWindow, MemoryProfilerModuleOverride overrideModule) : base(profilerWindow)
        {
            profilerWindow.SelectedFrameIndexChanged += UpdateContent;
            m_OverrideMemoryProfilerModule = overrideModule;

            // MemoryUsageBreakdown APIs currently expect to work with 2 sets of data for snapshots diffing
            const int k_AmountOfTotalNumbersExpectedByMemoryUsageBreakdown = 2;
            // And arrays with 3 sets of each detailed number where the 3rd one is the diff value, calculated which will be calculated by MemoryUsageBreakdown code and stored in that array.
            const int k_AmountOfDetailedNumbersExpectedByMemoryUsageBreakdown = 3;
            // Similarly, we don't need to calculate the Untracked value.
            // That is being done by the MemoryUsageBreakdown APIs based on whether or not there is a known total,
            // if Untracked should be shown, and by subtracting what is accounted for in the details from the provided total.
            // Therefore, we only need to provide values for the 6 tracked categories we want to show.
            const int k_AmountOfMemoryCategoriesExclusingUntracked = 6;

            m_Used = new List<ulong[]>(k_AmountOfMemoryCategoriesExclusingUntracked);
            m_Reserved = new List<ulong[]>(k_AmountOfMemoryCategoriesExclusingUntracked);

            m_TotalUsed = new ulong[k_AmountOfTotalNumbersExpectedByMemoryUsageBreakdown];
            m_TotalReserved = new ulong[k_AmountOfTotalNumbersExpectedByMemoryUsageBreakdown];
            m_SimplePaneStringBuilder = new StringBuilder(1024);

            for (int i = 0; i < k_AmountOfMemoryCategoriesExclusingUntracked; i++)
            {
                m_Used.Add(new ulong[k_AmountOfDetailedNumbersExpectedByMemoryUsageBreakdown]);
                m_Reserved.Add(new ulong[k_AmountOfDetailedNumbersExpectedByMemoryUsageBreakdown]);
            }
        }

        int[] oneFrameAvailabilityBuffer = new int[1];

        bool CheckMemoryStatsAvailablity(long frameIndex)
        {
            var dataNotAvailable = frameIndex < 0 || frameIndex<ProfilerWindow.firstAvailableFrameIndex || frameIndex> ProfilerWindow.lastAvailableFrameIndex;
            if (!dataNotAvailable)
            {
                ProfilerDriver.GetStatisticsAvailable(UnityEngine.Profiling.ProfilerArea.Memory, (int)frameIndex, oneFrameAvailabilityBuffer);
                if (oneFrameAvailabilityBuffer[0] == 0)
                    dataNotAvailable = true;
            }
            return !dataNotAvailable;
        }

        void ViewChanged(ProfilerMemoryView view)
        {
            m_UIState.ViewArea.Clear();
            m_OverrideMemoryProfilerModule.ShowDetailedMemoryPane = view;
            if (view == ProfilerMemoryView.Simple)
            {
                var frameIndex = ProfilerWindow.selectedFrameIndex;
                var dataAvailable = CheckMemoryStatsAvailablity(frameIndex);
                m_UIState.DetailedMenuLabel.text = "Simple";
                
                if (dataAvailable)
                {
                    m_UIState.ViewArea.Add(m_UIState.SimpleView);
                    m_AddedSimpleDataView = true;
                    UpdateContent(frameIndex);
                }
                else
                {
                    m_UIState.ViewArea.Add(m_UIState.NoDataView);
                    m_AddedSimpleDataView = false;
                }
            }
            else
            {
                m_UIState.DetailedMenuLabel.text = "Detailed";
                // Detailed View doesn't differentiate between there being frame data or not because
                // 1. Clear doesn't clear out old snapshots so there totally could be data here
                // 2. Take Snapshot also doesn't require there to be any frame data
                // this special case will disappear together with the detailed view eventually
                m_UIState.ViewArea.Add(m_UIState.DetailedView);
                m_AddedSimpleDataView = false;
            }
        }

        static ProfilerMarker s_UpdateMaxSystemUsedMemoryProfilerMarker = new ProfilerMarker("MemoryProfilerModule.UpdateMaxSystemUsedMemory");
        float[] m_CachedArray;
        void UpdateMaxSystemUsedMemory(long firstFrameToCheck, long lastFrameToCheck)
        {
            s_UpdateMaxSystemUsedMemoryProfilerMarker.Begin();
            var frameCountToCheck = lastFrameToCheck - firstFrameToCheck;
            m_MaxSystemUsedMemory = 0;
            var max = m_MaxSystemUsedMemory;
            // try to reuse the array if possible
            if (m_CachedArray == null || m_CachedArray.Length != frameCountToCheck)
                m_CachedArray = new float[frameCountToCheck];
            float maxValueInRange;
            ProfilerDriver.GetCounterValuesBatch(UnityEngine.Profiling.ProfilerArea.Memory, Content.SystemUsedMemoryCounterName, (int)firstFrameToCheck, 1, m_CachedArray, out maxValueInRange);
            if (maxValueInRange > max)
                max = (ulong)maxValueInRange;
            m_MaxSystemUsedMemory = max;
            s_UpdateMaxSystemUsedMemoryProfilerMarker.End();
        }

        void UpdateContent(long frame)
        {
            if (m_OverrideMemoryProfilerModule.ShowDetailedMemoryPane != ProfilerMemoryView.Simple)
                return;
            var dataAvailable = CheckMemoryStatsAvailablity(frame);

            if (m_AddedSimpleDataView != dataAvailable)
            {
                // refresh the view structure
                ViewChanged(ProfilerMemoryView.Simple);
                return;
            }
            if (!dataAvailable)
                return;
            if (m_UIState != null)
            {
                using (var data = ProfilerDriver.GetRawFrameDataView((int)frame, 0))
                {
                    m_SimplePaneStringBuilder.Clear();
                    if (data.valid && data.GetMarkerId("Total Reserved Memory") != FrameDataView.invalidMarkerId)
                    {
                        var systemUsedMemoryId = data.GetMarkerId(Content.SystemUsedMemoryCounterName);

                        var systemUsedMemory = (ulong)data.GetCounterValueAsLong(systemUsedMemoryId);

                        bool[] totalIsKnown = new bool[m_TotalUsed.Length];
                        totalIsKnown[0] = (systemUsedMemoryId != FrameDataView.invalidMarkerId && systemUsedMemory > 0);

                        var maxSystemUsedMemory = m_MaxSystemUsedMemory = systemUsedMemory;
                        if (!m_OverrideMemoryProfilerModule.Normalized)
                        {
                            UpdateMaxSystemUsedMemory(ProfilerWindow.firstAvailableFrameIndex, ProfilerWindow.lastAvailableFrameIndex);
                            maxSystemUsedMemory = m_MaxSystemUsedMemory;
                        }

                        var totalUsedId = data.GetMarkerId("Total Used Memory");
                        var totalUsed = (ulong)data.GetCounterValueAsLong(totalUsedId);
                        var totalReservedId = data.GetMarkerId("Total Reserved Memory");
                        var totalReserved = (ulong)data.GetCounterValueAsLong(totalReservedId);

                        m_TotalUsed[0] = totalUsed;
                        m_TotalReserved[0] = totalReserved;

                        if (!totalIsKnown[0])
                            systemUsedMemory = totalReserved;

                        m_UIState.TopLevelBreakdown.SetValues(new ulong[] {systemUsedMemory, systemUsedMemory}, new List<ulong[]> {m_TotalReserved}, new List<ulong[]> {m_TotalUsed}, m_OverrideMemoryProfilerModule.Normalized, new ulong[] {maxSystemUsedMemory, maxSystemUsedMemory}, totalIsKnown);

                        m_Used[4][0] = totalUsed;
                        m_Reserved[4][0] = totalReserved;

                        var gfxReservedId = data.GetMarkerId("Gfx Reserved Memory");
                        m_Reserved[1][0] = m_Used[1][0] = (ulong)data.GetCounterValueAsLong(gfxReservedId);

                        var managedUsedId = data.GetMarkerId("GC Used Memory");
                        m_Used[0][0] = (ulong)data.GetCounterValueAsLong(managedUsedId);
                        var managedReservedId = data.GetMarkerId("GC Reserved Memory");
                        m_Reserved[0][0] = (ulong)data.GetCounterValueAsLong(managedReservedId);

                        var audioReservedId = data.GetMarkerId("Audio Used Memory");
                        m_Reserved[2][0] = m_Used[2][0] = (ulong)data.GetCounterValueAsLong(audioReservedId);

                        var videoReservedId = data.GetMarkerId("Video Used Memory");
                        m_Reserved[3][0] = m_Used[3][0] = (ulong)data.GetCounterValueAsLong(videoReservedId);


                        var profilerUsedId = data.GetMarkerId("Profiler Used Memory");
                        m_Used[5][0] = (ulong)data.GetCounterValueAsLong(profilerUsedId);
                        var profilerReservedId = data.GetMarkerId("Profiler Reserved Memory");
                        m_Reserved[5][0] = (ulong)data.GetCounterValueAsLong(profilerReservedId);

                        m_Used[4][0] -= Math.Min(m_Used[0][0] + m_Used[1][0] + m_Used[2][0] + m_Used[3][0] + m_Used[5][0], m_Used[4][0]);
                        m_Reserved[4][0] -= Math.Min(m_Reserved[0][0] + m_Reserved[1][0] + m_Reserved[2][0] + m_Reserved[3][0] + m_Reserved[5][0], m_Reserved[4][0]);
                        m_UIState.Breakdown.SetValues(new ulong[] {systemUsedMemory, systemUsedMemory}, m_Reserved, m_Used, m_OverrideMemoryProfilerModule.Normalized, new ulong[] {maxSystemUsedMemory, maxSystemUsedMemory }, totalIsKnown, nameOfKnownTotal: Content.SystemUsedMemoryCounterName);

                        UpdateObjectRow(data, ref m_UIState.TexturesRow, "Texture Count", "Texture Memory");
                        UpdateObjectRow(data, ref m_UIState.MeshesRow, "Mesh Count", "Mesh Memory");
                        UpdateObjectRow(data, ref m_UIState.MaterialsRow, "Material Count", "Material Memory");
                        UpdateObjectRow(data, ref m_UIState.AnimationClipsRow, "AnimationClip Count", "AnimationClip Memory");
                        UpdateObjectRow(data, ref m_UIState.AssetsRow, "Asset Count");
                        UpdateObjectRow(data, ref m_UIState.GameObjectsRow, "Game Object Count");
                        UpdateObjectRow(data, ref m_UIState.SceneObjectsRow, "Scene Object Count");

                        UpdateObjectRow(data, ref m_UIState.GCAllocRow, "GC Allocation In Frame Count", "GC Allocated In Frame");

                        if (!m_UIState.CounterBasedUI.visible)
                            UIElementsHelper.SetVisibility(m_UIState.CounterBasedUI, true);
                    }
                    else
                    {
                        if (m_UIState.CounterBasedUI.visible)
                            UIElementsHelper.SetVisibility(m_UIState.CounterBasedUI, false);
                        m_SimplePaneStringBuilder.Append($"Please disable the Memory Profiler UI override in Preferences to view legacy data ('{MemoryProfilerSettings.MemoryProfilerPackageOverridesMemoryModuleUI}').");
                    }

                    if (m_SimplePaneStringBuilder.Length > 0)
                    {
                        UIElementsHelper.SetVisibility(m_UIState.Text, true);
                        m_UIState.Text.value = m_SimplePaneStringBuilder.ToString();
                    }
                    else
                    {
                        UIElementsHelper.SetVisibility(m_UIState.Text, false);
                    }
                }
            }
        }

        void UpdateObjectRow(RawFrameDataView data, ref ObjectTableRow row, string countMarkerName, string sizeMarkerName = null)
        {
            row.Count.text = data.GetCounterValueAsLong(data.GetMarkerId(countMarkerName)).ToString();
            if (!string.IsNullOrEmpty(sizeMarkerName))
                row.Size.text = EditorUtility.FormatBytes(data.GetCounterValueAsLong(data.GetMarkerId(sizeMarkerName)));
        }

        void ConnectionChanged(string playerName)
        {
            if (m_ConnectionState != null)
                UIElementsHelper.SetVisibility(m_UIState.EditorWarningLabel, m_ConnectionState.connectionName == "Editor");
        }

        protected override VisualElement CreateView()
        {
            VisualTreeAsset memoryModuleViewTree = EditorGUIUtility.Load(ResourcePath.MemoryModuleUxmlPath) as VisualTreeAsset;

            var root = memoryModuleViewTree.CloneTree();

            m_UIState = new UIState();

            var toolbar = root.Q("memory-module__toolbar");
            m_UIState.DetailedMenu = toolbar.Q<UnityEngine.UIElements.Button>("memory-module__toolbar__detail-view-menu");
            m_UIState.DetailedMenuLabel = m_UIState.DetailedMenu.Q<Label>("memory-module__toolbar__detail-view-menu__label");
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Simple"), false, () => ViewChanged(ProfilerMemoryView.Simple));
            menu.AddItem(new GUIContent("Detailed"), false, () => ViewChanged(ProfilerMemoryView.Detailed));
            m_UIState.DetailedMenu.clicked += () =>
            {
                menu.DropDown(UIElementsHelper.GetRect(m_UIState.DetailedMenu));
            };

            var installPackageButton = toolbar.Q<UnityEngine.UIElements.Button>("memory-module__toolbar__install-package-button");

            // in the main code base, this button offers to install the memory profiler package, here it is swapped to be one that opens it.
            installPackageButton.text = Content.OpenMemoryProfiler;
            installPackageButton.clicked += () => EditorWindow.GetWindow<MemoryProfilerWindow>();

            m_UIState.EditorWarningLabel = toolbar.Q("memory-module__toolbar__editor-warning");

            m_ConnectionState = PlayerConnectionGUIUtility.GetConnectionState(ProfilerWindow, ConnectionChanged);
            UIElementsHelper.SetVisibility(m_UIState.EditorWarningLabel, m_ConnectionState.connectionName == "Editor");

            m_UIState.ViewArea = root.Q("memory-module__view-area");

            m_UIState.SimpleView = m_UIState.ViewArea.Q("memory-module__simple-area");
            m_UIState.CounterBasedUI = m_UIState.SimpleView.Q("memory-module__simple-area__counter-based-ui");

            var normalizedToggle = m_UIState.CounterBasedUI.Q<Toggle>("memory-module__simple-area__breakdown__normalized-toggle");
            normalizedToggle.value = m_OverrideMemoryProfilerModule.Normalized;
            normalizedToggle.RegisterValueChangedCallback((evt) =>
            {
                m_OverrideMemoryProfilerModule.Normalized = evt.newValue;
                UpdateContent(ProfilerWindow.selectedFrameIndex);
            });

            m_UIState.TopLevelBreakdown = m_UIState.CounterBasedUI.Q<PackagedMemoryUsageBreakdown>("memory-usage-breakdown__top-level");
            m_UIState.TopLevelBreakdown.Setup();
            m_UIState.TopLevelBreakdown.DenormalizeFrameBased = true;
            m_UIState.TopLevelBreakdown.SetBAndDiffVisibility(false);
            m_UIState.Breakdown = m_UIState.CounterBasedUI.Q<PackagedMemoryUsageBreakdown>("memory-usage-breakdown");
            m_UIState.Breakdown.Setup();
            m_UIState.Breakdown.SetBAndDiffVisibility(false);

            var m_ObjectStatsTable = m_UIState.CounterBasedUI.Q("memory-usage-breakdown__object-stats_list");

            SetupObjectTableRow(m_ObjectStatsTable.Q("memory-usage-breakdown__object-stats__textures"), ref m_UIState.TexturesRow, Content.Textures);
            SetupObjectTableRow(m_ObjectStatsTable.Q("memory-usage-breakdown__object-stats__meshes"), ref m_UIState.MeshesRow, Content.Meshes);
            SetupObjectTableRow(m_ObjectStatsTable.Q("memory-usage-breakdown__object-stats__materials"), ref m_UIState.MaterialsRow, Content.Materials);
            SetupObjectTableRow(m_ObjectStatsTable.Q("memory-usage-breakdown__object-stats__animation-clips"), ref m_UIState.AnimationClipsRow, Content.AnimationClips);
            SetupObjectTableRow(m_ObjectStatsTable.Q("memory-usage-breakdown__object-stats__assets"), ref m_UIState.AssetsRow, Content.Assets, true);
            SetupObjectTableRow(m_ObjectStatsTable.Q("memory-usage-breakdown__object-stats__game-objects"), ref m_UIState.GameObjectsRow, Content.GameObjects, true);
            SetupObjectTableRow(m_ObjectStatsTable.Q("memory-usage-breakdown__object-stats__scene-objects"), ref m_UIState.SceneObjectsRow, Content.SceneObjects, true);

            var m_GCAllocExtraRow = m_UIState.CounterBasedUI.Q<VisualElement>("memory-usage-breakdown__object-stats__gc");
            SetupObjectTableRow(m_GCAllocExtraRow, ref m_UIState.GCAllocRow, Content.GCAlloc);

            m_UIState.Text = m_UIState.SimpleView.Q<TextField>("memory-module__simple-area__label");

            var detailedView = m_UIState.ViewArea.Q<VisualElement>("memory-module__detailed-snapshot-area");
            m_UIState.DetailedView = detailedView;

            m_UIState.NoDataView = m_UIState.ViewArea.Q("memory-module__no-frame-data__area");
            m_UIState.NoDataView.Q<Label>("memory-module__no-frame-data__label").text = Content.NoFrameDataAvailable;

            ViewChanged(m_OverrideMemoryProfilerModule.ShowDetailedMemoryPane);
            return root;
        }

        void SetupObjectTableRow(VisualElement rowRoot, ref ObjectTableRow row, string name, bool sizesUnknown = false)
        {
            rowRoot.Q<Label>("memory-usage-breakdown__object-table__name").text = name;
            row.Count = rowRoot.Q<Label>("memory-usage-breakdown__object-table__count-column");
            row.Count.text = "0";
            row.Size = rowRoot.Q<Label>("memory-usage-breakdown__object-table__size-column");
            row.Size.text = sizesUnknown ? "-" : "0";
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (ProfilerWindow != null)
                ProfilerWindow.SelectedFrameIndexChanged -= UpdateContent;

            if (m_ConnectionState != null)
                m_ConnectionState.Dispose();
            m_ConnectionState = null;

            m_UIState = null;
        }
    }
}
#endif

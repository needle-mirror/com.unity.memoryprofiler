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
using PackagedMemoryUsageBreakdown = Unity.MemoryProfiler.Editor.UI.SummaryViewController;
using Unity.Profiling;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace Unity.MemoryProfiler.Editor.MemoryProfilerModule
{
    internal class MemoryProfilerModuleViewController : ProfilerModuleViewController
    {
        const string k_UxmlAssetGuid = "48d21a1a975e6e44faaf6d938009e4f6";

        public static readonly string k_LabelNoFrameDataAvailable = L10n.Tr("No frame data available. Select a frame from the charts above to see its details here.");
        public static readonly string k_LabelOpenMemoryProfiler = L10n.Tr("Open Memory Profiler");

        MemoryProfilerModuleOverride m_OverrideMemoryProfilerModule;

        // Model
        IConnectionState m_ConnectionState;
        ResidentMemoryInEditorSummaryModelBuilder m_DeviceBreakdownModelBuilder;
        AllMemoryInEditorSummaryModelBuilder m_CommittedBreakdownModelBuilder;
        UnityObjectsMemoryInEditorSummaryModelBuilder m_UnityObjectsBreakdownModelBuilder;

        // View
        VisualElement m_Root;
        VisualElement m_NoDataView;
        VisualElement m_EditorWarningLabel;
        Button m_InstallPackageButton;
        Button m_ViewModeSelector;
        Label m_ViewModeLabel;

        VisualElement m_SimpleView;
        VisualElement m_DetailedView;
        VisualElement m_DeviceMemoryBreakdown;
        VisualElement m_CommittedMemoryBreakdown;
        VisualElement m_UnityObjectsBreakdown;

        bool m_DataAvailable;
        ProfilerMemoryView m_ViewMode;

        // -1 could be misinterpreted as current frame. Never set this to -1 though and only the actual frame index
        UniqueFrameId m_FrameIdOfLastBuildModel = new UniqueFrameId(-2, 0, -1f);
        /// <summary>
        /// We don't have public API access to the current session id, nor if the Profiler session was restarted within the session.
        /// using just the FrameIndex is therefore not enough to determine if two frames are the same or different.
        /// This is still not an _entrely_ guaranteed unique fingerprint, but way more accurate than just the FrameIndex.
        /// </summary>
        readonly struct UniqueFrameId : IEquatable<UniqueFrameId>
        {
            public readonly long FrameIndex;
            public readonly ulong FrameStartTimeNS;
            public readonly float FrameDurationMS;
            public UniqueFrameId(long frameIndex, ulong frameStartTimeNS, float frameDurationMS)
            {
                FrameIndex = frameIndex;
                FrameStartTimeNS = frameStartTimeNS;
                FrameDurationMS = frameDurationMS;
            }
            public readonly bool Equals(UniqueFrameId other)
            {
                return FrameIndex == other.FrameIndex && FrameStartTimeNS == other.FrameStartTimeNS && FrameDurationMS == other.FrameDurationMS;
            }
        }

        List<IMemorySummaryViewController> m_WidgetControllers;

        public MemoryProfilerModuleViewController(ProfilerWindow profilerWindow, MemoryProfilerModuleOverride overrideModule)
            : base(profilerWindow)
        {
            m_WidgetControllers = new List<IMemorySummaryViewController>();

            profilerWindow.SelectedFrameIndexChanged += UpdateWidgetsFrame;
            m_OverrideMemoryProfilerModule = overrideModule;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (ProfilerWindow != null)
                ProfilerWindow.SelectedFrameIndexChanged -= UpdateWidgetsFrame;

            m_ConnectionState?.Dispose();
            m_ConnectionState = null;
            if(m_WidgetControllers != null)
            {
                foreach (var controller in m_WidgetControllers)
                {
                    controller?.Dispose();
                }
                m_WidgetControllers = null;
            }
        }

        protected override VisualElement CreateView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");
            view.style.flexGrow = 1;

            GatherReferences(view);
            SetupView();

            return view;
        }

        void GatherReferences(VisualElement view)
        {
            var toolbar = view.Q("memory-profiler-module__toolbar");
            m_EditorWarningLabel = toolbar.Q("memory-profiler-module__toolbar__editor-warning");
            m_InstallPackageButton = toolbar.Q<Button>("memory-profiler-module__toolbar__install-package-button");

            m_ViewModeSelector = toolbar.Q<Button>("memory-profiler-module__toolbar__view-mode");
            m_ViewModeLabel = m_ViewModeSelector.Q<Label>("memory-profiler-module__toolbar__view-mode-label");

            m_NoDataView = view.Q("memory-profiler-module__no-frame-data");
            m_SimpleView = view.Q("memory-profiler-module__simple");
            m_DetailedView = view.Q("memory-profiler-module__detailed");

            m_DeviceMemoryBreakdown = view.Q<VisualElement>("memory-profiler-module__content__system");
            m_CommittedMemoryBreakdown = view.Q<VisualElement>("memory-profiler-module__content__total");
            m_UnityObjectsBreakdown = view.Q<VisualElement>("memory-profiler-module__content__unity-objects");
            m_Root = view;
        }

        void SetupView()
        {
            // Toolbar mode switch
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Simple"), false, () => UpdateViewState(ProfilerMemoryView.Simple, true));
            menu.AddItem(new GUIContent("Detailed"), false, () => UpdateViewState(ProfilerMemoryView.Detailed, true));
            m_ViewModeSelector.clicked += () =>
            {
                menu.DropDown(UIElementsHelper.GetRect(m_ViewModeSelector));
            };

            m_NoDataView.Q<Label>("memory-profiler-module__no-frame-data__label").text = k_LabelNoFrameDataAvailable;

            // In the main code base, this button offers to install the memory profiler package, here it is swapped to be one that opens it.
            m_InstallPackageButton.text = k_LabelOpenMemoryProfiler;
            m_InstallPackageButton.clicked += () => EditorWindow.GetWindow<MemoryProfilerWindow>();

            m_ConnectionState = PlayerConnectionGUIUtility.GetConnectionState(ProfilerWindow, (x) => RefreshEditorWarning());
            RefreshEditorWarning();

            UpdateViewState(ProfilerMemoryView.Simple, true);
            UpdateWidgetsFrame(ProfilerWindow.selectedFrameIndex);
        }

        void RefreshEditorWarning()
        {
            UIElementsHelper.SetVisibility(m_EditorWarningLabel, m_ConnectionState?.connectionName == "Editor");
        }

        void UpdateViewState(ProfilerMemoryView mode, bool force)
        {
            // Don't update if there is no state change
            var frame = ProfilerWindow.selectedFrameIndex;
            if(frame == -1)
                frame = ProfilerDriver.lastFrameIndex;

            bool isDataAvailable = CheckMemoryStatsAvailablity(frame);
            if (!force && (isDataAvailable == m_DataAvailable) && (mode == m_ViewMode))
                return;

            // Mode state
            m_ViewMode = mode;
            m_DataAvailable = isDataAvailable;
            m_OverrideMemoryProfilerModule.ShowDetailedMemoryPane = mode;

            // Update view pane visibility
            UIElementsHelper.SetVisibility(m_NoDataView, !isDataAvailable);
            UIElementsHelper.SetVisibility(m_SimpleView, isDataAvailable && (mode == ProfilerMemoryView.Simple));
            UIElementsHelper.SetVisibility(m_DetailedView, isDataAvailable && (mode == ProfilerMemoryView.Detailed));

            m_ViewModeLabel.text = mode == ProfilerMemoryView.Simple ? "Simple" : "Detailed";
        }

        void UpdateWidgetsFrame(long frame)
        {
            // -1 is the current/latest frame
            if (frame == -1)
                frame = ProfilerWindow.lastAvailableFrameIndex;

            if(Event.current?.type == EventType.Layout)
            {
                // don't change the layout during layout. Schedule the update for later
                m_Root.schedule.Execute(() => UpdateWidgetsFrame(frame));
                return;
            }

            if (CheckMemoryStatsAvailablity(frame))
            {
                using var m_FrameDataView = ProfilerDriver.GetRawFrameDataView((int)frame, 0);
                var frameId = new UniqueFrameId(frame, m_FrameDataView.frameStartTimeNs, m_FrameDataView.frameTimeMs);

                if (m_FrameIdOfLastBuildModel.Equals(frameId))
                   return;
                m_FrameIdOfLastBuildModel = frameId;
            }
            else
                m_FrameIdOfLastBuildModel = new UniqueFrameId(-2, 0, -1);

            UpdateViewState(m_ViewMode, false);

            if (!m_DataAvailable)
                return;

            if (m_CommittedBreakdownModelBuilder != null)
            {
                // Hidden until we get physical memory size into profiler stream
                UIElementsHelper.SetVisibility(m_DeviceMemoryBreakdown.parent, false);

                m_CommittedBreakdownModelBuilder.Frame = frame;
                m_UnityObjectsBreakdownModelBuilder.Frame = frame;
                foreach (var widget in m_WidgetControllers)
                {
                    widget.Update();
                }
            }
            else
            {
                //Commented out until we get physical memory size into profiler stream
                //m_DeviceMemoryBreakdown.Clear();
                //m_DeviceBreakdownModelBuilder = new DeviceMemoryInEditorWidgetModelBuilder() { Frame = frame };
                //AddController(m_DeviceMemoryBreakdown, new DeviceMemoryBreakdownViewController(m_DeviceBreakdownModelBuilder));
                UIElementsHelper.SetVisibility(m_DeviceMemoryBreakdown.parent, false);

                m_CommittedBreakdownModelBuilder = new AllMemoryInEditorSummaryModelBuilder() { Frame = -1 };
                AddController(m_CommittedMemoryBreakdown, new GenericMemorySummaryViewController(m_CommittedBreakdownModelBuilder, false)
                {
                    TotalLabelFormat = "Total allocated memory: {0}",
                    Selectable = false
                });
                m_UnityObjectsBreakdownModelBuilder = new UnityObjectsMemoryInEditorSummaryModelBuilder() { Frame = -1 };
                AddController(m_UnityObjectsBreakdown, new GenericMemorySummaryViewController(m_UnityObjectsBreakdownModelBuilder, false)
                {
                    TotalLabelFormat = null,
                    Selectable = false
                });

            }
        }

        void AddController<T>(VisualElement root, T controller) where T : ViewController, IMemorySummaryViewController
        {
            root.Clear();
            root.Add(controller.View);
            m_WidgetControllers.Add(controller);
        }

        bool CheckMemoryStatsAvailablity(long frameIndex)
        {
            if(frameIndex == -1)
            {
                // -1 means is the current frame, if there is one
                frameIndex = ProfilerWindow.lastAvailableFrameIndex;
            }
            if (frameIndex < 0)
                return false;
            if (frameIndex < ProfilerWindow.firstAvailableFrameIndex || frameIndex > ProfilerWindow.lastAvailableFrameIndex)
                return false;

            int[] tempBuffer = new int[1];
            ProfilerDriver.GetStatisticsAvailable(UnityEngine.Profiling.ProfilerArea.Memory, (int)frameIndex, tempBuffer);
            return tempBuffer[0] != 0;
        }
    }
}
#endif

using System;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class MemoryProfilerViewController : ViewController
    {
        const string k_UxmlAssetGuid = "3dd0da5afd7349e46be51745993b4f81";
        const string k_UssClass_Dark = "memory-profiler__dark";
        const string k_UssClass_Light = "memory-profiler__light";

        const string k_UxmlIdentifierCaptureToolbarViewContainer = "memory-profiler-view__toolbar__container";
        const string k_UxmlIdentifierLoadedSnapshotsViewContainer = "memory-profiler-view__open-snapshots-view__container";
        const string k_UxmlIdentifierSnapshotFilesListViewContainer = "memory-profiler-view__snapshots-list-view__container";
        const string k_UxmlIdentifierDetailsViewViewContainer = "memory-profiler-view__details-view__container";
        const string k_UxmlIdentifierAnalysisViewContainer = "memory-profiler-view__analysis-view__container";
        const string k_UxmlIdentifierSnapshotFilesListSplitter = "memory-profiler-view__snapshot-view__splitter";
        const string k_UxmlIdentifierDetailsSplitter = "memory-profiler-view__details-view__splitter";

        const string k_SnapshotListSplitViewFixedPaneSizePreferenceKey = "MemoryProfilerWindow.SnapshotListSplitView.FixedPaneSize";

        const string k_DetailsPaneSplitViewFixedPaneSizePreferenceKey = "MemoryProfilerWindow.DetailsPaneSplitView.FixedPaneSize";

        // State
        PlayerConnectionService m_PlayerConnectionService;
        SnapshotDataService m_SnapshotDataService;
        ScreenshotsManager m_ScreenshotsManager;

        // View
        VisualElement m_CaptureToolbarViewContainer;
        VisualElement m_AnalysisViewContainer;
        VisualElement m_LoadedSnapshotsViewContainer;
        VisualElement m_SnapshotFilesListViewContainer;
        VisualElement m_DetailsViewContainer;
        TwoPaneSplitView m_SnapshotFilesListSplitter;
        TwoPaneSplitView m_DetailsSplitter;

        CaptureToolbarViewController m_CaptureToolbarViewController;
        LoadedSnapshotsViewController m_LoadedSnapshotsViewController;
        SnapshotFilesListViewController m_SnapshotFilesListViewController;
        DetailsViewController m_DetailsViewController;
        ViewController m_AnalysisViewController;

        // Testing facility
        internal ViewController AnalysisViewController => m_AnalysisViewController;

        public MemoryProfilerViewController(PlayerConnectionService playerConnectionService, SnapshotDataService snapshotDataService)
        {
            m_PlayerConnectionService = playerConnectionService;
            m_SnapshotDataService = snapshotDataService;
            m_ScreenshotsManager = new ScreenshotsManager();
        }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");
            view.style.flexGrow = 1;

            var themeUssClass = (EditorGUIUtility.isProSkin) ? k_UssClass_Dark : k_UssClass_Light;
            view.AddToClassList(themeUssClass);

            GatherReferencesInView(view);

            return view;
        }

        protected override void ViewLoaded()
        {
            base.ViewLoaded();
            RefreshView();
        }

        void GatherReferencesInView(VisualElement view)
        {
            m_CaptureToolbarViewContainer = view.Q(k_UxmlIdentifierCaptureToolbarViewContainer);
            m_LoadedSnapshotsViewContainer = view.Q(k_UxmlIdentifierLoadedSnapshotsViewContainer);
            m_SnapshotFilesListViewContainer = view.Q(k_UxmlIdentifierSnapshotFilesListViewContainer);
            m_DetailsViewContainer = view.Q(k_UxmlIdentifierDetailsViewViewContainer);
            m_AnalysisViewContainer = view.Q(k_UxmlIdentifierAnalysisViewContainer);

            m_SnapshotFilesListSplitter = view.Q<TwoPaneSplitView>(k_UxmlIdentifierSnapshotFilesListSplitter);
            m_DetailsSplitter = view.Q<TwoPaneSplitView>(k_UxmlIdentifierDetailsSplitter);
        }

        void RefreshView()
        {
            m_CaptureToolbarViewController = new CaptureToolbarViewController(m_PlayerConnectionService, m_SnapshotDataService);
            m_CaptureToolbarViewContainer.Add(m_CaptureToolbarViewController.View);
            m_CaptureToolbarViewController.SnapshotsPanelToggle += ToggleSnapshotsPanel;
            m_CaptureToolbarViewController.DetailsPanelToggle += ToggleDetailsPanel;
            AddChild(m_CaptureToolbarViewController);

            m_DetailsViewController = new DetailsViewController();
            m_DetailsViewContainer.Add(m_DetailsViewController.View);
            AddChild(m_DetailsViewController);

            m_LoadedSnapshotsViewController = new LoadedSnapshotsViewController(m_SnapshotDataService, m_ScreenshotsManager);
            m_LoadedSnapshotsViewContainer.Add(m_LoadedSnapshotsViewController.View);
            AddChild(m_LoadedSnapshotsViewController);

            m_SnapshotFilesListViewController = new SnapshotFilesListViewController(m_SnapshotDataService, m_ScreenshotsManager);
            m_SnapshotFilesListViewContainer.Add(m_SnapshotFilesListViewController.View);
            AddChild(m_SnapshotFilesListViewController);

            RefreshAnalysisView();
            m_SnapshotDataService.LoadedSnapshotsChanged += RefreshAnalysisView;
            m_SnapshotDataService.CompareModeChanged += CompareViewChanged;
            m_SnapshotDataService.AboutToUnloadSnapshot += RefreshAnalysisView;

            // TwoPaneSplitView.viewDataKey is not currently supported so we need to manually persist its state.
            var snapshotListPaneSize = EditorPrefs.GetFloat(k_SnapshotListSplitViewFixedPaneSizePreferenceKey, 300);
            m_SnapshotFilesListSplitter.fixedPaneInitialDimension = snapshotListPaneSize;
            if (!CaptureToolbarViewController.SavedSnapshotListSplitViewToggleIsVisibleState)
                ToggleSnapshotsPanel(ChangeEvent<bool>.GetPooled(true, false));

            var detailsPaneSize = EditorPrefs.GetFloat(k_DetailsPaneSplitViewFixedPaneSizePreferenceKey, 300);
            m_DetailsSplitter.fixedPaneInitialDimension = detailsPaneSize;
            if (!CaptureToolbarViewController.SavedDetailsPaneSplitViewToggleIsVisibleState)
                ToggleDetailsPanel(ChangeEvent<bool>.GetPooled(true, false));

            // When we load a new screenshot, UI needs to be forced refreshed
            // to repaint with the newly loaded textures
            m_ScreenshotsManager.ScreenshotLoaded += ScreenshotsLoaded;

            // Register Analysis view controller as child controller, so that it'll get cleaned up with this one
            AddChild(m_AnalysisViewController);
        }

        void ScreenshotsLoaded(string _) // we don't care about the path of the loaded screenshot here. We just needed to know that we need to repaint
        {
            m_LoadedSnapshotsViewContainer.MarkDirtyRepaint();
            m_SnapshotFilesListViewContainer.MarkDirtyRepaint();
        }

        void RefreshAnalysisView()
        {
            // Reset state
            m_AnalysisViewController?.Dispose();
            m_AnalysisViewController = null;
            m_AnalysisViewContainer.Clear();

            // Create new analysis controller
            string viewKeyForAnalytics;
            if ((m_SnapshotDataService.Base != null) || (m_SnapshotDataService.Compared != null))
            {
                var baseSnapshot = m_SnapshotDataService.Base;
                var comparedSnapshot = m_SnapshotDataService.CompareMode ? m_SnapshotDataService.Compared : null;
                m_AnalysisViewController = new AnalysisTabBarController(m_DetailsViewController, baseSnapshot, comparedSnapshot);

                viewKeyForAnalytics = TextContent.SummaryViewName;
                if (comparedSnapshot != null)
                    viewKeyForAnalytics = TextContent.GetComparisonViewName(viewKeyForAnalytics);
            }
            else
            {
                var noDataViewController = new NoDataViewController();
                noDataViewController.TakeSnapshotSelected += m_PlayerConnectionService.TakeCapture;
                m_AnalysisViewController = noDataViewController;

                viewKeyForAnalytics = "No Data";
            }

            // Send default view updated event
            MemoryProfilerAnalytics.SendOpenViewEvent(viewKeyForAnalytics, true);

            m_AnalysisViewContainer.Add(m_AnalysisViewController.View);
        }

        void CompareViewChanged()
        {
            // Don't do anything if we're in single snapshot view
            // as nothing visibly changes
            if (m_SnapshotDataService.Compared == null)
                return;

            RefreshAnalysisView();
        }

        void ToggleSnapshotsPanel(ChangeEvent<bool> e)
        {
            if (e.newValue)
                m_SnapshotFilesListSplitter.UnCollapse();
            else
            {
                if (m_SnapshotFilesListSplitter.fixedPane is { resolvedStyle: not null } && !float.IsNaN(m_SnapshotFilesListSplitter.fixedPane.resolvedStyle.width))
                    EditorPrefs.SetFloat(k_SnapshotListSplitViewFixedPaneSizePreferenceKey, m_SnapshotFilesListSplitter.fixedPane.resolvedStyle.width);
                m_SnapshotFilesListSplitter.CollapseChild(0);
            }
        }

        void ToggleDetailsPanel(ChangeEvent<bool> e)
        {
            if (e.newValue)
            {
                m_DetailsSplitter.UnCollapse();
                m_DetailsViewController.SetCollapsed(false);
            }
            else
            {
                if (m_DetailsSplitter.fixedPane is { resolvedStyle: not null } && !float.IsNaN(m_DetailsSplitter.fixedPane.resolvedStyle.width))
                    EditorPrefs.SetFloat(k_DetailsPaneSplitViewFixedPaneSizePreferenceKey, m_SnapshotFilesListSplitter.fixedPane.resolvedStyle.width);
                m_DetailsViewController.SetCollapsed(true);
                m_DetailsSplitter.CollapseChild(1);
            }
        }

        protected override void Dispose(bool disposing)
        {
            m_SnapshotDataService.LoadedSnapshotsChanged -= RefreshAnalysisView;
            m_SnapshotDataService.CompareModeChanged -= CompareViewChanged;
            m_ScreenshotsManager.ScreenshotLoaded -= ScreenshotsLoaded;
            m_SnapshotDataService.AboutToUnloadSnapshot -= RefreshAnalysisView;

            base.Dispose(disposing);

            m_ScreenshotsManager?.Dispose();
            m_ScreenshotsManager = null;

            // only save these if the panels are visible and have an initialized size
            if (CaptureToolbarViewController.SavedSnapshotListSplitViewToggleIsVisibleState && m_SnapshotFilesListSplitter.fixedPane is { resolvedStyle: not null } && !float.IsNaN(m_SnapshotFilesListSplitter.fixedPane.resolvedStyle.width))
                EditorPrefs.SetFloat(k_SnapshotListSplitViewFixedPaneSizePreferenceKey, m_SnapshotFilesListSplitter.fixedPane.resolvedStyle.width);

            if (CaptureToolbarViewController.SavedDetailsPaneSplitViewToggleIsVisibleState && m_DetailsSplitter.fixedPane is { resolvedStyle: not null } && !float.IsNaN(m_DetailsSplitter.fixedPane.resolvedStyle.width))
                EditorPrefs.SetFloat(k_DetailsPaneSplitViewFixedPaneSizePreferenceKey, m_DetailsSplitter.fixedPane.resolvedStyle.width);
        }
    }
}

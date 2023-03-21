using System;
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

        const string k_AnalyticsId_SummarySingleViewId = "Summary(Default)";
        const string k_AnalyticsId_SummaryCompareViewId = "Summary Comparison(Default)";
        const string k_AnalyticsId_NoDataViewId = "No Data";

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

        public MemoryProfilerViewController(PlayerConnectionService playerConnectionService, SnapshotDataService snapshotDataService)
        {
            m_PlayerConnectionService = playerConnectionService;
            m_SnapshotDataService = snapshotDataService;
            m_ScreenshotsManager = new ScreenshotsManager();
        }

        public void RefreshScreenshotsOnSceneChange()
        {
            m_ScreenshotsManager.Invalidate();
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

            // When we load a new screenshot, UI needs to be forced refreshed
            // to repaint with the newly loaded textures
            m_ScreenshotsManager.ScreenshotLoaded += (_) =>
            {
                m_LoadedSnapshotsViewContainer.MarkDirtyRepaint();
                m_SnapshotFilesListViewContainer.MarkDirtyRepaint();
            };
        }

        void RefreshAnalysisView()
        {
            // Reset state
            m_AnalysisViewController?.Dispose();
            m_AnalysisViewController = null;
            m_AnalysisViewContainer.Clear();

            // Stop Collecting interaction events for the old page and send them off
            MemoryProfilerAnalytics.EndEventWithMetadata<MemoryProfilerAnalytics.InteractionsInPage>();
            MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.OpenedPageEvent>();

            // Create new analysis controller
            var viewKeyForAnalytics = string.Empty;
            if ((m_SnapshotDataService.Base != null) || (m_SnapshotDataService.Compared != null))
            {
                var baseSnapshot = m_SnapshotDataService.Base;
                var comparedSnapshot = m_SnapshotDataService.CompareMode ? m_SnapshotDataService.Compared : null;
                m_AnalysisViewController = new AnalysisTabBarController(m_DetailsViewController, baseSnapshot, comparedSnapshot);

                // Compare view activation analytics
                if (comparedSnapshot != null)
                {
                    viewKeyForAnalytics = k_AnalyticsId_SummaryCompareViewId;

                    MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.DiffedSnapshotEvent>();
                    var sameSesssion = (baseSnapshot.MetaData.SessionGUID == comparedSnapshot.MetaData.SessionGUID) && (baseSnapshot.MetaData.SessionGUID != Format.MetaData.InvalidSessionGUID);
                    var baseSnapshotDetails = MemoryProfilerAnalytics.GetSnapshotProjectAndUnityVersionDetails(baseSnapshot);
                    var compareSnapshotDetails = MemoryProfilerAnalytics.GetSnapshotProjectAndUnityVersionDetails(comparedSnapshot);
                    if (baseSnapshot.MetaData.Platform == comparedSnapshot.MetaData.Platform)
                    {
                        baseSnapshotDetails |= MemoryProfilerAnalytics.SnapshotProjectAndUnityVersionDetails.SamePlatformAsDiff;
                        compareSnapshotDetails |= MemoryProfilerAnalytics.SnapshotProjectAndUnityVersionDetails.SamePlatformAsDiff;
                    }
                    MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.DiffedSnapshotEvent() { sameSessionDiff = sameSesssion, captureInfoA = baseSnapshotDetails, captureInfoB = compareSnapshotDetails });
                }
                else
                {
                    viewKeyForAnalytics = k_AnalyticsId_SummarySingleViewId;
                }
            }
            else
            {
                viewKeyForAnalytics = k_AnalyticsId_NoDataViewId;

                var noDataViewController = new NoDataViewController();
                noDataViewController.TakeSnapshotSelected += m_PlayerConnectionService.TakeCapture;
                m_AnalysisViewController = noDataViewController;
            }

            m_AnalysisViewContainer.Add(m_AnalysisViewController.View);

            // End page open event and start collecting interaction events for the new page
            MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.OpenedPageEvent() { viewName = viewKeyForAnalytics });
            MemoryProfilerAnalytics.EndEventWithMetadata<MemoryProfilerAnalytics.InteractionsInPage>();
            MemoryProfilerAnalytics.StartEventWithMetaData<MemoryProfilerAnalytics.InteractionsInPage>(new MemoryProfilerAnalytics.InteractionsInPage() { viewName = viewKeyForAnalytics });
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
                m_SnapshotFilesListSplitter.CollapseChild(0);
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
                m_DetailsViewController.SetCollapsed(true);
                m_DetailsSplitter.CollapseChild(1);
            }
        }
    }
}

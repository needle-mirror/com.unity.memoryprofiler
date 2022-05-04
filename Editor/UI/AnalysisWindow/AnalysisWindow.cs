using System.Collections;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor;
using System;
using System.Text;
using Unity.EditorCoroutines.Editor;
using Unity.MemoryProfiler.Editor.UI.PathsToRoot;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class AnalysisWindow
    {
        public Button NoSnapshotOpenedCaptureButton { get; private set; }

        internal enum AnalysisPage
        {
            Summary = 0,
            Breakdowns,
            TreeMap,
            ObjectsAndAllocations,
            Fragmentation,
        }

        AnalysisPage m_CurrentPage;

        VisualElement m_AnalysisWindow;
        IUIStateHolder m_UIStateHolder;
        PathsToRootDetailView m_PathsToRootDetailView;

        VisualElement m_NoDataLoaded;
        VisualElement m_DataLoaded;

        Ribbon m_Ribbon;

        MemoryUsageSummary m_MemoryUsageSummary;
        bool[] m_MemorySummaryUnfoldingStatePerPage = new bool[(int)AnalysisPage.Fragmentation + 1];
        VisualElement m_MemoryUsageSummaryRoot;
        TopIssues m_TopIssues;

        SummaryPane m_SummaryPane;

        VisualElement m_ViewPane;

        OldViewLogic m_OldViewLogic;

        VisualElement m_ToolbarExtension;
        VisualElement m_ViewSelectionUI;

        ViewPane m_ActiveViewPane;

        ViewController m_ObjectBreakdownsViewController;

        public AnalysisWindow(IUIStateHolder memoryProfilerWindow, VisualElement root, SnapshotsWindow snapshotsWindow, PathsToRootDetailView pathsToRootDetailView)
        {
            m_UIStateHolder = memoryProfilerWindow;
            m_PathsToRootDetailView = pathsToRootDetailView;

            var analysisRoot = root.Q("analysis-window");
            m_AnalysisWindow = analysisRoot;

            // quick check if the ui hierarchy is fully loaded in or needs to be instantiated first, e.g. on domain reload
            m_NoDataLoaded = m_AnalysisWindow.Q("analysis-window--no-data-loaded");
            if (m_NoDataLoaded == null)
            {
                analysisRoot.Clear();

                VisualTreeAsset analysisViewTree;
                analysisViewTree = AssetDatabase.LoadAssetAtPath(ResourcePaths.AnalysisWindowUxmlPath, typeof(VisualTreeAsset)) as VisualTreeAsset;

                m_AnalysisWindow = analysisViewTree.Clone(analysisRoot);

                m_NoDataLoaded = m_AnalysisWindow.Q("analysis-window--no-data-loaded");
            }

            NoSnapshotOpenedCaptureButton = m_NoDataLoaded.Q<Button>("analysis-window--no-data-loaded__capture-button");

            m_DataLoaded = m_AnalysisWindow.Q("analysis-window--data-loaded");

            m_Ribbon = m_AnalysisWindow.Q<Ribbon>("analysis-window__ribbon__container");
            m_Ribbon.Clicked += (i) => RibbonButtonClicked(i);
            m_Ribbon.HelpClicked += () =>
            {
                MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInPage, MemoryProfilerAnalytics.PageInteractionType>(
                    MemoryProfilerAnalytics.PageInteractionType.DocumentationOpened);
                Application.OpenURL(DocumentationUrls.AnalysisWindowHelp);
            };

            m_ToolbarExtension = new Toolbar();

            m_ViewSelectionUI = new VisualElement();
            m_ViewSelectionUI.AddToClassList("analysis-window__view-selection-ui");
            var viewSelectionLabel = new Label("Select Table View:");
            viewSelectionLabel.AddToClassList("analysis-window__view-selection-ui_label");
            var viewSelectorButton = new DropDownButton();
            viewSelectorButton.AddToClassList("analysis-window__view-selection-ui_button");
            m_ViewSelectionUI.Add(viewSelectionLabel);
            m_ViewSelectionUI.Add(viewSelectorButton);

            m_OldViewLogic = new OldViewLogic(memoryProfilerWindow, this, m_ToolbarExtension, viewSelectorButton);
            memoryProfilerWindow.UIStateChanged += m_OldViewLogic.OnUIStateChanged;
            m_OldViewLogic.ViewPaneChanged += OnViewPaneChanged;
            memoryProfilerWindow.UIState.SelectionChanged += m_PathsToRootDetailView.UpdateRootObjects;
            m_PathsToRootDetailView.SelectionChangedEvt += memoryProfilerWindow.UIState.RegisterSelectionChangeEvent;

            snapshotsWindow.SwappedSnapshots += () => Recrate(m_UIStateHolder?.UIState?.CurrentMode?.CurrentViewPane);

            var m_OldHistoryLogic = new OldHistoryLogic(memoryProfilerWindow, m_OldViewLogic, root);

            memoryProfilerWindow.UIStateChanged += m_OldHistoryLogic.OnUIStateChanged;

            m_MemoryUsageSummaryRoot = m_AnalysisWindow.Q("analysis-window__memory-usage-summary");
            m_MemoryUsageSummary = new MemoryUsageSummary(m_MemoryUsageSummaryRoot, memoryProfilerWindow);
            memoryProfilerWindow.UIState.SelectionChanged += m_MemoryUsageSummary.OnSelectionChanged;
            m_MemoryUsageSummary.SelectionChangedEvt += memoryProfilerWindow.UIState.RegisterSelectionChangeEvent;
            m_MemorySummaryUnfoldingStatePerPage[0] = true;
            for (int i = 0; i < m_MemorySummaryUnfoldingStatePerPage.Length; i++)
            {
                SessionState.SetBool($"Unity.MemoryProfiler.Editor.MemoryUsageSummaryFoldoutState.{(AnalysisPage)i}", m_MemoryUsageSummary.FoldoutState);
            }

            m_MemoryUsageSummary.Foldout.RegisterValueChangedCallback(OnMemoryUsageSummaryFoldoutStateChanged);

            m_TopIssues = new TopIssues(m_AnalysisWindow.Q("top-ten-issues-section"));

            m_ViewPane = m_AnalysisWindow.Q("analysis-window__view-pane", "analysis-window__view-pane");

            m_ViewPane.Clear();

            m_SummaryPane = new SummaryPane(memoryProfilerWindow, m_OldViewLogic);

            m_ViewPane.Add(m_SummaryPane.VisualElements[0]);

            memoryProfilerWindow.UIState.SelectionChanged += OnSelectionChanged;
            RibbonButtonClicked(0, true);

            memoryProfilerWindow.UIStateChanged += OnUIStateChanged;
            OnUIStateChanged(memoryProfilerWindow.UIState);
        }

        void OnMemoryUsageSummaryFoldoutStateChanged(ChangeEvent<bool> evt)
        {
            // store Foldout state
            m_MemorySummaryUnfoldingStatePerPage[(int)m_CurrentPage] = m_MemoryUsageSummary.FoldoutState;
            SessionState.SetBool($"Unity.MemoryProfiler.Editor.MemoryUsageSummaryFoldoutState.{m_CurrentPage}", m_MemoryUsageSummary.FoldoutState);
        }

        void OnSelectionChanged(MemorySampleSelection selection)
        {
            if (m_ActiveViewPane != null)
                m_ActiveViewPane.OnSelectionChanged(selection);
        }

        void RibbonButtonClicked(int index, bool forceUpdate = false)
        {
            var newPage = (AnalysisPage)index;
            if (!forceUpdate && m_CurrentPage == newPage)
                return;
            var updateViewAnalytics = m_UIStateHolder.UIState.CurrentViewMode != UIState.ViewMode.ShowNone && (!forceUpdate || index == 0);
            if (updateViewAnalytics)
                MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.OpenedPageEvent>();
            var oldPage =   m_CurrentPage;
            m_CurrentPage = newPage;
            // restore foldout state
            m_MemoryUsageSummary.FoldoutState = m_MemorySummaryUnfoldingStatePerPage[(int)m_CurrentPage];
            switch (m_CurrentPage)
            {
                case AnalysisPage.Summary:
                    // force foldout open for summary because it's empty otherwise
                    m_MemoryUsageSummary.FoldoutState = true;
                    SwitchToSummaryView();
                    break;

                case AnalysisPage.Breakdowns:
                    if (oldPage == AnalysisPage.TreeMap)
                        TearDownViewController();
                    SwitchToObjectsView();
                    break;

                case AnalysisPage.TreeMap:
                    if (oldPage == AnalysisPage.Breakdowns)
                        TearDownViewController();
                    SwitchToTreeMapView();
                    break;

                case AnalysisPage.ObjectsAndAllocations:
                    SwitchToObjectsAndAllocationsView();
                    break;

                case AnalysisPage.Fragmentation:
                default:
                    SwitchToFragmentationView();
                    break;
            }

            if (updateViewAnalytics)
            {
                var viewName = forceUpdate ? "Summary(Default)" : m_CurrentPage.ToString();
                MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.OpenedPageEvent() { viewName = viewName });
                // Stop Collecting interaction events for the old page and send them off
                MemoryProfilerAnalytics.EndEventWithMetadata<MemoryProfilerAnalytics.InteractionsInPage>();
                // Start collecting interaction events for this page
                MemoryProfilerAnalytics.StartEventWithMetaData<MemoryProfilerAnalytics.InteractionsInPage>(new MemoryProfilerAnalytics.InteractionsInPage() { viewName = viewName });
            }
            else
            {
                // if no snpashot is open, try ending any interaction events
                // Stop Collecting interaction events for the old page and send them off
                MemoryProfilerAnalytics.EndEventWithMetadata<MemoryProfilerAnalytics.InteractionsInPage>();
            }
        }

        void SwitchToSummaryView()
        {
            if (m_UIStateHolder.UIState.CurrentViewMode != UIState.ViewMode.ShowNone)
            {
                m_OldViewLogic.OpenSummary(null, false);
            }
        }

        void SwitchToObjectsAndAllocationsView()
        {
            if (m_UIStateHolder.UIState.CurrentViewMode != UIState.ViewMode.ShowNone)
                m_OldViewLogic.OpenDefaultTable();
        }

        void SwitchToTreeMapView()
        {
            if (m_UIStateHolder.UIState.CurrentViewMode != UIState.ViewMode.ShowNone)
            {
                if (m_UIStateHolder.UIState.CurrentViewMode != UIState.ViewMode.ShowDiff)
                    m_OldViewLogic.OpenTreeMap(null);
                else
                {
                    if (m_ObjectBreakdownsViewController == null)
                    {
                        m_ObjectBreakdownsViewController = new FeatureUnavailableViewController("This feature is not available when comparing snapshots. Please switch from Compare mode to Single mode in the Snapshot Panel on the left.");
                        SetupViewControllerView();
                    }
                }
            }
        }

        void SwitchToFragmentationView()
        {
            if (m_UIStateHolder.UIState.CurrentViewMode != UIState.ViewMode.ShowNone)
            {
                if (m_UIStateHolder.UIState.CurrentViewMode != UIState.ViewMode.ShowDiff)
                    m_OldViewLogic.OpenMemoryMap(null);
                else
                    m_OldViewLogic.OpenMemoryMapDiff(null);
            }
        }

        // Hacking in an entry point for the Object-Breakdowns view controller architecture in the existing structure, until we can address wider architecture problems.
        void SwitchToObjectsView()
        {
            // Check tab is not already selected.
            if (m_ObjectBreakdownsViewController != null)
                return;

#if UNITY_2022_1_OR_NEWER
            var snapshot = m_UIStateHolder.UIState.snapshotMode?.snapshot;
            if (snapshot != null)
            {
                // Instantiate the object-breakdowns model and view controller using the selected snapshot.
                var objectBreakdownsModel = ObjectBreakdownsModel.CreateDefault(snapshot);
                m_ObjectBreakdownsViewController = new ObjectBreakdownsViewController(objectBreakdownsModel);
            }
            else
            {
                m_ObjectBreakdownsViewController = new FeatureUnavailableViewController($"This feature is not available when comparing snapshots. Please select a single snapshot in the Snapshot inspector.");
            }
#else
            m_ObjectBreakdownsViewController = new FeatureUnavailableViewController($"This feature is not available in Unity {Application.unityVersion}. Please use Unity 2022.1 or newer.");
#endif
            SetupViewControllerView();
        }

        void SetupViewControllerView()
        {
            // Hide the summary view.
            UIElementsHelper.SetVisibility(m_MemoryUsageSummaryRoot, false);

            // Hide the legacy view pane.
            UIElementsHelper.SetVisibility(m_ViewPane, false);

            // Hide the top issues.
            m_TopIssues.SetVisibility(false);
            // Add the view controller's view to the hierarchy.
            m_DataLoaded.Add(m_ObjectBreakdownsViewController.View);
        }

        void OnViewPaneChanged(ViewPane viewPane)
        {
            if (viewPane == null && m_UIStateHolder.UIState.CurrentMode != null)
            {
                viewPane = m_UIStateHolder.UIState.CurrentMode.CurrentViewPane;
            }
            Recrate(viewPane);
        }

        void OnUIStateChanged(UIState newState)
        {
            newState.ModeChanged -= OnModeChanged;
            newState.ModeChanged += OnModeChanged;
            OnModeChanged(newState.CurrentMode, newState.CurrentViewMode);
        }

        void OnModeChanged(UIState.BaseMode newMode, UIState.ViewMode newViewMode)
        {
            RibbonButtonClicked((int)m_CurrentPage, true);
        }

        void TearDownViewController()
        {
            // Show the legacy view pane.
            UIElementsHelper.SetVisibility(m_ViewPane, true);

            // Show the summary view.
            UIElementsHelper.SetVisibility(m_MemoryUsageSummaryRoot, true);

            // Destroy the objects view controller if it has been loaded.
            if (m_ObjectBreakdownsViewController != null)
            {
                m_ObjectBreakdownsViewController.Dispose();
                m_ObjectBreakdownsViewController = null;
            }
        }

        void Recrate(ViewPane viewPane)
        {
            // Hacking in an exit point for the Object-Breakdowns view controller architecture in the existing structure, until we can address wider architecture problems.
            TearDownViewController();

            m_ActiveViewPane = viewPane;
            if (viewPane == null)
            {
                UIElementsHelper.SetVisibility(m_NoDataLoaded, true);
                UIElementsHelper.SetVisibility(m_DataLoaded, false);
                m_ViewPane.Clear();
            }
            else
            {
                UIElementsHelper.SetVisibility(m_NoDataLoaded, false);
                UIElementsHelper.SetVisibility(m_DataLoaded, true);
                m_ViewPane.Clear();


                var firstRoot = m_ViewPane;
                var secondRoot = m_ViewPane;
                var effectiveSplitterCount = viewPane.VisualElements.Length;

                if (viewPane is MemoryMapPane || viewPane is MemoryMapDiffPane)
                {
                    // Memory Map uses the toolbar extension
                    m_ViewPane.Add(m_ToolbarExtension);

                    firstRoot = new TwoPaneSplitView(0, 300, TwoPaneSplitViewOrientation.Vertical);

                    bool hasCallstacks =
                        m_UIStateHolder.UIState.CurrentViewMode == UIState.ViewMode.ShowDiff ?
                        (m_UIStateHolder.UIState.FirstMode as UIState.SnapshotMode).snapshot.NativeAllocationSites != null && (m_UIStateHolder.UIState.FirstMode as UIState.SnapshotMode).snapshot.NativeCallstackSymbols.Count > 0 ||
                        (m_UIStateHolder.UIState.SecondMode as UIState.SnapshotMode).snapshot.NativeAllocationSites != null && (m_UIStateHolder.UIState.SecondMode as UIState.SnapshotMode).snapshot.NativeCallstackSymbols.Count > 0 :
                        m_UIStateHolder.UIState.snapshotMode.snapshot.NativeAllocationSites != null && m_UIStateHolder.UIState.snapshotMode.snapshot.NativeCallstackSymbols.Count > 0;

                    if (!hasCallstacks)
                    {
                        // the call stack pane would be empty so ignore it.
                        effectiveSplitterCount = 2;
                        m_ViewPane.Add(firstRoot);
                    }
                    else
                    {
                        secondRoot = new TwoPaneSplitView(1, 20, TwoPaneSplitViewOrientation.Vertical);
                        secondRoot.Add(firstRoot);
                        m_ViewPane.Add(secondRoot);
                    }
                    m_Ribbon.CurrentOption = (int)AnalysisPage.Fragmentation;
                }
                else if (viewPane is SummaryPane)
                {
                    m_Ribbon.CurrentOption = (int)AnalysisPage.Summary;
                }
                else
                {
                    if (viewPane is TreeMapPane)
                    {
                        m_Ribbon.CurrentOption = (int)AnalysisPage.TreeMap;
                    }
                    else
                    {
                        m_ViewPane.Add(m_ViewSelectionUI);
                        m_Ribbon.CurrentOption = (int)AnalysisPage.ObjectsAndAllocations;
                    }
                }
                m_CurrentPage = (AnalysisPage)m_Ribbon.CurrentOption;


                if (viewPane.VisualElements.Length == 2)
                {
                    firstRoot = new TwoPaneSplitView(0, 300, TwoPaneSplitViewOrientation.Vertical);
                    m_ViewPane.Add(firstRoot);
                }

                for (int i = 0; i < effectiveSplitterCount; i++)
                {
                    if (i <= 1)
                        firstRoot.Add(viewPane.VisualElements[i]);
                    else
                        secondRoot.Add(viewPane.VisualElements[i]);
                }

                if (m_UIStateHolder.UIState.snapshotMode != null)
                {
                    m_MemoryUsageSummary.SetSummaryValues(m_UIStateHolder.UIState.snapshotMode.snapshot);
                    m_TopIssues.SetVisibility(m_CurrentPage == AnalysisPage.Summary);
                    if (m_CurrentPage == AnalysisPage.Summary)
                        m_TopIssues.InitializeIssues(m_UIStateHolder.UIState.snapshotMode.snapshot);
                }
                else if (m_UIStateHolder.UIState.diffMode != null)
                {
                    var snapshotA = (m_UIStateHolder.UIState.diffMode.modeA as UIState.SnapshotMode).snapshot;
                    var snapshotB = (m_UIStateHolder.UIState.diffMode.modeB as UIState.SnapshotMode).snapshot;
                    m_MemoryUsageSummary.SetSummaryValues(snapshotA, snapshotB);

                    m_TopIssues.SetVisibility(m_CurrentPage == AnalysisPage.Summary);
                    if (m_CurrentPage == AnalysisPage.Summary)
                        m_TopIssues.InitializeIssues(snapshotA, snapshotB);
                }
            }
        }

        public void OnDisable()
        {
            m_MemoryUsageSummary.Foldout.UnregisterValueChangedCallback(OnMemoryUsageSummaryFoldoutStateChanged);
            m_CurrentPage = AnalysisPage.Summary;
            m_ViewPane.Clear();
        }
    }
}

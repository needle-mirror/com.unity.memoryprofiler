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

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class AnalysisWindow
    {
        public Button NoSnapshotOpenedCaptureButton { get; private set; }

        enum AnalysisPage
        {
            Summary = 0,
            ObjectsAndAllocations,
            Fragmentation
        }

        AnalysisPage m_CurrentPage;

        VisualElement m_AnalysisWindow;
        IUIStateHolder m_UIStateHolder;

        VisualElement m_NoDataLoaded;
        VisualElement m_DataLoaded;

        Ribbon m_Ribbon;

        MemoryUsageSummary m_MemoryUsageSummary;
        TopIssues m_TopIssues;

        SummaryPane m_SummaryPane;

        VisualElement m_ViewPane;

        OldViewLogic m_OldViewLogic;

        VisualElement m_ToolbarExtension;
        VisualElement m_ViewSelectionUI;

        public AnalysisWindow(IUIStateHolder memoryProfilerWindow, VisualElement root, SnapshotsWindow snapshotsWindow)
        {
            m_UIStateHolder = memoryProfilerWindow;

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

            m_Ribbon = m_AnalysisWindow.Q<Ribbon>("snapshot-window__ribbon__container");
            m_Ribbon.Clicked += RibbonButtonClicked;
            m_Ribbon.HelpClicked += () => Application.OpenURL(DocumentationUrls.AnalysisWindowHelp);

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

            snapshotsWindow.SwappedSnapshots += () => Recrate(m_UIStateHolder?.UIState?.CurrentMode?.CurrentViewPane);

            var m_OldHistoryLogic = new OldHistoryLogic(memoryProfilerWindow, m_OldViewLogic, root);

            memoryProfilerWindow.UIStateChanged += m_OldHistoryLogic.OnUIStateChanged;

            var memoryUsageSummaryRoot = m_AnalysisWindow.Q("MemoryUsageSummary");
            m_MemoryUsageSummary = new MemoryUsageSummary(memoryUsageSummaryRoot);

            m_TopIssues = new TopIssues(m_AnalysisWindow.Q("top-ten-issues-section"));

            m_ViewPane = m_AnalysisWindow.Q("analysis-window__view-pane", "analysis-window__view-pane");

            m_ViewPane.Clear();

            // TODO: Implement Summary Pane

            //m_SummaryPane = new SummaryPane(m_MemoryProfilerWindow.UIState, m_OldViewLogic);

            //m_ViewPane.Add(m_SummaryPane.VisualElements[0]);

            RibbonButtonClicked(0);
        }

        void RibbonButtonClicked(int index)
        {
            m_OldViewLogic.AddCurrentHistoryEvent();
            m_CurrentPage = (AnalysisPage)index;
            switch (m_CurrentPage)
            {
                case AnalysisPage.Summary:
                    SwitchToSummaryView();
                    break;
                case AnalysisPage.ObjectsAndAllocations:
                    SwitchToObjectsAndAllocationsView();
                    break;
                case AnalysisPage.Fragmentation:
                default:
                    SwitchToFragmentationView();
                    break;
            }
        }

        void SwitchToSummaryView()
        {
            if (m_UIStateHolder.UIState.CurrentViewMode != UIState.ViewMode.ShowNone)
            {
                if (m_UIStateHolder.UIState.CurrentViewMode != UIState.ViewMode.ShowDiff)
                    m_OldViewLogic.OpenTreeMap(null);
                else
                    // Diff Tree Map not yet implemented
                    m_OldViewLogic.OpenDefaultTable();
            }
        }

        void SwitchToObjectsAndAllocationsView()
        {
            if (m_UIStateHolder.UIState.CurrentViewMode != UIState.ViewMode.ShowNone)
                m_OldViewLogic.OpenDefaultTable();
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

        void OnViewPaneChanged(ViewPane viewPane)
        {
            if (viewPane == null && m_UIStateHolder.UIState.CurrentMode != null)
            {
                viewPane = m_UIStateHolder.UIState.CurrentMode.CurrentViewPane;
            }
            Recrate(viewPane);
        }

        void Recrate(ViewPane viewPane)
        {
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
                    m_Ribbon.CurrentOption = 2;
                }
                else
                {
                    var isFallbackSummaryPage = (m_UIStateHolder.UIState.diffMode != null && viewPane is SpreadsheetPane && (viewPane as SpreadsheetPane).ViewName == "Diff All Objects");
                    if (viewPane is TreeMapPane || isFallbackSummaryPage)
                    {
                        if (isFallbackSummaryPage)
                        {
                            m_ViewPane.Add(m_ViewSelectionUI);
                            if (m_CurrentPage == AnalysisPage.Summary)
                                m_Ribbon.CurrentOption = 0;
                            else
                                m_Ribbon.CurrentOption = 1;
                        }
                        else
                            m_Ribbon.CurrentOption = 0;
                    }
                    else
                    {
                        m_ViewPane.Add(m_ViewSelectionUI);
                        m_Ribbon.CurrentOption = 1;
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
    }
}

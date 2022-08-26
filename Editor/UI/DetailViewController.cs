using UnityEngine.UIElements;
using UnityEditor.IMGUI.Controls;
using Unity.MemoryProfiler.Editor.UI.PathsToRoot;
using Unity.EditorCoroutines.Editor;
using System.Collections;
using UnityEngine;
using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    class DetailViewController : ViewController
    {
        // State
        IUIStateHolder m_State;

        // View.
        bool m_ReferencesSectionVisible;
        bool m_ReferencesSectionExpanded;
        float m_ReferencesSectionExpandedSize;

        VisualElement m_Root;
        Foldout m_ReferencesFoldout;
        Foldout m_SelectionDetailsFolout;
        TwoPaneSplitView m_DetailsSplitter;
        VisualElement m_DetailsSplitterDragline;

        ObjectOrTypeLabel m_ReferenceSelection;
        PathsToRootDetailView m_PathsToRootDetailView;
        SelectedItemDetailsPanel m_SelectedObjectDetailsPanel;

        public DetailViewController(VisualElement root, IUIStateHolder uiState)
        {
            m_Root = root;
            m_State = uiState;

            // Initial state - visible & expanded
            m_ReferencesSectionVisible = true;
            m_ReferencesSectionExpanded = true;
            m_ReferencesSectionExpandedSize = 0;
        }

        public UIState UIState { get; set; }

        public void ManagedInspectorLinkWasClicked(int inspectorId, int treeViewId)
        {
            m_SelectedObjectDetailsPanel?.ManagedInspectorLinkWasClicked(inspectorId, treeViewId);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_PathsToRootDetailView?.OnDisable();
                m_PathsToRootDetailView = null;
                m_SelectedObjectDetailsPanel?.OnDisable();
                m_SelectedObjectDetailsPanel = null;
                m_ReferenceSelection?.Dispose();
                m_ReferenceSelection = null;

                if (UIState != null)
                    UIState.SelectionChanged -= UpdatePathsToRootSelectionDetails;
            }

            base.Dispose(disposing);
        }

        protected override VisualElement LoadView()
        {
            return m_Root;
        }

        protected override void ViewLoaded()
        {
            base.ViewLoaded();

            // Gather references
            m_DetailsSplitter = View.Q<TwoPaneSplitView>("details-panel__splitter");
            m_DetailsSplitterDragline = m_DetailsSplitter.Q("unity-dragline-anchor");
            m_ReferencesFoldout = View.Q<Foldout>("details-panel__section-header__references");
            m_SelectionDetailsFolout = View.Q<Foldout>("details-panel__section-header__selection-details");
            m_ReferenceSelection = View.Q<ObjectOrTypeLabel>("reference-item-details__unity-item-title");

            // Paths to root
            m_PathsToRootDetailView?.OnDisable();
            m_PathsToRootDetailView = new PathsToRootDetailView(m_State, new TreeViewState(),
                new MultiColumnHeaderWithTruncateTypeName(PathsToRootDetailView.CreateDefaultMultiColumnHeaderState())
                {
                    canSort = false
                }, View.Q<Ribbon>("references__ribbon__container"));
            UIState.SelectionChanged += m_PathsToRootDetailView.UpdateRootObjects;
            m_PathsToRootDetailView.SelectionChangedEvt += UIState.RegisterSelectionChangeEvent;

            // Reference selection
            m_ReferenceSelection.SwitchClasses(classToAdd: "object-or-type-label-selectable", classToRemove: "object-or-type-label");
            m_ReferenceSelection.AddManipulator(new Clickable(() =>
            {
                m_PathsToRootDetailView.ClearSecondarySelection();
            }));
            m_ReferenceSelection.ContextMenuOpening += ShowCopyMenuForReferencesTitle;
            m_ReferenceSelection.SetToNoObjectSelected();

            // Foldout controls
            const string k_ReferencesLabelText = "References";
            const string k_DetailsLabelText = "Selected Item Details";
            m_ReferencesFoldout.RegisterValueChangedCallback((evt) =>
            {
                if (evt.target != m_ReferencesFoldout || evt.newValue == evt.previousValue)
                    return;

                if (evt.newValue)
                {
                    MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.OpenedViewInSidePanelEvent>();
                    MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.OpenedViewInSidePanelEvent() { viewName = k_ReferencesLabelText });
                    ExpandReferencesSeletion();
                }
                else
                {
                    MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInReferencesPanel, MemoryProfilerAnalytics.ReferencePanelInteractionType>(MemoryProfilerAnalytics.ReferencePanelInteractionType.ReferencePanelWasHidden);
                    CollapseReferencesSeletion();
                }
            });
            m_SelectionDetailsFolout.RegisterValueChangedCallback((evt) =>
            {
                if (evt.target != m_SelectionDetailsFolout || evt.newValue == evt.previousValue)
                    return;

                if (evt.newValue)
                {
                    MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.OpenedViewInSidePanelEvent>();
                    MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.OpenedViewInSidePanelEvent() { viewName = k_DetailsLabelText });
                }
                else
                {
                    MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInSelectionDetailsPanel, MemoryProfilerAnalytics.SelectionDetailsPanelInteractionType>(MemoryProfilerAnalytics.SelectionDetailsPanelInteractionType.SelectionDetailsPanelWasHidden);
                }
            });

            // Selection and selection view
            m_SelectedObjectDetailsPanel?.OnDisable();
            m_SelectedObjectDetailsPanel = new SelectedItemDetailsPanel(m_State, View.Q("selected-item-details"));
            new SelectedItemDetailsForTypesAndObjects(m_State);
            UIState.SelectionChanged += UpdatePathsToRootSelectionDetails;
            UIState.SelectionChanged += m_SelectedObjectDetailsPanel.NewDetailItem;

            // References view update
            var referencesContainer = View.Q<IMGUIContainer>("references-imguicontainer");
            referencesContainer.onGUIHandler += () =>
            {
                m_PathsToRootDetailView.DoGUI(referencesContainer.contentRect);
            };
        }

        void ShowCopyMenuForReferencesTitle(ContextualMenuPopulateEvent evt)
        {
            m_SelectedObjectDetailsPanel?.ShowCopyMenu(evt, contextMenu: true);
        }

        bool HasReferencesSection(MemorySampleSelectionType selectionType)
        {
            switch (selectionType)
            {
                case MemorySampleSelectionType.ManagedObject:
                case MemorySampleSelectionType.UnifiedObject:
                case MemorySampleSelectionType.NativeObject:
                    return true;
                case MemorySampleSelectionType.NativeType:
                case MemorySampleSelectionType.ManagedType:
                case MemorySampleSelectionType.None:
                case MemorySampleSelectionType.Allocation:
                case MemorySampleSelectionType.AllocationSite:
                case MemorySampleSelectionType.AllocationCallstack:
                case MemorySampleSelectionType.NativeRegion:
                case MemorySampleSelectionType.ManagedRegion:
                case MemorySampleSelectionType.Allocator:
                case MemorySampleSelectionType.Label:
                case MemorySampleSelectionType.Connection:
                case MemorySampleSelectionType.HighlevelBreakdownElement:
                case MemorySampleSelectionType.Symbol:
                case MemorySampleSelectionType.Group:
                    return false;
                default:
                    // Check with the type author and add to the selection above
                    throw new ArgumentOutOfRangeException();
            }
        }

        void UpdatePathsToRootSelectionDetails(MemorySampleSelection memorySampleSelection)
        {
            // Set data and enable refrence panel if selection has references
            m_ReferenceSelection.SetLabelDataFromSelection(memorySampleSelection, memorySampleSelection.GetSnapshotItemIsPresentIn(UIState));
            SetReferencesSeletionVisible(HasReferencesSection(memorySampleSelection.Type));
        }

        void SetReferencesSeletionVisible(bool visible)
        {
            if (m_ReferencesSectionVisible == visible)
                return;

            if (visible)
            {
                m_DetailsSplitter.UnCollapse();
                UpdateReferencesSeletion(m_ReferencesSectionExpanded);
            }
            else
            {
                // Don't update saved size if section is collapsed
                if (m_ReferencesSectionExpanded)
                    m_ReferencesSectionExpandedSize = m_ReferencesFoldout.layout.height;

                m_DetailsSplitter.CollapseChild(0);
            }

            m_ReferencesSectionVisible = visible;
        }

        void CollapseReferencesSeletion()
        {
            if (!m_ReferencesSectionExpanded)
                return;

            // Save size for the future
            m_ReferencesSectionExpanded = false;
            m_ReferencesSectionExpandedSize = m_ReferencesFoldout.layout.height;

            // Update visual state
            UpdateReferencesSeletion(false);
        }

        void ExpandReferencesSeletion()
        {
            if (m_ReferencesSectionExpanded)
                return;

            // Set state
            m_ReferencesSectionExpanded = true;

            // Update visual state
            UpdateReferencesSeletion(true);
        }

        void UpdateReferencesSeletion(bool expanded)
        {
            // We don't want to have resize bar if reference section is
            // collapsed as this makes user to expand empty section
            m_DetailsSplitterDragline.visible = expanded;

            if (expanded)
            {
                // Cheat to force update
                m_DetailsSplitter.fixedPaneInitialDimension = 0;
                m_DetailsSplitter.fixedPaneInitialDimension = m_ReferencesSectionExpandedSize;
            }
            else
            {
                // Set to minimal size and enforce it so that user can't resize the section
                var minSize = m_ReferencesFoldout.style.minHeight.value.value;
                Debug.Assert(m_ReferencesFoldout.style.minHeight.value.unit == LengthUnit.Pixel, $"Expected that {m_ReferencesFoldout.name} units will be in pixels");
                m_DetailsSplitter.fixedPaneInitialDimension = minSize;
            }
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.IMGUI.Controls;
using Unity.MemoryProfiler.Editor.UI.PathsToRoot;

namespace Unity.MemoryProfiler.Editor.UI
{
    class ObjectDetailsViewController : ViewController
    {
        const string k_UxmlAssetGuid = "d59d3235bf801c14383c88017210b25d";
        const string k_UxmlIdentifierSplitter = "details-panel__splitter";
        const string k_UxmlIdentifierSplitterDragline = "unity-dragline-anchor";
        const string k_UxmlIdentifierReferencesFoldout = "details-panel__section-header__references";
        const string k_UxmlIdentifierReferencesRibbon = "references__ribbon__container";
        const string k_UxmlIdentifierReferencesIMGUI = "references-imguicontainer";
        const string k_UxmlIdentifierDetailsFoldout = "details-panel__section-header__selection-details";
        const string k_UxmlIdentifierReferencesSelection = "reference-item-details__unity-item-title";
        const string k_UxmlIdentifierSelectedDetails = "selected-item-details";

        public const string ReferencesLabelText = "References";
        public const string DetailsLabelText = "Selected Item Details";

        // State
        readonly CachedSnapshot m_Snapshot;
        readonly CachedSnapshot.SourceIndex m_DataSource;

        bool m_ReferencesSectionExpanded;
        float m_ReferencesSectionExpandedSize;

        // View.
        Foldout m_ReferencesFoldout;
        Ribbon m_ReferencesRibbonContainer;
        IMGUIContainer m_ReferencesIMGUIContainer;
        Foldout m_SelectionDetailsFolout;
        TwoPaneSplitView m_DetailsSplitter;
        VisualElement m_DetailsSplitterDragline;
        VisualElement m_SelectedItemDetails;

        ObjectOrTypeLabel m_ReferenceSelection;
        PathsToRootDetailView m_PathsToRootDetailView;
        SelectedItemDetailsPanel m_SelectedObjectDetailsPanel;
        SelectedItemDetailsForTypesAndObjects m_SelectedObjectDetailsBuilder;

        public ObjectDetailsViewController(CachedSnapshot snapshot, CachedSnapshot.SourceIndex source)
        {
            m_Snapshot = snapshot;
            m_DataSource = source;

            m_ReferencesSectionExpanded = true;
            m_ReferencesSectionExpandedSize = float.NaN;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (IsViewLoaded)
                {
                    // Update panel settings to the latest actual size
                    // - We update only if details panel was loaded
                    // - We fetch actual size as it is only updated on visibility
                    //   state change. Don't ask for the size if reference section
                    //   is collapsed, the returned value will be wrong
                    if (m_ReferencesSectionExpanded)
                        m_ReferencesSectionExpandedSize = m_ReferencesFoldout.layout.height;

                    MemoryProfilerSettings.ObjectDetailsReferenceSectionVisible = m_ReferencesSectionExpanded;
                    MemoryProfilerSettings.ObjectDetailsReferenceSectionSize = m_ReferencesSectionExpandedSize;
                }

                m_PathsToRootDetailView?.OnDisable();
                m_PathsToRootDetailView = null;
                m_SelectedObjectDetailsPanel?.Dispose();
                m_SelectedObjectDetailsPanel = null;
                m_ReferenceSelection?.Dispose();
                m_ReferenceSelection = null;
            }

            base.Dispose(disposing);
        }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");

            // Initial state - visible & expanded
            // We can't do it in the constructor, as the previous instance might not be destroyed there yet
            m_ReferencesSectionExpanded = MemoryProfilerSettings.ObjectDetailsReferenceSectionVisible;
            m_ReferencesSectionExpandedSize = MemoryProfilerSettings.ObjectDetailsReferenceSectionSize;

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
            m_DetailsSplitter = view.Q<TwoPaneSplitView>(k_UxmlIdentifierSplitter);
            m_DetailsSplitterDragline = m_DetailsSplitter.Q(k_UxmlIdentifierSplitterDragline);
            m_ReferencesFoldout = view.Q<Foldout>(k_UxmlIdentifierReferencesFoldout);
            m_ReferencesRibbonContainer = view.Q<Ribbon>(k_UxmlIdentifierReferencesRibbon);
            m_ReferencesIMGUIContainer = view.Q<IMGUIContainer>(k_UxmlIdentifierReferencesIMGUI);
            m_SelectionDetailsFolout = view.Q<Foldout>(k_UxmlIdentifierDetailsFoldout);
            m_ReferenceSelection = view.Q<ObjectOrTypeLabel>(k_UxmlIdentifierReferencesSelection);
            m_SelectedItemDetails = view.Q(k_UxmlIdentifierSelectedDetails);
        }

        void RefreshView()
        {
            // Paths to root
            var header = new MultiColumnHeaderWithTruncateTypeName(PathsToRootDetailView.CreateDefaultMultiColumnHeaderState()) { canSort = false };
            m_PathsToRootDetailView?.OnDisable();
            m_PathsToRootDetailView = new PathsToRootDetailView(new TreeViewState(), header, m_ReferencesRibbonContainer);
            m_PathsToRootDetailView.SetRoot(m_Snapshot, m_DataSource);
            m_PathsToRootDetailView.SelectionChangedEvt += UpdateSelectionDetails;

            // Reference selection
            m_ReferenceSelection.SwitchClasses(classToAdd: "object-or-type-label-selectable", classToRemove: "object-or-type-label");
            m_ReferenceSelection.AddManipulator(new Clickable(() =>
            {
                m_PathsToRootDetailView.ClearSecondarySelection();
            }));
            m_ReferenceSelection.ContextMenuOpening += ShowCopyMenuForReferencesTitle;
            m_ReferenceSelection.SetLabelData(m_Snapshot, m_DataSource);

            // Foldout controls
            m_ReferencesFoldout.RegisterValueChangedCallback(ReferencesToggle);
            m_SelectionDetailsFolout.RegisterValueChangedCallback(SelectionDetailsToggle);

            //// Selection and selection view
            m_SelectedObjectDetailsPanel?.Dispose();
            m_SelectedObjectDetailsPanel = new SelectedItemDetailsPanel(m_Snapshot, m_SelectedItemDetails);
            m_SelectedObjectDetailsBuilder = new SelectedItemDetailsForTypesAndObjects(m_Snapshot, m_SelectedObjectDetailsPanel);
            SetSelectedObjectToRoot();

            // References view update
            m_ReferencesIMGUIContainer.onGUIHandler += () =>
            {
                m_PathsToRootDetailView.DoGUI(m_ReferencesIMGUIContainer.contentRect);
            };

            // Update reference section state
            m_ReferencesFoldout.value = m_ReferencesSectionExpanded;
            UpdateReferencesSeletion(m_ReferencesSectionExpanded, true);
        }

        void ReferencesToggle(ChangeEvent<bool> evt)
        {
            if (evt.target != m_ReferencesFoldout || evt.newValue == evt.previousValue)
                return;

            if (evt.newValue)
            {
                ExpandReferencesSeletion();
            }
            else
            {
                CollapseReferencesSeletion();
            }
        }

        void SelectionDetailsToggle(ChangeEvent<bool> evt)
        {
            if (evt.target != m_SelectionDetailsFolout || evt.newValue == evt.previousValue)
                return;

            if (evt.newValue)
            {
                // Send analytics event for the opened references view
                MemoryProfilerAnalytics.SendOpenViewEvent(DetailsLabelText, false);
            }
        }

        void ShowCopyMenuForReferencesTitle(ContextualMenuPopulateEvent evt)
        {
            m_SelectedObjectDetailsPanel?.ShowCopyMenu(evt, contextMenu: true);
        }

        void CollapseReferencesSeletion()
        {
            if (!m_ReferencesSectionExpanded)
                return;

            // Save size for the future
            m_ReferencesSectionExpanded = false;

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

        void UpdateReferencesSeletion(bool expanded, bool initial = false)
        {
            // We don't want to have resize bar if reference section is
            // collapsed as this makes user to expand empty section
            m_DetailsSplitterDragline.visible = expanded;

            if (expanded)
            {
                // Cheat to force update
                m_DetailsSplitter.fixedPaneInitialDimension = 0;
                m_DetailsSplitter.fixedPaneInitialDimension = m_ReferencesSectionExpandedSize;

                // Send analytics event for the opened references view
                MemoryProfilerAnalytics.SendOpenViewEvent(ReferencesLabelText, initial);
            }
            else
            {
                // Don't update size on initial update, as the layout isn't updated yet
                if (!initial)
                    m_ReferencesSectionExpandedSize = m_ReferencesFoldout.layout.height;

                // Set to minimal size and enforce it so that user can't resize the section
                var minSize = m_ReferencesFoldout.style.minHeight.value.value;
                Debug.Assert(m_ReferencesFoldout.style.minHeight.value.unit == LengthUnit.Pixel, $"Expected that {m_ReferencesFoldout.name} units will be in pixels");
                m_DetailsSplitter.fixedPaneInitialDimension = minSize;
            }
        }

        void SetSelectedObjectToRoot()
        {
            UpdateSelectionDetails(m_DataSource);
        }

        void UpdateSelectionDetails(CachedSnapshot.SourceIndex source)
        {
            m_SelectedObjectDetailsPanel.Clear();
            m_SelectedObjectDetailsBuilder.SetSelection(source);
        }
    }
}

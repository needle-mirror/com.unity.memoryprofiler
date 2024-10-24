using System;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MemoryProfiler.Editor.UI.PathsToRoot;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

#if INSTANCE_ID_CHANGED
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#else
using UnityEditor.IMGUI.Controls;
#endif

namespace Unity.MemoryProfiler.Editor.UI
{
    class ObjectDetailsViewController : ViewController
    {
        const string k_UxmlAssetGuid = "d59d3235bf801c14383c88017210b25d";
        const string k_UxmlIdentifierSplitter = "details-panel__splitter";
        const string k_UxmlIdentifierSplitterDragline = "unity-dragline-anchor";
        const string k_UxmlIdentifierReferencesSection = "reference-trees";
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
        readonly long m_ChildCount;
        readonly string m_DataSourceFallbackName;
        readonly string m_DataSourceFallbackDescription;

        bool m_ReferencesSectionExpanded;
        float m_ReferencesSectionExpandedSize;

        bool m_ReferencesSectionHiddenBecauseThereIsNoRelevantData;
        bool ReferencesSectionExpanded => m_ReferencesSectionExpanded && !m_ReferencesSectionHiddenBecauseThereIsNoRelevantData;

        // View.
        VisualElement m_ReferencesSection;
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

        public ObjectDetailsViewController(CachedSnapshot snapshot, CachedSnapshot.SourceIndex source, long childCount = -1, string name = "", string description = "")
        {
            m_Snapshot = snapshot;
            m_DataSource = source;
            m_ChildCount = childCount;

            m_ReferencesSectionExpanded = true;
            m_ReferencesSectionExpandedSize = float.NaN;
            m_DataSourceFallbackName = name;
            m_DataSourceFallbackDescription = description;
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
                    if (m_ReferencesSectionExpanded && !m_ReferencesSectionHiddenBecauseThereIsNoRelevantData)
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
            m_ReferencesSection = view.Q<VisualElement>(k_UxmlIdentifierReferencesSection);
            m_ReferencesFoldout = view.Q<Foldout>(k_UxmlIdentifierReferencesFoldout);
            m_ReferencesRibbonContainer = view.Q<Ribbon>(k_UxmlIdentifierReferencesRibbon);
            m_ReferencesIMGUIContainer = view.Q<IMGUIContainer>(k_UxmlIdentifierReferencesIMGUI);
            m_SelectionDetailsFolout = view.Q<Foldout>(k_UxmlIdentifierDetailsFoldout);
            m_ReferenceSelection = view.Q<ObjectOrTypeLabel>(k_UxmlIdentifierReferencesSelection);
            m_SelectedItemDetails = view.Q(k_UxmlIdentifierSelectedDetails);
        }

        static bool HasReferencesData(CachedSnapshot snapshot, SourceIndex sourceIndex)
        {
            var itemObjectData = ObjectData.FromSourceLink(snapshot, sourceIndex);

            return itemObjectData.IsValid &&
                ObjectConnection.GetAllReferencingObjects(snapshot, itemObjectData).Length
                + ObjectConnection.GetAllReferencedObjects(snapshot, itemObjectData).Length > 0;
        }

        static bool GfxResourceHasNativeObjectAssociation(CachedSnapshot snapshot, SourceIndex sourceIndex)
        {
            Debug.Assert(sourceIndex.Id == SourceIndex.SourceId.GfxResource);
            var objectData = ObjectData.FromSourceLink(snapshot, sourceIndex);
            // grab the references to the Native object, if it is valid
            return objectData.IsValid && objectData.nativeObjectIndex >= 0;
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
                SetSelectedObjectToRoot();
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

            // Update reference section state according to saved stat
            m_ReferencesFoldout.value = m_ReferencesSectionExpanded;
            // override reference panel visibility depending on data availability
            var showReferencesPanel = m_DataSource.Id switch
            {
                // for regular objects, always show the references, as no references is an important datapoint
                SourceIndex.SourceId.NativeObject => true,
                SourceIndex.SourceId.ManagedObject => true,
                SourceIndex.SourceId.GfxResource => GfxResourceHasNativeObjectAssociation(m_Snapshot, m_DataSource),

                // Hide the References panel for non-objects unless there is data to show there.
                // This is to avoid confusion that this no references could mean nothing is referencing them,
                // when really we just don't have the full picture here (e.g. graphics scratch buffers held in native code NativeArrays held by a TempJob)
                _ => HasReferencesData(m_Snapshot, m_DataSource)
            };
            m_ReferencesSectionHiddenBecauseThereIsNoRelevantData = !showReferencesPanel;

            UpdateReferencesSeletion(ReferencesSectionExpanded, true);

            // if the section is hidden from code, hide it entirely, thereby making expanding it impossible as well until a new object is selected
            UIElementsHelper.SetVisibility(m_ReferencesFoldout, showReferencesPanel);
            UIElementsHelper.SetVisibility(m_ReferencesSection, showReferencesPanel);
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

            // Save state for the future
            m_ReferencesSectionExpanded = false;

            // Update visual state
            UpdateReferencesSeletion(ReferencesSectionExpanded);
        }

        void ExpandReferencesSeletion()
        {
            if (m_ReferencesSectionExpanded)
                return;

            // Save state for the future
            m_ReferencesSectionExpanded = true;

            // Update visual state
            UpdateReferencesSeletion(ReferencesSectionExpanded);
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
            UpdateSelectionDetails(m_DataSource, m_DataSourceFallbackName, m_DataSourceFallbackDescription);
        }

        void UpdateSelectionDetails(CachedSnapshot.SourceIndex source)
        {
            UpdateSelectionDetails(source, null, null);
        }

        void UpdateSelectionDetails(CachedSnapshot.SourceIndex source, string fallbackName, string fallbackDescription)
        {
            m_SelectedObjectDetailsPanel.Clear();
            m_SelectedObjectDetailsBuilder.SetSelection(source, fallbackName, fallbackDescription);
        }
    }
}

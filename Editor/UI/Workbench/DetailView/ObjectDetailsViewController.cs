using System;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MemoryProfiler.Editor.UI.PathsToRoot;
using Unity.MemoryProfiler.Editor.UIContentData;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;
using System.Collections.Generic;


#if IMGUI_TREEVIEW_API_CHANGED
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
        const string k_UxmlIdentifierRefocusButton = "reference-item-details__refocus-button";

        public const string ReferencesLabelText = "References";
        public const string DetailsLabelText = "Selected Item Details";

        internal CachedSnapshot.SourceIndex DataSource => m_DataSource;

        // State
        readonly CachedSnapshot m_Snapshot;
        readonly CachedSnapshot.SourceIndex m_DataSource;
        readonly BreakdownTableId m_Table;
        readonly ITableItemLocator m_CurrentTableLocator;
        readonly AnalysisTabBarController m_TabController;
        readonly long m_ChildCount;
        readonly string m_DataSourceFallbackName;
        readonly string m_DataSourceFallbackDescription;

        bool m_ReferencesSectionExpanded;
        float m_ReferencesSectionExpandedSize;
        // A view replaced before its first layout has no real height, which must not overwrite the remembered size.
        bool m_ReferencesSectionLaidOut;

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
        Button m_RefocusButton;
        bool m_ReferenceSelectionDiffersFromRoot;
        PathsToRootDetailView m_PathsToRootDetailView;

        /// <summary>
        /// Internal for testing purposes.
        /// </summary>
        internal PathsToRootDetailView PathsToRootDetailView => m_PathsToRootDetailView;
        SelectedItemDetailsPanel m_SelectedObjectDetailsPanel;
        SelectedItemDetailsForTypesAndObjects m_SelectedObjectDetailsBuilder;

        public ObjectDetailsViewController(CachedSnapshot snapshot, CachedSnapshot.SourceIndex source, BreakdownTableId table,
            ITableItemLocator currentTableLocator = null, AnalysisTabBarController tabController = null, long childCount = -1, string name = "", string description = "")
        {
            m_Snapshot = snapshot;
            m_DataSource = source;
            m_Table = table;
            m_CurrentTableLocator = currentTableLocator;
            m_TabController = tabController;
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
                    if (m_ReferencesSectionExpanded && !m_ReferencesSectionHiddenBecauseThereIsNoRelevantData && m_ReferencesSectionLaidOut)
                        m_ReferencesSectionExpandedSize = m_ReferencesFoldout.layout.height;

                    MemoryProfilerSettings.ObjectDetailsReferenceSectionVisible = m_ReferencesSectionExpanded;
                    MemoryProfilerSettings.ObjectDetailsReferenceSectionSize = m_ReferencesSectionExpandedSize;
                }

                if (m_TabController != null)
                    m_TabController.ActiveTableChanged -= UpdateRefocusButton;
                TableRevealHelper.AnyTableRebuilt -= UpdateRefocusButton;

                m_PathsToRootDetailView?.Dispose();
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
            m_ReferencesFoldout.RegisterCallback<GeometryChangedEvent>(_ => m_ReferencesSectionLaidOut = true);
            m_ReferencesRibbonContainer = view.Q<Ribbon>(k_UxmlIdentifierReferencesRibbon);
            m_ReferencesIMGUIContainer = view.Q<IMGUIContainer>(k_UxmlIdentifierReferencesIMGUI);
            m_SelectionDetailsFolout = view.Q<Foldout>(k_UxmlIdentifierDetailsFoldout);
            m_ReferenceSelection = view.Q<ObjectOrTypeLabel>(k_UxmlIdentifierReferencesSelection);
            m_SelectedItemDetails = view.Q(k_UxmlIdentifierSelectedDetails);
            m_RefocusButton = view.Q<Button>(k_UxmlIdentifierRefocusButton);
        }

        static bool HasReferencesData(CachedSnapshot snapshot, SourceIndex sourceIndex)
        {
            var references = new List<ObjectData>();
            ObjectConnection.GetAllReferencingObjects(snapshot, sourceIndex, references);
            var refCount = references.Count;
            ObjectConnection.GetAllReferencedObjects(snapshot, sourceIndex, references);
            return sourceIndex.Valid && refCount + references.Count > 0;
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
            var header = new MultiColumnHeaderWithTruncateTypeName(PathsToRootDetailView.CreateDefaultMultiColumnHeaderState());
            m_PathsToRootDetailView?.Dispose();
            m_PathsToRootDetailView = new PathsToRootDetailView(new TreeViewState(), header, m_ReferencesRibbonContainer, m_Table, m_CurrentTableLocator, m_TabController);

            var referencesSource = m_DataSource;
            if (m_DataSource.Id == SourceIndex.SourceId.NativeAllocation)
            {
                // for native allocations that are part of a native objects, we want to show the references to the native object which owns them
                var rootId = m_Snapshot.NativeAllocations.RootReferenceId[m_DataSource.Index];
                if (rootId >= NativeRootReferenceEntriesCache.FirstValidRootId && m_Snapshot.NativeObjects.RootReferenceIdToIndex.TryGetValue(rootId, out var nativeObjectIndex))
                {
                    referencesSource = new SourceIndex(SourceIndex.SourceId.NativeObject, nativeObjectIndex);
                }
            }

            m_PathsToRootDetailView.SetRoot(m_Snapshot, referencesSource);

            // Reference selection
            m_ReferenceSelection.SwitchClasses(classToAdd: "object-or-type-label-selectable", classToRemove: "object-or-type-label");
            m_ReferenceSelection.AddManipulator(new Clickable(() => Refocus(m_ReferenceSelection.GetRect())));
            m_PathsToRootDetailView.SelectionChangedEvt += OnReferenceSelected;
            m_RefocusButton.text = TextContent.RefocusButtonLabel;
            m_RefocusButton.tooltip = TextContent.RefocusButtonTooltip;
            m_RefocusButton.clicked += () => Refocus(m_RefocusButton.GetRect());
            if (m_TabController != null)
                m_TabController.ActiveTableChanged += UpdateRefocusButton;
            TableRevealHelper.AnyTableRebuilt += UpdateRefocusButton;
            m_ReferenceSelection.ContextMenuOpening += ShowCopyMenuForReferencesTitle;
            m_ReferenceSelection.ContextMenuOpening += AppendSelectInMenuForReferencesTitle;
            m_ReferenceSelection.SetLabelData(m_Snapshot, referencesSource);

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
            var showReferencesPanel = referencesSource.Id switch
            {
                // for regular objects, always show the references, as no references is an important datapoint
                SourceIndex.SourceId.NativeObject => true,
                SourceIndex.SourceId.ManagedObject => true,
                SourceIndex.SourceId.GfxResource => GfxResourceHasNativeObjectAssociation(m_Snapshot, referencesSource),

                // Hide the References panel for non-objects unless there is data to show there.
                // This is to avoid confusion that this no references could mean nothing is referencing them,
                // when really we just don't have the full picture here (e.g. graphics scratch buffers held in native code NativeArrays held by a TempJob)
                _ => HasReferencesData(m_Snapshot, referencesSource)
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
            m_SelectedObjectDetailsPanel?.ShowCopyMenu(evt, contextMenu: true, includeNavigation: false);
        }

        void AppendSelectInMenuForReferencesTitle(ContextualMenuPopulateEvent evt)
        {
            // The References title always represents this panel's root object, independent of whatever row is
            // currently shown in the Selected Item Details section below (which tracks the last-clicked reference).
            var entries = SelectInMenuBuilder.BuildEntries(m_Snapshot, m_DataSource, CurrentLocator, m_TabController, includeSelectInCurrentTable: true);
            if (entries.Count == 0)
                return;

            evt.menu.AppendSeparator();
            SelectInMenuBuilder.PopulateDropdownMenu(evt.menu, entries);
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
                if (!initial && m_ReferencesSectionLaidOut)
                    m_ReferencesSectionExpandedSize = m_ReferencesFoldout.layout.height;

                // Set to minimal size and enforce it so that user can't resize the section
                var minSize = m_ReferencesFoldout.style.minHeight.value.value;
                Debug.Assert(m_ReferencesFoldout.style.minHeight.value.unit == LengthUnit.Pixel, $"Expected that {m_ReferencesFoldout.name} units will be in pixels");
                m_DetailsSplitter.fixedPaneInitialDimension = minSize;
            }
        }

        void OnReferenceSelected(CachedSnapshot.SourceIndex source)
        {
            UpdateSelectionDetails(source, null, null);
            m_ReferenceSelectionDiffersFromRoot = source != m_DataSource;
            UpdateRefocusButton();
        }

        ITableItemLocator CurrentLocator => TableRevealHelper.ResolveCurrentLocator(m_TabController, m_CurrentTableLocator, m_DataSource, m_Snapshot);

        void UpdateRefocusButton()
        {
            var currentLocator = CurrentLocator;
            var tabChanged = m_TabController != null && m_TabController.ActiveLocator?.TableId != m_Table;
            var itemNeedsRefocus = currentLocator != null
                && (tabChanged || currentLocator.GetVisibility(m_DataSource, m_Snapshot).Status == ItemVisibility.State.HiddenByFilters);
            m_RefocusButton.style.display = m_ReferenceSelectionDiffersFromRoot || itemNeedsRefocus ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void ClearReferenceSelection()
        {
            m_PathsToRootDetailView.ClearSecondarySelection();
            SetSelectedObjectToRoot();
        }

        void Refocus(Rect menuAnchor)
        {
            ClearReferenceSelection();
            TableRevealHelper.RevealWithChoice(CurrentLocator, m_TabController, m_Snapshot,
                m_DataSource, frame: true, select: true, menuAnchor);
        }

        void SetSelectedObjectToRoot()
        {
            m_ReferenceSelectionDiffersFromRoot = false;
            UpdateRefocusButton();
            UpdateSelectionDetails(m_DataSource, m_DataSourceFallbackName, m_DataSourceFallbackDescription);
        }

        void UpdateSelectionDetails(CachedSnapshot.SourceIndex source, string fallbackName, string fallbackDescription)
        {
            m_SelectedObjectDetailsPanel.Clear();
            m_SelectedObjectDetailsBuilder.SetSelection(source, fallbackName, fallbackDescription);
            m_SelectedObjectDetailsPanel.SetPingSelectContext(source, m_CurrentTableLocator, m_TabController);
        }
    }
}

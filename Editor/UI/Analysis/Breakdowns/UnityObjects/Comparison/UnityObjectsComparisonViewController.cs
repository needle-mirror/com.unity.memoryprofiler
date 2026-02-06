using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.MemoryProfiler.Editor.Format;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class UnityObjectsComparisonViewController
        : AbstractComparisonTreeViewController<UnityObjectsComparisonModel, UnityObjectsComparisonModel.ItemData, UnityObjectsModel, UnityObjectsModel.ItemData>
        , UnityObjectsTableViewController.IResponder, IViewControllerWithVisibilityEvents
    {
        const string k_UxmlAssetGuid = "22b678e6f811eec4782c655ff73c2677";
        const string k_UssClass_Dark = "unity-objects-comparison-view__dark";
        const string k_UssClass_Light = "unity-objects-comparison-view__light";
        const string k_UxmlIdentifier_DescriptionLabel = "unity-objects-comparison-view__description-label";
        const string k_UxmlIdentifier_SearchField = "unity-objects-comparison-view__search-field";
        const string k_UxmlIdentifier_SplitView = "unity-objects-comparison-view__split-view";
        const string k_UxmlIdentifier_BaseTotalSizeBar = "unity-objects-comparison-view__base-total-size-bar";
        const string k_UxmlIdentifier_ComparedTotalSizeBar = "unity-objects-comparison-view__compared-total-size-bar";
        const string k_UxmlIdentifier_TreeView = "unity-objects-comparison-view__tree-view";
        const string k_UxmlIdentifier_TreeViewColumn__Description = "unity-objects-comparison-view__tree-view__column__description";
        const string k_UxmlIdentifier_TreeViewColumn__CountDelta = "unity-objects-comparison-view__tree-view__column__count-delta";
        const string k_UxmlIdentifier_TreeViewColumn__SizeDeltaBar = "unity-objects-comparison-view__tree-view__column__size-delta-bar";
        const string k_UxmlIdentifier_TreeViewColumn__SizeDelta = "unity-objects-comparison-view__tree-view__column__size-delta";
        const string k_UxmlIdentifier_TreeViewColumn__TotalSizeInA = "unity-objects-comparison-view__tree-view__column__total-size-in-a";
        const string k_UxmlIdentifier_TreeViewColumn__TotalSizeInB = "unity-objects-comparison-view__tree-view__column__total-size-in-b";
        const string k_UxmlIdentifier_TreeViewColumn__CountInA = "unity-objects-comparison-view__tree-view__column__count-in-a";
        const string k_UxmlIdentifier_TreeViewColumn__CountInB = "unity-objects-comparison-view__tree-view__column__count-in-b";
        const string k_UxmlIdentifier_FlattenToggle = "unity-objects-comparison-view__toolbar__flatten-toggle";
        const string k_UxmlIdentifier_UnchangedToggle = "unity-objects-comparison-view__toolbar__unchanged-toggle";
        const string k_UxmlIdentifier_LoadingOverlay = "unity-objects-comparison-view__loading-overlay";
        const string k_UxmlIdentifier_BaseTitleLabel = "unity-objects-comparison-view__secondary__base-title-label";
        const string k_UxmlIdentifier_BaseDescriptionLabel = "unity-objects-comparison-view__secondary__base-description-label";
        const string k_UxmlIdentifier_BaseViewContainer = "unity-objects-comparison-view__secondary__base-table-container";
        const string k_UxmlIdentifier_ComparedTitleLabel = "unity-objects-comparison-view__secondary__compared-title-label";
        const string k_UxmlIdentifier_ComparedDescriptionLabel = "unity-objects-comparison-view__secondary__compared-description-label";
        const string k_UxmlIdentifier_ComparedViewContainer = "unity-objects-comparison-view__secondary__compared-table-container";
        const string k_UxmlIdentifier_ErrorLabel = "unity-objects-comparison-view__error-label";

        // Sort comparisons for each column.
        protected override Dictionary<string, Comparison<TreeViewItemData<UnityObjectsComparisonModel.ItemData>>> SortComparisons => k_SortComparisons;
        static readonly Dictionary<string, Comparison<TreeViewItemData<UnityObjectsComparisonModel.ItemData>>> k_SortComparisons = new()
        {
            { k_UxmlIdentifier_TreeViewColumn__Description, (x, y) => string.Compare(x.data.Name, y.data.Name, StringComparison.OrdinalIgnoreCase) },
            { k_UxmlIdentifier_TreeViewColumn__CountDelta, (x, y) => x.data.CountDelta.CompareTo(y.data.CountDelta) },
            { k_UxmlIdentifier_TreeViewColumn__SizeDelta, (x, y) => x.data.SizeDelta.CompareTo(y.data.SizeDelta) },
            { k_UxmlIdentifier_TreeViewColumn__SizeDeltaBar, (x, y) => x.data.SizeDelta.CompareTo(y.data.SizeDelta) },
            { k_UxmlIdentifier_TreeViewColumn__TotalSizeInA, (x, y) => x.data.TotalSizeInA.Committed.CompareTo(y.data.TotalSizeInA.Committed) },
            { k_UxmlIdentifier_TreeViewColumn__TotalSizeInB, (x, y) => x.data.TotalSizeInB.Committed.CompareTo(y.data.TotalSizeInB.Committed) },
            { k_UxmlIdentifier_TreeViewColumn__CountInA, (x, y) => x.data.CountInA.CompareTo(y.data.CountInA) },
            { k_UxmlIdentifier_TreeViewColumn__CountInB, (x, y) => x.data.CountInB.CompareTo(y.data.CountInB) },
        };

        // Model.
        readonly CachedSnapshot m_SnapshotA;
        readonly CachedSnapshot m_SnapshotB;
        readonly string m_Description;
        ISelectionDetails m_SelectionDetails;
        bool m_SameSessionComparison;

        // Composed components
        ComparisonModelBuildOrchestrator<UnityObjectsComparisonModel, UnityObjectsComparisonModelBuilder.BuildArgs> m_BuildOrchestrator;
        ComparisonTableColumnManager<UnityObjectsComparisonModel.ItemData> m_ColumnManager;

        // View.
        Label m_DescriptionLabel;
        ToolbarSearchField m_SearchField;
        Toggle m_UnchangedToggle;
        TwoPaneSplitView m_SplitView;
        DetailedSizeBar m_TotalSizeBarA;
        DetailedSizeBar m_TotalSizeBarB;
        Toggle m_FlattenToggle;
        Label m_BaseTitleLabel;
        Label m_BaseDescriptionLabel;
        VisualElement m_BaseViewContainer;
        Label m_ComparedTitleLabel;
        Label m_ComparedDescriptionLabel;
        VisualElement m_ComparedViewContainer;
        bool m_WaitingForFilteringToBeAppliedToBaseView = false;
        bool m_WaitingForFilteringToBeAppliedToCompareView = false;

        // Child Controllers.
        UnityObjectsTableViewController m_BaseTableViewController;
        UnityObjectsTableViewController m_ComparedTableViewController;

        public UnityObjectsComparisonViewController(CachedSnapshot snapshotA, CachedSnapshot snapshotB, string description, ISelectionDetails selectionDetails)
            : base(idOfDefaultColumnWithPercentageBasedWidth: k_UxmlIdentifier_TreeViewColumn__Description)
        {
            m_SnapshotA = snapshotA;
            m_SnapshotB = snapshotB;
            m_Description = description;
            m_SelectionDetails = selectionDetails;

            // Initialize comparison model build orchestrator
            m_BuildOrchestrator = new ComparisonModelBuildOrchestrator<UnityObjectsComparisonModel, UnityObjectsComparisonModelBuilder.BuildArgs>(
                snapshotA,
                snapshotB,
                new UnityObjectsComparisonModelBuilderAdapter(),
                typeof(UnityObjectsComparisonModel).ToString());
        }

        protected override ToolbarSearchField SearchField => m_SearchField;

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

        protected override async void ViewLoaded()
        {
            m_SameSessionComparison = m_SnapshotA.MetaData.SessionGUID != MetaData.InvalidSessionGUID && m_SnapshotA.MetaData.SessionGUID == m_SnapshotB.MetaData.SessionGUID;

            // Configure 'Base (A)' Unity Objects table.
            m_BaseTitleLabel.text = "Base";
            m_BaseTableViewController = new UnityObjectsTableViewController(
                m_SnapshotA,
                null,
                false,
                false,
                this,
                m_SameSessionComparison)
            {
                FlattenHierarchy = true
            };
            // Set table mode and filtering before loading the view (via .View property triggering a build with buildOnLoad = true) to avoid triggering an immediate rebuild
            await m_BaseTableViewController.SetColumnsVisibilityAsync(AllTrackedMemoryTableMode.OnlyCommitted);
            await m_BaseTableViewController.SetFiltersAsync(m_Model?.BaseModel, unityObjectInstanceIdFilter: UnityObjectsModelBuilder.ShowNoObjectsAtAllFilter);

            m_BaseViewContainer.Add(m_BaseTableViewController.View);
            AddChild(m_BaseTableViewController);
            m_BaseTableViewController.HeaderContextMenuPopulateEvent += GenerateEmptyContextMenu;

            // Configure 'Compared (B)' Unity Objects table.
            m_ComparedTitleLabel.text = "Compared";
            m_ComparedTableViewController = new UnityObjectsTableViewController(
                m_SnapshotB,
                null,
                false,
                false,
                this,
                m_SameSessionComparison)
            {
                FlattenHierarchy = true
            };
            // Set table mode and filtering before loading the view (via .View property triggering a build with buildOnLoad = true) to avoid triggering an immediate rebuild
            await m_ComparedTableViewController.SetColumnsVisibilityAsync(AllTrackedMemoryTableMode.OnlyCommitted);
            await m_ComparedTableViewController.SetFiltersAsync(m_Model?.ComparedModel, unityObjectInstanceIdFilter: UnityObjectsModelBuilder.ShowNoObjectsAtAllFilter);

            m_ComparedViewContainer.Add(m_ComparedTableViewController.View);
            AddChild(m_ComparedTableViewController);
            m_ComparedTableViewController.HeaderContextMenuPopulateEvent += GenerateEmptyContextMenu;

            // After seting up base and Compared tables first, finalize the setup for the comparison table via base.ViewLoaded();
            base.ViewLoaded();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_SelectionDetails?.ClearSelection();
            }

            base.Dispose(disposing);
        }

        void GatherReferencesInView(VisualElement view)
        {
            m_DescriptionLabel = view.Q<Label>(k_UxmlIdentifier_DescriptionLabel);
            m_SearchField = view.Q<ToolbarSearchField>(k_UxmlIdentifier_SearchField);
            m_SplitView = view.Q<UnityEngine.UIElements.TwoPaneSplitView>(k_UxmlIdentifier_SplitView);
            m_TotalSizeBarA = view.Q<DetailedSizeBar>(k_UxmlIdentifier_BaseTotalSizeBar);
            m_TotalSizeBarB = view.Q<DetailedSizeBar>(k_UxmlIdentifier_ComparedTotalSizeBar);
            m_TreeView = view.Q<MultiColumnTreeView>(k_UxmlIdentifier_TreeView);
            m_FlattenToggle = view.Q<Toggle>(k_UxmlIdentifier_FlattenToggle);
            m_UnchangedToggle = view.Q<Toggle>(k_UxmlIdentifier_UnchangedToggle);
            m_LoadingOverlay = view.Q<ActivityIndicatorOverlay>(k_UxmlIdentifier_LoadingOverlay);
            m_BaseTitleLabel = view.Q<Label>(k_UxmlIdentifier_BaseTitleLabel);
            m_BaseDescriptionLabel = view.Q<Label>(k_UxmlIdentifier_BaseDescriptionLabel);
            m_BaseViewContainer = view.Q<VisualElement>(k_UxmlIdentifier_BaseViewContainer);
            m_ComparedTitleLabel = view.Q<Label>(k_UxmlIdentifier_ComparedTitleLabel);
            m_ComparedDescriptionLabel = view.Q<Label>(k_UxmlIdentifier_ComparedDescriptionLabel);
            m_ComparedViewContainer = view.Q<VisualElement>(k_UxmlIdentifier_ComparedViewContainer);
            m_ErrorLabel = view.Q<Label>(k_UxmlIdentifier_ErrorLabel);
        }

        void ConfigureSplitViewLayout(GeometryChangedEvent evt)
        {
            // There is currently no way to set a split view's initial dimension as a percentage from UXML/USS, so we must do it manually once on load.
            m_SplitView.fixedPaneInitialDimension = m_SplitView.layout.height * 0.5f;
            m_SplitView.UnregisterCallback<GeometryChangedEvent>(ConfigureSplitViewLayout);
        }

        protected override void ConfigureTreeView()
        {
            base.ConfigureTreeView();

            m_SplitView.RegisterCallback<GeometryChangedEvent>(ConfigureSplitViewLayout);

            m_DescriptionLabel.text = m_Description;
            m_FlattenToggle.text = "Flatten Hierarchy";
            m_FlattenToggle.RegisterValueChangedCallback(SetHierarchyFlattened);
            m_UnchangedToggle.text = "Show Unchanged";
            m_UnchangedToggle.RegisterValueChangedCallback(ApplyFilter);

            // Initialize column manager
            m_ColumnManager = new ComparisonTableColumnManager<UnityObjectsComparisonModel.ItemData>(m_TreeView);

            // Configure columns using the column manager
            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Description,
                "Description",
                0,
                BindCellForDescriptionColumn(),
                UnityObjectsDescriptionCell.Instantiate);

            m_ColumnManager.ConfigureCountDeltaColumn(
                k_UxmlIdentifier_TreeViewColumn__CountDelta,
                "Count Difference",
                item => item.CountDelta);

            m_ColumnManager.ConfigureSizeDeltaBarColumn(
                k_UxmlIdentifier_TreeViewColumn__SizeDeltaBar,
                "Size Difference Bar",
                item => item.SizeDelta,
                () => m_Model.LargestAbsoluteSizeDelta);

            m_ColumnManager.ConfigureSizeDeltaColumn(
                k_UxmlIdentifier_TreeViewColumn__SizeDelta,
                "Size Difference",
                item => item.SizeDelta);

            m_ColumnManager.ConfigureSizeColumn(
                k_UxmlIdentifier_TreeViewColumn__TotalSizeInA,
                "Size In A",
                item => Convert.ToInt64(item.TotalSizeInA.Committed));

            m_ColumnManager.ConfigureSizeColumn(
                k_UxmlIdentifier_TreeViewColumn__TotalSizeInB,
                "Size In B",
                item => Convert.ToInt64(item.TotalSizeInB.Committed));

            m_ColumnManager.ConfigureCountColumn(
                k_UxmlIdentifier_TreeViewColumn__CountInA,
                "Count In A",
                item => Convert.ToInt32(item.CountInA));

            m_ColumnManager.ConfigureCountColumn(
                k_UxmlIdentifier_TreeViewColumn__CountInB,
                "Count In B",
                item => Convert.ToInt32(item.CountInB));
        }

        protected UnityObjectsComparisonModelBuilder.BuildArgs GetComparisonModelBuilderArgs()
        {
            var searchStringFilter = ScopedContainsTextFilter.Create(m_SearchField.value);
            var flatten = m_FlattenToggle.value;
            var includeUnchanged = m_UnchangedToggle.value;
            return new UnityObjectsComparisonModelBuilder.BuildArgs(
                searchStringFilter,
                null,
                null,
                flatten,
                includeUnchanged,
                m_SameSessionComparison && !flatten,
                OnUnityObjectNameGroupComparisonSelected,
                OnUnityObjectTypeComparisonSelected);
        }

        protected override Func<UnityObjectsComparisonModel> GetModelBuilderTask(CancellationToken cancellationToken)
        {
            var compareArgs = GetComparisonModelBuilderArgs();
            return m_BuildOrchestrator.CreateBuildTask(compareArgs, cancellationToken);
        }

        protected override void OnViewReloaded(bool success)
        {
            base.OnViewReloaded(success);

            // Update usage counters
            MemoryProfilerAnalytics.AddUnityObjectsComparisonUsage(!string.IsNullOrEmpty(m_SearchField.value), m_FlattenToggle.value, m_UnchangedToggle.value);
        }

        protected override void RefreshView()
        {
            var maxValue = Math.Max(m_Model.TotalSnapshotSizeA.Committed, m_Model.TotalSnapshotSizeB.Committed);
            SetDetailedProgressBarValues(m_TotalSizeBarA, m_Model.TotalSizeA.Committed, m_Model.TotalSnapshotSizeA.Committed, maxValue);
            SetDetailedProgressBarValues(m_TotalSizeBarB, m_Model.TotalSizeB.Committed, m_Model.TotalSnapshotSizeB.Committed, maxValue);

            base.RefreshView();
        }

        void SetDetailedProgressBarValues(DetailedSizeBar detailedSizeBar, ulong totalMemorySize, ulong totalSnapshotMemorySize, ulong maxValue)
        {
            detailedSizeBar.Bar.Mode = MemoryBarElement.VisibilityMode.CommittedOnly;
            detailedSizeBar.SetValue(new MemorySize(totalMemorySize, 0), totalSnapshotMemorySize, maxValue);

            var totalMemorySizeText = EditorUtility.FormatBytes((long)totalMemorySize);
            detailedSizeBar.SetSizeText($"Allocated Memory In Table: {totalMemorySizeText}", $"{totalMemorySize:N0} B");

            var totalSnapshotMemorySizeText = EditorUtility.FormatBytes((long)totalSnapshotMemorySize);
            detailedSizeBar.SetTotalText($"Total Memory In Snapshot: {totalSnapshotMemorySizeText}", $"{totalSnapshotMemorySize:N0} B");
        }

        void GenerateEmptyContextMenu(ContextualMenuPopulateEvent evt)
        {
            // Compare mode doesn't allow column configuration
            evt.menu.ClearItems();
            evt.StopImmediatePropagation();
        }

        Action<VisualElement, int> BindCellForDescriptionColumn()
        {
            const string k_NoName = "<No Name>";
            return (element, rowIndex) =>
            {
                var cell = (UnityObjectsDescriptionCell)element;
                var itemData = m_TreeView.GetItemDataForIndex<UnityObjectsComparisonModel.ItemData>(rowIndex);

                var typeName = itemData.NativeTypeName;
                cell.SetTypeName(typeName);

                var displayText = itemData.Name;
                if (string.IsNullOrEmpty(displayText))
                    displayText = k_NoName;
                cell.SetText(displayText);

                string secondaryDisplayText;
                var childCount = itemData.ChildCount;
                if (childCount > 0)
                {
                    secondaryDisplayText = $"({childCount:N0} group{(childCount != 1 ? "s" : string.Empty)})";
                }
                else
                {
                    if (itemData.CountInA != itemData.CountInB)
                        secondaryDisplayText = $"({itemData.CountInA:N0} → {itemData.CountInB:N0} object{(itemData.CountInB != 1 ? "s" : string.Empty)})";
                    else
                        secondaryDisplayText = $"({itemData.CountInA:N0} object{(itemData.CountInA != 1 ? "s" : string.Empty)})";
                }
                cell.SetSecondaryText(secondaryDisplayText);
            };
        }

        void ApplyFilter(ChangeEvent<bool> evt)
        {
            // ApplyFilter is a listener to UI input (Unchanged filter changes) and therefore doesn't expect an awaitable task.
            // No caller or following code expects any part of the async build to be done.
            // Therefore: Fire-and-forget with proper error handling.
            FireAndForgetBuildModelAsync(false);
        }

        void SetHierarchyFlattened(ChangeEvent<bool> evt)
        {
            // SetHierarchyFlattened is a listener to UI input and therefore doesn't expect an awaitable task.
            // No caller or following code expects any part of the async build to be done.
            // Therefore: Fire-and-forget with proper error handling.
            FireAndForgetBuildModelAsync(false);
        }

        protected override void OnTreeItemSelected(int itemId, UnityObjectsComparisonModel.ItemData itemData)
        {
            // Invoke the selection processor for the selected item.
            itemData.SelectionProcessor?.Invoke();
        }

        public override void ClearSelection()
        {
            base.ClearSelection();
            m_BaseTableViewController.ClearSelection();
            m_ComparedTableViewController.ClearSelection();
            m_BaseTableViewController.SetFilters(unityObjectInstanceIdFilter: UnityObjectsModelBuilder.ShowNoObjectsAtAllFilter);
            m_ComparedTableViewController.SetFilters(unityObjectInstanceIdFilter: UnityObjectsModelBuilder.ShowNoObjectsAtAllFilter);
        }

        void OnUnityObjectNameGroupComparisonSelected(string objectName, string typeName, string instancId, UnityObjectsComparisonModelBuilder.SnapshotType snapshotType)
        {
            m_BaseTableViewController.ClearSelection();
            m_ComparedTableViewController.ClearSelection();
            m_SelectionDetails.ClearSelection();

            m_WaitingForFilteringToBeAppliedToBaseView = true;
            m_WaitingForFilteringToBeAppliedToCompareView = true;

            var objectNameFilter = MatchesTextFilter.Create(objectName);
            IEntityIdFilter sourceIndexFilter = null;
            if (!string.IsNullOrEmpty(instancId)
                && instancId.Substring(NativeObjectTools.NativeObjectIdFormatStringPrefix.Length).TryConvertToEntityID(out var instanceIdValue))
            {
                var snapshot = snapshotType switch
                {
                    UnityObjectsComparisonModelBuilder.SnapshotType.Base => m_SnapshotA,
                    UnityObjectsComparisonModelBuilder.SnapshotType.Compared => m_SnapshotB,
                    UnityObjectsComparisonModelBuilder.SnapshotType.Undefined => null,
                    _ => null
                };

                sourceIndexFilter = MatchesInstanceIdFilter.Create(instanceIdValue, snapshot);
            }
            var potentialAssemblyNameStart = typeName.IndexOf(UnityObjectsComparisonModel.AssemblyNameDisambiguationSeparator);
            if (potentialAssemblyNameStart != -1)
                typeName = typeName.Substring(0, potentialAssemblyNameStart);
            // TODO: This is a hack due for a pending refactor on a different branch that does away with the need to string filter the bottom tables.
            // Right now it will show objects with the same name, type, and instance ID, but potentially different assemblies.
            var typeNameFilter = MatchesTextFilter.Create(typeName);
            m_BaseTableViewController.SetFilters(
                m_Model?.BaseModel,
                unityObjectNameFilter: objectNameFilter,
                unityObjectTypeNameFilter: typeNameFilter,
                unityObjectInstanceIdFilter: sourceIndexFilter);
            m_ComparedTableViewController.SetFilters(
                m_Model?.ComparedModel,
                unityObjectNameFilter: objectNameFilter,
                unityObjectTypeNameFilter: typeNameFilter,
                unityObjectInstanceIdFilter: sourceIndexFilter);
        }


        void OnUnityObjectTypeComparisonSelected(string nativeTypeName)
        {
            m_BaseTableViewController.ClearSelection();
            m_ComparedTableViewController.ClearSelection();
            m_SelectionDetails.ClearSelection();

            m_BaseTableViewController.SetFilters(
                unityObjectInstanceIdFilter: UnityObjectsModelBuilder.ShowNoObjectsAtAllFilter);
            m_ComparedTableViewController.SetFilters(
                unityObjectInstanceIdFilter: UnityObjectsModelBuilder.ShowNoObjectsAtAllFilter);
        }

        void UnityObjectsTableViewController.IResponder.UnityObjectsTableViewControllerReloaded(
            UnityObjectsTableViewController tableViewController,
            bool success)
        {
            var isBaseTable = tableViewController == m_BaseTableViewController;
            var descriptionLabel = (isBaseTable) ? m_BaseDescriptionLabel : m_ComparedDescriptionLabel;
            var filteringForInstanceID = tableViewController.SourceIndexFilter != null;

            var tableIsFilteringExplicitlyForNoObjects = tableViewController.ExcludeAllFilterApplied;
            if (!tableIsFilteringExplicitlyForNoObjects)
            {
                var model = tableViewController.Model;
                var objectCount = model.RootNodes.Count;
                descriptionLabel.text = $"{objectCount:N0} object{(objectCount != 1 ? "s" : string.Empty)} with same {(filteringForInstanceID ? "ID" : "name")} | Group size: {EditorUtility.FormatBytes(Convert.ToInt64(model.TotalMemorySize.Committed))}";
            }

            // Hide the description if the table is explicitly filtering for 'no objects'.
            UIElementsHelper.SetElementDisplay(descriptionLabel, !tableIsFilteringExplicitlyForNoObjects);

            var selectFirstItem = false;
            if (isBaseTable && m_WaitingForFilteringToBeAppliedToBaseView)
            {
                // select the first item if the Responder was just waiting for Base View to reload
                selectFirstItem = !m_WaitingForFilteringToBeAppliedToCompareView;
                m_WaitingForFilteringToBeAppliedToBaseView = false;
            }
            else if (!isBaseTable && m_WaitingForFilteringToBeAppliedToCompareView)
            {
                // select the first item if the Responder was just waiting for Compare View to reload
                selectFirstItem = !m_WaitingForFilteringToBeAppliedToBaseView;
                m_WaitingForFilteringToBeAppliedToCompareView = false;
            }
            if (selectFirstItem)
            {
                if (!m_BaseTableViewController.SelectFirstItem())
                    m_ComparedTableViewController.SelectFirstItem();
            }
        }

        void UnityObjectsTableViewController.IResponder.UnityObjectsTableViewControllerSelectedItem(
            int itemId,
            UnityObjectsTableViewController viewController,
            UnityObjectsModel.ItemData itemData)
        {
            var isBaseView = viewController == m_BaseTableViewController;

            // Clear old selection
            var deselectedViewController = isBaseView ? m_ComparedTableViewController : m_BaseTableViewController;
            deselectedViewController.ClearSelection();

            // Make new selection
            var snapshot = isBaseView ? m_SnapshotA : m_SnapshotB;
            var view = BreakdownDetailsViewControllerFactory.Create(snapshot, itemId, itemData.Name, itemData.ChildCount, itemData.Source);
            m_SelectionDetails.SetSelection(view);
        }

        void IViewControllerWithVisibilityEvents.ViewWillBeDisplayed()
        {
            // Silent deselection on revisiting this view.
            // The Selection Details panel should stay the same but the selection in the tables needs to be cleared
            // So that there is no confusion about what is selected, and so that there is no previously selected item
            // that won't update the Selection Details panel when an attempt to select it is made.
            ClearSelection();
        }

        void IViewControllerWithVisibilityEvents.ViewWillBeHidden()
        {
        }
    }
}

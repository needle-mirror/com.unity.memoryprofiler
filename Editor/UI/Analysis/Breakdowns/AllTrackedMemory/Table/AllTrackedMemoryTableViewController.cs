using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class AllTrackedMemoryModelBuilderAdapter : IModelBuilder<AllTrackedMemoryModel, AllTrackedMemoryModelBuilder.BuildArgs>
    {
        readonly AllTrackedMemoryModelBuilder m_Builder = new AllTrackedMemoryModelBuilder();

        public AllTrackedMemoryModel EmptyModel => new AllTrackedMemoryModel(new(), default);

        public AllTrackedMemoryModel Build(CachedSnapshot snapshot, AllTrackedMemoryModelBuilder.BuildArgs args)
        {
            return m_Builder.Build(snapshot, args);
        }

        public AllTrackedMemoryModel Build(AllTrackedMemoryModel baseModel, IList<int> treeNodeIdFilter)
        {
            return m_Builder.Build(baseModel, treeNodeIdFilter);
        }

        public void ClearIdToIndexCache()
        {
            m_Builder.ClearIdToIndexCache();
        }
    }

    class AllTrackedMemoryTableViewController : SingleSnapshotTreeViewController<AllTrackedMemoryModel, AllTrackedMemoryModel.ItemData>, IAnalysisViewSelectable
    {
        const string k_UxmlAssetGuid = "e7ac30fe2b076984e978d41347c5f0e0";

        protected override string UxmlIdentifier_TreeViewColumn__Size => k_UxmlIdentifier_TreeViewColumn__Size;
        protected override string UxmlIdentifier_TreeViewColumn__ResidentSize => k_UxmlIdentifier_TreeViewColumn__ResidentSize;
        const string k_UssClass_Cell_Unreliable = "analysis-view__text__information-unreliable-or-unavailable";
        const string k_UxmlIdentifier_TreeView = "all-tracked-memory-table-view__tree-view";
        const string k_UxmlIdentifier_TreeViewColumn__Description = "all-tracked-memory-table-view__tree-view__column__description";
        const string k_UxmlIdentifier_TreeViewColumn__Size = "all-tracked-memory-table-view__tree-view__column__size";
        const string k_UxmlIdentifier_TreeViewColumn__SizeBar = "all-tracked-memory-table-view__tree-view__column__size-bar";
        const string k_UxmlIdentifier_TreeViewColumn__ResidentSize = "all-tracked-memory-table-view__tree-view__column__resident-size";
        const string k_UxmlIdentifier_TreeViewColumn__Status = "all-tracked-memory-table-view__tree-view__column__status";
        const string k_UxmlIdentifier_LoadingOverlay = "all-tracked-memory-table-view__loading-overlay";
        const string k_UxmlIdentifier_ErrorLabel = "all-tracked-memory-table-view__error-label";

        // Model.
        readonly CachedSnapshot m_Snapshot;
        readonly bool m_CompareMode;
        readonly bool m_DisambiguateUnityObjects;
        readonly IResponder m_Responder;
        protected override Dictionary<string, Comparison<TreeViewItemData<AllTrackedMemoryModel.ItemData>>> SortComparisons { get; }

        public override bool ExcludeAllFilterApplied => m_FilterManager.TreeNodeIdFilter != null && m_FilterManager.TreeNodeIdFilter.Count == 0;

        // Composed components
        readonly TableFilterManager<AllTrackedMemoryModel, AllTrackedMemoryModel.ItemData> m_FilterManager;
        readonly ModelBuildOrchestrator<AllTrackedMemoryModel, AllTrackedMemoryModelBuilder.BuildArgs> m_BuildOrchestrator;
        TreeViewColumnManager<AllTrackedMemoryModel.ItemData> m_ColumnManager;
        SizeCellRenderer m_SizeCellRenderer;

        // View.
        int? m_SelectAfterLoadItemId;
        const int k_DefaultColumnWidth = 120;
        const int k_SizeBarColumnWidth = 180;

        public AllTrackedMemoryTableViewController(
            CachedSnapshot snapshot,
            ToolbarSearchField searchField = null,
            StatusFilterButton statusFilterButton = null,
            bool compareMode = false,
            bool disambiguateUnityObjects = false,
            IResponder responder = null)
            : base(idOfDefaultColumnWithPercentageBasedWidth: null)
        {
            m_Snapshot = snapshot;
            m_SearchField = searchField;
            m_CompareMode = compareMode;
            m_DisambiguateUnityObjects = disambiguateUnityObjects;
            m_Responder = responder;

            m_SelectAfterLoadItemId = null;

            // Initialize composed components
            m_FilterManager = new TableFilterManager<AllTrackedMemoryModel, AllTrackedMemoryModel.ItemData>();
            statusFilterButton?.SetUp(m_FilterManager, () => { if (IsViewLoaded) FireAndForgetBuildModelAsync(); }, ObjectStatusExtensions.AllOfMemoryTableUnavailableStatuses);
            m_BuildOrchestrator = new ModelBuildOrchestrator<AllTrackedMemoryModel, AllTrackedMemoryModelBuilder.BuildArgs>(
                snapshot,
                new AllTrackedMemoryModelBuilderAdapter(),
                typeof(AllTrackedMemoryModel).ToString());

            SearchFilterChanged += OnSearchFilterChanged;
            MemoryProfilerSettings.AllocationRootsToSplitChanged += OnAllocationRootsToSplitChanged;

            // Sort comparisons for each column.
            SortComparisons = new()
            {
                { k_UxmlIdentifier_TreeViewColumn__Description, (x, y) => string.Compare(x.data.Name, y.data.Name, StringComparison.OrdinalIgnoreCase) },
                { k_UxmlIdentifier_TreeViewColumn__Size, (x, y) => x.data.TotalSize.Committed.CompareTo(y.data.TotalSize.Committed) },
                { k_UxmlIdentifier_TreeViewColumn__ResidentSize, (x, y) => x.data.TotalSize.Resident.CompareTo(y.data.TotalSize.Resident) },
                { k_UxmlIdentifier_TreeViewColumn__SizeBar, (x, y) => TableMode != AllTrackedMemoryTableMode.OnlyResident ? x.data.TotalSize.Committed.CompareTo(y.data.TotalSize.Committed) : x.data.TotalSize.Resident.CompareTo(y.data.TotalSize.Resident) },
                { k_UxmlIdentifier_TreeViewColumn__Status, (x, y) => x.data.WarningInfo.GetStatusSortKey().CompareTo(y.data.WarningInfo.GetStatusSortKey()) },
            };
        }

        public AllTrackedMemoryModel Model => m_Model;

        public IScopedFilter<string> SearchFilter => m_FilterManager.SearchFilter;
        public IEnumerable<ITextFilter> ItemPathFilter => m_FilterManager.PathFilters;

        ToolbarSearchField m_SearchField = null;
        protected override ToolbarSearchField SearchField => m_SearchField;

        void OnSearchFilterChanged(IScopedFilter<string> searchFilter)
        {
            SetFilters(searchFilter: searchFilter);
        }

        public void SetFilters(
            AllTrackedMemoryModel model = null,
            IScopedFilter<string> searchFilter = null,
            IEnumerable<ITextFilter> itemPathFilter = null,
            IList<int> treeNodeIdFilter = null)
        {
            // SetFilters is a listener to UI input (search filter changes) and therefore doesn't expect an awaitable task.
            // No caller or following code expects any part of the asnyc build to be done.
            FireAndForgetFilterAsync(model, searchFilter, itemPathFilter, treeNodeIdFilter);
        }

        async void FireAndForgetFilterAsync(
            AllTrackedMemoryModel model = null,
            IScopedFilter<string> searchFilter = null,
            IEnumerable<ITextFilter> itemPathFilter = null,
            IList<int> treeNodeIdFilter = null)
        {
            try
            {
                await SetFiltersAsync(model, searchFilter, itemPathFilter, treeNodeIdFilter);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is expected and acceptable - don't log
            }
            catch (Exception e)
            {
                // Log unexpected exceptions but don't rethrow (would crash the application in async void)
                Debug.LogException(e);
            }
        }

        public async Task SetFiltersAsync(
            AllTrackedMemoryModel model = null,
            IScopedFilter<string> searchFilter = null,
            IEnumerable<ITextFilter> itemPathFilter = null,
            IList<int> treeNodeIdFilter = null)
        {
            m_FilterManager.SearchFilter = searchFilter;
            m_FilterManager.PathFilters = itemPathFilter;
            m_FilterManager.TreeNodeIdFilter = treeNodeIdFilter;

            if (model != m_FilterManager.BaseModelForTreeNodeIdFiltering)
                m_BuildOrchestrator.ClearIdToIndexCache();
            m_FilterManager.BaseModelForTreeNodeIdFiltering = model;

            await m_FilterManager.ApplyFiltersAsync(m_FilterManager.TreeNodeIdFilter != null ?
                () => FilterModelAsync(m_FilterManager.BaseModelForTreeNodeIdFiltering, m_FilterManager.TreeNodeIdFilter, m_BuildOrchestrator) :
                () => BuildModelAsync(false), IsViewLoaded);
        }

        void OnAllocationRootsToSplitChanged(string[] allocationRootsToSplit)
        {
            if (IsViewLoaded)
                // OnAllocationRootsToSplitChanged is a listener to UI input and therefore doesn't expect an awaitable task.
                // No caller or following code expects any part of the async build to be done.
                // Therefore: Fire-and-forget with proper error handling.
                FireAndForgetBuildModelAsync(false);
        }

        public bool TrySelectCategory(IAnalysisViewSelectable.Category category)
        {
            int itemId = (int)category;

            // If tree view isn't loaded & populated yet, we have to delay
            // selection until async process is finished
            if (!TrySelectAndExpandTreeViewItem(itemId))
                m_SelectAfterLoadItemId = itemId;

            // Currently we have only "All Tracked View" categories to select,
            // so we always return true
            return true;
        }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");

            GatherReferencesInView(view);

            return view;
        }

        protected override void Dispose(bool disposing)
        {
            MemoryProfilerSettings.AllocationRootsToSplitChanged -= OnAllocationRootsToSplitChanged;

            SearchFilterChanged -= OnSearchFilterChanged;
            base.Dispose(disposing);
        }

        void GatherReferencesInView(VisualElement view)
        {
            m_TreeView = view.Q<MultiColumnTreeView>(k_UxmlIdentifier_TreeView);
            m_LoadingOverlay = view.Q<ActivityIndicatorOverlay>(k_UxmlIdentifier_LoadingOverlay);
            m_ErrorLabel = view.Q<Label>(k_UxmlIdentifier_ErrorLabel);

            // Initialize components that depend on m_TreeView
            m_ColumnManager = new TreeViewColumnManager<AllTrackedMemoryModel.ItemData>(m_TreeView);
            m_SizeCellRenderer = new SizeCellRenderer(m_TreeView, () => TableMode, TextContent.NotAvailable, k_UssClass_Cell_Unreliable);
        }

        bool CanShowResidentMemory()
        {
            return m_Snapshot.HasSystemMemoryResidentPages && !m_CompareMode;
        }

        protected override void ConfigureTreeView()
        {
            base.ConfigureTreeView();

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Description,
                TextContent.DescriptionTableColumnHeader,
                0,
                BindCellForDescriptionColumn(),
                makeCell: () => new ObjectOrTypeLabel() { HideTypeInfoForObjects = true },
                destroyCell: cell => cell.Q<ObjectOrTypeLabel>().Dispose());

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Size,
                TextContent.AllocatedSizeTableColumnHeader,
                k_DefaultColumnWidth,
                m_SizeCellRenderer.CreateMemorySizeBinding<AllTrackedMemoryModel.ItemData>(
                    item => item.TotalSize,
                    item => item.Unreliable,
                    TextContent.GraphicsResidentMemoryUnreliableTooltip,
                    useResident: false),
                visible: TableMode != AllTrackedMemoryTableMode.OnlyResident);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__SizeBar,
                TextContent.PercentImpactTableColumnHeader,
                k_SizeBarColumnWidth,
                m_SizeCellRenderer.CreateMemoryBarBinding<AllTrackedMemoryModel.ItemData>(
                    item => item.TotalSize,
                    () => TableMode != AllTrackedMemoryTableMode.OnlyResident
                        ? new MemorySize(m_Model.TotalMemorySize.Committed, m_Model.TotalMemorySize.Committed)
                        : new MemorySize(m_Model.TotalMemorySize.Resident, m_Model.TotalMemorySize.Resident),
                    item => item.Unreliable),
                makeCell: m_SizeCellRenderer.MakeMemoryBarCell);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__ResidentSize,
                TextContent.ResidentSizeTableColumnHeader,
                k_DefaultColumnWidth,
                m_SizeCellRenderer.CreateMemorySizeBinding<AllTrackedMemoryModel.ItemData>(
                    item => item.TotalSize,
                    item => item.Unreliable,
                    TextContent.GraphicsResidentMemoryUnreliableTooltip,
                    useResident: true),
                visible: CanShowResidentMemory() && TableMode != AllTrackedMemoryTableMode.OnlyCommitted);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Status,
                TextContent.StatusTableColumnHeader,
                k_DefaultColumnWidth,
                BindCellForStatusColumn(),
                makeCell: () => new StatusCell(),
                visible: true);
        }

        protected AllTrackedMemoryModelBuilder.BuildArgs GetModelBuilderArgs()
        {
            return new AllTrackedMemoryModelBuilder.BuildArgs(
                m_FilterManager.CompoundSearchFilter,
                m_FilterManager.PathFilters,
                MemoryProfilerSettings.ShowReservedMemoryBreakdown,
                m_DisambiguateUnityObjects,
                TableMode == AllTrackedMemoryTableMode.OnlyCommitted,
                MemoryProfilerSettings.FeatureFlags.EnableDynamicAllocationBreakdown_2024_10 ? MemoryProfilerSettings.AllocationRootsToSplit : null);
        }

        protected override Func<AllTrackedMemoryModel> GetModelBuilderTask(CancellationToken cancellationToken)
        {
            var args = GetModelBuilderArgs();
            return m_BuildOrchestrator.CreateBuildTask(
                args,
                m_FilterManager.BaseModelForTreeNodeIdFiltering,
                m_FilterManager.TreeNodeIdFilter,
                cancellationToken);
        }

        protected override void OnViewReloaded(bool success)
        {
            base.OnViewReloaded(success);
            // Notify responder.
            m_Responder?.Reloaded(this, success);
            // Update usage counters
            MemoryProfilerAnalytics.AddAllTrackedMemoryUsage(m_FilterManager.SearchFilter != null, MemoryProfilerSettings.ShowReservedMemoryBreakdown, TableMode);
        }

        protected override void RefreshView(bool restoreSelection = true)
        {
            base.RefreshView(restoreSelection);

            if (m_SelectAfterLoadItemId.HasValue)
            {
                // At this point we expect that it can't fail
                TrySelectAndExpandTreeViewItem(m_SelectAfterLoadItemId.Value);
                m_SelectAfterLoadItemId = null;
            }
        }

        bool TrySelectAndExpandTreeViewItem(int itemId)
        {
            if (m_TreeView.viewController.GetIndexForId(itemId) == -1)
                return false;

            m_TreeView.SetSelectionById(itemId);
            m_TreeView.ExpandItem(itemId);
            m_TreeView.Focus();
            m_TreeView.schedule.Execute(() => m_TreeView.ScrollToItemById(itemId));

            return true;
        }

        Action<VisualElement, int> BindCellForDescriptionColumn()
        {
            return (element, rowIndex) =>
            {
                var label = element.Q<ObjectOrTypeLabel>();
                var itemData = m_TreeView.GetItemDataForIndex<AllTrackedMemoryModel.ItemData>(rowIndex);

                if (!label.TrySetLabelDataFromSourceIndexForTableEntry(m_Snapshot, itemData.Source, itemData.Name, out var displayName))
                {
                    label.SetLabelData(m_Snapshot, displayName, string.Empty);
                }

                var secondaryDisplayText = string.Empty;
                var childCount = itemData.ChildCount;
                if (childCount > 0)
                    secondaryDisplayText = $"({childCount:N0} Item{((childCount > 1) ? "s" : string.Empty)})";
                label.SetSecondaryText(secondaryDisplayText);

                if (itemData.Unreliable)
                {
                    label.tooltip = TextContent.GraphicsResidentMemoryUnreliableTooltip;
                    label.AddToClassList(k_UssClass_Cell_Unreliable);
                }
                else
                {
                    label.tooltip = string.Empty;
                    label.RemoveFromClassList(k_UssClass_Cell_Unreliable);
                }
            };
        }

        Action<VisualElement, int> BindCellForStatusColumn()
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<AllTrackedMemoryModel.ItemData>(rowIndex);
                element.Q<StatusCell>().SetStatus(itemData.WarningInfo, itemData.ChildCount);
            };
        }

        protected override void OnTreeItemSelected(int itemId, AllTrackedMemoryModel.ItemData itemData)
        {
            m_Responder?.SelectedItem(itemId, this, itemData);
        }

        public interface IResponder
        {
            // Invoked when an item is selected in the table. Arguments are the view controller, and the item's data.
            void SelectedItem(
                int itemId,
                AllTrackedMemoryTableViewController viewController,
                AllTrackedMemoryModel.ItemData itemData);

            // Invoked after the table has been reloaded. Success argument is true if a model was successfully built or false it there was an error when building the model.
            void Reloaded(AllTrackedMemoryTableViewController viewController, bool success);
        }
    }
}

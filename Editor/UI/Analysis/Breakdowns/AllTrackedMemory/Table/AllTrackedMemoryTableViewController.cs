using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class AllTrackedMemoryModelBuilderAdapter : IModelBuilder<AllTrackedMemoryModel, AllTrackedMemoryModelBuilder.BuildArgs>
    {
        readonly AllTrackedMemoryModelBuilder m_Builder = new AllTrackedMemoryModelBuilder();

        public AllTrackedMemoryModel Build(CachedSnapshot snapshot, AllTrackedMemoryModelBuilder.BuildArgs args)
        {
            return m_Builder.Build(snapshot, args);
        }

        public AllTrackedMemoryModel Build(AllTrackedMemoryModel baseModel, List<int> treeNodeIdFilter)
        {
            return m_Builder.Build(baseModel, treeNodeIdFilter);
        }
    }

    class AllTrackedMemoryTableViewController : SingleSnapshotTreeViewController<AllTrackedMemoryModel, AllTrackedMemoryModel.ItemData>, IAnalysisViewSelectable
    {
        const string k_UxmlAssetGuid = "e7ac30fe2b076984e978d41347c5f0e0";

        protected override string UxmlIdentifier_TreeViewColumn__Size => k_UxmlIdentifier_TreeViewColumn__Size;
        protected override string UxmlIdentifier_TreeViewColumn__ResidentSize => k_UxmlIdentifier_TreeViewColumn__ResidentSize;
        const string k_UssClass_Dark = "all-tracked-memory-table-view__dark";
        const string k_UssClass_Light = "all-tracked-memory-table-view__light";
        const string k_UssClass_Cell_Unreliable = "analysis-view__text__information-unreliable-or-unavailable";
        const string k_UxmlIdentifier_TreeView = "all-tracked-memory-table-view__tree-view";
        const string k_UxmlIdentifier_TreeViewColumn__Description = "all-tracked-memory-table-view__tree-view__column__description";
        const string k_UxmlIdentifier_TreeViewColumn__Size = "all-tracked-memory-table-view__tree-view__column__size";
        const string k_UxmlIdentifier_TreeViewColumn__SizeBar = "all-tracked-memory-table-view__tree-view__column__size-bar";
        const string k_UxmlIdentifier_TreeViewColumn__ResidentSize = "all-tracked-memory-table-view__tree-view__column__resident-size";
        const string k_UxmlIdentifier_LoadingOverlay = "all-tracked-memory-table-view__loading-overlay";
        const string k_UxmlIdentifier_ErrorLabel = "all-tracked-memory-table-view__error-label";
        const string k_NotAvailable = "N/A";
        const string k_UnreliableTooltip = "The memory profiler cannot certainly attribute which part of the " +
            "resident memory belongs to graphics, as some of it might be included in the \"untracked\" memory.\n\n" +
            "Change focus to \"Allocated Memory\" to inspect graphics in detail.\n\n" +
            "We also recommend using a platform profiler for checking the residency status of graphics memory.";

        // Model.
        readonly CachedSnapshot m_Snapshot;
        readonly bool m_CompareMode;
        readonly bool m_DisambiguateUnityObjects;
        readonly IResponder m_Responder;
        protected override Dictionary<string, Comparison<TreeViewItemData<AllTrackedMemoryModel.ItemData>>> SortComparisons { get; }

        // Composed components
        readonly TableFilterManager<AllTrackedMemoryModel, AllTrackedMemoryModel.ItemData> m_FilterManager;
        readonly ModelBuildOrchestrator<AllTrackedMemoryModel, AllTrackedMemoryModelBuilder.BuildArgs> m_BuildOrchestrator;
        TreeViewColumnManager<AllTrackedMemoryModel.ItemData> m_ColumnManager;
        SizeCellRenderer m_SizeCellRenderer;

        // View.
        int? m_SelectAfterLoadItemId;

        public AllTrackedMemoryTableViewController(
            CachedSnapshot snapshot,
            ToolbarSearchField searchField = null,
            bool buildOnLoad = true,
            bool compareMode = false,
            bool disambiguateUnityObjects = false,
            IResponder responder = null)
            : base(idOfDefaultColumnWithPercentageBasedWidth: null, buildOnLoad)
        {
            m_Snapshot = snapshot;
            m_SearchField = searchField;
            m_CompareMode = compareMode;
            m_DisambiguateUnityObjects = disambiguateUnityObjects;
            m_Responder = responder;

            m_SelectAfterLoadItemId = null;

            // Initialize composed components
            m_FilterManager = new TableFilterManager<AllTrackedMemoryModel, AllTrackedMemoryModel.ItemData>();
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
            bool excludeAll = false)
        {
            // SetFilters is a listener to UI input (search filter changes) and therefore doesn't expect an awaitable task.
            // No caller or following code expects any part of the asnyc build to be done.
            // Therefore: Discard the returned task.
            _ = SetFiltersAsync(model, searchFilter, itemPathFilter, excludeAll);
        }

        public async Task SetFiltersAsync(
            AllTrackedMemoryModel model = null,
            IScopedFilter<string> searchFilter = null,
            IEnumerable<ITextFilter> itemPathFilter = null,
            bool excludeAll = false)
        {
            m_FilterManager.SearchFilter = searchFilter;
            m_FilterManager.PathFilters = itemPathFilter;
            m_FilterManager.TreeNodeIdFilter = excludeAll ? new List<int>() : null;
            m_FilterManager.BaseModelForTreeNodeIdFiltering = model;

            // Update base class members for compatibility
            m_TreeNodeIdFilter = m_FilterManager.TreeNodeIdFilter;
            m_BaseModelForTreeNodeIdFiltering = m_FilterManager.BaseModelForTreeNodeIdFiltering;

            await m_FilterManager.ApplyFiltersAsync(() => BuildModelAsync(false), IsViewLoaded);
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
            view.style.flexGrow = 1;

            var themeUssClass = (EditorGUIUtility.isProSkin) ? k_UssClass_Dark : k_UssClass_Light;
            view.AddToClassList(themeUssClass);

            GatherReferencesInView(view);

            return view;
        }

        protected override void Dispose(bool disposing)
        {
            MemoryProfilerSettings.AllocationRootsToSplitChanged -= OnAllocationRootsToSplitChanged;
            base.Dispose(disposing);
        }

        void GatherReferencesInView(VisualElement view)
        {
            m_TreeView = view.Q<MultiColumnTreeView>(k_UxmlIdentifier_TreeView);
            m_LoadingOverlay = view.Q<ActivityIndicatorOverlay>(k_UxmlIdentifier_LoadingOverlay);
            m_ErrorLabel = view.Q<Label>(k_UxmlIdentifier_ErrorLabel);

            // Initialize components that depend on m_TreeView
            m_ColumnManager = new TreeViewColumnManager<AllTrackedMemoryModel.ItemData>(m_TreeView);
            m_SizeCellRenderer = new SizeCellRenderer(m_TreeView, () => TableMode, k_NotAvailable, k_UssClass_Cell_Unreliable);
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
                "Description",
                0,
                BindCellForDescriptionColumn(),
                AllTrackedMemoryDescriptionCell.Instantiate);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Size,
                "Allocated Size",
                120,
                m_SizeCellRenderer.CreateMemorySizeBinding<AllTrackedMemoryModel.ItemData>(
                    item => item.TotalSize,
                    item => item.Unreliable,
                    k_UnreliableTooltip),
                visible: TableMode != AllTrackedMemoryTableMode.OnlyResident);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__SizeBar,
                "% Impact",
                180,
                m_SizeCellRenderer.CreateMemoryBarBinding<AllTrackedMemoryModel.ItemData>(
                    item => item.TotalSize,
                    () => TableMode != AllTrackedMemoryTableMode.OnlyResident
                        ? new MemorySize(m_Model.TotalMemorySize.Committed, m_Model.TotalMemorySize.Committed)
                        : new MemorySize(m_Model.TotalMemorySize.Resident, m_Model.TotalMemorySize.Resident),
                    item => item.Unreliable),
                m_SizeCellRenderer.MakeMemoryBarCell);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__ResidentSize,
                "Resident Size",
                120,
                m_SizeCellRenderer.CreateMemorySizeBinding<AllTrackedMemoryModel.ItemData>(
                    item => item.TotalSize,
                    item => item.Unreliable,
                    k_UnreliableTooltip,
                    useResident: true),
                visible: CanShowResidentMemory() && TableMode != AllTrackedMemoryTableMode.OnlyCommitted);
        }

        protected AllTrackedMemoryModelBuilder.BuildArgs GetModelBuilderArgs()
        {
            return new AllTrackedMemoryModelBuilder.BuildArgs(
                m_FilterManager.SearchFilter,
                m_FilterManager.PathFilters,
                m_FilterManager.ExcludeAllFilterApplied,
                MemoryProfilerSettings.ShowReservedMemoryBreakdown,
                m_DisambiguateUnityObjects,
                TableMode == AllTrackedMemoryTableMode.OnlyCommitted,
                ProcessObjectSelected,
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

        protected override void RefreshView()
        {
            base.RefreshView();

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
            const string k_NoName = "<No Name>";
            return (element, rowIndex) =>
            {
                var cell = (AllTrackedMemoryDescriptionCell)element;
                var itemData = m_TreeView.GetItemDataForIndex<AllTrackedMemoryModel.ItemData>(rowIndex);

                var displayText = itemData.Name;
                if (string.IsNullOrEmpty(displayText))
                    displayText = k_NoName;
                cell.SetText(displayText);

                var secondaryDisplayText = string.Empty;
                var childCount = itemData.ChildCount;
                if (childCount > 0)
                    secondaryDisplayText = $"({childCount:N0} Item{((childCount > 1) ? "s" : string.Empty)})";
                cell.SetSecondaryText(secondaryDisplayText);

                if (itemData.Unreliable)
                {
                    cell.tooltip = k_UnreliableTooltip;
                    cell.AddToClassList(k_UssClass_Cell_Unreliable);
                }
                else
                {
                    cell.tooltip = string.Empty;
                    cell.RemoveFromClassList(k_UssClass_Cell_Unreliable);
                }
            };
        }

        protected override void OnTreeItemSelected(int itemId, AllTrackedMemoryModel.ItemData itemData)
        {
            // Invoke the selection processor for the selected item.
            m_Model.SelectionProcessor?.Invoke(itemId, itemData);
        }

        void ProcessObjectSelected(int itemId, AllTrackedMemoryModel.ItemData itemData)
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

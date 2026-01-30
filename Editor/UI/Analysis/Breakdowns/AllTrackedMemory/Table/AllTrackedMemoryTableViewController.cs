using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
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

        public IScopedFilter<string> SearchFilter { get; private set; }
        // TODO: this looks to be unused and can probably be removed?
        public ITextFilter ItemNameFilter { get; private set; }

        public IEnumerable<ITextFilter> ItemPathFilter { get; private set; }

        ToolbarSearchField m_SearchField = null;
        protected override ToolbarSearchField SearchField => m_SearchField;

        void OnSearchFilterChanged(IScopedFilter<string> searchFilter)
        {
            SetFilters(searchFilter: searchFilter);
        }

        public void SetFilters(
            AllTrackedMemoryModel model = null,
            IScopedFilter<string> searchFilter = null,
            ITextFilter itemNameFilter = null,
            IEnumerable<ITextFilter> itemPathFilter = null,
            bool excludeAll = false)
        {
            // SetFilters is a listener to UI input (search filter changes) and therefore doesn't expect an awaitable task.
            // No caller or following code expects any part of the asnyc build to be done.
            // Therefore: Discard the returned task.
            _ = SetFiltersAsync(model, searchFilter, itemNameFilter, itemPathFilter, excludeAll);
        }

        public async Task SetFiltersAsync(
            AllTrackedMemoryModel model = null,
            IScopedFilter<string> searchFilter = null,
            ITextFilter itemNameFilter = null,
            IEnumerable<ITextFilter> itemPathFilter = null,
            bool excludeAll = false)
        {
            SearchFilter = searchFilter;
            ItemNameFilter = itemNameFilter;
            ItemPathFilter = itemPathFilter;
            m_TreeNodeIdFilter = excludeAll ? new List<int>() : null;
            // TODO: do not fully rebuild if a model was supplied
            if (IsViewLoaded)
                await BuildModelAsync(false);
        }

        void OnAllocationRootsToSplitChanged(string[] allocationRootsToSplit)
        {
            if (IsViewLoaded)
                // OnAllocationRootsToSplitChanged is a listener to UI input and therefore doesn't expect an awaitable task.
                // No caller or following code expects any part of the asnyc build to be done.
                // Therefore: Discard the returned task.
                _ = BuildModelAsync(false);
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
        }

        bool CanShowResidentMemory()
        {
            return m_Snapshot.HasSystemMemoryResidentPages && !m_CompareMode;
        }

        protected override void ConfigureTreeView()
        {
            base.ConfigureTreeView();

            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__Description, "Description", 0, BindCellForDescriptionColumn(), AllTrackedMemoryDescriptionCell.Instantiate);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__Size, "Allocated Size", 120, BindCellForSizeColumn(), visible: TableMode != AllTrackedMemoryTableMode.OnlyResident);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__SizeBar, "% Impact", 180, BindCellForMemoryBarColumn(), MakeMemoryBarCell);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__ResidentSize, "Resident Size", 120, BindCellForResidentSizeColumn(), visible: CanShowResidentMemory() && TableMode != AllTrackedMemoryTableMode.OnlyCommitted);
        }

        void ConfigureTreeViewColumn(string columnName, string columnTitle, int width, Action<VisualElement, int> bindCell, Func<VisualElement> makeCell = null, bool visible = true)
        {
            var column = m_TreeView.columns[columnName];
            column.title = columnTitle;
            column.bindCell = bindCell;
            column.visible = visible;
            if (width != 0)
            {
                column.width = width;
                column.minWidth = width / 2;
                column.maxWidth = width * 2;
            }
            if (makeCell != null)
                column.makeCell = makeCell;
        }

        protected AllTrackedMemoryModelBuilder.BuildArgs GetModelBuilderArgs()
        {
            return new AllTrackedMemoryModelBuilder.BuildArgs(
                SearchFilter,
                ItemNameFilter,
                ItemPathFilter,
                ExcludeAllFilterApplied,
                MemoryProfilerSettings.ShowReservedMemoryBreakdown,
                m_DisambiguateUnityObjects,
                TableMode == AllTrackedMemoryTableMode.OnlyCommitted,
                ProcessObjectSelected,
                MemoryProfilerSettings.FeatureFlags.EnableDynamicAllocationBreakdown_2024_10 ? MemoryProfilerSettings.AllocationRootsToSplit : null);
        }

        protected override Func<AllTrackedMemoryModel> GetModelBuilderTask(CancellationToken cancellationToken)
        {
            // Capture all variables locally in case they are changed before the task is started
            var modelTypeName = typeof(UnityObjectsModel).ToString();
            var treeNodeIdFilter = m_TreeNodeIdFilter;
            var baseModel = m_BaseModelForTreeNodeIdFiltering;
            if (treeNodeIdFilter != null)
            {
                return () =>
                {
                    AsyncTaskHelper.DebugLogAsyncStep("Start Building (derived)                 " + modelTypeName);
                    cancellationToken.ThrowIfCancellationRequested();
                    AsyncTaskHelper.DebugLogAsyncStep("Start Building (derived) (not canceled)                 " + modelTypeName);

                    // Build the data model as a derivative of an existing model with only certain tree node ids present
                    var modelBuilder = new AllTrackedMemoryModelBuilder();
                    var model = modelBuilder.Build(baseModel, treeNodeIdFilter);

                    cancellationToken.ThrowIfCancellationRequested();
                    AsyncTaskHelper.DebugLogAsyncStep("Building Finished                 " + modelTypeName);
                    return model;
                };
            }

            var snapshot = m_Snapshot;
            var args = GetModelBuilderArgs();

            return () =>
                {
                    AsyncTaskHelper.DebugLogAsyncStep("Start Building                 " + modelTypeName);
                    cancellationToken.ThrowIfCancellationRequested();
                    AsyncTaskHelper.DebugLogAsyncStep("Start Building (not canceled)                 " + modelTypeName);
                    // Build the data model.
                    var modelBuilder = new AllTrackedMemoryModelBuilder();
                    var model = modelBuilder.Build(snapshot, args);

                    cancellationToken.ThrowIfCancellationRequested();

                    AsyncTaskHelper.DebugLogAsyncStep("Building Finished                 " + modelTypeName);
                    return model;
                };
        }

        protected override void OnViewReloaded(bool success)
        {
            base.OnViewReloaded(success);
            // Notify responder.
            m_Responder?.Reloaded(this, success);
            // Update usage counters
            MemoryProfilerAnalytics.AddAllTrackedMemoryUsage(SearchFilter != null, MemoryProfilerSettings.ShowReservedMemoryBreakdown, TableMode);
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

        Action<VisualElement, int> BindCellForSizeColumn()
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<AllTrackedMemoryModel.ItemData>(rowIndex);
                var size = itemData.TotalSize.Committed;
                var cell = (Label)element;

                if (!itemData.Unreliable)
                {
                    cell.text = EditorUtility.FormatBytes((long)size);
                    cell.tooltip = $"{itemData.TotalSize.Committed:N0} B";
                    cell.displayTooltipWhenElided = false;
                    cell.RemoveFromClassList(k_UssClass_Cell_Unreliable);
                }
                else
                {
                    cell.text = k_NotAvailable;
                    cell.tooltip = k_UnreliableTooltip;
                    cell.AddToClassList(k_UssClass_Cell_Unreliable);
                }
            };
        }

        Action<VisualElement, int> BindCellForResidentSizeColumn()
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<AllTrackedMemoryModel.ItemData>(rowIndex);
                var size = itemData.TotalSize.Resident;
                var cell = (Label)element;

                if (!itemData.Unreliable)
                {
                    cell.text = EditorUtility.FormatBytes((long)size);
                    cell.tooltip = $"{size:N0} B";
                    cell.displayTooltipWhenElided = false;
                    cell.RemoveFromClassList(k_UssClass_Cell_Unreliable);
                }
                else
                {
                    cell.text = k_NotAvailable;
                    cell.tooltip = k_UnreliableTooltip;
                    cell.AddToClassList(k_UssClass_Cell_Unreliable);
                }
            };
        }

        Action<VisualElement, int> BindCellForMemoryBarColumn()
        {
            return (element, rowIndex) =>
            {
                var maxValue = TableMode != AllTrackedMemoryTableMode.OnlyResident ?
                    m_Model.TotalMemorySize.Committed : m_Model.TotalMemorySize.Resident;

                var item = m_TreeView.GetItemDataForIndex<AllTrackedMemoryModel.ItemData>(rowIndex);
                var cell = element as MemoryBar;

                if (!item.Unreliable)
                    cell.Set(item.TotalSize, maxValue, maxValue);
                else
                    cell.SetEmpty();
            };
        }

        VisualElement MakeMemoryBarCell()
        {
            var bar = new MemoryBar();
            bar.Mode = TableMode switch
            {
                AllTrackedMemoryTableMode.OnlyCommitted => MemoryBarElement.VisibilityMode.CommittedOnly,
                AllTrackedMemoryTableMode.OnlyResident => MemoryBarElement.VisibilityMode.ResidentOnly,
                AllTrackedMemoryTableMode.CommittedAndResident => MemoryBarElement.VisibilityMode.CommittedAndResident,
                _ => throw new NotImplementedException(),
            };
            return bar;
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

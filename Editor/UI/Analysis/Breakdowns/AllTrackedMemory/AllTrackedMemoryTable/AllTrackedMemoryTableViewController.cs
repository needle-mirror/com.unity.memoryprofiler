#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class AllTrackedMemoryTableViewController : TreeViewController<AllTrackedMemoryModel, AllTrackedMemoryModel.ItemData>, IAnalysisViewSelectable
    {
        const string k_UxmlAssetGuid = "e7ac30fe2b076984e978d41347c5f0e0";

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
        const string k_ErrorMessage = "Snapshot is from an outdated Unity version that is not fully supported.";
        const string k_NotAvailable = "N/A";
        const string k_UnreliableTooltip = "The memory profiler cannot certainly attribute which part of the " +
            "resident memory belongs to graphics, as some of it might be included in the \"untracked\" memory.\n\n" +
            "Change focus to \"Allocated Memory\" to inspect graphics in detail.\n\n" +
            "We also recommend using a platform profiler for checking the residency status of graphics memory.";

        // Model.
        readonly CachedSnapshot m_Snapshot;
        readonly bool m_BuildOnLoad;
        readonly bool m_CompareMode;
        readonly bool m_DisambiguateUnityObjects;
        readonly IResponder m_Responder;
        readonly Dictionary<string, Comparison<TreeViewItemData<AllTrackedMemoryModel.ItemData>>> m_SortComparisons;
        // View.
        int? m_SelectAfterLoadItemId;
        AllTrackedMemoryTableMode m_TableMode;
        ActivityIndicatorOverlay m_LoadingOverlay;
        Label m_ErrorLabel;

        public AllTrackedMemoryTableViewController(
            CachedSnapshot snapshot,
            ToolbarSearchField searchField = null,
            bool buildOnLoad = true,
            bool compareMode = false,
            bool disambiguateUnityObjects = false,
            IResponder responder = null)
            : base(idOfDefaultColumnWithPercentageBasedWidth: null)
        {
            m_Snapshot = snapshot;
            m_SearchField = searchField;
            m_BuildOnLoad = buildOnLoad;
            m_CompareMode = compareMode;
            m_DisambiguateUnityObjects = disambiguateUnityObjects;
            m_Responder = responder;

            m_SelectAfterLoadItemId = null;
            m_TableMode = AllTrackedMemoryTableMode.CommittedAndResident;

            SearchFilterChanged += OnSearchFilterChanged;
            MemoryProfilerSettings.AllocationRootsToSplitChanged += OnAllocationRootsToSplitChanged;

            // Sort comparisons for each column.
            m_SortComparisons = new()
            {
                { k_UxmlIdentifier_TreeViewColumn__Description, (x, y) => string.Compare(x.data.Name, y.data.Name, StringComparison.OrdinalIgnoreCase) },
                { k_UxmlIdentifier_TreeViewColumn__Size, (x, y) => x.data.Size.Committed.CompareTo(y.data.Size.Committed) },
                { k_UxmlIdentifier_TreeViewColumn__ResidentSize, (x, y) => x.data.Size.Resident.CompareTo(y.data.Size.Resident) },
                { k_UxmlIdentifier_TreeViewColumn__SizeBar, (x, y) => m_TableMode != AllTrackedMemoryTableMode.OnlyResident ? x.data.Size.Committed.CompareTo(y.data.Size.Committed) : x.data.Size.Resident.CompareTo(y.data.Size.Resident) },
            };
        }

        public AllTrackedMemoryModel Model => m_Model;

        public IScopedFilter<string> SearchFilter { get; private set; }

        public ITextFilter ItemNameFilter { get; private set; }

        public IEnumerable<ITextFilter> ItemPathFilter { get; private set; }

        public bool ExcludeAll { get; private set; }

        ToolbarSearchField m_SearchField = null;
        protected override ToolbarSearchField SearchField => m_SearchField;

        void OnSearchFilterChanged(IScopedFilter<string> searchFilter)
        {
            SetFilters(searchFilter);
        }

        public void SetFilters(
            IScopedFilter<string> searchFilter = null,
            ITextFilter itemNameFilter = null,
            IEnumerable<ITextFilter> itemPathFilter = null,
            bool excludeAll = false)
        {
            SearchFilter = searchFilter;
            ItemNameFilter = itemNameFilter;
            ItemPathFilter = itemPathFilter;
            ExcludeAll = excludeAll;
            if (IsViewLoaded)
                BuildModelAsync();
        }

        void OnAllocationRootsToSplitChanged(string [] allocationRootsToSplit)
        {
            if (IsViewLoaded)
                BuildModelAsync();
        }

        public void SetColumnsVisibility(AllTrackedMemoryTableMode mode)
        {
            if (m_TableMode == mode)
                return;

            m_TableMode = mode;
            var columns = m_TreeView.columns;
            switch (mode)
            {
                case AllTrackedMemoryTableMode.OnlyResident:
                    columns[k_UxmlIdentifier_TreeViewColumn__Size].visible = false;
                    columns[k_UxmlIdentifier_TreeViewColumn__ResidentSize].visible = true;
                    break;
                case AllTrackedMemoryTableMode.OnlyCommitted:
                    columns[k_UxmlIdentifier_TreeViewColumn__Size].visible = true;
                    columns[k_UxmlIdentifier_TreeViewColumn__ResidentSize].visible = false;
                    break;
                case AllTrackedMemoryTableMode.CommittedAndResident:
                    columns[k_UxmlIdentifier_TreeViewColumn__Size].visible = true;
                    columns[k_UxmlIdentifier_TreeViewColumn__ResidentSize].visible = true;
                    break;
            }

            if (IsViewLoaded)
                BuildModelAsync();
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

        protected override void ViewLoaded()
        {
            ConfigureTreeView();

            if (m_BuildOnLoad)
                BuildModelAsync();
            else
                m_LoadingOverlay.Hide();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                m_BuildModelWorker?.Dispose();

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
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__Size, "Allocated Size", 120, BindCellForSizeColumn());
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__SizeBar, "% Impact", 180, BindCellForMemoryBarColumn(), MakeMemoryBarCell);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__ResidentSize, "Resident Size", 120, BindCellForResidentSizeColumn(), visible: CanShowResidentMemory());
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

        protected override void BuildModelAsync()
        {
            // Cancel existing build if necessary.
            m_BuildModelWorker?.Dispose();

            // Show loading UI.
            m_LoadingOverlay.Show();

            // Dispatch asynchronous build.
            // Note: AsyncWorker is executed on another thread and can't use MemoryProfilerSettings.ShowReservedMemoryBreakdown.
            // We retrieve global setting immediately while on a main thread and pass it by value to the worker
            var snapshot = m_Snapshot;
            var args = new AllTrackedMemoryModelBuilder.BuildArgs(
                SearchFilter,
                ItemNameFilter,
                ItemPathFilter,
                ExcludeAll,
                MemoryProfilerSettings.ShowReservedMemoryBreakdown,
                m_DisambiguateUnityObjects,
                m_TableMode == AllTrackedMemoryTableMode.OnlyCommitted,
                ProcessObjectSelected,
                MemoryProfilerSettings.FeatureFlags.EnableDynamicAllocationBreakdown_2024_10 ? MemoryProfilerSettings.AllocationRootsToSplit : null);
            var sortComparison = BuildSortComparisonFromTreeView();
            m_BuildModelWorker = new AsyncWorker<AllTrackedMemoryModel>();
            m_BuildModelWorker.Execute((token) =>
            {
                try
                {
                    // Build the data model.
                    var modelBuilder = new AllTrackedMemoryModelBuilder();
                    var model = modelBuilder.Build(snapshot, args);
                    token.ThrowIfCancellationRequested();
                    // Sort it according to the current sort descriptors.
                    model.Sort(sortComparison);

                    return model;
                }
                catch (UnsupportedSnapshotVersionException)
                {
                    return null;
                }
                catch (OperationCanceledException)
                {
                    // We expect a TaskCanceledException to be thrown when cancelling an in-progress builder. Do not log an error to the console.
                    return null;
                }
                catch (Exception _e)
                {
                    Debug.LogError($"{_e.Message}\n{_e.StackTrace}");
                    return null;
                }
            }, (model) =>
                {
                    // Update model.
                    m_Model = model;

                    var success = model != null;
                    if (success)
                    {
                        // Refresh UI with new data model.
                        RefreshView();
                    }
                    else
                    {
                        // Display error message.
                        m_ErrorLabel.text = k_ErrorMessage;
                        UIElementsHelper.SetElementDisplay(m_ErrorLabel, true);
                    }

                    // Hide loading UI.
                    m_LoadingOverlay.Hide();

                    // Notify responder.
                    m_Responder?.Reloaded(this, success);

                    // Dispose asynchronous worker.
                    m_BuildModelWorker.Dispose();

                    // Update usage counters
                    MemoryProfilerAnalytics.AddAllTrackedMemoryUsage(SearchFilter != null, MemoryProfilerSettings.ShowReservedMemoryBreakdown, m_TableMode);
                });
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
                var size = itemData.Size.Committed;
                var cell = (Label)element;

                if (!itemData.Unreliable)
                {
                    cell.text = EditorUtility.FormatBytes((long)size);
                    cell.tooltip = $"{itemData.Size.Committed:N0} B";
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
                var size = itemData.Size.Resident;
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
                var maxValue = m_TableMode != AllTrackedMemoryTableMode.OnlyResident ?
                    m_Model.TotalMemorySize.Committed : m_Model.TotalMemorySize.Resident;

                var item = m_TreeView.GetItemDataForIndex<AllTrackedMemoryModel.ItemData>(rowIndex);
                var cell = element as MemoryBar;

                if (!item.Unreliable)
                    cell.Set(item.Size, maxValue, maxValue);
                else
                    cell.SetEmpty();
            };
        }

        VisualElement MakeMemoryBarCell()
        {
            var bar = new MemoryBar();
            bar.Mode = m_TableMode switch
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

        Comparison<TreeViewItemData<AllTrackedMemoryModel.ItemData>> BuildSortComparisonFromTreeView()
        {
            var sortedColumns = m_TreeView.sortedColumns;
            if (sortedColumns == null)
                return null;

            var sortComparisons = new List<Comparison<TreeViewItemData<AllTrackedMemoryModel.ItemData>>>();
            foreach (var sortedColumnDescription in sortedColumns)
            {
                if (sortedColumnDescription == null)
                    continue;

                var sortComparison = m_SortComparisons[sortedColumnDescription.columnName];

                // Invert the comparison's input arguments depending on the sort direction.
                var sortComparisonWithDirection = (sortedColumnDescription.direction == SortDirection.Ascending) ? sortComparison : (x, y) => sortComparison(y, x);
                sortComparisons.Add(sortComparisonWithDirection);
            }

            return (x, y) =>
            {
                var result = 0;
                foreach (var sortComparison in sortComparisons)
                {
                    result = sortComparison.Invoke(x, y);
                    if (result != 0)
                        break;
                }

                return result;
            };
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
#endif

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
    class UnityObjectsModelBuilderAdapter : IModelBuilder<UnityObjectsModel, UnityObjectsModelBuilder.BuildArgs>
    {
        readonly UnityObjectsModelBuilder m_Builder = new UnityObjectsModelBuilder();

        public UnityObjectsModel EmptyModel => new UnityObjectsModel(new(), default);

        public UnityObjectsModel Build(CachedSnapshot snapshot, UnityObjectsModelBuilder.BuildArgs args)
        {
            return m_Builder.Build(snapshot, args);
        }

        public UnityObjectsModel Build(UnityObjectsModel baseModel, IList<int> treeNodeIdFilter)
        {
            return m_Builder.Build(baseModel, treeNodeIdFilter);
        }

        public void ClearIdToIndexCache()
        {
            m_Builder.ClearIdToIndexCache();
        }
    }

    class UnityObjectsTableViewController : SingleSnapshotTreeViewController<UnityObjectsModel, UnityObjectsModel.ItemData>, IFilterRelaxationTarget
    {
        /// <summary>Column visibility and mode changes only need a cell refresh, not a model rebuild (unlike AllTrackedMemory where switching committed/resident recomputes the model).</summary>
        protected override bool SwitchingTableModeRequiresRebuild { get; } = false;

        protected override string UxmlIdentifier_TreeViewColumn__Size => k_UxmlIdentifier_TreeViewColumn__Size;
        protected override string UxmlIdentifier_TreeViewColumn__ResidentSize => k_UxmlIdentifier_TreeViewColumn__ResidentSize;
        const string k_UxmlAssetGuid = "e43db30ef43ddfd44bea11154f274126";
        const string k_UssClass_Cell_Unreliable = "analysis-view__text__information-unreliable-or-unavailable";
        const string k_UxmlIdentifier_TreeView = "unity-objects-table-view__tree-view";
        const string k_UxmlIdentifier_TreeViewColumn__Description = "unity-objects-table-view__tree-view__column__description";
        const string k_UxmlIdentifier_TreeViewColumn__Size = "unity-objects-table-view__tree-view__column__size";
        const string k_UxmlIdentifier_TreeViewColumn__SizeBar = "unity-objects-table-view__tree-view__column__size-bar";
        const string k_UxmlIdentifier_TreeViewColumn__NativeSize = "unity-objects-table-view__tree-view__column__native-size";
        const string k_UxmlIdentifier_TreeViewColumn__ManagedSize = "unity-objects-table-view__tree-view__column__managed-size";
        const string k_UxmlIdentifier_TreeViewColumn__ResidentSize = "unity-objects-table-view__tree-view__column__resident-size";
        const string k_UxmlIdentifier_TreeViewColumn__GpuSize = "unity-objects-table-view__tree-view__column__gpu-size";
        const string k_UxmlIdentifier_TreeViewColumn__Status = "unity-objects-table-view__tree-view__column__status";
        const string k_UxmlIdentifier_Toolbar = "unity-objects-table-view__toolbar";
        const string k_UxmlIdentifier_FlattenToggle = "unity-objects-table-view__toolbar__flatten-toggle";
        const string k_UxmlIdentifier_ScaleBarsToggle = "unity-objects-table-view__toolbar__scale-bars-toggle";
        const string k_UxmlIdentifier_DuplicatesToggle = "unity-objects-table-view__toolbar__duplicates-toggle";
        const string k_UxmlIdentifier_LoadingOverlay = "unity-objects-table-view__loading-overlay";
        const string k_UxmlIdentifier_ErrorLabel = "unity-objects-table-view__error-label";
        const string k_UxmlIdentifier_Pager = "unity-objects-table-view__pager";
        const string k_Graphics_NotAvailable = "N/A";

        const string k_SessionStateKey_FlattenHierarchy = "com.unity.memoryprofiler.UnityObjectsTableViewController.flattenHierarchy";
        const string k_SessionStateKey_ScaleBarsToParent = "com.unity.memoryprofiler.UnityObjectsTableViewController.scaleBarsToParent";
        const string k_SessionStateKey_ShowDuplicatesOnly = "com.unity.memoryprofiler.UnityObjectsTableViewController.showDuplicatesOnly";

        // Model.
        readonly CachedSnapshot m_Snapshot;
        readonly bool m_CompareMode;
        readonly IResponder m_Responder;
        protected override Dictionary<string, Comparison<TreeViewItemData<UnityObjectsModel.ItemData>>> SortComparisons { get; }
        /// <summary>
        /// Maps each sortable column to the compact-row sort key for the flattened view's native multi-column
        /// index sort. The Description (name) column is absent because name sorting goes through the model's
        /// managed path instead. The size-bar column resolves to committed/resident by table mode at sort time.
        /// </summary>
        readonly Dictionary<string, UnityObjectsFlatRowComparer.Column> m_FlatSortColumns;
        /// <summary>The paged model owns a native row store that must be disposed when it is replaced.</summary>
        UnityObjectsModel m_OwnedModel;
        FlatPagerBinding<UnityObjectsFlatRowComparer.Column> m_PagerBinding;
        bool m_ShowDuplicatesOnly;
        bool m_FlattenHierarchy;

        public override bool ExcludeAllFilterApplied => m_FilterManager.TreeNodeIdFilter != null && m_FilterManager.TreeNodeIdFilter.Count == 0;

        // Composed components
        readonly TableFilterManager<UnityObjectsModel, UnityObjectsModel.ItemData> m_FilterManager;
        readonly ModelBuildOrchestrator<UnityObjectsModel, UnityObjectsModelBuilder.BuildArgs> m_BuildOrchestrator;
        TreeViewColumnManager<UnityObjectsModel.ItemData> m_ColumnManager;
        protected override IColumnSumRefresher ColumnManager => m_ColumnManager;
        SizeCellRenderer m_SizeCellRenderer;

        // View.
        Toolbar m_Toolbar;
        Toggle m_FlattenToggle;
        Toggle m_DuplicatesToggle;
        PaginationControl m_Pager;
        const int k_DefaultColumnWidth = 120;
        const int k_SizeBarColumnWidth = 180;

        public UnityObjectsTableViewController(
            CachedSnapshot snapshot,
            ToolbarSearchField searchField = null,
            StatusFilterButton statusFilterButton = null,
            bool compareMode = false,
            bool disambiguateUnityObjects = false,
            IResponder responder = null)
            : base(idOfDefaultColumnWithPercentageBasedWidth: k_UxmlIdentifier_TreeViewColumn__Description)
        {
            m_Snapshot = snapshot;
            m_SearchField = searchField;
            m_StatusFilterButton = statusFilterButton;
            m_CompareMode = compareMode;
            m_Responder = responder;
            DisambiguateByInstanceId = disambiguateUnityObjects;

            // Initialize composed components
            m_FilterManager = new TableFilterManager<UnityObjectsModel, UnityObjectsModel.ItemData>();
            statusFilterButton?.SetUp(m_FilterManager, () => { if (IsViewLoaded) FireAndForgetBuildModelAsync(); }, ObjectStatusExtensions.UnityObjectsTableUnavailableStatuses);
            m_BuildOrchestrator = new ModelBuildOrchestrator<UnityObjectsModel, UnityObjectsModelBuilder.BuildArgs>(
                snapshot,
                new UnityObjectsModelBuilderAdapter(),
                typeof(UnityObjectsModel).ToString());

            SearchFilterChanged += OnSearchFilterChanged;

            // Sort comparisons for each column.
            SortComparisons = new()
            {
                { k_UxmlIdentifier_TreeViewColumn__Description, (x, y) => string.Compare(x.data.Name, y.data.Name, StringComparison.OrdinalIgnoreCase) },
                { k_UxmlIdentifier_TreeViewColumn__Size, (x, y) => x.data.TotalSize.Committed.CompareTo(y.data.TotalSize.Committed) },
                { k_UxmlIdentifier_TreeViewColumn__ResidentSize, (x, y) => x.data.TotalSize.Resident.CompareTo(y.data.TotalSize.Resident) },
                { k_UxmlIdentifier_TreeViewColumn__NativeSize, (x, y) => x.data.NativeSize.Committed.CompareTo(y.data.NativeSize.Committed) },
                { k_UxmlIdentifier_TreeViewColumn__ManagedSize, (x, y) => x.data.ManagedSize.Committed.CompareTo(y.data.ManagedSize.Committed) },
                { k_UxmlIdentifier_TreeViewColumn__GpuSize, (x, y) => x.data.GpuSize.Committed.CompareTo(y.data.GpuSize.Committed) },
                { k_UxmlIdentifier_TreeViewColumn__SizeBar, (x, y) => TableMode != AllTrackedMemoryTableMode.OnlyResident ? x.data.TotalSize.Committed.CompareTo(y.data.TotalSize.Committed) : x.data.TotalSize.Resident.CompareTo(y.data.TotalSize.Resident) },
                { k_UxmlIdentifier_TreeViewColumn__Status, (x, y) => x.data.WarningInfo.GetStatusSortKey().CompareTo(y.data.WarningInfo.GetStatusSortKey()) },
            };

            m_FlatSortColumns = new()
            {
                { k_UxmlIdentifier_TreeViewColumn__Size, UnityObjectsFlatRowComparer.Column.SizeCommitted },
                { k_UxmlIdentifier_TreeViewColumn__ResidentSize, UnityObjectsFlatRowComparer.Column.SizeResident },
                { k_UxmlIdentifier_TreeViewColumn__NativeSize, UnityObjectsFlatRowComparer.Column.NativeSize },
                { k_UxmlIdentifier_TreeViewColumn__ManagedSize, UnityObjectsFlatRowComparer.Column.ManagedSize },
                { k_UxmlIdentifier_TreeViewColumn__GpuSize, UnityObjectsFlatRowComparer.Column.GpuSize },
                { k_UxmlIdentifier_TreeViewColumn__Status, UnityObjectsFlatRowComparer.Column.Status },
            };
        }

        ToolbarSearchField m_SearchField = null;
        StatusFilterButton m_StatusFilterButton = null;
        protected override ToolbarSearchField SearchField => m_SearchField;

        public UnityObjectsModel Model => m_Model;

        public bool ShowDuplicatesOnly
        {
            get => m_ShowDuplicatesOnly;
            set
            {
                m_ShowDuplicatesOnly = value;
                SessionState.SetBool(k_SessionStateKey_ShowDuplicatesOnly, value);
                if (IsViewLoaded)
                    // ShowDuplicatesOnly is a property setter triggered by UI input and therefore doesn't expect an awaitable task.
                    // No caller or following code expects any part of the async build to be done.
                    // Therefore: Fire-and-forget with proper error handling.
                    FireAndForgetBuildModelAsync(false);
            }
        }

        public bool FlattenHierarchy
        {
            get => m_FlattenHierarchy;
            set
            {
                m_FlattenHierarchy = value;
                SessionState.SetBool(k_SessionStateKey_FlattenHierarchy, value);
                if (IsViewLoaded)
                    // FlattenHierarchy is a property setter triggered by UI input or startup config (before IsViewLoaded is true) and therefore doesn't expect an awaitable task.
                    // No caller or following code expects any part of the async build to be done.
                    // Therefore: Fire-and-forget with proper error handling.
                    FireAndForgetBuildModelAsync(false);
            }
        }

        public bool DisambiguateByInstanceId { get; private set; }

        // Filter properties - delegate to FilterManager with specific naming for UnityObjects
        public IScopedFilter<string> SearchStringFilter => m_FilterManager.SearchFilter;
        public ITextFilter UnityObjectNameFilter => m_FilterManager.NameFilter;
        public ITextFilter UnityObjectTypeNameFilter => m_FilterManager.TypeNameFilter;

        void OnSearchFilterChanged(IScopedFilter<string> searchFilter)
        {
            SetFilters(searchStringFilter: searchFilter);
        }

        public void SetFilters(
            UnityObjectsModel model = null,
            IScopedFilter<string> searchStringFilter = null,
            ITextFilter unityObjectNameFilter = null,
            ITextFilter unityObjectTypeNameFilter = null,
            IList<int> treeNodeIdFilter = null)
        {
            // SetFilters is a listener to UI input (search filter changes) and therefore doesn't expect an awaitable task.
            // No caller or following code expects any part of the asnyc build to be done.
            FireAndForgetFilterAsync(model, searchStringFilter, unityObjectNameFilter, unityObjectTypeNameFilter, treeNodeIdFilter);
        }

        async void FireAndForgetFilterAsync(
            UnityObjectsModel model = null,
            IScopedFilter<string> searchStringFilter = null,
            ITextFilter unityObjectNameFilter = null,
            ITextFilter unityObjectTypeNameFilter = null,
            IList<int> treeNodeIdFilter = null)
        {
            try
            {
                await SetFiltersAsync(model, searchStringFilter, unityObjectNameFilter, unityObjectTypeNameFilter, treeNodeIdFilter);
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
            UnityObjectsModel model = null,
            IScopedFilter<string> searchStringFilter = null,
            ITextFilter unityObjectNameFilter = null,
            ITextFilter unityObjectTypeNameFilter = null,
            IList<int> treeNodeIdFilter = null)
        {
            // Store all filters in FilterManager
            m_FilterManager.SearchFilter = searchStringFilter;
            m_FilterManager.NameFilter = unityObjectNameFilter;
            m_FilterManager.TypeNameFilter = unityObjectTypeNameFilter;

            if (model != m_FilterManager.BaseModelForTreeNodeIdFiltering)
                m_BuildOrchestrator.ClearIdToIndexCache();
            m_FilterManager.BaseModelForTreeNodeIdFiltering = model;

            // Set tree node filter for exclude-all scenario
            // Set an empty tree node id filter if the goal is to exclude everything
            m_FilterManager.TreeNodeIdFilter = treeNodeIdFilter;

            await m_FilterManager.ApplyFiltersAsync(m_FilterManager.TreeNodeIdFilter != null ?
                    () => FilterModelAsync(m_FilterManager.BaseModelForTreeNodeIdFiltering, m_FilterManager.TreeNodeIdFilter, m_BuildOrchestrator) :
                    () => BuildModelAsync(false), IsViewLoaded);
        }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");

            GatherReferencesInView(view);

            return view;
        }

        protected override void ViewLoaded()
        {
            base.ViewLoaded();

            m_FlattenHierarchy = SessionState.GetBool(k_SessionStateKey_FlattenHierarchy, false);
            m_FlattenToggle.SetValueWithoutNotify(m_FlattenHierarchy);
            m_FlattenToggle.text = TextContent.FlattenHierarchy;
            m_FlattenToggle.RegisterValueChangedCallback(OnFlattenHierarchyToggleValueChanged);
            m_ShowDuplicatesOnly = SessionState.GetBool(k_SessionStateKey_ShowDuplicatesOnly, false);
            m_DuplicatesToggle.SetValueWithoutNotify(m_ShowDuplicatesOnly);
            m_DuplicatesToggle.text = TextContent.ShowPotentialDuplicatesOnly;
            m_DuplicatesToggle.tooltip = TextContent.ShowPotentialDuplicatesOnlyTooltip;
            m_DuplicatesToggle.RegisterValueChangedCallback(OnShowDuplicatesOnlyToggleValueChanged);

            if (m_CompareMode)
                UIElementsHelper.SetElementDisplay(m_Toolbar, false);
        }

        void GatherReferencesInView(VisualElement view)
        {
            m_TreeView = view.Q<MultiColumnTreeView>(k_UxmlIdentifier_TreeView);
            m_Toolbar = view.Q<Toolbar>(k_UxmlIdentifier_Toolbar);
            m_FlattenToggle = view.Q<Toggle>(k_UxmlIdentifier_FlattenToggle);
            m_DuplicatesToggle = view.Q<Toggle>(k_UxmlIdentifier_DuplicatesToggle);
            m_Pager = view.Q<PaginationControl>(k_UxmlIdentifier_Pager);
            m_LoadingOverlay = view.Q<ActivityIndicatorOverlay>(k_UxmlIdentifier_LoadingOverlay);
            m_ErrorLabel = view.Q<Label>(k_UxmlIdentifier_ErrorLabel);

            // Initialize components that depend on m_TreeView
            m_ColumnManager = new TreeViewColumnManager<UnityObjectsModel.ItemData>(m_TreeView);
            m_SizeCellRenderer = new SizeCellRenderer(m_TreeView, () => TableMode, k_Graphics_NotAvailable, k_UssClass_Cell_Unreliable);
            m_SizeCellRenderer.SetUpScaleBarsToParentToggle(view.Q<Toggle>(k_UxmlIdentifier_ScaleBarsToggle), k_SessionStateKey_ScaleBarsToParent);

            m_PagerBinding = new FlatPagerBinding<UnityObjectsFlatRowComparer.Column>(
                m_Pager,
                m_ColumnManager.SetColumnTooltip,
                k_UxmlIdentifier_TreeViewColumn__Description,
                k_UxmlIdentifier_TreeViewColumn__SizeBar,
                m_FlatSortColumns,
                GetDefaultFlatSortColumn,
                TextContent.UnityObjectsNameColumnHeaderTooltip,
                UnityObjectsFlatRowComparer.k_MaxColumns,
                () => m_Model,
                () =>
                {
                    RefreshTreeViewRoots();
                    // A page flip changes the page totals, and SetRootItems bypasses RefreshView.
                    m_ColumnManager.RefreshColumnSums();
                });
        }

        protected override void ConfigureTreeView()
        {
            base.ConfigureTreeView();

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Description,
                TextContent.DescriptionTableColumnHeader,
                0,
                BindCellForDescriptionColumn(),
                makeCell: () => MakeLabelWithSelectInTableMenu(hideTypeInfoForObjects: true),
                destroyCell: cell => cell.Q<ObjectOrTypeLabel>().Dispose(),
                headerTooltip: TextContent.UnityObjectsNameColumnHeaderTooltip);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Size,
                TextContent.AllocatedSizeTableColumnHeader,
                k_DefaultColumnWidth,
                m_SizeCellRenderer.CreateMemorySizeBinding<UnityObjectsModel.ItemData>(
                    item => item.TotalSize,
                    useResident: false),
                visible: TableMode != AllTrackedMemoryTableMode.OnlyResident,
                sumProvider: () => m_Model?.TotalMemorySize.Committed ?? 0);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__SizeBar,
                TextContent.PercentImpactTableColumnHeader,
                k_SizeBarColumnWidth,
                m_SizeCellRenderer.CreateMemoryBarBinding<UnityObjectsModel.ItemData>(
                    item => item.TotalSize,
                    () => TableMode != AllTrackedMemoryTableMode.OnlyResident
                        ? new MemorySize(m_Model.TotalMemorySize.Committed, m_Model.TotalMemorySize.Committed)
                        : new MemorySize(m_Model.TotalMemorySize.Resident, m_Model.TotalMemorySize.Resident)),
                makeCell: m_SizeCellRenderer.MakeMemoryBarCell);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__NativeSize,
                TextContent.NativeSizeTableColumnHeader,
                k_DefaultColumnWidth,
                m_SizeCellRenderer.CreateMemorySizeBinding<UnityObjectsModel.ItemData>(
                    item => item.NativeSize),
                visible: !m_CompareMode,
                pagedSumProvider: () => PagedSumOverTypesOrObjects(item => item.NativeSize, model => model.TotalNativeSize));

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__ManagedSize,
                TextContent.ManagedSizeTableColumnHeader,
                k_DefaultColumnWidth,
                m_SizeCellRenderer.CreateMemorySizeBinding<UnityObjectsModel.ItemData>(
                    item => item.ManagedSize),
                visible: !m_CompareMode,
                pagedSumProvider: () => PagedSumOverTypesOrObjects(item => item.ManagedSize, model => model.TotalManagedSize));

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__ResidentSize,
                TextContent.ResidentSizeTableColumnHeader,
                k_DefaultColumnWidth,
                m_SizeCellRenderer.CreateMemorySizeBinding<UnityObjectsModel.ItemData>(
                    item => item.TotalSize,
                    useResident: true),
                visible: m_Snapshot.HasSystemMemoryResidentPages && TableMode != AllTrackedMemoryTableMode.OnlyCommitted,
                sumProvider: () => m_Model?.TotalMemorySize.Resident ?? 0);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__GpuSize,
                TextContent.GraphicsSizeTableColumnHeader,
                k_DefaultColumnWidth,
                BindCellForGpuSizeColumn(),
                visible: !m_CompareMode,
                pagedSumProvider: PagedGpuSum);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Status,
                TextContent.StatusTableColumnHeader,
                k_DefaultColumnWidth,
                BindCellForStatusColumn(),
                makeCell: () => new StatusCell(),
                visible: true);
        }

        protected UnityObjectsModelBuilder.BuildArgs GetModelBuilderArgs()
        {
            return new UnityObjectsModelBuilder.BuildArgs(
                m_FilterManager.CompoundSearchFilter,
                m_FilterManager.NameFilter,
                m_FilterManager.TypeNameFilter,
                FlattenHierarchy,
                ShowDuplicatesOnly,
                DisambiguateByInstanceId);
        }

        protected override Func<UnityObjectsModel> GetModelBuilderTask(CancellationToken cancellationToken)
        {
            var args = GetModelBuilderArgs();
            return m_BuildOrchestrator.CreateBuildTask(
                args,
                m_FilterManager.BaseModelForTreeNodeIdFiltering,
                m_FilterManager.TreeNodeIdFilter,
                cancellationToken);
        }

        /// <summary>
        /// Sorting the flattened, paged model can't go through TreeModel.Sort (which only sees the current page).
        /// Re-order the native store by the active column(s) instead, then re-materialize the first page. The
        /// table header allows mixing the name (Description) column with size/status columns (up to k_MaxColumns
        /// of the latter) at any priority; SortFlatMixed honors that order, staying on the fast native comparer
        /// when name isn't involved, and dropping only the name level (not the whole sort) above
        /// <see cref="FlatPagingLimits.NameSortHardLimit"/>.
        /// </summary>
        protected override Func<Task<UnityObjectsModel>, UnityObjectsModel> GetModelSorterTask(CancellationToken cancellationToken)
        {
            if (!m_FlattenHierarchy)
                return base.GetModelSorterTask(cancellationToken);

            // Read the active sort columns on the main thread (UI access) before the async continuation runs.
            var sort = m_PagerBinding.ReadSortState(m_TreeView);

            return task =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var model = task.Result;
                if (model == null || !model.IsPaged)
                    return model;

                // This runs on a background thread-pool thread, so materialize: false defers materializing the
                // page until OnModelRebuild is back on the main thread.
                if (sort.Levels.Length > 0)
                {
                    model.SortFlatMixed(sort.Levels, materialize: false);
                }
                else
                {
                    // No active size/status/name sort: fall back to the default (Size, descending).
                    model.SortFlatMixed(new[] { new FlatSortLevel<UnityObjectsFlatRowComparer.Column> { Column = m_PagerBinding.DefaultColumn, Descending = true } }, materialize: false);
                }
                return model;
            };
        }

        /// <summary>Size column/bar sort key: committed in the default mode, resident when OnlyResident is active.</summary>
        UnityObjectsFlatRowComparer.Column GetDefaultFlatSortColumn() => TableMode != AllTrackedMemoryTableMode.OnlyResident
            ? UnityObjectsFlatRowComparer.Column.SizeCommitted
            : UnityObjectsFlatRowComparer.Column.SizeResident;

        protected override void OnModelRebuild(bool success, bool modelHasNewTreeViewIds, Exception exception = null)
        {
            // GetModelSorterTask above defers materializing the paged model's current page to here: the
            // background sort continuation runs on a thread-pool thread, and RootNodes is also read by the
            // UI-bound tree view, so mutating it off the main thread would race that read. Materialize now, on
            // the main thread, before base.OnModelRebuild reads it via RefreshView -> SetRootItems.
            if (success && m_Model != null && m_Model.IsPaged)
                m_Model.MaterializePage(m_Model.CurrentPage);

            base.OnModelRebuild(success, modelHasNewTreeViewIds, exception);

            if (!success || m_Model == null)
            {
                m_PagerBinding.SyncTo(null);
                return;
            }

            // Free the native store of the model this build replaced. A justSort reuses the same model instance
            // (m_Model == m_OwnedModel), so it is not disposed; only a full rebuild swaps it.
            DisposableModelSwap.ReplaceIfChanged(ref m_OwnedModel, m_Model);

            // The pager only applies to the flattened, paged model. SortFlat/the build reset CurrentPage to 0,
            // so the pager follows the model.
            m_PagerBinding.SyncTo(m_Model);
        }

        protected override void OnViewReloaded(bool success)
        {
            base.OnViewReloaded(success);
            // Notify responder.
            m_Responder?.UnityObjectsTableViewControllerReloaded(this, success);
            // Update usage counters
            MemoryProfilerAnalytics.AddUnityObjectsUsage(m_FilterManager.SearchFilter != null, FlattenHierarchy, ShowDuplicatesOnly, TableMode);
        }

        Action<VisualElement, int> BindCellForDescriptionColumn()
        {
            return (element, rowIndex) =>
            {
                var label = element.Q<ObjectOrTypeLabel>();
                var itemData = m_TreeView.GetItemDataForIndex<UnityObjectsModel.ItemData>(rowIndex);

                var displayText = itemData.Name;
                if (string.IsNullOrEmpty(displayText))
                    displayText = CachedSnapshot.InvalidItemName;

                if (!label.TrySetLabelDataFromSourceIndex(m_Snapshot, itemData.Source, displayText))
                {
                    label.SetLabelData(m_Snapshot, displayText, string.Empty);
                }

                var secondaryDisplayText = string.Empty;
                var childCount = itemData.ChildCount;
                if (childCount > 0)
                    secondaryDisplayText = $"({childCount:N0} Object{((childCount > 1) ? "s" : string.Empty)})";
                label.SetSecondaryText(secondaryDisplayText);
            };
        }

        /// <summary>
        /// The page and model-wide totals of a per-category size, committed or resident per the table mode.
        /// The page total sums the materialized top-level rows (types, or objects when flattened), which counts each
        /// object once since every object belongs to a single type. The unpaged model's page total is its model total.
        /// </summary>
        PagedColumnSum PagedSumOverTypesOrObjects(
            Func<UnityObjectsModel.ItemData, MemorySize> sizeSelector, Func<UnityObjectsModel, MemorySize> modelTotalSelector)
        {
            if (m_Model == null)
                return PagedColumnSum.Unpaged(0);

            var resident = TableMode == AllTrackedMemoryTableMode.OnlyResident;
            ulong Pick(MemorySize size) => resident ? size.Resident : size.Committed;

            var modelTotal = Pick(modelTotalSelector(m_Model));
            if (!m_Model.IsPaged)
                return PagedColumnSum.Unpaged(modelTotal);

            var pageTotal = m_Model.RootNodes.SumOverTopLevelRows(item => Pick(sizeSelector(item)));
            return new PagedColumnSum(pageTotal, modelTotal);
        }

        /// <summary>
        /// The GPU column's page and model-wide totals. GPU memory has no resident tracking, so the total is always
        /// committed and not applicable in the resident-only table mode, matching the column's cells.
        /// </summary>
        PagedColumnSum PagedGpuSum()
        {
            if (m_Model == null)
                return PagedColumnSum.Unpaged(0);

            if (TableMode == AllTrackedMemoryTableMode.OnlyResident)
                return default;

            var modelTotal = m_Model.TotalGpuSize.Committed;
            if (!m_Model.IsPaged)
                return PagedColumnSum.Unpaged(modelTotal);

            var pageTotal = m_Model.RootNodes.SumOverTopLevelRows(item => item.GpuSize.Committed);
            return new PagedColumnSum(pageTotal, modelTotal);
        }

        Action<VisualElement, int> BindCellForGpuSizeColumn()
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<UnityObjectsModel.ItemData>(rowIndex);
                var label = element.Q<Label>();

                if (TableMode == AllTrackedMemoryTableMode.OnlyResident)
                {
                    label.text = TextContent.NotAvailable;
                    label.tooltip = TextContent.GraphicsSizeNotAvailableTooltip;
                    label.AddToClassList(k_UssClass_Cell_Unreliable);
                }
                else
                {
                    var size = itemData.GpuSize.Committed;
                    label.text = EditorUtility.FormatBytes((long)size);
                    label.tooltip = $"{size:N0} B";
                    label.displayTooltipWhenElided = false;
                    label.RemoveFromClassList(k_UssClass_Cell_Unreliable);
                }
            };
        }

        Action<VisualElement, int> BindCellForStatusColumn()
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<UnityObjectsModel.ItemData>(rowIndex);
                element.Q<StatusCell>().SetStatus(itemData.WarningInfo, itemData.ChildCount);
            };
        }

        void OnFlattenHierarchyToggleValueChanged(ChangeEvent<bool> evt)
        {
            FlattenHierarchy = evt.newValue;
        }

        void OnShowDuplicatesOnlyToggleValueChanged(ChangeEvent<bool> evt)
        {
            ShowDuplicatesOnly = evt.newValue;
        }

        protected override void OnTreeItemSelected(int itemId, UnityObjectsModel.ItemData itemData)
        {
            m_Responder?.UnityObjectsTableViewControllerSelectedItem(itemId, this, itemData);
        }

        protected override void Dispose(bool disposing)
        {
            SearchFilterChanged -= OnSearchFilterChanged;
            if (disposing)
                m_PagerBinding?.Detach();

            // base.Dispose cancels and waits for any in-flight background sort/filter (AsyncModelBuildCoordinator)
            // before returning. That has to happen before the native-buffer release below, or a still-running
            // SortFlat/ApplyFilter could dereference memory this frees out from under it.
            base.Dispose(disposing);

            if (disposing)
            {
                // Frees the paged model's native row store.
                DisposableModelSwap.ReplaceIfChanged(ref m_OwnedModel, null);
                m_FilterManager.Dispose();
                m_BuildOrchestrator.Dispose();
            }
        }

        public override BreakdownTableId TableId => BreakdownTableId.UnityObjects;

        /// <summary>Whether <paramref name="id"/> is one this table can ever show, independent of any filter.</summary>
        internal static bool CanEverContainSourceId(CachedSnapshot.SourceIndex.SourceId id)
        {
            switch (id)
            {
                case CachedSnapshot.SourceIndex.SourceId.NativeType:
                case CachedSnapshot.SourceIndex.SourceId.NativeObject:
                case CachedSnapshot.SourceIndex.SourceId.ManagedType:
                    return true;
                default:
                    return false;
            }
        }

        public override bool CanEverContain(CachedSnapshot.SourceIndex source) => CanEverContainSourceId(source.Id) && HasNativeObjectsOfType(m_Snapshot, source);

        /// <summary>
        /// False for a type that has no group row because <paramref name="snapshot"/> has no native objects of it, e.g. a
        /// plain C# type or a MonoBehaviour type whose instances are all leaked shells. Managed types only group the objects
        /// of the native base types that <see cref="UnityObjectsModelBuilder"/> disambiguates by managed type.
        /// </summary>
        internal static bool HasNativeObjectsOfType(CachedSnapshot snapshot, CachedSnapshot.SourceIndex source)
        {
            var nativeObjects = snapshot.NativeObjects;
            switch (source.Id)
            {
                case CachedSnapshot.SourceIndex.SourceId.NativeType:
                    for (long i = 0; i < nativeObjects.Count; i++)
                    {
                        if (nativeObjects.NativeTypeArrayIndex[i] == source.Index)
                            return true;
                    }
                    return false;
                case CachedSnapshot.SourceIndex.SourceId.ManagedType:
                    var managedObjects = snapshot.CrawledData.ManagedObjects;
                    var nativeTypes = snapshot.NativeTypes;
                    for (long i = 0; i < nativeObjects.Count; i++)
                    {
                        var managedObjectIndex = nativeObjects.ManagedObjectIndex[i];
                        if (managedObjectIndex < 0 || managedObjects[managedObjectIndex].ITypeDescription != source.Index)
                            continue;
                        var nativeType = nativeObjects.NativeTypeArrayIndex[i];
                        if (nativeType == nativeTypes.MonoBehaviourIdx || nativeType == nativeTypes.ScriptableObjectIdx || nativeType == nativeTypes.EditorScriptableObjectIdx)
                            return true;
                    }
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Diagnoses the filter dimensions this table shares with every other table (status mask, text search
        /// against the item's own name); mirrors <see cref="RootsAndImpactTableViewController.ComputeVisibility"/>'s shape.
        /// </summary>
        public override ItemVisibility GetVisibility(CachedSnapshot.SourceIndex source, CachedSnapshot owner = null)
        {
            if (!CanEverContainSourceId(source.Id))
                return new ItemVisibility(ItemVisibility.State.NotRepresentable);
            if (!HasNativeObjectsOfType(m_Snapshot, source))
                return new ItemVisibility(ItemVisibility.State.NoRow);

            var blocking = TableRevealHelper.GetBlockingStatusAndOwnNameFilters(m_Snapshot, source, m_FilterManager.CompoundSearchFilter);

            // Whether the item is a potential duplicate depends on which items the other filters let through, so it
            // can't be diagnosed while they hide it; clear the duplicates filter along with them to guarantee the reveal.
            blocking = WithDuplicatesBlockerIfHidden(blocking, m_ShowDuplicatesOnly);

            if (blocking == TableFilterKind.None && m_ShowDuplicatesOnly)
            {
                if (m_Model == null)
                    return new ItemVisibility(ItemVisibility.State.Unknown);

                if (!HasRow(source))
                    blocking |= TableFilterKind.DuplicatesOnly;
            }

            return ItemVisibility.FromBlockingFilters(blocking);
        }

        /// <summary>Internal for testing purposes.</summary>
        internal static TableFilterKind WithDuplicatesBlockerIfHidden(TableFilterKind blocking, bool showDuplicatesOnly)
            => blocking != TableFilterKind.None && showDuplicatesOnly ? blocking | TableFilterKind.DuplicatesOnly : blocking;

        protected override bool HasRow(CachedSnapshot.SourceIndex source)
            => m_Model.IsPaged
                ? m_Model.TryFindStoreIndex(source, out _)
                : m_Model.RootNodes.TryFindItemPath(data => data.Source == source, out _, out _);

        protected override bool TryRevealRow(CachedSnapshot.SourceIndex source, bool frame, bool select, CachedSnapshot owner)
        {
            if (!m_Model.IsPaged)
                return TryRevealTreeRow(data => data.Source == source, frame, select);

            return m_Model.TryFindStoreIndex(source, out var storeIndex)
                && m_PagerBinding.TryShowRow(storeIndex, out var treeId)
                && RevealTreeItem(treeId, frame, select);
        }

        /// <summary>See <see cref="RootsAndImpactTableViewController.ApplyFilterRelaxation"/>; the duplicates toggle is the only table-specific dimension here.</summary>
        public void ApplyFilterRelaxation(TableFilterKind filtersToRelax)
        {
            TableRevealHelper.RelaxStatusAndTextFilters(filtersToRelax, m_StatusFilterButton, m_SearchField, m_FilterManager);
            if ((filtersToRelax & TableFilterKind.DuplicatesOnly) != 0)
            {
                m_ShowDuplicatesOnly = false;
                SessionState.SetBool(k_SessionStateKey_ShowDuplicatesOnly, false);
                m_DuplicatesToggle.SetValueWithoutNotify(false);
            }
            FireAndForgetBuildModelAsync();
        }

        public interface IResponder
        {
            // Invoked when a Unity Object item is selected in the table. Arguments are the view controller, the native object's instance id, and the item's data.
            void UnityObjectsTableViewControllerSelectedItem(
                int itemId,
                UnityObjectsTableViewController viewController,
                UnityObjectsModel.ItemData itemData);

            // Invoked after the table has been reloaded. Success argument is true if a model was successfully built or false it there was an error when building the model.
            void UnityObjectsTableViewControllerReloaded(
                UnityObjectsTableViewController viewController,
                bool success);
        }
    }
}

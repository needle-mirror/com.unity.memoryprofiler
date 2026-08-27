using System;
using System.Collections.Generic;
using System.Threading;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class RootsAndImpactModelBuilderAdapter : IModelBuilder<RootsAndImpactModel, RootsAndImpactModelBuilder.BuildArgs>
    {
        readonly RootsAndImpactModelBuilder m_Builder = new RootsAndImpactModelBuilder();

        public RootsAndImpactModel EmptyModel => new RootsAndImpactModel(new(), null, default, default);

        public RootsAndImpactModel Build(CachedSnapshot snapshot, RootsAndImpactModelBuilder.BuildArgs args)
        {
            return m_Builder.Build(snapshot, args);
        }

        public RootsAndImpactModel Build(RootsAndImpactModel baseModel, IList<int> treeNodeIdFilter)
        {
            return m_Builder.Build(baseModel, treeNodeIdFilter);
        }

        public void ClearIdToIndexCache()
        {
            m_Builder.ClearIdToIndexCache();
        }
    }

    class RootsAndImpactTableViewController : SingleSnapshotTreeViewController<RootsAndImpactModel, RootsAndImpactModel.ItemData>
    {
        protected override bool SwitchingTableModeRequiresRebuild { get; } = false;

        protected override string UxmlIdentifier_TreeViewColumn__Size => k_UxmlIdentifier_TreeViewColumn__TotalImpact;
        protected override string UxmlIdentifier_TreeViewColumn__ResidentSize => k_UxmlIdentifier_TreeViewColumn__TotalImpactResident;

        const string k_UxmlAssetGuid = "59dba2b22fc552c4bbc2b21420496888";
        const string k_UxmlIdentifier_TreeView = "roots-and-impact-table-view__tree-view";
        const string k_UxmlIdentifier_TreeViewColumn__Description = "roots-and-impact-table-view__tree-view__column__description";
        const string k_UxmlIdentifier_TreeViewColumn__TotalImpact = "roots-and-impact-table-view__tree-view__column__total-impact";
        const string k_UxmlIdentifier_TreeViewColumn__TotalImpactResident = "roots-and-impact-table-view__tree-view__column__total-impact-resident";
        const string k_UxmlIdentifier_TreeViewColumn__TotalImpactBar = "roots-and-impact-table-view__tree-view__column__total-impact-bar";
        const string k_UxmlIdentifier_TreeViewColumn__OwnSize = "roots-and-impact-table-view__tree-view__column__own-size";
        const string k_UxmlIdentifier_TreeViewColumn__ExclusiveImpact = "roots-and-impact-table-view__tree-view__column__exclusive-impact";
        const string k_UxmlIdentifier_TreeViewColumn__SharedProRata = "roots-and-impact-table-view__tree-view__column__shared-pro-rata";
        const string k_UxmlIdentifier_TreeViewColumn__SharedInTotal = "roots-and-impact-table-view__tree-view__column__shared-in-total";
        const string k_UxmlIdentifier_TreeViewColumn__NativeImpact = "roots-and-impact-table-view__tree-view__column__native-impact";
        const string k_UxmlIdentifier_TreeViewColumn__ManagedImpact = "roots-and-impact-table-view__tree-view__column__managed-impact";
        const string k_UxmlIdentifier_TreeViewColumn__GfxImpact = "roots-and-impact-table-view__tree-view__column__gfx-impact";
        const string k_UxmlIdentifier_TreeViewColumn__Status = "roots-and-impact-table-view__tree-view__column__status";
        const string k_UxmlIdentifier_Toolbar = "roots-and-impact-table-view__toolbar";
        const string k_UxmlIdentifier_FlattenToggle = "roots-and-impact-table-view__toolbar__flatten-toggle";
        const string k_UxmlIdentifier_ExclusiveToggle = "roots-and-impact-table-view__toolbar__exclusive-toggle";
        const string k_UxmlIdentifier_RegionDataToggle = "roots-and-impact-table-view__toolbar__region-data-toggle";
        const string k_UxmlIdentifier_LoadingOverlay = "roots-and-impact-table-view__loading-overlay";
        const string k_UxmlIdentifier_ErrorLabel = "roots-and-impact-table-view__error-label";
        const string k_UxmlIdentifier_Pager = "roots-and-impact-table-view__pager";

        const string k_UssClass_FilterMatchAncestor = "roots-and-impact-table-view__filter-match-ancestor";

        const string k_SessionStateKey_FlattenHierarchy = "com.unity.memoryprofiler.RootsAndImpactTableViewController.flattenHierarchy";
        const string k_SessionStateKey_HideExclusiveOnly = "com.unity.memoryprofiler.RootsAndImpactTableViewController.hideExclusiveOnly";
        const string k_SessionStateKey_HideRegions = "com.unity.memoryprofiler.RootsAndImpactTableViewController.hideRegions";

        /// <summary>
        /// Base width of the Status column. Wider than the other tables because Roots &amp; Impact status
        /// cells can read e.g. "Referenced Managed Object (3) (12 Warnings in references)". The column
        /// manager derives the max resizable width as twice this, so the base also governs how far it
        /// can be dragged.
        /// </summary>
        const int k_StatusColumnBaseWidth = 180;

        // Model
        readonly CachedSnapshot m_Snapshot;
        readonly IResponder m_Responder;
        protected override Dictionary<string, Comparison<TreeViewItemData<RootsAndImpactModel.ItemData>>> SortComparisons { get; }
        /// <summary>
        /// Maps each sortable column to the compact-row sort key for the flattened view's native multi-column
        /// index sort. The Description (name) column is absent because name sorting goes through the model's
        /// managed path instead. The Total-Impact-bar column resolves to committed/resident by table mode at sort
        /// time.
        /// </summary>
        readonly Dictionary<string, RootsAndImpactFlatRowComparer.Column> m_FlatSortColumns;
        /// <summary>The paged model owns a native row store that must be disposed when it is replaced.</summary>
        RootsAndImpactModel m_OwnedModel;
        FlatPagerBinding<RootsAndImpactFlatRowComparer.Column> m_PagerBinding;
        /// <summary>
        /// Set when a filter or Hide-toggle change in flat mode should re-select over the existing superset store
        /// via the justSort reuse path instead of rebuilding. Captured and cleared by
        /// <see cref="GetModelSorterTask"/> on the main thread.
        /// </summary>
        bool m_PendingFlatFilterApply;
        bool m_FlattenHierarchy;
        bool m_HideExclusiveOnly;
        bool m_HideRegions;
        bool m_CompareMode;

        public override bool ExcludeAllFilterApplied => false;

        // Composed components
        readonly TableFilterManager<RootsAndImpactModel, RootsAndImpactModel.ItemData> m_FilterManager;
        StatusFilterButton m_StatusFilterButton;
        readonly ModelBuildOrchestrator<RootsAndImpactModel, RootsAndImpactModelBuilder.BuildArgs> m_BuildOrchestrator;
        TreeViewColumnManager<RootsAndImpactModel.ItemData> m_ColumnManager;
        SizeCellRenderer m_SizeCellRenderer;

        // View
        Toolbar m_Toolbar;
        Toggle m_FlattenToggle;
        Toggle m_ExclusiveToggle;
        Toggle m_RegionDataToggle;
        PaginationControl m_Pager;

        public RootsAndImpactTableViewController(
            CachedSnapshot snapshot,
            StatusFilterButton statusFilterButton = null,
            ToolbarSearchField searchField = null,
            bool compareMode = false,
            IResponder responder = null)
            : base(idOfDefaultColumnWithPercentageBasedWidth: k_UxmlIdentifier_TreeViewColumn__Description)
        {
            m_Snapshot = snapshot;
            m_SearchField = searchField;
            m_CompareMode = compareMode;
            m_Responder = responder;
            m_StatusFilterButton = statusFilterButton;

            // Initialize composed components
            m_FilterManager = new TableFilterManager<RootsAndImpactModel, RootsAndImpactModel.ItemData>();
            statusFilterButton?.SetUp(m_FilterManager, () => { if (IsViewLoaded && !TryRefilterFlatInPlace()) FireAndForgetBuildModelAsync(); }, ObjectStatusExtensions.RootsAndImpactTableUnavailableStatuses);
            m_BuildOrchestrator = new ModelBuildOrchestrator<RootsAndImpactModel, RootsAndImpactModelBuilder.BuildArgs>(
                snapshot,
                new RootsAndImpactModelBuilderAdapter(),
                typeof(RootsAndImpactModel).ToString());

            SearchFilterChanged += OnSearchFilterChanged;

            SortComparisons = new()
            {
                { k_UxmlIdentifier_TreeViewColumn__Description, (x, y) => string.Compare(x.data.Name, y.data.Name, StringComparison.OrdinalIgnoreCase) },
                { k_UxmlIdentifier_TreeViewColumn__TotalImpact, (x, y) => x.data.TotalImpactSum.Committed.CompareTo(y.data.TotalImpactSum.Committed) },
                { k_UxmlIdentifier_TreeViewColumn__TotalImpactResident, (x, y) => x.data.TotalImpactSum.Resident.CompareTo(y.data.TotalImpactSum.Resident) },
                { k_UxmlIdentifier_TreeViewColumn__TotalImpactBar, (x, y) => TableMode != AllTrackedMemoryTableMode.OnlyResident
                    ? x.data.TotalImpactSum.Committed.CompareTo(y.data.TotalImpactSum.Committed)
                    : x.data.TotalImpactSum.Resident.CompareTo(y.data.TotalImpactSum.Resident) },
                { k_UxmlIdentifier_TreeViewColumn__OwnSize, (x, y) => x.data.OwnSizeSum.Committed.CompareTo(y.data.OwnSizeSum.Committed) },
                { k_UxmlIdentifier_TreeViewColumn__ExclusiveImpact, (x, y) => x.data.ExclusiveImpactSum.Committed.CompareTo(y.data.ExclusiveImpactSum.Committed) },
                { k_UxmlIdentifier_TreeViewColumn__SharedProRata, (x, y) => x.data.SharedProRataSum.Committed.CompareTo(y.data.SharedProRataSum.Committed) },
                { k_UxmlIdentifier_TreeViewColumn__SharedInTotal, (x, y) => x.data.SharedInTotalSum.Committed.CompareTo(y.data.SharedInTotalSum.Committed) },
                { k_UxmlIdentifier_TreeViewColumn__NativeImpact, (x, y) => x.data.TotalImpact.NativeSize.Committed.CompareTo(y.data.TotalImpact.NativeSize.Committed) },
                { k_UxmlIdentifier_TreeViewColumn__ManagedImpact, (x, y) => x.data.TotalImpact.ManagedSize.Committed.CompareTo(y.data.TotalImpact.ManagedSize.Committed) },
                { k_UxmlIdentifier_TreeViewColumn__GfxImpact, (x, y) => x.data.TotalImpact.GfxSize.Committed.CompareTo(y.data.TotalImpact.GfxSize.Committed) },
                { k_UxmlIdentifier_TreeViewColumn__Status, (x, y) => x.data.WarnInfo.GetStatusSortKey().CompareTo(y.data.WarnInfo.GetStatusSortKey()) },
            };

            m_FlatSortColumns = new()
            {
                { k_UxmlIdentifier_TreeViewColumn__TotalImpact, RootsAndImpactFlatRowComparer.Column.TotalImpactCommitted },
                { k_UxmlIdentifier_TreeViewColumn__TotalImpactResident, RootsAndImpactFlatRowComparer.Column.TotalImpactResident },
                { k_UxmlIdentifier_TreeViewColumn__OwnSize, RootsAndImpactFlatRowComparer.Column.OwnSize },
                { k_UxmlIdentifier_TreeViewColumn__ExclusiveImpact, RootsAndImpactFlatRowComparer.Column.ExclusiveImpact },
                { k_UxmlIdentifier_TreeViewColumn__SharedProRata, RootsAndImpactFlatRowComparer.Column.SharedProRata },
                { k_UxmlIdentifier_TreeViewColumn__SharedInTotal, RootsAndImpactFlatRowComparer.Column.SharedInTotal },
                { k_UxmlIdentifier_TreeViewColumn__NativeImpact, RootsAndImpactFlatRowComparer.Column.NativeImpact },
                { k_UxmlIdentifier_TreeViewColumn__ManagedImpact, RootsAndImpactFlatRowComparer.Column.ManagedImpact },
                { k_UxmlIdentifier_TreeViewColumn__GfxImpact, RootsAndImpactFlatRowComparer.Column.GfxImpact },
                { k_UxmlIdentifier_TreeViewColumn__Status, RootsAndImpactFlatRowComparer.Column.Status },
            };
        }

        public RootsAndImpactModel Model => m_Model;

        ToolbarSearchField m_SearchField;
        protected override ToolbarSearchField SearchField => m_SearchField;

        void OnSearchFilterChanged(IScopedFilter<string> searchFilter)
        {
            m_FilterManager.SearchFilter = searchFilter;
            if (TryRefilterFlatInPlace())
                return;
            FireAndForgetBuildModelAsync(false);
        }

        // In flat mode a text/status filter change is a cheap index re-selection over the existing unfiltered
        // native store (re-pages from 0 and recomputes the size-bar total), not a full tree rebuild. Reuse the
        // model via the justSort path with the filter applied first; returns false to fall back to a rebuild
        // (tree mode, or before the first flat model exists). Structural toggles still rebuild.
        bool TryRefilterFlatInPlace()
        {
            if (!IsViewLoaded || !m_FlattenHierarchy || m_Model == null || !m_Model.IsPaged)
                return false;
            m_PendingFlatFilterApply = true;
            FireAndForgetBuildModelAsync(justSort: true);
            return true;
        }

        public bool FlattenHierarchy
        {
            get => m_FlattenHierarchy;
            set
            {
                m_FlattenHierarchy = value;
                SessionState.SetBool(k_SessionStateKey_FlattenHierarchy, value);
                if (IsViewLoaded)
                    FireAndForgetBuildModelAsync(false);
            }
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

            m_FlattenToggle.text = TextContent.RootsAndImpactFlattenToggle;
            m_FlattenToggle.RegisterValueChangedCallback(OnFlattenToggleValueChanged);
            m_ExclusiveToggle.text = TextContent.RootsAndImpactHideExclusiveToggle;
            m_ExclusiveToggle.tooltip = TextContent.RootsAndImpactHideExclusiveToggleTooltip;
            m_ExclusiveToggle.RegisterValueChangedCallback(OnHideExclusiveToggleValueChanged);

            m_RegionDataToggle.text = TextContent.RootsAndImpactRegionDataToggle;
            m_RegionDataToggle.tooltip = TextContent.RootsAndImpactRegionDataToggleTooltip;
            m_RegionDataToggle.RegisterValueChangedCallback(OnRegionDataToggleValueChanged);

            m_TreeView.horizontalScrollingEnabled = true;
            m_TreeView.schedule.Execute(ResizeTreeViewColumnsToFit).ExecuteLater(0);
        }

        void ResizeTreeViewColumnsToFit()
        {
            if (float.IsNaN(m_TreeView.contentRect.width))
            {
                m_TreeView.schedule.Execute(ResizeTreeViewColumnsToFit).ExecuteLater(0);
                return;
            }
            m_ColumnManager.ResizeToFit();
        }

        void GatherReferencesInView(VisualElement view)
        {
            m_TreeView = view.Q<MultiColumnTreeView>(k_UxmlIdentifier_TreeView);
            m_Toolbar = view.Q<Toolbar>(k_UxmlIdentifier_Toolbar);
            m_FlattenToggle = view.Q<Toggle>(k_UxmlIdentifier_FlattenToggle);
            m_ExclusiveToggle = view.Q<Toggle>(k_UxmlIdentifier_ExclusiveToggle);
            m_RegionDataToggle = view.Q<Toggle>(k_UxmlIdentifier_RegionDataToggle);
            m_LoadingOverlay = view.Q<ActivityIndicatorOverlay>(k_UxmlIdentifier_LoadingOverlay);
            m_ErrorLabel = view.Q<Label>(k_UxmlIdentifier_ErrorLabel);
            m_Pager = view.Q<PaginationControl>(k_UxmlIdentifier_Pager);

            m_ColumnManager = new TreeViewColumnManager<RootsAndImpactModel.ItemData>(m_TreeView);
            m_SizeCellRenderer = new SizeCellRenderer(m_TreeView, () => TableMode);

            m_PagerBinding = new FlatPagerBinding<RootsAndImpactFlatRowComparer.Column>(
                m_Pager,
                m_ColumnManager.SetColumnTooltip,
                k_UxmlIdentifier_TreeViewColumn__Description,
                k_UxmlIdentifier_TreeViewColumn__TotalImpactBar,
                m_FlatSortColumns,
                GetDefaultFlatSortColumn,
                TextContent.RootsAndImpactNameColumnHeaderTooltip,
                RootsAndImpactFlatRowComparer.k_MaxColumns,
                () => m_Model,
                () => { m_TreeView.SetRootItems(m_Model.RootNodes); m_TreeView.RefreshItems(); });

            m_FlattenHierarchy = SessionState.GetBool(k_SessionStateKey_FlattenHierarchy, false);
            m_FlattenToggle.SetValueWithoutNotify(m_FlattenHierarchy);
            m_HideExclusiveOnly = SessionState.GetBool(k_SessionStateKey_HideExclusiveOnly, false);
            m_ExclusiveToggle.SetValueWithoutNotify(m_HideExclusiveOnly);
            m_HideRegions = SessionState.GetBool(k_SessionStateKey_HideRegions, true);
            m_RegionDataToggle.SetValueWithoutNotify(m_HideRegions);
        }

        protected override void ConfigureTreeView()
        {
            base.ConfigureTreeView();

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Description,
                TextContent.RootsAndImpactNameColumnHeader,
                0,
                BindCellForDescriptionColumn(),
                makeCell: () => new ObjectOrTypeLabel(),
                destroyCell: cell => cell.Q<ObjectOrTypeLabel>().Dispose(),
                headerTooltip: TextContent.RootsAndImpactNameColumnHeaderTooltip);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__TotalImpact,
                TextContent.RootsAndImpactTotalImpactAllocatedColumnHeader,
                120,
                m_SizeCellRenderer.CreateMemorySizeBinding<RootsAndImpactModel.ItemData>(
                    item => item.TotalImpactSum,
                    useResident: false),
                visible: TableMode != AllTrackedMemoryTableMode.OnlyResident,
                headerTooltip: TextContent.RootsAndImpactTotalImpactColumnHeaderTooltip);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__TotalImpactResident,
                TextContent.RootsAndImpactTotalImpactResidentColumnHeader,
                120,
                m_SizeCellRenderer.CreateMemorySizeBinding<RootsAndImpactModel.ItemData>(
                    item => item.TotalImpactSum,
                    useResident: true),
                visible: m_Snapshot.HasSystemMemoryResidentPages && TableMode != AllTrackedMemoryTableMode.OnlyCommitted,
                headerTooltip: TextContent.RootsAndImpactTotalImpactColumnHeaderTooltip);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__TotalImpactBar,
                TextContent.PercentImpactTableColumnHeader,
                180,
                m_SizeCellRenderer.CreateMemoryBarBinding<RootsAndImpactModel.ItemData>(
                    item => item.TotalImpactSum,
                    () =>
                    {
                        var total = m_Model.TotalImpactSize.SumUp();
                        return TableMode != AllTrackedMemoryTableMode.OnlyResident
                            ? new MemorySize(total.Committed, total.Committed)
                            : new MemorySize(total.Resident, total.Resident);
                    }),
                makeCell: m_SizeCellRenderer.MakeMemoryBarCell);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__OwnSize,
                TextContent.RootsAndImpactOwnSizeColumnHeader,
                120,
                m_SizeCellRenderer.CreateMemorySizeBinding<RootsAndImpactModel.ItemData>(
                    item => item.OwnSizeSum),
                headerTooltip: TextContent.RootsAndImpactOwnSizeColumnHeaderTooltip);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__ExclusiveImpact,
                TextContent.RootsAndImpactExclusiveImpactColumnHeader,
                120,
                m_SizeCellRenderer.CreateMemorySizeBinding<RootsAndImpactModel.ItemData>(
                    item => item.ExclusiveImpactSum),
                headerTooltip: TextContent.RootsAndImpactExclusiveImpactColumnHeaderTooltip);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__SharedProRata,
                TextContent.RootsAndImpactSharedProRataColumnHeader,
                120,
                m_SizeCellRenderer.CreateMemorySizeBinding<RootsAndImpactModel.ItemData>(
                    item => item.SharedProRataSum),
                headerTooltip: TextContent.RootsAndImpactSharedProRataColumnHeaderTooltip);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__SharedInTotal,
                TextContent.RootsAndImpactSharedInTotalColumnHeader,
                120,
                m_SizeCellRenderer.CreateMemorySizeBinding<RootsAndImpactModel.ItemData>(
                    item => item.SharedInTotalSum),
                visible: false,
                headerTooltip: TextContent.RootsAndImpactSharedInTotalColumnHeaderTooltip);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__NativeImpact,
                TextContent.RootsAndImpactNativeImpactColumnHeader,
                120,
                m_SizeCellRenderer.CreateMemorySizeBinding<RootsAndImpactModel.ItemData>(
                    item => item.TotalImpact.NativeSize),
                visible: false);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__ManagedImpact,
                TextContent.RootsAndImpactManagedImpactColumnHeader,
                120,
                m_SizeCellRenderer.CreateMemorySizeBinding<RootsAndImpactModel.ItemData>(
                    item => item.TotalImpact.ManagedSize),
                visible: false);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__GfxImpact,
                TextContent.RootsAndImpactGfxImpactColumnHeader,
                120,
                m_SizeCellRenderer.CreateMemorySizeBinding<RootsAndImpactModel.ItemData>(
                    item => item.TotalImpact.GfxSize),
                visible: false);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Status,
                TextContent.StatusTableColumnHeader,
                k_StatusColumnBaseWidth,
                makeCell: () => new StatusCell(),
                bindCell: BindCellForStatusColumn());
        }

        RootsAndImpactModelBuilder.BuildArgs GetModelBuilderArgs(CancellationToken cancellationToken)
        {
            return new RootsAndImpactModelBuilder.BuildArgs(
                m_FilterManager.CompoundSearchFilter,
                FlattenHierarchy,
                m_HideExclusiveOnly,
                m_HideRegions,
                cancellationToken);
        }

        protected override Func<RootsAndImpactModel> GetModelBuilderTask(CancellationToken cancellationToken)
        {
            var args = GetModelBuilderArgs(cancellationToken);
            return m_BuildOrchestrator.CreateBuildTask(
                args,
                m_FilterManager.BaseModelForTreeNodeIdFiltering,
                m_FilterManager.TreeNodeIdFilter,
                cancellationToken);
        }

        protected override void OnModelRebuild(bool success, bool modelHasNewTreeViewIds, Exception exception = null)
        {
            // GetModelSorterTask below defers materializing the paged model's current page to here: the
            // background sort/filter continuation runs on a thread-pool thread, and RootNodes is also read by
            // the UI-bound tree view, so mutating it off the main thread would race that read. Materialize now,
            // on the main thread, before base.OnModelRebuild reads it via RefreshView -> SetRootItems.
            if (success && m_Model != null && m_Model.IsPaged)
                m_Model.MaterializePage(m_Model.CurrentPage);

            // Every ItemData an R&I row hands to UIToolkit is a redirect into a native row source, so this call
            // must come first: it sets m_Model and drives RefreshView -> SetRootItems, replacing UIToolkit's
            // node-to-data dictionary with the new model's redirects before the old model is disposed below.
            // Reversed, UIToolkit would read through redirects whose store had just been freed.
            base.OnModelRebuild(success, modelHasNewTreeViewIds, exception);

            if (!success || m_Model == null)
            {
                m_PagerBinding.SyncTo(null);
                return;
            }

            // Free the native store of the model this build replaced. A justSort reuses the same model
            // instance (m_Model == m_OwnedModel), so it is not disposed; only a full rebuild swaps it.
            DisposableModelSwap.ReplaceIfChanged(ref m_OwnedModel, m_Model);

            // The pager only applies to the flattened, paged model; it conveys position there in place of a
            // truncation notice. SortFlat/the build reset CurrentPage to 0, so the pager follows the model.
            m_PagerBinding.SyncTo(m_Model);
        }

        /// <summary>
        /// Sorting the flattened, paged model can't go through TreeModel.Sort (which only sees the current
        /// page). Re-order the native store by the active column(s) instead, then re-materialize the first page.
        /// The table header allows mixing the name (Description) column with size/status columns (up to
        /// k_MaxColumns of the latter) at any priority; SortFlatMixed honors that order, staying on the fast
        /// native comparer when name isn't involved, and dropping only the name level (not the whole sort) above
        /// <see cref="FlatPagingLimits.NameSortHardLimit"/>.
        /// </summary>
        protected override Func<System.Threading.Tasks.Task<RootsAndImpactModel>, RootsAndImpactModel> GetModelSorterTask(CancellationToken cancellationToken)
        {
            if (!m_FlattenHierarchy)
                return base.GetModelSorterTask(cancellationToken);

            // A filter change in flat mode rides this (justSort) path: capture whether to re-select the store
            // for the active filter, and the filter itself, on the main thread before the continuation runs.
            var applyFilter = m_PendingFlatFilterApply;
            m_PendingFlatFilterApply = false;
            var filter = applyFilter ? m_FilterManager.CompoundSearchFilter : null;
            var hideExclusiveOnly = m_HideExclusiveOnly;
            var hideRegions = m_HideRegions;

            // Read the active sort columns on the main thread (UI access) before the async continuation runs.
            var sort = m_PagerBinding.ReadSortState(m_TreeView);

            return task =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var model = task.Result;
                if (model == null || !model.IsPaged)
                    return model;

                // One atomic call so a superseded continuation can't land its stale filter/sort half after a
                // newer one's. materialize: false defers materializing the page to OnModelRebuild on the main thread.
                var sortLevels = sort.Levels.Length > 0
                    ? sort.Levels
                    // No active size/status/name sort: fall back to the UXML default (Total Impact, descending).
                    : new[] { new FlatSortLevel<RootsAndImpactFlatRowComparer.Column> { Column = m_PagerBinding.DefaultColumn, Descending = true } };
                model.ApplyFlatFilterAndSort(filter, applyFilter, hideExclusiveOnly, hideRegions, sortLevels, cancellationToken, materialize: false);
                return model;
            };
        }

        /// <summary>Total-Impact column/bar sort key: committed in the default mode, resident when OnlyResident is active.</summary>
        RootsAndImpactFlatRowComparer.Column GetDefaultFlatSortColumn() => TableMode != AllTrackedMemoryTableMode.OnlyResident
            ? RootsAndImpactFlatRowComparer.Column.TotalImpactCommitted
            : RootsAndImpactFlatRowComparer.Column.TotalImpactResident;

        protected override void OnViewReloaded(bool success)
        {
            base.OnViewReloaded(success);
            m_Responder?.RootsAndImpactTableViewControllerReloaded(this, success);
        }

        Action<VisualElement, int> BindCellForDescriptionColumn()
        {
            return (element, rowIndex) =>
            {
                var label = element.Q<ObjectOrTypeLabel>();
                var itemData = m_TreeView.GetItemDataForIndex<RootsAndImpactModel.ItemData>(rowIndex);

                var displayText = itemData.Name;
                if (string.IsNullOrEmpty(displayText))
                    displayText = CachedSnapshot.InvalidItemName;

                if (!label.TrySetLabelDataFromSourceIndex(m_Snapshot, itemData.Source, displayText))
                    label.SetLabelData(m_Snapshot, displayText, string.Empty);

                // In flat mode every row is a leaf — suppress the "(N)" child-count indicator.
                var secondaryDisplayText = string.Empty;
                if (!m_FlattenHierarchy && itemData.ChildCount > 0)
                    secondaryDisplayText = $"({itemData.ChildCount:N0})";
                label.SetSecondaryText(secondaryDisplayText);

                // De-emphasis for filter-match ancestors
                if (itemData.IsFilterMatchAncestor)
                    element.AddToClassList(k_UssClass_FilterMatchAncestor);
                else
                    element.RemoveFromClassList(k_UssClass_FilterMatchAncestor);
            };
        }

        Action<VisualElement, int> BindCellForStatusColumn()
        {
            return (element, rowIndex) =>
            {
                var label = element.Q<StatusCell>();
                var itemData = m_TreeView.GetItemDataForIndex<RootsAndImpactModel.ItemData>(rowIndex);
                // Roots & Impact rows show their own status; the warning icon/secondary describe warnings in
                // their referenced subtree. Flattened rows are never group headers, so they take the
                // non-group (own-status) path automatically.
                label.SetStatusForRootsAndImpact(itemData.Status, itemData.WarnInfo, itemData.IsGroupHeader);
            };
        }

        void OnFlattenToggleValueChanged(ChangeEvent<bool> evt)
        {
            FlattenHierarchy = evt.newValue;
        }

        void OnHideExclusiveToggleValueChanged(ChangeEvent<bool> evt)
        {
            m_HideExclusiveOnly = evt.newValue;
            SessionState.SetBool(k_SessionStateKey_HideExclusiveOnly, m_HideExclusiveOnly);
            // The flat store is a superset that includes exclusive-only rows, so this is a cheap re-selection.
            if (IsViewLoaded && !TryRefilterFlatInPlace())
                FireAndForgetBuildModelAsync(false);
        }

        void OnRegionDataToggleValueChanged(ChangeEvent<bool> evt)
        {
            m_HideRegions = evt.newValue;
            SessionState.SetBool(k_SessionStateKey_HideRegions, m_HideRegions);
            // The flat store is a superset that includes region rows, so this is a cheap re-selection.
            if (IsViewLoaded && !TryRefilterFlatInPlace())
                FireAndForgetBuildModelAsync(false);
        }

        protected override void OnTreeItemSelected(int itemId, RootsAndImpactModel.ItemData itemData)
        {
            m_Responder?.RootsAndImpactTableViewControllerSelectedItem(itemId, this, itemData);
        }

        public interface IResponder
        {
            void RootsAndImpactTableViewControllerSelectedItem(int itemId, RootsAndImpactTableViewController viewController, RootsAndImpactModel.ItemData itemData);
            void RootsAndImpactTableViewControllerReloaded(RootsAndImpactTableViewController viewController, bool success);
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
                // Releases this controller's reference to the row source, freeing the native store unless a
                // derived id-filtered model still holds one.
                DisposableModelSwap.ReplaceIfChanged(ref m_OwnedModel, null);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

    class RootsAndImpactTableViewController : SingleSnapshotTreeViewController<RootsAndImpactModel, RootsAndImpactModel.ItemData>, IFilterRelaxationTarget
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
        const string k_UxmlIdentifier_ScaleBarsToggle = "roots-and-impact-table-view__toolbar__scale-bars-toggle";
        const string k_UxmlIdentifier_LoadingOverlay = "roots-and-impact-table-view__loading-overlay";
        const string k_UxmlIdentifier_ErrorLabel = "roots-and-impact-table-view__error-label";
        const string k_UxmlIdentifier_Pager = "roots-and-impact-table-view__pager";

        const string k_UssClass_FilterMatchAncestor = "roots-and-impact-table-view__filter-match-ancestor";

        const string k_SessionStateKey_FlattenHierarchy = "com.unity.memoryprofiler.RootsAndImpactTableViewController.flattenHierarchy";
        /// <summary> Internal for testing purposes </summary>
        internal const string k_SessionStateKey_HideExclusiveOnly = "com.unity.memoryprofiler.RootsAndImpactTableViewController.hideExclusiveOnly";
        const string k_SessionStateKey_ScaleBarsToParent = "com.unity.memoryprofiler.RootsAndImpactTableViewController.scaleBarsToParent";
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
        /// <summary>Deep-link preset stashed when <see cref="ApplyStatusMaskPreset"/> is called before <see cref="ViewLoaded"/>.</summary>
        StatusMaskPreset? m_PendingStatusMaskPreset;

        public override bool ExcludeAllFilterApplied => false;

        // Composed components
        readonly TableFilterManager<RootsAndImpactModel, RootsAndImpactModel.ItemData> m_FilterManager;
        StatusFilterButton m_StatusFilterButton;
        readonly ModelBuildOrchestrator<RootsAndImpactModel, RootsAndImpactModelBuilder.BuildArgs> m_BuildOrchestrator;
        TreeViewColumnManager<RootsAndImpactModel.ItemData> m_ColumnManager;
        protected override IColumnSumRefresher ColumnManager => m_ColumnManager;
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
                { k_UxmlIdentifier_TreeViewColumn__TotalImpact, (lhs, rhs) => CompareByDisplayedSize(lhs, rhs, f => f.TotalImpactSum, d => d.TotalImpactSum) },
                { k_UxmlIdentifier_TreeViewColumn__TotalImpactResident, (lhs, rhs) => CompareByDisplayedSize(lhs, rhs, f => f.TotalImpactSum, d => d.TotalImpactSum, byResident: true) },
                { k_UxmlIdentifier_TreeViewColumn__TotalImpactBar, (lhs, rhs) => CompareByDisplayedSize(lhs, rhs, f => f.TotalImpactSum, d => d.TotalImpactSum, byResident: TableMode == AllTrackedMemoryTableMode.OnlyResident) },
                { k_UxmlIdentifier_TreeViewColumn__OwnSize, (x, y) => x.data.OwnSizeSum.Committed.CompareTo(y.data.OwnSizeSum.Committed) },
                { k_UxmlIdentifier_TreeViewColumn__ExclusiveImpact, (lhs, rhs) => CompareByDisplayedSize(lhs, rhs, f => f.ExclusiveImpactSum, d => d.ExclusiveImpactSum) },
                { k_UxmlIdentifier_TreeViewColumn__SharedProRata, (lhs, rhs) => CompareByDisplayedSize(lhs, rhs, f => f.SharedProRataSum, d => d.SharedProRataSum) },
                { k_UxmlIdentifier_TreeViewColumn__SharedInTotal, (lhs, rhs) => CompareByDisplayedSize(lhs, rhs, f => f.SharedInTotalSum, d => d.SharedInTotalSum) },
                { k_UxmlIdentifier_TreeViewColumn__NativeImpact, (lhs, rhs) => CompareByDisplayedSize(lhs, rhs, f => f.TotalImpact.NativeSize, d => d.TotalImpact.NativeSize) },
                { k_UxmlIdentifier_TreeViewColumn__ManagedImpact, (lhs, rhs) => CompareByDisplayedSize(lhs, rhs, f => f.TotalImpact.ManagedSize, d => d.TotalImpact.ManagedSize) },
                { k_UxmlIdentifier_TreeViewColumn__GfxImpact, (lhs, rhs) => CompareByDisplayedSize(lhs, rhs, f => f.TotalImpact.GfxSize, d => d.TotalImpact.GfxSize) },
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

            if (m_PendingStatusMaskPreset.HasValue)
            {
                var preset = m_PendingStatusMaskPreset.Value;
                m_PendingStatusMaskPreset = null;
                // State only: the initial TriggerBuild (from AddAndBuildChildTableView) does the one build.
                ApplyStatusMaskPresetNow(preset.ExclusiveStatusMask, preset.Flatten, rebuild: false);
            }
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
            m_SizeCellRenderer.SetUpScaleBarsToParentToggle(view.Q<Toggle>(k_UxmlIdentifier_ScaleBarsToggle), k_SessionStateKey_ScaleBarsToParent);

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
                RefreshTreeViewRoots);

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
                makeCell: () => MakeLabelWithSelectInTableMenu(),
                destroyCell: cell => cell.Q<ObjectOrTypeLabel>().Dispose(),
                headerTooltip: TextContent.RootsAndImpactNameColumnHeaderTooltip);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__TotalImpact,
                TextContent.RootsAndImpactTotalImpactAllocatedColumnHeader,
                120,
                m_SizeCellRenderer.CreateDualMemorySizeBinding<RootsAndImpactModel.ItemData>(
                    item => item.TotalImpactSum,
                    FilteredImpactSelector(f => f.TotalImpactSum),
                    TextContent.RootsAndImpactFilteredImpactCellTooltipFormat,
                    useResident: false),
                visible: TableMode != AllTrackedMemoryTableMode.OnlyResident,
                headerTooltip: TextContent.RootsAndImpactTotalImpactColumnHeaderTooltip,
                makeCell: () => new DualSizeCell(),
                sumProvider: () => m_Model?.TotalImpactSize.SumUp().Committed ?? 0);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__TotalImpactResident,
                TextContent.RootsAndImpactTotalImpactResidentColumnHeader,
                120,
                m_SizeCellRenderer.CreateDualMemorySizeBinding<RootsAndImpactModel.ItemData>(
                    item => item.TotalImpactSum,
                    FilteredImpactSelector(f => f.TotalImpactSum),
                    TextContent.RootsAndImpactFilteredImpactCellTooltipFormat,
                    useResident: true),
                visible: m_Snapshot.HasSystemMemoryResidentPages && TableMode != AllTrackedMemoryTableMode.OnlyCommitted,
                headerTooltip: TextContent.RootsAndImpactTotalImpactColumnHeaderTooltip,
                makeCell: () => new DualSizeCell(),
                sumProvider: () => m_Model?.TotalImpactSize.SumUp().Resident ?? 0);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__TotalImpactBar,
                TextContent.PercentImpactTableColumnHeader,
                180,
                m_SizeCellRenderer.CreateDualMemoryBarBinding<RootsAndImpactModel.ItemData>(
                    item => item.TotalImpactSum,
                    FilteredImpactSelector(f => f.TotalImpactSum),
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
                headerTooltip: TextContent.RootsAndImpactOwnSizeColumnHeaderTooltip,
                sumProvider: () => ModelImpactTotal(model => model.TotalOwnSize),
                sumTooltipFormat: TextContent.TableColumnHeaderSumAllRowsTooltipFormat);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__ExclusiveImpact,
                TextContent.RootsAndImpactExclusiveImpactColumnHeader,
                120,
                m_SizeCellRenderer.CreateDualMemorySizeBinding<RootsAndImpactModel.ItemData>(
                    item => item.ExclusiveImpactSum,
                    FilteredImpactSelector(f => f.ExclusiveImpactSum),
                    TextContent.RootsAndImpactFilteredImpactCellTooltipFormat),
                headerTooltip: TextContent.RootsAndImpactExclusiveImpactColumnHeaderTooltip,
                makeCell: () => new DualSizeCell(),
                sumProvider: () => ModelImpactTotal(model => model.TotalImpactExclusiveSize));

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__SharedProRata,
                TextContent.RootsAndImpactSharedProRataColumnHeader,
                120,
                m_SizeCellRenderer.CreateDualMemorySizeBinding<RootsAndImpactModel.ItemData>(
                    item => item.SharedProRataSum,
                    FilteredImpactSelector(f => f.SharedProRataSum),
                    TextContent.RootsAndImpactFilteredImpactCellTooltipFormat),
                headerTooltip: TextContent.RootsAndImpactSharedProRataColumnHeaderTooltip,
                makeCell: () => new DualSizeCell(),
                sumProvider: () => ModelImpactTotal(model => model.TotalImpactSharedProRataSize));

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__SharedInTotal,
                TextContent.RootsAndImpactSharedInTotalColumnHeader,
                120,
                m_SizeCellRenderer.CreateDualMemorySizeBinding<RootsAndImpactModel.ItemData>(
                    item => item.SharedInTotalSum,
                    FilteredImpactSelector(f => f.SharedInTotalSum),
                    TextContent.RootsAndImpactFilteredImpactCellTooltipFormat),
                visible: false,
                headerTooltip: TextContent.RootsAndImpactSharedInTotalColumnHeaderTooltip,
                makeCell: () => new DualSizeCell(),
                sumProvider: () => ModelImpactTotal(model => model.TotalSharedInTotalSize));

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__NativeImpact,
                TextContent.RootsAndImpactNativeImpactColumnHeader,
                120,
                m_SizeCellRenderer.CreateDualMemorySizeBinding<RootsAndImpactModel.ItemData>(
                    item => item.TotalImpact.NativeSize,
                    FilteredImpactSelector(f => f.TotalImpact.NativeSize),
                    TextContent.RootsAndImpactFilteredImpactCellTooltipFormat),
                visible: false,
                makeCell: () => new DualSizeCell(),
                sumProvider: () => ModelImpactTotal(model => model.TotalImpactSize.NativeSize));

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__ManagedImpact,
                TextContent.RootsAndImpactManagedImpactColumnHeader,
                120,
                m_SizeCellRenderer.CreateDualMemorySizeBinding<RootsAndImpactModel.ItemData>(
                    item => item.TotalImpact.ManagedSize,
                    FilteredImpactSelector(f => f.TotalImpact.ManagedSize),
                    TextContent.RootsAndImpactFilteredImpactCellTooltipFormat),
                visible: false,
                makeCell: () => new DualSizeCell(),
                sumProvider: () => ModelImpactTotal(model => model.TotalImpactSize.ManagedSize));

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__GfxImpact,
                TextContent.RootsAndImpactGfxImpactColumnHeader,
                120,
                m_SizeCellRenderer.CreateDualMemorySizeBinding<RootsAndImpactModel.ItemData>(
                    item => item.TotalImpact.GfxSize,
                    FilteredImpactSelector(f => f.TotalImpact.GfxSize),
                    TextContent.RootsAndImpactFilteredImpactCellTooltipFormat),
                visible: false,
                makeCell: () => new DualSizeCell(),
                sumProvider: () => ModelImpactTotal(model => model.TotalImpactSize.GfxSize));

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Status,
                TextContent.StatusTableColumnHeader,
                k_StatusColumnBaseWidth,
                makeCell: () => new StatusCell(),
                bindCell: BindCellForStatusColumn());
        }

        // Null when the row has no filtered impact, so the cell renders its own value alone.
        Func<int, MemorySize?> FilteredImpactSelector(Func<RootsAndImpactModel.FilteredImpact, MemorySize> field)
        {
            return itemId => m_Model != null && m_Model.TryGetFilteredImpact(itemId, out var filtered)
                ? (MemorySize?)field(filtered)
                : null;
        }

        /// <summary>
        /// The column total for an impact column, in the table mode's committed or resident channel. Taken from the
        /// model's accumulated totals rather than summing rows, since shared impact would be counted once per referrer.
        /// </summary>
        ulong ModelImpactTotal(Func<RootsAndImpactModel, MemorySize> totalSelector)
        {
            if (m_Model == null)
                return 0;

            var total = totalSelector(m_Model);
            return TableMode == AllTrackedMemoryTableMode.OnlyResident ? total.Resident : total.Committed;
        }

        /// <summary>
        /// Compares two rows by the size their cells display, using the filtered value when a row has one
        /// (see <see cref="FilteredImpactSelector"/>).
        /// </summary>
        int CompareByDisplayedSize(
            in TreeViewItemData<RootsAndImpactModel.ItemData> lhs,
            in TreeViewItemData<RootsAndImpactModel.ItemData> rhs,
            Func<RootsAndImpactModel.FilteredImpact, MemorySize> filteredField,
            Func<RootsAndImpactModel.ItemData, MemorySize> rawField,
            bool byResident = false)
        {
            var lhsSize = DisplayedSize(in lhs);
            var rhsSize = DisplayedSize(in rhs);
            return byResident
                ? lhsSize.Resident.CompareTo(rhsSize.Resident)
                : lhsSize.Committed.CompareTo(rhsSize.Committed);

            MemorySize DisplayedSize(in TreeViewItemData<RootsAndImpactModel.ItemData> item)
            {
                var model = m_ModelBeingSorted.Value ?? m_Model;
                return model != null && model.TryGetFilteredImpact(item.id, out var filtered)
                    ? filteredField(filtered)
                    : rawField(item.data);
            }
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
        /// The model being sorted, since a new model is sorted in the background before m_Model is updated to it.
        /// AsyncLocal because a superseded sort can still be running when the next one starts.
        /// </summary>
        readonly AsyncLocal<RootsAndImpactModel> m_ModelBeingSorted = new();

        /// <summary>
        /// Publishes the sorted model to <see cref="m_ModelBeingSorted"/> for the duration of a tree-mode sort pass.
        /// </summary>
        Func<Task<RootsAndImpactModel>, RootsAndImpactModel> WithModelBeingSorted(Func<Task<RootsAndImpactModel>, RootsAndImpactModel> baseSorter)
        {
            return t =>
            {
                m_ModelBeingSorted.Value = t.Result;
                try
                {
                    return baseSorter(t);
                }
                finally
                {
                    m_ModelBeingSorted.Value = null;
                }
            };
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
                return WithModelBeingSorted(base.GetModelSorterTask(cancellationToken));

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

                if (!label.TrySetLabelDataFromSourceIndex(m_Snapshot, itemData.Source, displayText, splitUnifiedObjectIntoHalves: true))
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

        /// <summary>
        /// Test-only seam: counts calls to <see cref="ApplyStatusMaskPresetNow"/>, so tests can tell an applied
        /// preset from one skipped because the table was already in the target state.
        /// </summary>
        internal int StatusMaskPresetAppliedCount { get; private set; }

        /// <summary>The exclusive status mask currently applied to the filter manager.</summary>
        internal ulong? ExclusiveStatusMask => m_FilterManager.ExclusiveStatusMask;

        /// <summary>
        /// Deep-link entry point: presets the exclusive status filter mask and flatten state, then rebuilds.
        /// Before the view has loaded, the preset is stashed and applied in <see cref="ViewLoaded"/>.
        /// </summary>
        public void ApplyStatusMaskPreset(ulong exclusiveStatusMask, bool flatten)
        {
            // A build fired here would race the initial build, and the two corrupt the shared model-builder scratch.
            if (!IsViewLoaded)
            {
                m_PendingStatusMaskPreset = new StatusMaskPreset(exclusiveStatusMask, flatten);
                return;
            }
            // Already in the target state (e.g. the same widget row inspected twice): skip the expensive rebuild.
            if (m_FlattenHierarchy == flatten && !m_HideExclusiveOnly
                && m_FilterManager.ExclusiveStatusMask == exclusiveStatusMask
                && m_FilterManager.SearchFilter == null)
                return;
            ApplyStatusMaskPresetNow(exclusiveStatusMask, flatten, rebuild: true);
        }

        void ApplyStatusMaskPresetNow(ulong exclusiveStatusMask, bool flatten, bool rebuild)
        {
            StatusMaskPresetAppliedCount++;

            // Backing field, not the property, so no extra build fires.
            m_FlattenHierarchy = flatten;
            m_FlattenToggle?.SetValueWithoutNotify(flatten);

            // Savings categories can include exclusive-only leaves, which this toggle would hide.
            SetHideExclusiveOnly(false);

            if (m_StatusFilterButton != null)
                // The button owns the filter manager's mask and its own toggle and label.
                m_StatusFilterButton.SetMask(exclusiveStatusMask);
            else
                m_FilterManager.ExclusiveStatusMask = exclusiveStatusMask;

            // The controller is reused across tab visits, so a stale text search would narrow the deep-linked total.
            TableRevealHelper.RelaxStatusAndTextFilters(TableFilterKind.TextSearch, m_StatusFilterButton, m_SearchField, m_FilterManager);

            if (rebuild)
                FireAndForgetBuildModelAsync(false);
        }

        /// <summary>Sets the filter, its toggle and its remembered session value, without rebuilding.</summary>
        void SetHideExclusiveOnly(bool hide)
        {
            m_HideExclusiveOnly = hide;
            SessionState.SetBool(k_SessionStateKey_HideExclusiveOnly, hide);
            m_ExclusiveToggle?.SetValueWithoutNotify(hide);
        }

        /// <summary>Sets the filter, its toggle and its remembered session value, without rebuilding.</summary>
        void SetHideRegions(bool hide)
        {
            m_HideRegions = hide;
            SessionState.SetBool(k_SessionStateKey_HideRegions, hide);
            m_RegionDataToggle?.SetValueWithoutNotify(hide);
        }

        void OnHideExclusiveToggleValueChanged(ChangeEvent<bool> evt)
        {
            SetHideExclusiveOnly(evt.newValue);
            // The flat store is a superset that includes exclusive-only rows, so this is a cheap re-selection.
            if (IsViewLoaded && !TryRefilterFlatInPlace())
                FireAndForgetBuildModelAsync(false);
        }

        void OnRegionDataToggleValueChanged(ChangeEvent<bool> evt)
        {
            SetHideRegions(evt.newValue);
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

        public override BreakdownTableId TableId => BreakdownTableId.RootsAndImpact;

        /// <summary>Whether <paramref name="id"/> is one this table can ever show, independent of any filter.</summary>
        internal static bool CanEverContainSourceId(CachedSnapshot.SourceIndex.SourceId id)
        {
            switch (id)
            {
                case CachedSnapshot.SourceIndex.SourceId.Prefab:
                case CachedSnapshot.SourceIndex.SourceId.Scene:
                case CachedSnapshot.SourceIndex.SourceId.ManagedType:
                case CachedSnapshot.SourceIndex.SourceId.ManagedObject:
                case CachedSnapshot.SourceIndex.SourceId.NativeObject:
                case CachedSnapshot.SourceIndex.SourceId.NativeRootReference:
                case CachedSnapshot.SourceIndex.SourceId.NativeAllocation:
                case CachedSnapshot.SourceIndex.SourceId.GCHandleIndex:
                case CachedSnapshot.SourceIndex.SourceId.ManagedHeapSection:
                case CachedSnapshot.SourceIndex.SourceId.NativeMemoryRegion:
                case CachedSnapshot.SourceIndex.SourceId.SystemMemoryRegion:
                    return true;
                default:
                    return false;
            }
        }

        public override bool CanEverContain(CachedSnapshot.SourceIndex source) => CanEverContainSourceId(source.Id);

        /// <summary>
        /// Unreferenced unrooted native allocations are only represented through an aggregate here, never as individual
        /// rows. Referenced ones get rows under whatever references them.
        /// </summary>
        static bool IsUnreferencedUnrootedNativeAllocation(CachedSnapshot snapshot, CachedSnapshot.SourceIndex source)
        {
            if (source.Id != CachedSnapshot.SourceIndex.SourceId.NativeAllocation)
                return false;
            var rootReference = snapshot.NativeAllocations.RootReferenceId[source.Index];
            var unrooted = rootReference <= CachedSnapshot.NativeRootReferenceEntriesCache.InvalidRootId
                || !snapshot.NativeRootReferences.IdToIndex.TryGetValue(rootReference, out var rootIndex)
                || rootIndex == CachedSnapshot.NativeRootReferenceEntriesCache.InvalidRootIndex;
            return unrooted && !snapshot.RootAndImpactInfo.IsReferencedUnrootedAllocation(source);
        }

        static bool IsRegionDataSourceId(CachedSnapshot.SourceIndex.SourceId id)
        {
            switch (id)
            {
                case CachedSnapshot.SourceIndex.SourceId.ManagedHeapSection:
                case CachedSnapshot.SourceIndex.SourceId.NativeMemoryRegion:
                case CachedSnapshot.SourceIndex.SourceId.SystemMemoryRegion:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Diagnoses each filter dimension against <see cref="RootAndImpactInfo"/>/<see cref="ObjectStatusComputation"/>
        /// for this one item, mirroring <see cref="RootsAndImpactModel.BuildRowFilterPredicate"/>'s ordering. Mode
        /// independent by construction: unlike the row store (an already-filtered superset in tree mode), it
        /// never needs the item to be present anywhere to explain why it isn't.
        /// </summary>
        public override ItemVisibility GetVisibility(CachedSnapshot.SourceIndex source, CachedSnapshot owner = null)
            => ComputeVisibility(m_Snapshot, source, m_HideRegions, m_HideExclusiveOnly, m_FilterManager.CompoundSearchFilter);

        /// <summary>
        /// Pure diagnosis logic behind <see cref="GetVisibility"/>, extracted so it can be exercised without a
        /// live <see cref="RootsAndImpactTableViewController"/> (which needs a loaded UI to hold the filter state
        /// this takes as parameters instead).
        /// </summary>
        internal static ItemVisibility ComputeVisibility(CachedSnapshot snapshot, CachedSnapshot.SourceIndex source,
            bool hideRegions, bool hideExclusiveOnly, CompoundSearchFilter filter)
        {
            if (!CanEverContainSourceId(source.Id))
                return new ItemVisibility(ItemVisibility.State.NotRepresentable);

            if (IsUnreferencedUnrootedNativeAllocation(snapshot, source))
                return new ItemVisibility(ItemVisibility.State.NotRepresentable);

            var blocking = TableFilterKind.None;

            var isRegionData = IsRegionDataSourceId(source.Id);
            if (hideRegions && isRegionData)
                blocking |= TableFilterKind.HideRegionData;

            if (!isRegionData && hideExclusiveOnly && SourceIndexToRootAndImpactInfoMapper.IsSourceIdSupported(source.Id))
            {
                var impact = SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in snapshot.RootAndImpactInfo.Impact, source);
                var exclusiveImpactSum = impact.Exclusive.SumUp();
                var sharedProRataSum = impact.SharedProRata.SumUp();
                if (sharedProRataSum.Committed == 0 && exclusiveImpactSum.Committed > 0)
                    blocking |= TableFilterKind.HideExclusiveOnly;
            }

            if (filter != null)
            {
                if (TableRevealHelper.IsBlockedByStatusMask(snapshot, source, filter))
                    blocking |= TableFilterKind.StatusMask;

                if (filter.SearchStringFilter != null)
                {
                    var typeNames = RootsAndImpactModelBuilder.GetTypeNamesForFiltering(snapshot, source);
                    var status = snapshot.ObjectStatusComputation.GetStatus(source);
                    var matchesText = filter.PassesTextOrStatusSearch(source.GetName(snapshot), status)
                        || (typeNames.Native != null && filter.PassesTextOrStatusSearch(typeNames.Native, status))
                        || (typeNames.Managed != null && filter.PassesTextOrStatusSearch(typeNames.Managed, status))
                        || (typeNames.ManagedBase != null && filter.PassesTextOrStatusSearch(typeNames.ManagedBase, status));
                    if (!matchesText)
                        blocking |= TableFilterKind.TextSearch;
                }
            }

            return ItemVisibility.FromBlockingFilters(blocking);
        }

        protected override bool TryRevealRow(CachedSnapshot.SourceIndex source, bool frame, bool select, CachedSnapshot owner)
        {
            if (m_Model.RowSource == null || !m_Model.RowSource.TryFindStoreIndex(source, out var storeIndex))
                return false;

            if (!m_Model.IsPaged)
                return TryRevealTreeRow(data => data.IsValid && data.Source == source, frame, select);

            return m_PagerBinding.TryShowRow(storeIndex, out var treeId) && RevealTreeItem(treeId, frame, select);
        }

        /// <summary>
        /// Relaxes any combination of this table's filter dimensions and triggers exactly one rebuild — the
        /// single atomic entry point a filter-relaxation dialog (e.g. from a Ping/Select onto a hidden item)
        /// calls into, rather than toggling each dimension through its own UI callback and rebuilding once per
        /// dimension.
        /// </summary>
        public void ApplyFilterRelaxation(TableFilterKind filtersToRelax)
        {
            TableRevealHelper.RelaxStatusAndTextFilters(filtersToRelax, m_StatusFilterButton, m_SearchField, m_FilterManager);

            if ((filtersToRelax & TableFilterKind.HideExclusiveOnly) != 0)
                SetHideExclusiveOnly(false);

            if ((filtersToRelax & TableFilterKind.HideRegionData) != 0)
                SetHideRegions(false);

            FireAndForgetBuildModelAsync(false);
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
                m_FilterManager.Dispose();
                m_BuildOrchestrator.Dispose();
            }
        }
    }
}

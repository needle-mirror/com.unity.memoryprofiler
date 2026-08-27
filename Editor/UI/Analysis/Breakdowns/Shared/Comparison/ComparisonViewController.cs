using System;
using System.Collections.Generic;
using System.Threading;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Abstract base view controller for visual comparison between two trees. Displays a comparison table, for which you must provide a ComparisonTableModel builder. Subclasses then provide the specialized base/compared views (see AllTrackedMemoryComparisonViewController for example).
    abstract class ComparisonViewController<TBaseModel, TBaseModelBuilderArgs, TBaseModelItemData, TModelSpecificData>
        : TreeViewController<
            ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>,
            ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>.ComparisonData>
        where TBaseModelItemData : INamedTreeItemData
        where TBaseModel : TreeModel<TBaseModelItemData>
    {
        const string k_UxmlAssetGuid = "c7699cc627afa2943b34e839e48b88af";
        const string k_UxmlIdentifier_DescriptionLabel = "comparison-view__description-label";
        const string k_UxmlIdentifier_SearchField = "comparison-view__search-field";
        const string k_UxmlIdentifier_StatusFilterButton = "comparison-view__status-filter-button";
        const string k_UxmlIdentifier_SplitView = "comparison-view__split-view";
        const string k_UxmlIdentifier_BaseTotalSizeBar = "comparison-view__base-total-size-bar";
        const string k_UxmlIdentifier_ComparedTotalSizeBar = "comparison-view__compared-total-size-bar";
        const string k_UxmlIdentifier_TreeView = "comparison-view__tree-view";
        const string k_UxmlIdentifier_TreeViewColumn__Description = "comparison-view__tree-view__column__description";
        const string k_UxmlIdentifier_TreeViewColumn__CountDelta = "comparison-view__tree-view__column__count-delta";
        const string k_UxmlIdentifier_TreeViewColumn__SizeDeltaBar = "comparison-view__tree-view__column__size-delta-bar";
        const string k_UxmlIdentifier_TreeViewColumn__SizeDelta = "comparison-view__tree-view__column__size-delta";
        const string k_UxmlIdentifier_TreeViewColumn__TotalSizeInA = "comparison-view__tree-view__column__total-size-in-a";
        const string k_UxmlIdentifier_TreeViewColumn__TotalSizeInB = "comparison-view__tree-view__column__total-size-in-b";
        const string k_UxmlIdentifier_TreeViewColumn__CountInA = "comparison-view__tree-view__column__count-in-a";
        const string k_UxmlIdentifier_TreeViewColumn__CountInB = "comparison-view__tree-view__column__count-in-b";
        const string k_UxmlIdentifier_FlattenToggle = "comparison-view__toolbar__flatten-toggle";
        const string k_UxmlIdentifier_UnchangedToggle = "comparison-view__toolbar__unchanged-toggle";
        const string k_UxmlIdentifier_StatusToggle = "comparison-view__toolbar__status-toggle";
        protected const string k_UxmlIdentifier_TreeViewColumn__StatusInA = "comparison-view__tree-view__column__status-in-a";
        protected const string k_UxmlIdentifier_TreeViewColumn__StatusInB = "comparison-view__tree-view__column__status-in-b";
        const string k_UxmlIdentifier_LoadingOverlay = "comparison-view__loading-overlay";
        const string k_UxmlIdentifier_BaseTitleLabel = "comparison-view__secondary__base-title-label";
        const string k_UxmlIdentifier_BaseDescriptionLabel = "comparison-view__secondary__base-description-label";
        const string k_UxmlIdentifier_BaseViewContainer = "comparison-view__secondary__base-table-container";
        const string k_UxmlIdentifier_ComparedTitleLabel = "comparison-view__secondary__compared-title-label";
        const string k_UxmlIdentifier_ComparedDescriptionLabel = "comparison-view__secondary__compared-description-label";
        const string k_UxmlIdentifier_ComparedViewContainer = "comparison-view__secondary__compared-table-container";
        const string k_UxmlIdentifier_ErrorLabel = "comparison-view__error-label";
        const string k_UxmlIdentifier_Pager = "comparison-view__pager";

        static protected string SpecificComparisonViewSessionStateKeyName => typeof(TBaseModel).Name;
        static readonly string k_SessionStateKey_FlattenHierarchy = $"com.unity.memoryprofiler.ComparisonViewController.{SpecificComparisonViewSessionStateKeyName}.flattenHierarchy";
        static readonly string k_SessionStateKey_IncludeUnchanged = $"com.unity.memoryprofiler.ComparisonViewController.{SpecificComparisonViewSessionStateKeyName}.includeUnchanged";
        static readonly string k_SessionStateKey_CompareExtraData = $"com.unity.memoryprofiler.ComparisonViewController.{SpecificComparisonViewSessionStateKeyName}.compareExtraData";

        /// <summary>
        /// Maps each sortable column to the compact-row sort key for the flattened, paged view's native
        /// multi-column index sort. Shared across every comparison view since <see cref="ComparisonTableModel{TBaseModel,TBaseModelTreeItemData,TModelSpecificData}.ComparisonData"/>'s
        /// blittable columns are the same regardless of <c>TModelSpecificData</c>. The Description (name) column
        /// is absent because name sorting goes through the model's managed path instead.
        /// </summary>
        static readonly Dictionary<string, ComparisonFlatRowComparer.Column> k_FlatSortColumns = new()
        {
            { k_UxmlIdentifier_TreeViewColumn__CountDelta, ComparisonFlatRowComparer.Column.CountDelta },
            { k_UxmlIdentifier_TreeViewColumn__SizeDelta, ComparisonFlatRowComparer.Column.SizeDelta },
            { k_UxmlIdentifier_TreeViewColumn__TotalSizeInA, ComparisonFlatRowComparer.Column.TotalSizeInA },
            { k_UxmlIdentifier_TreeViewColumn__TotalSizeInB, ComparisonFlatRowComparer.Column.TotalSizeInB },
            { k_UxmlIdentifier_TreeViewColumn__CountInA, ComparisonFlatRowComparer.Column.CountInA },
            { k_UxmlIdentifier_TreeViewColumn__CountInB, ComparisonFlatRowComparer.Column.CountInB },
            { k_UxmlIdentifier_TreeViewColumn__StatusInA, ComparisonFlatRowComparer.Column.StatusInA },
            { k_UxmlIdentifier_TreeViewColumn__StatusInB, ComparisonFlatRowComparer.Column.StatusInB },
        };

        // Static template of per-column sort comparisons for the columns that are always present.
        // Subclasses extend this via RegisterSortComparison() which writes into the instance copy.
        static readonly Dictionary<string, Comparison<TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>.ComparisonData>>> k_SortComparisons = new()
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

        // Per-instance copy that subclasses extend (e.g. adding status column sort comparisons).
        readonly Dictionary<string, Comparison<TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>.ComparisonData>>> m_SortComparisons;
        protected override Dictionary<string, Comparison<TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>.ComparisonData>>> SortComparisons => m_SortComparisons;

        /// <summary>Registers a sort comparison for a column that was added by a subclass (e.g. status columns).</summary>
        protected void RegisterSortComparison(
            string columnId,
            Comparison<TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>.ComparisonData>> comparison)
            => m_SortComparisons[columnId] = comparison;

        // Model.
        readonly string m_Description;
        readonly Func<CachedSnapshot, CachedSnapshot, TBaseModelBuilderArgs, TreeComparisonBuilder.BuildArgs, ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>> m_BuildModel;

        // Composed components - using orchestrator pattern for consistency
        ComparisonModelBuildOrchestrator<ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>, ComparisonModelBuilderArgs> m_BuildOrchestrator;
        ComparisonTableColumnManager<ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>.ComparisonData> m_ColumnManager;
        IExclusiveStatusFilterManager m_TableFilterManager;

        // View.
        Label m_DescriptionLabel;
        ToolbarSearchField m_SearchField;
        StatusFilterButton m_StatusFilterButton;
        Toggle m_FlattenToggle;
        Toggle m_UnchangedToggle;
        Toggle m_ExtraDataToggle;
        TwoPaneSplitView m_SplitView;
        DetailedSizeBar m_TotalSizeBarA;
        DetailedSizeBar m_TotalSizeBarB;
        Label m_BaseTitleLabel;
        Label m_ComparedTitleLabel;
        PaginationControl m_Pager;

        /// <summary>The paged model owns a native row store that must be disposed when it is replaced.</summary>
        ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData> m_OwnedModel;
        FlatPagerBinding<ComparisonFlatRowComparer.Column> m_PagerBinding;

        public ComparisonViewController(
            CachedSnapshot snapshotA,
            CachedSnapshot snapshotB,
            string description,
            Func<CachedSnapshot, CachedSnapshot, TBaseModelBuilderArgs, TreeComparisonBuilder.BuildArgs, ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>> buildModel)
            : base(idOfDefaultColumnWithPercentageBasedWidth: k_UxmlIdentifier_TreeViewColumn__Description)
        {
            SnapshotA = snapshotA;
            SnapshotB = snapshotB;
            m_Description = description;
            m_BuildModel = buildModel;
            m_SortComparisons = new Dictionary<string, Comparison<TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>.ComparisonData>>>(k_SortComparisons);

            // Initialize comparison model build orchestrator with functional adapter
            m_BuildOrchestrator = new ComparisonModelBuildOrchestrator<
                ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>,
                ComparisonModelBuilderArgs>(
                snapshotA,
                snapshotB,
                new FunctionalComparisonModelBuilderAdapter(buildModel),
                typeof(ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>).ToString());
        }

        protected override ToolbarSearchField SearchField => m_SearchField;
        protected StatusFilterButton StatusFilterButton => m_StatusFilterButton;
        protected virtual bool ShowFilterButton => true;
        /// <summary>Bitmask (bit N = ObjectStatus N) of statuses that will never appear in this comparison table. Passed to the status filter dropdown to disable their toggles.</summary>
        protected virtual ulong UnavailableStatusesForTable => 0;

        protected virtual bool ShowFlattenToggle => false;

        protected bool Flatten => m_FlattenToggle.value;
        protected bool IncludeUnchanged => m_UnchangedToggle.value;
        protected bool CompareExtraData => m_ExtraDataToggle.value;

        protected virtual bool ShowExtraDataToggle => false;
        protected virtual string ExtraDataToggleLabel => TextContent.CompareStatusToggleText;

        protected CachedSnapshot SnapshotA { get; }

        protected CachedSnapshot SnapshotB { get; }

        protected MultiColumnTreeView TreeView => m_TreeView;

        protected VisualElement BaseViewContainer { get; private set; }

        protected Label BaseDescriptionLabel { get; private set; }

        protected VisualElement ComparedViewContainer { get; private set; }

        protected Label ComparedDescriptionLabel { get; private set; }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");
            view.style.flexGrow = 1;

            GatherReferencesInView(view);

            return view;
        }

        protected override void ViewLoaded()
        {
            // Child view controllers must exist before base.ViewLoaded() runs ConfigureTreeView()
            // and before TriggerBuild() starts the comparison model build.
            var sameSession = SnapshotA.MetaData.SessionGUID != MetaData.InvalidSessionGUID
                && SnapshotA.MetaData.SessionGUID == SnapshotB.MetaData.SessionGUID;
            InitializeChildViewControllers(sameSession);

            m_SplitView.RegisterCallback<GeometryChangedEvent>(ConfigureSplitViewLayout);

            m_DescriptionLabel.text = m_Description;
            UIElementsHelper.SetVisibility(m_FlattenToggle, ShowFlattenToggle);
            UIElementsHelper.SetVisibility(m_StatusFilterButton, ShowFilterButton);
            m_TableFilterManager = new TableFilterManagerBase();
            m_StatusFilterButton?.SetUp(m_TableFilterManager, () => { if (IsViewLoaded) FireAndForgetBuildModelAsync(); }, UnavailableStatusesForTable);

            var flattenToggleInitialValue = SessionState.GetBool(k_SessionStateKey_FlattenHierarchy, false);
            m_FlattenToggle.SetValueWithoutNotify(flattenToggleInitialValue);
            m_FlattenToggle.text = TextContent.FlattenHierarchy;
            m_FlattenToggle.RegisterValueChangedCallback(ApplyFilter);
            // Subscribe to header context menu so the built-in column show/hide items are preserved
            // (without a subscriber, TreeViewController clears the menu entirely)
            HeaderContextMenuPopulateEvent += OnComparisonHeaderContextMenu;

            var unchangedToggleInitialValue = SessionState.GetBool(k_SessionStateKey_IncludeUnchanged, false);
            m_UnchangedToggle.SetValueWithoutNotify(unchangedToggleInitialValue);
            m_UnchangedToggle.text = TextContent.ShowUnchanged;
            m_UnchangedToggle.RegisterValueChangedCallback(ApplyFilter);
            UIElementsHelper.SetVisibility(m_ExtraDataToggle, ShowExtraDataToggle);

            var extraDataToggleInitialValue = SessionState.GetBool(k_SessionStateKey_CompareExtraData, false);
            m_ExtraDataToggle.SetValueWithoutNotify(extraDataToggleInitialValue);
            // a non-default value for the extra-data toggle may require adding more columns to be visible, so trigger the callback
            if (extraDataToggleInitialValue)
                OnExtraDataToggleValueChanged(extraDataToggleInitialValue);
            m_ExtraDataToggle.text = ExtraDataToggleLabel;
            m_ExtraDataToggle.RegisterValueChangedCallback(OnExtraDataToggleChanged);

            m_BaseTitleLabel.text = TextContent.BaseTableTitle;
            m_ComparedTitleLabel.text = TextContent.ComparedTableTitle;
            base.ViewLoaded();
            TriggerBuild();
        }

        // Override to create and register the base (A) and compared (B) child view controllers.
        // Called synchronously before the comparison view's own UI setup and model build are triggered.
        // SetColumnsVisibilityAsync and SetFiltersAsync are synchronous before the child view is loaded.
        protected abstract void InitializeChildViewControllers(bool sameSession);

        /// <summary>
        /// Registers a child table view controller for the comparison bottom-panel (base or compared):
        /// inserts its <see cref="ViewController.View"/> into <paramref name="container"/>, adds it
        /// as a child, and wires up the sub-table header context menu via
        /// <see cref="GenerateContextMenuForSubtables"/>. Does <em>not</em> trigger a build — comparison
        /// child views start empty with <c>ShowNoItemsAtAllFilter</c> and build only when the user selects
        /// a row in the comparison table. Use
        /// <see cref="BreakdownViewController.AddAndBuildChildTableView{TModel,TData}"/> when an
        /// immediate build is needed instead.
        /// </summary>
        protected void AddEmptyChildTableView<TModel, TData>(
            TreeViewController<TModel, TData> tableViewController,
            VisualElement container)
            where TModel : TreeModel<TData>
            where TData : INamedTreeItemData
        {
            container.Add(tableViewController.View);
            AddChild(tableViewController);
            tableViewController.HeaderContextMenuPopulateEvent += GenerateContextMenuForSubtables;
        }

        /// <summary>
        /// Header context menu handler for the base/compared sub-tables. Subscribing a (no-op) handler
        /// preserves the built-in column show/hide items from MultiColumnTreeView — without a subscriber,
        /// <see cref="TreeViewController{TModel,TTreeItemData}"/> clears the menu entirely. Comparison
        /// views don't yet offer table-mode switching, so there is nothing extra to add here.
        /// The main comparison table is handled separately by <see cref="OnComparisonHeaderContextMenu"/>.
        /// </summary>
        protected virtual void GenerateContextMenuForSubtables(ContextualMenuPopulateEvent evt)
        {
        }

        protected CompoundSearchFilter BuildTextFilterFromSearchText()
        {
            var searchText = m_SearchField.value;
            // ToolbarSearchField provides an empty (non-null) string when the search bar is empty. In that case we don't want a valid filter for "", i.e. "any text".
            if (string.IsNullOrEmpty(searchText))
                searchText = null;
            return CompoundSearchFilter.Create(searchText, m_TableFilterManager?.ExclusiveStatusMask);
        }

        void GatherReferencesInView(VisualElement view)
        {
            m_DescriptionLabel = view.Q<Label>(k_UxmlIdentifier_DescriptionLabel);
            m_SearchField = view.Q<ToolbarSearchField>(k_UxmlIdentifier_SearchField);
            m_StatusFilterButton = view.Q<StatusFilterButton>(k_UxmlIdentifier_StatusFilterButton);
            m_SplitView = view.Q<TwoPaneSplitView>(k_UxmlIdentifier_SplitView);
            m_TotalSizeBarA = view.Q<DetailedSizeBar>(k_UxmlIdentifier_BaseTotalSizeBar);
            m_TotalSizeBarB = view.Q<DetailedSizeBar>(k_UxmlIdentifier_ComparedTotalSizeBar);
            m_TreeView = view.Q<MultiColumnTreeView>(k_UxmlIdentifier_TreeView);
            m_FlattenToggle = view.Q<Toggle>(k_UxmlIdentifier_FlattenToggle);
            m_UnchangedToggle = view.Q<Toggle>(k_UxmlIdentifier_UnchangedToggle);
            m_ExtraDataToggle = view.Q<Toggle>(k_UxmlIdentifier_StatusToggle);
            m_LoadingOverlay = view.Q<ActivityIndicatorOverlay>(k_UxmlIdentifier_LoadingOverlay);
            m_BaseTitleLabel = view.Q<Label>(k_UxmlIdentifier_BaseTitleLabel);
            BaseDescriptionLabel = view.Q<Label>(k_UxmlIdentifier_BaseDescriptionLabel);
            BaseViewContainer = view.Q<VisualElement>(k_UxmlIdentifier_BaseViewContainer);
            m_ComparedTitleLabel = view.Q<Label>(k_UxmlIdentifier_ComparedTitleLabel);
            ComparedDescriptionLabel = view.Q<Label>(k_UxmlIdentifier_ComparedDescriptionLabel);
            ComparedViewContainer = view.Q<VisualElement>(k_UxmlIdentifier_ComparedViewContainer);
            m_ErrorLabel = view.Q<Label>(k_UxmlIdentifier_ErrorLabel);
            m_Pager = view.Q<PaginationControl>(k_UxmlIdentifier_Pager);
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

            // Initialize column manager for consistent column configuration
            m_ColumnManager = new ComparisonTableColumnManager<ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>.ComparisonData>(m_TreeView);

            m_PagerBinding = new FlatPagerBinding<ComparisonFlatRowComparer.Column>(
                m_Pager,
                m_ColumnManager.SetColumnTooltip,
                k_UxmlIdentifier_TreeViewColumn__Description,
                k_UxmlIdentifier_TreeViewColumn__SizeDeltaBar,
                k_FlatSortColumns,
                () => ComparisonFlatRowComparer.Column.SizeDelta,
                TextContent.DescriptionTableColumnHeader,
                ComparisonFlatRowComparer.k_MaxColumns,
                () => m_Model,
                () => { m_TreeView.SetRootItems(m_Model.RootNodes); m_TreeView.RefreshItems(); });

            // Configure standard comparison columns using column manager
            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Description,
                TextContent.DescriptionTableColumnHeader,
                0,
                BindCellForDescriptionColumn(),
                MakeDescriptionCell(),
                DestroyDescriptionCell());

            m_ColumnManager.ConfigureCountDeltaColumn(
                k_UxmlIdentifier_TreeViewColumn__CountDelta,
                TextContent.CountDifferenceTableColumnHeader,
                item => item.CountDelta);

            m_ColumnManager.ConfigureSizeDeltaBarColumn(
                k_UxmlIdentifier_TreeViewColumn__SizeDeltaBar,
                TextContent.SizeDifferenceBarTableColumnHeader,
                item => item.SizeDelta,
                () => m_Model.LargestAbsoluteSizeDelta);

            m_ColumnManager.ConfigureSizeDeltaColumn(
                k_UxmlIdentifier_TreeViewColumn__SizeDelta,
                TextContent.SizeDifferenceTableColumnHeader,
                item => item.SizeDelta);

            m_ColumnManager.ConfigureSizeColumn(
                k_UxmlIdentifier_TreeViewColumn__TotalSizeInA,
                string.Format(TextContent.SizeInTableColumnHeader, TextContent.SnapshotTagBase),
                item => Convert.ToInt64(item.TotalSizeInA.Committed));

            m_ColumnManager.ConfigureSizeColumn(
                k_UxmlIdentifier_TreeViewColumn__TotalSizeInB,
                string.Format(TextContent.SizeInTableColumnHeader, TextContent.SnapshotTagCompare),
                item => Convert.ToInt64(item.TotalSizeInB.Committed));

            m_ColumnManager.ConfigureCountColumn(
                k_UxmlIdentifier_TreeViewColumn__CountInA,
                string.Format(TextContent.CountInTableColumnHeader, TextContent.SnapshotTagBase),
                item => Convert.ToInt32(item.CountInA));

            m_ColumnManager.ConfigureCountColumn(
                k_UxmlIdentifier_TreeViewColumn__CountInB,
                string.Format(TextContent.CountInTableColumnHeader, TextContent.SnapshotTagCompare),
                item => Convert.ToInt32(item.CountInB));

            // Let subclasses bind their concrete ExtraData.StatusBitmaskInA/B fields
            ConfigureStatusColumns();
        }

        protected abstract TBaseModelBuilderArgs GetBaseModelBuilderArgs(CompoundSearchFilter searchFilter, bool sameSessionComparison);

        protected virtual TreeComparisonBuilder.BuildArgs GetComparisonModelBuilderArgs()
        {
            SessionState.SetBool(k_SessionStateKey_FlattenHierarchy, Flatten);
            SessionState.SetBool(k_SessionStateKey_IncludeUnchanged, IncludeUnchanged);
            SessionState.SetBool(k_SessionStateKey_CompareExtraData, CompareExtraData);
            // TODO: fix comparison tables to work with resident memory info
            return new TreeComparisonBuilder.BuildArgs(IncludeUnchanged, AllTrackedMemoryTableMode.OnlyCommitted, CompareExtraData);
        }

        protected override Func<ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>> GetModelBuilderTask(CancellationToken cancellationToken)
        {
            // Build arguments for the comparison model
            var sameSessionComparison = SnapshotA.MetaData.SessionGUID != MetaData.InvalidSessionGUID &&
                                       SnapshotA.MetaData.SessionGUID == SnapshotB.MetaData.SessionGUID;
            var itemNameFilter = BuildTextFilterFromSearchText();
            var baseModelArgs = GetBaseModelBuilderArgs(itemNameFilter, sameSessionComparison);
            var comparisonArgs = GetComparisonModelBuilderArgs();

            var args = new ComparisonModelBuilderArgs(baseModelArgs, comparisonArgs);
            return m_BuildOrchestrator.CreateBuildTask(args, cancellationToken);
        }

        /// <summary>
        /// Sorting the flattened, paged model can't go through TreeModel.Sort (which only sees the current
        /// page). Re-order the native store by the active column(s) instead; OnModelRebuild materializes the
        /// resulting page 0 back on the main thread. Non-paged models (tree mode, or any comparison view that
        /// never flattens) fall back to the base's managed sort unchanged. The table header allows mixing the
        /// name (Description) column with size/status columns at any priority; SortFlatMixed honors that order,
        /// staying on the fast native path when name isn't involved.
        /// </summary>
        protected override Func<System.Threading.Tasks.Task<ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>>, ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>> GetModelSorterTask(CancellationToken cancellationToken)
        {
            // Whether the outgoing m_Model is paged says nothing about the incoming one - Flatten can toggle
            // between builds - so capture both the header state and the base (managed) sorter unconditionally
            // here on the main thread, then dispatch on task.Result.IsPaged inside the continuation.
            var sort = m_PagerBinding.ReadSortState(m_TreeView);
            var baseSorter = base.GetModelSorterTask(cancellationToken);

            return task =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var model = task.Result;
                if (model == null || !model.IsPaged)
                    return baseSorter(task);

                // This runs on a background thread-pool thread, so materialize: false defers materializing the
                // page until OnModelRebuild is back on the main thread.
                if (sort.Levels.Length > 0)
                    model.SortFlatMixed(sort.Levels, materialize: false);
                else
                    // No active size/status/name sort: fall back to the UXML default (Size Delta, descending).
                    model.SortFlatMixed(new[] { new FlatSortLevel<ComparisonFlatRowComparer.Column> { Column = m_PagerBinding.DefaultColumn, Descending = true } }, materialize: false);
                return model;
            };
        }

        protected override void OnModelRebuild(bool success, bool modelHasNewTreeViewIds, Exception exception = null)
        {
            // GetModelSorterTask defers materializing the paged model's current page to here: the background
            // sort continuation runs on a thread-pool thread, and RootNodes is also read by the UI-bound tree
            // view, so mutating it off the main thread would race that read. Materialize now, on the main
            // thread, before the base call reads it via RefreshView -> SetRootItems.
            if (success && m_Model != null && m_Model.IsPaged)
                m_Model.MaterializePage(m_Model.CurrentPage);

            // Every ItemData a paged comparison row hands to UIToolkit is a redirect into the row source, so
            // this call must come first: it sets m_Model and drives RefreshView -> SetRootItems, replacing
            // UIToolkit's node-to-data dictionary with the new model's redirects before the old model is
            // disposed below.
            base.OnModelRebuild(success, modelHasNewTreeViewIds, exception);

            if (!success || m_Model == null)
            {
                m_PagerBinding.SyncTo(null);
                return;
            }

            // Free the native store of the model this build replaced.
            DisposableModelSwap.ReplaceIfChanged(ref m_OwnedModel, m_Model);

            m_PagerBinding.SyncTo(m_Model);
        }

        protected override void OnViewReloaded(bool success)
        {
            base.OnViewReloaded(success);

            var itemNameFilter = BuildTextFilterFromSearchText();
            TrackUsageAnalytics(itemNameFilter?.SearchStringFilter);
        }

        protected virtual void TrackUsageAnalytics(IScopedFilter<string> itemNameFilter) { }

        protected override void RefreshView(bool restoreSelection = true)
        {
            var maxValue = Math.Max(m_Model.TotalSnapshotSizeA.Committed, m_Model.TotalSnapshotSizeB.Committed);
            SetDetailedProgressBarValues(m_TotalSizeBarA, m_Model.TotalSizeA.Committed, m_Model.TotalSnapshotSizeA.Committed, maxValue);
            SetDetailedProgressBarValues(m_TotalSizeBarB, m_Model.TotalSizeB.Committed, m_Model.TotalSnapshotSizeB.Committed, maxValue);

            base.RefreshView(restoreSelection);
        }

        void SetDetailedProgressBarValues(DetailedSizeBar detailedSizeBar, ulong totalMemorySize, ulong totalSnapshotMemorySize, ulong maxValue)
        {
            detailedSizeBar.Bar.Mode = MemoryBarElement.VisibilityMode.CommittedOnly;
            detailedSizeBar.SetValue(new MemorySize(totalMemorySize, 0), totalSnapshotMemorySize, maxValue);

            var totalMemorySizeText = EditorUtility.FormatBytes((long)totalMemorySize);
            detailedSizeBar.SetSizeText(string.Format(TextContent.AllocatedMemoryInTableFormat, totalMemorySizeText), $"{totalMemorySize:N0} B");

            var totalSnapshotMemorySizeText = EditorUtility.FormatBytes((long)totalSnapshotMemorySize);
            detailedSizeBar.SetTotalText(string.Format(TextContent.TotalMemoryInSnapshotFormat, totalSnapshotMemorySizeText), $"{totalSnapshotMemorySize:N0} B");
        }

        protected virtual Func<VisualElement> MakeDescriptionCell() => () => new Label();

        protected virtual Action<VisualElement> DestroyDescriptionCell() => null;

        protected virtual Action<VisualElement, int> BindCellForDescriptionColumn()
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>.ComparisonData>(rowIndex);
                var name = itemData.Name;
                if (string.IsNullOrEmpty(name))
                    name = CachedSnapshot.InvalidItemName;

                // UITK Label supports undocumented escape formatting
                // We need to escape all `\` to make sure that paths don't trigger it
                name = name.Replace("\\", "\\\\");

                var cell = element.Q<Label>();
                cell.text = name;
            };
        }

        /// <summary>
        /// Override in subclasses to configure the Status In A / Status In B columns.
        /// Provide a callback that extracts both statuses from the concrete ComparisonData type.
        /// </summary>
        protected virtual void ConfigureStatusColumns() { }

        /// <summary>
        /// Configures both Status In A and Status In B columns from a single callback that returns
        /// the status bitmasks for a row. Each bitmask encodes which <see cref="ObjectStatus"/>
        /// values are present in the group (bit N = status N present; 0 = absent on that side).
        /// Cells show the single status name when uniform, or "Mixed" with a tooltip listing all
        /// present statuses when a group contains objects with differing statuses.
        /// </summary>
        protected void ConfigureStatusColumns(Func<int, (ObjectStatusWarningInfo infoA, int countInA, ObjectStatusWarningInfo infoB, int countInB, int childCount)> getData)
        {
            var columnA = m_TreeView.columns[k_UxmlIdentifier_TreeViewColumn__StatusInA];
            columnA.title = TextContent.StatusInAColumnHeader;
            columnA.width = 140;
            columnA.sortable = true;
            columnA.makeCell = () => new StatusCell();
            columnA.bindCell = BindCellForStatusColumn(getData, isColumnA: true);

            var columnB = m_TreeView.columns[k_UxmlIdentifier_TreeViewColumn__StatusInB];
            columnB.title = TextContent.StatusInBColumnHeader;
            columnB.width = 140;
            columnB.sortable = true;
            columnB.makeCell = () => new StatusCell();
            columnB.bindCell = BindCellForStatusColumn(getData, isColumnA: false);
        }

        protected virtual Action<VisualElement, int> BindCellForStatusColumn(Func<int, (ObjectStatusWarningInfo infoA, int countInA, ObjectStatusWarningInfo infoB, int countInB, int childCount)> getData, bool isColumnA)
        {
            return (element, rowIndex) =>
            {
                var cell = (StatusCell)element;
                var (infoA, countInA, infoB, countInB, _) = getData(rowIndex);
                cell.SetStatusForComparison(isColumnA ? infoA : infoB, isColumnA ? countInA : countInB);
            };
        }

        /// <summary>
        /// Shows or hides both Status In A and Status In B columns.
        /// Call from <see cref="OnExtraDataToggleValueChanged"/> in subclasses whose extra data includes status.
        /// </summary>
        protected void SetStatusColumnsVisible(bool visible)
        {
            if (m_TreeView == null)
                return;
            m_TreeView.columns[k_UxmlIdentifier_TreeViewColumn__StatusInA].visible = visible;
            m_TreeView.columns[k_UxmlIdentifier_TreeViewColumn__StatusInB].visible = visible;
        }


        public static void GetTreeViewIdsOfLeafNodesRecursively(ref List<int> treeViewIdsInA, ref List<int> treeViewIdsInB,
            IEnumerable<TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>.ComparisonData>> selectedItems)
        {
            foreach (var selected in selectedItems)
            {
                if (selected.hasChildren)
                {
                    GetTreeViewIdsOfLeafNodesRecursively(ref treeViewIdsInA, ref treeViewIdsInB, selected.children);
                }
                else
                {
                    if (selected.data.TreeViewIdsInA != null)
                        treeViewIdsInA.AddRange(selected.data.TreeViewIdsInA);
                    if (selected.data.TreeViewIdsInB != null)
                        treeViewIdsInB.AddRange(selected.data.TreeViewIdsInB);
                }
            }
        }

        void ApplyFilter(ChangeEvent<bool> evt)
        {
            // ApplyFilter is a listener to UI input (Unchanged filter changes) and therefore doesn't expect an awaitable task.
            // No caller or following code expects any part of the async build to be done.
            // Therefore: Fire-and-forget with proper error handling.
            FireAndForgetBuildModelAsync(false);
        }

        /// <summary>
        /// With this method subscribed to <see cref="UI.TreeViewController.HeaderContextMenuPopulateEvent"/>
        /// a default context menu is generated with options to toggle column visibility.
        /// Since comparison views do not yet support any other view mode than <see cref="AllTrackedMemoryTableMode.OnlyCommitted"/>
        /// and don't support switching between modes, we don't need to add to the menu yet,
        /// being subscribed to the event is enough to override the default behavior of absolutely empty context menus.
        ///
        /// This handles the column header context menu for the comparison table.
        /// The sub-tables might have a different behavior and are handled separately.
        /// </summary>
        void OnComparisonHeaderContextMenu(ContextualMenuPopulateEvent evt)
        {
            // No-op: preserves the built-in column show/hide items from MultiColumnTreeView.
            // Without a subscriber, TreeViewController.GenerateContextMenu clears the menu.
        }

        void OnExtraDataToggleChanged(ChangeEvent<bool> evt)
        {
            OnExtraDataToggleValueChanged(evt.newValue);
            // Rebuild so that extra-data differences are factored into HasChanged.
            FireAndForgetBuildModelAsync(false);
        }

        /// <summary>
        /// Called when the extra-data toggle changes value, before the model rebuild is triggered.
        /// Override to react to the new value, e.g. to show/hide columns that display extra data.
        /// </summary>
        protected virtual void OnExtraDataToggleValueChanged(bool newValue) { }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                m_PagerBinding?.Detach();

            // base.Dispose cancels and waits for any in-flight background sort/filter (AsyncModelBuildCoordinator)
            // before returning. That has to happen before the native-buffer release below, or a still-running
            // SortFlat/ApplyFilter could dereference memory this frees out from under it.
            base.Dispose(disposing);

            if (disposing)
            {
                // Releases this controller's reference to the row source, freeing the native store.
                DisposableModelSwap.ReplaceIfChanged(ref m_OwnedModel, null);
            }
        }

        // Wrapper struct to hold both base model args and comparison args
        readonly struct ComparisonModelBuilderArgs
        {
            public ComparisonModelBuilderArgs(TBaseModelBuilderArgs baseModelArgs, TreeComparisonBuilder.BuildArgs comparisonArgs)
            {
                BaseModelArgs = baseModelArgs;
                ComparisonArgs = comparisonArgs;
            }

            public TBaseModelBuilderArgs BaseModelArgs { get; }
            public TreeComparisonBuilder.BuildArgs ComparisonArgs { get; }
        }

        // Adapter to bridge functional builder to IComparisonModelBuilder interface
        class FunctionalComparisonModelBuilderAdapter : IComparisonModelBuilder<ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>, ComparisonModelBuilderArgs>
        {
            readonly Func<CachedSnapshot, CachedSnapshot, TBaseModelBuilderArgs, TreeComparisonBuilder.BuildArgs, ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>> m_BuildFunc;

            public FunctionalComparisonModelBuilderAdapter(
                Func<CachedSnapshot, CachedSnapshot, TBaseModelBuilderArgs, TreeComparisonBuilder.BuildArgs, ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData>> buildFunc)
            {
                m_BuildFunc = buildFunc;
            }

            public ComparisonTableModel<TBaseModel, TBaseModelItemData, TModelSpecificData> Build(
                CachedSnapshot snapshotA,
                CachedSnapshot snapshotB,
                ComparisonModelBuilderArgs args)
            {
                return m_BuildFunc(snapshotA, snapshotB, args.BaseModelArgs, args.ComparisonArgs);
            }
        }
    }
}

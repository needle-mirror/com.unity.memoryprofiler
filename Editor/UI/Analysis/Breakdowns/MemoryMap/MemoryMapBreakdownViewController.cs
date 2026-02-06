using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class MemoryMapBreakdownModelBuilderAdapter : IModelBuilder<MemoryMapBreakdownModel, MemoryMapBreakdownModelBuilder.BuildArgs>
    {
        readonly MemoryMapBreakdownModelBuilder m_Builder = new MemoryMapBreakdownModelBuilder();

        public MemoryMapBreakdownModel Build(CachedSnapshot snapshot, MemoryMapBreakdownModelBuilder.BuildArgs args)
        {
            return m_Builder.Build(snapshot, args);
        }

        public MemoryMapBreakdownModel Build(MemoryMapBreakdownModel baseModel, List<int> treeNodeIdFilter)
        {
            return m_Builder.Build(baseModel, treeNodeIdFilter);
        }
    }

    class MemoryMapBreakdownViewController : SingleSnapshotTreeViewController<MemoryMapBreakdownModel, MemoryMapBreakdownModel.ItemData>, IViewControllerWithVisibilityEvents
    {
        const string k_UxmlAssetGuid = "bc16108acf3b6484aa65bf05d6048e8f";

        protected override string UxmlIdentifier_TreeViewColumn__Size => k_UxmlIdentifier_TreeViewColumn__Size;
        protected override string UxmlIdentifier_TreeViewColumn__ResidentSize => k_UxmlIdentifier_TreeViewColumn__ResidentSize;
        const string k_UssClass_Dark = "memory-map-breakdown-view__dark";
        const string k_UssClass_Light = "memory-map-breakdown-view__light";
        const string k_UxmlIdentifier_SearchField = "memory-map-breakdown-view__search-field";
        const string k_UxmlIdentifier_TableDescription = "memory-map-breakdown-view__description";
        const string k_UxmlIdentifier_TableSizeBar = "memory-map-breakdown-view__table-size-bar";
        const string k_UxmlIdentifier_TreeView = "memory-map-breakdown-view__tree-view";
        const string k_UxmlIdentifier_TreeViewColumn__Address = "memory-map-breakdown-view__tree-view__column__address";
        const string k_UxmlIdentifier_TreeViewColumn__Type = "memory-map-breakdown-view__tree-view__column__type";
        const string k_UxmlIdentifier_TreeViewColumn__Size = "memory-map-breakdown-view__tree-view__column__size";
        const string k_UxmlIdentifier_TreeViewColumn__ResidentSize = "memory-map-breakdown-view__tree-view__column__residentsize";
        const string k_UxmlIdentifier_TreeViewColumn__Description = "memory-map-breakdown-view__tree-view__column__description";
        const string k_UxmlIdentifier_LoadingOverlay = "memory-map-breakdown-view__loading-overlay";
        const string k_UxmlIdentifier_ErrorLabel = "memory-map-breakdown-view__error-label";

        // Model.
        readonly CachedSnapshot m_Snapshot;
        protected override Dictionary<string, Comparison<TreeViewItemData<MemoryMapBreakdownModel.ItemData>>> SortComparisons { get; }

        // Composed components
        readonly TableFilterManager<MemoryMapBreakdownModel, MemoryMapBreakdownModel.ItemData> m_FilterManager;
        readonly ModelBuildOrchestrator<MemoryMapBreakdownModel, MemoryMapBreakdownModelBuilder.BuildArgs> m_BuildOrchestrator;
        TreeViewColumnManager<MemoryMapBreakdownModel.ItemData> m_ColumnManager;
        SizeCellRenderer m_SizeCellRenderer;

        // State
        ISelectionDetails m_SelectionDetails;

        // View.
        Label m_TableDescription;
        ToolbarSearchField m_SearchField;
        DetailedSizeBar m_TableSizeBar;

        public MemoryMapBreakdownViewController(CachedSnapshot snapshot, ISelectionDetails selectionDetails)
            : base(idOfDefaultColumnWithPercentageBasedWidth: null)
        {
            m_Snapshot = snapshot;
            m_SelectionDetails = selectionDetails;
            TableMode = AllTrackedMemoryTableMode.CommittedAndResident;

            // Initialize composed components
            m_FilterManager = new TableFilterManager<MemoryMapBreakdownModel, MemoryMapBreakdownModel.ItemData>();
            m_BuildOrchestrator = new ModelBuildOrchestrator<MemoryMapBreakdownModel, MemoryMapBreakdownModelBuilder.BuildArgs>(
                snapshot,
                new MemoryMapBreakdownModelBuilderAdapter(),
                typeof(MemoryMapBreakdownModel).ToString());

            SearchFilterChanged += OnSearchFilterChanged;

            // Sort comparisons for each column.
            SortComparisons = new()
            {
                { k_UxmlIdentifier_TreeViewColumn__Address, (x, y) => x.data.Address.CompareTo(y.data.Address) },
                { k_UxmlIdentifier_TreeViewColumn__Size, (x, y) => x.data.TotalSize.Committed.CompareTo(y.data.TotalSize.Committed) },
                { k_UxmlIdentifier_TreeViewColumn__ResidentSize, (x, y) => x.data.TotalSize.Resident.CompareTo(y.data.TotalSize.Resident) },
                { k_UxmlIdentifier_TreeViewColumn__Type, (x, y) => string.Compare(x.data.ItemType, y.data.ItemType, StringComparison.OrdinalIgnoreCase) },
                { k_UxmlIdentifier_TreeViewColumn__Description, (x, y) => string.Compare(x.data.Name, y.data.Name, StringComparison.OrdinalIgnoreCase) },
            };
        }

        protected override ToolbarSearchField SearchField => m_SearchField;

        public IScopedFilter<string> SearchFilter => m_FilterManager.SearchFilter;

        void OnSearchFilterChanged(IScopedFilter<string> searchFilter)
        {
            SetFilters(searchFilter: searchFilter);
        }

        public void SetFilters(
            MemoryMapBreakdownModel model = null,
            IScopedFilter<string> searchFilter = null,
            bool excludeAll = false)
        {
            _ = SetFiltersAsync(model, searchFilter, excludeAll);
        }

        public async Task SetFiltersAsync(
            MemoryMapBreakdownModel model = null,
            IScopedFilter<string> searchFilter = null,
            bool excludeAll = false)
        {
            m_FilterManager.SearchFilter = searchFilter;
            m_FilterManager.TreeNodeIdFilter = excludeAll ? new List<int>() : null;
            m_FilterManager.BaseModelForTreeNodeIdFiltering = model;

            // Update base class members for compatibility
            m_TreeNodeIdFilter = m_FilterManager.TreeNodeIdFilter;
            m_BaseModelForTreeNodeIdFiltering = m_FilterManager.BaseModelForTreeNodeIdFiltering;

            await m_FilterManager.ApplyFiltersAsync(() => BuildModelAsync(false), IsViewLoaded);
        }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");
            view.style.flexGrow = 1;

            var themeUssClass = (EditorGUIUtility.isProSkin) ? k_UssClass_Dark : k_UssClass_Light;
            view.AddToClassList(themeUssClass);
            return view;
        }

        protected override void ViewLoaded()
        {
            GatherViewReferences();
            base.ViewLoaded();

            // These styles are not supported in Unity 2020 and earlier. They will cause project errors if included in the stylesheet in those Editor versions.
            // Remove when we drop support for <= 2020 and uncomment these styles in the stylesheet.
            var transitionDuration = new StyleList<TimeValue>(new List<TimeValue>() { new TimeValue(0.23f) });
            var transitionTimingFunction = new StyleList<EasingFunction>(new List<EasingFunction>() { new EasingFunction(EasingMode.EaseOut) });
            m_LoadingOverlay.style.transitionDuration = transitionDuration;
            m_LoadingOverlay.style.transitionProperty = new StyleList<StylePropertyName>(new List<StylePropertyName>() { new StylePropertyName("opacity") });
            m_LoadingOverlay.style.transitionTimingFunction = transitionTimingFunction;
        }

        void GatherViewReferences()
        {
            m_TableDescription = View.Q<Label>(k_UxmlIdentifier_TableDescription);
            m_SearchField = View.Q<ToolbarSearchField>(k_UxmlIdentifier_SearchField);
            m_TableSizeBar = View.Q<DetailedSizeBar>(k_UxmlIdentifier_TableSizeBar);
            m_TreeView = View.Q<MultiColumnTreeView>(k_UxmlIdentifier_TreeView);
            m_LoadingOverlay = View.Q<ActivityIndicatorOverlay>(k_UxmlIdentifier_LoadingOverlay);
            m_ErrorLabel = View.Q<Label>(k_UxmlIdentifier_ErrorLabel);

            // Initialize components that depend on m_TreeView
            m_ColumnManager = new TreeViewColumnManager<MemoryMapBreakdownModel.ItemData>(m_TreeView);
            m_SizeCellRenderer = new SizeCellRenderer(m_TreeView, () => TableMode);
        }

        protected override void ConfigureTreeView()
        {
            base.ConfigureTreeView();

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Address,
                "Address",
                180,
                BindCellForAddressColumn(),
                MakeCellForAddressColumn);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Size,
                "Allocated Size",
                100,
                m_SizeCellRenderer.CreateMemorySizeBinding<MemoryMapBreakdownModel.ItemData>(
                    item => item.TotalSize),
                MakeCellForSizeColumn,
                visible: TableMode != AllTrackedMemoryTableMode.OnlyResident);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__ResidentSize,
                "Resident Size",
                100,
                BindCellForResidentSizeColumn(),
                visible: m_Snapshot.HasSystemMemoryResidentPages && TableMode != AllTrackedMemoryTableMode.OnlyCommitted);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Type,
                "Type",
                180,
                BindCellForTypeColumn());

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Description,
                "Name",
                0,
                BindCellForNameColumn());
        }

        protected MemoryMapBreakdownModelBuilder.BuildArgs GetModelBuilderArgs()
        {
            // Pass the scoped filter directly for proper hierarchical search
            return new MemoryMapBreakdownModelBuilder.BuildArgs(m_FilterManager.SearchFilter);
        }

        protected override Func<MemoryMapBreakdownModel> GetModelBuilderTask(CancellationToken cancellationToken)
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
            MemoryProfilerAnalytics.AddMemoryMapUsage(m_FilterManager.SearchFilter != null);
        }

        protected override void RefreshView()
        {
            m_TableDescription.text = "A memory map of all allocations reported for the process.";

            var totalMemorySizeText = EditorUtility.FormatBytes((long)m_Model.TotalMemorySize.Committed);
            var totalSnapshotMemorySizeText = EditorUtility.FormatBytes((long)m_Model.TotalSnapshotMemorySize.Committed);
            string sizeText = $"Allocated Memory In Table: {totalMemorySizeText}";
            string totalSizeText = $"Total Allocated Memory In Snapshot: {totalSnapshotMemorySizeText}";

            m_TableSizeBar.SetValue(m_Model.TotalMemorySize, m_Model.TotalMemorySize.Committed, m_Model.TotalSnapshotMemorySize.Committed);
            m_TableSizeBar.SetSizeText(sizeText, $"{m_Model.TotalMemorySize.Committed:N0} B");
            m_TableSizeBar.SetTotalText(totalSizeText, $"{m_Model.TotalSnapshotMemorySize.Committed:N0} B");

            base.RefreshView();
        }

        VisualElement MakeCellForAddressColumn()
        {
            var cell = new Label();
            cell.AddToClassList("memory-map-breakdown-view__tree-view__address");
            return cell;
        }

        Action<VisualElement, int> BindCellForAddressColumn()
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<MemoryMapBreakdownModel.ItemData>(rowIndex);
                ((Label)element).text = $"{(long)itemData.Address:X16}";
            };
        }

        VisualElement MakeCellForSizeColumn()
        {
            var cell = new Label();
            cell.AddToClassList("memory-map-breakdown-view__tree-view__size");
            return cell;
        }

        Action<VisualElement, int> BindCellForResidentSizeColumn()
        {
            return (element, rowIndex) =>
            {
                if (m_TreeView.GetParentIdForIndex(rowIndex) == -1)
                {
                    var itemData = m_TreeView.GetItemDataForIndex<MemoryMapBreakdownModel.ItemData>(rowIndex);
                    ((Label)element).text = EditorUtility.FormatBytes((long)itemData.TotalSize.Resident);
                }
                else
                    ((Label)element).text = "";
            };
        }

        Action<VisualElement, int> BindCellForTypeColumn()
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<MemoryMapBreakdownModel.ItemData>(rowIndex);
                string labelText = itemData.ItemType;
                ((Label)element).text = labelText;
            };
        }

        Action<VisualElement, int> BindCellForNameColumn()
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<MemoryMapBreakdownModel.ItemData>(rowIndex);
                ((Label)element).text = itemData.Name;
            };
        }

        protected override void OnTreeItemSelected(int itemId, MemoryMapBreakdownModel.ItemData itemData)
        {
            ViewController detailsView = BreakdownDetailsViewControllerFactory.Create(m_Snapshot, itemId, itemData.Name, 0, itemData.Source);
            m_SelectionDetails.SetSelection(detailsView);
        }

        void IViewControllerWithVisibilityEvents.ViewWillBeDisplayed()
        {
            // Silent deselection on revisiting this view.
            // The Selection Details panel should stay the same but the selection in the table needs to be cleared
            // So that there is no confusion about what is selected, and so that there is no previously selected item
            // that won't update the Selection Details panel when an attempt to select it is made.
            ClearSelection();
        }

        void IViewControllerWithVisibilityEvents.ViewWillBeHidden()
        {
        }
    }
}

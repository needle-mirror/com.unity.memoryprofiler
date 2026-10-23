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
    class MemoryMapBreakdownModelBuilderAdapter : IModelBuilder<MemoryMapBreakdownModel, MemoryMapBreakdownModelBuilder.BuildArgs>
    {
        readonly MemoryMapBreakdownModelBuilder m_Builder = new MemoryMapBreakdownModelBuilder();

        public MemoryMapBreakdownModel EmptyModel => new MemoryMapBreakdownModel(new(), default);

        public MemoryMapBreakdownModel Build(CachedSnapshot snapshot, MemoryMapBreakdownModelBuilder.BuildArgs args)
        {
            return m_Builder.Build(snapshot, args);
        }

        public MemoryMapBreakdownModel Build(MemoryMapBreakdownModel baseModel, IList<int> treeNodeIdFilter)
        {
            return m_Builder.Build(baseModel, treeNodeIdFilter);
        }

        public void ClearIdToIndexCache()
        {
            m_Builder.ClearIdToIndexCache();
        }
    }

    class MemoryMapTableViewController : SingleSnapshotTreeViewController<MemoryMapBreakdownModel, MemoryMapBreakdownModel.ItemData>, IFilterRelaxationTarget
    {
        const string k_UxmlAssetGuid = "bc16108acf3b6484aa65bf05d6048e8f";

        protected override string UxmlIdentifier_TreeViewColumn__Size => k_UxmlIdentifier_TreeViewColumn__Size;
        protected override string UxmlIdentifier_TreeViewColumn__ResidentSize => k_UxmlIdentifier_TreeViewColumn__ResidentSize;
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
        readonly IResponder m_Responder;
        protected override Dictionary<string, Comparison<TreeViewItemData<MemoryMapBreakdownModel.ItemData>>> SortComparisons { get; }

        public override bool ExcludeAllFilterApplied => m_FilterManager.TreeNodeIdFilter != null && m_FilterManager.TreeNodeIdFilter.Count == 0;

        // Composed components
        readonly TableFilterManager<MemoryMapBreakdownModel, MemoryMapBreakdownModel.ItemData> m_FilterManager;
        readonly ModelBuildOrchestrator<MemoryMapBreakdownModel, MemoryMapBreakdownModelBuilder.BuildArgs> m_BuildOrchestrator;
        TreeViewColumnManager<MemoryMapBreakdownModel.ItemData> m_ColumnManager;
        protected override IColumnSumRefresher ColumnManager => m_ColumnManager;
        SizeCellRenderer m_SizeCellRenderer;

        // View.
        const int k_DefaultColumnWidth = 100;
        const int k_WideColumnWidth = 180;

        public interface IResponder
        {
            void MemoryMapTableViewControllerReloaded(MemoryMapTableViewController viewController, bool success);
            void MemoryMapTableViewControllerSelectedItem(int itemId, MemoryMapTableViewController viewController, MemoryMapBreakdownModel.ItemData itemData);
        }

        public MemoryMapTableViewController(CachedSnapshot snapshot, ToolbarSearchField searchField, StatusFilterButton statusFilterButton, IResponder responder)
            : base(idOfDefaultColumnWithPercentageBasedWidth: null)
        {
            // The Memory Map doesn't use the status filter, so hide it.
            if (statusFilterButton != null)
                UIElementsHelper.SetVisibility(statusFilterButton, false);

            m_Snapshot = snapshot;
            m_Responder = responder;
            SearchField = searchField;
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

        protected override ToolbarSearchField SearchField { get; }

        public MemoryMapBreakdownModel Model => m_Model;

        public IScopedFilter<string> SearchFilter => m_FilterManager.SearchFilter;

        void OnSearchFilterChanged(IScopedFilter<string> searchFilter)
        {
            SetFilters(searchFilter: searchFilter);
        }

        public void SetFilters(
            MemoryMapBreakdownModel model = null,
            IScopedFilter<string> searchFilter = null,
            IList<int> treeNodeIdFilter = null)
        {
            // SetFilters is a listener to UI input (search filter changes) and therefore doesn't expect an awaitable task.
            // No caller or following code expects any part of the asnyc build to be done.
            FireAndForgetFilterAsync(model, searchFilter, treeNodeIdFilter);
        }

        async void FireAndForgetFilterAsync(
            MemoryMapBreakdownModel model = null,
            IScopedFilter<string> searchFilter = null,
            IList<int> treeNodeIdFilter = null)
        {
            try
            {
                await SetFiltersAsync(model, searchFilter, treeNodeIdFilter);
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
            MemoryMapBreakdownModel model = null,
            IScopedFilter<string> searchFilter = null,
            IList<int> treeNodeIdFilter = null)
        {
            m_FilterManager.SearchFilter = searchFilter;
            m_FilterManager.TreeNodeIdFilter = treeNodeIdFilter;

            if (model != m_FilterManager.BaseModelForTreeNodeIdFiltering)
                m_BuildOrchestrator.ClearIdToIndexCache();
            m_FilterManager.BaseModelForTreeNodeIdFiltering = model;

            await m_FilterManager.ApplyFiltersAsync(m_FilterManager.TreeNodeIdFilter != null ?
                    () => FilterModelAsync(m_FilterManager.BaseModelForTreeNodeIdFiltering, m_FilterManager.TreeNodeIdFilter, m_BuildOrchestrator) :
                    () => BuildModelAsync(false), IsViewLoaded);
        }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");

            return view;
        }

        protected override void ViewLoaded()
        {
            GatherViewReferences();
            base.ViewLoaded();
        }

        void GatherViewReferences()
        {
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
                TextContent.AddressTableColumnHeader,
                k_WideColumnWidth,
                BindCellForAddressColumn(),
                makeCell: MakeCellForAddressColumn);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Size,
                TextContent.AllocatedSizeTableColumnHeader,
                k_DefaultColumnWidth,
                m_SizeCellRenderer.CreateMemorySizeBinding<MemoryMapBreakdownModel.ItemData>(
                    item => item.TotalSize),
                makeCell: MakeCellForSizeColumn,
                visible: TableMode != AllTrackedMemoryTableMode.OnlyResident,
                sumProvider: () => m_Model?.TotalMemorySize.Committed ?? 0);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__ResidentSize,
                TextContent.ResidentSizeTableColumnHeader,
                k_DefaultColumnWidth,
                BindCellForResidentSizeColumn(),
                visible: m_Snapshot.HasSystemMemoryResidentPages && TableMode != AllTrackedMemoryTableMode.OnlyCommitted,
                sumProvider: () => m_Model?.TotalMemorySize.Resident ?? 0);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Type,
                TextContent.TypeTableColumnHeader,
                k_WideColumnWidth,
                BindCellForTypeColumn(),
                makeCell: () => MakeLabelWithSelectInTableMenu(),
                destroyCell: cell => cell.Q<ObjectOrTypeLabel>().Dispose());

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Description,
                TextContent.NameTableColumnHeader,
                0,
                BindCellForNameColumn(),
                makeCell: () => MakeLabelWithSelectInTableMenu(hideTypeInfoForObjects: true),
                destroyCell: cell => cell.Q<ObjectOrTypeLabel>().Dispose());
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
            m_Responder?.MemoryMapTableViewControllerReloaded(this, success);
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
                element.Q<Label>().text = $"{(long)itemData.Address:X16}";
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
                    element.Q<Label>().text = EditorUtility.FormatBytes((long)itemData.TotalSize.Resident);
                }
                else
                    element.Q<Label>().text = "";
            };
        }

        Action<VisualElement, int> BindCellForTypeColumn()
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<MemoryMapBreakdownModel.ItemData>(rowIndex);
                string labelText = itemData.ItemType;
                var label = element.Q<ObjectOrTypeLabel>();
                switch (itemData.Source.Id)
                {
                    case CachedSnapshot.SourceIndex.SourceId.ManagedObject:
                    case CachedSnapshot.SourceIndex.SourceId.NativeObject:
                        if (!label.TrySetLabelDataFromSourceIndex(m_Snapshot, itemData.Source, null, splitUnifiedObjectIntoHalves: true))
                        {
                            // Category/group node with no typed source — show name as plain text.
                            label.SetLabelData(m_Snapshot, labelText, string.Empty, dataType: ObjectOrTypeLabel.DataType.PureCSharpType, showDataTypeIcon: true);
                        }
                        else
                            label.NativeObjectName = null; // Type column should not show native names, even for unified types.
                        break;
                    case CachedSnapshot.SourceIndex.SourceId.NativeType:
                    case CachedSnapshot.SourceIndex.SourceId.ManagedType:
                    case CachedSnapshot.SourceIndex.SourceId.NativeRootReference:
                    case CachedSnapshot.SourceIndex.SourceId.GfxResource:
                    case CachedSnapshot.SourceIndex.SourceId.GCHandleIndex:
                    case CachedSnapshot.SourceIndex.SourceId.ManagedRoot:
                    case CachedSnapshot.SourceIndex.SourceId.Scene:
                    case CachedSnapshot.SourceIndex.SourceId.Prefab:
                    case CachedSnapshot.SourceIndex.SourceId.MemoryLabel:
                    case CachedSnapshot.SourceIndex.SourceId.None:
#if DEBUG_VALIDATION
                        Debug.LogWarning($"Unexpected typed source {itemData.Source.Id} for Type column. Labeling as plain text.");
#endif
                        label.SetLabelData(m_Snapshot, labelText, null, dataType: ObjectOrTypeLabel.DataType.NativeUnityType, showDataTypeIcon: true);
                        break;
                    case CachedSnapshot.SourceIndex.SourceId.SystemMemoryRegion:
                    case CachedSnapshot.SourceIndex.SourceId.NativeMemoryRegion:
                    case CachedSnapshot.SourceIndex.SourceId.ManagedHeapSection:
                    case CachedSnapshot.SourceIndex.SourceId.NativeAllocation:
                        // Category/group node with no typed source — show name as plain text.
                        ObjectOrTypeLabel.DataType dataType = itemData.Source.Id switch
                        {
                            CachedSnapshot.SourceIndex.SourceId.ManagedHeapSection => ObjectOrTypeLabel.DataType.PureCSharpType,
                            _ => DataTypeOfContent(itemData.ContentKinds)
                        };
                        label.SetLabelData(m_Snapshot, labelText, null, dataType: dataType, showDataTypeIcon: true);
                        label.SetRowSource(m_Snapshot, itemData.Source);
                        break;
                    default:
                        throw new NotImplementedException($"Unexpected source {itemData.Source.Id} for Type column.");
                }
            };
        }

        internal static ObjectOrTypeLabel.DataType DataTypeOfContent(MemoryMapBreakdownModel.ContentKinds contentKinds)
        {
            var includesManaged = (contentKinds & MemoryMapBreakdownModel.ContentKinds.Managed) != 0;
            var includesNative = (contentKinds & MemoryMapBreakdownModel.ContentKinds.Native) != 0;
            if (includesManaged && includesNative)
                return ObjectOrTypeLabel.DataType.UnifiedUnityType;
            return includesManaged ? ObjectOrTypeLabel.DataType.PureCSharpType : ObjectOrTypeLabel.DataType.NativeUnityType;
        }

        Action<VisualElement, int> BindCellForNameColumn()
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<MemoryMapBreakdownModel.ItemData>(rowIndex);
                var label = element.Q<ObjectOrTypeLabel>();
                if (!label.TrySetLabelDataFromSourceIndexForTableEntry(m_Snapshot, itemData.Source, itemData.Name, out var displayName, splitUnifiedObjectIntoHalves: true))
                {
                    // Category/group node with no typed source — show name as plain text.
                    label.SetLabelData(m_Snapshot, displayName, string.Empty);
                    label.SetRowSource(m_Snapshot, itemData.Source);
                }
            };
        }

        protected override void OnTreeItemSelected(int itemId, MemoryMapBreakdownModel.ItemData itemData)
        {
            m_Responder?.MemoryMapTableViewControllerSelectedItem(itemId, this, itemData);
        }

        public override BreakdownTableId TableId => BreakdownTableId.MemoryMap;

        /// <summary>Whether <paramref name="id"/> is one this table can ever show, independent of any filter.</summary>
        internal static bool CanEverContainSourceId(CachedSnapshot.SourceIndex.SourceId id)
        {
            switch (id)
            {
                case CachedSnapshot.SourceIndex.SourceId.SystemMemoryRegion:
                case CachedSnapshot.SourceIndex.SourceId.NativeMemoryRegion:
                case CachedSnapshot.SourceIndex.SourceId.ManagedHeapSection:
                case CachedSnapshot.SourceIndex.SourceId.NativeAllocation:
                case CachedSnapshot.SourceIndex.SourceId.NativeObject:
                case CachedSnapshot.SourceIndex.SourceId.ManagedObject:
                    return true;
                default:
                    return false;
            }
        }

        public override bool CanEverContain(CachedSnapshot.SourceIndex source) => CanEverContainSourceId(source.Id);

        /// <summary>
        /// Diagnoses this table's one filter dimension (text search against the item's own name) — the Memory
        /// Map has no status filter at all (see the constructor), so status mask never blocks anything here.
        /// </summary>
        public override ItemVisibility GetVisibility(CachedSnapshot.SourceIndex source, CachedSnapshot owner = null)
        {
            if (!CanEverContain(source))
                return new ItemVisibility(ItemVisibility.State.NotRepresentable);

            var filter = m_FilterManager.CompoundSearchFilter;
            if (filter?.SearchStringFilter != null && !filter.MatchesTextName(source.GetName(m_Snapshot)))
                return new ItemVisibility(ItemVisibility.State.HiddenByFilters, TableFilterKind.TextSearch);

            return new ItemVisibility(ItemVisibility.State.Visible);
        }

        protected override bool TryRevealRow(CachedSnapshot.SourceIndex source, bool frame, bool select, CachedSnapshot owner)
            => TryRevealTreeRow(data => data.Source == source, frame, select);

        /// <summary>Only the text-search dimension applies here — the Memory Map has no status filter.</summary>
        public void ApplyFilterRelaxation(TableFilterKind filtersToRelax)
        {
            TableRevealHelper.RelaxStatusAndTextFilters(filtersToRelax, statusFilterButton: null, SearchField, m_FilterManager);
            FireAndForgetBuildModelAsync();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            SearchFilterChanged -= OnSearchFilterChanged;
            if (disposing)
            {
                m_FilterManager.Dispose();
                m_BuildOrchestrator.Dispose();
            }
        }
    }
}

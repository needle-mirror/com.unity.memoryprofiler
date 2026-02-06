using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class UnityObjectsModelBuilderAdapter : IModelBuilder<UnityObjectsModel, UnityObjectsModelBuilder.BuildArgs>
    {
        readonly UnityObjectsModelBuilder m_Builder = new UnityObjectsModelBuilder();

        public UnityObjectsModel Build(CachedSnapshot snapshot, UnityObjectsModelBuilder.BuildArgs args)
        {
            return m_Builder.Build(snapshot, args);
        }

        public UnityObjectsModel Build(UnityObjectsModel baseModel, List<int> treeNodeIdFilter)
        {
            return m_Builder.Build(baseModel, treeNodeIdFilter);
        }
    }

    class UnityObjectsTableViewController : SingleSnapshotTreeViewController<UnityObjectsModel, UnityObjectsModel.ItemData>
    {
        // In contrast to AllTrackedMemory, setting the mode and column visibility for Unity Objects tables does not require a table rebuild
        // The TreeView needs to be refreshed though as the cells changed and need to be rebuild
        protected override bool SwitchingTableModeRequiresRebuild { get; } = false;

        protected override string UxmlIdentifier_TreeViewColumn__Size => k_UxmlIdentifier_TreeViewColumn__Size;
        protected override string UxmlIdentifier_TreeViewColumn__ResidentSize => k_UxmlIdentifier_TreeViewColumn__ResidentSize;
        const string k_UxmlAssetGuid = "e43db30ef43ddfd44bea11154f274126";
        const string k_UssClass_Dark = "unity-objects-table-view__dark";
        const string k_UssClass_Light = "unity-objects-table-view__light";
        const string k_UssClass_Cell_Unreliable = "analysis-view__text__information-unreliable-or-unavailable";
        const string k_UxmlIdentifier_TreeView = "unity-objects-table-view__tree-view";
        const string k_UxmlIdentifier_TreeViewColumn__Description = "unity-objects-table-view__tree-view__column__description";
        const string k_UxmlIdentifier_TreeViewColumn__Size = "unity-objects-table-view__tree-view__column__size";
        const string k_UxmlIdentifier_TreeViewColumn__SizeBar = "unity-objects-table-view__tree-view__column__size-bar";
        const string k_UxmlIdentifier_TreeViewColumn__NativeSize = "unity-objects-table-view__tree-view__column__native-size";
        const string k_UxmlIdentifier_TreeViewColumn__ManagedSize = "unity-objects-table-view__tree-view__column__managed-size";
        const string k_UxmlIdentifier_TreeViewColumn__ResidentSize = "unity-objects-table-view__tree-view__column__resident-size";
        const string k_UxmlIdentifier_TreeViewColumn__GpuSize = "unity-objects-table-view__tree-view__column__gpu-size";
        const string k_UxmlIdentifier_Toolbar = "unity-objects-table-view__toolbar";
        const string k_UxmlIdentifier_FlattenToggle = "unity-objects-table-view__toolbar__flatten-toggle";
        const string k_UxmlIdentifier_DuplicatesToggle = "unity-objects-table-view__toolbar__duplicates-toggle";
        const string k_UxmlIdentifier_LoadingOverlay = "unity-objects-table-view__loading-overlay";
        const string k_UxmlIdentifier_ErrorLabel = "unity-objects-table-view__error-label";
        const string k_Graphics_NotAvailable = "N/A";
        const string k_Graphics_NotAvailable_ToolTip = "The memory profiler cannot certainly attribute which part of the resident memory belongs to graphics." +
            "\n\nChange focus to 'Allocated Memory' to inspect graphics memory." +
            "\n\nWe also recommend using a platform profiler for checking the residency status of graphics memory.";

        // Model.
        readonly CachedSnapshot m_Snapshot;
        readonly bool m_ShowAdditionalOptions;
        readonly IResponder m_Responder;
        protected override Dictionary<string, Comparison<TreeViewItemData<UnityObjectsModel.ItemData>>> SortComparisons { get; }
        bool m_ShowDuplicatesOnly;
        bool m_FlattenHierarchy;

        // Composed components
        readonly TableFilterManager<UnityObjectsModel, UnityObjectsModel.ItemData> m_FilterManager;
        readonly ModelBuildOrchestrator<UnityObjectsModel, UnityObjectsModelBuilder.BuildArgs> m_BuildOrchestrator;
        TreeViewColumnManager<UnityObjectsModel.ItemData> m_ColumnManager;
        SizeCellRenderer m_SizeCellRenderer;

        // View.
        Toolbar m_Toolbar;
        Toggle m_FlattenToggle;
        Toggle m_DuplicatesToggle;

        public UnityObjectsTableViewController(
            CachedSnapshot snapshot,
            ToolbarSearchField searchField = null,
            bool buildOnLoad = true,
            bool showAdditionalOptions = true,
            IResponder responder = null,
            bool disambiguateByInstanceID = false)
            : base(idOfDefaultColumnWithPercentageBasedWidth: k_UxmlIdentifier_TreeViewColumn__Description, buildOnLoad)
        {
            m_Snapshot = snapshot;
            m_SearchField = searchField;
            m_ShowAdditionalOptions = showAdditionalOptions;
            m_Responder = responder;
            DisambiguateByInstanceID = disambiguateByInstanceID;

            // Initialize composed components
            m_FilterManager = new TableFilterManager<UnityObjectsModel, UnityObjectsModel.ItemData>();
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
            };
        }

        ToolbarSearchField m_SearchField = null;
        protected override ToolbarSearchField SearchField => m_SearchField;

        public UnityObjectsModel Model => m_Model;

        public bool ShowDuplicatesOnly
        {
            get => m_ShowDuplicatesOnly;
            set
            {
                m_ShowDuplicatesOnly = value;
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
                if (IsViewLoaded)
                    // FlattenHierarchy is a property setter triggered by UI input or startup config (before IsViewLoaded is true) and therefore doesn't expect an awaitable task.
                    // No caller or following code expects any part of the async build to be done.
                    // Therefore: Fire-and-forget with proper error handling.
                    FireAndForgetBuildModelAsync(false);
            }
        }

        public bool DisambiguateByInstanceID { get; private set; }

        // Filter properties - delegate to FilterManager with specific naming for UnityObjects
        public IScopedFilter<string> SearchStringFilter => m_FilterManager.SearchFilter;
        public ITextFilter UnityObjectNameFilter => m_FilterManager.NameFilter;
        public ITextFilter UnityObjectTypeNameFilter => m_FilterManager.TypeNameFilter;
        public IEntityIdFilter SourceIndexFilter => m_FilterManager.EntityIdFilter;

        void OnSearchFilterChanged(IScopedFilter<string> searchFilter)
        {
            SetFilters(searchStringFilter: searchFilter);
        }

        public void SetFilters(
            UnityObjectsModel model = null,
            IScopedFilter<string> searchStringFilter = null,
            ITextFilter unityObjectNameFilter = null,
            ITextFilter unityObjectTypeNameFilter = null,
            IEntityIdFilter unityObjectInstanceIdFilter = null)
        {
            // SetFilters is a listener to UI input (search filter changes) and therefore doesn't expect an awaitable task.
            // No caller or following code expects any part of the asnyc build to be done.
            // Therefore: Discard the returned task.
            _ = SetFiltersAsync(model, searchStringFilter, unityObjectNameFilter, unityObjectTypeNameFilter, unityObjectInstanceIdFilter);
        }

        public async Task SetFiltersAsync(
            UnityObjectsModel model = null,
            IScopedFilter<string> searchStringFilter = null,
            ITextFilter unityObjectNameFilter = null,
            ITextFilter unityObjectTypeNameFilter = null,
            IEntityIdFilter unityObjectInstanceIdFilter = null)
        {
            ClearFilters();

            // Store all filters in FilterManager
            m_FilterManager.SearchFilter = searchStringFilter;
            m_FilterManager.NameFilter = unityObjectNameFilter;
            m_FilterManager.TypeNameFilter = unityObjectTypeNameFilter;
            m_FilterManager.EntityIdFilter = unityObjectInstanceIdFilter;
            m_FilterManager.BaseModelForTreeNodeIdFiltering = model;

            // Set tree node filter for exclude-all scenario
            // To align this logic with the All Of Memory table, set an empty tree node id filter if the goal is to exclude everything
            // TODO: filtering out to show no objects is still handled via the SourceIndexFilter instead of the ExcludeAllFilterApplied that checks m_TreeNodeIdFilter
            m_FilterManager.TreeNodeIdFilter =
                // This kind of filtering is essentially a hack to keep the Base & Compare tables empty.
                (unityObjectInstanceIdFilter?.Passes(CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone) ?? false)
                ? new List<int>() : null;

            // Update base class members for compatibility
            m_TreeNodeIdFilter = m_FilterManager.TreeNodeIdFilter;
            m_BaseModelForTreeNodeIdFiltering = m_FilterManager.BaseModelForTreeNodeIdFiltering;

            await m_FilterManager.ApplyFiltersAsync(() => BuildModelAsync(false), IsViewLoaded);
        }

        protected virtual void ClearFilters()
        {
            m_FilterManager.ClearFilters();

            // Update base class members
            m_BaseModelForTreeNodeIdFiltering = default;
            m_TreeNodeIdFilter = null;
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
            base.ViewLoaded();

            m_FlattenToggle.text = "Flatten Hierarchy";
            m_FlattenToggle.RegisterValueChangedCallback(OnFlattenHierarchyToggleValueChanged);
            m_DuplicatesToggle.text = "Show Potential Duplicates Only";
            m_DuplicatesToggle.tooltip = "Show potential duplicate Unity Objects only. Potential duplicates, which are Unity Objects of the same type, name, and size, might represent the same asset loaded multiple times in memory.";
            m_DuplicatesToggle.RegisterValueChangedCallback(OnShowDuplicatesOnlyToggleValueChanged);

            if (!m_ShowAdditionalOptions)
                UIElementsHelper.SetElementDisplay(m_Toolbar, false);
        }

        void GatherReferencesInView(VisualElement view)
        {
            m_TreeView = view.Q<MultiColumnTreeView>(k_UxmlIdentifier_TreeView);
            m_Toolbar = view.Q<Toolbar>(k_UxmlIdentifier_Toolbar);
            m_FlattenToggle = view.Q<Toggle>(k_UxmlIdentifier_FlattenToggle);
            m_DuplicatesToggle = view.Q<Toggle>(k_UxmlIdentifier_DuplicatesToggle);
            m_LoadingOverlay = view.Q<ActivityIndicatorOverlay>(k_UxmlIdentifier_LoadingOverlay);
            m_ErrorLabel = view.Q<Label>(k_UxmlIdentifier_ErrorLabel);

            // Initialize components that depend on m_TreeView
            m_ColumnManager = new TreeViewColumnManager<UnityObjectsModel.ItemData>(m_TreeView);
            m_SizeCellRenderer = new SizeCellRenderer(m_TreeView, () => TableMode, k_Graphics_NotAvailable, k_UssClass_Cell_Unreliable);
        }

        protected override void ConfigureTreeView()
        {
            base.ConfigureTreeView();

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Description,
                "Description",
                0,
                BindCellForDescriptionColumn(),
                UnityObjectsDescriptionCell.Instantiate);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__Size,
                "Allocated Size",
                120,
                m_SizeCellRenderer.CreateMemorySizeBinding<UnityObjectsModel.ItemData>(
                    item => item.TotalSize),
                visible: TableMode != AllTrackedMemoryTableMode.OnlyResident);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__SizeBar,
                "% Impact",
                180,
                m_SizeCellRenderer.CreateMemoryBarBinding<UnityObjectsModel.ItemData>(
                    item => item.TotalSize,
                    () => TableMode != AllTrackedMemoryTableMode.OnlyResident
                        ? new MemorySize(m_Model.TotalMemorySize.Committed, m_Model.TotalMemorySize.Committed)
                        : new MemorySize(m_Model.TotalMemorySize.Resident, m_Model.TotalMemorySize.Resident)),
                m_SizeCellRenderer.MakeMemoryBarCell);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__NativeSize,
                "Native Size",
                120,
                m_SizeCellRenderer.CreateMemorySizeBinding<UnityObjectsModel.ItemData>(
                    item => item.NativeSize));

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__ManagedSize,
                "Managed Size",
                120,
                m_SizeCellRenderer.CreateMemorySizeBinding<UnityObjectsModel.ItemData>(
                    item => item.ManagedSize));

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__ResidentSize,
                "Resident Size",
                120,
                m_SizeCellRenderer.CreateMemorySizeBinding<UnityObjectsModel.ItemData>(
                    item => item.TotalSize,
                    useResident: true),
                visible: m_Snapshot.HasSystemMemoryResidentPages && TableMode != AllTrackedMemoryTableMode.OnlyCommitted);

            m_ColumnManager.ConfigureColumn(
                k_UxmlIdentifier_TreeViewColumn__GpuSize,
                "Graphics Size",
                120,
                BindCellForGpuSizeColumn());
        }

        protected UnityObjectsModelBuilder.BuildArgs GetModelBuilderArgs()
        {
            return new UnityObjectsModelBuilder.BuildArgs(
                m_FilterManager.SearchFilter,
                m_FilterManager.NameFilter,
                m_FilterManager.TypeNameFilter,
                m_FilterManager.EntityIdFilter,
                FlattenHierarchy,
                ShowDuplicatesOnly,
                DisambiguateByInstanceID,
                ProcessUnityObjectItemSelected);
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
            const string k_NoName = "<No Name>";
            return (element, rowIndex) =>
            {
                var cell = (UnityObjectsDescriptionCell)element;
                var itemData = m_TreeView.GetItemDataForIndex<UnityObjectsModel.ItemData>(rowIndex);

                var typeName = GetNativeBaseTypeName(itemData.Source);
                cell.SetTypeName(typeName);

                var displayText = itemData.Name;
                if (string.IsNullOrEmpty(displayText))
                    displayText = k_NoName;
                cell.SetText(displayText);

                var secondaryDisplayText = string.Empty;
                var childCount = itemData.ChildCount;
                if (childCount > 0)
                    secondaryDisplayText = $"({childCount:N0} Object{((childCount > 1) ? "s" : string.Empty)})";
                cell.SetSecondaryText(secondaryDisplayText);
            };
        }

        string GetNativeBaseTypeName(CachedSnapshot.SourceIndex source)
        {
            var typeIndex = source.Id switch
            {
                CachedSnapshot.SourceIndex.SourceId.NativeObject => m_Snapshot.NativeObjects.NativeTypeArrayIndex[source.Index],
                CachedSnapshot.SourceIndex.SourceId.NativeType => source.Index,
                CachedSnapshot.SourceIndex.SourceId.ManagedType => m_Snapshot.TypeDescriptions.UnifiedTypeInfoManaged[source.Index].NativeTypeIndex,
                _ => throw new ArgumentOutOfRangeException(),
            };
            return m_Snapshot.NativeTypes.TypeName[typeIndex];
        }

        Action<VisualElement, int> BindCellForGpuSizeColumn()
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<UnityObjectsModel.ItemData>(rowIndex);
                var label = element as Label;

                if (TableMode == AllTrackedMemoryTableMode.OnlyResident)
                {
                    label.text = k_Graphics_NotAvailable;
                    label.tooltip = k_Graphics_NotAvailable_ToolTip;
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
            // Invoke the selection processor for the selected item.
            m_Model.SelectionProcessor?.Invoke(itemId, itemData);
        }

        void ProcessUnityObjectItemSelected(int itemId, UnityObjectsModel.ItemData itemData)
        {
            m_Responder?.UnityObjectsTableViewControllerSelectedItem(itemId, this, itemData);
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

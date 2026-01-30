using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
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

        // View.
        Toolbar m_Toolbar;
        // Search not yet implemented
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
                    // ShowDuplicatesOnly is a listener to UI input and therefore doesn't expect an awaitable task.
                    // No caller or following code expects any part of the asnyc build to be done.
                    // Therefore: Discard the returned task.
                    _ = BuildModelAsync(false);
            }
        }

        public bool FlattenHierarchy
        {
            get => m_FlattenHierarchy;
            set
            {
                m_FlattenHierarchy = value;
                if (IsViewLoaded)
                    // FlattenHierarchy is a listener to UI input or startup config (before IsViewLoaded is true) and therefore doesn't expect an awaitable task.
                    // No caller or following code expects any part of the asnyc build to be done.
                    // Therefore: Discard the returned task.
                    _ = BuildModelAsync(false);
            }
        }

        public bool DisambiguateByInstanceID { get; private set; }

        public IScopedFilter<string> SearchStringFilter { get; private set; }
        public ITextFilter UnityObjectNameFilter { get; private set; }

        public ITextFilter UnityObjectTypeNameFilter { get; private set; }

        public IEntityIdFilter SourceIndexFilter { get; private set; }

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

            SearchStringFilter = searchStringFilter;
            UnityObjectNameFilter = unityObjectNameFilter;
            UnityObjectTypeNameFilter = unityObjectTypeNameFilter;
            SourceIndexFilter = unityObjectInstanceIdFilter;

            // to align this logic with the All Of Memory table, set an empty tree node id filter if the goal is to exclude everything
            // TODO: filtering out to show no objects is still handles via the SourceIndexFilter instead of the ExcludeAllFilterApplied that checks m_TreeNodeIdFilter
            m_TreeNodeIdFilter =
                // This kind of filtering is essentially a hack to keep the Base & Compare tables empty.
                (unityObjectInstanceIdFilter?.Passes(CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone) ?? false)
                ? new List<int>() : null;

            // TODO: do not fully rebuild if a model was supplied
            if (IsViewLoaded)
                await BuildModelAsync(false);
        }

        protected virtual void ClearFilters()
        {
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
        }

        protected override void ConfigureTreeView()
        {
            base.ConfigureTreeView();

            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__Description, "Description", BindCellForDescriptionColumn(), makeCell: UnityObjectsDescriptionCell.Instantiate);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__Size, "Allocated Size", BindCellForSizeColumn(SizeType.Total), visible: TableMode != AllTrackedMemoryTableMode.OnlyResident);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__SizeBar, "% Impact", BindCellForSizeBarColumn(), makeCell: MakeSizeBarCell);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__NativeSize, "Native Size", BindCellForSizeColumn(SizeType.Native));
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__ManagedSize, "Managed Size", BindCellForSizeColumn(SizeType.Managed));
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__ResidentSize, "Resident Size", BindCellForSizeColumn(SizeType.Resident), visible: m_Snapshot.HasSystemMemoryResidentPages && TableMode != AllTrackedMemoryTableMode.OnlyCommitted);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__GpuSize, "Graphics Size", BindCellForGpuSizeColumn());
        }

        protected UnityObjectsModelBuilder.BuildArgs GetModelBuilderArgs()
        {
            return new UnityObjectsModelBuilder.BuildArgs(
                SearchStringFilter,
                UnityObjectNameFilter,
                UnityObjectTypeNameFilter,
                SourceIndexFilter,
                FlattenHierarchy,
                ShowDuplicatesOnly,
                DisambiguateByInstanceID,
                ProcessUnityObjectItemSelected);
        }

        protected override Func<UnityObjectsModel> GetModelBuilderTask(CancellationToken cancellationToken)
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
                    var modelBuilder = new UnityObjectsModelBuilder();
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
                    var modelBuilder = new UnityObjectsModelBuilder();
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
            m_Responder?.UnityObjectsTableViewControllerReloaded(this, success);
            // Update usage counters
            MemoryProfilerAnalytics.AddUnityObjectsUsage(SearchStringFilter != null, FlattenHierarchy, ShowDuplicatesOnly, TableMode);
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

        Action<VisualElement, int> BindCellForSizeBarColumn()
        {
            return (element, rowIndex) =>
            {
                var maxValue = TableMode != AllTrackedMemoryTableMode.OnlyResident ?
                    m_Model.TotalMemorySize.Committed : m_Model.TotalMemorySize.Resident;

                var cell = element as MemoryBar;
                var item = m_TreeView.GetItemDataForIndex<UnityObjectsModel.ItemData>(rowIndex);
                cell.Set(item.TotalSize, maxValue, maxValue);
            };
        }

        Action<VisualElement, int> BindCellForSizeColumn(SizeType sizeType)
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<UnityObjectsModel.ItemData>(rowIndex);
                var size = 0UL;
                size = sizeType switch
                {
                    SizeType.Total => itemData.TotalSize.Committed,
                    SizeType.Native => itemData.NativeSize.Committed,
                    SizeType.Managed => itemData.ManagedSize.Committed,
                    SizeType.Gpu => itemData.GpuSize.Committed,
                    SizeType.Resident => itemData.TotalSize.Resident,
                    _ => throw new ArgumentException("Unknown size type."),
                };
                ((Label)element).text = EditorUtility.FormatBytes((long)size);
                ((Label)element).tooltip = $"{size:N0} B";
                ((Label)element).displayTooltipWhenElided = false;
            };
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


        VisualElement MakeSizeBarCell()
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

        enum SizeType
        {
            Total,
            Native,
            Managed,
            Gpu,
            Resident
        }
    }
}

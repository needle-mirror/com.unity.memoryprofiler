using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
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
        }

        protected override void ConfigureTreeView()
        {
            base.ConfigureTreeView();

            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__Address, "Address", BindCellForAddressColumn(), width: 180, makeCell: MakeCellForAddressColumn);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__Size, "Allocated Size", BindCellForSizeColumn(), width: 100, makeCell: MakeCellForSizeColumn, visible: TableMode != AllTrackedMemoryTableMode.OnlyResident);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__ResidentSize, "Resident Size", BindCellForResidentSizeColumn(), width: 100, visible: m_Snapshot.HasSystemMemoryResidentPages && TableMode != AllTrackedMemoryTableMode.OnlyCommitted);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__Type, "Type", BindCellForTypeColumn(), width: 180);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__Description, "Name", BindCellForNameColumn());
        }

        protected MemoryMapBreakdownModelBuilder.BuildArgs GetModelBuilderArgs()
        {
            var nameFilter = m_SearchField.value;
            return new MemoryMapBreakdownModelBuilder.BuildArgs(nameFilter);
        }

        protected override Func<MemoryMapBreakdownModel> GetModelBuilderTask(CancellationToken cancellationToken)
        {
            // Capture all variables locally in case they are changed before the task is started
            var modelTypeName = typeof(MemoryMapBreakdownModel).ToString();
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
                    var modelBuilder = new MemoryMapBreakdownModelBuilder();
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
                    var modelBuilder = new MemoryMapBreakdownModelBuilder();
                    var model = modelBuilder.Build(snapshot, args);

                    cancellationToken.ThrowIfCancellationRequested();

                    AsyncTaskHelper.DebugLogAsyncStep("Building Finished                 " + modelTypeName);
                    return model;
                };
        }

        protected override void OnViewReloaded(bool success)
        {
            base.OnViewReloaded(success);

            // Update usage counters
            MemoryProfilerAnalytics.AddMemoryMapUsage(m_SearchField.value != null);
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

        Action<VisualElement, int> BindCellForSizeColumn()
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<MemoryMapBreakdownModel.ItemData>(rowIndex);
                ((Label)element).text = EditorUtility.FormatBytes((long)itemData.TotalSize.Committed);
            };
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

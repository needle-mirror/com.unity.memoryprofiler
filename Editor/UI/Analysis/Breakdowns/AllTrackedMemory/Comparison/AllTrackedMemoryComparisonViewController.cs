using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.Extensions;
using Unity.MemoryProfiler.Editor.Format;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Displays comparison between two All Tracked Memory trees.
    class AllTrackedMemoryComparisonViewController :
        ComparisonViewController<AllTrackedMemoryModel, AllTrackedMemoryModelBuilder.BuildArgs, AllTrackedMemoryModel.ItemData, AllTrackedComparisonTableModel.ExtraData>,
        AllTrackedMemoryTableViewController.IResponder, IAnalysisViewSelectable, IViewControllerWithVisibilityEvents
    {
        protected override ulong UnavailableStatusesForTable => ObjectStatusExtensions.AllOfMemoryTableUnavailableStatuses;
        public override BreakdownTableId TableId => BreakdownTableId.AllTrackedMemoryComparison;
        public override ITableItemLocator BaseTable => m_BaseViewController;
        public override ITableItemLocator ComparedTable => m_ComparedViewController;
        public override bool CanEverContain(CachedSnapshot.SourceIndex source) => AllTrackedMemoryTableViewController.CanEverContainSourceId(source.Id);
        readonly ISelectionDetails m_SelectionDetails;

        // View
        int? m_SelectAfterLoadItemId;
        bool m_SameSessionDiff = false;
        bool m_WaitingForFilteringToBeAppliedToBaseView = false;
        bool m_WaitingForFilteringToBeAppliedToCompareView = false;

        // Children.
        AllTrackedMemoryTableViewController m_BaseViewController;

        /// <summary>Internal for testing purposes.</summary>
        internal AllTrackedMemoryTableViewController BaseTableViewControllerForTesting => m_BaseViewController;

        /// <summary>Internal for testing purposes.</summary>
        internal AllTrackedMemoryTableViewController ComparedTableViewControllerForTesting => m_ComparedViewController;
        AllTrackedMemoryTableViewController m_ComparedViewController;

        public AllTrackedMemoryComparisonViewController(CachedSnapshot snapshotA, CachedSnapshot snapshotB, string description, ISelectionDetails selectionDetails)
            : base(snapshotA, snapshotB, description, AllTrackedMemoryComparisonTableModelBuilder.Build)
        {
            m_SelectionDetails = selectionDetails;
            m_SelectAfterLoadItemId = null;
        }

        public bool TrySelectCategory(IAnalysisViewSelectable.Category category)
        {
            int itemId = (int)category;

            // If tree view isn't loaded & populated yet, we have to delay
            // selection until async process is finished
            if (!TrySelectAndExpandTreeViewItem(itemId))
                m_SelectAfterLoadItemId = itemId;

            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_SelectionDetails?.ClearSelection();
            }

            base.Dispose(disposing);
        }

        protected override Func<VisualElement> MakeDescriptionCell()
        {
            return () => MakeLabelWithSelectInTableMenu(hideTypeInfoForObjects: true);
        }

        protected override Action<VisualElement, int> BindCellForDescriptionColumn()
        {
            return (element, rowIndex) =>
            {
                var label = element.Q<ObjectOrTypeLabel>();
                var itemData = m_TreeView.GetItemDataForIndex<AllTrackedComparisonTableModel.ComparisonData>(rowIndex);

                var sourceInA = itemData.SourceIndex.InA;
                var sourceInB = itemData.SourceIndex.InB;

                if (!label.TrySetLabelDataFromSourceIndexForTableEntry(SnapshotA, sourceInA, itemData.Name, out var displayName, splitUnifiedObjectIntoHalves: true) &&
                    !label.TrySetLabelDataFromSourceIndex(SnapshotB, sourceInB, displayName, splitUnifiedObjectIntoHalves: true))
                {
                    // Category/group node with no typed source — show name as plain text.
                    label.SetLabelData(SnapshotA, displayName, string.Empty);
                    return;
                }

                ReconcileUnifiedShapeAcrossSnapshots(label, sourceInA, sourceInB);
            };
        }

        /// <summary>
        /// Rows are matched by name across A and B; outside of a same-session comparison that match is not
        /// guaranteed to be the same object instance, so no further reconciliation is attempted there.
        /// In a same-session comparison, <see cref="InitializeChildViewControllers"/> enables
        /// <c>disambiguateUnityObjects</c> on both child tables, and
        /// <see cref="ComparisonViewController{TBaseModel, TBaseModelBuilderArgs, TBaseModelItemData, TModelSpecificData}.GetModelBuilderTask(System.Threading.CancellationToken)"/>
        /// passes true for the sameSessionComparison arg to
        /// <see cref="ComparisonViewController{TBaseModel, TBaseModelBuilderArgs, TBaseModelItemData, TModelSpecificData}.GetBaseModelBuilderArgs(CompoundSearchFilter, bool)"/>
        /// so that a Managed/Native Unity Object row's name embeds its native InstanceId (<see cref="NativeObjectTools.ProduceNativeObjectNameAndId"/>).
        /// Therefore matching such a row by name IS matching it by instance, and that instance can gain or lose its
        /// Managed Shell between A and B. The label above was only ever classified from whichever snapshot won
        /// the "prefer A, else B" resolution, so such a change could silently revert the icon to the plain
        /// (non-Unified) variant. Fold in the other snapshot's shape so the half/Unified icon still shows
        /// whenever the object was a Full Unity Object in either snapshot.
        /// </summary>
        void ReconcileUnifiedShapeAcrossSnapshots(ObjectOrTypeLabel label, CachedSnapshot.SourceIndex sourceInA, CachedSnapshot.SourceIndex sourceInB)
        {
            if (!m_SameSessionDiff || !IsManagedOrNativeObjectRow(sourceInA.Id) || !IsManagedOrNativeObjectRow(sourceInB.Id))
                return;

            var infoA = new UnifiedUnityObjectInfo(SnapshotA, ObjectData.FromSourceLink(SnapshotA, sourceInA));
            var infoB = new UnifiedUnityObjectInfo(SnapshotB, ObjectData.FromSourceLink(SnapshotB, sourceInB));
            if (!infoA.Type.IsUnityObjectType || !infoB.Type.IsUnityObjectType)
                return;

            var leakInA = infoA.IsLeakedDynamicAsset || infoA.IsLeakedShell;
            var leakInB = infoB.IsLeakedDynamicAsset || infoB.IsLeakedShell;

            // Prefer displaying an icon showing a leak status over one that is not
            // THEN prefer showing a unified icon when no leak is present.
            if (leakInA == leakInB)
            {
                // The leak Status didn't change
                if (infoA.IsFullUnityObject || !infoB.IsFullUnityObject)
                    // The icon is already for a full Unity Object or isn't a
                    // full Unity Object in B either, nothing to change
                    return;
            }
            else
            {
                // prefer the leak status that is already used, or use the one from B
                if (leakInA)
                    return;
            }

            var dataTypeB = ObjectOrTypeLabel.GetDataTypeFromStatus(SnapshotB, in infoB, sourceInB);
            label.Type = ObjectOrTypeLabel.ToHalfVariant(
                dataTypeB is ObjectOrTypeLabel.DataType.LeakedDynamicAsset ? dataTypeB : ObjectOrTypeLabel.DataType.UnifiedUnityObject,
                sourceInA.Id);
        }

        /// <summary>
        /// Cheap pre-filter only: true for any managed or native object, regardless of whether its type
        /// derives from UnityEngine.Object. Callers must still check Type.IsUnityObjectType.
        /// </summary>
        static bool IsManagedOrNativeObjectRow(CachedSnapshot.SourceIndex.SourceId id) =>
            id is CachedSnapshot.SourceIndex.SourceId.ManagedObject or CachedSnapshot.SourceIndex.SourceId.NativeObject;

        protected override Action<VisualElement> DestroyDescriptionCell()
        {
            return element =>
            {
                var label = element.Q<ObjectOrTypeLabel>();
                label.Dispose();
            };
        }

        protected override void InitializeChildViewControllers(bool sameSession)
        {
            m_SameSessionDiff = sameSession;

            // Configure 'Base (A)' table. SetColumnsVisibilityAsync and SetFiltersAsync are
            // synchronous before the view is loaded — they only store state. Assert completion.
            m_BaseViewController = new AllTrackedMemoryTableViewController(
                SnapshotA,
                compareMode: true,
                disambiguateUnityObjects: sameSession,
                responder: this)
            { TabController = TabController };
            // SetColumnsVisibilityAsync and SetFiltersAsync are synchronous before the view is loaded — they only store state. Assert completion.
            m_BaseViewController.SetColumnsVisibilityAsync(AllTrackedMemoryTableMode.OnlyCommitted).AssertCompleted();
            m_BaseViewController.SetFiltersAsync(m_Model?.BaseModel, treeNodeIdFilter: TreeViewControllerHelper.ShowNoItemsAtAllFilter).AssertCompleted();
            AddEmptyChildTableView(m_BaseViewController, BaseViewContainer);

            // Configure 'Compared (B)' table.
            m_ComparedViewController = new AllTrackedMemoryTableViewController(
                SnapshotB,
                compareMode: true,
                disambiguateUnityObjects: sameSession,
                responder: this)
            { TabController = TabController };
            m_ComparedViewController.SetColumnsVisibilityAsync(AllTrackedMemoryTableMode.OnlyCommitted).AssertCompleted();
            m_ComparedViewController.SetFiltersAsync(m_Model?.ComparedModel, treeNodeIdFilter: TreeViewControllerHelper.ShowNoItemsAtAllFilter).AssertCompleted();
            AddEmptyChildTableView(m_ComparedViewController, ComparedViewContainer);
        }

        protected override void RefreshView(bool restoreSelection = true)
        {
            base.RefreshView(restoreSelection);

            // Resolve delayed selection
            if (m_SelectAfterLoadItemId.HasValue)
            {
                // At this point we expect that it can't fail
                TrySelectAndExpandTreeViewItem(m_SelectAfterLoadItemId.Value);
                m_SelectAfterLoadItemId = null;
            }
        }

        public override void ClearSelection()
        {
            base.ClearSelection();
            m_BaseViewController.ClearSelection();
            m_ComparedViewController.ClearSelection();
            m_BaseViewController.SetFilters(m_Model?.BaseModel, treeNodeIdFilter: TreeViewControllerHelper.ShowNoItemsAtAllFilter);
            m_ComparedViewController.SetFilters(m_Model?.ComparedModel, treeNodeIdFilter: TreeViewControllerHelper.ShowNoItemsAtAllFilter);
        }

        protected override void OnTreeItemSelected(int itemId, AllTrackedComparisonTableModel.ComparisonData itemData)
        {
            m_BaseViewController.ClearSelection();
            m_ComparedViewController.ClearSelection();

            var selectedItems = m_TreeView.GetSelectedItems<AllTrackedComparisonTableModel.ComparisonData>();

            m_WaitingForFilteringToBeAppliedToBaseView = true;
            m_WaitingForFilteringToBeAppliedToCompareView = true;

            // When disambiguating native objects by Instance ID, allow selecting the Type, Name and instance ID
            // We don't want to throw the entire bathtub at the Base and Compared tables when a group node is selected in the comparison table,
            // (though performance issues might no longer be a concern with TreeNodeId filtering, the UX value is questionable)
            // but we do want to allow drilling down into the type/name/instanceID nodes for native objects (including their Graphics resources.
            // We also want to allow drilling into Managed types on a per type level.
            // Graphics has 1 less hierarchy layer before it goes to list out Types of Graphics assets compared to the Managed or Native tree.
            // And here we allow showing the Base and Compared table for leaf nodes and those deep enough.
            var isInGraphicsPath = itemData.ItemPath.Count > 0 && itemData.ItemPath[0] == AllTrackedMemoryModelBuilder.GraphicsGroupName;
            var pathThreshhold = isInGraphicsPath ? 2 : 3;

            var filter = !selectedItems.First().hasChildren || (itemData.ItemPath.Count >= pathThreshhold);

            if (filter)
            {
                var treeViewIdsInA = new List<int>();
                var treeViewIdsInB = new List<int>();

                GetTreeViewIdsOfLeafNodesRecursively(ref treeViewIdsInA, ref treeViewIdsInB, selectedItems);
                // Filter the base/compared tables based on the treeview ids in the selected comparison table.
                m_BaseViewController.SetFilters(m_Model?.BaseModel, treeNodeIdFilter: treeViewIdsInA);
                m_ComparedViewController.SetFilters(m_Model?.ComparedModel, treeNodeIdFilter: treeViewIdsInB);
            }
            else
            {
                // Show an empty table if a non-leaf (group) node is selected in the comparison table.
                m_BaseViewController.SetFilters(m_Model?.BaseModel, treeNodeIdFilter: TreeViewControllerHelper.ShowNoItemsAtAllFilter);
                m_ComparedViewController.SetFilters(m_Model?.ComparedModel, treeNodeIdFilter: TreeViewControllerHelper.ShowNoItemsAtAllFilter);
            }

            // Use ComparisonSourceIndex to create a typed details view, preferring InA.
            var sourceInA = itemData.SourceIndex.InA;
            var sourceInB = itemData.SourceIndex.InB;
            ViewController detailsView;
            if (sourceInA.Id != CachedSnapshot.SourceIndex.SourceId.None)
                detailsView = BreakdownDetailsViewControllerFactory.Create(SnapshotA, itemId, itemData.Name, itemData.ChildCount, sourceInA, BreakdownTableId.AllTrackedMemoryComparison, this, TabController);
            else if (sourceInB.Id != CachedSnapshot.SourceIndex.SourceId.None)
                detailsView = BreakdownDetailsViewControllerFactory.Create(SnapshotB, itemId, itemData.Name, itemData.ChildCount, sourceInB, BreakdownTableId.AllTrackedMemoryComparison, this, TabController);
            else
            {
                var snapshot = itemData.CountInA > 0 ? SnapshotA : SnapshotB;
                detailsView = BreakdownDetailsViewControllerFactory.Create(snapshot, itemId, itemData.Name, 0, new CachedSnapshot.SourceIndex(), BreakdownTableId.AllTrackedMemoryComparison, this, TabController);
            }
            m_SelectionDetails.SetSelection(detailsView);
        }


        bool TrySelectAndExpandTreeViewItem(int itemId)
        {
            if ((TreeView == null) || (TreeView.viewController.GetIndexForId(itemId) == -1))
                return false;

            TreeView.SetSelectionById(itemId);
            TreeView.ExpandItem(itemId);
            TreeView.Focus();
            TreeView.schedule.Execute(() => TreeView.ScrollToItemById(itemId));

            return true;
        }

        void AllTrackedMemoryTableViewController.IResponder.Reloaded(
            AllTrackedMemoryTableViewController viewController,
            bool success)
        {
            var isBaseView = viewController == m_BaseViewController;
            var descriptionLabel = (isBaseView) ? BaseDescriptionLabel : ComparedDescriptionLabel;
            var model = viewController.Model;
            var itemCount = model.RootNodes.Count;
            descriptionLabel.text = $"{itemCount:N0} item{(itemCount != 1 ? "s" : string.Empty)} | Size: {EditorUtility.FormatBytes(Convert.ToInt64(model.TotalMemorySize.Committed))}";

            var selectFirstItem = false;
            if (isBaseView && m_WaitingForFilteringToBeAppliedToBaseView)
            {
                // select the first item if the Responder was just waiting for Base View to reload
                selectFirstItem = !m_WaitingForFilteringToBeAppliedToCompareView;
                m_WaitingForFilteringToBeAppliedToBaseView = false;
            }
            else if (!isBaseView && m_WaitingForFilteringToBeAppliedToCompareView)
            {
                // select the first item if the Responder was just waiting for Compare View to reload
                selectFirstItem = !m_WaitingForFilteringToBeAppliedToBaseView;
                m_WaitingForFilteringToBeAppliedToCompareView = false;
            }
            if (selectFirstItem && !TrySelectPendingSubTableItem())
            {
                if (!m_BaseViewController.SelectFirstItem())
                    m_ComparedViewController.SelectFirstItem();
            }
        }

        void AllTrackedMemoryTableViewController.IResponder.SelectedItem(
            int itemId,
            AllTrackedMemoryTableViewController viewController,
            AllTrackedMemoryModel.ItemData itemData)
        {
            var isBaseView = viewController == m_BaseViewController;
            var snapshot = isBaseView ? SnapshotA : SnapshotB;

            ViewController detailsView = BreakdownDetailsViewControllerFactory.Create(snapshot, itemId, itemData.Name, itemData.ChildCount, itemData.Source, BreakdownTableId.AllTrackedMemoryComparison, viewController, TabController);
            m_SelectionDetails.SetSelection(detailsView);
        }

        void IViewControllerWithVisibilityEvents.ViewWillBeDisplayed()
        {
            // Silent deselection on revisiting this view.
            // The Selection Details panel should stay the same but the selection in the tables needs to be cleared
            // So that there is no confusion about what is selected, and so that there is no previously selected item
            // that won't update the Selection Details panel when an attempt to select it is made.
            ClearSelection();
        }

        void IViewControllerWithVisibilityEvents.ViewWillBeHidden()
        {
        }

        protected override void TrackUsageAnalytics(IScopedFilter<string> itemNameFilter)
        {
            MemoryProfilerAnalytics.AddAllTrackedMemoryComparisonUsage(itemNameFilter != null, MemoryProfilerSettings.ShowReservedMemoryBreakdown, IncludeUnchanged);
        }

        protected override AllTrackedMemoryModelBuilder.BuildArgs GetBaseModelBuilderArgs(CompoundSearchFilter searchFilter, bool sameSessionComparison)
        {
            return new AllTrackedMemoryModelBuilder.BuildArgs(
                searchFilter: searchFilter,
                breakdownNativeReserved: MemoryProfilerSettings.ShowReservedMemoryBreakdown,
                disambiguateUnityObjects: sameSessionComparison,
                allocationRootNamesToSplitIntoSuballocations: MemoryProfilerSettings.FeatureFlags.EnableDynamicAllocationBreakdown_2024_10 ? MemoryProfilerSettings.AllocationRootsToSplit : null);
        }

        protected override bool ShowExtraDataToggle => true;

        protected override void OnExtraDataToggleValueChanged(bool newValue) => SetStatusColumnsVisible(newValue);

        protected override void ConfigureStatusColumns()
        {
            ConfigureStatusColumns(rowIndex =>
            {
                var data = m_TreeView.GetItemDataForIndex<AllTrackedComparisonTableModel.ComparisonData>(rowIndex);
                return (data.ExtraData.WarningInfoInA, (int)data.CountInA, data.ExtraData.WarningInfoInB, (int)data.CountInB, data.ChildCount);
            });

            RegisterSortComparison(k_UxmlIdentifier_TreeViewColumn__StatusInA,
                (x, y) => x.data.ExtraData.WarningInfoInA.GetStatusSortKey()
                           .CompareTo(y.data.ExtraData.WarningInfoInA.GetStatusSortKey()));
            RegisterSortComparison(k_UxmlIdentifier_TreeViewColumn__StatusInB,
                (x, y) => x.data.ExtraData.WarningInfoInB.GetStatusSortKey()
                           .CompareTo(y.data.ExtraData.WarningInfoInB.GetStatusSortKey()));
        }
    }
}

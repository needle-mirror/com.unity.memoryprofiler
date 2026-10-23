using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.Format;
using UnityEditor;
using UnityEngine.UIElements;
using static Unity.MemoryProfiler.Editor.CachedSnapshot.SourceIndex;

namespace Unity.MemoryProfiler.Editor.UI
{
    class UnityObjectsComparisonViewController
        : ComparisonViewController<UnityObjectsModel, UnityObjectsComparisonModelBuilder.UnityObjectSpecificBuildArgs, UnityObjectsModel.ItemData, UnityObjectsComparisonModel.ExtraData>
        , UnityObjectsTableViewController.IResponder, IViewControllerWithVisibilityEvents
    {
        ISelectionDetails m_SelectionDetails;
        bool m_WaitingForFilteringToBeAppliedToBaseView = false;
        bool m_WaitingForFilteringToBeAppliedToCompareView = false;

        // Child Controllers.
        UnityObjectsTableViewController m_BaseTableViewController;

        /// <summary>Internal for testing purposes.</summary>
        internal UnityObjectsTableViewController BaseTableViewControllerForTesting => m_BaseTableViewController;

        /// <summary>Internal for testing purposes.</summary>
        internal UnityObjectsTableViewController ComparedTableViewControllerForTesting => m_ComparedTableViewController;
        UnityObjectsTableViewController m_ComparedTableViewController;

        public UnityObjectsComparisonViewController(CachedSnapshot snapshotA, CachedSnapshot snapshotB, string description, ISelectionDetails selectionDetails)
            : base(snapshotA, snapshotB, description,
                (a, b, args, compArgs) => new UnityObjectsComparisonModelBuilder().Build(a, b, args, compArgs))
        {
            m_SelectionDetails = selectionDetails;
        }

        public override BreakdownTableId TableId => BreakdownTableId.UnityObjectsComparison;
        public override ITableItemLocator BaseTable => m_BaseTableViewController;
        public override ITableItemLocator ComparedTable => m_ComparedTableViewController;
        public override bool CanEverContain(CachedSnapshot.SourceIndex source) => UnityObjectsTableViewController.CanEverContainSourceId(source.Id);

        protected override bool ShowFlattenToggle => true;
        protected override bool ShowExtraDataToggle => true;
        protected override ulong UnavailableStatusesForTable => ObjectStatusExtensions.UnityObjectsTableUnavailableStatuses;
        protected override void OnExtraDataToggleValueChanged(bool newValue) => SetStatusColumnsVisible(newValue);

        protected override Func<VisualElement> MakeDescriptionCell()
        {
            return () => MakeLabelWithSelectInTableMenu(hideTypeInfoForObjects: true);
        }

        protected override Action<VisualElement> DestroyDescriptionCell()
        {
            return element =>
            {
                var label = element.Q<ObjectOrTypeLabel>();
                label.Dispose();
            };
        }

        protected override Action<VisualElement, int> BindCellForDescriptionColumn()
        {
            return (element, rowIndex) =>
            {
                var label = element.Q<ObjectOrTypeLabel>();
                var itemData = m_TreeView.GetItemDataForIndex<UnityObjectsComparisonModel.ComparisonData>(rowIndex);

                var sourceInA = itemData.SourceIndex.InA;
                var sourceInB = itemData.SourceIndex.InB;

                if (!label.TrySetLabelDataFromSourceIndexForTableEntry(SnapshotA, sourceInA, itemData.Name, out var displayName) &&
                    !label.TrySetLabelDataFromSourceIndex(SnapshotB, sourceInB, displayName))
                {
                    label.SetLabelData(SnapshotA, displayName, string.Empty);
                }

                string secondaryDisplayText;
                var childCount = itemData.ChildCount;
                if (childCount > 0)
                {
                    secondaryDisplayText = $"({childCount:N0} group{(childCount != 1 ? "s" : string.Empty)})";
                }
                else
                {
                    if (itemData.CountInA != itemData.CountInB)
                        secondaryDisplayText = $"({itemData.CountInA:N0} → {itemData.CountInB:N0} object{(itemData.CountInB != 1 ? "s" : string.Empty)})";
                    else
                        secondaryDisplayText = $"({itemData.CountInA:N0} object{(itemData.CountInA != 1 ? "s" : string.Empty)})";
                }

                // For A-only or B-only ManagedType rows, append the assembly name to distinguish
                // same-named types that live in different assemblies across snapshots.
                if (sourceInA.Id == CachedSnapshot.SourceIndex.SourceId.ManagedType &&
                    sourceInB.Id == CachedSnapshot.SourceIndex.SourceId.None)
                {
                    AppendAssemblyName(SnapshotA, sourceInA, ref secondaryDisplayText);
                }
                else if (sourceInB.Id == CachedSnapshot.SourceIndex.SourceId.ManagedType &&
                         sourceInA.Id == CachedSnapshot.SourceIndex.SourceId.None)
                {
                    AppendAssemblyName(SnapshotB, sourceInB, ref secondaryDisplayText);
                }

                label.SetSecondaryText(secondaryDisplayText);
            };
        }

        void AppendAssemblyName(CachedSnapshot snapshot, CachedSnapshot.SourceIndex sourceIndex, ref string secondaryDisplayText)
        {
            var assembly = snapshot.TypeDescriptions.Assembly[sourceIndex.Index];
            if (!string.IsNullOrEmpty(assembly))
                secondaryDisplayText = string.IsNullOrEmpty(secondaryDisplayText)
                    ? assembly
                    : $"{secondaryDisplayText} [{assembly}]";
        }

        protected override UnityObjectsComparisonModelBuilder.UnityObjectSpecificBuildArgs GetBaseModelBuilderArgs(
            CompoundSearchFilter searchFilter, bool sameSessionComparison)
        {
            return new UnityObjectsComparisonModelBuilder.UnityObjectSpecificBuildArgs(
                searchFilter: searchFilter,
                flattenHierarchy: Flatten,
                disambiguateByInstanceId: sameSessionComparison && !Flatten);
        }

        protected override void TrackUsageAnalytics(IScopedFilter<string> itemNameFilter)
        {
            MemoryProfilerAnalytics.AddUnityObjectsComparisonUsage(itemNameFilter != null, Flatten, IncludeUnchanged);
        }

        protected override void InitializeChildViewControllers(bool sameSession)
        {
            // Configure 'Base (A)' Unity Objects table. SetColumnsVisibilityAsync and SetFiltersAsync
            // are synchronous before the view is loaded — they only store state. Assert completion.
            m_BaseTableViewController = new UnityObjectsTableViewController(
                SnapshotA,
                compareMode: true,
                disambiguateUnityObjects: sameSession,
                responder: this)
            {
                TabController = TabController,
                FlattenHierarchy = true
            };
            // SetColumnsVisibilityAsync and SetFiltersAsync are synchronous before the view is loaded — they only store state. Assert completion.
            m_BaseTableViewController.SetColumnsVisibilityAsync(AllTrackedMemoryTableMode.OnlyCommitted).AssertCompleted();
            m_BaseTableViewController.SetFiltersAsync(m_Model?.BaseModel, treeNodeIdFilter: TreeViewControllerHelper.ShowNoItemsAtAllFilter).AssertCompleted();
            AddEmptyChildTableView(m_BaseTableViewController, BaseViewContainer);

            // Configure 'Compared (B)' Unity Objects table.
            m_ComparedTableViewController = new UnityObjectsTableViewController(
                SnapshotB,
                compareMode: true,
                disambiguateUnityObjects: sameSession,
                responder: this)
            {
                TabController = TabController,
                FlattenHierarchy = true
            };
            m_ComparedTableViewController.SetColumnsVisibilityAsync(AllTrackedMemoryTableMode.OnlyCommitted).AssertCompleted();
            m_ComparedTableViewController.SetFiltersAsync(m_Model?.ComparedModel, treeNodeIdFilter: TreeViewControllerHelper.ShowNoItemsAtAllFilter).AssertCompleted();
            AddEmptyChildTableView(m_ComparedTableViewController, ComparedViewContainer);
        }

        protected override void ConfigureStatusColumns()
        {
            ConfigureStatusColumns(rowIndex =>
            {
                var data = m_TreeView.GetItemDataForIndex<UnityObjectsComparisonModel.ComparisonData>(rowIndex);
                return (data.ExtraData.WarningInfoInA, (int)data.CountInA, data.ExtraData.WarningInfoInB, (int)data.CountInB, data.ChildCount);
            });

            RegisterSortComparison(k_UxmlIdentifier_TreeViewColumn__StatusInA,
                (x, y) => x.data.ExtraData.WarningInfoInA.GetStatusSortKey()
                           .CompareTo(y.data.ExtraData.WarningInfoInA.GetStatusSortKey()));
            RegisterSortComparison(k_UxmlIdentifier_TreeViewColumn__StatusInB,
                (x, y) => x.data.ExtraData.WarningInfoInB.GetStatusSortKey()
                           .CompareTo(y.data.ExtraData.WarningInfoInB.GetStatusSortKey()));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_SelectionDetails?.ClearSelection();
            }

            base.Dispose(disposing);
        }


        public override void ClearSelection()
        {
            base.ClearSelection();
            m_BaseTableViewController.ClearSelection();
            m_ComparedTableViewController.ClearSelection();
            m_BaseTableViewController.SetFilters(m_Model?.BaseModel, treeNodeIdFilter: TreeViewControllerHelper.ShowNoItemsAtAllFilter);
            m_ComparedTableViewController.SetFilters(m_Model?.ComparedModel, treeNodeIdFilter: TreeViewControllerHelper.ShowNoItemsAtAllFilter);
        }

        protected override void OnTreeItemSelected(int itemId, UnityObjectsComparisonModel.ComparisonData itemData)
        {
            m_BaseTableViewController.ClearSelection();
            m_ComparedTableViewController.ClearSelection();

            // Use ComparisonSourceIndex to show type/object details, preferring InA.
            var sourceInA = itemData.SourceIndex.InA;
            var sourceInB = itemData.SourceIndex.InB;
            if (sourceInA.Id != CachedSnapshot.SourceIndex.SourceId.None)
            {
                var view = BreakdownDetailsViewControllerFactory.Create(SnapshotA, itemId, itemData.Name, itemData.ChildCount, sourceInA, BreakdownTableId.UnityObjectsComparison, this, TabController);
                m_SelectionDetails.SetSelection(view);
            }
            else if (sourceInB.Id != CachedSnapshot.SourceIndex.SourceId.None)
            {
                var view = BreakdownDetailsViewControllerFactory.Create(SnapshotB, itemId, itemData.Name, itemData.ChildCount, sourceInB, BreakdownTableId.UnityObjectsComparison, this, TabController);
                m_SelectionDetails.SetSelection(view);
            }
            else
            {
                m_SelectionDetails.ClearSelection();
            }

            m_WaitingForFilteringToBeAppliedToBaseView = true;
            m_WaitingForFilteringToBeAppliedToCompareView = true;

            var treeViewIdsInA = new List<int>();
            var treeViewIdsInB = new List<int>();
            var selectedItem = m_TreeView.GetSelectedItems<UnityObjectsComparisonModel.ComparisonData>();

            ComparisonViewController<UnityObjectsModel, UnityObjectsModelBuilder, UnityObjectsModel.ItemData, UnityObjectsComparisonModel.ExtraData>
                .GetTreeViewIdsOfLeafNodesRecursively(ref treeViewIdsInA, ref treeViewIdsInB, selectedItem);

            m_BaseTableViewController.SetFilters(m_Model?.BaseModel, treeNodeIdFilter: treeViewIdsInA);
            m_ComparedTableViewController.SetFilters(m_Model?.ComparedModel, treeNodeIdFilter: treeViewIdsInB);
        }

        void UnityObjectsTableViewController.IResponder.UnityObjectsTableViewControllerReloaded(
            UnityObjectsTableViewController tableViewController,
            bool success)
        {
            var isBaseTable = tableViewController == m_BaseTableViewController;
            var descriptionLabel = isBaseTable ? BaseDescriptionLabel : ComparedDescriptionLabel;

            var tableIsFilteringExplicitlyForNoObjects = tableViewController.ExcludeAllFilterApplied;
            if (!tableIsFilteringExplicitlyForNoObjects)
            {
                var model = tableViewController.Model;
                var objectCount = model.RootNodes.Count;
                descriptionLabel.text = $"{objectCount:N0} object{(objectCount != 1 ? "s" : string.Empty)} with the same name | Group size: {EditorUtility.FormatBytes(Convert.ToInt64(model.TotalMemorySize.Committed))}";
            }

            // Hide the description if the table is explicitly filtering for 'no objects'.
            UIElementsHelper.SetElementDisplay(descriptionLabel, !tableIsFilteringExplicitlyForNoObjects);

            var selectFirstItem = false;
            if (isBaseTable && m_WaitingForFilteringToBeAppliedToBaseView)
            {
                selectFirstItem = !m_WaitingForFilteringToBeAppliedToCompareView;
                m_WaitingForFilteringToBeAppliedToBaseView = false;
            }
            else if (!isBaseTable && m_WaitingForFilteringToBeAppliedToCompareView)
            {
                selectFirstItem = !m_WaitingForFilteringToBeAppliedToBaseView;
                m_WaitingForFilteringToBeAppliedToCompareView = false;
            }
            if (selectFirstItem && !TrySelectPendingSubTableItem())
            {
                if (!m_BaseTableViewController.SelectFirstItem())
                    m_ComparedTableViewController.SelectFirstItem();
            }
        }

        void UnityObjectsTableViewController.IResponder.UnityObjectsTableViewControllerSelectedItem(
            int itemId,
            UnityObjectsTableViewController viewController,
            UnityObjectsModel.ItemData itemData)
        {
            var isBaseView = viewController == m_BaseTableViewController;

            // Clear old selection
            var deselectedViewController = isBaseView ? m_ComparedTableViewController : m_BaseTableViewController;
            deselectedViewController.ClearSelection();

            // Make new selection
            var snapshot = isBaseView ? SnapshotA : SnapshotB;
            var view = BreakdownDetailsViewControllerFactory.Create(snapshot, itemId, itemData.Name, itemData.ChildCount, itemData.Source, BreakdownTableId.UnityObjectsComparison, viewController, TabController);
            m_SelectionDetails.SetSelection(view);
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
    }
}

using System;
using System.Linq;
using Unity.MemoryProfiler.Editor.Format;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Displays comparison between two All Tracked Memory trees.
    class AllTrackedMemoryComparisonViewController : ComparisonViewController, AllTrackedMemoryTableViewController.IResponder, IAnalysisViewSelectable, IViewControllerWithVisibilityEvents
    {
        readonly ISelectionDetails m_SelectionDetails;

        // View
        int? m_SelectAfterLoadItemId;
        bool m_SameSessionDiff = false;
        bool m_WaitingForFilteringToBeAppliedToBaseView = false;
        bool m_WaitingForFilteringToBeAppliedToCompareView = false;

        // Children.
        AllTrackedMemoryTableViewController m_BaseViewController;
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
                m_SelectionDetails?.ClearSelection();

            base.Dispose(disposing);
        }

        protected override void ViewLoaded()
        {
            base.ViewLoaded();

            m_SameSessionDiff = SnapshotA.MetaData.SessionGUID != MetaData.InvalidSessionGUID && SnapshotA.MetaData.SessionGUID == SnapshotB.MetaData.SessionGUID;

            // Configure 'Base (A)' table.
            m_BaseViewController = new AllTrackedMemoryTableViewController(
                SnapshotA,
                null,
                false,
                true,
                m_SameSessionDiff,
                this);
            BaseViewContainer.Add(m_BaseViewController.View);
            AddChild(m_BaseViewController);
            m_BaseViewController.SetFilters(excludeAll: true);
            m_BaseViewController.SetColumnsVisibility(AllTrackedMemoryTableMode.OnlyCommitted);
            m_BaseViewController.HeaderContextMenuPopulateEvent += GenerateEmptyContextMenu;

            // Configure 'Compared (B)' table.
            m_ComparedViewController = new AllTrackedMemoryTableViewController(
                SnapshotB,
                null,
                false,
                true,
                m_SameSessionDiff,
                this);
            ComparedViewContainer.Add(m_ComparedViewController.View);
            AddChild(m_ComparedViewController);
            m_ComparedViewController.SetFilters(excludeAll: true);
            m_ComparedViewController.SetColumnsVisibility(AllTrackedMemoryTableMode.OnlyCommitted);
            m_ComparedViewController.HeaderContextMenuPopulateEvent += GenerateEmptyContextMenu;
        }

        protected override void RefreshView()
        {
            base.RefreshView();

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
            m_BaseViewController.SetFilters(excludeAll: true);
            m_ComparedViewController.SetFilters(excludeAll: true);
        }

        protected override void OnTreeItemSelected(int itemId, ComparisonTableModel.ComparisonData itemData)
        {
            m_BaseViewController.ClearSelection();
            m_ComparedViewController.ClearSelection();

            var selectedItems = m_TreeView.GetSelectedItems<ComparisonTableModel.ComparisonData>();
            var selectedItem = selectedItems.First();

            m_WaitingForFilteringToBeAppliedToBaseView = true;
            m_WaitingForFilteringToBeAppliedToCompareView = true;

            var filterComparisonTableByPath = !selectedItem.hasChildren;
            // When disambiguating native objects by Instance ID, allow selecting the name items
            // and showing children with the same name
            var filterDownToChildren = !filterComparisonTableByPath && itemData.ItemPath.Count >= 4 &&
                (m_SameSessionDiff && !selectedItem.children.First().hasChildren);
            filterComparisonTableByPath |= filterDownToChildren;

            if (filterComparisonTableByPath)
            {
                // Filter the base/compared tables to the current comparison table selection and search text filter.
                var searchFilter = BuildTextFilterFromSearchText();
                var itemPathFilter = new ITextFilter[itemData.ItemPath.Count + (filterDownToChildren ? 1 : 0)];
                for (var i = 0; i < itemData.ItemPath.Count; i++)
                {
                    var pathComponent = itemData.ItemPath[i];
                    itemPathFilter[i] = MatchesTextFilter.Create(pathComponent);
                }
                if (filterDownToChildren)
                    itemPathFilter[itemPathFilter.Length - 1] = MatchesAllTextFilter.Create();
                m_BaseViewController.SetFilters(searchFilter: searchFilter, itemPathFilter: itemPathFilter);
                m_ComparedViewController.SetFilters(searchFilter: searchFilter, itemPathFilter: itemPathFilter);
            }
            else
            {
                // Show an empty table if a non-leaf (group) node is selected in the comparison table.
                m_BaseViewController.SetFilters(excludeAll: true);
                m_ComparedViewController.SetFilters(excludeAll: true);
            }

            var snapshot = itemData.CountInA > 0 ? SnapshotA : SnapshotB;
            var view = BreakdownDetailsViewControllerFactory.Create(snapshot, itemId, itemData.Name, 0, new CachedSnapshot.SourceIndex());
            m_SelectionDetails.SetSelection(view);
        }

        void GenerateEmptyContextMenu(ContextualMenuPopulateEvent evt)
        {
            // Compare mode doesn't allow column configuration
            evt.menu.ClearItems();
            evt.StopImmediatePropagation();
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
            if (selectFirstItem)
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

            ViewController detailsView = BreakdownDetailsViewControllerFactory.Create(snapshot, itemId, itemData.Name, itemData.ChildCount, itemData.Source);
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
    }
}

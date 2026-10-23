using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    class UnityObjectsBreakdownViewController : BreakdownViewController, UnityObjectsTableViewController.IResponder, IViewControllerWithVisibilityEvents
    {
        UnityObjectsTableViewController m_TableViewController;

        public override BreakdownTableId TableId => BreakdownTableId.UnityObjects;

        public UnityObjectsBreakdownViewController(CachedSnapshot snapshot, string description, ISelectionDetails selectionDetails)
            : base(snapshot, description, selectionDetails)
        {
        }

        protected override void ViewLoaded()
        {
            base.ViewLoaded();

            // Initialize Unity Objects table as a child view controller.
            m_TableViewController = new UnityObjectsTableViewController(Snapshot, SearchField, StatusFilterButton, responder: this) { TabController = TabController };
            AddChild(m_TableViewController);

            // SetColumnsVisibilityAsync is synchronous before the view is loaded (IsViewLoaded = false):
            // it only sets the TableMode property. Assert synchronous completion as a safety net.
            m_TableViewController.SetColumnsVisibilityAsync(TableColumnsMode).AssertCompleted();

            AddAndBuildChildTableView(m_TableViewController);
            // Setup table mode context menu and dropdown
            m_TableViewController.HeaderContextMenuPopulateEvent += GenerateTreeViewContextMenu;
            TableColumnsModeChanged += UpdateTableColumnsMode;
        }

        async void UpdateTableColumnsMode(AllTrackedMemoryTableMode mode)
        {
            try
            {
                // Update table mode view
                await m_TableViewController.SetColumnsVisibilityAsync(mode);

                // Refresh table common header. Model may be null if the mode changed before the build finished.
                var model = m_TableViewController.Model;
                if (model != null)
                    RefreshTableSizeBar(model.TotalMemorySize, model.TotalSnapshotMemorySize);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

        void UnityObjectsTableViewController.IResponder.UnityObjectsTableViewControllerReloaded(
            UnityObjectsTableViewController viewController,
            bool success)
        {
            if (!success)
                return;

            var model = viewController.Model;
            RefreshTableSizeBar(model.TotalMemorySize, model.TotalSnapshotMemorySize);
        }

        void UnityObjectsTableViewController.IResponder.UnityObjectsTableViewControllerSelectedItem(
            int itemId,
            UnityObjectsTableViewController viewController,
            UnityObjectsModel.ItemData itemData)
        {
            var view = BreakdownDetailsViewControllerFactory.Create(Snapshot, itemId, itemData.Name, itemData.ChildCount, itemData.Source, BreakdownTableId.UnityObjects, this, TabController);
            SelectionDetails.SetSelection(view);
        }

        void IViewControllerWithVisibilityEvents.ViewWillBeDisplayed()
        {
            // Silent deselection on revisiting this view.
            // The Selection Details panel should stay the same but the selection in the table needs to be cleared
            // So that there is no confusion about what is selected, and so that there is no previously selected item
            // that won't update the Selection Details panel when an attempt to select it is made.
            m_TableViewController.ClearSelection();
        }

        void IViewControllerWithVisibilityEvents.ViewWillBeHidden()
        {
        }
    }
}

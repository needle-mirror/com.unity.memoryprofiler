using Unity.MemoryProfiler.Editor.UIContentData;

namespace Unity.MemoryProfiler.Editor.UI
{
    class RootsAndImpactBreakdownViewController : BreakdownViewController, RootsAndImpactTableViewController.IResponder, IViewControllerWithVisibilityEvents
    {
        protected override string AllocatedMemoryInTableFormatString => TextContent.RootsAndImpactAllocatedMemoryInTableFormatString;
        protected override string AllocatedMemoryInSnapshotFormatString => base.AllocatedMemoryInSnapshotFormatString;
        protected override string ResidentMemoryInTableFormatString => TextContent.RootsAndImpactResidentMemoryInTableFormatString;
        protected override string ResidentMemoryInSnapshotFormatString => base.ResidentMemoryInSnapshotFormatString;

        RootsAndImpactTableViewController m_TableViewController;

        public RootsAndImpactBreakdownViewController(CachedSnapshot snapshot, string description, ISelectionDetails selectionDetails)
            : base(snapshot, description, selectionDetails)
        {
        }

        protected override void ViewLoaded()
        {
            base.ViewLoaded();

            m_TableViewController = new RootsAndImpactTableViewController(Snapshot, StatusFilterButton, searchField: SearchField, responder: this);
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
            await m_TableViewController.SetColumnsVisibilityAsync(mode);

            var model = m_TableViewController.Model;
            if (model != null)
                RefreshTableSizeBar(model.TotalImpactSize.SumUp(), model.TotalSnapshotMemorySize);
        }

        void RootsAndImpactTableViewController.IResponder.RootsAndImpactTableViewControllerReloaded(
            RootsAndImpactTableViewController viewController,
            bool success)
        {
            if (!success)
                return;

            var model = viewController.Model;
            RefreshTableSizeBar(model.TotalImpactSize.SumUp(), model.TotalSnapshotMemorySize);
        }

        void RootsAndImpactTableViewController.IResponder.RootsAndImpactTableViewControllerSelectedItem(
            int itemId,
            RootsAndImpactTableViewController viewController,
            RootsAndImpactModel.ItemData itemData)
        {
            var view = BreakdownDetailsViewControllerFactory.Create(Snapshot, itemId, itemData.Name, itemData.ChildCount, itemData.Source);
            SelectionDetails.SetSelection(view);
        }

        void IViewControllerWithVisibilityEvents.ViewWillBeDisplayed()
        {
            m_TableViewController?.ClearSelection();
        }

        void IViewControllerWithVisibilityEvents.ViewWillBeHidden()
        {
        }
    }
}

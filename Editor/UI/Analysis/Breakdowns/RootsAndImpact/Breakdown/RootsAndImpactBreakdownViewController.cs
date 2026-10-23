using Unity.MemoryProfiler.Editor.UIContentData;

namespace Unity.MemoryProfiler.Editor.UI
{
    class RootsAndImpactBreakdownViewController : BreakdownViewController, RootsAndImpactTableViewController.IResponder, IViewControllerWithVisibilityEvents, IRootsAndImpactStatusFilterTarget
    {
        protected override string AllocatedMemoryInTableFormatString => TextContent.RootsAndImpactAllocatedMemoryInTableFormatString;
        protected override string AllocatedMemoryInSnapshotFormatString => base.AllocatedMemoryInSnapshotFormatString;
        protected override string ResidentMemoryInTableFormatString => TextContent.RootsAndImpactResidentMemoryInTableFormatString;
        protected override string ResidentMemoryInSnapshotFormatString => base.ResidentMemoryInSnapshotFormatString;

        RootsAndImpactTableViewController m_TableViewController;
        /// <summary>Deep-link preset stashed when <see cref="ApplyStatusMaskPreset"/> arrives before the table exists.</summary>
        StatusMaskPreset? m_PendingStatusMaskPreset;

        public override BreakdownTableId TableId => BreakdownTableId.RootsAndImpact;

        public RootsAndImpactBreakdownViewController(CachedSnapshot snapshot, string description, ISelectionDetails selectionDetails)
            : base(snapshot, description, selectionDetails)
        {
        }

        public void ApplyStatusMaskPreset(ulong exclusiveStatusMask, bool flatten)
        {
            if (m_TableViewController == null)
                m_PendingStatusMaskPreset = new StatusMaskPreset(exclusiveStatusMask, flatten);
            else
                m_TableViewController.ApplyStatusMaskPreset(exclusiveStatusMask, flatten);
        }

        /// <summary>Test-only seam: the table controller, once <see cref="ViewLoaded"/> has created it.</summary>
        internal RootsAndImpactTableViewController TableViewControllerForTesting => m_TableViewController;

        /// <summary>Test-only seam: whether a deep-link preset is stashed, awaiting the table controller to exist.</summary>
        internal bool HasPendingStatusMaskPresetForTesting => m_PendingStatusMaskPreset.HasValue;

        protected override void ViewLoaded()
        {
            base.ViewLoaded();

            m_TableViewController = new RootsAndImpactTableViewController(Snapshot, StatusFilterButton, searchField: SearchField, responder: this) { TabController = TabController };

            // Before AddChild, so the table's initial build uses the preset instead of racing a second build.
            if (m_PendingStatusMaskPreset.HasValue)
            {
                var preset = m_PendingStatusMaskPreset.Value;
                m_PendingStatusMaskPreset = null;
                m_TableViewController.ApplyStatusMaskPreset(preset.ExclusiveStatusMask, preset.Flatten);
            }

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
            {
                RefreshTableSizeBar(model.TotalImpactSize.SumUp(), model.TotalSnapshotMemorySize);
            }
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
            var view = BreakdownDetailsViewControllerFactory.Create(Snapshot, itemId, itemData.Name, itemData.ChildCount, itemData.Source, BreakdownTableId.RootsAndImpact, this, TabController);
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

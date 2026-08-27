using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    class AllTrackedMemoryBreakdownViewController : BreakdownViewController,
        AllTrackedMemoryTableViewController.IResponder, IAnalysisViewSelectable, IViewControllerWithVisibilityEvents
    {
        AllTrackedMemoryTableViewController m_TableViewController;

        public AllTrackedMemoryBreakdownViewController(CachedSnapshot snapshot, string description, ISelectionDetails selectionDetails)
            : base(snapshot, description, selectionDetails)
        {
        }

        protected override void ViewLoaded()
        {
            base.ViewLoaded();

            // Initialize All Tracked Memory table as a child view controller.
            m_TableViewController = new AllTrackedMemoryTableViewController(Snapshot, SearchField, StatusFilterButton, responder: this);
            AddChild(m_TableViewController);

            // SetColumnsVisibilityAsync is synchronous before the view is loaded (IsViewLoaded = false):
            // it only sets the TableMode property. Assert synchronous completion as a safety net.
            m_TableViewController.SetColumnsVisibilityAsync(TableColumnsMode).AssertCompleted();

            AddAndBuildChildTableView(m_TableViewController);
            // Setup table mode context menu and dropdown
            m_TableViewController.HeaderContextMenuPopulateEvent += GenerateTreeViewContextMenu;
            TableColumnsModeChanged += UpdateTableColumnsMode;
        }

        public bool TrySelectCategory(IAnalysisViewSelectable.Category category)
        {
            return m_TableViewController.TrySelectCategory(category);
        }

        async void UpdateTableColumnsMode(AllTrackedMemoryTableMode mode)
        {
            try
            {
                // Update table mode view
                await m_TableViewController.SetColumnsVisibilityAsync(mode);

                // Refresh table common header
                var model = m_TableViewController.Model;
                if (model != null)
                    RefreshTableSizeBar(model.TotalMemorySize, model.TotalGraphicsMemorySize, model.TotalSnapshotMemorySize);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

        void AllTrackedMemoryTableViewController.IResponder.Reloaded(
            AllTrackedMemoryTableViewController viewController,
            bool success)
        {
            if (!success)
                return;

            var model = viewController.Model;
            RefreshTableSizeBar(model.TotalMemorySize, model.TotalGraphicsMemorySize, model.TotalSnapshotMemorySize);
        }

        void AllTrackedMemoryTableViewController.IResponder.SelectedItem(
            int itemId,
            AllTrackedMemoryTableViewController viewController,
            AllTrackedMemoryModel.ItemData itemData)
        {
            ViewController detailsView = BreakdownDetailsViewControllerFactory.Create(Snapshot, itemId, itemData.Name, itemData.ChildCount, itemData.Source);
            SelectionDetails.SetSelection(detailsView);
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

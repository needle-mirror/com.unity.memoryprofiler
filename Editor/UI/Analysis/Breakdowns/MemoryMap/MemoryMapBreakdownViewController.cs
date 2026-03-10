using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class MemoryMapBreakdownViewController : BreakdownViewController,
        MemoryMapTableViewController.IResponder, IViewControllerWithVisibilityEvents
    {
        MemoryMapTableViewController m_TableViewController;

        public MemoryMapBreakdownViewController(CachedSnapshot snapshot, string description, ISelectionDetails selectionDetails)
            : base(snapshot, description, selectionDetails)
        {
        }

        protected override void ViewLoaded()
        {
            base.ViewLoaded();

            // Initialize Memory Map table as a child view controller.
            m_TableViewController = new MemoryMapTableViewController(Snapshot, SearchField, responder: this);
            AddChild(m_TableViewController);

            TableContainer.Add(m_TableViewController.View);

            // Memory Map always shows both Committed and Resident columns
            TableColumnsMode = AllTrackedMemoryTableMode.CommittedAndResident;
            // Hide table mode dropdown since Memory Map always shows both columns
            var dropdownPart = TableColumnsDropdown?.Q(className: "unity-popup-field__input");
            if (dropdownPart != null)
            {
                UIElementsHelper.SetVisibility(dropdownPart, false);
            }
        }

        void MemoryMapTableViewController.IResponder.MemoryMapTableViewControllerReloaded(
            MemoryMapTableViewController viewController,
            bool success)
        {
            if (!success)
                return;

            var model = viewController.Model;
            RefreshTableSizeBar(model.TotalMemorySize, model.TotalSnapshotMemorySize);
        }

        void MemoryMapTableViewController.IResponder.MemoryMapTableViewControllerSelectedItem(
            int itemId,
            MemoryMapTableViewController viewController,
            MemoryMapBreakdownModel.ItemData itemData)
        {
            ViewController detailsView = BreakdownDetailsViewControllerFactory.Create(Snapshot, itemId, itemData.Name, 0, itemData.Source);
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

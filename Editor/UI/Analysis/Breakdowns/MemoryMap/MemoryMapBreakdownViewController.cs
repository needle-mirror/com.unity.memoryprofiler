#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class MemoryMapBreakdownViewController : TreeViewController<MemoryMapBreakdownModel, MemoryMapBreakdownModel.ItemData>, IViewControllerWithVisibilityEvents
    {
        const string k_UxmlAssetGuid = "bc16108acf3b6484aa65bf05d6048e8f";

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
        const string k_ErrorMessage = "Snapshot is from an outdated Unity version that is not fully supported.";

        // Model.
        readonly CachedSnapshot m_Snapshot;

        // State
        ISelectionDetails m_SelectionDetails;

        // View.
        Label m_TableDescription;
        ToolbarSearchField m_SearchField;
        DetailedSizeBar m_TableSizeBar;
        ActivityIndicatorOverlay m_LoadingOverlay;
        Label m_ErrorLabel;

        public MemoryMapBreakdownViewController(CachedSnapshot snapshot, ISelectionDetails selectionDetails)
            : base (idOfDefaultColumnWithPercentageBasedWidth: null)
        {
            m_Snapshot = snapshot;
            m_SelectionDetails = selectionDetails;
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
            ConfigureTreeView();

            // These styles are not supported in Unity 2020 and earlier. They will cause project errors if included in the stylesheet in those Editor versions.
            // Remove when we drop support for <= 2020 and uncomment these styles in the stylesheet.
            var transitionDuration = new StyleList<TimeValue>(new List<TimeValue>() { new TimeValue(0.23f) });
            var transitionTimingFunction = new StyleList<EasingFunction>(new List<EasingFunction>() { new EasingFunction(EasingMode.EaseOut) });
            m_LoadingOverlay.style.transitionDuration = transitionDuration;
            m_LoadingOverlay.style.transitionProperty = new StyleList<StylePropertyName>(new List<StylePropertyName>() { new StylePropertyName("opacity") });
            m_LoadingOverlay.style.transitionTimingFunction = transitionTimingFunction;

            BuildModelAsync();
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
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__Size, "Allocated Size", BindCellForSizeColumn(), width: 100, makeCell: MakeCellForSizeColumn);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__ResidentSize, "Resident Size", BindCellForResidentSizeColumn(), width: 100, visible: m_Snapshot.HasSystemMemoryResidentPages);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__Type, "Type", BindCellForTypeColumn(), width: 180);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__Description, "Name", BindCellForNameColumn());
        }

        protected override void BuildModelAsync()
        {
            // Cancel existing build if necessary.
            m_BuildModelWorker?.Dispose();

            // Show loading UI.
            m_LoadingOverlay.Show();

            // Dispatch asynchronous build.
            var snapshot = m_Snapshot;
            var nameFilter = m_SearchField.value;
            var args = new MemoryMapBreakdownModelBuilder.BuildArgs(nameFilter);
            var sortDescriptors = BuildSortDescriptorsFromTreeView();
            m_BuildModelWorker = new AsyncWorker<MemoryMapBreakdownModel>();
            m_BuildModelWorker.Execute(() =>
            {
                try
                {
                    // Build the data model.
                    var modelBuilder = new MemoryMapBreakdownModelBuilder();
                    var model = modelBuilder.Build(snapshot, args);

                    // Sort it according to the current sort descriptors.
                    model.Sort(sortDescriptors);

                    return model;
                }
                catch (UnsupportedSnapshotVersionException)
                {
                    return null;
                }
                catch (System.Threading.ThreadAbortException)
                {
                    // We expect a ThreadAbortException to be thrown when cancelling an in-progress builder. Do not log an error to the console.
                    return null;
                }
                catch (Exception ex)
                {
                    Debug.LogError(ex);
                    throw;
                }
            }, (model) =>
            {
                // Update model.
                m_Model = model;

                if (model != null)
                {
                    // Refresh UI with new data model.
                    RefreshView();
                }
                else
                {
                    // Display error message.
                    m_ErrorLabel.text = k_ErrorMessage;
                    UIElementsHelper.SetElementDisplay(m_ErrorLabel, true);
                }

                // Hide loading UI.
                m_LoadingOverlay.Hide();

                // Update usage counters
                MemoryProfilerAnalytics.AddMemoryMapUsage(nameFilter != null);
            });
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
                ((Label)element).text = EditorUtility.FormatBytes((long)itemData.Size.Committed);
            };
        }

        Action<VisualElement, int> BindCellForResidentSizeColumn()
        {
            return (element, rowIndex) =>
            {
                if (m_TreeView.GetParentIdForIndex(rowIndex) == -1)
                {
                    var itemData = m_TreeView.GetItemDataForIndex<MemoryMapBreakdownModel.ItemData>(rowIndex);
                    ((Label)element).text = EditorUtility.FormatBytes((long)itemData.Size.Resident);
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

        IEnumerable<MemoryMapBreakdownModel.SortDescriptor> BuildSortDescriptorsFromTreeView()
        {
            var sortDescriptors = new List<MemoryMapBreakdownModel.SortDescriptor>();

            var sortedColumns = m_TreeView.sortedColumns;
            using (var enumerator = sortedColumns.GetEnumerator())
            {
                if (enumerator.MoveNext())
                {
                    var sortDescription = enumerator.Current;
                    var sortProperty = ColumnNameToSortbleItemDataProperty(sortDescription.columnName);
                    var sortDirection = (sortDescription.direction == SortDirection.Ascending) ?
                        MemoryMapBreakdownModel.SortDirection.Ascending : MemoryMapBreakdownModel.SortDirection.Descending;
                    var sortDescriptor = new MemoryMapBreakdownModel.SortDescriptor(sortProperty, sortDirection);
                    sortDescriptors.Add(sortDescriptor);
                }
            }

            return sortDescriptors;
        }

        MemoryMapBreakdownModel.SortableItemDataProperty ColumnNameToSortbleItemDataProperty(string columnName)
        {
            switch (columnName)
            {
                case k_UxmlIdentifier_TreeViewColumn__Address:
                    return MemoryMapBreakdownModel.SortableItemDataProperty.Address;

                case k_UxmlIdentifier_TreeViewColumn__Size:
                    return MemoryMapBreakdownModel.SortableItemDataProperty.Size;

                case k_UxmlIdentifier_TreeViewColumn__ResidentSize:
                    return MemoryMapBreakdownModel.SortableItemDataProperty.ResidentSize;

                case k_UxmlIdentifier_TreeViewColumn__Description:
                    return MemoryMapBreakdownModel.SortableItemDataProperty.Name;

                case k_UxmlIdentifier_TreeViewColumn__Type:
                    return MemoryMapBreakdownModel.SortableItemDataProperty.Type;

                default:
                    throw new ArgumentException("Unable to sort. Unknown column name.");
            }
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
#endif

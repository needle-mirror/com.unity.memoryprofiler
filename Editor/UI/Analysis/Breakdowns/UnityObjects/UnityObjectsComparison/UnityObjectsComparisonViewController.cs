#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.Format;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class UnityObjectsComparisonViewController : TreeViewController<UnityObjectsComparisonModel, UnityObjectsComparisonModel.ItemData>, UnityObjectsTableViewController.IResponder, IViewControllerWithVisibilityEvents
    {
        const string k_UxmlAssetGuid = "22b678e6f811eec4782c655ff73c2677";
        const string k_UssClass_Dark = "unity-objects-comparison-view__dark";
        const string k_UssClass_Light = "unity-objects-comparison-view__light";
        const string k_UxmlIdentifier_DescriptionLabel = "unity-objects-comparison-view__description-label";
        const string k_UxmlIdentifier_SearchField = "unity-objects-comparison-view__search-field";
        const string k_UxmlIdentifier_SplitView = "unity-objects-comparison-view__split-view";
        const string k_UxmlIdentifier_BaseTotalSizeBar = "unity-objects-comparison-view__base-total-size-bar";
        const string k_UxmlIdentifier_ComparedTotalSizeBar = "unity-objects-comparison-view__compared-total-size-bar";
        const string k_UxmlIdentifier_TreeView = "unity-objects-comparison-view__tree-view";
        const string k_UxmlIdentifier_TreeViewColumn__Description = "unity-objects-comparison-view__tree-view__column__description";
        const string k_UxmlIdentifier_TreeViewColumn__CountDelta = "unity-objects-comparison-view__tree-view__column__count-delta";
        const string k_UxmlIdentifier_TreeViewColumn__SizeDeltaBar = "unity-objects-comparison-view__tree-view__column__size-delta-bar";
        const string k_UxmlIdentifier_TreeViewColumn__SizeDelta = "unity-objects-comparison-view__tree-view__column__size-delta";
        const string k_UxmlIdentifier_TreeViewColumn__TotalSizeInA = "unity-objects-comparison-view__tree-view__column__total-size-in-a";
        const string k_UxmlIdentifier_TreeViewColumn__TotalSizeInB = "unity-objects-comparison-view__tree-view__column__total-size-in-b";
        const string k_UxmlIdentifier_TreeViewColumn__CountInA = "unity-objects-comparison-view__tree-view__column__count-in-a";
        const string k_UxmlIdentifier_TreeViewColumn__CountInB = "unity-objects-comparison-view__tree-view__column__count-in-b";
        const string k_UxmlIdentifier_FlattenToggle = "unity-objects-comparison-view__toolbar__flatten-toggle";
        const string k_UxmlIdentifier_UnchangedToggle = "unity-objects-comparison-view__toolbar__unchanged-toggle";
        const string k_UxmlIdentifier_LoadingOverlay = "unity-objects-comparison-view__loading-overlay";
        const string k_UxmlIdentifier_BaseTitleLabel = "unity-objects-comparison-view__secondary__base-title-label";
        const string k_UxmlIdentifier_BaseDescriptionLabel = "unity-objects-comparison-view__secondary__base-description-label";
        const string k_UxmlIdentifier_BaseViewContainer = "unity-objects-comparison-view__secondary__base-table-container";
        const string k_UxmlIdentifier_ComparedTitleLabel = "unity-objects-comparison-view__secondary__compared-title-label";
        const string k_UxmlIdentifier_ComparedDescriptionLabel = "unity-objects-comparison-view__secondary__compared-description-label";
        const string k_UxmlIdentifier_ComparedViewContainer = "unity-objects-comparison-view__secondary__compared-table-container";
        const string k_UxmlIdentifier_ErrorLabel = "unity-objects-comparison-view__error-label";
        const string k_ErrorMessage = "At least one snapshot is from an outdated Unity version that is not fully supported.";

        // Sort comparisons for each column.
        static readonly Dictionary<string, Comparison<TreeViewItemData<UnityObjectsComparisonModel.ItemData>>> k_SortComparisons = new()
        {
            { k_UxmlIdentifier_TreeViewColumn__Description, (x, y) => string.Compare(x.data.Name, y.data.Name, StringComparison.OrdinalIgnoreCase) },
            { k_UxmlIdentifier_TreeViewColumn__CountDelta, (x, y) => x.data.CountDelta.CompareTo(y.data.CountDelta) },
            { k_UxmlIdentifier_TreeViewColumn__SizeDelta, (x, y) => x.data.SizeDelta.CompareTo(y.data.SizeDelta) },
            { k_UxmlIdentifier_TreeViewColumn__SizeDeltaBar, (x, y) => x.data.SizeDelta.CompareTo(y.data.SizeDelta) },
            { k_UxmlIdentifier_TreeViewColumn__TotalSizeInA, (x, y) => x.data.TotalSizeInA.Committed.CompareTo(y.data.TotalSizeInA.Committed) },
            { k_UxmlIdentifier_TreeViewColumn__TotalSizeInB, (x, y) => x.data.TotalSizeInB.Committed.CompareTo(y.data.TotalSizeInB.Committed) },
            { k_UxmlIdentifier_TreeViewColumn__CountInA, (x, y) => x.data.CountInA.CompareTo(y.data.CountInA) },
            { k_UxmlIdentifier_TreeViewColumn__CountInB, (x, y) => x.data.CountInB.CompareTo(y.data.CountInB) },
        };

        // Model.
        readonly CachedSnapshot m_SnapshotA;
        readonly CachedSnapshot m_SnapshotB;
        readonly string m_Description;
        ISelectionDetails m_SelectionDetails;
        bool m_SameSessionComparison;

        // View.
        Label m_DescriptionLabel;
        ToolbarSearchField m_SearchField;
        Toggle m_UnchangedToggle;
        TwoPaneSplitView m_SplitView;
        DetailedSizeBar m_TotalSizeBarA;
        DetailedSizeBar m_TotalSizeBarB;
        Toggle m_FlattenToggle;
        ActivityIndicatorOverlay m_LoadingOverlay;
        Label m_BaseTitleLabel;
        Label m_BaseDescriptionLabel;
        VisualElement m_BaseViewContainer;
        Label m_ComparedTitleLabel;
        Label m_ComparedDescriptionLabel;
        VisualElement m_ComparedViewContainer;
        Label m_ErrorLabel;
        bool m_WaitingForFilteringToBeAppliedToBaseView = false;
        bool m_WaitingForFilteringToBeAppliedToCompareView = false;

        // Child Controllers.
        UnityObjectsTableViewController m_BaseTableViewController;
        UnityObjectsTableViewController m_ComparedTableViewController;

        public UnityObjectsComparisonViewController(CachedSnapshot snapshotA, CachedSnapshot snapshotB, string description, ISelectionDetails selectionDetails)
            : base(idOfDefaultColumnWithPercentageBasedWidth: k_UxmlIdentifier_TreeViewColumn__Description)
        {
            m_SnapshotA = snapshotA;
            m_SnapshotB = snapshotB;
            m_Description = description;
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

            GatherReferencesInView(view);

            return view;
        }

        protected override void ViewLoaded()
        {
            m_SplitView.RegisterCallback<GeometryChangedEvent>(ConfigureSplitViewLayout);
            ConfigureTreeView();

            m_DescriptionLabel.text = m_Description;
            m_FlattenToggle.text = "Flatten Hierarchy";
            m_FlattenToggle.RegisterValueChangedCallback(SetHierarchyFlattened);
            m_UnchangedToggle.text = "Show Unchanged";
            m_UnchangedToggle.RegisterValueChangedCallback(ApplyFilter);

            m_SameSessionComparison = m_SnapshotA.MetaData.SessionGUID != MetaData.InvalidSessionGUID && m_SnapshotA.MetaData.SessionGUID == m_SnapshotB.MetaData.SessionGUID;

            // Configure 'Base (A)' Unity Objects table.
            m_BaseTitleLabel.text = "Base";
            m_BaseTableViewController = new UnityObjectsTableViewController(
                m_SnapshotA,
                null,
                false,
                false,
                this,
                m_SameSessionComparison)
            {
                FlattenHierarchy = true
            };
            m_BaseTableViewController.SetFilters(unityObjectInstanceIdFilter: UnityObjectsModelBuilder.ShowNoObjectsAtAllFilter);
            m_BaseViewContainer.Add(m_BaseTableViewController.View);
            AddChild(m_BaseTableViewController);
            m_BaseTableViewController.SetColumnsVisibility(AllTrackedMemoryTableMode.OnlyCommitted);
            m_BaseTableViewController.HeaderContextMenuPopulateEvent += GenerateEmptyContextMenu;

            // Configure 'Compared (B)' Unity Objects table.
            m_ComparedTitleLabel.text = "Compared";
            m_ComparedTableViewController = new UnityObjectsTableViewController(
                m_SnapshotB,
                null,
                false,
                false,
                this,
                m_SameSessionComparison)
            {
                FlattenHierarchy = true
            };
            m_ComparedTableViewController.SetFilters(unityObjectInstanceIdFilter: UnityObjectsModelBuilder.ShowNoObjectsAtAllFilter);
            m_ComparedViewContainer.Add(m_ComparedTableViewController.View);
            AddChild(m_ComparedTableViewController);
            m_ComparedTableViewController.SetColumnsVisibility(AllTrackedMemoryTableMode.OnlyCommitted);
            m_ComparedTableViewController.HeaderContextMenuPopulateEvent += GenerateEmptyContextMenu;

            BuildModelAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_SelectionDetails?.ClearSelection();
                m_BuildModelWorker?.Dispose();
            }

            base.Dispose(disposing);
        }

        void GatherReferencesInView(VisualElement view)
        {
            m_DescriptionLabel = view.Q<Label>(k_UxmlIdentifier_DescriptionLabel);
            m_SearchField = view.Q<ToolbarSearchField>(k_UxmlIdentifier_SearchField);
            m_SplitView = view.Q<UnityEngine.UIElements.TwoPaneSplitView>(k_UxmlIdentifier_SplitView);
            m_TotalSizeBarA = view.Q<DetailedSizeBar>(k_UxmlIdentifier_BaseTotalSizeBar);
            m_TotalSizeBarB = view.Q<DetailedSizeBar>(k_UxmlIdentifier_ComparedTotalSizeBar);
            m_TreeView = view.Q<MultiColumnTreeView>(k_UxmlIdentifier_TreeView);
            m_FlattenToggle = view.Q<Toggle>(k_UxmlIdentifier_FlattenToggle);
            m_UnchangedToggle = view.Q<Toggle>(k_UxmlIdentifier_UnchangedToggle);
            m_LoadingOverlay = view.Q<ActivityIndicatorOverlay>(k_UxmlIdentifier_LoadingOverlay);
            m_BaseTitleLabel = view.Q<Label>(k_UxmlIdentifier_BaseTitleLabel);
            m_BaseDescriptionLabel = view.Q<Label>(k_UxmlIdentifier_BaseDescriptionLabel);
            m_BaseViewContainer = view.Q<VisualElement>(k_UxmlIdentifier_BaseViewContainer);
            m_ComparedTitleLabel = view.Q<Label>(k_UxmlIdentifier_ComparedTitleLabel);
            m_ComparedDescriptionLabel = view.Q<Label>(k_UxmlIdentifier_ComparedDescriptionLabel);
            m_ComparedViewContainer = view.Q<VisualElement>(k_UxmlIdentifier_ComparedViewContainer);
            m_ErrorLabel = view.Q<Label>(k_UxmlIdentifier_ErrorLabel);
        }

        void ConfigureSplitViewLayout(GeometryChangedEvent evt)
        {
            // There is currently no way to set a split view's initial dimension as a percentage from UXML/USS, so we must do it manually once on load.
            m_SplitView.fixedPaneInitialDimension = m_SplitView.layout.height * 0.5f;
            m_SplitView.UnregisterCallback<GeometryChangedEvent>(ConfigureSplitViewLayout);
        }

        protected override void ConfigureTreeView()
        {
            base.ConfigureTreeView();

            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__Description, "Description", BindCellForDescriptionColumn(), makeCell: UnityObjectsDescriptionCell.Instantiate);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__CountDelta, "Count Difference", BindCellForCountDeltaColumn(), makeCell: CountDeltaCell.Instantiate);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__SizeDeltaBar, "Size Difference Bar", BindCellForSizeDeltaBarColumn(), makeCell: DeltaBarCell.Instantiate);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__SizeDelta, "Size Difference", BindCellForSizeColumn(SizeType.SizeDelta), makeCell: MakeSizeDeltaCell);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__TotalSizeInA, "Size In A", BindCellForSizeColumn(SizeType.TotalSizeInA));
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__TotalSizeInB, "Size In B", BindCellForSizeColumn(SizeType.TotalSizeInB));
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__CountInA, "Count In A", BindCellForCountColumn(CountType.CountInA));
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__CountInB, "Count In B", BindCellForCountColumn(CountType.CountInB));
        }

        protected override void BuildModelAsync()
        {
            // Cancel existing build if necessary.
            m_BuildModelWorker?.Dispose();

            // Show loading UI.
            m_LoadingOverlay.Show();

            // Dispatch asynchronous build.
            var snapshotA = m_SnapshotA;
            var snapshotB = m_SnapshotB;
            var searchStringFilter = ScopedContainsTextFilter.Create(m_SearchField.value);
            var flatten = m_FlattenToggle.value;
            var includeUnchanged = m_UnchangedToggle.value;
            var args = new UnityObjectsComparisonModelBuilder.BuildArgs(
                searchStringFilter,
                null,
                null,
                flatten,
                includeUnchanged,
                m_SameSessionComparison && !flatten,
                OnUnityObjectNameGroupComparisonSelected,
                OnUnityObjectTypeComparisonSelected);
            var sortComparison = BuildSortComparisonFromTreeView();
            m_BuildModelWorker = new AsyncWorker<UnityObjectsComparisonModel>();
            m_BuildModelWorker.Execute((token) =>
            {
                try
                {
                    // Build the data model.
                    var modelBuilder = new UnityObjectsComparisonModelBuilder();
                    var model = modelBuilder.Build(snapshotA, snapshotB, args);
                    token.ThrowIfCancellationRequested();
                    // Sort it according to the current sort descriptors.
                    model.Sort(sortComparison);

                    return model;
                }
                catch (UnsupportedSnapshotVersionException)
                {
                    return null;
                }
                catch (OperationCanceledException)
                {
                    // We expect a TaskCanceledException to be thrown when cancelling an in-progress builder. Do not log an error to the console.
                    return null;
                }
                catch (Exception _e)
                {
                    Debug.LogError($"{_e.Message}\n{_e.StackTrace}");
                    return null;
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

                    // Dispose asynchronous worker.
                    m_BuildModelWorker.Dispose();

                    // Update usage counters
                    MemoryProfilerAnalytics.AddUnityObjectsComparisonUsage(searchStringFilter != null, flatten, includeUnchanged);
                });
        }

        protected override void RefreshView()
        {
            var maxValue = Math.Max(m_Model.TotalSnapshotAMemorySize.Committed, m_Model.TotalSnapshotBMemorySize.Committed);
            SetDetailedProgressBarValues(m_TotalSizeBarA, m_Model.TotalMemorySizeA.Committed, m_Model.TotalSnapshotAMemorySize.Committed, maxValue);
            SetDetailedProgressBarValues(m_TotalSizeBarB, m_Model.TotalMemorySizeB.Committed, m_Model.TotalSnapshotBMemorySize.Committed, maxValue);

            base.RefreshView();
        }

        void SetDetailedProgressBarValues(DetailedSizeBar detailedSizeBar, ulong totalMemorySize, ulong totalSnapshotMemorySize, ulong maxValue)
        {
            detailedSizeBar.Bar.Mode = MemoryBarElement.VisibilityMode.CommittedOnly;
            detailedSizeBar.SetValue(new MemorySize(totalMemorySize, 0), totalSnapshotMemorySize, maxValue);

            var totalMemorySizeText = EditorUtility.FormatBytes((long)totalMemorySize);
            detailedSizeBar.SetSizeText($"Allocated Memory In Table: {totalMemorySizeText}", $"{totalMemorySize:N0} B");

            var totalSnapshotMemorySizeText = EditorUtility.FormatBytes((long)totalSnapshotMemorySize);
            detailedSizeBar.SetTotalText($"Total Memory In Snapshot: {totalSnapshotMemorySizeText}", $"{totalSnapshotMemorySize:N0} B");
        }

        void GenerateEmptyContextMenu(ContextualMenuPopulateEvent evt)
        {
            // Compare mode doesn't allow column configuration
            evt.menu.ClearItems();
            evt.StopImmediatePropagation();
        }

        Action<VisualElement, int> BindCellForDescriptionColumn()
        {
            const string k_NoName = "<No Name>";
            return (element, rowIndex) =>
            {
                var cell = (UnityObjectsDescriptionCell)element;
                var itemData = m_TreeView.GetItemDataForIndex<UnityObjectsComparisonModel.ItemData>(rowIndex);

                var typeName = itemData.NativeTypeName;
                cell.SetTypeName(typeName);

                var displayText = itemData.Name;
                if (string.IsNullOrEmpty(displayText))
                    displayText = k_NoName;
                cell.SetText(displayText);

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
                cell.SetSecondaryText(secondaryDisplayText);
            };
        }

        Action<VisualElement, int> BindCellForSizeDeltaBarColumn()
        {
            return (element, rowIndex) =>
            {
                var cell = (DeltaBarCell)element;
                var sizeDelta = m_TreeView.GetItemDataForIndex<UnityObjectsComparisonModel.ItemData>(rowIndex).SizeDelta;
                var proportionalSizeDelta = 0f;
                if (sizeDelta != 0)
                    proportionalSizeDelta = (float)sizeDelta / m_Model.LargestAbsoluteSizeDelta;
                cell.SetDeltaScalar(proportionalSizeDelta);
                cell.tooltip = FormatBytes(sizeDelta);
            };
        }

        Action<VisualElement, int> BindCellForCountDeltaColumn()
        {
            return (element, rowIndex) =>
            {
                var cell = (CountDeltaCell)element;
                var itemData = m_TreeView.GetItemDataForIndex<UnityObjectsComparisonModel.ItemData>(rowIndex);
                var countDelta = itemData.CountDelta;
                cell.SetCountDelta(countDelta);
            };
        }

        Action<VisualElement, int> BindCellForSizeColumn(SizeType sizeType)
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<UnityObjectsComparisonModel.ItemData>(rowIndex);
                var size = sizeType switch
                {
                    SizeType.SizeDelta => itemData.SizeDelta,
                    SizeType.TotalSizeInA => Convert.ToInt64(itemData.TotalSizeInA.Committed),
                    SizeType.TotalSizeInB => Convert.ToInt64(itemData.TotalSizeInB.Committed),
                    _ => throw new ArgumentException("Unknown size type."),
                };

                ((Label)element).text = FormatBytes(size);
            };
        }

        Action<VisualElement, int> BindCellForCountColumn(CountType countType)
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<UnityObjectsComparisonModel.ItemData>(rowIndex);
                var count = countType switch
                {
                    CountType.CountInA => Convert.ToInt32(itemData.CountInA),
                    CountType.CountInB => Convert.ToInt32(itemData.CountInB),
                    _ => throw new ArgumentException("Unknown count type."),
                };

                ((Label)element).text = $"{count:N0}";
            };
        }

        VisualElement MakeSizeDeltaCell()
        {
            var cell = new Label();
            cell.AddToClassList("unity-multi-column-view__cell__label");

            // Make this a cell with a darkened background. This requires quite a bit of styling to be compatible with tree view selection styling, so that is why it is its own class.
            cell.AddToClassList("dark-tree-view-cell");

            return cell;
        }

        void ApplyFilter(ChangeEvent<bool> evt)
        {
            BuildModelAsync();
        }

        void SetHierarchyFlattened(ChangeEvent<bool> evt)
        {
            BuildModelAsync();
        }

        protected override void OnTreeItemSelected(int itemId, UnityObjectsComparisonModel.ItemData itemData)
        {
            // Invoke the selection processor for the selected item.
            itemData.SelectionProcessor?.Invoke();
        }

        public override void ClearSelection()
        {
            base.ClearSelection();
            m_BaseTableViewController.ClearSelection();
            m_ComparedTableViewController.ClearSelection();
            m_BaseTableViewController.SetFilters(unityObjectInstanceIdFilter: UnityObjectsModelBuilder.ShowNoObjectsAtAllFilter);
            m_ComparedTableViewController.SetFilters(unityObjectInstanceIdFilter: UnityObjectsModelBuilder.ShowNoObjectsAtAllFilter);
        }

        void OnUnityObjectNameGroupComparisonSelected(string objectName, string typeName, string instancId, UnityObjectsComparisonModelBuilder.SnapshotType snapshotType)
        {
            m_BaseTableViewController.ClearSelection();
            m_ComparedTableViewController.ClearSelection();
            m_SelectionDetails.ClearSelection();

            m_WaitingForFilteringToBeAppliedToBaseView = true;
            m_WaitingForFilteringToBeAppliedToCompareView = true;

            var objectNameFilter = MatchesTextFilter.Create(objectName);
            IInstancIdFilter sourceIndexFilter = null;
            if(!string.IsNullOrEmpty(instancId)
                && instancId.Substring(NativeObjectTools.NativeObjectIdFormatStringPrefix.Length).TryConvertToInstanceID(out var instanceIdValue))
            {
                var snapshot = snapshotType switch
                {
                    UnityObjectsComparisonModelBuilder.SnapshotType.Base => m_SnapshotA,
                    UnityObjectsComparisonModelBuilder.SnapshotType.Compared => m_SnapshotB,
                    UnityObjectsComparisonModelBuilder.SnapshotType.Undefined => null,
                    _ => null
                };

                sourceIndexFilter = MatchesInstanceIdFilter.Create(instanceIdValue, snapshot);
            }
            var potentialAssemblyNameStart = typeName.IndexOf(UnityObjectsComparisonModel.AssemblyNameDisambiguationSeparator);
            if (potentialAssemblyNameStart != -1)
                typeName = typeName.Substring(0, potentialAssemblyNameStart);
            // TODO: This is a hack due for a pending refactor on a different branch that does away with the need to string filter the bottom tables.
            // Right now it will show objects with the same name, type, and instance ID, but potentially different assemblies.
            var typeNameFilter = MatchesTextFilter.Create(typeName);
            m_BaseTableViewController.SetFilters(
                unityObjectNameFilter: objectNameFilter,
                unityObjectTypeNameFilter: typeNameFilter,
                unityObjectInstanceIdFilter: sourceIndexFilter);
            m_ComparedTableViewController.SetFilters(
                unityObjectNameFilter: objectNameFilter,
                unityObjectTypeNameFilter: typeNameFilter,
                unityObjectInstanceIdFilter: sourceIndexFilter);
        }

        void OnUnityObjectTypeComparisonSelected(string nativeTypeName)
        {
            m_BaseTableViewController.ClearSelection();
            m_ComparedTableViewController.ClearSelection();
            m_SelectionDetails.ClearSelection();

            m_BaseTableViewController.SetFilters(
                unityObjectInstanceIdFilter: UnityObjectsModelBuilder.ShowNoObjectsAtAllFilter);
            m_ComparedTableViewController.SetFilters(
                unityObjectInstanceIdFilter: UnityObjectsModelBuilder.ShowNoObjectsAtAllFilter);
        }

        void UnityObjectsTableViewController.IResponder.UnityObjectsTableViewControllerReloaded(
            UnityObjectsTableViewController tableViewController,
            bool success)
        {
            var isBaseTable = tableViewController == m_BaseTableViewController;
            var descriptionLabel = (isBaseTable) ? m_BaseDescriptionLabel : m_ComparedDescriptionLabel;
            var filteringForInstanceID = tableViewController.SourceIndexFilter != null;
            // This kind of filtering is essentially a hack to keep the Base & Compare tables empty.
            var tableIsFilteringExplicitlyForNoObjects = filteringForInstanceID && tableViewController.SourceIndexFilter.Passes(CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone);
            if (!tableIsFilteringExplicitlyForNoObjects)
            {
                var model = tableViewController.Model;
                var objectCount = model.RootNodes.Count;
                descriptionLabel.text = $"{objectCount:N0} object{(objectCount != 1 ? "s" : string.Empty)} with same {(filteringForInstanceID ? "ID" : "name")} | Group size: {EditorUtility.FormatBytes(Convert.ToInt64(model.TotalMemorySize.Committed))}";
            }

            // Hide the description if the table is explicitly filtering for 'no objects'.
            UIElementsHelper.SetElementDisplay(descriptionLabel, !tableIsFilteringExplicitlyForNoObjects);

            var selectFirstItem = false;
            if(isBaseTable && m_WaitingForFilteringToBeAppliedToBaseView)
            {
                // select the first item if the Responder was just waiting for Base View to reload
                selectFirstItem = !m_WaitingForFilteringToBeAppliedToCompareView;
                m_WaitingForFilteringToBeAppliedToBaseView = false;
            }
            else if (!isBaseTable && m_WaitingForFilteringToBeAppliedToCompareView)
            {
                // select the first item if the Responder was just waiting for Compare View to reload
                selectFirstItem = !m_WaitingForFilteringToBeAppliedToBaseView;
                m_WaitingForFilteringToBeAppliedToCompareView = false;
            }
            if (selectFirstItem)
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
            var snapshot = isBaseView ? m_SnapshotA : m_SnapshotB;
            var view = BreakdownDetailsViewControllerFactory.Create(snapshot, itemId, itemData.Name, itemData.ChildCount, itemData.Source);
            m_SelectionDetails.SetSelection(view);
        }

        Comparison<TreeViewItemData<UnityObjectsComparisonModel.ItemData>> BuildSortComparisonFromTreeView()
        {
            var sortedColumns = m_TreeView.sortedColumns;
            if (sortedColumns == null)
                return null;

            var sortComparisons = new List<Comparison<TreeViewItemData<UnityObjectsComparisonModel.ItemData>>>();
            foreach (var sortedColumnDescription in sortedColumns)
            {
                if (sortedColumnDescription == null)
                    continue;

                var sortComparison = k_SortComparisons[sortedColumnDescription.columnName];

                // Invert the comparison's input arguments depending on the sort direction.
                var sortComparisonWithDirection = (sortedColumnDescription.direction == SortDirection.Ascending) ? sortComparison : (x, y) => sortComparison(y, x);
                sortComparisons.Add(sortComparisonWithDirection);
            }

            return (x, y) =>
            {
                var result = 0;
                foreach (var sortComparison in sortComparisons)
                {
                    result = sortComparison.Invoke(x, y);
                    if (result != 0)
                        break;
                }

                return result;
            };
        }

        static string FormatBytes(long bytes)
        {
            var sizeText = new System.Text.StringBuilder();

            // Our built-in formatter for bytes doesn't support negative values.
            if (bytes < 0)
                sizeText.Append("-");

            var absoluteBytes = Math.Abs(bytes);
            sizeText.Append(EditorUtility.FormatBytes(absoluteBytes));
            return sizeText.ToString();
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

        enum SizeType
        {
            SizeDelta,
            TotalSizeInA,
            TotalSizeInB,
        }

        enum CountType
        {
            CountInA,
            CountInB,
        }
    }
}
#endif

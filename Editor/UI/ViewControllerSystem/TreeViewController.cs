using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    interface INamedTreeItemData
    {
        // The name of this item.
        string Name { get; }
    }

    abstract class SingleSnapshotTreeViewController<TModel, TTreeItemData> : TreeViewController<TModel, TTreeItemData>
        where TModel : TreeModel<TTreeItemData> where TTreeItemData : IPrivateComparableItemData
    {
        // assume that, by default, switching the mode means we need to rebuild the model
        protected virtual bool SwitchingTableModeRequiresRebuild { get; } = true;

        protected abstract string UxmlIdentifier_TreeViewColumn__Size { get; }
        protected abstract string UxmlIdentifier_TreeViewColumn__ResidentSize { get; }
        protected SingleSnapshotTreeViewController(string idOfDefaultColumnWithPercentageBasedWidth) : base(idOfDefaultColumnWithPercentageBasedWidth)
        {
            TableMode = AllTrackedMemoryTableMode.OnlyCommitted;
        }

        protected AllTrackedMemoryTableMode TableMode { get; set; }

        public abstract bool ExcludeAllFilterApplied { get; }

        // TODO: Move to TreeViewController after adjusting comparison tables to have Resident vs Committed info
        public virtual async Task SetColumnsVisibilityAsync(AllTrackedMemoryTableMode mode)
        {
            if (TableMode != mode)
            {
                TableMode = mode;
                if (IsViewLoaded)
                {
                    SetColumnVisibilityAcordingToTableMode();
                    if (SwitchingTableModeRequiresRebuild)
                    {
                        await BuildModelAsync(false);
                    }
                    else
                    {
                        if (m_Model != null)
                            RefreshView();
                        else if (!HasModelOrIsBuildingOne)
                            await BuildModelAsync(false);
                    }
                }
            }
            await Task.CompletedTask;
        }

        protected void SetColumnVisibilityAcordingToTableMode()
        {
            var columns = m_TreeView.columns;
            switch (TableMode)
            {
                case AllTrackedMemoryTableMode.OnlyResident:
                    columns[UxmlIdentifier_TreeViewColumn__Size].visible = false;
                    columns[UxmlIdentifier_TreeViewColumn__ResidentSize].visible = true;
                    break;
                case AllTrackedMemoryTableMode.OnlyCommitted:
                    columns[UxmlIdentifier_TreeViewColumn__Size].visible = true;
                    columns[UxmlIdentifier_TreeViewColumn__ResidentSize].visible = false;
                    break;
                case AllTrackedMemoryTableMode.CommittedAndResident:
                    columns[UxmlIdentifier_TreeViewColumn__Size].visible = true;
                    columns[UxmlIdentifier_TreeViewColumn__ResidentSize].visible = true;
                    break;
            }
        }
    }

    static class TreeViewControllerHelper
    {
        public static readonly IList<int> ShowNoItemsAtAllFilter = new List<int>().AsReadOnly();
    }

    /// <summary>
    /// Base Tree view controller class, abstracting shared code used by all Tree View Controllers
    /// </summary>
    abstract class TreeViewController<TModel, TTreeItemData> : ViewController where TModel : TreeModel<TTreeItemData> where TTreeItemData : INamedTreeItemData
    {
        const string k_UssClass_Dark = "table-view-base__dark";
        const string k_UssClass_Light = "table-view-base__light";

        // Composed component for async model building
        AsyncModelBuildCoordinator<TModel> m_BuildCoordinator;

        // Model.
        protected bool HasModelOrIsBuildingOne => m_BuildCoordinator.HasModelOrIsBuildingOne;
        protected virtual TModel m_Model
        {
            get => m_BuildCoordinator.CurrentModel;
            set => throw new NotImplementedException();
        }
        protected virtual List<TreeViewItemData<TTreeItemData>> RootNodes => m_Model.RootNodes;
        protected abstract Dictionary<string, Comparison<TreeViewItemData<TTreeItemData>>> SortComparisons { get; }
        // View.
        protected MultiColumnTreeView m_TreeView;
        protected ActivityIndicatorOverlay m_LoadingOverlay;
        protected Label m_ErrorLabel;

        readonly string m_IDOfDefaultColumnWithPercentageBasedWidth = null;
        protected abstract ToolbarSearchField SearchField { get; }

        protected event Action<IScopedFilter<string>> SearchFilterChanged = null;

        List<string> m_SelectionPath = new List<string>();

        public TreeViewController(string idOfDefaultColumnWithPercentageBasedWidth)
        {
            m_IDOfDefaultColumnWithPercentageBasedWidth = idOfDefaultColumnWithPercentageBasedWidth;
            m_BuildCoordinator = new AsyncModelBuildCoordinator<TModel>();
        }

        /// <summary>
        /// The default assumption for Memory Profiler tables is that they don't need a header context menu, so we provide an empty one by default.
        /// If a view wants to allow Users to enable or disable optional columns, they can subscribe to this event with an empty handler to get the
        /// default menu or provide or append their own menu items.
        /// </summary>
        public event Action<ContextualMenuPopulateEvent> HeaderContextMenuPopulateEvent;

        public virtual void ClearSelection()
        {
            // TreeView doesn't have ClearSelection without notification.
            // We don't need notification as we need notification only
            // on user input
            m_TreeView.SetSelectionWithoutNotify(Array.Empty<int>());
            m_SelectionPath.Clear();
        }

        /// <summary>
        /// Selects the first item in the table.
        /// </summary>
        /// <returns>true if an item was selected, false if not.</returns>
        public bool SelectFirstItem()
        {
            if (m_TreeView.GetTreeCount() <= 0)
                return false;
            // force a selection notification by making sure the selected index changed
            if (m_TreeView.selectedIndex == 0)
                ClearSelection();
            m_TreeView.SetSelection(0);
            return true;
        }

        protected override void ViewLoaded()
        {
            ConfigureTreeView();

            // Apply table-specific theme classes
            var baseThemeUssClass = (EditorGUIUtility.isProSkin) ? k_UssClass_Dark : k_UssClass_Light;
            View.AddToClassList(baseThemeUssClass);

            // The loading overlay starts visible (from UXML). Hide it here; the owner of this controller
            // is responsible for calling TriggerBuild() to start the background build, which will show
            // the overlay again and hide it on completion.
            m_LoadingOverlay.Hide();
        }

        /// <summary>
        /// Triggers a background model build. Shows the loading overlay until the build completes.
        /// Call this after the view has been added to the UI hierarchy and all initial configuration
        /// (e.g. column visibility, filters) is complete.
        /// </summary>
        public void TriggerBuild()
        {
            FireAndForgetBuildModelAsync(false);
        }

        [NonSerialized]
        bool m_Configured = false;
        protected virtual void ConfigureTreeView()
        {
            if (m_Configured)
            {
#if ENABLE_MEMORY_PROFILER_DEBUG
                Debug.LogError($"Configuring {GetType()} twice!");
#endif
                return;
            }

            m_TreeView.RegisterCallback<GeometryChangedEvent>(ConfigureInitialTreeViewLayout);

            if (SearchField != null)
            {
                SearchField.RegisterValueChangedCallback(OnSearchValueChanged);
                // Delayed search is generally preferable for TreeViews, which can be expensive to update and don't need to update on every keystroke to feel responsive.
                var searchInput = SearchField.Q<TextInputBaseField<string>>();
                if (searchInput != null)
                {
                    searchInput.isDelayed = true;
                }
            }

            m_TreeView.selectionChanged += OnTreeViewSelectionChanged;
            m_TreeView.columnSortingChanged += OnTreeViewSortingChanged;
            m_TreeView.headerContextMenuPopulateEvent += GenerateContextMenu;
            m_Configured = true;
        }

        protected void ConfigureInitialTreeViewLayout(GeometryChangedEvent evt)
        {
            // There is currently no way to set a tree view column's initial width as a percentage from UXML/USS, so we must do it manually once on load.
            if (!string.IsNullOrEmpty(m_IDOfDefaultColumnWithPercentageBasedWidth))
            {
                var column = m_TreeView.columns[m_IDOfDefaultColumnWithPercentageBasedWidth];
                column.width = m_TreeView.layout.width * 0.4f;
            }
            m_TreeView.UnregisterCallback<GeometryChangedEvent>(ConfigureInitialTreeViewLayout);
        }

        protected void OnTreeViewSelectionChanged(IEnumerable<object> items)
        {
            m_SelectionPath.Clear();
            var selectedIndex = m_TreeView.selectedIndex;
            if (selectedIndex == -1)
                return;

            var itemId = m_TreeView.GetIdForIndex(selectedIndex);
            var itemData = m_TreeView.GetItemDataForIndex<TTreeItemData>(selectedIndex);
            OnTreeItemSelected(itemId, itemData);

            m_SelectionPath.Add(itemData.Name);
            var parentId = m_TreeView.GetParentIdForIndex(selectedIndex);
            while (parentId >= 0)
            {
                itemData = m_TreeView.GetItemDataForId<TTreeItemData>(parentId);
                m_SelectionPath.Insert(0, itemData.Name);
                parentId = m_TreeView.viewController.GetParentId(parentId);
            }
        }

        protected abstract void OnTreeItemSelected(int itemId, TTreeItemData itemData);

        protected virtual void OnTreeViewSortingChanged()
        {
            var sortedColumns = m_TreeView.sortedColumns;
            if (sortedColumns == null)
                return;

            // OnTreeViewSortingChanged is a listener to UI input and therefore doesn't expect an awaitable task.
            // No caller or following code expects any part of the async build to be done.
            // Therefore: Fire-and-forget with proper error handling.
            FireAndForgetBuildModelAsync(justSort: true);
        }

        void OnSearchValueChanged(ChangeEvent<string> evt)
        {
            if (SearchFilterChanged != null)
            {
                var searchText = evt.newValue;
                var searchFilter = ScopedContainsTextFilter.Create(searchText);
                SearchFilterChanged(searchFilter);
            }
            else if (IsViewLoaded)
                // OnSearchValueChanged is a listener to UI input and therefore doesn't expect an awaitable task.
                // No caller or following code expects any part of the async build to be done.
                // Therefore: Fire-and-forget with proper error handling.
                FireAndForgetBuildModelAsync();
        }

        void GenerateContextMenu(ContextualMenuPopulateEvent evt, Column column)
        {
            if (HeaderContextMenuPopulateEvent == null)
                GenerateEmptyContextMenu(evt);
            else
                HeaderContextMenuPopulateEvent(evt);
        }

        protected void GenerateEmptyContextMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.ClearItems();
            evt.StopImmediatePropagation();
        }

        protected abstract Func<TModel> GetModelBuilderTask(CancellationToken cancellationToken);

        protected virtual Func<Task<TModel>, TModel> GetModelSorterTask(CancellationToken cancellationToken)
        {
            // Capture all variables locally in case they are changed before the sorting task is started
            var sortComparison = TreeViewSortHelper.BuildSortComparison(m_TreeView, SortComparisons);
            return new Func<Task<TModel>, TModel>((t) =>
            {
                AsyncTaskHelper.DebugLogAsyncStep("Start Sorting                 " + typeof(TModel));
                cancellationToken.ThrowIfCancellationRequested();
                AsyncTaskHelper.DebugLogAsyncStep("Start Sorting (not Canceled)                 " + typeof(TModel));

                var model = t.Result;
                // Sort it according to the current sort descriptors.
                model?.Sort(sortComparison);

                cancellationToken.ThrowIfCancellationRequested();
                AsyncTaskHelper.DebugLogAsyncStep("Sorting Finished                 " + typeof(TModel));
                return model;
            });
        }

        /// <summary>
        /// Executes BuildModelAsync in a fire-and-forget manner with proper exception handling.
        /// This is appropriate for UI event handlers that cannot be async and don't need to wait for completion.
        /// Exceptions are logged but don't crash the application.
        /// </summary>
        protected async void FireAndForgetBuildModelAsync(bool justSort = false)
        {
            try
            {
                await BuildModelAsync(justSort);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is expected and acceptable - don't log
            }
            catch (Exception e)
            {
                // Log unexpected exceptions but don't rethrow (would crash the application in async void)
                Debug.LogException(e);
            }
        }

        protected virtual async Task BuildModelAsync(bool justSort = false)
        {
            await m_BuildCoordinator.BuildModelAsync(
                GetModelBuilderTask,
                GetModelSorterTask,
                (model, success, modelHasNewTreeViewIds, exception) => OnModelRebuild(success, modelHasNewTreeViewIds, exception),
                OnViewReloaded,
                () => m_LoadingOverlay.Show(),
                () => m_LoadingOverlay.Hide(),
                justSort);
        }

        protected virtual async Task FilterModelAsync<TModelBuilderArgs>(TModel baseModel, IList<int> treeNodeIdFilter, ModelBuildOrchestrator<TModel, TModelBuilderArgs> modelBuildOrchestrator)
        {
            await m_BuildCoordinator.BuildModelAsync(
                (ct) => modelBuildOrchestrator.CreateBuildTask(default(TModelBuilderArgs), baseModel, treeNodeIdFilter, ct),
                GetModelSorterTask,
                (model, success, modelHasNewTreeViewIds, exception) => OnModelRebuild(success, modelHasNewTreeViewIds, exception),
                OnViewReloaded,
                () => m_LoadingOverlay.Show(),
                () => m_LoadingOverlay.Hide(),
                justSort: false);
        }

        protected virtual void OnModelRebuild(bool success, bool modelHasNewTreeViewIds, Exception exception = null)
        {
            if (success)
            {
                // Refresh UI with new data model, but don't restore the selection if the model has new tree view Ids
                RefreshView(restoreSelection: !modelHasNewTreeViewIds);
            }
            else
            {
                // Display error message.
                if (exception != null)
                    m_ErrorLabel.text = exception.Message;
                else
                    m_ErrorLabel.text = TextContent.UnclearViewLoadingIssue;
                UIElementsHelper.SetElementDisplay(m_ErrorLabel, true);
            }
        }

        protected virtual void RefreshView(bool restoreSelection = true)
        {
            m_TreeView.SetRootItems(m_Model.RootNodes);
            m_TreeView.RefreshItems();
            if (restoreSelection)
                RestoreSelection();
            else
                ClearSelection();
        }

        protected virtual void OnViewReloaded(bool success) { }

        void RestoreSelection()
        {
            var idPath = new List<int>();
            bool failedToFindFullPath = false;
            if (m_SelectionPath.Count > 0)
            {
                var rootIds = m_TreeView.GetRootIds();
                if (rootIds != null)
                {
                    var ids = rootIds.GetEnumerator();
                    for (int i = 0; i < m_SelectionPath.Count; i++)
                    {
                        var name = m_SelectionPath[i];
                        while (ids.MoveNext())
                        {
                            var itemData = m_TreeView.GetItemDataForId<TTreeItemData>(ids.Current);
                            if (itemData.Name == name)
                            {
                                idPath.Add(ids.Current);
                                var childIds = m_TreeView.viewController.GetChildrenIds(ids.Current);
                                if (childIds == null)
                                {
                                    failedToFindFullPath = i == m_SelectionPath.Count - 1;
                                }
                                else
                                {
                                    // switch out the ids and jump to the next name
                                    ids.Dispose();
                                    ids = childIds.GetEnumerator();
                                }
                                break;
                            }
                        }
                        if (failedToFindFullPath)
                            break;
                    }
                    ids.Dispose();
                }
            }
            if (idPath.Count > 0 && !failedToFindFullPath)
            {
                // first silently clear the selection, so that reselection fires selection events
                m_TreeView.SetSelectionByIdWithoutNotify(new int[] { -1 });

                // if it was fully found, select, notify, expand towards and frame the selection
                var idToSelect = idPath[idPath.Count - 1];
                m_TreeView.SetSelectionById(idToSelect);
                foreach (var idInPath in idPath)
                {
                    m_TreeView.ExpandItem(idInPath);
                }
                m_TreeView.ScrollToItemById(idToSelect);
            }
            else
                // otherwise silently clear selection so that it doesn't count as an active user selection,
                // doesn't clear m_SelectionPath and allows reconstructing it when search is cleared
                m_TreeView.SetSelectionByIdWithoutNotify(new int[] { -1 });

        }

        protected void ConfigureTreeViewColumn(string columnName, string columnTitle, Action<VisualElement, int> bindCell, int width = 0, bool visible = true, Func<VisualElement> makeCell = null)
        {
            var column = m_TreeView.columns[columnName];
            column.title = columnTitle;
            column.bindCell = bindCell;
            column.visible = visible;
            if (width != 0)
            {
                column.width = width;
                column.minWidth = width;
            }
            if (makeCell != null)
                column.makeCell = makeCell;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                m_BuildCoordinator?.Dispose();

            SearchField?.UnregisterValueChangedCallback(OnSearchValueChanged);

            base.Dispose(disposing);
        }
    }
}

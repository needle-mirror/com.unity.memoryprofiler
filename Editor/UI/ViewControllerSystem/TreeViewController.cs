using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
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

    abstract class AbstractComparisonTreeViewController<TComparisonModel, TComparisonTreeItemData, TBaseModel, TBaseModelItemData>
        : TreeViewController<TComparisonModel, TComparisonTreeItemData>
        where TComparisonModel : TreeModel<TComparisonTreeItemData>, IComparisonTreeModel<TComparisonTreeItemData, TBaseModel>
        where TComparisonTreeItemData : INamedTreeItemData
        where TBaseModel : TreeModel<TBaseModelItemData>
        where TBaseModelItemData : INamedTreeItemData
    {
        const string k_ErrorMessage = "At least one snapshot is from an outdated Unity version that is not fully supported.";
        protected override string ModelBuilderErrorMessage => k_ErrorMessage;
        protected AbstractComparisonTreeViewController(string idOfDefaultColumnWithPercentageBasedWidth, bool buildOnLoad = true) : base(idOfDefaultColumnWithPercentageBasedWidth, buildOnLoad)
        {
        }
    }

    abstract class SingleSnapshotTreeViewController<TModel, TTreeItemData> : TreeViewController<TModel, TTreeItemData>
        where TModel : TreeModel<TTreeItemData> where TTreeItemData : IPrivateComparableItemData
    {
        // assume that, by default, switching the mode means we need to rebuild the model
        protected virtual bool SwitchingTableModeRequiresRebuild { get; } = true;

        protected abstract string UxmlIdentifier_TreeViewColumn__Size { get; }
        protected abstract string UxmlIdentifier_TreeViewColumn__ResidentSize { get; }
        protected SingleSnapshotTreeViewController(string idOfDefaultColumnWithPercentageBasedWidth, bool buildOnLoad = true) : base(idOfDefaultColumnWithPercentageBasedWidth, buildOnLoad)
        {
            TableMode = AllTrackedMemoryTableMode.OnlyCommitted;
            m_TreeNodeIdFilter = null;
        }

        protected AllTrackedMemoryTableMode TableMode { get; set; }

        public bool ExcludeAllFilterApplied => m_TreeNodeIdFilter != null && m_TreeNodeIdFilter.Count == 0;
        protected TModel m_BaseModelForTreeNodeIdFiltering;
        protected List<int> m_TreeNodeIdFilter;

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

    /// <summary>
    /// Base Tree view controller class, abstracting shared code used by all Tree View Controllers
    /// </summary>
    abstract class TreeViewController<TModel, TTreeItemData> : ViewController where TModel : TreeModel<TTreeItemData> where TTreeItemData : INamedTreeItemData
    {
        const string k_ErrorMessage = "Snapshot is from an outdated Unity version that is not fully supported.";
        protected virtual string ModelBuilderErrorMessage => k_ErrorMessage;

        // Model.
        protected bool HasModelOrIsBuildingOne => m_LastBuildModelTask != null;
        protected virtual TModel m_Model
        {
            get => (m_LastFinalizedModelTask?.IsCompletedSuccessfully ?? false) ? m_LastFinalizedModelTask.Result : default;
            set => throw new NotImplementedException();
        }
        protected virtual List<TreeViewItemData<TTreeItemData>> RootNodes => m_Model.RootNodes;
        protected abstract Dictionary<string, Comparison<TreeViewItemData<TTreeItemData>>> SortComparisons { get; }
        readonly bool m_BuildOnLoad;

        Task<TModel> m_LastBuildModelTask;
        Task<TModel> m_LastFinalizedModelTask;
        List<Task<TModel>> m_CanceledTasks = new List<Task<TModel>>();
        CancellationTokenSource m_BuildModelCTS;
        Func<Task<TModel>, TModel> m_PendingModelSorting;
        CancellationTokenSource m_BuildModelSortingCTS;

        // View.
        protected MultiColumnTreeView m_TreeView;
        protected ActivityIndicatorOverlay m_LoadingOverlay;
        protected Label m_ErrorLabel;

        readonly string m_IDOfDefaultColumnWithPercentageBasedWidth = null;
        protected abstract ToolbarSearchField SearchField { get; }

        protected event Action<IScopedFilter<string>> SearchFilterChanged = null;

        List<string> m_SelectionPath = new List<string>();

        public TreeViewController(string idOfDefaultColumnWithPercentageBasedWidth, bool buildOnLoad = true)
        {
            m_IDOfDefaultColumnWithPercentageBasedWidth = idOfDefaultColumnWithPercentageBasedWidth;
            m_BuildOnLoad = buildOnLoad;
        }

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

            if (m_BuildOnLoad)
                // Building on load in ViewLoaded is an automatic trigger, likely to be automatically triggered for accessing the .View property,
                // which therefore doesn't expect an awaitable task.
                // No caller or following code expects any part of the asnyc build to be done.
                // Therefore: Discard the returned task.
                _ = BuildModelAsync(false);
            else
                m_LoadingOverlay.Hide();
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
            // No caller or following code expects any part of the asnyc build to be done.
            // Therefore: Discard the returned task.
            _ = BuildModelAsync(justSort: true);
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
                // No caller or following code expects any part of the asnyc build to be done.
                // Therefore: Discard the returned task.
                _ = BuildModelAsync();
        }

        void GenerateContextMenu(ContextualMenuPopulateEvent evt, Column column)
        {
            if (HeaderContextMenuPopulateEvent == null)
                GenerateEmptyContextMenu(evt, column);
            else
                HeaderContextMenuPopulateEvent(evt);
        }

        void GenerateEmptyContextMenu(ContextualMenuPopulateEvent evt, Column column)
        {
            evt.menu.ClearItems();
            evt.StopImmediatePropagation();
        }

        protected abstract Func<TModel> GetModelBuilderTask(CancellationToken cancellationToken);

        protected virtual Func<Task<TModel>, TModel> GetModelSorterTask(CancellationToken cancellationToken)
        {
            // Capture all variables locally in case they are changed before the sorting task is started
            var sortComparison = BuildSortComparisonFromTreeView();
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

        protected virtual async Task BuildModelAsync(bool justSort = false)
        {
            // Show loading UI.
            m_LoadingOverlay.Show();

            // cleanup previously canceled builds
            for (var i = m_CanceledTasks.Count - 1; i >= 0; i--)
            {
                if (!m_CanceledTasks[i].IsCompleted) continue;
                m_CanceledTasks[i].Dispose();
                m_CanceledTasks.RemoveAt(i);
            }

            // grab the main thread scheduler before any 'await' can move this method off of the main thread
            var mainThreadScheduler = TaskScheduler.FromCurrentSynchronizationContext();
            CancellationTokenSource buildCTS = null;

            var modelTypeName = typeof(TModel).ToString();

            AsyncTaskHelper.DebugLogAsyncStep("Async Building setup                 " + modelTypeName);
            if (!justSort || !HasModelOrIsBuildingOne)
            {
                // if we don't just sort or have no previous model builder task to continue on, we need to do a full (re)build

                // cancel previous build, if any. this will also cancel the current sort task
                if (m_LastBuildModelTask is { IsCompleted: false })
                    m_CanceledTasks.Add(m_LastBuildModelTask);
                m_BuildModelCTS?.Cancel();
                m_BuildModelCTS = buildCTS = new CancellationTokenSource();

                AsyncTaskHelper.DebugLogAsyncStep("Async Building setup - Builder                 " + modelTypeName);
                // Get the task on mainthread as some builder configs come from EditorPrefs, which is main thread accessible only
                var buildModelChildTask = GetModelBuilderTask(buildCTS.Token);
                m_LastBuildModelTask = Task.Run(() =>
                {
                    try
                    {
                        buildCTS.Token.ThrowIfCancellationRequested();
                        // run actual task on the threaded task's thread
                        return buildModelChildTask();
                    }
                    catch (OperationCanceledException)
                    {
                        // cancellation is expected. Log it though.
                        AsyncTaskHelper.DebugLogAsyncStep("Building Canceled                 " + modelTypeName);
                        // continuation tasks check for cancellation and against null models, but critically,
                        // the UI needs to get updated (by the last task in the queue) no matter the exception.
                        return null;
                    }
                    catch (UnsupportedSnapshotVersionException)
                    {
                        return null;
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        return null;
                    }
                }, buildCTS.Token);
            }
            else
            {
                AsyncTaskHelper.DebugLogAsyncStep("Async Building setup - Builder Reuse                 " + modelTypeName);
                // The builder might still be around, but the CancellationTokenSource might have been disposed and nulled.
                // If so, create a new one so that the rest of the code behaves consistently.
                buildCTS = m_BuildModelCTS ??= new CancellationTokenSource();
            }

            // cancel pending sort operations before creating new sort cancelation token source
            m_BuildModelSortingCTS?.Cancel();
            var sortCTS = m_BuildModelSortingCTS = new CancellationTokenSource();

            // store current sorting state as sorting delegate before awaiting the builder task,
            // which frees the main thread to trigger a new sorting task while the model is still building
            m_PendingModelSorting = GetModelSorterTask(sortCTS.Token);

            buildCTS.Token.Register(
                () =>
                {
                    // cancel whatever sort operation would have followed this build, which may not be the one that was started with it
                    // therefore use the CTS stored on instance fields rather than method local fields
                    if (m_BuildModelSortingCTS != null && !m_BuildModelSortingCTS.IsCancellationRequested)
                        m_BuildModelSortingCTS.Cancel();
                    m_BuildModelSortingCTS = null;
                    AsyncTaskHelper.DebugLogAsyncStep("buildCTS canceled Sorting                 " + modelTypeName);
                }
            );

            TModel MainThreadUIIntegrationFunc(Task<TModel> t)
            {
                sortCTS.Token.ThrowIfCancellationRequested();
                AsyncTaskHelper.DebugLogAsyncStep("Apply Model                 " + modelTypeName);

                // Apply the model
                var model = t.Result;
                m_LastFinalizedModelTask = Task.FromResult(model);

                var success = model != null;
                OnModelRebuild(success);

                // Hide loading UI.
                m_LoadingOverlay.Hide();

                // Notify responder.
                OnViewReloaded(success);

                AsyncTaskHelper.DebugLogAsyncStep("Model Applied                 " + modelTypeName);
                return model;
            }
            ;

            try
            {
                // Wait for the model to finish building to avoid scheduling a continuation to a task that might've been canceled
                await m_LastBuildModelTask;

                AsyncTaskHelper.DebugLogAsyncStep("Async Building setup - Sorting                 " + modelTypeName);
                var sortTaskFunc = m_PendingModelSorting;
                buildCTS.Token.ThrowIfCancellationRequested();
                sortCTS.Token.ThrowIfCancellationRequested();
                var sortTask = m_LastBuildModelTask.ContinueWith(t => sortTaskFunc(t),
                    sortCTS.Token,
                    // Option None instead of OnlyOnRanToCompletion because with the latter it's unclear if cancellation ripples through and all tasks will end (ending the Task.WhenAll).
                    // Since Builder task cancels sort task, only running to completion is implied.
                    // None also means this task will run Async, i.e. off the main thread, unless given the mainThreadScheduler (see mainThreadUIIntegrationTask)
                    TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);

                AsyncTaskHelper.DebugLogAsyncStep("Async Building setup - Integration                 " + modelTypeName);
                var mainThreadUIIntegrationTask = sortTask.ContinueWith(MainThreadUIIntegrationFunc
                    , sortCTS.Token, TaskContinuationOptions.None, mainThreadScheduler);

                AsyncTaskHelper.DebugLogAsyncStep("Async Building - Await                 " + modelTypeName);
                await mainThreadUIIntegrationTask.ConfigureAwait(false);

                AsyncTaskHelper.DebugLogAsyncStep("Async Building - Await Fininshed                 " + modelTypeName);
            }
            catch (OperationCanceledException)
            {
                // meh, expected. Ignore.
                AsyncTaskHelper.DebugLogAsyncStep("Async Building - Canceled                 " + modelTypeName);
            }
            catch (Exception e)
            {
                // Update the UI even if an exception happens
                try
                {
                    MainThreadUIIntegrationFunc(Task.FromResult((TModel)null));
                }
                catch (OperationCanceledException) { }
                Debug.LogException(e);
            }
            finally
            {
                // Cleanup:
                // There can only ever be one overall model builder & Sort combo that will run to completion at a time
                // but while a new one might have been started and set as m_BuildModelTask,
                // an old one could be canceled and still run to the end.
                if (!buildCTS.IsCancellationRequested)
                {
                    // Only clean up the instance fields if this is the running main task and it was not canceled
                    m_BuildModelCTS = null;
                    m_BuildModelSortingCTS = null;
                }
                buildCTS.Dispose();
                sortCTS.Dispose();

                AsyncTaskHelper.DebugLogAsyncStep("Async Building - Finally                 " + modelTypeName);
            }
            AsyncTaskHelper.DebugLogAsyncStep("Async Building - Done                 " + modelTypeName);
        }

        protected virtual void OnModelRebuild(bool success)
        {
            if (success)
            {
                // Refresh UI with new data model.
                RefreshView();
            }
            else
            {
                // Display error message.
                m_ErrorLabel.text = ModelBuilderErrorMessage;
                UIElementsHelper.SetElementDisplay(m_ErrorLabel, true);
            }
        }

        protected virtual void RefreshView()
        {
            m_TreeView.SetRootItems(m_Model.RootNodes);
            m_TreeView.RefreshItems();
            RestoreSelection();
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

        protected Comparison<TreeViewItemData<TTreeItemData>> BuildSortComparisonFromTreeView()
        {
            var sortedColumns = m_TreeView.sortedColumns;
            if (sortedColumns == null)
                return null;

            var sortComparisons = new List<Comparison<TreeViewItemData<TTreeItemData>>>();
            foreach (var sortedColumnDescription in sortedColumns)
            {
                if (sortedColumnDescription == null)
                    continue;

                var sortComparison = SortComparisons[sortedColumnDescription.columnName];

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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_BuildModelCTS?.Cancel();
                m_BuildModelSortingCTS?.Cancel();
                m_BuildModelCTS = null;
                m_BuildModelSortingCTS = null;

                DisposeTaskAsync(m_LastBuildModelTask).Wait();

                if (m_LastBuildModelTask != m_LastFinalizedModelTask)
                    DisposeTaskAsync(m_LastFinalizedModelTask).Wait();

                foreach (var canceledTask in m_CanceledTasks)
                {
                    if (!canceledTask.IsCompleted)
                        DisposeTaskAsync(canceledTask).Wait();
                }
                m_CanceledTasks.Clear();

                m_LastFinalizedModelTask = null;
                m_LastBuildModelTask = null;
            }

            base.Dispose(disposing);
        }

        static async Task DisposeTaskAsync(Task task)
        {
            if (task != null)
            {
                try
                {
                    AsyncTaskHelper.DebugLogAsyncStep($"Async Building - DisposeTaskAsync  - task status: {task.Status}  -   {typeof(TModel)}");

                    if (task.Status > TaskStatus.WaitingForActivation)
                        await task.ConfigureAwait(false);
                    else
                    {
                        // the task is canceled so there is no risk of it triggering a crash,
                        // but littering zombie tasks around isn't ideal...
#if DEBUG_VALIDATION
                        Debug.LogWarning("Zombie Task detected.");
#endif
                    }
                }
                catch (OperationCanceledException)
                {
                    // meh, expected. Ignore.
                    AsyncTaskHelper.DebugLogAsyncStep($"Async Building canceled - DisposeTaskAsync                 {typeof(TModel)}");
                }
                catch (Exception e)
                {
                    // Log the exception but finish up without rethrowing
                    Debug.LogException(e);
                }
                finally
                {
                    if (task.IsFaulted)
                        task.Dispose();
                }
            }
            await Task.CompletedTask;
        }
    }
}

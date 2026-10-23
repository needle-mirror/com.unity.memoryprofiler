using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Coordinates asynchronous model building and sorting for TreeViewController.
    /// Manages task lifecycle, cancellation tokens, and cleanup of canceled tasks.
    /// This component extracts the complex async orchestration logic from TreeViewController
    /// to make it more testable and maintainable.
    /// </summary>
    class AsyncModelBuildCoordinator<TModel> where TModel : class
    {
        Task<TModel> m_LastBuildModelTask;
        Task<TModel> m_LastSortTask;
        Task<TModel> m_LastFinalizedModelTask;
        List<Task<TModel>> m_CanceledTasks = new List<Task<TModel>>();
        CancellationTokenSource m_BuildModelCTS;
        Func<Task<TModel>, TModel> m_PendingModelSorting;
        CancellationTokenSource m_BuildModelSortingCTS;
        /// <summary>
        /// Incremented by every <see cref="BuildModelAsync"/> call, so that one which has since been superseded
        /// can tell that the loading overlay it put up now belongs to a newer build and must be left alone.
        /// </summary>
        int m_LatestBuildGeneration;
        int m_SettledBuildGeneration;

        /// <summary>
        /// Gets whether a model is currently being built or if a completed model exists.
        /// </summary>
        public bool HasModelOrIsBuildingOne => m_LastBuildModelTask != null;

        /// <summary>Whether the latest requested build has yet to be applied, fail or be canceled.</summary>
        public bool IsBuilding => Volatile.Read(ref m_SettledBuildGeneration) != Volatile.Read(ref m_LatestBuildGeneration);

        /// <summary>
        /// Gets the current finalized model, or null if no model has been successfully built.
        /// </summary>
        public TModel CurrentModel =>
            (m_LastFinalizedModelTask?.IsCompletedSuccessfully ?? false)
                ? m_LastFinalizedModelTask.Result
                : default;

        /// <summary>
        /// Builds a model asynchronously with proper cancellation and cleanup handling.
        /// A call that has been superseded by a later one owns nothing from that point on: it must neither apply
        /// its model nor hide the loading overlay, which by then belongs to the build that replaced it.
        /// </summary>
        /// <param name="getBuilderTask">Function to get the builder task</param>
        /// <param name="getSorterTask">Function to get the sorter task</param>
        /// <param name="onModelRebuild">Callback when model is rebuilt (success/failure)</param>
        /// <param name="onViewReloaded">Callback when view should be reloaded</param>
        /// <param name="showLoading">Action to show loading UI</param>
        /// <param name="hideLoading">Action to hide loading UI. Invoked only while this build is still the latest one.</param>
        /// <param name="justSort">If true, only sort existing model without rebuilding</param>
        /// <returns>The built and sorted model, or null if canceled/failed</returns>
        public async Task<TModel> BuildModelAsync(
            Func<CancellationToken, Func<TModel>> getBuilderTask,
            Func<CancellationToken, Func<Task<TModel>, TModel>> getSorterTask,
            Action<TModel, bool, bool, Exception> onModelRebuild,
            Action<bool> onViewReloaded,
            Action showLoading,
            Action hideLoading,
            bool justSort = false)
        {
            // Show loading UI. A superseded build must not take it back down: the newer build that replaced it
            // is still running behind it, and hiding it early exposes a half-built view to interaction until
            // that build lands.
            var buildGeneration = Interlocked.Increment(ref m_LatestBuildGeneration);
            showLoading();

            void HideLoadingUnlessSuperseded()
            {
                if (buildGeneration == Volatile.Read(ref m_LatestBuildGeneration))
                    hideLoading();
            }

            void SettleUnlessSuperseded()
            {
                if (buildGeneration == Volatile.Read(ref m_LatestBuildGeneration))
                    Volatile.Write(ref m_SettledBuildGeneration, buildGeneration);
            }

            // Cleanup previously canceled builds
            CleanupCanceledTasks();

            // Grab the main thread scheduler before any 'await' can move this method off the main thread
            var mainThreadScheduler = TaskScheduler.FromCurrentSynchronizationContext();
            CancellationTokenSource buildCTS = null;
            var modelHasNewTreeViewIds = false;

            var modelTypeName = typeof(TModel).ToString();

            AsyncTaskHelper.DebugLogAsyncStep("Async Building setup                 " + modelTypeName);

            try
            {
                // Setup builder task
                if (!justSort || !HasModelOrIsBuildingOne)
                {
                    // If we don't just sort or have no previous model builder task to continue on, we need to do a full (re)build
                    buildCTS = SetupBuilderTask(getBuilderTask, modelTypeName);
                    modelHasNewTreeViewIds = true;
                }
                else
                {
                    AsyncTaskHelper.DebugLogAsyncStep("Async Building setup - Builder Reuse                 " + modelTypeName);
                    // The builder might still be around, but the CancellationTokenSource might have been disposed and nulled.
                    // If so, create a new one so that the rest of the code behaves consistently.
                    buildCTS = m_BuildModelCTS ??= new CancellationTokenSource();
                }

                // Setup sorter task
                var sortCTS = SetupSorterTask(getSorterTask, buildCTS, modelTypeName);

                // Define UI integration function
                TModel MainThreadUIIntegrationFunc(Task<TModel> t)
                {
                    sortCTS.Token.ThrowIfCancellationRequested();
                    AsyncTaskHelper.DebugLogAsyncStep("Apply Model                 " + modelTypeName);

                    // Apply the model
                    var model = t.Result;
                    m_LastFinalizedModelTask = Task.FromResult(model);

                    var success = model != null;
                    SettleUnlessSuperseded();
                    onModelRebuild(model, success, modelHasNewTreeViewIds, null);

                    // Hide loading UI
                    HideLoadingUnlessSuperseded();

                    // Notify responder
                    onViewReloaded(success);

                    AsyncTaskHelper.DebugLogAsyncStep("Model Applied                 " + modelTypeName);
                    return model;
                }

                // Execute the build pipeline
                return await ExecuteBuildPipeline(
                    buildCTS,
                    sortCTS,
                    MainThreadUIIntegrationFunc,
                    mainThreadScheduler,
                    modelTypeName);
            }
            catch (OperationCanceledException)
            {
                AsyncTaskHelper.DebugLogAsyncStep("Async Building - Canceled                 " + modelTypeName);
                SettleUnlessSuperseded();
                HideLoadingUnlessSuperseded();
                return null;
            }
            catch (Exception e)
            {
                // Update the UI even if an exception happens
                try
                {
                    SettleUnlessSuperseded();
                    onModelRebuild(null, false, modelHasNewTreeViewIds, e);
                    onViewReloaded(false);
                    HideLoadingUnlessSuperseded();
                }
                catch (OperationCanceledException) { }
                Debug.LogException(e);
                return null;
            }
            finally
            {
                CleanupAfterBuild(buildCTS, modelTypeName);
            }
        }

        /// <summary>
        /// Cancels any in-progress model building or sorting.
        /// </summary>
        public void Cancel()
        {
            m_BuildModelCTS?.Cancel();
            m_BuildModelSortingCTS?.Cancel();
        }

        /// <summary>
        /// Disposes all managed resources and cancels any in-progress operations.
        /// Blocks synchronously until all in-flight background work (the build task and the sort task)
        /// has stopped. This is required because those steps run on background threads and process model
        /// data that references CachedSnapshot memory (DynamicArrays), which will be freed by the caller
        /// once Dispose returns. Making this async would allow the snapshot to be freed while a background
        /// step is still reading its data, causing use-after-free.
        /// The UI-integration continuation runs on the main thread and is intentionally NOT awaited here:
        /// it cannot execute while Dispose holds the main thread (and is canceled via the sort token), so
        /// awaiting it synchronously would deadlock.
        /// </summary>
        public void Dispose()
        {
            Cancel();
            m_BuildModelCTS = null;
            m_BuildModelSortingCTS = null;

            DisposeTaskAsync(m_LastBuildModelTask).Wait();

            // The sort task is a background-thread continuation of the build task (TaskScheduler.Default).
            // It can still be running model.Sort() after the build completed, so wait for it as well.
            if (m_LastSortTask != m_LastBuildModelTask)
                DisposeTaskAsync(m_LastSortTask).Wait();

            if (m_LastFinalizedModelTask != m_LastBuildModelTask && m_LastFinalizedModelTask != m_LastSortTask)
                DisposeTaskAsync(m_LastFinalizedModelTask).Wait();

            foreach (var canceledTask in m_CanceledTasks)
            {
                if (!canceledTask.IsCompleted)
                    DisposeTaskAsync(canceledTask).Wait();
            }
            // The waits above leave every canceled task completed, so this also releases the models any of them
            // produced after being superseded. Without it those models, and their native stores, would outlive
            // this coordinator with nothing left holding a reference to them.
            CleanupCanceledTasks();
            m_CanceledTasks.Clear();

            m_LastFinalizedModelTask = null;
            m_LastBuildModelTask = null;
            m_LastSortTask = null;
        }

        void CleanupCanceledTasks()
        {
            for (var i = m_CanceledTasks.Count - 1; i >= 0; i--)
            {
                var task = m_CanceledTasks[i];
                if (!task.IsCompleted) continue;

                // A build superseded before it finished (its CTS canceled) can still have run to completion on a
                // background thread and produced a real model holding unmanaged resources, such as
                // RootsAndImpactModel's native row source. That model was never applied to the view, so nothing
                // else will dispose it. The CurrentModel check covers a justSort, which re-uses the same task
                // instance as m_LastFinalizedModelTask.
                if (task.Status == TaskStatus.RanToCompletion && task.Result is IDisposable disposableModel
                    && !ReferenceEquals(task.Result, CurrentModel))
                {
                    disposableModel.Dispose();
                }

                task.Dispose();
                m_CanceledTasks.RemoveAt(i);
            }
        }

        CancellationTokenSource SetupBuilderTask(
            Func<CancellationToken, Func<TModel>> getBuilderTask,
            string modelTypeName)
        {
            // Cancel previous build, if any. This will also cancel the current sort task.
            // Track both still-running tasks so Dispose can wait for them to stop touching model data.
            if (m_LastBuildModelTask is { IsCompleted: false })
                m_CanceledTasks.Add(m_LastBuildModelTask);
            if (m_LastSortTask is { IsCompleted: false })
                m_CanceledTasks.Add(m_LastSortTask);

            m_BuildModelCTS?.Cancel();
            var buildCTS = m_BuildModelCTS = new CancellationTokenSource();

            AsyncTaskHelper.DebugLogAsyncStep("Async Building setup - Builder                 " + modelTypeName);

            // Get the task on main thread as some builder configs come from EditorPrefs, which is main thread accessible only
            var buildModelChildTask = getBuilderTask(buildCTS.Token);

            m_LastBuildModelTask = Task.Run(() =>
            {
                try
                {
                    buildCTS.Token.ThrowIfCancellationRequested();
                    // Run actual task on the threaded task's thread
                    return buildModelChildTask();
                }
                catch (OperationCanceledException)
                {
                    // Cancellation is expected. Log it though.
                    AsyncTaskHelper.DebugLogAsyncStep("Building Canceled                 " + modelTypeName);
                    // Continuation tasks check for cancellation and against null models, but critically,
                    // the UI needs to get updated (by the last task in the queue) no matter the exception.
                    return null;
                }
                // Any other exception (e.g. UnsupportedSnapshotVersionException) is intentionally left to fault the
                // task so it propagates through the awaited build task in ExecuteBuildPipeline up to BuildModelAsync's
                // outer catch, which forwards it to onModelRebuild (to show its message in the UI) and logs it.
            }, buildCTS.Token);

            return buildCTS;
        }

        CancellationTokenSource SetupSorterTask(
            Func<CancellationToken, Func<Task<TModel>, TModel>> getSorterTask,
            CancellationTokenSource buildCTS,
            string modelTypeName)
        {
            // Cancel pending sort operations before creating new sort cancellation token source
            m_BuildModelSortingCTS?.Cancel();
            var sortCTS = m_BuildModelSortingCTS = new CancellationTokenSource();

            // Store current sorting state as sorting delegate before awaiting the builder task,
            // which frees the main thread to trigger a new sorting task while the model is still building
            m_PendingModelSorting = getSorterTask(sortCTS.Token);

            buildCTS.Token.Register(() =>
            {
                // Cancel whatever sort operation would have followed this build, which may not be the one that was started with it
                // Therefore use the CTS stored on instance fields rather than method local fields
                if (m_BuildModelSortingCTS != null && !m_BuildModelSortingCTS.IsCancellationRequested)
                    m_BuildModelSortingCTS.Cancel();
                m_BuildModelSortingCTS = null;
                AsyncTaskHelper.DebugLogAsyncStep("buildCTS canceled Sorting                 " + modelTypeName);
            });

            return sortCTS;
        }

        async Task<TModel> ExecuteBuildPipeline(
            CancellationTokenSource buildCTS,
            CancellationTokenSource sortCTS,
            Func<Task<TModel>, TModel> mainThreadUIIntegrationFunc,
            TaskScheduler mainThreadScheduler,
            string modelTypeName)
        {
            // Wait for the model to finish building to avoid scheduling a continuation to a task that might've been canceled
            // Use ConfigureAwait(false) to avoid deadlocks by not capturing the synchronization context
            await m_LastBuildModelTask.ConfigureAwait(false);

            AsyncTaskHelper.DebugLogAsyncStep("Async Building setup - Sorting                 " + modelTypeName);
            var sortTaskFunc = m_PendingModelSorting;
            buildCTS.Token.ThrowIfCancellationRequested();
            sortCTS.Token.ThrowIfCancellationRequested();

            var sortTask = m_LastSortTask = m_LastBuildModelTask.ContinueWith(
                t => sortTaskFunc(t),
                sortCTS.Token,
                // Option None instead of OnlyOnRanToCompletion because with the latter it's unclear if cancellation ripples through and all tasks will end
                // Since Builder task cancels sort task, only running to completion is implied.
                // None also means this task will run Async, i.e. off the main thread, unless given the mainThreadScheduler (see mainThreadUIIntegrationTask)
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);

            AsyncTaskHelper.DebugLogAsyncStep("Async Building setup - Integration                 " + modelTypeName);
            var mainThreadUIIntegrationTask = sortTask.ContinueWith(
                mainThreadUIIntegrationFunc,
                sortCTS.Token,
                TaskContinuationOptions.None,
                mainThreadScheduler);

            AsyncTaskHelper.DebugLogAsyncStep("Async Building - Await                 " + modelTypeName);
            var result = await mainThreadUIIntegrationTask.ConfigureAwait(false);

            AsyncTaskHelper.DebugLogAsyncStep("Async Building - Await Finished                 " + modelTypeName);
            return result;
        }

        void CleanupAfterBuild(CancellationTokenSource buildCTS, string modelTypeName)
        {
            // Cleanup:
            // There can only ever be one overall model builder & Sort combo that will run to completion at a time
            // but while a new one might have been started and set as m_BuildModelTask,
            // an old one could be canceled and still run to the end.
            if (buildCTS != null && !buildCTS.IsCancellationRequested)
            {
                // Only clean up the instance fields if this is the running main task and it was not canceled
                m_BuildModelCTS = null;
                m_BuildModelSortingCTS = null;
            }

            buildCTS?.Dispose();

            AsyncTaskHelper.DebugLogAsyncStep("Async Building - Finally                 " + modelTypeName);
            AsyncTaskHelper.DebugLogAsyncStep("Async Building - Done                 " + modelTypeName);
        }

        static async Task DisposeTaskAsync(Task task)
        {
            if (task != null)
            {
                try
                {
                    AsyncTaskHelper.DebugLogAsyncStep($"Async Building - DisposeTaskAsync  - task status: {task.Status}");

                    if (task.Status > TaskStatus.WaitingForActivation)
                        await task.ConfigureAwait(false);
                    else
                    {
                        // The task is canceled so there is no risk of it triggering a crash,
                        // but littering zombie tasks around isn't ideal...
#if DEBUG_VALIDATION
                        Debug.LogWarning("Zombie Task detected.");
#endif
                    }
                }
                catch (OperationCanceledException)
                {
                    // Meh, expected. Ignore.
                    AsyncTaskHelper.DebugLogAsyncStep("Async Building canceled - DisposeTaskAsync");
                }
                catch (Exception)
                {
                    // The build pipeline already surfaced and logged this exception when it first awaited the
                    // task (see BuildModelAsync's catch, which also routes it to the view's error label).
                    // Awaiting it again here only observes the now-faulted task so it can't resurface as an
                    // unobserved task exception — it must NOT be logged a second time.
                    AsyncTaskHelper.DebugLogAsyncStep("Async Building - DisposeTaskAsync observed an already-logged faulted task");
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

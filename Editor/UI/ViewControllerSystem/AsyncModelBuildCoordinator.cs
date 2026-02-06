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
        Task<TModel> m_LastFinalizedModelTask;
        List<Task<TModel>> m_CanceledTasks = new List<Task<TModel>>();
        CancellationTokenSource m_BuildModelCTS;
        Func<Task<TModel>, TModel> m_PendingModelSorting;
        CancellationTokenSource m_BuildModelSortingCTS;

        /// <summary>
        /// Gets whether a model is currently being built or if a completed model exists.
        /// </summary>
        public bool HasModelOrIsBuildingOne => m_LastBuildModelTask != null;

        /// <summary>
        /// Gets the current finalized model, or null if no model has been successfully built.
        /// </summary>
        public TModel CurrentModel =>
            (m_LastFinalizedModelTask?.IsCompletedSuccessfully ?? false)
                ? m_LastFinalizedModelTask.Result
                : default;

        /// <summary>
        /// Builds a model asynchronously with proper cancellation and cleanup handling.
        /// </summary>
        /// <param name="getBuilderTask">Function to get the builder task</param>
        /// <param name="getSorterTask">Function to get the sorter task</param>
        /// <param name="onModelRebuild">Callback when model is rebuilt (success/failure)</param>
        /// <param name="onViewReloaded">Callback when view should be reloaded</param>
        /// <param name="showLoading">Action to show loading UI</param>
        /// <param name="hideLoading">Action to hide loading UI</param>
        /// <param name="justSort">If true, only sort existing model without rebuilding</param>
        /// <returns>The built and sorted model, or null if canceled/failed</returns>
        public async Task<TModel> BuildModelAsync(
            Func<CancellationToken, Func<TModel>> getBuilderTask,
            Func<CancellationToken, Func<Task<TModel>, TModel>> getSorterTask,
            Action<TModel, bool> onModelRebuild,
            Action<bool> onViewReloaded,
            Action showLoading,
            Action hideLoading,
            bool justSort = false)
        {
            // Show loading UI
            showLoading();

            // Cleanup previously canceled builds
            CleanupCanceledTasks();

            // Grab the main thread scheduler before any 'await' can move this method off the main thread
            var mainThreadScheduler = TaskScheduler.FromCurrentSynchronizationContext();
            CancellationTokenSource buildCTS = null;

            var modelTypeName = typeof(TModel).ToString();

            AsyncTaskHelper.DebugLogAsyncStep("Async Building setup                 " + modelTypeName);

            try
            {
                // Setup builder task
                if (!justSort || !HasModelOrIsBuildingOne)
                {
                    // If we don't just sort or have no previous model builder task to continue on, we need to do a full (re)build
                    buildCTS = SetupBuilderTask(getBuilderTask, modelTypeName);
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
                    onModelRebuild(model, success);

                    // Hide loading UI
                    hideLoading();

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
                hideLoading();
                return null;
            }
            catch (Exception e)
            {
                // Update the UI even if an exception happens
                try
                {
                    onModelRebuild(null, false);
                    hideLoading();
                    onViewReloaded(false);
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
        /// </summary>
        public void Dispose()
        {
            Cancel();
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

        void CleanupCanceledTasks()
        {
            for (var i = m_CanceledTasks.Count - 1; i >= 0; i--)
            {
                if (!m_CanceledTasks[i].IsCompleted) continue;
                m_CanceledTasks[i].Dispose();
                m_CanceledTasks.RemoveAt(i);
            }
        }

        CancellationTokenSource SetupBuilderTask(
            Func<CancellationToken, Func<TModel>> getBuilderTask,
            string modelTypeName)
        {
            // Cancel previous build, if any. This will also cancel the current sort task
            if (m_LastBuildModelTask is { IsCompleted: false })
                m_CanceledTasks.Add(m_LastBuildModelTask);

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

            var sortTask = m_LastBuildModelTask.ContinueWith(
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

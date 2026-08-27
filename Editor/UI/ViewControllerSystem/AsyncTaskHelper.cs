// Never ship without commenting out this define
// #define DEBUG_ASYNC_TASK_WORKER // needs to be set in PlayerSettings
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI
{
    static class AsyncTaskHelper
    {
        /// <summary>
        /// Helper method to conditionally log out messages about async tasks if e.g. the order of operations needs debugging.
        /// Conditionally compiled out in release.
        /// </summary>
        [Conditional("DEBUG_ASYNC_TASK_WORKER"), MethodImpl(MethodImplementationHelper.AggressiveInlining), HideInCallstack()]
        public static void DebugLogAsyncStep(string text)
        {
            UnityEngine.Debug.Log(text);
        }

        /// <summary>
        /// Asserts that a task completed synchronously and successfully. Call this instead of discarding a
        /// task with <c>_ =</c> when the task is expected to be synchronous (e.g. methods that are no-ops
        /// before their view is loaded). Throws immediately — without blocking — if the task did not
        /// complete, faulted, or was canceled. This ensures that if the method is ever made truly async or
        /// starts failing, the bug is caught early rather than silently dropped.
        /// </summary>
        public static void AssertCompleted(this Task task)
        {
            if (task.Status == TaskStatus.RanToCompletion)
                return;
            if (task.IsCompleted)
                task.GetAwaiter().GetResult(); // rethrows the fault or cancellation
            else
                throw new InvalidOperationException(
                    $"Expected task to complete synchronously, but it is in state '{task.Status}'. " +
                    "If the underlying method was made async, callers must be updated to await it.");
        }
    }
}

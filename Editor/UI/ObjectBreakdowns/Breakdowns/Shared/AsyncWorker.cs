using System;
using System.Collections;
using System.Threading;
using Unity.EditorCoroutines.Editor;

namespace Unity.MemoryProfiler.Editor.UI
{
    class AsyncWorker<T> : IDisposable
    {
        Func<T> m_Execution;
        Action<T> m_Completion;
        T m_Result;
        bool m_Completed;
        Thread m_Thread;
        bool m_Disposed;

        public void Execute(Func<T> execution, Action<T> completion)
        {
            if (m_Thread != null)
                throw new InvalidOperationException("Async worker is already executing.");

            m_Execution = execution;
            if (m_Execution == null)
                throw new ArgumentNullException(nameof(execution));

            m_Completion = completion;
            if (m_Completion == null)
                throw new ArgumentNullException(nameof(completion));

            // Start a coroutine to invoke the completion handler on the main thread when the work is completed.
            EditorCoroutineUtility.StartCoroutine(WaitForExecutionCompletion(), this);

            // Begin the work on a new thread.
            m_Thread = new Thread(WorkerThreadStart);
            m_Thread.Start();
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (m_Disposed)
                return;

            if (disposing)
                m_Thread?.Abort();

            m_Disposed = true;
        }

        void WorkerThreadStart()
        {
            m_Result = m_Execution();
            m_Completed = true;
        }

        IEnumerator WaitForExecutionCompletion()
        {
            while (!m_Completed)
                yield return null;

            m_Completion?.Invoke(m_Result);
        }
    }
}

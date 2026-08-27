using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.IO.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Diagnostics;

namespace Unity.MemoryProfiler.Editor.Format.QueriedSnapshot
{
    [StructLayout(LayoutKind.Sequential)]
    struct ReadOperation : IDisposable
    {
        public JobHandle JobHandle => m_Handle.JobHandle;
        ReadHandle m_Handle;
        ReadError m_Err;
        DynamicArray<byte> m_Buffer;
        internal ReadOperation(ReadHandle handle, DynamicArray<byte> buffer)
        {
            m_Err = ReadError.InProgress;
            m_Handle = handle;
            m_Buffer = buffer;
        }

        public ReadError Error
        {
            get
            {
                if (m_Err == ReadError.InProgress && m_Handle.IsValid() && m_Handle.Status != ReadStatus.InProgress)
                {
                    switch (m_Handle.Status)
                    {
                        case ReadStatus.Truncated:
                        case ReadStatus.Failed:
                            m_Err = ReadError.FileReadFailed;
                            break;
                        case ReadStatus.Complete:
                            m_Err = ReadError.Success;
                            break;
                        case ReadStatus.Canceled:
                            m_Err = ReadError.ReadingAborted;
                            break;
                    }
                }

                return m_Err;
            }
            internal set => m_Err = value;
        }

        public DynamicArray<byte> Result
        {
            get
            {
                Checks.CheckEquals(true, IsDone);
                Checks.CheckEqualsEnum(ReadError.Success, Error);
                return m_Buffer;
            }
        }

        public void Complete()
        {
            if (!m_Handle.IsValid())
                return;

            m_Handle.JobHandle.Complete();
            // Update Error state
            m_Err = Error;
            m_Handle.Dispose();
            m_Handle = default;
        }

        public bool IsDone { get { return m_Handle.IsValid() ? m_Handle.JobHandle.IsCompleted : true; } }
        public bool IsValid { get { return m_Handle.IsValid(); } }

        public void Dispose()
        {
            if (!IsValid)
                return;

            // Update Error state
            m_Err = Error;
            if (m_Err == ReadError.InProgress)
            {
                m_Err = ReadError.ReadingAborted;
                m_Handle.Cancel();
            }

            m_Handle.Dispose();
            m_Handle = default;
        }
    }

    interface IGenericReadOperation : IDisposable
    {
        ReadError Error { get; }
        DynamicArray<byte> Result { get; }
        void Complete();
        public JobHandle JobHandle { get; }
        bool IsDone { get; }
        bool IsValid { get; }
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct GenericReadOperation : IGenericReadOperation, IDisposable
    {
        ReadOperation ReadOperation;
        internal GenericReadOperation(ReadOperation readOperation)
        {
            ReadOperation = readOperation;
        }
        internal GenericReadOperation(ReadHandle handle, DynamicArray<byte> buffer) : this(new ReadOperation(handle, buffer)) { }

        public ReadError Error { get => ReadOperation.Error; internal set => ReadOperation.Error = value; }

        public DynamicArray<byte> Result => ReadOperation.Result;

        public void Complete() => ReadOperation.Complete();
        public JobHandle JobHandle => ReadOperation.JobHandle;

        public bool IsDone => ReadOperation.IsDone;
        public bool IsValid => ReadOperation.IsValid;

        public void Dispose() => ReadOperation.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct NestedDynamicSizedArrayReadOperation<T> : IGenericReadOperation where T : unmanaged
    {
        BurstableBool m_DoneReading;
        BurstableBool m_AbortedReading;
        NestedDynamicArray<T> m_NestedArrayStructure;
        NativeArray<GenericReadOperation> m_ReadOperations;

        /// <summary>
        /// The count of the nested array at the given index, which is readable even before the async read operation is done.
        /// </summary>
        /// <param name="idx"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public readonly long Count(long idx) => m_NestedArrayStructure.Count(idx);

        /// <summary>
        /// Sorting the nested array structure based on the index remapping is allowed even before the async read operation is done.
        /// </summary>
        /// <param name="indexRemapping"></param>
        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public readonly void Sort(DynamicArray<long> indexRemapping) => m_NestedArrayStructure.Sort(indexRemapping);

        public ReadError Error
        {
            get
            {
                if (m_AbortedReading == BurstableBool.TRUE)
                    return ReadError.ReadingAborted;
                var oneSuccess = false;
                var oneNonSuccess = false;
                if (m_DoneReading == BurstableBool.FALSE)
                {
                    foreach (var readOp in m_ReadOperations)
                    {
                        if (readOp.Error != ReadError.None)
                        {
                            if (readOp.Error == ReadError.Success)
                                oneSuccess = true;
                            else
                                return readOp.Error;
                        }
                        else
                            oneNonSuccess = true;
                    }
                }
                return oneSuccess && !oneNonSuccess ? ReadError.Success : ReadError.None;
            }
        }

        public DynamicArray<byte> Result => throw new NotImplementedException();

        public bool IsDone
        {
            get
            {
                if (!IsCreated || m_DoneReading == BurstableBool.TRUE)
                    return true;
                if (!JobHandle.IsCompleted)
                    return false;
                if (IsCreated)
                    // finalize the reading ops
                    Complete(false);
                return true;
            }
        }

        public bool IsValid => m_NestedArrayStructure.IsCreated && m_ReadOperations.IsCreated && m_ReadOperations.Length > 0;

        public bool IsCreated => m_NestedArrayStructure.IsCreated;

        public JobHandle JobHandle { get; }

        internal NestedDynamicSizedArrayReadOperation(ref NativeArray<GenericReadOperation> genericReadOperations, NestedDynamicArray<T> nestedArrayStructure)
        {
            m_ReadOperations = genericReadOperations;
            switch (genericReadOperations.Length)
            {
                case 0:
                    JobHandle = default;
                    break;
                case 1:
                    JobHandle = m_ReadOperations[0].IsValid ? genericReadOperations[0].JobHandle : default;
                    break;
                default:
                {
                    var handles = new NativeArray<JobHandle>(genericReadOperations.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    for (int i = 0; i < m_ReadOperations.Length; i++)
                    {
                        handles[i] = m_ReadOperations[i].IsValid ? m_ReadOperations[i].JobHandle : default;
                    }
                    JobHandle = JobHandle.CombineDependencies(handles);
                    handles.Dispose();
                    break;
                }
            }
            m_NestedArrayStructure = nestedArrayStructure;
            m_DoneReading = BurstableBool.FALSE;
            m_AbortedReading = BurstableBool.FALSE;
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        internal NestedDynamicArray<T> CompleteReadAndGetNestedResults()
        {
            Complete(false);
            return m_NestedArrayStructure;
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public void Complete() => Complete(false);

        void Complete(bool cancel)
        {
            if (m_DoneReading == BurstableBool.FALSE)
            {
                if (IsCreated)
                {
                    if (cancel)
                    {
                        for (int i = 0; i < m_ReadOperations.Length; i++)
                        {
                            var readOp = m_ReadOperations[i];
                            readOp.Dispose();
                            m_ReadOperations[i] = readOp;
                        }
                        // Cancelation only happens from within Dispose, so don't call it again
                    }
                    else
                    {
                        JobHandle.Complete();
                        for (int i = 0; i < m_ReadOperations.Length; i++)
                        {
                            var readOp = m_ReadOperations[i];
                            readOp.Complete();
                            m_ReadOperations[i] = readOp;
                            Checks.CheckEquals(true, readOp.Result.IsCreated);
                        }
                        Dispose();
                    }
                }
            }
        }

        public void Dispose()
        {
            if (m_DoneReading == BurstableBool.TRUE)
                return;
            var aborted = false;

            if (!JobHandle.IsCompleted)
            {
                aborted = true;
                Complete(cancel: true);
            }

            if (m_ReadOperations.IsCreated)
            {
                for (int i = 0; i < m_ReadOperations.Length; i++)
                {
                    var readOp = m_ReadOperations[i];
                    if (!readOp.IsDone)
                        aborted = true;
                    if (readOp.IsValid)
                        readOp.Dispose();
                    m_ReadOperations[i] = readOp;
                }
                m_ReadOperations.Dispose();
            }
            else
            {
                aborted = true;
            }
            // While reading hasn't necessarily finished succesfully, it is no longer happening.
            m_DoneReading = BurstableBool.TRUE;
            if (aborted)
                // Error State should be ReadingAborted
                m_AbortedReading = BurstableBool.TRUE;
        }
    }
}

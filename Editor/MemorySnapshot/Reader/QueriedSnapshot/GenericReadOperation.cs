using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IO.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Diagnostics;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.Format.QueriedSnapshot
{
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
                        case ReadStatus.Failed:
                            m_Err = ReadError.FileReadFailed;
                            break;
                        case ReadStatus.Complete:
                            m_Err = ReadError.Success;
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
            m_Handle = new ReadHandle();
        }

        public bool IsDone { get { return m_Handle.IsValid() ? m_Handle.JobHandle.IsCompleted : true; } }

        public void Dispose()
        {
            if (!m_Handle.IsValid())
                return;

            // Update Error state
            m_Err = Error;
            if (m_Err == ReadError.InProgress)
                m_Err = ReadError.ReadingAborted;
            m_Handle.Dispose();
            m_Handle = new ReadHandle();
        }
    }

    interface IGenericReadOperation : IDisposable
    {
        ReadError Error { get; }
        DynamicArray<byte> Result { get; }
        void Complete();
        bool IsDone { get; }
    }

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

        public bool IsDone => ReadOperation.IsDone;

        public void Dispose() => ReadOperation.Dispose();
    }

    unsafe struct NestedDynamicSizedArrayReadOperation<T> : IGenericReadOperation where T : unmanaged
    {
        bool m_DoneReading;
        NestedDynamicArray<T> m_NestedArrayStructure;
        NativeArray<GenericReadOperation> m_ReadOperations;

        /// <summary>
        /// The count of the nested array at the given index, which is readable even before the async read operation is done.
        /// </summary>
        /// <param name="idx"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly long Count(long idx) => m_NestedArrayStructure.Count(idx);

        /// <summary>
        /// Sorting the nested array structure based on the index remapping is allowed even before the async read operation is done.
        /// </summary>
        /// <param name="indexRemapping"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void Sort(DynamicArray<long> indexRemapping) => m_NestedArrayStructure.Sort(indexRemapping);

        public ReadError Error
        {
            get
            {
                var oneSuccess = false;
                var oneNonSuccess = false;
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
                if (oneNonSuccess)
                    oneNonSuccess = false;
                return oneSuccess ? ReadError.Success : ReadError.None;
            }
        }

        public DynamicArray<byte> Result => throw new NotImplementedException();

        public bool IsDone
        {
            get
            {
                foreach (var readOp in m_ReadOperations)
                {
                    if (!readOp.IsDone)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        public bool IsCreated => m_NestedArrayStructure.IsCreated;

        internal NestedDynamicSizedArrayReadOperation(List<GenericReadOperation> genericReadOperations, NestedDynamicArray<T> nestedArrayStructure, Allocator allocator)
        {
            m_ReadOperations = new NativeArray<GenericReadOperation>(genericReadOperations.ToArray(), allocator);
            m_NestedArrayStructure = nestedArrayStructure;
            m_DoneReading = false;
        }

        internal NestedDynamicArray<T> CompleteReadAndGetNestedResults()
        {
            if (!m_DoneReading)
            {
                if (!IsDone && IsCreated)
                {
                    foreach (var readOp in m_ReadOperations)
                    {
                        readOp.Complete();
                        Checks.CheckEquals(true, readOp.Result.IsCreated);
                        readOp.Dispose();
                    }
                }
                m_DoneReading = true;
            }
            return m_NestedArrayStructure;
        }

        public void Complete() => CompleteReadAndGetNestedResults();

        public void Dispose()
        {
            if (m_DoneReading)
                return;
            foreach (var readOp in m_ReadOperations)
            {
                readOp.Dispose();
            }
            m_ReadOperations.Dispose();
            // While reading hasn't finished succesfully, it is no longer happening.
            // Error State should be ReadingAborted
            m_DoneReading = true;
        }
    }
}

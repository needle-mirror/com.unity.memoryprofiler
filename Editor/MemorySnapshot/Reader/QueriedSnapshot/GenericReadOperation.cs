using System;
using System.Collections;
using System.Collections.Generic;
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
                Checks.CheckEquals(ReadError.Success, Error);
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

        internal bool KeepWaiting { get { return m_Handle.IsValid() && m_Handle.Status == ReadStatus.InProgress; } }

        public void Dispose()
        {
            if (!m_Handle.IsValid())
                return;

            // Update Error state
            m_Err = Error;
            m_Handle.Dispose();
            m_Handle = new ReadHandle();
        }
    }

    interface IGenericReadOperation : IEnumerator, IDisposable
    {
        bool keepWaiting { get; }
        ReadError Error { get; }
        DynamicArray<byte> Result { get; }
        void Complete();
        bool IsDone { get; }
    }

    unsafe class GenericReadOperation : CustomYieldInstruction, IGenericReadOperation
    {
        ReadOperation ReadOperation;
        internal GenericReadOperation(ReadOperation readOperation)
        {
            ReadOperation = readOperation;
        }
        internal GenericReadOperation(ReadHandle handle, DynamicArray<byte> buffer) : this(new ReadOperation(handle, buffer)) { }

        public override bool keepWaiting => ReadOperation.KeepWaiting;

        public ReadError Error { get => ReadOperation.Error; internal set => ReadOperation.Error = value; }

        public DynamicArray<byte> Result => ReadOperation.Result;

        public void Complete() => ReadOperation.Complete();

        public bool IsDone => ReadOperation.IsDone;

        public void Dispose() => ReadOperation.Dispose();
    }

    unsafe class NestedDynamicSizedArrayReadOperation<T> : CustomYieldInstruction, IGenericReadOperation where T : unmanaged
    {
        bool m_DoneReading;
        NestedDynamicArray<T> m_NestedArrayStructure;
        List<GenericReadOperation> m_ReadOperations;
        /// <summary>
        /// Preliminary access to the nested dynamic sized array is safe for sorting the elements (without reading the content) or to get the count.
        /// </summary>
        internal ref NestedDynamicArray<T> UnsafeAccessToNestedDynamicSizedArray { get => ref m_NestedArrayStructure; }

        public ReadError Error
        {
            get
            {
                foreach (var readOp in m_ReadOperations)
                {
                    if (readOp.Error != ReadError.None)
                    {
                        return readOp.Error;
                    }
                }
                return ReadError.None;
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

        public override bool keepWaiting
        {
            get
            {
                foreach (var readOp in m_ReadOperations)
                {
                    if (readOp.keepWaiting)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        internal NestedDynamicSizedArrayReadOperation(List<GenericReadOperation> genericReadOperations, NestedDynamicArray<T> nestedArrayStructure)
        {
            m_ReadOperations = genericReadOperations;
            m_NestedArrayStructure = nestedArrayStructure;
        }

        internal NestedDynamicArray<T> CompleteReadAndGetNestedResults()
        {
            if (!m_DoneReading)
            {
                if (!IsDone)
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
        }
    }
}

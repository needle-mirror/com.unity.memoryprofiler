using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IO.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Diagnostics;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.Format.QueriedSnapshot
{
    public unsafe class GenericReadOperation : CustomYieldInstruction
    {
        ReadHandle m_Handle;
        ReadError m_Err;
        NativeArray<byte> m_Buffer;

        internal GenericReadOperation(ReadHandle handle, NativeArray<byte> buffer)
        {
            m_Err = ReadError.InProgress;
            m_Handle = handle;
            m_Buffer = buffer;
        }

        public override bool keepWaiting { get { return m_Handle.IsValid() && m_Handle.Status == ReadStatus.InProgress; } }

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
            internal set { m_Err = value; }
        }

        public NativeArray<byte> Result
        {
            get
            {
                Checks.CheckEquals(true, m_Handle.IsValid());
                Checks.CheckEquals(true, Error == ReadError.Success);
                return m_Buffer;
            }
        }

        public bool IsDone { get { return m_Handle.JobHandle.IsCompleted; } }

        public void Dispose()
        {
            if (!m_Handle.IsValid())
                return;
            m_Handle.Dispose();
        }
    }
}

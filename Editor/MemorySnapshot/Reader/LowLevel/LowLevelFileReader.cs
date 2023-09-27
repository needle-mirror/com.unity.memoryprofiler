using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.IO.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Diagnostics;

namespace Unity.MemoryProfiler.Editor.Format.LowLevel.IO
{
    enum ReadMode
    {
        Async,
        Blocking
    }

    unsafe struct LowLevelFileReader : IDisposable
    {
        GCHandle m_FilePath;
        public long FileLength { get; private set; }
        public bool IsCreated { get { return m_FilePath.IsAllocated; } }
        public string FilePath { get { return m_FilePath.Target as string; } }

        public LowLevelFileReader(string filePath)
        {
            Checks.CheckFileExistsAndThrow(filePath);

            var fileInfo = new FileInfo(filePath);
            FileLength = fileInfo.Length;
            m_FilePath = GCHandle.Alloc(filePath, GCHandleType.Normal); //readonly no need to pin
        }

        public void Dispose()
        {
            if (!IsCreated)
                return;

            FileLength = 0;
            m_FilePath.Free();
        }

        public ReadHandle Read(ReadCommand* readCmds, uint cmdCount, ReadMode mode = ReadMode.Async)
        {
            var handle = AsyncReadManager.Read(FilePath, readCmds, cmdCount, subsystem: AssetLoadingSubsystem.FileInfo);

            if (mode == ReadMode.Blocking)
                handle.JobHandle.Complete();

            return handle;
        }
    }
}

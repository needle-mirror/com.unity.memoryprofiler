#define ENABLE_LOW_LEVEL_READER_CHECKS
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.IO.LowLevel.Unsafe;

namespace Unity.MemoryProfiler.Editor.Format.LowLevel.IO
{
    static class ReadCommandBufferUtils
    {
        [MethodImpl(256)] //256 is the value of MethodImplOptions.AggresiveInlining
        public unsafe static ReadCommand GetCommand(void* buffer, long readSize, long offset)
        {
            var cmd = new ReadCommand();
            cmd.Buffer = buffer;
            cmd.Size = readSize;
            cmd.Offset = offset;

            return cmd;
        }
    }
}

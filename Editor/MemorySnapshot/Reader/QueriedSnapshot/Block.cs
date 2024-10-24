using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.MemoryProfiler.Editor.Format.QueriedSnapshot
{
    struct BlockHeader
    {
        public readonly ulong ChunkSize;
        public readonly ulong TotalBytes;
    }

    unsafe struct Block : IDisposable
    {
        public readonly BlockHeader Header;
        public readonly uint OffsetCount;
        long* m_Offsets;

        public Block(BlockHeader header)
        {
            Header = header;
            OffsetCount = (uint)((Header.TotalBytes / Header.ChunkSize) + (Header.TotalBytes % Header.ChunkSize != 0UL ? 1UL : 0UL));
            m_Offsets = (long*)UnsafeUtility.Malloc(sizeof(long) * OffsetCount,
                UnsafeUtility.AlignOf<long>(), Allocator.Persistent);
        }

        public void Dispose()
        {
            if (m_Offsets == null)
                return;

            UnsafeUtility.Free(m_Offsets, Allocator.Persistent);
            m_Offsets = null;
        }

        public unsafe long* GetOffsetsPtr() { return m_Offsets; }
    }
}

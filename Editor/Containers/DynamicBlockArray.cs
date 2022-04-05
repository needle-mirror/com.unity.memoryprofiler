using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Diagnostics;

namespace Unity.MemoryProfiler.Editor.Containers
{
    internal static class MathFunc
    {
        public static uint ToNextPow2(uint n)
        {
            --n;
            n |= (n >> 1);
            n |= (n >> 2);
            n |= (n >> 4);
            n |= (n >> 8);
            n |= (n >> 16);
            ++n;
            return n;
        }
    }

    [StructLayout(LayoutKind.Explicit, Pack = 4, Size = 8)]
    unsafe struct MemBlock
    {
        [FieldOffset(0)]
        public void* mem;
    }

    unsafe struct DynamicBlockArray<T> : IDisposable where T : struct
    {
        public const int k_InitialBlockSlots = 8;
        MemBlock* m_BlockList;
        uint m_BlockSlots;
        uint m_UnusedBlockSlots;
        int m_BlockSize;
        uint m_Capacity;

        public uint Count { get; private set; }

        public uint Capacity
        {
            get { return m_Capacity; }
            set
            {
                uint blocks = ComputeBlockCount(value, m_BlockSize);
                uint occupiedBlocks = m_BlockSlots - m_UnusedBlockSlots;
                if (blocks > occupiedBlocks)
                {
                    Grow((int)(blocks - occupiedBlocks));
                }
                else if (blocks < occupiedBlocks)
                {
                    Shrink((int)(occupiedBlocks - blocks));
                }
            }
        }

        public DynamicBlockArray(int blockSize, int initialCapacity)
        {
            Checks.CheckEquals(true, UnsafeUtility.IsBlittable<T>());
            m_BlockSize = blockSize;
            uint preAllocatedBlockCount = ComputeBlockCount((uint)initialCapacity, m_BlockSize);
            m_Capacity = preAllocatedBlockCount * (uint)m_BlockSize;
            Count = 0;
            m_BlockSlots = preAllocatedBlockCount;
            m_UnusedBlockSlots = 0;
            m_BlockList = (MemBlock*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<MemBlock>() * preAllocatedBlockCount, UnsafeUtility.AlignOf<MemBlock>(), Allocator.Persistent);

            while (preAllocatedBlockCount > 0)
            {
                --preAllocatedBlockCount;
                var block = new MemBlock();
                block.mem = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<T>() * m_BlockSize, UnsafeUtility.AlignOf<T>(), Allocator.Persistent);
                *(m_BlockList + preAllocatedBlockCount) = block;
            }
        }

        static uint ComputeBlockCount(uint elementCount, int blockSize)
        {
            return (uint)((elementCount / blockSize) + (elementCount % blockSize != 0 ? 1 : 0));
        }

        public T this[long idx]
        {
            get
            {
                Checks.CheckIndexOutOfBoundsAndThrow(idx, Count);
                return UnsafeUtility.ReadArrayElement<T>(m_BlockList[(idx / m_BlockSize)].mem, (int)(idx % m_BlockSize));
            }
            set
            {
                Checks.CheckIndexOutOfBoundsAndThrow(idx, Count);
                UnsafeUtility.WriteArrayElement(m_BlockList[(idx / m_BlockSize)].mem, (int)(idx % m_BlockSize), value);
            }
        }

        public T Back()
        {
            return this[Count - 1];
        }

        void Grow(int blocks)
        {
            if (m_UnusedBlockSlots < blocks)
            {
                var blockSlotsCount = m_BlockSlots;
                m_BlockSlots = m_BlockSlots * 2 > m_BlockSlots + blocks ?
                    m_BlockSlots * 2 : MathFunc.ToNextPow2(m_BlockSlots + Count);
                var newBlocks = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<MemBlock>() * m_BlockSlots, UnsafeUtility.AlignOf<MemBlock>(), Allocator.Persistent);
                UnsafeUtility.MemCpy(newBlocks, m_BlockList, UnsafeUtility.SizeOf<MemBlock>() * blockSlotsCount);
                UnsafeUtility.Free(m_BlockList, Allocator.Persistent);
                m_BlockList = (MemBlock*)newBlocks;
                m_UnusedBlockSlots = m_BlockSlots - blockSlotsCount;
            }

            for (int i = 0; i < blocks; ++i)
            {
                var mem = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<T>() * m_BlockSize,
                    UnsafeUtility.AlignOf<T>(), Allocator.Persistent);
                m_BlockList[m_BlockSlots - m_UnusedBlockSlots].mem = mem;
                --m_UnusedBlockSlots;
            }
            m_Capacity += (uint)(blocks * m_BlockSize);
        }

        void Shrink(int blocks)
        {
            for (int i = 0; i < blocks; ++i)
            {
                UnsafeUtility.Free(m_BlockList[(m_BlockSlots - 1) - m_UnusedBlockSlots].mem, Allocator.Persistent);
                ++m_UnusedBlockSlots;
            }

            m_Capacity -= (uint)(blocks * m_BlockSize);
            if (Count > m_Capacity)
                Count = m_Capacity;
        }

        public void Push(T value)
        {
            if (Count + 1 >= m_Capacity)
                Grow(1);
            this[Count++] = value;
        }

        public T Pop()
        {
            var ret = this[Count - 1];
            --Count;
            return ret;
        }

        public void Clear(bool stomp)
        {
            if (stomp)
            {
                for (uint i = 0; i < m_BlockSlots; ++i)
                {
                    UnsafeUtility.MemClear(m_BlockList[i].mem, UnsafeUtility.SizeOf<T>() * m_BlockSize);
                }
            }

            Count = 0;
        }

        public void Dispose()
        {
            if (m_BlockList != null)
            {
                int occupiedBlocks = (int)(m_BlockSlots - m_UnusedBlockSlots);
                for (int i = 0; i < occupiedBlocks; ++i)
                {
                    UnsafeUtility.Free((m_BlockList + i)->mem, Allocator.Persistent);
                }
                UnsafeUtility.Free(m_BlockList, Allocator.Persistent);
                m_BlockList = null;
            }
        }
    }
}

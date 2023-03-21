using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;

namespace Unity.MemoryProfiler.Editor.Containers
{
    internal struct NestedDynamicArray<T> : IDisposable where T : unmanaged
    {
        Allocator m_Allocator;
        DynamicArray<DynamicArray<T>> m_NestedArrays;
        DynamicArray<T> m_Data;
        public long SectionCount { get; private set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long Count(long idx)
        {
            Checks.CheckEquals(true, IsCreated);
            return m_NestedArrays[idx].Count;
        }

        public bool IsCreated => m_NestedArrays.IsCreated;

        public NestedDynamicArray(DynamicArray<long> offsets, DynamicArray<T> data)
        {
            m_Allocator = data.Allocator;
            SectionCount = offsets.Count - 1;
            m_NestedArrays = new DynamicArray<DynamicArray<T>>(SectionCount, data.Allocator);

            m_Data = data;
            unsafe
            {
                for (long i = 0; i < SectionCount; i++)
                {
                    var size = offsets[i + 1] - offsets[i];
                    var start = (byte*)m_Data.GetUnsafePtr();
                    m_NestedArrays[i] = DynamicArray<T>.ConvertExistingDataToNativeArray((T*)(start + offsets[i]), size / sizeof(T), Allocator.None);
                }
            }
        }

        public void Sort(DynamicArray<long> indexRemapping)
        {
            Checks.CheckEquals(true, IsCreated);
            if (indexRemapping.Count != SectionCount)
                throw new ArgumentException("the length of passed index list does not match the SectionCount");
            var newBlocks = new DynamicArray<DynamicArray<T>>(m_NestedArrays.Count, m_Allocator);
            for (long i = 0; i < SectionCount; i++)
            {
                newBlocks[i] = m_NestedArrays[indexRemapping[i]];
            }
            m_NestedArrays.Dispose();
            m_NestedArrays = newBlocks;
        }

        public DynamicArray<T> this[long idx]
        {
            get
            {
                Checks.CheckEquals(true, IsCreated);
                Checks.CheckIndexOutOfBoundsAndThrow(idx, SectionCount);
                return m_NestedArrays[idx];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void* GetUnsafePtr(long idx)
        {
            Checks.CheckEquals(true, IsCreated);
            return m_NestedArrays[idx].GetUnsafePtr();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe T* GetUnsafeTypedPtr(long idx)
        {
            Checks.CheckEquals(true, IsCreated);
            return m_NestedArrays[idx].GetUnsafeTypedPtr();
        }


        public void Dispose()
        {
            if (IsCreated)
            {
                m_Data.Dispose();
                m_NestedArrays.Dispose();
            }
        }
    }
}

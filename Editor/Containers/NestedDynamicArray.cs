using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Diagnostics;

namespace Unity.MemoryProfiler.Editor.Containers
{
    internal struct NestedDynamicArray<T> : IDisposable, IEnumerable<DynamicArrayRef<T>> where T : unmanaged
    {
        readonly Allocator m_Allocator;
        DynamicArray<DynamicArrayRef<T>> m_NestedArrays;
        readonly DynamicArray<DynamicArray<T>> m_Data;
        public long SectionCount { readonly get; private set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly long Count(long idx)
        {
            Checks.CheckEquals(true, IsCreated);
            return m_NestedArrays[idx].Count;
        }

        public readonly bool IsCreated => m_NestedArrays.IsCreated;

        public NestedDynamicArray(DynamicArray<long> offsets, DynamicArray<DynamicArray<T>> data)
        {
            m_Allocator = data.Allocator;
            // Allow creation of NestedDynamicArray data with 0 sections
            SectionCount = Math.Max(offsets.Count - 1, 0);
            m_NestedArrays = new DynamicArray<DynamicArrayRef<T>>(SectionCount, data.Allocator);

            m_Data = data;
            if (SectionCount == 0)
            {
                if (data.Count != 0)
                    throw new ArgumentException("Creating a NestedDynamicArray with no sections but with data is not supported, as it hints at faulty inputs.");
                return;
            }
            unsafe
            {
                var dataBlockOffset = 0L;
                var dataBlockIndex = 0L;
                // The offsets are given in bytes, not in T
                var start = (byte*)m_Data[dataBlockIndex].GetUnsafePtr();
                var endOfCurrentDataBlock = m_Data[dataBlockIndex].Count * sizeof(T);
                for (long i = 0; i < SectionCount; i++)
                {
                    var sizeInByte = offsets[i + 1] - offsets[i];
                    // sections are never split across data blocks, so to check if we need to move on to the next block,
                    // we need to check the sections end offset as some sections might have zero length
                    while (offsets[i] + sizeInByte > endOfCurrentDataBlock)
                    {
                        dataBlockOffset = endOfCurrentDataBlock;
                        start = (byte*)m_Data[dataBlockIndex].GetUnsafePtr();
                        endOfCurrentDataBlock += m_Data[dataBlockIndex].Count * sizeof(T);
                    }
                    var offset = offsets[i] - dataBlockOffset;
                    m_NestedArrays[i] = DynamicArrayRef<T>.ConvertExistingDataToDynamicArrayRef((T*)(start + offset), sizeInByte / sizeof(T));
                }
            }
        }

        public void Sort(DynamicArray<long> indexRemapping)
        {
            Checks.CheckEquals(true, IsCreated);
            if (indexRemapping.Count != SectionCount)
                throw new ArgumentException("the length of passed index list does not match the SectionCount");
            var newBlocks = new DynamicArray<DynamicArrayRef<T>>(m_NestedArrays.Count, m_Allocator);
            for (long i = 0; i < SectionCount; i++)
            {
                newBlocks[i] = m_NestedArrays[indexRemapping[i]];
            }
            m_NestedArrays.Dispose();
            m_NestedArrays = newBlocks;
        }

        public readonly DynamicArrayRef<T> this[long idx]
        {
            get
            {
                Checks.CheckEquals(true, IsCreated);
                Checks.CheckIndexOutOfBoundsAndThrow(idx, SectionCount);
                return m_NestedArrays[idx];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly unsafe void* GetUnsafePtr(long idx)
        {
            Checks.CheckEquals(true, IsCreated);
            return m_NestedArrays[idx].GetUnsafePtr();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly unsafe T* GetUnsafeTypedPtr(long idx)
        {
            Checks.CheckEquals(true, IsCreated);
            return m_NestedArrays[idx].GetUnsafeTypedPtr();
        }


        public void Dispose()
        {
            if (IsCreated)
            {
                foreach (var nestedDataBlock in m_Data)
                    nestedDataBlock.Dispose();
                m_Data.Dispose();
                m_NestedArrays.Dispose();
            }
        }

        public readonly IEnumerator<DynamicArrayRef<T>> GetEnumerator() => new NestedEnumerator(this);

        readonly IEnumerator IEnumerable.GetEnumerator() => new NestedEnumerator(this);

        public readonly IEnumerator<T> GetElementEnumerator() => new Enumerator(this);

        struct NestedEnumerator : IEnumerator<DynamicArrayRef<T>>
        {
            public readonly DynamicArrayRef<T> Current => m_NestedArray[m_Index];

            readonly object IEnumerator.Current => m_NestedArray[m_Index];

            NestedDynamicArray<T> m_NestedArray;
            long m_Index;
            public NestedEnumerator(NestedDynamicArray<T> nestedArray)
            {
                m_Index = -1;
                m_NestedArray = nestedArray;
            }

            public void Dispose()
            {
                m_NestedArray = default;
                m_Index = 0;
            }

            public bool MoveNext()
            {
                return ++m_Index < m_NestedArray.SectionCount;
            }

            public void Reset()
            {
                m_Index = -1;
            }
        }

        struct Enumerator : IEnumerator<T>
        {
            public readonly T Current => m_NestedArray[m_ArrayIndex][m_ElementIndex];

            readonly object IEnumerator.Current => m_NestedArray[m_ArrayIndex][m_ElementIndex];

            NestedDynamicArray<T> m_NestedArray;
            long m_ArrayIndex;
            long m_ElementIndex;

            public Enumerator(NestedDynamicArray<T> nestedArray)
            {
                m_ArrayIndex = -1;
                m_ElementIndex = -1;
                m_NestedArray = nestedArray;
            }

            public void Dispose()
            {
                m_NestedArray = default;
                m_ArrayIndex = 0;
                m_ElementIndex = 0;
            }

            public bool MoveNext()
            {
                if (m_ArrayIndex < 0)
                {
                    if (m_NestedArray.SectionCount == 0)
                        return false;
                    ++m_ArrayIndex;
                }

                while (++m_ElementIndex >= m_NestedArray.Count(m_ArrayIndex))
                {
                    if (m_ArrayIndex + 1 >= m_NestedArray.SectionCount)
                        return false;
                    ++m_ArrayIndex;
                    m_ElementIndex = -1;
                }
                return true;
            }

            public void Reset()
            {
                m_ArrayIndex = -1;
                m_ElementIndex = -1;
            }
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Diagnostics;

#if UNITY_COLLECTIONS_CHANGED
using Allocator = Unity.Collections.AllocatorManager;
using AllocatorType = Unity.Collections.AllocatorManager.AllocatorHandle;
using static Unity.Collections.AllocatorManager;
#else
using Allocator = Unity.Collections.Allocator;
using AllocatorType = Unity.Collections.Allocator;
#endif

namespace Unity.MemoryProfiler.Editor.Containers
{
    internal struct NestedDynamicArray<T> : IDisposable, IEnumerable<DynamicArrayRef<T>> where T : unmanaged
    {
        readonly AllocatorType m_Allocator;
        DynamicArray<DynamicArrayRef<T>> m_NestedArrays;
        readonly DynamicArray<DynamicArray<T>> m_Data;
        public long SectionCount { readonly get; private set; }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public readonly long Count(long idx)
        {
            Checks.CheckEquals(true, IsCreated);
            return m_NestedArrays[idx].Count;
        }

        public readonly bool IsCreated => m_NestedArrays.IsCreated;

        /// <summary>
        ///
        /// </summary>
        /// <param name="offsets">A list of indices (given in bytes, not in <typeparamref name="T"/>)
        /// for the first byte of every nested array in the <paramref name="data"/>,
        /// usually starting with 0 for the first nested array.
        /// The offset to just behind the end of the last array is also needed to know its size.
        /// The offsets data is only needed during constructin and should get disposed by the caller afterwards.</param>
        /// <param name="data">The data to use as the basis for the Nested array.
        /// The nested array "takes ownership" of the data and will dispose it when disposed.</param>
        /// <exception cref="ArgumentException">Disposes itself before throwing.</exception>
        public NestedDynamicArray(DynamicArray<long> offsets, DynamicArray<T> data)
        {
            m_Allocator = data.Allocator;
            // Allow creation of NestedDynamicArray data with 0 sections
            SectionCount = Math.Max(offsets.Count - 1, 0);
            m_NestedArrays = new DynamicArray<DynamicArrayRef<T>>(SectionCount, data.Allocator);

            var clearArrayZero = false;
            if (data.Count == 0)
            {
                if (SectionCount > 0 && offsets[offsets.Count - 1] == 0)
                {
                    InitializeEmpty(out m_Data, ref clearArrayZero, m_Allocator);
                }
                else
                {
                    m_Data = new DynamicArray<DynamicArray<T>>(0, data.Allocator);
                }
                data.Dispose();
            }
            else
            {
                m_Data = new DynamicArray<DynamicArray<T>>(1, data.Allocator);
                m_Data[0] = data;
            }
            try
            {
                BuildSections(offsets);
            }
            catch
            {
                Dispose();
                throw;
            }
            if (clearArrayZero)
                m_Data[0].Clear(false);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="offsets">A list of indices (given in bytes, not in <typeparamref name="T"/>)
        /// for the first byte of every nested array in the <paramref name="data"/>,
        /// usually starting with 0 for the first nested array.
        /// The offset to just behind the end of the last array is also needed to know its size.
        /// The offsets data is only needed during constructing and should get disposed by the caller afterward.</param>
        /// <param name="data">The data to use as the basis for the Nested array. The nested array of
        /// the data array does not have to have be nested at the same granularity as the resulting nested array.
        /// Allowing a nested dynamic array as input is more for the benefit of allowing bigger data amounts to be chunked up.
        /// The nested array "takes ownership" of the data and will dispose it when disposed.</param>
        /// <exception cref="ArgumentException">Disposes itself before throwing.</exception>
        public NestedDynamicArray(DynamicArray<long> offsets, DynamicArray<DynamicArray<T>> data)
        {
            m_Allocator = data.Allocator;
            // Allow creation of NestedDynamicArray data with 0 sections
            SectionCount = Math.Max(offsets.Count - 1, 0);
            m_NestedArrays = new DynamicArray<DynamicArrayRef<T>>(SectionCount, m_Allocator);

            m_Data = data;

            var clearArrayZero = false;
            if (SectionCount > 0 && offsets[offsets.Count - 1] == 0)
            {
                var hasNoData = true;
                if (data.Count > 0)
                {
                    for (long i = 0; i < data.Count; i++)
                    {
                        if (data[i].Count > 0)
                        {
                            hasNoData = false;
                            break;
                        }
                    }
                }
                if (hasNoData)
                {
                    InitializeEmpty(out m_Data, ref clearArrayZero, m_Allocator);
                    data.Dispose();
                }
            }
            try
            {
                BuildSections(offsets);
            }
            catch
            {
                Dispose();
                throw;
            }
            if (clearArrayZero)
                m_Data[0].Clear(false);
        }

        static void InitializeEmpty(out DynamicArray<DynamicArray<T>> m_Data, ref bool clearArrayZero, AllocatorType m_Allocator)
        {
            // add a fake entry so BuildSections can create a list of 0 sized DynamicRefArrays.
            m_Data = new DynamicArray<DynamicArray<T>>(1, m_Allocator);
            m_Data[0] = new DynamicArray<T>(1, m_Allocator, memClear: true);
            clearArrayZero = true;
        }

        void BuildSections(DynamicArray<long> offsets)
        {
            if (SectionCount == 0)
            {
                if (m_Data.Count != 0)
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
                        // the start of the current range needs to be within the offset range for the data block,
                        // otherwise it would indicate that an offset range was spanning across 2 or more data blocks
                        if (offsets[i] < dataBlockOffset)
                            throw new ArgumentException($"offsets[{i}] is {offsets[i]} while its data block at index {dataBlockIndex + 1} should have started at an offset of {endOfCurrentDataBlock}. This indicates that a section was split across data blocks which is not allowed.");
                        start = (byte*)m_Data[++dataBlockIndex].GetUnsafePtr();
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
            using var newBlocks = new DynamicArray<DynamicArrayRef<T>>(m_NestedArrays.Count, Allocator.Temp);
            for (long i = 0; i < SectionCount; i++)
            {
                newBlocks[i] = m_NestedArrays[indexRemapping[i]];
            }
            unsafe
            {
                UnsafeUtility.MemCpy(m_NestedArrays.GetUnsafePtr(), newBlocks.GetUnsafePtr(), sizeof(DynamicArrayRef<T>) * SectionCount);
            }
        }

        public ref readonly DynamicArrayRef<T> this[long idx]
        {
            get
            {
                Checks.CheckEquals(true, IsCreated);
                Checks.CheckIndexOutOfBoundsAndThrow(idx, SectionCount);
                return ref m_NestedArrays[idx];
            }
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public readonly unsafe void* GetUnsafePtr(long idx)
        {
            Checks.CheckEquals(true, IsCreated);
            return m_NestedArrays[idx].GetUnsafePtr();
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public readonly unsafe T* GetUnsafeTypedPtr(long idx)
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

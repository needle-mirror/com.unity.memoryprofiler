using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Diagnostics;

namespace Unity.MemoryProfiler.Editor.Containers
{
    unsafe interface ILongIndexedContainer<T> : IEnumerable<T>
        where T : unmanaged
    {
        public long Count { get; }

        public long Capacity { get; }
        public bool IsCreated { get; }

        public ref T this[long idx] { get; }

        public T* GetUnsafeTypedPtr();
        public void* GetUnsafePtr();
    }

    /// <summary>
    /// DynamicArrayRef is a readonly, lighter weight, non owning, sub-data, possibly type-converting, handle/view into a <see cref="DynamicArray{T}"/>.
    /// It is used, in particular, for <seealso cref="NestedDynamicArray{T}"/>, where it provides simple views into the nested data
    /// while using a similar API structure using the same <see cref="ILongIndexedContainer{T}"/> interface as <see cref="DynamicArray{T}"/>
    /// while not implying ownership, nor allowing growing or
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [StructLayout(LayoutKind.Sequential)]
    unsafe readonly struct DynamicArrayRef<T> : IEquatable<DynamicArrayRef<T>>, ILongIndexedContainer<T> where T : unmanaged
    {
        [NativeDisableUnsafePtrRestriction]
        readonly T* m_Data;
        readonly long m_Count;

        public readonly long Count => m_Count;
        public readonly long Capacity => Count;

        public readonly bool IsCreated { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly T* GetUnsafeTypedPtr() => m_Data;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void* GetUnsafePtr() => m_Data;

        public readonly ref T this[long idx]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Checks.CheckIndexInRangeAndThrow(idx, Count);
                return ref m_Data[idx];
            }
        }

        public static implicit operator DynamicArrayRef<T>(DynamicArray<T> dynamicArray)
            => ConvertExistingDataToDynamicArrayRef(dynamicArray);

        public static unsafe DynamicArrayRef<T> ConvertExistingDataToDynamicArrayRef(DynamicArray<T> dynamicArray)
        {
            return (dynamicArray.Count == 0 || !dynamicArray.IsCreated) ?
                new DynamicArrayRef<T>(null, 0, dynamicArray.IsCreated) :
                new DynamicArrayRef<T>(dynamicArray.GetUnsafeTypedPtr(), dynamicArray.Count, dynamicArray.IsCreated);
        }

        public static unsafe DynamicArrayRef<T> ConvertExistingDataToDynamicArrayRef(T* dataPointer, long length)
        {
            return new DynamicArrayRef<T>(dataPointer, length, true);
        }

        unsafe DynamicArrayRef(T* data, long count, bool isCreated)
        {
            m_Data = data;
            m_Count = count;
            IsCreated = isCreated;
        }

        public readonly IEnumerator<T> GetEnumerator() => new DynamicArrayEnumerator<T>(this);

        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public readonly int GetHashCode(DynamicArrayRef<T> obj) => obj.GetHashCode();

        public override int GetHashCode()
        {
            if (!IsCreated || Count == 0)
                return HashCode.Combine(Count, IsCreated);
            var hash = this[0].GetHashCode();
            for (long i = 1; i < Count; i++)
            {
                hash = HashCode.Combine(hash, this[i]);
            }
            return hash;
        }

        public readonly bool Equals(DynamicArrayRef<T> other)
        {
            if (m_Data == other.m_Data)
                return true;

            if (Count != other.Count)
                return false;

            return UnsafeUtility.MemCmp(m_Data, other.m_Data, sizeof(T) * Count) == 0;
        }
    }

    /// <summary>
    /// <see cref="NativeArray{T}"/> is int indexed, but the kind of data amounts that the Memory Profiler
    /// needs to be able to handle when dealing with memory snapshots needs to be able to handle more than <see cref="int.MaxValue"/> items.
    /// Additionally, this data structure can also grow and be used as a stack.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    unsafe struct DynamicArray<T> : ILongIndexedContainer<T>, IDisposable where T : unmanaged
    {
        [NativeDisableUnsafePtrRestriction]
        T* m_Data;
        long m_Capacity;
        long m_Count;
        public readonly long Count => m_Count;

        Allocator m_Allocator;
        public readonly Allocator Allocator => m_Allocator;

        public enum ResizePolicy
        {
            Double,
            Exact,
            /// <summary>
            /// closest multiple of 32
            /// </summary>
            ClosestIncrementMultiple
        }
        const ResizePolicy k_DefaultResizePolicy = ResizePolicy.ClosestIncrementMultiple;
        const long k_DefaultCapacityIncrements = 32;

        /// <summary>
        /// Increasing the capacity will not clear any new memory.
        /// To controll memory clearing behavior while changing the capacity, use <see cref="Reserve(long, bool, ResizePolicy)"/>.
        /// </summary>
        public long Capacity
        {
            readonly get { return m_Capacity; }
            set
            {
                Checks.CheckEquals(true, IsCreated);
                ResizeInternalBuffer(value, true);
            }
        }

        public bool IsCreated { readonly get; private set; }
        public long ElementSize => sizeof(T);

        public DynamicArray(Allocator allocator) : this(0, allocator) { }

        public DynamicArray(long initialCount, Allocator allocator, bool memClear = false) : this(initialCount, initialCount, allocator, memClear) { }

        public DynamicArray(long initialCount, long initialCapacity, Allocator allocator, bool memClear = false)
        {
            m_Allocator = allocator;
            m_Count = initialCount;
            m_Capacity = initialCapacity;

            if (m_Capacity != 0)
            {
                var allocSize = m_Capacity * sizeof(T);
                m_Data = (T*)UnsafeUtility.Malloc(allocSize, UnsafeUtility.AlignOf<T>(), m_Allocator);
                if (memClear)
                    UnsafeUtility.MemClear(m_Data, allocSize);
            }
            else
                m_Data = null;
            IsCreated = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe DynamicArray<T> ConvertExistingDataToDynamicArray(T* dataPointer, long count, Allocator allocator)
            => ConvertExistingDataToDynamicArray(dataPointer, count, allocator, count);

        public static unsafe DynamicArray<T> ConvertExistingDataToDynamicArray(T* dataPointer, long count, Allocator allocator, long capacity)
        {
            if (capacity < count)
                throw new ArgumentException("Capacity must be greater or equal to length", nameof(capacity));
            return new DynamicArray<T>(allocator)
            {
                m_Data = dataPointer,
                m_Count = count,
                m_Capacity = capacity,
                IsCreated = true,
            };
        }

        public DynamicArray<U> Reinterpret<U>() where U : unmanaged
        {
            return Reinterpret<U>(sizeof(T));
        }

        DynamicArray<U> Reinterpret<U>(int expectedTypeSize) where U : unmanaged
        {
            long tSize = sizeof(T);
            long uSize = sizeof(U);

            long byteCount = Count * tSize;
            long uCount = byteCount / uSize;

            long byteCap = m_Capacity * tSize;
            long uCap = byteCap / uSize;


            Checks.CheckEquals(expectedTypeSize, tSize);
            Checks.CheckEquals(byteCount, uCount * uSize);
            Checks.IsTrue(uCount >= 0);
            Checks.IsTrue(uCap >= 0);

            return new DynamicArray<U>()
            {
                m_Data = (U*)m_Data,
                IsCreated = true,
                m_Count = uCount,
                m_Capacity = uCap,
                m_Allocator = m_Allocator
            };
        }

        public void CopyFrom(T[] arr)
        {
            Checks.CheckNotNull(arr);
            Resize(arr.Length, false);
            unsafe
            {
                fixed (void* src = arr)
                    UnsafeUtility.MemCpy(m_Data, src, sizeof(T) * arr.LongLength);
            }
        }

        public readonly ref T this[long idx]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Checks.CheckEquals(true, IsCreated);
                Checks.CheckIndexInRangeAndThrow(idx, Count);
                return ref (m_Data)[idx];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void* GetUnsafePtr() { return m_Data; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly T* GetUnsafeTypedPtr() { return m_Data; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref readonly T Back()
        {
            Checks.CheckEquals(true, IsCreated);
            return ref this[Count - 1];
        }

        /// <summary>
        /// Try adding a number of elements to the array without growing the <see cref="Capacity"/>.
        /// </summary>
        /// <param name="count"></param>
        /// <returns>true if succesful, false if resizing would have been bigger than capacity.</returns>
        public bool TryAddCountWithoutGrowing(long count, out long firstNewElementIndex, bool memClear = false)
        {
            Checks.CheckEquals(true, IsCreated);
            // count has got to be positive
            Checks.CheckCountWithinReasonAndThrowArgumentException(count, m_Count);

            var newCount = m_Count + count;
            if (newCount > m_Capacity)
            {
                firstNewElementIndex = -1;
                return false;
            }
            firstNewElementIndex = m_Count;
            m_Count = newCount;
            if (memClear)
                MemClear(firstNewElementIndex);
            return true;
        }

        public void Reserve(long neededCapacity, bool memClearAllAboveCount = false, ResizePolicy resizePolicy = k_DefaultResizePolicy)
        {
            Checks.CheckEquals(true, IsCreated);

            if (neededCapacity <= m_Capacity)
                return;

            ResizeInternalBuffer(neededCapacity, false, resizePolicy);

            if (memClearAllAboveCount)
                MemClear(m_Count);
        }

        void MemClear(long start)
        {
            var dst = (byte*)m_Data;
            var offsetInBytes = start * sizeof(T);
            UnsafeUtility.MemClear(dst + offsetInBytes, (m_Capacity - m_Count) * sizeof(T));
        }

        static long CalculateSizeForDoubling(long neededCapacity, long currentCapacity)
        {
            var nextCapacity = currentCapacity;
            if (nextCapacity == 0)
                nextCapacity = 1;
            while (nextCapacity < neededCapacity)
            {
                nextCapacity *= 2;
            }
            return nextCapacity;
        }

        public void Resize(long newSize, bool memClear, ResizePolicy resizePolicy = k_DefaultResizePolicy)
        {
            Checks.CheckEquals(true, IsCreated);
            if (newSize > m_Capacity)
                ResizeInternalBuffer(newSize, false, resizePolicy);

            if (memClear && newSize > m_Count)
            {
                var dst = (byte*)m_Data;
                var offsetToLastElementInBytes = m_Count * sizeof(T);
                UnsafeUtility.MemClear(dst + offsetToLastElementInBytes, (newSize - m_Count) * sizeof(T));
            }
            m_Count = newSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ResizeInternalBuffer(long newCapacity, bool forceResize, ResizePolicy resizePolicy = k_DefaultResizePolicy)
        {
            if (newCapacity > m_Capacity || (forceResize && newCapacity != m_Capacity))
            {
                newCapacity = resizePolicy switch
                {
                    ResizePolicy.Double => CalculateSizeForDoubling(newCapacity, m_Capacity),
                    ResizePolicy.Exact => newCapacity,
                    ResizePolicy.ClosestIncrementMultiple => (newCapacity / k_DefaultCapacityIncrements + 1) * k_DefaultCapacityIncrements,
                    _ => throw new NotImplementedException(),
                };

                if (m_Allocator == Allocator.None)
                    throw new NotSupportedException("Resizing a DynamicArray that acts as a slice of another DynamicArray is not allowed");
                var newMem = (T*)UnsafeUtility.Malloc(newCapacity * sizeof(T), UnsafeUtility.AlignOf<T>(), m_Allocator);

                if (m_Data != null)
                {
                    UnsafeUtility.MemCpy(newMem, m_Data, Count * sizeof(T));
                    UnsafeUtility.Free(m_Data, m_Allocator);
                }

                m_Data = newMem;
                m_Capacity = newCapacity;
            }
        }

        public void Push(T value, ResizePolicy resizePolicy = ResizePolicy.Double, bool memClearForExcessExpansion = false)
        {
            Checks.CheckEquals(true, IsCreated);
            if (Count + 1 > m_Capacity)
                ResizeInternalBuffer(m_Capacity + 1, false, resizePolicy);

            if (memClearForExcessExpansion)
                MemClear(m_Count);
            (m_Data)[m_Count++] = value;
        }

        /// <summary>
        /// Note: This whole method is not thread safe. It is not intended to be used in a multithreaded context.
        /// Expansion is not atomic and after reserving, copying isn't atomic either so simultaneous Pop()s could lead to data corruption.
        /// </summary>
        /// <param name="values"></param>
        /// <param name="memClearForExcessExpansion"></param>
        /// <param name="resizePolicy"></param>
        public void PushRange(DynamicArray<T> values, bool memClearForExcessExpansion = false, ResizePolicy resizePolicy = ResizePolicy.Exact)
        {
            Checks.CheckEquals(true, IsCreated);
            if (values.Count == 0)
                return;

            var newCount = m_Count + values.Count;
            if (newCount > m_Capacity)
                ResizeInternalBuffer(newCount, false, resizePolicy);

            if (memClearForExcessExpansion)
                MemClear(m_Count);

            UnsafeUtility.MemCpy((void*)(m_Data + m_Count), values.GetUnsafePtr(), values.Count * sizeof(T));
            m_Count = newCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ref T Peek()
        {
            Checks.CheckEquals(true, IsCreated);
            Checks.CheckCountGreaterZeroAndThrowInvalidOperationException(m_Count);

            return ref this[m_Count - 1];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Pop()
        {
            Checks.CheckEquals(true, IsCreated);
            Checks.CheckCountGreaterZeroAndThrowInvalidOperationException(m_Count);

            return (m_Data)[--m_Count];
        }

        public void RemoveAt(long index)
        {
            Checks.CheckEquals(true, IsCreated);
            Checks.CheckIndexInRangeAndThrow(index, Count);

            if (m_Count != index + 1)
            {
                // if not removing the highest index item, move everything following the removed index towards the index
                // not really save if multiple threads add/remove elements.
                UnsafeUtility.MemMove(m_Data + index, m_Data + index + 1, (m_Count - index) * sizeof(T));
            }
            --m_Count;
        }

        public void Clear(bool stomp)
        {
            Checks.CheckEquals(true, IsCreated);
            if (stomp)
            {
                UnsafeUtility.MemClear(m_Data, sizeof(T));
            }

            m_Count = 0;
        }

        public void ShrinkToFit()
        {
            Capacity = Count;
        }

        public void Dispose()
        {
            if (IsCreated && m_Data != null && m_Allocator > Allocator.None)
            {
                if (typeof(IDisposable).IsAssignableFrom(typeof(T)))
                {
                    for (var i = 0; i < m_Count; i++)
                    {
                        ((IDisposable)m_Data[i]).Dispose();
                    }
                }
                UnsafeUtility.Free(m_Data, m_Allocator);
            }
            m_Capacity = 0;
            m_Count = 0;
            m_Data = null;
            m_Allocator = Allocator.Invalid;
            IsCreated = false;
        }

        public readonly IEnumerator<T> GetEnumerator() => new DynamicArrayEnumerator<T>(this);

        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    unsafe struct DynamicArrayEnumerator<T> : IEnumerator<T>
        where T : unmanaged
    {
        public readonly T Current => nextIndex == 0 ? default : m_Array[nextIndex - 1];

        readonly object IEnumerator.Current => Current;

        ILongIndexedContainer<T> m_Array;
        long nextIndex;

        public DynamicArrayEnumerator(ILongIndexedContainer<T> array)
        {
            m_Array = array;
            nextIndex = 0;
        }

        public void Dispose()
        {
            m_Array = default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            return ++nextIndex < m_Array.Count;
        }

        public void Reset()
        {
            nextIndex = 0;
        }
    }
}

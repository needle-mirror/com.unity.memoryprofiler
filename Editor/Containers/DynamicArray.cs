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
    unsafe readonly struct DynamicArrayRef<T> : ILongIndexedContainer<T> where T : unmanaged
    {
        [NativeDisableUnsafePtrRestriction]
        readonly T* m_Data;
        readonly long m_Size;

        public readonly long Count => m_Size;
        public readonly long Capacity => Count;

        public readonly bool IsCreated { get; }
        public readonly T* GetUnsafeTypedPtr() => m_Data;

        public readonly void* GetUnsafePtr() => m_Data;

        public readonly ref T this[long idx]
        {
            get
            {
                Checks.CheckIndexOutOfBoundsAndThrow(idx, Count);
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

        unsafe DynamicArrayRef(T* data, long lendth, bool isCreated)
        {
            m_Data = data;
            m_Size = lendth;
            IsCreated = isCreated;
        }

        public readonly IEnumerator<T> GetEnumerator() => new DynamicArrayEnumerator<T>(this);

        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
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
        public long Count { readonly get; private set; }

        Allocator m_Allocator;
        public readonly Allocator Allocator => m_Allocator;

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

        public DynamicArray(Allocator allocator) : this(0, allocator) { }

        public DynamicArray(long initialSize, Allocator allocator, bool memClear = false)
        {
            m_Allocator = allocator;
            Count = initialSize;
            m_Capacity = initialSize;

            if (initialSize != 0)
            {
                var allocSize = initialSize * UnsafeUtility.SizeOf<T>();
                m_Data = (T*)UnsafeUtility.Malloc(allocSize, UnsafeUtility.AlignOf<T>(), m_Allocator);
                if (memClear)
                    UnsafeUtility.MemClear(m_Data, allocSize);
            }
            else
                m_Data = null;
            IsCreated = true;
        }

        public static unsafe DynamicArray<T> ConvertExistingDataToNativeArray(T* dataPointer, long length, Allocator allocator)
        {
            return new DynamicArray<T>(allocator)
            {
                m_Data = dataPointer,
                Count = length,
                m_Capacity = length,
                IsCreated = true,
            };
        }

        public DynamicArray<U> Reinterpret<U>() where U : unmanaged
        {
            return Reinterpret<U>(UnsafeUtility.SizeOf<T>());
        }

        DynamicArray<U> Reinterpret<U>(int expectedTypeSize) where U : unmanaged
        {
            long tSize = UnsafeUtility.SizeOf<T>();
            long uSize = UnsafeUtility.SizeOf<U>();

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
                Count = uCount,
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
                    UnsafeUtility.MemCpy(m_Data, src, UnsafeUtility.SizeOf<T>() * arr.LongLength);
            }
        }

        public readonly ref T this[long idx]
        {
            get
            {
                Checks.CheckEquals(true, IsCreated);
                Checks.CheckIndexOutOfBoundsAndThrow(idx, Count);
                return ref (m_Data)[idx];
            }
        }

        public readonly void* GetUnsafePtr() { return m_Data; }

        public readonly T* GetUnsafeTypedPtr() { return m_Data; }

        [MethodImpl(256)]
        public readonly T Back()
        {
            Checks.CheckEquals(true, IsCreated);
            return this[Count - 1];
        }

        public void Resize(long newSize, bool memClear)
        {
            Checks.CheckEquals(true, IsCreated);
            var oldCount = Count;
            if (newSize > m_Capacity)
                ResizeInternalBuffer(newSize, false);

            if (memClear && newSize > oldCount)
            {
                var typeSize = UnsafeUtility.SizeOf<T>();
                UnsafeUtility.MemClear(((byte*)m_Data) + (oldCount * typeSize), (newSize - oldCount) * typeSize);
            }
            Count = newSize;
        }

        [MethodImpl(256)]
        void ResizeInternalBuffer(long newCapacity, bool forceResize)
        {
            if (newCapacity > m_Capacity || (forceResize && newCapacity != m_Capacity))
            {
                if (m_Allocator == Allocator.None)
                    throw new NotSupportedException("Resizing a DynamicArray that acts as a slice of another DynamicArray is not allowed");
                var elemSize = UnsafeUtility.SizeOf<T>();
                var newMem = (T*)UnsafeUtility.Malloc(newCapacity * elemSize, UnsafeUtility.AlignOf<T>(), m_Allocator);

                if (m_Data != null)
                {
                    UnsafeUtility.MemCpy(newMem, m_Data, Count * elemSize);
                    UnsafeUtility.Free(m_Data, m_Allocator);
                }

                m_Data = newMem;
                m_Capacity = newCapacity;
            }
        }

        public void Push(T value)
        {
            Checks.CheckEquals(true, IsCreated);
            if (Count + 1 > m_Capacity)
                ResizeInternalBuffer(Math.Max(m_Capacity, 1) * 2, false);
            this[Count++] = value;
        }

        public T Pop()
        {
            return this[--Count];
        }

        public readonly ref T Peek()
        {
            return ref this[Count - 1];
        }

        public void Clear(bool stomp)
        {
            Checks.CheckEquals(true, IsCreated);
            if (stomp)
            {
                UnsafeUtility.MemClear(m_Data, UnsafeUtility.SizeOf<T>());
            }

            Count = 0;
        }

        public void ShrinkToFit()
        {
            Capacity = Count;
        }

        public void Dispose()
        {
            if (IsCreated && m_Data != null && m_Allocator > Allocator.None)
            {
                UnsafeUtility.Free(m_Data, m_Allocator);
            }
            m_Capacity = 0;
            Count = 0;
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

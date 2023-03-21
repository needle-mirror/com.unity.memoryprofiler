using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Containers.Memory;
using Unity.MemoryProfiler.Editor.Diagnostics;

namespace Unity.MemoryProfiler.Editor.Containers
{
    unsafe struct DynamicArray<T> : IDisposable, IEnumerable<T> where T : unmanaged
    {
        void* m_Data;
        long m_Capacity;
        public long Count { get; private set; }

        Allocator m_Allocator;
        public Allocator Allocator => m_Allocator;

        public long Capacity
        {
            get { return m_Capacity; }
            set
            {
                Checks.CheckEquals(true, IsCreated);
                ResizeInternalBuffer(value, true);
            }
        }

        public bool IsCreated { get; private set; }

        public DynamicArray(Allocator allocator) : this(0, allocator) { }

        public DynamicArray(long initialSize, Allocator allocator, bool memClear = false)
        {
            m_Allocator = allocator;
            Count = initialSize;
            m_Capacity = initialSize;

            if (initialSize != 0)
            {
                var allocSize = initialSize * UnsafeUtility.SizeOf<T>();
                m_Data = UnsafeUtility.Malloc(allocSize, UnsafeUtility.AlignOf<T>(), m_Allocator);
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

        public DynamicArray<U> Reinterpret<U>(int expectedTypeSize) where U : unmanaged
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
                m_Data = m_Data,
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
                ulong handle;
                void* src = UnsafeUtility.PinGCArrayAndGetDataAddress(arr, out handle);
                UnsafeUtility.MemCpy(m_Data, src, UnsafeUtility.SizeOf<T>() * arr.LongLength);
                UnsafeUtility.ReleaseGCObject(handle);
            }
        }

        public T this[long idx]
        {
            get
            {
                Checks.CheckEquals(true, IsCreated);
                Checks.CheckIndexOutOfBoundsAndThrow(idx, Count);
                return ((T*)m_Data)[idx];
            }
            set
            {
                Checks.CheckEquals(true, IsCreated);
                Checks.CheckIndexOutOfBoundsAndThrow(idx, Count);
                UnsafeDataUtility.WriteArrayElement(m_Data, idx, ref value);
            }
        }

        public void* GetUnsafePtr() { return m_Data; }

        public T* GetUnsafeTypedPtr() { return (T*)m_Data; }

        [MethodImpl(256)]
        public T Back()
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
                int elemSize = UnsafeUtility.SizeOf<T>();
                void* newMem = UnsafeUtility.Malloc(newCapacity * elemSize, UnsafeUtility.AlignOf<T>(), m_Allocator);

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
            if (Count + 1 >= m_Capacity)
                ResizeInternalBuffer(Math.Max(m_Capacity, 1) * 2, false);
            this[++Count - 1] = value;
        }

        public T Pop()
        {
            var ret = this[Count - 1];
            --Count;
            return ret;
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

        public IEnumerator<T> GetEnumerator()
        {
            return new DynamicArrayEnumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        struct DynamicArrayEnumerator : IEnumerator<T>
        {
            public T Current => nextIndex == 0 ? default : m_Array[nextIndex - 1];

            object IEnumerator.Current => Current;

            DynamicArray<T> m_Array;
            long nextIndex;

            public DynamicArrayEnumerator(DynamicArray<T> array)
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
}

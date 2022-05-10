using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Containers.Memory;
using Unity.MemoryProfiler.Editor.Diagnostics;

namespace Unity.MemoryProfiler.Editor.Containers
{
    public unsafe struct DynamicArray<T> : IDisposable where T : unmanaged
    {
        void* m_Data;
        long m_Capacity;
        Allocator m_Label;

        public long Count { get; private set; }

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

        public DynamicArray(Allocator label) : this(0, label) {}

        public DynamicArray(long initialSize, Allocator label, bool memClear = false)
        {
            m_Label = label;
            Count = initialSize;
            m_Capacity = initialSize;

            if (initialSize != 0)
            {
                var allocSize = initialSize * UnsafeUtility.SizeOf<T>();
                m_Data = UnsafeUtility.Malloc(allocSize, UnsafeUtility.AlignOf<T>(), m_Label);
                if (memClear)
                    UnsafeUtility.MemClear(m_Data, allocSize);
            }
            else
                m_Data = null;
            IsCreated = true;
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
                m_Label = m_Label
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
                //return UnsafeDataUtility.ReadArrayElement<T>(m_Data, idx);
                var ptr = ((T*)m_Data) + idx;
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
            if (newCapacity > m_Capacity || forceResize)
            {
                int elemSize = UnsafeUtility.SizeOf<T>();
                void* newMem = UnsafeUtility.Malloc(newCapacity * elemSize, UnsafeUtility.AlignOf<T>(), m_Label);

                if (m_Data != null)
                {
                    UnsafeUtility.MemCpy(newMem, m_Data, Count * elemSize);
                    UnsafeUtility.Free(m_Data, m_Label);
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
            if (IsCreated && m_Data != null)
            {
                UnsafeUtility.Free(m_Data, m_Label);
                m_Data = null;
                m_Capacity = 0;
                Count = 0;
                m_Label = Allocator.None;
            }
            IsCreated = false;
        }
    }
}

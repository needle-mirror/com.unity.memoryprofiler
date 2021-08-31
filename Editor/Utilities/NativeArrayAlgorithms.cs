using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Containers;

namespace Unity.MemoryProfiler.Containers
{
    namespace Unsafe
    {
        internal static class DynamicArrayAlgorithms
        {
            /// <summary>
            /// Implementation of the binary search algorithm.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="array">Pre sorted native array</param>
            /// <param name="value"></param>
            /// <remarks>Note that there are no checks in regards to the provided NativeArray's sort state.</remarks>
            /// <returns>Index of the value</returns>
            public static long BinarySearch<T>(DynamicArray<T> array, T value) where T : unmanaged, IComparable<T>
            {
                unsafe
                {
                    switch (array.Count)
                    {
                        case 1:
                            if (array[0].CompareTo(value) == 0)
                            {
                                return 0;
                            }
                            return -1;

                        case 2:
                            if (array[0].CompareTo(value) == 0)
                            {
                                return 0;
                            }

                            if (array[1].CompareTo(value) == 0)
                            {
                                return 1;
                            }
                            return -1;
                    }

                    long left = 0;
                    long right = array.Count - 1;
                    while (left <= right)
                    {
                        long mid = left + ((right - left) >> 1);
                        long cmpResult = array[mid].CompareTo(value);

                        switch (cmpResult)
                        {
                            case -1:
                                left = mid + 1;
                                break;
                            case 1:
                                right = mid - 1;
                                break;
                            case 0:
                                return mid;
                        }
                    }
                    return ~left;
                }
            }

            /// <summary>
            /// Port of MSDN's internal method for QuickSort, can work with native array containers inside a jobified environment.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="array"></param>
            /// <param name="startIndex"></param>
            /// <param name="length"></param>
            /// <remarks>
            /// ~10% slower than it's counterpart when using Mono 3.5, and 10% faster when using Mono 4.x
            /// </remarks>
            public static void IntrospectiveSort<T>(NativeArray<T> array, int startIndex, int length) where T : unmanaged, IComparable<T>
            {
                if (length < 0 || length > array.Length)
                    throw new ArgumentOutOfRangeException("length should be in the range [0, array.Length].");
                if (startIndex < 0 || startIndex > length - 1)
                    throw new ArgumentOutOfRangeException("startIndex should in the range [0, length).");

                if (length < 2)
                    return;

                unsafe
                {
                    NativeArrayData<T> data = new NativeArrayData<T>((byte*)array.GetUnsafePtr());
                    IntroSortInternal(ref data, startIndex, length + startIndex - 1, GetMaxDepth(array.Length), GetPartitionThreshold());
                }
            }

            unsafe struct NativeArrayData<T> where T : unmanaged
            {
                [NativeDisableUnsafePtrRestriction]
                public readonly T* ptr;
                public T aux_first;
                public T aux_second;

                public NativeArrayData(void* nativeArrayPtr)
                {
                    ptr = (T*)nativeArrayPtr;
                    aux_first = default(T);
                    aux_second = aux_first;
                }
            }

            static void IntroSortInternal<T>(ref NativeArrayData<T> array, int low, int high, int depth, int partitionThreshold) where T : unmanaged, IComparable<T>

            {
                while (high > low)
                {
                    int partitionSize = high - low + 1;
                    if (partitionSize <= partitionThreshold)
                    {
                        switch (partitionSize)
                        {
                            case 1:
                                return;
                            case 2:
                                SwapIfGreater(ref array, low, high);
                                return;
                            case 3:
                                SwapSortAscending(ref array, low, high - 1, high);
                                return;
                            default:
                                InsertionSort(ref array, low, high);
                                return;
                        }
                    }
                    else if (depth == 0)
                    {
                        Heapsort(ref array, low, high);
                        return;
                    }
                    --depth;

                    int pivot = PartitionRangeAndPlacePivot(ref array, low, high);
                    IntroSortInternal(ref array, pivot + 1, high, depth, partitionThreshold);
                    high = pivot - 1;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static void Heapsort<T>(ref NativeArrayData<T> array, int low, int high) where T : unmanaged, IComparable<T>
            {
                int rangeSize = high - low + 1;
                for (int i = rangeSize / 2; i >= 1; --i)
                {
                    DownHeap(ref array, i, rangeSize, low);
                }
                for (int i = rangeSize; i > 1; --i)
                {
                    Swap(ref array, low, low + i - 1);

                    DownHeap(ref array, 1, i - 1, low);
                }
            }

            unsafe static void DownHeap<T>(ref NativeArrayData<T> array, int i, int n, int low) where T : unmanaged, IComparable<T>
            {
                var typeSize = UnsafeUtility.SizeOf<T>();
                array.aux_first = *(array.ptr + (low + i - 1));

                int child;
                while (i <= n / 2)
                {
                    child = 2 * i;
                    T* cChildAddr = array.ptr + (low + child - 1);
                    T* nChildAddr = array.ptr + (low + child);

                    if (child < n && cChildAddr->CompareTo(*nChildAddr) < 0)
                    {
                        ++child;
                        cChildAddr = nChildAddr;
                        if (!(array.aux_first.CompareTo(*nChildAddr) < 0))
                            break;
                    }
                    else
                    {
                        if (!(array.aux_first.CompareTo(*cChildAddr) < 0))
                            break;
                    }

                    UnsafeUtility.MemCpy(array.ptr + (low + i - 1), cChildAddr, typeSize);
                    i = child;
                }
                *(array.ptr + (low + i - 1)) = array.aux_first;
            }

            unsafe static void InsertionSort<T>(ref NativeArrayData<T> array, int low, int high) where T : unmanaged, IComparable<T>
            {
                int i, j;
                var typeSize = UnsafeUtility.SizeOf<T>();

                for (i = low; i < high; ++i)
                {
                    j = i;
                    array.aux_first = *(array.ptr + (i + 1));
                    while (j >= low)
                    {
                        if (!(array.aux_first.CompareTo(*(array.ptr + j)) < 0))
                            break;
                        UnsafeUtility.MemCpy(array.ptr + (j + 1), array.ptr + j, typeSize);
                        j--;
                    }
                    *(array.ptr + (j + 1)) = array.aux_first;
                }
            }

            unsafe static int PartitionRangeAndPlacePivot<T>(ref NativeArrayData<T> array, int low, int high) where T : unmanaged, IComparable<T>
            {
                int mid = low + (high - low) / 2;

                // Sort low/high/mid in order to have the correct pivot.
                SwapSortAscending(ref array, low, mid, high);

                array.aux_second = *(array.ptr + mid);

                Swap(ref array, mid, high - 1);
                int left = low, right = high - 1;

                while (left < right)
                {
                    do { ++left; }
                    while ((array.ptr + left)->CompareTo(array.aux_second) < 0);

                    do { --right; }
                    while (array.aux_second.CompareTo(*(array.ptr + right)) < 0);

                    if (left >= right)
                        break;

                    Swap(ref array, left, right);
                }

                Swap(ref array, left, (high - 1));
                return left;
            }
            
            unsafe static void SwapSortAscending<T>(ref NativeArrayData<T> array, int left, int mid, int right) where T : unmanaged, IComparable<T>
            {
                var typeSize = UnsafeUtility.SizeOf<T>();
                T* leftAddr = array.ptr + left;
                T* midAddr = array.ptr + mid;
                T* rightAddr = array.ptr + right;

                int bitmask = 0;
                if (leftAddr->CompareTo(*midAddr) > 0)
                    bitmask = 1;
                if (leftAddr->CompareTo(*rightAddr) > 0)
                    bitmask |= 1 << 1;
                if (midAddr->CompareTo(*rightAddr) > 0)
                    bitmask |= 1 << 2;

                switch (bitmask)
                {
                    case 1:
                        array.aux_first = *leftAddr;
                        UnsafeUtility.MemCpy(leftAddr, midAddr, typeSize);
                        *midAddr = array.aux_first;
                        return;
                    case 3:
                        array.aux_first = *leftAddr;
                        UnsafeUtility.MemCpy(leftAddr, midAddr, typeSize);
                        UnsafeUtility.MemCpy(midAddr, rightAddr, typeSize);
                        *rightAddr = array.aux_first;
                        return;
                    case 4:
                        array.aux_first = *midAddr;
                        UnsafeUtility.MemCpy(midAddr, rightAddr, typeSize);
                        *rightAddr = array.aux_first;
                        return;
                    case 6:
                        array.aux_first = *leftAddr;
                        UnsafeUtility.MemCpy(leftAddr, rightAddr, typeSize);
                        UnsafeUtility.MemCpy(rightAddr, midAddr, typeSize);
                        *midAddr = array.aux_first;
                        return;
                    case 7:
                        array.aux_first = *leftAddr;
                        UnsafeUtility.MemCpy(leftAddr, rightAddr, typeSize);
                        *rightAddr = array.aux_first;
                        return;
                    default: //we are already ordered
                        return;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            unsafe static void SwapIfGreater<T>(ref NativeArrayData<T> array, int lhs, int rhs) where T : unmanaged, IComparable<T>
            {
                if (lhs != rhs)
                {
                    T* leftAddr = array.ptr + lhs;
                    T* rightAddr = array.ptr + rhs;

                    if (leftAddr->CompareTo(*rightAddr) > 0)
                    {
                        array.aux_first = *rightAddr;
                        UnsafeUtility.MemCpy(rightAddr, leftAddr, UnsafeUtility.SizeOf<T>());
                        *leftAddr = array.aux_first;
                    }
                }
            }
            
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            unsafe static void Swap<T>(ref NativeArrayData<T> array, int lhs, int rhs) where T : unmanaged, IComparable<T>
            {
                T* leftAddr = array.ptr + lhs;
                T* rightAddr = array.ptr + rhs;
                array.aux_first = *leftAddr;

                UnsafeUtility.MemCpy(leftAddr, rightAddr, UnsafeUtility.SizeOf<T>());
                *rightAddr = array.aux_first;
            }

            static int GetMaxDepth(int length)
            {
                return 2 * UnityEngine.Mathf.FloorToInt((float)Math.Log(length, 2));
            }

            static int GetPartitionThreshold()
            {
                return 16;
            }
        }
    }
}

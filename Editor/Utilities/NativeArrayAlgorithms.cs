using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.MemoryProfiler.Editor.Containers.Unsafe;

namespace Unity.MemoryProfiler
{
    internal interface IRefComparer<T> where T : unmanaged
    {
        int Compare(ref T x, ref T y);
    }
}

namespace Unity.MemoryProfiler.Editor.Containers
{
    namespace Unsafe
    {

        /// <summary>
        /// This struct provides comparison logic for an array of unmanaged data via the indices into that array.
        /// It is used when <see cref="IntrospectiveSort{TComparer}(long, long, ref TComparer)"/> is used not to sort the data array itself,
        /// but instead an array of indices into the data array.
        ///
        /// This indirect sorting is e.g. used in <seealso cref="CachedSnapshot.IndirectlySortedEntriesCache{TComparableData}"/>.
        ///
        /// If you want to sort an array data by comparing the elements to each other directly, use <seealso cref="DirectValueComparer{T}"/>.
        /// </summary>
        /// <typeparam name="TComparableData"></typeparam>
        unsafe readonly struct IndexedArrayValueComparer<TComparableData> : IRefComparer<long>
            where TComparableData : unmanaged, IComparable<TComparableData>
        {
            // TODO: Once ref structs are available, change from pointer logic to ref field
            [NativeDisableUnsafePtrRestriction]
            readonly TComparableData* m_IndexedArrayPtr;
            public IndexedArrayValueComparer(in DynamicArray<TComparableData> indexedArray)
            {
                m_IndexedArrayPtr = indexedArray.GetUnsafeTypedPtr();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int Compare(ref long arrayIndexA, ref long arrayIndexB)
            {
                return m_IndexedArrayPtr[arrayIndexA].CompareTo(m_IndexedArrayPtr[arrayIndexB]);
            }
        }


        /// <summary>
        /// This struct provides comparison logic for an array of unmanaged data via the indices into two arrays that define a range between these values.
        /// If the start value of the range matches, the tie breaker is the inverse comparison result of the Length of each range.
        ///
        /// It is used when <see cref="IntrospectiveSort{TComparer}(long, long, ref TComparer)"/> is used not to sort the data array itself,
        /// but instead an array of indices into the data array that represents ranges that can overlap.
        ///
        /// Nb! Using this Comparer to sort data with multiple ranges of the same size and address, is not supported.
        ///
        /// This indirect sorting is e.g. used in <seealso cref="CachedSnapshot.IndirectlySortedEntriesCacheSortedByAddressAndSizeArray"/>.
        ///
        /// If you want to sort an array data by comparing the elements to each other directly, use <seealso cref="DirectValueComparer{T}"/>.
        /// </summary>
        /// <typeparam name="TComparableData"></typeparam>
        unsafe readonly struct IndexedArrayRangeValueComparer<TComparableData> : IRefComparer<long>
            where TComparableData : unmanaged, IComparable<TComparableData>
        {
            // TODO: Once ref structs are available, change from pointer logic to ref field
            [NativeDisableUnsafePtrRestriction]
            readonly TComparableData* m_RangeStartValuesArrayPtr;
            [NativeDisableUnsafePtrRestriction]
            readonly TComparableData* m_RangeLengthValuesArrayPtr;
            public IndexedArrayRangeValueComparer(in DynamicArray<TComparableData> indexedRangeStartValueArray, in DynamicArray<TComparableData> indexedRangeLengthValueArray)
            {
                Checks.CheckEquals(indexedRangeStartValueArray.Count, indexedRangeLengthValueArray.Count);
                m_RangeStartValuesArrayPtr = indexedRangeStartValueArray.GetUnsafeTypedPtr();
                m_RangeLengthValuesArrayPtr = indexedRangeLengthValueArray.GetUnsafeTypedPtr();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int Compare(ref long arrayIndexA, ref long arrayIndexB)
            {
                var res = m_RangeStartValuesArrayPtr[arrayIndexA].CompareTo(m_RangeStartValuesArrayPtr[arrayIndexB]);
                if (res != 0)
                    return res;

#if ENABLE_MEMORY_PROFILER_DEBUG
                if(arrayIndexA != arrayIndexB && (m_RangeLengthValuesArrayPtr[arrayIndexA].CompareTo(default) != 0 && m_RangeLengthValuesArrayPtr[arrayIndexA].CompareTo(default) != 0))
                    // Range Items can't have the same start and end values, unless one of them is of length 0.
                    Checks.CheckNotEquals(m_RangeLengthValuesArrayPtr[arrayIndexA].CompareTo(m_RangeLengthValuesArrayPtr[arrayIndexB]), 0);
#endif
                // if the start address matches, compare their sizes and invert the result so that the end range value is used as basis for the comparison
                return -(m_RangeLengthValuesArrayPtr[arrayIndexA].CompareTo(m_RangeLengthValuesArrayPtr[arrayIndexB]));
            }
        }

        /// <summary>
        /// This struct contains all data, needed to sort an array efficiently via
        /// <see cref="IntrospectiveSort{TComparer}(long, long, ref ArraySortingData{long, TComparer})"/>
        /// including the "functional" data of how to compare the values that should be sorted.
        /// That means that sorting does sort by comparing the <typeparamref name="TValueToSort"/>
        /// elements to each other directly, but through the <typeparamref name="TComparer"/> intermediary.
        /// That intermediary could e.g. compare the <typeparamref name="TValueToSort"/> elements of the array that is being sorted directly,
        /// but it could also use reinterpret these <typeparamref name="TValueToSort"/> elements as indices into a completely different array,
        /// resolve the indices to the values in that array and compare them instead.
        ///
        /// This indirect sorting is e.g. used in <seealso cref="CachedSnapshot.IndirectlySortedEntriesCache{TSortComparer}"/>.
        /// </summary>
        /// <typeparam name="TValueToSort"></typeparam>
        /// <typeparam name="TComparer"></typeparam>
        readonly unsafe struct ArraySortingData<TValueToSort, TComparer>
            where TValueToSort : unmanaged
            where TComparer : unmanaged, IRefComparer<TValueToSort>
        {
            public readonly long Count { get; }
            public readonly ref TValueToSort this[long index] => ref m_Ptr[index];

            // TODO: Once ref structs are available, change from pointer logic to ref field
            [NativeDisableUnsafePtrRestriction]
            readonly TValueToSort* m_Ptr;

            readonly TComparer m_Comparer;

            public ArraySortingData(TValueToSort* ptrOfNativeDataToBeSorted, long count, TComparer comparer)
            {
                m_Ptr = ptrOfNativeDataToBeSorted;
                Count = count;
                m_Comparer = comparer;
            }

            /// <summary>
            /// Technically the parameters should be 'in' parameters
            /// but that would trigger safety checks to ensure that they aren't changed.
            /// Often the Values passed in will just be of pointer size so their 'ref' does not save us much here.
            ///
            /// BUT in some instances this will be structs, e.g. when sorting
            /// <seealso cref="CachedSnapshot.EntriesMemoryMapCache.AddressPoint"/>
            /// or <see cref="CachedSnapshot.ManagedMemorySectionEntriesCache.SortIndexHelper"/>.
            /// and in those cases this will safe us a copy. Though ideally the compiler will just inline this as requested.
            /// </summary>
            /// <param name="valueA"></param>
            /// <param name="valueB"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly int Compare(ref TValueToSort valueA, ref TValueToSort valueB)
            {
                return m_Comparer.Compare(ref valueA, ref valueB);
            }
        }
    }

    static class ArraySortingData<TSortComparer>
        where TSortComparer : unmanaged, IRefComparer<long>
    {
        /// <summary>
        /// This is a helper function to simplify the initialization of sort data when sorting an array of <paramref name="indices"/>
        /// with the help of an sorting <paramref name="comparer"/> like <see cref="IndexedArrayValueComparer{TComparableData}"/>
        /// that compares elements in the base data array via the indices of the <paramref name="indices"/> array that is being sorted.
        /// </summary>
        /// <param name="indices"></param>
        /// <param name="comparer"></param>
        /// <returns></returns>
        public unsafe static ArraySortingData<long, TSortComparer> GetSortDataForSortingAnIndexingArray
            (in DynamicArray<long> indices, TSortComparer comparer) =>
            new ArraySortingData<long, TSortComparer>(indices.GetUnsafeTypedPtr(), indices.Count, comparer);
    }

    internal static class DynamicArrayAlgorithms
    {
        const bool k_Debug = false;

        [BurstCompile(CompileSynchronously = true, DisableDirectCall = false, DisableSafetyChecks = !k_Debug, Debug = k_Debug)]
        public static unsafe long FindIndex<TArrayValues, TComparer>(DynamicArray<TArrayValues> array, TArrayValues value, TComparer comparer)
            where TArrayValues : unmanaged, IComparable<TArrayValues>
            where TComparer : unmanaged, IRefComparer<TArrayValues>
        {
            if (array.Count == 0)
                return ~0;

            // avoid bounds checks
            var arr = array.GetUnsafeTypedPtr();
            var length = array.Count;
            for (long i = 0; i < length; i++)
            {
                if (comparer.Compare(ref arr[i], ref value) == 0)
                    return i;
            }
            return ~0;
        }

        /// <summary>
        /// Implementation of the binary search algorithm.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="array">Pre sorted native array</param>
        /// <param name="value"></param>
        /// <remarks>Note that there are no checks in regards to the provided DynamicArray's sort state.</remarks>
        /// <returns>
        /// Index of the value. -1 (which equals ~0) means the item wasn't found and the insertion point would be 0, i.e. before the first element.
        /// <c>Index &lt; 0</c> means that no direct hit was found and that:
        /// <list type="bullet">
        ///     <item>
        ///         <description>Index is the index to the next bigger item (and would be the insertion point for an item of this <paramref name="value"/>)</description>
        ///         <item>
        ///             <description> -&gt; <c>if (~Index &gt;= array.Count)</c> there is no bigger item, ergo item not found but would be inserted at the end</description>
        ///         </item>
        ///     </item>
        ///     <item>
        ///         <description>~Index-1 is the next smaller item.</description>
        ///         <item>
        ///             <description> -&gt; Carefull: -1 (which equals to ~0) means ~Index-1 evaluates back to -1 i.e. item not found.</description>
        ///         </item>
        ///     </item>
        /// </list>
        /// </returns>
        [BurstCompile(CompileSynchronously = true, DisableDirectCall = false, DisableSafetyChecks = !k_Debug, Debug = k_Debug)]
        public static unsafe long BinarySearch<T>(DynamicArray<T> array, T value) where T : unmanaged, IComparable<T>
        {
            if (array.Count == 0)
                return ~0;

            // avoid bounds checks
            var arr = array.GetUnsafeTypedPtr();

            long left = 0;
            long right = array.Count - 1;
            while (left <= right)
            {
                long mid = left + ((right - left) >> 1);
                long cmpResult = arr[mid].CompareTo(value);

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
            // No direct hit was found but the last compared item was Bigger than the searched for item
            // this result is going to be negative but if the calling code is looking for an insertion point within the sorted array,
            // or will happily consider the item that is just slightly smaller than the searched item as the found item, it can do
            //
            // if(returnedValue < 0) var nextBiggerItemAndInsertionPoint = returnedValue = ~returnedValue;
            // or
            // if(returnedValue < 0) var nextSmallerItem = returnedValue = ~returnedValue -1;
            //
            // if that last compared item was the very first item in the array but bigger than the searched item,
            // left is 0 and there is no smaller item to insert in front of. The return value in this case is then -1
            return ~left;
        }

        /// <summary>
        /// Implementation of the binary search algorithm for ranges of values.
        /// Will find the ranges of size zero (if there are multiple with the same start value, it will return the last one).
        /// Will also always find the deepest nested region.
        /// </summary>
        /// <param name="array">Pre sorted native array</param>
        /// <param name="value"></param>
        /// <returns>
        /// Index of the value.
        /// Nb: Preceeding item(s!) could have started at the exact same value but been of a larger size, i.e. overlapped with the found range.
        /// (e.g. given the ranges of [0,2][0,1][0,0] and looking for an item that at 0 address 0 will return 2 to indicate the last range.
        /// Searching for address 1 will return the middle item at index 1.)
        ///
        /// -1 (which equals ~0) means the item wasn't found and the insertion point would be 0, i.e. before the first element.
        /// <c>Index &lt; 0</c> means that no direct hit was found and that:
        /// <list type="bullet">
        ///     <item>
        ///         <description>Index is the index to the next bigger item (and would be the insertion point for an item of this <paramref name="value"/>)</description>
        ///         <item>
        ///             <description> -&gt; <c>if (~Index &gt;= array.Count)</c> there is no bigger item, ergo item not found but would be inserted at the end</description>
        ///         </item>
        ///     </item>
        ///     <item>
        ///         <description>~Index-1 is the next smaller item.</description>
        ///         <item>
        ///             <description> -&gt; Carefull: -1 (which equals to ~0) means ~Index-1 evaluates back to -1 i.e. item not found.</description>
        ///         </item>
        ///     </item>
        /// </list>
        /// </returns>
        [BurstCompile(CompileSynchronously = true, DisableDirectCall = false, DisableSafetyChecks = !k_Debug, Debug = k_Debug)]
        public static long BinarySearch(CachedSnapshot.ISortedEntriesCache array, ulong value)
        {
            if (array.Count == 0)
                return ~0;

            long left = 0;
            long right = array.Count - 1;
            while (left <= right)
            {
                long mid = left + ((right - left) >> 1);
                long cmpResult = array.Address(mid).CompareTo(value);

                switch (cmpResult)
                {
                    case -1:
                        left = mid + 1;
                        break;
                    case 1:
                        right = mid - 1;
                        break;
                    case 0:
                        // check if there could be an enclosed region starting at the same address
                        if ((mid + 1 < array.Count && array.Address(mid + 1) == value))
                        {
                            // Keep searching. If nothing else is found, insertion point would be after this item as the fallback is to return ~left
                            left = mid + 1;
                            break;
                        }
                        return mid;
                }
            }
            // No direct hit was found but the last compared item was bigger than the searched for item
            // Check if the last item fit the search criteria
            var lastChecked = left - 1;
            if (lastChecked >= 0 && array.Address(lastChecked) + array.Size(lastChecked) < value)
                return lastChecked;
            // If it didn't, this result is going to be negative but if the calling code is looking for an insertion point within the sorted array,
            // or will happily consider the item that is just slightly smaller than the searched item as the found item, it can do
            //
            // if(returnedValue < 0) var nextBiggerItemAndInsertionPoint = returnedValue = ~returnedValue;
            // or
            // if(returnedValue < 0) var nextSmallerItem = returnedValue = ~returnedValue -1;
            //
            // if that last compared item was the very first item in the array but bigger than the searched item,
            // left is 0 and there is no smaller item to insert in front of. The return value in this case is then -1
            return ~left;
        }

        /// <summary>
        /// Port of MSDN's internal method for QuickSort, which works with <see cref="NativeArray{T}"/> and <see cref="DynamicArray{T}"/> data
        /// and is compatible with Burst.
        ///
        /// If needed, it'd also be trivial to adjust for sorting any fixed or pinned managed array of unmanaged data types.
        /// </summary>
        /// <typeparam name="TComparer"></typeparam>
        /// <param name="startIndex"></param>
        /// <param name="length"></param>
        /// <param name="comparer"></param>
        [BurstCompile(CompileSynchronously = true, DisableDirectCall = false, DisableSafetyChecks = !k_Debug, Debug = k_Debug)]
        public static unsafe void IntrospectiveSort<TComparer>(long startIndex, long length, ref ArraySortingData<long, TComparer> comparer)
            where TComparer : unmanaged, IRefComparer<long>
        {
            if (length < 0 || length > comparer.Count)
                throw new ArgumentOutOfRangeException(nameof(length), "length should be in the range [0, array.Length].");
            if (startIndex < 0 || startIndex > length - 1)
                throw new ArgumentOutOfRangeException(nameof(startIndex), "startIndex should in the range [0, length).");

            if (length < 2)
                return;

            IntroSortInternal(ref comparer, startIndex, length + startIndex - 1, GetMaxDepth(comparer.Count), GetPartitionThreshold());
        }

        /// <summary>
        /// Port of MSDN's internal method for QuickSort, which works with <see cref="NativeArray{T}"/> and <see cref="DynamicArray{T}"/> data
        /// and is compatible with Burst.
        ///
        /// If needed, it'd also be trivial to adjust for sorting any fixed or pinned managed array of unmanaged data types.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="array"></param>
        /// <param name="startIndex"></param>
        /// <param name="length"></param>
        [BurstCompile(CompileSynchronously = true, DisableDirectCall = false, DisableSafetyChecks = !k_Debug, Debug = k_Debug)]
        public static unsafe void IntrospectiveSort<T>(DynamicArray<T> array, long startIndex, long length) where T : unmanaged, IComparable<T>
        {
            if (length < 0 || length > array.Count)
                throw new ArgumentOutOfRangeException(nameof(length), "length should be in the range [0, array.Length].");
            if (startIndex < 0 || startIndex > Math.Max(0, length - 1))
                throw new ArgumentOutOfRangeException(nameof(startIndex), "startIndex should in the range [0, length).");

            if (length < 2)
                return;

            var comparer = new ArraySortingData<T, DirectValueComparer<T>>(array.GetUnsafeTypedPtr(), array.Count, new DirectValueComparer<T>());
            IntroSortInternal(ref comparer, startIndex, length + startIndex - 1, GetMaxDepth(array.Count), GetPartitionThreshold());
        }

        /// <summary>
        /// Port of MSDN's internal method for QuickSort, which works with <see cref="NativeArray{T}"/> and <see cref="DynamicArray{T}"/> data
        /// and is compatible with Burst.
        ///
        /// If needed, it'd also be trivial to adjust for sorting any fixed or pinned managed array of unmanaged data types.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="array"></param>
        /// <param name="startIndex"></param>
        /// <param name="length"></param>
        [BurstCompile(CompileSynchronously = true, DisableDirectCall = false, DisableSafetyChecks = !k_Debug, Debug = k_Debug)]
        public static unsafe void IntrospectiveSort<T>(NativeArray<T> array, int startIndex, int length) where T : unmanaged, IComparable<T>
        {
            if (length < 0 || length > array.Length)
                throw new ArgumentOutOfRangeException(nameof(length), "length should be in the range [0, array.Length].");
            if (startIndex < 0 || startIndex > Math.Max(0, length - 1))
                throw new ArgumentOutOfRangeException(nameof(startIndex), "startIndex should in the range [0, length).");

            if (length < 2)
                return;

            var comparer = new ArraySortingData<T, DirectValueComparer<T>>((T*)array.GetUnsafePtr(), array.Length, new DirectValueComparer<T>());
            IntroSortInternal(ref comparer, startIndex, length + startIndex - 1, GetMaxDepth(array.Length), GetPartitionThreshold());
        }

        /// <summary>
        /// Use this comparer in conjunction with <see cref="ArraySortingData{T, DirectValueComparer{T}}"/>
        /// to sort an array by comparing its values to each other directly.
        ///
        /// If you want to sort an array of indices into a different array instead, use <seealso cref="IndexedArrayValueComparer{T}"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        readonly struct DirectValueComparer<T> : IRefComparer<T> where T : unmanaged, IComparable<T>
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly int Compare(ref T x, ref T y)
            {
                return x.CompareTo(y);
            }
        }

        static void IntroSortInternal<T, TComparer>(ref ArraySortingData<T, TComparer> data, long low, long high, int depth, long partitionThreshold)
            where T : unmanaged
            where TComparer : unmanaged, IRefComparer<T>
        {
            while (high > low)
            {
                var partitionSize = high - low + 1;
                if (partitionSize <= partitionThreshold)
                {
                    switch (partitionSize)
                    {
                        case 1:
                            return;
                        case 2:
                            SwapIfGreater(ref data, low, high);
                            return;
                        case 3:
                            SwapSortAscending(ref data, low, high - 1, high);
                            return;
                        default:
                            InsertionSort(ref data, low, high);
                            return;
                    }
                }
                else if (depth == 0)
                {
                    Heapsort(ref data, low, high);
                    return;
                }
                --depth;

                var pivot = PartitionRangeAndPlacePivot(ref data, low, high);
                IntroSortInternal(ref data, pivot + 1, high, depth, partitionThreshold);
                high = pivot - 1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Heapsort<T, TComparer>(ref ArraySortingData<T, TComparer> data, long low, long high)
            where T : unmanaged
            where TComparer : unmanaged, IRefComparer<T>
        {
            var rangeSize = high - low + 1;
            for (var i = rangeSize / 2; i >= 1; --i)
            {
                DownHeap(ref data, i, rangeSize, low);
            }
            for (var i = rangeSize; i > 1; --i)
            {
                Swap(ref data, low, low + i - 1);

                DownHeap(ref data, 1, i - 1, low);
            }
        }

        static void DownHeap<T, TComparer>(ref ArraySortingData<T, TComparer> data, long i, long n, long low)
            where T : unmanaged
            where TComparer : unmanaged, IRefComparer<T>
        {
            // store a copy
            var tmp = data[low + i - 1];

            long child;
            while (i <= n / 2)
            {
                child = 2 * i;
                ref var cChildElement = ref data[low + child - 1];
                ref var nChildElement = ref data[low + child];

                if (child < n && data.Compare(ref cChildElement, ref nChildElement) < 0)
                {
                    ++child;
                    // NB! This is the only place in the sorting algo where we need to change the referent for a ref var
                    // i.e. we do not change the aliased array element here, but change the alias instead.
                    // Hence the 'ref' is crutial here.
                    cChildElement = ref nChildElement;
                    if (!(data.Compare(ref tmp, ref nChildElement) < 0))
                        break;
                }
                else
                {
                    if (!(data.Compare(ref tmp, ref cChildElement) < 0))
                        break;
                }

                data[low + i - 1] = cChildElement;
                i = child;
            }
            data[low + i - 1] = tmp;
        }

        static void InsertionSort<T, TComparer>(ref ArraySortingData<T, TComparer> data, long low, long high)
            where T : unmanaged
            where TComparer : unmanaged, IRefComparer<T>
        {
            long i, j;

            for (i = low; i < high; ++i)
            {
                j = i;
                // store a copy
                var tmp = data[i + 1];
                while (j >= low)
                {
                    if (!(data.Compare(ref tmp, ref data[j]) < 0))
                        break;
                    data[j + 1] = data[j];
                    j--;
                }
                data[j + 1] = tmp;
            }
        }

        static long PartitionRangeAndPlacePivot<T, TComparer>(ref ArraySortingData<T, TComparer> data, long low, long high)
            where T : unmanaged
            where TComparer : unmanaged, IRefComparer<T>
        {
            var mid = low + (high - low) / 2;

            // Sort low/high/mid in order to have the correct pivot.
            SwapSortAscending(ref data, low, mid, high);

            // store a copy
            var tmp = data[mid];

            Swap(ref data, mid, high - 1);
            long left = low, right = high - 1;

            while (left < right)
            {
                do { ++left; }
                while (data.Compare(ref data[left], ref tmp) < 0);

                do { --right; }
                while (data.Compare(ref tmp, ref data[right]) < 0);

                if (left >= right)
                    break;

                Swap(ref data, left, right);
            }

            Swap(ref data, left, (high - 1));
            return left;
        }

        static void SwapSortAscending<T, TComparer>(ref ArraySortingData<T, TComparer> data, long left, long mid, long right)
            where T : unmanaged
            where TComparer : unmanaged, IRefComparer<T>
        {
            ref var leftElement = ref data[left];
            ref var midElement = ref data[mid];
            ref var rightElement = ref data[right];

            int bitmask = 0;
            if (data.Compare(ref leftElement, ref midElement) > 0)
                bitmask = 1;
            if (data.Compare(ref leftElement, ref rightElement) > 0)
                bitmask |= 1 << 1;
            if (data.Compare(ref midElement, ref rightElement) > 0)
                bitmask |= 1 << 2;

            switch (bitmask)
            {
                case 1:
                    (leftElement, midElement) = (midElement, leftElement);
                    return;
                case 3:
                    (leftElement, midElement, rightElement) = (midElement, rightElement, leftElement);
                    return;
                case 4:
                    (rightElement, midElement) = (midElement, rightElement);
                    return;
                case 6:
                    (leftElement, rightElement, midElement) = (rightElement, midElement, leftElement);
                    return;
                case 7:
                    (rightElement, leftElement) = (leftElement, rightElement);
                    return;
                default: //we are already ordered
                    return;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SwapIfGreater<T, TComparer>(ref ArraySortingData<T, TComparer> data, long lhs, long rhs)
            where T : unmanaged
            where TComparer : unmanaged, IRefComparer<T>
        {
            if (!lhs.Equals(rhs))
            {
                ref var leftElement = ref data[lhs];
                ref var rightElement = ref data[rhs];

                if (data.Compare(ref leftElement, ref rightElement) > 0)
                {
                    (rightElement, leftElement) = (leftElement, rightElement);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Swap<T, TComparer>(ref ArraySortingData<T, TComparer> data, long lhs, long rhs)
            where T : unmanaged
            where TComparer : unmanaged, IRefComparer<T>
        {
            (data[rhs], data[lhs]) = (data[lhs], data[rhs]);
        }

        static int GetMaxDepth(long length)
        {
            return 2 * UnityEngine.Mathf.FloorToInt((float)Math.Log(length, 2));
        }

        static long GetPartitionThreshold()
        {
            return 16;
        }
    }
}

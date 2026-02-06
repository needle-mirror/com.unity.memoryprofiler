using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Containers.Unsafe;

namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
        public interface ISortedEntriesCache
        {
            void Preload();
            long Count { get; }
            ulong Address(long index);
            ulong FullSize(long index);
        }

        public abstract class IndirectlySortedEntriesCache<TSortComparer, TUnsafeCache>
            : IDisposable, ISortedEntriesCache
            where TSortComparer : unmanaged, IRefComparer<long>
            where TUnsafeCache : unmanaged, ISortedEntriesCache
        {
            protected CachedSnapshot m_Snapshot;
            protected DynamicArray<long> m_Sorting;
            protected bool Loaded => m_Loaded;
            bool m_Loaded;

            protected IndirectlySortedEntriesCache(CachedSnapshot snapshot)
            {
                m_Snapshot = snapshot;
                m_Loaded = false;
            }

            public long this[long index]
            {
                get
                {
                    if (m_Loaded)
                        return m_Sorting[index];
                    // m_Loaded is more likely than not true, but we can't tell the optimizer that in C#,
                    // so have the less likely case as separate fallback branch
                    Preload();
                    return m_Sorting[index];
                }
            }

            public abstract long Count { get; }
            protected abstract TSortComparer SortingComparer { get; }
            unsafe ArraySortingData<long, TSortComparer> Comparer =>
                ArraySortingData<TSortComparer>.GetSortDataForSortingAnIndexingArray(in m_Sorting, SortingComparer);

            public abstract ulong Address(long index);

            public virtual void Preload()
            {
                if (!m_Sorting.IsCreated)
                {
                    m_Sorting = new DynamicArray<long>(Count, Allocator.Persistent);
                    var count = m_Sorting.Count;
                    for (long i = 0; i < count; ++i)
                        m_Sorting[i] = i;
                    if (count > 0)
                    {
                        var comparer = Comparer;
                        DynamicArrayAlgorithms.IntrospectiveSort(0, Count, ref comparer);
                    }
                }
                m_Loaded = true;
            }

            public abstract ulong FullSize(long index);

            public virtual void Dispose()
            {
                m_Sorting.Dispose();
            }

            /// <summary>
            /// A burst compatible way to access the sorted data. It is not safe to use if the holding object has been disposed.
            /// </summary>
            public virtual TUnsafeCache UnsafeCache { get; }

            /// <summary>
            /// Uses <see cref="DynamicArrayAlgorithms.BinarySearch"/> to quickly find an item
            /// which has a start <see cref="Address(long)"/> matching the provided <paramref name="address"/>,
            /// or, if <paramref name="onlyDirectAddressMatches"/> is false, where the <paramref name="address"/> falls between
            /// the items start <see cref="Address(long)"/> and last address (using <see cref="FullSize(long)"/>).
            ///
            /// If there are 0 sized items at the address, the last item will be returned.
            /// </summary>
            /// <param name="address"></param>
            /// <param name="onlyDirectAddressMatches"></param>
            /// <remarks> CAUTION: For data where regions can overlap, e.g. <seealso cref="SortedNativeMemoryRegionEntriesCache"/>
            /// this will find the deepest nested region, not any potential enclosing regions.</remarks>
            /// <returns> Index of the value within the <seealso cref="IndirectlySortedEntriesCache"/>.
            /// -1 means the item wasn't found.</returns>
            public long Find(ulong address, bool onlyDirectAddressMatches)
            {
                var idx = DynamicArrayAlgorithms.BinarySearch(this.UnsafeCache, address);
                if (idx < 0)
                {
                    // -1 means the address is smaller than the first Address, early out with -1
                    if (idx == -1 || onlyDirectAddressMatches)
                        return -1;
                    // otherwise, a negative Index just means there was no match of the any address range (yet matching with a range of size 0 if the address matches)
                    // and ~idx - 1 will give us the index to the next smaller Address
                    idx = ~idx - 1;
                }
                var foundAddress = Address(idx);
                if (address == foundAddress)
                    return idx;
                if (onlyDirectAddressMatches)
                    return -1;
                var size = FullSize(idx);
                if (address > foundAddress && (address < (foundAddress + size) || size == 0))
                {
                    return idx;
                }
                return -1;
            }
        }

        /// <summary>
        /// Used for entry caches that don't have any overlaps and no items of size 0 (or if, not right next to each other,
        /// i.e. <see cref="SortedNativeObjects"/> are fine to use this instead of <see cref="IndirectlySortedEntriesCacheSortedByAddressAndSizeArray"/>
        /// as while some Native Objects may report a size of 0, their addresses will never match
        /// </summary>
        public abstract class IndirectlySortedEntriesCacheSortedByAddressArray<TUnsafeCache> : IndirectlySortedEntriesCache<IndexedArrayValueComparer<ulong>, TUnsafeCache>
            where TUnsafeCache : unmanaged, ISortedEntriesCache
        {
            protected unsafe override IndexedArrayValueComparer<ulong> SortingComparer =>
                new IndexedArrayValueComparer<ulong>(in Addresses);
            public IndirectlySortedEntriesCacheSortedByAddressArray(CachedSnapshot snapshot) : base(snapshot) { }
            protected abstract ref readonly DynamicArray<ulong> Addresses { get; }
            public override ulong Address(long index) => Addresses[this[index]];
        }

        /// <summary>
        /// Used for entry caches that can have overlapping regions or those which border right next to each other while having sizes of 0
        /// </summary>
        public abstract class IndirectlySortedEntriesCacheSortedByAddressAndSizeArray<TUnsafeCache> : IndirectlySortedEntriesCache<IndexedArrayRangeValueComparer<ulong>, TUnsafeCache>
            where TUnsafeCache : unmanaged, ISortedEntriesCache
        {
            protected unsafe override IndexedArrayRangeValueComparer<ulong> SortingComparer =>
                new IndexedArrayRangeValueComparer<ulong>(in Addresses, in Sizes, AllowExactlyOverlappingRegions);
            protected virtual bool AllowExactlyOverlappingRegions => false;
            public IndirectlySortedEntriesCacheSortedByAddressAndSizeArray(CachedSnapshot snapshot) : base(snapshot) { }
            protected abstract ref readonly DynamicArray<ulong> Addresses { get; }
            protected abstract ref readonly DynamicArray<ulong> Sizes { get; }
            public override ulong Address(long index) => Addresses[this[index]];
            public override ulong FullSize(long index) => Sizes[this[index]];
        }

        /// <summary>
        /// Used for entry caches that can have overlapping regions or those which border right next to each other while having sizes of 0
        /// </summary>
        public abstract class IndirectlySortedEntriesCacheSortedByAddressAndSizeArrayWithCache : IndirectlySortedEntriesCacheSortedByAddressAndSizeArray<UnsafeAddressAndSizeCache>
        {
            public IndirectlySortedEntriesCacheSortedByAddressAndSizeArrayWithCache(CachedSnapshot snapshot) : base(snapshot) { }

            public override void Preload()
            {
                var wasLoaded = Loaded;
                base.Preload();
                if (!wasLoaded)
                {
                    m_UnsafeCache = new UnsafeAddressAndSizeCache(m_Sorting, Addresses, Sizes);
                }
            }
            UnsafeAddressAndSizeCache m_UnsafeCache;
            public override UnsafeAddressAndSizeCache UnsafeCache
            {
                get
                {
                    if (Loaded)
                        return m_UnsafeCache;
                    Preload();
                    return m_UnsafeCache;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public readonly struct UnsafeAddressAndSizeCache : CachedSnapshot.ISortedEntriesCache
        {
            public long Count => m_Addresses.Count;

            public long this[long index] => m_Sorting[index];
            readonly DynamicArray<long> m_Sorting;
            readonly DynamicArray<ulong> m_Addresses;
            readonly DynamicArray<ulong> m_Sizes;
            public ulong Address(long index) => m_Addresses[this[index]];
            public ulong FullSize(long index) => m_Sizes[this[index]];

            public UnsafeAddressAndSizeCache(DynamicArray<long> sorting, DynamicArray<ulong> addresses, DynamicArray<ulong> sizes)
            {
                m_Sorting = sorting;
                m_Addresses = addresses;
                m_Sizes = sizes;
            }
            // already preloaded
            public void Preload() { }
        }
    }
}

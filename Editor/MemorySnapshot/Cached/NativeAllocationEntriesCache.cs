using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
        public class NativeAllocationEntriesCache : IDisposable
        {
            public long Count;
            public DynamicArray<int> MemoryRegionIndex = default;
            /// <summary>
            /// Note: Reference ID 0 means the allocation was not rooted to anything, not that it was rooted to "System : ExecutableAndDlls"
            /// </summary>
            public DynamicArray<long> RootReferenceId = default;
            public DynamicArray<ulong> Address = default;
            public DynamicArray<ulong> Size = default;
            public DynamicArray<int> OverheadSize = default;
            public DynamicArray<int> PaddingSize = default;
            public DynamicArray<ulong> AllocationSiteId = default;

            public NativeAllocationEntriesCache(ref IFileReader reader, bool allocationSites /*do not read allocation sites if they aren't present*/)
            {
                Count = reader.GetEntryCount(EntryType.NativeAllocations_Address);

                if (Count == 0)
                    return;

                MemoryRegionIndex = reader.Read(EntryType.NativeAllocations_MemoryRegionIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                RootReferenceId = reader.Read(EntryType.NativeAllocations_RootReferenceId, 0, Count, Allocator.Persistent).Result.Reinterpret<long>();
                Address = reader.Read(EntryType.NativeAllocations_Address, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                Size = reader.Read(EntryType.NativeAllocations_Size, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                OverheadSize = reader.Read(EntryType.NativeAllocations_OverheadSize, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                PaddingSize = reader.Read(EntryType.NativeAllocations_PaddingSize, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();

                if (allocationSites)
                    AllocationSiteId = reader.Read(EntryType.NativeAllocations_AllocationSiteId, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
            }

            public void Dispose()
            {
                Count = 0;
                MemoryRegionIndex.Dispose();
                RootReferenceId.Dispose();
                Address.Dispose();
                Size.Dispose();
                OverheadSize.Dispose();
                PaddingSize.Dispose();
                AllocationSiteId.Dispose();
            }

            public string ProduceAllocationNameForAllocation(CachedSnapshot snapshot, long allocationIndex, bool higlevelObjectNameOnlyIfAvailable = true, bool ignoreNativeObjectName = false)
            {
                // Check if we have memory label roots information
                if (snapshot.NativeAllocations.RootReferenceId.Count <= 0)
                    return InvalidItemName;

                // Check if allocation has memory label root
                var rootReferenceId = snapshot.NativeAllocations.RootReferenceId[allocationIndex];
                if (rootReferenceId <= 0)
                    return InvalidItemName;
                return ProduceAllocationNameForRootReferenceId(snapshot, rootReferenceId, higlevelObjectNameOnlyIfAvailable, ignoreNativeObjectName);
            }

            public string ProduceAllocationNameForRootReferenceId(CachedSnapshot snapshot, long rootReferenceId, bool higlevelObjectNameOnlyIfAvailable = true, bool ignoreNativeObjectName = false)
            {
                var nativeObjectName = String.Empty;
                // Lookup native object index associated with memory label root
                if (!ignoreNativeObjectName && snapshot.NativeObjects.RootReferenceIdToIndex.TryGetValue(rootReferenceId, out var objectIndex))
                {
                    if (higlevelObjectNameOnlyIfAvailable)
                        return snapshot.NativeObjects.ObjectName[objectIndex];
                    else
                        nativeObjectName = snapshot.NativeObjects.ObjectName[objectIndex];
                }

                // Try to see if memory label root is associated with any memory area
                if (snapshot.NativeRootReferences.IdToIndex.TryGetValue(rootReferenceId, out long rootIndex))
                {
                    var allocationObjectName = snapshot.NativeRootReferences.ObjectName[rootIndex];
                    return snapshot.NativeRootReferences.AreaName[rootIndex] + (string.IsNullOrEmpty(allocationObjectName) ? "" : (":" + allocationObjectName)) + (string.IsNullOrEmpty(nativeObjectName) || allocationObjectName == nativeObjectName ? "" : $" \"{nativeObjectName}\"");
                }

                return InvalidItemName;
            }
        }

        public class SortedNativeAllocationsCache : IndirectlySortedEntriesCacheSortedByAddressAndSizeArray<SortedNativeAllocationsCache.UnsafeNativeAllocationsCache>
        {
            public SortedNativeAllocationsCache(CachedSnapshot snapshot) : base(snapshot) { }

            public override long Count => m_Snapshot.NativeAllocations.Count;

            protected override ref readonly DynamicArray<ulong> Addresses => ref m_Snapshot.NativeAllocations.Address;
            protected override ref readonly DynamicArray<ulong> Sizes => ref m_Snapshot.NativeAllocations.Size;

            // The allocation start address discounts padding and overhead sizes used for Allocation headers
            // (overhead size also includes potential footers but those don't matter for the start address)
            // in order to find allocations based on pointers into the allocation, we therefore need to consider the full size of the allocation.
            public override ulong FullSize(long index) => m_Snapshot.NativeAllocations.Size[this[index]] + (ulong)m_Snapshot.NativeAllocations.OverheadSize[this[index]] + (ulong)m_Snapshot.NativeAllocations.PaddingSize[this[index]];

            public int MemoryRegionIndex(long index) => m_Snapshot.NativeAllocations.MemoryRegionIndex[this[index]];
            public long RootReferenceId(long index) => m_Snapshot.NativeAllocations.RootReferenceId[this[index]];
            public ulong AllocationSiteId(long index) => m_Snapshot.NativeAllocations.AllocationSiteId[this[index]];
            public int OverheadSize(long index) => m_Snapshot.NativeAllocations.OverheadSize[this[index]];
            public int PaddingSize(long index) => m_Snapshot.NativeAllocations.PaddingSize[this[index]];

            public override void Preload()
            {
                var wasLoaded = Loaded;
                base.Preload();
                if (!wasLoaded)
                {
                    m_UnsafeCache = new UnsafeNativeAllocationsCache(m_Sorting, Addresses, Sizes, m_Snapshot.NativeAllocations.OverheadSize, m_Snapshot.NativeAllocations.PaddingSize);
                }
            }
            UnsafeNativeAllocationsCache m_UnsafeCache;
            public override UnsafeNativeAllocationsCache UnsafeCache
            {
                get
                {
                    if (Loaded)
                        return m_UnsafeCache;
                    Preload();
                    return m_UnsafeCache;
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            public readonly struct UnsafeNativeAllocationsCache : CachedSnapshot.ISortedEntriesCache
            {
                public long Count => m_Addresses.Count;

                public long this[long index] => m_Sorting[index];
                readonly DynamicArray<long> m_Sorting;
                readonly DynamicArray<ulong> m_Addresses;
                readonly DynamicArray<ulong> m_Sizes;
                readonly DynamicArray<int> m_OverheadSizes;
                readonly DynamicArray<int> m_PaddingSizes;
                public ulong Address(long index) => m_Addresses[this[index]];
                public ulong FullSize(long index) => m_Sizes[this[index]] + (ulong)m_OverheadSizes[this[index]] + (ulong)m_PaddingSizes[this[index]];

                public UnsafeNativeAllocationsCache(DynamicArray<long> sorting, DynamicArray<ulong> addresses, DynamicArray<ulong> sizes, DynamicArray<int> overheadSizes, DynamicArray<int> paddingSizes)
                {
                    m_Sorting = sorting;
                    m_Addresses = addresses;
                    m_Sizes = sizes;
                    m_OverheadSizes = overheadSizes;
                    m_PaddingSizes = paddingSizes;
                }
                // already preloaded
                public void Preload() { }
            }
        }
    }
}

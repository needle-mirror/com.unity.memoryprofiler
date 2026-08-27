using System;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
        public class SystemMemoryRegionEntriesCache : IDisposable
        {
            public enum MemoryType : ushort
            {
                // NB!: The same as in SystemInfoMemory.h
                Private = 0, // Private to this process allocations
                Mapped = 1,  // Allocations mapped to a file (dll/exe/etc)
                Shared = 2,  // Shared memory
                Device = 3,   // Shared device or driver memory (like GPU, sound cards, etc)
                Count
            }

            public long Count;
            public string[] RegionName;
            /// <summary>
            /// The address at which the system memory region starts. Regions that have been merged with a previous region will have their address set to 0.
            /// </summary>
            public DynamicArray<ulong> RegionAddress = default;
            /// <summary>
            /// The size of the system memory region in bytes. Regions that have been merged with a previous region will have their size set to 0.
            /// </summary>
            public DynamicArray<ulong> RegionSize = default;
            /// <summary>
            /// The resident memory amount of this region in bytes. Regions that have been merged with a previous region will have their resident memory set to 0.
            /// </summary>
            public DynamicArray<ulong> RegionResident = default;
            public DynamicArray<MemoryType> RegionType = default;
            /// <summary>
            /// Indices of regions that have been merged with their previous regions.
            /// Item1 is the index that was extended, and Item2 is the index of the region that was merged into the previous one.
            /// </summary>
            public DynamicArray<(long, long)> RegionStitched = default;
            public readonly long ExecutableAndDllsCount;

            public SystemMemoryRegionEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.SystemMemoryRegions_Address);
                RegionName = new string[Count];

                if (Count == 0)
                {
                    RegionAddress = new DynamicArray<ulong>(0, Allocator.Persistent);
                    RegionSize = new DynamicArray<ulong>(0, Allocator.Persistent);
                    RegionResident = new DynamicArray<ulong>(0, Allocator.Persistent);
                    RegionType = new DynamicArray<MemoryType>(0, Allocator.Persistent);
                    RegionStitched = new DynamicArray<(long, long)>(0, Allocator.Persistent);
                    return;
                }

                RegionAddress = reader.Read(EntryType.SystemMemoryRegions_Address, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RegionSize = reader.Read(EntryType.SystemMemoryRegions_Size, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RegionResident = reader.Read(EntryType.SystemMemoryRegions_Resident, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RegionType = reader.Read(EntryType.SystemMemoryRegions_Type, 0, Count, Allocator.Persistent).Result.Reinterpret<MemoryType>();
                RegionStitched = new DynamicArray<(long, long)>(0, 20, Allocator.Persistent);

                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.SystemMemoryRegions_Name, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.SystemMemoryRegions_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref RegionName);
                }

                var endAddress = RegionAddress[0] + RegionSize[0];
                for (long i = 1; i < Count; i++)
                {
                    var regionEndAddress = RegionAddress[i] + RegionSize[i];
                    if (RegionAddress[i] == endAddress && RegionType[i] == RegionType[i - 1] && RegionName[i] == RegionName[i - 1])
                    {
                        var previousIndex = i - 1L;
                        if (RegionStitched.Count > 0 && RegionStitched[RegionStitched.Count - 1].Item2 == previousIndex)
                        {
                            previousIndex = RegionStitched[RegionStitched.Count - 1].Item1;
                        }
                        RegionSize[previousIndex] += RegionSize[i];
                        RegionResident[previousIndex] += RegionResident[i];

                        RegionStitched.Push((previousIndex, i));
                        RegionAddress[i] = 0;
                        RegionSize[i] = 0;
                        RegionResident[i] = 0;
                    }
                    else if (RegionType[i] == MemoryType.Mapped)
                    {
                        ++ExecutableAndDllsCount;
                    }
                    endAddress = regionEndAddress;
                }
            }

            public void Dispose()
            {
                Count = 0;
                RegionName = null;
                RegionAddress.Dispose();
                RegionSize.Dispose();
                RegionResident.Dispose();
                RegionType.Dispose();
                RegionStitched.Dispose();
            }
        }

        /// <summary>
        /// Address-and-size sorted view over <see cref="SystemMemoryRegionEntriesCache"/>, enabling a
        /// binary-search <see cref="IndirectlySortedEntriesCache{TSortComparer,TUnsafeCache}.Find"/> from an
        /// arbitrary address to the system region that contains it (e.g. to map a managed type's static-field
        /// storage address to its region for resident-page lookup).
        /// Merged-away regions (address and size zeroed during caching) sort to the front and are never
        /// matched by a non-zero address, so they don't interfere with lookups.
        /// </summary>
        public class SortedSystemMemoryRegionEntriesCache : IndirectlySortedEntriesCacheSortedByAddressAndSizeArrayWithCache
        {
            public SortedSystemMemoryRegionEntriesCache(CachedSnapshot snapshot) : base(snapshot) { }

            public override long Count => m_Snapshot.SystemMemoryRegions.Count;

            // Stitching can leave several zeroed (address 0, size 0) entries that "exactly overlap" at 0.
            protected override bool AllowExactlyOverlappingRegions => true;
            protected override ref readonly DynamicArray<ulong> Addresses => ref m_Snapshot.SystemMemoryRegions.RegionAddress;
            protected override ref readonly DynamicArray<ulong> Sizes => ref m_Snapshot.SystemMemoryRegions.RegionSize;

            public SystemMemoryRegionEntriesCache.MemoryType RegionType(long index) => m_Snapshot.SystemMemoryRegions.RegionType[this[index]];
            public string Name(long index) => m_Snapshot.SystemMemoryRegions.RegionName[this[index]];
        }
    }
}

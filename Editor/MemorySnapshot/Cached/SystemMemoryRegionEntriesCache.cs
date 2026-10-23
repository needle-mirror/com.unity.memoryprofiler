using System;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using UnityEngine;

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
            /// A region that was merged into the region it was exactly adjacent to.
            /// </summary>
            public readonly struct StitchedRegion
            {
                /// <summary>
                /// The index of the region that was extended.
                /// </summary>
                public readonly long ExtendedIndex;
                /// <summary>
                /// The index of the region that was merged into <see cref="ExtendedIndex"/>.
                /// </summary>
                public readonly long MergedIndex;

                public StitchedRegion(long extendedIndex, long mergedIndex)
                {
                    ExtendedIndex = extendedIndex;
                    MergedIndex = mergedIndex;
                }
            }

            /// <summary>
            /// A region whose address range was fully contained within a same-type neighboring region and was folded away as a duplicate.
            /// </summary>
            public readonly struct ContainedRegion
            {
                /// <summary>
                /// The index of the surviving (kept) region.
                /// </summary>
                public readonly long KeptIndex;
                /// <summary>
                /// The index of the region that was folded away.
                /// </summary>
                public readonly long MergedAwayIndex;
                /// <summary>
                /// <c>RegionAddress[MergedAwayIndex] - RegionAddress[KeptIndex]</c> as captured at merge time, before either address was zeroed
                /// (needed to align residency data for the two rows - see <see cref="SystemMemoryResidentPagesEntriesCache"/>).
                /// </summary>
                public readonly long AddressDelta;

                public ContainedRegion(long keptIndex, long mergedAwayIndex, long addressDelta)
                {
                    KeptIndex = keptIndex;
                    MergedAwayIndex = mergedAwayIndex;
                    AddressDelta = addressDelta;
                }
            }

            /// <summary>
            /// A region that partially, but not fully, overlapped a same-type neighboring region and had its tail trimmed away
            /// as a stale duplicate of the neighbor's later reading.
            /// </summary>
            public readonly struct TrimmedRegion
            {
                /// <summary>
                /// The index of the trimmed (older, lower raw index) region.
                /// </summary>
                public readonly long TrimmedIndex;
                /// <summary>
                /// The index of the neighboring region whose start dictated the trim (newer, higher raw index).
                /// </summary>
                public readonly long NewerIndex;
                /// <summary>
                /// The number of bytes removed from the tail of <see cref="TrimmedIndex"/>
                /// (needed to shrink residency data for the trimmed row - see <see cref="SystemMemoryResidentPagesEntriesCache"/>).
                /// </summary>
                public readonly long RemovedByteCount;

                public TrimmedRegion(long trimmedIndex, long newerIndex, long removedByteCount)
                {
                    TrimmedIndex = trimmedIndex;
                    NewerIndex = newerIndex;
                    RemovedByteCount = removedByteCount;
                }
            }

            /// <summary>
            /// Regions that have been merged with their previous regions.
            /// </summary>
            public DynamicArray<StitchedRegion> RegionStitched = default;
            /// <summary>
            /// Regions whose address range was found to be fully contained within a same-type
            /// neighboring region (rather than merely adjacent to it) and were folded away as a duplicate.
            /// </summary>
            public DynamicArray<ContainedRegion> RegionMergedByContainment = default;
            /// <summary>
            /// Regions that partially, but not fully, overlapped a same-type neighboring region
            /// (neither containing nor contained by it) and had their tail trimmed away as a stale duplicate
            /// of the neighbor's later reading.
            /// </summary>
            public DynamicArray<TrimmedRegion> RegionTrimmedForOverlap = default;
            public readonly long ExecutableAndDllsCount;

            public SystemMemoryRegionEntriesCache(ref IFileReader reader, RuntimePlatform capturePlatform, bool captureHasUdsPoolAndReservationHints)
            {
                Count = reader.GetEntryCount(EntryType.SystemMemoryRegions_Address);
                RegionName = new string[Count];

                if (Count == 0)
                {
                    RegionAddress = new DynamicArray<ulong>(0, Allocator.Persistent);
                    RegionSize = new DynamicArray<ulong>(0, Allocator.Persistent);
                    RegionResident = new DynamicArray<ulong>(0, Allocator.Persistent);
                    RegionType = new DynamicArray<MemoryType>(0, Allocator.Persistent);
                    RegionStitched = new DynamicArray<StitchedRegion>(0, Allocator.Persistent);
                    RegionMergedByContainment = new DynamicArray<ContainedRegion>(0, Allocator.Persistent);
                    RegionTrimmedForOverlap = new DynamicArray<TrimmedRegion>(0, Allocator.Persistent);
                    return;
                }

                RegionAddress = reader.Read(EntryType.SystemMemoryRegions_Address, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RegionSize = reader.Read(EntryType.SystemMemoryRegions_Size, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RegionResident = reader.Read(EntryType.SystemMemoryRegions_Resident, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RegionType = reader.Read(EntryType.SystemMemoryRegions_Type, 0, Count, Allocator.Persistent).Result.Reinterpret<MemoryType>();
                RegionStitched = new DynamicArray<StitchedRegion>(0, 20, Allocator.Persistent);
                RegionMergedByContainment = new DynamicArray<ContainedRegion>(0, 20, Allocator.Persistent);
                RegionTrimmedForOverlap = new DynamicArray<TrimmedRegion>(0, 20, Allocator.Persistent);

                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.SystemMemoryRegions_Name, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.SystemMemoryRegions_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref RegionName);
                }

                ExecutableAndDllsCount = MergeAdjacentAndContainedRegions(RegionAddress, RegionSize, RegionResident, RegionType, RegionName, Count, ref RegionStitched, ref RegionMergedByContainment, ref RegionTrimmedForOverlap, capturePlatform, captureHasUdsPoolAndReservationHints);
            }

            /// <summary>
            /// Compares each region against the current region (a single entry or a stitched chain) and folds away duplicate reports:
            /// <list type="bullet">
            /// <item>Exactly adjacent, same type and name: one mapping split into several entries, so the sizes are summed (<paramref name="stitched"/>).</item>
            /// <item>Same type, one range fully inside the other: the same mapping reported twice while it resized mid-capture. The larger one is kept as-is (<paramref name="containedAway"/>).</item>
            /// <item>Same type, partially overlapping: regions are reported in one sequential pass, so the higher index is the fresher reading.
            /// The current region's tail is trimmed to where the newer one starts (<paramref name="trimmedForOverlap"/>).</item>
            /// </list>
            /// Overlaps of different types, or involving a UDS pool reservation (see <c>EntriesMemoryMapCache.IsKnownUdsReservationWrap</c>),
            /// are genuine structure and are left for <see cref="EntriesMemoryMapCache.PostProcess"/> to handle.
            /// See InternalDocs~/SnapshotData.md, "System memory regions".
            /// </summary>
            internal static long MergeAdjacentAndContainedRegions(
                DynamicArray<ulong> address,
                DynamicArray<ulong> size,
                DynamicArray<ulong> resident,
                DynamicArray<MemoryType> type,
                string[] name,
                long count,
                ref DynamicArray<StitchedRegion> stitched,
                ref DynamicArray<ContainedRegion> containedAway,
                ref DynamicArray<TrimmedRegion> trimmedForOverlap,
                RuntimePlatform capturePlatform = PlatformsHelper.UnknownPlatform,
                bool captureHasUdsPoolAndReservationHints = false)
            {
                long executableAndDllsCount = 0;
                if (count == 0)
                    return executableAndDllsCount;

                var currentIndex = 0L;
                var currentEnd = address[0] + size[0];
                // Resident page windows are laid out in raw index order, so a region can only be stitched onto the current one
                // while no folded-away region lies between them.
                var canStitchToCurrent = true;
                for (long nextIndex = 1; nextIndex < count; nextIndex++)
                {
                    var nextAddress = address[nextIndex];
                    var nextEnd = nextAddress + size[nextIndex];
                    var currentAddress = address[currentIndex];

                    if (canStitchToCurrent && nextAddress == currentEnd && type[nextIndex] == type[currentIndex] && name[nextIndex] == name[currentIndex])
                    {
                        size[currentIndex] += size[nextIndex];
                        resident[currentIndex] += resident[nextIndex];

                        stitched.Push(new StitchedRegion(currentIndex, nextIndex));
                        address[nextIndex] = 0;
                        size[nextIndex] = 0;
                        resident[nextIndex] = 0;
                        currentEnd = nextEnd;
                        continue;
                    }

                    var overlaps = nextAddress < currentEnd && nextEnd > currentAddress;
                    if (overlaps && type[nextIndex] == type[currentIndex]
                        && !EntriesMemoryMapCache.IsKnownUdsReservationWrap(capturePlatform, captureHasUdsPoolAndReservationHints, size[currentIndex], type[currentIndex])
                        && !EntriesMemoryMapCache.IsKnownUdsReservationWrap(capturePlatform, captureHasUdsPoolAndReservationHints, size[nextIndex], type[nextIndex]))
                    {
                        var nextContainsCurrent = nextAddress <= currentAddress && nextEnd >= currentEnd;
                        var currentContainsNext = currentAddress <= nextAddress && currentEnd >= nextEnd;

                        if (nextContainsCurrent || currentContainsNext)
                        {
                            // Identical ranges keep the later entry, otherwise the larger one is kept.
                            var isExactTie = nextContainsCurrent && currentContainsNext;
                            var keepIndex = isExactTie || !currentContainsNext ? nextIndex : currentIndex;
                            var mergeIndex = keepIndex == nextIndex ? currentIndex : nextIndex;

                            containedAway.Push(new ContainedRegion(keepIndex, mergeIndex, (long)address[mergeIndex] - (long)address[keepIndex]));
                            address[mergeIndex] = 0;
                            size[mergeIndex] = 0;
                            resident[mergeIndex] = 0;

                            if (keepIndex == nextIndex)
                            {
                                currentIndex = nextIndex;
                                currentEnd = nextEnd;
                                canStitchToCurrent = true;
                            }
                            else
                            {
                                canStitchToCurrent = false;
                            }
                            continue;
                        }

                        // Only the observed shape (next region starting inside the current one) is trimmed, the reverse is left to PostProcess.
                        if (nextAddress > currentAddress)
                        {
                            var removedByteCount = (long)(currentEnd - nextAddress);
                            size[currentIndex] -= (ulong)removedByteCount;
                            // Without page-level resident data (e.g. format v16) resident isn't re-derived, so clamp it to the trimmed size.
                            if (resident[currentIndex] > size[currentIndex])
                                resident[currentIndex] = size[currentIndex];
                            trimmedForOverlap.Push(new TrimmedRegion(currentIndex, nextIndex, removedByteCount));

                            currentIndex = nextIndex;
                            currentEnd = nextEnd;
                            canStitchToCurrent = true;
                            continue;
                        }
                    }

                    if (type[nextIndex] == MemoryType.Mapped)
                    {
                        ++executableAndDllsCount;
                    }
                    currentIndex = nextIndex;
                    currentEnd = nextEnd;
                    canStitchToCurrent = true;
                }

                return executableAndDllsCount;
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
                RegionMergedByContainment.Dispose();
                RegionTrimmedForOverlap.Dispose();
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

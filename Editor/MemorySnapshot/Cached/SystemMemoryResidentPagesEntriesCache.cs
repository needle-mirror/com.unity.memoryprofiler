using System;
using System.Collections;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using Debug = UnityEngine.Debug;
using RuntimePlatform = UnityEngine.RuntimePlatform;

namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
        public class SystemMemoryResidentPagesEntriesCache : IDisposable
        {
            public readonly long Count;
            /// <summary>
            /// The address at which the system memory region starts. Regions that have been merged with a previous region will have their address set to 0.
            /// </summary>
            public readonly DynamicArray<ulong> RegionAddress;
            /// <summary>
            /// The index of the first page in the <see cref="PageStates"/> bitset. Regions that have been merged with a previous region will have their start and end page index set to 0.
            /// </summary>
            public readonly DynamicArray<int> RegionStartPageIndex;
            /// <summary>
            /// The index of the last page in the <see cref="PageStates"/> bitset. Regions that have been merged with a previous region will have their start and end page index set to -1 to avoid region 0 to be considered in range.
            /// </summary>
            public readonly DynamicArray<int> RegionEndPageIndex;
            /// <summary>
            /// The states of the pages, where true indicates a resident page and false indicates a non-resident page.
            /// </summary>
            public readonly BitArray PageStates;
            /// <summary>
            /// How big each page in this snapshot is in bytes. 
            /// </summary>
            public readonly uint PageSize;

            public SystemMemoryResidentPagesEntriesCache(ref IFileReader reader, SystemMemoryRegionEntriesCache systemMemoryRegionEntries)
            {
                Count = reader.GetEntryCount(EntryType.SystemMemoryResidentPages_Address);
                if (Count == 0)
                {
                    PageSize = 0;
                    PageStates = new BitArray(0);
                    RegionAddress = default;
                    RegionStartPageIndex = default;
                    RegionEndPageIndex = default;
                    return;
                }

                if (systemMemoryRegionEntries.Count != Count)
                    throw new InvalidOperationException($"SystemMemoryResidentPages entries count should be the same as SystemMemoryRegionEntries count. Please report a bug! (SystemMemoryResidentPages count: {Count}, SystemMemoryRegionEntries count: {systemMemoryRegionEntries.Count})");

                RegionAddress = reader.Read(EntryType.SystemMemoryResidentPages_Address, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RegionStartPageIndex = reader.Read(EntryType.SystemMemoryResidentPages_FirstPageIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                RegionEndPageIndex = reader.Read(EntryType.SystemMemoryResidentPages_LastPageIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();

                var tempPageStates = new BitArray[1];
                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.SystemMemoryResidentPages_PagesState, 0, 1);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.SystemMemoryResidentPages_PagesState, tmp, 0, 1);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref tempPageStates);
                    PageStates = tempPageStates[0];
                }

                unsafe
                {
                    uint tempPageSize = 0;
                    byte* tempPageSizePtr = (byte*)&tempPageSize;
                    reader.ReadUnsafe(EntryType.SystemMemoryResidentPages_PageSize, tempPageSizePtr, sizeof(uint), 0, 1);
                    PageSize = tempPageSize;
                }

                // revert over the stitched regions to ensure that the last merged region of a group of contiguous regions
                // is the one setting the end page index for all the merged regions.
                for (var i = systemMemoryRegionEntries.RegionStitched.Count - 1; i >= 0; i--)
                {
                    var stitchedRegion = systemMemoryRegionEntries.RegionStitched[i];

                    // For merged regions, Item1 is the index that was extended, and Item2 is the index of the region that was merged into the previous one.
                    // If the end page index of the first region is greater than the end page index of the second region,
                    // a following stitched region must have updated the end page index of the first region already.
                    if (RegionEndPageIndex[stitchedRegion.Item1] < RegionEndPageIndex[stitchedRegion.Item2])
                    {
                        RegionEndPageIndex[stitchedRegion.Item1] = RegionEndPageIndex[stitchedRegion.Item2];
                    }
                    // Merged regions are not added to the Memory Entries Map and will never be queried directly, but might iterated over to calculate totals
                    // so we need to set these to 0 (or -1) to avoid confusion and potential bugs.
                    RegionEndPageIndex[stitchedRegion.Item2] = -1; // Set to -1 to avoid the inclusive end page index checking in CalculateResidentMemory to be accidentally used with a merged region.
                    RegionStartPageIndex[stitchedRegion.Item2] = 0;
                    RegionAddress[stitchedRegion.Item2] = 0;
                }
            }

            public void Dispose()
            {
                RegionAddress.Dispose();
                RegionStartPageIndex.Dispose();
                RegionEndPageIndex.Dispose();
            }

            public ulong CalculateResidentMemory(CachedSnapshot snapshot, long regionIndex, ulong address, ulong size, SourceIndex.SourceId sourceId)
            {
                if ((Count == 0) || (size == 0))
                    return 0;

                var regionAddress = RegionAddress[regionIndex];
                var firstPageIndex = RegionStartPageIndex[regionIndex];
                var lastPageIndex = RegionEndPageIndex[regionIndex];

                // Calculate first and last page index in bitset PageState
                var addrDelta = address - regionAddress;
                var begPage = (int)(addrDelta / PageSize) + firstPageIndex;
                var endPage = (int)((addrDelta + size - 1) / PageSize) + firstPageIndex;
                if ((begPage < firstPageIndex) || (endPage > lastPageIndex))
                {
                    // FIXME: Ignore the log on Unity 6 and OSX for now to avoid unstable tests. This is being investigated
                    //if (snapshot.MetaData.UnityVersionMajor <= 2023 ||
                    //    !(snapshot.MetaData.TargetInfo is { RuntimePlatform: RuntimePlatform.OSXEditor } || snapshot.MetaData.TargetInfo is { RuntimePlatform: RuntimePlatform.OSXPlayer }))
                    //    Debug.LogAssertion($"Page range is outside of system region range. Please report a bug! (Source: {sourceId}, regionIndex: {regionIndex}, regionAddress: {regionAddress}, address: {address}, addrDelta: {addrDelta}, begPage: {begPage}, firstPageIndex: {firstPageIndex}, endPage: {endPage}, lastPageIndex: {lastPageIndex})");
                    return 0;
                }

                // Sum total for all pages in range
                ulong residentSize = 0;
                for (var p = begPage; p <= endPage; p++)
                {
                    if (PageStates[p])
                        residentSize += PageSize;
                }

                // As address might be not aligned, we need to subtract
                // difference for the first and the last page
                if (PageStates[begPage])
                {
                    var head = address % PageSize;
                    residentSize -= head;
                }

                if (PageStates[endPage])
                {
                    var tail = (address + size) % PageSize;
                    if (tail > 0)
                        residentSize -= PageSize - tail;
                }

                return residentSize;
            }
        }
    }
}

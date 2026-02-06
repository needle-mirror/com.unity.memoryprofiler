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
            public readonly DynamicArray<ulong> RegionAddress;
            public readonly DynamicArray<int> RegionStartPageIndex;
            public readonly DynamicArray<int> RegionEndPageIndex;
            public readonly BitArray PageStates;
            public readonly uint PageSize;

            public SystemMemoryResidentPagesEntriesCache(ref IFileReader reader)
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
                    if (snapshot.MetaData.UnityVersionMajor <= 2023 ||
                        !(snapshot.MetaData.TargetInfo is { RuntimePlatform: RuntimePlatform.OSXEditor } || snapshot.MetaData.TargetInfo is { RuntimePlatform: RuntimePlatform.OSXPlayer }))
                        Debug.LogAssertion($"Page range is outside of system region range. Please report a bug! (Source: {sourceId}, regionIndex: {regionIndex}, regionAddress: {regionAddress}, address: {address}, addrDelta: {addrDelta}, begPage: {begPage}, firstPageIndex: {firstPageIndex}, endPage: {endPage}, lastPageIndex: {lastPageIndex})");
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

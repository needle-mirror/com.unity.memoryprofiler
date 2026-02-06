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
            public DynamicArray<ulong> RegionAddress = default;
            public DynamicArray<ulong> RegionSize = default;
            public DynamicArray<ulong> RegionResident = default;
            public DynamicArray<ushort> RegionType = default;

            public SystemMemoryRegionEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.SystemMemoryRegions_Address);
                RegionName = new string[Count];

                if (Count == 0)
                    return;

                RegionAddress = reader.Read(EntryType.SystemMemoryRegions_Address, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RegionSize = reader.Read(EntryType.SystemMemoryRegions_Size, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RegionResident = reader.Read(EntryType.SystemMemoryRegions_Resident, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RegionType = reader.Read(EntryType.SystemMemoryRegions_Type, 0, Count, Allocator.Persistent).Result.Reinterpret<ushort>();

                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.SystemMemoryRegions_Name, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.SystemMemoryRegions_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref RegionName);
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
            }
        }
    }
}

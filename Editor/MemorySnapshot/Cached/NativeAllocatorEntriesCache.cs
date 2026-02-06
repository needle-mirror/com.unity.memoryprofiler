using System;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
        /// <summary>
        /// A table of all allocators which Unity uses to manage memory allocations in native code.
        /// All size values are in bytes.
        /// </summary>
        public class NativeAllocatorEntriesCache : IDisposable
        {
            /// <summary>
            /// Count of allocators.
            /// </summary>
            public long Count;
            /// <summary>
            /// Name of allocator.
            /// </summary>
            public string[] AllocatorName;
            /// <summary>
            /// Memory which was requested by Unity native systems from the allocator and is being used to store data.
            /// </summary>
            public DynamicArray<ulong> UsedSize = default;
            /// <summary>
            /// Total memory that was requested by allocator from System.
            /// May be larger than UsedSize to utilize pooling approach.
            /// </summary>
            public DynamicArray<ulong> ReservedSize = default;
            /// <summary>
            /// Total size of memory dedicated to allocations tracking.
            /// </summary>
            public DynamicArray<ulong> OverheadSize = default;
            /// <summary>
            /// Maximum amount of memory allocated with this allocator since app start.
            /// </summary>
            public DynamicArray<ulong> PeakUsedSize = default;
            /// <summary>
            /// Allocations count made via the specific allocator.
            /// </summary>
            public DynamicArray<ulong> AllocationCount = default;

            public NativeAllocatorEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeAllocatorInfo_AllocatorName);
                AllocatorName = new string[Count];

                if (Count == 0)
                    return;

                UsedSize = reader.Read(EntryType.NativeAllocatorInfo_UsedSize, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                ReservedSize = reader.Read(EntryType.NativeAllocatorInfo_ReservedSize, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                OverheadSize = reader.Read(EntryType.NativeAllocatorInfo_OverheadSize, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                PeakUsedSize = reader.Read(EntryType.NativeAllocatorInfo_PeakUsedSize, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                AllocationCount = reader.Read(EntryType.NativeAllocatorInfo_AllocationCount, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();

                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeAllocatorInfo_AllocatorName, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeAllocatorInfo_AllocatorName, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref AllocatorName);
                }
            }

            public void Dispose()
            {
                Count = 0;
                AllocatorName = null;
                UsedSize.Dispose();
                ReservedSize.Dispose();
                OverheadSize.Dispose();
                PeakUsedSize.Dispose();
                AllocationCount.Dispose();
            }
        }

    }
}

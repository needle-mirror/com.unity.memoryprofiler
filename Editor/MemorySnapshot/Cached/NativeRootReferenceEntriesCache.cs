using System;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;

// Pre com.unity.collections@2.1.0 NativeHashMap was not constraining its held data to unmanaged but to struct.
// NativeHashSet does not have the same issue, but for ease of use may get an alias below for EntityId.
#if !UNMANAGED_NATIVE_HASHMAP_AVAILABLE
using LongToLongHashMap = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<long, long>;
#else
using LongToLongHashMap = Unity.Collections.NativeHashMap<long, long>;
#endif

namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
        public class NativeRootReferenceEntriesCache : IDisposable
        {
            public const long InvalidRootId = 0;
            public const long FirstValidRootId = 1;
            public const long InvalidRootIndex = 0;
            public const long FirstValidRootIndex = 1;
            public long Count;
            public DynamicArray<long> Id = default;
            public DynamicArray<ulong> AccumulatedSize = default;
            public string[] AreaName;
            public string[] ObjectName;
            public LongToLongHashMap IdToIndex;
            public readonly SourceIndex VMRootReferenceIndex = default;
            public readonly ulong AccumulatedSizeOfVMRoot = 0UL;
            public readonly ulong ExecutableAndDllsReportedValue;
            public const string ExecutableAndDllsRootReferenceName = "ExecutableAndDlls";
            public readonly long ExecutableAndDllsRootReferenceIndex = -1;
            static readonly string[] k_VMRootNames =
            {
                "Mono VM",
                "IL2CPP VM",
                "IL2CPPMemoryAllocator",
            };

            public NativeRootReferenceEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeRootReferences_Id);

                AreaName = new string[Count];
                ObjectName = new string[Count];

                IdToIndex = new LongToLongHashMap((int)Count, Allocator.Persistent);

                if (Count == 0)
                    return;

                Id = reader.Read(EntryType.NativeRootReferences_Id, 0, Count, Allocator.Persistent).Result.Reinterpret<long>();
                AccumulatedSize = reader.Read(EntryType.NativeRootReferences_AccumulatedSize, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();

                using (var tmpBuffer = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeRootReferences_AreaName, 0, Count);
                    tmpBuffer.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeRootReferences_AreaName, tmpBuffer, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmpBuffer, ref AreaName);

                    tmpSize = reader.GetSizeForEntryRange(EntryType.NativeRootReferences_ObjectName, 0, Count);
                    tmpBuffer.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeRootReferences_ObjectName, tmpBuffer, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmpBuffer, ref ObjectName);
                }

                var hasCalculatedAccumulatedSizeOfVMRoot = false;
                for (long i = 0; i < Count; i++)
                {
                    if (ExecutableAndDllsRootReferenceIndex == -1 && ObjectName[i] == ExecutableAndDllsRootReferenceName)
                    {
                        ExecutableAndDllsRootReferenceIndex = i;
                        ExecutableAndDllsReportedValue = AccumulatedSize[i];
                        // Nothing is ever actually rooted to "System : ExecutableAndDlls". This is just a hacky way of reporting systeminfo::GetExecutableSizeMB()
                        // therefore there is no need to map it to an index (of 0) and thereby wrongly suggest that allocations with root id 0 would belong to the executable size
                        if (i == 0)
                            continue;
                    }
                    IdToIndex.Add(Id[i], i);

                    if (!hasCalculatedAccumulatedSizeOfVMRoot)
                    {
                        var name = ObjectName[i];
                        foreach (var vmRootName in k_VMRootNames)
                        {
                            if (name.Equals(vmRootName, StringComparison.Ordinal))
                            {
                                // There is only one VM root in a capture, so we can stop looking once found.
                                AccumulatedSizeOfVMRoot = AccumulatedSize[i];
                                VMRootReferenceIndex = new SourceIndex(SourceIndex.SourceId.NativeRootReference, i);
                                hasCalculatedAccumulatedSizeOfVMRoot = true;
                            }
                        }
                    }
                }
            }

            public void Dispose()
            {
                Id.Dispose();
                AccumulatedSize.Dispose();
                Count = 0;
                AreaName = null;
                ObjectName = null;
                IdToIndex.Dispose();
            }
        }
    }
}

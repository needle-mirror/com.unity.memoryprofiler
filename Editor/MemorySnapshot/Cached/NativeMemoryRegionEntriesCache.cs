using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;


#if !ENTITY_ID_CHANGED_SIZE
// the official EntityId lives in the UnityEngine namespace, which might be be added as a using via the IDE,
// so to avoid mistakenly using a version of this struct with the wrong size, alias it here.
using EntityId = Unity.MemoryProfiler.Editor.EntityId;
#else
// This should be greyed out by the IDE, otherwise you're missing an alias above
using UnityEngine;
using EntityId = UnityEngine.EntityId;
#endif

namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
        public class NativeMemoryRegionEntriesCache : IDisposable
        {
            public long Count;
            public string[] MemoryRegionName;
            public DynamicArray<int> ParentIndex = default;
            public DynamicArray<ulong> AddressBase = default;
            public DynamicArray<ulong> AddressSize = default;
            public DynamicArray<int> FirstAllocationIndex = default;
            public DynamicArray<int> NumAllocations = default;
            public readonly bool UsesDynamicHeapAllocator = false;
            public readonly bool UsesSystemAllocator;
            public HashSet<long> GPUAllocatorIndices = new HashSet<long>();

            const string k_DynamicHeapAllocatorName = "ALLOC_DEFAULT_MAIN";
            const string k_GPUAllocatorName = "ALLOC_GPU";

#if ENTITY_ID_STRUCT_AVAILABLE && !ENTITY_ID_CHANGED_SIZE
            static NativeMemoryRegionEntriesCache()
            {
                Checks.IsTrue((typeof(EntityId) != typeof(UnityEngine.EntityId)), "The wrong type of EntityId struct is used, probably due to accidentally addin a 'using UnityEngine;' to this file.");
            }
#endif

            public NativeMemoryRegionEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeMemoryRegions_AddressBase);
                MemoryRegionName = new string[Count];

                if (Count == 0)
                    return;

                ParentIndex = reader.Read(EntryType.NativeMemoryRegions_ParentIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                AddressBase = reader.Read(EntryType.NativeMemoryRegions_AddressBase, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                AddressSize = reader.Read(EntryType.NativeMemoryRegions_AddressSize, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                FirstAllocationIndex = reader.Read(EntryType.NativeMemoryRegions_FirstAllocationIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                NumAllocations = reader.Read(EntryType.NativeMemoryRegions_NumAllocations, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();

                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeMemoryRegions_Name, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeMemoryRegions_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref MemoryRegionName);
                }

                for (long i = 0; i < Count; i++)
                {
                    if (!UsesDynamicHeapAllocator && AddressSize[i] > 0 && MemoryRegionName[i].StartsWith(k_DynamicHeapAllocatorName))
                    {
                        UsesDynamicHeapAllocator = true;
                    }

                    if (MemoryRegionName[i].StartsWith(k_GPUAllocatorName))
                    {
                        GPUAllocatorIndices.Add(i);
                    }
                }
                if (Count > 0)
                    UsesSystemAllocator = !UsesDynamicHeapAllocator;
            }

            public void Dispose()
            {
                Count = 0;
                MemoryRegionName = null;
                ParentIndex.Dispose();
                AddressBase.Dispose();
                AddressSize.Dispose();
                FirstAllocationIndex.Dispose();
                NumAllocations.Dispose();
            }
        }

        public class SortedNativeMemoryRegionEntriesCache : IndirectlySortedEntriesCacheSortedByAddressAndSizeArrayWithCache
        {
            public readonly DynamicArray<byte> RegionHierarchLayer;
            public SortedNativeMemoryRegionEntriesCache(CachedSnapshot snapshot) : base(snapshot)
            {
                RegionHierarchLayer = new DynamicArray<byte>(Count, Allocator.Persistent);
            }

            public override long Count => m_Snapshot.NativeMemoryRegions.Count;

            protected override bool AllowExactlyOverlappingRegions => true;
            protected override ref readonly DynamicArray<ulong> Addresses => ref m_Snapshot.NativeMemoryRegions.AddressBase;
            protected override ref readonly DynamicArray<ulong> Sizes => ref m_Snapshot.NativeMemoryRegions.AddressSize;
            public string Name(long index) => m_Snapshot.NativeMemoryRegions.MemoryRegionName[this[index]];
            public int UnsortedParentRegionIndex(long index) => m_Snapshot.NativeMemoryRegions.ParentIndex[this[index]];
            public int UnsortedFirstAllocationIndex(long index) => m_Snapshot.NativeMemoryRegions.FirstAllocationIndex[this[index]];
            public int UnsortedNumAllocations(long index) => m_Snapshot.NativeMemoryRegions.NumAllocations[this[index]];

            public override void Preload()
            {
                base.Preload();
                var count = Count;
                if (count <= 0)
                    return;

                using var regionLayerStack = new DynamicArray<(sbyte, ulong)>(0, 10, Allocator.Temp, memClear: false);
                for (long i = 0; i < count; i++)
                {
                    sbyte currentLayer = -1;
                    var regionEnd = Address(i) + FullSize(i);

                    if (regionLayerStack.Count > 0)
                    {
                        // avoid the copy
                        ref readonly var enclosingRegion = ref regionLayerStack.Peek();
                        while (regionEnd > enclosingRegion.Item2)
                        {
                            // pop layer stack until the enclosung region encompases this region
                            regionLayerStack.Pop();
                            if (regionLayerStack.Count > 0)
                                enclosingRegion = ref regionLayerStack.Peek();
                            else
                                break;
                        }
                        // if there are no enclosing regions, we are at the top level, aka -1
                        currentLayer = regionLayerStack.Count > 0 ? enclosingRegion.Item1 : (sbyte)-1;
                    }

                    regionLayerStack.Push(new(++currentLayer, Address(i)));
                    RegionHierarchLayer[i] = (byte)currentLayer;
                }
            }
            public override void Dispose()
            {
                base.Dispose();
                RegionHierarchLayer.Dispose();
            }
        }

    }
}

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.EnumerationUtilities;
using Unity.MemoryProfiler.Editor.Extensions;
using UnityEngine;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal struct NativeRootSize
    {
        public MemorySize NativeSize;
        public MemorySize ManagedSize;
        public MemorySize GfxSize;
        public MemorySize SumUp() => NativeSize + ManagedSize + GfxSize;
    }

    internal struct ProcessedNativeRoot
    {
        /// <summary>
        /// The accumulated Native, Graphics and Managed size of all memory as mapped out in the <see cref="EntriesMemoryMapCache"/>
        /// complete with Committed and Resident amounts and accumulated to the root.
        /// </summary>
        public NativeRootSize AccumulatedRootSizes;

        /// <summary>
        /// The <see cref="SourceIndex"/> associated with this entry.
        /// For Native Object roots, the <see cref="SourceIndex.Id"/> will be <see cref="SourceIndex.SourceId.NativeObject"/>,
        /// for non object rooted entries it will be <see cref="SourceIndex.SourceId.NativeRootReference"/>.
        /// </summary>
        public SourceIndex NativeObjectOrRootIndex;

        /// <summary>
        /// The root id associated with this entry.
        ///
        /// Note: This is NOT a <see cref="SourceIndex.SourceId.NativeRootReference"/> but an Root Reference _ID_ that needs to be converted to an index via <see cref="ProcessedNativeRoots.RootIdToMappedIndex"/>.
        /// </summary>
        public long RootId;

        /// <summary>
        /// If there are graphics resources associated with this entry
        /// this holds the <see cref="SourceIndex.SourceId.GfxResource"/> index for the first resource mapping to it.
        /// Any further resources are listed in the <see cref="ProcessedNativeRoots.AdditionalGraphicsResourceIndices"/> without a direct index mapping.
        /// </summary>
        public SourceIndex AssociatadGraphicsResourceIndex;
    }

    internal class ProcessedNativeRoots
    {
        public const int ProcessStepCount = 3;

        public long Count { get; private set; }

        /// <summary>
        /// All Native Root data that has corresponding info in <see cref="EntriesMemoryMapCache"/> (i.e. stuff like the first entry,
        /// which is the fake root for reporting the size of Executables & DLLS without any allocations actually rooted to it will remain with an invalid
        /// <see cref="ProcessedNativeRoot.NativeObjectOrRootIndex"/> and no sizes).
        /// Since all Graphics Resources are also processed and linked up with their Native Root entries / Native Objects, iterating this data
        /// can replace iterating the <see cref="EntriesMemoryMapCache"/> if one is only interested in Native Objects (and their potential Managed Shells),
        /// Native Unity Subsystems and Graphics memory (i.e. all of _used_ Native and Graphics Memory + non-leaked Unity Shell Objects.)
        ///
        /// Note: This is indexed by root indices unless no Native Allocations where reported, in which case it is indexed by native object index.
        /// Outside of iterating over this array, it is safer to go from root ID to mapped index via <see cref="RootIdToMappedIndex(long)"/>.
        /// </summary>
        public DynamicArray<ProcessedNativeRoot> Data;

        /// <summary>
        /// If there are more than one graphics resources associated with an entry in <see cref="Data"/>,
        /// this holds the <see cref="SourceIndex.SourceId.GfxResource"/> for any additional resource.
        ///
        /// To get an index mapping to the <see cref="Data"/> list, get the <see cref="NativeGfxResourceReferenceEntriesCache.RootId"/> entry for
        /// the <see cref="SourceIndex.SourceId.GfxResource"/> index, and convert the root id to the root index via <see cref="RootIdToMappedIndex"/>
        /// </summary>
        public DynamicArray<SourceIndex> AdditionalGraphicsResourceIndices;

        /// <summary>
        /// If a graphics resource is not associated with a Native Object entry it is added to this array.
        ///
        /// To get an index mapping to the <see cref="Data"/> list, get the <see cref="NativeGfxResourceReferenceEntriesCache.RootId"/> entry for
        /// the <see cref="SourceIndex.SourceId.GfxResource"/> index, and convert the root id to the root index via <see cref="RootIdToMappedIndex"/>
        /// </summary>
        public DynamicArray<SourceIndex> NonObjectRootedGraphicsResourceIndices;

        /// <summary>
        /// If a graphics resource is not associated with a Native Root it is added to this array.
        ///
        /// To get an index mapping to the <see cref="Data"/> list, get the <see cref="NativeGfxResourceReferenceEntriesCache.RootId"/> entry for
        /// the <see cref="SourceIndex.SourceId.GfxResource"/> index, and convert the root id to the root index via <see cref="RootIdToMappedIndex"/>
        /// </summary>
        public DynamicArray<SourceIndex> UnrootedGraphicsResourceIndices;

        public MemorySize NativeAllocationsThatAreUntrackedGraphicsResources => m_NativeAllocationsThatAreUntrackedGraphicsResources;
        MemorySize m_NativeAllocationsThatAreUntrackedGraphicsResources;

        public NativeRootSize UnknownRootMemory => m_UnknownRootMemory;
        NativeRootSize m_UnknownRootMemory;

        public MemorySize TotalMemoryInSnapshot => m_TotalMemoryInSnapshot;
        MemorySize m_TotalMemoryInSnapshot;

        bool m_NoRootInfo = false;
        bool m_Processed = false;

        Dictionary<long, long> m_NativeRootIdToNativeRootIndex;
        Dictionary<long, int> m_NativeRootIdToNativeObjectIndex;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long RootIdToMappedIndex(long rootId)
        {
            return m_NoRootInfo ? m_NativeRootIdToNativeObjectIndex[rootId] : m_NativeRootIdToNativeRootIndex[rootId];
        }

        public IEnumerator<EnumerationStatus> ReadOrProcess(CachedSnapshot snapshot, EnumerationStatus status)
        {
            if (m_Processed)
            {
                for (int i = 0; i < ProcessStepCount; i++)
                {
                    status.IncrementStep("Skipping already Processed Native Roots");
                }
                yield break;
            }
            yield return status.IncrementStep("Process Native Roots");

            Count = snapshot.NativeRootReferences.Count;
            if (Count == 0)
            {
                m_NoRootInfo = true;
                Count = snapshot.NativeObjects.Count;
            }

            Data = new DynamicArray<ProcessedNativeRoot>(Count, Allocator.Persistent, memClear: true);

            AdditionalGraphicsResourceIndices = new DynamicArray<SourceIndex>(0, 20, Allocator.Persistent, memClear: true);
            NonObjectRootedGraphicsResourceIndices = new DynamicArray<SourceIndex>(0, 20, Allocator.Persistent, memClear: true);
            UnrootedGraphicsResourceIndices = new DynamicArray<SourceIndex>(0, 20, Allocator.Persistent, memClear: true);

            // already mark processed in case processing throws. That ensures that the native data gets unloaded in those cases too.
            m_Processed = true;

            m_NativeRootIdToNativeRootIndex = snapshot.NativeRootReferences.IdToIndex;
            m_NativeRootIdToNativeObjectIndex = snapshot.NativeObjects.RootReferenceIdToIndex;

            yield return status.IncrementStep("Process Native Roots: Walk Memory Map");
            snapshot.EntriesMemoryMap.ForEachFlatWithResidentSize((index, address, size, residentSize, source) =>
            {
                var memorySize = new MemorySize(size, residentSize);
                m_TotalMemoryInSnapshot += memorySize;
                // Add items to respective group container
                switch (source.Id)
                {
                    case SourceIndex.SourceId.NativeObject:
                        ProcessNativeObject(snapshot, source, memorySize);
                        break;
                    case SourceIndex.SourceId.NativeAllocation:
                        ProcessNativeAllocation(snapshot, source, memorySize);
                        break;
                    case SourceIndex.SourceId.ManagedObject:
                        ProcessManagedObject(snapshot, source, memorySize);
                        break;
                    default:
                        break;
                }
            });
            yield return status.IncrementStep("Process Native Roots: Process Graphics Resources");
            // Add estimated resources
            if (snapshot.HasGfxResourceReferencesAndAllocators)
            {
                // Add estimated graphics resources
                ProcessGraphicsResources(snapshot);
            }
            else
            {
                ProcessLegacyNativeObjectSizesAndGraphicsResources(snapshot);
            }
        }

        private void ProcessManagedObject(CachedSnapshot snapshot, SourceIndex source, MemorySize memorySize)
        {
            var nativeObjectIndex = snapshot.CrawledData.ManagedObjects[source.Index].NativeObjectIndex;
            if (nativeObjectIndex < NativeTypeEntriesCache.FirstValidTypeIndex)
                return;
            var rootReferenceId = snapshot.NativeObjects.RootReferenceId[nativeObjectIndex];
            var rootIndex = RootIdToMappedIndex(rootReferenceId);
            Data[rootIndex].AccumulatedRootSizes.ManagedSize += memorySize;
        }

        // Native allocation might be associated with an object or Unity "subsystem"
        // ProcessNativeAllocation should be able to register either in objects or allocations
        void ProcessNativeAllocation(
            CachedSnapshot snapshot,
            SourceIndex source,
            MemorySize size)
        {
            var nativeAllocations = snapshot.NativeAllocations;
            var rootReferenceId = nativeAllocations.RootReferenceId[source.Index];

            if (snapshot.MetaData.TargetInfo is { RuntimePlatform: RuntimePlatform.Switch }
                && snapshot.EntriesMemoryMap.GetPointType(source) == EntriesMemoryMapCache.PointType.Device)
            {
                m_NativeAllocationsThatAreUntrackedGraphicsResources += size;
                return;
            }

            if (rootReferenceId >= NativeRootReferenceEntriesCache.FirstValidRootIndex)
            {
                SourceIndex rootSource = new SourceIndex();
                // Is this allocation associated with a native object?
                if (snapshot.NativeObjects.RootReferenceIdToIndex.TryGetValue(rootReferenceId, out var objectIndex))
                {
                    rootSource = new SourceIndex(SourceIndex.SourceId.NativeObject, objectIndex);
                }
                // Extract rootIndex.
                if (snapshot.NativeRootReferences.IdToIndex.GetOrInitializeValue(rootReferenceId, out long rootIndex, -1)
                    && !rootSource.Valid)
                {
                    rootSource = new SourceIndex(SourceIndex.SourceId.NativeRootReference, rootIndex);
                }

                if (!rootSource.Valid)
                {
                    m_UnknownRootMemory.NativeSize += size;
                    return;
                }

                ref var data = ref Data[rootIndex];
                data.AccumulatedRootSizes.NativeSize += size;
                data.NativeObjectOrRootIndex = rootSource;
                data.RootId = rootReferenceId;
                return;
            }
            else
            {
                m_UnknownRootMemory.NativeSize += size;
                return;
            }
        }

        void ProcessNativeObject(
            CachedSnapshot snapshot,
            SourceIndex source,
            MemorySize size)
        {
            var rootReferenceId = snapshot.NativeObjects.RootReferenceId[source.Index];
            var rootIndex = RootIdToMappedIndex(rootReferenceId);

            ref var data = ref Data[rootIndex];
            data.AccumulatedRootSizes.NativeSize += size;
            data.NativeObjectOrRootIndex = source;
            data.RootId = rootReferenceId;
        }

        void ProcessGraphicsResources(CachedSnapshot snapshot)
        {
            // Graphics resource info is only reported if Native Allocations (and with that, their root references) are reported
            // This function can therefore ignore m_NoRootInfo.
            // If no native objects are reported, their graphics allocations are still rooted to their native object roots
            var nativeGfxResourceReferences = snapshot.NativeGfxResourceReferences;
            var nativeRootReferences = snapshot.NativeRootReferences;
            for (long i = 0; i < nativeGfxResourceReferences.Count; i++)
            {
                var source = new SourceIndex(SourceIndex.SourceId.GfxResource, i);
                var rootId = nativeGfxResourceReferences.RootId[i];
                var rootIndex = rootId >= CachedSnapshot.NativeRootReferenceEntriesCache.FirstValidRootId ? nativeRootReferences.IdToIndex[rootId] : CachedSnapshot.NativeRootReferenceEntriesCache.InvalidRootIndex;

                var size = 0UL;
                size = nativeGfxResourceReferences.GfxSize[i];
                if (size == 0)
                    continue;

                var memorySize = new MemorySize(size, 0);

                if (rootIndex <= CachedSnapshot.NativeRootReferenceEntriesCache.InvalidRootIndex)
                {
                    m_UnknownRootMemory.GfxSize += memorySize;
                    UnrootedGraphicsResourceIndices.Push(source);
                    continue;
                }

                ref var data = ref Data[rootIndex];

                data.AccumulatedRootSizes.GfxSize += memorySize;
                if (!data.AssociatadGraphicsResourceIndex.Valid)
                    data.AssociatadGraphicsResourceIndex = source;
                else
                    AdditionalGraphicsResourceIndices.Push(source);

                if (!data.NativeObjectOrRootIndex.Valid)
                    data.NativeObjectOrRootIndex = new SourceIndex(SourceIndex.SourceId.NativeRootReference, rootIndex);
                else if (data.NativeObjectOrRootIndex.Id != SourceIndex.SourceId.NativeObject)
                    NonObjectRootedGraphicsResourceIndices.Push(source);

                data.RootId = rootId;
            }
        }

        void ProcessLegacyNativeObjectSizesAndGraphicsResources(CachedSnapshot snapshot)
        {
            if (m_NoRootInfo)
                // Without root info, we can't subtract the AccumulatedSize of the root from the reported native object size
                return;
            var nativeObjects = snapshot.NativeObjects;
            var nativeRootReferences = snapshot.NativeRootReferences;

            for (long i = 0; i < nativeObjects.Count; i++)
            {
                var totalSize = nativeObjects.Size[i];
                var rootReferenceId = nativeObjects.RootReferenceId[i];

                if (rootReferenceId <= 0)
                    continue;

                if (!nativeRootReferences.IdToIndex.TryGetValue(rootReferenceId, out var rootReferenceIndex))
                    continue;

                var rootAccumulatedSize = nativeRootReferences.AccumulatedSize[rootReferenceIndex];
                ref var data = ref Data[rootReferenceIndex];
                if (rootAccumulatedSize >= totalSize)
                {
                    if (rootAccumulatedSize > data.AccumulatedRootSizes.NativeSize.Committed)
                    {
                        // Some legacy data has full mapping from Native Size to Accumulated Root size
                        // but there is Native Memory region info missing and the mapped root sizes don't match up
                        // in those cases, trust the AccumulatedSize of the Root References over the EntriesMemoryMap calculations,
                        // as the latter doesn't have solid enough data
                        data.AccumulatedRootSizes.NativeSize = new MemorySize(rootAccumulatedSize, 0);
                    }
                    continue;
                }

                data.AccumulatedRootSizes.NativeSize = new MemorySize(rootAccumulatedSize, 0);
                var gfxSize = new MemorySize(totalSize - rootAccumulatedSize, 0);

                data.AccumulatedRootSizes.GfxSize += gfxSize;
            }

        }

        public void Dispose()
        {
            if (!m_Processed)
                return;
            Data.Dispose();

            AdditionalGraphicsResourceIndices.Dispose();
            NonObjectRootedGraphicsResourceIndices.Dispose();
            UnrootedGraphicsResourceIndices.Dispose();
        }
    }
}

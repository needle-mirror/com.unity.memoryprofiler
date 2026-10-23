//#define CRAWLER_PERFORMANCE_ANALYSIS
// When defining DEBUG_JOBIFIED_CRAWLER, define for the assembly or also define DEBUG_JOBIFIED_CRAWLER in ManagedDataCrawler.cs, ParallelReferenceArrayCrawlerJobChunk.cs, ParallelStaticFieldsCrawlerJobChunk.cs, ParallelStructArrayCrawlerJobChunk.cs, ParallelReferenceArrayCrawlerJobChunk.cs, ChunkedParallelArrayCrawlerJob.cs and JobifiedCrawlDataStacksPool.cs
//#define DEBUG_JOBIFIED_CRAWLER
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.MemoryProfiler.Editor.Extensions;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.UI;
using Unity.Profiling;
#if CRAWLER_PERFORMANCE_ANALYSIS
using UnityEditor;
using System.Diagnostics;
#endif
#if !UNMANAGED_NATIVE_HASHMAP_AVAILABLE
using AddressToManagedIndexHashMap = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<ulong, long>;
using TypeIndexToCrawlerDataIndexHashMap = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<int, long>;
#if !ENTITY_ID_CHANGED_SIZE
using AddressToInstanceId = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<ulong, Unity.MemoryProfiler.Editor.EntityId>;
using InstanceIdToNativeObjectIndex = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<Unity.MemoryProfiler.Editor.EntityId, long>;
#else
using AddressToInstanceId = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<ulong, UnityEngine.EntityId>;
using InstanceIdToNativeObjectIndex = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<UnityEngine.EntityId, long>;
#endif
using AddressToIndex = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<ulong, long>;
#else
using AddressToManagedIndexHashMap = Unity.Collections.NativeHashMap<ulong, long>;
using TypeIndexToCrawlerDataIndexHashMap = Unity.Collections.NativeHashMap<int, long>;
#if !ENTITY_ID_CHANGED_SIZE
using AddressToInstanceId = Unity.Collections.NativeHashMap<ulong, Unity.MemoryProfiler.Editor.EntityId>;
using InstanceIdToNativeObjectIndex = Unity.Collections.NativeHashMap<Unity.MemoryProfiler.Editor.EntityId, long>;
#else
using AddressToInstanceId = Unity.Collections.NativeHashMap<ulong, UnityEngine.EntityId>;
using InstanceIdToNativeObjectIndex = Unity.Collections.NativeHashMap<UnityEngine.EntityId, long>;
#endif
using AddressToIndex = Unity.Collections.NativeHashMap<ulong, long>;
#endif
using static Unity.MemoryProfiler.Editor.CachedSnapshot;
using Debug = UnityEngine.Debug;
using Unity.MemoryProfiler.Editor.EnumerationUtilities;

#if !ENTITY_ID_CHANGED_SIZE
// the official EntityId lives in the UnityEngine namespace, which might be be added as a using via the IDE,
// so to avoid mistakenly using a version of this struct with the wrong size, alias it here.
using EntityId = Unity.MemoryProfiler.Editor.EntityId;
#else
using EntityId = UnityEngine.EntityId;
// This should be greyed out by the IDE, otherwise you're missing an alias above
using UnityEngine;
#endif

namespace Unity.MemoryProfiler.Editor.Managed
{
    [BurstCompile]
    static partial class ManagedDataCrawler
    {
#if ENTITY_ID_STRUCT_AVAILABLE && !ENTITY_ID_CHANGED_SIZE
        static ManagedDataCrawler()
        {
            Checks.IsTrue((typeof(EntityId) != typeof(UnityEngine.EntityId)), "The wrong type of EntityId struct is used, probably due to accidentally adding a 'using UnityEngine;' to this file.");
        }
#endif

        // Processing arrays in jobs is a neat speed up but for smaller arrays, the scheduling overhead beats the gain.
        // These values are based on rough experiments. Feel free to test and fine tune
        internal const int JobifiedArrayCrawlingFieldCountThreshold = 1000;
        internal const int MinFieldCountPerJobChunk = 150_000;
        const int k_MaxFieldCountPerJobChunk = 250_000;
        const int k_IdealJobCount = 8;
        const int k_MaxStackMemoryUsageForJobifiedCrawling = 32 * 1024 * 1024; // 32 MB
        const int k_MaxConcurrentJobCount = k_MaxStackMemoryUsageForJobifiedCrawling / k_MaxFieldCountPerJobChunk;

        // Clearing the memory should not be necessary but it can help with debugging
        const bool k_MemClearCrawlDataStack = false;
        const bool k_MemClearFieldLayoutData = false;

        struct StackCrawlData
        {
            // Address of object to be crawled.
            public ulong Address;
            // bytes of the object to be crawled
            public BytesAndOffset Bytes;
            // or the managed object index if it was already crawled and just needs to be connected
            public long ManagedObjectIndex;
            // or the source index to a native allocation or object that just needs to be connected
            public SourceIndex SourceIndexOfNativeItem;
            public int TypeIndexOfTheTypeOwningTheField;
            // might be a Managed Object or Type
            public SourceIndex ReferenceOwner;
            public int FieldIndexOfTheFieldOnTheReferenceOwner;
            public int FieldIndexOfTheActualField_WhichMayBeOnANestedValueType;
            public int OffsetFromReferenceOwnerHeaderStartToFieldOnValueType;
            public long IndexInReferenceOwningArray;

            public StackCrawlData(
                ulong address,
                BytesAndOffset bytesAndOffsets = default,
                long managedObjectIndex = -1,
                SourceIndex sourceIndexOfNativeItem = default,
                int typeIndexOfTheTypeOwningTheField = -1,
                SourceIndex referenceOwner = default,
                int fieldIndexOfTheFieldOnTheReferenceOwner = -1,
                int fieldIndexOfTheActualField_WhichMayBeOnANestedValueType = -1,
                int offsetFromReferenceOwnerHeaderStartToFieldOnValueType = 0,
                long indexInReferenceOwningArray = -1)
            {
                Address = address;
                Bytes = bytesAndOffsets;
                ManagedObjectIndex = managedObjectIndex;
                SourceIndexOfNativeItem = sourceIndexOfNativeItem;
                TypeIndexOfTheTypeOwningTheField = typeIndexOfTheTypeOwningTheField;
                ReferenceOwner = referenceOwner;
                FieldIndexOfTheFieldOnTheReferenceOwner = fieldIndexOfTheFieldOnTheReferenceOwner;
                FieldIndexOfTheActualField_WhichMayBeOnANestedValueType = fieldIndexOfTheActualField_WhichMayBeOnANestedValueType;
                OffsetFromReferenceOwnerHeaderStartToFieldOnValueType = offsetFromReferenceOwnerHeaderStartToFieldOnValueType;
                IndexInReferenceOwningArray = indexInReferenceOwningArray;
            }
        }

        readonly struct NativeObjectOrAllocationFinder : IDisposable
        {
            [ReadOnly]
            public readonly AddressToInstanceId AddressToInstanceId;
            [ReadOnly]
            public readonly InstanceIdToNativeObjectIndex InstanceIdToNativeObjectIndex;

            [ReadOnly]
            public readonly DynamicArray<ulong> NativeSortedAllocationAddress;

            [ReadOnly]
            public readonly DynamicArray<ulong> NativeSortedAllocationSize;

            [ReadOnly]
            public readonly DynamicArray<long> NativeSortedAllocationIndex;

            public NativeObjectOrAllocationFinder(
                AddressToInstanceId addressToInstanceId,
                InstanceIdToNativeObjectIndex instanceIdToNativeObjectIndex,
                DynamicArray<ulong> nativeSortedAllocationAddress,
                DynamicArray<ulong> nativeSortedAllocationSize,
                DynamicArray<long> nativeSortedAllocationIndex)
            {
                AddressToInstanceId = addressToInstanceId;
                InstanceIdToNativeObjectIndex = instanceIdToNativeObjectIndex;
                NativeSortedAllocationAddress = nativeSortedAllocationAddress;
                NativeSortedAllocationSize = nativeSortedAllocationSize;
                NativeSortedAllocationIndex = nativeSortedAllocationIndex;
            }

            public bool TryFindSourceIndexForAddress(ulong address, out SourceIndex sourceIndex)
            {
                if (AddressToInstanceId.TryGetValue(address, out var instanceId))
                {
                    if (InstanceIdToNativeObjectIndex.TryGetValue(instanceId, out var nativeObjectIndex))
                    {
                        sourceIndex = new SourceIndex(SourceIndex.SourceId.NativeObject, nativeObjectIndex);
                        return true;
                    }
                }
                else
                {
                    // Native allocations tend to not be hit at exactly their address due to allocation headers and because pointers to their data can point further into the allocation
                    // than their start address, so we need to binary search for an address + size combo that could fit.
                    var idx = DynamicArrayAlgorithms.BinarySearch(NativeSortedAllocationAddress, address);

                    if (idx < 0)
                    {
                        // -1 means the address is smaller than the first StartAddress, early out with an invalid bytesAndOffset
                        if (idx == -1)
                        {
                            sourceIndex = default;
                            return false;
                        }
                        // otherwise, a negative Index just means there was no direct hit and ~idx - 1 will give us the index to the next smaller StartAddress
                        idx = ~idx - 1;
                    }

                    if (address >= NativeSortedAllocationAddress[idx] && address < (NativeSortedAllocationAddress[idx] + (ulong)NativeSortedAllocationSize[idx]))
                    {
                        sourceIndex = new SourceIndex(SourceIndex.SourceId.NativeAllocation, NativeSortedAllocationIndex[idx]);
                        return true;
                    }
                }
                sourceIndex = default;
                return false;
            }

            public void Dispose()
            {
                NativeSortedAllocationAddress.Dispose();
                NativeSortedAllocationSize.Dispose();
                NativeSortedAllocationIndex.Dispose();
            }
        }

        class IntermediateCrawlData :
            IDisposable, IChunkedCrawlerJobManagerBase
        {
            public ref DynamicArray<int> TypesWithStaticFields => ref m_TypesWithStaticFields;
            DynamicArray<int> m_TypesWithStaticFields;
            /// <summary>
            /// Types with a non-empty <see cref="CachedSnapshot.TypeDescriptionEntriesCache.StaticFieldSlots"/>
            /// row (format v20+) — resolved-address static fields, consumed directly by
            /// <see cref="CrawlStaticFieldSlots"/> rather than the byte-crawl path used for
            /// <see cref="TypesWithStaticFields"/>. Disjoint in practice from
            /// <see cref="TypesWithStaticFields"/>: v20+ files always write an empty
            /// <c>TypeDescriptions_StaticFieldBytes</c>, so <see cref="TypesWithStaticFields"/> is empty
            /// whenever this list is populated, and vice versa for pre-v20 files.
            /// </summary>
            public ref DynamicArray<int> TypesWithStaticFieldSlots => ref m_TypesWithStaticFieldSlots;
            DynamicArray<int> m_TypesWithStaticFieldSlots;
            public ref DynamicArray<StackCrawlData> CrawlDataStack => ref m_CrawlDataStack;
            DynamicArray<StackCrawlData> m_CrawlDataStack;
            public BlockList<ManagedConnection> ManagedConnections => CachedMemorySnapshot.CrawledData.Connections;
            public CachedSnapshot CachedMemorySnapshot { get; }
            NativeHashSet<int> m_TypesWithObjectsThatMayStillNeedNativeTypeConnection;
            public ref NativeHashSet<int> TypesWithObjectsThatMayStillNeedNativeTypeConnection => ref m_TypesWithObjectsThatMayStillNeedNativeTypeConnection;
            public ref FieldLayouts StaticFieldLayoutInfo => ref m_StaticFieldLayoutInfo;
            FieldLayouts m_StaticFieldLayoutInfo;
            bool m_DisposedStaticInfo = false;
            public ref FieldLayouts FieldLayouts => ref m_FieldLayouts;
            FieldLayouts m_FieldLayouts;
            public ref NativeObjectOrAllocationFinder NativeObjectOrAllocationFinder => ref m_NativeObjectOrAllocationFinder;
            NativeObjectOrAllocationFinder m_NativeObjectOrAllocationFinder;
            public MemoryProfilerSettings.FeatureFlags.CrawlerFlags ConfigurableCrawlerOptions;
            const int k_InitialStackSize = 256;

            public long MaxObjectIndexFindableViaPointers => MemoryProfilerSettings.FeatureFlags.ManagedCrawlerConsidersPointersToGCHandleHeldManagedObjects ? CachedMemorySnapshot.GcHandles.UniqueCount : 0;

            /// <summary>
            /// Object starts from the backend-provided object table, not created when the snapshot has none or no heap bytes to walk.
            /// CoreCLR captures static values after the heap, so a static can point at memory that holds no captured object;
            /// static-rooted pointers are only followed if they hit an object start listed here.
            /// </summary>
            public BackendObjectAddressIndex BackendObjects { get; }

            public IntermediateCrawlData(CachedSnapshot snapshot)
            {
                if (snapshot.HasBackendManagedGraph && snapshot.ManagedHeapSections.Count > 0)
                    BackendObjects = new BackendObjectAddressIndex(snapshot.ManagedHeapSections, snapshot.ManagedObjects.Target);

                CachedMemorySnapshot = snapshot;
                m_CrawlDataStack = new DynamicArray<StackCrawlData>(0, k_InitialStackSize, Allocator.Persistent, memClear: k_MemClearCrawlDataStack);
                m_TypesWithObjectsThatMayStillNeedNativeTypeConnection = new NativeHashSet<int>(100, Allocator.Persistent);

                m_TypesWithStaticFields = new DynamicArray<int>(0, 16, Allocator.Persistent);
                m_TypesWithStaticFieldSlots = new DynamicArray<int>(0, 16, Allocator.Persistent);
                for (int i = 0; i != snapshot.TypeDescriptions.Count; ++i)
                {
                    if (snapshot.TypeDescriptions.HasStaticFieldData(i))
                    {
                        m_TypesWithStaticFields.Push(i);
                    }
                    if (snapshot.TypeDescriptions.HasStaticFieldSlotData(snapshot, i))
                    {
                        m_TypesWithStaticFieldSlots.Push(i);
                    }
                }

                m_StaticFieldLayoutInfo = new FieldLayouts(snapshot.TypeDescriptions.Count, 3);
                m_FieldLayouts = new FieldLayouts(snapshot.TypeDescriptions.Count, 4);

                var nativeSortedAllocationAddress = new DynamicArray<ulong>(snapshot.NativeAllocations.Count, Allocator.Persistent);
                var nativeSortedAllocationSize = new DynamicArray<ulong>(snapshot.NativeAllocations.Count, Allocator.Persistent);
                var nativeSortedAllocationIndex = new DynamicArray<long>(snapshot.NativeAllocations.Count, Allocator.Persistent);
                for (long i = 0; i < snapshot.NativeAllocations.Count; i++)
                {
                    nativeSortedAllocationAddress[i] = snapshot.SortedNativeAllocations.Address(i);
                    nativeSortedAllocationSize[i] = snapshot.SortedNativeAllocations.FullSize(i);
                    nativeSortedAllocationIndex[i] = snapshot.SortedNativeAllocations[i];
                }

                m_NativeObjectOrAllocationFinder = new NativeObjectOrAllocationFinder(
                    addressToInstanceId: snapshot.NativeObjects.NativeObjectAddressToEntityId,
                    instanceIdToNativeObjectIndex: snapshot.NativeObjects.InstanceId2Index,
                    nativeSortedAllocationAddress: nativeSortedAllocationAddress,
                    nativeSortedAllocationSize: nativeSortedAllocationSize,
                    nativeSortedAllocationIndex: nativeSortedAllocationIndex);

                if (snapshot.TypeDescriptions.ITypeIntPtr > TypeDescriptionEntriesCache.ITypeInvalid)
                {
                    // Initialize field layout for IntPtr as the actual holding field is not of type IntPtr
                    // but we do want the FieldReferenceType to be IntPtr, so that we don't have to check its m_value field type for an asterix while builing field layouts for types using IntPtrs.
                    BuildFullFieldLayoutForCrawling(this, ref m_FieldLayouts, snapshot.TypeDescriptions.ITypeIntPtr, false);

                    // System.IntPtr can have no crawlable instance field (e.g. on CoreCLR), so there may be no entry to retype.
                    var intPtrIndex = m_FieldLayouts.FieldLayoutInfo.Count - 1;
                    if (intPtrIndex >= 0)
                    {
                        var intPtrRawFieldLayout = m_FieldLayouts.FieldLayoutInfo[intPtrIndex];
                        m_FieldLayouts.FieldLayoutInfo[intPtrIndex] = new FieldLayoutInfo(
                                remainingFieldCountForThisType: intPtrRawFieldLayout.RemainingFieldCountForThisType,
                                indexOfTheTypeOfTheReferenceOwner: intPtrRawFieldLayout.IndexOfTheTypeOfTheReferenceOwner,
                                indexOfTheTypeOwningTheActualFieldIndex: intPtrRawFieldLayout.IndexOfTheTypeOwningTheActualFieldIndex,
                                offsetFromPreviousAddress: intPtrRawFieldLayout.OffsetFromPreviousAddress,
                                fieldIndexOnReferenceOwner: intPtrRawFieldLayout.FieldIndexOnReferenceOwner,
                                actualFieldIndexOnPotentialNestedValueType: intPtrRawFieldLayout.ActualFieldIndexOnPotentialNestedValueType,
                                additionalFieldOffsetFromFieldOnReferenceOwnerToFieldOnValueType: intPtrRawFieldLayout.OffsetFromReferenceOwnerHeaderStartToFieldOnValueType,
                                fieldReferenceType: FieldReferenceType.IntPtr
                            );
                    }
                }
                ConfigurableCrawlerOptions = MemoryProfilerSettings.FeatureFlags.ConfigurableCrawlerOptions;
            }

            public void DisposeOfStaticFieldCrawlingHelpersEarly()
            {
                if (m_DisposedStaticInfo)
                    return;
                m_StaticFieldLayoutInfo.Dispose();
                m_DisposedStaticInfo = true;
            }

            public void Dispose()
            {
                m_FieldLayouts.Dispose();

                // This should have been disposed of immediately after crawling static fields
                Debug.Assert(m_DisposedStaticInfo, $"{nameof(DisposeOfStaticFieldCrawlingHelpersEarly)} wasn't called");

                m_CrawlDataStack.Dispose();
                m_NativeObjectOrAllocationFinder.Dispose();
                m_TypesWithObjectsThatMayStillNeedNativeTypeConnection.Dispose();
                m_TypesWithStaticFields.Dispose();
                m_TypesWithStaticFieldSlots.Dispose();
                if (BackendObjects.IsCreated)
                    BackendObjects.Dispose();
            }
        }
        public const string ConnectNativeToManageObjectProfilerMarkerName = "Crawler.ConnectNativeToManageObject";
        static readonly ProfilerMarker k_ConnectNativeToManageObjectProfilerMarker = new ProfilerMarker(ConnectNativeToManageObjectProfilerMarkerName);
        public const string PostProcessNonGCHeldObjectsProfilerMarkerName = "Crawler.PostProcessNonGCHeldObjects";
        static readonly ProfilerMarker k_PostProcessNonGCHeldObjectsProfilerMarker = new ProfilerMarker(PostProcessNonGCHeldObjectsProfilerMarkerName);
        public const string ConnectRemainingManagedTypesToNativeTypesProfilerMarkerName = "Crawler.ConnectRemainingManagedTypesToNativeTypes";
        static readonly ProfilerMarker k_ConnectRemainingManagedTypesToNativeTypesProfilerMarker = new ProfilerMarker(ConnectRemainingManagedTypesToNativeTypesProfilerMarkerName);
        public const string ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypesProfilerMarkerName = "Crawler.ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypes";
        static readonly ProfilerMarker k_ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypesProfilerMarker = new ProfilerMarker(ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypesProfilerMarkerName);

        public const string GatherIntermediateCrawlDataMarkerName = "GatherIntermediateCrawlData";
        static readonly ProfilerMarker k_GatherIntermediateCrawlDataMarker = new ProfilerMarker(GatherIntermediateCrawlDataMarkerName);
        public const string GatherManagedRootDataMarkerName = "GatherManagedRootData";
        static readonly ProfilerMarker k_GatherManagedRootDataMarker = new ProfilerMarker(GatherManagedRootDataMarkerName);
        public const string GatherFrozenHeapObjectDataMarkerName = "GatherFrozenHeapObjectData";
        static readonly ProfilerMarker k_GatherFrozenHeapObjectDataMarker = new ProfilerMarker(GatherFrozenHeapObjectDataMarkerName);

        // GCHandleEntriesCache guarantees every row targets a unique address (duplicates from pre-19.3
        // files are removed once at load, see GCHandleEntriesCache.RemoveDuplicateTargets) - every
        // GcHandles row index is pushed as-is, both as its ManagedObjectIndex and as the SourceIndex
        // handed to the crawl stack, with no separate discovery/dedup pass needed.
        static void GatherIntermediateCrawlData(CachedSnapshot snapshot, IntermediateCrawlData crawlData, ref DynamicArray<StackCrawlData> crawlerStack)
        {
            using var _ = k_GatherIntermediateCrawlDataMarker.Auto();
            ref var managedHeapSections = ref snapshot.ManagedHeapSections;
            var vmInfo = snapshot.VirtualMachineInformation;

            for (int i = 0; i != snapshot.GcHandles.Count; i++)
            {
                var target = snapshot.GcHandles.Target[i];
                var moi = new ManagedObjectInfo
                {
                    ManagedObjectIndex = i,
                    ITypeDescription = -1,
                    NativeObjectIndex = -1,
                };
                managedHeapSections.Find(target, vmInfo, out moi.data);

                snapshot.CrawledData.MangedObjectIndexByAddress.Add(target, moi.ManagedObjectIndex);
                crawlData.CachedMemorySnapshot.CrawledData.ManagedObjects.Push(moi);

                crawlerStack.Push(new StackCrawlData(target, referenceOwner: new SourceIndex(SourceIndex.SourceId.GCHandleIndex, i), bytesAndOffsets: moi.data), memClearForExcessExpansion: k_MemClearCrawlDataStack);
            }
        }

        /// <summary>
        /// Seeds discovery from <see cref="CachedSnapshot.ManagedRoots"/> (Stack / Finalizer / Other, and Handle
        /// as a fallback), in addition to <see cref="GatherIntermediateCrawlData"/>'s GcHandles-only seeding —
        /// closes the crawl-completeness gap where the byte walk never consumed these root kinds at all. Only
        /// runs when <see cref="CachedSnapshot.HasManagedRoots"/>; on older/Mono/IL2CPP files this is a no-op
        /// and GcHandles-only seeding is unchanged. Rows whose target is already claimed by the GcHandles pass
        /// (<see cref="ManagedData.MangedObjectIndexByAddress"/>) are skipped — first-claim wins, since a real
        /// object reachable from multiple roots only needs one discovery path.
        /// </summary>
        static void GatherManagedRootData(CachedSnapshot snapshot, IntermediateCrawlData crawlData, ref DynamicArray<StackCrawlData> crawlerStack)
        {
            if (!snapshot.HasManagedRoots)
                return;

            using var _ = k_GatherManagedRootDataMarker.Auto();
            var managedRoots = snapshot.ManagedRoots;
            ref var managedHeapSections = ref snapshot.ManagedHeapSections;
            var vmInfo = snapshot.VirtualMachineInformation;
            var objectsByAddress = snapshot.CrawledData.MangedObjectIndexByAddress;

            var resolvedManagedObjectIndices = new DynamicArray<long>(managedRoots.Count, Allocator.Persistent);
            if (resolvedManagedObjectIndices.Count > 0)
            {
                var invalidIndex = ManagedData.InvalidObjectIndex;
                unsafe { UnsafeUtility.MemCpyReplicate(resolvedManagedObjectIndices.GetUnsafePtr(), &invalidIndex, sizeof(long), (int)resolvedManagedObjectIndices.Count); }
            }

            for (long i = 0; i < managedRoots.Count; i++)
            {
                var target = managedRoots.Target[i];
                if (target == 0)
                    continue; // row's address couldn't be resolved from this file — not seedable, not a bug (see ManagedRootsEntriesCache.Target)

                if (objectsByAddress.TryGetValue(target, out var existingIndex))
                {
                    // Already discovered via GcHandles (or an earlier ManagedRoots row targeting the same
                    // address) — first-claim wins, this row just resolves to the existing object.
                    resolvedManagedObjectIndices[i] = existingIndex;
                    continue;
                }

                var moi = new ManagedObjectInfo
                {
                    ManagedObjectIndex = crawlData.CachedMemorySnapshot.CrawledData.ManagedObjects.Count,
                    ITypeDescription = -1,
                    NativeObjectIndex = -1,
                };
                managedHeapSections.Find(target, vmInfo, out moi.data);

                objectsByAddress.Add(target, moi.ManagedObjectIndex);
                resolvedManagedObjectIndices[i] = moi.ManagedObjectIndex;
                crawlData.CachedMemorySnapshot.CrawledData.ManagedObjects.Push(moi);

                crawlerStack.Push(new StackCrawlData(target, referenceOwner: new SourceIndex(SourceIndex.SourceId.ManagedRoot, i), bytesAndOffsets: moi.data), memClearForExcessExpansion: k_MemClearCrawlDataStack);
            }

            snapshot.CrawledData.ManagedRootIndexToManagedObjectIndex = resolvedManagedObjectIndices;
        }

        /// <summary>
        /// Seeds discovery for backend <see cref="CachedSnapshot.ManagedObjects"/> rows living on a heap section
        /// the GC exempts from normal per-object rooting, that neither the byte walk nor
        /// <see cref="GatherManagedRootData"/> ever finds — see <see cref="SourceIndex.SourceId.ManagedHeapSection"/>'s
        /// doc comment for which objects this covers and why the section's exemption is what keeps them alive.
        /// Records them in <see cref="ManagedData.FrozenHeapManagedObjectIndices"/>, which
        /// <see cref="RootAndImpactInfo"/> uses to seed one root per frozen heap object. Only
        /// runs when <see cref="CachedSnapshot.HasBackendManagedGraph"/> (CoreCLR; Mono / IL2CPP never populate
        /// <see cref="CachedSnapshot.ManagedObjects"/>, so there's nothing to scan here for them) and when
        /// per-section <see cref="ManagedMemorySectionEntriesCache.Flags"/> is populated (format v20+ — older
        /// files can't identify which sections are frozen/pinned, so there's nothing safe to seed).
        /// </summary>
        static void GatherFrozenHeapObjectData(CachedSnapshot snapshot, IntermediateCrawlData crawlData, ref DynamicArray<StackCrawlData> crawlerStack)
        {
            if (!snapshot.HasBackendManagedGraph || snapshot.ManagedHeapSections.Flags.Count == 0)
                return;

            using var _ = k_GatherFrozenHeapObjectDataMarker.Auto();
            var managedObjects = snapshot.ManagedObjects;
            ref var managedHeapSections = ref snapshot.ManagedHeapSections;
            var vmInfo = snapshot.VirtualMachineInformation;
            var objectsByAddress = snapshot.CrawledData.MangedObjectIndexByAddress;

            var sectionToManagedObjectIndex = new DynamicArray<long>(managedHeapSections.Count, Allocator.Persistent);
            if (sectionToManagedObjectIndex.Count > 0)
            {
                var invalidIndex = ManagedData.InvalidObjectIndex;
                unsafe { UnsafeUtility.MemCpyReplicate(sectionToManagedObjectIndex.GetUnsafePtr(), &invalidIndex, sizeof(long), (int)sectionToManagedObjectIndex.Count); }
            }
            var frozenHeapObjectIndices = new DynamicArray<long>(0, managedObjects.Count, Allocator.Persistent);

            for (long i = 0; i < managedObjects.Count; i++)
            {
                var target = managedObjects.Target[i];
                if (target == 0 || objectsByAddress.ContainsKey(target))
                    continue; // already discovered via GcHandles or ManagedRoots — first-claim wins

                var moi = new ManagedObjectInfo
                {
                    ManagedObjectIndex = crawlData.CachedMemorySnapshot.CrawledData.ManagedObjects.Count,
                    ITypeDescription = -1,
                    NativeObjectIndex = -1,
                };
                managedHeapSections.Find(target, vmInfo, out moi.data);
                if (!moi.data.IsValid)
                    continue; // heap bytes unloaded or address outside any known section — nothing to root against

                var sectionIndex = moi.data.HeapSectionIndex;
                var sectionFlag = managedHeapSections.Flags[sectionIndex];
                if (sectionFlag != HeapChunkType.NonGC && sectionFlag != HeapChunkType.PinnedObjectHeap)
                    continue; // lives on an ordinary GC section — not exempt from rooting, so not this function's population to seed

                objectsByAddress.Add(target, moi.ManagedObjectIndex);
                crawlData.CachedMemorySnapshot.CrawledData.ManagedObjects.Push(moi);

                if (sectionToManagedObjectIndex[sectionIndex] == ManagedData.InvalidObjectIndex)
                    sectionToManagedObjectIndex[sectionIndex] = moi.ManagedObjectIndex; // first object on this section not already claimed by another root - not necessarily the section's first frozen/pinned object
                frozenHeapObjectIndices.Push(moi.ManagedObjectIndex);

                crawlerStack.Push(new StackCrawlData(target, referenceOwner: new SourceIndex(SourceIndex.SourceId.ManagedHeapSection, sectionIndex), bytesAndOffsets: moi.data), memClearForExcessExpansion: k_MemClearCrawlDataStack);
            }

            snapshot.CrawledData.ManagedHeapSectionIndexToManagedObjectIndex = sectionToManagedObjectIndex;
            snapshot.CrawledData.FrozenHeapManagedObjectIndices = frozenHeapObjectIndices;
        }

        public const string CrawlHandleRootedObjectsMarkerName = "Crawl GC Handle Rooted Objects";
        static readonly ProfilerMarker k_CrawlHandleRootedObjectsMarker = new ProfilerMarker(CrawlHandleRootedObjectsMarkerName);
        public const string CrawlManagedRootRootedObjectsMarkerName = "Crawl Managed Root Rooted Objects";
        static readonly ProfilerMarker k_CrawlManagedRootRootedObjectsMarker = new ProfilerMarker(CrawlManagedRootRootedObjectsMarkerName);
        public const string CrawlFrozenHeapObjectsMarkerName = "Crawl Frozen Heap Objects";
        static readonly ProfilerMarker k_CrawlFrozenHeapObjectsMarker = new ProfilerMarker(CrawlFrozenHeapObjectsMarkerName);
        public const string CrawlStaticsMarker = "Crawl Static Fields";
        static readonly ProfilerMarker k_CrawlStaticsMarker = new ProfilerMarker(CrawlStaticsMarker);
        public const string CrawlStaticallyReferencedObjectsMarker = "Crawl Statically Rooted Objects";
        static readonly ProfilerMarker k_CrawlStaticallyReferencedObjectsMarker = new ProfilerMarker(CrawlStaticallyReferencedObjectsMarker);
        public const string CrawlUnityObjectConnectionsMarkerName = "Crawler mapping Unity Object connections";
        static readonly ProfilerMarker k_CrawlUnityObjectConnectionsMarker = new ProfilerMarker(CrawlUnityObjectConnectionsMarkerName);
        public const string CrawlFinalizeMarkerName = "Crawler Finalizing";
        static readonly ProfilerMarker k_CrawlFinalizeMarker = new ProfilerMarker(CrawlFinalizeMarkerName);
        public const int CrawlStepCount = 11;
        public static IEnumerator<EnumerationStatus> Crawl(CachedSnapshot snapshot, EnumerationStatus status)
        {
            Debug.Assert(!(snapshot.CrawledData?.Crawled ?? false), "Crawling an already crawled snapshot.");

            // No heap bytes to walk: build CrawledData directly from the backend-provided graph tables.
            if (snapshot.HasBackendManagedGraph && snapshot.ManagedHeapSections.Count == 0)
            {
                var graphOnly = CrawlFromBackendGraphWithoutHeapBytes(snapshot, status);
                while (graphOnly.MoveNext())
                    yield return graphOnly.Current;
                yield break;
            }

            using var crawlData = new IntermediateCrawlData(snapshot);
#if CRAWLER_PERFORMANCE_ANALYSIS
            var watch = Stopwatch.StartNew();
            Debug.Log("Starting Crawl");
#endif

            // Gather handles and duplicates and enqueue them first to reserve their Managed Object indices
            // Just hold of on actually parsing them until after static field parsing is done.
            yield return status.IncrementStep(stepStatus: "Enqueueing GC Handles for crawling.");

            // ensure that the managed heap data is ready for use to avoid calling Complete on the reader's job handle from within the static field crawler jobs, or within GatherIntermediateCrawlData
            crawlData.CachedMemorySnapshot.ManagedHeapSections.CompleteHeapBytesRead();

            // When heap bytes are present, the byte walk below is authoritative and self-sufficient, so the file's
            // managed graph is not prepopulated here (it would collide with GatherIntermediateCrawlData's GC-handle
            // duplicate detection) — it's used only for the DEBUG_VALIDATION cross-check below.

            var gcHandleHeldObjectsToCrawl = new DynamicArray<StackCrawlData>(0, snapshot.GcHandles.Count, Allocator.Persistent, memClear: k_MemClearCrawlDataStack);
            GatherIntermediateCrawlData(snapshot, crawlData, ref gcHandleHeldObjectsToCrawl);

            yield return status.IncrementStep(stepStatus: "Enqueueing Managed Roots for crawling.");

            var managedRootHeldObjectsToCrawl = new DynamicArray<StackCrawlData>(0, snapshot.ManagedRoots.Count, Allocator.Persistent, memClear: k_MemClearCrawlDataStack);
            GatherManagedRootData(snapshot, crawlData, ref managedRootHeldObjectsToCrawl);

            yield return status.IncrementStep(stepStatus: "Enqueueing frozen heap objects for crawling.");

            var frozenHeapObjectsToCrawl = new DynamicArray<StackCrawlData>(0, snapshot.ManagedObjects.Count, Allocator.Persistent, memClear: k_MemClearCrawlDataStack);
            GatherFrozenHeapObjectData(snapshot, crawlData, ref frozenHeapObjectsToCrawl);

            yield return status.IncrementStep(stepStatus: "Reading Managed Heap bytes");

            // Start with static fields and enqueue any heap objects held by their fields
            // For Memory Profiling purposes, we deem static fields to be the strongest binding roots.
            // Therefore, we parse these first to (later) primarily attribute statically held objects to their static fields
            yield return status.IncrementStep(stepStatus: "Crawling managed types with static fields");
            {
                CrawlStaticFields(crawlData);
                // Format v20+ resolved-address static fields (see CrawlStaticFieldSlots) — disjoint
                // from the byte-crawl path above in practice, since a v20+ file always writes an
                // empty TypeDescriptions_StaticFieldBytes (CrawlStaticFields no-ops on such a file).
                CrawlStaticFieldSlots(crawlData);
            }

            //crawl objects referenced by static fields
            yield return status.IncrementStep(stepStatus: "Crawling objects rooted by static fields.");
            CrawlStep(crawlData, k_CrawlStaticallyReferencedObjectsMarker);

            //crawl GC Handle held objects data
            yield return status.IncrementStep(stepStatus: "Crawling GC Handle held objects and their referenences.");
            crawlData.CrawlDataStack.PushRange(gcHandleHeldObjectsToCrawl, memClearForExcessExpansion: k_MemClearCrawlDataStack);
            gcHandleHeldObjectsToCrawl.Dispose();
            CrawlStep(crawlData, k_CrawlHandleRootedObjectsMarker);

            //crawl Managed Root (Stack/Finalizer/Other, and Handle fallback) held objects data
            yield return status.IncrementStep(stepStatus: "Crawling Managed Root held objects and their references.");
            crawlData.CrawlDataStack.PushRange(managedRootHeldObjectsToCrawl, memClearForExcessExpansion: k_MemClearCrawlDataStack);
            managedRootHeldObjectsToCrawl.Dispose();
            CrawlStep(crawlData, k_CrawlManagedRootRootedObjectsMarker);

            //crawl frozen heap objects (NonGC-heap objects with no root of their own)
            yield return status.IncrementStep(stepStatus: "Crawling frozen heap objects.");
            crawlData.CrawlDataStack.PushRange(frozenHeapObjectsToCrawl, memClearForExcessExpansion: k_MemClearCrawlDataStack);
            frozenHeapObjectsToCrawl.Dispose();
            CrawlStep(crawlData, k_CrawlFrozenHeapObjectsMarker);

            //crawl connection data
            yield return status.IncrementStep(stepStatus: "Mapping out Unity Object references");
            {
                using var _ = k_CrawlUnityObjectConnectionsMarker.Auto();
                ConnectNativeToManageObject(crawlData);
                PostProcessNonGCHeldObjects(crawlData);
                ConnectRemainingManagedTypesToNativeTypes(crawlData);
                AddupRawRefCount(snapshot);
            }

            //crawl connection data
            yield return status.IncrementStep(stepStatus: "Tallying up managed memory usage and creating connection maps");

            using var finalizerMarker = k_CrawlFinalizeMarker.Auto();
            snapshot.CrawledData.AddUpTotalMemoryUsageAndCacheStringPreviews(snapshot, snapshot.ManagedHeapSections);
            snapshot.CrawledData.CreateConnectionMaps(snapshot);

#if DEBUG_VALIDATION
            // When the backend also supplied the reference graph, the byte-derived edges should match it.
            if (snapshot.HasBackendManagedGraph)
                ValidateByteEdgesAgainstBackendGraph(snapshot);
#endif

            snapshot.CrawledData.FinishedCrawling();
#if CRAWLER_PERFORMANCE_ANALYSIS
            Debug.Log($"Finished Crawling in {watch.ElapsedMilliseconds}ms");
#endif
        }

        public const string CrawlFromBackendGraphMarkerName = "Crawler building managed graph from backend tables";
        static readonly ProfilerMarker k_CrawlFromBackendGraphMarker = new ProfilerMarker(CrawlFromBackendGraphMarkerName);

        /// <summary>
        /// Build <see cref="CachedSnapshot.CrawledData"/> from the backend-provided managed graph tables when
        /// there are no heap bytes to walk (managed-only snapshot). Objects come from
        /// <see cref="CachedSnapshot.ManagedObjects"/> (sizes are the type's minimum heap size - exact array /
        /// string sizes need bytes and are unavailable), and connections are synthesized directly from
        /// <see cref="CachedSnapshot.ManagedReferences"/> WITHOUT field provenance (FieldFrom == -1). Edges whose
        /// target row is <see cref="Format.QueriedSnapshot.PackedIndexReader.InvalidIndex"/> (uncaptured frozen /
        /// non-GC-heap objects) are skipped. Native/managed / type stitching still runs.
        /// </summary>
        static void BuildManagedObjectsFromBackendGraph(CachedSnapshot snapshot)
        {
            var backendObjects = snapshot.ManagedObjects;
            var typeDescriptions = snapshot.TypeDescriptions;
            var vmInfo = snapshot.VirtualMachineInformation;
            ref var objectList = ref snapshot.CrawledData.ManagedObjects;
            var objectsByAddress = snapshot.CrawledData.MangedObjectIndexByAddress;

            // Both containers start sized for GcHandles.Count, which undercounts the full object set here; reserve
            // up front to avoid repeated doubling/rehash as objects are pushed below.
            objectList.Reserve(backendObjects.Count);
            if (objectsByAddress.Capacity < backendObjects.Count)
                objectsByAddress.Capacity = (int)backendObjects.Count;

            for (long i = 0; i < backendObjects.Count; i++)
            {
                var address = backendObjects.Target[i];
                var typeIndex = (int)backendObjects.TypeIndex[i];
                if (typeIndex < 0 || typeIndex >= typeDescriptions.Count)
                    typeIndex = TypeDescriptionEntriesCache.ITypeInvalid;

                var moi = new ManagedObjectInfo
                {
                    ManagedObjectIndex = i,
                    PtrObject = address,
                    ITypeDescription = typeIndex,
                    NativeObjectIndex = -1,
                    Size = typeIndex >= 0 ? typeDescriptions.GetMinSizeForHeapObjectOfType(typeIndex, in vmInfo) : 0,
                };
                objectList.Push(moi);
                objectsByAddress.TryAdd(address, i);
            }
        }

        /// <summary>
        /// Builds the GCHandle-index -&gt; <see cref="ManagedObjectInfo.ManagedObjectIndex"/> map by address, since
        /// <see cref="BuildManagedObjectsFromBackendGraph"/> stores objects in raw backend file order rather than
        /// the byte walk's handle-targets-first order that callers like <see cref="ObjectData.FromManagedObjectIndex"/>
        /// otherwise assume.
        /// </summary>
        static void BuildGCHandleToManagedObjectIndexMapFromBackendGraph(CachedSnapshot snapshot)
        {
            snapshot.CrawledData.ObjectIndicesMatchGCHandleOrder = false;

            var handleCount = snapshot.GcHandles.Count;
            var map = new DynamicArray<long>(handleCount, Allocator.Persistent);
            var targets = new NativeHashSet<long>((int)handleCount, Allocator.Persistent);
            var objectsByAddress = snapshot.CrawledData.MangedObjectIndexByAddress;
            for (long i = 0; i < handleCount; i++)
            {
                if (objectsByAddress.TryGetValue(snapshot.GcHandles.Target[i], out var managedObjectIndex))
                {
                    map[i] = managedObjectIndex;
                    targets.Add(managedObjectIndex);
                }
                else
                    map[i] = ManagedData.InvalidObjectIndex; // handle target has no ManagedObjectInfo row (e.g. collected mid-capture)
            }
            snapshot.CrawledData.GCHandleIndexToManagedObjectIndex = map;
            snapshot.CrawledData.ManagedObjectIndicesThatAreGCHandleTargets = targets;
        }

        /// <summary>
        /// Synthesizes bare managed-to-managed connections (no field provenance) from the backend reference table.
        /// Edges to uncaptured source/target objects are skipped.
        /// </summary>
        static void SynthesizeConnectionsFromBackendGraph(CachedSnapshot snapshot, IntermediateCrawlData crawlData)
        {
            var backendReferences = snapshot.ManagedReferences;
            var managedObjectCount = snapshot.CrawledData.ManagedObjects.Count;
            for (long i = 0; i < backendReferences.Count; i++)
            {
                var from = backendReferences.FromIndex[i];
                var to = backendReferences.ToIndex[i];
                // Skip edges whose source or target object was not captured (e.g. frozen / non-GC-heap targets).
                if (from < 0 || to < 0)
                    continue;
                if (from >= managedObjectCount || to >= managedObjectCount)
                {
#if DEBUG_VALIDATION
                    Debug.LogWarning($"ManagedReferences row {i} has an out-of-range index (from={from}, to={to}, count={managedObjectCount}); skipping. The snapshot file may be corrupted.");
#endif
                    continue;
                }

                crawlData.ManagedConnections.Add(ManagedConnection.MakeConnection(snapshot,
                    new SourceIndex(SourceIndex.SourceId.ManagedObject, from),
                    new SourceIndex(SourceIndex.SourceId.ManagedObject, to),
                    fromField: -1, valueTypeFieldOwningITypeDescription: -1, valueTypeFieldFrom: -1,
                    offsetFromReferenceOwnerHeaderStartToFieldOnValueType: 0, arrayIndexFrom: -1));

                // The RefCount on a managed object is otherwise incremented by the byte walk when it records
                // an incoming edge; replicate that here so downstream views see consistent counts.
                ref var target = ref snapshot.CrawledData.ManagedObjects[to];
                ++target.RefCount;
            }
        }

        static IEnumerator<EnumerationStatus> CrawlFromBackendGraphWithoutHeapBytes(CachedSnapshot snapshot, EnumerationStatus status)
        {
            using var _ = k_CrawlFromBackendGraphMarker.Auto();
            using var crawlData = new IntermediateCrawlData(snapshot);

            // Static-field crawl helpers aren't used on this path; dispose them up front to satisfy the disposal invariant.
            yield return status.IncrementStep(stepStatus: "Preparing backend-graph crawl.");
            crawlData.DisposeOfStaticFieldCrawlingHelpersEarly();

            // Steps that need `ref` locals live in non-iterator helpers, since an iterator method can't hold them (CS8176).
            yield return status.IncrementStep(stepStatus: "Building managed objects from backend graph.");
            BuildManagedObjectsFromBackendGraph(snapshot);

            yield return status.IncrementStep(stepStatus: "Mapping GC Handles to managed objects.");
            BuildGCHandleToManagedObjectIndexMapFromBackendGraph(snapshot);

            yield return status.IncrementStep(stepStatus: "Synthesizing managed references from backend graph.");
            SynthesizeConnectionsFromBackendGraph(snapshot, crawlData);

            yield return status.IncrementStep(stepStatus: "Mapping out Unity Object references.");
            {
                using var __ = k_CrawlUnityObjectConnectionsMarker.Auto();
                snapshot.GcHandles.UniqueCount = snapshot.GcHandles.Count;
                ConnectNativeToManagedShellsFromBackendGraph(crawlData);
                RegisterLeakedShellTypesFromBackendGraph(crawlData);
            }

            yield return status.IncrementStep(stepStatus: "Connecting remaining managed types to native types.");
            {
                using var __ = k_CrawlUnityObjectConnectionsMarker.Auto();
                ConnectRemainingManagedTypesToNativeTypes(crawlData);
                AddupRawRefCount(snapshot);
            }

            yield return status.IncrementStep(stepStatus: "Tallying up managed memory usage.");
            using var finalizerMarker = k_CrawlFinalizeMarker.Auto();
            snapshot.CrawledData.AddUpTotalMemoryUsageAndCacheStringPreviews(snapshot, snapshot.ManagedHeapSections);

            yield return status.IncrementStep(stepStatus: "Creating connection maps.");
            snapshot.CrawledData.CreateConnectionMaps(snapshot);
            snapshot.CrawledData.FinishedCrawling();
        }

#if DEBUG_VALIDATION
        /// <summary>
        /// Asserts the byte-derived managed-to-managed edge set matches the backend-provided
        /// <see cref="CachedSnapshot.ManagedReferences"/> set. Compares by object address rather than row index,
        /// since the byte crawl's <see cref="ManagedObjectInfo.ManagedObjectIndex"/> order need not match the
        /// file's object table order. Logs one warning with the divergence counts and the most common
        /// source type -> target type groups, each with a sample edge and the source field holding it.
        /// </summary>
        static void ValidateByteEdgesAgainstBackendGraph(CachedSnapshot snapshot)
        {
            var backendObjects = snapshot.ManagedObjects;
            var backendReferences = snapshot.ManagedReferences;
            if (backendReferences.Count == 0)
                return;

            var fileEdges = new HashSet<(ulong, ulong)>();
            for (long i = 0; i < backendReferences.Count; i++)
            {
                var from = backendReferences.FromIndex[i];
                var to = backendReferences.ToIndex[i];
                // Skip edges to uncaptured targets (frozen / non-GC-heap objects with no object-table row):
                // the byte walk can't produce a matching edge, so they aren't a divergence.
                if (from < 0 || to < 0)
                    continue;
                fileEdges.Add((backendObjects.Target[from], backendObjects.Target[to]));
            }

            var byteEdges = new HashSet<(ulong, ulong)>();
            ref var managedObjects = ref snapshot.CrawledData.ManagedObjects;
            var connections = snapshot.CrawledData.Connections;
            for (long i = 0; i < connections.Count; i++)
            {
                var c = connections[i];
                if (c.IndexFrom.Id == SourceIndex.SourceId.ManagedObject && c.IndexTo.Id == SourceIndex.SourceId.ManagedObject)
                    byteEdges.Add((managedObjects[c.IndexFrom.Index].PtrObject, managedObjects[c.IndexTo.Index].PtrObject));
            }

            var backendOnly = new List<(ulong From, ulong To)>();
            foreach (var e in fileEdges)
            {
                if (!byteEdges.Contains(e))
                    backendOnly.Add(e);
            }
            var byteOnly = new List<(ulong From, ulong To)>();
            foreach (var e in byteEdges)
            {
                if (!fileEdges.Contains(e))
                    byteOnly.Add(e);
            }
            if (backendOnly.Count == 0 && byteOnly.Count == 0)
                return;

            var backendRowByAddress = new Dictionary<ulong, long>();
            for (long i = 0; i < backendObjects.Count; i++)
                backendRowByAddress[backendObjects.Target[i]] = i;

            var message = new StringBuilder($"Managed reference graph divergence: {backendOnly.Count} of {fileEdges.Count} backend references were not produced by the byte crawl, "
                + $"{byteOnly.Count} of {byteEdges.Count} byte-crawled references are missing from the backend graph.");
            AppendTopDivergenceGroups(message, "Backend-only", backendOnly, snapshot, backendRowByAddress);
            AppendTopDivergenceGroups(message, "Byte-crawl-only", byteOnly, snapshot, backendRowByAddress);
            Debug.LogWarning(message.ToString());
        }

        static void AppendTopDivergenceGroups(StringBuilder message, string label, List<(ulong From, ulong To)> edges, CachedSnapshot snapshot, Dictionary<ulong, long> backendRowByAddress)
        {
            const int k_MaxGroups = 10;
            if (edges.Count == 0)
                return;

            var groups = new Dictionary<(int SourceType, bool SourceCrawled, int TargetType), (long Count, ulong From, ulong To)>();
            foreach (var e in edges)
            {
                var key = (GetTypeIndex(snapshot, backendRowByAddress, e.From), snapshot.CrawledData.MangedObjectIndexByAddress.ContainsKey(e.From), GetTypeIndex(snapshot, backendRowByAddress, e.To));
                groups[key] = groups.TryGetValue(key, out var group) ? (group.Count + 1, group.From, group.To) : (1, e.From, e.To);
            }

            var sortedGroups = new List<KeyValuePair<(int SourceType, bool SourceCrawled, int TargetType), (long Count, ulong From, ulong To)>>(groups);
            sortedGroups.Sort((a, b) => b.Value.Count.CompareTo(a.Value.Count));
            message.Append($"\n{label} references by source type -> target type, top {Math.Min(k_MaxGroups, sortedGroups.Count)} of {sortedGroups.Count} groups:");
            for (int i = 0; i < sortedGroups.Count && i < k_MaxGroups; i++)
            {
                var key = sortedGroups[i].Key;
                var value = sortedGroups[i].Value;
                message.Append($"\n  {value.Count}x {GetTypeName(snapshot, key.SourceType)}{(key.SourceCrawled ? "" : " (source not crawled)")} -> {GetTypeName(snapshot, key.TargetType)}, "
                    + $"e.g. 0x{value.From:X} -> 0x{value.To:X}: {DescribeReferencingField(snapshot, backendRowByAddress, value.From, value.To)}");
            }
        }

        static string GetTypeName(CachedSnapshot snapshot, int typeIndex)
        {
            return typeIndex >= 0 ? snapshot.TypeDescriptions.TypeDescriptionName[typeIndex] : "<unknown type>";
        }

        static int GetTypeIndex(CachedSnapshot snapshot, Dictionary<ulong, long> backendRowByAddress, ulong address)
        {
            if (snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(address, out var crawledIndex))
                return snapshot.CrawledData.ManagedObjects[crawledIndex].ITypeDescription;
            if (backendRowByAddress.TryGetValue(address, out var backendRow))
                return (int)snapshot.ManagedObjects.TypeIndex[backendRow];
            return -1;
        }

        // Scans the source object's bytes for the target address and names the instance field at that offset.
        static string DescribeReferencingField(CachedSnapshot snapshot, Dictionary<ulong, long> backendRowByAddress, ulong from, ulong to)
        {
            const long k_MaxScannedBytes = 4096;
            if (!snapshot.FindHeapBytes(from, out var bytes, out var referenceScope))
                return "source bytes not captured";

            using (referenceScope)
            {
                var pointerSize = (long)snapshot.VirtualMachineInformation.PointerSize;
                var typeIndex = GetTypeIndex(snapshot, backendRowByAddress, from);
                long objectSize = 0;
                if (snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(from, out var crawledIndex))
                    objectSize = snapshot.CrawledData.ManagedObjects[crawledIndex].Size;
                if (objectSize <= 0)
                    objectSize = SizeOfObjectInBytes(snapshot, typeIndex, typeIndex, bytes, snapshot.ManagedHeapSections, ignoreErrorsBecauseObjectIsSpeculative: true);
                if (objectSize <= 0)
                    return "source object size unknown";
                var size = Math.Min(objectSize, k_MaxScannedBytes);

                for (long offset = 0; offset + pointerSize <= size && bytes.CouldFitAllocation(offset + pointerSize); offset += pointerSize)
                {
                    if (bytes.Add((ulong)offset).TryReadPointer(out var value) != BytesAndOffset.PtrReadError.Success || value != to)
                        continue;

                    if (typeIndex >= 0)
                    {
                        foreach (var field in snapshot.TypeDescriptions.FieldIndicesInstance[typeIndex])
                        {
                            if (snapshot.FieldDescriptions.Offset[field] == offset)
                                return $"field '{snapshot.FieldDescriptions.FieldDescriptionName[field]}' (+0x{offset:X})";
                        }
                    }
                    return $"+0x{offset:X} (array element or nested field)";
                }
                return $"target not in the first {size} source bytes";
            }
        }
#endif

        static void CrawlStep(IntermediateCrawlData crawlData, ProfilerMarker profilerMarker)
        {
            using var _ = profilerMarker.Auto();
            while (crawlData.CrawlDataStack.Count > 0)
            {
                CrawlPointer(crawlData);
            }
        }

        static void CrawlStaticFields(IntermediateCrawlData crawlData)
        {
            ////This function is setting up what is the multithreaded equivalent to:
            //for (int i = 0; i < crawlData.TypesWithStaticFields.Count; i++)
            //{
            //    var iTypeDescription = crawlData.TypesWithStaticFields[i];
            //    var bytesOffset = new BytesAndOffset(snapshot.TypeDescriptions.StaticFieldBytes[iTypeDescription], snapshot.VirtualMachineInformation.PointerSize);
            //    if (iTypeDescription == 163632)
            //        Debug.Log($"Crawling {snapshot.TypeDescriptions.TypeDescriptionName[iTypeDescription]}'s static fields");
            //    CrawlRawObjectData(crawlData, bytesOffset, iTypeDescription, true, new SourceIndex(SourceIndex.SourceId.ManagedType, iTypeDescription), allowStackExpansion: true);
            //}
            //// Make some space, we're done with these
            //crawlData.DisposeOfStaticFieldCrawlingHelpersEarly();
            //return;

            using var _ = k_CrawlStaticsMarker.Auto();
            var snapshot = crawlData.CachedMemorySnapshot;
            var virtualMachineInformation = snapshot.VirtualMachineInformation;
            using var typeIndexToCrawlerDataIndex = new TypeIndexToCrawlerDataIndexHashMap((int)crawlData.TypesWithStaticFields.Count, Allocator.Persistent);
            using var staticTypeFieldCrawlerData = new DynamicArray<StaticFieldsCrawlerJobChunk.StaticTypeFieldCrawlerData>(
                0, k_IdealJobCount, Allocator.Persistent);
            long totalStaticFieldCount = 0L;

            ref var managedObjectIndexByAddress = ref snapshot.CrawledData.MangedObjectIndexByAddress;
            ref var nativeObjectOrAllocationFinder = ref crawlData.NativeObjectOrAllocationFinder;

            // build full static field crawling data
            ref var typesWithStaticFields = ref crawlData.TypesWithStaticFields;
            for (long iTypeWithStaticFields = 0; iTypeWithStaticFields < typesWithStaticFields.Count; ++iTypeWithStaticFields)
            {
                var iTypeDescription = typesWithStaticFields[iTypeWithStaticFields];
                var fieldLayouts = GetFullFieldLayoutForCrawling(crawlData, iTypeDescription, useStaticFields: true);
                if (fieldLayouts.Count <= 0)
                    continue;
                var bytesOffset = new BytesAndOffset(snapshot.TypeDescriptions.StaticFieldBytes[iTypeDescription], snapshot.VirtualMachineInformation.PointerSize, iTypeDescription, true);
                var fieldLayoutIndex = crawlData.StaticFieldLayoutInfo.GetFieldLayoutInfoIndex(iTypeDescription);
                if (fieldLayoutIndex < 0)
                {
                    Debug.LogError("Could not retrieve a valid field layout index.");
                    continue;
                }
                typeIndexToCrawlerDataIndex.Add(iTypeDescription, staticTypeFieldCrawlerData.Count);
                staticTypeFieldCrawlerData.Push(new StaticFieldsCrawlerJobChunk.StaticTypeFieldCrawlerData
                {
                    TypeIndex = iTypeDescription,
                    FieldLayoutIndex = fieldLayoutIndex,
                    Bytes = bytesOffset,
                });
                totalStaticFieldCount += fieldLayouts.Count;
            }

            // unlikely early out
            if (totalStaticFieldCount == 0)
            {
                crawlData.DisposeOfStaticFieldCrawlingHelpersEarly();
                return;
            }

            // split the crawling work up into chunks
            // Note: we don't expect static fields to ever burst the upper limit of concurrent k_MaxConcurrentJobCount / k_MaxStackMemoryUsageForJobifiedCrawling so, not much point in complicating the solution here
            var chunkCount = CalculateBatchElementCount(totalStaticFieldCount);
            var targetBatchSize = totalStaticFieldCount / chunkCount;
            var jobChunks = new DynamicArray<StaticFieldsCrawlerJobChunk>
                (chunkCount, Allocator.Persistent);
            var firstField = 0L;
            var firstDataEntry = 0L;
            var fieldLayoutInfos = crawlData.StaticFieldLayoutInfo.FieldLayoutInfo;
            for (int i = 0; i < chunkCount; i++)
            {
                var lastField = (i == chunkCount - 1) ? totalStaticFieldCount - 1 : Math.Min(firstField + targetBatchSize, totalStaticFieldCount - 1);
                lastField += fieldLayoutInfos[lastField].RemainingFieldCountForThisType;
                var lastTypeIndex = fieldLayoutInfos[lastField].IndexOfTheTypeOfTheReferenceOwner;
                var lastDataEntryIndex = typeIndexToCrawlerDataIndex[lastTypeIndex];

                var fieldCount = lastField - firstField;
                var stack = new DynamicArray<StackCrawlData>(0, fieldCount, Allocator.Persistent, memClear: k_MemClearCrawlDataStack);
                DynamicArrayRef<FieldLayoutInfo> thisChunksFieldLayouts;
                DynamicArrayRef<StaticFieldsCrawlerJobChunk.StaticTypeFieldCrawlerData> thisChunksData;
                unsafe
                {
                    var startLayout = fieldLayoutInfos.GetUnsafeTypedPtr();
                    thisChunksFieldLayouts = DynamicArrayRef<FieldLayoutInfo>.ConvertExistingDataToDynamicArrayRef(startLayout + firstField, fieldCount);

                    var startData = staticTypeFieldCrawlerData.GetUnsafeTypedPtr();
                    thisChunksData = DynamicArrayRef<StaticFieldsCrawlerJobChunk.StaticTypeFieldCrawlerData>.ConvertExistingDataToDynamicArrayRef(startData + firstDataEntry, lastDataEntryIndex - firstDataEntry);
                }
                jobChunks[i] = new(in snapshot.ManagedHeapSections, in virtualMachineInformation, in managedObjectIndexByAddress, in nativeObjectOrAllocationFinder, crawlData.ConfigurableCrawlerOptions, ref stack,
                    in thisChunksData, in thisChunksFieldLayouts, crawlData.MaxObjectIndexFindableViaPointers);
                firstField = lastField + 1;
                firstDataEntry = lastDataEntryIndex + 1;
            }
            var mainJob = new ChunkedParallelArrayCrawlerJob<StaticFieldsCrawlerJobChunk>(ref jobChunks);

            var handle = mainJob.ScheduleByRef(mainJob.ChunkCount, 1);

            // and wait for the jobs to finish
            handle.Complete();
            mainJob.Finish(crawlData);

            // Make some space, we're done with these
            crawlData.DisposeOfStaticFieldCrawlingHelpersEarly();
        }

        /// <summary>
        /// Crawls static fields from the v20+ TypeDescriptions_StaticFieldSlots data. Runs sequentially because it
        /// pushes onto the shared, non-parallel-safe crawlData.CrawlDataStack.
        /// A reference-typed slot holds the pointer; a value-typed slot holds the value's bytes inline, walked with
        /// the field layout (no header). Slot groups pair with FieldIndicesOwnedStatic[iType] by position; a zero-count group contributes nothing.
        /// </summary>
        static void CrawlStaticFieldSlots(IntermediateCrawlData crawlData)
        {
            using var _ = k_CrawlStaticsMarker.Auto();
            var snapshot = crawlData.CachedMemorySnapshot;
            var pointerSize = snapshot.VirtualMachineInformation.PointerSize;
            var maxObjectIndexFindableViaPointers = crawlData.MaxObjectIndexFindableViaPointers;
            var structLayout = new DynamicArray<FieldLayoutInfo>(0, 4, Allocator.Temp);
            try
            {
                ref var typesWithStaticFieldSlots = ref crawlData.TypesWithStaticFieldSlots;
                for (long iTypeWithSlots = 0; iTypeWithSlots < typesWithStaticFieldSlots.Count; ++iTypeWithSlots)
                {
                    var iTypeDescription = typesWithStaticFieldSlots[iTypeWithSlots];
                    var ownedStaticFields = snapshot.TypeDescriptions.FieldIndicesOwnedStatic[iTypeDescription];
                    var blob = snapshot.TypeDescriptions.StaticFieldSlots[iTypeDescription];
                    var slotByteSizes = snapshot.TypeDescriptions.GetStaticFieldSlotByteSizes(snapshot, iTypeDescription);
                    var slotGroups = snapshot.TypeDescriptions.GetStaticFieldSlotGroups(snapshot, iTypeDescription);
                    if (slotGroups.Count != ownedStaticFields.Count)
                    {
                        Debug.LogError($"TypeDescriptions_StaticFieldSlots group count ({slotGroups.Count}) doesn't match " +
                            $"FieldIndicesOwnedStatic count ({ownedStaticFields.Count}) for type index {iTypeDescription}. Skipping.");
                        continue;
                    }

                    var referenceOwner = new SourceIndex(SourceIndex.SourceId.ManagedType, iTypeDescription);
                    var blobBytesAndOffset = new BytesAndOffset(blob, pointerSize, iTypeDescription, isStaticType: true);
                    for (int fieldOrdinal = 0; fieldOrdinal < ownedStaticFields.Count; ++fieldOrdinal)
                    {
                        var fieldIndex = ownedStaticFields[fieldOrdinal];
                        var fieldTypeIndex = snapshot.FieldDescriptions.TypeIndex[fieldIndex];
                        var group = slotGroups[fieldOrdinal];
                        if (group.Count == 0)
                            continue;

                        // CoreCLR PTR/FNPTR and array-shaped statics store the pointee/element type in
                        // TypeIndex, not the field's own type - same misclassification risk
                        // BuildFieldLayoutEntryForField already guards against for instance fields.
                        var elementType = snapshot.FieldDescriptions.ElementType[fieldIndex];
                        bool isPointerLikeField = FieldDescriptionEntriesCache.IsPointerLikeElementType(elementType, true);
                        bool isReferenceByElementType = isPointerLikeField
                            || FieldDescriptionEntriesCache.IsArrayShapedElementType(elementType, true);
                        bool isValueTypeField = !isReferenceByElementType && fieldTypeIndex >= 0 && snapshot.TypeDescriptions.HasFlag(fieldTypeIndex, TypeFlags.kValueType);
                        int slotByteSize = slotByteSizes[fieldOrdinal];

                        // The slot IS the value's bytes - walk its own field layout, same machinery an
                        // instance value-type field already uses (BuildFieldLayoutEntryForField), but built
                        // directly here (not via CrawlRawObjectData(fieldTypeIndex, ...)) so the resulting
                        // connections attribute back to the true outer static field (fieldIndex) rather than
                        // one of the struct's own field indices, and so
                        // OffsetFromReferenceOwnerHeaderStartToFieldOnValueType comes out relative to this
                        // field's own start (offset 0) - the coordinate space
                        // ObjectData.GetFieldByFieldDescriptionsIndex's nested-field search expects, matching
                        // how an ordinary instance value-type field is walked (no header to skip either way,
                        // since a static slot has no object header). Built once per field, reused for every
                        // thread-static slot below - the layout itself doesn't depend on which slot's bytes
                        // it's later walked against.
                        if (isValueTypeField)
                        {
                            structLayout.Clear(false);
                            BuildFieldLayoutEntryForField(crawlData, ref structLayout, iTypeDescription, useStaticFields: true, objectHeaderSizeSkippedByCrawler: 0, fieldIndex, fieldOffset: 0);
                            if (structLayout.Count > 1)
                                DynamicArrayAlgorithms.IntrospectiveSort(structLayout, 0, structLayout.Count);
                            for (var i = structLayout.Count - 1; i >= 1; --i)
                                structLayout[i] = new FieldLayoutInfo(structLayout[i], structLayout[i - 1].OffsetFromPreviousAddress, 0);
                            if (structLayout.Count > 0)
                                structLayout[0] = new FieldLayoutInfo(structLayout[0], 0, 0);
                        }

                        for (int slot = 0; slot < group.Count; ++slot)
                        {
                            var slotBytesAndOffset = blobBytesAndOffset.Add((ulong)(group.SlotDataOffset + slot * slotByteSize));

                            if (isValueTypeField)
                            {
                                DynamicArrayRef<FieldLayoutInfo> structLayoutRef = structLayout;
                                CrawlRawObjectData(
                                    in snapshot.ManagedHeapSections, in snapshot.VirtualMachineInformation, in snapshot.CrawledData.MangedObjectIndexByAddress,
                                    in crawlData.NativeObjectOrAllocationFinder, crawlData.ConfigurableCrawlerOptions,
                                    ref crawlData.CrawlDataStack, in structLayoutRef, in slotBytesAndOffset, in referenceOwner, maxObjectIndexFindableViaPointers,
                                    allowStackExpansion: true);
                            }
                            else if (isPointerLikeField)
                            {
                                // A raw native pointer (PTR/FNPTR), not a managed reference - route through
                                // the same CrawlRawFieldData path an instance PTR/FNPTR field uses, so a
                                // target that isn't a managed object gets checked against
                                // NativeObjectOrAllocationFinder instead of being dropped.
                                if (slotBytesAndOffset.TryReadPointer(out var address) != BytesAndOffset.PtrReadError.Success
                                    || address == 0)
                                    continue;
                                CrawlRawFieldData(address, FieldReferenceType.Ptr, maxObjectIndexFindableViaPointers,
                                    in snapshot.CrawledData.MangedObjectIndexByAddress, in snapshot.ManagedHeapSections, in snapshot.VirtualMachineInformation,
                                    ref crawlData.CrawlDataStack, in crawlData.NativeObjectOrAllocationFinder, crawlData.ConfigurableCrawlerOptions,
                                    iTypeDescription, in referenceOwner,
                                    allowStackExpansion: true,
                                    fieldIndexOfTheFieldOnTheReferenceOwner: fieldIndex);
                            }
                            else
                            {
                                if (slotBytesAndOffset.TryReadPointer(out var address) != BytesAndOffset.PtrReadError.Success
                                    || address == 0)
                                    continue;
                                // Resolve the target's heap bytes now (same as every other crawl-root push -
                                // see the GC-handle path above) rather than leaving Bytes unset: CrawlPointer's
                                // new-discovery branch has no fallback resolution step for a plain managed
                                // reference with an invalid BytesAndOffset and no native counterpart, so an
                                // unresolved push here is silently dropped instead of discovering the object.
                                snapshot.ManagedHeapSections.Find(address, snapshot.VirtualMachineInformation, out BytesAndOffset targetBytesAndOffset);
                                crawlData.CrawlDataStack.Push(new StackCrawlData(address,
                                    bytesAndOffsets: targetBytesAndOffset,
                                    referenceOwner: referenceOwner,
                                    fieldIndexOfTheFieldOnTheReferenceOwner: fieldIndex),
                                    memClearForExcessExpansion: k_MemClearCrawlDataStack);
                            }
                        }
                    }
                }
            }
            finally
            {
                structLayout.Dispose();
            }
        }

        static int CalculateBatchElementCount(long totalCount, long minBatchSize = MinFieldCountPerJobChunk, long maxBatchSize = k_MaxFieldCountPerJobChunk, long idealBatchCount = k_IdealJobCount)
        {
            if (totalCount / idealBatchCount > maxBatchSize)
                // Round up to avoid a batch size bigger than max (not an exact science, and that's fine)
                return UnityEngine.Mathf.RoundToInt((float)totalCount / maxBatchSize + 0.5f);
            else if (totalCount / minBatchSize <= idealBatchCount)
                // Round down to avoid a batch size smaller than min (not an exact science, and that's fine)
                return Math.Max(1, UnityEngine.Mathf.RoundToInt((float)totalCount / minBatchSize - 0.5f));
            else
                return (int)idealBatchCount;
        }

        static void AddupRawRefCount(CachedSnapshot snapshot)
        {
            for (long i = 0; i != snapshot.Connections.Count; ++i)
            {
                var connectionTo = snapshot.Connections.To[i];
                switch (connectionTo.Id)
                {
                    case SourceIndex.SourceId.ManagedObject:
                    {
                        ref var objRef = ref snapshot.CrawledData.ManagedObjects[connectionTo.Index];
                        ++objRef.RefCount;

#if DEBUG_VALIDATION
                        // This whole if block is here to make the investigations for PROF-2420 easier.
                        // It does not manage to fix up all faulty managed objects, but can add extra context to some.
                        if (snapshot.CrawledData.ManagedObjects[connectionTo.Index].NativeObjectIndex == -1)
                        {
                            var connectionFrom = snapshot.Connections.From[i];
                            if (connectionFrom.Id is SourceIndex.SourceId.NativeObject)
                            {
                                var iMissingNativeFrom = connectionFrom.Index;
                                objRef.NativeObjectIndex = iMissingNativeFrom;
                                var nativeTypeIndex = snapshot.NativeObjects.NativeTypeArrayIndex[iMissingNativeFrom];
                                var typeInfo = nativeTypeIndex is not NativeTypeEntriesCache.InvalidTypeIndex ? snapshot.TypeDescriptions.UnifiedTypeInfoNative[nativeTypeIndex] : UnifiedType.Invalid;
                                if (objRef.ITypeDescription is TypeDescriptionEntriesCache.ITypeInvalid && typeInfo.ManagedTypeIndex is not TypeDescriptionEntriesCache.ITypeInvalid
                                    // ignore it if the managed type registered for this is just base object.
                                    && typeInfo.ManagedTypeIndex != snapshot.TypeDescriptions.ITypeUnityObject)
                                    objRef.ITypeDescription = typeInfo.ManagedTypeIndex;

                                var GCHandleReported = snapshot.CrawledData.InvalidManagedObjectsReportedViaGCHandles.ContainsKey(objRef.ManagedObjectIndex);
                                snapshot.CrawledData.InvalidManagedObjectsReportedViaGCHandles.Remove(objRef.ManagedObjectIndex);

                                // We are not validating all GCHandle held managed objects here, only those held by native objects.
                                // As we lock native object creation while reporting the managed data and native objects, this should be guaranteed.

                                Debug.LogError($"Found a Managed Object that was reported because a Native Object held {(GCHandleReported ? "a GCHandle" : "some other kind of reference")} to it, " +
                                    $"with a target pointing at {(objRef.data.IsValid ? "a valid" : "an invalid")} managed heap section. " +
                                    $"The Native Object named {snapshot.NativeObjects.ObjectName[iMissingNativeFrom]} was of type {snapshot.NativeTypes.TypeName[nativeTypeIndex]}. " +
                                    (objRef.ITypeDescription < 0 ? "No Managed base type was found"
                                    : $"The Managed Type was set to the managed base type {snapshot.TypeDescriptions.TypeDescriptionName[objRef.ITypeDescription]} as a stop gap."));
                            }
                        }
#endif
                        break;
                    }
                    case SourceIndex.SourceId.NativeObject:
                    {
                        ++snapshot.NativeObjects.RefCount[connectionTo.Index];
                        break;
                    }
                    default:
                        break;
                }
            }
            // remove the "&& false" bits when analysing Managed Heap data reporting fixes
            // ATM this is not expected to work, as there is a possible delay between dumping the heap chunks and reporting GCHandles
            // and scripting_gc_start_world() is called inbetween of these as it is needed for reporting managed Types and current reporting order places GCHandle reporting after Type reporting.
#if DEBUG_VALIDATION && false
            // This is here to make the investigations for PROF-2420 easier.
            if(snapshot.CrawledData.InvalidManagedObjectsReportedViaGCHandles.Count > 0)
                Debug.LogError($"There are {snapshot.CrawledData.InvalidManagedObjectsReportedViaGCHandles.Count} Managed Objects that were reported as part of GCHandles that could not be reunited with their native object by the Managed Crawler.");
#endif
        }

        static void ConnectNativeToManageObject(IntermediateCrawlData crawlData)
        {
            using var marker = k_ConnectNativeToManageObjectProfilerMarker.Auto();
            var snapshot = crawlData.CachedMemorySnapshot;
            ref readonly var uninstantiatableUnityBaseTypes = ref snapshot.TypeDescriptions.UninstantiatableUnityBaseTypesWithSetNativeType;
            ref readonly var managedTypeInfos = ref snapshot.TypeDescriptions.UnifiedTypeInfoManaged;
            ref var objectInfos = ref snapshot.CrawledData.ManagedObjects;

            if (snapshot.TypeDescriptions.Count == 0)
                return;

            // Field on UnityEngine.Object linking a managed shell to its native object: the native object's
            // EntityId (current runtimes) or a raw native pointer (m_CachedPtr, snapshots from older runtimes).
            var nativeLinkOffset = snapshot.TypeDescriptions.IFieldUnityObjectNativeLinkOffset;
            var nativeLinkIsEntityId = snapshot.TypeDescriptions.UnityObjectNativeLinkIsEntityId;
            var nativeLinkOffsetPlusFieldSize = nativeLinkOffset + snapshot.TypeDescriptions.UnityObjectNativeLinkFieldSize;

#if DEBUG_VALIDATION
            // These are used to double-check that all Native -> Managed connections reported via GCHandles on Native Objects are correctly found via the native link field (m_EntityId / legacy m_CachedPtr)
            var firstManagedToNativeConnection = snapshot.CrawledData.Connections.Count;
            var managedObjectAddressToNativeObjectIndex = new Dictionary<ulong, long>();
#endif

            var gcHandleHeldManagedObjectCount = Math.Min(snapshot.GcHandles.UniqueCount, objectInfos.Count);

            // First, only check the objects that could be held by a native object
            for (long i = 0; i < gcHandleHeldManagedObjectCount; i++)
            {
                ref var objectInfo = ref objectInfos[i];
#if DEBUG_VALIDATION
                Debug.Assert(objectInfo.NativeObjectIndex != 0
                    // native object index 0 should only be set for GC Handle reported object with index 0, otherwise it points at faulty data due to missing initialization
                    || (snapshot.NativeObjects.GCHandleIndexToIndex.TryGetValue(i, out var nativeObjectIndexFromGCHandleIndex) && nativeObjectIndexFromGCHandleIndex == 0),
                    "NativeObjectIndex has been left uninitialized.");
#endif
                if (objectInfo.ITypeDescription is TypeDescriptionEntriesCache.ITypeInvalid)
                    continue;

                //Must derive of unity Object
                ref readonly var typeInfo = ref managedTypeInfos[objectInfo.ITypeDescription];

                var isUnityType = typeInfo.IsUnityObjectType;
                var isOrCouldBeAUnityObject = isUnityType || objectInfo.NativeObjectIndex >= 0;

                if (!isUnityType && objectInfo.ITypeDescription >= 0)
                {
                    // A non Unity Object type object that is held by a GCHandle (i.e. has an index lower than GcHandles.UniqueCount could be an array of a type
                    // attributed with [UsedByNativeCode] or [RequiredByNativeCode]
                    var objInfo = ObjectData.FromManagedObjectInfo(snapshot, objectInfo);
                    var arrayInfo = objInfo.dataType == ObjectDataType.Array ? objInfo.GetArrayInfo(snapshot) : null;
                    var isEmptyArray = arrayInfo != null && arrayInfo.Length == 0 && arrayInfo.Rank.Length == 1;
                    if (isEmptyArray &&
                            snapshot.TypeDescriptions.TypeDescriptionName[objInfo.managedTypeIndex].StartsWith("Unity"))
                    {
                        // To be fair, we're not 100% sure its [UsedByNativeCode] or [RequiredByNativeCode] or indeed any of these,
                        // but practically those are the only type of reasons objects fitting this patterns are held by a GCHandle
                        snapshot.CrawledData.IndicesOfManagedObjectsHeldByRequiredByNativeCodeAttribute.Add(i);
                    }
                    else if (isEmptyArray && snapshot.TypeDescriptions.ITypeUnityEditorEditor > 0 // for non player captures the Editor type would not be found
                        && snapshot.TypeDescriptions.DerivesFrom(arrayInfo.ElementTypeDescription, snapshot.TypeDescriptions.ITypeUnityEditorEditor, false))
                    {
                        // This is likely held via GCHandle because of a [CustomEditor] attribute
                        // For now, it is not getting disambiguated from [RequiredByNativeCode] as the effect is the same
                        snapshot.CrawledData.IndicesOfManagedObjectsHeldByRequiredByNativeCodeAttribute.Add(i);
                    }
                    // there seem to be no further instance of this at the moment. Keeping this around for debugging purposes though
                    //else if (isEmptyArray)
                    //{
                    //    var typeFields = snapshot.TypeDescriptions.FieldIndicesInstance[arrayInfo.ElementTypeDescription];
                    //    var baseTypeIsUnityType = false;
                    //    foreach (var field in typeFields)
                    //    {
                    //        if (field == snapshot.TypeDescriptions.IFieldUnityObjectNativeLink)
                    //        {
                    //            baseTypeIsUnityType = true;
                    //            break;
                    //        }
                    //    }
                    //    Debug.Log($"Found an empty array {(baseTypeIsUnityType?"(with UnityType base type) ":"")}of type {snapshot.TypeDescriptions.TypeDescriptionName[objectInfo.ITypeDescription]}");
                    //}
                    else if (arrayInfo == null)
                    {
                        // Checking field information only makes sense for non array types, as otherwise the array's base type fields are checked which could be misleading

                        // If this managed object was reported because someone had a GC Handle on it, chances are pretty good that there is a Native Object behind this
                        // Given that the type isn't yet known to be a UnityObjectType, something might've gone wrong with the TypeDescription reporting
                        // (annecdotal evidence suggests that ScriptableSingletons residing in assemblies other that the Editor Assembly could be affected this way
                        // If this looks to be the case, try to patch the data back up as good as possible
                        var typeFields = snapshot.TypeDescriptions.FieldIndicesInstance[objectInfo.ITypeDescription];
                        foreach (var field in typeFields)
                        {
                            if (field == snapshot.TypeDescriptions.IFieldUnityObjectNativeLink)
                            {
                                isOrCouldBeAUnityObject = true;
#if DEBUG_VALIDATION
                                Debug.LogError($"Found an object of type {snapshot.TypeDescriptions.TypeDescriptionName[objectInfo.ITypeDescription]} with a UnityEngine.Object native link field but no parent type that would indicate that it is a Unity Object type.");
#endif
                                break;
                            }
                        }
                        if (!isOrCouldBeAUnityObject)
                            crawlData.CachedMemorySnapshot.CrawledData.IndicesOfPureManagedObjectsHeldByNonNativeObjectRelatedGCHandle.Add(i);
                    }
                }
                if (isOrCouldBeAUnityObject)
                {
                    // TODO: Add index to a list of Managed Unity Objects here
                    if (objectInfo.NativeObjectIndex < 0)
                    {
                        var instanceID = CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone;
                        var heapSection = objectInfo.data;
                        if (heapSection.IsValid && heapSection.CouldFitAllocation(nativeLinkOffsetPlusFieldSize))
                            heapSection = heapSection.Add((ulong)nativeLinkOffset);
#if DEBUG_VALIDATION
                        else // in case there was a broken resolution of the heap section on first crawl, which should only be possible to happen if it was a GC Handle held item, that did not resolve to a valid managed object
                        {
                            if (snapshot.ManagedHeapSections.Find(objectInfo.PtrObject + (ulong)nativeLinkOffset, snapshot.VirtualMachineInformation, out heapSection)
                                && heapSection.CouldFitAllocation(snapshot.TypeDescriptions.UnityObjectNativeLinkFieldSize))
                                Debug.LogError("Managed Object didn't have valid heap associated");
                        }
#endif
                        if (!heapSection.IsValid)
                        {
                            // Don't warn if this was an attempt to fix broken data
                            if (isUnityType)
                                Debug.LogWarning("Managed object (addr:" + objectInfo.PtrObject + ", index:" + objectInfo.ManagedObjectIndex + ") does not have data at native link field offset(" + nativeLinkOffset + ")");
                        }
                        else if (nativeLinkIsEntityId)
                        {
                            // The shell stores the native object's EntityId. An unbound shell (fake null, or its
                            // object destroyed) has EntityId.None, or an id no native object in the snapshot has.
                            instanceID = EntityId.FromULong(heapSection.ReadUInt64());
                        }
                        else
                        {
                            if (heapSection.TryReadPointer(out var cachedPtr) == BytesAndOffset.PtrReadError.Success)
                                snapshot.NativeObjects.NativeObjectAddressToEntityId.GetOrInitializeValue(cachedPtr, out instanceID, CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone);
                        }

                        if (instanceID != CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone)
                        {
                            if (snapshot.NativeObjects.InstanceId2Index.GetOrInitializeValue(instanceID, out objectInfo.NativeObjectIndex, -1))
                            {
                                snapshot.NativeObjects.ManagedObjectIndex[objectInfo.NativeObjectIndex] = (int)i;
                            }
                        }
                    }
                    if (objectInfo.NativeObjectIndex >= 0)
                    {
                        if (typeInfo.NativeTypeIndex is NativeTypeEntriesCache.InvalidTypeIndex
                            || uninstantiatableUnityBaseTypes.Contains(typeInfo.ManagedBaseTypeIndexForNativeType))
                        {
                            var nativeTypeIndex = snapshot.NativeObjects.NativeTypeArrayIndex[objectInfo.NativeObjectIndex];

                            if (!isUnityType)
                            {
                                if (nativeTypeIndex == snapshot.NativeTypes.MonoBehaviourIdx
                                    || nativeTypeIndex == snapshot.NativeTypes.ScriptableObjectIdx
                                    || nativeTypeIndex == snapshot.NativeTypes.EditorScriptableObjectIdx)
                                {
                                    // This actually WAS a Unity Object with faulty type data reporting, fix up UnityObjectTypeIndexToNativeTypeIndex,
                                    // but set native type to -1 so that ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypes can fix up all managed base types that ARE reported
                                    snapshot.TypeDescriptions.UpdateManagedToNativeTypeMapping(snapshot.NativeTypes, objectInfo.ITypeDescription, -1);
                                    isUnityType = true;
                                }
                                else
                                {
#if DEBUG_VALIDATION
                                    Debug.LogWarning("Managed object (addr:" + objectInfo.PtrObject + ", index:" + objectInfo.ManagedObjectIndex + ") looked like it could have been a Unity Object with a faultily reported managed type but wasn't a ScriptableObject or MonoBehaviour");
#endif
                                    // As a safeguard measure, in case that there are objects:
                                    // - with GCHandles on them
                                    // - with a field at the same position as m_CachedHandle
                                    // - which contains data that looks like a valid address to a valid native object
                                    // but which isn't likely to have broken reported type data (aka is not a scriptable type)
                                    // Ignore it and BAIL!
                                    objectInfo.NativeObjectIndex = -1;
                                    continue;
                                }
                            }
                            // This will also update the "ref readonly var" typeInfo.NativeTypeIndex for usage later down this function,
                            // thereby avoiding adding it to TypesWithObjectsThatMayStillNeedNativeTypeConnection for later type stitching.
                            ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypes(snapshot, nativeTypeIndex, objectInfo.ITypeDescription);
                        }
                        if (snapshot.HasConnectionOverhaul)
                        {
                            crawlData.ManagedConnections.Add(ManagedConnection.MakeUnityEngineObjectConnection(objectInfo.NativeObjectIndex, objectInfo.ManagedObjectIndex));
                            // The Asset GC checks all UnityEngine.Objects on the heap for collectability
                            // (outside of the GCHandle held by the Native Object).
                            // If a Managed shell isn't collectable, it adds to the ref count
                            // (we increase the ref count regardless of collectability as the selection details already discount self references for us)
                            // of the Native Object it points to via its m_EntityId (legacy snapshots: m_CachedPtr).
                            // Other managed pointers do not contribute to the ref count.
                            // Note: before the connection overhaul, the reported connections parsed in AddupRawRefCount already included Managed -> Native references, adding to the ref count there
                            ++snapshot.NativeObjects.RefCount[objectInfo.NativeObjectIndex];

                            if (nativeLinkIsEntityId)
                            {
                                // The generic crawler only finds Managed -> Native references through pointer-typed fields.
                                // With a legacy m_CachedPtr that produced a "managed shell -> its native object" reference
                                // (with field info) while crawling the shell; an EntityId is not a pointer, so add that
                                // reference here to keep the (unconsolidated) reference views identical.
                                crawlData.ManagedConnections.Add(ManagedConnection.MakeConnection(snapshot,
                                    new SourceIndex(SourceIndex.SourceId.ManagedObject, objectInfo.ManagedObjectIndex),
                                    new SourceIndex(SourceIndex.SourceId.NativeObject, objectInfo.NativeObjectIndex),
                                    snapshot.TypeDescriptions.IFieldUnityObjectNativeLink,
                                    valueTypeFieldOwningITypeDescription: -1, valueTypeFieldFrom: -1,
                                    offsetFromReferenceOwnerHeaderStartToFieldOnValueType: 0, arrayIndexFrom: -1));
                            }
#if DEBUG_VALIDATION
                            managedObjectAddressToNativeObjectIndex.Add(objectInfo.PtrObject, objectInfo.NativeObjectIndex);
#endif
                        }
                    }
                    else
                    {
                        snapshot.CrawledData.IndicesOfManagedPotentialUnityObjectsHeldByNonNativeObjectRelatedGCHandle.Add(i);
                    }
                    if (isUnityType && !crawlData.TypesWithObjectsThatMayStillNeedNativeTypeConnection.Contains(objectInfo.ITypeDescription)
                        && (typeInfo.NativeTypeIndex is NativeTypeEntriesCache.InvalidTypeIndex
                        || typeInfo.NativeTypeIndex == snapshot.NativeTypes.BaseObjectIdx
                        || uninstantiatableUnityBaseTypes.Contains(typeInfo.ManagedBaseTypeIndexForNativeType)))
                    {
                        // make a note of the failure to connect this object's type to its native type
                        // after all objects were connected, the types that are still not connected can then
                        // be filtered by those types that had actual object instances in the snapshot
                        crawlData.TypesWithObjectsThatMayStillNeedNativeTypeConnection.Add(objectInfo.ITypeDescription);
                    }
                }
                //else
                //{
                // TODO: Add index to a list of Pure C# Objects here
                //}
            }

#if DEBUG_VALIDATION
            // While there can be GCHandles pointing at invalid managed objects due to restarting the GC and scripting threads after taking a snapshot of the heap bytes
            // and before reporting GCHandles, the GCHandles associated with native objects should always point at valid managed objects.
            // Or at least for snapshots taken since the fix for UUM-31235 landed, which suspends the PreloadManager (and a later PR that locks object creation) for the duration of reporting both managed data and native objects.

            // Double-check that all Native -> Managed connections reported via GCHandles on Native Objects have been correctly found via the native link field
            if (snapshot.Connections.IndexOfFirstNativeToGCHandleConnection >= 0)
            {
                for (long nativeConnectionIndex = snapshot.Connections.IndexOfFirstNativeToGCHandleConnection; nativeConnectionIndex < snapshot.Connections.Count; nativeConnectionIndex++)
                {
                    var nativeObjectIndex = snapshot.Connections.From[nativeConnectionIndex];
                    Checks.CheckEqualsEnum(nativeObjectIndex.Id, SourceIndex.SourceId.NativeObject);
                    Checks.CheckEqualsEnum(snapshot.Connections.To[nativeConnectionIndex].Id, SourceIndex.SourceId.ManagedObject);
                    var managedShellAddress = snapshot.GcHandles.Target[snapshot.Connections.To[nativeConnectionIndex].Index];
                    if (snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(managedShellAddress, out var managedObjectIndex))
                    {
                        var managedObject = snapshot.CrawledData.ManagedObjects[managedObjectIndex];
                        if (managedObject.NativeObjectIndex != nativeObjectIndex.Index)
                            Debug.LogError("Native Object is not correctly linked with its Managed Object via NativeObjectIndex");
                        bool foundConnection = managedObjectAddressToNativeObjectIndex.ContainsKey(managedShellAddress);
                        if (!foundConnection)
                            Debug.LogError("Native Object is not correctly linked with its Managed Object via ManagedObjectAddrress");
                    }
                }
            }
#endif
        }

        /// <summary>
        /// Byte-free equivalent of <see cref="ConnectNativeToManageObject"/> for the backend-graph / no-heap-bytes
        /// path: walks the file's GC-handle -&gt; native-object map
        /// (<see cref="CachedSnapshot.NativeObjectEntriesCache.GCHandleIndexToIndex"/>) and resolves each handle's
        /// target through the address map to the managed shell row, instead of reading the native link field (<c>m_EntityId</c>) from heap bytes.
        /// </summary>
        static void ConnectNativeToManagedShellsFromBackendGraph(IntermediateCrawlData crawlData)
        {
            using var marker = k_ConnectNativeToManageObjectProfilerMarker.Auto();
            var snapshot = crawlData.CachedMemorySnapshot;
            if (snapshot.TypeDescriptions.Count == 0)
                return;

            ref var objectInfos = ref snapshot.CrawledData.ManagedObjects;
            ref readonly var managedTypeInfos = ref snapshot.TypeDescriptions.UnifiedTypeInfoManaged;
            ref readonly var uninstantiatableUnityBaseTypes = ref snapshot.TypeDescriptions.UninstantiatableUnityBaseTypesWithSetNativeType;

            // GCHandleIndexToIndex maps gcHandleIndex -> nativeObjectIndex for every native object that owns a handle.
            var handleToNative = snapshot.NativeObjects.GCHandleIndexToIndex;
            var enumerator = handleToNative.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var gcHandleIndex = enumerator.Current.Key;
                var nativeObjectIndex = enumerator.Current.Value;
                if (gcHandleIndex < 0 || gcHandleIndex >= snapshot.GcHandles.Count)
                    continue;

                var managedObjectIndex = snapshot.CrawledData.ResolveManagedObjectIndexForGCHandle(gcHandleIndex);
                if (managedObjectIndex == ManagedData.InvalidObjectIndex)
                    continue;

                ref var objectInfo = ref objectInfos[managedObjectIndex];
                objectInfo.NativeObjectIndex = nativeObjectIndex;
                snapshot.NativeObjects.ManagedObjectIndex[nativeObjectIndex] = (int)managedObjectIndex;

                // Stitch managed type -> native type when the managed type isn't associated yet.
                if (objectInfo.ITypeDescription >= 0)
                {
                    ref readonly var typeInfo = ref managedTypeInfos[objectInfo.ITypeDescription];
                    if (typeInfo.NativeTypeIndex is NativeTypeEntriesCache.InvalidTypeIndex)
                    {
                        var nativeTypeIndex = snapshot.NativeObjects.NativeTypeArrayIndex[nativeObjectIndex];
                        ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypes(snapshot, nativeTypeIndex, objectInfo.ITypeDescription);
                    }

                    // A type still resolving to no (or an uninstantiatable base) native type needs a deferred stitch attempt later.
                    if (typeInfo.IsUnityObjectType && !crawlData.TypesWithObjectsThatMayStillNeedNativeTypeConnection.Contains(objectInfo.ITypeDescription)
                        && (typeInfo.NativeTypeIndex is NativeTypeEntriesCache.InvalidTypeIndex
                        || uninstantiatableUnityBaseTypes.Contains(typeInfo.ManagedBaseTypeIndexForNativeType)))
                    {
                        crawlData.TypesWithObjectsThatMayStillNeedNativeTypeConnection.Add(objectInfo.ITypeDescription);
                    }
                }

                if (snapshot.HasConnectionOverhaul)
                {
                    crawlData.ManagedConnections.Add(ManagedConnection.MakeUnityEngineObjectConnection(nativeObjectIndex, managedObjectIndex));
                    ++snapshot.NativeObjects.RefCount[nativeObjectIndex];
                }
            }
        }

        /// <summary>
        /// Byte-free equivalent of <see cref="PostProcessNonGCHeldObjects"/> for the backend-graph / no-heap-bytes
        /// path: <see cref="PostProcessNonGCHeldObjects"/>'s GC-handle-held-objects-first partition doesn't hold
        /// here (<see cref="BuildManagedObjectsFromBackendGraph"/> uses raw file order instead), so this visits
        /// every object <see cref="ConnectNativeToManagedShellsFromBackendGraph"/> left untouched
        /// (<see cref="ManagedObjectInfo.NativeObjectIndex"/> still -1), i.e. leaked managed shells with no GC handle.
        /// </summary>
        static void RegisterLeakedShellTypesFromBackendGraph(IntermediateCrawlData crawlData)
        {
            using var marker = k_PostProcessNonGCHeldObjectsProfilerMarker.Auto();
            var snapshot = crawlData.CachedMemorySnapshot;
            ref var objectInfos = ref snapshot.CrawledData.ManagedObjects;
            ref readonly var managedTypeInfos = ref snapshot.TypeDescriptions.UnifiedTypeInfoManaged;
            ref readonly var uninstantiatableUnityBaseTypes = ref snapshot.TypeDescriptions.UninstantiatableUnityBaseTypesWithSetNativeType;

            for (long i = 0; i < objectInfos.Count; i++)
            {
                ref var objectInfo = ref objectInfos[i];
                if (objectInfo.NativeObjectIndex != -1 || objectInfo.ITypeDescription is TypeDescriptionEntriesCache.ITypeInvalid)
                    continue;

                ref readonly var typeInfo = ref managedTypeInfos[objectInfo.ITypeDescription];
                if (typeInfo.IsUnityObjectType && !crawlData.TypesWithObjectsThatMayStillNeedNativeTypeConnection.Contains(objectInfo.ITypeDescription)
                    && (typeInfo.NativeTypeIndex is NativeTypeEntriesCache.InvalidTypeIndex
                    || uninstantiatableUnityBaseTypes.Contains(typeInfo.ManagedBaseTypeIndexForNativeType)))
                {
                    crawlData.TypesWithObjectsThatMayStillNeedNativeTypeConnection.Add(objectInfo.ITypeDescription);
                }
            }
        }

        /// <summary>
        /// Needs to be called before <see cref="ConnectRemainingManagedTypesToNativeTypes(IntermediateCrawlData)"/>
        /// as it might add more managed types that need reattatching to their native types (e.g. because the only instances that exist of them are Leaked Shells)
        /// </summary>
        /// <param name="crawlData"></param>
        static void PostProcessNonGCHeldObjects(IntermediateCrawlData crawlData)
        {
            using var marker = k_PostProcessNonGCHeldObjectsProfilerMarker.Auto();
            var snapshot = crawlData.CachedMemorySnapshot;
            ref var objectInfos = ref snapshot.CrawledData.ManagedObjects;
            ref readonly var uninstantiatableUnityBaseTypes = ref snapshot.TypeDescriptions.UninstantiatableUnityBaseTypesWithSetNativeType;

            using var cachedListOfSpeculativeManagedObjectIndicesToClear = new DynamicArray<long>(0, 20, Allocator.Temp);

            var gcHandleHeldManagedObjectCount = Math.Min(snapshot.GcHandles.UniqueCount, objectInfos.Count);
            // simpler checks for every object that index wise can impossibly be held by a Native Object
            for (long i = gcHandleHeldManagedObjectCount; i < objectInfos.Count; i++)
            {
                ref var objectInfo = ref objectInfos[i];
                if (objectInfo.ITypeDescription is TypeDescriptionEntriesCache.ITypeInvalid)
                    continue;

                var typeInfo = snapshot.TypeDescriptions.UnifiedTypeInfoManaged[objectInfo.ITypeDescription];

                if (typeInfo.IsUnityObjectType && !crawlData.TypesWithObjectsThatMayStillNeedNativeTypeConnection.Contains(objectInfo.ITypeDescription)
                    // the type info should usually always point at a native type but that could be the base Object type
                    && (typeInfo.NativeTypeIndex is NativeTypeEntriesCache.InvalidTypeIndex
                    // So also check that the Managed type for this object is actually one that could have objects of its type in memory,
                    // instead of this just being UnityEngine.Object or a different scripting fallback type.
                    || uninstantiatableUnityBaseTypes.Contains(typeInfo.ManagedBaseTypeIndexForNativeType)))
                {
                    // make a note of the failure to connect this object's type to its native type
                    // after all objects were connected, the types that are still not connected can then
                    // be filtered by those types that had actual object instances in the snapshot
                    crawlData.TypesWithObjectsThatMayStillNeedNativeTypeConnection.Add(objectInfo.ITypeDescription);
                }
                // Only GC Handle held objects should have a NativeObjectIndex other than -1
                Checks.CheckEquals(objectInfo.NativeObjectIndex, -1); // "NativeObjectIndex has been set for a non GCHandle held object."
            }
        }

        /// <summary>
        /// This method serves a double purpose:
        /// 1. It iterates up the entire managed inheritance chain of the passed managed type and connects every managed type along the way to its base native type.
        ///    This is the only way to find the managed to native type connection for managed types like UnityEngine.ScriptableObject or UnityEngine.MonoBehaviour,
        ///    that will never have an instance of their base types in the snapshot, but an object of a derived type is _nearly_ guaranteed.
        /// 2. As the snapshot does not report the connection from a Native Type to the Managed Base Type, this function checks if viable Managed types
        ///    that it iterates over could be the managed Base Type
        ///
        /// Beyond checking that <see cref="CachedSnapshot.TypeDescriptions.UnifiedTypeInfoManaged"/> previously mapped the managed type to -1,
        /// no further checks are needed before calling and it stops as soon as it hits the first managed base type that already has an association.
        /// </summary>
        /// <param name="snapshot"></param>
        /// <param name="nativeTypeIndex"></param>
        /// <param name="managedType"></param>
        static void ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypes(CachedSnapshot snapshot, int nativeTypeIndex, int managedType)
        {
            if (nativeTypeIndex == -1)
                return;
            using var marker = k_ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypesProfilerMarker.Auto();
            // This Method links up the Managed Types to the Native Type and, while at it, seeks to link up the Native Type to the Managed Base Type
            // E.g. the Managed Base Type for a user created component is 'UnityEngine.MonoBehaviour'. No Instances of that exact type will ever be in a capture.
            // Though there will be multiple derived Managed Types, we only need to connect the Native Type 'Monobehaviour' to the Managed Base Type 'UnityEngine.MonoBehaviour' once.
            // Whether or not we still need to do that doesn't change during the while loop, and not rechecking if it is needed is an optimization, so only check this once here.
            var oldManagedBaseTypeIndexForNativeType = snapshot.TypeDescriptions.UnifiedTypeInfoNative[nativeTypeIndex].ManagedBaseTypeIndexForNativeType;
            ref readonly var uninstantiatableUnityBaseTypes = ref snapshot.TypeDescriptions.UninstantiatableUnityBaseTypesWithSetNativeType;
            var nativeUnityObjectTypeIndexToManagedBaseTypeIsNotYetReported =
                oldManagedBaseTypeIndexForNativeType is TypeDescriptionEntriesCache.ITypeInvalid
                || uninstantiatableUnityBaseTypes.Contains(oldManagedBaseTypeIndexForNativeType);

            var newlyFoundManagedBaseType = TypeDescriptionEntriesCache.ITypeInvalid;
            var originalManagedTypeToUpdate = managedType;

            while (managedType >= 0 &&
                (snapshot.TypeDescriptions.UnifiedTypeInfoManaged[managedType].NativeTypeIndex is NativeTypeEntriesCache.InvalidTypeIndex
                || uninstantiatableUnityBaseTypes.Contains(snapshot.TypeDescriptions.UnifiedTypeInfoManaged[managedType].ManagedBaseTypeIndexForNativeType)))
            {
                // Register the managed type connection to the native base type
                //
                // EditorScriptableObject is a fake native type stand-in for ScriptableObjects of types that are located in Editor Only assemblies.
                // The Managed Type UnityEngine.ScriptableObject should not be tracked as derived from this fake native type
                // as there are likely managed derivatives of it that are both located in Editor Only assemblies or not, but on the managed side,
                // they are not tracked as different types.
                // Their derived types are still necessarily EditorScriptableObject (as they are located in and Editor Only assembly).
                // So just ignore the link between the exact types of UnityEngine.ScriptableObject (managed) and EditorScriptableObject (native) here,
                // to avoid confusion or unstable type mapping results of the managed UnityEngine.ScriptableObject type.
                if (!(nativeTypeIndex == snapshot.NativeTypes.EditorScriptableObjectIdx && managedType == snapshot.TypeDescriptions.ITypeUnityScriptableObject))
                {
                    snapshot.TypeDescriptions.UpdateManagedToNativeTypeMapping(snapshot.NativeTypes, managedType, nativeTypeIndex,
                        // only update the base type here if it wasn't yet reported
                        baseFallbackType: nativeUnityObjectTypeIndexToManagedBaseTypeIsNotYetReported
                        // and if it wasn't updated through this process, as the inhereting managed types should not have a higher level base type set
                        && newlyFoundManagedBaseType is not TypeDescriptionEntriesCache.ITypeInvalid
                        ? oldManagedBaseTypeIndexForNativeType : TypeDescriptionEntriesCache.ITypeInvalid);
                }

                // Check if this could be the still unreported Managed Base Type for this Native Type
                if (nativeUnityObjectTypeIndexToManagedBaseTypeIsNotYetReported)
                {
                    // Check if this managed object's managed type could map directly to a Unity owned native type
                    var typeName = snapshot.TypeDescriptions.TypeDescriptionName[managedType];
                    if (typeName.StartsWith("Unity"))
                    {
                        var startOfNamespaceStrippedManagedTypeName = typeName.LastIndexOf('.') + 1;
                        var managedTypeNameLength = typeName.Length - startOfNamespaceStrippedManagedTypeName;
                        var nativeTypeNameLength = snapshot.NativeTypes.TypeName[nativeTypeIndex].Length;
                        if (managedTypeNameLength == nativeTypeNameLength)
                        {
                            unsafe
                            {
                                fixed (char* nativeName = snapshot.NativeTypes.TypeName[nativeTypeIndex], managedName = typeName)
                                {
                                    // no need to create a bunch of managed substrings in a hot loop, pointer logic to the rescue!
                                    char* managedSubstring = managedName + startOfNamespaceStrippedManagedTypeName;
                                    if (UnsafeUtility.MemCmp(managedSubstring, nativeName, managedTypeNameLength) == 0)
                                    {
                                        snapshot.TypeDescriptions.UpdateManagedToNativeTypeMapping(snapshot.NativeTypes, managedType, nativeTypeIndex, baseFallbackType: managedType);
                                        nativeUnityObjectTypeIndexToManagedBaseTypeIsNotYetReported = false;
                                        newlyFoundManagedBaseType = managedType;
                                    }
                                }
                            }
                        }
                    }
                }
                // continue with the base type of this managed object type
                managedType = snapshot.TypeDescriptions.BaseOrElementTypeIndex[managedType];
            }

            // if a new base type was found, update the base types for all inheriting types
            if (newlyFoundManagedBaseType is not TypeDescriptionEntriesCache.ITypeInvalid)
            {
                managedType = originalManagedTypeToUpdate;
                while (managedType != newlyFoundManagedBaseType)
                {
                    if (!(nativeTypeIndex == snapshot.NativeTypes.EditorScriptableObjectIdx && managedType == snapshot.TypeDescriptions.ITypeUnityScriptableObject))
                    {
                        snapshot.TypeDescriptions.UpdateManagedToNativeTypeMapping(snapshot.NativeTypes, managedType, nativeTypeIndex,
                            // update the base type here to the newly found one
                            baseFallbackType: newlyFoundManagedBaseType);
                    }
                    // continue with the base type of this managed object type
                    managedType = snapshot.TypeDescriptions.BaseOrElementTypeIndex[managedType];
                }
            }
        }

        /// <summary>
        /// Most Unity Object managed and native types should have been connected to each other by
        /// <see cref="ConnectNativeToManageObject(IntermediateCrawlData)"/> before calling this.
        ///
        /// This method is there for cases where there are only Leaked Managed Shell objects or no objects of a Unity type in a snapshot.
        /// </summary>
        /// <param name="crawlData"></param>
        static void ConnectRemainingManagedTypesToNativeTypes(IntermediateCrawlData crawlData)
        {
            using var marker = k_ConnectRemainingManagedTypesToNativeTypesProfilerMarker.Auto();
            var snapshot = crawlData.CachedMemorySnapshot;
            var managedTypes = snapshot.TypeDescriptions;
            if (managedTypes.Count == 0)
                return;
            ref readonly var uninstantiatableUnityBaseTypes = ref snapshot.TypeDescriptions.UninstantiatableUnityBaseTypesWithSetNativeType;

            var unityObjectTypeIndexToNativeTypeIndex = managedTypes.UnifiedTypeInfoManaged;
            for (long i = 0; i < unityObjectTypeIndexToNativeTypeIndex.Count; i++)
            {
                ref var type = ref unityObjectTypeIndexToNativeTypeIndex[i];
                // process all Unity Object Types that are not yet connected to their native types.
                if (type.IsUnityObjectType
                    && (type.NativeTypeIndex is NativeTypeEntriesCache.InvalidTypeIndex
                    || uninstantiatableUnityBaseTypes.Contains(type.ManagedBaseTypeIndexForNativeType)))
                {
                    var topLevelManagedType = type.ManagedTypeIndex;
                    // This process of connecting the managed type to a native type is rather costly
                    // Only spend that effort for types that had actual object instances in the snapshot
                    // Other types will not be displayed in any tables and establishing the connection therefore doesn't matter
                    if (type.ManagedTypeIndex is TypeDescriptionEntriesCache.ITypeInvalid
                        || !crawlData.TypesWithObjectsThatMayStillNeedNativeTypeConnection.Contains(type.ManagedTypeIndex))
                        continue;

                    while (type.IsUnityObjectType)
                    {
                        var nativeTypeIndex = type.NativeTypeIndex;
                        if (type.NativeTypeIndex is NativeTypeEntriesCache.InvalidTypeIndex
                            || uninstantiatableUnityBaseTypes.Contains(type.ManagedBaseTypeIndexForNativeType))
                        {
                            var typeName = managedTypes.TypeDescriptionName[type.ManagedTypeIndex];
                            if (typeName.StartsWith("Unity"))
                            {
                                typeName = typeName.Substring(typeName.LastIndexOf('.') + 1);
                                nativeTypeIndex = Array.FindIndex(snapshot.NativeTypes.TypeName, e => e.Equals(typeName));
                            }
                        }
                        if (nativeTypeIndex is not NativeTypeEntriesCache.InvalidTypeIndex)
                        {
                            ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypes(snapshot, nativeTypeIndex, topLevelManagedType);
                            break;
                        }
                        // continue with the base type of this managed object type
                        var managedType = managedTypes.BaseOrElementTypeIndex[type.ManagedTypeIndex];
                        if (managedType is TypeDescriptionEntriesCache.ITypeInvalid)
                            break;
                        type = ref unityObjectTypeIndexToNativeTypeIndex[managedType];
                    }
                }
            }
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        static void CrawlRawObjectData(
            IntermediateCrawlData crawlData, in BytesAndOffset bytesAndOffsetOfFieldDataWithoutHeader,
            int iTypeDescription, in SourceIndex indexOfFrom, long maxObjectIndexFindableViaPointers,
            long fromArrayIndex = -1, bool allowStackExpansion = false)
        {
            var fieldLayouts = GetFullFieldLayoutForCrawling(crawlData, iTypeDescription, false);
            CrawlRawObjectData(
                in crawlData.CachedMemorySnapshot.ManagedHeapSections, in crawlData.CachedMemorySnapshot.VirtualMachineInformation, in crawlData.CachedMemorySnapshot.CrawledData.MangedObjectIndexByAddress,
                in crawlData.NativeObjectOrAllocationFinder, crawlData.ConfigurableCrawlerOptions,
                ref crawlData.CrawlDataStack, in fieldLayouts, in bytesAndOffsetOfFieldDataWithoutHeader, in indexOfFrom, maxObjectIndexFindableViaPointers,
                fromArrayIndex, allowStackExpansion: allowStackExpansion);
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining), BurstCompile(CompileSynchronously = true, DisableDirectCall = false, DisableSafetyChecks = k_DisableBurstDebugChecks, Debug = k_DebugBurstJobs)]
        static void CrawlRawObjectData(
            in ManagedMemorySectionEntriesCache managedHeapBytes, in VirtualMachineInformation vmInfo, in AddressToManagedIndexHashMap managedObjectIndexByAddress,
            in NativeObjectOrAllocationFinder nativeObjectOrAllocationFinder, in MemoryProfilerSettings.FeatureFlags.CrawlerFlags crawlerFlags,
            ref DynamicArray<StackCrawlData> crawlDataStack, in DynamicArrayRef<FieldLayoutInfo> fieldLayouts, in BytesAndOffset bytesAndOffsetOfFieldDataWithoutHeader,
            in SourceIndex indexOfFrom, long maxObjectIndexFindableViaPointers,
            long fromArrayIndex = -1, bool allowStackExpansion = false)
        {
            // Can't use foreach here or Burst won't like this method anymore
            // ReSharper disable once ForCanBeConvertedToForeach
            var bytes = bytesAndOffsetOfFieldDataWithoutHeader;
            for (var i = 0; i < fieldLayouts.Count; i++)
            {
                ref var fieldLayout = ref fieldLayouts[i];
                bytes = bytes.Add((ulong)fieldLayout.OffsetFromPreviousAddress);

                if (bytes.TryReadPointer(out var address) != BytesAndOffset.PtrReadError.Success
                    || address == 0)
                    continue;

                CrawlRawFieldData(address, fieldLayout.FieldReferenceType, maxObjectIndexFindableViaPointers,
                    in managedObjectIndexByAddress, in managedHeapBytes, in vmInfo,
                    ref crawlDataStack, in nativeObjectOrAllocationFinder, in crawlerFlags,
                    fieldLayout.IndexOfTheTypeOwningTheActualFieldIndex, in indexOfFrom,
                    allowStackExpansion,
                    fromArrayIndex,
                    fieldIndexOfTheFieldOnTheReferenceOwner: fieldLayout.FieldIndexOnReferenceOwner,
                    fieldIndexOfTheActualField_WhichMayBeOnANestedValueType: fieldLayout.ActualFieldIndexOnPotentialNestedValueType,
                    offsetFromReferenceOwnerHeaderStartToFieldOnValueType: fieldLayout.OffsetFromReferenceOwnerHeaderStartToFieldOnValueType
                    );
            }
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining), BurstCompile(CompileSynchronously = true, DisableDirectCall = false, DisableSafetyChecks = k_DisableBurstDebugChecks, Debug = k_DebugBurstJobs)]
        static void CrawlRawFieldData(ulong arrayDataPtr, FieldReferenceType referenceTypeArrayFieldType, long maxObjectIndexFindableViaPointers,
            in AddressToManagedIndexHashMap mangedObjectIndexByAddress, in ManagedMemorySectionEntriesCache ManagedHeapSections, in VirtualMachineInformation vmInfo,
            ref DynamicArray<StackCrawlData> resultingCrawlDataStack, in NativeObjectOrAllocationFinder nativeObjectOrAllocationFinder, in MemoryProfilerSettings.FeatureFlags.CrawlerFlags crawlerFlags,
            int typeIndexOfTheTypeOwningTheField, in SourceIndex referenceOwner,
            bool allowStackExpansion,
            long indexInReferenceOwningArray = -1,
            int fieldIndexOfTheFieldOnTheReferenceOwner = -1,
            int fieldIndexOfTheActualField_WhichMayBeOnANestedValueType = -1,
            int offsetFromReferenceOwnerHeaderStartToFieldOnValueType = 0)
        {
            if (mangedObjectIndexByAddress.TryGetValue(arrayDataPtr, out var managedObjectIndex))
            {
                // non-reference type fields are limited to what objects they can find, see MemoryProfilerSettings.FeatureFlags.ManagedCrawlerConsidersPointersToGCHandleHeldManagedObjects
                if (referenceTypeArrayFieldType is FieldReferenceType.ReferenceField
                    || referenceTypeArrayFieldType is not FieldReferenceType.PotentialPointerToNativeAllocation && managedObjectIndex < maxObjectIndexFindableViaPointers)
                {
                    if (resultingCrawlDataStack.Count >= resultingCrawlDataStack.Capacity && !allowStackExpansion)
                        throw new InvalidOperationException("CrawlDataStack is full, can't insert new data");

                    resultingCrawlDataStack.Push(new StackCrawlData(arrayDataPtr, managedObjectIndex: managedObjectIndex, typeIndexOfTheTypeOwningTheField: typeIndexOfTheTypeOwningTheField, referenceOwner: referenceOwner,
                        indexInReferenceOwningArray: indexInReferenceOwningArray,
                        fieldIndexOfTheFieldOnTheReferenceOwner: fieldIndexOfTheFieldOnTheReferenceOwner,
                        fieldIndexOfTheActualField_WhichMayBeOnANestedValueType: fieldIndexOfTheActualField_WhichMayBeOnANestedValueType,
                        offsetFromReferenceOwnerHeaderStartToFieldOnValueType: offsetFromReferenceOwnerHeaderStartToFieldOnValueType
                        ), memClearForExcessExpansion: k_MemClearCrawlDataStack);
                }
            }
            // Only reference type fields can refer to objects in the managed heap
            else if (referenceTypeArrayFieldType is FieldReferenceType.ReferenceField
                // only add it to the crawler stack if the address fits a managed heap section
                && ManagedHeapSections.Find(arrayDataPtr, vmInfo, out BytesAndOffset bytes))
            {
                if (resultingCrawlDataStack.Count >= resultingCrawlDataStack.Capacity && !allowStackExpansion)
                    throw new InvalidOperationException("CrawlDataStack is full, can't insert new data");

                resultingCrawlDataStack.Push(new StackCrawlData(arrayDataPtr, bytesAndOffsets: bytes, typeIndexOfTheTypeOwningTheField: typeIndexOfTheTypeOwningTheField, referenceOwner: referenceOwner,
                    indexInReferenceOwningArray: indexInReferenceOwningArray,
                        fieldIndexOfTheFieldOnTheReferenceOwner: fieldIndexOfTheFieldOnTheReferenceOwner,
                        fieldIndexOfTheActualField_WhichMayBeOnANestedValueType: fieldIndexOfTheActualField_WhichMayBeOnANestedValueType,
                        offsetFromReferenceOwnerHeaderStartToFieldOnValueType: offsetFromReferenceOwnerHeaderStartToFieldOnValueType
                    ), memClearForExcessExpansion: k_MemClearCrawlDataStack);
            }
            // ReferenceFields are ignored for native reference tracing
            else if ((referenceTypeArrayFieldType is not FieldReferenceType.ReferenceField)
                // only add it to the crawler stack if the address fits for a native source
                && nativeObjectOrAllocationFinder.TryFindSourceIndexForAddress(arrayDataPtr, out var nativeSource))
            {
                if (resultingCrawlDataStack.Count >= resultingCrawlDataStack.Capacity && !allowStackExpansion)
                    throw new InvalidOperationException("CrawlDataStack is full, can't insert new data");

                resultingCrawlDataStack.Push(new StackCrawlData(arrayDataPtr, sourceIndexOfNativeItem: nativeSource, typeIndexOfTheTypeOwningTheField: typeIndexOfTheTypeOwningTheField, referenceOwner: referenceOwner,
                    indexInReferenceOwningArray: indexInReferenceOwningArray,
                    fieldIndexOfTheFieldOnTheReferenceOwner: fieldIndexOfTheFieldOnTheReferenceOwner,
                    fieldIndexOfTheActualField_WhichMayBeOnANestedValueType: fieldIndexOfTheActualField_WhichMayBeOnANestedValueType,
                    offsetFromReferenceOwnerHeaderStartToFieldOnValueType: offsetFromReferenceOwnerHeaderStartToFieldOnValueType
                    ), memClearForExcessExpansion: k_MemClearCrawlDataStack);
            }
        }

        /// <summary>
        /// With <paramref name="useStaticFields"/> false the layout offsets are header-exclusive, because CrawlObject adds ObjectHeaderSize itself and
        /// value-type array elements and static slots have no header. It is cached per type, so offsets must not depend on the caller.
        /// </summary>
        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        static DynamicArrayRef<FieldLayoutInfo> GetFullFieldLayoutForCrawling(IntermediateCrawlData crawlData, int typeDescriptionIndex, bool useStaticFields)
        {
            ref var fieldLayouts = ref (useStaticFields ? ref crawlData.StaticFieldLayoutInfo : ref crawlData.FieldLayouts);
            // value type fields will likely have subfields so we need to account for some likely extra fields.
            // If we guess wrong, we'll just grow the array though.
            var fieldLayoutInfos = fieldLayouts[typeDescriptionIndex];
            if (fieldLayoutInfos.IsCreated)
                return fieldLayoutInfos;

            return BuildFullFieldLayoutForCrawling(crawlData, ref fieldLayouts, typeDescriptionIndex, useStaticFields);
        }

        static DynamicArrayRef<FieldLayoutInfo> BuildFullFieldLayoutForCrawling(
            IntermediateCrawlData crawlData, ref FieldLayouts fieldLayouts,
            int iTypeDescription, bool useStaticFields)
        {

            var fieldIndexCountAssumption = useStaticFields
                ? crawlData.CachedMemorySnapshot.TypeDescriptions.FieldIndicesOwnedStatic[iTypeDescription].Count * 2
                : crawlData.CachedMemorySnapshot.TypeDescriptions.FieldIndicesInstance[iTypeDescription].Count * 4;
            var fieldLayoutInfos = new DynamicArray<FieldLayoutInfo>(0, Math.Max(1, fieldIndexCountAssumption), Allocator.Temp);

            BuildFieldLayoutForCrawling(crawlData, ref fieldLayoutInfos, iTypeDescription, useStaticFields);
            if (fieldLayoutInfos.Count > 0)
            {
                if (fieldLayoutInfos.Count > 1)
                    // for Mono & IL2CPP the sort is superfluous, but lets not build the assumption into the algorithm that the fields are always reported as sorted by their offset.
                    DynamicArrayAlgorithms.IntrospectiveSort(fieldLayoutInfos, 0, fieldLayoutInfos.Count);

                long remainingFieldCount = 0;
                for (var i = fieldLayoutInfos.Count - 1; i >= 1; --i, ++remainingFieldCount)
                {
                    fieldLayoutInfos[i] = new FieldLayoutInfo(fieldLayoutInfos[i], fieldLayoutInfos[i - 1].OffsetFromPreviousAddress, remainingFieldCount);
                }
                fieldLayoutInfos[0] = new FieldLayoutInfo(fieldLayoutInfos[0], 0, remainingFieldCount);

#if DEBUG_VALIDATION
                // Catches boxed/unboxed coordinate mixing early: a layout built with useStaticFields
                // false must fit within the type's own (unboxed) size, since every consumer walks it
                // against bytes that already start at the first field. Array type rows have no
                // per-type size (per-instance size is composed from header + length * elementSize
                // instead, see ArrayTools) and are walked via a different consumer entirely, so they
                // have no meaningful bound to check here.
                var snapshotForValidation = crawlData.CachedMemorySnapshot;
                if (!useStaticFields && !snapshotForValidation.TypeDescriptions.HasFlag(iTypeDescription, TypeFlags.kArray))
                {
                    var isValueType = snapshotForValidation.TypeDescriptions.HasFlag(iTypeDescription, TypeFlags.kValueType);
                    var unboxedSize = snapshotForValidation.TypeDescriptions.Size[iTypeDescription]
                        - (isValueType ? 0 : (int)snapshotForValidation.VirtualMachineInformation.ObjectHeaderSize);
                    if (unboxedSize > 0)
                    {
                        long absoluteOffset = 0;
                        for (long i = 0; i < fieldLayoutInfos.Count; i++)
                        {
                            absoluteOffset += fieldLayoutInfos[i].OffsetFromPreviousAddress;
                            // Each entry is a pointer-sized read, so the whole pointer must fit, not just its start.
                            if (absoluteOffset + snapshotForValidation.VirtualMachineInformation.PointerSize > unboxedSize)
                                Debug.LogError($"Field layout offset {absoluteOffset} for type {snapshotForValidation.TypeDescriptions.TypeDescriptionName[iTypeDescription]} exceeds its unboxed size {unboxedSize} - boxed/unboxed coordinate mixing suspected.");
                        }
                    }
                }
#endif
            }
            fieldLayouts.AddType(iTypeDescription, fieldLayoutInfos, memClearAllAboveCount: k_MemClearFieldLayoutData);
            fieldLayoutInfos.Dispose();
            return fieldLayouts[iTypeDescription];
        }

        static void BuildFieldLayoutForCrawling(
            IntermediateCrawlData crawlData, ref DynamicArray<FieldLayoutInfo> fieldLayoutInfos,
            int iTypeDescription, bool useStaticFields)
        {
            var snapshot = crawlData.CachedMemorySnapshot;

            var fields = useStaticFields ? snapshot.TypeDescriptions.FieldIndicesOwnedStatic[iTypeDescription] : snapshot.TypeDescriptions.FieldIndicesInstance[iTypeDescription];
            var isNonStaticReferenceType = !useStaticFields && !snapshot.TypeDescriptions.HasFlag(iTypeDescription, TypeFlags.kValueType);
            // FieldDescriptions.Offset is header-inclusive (boxed) for any non-static field - subtract
            // the header for both reference AND value types to get back to the unboxed coordinates this
            // layout is walked in (see GetFullFieldLayoutForCrawling above). objectHeaderSizeSkippedByCrawler
            // is a separate, UI-attribution-only value and must stay reference-type-only.
            var fieldOffsetsAreHeaderInclusive = !useStaticFields;
            var objectHeaderSizeSkippedByCrawler = isNonStaticReferenceType ? (int)snapshot.VirtualMachineInformation.ObjectHeaderSize : 0;

            // v20+ statics are addressed by field ordinal via GetStaticFieldSlotGroups, since FieldDescriptions.Offset is 0 for GC statics.
            // Types without slot data (e.g. pre-v20) have no groups and fall back to FieldDescriptions.Offset below.
            var staticSlotGroups = useStaticFields && snapshot.HasStaticFieldSlotsAndFlags
                ? snapshot.TypeDescriptions.GetStaticFieldSlotGroups(snapshot, iTypeDescription)
                : default;
            var usesStaticFieldSlots = staticSlotGroups.Count > 0 && staticSlotGroups.Count == fields.Count;

            int staticFieldOrdinal = 0;
            foreach (var iField in fields)
            {
                int fieldOffset;
                if (usesStaticFieldSlots)
                {
                    var group = staticSlotGroups[staticFieldOrdinal];
                    ++staticFieldOrdinal;
                    if (group.Count == 0)
                        // Unresolved static (e.g. RVA/context static, or an unsupported thread-static) - no bytes to point at.
                        continue;
                    fieldOffset = (int)group.SlotDataOffset;
                }
                else
                {
                    fieldOffset = snapshot.FieldDescriptions.Offset[iField];
                    if (fieldOffsetsAreHeaderInclusive)
                        fieldOffset -= (int)snapshot.VirtualMachineInformation.ObjectHeaderSize;
                }

                BuildFieldLayoutEntryForField(crawlData, ref fieldLayoutInfos, iTypeDescription, useStaticFields, objectHeaderSizeSkippedByCrawler, iField, fieldOffset);
            }
        }

        // Appends the FieldLayoutInfo entry/entries (possibly more than one, if iField is itself a
        // nested value type) for one field of iTypeDescription, given that field's own offset
        // (relative to iTypeDescription's own start - the same coordinate space fieldOffset already
        // has coming into BuildFieldLayoutForCrawling above, whether it came from FieldDescriptions.Offset
        // or a static-field-slot's SlotDataOffset).
        static void BuildFieldLayoutEntryForField(
            IntermediateCrawlData crawlData, ref DynamicArray<FieldLayoutInfo> fieldLayoutInfos,
            int iTypeDescription, bool useStaticFields, int objectHeaderSizeSkippedByCrawler,
            int iField, int fieldOffset)
        {
            var snapshot = crawlData.CachedMemorySnapshot;
            var iField_TypeDescription_TypeIndex = snapshot.FieldDescriptions.TypeIndex[iField];

            // CoreCLR PTR/FNPTR fields: TypeIndex is the (possibly invalid, e.g. void*/delegate*<...>)
            // pointee type, not the field's own type, and must not be used to query TypeDescriptions
            // for the field's own type flags/size/name the way Mono/IL2CPP's synthesised pointer
            // classes allow. Handle them directly and skip the value-type/reference-type path below.
            // ElementType is only allocated on format v19+ files, so check HasBackendManagedGraph
            // (which implies v19+) before indexing it.
            if (snapshot.HasBackendManagedGraph && FieldDescriptionEntriesCache.IsPointerLikeElementType(snapshot.FieldDescriptions.ElementType[iField], snapshot.HasBackendManagedGraph))
            {
                fieldLayoutInfos.Push(new FieldLayoutInfo
                    (
                        remainingFieldCountForThisType: 0,
                        indexOfTheTypeOwningTheActualFieldIndex: iTypeDescription,
                        indexOfTheTypeOfTheReferenceOwner: iTypeDescription,
                        offsetFromPreviousAddress: fieldOffset,
                        fieldIndexOnReferenceOwner: iField,
                        actualFieldIndexOnPotentialNestedValueType: iField,
                        additionalFieldOffsetFromFieldOnReferenceOwnerToFieldOnValueType: objectHeaderSizeSkippedByCrawler,
                        fieldReferenceType: FieldReferenceType.Ptr
                    ), memClearForExcessExpansion: k_MemClearFieldLayoutData);
                return;
            }

            // CoreCLR array-shaped field: TypeIndex points at the array's ELEMENT type (real or
            // synthetic), not an array type row — set purely so the element's own name/size/flags
            // are resolvable, not so the FIELD is treated as an inline nested value type. The field
            // itself is always a reference (an array), never inline, even when the element happens
            // to be a value type (e.g. int[]). This only prevents the misclassification; the field
            // still displays as the element's own type name rather than "Foo[]".
            // ElementType is only allocated on format v19+ files (see the HasBackendManagedGraph
            // check above) - short-circuit before indexing it on an older file.
            var isArrayShapedField = snapshot.HasBackendManagedGraph
                && FieldDescriptionEntriesCache.IsArrayShapedElementType(snapshot.FieldDescriptions.ElementType[iField], snapshot.HasBackendManagedGraph);

            var isValueTypeField = !isArrayShapedField && snapshot.TypeDescriptions.HasFlag(iField_TypeDescription_TypeIndex, TypeFlags.kValueType);
            // Recurse into value types that can hold a pointer (size >= pointer size, or unknown size). IntPtr is pre-registered as FieldReferenceType.IntPtr
            // in IntermediateCrawlData's constructor, so recursing into it is safe. See AGENTS.md in this folder.
            var valueTypeSize = isValueTypeField ? snapshot.TypeDescriptions.Size[iField_TypeDescription_TypeIndex] : 0;
            var recurseIntoValueType = isValueTypeField && (valueTypeSize <= 0 || valueTypeSize >= snapshot.VirtualMachineInformation.PointerSize);
            if (recurseIntoValueType)
            {
                // Lookup existing field layout info for this nested value type, or get it generated
                var nestedValueTypeFields = GetFullFieldLayoutForCrawling(crawlData, iField_TypeDescription_TypeIndex, useStaticFields: false);

                // take the nested value type field info and adjust it to the current field and ReferenceOwner
                int subFieldOffset = fieldOffset;
                for (long i = 0; i < nestedValueTypeFields.Count; i++)
                {
                    subFieldOffset += nestedValueTypeFields[i].OffsetFromPreviousAddress;
                    fieldLayoutInfos.Push(new FieldLayoutInfo
                        (
                            remainingFieldCountForThisType: 0,
                            indexOfTheTypeOwningTheActualFieldIndex: nestedValueTypeFields[i].IndexOfTheTypeOwningTheActualFieldIndex, // iTypeDescription,
                            indexOfTheTypeOfTheReferenceOwner: iTypeDescription,
                            offsetFromPreviousAddress: subFieldOffset,
                            fieldIndexOnReferenceOwner: iField,
                            actualFieldIndexOnPotentialNestedValueType: nestedValueTypeFields[i].ActualFieldIndexOnPotentialNestedValueType, // iField,
                            additionalFieldOffsetFromFieldOnReferenceOwnerToFieldOnValueType: subFieldOffset + objectHeaderSizeSkippedByCrawler,
                            fieldReferenceType: nestedValueTypeFields[i].FieldReferenceType
                        ), memClearForExcessExpansion: k_MemClearFieldLayoutData);
                }
                if (useStaticFields ? snapshot.TypeDescriptions.FieldIndicesStatic[iField_TypeDescription_TypeIndex].Count > 0
                    : snapshot.TypeDescriptions.FieldIndicesInstance[iField_TypeDescription_TypeIndex].Count > 0)
                    return;
            }

            if (!isValueTypeField || snapshot.TypeDescriptions.Size[iField_TypeDescription_TypeIndex] == snapshot.VirtualMachineInformation.PointerSize)
            {
                FieldReferenceType fieldReferenceType =
                    isValueTypeField ?
                    //snapshot.TypeDescriptions.ITypeIntPtr == iField_TypeDescription_TypeIndex ? FieldReferenceType.IntPtr : // as long as we initalize the IntPtr type to explicitly be FieldReferenceType.IntPtr, we don't need to doubt those as potential pointers here and check for an asterix
                    FieldReferenceType.Ptr
                    : FieldReferenceType.ReferenceField;

                if (fieldReferenceType == FieldReferenceType.Ptr && !snapshot.TypeDescriptions.TypeDescriptionName[iField_TypeDescription_TypeIndex].Contains('*'))
                {
                    // Since we have some pointer sized fields that refer to native allocations in e.g. DOTS/Entities code,
                    // we can considers any field of pointer size as a potential pointer to a native allocation to ensure we're pointing out these connections.
                    // To avoid adding too much strain on the snapshot opening process though, only consider this for fields on structs coming from the "Unity.Entities" namespace
                    const string k_EntitiesNamespace = "Unity.Entities";
                    if (crawlData.ConfigurableCrawlerOptions.ManagedCrawlerConsidersPotentialPointersToNativeAllocationsOutsideOfEntitiesNamespace
                        || (crawlData.ConfigurableCrawlerOptions.ManagedCrawlerConsidersPotentialPointersToNativeAllocations
                        && snapshot.TypeDescriptions.TypeDescriptionName[iTypeDescription].StartsWith(k_EntitiesNamespace)))
                    {
                        fieldReferenceType = FieldReferenceType.PotentialPointerToNativeAllocation;
                    }
                    else
                        return;
                }

                fieldLayoutInfos.Push(new FieldLayoutInfo
                    (
                        remainingFieldCountForThisType: 0,
                        indexOfTheTypeOwningTheActualFieldIndex: iTypeDescription,
                        indexOfTheTypeOfTheReferenceOwner: iTypeDescription,
                        offsetFromPreviousAddress: fieldOffset,
                        fieldIndexOnReferenceOwner: iField,
                        actualFieldIndexOnPotentialNestedValueType: iField,
                        additionalFieldOffsetFromFieldOnReferenceOwnerToFieldOnValueType: objectHeaderSizeSkippedByCrawler,
                        fieldReferenceType: fieldReferenceType
                    ), memClearForExcessExpansion: k_MemClearFieldLayoutData);
            }
        }
#if PROFILING_CORE_AVAILABLE
        public const string CrawlPointerStructArraysJobScheduleMarkerName = "CrawlPointer - schedule value type array crawler jobs";
        static readonly ProfilerMarker<long> k_CrawlPointerStructArraysJobScheduleMarker = new ProfilerMarker<long>(CrawlPointerStructArraysJobScheduleMarkerName, "array length");
        public const string CrawlPointerObjectArraysJobScheduleMarkerName = "CrawlPointer - schedule reference type array crawler jobs";
        static readonly ProfilerMarker<long> k_CrawlPointerObjectArraysJobScheduleMarker = new ProfilerMarker<long>(CrawlPointerObjectArraysJobScheduleMarkerName, "array length");
#endif
        public const string CrawlPointerArrayChunkMarkerName = "CrawlPointer - schedule chunk off array crawler jobs";
        static readonly ProfilerMarker k_CrawlPointerArrayChunkMarker = new ProfilerMarker(CrawlPointerArrayChunkMarkerName);

        public const string CrawlPointersInStructArraysMarkerName = "CrawlPointer - handle value type array crawler";
        static readonly ProfilerMarker k_CrawlPointersInStructArraysMarker = new ProfilerMarker(CrawlPointersInStructArraysMarkerName);
        public const string CrawlPointersInObjectArraysMarkerName = "CrawlPointer - handle reference type array crawler";
        static readonly ProfilerMarker k_CrawlPointersInObjectArraysMarker = new ProfilerMarker(CrawlPointersInObjectArraysMarkerName);

#if DEBUG_VALIDATION
        static readonly ProfilerMarker k_CrawlPointerDebugValidationMarker = new ProfilerMarker("CrawlPointer - DEBUG_VALIDATION");
#endif

        static bool CrawlPointer(IntermediateCrawlData crawlData)
        {
            Debug.Assert(crawlData.CrawlDataStack.Count > 0);

            var snapshot = crawlData.CachedMemorySnapshot;
            var data = crawlData.CrawlDataStack.Pop();
            ref var objectList = ref snapshot.CrawledData.ManagedObjects;
            var objectsByAddress = snapshot.CrawledData.MangedObjectIndexByAddress;
            var managedHeapSections = snapshot.ManagedHeapSections;
            var virtualMachineInformation = snapshot.VirtualMachineInformation;
            ManagedObjectInfo obj;
            bool wasAlreadyCrawled = false;
            BytesAndOffset byteOffset = data.Bytes;
            // Computed unconditionally (not just on the "already known object" branch below) so it
            // matches the same handle-held-by-native-object check ManagedConnection.MakeConnection
            // makes for SourceId.GCHandleIndex — otherwise a GCHandle-rooted object first reached via
            // the "not yet known" branch skips this check entirely and MakeConnection asserts.
            bool referenceOwnerIsNativeObject = data.ReferenceOwner.Id == SourceIndex.SourceId.GCHandleIndex
                && snapshot.NativeObjects.GCHandleIndexToIndex.ContainsKey(data.ReferenceOwner.Index);

            // if FieldFromITypeDescription differs from the type of the holding Type or Object, that's because the field is held by a value type
            var valueTypeFieldOwningITypeDescription = data.ReferenceOwner.Id switch
            {
                SourceIndex.SourceId.ManagedType => data.ReferenceOwner.Index != data.TypeIndexOfTheTypeOwningTheField ? data.TypeIndexOfTheTypeOwningTheField : -1,
                SourceIndex.SourceId.ManagedObject => objectList[data.ReferenceOwner.Index].ITypeDescription != data.TypeIndexOfTheTypeOwningTheField ? data.TypeIndexOfTheTypeOwningTheField : -1,
                _ => -1
            };
            var idx = data.ManagedObjectIndex;
            if (idx < 0 && !objectsByAddress.GetOrInitializeValue(data.Address, out idx, -1))
            {
                // Static values can be stale, see BackendObjects.
                if (data.ReferenceOwner.Id == SourceIndex.SourceId.ManagedType && crawlData.BackendObjects.IsCreated
                    && !crawlData.BackendObjects.Contains(data.Address))
                    return false;

                if (!byteOffset.IsValid)
                {
#if DEBUG_VALIDATION
                    using var a_ = k_CrawlPointerDebugValidationMarker.Auto();
                    // This whole if block is here to validate that PROF-2420 is fixed.
                    if (snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(data.Address, out var manageObjectIndex))
                    {
                        for (long i = 0; i < snapshot.GcHandles.Target.Count; i++)
                        {
                            if (snapshot.GcHandles.Target[i] == data.Address)
                            {
                                if (snapshot.CrawledData.InvalidManagedObjectsReportedViaGCHandles.ContainsKey(manageObjectIndex))
                                    break;
                                snapshot.CrawledData.InvalidManagedObjectsReportedViaGCHandles.Add(manageObjectIndex, new ManagedObjectInfo() { PtrObject = data.Address, ManagedObjectIndex = manageObjectIndex, NativeObjectIndex = -1 });
                                break;
                            }
                        }
                    }
#endif
                    // Handle Managed -> Native references
                    if (data.SourceIndexOfNativeItem.Valid)
                    {

#if DEBUG_VALIDATION
                        if (data.SourceIndexOfNativeItem.Id == SourceIndex.SourceId.NativeObject &&
                            data.ReferenceOwner.Id == SourceIndex.SourceId.ManagedObject &&
                            data.FieldIndexOfTheFieldOnTheReferenceOwner == snapshot.TypeDescriptions.IFieldUnityObjectNativeLink)
                        {
                            // Check that this native link field reference from Managed Shell to Native Object has already been linked up to the managed object up with the Native one
                            Checks.CheckEquals(objectList[data.ReferenceOwner.Index].NativeObjectIndex, data.SourceIndexOfNativeItem.Index);
                            Checks.CheckEquals(snapshot.NativeObjects.ManagedObjectIndex[data.SourceIndexOfNativeItem.Index], data.ReferenceOwner.Index);
                        }
#endif
                        crawlData.ManagedConnections.Add(
                                ManagedConnection.MakeConnection(snapshot, data.ReferenceOwner, data.SourceIndexOfNativeItem,
                                data.FieldIndexOfTheFieldOnTheReferenceOwner,
                                valueTypeFieldOwningITypeDescription, data.FieldIndexOfTheActualField_WhichMayBeOnANestedValueType,
                                data.OffsetFromReferenceOwnerHeaderStartToFieldOnValueType, data.IndexInReferenceOwningArray));

                        return true;
                    }
                    return false;
                }
                var typeOfReferencingFieldOrArray = GetReferenceTargetTypeInfo(snapshot, data, valueTypeFieldOwningITypeDescription, idx, out var _);
                obj = ParseObjectHeader(snapshot, data.Address, out wasAlreadyCrawled, false, byteOffset, typeOfReferencingFieldOrArray);
            }
            else
            {
                obj = objectList[idx];

                // this happens on objects from gcHandles, they are added before any other crawled object but have their ptr set to 0.
                if (obj.PtrObject == 0)
                {
                    var heldAsType = GetReferenceTargetTypeInfo(snapshot, data, valueTypeFieldOwningITypeDescription, idx, out var nativeObjectIndex);

                    if (!byteOffset.IsValid)
                        // Should only be the case for GC Handle held objects that are not getting their heap bytes searched for in the initial setup phase
                        managedHeapSections.Find(data.Address, virtualMachineInformation, out byteOffset);
                    idx = obj.ManagedObjectIndex;
                    if (TryParseObjectHeader(snapshot, data.Address, out obj, byteOffset, typeOfReferencingField: heldAsType, ignoreErrorsBecauseObjectIsSpeculative: false))
                    {
                        obj.ManagedObjectIndex = idx;
                        if (nativeObjectIndex >= 0)
                            obj.NativeObjectIndex = (int)nativeObjectIndex;
                    }
                    else
                    {
                        // TODO: This is a faulty object that was reported via GCHandles but are not/no longer valid by the time the heap was dumped.
                        // remove the "&& false" bits when analysing Managed Heap data reporting fixes
                        // ATM this is not expected to work, as there is a possible delay between reporting GCHandles and dumping the heap chunks
#if DEBUG_VALIDATION
                        Debug.Assert(objectList[idx].NativeObjectIndex == -1, "A reported GCHandle pointed at a ManagedObject that couldn't be parsed and NativeObjectIndex was incorrectly initialized");
#if false
                        Debug.LogError("A reported GCHandle pointed at a ManagedObject that couldn't be parsed");
                        // This is here to make the investigations for PROF-2420 easier.
#endif
#endif
                        objectList[idx].NativeObjectIndex = -1;
                        return false;
                    }

                    wasAlreadyCrawled = false;
                }
                else
                {
                    wasAlreadyCrawled = true;
                }
            }
            if (!obj.IsValid())
                return false;

            // Native Object connections are handled separately and added to the refcount there.
            if (!referenceOwnerIsNativeObject)
                ++obj.RefCount;

            objectList[obj.ManagedObjectIndex] = obj;
            objectsByAddress[obj.PtrObject] = obj.ManagedObjectIndex;

            // The ReferenceOwner should always be valid and either a Managed Object, Managed Type, GCHandle, ManagedRoot, or ManagedHeapSection.
            Checks.IsTrue(data.ReferenceOwner.Valid);
            // Native object connections are added separately, so they are ignored here. Otherwise we'd add their references twice, and here via the GCHandle instead of the native object.
            if (data.ReferenceOwner.Valid && !referenceOwnerIsNativeObject)
            {
                crawlData.ManagedConnections.Add(ManagedConnection.MakeConnection(snapshot, data.ReferenceOwner, new SourceIndex(SourceIndex.SourceId.ManagedObject, obj.ManagedObjectIndex), data.FieldIndexOfTheFieldOnTheReferenceOwner,
                    valueTypeFieldOwningITypeDescription, data.FieldIndexOfTheActualField_WhichMayBeOnANestedValueType, data.OffsetFromReferenceOwnerHeaderStartToFieldOnValueType, data.IndexInReferenceOwningArray));
            }

            if (!wasAlreadyCrawled)
                return CrawlObject(crawlData, obj, byteOffset);

            return true;
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        static int GetReferenceTargetTypeInfo(CachedSnapshot snapshot, StackCrawlData data, int valueTypeFieldOwningITypeDescription, long managedObjectIndex, out long nativeObjectIndex)
        {
            var gcHandleBasedTypeInfo = -1;
            if (managedObjectIndex >= 0 && managedObjectIndex < snapshot.GcHandles.UniqueCount)
            {
                gcHandleBasedTypeInfo = GetGCHandleHeldTypeInfo(snapshot, managedObjectIndex, out nativeObjectIndex);
            }
            else
            {
                nativeObjectIndex = -1;
            }

            switch (data.ReferenceOwner.Id)
            {
                case SourceIndex.SourceId.ManagedObject:
                case SourceIndex.SourceId.ManagedType:
                    var fieldBasedTypeInfo = GeTypeOfReferencingFieldOrArrayElement(snapshot, data, valueTypeFieldOwningITypeDescription);
                    // if the field based type info isn't all that specific, prefer the gchandle based type info, if present.
                    if ((fieldBasedTypeInfo < 0 || fieldBasedTypeInfo == snapshot.TypeDescriptions.ITypeObject)
                        && gcHandleBasedTypeInfo >= 0)
                        return gcHandleBasedTypeInfo;
                    return fieldBasedTypeInfo;
                case SourceIndex.SourceId.GCHandleIndex:
                    return gcHandleBasedTypeInfo;
                case SourceIndex.SourceId.ManagedRoot:
                case SourceIndex.SourceId.ManagedHeapSection:
                    // Same as a GCHandle: neither a root nor a heap section has a field of its own to infer a
                    // more specific type from — the byte walk will still resolve the object's real type once it
                    // parses its header via TryParseObjectHeader.
                    return snapshot.TypeDescriptions.ITypeObject;
                default:
                    throw new NotImplementedException();
            }
        }

        static int GeTypeOfReferencingFieldOrArrayElement(CachedSnapshot snapshot, StackCrawlData data, int valueTypeFieldOwningITypeDescription)
        {
            var holdingFieldIndex = valueTypeFieldOwningITypeDescription >= 0 ? data.FieldIndexOfTheActualField_WhichMayBeOnANestedValueType : data.FieldIndexOfTheFieldOnTheReferenceOwner;
            // HoldingFieldIndex will be negative for references coming from a reference array, or a GCHandle.
            if (holdingFieldIndex < 0)
            {
                if (data.ReferenceOwner.Id == SourceIndex.SourceId.GCHandleIndex)
                {
                    // If the reference is coming from GCHandle, we can't use the reference owner information to constrain the type to anything more specific
                    // than Object, unless there is a native object associated with the GCHandle. But GetGCHandleHeldTypeInfo should be used for getting that info.
                    return snapshot.TypeDescriptions.ITypeObject;
                }
                else
                {
                    // For reference arrays we need to grab the type of the array and then the base type of that array to use as the field type.
                    var typeOfArray = snapshot.CrawledData.ManagedObjects[data.ReferenceOwner.Index].ITypeDescription;
                    if (data.IndexInReferenceOwningArray >= 0 && snapshot.TypeDescriptions.Flags[typeOfArray].HasFlag(TypeFlags.kArray))
                    {
                        return snapshot.TypeDescriptions.BaseOrElementTypeIndex[typeOfArray];
                    }
                    return -1;
                }
            }
            else
                return snapshot.FieldDescriptions.TypeIndex[holdingFieldIndex];
        }

        /// <summary>
        /// Retrieves the managed base type information for a GC Handle held object based on the Native Object that holds it, and also provides the native object's index.
        /// </summary>
        /// <param name="snapshot"></param>
        /// <param name="managedObjectIndex"></param>
        /// <param name="nativeObjectIndex">If >= 0 the managed object is held by a Native Object via a GCHandle.</param>
        /// <returns></returns>
        static int GetGCHandleHeldTypeInfo(CachedSnapshot snapshot, long managedObjectIndex, out long nativeObjectIndex)
        {
            Debug.Assert(snapshot.GcHandles.UniqueCount > managedObjectIndex, "The index of the managed object is invalid for a GCHandle held object");
            // regular GC Handles can hold anything of type object and are otherwise not specifying what type of object they are holding
            var heldAsType = snapshot.TypeDescriptions.ITypeObject;
            nativeObjectIndex = -1;
            // GCHandleIndexToIndex is keyed by raw GCHandle index, which only equals managedObjectIndex when
            // ObjectIndicesMatchGCHandleOrder holds (the byte-walk path this function is exclusively called
            // from today). Guard explicitly rather than querying with the wrong key on a future caller that
            // reaches here with that flag false.
            if (snapshot.CrawledData.ObjectIndicesMatchGCHandleOrder
                && snapshot.NativeObjects.GCHandleIndexToIndex.GetOrInitializeValue(managedObjectIndex, out nativeObjectIndex, -1))
            {
                var typeInfo = snapshot.TypeDescriptions.UnifiedTypeInfoNative[snapshot.NativeObjects.NativeTypeArrayIndex[nativeObjectIndex]];
                if (typeInfo.ManagedTypeIndex != TypeDescriptionEntriesCache.ITypeInvalid)
                    heldAsType = typeInfo.ManagedTypeIndex;
                else // at the very least, we can make the type more specific than just ´object´ and upgrade it to UnityEngine.Object
                    heldAsType = snapshot.TypeDescriptions.ITypeUnityObject;
            }
            return heldAsType;
        }

        static bool CrawlObject(IntermediateCrawlData crawlData, ManagedObjectInfo obj, in BytesAndOffset byteOffset)
        {
            var snapshot = crawlData.CachedMemorySnapshot;
            var typeDescriptions = snapshot.TypeDescriptions;
            var virtualMachineInformation = snapshot.VirtualMachineInformation;
            var objectIndex = new SourceIndex(SourceIndex.SourceId.ManagedObject, obj.ManagedObjectIndex);
            var maxObjectIndexFindableViaPointers = crawlData.MaxObjectIndexFindableViaPointers;

            if (!typeDescriptions.HasFlag(obj.ITypeDescription, TypeFlags.kArray))
            {
                CrawlRawObjectData(crawlData, byteOffset.Add(snapshot.VirtualMachineInformation.ObjectHeaderSize), obj.ITypeDescription, objectIndex, maxObjectIndexFindableViaPointers, allowStackExpansion: true);

                return true;
            }

            var arrayLength = ManagedHeapArrayDataTools.ReadArrayLength(snapshot, byteOffset, obj.ITypeDescription, out var arrayRank);

            if (arrayLength <= 0)
            {
                //if(arrayRank > 1)
                // TODO: Add to list of faulty Multidimensional arrays with one or more dimensions set to 0 length, collapsing the whole thing down and wasting space.
                return false;
            }

            var iElementTypeDescription = typeDescriptions.BaseOrElementTypeIndex[obj.ITypeDescription];
            if (iElementTypeDescription == -1)
                return false; //do not crawl uninitialized object types, as we currently don't have proper handling for these

            var arrayDataOffset = ManagedHeapArrayDataTools.IsCoreClrArrayLayout(virtualMachineInformation)
                ? ManagedHeapArrayDataTools.GetCoreClrArrayDataOffset(virtualMachineInformation, arrayRank)
                : virtualMachineInformation.ArrayHeaderSize;
            var arrayData = byteOffset.Add(arrayDataOffset);

            var isValueTypeArray = typeDescriptions.HasFlag(iElementTypeDescription, TypeFlags.kValueType);
            var referenceTypeArrayFieldType = FieldReferenceType.ReferenceField;

            var fieldLayouts = GetFullFieldLayoutForCrawling(crawlData, iElementTypeDescription, false);

            if (isValueTypeArray && fieldLayouts.Count == 0)
            {
                if (snapshot.TypeDescriptions.CouldThisFieldBeReadAsAnAddress(iElementTypeDescription, snapshot))
                {
                    // it's technically a bunch of pointers
                    isValueTypeArray = false;
                    referenceTypeArrayFieldType = snapshot.TypeDescriptions.TypeDescriptionName[iElementTypeDescription].EndsWith('*') ? FieldReferenceType.Ptr : FieldReferenceType.PotentialPointerToNativeAllocation;
                    // early out if this is only a potential pointer and if those are not currently processed
                    if (referenceTypeArrayFieldType == FieldReferenceType.PotentialPointerToNativeAllocation
                        // only check these when scanning beyond just entities related potential references, as entities related potential pointers will always be in a struct and never a managed array on e.g. ulongs
                        && !crawlData.ConfigurableCrawlerOptions.ManagedCrawlerConsidersPotentialPointersToNativeAllocationsOutsideOfEntitiesNamespace)
                        return false;
                }
                else
                    return false;
            }

            ref var managedObjectIndexByAddress = ref snapshot.CrawledData.MangedObjectIndexByAddress;
            ref var nativeObjectOrAllocationFinder = ref crawlData.NativeObjectOrAllocationFinder;

            var managedHeapSections = snapshot.ManagedHeapSections;
            ref var crawlDataStack = ref crawlData.CrawlDataStack;

            var fieldCountPerArrayElement = isValueTypeArray ? fieldLayouts.Count : 1;

            if (arrayLength <= 0 || arrayLength <= (isValueTypeArray ? (JobifiedArrayCrawlingFieldCountThreshold / fieldCountPerArrayElement) : JobifiedArrayCrawlingFieldCountThreshold))
            {
                using var _ = isValueTypeArray ? k_CrawlPointersInStructArraysMarker.Auto() : k_CrawlPointersInObjectArraysMarker.Auto();

                if (isValueTypeArray)
                {
                    var typeSize = (uint)typeDescriptions.Size[iElementTypeDescription];

                    for (var i = 0; i < arrayLength; i++)
                    {
                        CrawlRawObjectData(snapshot.ManagedHeapSections, virtualMachineInformation, managedObjectIndexByAddress, nativeObjectOrAllocationFinder, crawlData.ConfigurableCrawlerOptions, ref crawlDataStack, fieldLayouts, arrayData, objectIndex, maxObjectIndexFindableViaPointers, fromArrayIndex: i, allowStackExpansion: true);
                        arrayData = arrayData.Add(typeSize);
                    }
                }
                else
                {
                    for (long i = 0; i < arrayLength; i++)
                    {
                        if (arrayData.TryReadPointer(out var arrayDataPtr) != BytesAndOffset.PtrReadError.Success)
                            return false;
                        if (arrayDataPtr != 0)
                        {
                            CrawlRawFieldData(arrayDataPtr, referenceTypeArrayFieldType, maxObjectIndexFindableViaPointers,
                                in managedObjectIndexByAddress, in managedHeapSections, in virtualMachineInformation,
                                ref crawlDataStack, in nativeObjectOrAllocationFinder, crawlData.ConfigurableCrawlerOptions,
                                obj.ITypeDescription, in objectIndex,
                                allowStackExpansion: true,
                                indexInReferenceOwningArray: i);
                        }
                        arrayData = arrayData.NextPointer();
                    }
                }
                return true;
            }
            else
            {
#if PROFILING_CORE_AVAILABLE
                using var _ = isValueTypeArray ? k_CrawlPointerStructArraysJobScheduleMarker.Auto(arrayLength) : k_CrawlPointerObjectArraysJobScheduleMarker.Auto(arrayLength);
#endif
                var totalFieldCount = arrayLength * fieldCountPerArrayElement;
                var remainingChunkCount = CalculateBatchElementCount(totalFieldCount);

                var chunkCountForCurrentBatch = Math.Min(remainingChunkCount, k_MaxConcurrentJobCount);

                var standardChunkArrayElementCount = arrayLength / remainingChunkCount;
                // The last chunk size might differ
                var lastChunkFieldCount = totalFieldCount - standardChunkArrayElementCount * (remainingChunkCount - 1) * fieldCountPerArrayElement;
                var fieldCountPerStack = Math.Max(standardChunkArrayElementCount * fieldCountPerArrayElement, lastChunkFieldCount);
                // adding all stacks we need to this pool for easy reuse. It also means we can dispose them all in one go.
                var stackPool = new DynamicArray<DynamicArray<StackCrawlData>>(chunkCountForCurrentBatch, Allocator.Persistent);
                for (long i = 0; i < stackPool.Count; i++)
                {
                    stackPool[i] = new DynamicArray<StackCrawlData>(0, fieldCountPerStack, Allocator.Persistent, memClear: k_MemClearCrawlDataStack);
                }

                var indexOfNextArrayElementToProcess = 0L;
                while (indexOfNextArrayElementToProcess < arrayLength)
                {
                    using var _1 = k_CrawlPointerArrayChunkMarker.Auto();

                    // for any iteration past the first, make sure the stack pools are cleared out to avoid adding dirty data to the crawler stack
                    if (indexOfNextArrayElementToProcess > 0)
                    {
                        for (long i = 0; i < chunkCountForCurrentBatch; i++)
                        {
                            stackPool[i].Clear(stomp: k_MemClearCrawlDataStack);
                        }
                    }

                    if (isValueTypeArray)
                    {
                        var typeSize = (uint)typeDescriptions.Size[iElementTypeDescription];

                        var jobChunks = new DynamicArray<StructArrayCrawlerJobChunk>(0, chunkCountForCurrentBatch, Allocator.Persistent);

                        for (var i = 0; i < chunkCountForCurrentBatch && indexOfNextArrayElementToProcess < arrayLength; i++)
                        {
                            var concreteChunkElementCount = (i == remainingChunkCount - 1) ? arrayLength - indexOfNextArrayElementToProcess : standardChunkArrayElementCount;
                            jobChunks.Push(new StructArrayCrawlerJobChunk(
                                managedHeapBytes: in managedHeapSections,
                                vmInfo: in virtualMachineInformation,
                                mangedObjectIndexByAddress: in managedObjectIndexByAddress,
                                nativeObjectOrAllocationFinder: in nativeObjectOrAllocationFinder,
                                configurableCrawlerOptions: crawlData.ConfigurableCrawlerOptions,
                                arrayObjectIndex: in objectIndex,
                                resultingCrawlDataStack: ref stackPool[i],
                                arrayData: in arrayData,
                                typeSize: typeSize,
                                startArrayIndex: indexOfNextArrayElementToProcess,
                                chunkElementCount: concreteChunkElementCount,
                                maxObjectIndexFindableViaPointers: maxObjectIndexFindableViaPointers,
                                fieldLayoutInfos: fieldLayouts
                                ));
                            indexOfNextArrayElementToProcess += concreteChunkElementCount;
                        }
                        var mainJob = new ChunkedParallelArrayCrawlerJob<StructArrayCrawlerJobChunk>(ref jobChunks);
                        var handle = mainJob.ScheduleByRef(mainJob.ChunkCount, 1);
                        handle.Complete();
                        mainJob.Finish(crawlData);
                    }
                    else
                    {
                        var jobChunks = new DynamicArray<ReferenceArrayCrawlerJobChunk>(0, chunkCountForCurrentBatch, Allocator.Persistent);

                        for (var i = 0; i < chunkCountForCurrentBatch && indexOfNextArrayElementToProcess < arrayLength; i++)
                        {
                            var concreteChunkElementCount = (i == remainingChunkCount - 1) ? arrayLength - indexOfNextArrayElementToProcess : standardChunkArrayElementCount;
                            jobChunks.Push(new ReferenceArrayCrawlerJobChunk(
                                managedHeapBytes: in managedHeapSections,
                                vmInfo: in virtualMachineInformation,
                                mangedObjectIndexByAddress: in managedObjectIndexByAddress,
                                nativeObjectOrAllocationFinder: in nativeObjectOrAllocationFinder,
                                configurableCrawlerOptions: crawlData.ConfigurableCrawlerOptions,
                                arrayObjectIndex: in objectIndex,
                                resultingCrawlDataStack: ref stackPool[i],
                                arrayData: in arrayData,
                                pointerSize: crawlData.CachedMemorySnapshot.VirtualMachineInformation.PointerSize,
                                startArrayIndex: indexOfNextArrayElementToProcess,
                                chunkElementCount: concreteChunkElementCount,
                                arrayObjectTypeIndex: obj.ITypeDescription,
                                referenceTypeArrayFieldType: referenceTypeArrayFieldType,
                                maxObjectIndexFindableViaPointers: maxObjectIndexFindableViaPointers
                                ));
                            indexOfNextArrayElementToProcess += concreteChunkElementCount;
                        }
                        var mainJob = new ChunkedParallelArrayCrawlerJob<ReferenceArrayCrawlerJobChunk>(ref jobChunks);
                        var handle = mainJob.ScheduleByRef(mainJob.ChunkCount, 1);
                        handle.Complete();
                        mainJob.Finish(crawlData);
                    }
                    remainingChunkCount = Math.Max(0, remainingChunkCount - chunkCountForCurrentBatch);
                    chunkCountForCurrentBatch = Math.Min(remainingChunkCount, k_MaxConcurrentJobCount);
                }
                stackPool.DisposeArrayOfDisposables();
            }
            return true;
        }

        static long SizeOfObjectInBytes(CachedSnapshot snapshot, int iTypeDescription, int fieldType, BytesAndOffset byteOffset, ManagedMemorySectionEntriesCache heap, bool ignoreErrorsBecauseObjectIsSpeculative)
        {
            if (iTypeDescription < 0) return 0;

            if (snapshot.TypeDescriptions.HasFlag(iTypeDescription, TypeFlags.kArray))
                return ManagedHeapArrayDataTools.ReadArrayObjectSizeInBytes(snapshot, byteOffset, iTypeDescription);

            if (snapshot.TypeDescriptions.ITypeString == iTypeDescription)
            {
                var size = StringTools.ReadStringObjectSizeInBytes(byteOffset, snapshot.VirtualMachineInformation, out var validString,
                    // only log errors for impossible strings if we're sure we're expecting one and not just following some random long into uncharted heap space
                    logError: ignoreErrorsBecauseObjectIsSpeculative ? false : fieldType == snapshot.TypeDescriptions.ITypeString);
                return validString ? size : 0;
            }
            // Array and string are the only types that are special, all other types just have one size, which is stored in the type description,
            // but boxed value types need to take the header size into account as well, so grab the size via GetMinSizeForHeapObjectOfType.
            return snapshot.TypeDescriptions.GetMinSizeForHeapObjectOfType(iTypeDescription, in snapshot.VirtualMachineInformation);
        }

        internal static ManagedObjectInfo ParseObjectHeader(CachedSnapshot snapshot, ulong addressOfHeader, out bool wasAlreadyCrawled, bool ignoreErrorsBecauseObjectIsSpeculative, BytesAndOffset byteOffset, int typeOfReferencingField)
        {
            ref var objectList = ref snapshot.CrawledData.ManagedObjects;
            var objectsByAddress = snapshot.CrawledData.MangedObjectIndexByAddress;

            ManagedObjectInfo objectInfo = new ManagedObjectInfo
            {
                ManagedObjectIndex = -1,
                NativeObjectIndex = -1
            };
            if (!objectsByAddress.TryGetValue(addressOfHeader, out var idx))
            {
                if (TryParseObjectHeader(snapshot, addressOfHeader, out objectInfo, byteOffset, typeOfReferencingField, ignoreErrorsBecauseObjectIsSpeculative: ignoreErrorsBecauseObjectIsSpeculative))
                {
                    objectInfo.ManagedObjectIndex = (int)objectList.Count;
                    objectList.Push(objectInfo);
                    objectsByAddress.Add(addressOfHeader, objectInfo.ManagedObjectIndex);
                }
                wasAlreadyCrawled = false;
                return objectInfo;
            }

            wasAlreadyCrawled = true;
            return objectList[idx];
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        static bool ObjectTypeCouldBeHeldByField(CachedSnapshot snapshot, int objectType, int fieldType)
        {
            // fieldType must be valid but if there is no actual field, passing in TypeDescriptions.ITypeObject is fair game
            Checks.CheckNotEquals(-1, fieldType);

            // if the "object" type is not deriving from object then that could not be a valid heap object
            // (Unless there is something wrong with the reported Type data.
            // I've had an instance where such an object was held via GC Handle from a Scriptable Object, and assumed the Type data
            // was wrong. However, with [PROFB-231](heap data getting collected before Native Object data is collected and while Object Creation isn't locked)
            // that would just be a different indicator for invalid snapshot data, without needing to assume Type data was wrong.)
            if (!snapshot.TypeDescriptions.IsConcrete(objectType))
            {
                // it isn't confirmed to be concrete. If it is safe to ignore, we will,
                // otherwise we'll assume it could be valid yet can't constrain it further and need to early exit here
                return snapshot.TypeDescriptions.IgnoreForHeapObjectTypeChecks(objectType);
            }

            var fieldTypeCouldHoldAnyObject =
                fieldType == snapshot.TypeDescriptions.ITypeObject
                || snapshot.TypeDescriptions.CouldThisFieldBeReadAsAnAddress(fieldType, snapshot);
            if (fieldTypeCouldHoldAnyObject
                // if the field isn't one deriving from object, we are lacking further information in the snapshot data to determine what it could hold, so we'll have to assume everything is valid
                || !snapshot.TypeDescriptions.IsConcrete(fieldType))
                return true;

            return snapshot.TypeDescriptions.DerivesFrom(objectType, fieldType, excludeArrayElementBaseTypes: true);
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        static bool ValidateObjectHeaderType(CachedSnapshot snapshot, BytesAndOffset boHeader, int objectType, int fieldType, out long sizeIncludingHeader, bool ignoreErrorsBecauseObjectIsSpeculative)
        {
            // we're validating against the bytes starting from the header,
            // but the type size might not include the header for a boxed value type, so grab the size for this via GetMinSizeForHeapObjectOfType
            // to make sure that any potential header size is included in the check
            if (objectType >= 0 && boHeader.CouldFitAllocation(snapshot.TypeDescriptions.GetMinSizeForHeapObjectOfType(objectType, in snapshot.VirtualMachineInformation))
                && ObjectTypeCouldBeHeldByField(snapshot, objectType, fieldType))
            {
                try
                {
                    sizeIncludingHeader = SizeOfObjectInBytes(snapshot, objectType, fieldType, boHeader, snapshot.ManagedHeapSections, ignoreErrorsBecauseObjectIsSpeculative);
                }
                catch
                {
                    sizeIncludingHeader = 0;
                    return false;
                }
                if ((sizeIncludingHeader > 0 && boHeader.CouldFitAllocation(sizeIncludingHeader)))
                {
                    return true;
                }
            }
            sizeIncludingHeader = 0;
            return false;
        }

        public static bool TryParseObjectHeader(CachedSnapshot snapshot, ulong addressOfHeader, out ManagedObjectInfo info, BytesAndOffset boHeader, int typeOfReferencingField, bool ignoreErrorsBecauseObjectIsSpeculative)
        {
            bool resolveFailed = false;
            long sizeIncludingHeader = 0;
            var heap = snapshot.ManagedHeapSections;
            info = new ManagedObjectInfo
            {
                ManagedObjectIndex = -1,
                NativeObjectIndex = -1
            };

            if (!boHeader.IsValid)
                resolveFailed = true;
            else
            {
                boHeader.TryReadPointer(out var ptrIdentity);

                info.PtrTypeInfo = ptrIdentity;
                info.ITypeDescription = snapshot.TypeDescriptions.TypeInfo2ArrayIndex(info.PtrTypeInfo);

                if (!ValidateObjectHeaderType(snapshot, boHeader, info.ITypeDescription, typeOfReferencingField, out sizeIncludingHeader, ignoreErrorsBecauseObjectIsSpeculative))
                {
                    // invalid heap byte data. Resolution failed.
                    info.ITypeDescription = -1;
                }
                if (info.ITypeDescription < 0)
                {
                    // Mono headers point at a vtable that points at the class. CoreCLR headers point at the type directly,
                    // so dereferencing again would just read whatever object the first word happens to point at.
                    if (snapshot.IsCoreClr)
                        resolveFailed = true;
                    else if (heap.Find(ptrIdentity, snapshot.VirtualMachineInformation, out BytesAndOffset boIdentity) && boIdentity.IsValid)
                    {
                        boIdentity.TryReadPointer(out var ptrTypeInfo);
                        info.PtrTypeInfo = ptrTypeInfo;
                        info.ITypeDescription = snapshot.TypeDescriptions.TypeInfo2ArrayIndex(info.PtrTypeInfo);
                        resolveFailed = !ValidateObjectHeaderType(snapshot, boHeader, info.ITypeDescription, typeOfReferencingField, out sizeIncludingHeader, ignoreErrorsBecauseObjectIsSpeculative);
                    }
                    else
                    {
                        resolveFailed = true;
                    }
                }
            }

            if (resolveFailed)
            {
                //enable these defines in order to track objects that are missing type data, this can happen if for whatever reason mono got changed and there are types / heap chunks that we do not report
                //addresses here can be used to identify the objects within the Unity process by using a debug version of the mono libs in order to add to the capture where this data resides.
#if DEBUG_VALIDATION && ALL_SYSTEM_TYPE_INFO_IS_REPORTED
                Debug.LogError($"Bad object detected:\nheader at address: 0x{addressOfHeader:X16} \nvtable at address 0x{info.PtrTypeInfo:X16}");
                // can add more data by passing in the stack data
                //+$"\nDetails:\n From object: 0x{data.ptrFrom:X16}\n " +
                //$"From type: {(data.typeFrom != -1 ? snapshot.TypeDescriptions.TypeDescriptionName[data.typeFrom] : info.typeFrom.ToString())}\n" +
                //$"From field: {(data.fieldFrom != -1 ? snapshot.FieldDescriptions.FieldDescriptionName[data.fieldFrom] : info.fieldFrom.ToString())}\n" +
                //    $"From array data: arrayIndex - {(data.fromArrayIndex)}, indexOf - {(data.indexOfFrom)}");
#endif
                info.PtrTypeInfo = 0;
                info.ITypeDescription = -1;
                info.Size = 0;
                info.PtrObject = 0;
                info.data = default;

                return false;
            }

            info.Size = sizeIncludingHeader;
            info.data = boHeader;
            info.PtrObject = addressOfHeader;
            return true;
        }
    }
}

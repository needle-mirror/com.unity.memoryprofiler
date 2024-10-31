//#define CRAWLER_PERFORMANCE_ANALYSIS
// When defining DEBUG_JOBIFIED_CRAWLER, define for the assembly or also define DEBUG_JOBIFIED_CRAWLER in ManagedDataCrawler.cs, ParallelReferenceArrayCrawlerJobChunk.cs, ParallelStaticFieldsCrawlerJobChunk.cs, ParallelStructArrayCrawlerJobChunk.cs, ParallelReferenceArrayCrawlerJobChunk.cs, ChunkedParallelArrayCrawlerJob.cs and JobifiedCrawlDataStacksPool.cs
//#define DEBUG_JOBIFIED_CRAWLER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.MemoryProfiler.Editor.Extensions;
using Unity.MemoryProfiler.Editor.Format;
using Unity.Profiling;
#if CRAWLER_PERFORMANCE_ANALYSIS
using UnityEditor;
using System.Diagnostics;
#endif
#if !UNMANAGED_NATIVE_HASHMAP_AVAILABLE
using AddressToManagedIndexHashMap = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<ulong, long>;
using TypeIndexToCrawlerDataIndexHashMap = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<int, long>;
#else
using AddressToManagedIndexHashMap = Unity.Collections.NativeHashMap<ulong, long>;
using TypeIndexToCrawlerDataIndexHashMap  = Unity.Collections.NativeHashMap<int, long>;
#endif
using static Unity.MemoryProfiler.Editor.CachedSnapshot;
using Debug = UnityEngine.Debug;

namespace Unity.MemoryProfiler.Editor.Managed
{
    static partial class ManagedDataCrawler
    {
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
            public int TypeIndexOfTheTypeOwningTheField;
            // might be a Managed Object or Type
            public SourceIndex ReferenceOwner;
            public int FieldIndexOfTheFieldOnTheReferenceOwner;
            public int FieldIndexOfTheActualField_WhichMayBeOnANestedValueType;
            public int OffsetFromReferenceOwnerHeaderStartToFieldOnValueType;
            public long IndexInReferenceOwningArray;
        }

        class IntermediateCrawlData :
            IDisposable, IChunkedCrawlerJobManagerBase
        {
            public List<int> TypesWithStaticFields { get; }
            public ref DynamicArray<StackCrawlData> CrawlDataStack => ref m_CrawlDataStack;
            DynamicArray<StackCrawlData> m_CrawlDataStack;
            public BlockList<ManagedConnection> ManagedConnections => CachedMemorySnapshot.CrawledData.Connections;
            public CachedSnapshot CachedMemorySnapshot { get; }
            public ref DynamicArray<int> DuplicatedGCHandleTargetsStack => ref m_DuplicatedGCHandleTargetsStack;
            DynamicArray<int> m_DuplicatedGCHandleTargetsStack;
            public HashSet<int> TypesWithObjectsThatMayStillNeedNativeTypeConnection { get; }
            public ref FieldLayouts StaticFieldLayoutInfo => ref m_StaticFieldLayoutInfo;
            FieldLayouts m_StaticFieldLayoutInfo;
            bool m_DisposedStaticInfo = false;
            public ref FieldLayouts FieldLayouts => ref m_FieldLayouts;
            FieldLayouts m_FieldLayouts;

            const int k_InitialStackSize = 256;

            public IntermediateCrawlData(CachedSnapshot snapshot)
            {
                m_DuplicatedGCHandleTargetsStack = new DynamicArray<int>(0, k_InitialStackSize, Allocator.Persistent);
                CachedMemorySnapshot = snapshot;
                m_CrawlDataStack = new DynamicArray<StackCrawlData>(0, k_InitialStackSize, Allocator.Persistent, memClear: k_MemClearCrawlDataStack);
                TypesWithObjectsThatMayStillNeedNativeTypeConnection = new HashSet<int>();

                TypesWithStaticFields = new List<int>();
                for (int i = 0; i != snapshot.TypeDescriptions.Count; ++i)
                {
                    if (snapshot.TypeDescriptions.HasStaticFieldData(i))
                    {
                        TypesWithStaticFields.Add(i);
                    }
                }

                m_StaticFieldLayoutInfo = new FieldLayouts(snapshot.TypeDescriptions.Count, 3);
                m_FieldLayouts = new FieldLayouts(snapshot.TypeDescriptions.Count, 4);

                foreach (var managedToNativeType in snapshot.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex)
                {
                    if (managedToNativeType.Value >= 0 && managedToNativeType.Key >= 0)
                        snapshot.CrawledData.NativeUnityObjectTypeIndexToManagedBaseTypeIndex.TryAdd(managedToNativeType.Value, managedToNativeType.Key);
                }
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
                m_DuplicatedGCHandleTargetsStack.Dispose();
                m_FieldLayouts.Dispose();

                // This should have been disposed of immediately after crawling static fields
                Debug.Assert(m_DisposedStaticInfo, $"{nameof(DisposeOfStaticFieldCrawlingHelpersEarly)} wasn't called");

                m_CrawlDataStack.Dispose();
            }
        }

        static readonly ProfilerMarker k_ConnectNativeToManageObjectProfilerMarker = new ProfilerMarker("Crawler.ConnectNativeToManageObject");
        static readonly ProfilerMarker k_ConnectRemainingManagedTypesToNativeTypesProfilerMarker = new ProfilerMarker("Crawler.ConnectRemainingManagedTypesToNativeTypes");
        static readonly ProfilerMarker k_ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypesProfilerMarker = new ProfilerMarker("Crawler.ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypes");

        static readonly ProfilerMarker k_GatherIntermediateCrawlDataMarker = new ProfilerMarker("GatherIntermediateCrawlData");

        static void GatherIntermediateCrawlData(CachedSnapshot snapshot, IntermediateCrawlData crawlData, ref DynamicArray<StackCrawlData> crawlerStack)
        {
            using var _ = k_GatherIntermediateCrawlDataMarker.Auto();
            unsafe
            {
                var uniqueHandlesPtr = (ulong*)UnsafeUtility.Malloc(sizeof(ulong) * snapshot.GcHandles.Count, UnsafeUtility.AlignOf<ulong>(), Allocator.Temp);
                var uniqueHandlesHeapBytesPtr = (BytesAndOffset*)UnsafeUtility.Malloc(sizeof(BytesAndOffset) * snapshot.GcHandles.Count, UnsafeUtility.AlignOf<BytesAndOffset>(), Allocator.Temp);

                ulong* uniqueHandlesBegin = uniqueHandlesPtr;
                BytesAndOffset* uniqueHandlesHeapBytesBegin = uniqueHandlesHeapBytesPtr;
                int writtenRange = 0;

                var managedHeapSections = snapshot.ManagedHeapSections;
                var vmInfo = snapshot.VirtualMachineInformation;

                // Parse all handles
                for (int i = 0; i != snapshot.GcHandles.Count; i++)
                {
                    var target = snapshot.GcHandles.Target[i];
                    var bytesAndOffsets = managedHeapSections.Find(target, vmInfo);
                    var moi = new ManagedObjectInfo
                    {
                        ManagedObjectIndex = i,
                        ITypeDescription = -1,
                        NativeObjectIndex = -1,
                        data = bytesAndOffsets,
                    };

                    //this can only happen pre 19.3 scripting snapshot implementations where we dumped all handle targets but not the handles.
                    //Eg: multiple handles can have the same target. Future facing we need to start adding that as we move forward
                    if (snapshot.CrawledData.MangedObjectIndexByAddress.ContainsKey(target))
                    {
                        moi.PtrObject = target;
                        crawlData.DuplicatedGCHandleTargetsStack.Push(i);
                    }
                    else
                    {
                        snapshot.CrawledData.MangedObjectIndexByAddress.Add(target, moi.ManagedObjectIndex);
                        *(uniqueHandlesBegin++) = target;
                        *(uniqueHandlesHeapBytesBegin++) = bytesAndOffsets;
                        ++writtenRange;
                    }

                    crawlData.CachedMemorySnapshot.CrawledData.ManagedObjects.Push(moi);
                }
                uniqueHandlesBegin = uniqueHandlesPtr; //reset iterator
                uniqueHandlesHeapBytesBegin = uniqueHandlesHeapBytesPtr;
                ulong* uniqueHandlesEnd = uniqueHandlesPtr + writtenRange;
                //add handles for processing
                while (uniqueHandlesBegin != uniqueHandlesEnd)
                {
                    var address = UnsafeUtility.ReadArrayElement<ulong>(uniqueHandlesBegin++, 0);
                    var bytesAndOffsets = UnsafeUtility.ReadArrayElement<BytesAndOffset>(uniqueHandlesHeapBytesBegin++, 0);
                    crawlerStack.Push(new StackCrawlData { Address = address, Bytes = bytesAndOffsets, ManagedObjectIndex = -1, TypeIndexOfTheTypeOwningTheField = -1, ReferenceOwner = default, FieldIndexOfTheFieldOnTheReferenceOwner = -1, FieldIndexOfTheActualField_WhichMayBeOnANestedValueType = -1, OffsetFromReferenceOwnerHeaderStartToFieldOnValueType = 0, IndexInReferenceOwningArray = -1 }, memClearForExcessExpansion: k_MemClearCrawlDataStack);
                }
                UnsafeUtility.Free(uniqueHandlesPtr, Allocator.Temp);
                UnsafeUtility.Free(uniqueHandlesHeapBytesPtr, Allocator.Temp);
            }
        }

        static readonly ProfilerMarker k_CrawlHandleRootedObjectsMarker = new ProfilerMarker("Crawl GC Handle Rooted Objects");
        static readonly ProfilerMarker k_CrawlHandlesOldDataMarker = new ProfilerMarker("Adjust for Old Snapshot Data");
        static readonly ProfilerMarker k_CrawlStaticsMarker = new ProfilerMarker("Crawl Static Fields");
        static readonly ProfilerMarker k_CrawlStaticallyReferencedObjectsMarker = new ProfilerMarker("Crawl Statically Rooted Objects");
        static readonly ProfilerMarker k_CrawlFinalizeMarker = new ProfilerMarker("Crawler Finalizing");

        public static IEnumerator Crawl(CachedSnapshot snapshot)
        {
            Debug.Assert(!(snapshot.CrawledData?.Crawled ?? false), "Crawling an already crawled snapshot.");
            const int stepCount = 9;
            var status = new EnumerationUtilities.EnumerationStatus(stepCount);

            using var crawlData = new IntermediateCrawlData(snapshot);
#if CRAWLER_PERFORMANCE_ANALYSIS
            var watch = Stopwatch.StartNew();
            Debug.Log("Starting Crawl");
#endif

            // Gather handles and duplicates and enqueue them first to reserve their Managed Object indices
            // Just hold of on actually parsing them until after static field parsing is done.
            yield return status.IncrementStep(stepStatus: "Enqueueing GC Handles for crawling.");
            var gcHandleHeldObjectsToCrawl = new DynamicArray<StackCrawlData>(0, snapshot.GcHandles.Count, Allocator.Persistent, memClear: k_MemClearCrawlDataStack);
            GatherIntermediateCrawlData(snapshot, crawlData, ref gcHandleHeldObjectsToCrawl);

            // these key Unity Types will never show up as objects of their managed base type as they are only ever used via derived types
            if (snapshot.TypeDescriptions.ITypeUnityMonoBehaviour >= 0)
            {
                AddBaseUnityObjectTypesToNativeUnityObjectTypeIndexToManagedBaseTypeIndex(snapshot, snapshot.NativeTypes.MonoBehaviourIdx, snapshot.TypeDescriptions.ITypeUnityMonoBehaviour);
            }
            if (snapshot.TypeDescriptions.ITypeUnityScriptableObject >= 0)
            {
                AddBaseUnityObjectTypesToNativeUnityObjectTypeIndexToManagedBaseTypeIndex(snapshot, snapshot.NativeTypes.ScriptableObjectIdx, snapshot.TypeDescriptions.ITypeUnityScriptableObject);
                AddBaseUnityObjectTypesToNativeUnityObjectTypeIndexToManagedBaseTypeIndex(snapshot, snapshot.NativeTypes.EditorScriptableObjectIdx, snapshot.TypeDescriptions.ITypeUnityScriptableObject);
            }
            if (snapshot.TypeDescriptions.ITypeUnityComponent >= 0)
            {
                AddBaseUnityObjectTypesToNativeUnityObjectTypeIndexToManagedBaseTypeIndex(snapshot, snapshot.NativeTypes.ComponentIdx, snapshot.TypeDescriptions.ITypeUnityComponent);
            }

            yield return status.IncrementStep(stepStatus: "Reading Managed Heap bytes");
            // ensure that the managed heap data is ready for use to avoid calling Complete on the reader's job handle from within the static field crawler jobs
            crawlData.CachedMemorySnapshot.ManagedHeapSections.CompleteHeapBytesRead();

            // Start with static fields and enqueue any heap objects held by their fields
            // For Memory Profiling purposes, we deem static fields to be the strongest binding roots.
            // Therefore, we parse these first to (later) primarily attribute statically held objects to their static fields
            yield return status.IncrementStep(stepStatus: "Crawling managed types with static fields");
            {
                CrawlStaticFields(crawlData);
            }

            //crawl objects referenced by static fields
            yield return status.IncrementStep(stepStatus: "Crawling objects rooted by static fields.");
            CrawlStep(crawlData, k_CrawlStaticallyReferencedObjectsMarker);

            //crawl GC Handle held objects data
            yield return status.IncrementStep(stepStatus: "Crawling GC Handle held objects and their referenences.");
            crawlData.CrawlDataStack.PushRange(gcHandleHeldObjectsToCrawl, memClearForExcessExpansion: k_MemClearCrawlDataStack);
            gcHandleHeldObjectsToCrawl.Dispose();
            CrawlStep(crawlData, k_CrawlHandleRootedObjectsMarker);

            if (crawlData.DuplicatedGCHandleTargetsStack.Count > 0)
            {
                yield return status.IncrementStep(stepStatus: "Adjusting for pre 2019.3 snapshot format data");
                using var _ = k_CrawlHandlesOldDataMarker.Auto();
                //copy crawled object source data for duplicate objects
                foreach (var i in crawlData.DuplicatedGCHandleTargetsStack)
                {
                    var ptr = snapshot.CrawledData.ManagedObjects[i].PtrObject;
                    snapshot.CrawledData.ManagedObjects[i] = snapshot.CrawledData.ManagedObjects[snapshot.CrawledData.MangedObjectIndexByAddress[ptr]];
                }
            }
            //crawl connection data
            yield return status.IncrementStep(stepStatus: "Mapping out Unity Object references");
            using var finalizerMarker = k_CrawlFinalizeMarker.Auto();
            ConnectNativeToManageObject(crawlData);
            ConnectRemainingManagedTypesToNativeTypes(crawlData);
            AddupRawRefCount(snapshot);

            //crawl connection data
            yield return status.IncrementStep(stepStatus: "Tallying up managed memory usage and creating connection maps");

            snapshot.CrawledData.AddUpTotalMemoryUsage(crawlData.CachedMemorySnapshot.ManagedHeapSections);
            snapshot.CrawledData.CreateConnectionMaps(snapshot);
            snapshot.CrawledData.FinishedCrawling();
#if CRAWLER_PERFORMANCE_ANALYSIS
            Debug.Log($"Finished Crawling in {watch.ElapsedMilliseconds}ms");
#endif
        }

        static void AddBaseUnityObjectTypesToNativeUnityObjectTypeIndexToManagedBaseTypeIndex(CachedSnapshot snapshot, int nativeType, int managedType)
        {
            var dict = snapshot.CrawledData.NativeUnityObjectTypeIndexToManagedBaseTypeIndex;
            if (nativeType >= 0 && (!dict.TryGetValue(nativeType, out var alreadyRegisteredManagedTypeIndex)
                || alreadyRegisteredManagedTypeIndex != managedType))
                dict[nativeType] = managedType;
        }

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
            using var typeIndexToCrawlerDataIndex = new TypeIndexToCrawlerDataIndexHashMap(crawlData.TypesWithStaticFields.Count, Allocator.Persistent);
            using var staticTypeFieldCrawlerData = new DynamicArray<StaticFieldsCrawlerJobChunk.StaticTypeFieldCrawlerData>(
                0, k_IdealJobCount, Allocator.Persistent);
            long totalStaticFieldCount = 0L;

            ref var managedObjectIndexByAddress = ref snapshot.CrawledData.MangedObjectIndexByAddress;

            // build full static field crawling data
            foreach (var iTypeDescription in crawlData.TypesWithStaticFields)
            {
                var fieldLayouts = GetFullFieldLayoutForCrawling(crawlData, iTypeDescription, useStaticFields: true);
                if (fieldLayouts.Count <= 0)
                    continue;
                var bytesOffset = new BytesAndOffset(snapshot.TypeDescriptions.StaticFieldBytes[iTypeDescription], snapshot.VirtualMachineInformation.PointerSize);
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
                jobChunks[i] = new(in snapshot.ManagedHeapSections, in virtualMachineInformation, in managedObjectIndexByAddress, ref stack)
                {
                    StaticTypeData = thisChunksData,
                    FieldLayoutInfos = thisChunksFieldLayouts,
                };
                firstField = lastField + 1;
                firstDataEntry = lastDataEntryIndex + 1;
            }
            var mainJob = new ChunkedParallelArrayCrawlerJob<StaticFieldsCrawlerJobChunk>(ref jobChunks);

            var handle = mainJob.ScheduleByRef(mainJob.ChunkCount, 1);

            // and wait for the jobs to finish
            handle.Complete();
            mainJob.Finish(crawlData);

            jobChunks.Dispose();
            // Make some space, we're done with these
            crawlData.DisposeOfStaticFieldCrawlingHelpersEarly();

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
                int iManagedTo = snapshot.UnifiedObjectIndexToManagedObjectIndex(snapshot.Connections.To[i]);
                if (iManagedTo >= 0)
                {
                    ref var objRef = ref snapshot.CrawledData.ManagedObjects[iManagedTo];
                    ++objRef.RefCount;

#if DEBUG_VALIDATION
                    // This whole if block is here to make the investigations for PROF-2420 easier.
                    // It does not manage to fix up all faulty managed objects, but can add extra context to some.
                    if (snapshot.CrawledData.ManagedObjects[iManagedTo].NativeObjectIndex == -1)
                    {
                        int iMissingNativeFrom = snapshot.UnifiedObjectIndexToNativeObjectIndex(snapshot.Connections.From[i]);
                        if (iMissingNativeFrom >= 0)
                        {
                            objRef.NativeObjectIndex = iMissingNativeFrom;
                            var nativeTypeIndex = snapshot.NativeObjects.NativeTypeArrayIndex[iMissingNativeFrom];
                            if (objRef.ITypeDescription == -1 && snapshot.CrawledData.NativeUnityObjectTypeIndexToManagedBaseTypeIndex.TryGetValue(
                                nativeTypeIndex, out var managedBaseTypeIndex)
                                && managedBaseTypeIndex >= 0)
                                objRef.ITypeDescription = managedBaseTypeIndex;

                            var GCHandleReported = snapshot.CrawledData.InvalidManagedObjectsReportedViaGCHandles.ContainsKey(objRef.ManagedObjectIndex);
                            snapshot.CrawledData.InvalidManagedObjectsReportedViaGCHandles.Remove(objRef.ManagedObjectIndex);

                        // remove the "&& false" bits when analysing Managed Heap data reporting fixes
                        // ATM this is not expected to work, as there is a possible delay between reporting GCHandles and dumping the heap chunks
#if DEBUG_VALIDATION && false
                            Debug.LogError($"Found a Managed Object that was reported because a Native Object held {(GCHandleReported ? "a GCHandle" : "some other kind of reference")} to it, " +
                                $"with a target pointing at {(obj.data.IsValid ? "a valid" : "an invalid")} managed heap section. " +
                                $"The Native Object named {snapshot.NativeObjects.ObjectName[iMissingNativeFrom]} was of type {snapshot.NativeTypes.TypeName[nativeTypeIndex]}. " +
                                (obj.ITypeDescription < 0 ? "No Managed base type was found"
                                : $"The Managed Type was set to the managed base type {snapshot.TypeDescriptions.TypeDescriptionName[obj.ITypeDescription]} as a stop gap."));
#endif
                        }
                    }
#endif
                    continue;
                }

                int iNativeTo = snapshot.UnifiedObjectIndexToNativeObjectIndex(snapshot.Connections.To[i]);
                if (iNativeTo >= 0)
                {
                    var rc = ++snapshot.NativeObjects.RefCount[iNativeTo];
                    snapshot.NativeObjects.RefCount[iNativeTo] = rc;
                    continue;
                }
            }
            // remove the "&& false" bits when analysing Managed Heap data reporting fixes
            // ATM this is not expected to work, as there is a possible delay between reporting GCHandles and dumping the heap chunks
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
            ref var objectInfos = ref snapshot.CrawledData.ManagedObjects;

            if (snapshot.TypeDescriptions.Count == 0)
                return;

            int cachedPtrOffset = snapshot.TypeDescriptions.IFieldUnityObjectMCachedPtrOffset;

#if DEBUG_VALIDATION
            // These are used to double-check that all Native -> Managed connections reported via GCHandles on Native Objects are correctly found via m_CachedPtr
            long firstManagedToNativeConnection = snapshot.CrawledData.Connections.Count;
            Dictionary<ulong, int> managedObjectAddressToNativeObjectIndex = new Dictionary<ulong, int>();
#endif

            for (int i = 0; i != objectInfos.Count; i++)
            {
                ref var objectInfo = ref objectInfos[i];
#if DEBUG_VALIDATION
                Debug.Assert(objectInfo.NativeObjectIndex != 0
                    // native object index 0 should only be set for GC Handle reported object with index 0, otherwise it points at faulty data due to missing initialization
                    || (snapshot.NativeObjects.GCHandleIndexToIndex.TryGetValue(i, out var nativeObjectIndexFromGCHandleIndex) && nativeObjectIndexFromGCHandleIndex == 0),
                    "NativeObjectIndex has been left uninitialized.");
#endif

                //Must derive of unity Object
                var isInUnityObjectTypeIndexToNativeTypeIndex = snapshot.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex.GetOrInitializeValue(objectInfo.ITypeDescription, out var nativeTypeIndex, -1);
                var isOrCouldBeAUnityObject = isInUnityObjectTypeIndexToNativeTypeIndex || objectInfo.NativeObjectIndex >= 0;
                if (i >= snapshot.GcHandles.Count)
                {
                    // This could only be a leaked managed shell with not native object backing
                    //objectInfo.NativeObjectIndex = -1; // shouldn't be necessary and shouldn't be anything else
#if DEBUG_VALIDATION
                    // Only GC Handle held objects should have a NativeObjectIndex other than -1
                    Debug.Assert(objectInfo.NativeObjectIndex == -1, "NativeObjectIndex has been set for a non GCHandle held object.");
#endif
                }
                else if (!isInUnityObjectTypeIndexToNativeTypeIndex && objectInfo.ITypeDescription >= 0)
                {
                    // If this managed object was reported because someone had a GC Handle on it, chances are pretty good that there is a Native Object behind this
                    // Given that the type isn't yet known to be a UnityObjectType, something might've gone wrong with the TypeDescription reporting
                    // (annecdotal evidence suggests that ScriptableSingletons residing in assemblies other that the Editor Assembly could be affected this way
                    // If this looks to be the case, try to patch the data back up as good as possible
                    var typeFields = snapshot.TypeDescriptions.FieldIndicesInstance[objectInfo.ITypeDescription];
                    foreach (var field in typeFields)
                    {
                        if (field == snapshot.TypeDescriptions.IFieldUnityObjectMCachedPtrOffset)
                        {
                            isOrCouldBeAUnityObject = true;
                            break;
                        }
                    }
                }
                if (isOrCouldBeAUnityObject)
                {
                    // TODO: Add index to a list of Managed Unity Objects here
                    if (objectInfo.NativeObjectIndex < 0)
                    {
                        var instanceID = CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone;
                        var heapSection = objectInfo.data;
                        if (heapSection.IsValid && heapSection.CouldFitAllocation(cachedPtrOffset))
                            heapSection = heapSection.Add((ulong)cachedPtrOffset);
#if DEBUG_VALIDATION
                        else // in case there was a broken resolution of the heap section on first crawl, which should only be possible to happen if it was a GC Handle held item, that did not resolve to a valid managed object
                        {
                            heapSection = snapshot.ManagedHeapSections.Find(objectInfo.PtrObject + (ulong)cachedPtrOffset, snapshot.VirtualMachineInformation);
                            if (heapSection.TryReadPointer(out var cachedPtr) == BytesAndOffset.PtrReadError.Success)
                                Debug.LogError("Managed Object didn't have valid heap associated");
                        }
#endif
                        if (!heapSection.IsValid)
                        {
                            // Don't warn if this was an attempt to fix broken data
                            if (isInUnityObjectTypeIndexToNativeTypeIndex)
                                Debug.LogWarning("Managed object (addr:" + objectInfo.PtrObject + ", index:" + objectInfo.ManagedObjectIndex + ") does not have data at cachedPtr offset(" + cachedPtrOffset + ")");
                        }
                        else
                        {
                            if (heapSection.TryReadPointer(out var cachedPtr) == BytesAndOffset.PtrReadError.Success)
                                instanceID = snapshot.NativeObjects.NativeObjectAddressToInstanceId.GetValueOrDefault(cachedPtr, CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone);
                            // cachedPtr == 0UL or instanceID == CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone -> Leaked Shell
                            // TODO: Add index to a list of leaked shells here.
                        }

                        if (instanceID != CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone)
                        {
                            if (snapshot.NativeObjects.InstanceId2Index.GetOrInitializeValue(instanceID, out objectInfo.NativeObjectIndex, -1))
                            {
                                snapshot.NativeObjects.ManagedObjectIndex[objectInfo.NativeObjectIndex] = i;
                            }
                        }
                    }
                    if (objectInfo.NativeObjectIndex >= 0)
                    {
                        if (nativeTypeIndex == -1)
                        {
                            nativeTypeIndex = snapshot.NativeObjects.NativeTypeArrayIndex[objectInfo.NativeObjectIndex];

                            if (!isInUnityObjectTypeIndexToNativeTypeIndex)
                            {
                                if (nativeTypeIndex == snapshot.NativeTypes.MonoBehaviourIdx
                                    || nativeTypeIndex == snapshot.NativeTypes.ScriptableObjectIdx
                                    || nativeTypeIndex == snapshot.NativeTypes.EditorScriptableObjectIdx)
                                {
                                    // This acutally WAS a Unity Object with faulty type data reporting, fix up UnityObjectTypeIndexToNativeTypeIndex,
                                    // but set native type to -1 so that ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypes can fix up all managed base types that ARE reported
                                    snapshot.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex.Add(objectInfo.ITypeDescription, -1);
                                    isInUnityObjectTypeIndexToNativeTypeIndex = true;
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

                            ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypes(snapshot, nativeTypeIndex, objectInfo.ITypeDescription);
                        }
                        if (snapshot.HasConnectionOverhaul)
                        {
                            snapshot.CrawledData.Connections.Add(ManagedConnection.MakeUnityEngineObjectConnection(objectInfo.NativeObjectIndex, objectInfo.ManagedObjectIndex));
                            ref var refCount = ref snapshot.NativeObjects.RefCount[objectInfo.NativeObjectIndex];
                            ++refCount;
#if DEBUG_VALIDATION
                            managedObjectAddressToNativeObjectIndex.Add(objectInfo.PtrObject, objectInfo.NativeObjectIndex);
#endif
                        }
                    }
                    if (nativeTypeIndex == -1 && isInUnityObjectTypeIndexToNativeTypeIndex)
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

            // TODO: while fixing [PROFB-231] - Debug this and find out what's up with that. It fails in CI.
            // remove the "&& false" bits when analysing Managed Heap data reporting fixes
            // ATM this is not expected to work, as there is a possible delay between reporting GCHandles and dumping the heap chunks
            // This is here to make the investigations for PROF-2420 easier.
#if DEBUG_VALIDATION && false
            // Double-check that all Native -> Managed connections reported via GCHandles on Native Objects have been correctly found via m_CachedPtr
            if (snapshot.Connections.IndexOfFirstNativeToGCHandleConnection >= 0)
            {
                var gcHandlesCount = snapshot.GcHandles.Count;
                for (long nativeConnectionIndex = snapshot.Connections.IndexOfFirstNativeToGCHandleConnection; nativeConnectionIndex < snapshot.Connections.Count; nativeConnectionIndex++)
                {
                    var nativeObjectIndex = snapshot.Connections.From[nativeConnectionIndex] - gcHandlesCount;
                    var managedShellAddress = snapshot.GcHandles.Target[snapshot.Connections.To[nativeConnectionIndex]];
                    var managedObjectIndex = snapshot.CrawledData.MangedObjectIndexByAddress[managedShellAddress];
                    var managedObject = snapshot.CrawledData.ManagedObjects[managedObjectIndex];
                    if (managedObject.NativeObjectIndex != nativeObjectIndex)
                        Debug.LogError("Native Object is not correctly linked with its Managed Object via NativeObjectIndex");
                    bool foundConnection = managedObjectAddressToNativeObjectIndex.ContainsKey(managedShellAddress);
                    if (!foundConnection)
                        Debug.LogError("Native Object is not correctly linked with its Managed Object via ManagedObjectAddrress");
                }
            }
#endif
        }

        /// <summary>
        /// This method serves a double purpose:
        /// 1. It iterates up the entire managed inheritance chain of the passed managed type and connects every managed type along the way to its base native type.
        ///    This is the only way to find the managed to native type connection for managed types like UnityEngine.ScriptableObject or UnityEngine.MonoBehaviour,
        ///    that will never have an instance of their base types in the snapshot, but an object of a derived type is _nearly_ guaranteed.
        /// 2. As the snapshot does not report the connection from a Native Type to the Managed Base Type, this function checks if viable Managed types
        ///    that it iterates over could be the managed Base Type
        ///
        /// Beyond checking that <see cref="CachedSnapshot.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex"/> previously mapped the managed type to -1,
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
            bool nativeUnityObjectTypeIndexToManagedBaseTypeIsNotYetReported = !snapshot.CrawledData.NativeUnityObjectTypeIndexToManagedBaseTypeIndex.ContainsKey(nativeTypeIndex);

            while (managedType >= 0 && snapshot.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex.TryGetValue(managedType, out var n) && n == -1)
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
                    snapshot.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex[managedType] = nativeTypeIndex;

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
                                    // no need to create a bunch of managed substrings in a hot loop
                                    char* managedSubstring = managedName + startOfNamespaceStrippedManagedTypeName;
                                    if (UnsafeUtility.MemCmp(managedSubstring, nativeName, managedTypeNameLength) == 0)
                                    {
                                        snapshot.CrawledData.NativeUnityObjectTypeIndexToManagedBaseTypeIndex.Add(nativeTypeIndex, managedType);
                                        nativeUnityObjectTypeIndexToManagedBaseTypeIsNotYetReported = false;
                                    }
                                }
                            }
                        }
                    }
                }
                // continue with the base type of this managed object type
                managedType = snapshot.TypeDescriptions.BaseOrElementTypeIndex[managedType];
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
            var unityObjectTypeIndexToNativeTypeIndex = managedTypes.UnityObjectTypeIndexToNativeTypeIndex;
            var managedToNativeTypeDict = new Dictionary<int, int>();
            foreach (var item in unityObjectTypeIndexToNativeTypeIndex)
            {
                // process all Unity Object Types that are not yet connected to their native types.
                if (item.Value == -1)
                {
                    var managedType = item.Key;
                    var topLevelManagedType = managedType;
                    // This process of connecting the managed type to a native type is rather costly
                    // Only spend that effort for types that had actual object instances in the snapshot
                    // Other types will not be displayed in any tables and establishing the connection therefore doesn't matter
                    if (!crawlData.TypesWithObjectsThatMayStillNeedNativeTypeConnection.Contains(managedType))
                        continue;
                    while (managedType >= 0 && unityObjectTypeIndexToNativeTypeIndex.TryGetValue(managedType, out var nativeTypeIndex))
                    {
                        if (nativeTypeIndex == -1)
                        {
                            var typeName = managedTypes.TypeDescriptionName[managedType];
                            if (typeName.StartsWith("Unity"))
                            {
                                typeName = typeName.Substring(typeName.LastIndexOf('.') + 1);
                                nativeTypeIndex = Array.FindIndex(snapshot.NativeTypes.TypeName, e => e.Equals(typeName));
                            }
                        }
                        if (nativeTypeIndex >= 0)
                        {
                            // can't modify the collection while we iterate over it, so store the connectio for after this foreach
                            managedToNativeTypeDict.Add(topLevelManagedType, nativeTypeIndex);
                            break;
                        }
                        // continue with the base type of this managed object type
                        managedType = managedTypes.BaseOrElementTypeIndex[managedType];
                    }
                }
            }
            foreach (var item in managedToNativeTypeDict)
            {
                ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypes(snapshot, item.Value, item.Key);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CrawlRawObjectData(
            IntermediateCrawlData crawlData, BytesAndOffset bytesAndOffsetOfFieldDataWithoutHeader,
            int iTypeDescription, bool useStaticFields, SourceIndex indexOfFrom,
            long fromArrayIndex = -1, bool allowStackExpansion = false)
        {
            var fieldLayouts = GetFullFieldLayoutForCrawling(crawlData, iTypeDescription, useStaticFields);
            CrawlRawObjectData(in crawlData.CachedMemorySnapshot.ManagedHeapSections, crawlData.CachedMemorySnapshot.VirtualMachineInformation, in crawlData.CachedMemorySnapshot.CrawledData.MangedObjectIndexByAddress, ref crawlData.CrawlDataStack, fieldLayouts, bytesAndOffsetOfFieldDataWithoutHeader, indexOfFrom, fromArrayIndex, allowStackExpansion: allowStackExpansion);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining), BurstCompile(CompileSynchronously = true, DisableDirectCall = false, DisableSafetyChecks = k_DisableBurstDebugChecks, Debug = k_DebugBurstJobs)]
        static void CrawlRawObjectData(
            in ManagedMemorySectionEntriesCache managedHeapBytes, in VirtualMachineInformation vmInfo, in AddressToManagedIndexHashMap managedObjectIndexByAddress,
            ref DynamicArray<StackCrawlData> crawlDataStack, DynamicArrayRef<FieldLayoutInfo> fieldLayouts, BytesAndOffset bytesAndOffsetOfFieldDataWithoutHeader,
            SourceIndex indexOfFrom,
            long fromArrayIndex = -1, int insertAtStackIndex = -1, bool allowStackExpansion = false)
        {
            // Can't use foreach here or Burst won't like this method anymore
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < fieldLayouts.Count; i++)
            {
                var fieldLayout = fieldLayouts[i];
                bytesAndOffsetOfFieldDataWithoutHeader = bytesAndOffsetOfFieldDataWithoutHeader.Add((ulong)fieldLayout.OffsetFromPreviousAddress);

                if (bytesAndOffsetOfFieldDataWithoutHeader.TryReadPointer(out var address) != BytesAndOffset.PtrReadError.Success
                    || address == 0)
                    continue;
                BytesAndOffset heapBytes;

                // if the address is an already found managed object, we don't need to look it up in the heap
                if (managedObjectIndexByAddress.TryGetValue(address, out var managedObjectIndex) && managedObjectIndex >= 0)
                    heapBytes = default;
                else if (managedHeapBytes.Find(address, vmInfo, out heapBytes))
                    managedObjectIndex = -1; // Object index is not defined yet and will be set on crawling the object
                else
                    continue; // if it is neither a known managed object nor a valid heap address, we can't crawl it

                var stackCrawlData = new StackCrawlData()
                {
                    Address = address,
                    Bytes = heapBytes,
                    ManagedObjectIndex = managedObjectIndex,
                    TypeIndexOfTheTypeOwningTheField = fieldLayout.IndexOfTheTypeOwningTheActualFieldIndex,
                    ReferenceOwner = indexOfFrom,
                    FieldIndexOfTheFieldOnTheReferenceOwner = fieldLayout.FieldIndexOnReferenceOwner,
                    FieldIndexOfTheActualField_WhichMayBeOnANestedValueType = fieldLayout.ActualFieldIndexOnPotentialNestedValueType,
                    OffsetFromReferenceOwnerHeaderStartToFieldOnValueType = fieldLayout.OffsetFromReferenceOwnerHeaderStartToFieldOnValueType,
                    IndexInReferenceOwningArray = fromArrayIndex
                };
                // to allow for IJobParallelFor parsing, we need to be able to insert the new data at a specific index
                // This does then however require subsequent pruning of non-entries after the job data has been joined together again
                if (insertAtStackIndex >= 0)
                {
                    crawlDataStack[insertAtStackIndex++] = stackCrawlData;
                }
                else
                {
                    if (crawlDataStack.Count >= crawlDataStack.Capacity && !allowStackExpansion)
                        throw new InvalidOperationException("CrawlDataStack is full, can't insert new data");
                    crawlDataStack.Push(stackCrawlData, memClearForExcessExpansion: k_MemClearCrawlDataStack);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
            int iTypeDescription, bool useStaticFields, SourceIndex indexOfFrom = default,
            int baseFieldFrom = -1, int additionalFieldOffset = 0, int fromArrayIndex = -1)
        {

            var fieldIndexCountAssumption = useStaticFields
                ? crawlData.CachedMemorySnapshot.TypeDescriptions.fieldIndicesOwnedStatic[iTypeDescription].Length * 2
                : crawlData.CachedMemorySnapshot.TypeDescriptions.FieldIndicesInstance[iTypeDescription].Length * 4;
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

            var fields = useStaticFields ? snapshot.TypeDescriptions.fieldIndicesOwnedStatic[iTypeDescription] : snapshot.TypeDescriptions.FieldIndicesInstance[iTypeDescription];
            var isNonStaticReferenceType = !useStaticFields && !snapshot.TypeDescriptions.HasFlag(iTypeDescription, TypeFlags.kValueType);
            var objectHeaderSizeSkippedByCrawler = isNonStaticReferenceType ? (int)snapshot.VirtualMachineInformation.ObjectHeaderSize : 0;
            foreach (var iField in fields)
            {

                var iField_TypeDescription_TypeIndex = snapshot.FieldDescriptions.TypeIndex[iField];

                var fieldOffset = snapshot.FieldDescriptions.Offset[iField];
                if (!useStaticFields)
                {
                    fieldOffset -= (int)snapshot.VirtualMachineInformation.ObjectHeaderSize;
                }

                if (snapshot.TypeDescriptions.HasFlag(iField_TypeDescription_TypeIndex, TypeFlags.kValueType))
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
                                additionalFieldOffsetFromFieldOnReferenceOwnerToFieldOnValueType: subFieldOffset + objectHeaderSizeSkippedByCrawler
                            ), memClearForExcessExpansion: k_MemClearFieldLayoutData);
                    }
                    continue; // FIXME: this means the crawler ignores int/long and pointer types e.g. System.Void*! i.e. primitves that the conservative GC might read as possible references
                }

                if (snapshot.TypeDescriptions.Size[iField_TypeDescription_TypeIndex] != snapshot.VirtualMachineInformation.PointerSize)
                {
                    fieldLayoutInfos.Push(new FieldLayoutInfo
                        (
                            remainingFieldCountForThisType: 0,
                            indexOfTheTypeOwningTheActualFieldIndex: iTypeDescription,
                            indexOfTheTypeOfTheReferenceOwner: iTypeDescription,
                            offsetFromPreviousAddress: fieldOffset,
                            fieldIndexOnReferenceOwner: iField,
                            actualFieldIndexOnPotentialNestedValueType: iField,
                            additionalFieldOffsetFromFieldOnReferenceOwnerToFieldOnValueType: objectHeaderSizeSkippedByCrawler
                        ), memClearForExcessExpansion: k_MemClearFieldLayoutData);
                }
            }
        }
        static readonly ProfilerMarker<long> k_CrawlPointerStructArraysJobScheduleMarker = new ProfilerMarker<long>("CrawlPointer - schedule value type array crawler jobs", "array length");
        static readonly ProfilerMarker k_CrawlPointerArrayChunkMarker = new ProfilerMarker("CrawlPointer - schedule chunk off array crawler jobs");
        static readonly ProfilerMarker<long> k_CrawlPointerObjectArraysJobScheduleMarker = new ProfilerMarker<long>("CrawlPointer - schedule reference type array crawler jobs", "array length");

        static readonly ProfilerMarker k_CrawlPointersInStructArraysMarker = new ProfilerMarker("CrawlPointer - handle value type array crawler");
        static readonly ProfilerMarker k_CrawlPointersInObjectArraysMarker = new ProfilerMarker("CrawlPointer - handle reference type array crawler");

        static readonly ProfilerMarker k_CrawlPointerDebugValidationMarker = new ProfilerMarker("CrawlPointer - DEBUG_VALIDATION");

        static bool CrawlPointer(IntermediateCrawlData crawlData)
        {
            Debug.Assert(crawlData.CrawlDataStack.Count > 0);

            var snapshot = crawlData.CachedMemorySnapshot;
            var data = crawlData.CrawlDataStack.Pop();
#if DEBUG_JOBIFIED_CRAWLER
            if (JobifiedCrawlDataStacksPool.IndexOfFoundElement == crawlData.CrawlDataStack.Count)
                JobifiedCrawlDataStacksPool.IndexOfFoundElement = -1;
#endif
            ref var objectList = ref snapshot.CrawledData.ManagedObjects;
            var objectsByAddress = snapshot.CrawledData.MangedObjectIndexByAddress;
            var managedHeapSections = snapshot.ManagedHeapSections;
            var virtualMachineInformation = snapshot.VirtualMachineInformation;
            ManagedObjectInfo obj;
            bool wasAlreadyCrawled = false;
            BytesAndOffset byteOffset = data.Bytes;
            bool referenceOwnerIsNativeObject = false;

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
                if (!byteOffset.IsValid)
                {
#if DEBUG_VALIDATION
                    using var a_ = k_CrawlPointerDebugValidationMarker.Auto();
                    // This whole if block is here to make the investigations for PROF-2420 easier.
                    if(snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(data.Address, out var manageObjectIndex))
                    {
                        for (long i = 0; i < snapshot.GcHandles.Target.Count; i++)
                        {
                            if(snapshot.GcHandles.Target[i] == data.Address)
                            {
                                if (snapshot.CrawledData.InvalidManagedObjectsReportedViaGCHandles.ContainsKey(manageObjectIndex))
                                    break;
                                snapshot.CrawledData.InvalidManagedObjectsReportedViaGCHandles.Add(manageObjectIndex, new ManagedObjectInfo() { PtrObject = data.Address, ManagedObjectIndex = manageObjectIndex, NativeObjectIndex = -1});
                                break;
                            }
                        }
                    }
#endif
                    return false;
                }
                var typeOfReferencingFieldOrArray = GeTypeOfReferencingFieldOrArrayElement(snapshot, data, valueTypeFieldOwningITypeDescription);
                obj = ParseObjectHeader(snapshot, data.Address, out wasAlreadyCrawled, false, byteOffset, typeOfReferencingFieldOrArray);
            }
            else
            {
                obj = objectList[idx];

                // this happens on objects from gcHandles, they are added before any other crawled object but have their ptr set to 0.
                if (obj.PtrObject == 0)
                {
                    var heldAsType = GetGCHandleHeldTypeInfo(snapshot, idx, out var nativeObjectIndex);
                    referenceOwnerIsNativeObject = nativeObjectIndex >= 0;
                    //if(!referenceOwnerIsNativeObject && data.ReferenceOwner.Valid)
                    //{
                    //    // The object is held by some managed reference beyond just the GCHandles entry
                    //    // so there might be more field specifics as part of the crawler stack data that can be used here

                    //    // However, that managed reference might be bogus. The GC Handle might also be bogus due to PROFB-231,
                    //    // but if it does come from a Native Object we wouldn't be here and otherwise it is still more likely
                    //    // to be a random type deriving from System.Object that is held in memory, than that this supposed reference is correct
                    //    // and therefore it shouldn't be used to constrain what object would be found here.
                    //    //var typeOfReferencingFieldOrArray = GeTypeOfReferencingFieldOrArrayElement(snapshot, data, valueTypeFieldOwningITypeDescription);
                    //    //if (typeOfReferencingFieldOrArray >= 0)
                    //    //{
                    //
                    //    //}
                    //}
                    if (!byteOffset.IsValid)
                        // Should only be the case for GC Handle held objects that are not getting their heap bytes searched for in the initial setup phase
                        byteOffset = managedHeapSections.Find(data.Address, virtualMachineInformation);
                    idx = obj.ManagedObjectIndex;
                    if (TryParseObjectHeader(snapshot, data.Address, out obj, byteOffset, typeOfReferencingField: heldAsType, heldByNativeUnityObject: referenceOwnerIsNativeObject))
                    {
                        obj.ManagedObjectIndex = idx;
                        if (referenceOwnerIsNativeObject)
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
                    if (!data.ReferenceOwner.Valid && idx <= snapshot.GcHandles.Count && obj.NativeObjectIndex >= 0)
                        referenceOwnerIsNativeObject = true;
                    wasAlreadyCrawled = true;
                }
            }
            if (!obj.IsValid())
                return false;

            var addConnection = data.ReferenceOwner.Valid;
            // Reference Owner is invalid for stack entries added via GCHandles, but those might not belong to native object.
            // Native Object connections are handled separately and added to the refcount there.
            // Without a valid source, we can't add a valid connection, but we should add to the ref count.
            if (addConnection || !referenceOwnerIsNativeObject)
                ++obj.RefCount;

            objectList[obj.ManagedObjectIndex] = obj;
            objectsByAddress[obj.PtrObject] = obj.ManagedObjectIndex;

            if (addConnection)
            {
                crawlData.ManagedConnections.Add(ManagedConnection.MakeConnection(snapshot, data.ReferenceOwner, obj.ManagedObjectIndex, data.FieldIndexOfTheFieldOnTheReferenceOwner,
                    valueTypeFieldOwningITypeDescription, data.FieldIndexOfTheActualField_WhichMayBeOnANestedValueType, data.OffsetFromReferenceOwnerHeaderStartToFieldOnValueType, data.IndexInReferenceOwningArray));
            }

            if (!wasAlreadyCrawled)
                return CrawlObject(crawlData, obj, byteOffset);

            return true;
        }

        static int GeTypeOfReferencingFieldOrArrayElement(CachedSnapshot snapshot, StackCrawlData data, int valueTypeFieldOwningITypeDescription)
        {
            var holdingFieldIndex = valueTypeFieldOwningITypeDescription >= 0 ? data.FieldIndexOfTheActualField_WhichMayBeOnANestedValueType : data.FieldIndexOfTheFieldOnTheReferenceOwner;
            // HoldingFieldIndex will be negative for references coming from a reference array, or a GCHandle.
            if (holdingFieldIndex < 0)
            {
                if (!data.ReferenceOwner.Valid)
                    // If the reference is not coming from a valid source, the source is a GCHandle.
                    return snapshot.TypeDescriptions.ITypeObject;
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
            Debug.Assert(snapshot.GcHandles.Count > managedObjectIndex, "The index of the managed object is invalid for a GCHandle held object");
            // regular GC Handles can hold anything of type object and are otherwise not specifying what type of object they are holding
            var heldAsType = snapshot.TypeDescriptions.ITypeObject;
            // However, if a native object was reported to hold a GC Handle to a managed object, we can infer the base type of the managed object
            if (snapshot.NativeObjects.GCHandleIndexToIndex.GetOrInitializeValue(managedObjectIndex, out nativeObjectIndex, -1))
            {
                var nativeType = snapshot.NativeObjects.NativeTypeArrayIndex[nativeObjectIndex];
                if (snapshot.CrawledData.NativeUnityObjectTypeIndexToManagedBaseTypeIndex.TryGetValue(nativeType, out var managedBaseType))
                    heldAsType = managedBaseType;
                else // at the very least, we can make the type more specific than just ´object´ and upgrade it to UnityEngine.Object
                    heldAsType = snapshot.TypeDescriptions.ITypeUnityObject;
            }
            return heldAsType;
        }

        static bool CrawlObject(IntermediateCrawlData crawlData, ManagedObjectInfo obj, BytesAndOffset byteOffset)
        {
            var snapshot = crawlData.CachedMemorySnapshot;
            var typeDescriptions = snapshot.TypeDescriptions;
            var virtualMachineInformation = snapshot.VirtualMachineInformation;

            if (!typeDescriptions.HasFlag(obj.ITypeDescription, TypeFlags.kArray))
            {
                CrawlRawObjectData(crawlData, byteOffset.Add(snapshot.VirtualMachineInformation.ObjectHeaderSize), obj.ITypeDescription, false, new SourceIndex(SourceIndex.SourceId.ManagedObject, obj.ManagedObjectIndex), allowStackExpansion: true);

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

            var arrayData = byteOffset.Add(virtualMachineInformation.ArrayHeaderSize);
            var arrayObjectIndex = new SourceIndex(SourceIndex.SourceId.ManagedObject, obj.ManagedObjectIndex);

            var isValueTypeArray = typeDescriptions.HasFlag(iElementTypeDescription, TypeFlags.kValueType);

            var fieldLayouts = GetFullFieldLayoutForCrawling(crawlData, iElementTypeDescription, false);

            if (isValueTypeArray && fieldLayouts.Count == 0)
                return false;

            ref var managedObjectIndexByAddress = ref snapshot.CrawledData.MangedObjectIndexByAddress;

            var managedHeapSections = snapshot.ManagedHeapSections;
            ref var crawlDataStack = ref crawlData.CrawlDataStack;

            var fieldCountPerArrayElement = isValueTypeArray ? fieldLayouts.Count : 1;

            if (arrayLength <= 0 || arrayLength <= (isValueTypeArray ? (JobifiedArrayCrawlingFieldCountThreshold / fieldCountPerArrayElement) : JobifiedArrayCrawlingFieldCountThreshold))
            {
                using var _ = isValueTypeArray ? k_CrawlPointersInStructArraysMarker.Auto() : k_CrawlPointersInObjectArraysMarker.Auto();

                if (isValueTypeArray)
                {
                    var typeSize = (ulong)typeDescriptions.Size[iElementTypeDescription];

                    for (var i = 0; i < arrayLength; i++)
                    {
                        CrawlRawObjectData(in snapshot.ManagedHeapSections, in virtualMachineInformation, in managedObjectIndexByAddress, ref crawlDataStack, fieldLayouts, arrayData, arrayObjectIndex, fromArrayIndex: i, allowStackExpansion: true);
                        arrayData = arrayData.Add(typeSize);
                    }
                }
                else
                {
                    for (var i = 0; i < arrayLength; i++)
                    {
                        if (arrayData.TryReadPointer(out var arrayDataPtr) != BytesAndOffset.PtrReadError.Success)
                            return false;
                        if (arrayDataPtr != 0)
                        {
                            if (managedObjectIndexByAddress.TryGetValue(arrayDataPtr, out var managedObjectIndex))
                                crawlDataStack.Push(new StackCrawlData() { Address = arrayDataPtr, ManagedObjectIndex = managedObjectIndex, TypeIndexOfTheTypeOwningTheField = obj.ITypeDescription, ReferenceOwner = arrayObjectIndex, FieldIndexOfTheFieldOnTheReferenceOwner = -1, FieldIndexOfTheActualField_WhichMayBeOnANestedValueType = -1, OffsetFromReferenceOwnerHeaderStartToFieldOnValueType = 0, IndexInReferenceOwningArray = i }, memClearForExcessExpansion: k_MemClearCrawlDataStack);
                            else if (managedHeapSections.Find(arrayDataPtr, virtualMachineInformation, out var bytes))
                                crawlDataStack.Push(new StackCrawlData() { Address = arrayDataPtr, Bytes = bytes, ManagedObjectIndex = -1, TypeIndexOfTheTypeOwningTheField = obj.ITypeDescription, ReferenceOwner = arrayObjectIndex, FieldIndexOfTheFieldOnTheReferenceOwner = -1, FieldIndexOfTheActualField_WhichMayBeOnANestedValueType = -1, OffsetFromReferenceOwnerHeaderStartToFieldOnValueType = 0, IndexInReferenceOwningArray = i }, memClearForExcessExpansion: k_MemClearCrawlDataStack);
                        }
                        arrayData = arrayData.NextPointer();
                    }
                }
                return true;
            }
            else
            {
                using var _ = isValueTypeArray ? k_CrawlPointerStructArraysJobScheduleMarker.Auto(arrayLength) : k_CrawlPointerObjectArraysJobScheduleMarker.Auto(arrayLength);

                var totalFieldCount = arrayLength * fieldCountPerArrayElement;
                var remainingChunkCount = CalculateBatchElementCount(totalFieldCount);

                var chunkCountForCurrentBatch = Math.Min(remainingChunkCount, k_MaxConcurrentJobCount);

                var standardChunkArrayElementCount = arrayLength / remainingChunkCount;
                // The last chunk size might differ
                var lastChunkFieldCount = totalFieldCount - standardChunkArrayElementCount * (remainingChunkCount - 1) * fieldCountPerArrayElement;
                var fieldCountPerStack = Math.Max(standardChunkArrayElementCount * fieldCountPerArrayElement, lastChunkFieldCount);
                // adding all stacks we need to this pool for easy reuse. It also means we can dispose them all in one go.
                using var stackPool = new DynamicArray<DynamicArray<StackCrawlData>>(chunkCountForCurrentBatch, Allocator.Persistent);
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
                                mangedObjectIndexByAddress: managedObjectIndexByAddress,
                                arrayObjectIndex: arrayObjectIndex,
                                resultingCrawlDataStack: ref stackPool[i],
                                arrayData: arrayData,
                                typeSize: typeSize,
                                startArrayIndex: indexOfNextArrayElementToProcess,
                                chunkElementCount: concreteChunkElementCount
                                )
                            {
                                FieldLayoutInfo = fieldLayouts
                            });
                            indexOfNextArrayElementToProcess += concreteChunkElementCount;
                        }
                        var mainJob = new ChunkedParallelArrayCrawlerJob<StructArrayCrawlerJobChunk>(ref jobChunks);
                        var handle = mainJob.ScheduleByRef(mainJob.ChunkCount, 1);
                        handle.Complete();
                        mainJob.Finish(crawlData);
                    }
                    else
                    {
                        var typeSize = (uint)typeDescriptions.Size[iElementTypeDescription];

                        var jobChunks = new DynamicArray<ReferenceArrayCrawlerJobChunk>(0, chunkCountForCurrentBatch, Allocator.Persistent);

                        for (var i = 0; i < chunkCountForCurrentBatch && indexOfNextArrayElementToProcess < arrayLength; i++)
                        {
                            var concreteChunkElementCount = (i == remainingChunkCount - 1) ? arrayLength - indexOfNextArrayElementToProcess : standardChunkArrayElementCount;
                            jobChunks.Push(new ReferenceArrayCrawlerJobChunk(
                                managedHeapBytes: in managedHeapSections,
                                vmInfo: in virtualMachineInformation,
                                mangedObjectIndexByAddress: managedObjectIndexByAddress,
                                arrayObjectIndex: arrayObjectIndex,
                                resultingCrawlDataStack: ref stackPool[i],
                                arrayData: arrayData,
                                pointerSize: crawlData.CachedMemorySnapshot.VirtualMachineInformation.PointerSize,
                                startArrayIndex: indexOfNextArrayElementToProcess,
                                chunkElementCount: concreteChunkElementCount,
                                arrayObjectTypeIndex: obj.ITypeDescription
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
            }
            return true;
        }

        static long SizeOfObjectInBytes(CachedSnapshot snapshot, int iTypeDescription, BytesAndOffset byteOffset, ManagedMemorySectionEntriesCache heap)
        {
            if (iTypeDescription < 0) return 0;

            if (snapshot.TypeDescriptions.HasFlag(iTypeDescription, TypeFlags.kArray))
                return ManagedHeapArrayDataTools.ReadArrayObjectSizeInBytes(snapshot, byteOffset, iTypeDescription);

            if (snapshot.TypeDescriptions.ITypeString == iTypeDescription)
                return StringTools.ReadStringObjectSizeInBytes(byteOffset, snapshot.VirtualMachineInformation);

            // array and string are the only types that are special, all other types just have one size, which is stored in the type description
            return snapshot.TypeDescriptions.Size[iTypeDescription];
        }

        internal static ManagedObjectInfo ParseObjectHeader(CachedSnapshot snapshot, ulong addressOfHeader, out bool wasAlreadyCrawled, bool ignoreBadHeaderError, BytesAndOffset byteOffset, int typeOfReferencingField)
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
                if (TryParseObjectHeader(snapshot, addressOfHeader, out objectInfo, byteOffset, typeOfReferencingField, heldByNativeUnityObject: false))
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool ObjectTypeCouldBeHeldByField(CachedSnapshot snapshot, int objectType, int fieldType, bool heldByNativeUnityObject)
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

            // TODO: extend by checks for void* fields and other types that could hold references once they get processed as potential reference holders by the crawler
            if (fieldType == snapshot.TypeDescriptions.ITypeObject || fieldType == snapshot.TypeDescriptions.ITypeIntPtr
                // if the field isn't one deriving from object, we are lacking further information in the snapshot data to determine what it could hold, so we'll have to assume everything is valid
                || !snapshot.TypeDescriptions.IsConcrete(fieldType))
                return true;

            return snapshot.TypeDescriptions.DerivesFrom(objectType, fieldType, excludeArrayElementBaseTypes: true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool ValidateObjectHeaderType(CachedSnapshot snapshot, BytesAndOffset boHeader, int objectType, int fieldType, bool heldByNativeUnityObject, out long size)
        {
            if (objectType >= 0 && boHeader.CouldFitAllocation(snapshot.TypeDescriptions.Size[objectType]) && ObjectTypeCouldBeHeldByField(snapshot, objectType, fieldType, heldByNativeUnityObject))
            {
                try
                {
                    size = SizeOfObjectInBytes(snapshot, objectType, boHeader, snapshot.ManagedHeapSections);
                }
                catch
                {
                    size = 0;
                    return false;
                }
                if ((size > 0 && boHeader.CouldFitAllocation(size)))
                {
                    return true;
                }
            }
            size = 0;
            return false;
        }

        public static bool TryParseObjectHeader(CachedSnapshot snapshot, ulong addressOfHeader, out ManagedObjectInfo info, BytesAndOffset boHeader, int typeOfReferencingField, bool heldByNativeUnityObject)
        {
            bool resolveFailed = false;
            long size = 0;
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

                if (!ValidateObjectHeaderType(snapshot, boHeader, info.ITypeDescription, typeOfReferencingField, heldByNativeUnityObject, out size))
                {
                    // invalid heap byte data. Resolution failed.
                    info.ITypeDescription = -1;
                }
                if (info.ITypeDescription < 0)
                {
                    var boIdentity = heap.Find(ptrIdentity, snapshot.VirtualMachineInformation);
                    if (boIdentity.IsValid)
                    {
                        boIdentity.TryReadPointer(out var ptrTypeInfo);
                        info.PtrTypeInfo = ptrTypeInfo;
                        info.ITypeDescription = snapshot.TypeDescriptions.TypeInfo2ArrayIndex(info.PtrTypeInfo);
                        resolveFailed = !ValidateObjectHeaderType(snapshot, boHeader, info.ITypeDescription, typeOfReferencingField, heldByNativeUnityObject, out size);
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

            info.Size = size;
            info.data = boHeader;
            info.PtrObject = addressOfHeader;
            return true;
        }
    }
}

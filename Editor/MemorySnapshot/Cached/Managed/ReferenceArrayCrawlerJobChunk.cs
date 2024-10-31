// When defining DEBUG_JOBIFIED_CRAWLER, define for the assembly or also define DEBUG_JOBIFIED_CRAWLER in ManagedDataCrawler.cs, ParallelReferenceArrayCrawlerJobChunk.cs, ParallelStaticFieldsCrawlerJobChunk.cs, ParallelStructArrayCrawlerJobChunk.cs, ParallelReferenceArrayCrawlerJobChunk.cs, ChunkedParallelArrayCrawlerJob.cs and JobifiedCrawlDataStacksPool.cs
//#define DEBUG_JOBIFIED_CRAWLER
using Unity.Burst;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;
#if !UNMANAGED_NATIVE_HASHMAP_AVAILABLE
using AddressToManagedIndexHashMap = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<ulong, long>;
#else
using AddressToManagedIndexHashMap = Unity.Collections.NativeHashMap<ulong, long>;
#endif

namespace Unity.MemoryProfiler.Editor.Managed
{
    static partial class ManagedDataCrawler
    {
        [BurstCompile(CompileSynchronously = true, DisableDirectCall = false, DisableSafetyChecks = k_DisableBurstDebugChecks, Debug = k_DebugBurstJobs)]
        struct ReferenceArrayCrawlerJobChunk : ICrawlerJobChunk<ReferenceArrayCrawlerJobChunk>
        {
            [ReadOnly]
            readonly BytesAndOffset m_ArrayData;
            long m_ChunkElementCount;

            DynamicArray<StackCrawlData> m_ResultingCrawlDataStack;
            public DynamicArray<StackCrawlData> ResultingCrawlDataStack => m_ResultingCrawlDataStack;
#if DEBUG_JOBIFIED_CRAWLER
            public long IndexOfFoundElement { get; private set; }
#endif

            readonly uint m_PointerSize;
            readonly long m_StartArrayIndex;
            readonly SourceIndex m_ArrayObjectIndex;
            readonly ManagedMemorySectionEntriesCache m_ManagedHeapBytes;
            readonly VirtualMachineInformation m_VMInfo;
            [ReadOnly]
            readonly AddressToManagedIndexHashMap m_MangedObjectIndexByAddress;
            int m_ArrayObjectTypeIndex;

            public ReferenceArrayCrawlerJobChunk(
                in ManagedMemorySectionEntriesCache managedHeapBytes, in VirtualMachineInformation vmInfo, in AddressToManagedIndexHashMap mangedObjectIndexByAddress,
                SourceIndex arrayObjectIndex,
                ref DynamicArray<StackCrawlData> resultingCrawlDataStack, BytesAndOffset arrayData,
                 uint pointerSize, long startArrayIndex, long chunkElementCount, int arrayObjectTypeIndex)
            {
                // assign all the fields
                m_ManagedHeapBytes = managedHeapBytes;
                m_VMInfo = vmInfo;
                m_MangedObjectIndexByAddress = mangedObjectIndexByAddress;
                m_ArrayData = arrayData;
                m_PointerSize = pointerSize;
                m_StartArrayIndex = startArrayIndex;
                m_ResultingCrawlDataStack = resultingCrawlDataStack;
                m_ArrayObjectIndex = arrayObjectIndex;
                m_ChunkElementCount = chunkElementCount;
                // Set by InitJobTypeSpecificFields
                m_ArrayObjectTypeIndex = arrayObjectTypeIndex;
#if DEBUG_JOBIFIED_CRAWLER
                IndexOfFoundElement = -1;
#endif
            }

            public void Process()
            {
                var end = m_StartArrayIndex + m_ChunkElementCount;
                for (var index = m_StartArrayIndex; index < end; index++)
                {
                    if (m_ArrayData.Add((ulong)(index * m_PointerSize)).TryReadPointer(out var arrayDataPtr) == BytesAndOffset.PtrReadError.Success)
                    {
                        // don't process null pointers
                        if (arrayDataPtr != 0)
                        {
#if DEBUG_JOBIFIED_CRAWLER
                            // if the current item needs monitoring in CrawlPointer, add that logic here
                            //if(something)
                            //  IndexOfFoundElement = m_ResultingCrawlDataStack.Count;
#endif
                            if (m_MangedObjectIndexByAddress.TryGetValue(arrayDataPtr, out var managedObjectIndex))
                                m_ResultingCrawlDataStack.Push(new StackCrawlData() { Address = arrayDataPtr, ManagedObjectIndex = managedObjectIndex, TypeIndexOfTheTypeOwningTheField = m_ArrayObjectTypeIndex, ReferenceOwner = m_ArrayObjectIndex, FieldIndexOfTheFieldOnTheReferenceOwner = -1, FieldIndexOfTheActualField_WhichMayBeOnANestedValueType = -1, OffsetFromReferenceOwnerHeaderStartToFieldOnValueType = 0, IndexInReferenceOwningArray = index }, memClearForExcessExpansion: k_MemClearCrawlDataStack);
                            else if (m_ManagedHeapBytes.Find(arrayDataPtr, m_VMInfo, out var bytes))
                                m_ResultingCrawlDataStack.Push(new StackCrawlData() { Address = arrayDataPtr, Bytes = bytes, ManagedObjectIndex = -1, TypeIndexOfTheTypeOwningTheField = m_ArrayObjectTypeIndex, ReferenceOwner = m_ArrayObjectIndex, FieldIndexOfTheFieldOnTheReferenceOwner = -1, FieldIndexOfTheActualField_WhichMayBeOnANestedValueType = -1, OffsetFromReferenceOwnerHeaderStartToFieldOnValueType = 0, IndexInReferenceOwningArray = index }, memClearForExcessExpansion: k_MemClearCrawlDataStack);
                        }
                    }
                }
            }

            public void Dispose()
            {
            }
        }
    }
}

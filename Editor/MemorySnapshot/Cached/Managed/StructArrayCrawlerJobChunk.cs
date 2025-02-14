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
        struct StructArrayCrawlerJobChunk : ICrawlerJobChunk<StructArrayCrawlerJobChunk>
        {
            [ReadOnly]
            public readonly DynamicArrayRef<FieldLayoutInfo> FieldLayoutInfo;

            [ReadOnly]
            readonly BytesAndOffset m_ArrayData;
            readonly long m_ChunkElementCount;

            DynamicArray<StackCrawlData> m_ResultingCrawlDataStack;
            public readonly DynamicArray<StackCrawlData> ResultingCrawlDataStack => m_ResultingCrawlDataStack;
#if DEBUG_JOBIFIED_CRAWLER
            public long IndexOfFoundElement { get; private set; }
#endif

            readonly uint m_TypeSize;
            readonly long m_StartArrayIndex;
            readonly long m_MaxObjectIndexFindableViaPointers;
            readonly SourceIndex m_ArrayObjectIndex;
            readonly ManagedMemorySectionEntriesCache m_ManagedHeapBytes;
            readonly VirtualMachineInformation m_VMInfo;
            [ReadOnly]
            readonly AddressToManagedIndexHashMap m_MangedObjectIndexByAddress;

            [ReadOnly]
            readonly NativeObjectOrAllocationFinder m_NativeObjectOrAllocationFinder;

            public StructArrayCrawlerJobChunk(
                in ManagedMemorySectionEntriesCache managedHeapBytes, in VirtualMachineInformation vmInfo, in AddressToManagedIndexHashMap mangedObjectIndexByAddress,
                in NativeObjectOrAllocationFinder nativeObjectOrAllocationFinder,
                in SourceIndex arrayObjectIndex,
                ref DynamicArray<StackCrawlData> resultingCrawlDataStack, in BytesAndOffset arrayData,
                in DynamicArrayRef<FieldLayoutInfo> fieldLayoutInfos,
                uint typeSize, long startArrayIndex, long chunkElementCount, long maxObjectIndexFindableViaPointers)
            {
                // assign all the fields
                m_ManagedHeapBytes = managedHeapBytes;
                m_VMInfo = vmInfo;
                m_MangedObjectIndexByAddress = mangedObjectIndexByAddress;
                m_NativeObjectOrAllocationFinder = nativeObjectOrAllocationFinder;
                m_ArrayData = arrayData;
                m_TypeSize = typeSize;
                m_StartArrayIndex = startArrayIndex;
                m_MaxObjectIndexFindableViaPointers = maxObjectIndexFindableViaPointers;
                m_ResultingCrawlDataStack = resultingCrawlDataStack;
                m_ArrayObjectIndex = arrayObjectIndex;
                m_ChunkElementCount = chunkElementCount;
                FieldLayoutInfo = fieldLayoutInfos;
#if DEBUG_JOBIFIED_CRAWLER
                IndexOfFoundElement = -1;
#endif
            }

            public void Process()
            {
                for (int index = 0; index < m_ChunkElementCount; ++index)
                {
#if DEBUG_JOBIFIED_CRAWLER
                    // if the current item needs monitoring in CrawlPointer, add that logic here
                    //if(something)
                    //IndexOfFoundElement = m_ResultingCrawlDataStack.Count;
#endif
                    var indexInArray = m_StartArrayIndex + index;
                    CrawlRawObjectData(m_ManagedHeapBytes, in m_VMInfo, m_MangedObjectIndexByAddress,
                        in m_NativeObjectOrAllocationFinder,
                        ref m_ResultingCrawlDataStack, in FieldLayoutInfo, m_ArrayData.Add((ulong)(indexInArray * m_TypeSize)), in m_ArrayObjectIndex,
                        fromArrayIndex: indexInArray, maxObjectIndexFindableViaPointers: m_MaxObjectIndexFindableViaPointers);
                }
            }

            public void Dispose()
            {
            }
        }
    }
}

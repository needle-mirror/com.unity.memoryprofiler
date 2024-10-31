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
            public DynamicArrayRef<FieldLayoutInfo> FieldLayoutInfo;

            [ReadOnly]
            readonly BytesAndOffset m_ArrayData;
            long m_ChunkElementCount;

            DynamicArray<StackCrawlData> m_ResultingCrawlDataStack;
            public DynamicArray<StackCrawlData> ResultingCrawlDataStack => m_ResultingCrawlDataStack;
#if DEBUG_JOBIFIED_CRAWLER
            public long IndexOfFoundElement { get; private set; }
#endif

            readonly uint m_TypeSize;
            readonly long m_StartArrayIndex;
            readonly SourceIndex m_ArrayObjectIndex;
            readonly ManagedMemorySectionEntriesCache m_ManagedHeapBytes;
            readonly VirtualMachineInformation m_VMInfo;
            [ReadOnly]
            readonly AddressToManagedIndexHashMap m_MangedObjectIndexByAddress;

            public StructArrayCrawlerJobChunk(
                in ManagedMemorySectionEntriesCache managedHeapBytes, in VirtualMachineInformation vmInfo, in AddressToManagedIndexHashMap mangedObjectIndexByAddress,
                SourceIndex arrayObjectIndex,
                ref DynamicArray<StackCrawlData> resultingCrawlDataStack, BytesAndOffset arrayData,
                 uint typeSize, long startArrayIndex, long chunkElementCount)
            {
                // assign all the fields
                m_ManagedHeapBytes = managedHeapBytes;
                m_VMInfo = vmInfo;
                m_MangedObjectIndexByAddress = mangedObjectIndexByAddress;
                m_ArrayData = arrayData;
                m_TypeSize = typeSize;
                m_StartArrayIndex = startArrayIndex;
                m_ResultingCrawlDataStack = resultingCrawlDataStack;
                m_ArrayObjectIndex = arrayObjectIndex;
                m_ChunkElementCount = chunkElementCount;
                // Set by InitJobTypeSpecificFields
                FieldLayoutInfo = default;
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
                    CrawlRawObjectData(in m_ManagedHeapBytes, in m_VMInfo, in m_MangedObjectIndexByAddress, ref m_ResultingCrawlDataStack, FieldLayoutInfo, m_ArrayData.Add((ulong)(indexInArray * m_TypeSize)), m_ArrayObjectIndex,
                        fromArrayIndex: indexInArray);
                }
            }

            public void Dispose()
            {
            }
        }
    }
}

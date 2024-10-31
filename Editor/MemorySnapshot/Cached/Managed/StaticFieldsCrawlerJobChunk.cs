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
        struct StaticFieldsCrawlerJobChunk
            : ICrawlerJobChunk<StaticFieldsCrawlerJobChunk>
        {
            [ReadOnly]
            public DynamicArrayRef<FieldLayoutInfo> FieldLayoutInfos;
#if DEBUG_JOBIFIED_CRAWLER
            public long IndexOfFoundElement { get; private set; }
#endif
            public struct StaticTypeFieldCrawlerData
            {
                public int TypeIndex;
                public long FieldLayoutIndex;
                public BytesAndOffset Bytes;
            }
            [ReadOnly]
            public DynamicArrayRef<StaticTypeFieldCrawlerData> StaticTypeData;

            public DynamicArray<StackCrawlData> m_ResultingCrawlDataStack;
            public DynamicArray<StackCrawlData> ResultingCrawlDataStack => m_ResultingCrawlDataStack;
            ManagedMemorySectionEntriesCache m_ManagedHeapBytes;
            VirtualMachineInformation m_VMInfo;
            readonly AddressToManagedIndexHashMap m_MangedObjectIndexByAddress;


            public StaticFieldsCrawlerJobChunk(
                in ManagedMemorySectionEntriesCache managedHeapBytes, in VirtualMachineInformation vmInfo, in AddressToManagedIndexHashMap managedObjectIndexByAddress,
                ref DynamicArray<StackCrawlData> resultingCrawlDataStack)
            {
                // assign all the fields
                m_ManagedHeapBytes = managedHeapBytes;
                m_VMInfo = vmInfo;
                m_ResultingCrawlDataStack = resultingCrawlDataStack;
                m_MangedObjectIndexByAddress = managedObjectIndexByAddress;
                // Set by InitJobTypeSpecificFields
                StaticTypeData = default;
                FieldLayoutInfos = default;
#if DEBUG_JOBIFIED_CRAWLER
                IndexOfFoundElement = -1;
#endif
            }

            public void Process()
            {
                for (var index = 0; index < StaticTypeData.Count; index++)
                {
                    var typeIndex = StaticTypeData[index].TypeIndex;
                    var fieldLayoutIndex = StaticTypeData[index].FieldLayoutIndex;
                    DynamicArrayRef<FieldLayoutInfo> fieldLayouts;
                    unsafe
                    {
                        fieldLayouts = DynamicArrayRef<FieldLayoutInfo>.ConvertExistingDataToDynamicArrayRef(
                                            dataPointer: FieldLayoutInfos.GetUnsafeTypedPtr() + fieldLayoutIndex,
                                            length: FieldLayoutInfos[fieldLayoutIndex].RemainingFieldCountForThisType + 1);
                    }
                    var staticFieldBytes = StaticTypeData[index].Bytes;
#if DEBUG_JOBIFIED_CRAWLER
                    // if the current item needs monitoring in CrawlPointer, add that logic here
                    //const long k_FieldIndex = 0;
                    //if(something)
                    //  IndexOfFoundElement = m_ResultingCrawlDataStack.Count + k_FieldIndex;
#endif
                    CrawlRawObjectData(in m_ManagedHeapBytes, in m_VMInfo, in m_MangedObjectIndexByAddress, ref m_ResultingCrawlDataStack, fieldLayouts, staticFieldBytes, new SourceIndex(SourceIndex.SourceId.ManagedType, typeIndex));

                }
            }

            public void Dispose()
            {
                if (m_ResultingCrawlDataStack.IsCreated)
                    m_ResultingCrawlDataStack.Dispose();
            }
        }
    }
}

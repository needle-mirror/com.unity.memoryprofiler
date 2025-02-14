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
        struct StaticFieldsCrawlerJobChunk : ICrawlerJobChunk<StaticFieldsCrawlerJobChunk>
        {
            [ReadOnly]
            public readonly DynamicArrayRef<FieldLayoutInfo> FieldLayoutInfos;
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
            public readonly DynamicArrayRef<StaticTypeFieldCrawlerData> StaticTypeData;

            public DynamicArray<StackCrawlData> m_ResultingCrawlDataStack;
            public readonly DynamicArray<StackCrawlData> ResultingCrawlDataStack => m_ResultingCrawlDataStack;
            ManagedMemorySectionEntriesCache m_ManagedHeapBytes;
            readonly VirtualMachineInformation m_VMInfo;
            [ReadOnly]
            readonly AddressToManagedIndexHashMap m_MangedObjectIndexByAddress;

            [ReadOnly]
            readonly NativeObjectOrAllocationFinder m_NativeObjectOrAllocationFinder;

            readonly long m_MaxObjectIndexFindableViaPointers;

            public StaticFieldsCrawlerJobChunk(
                in ManagedMemorySectionEntriesCache managedHeapBytes, in VirtualMachineInformation vmInfo, in AddressToManagedIndexHashMap managedObjectIndexByAddress,
                in NativeObjectOrAllocationFinder nativeObjectOrAllocationFinder,
                ref DynamicArray<StackCrawlData> resultingCrawlDataStack,
                in DynamicArrayRef<StaticTypeFieldCrawlerData> staticTypeData,
                in DynamicArrayRef<FieldLayoutInfo> fieldLayoutInfos,
                long maxObjectIndexFindableViaPointers)
            {
                // assign all the fields
                m_ManagedHeapBytes = managedHeapBytes;
                m_VMInfo = vmInfo;
                m_ResultingCrawlDataStack = resultingCrawlDataStack;
                m_MangedObjectIndexByAddress = managedObjectIndexByAddress;
                m_NativeObjectOrAllocationFinder = nativeObjectOrAllocationFinder;
                StaticTypeData = staticTypeData;
                FieldLayoutInfos = fieldLayoutInfos;
                m_MaxObjectIndexFindableViaPointers = maxObjectIndexFindableViaPointers;
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
#if DEBUG_JOBIFIED_CRAWLER
                    // if the current item needs monitoring in CrawlPointer, add that logic here
                    //const long k_FieldIndex = 0;
                    //if(something)
                    //  IndexOfFoundElement = m_ResultingCrawlDataStack.Count + k_FieldIndex;
#endif
                    var type = new SourceIndex(SourceIndex.SourceId.ManagedType, typeIndex);
                    CrawlRawObjectData(m_ManagedHeapBytes, in m_VMInfo, m_MangedObjectIndexByAddress,
                        in m_NativeObjectOrAllocationFinder,
                        ref m_ResultingCrawlDataStack, in fieldLayouts, StaticTypeData[index].Bytes, in type, m_MaxObjectIndexFindableViaPointers);

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

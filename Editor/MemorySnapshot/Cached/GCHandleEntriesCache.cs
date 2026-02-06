using System;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using Unity.MemoryProfiler.Editor.Managed;
namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
        public class GCHandleEntriesCache : IDisposable
        {
            public DynamicArray<ulong> Target = default;
            /// <summary>
            /// Use this Count when iterating over all entries in <see cref="Target"/>.
            /// When checking if a Managed Object index is low enough to belong to a Managed Shell for a Native Object that
            /// reported it via its GCHandle, use <see cref="UniqueCount"/> instead.
            /// </summary>
            public long Count;
            /// <summary>
            /// Pre 19.3 scripting snapshot implementations could report dublicated GCHandles.
            /// after <see cref="CachedSnapshot.PostProcess"/> is finished, or more precisely after <seealso cref="ManagedDataCrawler.GatherIntermediateCrawlData(CachedSnapshot, ManagedDataCrawler.IntermediateCrawlData, ref DynamicArray{ManagedDataCrawler.StackCrawlData})"/>
            /// ran, this value is going to be accurate. Before that it is just a copy of <see cref="Count"/>.
            /// </summary>
            public long UniqueCount;

            public GCHandleEntriesCache(ref IFileReader reader)
            {
                unsafe
                {
                    UniqueCount = Count = reader.GetEntryCount(EntryType.GCHandles_Target);

                    if (Count == 0)
                        return;

                    Target = reader.Read(EntryType.GCHandles_Target, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                }
            }

            public void Dispose()
            {
                Count = 0;
                UniqueCount = 0;
                Target.Dispose();
            }
        }
    }
}

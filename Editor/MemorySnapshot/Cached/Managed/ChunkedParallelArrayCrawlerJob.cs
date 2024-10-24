// When defining DEBUG_JOBIFIED_CRAWLER, define for the assembly or also define DEBUG_JOBIFIED_CRAWLER in ManagedDataCrawler.cs, ParallelReferenceArrayCrawlerJobChunk.cs, ParallelStaticFieldsCrawlerJobChunk.cs, ParallelStructArrayCrawlerJobChunk.cs, ParallelReferenceArrayCrawlerJobChunk.cs, ChunkedParallelArrayCrawlerJob.cs and JobifiedCrawlDataStacksPool.cs
//#define DEBUG_JOBIFIED_CRAWLER
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.MemoryProfiler.Editor.Containers;
using Debug = UnityEngine.Debug;

namespace Unity.MemoryProfiler.Editor.Managed
{
    static partial class ManagedDataCrawler
    {
        const bool k_DebugBurstJobs =
#if DEBUG_JOBIFIED_CRAWLER
            true;
#else
            false;
#endif
        const bool k_DisableBurstDebugChecks = !k_DebugBurstJobs;

        interface IChunkedCrawlerJobManagerBase
        {
            ref DynamicArray<StackCrawlData> CrawlDataStack { get; }
        }

        interface IChunkedParallelCrawlerJob
        {
#if DEBUG_JOBIFIED_CRAWLER
            public long IndexOfFoundElement { get; }
#endif
            void Finish(IChunkedCrawlerJobManagerBase crawlData);
        }

        interface ICrawlerJobChunk<T> : IDisposable where T : unmanaged
        {
            public DynamicArray<StackCrawlData> ResultingCrawlDataStack { get; }
#if DEBUG_JOBIFIED_CRAWLER
            public long IndexOfFoundElement { get; }
#endif
            void Process();
        }

        interface IChunkedJobInitializer<TJobChunk>
            where TJobChunk : unmanaged, ICrawlerJobChunk<TJobChunk>
        {
            public void Init(ref TJobChunk job);
        }

        [BurstCompile(CompileSynchronously = true, DisableDirectCall = false, DisableSafetyChecks = k_DisableBurstDebugChecks, Debug = k_DebugBurstJobs)]
        unsafe struct ChunkedParallelArrayCrawlerJob<TJobChunk> : IChunkedParallelCrawlerJob, IJobParallelFor
            where TJobChunk : unmanaged, ICrawlerJobChunk<TJobChunk>
        {
            [ReadOnly, NativeDisableUnsafePtrRestriction]
            public DynamicArray<TJobChunk>* Chunks;

#if DEBUG_JOBIFIED_CRAWLER
            public long IndexOfFoundElement { get; private set; }
#endif
            public int ChunkCount;

            public ChunkedParallelArrayCrawlerJob(ref DynamicArray<TJobChunk> jobs)
            {
                unsafe
                {
                    Chunks = (DynamicArray<TJobChunk>*)UnsafeUtility.AddressOf(ref jobs);
                }
                Debug.Assert(jobs.Count < int.MaxValue, "Can't schedule more jobs than int.MaxValue");
                ChunkCount = (int)jobs.Count;
#if DEBUG_JOBIFIED_CRAWLER
                IndexOfFoundElement = -1;
#endif
            }

            public void Finish(IChunkedCrawlerJobManagerBase crawlData)
            {
                // Combine the data from the parallel jobs!
                for (var i = 0; i < ChunkCount; i++)
                {
                    if ((*Chunks)[i].ResultingCrawlDataStack.IsCreated && (*Chunks)[i].ResultingCrawlDataStack.Count > 0)
                    {
#if DEBUG_JOBIFIED_CRAWLER
                        if ((*Chunks)[i].IndexOfFoundElement >= 0)
                            IndexOfFoundElement = crawlData.CrawlDataStack.Count + (*Chunks)[i].IndexOfFoundElement;
#endif
                        crawlData.CrawlDataStack.PushRange((*Chunks)[i].ResultingCrawlDataStack, memClearForExcessExpansion: k_MemClearCrawlDataStack);
                    }
                }
                // cleanup the jobs
                (*Chunks).Dispose();
            }

            public void Execute(int index)
            {
                ref var chunk = ref (*Chunks)[index];
                chunk.Process();
            }
        }
    }
}

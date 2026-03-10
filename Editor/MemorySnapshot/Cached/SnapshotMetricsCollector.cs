using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Profiling;

namespace Unity.MemoryProfiler.Editor
{
    /// <summary>
    /// Helper for collecting snapshot metrics. Methods are compiled out when MEMORY_PROFILER_METRICS is not defined.
    /// </summary>
    internal struct SnapshotMetricsCollector : IDisposable
    {
        public SnapshotMetrics Metrics;

        Dictionary<SnapshotMetrics.TimingStep, ProfilerRecorder> m_ProfilerRecorders;

        [IgnoredByDeepProfiler, Conditional("MEMORY_PROFILER_METRICS")]
        public void StartTiming()
        {
            m_ProfilerRecorders = new Dictionary<SnapshotMetrics.TimingStep, ProfilerRecorder>();
            StartProfilerMarkerAndRecorder(SnapshotMetrics.TimingStep.ConstructorTotal);
        }

        [IgnoredByDeepProfiler, Conditional("MEMORY_PROFILER_METRICS")]
        public void RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep step, bool doNotStartNext = false)
        {
            if (m_ProfilerRecorders != null)
            {
                var timingInNanoSeconds = EndProfilerMarkerAndGetRecorderData(step);
                Metrics.ConstructorTiming.SetTiming(step, timingInNanoSeconds);

                if (!doNotStartNext)
                    StartProfilerMarkerAndRecorder(step + 1);
            }
        }

        [IgnoredByDeepProfiler, Conditional("MEMORY_PROFILER_METRICS")]
        public void StartFirstConstructorTiming(SnapshotMetrics.TimingStep step)
        {
            if (m_ProfilerRecorders != null)
            {
                StartProfilerMarkerAndRecorder(step);
            }
        }

        [IgnoredByDeepProfiler, Conditional("MEMORY_PROFILER_METRICS")]
        void StartProfilerMarkerAndRecorder(SnapshotMetrics.TimingStep step, int stepCount = 0)
        {
            var marker = SnapshotMetrics.GetProfilerMarkerForTimingStep(step);
            if (marker.Handle.ToInt64() == 0)
                return;
            m_ProfilerRecorders.Add(step, ProfilerRecorder.StartNew(marker, capacity: stepCount + 1));
            marker.Begin();
        }

        [IgnoredByDeepProfiler]
        long EndProfilerMarkerAndGetRecorderData(SnapshotMetrics.TimingStep step, bool wasMultiStepMarker = false)
        {
#if MEMORY_PROFILER_METRICS
            var marker = SnapshotMetrics.GetProfilerMarkerForTimingStep(step);
            if (marker.Handle.ToInt64() == 0)
                return 0;
            marker.End();
            var timingInNanoSeconds = wasMultiStepMarker ? GetSummedUpRecorderData(m_ProfilerRecorders[step]) : m_ProfilerRecorders[step].CurrentValue;
            m_ProfilerRecorders[step].Stop();
            return timingInNanoSeconds;
#else
            return 0;
#endif
        }

        long GetSummedUpRecorderData(ProfilerRecorder recoder)
        {
#if MEMORY_PROFILER_METRICS
            // stop the recorder to get the multi sample results.
            recoder.Stop();
            var samplesCount = recoder.Count;
            if (samplesCount == 0)
                return 0;

            long timingInNanoSeconds = 0;
            unsafe
            {
                var samples = stackalloc ProfilerRecorderSample[samplesCount];
                recoder.CopyTo(samples, samplesCount);
                for (var i = 0; i < samplesCount; ++i)
                    timingInNanoSeconds += samples[i].Value;
            }
            return timingInNanoSeconds;
#else
            return 0;
#endif
        }

        [IgnoredByDeepProfiler, Conditional("MEMORY_PROFILER_METRICS")]
        public void StartPostProcessTiming()
        {
            m_ProfilerRecorders ??= new Dictionary<SnapshotMetrics.TimingStep, ProfilerRecorder>();
            StartProfilerMarkerAndRecorder(SnapshotMetrics.TimingStep.TotalPostProcessTime);
        }

        [IgnoredByDeepProfiler, Conditional("MEMORY_PROFILER_METRICS")]
        public void StartPostProcessStep(SnapshotMetrics.TimingStep step, int stepCount)
        {
            if (m_ProfilerRecorders != null)
            {
                StartProfilerMarkerAndRecorder(step, stepCount);
            }
        }

        /// <summary>
        /// ProfilerMarkers need to be ended before yielding a coroutine or doing any async work, otherwise the timings will be incorrect.
        /// This method allows to pause a specific timing step, and resume it later with <see cref="ResumePostProcessTiming(SnapshotMetrics.TimingStep)"/>, so that we can get accurate timings for post process steps even if they yield or do async work. 
        /// </summary>
        /// <param name="step"></param>
        [IgnoredByDeepProfiler, Conditional("MEMORY_PROFILER_METRICS")]
        public void PausePostProcessTiming(SnapshotMetrics.TimingStep step)
        {
            var marker = SnapshotMetrics.GetProfilerMarkerForTimingStep(step);
            if (marker.Handle.ToInt64() == 0)
                return;
            marker.End();
        }

        [IgnoredByDeepProfiler, Conditional("MEMORY_PROFILER_METRICS")]
        public void ResumePostProcessTiming(SnapshotMetrics.TimingStep step)
        {
            var marker = SnapshotMetrics.GetProfilerMarkerForTimingStep(step);
            if (marker.Handle.ToInt64() == 0)
                return;
            marker.Begin();
        }

        [IgnoredByDeepProfiler, Conditional("MEMORY_PROFILER_METRICS")]
        public void RecordPostProcessTiming(SnapshotMetrics.TimingStep step)
        {
            if (m_ProfilerRecorders != null)
            {
                var timingInNanoSeconds = EndProfilerMarkerAndGetRecorderData(step, wasMultiStepMarker: true);

                Metrics.PostProcessTiming.SetTiming(step, timingInNanoSeconds);
            }
        }

        [Conditional("MEMORY_PROFILER_METRICS")]
        public void CollectSnapshotMetadata(CachedSnapshot snapshot)
        {
            // Basic snapshot info
            Metrics.SnapshotMetadata.SnapshotName = System.IO.Path.GetFileName(snapshot.FullPath);
            Metrics.SnapshotMetadata.CaptureTimestamp = snapshot.TimeStamp;

            var meta = snapshot.MetaData;
            if (meta != null)
            {
                Metrics.SnapshotMetadata.ProductName = meta.ProductName ?? "Unknown";
                Metrics.SnapshotMetadata.UnityVersion = meta.UnityVersion ?? "Unknown";
                Metrics.SnapshotMetadata.Platform = meta.Platform ?? "Unknown";
                Metrics.SnapshotMetadata.IsEditorCapture = meta.IsEditorCapture;
                Metrics.SnapshotMetadata.CaptureFlags = meta.CaptureFlags;

                // Memory stats from target if available
                if (meta.TargetMemoryStats.HasValue)
                {
                    var stats = meta.TargetMemoryStats.Value;
                    Metrics.MemoryTotals.TotalVirtualMemory = stats.TotalVirtualMemory;
                    Metrics.MemoryTotals.TotalUsedMemory = stats.TotalUsedMemory;
                    Metrics.MemoryTotals.TotalReservedMemory = stats.TotalReservedMemory;
                    Metrics.MemoryTotals.TotalGfxMemory = stats.GraphicsUsedMemory;
                    Metrics.MemoryTotals.TotalAudioMemory = stats.AudioUsedMemory;
                    Metrics.MemoryTotals.TotalManagedHeapUsed = stats.GcHeapUsedMemory;
                    Metrics.MemoryTotals.TotalManagedHeapReserved = stats.GcHeapReservedMemory;
                }
            }

            // If we don't have target memory stats, calculate managed heap from sections
            if (Metrics.MemoryTotals.TotalManagedHeapReserved == 0)
            {
                Metrics.MemoryTotals.TotalManagedHeapReserved = snapshot.ManagedHeapSections.ManagedHeapMemoryReserved;
            }
        }

        [Conditional("MEMORY_PROFILER_METRICS")]
        public void CollectEntryCounts(CachedSnapshot snapshot)
        {
            Metrics.RawEntryCounts.NativeObjectCount = snapshot.NativeObjects.Count;
            Metrics.RawEntryCounts.NativeAllocationCount = snapshot.NativeAllocations.Count;
            Metrics.RawEntryCounts.NativeAllocationSiteCount = snapshot.NativeAllocationSites.Count;
            Metrics.RawEntryCounts.NativeCallstackSymbolCount = snapshot.NativeCallstackSymbols.Count;
            Metrics.RawEntryCounts.NativeTypeCount = snapshot.NativeTypes.Count;
            Metrics.RawEntryCounts.NativeRootReferenceCount = snapshot.NativeRootReferences.Count;
            Metrics.RawEntryCounts.NativeMemoryRegionCount = snapshot.NativeMemoryRegions.Count;
            Metrics.RawEntryCounts.NativeGfxResourceCount = snapshot.NativeGfxResourceReferences.Count;
            Metrics.RawEntryCounts.ManagedObjectCount = snapshot.CrawledData.ManagedObjects.Count;
            Metrics.RawEntryCounts.ManagedTypeCount = snapshot.TypeDescriptions.Count;
            Metrics.RawEntryCounts.GCHandleCount = snapshot.GcHandles.Count;
            Metrics.RawEntryCounts.ConnectionCount = snapshot.CrawledData.Connections.Count;
            Metrics.RawEntryCounts.SystemMemoryRegionCount = snapshot.SystemMemoryRegions.Count;
            Metrics.RawEntryCounts.SceneCount = snapshot.SceneRoots.SceneCount;
            Metrics.RawEntryCounts.PrefabCount = snapshot.SceneRoots.PrefabRootCount;
        }

        [Conditional("MEMORY_PROFILER_METRICS")]
        public void CollectProcessedDataCounts(CachedSnapshot snapshot)
        {
            Metrics.ProcessedData.ProcessedRootCount = snapshot.ProcessedNativeRoots.Count;
            Metrics.ProcessedData.UnrootedAllocationCount = snapshot.ProcessedNativeRoots.UnrootedNativeAllocationIndices.Count;
            Metrics.ProcessedData.UnrootedGfxResourceCount = snapshot.ProcessedNativeRoots.UnrootedGraphicsResourceIndices.Count;

            if (snapshot.RootAndImpactInfo.SuccessfullyBuilt)
            {
                Metrics.RootAndImpact.ShortestPathInfoTotalCount = snapshot.RootAndImpactInfo.SourceIndexToRootAndImpactInfo.TotalCount;
                Metrics.RootAndImpact.OwnedChildListCount = snapshot.RootAndImpactInfo.OwnedChildList.Count;
                Metrics.RootAndImpact.UnownedChildListCount = snapshot.RootAndImpactInfo.UnownedChildList.Count;
                if (snapshot.RootAndImpactInfo.Roots.IsCreated)
                {
                    var rootsCount = 0L;
                    for (long i = 0; i < snapshot.RootAndImpactInfo.Roots.SectionCount; i++)
                        rootsCount += snapshot.RootAndImpactInfo.Roots.Count(i);
                    Metrics.RootAndImpact.RootsCount = rootsCount;
                }
            }

            Metrics.MemoryMapEntryCount = snapshot.EntriesMemoryMap.Count;
        }

        [Conditional("MEMORY_PROFILER_METRICS")]
        public void LogMetrics()
        {
            Metrics.LogToConsole();
        }

        public void Dispose()
        {
            if (m_ProfilerRecorders != null)
            {
                foreach (var recorder in m_ProfilerRecorders.Values)
                {
                    recorder.Dispose();
                }
                m_ProfilerRecorders = null;
            }
        }
    }
}

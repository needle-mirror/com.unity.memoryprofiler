using System;
using Unity.Profiling;
using Unity.Profiling.Memory;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Unity.MemoryProfiler.Editor
{
    /// <summary>
    /// Diagnostic metrics collected during snapshot loading and processing.
    /// Enable by defining MEMORY_PROFILER_METRICS.
    /// </summary>
    [Serializable]
    internal struct SnapshotMetrics
    {
        /// <summary>
        /// Snapshot loading and processing timing steps
        /// </summary>
        internal enum TimingStep
        {
            ConstructorTotal,
            NativeAllocationSitesCache,
            FieldDescriptionsCache,
            NativeTypesCache,
            TypeDescriptionsCache,
            NativeRootReferencesCache,
            NativeObjectsCache,
            NativeMemoryRegionsCache,
            NativeMemoryLabelsCache,
            NativeCallstackSymbolsCache,
            NativeAllocationsCache,
            ManagedStacksCache,
            ManagedHeapSectionsCache,
            GcHandlesCache,
            ConnectionsCache,
            SceneRootsCache,
            NativeGfxResourcesCache,
            NativeAllocatorsCache,
            SystemMemoryRegionsCache,
            SortedCaches,
            /// <summary>
            /// This is the step that could accidentally be triggered as the follow up step to <see cref="SortedCaches"/>,
            /// if <see cref="SnapshotMetricsCollector.RecordConstructorTimingAndStartNext(TimingStep, bool)"/> is not called with false.
            /// Having it in this enum is a bit of a spacer for added safety.
            /// </summary>
            TransitionToPostProcess_NoMarker,
            CrawlTime,
            MemoryMapBuildTime,
            ProcessedRootsBuildTime,
            RootAndImpactBuildTime,
            TotalPostProcessTime
        }

#if MEMORY_PROFILER_METRICS
        static readonly string[] s_ProfilerMarkerNames;
        static readonly ProfilerMarker[] s_ProfilerMarkerList;

        static SnapshotMetrics()
        {
            // Initialize marker names array (must match TimingStep enum order)
            s_ProfilerMarkerNames = new string[]
            {
                "CachedSnapshot Constructor", // ConstructorTotal
                "CachedSnapshot.NativeAllocationSitesCache Constructor", // NativeAllocationSitesCache
                "CachedSnapshot.FieldDescriptionsCache Constructor", // FieldDescriptionsCache
                "CachedSnapshot.NativeTypesCache Constructor", // NativeTypesCache
                "CachedSnapshot.TypeDescriptionsCache Constructor", // TypeDescriptionsCache
                "CachedSnapshot.NativeRootReferencesCache Constructor", // NativeRootReferencesCache
                "CachedSnapshot.NativeObjectsCache Constructor", // NativeObjectsCache
                "CachedSnapshot.NativeMemoryRegionsCache Constructor", // NativeMemoryRegionsCache
                "CachedSnapshot.NativeMemoryLabelsCache Constructor", // NativeMemoryLabelsCache
                "CachedSnapshot.NativeCallstackSymbolsCache Constructor", // NativeCallstackSymbolsCache
                "CachedSnapshot.NativeAllocationsCache Constructor", // NativeAllocationsCache
                "CachedSnapshot.ManagedStacksCache Constructor", // ManagedStacksCache
                "CachedSnapshot.ManagedHeapSectionsCache Constructor", // ManagedHeapSectionsCache
                "CachedSnapshot.GcHandlesCache Constructor", // GcHandlesCache
                "CachedSnapshot.ConnectionsCache Constructor", // ConnectionsCache
                "CachedSnapshot.SceneRootsCache Constructor", // SceneRootsCache
                "CachedSnapshot.NativeGfxResourcesCache Constructor", // NativeGfxResourcesCache
                "CachedSnapshot.NativeAllocatorsCache Constructor", // NativeAllocatorsCache
                "CachedSnapshot.SystemMemoryRegionsCache Constructor", // SystemMemoryRegionsCache
                "CachedSnapshot.SortedCaches Constructor", // SortedCaches
                null, // TransitionToPostProcess_NoMarker - no marker for this step
                $"{nameof(Managed.ManagedDataCrawler)}.{nameof(Managed.ManagedDataCrawler.Crawl)}", // CrawlTime
                "CachedSnapshot.PostProcess Build EntriesMemoryMapCache", // MemoryMapBuildTime
                $"{nameof(ProcessedNativeRoots)}.{nameof(ProcessedNativeRoots.ReadOrProcess)}", // ProcessedRootsBuildTime
                $"{nameof(RootAndImpactInfo)}.{nameof(RootAndImpactInfo.ReadOrProcess)}", // RootAndImpactBuildTime
                $"{nameof(CachedSnapshot)}.{nameof(CachedSnapshot.PostProcess)} Total", // TotalPostProcessTime
            };

            // Initialize profiler markers from names
            s_ProfilerMarkerList = new ProfilerMarker[s_ProfilerMarkerNames.Length];
            for (int i = 0; i < s_ProfilerMarkerNames.Length; i++)
            {
                if (s_ProfilerMarkerNames[i] != null)
                {
                    s_ProfilerMarkerList[i] = new ProfilerMarker(s_ProfilerMarkerNames[i]);
                }
            }
        }

#endif
        public static ProfilerMarker GetProfilerMarkerForTimingStep(TimingStep step)
        {
#if MEMORY_PROFILER_METRICS
            var stepIndex = (int)step;
            if (stepIndex >= 0 && stepIndex < s_ProfilerMarkerList.Length)
                return s_ProfilerMarkerList[stepIndex];
#endif
            return default;
        }

        public static string GetProfilerMarkerName(TimingStep step)
        {
#if MEMORY_PROFILER_METRICS
            var stepIndex = (int)step;
            if (stepIndex >= 0 && stepIndex < s_ProfilerMarkerNames.Length)
                return s_ProfilerMarkerNames[stepIndex];
#endif
            return null;
        }
        /// <summary>
        /// Snapshot metadata
        /// </summary>
        [Serializable]
        public struct SnapshotMetadataMetrics
        {
            public string SnapshotName;
            public string ProductName;
            public string UnityVersion;
            public string Platform;
            public bool IsEditorCapture;
            public CaptureFlags CaptureFlags;
            public DateTime CaptureTimestamp;
        }
        public SnapshotMetadataMetrics SnapshotMetadata;

        /// <summary>
        /// Memory totals (bytes)
        /// </summary>
        [Serializable]
        public struct MemoryTotalsMetrics
        {
            public ulong TotalVirtualMemory;
            public ulong TotalUsedMemory;
            public ulong TotalReservedMemory;
            public ulong TotalManagedHeapUsed;
            public ulong TotalManagedHeapReserved;
            public ulong TotalGfxMemory;
            public ulong TotalAudioMemory;
        }
        public MemoryTotalsMetrics MemoryTotals;

        /// <summary>
        /// Raw entry counts
        /// </summary>
        [Serializable]
        public struct RawEntryCountsMetrics
        {
            public long NativeObjectCount;
            public long NativeAllocationCount;
            public long NativeAllocationSiteCount;
            public long NativeCallstackSymbolCount;
            public long NativeTypeCount;
            public long NativeRootReferenceCount;
            public long NativeMemoryRegionCount;
            public long NativeGfxResourceCount;
            public long ManagedObjectCount;
            public long ManagedTypeCount;
            public long GCHandleCount;
            public long ConnectionCount;
            public long SystemMemoryRegionCount;
            public long SceneCount;
            public long PrefabCount;
        }
        public RawEntryCountsMetrics RawEntryCounts;

        /// <summary>
        /// ProcessedNativeRoots metrics
        /// </summary>
        [Serializable]
        public struct ProcessedDataMetrics
        {
            public long ProcessedRootCount;
            public long UnrootedAllocationCount;
            public long UnrootedGfxResourceCount;
        }
        public ProcessedDataMetrics ProcessedData;

        /// <summary>
        /// RootAndImpactInfo metrics
        /// </summary>
        [Serializable]
        public struct RootAndImpactMetrics
        {
            public long ShortestPathInfoTotalCount;
            public long OwnedChildListCount;
            public long UnownedChildListCount;
            public long RootsCount;
        }
        public RootAndImpactMetrics RootAndImpact;

        // EntriesMemoryMap metrics
        public long MemoryMapEntryCount;

        /// <summary>
        /// Constructor timing (milliseconds)
        /// </summary>
        [Serializable]
        public struct ConstructorTimingMetrics
        {
            public long ConstructorTotalNanoSec;
            public long NativeAllocationSitesCacheNanoSec;
            public long FieldDescriptionsCacheNanoSec;
            public long NativeTypesCacheNanoSec;
            public long TypeDescriptionsCacheNanoSec;
            public long NativeRootReferencesCacheNanoSec;
            public long NativeObjectsCacheNanoSec;
            public long NativeMemoryRegionsCacheNanoSec;
            public long NativeMemoryLabelsCacheNanoSec;
            public long NativeCallstackSymbolsCacheNanoSec;
            public long NativeAllocationsCacheNanoSec;
            public long ManagedStacksCacheNanoSec;
            public long ManagedHeapSectionsCacheNanoSec;
            public long GcHandlesCacheNanoSec;
            public long ConnectionsCacheNanoSec;
            public long SceneRootsCacheNanoSec;
            public long NativeGfxResourcesCacheNanoSec;
            public long NativeAllocatorsCacheNanoSec;
            public long SystemMemoryRegionsCacheNanoSec;
            public long SortedCachesNanoSec;

            internal void SetTiming(TimingStep step, long nanoSeconds)
            {
                switch (step)
                {
                    case TimingStep.ConstructorTotal:
                        ConstructorTotalNanoSec = nanoSeconds;
                        break;
                    case TimingStep.NativeAllocationSitesCache:
                        NativeAllocationSitesCacheNanoSec = nanoSeconds;
                        break;
                    case TimingStep.FieldDescriptionsCache:
                        FieldDescriptionsCacheNanoSec = nanoSeconds;
                        break;
                    case TimingStep.NativeTypesCache:
                        NativeTypesCacheNanoSec = nanoSeconds;
                        break;
                    case TimingStep.TypeDescriptionsCache:
                        TypeDescriptionsCacheNanoSec = nanoSeconds;
                        break;
                    case TimingStep.NativeRootReferencesCache:
                        NativeRootReferencesCacheNanoSec = nanoSeconds;
                        break;
                    case TimingStep.NativeObjectsCache:
                        NativeObjectsCacheNanoSec = nanoSeconds;
                        break;
                    case TimingStep.NativeMemoryRegionsCache:
                        NativeMemoryRegionsCacheNanoSec = nanoSeconds;
                        break;
                    case TimingStep.NativeMemoryLabelsCache:
                        NativeMemoryLabelsCacheNanoSec = nanoSeconds;
                        break;
                    case TimingStep.NativeCallstackSymbolsCache:
                        NativeCallstackSymbolsCacheNanoSec = nanoSeconds;
                        break;
                    case TimingStep.NativeAllocationsCache:
                        NativeAllocationsCacheNanoSec = nanoSeconds;
                        break;
                    case TimingStep.ManagedStacksCache:
                        ManagedStacksCacheNanoSec = nanoSeconds;
                        break;
                    case TimingStep.ManagedHeapSectionsCache:
                        ManagedHeapSectionsCacheNanoSec = nanoSeconds;
                        break;
                    case TimingStep.GcHandlesCache:
                        GcHandlesCacheNanoSec = nanoSeconds;
                        break;
                    case TimingStep.ConnectionsCache:
                        ConnectionsCacheNanoSec = nanoSeconds;
                        break;
                    case TimingStep.SceneRootsCache:
                        SceneRootsCacheNanoSec = nanoSeconds;
                        break;
                    case TimingStep.NativeGfxResourcesCache:
                        NativeGfxResourcesCacheNanoSec = nanoSeconds;
                        break;
                    case TimingStep.NativeAllocatorsCache:
                        NativeAllocatorsCacheNanoSec = nanoSeconds;
                        break;
                    case TimingStep.SystemMemoryRegionsCache:
                        SystemMemoryRegionsCacheNanoSec = nanoSeconds;
                        break;
                    case TimingStep.SortedCaches:
                        SortedCachesNanoSec = nanoSeconds;
                        break;
                }
            }
        }
        public ConstructorTimingMetrics ConstructorTiming;

        /// <summary>
        /// PostProcess timing (milliseconds)
        /// </summary>
        [Serializable]
        public struct PostProcessTimingMetrics
        {
            public long CrawlTimeNanoSec;
            public long MemoryMapBuildTimeNanoSec;
            public long ProcessedRootsBuildTimeNanoSec;
            public long RootAndImpactBuildTimeNanoSec;
            public long TotalPostProcessTimeNanoSec;

            internal void SetTiming(TimingStep step, long nanoSeconds)
            {
                switch (step)
                {
                    case TimingStep.CrawlTime:
                        CrawlTimeNanoSec = nanoSeconds;
                        break;
                    case TimingStep.MemoryMapBuildTime:
                        MemoryMapBuildTimeNanoSec = nanoSeconds;
                        break;
                    case TimingStep.ProcessedRootsBuildTime:
                        ProcessedRootsBuildTimeNanoSec = nanoSeconds;
                        break;
                    case TimingStep.RootAndImpactBuildTime:
                        RootAndImpactBuildTimeNanoSec = nanoSeconds;
                        break;
                    case TimingStep.TotalPostProcessTime:
                        TotalPostProcessTimeNanoSec = nanoSeconds;
                        break;
                }
            }
        }
        public PostProcessTimingMetrics PostProcessTiming;

        public readonly void LogToConsole()
        {
            Debug.Log($"=== Snapshot Metrics ===\n" + ToJsonString());
        }

        public readonly string ToJsonString()
        {
            return JsonUtility.ToJson(this, prettyPrint: true);
        }
    }
}

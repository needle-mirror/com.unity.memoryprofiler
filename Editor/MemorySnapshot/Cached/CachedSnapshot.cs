using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using Unity.MemoryProfiler.Editor.EnumerationUtilities;
using Unity.MemoryProfiler.Editor.Managed;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.Profiling;

using Debug = UnityEngine.Debug;
using UnityException = UnityEngine.UnityException;



#if !ENTITY_ID_CHANGED_SIZE
// the official EntityId lives in the UnityEngine namespace, which might be be added as a using via the IDE,
// so to avoid mistakenly using a version of this struct with the wrong size, alias it here.
using EntityId = Unity.MemoryProfiler.Editor.EntityId;
#else
// This should be greyed out by the IDE, otherwise you're missing an alias above
using UnityEngine;
using EntityId = UnityEngine.EntityId;
#endif

namespace Unity.MemoryProfiler.Editor
{
    [BurstCompile]
    internal partial class CachedSnapshot : IDisposable
    {
        public const string InvalidItemName = "<No Name>";
        public const string UnrootedItemName = "Unrooted";
        public const string UnknownMemlabelName = "Unknown MemLabel";
        public const string RootName = "Root";

        /// <summary>
        /// Metrics collected during the last snapshot load (<see cref="CachedSnapshot.CachedSnapshot(IFileReader)"/>)
        /// and <see cref="PostProcess"/> call.
        /// Only populated when MEMORY_PROFILER_METRICS is defined.
        /// </summary>
        public SnapshotMetrics LastMetrics { get; private set; }

        SnapshotMetricsCollector m_MetricsCollector;


#if ENTITY_ID_STRUCT_AVAILABLE && !ENTITY_ID_CHANGED_SIZE
        static CachedSnapshot()
        {
            Checks.IsTrue((typeof(EntityId) != typeof(UnityEngine.EntityId)), "The wrong type of EntityId struct is used, probably due to accidentally adding a 'using UnityEngine;' to this file.");
        }
#endif

        public bool Valid { get { return !m_Disposed && CrawledData.Crawled; } }
        bool m_Disposed = false;

        FormatVersion m_SnapshotVersion;

        public bool HasConnectionOverhaul
        {
            get { return m_SnapshotVersion >= FormatVersion.NativeConnectionsAsInstanceIdsVersion; }
        }

        public bool HasTargetAndMemoryInfo
        {
            get { return m_SnapshotVersion >= FormatVersion.ProfileTargetInfoAndMemStatsVersion; }
        }

        public bool HasMemoryLabelSizesAndGCHeapTypes
        {
            get { return m_SnapshotVersion >= FormatVersion.MemLabelSizeAndHeapIdVersion; }
        }

        public bool HasSceneRootsAndAssetbundles
        {
            get { return m_SnapshotVersion >= FormatVersion.SceneRootsAndAssetBundlesVersion; }
        }

        public bool HasGfxResourceReferencesAndAllocators
        {
            get { return m_SnapshotVersion >= FormatVersion.GfxResourceReferencesAndAllocatorsVersion; }
        }

        public bool HasNativeObjectMetaData
        {
            get { return m_SnapshotVersion >= FormatVersion.NativeObjectMetaDataVersion; }
        }

        public bool HasSystemMemoryRegionsInfo
        {
            get { return (m_SnapshotVersion >= FormatVersion.SystemMemoryRegionsVersion) && (SystemMemoryRegions.Count > 0); }
        }

        public bool HasSystemMemoryResidentPages
        {
            get { return (m_SnapshotVersion >= FormatVersion.SystemMemoryResidentPagesVersion) && (SystemMemoryResidentPages.Count > 0); }
        }

        public bool HasEntityIDAs8ByteStructs
        {
            get { return (m_SnapshotVersion >= FormatVersion.EntityIDAs8ByteStructs); }
        }

        /// <summary>
        /// True when the file carries a non-empty backend-provided managed object graph (format v19+,
        /// populated by CoreCLR; false on Mono / IL2CPP and pre-v19 files). When true, the crawler
        /// prepopulates its object set from <see cref="ManagedObjects"/> instead of discovering objects
        /// byte-by-byte, and, when heap bytes are also absent, builds the reference graph from
        /// <see cref="ManagedReferences"/> instead of walking field bytes. Only valid once
        /// <see cref="ManagedObjects"/> is constructed. <see cref="ManagedRoots"/> is parsed but not yet
        /// consumed by the crawler.
        /// </summary>
        public bool HasBackendManagedGraph
        {
            get { return m_SnapshotVersion >= FormatVersion.ManagedConnectionsVersion && ManagedObjects != null && ManagedObjects.Count > 0; }
        }

        /// <summary>
        /// True when the file carries resolved GC-static field slots, per-field attribute flags, and
        /// heap-section chunk-type flags (format v20+). Populated by all backends; only CoreCLR resolves
        /// thread-static slots and the static-flavor bits (AppDomain/Thread/Context/RVA) — Mono / IL2CPP
        /// populate ordinary-static slots and the attribute bits (InitOnly/Literal/Visibility/SpecialName)
        /// but leave the static-flavor bits at 0.
        /// </summary>
        public bool HasStaticFieldSlotsAndFlags
        {
            get { return m_SnapshotVersion >= FormatVersion.StaticFieldAndNonGCHeapVersion; }
        }

        bool m_HasPrefabRootInfo;
        public bool HasPrefabRootInfo => m_HasPrefabRootInfo;

        public ManagedData CrawledData { internal set; get; }

        IFileReader m_Reader;
        public readonly MetaData MetaData;
        public DateTime TimeStamp { get; private set; }
        public ref readonly VirtualMachineInformation VirtualMachineInformation => ref m_VirtualMachineInformation;
        readonly VirtualMachineInformation m_VirtualMachineInformation;
        public NativeAllocationSiteEntriesCache NativeAllocationSites;
        public TypeDescriptionEntriesCache TypeDescriptions;
        public NativeTypeEntriesCache NativeTypes;
        public NativeRootReferenceEntriesCache NativeRootReferences;
        public NativeObjectEntriesCache NativeObjects;
        public NativeMemoryRegionEntriesCache NativeMemoryRegions;
        public NativeMemoryLabelEntriesCache NativeMemoryLabels;
        public NativeCallstackSymbolEntriesCache NativeCallstackSymbols;
        public NativeAllocationEntriesCache NativeAllocations;
        public ManagedMemorySectionEntriesCache ManagedStacks;
        public ManagedMemorySectionEntriesCache ManagedHeapSections;
        public OnDemandHeapSectionCache OnDemandHeapCache;

        public GCHandleEntriesCache GcHandles;
        public FieldDescriptionEntriesCache FieldDescriptions;
        public ConnectionEntriesCache Connections;

        /// <summary>Every managed object from the backend-provided graph (format v19+). Empty on Mono / IL2CPP and on older files; populated by CoreCLR. See <see cref="HasBackendManagedGraph"/>.</summary>
        public ManagedObjectsEntriesCache ManagedObjects;
        /// <summary>Object→object edges from the backend-provided graph (format v19+). Empty on Mono / IL2CPP and on older files; populated by CoreCLR. See <see cref="HasBackendManagedGraph"/>.</summary>
        public ManagedReferencesEntriesCache ManagedReferences;
        /// <summary>Roots from the backend-provided graph (format v19+). Empty on Mono / IL2CPP and on older files; populated by CoreCLR. See <see cref="HasBackendManagedGraph"/>.</summary>
        public ManagedRootsEntriesCache ManagedRoots;

        public SortedNativeMemoryRegionEntriesCache SortedNativeRegionsEntries;
        public SortedSystemMemoryRegionEntriesCache SortedSystemMemoryRegions;
        public SortedManagedObjectsCache SortedManagedObjects;
        public SortedNativeAllocationsCache SortedNativeAllocations;
        public SortedNativeObjectsCache SortedNativeObjects;

        public SceneRootEntriesCache SceneRoots;
        public NativeAllocatorEntriesCache NativeAllocators;
        public NativeGfxResourceReferenceEntriesCache NativeGfxResourceReferences;

        public SystemMemoryRegionEntriesCache SystemMemoryRegions;
        public SystemMemoryResidentPagesEntriesCache SystemMemoryResidentPages;
        public EntriesMemoryMapCache EntriesMemoryMap;
        public ProcessedNativeRoots ProcessedNativeRoots;
        public RootAndImpactInfo RootAndImpactInfo;
        public ObjectStatusComputation ObjectStatusComputation;

        /// <summary>
        /// Cache of table entry names that require heap bytes access.
        /// This includes string previews and native allocation names.
        /// Populated during crawling and persists after heap bytes are unloaded.
        /// </summary>
        public TableEntryNameCache TableEntryNames { get; private set; }

        public CachedSnapshot(IFileReader reader, bool loadFully = true)
        {
            m_MetricsCollector = new SnapshotMetricsCollector();
            m_MetricsCollector.StartTiming();
            m_Reader = reader;
            unsafe
            {
                VirtualMachineInformationFileData vmInfoRaw;
                reader.ReadUnsafe(EntryType.Metadata_VirtualMachineInformation, &vmInfoRaw, sizeof(VirtualMachineInformationFileData), 0, 1);

                if (!VMTools.ValidateVirtualMachineInfo(vmInfoRaw))
                {
                    throw new UnityException("Invalid VM info. Snapshot file is corrupted.");
                }

                long ticks;
                reader.ReadUnsafe(EntryType.Metadata_RecordDate, &ticks, sizeof(long), 0, 1);
                TimeStamp = new DateTime(ticks);

                MetaData = new MetaData(reader);

                m_VirtualMachineInformation = new VirtualMachineInformation(vmInfoRaw, MetaData.TargetInfo?.ScriptingBackend);
            }
            if (loadFully)
                Load();
        }

        public CachedSnapshot Load()
        {
            var reader = m_Reader;
            if (m_SnapshotVersion != (FormatVersion)0)
                return this; // don't load twice.
            unsafe
            {
                m_SnapshotVersion = reader.FormatVersion;

                SetUnityVersionSpecificFlags();

                m_MetricsCollector.StartFirstConstructorTiming(SnapshotMetrics.TimingStep.NativeAllocationSitesCache);
                NativeAllocationSites = new NativeAllocationSiteEntriesCache(ref reader);
#if MEMORY_PROFILER_METRICS
                // Sync async reads to get accurate timing and prevent concurrent file reads
                NativeAllocationSites.CompleteAsyncReadOperations();
#endif
                m_MetricsCollector.RecordLoadingPhase($"NativeAllocationSites done (Count={NativeAllocationSites.Count})");
                m_MetricsCollector.RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep.NativeAllocationSitesCache);

                FieldDescriptions = new FieldDescriptionEntriesCache(ref reader);
                m_MetricsCollector.RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep.FieldDescriptionsCache);

                NativeTypes = new NativeTypeEntriesCache(ref reader);
                m_MetricsCollector.RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep.NativeTypesCache);

                TypeDescriptions = new TypeDescriptionEntriesCache(ref reader, FieldDescriptions, NativeTypes, m_VirtualMachineInformation);
#if MEMORY_PROFILER_METRICS
                // Sync async reads to get accurate timing for this cache
                TypeDescriptions.CompleteAsyncReadOperations();
#endif
                m_MetricsCollector.RecordLoadingPhase($"TypeDescriptions done (Count={TypeDescriptions.Count})");
                m_MetricsCollector.RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep.TypeDescriptionsCache);

                NativeRootReferences = new NativeRootReferenceEntriesCache(ref reader);
                m_MetricsCollector.RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep.NativeRootReferencesCache);

                NativeAllocations = new NativeAllocationEntriesCache(ref reader, NativeAllocationSites.Count != 0);
                m_MetricsCollector.RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep.NativeAllocationsCache);

                NativeObjects = new NativeObjectEntriesCache(ref reader, NativeTypes.AssetBundleIdx, NativeAllocations);
#if MEMORY_PROFILER_METRICS
                // Sync async reads to get accurate timing for this cache
                NativeObjects.CompleteAsyncReadOperations();
#endif
                m_MetricsCollector.RecordLoadingPhase($"NativeObjects done (Count={NativeObjects.Count})");
                m_MetricsCollector.RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep.NativeObjectsCache);

                NativeMemoryRegions = new NativeMemoryRegionEntriesCache(ref reader);
                m_MetricsCollector.RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep.NativeMemoryRegionsCache);

                NativeMemoryLabels = new NativeMemoryLabelEntriesCache(ref reader, HasMemoryLabelSizesAndGCHeapTypes);
                m_MetricsCollector.RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep.NativeMemoryLabelsCache);

                NativeCallstackSymbols = new NativeCallstackSymbolEntriesCache(ref reader);
                m_MetricsCollector.RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep.NativeCallstackSymbolsCache);


                m_MetricsCollector.RecordLoadingPhase("ManagedStacks: starting constructor");
                ManagedStacks = new ManagedMemorySectionEntriesCache(ref reader, false, true, ref m_MetricsCollector);
                m_MetricsCollector.RecordManagedSectionDiagnostics(isHeap: false, ManagedStacks.Count, (long)ManagedStacks.StackMemoryReserved);
                m_MetricsCollector.RecordLoadingPhase($"ManagedStacks: constructor done (Count={ManagedStacks.Count}, Bytes={ManagedStacks.StackMemoryReserved})");
#if MEMORY_PROFILER_METRICS
                // Sync async reads to get accurate timing for this cache
                m_MetricsCollector.RecordLoadingPhase("ManagedStacks: starting CompleteHeapBytesRead");
                ManagedStacks.CompleteHeapBytesRead();
                m_MetricsCollector.RecordLoadingPhase("ManagedStacks: CompleteHeapBytesRead done");
#endif
                m_MetricsCollector.RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep.ManagedStacksCache);

                m_MetricsCollector.RecordLoadingPhase("ManagedHeapSections: starting constructor");
                ManagedHeapSections = new ManagedMemorySectionEntriesCache(ref reader, HasMemoryLabelSizesAndGCHeapTypes, false, ref m_MetricsCollector);
                m_MetricsCollector.RecordManagedSectionDiagnostics(isHeap: true, ManagedHeapSections.Count,
                    (long)(ManagedHeapSections.ManagedHeapMemoryReserved + ManagedHeapSections.VirtualMachineMemoryReserved));
                m_MetricsCollector.RecordLoadingPhase($"ManagedHeapSections: constructor done (Count={ManagedHeapSections.Count}, HeapBytes={ManagedHeapSections.ManagedHeapMemoryReserved}, VMBytes={ManagedHeapSections.VirtualMachineMemoryReserved})");
#if MEMORY_PROFILER_METRICS
                // Sync async reads to get accurate timing for this cache
                m_MetricsCollector.RecordLoadingPhase("ManagedHeapSections: starting CompleteHeapBytesRead");
                ManagedHeapSections.CompleteHeapBytesRead();
                m_MetricsCollector.RecordLoadingPhase("ManagedHeapSections: CompleteHeapBytesRead done");
#endif
                m_MetricsCollector.RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep.ManagedHeapSectionsCache);

                GcHandles = new GCHandleEntriesCache(ref reader);
                m_MetricsCollector.RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep.GcHandlesCache);

                Connections = new ConnectionEntriesCache(ref reader, NativeObjects, GcHandles.Count);
                m_MetricsCollector.RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep.ConnectionsCache);

                // Backend-provided managed object graph (format v19+). Empty on Mono / IL2CPP and older files.
                // Constructed before CrawledData so HasBackendManagedGraph can steer the crawl.
                ManagedObjects = new ManagedObjectsEntriesCache(ref reader);
                ManagedReferences = new ManagedReferencesEntriesCache(ref reader);
                ManagedRoots = new ManagedRootsEntriesCache(ref reader);

                SceneRoots = new SceneRootEntriesCache(ref reader);
                // TODO: Jobifiy GenerateBaseData and sync at end
                SceneRoots.GenerateBaseData(this, NativeObjects.InstanceId2Index, NativeTypes.GameObjectIdx);
                m_MetricsCollector.RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep.SceneRootsCache);

                NativeGfxResourceReferences = new NativeGfxResourceReferenceEntriesCache(ref reader);
                m_MetricsCollector.RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep.NativeGfxResourcesCache);

                NativeAllocators = new NativeAllocatorEntriesCache(ref reader);
                m_MetricsCollector.RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep.NativeAllocatorsCache);

                SystemMemoryRegions = new SystemMemoryRegionEntriesCache(ref reader);
                SystemMemoryResidentPages = new SystemMemoryResidentPagesEntriesCache(ref reader, SystemMemoryRegions);
                m_MetricsCollector.RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep.SystemMemoryRegionsCache);

                SortedManagedObjects = new SortedManagedObjectsCache(this);
                SortedNativeRegionsEntries = new SortedNativeMemoryRegionEntriesCache(this);
                SortedSystemMemoryRegions = new SortedSystemMemoryRegionEntriesCache(this);
                SortedNativeAllocations = new SortedNativeAllocationsCache(this);
                SortedNativeObjects = new SortedNativeObjectsCache(this);
                m_MetricsCollector.RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep.SortedCaches, doNotStartNext: true);

                EntriesMemoryMap = new EntriesMemoryMapCache(this);

                CrawledData = new ManagedData(GcHandles.Count, Connections.Count, NativeTypes.Count);

                ProcessedNativeRoots = new ProcessedNativeRoots();

                // Initialize string preview cache for table display (survives heap bytes unload)
                TableEntryNames = new TableEntryNameCache();
            }

            m_MetricsCollector.RecordConstructorTimingAndStartNext(SnapshotMetrics.TimingStep.ConstructorTotal, doNotStartNext: true);
            return this;
        }

        void SetUnityVersionSpecificFlags()
        {
            m_HasPrefabRootInfo = UnityVersionHasPrefabRootInfo();
        }

        bool UnityVersionHasPrefabRootInfo()
        {
            return MetaData.UnityVersionEqualOrNewer(6000, 5, 0, MetaData.UnityReleaseType.Alpha, 3)
                   || MetaData.UnityVersionEqualOrNewerWithinMinorRelease(6000, 4, 0, MetaData.UnityReleaseType.Alpha, 6)
                   || MetaData.UnityVersionEqualOrNewerWithinMinorRelease(6000, 3, 1, MetaData.UnityReleaseType.Full, 1)
                   || MetaData.UnityVersionEqualOrNewerWithinMinorRelease(6000, 2, 15, MetaData.UnityReleaseType.Full, 1)
                   // skip 6000.1 as it did not get the prefab fix.
                   || MetaData.UnityVersionEqualOrNewerWithinMinorRelease(6000, 0, 64, MetaData.UnityReleaseType.Full, 1);
        }

        public const string CacheNativeAllocationNamesMarkerName = "CachedSnapshot.CacheNativeAllocationNames";
        static readonly ProfilerMarker k_CacheNativeAllocationNamesMarker = new ProfilerMarker(CacheNativeAllocationNamesMarkerName);
        /// <summary>
        /// Cache native allocation names that have managed references.
        /// Must be called after crawling is complete and before heap bytes are unloaded.
        /// </summary>
        void CacheNativeAllocationNames()
        {
            if (TableEntryNames == null)
                return;
            using var _ = k_CacheNativeAllocationNamesMarker.Auto();

            // Iterate over all native allocations that have managed references
            for (long i = 0; i < NativeAllocations.Count; i++)
            {
                var nativeAllocationIndex = new SourceIndex(SourceIndex.SourceId.NativeAllocation, i);
                if (CrawledData.ConnectionsToMappedToSourceIndex.TryGetValue(nativeAllocationIndex, out var references))
                {
                    // Generate and cache the name while heap bytes are still available
                    var name = NativeAllocationTools.ProduceNativeAllocationNameInternal(nativeAllocationIndex, this, ref references, MemoryProfilerSettings.MemorySnapshotTruncateTypes);
                    TableEntryNames.CacheNativeAllocationName(nativeAllocationIndex.Index, name);
                }
            }
        }

        public int PostProcessStepCountWithCrawler =>
            ManagedDataCrawler.CrawlStepCount
            + PostProcessStepCountWithoutCrawler
            + 1; // heap unloading step (includes caching table entry names)

        public int PostProcessStepCountWithoutCrawler =>
            +EntriesMemoryMapCache.BuildStepCount
            + ProcessedNativeRoots.ProcessStepCount
            + RootAndImpactInfo.ProcessStepCount
            + ObjectStatusComputation.ProcessStepCount
            + 1; //final step

        public IEnumerator<EnumerationStatus> PostProcess(bool crawlManaged = true)
        {
            if (RootAndImpactInfo != null || ObjectStatusComputation != null)
                throw new InvalidOperationException("PostProcess has already been called.");

            var status = new EnumerationStatus(crawlManaged ? PostProcessStepCountWithCrawler : PostProcessStepCountWithoutCrawler);
            m_MetricsCollector.StartPostProcessTiming();

            IEnumerator<EnumerationStatus> processor = null;
            // Managed Objects end up on the Memory Map, so crawl them first
            if (crawlManaged)
            {
                m_MetricsCollector.StartPostProcessStep(SnapshotMetrics.TimingStep.CrawlTime, ManagedDataCrawler.CrawlStepCount);
                processor = ManagedDataCrawler.Crawl(this, status);
                while (processor.MoveNext())
                {
                    m_MetricsCollector.PausePostProcessTiming(SnapshotMetrics.TimingStep.CrawlTime);
                    yield return processor.Current;
                    m_MetricsCollector.ResumePostProcessTiming(SnapshotMetrics.TimingStep.CrawlTime);
                }
                // Cache table entry names. As they are informed by the crawler results, include them in the crawl time metric.
                m_MetricsCollector.PausePostProcessTiming(SnapshotMetrics.TimingStep.CrawlTime);
                yield return status.IncrementStep(stepStatus: "Caching table entry names and releasing heap memory");
                m_MetricsCollector.ResumePostProcessTiming(SnapshotMetrics.TimingStep.CrawlTime);

                // Cache native allocation names before unloading heap bytes
                // This must happen after crawling so that ConnectionsToMappedToSourceIndex is populated
                CacheNativeAllocationNames();

                ManagedHeapSections.UnloadHeapBytes();
                ManagedStacks.UnloadHeapBytes();
                m_MetricsCollector.RecordPostProcessTiming(SnapshotMetrics.TimingStep.CrawlTime);
            }
            else
            {
                // Unload heap bytes for managed objects.
                // Technically we would have not needed to load them in in the first place, but the Constructor does not
                // know if the PostProcess will be called with crawlManaged = true or false
                ManagedHeapSections.UnloadHeapBytes();
                ManagedStacks.UnloadHeapBytes();
            }
            InitializeOnDemandHeapCache();

            // these need the managed object count
            RootAndImpactInfo = new RootAndImpactInfo(this);
            ObjectStatusComputation = new ObjectStatusComputation();

            // To populate ProcessedNativeRoots with reliable native sizes for all allocations, as well as Resident vs Committed amounts for all Native Roots,the Memory Map needs to be built
            m_MetricsCollector.StartPostProcessStep(SnapshotMetrics.TimingStep.MemoryMapBuildTime, EntriesMemoryMapCache.BuildStepCount);
            processor = EntriesMemoryMap.Build(status);
            while (processor.MoveNext())
            {
                m_MetricsCollector.PausePostProcessTiming(SnapshotMetrics.TimingStep.MemoryMapBuildTime);
                yield return processor.Current;
                m_MetricsCollector.ResumePostProcessTiming(SnapshotMetrics.TimingStep.MemoryMapBuildTime);
            }
            m_MetricsCollector.RecordPostProcessTiming(SnapshotMetrics.TimingStep.MemoryMapBuildTime);

            // The Unity Objects Summary table will need ready to go ProcessedNativeRoots to show Unity Objects with their rooted native, gfx and managed memory
            m_MetricsCollector.StartPostProcessStep(SnapshotMetrics.TimingStep.ProcessedRootsBuildTime, ProcessedNativeRoots.ProcessStepCount);
            processor = ProcessedNativeRoots.ReadOrProcess(this, status);
            while (processor.MoveNext())
            {
                m_MetricsCollector.PausePostProcessTiming(SnapshotMetrics.TimingStep.ProcessedRootsBuildTime);
                yield return processor.Current;
                m_MetricsCollector.ResumePostProcessTiming(SnapshotMetrics.TimingStep.ProcessedRootsBuildTime);
            }
            m_MetricsCollector.RecordPostProcessTiming(SnapshotMetrics.TimingStep.ProcessedRootsBuildTime);

            // The Impact columns need Path To Root info, though this could be moved to the background?
            m_MetricsCollector.StartPostProcessStep(SnapshotMetrics.TimingStep.RootAndImpactBuildTime, RootAndImpactInfo.ProcessStepCount);
            processor = RootAndImpactInfo.ReadOrProcess(this, status);
            while (processor.MoveNext())
            {
                m_MetricsCollector.PausePostProcessTiming(SnapshotMetrics.TimingStep.RootAndImpactBuildTime);
                yield return processor.Current;
                m_MetricsCollector.ResumePostProcessTiming(SnapshotMetrics.TimingStep.RootAndImpactBuildTime);
            }
            m_MetricsCollector.RecordPostProcessTiming(SnapshotMetrics.TimingStep.RootAndImpactBuildTime);

            // Object status classification — uses RootAndImpactInfo when available, falls back to flag-based checks
            processor = ObjectStatusComputation.ReadOrProcess(this, status);
            while (processor.MoveNext())
            {
                yield return processor.Current;
            }

            // Finalize metrics collection
            m_MetricsCollector.RecordPostProcessTiming(SnapshotMetrics.TimingStep.TotalPostProcessTime);
            m_MetricsCollector.CollectSnapshotMetadata(this);
            m_MetricsCollector.CollectEntryCounts(this);
            m_MetricsCollector.CollectProcessedDataCounts(this);
            LastMetrics = m_MetricsCollector.Metrics;
            m_MetricsCollector.LogMetrics();
        }

        /// <summary>
        /// Logs whatever metrics have been collected so far, including steps that are still in progress.
        /// Useful for diagnosing where <see cref="PostProcess"/> hung: completed steps show their timing,
        /// the step that is currently running (or stuck) shows 0, and later steps are absent entirely.
        /// No-op when MEMORY_PROFILER_METRICS is not defined.
        /// </summary>
        internal void LogCurrentMetrics(bool asError = false) => m_MetricsCollector.LogMetrics(asError);

        public string FullPath
        {
            get
            {
                return m_Reader.FullPath;
            }
        }

        public enum NativeAllocationOrRegionSearchResult
        {
            NullOrUninitialized,
            Invalid,
            NotInTrackedMemory,
            FoundAllocation,
            FoundRegion,
            NothingFound
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public NativeAllocationOrRegionSearchResult FindNativeAllocationOrRegion(ulong pointer,
            out SourceIndex nativeRegionIndex, out SourceIndex nativeAllocationIndex)
        {
            return FindNativeAllocationOrRegion(pointer, out nativeRegionIndex, out nativeAllocationIndex,
                 out _, false);
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public NativeAllocationOrRegionSearchResult FindNativeAllocationOrRegion(ulong pointer,
            out SourceIndex nativeRegionIndex, out SourceIndex nativeAllocationIndex,
            out string nativeRegionPath)
        {
            return FindNativeAllocationOrRegion(pointer, out nativeRegionIndex, out nativeAllocationIndex,
                out nativeRegionPath, true);
        }

        NativeAllocationOrRegionSearchResult FindNativeAllocationOrRegion(ulong pointer,
            out SourceIndex nativeRegionIndex, out SourceIndex nativeAllocationIndex, out string nativeRegionPath,
            bool buildFullNativePath = false)
        {
            nativeRegionIndex = nativeAllocationIndex = default;
            nativeRegionPath = null;
            if (pointer == 0)
            {
                return NativeAllocationOrRegionSearchResult.NullOrUninitialized;
            }
            else if (pointer == ulong.MaxValue)
            {
                return NativeAllocationOrRegionSearchResult.Invalid;
            }
            var nativeRegionIndexInSortedRegions = SortedNativeRegionsEntries.Find(pointer, onlyDirectAddressMatches: false);
            if (nativeRegionIndexInSortedRegions >= 0)
            {
                nativeRegionIndex = new SourceIndex(SourceIndex.SourceId.NativeMemoryRegion, SortedNativeRegionsEntries[nativeRegionIndexInSortedRegions]);
                nativeRegionPath = SortedNativeRegionsEntries.Name(nativeRegionIndexInSortedRegions);

                if (buildFullNativePath)
                {
                    var foundRegionInLayer = SortedNativeRegionsEntries.RegionHierarchLayer[nativeRegionIndexInSortedRegions];
                    if (foundRegionInLayer > 0)
                    {
                        // search backwards for parent regions.
                        for (var iRegion = nativeRegionIndexInSortedRegions - 1; iRegion >= 0; iRegion--)
                        {
                            if (SortedNativeRegionsEntries.RegionHierarchLayer[iRegion] >= foundRegionInLayer
                                || SortedNativeRegionsEntries.Address(iRegion) + SortedNativeRegionsEntries.FullSize(iRegion) < pointer)
                                continue;
                            if (SortedNativeRegionsEntries.Address(iRegion) <= pointer)
                            {

                                nativeRegionPath += $"{SortedNativeRegionsEntries.Name(iRegion)} / {nativeRegionPath}";
                                foundRegionInLayer = SortedNativeRegionsEntries.RegionHierarchLayer[nativeRegionIndexInSortedRegions];
                                // found a parent region, continue searching if it is not on layer 0 though as there should be another parent-region enclosing this one
                                if (foundRegionInLayer == 0)
                                    break;
                            }
                        }
                    }
                }

                long idx = DynamicArrayAlgorithms.BinarySearch(SortedNativeAllocations.UnsafeCache, pointer);
                // -1 means the address is smaller than the first starting Address,
                if (idx != -1)
                {
                    if (idx < 0)
                    {
                        // otherwise, a negative Index just means there was no direct hit and ~idx - 1 will give us the index to the next smaller starting Address
                        idx = ~idx - 1;
                    }
                    var startAddress = SortedNativeAllocations.Address(idx);
                    if (pointer >= startAddress && pointer < (startAddress + SortedNativeAllocations.FullSize(idx)))
                    {
                        nativeAllocationIndex = new SourceIndex(SourceIndex.SourceId.NativeAllocation, SortedNativeAllocations[idx]);
                        return NativeAllocationOrRegionSearchResult.FoundAllocation;
                    }
                }
            }
            else
            {
                // This pointer points out of range! Could be IL2CPP Virtual Machine Memory but it could just be entirely broken
                return NativeAllocationOrRegionSearchResult.NotInTrackedMemory;
            }
            return nativeRegionIndexInSortedRegions >= 0 ? NativeAllocationOrRegionSearchResult.FoundRegion : NativeAllocationOrRegionSearchResult.NothingFound;
        }

        public bool UseDeviceMemoryForGraphics
        {
            get
            {
                return NativeMemoryRegions.GPUAllocatorIndices.Count > 0;
            }
        }

        public void Dispose()
        {
            if (!m_Disposed)
            {
                m_Disposed = true;

                m_MetricsCollector.Dispose();
                m_MetricsCollector = default;

                NativeAllocationSites.Dispose();
                TypeDescriptions.Dispose();
                NativeTypes.Dispose();
                NativeRootReferences.Dispose();
                NativeObjects.Dispose();
                NativeMemoryRegions.Dispose();
                NativeMemoryLabels.Dispose();
                NativeCallstackSymbols.Dispose();
                NativeAllocations.Dispose();
                ManagedStacks.Dispose();
                ManagedHeapSections.Dispose();
                GcHandles.Dispose();
                FieldDescriptions.Dispose();
                Connections.Dispose();
                ManagedObjects?.Dispose();
                ManagedReferences?.Dispose();
                ManagedRoots?.Dispose();
                SceneRoots.Dispose();
                NativeGfxResourceReferences.Dispose();
                NativeAllocations.Dispose();
                NativeAllocators.Dispose();

                SystemMemoryRegions.Dispose();
                SystemMemoryResidentPages.Dispose();

                RootAndImpactInfo.Dispose();
                ObjectStatusComputation?.Dispose();
                EntriesMemoryMap.Dispose();
                CrawledData.Dispose();
                CrawledData = null;

                TableEntryNames?.Dispose();
                TableEntryNames = null;

                OnDemandHeapCache?.Dispose();
                OnDemandHeapCache = null;

                SortedNativeRegionsEntries.Dispose();
                SortedSystemMemoryRegions.Dispose();
                SortedManagedObjects.Dispose();
                SortedNativeAllocations.Dispose();
                SortedNativeObjects.Dispose();

                ProcessedNativeRoots.Dispose();
                // Close and dispose the reader
                m_Reader.Close();
            }
        }

        unsafe static void ConvertDynamicArrayByteBufferToManagedArray<T>(DynamicArray<byte> nativeEntryBuffer, ref T[] elements) where T : class
        {
            byte* binaryDataStream = nativeEntryBuffer.GetUnsafeTypedPtr();
            //jump over the offsets array
            long* binaryEntriesLength = (long*)binaryDataStream;
            binaryDataStream = binaryDataStream + sizeof(long) * (elements.Length + 1); //+1 due to the final element offset being at the end

            for (int i = 0; i < elements.Length; ++i)
            {
                byte* srcPtr = binaryDataStream + binaryEntriesLength[i];
                long actualLength = binaryEntriesLength[i + 1] - binaryEntriesLength[i];

                if (typeof(T) == typeof(string))
                {
                    var intLength = Convert.ToInt32(actualLength);
                    elements[i] = new string(unchecked((sbyte*)srcPtr), 0, intLength, System.Text.Encoding.UTF8) as T;
                }
                else if (typeof(T) == typeof(BitArray))
                {
                    byte[] temp = new byte[actualLength];
                    fixed (byte* dstPtr = temp)
                        UnsafeUtility.MemCpy(dstPtr, srcPtr, actualLength);

                    var arr = new BitArray(temp);
                    elements[i] = arr as T;
                }
                else
                {
                    Debug.LogError($"Use {nameof(NestedDynamicArray<byte>)} instead");
                    if (typeof(T) == typeof(byte[]))
                    {
                        var arr = new byte[actualLength];
                        fixed (void* dstPtr = arr)
                            UnsafeUtility.MemCpy(dstPtr, srcPtr, actualLength);
                        elements[i] = arr as T;
                    }
                    else if (typeof(T) == typeof(int[]))
                    {
                        var arr = new int[actualLength / sizeof(int)];
                        fixed (void* dstPtr = arr)
                            UnsafeUtility.MemCpy(dstPtr, srcPtr, actualLength);
                        elements[i] = arr as T;
                    }
                    else if (typeof(T) == typeof(ulong[]))
                    {
                        var arr = new ulong[actualLength / sizeof(ulong)];
                        fixed (void* dstPtr = arr)
                            UnsafeUtility.MemCpy(dstPtr, srcPtr, actualLength);
                        elements[i] = arr as T;
                    }
                    else if (typeof(T) == typeof(long[]))
                    {
                        var arr = new long[actualLength / sizeof(long)];
                        fixed (void* dstPtr = arr)
                            UnsafeUtility.MemCpy(dstPtr, srcPtr, actualLength);
                        elements[i] = arr as T;
                    }
                    else
                    {
                        Debug.LogErrorFormat("Unsuported type provided for conversion, type name: {0}", typeof(T).FullName);
                    }
                }
            }
        }

        /// <summary>
        /// Compact (64bit) and type-safe index into CachedSnapshot data.
        ///
        /// Nb! SourceIndex doesn't provide safety checks that the correct CachedSnapshot
        /// is used with the index and it's user's responsibility to pair index with
        /// the correct snapshot.
        /// </summary>
        [Serializable, StructLayout(LayoutKind.Explicit, Size = sizeof(ulong), Pack = sizeof(ulong))]
        readonly public struct SourceIndex : IEquatable<SourceIndex>, IComparable<SourceIndex>, IEqualityComparer<SourceIndex>
        {
            const int kSourceIdShift = 56;
            const long kSourceIdMask = 0xFF;
            const long kIndexMask = 0x00FFFFFFFFFFFFFF;

            public enum SourceId : byte
            {
                None,
                SystemMemoryRegion,
                NativeMemoryRegion,
                ManagedHeapSection,
                NativeObject,
                NativeRootReference,
                NativeAllocation,
                ManagedObject,
                NativeType,
                ManagedType,
                GfxResource,
                GCHandleIndex,
                MemoryLabel,
                Scene,
                Prefab
            }

            public enum SpecialNoneCase : long
            {
                None,
                UnrootedAllocation,
                UnknownMemLabel,
                Root,
            }

            [FieldOffset(0)]
            readonly ulong m_Data;

            public readonly SourceId Id
            {
                get => (SourceId)(m_Data >> kSourceIdShift);
            }

            public readonly long Index
            {
                get => (long)(m_Data & kIndexMask);
            }

            public bool Valid => Id != SourceId.None;

            public SourceIndex(SourceId source, long index)
            {
                if (((ulong)source > kSourceIdMask) || index < 0 || ((ulong)index > kIndexMask))
                    throw new ArgumentOutOfRangeException();

                m_Data = ((ulong)source << kSourceIdShift) | ((ulong)index & kIndexMask);
            }

            public string GetName(CachedSnapshot snapshot)
            {
                switch (Id)
                {
                    case SourceId.None:
                        switch ((SpecialNoneCase)Index)
                        {
                            case SpecialNoneCase.UnrootedAllocation:
                                return UnrootedItemName;
                            case SpecialNoneCase.UnknownMemLabel:
                                return UnknownMemlabelName;
                            case SpecialNoneCase.Root:
                                return RootName;
                            default:
                                return string.Empty;
                        }

                    case SourceId.SystemMemoryRegion:
                    {
                        var name = snapshot.SystemMemoryRegions.RegionName[Index];
                        if (string.IsNullOrEmpty(name))
                        {
                            var regionType = snapshot.SystemMemoryRegions.RegionType[Index];
                            if (regionType != SystemMemoryRegionEntriesCache.MemoryType.Mapped)
                                name = regionType.ToString();
                        }
                        return name;
                    }

                    case SourceId.NativeMemoryRegion:
                        return snapshot.NativeMemoryRegions.MemoryRegionName[Index];
                    case SourceId.NativeAllocation:
                        return snapshot.NativeAllocations.ProduceAllocationNameForAllocation(snapshot, Index);
                    case SourceId.NativeObject:
                        return snapshot.NativeObjects.GetNonEmptyObjectName(Index);
                    case SourceId.NativeType:
                        return snapshot.NativeTypes.TypeName[Index];
                    case SourceId.NativeRootReference:
                        return snapshot.NativeRootReferences.GetNonEmptyObjectName(Index);
                    case SourceId.ManagedHeapSection:
                        return snapshot.ManagedHeapSections.SectionName(Index);
                    case SourceId.ManagedObject:
                    {
                        return snapshot.CrawledData.ManagedObjects[Index].ProduceManagedObjectName(snapshot);
                    }
                    case SourceId.ManagedType:
                        return snapshot.TypeDescriptions.TypeDescriptionName[Index];


                    case SourceId.GfxResource:
                    {
                        // Get associated memory label root
                        var rootReferenceId = snapshot.NativeGfxResourceReferences.RootId[Index];
                        if (rootReferenceId <= 0)
                            return InvalidItemName;

                        // Lookup native object index associated with memory label root
                        if (snapshot.NativeObjects.RootReferenceIdToIndex.TryGetValue(rootReferenceId, out var objectIndex))
                            return snapshot.NativeObjects.GetNonEmptyObjectName(objectIndex);

                        // Try to see is memory label root associated with any memory area
                        if (snapshot.NativeRootReferences.IdToIndex.TryGetValue(rootReferenceId, out long rootIndex))
                            return snapshot.NativeRootReferences.AreaName[rootIndex] + ":" + snapshot.NativeRootReferences.ObjectName[rootIndex];

                        return InvalidItemName;
                    }
                    case SourceId.GCHandleIndex:
                        if (snapshot.NativeObjects.GCHandleIndexToIndex.TryGetValue(Index, out var nativeObjectIndex))
                            return new SourceIndex(SourceId.NativeObject, nativeObjectIndex).GetName(snapshot);
                        return $"GCHandle index: {Index}";
                    case SourceId.MemoryLabel:
                        return Index >= NativeMemoryLabelEntriesCache.InvalidMemLabelIndex ? snapshot.NativeMemoryLabels.MemoryLabelName[Index] : UnknownMemlabelName;
                    case SourceId.Scene:
                        return $"{snapshot.SceneRoots.Name[Index]}.scene";
                    case SourceId.Prefab:
                        return snapshot.SceneRoots.AllPrefabRootTransformSourceIndices[Index].GetName(snapshot);
                }

                Debug.Assert(false, $"Unknown source link type {Id}, please report a bug.");
                return InvalidItemName;
            }

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public bool Equals(SourceIndex other) => m_Data == other.m_Data;

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public override bool Equals(object obj) => obj is SourceIndex other && Equals(other);

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public bool Equals(SourceIndex x, SourceIndex y) => x.Equals(y);

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public override int GetHashCode() => m_Data.GetHashCode();
            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public int GetHashCode(SourceIndex index) => index.m_Data.GetHashCode();

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public static bool operator ==(SourceIndex x, SourceIndex y) => x.m_Data == y.m_Data;
            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public static bool operator !=(SourceIndex x, SourceIndex y) => x.m_Data != y.m_Data;

            public override string ToString()
            {
                return $"(Source:{Id} Index:{Index})";
            }

            readonly int IComparable<SourceIndex>.CompareTo(SourceIndex other)
            {
                var ret = Id.CompareTo(other.Id);
                if (ret == 0)
                    ret = Index.CompareTo(other.Index);
                return ret;
            }
        }
    }
}

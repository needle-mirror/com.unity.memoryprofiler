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
using Debug = UnityEngine.Debug;
using UnityException = UnityEngine.UnityException;
using Unity.MemoryProfiler.Editor.Diagnostics;


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

#if ENTITY_ID_STRUCT_AVAILABLE && !ENTITY_ID_CHANGED_SIZE
        static CachedSnapshot()
        {
            Checks.IsTrue((typeof(EntityId) != typeof(UnityEngine.EntityId)), "The wrong type of EntityId struct is used, probably due to accidentally addin a 'using UnityEngine;' to this file.");
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

        bool m_HasPrefabRootInfo;
        public bool HasPrefabRootInfo => m_HasPrefabRootInfo;

        public ManagedData CrawledData { internal set; get; }

        IFileReader m_Reader;
        public MetaData MetaData { get; private set; }
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
        public GCHandleEntriesCache GcHandles;
        public FieldDescriptionEntriesCache FieldDescriptions;
        public ConnectionEntriesCache Connections;

        public SortedNativeMemoryRegionEntriesCache SortedNativeRegionsEntries;
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

        public CachedSnapshot(IFileReader reader)
        {
            unsafe
            {
                VirtualMachineInformation vmInfo;
                reader.ReadUnsafe(EntryType.Metadata_VirtualMachineInformation, &vmInfo, sizeof(VirtualMachineInformation), 0, 1);

                // Re-enable this check once we add capture of the VM memory on CoreCLR. It is disabled currently to allow us to have snapshot tests running that are unrelated to VM memory. https://jira.unity3d.com/browse/VM-1768
#if !ENABLE_CORECLR
                if (!VMTools.ValidateVirtualMachineInfo(vmInfo))
                {
                    throw new UnityException("Invalid VM info. Snapshot file is corrupted.");
                }
#endif

                m_Reader = reader;
                long ticks;
                reader.ReadUnsafe(EntryType.Metadata_RecordDate, &ticks, sizeof(long), 0, 1);
                TimeStamp = new DateTime(ticks);

                m_VirtualMachineInformation = vmInfo;
                m_SnapshotVersion = reader.FormatVersion;

                MetaData = new MetaData(reader);
                SetUnityVersionSpecificFlags();

                NativeAllocationSites = new NativeAllocationSiteEntriesCache(ref reader);
                FieldDescriptions = new FieldDescriptionEntriesCache(ref reader);
                NativeTypes = new NativeTypeEntriesCache(ref reader);
                TypeDescriptions = new TypeDescriptionEntriesCache(ref reader, FieldDescriptions, NativeTypes, vmInfo);
                NativeRootReferences = new NativeRootReferenceEntriesCache(ref reader);
                NativeObjects = new NativeObjectEntriesCache(ref reader, NativeTypes.AssetBundleIdx);
                NativeMemoryRegions = new NativeMemoryRegionEntriesCache(ref reader);
                NativeMemoryLabels = new NativeMemoryLabelEntriesCache(ref reader, HasMemoryLabelSizesAndGCHeapTypes);
                NativeCallstackSymbols = new NativeCallstackSymbolEntriesCache(ref reader);
                NativeAllocations = new NativeAllocationEntriesCache(ref reader, NativeAllocationSites.Count != 0);
                ManagedStacks = new ManagedMemorySectionEntriesCache(ref reader, false, true);
                ManagedHeapSections = new ManagedMemorySectionEntriesCache(ref reader, HasMemoryLabelSizesAndGCHeapTypes, false);
                GcHandles = new GCHandleEntriesCache(ref reader);
                Connections = new ConnectionEntriesCache(ref reader, NativeObjects, GcHandles.Count, HasConnectionOverhaul);
                SceneRoots = new SceneRootEntriesCache(ref reader);
                // TODO: Jobifiy GenerateBaseData and sync at end
                SceneRoots.GenerateBaseData(this, NativeObjects.InstanceId2Index, NativeTypes.GameObjectIdx);

                NativeGfxResourceReferences = new NativeGfxResourceReferenceEntriesCache(ref reader);
                NativeAllocators = new NativeAllocatorEntriesCache(ref reader);

                SystemMemoryRegions = new SystemMemoryRegionEntriesCache(ref reader);
                SystemMemoryResidentPages = new SystemMemoryResidentPagesEntriesCache(ref reader);

                SortedManagedObjects = new SortedManagedObjectsCache(this);

                SortedNativeRegionsEntries = new SortedNativeMemoryRegionEntriesCache(this);
                SortedNativeAllocations = new SortedNativeAllocationsCache(this);
                SortedNativeObjects = new SortedNativeObjectsCache(this);

                EntriesMemoryMap = new EntriesMemoryMapCache(this);

                CrawledData = new ManagedData(GcHandles.Count, Connections.Count, NativeTypes.Count);

                ProcessedNativeRoots = new ProcessedNativeRoots();
            }
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

        public int PostProcessStepCountWithCrawler =>
            ManagedDataCrawler.CrawlStepCount
            + PostProcessStepCountWithoutCrawler;
        public int PostProcessStepCountWithoutCrawler =>
            +EntriesMemoryMapCache.BuildStepCount
            + ProcessedNativeRoots.ProcessStepCount
            + RootAndImpactInfo.ProcessStepCount
            + 1; //final step

        public IEnumerator<EnumerationStatus> PostProcess(bool crawlManaged = true)
        {
            var status = new EnumerationStatus(crawlManaged ? PostProcessStepCountWithCrawler : PostProcessStepCountWithoutCrawler);

            IEnumerator<EnumerationStatus> processor = null;
            // Managed Objects end up on the Memory Map, so crawl them first
            if (crawlManaged)
            {
                processor = ManagedDataCrawler.Crawl(this, status);
                while (processor.MoveNext())
                    yield return processor.Current;
            }
            // these need the managed object count
            RootAndImpactInfo = new RootAndImpactInfo(this);

            // To populate ProcessedNativeRoots with reliable native sizes for all allocations, as well as Resident vs Committed amounts for all Native Roots,the Memory Map needs to be built
            processor = EntriesMemoryMap.Build(status);
            while (processor.MoveNext())
                yield return processor.Current;

            // The Unity Objects Summary table will need ready to go ProcessedNativeRoots to show Unity Objects with their rooted native, gfx and managed memory
            processor = ProcessedNativeRoots.ReadOrProcess(this, status);
            while (processor.MoveNext())
                yield return processor.Current;

            // The Impact columns need Path To Root info, though this could be moved to the background?
            processor = RootAndImpactInfo.ReadOrProcess(this, status);
            while (processor.MoveNext())
                yield return processor.Current;
        }

        public string FullPath
        {
            get
            {
                return m_Reader.FullPath;
            }
        }

        //Unified Object index are in that order: gcHandle, native object, crawled objects
        public long ManagedObjectIndexToUnifiedObjectIndex(long i)
        {
            // Invalid
            if (i < 0)
                return -1;

            // GC Handle
            if (i < GcHandles.UniqueCount)
                return i;

            // Managed objects, but not reported through GC Handle
            if (i < CrawledData.ManagedObjects.Count)
                return i + NativeObjects.Count;

            return -1;
        }

        public long NativeObjectIndexToUnifiedObjectIndex(long i)
        {
            if (i < 0)
                return -1;

            // Shift behind native objects
            if (i < NativeObjects.Count)
                return i + GcHandles.UniqueCount;

            return -1;
        }

        public int UnifiedObjectIndexToManagedObjectIndex(long i)
        {
            if (i < 0)
                return -1;

            if (i < GcHandles.UniqueCount)
                return (int)i;

            // If CrawledData.ManagedObjects includes GcHandles as first GcHandles.Count
            // than it makes sense as we want to remap only excess
            int firstCrawled = (int)(GcHandles.UniqueCount + NativeObjects.Count);
            int lastCrawled = (int)(NativeObjects.Count + CrawledData.ManagedObjects.Count);

            if (i >= firstCrawled && i < lastCrawled)
                return (int)(i - (int)NativeObjects.Count);

            return -1;
        }

        public int UnifiedObjectIndexToNativeObjectIndex(long i)
        {
            if (i < GcHandles.UniqueCount) return -1;
            int firstCrawled = (int)(GcHandles.UniqueCount + NativeObjects.Count);
            if (i < firstCrawled) return (int)(i - (int)GcHandles.UniqueCount);
            return -1;
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
                SceneRoots.Dispose();
                NativeGfxResourceReferences.Dispose();
                NativeAllocations.Dispose();
                NativeAllocators.Dispose();

                SystemMemoryRegions.Dispose();
                SystemMemoryResidentPages.Dispose();

                RootAndImpactInfo.Dispose();
                EntriesMemoryMap.Dispose();
                CrawledData.Dispose();
                CrawledData = null;

                SortedNativeRegionsEntries.Dispose();
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
        [StructLayout(LayoutKind.Explicit, Size = sizeof(ulong), Pack = sizeof(ulong))]
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
                NativeAllocation,
                ManagedHeapSection,
                NativeObject,
                ManagedObject,
                NativeType,
                ManagedType,
                NativeRootReference,
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
                            var regionType = (SystemMemoryRegionEntriesCache.MemoryType)snapshot.SystemMemoryRegions.RegionType[Index];
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
                        return snapshot.NativeObjects.ObjectName[Index];
                    case SourceId.NativeType:
                        return snapshot.NativeTypes.TypeName[Index];
                    case SourceId.NativeRootReference:
                        return snapshot.NativeRootReferences.ObjectName[Index];

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
                            return snapshot.NativeObjects.ObjectName[objectIndex];

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

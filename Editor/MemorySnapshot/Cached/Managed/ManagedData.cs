using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Extensions;
using System.Collections.Generic;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;
using System.Runtime.CompilerServices;

using System.Runtime.InteropServices;


#if !UNMANAGED_NATIVE_HASHMAP_AVAILABLE
using AddressToManagedIndexHashMap = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<ulong, long>;
using ConnectionsHashMap = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<Unity.MemoryProfiler.Editor.CachedSnapshot.SourceIndex, Unity.Collections.LowLevel.Unsafe.UnsafeList<int>>;
using InvalidObjectsHashMap = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<long, Unity.MemoryProfiler.Editor.ManagedObjectInfo>;
#else
using AddressToManagedIndexHashMap = Unity.Collections.NativeHashMap<ulong, long>;
using ConnectionsHashMap  = Unity.Collections.NativeHashMap<Unity.MemoryProfiler.Editor.CachedSnapshot.SourceIndex, Unity.Collections.LowLevel.Unsafe.UnsafeList<int>>;
using InvalidObjectsHashMap  = Unity.Collections.NativeHashMap<long, Unity.MemoryProfiler.Editor.ManagedObjectInfo>;
#endif

namespace Unity.MemoryProfiler.Editor
{
    class ManagedData : IDisposable
    {
        public const long FirstValidObjectIndex = 0;
        public const long InvalidObjectIndex = -1;

        public bool Crawled { private set; get; }
        const int k_ManagedConnectionsInitialSize = 65536;
        const int k_ExtraordinaryLifenessHashSetsInitialSize = 64;
        DynamicArray<ManagedObjectInfo> m_ManagedObjects;
        public ref DynamicArray<ManagedObjectInfo> ManagedObjects => ref m_ManagedObjects;
        AddressToManagedIndexHashMap m_MangedObjectIndexByAddress;
        public ref AddressToManagedIndexHashMap MangedObjectIndexByAddress => ref m_MangedObjectIndexByAddress;
        public BlockList<ManagedConnection> Connections { private set; get; }
        public ulong ManagedObjectMemoryUsage { private set; get; }
        public ulong ActiveHeapMemoryUsage { private set; get; }
        public ulong ActiveHeapMemoryEmptySpace { private set; get; }
        // ConnectionsToMappedToSourceIndex and ConnectionsFromMappedToSourceIndex are derived structure used in accelerating searches in the details view
        ConnectionsHashMap m_ConnectionsToMappedToSourceIndex;
        public ref ConnectionsHashMap ConnectionsToMappedToSourceIndex => ref m_ConnectionsToMappedToSourceIndex;
        ConnectionsHashMap m_ConnectionsFromMappedToSourceIndex;
        public ref ConnectionsHashMap ConnectionsFromMappedToSourceIndex => ref m_ConnectionsFromMappedToSourceIndex;

        NativeHashSet<long> m_IndicesOfManagedObjectsHeldByRequiredByNativeCodeAttribute;
        /// <summary>Indices of managed objects held by a GCHandle believed to be arrays attributed with <c>[RequiredByNativeCode]</c> / <c>[UsedByNativeCode]</c> (or a <c>[CustomEditor]</c>-driven equivalent), inferred heuristically since the attribute itself isn't captured.</summary>
        public ref NativeHashSet<long> IndicesOfManagedObjectsHeldByRequiredByNativeCodeAttribute => ref m_IndicesOfManagedObjectsHeldByRequiredByNativeCodeAttribute;

        NativeHashSet<long> m_IndicesOfPureManagedObjectsHeldByNonNativeObjectRelatedGCHandle;
        /// <summary>Indices of non-Unity-Object managed objects held by a GCHandle for a reason other than being backed by a native object.</summary>
        public ref NativeHashSet<long> IndicesOfPureManagedObjectsHeldByNonNativeObjectRelatedGCHandle => ref m_IndicesOfPureManagedObjectsHeldByNonNativeObjectRelatedGCHandle;

        NativeHashSet<long> m_IndicesOfManagedPotentialUnityObjectsHeldByNonNativeObjectRelatedGCHandle;
        /// <summary>Indices of managed objects held by a GCHandle that are, or could be, Unity Object types, but aren't yet confirmed to be backed by a native object.</summary>
        public ref NativeHashSet<long> IndicesOfManagedPotentialUnityObjectsHeldByNonNativeObjectRelatedGCHandle => ref m_IndicesOfManagedPotentialUnityObjectsHeldByNonNativeObjectRelatedGCHandle;

        /// <summary>
        /// False when <see cref="ManagedObjects"/> was populated from the backend graph without heap bytes
        /// (raw backend file order instead of the byte walk's handle-targets-first order). Use
        /// <see cref="ResolveManagedObjectIndexForGCHandle"/> / <see cref="IsGCHandleTarget"/> rather than
        /// comparing indices directly.
        /// </summary>
        public bool ObjectIndicesMatchGCHandleOrder { get; set; } = true;
        /// <summary>
        /// Populated only when <see cref="ObjectIndicesMatchGCHandleOrder"/> is false: GCHandle index -> the
        /// <see cref="ManagedObjectInfo.ManagedObjectIndex"/> it targets, or <see cref="InvalidObjectIndex"/>
        /// if that target has no <see cref="ManagedObjectInfo"/> row.
        /// </summary>
        DynamicArray<long> m_GCHandleIndexToManagedObjectIndex;
        public ref DynamicArray<long> GCHandleIndexToManagedObjectIndex => ref m_GCHandleIndexToManagedObjectIndex;
        /// <summary>Populated only when <see cref="ObjectIndicesMatchGCHandleOrder"/> is false. Reverse lookup: is this <see cref="ManagedObjectInfo.ManagedObjectIndex"/> the target of some GCHandle.</summary>
        NativeHashSet<long> m_ManagedObjectIndicesThatAreGCHandleTargets;
        public ref NativeHashSet<long> ManagedObjectIndicesThatAreGCHandleTargets => ref m_ManagedObjectIndicesThatAreGCHandleTargets;

        /// <summary>
        /// The <see cref="ManagedObjectInfo.ManagedObjectIndex"/> that GCHandle <paramref name="gcHandleIndex"/>
        /// targets, or <see cref="InvalidObjectIndex"/> if that target has no <see cref="ManagedObjectInfo"/> row
        /// (e.g. collected mid-capture).
        /// </summary>
        public long ResolveManagedObjectIndexForGCHandle(long gcHandleIndex) =>
            ObjectIndicesMatchGCHandleOrder ? gcHandleIndex : GCHandleIndexToManagedObjectIndex[gcHandleIndex];

        /// <summary>
        /// True if <paramref name="managedObjectIndex"/> is the target of some GCHandle. <paramref name="uniqueGCHandleCount"/>
        /// should be <see cref="CachedSnapshot.GCHandleEntriesCache.UniqueCount"/>, only used on the
        /// <see cref="ObjectIndicesMatchGCHandleOrder"/> fast path.
        /// </summary>
        public bool IsGCHandleTarget(long managedObjectIndex, long uniqueGCHandleCount) =>
            ObjectIndicesMatchGCHandleOrder ? managedObjectIndex < uniqueGCHandleCount : ManagedObjectIndicesThatAreGCHandleTargets.Contains(managedObjectIndex);

#if DEBUG_VALIDATION
        // This Dictionary block is here to make the investigations for PROF-2420 easier.
        InvalidObjectsHashMap m_InvalidManagedObjectsReportedViaGCHandles;
        public ref InvalidObjectsHashMap InvalidManagedObjectsReportedViaGCHandles => ref m_InvalidManagedObjectsReportedViaGCHandles;
#endif

        public ManagedData(long rawGcHandleCount, long rawConnectionsCount, long nativeTypeCount)
        {
            //compute initial block counts for larger snapshots
            m_ManagedObjects = new DynamicArray<ManagedObjectInfo>(0, rawGcHandleCount, Allocator.Persistent, true);
            m_MangedObjectIndexByAddress = new AddressToManagedIndexHashMap(k_ManagedConnectionsInitialSize, Allocator.Persistent);
            m_ConnectionsToMappedToSourceIndex = new ConnectionsHashMap(k_ManagedConnectionsInitialSize, Allocator.Persistent);
            m_ConnectionsFromMappedToSourceIndex = new ConnectionsHashMap(k_ManagedConnectionsInitialSize, Allocator.Persistent);
#if DEBUG_VALIDATION
            m_InvalidManagedObjectsReportedViaGCHandles = new InvalidObjectsHashMap(k_ManagedConnectionsInitialSize, Allocator.Persistent);
#endif
            Connections = new BlockList<ManagedConnection>(k_ManagedConnectionsInitialSize, rawConnectionsCount);
            m_IndicesOfManagedObjectsHeldByRequiredByNativeCodeAttribute = new NativeHashSet<long>(k_ExtraordinaryLifenessHashSetsInitialSize, Allocator.Persistent);
            m_IndicesOfPureManagedObjectsHeldByNonNativeObjectRelatedGCHandle = new NativeHashSet<long>(k_ExtraordinaryLifenessHashSetsInitialSize, Allocator.Persistent);
            m_IndicesOfManagedPotentialUnityObjectsHeldByNonNativeObjectRelatedGCHandle = new NativeHashSet<long>(k_ExtraordinaryLifenessHashSetsInitialSize, Allocator.Persistent);
        }

        internal void AddUpTotalMemoryUsageAndCacheStringPreviews(CachedSnapshot snapshot, CachedSnapshot.ManagedMemorySectionEntriesCache managedMemorySections)
        {
            var totalManagedObjectsCount = ManagedObjects.Count;
            ManagedObjectMemoryUsage = 0;
            if (managedMemorySections.Count <= 0)
            {
                ActiveHeapMemoryUsage = 0;

                return;
            }

            var tableEntryNames = snapshot.TableEntryNames;
            var typeDescriptions = snapshot.TypeDescriptions;
            var stringTypeIndex = typeDescriptions.ITypeString;
            var charArrayTypeIndex = typeDescriptions.ITypeCharArray;
            var vmInfo = snapshot.VirtualMachineInformation;

            var activeHeapSectionStartAddress = managedMemorySections.StartAddress[managedMemorySections.FirstAssumedActiveHeapSectionIndex];
            var activeHeapSectionEndAddress = managedMemorySections.StartAddress[managedMemorySections.LastAssumedActiveHeapSectionIndex] + managedMemorySections.SectionSize[managedMemorySections.LastAssumedActiveHeapSectionIndex];

            for (long i = 0; i < totalManagedObjectsCount; i++)
            {
                ref readonly var managedObject = ref ManagedObjects[i];
                var size = (ulong)managedObject.Size;
                ManagedObjectMemoryUsage += size;

                if (managedObject.PtrObject > activeHeapSectionStartAddress && managedObject.PtrObject < activeHeapSectionEndAddress)
                {
                    ActiveHeapMemoryUsage += size;
                }

                if (tableEntryNames == null)
                    continue;

                var typeIndex = managedObject.ITypeDescription;
                if (typeIndex == stringTypeIndex)
                {
                    // Cache string preview
                    var preview = managedObject.ReadFirstStringLine(vmInfo, addQuotes: true);
                    tableEntryNames.CachePreview(i, preview);
                }
                else if (typeIndex == charArrayTypeIndex)
                {
                    // Cache char[] preview ...
                    var preview = managedObject.ReadFirstCharArrayLine(snapshot, addQuotes: true);
                    tableEntryNames.CachePreview(i, preview);
                    // ... and their array info
                    var arrayInfo = ManagedHeapArrayDataTools.GetArrayInfo(snapshot, managedObject.data, typeIndex);
                    if (arrayInfo != null)
                    {
                        tableEntryNames.CacheArrayInfo(i, arrayInfo);
                    }
                }
                else if (typeIndex >= 0 && typeDescriptions.HasFlag(typeIndex, Unity.MemoryProfiler.Editor.Format.TypeFlags.kArray))
                {
                    // Cache array info for all array types
                    var arrayInfo = ManagedHeapArrayDataTools.GetArrayInfo(snapshot, managedObject.data, typeIndex);
                    if (arrayInfo != null)
                    {
                        tableEntryNames.CacheArrayInfo(i, arrayInfo);
                    }
                }
            }
            ActiveHeapMemoryEmptySpace = managedMemorySections.StartAddress[managedMemorySections.LastAssumedActiveHeapSectionIndex]
                + managedMemorySections.SectionSize[managedMemorySections.LastAssumedActiveHeapSectionIndex]
                - managedMemorySections.StartAddress[managedMemorySections.FirstAssumedActiveHeapSectionIndex]
                - ActiveHeapMemoryUsage;
        }

        internal void FinishedCrawling()
        {
            Crawled = true;
        }

        public void CreateConnectionMaps(CachedSnapshot cs)
        {
            // presumably, we'll never have more connections than int max but technically, Connections is long indexed.
            Debug.Assert(Connections.Count < int.MaxValue);
            // Growing the capacity while iterating over it had significant enough performance impact to show up when profiling. Preempt it here
            ConnectionsToMappedToSourceIndex.Capacity = (int)Connections.Count;
            ConnectionsFromMappedToSourceIndex.Capacity = (int)Connections.Count;
            for (var i = 0; i < Connections.Count; i++)
            {
                ConnectionsToMappedToSourceIndex.GetAndAddToListOrCreateList(Connections[i].IndexTo, i, Allocator.Persistent);

                ConnectionsFromMappedToSourceIndex.GetAndAddToListOrCreateList(Connections[i].IndexFrom, i, Allocator.Persistent);

                // Technically all Native <-> Managed Object connections are bidirectional but only registered as a managed connection
                // in the direction Native -> Managed. Thus their inverse lookup should technically be added below for completeness
                // Practically that'd be confusing with the naming of these maps and if any users expect the mapped connection's
                // IndexTo and IndexFrom SourceId to match their lookup key's SourceId, that'd be confusing.
                // Also, all current users (i.e. ObjectConnection) of these maps are aware of the bidirectionallity and handle that
                // explicitly themselves.
                //if (Connections[i].IndexFrom.Id == CachedSnapshot.SourceIndex.SourceId.NativeObject
                //    && Connections[i].IndexTo.Id == CachedSnapshot.SourceIndex.SourceId.ManagedObject)
                //{
                //    ConnectionsToMappedToSourceIndex.AddToOrCreateList(Connections[i].IndexFrom, i);
                //    ConnectionsFromMappedToSourceIndex.AddToOrCreateList(Connections[i].IndexTo, i);
                //}
            }
        }

        public void Dispose()
        {
            ManagedObjects.Dispose();
            MangedObjectIndexByAddress.Dispose();
            GCHandleIndexToManagedObjectIndex.Dispose();
            ConnectionsToMappedToSourceIndex.DisposeListsAndHashmap();
            ConnectionsFromMappedToSourceIndex.DisposeListsAndHashmap();
            IndicesOfManagedObjectsHeldByRequiredByNativeCodeAttribute.Dispose();
            IndicesOfPureManagedObjectsHeldByNonNativeObjectRelatedGCHandle.Dispose();
            IndicesOfManagedPotentialUnityObjectsHeldByNonNativeObjectRelatedGCHandle.Dispose();
            if (ManagedObjectIndicesThatAreGCHandleTargets.IsCreated)
                ManagedObjectIndicesThatAreGCHandleTargets.Dispose();
#if DEBUG_VALIDATION
            InvalidManagedObjectsReportedViaGCHandles.Dispose();
#endif
        }
    }

    internal partial class CachedSnapshot
    {
        public class SortedManagedObjectsCache : IndirectlySortedEntriesCache<SortedManagedObjectsCache.IndexedManagedObjectAddressComparer, SortedManagedObjectsCache.UnsafeManagedObjectsCache>
        {
            /// <summary>
            /// This comparer is used to sort <seealso cref="IndirectlySortedEntriesCache.m_Sorting"/>
            /// (which is array of indices of, in the cas of this class, all <seealso cref="ManagedData.ManagedObjects"/>)
            /// based on the address of each object.
            ///
            /// This helper struct is needed as <seealso cref="ManagedObjectInfo"/> does not implement <seealso cref="IComparable{ManagedObjectInfo}"/>
            /// as it would be unclear what parameters it would compare by, and if it would forcibly be the address, that could be rather confusing.
            /// Especially since it is only needed here.
            /// </summary>
            public unsafe readonly struct IndexedManagedObjectAddressComparer : IRefComparer<long>
            {
                readonly ManagedObjectInfo* m_Ptr;
                public IndexedManagedObjectAddressComparer(in DynamicArray<ManagedObjectInfo> managedObjectInfos)
                {
                    m_Ptr = managedObjectInfos.GetUnsafeTypedPtr();
                }

                [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
                public int Compare(ref long objectAIndex, ref long objectBIndex)
                {
                    // managed objects can't have a negative size, or be nested within other managed objects, so we don't need to compare sizes as well
                    return m_Ptr[objectAIndex].PtrObject.CompareTo(m_Ptr[objectBIndex].PtrObject);
                }
            }

            public SortedManagedObjectsCache(CachedSnapshot snapshot) : base(snapshot) { }


            public override long Count => m_Snapshot.CrawledData.ManagedObjects.Count;

            protected override IndexedManagedObjectAddressComparer SortingComparer => new IndexedManagedObjectAddressComparer(in m_Snapshot.CrawledData.ManagedObjects);

            public override ulong Address(long index) => m_Snapshot.CrawledData.ManagedObjects[this[index]].PtrObject;
            public override ulong FullSize(long index) => (ulong)m_Snapshot.CrawledData.ManagedObjects[this[index]].Size;

            public override void Preload()
            {
                if (!m_Snapshot.CrawledData.Crawled)
                    throw new InvalidOperationException("Can't use SortedManagedObjectsCache before crawling is done");
                var wasLoaded = Loaded;
                base.Preload();
                if (!wasLoaded)
                {
                    m_UnsafeCache = new UnsafeManagedObjectsCache(m_Sorting, m_Snapshot.CrawledData.ManagedObjects);
                }
            }
            UnsafeManagedObjectsCache m_UnsafeCache;
            public override UnsafeManagedObjectsCache UnsafeCache
            {
                get
                {
                    if (Loaded)
                        return m_UnsafeCache;
                    Preload();
                    return m_UnsafeCache;
                }
            }
            [StructLayout(LayoutKind.Sequential)]
            public readonly struct UnsafeManagedObjectsCache : CachedSnapshot.ISortedEntriesCache
            {
                public long Count => m_ManagedObjects.Count;

                public long this[long index] => m_Sorting[index];
                readonly DynamicArray<long> m_Sorting;
                readonly DynamicArray<ManagedObjectInfo> m_ManagedObjects;
                public ulong Address(long index) => m_ManagedObjects[this[index]].PtrObject;
                public ulong FullSize(long index) => (ulong)m_ManagedObjects[this[index]].Size;

                public UnsafeManagedObjectsCache(DynamicArray<long> sorting, DynamicArray<ManagedObjectInfo> managedObjects)
                {
                    m_Sorting = sorting;
                    m_ManagedObjects = managedObjects;
                }
                // already preloaded
                public void Preload() { }
            }
        }
    }
}

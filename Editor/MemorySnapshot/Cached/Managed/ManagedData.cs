using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Extensions;
#if !UNMANAGED_NATIVE_HASHMAP_AVAILABLE
using AddressToManagedIndexHashMap = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<ulong, long>;
using TypeIndexMappingHashMap = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<int, int>;
using ConnectionsHashMap = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<Unity.MemoryProfiler.Editor.CachedSnapshot.SourceIndex, Unity.Collections.LowLevel.Unsafe.UnsafeList<int>>;
using InvalidObjectsHashMap = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<long, Unity.MemoryProfiler.Editor.ManagedObjectInfo>;
#else
using AddressToManagedIndexHashMap = Unity.Collections.NativeHashMap<ulong, long>;
using TypeIndexMappingHashMap  = Unity.Collections.NativeHashMap<int, int>;
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
        DynamicArray<ManagedObjectInfo> m_ManagedObjects;
        public ref DynamicArray<ManagedObjectInfo> ManagedObjects => ref m_ManagedObjects;
        AddressToManagedIndexHashMap m_MangedObjectIndexByAddress;
        public ref AddressToManagedIndexHashMap MangedObjectIndexByAddress => ref m_MangedObjectIndexByAddress;
        public BlockList<ManagedConnection> Connections { private set; get; }
        TypeIndexMappingHashMap m_NativeUnityObjectTypeIndexToManagedBaseTypeIndex = new TypeIndexMappingHashMap(k_ManagedConnectionsInitialSize, Allocator.Persistent);
        public ref TypeIndexMappingHashMap NativeUnityObjectTypeIndexToManagedBaseTypeIndex => ref m_NativeUnityObjectTypeIndexToManagedBaseTypeIndex;
        public ulong ManagedObjectMemoryUsage { private set; get; }
        public ulong ActiveHeapMemoryUsage { private set; get; }
        public ulong ActiveHeapMemoryEmptySpace { private set; get; }
        // ConnectionsToMappedToSourceIndex and ConnectionsFromMappedToSourceIndex are derived structure used in accelerating searches in the details view
        ConnectionsHashMap m_ConnectionsToMappedToSourceIndex;
        public ref ConnectionsHashMap ConnectionsToMappedToSourceIndex => ref m_ConnectionsToMappedToSourceIndex;
        ConnectionsHashMap m_ConnectionsFromMappedToSourceIndex;
        public ref ConnectionsHashMap ConnectionsFromMappedToSourceIndex => ref m_ConnectionsFromMappedToSourceIndex;

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
            m_NativeUnityObjectTypeIndexToManagedBaseTypeIndex = new TypeIndexMappingHashMap((int)nativeTypeCount, Allocator.Persistent);
            m_ConnectionsToMappedToSourceIndex = new ConnectionsHashMap(k_ManagedConnectionsInitialSize, Allocator.Persistent);
            m_ConnectionsFromMappedToSourceIndex = new ConnectionsHashMap(k_ManagedConnectionsInitialSize, Allocator.Persistent);
#if DEBUG_VALIDATION
            m_InvalidManagedObjectsReportedViaGCHandles = new InvalidObjectsHashMap(k_ManagedConnectionsInitialSize, Allocator.Persistent);
#endif
            Connections = new BlockList<ManagedConnection>(k_ManagedConnectionsInitialSize, rawConnectionsCount);
        }

        internal void AddUpTotalMemoryUsage(CachedSnapshot.ManagedMemorySectionEntriesCache managedMemorySections)
        {
            var totalManagedObjectsCount = ManagedObjects.Count;
            ManagedObjectMemoryUsage = 0;
            if (managedMemorySections.Count <= 0)
            {
                ActiveHeapMemoryUsage = 0;

                return;
            }

            var activeHeapSectionStartAddress = managedMemorySections.StartAddress[managedMemorySections.FirstAssumedActiveHeapSectionIndex];
            var activeHeapSectionEndAddress = managedMemorySections.StartAddress[managedMemorySections.LastAssumedActiveHeapSectionIndex] + managedMemorySections.SectionSize[managedMemorySections.LastAssumedActiveHeapSectionIndex];
            for (int i = 0; i < totalManagedObjectsCount; i++)
            {
                var size = (ulong)ManagedObjects[i].Size;
                ManagedObjectMemoryUsage += size;

                if (ManagedObjects[i].PtrObject > activeHeapSectionStartAddress && ManagedObjects[i].PtrObject < activeHeapSectionEndAddress)
                {
                    ActiveHeapMemoryUsage += size;
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
            NativeUnityObjectTypeIndexToManagedBaseTypeIndex.Dispose();
            foreach (var to in ConnectionsToMappedToSourceIndex)
                to.Value.Dispose();
            ConnectionsToMappedToSourceIndex.Dispose();
            foreach (var from in ConnectionsFromMappedToSourceIndex)
                from.Value.Dispose();
            ConnectionsFromMappedToSourceIndex.Dispose();
#if DEBUG_VALIDATION
            InvalidManagedObjectsReportedViaGCHandles.Dispose();
#endif
        }
    }

}

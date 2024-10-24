using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Extensions;

namespace Unity.MemoryProfiler.Editor
{
    class ManagedData : IDisposable
    {
        public bool Crawled { private set; get; }
        const int k_ManagedConnectionsInitialSize = 65536;
        DynamicArray<ManagedObjectInfo> m_ManagedObjects;
        public ref DynamicArray<ManagedObjectInfo> ManagedObjects => ref m_ManagedObjects;
        NativeHashMap<ulong, long> m_MangedObjectIndexByAddress;
        public ref NativeHashMap<ulong, long> MangedObjectIndexByAddress => ref m_MangedObjectIndexByAddress;
        public BlockList<ManagedConnection> Connections { private set; get; }
        NativeHashMap<int, int> m_NativeUnityObjectTypeIndexToManagedBaseTypeIndex = new NativeHashMap<int, int>(k_ManagedConnectionsInitialSize, Allocator.Persistent);
        public ref NativeHashMap<int, int> NativeUnityObjectTypeIndexToManagedBaseTypeIndex => ref m_NativeUnityObjectTypeIndexToManagedBaseTypeIndex;
        public ulong ManagedObjectMemoryUsage { private set; get; }
        public ulong ActiveHeapMemoryUsage { private set; get; }
        public ulong ActiveHeapMemoryEmptySpace { private set; get; }
        // ConnectionsToMappedToSourceIndex and ConnectionsFromMappedToSourceIndex are derived structure used in accelerating searches in the details view
        NativeHashMap<CachedSnapshot.SourceIndex, UnsafeList<int>> m_ConnectionsToMappedToSourceIndex;
        public ref NativeHashMap<CachedSnapshot.SourceIndex, UnsafeList<int>> ConnectionsToMappedToSourceIndex => ref m_ConnectionsToMappedToSourceIndex;
        NativeHashMap<CachedSnapshot.SourceIndex, UnsafeList<int>> m_ConnectionsFromMappedToSourceIndex;
        public ref NativeHashMap<CachedSnapshot.SourceIndex, UnsafeList<int>> ConnectionsFromMappedToSourceIndex => ref m_ConnectionsFromMappedToSourceIndex;

#if DEBUG_VALIDATION
        // This Dictionary block is here to make the investigations for PROF-2420 easier.
        NativeHashMap<long, ManagedObjectInfo> m_InvalidManagedObjectsReportedViaGCHandles;
        public ref NativeHashMap<long, ManagedObjectInfo> InvalidManagedObjectsReportedViaGCHandles => ref m_InvalidManagedObjectsReportedViaGCHandles;
#endif

        public ManagedData(long rawGcHandleCount, long rawConnectionsCount, long nativeTypeCount)
        {
            //compute initial block counts for larger snapshots
            m_ManagedObjects = new DynamicArray<ManagedObjectInfo>(0, rawGcHandleCount, Allocator.Persistent, true);
            m_MangedObjectIndexByAddress = new NativeHashMap<ulong, long>(k_ManagedConnectionsInitialSize, Allocator.Persistent);
            m_NativeUnityObjectTypeIndexToManagedBaseTypeIndex = new NativeHashMap<int, int>((int)nativeTypeCount, Allocator.Persistent);
            m_ConnectionsToMappedToSourceIndex = new NativeHashMap<CachedSnapshot.SourceIndex, UnsafeList<int>>(k_ManagedConnectionsInitialSize, Allocator.Persistent);
            m_ConnectionsFromMappedToSourceIndex = new NativeHashMap<CachedSnapshot.SourceIndex, UnsafeList<int>>(k_ManagedConnectionsInitialSize, Allocator.Persistent);
#if DEBUG_VALIDATION
            m_InvalidManagedObjectsReportedViaGCHandles = new NativeHashMap<long, ManagedObjectInfo>(k_ManagedConnectionsInitialSize, Allocator.Persistent);
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

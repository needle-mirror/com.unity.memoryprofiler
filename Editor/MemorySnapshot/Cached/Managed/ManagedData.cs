using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Extensions;

namespace Unity.MemoryProfiler.Editor
{
    class ManagedData : IDisposable
    {
        public bool Crawled { private set; get; }
        const int k_ManagedConnectionsBlockSize = 65536;
        DynamicArray<ManagedObjectInfo> m_ManagedObjects;
        public ref DynamicArray<ManagedObjectInfo> ManagedObjects => ref m_ManagedObjects;
        public Dictionary<ulong, int> MangedObjectIndexByAddress { private set; get; } = new Dictionary<ulong, int>();
        public BlockList<ManagedConnection> Connections { private set; get; }
        public Dictionary<int, int> NativeUnityObjectTypeIndexToManagedBaseTypeIndex { get; private set; } = new Dictionary<int, int>();
        public ulong ManagedObjectMemoryUsage { private set; get; }
        public ulong AbandonedManagedObjectMemoryUsage { private set; get; }
        public ulong ActiveHeapMemoryUsage { private set; get; }
        public ulong ActiveHeapMemoryEmptySpace { private set; get; }
        public ulong AbandonedManagedObjectActiveHeapMemoryUsage { private set; get; }
        // ConnectionsToMappedToSourceIndex and ConnectionsFromMappedToSourceIndex are derived structure used in accelerating searches in the details view
        public Dictionary<CachedSnapshot.SourceIndex, List<int>> ConnectionsToMappedToSourceIndex { private set; get; } = new Dictionary<CachedSnapshot.SourceIndex, List<int>>();
        public Dictionary<CachedSnapshot.SourceIndex, List<int>> ConnectionsFromMappedToSourceIndex { private set; get; } = new Dictionary<CachedSnapshot.SourceIndex, List<int>>();

#if DEBUG_VALIDATION
        // This Dictionary block is here to make the investigations for PROF-2420 easier.
        public Dictionary<int, ManagedObjectInfo> InvalidManagedObjectsReportedViaGCHandles { private set; get; } = new Dictionary<int, ManagedObjectInfo>();
#endif

        public ManagedData(long rawGcHandleCount, long rawConnectionsCount)
        {
            //compute initial block counts for larger snapshots
            m_ManagedObjects = new DynamicArray<ManagedObjectInfo>(rawGcHandleCount, Collections.Allocator.Persistent, true);
            m_ManagedObjects.Clear(false);
            Connections = new BlockList<ManagedConnection>(k_ManagedConnectionsBlockSize, rawConnectionsCount);
        }

        internal void AddUpTotalMemoryUsage(CachedSnapshot.ManagedMemorySectionEntriesCache managedMemorySections)
        {
            var totalManagedObjectsCount = ManagedObjects.Count;
            ManagedObjectMemoryUsage = 0;
            if (managedMemorySections.Count <= 0)
            {
                ActiveHeapMemoryUsage = AbandonedManagedObjectMemoryUsage = 0;

                return;
            }

            var activeHeapSectionStartAddress = managedMemorySections.StartAddress[managedMemorySections.FirstAssumedActiveHeapSectionIndex];
            var activeHeapSectionEndAddress = managedMemorySections.StartAddress[managedMemorySections.LastAssumedActiveHeapSectionIndex] + managedMemorySections.SectionSize[managedMemorySections.LastAssumedActiveHeapSectionIndex];
            for (int i = 0; i < totalManagedObjectsCount; i++)
            {
                var size = (ulong)ManagedObjects[i].Size;
                ManagedObjectMemoryUsage += size;
                if (ManagedObjects[i].RefCount == 0)
                    AbandonedManagedObjectMemoryUsage += size;

                if (ManagedObjects[i].PtrObject > activeHeapSectionStartAddress && ManagedObjects[i].PtrObject < activeHeapSectionEndAddress)
                {
                    ActiveHeapMemoryUsage += size;
                    if (ManagedObjects[i].RefCount == 0)
                        AbandonedManagedObjectActiveHeapMemoryUsage += size;
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
            for (var i = 0; i < Connections.Count; i++)
            {
                ConnectionsToMappedToSourceIndex.GetAndAddToListOrCreateList(Connections[i].IndexTo, i);

                ConnectionsFromMappedToSourceIndex.GetAndAddToListOrCreateList(Connections[i].IndexFrom, i);

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
        }
    }

}

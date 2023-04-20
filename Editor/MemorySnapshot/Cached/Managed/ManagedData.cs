using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.Containers;

namespace Unity.MemoryProfiler.Editor
{
    class ManagedData
    {
        public bool Crawled { private set; get; }
        const int k_ManagedObjectBlockSize = 32768;
        const int k_ManagedConnectionsBlockSize = 65536;
        public BlockList<ManagedObjectInfo> ManagedObjects { private set; get; }
        public Dictionary<ulong, int> MangedObjectIndexByAddress { private set; get; } = new Dictionary<ulong, int>();
        public BlockList<ManagedConnection> Connections { private set; get; }
        public Dictionary<int, int> NativeUnityObjectTypeIndexToManagedBaseTypeIndex { get; private set; } = new Dictionary<int, int>();
        public ulong ManagedObjectMemoryUsage { private set; get; }
        public ulong AbandonedManagedObjectMemoryUsage { private set; get; }
        public ulong ActiveHeapMemoryUsage { private set; get; }
        public ulong ActiveHeapMemoryEmptySpace { private set; get; }
        public ulong AbandonedManagedObjectActiveHeapMemoryUsage { private set; get; }
        // ConnectionsMappedToUnifiedIndex and ConnectionsMappedToNativeIndex are derived structure used in accelerating searches in the details view
        public Dictionary<long, List<int>> ConnectionsToMappedToUnifiedIndex { private set; get; } = new Dictionary<long, List<int>>();
        public Dictionary<long, List<int>> ConnectionsFromMappedToUnifiedIndex { private set; get; } = new Dictionary<long, List<int>>();
        public Dictionary<long, List<int>> ConnectionsMappedToNativeIndex { private set; get; } = new Dictionary<long, List<int>>();

#if DEBUG_VALIDATION
        // This Dictionary block is here to make the investigations for PROF-2420 easier.
        public Dictionary<int, ManagedObjectInfo> InvalidManagedObjectsReportedViaGCHandles { private set; get; } = new Dictionary<int, ManagedObjectInfo>();
#endif

        public ManagedData(long rawGcHandleCount, long rawConnectionsCount)
        {
            //compute initial block counts for larger snapshots
            ManagedObjects = new BlockList<ManagedObjectInfo>(k_ManagedObjectBlockSize, rawGcHandleCount);
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
            for (var i = 0; i < Connections.Count; i++)
            {
                var key = Connections[i].GetUnifiedIndexTo(cs);
                if (ConnectionsToMappedToUnifiedIndex.TryGetValue(key, out var unifiedIndexList))
                    unifiedIndexList.Add(i);
                else
                    ConnectionsToMappedToUnifiedIndex[key] = new List<int> { i };
            }

            for (var i = 0; i < Connections.Count; i++)
            {
                var key = Connections[i].GetUnifiedIndexFrom(cs);
                if (ConnectionsFromMappedToUnifiedIndex.TryGetValue(key, out var unifiedIndexList))
                    unifiedIndexList.Add(i);
                else
                    ConnectionsFromMappedToUnifiedIndex[key] = new List<int> { i };
            }

            for (var i = 0; i < Connections.Count; i++)
            {
                var key = Connections[i].UnityEngineNativeObjectIndex;
                if (ConnectionsMappedToNativeIndex.TryGetValue(key, out var nativeObjectList))
                    nativeObjectList.Add(i);
                else
                    ConnectionsMappedToNativeIndex[key] = new List<int> { i };
            }
        }
    }

}

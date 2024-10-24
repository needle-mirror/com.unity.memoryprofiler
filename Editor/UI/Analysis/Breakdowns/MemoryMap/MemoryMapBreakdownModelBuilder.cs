#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Builds an MemoryMapBreakdownModel.
    class MemoryMapBreakdownModelBuilder
    {
        int m_ItemId;

        public MemoryMapBreakdownModelBuilder()
        {
            m_ItemId = 0;
        }

        public MemoryMapBreakdownModel Build(CachedSnapshot snapshot, in BuildArgs args)
        {
            if (!CanBuildBreakdownForSnapshot(snapshot))
                throw new UnsupportedSnapshotVersionException(snapshot);

            List<TreeViewItemData<MemoryMapBreakdownModel.ItemData>> roots = null;
            ConvertToTreeViewRecursive(snapshot, args, null, -1, ref roots);
            return new MemoryMapBreakdownModel(roots, GetTotalMemorySize(snapshot));
        }

        bool CanBuildBreakdownForSnapshot(CachedSnapshot snapshot)
        {
            return true;
        }

        void ConvertToTreeViewRecursive(CachedSnapshot snapshot, in BuildArgs args, CachedSnapshot.SourceIndex? currentSystemRegion, long parentIndex, ref List<TreeViewItemData<MemoryMapBreakdownModel.ItemData>> _output)
        {
            var filterArgs = args;
            var data = snapshot.EntriesMemoryMap.Data;
            var output = new List<TreeViewItemData<MemoryMapBreakdownModel.ItemData>>();
            snapshot.EntriesMemoryMap.ForEachChild(parentIndex, (index, address, size, source) =>
            {
                var item = data[index];

                var name = snapshot.EntriesMemoryMap.GetName(index);
                if (!NamePassesFilter(name, filterArgs.NameFilter))
                    return;

                // Track current/parent system region for resident memory calculations
                var systemRegion = currentSystemRegion;
                if (!systemRegion.HasValue && item.Source.Id == CachedSnapshot.SourceIndex.SourceId.SystemMemoryRegion)
                    systemRegion = item.Source;

                // When the first children starts not at the same address as
                // its parent, ForEachChild reports special "fake" reserved
                // children with the same index as its parent
                var parentHeadReserved = parentIndex == index;

                // Generate nodes for all children spans
                List<TreeViewItemData<MemoryMapBreakdownModel.ItemData>> children = null;
                if (!parentHeadReserved && (item.ChildrenCount > 0))
                    ConvertToTreeViewRecursive(snapshot, filterArgs, systemRegion, index, ref children);

                var residentSize = 0UL;
                if (systemRegion.HasValue && snapshot.HasSystemMemoryResidentPages)
                    residentSize = snapshot.SystemMemoryResidentPages.CalculateResidentMemory(snapshot, systemRegion.Value.Index, item.Address, size, item.Source.Id);

                var treeNode = new MemoryMapBreakdownModel.ItemData(
                    name,
                    item.Address,
                    new MemorySize(size, residentSize),
                    GetDataSourceTypeName(snapshot, index, parentHeadReserved),
                    item.Source);
                output.Add(new TreeViewItemData<MemoryMapBreakdownModel.ItemData>(m_ItemId++, treeNode, children));
            });

            _output = output;
        }

        MemorySize GetTotalMemorySize(CachedSnapshot snapshot)
        {
            var totalMemorySize = new MemorySize();
            snapshot.EntriesMemoryMap.ForEachFlatWithResidentSize((index, address, size, residentSize, source) =>
            {
                totalMemorySize += new MemorySize(size, residentSize);
            });

            var memoryStats = snapshot.MetaData.TargetMemoryStats;
            if (memoryStats.HasValue && !snapshot.HasSystemMemoryRegionsInfo && (memoryStats.Value.TotalVirtualMemory > 0))
                totalMemorySize = new MemorySize(memoryStats.Value.TotalVirtualMemory, 0);

            return totalMemorySize;
        }

        string GetDataSourceTypeName(CachedSnapshot snapshot, long itemIndex, bool reservedMemSpan)
        {
            var data = snapshot.EntriesMemoryMap.Data;
            var item = data[itemIndex];

            switch (item.Source.Id)
            {
                case CachedSnapshot.SourceIndex.SourceId.SystemMemoryRegion:
                    return GetSystemRegionType(snapshot, itemIndex, reservedMemSpan);

                case CachedSnapshot.SourceIndex.SourceId.NativeMemoryRegion:
                    // See comment in ConvertToTreeViewRecursive
                    if (reservedMemSpan)
                        return "Reserved";
                    return "Unity Allocator";
                case CachedSnapshot.SourceIndex.SourceId.NativeAllocation:
                    return "Native Allocation";
                case CachedSnapshot.SourceIndex.SourceId.NativeObject:
                    var typeIndex = snapshot.NativeObjects.NativeTypeArrayIndex[item.Source.Index];
                    return snapshot.NativeTypes.TypeName[typeIndex];


                case CachedSnapshot.SourceIndex.SourceId.ManagedHeapSection:
                    // See comment in ConvertToTreeViewRecursive
                    if (reservedMemSpan)
                        return "Reserved";
                    return "Managed Heap";
                case CachedSnapshot.SourceIndex.SourceId.ManagedObject:
                    ref readonly var managedObjects = ref snapshot.CrawledData.ManagedObjects;
                    var managedTypeIndex = managedObjects[item.Source.Index].ITypeDescription;
                    if (managedTypeIndex < 0)
                        return "Managed Object";
                    return snapshot.TypeDescriptions.TypeDescriptionName[managedTypeIndex];

                case CachedSnapshot.SourceIndex.SourceId.GfxResource:
                    return "Graphics Object";

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        string GetSystemRegionType(CachedSnapshot snapshot, long itemIndex, bool reservedMemSpan)
        {
            var data = snapshot.EntriesMemoryMap.Data;
            var item = data[itemIndex];

            // Check if we have usage hint from an OS
            var pointType = snapshot.EntriesMemoryMap.GetPointType(item.Source);
            switch (pointType)
            {
                case CachedSnapshot.EntriesMemoryMapCache.PointType.Device:
                    return "Device";
                case CachedSnapshot.EntriesMemoryMapCache.PointType.Mapped:
                    return "Executables & Mapped";
                case CachedSnapshot.EntriesMemoryMapCache.PointType.Shared:
                    return "Shared";
                case CachedSnapshot.EntriesMemoryMapCache.PointType.AndroidRuntime:
                    return "Android Runtime";
            }

            // Try to guess type from children. It could be:
            // - Unity - if we have information what occupies 100% of the region
            // - Mixed - if we only known about part of the region
            // - Untracked - if we don't know anything about the region
            if (!reservedMemSpan && item.ChildrenCount > 0)
            {
                bool mixed = false;
                bool hasUnityObjects = false;
                snapshot.EntriesMemoryMap.ForEachChild(itemIndex, (index, address, size, source) =>
                {
                    var childItem = data[index];
                    switch (childItem.Source.Id)
                    {
                        case CachedSnapshot.SourceIndex.SourceId.NativeMemoryRegion:
                        case CachedSnapshot.SourceIndex.SourceId.NativeAllocation:
                        case CachedSnapshot.SourceIndex.SourceId.ManagedHeapSection:
                        case CachedSnapshot.SourceIndex.SourceId.NativeObject:
                        case CachedSnapshot.SourceIndex.SourceId.ManagedObject:
                            hasUnityObjects = true;
                            break;
                        case CachedSnapshot.SourceIndex.SourceId.SystemMemoryRegion:
                            // We encountered a section which isn't used by any Unity object
                            mixed = true;
                            break;
                        default:
                            // Make sure that any new source types which can be in
                            // memory map are registered here
                            throw new ArgumentOutOfRangeException();
                    }
                });

                if (hasUnityObjects)
                    return mixed ? "Mixed" : "Unity";
            }

            // System region with no known usage hints
            return "Untracked";
        }

        bool NamePassesFilter(string name, string nameFilter)
        {
            if (!string.IsNullOrEmpty(nameFilter))
            {
                if (string.IsNullOrEmpty(name))
                    return false;

                if (!name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        internal readonly struct BuildArgs
        {
            public BuildArgs(string nameFilter)
            {
                NameFilter = nameFilter;
            }

            public string NameFilter { get; }
        }
    }
}
#endif

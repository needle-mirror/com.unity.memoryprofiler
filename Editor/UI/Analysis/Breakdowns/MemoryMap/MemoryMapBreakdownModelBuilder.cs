using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using static Unity.MemoryProfiler.Editor.UI.TreeModelHelpers;

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

        Dictionary<int, IndexableTreeViewListAndIndex<MemoryMapBreakdownModel.ItemData>> m_IdToIndex;

        public MemoryMapBreakdownModel Build(MemoryMapBreakdownModel baseModel, IEnumerable<int> treeNodeIds = null)
        {
            var tree = new List<TreeViewItemData<MemoryMapBreakdownModel.ItemData>>();

            var totalMemory = new MemorySize();

            if (treeNodeIds != null)
            {
                var stack = new Stack<TreeViewItemData<MemoryMapBreakdownModel.ItemData>>();
                foreach (var id in treeNodeIds)
                {
                    // the only place where the base model has to be assumed as non-null.
                    var item = baseModel.RootNodes.GetItemById(ref m_IdToIndex, id);
                    stack.Push(item);
                }
                while (stack.Count > 0)
                {
                    var treeNode = stack.Pop();
                    // only add leaf nodes, add children to the stack to be processed
                    if (treeNode.hasChildren)
                    {
                        foreach (var child in treeNode.children)
                        {
                            stack.Push(child);
                        }
                    }
                    else
                    {
                        tree.Add(new TreeViewItemData<MemoryMapBreakdownModel.ItemData>(treeNode.id, treeNode.data));
                        totalMemory += treeNode.data.TotalSize;
                    }
                }
            }
            // the base model could be null, but in an empty model, there are no selections to be made anyways.
            var model = CreateDerivedModel(tree, totalMemory, baseModel);
            return model;
        }

        /// <summary>
        /// Creates a derived model, based on a provided <paramref name="baseModel"/> and some <paramref name="rootNodes"/>.
        /// The <paramref name="baseModel"/> may be null if <paramref name="rootNodes"/> is an empty list.
        /// </summary>
        /// <param name="rootNodes"></param>
        /// <param name="totalMemory"></param>
        /// <param name="baseModel"></param>
        /// <returns></returns>
        protected MemoryMapBreakdownModel CreateDerivedModel(List<TreeViewItemData<MemoryMapBreakdownModel.ItemData>> rootNodes, MemorySize totalMemory, MemoryMapBreakdownModel baseModel)
        {
            return new MemoryMapBreakdownModel(rootNodes, totalMemory);
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

                // Track current/parent system region for resident memory calculations
                var systemRegion = currentSystemRegion;
                if (!systemRegion.HasValue && item.Source.Id == CachedSnapshot.SourceIndex.SourceId.SystemMemoryRegion)
                    systemRegion = item.Source;

                // When the first children starts not at the same address as
                // its parent, ForEachChild reports special "fake" reserved
                // children with the same index as its parent
                var parentHeadReserved = parentIndex == index;

                // Get type early for filtering
                var itemType = GetDataSourceTypeName(snapshot, index, parentHeadReserved);

                // Get address string for filtering
                var addressString = $"{item.Address:X16}";

                // Use scoped filtering for proper hierarchical search support
                // Search across Name, Address, and Type columns
                using var nameScope = filterArgs.SearchFilter?.OpenScope(name, snapshot);
                using var addressScope = filterArgs.SearchFilter?.OpenScope(addressString, snapshot);
                using var typeScope = filterArgs.SearchFilter?.OpenScope(itemType, snapshot);

                // Generate nodes for all children spans (children inherit the open scope)
                List<TreeViewItemData<MemoryMapBreakdownModel.ItemData>> children = null;
                if (!parentHeadReserved && (item.ChildrenCount > 0))
                    ConvertToTreeViewRecursive(snapshot, filterArgs, systemRegion, index, ref children);

                // Only after processing children can we determine if this node should be included:
                // - If we have matching children, include this parent node to show the hierarchy
                // - If no filter is active, include all nodes
                // - If this is a leaf node (no children possible), check if the node's scope itself matches 
                bool isLeafNode = parentHeadReserved || item.ChildrenCount == 0;
                bool hasMatchingChildren = children != null && children.Count > 0;
                bool nodeMatchesFilter = filterArgs.SearchFilter?.CurrentScopePasses ?? true;

                bool shouldInclude = hasMatchingChildren ||                      // Parent of matching children
                                    (filterArgs.SearchFilter == null) ||          // No filter active
                                    (isLeafNode && nodeMatchesFilter);            // Leaf node that matches

                if (shouldInclude)
                {
                    var residentSize = 0UL;
                    if (systemRegion.HasValue && snapshot.HasSystemMemoryResidentPages)
                        residentSize = snapshot.SystemMemoryResidentPages.CalculateResidentMemory(snapshot, systemRegion.Value.Index, item.Address, size, item.Source.Id);

                    var treeNode = new MemoryMapBreakdownModel.ItemData(
                        name,
                        item.Address,
                        new MemorySize(size, residentSize),
                        itemType,
                        item.Source);
                    output.Add(new TreeViewItemData<MemoryMapBreakdownModel.ItemData>(m_ItemId++, treeNode, children));
                }
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
                    // See comment in ConvertToTreeViewRecursive about "fake" reserved children
                    if (reservedMemSpan)
                        return "Reserved";
                    return "Unity Allocator";
                case CachedSnapshot.SourceIndex.SourceId.NativeAllocation:
                    return "Native Allocation";
                case CachedSnapshot.SourceIndex.SourceId.NativeObject:
                    var typeIndex = snapshot.NativeObjects.NativeTypeArrayIndex[item.Source.Index];
                    return snapshot.NativeTypes.TypeName[typeIndex];


                case CachedSnapshot.SourceIndex.SourceId.ManagedHeapSection:
                    // See comment in ConvertToTreeViewRecursive about "fake" reserved children
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

        internal readonly struct BuildArgs
        {
            public BuildArgs(IScopedFilter<string> searchFilter)
            {
                SearchFilter = searchFilter;
            }

            public IScopedFilter<string> SearchFilter { get; }
        }
    }
}

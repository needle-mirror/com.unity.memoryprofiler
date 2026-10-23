using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
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

        public void ClearIdToIndexCache()
        {
            TreeModelHelpers.ClearIdToIndexCache(ref m_IdToIndex);
        }

        public MemoryMapBreakdownModel Build(MemoryMapBreakdownModel baseModel, IEnumerable<int> treeNodeIdFilter = null)
        {
            var tree = new List<TreeViewItemData<MemoryMapBreakdownModel.ItemData>>();

            var totalMemory = new MemorySize();

            if (treeNodeIdFilter != null)
            {
                var stack = new Stack<TreeViewItemData<MemoryMapBreakdownModel.ItemData>>();
                foreach (var id in treeNodeIdFilter)
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
                using var nameScope = filterArgs.SearchFilter?.OpenScope(name);
                using var addressScope = filterArgs.SearchFilter?.OpenScope(addressString);
                using var typeScope = filterArgs.SearchFilter?.OpenScope(itemType);

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
                        item.Source,
                        GetContentKinds(OwnContentKinds(snapshot, item.Source), children));
                    output.Add(new TreeViewItemData<MemoryMapBreakdownModel.ItemData>(m_ItemId++, treeNode, children));
                }
            });

            _output = output;
        }

        static MemoryMapBreakdownModel.ContentKinds OwnContentKinds(CachedSnapshot snapshot, CachedSnapshot.SourceIndex source)
            => OwnContentKinds(source.Id, source.Id == CachedSnapshot.SourceIndex.SourceId.NativeAllocation
                && CachedSnapshot.NativeAllocationEntriesCache.IsVMHeapAllocation(snapshot, source.Index));

        /// <summary>
        /// The kind of memory an item of <paramref name="sourceId"/> itself represents, before its children are taken into account.
        /// Kept exhaustive without a discard arm so that a new <see cref="CachedSnapshot.SourceIndex.SourceId"/> fails to compile (CS8509).
        /// </summary>
        // CS8524 (the *unnamed* enum value isn't covered) is suppressed: every defined value is handled below, and
        // a runtime value is always a defined enum member. We deliberately keep no discard arm so that adding a new
        // named section type still trips CS8509 (a -warnaserror+ build error) — suppressing CS8524 keeps that gate while
        // not forcing a catch-all that would defeat it.
#pragma warning disable CS8524
        internal static MemoryMapBreakdownModel.ContentKinds OwnContentKinds(CachedSnapshot.SourceIndex.SourceId sourceId, bool isVMHeapAllocation) => sourceId switch
        {
            CachedSnapshot.SourceIndex.SourceId.ManagedHeapSection => MemoryMapBreakdownModel.ContentKinds.Managed,
            CachedSnapshot.SourceIndex.SourceId.ManagedObject => MemoryMapBreakdownModel.ContentKinds.Managed,
            CachedSnapshot.SourceIndex.SourceId.ManagedType => MemoryMapBreakdownModel.ContentKinds.Managed,
            CachedSnapshot.SourceIndex.SourceId.ManagedRoot => MemoryMapBreakdownModel.ContentKinds.Managed,
            CachedSnapshot.SourceIndex.SourceId.NativeAllocation => isVMHeapAllocation ? MemoryMapBreakdownModel.ContentKinds.Managed : MemoryMapBreakdownModel.ContentKinds.Native,
            CachedSnapshot.SourceIndex.SourceId.NativeMemoryRegion => MemoryMapBreakdownModel.ContentKinds.Native,
            CachedSnapshot.SourceIndex.SourceId.NativeObject => MemoryMapBreakdownModel.ContentKinds.Native,
            CachedSnapshot.SourceIndex.SourceId.NativeType => MemoryMapBreakdownModel.ContentKinds.Native,
            CachedSnapshot.SourceIndex.SourceId.NativeRootReference => MemoryMapBreakdownModel.ContentKinds.Native,
            CachedSnapshot.SourceIndex.SourceId.GfxResource => MemoryMapBreakdownModel.ContentKinds.Native,
            CachedSnapshot.SourceIndex.SourceId.SystemMemoryRegion => MemoryMapBreakdownModel.ContentKinds.None,
            CachedSnapshot.SourceIndex.SourceId.None => MemoryMapBreakdownModel.ContentKinds.None,
            CachedSnapshot.SourceIndex.SourceId.GCHandleIndex => MemoryMapBreakdownModel.ContentKinds.None,
            CachedSnapshot.SourceIndex.SourceId.MemoryLabel => MemoryMapBreakdownModel.ContentKinds.None,
            CachedSnapshot.SourceIndex.SourceId.Scene => MemoryMapBreakdownModel.ContentKinds.None,
            CachedSnapshot.SourceIndex.SourceId.Prefab => MemoryMapBreakdownModel.ContentKinds.None,
        };
#pragma warning restore CS8524

        internal static MemoryMapBreakdownModel.ContentKinds GetContentKinds(MemoryMapBreakdownModel.ContentKinds kinds, List<TreeViewItemData<MemoryMapBreakdownModel.ItemData>> children)
        {
            if (children == null)
                return kinds;

            foreach (var child in children)
            {
                kinds |= child.data.ContentKinds;
                // A nested system region is memory not claimed by any Unity allocation, which the parent shows as native.
                if (child.data.Source.Id == CachedSnapshot.SourceIndex.SourceId.SystemMemoryRegion)
                    kinds |= MemoryMapBreakdownModel.ContentKinds.Native;
            }
            return kinds;
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
                        return TextContent.MemoryMapTypeReserved;
                    return TextContent.MemoryMapTypeUnityAllocator;
                case CachedSnapshot.SourceIndex.SourceId.NativeAllocation:
                    return TextContent.MemoryMapTypeNativeAllocation;
                case CachedSnapshot.SourceIndex.SourceId.NativeObject:
                    var typeIndex = snapshot.NativeObjects.NativeTypeArrayIndex[item.Source.Index];
                    return snapshot.NativeTypes.TypeName[typeIndex];


                case CachedSnapshot.SourceIndex.SourceId.ManagedHeapSection:
                    // See comment in ConvertToTreeViewRecursive about "fake" reserved children
                    if (reservedMemSpan)
                        return TextContent.MemoryMapTypeReserved;
                    return TextContent.MemoryMapTypeManagedHeap;
                case CachedSnapshot.SourceIndex.SourceId.ManagedObject:
                    ref readonly var managedObjects = ref snapshot.CrawledData.ManagedObjects;
                    var managedTypeIndex = managedObjects[item.Source.Index].ITypeDescription;
                    if (managedTypeIndex < 0)
                        return TextContent.MemoryMapTypeManagedObject;
                    return snapshot.TypeDescriptions.TypeDescriptionName[managedTypeIndex];

                case CachedSnapshot.SourceIndex.SourceId.GfxResource:
                    return TextContent.MemoryMapTypeGraphicsObject;

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
                    return TextContent.MemoryMapTypeDevice;
                case CachedSnapshot.EntriesMemoryMapCache.PointType.Mapped:
                    return TextContent.MemoryMapTypeExecutablesAndMapped;
                case CachedSnapshot.EntriesMemoryMapCache.PointType.Shared:
                    return TextContent.MemoryMapTypeShared;
                case CachedSnapshot.EntriesMemoryMapCache.PointType.AndroidRuntime:
                    return TextContent.MemoryMapTypeAndroidRuntime;
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
                    return mixed ? TextContent.MemoryMapTypeMixed : TextContent.MemoryMapTypeUnity;
            }

            // System region with no known usage hints
            return TextContent.MemoryMapTypeUntracked;
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

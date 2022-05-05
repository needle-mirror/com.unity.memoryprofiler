#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Data model for the 'Unity Objects' breakdown.
    class UnityObjectsModel : TreeModel<UnityObjectsModel.ItemData>
    {
        public UnityObjectsModel(List<TreeViewItemData<ItemData>> treeRootNodes, Dictionary<int, string> itemTypeNamesMap, ulong totalSnapshotMemorySize)
            : base(treeRootNodes)
        {
            ItemTypeNamesMap = itemTypeNamesMap;
            TotalSnapshotMemorySize = totalSnapshotMemorySize;

            var totalMemorySize = 0UL;
            foreach (var rootItem in treeRootNodes)
            {
                totalMemorySize += rootItem.data.TotalSize;
            }
            TotalMemorySize = totalMemorySize;
        }

        // All Unity Object Type names for items in the tree. Used by items to look up their type name.
        public Dictionary<int, string> ItemTypeNamesMap { get; }

        // The total size, in bytes, of memory accounted for in the breakdown.
        public ulong TotalMemorySize { get; }

        // The total size, in bytes, of memory accounted for in the original snapshot.
        public ulong TotalSnapshotMemorySize { get; }

        // Sort the tree's data as specified by the sort descriptors.
        public void Sort(IEnumerable<SortDescriptor> sortDescriptors)
        {
            var sortComparison = BuildSortComparison(sortDescriptors);
            if (sortComparison == null)
                return;

            Sort(sortComparison);
        }

        Comparison<TreeViewItemData<ItemData>> BuildSortComparison(IEnumerable<SortDescriptor> sortDescriptors)
        {
            // Currently we only support sorting by a single property.
            SortDescriptor sortDescriptor = null;
            using (var enumerator = sortDescriptors.GetEnumerator())
            {
                if (enumerator.MoveNext())
                    sortDescriptor = enumerator.Current;
            }

            if (sortDescriptor == null)
                return null;

            var property = sortDescriptor.Property;
            var direction = sortDescriptor.Direction;
            switch (property)
            {
                case SortableItemDataProperty.Name:
                    if (direction == SortDirection.Ascending)
                        return (x, y) => string.Compare(
                            x.data.Name,
                            y.data.Name,
                            StringComparison.OrdinalIgnoreCase);
                    else
                        return (x, y) => string.Compare(
                            y.data.Name,
                            x.data.Name,
                            StringComparison.OrdinalIgnoreCase);

                case SortableItemDataProperty.TotalSize:
                    if (direction == SortDirection.Ascending)
                        return (x, y) => x.data.TotalSize.CompareTo(y.data.TotalSize);
                    else
                        return (x, y) => y.data.TotalSize.CompareTo(x.data.TotalSize);

                case SortableItemDataProperty.NativeSize:
                    if (direction == SortDirection.Ascending)
                        return (x, y) => x.data.NativeSize.CompareTo(y.data.NativeSize);
                    else
                        return (x, y) => y.data.NativeSize.CompareTo(x.data.NativeSize);

                case SortableItemDataProperty.ManagedSize:
                    if (direction == SortDirection.Ascending)
                        return (x, y) => x.data.ManagedSize.CompareTo(y.data.ManagedSize);
                    else
                        return (x, y) => y.data.ManagedSize.CompareTo(x.data.ManagedSize);

                case SortableItemDataProperty.GpuSize:
                    if (direction == SortDirection.Ascending)
                        return (x, y) => x.data.GpuSize.CompareTo(y.data.GpuSize);
                    else
                        return (x, y) => y.data.GpuSize.CompareTo(x.data.GpuSize);

                default:
                    throw new ArgumentException("Unable to sort. Unknown column name.");
            }
        }

        // The data associated with each item in the tree.
        public readonly struct ItemData
        {
            public ItemData(
                string name,
                ulong nativeSize,
                ulong managedSize,
                ulong gpuSize,
                int typeNameLookupKey,
                Action selectionProcessor,
                int childCount = 0)
            {
                Name = name;
                TotalSize = nativeSize + managedSize + gpuSize;
                NativeSize = nativeSize;
                ManagedSize = managedSize;
                GpuSize = gpuSize;
                TypeNameLookupKey = typeNameLookupKey;
                SelectionProcessor = selectionProcessor;
                ChildCount = childCount;
            }

            // The name of this item.
            public string Name { get; }

            // The total size of this item including its children. Computed by summing NativeSize, ManagedSize, and GpuSize.
            public ulong TotalSize { get; }

            // The native size of this item including its children.
            public ulong NativeSize { get; }

            // The managed size of this item including its children.
            public ulong ManagedSize { get; }

            // The GPU size of this item including its children.
            public ulong GpuSize { get; }

            // The key into the model's ItemTypeNamesMap, to retrieve this item's type name.
            public int TypeNameLookupKey { get; }

            // A callback to process the selection of this item.
            public Action SelectionProcessor { get; }

            // The number of children.
            public int ChildCount { get; }
        }

        public class SortDescriptor
        {
            public SortDescriptor(SortableItemDataProperty property, SortDirection direction)
            {
                Property = property;
                Direction = direction;
            }

            public SortableItemDataProperty Property { get; }
            public SortDirection Direction { get; }
        }

        public enum SortableItemDataProperty
        {
            Name,
            TotalSize,
            NativeSize,
            ManagedSize,
            GpuSize,
        }

        public enum SortDirection
        {
            Ascending,
            Descending,
        }
    }
}
#endif

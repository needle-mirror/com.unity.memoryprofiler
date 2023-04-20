#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Data model representing the 'AllSystemMemoryBreakdown' breakdown.
    class MemoryMapBreakdownModel : TreeModel<MemoryMapBreakdownModel.ItemData>
    {
        public MemoryMapBreakdownModel(List<TreeViewItemData<ItemData>> treeRootNodes, MemorySize totalSnapshotMemorySize)
            : base(treeRootNodes)
        {
            TotalSnapshotMemorySize = totalSnapshotMemorySize;

            var totalMemorySize = new MemorySize();
            foreach (var rootItem in treeRootNodes)
                totalMemorySize += rootItem.data.Size;
            TotalMemorySize = totalMemorySize;
        }

        // The total size, in bytes, of memory accounted for in the breakdown.
        public MemorySize TotalMemorySize { get; }

        // The total size, in bytes, of memory accounted for in the original snapshot.
        public MemorySize TotalSnapshotMemorySize { get; }

        // Sort the tree's data as specified by the sort descriptors.
        public void Sort(IEnumerable<SortDescriptor> sortDescriptors)
        {
            var sortComparison = BuildSortComparison(sortDescriptors);
            if (sortComparison == null)
                return;

            Sort(sortComparison);
        }

        // Build a sort comparison from a collection of sort descriptors.
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
                        return new Comparison<TreeViewItemData<ItemData>>(
                            (x, y) => x.data.Name.CompareTo(y.data.Name));
                    else
                        return new Comparison<TreeViewItemData<ItemData>>(
                            (x, y) => y.data.Name.CompareTo(x.data.Name));

                case SortableItemDataProperty.Address:
                    if (direction == SortDirection.Ascending)
                        return new Comparison<TreeViewItemData<ItemData>>(
                            (x, y) => x.data.Address.CompareTo(y.data.Address));
                    else
                        return new Comparison<TreeViewItemData<ItemData>>(
                            (x, y) => y.data.Address.CompareTo(x.data.Address));

                case SortableItemDataProperty.Size:
                    if (direction == SortDirection.Ascending)
                        return new Comparison<TreeViewItemData<ItemData>>(
                            (x, y) => x.data.Size.Committed.CompareTo(y.data.Size.Committed));
                    else
                        return new Comparison<TreeViewItemData<ItemData>>(
                            (x, y) => y.data.Size.Committed.CompareTo(x.data.Size.Committed));

                case SortableItemDataProperty.ResidentSize:
                    if (direction == SortDirection.Ascending)
                        return new Comparison<TreeViewItemData<ItemData>>(
                            (x, y) => x.data.Size.Resident.CompareTo(y.data.Size.Resident));
                    else
                        return new Comparison<TreeViewItemData<ItemData>>(
                            (x, y) => y.data.Size.Resident.CompareTo(x.data.Size.Resident));

                case SortableItemDataProperty.Type:
                    if (direction == SortDirection.Ascending)
                        return new Comparison<TreeViewItemData<ItemData>>(
                            (x, y) => x.data.ItemType.CompareTo(y.data.ItemType));
                    else
                        return new Comparison<TreeViewItemData<ItemData>>(
                            (x, y) => y.data.ItemType.CompareTo(x.data.ItemType));

                default:
                    throw new ArgumentException("Unable to sort. Unknown column name.");
            }
        }

        // The data associated with each item in the tree.
        public readonly struct ItemData : INamedTreeItemData
        {
            public ItemData(string name, ulong address, MemorySize size, string itemType, CachedSnapshot.SourceIndex source)
            {
                Name = name;
                Address = address;
                Size = size;
                ItemType = itemType;
                Source = source;
            }

            // The name of the element.
            public string Name { get; }

            // The memory address of the element.
            public ulong Address { get; }

            // The size of this item.
            public MemorySize Size { get; }

            public string ItemType { get; }

            // The index into the CachedSnapshot's data, to retrieve relevant item from CachedSnapshot.
            public CachedSnapshot.SourceIndex Source { get; }
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
            Address,
            Size,
            ResidentSize,
            Type
        }

        public enum SortDirection
        {
            Ascending,
            Descending,
        }
    }
}
#endif

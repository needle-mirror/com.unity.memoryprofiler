#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Data model representing the 'AllTrackedMemory' breakdown.
    class AllTrackedMemoryModel : TreeModel<AllTrackedMemoryModel.ItemData>
    {
        public AllTrackedMemoryModel(List<TreeViewItemData<ItemData>> treeRootNodes, ulong totalSnapshotMemorySize)
            : base(treeRootNodes)
        {
            TotalSnapshotMemorySize = totalSnapshotMemorySize;

            var totalMemorySize = 0UL;
            foreach (var rootItem in treeRootNodes)
            {
                totalMemorySize += rootItem.data.Size;
            }
            TotalMemorySize = totalMemorySize;
        }

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
                        return (x, y) => string.Compare(
                            x.data.Name,
                            y.data.Name,
                            StringComparison.OrdinalIgnoreCase);
                    else
                        return (x, y) => string.Compare(
                            y.data.Name,
                            x.data.Name,
                            StringComparison.OrdinalIgnoreCase);

                case SortableItemDataProperty.Size:
                    if (direction == SortDirection.Ascending)
                        return (x, y) => x.data.Size.CompareTo(y.data.Size);
                    else
                        return (x, y) => y.data.Size.CompareTo(x.data.Size);

                default:
                    throw new ArgumentException("Unable to sort. Unknown column name.");
            }
        }

        // The data associated with each item in the tree.
        public readonly struct ItemData
        {
            public ItemData(
                string name,
                ulong size,
                Action selectionProcessor = null,
                int childCount = 0)
            {
                Name = name;
                Size = size;
                SelectionProcessor = selectionProcessor;
                ChildCount = childCount;
            }

            // The name of this item.
            public string Name { get; }

            // The total size of this item including its children.
            public ulong Size { get; }

            // The number of children.
            public int ChildCount { get; }

            // A callback to process the selection of this item.
            public Action SelectionProcessor { get; }
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
                Size,
        }

        public enum SortDirection
        {
            Ascending,
                Descending,
        }
    }
}
#endif

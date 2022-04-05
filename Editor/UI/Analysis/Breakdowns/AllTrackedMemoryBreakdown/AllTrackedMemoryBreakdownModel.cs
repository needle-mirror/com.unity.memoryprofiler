#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Data model representing the 'AllTrackedMemory' breakdown.
    class AllTrackedMemoryBreakdownModel : TreeModel<AllTrackedMemoryBreakdownModel.ItemData>
    {
        public AllTrackedMemoryBreakdownModel(List<TreeViewItemData<ItemData>> treeRootNodes, ulong totalSnapshotMemorySize)
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
                        return new Comparison<TreeViewItemData<ItemData>>(
                            (x, y) => x.data.Name.CompareTo(y.data.Name));
                    else
                        return new Comparison<TreeViewItemData<ItemData>>(
                            (x, y) => y.data.Name.CompareTo(x.data.Name));

                case SortableItemDataProperty.Size:
                    if (direction == SortDirection.Ascending)
                        return new Comparison<TreeViewItemData<ItemData>>(
                            (x, y) => x.data.Size.CompareTo(y.data.Size));
                    else
                        return new Comparison<TreeViewItemData<ItemData>>(
                            (x, y) => y.data.Size.CompareTo(x.data.Size));

                default:
                    throw new ArgumentException("Unable to sort. Unknown column name.");
            }
        }

        // The data associated with each item in the tree.
        public readonly struct ItemData
        {
            public ItemData(string name, ulong size, int childCount = 0, CachedSnapshotDataIndex dataIndex = default)
            {
                Name = name;
                Size = size;
                ChildCount = childCount;
                DataIndex = dataIndex;
            }

            // The name of this item.
            public string Name { get; }

            // The total size of this item including its children.
            public ulong Size { get; }

            // The number of children.
            public int ChildCount { get; }

            // The index into the CachedSnapshot's data, to retrieve relevant item from CachedSnapshot.
            public CachedSnapshotDataIndex DataIndex { get; }
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

        public readonly struct CachedSnapshotDataIndex
        {
            CachedSnapshotDataIndex(long index, Type dataType)
            {
                Index = index;
                DataType = dataType;
            }

            public long Index { get; }

            public Type DataType { get; }

            public static CachedSnapshotDataIndex FromNativeObjectIndex(long nativeObjectIndex)
            {
                return new CachedSnapshotDataIndex(nativeObjectIndex, Type.NativeObject);
            }

            public static CachedSnapshotDataIndex FromNativeTypeIndex(long nativeTypeIndex)
            {
                return new CachedSnapshotDataIndex(nativeTypeIndex, Type.NativeType);
            }

            public static CachedSnapshotDataIndex FromManagedObjectIndex(long managedObjectIndex)
            {
                return new CachedSnapshotDataIndex(managedObjectIndex, Type.ManagedObject);
            }

            public static CachedSnapshotDataIndex FromManagedTypeIndex(long managedTypeIndex)
            {
                return new CachedSnapshotDataIndex(managedTypeIndex, Type.ManagedType);
            }

            public enum Type
            {
                Invalid,
                NativeObject,
                NativeType,
                ManagedObject,
                ManagedType,
            }
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

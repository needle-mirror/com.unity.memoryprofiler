#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// TODO:
    ///  [System Regions]
    ///  - [NativeMemoryRegions]
    ///      - NativeAllocations
    ///      - SortedNativeAllocations -> find root for prettier name / area or object
    ///  - [ManagedHeapSections]
    ///      - SortedManagedObjects
    ///  - [ManagedStacks]
    ///      - Empty
    ///  - [NativeGfxResourceReferences]
    ///      - (detailed by itself)
    ///
    /// * check sorted version, as it might have more relations information
    ///
    /// NativeRootReferenceEntriesCache -> has accumulated size


    // Data model representing the 'AllSystemMemoryBreakdown' breakdown.
    class AllSystemMemoryBreakdownModel : TreeModel<AllSystemMemoryBreakdownModel.ItemData>
    {
        public AllSystemMemoryBreakdownModel(List<TreeViewItemData<ItemData>> treeRootNodes, ulong totalCommitted, ulong totalResident, ulong[] committedByType, ulong[] residentByType)
            : base(treeRootNodes)
        {
            TotalCommitted = totalCommitted;
            TotalResident = totalResident;
            CommittedByType = committedByType;
            ResidentByType = residentByType;
        }

        // The total size, in bytes, of memory committed by the process.
        public ulong TotalCommitted { get; }

        // The total size, in bytes, of memory physically used by the process.
        public ulong TotalResident { get; }

        // Breakdown of total committed by type.
        public ulong[] CommittedByType { get; }

        // Breakdown of total resident by type.
        public ulong[] ResidentByType { get; }

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
                            (x, y) => x.data.Size.CompareTo(y.data.Size));
                    else
                        return new Comparison<TreeViewItemData<ItemData>>(
                            (x, y) => y.data.Size.CompareTo(x.data.Size));

                case SortableItemDataProperty.ResidentSize:
                    if (direction == SortDirection.Ascending)
                        return new Comparison<TreeViewItemData<ItemData>>(
                            (x, y) => x.data.ResidentSize.CompareTo(y.data.ResidentSize));
                    else
                        return new Comparison<TreeViewItemData<ItemData>>(
                            (x, y) => y.data.ResidentSize.CompareTo(x.data.ResidentSize));

                case SortableItemDataProperty.Type:
                    if (direction == SortDirection.Ascending)
                        return new Comparison<TreeViewItemData<ItemData>>(
                            (x, y) => x.data.DataType.CompareTo(y.data.DataType));
                    else
                        return new Comparison<TreeViewItemData<ItemData>>(
                            (x, y) => y.data.DataType.CompareTo(x.data.DataType));

                default:
                    throw new ArgumentException("Unable to sort. Unknown column name.");
            }
        }

        // The data associated with each item in the tree.
        public readonly struct ItemData
        {
            public enum Type
            {
                Unknown,
                MixedTypes,
                ///
                MappedFile,
                SharedMemory,
                DeviceMemory,
                UnityNativeRegion,
                UnityManagedHeap,
                UnityManagedStack,
                UnityManagedDomain,
                ///
                Count
            }

            public ItemData(string name, ulong address, ulong size, ulong residentSize, Type dataType, CachedSnapshotDataIndex dataIndex = default)
            {
                Name = name;
                Address = address;
                Size = size;
                ResidentSize = residentSize;
                DataType = dataType;
                DataIndex = dataIndex;
            }

            // The name of the element.
            public string Name { get; }

            // The memory address of the element.
            public ulong Address { get; }

            // The size of this item.
            public ulong Size { get; }

            // The resident part size of this item.
            public ulong ResidentSize { get; }

            public Type DataType { get; }

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
            CachedSnapshotDataIndex(long index, Source dataSource)
            {
                Index = index;
                DataSource = dataSource;
            }

            public long Index { get; }

            public Source DataSource { get; }

            public static CachedSnapshotDataIndex FromSystemMemoryRegionIndex(long index)
            {
                return new CachedSnapshotDataIndex(index, Source.SystemMemoryRegion);
            }

            public static CachedSnapshotDataIndex FromNativeMemoryRegionIndex(long index)
            {
                return new CachedSnapshotDataIndex(index, Source.NativeMemoryRegion);
            }

            public static CachedSnapshotDataIndex FromManagedHeapIndex(long index)
            {
                return new CachedSnapshotDataIndex(index, Source.ManagedHeap);
            }

            public static CachedSnapshotDataIndex FromManagedStackIndex(long index)
            {
                return new CachedSnapshotDataIndex(index, Source.ManagedStack);
            }

            public enum Source
            {
                Invalid,
                SystemMemoryRegion,
                NativeMemoryRegion,
                ManagedHeap,
                ManagedStack
            }
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

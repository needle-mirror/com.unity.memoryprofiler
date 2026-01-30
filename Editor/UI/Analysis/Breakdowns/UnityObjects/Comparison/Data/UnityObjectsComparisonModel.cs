using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Data model representing the 'Unity Objects' comparison breakdown.
    class UnityObjectsComparisonModel
        : TreeModel<UnityObjectsComparisonModel.ItemData>,
        IComparisonTreeModel<UnityObjectsComparisonModel.ItemData, UnityObjectsModel>
    {
        public const string AssemblyNameDisambiguationSeparator = " (";

        public UnityObjectsComparisonModel(
            UnityObjectsModel baseModel,
            UnityObjectsModel comparedModel,
            List<TreeViewItemData<ItemData>> treeRootNodes,
            MemorySize totalSnapshotAMemorySize,
            MemorySize totalSnapshotBMemorySize)
            : base(treeRootNodes)
        {
            var totalMemorySizeA = new MemorySize();
            var totalMemorySizeB = new MemorySize();
            var largestAbsoluteSizeDelta = 0L;
            foreach (var rootItem in treeRootNodes)
            {
                totalMemorySizeA += rootItem.data.TotalSizeInA;
                totalMemorySizeB += rootItem.data.TotalSizeInB;

                var absoluteSizeDelta = Math.Abs(rootItem.data.SizeDelta);
                largestAbsoluteSizeDelta = Math.Max(absoluteSizeDelta, largestAbsoluteSizeDelta);
            }

            TotalSizeA = totalMemorySizeA;
            TotalSizeB = totalMemorySizeB;
            LargestAbsoluteSizeDelta = largestAbsoluteSizeDelta;

            // Workaround for inflated resident due to fake gfx resources
            TotalSnapshotSizeA = MemorySize.Max(totalSnapshotAMemorySize, totalMemorySizeA);
            TotalSnapshotSizeB = MemorySize.Max(totalSnapshotBMemorySize, totalMemorySizeB);
            BaseModel = baseModel;
            ComparedModel = comparedModel;
        }

        // The total size, in bytes, of memory accounted for in snapshot A.
        public MemorySize TotalSizeA { get; }

        // The total size, in bytes, of memory accounted for in snapshot B.
        public MemorySize TotalSizeB { get; }

        // The total size, in bytes, of all memory in snapshot A.
        public MemorySize TotalSnapshotSizeA { get; }

        // The total size, in bytes, of all memory in snapshot B.
        public MemorySize TotalSnapshotSizeB { get; }

        // The largest absolute size delta (difference), in bytes, between the two snapshots of any single item.
        public long LargestAbsoluteSizeDelta { get; }

        public UnityObjectsModel BaseModel { get; }
        public UnityObjectsModel ComparedModel { get; }

        // The data associated with each item in the tree. Represents a single difference between two snapshots.
        public readonly struct ItemData : INamedTreeItemData
        {
            public ItemData(
                string name,
                MemorySize totalSizeInA,
                MemorySize totalSizeInB,
                uint countInA,
                uint countInB,
                string nativeTypeName,
                Action selectionProcessor,
                int childCount = 0)
            {
                Name = name;
                SizeDelta = Convert.ToInt64(totalSizeInB.Committed) - Convert.ToInt64(totalSizeInA.Committed);
                TotalSizeInA = totalSizeInA;
                TotalSizeInB = totalSizeInB;
                CountInA = countInA;
                CountInB = countInB;
                CountDelta = Convert.ToInt32(countInB) - Convert.ToInt32(countInA);
                NativeTypeName = nativeTypeName;
                SelectionProcessor = selectionProcessor;
                ChildCount = childCount;
            }

            // The name of this item.
            public string Name { get; }

            // The difference in size, in bytes, between A and B. Computed as B - A.
            public long SizeDelta { get; }

            // The total size in bytes of this item in A, including its children.
            public MemorySize TotalSizeInA { get; }

            // The total size of this item in B, including its children.
            public MemorySize TotalSizeInB { get; }

            // The number of this item in A.
            public uint CountInA { get; }

            // The number of this item in B.
            public uint CountInB { get; }

            // The difference in count between A and B. Computed as B - A.
            public int CountDelta { get; }

            // The name of the Unity Object Type associated with this item.
            public string NativeTypeName { get; }

            // A callback to process the selection of this item.
            public Action SelectionProcessor { get; }

            // The number of children.
            public int ChildCount { get; }

            // Has this item or any of its children changed? A change can come from a change in size or a change in count.
            public bool HasChanged => TotalSizeInA != TotalSizeInB || CountInA != CountInB;
        }
    }
}

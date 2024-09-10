#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Data model representing the 'Unity Objects' comparison breakdown.
    class UnityObjectsComparisonModel : TreeModel<UnityObjectsComparisonModel.ItemData>
    {
        public const string AssemblyNameDisambiguationSeparator = " (";

        public UnityObjectsComparisonModel(
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

            TotalMemorySizeA = totalMemorySizeA;
            TotalMemorySizeB = totalMemorySizeB;
            LargestAbsoluteSizeDelta = largestAbsoluteSizeDelta;

            // Workaround for inflated resident due to fake gfx resources
            TotalSnapshotAMemorySize = MemorySize.Max(totalSnapshotAMemorySize, totalMemorySizeA);
            TotalSnapshotBMemorySize = MemorySize.Max(totalSnapshotBMemorySize, totalMemorySizeB);
        }

        // The total size, in bytes, of memory accounted for in snapshot A.
        public MemorySize TotalMemorySizeA { get; }

        // The total size, in bytes, of memory accounted for in snapshot B.
        public MemorySize TotalMemorySizeB { get; }

        // The total size, in bytes, of all memory in snapshot A.
        public MemorySize TotalSnapshotAMemorySize { get; }

        // The total size, in bytes, of all memory in snapshot B.
        public MemorySize TotalSnapshotBMemorySize { get; }

        // The largest absolute size delta (difference), in bytes, between the two snapshots of any single item.
        public long LargestAbsoluteSizeDelta { get; }

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
#endif

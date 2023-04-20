#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // General model for comparison of two tree-based models.
    class ComparisonTableModel : TreeModel<ComparisonTableModel.ComparisonData>
    {
        public ComparisonTableModel(
            List<TreeViewItemData<ComparisonData>> rootNodes,
            ulong totalSnapshotSizeA,
            ulong totalSnapshotSizeB,
            long largestAbsoluteSizeDelta)
            : base(rootNodes)
        {

            var totalSizeA = 0UL;
            var totalSizeB = 0UL;
            foreach (var rootItem in rootNodes)
            {
                totalSizeA += rootItem.data.TotalSizeInA;
                totalSizeB += rootItem.data.TotalSizeInB;
            }

            TotalSizeA = totalSizeA;
            TotalSizeB = totalSizeB;
            TotalSnapshotSizeA = totalSnapshotSizeA;
            TotalSnapshotSizeB = totalSnapshotSizeB;
            LargestAbsoluteSizeDelta = largestAbsoluteSizeDelta;
        }

        // The total size of memory in the model from A, in bytes; the sum of all tree items' TotalSizeInA field.
        public ulong TotalSizeA { get; }

        // The total size of memory in the model from B, in bytes; the sum of all tree items' TotalSizeInB field.
        public ulong TotalSizeB { get; }

        // The size of all memory in A's source snapshot, in bytes.
        public ulong TotalSnapshotSizeA { get; }

        // The size of all memory in B's source snapshot, in bytes.
        public ulong TotalSnapshotSizeB { get; }

        // The largest absolute size delta of any single item in the model's tree, in bytes.
        public long LargestAbsoluteSizeDelta { get; }

        // The data associated with each item in the tree.
        public readonly struct ComparisonData : INamedTreeItemData
        {
            public ComparisonData(
                string name,
                ulong totalSizeInA,
                ulong totalSizeInB,
                uint countInA,
                uint countInB,
                List<string> itemPath)
            {
                Name = name;
                SizeDelta = Convert.ToInt64(totalSizeInB) - Convert.ToInt64(totalSizeInA);
                TotalSizeInA = totalSizeInA;
                TotalSizeInB = totalSizeInB;
                CountInA = countInA;
                CountInB = countInB;
                CountDelta = Convert.ToInt32(countInB) - Convert.ToInt32(countInA);
                HasChanged = TotalSizeInA != TotalSizeInB || CountInA != CountInB;
                ItemPath = itemPath;
            }

            // The name of this item.
            public string Name { get; }

            // The difference in size, in bytes, between A and B. Computed as B - A.
            public long SizeDelta { get; }

            // The total size in bytes of this item in A, including its children.
            public ulong TotalSizeInA { get; }

            // The total size of this item in B, including its children.
            public ulong TotalSizeInB { get; }

            // The number of this item in A.
            public uint CountInA { get; }

            // The number of this item in B.
            public uint CountInB { get; }

            // The difference in count between A and B. Computed as B - A.
            public int CountDelta { get; }

            // Has this item or any of its children changed?
            public bool HasChanged { get; }

            // Item path.
            public List<string> ItemPath { get; }
        }
    }
}
#endif

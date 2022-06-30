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

        // The data associated with each item in the tree.
        public readonly struct ItemData : IComparableItemData
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
    }
}
#endif

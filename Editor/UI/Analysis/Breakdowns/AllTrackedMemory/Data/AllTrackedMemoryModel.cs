#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Data model representing the 'AllTrackedMemory' breakdown.
    class AllTrackedMemoryModel : TreeModel<AllTrackedMemoryModel.ItemData>
    {
        public AllTrackedMemoryModel(List<TreeViewItemData<ItemData>> treeRootNodes, MemorySize totalSnapshotMemorySize, Action<int, ItemData> selectionProcessor)
            : base(treeRootNodes)
        {
            SelectionProcessor = selectionProcessor;

            var totalMemorySize = new MemorySize();
            foreach (var rootItem in treeRootNodes)
            {
                if (rootItem.data.Name == AllTrackedMemoryModelBuilder.GraphicsGroupName)
                    TotalGraphicsMemorySize += rootItem.data.Size;

                totalMemorySize += rootItem.data.Size;
            }
            TotalMemorySize = totalMemorySize;

            // Workaround for inflated resident due to gfx resources accounted as fully resident.
            TotalSnapshotMemorySize = MemorySize.Max(totalSnapshotMemorySize, totalMemorySize);
        }

        // The total size, in bytes, of memory accounted for in the breakdown.
        // Includes graphics memory.
        public MemorySize TotalMemorySize { get; }

        // The total size, in bytes, of graphics memory accounted for in the breakdown.
        public MemorySize TotalGraphicsMemorySize { get; }

        // The total size, in bytes, of memory accounted for in the original snapshot.
        // Includes graphics memory.
        public MemorySize TotalSnapshotMemorySize { get; }

        // A callback to process the selection of an item.
        public Action<int, ItemData> SelectionProcessor { get; }

        // The data associated with each item in the tree.
        public readonly struct ItemData : IPrivateComparableItemData, INamedTreeItemData
        {
            public ItemData(
                string name,
                MemorySize size,
                CachedSnapshot.SourceIndex source,
                int childCount = 0)
            {
                Name = name;
                Size = size;
                Source = source;
                ChildCount = childCount;
                Unreliable = false;
            }

            // The name of this item.
            public string Name { get; }

            // The total size of this item including its children.
            public MemorySize Size { get; }

            // The source of this item in memory snapshot.
            public CachedSnapshot.SourceIndex Source { get; }

            // The number of children.
            public int ChildCount { get; }

            // The item information is unreliable and being shown "for information"
            public bool Unreliable { get; init; }
        }
    }
}
#endif

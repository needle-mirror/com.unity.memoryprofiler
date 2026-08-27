using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Data model representing the 'AllTrackedMemory' breakdown.
    class AllTrackedMemoryModel : TreeModel<AllTrackedMemoryModel.ItemData>
    {
        public AllTrackedMemoryModel(List<TreeViewItemData<ItemData>> treeRootNodes, MemorySize totalSnapshotMemorySize)
            : base(treeRootNodes)
        {
            var totalMemorySize = new MemorySize();
            foreach (var rootItem in treeRootNodes)
            {
                if (rootItem.data.Name == AllTrackedMemoryModelBuilder.GraphicsGroupName)
                    TotalGraphicsMemorySize += rootItem.data.TotalSize;

                totalMemorySize += rootItem.data.TotalSize;
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

        // The data associated with each item in the tree.
        public readonly struct ItemData : IPrivateComparableItemData
        {
            public ItemData(
                string name,
                MemorySize size,
                CachedSnapshot.SourceIndex source,
                int childCount = 0,
                ObjectStatus status = ObjectStatus.None)
            {
                Name = name;
                TotalSize = size;
                Source = source;
                ChildCount = childCount;
                Unreliable = false;
                WarningInfo = ObjectStatusWarningInfo.FromStatus(status);
            }

            // The name of this item.
            public string Name { get; }

            // The total size of this item including its children.
            public MemorySize TotalSize { get; }

            // The source of this item in memory snapshot.
            public CachedSnapshot.SourceIndex Source { get; }

            // The number of children.
            public int ChildCount { get; }

            // The item information is unreliable and being shown "for information"
            public bool Unreliable { get; init; }

            // Aggregated warning info: status bitmask and warning-object count for this subtree.
            // For leaf nodes: FromStatus(status). For group nodes: OR of children, set via init.
            public ObjectStatusWarningInfo WarningInfo { get; init; }
        }
    }
}

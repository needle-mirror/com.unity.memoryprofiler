#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Data model for the 'Unity Objects' breakdown.
    class UnityObjectsModel : TreeModel<UnityObjectsModel.ItemData>
    {
        public UnityObjectsModel(List<TreeViewItemData<ItemData>> treeRootNodes, MemorySize totalSnapshotMemorySize, Action<int, ItemData> selectionProcessor)
            : base(treeRootNodes)
        {
            TotalSnapshotMemorySize = totalSnapshotMemorySize;
            SelectionProcessor = selectionProcessor;

            var totalMemorySize = new MemorySize();
            foreach (var rootItem in treeRootNodes)
                totalMemorySize += rootItem.data.TotalSize;

            TotalMemorySize = totalMemorySize;

            // Workaround for inflated resident due to fake gfx resources
            TotalSnapshotMemorySize = MemorySize.Max(totalSnapshotMemorySize, totalMemorySize);
        }

        // The total size, in bytes, of memory accounted for in the breakdown.
        public MemorySize TotalMemorySize { get; }

        // The total size, in bytes, of memory accounted for in the original snapshot.
        public MemorySize TotalSnapshotMemorySize { get; }

        // A callback to process the selection of this item.
        public Action<int, ItemData> SelectionProcessor { get; }

        // The data associated with each item in the tree.
        public readonly struct ItemData : INamedTreeItemData
        {
            public ItemData(
                string name,
                MemorySize nativeSize,
                MemorySize managedSize,
                MemorySize gpuSize,
                CachedSnapshot.SourceIndex source,
                int childCount = 0)
            {
                Name = name;
                TotalSize = nativeSize + managedSize + gpuSize;
                NativeSize = nativeSize;
                ManagedSize = managedSize;
                GpuSize = gpuSize;
                Source = source;
                ChildCount = childCount;
            }

            // The name of this item.
            public string Name { get; }

            // The total size of this item including its children. Computed by summing NativeSize, ManagedSize, and GpuSize.
            public MemorySize TotalSize { get; }

            // The native size of this item including its children.
            public MemorySize NativeSize { get; }

            // The managed size of this item including its children.
            public MemorySize ManagedSize { get; }

            // The GPU size of this item including its children.
            public MemorySize GpuSize { get; }

            // The key into the cached snapshot, to retrieve this item's related data.
            public CachedSnapshot.SourceIndex Source { get; }

            // The number of children.
            public int ChildCount { get; }
        }
    }
}
#endif

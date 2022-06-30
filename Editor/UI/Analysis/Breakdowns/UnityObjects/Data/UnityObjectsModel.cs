#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Data model for the 'Unity Objects' breakdown.
    class UnityObjectsModel : TreeModel<UnityObjectsModel.ItemData>
    {
        public UnityObjectsModel(List<TreeViewItemData<ItemData>> treeRootNodes, Dictionary<int, string> itemTypeNamesMap, ulong totalSnapshotMemorySize)
            : base(treeRootNodes)
        {
            ItemTypeNamesMap = itemTypeNamesMap;
            TotalSnapshotMemorySize = totalSnapshotMemorySize;

            var totalMemorySize = 0UL;
            foreach (var rootItem in treeRootNodes)
            {
                totalMemorySize += rootItem.data.TotalSize;
            }
            TotalMemorySize = totalMemorySize;
        }

        // All Unity Object Type names for items in the tree. Used by items to look up their type name.
        public Dictionary<int, string> ItemTypeNamesMap { get; }

        // The total size, in bytes, of memory accounted for in the breakdown.
        public ulong TotalMemorySize { get; }

        // The total size, in bytes, of memory accounted for in the original snapshot.
        public ulong TotalSnapshotMemorySize { get; }

        // The data associated with each item in the tree.
        public readonly struct ItemData
        {
            public ItemData(
                string name,
                ulong nativeSize,
                ulong managedSize,
                ulong gpuSize,
                int typeNameLookupKey,
                Action selectionProcessor,
                int childCount = 0)
            {
                Name = name;
                TotalSize = nativeSize + managedSize + gpuSize;
                NativeSize = nativeSize;
                ManagedSize = managedSize;
                GpuSize = gpuSize;
                TypeNameLookupKey = typeNameLookupKey;
                SelectionProcessor = selectionProcessor;
                ChildCount = childCount;
            }

            // The name of this item.
            public string Name { get; }

            // The total size of this item including its children. Computed by summing NativeSize, ManagedSize, and GpuSize.
            public ulong TotalSize { get; }

            // The native size of this item including its children.
            public ulong NativeSize { get; }

            // The managed size of this item including its children.
            public ulong ManagedSize { get; }

            // The GPU size of this item including its children.
            public ulong GpuSize { get; }

            // The key into the model's ItemTypeNamesMap, to retrieve this item's type name.
            public int TypeNameLookupKey { get; }

            // A callback to process the selection of this item.
            public Action SelectionProcessor { get; }

            // The number of children.
            public int ChildCount { get; }
        }
    }
}
#endif

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Data model representing the 'AllSystemMemoryBreakdown' breakdown.
    class MemoryMapBreakdownModel : TreeModel<MemoryMapBreakdownModel.ItemData>
    {
        public MemoryMapBreakdownModel(List<TreeViewItemData<ItemData>> treeRootNodes, MemorySize totalSnapshotMemorySize)
            : base(treeRootNodes)
        {
            TotalSnapshotMemorySize = totalSnapshotMemorySize;

            var totalMemorySize = new MemorySize();
            foreach (var rootItem in treeRootNodes)
                totalMemorySize += rootItem.data.TotalSize;
            TotalMemorySize = totalMemorySize;
        }

        // The total size, in bytes, of memory accounted for in the breakdown.
        public MemorySize TotalMemorySize { get; }

        // The total size, in bytes, of memory accounted for in the original snapshot.
        public MemorySize TotalSnapshotMemorySize { get; }

        /// <summary>
        /// Which kinds of memory an item and its (included) descendants contain.
        /// </summary>
        [Flags]
        public enum ContentKinds : byte
        {
            None = 0,
            Managed = 1 << 0,
            Native = 1 << 1,
        }

        // The data associated with each item in the tree.
        public readonly struct ItemData : IPrivateComparableItemData, IHasSourceIndex
        {
            public ItemData(string name, ulong address, MemorySize size, string itemType, CachedSnapshot.SourceIndex source, ContentKinds contentKinds)
            {
                Name = name;
                Address = address;
                TotalSize = size;
                ItemType = itemType;
                Source = source;
                ContentKinds = contentKinds;
            }

            // The name of the element.
            public string Name { get; }

            // The memory address of the element.
            public ulong Address { get; }

            // The size of this item.
            public MemorySize TotalSize { get; }

            public string ItemType { get; }

            // The index into the CachedSnapshot's data, to retrieve relevant item from CachedSnapshot.
            public CachedSnapshot.SourceIndex Source { get; }

            public ContentKinds ContentKinds { get; }
        }
    }
}

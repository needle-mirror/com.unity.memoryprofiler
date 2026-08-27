using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>Data model for the 'Unity Objects' breakdown.</summary>
    class UnityObjectsModel : TreeModel<UnityObjectsModel.ItemData>, IDisposable, IPagedFlatModel
    {
        /// <summary>
        /// Flattened, paged backing data, delegated to the shared core. Null for the hierarchical (tree) model,
        /// which holds its rows directly in RootNodes instead.
        /// </summary>
        readonly PagedFlatModel<UnityObjectsCompactRow, ItemData> m_Paged;
        /// <summary>
        /// The row store this model owns and disposes. Held directly (rather than only through m_Paged, which
        /// never disposes it) so the native sort comparer can be built from the store's row pointer.
        /// </summary>
        readonly FlattenedRowStore<UnityObjectsCompactRow> m_Store;

        /// <summary>Hierarchical (tree) model: rows are held directly in <paramref name="treeRootNodes"/>.</summary>
        public UnityObjectsModel(List<TreeViewItemData<ItemData>> treeRootNodes, MemorySize totalSnapshotMemorySize)
            : base(treeRootNodes)
        {
            var totalMemorySize = new MemorySize();
            foreach (var rootItem in treeRootNodes)
                totalMemorySize += rootItem.data.TotalSize;

            TotalMemorySize = totalMemorySize;

            // Workaround for inflated resident due to fake gfx resources
            TotalSnapshotMemorySize = MemorySize.Max(totalSnapshotMemorySize, totalMemorySize);
        }

        /// <summary>
        /// Flattened, paginated model: every row lives in <paramref name="store"/> (native), and only the current
        /// page is materialized into <see cref="TreeModel{T}.RootNodes"/> at a time via the shared
        /// <see cref="PagedFlatModel{TRow,TItemData}"/> core. The model takes ownership of the store (disposed with
        /// it). <paramref name="names"/> holds each flat row's display name, indexed by <see cref="UnityObjectsCompactRow.NameId"/>.
        /// </summary>
        public UnityObjectsModel(
            FlattenedRowStore<UnityObjectsCompactRow> store,
            List<string> names,
            int pageSize,
            MemorySize totalMemorySize,
            MemorySize totalSnapshotMemorySize)
            : base(new List<TreeViewItemData<ItemData>>())
        {
            m_Store = store;
            TotalMemorySize = totalMemorySize;
            // Workaround for inflated resident due to fake gfx resources
            TotalSnapshotMemorySize = MemorySize.Max(totalSnapshotMemorySize, totalMemorySize);

            string ResolveName(UnityObjectsCompactRow row) => names[row.NameId];
            ItemData Rehydrate(long index)
            {
                var row = store[index];
                return new ItemData(ResolveName(row), row.NativeSize, row.ManagedSize, row.GpuSize, row.Source, row.ChildCount, row.WarningInfo);
            }

            m_Paged = new PagedFlatModel<UnityObjectsCompactRow, ItemData>(store, RootNodes, pageSize, row => row.TreeId, ResolveName, Rehydrate);
        }

        /// <summary>The total size, in bytes, of memory accounted for in the breakdown.</summary>
        public MemorySize TotalMemorySize { get; }

        /// <summary>The total size, in bytes, of memory accounted for in the original snapshot.</summary>
        public MemorySize TotalSnapshotMemorySize { get; }

        /// <summary>True for the flattened, paginated model; false for the hierarchical model.</summary>
        public bool IsPaged => m_Paged != null;

        /// <summary>Number of flattened rows (0 for the hierarchical model).</summary>
        public long FlattenedRowsTotal => m_Paged?.MatchingRowCount ?? 0;

        /// <summary>Rows shown per page in the flattened view.</summary>
        public int PageSize => m_Paged?.PageSize ?? 0;

        /// <summary>Zero-based index of the page currently materialized into <see cref="TreeModel{T}.RootNodes"/>.</summary>
        public int CurrentPage => m_Paged?.CurrentPage ?? 0;

        /// <summary>Number of pages the flattened rows span; always at least 1 (1 for the hierarchical model).</summary>
        public int TotalPages => m_Paged?.TotalPages ?? 1;

        /// <summary>
        /// Re-orders the flattened rows by the given column levels (size/status; supports the table's multi-column
        /// sort while paged), via the native index comparer, then shows page 0.
        /// </summary>
        public unsafe void SortFlatColumns(ReadOnlySpan<(UnityObjectsFlatRowComparer.Column column, bool descending)> levels, bool materialize = true)
        {
            if (m_Paged == null)
                return;
            var comparer = new UnityObjectsFlatRowComparer(m_Store.GetUnsafeTypedPtr(), levels);
            m_Paged.SortFlat(comparer, materialize);
        }

        /// <summary>Re-orders the flattened rows alphabetically by name (gated by a row-count threshold), then shows page 0.</summary>
        public void SortFlatByName(bool descending, bool materialize = true) => m_Paged?.SortFlatByName(descending, materialize);

        /// <summary>
        /// Re-orders the flattened rows by an ordered list of levels that may mix the name (Description) column
        /// with typed columns at any priority, then shows page 0. See <see cref="FlatSortMixing.Sort{TColumn}"/>
        /// for the fast-path/managed-path decision this delegates to.
        /// </summary>
        public unsafe void SortFlatMixed(ReadOnlySpan<FlatSortLevel<UnityObjectsFlatRowComparer.Column>> levels, bool materialize = true)
        {
            if (m_Paged == null)
                return;

            var rows = m_Store.GetUnsafeTypedPtr();
            FlatSortMixing.Sort(
                levels,
                m_Paged.MatchingRowCount,
                (lvls, mat) => SortFlatColumns(lvls, mat),
                SortFlatByName,
                m_Paged.ResolveMatchingNames,
                m_Paged.SortFlatManaged,
                (a, b, column) => UnityObjectsFlatRowComparer.CompareColumn(column, in rows[a], in rows[b], rows[a].TotalSize, rows[b].TotalSize),
                materialize);
        }

        /// <summary>Refills <see cref="TreeModel{T}.RootNodes"/> with the rows of <paramref name="page"/>.</summary>
        public void MaterializePage(int page) => m_Paged?.MaterializePage(page);

        public void Dispose()
        {
            m_Paged?.Dispose();
            m_Store?.Dispose();
        }

        /// <summary>The data associated with each item in the tree.</summary>
        public readonly struct ItemData : IPrivateComparableItemData
        {
            public ItemData(
                string name,
                MemorySize nativeSize,
                MemorySize managedSize,
                MemorySize gpuSize,
                CachedSnapshot.SourceIndex source,
                int childCount = 0,
                ObjectStatus status = ObjectStatus.None)
            {
                Name = name;
                TotalSize = nativeSize + managedSize + gpuSize;
                NativeSize = nativeSize;
                ManagedSize = managedSize;
                GpuSize = gpuSize;
                Source = source;
                ChildCount = childCount;
                WarningInfo = ObjectStatusWarningInfo.FromStatus(status);
            }

            /// <summary>
            /// Rehydrates a flattened-view row from a <see cref="UnityObjectsCompactRow"/>, taking the aggregated
            /// <see cref="ObjectStatusWarningInfo"/> directly rather than recomputing it from a single status.
            /// </summary>
            internal ItemData(
                string name,
                MemorySize nativeSize,
                MemorySize managedSize,
                MemorySize gpuSize,
                CachedSnapshot.SourceIndex source,
                int childCount,
                ObjectStatusWarningInfo warningInfo)
            {
                Name = name;
                TotalSize = nativeSize + managedSize + gpuSize;
                NativeSize = nativeSize;
                ManagedSize = managedSize;
                GpuSize = gpuSize;
                Source = source;
                ChildCount = childCount;
                WarningInfo = warningInfo;
            }

            /// <summary>The name of this item.</summary>
            public string Name { get; }

            /// <summary>The total size of this item including its children. Computed by summing NativeSize, ManagedSize, and GpuSize.</summary>
            public MemorySize TotalSize { get; }

            /// <summary>The native size of this item including its children.</summary>
            public MemorySize NativeSize { get; }

            /// <summary>The managed size of this item including its children.</summary>
            public MemorySize ManagedSize { get; }

            /// <summary>The GPU size of this item including its children.</summary>
            public MemorySize GpuSize { get; }

            /// <summary>The key into the cached snapshot, to retrieve this item's related data.</summary>
            public CachedSnapshot.SourceIndex Source { get; }

            /// <summary>The number of children.</summary>
            public int ChildCount { get; }

            /// <summary>Aggregated warning info: status bitmask and warning-object count for this subtree. For leaf nodes: FromStatus(status). For group nodes: OR of children, set via init.</summary>
            public ObjectStatusWarningInfo WarningInfo { get; init; }
        }
    }
}

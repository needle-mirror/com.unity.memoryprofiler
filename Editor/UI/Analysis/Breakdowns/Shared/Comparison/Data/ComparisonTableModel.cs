using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using static Unity.MemoryProfiler.Editor.UI.TreeModelHelpers;

namespace Unity.MemoryProfiler.Editor.UI
{
    // General model for comparison of two tree-based models.
    class ComparisonTableModel<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>
        : TreeModel<ComparisonTableModel<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>.ComparisonData>, IDisposable, IPagedFlatModel
        where TBaseModelTreeItemData : INamedTreeItemData
    {
        /// <summary>
        /// Flattened, paged backing data, delegated to the shared core. Null for the hierarchical (tree) model,
        /// which holds its rows directly in RootNodes instead.
        /// </summary>
        readonly PagedFlatModel<ComparisonCompactRow, ComparisonData> m_Paged;
        /// <summary>The row source this model owns and disposes, holding the native store the sort comparer reads from.</summary>
        readonly ComparisonRowSource<TModelSpecificData> m_RowSource;

        public ComparisonTableModel(
            List<TreeViewItemData<ComparisonData>> rootNodes,
            TBaseModel baseModel,
            MemorySize totalSnapshotSizeA,
            TBaseModel comparedModel,
            MemorySize totalSnapshotSizeB,
            long largestAbsoluteSizeDelta)
            : base(rootNodes)
        {
            var totalSizeA = new MemorySize();
            var totalSizeB = new MemorySize();
            foreach (var rootItem in rootNodes)
            {
                totalSizeA += rootItem.data.TotalSizeInA;
                totalSizeB += rootItem.data.TotalSizeInB;
            }
            BaseModel = baseModel;
            ComparedModel = comparedModel;
            TotalSizeA = totalSizeA;
            TotalSizeB = totalSizeB;
            TotalSnapshotSizeA = totalSnapshotSizeA;
            TotalSnapshotSizeB = totalSnapshotSizeB;
            LargestAbsoluteSizeDelta = largestAbsoluteSizeDelta;
        }

        /// <summary>
        /// Flattened, paginated model: every row lives in <paramref name="rowSource"/> (native), and only the
        /// current page is materialized into <see cref="TreeModel{T}.RootNodes"/> at a time via the shared
        /// <see cref="PagedFlatModel{TRow,TItemData}"/> core. The model takes ownership of <paramref name="rowSource"/>
        /// (disposed with it). <paramref name="totalSizeA"/>/<paramref name="totalSizeB"/> must be computed by the
        /// caller over every flattened row, not just the visible page.
        /// </summary>
        public ComparisonTableModel(
            ComparisonRowSource<TModelSpecificData> rowSource,
            int pageSize,
            TBaseModel baseModel,
            MemorySize totalSnapshotSizeA,
            TBaseModel comparedModel,
            MemorySize totalSnapshotSizeB,
            long largestAbsoluteSizeDelta,
            MemorySize totalSizeA,
            MemorySize totalSizeB)
            : base(new List<TreeViewItemData<ComparisonData>>())
        {
            m_RowSource = rowSource;
            BaseModel = baseModel;
            ComparedModel = comparedModel;
            TotalSizeA = totalSizeA;
            TotalSizeB = totalSizeB;
            TotalSnapshotSizeA = totalSnapshotSizeA;
            TotalSnapshotSizeB = totalSnapshotSizeB;
            LargestAbsoluteSizeDelta = largestAbsoluteSizeDelta;

            ComparisonData Rehydrate(long index)
            {
                ref readonly var row = ref rowSource.Store[index];
                return new ComparisonData(
                    rowSource.GetName(row.NameId),
                    row.TotalSizeInA,
                    row.TotalSizeInB,
                    row.CountInA,
                    row.CountInB,
                    rowSource.GetIdsA(row.IdsAId),
                    rowSource.GetIdsB(row.IdsBId),
                    new List<string>(),
                    rowSource.GetExtra(row.ExtraId),
                    row.ChildCount,
                    row.Source,
                    row.HasChanged);
            }

            m_Paged = new PagedFlatModel<ComparisonCompactRow, ComparisonData>(
                rowSource.Store, RootNodes, pageSize, row => row.TreeId, rowSource.ResolveName, Rehydrate);
        }

        /// <summary>True for the flattened, paginated model; false for the hierarchical model.</summary>
        public bool IsPaged => m_Paged != null;

        /// <summary>Number of flattened rows (0 for the hierarchical model).</summary>
        public long FlattenedRowsTotal => m_Paged?.MatchingRowCount ?? 0;

        /// <summary>Zero-based index of the page currently materialized into <see cref="TreeModel{T}.RootNodes"/>.</summary>
        public int CurrentPage => m_Paged?.CurrentPage ?? 0;

        /// <summary>Number of pages the flattened rows span; always at least 1 (1 for the hierarchical model).</summary>
        public int TotalPages => m_Paged?.TotalPages ?? 1;

        /// <summary>
        /// Re-orders the flattened rows by the given column levels via the native index comparer, then shows page 0.
        /// </summary>
        public unsafe void SortFlatColumns(ReadOnlySpan<(ComparisonFlatRowComparer.Column column, bool descending)> levels, bool materialize = true)
        {
            if (m_Paged == null)
                return;
            var comparer = new ComparisonFlatRowComparer(m_RowSource.Store.GetUnsafeTypedPtr(), levels);
            m_Paged.SortFlat(comparer, materialize);
        }

        /// <summary>Re-orders the flattened rows alphabetically by name, then shows page 0.</summary>
        public void SortFlatByName(bool descending, bool materialize = true) => m_Paged?.SortFlatByName(descending, materialize);

        /// <summary>
        /// Re-orders the flattened rows by an ordered list of levels that may mix the name (Description) column
        /// with typed columns at any priority, then shows page 0. See <see cref="FlatSortMixing.Sort{TColumn}"/>
        /// for the fast-path/managed-path decision this delegates to.
        /// </summary>
        public unsafe void SortFlatMixed(ReadOnlySpan<FlatSortLevel<ComparisonFlatRowComparer.Column>> levels, bool materialize = true)
        {
            if (m_Paged == null)
                return;

            var rows = m_RowSource.Store.GetUnsafeTypedPtr();
            FlatSortMixing.Sort(
                levels,
                m_Paged.MatchingRowCount,
                (lvls, mat) => SortFlatColumns(lvls, mat),
                SortFlatByName,
                m_Paged.ResolveMatchingNames,
                m_Paged.SortFlatManaged,
                (a, b, column) => ComparisonFlatRowComparer.CompareColumn(column, in rows[a], in rows[b]),
                materialize);
        }

        /// <summary>Refills <see cref="TreeModel{T}.RootNodes"/> with the rows of <paramref name="page"/>.</summary>
        public void MaterializePage(int page) => m_Paged?.MaterializePage(page);

        public void Dispose()
        {
            m_Paged?.Dispose();
            m_RowSource?.Dispose();
        }

        // The total size of memory in the model from A, in bytes; the sum of all tree items' TotalSizeInA field.
        public MemorySize TotalSizeA { get; }

        // The total size of memory in the model from B, in bytes; the sum of all tree items' TotalSizeInB field.
        public MemorySize TotalSizeB { get; }

        // The size of all memory in A's source snapshot, in bytes.
        public MemorySize TotalSnapshotSizeA { get; }

        // The size of all memory in B's source snapshot, in bytes.
        public MemorySize TotalSnapshotSizeB { get; }

        // The largest absolute size delta of any single item in the model's tree, in bytes.
        public long LargestAbsoluteSizeDelta { get; }

        public TBaseModel BaseModel { get; }
        public TBaseModel ComparedModel { get; }

        // The data associated with each item in the tree.
        public readonly struct ComparisonData : INamedTreeItemData
        {
            public ComparisonData(
                string name,
                MemorySize totalSizeInA,
                MemorySize totalSizeInB,
                uint countInA,
                uint countInB,
                int[] treeViewIdsInA,
                int[] treeViewIdsInB,
                List<string> itemPath,
                TModelSpecificData extraData,
                AllTrackedMemoryTableMode tableMode,
                int childCount = 0,
                ComparisonSourceIndex sourceIndex = default,
                bool extraChanged = false)
            {
                Name = name;
                SizeDelta = Convert.ToInt64(totalSizeInB.Committed) - Convert.ToInt64(totalSizeInA.Committed);
                TotalSizeInA = totalSizeInA;
                TotalSizeInB = totalSizeInB;
                CountInA = countInA;
                CountInB = countInB;
                TreeViewIdsInA = treeViewIdsInA;
                TreeViewIdsInB = treeViewIdsInB;
                CountDelta = Convert.ToInt32(countInB) - Convert.ToInt32(countInA);
                var sizeChanged = tableMode switch
                {
                    AllTrackedMemoryTableMode.OnlyCommitted => totalSizeInA.Committed != totalSizeInB.Committed,
                    AllTrackedMemoryTableMode.OnlyResident => totalSizeInA.Resident != totalSizeInB.Resident,
                    AllTrackedMemoryTableMode.CommittedAndResident => totalSizeInA != totalSizeInB,
                    _ => throw new NotImplementedException()
                };
                HasChanged = sizeChanged || CountInA != CountInB || extraChanged;
                ItemPath = itemPath;
                ExtraData = extraData;
                ChildCount = childCount;
                SourceIndex = sourceIndex;
            }

            /// <summary>
            /// Rehydrates a flattened, paged row from a <see cref="ComparisonCompactRow"/>, taking the precomputed
            /// HasChanged flag directly rather than recomputing it from ExtraData — the row was flattened from a
            /// leaf node, whose HasChanged already folded in size/count/extra-data change detection at build time.
            /// </summary>
            internal ComparisonData(
                string name,
                MemorySize totalSizeInA,
                MemorySize totalSizeInB,
                uint countInA,
                uint countInB,
                int[] treeViewIdsInA,
                int[] treeViewIdsInB,
                List<string> itemPath,
                TModelSpecificData extraData,
                int childCount,
                ComparisonSourceIndex sourceIndex,
                bool hasChanged)
            {
                Name = name;
                SizeDelta = Convert.ToInt64(totalSizeInB.Committed) - Convert.ToInt64(totalSizeInA.Committed);
                TotalSizeInA = totalSizeInA;
                TotalSizeInB = totalSizeInB;
                CountInA = countInA;
                CountInB = countInB;
                TreeViewIdsInA = treeViewIdsInA;
                TreeViewIdsInB = treeViewIdsInB;
                CountDelta = Convert.ToInt32(countInB) - Convert.ToInt32(countInA);
                HasChanged = hasChanged;
                ItemPath = itemPath;
                ExtraData = extraData;
                ChildCount = childCount;
                SourceIndex = sourceIndex;
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

            // Has this item or any of its children changed?
            public bool HasChanged { get; }

            // Item path.
            public List<string> ItemPath { get; }
            public int[] TreeViewIdsInA { get; }
            public int[] TreeViewIdsInB { get; }
            public TModelSpecificData ExtraData { get; }

            // The number of direct child comparison groups.
            public int ChildCount { get; }

            // The snapshot-level source handles from each snapshot for this comparison row.
            public ComparisonSourceIndex SourceIndex { get; }
        }

        // There is currently no need for tree node id based filtering beyond filtering Base and Compared tables based on the tree node ids selected in the comparison view
        // In case we see a use for the faster filtering via tree node ids, this should do it.
        //static public ComparisonTableModel<TBaseModel, TBaseModelTreeItemData> Build(ComparisonTableModel<TBaseModel, TBaseModelTreeItemData> fullModel, IEnumerable<int> treeNodeIdFilter = null)
        //{
        //    Dictionary<int, Tuple<IndexableEnumerable<TreeViewItemData<ComparisonData>>, int>> idToIndex = null;
        //    var tree = new List<TreeViewItemData<ComparisonData>>();

        //    var totalMemoryA = new MemorySize();
        //    var totalMemoryB = new MemorySize();
        //    var largestAbsoluteSizeDelta = 0ul;

        //    if (treeNodeIdFilter != null)
        //    {
        //        var stack = new Stack<TreeViewItemData<ComparisonData>>();
        //        foreach (var id in treeNodeIdFilter)
        //        {
        //            // the only place where the base model has to be assumed as non-null.
        //            var item = fullModel.RootNodes.GetItemById(ref idToIndex, id);
        //            stack.Push(item);
        //        }
        //        while (stack.Count > 0)
        //        {
        //            var treeNode = stack.Pop();
        //            // only add leaf nodes, add children to the stack to be processed
        //            if (treeNode.hasChildren)
        //            {
        //                foreach (var child in treeNode.children)
        //                {
        //                    stack.Push(child);
        //                }
        //            }
        //            else
        //            {
        //                tree.Add(new TreeViewItemData<ComparisonData>(treeNode.id, treeNode.data));
        //                var diff = treeNode.data.TotalSizeInA.Committed > treeNode.data.TotalSizeInB.Committed ?
        //                    treeNode.data.TotalSizeInA.Committed - treeNode.data.TotalSizeInB.Committed
        //                    : treeNode.data.TotalSizeInA.Committed - treeNode.data.TotalSizeInB.Committed;
        //                if(diff > largestAbsoluteSizeDelta)
        //                    largestAbsoluteSizeDelta = diff;
        //                totalMemoryA += treeNode.data.TotalSizeInA;
        //                totalMemoryB += treeNode.data.TotalSizeInB;
        //            }
        //        }
        //    }
        //    // the base model could be null, but in an empty model, there are no selections to be made anyways.
        //    var model = CreateDerivedModel(tree, totalMemoryA, totalMemoryB, fullModel, (long)largestAbsoluteSizeDelta);
        //    return model;
        //}
        //
        ///// <summary>
        ///// Creates a derived model, based on a provided <paramref name="baseModel"/> and some <paramref name="rootNodes"/>.
        ///// The <paramref name="baseModel"/> may be null if <paramref name="rootNodes"/> is an empty list.
        ///// </summary>
        ///// <param name="rootNodes"></param>
        ///// <param name="totalMemory"></param>
        ///// <param name="baseModel"></param>
        ///// <returns></returns>
        //static ComparisonTableModel<TBaseModel, TBaseModelTreeItemData> CreateDerivedModel(List<TreeViewItemData<ComparisonData>> rootNodes, MemorySize totalMemoryA, MemorySize totalMemoryB, ComparisonTableModel<TBaseModel, TBaseModelTreeItemData> baseModel, long largestAbsoluteSizeDelta)
        //{
        //    return new ComparisonTableModel<TBaseModel, TBaseModelTreeItemData>(rootNodes, baseModel.BaseModel, totalMemoryA, baseModel.ComparedModel, totalMemoryB, largestAbsoluteSizeDelta);
        //}
    }
}

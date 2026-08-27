using System;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Blittable per-row record for a flattened comparison table, carrying everything needed to sort the row
    /// natively. Shared across every comparison view (<see cref="ComparisonTableModel{TBaseModel,TBaseModelTreeItemData,TModelSpecificData}.ComparisonData"/>'s
    /// blittable columns are identical regardless of <c>TModelSpecificData</c>), so this row type is not itself
    /// generic. The managed parts of a comparison row (name, tree-view id arrays, and the view-specific
    /// <c>TModelSpecificData</c>) live in the parallel side lists of <see cref="ComparisonRowSource{TModelSpecificData}"/>,
    /// indexed by <see cref="NameId"/>/<see cref="IdsAId"/>/<see cref="IdsBId"/>/<see cref="ExtraId"/>.
    /// </summary>
    readonly struct ComparisonCompactRow
    {
        public readonly ComparisonSourceIndex Source;
        public readonly MemorySize TotalSizeInA;
        public readonly MemorySize TotalSizeInB;
        public readonly long SizeDelta;
        public readonly uint CountInA;
        public readonly uint CountInB;
        public readonly int CountDelta;
        public readonly int ChildCount;
        public readonly int TreeId;
        /// <summary>Index into <see cref="ComparisonRowSource{TModelSpecificData}"/>'s name list.</summary>
        public readonly int NameId;
        /// <summary>Index into the ids-in-A side list, or -1 when the row has no matched items in A.</summary>
        public readonly int IdsAId;
        /// <summary>Index into the ids-in-B side list, or -1 when the row has no matched items in B.</summary>
        public readonly int IdsBId;
        /// <summary>Index into the extra-data side list.</summary>
        public readonly int ExtraId;
        /// <summary>
        /// Precomputed by the caller from the view-specific ExtraData's aggregated warning info, so the
        /// Burst-compatible sort comparer can rank by status without knowing the shape of TModelSpecificData.
        /// </summary>
        public readonly int StatusSortKeyInA;
        public readonly int StatusSortKeyInB;
        public readonly bool HasChanged;

        public ComparisonCompactRow(
            int treeId,
            ComparisonSourceIndex source,
            MemorySize totalSizeInA,
            MemorySize totalSizeInB,
            uint countInA,
            uint countInB,
            int childCount,
            int nameId,
            int idsAId,
            int idsBId,
            int extraId,
            int statusSortKeyInA,
            int statusSortKeyInB,
            bool hasChanged)
        {
            TreeId = treeId;
            Source = source;
            TotalSizeInA = totalSizeInA;
            TotalSizeInB = totalSizeInB;
            SizeDelta = Convert.ToInt64(totalSizeInB.Committed) - Convert.ToInt64(totalSizeInA.Committed);
            CountInA = countInA;
            CountInB = countInB;
            CountDelta = Convert.ToInt32(countInB) - Convert.ToInt32(countInA);
            ChildCount = childCount;
            NameId = nameId;
            IdsAId = idsAId;
            IdsBId = idsBId;
            ExtraId = extraId;
            StatusSortKeyInA = statusSortKeyInA;
            StatusSortKeyInB = statusSortKeyInB;
            HasChanged = hasChanged;
        }
    }

    /// <summary>
    /// Unmanaged, multi-column comparer over an index array into a <c>ComparisonCompactRow*</c> buffer, for
    /// <see cref="PagedFlatModel{TRow,TItemData}.SortFlat{TComparer}"/>. Compares only blittable row fields, so it
    /// stays Burst-compatible; the name column goes through the model's managed name sort instead. Honors up to
    /// <see cref="k_MaxColumns"/> levels, so the table header's multi-column sort keeps working while paged.
    /// </summary>
    unsafe struct ComparisonFlatRowComparer : IRefComparer<long>
    {
        public enum Column : byte
        {
            SizeDelta,
            TotalSizeInA,
            TotalSizeInB,
            CountDelta,
            CountInA,
            CountInB,
            StatusInA,
            StatusInB,
        }

        public const int k_MaxColumns = 4;

        [NativeDisableUnsafePtrRestriction]
        readonly ComparisonCompactRow* m_Rows;
        fixed byte m_Columns[k_MaxColumns];
        fixed byte m_Descending[k_MaxColumns];
        readonly int m_Count;

        public ComparisonFlatRowComparer(ComparisonCompactRow* rows, ReadOnlySpan<(Column column, bool descending)> levels)
        {
            m_Rows = rows;
            m_Count = Math.Min(levels.Length, k_MaxColumns);
            for (var i = 0; i < m_Count; i++)
            {
                m_Columns[i] = (byte)levels[i].column;
                m_Descending[i] = (byte)(levels[i].descending ? 1 : 0);
            }
        }

        public int Compare(ref long indexA, ref long indexB)
        {
            ref readonly var a = ref m_Rows[indexA];
            ref readonly var b = ref m_Rows[indexB];
            for (var i = 0; i < m_Count; i++)
            {
                var c = CompareColumn((Column)m_Columns[i], in a, in b);
                if (m_Descending[i] != 0)
                    c = -c;
                if (c != 0)
                    return c;
            }
            return 0;
        }

        /// <summary>
        /// Compares two rows on a single column.
        /// <see cref="ComparisonTableModel{TBaseModel,TBaseModelItemData,TModelSpecificData}.SortFlatMixed"/> uses
        /// it when interleaving a managed name comparison with the typed columns.
        /// </summary>
        public static int CompareColumn(Column column, in ComparisonCompactRow a, in ComparisonCompactRow b)
        {
            switch (column)
            {
                case Column.SizeDelta: return a.SizeDelta.CompareTo(b.SizeDelta);
                case Column.TotalSizeInA: return a.TotalSizeInA.Committed.CompareTo(b.TotalSizeInA.Committed);
                case Column.TotalSizeInB: return a.TotalSizeInB.Committed.CompareTo(b.TotalSizeInB.Committed);
                case Column.CountDelta: return a.CountDelta.CompareTo(b.CountDelta);
                case Column.CountInA: return a.CountInA.CompareTo(b.CountInA);
                case Column.CountInB: return a.CountInB.CompareTo(b.CountInB);
                case Column.StatusInA: return a.StatusSortKeyInA.CompareTo(b.StatusSortKeyInA);
                case Column.StatusInB: return a.StatusSortKeyInB.CompareTo(b.StatusSortKeyInB);
                default: return 0;
            }
        }
    }
}

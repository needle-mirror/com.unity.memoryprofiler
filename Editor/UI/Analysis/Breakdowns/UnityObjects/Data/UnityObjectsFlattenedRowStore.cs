using System;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Blittable per-row record for the flattened Unity Objects view, carrying everything needed to sort the row
    /// and to rehydrate its <see cref="UnityObjectsModel.ItemData"/> for display. Unmanaged (no string/ref fields)
    /// so the full row set can live in a native <see cref="FlattenedRowStore{TRow}"/>. The display name lives in
    /// a parallel managed list indexed by <see cref="NameId"/>, because a leaf's name is not always derivable
    /// from <see cref="Source"/> (instance-id disambiguation, duplicate grouping). The model pages over the store
    /// via <see cref="PagedFlatModel{TRow,TItemData}"/>, sorting an index array with
    /// <see cref="UnityObjectsFlatRowComparer"/>.
    /// </summary>
    readonly struct UnityObjectsCompactRow
    {
        public readonly CachedSnapshot.SourceIndex Source;
        public readonly MemorySize NativeSize;
        public readonly MemorySize ManagedSize;
        public readonly MemorySize GpuSize;
        public readonly ObjectStatusWarningInfo WarningInfo;
        public readonly int ChildCount;
        public readonly int TreeId;
        /// <summary>Index into the model's parallel flat-name list (every flat row has a resolved display name).</summary>
        public readonly int NameId;
        /// <summary>
        /// Precomputed so the Burst-compiled sort comparer can rank by status without calling the managed
        /// GetStatusSortKey (which reads a managed static table and would break Burst compilation).
        /// </summary>
        public readonly int StatusSortKey;

        public UnityObjectsCompactRow(int treeId, in UnityObjectsModel.ItemData data, int nameId)
        {
            TreeId = treeId;
            Source = data.Source;
            NativeSize = data.NativeSize;
            ManagedSize = data.ManagedSize;
            GpuSize = data.GpuSize;
            WarningInfo = data.WarningInfo;
            ChildCount = data.ChildCount;
            StatusSortKey = data.WarningInfo.GetStatusSortKey();
            NameId = nameId;
        }

        public MemorySize TotalSize => NativeSize + ManagedSize + GpuSize;
    }

    /// <summary>
    /// Unmanaged, multi-column comparer over an index array into a <c>UnityObjectsCompactRow*</c> buffer, for
    /// <see cref="PagedFlatModel{TRow,TItemData}.SortFlat{TComparer}"/>. Compares only blittable row fields
    /// (sizes plus the precomputed status key) so it stays Burst-compatible; the name column goes through the
    /// model's managed name sort instead. Honors up to <see cref="k_MaxColumns"/> levels, so the table header's
    /// multi-column sort keeps working while paged.
    /// </summary>
    unsafe struct UnityObjectsFlatRowComparer : IRefComparer<long>
    {
        public enum Column : byte
        {
            SizeCommitted,
            SizeResident,
            NativeSize,
            ManagedSize,
            GpuSize,
            Status,
        }

        public const int k_MaxColumns = 4;

        [NativeDisableUnsafePtrRestriction]
        readonly UnityObjectsCompactRow* m_Rows;
        fixed byte m_Columns[k_MaxColumns];
        fixed byte m_Descending[k_MaxColumns];
        readonly int m_Count;

        public UnityObjectsFlatRowComparer(UnityObjectsCompactRow* rows, ReadOnlySpan<(Column column, bool descending)> levels)
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
            var aTotalSize = a.TotalSize;
            var bTotalSize = b.TotalSize;
            for (var i = 0; i < m_Count; i++)
            {
                var c = CompareColumn((Column)m_Columns[i], in a, in b, aTotalSize, bTotalSize);
                if (m_Descending[i] != 0)
                    c = -c;
                if (c != 0)
                    return c;
            }
            return 0;
        }

        /// <summary>
        /// Compares two rows on a single column, given their precomputed total size.
        /// <see cref="UnityObjectsModel.SortFlatMixed"/> uses it when interleaving a managed name comparison with the typed columns.
        /// </summary>
        public static int CompareColumn(Column column, in UnityObjectsCompactRow a, in UnityObjectsCompactRow b, MemorySize aTotalSize, MemorySize bTotalSize)
        {
            switch (column)
            {
                case Column.SizeCommitted: return aTotalSize.Committed.CompareTo(bTotalSize.Committed);
                case Column.SizeResident: return aTotalSize.Resident.CompareTo(bTotalSize.Resident);
                case Column.NativeSize: return a.NativeSize.Committed.CompareTo(b.NativeSize.Committed);
                case Column.ManagedSize: return a.ManagedSize.Committed.CompareTo(b.ManagedSize.Committed);
                case Column.GpuSize: return a.GpuSize.Committed.CompareTo(b.GpuSize.Committed);
                case Column.Status: return a.StatusSortKey.CompareTo(b.StatusSortKey);
                default: return 0;
            }
        }
    }
}

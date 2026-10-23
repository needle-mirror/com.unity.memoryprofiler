using System;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Blittable per-row record for the Roots &amp; Impact view, carrying everything needed to sort the row and
    /// to rehydrate its <see cref="RootsAndImpactModel.ItemData"/> for display. Unmanaged (no string/ref fields)
    /// so the full row set can live in a native <see cref="FlattenedRowStore{TRow}"/>. The model pages over the
    /// store via <see cref="PagedFlatModel{TRow,TItemData}"/>, sorting an index array with
    /// <see cref="RootsAndImpactFlatRowComparer"/>.
    /// </summary>
    readonly struct CompactFlatRow
    {
        public readonly CachedSnapshot.SourceIndex Source;
        public readonly NativeRootSize TotalImpact;
        public readonly MemorySize OwnSizeSum;
        public readonly MemorySize ExclusiveImpactSum;
        public readonly MemorySize SharedProRataSum;
        public readonly MemorySize SharedInTotalSum;
        public readonly ObjectStatusWarningInfo WarnInfo;
        public readonly int ChildCount;
        public readonly int TreeId;
        /// <summary>
        /// The number of referrers this row's impact was divided across (see <see cref="RootAndImpactInfo.GetReferrerCount"/>);
        /// <see cref="RootAndImpactInfo.ReferrerData.SingleReferrer"/> if it never divided and for synthetic rows.
        /// </summary>
        public readonly long ReferrerCount;
        /// <summary>
        /// -1 resolves the display name from Source; otherwise an index into the model's special-name list
        /// (synthetic/region rows whose name is a TextContent constant, not derivable from Source).
        /// </summary>
        public readonly int SpecialNameId;
        /// <summary>
        /// Precomputed so the Burst-compiled sort comparer can rank by status without calling the managed
        /// GetStatusSortKey (which reads a managed static table and would break Burst compilation).
        /// </summary>
        public readonly int StatusSortKey;
        /// <summary>
        /// Depth of this row in its DFS pre-order subtree (each bucket-root subtree starts at 0; synthetic
        /// region/aggregate rows are depth-0 singletons). The filtered-total recompute uses it to count only the
        /// topmost included node per path, so shared bytes are not counted once per ancestor.
        /// </summary>
        public readonly ushort Depth;
        /// <summary>
        /// Which ownership bucket this row belongs to, so that a flat-mode search matching a bucket's header
        /// name can reveal its whole bucket. Ownership rows carry (byte)OwnershipBucket; see the sentinels.
        /// </summary>
        public readonly byte BucketId;
        public readonly ObjectStatus Status;
        /// <summary>Boolean row attributes packed into one byte to keep the struct unambiguously unmanaged and small.</summary>
        readonly Flags m_Flags;

        [Flags]
        enum Flags : byte
        {
            /// <summary>No attributes set.</summary>
            None = 0,
            /// <summary>The row's own size includes graphics memory (GfxSize &gt; 0).</summary>
            OwnSizeIncludesGraphics = 1 << 0,
            /// <summary>
            /// Region data (Reserved / Untracked / Executables / VM-heap), which the Hide Region Data toggle
            /// removes. Not set on the unrooted-allocations row: that is allocation data and stays visible
            /// whatever the toggle says.
            /// </summary>
            RegionData = 1 << 1,
            /// <summary>Virtual OwnershipBucket group-header row (no real SourceIndex). Tree-mode only.</summary>
            GroupHeader = 1 << 2,
            /// <summary>Ancestor that doesn't match a status filter itself but has matching descendants (visual de-emphasis). Tree-mode only.</summary>
            FilterMatchAncestor = 1 << 3,
            /// <summary>The row's own ownership-tree parent received none of its impact (see <see cref="RootAndImpactInfo.TryGetOrphanedSplitRedirect"/>).</summary>
            ParentExcludedFromSplit = 1 << 4,
        }

        /// <summary>Bucket sentinel for synthetic rows that participate in no bucket-name reveal (regions, unrooted).</summary>
        public const byte BucketIdNone = 255;
        /// <summary>Bucket sentinel for the Virtual Machine Memory pseudo-bucket (re-homed VM root subtree + the VM-heap row).</summary>
        public const byte BucketIdVirtualMachine = 254;

        public CompactFlatRow(
            int treeId,
            CachedSnapshot.SourceIndex source,
            NativeRootSize ownSize,
            NativeRootSize totalImpact,
            MemorySize exclusiveImpactSum,
            MemorySize sharedProRataSum,
            MemorySize sharedInTotalSum,
            ObjectStatus status,
            ObjectStatusWarningInfo warnInfo,
            int childCount,
            int specialNameId,
            ushort depth,
            byte bucketId,
            bool isRegionData,
            bool isGroupHeader = false,
            bool isFilterMatchAncestor = false,
            long referrerCount = RootAndImpactInfo.ReferrerData.SingleReferrer,
            bool parentExcludedFromSplit = false)
        {
            TreeId = treeId;
            Source = source;
            TotalImpact = totalImpact;
            OwnSizeSum = ownSize.SumUp();
            ExclusiveImpactSum = exclusiveImpactSum;
            SharedProRataSum = sharedProRataSum;
            SharedInTotalSum = sharedInTotalSum;
            WarnInfo = warnInfo;
            ChildCount = childCount;
            Status = status;
            StatusSortKey = warnInfo.GetStatusSortKey();
            m_Flags = (ownSize.GfxSize.Committed > 0 ? Flags.OwnSizeIncludesGraphics : Flags.None)
                | (isRegionData ? Flags.RegionData : Flags.None)
                | (isGroupHeader ? Flags.GroupHeader : Flags.None)
                | (isFilterMatchAncestor ? Flags.FilterMatchAncestor : Flags.None)
                | (parentExcludedFromSplit ? Flags.ParentExcludedFromSplit : Flags.None);
            ReferrerCount = referrerCount;
            SpecialNameId = specialNameId;
            Depth = depth;
            BucketId = bucketId;
        }

        public bool OwnSizeIncludesGraphics => (m_Flags & Flags.OwnSizeIncludesGraphics) != 0;

        /// <summary>True for region-data rows (Reserved / Untracked / Executables / VM-heap) hidden by the Hide Region Data toggle.</summary>
        public bool IsRegionData => (m_Flags & Flags.RegionData) != 0;

        /// <summary>True for virtual OwnershipBucket group header rows (no real SourceIndex). Tree-mode only.</summary>
        public bool IsGroupHeader => (m_Flags & Flags.GroupHeader) != 0;

        /// <summary>True for ancestor nodes that don't match a status filter themselves but contain descendants that do. Tree-mode only.</summary>
        public bool IsFilterMatchAncestor => (m_Flags & Flags.FilterMatchAncestor) != 0;

        /// <summary>
        /// True if this row's ownership-tree parent received none of its impact (see <see cref="RootAndImpactInfo.TryGetOrphanedSplitRedirect"/>).
        /// </summary>
        public bool ParentExcludedFromSplit => (m_Flags & Flags.ParentExcludedFromSplit) != 0;

        /// <summary>
        /// True when this row has only exclusive impact, no shared pro-rata, making it a Hide Exclusive-Only
        /// candidate. The model still keeps it when a descendant survives the toggle, as a descendant can carry
        /// shared impact its shortest-path parent did not receive. Region rows look exclusive-only but are not
        /// subject to the toggle, so the model excludes them from this gate.
        /// </summary>
        public bool IsExclusiveOnly => SharedProRataSum.Committed == 0 && ExclusiveImpactSum.Committed > 0;

        public MemorySize TotalImpactSum => TotalImpact.SumUp();
    }

    /// <summary>
    /// Unmanaged, multi-column comparer over an index array into a <c>CompactFlatRow*</c> buffer, for
    /// <see cref="PagedFlatModel{TRow,TItemData}.SortFlat{TComparer}"/>. Compares only blittable row fields
    /// (sizes plus the precomputed status key) so it stays Burst-compatible; the name column goes through the
    /// model's managed name sort instead. Honors up to <see cref="k_MaxColumns"/> levels, so the table header's
    /// multi-column sort keeps working while paged.
    /// </summary>
    unsafe struct RootsAndImpactFlatRowComparer : IRefComparer<long>
    {
        public enum Column : byte
        {
            TotalImpactCommitted,
            TotalImpactResident,
            OwnSize,
            ExclusiveImpact,
            SharedProRata,
            SharedInTotal,
            NativeImpact,
            ManagedImpact,
            GfxImpact,
            Status,
        }

        public const int k_MaxColumns = 4;

        [NativeDisableUnsafePtrRestriction]
        readonly CompactFlatRow* m_Rows;
        fixed byte m_Columns[k_MaxColumns];
        fixed byte m_Descending[k_MaxColumns];
        readonly int m_Count;

        public RootsAndImpactFlatRowComparer(CompactFlatRow* rows, ReadOnlySpan<(Column column, bool descending)> levels)
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
            var aSummed = a.TotalImpact.SumUp();
            var bSummed = b.TotalImpact.SumUp();
            for (var i = 0; i < m_Count; i++)
            {
                var c = CompareColumn((Column)m_Columns[i], in a, in b, aSummed, bSummed);
                if (m_Descending[i] != 0)
                    c = -c;
                if (c != 0)
                    return c;
            }
            return 0;
        }

        /// <summary>
        /// Compares two rows on a single column, given their precomputed summed impact.
        /// <see cref="RootsAndImpactModel.SortFlatMixed"/> uses it when interleaving a managed name comparison with the typed columns.
        /// </summary>
        public static int CompareColumn(Column column, in CompactFlatRow a, in CompactFlatRow b, MemorySize aSummed, MemorySize bSummed)
        {
            switch (column)
            {
                case Column.TotalImpactCommitted: return aSummed.Committed.CompareTo(bSummed.Committed);
                case Column.TotalImpactResident: return aSummed.Resident.CompareTo(bSummed.Resident);
                case Column.OwnSize: return a.OwnSizeSum.Committed.CompareTo(b.OwnSizeSum.Committed);
                case Column.ExclusiveImpact: return a.ExclusiveImpactSum.Committed.CompareTo(b.ExclusiveImpactSum.Committed);
                case Column.SharedProRata: return a.SharedProRataSum.Committed.CompareTo(b.SharedProRataSum.Committed);
                case Column.SharedInTotal: return a.SharedInTotalSum.Committed.CompareTo(b.SharedInTotalSum.Committed);
                case Column.NativeImpact: return a.TotalImpact.NativeSize.Committed.CompareTo(b.TotalImpact.NativeSize.Committed);
                case Column.ManagedImpact: return a.TotalImpact.ManagedSize.Committed.CompareTo(b.TotalImpact.ManagedSize.Committed);
                case Column.GfxImpact: return a.TotalImpact.GfxSize.Committed.CompareTo(b.TotalImpact.GfxSize.Committed);
                case Column.Status: return a.StatusSortKey.CompareTo(b.StatusSortKey);
                default: return 0;
            }
        }
    }
}

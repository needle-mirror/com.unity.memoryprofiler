using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>Data model for the 'Roots &amp; Impact' breakdown.</summary>
    class RootsAndImpactModel : TreeModel<RootsAndImpactModel.ItemData>, IDisposable, IPagedFlatModel
    {
        /// <summary>
        /// Flattened, paged backing data, delegated to the shared core. Null for the hierarchical view, the
        /// id-filtered view and the empty model, which all hold their rows directly in RootNodes instead.
        /// </summary>
        readonly PagedFlatModel<CompactFlatRow, ItemData> m_Paged;
        /// <summary>
        /// Owns the native store and its name-resolution dependencies; refcounted so a derived (id-filtered)
        /// model can keep it alive past this model's disposal. Released, not disposed directly, in Dispose().
        /// </summary>
        readonly RootsAndImpactRowSource m_RowSource;
        /// <summary>
        /// Held directly, in addition to via m_RowSource, so the native sort comparer can be built from the
        /// store's row pointer.
        /// </summary>
        readonly FlattenedRowStore<CompactFlatRow> m_Store;
        /// <summary>
        /// The totals over the whole (unfiltered) store, restored when no filter is active. A filter recomputes
        /// them over the matching subset via the depth-stack below.
        /// </summary>
        readonly NativeRootSize m_UnfilteredTotalImpact;
        readonly MemorySize m_UnfilteredTotalImpactExclusive;
        readonly MemorySize m_UnfilteredTotalOwnSize;
        readonly MemorySize m_UnfilteredTotalSharedInTotal;
        readonly MemorySize m_UnfilteredTotalSharedProRata;
        /// <summary>
        /// Reusable scratch for the filtered-total recompute: a stack of the depths of currently-kept ancestor
        /// rows along the DFS path. See ApplyFlatFilter / AccumulateFilteredTotal.
        /// </summary>
        readonly List<ushort> m_KeptAncestorDepthStack = new List<ushort>();
        /// <summary>Running total accumulated during a filtered-total recompute pass.</summary>
        NativeRootSize m_FilteredTotalAccumulator;
        MemorySize m_FilteredExclusiveAccumulator;
        MemorySize m_FilteredSharedProRataAccumulator;
        MemorySize m_FilteredOwnSizeAccumulator;
        MemorySize m_FilteredSharedInTotalAccumulator;
        /// <summary>
        /// True while the filter is the "all potential savings" deep-link with no text search, where the AssetBundle
        /// rows' contribution is replaced by the shared bundle-level accounting. A field because
        /// AccumulateFilteredTotal runs as a bound delegate and cannot take extra parameters.
        /// </summary>
        bool m_ApplyAssetBundleAllSavingsSubstitution;
        /// <summary>
        /// Sum of the topmost-kept AssetBundle-category rows' TotalImpact and ExclusiveImpactSum while
        /// <see cref="m_ApplyAssetBundleAllSavingsSubstitution"/> is set, which ApplyFlatFilter swaps for the shared category value.
        /// </summary>
        NativeRootSize m_FilteredAssetBundleCategoryTotal;
        /// <summary>Exclusive counterpart of <see cref="m_FilteredAssetBundleCategoryTotal"/>.</summary>
        MemorySize m_FilteredAssetBundleCategoryExclusive;
        /// <summary>Shared pro-rata counterpart of <see cref="m_FilteredAssetBundleCategoryTotal"/>.</summary>
        MemorySize m_FilteredAssetBundleCategorySharedProRata;
        bool[] m_BucketRevealMask;
        /// <summary>
        /// Ascending store indices of the exclusive-only rows that Hide Exclusive-Only still keeps because a
        /// descendant survives it. Built on first use; see EnsureExclusiveOnlyRowsWithSurvivingDescendants.
        /// </summary>
        DynamicArray<long> m_ExclusiveOnlyRowsWithSurvivingDescendants;
        /// <summary>Guards every method below that mutates the flattened view's shared state.</summary>
        readonly object m_FlatMutationLock = new object();
        readonly Dictionary<int, FilteredImpact> m_FilteredImpactByItemId;

        /// <summary>
        /// Hierarchical / id-filtered / empty model: rows are held directly in <paramref name="treeRootNodes"/>,
        /// each an ItemData redirect into <paramref name="rowSource"/> (null only for the truly-empty model,
        /// whose treeRootNodes is empty and so never dereferences it). The model retains <paramref name="rowSource"/>
        /// and releases it on Dispose — for the plain hierarchical build this is sole ownership of a fresh
        /// source; for the id-filtered derived model it shares (and keeps alive) the base model's source.
        /// </summary>
        public RootsAndImpactModel(
            List<TreeViewItemData<ItemData>> treeRootNodes,
            RootsAndImpactRowSource rowSource,
            NativeRootSize totalImpactSize,
            MemorySize totalSnapshotMemorySize,
            MemorySize totalImpactExclusiveSize = default,
            MemorySize totalOwnSize = default,
            MemorySize totalSharedInTotalSize = default,
            MemorySize totalSharedProRataSize = default,
            Dictionary<int, FilteredImpact> filteredImpactByItemId = null)
            : base(treeRootNodes)
        {
            TotalImpactSize = totalImpactSize;
            TotalSnapshotMemorySize = totalSnapshotMemorySize;
            TotalImpactExclusiveSize = totalImpactExclusiveSize;
            TotalOwnSize = totalOwnSize;
            TotalSharedInTotalSize = totalSharedInTotalSize;
            m_RowSource = rowSource;
            m_FilteredImpactByItemId = filteredImpactByItemId;
            TotalImpactSharedProRataSize = totalSharedProRataSize;
        }

        static ulong SaturatingSubtract(ulong total, ulong part) => total > part ? total - part : 0;

        internal static MemorySize SharedProRataOf(NativeRootSize totalImpactSize, MemorySize totalImpactExclusiveSize)
        {
            var totalImpactSum = totalImpactSize.SumUp();
            return new MemorySize(
                SaturatingSubtract(totalImpactSum.Committed, totalImpactExclusiveSize.Committed),
                SaturatingSubtract(totalImpactSum.Resident, totalImpactExclusiveSize.Resident));
        }

        /// <summary>
        /// Gets the filter-relevant impact of a de-emphasized filter-match-ancestor or group-header row. Returns
        /// <c>false</c> for matching rows and unfiltered builds, which render their own values.
        /// </summary>
        public bool TryGetFilteredImpact(int itemId, out FilteredImpact filteredImpact)
        {
            if (m_FilteredImpactByItemId != null)
                return m_FilteredImpactByItemId.TryGetValue(itemId, out filteredImpact);
            filteredImpact = default;
            return false;
        }

        /// <summary>
        /// Flattened, paginated model: every matching row lives in <paramref name="rowSource"/>'s store (native),
        /// and only the current page is materialized into <see cref="TreeModel{T}.RootNodes"/> at a time via the
        /// shared <see cref="PagedFlatModel{TRow,TItemData}"/> core. The model retains <paramref name="rowSource"/>
        /// and releases it on Dispose.
        /// </summary>
        public RootsAndImpactModel(
            RootsAndImpactRowSource rowSource,
            int pageSize,
            NativeRootSize totalImpactSize,
            MemorySize totalSnapshotMemorySize,
            MemorySize totalImpactExclusiveSize = default,
            MemorySize totalOwnSize = default,
            MemorySize totalSharedInTotalSize = default,
            MemorySize totalSharedProRataSize = default)
            : base(new List<TreeViewItemData<ItemData>>())
        {
            TotalImpactSize = totalImpactSize;
            TotalSnapshotMemorySize = totalSnapshotMemorySize;
            TotalImpactExclusiveSize = totalImpactExclusiveSize;
            TotalOwnSize = totalOwnSize;
            TotalSharedInTotalSize = totalSharedInTotalSize;
            TotalImpactSharedProRataSize = totalSharedProRataSize;
            m_RowSource = rowSource;
            m_Store = rowSource.Store;
            m_UnfilteredTotalImpact = totalImpactSize;
            m_UnfilteredTotalImpactExclusive = totalImpactExclusiveSize;
            m_UnfilteredTotalOwnSize = totalOwnSize;
            m_UnfilteredTotalSharedInTotal = totalSharedInTotalSize;
            m_UnfilteredTotalSharedProRata = totalSharedProRataSize;
            m_BucketRevealMask = new bool[(int)RootAndImpactInfo.OwnershipBucket.Count];

            m_Paged = new PagedFlatModel<CompactFlatRow, ItemData>(m_Store, RootNodes, pageSize, row => row.TreeId,
                ResolveName, index => new ItemData(m_RowSource, index));
        }

        /// <summary>The row source backing this model's ItemData redirects; null only for the truly-empty model.</summary>
        internal RootsAndImpactRowSource RowSource => m_RowSource;

        /// <summary>Resolves a flattened row's display name: synthetic/region rows from the special-name side table, ownership rows from the snapshot.</summary>
        string ResolveName(CompactFlatRow row) => m_RowSource.ResolveName(in row);

        /// <summary>
        /// The total impact across all roots in the model.
        ///
        /// For a flattened model, this is NOT the sum of all root models of the flat list,
        /// as that would show up as a bigger number than the snapshot total due to
        /// double-counting shared memory. Instead for flat models, this is the total impact
        /// of each of the first included items of the tree before it was flattened.
        /// Thus, switching a tree to a flat view will not change this number, and unless
        /// filtering was applied, it will always match the snapshot total,
        /// <see cref="TotalSnapshotMemorySize"/>.
        /// </summary>
        public NativeRootSize TotalImpactSize { get; private set; }

        /// <summary>
        /// The portion of <see cref="TotalImpactSize"/> not shared with any other referrer: the sum of the displayed
        /// <see cref="ItemData.ExclusiveImpactSum"/> of the same top-level rows.
        /// </summary>
        public MemorySize TotalImpactExclusiveSize { get; private set; }

        /// <summary>
        /// The shared (pro-rata) column summed over the topmost rows from their displayed values, which stay
        /// accurate where a row's total and exclusive impact were discounted.
        /// </summary>
        public MemorySize TotalImpactSharedProRataSize { get; private set; }

        /// <summary>
        /// The own size of every row summed at all depths, because <see cref="ItemData.OwnSizeSum"/> never covers a
        /// row's subtree.
        /// </summary>
        public MemorySize TotalOwnSize { get; private set; }

        /// <summary>
        /// The shared-in-total column summed over the topmost rows. It intentionally exceeds the snapshot total,
        /// since shared bytes count once per referrer; read it as a reference-hotspot magnitude, not an amount of memory.
        /// </summary>
        public MemorySize TotalSharedInTotalSize { get; private set; }

        /// <summary>The total size of memory accounted for in the original snapshot, used to reconcile the table total against the snapshot.</summary>
        public MemorySize TotalSnapshotMemorySize { get; }

        /// <summary>True for the flattened, paginated model; false for the hierarchical / id-filtered / empty model.</summary>
        public bool IsPaged => m_Paged != null;

        /// <summary>Number of flattened rows matching the current filter (0 for the hierarchical and empty model).</summary>
        public long FlattenedRowsTotal => m_Paged?.MatchingRowCount ?? 0;

        /// <summary>Rows shown per page in the flattened view.</summary>
        public int PageSize => m_Paged?.PageSize ?? 0;

        /// <summary>Zero-based index of the page currently materialized into <see cref="TreeModel{T}.RootNodes"/>.</summary>
        public int CurrentPage => m_Paged?.CurrentPage ?? 0;

        /// <summary>Number of pages the flattened rows span; always at least 1 (1 for the non-paged model).</summary>
        public int TotalPages => m_Paged?.TotalPages ?? 1;

        /// <summary>
        /// Re-orders the flattened rows by the given column levels (size/status; supports the table's
        /// multi-column sort while paged), via the native index comparer, then shows page 0.
        /// </summary>
        public unsafe void SortFlatColumns(ReadOnlySpan<(RootsAndImpactFlatRowComparer.Column column, bool descending)> levels, bool materialize = true)
        {
            if (m_Paged == null)
                return;
            var comparer = new RootsAndImpactFlatRowComparer(m_Store.GetUnsafeTypedPtr(), levels);
            lock (m_FlatMutationLock)
                m_Paged.SortFlat(comparer, materialize);
        }

        /// <summary>Re-orders the flattened rows alphabetically by name (gated by a row-count threshold), then shows page 0.</summary>
        public void SortFlatByName(bool descending, bool materialize = true)
        {
            if (m_Paged == null)
                return;
            lock (m_FlatMutationLock)
                m_Paged.SortFlatByName(descending, materialize);
        }

        /// <summary>
        /// Re-orders the flattened rows by an ordered list of levels that may mix the name (Description) column
        /// with typed columns at any priority, then shows page 0. See <see cref="FlatSortMixing.Sort{TColumn}"/>
        /// for the fast-path/managed-path decision this delegates to.
        /// </summary>
        public unsafe void SortFlatMixed(ReadOnlySpan<FlatSortLevel<RootsAndImpactFlatRowComparer.Column>> levels, bool materialize = true)
        {
            if (m_Paged == null)
                return;

            lock (m_FlatMutationLock)
                SortFlatMixedLocked(levels, materialize);
        }

        unsafe void SortFlatMixedLocked(ReadOnlySpan<FlatSortLevel<RootsAndImpactFlatRowComparer.Column>> levels, bool materialize)
        {
            var rows = m_Store.GetUnsafeTypedPtr();
            FlatSortMixing.Sort(
                levels,
                m_Paged.MatchingRowCount,
                (lvls, mat) => SortFlatColumns(lvls, mat),
                SortFlatByName,
                m_Paged.ResolveMatchingNames,
                m_Paged.SortFlatManaged,
                (a, b, column) => RootsAndImpactFlatRowComparer.CompareColumn(column, in rows[a], in rows[b], rows[a].TotalImpact.SumUp(), rows[b].TotalImpact.SumUp()),
                materialize);
        }

        /// <summary>
        /// Re-selects which of the store's rows match the given text/status filter and the structural Hide
        /// Exclusive-Only / Hide Region Data toggles, recomputes <see cref="TotalImpactSize"/> over that subset,
        /// and shows page 0. The store is an unfiltered superset, so this is an index re-selection rather than a
        /// rebuild. <paramref name="filter"/> is <c>null</c> for no text/status filtering.
        ///
        /// Both the per-row inclusion rule and the "topmost included node per path" total have to match what a
        /// from-scratch build would produce, or the size bar and totals drift from the hierarchical view.
        /// </summary>
        public void ApplyFlatFilter(CompoundSearchFilter filter, bool hideExclusiveOnly, bool hideRegionData, bool materialize = true)
        {
            if (m_Paged == null)
                return;

            lock (m_FlatMutationLock)
                ApplyFlatFilterLocked(filter, hideExclusiveOnly, hideRegionData, materialize);
        }

        void ApplyFlatFilterLocked(CompoundSearchFilter filter, bool hideExclusiveOnly, bool hideRegionData, bool materialize)
        {
            if (filter == null && !hideExclusiveOnly && !hideRegionData)
            {
                // Nothing excluded: show the whole store; the total is the precomputed unfiltered baseline.
                TotalImpactSize = m_UnfilteredTotalImpact;
                TotalImpactExclusiveSize = m_UnfilteredTotalImpactExclusive;
                TotalOwnSize = m_UnfilteredTotalOwnSize;
                TotalSharedInTotalSize = m_UnfilteredTotalSharedInTotal;
                TotalImpactSharedProRataSize = m_UnfilteredTotalSharedProRata;
                m_Paged.ApplyFilter(null, materialize: materialize);
                return;
            }

            bool[] revealBucket = null;
            var revealVirtualMachine = false;
            if (filter != null)
                revealBucket = BuildBucketRevealMask(filter, out revealVirtualMachine);
            if (hideExclusiveOnly)
                EnsureExclusiveOnlyRowsWithSurvivingDescendants();
            var predicate = BuildRowFilterPredicate(filter, hideExclusiveOnly, hideRegionData, revealBucket, revealVirtualMachine);

            m_KeptAncestorDepthStack.Clear();
            m_ApplyAssetBundleAllSavingsSubstitution =
                filter != null && filter.ExclusiveStatusMask == RootsAndImpactSummaryModelBuilder.AllSavingsStatusMask
                && filter.SearchStringFilter == null;
            m_FilteredTotalAccumulator = default;
            m_FilteredExclusiveAccumulator = default;
            m_FilteredSharedProRataAccumulator = default;
            m_FilteredOwnSizeAccumulator = default;
            m_FilteredSharedInTotalAccumulator = default;
            m_FilteredAssetBundleCategoryTotal = default;
            m_FilteredAssetBundleCategoryExclusive = default;
            m_FilteredAssetBundleCategorySharedProRata = default;
            m_Paged.ApplyFilter(predicate, AccumulateFilteredTotal, materialize);
            if (m_ApplyAssetBundleAllSavingsSubstitution)
            {
                // Row impacts miss a used bundle's reclaimable share and overstate unused content the recoverable splits pro-rata.
                m_RowSource.ComputeAssetBundleSavingsCategory(out var categoryTotal, out var categoryExclusive);
                m_FilteredTotalAccumulator = m_FilteredTotalAccumulator - m_FilteredAssetBundleCategoryTotal + categoryTotal;
                m_FilteredExclusiveAccumulator = m_FilteredExclusiveAccumulator - m_FilteredAssetBundleCategoryExclusive + categoryExclusive;
                m_FilteredSharedProRataAccumulator = m_FilteredSharedProRataAccumulator - m_FilteredAssetBundleCategorySharedProRata
                    + SharedProRataOf(categoryTotal, categoryExclusive);
            }
            TotalImpactSize = m_FilteredTotalAccumulator;
            TotalImpactExclusiveSize = m_FilteredExclusiveAccumulator;
            TotalOwnSize = m_FilteredOwnSizeAccumulator;
            TotalSharedInTotalSize = m_FilteredSharedInTotalAccumulator;
            TotalImpactSharedProRataSize = m_FilteredSharedProRataAccumulator;
        }

        /// <summary>
        /// Re-selects and re-sorts under one lock scope, rechecking <paramref name="cancellationToken"/> once
        /// inside it, so a superseded continuation can't land its stale filter/sort after a newer one already has.
        /// </summary>
        public void ApplyFlatFilterAndSort(
            CompoundSearchFilter filter,
            bool applyFilter,
            bool hideExclusiveOnly,
            bool hideRegionData,
            ReadOnlySpan<FlatSortLevel<RootsAndImpactFlatRowComparer.Column>> sortLevels,
            CancellationToken cancellationToken,
            bool materialize = true)
        {
            if (m_Paged == null)
                return;

            lock (m_FlatMutationLock)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (applyFilter)
                    ApplyFlatFilterLocked(filter, hideExclusiveOnly, hideRegionData, materialize: false);

                SortFlatMixedLocked(sortLevels, materialize);
            }
        }

        /// <summary>
        /// Runs for every store row in DFS pre-order during ApplyFlatFilter. Maintains a stack of the depths of
        /// currently-kept ancestor rows; a kept row contributes its TotalImpact, ExclusiveImpactSum and SharedInTotalSum
        /// only when it has no kept ancestor, whose values already subsume it. OwnSizeSum accumulates at every kept
        /// row. Also tracks AssetBundle-category rows separately while
        /// <see cref="m_ApplyAssetBundleAllSavingsSubstitution"/> is set.
        /// </summary>
        void AccumulateFilteredTotal(long storeIndex, bool kept)
        {
            ref readonly var row = ref m_Store[storeIndex];
            var depth = row.Depth;
            while (m_KeptAncestorDepthStack.Count > 0 && m_KeptAncestorDepthStack[m_KeptAncestorDepthStack.Count - 1] >= depth)
                m_KeptAncestorDepthStack.RemoveAt(m_KeptAncestorDepthStack.Count - 1);
            if (!kept)
                return;
            m_FilteredOwnSizeAccumulator += row.OwnSizeSum;
            if (m_KeptAncestorDepthStack.Count == 0)
            {
                m_FilteredTotalAccumulator += row.TotalImpact;
                m_FilteredExclusiveAccumulator += row.ExclusiveImpactSum;
                m_FilteredSharedProRataAccumulator += row.SharedProRataSum;
                m_FilteredSharedInTotalAccumulator += row.SharedInTotalSum;
                if (m_ApplyAssetBundleAllSavingsSubstitution
                    && RootsAndImpactSummaryModelBuilder.TryMapStatusToIssue(row.Status, out var issue)
                    && issue == RootsAndImpactSummaryModelBuilder.TypeOfRootIssues.UnloadableAssetBundleMemory)
                {
                    m_FilteredAssetBundleCategoryTotal += row.TotalImpact;
                    m_FilteredAssetBundleCategoryExclusive += row.ExclusiveImpactSum;
                    m_FilteredAssetBundleCategorySharedProRata += row.SharedProRataSum;
                }
            }
            m_KeptAncestorDepthStack.Add(depth);
        }

        /// <summary>
        /// The tree build hides an exclusive-only node only once none of its children survived (a post-order,
        /// leaf-only rule), so an exclusive-only node above a descendant with shared impact stays visible. This
        /// reproduces that over the pre-order store in one reverse pass, where <c>survivorBelowDepth[d]</c> says
        /// whether a surviving row at depth d was seen since the last row shallower than d — i.e. whether the
        /// next parent reached at depth d - 1 has a surviving descendant. Rows at the saturated depth
        /// <see cref="ushort.MaxValue"/> can't be told apart from their descendants, so each of them counts any
        /// later surviving saturated row as one; that can only keep extra rows below an already-kept ancestor,
        /// never drop one or change the total. The store is immutable for this model, so the result is computed once.
        /// </summary>
        void EnsureExclusiveOnlyRowsWithSurvivingDescendants()
        {
            if (m_ExclusiveOnlyRowsWithSurvivingDescendants.IsCreated)
                return;

            var rows = new DynamicArray<long>(0, Allocator.Persistent);
            var survivorBelowDepth = new DynamicArray<bool>(0, Allocator.Persistent);
            try
            {
                for (var i = m_Store.Count - 1; i >= 0; i--)
                {
                    ref readonly var row = ref m_Store[i];
                    var childDepth = Math.Min(row.Depth + 1L, ushort.MaxValue);
                    if (survivorBelowDepth.Count <= childDepth)
                        survivorBelowDepth.Resize(childDepth + 1, memClear: true);

                    var hasSurvivingDescendant = survivorBelowDepth[childDepth];
                    survivorBelowDepth[childDepth] = false;

                    var exclusiveOnly = row.IsExclusiveOnly && !row.IsRegionData;
                    if (exclusiveOnly && hasSurvivingDescendant)
                        rows.Push(i);
                    if (!exclusiveOnly || hasSurvivingDescendant)
                        survivorBelowDepth[row.Depth] = true;
                }
            }
            finally
            {
                survivorBelowDepth.Dispose();
            }

            for (long lo = 0, hi = rows.Count - 1; lo < hi; lo++, hi--)
                (rows[lo], rows[hi]) = (rows[hi], rows[lo]);
            m_ExclusiveOnlyRowsWithSurvivingDescendants = rows;
        }

        /// <summary>
        /// A row is kept if it survives the structural hide toggles AND the text/status search. Ownership rows
        /// additionally surface to the GraphicsBuffers status filter when they own graphics memory, and a search
        /// matching a bucket header name reveals that whole bucket. Mirrors
        /// <c>NodePassesFilterIncludingOwnedGraphics</c> plus the Hide Exclusive-Only / Hide Region Data gates.
        /// </summary>
        Func<long, bool> BuildRowFilterPredicate(CompoundSearchFilter filter, bool hideExclusiveOnly, bool hideRegionData, bool[] revealBucket, bool revealVirtualMachine)
        {
            // Resolve ownership-row names only when a text component is present; status-only filters ignore the
            // name (PassesFlat checks the status), so skip the per-row name resolution entirely for those.
            var hasTextFilter = filter?.SearchStringFilter != null;
            return i =>
            {
                var row = m_Store[i];

                // Structural hide toggles, applied as post-filters over the superset store. Hide Exclusive-Only
                // drops rows with only exclusive impact unless a descendant survives the toggle, as the tree build
                // does. Region rows look exclusive-only but are emitted outside the ownership tree and are never
                // subject to this toggle, so they are excluded from the gate.
                if (hideRegionData && row.IsRegionData)
                    return false;
                if (hideExclusiveOnly && row.IsExclusiveOnly && !row.IsRegionData
                    && DynamicArrayAlgorithms.BinarySearch(m_ExclusiveOnlyRowsWithSurvivingDescendants, i) < 0)
                    return false;

                if (filter == null)
                    return true;

                var revealsThisBucket = row.BucketId == CompactFlatRow.BucketIdVirtualMachine
                    ? revealVirtualMachine
                    : row.BucketId < revealBucket.Length && revealBucket[row.BucketId];
                if (revealsThisBucket)
                    return true;

                if (row.SpecialNameId >= 0)
                {
                    var specialName = ResolveName(row);
                    return filter.PassesFlat(specialName, row.Status);
                }

                // A candidate (the row's own name, or one of its type-name candidates below) passes if it
                // matches on name+status directly, OR via a synthesized status the row also stands for:
                // GraphicsBuffers when the row owns graphics memory, or PrefabRoot for a Prefab book-keeping
                // row (whose displayed status is always forwarded from its root Transform, never PrefabRoot
                // itself). Mirrors RootsAndImpactModelBuilder.NodePassesFilterIncludingSynthesizedStatuses.
                var isPrefab = row.Source.Id == CachedSnapshot.SourceIndex.SourceId.Prefab;
                bool PassesCandidate(string candidate) =>
                    filter.PassesFlat(candidate, row.Status)
                        || (row.OwnSizeIncludesGraphics && filter.PassesFlat(candidate, ObjectStatus.GraphicsBuffers))
                        || (isPrefab && filter.PassesFlat(candidate, ObjectStatus.PrefabRoot));

                var name = hasTextFilter ? ResolveName(row) : null;
                if (PassesCandidate(name))
                    return true;

                // An ownership row's own displayed Name is an object/instance name (e.g. a ManagedObject's
                // pointer/preview string, or a NativeObject's scene object name), not its type — so a search
                // for the type name itself only matches via these candidates.
                if (hasTextFilter)
                {
                    var typeNames = m_RowSource.GetTypeNamesForFiltering(in row);
                    if ((typeNames.Native != null && PassesCandidate(typeNames.Native))
                        || (typeNames.Managed != null && PassesCandidate(typeNames.Managed))
                        || (typeNames.ManagedBase != null && PassesCandidate(typeNames.ManagedBase)))
                        return true;
                }

                return false;
            };
        }

        /// <summary>
        /// Precompute which ownership buckets (by BucketId) a text search reveals wholesale by matching their
        /// header name, plus the Virtual Machine Memory pseudo-bucket. Indexed by BucketId; sentinels are out of range.
        /// </summary>
        bool[] BuildBucketRevealMask(CompoundSearchFilter filter, out bool revealVirtualMachine)
        {
            // Mirror tree mode's "Managed Roots" name match revealing every sub-group (see
            // RootsAndImpactModelBuilder.IsManagedRootsSubGroup) so flat mode's reveal mask agrees.
            bool managedRootsNameMatches = filter.MatchesTextName(UIContentData.TextContent.RootsAndImpactBucketManagedRoots);

            Array.Clear(m_BucketRevealMask, 0, m_BucketRevealMask.Length);
            for (var b = 0; b < m_BucketRevealMask.Length; b++)
            {
                m_BucketRevealMask[b] = filter.MatchesTextName(RootsAndImpactModelBuilder.BucketDisplayNameForBucketId((byte)b))
                    || (managedRootsNameMatches && RootsAndImpactModelBuilder.IsManagedRootsSubGroup((RootAndImpactInfo.OwnershipBucket)b));
            }
            revealVirtualMachine = filter.MatchesTextName(
                RootsAndImpactModelBuilder.BucketDisplayNameForBucketId(CompactFlatRow.BucketIdVirtualMachine));
            return m_BucketRevealMask;
        }

        /// <summary>Refills <see cref="TreeModel{T}.RootNodes"/> with the rows of <paramref name="page"/>.</summary>
        public void MaterializePage(int page)
        {
            if (m_Paged == null)
                return;
            lock (m_FlatMutationLock)
                m_Paged.MaterializePage(page);
        }

        public bool TryGetPageForRow(long storeIndex, out int page)
        {
            page = 0;
            return m_Paged != null && m_Paged.TryGetPageForRow(storeIndex, out page);
        }

        public int GetTreeIdForStoreIndex(long storeIndex) => RowSource.Store[storeIndex].TreeId;

        public void Dispose()
        {
            m_Paged?.Dispose();
            m_ExclusiveOnlyRowsWithSurvivingDescendants.Dispose();
            m_RowSource?.Release();
        }

        /// <summary>
        /// The per-row data backing each item in the tree: a redirect into a
        /// <see cref="RootsAndImpactRowSource"/>'s <see cref="CompactFlatRow"/> store, for both the flattened
        /// paged view and the tree-mode per-build store. UIToolkit's TreeDataController holds one entry per node
        /// in a managed dictionary, so a multi-million-node ownership tree only fits if that entry stays this
        /// small.
        /// </summary>
        public readonly struct ItemData : IPrivateComparableItemData, IHasSourceIndex
        {
            static readonly CompactFlatRow s_Zero = default;

            readonly RootsAndImpactRowSource m_Source;
            readonly long m_Index;

            internal ItemData(RootsAndImpactRowSource source, long index)
            {
                m_Source = source;
                m_Index = index;
            }

            /// <summary>
            /// False for a default-constructed ItemData, or one whose row source has already released its store.
            /// UIToolkit's TreeDataController hands out default(TItemData) on a lookup miss, so every property
            /// below has to read as a zeroed/null row rather than throw when this is false.
            /// </summary>
            public bool IsValid => m_Source != null && m_Source.RowsAreAlive;

            ref readonly CompactFlatRow Row
            {
                get
                {
                    if (IsValid)
                        return ref m_Source.Store[m_Index];
                    return ref s_Zero;
                }
            }

            /// <summary>The name of this item in the UI, sorting the table by the name column, and for text based search filtering.</summary>
            public string Name => IsValid ? m_Source.GetCachedName(m_Index) : null;

            /// <summary>Key into the cached snapshot for this item's related data; <see cref="CachedSnapshot.SourceIndex.None"/> for aggregate/header rows.</summary>
            public CachedSnapshot.SourceIndex Source => Row.Source;

            /// <summary>
            /// The total impact summed across Native/Managed/Gfx, used for size bar and default sort.
            /// </summary>
            public MemorySize TotalSize => Row.TotalImpactSum;

            /// <summary>
            /// The object's own size (actual allocation, not attributed impact).
            /// Enables comparing an object's real size vs its impact attribution.
            /// </summary>
            public MemorySize OwnSizeSum => Row.OwnSizeSum;

            /// <summary>
            /// Impact = Exclusive + SharedProRata — the attributed memory without double-counting.
            /// Broken down into NativeSize/ManagedSize/GfxSize, each with Committed/Resident.
            /// </summary>
            public NativeRootSize TotalImpact => Row.TotalImpact;

            /// <summary>
            /// Alias for <see cref="TotalSize"/> (which is needed to fulfill the <see cref="INamedTreeItemData"/> interface),
            /// clarifying that it is indeed the summed up <see cref="TotalImpact"/>. Use this instead of calling
            /// <see cref="NativeRootSize.SumUp()"/> on <see cref="TotalImpact"/> to avoid doing the same math repeatedly.
            /// </summary>
            public MemorySize TotalImpactSum => Row.TotalImpactSum;

            /// <summary>
            /// Memory exclusively owned by this object (not shared with anyone).
            /// </summary>
            public MemorySize ExclusiveImpactSum => Row.ExclusiveImpactSum;

            /// <summary>
            /// Pro-rata share of memory shared with other referees.
            /// If this is a leaf object whose shared memory is 0, it will be excluded from the table by
            /// the default active <see cref="RootsAndImpactModelBuilder.BuildArgs.HideExclusiveOnlyItems"/> option.
            /// </summary>
            public MemorySize SharedProRataSum => Row.SharedProRataSum;

            /// <summary>
            /// Total shared memory if this were the sole owner of the total shared memory of all its references.
            /// Useful for spotting reference hotspots when sorted descending in a flattened view.
            /// </summary>
            public MemorySize SharedInTotalSum => Row.SharedInTotalSum;

            public ObjectStatusWarningInfo WarnInfo => Row.WarnInfo;

            /// <summary>The number of children; 0 marks a leaf, which several cell renderers key off to switch between leaf and group presentation.</summary>
            public int ChildCount => Row.ChildCount;

            /// <summary>
            /// Precomputed status classification (leaked, unused, used, etc.).
            /// </summary>
            public ObjectStatus Status => Row.Status;

            /// <summary>
            /// True for virtual OwnershipBucket group header rows (no real SourceIndex).
            /// </summary>
            public bool IsGroupHeader => Row.IsGroupHeader;

            /// <summary>
            /// True for ancestor nodes that don't match a status filter themselves
            /// but contain descendants that do (for visual de-emphasis).
            /// </summary>
            public bool IsFilterMatchAncestor => Row.IsFilterMatchAncestor;

            /// <summary>
            /// True if the own size of this item includes graphics memory (GfxSize > 0).
            /// </summary>
            public bool OwnSizeIncludesGraphics => Row.OwnSizeIncludesGraphics;

            /// <summary>
            /// The number of referrers this item's impact was divided across (see <see cref="RootAndImpactInfo.GetReferrerCount"/>);
            /// <see cref="RootAndImpactInfo.ReferrerData.SingleReferrer"/> if it never divided and for group-header rows.
            /// </summary>
            public long ReferrerCount => IsValid ? Row.ReferrerCount : RootAndImpactInfo.ReferrerData.SingleReferrer;

            /// <summary>
            /// True if this item's ownership-tree parent received none of its impact (see <see cref="RootAndImpactInfo.TryGetOrphanedSplitRedirect"/>).
            /// </summary>
            public bool ParentExcludedFromSplit => Row.ParentExcludedFromSplit;

            /// <summary>
            /// True if this item's <see cref="ExclusiveImpactSum"/> does not reach a single referrer up the chain as Exclusive,
            /// because its impact was divided or its parent was excluded from the split.
            /// </summary>
            public bool ExclusiveWasShared => ReferrerCount != RootAndImpactInfo.ReferrerData.SingleReferrer || ParentExcludedFromSplit;
        }

        /// <summary>
        /// The impact of the topmost filter-matching descendants beneath a de-emphasized ancestor or group-header row.
        /// Kept in a side-table (see <see cref="TryGetFilteredImpact"/>) so unfiltered rows pay no per-row cost.
        /// </summary>
        public readonly struct FilteredImpact
        {
            public FilteredImpact(NativeRootSize totalImpact, MemorySize exclusiveImpactSum, MemorySize sharedProRataSum, MemorySize sharedInTotalSum)
            {
                TotalImpact = totalImpact;
                ExclusiveImpactSum = exclusiveImpactSum;
                SharedProRataSum = sharedProRataSum;
                SharedInTotalSum = sharedInTotalSum;
            }

            public NativeRootSize TotalImpact { get; }
            public MemorySize ExclusiveImpactSum { get; }
            public MemorySize SharedProRataSum { get; }
            public MemorySize SharedInTotalSum { get; }

            /// <summary>The summed-up <see cref="TotalImpact"/>, matching <see cref="ItemData.TotalImpactSum"/>.</summary>
            public MemorySize TotalImpactSum => TotalImpact.SumUp();

            public static FilteredImpact operator +(FilteredImpact a, FilteredImpact b) => new FilteredImpact(
                a.TotalImpact + b.TotalImpact,
                a.ExclusiveImpactSum + b.ExclusiveImpactSum,
                a.SharedProRataSum + b.SharedProRataSum,
                a.SharedInTotalSum + b.SharedInTotalSum);
        }
    }
}

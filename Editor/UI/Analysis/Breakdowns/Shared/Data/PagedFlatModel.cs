using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    static class FlatPagingLimits
    {
        /// <summary>
        /// Row count above which the Description column header warns that name sorting may be slow: it resolves
        /// a name per row from the snapshot into a managed sort array, unlike the native index sort the size and
        /// status columns use. Sorting stays enabled above the threshold, up to <see cref="NameSortHardLimit"/>.
        /// See <see cref="PagedFlatModel{TRow,TItemData}.SortFlatByName"/>.
        /// </summary>
        public const long NameSortSlowThreshold = 200_000;

        /// <summary>
        /// Row count above which name sorting is disabled outright rather than just warned about: the sort
        /// allocates a managed <c>long[]</c> and <c>string[]</c> sized to the row count, so a set large enough
        /// to need paging in the first place (hundreds of millions of rows) can exhaust the managed heap on its
        /// own, before it even fits comfortably under <see cref="int.MaxValue"/>. Size and status columns are
        /// unaffected: they sort natively and don't allocate proportionally to row count.
        /// </summary>
        public const long NameSortHardLimit = 20_000_000;
    }

    /// <summary>
    /// Shared classification/dispatch logic for a flat sort that may mix the name (Description) column with
    /// typed columns at any priority. Each view's <c>*Model.SortFlatMixed</c> is a thin wrapper around
    /// <see cref="Sort{TColumn}"/>, supplying its own row pointer and typed column comparison; the
    /// fast-path/managed-path decision itself lives here once. <typeparamref name="TColumn"/> is the view's own
    /// comparer enum (e.g. <see cref="ComparisonFlatRowComparer.Column"/>).
    /// </summary>
    static class FlatSortMixing
    {
        /// <summary>
        /// Re-orders the matching rows by <paramref name="levels"/>, in click order. No name level goes through
        /// <paramref name="sortFlatColumns"/> (the fast, native path); a name-only level goes through
        /// <paramref name="sortFlatByName"/> (the fast, name-only path). Only a genuine mix pays for a
        /// managed comparison pass via <paramref name="sortFlatManaged"/>, and even then only up to
        /// <see cref="FlatPagingLimits.NameSortHardLimit"/> matching rows — above it the name level is dropped
        /// (matching <paramref name="sortFlatByName"/>'s own no-op-above-limit safety valve) but any remaining
        /// typed levels still apply, via <paramref name="sortFlatColumns"/>, rather than the whole sort silently
        /// doing nothing.
        /// </summary>
        public static void Sort<TColumn>(
            ReadOnlySpan<FlatSortLevel<TColumn>> levels,
            long matchingRowCount,
            Action<(TColumn, bool)[], bool> sortFlatColumns,
            Action<bool, bool> sortFlatByName,
            Func<Dictionary<long, string>> resolveMatchingNames,
            Action<Comparison<long>, bool> sortFlatManaged,
            Func<long, long, TColumn, int> compareColumn,
            bool materialize)
            where TColumn : unmanaged, Enum
        {
            FlatSortLevel<TColumn>? nameLevel = null;
            var numericLevels = new List<(TColumn, bool)>(levels.Length);
            foreach (var level in levels)
            {
                if (level.IsName)
                    nameLevel = level;
                else
                    numericLevels.Add((level.Column, level.Descending));
            }

            if (nameLevel == null)
            {
                sortFlatColumns(numericLevels.ToArray(), materialize);
                return;
            }
            if (matchingRowCount > FlatPagingLimits.NameSortHardLimit)
            {
                if (numericLevels.Count > 0)
                    sortFlatColumns(numericLevels.ToArray(), materialize);
                return;
            }
            if (numericLevels.Count == 0)
            {
                sortFlatByName(nameLevel.Value.Descending, materialize);
                return;
            }

            var names = resolveMatchingNames();
            var orderedLevels = levels.ToArray();
            sortFlatManaged((a, b) =>
            {
                foreach (var level in orderedLevels)
                {
                    var c = level.IsName
                        ? string.Compare(names[a], names[b], StringComparison.OrdinalIgnoreCase)
                        : compareColumn(a, b, level.Column);
                    if (level.Descending)
                        c = -c;
                    if (c != 0)
                        return c;
                }
                return 0;
            }, materialize);
        }
    }

    /// <summary>
    /// Drives a paged, flattened table view over a <see cref="FlattenedRowStore{TRow}"/>: it keeps the current
    /// sort/filter as an index permutation into the store (<c>m_Order</c>, a native
    /// <see cref="DynamicArray{T}"/> of <c>long</c> indices so it scales past <see cref="int.MaxValue"/> rows)
    /// and materializes only the visible page into the model's <see cref="TreeModel{T}.RootNodes"/> list.
    /// Sorting re-orders the indices, filtering re-selects them and paging slices them, so none of those
    /// operations rebuilds the store or materializes the full row set.
    ///
    /// View-agnostic: the owning model supplies the blittable <typeparamref name="TRow"/>, a tree-id and a
    /// display-name accessor, and a rehydrator keyed by store index rather than by row, so a
    /// <typeparamref name="TItemData"/> that is itself a redirect into the store needs no eager row lookup.
    /// Column sorting is supplied as an unmanaged <see cref="IRefComparer{T}"/> over the index array.
    /// </summary>
    sealed class PagedFlatModel<TRow, TItemData> : IDisposable where TRow : unmanaged
    {
        readonly FlattenedRowStore<TRow> m_Store;
        readonly List<TreeViewItemData<TItemData>> m_PageList;
        readonly int m_PageSize;
        readonly Func<TRow, int> m_GetTreeId;
        readonly Func<TRow, string> m_ResolveName;
        readonly Func<long, TItemData> m_Rehydrate;

        /// <summary>Store-row indices in current filter+sort order; its length is the matching-row count (M).</summary>
        DynamicArray<long> m_Order;
        int m_CurrentPage;

        public PagedFlatModel(
            FlattenedRowStore<TRow> store,
            List<TreeViewItemData<TItemData>> pageList,
            int pageSize,
            Func<TRow, int> getTreeId,
            Func<TRow, string> resolveName,
            Func<long, TItemData> rehydrate)
        {
            m_Store = store;
            m_PageList = pageList;
            m_PageSize = Math.Max(1, pageSize);
            m_GetTreeId = getTreeId;
            m_ResolveName = resolveName;
            m_Rehydrate = rehydrate;

            m_Order = new DynamicArray<long>(0, m_Store.Count, Allocator.Persistent);
            SelectAll();
            MaterializePage(0);
        }

        /// <summary>Total rows in the store (across all pages, ignoring any active filter).</summary>
        public long StoreRowCount => m_Store.Count;

        /// <summary>Rows matching the current filter (the paged set; equals the store count when unfiltered).</summary>
        public long MatchingRowCount => m_Order.Count;

        public int PageSize => m_PageSize;
        public int CurrentPage => m_CurrentPage;

        public int TotalPages
        {
            get
            {
                if (m_Order.Count == 0)
                    return 1;
                return (int)((m_Order.Count + (long)m_PageSize - 1) / m_PageSize);
            }
        }

        void SelectAll()
        {
            var count = m_Store.Count;
            m_Order.Resize(count, false);
            for (long i = 0; i < count; i++)
                m_Order[i] = i;
        }

        /// <summary>
        /// Re-selects the matching rows (no store rebuild), then resets to page 0. The predicate takes a store
        /// index rather than a row so callers can also read side tables such as a resolved-name cache; pass
        /// <c>null</c> to select everything.
        ///
        /// <paramref name="onRow"/>, if supplied, is invoked for EVERY store row in store order with its index
        /// and whether it was kept, before the page is materialized. It exists for owning models whose
        /// aggregation has to see the non-kept rows too (R&amp;I's depth-stack filtered total), so that
        /// view-specific rule stays out of this core.
        ///
        /// <paramref name="materialize"/> is false when this runs on the background sort/filter thread of an
        /// in-place refilter (<see cref="TableViewController{TModel,TItemData}.GetModelSorterTask"/>): the caller
        /// materializes the page itself once back on the main thread, since <see cref="TreeModel{T}.RootNodes"/>
        /// is also read by the UI-bound tree view and mutating it off the main thread races that read.
        /// </summary>
        public void ApplyFilter(Func<long, bool> predicate, Action<long, bool> onRow = null, bool materialize = true)
        {
            if (predicate == null && onRow == null)
            {
                SelectAll();
                ResetToFirstPage(materialize);
                return;
            }

            m_Order.Resize(0, false);
            var count = m_Store.Count;
            for (long i = 0; i < count; i++)
            {
                var kept = predicate == null || predicate(i);
                onRow?.Invoke(i, kept);
                if (kept)
                    m_Order.Push(i);
            }
            ResetToFirstPage(materialize);
        }

        /// <summary>
        /// Re-orders the matching rows with the given unmanaged index comparer, then resets to page 0. The sort
        /// runs natively over the index array, so it scales past <see cref="int.MaxValue"/> rows.
        /// See <see cref="ApplyFilter"/> for <paramref name="materialize"/>.
        /// </summary>
        public void SortFlat<TComparer>(TComparer comparer, bool materialize = true) where TComparer : unmanaged, IRefComparer<long>
        {
            if (m_Order.Count > 1)
            {
                var sortData = ArraySortingData<TComparer>.GetSortDataForSortingAnIndexingArray(m_Order, comparer);
                DynamicArrayAlgorithms.IntrospectiveSort(0, m_Order.Count, ref sortData);
            }
            ResetToFirstPage(materialize);
        }

        /// <summary>
        /// Re-orders the matching rows alphabetically by resolved display name, then resets to page 0. Unlike
        /// <see cref="SortFlat{TComparer}"/> this resolves a name per matching row into managed arrays, so it is
        /// bounded by <see cref="FlatPagingLimits.NameSortHardLimit"/> rather than by the managed array limit.
        /// A row set too large to sort this way is left in its current order rather than throwing or allocating
        /// unboundedly. See <see cref="ApplyFilter"/> for <paramref name="materialize"/>.
        /// </summary>
        public void SortFlatByName(bool descending, bool materialize = true)
        {
            if (m_Order.Count > FlatPagingLimits.NameSortHardLimit)
                return;

            var n = (int)m_Order.Count;
            var indices = new long[n];
            var keys = new string[n];
            for (var i = 0; i < n; i++)
            {
                indices[i] = m_Order[i];
                keys[i] = m_ResolveName(m_Store[indices[i]]);
            }
            Array.Sort(keys, indices, StringComparer.OrdinalIgnoreCase);
            if (descending)
                Array.Reverse(indices);
            for (var i = 0; i < n; i++)
                m_Order[i] = indices[i];
            ResetToFirstPage(materialize);
        }

        /// <summary>
        /// Re-orders the matching rows with an arbitrary managed comparison over store indices, then resets to
        /// page 0. Slower than <see cref="SortFlat{TComparer}"/> (which stays native/Burst-friendly) since it
        /// goes through a boxed delegate per comparison instead of an unmanaged <see cref="IRefComparer{T}"/>;
        /// use this only when the sort needs managed data a native comparer can't touch, e.g. resolved display
        /// names mixed with typed columns. See <see cref="ApplyFilter"/> for <paramref name="materialize"/>.
        /// </summary>
        public void SortFlatManaged(Comparison<long> comparer, bool materialize = true)
        {
            if (m_Order.Count > 1)
            {
                var n = (int)m_Order.Count;
                var indices = new long[n];
                for (var i = 0; i < n; i++)
                    indices[i] = m_Order[i];
                Array.Sort(indices, comparer);
                for (var i = 0; i < n; i++)
                    m_Order[i] = indices[i];
            }
            ResetToFirstPage(materialize);
        }

        /// <summary>
        /// Resolves and returns the display name of every currently matching row, keyed by store index, for a
        /// caller building a managed comparison (<see cref="SortFlatManaged"/>) that mixes name with other
        /// criteria. Resolves each row once, up front, rather than repeatedly during the sort. Only call this
        /// when the matching row count is within a caller-enforced bound (e.g.
        /// <see cref="FlatPagingLimits.NameSortHardLimit"/>) — unlike <see cref="SortFlatByName"/>, this has no
        /// bound of its own.
        /// </summary>
        public Dictionary<long, string> ResolveMatchingNames()
        {
            var names = new Dictionary<long, string>((int)m_Order.Count);
            for (long i = 0; i < m_Order.Count; i++)
            {
                var index = m_Order[i];
                if (!names.ContainsKey(index))
                    names[index] = m_ResolveName(m_Store[index]);
            }
            return names;
        }

        /// <summary>Resets to page 0, materializing it immediately unless deferred to the main thread by the caller.</summary>
        void ResetToFirstPage(bool materialize)
        {
            if (materialize)
                MaterializePage(0);
            else
                m_CurrentPage = 0;
        }

        /// <summary>
        /// Locates the page and in-page offset of a store row in the current filter+sort order. m_Order holds
        /// no reverse index, so this is a linear scan over the matching rows (M) — an accepted cost for a
        /// manual reveal click, not a hot path.
        /// </summary>
        public bool TryGetPageForRow(long storeIndex, out int page)
        {
            for (long i = 0; i < m_Order.Count; i++)
            {
                if (m_Order[i] != storeIndex)
                    continue;
                page = (int)(i / m_PageSize);
                return true;
            }
            page = 0;
            return false;
        }

        /// <summary>Refills the model's RootNodes list with the rows of <paramref name="page"/> (clamped).</summary>
        public void MaterializePage(int page)
        {
            m_CurrentPage = Math.Clamp(page, 0, TotalPages - 1);
            m_PageList.Clear();

            var start = (long)m_CurrentPage * m_PageSize;
            var end = Math.Min(start + m_PageSize, m_Order.Count);
            for (var i = start; i < end; i++)
            {
                var index = m_Order[i];
                ref readonly var row = ref m_Store[index];
                var data = m_Rehydrate(index);
                m_PageList.Add(new TreeViewItemData<TItemData>(m_GetTreeId(row), data));
            }
        }

        /// <summary>
        /// Disposes only the sort/filter order array. Store ownership stays with the caller, which may refcount
        /// it across several derived models, so this core cannot assume it holds the only reference.
        /// </summary>
        public void Dispose()
        {
            if (m_Order.IsCreated)
                m_Order.Dispose();
        }
    }
}

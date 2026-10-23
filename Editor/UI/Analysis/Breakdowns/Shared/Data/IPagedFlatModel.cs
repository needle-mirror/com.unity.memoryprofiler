namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// The subset of a flattened, paginated breakdown model (<see cref="RootsAndImpactModel"/>,
    /// <see cref="UnityObjectsModel"/>) that <see cref="FlatPagerBinding{TColumn}"/> needs to drive the pager.
    /// Deliberately excludes column sorting, whose parameter type is a view-specific comparer enum.
    /// </summary>
    interface IPagedFlatModel
    {
        /// <summary>True for the flattened, paginated model; false for the hierarchical / id-filtered / empty model.</summary>
        bool IsPaged { get; }

        /// <summary>Number of flattened rows matching the current filter (0 for a non-paged model).</summary>
        long FlattenedRowsTotal { get; }

        /// <summary>Zero-based index of the page currently materialized into the tree view's root items.</summary>
        int CurrentPage { get; }

        /// <summary>Number of pages the flattened rows span; always at least 1 (1 for a non-paged model).</summary>
        int TotalPages { get; }

        /// <summary>Materializes the given page into the tree view's root items.</summary>
        void MaterializePage(int page);

        /// <summary>Re-orders the flattened rows alphabetically by name, then shows page 0.</summary>
        void SortFlatByName(bool descending, bool materialize = true);

        /// <summary>The page holding store row <paramref name="storeIndex"/> in the current filter+sort order; false when the model isn't paged or its filter excludes the row.</summary>
        bool TryGetPageForRow(long storeIndex, out int page);

        /// <summary>The flattened row's tree-view id, for selecting it once its page is materialized.</summary>
        int GetTreeIdForStoreIndex(long storeIndex);
    }
}

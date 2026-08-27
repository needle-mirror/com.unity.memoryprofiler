using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unity.MemoryProfiler.Editor.UI
{
    interface IExclusiveStatusFilterManager
    {
        /// <summary>
        /// Bitmask of allowed <see cref="ObjectStatus"/> values (bit N = status N allowed).
        /// <c>null</c> means no filter is active (all statuses pass).
        /// </summary>
        public ulong? ExclusiveStatusMask { get; set; }
    }

    class TableFilterManagerBase : IExclusiveStatusFilterManager
    {
        public ulong? ExclusiveStatusMask { get; set; }
    }

    class TableFilterManager<TModel, TItemData> : TableFilterManagerBase
        where TModel : TreeModel<TItemData>
        where TItemData : IPrivateComparableItemData
    {
        public CompoundSearchFilter CompoundSearchFilter
        {
            get
            {
                var text = SearchFilter?.FilterValue;
                if (string.IsNullOrEmpty(text))
                    text = null;
                if (text == null && ExclusiveStatusMask == null)
                    return null;
                return new CompoundSearchFilter(text, exclusiveStatusMask: ExclusiveStatusMask);
            }
        }
        public IScopedFilter<string> SearchFilter { get; set; }
        public IScopedFilter<ObjectStatus> StatusFilter
        {
            get
            {
                if (SearchFilter == null || string.IsNullOrEmpty(SearchFilter.FilterValue))
                    return null;

                // Precompute which statuses match the search string (avoids per-object string comparison)
                HashSet<ObjectStatus> searchStringMatchingStatuses = ObjectStatusExtensions.GetMatchingStatuses(SearchFilter.FilterValue);
                return ScopedEnumFilter<ObjectStatus>.Create(searchStringMatchingStatuses, ObjectStatus.None);
            }
        }
        public ITextFilter NameFilter { get; set; }
        public IEnumerable<ITextFilter> PathFilters { get; set; }
        /// <summary>
        /// Should be null if no filtering is expected and a full model is to be built, instead of one derived from <see cref="BaseModelForTreeNodeIdFiltering"/>.
        /// If <see cref="BaseModelForTreeNodeIdFiltering"/> is null but this isn't, no model is built.
        /// (This case is used when initializing Base and Compared tables for Comparison tables to avoid these base tables building a model on UI initialization.)
        ///
        /// Pass an empty list to result in an empty model.
        /// </summary>
        public IList<int> TreeNodeIdFilter { get; set; }
        /// <summary>
        /// Used together with <see cref="TreeNodeIdFilter"/> to build a derived model that only contains some of the entries, filtered by their TreeNode Id.
        /// Can be null while <see cref="TreeNodeIdFilter"/> is non-null to avoid building a model, e.g. during Comparsion table initialization UI where the Comparison table is still building all three models.
        /// </summary>
        public TModel BaseModelForTreeNodeIdFiltering { get; set; }

        // Optional Unity Objects specific filters (null for tables that don't need them)
        public ITextFilter TypeNameFilter { get; set; }

        public void ClearFilters()
        {
            SearchFilter = null;
            NameFilter = null;
            PathFilters = null;
            TreeNodeIdFilter = null;
            BaseModelForTreeNodeIdFiltering = default;
            TypeNameFilter = null;
        }

        public async Task ApplyFiltersAsync(
            Func<Task> rebuildCallback,
            bool isViewLoaded)
        {
            if (isViewLoaded)
                await rebuildCallback();
        }

        public void SetTreeNodeIdFilter(List<int> filter)
        {
            TreeNodeIdFilter = filter;
        }

        public void SetExcludeAllFilter()
        {
            TreeNodeIdFilter = TreeViewControllerHelper.ShowNoItemsAtAllFilter;
        }
    }
}

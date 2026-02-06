using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unity.MemoryProfiler.Editor.UI
{
    class TableFilterManager<TModel, TItemData>
        where TModel : TreeModel<TItemData>
        where TItemData : IPrivateComparableItemData
    {
        public IScopedFilter<string> SearchFilter { get; set; }
        public ITextFilter NameFilter { get; set; }
        public IEnumerable<ITextFilter> PathFilters { get; set; }
        public List<int> TreeNodeIdFilter { get; set; }
        public TModel BaseModelForTreeNodeIdFiltering { get; set; }

        // Optional Unity Objects specific filters (null for tables that don't need them)
        public ITextFilter TypeNameFilter { get; set; }
        public IEntityIdFilter EntityIdFilter { get; set; }

        public bool ExcludeAllFilterApplied => TreeNodeIdFilter != null && TreeNodeIdFilter.Count == 0;

        public void ClearFilters()
        {
            SearchFilter = null;
            NameFilter = null;
            PathFilters = null;
            TreeNodeIdFilter = null;
            BaseModelForTreeNodeIdFiltering = default;
            TypeNameFilter = null;
            EntityIdFilter = null;
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
            TreeNodeIdFilter = new List<int>();
        }
    }
}

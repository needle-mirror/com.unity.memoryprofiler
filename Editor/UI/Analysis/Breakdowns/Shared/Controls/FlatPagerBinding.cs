using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// One entry of the active sort state for a flattened, paged table's header, in the column's click order.
    /// The name (Description) column has no comparer enum value of its own — it goes through a separate,
    /// managed string comparison rather than the typed column's unmanaged one — so it's tagged
    /// <see cref="IsName"/> instead of carrying a <see cref="Column"/> value.
    /// </summary>
    readonly struct FlatSortLevel<TColumn> where TColumn : unmanaged, Enum
    {
        public bool IsName { get; init; }
        public TColumn Column { get; init; }
        public bool Descending { get; init; }
    }

    /// <summary>
    /// The active sort state for a flattened, paged table's header, as read from
    /// <see cref="FlatPagerBinding{TColumn}.ReadSortState"/>, in click order (primary first).
    /// </summary>
    readonly struct FlatSortRequest<TColumn> where TColumn : unmanaged, Enum
    {
        public FlatSortLevel<TColumn>[] Levels { get; init; }
    }

    /// <summary>
    /// Shared glue between a <see cref="PaginationControl"/> and a flattened, paged breakdown table's controller:
    /// page-click handling, header sort-state capture, and the pager-visibility/tooltip sync that follows a model
    /// rebuild. <typeparamref name="TColumn"/> is the table's own flat-row comparer enum (e.g.
    /// <see cref="RootsAndImpactFlatRowComparer.Column"/>); everything else here is mechanical and view-neutral.
    /// </summary>
    sealed class FlatPagerBinding<TColumn> where TColumn : unmanaged, Enum
    {
        readonly PaginationControl m_Pager;
        readonly Action<string, string> m_SetColumnTooltip;
        readonly string m_DescriptionColumnId;
        readonly string m_BarColumnId;
        readonly Dictionary<string, TColumn> m_ColumnMap;
        readonly Func<TColumn> m_DefaultColumn;
        readonly string m_DefaultTooltip;
        readonly int m_MaxSortLevels;
        readonly Func<IPagedFlatModel> m_CurrentModel;
        readonly Action m_RefreshTreeViewRoots;

        public FlatPagerBinding(
            PaginationControl pager,
            Action<string, string> setColumnTooltip,
            string descriptionColumnId,
            string barColumnId,
            Dictionary<string, TColumn> columnMap,
            Func<TColumn> defaultColumn,
            string defaultTooltip,
            int maxSortLevels,
            Func<IPagedFlatModel> currentModel,
            Action refreshTreeViewRoots)
        {
            m_Pager = pager;
            m_SetColumnTooltip = setColumnTooltip;
            m_DescriptionColumnId = descriptionColumnId;
            m_BarColumnId = barColumnId;
            m_ColumnMap = columnMap;
            m_DefaultColumn = defaultColumn;
            m_DefaultTooltip = defaultTooltip;
            m_MaxSortLevels = maxSortLevels;
            m_CurrentModel = currentModel;
            m_RefreshTreeViewRoots = refreshTreeViewRoots;

            m_Pager.PageSelected += OnPageSelected;
        }

        /// <summary>The comparer column the active table mode falls back to when no size/status column is sorted.</summary>
        public TColumn DefaultColumn => m_DefaultColumn();

        void OnPageSelected(int page)
        {
            var model = m_CurrentModel();
            if (model == null || !model.IsPaged)
                return;
            ShowPage(model, page);
        }

        void ShowPage(IPagedFlatModel model, int page)
        {
            model.MaterializePage(page);
            m_RefreshTreeViewRoots();
            m_Pager.SetState(model.CurrentPage, model.TotalPages);
        }

        /// <summary>Shows the page holding store row <paramref name="storeIndex"/>, for selecting it by the returned <paramref name="treeId"/>.</summary>
        public bool TryShowRow(long storeIndex, out int treeId)
        {
            treeId = -1;
            var model = m_CurrentModel();
            if (model == null || !model.TryGetPageForRow(storeIndex, out var page))
                return false;

            ShowPage(model, page);
            treeId = model.GetTreeIdForStoreIndex(storeIndex);
            return true;
        }

        /// <summary>
        /// Shows/hides the pager for <paramref name="model"/> and, when paged, syncs its state and refreshes the
        /// name column's slow-sort tooltip. Call from <c>OnModelRebuild</c>; pass null (or a non-paged model) to
        /// hide the pager, e.g. on a failed rebuild.
        /// </summary>
        public void SyncTo(IPagedFlatModel model)
        {
            var paged = model != null && model.IsPaged;
            UIElementsHelper.SetElementDisplay(m_Pager, paged && model.TotalPages > 1);
            if (paged)
                m_Pager.SetState(model.CurrentPage, model.TotalPages);

            UpdateNameColumnTooltip(paged ? model.FlattenedRowsTotal : 0);
        }

        void UpdateNameColumnTooltip(long flattenedRowCount)
        {
            // Below NameSortSlowThreshold: default tooltip. Between it and NameSortHardLimit: warn that sorting
            // is slow, but still allow it — the user can decide whether the wait is acceptable. Above
            // NameSortHardLimit: PagedFlatModel.SortFlatByName no-ops rather than allocating unboundedly, so say
            // sorting is disabled instead of promising a slow sort that never happens.
            string tooltip;
            if (flattenedRowCount > FlatPagingLimits.NameSortHardLimit)
                tooltip = string.Format(TextContent.FlattenedNameSortDisabledTooltip, flattenedRowCount);
            else if (flattenedRowCount > FlatPagingLimits.NameSortSlowThreshold)
                tooltip = string.Format(TextContent.FlattenedNameSortSlowTooltip, flattenedRowCount);
            else
                tooltip = m_DefaultTooltip;
            m_SetColumnTooltip(m_DescriptionColumnId, tooltip);
        }

        public bool TryResolveColumn(string columnId, out TColumn column)
        {
            if (columnId == m_BarColumnId)
            {
                column = DefaultColumn;
                return true;
            }
            return m_ColumnMap.TryGetValue(columnId, out column);
        }

        /// <summary>
        /// Reads the tree view header's active sort columns, in click order (primary first). The name
        /// (Description) column is included in that order rather than split out, so a caller building a mixed
        /// sort can honor it at whatever priority the user actually picked. Main-thread only — call
        /// synchronously from <c>GetModelSorterTask</c>'s prologue, before the async continuation, since
        /// <paramref name="treeView"/> is a UI-thread-only object.
        /// </summary>
        public FlatSortRequest<TColumn> ReadSortState(MultiColumnTreeView treeView)
        {
            var levels = new List<FlatSortLevel<TColumn>>();
            var columnLevelCount = 0;
            var sortedColumns = treeView.sortedColumns;
            if (sortedColumns != null)
            {
                foreach (var sortedColumn in sortedColumns)
                {
                    if (sortedColumn == null)
                        continue;
                    var descending = sortedColumn.direction == SortDirection.Descending;
                    if (sortedColumn.columnName == m_DescriptionColumnId)
                    {
                        levels.Add(new FlatSortLevel<TColumn> { IsName = true, Descending = descending });
                        continue;
                    }
                    if (columnLevelCount < m_MaxSortLevels && TryResolveColumn(sortedColumn.columnName, out var column))
                    {
                        levels.Add(new FlatSortLevel<TColumn> { Column = column, Descending = descending });
                        columnLevelCount++;
                    }
                }
            }

            return new FlatSortRequest<TColumn> { Levels = levels.ToArray() };
        }

        /// <summary>Unsubscribes from the pager. Call from the controller's Dispose.</summary>
        public void Detach()
        {
            m_Pager.PageSelected -= OnPageSelected;
        }
    }
}

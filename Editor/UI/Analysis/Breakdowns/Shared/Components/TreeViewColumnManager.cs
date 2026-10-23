using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class ColumnDefinition<TItemData>
    {
        const string k_CellClassName = "unity-tree-view__item-content";
        public string Title { get; set; }
        public string Tooltip { get; set; }
        public Texture2D Icon { get; set; }
        public int Width { get; set; }
        public Action<VisualElement, int> BindCell { get; set; }
        public Func<VisualElement> MakeCell { get; set; }
        public Action<VisualElement> DestroyCell { get; set; }
        public Func<TreeViewItemData<TItemData>, IComparable> SortKeySelector { get; set; }
        public bool Visible { get; set; } = true;
        public bool Sortable { get; set; } = true;

        /// <summary>
        /// Optional total for this column over the filtered dataset, shown after the header title; returns 0 while
        /// there is no model. A column without a provider shows no total. Ignored when <see cref="PagedSumProvider"/>
        /// is set.
        /// </summary>
        public Func<ulong> SumProvider { get; set; }

        /// <summary>
        /// Optional total for a paged model whose per-row values exist only for the materialized page. Takes
        /// priority over <see cref="SumProvider"/>.
        /// </summary>
        public Func<PagedColumnSum> PagedSumProvider { get; set; }

        /// <summary>
        /// Overrides the total's tooltip format (<c>{0}</c> is the exact byte count) for a column whose total is not
        /// over the topmost rows only.
        /// </summary>
        public string SumTooltipFormat { get; set; }

        /// <summary>
        /// The header this column is bound to, so totals and tooltips update without rebuilding the headers.
        /// </summary>
        public ColumnHeaderBinding HeaderBinding { get; set; }

        public ColumnDefinition(
            string title,
            Action<VisualElement, int> bindCell,
            string tooltip = null,
            Texture2D icon = null,
            int width = 0,
            Func<VisualElement> makeCell = null,
            Action<VisualElement> destroyCell = null,
            Func<TreeViewItemData<TItemData>, IComparable> sortKeySelector = null,
            bool visible = true,
            bool sortable = true,
            Func<ulong> sumProvider = null,
            Func<PagedColumnSum> pagedSumProvider = null,
            string sumTooltipFormat = null)
        {
            Title = title;
            SumTooltipFormat = sumTooltipFormat;
            Tooltip = tooltip;
            Icon = icon;
            Width = width;
            BindCell = bindCell;
            SumProvider = sumProvider;
            PagedSumProvider = pagedSumProvider;
            if (makeCell != null)
            {
                // Ensure all cells are properly wrapped in a container with the correct class for styling.
                MakeCell = () =>
                {
                    var cell = makeCell();
                    var cellWrapper = new VisualElement()
                    {
                        name = k_CellClassName,
                    };
                    cellWrapper.AddToClassList(k_CellClassName);
                    cellWrapper.Add(cell);
                    return cellWrapper;
                };
            }
            DestroyCell = destroyCell;
            SortKeySelector = sortKeySelector;
            Visible = visible;
            Sortable = sortable;
        }
    }

    class TreeViewColumnManager<TItemData> : IColumnSumRefresher
    {
        readonly MultiColumnTreeView m_TreeView;
        readonly Dictionary<string, ColumnDefinition<TItemData>> m_ColumnDefinitions;

        public TreeViewColumnManager(MultiColumnTreeView treeView)
        {
            m_TreeView = treeView;
            m_ColumnDefinitions = new Dictionary<string, ColumnDefinition<TItemData>>();
        }

        public void ConfigureColumn(string columnId, ColumnDefinition<TItemData> definition)
        {
            m_ColumnDefinitions[columnId] = definition;
            ApplyColumnConfiguration(columnId, definition);
        }

        public void ConfigureColumn(
            string columnId,
            string columnTitle,
            int width,
            Action<VisualElement, int> bindCell,
            string headerTooltip = null,
            Func<VisualElement> makeCell = null,
            Action<VisualElement> destroyCell = null,
            bool visible = true,
            bool sortable = true,
            Func<ulong> sumProvider = null,
            Func<PagedColumnSum> pagedSumProvider = null,
            string sumTooltipFormat = null)
        {
            var definition = new ColumnDefinition<TItemData>(
                columnTitle,
                bindCell,
                headerTooltip,
                icon: null,
                width,
                makeCell,
                destroyCell,
                visible: visible,
                sortable: sortable,
                sumProvider: sumProvider,
                pagedSumProvider: pagedSumProvider,
                sumTooltipFormat: sumTooltipFormat);

            ConfigureColumn(columnId, definition);
        }

        void ApplyColumnConfiguration(string columnId, ColumnDefinition<TItemData> definition)
        {

            var column = m_TreeView.columns[columnId];
            column.title = definition.Title;
            column.bindCell = definition.BindCell;
            column.visible = definition.Visible;
            column.sortable = definition.Sortable;
            // The default header has picking mode Ignore, so the tooltip on its title label never shows. To get a
            // working tooltip we override makeHeader (otherwise bindHeader isn't called) and rebuild a header with the
            // same structure and class names as the default one so it keeps the default styling.
            column.makeHeader = ColumnHeaderWithSum.MakeHeader;
            column.bindHeader = (header) => BindHeader(header, definition);

            if (definition.Width != 0)
            {
                column.width = definition.Width;
                column.minWidth = definition.Width / 2;
                column.maxWidth = definition.Width * 2;
            }

            if (definition.MakeCell != null)
                column.makeCell = definition.MakeCell;

            if (definition.DestroyCell != null)
                column.destroyCell = definition.DestroyCell;
        }

        void BindHeader(VisualElement header, ColumnDefinition<TItemData> definition)
        {
            definition.HeaderBinding = ColumnHeaderWithSum.BindHeader(
                header, definition.Title, definition.Tooltip, definition.Icon);
            RefreshColumnSum(definition);
        }

        /// <summary>
        /// Re-evaluates every column total. Call after the model was rebuilt or refiltered.
        /// </summary>
        public void InvalidateRowCaches() { }

        public void RefreshColumnSums()
        {
            foreach (var definition in m_ColumnDefinitions.Values)
                RefreshColumnSum(definition);
        }

        static void RefreshColumnSum(ColumnDefinition<TItemData> definition)
        {
            if (definition.PagedSumProvider != null)
                ColumnHeaderWithSum.SetPagedByteSizeSum(definition.HeaderBinding, definition.PagedSumProvider());
            else if (definition.SumProvider != null)
                ColumnHeaderWithSum.SetByteSizeSum(definition.HeaderBinding, definition.SumProvider(), definition.SumTooltipFormat);
            else
                definition.HeaderBinding?.SetSum(null, null);
        }

        public void SetColumnVisibility(string columnId, bool visible)
        {
            if (m_ColumnDefinitions.TryGetValue(columnId, out var definition))
            {
                definition.Visible = visible;
            }

            if (m_TreeView.columns.Contains(columnId))
            {
                m_TreeView.columns[columnId].visible = visible;
            }
        }

        /// <summary>
        /// Updates the stored tooltip for a column and refreshes the rendered header element. Pass <c>null</c> to
        /// fall back to the column title.
        /// </summary>
        public void SetColumnTooltip(string columnId, string tooltip)
        {
            if (!m_ColumnDefinitions.TryGetValue(columnId, out var definition))
                return;

            definition.Tooltip = tooltip;

            // Update the already-rendered header directly using the reference cached by BindHeader — a column's
            // bindHeader delegate also captures 'definition' by reference, so the next header re-bind picks up the
            // new value regardless.
            definition.HeaderBinding?.SetTooltip(definition.Tooltip);
        }

        public void ApplyTableMode(
            AllTrackedMemoryTableMode mode,
            string sizeColumnId,
            string residentSizeColumnId)
        {
            switch (mode)
            {
                case AllTrackedMemoryTableMode.OnlyResident:
                    SetColumnVisibility(sizeColumnId, false);
                    SetColumnVisibility(residentSizeColumnId, true);
                    break;
                case AllTrackedMemoryTableMode.OnlyCommitted:
                    SetColumnVisibility(sizeColumnId, true);
                    SetColumnVisibility(residentSizeColumnId, false);
                    break;
                case AllTrackedMemoryTableMode.CommittedAndResident:
                    SetColumnVisibility(sizeColumnId, true);
                    SetColumnVisibility(residentSizeColumnId, true);
                    break;
            }
        }

        public Dictionary<string, Comparison<TreeViewItemData<TItemData>>> BuildSortComparisons(
            Dictionary<string, Func<TreeViewItemData<TItemData>, IComparable>> sortKeySelectors)
        {
            var comparisons = new Dictionary<string, Comparison<TreeViewItemData<TItemData>>>();

            foreach (var kvp in sortKeySelectors)
            {
                var columnId = kvp.Key;
                var keySelector = kvp.Value;
                comparisons[columnId] = (x, y) =>
                {
                    var keyX = keySelector(x);
                    var keyY = keySelector(y);
                    return keyX.CompareTo(keyY);
                };
            }

            return comparisons;
        }

        public void ResizeToFit()
        {
            var oldStretchMode = new Dictionary<string, bool>(m_ColumnDefinitions.Count);
            foreach (var columnDefinition in m_ColumnDefinitions)
            {
                oldStretchMode[columnDefinition.Key] = m_TreeView.columns[columnDefinition.Key].stretchable;
                m_TreeView.columns[columnDefinition.Key].stretchable = true;
            }
            // give the layouting a chance to adjust to the changed configuration
            m_TreeView.schedule.Execute(() => DelayedResizeToFit(oldStretchMode)).ExecuteLater(0);
        }

        void DelayedResizeToFit(Dictionary<string, bool> oldStretchMode)
        {
            m_TreeView.TryResizeColumnsToFit();

            foreach (var columnDefinition in m_ColumnDefinitions)
            {
                m_TreeView.columns[columnDefinition.Key].stretchable = oldStretchMode[columnDefinition.Key];
            }
        }
    }
}

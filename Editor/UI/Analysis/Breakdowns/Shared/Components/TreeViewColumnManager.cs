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
        /// The header element the tooltip was last applied to, cached by <see cref="MultiColumnHeaderBuilder.Bind"/>
        /// so a later tooltip update can be written directly instead of re-querying the tree view for it.
        /// </summary>
        public VisualElement TooltipTarget { get; set; }

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
            bool sortable = true)
        {
            Title = title;
            Tooltip = tooltip;
            Icon = icon;
            Width = width;
            BindCell = bindCell;
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

    class TreeViewColumnManager<TItemData>
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
            bool sortable = true)
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
                sortable: sortable);

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
            column.makeHeader = MakeHeader;
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

        VisualElement MakeHeader() => MultiColumnHeaderBuilder.MakeHeader(out _, out _);

        void BindHeader(VisualElement header, ColumnDefinition<TItemData> definition)
        {
            definition.TooltipTarget = MultiColumnHeaderBuilder.Bind(header, definition.Title, definition.Tooltip, definition.Icon);
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
            if (definition.TooltipTarget != null)
                definition.TooltipTarget.tooltip = definition.Tooltip ?? definition.Title;
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

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class ColumnDefinition<TItemData>
    {
        public string Title { get; set; }
        public int Width { get; set; }
        public Action<VisualElement, int> BindCell { get; set; }
        public Func<VisualElement> MakeCell { get; set; }
        public Func<TreeViewItemData<TItemData>, IComparable> SortKeySelector { get; set; }
        public bool Visible { get; set; } = true;

        public ColumnDefinition(
            string title,
            Action<VisualElement, int> bindCell,
            int width = 0,
            Func<VisualElement> makeCell = null,
            Func<TreeViewItemData<TItemData>, IComparable> sortKeySelector = null,
            bool visible = true)
        {
            Title = title;
            Width = width;
            BindCell = bindCell;
            MakeCell = makeCell;
            SortKeySelector = sortKeySelector;
            Visible = visible;
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
            Func<VisualElement> makeCell = null,
            bool visible = true)
        {
            var definition = new ColumnDefinition<TItemData>(
                columnTitle,
                bindCell,
                width,
                makeCell,
                visible: visible);

            ConfigureColumn(columnId, definition);
        }

        void ApplyColumnConfiguration(string columnId, ColumnDefinition<TItemData> definition)
        {
            var column = m_TreeView.columns[columnId];
            column.title = definition.Title;
            column.bindCell = definition.BindCell;
            column.visible = definition.Visible;

            if (definition.Width != 0)
            {
                column.width = definition.Width;
                column.minWidth = definition.Width / 2;
                column.maxWidth = definition.Width * 2;
            }

            if (definition.MakeCell != null)
                column.makeCell = definition.MakeCell;
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
    }
}

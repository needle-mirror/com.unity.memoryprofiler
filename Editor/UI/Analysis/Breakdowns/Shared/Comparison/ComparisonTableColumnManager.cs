using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Manages column configuration for comparison tables.
    /// Provides common comparison-specific column types (delta columns, count/size in A/B, etc.)
    /// </summary>
    class ComparisonTableColumnManager<TItemData>
    {
        readonly MultiColumnTreeView m_TreeView;

        public ComparisonTableColumnManager(MultiColumnTreeView treeView)
        {
            m_TreeView = treeView;
        }

        /// <summary>
        /// Configure a generic column with custom binding and cell factory.
        /// </summary>
        public void ConfigureColumn(
            string columnId,
            string title,
            int width,
            Action<VisualElement, int> bindCell,
            Func<VisualElement> makeCell = null,
            bool visible = true)
        {
            var column = m_TreeView.columns[columnId];
            column.title = title;
            column.bindCell = bindCell;
            column.visible = visible;

            if (width != 0)
            {
                column.width = width;
                column.minWidth = width / 2;
                column.maxWidth = width * 2;
            }

            if (makeCell != null)
                column.makeCell = makeCell;
        }

        /// <summary>
        /// Configure a count delta column that shows the difference in counts between snapshots.
        /// </summary>
        public void ConfigureCountDeltaColumn(
            string columnId,
            string title,
            Func<TItemData, int> getCountDelta,
            int width = 120)
        {
            ConfigureColumn(
                columnId,
                title,
                width,
                BindCellForCountDelta(getCountDelta),
                CountDeltaCell.Instantiate);
        }

        /// <summary>
        /// Configure a size delta bar column that shows a proportional bar for size differences.
        /// </summary>
        public void ConfigureSizeDeltaBarColumn(
            string columnId,
            string title,
            Func<TItemData, long> getSizeDelta,
            Func<long> getLargestAbsoluteSizeDelta,
            int width = 180)
        {
            ConfigureColumn(
                columnId,
                title,
                width,
                BindCellForSizeDeltaBar(getSizeDelta, getLargestAbsoluteSizeDelta),
                DeltaBarCell.Instantiate);
        }

        /// <summary>
        /// Configure a size delta column that shows the numeric difference in size.
        /// </summary>
        public void ConfigureSizeDeltaColumn(
            string columnId,
            string title,
            Func<TItemData, long> getSizeDelta,
            int width = 120)
        {
            ConfigureColumn(
                columnId,
                title,
                width,
                BindCellForSizeDelta(getSizeDelta),
                MakeSizeDeltaCell);
        }

        /// <summary>
        /// Configure a size column that shows a size value (used for "Size in A" or "Size in B").
        /// </summary>
        public void ConfigureSizeColumn(
            string columnId,
            string title,
            Func<TItemData, long> getSize,
            int width = 120)
        {
            ConfigureColumn(
                columnId,
                title,
                width,
                BindCellForSize(getSize));
        }

        /// <summary>
        /// Configure a count column that shows a count value (used for "Count in A" or "Count in B").
        /// </summary>
        public void ConfigureCountColumn(
            string columnId,
            string title,
            Func<TItemData, int> getCount,
            int width = 100)
        {
            ConfigureColumn(
                columnId,
                title,
                width,
                BindCellForCount(getCount));
        }

        Action<VisualElement, int> BindCellForCountDelta(Func<TItemData, int> getCountDelta)
        {
            return (element, rowIndex) =>
            {
                var cell = (CountDeltaCell)element;
                var itemData = m_TreeView.GetItemDataForIndex<TItemData>(rowIndex);
                var countDelta = getCountDelta(itemData);
                cell.SetCountDelta(countDelta);
            };
        }

        Action<VisualElement, int> BindCellForSizeDeltaBar(
            Func<TItemData, long> getSizeDelta,
            Func<long> getLargestAbsoluteSizeDelta)
        {
            return (element, rowIndex) =>
            {
                var cell = (DeltaBarCell)element;
                var itemData = m_TreeView.GetItemDataForIndex<TItemData>(rowIndex);
                var sizeDelta = getSizeDelta(itemData);
                var proportionalSizeDelta = 0f;
                if (sizeDelta != 0)
                {
                    var largestDelta = getLargestAbsoluteSizeDelta();
                    if (largestDelta != 0)
                        proportionalSizeDelta = (float)sizeDelta / largestDelta;
                }
                cell.SetDeltaScalar(proportionalSizeDelta);
                cell.tooltip = FormatBytes(sizeDelta);
            };
        }

        Action<VisualElement, int> BindCellForSizeDelta(Func<TItemData, long> getSizeDelta)
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<TItemData>(rowIndex);
                var sizeDelta = getSizeDelta(itemData);
                ((Label)element).text = FormatBytes(sizeDelta);
            };
        }

        Action<VisualElement, int> BindCellForSize(Func<TItemData, long> getSize)
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<TItemData>(rowIndex);
                var size = getSize(itemData);
                ((Label)element).text = FormatBytes(size);
            };
        }

        Action<VisualElement, int> BindCellForCount(Func<TItemData, int> getCount)
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<TItemData>(rowIndex);
                var count = getCount(itemData);
                ((Label)element).text = $"{count:N0}";
            };
        }

        VisualElement MakeSizeDeltaCell()
        {
            var cell = new Label();
            cell.AddToClassList("unity-multi-column-view__cell__label");
            // Make this a cell with a darkened background for visual distinction
            cell.AddToClassList("dark-tree-view-cell");
            return cell;
        }

        static string FormatBytes(long bytes)
        {
            var sizeText = new System.Text.StringBuilder();

            // Our built-in formatter for bytes doesn't support negative values
            if (bytes < 0)
                sizeText.Append("-");

            var absoluteBytes = Math.Abs(bytes);
            sizeText.Append(EditorUtility.FormatBytes(absoluteBytes));
            return sizeText.ToString();
        }
    }
}

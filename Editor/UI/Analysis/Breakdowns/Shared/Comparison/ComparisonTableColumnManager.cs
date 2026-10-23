using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Manages column configuration for comparison tables.
    /// Provides common comparison-specific column types (delta columns, count/size in A/B, etc.)
    /// </summary>
    class ComparisonTableColumnManager<TItemData> : IColumnSumRefresher
    {
        class ColumnHeaderInfo
        {
            public string Title;
            public string Tooltip;

            /// <summary>
            /// Optional signed total for this column, shown after the header title. Null for a column without a total.
            /// </summary>
            public Func<long> SumProvider;

            /// <summary>
            /// The header this column is bound to, so totals and tooltips update without rebuilding the headers.
            /// </summary>
            public ColumnHeaderBinding HeaderBinding;
        }

        readonly MultiColumnTreeView m_TreeView;

        /// <summary>
        /// Whether size delta bars scale within their parent row instead of to the table's largest delta.
        /// A parent's net delta can be smaller than a child's, so the scale comes from the siblings (see
        /// <see cref="GetSiblingDeltaScale"/>). Rows without a parent always scale to the table's largest delta.
        /// </summary>
        public bool ScaleBarsToParent { get; set; }

        /// <summary>
        /// Wires a footer toggle to <see cref="ScaleBarsToParent"/>. Toggling only rebinds the visible rows.
        /// </summary>
        public void SetUpScaleBarsToParentToggle(Toggle toggle, string sessionStateKey)
        {
            ScaleBarsToParent = ScaleBarsToParentOption.SetUp(toggle, sessionStateKey, value =>
            {
                ScaleBarsToParent = value;
                m_TreeView.RefreshItems();
            }, TextContent.ScaleBarsToParentComparisonToggleTooltip);
        }

        /// <summary>
        /// Per parent id: the parent's delta at the time of computing, and its children's bar scale.
        /// </summary>
        readonly Dictionary<int, (long ParentDelta, long Scale)> m_SiblingDeltaScaleCache = new Dictionary<int, (long, long)>();

        readonly Dictionary<string, ColumnHeaderInfo> m_ColumnHeaders = new Dictionary<string, ColumnHeaderInfo>();

        public ComparisonTableColumnManager(MultiColumnTreeView treeView)
        {
            m_TreeView = treeView;
        }

        /// <summary>
        /// Updates the stored tooltip for a column and refreshes the rendered header element. Pass <c>null</c> to
        /// fall back to the column title.
        /// </summary>
        public void SetColumnTooltip(string columnId, string tooltip)
        {
            if (!m_ColumnHeaders.TryGetValue(columnId, out var headerInfo))
                return;

            headerInfo.Tooltip = tooltip;

            // Update the already-rendered header directly using the reference cached by BindHeader — the column's
            // bindHeader delegate also captures 'headerInfo' by reference, so the next header re-bind picks up the
            // new value regardless.
            headerInfo.HeaderBinding?.SetTooltip(headerInfo.Tooltip);
        }

        /// <summary>
        /// Configure a generic column with custom binding and cell factory. <paramref name="sumProvider"/> optionally
        /// supplies the column's signed total, shown in the header.
        /// </summary>
        public void ConfigureColumn(
            string columnId,
            string title,
            int width,
            Action<VisualElement, int> bindCell,
            Func<VisualElement> makeCell = null,
            Action<VisualElement> destroyCell = null,
            bool visible = true,
            string headerTooltip = null,
            Func<long> sumProvider = null)
        {
            var column = m_TreeView.columns[columnId];
            column.title = title;
            column.bindCell = bindCell;
            column.visible = visible;

            var headerInfo = new ColumnHeaderInfo { Title = title, Tooltip = headerTooltip, SumProvider = sumProvider };
            m_ColumnHeaders[columnId] = headerInfo;
            // The default header has picking mode Ignore, so the tooltip on its title label never shows. To get a
            // working tooltip we override makeHeader (otherwise bindHeader isn't called) and rebuild a header with
            // the same structure and class names as the default one so it keeps the default styling.
            column.makeHeader = ColumnHeaderWithSum.MakeHeader;
            column.bindHeader = header => BindHeader(header, headerInfo);

            if (width != 0)
            {
                column.width = width;
                column.minWidth = width / 2;
                column.maxWidth = width * 2;
            }

            if (makeCell != null)
                column.makeCell = makeCell;

            if (destroyCell != null)
                column.destroyCell = destroyCell;
        }

        void BindHeader(VisualElement header, ColumnHeaderInfo headerInfo)
        {
            headerInfo.HeaderBinding = ColumnHeaderWithSum.BindHeader(
                header, headerInfo.Title, headerInfo.Tooltip, icon: null);
            RefreshColumnSum(headerInfo);
        }

        /// <summary>
        /// Re-evaluates every column total. Call after the model was rebuilt or refiltered.
        /// </summary>
        public void InvalidateRowCaches() => m_SiblingDeltaScaleCache.Clear();

        public void RefreshColumnSums()
        {
            foreach (var headerInfo in m_ColumnHeaders.Values)
                RefreshColumnSum(headerInfo);
        }

        static void RefreshColumnSum(ColumnHeaderInfo headerInfo)
        {
            if (headerInfo.SumProvider == null)
            {
                headerInfo.HeaderBinding?.SetSum(null, null);
                return;
            }

            var sum = headerInfo.SumProvider();
            var formatted = FormatBytes(sum);
            headerInfo.HeaderBinding?.SetSum(
                string.Format(TextContent.TableColumnHeaderSumFormat, formatted),
                string.Format(TextContent.TableColumnHeaderSumTooltipFormat, formatted, $"{sum:N0}"));
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
            int width = 120,
            Func<long> sumProvider = null)
        {
            ConfigureColumn(
                columnId,
                title,
                width,
                BindCellForSizeDelta(getSizeDelta),
                MakeSizeDeltaCell,
                sumProvider: sumProvider);
        }

        /// <summary>
        /// Configure a size column that shows a size value (used for "Size in A" or "Size in B").
        /// </summary>
        public void ConfigureSizeColumn(
            string columnId,
            string title,
            Func<TItemData, long> getSize,
            int width = 120,
            Func<long> sumProvider = null)
        {
            ConfigureColumn(
                columnId,
                title,
                width,
                BindCellForSize(getSize),
                sumProvider: sumProvider);
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
                var cell = element.Q<CountDeltaCell>();
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
                var cell = element.Q<DeltaBarCell>();
                var itemData = m_TreeView.GetItemDataForIndex<TItemData>(rowIndex);
                var sizeDelta = getSizeDelta(itemData);
                var proportionalSizeDelta = 0f;
                if (sizeDelta != 0)
                {
                    var largestDelta = getLargestAbsoluteSizeDelta();
                    if (ScaleBarsToParent)
                    {
                        var parentId = m_TreeView.GetParentIdForIndex(rowIndex);
                        if (parentId >= 0)
                        {
                            var siblingScale = GetSiblingDeltaScale(parentId, getSizeDelta);
                            if (siblingScale != 0)
                                largestDelta = siblingScale;
                        }
                    }
                    if (largestDelta != 0)
                        proportionalSizeDelta = (float)sizeDelta / largestDelta;
                }
                cell.SetDeltaScalar(proportionalSizeDelta);
                cell.tooltip = FormatBytes(sizeDelta);
            };
        }

        /// <summary>
        /// The larger of the summed positive and summed absolute negative deltas of <paramref name="parentId"/>'s
        /// children, so no child's bar exceeds full width and increases and decreases share one scale.
        /// </summary>
        long GetSiblingDeltaScale(int parentId, Func<TItemData, long> getSizeDelta)
        {
            var parentDelta = getSizeDelta(m_TreeView.GetItemDataForId<TItemData>(parentId));
            if (m_SiblingDeltaScaleCache.TryGetValue(parentId, out var cached) && cached.ParentDelta == parentDelta)
                return cached.Scale;

            long positiveSum = 0;
            long negativeSum = 0;
            foreach (var childId in m_TreeView.viewController.GetChildrenIds(parentId))
            {
                var childDelta = getSizeDelta(m_TreeView.GetItemDataForId<TItemData>(childId));
                if (childDelta > 0)
                    positiveSum += childDelta;
                else
                    negativeSum -= childDelta;
            }

            var scale = Math.Max(positiveSum, negativeSum);
            m_SiblingDeltaScaleCache[parentId] = (parentDelta, scale);
            return scale;
        }

        Action<VisualElement, int> BindCellForSizeDelta(Func<TItemData, long> getSizeDelta)
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<TItemData>(rowIndex);
                var sizeDelta = getSizeDelta(itemData);
                element.Q<Label>().text = FormatBytes(sizeDelta);
            };
        }

        Action<VisualElement, int> BindCellForSize(Func<TItemData, long> getSize)
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<TItemData>(rowIndex);
                var size = getSize(itemData);
                element.Q<Label>().text = FormatBytes(size);
            };
        }

        Action<VisualElement, int> BindCellForCount(Func<TItemData, int> getCount)
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<TItemData>(rowIndex);
                var count = getCount(itemData);
                element.Q<Label>().text = $"{count:N0}";
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

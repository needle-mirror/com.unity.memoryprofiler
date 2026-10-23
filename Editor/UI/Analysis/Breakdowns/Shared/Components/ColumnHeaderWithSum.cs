using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// A column manager whose headers show per-column totals, refreshed from
    /// <see cref="TreeViewController{TModel, TTreeItemData}.RefreshView"/>.
    /// </summary>
    interface IColumnSumRefresher
    {
        void RefreshColumnSums();

        /// <summary>
        /// Drops state derived from the current rows. Call before the tree view swaps in or rebinds new rows, so
        /// cells that bind during that refresh never read state computed for the previous model.
        /// </summary>
        void InvalidateRowCaches();
    }

    /// <summary>
    /// A bound column header that can show a total after its title, e.g. "Allocated Size (104.2 MB)".
    /// The total is part of the title text, so a narrow column elides it first; the tooltip always shows it in full.
    /// </summary>
    sealed class ColumnHeaderBinding
    {
        readonly Label m_TitleLabel;
        readonly VisualElement m_TooltipTarget;
        readonly string m_Title;
        string m_Tooltip;
        string m_SumText;
        string m_SumTooltip;

        public ColumnHeaderBinding(Label titleLabel, VisualElement tooltipTarget, string title, string tooltip)
        {
            m_TitleLabel = titleLabel;
            m_TooltipTarget = tooltipTarget;
            m_Title = title;
            m_Tooltip = tooltip;
            Apply();
        }

        /// <summary>Replaces the column's tooltip; pass null to fall back to the column title.</summary>
        public void SetTooltip(string tooltip)
        {
            m_Tooltip = tooltip;
            Apply();
        }

        /// <summary>
        /// Shows <paramref name="text"/> after the title and <paramref name="tooltip"/> below the column tooltip.
        /// A null or empty <paramref name="text"/> shows the plain header.
        /// </summary>
        public void SetSum(string text, string tooltip)
        {
            m_SumText = text;
            m_SumTooltip = tooltip;
            Apply();
        }

        void Apply()
        {
            var hasSum = !string.IsNullOrEmpty(m_SumText);
            if (m_TitleLabel != null)
                m_TitleLabel.text = hasSum ? $"{m_Title} {m_SumText}" : m_Title;

            var tooltip = m_Tooltip ?? m_Title;
            if (hasSum && !string.IsNullOrEmpty(m_SumTooltip))
                tooltip = $"{tooltip}\n\n{m_SumTooltip}";
            m_TooltipTarget.tooltip = tooltip;
        }
    }

    /// <summary>
    /// Builds and binds the shared multi-column header with an optional total after the title.
    /// Callers pass finished text, so byte sizes and signed deltas use the same header.
    /// </summary>
    static class ColumnHeaderWithSum
    {
        /// <summary>
        /// Rebuilds the default header with the same structure and class names, because the default one has picking
        /// mode Ignore and never shows a tooltip. Overriding makeHeader is also what makes bindHeader get called.
        /// </summary>
        public static VisualElement MakeHeader()
        {
            return MultiColumnHeaderBuilder.MakeHeader(out _, out _);
        }

        /// <summary>
        /// Applies <paramref name="title"/>, <paramref name="icon"/> and <paramref name="tooltip"/> to a header built
        /// by <see cref="MakeHeader"/> and returns the binding, for the caller to keep and update.
        /// </summary>
        public static ColumnHeaderBinding BindHeader(VisualElement header, string title, string tooltip, Texture2D icon)
        {
            var tooltipTarget = MultiColumnHeaderBuilder.Bind(header, title, tooltip, icon);
            var titleLabel = header.Q<Label>(MultiColumnHeaderClassNames.ColumnHeaderTitleName);
            return new ColumnHeaderBinding(titleLabel, tooltipTarget, title, tooltip);
        }

        /// <summary>The total for a byte size, e.g. "(104.2 MB)", with the exact byte count in the tooltip.</summary>
        public static void SetByteSizeSum(ColumnHeaderBinding binding, ulong sum, string tooltipFormat = null)
        {
            var formatted = EditorUtility.FormatBytes((long)sum);
            binding?.SetSum(
                string.Format(TextContent.TableColumnHeaderSumFormat, formatted),
                string.Format(tooltipFormat ?? TextContent.TableColumnHeaderSumTooltipFormat, formatted, $"{sum:N0}"));
        }

        /// <summary>
        /// The total for a column whose value can differ between the current page and the whole filtered
        /// dataset, e.g. "(page: 4.1 MB / all: 104.2 MB)" — or the plain single-value form when the page covers
        /// everything. Shows the plain header for a sum that is not applicable.
        /// </summary>
        public static void SetPagedByteSizeSum(ColumnHeaderBinding binding, PagedColumnSum sum)
        {
            if (!sum.IsApplicable)
            {
                binding?.SetSum(null, null);
                return;
            }

            if (sum.PageTotal == sum.ModelTotal)
            {
                SetByteSizeSum(binding, sum.ModelTotal);
                return;
            }

            var page = EditorUtility.FormatBytes((long)sum.PageTotal);
            var all = EditorUtility.FormatBytes((long)sum.ModelTotal);
            binding?.SetSum(
                string.Format(TextContent.TableColumnHeaderPagedSumFormat, page, all),
                string.Format(TextContent.TableColumnHeaderPagedSumTooltipFormat,
                    page, $"{sum.PageTotal:N0}", all, $"{sum.ModelTotal:N0}"));
        }
    }

    /// <summary>
    /// A column total for the materialized page and the whole filtered dataset. For an unpaged model both are the
    /// model total. The default value is a sum that does not apply to the column in its current state.
    /// </summary>
    readonly struct PagedColumnSum
    {
        public PagedColumnSum(ulong pageTotal, ulong modelTotal)
        {
            IsApplicable = true;
            PageTotal = pageTotal;
            ModelTotal = modelTotal;
        }

        public static PagedColumnSum Unpaged(ulong modelTotal) => new PagedColumnSum(modelTotal, modelTotal);

        public bool IsApplicable { get; }
        public ulong PageTotal { get; }
        public ulong ModelTotal { get; }
    }
}

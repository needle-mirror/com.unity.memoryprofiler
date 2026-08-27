using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// A page-flipping control rendering <c>&lt; 1 [2] [3] … [n] &gt;</c>: previous/next arrows (disabled at the
    /// ends), the first and last page always shown, a window of pages around the current one, and <c>…</c>
    /// gaps. Once <see cref="BuildPageItems"/> switches into windowed mode (see its doc) the block count between
    /// the arrows is always exactly <see cref="MaxBlockCount"/>, so flipping pages never changes the control's
    /// width. The current page's button is shown disabled. Page indices are zero-based internally and reported
    /// via <see cref="PageSelected"/>; the visible labels are one-based.
    /// </summary>
#if UNITY_6000_0_OR_NEWER
    [UxmlElement]
#endif
    internal partial class PaginationControl : VisualElement
    {
        /// <summary>Sentinel in <see cref="BuildPageItems"/> output marking an elided range ("…").</summary>
        public const int Ellipsis = -1;

        /// <summary>How many pages either side of the current one <see cref="BuildPageItems"/> keeps visible.</summary>
        const int SiblingCount = 1;

        /// <summary>
        /// Block count between (and including) the first and last page once windowing kicks in:
        /// first, last, two sibling pages either side of current, the current page itself, and two ellipsis slots.
        /// </summary>
        const int MaxBlockCount = 2 + 2 * SiblingCount + 1 + 2;

        const string k_UssClassName = "memory-profiler-pager";
        const string k_PagesUssClassName = "memory-profiler-pager__pages";
        const string k_ButtonUssClassName = "memory-profiler-pager__button";
        const string k_CurrentButtonUssClassName = "memory-profiler-pager__button--current";
        const string k_EllipsisUssClassName = "memory-profiler-pager__ellipsis";

        int m_CurrentPage;
        int m_TotalPages = 1;

        /// <summary>Raised with the zero-based page index the user clicked (never the current page).</summary>
        public event Action<int> PageSelected;

        public PaginationControl()
        {
            AddToClassList(k_UssClassName);
        }

        /// <summary>Updates the control to show <paramref name="totalPages"/> pages with <paramref name="currentPage"/> active, and rebuilds the buttons.</summary>
        public void SetState(int currentPage, int totalPages)
        {
            m_TotalPages = Math.Max(1, totalPages);
            m_CurrentPage = Math.Clamp(currentPage, 0, m_TotalPages - 1);
            Rebuild();
        }

        void Rebuild()
        {
            Clear();

            // The prev/next arrows sit on either side of a page-number window whose block count is fixed by
            // BuildPageItems, so the window's width stays constant and the arrows keep their position — they
            // don't slip out from under the cursor when the page is flipped repeatedly.
            AddArrow(this, "‹", TextContent.PagerPreviousPage, m_CurrentPage - 1, m_CurrentPage > 0);

            var pages = new VisualElement();
            pages.AddToClassList(k_PagesUssClassName);
            foreach (var page in BuildPageItems(m_CurrentPage, m_TotalPages))
            {
                if (page == Ellipsis)
                {
                    var ellipsis = new Label("…");
                    ellipsis.AddToClassList(k_EllipsisUssClassName);
                    pages.Add(ellipsis);
                    continue;
                }

                var isCurrent = page == m_CurrentPage;
                var targetPage = page;
                var button = new Button(() => PageSelected?.Invoke(targetPage)) { text = (page + 1).ToString() };
                button.AddToClassList(k_ButtonUssClassName);
                if (isCurrent)
                {
                    button.AddToClassList(k_CurrentButtonUssClassName);
                    button.SetEnabled(false);
                }
                pages.Add(button);
            }
            Add(pages);

            AddArrow(this, "›", TextContent.PagerNextPage, m_CurrentPage + 1, m_CurrentPage < m_TotalPages - 1);
        }

        void AddArrow(VisualElement parent, string glyph, string tooltip, int targetPage, bool enabled)
        {
            var button = new Button(() => PageSelected?.Invoke(targetPage)) { text = glyph, tooltip = tooltip };
            button.AddToClassList(k_ButtonUssClassName);
            button.SetEnabled(enabled);
            parent.Add(button);
        }

        /// <summary>
        /// Computes the page-button sequence for <paramref name="current"/> of <paramref name="total"/> pages:
        /// the first and last page, up to <see cref="SiblingCount"/> pages either side of the current one, and
        /// <see cref="Ellipsis"/> wherever a gap between included pages is elided. Pure logic, unit-tested
        /// without the editor. All page values are zero-based.
        ///
        /// The block count is only constant while windowing is active. Below <see cref="MaxBlockCount"/> total
        /// pages there are not enough pages to need a window at all — every page is listed and the block count
        /// equals <paramref name="total"/>, which cannot jitter either, since <paramref name="total"/> does not
        /// change while flipping pages. From <see cref="MaxBlockCount"/> pages up, exactly <see cref="MaxBlockCount"/>
        /// blocks are always returned: a gap collapsed by proximity to an end (see below) is not omitted, it is
        /// spent on one more real page number instead, so the sequence never shrinks or grows as the current page
        /// changes — only which pages/gaps occupy those blocks does.
        /// </summary>
        public static List<int> BuildPageItems(int current, int total)
        {
            var result = new List<int>();
            if (total <= 0)
                return result;
            current = Math.Clamp(current, 0, total - 1);

            if (total <= MaxBlockCount)
            {
                for (var p = 0; p < total; p++)
                    result.Add(p);
                return result;
            }

            var leftSibling = Math.Max(current - SiblingCount, 0);
            var rightSibling = Math.Min(current + SiblingCount, total - 1);

            // A gap is only worth an ellipsis block if it elides 2+ pages; a single hidden page is cheaper to
            // just show. Below that threshold on a side, the window is extended into that side's slack instead
            // of leaving the block unused, which is what keeps the total block count fixed at MaxBlockCount.
            var showLeftGap = leftSibling > 2;
            var showRightGap = rightSibling < total - 3;

            result.Add(0);
            if (showLeftGap)
                result.Add(Ellipsis);

            if (showLeftGap && showRightGap)
            {
                for (var p = leftSibling; p <= rightSibling; p++)
                    result.Add(p);
            }
            else
            {
                // One side has no gap: its ellipsis block is unused, so the window is extended by one extra
                // page to fill that slot instead, keeping the total block count at MaxBlockCount either way.
                var fillCount = MaxBlockCount - 3;
                if (showRightGap)
                {
                    for (var p = 1; p <= fillCount; p++)
                        result.Add(p);
                }
                else
                {
                    for (var p = total - 1 - fillCount; p < total - 1; p++)
                        result.Add(p);
                }
            }

            if (showRightGap)
                result.Add(Ellipsis);
            result.Add(total - 1);
            return result;
        }

#if !UNITY_6000_0_OR_NEWER
        public new class UxmlFactory : UxmlFactory<PaginationControl> { }
#endif
    }
}

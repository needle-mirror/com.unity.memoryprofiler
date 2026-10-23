using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// A size cell showing a primary value and, optionally, a struck-through, dimmed secondary value beside it.
    /// With a single value it renders like a plain size <see cref="Label"/> cell.
    /// </summary>
    class DualSizeCell : VisualElement
    {
        const string k_UssClassName = "dual-size-cell";
        const string k_PrimaryUssClassName = "dual-size-cell__primary";
        const string k_SecondaryUssClassName = "dual-size-cell__secondary";

        readonly Label m_Primary;
        readonly Label m_Secondary;

        public DualSizeCell()
        {
            AddToClassList(k_UssClassName);

            m_Primary = new Label { displayTooltipWhenElided = false };
            m_Primary.AddToClassList(k_PrimaryUssClassName);
            Add(m_Primary);

            m_Secondary = new Label { enableRichText = true, displayTooltipWhenElided = false };
            m_Secondary.AddToClassList(k_SecondaryUssClassName);
            Add(m_Secondary);
        }

        /// <summary>Shows a single value with the secondary label hidden.</summary>
        public void SetSingle(string text, string tooltip, bool unreliable, string unreliableCssClass)
        {
            m_Primary.text = text;
            if (unreliable)
                m_Primary.AddToClassList(unreliableCssClass);
            else
                m_Primary.RemoveFromClassList(unreliableCssClass);

            UIElementsHelper.SetVisibility(m_Secondary, false);

            tooltip ??= string.Empty;
            m_Primary.tooltip = tooltip;
            this.tooltip = tooltip;
        }

        /// <summary>
        /// Shows <paramref name="primaryText"/> followed by <paramref name="secondaryText"/> struck through and dimmed.
        /// </summary>
        public void SetDual(string primaryText, string secondaryText, string tooltip, string unreliableCssClass)
        {
            m_Primary.text = primaryText;
            m_Primary.RemoveFromClassList(unreliableCssClass);

            m_Secondary.text = $"<s>{secondaryText}</s>";
            UIElementsHelper.SetVisibility(m_Secondary, true);

            tooltip ??= string.Empty;
            m_Primary.tooltip = tooltip;
            m_Secondary.tooltip = tooltip;
            this.tooltip = tooltip;
        }
    }
}

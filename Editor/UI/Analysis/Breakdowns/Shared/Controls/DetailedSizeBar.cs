#if UNITY_2022_1_OR_NEWER
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // A UI Component used to display a size quantity as a fraction of a whole, such as on the Unity Objects breakdown tables.
    // Contains a progress bar to visually show the relative size, along with a footer that contains two labels for describing the values, one left-aligned and one right-aligned.
    class DetailedSizeBar : VisualElement
    {
        const string k_UxmlClass = "detailed-size-bar";
        const string k_UxmlClass_BarContainer = "detailed-size-bar__bar-container";
        const string k_UxmlClass_Bar = "detailed-size-bar__bar";
        const string k_UxmlClass_BarRemainder = "detailed-size-bar__bar-remainder";
        const string k_UxmlClass_Footer = "detailed-size-bar__footer";
        const string k_UxmlClass_SizeLabel = "detailed-size-bar__size-label";
        const string k_UxmlClass_TotalLabel = "detailed-size-bar__total-label";

        VisualElement m_Reminder;

        public DetailedSizeBar()
        {
            var barContainer = new VisualElement();
            barContainer.AddToClassList(k_UxmlClass_BarContainer);
            Add(barContainer);

            Bar = new MemoryBarElement();
            Bar.AddToClassList(k_UxmlClass_Bar);
            barContainer.Add(Bar);

            m_Reminder = new VisualElement();
            m_Reminder.AddToClassList(k_UxmlClass_BarRemainder);
            barContainer.Add(m_Reminder);

            var footer = new VisualElement()
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                }
            };
            footer.AddToClassList(k_UxmlClass_Footer);
            Add(footer);

            SizeLabel = new Label();
            SizeLabel.AddToClassList(k_UxmlClass_SizeLabel);
            footer.Add(SizeLabel);

            // Adding spacer rather than having SizeLabel grow, so that
            // its tooltip appears in the correct position.
            SpacerElement = new VisualElement()
            {
                style =
                {
                    flexGrow = 1,
                }
            };
            footer.Add(SpacerElement);

            TotalLabel = new Label();
            TotalLabel.AddToClassList(k_UxmlClass_TotalLabel);
            footer.Add(TotalLabel);

            AddToClassList(k_UxmlClass);
        }

        public MemoryBarElement Bar { get; }

        Label SizeLabel { get; }

        VisualElement SpacerElement { get; }

        Label TotalLabel { get; }

        public void SetValue(MemorySize size, ulong total, ulong maxValue)
        {
            Bar.Set(string.Empty, size, total, maxValue);

            // Leftover filler, for compare mode when 100% of total might be
            // not 100% of visual as snapshot size is different
            float remainderPerc = maxValue > 0 ? (float)(maxValue - total) / maxValue * 100 : 0;
            m_Reminder.style.width = new StyleLength(Length.Percent(remainderPerc));
        }

        public void SetSizeText(string text, string sizeTooltip)
        {
            SizeLabel.text = text;
            SizeLabel.tooltip = sizeTooltip;
            SizeLabel.displayTooltipWhenElided = false;
        }

        public void SetTotalText(string text, string sizeTooltip)
        {
            TotalLabel.text = text;
            TotalLabel.tooltip = sizeTooltip;
            TotalLabel.displayTooltipWhenElided = false;
        }

        public new class UxmlFactory : UxmlFactory<DetailedSizeBar> {}
    }
}
#endif

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class MemoryUsageBreakdownElement : VisualElement
    {
        internal const string ElementHoveredPseudoState = "memory-usage-breakdown__element--hovered";
        internal const string ElementSelectedPseudoState = "memory-usage-breakdown__element--selected";
        static class Content
        {
            public static readonly string SelectedFormatStringPartOfTooltip = L10n.Tr("\nSelected: {0}\n({1:0.0}% of {2})");
            public static readonly string Reserved = L10n.Tr("Reserved");
            public static readonly string UsedFormatStringPartOfTooltip = L10n.Tr(" Used: {0}\n({1:0.0}% of {2})");
            public static readonly string ReservedFormatStringPartOfTooltip = L10n.Tr("{0}: {1}\n({2:0.0}% of {3})");
            public static readonly string ReservedClarificationForReservedPartOfTooltip = L10n.Tr("\nReserved");
        }

        public struct SnapshotStats
        {
            public ulong totalBytes;
            public ulong usedBytes;
            public ulong selectedBytes;
        }

        public SnapshotStats[] stats = new SnapshotStats[3];

        public enum StatsIdx
        {
            a = 0,
            b = 1,
            Diff
        };

        public event Action<int> Selected = delegate {};
        public event Action<int> Deselected = delegate {};
        public int Id { get; private set; }

        public string Text { get; private set; }

        public long TotalBytes(StatsIdx idx)
        {
            return (long)stats[(int)idx].totalBytes;
        }

        public bool ShowUsed { get; private set; }

        public bool ShowSelected { get; private set; }

        public float PercentageUsed(StatsIdx idx)
        {
            return PercentageUsed((int)idx);
        }

        public float PercentageUsed(int idx)
        {
            return stats[idx].totalBytes == 0 ? 100 : stats[idx].usedBytes / (float)stats[idx].totalBytes * 100;
        }

        public float PercentageSelected(StatsIdx idx)
        {
            return PercentageSelected((int)idx);
        }

        public float PercentageSelected(int idx)
        {
            return stats[idx].totalBytes == 0 ? 100 : stats[idx].selectedBytes / (float)stats[idx].totalBytes * 100;
        }

        public string BackgroundColorClass { get; private set; }

        VisualElement[] m_ReservedElement = new VisualElement[2];
        VisualElement[] m_BackgroundElement = new VisualElement[2];
        VisualElement[] m_UsedElement = new VisualElement[2];
        VisualElement[] m_SelectedElement = new VisualElement[2];

        MemoryUsageBreakdown m_BreakdownParent;
        VisualElement m_ColorBox;
        Label m_RowName;
        Label m_RowASize;
        Label m_RowBSize;
        Label m_DiffSize;
        bool m_BarsSetup;
        public bool[] barValuesSet = new bool[2];

        public MemoryUsageBreakdownElement(string text, string backgroundColorClass, bool showUsed = false, bool showSelected = false) : this()
        {
            Text = text;
            BackgroundColorClass = backgroundColorClass;
            ShowUsed = showUsed;
            ShowSelected = showSelected;

            if (!string.IsNullOrEmpty(backgroundColorClass))
                AddToClassList(backgroundColorClass);
        }

        public MemoryUsageBreakdownElement() : base()
        {
        }

        void Init(string text, bool showUsed, ulong[] used, ulong[] total, bool showSelected, ulong[] selected, string backgroundColorClass)
        {
            Text = text;
            ShowUsed = showUsed;
            stats[0].usedBytes = used[0];
            stats[0].totalBytes = total[0];
            stats[1].usedBytes = used[1];
            stats[1].totalBytes = total[1];
            ShowSelected = showSelected;
            stats[0].selectedBytes = selected[0];
            stats[1].selectedBytes = selected[1];
            BackgroundColorClass = backgroundColorClass;
        }

        void OnGeometryChangedEvent(GeometryChangedEvent e)
        {
            for (int i = 0; i < m_ReservedElement.Length; i++)
            {
                var backgroundColor = m_ReservedElement[i].resolvedStyle.backgroundColor;
                var outlineColor = backgroundColor;
                if (ShowUsed)
                {
                    backgroundColor.a = 0.3f;
                }

                m_ReservedElement[i].style.backgroundColor = backgroundColor;
                m_ReservedElement[i].style.borderBottomColor = outlineColor;
                m_ReservedElement[i].style.borderTopColor = outlineColor;
                m_ReservedElement[i].style.borderLeftColor = outlineColor;
                m_ReservedElement[i].style.borderRightColor = outlineColor;
            }

            UnregisterCallback<GeometryChangedEvent>(OnGeometryChangedEvent);
        }

        public void SetValues(ulong[] total, ulong[] used, ulong selected = 0, bool showSelected = false)
        {
            ShowSelected = showSelected;
            stats[0].selectedBytes = stats[1].selectedBytes = selected;

            stats[0].usedBytes = used[0];
            stats[0].totalBytes = total[0];
            stats[1].usedBytes = used[1];
            stats[1].totalBytes = total[1];
            UpdateDisplayToValues();
        }

        void UpdateDisplayToValues()
        {
            var tooltipText = BuildTooltipText(m_BreakdownParent.HeaderText, Text, (ulong)m_BreakdownParent.GetTotalBytes(0), stats[0].totalBytes, ShowUsed, stats[0].usedBytes, ShowSelected, stats[0].selectedBytes);
            tooltip = tooltipText;
            m_RowASize.text = BuildRowSizeText(stats[0].totalBytes, stats[0].usedBytes, ShowUsed, ShowSelected);

            m_RowBSize.text = BuildRowSizeText(stats[1].totalBytes, stats[1].usedBytes, ShowUsed, ShowSelected);
            stats[2].totalBytes = stats[0].totalBytes > stats[1].totalBytes ? stats[0].totalBytes - stats[1].totalBytes : stats[1].totalBytes - stats[0].totalBytes;
            stats[2].usedBytes = stats[0].usedBytes > stats[1].usedBytes ? stats[0].usedBytes - stats[1].usedBytes : stats[1].usedBytes - stats[0].usedBytes;
            m_DiffSize.text = BuildRowSizeText(stats[2].totalBytes, stats[2].usedBytes, ShowUsed, ShowSelected);
            m_RowName.tooltip = m_RowASize.tooltip = tooltip;
            SetBarElements();
        }

        public void SetSelected(ulong selectedBytesA, ulong selectedBytesB)
        {
            ShowSelected = true;
            stats[0].selectedBytes = selectedBytesA;
            stats[1].selectedBytes = selectedBytesB;
            UpdateDisplayToValues();
        }

        public void ClearSelected()
        {
            ShowSelected = false;
        }

        public void UpdateText(string breakdownName)
        {
            Text = breakdownName;
            tooltip = BuildTooltipText(m_BreakdownParent.HeaderText, Text, (ulong)m_BreakdownParent.GetTotalBytes(0), stats[0].totalBytes, ShowUsed, stats[0].usedBytes, ShowSelected, stats[0].selectedBytes);
            m_RowName.text = Text;
            m_RowName.tooltip = m_RowASize.tooltip = tooltip;
        }

        public static string BuildTooltipText(string memoryBreakDownName, string elementName, ulong totalBytes, ulong reservedBytes, bool showUsed = false, ulong usedBytes = 0, bool showSelected = false, ulong selectedBytes = 0)
        {
            // Unity/Other Used: 27MB
            // (90% of Reserved)
            // Reserved: 30 MB
            // (90% of Total Memory)
            // Selected: 1MB
            // (XX% of Unity/Other)

            // or

            // Unity/Other: 30 MB
            // (90% of Total Memory)
            // Selected: 1MB
            // (XX% of Unity/Other)
            var selectedText = showSelected ? string.Format(Content.SelectedFormatStringPartOfTooltip, EditorUtility.FormatBytes((long)selectedBytes), selectedBytes / (float)reservedBytes * 100, showUsed ? Content.Reserved : elementName) : "";
            var usedText = showUsed ? string.Format(Content.UsedFormatStringPartOfTooltip, EditorUtility.FormatBytes((long)usedBytes), usedBytes / (float)reservedBytes * 100, showUsed ? Content.Reserved : elementName) : "";
            var reservedText = string.Format(Content.ReservedFormatStringPartOfTooltip, showUsed ? Content.ReservedClarificationForReservedPartOfTooltip : "", EditorUtility.FormatBytes((long)reservedBytes), reservedBytes / (float)totalBytes * 100, memoryBreakDownName);
            return string.Format("{0}{1}{2}{3}",
                elementName,
                usedText,
                reservedText,
                selectedText);
        }

        public static string BuildRowSizeText(ulong totalBytes, ulong usedBytes, bool showUsed, bool showSelected = false, ulong selectedBytes = 0)
        {
            if (showUsed)
            {
                var total = EditorUtility.FormatBytes((long)totalBytes);
                var used = EditorUtility.FormatBytes((long)usedBytes);

                // check if the last two characters are the same (i.e. " B" "KB" ...) so we can drop unnecesary unit qualifiers
                if (total[total.Length - 1] == used[used.Length - 1]
                    && total[total.Length - 2] == used[used.Length - 2])
                {
                    used = used.Substring(0, used.Length - (used[used.Length - 2] == ' ' ? 2 : 3));
                }
                return string.Format("{0} / {1}", used, total);
            }
            else
            {
                return EditorUtility.FormatBytes((long)totalBytes);
            }
        }

        void SetBarElements()
        {
            for (int i = 0; i < m_UsedElement.Length; i++)
            {
                var percentageUsed = PercentageUsed(i);
                m_UsedElement[i].style.SetBarWidthInPercent(percentageUsed);
                UIElementsHelper.SetVisibility(m_SelectedElement[i], ShowSelected);
                var percentageSelected = PercentageSelected(i);
                m_SelectedElement[i].style.SetBarWidthInPercent(percentageSelected);
            }
            for (int i = 0; i < barValuesSet.Length; i++)
            {
                // Only used and selected have been set yet, not the total
                barValuesSet[i] = false;
            }
        }

        public void Setup(MemoryUsageBreakdown memoryUsageBreakdown, int id, VisualElement colorBox, Label rowName, Label rowASize, Label rowBSize = null, Label diffSize = null)
        {
            SetUpBars();
            Id = id;
            m_BreakdownParent = memoryUsageBreakdown;
            m_RowName = rowName;
            m_RowName.parent.RegisterClickEvent(OnSelected);
            m_RowName.parent.RegisterCallback<MouseEnterEvent>(OnMouseEnter);
            m_RowName.parent.RegisterCallback<MouseLeaveEvent>(OnMouseLeave);
            m_ColorBox = colorBox;
            m_RowASize = rowASize;
            m_RowASize.RegisterClickEvent(OnSelected);
            m_RowASize.RegisterCallback<MouseEnterEvent>(OnMouseEnter);
            m_RowASize.RegisterCallback<MouseLeaveEvent>(OnMouseLeave);
            m_RowBSize = rowBSize;
            m_RowBSize.RegisterClickEvent(OnSelected);
            m_RowBSize.RegisterCallback<MouseEnterEvent>(OnMouseEnter);
            m_RowBSize.RegisterCallback<MouseLeaveEvent>(OnMouseLeave);
            m_DiffSize = diffSize;
            m_DiffSize.RegisterClickEvent(OnSelected);
            m_DiffSize.RegisterCallback<MouseEnterEvent>(OnMouseEnter);
            m_DiffSize.RegisterCallback<MouseLeaveEvent>(OnMouseLeave);

            if (!string.IsNullOrEmpty(BackgroundColorClass))
                m_ColorBox.AddToClassList(BackgroundColorClass);

            m_RowName.text = Text;

            UIElementsHelper.SetVisibility(m_ColorBox.Q<VisualElement>(ElementAndStyleNames.ColorBoxUnused), ShowUsed);
            UIElementsHelper.SetVisibility(m_RowName.parent.Q<VisualElement>(ElementAndStyleNames.LegendUsedReserved), ShowUsed);

            SetValues(new[] { stats[0].totalBytes, stats[1].totalBytes} , new[] { stats[0].usedBytes, stats[1].usedBytes });
        }

        void SetUpBars()
        {
            if (m_BarsSetup) return;

            for (int i = 0; i < m_BackgroundElement.Length; i++)
            {
                var bar = this.parent.Q<VisualElement>($"{ElementAndStyleNames.MemoryUsageBar}-{Enum.GetName(typeof(StatsIdx), i)}").Q<VisualElement>(ElementAndStyleNames.KnownParts);
                bar.AddToClassList(ElementAndStyleNames.BarElement);

                m_BackgroundElement[i] = new VisualElement();
                m_BackgroundElement[i].AddToClassList(ElementAndStyleNames.BarBackground);
                m_BackgroundElement[i].style.marginLeft = m_BackgroundElement[i].style.marginRight = (StyleLength)1.5;
                bar.hierarchy.Add(m_BackgroundElement[i]);
                m_BackgroundElement[i].RegisterClickEvent(OnSelected);
                m_BackgroundElement[i].RegisterCallback<MouseEnterEvent>(OnMouseEnter);
                m_BackgroundElement[i].RegisterCallback<MouseLeaveEvent>(OnMouseLeave);

                m_ReservedElement[i] = new VisualElement();
                m_ReservedElement[i].AddToClassList(ElementAndStyleNames.BarReserved);
                m_BackgroundElement[i].Add(m_ReservedElement[i]);

                m_UsedElement[i] = new VisualElement();
                m_UsedElement[i].AddToClassList(ElementAndStyleNames.BarUsedPortion);
                m_ReservedElement[i].Add(m_UsedElement[i]);

                m_SelectedElement[i] = new VisualElement();
                m_SelectedElement[i].AddToClassList(ElementAndStyleNames.BarSelectedPortion);
                m_ReservedElement[i].Add(m_SelectedElement[i]);

                m_ReservedElement[i].AddToClassList(BackgroundColorClass);
                UIElementsHelper.SetVisibility(m_UsedElement[i], ShowUsed);

                m_UsedElement[i].AddToClassList(BackgroundColorClass);
            }

            RegisterCallback<GeometryChangedEvent>(OnGeometryChangedEvent);

            SetBarElements();

            m_BarsSetup = true;
        }

        void OnSelected()
        {
            if (!m_BreakdownParent.SelectableElements)
                return;

            if (m_RowName.parent.ClassListContains(ElementSelectedPseudoState))
            {
                Deselect();
                return;
            }
            m_RowName.parent.AddToClassList(ElementSelectedPseudoState);
            for (int i = 0; i < m_BackgroundElement.Length; i++)
            {
                m_BackgroundElement[i].AddToClassList(ElementSelectedPseudoState);
            }
            m_RowASize.AddToClassList(ElementSelectedPseudoState);
            m_RowBSize.AddToClassList(ElementSelectedPseudoState);
            m_DiffSize.AddToClassList(ElementSelectedPseudoState);
            Selected(Id);
        }

        public void Deselect(bool fireEvent = true)
        {
            if (!m_BreakdownParent.SelectableElements)
                return;
            for (int i = 0; i < m_BackgroundElement.Length; i++)
            {
                m_BackgroundElement[i].RemoveFromClassList(ElementSelectedPseudoState);
            }
            m_RowName.parent.RemoveFromClassList(ElementSelectedPseudoState);
            m_RowASize.RemoveFromClassList(ElementSelectedPseudoState);
            m_RowBSize.RemoveFromClassList(ElementSelectedPseudoState);
            m_DiffSize.RemoveFromClassList(ElementSelectedPseudoState);
            if (fireEvent)
                Deselected(Id);
        }

        void OnMouseEnter(MouseEnterEvent e)
        {
            for (int i = 0; i < m_BackgroundElement.Length; i++)
            {
                m_BackgroundElement[i].AddToClassList(ElementHoveredPseudoState);
            }
            m_RowName.parent.AddToClassList(ElementHoveredPseudoState);
            m_RowASize.AddToClassList(ElementHoveredPseudoState);
            m_RowBSize.AddToClassList(ElementHoveredPseudoState);
            m_DiffSize.AddToClassList(ElementHoveredPseudoState);
        }

        void OnMouseLeave(MouseLeaveEvent e)
        {
            for (int i = 0; i < m_BackgroundElement.Length; i++)
            {
                m_BackgroundElement[i].RemoveFromClassList(ElementHoveredPseudoState);
            }

            m_RowName.parent.RemoveFromClassList(ElementHoveredPseudoState);
            m_RowASize.RemoveFromClassList(ElementHoveredPseudoState);
            m_RowBSize.RemoveFromClassList(ElementHoveredPseudoState);
            m_DiffSize.RemoveFromClassList(ElementHoveredPseudoState);
        }

        static class ElementAndStyleNames
        {
            public static readonly string MemoryUsageBar = "memory-usage-breakdown__memory-usage-bar";
            public static readonly string KnownParts = "memory-usage-breakdown__memory-usage-bar__known-parts";
            public static readonly string BarElement = "memory-usage-bar__element";
            public static readonly string BarBackground = "memory-usage-breakdown__memory-usage-bar__background";
            public static readonly string BarReserved = "memory-usage-breakdown__memory-usage-bar__reserved";
            public static readonly string BarUsedPortion = "memory-usage-breakdown__memory-usage-bar__used-portion";
            public static readonly string BarSelectedPortion = "memory-usage-breakdown__memory-usage-bar__selected-portion";
            public static readonly string ColorBoxUnused = "memory-usage-breakdown__legend__color-box__unused";
            public static readonly string LegendUsedReserved = "memory-usage-breakdown__legend__used-reserved";
        }

        /// <summary>
        /// Instantiates a <see cref="MemoryUsageBreakdownElement"/> using the data read from a UXML file.
        /// </summary>
        public new class UxmlFactory : UxmlFactory<MemoryUsageBreakdownElement, UxmlTraits> {}

        /// <summary>
        /// Defines <see cref="UxmlTraits"/> for the <see cref="MemoryUsageBreakdownElement"/>.
        /// </summary>
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlStringAttributeDescription m_Text = new UxmlStringAttributeDescription { name = "text", defaultValue = "Other" };
            UxmlStringAttributeDescription m_ColorClass = new UxmlStringAttributeDescription { name = "background-color-class", defaultValue = "" };
            UxmlBoolAttributeDescription m_ShowUsed = new UxmlBoolAttributeDescription { name = "show-used", defaultValue = false };
            UxmlLongAttributeDescription m_Used = new UxmlLongAttributeDescription { name = "used-bytes", defaultValue = 50 };
            UxmlLongAttributeDescription m_Total = new UxmlLongAttributeDescription { name = "total-bytes", defaultValue = 100 };
            UxmlBoolAttributeDescription m_ShowSelected = new UxmlBoolAttributeDescription { name = "show-selected", defaultValue = false };
            UxmlLongAttributeDescription m_Selected = new UxmlLongAttributeDescription { name = "selected-bytes", defaultValue = 0};

            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
            {
                get { yield break; }
            }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var text = m_Text.GetValueFromBag(bag, cc);
                var showUsed = m_ShowUsed.GetValueFromBag(bag, cc);
                var total = m_Total.GetValueFromBag(bag, cc);
                var showSelected = m_ShowSelected.GetValueFromBag(bag, cc);
                var used = Mathf.Clamp(m_Used.GetValueFromBag(bag, cc), 0, total);
                var selected = Mathf.Clamp(m_Selected.GetValueFromBag(bag, cc), 0, total);
                var color = m_ColorClass.GetValueFromBag(bag, cc);

                ((MemoryUsageBreakdownElement)ve).Init(text, showUsed, new ulong[] {(ulong)used, (ulong)used},  new ulong[] {(ulong)total, (ulong)total}, showSelected, new ulong[] {(ulong)selected, (ulong)selected}, color);
            }
        }
    }
}

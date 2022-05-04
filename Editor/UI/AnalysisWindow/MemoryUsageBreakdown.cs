using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class MemoryUsageBreakdown : VisualElement
    {
        internal class SortControl
        {
            List<MemoryUsageBreakdownElement> m_SortableList;
            VisualElement[] controlGroups = new VisualElement[3];
            VisualElement[] ascending = new VisualElement[3];
            VisualElement[] descending = new VisualElement[3];
            Action<int, bool> OnSorted;
            bool reverse;

            static class ElementAndStyleNames
            {
                public const string AscendingEnabled = "legend__header__sort-ascending--enabled";
                public const string DecendingEnabled = "legend__header__sort-descending--enabled";
                public const string AscendingDisabled = "legend__header__sort-ascending--disabled";
                public const string DecendingDisabled = "legend__header__sort-descending--disabled";
                public const string Ascending = "memory-usage-breakdown__legend__header__sort-ascending";
                public const string Descending = "memory-usage-breakdown__legend__header__sort-descending";
            }

            public SortControl(VisualElement a, VisualElement b, VisualElement diff, Action<int, bool> onSorted)
            {
                OnSorted = onSorted;

                controlGroups[0] = a;
                controlGroups[1] = b;
                controlGroups[2] = diff;

                for (int i = 0; i < controlGroups.Length; i++)
                {
                    ascending[i] = controlGroups[i].Q<VisualElement>(ElementAndStyleNames.Ascending);
                    descending[i] = controlGroups[i].Q<VisualElement>(ElementAndStyleNames.Descending);
                    // TODO: Make visible again and re-add the sorting callback after redesign.
                    UIElementsHelper.SetVisibility(ascending[i], false);
                    UIElementsHelper.SetVisibility(descending[i], false);

                    //var idx = i;
                    //controlGroups[i].RegisterCallback<MouseDownEvent>((x) =>
                    //{
                    //    if (x.button == 0)
                    //    {
                    //        OnSorted.Invoke(idx, reverse);
                    //        reverse = !reverse;
                    //        SetElementState(idx, reverse);
                    //    }
                    //});
                }
            }

            public void ClearStates()
            {
                for (int i = 0; i < controlGroups.Length; i++)
                {
                    ascending[i].SwitchClasses(classToAdd: ElementAndStyleNames.AscendingDisabled, classToRemove: ElementAndStyleNames.AscendingEnabled);
                    descending[i].SwitchClasses(classToAdd: ElementAndStyleNames.DecendingDisabled, classToRemove: ElementAndStyleNames.DecendingEnabled);
                }
            }

            void SetElementState(int idx, bool reverse)
            {
                ClearStates();
                if (reverse)
                {
                    ascending[idx].SwitchClasses(classToAdd: ElementAndStyleNames.AscendingEnabled, classToRemove: ElementAndStyleNames.AscendingDisabled);
                }
                else
                {
                    descending[idx].SwitchClasses(classToAdd: ElementAndStyleNames.DecendingEnabled, classToRemove: ElementAndStyleNames.DecendingDisabled);
                }
            }
        }

        static class Content
        {
            public const string TotalLabelSeparator = " | ";
            public static readonly string UnknownText = L10n.Tr("Unknown");
            public static readonly string KnownTotalName = L10n.Tr("Total");
            public static readonly string TotalFormatString = L10n.Tr("Total: {0}");
            public static readonly string TotalLabelTooltip = L10n.Tr("The Total memory usage.");
            public static readonly string KnownTotalFormatString = L10n.Tr("{0}: {1}");
            public static readonly string KnownTotalLabelTooltip = L10n.Tr("The {0} memory usage.");
            public static readonly string TotalLabelTooltipWhenLessThanTotalTracked = L10n.Tr("The Total Tracked memory usage and the Total System Used memory usage differed. The OS reported less memory as 'In Use' than Unity's systems can account for. This could be down to a faulty memory query to the OS, or that the OS doesn't treat all memory Unity uses as 'In Use' by this app, e.g. due to shared DLLs.");
            public static readonly string MaxFormatString = L10n.Tr("Max: {0}");
            public static string GetTotalAndMaxLabelTooltip(bool denormalizeFrameBased) => denormalizeFrameBased ? TotalAndMaxLabelTooltipFrameBased : TotalAndMaxLabelTooltipSnapshotBased;
            public static readonly string TotalAndMaxLabelTooltipFrameBased = L10n.Tr("The bar is scaled in relation to the frame with the highest total memory usage (Max).");
            public static readonly string TotalAndMaxLabelTooltipSnapshotBased = L10n.Tr("The bar is scaled in relation to snapshot with the highest total memory usage (Max).");
            public static string GetTotalAndMaxLabelTooltipForMaxValue(bool denormalizeFrameBased) => denormalizeFrameBased ? TotalAndMaxLabelTooltipForMaxValueFrameBased : TotalAndMaxLabelTooltipForMaxValueSnapshotBased;
            public static readonly string TotalAndMaxLabelTooltipForMaxValueFrameBased = L10n.Tr("This is one of the frames with the highest total memory usage (Max).");
            public static readonly string TotalAndMaxLabelTooltipForMaxValueSnapshotBased = L10n.Tr("This is the snapshot with the highest total memory usage (Max).");
        }

        static class ElementAndStyleNames
        {
            public static readonly string HeaderA = "memory-usage-breakdown__header-a";
            public static readonly string HeaderB = "memory-usage-breakdown__header-b";
            public static readonly string HeaderTitle = "memory-usage-breakdown__header__title";
            public static readonly string HeaderSize = "memory-usage-breakdown__header__total-value";
            public static readonly string MemoryUsageBarA = "memory-usage-breakdown__memory-usage-bar-a";
            public static readonly string MemoryUsageBarB = "memory-usage-breakdown__memory-usage-bar-b";
            public static readonly string LegendTable = "memory-usage-breakdown__legend-table";
            public static readonly string MemoryUsageBarKnownParts = "memory-usage-breakdown__memory-usage-bar__known-parts";
            public static readonly string MemoryUsageBarUnknown = "memory-usage-breakdown__memory-usage-bar__unknown";
            public static readonly string LegendTableNameColumn = "memory-usage-breakdown__legend-table-name-column";
            public static readonly string LegendTableSnapshotAColumn = "memory-usage-breakdown__legend-table-snapshot-a-column";
            public static readonly string LegendTableSnapshotBColumn = "memory-usage-breakdown__legend-table-snapshot-b-column";
            public static readonly string LegendTableDiffColumn = "memory-usage-breakdown__legend-table-diff-column";
            public static readonly string LegendColorBox = "memory-usage-breakdown__legend__color-box";
            public static readonly string LegendName = "memory-usage-breakdown__legend__name";
            public static readonly string LegendSizeColumn = "memory-usage-breakdown__legend__size-column";
            public static readonly string MemorySummaryCategoryUnknown = "background-color__memory-summary-category__unknown";
            public static readonly string MemoryUsageBreakdownLegendNameAndColor = "memory-usage-breakdown__legend__name-and-color";
            public static readonly string BreakDownBars = "memory-usage-breakdown__Bars";
            public const string ColumnAControls = "memory-usage-breakdown__legend-table-column-a-controls";
            public const string ColumnBControls = "memory-usage-breakdown__legend-table-column-b-controls";
            public const string DiffColumnControls = "memory-usage-breakdown__legend-table-diff-column-controls";
            public static readonly string ColorBoxUnused = "memory-usage-breakdown__legend__color-box__unused";
            public static readonly string LegendUsedReserved = "memory-usage-breakdown__legend__used-reserved";
            public static readonly string HeaderIcon = "memory-usage-breakdown__legend__head-icon";
            public const string EmptySpaceHolder = "emptyspaceholder";
            public const string EmptySpaceHolderDiffModeClass = "legend__table-empty-space-holder";
            public const string EmptySpaceHolderNonDiffModeClass = "legend__table-empty-space-holder--non-diff";
            public const string ColumnAHeaderNonDiffModeClass = "legend__table-column-controls--non-diff";
            public const string ColumnAHeaderDiffModeClass = "legend__table-column-controls";
        }

        public string HeaderText { get; private set; }

        public bool DenormalizeFrameBased { get; set; }

        ulong[] m_TotalBytes = new ulong[2];
        ulong[] m_TotalTrackedBytes = new ulong[2];

        public ulong[] TotalBytes
        {
            get => m_TotalBytes;
            private set => m_TotalBytes = value;
        }

        public long GetTotalBytes(MemoryUsageBreakdownElement.StatsIdx idx)
        {
            return GetTotalBytes((int)idx);
        }

        public long GetTotalBytes(int idx)
        {
            return (long)m_TotalBytes[idx];
        }

        bool m_Normalized;
        ulong[] m_MaxTotalBytesToNormalizeTo = new ulong[2];

        public bool ShowUnknown { get; private set; }
        public bool[] TotalIsKnown { get; private set; }

        public string UnknownName { get; private set; } = Content.UnknownText;
        public string KnownTotalName { get; private set; } = Content.KnownTotalName;
        public int Id { get; private set; } = -1;

        public event Action<int, int> ElementSelected = delegate {};
        public event Action<int, int> ElementDeselected = delegate {};

        VisualTreeAsset m_NameAndColor;
        VisualTreeAsset m_Size;

        List<MemoryUsageBreakdownElement> m_Elements = new List<MemoryUsageBreakdownElement>();

        VisualElement m_Root;
        VisualElement m_Content;
        VisualElement m_MemoryUsageBar;
        VisualElement m_MemoryUsageTable;
        Label m_HeaderName;
        Label[] m_HeaderSize = new Label[2];

        VisualElement[] m_UnknownBar = new VisualElement[2];
        SortControl m_SortControls;
        VisualElement m_UnknownRoot;
        Label[] m_UnknownRowColumnSize = new Label[2];
        Label m_UnknownRowDiffColumnSize;
        VisualElement m_EmptySpaceHolderCategoryNameHeader;
        VisualElement m_EmptySpaceColumnAHeader;

        ulong[] m_UnknownSize = new ulong[2];
        private bool[] m_UnknownUnknown = new bool[2];

        public override VisualElement contentContainer
        {
            get { return m_Content; }
        }

        public bool Normalized
        {
            get
            {
                return m_Normalized;
            }
            internal set
            {
                if (m_Normalized == value)
                    return;
                m_Normalized = value;
                SetTotalUsed(m_TotalBytes, m_TotalTrackedBytes, m_Normalized, m_MaxTotalBytesToNormalizeTo, TotalIsKnown, m_UnknownUnknown, setBars: true);
            }
        }

        public enum CompareMode
        {
            Single,
            Diff
        }

        public CompareMode Mode { get; private set;}

        public bool SelectableElements = false;

        VisualElement[] m_KnownParts = new VisualElement[2];

        public MemoryUsageBreakdown(string headerText, bool showUnknown = false, bool singleMode = true)
            : this()
        {
            ShowUnknown = showUnknown;
            HeaderText = headerText;
            Init(headerText, m_TotalBytes, showUnknown, UnknownName);

            Mode = singleMode ? CompareMode.Single : CompareMode.Diff;
            SetBAndDiffVisibility(!singleMode);
        }

        public MemoryUsageBreakdown()
            : base()
        {
            VisualTreeAsset memoryUsageBreakdownViewTree;
            memoryUsageBreakdownViewTree = EditorGUIUtility.Load(ResourcePaths.MemoryUsageBreakdownUxmlPath) as VisualTreeAsset;

            m_NameAndColor = EditorGUIUtility.Load(ResourcePaths.MemoryUsageBreakdownLegendNameAndColorUxmlPath) as VisualTreeAsset;
            m_Size = EditorGUIUtility.Load(ResourcePaths.MemoryUsageBreakdownLegendSizeUxmlPath) as VisualTreeAsset;

            m_Root = memoryUsageBreakdownViewTree.Clone();

            style.flexShrink = 0;
            m_Root.style.flexGrow = 1;

            hierarchy.Add(m_Root);
            m_Root.parent.style.flexDirection = FlexDirection.Row;

            var header = m_Root.Q<VisualElement>(ElementAndStyleNames.HeaderA);
            m_HeaderName = header.Q<Label>(ElementAndStyleNames.HeaderTitle);
            m_HeaderSize[0] = header.Q<Label>(ElementAndStyleNames.HeaderSize);

            m_EmptySpaceHolderCategoryNameHeader = m_Root.Q(ElementAndStyleNames.EmptySpaceHolder);
            m_EmptySpaceColumnAHeader = m_Root.Q(ElementAndStyleNames.ColumnAControls);

            header = m_Root.Q<VisualElement>(ElementAndStyleNames.HeaderB);
            m_HeaderSize[1] = header.Q<Label>(ElementAndStyleNames.HeaderSize);

            m_MemoryUsageBar = m_Root.Q(ElementAndStyleNames.BreakDownBars);

            m_MemoryUsageTable = m_Root.Q(ElementAndStyleNames.LegendTable);
            m_Content = m_MemoryUsageBar;

            m_UnknownBar[0] = m_MemoryUsageBar.Q(ElementAndStyleNames.MemoryUsageBarA).Q(ElementAndStyleNames.MemoryUsageBarUnknown);
            m_UnknownBar[0].RegisterClickEvent(OnSelectedUnkown);
            m_UnknownBar[0].RegisterCallback<MouseEnterEvent>(OnMouseEnterUnkown);
            m_UnknownBar[0].RegisterCallback<MouseLeaveEvent>(OnMouseLeaveUnkown);
            m_UnknownBar[1] = m_MemoryUsageBar.Q(ElementAndStyleNames.MemoryUsageBarB).Q(ElementAndStyleNames.MemoryUsageBarUnknown);
            m_UnknownBar[1].RegisterClickEvent(OnSelectedUnkown);
            m_UnknownBar[1].RegisterCallback<MouseEnterEvent>(OnMouseEnterUnkown);
            m_UnknownBar[1].RegisterCallback<MouseLeaveEvent>(OnMouseLeaveUnkown);
            m_SortControls = new SortControl(m_EmptySpaceColumnAHeader, m_Root.Q(ElementAndStyleNames.ColumnBControls), m_Root.Q(ElementAndStyleNames.DiffColumnControls), SortElements);
            Setup();

            m_KnownParts[0] = m_Content.Q<VisualElement>(ElementAndStyleNames.MemoryUsageBarA).Q<VisualElement>(ElementAndStyleNames.MemoryUsageBarKnownParts);
            m_KnownParts[1] = m_Content.Q<VisualElement>(ElementAndStyleNames.MemoryUsageBarB).Q<VisualElement>(ElementAndStyleNames.MemoryUsageBarKnownParts);
        }

        public void SetValues(ulong[] totalBytes, List<ulong[]> reserved, List<ulong[]> used, bool normalized, ulong[] maxTotalBytesToNormalizeTo, bool[] totalIsKnown = null, bool setBars = true, string nameOfKnownTotal = null)
        {
            GatherElements();
            SetupElements();
            m_SortControls.ClearStates();
            m_TotalBytes = totalBytes;

            if (!string.IsNullOrEmpty(nameOfKnownTotal))
                KnownTotalName = nameOfKnownTotal;

            ulong[] totalTracked = new ulong[totalBytes.Length];

            bool[] unknownUnknown = new bool[totalBytes.Length];
            if (totalIsKnown == null)
            {
                totalIsKnown = new bool[totalBytes.Length];
                for (int i = 0; i < totalIsKnown.Length; i++)
                {
                    totalIsKnown[i] = true;
                }
            }
            for (int i = 0; i < unknownUnknown.Length; i++)
            {
                unknownUnknown[i] = !totalIsKnown[i];
            }
            for (int i = 0; i < m_Elements.Count && i < reserved.Count; i++)
            {
                for (int ii = 0; ii < totalTracked.Length; ii++)
                {
                    totalTracked[ii] += reserved[i][ii];
                    if (totalIsKnown[ii] && totalTracked[ii] > totalBytes[ii])
                    {
                        unknownUnknown[ii] = true;
                    }
                    if (used == null || i >= used.Count)
                        m_Elements[i].SetValues(reserved[i], new ulong[] { 0, 0, 0 });
                    else
                        m_Elements[i].SetValues(reserved[i], used[i]);
                }
            }
            SetTotalUsed(totalBytes, totalTracked, normalized, maxTotalBytesToNormalizeTo, totalIsKnown, unknownUnknown, setBars);
        }

        public void SetCategoryName(string name, int elementIndex)
        {
            if (m_Elements != null && elementIndex < m_Elements.Count)
            {
                m_Elements[elementIndex].UpdateText(name);
            }
        }

        public void SetBAndDiffVisibility(bool visibility)
        {
            m_EmptySpaceHolderCategoryNameHeader.SwitchClasses(
                classA: ElementAndStyleNames.EmptySpaceHolderDiffModeClass,
                classB: ElementAndStyleNames.EmptySpaceHolderNonDiffModeClass,
                addARemoveB: visibility);
            m_EmptySpaceColumnAHeader.SwitchClasses(
                classA: ElementAndStyleNames.ColumnAHeaderDiffModeClass,
                classB: ElementAndStyleNames.ColumnAHeaderNonDiffModeClass,
                addARemoveB: visibility);
            UIElementsHelper.SetVisibility(m_Root.Q(ElementAndStyleNames.HeaderIcon), visibility);
            UIElementsHelper.SetVisibility(m_MemoryUsageTable.Q(ElementAndStyleNames.LegendTableSnapshotBColumn), visibility);
            UIElementsHelper.SetVisibility(m_MemoryUsageTable.Q(ElementAndStyleNames.LegendTableDiffColumn), visibility);
            UIElementsHelper.SetVisibility(this.Q<VisualElement>(ElementAndStyleNames.HeaderB), visibility);
            UIElementsHelper.SetVisibility(this.Q<VisualElement>(ElementAndStyleNames.MemoryUsageBarB), visibility);
            if (visibility)
                Mode = CompareMode.Diff;
            else
                Mode = CompareMode.Single;
        }

        void SetTotalUsed(ulong[] totalBytes, ulong[] totalTracked, bool normalized, ulong[] maxTotalBytesToNormalizeTo, bool[] totalIsKnown, bool[] unknownUnknown, bool setBars = true)
        {
            m_TotalBytes = totalBytes;
            TotalIsKnown = totalIsKnown;
            m_TotalTrackedBytes = totalTracked;

            for (int i = 0; i < totalBytes.Length; i++)
            {
                m_Normalized = normalized;
                m_MaxTotalBytesToNormalizeTo[i] = Math.Max(m_TotalBytes[i], maxTotalBytesToNormalizeTo[i]);

                UpdateTotalSizeText(i);

                m_UnknownSize[i] = m_TotalBytes[i];

                for (int ii = 0; ii < m_Elements.Count; ii++)
                {
                    var percentage = m_TotalTrackedBytes[i] > 0 ? (m_Elements[ii].stats[i].totalBytes / (float)m_TotalTrackedBytes[i]) * 100 : 100;
                    var ve = m_Elements[ii].parent.Q<VisualElement>($"memory-usage-breakdown__memory-usage-bar-{Enum.GetName(typeof(MemoryUsageBreakdownElement.StatsIdx), i)}").Q<VisualElement>(ElementAndStyleNames.MemoryUsageBarKnownParts).Children().ToArray();
                    if (!m_Elements[ii].barValuesSet[i] && setBars && totalBytes[i] != 0)
                    {
                        ve[ii].style.SetBarWidthInPercent(percentage);
                        m_Elements[ii].barValuesSet[i] = true;
                    }

                    var elementSize = (ulong)m_Elements[ii].stats[i].totalBytes;
                    if (elementSize > m_UnknownSize[i])
                    {
                        m_UnknownSize[i] = 0ul;
                        unknownUnknown[i] = true;
                    }
                    else
                        m_UnknownSize[i] -= Math.Min(elementSize, m_UnknownSize[i]);
                }

                if (m_TotalTrackedBytes[i] > m_TotalBytes[i] && KnownTotalName == Content.KnownTotalName)
                {
                    // showing a separate Total isn't desired, so take the bigger of the two
                    m_TotalBytes[i] = m_TotalTrackedBytes[i];
                }

                var totalBarByteAmount = Math.Max(m_TotalTrackedBytes[i], m_TotalBytes[i]);

                if (!ShowUnknown && m_Normalized)
                    totalBarByteAmount = m_TotalBytes[i];

                if (!m_Normalized && m_MaxTotalBytesToNormalizeTo[i] > totalBarByteAmount)
                    totalBarByteAmount = m_MaxTotalBytesToNormalizeTo[i];

                if (ShowUnknown || !m_Normalized)
                {
                    var percentage = totalBarByteAmount > 0 ? m_TotalTrackedBytes[i] / (float)totalBarByteAmount * 100 : 100;
                    m_KnownParts[i].style.width = new Length(Mathf.RoundToInt(percentage), LengthUnit.Percent);
                }
                else
                {
                    m_KnownParts[i].style.width = new Length(100, LengthUnit.Percent);
                }

                if (m_UnknownBar[i].visible != ShowUnknown)
                    UIElementsHelper.SetVisibility(m_UnknownBar[i], ShowUnknown);
                if (ShowUnknown)
                {
                    var percentage = m_UnknownSize[i] / (float)totalBarByteAmount * 100;
                    m_UnknownBar[i].style.SetBarWidthInPercent(Mathf.RoundToInt(percentage));

                    m_UnknownBar[i].tooltip = MemoryUsageBreakdownElement.BuildTooltipText(HeaderText, UnknownName, (ulong)GetTotalBytes(i), m_UnknownSize[i]);
                    m_UnknownRoot.tooltip = m_UnknownBar[i].tooltip;

                    m_UnknownRowColumnSize[i].text = unknownUnknown[i] ? Content.UnknownText : MemoryUsageBreakdownElement.BuildRowSizeText(m_UnknownSize[i], m_UnknownSize[i], false);

                    var unknownDiff = m_UnknownSize[0] > m_UnknownSize[1] ? m_UnknownSize[0] - m_UnknownSize[1] : m_UnknownSize[1] - m_UnknownSize[0];
                    m_UnknownRowDiffColumnSize.text = unknownUnknown[i] ? Content.UnknownText : MemoryUsageBreakdownElement.BuildRowSizeText(unknownDiff, unknownDiff, false);

                    UIElementsHelper.SetVisibility(m_UnknownRoot.Q<VisualElement>(ElementAndStyleNames.ColorBoxUnused), false);
                    UIElementsHelper.SetVisibility(m_UnknownRoot.Q<VisualElement>(ElementAndStyleNames.LegendUsedReserved), false);
                }

                if (m_UnknownRoot != null && m_UnknownRoot.visible != ShowUnknown)
                    UIElementsHelper.SetVisibility(m_UnknownRoot, ShowUnknown);
            }
            if (m_UnknownUnknown.Length != unknownUnknown.Length)
                m_UnknownUnknown = new bool[unknownUnknown.Length];
            unknownUnknown.CopyTo(m_UnknownUnknown, 0);
        }

        void UpdateTotalSizeText(int i)
        {
            var totalBarByteAmount = Math.Max(m_TotalTrackedBytes[i], m_TotalBytes[i]);
            if (!m_Normalized && totalBarByteAmount < m_MaxTotalBytesToNormalizeTo[i])
            {
                var text = "";
                string tooltip;
                if (m_TotalTrackedBytes[i] > m_TotalBytes[i])
                {
                    text += string.Format(Content.KnownTotalFormatString, KnownTotalName, EditorUtility.FormatBytes((long)m_TotalBytes[i]));
                    text += Content.TotalLabelSeparator;
                    text += string.Format(Content.TotalFormatString, EditorUtility.FormatBytes((long)m_TotalTrackedBytes[i] + (long)m_UnknownSize[i]));
                    text += Content.TotalLabelSeparator;
                    tooltip = Content.TotalLabelTooltipWhenLessThanTotalTracked + "\n" + Content.GetTotalAndMaxLabelTooltip(DenormalizeFrameBased);
                }
                else
                {
                    text += string.Format(Content.TotalFormatString, EditorUtility.FormatBytes((long)m_TotalTrackedBytes[i] + (long)m_UnknownSize[i]));
                    text += Content.TotalLabelSeparator;
                    tooltip = Content.GetTotalAndMaxLabelTooltip(DenormalizeFrameBased);
                }
                text += string.Format(Content.MaxFormatString, EditorUtility.FormatBytes((long)m_MaxTotalBytesToNormalizeTo[i]));
                m_HeaderSize[i].text = text;
                m_HeaderSize[i].tooltip = tooltip;
            }
            else
            {
                if (m_TotalTrackedBytes[i] > m_TotalBytes[i])
                {
                    m_HeaderSize[i].text = string.Format(Content.KnownTotalFormatString, KnownTotalName, EditorUtility.FormatBytes((long)m_TotalBytes[i]))
                        + Content.TotalLabelSeparator
                        + string.Format(Content.TotalFormatString, EditorUtility.FormatBytes((long)m_TotalTrackedBytes[i] + (long)m_UnknownSize[i]));
                    m_HeaderSize[i].tooltip = Content.TotalLabelTooltipWhenLessThanTotalTracked;

                    if (!m_Normalized)
                    {
                        if (totalBarByteAmount < m_MaxTotalBytesToNormalizeTo[i])
                            m_HeaderSize[i].tooltip += "\n" + Content.GetTotalAndMaxLabelTooltip(DenormalizeFrameBased);
                        else
                            m_HeaderSize[i].tooltip += "\n" + Content.GetTotalAndMaxLabelTooltipForMaxValue(DenormalizeFrameBased);
                    }
                }
                else
                {
                    m_HeaderSize[i].text = string.Format(Content.TotalFormatString, EditorUtility.FormatBytes((long)m_TotalBytes[i]));
                    m_HeaderSize[i].tooltip = m_Normalized ? Content.TotalLabelTooltip : Content.GetTotalAndMaxLabelTooltipForMaxValue(DenormalizeFrameBased);
                }
            }
        }

        void Init(string headerText, ulong[] totalMemory, bool showUnknown, string unknownName)
        {
            ShowUnknown = showUnknown;
            m_TotalBytes = totalMemory;
            UnknownName = unknownName;
            if (m_HeaderName != null)
            {
                m_HeaderName.text = HeaderText = headerText;
                for (int i = 0; i < m_HeaderSize.Length; i++)
                    UpdateTotalSizeText(i);
            }

            Setup();
        }

        public void Setup(bool selectableElements = false, int id = -1)
        {
            SelectableElements = selectableElements;
            Id = id;

            GatherElements();

            if (m_Elements.Count > 0)
            {
                UnregisterCallback<GeometryChangedEvent>(OnPostDisplaySetup);
                SetupElements();
            }
            else
                RegisterCallback<GeometryChangedEvent>(OnPostDisplaySetup);
        }

        void GatherElements()
        {
            m_Elements.Clear();
            if (m_Content == null)
                return;
            for (int i = 0; i < m_Content.childCount; i++)
            {
                if (m_Content[i] is MemoryUsageBreakdownElement)
                {
                    m_Elements.Add(m_Content[i] as MemoryUsageBreakdownElement);
                }
            }
        }

        void OnPostDisplaySetup(GeometryChangedEvent evt)
        {
            GatherElements();
            if (m_Elements.Count > 0)
            {
                UnregisterCallback<GeometryChangedEvent>(OnPostDisplaySetup);
                SetupElements();
            }
        }

        VisualElement AddToTree(string ColumnnToAddTo, VisualTreeAsset treeToClone, bool handleAsLastRow)
        {
            var tree = treeToClone.CloneTree();
            if (handleAsLastRow)
            {
                tree[0].style.borderBottomWidth = 0;
            }

            m_MemoryUsageTable.Q(ColumnnToAddTo).hierarchy.Insert(m_MemoryUsageTable.Q(ColumnnToAddTo).hierarchy.childCount - 1, tree);
            return tree;
        }

        Label AddToTreeAsUnknownRow(string ColumnnToAddTo, VisualTreeAsset treeToClone)
        {
            var tree = AddToTree(ColumnnToAddTo, treeToClone, true);
            var label = tree.Q<Label>(ElementAndStyleNames.LegendSizeColumn);
            label.text = "?? B";
            return label;
        }

        void SortElements(int column, bool reverse)
        {
            m_Elements.Sort((a, b) => a.stats[column].totalBytes.CompareTo(b.stats[column].totalBytes));
            if (reverse)
                m_Elements.Reverse();
            SetupElements();
        }

        void SetupElements()
        {
            ClearColumns();
            bool idsAreAlreadyInitalized = false;
            if (m_Elements != null && m_Elements.Count > 1)
            {
                foreach (var item in m_Elements)
                {
                    if (item.Id != 0)
                        idsAreAlreadyInitalized = true;
                }
            }
            for (int i = 0; i < m_Elements.Count; i++)
            {
                var element = m_Elements[i];
                var handleLastRow = m_Elements.Last() == element && !ShowUnknown;
                var nameAndColorTree = AddToTree(ElementAndStyleNames.LegendTableNameColumn, m_NameAndColor, handleLastRow);
                var aColumnTree = AddToTree(ElementAndStyleNames.LegendTableSnapshotAColumn, m_Size, handleLastRow);
                var bColumnTree = AddToTree(ElementAndStyleNames.LegendTableSnapshotBColumn, m_Size, handleLastRow);
                var diffColumnTree = AddToTree(ElementAndStyleNames.LegendTableDiffColumn, m_Size, handleLastRow);

                element.Setup(this, idsAreAlreadyInitalized ? element.Id : i,
                    nameAndColorTree.Q(ElementAndStyleNames.LegendColorBox),
                    nameAndColorTree.Q<Label>(ElementAndStyleNames.LegendName),
                    aColumnTree.Q<Label>(ElementAndStyleNames.LegendSizeColumn),
                    bColumnTree.Q<Label>(ElementAndStyleNames.LegendSizeColumn),
                    diffColumnTree.Q<Label>(ElementAndStyleNames.LegendSizeColumn));

                element.Selected -= OnSelectedElement;
                element.Selected += OnSelectedElement;
                element.Deselected -= OnDeselectedElement;
                element.Deselected += OnDeselectedElement;
            }

            if (ShowUnknown)
            {
                var nameAndColorTree = AddToTree(ElementAndStyleNames.LegendTableNameColumn, m_NameAndColor, true);
                nameAndColorTree.Q(ElementAndStyleNames.LegendColorBox).AddToClassList(ElementAndStyleNames.MemorySummaryCategoryUnknown);
                nameAndColorTree.Q<Label>(ElementAndStyleNames.LegendName).text = UnknownName;

                m_UnknownRowColumnSize[0] = AddToTreeAsUnknownRow(ElementAndStyleNames.LegendTableSnapshotAColumn, m_Size);
                m_UnknownRowColumnSize[0].RegisterClickEvent(OnSelectedUnkown);
                m_UnknownRowColumnSize[0].RegisterCallback<MouseEnterEvent>(OnMouseEnterUnkown);
                m_UnknownRowColumnSize[0].RegisterCallback<MouseLeaveEvent>(OnMouseLeaveUnkown);
                m_UnknownRowColumnSize[1] = AddToTreeAsUnknownRow(ElementAndStyleNames.LegendTableSnapshotBColumn, m_Size);
                m_UnknownRowColumnSize[1].RegisterClickEvent(OnSelectedUnkown);
                m_UnknownRowColumnSize[1].RegisterCallback<MouseEnterEvent>(OnMouseEnterUnkown);
                m_UnknownRowColumnSize[1].RegisterCallback<MouseLeaveEvent>(OnMouseLeaveUnkown);
                m_UnknownRowDiffColumnSize = AddToTreeAsUnknownRow(ElementAndStyleNames.LegendTableDiffColumn, m_Size);
                m_UnknownRowDiffColumnSize.RegisterClickEvent(OnSelectedUnkown);
                m_UnknownRowDiffColumnSize.RegisterCallback<MouseEnterEvent>(OnMouseEnterUnkown);
                m_UnknownRowDiffColumnSize.RegisterCallback<MouseLeaveEvent>(OnMouseLeaveUnkown);

                m_UnknownRoot = nameAndColorTree.Q("memory-usage-breakdown__legend__name-and-color");
                m_UnknownRoot.RegisterClickEvent(OnSelectedUnkown);
                m_UnknownRoot.RegisterCallback<MouseEnterEvent>(OnMouseEnterUnkown);
                m_UnknownRoot.RegisterCallback<MouseLeaveEvent>(OnMouseLeaveUnkown);
            }

            ulong[] totalTracked = new ulong[m_TotalBytes.Length];
            m_TotalBytes.CopyTo(totalTracked, 0);

            bool[] unknownUnknown = new bool[m_TotalBytes.Length];

            SetTotalUsed(m_TotalBytes, totalTracked, m_Normalized, m_MaxTotalBytesToNormalizeTo, TotalIsKnown, unknownUnknown);
        }

        public void SelectRange(int index, ulong bytesA, ulong bytesB)
        {
            if (index > 0 && index < m_Elements.Count)
                m_Elements[index].SetSelected(bytesA, bytesB);
        }

        public void ClearSelectedRanges()
        {
            foreach (var element in m_Elements)
            {
                element.ClearSelected();
            }
        }

        void OnSelectedElement(int id)
        {
            foreach (var element in m_Elements)
            {
                if (element.Id != id)
                    element.Deselect(false);
            }
            if (m_UnknownRoot != null && id != -1)
                DeselectUnknown(false);
            ElementSelected(Id, id);
        }

        void OnDeselectedElement(int id)
        {
            ElementDeselected(Id, id);
        }

        public void ClearSelection()
        {
            foreach (var element in m_Elements)
            {
                element.Deselect(false);
            }
            if (m_UnknownRoot != null)
                DeselectUnknown(false);
        }

        void OnSelectedUnkown()
        {
            if (!SelectableElements)
                return;
            if (m_UnknownRoot.ClassListContains(MemoryUsageBreakdownElement.ElementSelectedPseudoState))
            {
                DeselectUnknown();
                return;
            }
            m_UnknownRoot.AddToClassList(MemoryUsageBreakdownElement.ElementSelectedPseudoState);
            for (int i = 0; i < m_UnknownBar.Length; i++)
            {
                m_UnknownBar[i].AddToClassList(MemoryUsageBreakdownElement.ElementSelectedPseudoState);
            }
            for (int i = 0; i < m_UnknownRowColumnSize.Length; i++)
            {
                m_UnknownRowColumnSize[i].AddToClassList(MemoryUsageBreakdownElement.ElementSelectedPseudoState);
            }
            m_UnknownRowDiffColumnSize.AddToClassList(MemoryUsageBreakdownElement.ElementSelectedPseudoState);
            OnSelectedElement(-1);
        }

        void DeselectUnknown(bool fireEvent = true)
        {
            m_UnknownRoot.RemoveFromClassList(MemoryUsageBreakdownElement.ElementSelectedPseudoState);
            for (int i = 0; i < m_UnknownBar.Length; i++)
            {
                m_UnknownBar[i].RemoveFromClassList(MemoryUsageBreakdownElement.ElementSelectedPseudoState);
            }
            for (int i = 0; i < m_UnknownRowColumnSize.Length; i++)
            {
                m_UnknownRowColumnSize[i].RemoveFromClassList(MemoryUsageBreakdownElement.ElementSelectedPseudoState);
            }
            m_UnknownRowDiffColumnSize.RemoveFromClassList(MemoryUsageBreakdownElement.ElementSelectedPseudoState);
            if (fireEvent)
                OnDeselectedElement(-1);
        }

        void OnMouseEnterUnkown(MouseEnterEvent e)
        {
            for (int i = 0; i < m_UnknownBar.Length; i++)
            {
                m_UnknownBar[i].AddToClassList(MemoryUsageBreakdownElement.ElementHoveredPseudoState);
            }
            for (int i = 0; i < m_UnknownRowColumnSize.Length; i++)
            {
                m_UnknownRowColumnSize[i].AddToClassList(MemoryUsageBreakdownElement.ElementHoveredPseudoState);
            }
            m_UnknownRoot.AddToClassList(MemoryUsageBreakdownElement.ElementHoveredPseudoState);
            m_UnknownRowDiffColumnSize.AddToClassList(MemoryUsageBreakdownElement.ElementHoveredPseudoState);
        }

        void OnMouseLeaveUnkown(MouseLeaveEvent e)
        {
            for (int i = 0; i < m_UnknownBar.Length; i++)
            {
                m_UnknownBar[i].RemoveFromClassList(MemoryUsageBreakdownElement.ElementHoveredPseudoState);
            }
            for (int i = 0; i < m_UnknownRowColumnSize.Length; i++)
            {
                m_UnknownRowColumnSize[i].RemoveFromClassList(MemoryUsageBreakdownElement.ElementHoveredPseudoState);
            }
            m_UnknownRoot.RemoveFromClassList(MemoryUsageBreakdownElement.ElementHoveredPseudoState);
            m_UnknownRowDiffColumnSize.RemoveFromClassList(MemoryUsageBreakdownElement.ElementHoveredPseudoState);
        }

        void ClearColumns()
        {
            ClearColumn(m_MemoryUsageTable.Q(ElementAndStyleNames.LegendTableNameColumn), ElementAndStyleNames.MemoryUsageBreakdownLegendNameAndColor, m_NameAndColor);
            ClearColumn(m_MemoryUsageTable.Q(ElementAndStyleNames.LegendTableSnapshotAColumn), ElementAndStyleNames.LegendSizeColumn, m_Size);
            ClearColumn(m_MemoryUsageTable.Q(ElementAndStyleNames.LegendTableSnapshotBColumn), ElementAndStyleNames.LegendSizeColumn, m_Size);
            ClearColumn(m_MemoryUsageTable.Q(ElementAndStyleNames.LegendTableDiffColumn), ElementAndStyleNames.LegendSizeColumn, m_Size);
        }

        void ClearColumn(VisualElement ve, string id, VisualTreeAsset vte)
        {
            List<VisualElement> removals = new List<VisualElement>();
            int i = 0;
            foreach (var child in ve.Children())
            {
                if (child.name.Contains(id) || (child is TemplateContainer tc && (tc.templateId.Contains(id) || tc.TemplateSourceEquals(vte))))
                {
                    removals.Add(child);
                }

                i++;
            }

            foreach (var removal in removals)
            {
                ve.Remove(removal);
            }
        }

        /// <summary>
        /// Instantiates a <see cref="MemoryUsageBreakdown"/> using the data read from a UXML file.
        /// </summary>
        public new class UxmlFactory : UxmlFactory<MemoryUsageBreakdown, UxmlTraits> {}

        /// <summary>
        /// Defines <see cref="UxmlTraits"/> for the <see cref="MemoryUsageBreakdown"/>.
        /// </summary>
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlStringAttributeDescription m_HeaderText = new UxmlStringAttributeDescription { name = "header-text", defaultValue = "Memory Usage" };
            UxmlIntAttributeDescription m_TotalMemory = new UxmlIntAttributeDescription { name = "total-bytes", defaultValue = (int)(1024 * 1024 * 1024 * 1.2f) };
            UxmlBoolAttributeDescription m_ShowUnknown = new UxmlBoolAttributeDescription { name = "show-unknown", defaultValue = false };
            UxmlStringAttributeDescription m_UnknownName = new UxmlStringAttributeDescription { name = "unknown-name", defaultValue = "Unknown" };

            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
            {
                get
                {
                    // Can only contain MemoryUsageBreakdownElements
                    yield return new UxmlChildElementDescription(typeof(MemoryUsageBreakdownElement));
                }
            }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var totalMemory = m_TotalMemory.GetValueFromBag(bag, cc);
                var headerText = m_HeaderText.GetValueFromBag(bag, cc);
                var showUnknown = m_ShowUnknown.GetValueFromBag(bag, cc);
                var unknownName = m_UnknownName.GetValueFromBag(bag, cc);

                ((MemoryUsageBreakdown)ve).Init(headerText, new ulong[] { (ulong)totalMemory, (ulong)totalMemory }, showUnknown, unknownName);
            }
        }
    }
}

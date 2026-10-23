using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class MemorySummaryBarViewController : ViewController
    {
        const string k_UxmlAssetGuid = "f1260fe1fcaaea242b006822b2aff5a2";

        // Breakdown bar parts
        const string k_UxmlMemoryUsageBarContainerA = "memory-summary__bar__container-a";
        const string k_UxmlMemoryUsageBarContainerB = "memory-summary__bar__container-b";
        const string k_UxmlMemoryUsageBarHeaderTitle = "memory-summary__bar__total-value";
        const string k_UxmlMemoryUsageBarBreakdownBar = "memory-summary__bar";
        const string k_UxmlMemoryUsageBarSnapshotLabel = "memory-summary__bar__tag";
        const string k_UxmlMemoryUsageBarCell = "memory-summary__bar__element";
        const string k_UxmlMemoryUsageBarCellRemainder = "memory-summary__category__color-remainder";
        // Element states
        const string k_UxmlLegendTableCellHoverStateClass = "memory-summary__element-hovered";
        const string k_UxmlLegendTableCellSelectedStateClass = "memory-summary__element-selected";

        // Model
        MemorySummaryModel m_Model;
        string m_TotalLabelFormat;

        // View
        VisualElement m_ContainerA;
        VisualElement m_BarA;
        VisualElement m_SnapshotLabelA;
        Label m_TotalA;
        MemoryBarElement[] m_CellsA;

        VisualElement m_ContainerB;
        VisualElement m_BarB;
        VisualElement m_SnapshotLabelB;
        Label m_TotalB;
        MemoryBarElement[] m_CellsB;

        // State
        bool m_NormalizeBars;
        bool m_ShowResidentMemory;
        bool m_ForceShowResidentBars;
        bool m_ShowSnapshotLabels;
        bool m_UseResidentAsSourceInTooltips;
        bool m_ShowResidentSizeOnly;
        bool m_DeEmphasizeNonResidentPortion;
        string m_TooltipHint;
        int m_SelectedCellIndex = -1;

        readonly MemorySummaryDataOverrides m_DataOverrides;

        public event Action<int, bool> OnRowHovered;
        public event Action<int> OnRowClicked;
        public event Action<int> OnRowDoubleClicked;

        public MemorySummaryBarViewController(MemorySummaryModel model, in MemorySummaryDataOverrides dataOverrides = default)
        {
            m_Model = model;

            m_NormalizeBars = false;
            m_ShowResidentMemory = false;
            m_ForceShowResidentBars = false;
            m_ShowSnapshotLabels = false;

            m_DataOverrides = dataOverrides;
        }

        public string TotalLabelFormat
        {
            get => m_TotalLabelFormat;
            set
            {
                m_TotalLabelFormat = value;
                if (IsViewLoaded)
                    RefreshTotalLabels();
            }
        }

        /// <summary>Optional line appended to every bar cell's tooltip.</summary>
        public string TooltipHint
        {
            get => m_TooltipHint;
            set
            {
                if (m_TooltipHint == value)
                    return;

                m_TooltipHint = value;
                if (!IsViewLoaded)
                    return;

                RefreshView();
            }
        }

        public bool Normalize
        {
            get => m_NormalizeBars;
            set
            {
                m_NormalizeBars = value;
                if (IsViewLoaded)
                    RefreshView();
            }
        }

        public bool ShowResidentMemory
        {
            get => m_ShowResidentMemory;
            set
            {
                m_ShowResidentMemory = value;
                if (IsViewLoaded)
                    RefreshView();
            }
        }

        // Use Resident size as a source for total calculations in tooltip.
        // If false then resident contribution is calculated against committed memory.
        public bool UseResidentSizeAsSourceInTooltips
        {
            get => m_UseResidentAsSourceInTooltips;
            set
            {
                m_UseResidentAsSourceInTooltips = value;
                if (IsViewLoaded)
                    RefreshView();
            }
        }

        /// <summary>Size the bars by the resident portion instead of the committed size, which stays in the tooltip.</summary>
        public bool ShowResidentSizeOnly
        {
            get => m_ShowResidentSizeOnly;
            set
            {
                if (m_ShowResidentSizeOnly == value)
                    return;

                m_ShowResidentSizeOnly = value;
                if (IsViewLoaded)
                    RefreshView();
            }
        }

        /// <summary>See <see cref="MemoryBarElement.DeEmphasizeNonResidentPortion"/>.</summary>
        public bool DeEmphasizeNonResidentPortion
        {
            get => m_DeEmphasizeNonResidentPortion;
            set
            {
                if (m_DeEmphasizeNonResidentPortion == value)
                    return;

                m_DeEmphasizeNonResidentPortion = value;
                if (IsViewLoaded)
                    RefreshView();
            }
        }

        public bool ForceShowResidentBars
        {
            get => m_ForceShowResidentBars;
            set
            {
                if (m_ForceShowResidentBars == value)
                    return;

                m_ForceShowResidentBars = value;
                RefreshCellsForceResidentBar(m_CellsA, m_ForceShowResidentBars);
                RefreshCellsForceResidentBar(m_CellsB, m_ForceShowResidentBars);
            }
        }

        public bool ShowSnapshotLabels
        {
            get => m_ShowSnapshotLabels;
            set
            {
                m_ShowSnapshotLabels = value;
                if (IsViewLoaded)
                    RefreshView();
            }
        }

        /// <param name="revealSplitOnAllBars">Reveal the committed/resident split on every bar rather than only the hovered one.</param>
        public void SetCellHovered(int index, bool state, bool revealSplitOnAllBars = true)
        {
            SetCellHoverState(m_CellsA, index, state, revealSplitOnAllBars);
            SetCellHoverState(m_CellsB, index, state, revealSplitOnAllBars);
        }

        public void SetCellSelected(int index, bool state)
        {
            if (state)
                m_SelectedCellIndex = index;
            else if (m_SelectedCellIndex == index)
                m_SelectedCellIndex = -1;

            SetCellSelectedState(m_CellsA, index, state);
            SetCellSelectedState(m_CellsB, index, state);
        }

        public void SetMinimumWidthExcludingTotalLabel(float minWidth)
        {
            if (!IsViewLoaded)
                return;

            const int k_Spacing = 8;
            var largestTotalLabelWidth = System.Math.Max(m_TotalA.contentRect.width, m_TotalB.contentRect.width);
            View.style.minWidth = minWidth + largestTotalLabelWidth + k_Spacing;
        }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");

            return view;
        }

        protected override void ViewLoaded()
        {
            GatherViewReferences();
            RefreshView();
        }

        protected virtual void GatherViewReferences()
        {
            m_ContainerA = View.Q(k_UxmlMemoryUsageBarContainerA);
            m_BarA = m_ContainerA.Q(k_UxmlMemoryUsageBarBreakdownBar);
            m_SnapshotLabelA = m_ContainerA.Q(k_UxmlMemoryUsageBarSnapshotLabel);
            m_TotalA = m_ContainerA.Q<Label>(k_UxmlMemoryUsageBarHeaderTitle);

            m_ContainerB = View.Q(k_UxmlMemoryUsageBarContainerB);
            m_BarB = m_ContainerB.Q(k_UxmlMemoryUsageBarBreakdownBar);
            m_SnapshotLabelB = m_ContainerB.Q(k_UxmlMemoryUsageBarSnapshotLabel);
            m_TotalB = m_ContainerB.Q<Label>(k_UxmlMemoryUsageBarHeaderTitle);
        }

        public void Update(MemorySummaryModel model)
        {
            m_Model = model;
            RefreshView();
        }

        void RefreshView()
        {
            var scaleTotalA = ScaleTotalOf(m_Model.TotalA, m_Model.ResidentTotalA);
            var scaleTotalB = ScaleTotalOf(m_Model.TotalB, m_Model.ResidentTotalB);
            var maxValue = Math.Max(scaleTotalA, scaleTotalB);

            // Build breakdown bar for snapshot *A*
            var maxValueBarA = m_NormalizeBars ? scaleTotalA : maxValue;
            var accumulatedTotalA = RefreshBar(m_BarA, ref m_CellsA, m_Model.TotalA, maxValueBarA, (row) => { return row.BaseSize; });
            MakeRemainderCell(m_BarA, BarExtentOf(accumulatedTotalA), maxValueBarA);
            UIElementsHelper.SetVisibility(m_SnapshotLabelA, m_ShowSnapshotLabels && m_Model.CompareMode);

            // Build breakdown bar for snapshot *B*
            UIElementsHelper.SetVisibility(m_ContainerB, m_Model.CompareMode);
            if (m_Model.CompareMode)
            {
                var maxValueBarB = m_NormalizeBars ? scaleTotalB : maxValue;
                var accumulatedTotalB = RefreshBar(m_BarB, ref m_CellsB, m_Model.TotalB, maxValueBarB, (row) => { return row.ComparedSize; });
                MakeRemainderCell(m_BarB, BarExtentOf(accumulatedTotalB), maxValueBarB);
                UIElementsHelper.SetVisibility(m_SnapshotLabelB, m_ShowSnapshotLabels);
            }

            RestoreCellStateAfterRebuild();
            RefreshTotalLabels();
        }

        /// <summary>
        /// Reapplies USS-class state lost when cells are rebuilt. Hover is not restored: the pointer is not over
        /// a freshly replaced cell, so no MouseLeaveEvent would ever clear it.
        /// </summary>
        void RestoreCellStateAfterRebuild()
        {
            if (m_SelectedCellIndex >= m_Model.Rows.Count)
                m_SelectedCellIndex = -1;

            if (m_SelectedCellIndex >= 0)
            {
                SetCellSelectedState(m_CellsA, m_SelectedCellIndex, true);
                SetCellSelectedState(m_CellsB, m_SelectedCellIndex, true);
            }

            RefreshCellsForceResidentBar(m_CellsA, m_ForceShowResidentBars);
            RefreshCellsForceResidentBar(m_CellsB, m_ForceShowResidentBars);
        }

        /// <summary>The total the bars are scaled against: the resident total in <see cref="ShowResidentSizeOnly"/> mode.</summary>
        ulong ScaleTotalOf(ulong committedTotal, ulong residentTotal) => m_ShowResidentSizeOnly ? residentTotal : committedTotal;

        void RefreshTotalLabels()
        {
            UIElementsHelper.SetVisibility(m_TotalA, m_TotalLabelFormat != null);
            UIElementsHelper.SetVisibility(m_TotalB, m_TotalLabelFormat != null);

            if (m_TotalLabelFormat != null)
            {
                m_TotalA.text = FormatTotalLabel(m_Model.TotalA, m_Model.ResidentTotalA);
                m_TotalB.text = FormatTotalLabel(m_Model.TotalB, m_Model.ResidentTotalB);
            }
        }

        /// <summary>Formats the bar label. <c>{0}</c> is the total the bar is scaled against, <c>{1}</c> the resident total.</summary>
        public string FormatTotalLabel(ulong committedTotal, ulong residentTotal)
        {
            return string.Format(m_TotalLabelFormat,
                EditorUtility.FormatBytes((long)ScaleTotalOf(committedTotal, residentTotal)),
                EditorUtility.FormatBytes((long)residentTotal));
        }

        MemorySize RefreshBar(VisualElement root, ref MemoryBarElement[] cells, ulong total, ulong maxValue, Func<MemorySummaryModel.Row, MemorySize> accessor)
        {
            // Remove all old bar parts
            root.Clear();

            // Create cells for column from model data
            var accumulatedTotal = new MemorySize();
            cells = new MemoryBarElement[m_Model.Rows.Count];
            for (var i = 0; i < m_Model.Rows.Count; i++)
            {
                var row = m_Model.Rows[i];
                var size = accessor(row);
                var elem = MakeCell(i, row.StyleId, row.Name, size, total, maxValue, row.ResidentSizeUnavailable);
                cells[i] = elem;
                root.Add(elem);

                accumulatedTotal += size;
            }

            return accumulatedTotal;
        }

        MemoryBarElement MakeCell(int rowId, string styleId, string name, MemorySize size, ulong total, ulong maxValue, bool ignoreResidentMemory)
        {
            var cell = new MemoryBarElement();

            if (m_ShowResidentMemory && !ignoreResidentMemory)
            {
                if (m_ShowResidentSizeOnly)
                    cell.Mode = MemoryBarElement.VisibilityMode.ResidentOnlyWithCommittedTooltip;
                else
                    cell.Mode = m_UseResidentAsSourceInTooltips ? MemoryBarElement.VisibilityMode.ResidentOverCommitted : MemoryBarElement.VisibilityMode.CommittedAndResidentOnHover;
            }
            else
                cell.Mode = MemoryBarElement.VisibilityMode.CommittedOnly;

            cell.DeEmphasizeNonResidentPortion = m_DeEmphasizeNonResidentPortion;
            cell.Set(name, size, total, maxValue, in m_DataOverrides);
            if (!string.IsNullOrEmpty(m_TooltipHint))
                cell.tooltip += "\n\n" + m_TooltipHint;
            cell.SetStyle(styleId);
            RegisterCellCallbacks(cell, rowId);
            return cell;
        }

        /// <summary>The size a bar cell is drawn at: the resident portion in <see cref="ShowResidentSizeOnly"/> mode.</summary>
        ulong BarExtentOf(MemorySize size) => m_ShowResidentSizeOnly ? size.Resident : size.Committed;

        /// <summary>
        /// Pads a bar out to the other snapshot's larger scale in compare mode. It is not part of this snapshot's
        /// memory, so it never carries a category or de-emphasis color.
        /// </summary>
        void MakeRemainderCell(VisualElement root, ulong value, ulong total)
        {
            if (value >= total)
                return;

            float widthInPercent = ((float)(total - value)) / total * 100;

            var cell = new VisualElement();
            cell.AddToClassList(k_UxmlMemoryUsageBarCell);
            cell.AddToClassList(k_UxmlMemoryUsageBarCellRemainder);
            cell.style.width = new StyleLength(Length.Percent(widthInPercent));
            cell.style.marginLeft = cell.style.marginRight = (StyleLength)1.5;
            root.Add(cell);
        }

        void RegisterCellCallbacks(VisualElement element, int rowId)
        {
            element.RegisterCallback<MouseEnterEvent>((e) => { OnRowHovered?.Invoke(rowId, true); });
            element.RegisterCallback<MouseLeaveEvent>((e) => { OnRowHovered?.Invoke(rowId, false); });
            element.RegisterCallback<PointerDownEvent>((e) =>
            {
                OnRowClicked?.Invoke(rowId);
                if (e.clickCount == 2)
                    OnRowDoubleClicked?.Invoke(rowId);
                e.StopPropagation();
            });
        }

        void SetCellHoverState(MemoryBarElement[] cells, int index, bool state, bool revealSplitOnAllBars)
        {
            if ((cells == null) || (index >= cells.Length))
                return;

            var element = cells[index];
            if (state)
                element.AddToClassList(k_UxmlLegendTableCellHoverStateClass);
            else
                element.RemoveFromClassList(k_UxmlLegendTableCellHoverStateClass);

            if (revealSplitOnAllBars)
                RefreshCellsForceResidentBar(cells, state);
            else
                RefreshCellForceResidentBar(element, state);
        }

        void SetCellSelectedState(MemoryBarElement[] cells, int index, bool state)
        {
            if ((cells == null) || (index >= cells.Length))
                return;

            var element = cells[index];
            if (state)
                element.AddToClassList(k_UxmlLegendTableCellSelectedStateClass);
            else
                element.RemoveFromClassList(k_UxmlLegendTableCellSelectedStateClass);
        }

        void RefreshCellsForceResidentBar(MemoryBarElement[] cells, bool state)
        {
            if (cells == null)
                return;

            foreach (var cell in cells)
                RefreshCellForceResidentBar(cell, state);
        }

        void RefreshCellForceResidentBar(MemoryBarElement cell, bool state)
        {
            if (!m_ShowResidentMemory || m_ShowResidentSizeOnly)
                return;

            MemoryBarElement.VisibilityMode newMode;
            if (!m_UseResidentAsSourceInTooltips)
                newMode = state ? MemoryBarElement.VisibilityMode.CommittedAndResident : MemoryBarElement.VisibilityMode.CommittedAndResidentOnHover;
            else
                newMode = MemoryBarElement.VisibilityMode.ResidentOverCommitted;

            cell.Mode = newMode;
        }
    }
}

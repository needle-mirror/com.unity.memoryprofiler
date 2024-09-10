using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class MemorySummaryLegendViewController : ViewController
    {
        const string k_UxmlAssetGuid = "3bf369f02dcfe494284c624593e24cfe";
        const string k_UxmlNameCellGuid = "c30ef0628d7c0d446a7befff07cae5b7";
        const string k_UxmlSizeCellGuid = "223694cb87b669347baf1a3e5aec5ddb";

        // Legend table columns
        const string k_UxmlColumnName = "memory-summary__legend__name-column";
        const string k_UxmlColumnA = "memory-summary__legend__snapshot-a-column";
        const string k_UxmlColumnB = "memory-summary__legend__snapshot-b-column";
        const string k_UxmlColumnDiff = "memory-summary__legend__diff-column";
        // Legend table column elements
        const string k_UxmlColumnHeader = "memory-summary__legend__column-controls";
        const string k_UxmlColumnCells = "memory-summary__legend__cells";
        const string k_UxmlFirstRow = "memory-summary__legend__first-row";
        const string k_UxmlLastRow = "memory-summary__legend__last-row";
        // Legend table cell elements
        const string k_UxmlCellNameLabel = "memory-summary__legend__name";
        const string k_UxmlCellValueLabel = "memory-summary__legend__size-column";
        const string k_UxmlCellColorBox = "memory-summary__legend__color-box";
        const string k_UxmlCellColorBoxFree = "memory-summary__legend__color-box__unused";
        const string k_UxmlCellColorBoxReserved = "memory-summary__legend__used-reserved";
        //  Element states
        const string k_UxmlCellHoverStateClass = "memory-summary__element-hovered";
        const string k_UxmlCellSelectedStateClass = "memory-summary__element-selected";
        // Category color style templates
        const string k_UxmlElementSolidColor = "memory-category-color__";

        // Model
        MemorySummaryModel m_Model;

        // View
        struct Column
        {
            public VisualElement Root;
            public VisualElement Header;
            public VisualElement CellsContainer;
        }

        VisualTreeAsset m_NameCellUxml;
        VisualTreeAsset m_SizeCellUxml;

        // View
        Column m_ColumnName;
        Column m_ColumnSnapshotA;
        Column m_ColumnSnapshotB;
        Column m_ColumnDifference;

        bool m_ShowResidentSize;
        bool m_UseResidentAsSource;

        public event Action<int, bool> OnRowHovered;
        public event Action<int> OnRowClicked;
        public event Action<int> OnRowDoubleClicked;

        public MemorySummaryLegendViewController(MemorySummaryModel model)
        {
            m_Model = model;
            m_ShowResidentSize = false;
            m_UseResidentAsSource = false;
        }

        public bool ShowResidentSize
        {
            get => m_ShowResidentSize;
            set
            {
                m_ShowResidentSize = value;
                if (IsViewLoaded)
                    RefreshView();
            }
        }

        public bool UseResidentAsSource
        {
            get => m_UseResidentAsSource;
            set
            {
                m_UseResidentAsSource = value;
                if (IsViewLoaded)
                    RefreshView();
            }
        }

        public void SetRowHovered(int index, bool state)
        {
            SetCellHoverState(m_ColumnName.CellsContainer, index, state);
            SetCellHoverState(m_ColumnSnapshotA.CellsContainer, index, state);
            SetCellHoverState(m_ColumnSnapshotB.CellsContainer, index, state);
            SetCellHoverState(m_ColumnDifference.CellsContainer, index, state);
        }

        public void SetRowSelected(int index, bool state)
        {
            SetCellSelectedState(m_ColumnName.CellsContainer, index, state);
            SetCellSelectedState(m_ColumnSnapshotA.CellsContainer, index, state);
            SetCellSelectedState(m_ColumnSnapshotB.CellsContainer, index, state);
            SetCellSelectedState(m_ColumnDifference.CellsContainer, index, state);
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

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        void GatherViewReferences()
        {
            GatherColumnReferences(k_UxmlColumnName, ref m_ColumnName);
            GatherColumnReferences(k_UxmlColumnA, ref m_ColumnSnapshotA);
            GatherColumnReferences(k_UxmlColumnB, ref m_ColumnSnapshotB);
            GatherColumnReferences(k_UxmlColumnDiff, ref m_ColumnDifference);

            m_NameCellUxml = ViewControllerUtility.LoadVisualTreeAssetFromUxml(k_UxmlNameCellGuid);
            m_SizeCellUxml = ViewControllerUtility.LoadVisualTreeAssetFromUxml(k_UxmlSizeCellGuid);
        }

        void GatherColumnReferences(string id, ref Column ret)
        {
            ret = new Column();
            ret.Root = View.Q<VisualElement>(id);
            ret.Header = ret.Root.Q<VisualElement>(k_UxmlColumnHeader);
            ret.CellsContainer = ret.Root.Q<VisualElement>(k_UxmlColumnCells);
        }

        public void Update(MemorySummaryModel model)
        {
            m_Model = model;
            RefreshView();
        }

        void RefreshView()
        {
            // Update legend table header snapshot id icons state
            UIElementsHelper.SetVisibility(m_ColumnName.Header, m_Model.CompareMode);
            UIElementsHelper.SetVisibility(m_ColumnSnapshotA.Header, m_Model.CompareMode);
            UIElementsHelper.SetVisibility(m_ColumnSnapshotB.Header, m_Model.CompareMode);
            UIElementsHelper.SetVisibility(m_ColumnDifference.Header, m_Model.CompareMode);

            // Build names column for the Legend Table
            RefreshColumnCells(ref m_ColumnName, (rowId, row, reusableCell) =>
            {
                return MakeNameCell(rowId, reusableCell);
            });

            // Build Snapshot *A* column for the Legend Table
            RefreshColumnCells(ref m_ColumnSnapshotA, (rowId, row, reusableCell) =>
            {
                return MakeSizeCell(rowId, reusableCell, row.BaseSize);
            });

            // Build Snapshot *B* and *diff* column for the Legend Table
            UIElementsHelper.SetVisibility(m_ColumnSnapshotB.Root, m_Model.CompareMode);
            UIElementsHelper.SetVisibility(m_ColumnDifference.Root, m_Model.CompareMode);
            if (m_Model.CompareMode)
            {
                UIElementsHelper.SetVisibility(m_ColumnSnapshotB.Root, true);
                RefreshColumnCells(ref m_ColumnSnapshotB, (rowId, row, reusableCell) =>
                {
                    return MakeSizeCell(rowId, reusableCell, row.ComparedSize);
                });

                UIElementsHelper.SetVisibility(m_ColumnDifference.Root, true);
                RefreshColumnCells(ref m_ColumnDifference, (rowId, row, reusableCell) =>
                {
                    var diffTotal = (long)row.ComparedSize.Committed - (long)row.BaseSize.Committed;
                    var diffInner = (long)row.ComparedSize.Resident - (long)row.BaseSize.Resident;
                    return MakeSizeCell(rowId, reusableCell, diffTotal, diffInner);
                });
            }
        }

        void RefreshColumnCells(ref Column column, Func<int, MemorySummaryModel.Row, VisualElement, VisualElement> makeCell)
        {
            var reuseOldCells = column.CellsContainer.childCount == m_Model.Rows.Count;
            if (!reuseOldCells)
            {
                // Remove all old cells
                column.CellsContainer.Clear();
            }

            // Create cells for column from model data
            for (var i = 0; i < m_Model.Rows.Count; i++)
            {
                var elem = makeCell(i, m_Model.Rows[i], reuseOldCells ? column.CellsContainer[i] : null);
                if (!reuseOldCells)
                {
                    if (i == 0)
                        elem.AddToClassList(k_UxmlFirstRow);
                    if (i == m_Model.Rows.Count - 1)
                        elem.AddToClassList(k_UxmlLastRow);
                    column.CellsContainer.Add(elem);
                }
            }
        }

        VisualElement MakeNameCell(int rowId, VisualElement reusableCell)
        {
            var row = m_Model.Rows[rowId];

            var item = reusableCell ?? ViewControllerUtility.Instantiate(m_NameCellUxml);
            item.tooltip = MakeTooltipText(rowId);
            if (reusableCell == null)
                RegisterCellCallbacks(item, rowId);

            var colorBox = item.Q<VisualElement>(k_UxmlCellColorBox);
            var colorBoxClassName = k_UxmlElementSolidColor + row.StyleId;
            if (reusableCell != null)
            {
                string oldClassName = null;
                foreach (var klass in colorBox.GetClasses())
                {
                    if (klass.StartsWith(k_UxmlElementSolidColor))
                    {
                        Debug.Assert(oldClassName == null, "Added two coloring class names");
                        oldClassName = klass;
                    }
                }
                if (oldClassName != colorBoxClassName)
                {
                    colorBox.SwitchClasses(colorBoxClassName, oldClassName);
                }
            }
            else
            {
                colorBox.AddToClassList(colorBoxClassName);
            }
            var colorBoxUnused = item.Q<VisualElement>(k_UxmlCellColorBoxFree);
            UIElementsHelper.SetVisibility(colorBoxUnused, row.BaseSize.Resident > 0);

            var reservedLabel = item.Q<VisualElement>(k_UxmlCellColorBoxReserved);
            UIElementsHelper.SetVisibility(reservedLabel, false);

            var text = item.Q<Label>(k_UxmlCellNameLabel);
            text.text = row.Name;
            return item;
        }

        VisualElement MakeSizeCell(int rowId, VisualElement reusableCell, MemorySize size)
        {
            return MakeSizeCell(rowId, reusableCell, (long)size.Committed, (long)size.Resident);
        }

        VisualElement MakeSizeCell(int rowId, VisualElement reusableCell, long committed, long resident)
        {
            long valToUse = m_ShowResidentSize && m_UseResidentAsSource ? resident : committed;
            // FormatBytes doesn't handle negative values, sadly (and it's probably too late to change that now)
            var sizeText = EditorUtility.FormatBytes(Math.Abs(valToUse));
            if (valToUse < 0)
                sizeText = "-" + sizeText;
            var item = reusableCell ?? ViewControllerUtility.Instantiate(m_SizeCellUxml);
            item.Q<Label>(k_UxmlCellValueLabel).text = sizeText;
            item.tooltip = MakeTooltipText(rowId);
            if (reusableCell == null)
                RegisterCellCallbacks(item, rowId);
            return item;
        }

        string MakeTooltipText(int rowId)
        {
            var row = m_Model.Rows[rowId];
            string toolTipText;

            var showAllocated = !(m_ShowResidentSize && m_UseResidentAsSource);
            var showResident = m_ShowResidentSize && !row.ResidentSizeUnavailable;
            if (m_Model.CompareMode)
            {
                toolTipText = MemorySizeTooltipBuilder.MakeTooltip("\nA:", row.BaseSize, m_Model.TotalA, showAllocated, showResident, " - ");
                toolTipText += MemorySizeTooltipBuilder.MakeTooltip("\nB:", row.ComparedSize, m_Model.TotalB, showAllocated, showResident, " - ");
            }
            else
                toolTipText = MemorySizeTooltipBuilder.MakeTooltip(string.Empty, row.BaseSize, m_Model.TotalA, showAllocated, showResident, string.Empty);

            return toolTipText;
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

        void SetCellHoverState(VisualElement root, int index, bool state)
        {
            if (root.childCount <= index)
                return;

            var element = root[index];
            if (state)
                element.AddToClassList(k_UxmlCellHoverStateClass);
            else
                element.RemoveFromClassList(k_UxmlCellHoverStateClass);
        }

        void SetCellSelectedState(VisualElement root, int index, bool state)
        {
            if (root.childCount <= index)
                return;

            var element = root[index];
            if (state)
                element.AddToClassList(k_UxmlCellSelectedStateClass);
            else
                element.RemoveFromClassList(k_UxmlCellSelectedStateClass);
        }
    }
}

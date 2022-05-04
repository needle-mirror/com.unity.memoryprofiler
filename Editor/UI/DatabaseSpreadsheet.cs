using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using Filter = Unity.MemoryProfiler.Editor.Database.Operation.Filter;
using Unity.MemoryProfiler.Editor.Database.Operation;
using Unity.MemoryProfiler.Editor.Database.Operation.Filter;
using Unity.Collections.LowLevel.Unsafe;
using Unity.EditorCoroutines.Editor;
using System.Collections;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// A spreadsheet containing data from a Database.Table
    /// </summary>
    internal class DatabaseSpreadsheet : TextSpreadsheet
    {
        public event Action UserChangedFilters = delegate {};

        protected Database.Table m_TableSource;
        protected Database.Table m_TableDisplay;
        protected FormattingOptions m_FormattingOptions;

        const string k_DisplayWidthPrefKeyBase = "Unity.MemoryProfiler.Editor.Database.DisplayWidth";
        const string k_NewLineSeparator = "\n";
        string[] m_DisplayWidthPrefKeysPerColumn;

        long m_CurrentRowCount = 0;
        long m_CurrentAccumulativeSize = 0;
        long m_CurrentAccumulativeSizeSame = 0;
        long m_CurrentAccumulativeSizeNew = 0;
        long m_CurrentAccumulativeSizeDeleted = 0;

        [NonSerialized]
        bool m_Initialized = false;

        bool m_WasDirty = false;
        EditorCoroutine m_DelayedOnGUICall = null;
        public bool ViewStateFilteringChangedSinceLastSelectionOrViewClose { get; private set; }
        public void ViewStateIsCleaned() => ViewStateFilteringChangedSinceLastSelectionOrViewClose = false;

        public int GetDisplayWidth(int columnIndex, int defaultDisplayWidth)
        {
            return EditorPrefs.GetInt(m_DisplayWidthPrefKeysPerColumn[columnIndex], defaultDisplayWidth);
        }

        public void SetDisplayWidth(int columnIndex, int value)
        {
            value = Mathf.Max(value, UI.SplitterStateEx.MinSplitterSize);
            EditorPrefs.SetInt(m_DisplayWidthPrefKeysPerColumn[columnIndex], value);
        }

        public Database.Table SourceTable
        {
            get
            {
                return m_TableSource;
            }
        }

        public Database.Table DisplayTable
        {
            get
            {
                return m_TableDisplay;
            }
        }

        public override long RowCount
        {
            get
            {
                return m_TableDisplay.GetRowCount();
            }
        }

        public long SelectedRow
        {
            get
            {
                return m_GUIState.SelectedRow;
            }

            set
            {
                if (m_GUIState.SelectedRow != value)
                {
                    m_GUIState.SelectedRow = value;
                    OnRowSelectionChanged(value);
                }
            }
        }

        public void RestoreSelectedRow(long rowIndex)
        {
            m_GUIState.SelectedRow = rowIndex;
            if (rowIndex >= 0)
                Goto(GetLinkToCurrentSelection(), onlyScrollIfNeeded: true);
        }

        // having no listeners signifies that links don't work and don't get a link cursor change, so, no  "= delegate { };" here
        public event Action<DatabaseSpreadsheet, Database.LinkRequest, Database.CellPosition> LinkClicked = null;

        //keep the state of each column's desired filters
        //does not contain order in which the filters should be applied
        //in order of m_TableSource columns
        protected Filter.ColumnState[] m_ColumnState;

        HashSet<long> m_ColumnsWithMatchFilters = new HashSet<long>();
        Filter.Multi m_Filters = new Filter.Multi();
        Filter.Sort m_AllLevelSortFilter = new Filter.Sort();
        //filter.DefaultSort allLevelDefaultSort = new filter.DefaultSort();

        public struct State : IEquatable<State>
        {
            public Database.Operation.Filter.Filter Filter;
            public Database.CellLink SelectedCell;
            public Vector2 ScrollPosition;
            public List<Database.CellPosition> ExpandedCells;

            public long SelectedRow;
            public bool SelectionIsLatent;
            public long FirstVisibleRow;
            public float FirstVisibleRowY;
            public long FirstVisibleRowIndex;//sequential index assigned to all visible row. Differ from row index if there are invisible rows
            public double HeightBeforeFirstVisibleRow;//using double since this value will be maintained by offseting it.

            public bool Equals(State other)
            {
                return Filter.Equals(other.Filter) &&
                    SelectedCell == other.SelectedCell &&
                    (SelectedCell == null || SelectedCell.ToString().Equals(other.SelectedCell.ToString())) &&
                    ScrollPosition.Equals(other.ScrollPosition) &&
                    ExpandedCells == other.ExpandedCells &&
                    SelectedRow == other.SelectedRow &&
                    SelectionIsLatent == other.SelectionIsLatent &&
                    FirstVisibleRow == other.FirstVisibleRow &&
                    FirstVisibleRowIndex == other.FirstVisibleRowIndex &&
                    HeightBeforeFirstVisibleRow == other.HeightBeforeFirstVisibleRow;
            }
        }

        public State CurrentState
        {
            get
            {
                State state = new State();

                state.SelectedCell = GetLinkToCurrentSelection();

                state.Filter = GetCurrentFilterCopy();
                state.ScrollPosition = m_GUIState.ScrollPosition;
                state.SelectedRow = m_GUIState.SelectedRow;
                state.SelectionIsLatent = m_GUIState.SelectionIsLatent;
                state.FirstVisibleRow = m_GUIState.FirstVisibleRow;
                state.FirstVisibleRowIndex = m_GUIState.FirstVisibleRowIndex;
                state.FirstVisibleRowY = m_GUIState.FirstVisibleRowY;
                state.HeightBeforeFirstVisibleRow = m_GUIState.HeightBeforeFirstVisibleRow;

                state.ExpandedCells = new List<Database.CellPosition>();
                var rowCount = DisplayTable.GetRowCount();
                var columnCount = DisplayTable.GetMetaData().GetColumnCount();
                for (long row = 0; row < rowCount; row++)
                {
                    for (int col = 0; col < columnCount; col++)
                    {
                        var expendedState = DisplayTable.GetCellExpandState(row, col);
                        if (expendedState.isColumnExpandable && expendedState.isExpanded)
                        {
                            state.ExpandedCells.Add(new Database.CellPosition(row, col));
                        }
                    }
                }

                return state;
            }
            set
            {
                InitFilter(value.Filter, value.ExpandedCells);

                m_GUIState.ScrollPosition = value.ScrollPosition;
                m_GUIState.SelectedRow = value.SelectedRow;
                m_GUIState.SelectionIsLatent = value.SelectionIsLatent;
                m_GUIState.FirstVisibleRow = value.FirstVisibleRow;
                m_GUIState.FirstVisibleRowIndex = value.FirstVisibleRowIndex;
                m_GUIState.FirstVisibleRowY = value.FirstVisibleRowY;
                m_GUIState.HeightBeforeFirstVisibleRow = value.HeightBeforeFirstVisibleRow;
            }
        }


        public DatabaseSpreadsheet(FormattingOptions formattingOptions, Database.Table table, IViewEventListener listener, State state)
            : base(listener)
        {
            m_TableSource = table;
            m_TableDisplay = table;
            m_FormattingOptions = formattingOptions;

            InitializeIfNeeded();
            CurrentState = state;
        }

        public DatabaseSpreadsheet(FormattingOptions formattingOptions, Database.Table table, IViewEventListener listener)
            : base(listener)
        {
            m_TableSource = table;
            m_TableDisplay = table;
            m_FormattingOptions = formattingOptions;

            InitializeIfNeeded();
            InitDefaultTableFilter();
        }

        protected override void InitializeIfNeeded()
        {
            if (!m_Initialized)
            {
                InitSplitter();
                Styles.Initialize();
            }
            m_Initialized = true;
        }

        private void InitSplitter()
        {
            var meta = m_TableSource.GetMetaData();
            int colCount = meta.GetColumnCount();
            m_ColumnState = new Filter.ColumnState[colCount];
            int[] colSizes = new int[colCount];
            bool[] colShown = new bool[colCount];
            string[] colNames = new string[colCount];

            string basePrefKey = k_DisplayWidthPrefKeyBase /*+ DisplayTable.GetName()*/;
            m_DisplayWidthPrefKeysPerColumn = new string[colCount];
            for (int i = 0; i != colCount; ++i)
            {
                var column = meta.GetColumnByIndex(i);
                m_DisplayWidthPrefKeysPerColumn[i] = basePrefKey + column.Name;
                colSizes[i] = GetDisplayWidth(i, column.DefaultDisplayWidth);
                colShown[i] = column.ShownByDefault;
                colNames[i] = column.DisplayName;
                m_ColumnState[i] = new Filter.ColumnState();
            }
            m_Splitter = new SplitterStateEx(colSizes, colShown, colNames);
            m_Splitter.RealSizeChanged += SetDisplayWidth;
        }

        void UpdateMatchFilterState()
        {
            m_ColumnsWithMatchFilters.Clear();
            var filterList = m_Filters.filters;
            foreach (var f in filterList)
            {
                var fM = f as Filter.Match;
                if (fM != null)
                    m_ColumnsWithMatchFilters.Add(fM.ColumnIndex);
            }
        }

        private void InitEmptyFilter(List<Database.CellPosition> expandedCells = null)
        {
            m_Filters = new Filter.Multi();
            var ds = new Database.Operation.Filter.DefaultSort(m_AllLevelSortFilter, null);
            m_Filters.filters.Add(ds);
            UpdateDisplayTable(expandedCells);
        }

        protected void InitFilter(Database.Operation.Filter.Filter filter, List<Database.CellPosition> expandedCells = null)
        {
            if (filter == null)
            {
                InitEmptyFilter(expandedCells);
                return;
            }
            Database.Operation.Filter.FilterCloning fc = new Database.Operation.Filter.FilterCloning();
            var deffilter = filter.Clone(fc);
            if (deffilter != null)
            {
                m_Filters = new Filter.Multi();

                m_Filters.filters.Add(deffilter);

                m_AllLevelSortFilter = fc.GetFirstUniqueOf<Filter.Sort>();
                if (m_AllLevelSortFilter == null)
                {
                    m_AllLevelSortFilter = new Filter.Sort();
                    var ds = new Database.Operation.Filter.DefaultSort(m_AllLevelSortFilter, null);
                    m_Filters.filters.Add(ds);
                }
                bool bDirty = false;
                m_Filters.Simplify(ref bDirty);
                UpdateDisplayTable(expandedCells);
            }
            else
            {
                InitEmptyFilter(expandedCells);
            }
        }

        protected void InitDefaultTableFilter()
        {
            var meta = m_TableSource.GetMetaData();
            if (meta.defaultFilter == null)
            {
                InitEmptyFilter();
                return;
            }
            InitFilter(meta.defaultFilter);
        }

        public Database.CellLink GetLinkToCurrentSelection()
        {
            if (m_GUIState.SelectedRow >= 0)
            {
                return m_TableDisplay.GetLinkTo(new Database.CellPosition(m_GUIState.SelectedRow, 0));
            }
            return null;
        }

        public Database.CellLink GetLinkToFirstVisible()
        {
            if (m_GUIState.FirstVisibleRow >= 0)
            {
                return m_TableDisplay.GetLinkTo(new Database.CellPosition(m_GUIState.FirstVisibleRow, 0));
            }
            return null;
        }

        public Database.Operation.Filter.Filter GetCurrentFilterCopy()
        {
            Database.Operation.Filter.FilterCloning fc = new Database.Operation.Filter.FilterCloning();
            return m_Filters.Clone(fc);
        }

        public void Goto(Database.CellLink cl, bool onlyScrollIfNeeded = false)
        {
            Goto(cl.Apply(m_TableDisplay), onlyScrollIfNeeded);
        }

        protected override long GetFirstRow()
        {
            long c = m_TableDisplay.GetRowCount();
            if (c <= 0) return -1;
            return 0;
        }

        protected override long GetNextVisibleRow(long row)
        {
            row += 1;
            if (row >= m_TableDisplay.GetRowCount())
            {
                return -1;
            }
            return row;
        }

        protected override long GetPreviousVisibleRow(long row)
        {
            return row - 1;
        }

        protected override DirtyRowRange SetCellExpanded(long row, long col, bool expanded)
        {
            MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInPage, MemoryProfilerAnalytics.PageInteractionType>(
                MemoryProfilerAnalytics.PageInteractionType.TreeViewElementWasExpanded);

            DirtyRowRange o;
            o.Range = m_TableDisplay.ExpandCell(row, (int)col, expanded);
            o.HeightOffset = k_RowHeight * o.Range.Length;
            return o;
        }

        protected override bool GetCellExpanded(long row, long col)
        {
            return m_TableDisplay.GetCellExpandState(row, (int)col).isExpanded;
        }

        protected override void DrawHeader(long col, Rect r, SplitterStateEx splitter, ref GUIPipelineState pipe)
        {
            var colState = m_ColumnState[col];

            string str = m_FormattingOptions.ObjectDataFormatter.ShowPrettyNames
                ? m_TableDisplay.GetMetaData().GetColumnByIndex((int)col).DisplayName
                : m_TableDisplay.GetMetaData().GetColumnByIndex((int)col).Name;
            if (colState.Grouped)
            {
                str = "[" + str + "]";
            }
            var sorted = colState.Sorted != SortOrder.None ? colState.Sorted : colState.DefaultSorted;
            var sortName = Filter.Sort.GetSortName(sorted);
            str = sortName + " " + str;

            if (!GUI.Button(r, str, Styles.General.Header))
                return;

            var meta = m_TableSource.GetMetaData();
            var metaCol = meta.GetColumnByIndex((int)col);
            bool canGroup = false;
            if (metaCol != null)
            {
                if (metaCol.DefaultGroupAlgorithm != null)
                {
                    canGroup = true;
                }
            }

            var menu = new GenericMenu();
            const string strGroup = "Group";
            const string strSortAsc = "▲ Sort Ascending";
            const string strSortDsc = "▼ Sort Descending";
            const string strMatch = "Match...";
            const string strHide = "Hide Column";
            const string strShow = "Show Column/";
            if (canGroup)
            {
                menu.AddItem(new GUIContent(strGroup), colState.Grouped, () =>
                {
                    if (colState.Grouped)
                        RemoveSubGroupFilter((int)col);
                    else
                        AddSubGroupFilter((int)col);
                });
            }

            menu.AddItem(new GUIContent(strSortAsc), sorted == SortOrder.Ascending, () =>
            {
                if (sorted == SortOrder.Ascending)
                    RemoveDefaultSortFilter();
                else
                    SetDefaultSortFilter((int)col, SortOrder.Ascending);
            });

            menu.AddItem(new GUIContent(strSortDsc), sorted == SortOrder.Descending, () =>
            {
                if (sorted == SortOrder.Descending)
                    RemoveDefaultSortFilter();
                else
                    SetDefaultSortFilter((int)col, SortOrder.Descending);
            });

            if (m_ColumnsWithMatchFilters.Contains(col))
            {
                menu.AddDisabledItem(new GUIContent(strMatch));
            }
            else
            {
                menu.AddItem(new GUIContent(strMatch), false, () =>
                {
                    AddMatchFilter((int)col);
                    m_ColumnsWithMatchFilters.Add(col);
                });
            }
            menu.AddSeparator("");

            if (splitter.CanHideColumns)
                menu.AddItem(new GUIContent(strHide), false, () =>
                {
                    MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.ColumnVisibilityChangedEvent>();
                    splitter.HideColumn(col);

                    MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.ColumnVisibilityChangedEvent() { viewName = m_TableDisplay.GetName(), shownOrHidden = false, columnIndex = (int)col, columnName = m_TableDisplay.GetMetaData().GetColumnByIndex((int)col).Name});

                    MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInPage, MemoryProfilerAnalytics.PageInteractionType>(
                        MemoryProfilerAnalytics.PageInteractionType.ColumnWasHidden);
                });
            else
                menu.AddDisabledItem(new GUIContent(strHide), false);

            if (splitter.AreColumnsHidden)
            {
                var HiddenColumns = splitter.GetHiddenColumnNames();
                foreach (var hiddenColumn in HiddenColumns)
                {
                    menu.AddItem(new GUIContent(strShow + hiddenColumn.Name), false, () =>
                    {
                        MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.ColumnVisibilityChangedEvent>();
                        splitter.ShowColumn(hiddenColumn.Index);

                        MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.ColumnVisibilityChangedEvent() { viewName = m_TableDisplay.GetName(), shownOrHidden = true, columnIndex = hiddenColumn.Index, columnName = hiddenColumn.Name });

                        MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInPage, MemoryProfilerAnalytics.PageInteractionType>(
                            MemoryProfilerAnalytics.PageInteractionType.ColumnWasRevealed);
                    });
                }
            }

            menu.DropDown(r);
        }

        List<MemoryProfilerAnalytics.Filter> m_FilterBuffer = new List<MemoryProfilerAnalytics.Filter>();

        void ReportFilterChanges()
        {
            m_FilterBuffer.Clear();
            foreach (var filter in m_Filters.filters)
            {
                if (filter is Filter.Sort)
                {
                    var sortFilter = (filter as Filter.Sort);
                    var level = (sortFilter.SortLevel != null && sortFilter.SortLevel.Count > 0) ? sortFilter.SortLevel[0] : null;
                    if (level == null)
                        continue;
                    string columnName = GetColumnName(level);
                    if (level.Order != SortOrder.None)
                    {
                        MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.SortedColumnEvent>();
                        MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.SortedColumnEvent() { viewName = m_TableDisplay.GetName(), Ascending = level.Order == SortOrder.Ascending, shown = GetColumnIndex(level), fileName = GetColumnName(level) });
                    }
                    MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInPage, MemoryProfilerAnalytics.PageInteractionType>(
                        MemoryProfilerAnalytics.PageInteractionType.TableSortingWasChanged);
                    m_FilterBuffer.Add(new MemoryProfilerAnalytics.Filter() {column = columnName, filterName = "Sort" });
                }
                else if (filter is Filter.DefaultSort)
                {
                    var sortFilter = (filter as Filter.DefaultSort);
                    Filter.Sort.Level level = null;
                    if (sortFilter.SortOverride != null && sortFilter.SortOverride.SortLevel != null && sortFilter.SortOverride.SortLevel.Count > 0)
                        level = sortFilter.SortOverride.SortLevel[0];
                    if ((level == null || level.Order == SortOrder.None) && sortFilter.SortDefault != null && sortFilter.SortDefault.SortLevel != null && sortFilter.SortDefault.SortLevel.Count > 0)
                        level = sortFilter.SortDefault.SortLevel[0];
                    if (level == null)
                        continue;
                    string columnName = GetColumnName(level);
                    if (level != null && level.Order != SortOrder.None)
                    {
                        MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.SortedColumnEvent>();
                        MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.SortedColumnEvent() { viewName = m_TableDisplay.GetName(), Ascending = level.Order == SortOrder.Ascending, shown = GetColumnIndex(level), fileName = GetColumnName(level) });
                    }
                    MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInPage, MemoryProfilerAnalytics.PageInteractionType>(
                        MemoryProfilerAnalytics.PageInteractionType.TableSortingWasChanged);
                    m_FilterBuffer.Add(new MemoryProfilerAnalytics.Filter() { column = columnName, filterName = "Sort" });
                }
                else if (filter is Filter.Group)
                {
                    m_FilterBuffer.Add(new MemoryProfilerAnalytics.Filter() { column = (filter as Filter.Group).GetColumnName(m_TableDisplay), filterName = "Group"});
                }
                else if (filter is Filter.Match)
                {
                    var matchFilter = (filter as Filter.Match);
                    m_FilterBuffer.Add(new MemoryProfilerAnalytics.Filter() { column = matchFilter.GetColumnName(m_TableDisplay), filterName = "Match" });
                }
            }
            MemoryProfilerAnalytics.FiltersChanged(m_TableDisplay.GetName(),  m_FilterBuffer);
        }

        string GetColumnName(Filter.Sort.Level sortLevel)
        {
            var columnIndex = GetColumnIndex(sortLevel);
            if (columnIndex >= 0)
            {
                return m_TableDisplay.GetMetaData().GetColumnByIndex(columnIndex).Name;
            }
            return "";
        }

        int GetColumnIndex(Filter.Sort.Level sortLevel)
        {
            if (sortLevel is Filter.Sort.LevelByIndex)
            {
                return (sortLevel as Filter.Sort.LevelByIndex).ColumnIndex;
            }
            return -1;
        }

        public void UpdateTable()
        {
            if (!m_Initialized)
            {
                InitSplitter();
                m_Initialized = true;
            }

            var updater = m_TableDisplay.BeginUpdate();
            if (updater != null)
            {
                long sel = updater.OldToNewRow(m_GUIState.SelectedRow);

                //find the row that is still the first visible or the previous one that still exist after the uptate
                long fvr = -1;
                long fvr_before = m_GUIState.FirstVisibleRow;
                do
                {
                    fvr = updater.OldToNewRow(fvr_before);
                    --fvr_before;
                }
                while (fvr < 0 && fvr_before >= 0);

                //if did not find any valid first visible row, use selected row
                if (fvr < 0)
                {
                    fvr = sel;
                }

                m_TableSource.EndUpdate(updater);

                if (fvr >= 0)
                {
                    long nextRow;
                    long fvrIndex = 0;
                    float fvrY = GetCumulativeHeight(GetFirstRow(), fvr, out nextRow, ref fvrIndex);
                    long lastIndex = fvrIndex;
                    float totalh = fvrY + GetCumulativeHeight(nextRow, long.MaxValue, out nextRow, ref lastIndex);


                    m_GUIState.ScrollPosition.y = fvrY;
                    m_GUIState.FirstVisibleRowY = fvrY;
                    m_GUIState.FirstVisibleRow = fvr;
                    m_GUIState.FirstVisibleRowIndex = fvrIndex;
                    m_GUIState.HeightBeforeFirstVisibleRow = fvrY;
                    m_TotalDataHeight = totalh;
                }
                else
                {
                    m_GUIState.ScrollPosition = Vector2.zero;
                    m_GUIState.FirstVisibleRowY = 0;
                    m_GUIState.FirstVisibleRow = GetFirstRow();
                    m_GUIState.FirstVisibleRowIndex = 0;
                    m_GUIState.HeightBeforeFirstVisibleRow = 0;
                    long nextRow;
                    long lastIndex = 0;
                    m_TotalDataHeight = GetCumulativeHeight(GetFirstRow(), long.MaxValue, out nextRow, ref lastIndex);
                }

                SelectedRow = sel;
                //m_Listener.OnRepaint();
            }
            else
            {
                m_TableDisplay.EndUpdate(updater);
            }
            //UpdateDataState();
            //ResetGUIState();
        }

        public void UpdateDisplayTable(List<Database.CellPosition> expandedCells = null, bool resetState = true)
        {
            UpdateColumnState();
            var valid = m_TableSource is DiffTable ? ((DiffTable)m_TableSource).TablesAreValid() : true;
            if (resetState && valid)
                m_TableDisplay = m_Filters.CreateFilter(m_TableSource);

            UpdateExpandedState(expandedCells);
            UpdateDataState();
            if (resetState)
            {
                // we want to preserve if the selection state was latent and thats it
                var ls = m_GUIState.SelectionIsLatent;
                ResetGUIState();
                m_GUIState.SelectionIsLatent = ls;
            }

            m_CurrentRowCount = m_TableDisplay.GetRowCount();
            var col = m_TableDisplay.GetColumnByName("Size");
            if (col == null)
                col = m_TableDisplay.GetColumnByName("size");
            //if(col == null) // ignore addressSize, that's used for Memory Regions which may overlap / have sub regions in the same table so we can't just add them all up
            //    col = m_TableDisplay.GetColumnByName("addressSize");
            if (col != null)
            {
                if (col is Database.ColumnTyped<long>)
                {
                    var sizeColumn = col as Database.ColumnTyped<long>;
                    CalculateTotalMemory(sizeColumn);
                }

                if (col is Database.ColumnTyped<ulong>)
                {
                    var sizeColumn = col as Database.ColumnTyped<ulong>;
                    CalculateTotalMemory(sizeColumn);
                }

                if (col is Database.ColumnTyped<int>)
                {
                    var sizeColumn = col as Database.ColumnTyped<int>;
                    CalculateTotalMemory(sizeColumn);
                }
            }
        }

        void CalculateTotalMemory<T>(Database.ColumnTyped<T> sizeColumn) where T : unmanaged, System.IComparable
        {
            var diffCol = m_TableDisplay.GetColumnByName("Diff") as Database.ColumnTyped<DiffTable.DiffResult>;
            m_CurrentAccumulativeSize = 0;
            m_CurrentAccumulativeSizeSame = 0;
            m_CurrentAccumulativeSizeNew = 0;
            m_CurrentAccumulativeSizeDeleted = 0;
            var valueTypeSize = UnsafeUtility.SizeOf<T>();
            bool asLong = valueTypeSize == sizeof(long);
            if (diffCol != null)
            {
                for (long i = 0; i < m_CurrentRowCount; i++)
                {
                    T value = sizeColumn.GetRowValue(i);
                    long val = 0;
                    unsafe
                    {
                        long * outVal = &val;
                        void * inVal = &value;
                        UnsafeUtility.MemCpy(outVal, inVal, valueTypeSize);
                    }
                    switch (diffCol.GetRowValue(i))
                    {
                        case DiffTable.DiffResult.None:
                            break;
                        case DiffTable.DiffResult.Deleted:
                            m_CurrentAccumulativeSizeDeleted += val;
                            break;
                        case DiffTable.DiffResult.New:
                            m_CurrentAccumulativeSizeNew += val;
                            break;
                        case DiffTable.DiffResult.Same:
                            m_CurrentAccumulativeSizeSame += val;
                            break;
                        default:
                            break;
                    }
                }
            }
            else
            {
                for (long i = 0; i < m_CurrentRowCount; i++)
                {
                    T value = sizeColumn.GetRowValue(i);
                    long val = 0;
                    unsafe
                    {
                        long * outVal = &val;
                        void * inVal = &value;
                        UnsafeUtility.MemCpy(outVal, inVal, valueTypeSize);
                    }
                    m_CurrentAccumulativeSize += val;
                }
            }
        }

        void UpdateExpandedState(List<Database.CellPosition> expandedCells)
        {
            if (expandedCells == null)
                return;
            foreach (var cell in expandedCells)
            {
                m_TableDisplay.ExpandCell(cell.row, cell.col, true);
            }
        }

        protected override void DrawCell(long row, long col, Rect r, long index, bool selected, ref GUIPipelineState pipe)
        {
            var s = m_TableDisplay.GetCellExpandState(row, (int)col);

            if (s.isColumnExpandable)
            {
                int indent = s.expandDepth * 16;
                r.x += indent;
                r.width -= indent;
                if (s.isExpandable)
                {
                    Rect rToggle = new Rect(r.x, r.y, Styles.General.FoldoutWidth, r.height);
                    bool newExpanded = GUI.Toggle(rToggle, s.isExpanded, GUIContent.none, Styles.General.Foldout);
                    if (newExpanded != s.isExpanded)
                    {
                        pipe.processMouseClick = false;
                        SetCellExpandedState(row, col, newExpanded);
                    }
                }
                r.x += 16;
                r.width -= 16;
            }

            Database.LinkRequest link = null;
            if (LinkClicked != null)
            {
                link = m_TableDisplay.GetCellLink(new Database.CellPosition(row, (int)col));
            }
            if (Event.current.type == EventType.Repaint)
            {
                var tablerows = m_TableDisplay.GetRowCount();
                var column = m_TableDisplay.GetColumnByIndex((int)col);
                var metaColumn = m_TableDisplay.GetMetaData().GetColumnByIndex((int)col);
                if (column != null)
                {
                    var rowStrVal = String.Empty;
                    var formatter = m_FormattingOptions.GetFormatter(metaColumn.FormatName);

                    if (tablerows == column.GetRowCount())
                        rowStrVal = column.GetRowValueString(row, m_FormattingOptions.GetFormatter(metaColumn.FormatName));

                    var idx = rowStrVal.IndexOf(k_NewLineSeparator, StringComparison.InvariantCultureIgnoreCase);
                    string tooltip = null;
                    if (idx != -1)
                    {
                        tooltip = rowStrVal;
                        rowStrVal = rowStrVal.Substring(0, idx);
                    }

                    DrawTextEllipsis(rowStrVal, tooltip, r,
                        link == null ? Styles.General.NumberLabel : Styles.General.ClickableLabel
                        , EllipsisStyleMetricData, selected);
                }
            }
            if (link != null)
            {
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
                }
            }
        }

        protected override void OnGUI_CellMouseMove(Database.CellPosition pos)
        {
        }

        protected override void OnGUI_CellMouseDown(Database.CellPosition pos)
        {
            //UnityEngine.Debug.Log("MouseDown at (" + Event.current.mousePosition.x + ", " + Event.current.mousePosition.y + " row:" + row + " col:" + col);
        }

        protected override void OnGUI_CellMouseUp(Database.CellPosition pos)
        {
            if (LinkClicked != null)
            {
                var link = m_TableDisplay.GetCellLink(pos);
                if (link != null)
                {
                    LinkClicked(this, link, pos);
                }
            }
        }

        // update m_ColumnState from filters
        protected void UpdateColumnState()
        {
            long colCount = m_TableSource.GetMetaData().GetColumnCount();
            for (long i = 0; i != colCount; ++i)
            {
                m_ColumnState[i] = new Filter.ColumnState();
            }

            m_Filters.UpdateColumnState(m_TableSource, m_ColumnState);
        }

        public bool RemoveSubSortFilter(int colIndex, bool update = true)
        {
            if (m_AllLevelSortFilter.SortLevel.RemoveAll(x => x.GetColumnIndex(m_TableSource) == colIndex) > 0)
            {
                bool dirty = false;
                m_Filters.Simplify(ref dirty);
                if (update)
                {
                    UpdateDisplayTable();
                }
                return true;
            }
            ReportFilterChanges();
            return false;
        }

        // return if something change
        public bool AddSubSortFilter(int colIndex, SortOrder ss, bool update = true)
        {
            Filter.Sort.Level sl = new Filter.Sort.LevelByIndex(colIndex, ss);
            int index = m_AllLevelSortFilter.SortLevel.FindIndex(x => x.GetColumnIndex(m_TableSource) == colIndex);
            if (index >= 0)
            {
                if (m_AllLevelSortFilter.SortLevel[index].Equals(sl)) return false;
                m_AllLevelSortFilter.SortLevel[index] = sl;
            }
            else
            {
                m_AllLevelSortFilter.SortLevel.Add(sl);
            }
            if (update)
            {
                UpdateDisplayTable();
            }
            ReportFilterChanges();
            return true;
        }

        // return if something change
        public bool RemoveDefaultSortFilter(bool update = true)
        {
            bool changed = m_AllLevelSortFilter.SortLevel.Count > 0;
            m_AllLevelSortFilter.SortLevel.Clear();
            if (changed && update)
            {
                UpdateDisplayTable();
            }
            if (changed)
                ReportFilterChanges();
            return changed;
        }

        public bool SetDefaultSortFilter(int colIndex, SortOrder ss, bool update = true)
        {
            var changed = false;
            if (m_AllLevelSortFilter.SortLevel.Count != 1 || m_AllLevelSortFilter.SortLevel[0].Order != ss || m_AllLevelSortFilter.SortLevel[0].GetColumnIndex(SourceTable) != colIndex)
            {
                m_AllLevelSortFilter.SortLevel.Clear();

                if (ss != SortOrder.None)
                {
                    Filter.Sort.Level sl = new Filter.Sort.LevelByIndex(colIndex, ss);
                    m_AllLevelSortFilter.SortLevel.Add(sl);
                }
                changed = true;
            }
            if (!changed)
                // Nothing more to do here
                return changed;
            if (update)
            {
                UpdateDisplayTable();
            }
            ReportFilterChanges();
            return changed;
        }

        // return if something change
        public bool AddSubGroupFilter(int colIndex, bool update = true)
        {
            var newFilter = new Filter.GroupByColumnIndex(colIndex, SortOrder.Ascending);


            var ds = new Database.Operation.Filter.DefaultSort(m_AllLevelSortFilter, null);

            var gfp = GetDeepestGroupFilter(m_Filters);
            if (gfp.child != null)
            {
                //add the new group with the default sort filter
                var newMulti = new Filter.Multi();
                newMulti.filters.Add(newFilter);
                newMulti.filters.Add(ds);
                var subf = gfp.child.SubGroupFilter;
                gfp.child.SubGroupFilter = newMulti;
                newFilter.SubGroupFilter = subf;
            }
            else
            {
                //add it to top, already has te default sort filter there
                newFilter.SubGroupFilter = ds;
                m_Filters.filters.Insert(0, newFilter);
            }

            if (update)
            {
                UpdateDisplayTable();
            }
            ReportFilterChanges();
            return true;
        }

        // return if something change
        public bool RemoveSubGroupFilter(long colIndex, bool update = true)
        {
            FilterParenthood<Filter.Filter, Filter.Group> fpToRemove = new FilterParenthood<Filter.Filter, Filter.Group>();

            foreach (var fp in VisitAllSubGroupFilters(m_Filters))
            {
                if (fp.child.GetColumnIndex(m_TableSource) == colIndex)
                {
                    fpToRemove = fp;
                    break;
                }
            }

            if (fpToRemove.child != null)
            {
                if (Filter.Filter.RemoveFilter(fpToRemove.parent, fpToRemove.child))
                {
                    bool dirty = false;
                    m_Filters.Simplify(ref dirty);
                    if (update)
                    {
                        UpdateDisplayTable();
                    }
                    ReportFilterChanges();
                    return true;
                }
            }

            return false;
        }

        public bool AddMatchFilter(int colIndex, string matchString = "", bool update = true)
        {
            var newFilter = new Filter.Match(colIndex, matchString);

            m_Filters.filters.Insert(0, newFilter);

            if (update)
            {
                UpdateDisplayTable();
            }
            ReportFilterChanges();

            MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInPage, MemoryProfilerAnalytics.PageInteractionType>(
                MemoryProfilerAnalytics.PageInteractionType.SearchInPageWasUsed);
            return true;
        }

        public bool SetMatchFilter(int colIndex, string matchString = "", bool update = true, bool forceFocus = false)
        {
            var filtersGotRemoved = false;
            for (int i = m_Filters.filters.Count - 1; i >= 0; i--)
            {
                if (m_Filters.filters[i] is Filter.Match && (m_Filters.filters[i] as Filter.Match).ColumnIndex == colIndex)
                {
                    var matchFilter = (m_Filters.filters[i] as Filter.Match);
                    if (matchFilter.MatchString == matchString && matchFilter.MatchExactly)
                        // nothing to do here
                        return filtersGotRemoved;
                    m_Filters.filters.RemoveAt(i);
                    filtersGotRemoved = true;
                }
            }
            var filter = new Filter.Match(colIndex, matchString, forceFocus, matchExactly: true);
            m_Filters.filters.Insert(0, filter);

            if (update)
            {
                UpdateDisplayTable();
            }
            ReportFilterChanges();
            return true;
        }

        public bool RemoveMatchFilter(int colIndex, bool update = true)
        {
            for (int i = m_Filters.filters.Count - 1; i >= 0; i--)
            {
                if (m_Filters.filters[i] is Filter.Match && (m_Filters.filters[i] as Filter.Match).ColumnIndex == colIndex)
                {
                    m_Filters.filters.RemoveAt(i);
                }
            }

            if (update)
            {
                UpdateDisplayTable();
            }
            ReportFilterChanges();
            return true;
        }

        protected struct FilterParenthood<PFilter, CFilter> where PFilter : Filter.Filter where CFilter : Filter.Filter
        {
            public FilterParenthood(PFilter parent, CFilter child)
            {
                this.parent = parent;
                this.child = child;
            }

            public static implicit operator FilterParenthood<Filter.Filter, Filter.Filter>(FilterParenthood<PFilter, CFilter> a)
            {
                FilterParenthood<Filter.Filter, Filter.Filter> o = new FilterParenthood<Filter.Filter, Filter.Filter>();
                o.parent = a.parent;
                o.child = a.child;
                return o;
            }
            public PFilter parent;
            public CFilter child;
        }

        protected IEnumerable<FilterParenthood<Filter.Filter, Filter.Group>> VisitAllSubGroupFilters(Filter.Filter filter)
        {
            foreach (var f in filter.SubFilters())
            {
                if (f is Filter.Group)
                {
                    Filter.Group gf = (Filter.Group)f;
                    yield return new FilterParenthood<Filter.Filter, Filter.Group>(filter, gf);
                }
                foreach (var f2 in VisitAllSubGroupFilters(f))
                {
                    yield return f2;
                }
            }
        }

        protected FilterParenthood<Filter.Filter, Filter.Group> GetFirstSubGroupFilter(Filter.Filter filter)
        {
            var e = VisitAllSubGroupFilters(filter).GetEnumerator();
            if (e.MoveNext()) return e.Current;
            return new FilterParenthood<Filter.Filter, Filter.Group>();
        }

        protected FilterParenthood<Filter.Filter, Filter.Group> GetDeepestGroupFilter(Filter.Filter filter)
        {
            foreach (var f in filter.SubFilters())
            {
                var sgf = GetDeepestGroupFilter(f);
                if (sgf.child != null) return sgf;

                if (f is Filter.Group)
                {
                    Filter.Group gf = (Filter.Group)f;
                    return new FilterParenthood<Filter.Filter, Filter.Group>(filter, gf);
                }
            }

            return new FilterParenthood<Filter.Filter, Filter.Group>();
        }

        public void OnGui_Filters()
        {
            //GUILayout.Label("Table: " + m_TableDisplay.GetDisplayName());
            GUILayout.Label("Filters:");
            if (m_AllLevelSortFilter.SortLevel.Count == 0 &&
                (m_Filters.filters.Count == 0 ||
                 m_Filters.filters.Count == 1 && m_Filters.filters[0] is DefaultSort && (m_Filters.filters[0] as DefaultSort).SortOverride == null))
            {
                GUILayout.Label("None. Add via column headers.");
            }
            bool dirty = false;

            EditorGUILayout.BeginVertical();

            bool matching = dirty;
            m_Filters.OnGui(m_TableDisplay, ref matching);
            if (matching != dirty)
            {
                SetSelectionAfterFilterChange(matching);
            }
            dirty = matching;

            // hack to prevent the filters from being restored
            // when the last filter is removed it sets dirtyto true and spawns the delayed gui call which updates the filters
            // iff the selected object is a group of one i.e. AudioManager it will restore all the filters
            bool preRemoval = dirty;
            m_AllLevelSortFilter.OnGui(m_TableDisplay, ref dirty);
            EditorGUILayout.EndVertical();

            if (dirty)
            {
                m_WasDirty = true;
                ViewStateFilteringChangedSinceLastSelectionOrViewClose = true;
                if (m_DelayedOnGUICall == null)
                    SetSelectionAfterFilterChange(preRemoval == dirty ? preRemoval : dirty);
            }
            UpdateMatchFilterState();
            GUILayout.FlexibleSpace();
            GUILayout.Label("Count: " +  m_CurrentRowCount.ToString("N0"));
            if (m_CurrentAccumulativeSize > 0)
                GUILayout.Label("Total Size: " + EditorUtility.FormatBytes(m_CurrentAccumulativeSize));
            else if (m_CurrentAccumulativeSizeDeleted > 0 || m_CurrentAccumulativeSizeNew > 0 || m_CurrentAccumulativeSizeSame > 0)
            {
                GUILayout.Label("Total Sizes: " + EditorUtility.FormatBytes(m_CurrentAccumulativeSizeSame) + " (Same) + " +
                    EditorUtility.FormatBytes(m_CurrentAccumulativeSizeNew) + " (New) - " +
                    EditorUtility.FormatBytes(m_CurrentAccumulativeSizeDeleted) + " (Deleted) = " +
                    EditorUtility.FormatBytes(m_CurrentAccumulativeSizeSame + m_CurrentAccumulativeSizeDeleted) + " (old) " +
                    EditorUtility.FormatBytes(m_CurrentAccumulativeSizeSame + m_CurrentAccumulativeSizeNew - m_CurrentAccumulativeSizeDeleted) + " (new) "
                );
            }
        }

        IEnumerator DelayedOnGUICall(float delay = 0.3f, bool updateFilters = true)
        {
            if (m_Listener == null || !m_Listener.IsAlive)
            {
                m_DelayedOnGUICall = null;
                yield break;
            }
            var listener = ((IViewEventListener)m_Listener.Target) as ViewPane;
            CachedSnapshot delayCallSnapshotA = null;
            CachedSnapshot delayCallSnapshotB = null;
            bool wasDiff = false;
            if (listener != null)
            {
                if (listener.m_UIState.CurrentViewMode == UIState.ViewMode.ShowDiff)
                {
                    delayCallSnapshotA = (listener.m_UIState.FirstMode as UIState.SnapshotMode).snapshot;
                    delayCallSnapshotB = (listener.m_UIState.SecondMode as UIState.SnapshotMode).snapshot;
                    wasDiff = true;
                }
                else
                    delayCallSnapshotA = listener.m_UIState.snapshotMode.snapshot;
            }
            listener = null;
            if (delayCallSnapshotA == null || !delayCallSnapshotA.Valid)
            {
                m_DelayedOnGUICall = null;
                yield break;
            }
            yield return new WaitForSeconds(delay);

            if (m_WasDirty)
            {
                m_WasDirty = false;

                // don't delay update if there is no valid snapshot or window
                if (m_Listener == null || !m_Listener.IsAlive || delayCallSnapshotA == null || !delayCallSnapshotA.Valid
                    || (wasDiff && (delayCallSnapshotB == null || !delayCallSnapshotB.Valid)))
                {
                    m_DelayedOnGUICall = null;
                    yield break;
                }
                listener = ((IViewEventListener)m_Listener.Target) as ViewPane;
                if (listener != null)
                {
                    if (listener.m_UIState.CurrentViewMode == UIState.ViewMode.ShowDiff && !wasDiff)
                    {
                        m_DelayedOnGUICall = null;
                        yield break;
                    }
                    if (listener.m_UIState.CurrentViewMode == UIState.ViewMode.ShowDiff)
                    {
                        if (delayCallSnapshotA != (listener.m_UIState.FirstMode as UIState.SnapshotMode).snapshot
                            || delayCallSnapshotB != (listener.m_UIState.SecondMode as UIState.SnapshotMode).snapshot)
                        {
                            m_DelayedOnGUICall = null;
                            yield break;
                        }
                    }
                    else if (delayCallSnapshotA != listener.m_UIState.snapshotMode.snapshot)
                    {
                        m_DelayedOnGUICall = null;
                        yield break;
                    }
                }
                else
                {
                    m_DelayedOnGUICall = null;
                    yield break;
                }
                UpdateDisplayTable(null, updateFilters);
                ReportFilterChanges();

                if (m_Listener != null && m_Listener.IsAlive)
                {
                    ((IViewEventListener)m_Listener.Target).OnRepaint();
                }
                if (updateFilters)
                    UserChangedFilters();
            }
            m_DelayedOnGUICall = null;
        }

        public void SetSelectionAfterFilterChange(bool updateFilters = true)
        {
            m_WasDirty = true;
            m_DelayedOnGUICall = EditorCoroutineUtility.StartCoroutine(DelayedOnGUICall(0.3f, updateFilters), (((IViewEventListener)m_Listener.Target) as ViewPane).m_UIStateHolder as EditorWindow);
        }

        public void SetSelectionAsLatent(bool latent, bool updateFilters = true)
        {
            if (m_GUIState.SelectedRow >= 0 && m_GUIState.SelectionIsLatent != latent)
            {
                m_GUIState.SelectionIsLatent = latent;
                m_WasDirty = true;
                m_DelayedOnGUICall = EditorCoroutineUtility.StartCoroutine(DelayedOnGUICall(0.3f, updateFilters), (((IViewEventListener)m_Listener.Target) as ViewPane).m_UIStateHolder as EditorWindow);
            }
        }

        public void ClearSelection()
        {
            if (m_GUIState.SelectedRow >= 0)
            {
                m_GUIState.SelectedRow = -1;
                m_WasDirty = true;
                EditorCoroutineUtility.StartCoroutine(DelayedOnGUICall(), (((IViewEventListener)m_Listener.Target) as ViewPane).m_UIStateHolder as EditorWindow);
            }
        }

        protected override string GetNoDataReason()
        {
            return m_TableDisplay.NoDataMessage;
        }
    }
}

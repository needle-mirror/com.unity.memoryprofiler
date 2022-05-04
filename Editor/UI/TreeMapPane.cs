#define REMOVE_VIEW_HISTORY
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.Database.View;
using Unity.MemoryProfiler.Editor.UI.Treemap;
using Unity.MemoryProfiler.Editor.UIContentData;
using System.Collections;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class TreeMapPane : ViewPane, IDisposable
    {
        public override string ViewName { get { return TextContent.TreeMapView.text; } }

        UI.Treemap.TreeMapView m_TreeMap;

        UI.DatabaseSpreadsheet m_Spreadsheet;

        string m_CurrentTableTypeFilter;

        CodeType m_CurrentCodeType = CodeType.Unknown;

#if !REMOVE_VIEW_HISTORY
        internal class ViewStateHistory : ViewStateChangedHistoryEvent
        {
            public readonly DatabaseSpreadsheet.State SpreadsheetState;

            public ViewStateHistory(DatabaseSpreadsheet.State spreadsheetState)
            {
                SpreadsheetState = spreadsheetState;
            }

            protected override bool IsEqual(HistoryEvent evt)
            {
                var other = evt as ViewStateHistory;
                return other != null &&
                    SpreadsheetState.Equals(other.SpreadsheetState);
            }
        }

        internal class History : ViewOpenHistoryEvent
        {
            public override ViewStateChangedHistoryEvent ViewStateChangeRestorePoint => m_TableState;
            ViewStateHistory m_TableState;

            public History(TreeMapPane pane)
            {
                Mode = pane.m_UIState.CurrentMode;
                m_TableState = pane.GetViewStateFilteringChangesSinceLastSelectionOrViewClose() as ViewStateHistory;
            }

            public void Restore(TreeMapPane pane, ViewStateChangedHistoryEvent viewStateToRestore = null, SelectionEvent selectionEvent = null, bool selectionIsLatent = false)
            {
                ViewStateHistory viewStateHistory = m_TableState;
                if (viewStateToRestore != null && viewStateToRestore is ViewStateHistory)
                    viewStateHistory = viewStateToRestore as ViewStateHistory;

                if (viewStateHistory != null)
                    pane.m_Spreadsheet.CurrentState = viewStateHistory.SpreadsheetState;

                if (selectionEvent != null)
                {
                    string groupName = "";
                    var metric = default(ObjectMetric);
                    switch (selectionEvent.Selection.Type)
                    {
                        case MemorySampleSelectionType.NativeObject:
                            metric = new ObjectMetric((int)selectionEvent.Selection.ItemIndex, pane.m_UIState.snapshotMode.snapshot, ObjectMetricType.Native);
                            groupName = metric.GetTypeName();
                            break;
                        case MemorySampleSelectionType.ManagedObject:
                            metric = new ObjectMetric((int)selectionEvent.Selection.ItemIndex, pane.m_UIState.snapshotMode.snapshot, ObjectMetricType.Managed);
                            groupName = metric.GetTypeName();
                            break;
                        case MemorySampleSelectionType.NativeType:
                            groupName = pane.m_UIState.snapshotMode.snapshot.NativeTypes.TypeName[selectionEvent.Selection.ItemIndex];
                            break;
                        case MemorySampleSelectionType.ManagedType:
                            groupName = pane.m_UIState.snapshotMode.snapshot.TypeDescriptions.TypeDescriptionName[selectionEvent.Selection.ItemIndex];
                            break;
                        case MemorySampleSelectionType.HighlevelBreakdownElement:
                            break;
                        case MemorySampleSelectionType.UnifiedObject:
                        case MemorySampleSelectionType.Allocation:
                        case MemorySampleSelectionType.AllocationSite:
                        case MemorySampleSelectionType.AllocationCallstack:
                        case MemorySampleSelectionType.NativeRegion:
                        case MemorySampleSelectionType.ManagedRegion:
                        case MemorySampleSelectionType.Allocator:
                        case MemorySampleSelectionType.Label:
                        case MemorySampleSelectionType.Connection:
                        case MemorySampleSelectionType.Symbol:
                        default:
                            throw new NotImplementedException();
                    }

                    var group = pane.m_TreeMap.FindGroup(groupName);
                    if (group != null)
                    {
                        pane.OnClickGroup(group, false);
                        pane.m_ViewStateFilteringChangedSinceLastSelectionOrViewClose = false;
                    }
                    if (!metric.Equals(default(ObjectMetric)))
                    {
                        if (pane.m_TreeMap.HasMetric(metric))
                        {
                            pane.OpenMetricData(metric, true);
                        }
                        else
                        {
                            pane.ShowAllObjects(metric, true);
                        }
                        pane.m_ViewStateFilteringChangedSinceLastSelectionOrViewClose = false;
                    }
                    else
                    {
                        // the event was invalid, ignore it
                        selectionEvent = null;
                    }
                }
                else
                {
                    pane.m_TreeMap.FocusOnAll(true);
                    pane.ShowAllObjects(default(ObjectMetric), false);
                }

                pane.m_EventListener.OnRepaint();
            }

            public override string ToString()
            {
                string name = Mode.GetSchema().GetDisplayName() + seperator + "Tree Map";

                return name;
            }

            protected override bool IsEqual(HistoryEvent evt)
            {
                var hEvt = evt as History;
                if (hEvt == null)
                    return false;

                return evt != null && Mode == hEvt.Mode
                    && (ViewStateChangeRestorePoint != null || ViewStateChangeRestorePoint.Equals(hEvt.ViewStateChangeRestorePoint));
            }
        }
#endif

        public CodeType CurrentCodeType
        {
            set
            {
                if (value == m_CurrentCodeType)
                    return;
                switch (value)
                {
                    case CodeType.Native:
                    case CodeType.Managed:
                        m_CurrentCodeType = value;
                        break;
                    default:
                        if (m_CurrentCodeType == CodeType.Unknown)
                            return;
                        m_CurrentCodeType = CodeType.Unknown;
                        break;
                }
                ShowAllObjects(default(Treemap.ObjectMetric), false);
            }
        }

        string TableName
        {
            get
            {
                switch (m_CurrentCodeType)
                {
                    case CodeType.Native:
                        return ObjectAllNativeTable.TableName;
                    case CodeType.Managed:
                        return ObjectAllManagedTable.TableName;
                    default:
                        return ObjectAllTable.TableName;
                }
            }
        }

        public override VisualElement[] VisualElements
        {
            get
            {
                if (m_VisualElements == null)
                {
                    m_VisualElements = new VisualElement[]
                    {
                        new IMGUIContainer(() => OnGUI(0))
                        {
                            name = "tree-map",
                            style =
                            {
                                flexGrow = 3,
                            }
                        },
                        new IMGUIContainer(() => OnGUI(1))
                        {
                            name = "tree-map__spreadsheet",
                            style =
                            {
                                flexGrow = 1,
                            }
                        }
                    };
                    m_VisualElementsOnGUICalls = new Action<Rect>[]
                    {
                        OnGUI,
                        OnGUISpreadsheet,
                    };
                }
                return m_VisualElements;
            }
        }

#if !REMOVE_VIEW_HISTORY
        public override bool ViewStateFilteringChangedSinceLastSelectionOrViewClose => m_ViewStateFilteringChangedSinceLastSelectionOrViewClose || m_Spreadsheet.ViewStateFilteringChangedSinceLastSelectionOrViewClose;
        bool m_ViewStateFilteringChangedSinceLastSelectionOrViewClose = false;
#endif

        public TreeMapPane(IUIStateHolder s, IViewPaneEventListener l)
            : base(s, l)
        {
            EditorCoroutines.Editor.EditorCoroutineUtility.StartCoroutine(GenerateTreeMap(s), s);
        }

        IEnumerator GenerateTreeMap(IUIStateHolder s)
        {
            MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.GenerateViewEvent>();
            ProgressBarDisplay.ShowBar("Generating Tree Map");
            var treeMap = new UI.Treemap.TreeMapView(s.UIState.snapshotMode.snapshot);
            ProgressBarDisplay.UpdateProgress(0.2f, "Setting up Tree Map");
            yield return null;
            treeMap.Setup();
            treeMap.OnClickItem = OnClickItem;
            treeMap.OnClickGroup = OnClickGroup;
            treeMap.OnOpenItem = OnOpenItem;

            ProgressBarDisplay.UpdateProgress(0.9f, "Finishing up Tree Map");
            yield return null;
            m_TreeMap = treeMap;
            ShowAllObjects(default(Treemap.ObjectMetric), false);
            ProgressBarDisplay.ClearBar();
            MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.GenerateViewEvent() { viewName = "TreeMap" });
        }

        public void ShowAllObjects(Treemap.ObjectMetric itemCopyToSelect, bool focus, ObjectMetricType filter = ObjectMetricType.None)
        {
            // TODO: Fix history zooming UX. Or not. it's always been a bit of a mess.
            focus = false;

            Treemap.ObjectMetric itemToSelect = default(Treemap.ObjectMetric);
            m_TreeMap.ClearMetric();
            if ((filter == ObjectMetricType.None || filter == ObjectMetricType.Managed)
                && (m_CurrentCodeType == CodeType.Unknown || m_CurrentCodeType == CodeType.Managed))
            {
                var managedObjects = m_UIState.snapshotMode.snapshot.CrawledData.ManagedObjects;
                for (int i = 0; i < managedObjects.Count; ++i)
                {
                    var managedObject = managedObjects[i];
                    if (managedObject.Size > 0)
                    {
                        var o = new Treemap.ObjectMetric(managedObject.ManagedObjectIndex, m_UIState.snapshotMode.snapshot, Treemap.ObjectMetricType.Managed);
                        if (o.Equals(itemCopyToSelect))
                        {
                            itemToSelect = o;
                        }
                        m_TreeMap.AddMetric(o);
                    }
                }
            }
            if ((filter == ObjectMetricType.None || filter == ObjectMetricType.Native)
                && (m_CurrentCodeType == CodeType.Unknown || m_CurrentCodeType == CodeType.Native))
            {
                for (int i = 0; i != m_UIState.snapshotMode.snapshot.NativeObjects.Count; ++i)
                {
                    if (m_UIState.snapshotMode.snapshot.NativeObjects.Size[i] > 0)
                    {
                        var o = new Treemap.ObjectMetric(i, m_UIState.snapshotMode.snapshot, Treemap.ObjectMetricType.Native);
                        if (o.Equals(itemCopyToSelect))
                        {
                            itemToSelect = o;
                        }
                        m_TreeMap.AddMetric(o);
                    }
                    else
                    {
                        // Some Native Objects may have no size associated with them and will show up in the table so, report them in the TreeMap too.
                        var INatTypeDesc = m_UIState.snapshotMode.snapshot.NativeObjects.NativeTypeArrayIndex[i];
                        if (INatTypeDesc >= 0)
                        {
                            m_TreeMap.AddEmptyObjectCount(m_UIState.snapshotMode.snapshot.NativeTypes.TypeName[INatTypeDesc], INatTypeDesc);
                        }
                    }
                }
            }
            m_TreeMap.UpdateMetric();

            if (!itemToSelect.Equals(default(Treemap.ObjectMetric)))
                OpenMetricData(itemToSelect, focus);
            else
            {
                try
                {
                    var lr = new Database.LinkRequestTable();
                    lr.LinkToOpen = new Database.TableLink();
                    lr.LinkToOpen.TableName = ObjectAllTable.TableName;
                    lr.SourceTable = null;
                    lr.SourceColumn = null;
                    lr.SourceRow = -1;
                    OpenLinkRequest(lr, false, null, false);
                }
                catch (Exception)
                {
                    throw;
                }
            }
        }

#if !REMOVE_VIEW_HISTORY
        public override UI.ViewOpenHistoryEvent GetOpenHistoryEvent()
        {
            return new History(this);
        }

        public override UI.ViewStateChangedHistoryEvent GetViewStateFilteringChangesSinceLastSelectionOrViewClose()
        {
            m_ViewStateFilteringChangedSinceLastSelectionOrViewClose = false;
            m_Spreadsheet.ViewStateIsCleaned();
            var stateEvent = new ViewStateHistory(m_Spreadsheet.CurrentState);
            stateEvent.ChangeType = ViewStateChangedHistoryEvent.StateChangeType.FiltersChanged;
            return stateEvent;
        }

#endif

        public void OnClickItem(Treemap.Item a, bool record)
        {
            //if (record)
            //{
            //    //m_UIState.AddHistoryEvent(GetCurrentHistoryEvent());
            //}
            m_TreeMap.SelectItem(a);
            long rowIndex;
            OpenMetricData(a.Metric, false, out rowIndex);
            if (record)
            {
                var selection = new MemorySampleSelection(m_UIState, m_Spreadsheet.DisplayTable, rowIndex, a);
                m_UIState.RegisterSelectionChangeEvent(selection);
            }

            OnItemClicked?.Invoke(a.Metric.GetObjectUID());
        }

        public void OnOpenItem(Treemap.Item a, bool record)
        {
            //m_EventListener.OnOpenTable;
        }

        public void OnClickGroup(Treemap.Group a, bool record)
        {
            //if (record)
            //    m_UIState.AddHistoryEvent(GetCurrentHistoryEvent());
            var groupSelected = m_TreeMap.SelectGroup(a, record);

            OpenGroupData(a);

            // If the group wasn't selected, it's because it only contains one item in it, and that was selected directly instead
            // That direct item selection will then already have been recorded as a selection event, so the type selection event should be skipped.
            if (record && groupSelected)
            {
                var selection = new MemorySampleSelection(m_UIState, m_Spreadsheet.DisplayTable, a);
                m_UIState.RegisterSelectionChangeEvent(selection);
            }
        }

        void OpenGroupData(Treemap.Group group)
        {
            if (group.Items[0].Metric.MetricType == Treemap.ObjectMetricType.Native)
                SetTypeFilter(group.Name, ObjectDataType.NativeObject);
            else
            {
                ObjectData objectData = ObjectData.FromManagedObjectIndex(m_UIState.snapshotMode.snapshot, group.Items[0].Metric.ObjectIndex);
                var dataType = objectData.dataType;
                SetTypeFilter(group.Name, dataType);
            }
        }

        bool OpenMetricData(Treemap.ObjectMetric metric, bool focus)
        {
            long rowIndex;
            m_TreeMap.SelectItem(m_TreeMap.GetItemByObjectUID(metric.GetObjectUID()));
            return OpenMetricData(metric, focus, out rowIndex);
        }

        bool OpenMetricData(Treemap.ObjectMetric metric, bool focus, out long rowIndex)
        {
            switch (metric.MetricType)
            {
                case Treemap.ObjectMetricType.Managed:
                    var metricTypeName = metric.GetTypeName();
                    ObjectDataType dataType = ObjectData.FromManagedObjectIndex(m_UIState.snapshotMode.snapshot, metric.ObjectIndex).dataType;

#if !REMOVE_VIEW_HISTORY
                    m_ViewStateFilteringChangedSinceLastSelectionOrViewClose |= SetTypeFilter(metricTypeName, dataType);
#endif
                    if (m_CurrentTableTypeFilter == metricTypeName)
                    {
                        var builder = new Database.View.Where.Builder("Index", Database.Operation.Operator.Equal, new Database.Operation.Expression.MetaExpression(metric.GetObjectUID().ToString(), true));

                        var whereStatement = builder.Build(null, null, null, null, null, m_Spreadsheet.DisplayTable, null); //yeah we could add a no param Build() too..
                        rowIndex = whereStatement.GetFirstMatchIndex(-1);

                        if (rowIndex >= 0)
                        {
                            m_Spreadsheet.Goto(new Database.CellPosition(rowIndex, 0), onlyScrollIfNeeded: true, true, true);
                            return true;
                        }
                    }
                    break;
                case Treemap.ObjectMetricType.Native:

                    metricTypeName = metric.GetTypeName();
                    var instanceId = m_UIState.snapshotMode.snapshot.NativeObjects.InstanceId[metric.ObjectIndex];
#if !REMOVE_VIEW_HISTORY
                    m_ViewStateFilteringChangedSinceLastSelectionOrViewClose |= SetTypeFilter(metricTypeName, ObjectDataType.NativeObject);
#endif
                    if (m_CurrentTableTypeFilter == metricTypeName)
                    {
                        var builder = new Database.View.Where.Builder("NativeInstanceId", Database.Operation.Operator.Equal, new Database.Operation.Expression.MetaExpression(instanceId.ToString(), true));
                        var whereStatement = builder.Build(null, null, null, null, null, m_Spreadsheet.DisplayTable, null); //yeah we could add a no param Build() too..
                        rowIndex = whereStatement.GetFirstMatchIndex(-1);

                        if (rowIndex >= 0)
                        {
                            m_Spreadsheet.Goto(new Database.CellPosition(rowIndex, 0), onlyScrollIfNeeded: true, true, true);
                            return true;
                        }
                    }
                    break;
            }
            rowIndex = -1;
            return false;
        }

        void ClearTypeFilter()
        {
            var dataTypeColumnIndex = m_Spreadsheet.DisplayTable.GetColumnIndexByName("DataType");
            m_Spreadsheet.RemoveMatchFilter(dataTypeColumnIndex);
            var typeColumnIndex = m_Spreadsheet.DisplayTable.GetColumnIndexByName("Type");
            m_Spreadsheet.RemoveMatchFilter(typeColumnIndex, true);
        }

        bool SetTypeFilter(string typeName, ObjectDataType dataType)
        {
            // verifiy and ensure that the right Objects table is shown
            if (m_Spreadsheet.SourceTable.GetName() != TableName)
            {
                try
                {
                    var lr = new Database.LinkRequestTable();
                    lr.LinkToOpen = new Database.TableLink();
                    lr.LinkToOpen.TableName = TableName;
                    lr.SourceTable = null;
                    lr.SourceColumn = null;
                    lr.SourceRow = -1;
                    OpenLinkRequest(lr, false, null, false);
                }
                catch (Exception)
                {
                    throw;
                }
            }
            m_CurrentTableTypeFilter = typeName;
            string typeNameColumnValue = ObjectListObjectTypeColumn.GetTopLevelObjectTypeColumnValue(dataType);

            bool changed = false;
            if (typeNameColumnValue != null)
            {
                var dataTypeColumnIndex = m_Spreadsheet.DisplayTable.GetColumnIndexByName("DataType");
                changed = m_Spreadsheet.SetMatchFilter(dataTypeColumnIndex, typeNameColumnValue, true);
            }

            var typeColumnIndex = m_Spreadsheet.DisplayTable.GetColumnIndexByName("Type");
            if (m_Spreadsheet.SetMatchFilter(typeColumnIndex, typeName, true))
                changed = true;

            var sizeColumn = m_Spreadsheet.DisplayTable.GetColumnIndexByName("Size");
            if (m_Spreadsheet.SetDefaultSortFilter(sizeColumn, Database.Operation.SortOrder.Descending, true))
                changed = true;

#if !REMOVE_VIEW_HISTORY
            m_ViewStateFilteringChangedSinceLastSelectionOrViewClose |= changed;
#endif
            return changed;
        }

        void OpenLinkRequest(Database.LinkRequestTable link, bool focus, string tableTypeFilter = null, bool select = true)
        {
            List<Where.Builder> tableFilterWhere = null;
            m_CurrentTableTypeFilter = tableTypeFilter;
            if (tableTypeFilter != null)
            {
                tableFilterWhere = new List<Where.Builder>();
                tableFilterWhere.Add(new Where.Builder("Type", Database.Operation.Operator.Equal, new Database.Operation.Expression.MetaExpression(tableTypeFilter, true)));
            }

            //TODO this code is the same as the one inSpreadsheetPane, should be put together
            var tableRef = new Database.TableReference(link.LinkToOpen.TableName, link.Parameters);
            var table = m_UIState.snapshotMode.SchemaToDisplay.GetTableByReference(tableRef);
            if (table == null)
            {
                UnityEngine.Debug.LogError("No table named '" + link.LinkToOpen.TableName + "' found.");
                return;
            }
            if (link.LinkToOpen.RowWhere != null && link.LinkToOpen.RowWhere.Count > 0)
            {
                if (table.GetMetaData().defaultFilter != null)
                {
                    table = table.GetMetaData().defaultFilter.CreateFilter(table);
                }
                Database.Operation.ExpressionParsingContext expressionParsingContext = null;
                if (link.SourceView != null)
                {
                    expressionParsingContext = link.SourceView.ExpressionParsingContext;
                }
                if (tableFilterWhere != null && tableFilterWhere.Count > 0)
                {
                    table = FilterTable(table, link.SourceRow, tableFilterWhere);
                }
                var whereUnion = new WhereUnion(link.LinkToOpen.RowWhere, null, null, null, null, m_UIState.snapshotMode.SchemaToDisplay, table, expressionParsingContext);
                long rowToSelect = whereUnion.GetIndexFirstMatch(link.SourceRow);
                if (rowToSelect < 0)
                {
                    Debug.LogError("Could not find entry in target table '" + link.LinkToOpen.TableName + "'");
                    return;
                }
                OpenTable(tableRef, table, new Database.CellPosition(rowToSelect, 0), focus, select);
            }
            else if (tableFilterWhere != null && tableFilterWhere.Count > 0)
            {
                table = FilterTable(table, link.SourceRow, tableFilterWhere);
                OpenTable(tableRef, table, new Database.CellPosition(0, 0), focus, select);
            }
            else
            {
                OpenTable(tableRef, table, new Database.CellPosition(0, 0), focus, select);
            }
        }

        Database.Table FilterTable(Database.Table table, long row, List<Database.View.Where.Builder> tableFilterWhere)
        {
            var tableFilterWhereUnion = new WhereUnion(tableFilterWhere, null, null, null, null, m_UIState.snapshotMode.SchemaToDisplay, table, null);
            var indices = tableFilterWhereUnion.GetMatchingIndices(row);
            return new Database.Operation.IndexedTable(table, new ArrayRange(indices));
        }

        void OnSpreadsheetClick(DatabaseSpreadsheet sheet, Database.LinkRequest link, Database.CellPosition pos)
        {
            //used to ping objects in the hierarchy
            if (link.IsPingLink)
            {
                (link as Database.LinkRequestSceneHierarchy).Ping();
                return;
            }

            var tableLinkRequest = link as Database.LinkRequestTable;
            if (tableLinkRequest != null)
            {
                if (tableLinkRequest.LinkToOpen.TableName == ObjectTable.TableName)
                {
                    // TODO: Remove Object table linking, move all details to Details view.
                    // This currently can't be used properly with selection & view History

                    //open object link in the same pane
                    OpenLinkRequest(tableLinkRequest, true);
                    return;
                }
            }
            else
                Debug.LogWarning("Cannot open unknown link '" + link.ToString() + "'");

            //open the link in the spreadsheet pane
            m_EventListener.OnOpenLink(link);
        }

        void OnRowSelected(long rowIndex)
        {
            var selection = new MemorySampleSelection(m_UIState, m_Spreadsheet.DisplayTable, rowIndex);
            m_UIState.RegisterSelectionChangeEvent(selection);
        }

        public override void SetSelectionFromHistoryEvent(SelectionEvent selectionEvent)
        {
            if (selectionEvent.Selection.Rank == MemorySampleSelectionRank.MainSelection)
            {
                if (selectionEvent.Selection.Type == MemorySampleSelectionType.ManagedType ||
                    selectionEvent.Selection.Type == MemorySampleSelectionType.NativeType)
                {
                    if (selectionEvent.Selection.ItemIndex <= 0)
                        return;

#if !REMOVE_VIEW_HISTORY
                    var lastDirtyState = ViewStateFilteringChangedSinceLastSelectionOrViewClose;
#endif
                    string typeName;
                    var cs = (m_UIState.CurrentMode as UIState.SnapshotMode).snapshot;
                    if (selectionEvent.Selection.Type == MemorySampleSelectionType.ManagedType)
                        typeName = cs.TypeDescriptions.TypeDescriptionName[selectionEvent.Selection.ItemIndex];
                    else
                        typeName = cs.NativeTypes.TypeName[selectionEvent.Selection.ItemIndex];

                    var group = m_TreeMap.FindGroup(typeName);
                    if (group != null)
                        OnClickGroup(group, false);

                    // clear row selection
                    var spreadsheetState = m_Spreadsheet.CurrentState;
                    if (spreadsheetState.SelectedRow >= 0)
                    {
                        spreadsheetState.SelectedRow = -1;
                        spreadsheetState.SelectionIsLatent = false;
                        m_Spreadsheet.CurrentState = spreadsheetState;
                        m_EventListener.OnRepaint();
                    }

#if !REMOVE_VIEW_HISTORY
                    // The History event is not recorded but the view might think it's dirty afterwards,
                    // but these changes where part of the history already, so no need to restore them
                    if (!lastDirtyState && ViewStateFilteringChangedSinceLastSelectionOrViewClose)
                    {
                        m_ViewStateFilteringChangedSinceLastSelectionOrViewClose = false;
                        m_Spreadsheet.ViewStateIsCleaned();
                    }
#endif
                    return;
                }
                else
                {
                    var cs = (m_UIState.CurrentMode as UIState.SnapshotMode).snapshot;
                    string typeName;
                    if (selectionEvent.Selection.Type == MemorySampleSelectionType.ManagedObject)
                        typeName = cs.TypeDescriptions.TypeDescriptionName[selectionEvent.Selection.SecondaryItemIndex];
                    else
                        typeName = cs.NativeTypes.TypeName[selectionEvent.Selection.SecondaryItemIndex];
                    var group = m_TreeMap.FindGroup(typeName);
                    if (group != null && group.Items.Count == 1)
                    {
                        // This wasn't an object selection but the selection of a group with only one item in it, so make sure view filters are restored correctly
                        // Note: may have been a selection made directly via the tables but the likely hood is low and setting the filters in that case won't hurt

                        ObjectDataType dataType;
                        if (selectionEvent.Selection.Type == MemorySampleSelectionType.ManagedObject)
                            dataType = ObjectData.FromManagedObjectIndex(m_UIState.snapshotMode.snapshot, (int)selectionEvent.Selection.ItemIndex).dataType;
                        else
                            dataType = ObjectDataType.NativeObject;
#if !REMOVE_VIEW_HISTORY
                        m_ViewStateFilteringChangedSinceLastSelectionOrViewClose = false;
#endif
                        m_Spreadsheet.ViewStateIsCleaned();
                    }
                    m_Spreadsheet.RestoreSelectedRow(selectionEvent.Selection.FindSelectionInTable(m_UIState, m_Spreadsheet.DisplayTable));
                }
            }
            else
            {
                var currentState = m_Spreadsheet.CurrentState;
                currentState.SelectionIsLatent = true;
                m_Spreadsheet.CurrentState = currentState;
                m_EventListener.OnRepaint();
            }
            long uid = -1;
            switch (selectionEvent.Selection.Type)
            {
                case MemorySampleSelectionType.NativeObject:
                    uid = m_UIState.snapshotMode.snapshot.NativeObjectIndexToUnifiedObjectIndex(selectionEvent.Selection.ItemIndex);
                    break;
                case MemorySampleSelectionType.ManagedObject:
                    uid = m_UIState.snapshotMode.snapshot.ManagedObjectIndexToUnifiedObjectIndex(selectionEvent.Selection.ItemIndex);
                    break;
                default:
                    uid = -1;
                    break;
            }
            SelectObjectByUID(uid, false);
        }

        void OnRowSelectionChanged(long row)
        {
            var objectUID = GetTableObjectUID(m_Spreadsheet.DisplayTable, row);
            if (objectUID >= 0)
            {
                // TODO: Fix history zooming UX. Or not. it's always been a bit of a mess.
                SelectObjectByUID(objectUID, false /*focus*/);
            }
        }

        private void SelectObjectByUID(long objectUID, bool focus)
        {
            var i = m_TreeMap.GetItemByObjectUID(objectUID);
            if (i != null)
            {
                if (focus)
                {
                    m_TreeMap.FocusOnItem(i, true);
                }
                else
                {
                    m_TreeMap.SelectItem(i);
                }
            }
        }

        private long GetTableObjectUID(Database.Table table, long row)
        {
            var indexColBase = table.GetColumnByName("Index");
            var indexColSub = indexColBase;
            while (indexColSub != null && indexColSub is Database.IColumnDecorator)
            {
                indexColSub = (indexColSub as Database.IColumnDecorator).GetBaseColumn();
            }
            if (indexColSub != null && indexColSub is ObjectListUnifiedIndexColumn)
            {
                var indexCol = (Database.ColumnTyped<long>)indexColBase;
                var objectUID = indexCol.GetRowValue(row);
                return objectUID;
            }
            return -1;
        }

        public void OpenTable(Database.TableReference tableRef, Database.Table table, Database.CellPosition pos, bool focus, bool select)
        {
            if (select)
            {
                var objectUID = GetTableObjectUID(table, pos.row);
                if (objectUID >= 0)
                {
                    // TODO: Fix history zooming UX. Or not. it's always been a bit of a mess.
                    SelectObjectByUID(objectUID, false /*focus*/);
                }
            }

            //m_CurrentTableIndex = m_UIState.GetTableIndex(table);
            m_Spreadsheet = new UI.DatabaseSpreadsheet(m_UIState.FormattingOptions, table, this);
            m_Spreadsheet.UserChangedFilters += OnUserChangedSpreadsheetFilters;
            m_Spreadsheet.LinkClicked += OnSpreadsheetClick;
            m_Spreadsheet.RowSelectionChanged += OnRowSelected;
            m_Spreadsheet.RowSelectionChanged += OnRowSelectionChanged;
            m_Spreadsheet.Goto(pos, selectRow: select);
            m_EventListener.OnRepaint();
        }

#if !REMOVE_VIEW_HISTORY
        public void OpenHistoryEvent(History e, ViewStateChangedHistoryEvent viewStateToRestore = null, SelectionEvent selectionEvent = null, bool selectionIsLatent = false)
        {
            if (e == null) return;
            m_EventToOpenNextDraw = new DelayedEventOpeningCache(){
                EventToOpen = e,
                ViewStateToRestore = viewStateToRestore,
                SelectionEvent = selectionEvent,
                SelectionIsLatent = selectionIsLatent
            };
            m_EventListener.OnRepaint();
        }

#endif

        void OnUserChangedSpreadsheetFilters()
        {
            var selection = m_UIState.history.GetLastSelectionEvent(MemorySampleSelectionRank.MainSelection);
            if (selection != null && selection.Selection.Valid && selection.Selection.RowIndex != -1)
            {
                ApplyActiveSelectionAfterOpening(selection);
            }
        }

        public override void ApplyActiveSelectionAfterOpening(SelectionEvent selectionEvent)
        {
            if (m_EventToOpenNextDraw == null)
                m_EventToOpenNextDraw = new DelayedEventOpeningCache();
            m_EventToOpenNextDraw.SelectionEvent = selectionEvent;

            m_EventListener.OnRepaint();
        }

        void OpenHistoryEventImmediate(DelayedEventOpeningCache e)
        {
#if !REMOVE_VIEW_HISTORY
            if (e.EventToOpen != null)
                e.EventToOpen.Restore(this, e.ViewStateToRestore, e.SelectionEvent, e.SelectionIsLatent);
            else
#endif
            m_EventToOpenNextDraw = null;
            if (e.SelectionEvent != null)
                SetSelectionFromHistoryEvent(e.SelectionEvent);
        }

        class DelayedEventOpeningCache
        {
            public History EventToOpen = null;
#if !REMOVE_VIEW_HISTORY
            public ViewStateChangedHistoryEvent ViewStateToRestore = null;
#endif
            public SelectionEvent SelectionEvent = null;
            public bool SelectionIsLatent;
        }
        DelayedEventOpeningCache m_EventToOpenNextDraw = null;

        public Action<long> OnItemClicked;

        void InitializeIfNeeded()
        {
            if (Styles.General == null)
                Styles.Initialize();
        }

        public override void OnGUI(Rect r)
        {
            if (m_TreeMap == null)
                return;
            InitializeIfNeeded();

            if (m_UIState.HotKey.m_CameraFocus.IsTriggered())
            {
                if (m_TreeMap.SelectedItem != null)
                {
                    m_TreeMap.FocusOnItem(m_TreeMap.SelectedItem, false);
                }
            }
            if (m_UIState.HotKey.m_CameraShowAll.IsTriggered())
            {
                if (m_TreeMap.SelectedItem != null)
                {
                    m_TreeMap.FocusOnAll(false);
                }
            }
            m_UIState.FormattingOptions.ObjectDataFormatter.forceLinkAllObject = true;
            r.xMin++;
            r.yMin++;
            r.xMax--;
            r.yMax--;
            m_TreeMap.OnGUI(r);
        }

        void OnGUISpreadsheet(Rect r)
        {
            if (m_TreeMap == null)
                return;

            if (m_Spreadsheet != null)
            {
                GUILayout.BeginArea(r);
                EditorGUILayout.BeginVertical();
                EditorGUILayout.BeginHorizontal();
                m_Spreadsheet.OnGui_Filters();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(2);
                m_Spreadsheet.OnGUI(r.width);
                GUILayout.Space(2);
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(2);
                EditorGUILayout.EndVertical();
                GUILayout.EndArea();
            }

            if (Event.current.type == EventType.Repaint && m_EventToOpenNextDraw != null)
            {
                //this must be done after at least one call of m_TreeMap.OnGUI(rectMap)
                //so that m_TreeMap is initialized with the appropriate rect.
                //otherwise the zoom area will generate NaNs.
                OpenHistoryEventImmediate(m_EventToOpenNextDraw);
                m_EventListener.OnRepaint();
            }
            else if (m_TreeMap != null && m_TreeMap.IsAnimated())
            {
                m_EventListener.OnRepaint();
            }
        }

        public override void OnSelectionChanged(MemorySampleSelection selection)
        {
            if (m_Spreadsheet == null)
                return; // Domain Reload or Serialization/Deserialization related untimely event fired. Ignore it, this view is closed for business.

            if (selection.Rank == MemorySampleSelectionRank.SecondarySelection)
                m_Spreadsheet.SetSelectionAsLatent(true, selection.Table != "Path To Root"); // dont update the filters if the selection is from the paths to root view
            switch (selection.Type)
            {
                case MemorySampleSelectionType.NativeObject:
                case MemorySampleSelectionType.ManagedObject:
                case MemorySampleSelectionType.UnifiedObject:
                case MemorySampleSelectionType.NativeType:
                case MemorySampleSelectionType.ManagedType:
                    // TODO: check that this is the type of item currently shown and if the selection wasn't made in this view, that it is appropriately updated. For now, assume it was made in this view.
                    break;
                case MemorySampleSelectionType.None:
                case MemorySampleSelectionType.Allocation:
                case MemorySampleSelectionType.AllocationSite:
                case MemorySampleSelectionType.Symbol:
                case MemorySampleSelectionType.AllocationCallstack:
                case MemorySampleSelectionType.NativeRegion:
                case MemorySampleSelectionType.ManagedRegion:
                case MemorySampleSelectionType.Allocator:
                case MemorySampleSelectionType.Label:
                case MemorySampleSelectionType.Connection:
                case MemorySampleSelectionType.HighlevelBreakdownElement:
                default:
                    if (selection.Rank == MemorySampleSelectionRank.MainSelection && m_TreeMap != null && (selection.Valid || m_UIStateHolder.UIState.CurrentMode != null))
                    {
                        m_TreeMap.ClearSelection();
                        m_Spreadsheet.ClearSelection();
                        ClearTypeFilter();
                    }
                    break;
            }
        }

        public override void OnClose()
        {
        }

        public void Dispose()
        {
            //Let this be cleaned up by GC later. reconstructing it is just too expensive
            m_TreeMap.CleanupMeshes();
            m_TreeMap = null;
            m_Spreadsheet = null;
        }
    }
}

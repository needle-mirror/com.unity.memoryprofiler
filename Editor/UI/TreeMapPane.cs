using UnityEngine;
#if UNITY_2019_1_OR_NEWER
using UnityEngine.UIElements;
#else
using UnityEngine.Experimental.UIElements;
#endif
using UnityEditor;
using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.Database.View;
using Unity.MemoryProfiler.Editor.UIContentData;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class TreeMapPane : ViewPane
    {
        public override string ViewName { get { return TextContent.TreeMapView.text; } }

        UI.Treemap.TreeMapView m_TreeMap;

        UI.DatabaseSpreadsheet m_Spreadsheet;

        string m_CurrentTableTypeFilter;

        CodeType m_CurrentCodeType = CodeType.Unknown;

        internal class History : HistoryEvent
        {
            string m_GroupName;
            Treemap.ObjectMetric m_SelectedItem;
            DatabaseSpreadsheet.State m_TableState;

            public History(TreeMapPane pane)
            {
                Mode = pane.m_UIState.CurrentMode;
                m_TableState = pane.m_Spreadsheet.CurrentState;

                if (pane.m_TreeMap.SelectedItem != null)
                {
                    m_SelectedItem = pane.m_TreeMap.SelectedItem.Metric;
                    m_GroupName = m_SelectedItem.GetTypeName();
                }
                else if (pane.m_TreeMap.SelectedGroup != null)
                {
                    m_GroupName = pane.m_TreeMap.SelectedGroup.Name;
                }
            }

            public void Restore(TreeMapPane pane)
            {
                if (!m_SelectedItem.Equals(default(Treemap.ObjectMetric)))
                {
                    if (pane.m_TreeMap.HasMetric(m_SelectedItem))
                    {
                        pane.OpenMetricData(m_SelectedItem, true);
                    }
                    else
                    {
                        pane.ShowAllObjects(m_SelectedItem, true);
                    }
                }
                else if (m_GroupName != null)
                {
                    Treemap.Group group = pane.m_TreeMap.FindGroup(m_GroupName);

                    if (group != null)
                    {
                        pane.OnClickGroup(group, false);
                    }
                    else
                    {
                        pane.ShowAllObjects(default(Treemap.ObjectMetric), true);
                    }
                }
                else
                {
                    pane.m_TreeMap.FocusOnAll(true);
                    pane.ShowAllObjects(default(Treemap.ObjectMetric), false);
                }

                pane.m_EventListener.OnRepaint();
                pane.m_Spreadsheet.CurrentState = m_TableState;
            }

            public override string ToString()
            {
                string name = Mode.GetSchema().GetDisplayName() + seperator + "Tree Map";

                if (!m_SelectedItem.Equals(default(Treemap.ObjectMetric)))
                {
                    name += seperator + m_SelectedItem.GetName();
                }

                return name;
            }

            protected override bool IsEqual(HistoryEvent evt)
            {
                var hEvt = evt as History;
                if (hEvt == null)
                    return false;

                return Mode == hEvt.Mode
                    && m_GroupName == hEvt.m_GroupName
                    && m_SelectedItem.Equals(hEvt.m_SelectedItem);
            }
        }

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

        public TreeMapPane(IUIStateHolder s, IViewPaneEventListener l)
            : base(s, l)
        {
            m_TreeMap = new UI.Treemap.TreeMapView(s.UIState.snapshotMode.snapshot);
            m_TreeMap.Setup();
            m_TreeMap.OnClickItem = OnClickItem;
            m_TreeMap.OnClickGroup = OnClickGroup;
            m_TreeMap.OnOpenItem = OnOpenItem;

            ShowAllObjects(default(Treemap.ObjectMetric), false);
        }

        public void ShowAllObjects(Treemap.ObjectMetric itemCopyToSelect, bool focus)
        {
            // TODO: Fix history zooming UX. Or not. it's always been a bit of a mess.
            focus = false;

            Treemap.ObjectMetric itemToSelect = default(Treemap.ObjectMetric);
            m_TreeMap.ClearMetric();
            if (m_CurrentCodeType == CodeType.Unknown || m_CurrentCodeType == CodeType.Managed)
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
            if (m_CurrentCodeType == CodeType.Unknown || m_CurrentCodeType == CodeType.Native)
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
                        if (INatTypeDesc > 0)
                        {
                            m_TreeMap.AddEmptyObjectCount(m_UIState.snapshotMode.snapshot.NativeTypes.TypeName[INatTypeDesc]);
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

        public override UI.HistoryEvent GetCurrentHistoryEvent()
        {
            return new History(this);
        }

        public void OnClickItem(Treemap.Item a, bool record)
        {
            if (record)
                m_UIState.AddHistoryEvent(GetCurrentHistoryEvent());
            m_TreeMap.SelectItem(a);
            OpenMetricData(a.Metric, false);
        }

        public void OnOpenItem(Treemap.Item a, bool record)
        {
            //m_EventListener.OnOpenTable;
        }

        public void OnClickGroup(Treemap.Group a, bool record)
        {
            if (record)
                m_UIState.AddHistoryEvent(GetCurrentHistoryEvent());
            m_TreeMap.SelectGroup(a);
            OpenGroupData(a);
        }

        void OpenGroupData(Treemap.Group group)
        {
            // TODO: Clean up fall back if the SetTypeFilter and GoTo solution suffices / offers the better workflow
            //var lr = new Database.LinkRequestTable();
            //lr.LinkToOpen = new Database.TableLink();
            //lr.LinkToOpen.TableName = ObjectAllTable.TableName;
            //lr.SourceTable = null;
            //lr.SourceColumn = null;
            //lr.SourceRow = -1;
            //OpenLinkRequest(lr, false, group.Name, false);

            if (group.Items[0].Metric.MetricType == Treemap.ObjectMetricType.Native)
                SetTypeFilter(group.Name, ObjectDataType.NativeObject);
            else
            {
                var managedObj = m_UIState.snapshotMode.snapshot.CrawledData.ManagedObjects[group.Items[0].Metric.ObjectIndex];
                ObjectDataType dataType = ObjectDataType.ReferenceObject;


                if (m_UIState.snapshotMode.snapshot.TypeDescriptions.HasFlag(managedObj.ITypeDescription, Format.TypeFlags.kArray))
                    dataType = ObjectDataType.Array;
                else if (m_UIState.snapshotMode.snapshot.TypeDescriptions.HasFlag(managedObj.ITypeDescription, Format.TypeFlags.kValueType))
                    dataType = ObjectDataType.BoxedValue;
                SetTypeFilter(group.Name, dataType);
            }
        }

        void OpenMetricData(Treemap.ObjectMetric metric, bool focus)
        {
            switch (metric.MetricType)
            {
                case Treemap.ObjectMetricType.Managed:
                    var metricTypeName = metric.GetTypeName();
                    var managedObj = m_UIState.snapshotMode.snapshot.CrawledData.ManagedObjects[metric.ObjectIndex];

                    ObjectDataType dataType = ObjectDataType.ReferenceObject;

                    if (m_UIState.snapshotMode.snapshot.TypeDescriptions.HasFlag(managedObj.ITypeDescription, Format.TypeFlags.kArray))
                        dataType = ObjectDataType.Array;
                    else if (m_UIState.snapshotMode.snapshot.TypeDescriptions.HasFlag(managedObj.ITypeDescription, Format.TypeFlags.kValueType))
                        dataType = ObjectDataType.BoxedValue;

                    var filtersChanged = SetTypeFilter(metricTypeName, dataType);

                    if (m_CurrentTableTypeFilter == metricTypeName)
                    {
                        var builder = new Database.View.Where.Builder("Index", Database.Operation.Operator.Equal, new Database.Operation.Expression.MetaExpression(metric.GetObjectUID().ToString(), true));

                        var whereStatement = builder.Build(null, null, null, null, null, m_Spreadsheet.DisplayTable, null); //yeah we could add a no param Build() too..
                        var row = whereStatement.GetFirstMatchIndex(-1);

                        if (row >= 0)
                        {
                            m_Spreadsheet.Goto(new Database.CellPosition(row, 0));
                            return;
                        }
                    }
                    // TODO: Clean up fall back if the SetTypeFilter and GoTo solution suffices / offers the better workflow
                    //Debug.LogWarning("This could shouldn't execute");
                    //var lr = new Database.LinkRequestTable();
                    //lr.LinkToOpen = new Database.TableLink();
                    //lr.LinkToOpen.TableName = TableName;
                    //lr.SourceTable = null;
                    //lr.SourceColumn = null;
                    //lr.SourceRow = -1;
                    //lr.Parameters.AddValue(ObjectTable.ObjParamName, managedObj.PtrObject);
                    //lr.Parameters.AddValue(ObjectTable.TypeParamName, managedObj.ITypeDescription);
                    //OpenLinkRequest(lr, focus, metricTypeName);
                    break;
                case Treemap.ObjectMetricType.Native:

                    metricTypeName = metric.GetTypeName();
                    var instanceId = m_UIState.snapshotMode.snapshot.NativeObjects.InstanceId[metric.ObjectIndex];

                    filtersChanged = SetTypeFilter(metricTypeName, ObjectDataType.NativeObject);

                    if (m_CurrentTableTypeFilter == metricTypeName)
                    {
                        var builder = new Database.View.Where.Builder("NativeInstanceId", Database.Operation.Operator.Equal, new Database.Operation.Expression.MetaExpression(instanceId.ToString(), true));
                        var whereStatement = builder.Build(null, null, null, null, null, m_Spreadsheet.DisplayTable, null); //yeah we could add a no param Build() too..
                        var row = whereStatement.GetFirstMatchIndex(-1);

                        if (row >= 0)
                        {
                            m_Spreadsheet.Goto(new Database.CellPosition(row, 0));
                            return;
                        }
                    }
                    // TODO: Clean up fall back if the SetTypeFilter and GoTo solution suffices / offers the better workflow
                    //Debug.LogWarning("This could shouldn't execute");
                    //lr = new Database.LinkRequestTable();
                    //lr.LinkToOpen = new Database.TableLink();
                    //lr.LinkToOpen.TableName = TableName;
                    //var b = new Database.View.Where.Builder("NativeInstanceId", Database.Operation.Operator.Equal, new Database.Operation.Expression.MetaExpression(instanceId.ToString(), true));
                    //lr.LinkToOpen.RowWhere = new System.Collections.Generic.List<Database.View.Where.Builder>();
                    //lr.LinkToOpen.RowWhere.Add(b);
                    //lr.SourceTable = null;
                    //lr.SourceColumn = null;
                    //lr.SourceRow = -1;
                    //OpenLinkRequest(lr, focus, metricTypeName);
                    break;
            }
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
            string typeNameColumnValue = null;
            switch (dataType)
            {
                case ObjectDataType.Array:
                    typeNameColumnValue = "Managed Array";
                    break;
                case ObjectDataType.BoxedValue:
                    typeNameColumnValue = "Managed Boxed Value";
                    break;
                case ObjectDataType.NativeObject:
                    typeNameColumnValue = "Native Object";
                    break;
                case ObjectDataType.ReferenceObject:
                    typeNameColumnValue = "Managed Object";
                    break;
                case ObjectDataType.Unknown:
                case ObjectDataType.Global:
                case ObjectDataType.Value:
                case ObjectDataType.Object:
                case ObjectDataType.ReferenceArray:
                case ObjectDataType.Type:
                default:
                    break;
            }
            bool changed = false;
            if (typeNameColumnValue != null)
            {
                var dataTypeColumnIndex = m_Spreadsheet.DisplayTable.GetColumnIndexByName("DataType");
                changed = m_Spreadsheet.SetMatchFilter(dataTypeColumnIndex, typeNameColumnValue, false);
            }

            var typeColumnIndex = m_Spreadsheet.DisplayTable.GetColumnIndexByName("Type");
            if (m_Spreadsheet.SetMatchFilter(typeColumnIndex, typeName, true))
                changed = true;
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

            //add current event in history

            m_UIState.AddHistoryEvent(GetCurrentHistoryEvent());
            var tableLinkRequest = link as Database.LinkRequestTable;
            if (tableLinkRequest != null)
            {
                if (tableLinkRequest.LinkToOpen.TableName == ObjectTable.TableName)
                {
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

        void OnRowSelectionChanged(long row)
        {
            var objectUID = GetTableObjectUID(m_Spreadsheet.DisplayTable, row);
            if (objectUID >= 0)
            {
                // TODO: Fix history zooming UX. Or not. it's always been a bit of a mess.
                SelectObjectByUID(objectUID, false /*focus*/);
            }
        }

        private void SelectObjectByUID(int objectUID, bool focus)
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

        private int GetTableObjectUID(Database.Table table, long row)
        {
            var indexColBase = table.GetColumnByName("Index");
            var indexColSub = indexColBase;
            while (indexColSub != null && indexColSub is Database.IColumnDecorator)
            {
                indexColSub = (indexColSub as Database.IColumnDecorator).GetBaseColumn();
            }
            if (indexColSub != null && indexColSub is ObjectListUnifiedIndexColumn)
            {
                var indexCol = (Database.ColumnTyped<int>)indexColBase;
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
            m_Spreadsheet.LinkClicked += OnSpreadsheetClick;
            m_Spreadsheet.RowSelectionChanged += OnRowSelectionChanged;
            m_Spreadsheet.Goto(pos);
            m_EventListener.OnRepaint();
        }

        public void OpenHistoryEvent(History e)
        {
            //m_TreeMap.SelectItem(a);
            //OpenMetricData(a._metric);
            if (e == null) return;
            m_EventToOpenNextDraw = e;
            m_EventListener.OnRepaint();
        }

        public void OpenHistoryEventImmediate(History e)
        {
            e.Restore(this);
        }

        History m_EventToOpenNextDraw = null;

        public override void OnGUI(Rect r)
        {
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
            if (m_Spreadsheet != null)
            {
                GUILayout.BeginArea(r);
                EditorGUILayout.BeginVertical();
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Filters:");

                m_Spreadsheet.OnGui_Filters();
                GUILayout.FlexibleSpace();
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

            if (m_EventToOpenNextDraw != null)
            {
                //this must be done after at least one call of m_TreeMap.OnGUI(rectMap)
                //so that m_TreeMap is initialized with the appropriate rect.
                //otherwise the zoom area will generate NaNs.
                OpenHistoryEventImmediate(m_EventToOpenNextDraw);
                m_EventToOpenNextDraw = null;
                m_EventListener.OnRepaint();
            }
            else if (m_TreeMap != null && m_TreeMap.IsAnimated())
            {
                m_EventListener.OnRepaint();
            }
        }

        public override void OnClose()
        {
            m_TreeMap.CleanupMeshes();
            m_TreeMap = null;
            m_Spreadsheet = null;
        }
    }
}

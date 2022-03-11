using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.Database.Operation
{
    internal interface IDiffColumn
    {
        void Initialize(DiffTable table, Column[] sourceColumn);
    }

    /// <summary>
    /// A column that represent the difference between 2 entries of a DiffTable.
    /// it may represent an entry that is only present in 1 table or both.
    /// </summary>
    /// <typeparam name="DataT"></typeparam>
    internal class DiffColumnTyped<DataT> : ColumnTyped<DataT>, IDiffColumn where DataT : System.IComparable
    {
        protected ColumnTyped<DataT>[] m_SourceColumn;
        protected DiffTable m_Table;

        public DiffColumnTyped()
        {
            type = typeof(DataT);
        }

        void IDiffColumn.Initialize(DiffTable table, Column[] sourceColumn)
        {
            m_Table = table;
            m_SourceColumn = new ColumnTyped<DataT>[sourceColumn.Length];
            for (int i = 0; i < sourceColumn.Length; ++i)
            {
                m_SourceColumn[i] = (ColumnTyped<DataT>)sourceColumn[i];
            }
        }

        public override long GetRowCount()
        {
            return m_Table.GetRowCount();
        }

        public override string GetRowValueString(long row, IDataFormatter formatter)
        {
            int tabI = m_Table.m_Entries[row].tableIndex;
            long rowI = m_Table.m_Entries[row].rowIndex;
            return m_SourceColumn[tabI].GetRowValueString(rowI, formatter);
        }

        public override DataT GetRowValue(long row)
        {
            int tabI = m_Table.m_Entries[row].tableIndex;
            long rowI = m_Table.m_Entries[row].rowIndex;
            return m_SourceColumn[tabI].GetRowValue(rowI);
        }

        public override void SetRowValue(long row, DataT value)
        {
            int tabI = m_Table.m_Entries[row].tableIndex;
            long rowI = m_Table.m_Entries[row].rowIndex;
            m_SourceColumn[tabI].SetRowValue(rowI, value);
        }

        public override LinkRequest GetRowLink(long row)
        {
            int tabI = m_Table.m_Entries[row].tableIndex;
            long rowI = m_Table.m_Entries[row].rowIndex;
            var link = m_SourceColumn[tabI].GetRowLink(rowI);
            if (link == null) return null;
            link.Parameters.AddValue("snapshotindex", tabI);
            return link;
        }
    }

    /// <summary>
    /// A Column that output wheater an entry in a DiffTable is present in the first table only (delete), in the second table only (new) or in both tables (same)
    /// </summary>
    internal class DiffColumnResult : ColumnTyped<DiffTable.DiffResult>
    {
        protected DiffTable m_Table;

        static readonly string k_None = UnityEditor.L10n.Tr("None");
        static readonly string k_Same = UnityEditor.L10n.Tr("Same"); // in both

        public DiffColumnResult(DiffTable table)
        {
            m_Table = table;
        }

        public override long GetRowCount()
        {
            return m_Table.GetRowCount();
        }

        public override string GetRowValueString(long row, IDataFormatter formatter)
        {
            switch (m_Table.m_Entries[row].diffResult)
            {
                case DiffTable.DiffResult.None:
                    return k_None;
                case DiffTable.DiffResult.Deleted:
                    return m_Table.sameSessionDiff ? m_Table.snapshotAIsOlder ? "Deleted in B" : "Deleted in A" : m_Table.snapshotAIsOlder ? "Not in B ('Deleted')" : "Not in A ('Deleted')";
                case DiffTable.DiffResult.New:
                    return m_Table.sameSessionDiff ? m_Table.snapshotAIsOlder ? "New in B" : "New in A" : m_Table.snapshotAIsOlder ? "Not in A ('New')" : "Not in B ('New')";
                case DiffTable.DiffResult.Same:
                    return k_Same;
                default:
                    throw new NotImplementedException();
            }
        }

        public override DiffTable.DiffResult GetRowValue(long row)
        {
            return m_Table.m_Entries[row].diffResult;
        }

        public override void SetRowValue(long row, DiffTable.DiffResult value)
        {
            m_Table.m_Entries[row].diffResult = value;
        }
    }

    /// <summary>
    /// Compares 2 table with identical structure and output each rows which primary key are present in either the first one only, the second one only or in both
    /// </summary>
    internal class DiffTable : Table
    {
        public Table[] sourceTables;
        public enum DiffFilter
        {
            All = 0,
            InFirst = 1,
            InSecond = 2,
            NotInFirst = 4,
            NotInSecond = 8,

            InFirstOnly = InFirst | NotInSecond,
            InSecondOnly = InSecond | NotInFirst,
            InBothOnly = InFirst | InSecond,
        }
        public enum DiffResult
        {
            None = 0,    // Present in none.
            Deleted = 1, // Present in first only
            New = 2,     // Present in second only
            Same = 3,    // Present in both first and second
        }

        public DiffFilter resultFilter
        {
            set
            {
                m_FilterInclude = (int)value & 3;
                m_FilterExclude = (int)value >> 2 & 3;
            }
        }
        private int m_FilterInclude = 0;
        private int m_FilterExclude = 0;
        public int[] columnKey;
        public struct Entry
        {
            public Entry(DiffResult diffResult, int tableIndex, long rowIndex)
            {
                this.diffResult = diffResult;
                this.tableIndex = tableIndex;
                this.rowIndex = rowIndex;
            }

            public DiffResult diffResult;
            public int tableIndex;
            public long rowIndex;
        }
        public Entry[] m_Entries;

        public bool snapshotAIsOlder { get; private set; }
        public bool sameSessionDiff { get; private set; }

        public DiffTable(Schema schema, Table[] table, int[] columnKey, bool snapshotAIsOlder, bool sameSessionDiff)
            : base(schema)
        {
            this.sourceTables = table;
            this.columnKey = columnKey;
            this.snapshotAIsOlder = snapshotAIsOlder;
            this.sameSessionDiff = sameSessionDiff;

            bool hasNoData = true;
            for (int i = 0; i < table.Length; ++i)
            {
                if (table[i].GetRowCount() > 0)
                    hasNoData = false;
            }

            if (hasNoData)
            {
                NoDataMessage = table[0].NoDataMessage;
            }

            var meta = table[0].GetMetaData();
            MetaTable mt = new MetaTable();
            mt.displayName = "Diff " + table[0].GetDisplayName();
            mt.name = "Diff_" + table[0].GetName();
            mt.defaultAllLevelSortFilter = meta.defaultAllLevelSortFilter;
            mt.defaultFilter = meta.defaultFilter;
            MetaColumn[] mc = new MetaColumn[meta.GetColumnCount() + 1];
            var metaType = new MetaType() { scriptingType = typeof(DiffResult), comparisonMethod = DataMatchMethod.AsEnum };
            mc[0] = new MetaColumn("Diff", "Diff", metaType, false, Grouping.groupByDuplicate, null);
            for (int i = 0; i < meta.GetColumnCount(); ++i)
            {
                mc[i + 1] = new MetaColumn(meta.GetColumnByIndex(i));
            }
            mt.SetColumns(mc);
            m_Meta = mt;


            CreateColumn();
        }

        public bool IsResultFilter(DiffResult a)
        {
            //Filter/Result include table
            //    01         10         11
            //00  (00)pass   (00)pass   (00)pass
            //01  (01)pass   (00)fail   (01)pass
            //10  (00)fail   (10)pass   (10)pass
            //11  (01)fail   (10)fail   (11)pass
            var filterInclude = (int)a & m_FilterInclude;
            if (filterInclude != m_FilterInclude) return false;

            //Filter/Result exclude table
            //    01         10         11
            //00  (00)pass   (00)pass   (00)pass
            //01  (01)fail   (00)pass   (01)fail
            //10  (00)pass   (10)fail   (10)fail
            //11  (01)fail   (10)fail   (11)fail
            var filterExclude = (int)a & m_FilterExclude;
            if (filterExclude != 0) return false;

            //all pass
            return true;
        }

        protected void CreateColumn()
        {
            m_Columns = new System.Collections.Generic.List<Column>(m_Meta.GetColumnCount());
            m_Columns.Add(new DiffColumnResult(this));
            for (int i = 1; i != m_Meta.GetColumnCount(); ++i)
            {
                var metaCol = m_Meta.GetColumnByIndex(i);
                IDiffColumn newCol = (IDiffColumn)ColumnCreator.CreateColumn(typeof(DiffColumnTyped<>), metaCol.Type.scriptingType);
                Column[] c = new Column[sourceTables.Length];
                for (int j = 0; j < sourceTables.Length; ++j)
                {
                    c[j] = sourceTables[j].GetColumnByIndex(i - 1);
                }
                newCol.Initialize(this, c);
                m_Columns.Add((Column)newCol);
            }
        }

        public override long GetRowCount()
        {
            if (m_Entries == null) return -1;
            return m_Entries.Length;
        }

        int MultiColumnElementCompare(Column[] lhs, long lhsIdx, long rhsIdx, Expression[] expressions)
        {
            for (int i = 0; i < lhs.Length; ++i)
            {
                int cmp = lhs[i].Compare(lhsIdx, expressions[i], rhsIdx);
                if (cmp != 0)
                    return cmp;
            }

            return 0;
        }

        protected List<Entry> Diff(MetaColumn[] mc, Column[] colA, long[] indexA, Column[] colB, long[] indexB)
        {
            int curA = 0;
            int curB = 0;
            int maxA = indexA.Length;
            int maxB = indexB.Length;
            Expression[] expressions = new Expression[mc.Length];
            for (int i = 0; i < mc.Length; ++i)
            {
                expressions[i] = ColumnCreator.CreateTypedExpressionColumn(mc[i].Type.scriptingType, colB[i]);
            }

            List<Entry> entries = new List<Entry>();
            while (curA < maxA && curB < maxB)
            {
                int r = MultiColumnElementCompare(colA, indexA[curA], indexB[curB], expressions);
                switch (r)
                {
                    case -1:
                        if (IsResultFilter(DiffResult.Deleted))
                        {
                            entries.Add(new Entry(DiffResult.Deleted, 0, indexA[curA]));
                        }

                        ++curA;
                        break;
                    case 0:
                        if (IsResultFilter(DiffResult.Same))
                        {
                            entries.Add(new Entry(DiffResult.Same, 0, indexA[curA]));
                        }
                        ++curA;
                        ++curB;
                        break;
                    case 1:

                        if (IsResultFilter(DiffResult.New))
                        {
                            entries.Add(new Entry(DiffResult.New, 1, indexB[curB]));
                        }
                        ++curB;
                        break;
                    default:
                        throw new Exception("Bad compare result");
                }
            }

            if (IsResultFilter(DiffResult.Deleted))
            {
                while (curA < maxA)
                {
                    // trailing deleted entries
                    entries.Add(new Entry(DiffResult.Deleted, 0, curA));
                    ++curA;
                }
            }
            else
            {
                UnityEngine.Debug.Log("ignored entry");
            }
            if (IsResultFilter(DiffResult.New))
            {
                while (curB < maxB)
                {
                    // trailing new entries
                    entries.Add(new Entry(DiffResult.New, 1, curB));
                    ++curB;
                }
            }
            else
            {
                UnityEngine.Debug.Log("ignored entry");
            }
            return entries;
        }

        public override bool ComputeRowCount()
        {
            return Update();
        }

        public override bool Update()
        {
            if (m_Entries != null) return false;
            sourceTables[0].Update();
            sourceTables[1].Update();
            var mc = sourceTables[0].GetMetaData().GetColumnsByIndex(columnKey);
            var lhsTableCols = sourceTables[0].GetColumnsByIndex(columnKey);
            var lhsTableIndices = lhsTableCols[0].GetSortIndex(SortOrder.Ascending, new ArrayRange(0, lhsTableCols[0].GetRowCount()), false);

            var rhsTableCols = sourceTables[1].GetColumnsByIndex(columnKey);
            var rhsTableIndices = rhsTableCols[0].GetSortIndex(SortOrder.Ascending, new ArrayRange(0, rhsTableCols[0].GetRowCount()), false);

            m_Entries = Diff(mc, lhsTableCols, lhsTableIndices, rhsTableCols, rhsTableIndices).ToArray();
            return true;
        }

        public override Database.CellLink GetLinkTo(CellPosition pos)
        {
            return new LinkPosition(pos);
        }

        public void OnSnapshotsSwapped()
        {
            snapshotAIsOlder = !snapshotAIsOlder;
        }

        public bool TablesAreValid()
        {
            bool valid = true;

            foreach (var table in sourceTables)
            {
                var allTable = table as ObjectAllTable;
                if (allTable != null)
                {
                    if (!allTable.Snapshot.Valid)
                        return false;
                }
            }

            return valid;
        }
    }


    internal class DiffSchema : SchemaAggregate
    {
        public Schema m_SchemaBefore;
        public Schema m_SchemaAfter;
        public DiffSchema(Schema schemaBefore, Schema schemaAfter, bool snapshotAIsOlder, bool sameSessionDiff, Action onTableCompute = null)
        {
            name = "Diff";
            m_SchemaBefore = schemaBefore;
            m_SchemaAfter = schemaAfter;
            if (onTableCompute != null)
                onTableCompute.Invoke();
            ComputeTables(snapshotAIsOlder, sameSessionDiff);
        }

        public override bool OwnsTable(Table table)
        {
            if (table.Schema == this) return true;
            return base.OwnsTable(table);
        }

        protected void ComputeTables(bool snapshotAIsOlder, bool sameSessionDiff)
        {
            for (int iTable = 0; iTable < m_SchemaBefore.GetTableCount(); ++iTable)
            {
                var tabA = m_SchemaBefore.GetTableByIndex(iTable);
                var tabB = m_SchemaAfter.GetTableByName(tabA.GetName());

                if (tabB == null) continue;

                int[] primKey = tabA.GetMetaData().GetPrimaryKeyColumnIndex();

                if (primKey.Length == 0)
                {
                    Debug.LogWarning("Cannot diff tables without primary key. Table '" + tabA.GetName() + "'");
                    continue;
                }
                var tabOut = new Database.Operation.DiffTable(this, new Database.Table[] { tabA, tabB }, primKey, snapshotAIsOlder, sameSessionDiff);
                AddTable(tabOut);
            }
        }

        public override Table GetTableByName(string name, ParameterSet param)
        {
            int snapshotIndex;
            if (param != null && param.TryGetValue("snapshotindex", out snapshotIndex))
            {
                Schema schema;
                switch (snapshotIndex)
                {
                    case 0:
                        schema = m_SchemaBefore;
                        break;
                    case 1:
                        schema = m_SchemaAfter;
                        break;
                    default:
                        Debug.LogError("Requesting a table from an invalid snapshot index. Must be 0 or 1. Is " + snapshotIndex);
                        return null;
                }
                return schema.GetTableByName(name, param);
            }
            return GetTableByName(name);
        }

        public override Table GetTableByReference(TableReference tableRef)
        {
            return GetTableByName(tableRef.Name, tableRef.Param);
        }

        public void OnSnapshotsSwapped()
        {
            for (int i = 0; i < tables.Count; i++)
            {
                (tables[i] as DiffTable).OnSnapshotsSwapped();
            }
        }
    }
}

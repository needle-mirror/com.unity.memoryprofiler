using UnityEngine;

namespace Unity.MemoryProfiler.Editor.Database
{
    class APITable : Table
    {
        public CachedSnapshot m_Snapshot;
        readonly long m_RowCount;
        System.Collections.Generic.List<MetaColumn> m_ListMetaColumns = new System.Collections.Generic.List<MetaColumn>();
        System.Collections.Generic.List<Column> m_ListColumns = new System.Collections.Generic.List<Column>();

        public APITable(Schema schema, CachedSnapshot s, long rowCount)
            : base(schema)
        {
            m_Snapshot = s;
            m_RowCount = rowCount;
        }

        public void AddColumn(MetaColumn mc, Column c)
        {
            m_ListMetaColumns.Add(mc);
            m_ListColumns.Add(c);

            var t1 = c.type;
            var t2 = mc.Type.scriptingType;

            if (!(t1 == t2 || t1.Equals(t2)))
                Debug.LogError("Type of Column must be the same as its MetaColumn.\nColumn: '" + mc.Name + "'");
        }

        public void CreateTable(string nameId, string nameDisplay)
        {
            m_Meta = new MetaTable();
            m_Meta.SetColumns(m_ListMetaColumns.ToArray());
            m_Meta.name = nameId;
            m_Meta.displayName = nameDisplay;

            m_Columns = m_ListColumns;

            m_ListMetaColumns = null;
            m_ListColumns = null;
        }

        public override long GetRowCount()
        {
            return m_RowCount;
        }

        public override CellLink GetLinkTo(CellPosition pos)
        {
            return new LinkPosition(pos);
        }
    }
}

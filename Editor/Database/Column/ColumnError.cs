namespace Unity.MemoryProfiler.Editor.Database
{
    /// <summary>
    /// Column that output "Error" for all its rows
    /// </summary>
    internal class ColumnError : Database.ColumnTyped<string>
    {
#if MEMPROFILER_DEBUG_INFO
        public override string GetDebugString(long row)
        {
            return "ColumnError[" + row + "]";
        }

#endif
        private Table m_Table;
        public ColumnError(Table table)
        {
            m_Table = table;
        }

        public override long GetRowCount()
        {
            return m_Table.GetRowCount();
        }

        public override string GetRowValueString(long row, IDataFormatter formatter)
        {
            return "Error";
        }

        public override string GetRowValue(long row)
        {
            return "Error";
        }

        public override void SetRowValue(long row, string value)
        {
        }

        public override LinkRequest GetRowLink(long row)
        {
            return null;
        }
    }
}

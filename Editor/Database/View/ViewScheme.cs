using System.Xml;
using UnityEngine;
using UnityEditor;
using System.IO;

namespace Unity.MemoryProfiler.Editor.Database.View
{
    internal class ViewSchema : Database.Schema
    {
        public string name = "<unknown>";
        public Database.Schema baseSchema;
        public ViewTable[] tables;
        public System.Collections.Generic.Dictionary<string, ViewTable> tablesByName = new System.Collections.Generic.Dictionary<string, ViewTable>();
        public ViewSchema()
        {
        }

        public override string GetDisplayName()
        {
            return baseSchema.GetDisplayName();
        }

        public override bool OwnsTable(Table table)
        {
            if (table.Schema == this) return true;
            if (System.Array.IndexOf(tables, table) >= 0) return true;
            if (baseSchema != null)
            {
                return baseSchema.OwnsTable(table);
            }
            return false;
        }

        public override long GetTableCount()
        {
            if (baseSchema != null)
            {
                return baseSchema.GetTableCount() + tables.Length;
            }
            return tables.Length;
        }

        public override Table GetTableByIndex(long index)
        {
            if (baseSchema != null && index < baseSchema.GetTableCount())
            {
                return baseSchema.GetTableByIndex(index);
            }
            else
            {
                index -= baseSchema.GetTableCount();
                return tables[index];
            }
        }

        public override Table GetTableByName(string name)
        {
            ViewTable vt;
            if (tablesByName.TryGetValue(name, out vt))
            {
                return vt;
            }
            if (baseSchema != null)
            {
                return baseSchema.GetTableByName(name);
            }
            return null;
        }

        public override Table GetTableByName(string name, ParameterSet param)
        {
            ViewTable vt;
            if (tablesByName.TryGetValue(name, out vt))
            {
                return vt;
            }
            if (baseSchema != null)
            {
                return baseSchema.GetTableByName(name, param);
            }
            return null;
        }

        internal class Builder
        {
            public string name;
            protected System.Collections.Generic.List<ViewTable.Builder> viewTable = new System.Collections.Generic.List<ViewTable.Builder>();

            public ViewSchema Build(Database.Schema baseSchema)
            {
                ViewSchema vs = new ViewSchema();
                vs.baseSchema = baseSchema;
                vs.tables = new ViewTable[viewTable.Count];
                vs.name = name;
                int i = 0;
                foreach (var tBuilder in viewTable)
                {
                    var table = tBuilder.Build(vs, baseSchema);
                    vs.tables[i] = table;
                    vs.tablesByName.Add(table.GetName(), table);
                    ++i;
                }
                return vs;
            }
        }
    }
}

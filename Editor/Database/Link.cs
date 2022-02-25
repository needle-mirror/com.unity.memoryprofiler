using UnityEditor;
using UnityEditor.Networking.PlayerConnection;
using UnityEngine.Diagnostics;
using System.Reflection;
using System;
using System.Linq;
using Unity.MemoryProfiler.Editor.UIContentData;

namespace Unity.MemoryProfiler.Editor.Database
{
    // A request to open a link.
    internal abstract class LinkRequest
    {
        ParameterSet m_Parameters;
        public ParameterSet Parameters
        {
            get
            {
                if (m_Parameters == null) m_Parameters = new ParameterSet();
                return m_Parameters;
            }
        }
        public bool HasParameters
        {
            get
            {
                return m_Parameters != null && m_Parameters.Count > 0;
            }
        }

        public abstract bool IsPingLink { get; }
    }

    // Definition of a link to a table
    internal class TableLink
    {
        public string TableName;
        public System.Collections.Generic.List<View.Where.Builder> RowWhere;
        public System.Collections.Generic.Dictionary<string, string> Parameters;
        public void SetParameter(string name, string value)
        {
            if (Parameters == null)
            {
                Parameters = new System.Collections.Generic.Dictionary<string, string>();
            }
            Parameters.Add(name, value);
        }
    }

    // Request to open a link to a table
    internal class LinkRequestTable : LinkRequest
    {
        public TableLink LinkToOpen;
        public View.ViewTable SourceView;
        public Table SourceTable;
        public Column SourceColumn;
        public long SourceRow;

        public override bool IsPingLink => false;

        public static LinkRequestTable MakeLinkRequest(TableLink metaLink, Table sourceTable, Column sourceColumn, long sourceRow, Database.Operation.ExpressionParsingContext expressionParsingContext)
        {
            if (metaLink == null) return null;
            var lr = new LinkRequestTable();
            lr.LinkToOpen = metaLink;
            lr.SourceTable = sourceTable;
            lr.SourceView = sourceTable as View.ViewTable;
            lr.SourceColumn = sourceColumn;
            lr.SourceRow = sourceRow;

            if (lr.LinkToOpen.Parameters != null)
            {
                foreach (var p in lr.LinkToOpen.Parameters)
                {
                    var opt = new Operation.Expression.ParseIdentifierOption(sourceTable.Schema as View.ViewSchema, sourceTable, true, true, typeof(string), expressionParsingContext);
                    var metaExpression = new Operation.Expression.MetaExpression(p.Value, true);
                    var exp = Operation.Expression.ParseIdentifier(metaExpression, opt);
                    var exp2 = Operation.ColumnCreator.CreateTypedExpressionFixedRow(exp, sourceRow);
                    lr.Parameters.Add(p.Key, exp2);
                }
            }
            return lr;
        }
    }

    internal class LinkRequestSceneHierarchy : LinkRequest
    {
        public override bool IsPingLink => true;
        int instanceId = CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone;
        uint sessionId = uint.MaxValue;
        public static LinkRequestSceneHierarchy MakeLinkRequest(int instanceIdToPing, uint sessionId)
        {
            var lr = new LinkRequestSceneHierarchy();
            lr.instanceId = instanceIdToPing;
            lr.sessionId = sessionId;

            return lr;
        }

        public void Ping()
        {
            EditorAssetFinderUtility.Ping(instanceId, sessionId);
        }
    }
}

using System.Collections.Generic;
using System.Text;
using Unity.MemoryProfiler.Editor.Database;
using Unity.MemoryProfiler.Editor.Extensions.String;

namespace Unity.MemoryProfiler.Editor
{
    internal class ObjectReferenceTable : ObjectListTable
    {
        public static string kObjectReferenceTableName = "ManagedObjectReference";
        //not used //static string k_ObjectReferenceTableDisplayName = "Managed Object Reference";
        //not used //public ManagedConnection[] managedReference;

        ObjectData m_Object;
        ObjectData[] m_References;
        List<int> m_FieldIndicesPath = new List<int>(64);
        StringBuilder m_NamePathBuiler = new StringBuilder(256);

        public ObjectReferenceTable(Database.Schema schema, SnapshotObjectDataFormatter formatter, CachedSnapshot snapshot, ManagedData crawledData, ObjectData obj, ObjectMetaType metaType)
            : base(schema, formatter, snapshot, crawledData, metaType)
        {
            m_Object = obj;
            m_References = ObjectConnection.GetAllObjectConnectingTo(snapshot, obj);
            InitObjectList();
        }

        public override string GetName()
        {
            var str = Formatter.Format(m_Object, DefaultDataFormatter.Instance); //string.Format("0x{0:X16}", ptr);
            return kObjectReferenceTableName + "(" + str + ")";
        }

        public override string GetDisplayName() { return GetName(); }


        public override long GetObjectCount()
        {
            return m_References.LongLength;
        }

        void BuildMemberPath(int typeIndex, int fieldIndexToMatch, List<int> typeList)
        {
            HashSet<int> visitedFields = new HashSet<int>();
            RecurseBuildMemberPath(typeIndex, fieldIndexToMatch, typeList, visitedFields);
        }

        bool RecurseBuildMemberPath(int typeIndex, int fieldIndexToMatch, List<int> typeList, HashSet<int> visited)
        {
            var fields = Snapshot.typeDescriptions.fieldIndices[typeIndex];
            for (int i = 0; i < fields.Length; ++i)
            {
                if (fieldIndexToMatch == fields[i])
                    return true;
            }

            //second pass recurse scan
            for (int i = 0; i < fields.Length; ++i)
            {
                var field = fields[i];

                if (visited.Contains(field))
                    continue;
                else
                    visited.Add(field);


                var fieldTypeIndex = Snapshot.fieldDescriptions.typeIndex[field];
                typeList.Add(field);
                if (!RecurseBuildMemberPath(fieldTypeIndex, fieldIndexToMatch, typeList, visited))
                    typeList.RemoveAt(typeList.Count - 1);
                else
                    return true;
            }

            return false;
        }

        public override string GetObjectName(long row)
        {
            if (m_References[row].isManaged)
            {
                if (m_References[row].m_Parent != null)
                {
                    var typeIndex = m_References[row].m_Parent.obj.managedTypeIndex;
                    var typeName = Snapshot.typeDescriptions.typeDescriptionName[typeIndex];
                    var iField = m_References[row].m_Parent.iField;
                    var arrayIndex = m_References[row].m_Parent.arrayIndex;
                    if (iField >= 0)
                    {
                        var fieldName = Snapshot.fieldDescriptions.fieldDescriptionName[iField];

                        m_FieldIndicesPath.Clear();
                        m_NamePathBuiler.Clear();

                        BuildMemberPath(typeIndex, iField, m_FieldIndicesPath);
                        m_NamePathBuiler.Append(typeName);
                        m_NamePathBuiler.Append('.');
                        for (int i = 0; i < m_FieldIndicesPath.Count; ++i)
                        {
                            m_NamePathBuiler.Append(Snapshot.fieldDescriptions.fieldDescriptionName[m_FieldIndicesPath[i]]);
                            m_NamePathBuiler.Append('.');
                        }
                        m_NamePathBuiler.Append(fieldName);
                        return m_NamePathBuiler.ToString();
                    }
                    else if (arrayIndex >= 0)
                    {
                        if (typeName.EndsWith("[]"))
                        {
                            return typeName.Substring(0, typeName.Length - 2) + "[" + arrayIndex + "]";
                        }
                        else
                        {
                            return typeName + "[" + arrayIndex + "]";
                        }
                    }
                }
            }
            return "[" + row + "]";
        }

        public override ObjectData GetObjectData(long row)
        {
            return m_References[row];
        }

        public override bool GetObjectStatic(long row)
        {
            return false;
        }
    }
}

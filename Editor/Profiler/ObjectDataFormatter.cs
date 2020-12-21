using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.Database;

namespace Unity.MemoryProfiler.Editor
{
    internal class ObjectDataFormatter
    {
        public Dictionary<string, IObjectDataTypeFormatter> m_TypeFormatter = new Dictionary<string, IObjectDataTypeFormatter>();

        // these options should be moved into FormattingOptions
        public bool forceLinkAllObject = false;
        public bool flattenFields = true;
        public bool flattenStaticFields = true;
        public bool ShowPrettyNames
        {
            get
            {
                return m_ShowPrettyNames;
            }
            set
            {
                if (value != m_ShowPrettyNames)
                {
                    m_ShowPrettyNames = value;
                    PrettyNamesOptionChanged();
                }
            }
        }
        bool m_ShowPrettyNames = true;

        public event Action PrettyNamesOptionChanged = delegate {};

        protected void AddTypeFormatter(IObjectDataTypeFormatter Formatter)
        {
            var name = Formatter.GetTypeName();
            m_TypeFormatter.Add(name, Formatter);
        }

        public ObjectDataFormatter()
        {
            AddTypeFormatter(new Int16TypeDisplay());
            AddTypeFormatter(new Int32TypeDisplay());
            AddTypeFormatter(new Int64TypeDisplay());
            AddTypeFormatter(new UInt16TypeDisplay());
            AddTypeFormatter(new UInt32TypeDisplay());
            AddTypeFormatter(new UInt64TypeDisplay());
            AddTypeFormatter(new BooleanTypeDisplay());
            AddTypeFormatter(new CharTypeDisplay());
            AddTypeFormatter(new DoubleTypeDisplay());
            AddTypeFormatter(new SingleTypeDisplay());
            AddTypeFormatter(new StringTypeDisplay());
            AddTypeFormatter(new IntPtrTypeDisplay());
            AddTypeFormatter(new ByteTypeDisplay());
        }

        public void Clear()
        {
            PrettyNamesOptionChanged = delegate {};
        }
    }


    internal class SnapshotObjectDataFormatter
    {
        const string k_NullPtrAddr = "0x0000000000000000";
        const string k_NullRef = "null";
        const string k_ArrayClosedSqBrackets = "[]";

        public ObjectDataFormatter BaseFormatter;

        public Dictionary<int, IObjectDataTypeFormatter> m_TypeFormatter = new Dictionary<int, IObjectDataTypeFormatter>();
        public CachedSnapshot m_Snapshot;
        public bool flattenFields { get { return BaseFormatter.flattenFields; } }
        public bool flattenStaticFields { get { return BaseFormatter.flattenStaticFields; } }
        public bool forceLinkAllObject { get { return BaseFormatter.forceLinkAllObject; } }

        public SnapshotObjectDataFormatter(ObjectDataFormatter baseFormatter, CachedSnapshot d)
        {
            BaseFormatter = baseFormatter;
            m_Snapshot = d;
            foreach (var tr in baseFormatter.m_TypeFormatter)
            {
                int i = m_Snapshot.typeDescriptions.typeDescriptionName.FindIndex(x => x == tr.Key);
                if (i >= 0)
                {
                    m_TypeFormatter[i] = tr.Value;
                }
            }
        }

        public void Clear()
        {
            m_Snapshot = null;
            BaseFormatter.Clear();
        }

        public bool IsExpandable(int iTypeIndex)
        {
            if (iTypeIndex < 0) return false;
            IObjectDataTypeFormatter td;
            if (m_TypeFormatter.TryGetValue(iTypeIndex, out td))
            {
                return td.Expandable();
            }
            return true;
        }

        // Formats "[ptr]" or "null" if ptr == 0
        public string FormatPointer(ulong ptr)
        {
            if (ptr == 0) return k_NullPtrAddr;
            return string.Format("0x{0:x16}", ptr);
        }

        // Formats "[ptr+offset]"
        public string FormatPointerAndOffset(ulong ptr, int offset)
        {
            if (ptr == 0)
                return k_NullRef;

            if (offset >= 0)
            {
                return string.Format("0x{0:x16}+{1:x}", ptr, offset);
            }
            else
            {
                return string.Format("0x{0:x16}-{1:x}", ptr, -offset);
            }
        }

        // Formats "[ptr][index]"
        public string FormatterPointerAndIndex(ulong ptr, int index)
        {
            if (ptr == 0)
                return string.Format("{0}][{1}", k_NullPtrAddr, index);
            return string.Format("0x{0:x16}][{1}", ptr, index);
        }

        // Formats "{field=value, ...}"
        public string FormatObjectBrief(ObjectData od, IDataFormatter formatter, bool objectBrief)
        {
            if (objectBrief)
            {
                string result = "{";
                var iid = od.GetInstanceID(m_Snapshot);
                if (iid != ObjectData.InvalidInstanceID)
                {
                    result += "InstanceID=" + iid;
                }
                int fieldCount = od.GetInstanceFieldCount(m_Snapshot);
                if (fieldCount > 0)
                {
                    if (iid != ObjectData.InvalidInstanceID)
                    {
                        result += ", ";
                    }
                    var field = od.GetInstanceFieldByIndex(m_Snapshot, 0);
                    string k = field.GetFieldName(m_Snapshot);
                    string v = Format(field, formatter, false);
                    if (fieldCount > 1)
                    {
                        return result + k + "=" + v + ", ...}";
                    }
                    else
                    {
                        return result + k + "=" + v + "}";
                    }
                }
                else
                {
                    return result + "}";
                }
            }
            ulong ptr;
            if (od.TryGetObjectPointer(out ptr))
            {
                return FormatPointer(ptr);
            }
            return "{...}";
        }

        public string FormatValueType(ObjectData od, IDataFormatter formatter, bool objectBrief)
        {
            IObjectDataTypeFormatter td;
            if (m_TypeFormatter.TryGetValue(od.managedTypeIndex, out td))
            {
                return td.Format(m_Snapshot, od, formatter);
            }
            return FormatObjectBrief(od, formatter, objectBrief);
        }

        public string FormatObject(ObjectData od, IDataFormatter formatter, bool objectBrief)
        {
            IObjectDataTypeFormatter td;
            if (m_TypeFormatter.TryGetValue(od.managedTypeIndex, out td))
            {
                return td.Format(m_Snapshot, od, formatter);
            }
            return FormatObjectBrief(od, formatter, objectBrief);
        }

        public int CountArrayOfArrays(string typename)
        {
            int count = 0;

            int iter = 0;
            while (true)
            {
                int idxFound = typename.IndexOf(k_ArrayClosedSqBrackets, iter);
                if (idxFound == -1)
                    break;
                ++count;
                iter = idxFound + k_ArrayClosedSqBrackets.Length;
                if (iter >= typename.Length)
                    break;
            }

            return count;
        }

        public string FormatArray(ObjectData od, IDataFormatter formatter)
        {
            IObjectDataTypeFormatter td;
            if (m_TypeFormatter.TryGetValue(od.managedTypeIndex, out td))
            {
                return td.Format(m_Snapshot, od, formatter);
            }

            var originalTypeName = m_Snapshot.typeDescriptions.typeDescriptionName[od.managedTypeIndex];
            var sb = new System.Text.StringBuilder(originalTypeName);


            if (od.hostManagedObjectPtr != 0)
            {
                var arrayInfo = ArrayTools.GetArrayInfo(m_Snapshot, od.managedObjectData, od.managedTypeIndex);
                switch (arrayInfo.rank.Length)
                {
                    case 1:
                        int nestedArrayCount = CountArrayOfArrays(originalTypeName);
                        sb.Replace(k_ArrayClosedSqBrackets, string.Empty);
                        sb.Append('[');
                        sb.Append(arrayInfo.ArrayRankToString());
                        sb.Append(']');
                        for (int i = 1; i < nestedArrayCount; ++i)
                        {
                            sb.Append(k_ArrayClosedSqBrackets);
                        }
                        break;
                    default:
                        sb.Append('[');
                        sb.Append(arrayInfo.ArrayRankToString());
                        sb.Append(']');
                        break;
                }
            }
            return sb.ToString();
        }

        public string Format(ObjectData od, IDataFormatter formatter, bool objectBrief = true)
        {
            switch (od.dataType)
            {
                case ObjectDataType.BoxedValue:
                    return FormatValueType(od.GetBoxedValue(m_Snapshot, true), formatter, objectBrief);
                case ObjectDataType.Value:
                    return FormatValueType(od, formatter, objectBrief);
                case ObjectDataType.Object:
                    return FormatObject(od, formatter, objectBrief);
                case ObjectDataType.Array:
                    return FormatArray(od, formatter);
                case ObjectDataType.ReferenceObject:
                {
                    ulong ptr = od.GetReferencePointer();
                    if (ptr == 0)
                    {
                        return k_NullRef;
                    }
                    else
                    {
                        var o = ObjectData.FromManagedPointer(m_Snapshot, ptr);
                        return FormatObject(o, formatter, objectBrief);
                    }
                }
                case ObjectDataType.ReferenceArray:
                {
                    ulong ptr = od.GetReferencePointer();
                    if (ptr == 0)
                    {
                        return k_NullRef;
                    }
                    var arr = ObjectData.FromManagedPointer(m_Snapshot, ptr);
                    return FormatArray(arr, formatter);
                }
                case ObjectDataType.Type:
                    return m_Snapshot.typeDescriptions.typeDescriptionName[od.managedTypeIndex];
                case ObjectDataType.Global:
                    return "<global>";
                case ObjectDataType.NativeObject:
                    return FormatPointer(m_Snapshot.nativeObjects.nativeObjectAddress[od.nativeObjectIndex]);
                default:
                    return "<uninitialized type>";
            }
        }

        public string FormatInstanceId(CodeType codeType, int iid)
        {
            switch (codeType)
            {
                case CodeType.Managed:
                    return "iid=" + iid + ",M";
                case CodeType.Native:
                    return "iid=" + iid + ",N";
                default:
                    return "iid=" + iid;
            }
        }
    }
}

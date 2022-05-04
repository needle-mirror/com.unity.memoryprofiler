using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor
{
    class DetailFormatter
    {
        const string k_NullPtrAddr = "0x0000000000000000";
        const string k_NullRef = "null";
        const string k_FailedToReadObject = TextContent.InvalidObjectPleaseReportABugMessageShort;
        const string k_ArrayClosedSqBrackets = "[]";

        Dictionary<int, Func<ObjectData, string>> m_TypeFormatter = new Dictionary<int, Func<ObjectData, string>>();
        CachedSnapshot m_Snapshot;
        const string k_InvalidIntPtr = "Invalid IntPtr";

        Dictionary<string, Func<ObjectData, string>> DefaultFormatters()
        {
            return new Dictionary<string, Func<ObjectData, string>>()
            {
                ["System.Int16"] = od => BasicFormat(od.managedObjectData.ReadInt16()),
                ["System.Int32"] = od => BasicFormat(od.managedObjectData.ReadInt32()),
                ["System.Int64"] = od => BasicFormat(od.managedObjectData.ReadInt64()),
                ["System.UInt16"] = od => BasicFormat(od.managedObjectData.ReadUInt16()),
                ["System.UInt32"] = od => BasicFormat(od.managedObjectData.ReadUInt32()),
                ["System.UInt64"] = od => BasicFormat(od.managedObjectData.ReadUInt64()),
                ["System.Boolean"] = od => BasicFormat(od.managedObjectData.ReadBoolean()),
                ["System.Char"] = od => BasicFormat(od.managedObjectData.ReadChar()),
                ["System.Double"] = od => BasicFormat(od.managedObjectData.ReadDouble()),
                ["System.Single"] = od => BasicFormat(od.managedObjectData.ReadSingle()),
                ["System.String"] = FormatString,
                ["System.IntPtr"] = FormatIntPtr,
                ["System.Byte"] = od => BasicFormat(od.managedObjectData.ReadByte())
            };
        }

        public DetailFormatter(CachedSnapshot d)
        {
            m_Snapshot = d;
            foreach (var valueType in DefaultFormatters())
            {
                int i = Array.FindIndex(m_Snapshot.TypeDescriptions.TypeDescriptionName, x => x == valueType.Key);
                if (i >= 0)
                {
                    m_TypeFormatter[i] = valueType.Value;
                }
            }
        }

        string BasicFormat(object obj)
        {
            return obj.ToString();
        }

        string FormatString(ObjectData od)
        {
            if (od.dataIncludeObjectHeader)
            {
                od = od.GetBoxedValue(m_Snapshot, true);
            }
            return BasicFormat(od.managedObjectData.ReadString(out _));
        }

        string FormatIntPtr(ObjectData od)
        {
            return od.managedObjectData.TryReadPointer(out var ptr) != BytesAndOffset.PtrReadError.Success ? k_InvalidIntPtr : FormatPointer(ptr);
        }

        // Formats "[ptr]" or "null" if ptr == 0
        public string FormatPointer(ulong ptr)
        {
            return ptr == 0 ? k_NullPtrAddr : string.Format("0x{0:x16}", ptr);
        }

        // Formats "{field=value, ...}"
        string FormatObjectBrief(ObjectData od, bool objectBrief)
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
                    string v = Format(field, false);
                    if (fieldCount > 1)
                    {
                        return result + k + "=" + v + ", ...}";
                    }

                    return result + k + "=" + v + "}";
                }

                return result + "}";
            }

            return od.TryGetObjectPointer(out var ptr) ? FormatPointer(ptr) : "{...}";
        }

        public string FormatValueType(ObjectData od, bool objectBrief)
        {
            if (m_TypeFormatter.TryGetValue(od.managedTypeIndex, out var td))
            {
                return td.Invoke(od);
            }
            return FormatObjectBrief(od, objectBrief);
        }

        public string FormatObject(ObjectData od, bool objectBrief)
        {
            if (m_TypeFormatter.TryGetValue(od.managedTypeIndex, out var td))
            {
                return td.Invoke(od);
            }
            return FormatObjectBrief(od, objectBrief);
        }

        int CountArrayOfArrays(string typename)
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

        public string FormatArray(ObjectData od)
        {
            if (m_TypeFormatter.TryGetValue(od.managedTypeIndex, out var td))
            {
                return td.Invoke(od);
            }

            var originalTypeName = m_Snapshot.TypeDescriptions.TypeDescriptionName[od.managedTypeIndex];
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

        string Format(ObjectData od, bool objectBrief = true)
        {
            if (!od.IsValid)
                return k_FailedToReadObject;
            switch (od.dataType)
            {
                case ObjectDataType.BoxedValue:
                    return FormatValueType(od.GetBoxedValue(m_Snapshot, true), objectBrief);
                case ObjectDataType.Value:
                    return FormatValueType(od, objectBrief);
                case ObjectDataType.Object:
                    return FormatObject(od, objectBrief);
                case ObjectDataType.Array:
                    return FormatArray(od);
                case ObjectDataType.ReferenceObject:
                {
                    ulong ptr = od.GetReferencePointer();
                    if (ptr == 0)
                    {
                        return k_NullRef;
                    }

                    var o = ObjectData.FromManagedPointer(m_Snapshot, ptr);
                    return !o.IsValid ? k_FailedToReadObject : FormatObject(o, objectBrief);
                }
                case ObjectDataType.ReferenceArray:
                {
                    ulong ptr = od.GetReferencePointer();
                    if (ptr == 0)
                    {
                        return k_NullRef;
                    }
                    var arr = ObjectData.FromManagedPointer(m_Snapshot, ptr);
                    return !arr.IsValid ? k_FailedToReadObject : FormatArray(arr);
                }
                case ObjectDataType.Type:
                    return m_Snapshot.TypeDescriptions.TypeDescriptionName[od.managedTypeIndex];
                case ObjectDataType.NativeObject:
                    return FormatPointer(m_Snapshot.NativeObjects.NativeObjectAddress[od.nativeObjectIndex]);
                default:
                    return "<uninitialized type>";
            }
        }
    }
}

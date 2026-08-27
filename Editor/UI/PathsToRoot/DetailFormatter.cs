using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UI.PathsToRoot;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor
{
    class DetailFormatter
    {
        public const int PointerFormatSignificantDigits = 16;
        public const int PointerNameMinLength = PointerFormatSignificantDigits + 2 /*"0x"*/;
        public static readonly string PointerFormatString = $"0x{{0:X{PointerFormatSignificantDigits}}}";

        const string k_NullPtrAddr = "0x0000000000000000";
        const string k_NullRef = "null";
        static readonly string k_FailedToReadObject = TextContent.InvalidObjectPleaseReportABugMessageShort;

        Dictionary<int, Func<ObjectData, string>> m_TypeFormatter = new Dictionary<int, Func<ObjectData, string>>();
        CachedSnapshot m_Snapshot;
        const string k_InvalidIntPtr = "Invalid IntPtr";

        Dictionary<string, Func<ObjectData, string>> DefaultFormatters()
        {
            return new Dictionary<string, Func<ObjectData, string>>()
            {
                ["System.Int16"] = od => FormatPrimitiveValue(od, bytes => bytes.ReadInt16()),
                ["System.Int32"] = od => FormatPrimitiveValue(od, bytes => bytes.ReadInt32()),
                ["System.Int64"] = od => FormatPrimitiveValue(od, bytes => bytes.ReadInt64()),
                ["System.UInt16"] = od => FormatPrimitiveValue(od, bytes => bytes.ReadUInt16()),
                ["System.UInt32"] = od => FormatPrimitiveValue(od, bytes => bytes.ReadUInt32()),
                ["System.UInt64"] = od => FormatPrimitiveValue(od, bytes => bytes.ReadUInt64()),
                ["System.Boolean"] = od => FormatPrimitiveValue(od, bytes => bytes.ReadBoolean()),
                ["System.Char"] = od => FormatPrimitiveValue(od, bytes => bytes.ReadChar()),
                ["System.Double"] = od => FormatPrimitiveValue(od, bytes => bytes.ReadDouble()),
                ["System.Single"] = od => FormatPrimitiveValue(od, bytes => bytes.ReadSingle()),
                ["System.Byte"] = od => FormatPrimitiveValue(od, bytes => bytes.ReadByte()),
                ["System.String"] = FormatString,
                ["System.IntPtr"] = FormatIntPtr,
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

        string FormatPrimitiveValue<T>(ObjectData od, Func<BytesAndOffset, T> reader)
        {
            // Pass no address as the address should only be deduced from the section index and offset in managedObjectData
            // See ObjectData.GetReferencedPointer for further details of why not to use the hostManagedObjectPtr.
            if (m_Snapshot.TryGetHeapBytesWithFastPath(od.managedObjectData, 0, out var bytes, out var onDemandRefScope))
            {
                using (onDemandRefScope)
                    return BasicFormat(reader(bytes));
            }
            return k_FailedToReadObject;
        }

        string FormatString(ObjectData od)
        {
            // We don't care here if we only get a shortened preview. If it is available, get it.
            if (m_Snapshot.TableEntryNames?.TryGetPreview(od.GetManagedObjectIndex(m_Snapshot), out var preview) ?? false)
                return preview;
            // Otherwise read the first line of the string. (since strings are objects, the hostManagedObjectPtr here is actually the address of the string object and correct to pass in as a fallback)
            if (m_Snapshot.TryGetHeapBytesWithFastPath(od.managedObjectData, od.hostManagedObjectPtr, out var bytes, out var onDemandRefScope))
            {
                using (onDemandRefScope)
                    return StringTools.ReadFirstStringLine(bytes, m_Snapshot.VirtualMachineInformation, false);
            }
            return k_FailedToReadObject;
        }

        string FormatIntPtr(ObjectData od)
        {
            // Pass no address as the address should only be deduced from the section index and offset in managedObjectData
            // See ObjectData.GetReferencedPointer for further details of why not to use the hostManagedObjectPtr.
            if (m_Snapshot.TryGetHeapBytesWithFastPath(od.managedObjectData, 0, out var bytes, out var onDemandRefScope))
            {
                using (onDemandRefScope)
                    return bytes.TryReadPointer(out var ptr) != BytesAndOffset.PtrReadError.Success ? k_InvalidIntPtr : FormatPointer(ptr);
            }
            return k_InvalidIntPtr;
        }

        // Formats "[ptr]" or "null" if ptr == 0
        public static string FormatPointer(ulong ptr)
        {
            return ptr == 0 ? k_NullPtrAddr : string.Format(PointerFormatString, ptr);
        }

        // Formats "{field=value, ...}"
        string FormatObjectBrief(ObjectData od, bool objectBrief, bool truncateTypeNames)
        {
            if (objectBrief)
            {
                string result = "{";
                var iid = od.GetEntityId(m_Snapshot);
                if (iid != ObjectData.InvalidInstanceID)
                {
                    result += "EntityId=" + iid;
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
                    string v = Format(field, false, truncateTypeNames);
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

        public string FormatValueType(ObjectData od, bool objectBrief, bool truncateTypeNames)
        {
            if (m_TypeFormatter.TryGetValue(od.managedTypeIndex, out var td))
            {
                return td.Invoke(od);
            }
            return FormatObjectBrief(od, objectBrief, truncateTypeNames);
        }

        public string FormatObject(ObjectData od, bool objectBrief, bool truncateTypeNames)
        {
            if (m_TypeFormatter.TryGetValue(od.managedTypeIndex, out var td))
            {
                return td.Invoke(od);
            }
            return FormatObjectBrief(od, objectBrief, truncateTypeNames);
        }

        public string FormatArray(ObjectData od)
        {
            if (m_TypeFormatter.TryGetValue(od.managedTypeIndex, out var td))
            {
                return td.Invoke(od);
            }

            return od.GenerateArrayDescription(m_Snapshot);
        }

        string Format(ObjectData od, bool objectBrief = true, bool truncateTypeNames = false)
        {
            if (!od.IsValid)
                return k_FailedToReadObject;
            switch (od.dataType)
            {
                case ObjectDataType.BoxedValue:
                    return FormatValueType(od.GetBoxedValue(m_Snapshot, true), objectBrief, truncateTypeNames);
                case ObjectDataType.Value:
                    return FormatValueType(od, objectBrief, truncateTypeNames);
                case ObjectDataType.Object:
                    return FormatObject(od, objectBrief, truncateTypeNames);
                case ObjectDataType.Array:
                    return FormatArray(od, truncateTypeNames);
                case ObjectDataType.ReferenceObject:
                {
                    ulong ptr = od.GetReferencePointer(m_Snapshot);
                    if (ptr == 0)
                    {
                        return k_NullRef;
                    }
                    var o = ObjectData.FromManagedPointer(m_Snapshot, ptr, od.managedTypeIndex);
                    return !o.IsValid ? k_FailedToReadObject : FormatObject(o, objectBrief, truncateTypeNames);
                }
                case ObjectDataType.ReferenceArray:
                {
                    ulong ptr = od.GetReferencePointer(m_Snapshot);
                    if (ptr == 0)
                    {
                        return k_NullRef;
                    }
                    var arr = ObjectData.FromManagedPointer(m_Snapshot, ptr, od.managedTypeIndex);
                    return !arr.IsValid ? k_FailedToReadObject : FormatArray(arr, truncateTypeNames);
                }
                case ObjectDataType.Type:
                {
                    var typeName = m_Snapshot.TypeDescriptions.TypeDescriptionName[od.managedTypeIndex];
                    if (truncateTypeNames)
                        typeName = PathsToRootDetailView.TruncateTypeName(typeName);
                    return typeName;
                }
                case ObjectDataType.NativeObject:
                    return FormatPointer(m_Snapshot.NativeObjects.NativeObjectAddress[od.nativeObjectIndex]);
                case ObjectDataType.NativeAllocation:
                    return NativeAllocationTools.ProduceNativeAllocationName(od.GetSourceLink(m_Snapshot), m_Snapshot, truncateTypeNames);
                case ObjectDataType.GCHandle:
                    return od.GenerateObjectName(m_Snapshot);
                default:
                    return "<uninitialized type>";
            }
        }

        public string FormatArray(ObjectData od, bool truncateTypeNames)
        {
            if (m_TypeFormatter.TryGetValue(od.managedTypeIndex, out var td))
            {
                return td.Invoke(od);
            }

            return od.GenerateArrayDescription(m_Snapshot, truncateTypeNames);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.MemoryProfiler.Editor.Format;
using UnityEngine;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal enum ObjectDataType
    {
        Unknown,
        Value,
        Object,
        Array,
        BoxedValue,
        ReferenceObject,
        ReferenceArray,
        Type,
        NativeObject,
    }

    internal enum CodeType
    {
        Native,
        Managed,
        Unknown,
        Count,
    }

    internal class ObjectDataParent
    {
        public ObjectData obj;
        public int iField;
        public int arrayIndex;
        public bool expandToTarget;//true means it should display the value/target of the field. False means it should display the owning object
        public ObjectDataParent(ObjectData obj, int iField, int arrayIndex, bool expandToTarget)
        {
            this.obj = obj;
            this.iField = iField;
            this.arrayIndex = arrayIndex;
            this.expandToTarget = expandToTarget;
        }
    }
    internal struct ObjectData
    {
        private void SetManagedType(CachedSnapshot snapshot, int iType)
        {
            m_data.managed.iType = iType;
        }

        public static int InvalidInstanceID
        {
            get
            {
                return CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone;
            }
        }
        private ObjectDataType m_dataType;
        public ObjectDataParent m_Parent;//used for reference object/array and value to hold the owning object.
        public ObjectData displayObject
        {
            get
            {
                if (m_Parent != null && !m_Parent.expandToTarget)
                {
                    return m_Parent.obj;
                }
                return this;
            }
        }
        [StructLayout(LayoutKind.Explicit)]
        public struct Data
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct Managed
            {
                public ulong objectPtr;
                public int iType;
            }
            [StructLayout(LayoutKind.Sequential)]
            public struct Native
            {
                public int index;
            }
            [FieldOffset(0)] public Managed managed;
            [FieldOffset(0)] public Native native;
        }
        private Data m_data;
        public int managedTypeIndex
        {
            get
            {
                switch (m_dataType)
                {
                    case ObjectDataType.Array:
                    case ObjectDataType.BoxedValue:
                    case ObjectDataType.Object:
                    case ObjectDataType.ReferenceArray:
                    case ObjectDataType.ReferenceObject:
                    case ObjectDataType.Value:
                    case ObjectDataType.Type:
                        return m_data.managed.iType;
                }

                return -1;
            }
        }
        public BytesAndOffset managedObjectData;

        public ObjectDataType dataType
        {
            get
            {
                return m_dataType;
            }
        }
        public int nativeObjectIndex
        {
            get
            {
                if (m_dataType == ObjectDataType.NativeObject)
                {
                    return m_data.native.index;
                }
                return -1;
            }
        }
        public ulong hostManagedObjectPtr
        {
            get
            {
                switch (m_dataType)
                {
                    case ObjectDataType.Array:
                    case ObjectDataType.BoxedValue:
                    case ObjectDataType.Object:
                    case ObjectDataType.ReferenceArray:
                    case ObjectDataType.ReferenceObject:
                    case ObjectDataType.Value:
                        return m_data.managed.objectPtr;
                }
                return 0;
            }
        }

        public int fieldIndex
        {
            get
            {
                switch (m_dataType)
                {
                    case ObjectDataType.ReferenceArray:
                    case ObjectDataType.ReferenceObject:
                    case ObjectDataType.Value:
                        if (m_Parent != null)
                        {
                            return m_Parent.iField;
                        }
                        break;
                }
                return -1;
            }
        }
        public int arrayIndex
        {
            get
            {
                switch (m_dataType)
                {
                    case ObjectDataType.ReferenceArray:
                    case ObjectDataType.ReferenceObject:
                    case ObjectDataType.Value:
                        if (m_Parent != null)
                        {
                            return m_Parent.arrayIndex;
                        }
                        break;
                }
                return 0;
            }
        }
        public bool dataIncludeObjectHeader
        {
            get
            {
                switch (m_dataType)
                {
                    case ObjectDataType.Unknown:
                    case ObjectDataType.ReferenceObject:
                    case ObjectDataType.ReferenceArray:
                    case ObjectDataType.Value:
                    case ObjectDataType.Type:
                        return false;
                    case ObjectDataType.Array:
                    case ObjectDataType.Object:
                    case ObjectDataType.BoxedValue:
                        return true;
                }
                throw new Exception("Bad datatype");
            }
        }
        public bool IsValid
        {
            get
            {
                return m_dataType != ObjectDataType.Unknown;//return data.IsValid;
            }
        }

        public ObjectFlags GetFlags(CachedSnapshot cs)
        {
            switch (dataType)
            {
                case ObjectDataType.NativeObject:
                    return cs.NativeObjects.Flags[nativeObjectIndex];
                case ObjectDataType.Unknown:
                case ObjectDataType.Value:
                case ObjectDataType.Object:
                case ObjectDataType.Array:
                case ObjectDataType.BoxedValue:
                case ObjectDataType.ReferenceObject:
                case ObjectDataType.ReferenceArray:
                case ObjectDataType.Type:
                default:
                    return 0;
            }
        }

        public bool HasFields(CachedSnapshot cachedSnapshot)
        {
            return GetInstanceFieldCount(cachedSnapshot) > 0;
        }

        public bool TryGetObjectPointer(out ulong ptr)
        {
            switch (dataType)
            {
                case ObjectDataType.ReferenceArray:
                case ObjectDataType.ReferenceObject:
                case ObjectDataType.Object:
                case ObjectDataType.Array:
                case ObjectDataType.BoxedValue:
                case ObjectDataType.Value:
                    ptr = hostManagedObjectPtr;
                    return true;
                default:
                    ptr = 0;
                    return false;
            }
        }

        public ulong GetObjectPointer(CachedSnapshot snapshot, bool logError = true)
        {
            switch (dataType)
            {
                case ObjectDataType.ReferenceArray:
                case ObjectDataType.ReferenceObject:
                    return GetReferencePointer();
                case ObjectDataType.Object:
                case ObjectDataType.Array:
                case ObjectDataType.BoxedValue:
                    return hostManagedObjectPtr;
                case ObjectDataType.Value:
                    ulong offset = 0;
                    bool isStatic = false;
                    if (IsField())
                    {
                        int fieldIdx = fieldIndex;
                        offset = (ulong)snapshot.FieldDescriptions.Offset[fieldIdx];
                        isStatic = snapshot.FieldDescriptions.IsStatic[fieldIdx] == 1;
                        if (isStatic)
                        {
                            offset = snapshot.TypeDescriptions.TypeInfoAddress[m_Parent.obj.managedTypeIndex];
                            offset += (ulong)snapshot.TypeDescriptions.Size[m_Parent.obj.managedTypeIndex];

                            var staticFieldIndices = snapshot.TypeDescriptions.fieldIndicesStatic[m_Parent.obj.managedTypeIndex];

                            for (int i = 0; i < staticFieldIndices.Length; ++i)
                            {
                                var cFieldIdx = staticFieldIndices[i];
                                if (cFieldIdx == fieldIdx)
                                    break;
                                offset += (ulong)snapshot.FieldDescriptions.Offset[cFieldIdx];
                            }
                        }
                    }
                    else if (arrayIndex >= 0) //compute our offset within the array
                    {
                        offset += (ulong)(snapshot.VirtualMachineInformation.ArrayHeaderSize + arrayIndex * snapshot.TypeDescriptions.Size[managedTypeIndex]);
                    }

                    return isStatic ? offset : hostManagedObjectPtr + offset;
                case ObjectDataType.NativeObject:
                    return snapshot.NativeObjects.NativeObjectAddress[nativeObjectIndex];
                case ObjectDataType.Type:
                    if (m_data.managed.iType >= 0)
                        return snapshot.TypeDescriptions.TypeInfoAddress[m_data.managed.iType];
                    if (logError)
                        UnityEngine.Debug.LogError("Requesting an object pointer on an invalid data type");
                    return 0;
                default:
                    if (logError)
                        UnityEngine.Debug.LogError("Requesting an object pointer on an invalid data type");
                    return 0;
            }
        }

        public ulong GetReferencePointer()
        {
            switch (m_dataType)
            {
                case ObjectDataType.ReferenceObject:
                case ObjectDataType.ReferenceArray:
                    ulong ptr;
                    managedObjectData.TryReadPointer(out ptr);
                    return ptr;
                default:
                    UnityEngine.Debug.LogError("Requesting a reference pointer on an invalid data type");
                    return 0;
            }
        }

        public ObjectData GetBoxedValue(CachedSnapshot snapshot, bool expandToTarget)
        {
            switch (m_dataType)
            {
                case ObjectDataType.Object:
                case ObjectDataType.BoxedValue:
                    break;
                default:
                    UnityEngine.Debug.LogError("Requesting a boxed value on an invalid data type");
                    return Invalid;
            }
            ObjectData od = this;
            od.m_Parent = new ObjectDataParent(this, -1, -1, expandToTarget);
            od.m_dataType = ObjectDataType.Value;
            od.managedObjectData = od.managedObjectData.Add(snapshot.VirtualMachineInformation.ObjectHeaderSize);
            return od;
        }

        public ArrayInfo GetArrayInfo(CachedSnapshot snapshot)
        {
            if (m_dataType != ObjectDataType.Array)
            {
                UnityEngine.Debug.LogError("Requesting an ArrayInfo on an invalid data type");
                return null;
            }
            return ArrayTools.GetArrayInfo(snapshot, managedObjectData, m_data.managed.iType);
        }

        public ObjectData GetArrayElement(CachedSnapshot snapshot, int index, bool expandToTarget)
        {
            return GetArrayElement(snapshot, GetArrayInfo(snapshot), index, expandToTarget);
        }

        public ObjectData GetArrayElement(CachedSnapshot snapshot, ArrayInfo ai, int index, bool expandToTarget)
        {
            switch (m_dataType)
            {
                case ObjectDataType.Array:
                case ObjectDataType.ReferenceArray:
                    break;
                default:
                    Debug.Log("Requesting an array element on an invalid data type");
                    return Invalid;
            }
            ObjectData o = new ObjectData();
            o.m_Parent = new ObjectDataParent(this, -1, index, expandToTarget);
            o.SetManagedType(snapshot, ai.elementTypeDescription);
            o.m_data.managed.objectPtr = m_data.managed.objectPtr;
            o.m_dataType = TypeToSubDataType(snapshot, ai.elementTypeDescription);
            o.managedObjectData = ai.GetArrayElement(index);
            return o;
        }

        public static ObjectDataType TypeToSubDataType(CachedSnapshot snapshot, int iType)
        {
            if (iType < 0)
                return ObjectDataType.Unknown;
            if (snapshot.TypeDescriptions.HasFlag(iType, TypeFlags.kArray))
                return ObjectDataType.ReferenceArray;
            else if (snapshot.TypeDescriptions.HasFlag(iType, TypeFlags.kValueType))
                return ObjectDataType.Value;
            else
                return ObjectDataType.ReferenceObject;
        }

        public static ObjectDataType TypeToDataType(CachedSnapshot snapshot, int iType)
        {
            if (iType < 0)
                return ObjectDataType.Unknown;
            if (snapshot.TypeDescriptions.HasFlag(iType, TypeFlags.kArray))
                return ObjectDataType.Array;
            else if (snapshot.TypeDescriptions.HasFlag(iType, TypeFlags.kValueType))
                return ObjectDataType.BoxedValue;
            else
                return ObjectDataType.Object;
        }

        // ObjectData is pointing to an object's field
        public bool IsField()
        {
            return m_Parent != null && m_Parent.iField >= 0;
        }

        // ObjectData is pointing to an item in an array
        public bool IsArrayItem()
        {
            return m_Parent != null && m_Parent.obj.dataType == ObjectDataType.Array;
        }

        // Returns the name of the field this ObjectData is pointing at.
        // should be called only when IsField() return true
        public string GetFieldName(CachedSnapshot snapshot)
        {
            return snapshot.FieldDescriptions.FieldDescriptionName[m_Parent.iField];
        }

        // Returns the number of fields the object (that this ObjectData is currently pointing at) has
        public int GetInstanceFieldCount(CachedSnapshot snapshot)
        {
            switch (m_dataType)
            {
                case ObjectDataType.Object:
                case ObjectDataType.BoxedValue:
                case ObjectDataType.ReferenceObject:
                case ObjectDataType.Value:
                    if (managedTypeIndex < 0 || managedTypeIndex >= snapshot.TypeDescriptions.FieldIndicesInstance.Length)
                        return 0;
                    return snapshot.TypeDescriptions.FieldIndicesInstance[managedTypeIndex].Length;
                default:
                    return 0;
            }
        }

        // Returns a new ObjectData pointing to the object's (that this ObjectData is currently pointing at) field
        // using the field index from [0, GetInstanceFieldCount()[
        public ObjectData GetInstanceFieldByIndex(CachedSnapshot snapshot, int i)
        {
            int iField = snapshot.TypeDescriptions.FieldIndicesInstance[managedTypeIndex][i];
            return GetInstanceFieldBySnapshotFieldIndex(snapshot, iField, true);
        }

        // Returns a new ObjectData pointing to the object's (that this ObjectData is currently pointing at) field
        // using a field index from snapshot.fieldDescriptions
        public ObjectData GetInstanceFieldBySnapshotFieldIndex(CachedSnapshot snapshot, int iField, bool expandToTarget)
        {
            ObjectData obj;
            ulong objectPtr;

            switch (m_dataType)
            {
                case ObjectDataType.ReferenceObject:
                case ObjectDataType.ReferenceArray:
                    objectPtr = GetReferencePointer();
                    obj = FromManagedPointer(snapshot, objectPtr);
                    break;
                case ObjectDataType.Unknown: //skip unknown/uninitialized types as some snapshots will have them
                    return new ObjectData();
                default:
                    obj = this;
                    objectPtr = m_data.managed.objectPtr;
                    break;
            }

            var fieldOffset = snapshot.FieldDescriptions.Offset[iField];
            var fieldType = snapshot.FieldDescriptions.TypeIndex[iField];
            bool isStatic = snapshot.FieldDescriptions.IsStatic[iField] == 1;
            switch (m_dataType)
            {
                case ObjectDataType.Value:
                    if (!isStatic)
                        fieldOffset -= snapshot.VirtualMachineInformation.ObjectHeaderSize;
                    break;
                case ObjectDataType.Object:
                case ObjectDataType.BoxedValue:
                    break;
                case ObjectDataType.Type:
                    if (!isStatic)
                    {
                        Debug.LogError("Requesting a non-static field on a type");
                        return Invalid;
                    }
                    break;
                default:
                    break;
            }

            ObjectData o = new ObjectData();
            o.m_Parent = new ObjectDataParent(obj, iField, -1, expandToTarget);
            o.SetManagedType(snapshot, fieldType);
            o.m_dataType = TypeToSubDataType(snapshot, fieldType);

            if (isStatic)
            {
                //the field requested might come from a base class. make sure we are using the right staticFieldBytes.
                var iOwningType = obj.m_data.managed.iType;
                while (iOwningType >= 0)
                {
                    var fieldIndex = Array.FindIndex(snapshot.TypeDescriptions.fieldIndicesOwnedStatic[iOwningType], x => x == iField);
                    if (fieldIndex >= 0)
                    {
                        //field iField is owned by type iCurrentBase
                        break;
                    }
                    iOwningType = snapshot.TypeDescriptions.BaseOrElementTypeIndex[iOwningType];
                }
                if (iOwningType < 0)
                {
                    Debug.LogError("Field requested is not owned by the type not any of its bases");
                    return Invalid;
                }

                o.m_data.managed.objectPtr = 0;
                var typeStaticData = new BytesAndOffset(snapshot.TypeDescriptions.StaticFieldBytes[iOwningType], snapshot.VirtualMachineInformation.PointerSize);
                o.managedObjectData = typeStaticData.Add(fieldOffset);
            }
            else
            {
                o.m_data.managed.objectPtr = objectPtr;// m_data.managed.objectPtr;
                o.managedObjectData = obj.managedObjectData.Add(fieldOffset);
            }
            return o;
        }

        public int GetInstanceID(CachedSnapshot snapshot)
        {
            int nativeIndex = nativeObjectIndex;
            if (nativeIndex < 0)
            {
                int managedIndex = GetManagedObjectIndex(snapshot);
                if (managedIndex >= 0)
                {
                    nativeIndex = snapshot.CrawledData.ManagedObjects[managedIndex].NativeObjectIndex;
                }
            }

            if (nativeIndex >= 0)
            {
                return snapshot.NativeObjects.InstanceId[nativeIndex];
            }
            return CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone;
        }

        public ObjectData GetBase(CachedSnapshot snapshot)
        {
            switch (m_dataType)
            {
                case ObjectDataType.ReferenceObject:
                case ObjectDataType.Object:
                case ObjectDataType.Type:
                case ObjectDataType.Value:
                case ObjectDataType.BoxedValue:
                    break;
                default:
                    UnityEngine.Debug.LogError("Requesting a base on an invalid data type");
                    return Invalid;
            }

            var b = snapshot.TypeDescriptions.BaseOrElementTypeIndex[m_data.managed.iType];
            if (b == snapshot.TypeDescriptions.ITypeValueType
                || b == snapshot.TypeDescriptions.ITypeObject
                || b == snapshot.TypeDescriptions.ITypeEnum
                || b == TypeDescriptionEntriesCache.ITypeInvalid)
                return Invalid;

            ObjectData o = this;
            o.SetManagedType(snapshot, b);
            return o;
        }

        public long GetUnifiedObjectIndex(CachedSnapshot snapshot)
        {
            switch (dataType)
            {
                case ObjectDataType.Array:
                case ObjectDataType.Object:
                case ObjectDataType.BoxedValue:
                {
                    int idx;
                    if (snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(m_data.managed.objectPtr, out idx))
                    {
                        return snapshot.ManagedObjectIndexToUnifiedObjectIndex(idx);
                    }
                    break;
                }
                case ObjectDataType.NativeObject:
                    return snapshot.NativeObjectIndexToUnifiedObjectIndex(m_data.native.index);
            }

            return -1;
        }

        public ManagedObjectInfo GetManagedObject(CachedSnapshot snapshot)
        {
            switch (dataType)
            {
                case ObjectDataType.Array:
                case ObjectDataType.Object:
                case ObjectDataType.BoxedValue:
                {
                    int idx;
                    if (snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(m_data.managed.objectPtr, out idx))
                    {
                        return snapshot.CrawledData.ManagedObjects[idx];
                    }
                    throw new Exception("Invalid object pointer used to query object list.");
                }
                case ObjectDataType.ReferenceObject:
                case ObjectDataType.ReferenceArray:
                {
                    int idx;
                    ulong refPtr = GetReferencePointer();
                    if (refPtr == 0)
                        return default(ManagedObjectInfo);
                    if (snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(GetReferencePointer(), out idx))
                    {
                        return snapshot.CrawledData.ManagedObjects[idx];
                    }
                    //do not throw, if the ref pointer is not valid the object might have been null-ed
                    return default(ManagedObjectInfo);
                }
                default:
                    throw new Exception("GetManagedObjectSize was called on a instance of ObjectData which does not contain an managed object.");
            }
        }

        public int GetManagedObjectIndex(CachedSnapshot snapshot)
        {
            switch (dataType)
            {
                case ObjectDataType.Array:
                case ObjectDataType.Object:
                case ObjectDataType.BoxedValue:
                {
                    int idx;
                    if (snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(m_data.managed.objectPtr, out idx))
                    {
                        return idx;
                    }


                    break;
                }
            }

            return -1;
        }

        public int GetNativeObjectIndex(CachedSnapshot snapshot)
        {
            switch (dataType)
            {
                case ObjectDataType.NativeObject:
                    return m_data.native.index;
            }

            return -1;
        }

        private ObjectData(ObjectDataType t)
        {
            m_dataType = t;
            m_data = new Data();
            m_data.managed.objectPtr = 0;
            managedObjectData = new BytesAndOffset();
            m_data.managed.iType = -1;
            m_Parent = null;
        }

        public static ObjectData Invalid
        {
            get
            {
                return new ObjectData();
            }
        }

        public string GetValueAsString(CachedSnapshot cachedSnapshot)
        {
            if (isManaged)
            {
                if (managedObjectData.bytes == null)
                    return "null";

                if (managedTypeIndex == cachedSnapshot.TypeDescriptions.ITypeChar)
                    return managedObjectData.ReadChar().ToString();

                if (managedTypeIndex == cachedSnapshot.TypeDescriptions.ITypeInt16)
                    return managedObjectData.ReadInt16().ToString();

                if (managedTypeIndex == cachedSnapshot.TypeDescriptions.ITypeInt32)
                    return managedObjectData.ReadInt32().ToString();

                if (managedTypeIndex == cachedSnapshot.TypeDescriptions.ITypeInt64)
                    return managedObjectData.ReadInt64().ToString();

                if (managedTypeIndex == cachedSnapshot.TypeDescriptions.ITypeIntPtr)
                    return managedObjectData.ReadUInt64().ToString();

                if (managedTypeIndex == cachedSnapshot.TypeDescriptions.ITypeBool)
                    return managedObjectData.ReadBoolean().ToString();

                if (managedTypeIndex == cachedSnapshot.TypeDescriptions.ITypeSingle)
                    return managedObjectData.ReadSingle().ToString();

                if (managedTypeIndex == cachedSnapshot.TypeDescriptions.ITypeByte)
                    return managedObjectData.ReadByte().ToString();

                if (managedTypeIndex == cachedSnapshot.TypeDescriptions.ITypeDouble)
                    return managedObjectData.ReadDouble().ToString();

                if (managedTypeIndex == cachedSnapshot.TypeDescriptions.ITypeUInt16)
                    return managedObjectData.ReadUInt16().ToString();

                if (managedTypeIndex == cachedSnapshot.TypeDescriptions.ITypeUInt32)
                    return managedObjectData.ReadUInt32().ToString();

                if (managedTypeIndex == cachedSnapshot.TypeDescriptions.ITypeUInt64)
                    return managedObjectData.ReadUInt64().ToString();

                if (managedTypeIndex == cachedSnapshot.TypeDescriptions.ITypeString)
                    return managedObjectData.ReadString(out _);
            }
            return "";
        }

        internal string GetFieldDescription(CachedSnapshot cachedSnapshot)
        {
            string ret = "";
            ret += "Field: " + GetFieldName(cachedSnapshot);
            ret += " of type " + cachedSnapshot.TypeDescriptions.TypeDescriptionName[managedTypeIndex];

            if (nativeObjectIndex != -1)
                return ret + " on object " + cachedSnapshot.NativeObjects.ObjectName[nativeObjectIndex];

            if (dataType == ObjectDataType.ReferenceArray)
            {
                return ret + $" on managed object [0x{hostManagedObjectPtr:x8}]";
            }
            if (GetManagedObject(cachedSnapshot).NativeObjectIndex != -1)
            {
                return ret + " on object " + cachedSnapshot.NativeObjects.ObjectName[GetManagedObject(cachedSnapshot).NativeObjectIndex];
            }

            return ret + $" on managed object [0x{hostManagedObjectPtr:x8}]";
        }

        internal string GenerateArrayDescription(CachedSnapshot cachedSnapshot)
        {
            return $"{cachedSnapshot.TypeDescriptions.TypeDescriptionName[displayObject.managedTypeIndex]}[{arrayIndex}]";
        }

        public string GenerateTypeName(CachedSnapshot cachedSnapshot)
        {
            switch (displayObject.dataType)
            {
                case ObjectDataType.Array:
                case ObjectDataType.BoxedValue:
                case ObjectDataType.Object:
                case ObjectDataType.ReferenceArray:
                case ObjectDataType.Value:
                    return displayObject.managedTypeIndex < 0 ? "<unknown type>" : cachedSnapshot.TypeDescriptions.TypeDescriptionName[displayObject.managedTypeIndex];

                case ObjectDataType.ReferenceObject:
                {
                    var ptr = displayObject.GetReferencePointer();
                    if (ptr != 0)
                    {
                        var obj = ObjectData.FromManagedPointer(cachedSnapshot, ptr);
                        if (obj.IsValid && obj.managedTypeIndex != displayObject.managedTypeIndex)
                        {
                            return $"({cachedSnapshot.TypeDescriptions.TypeDescriptionName[obj.managedTypeIndex]}) {cachedSnapshot.TypeDescriptions.TypeDescriptionName[displayObject.managedTypeIndex]}";
                        }
                    }

                    return cachedSnapshot.TypeDescriptions.TypeDescriptionName[displayObject.managedTypeIndex];
                }

                case ObjectDataType.Type:
                    return "Type";
                case ObjectDataType.NativeObject:
                {
                    int iType = cachedSnapshot.NativeObjects.NativeTypeArrayIndex[displayObject.nativeObjectIndex];
                    return cachedSnapshot.NativeTypes.TypeName[iType];
                }
                case ObjectDataType.Unknown:
                default:
                    return "<unintialized type>";
            }
        }

        public static ObjectData FromManagedType(CachedSnapshot snapshot, int iType)
        {
            ObjectData o = new ObjectData();
            o.SetManagedType(snapshot, iType);
            o.m_dataType = ObjectDataType.Type;
            o.managedObjectData = new BytesAndOffset { bytes = snapshot.TypeDescriptions.StaticFieldBytes[iType], offset = 0, pointerSize = snapshot.VirtualMachineInformation.PointerSize };
            return o;
        }

        //index from an imaginary array composed of native objects followed by managed objects.
        public static ObjectData FromUnifiedObjectIndex(CachedSnapshot snapshot, long index)
        {
            int iNative = snapshot.UnifiedObjectIndexToNativeObjectIndex(index);
            if (iNative >= 0)
            {
                return FromNativeObjectIndex(snapshot, iNative);
            }

            int iManaged = snapshot.UnifiedObjectIndexToManagedObjectIndex(index);
            if (iManaged >= 0)
            {
                return FromManagedObjectIndex(snapshot, iManaged);
            }

            return ObjectData.Invalid;
        }

        public static ObjectData FromNativeObjectIndex(CachedSnapshot snapshot, int index)
        {
            if (index < 0 || index >= snapshot.NativeObjects.Count)
                return ObjectData.Invalid;
            ObjectData o = new ObjectData();
            o.m_dataType = ObjectDataType.NativeObject;
            o.m_data.native.index = index;
            return o;
        }

        public static ObjectData FromManagedObjectInfo(CachedSnapshot snapshot, ManagedObjectInfo moi)
        {
            if (moi.ITypeDescription < 0)
                return ObjectData.Invalid;
            ObjectData o = new ObjectData();
            o.m_dataType = TypeToDataType(snapshot, moi.ITypeDescription);// ObjectDataType.Object;
            o.m_data.managed.objectPtr = moi.PtrObject;
            o.SetManagedType(snapshot, moi.ITypeDescription);
            o.managedObjectData = moi.data;
            return o;
        }

        public static ObjectData FromManagedObjectIndex(CachedSnapshot snapshot, int index)
        {
            if (index < 0 || index >= snapshot.CrawledData.ManagedObjects.Count)
                return ObjectData.Invalid;
            var moi = snapshot.CrawledData.ManagedObjects[index];

            if (index < snapshot.GcHandles.Count)
            {
                //When snapshotting we might end up getting some handle targets as they are about to be collected
                //we do restart the world temporarily this can cause us to end up with targets that are not present in the dumped heaps
                if (moi.PtrObject == 0)
                    return ObjectData.Invalid;

                if (moi.PtrObject != snapshot.GcHandles.Target[index])
                {
                    throw new Exception("bad object");
                }
            }

            return FromManagedObjectInfo(snapshot, moi);
        }

        public static ObjectData FromManagedPointer(CachedSnapshot snapshot, ulong ptr, int asTypeIndex = -1)
        {
            if (ptr == 0)
                return Invalid;
            int idx;
            if (snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(ptr, out idx))
            {
                return FromManagedObjectInfo(snapshot, snapshot.CrawledData.ManagedObjects[idx]);
            }
            else
            {
                ObjectData o = new ObjectData();
                o.m_data.managed.objectPtr = ptr;
                o.managedObjectData = snapshot.ManagedHeapSections.Find(ptr, snapshot.VirtualMachineInformation);
                ManagedObjectInfo info = default(ManagedObjectInfo);
                if (Crawler.TryParseObjectHeader(snapshot, new Crawler.StackCrawlData() { ptr = ptr }, out info, o.managedObjectData))
                {
                    if (asTypeIndex >= 0)
                    {
                        o.SetManagedType(snapshot, asTypeIndex);
                    }
                    else
                    {
                        o.SetManagedType(snapshot, info.ITypeDescription);
                    }

                    o.m_dataType = TypeToDataType(snapshot, info.ITypeDescription);
                    return o;
                }
            }
            return Invalid;
        }

        public bool isNative
        {
            get
            {
                return dataType == ObjectDataType.NativeObject;
            }
        }
        public bool isManaged
        {
            get
            {
                switch (dataType)
                {
                    case ObjectDataType.Value:
                    case ObjectDataType.Object:
                    case ObjectDataType.Array:
                    case ObjectDataType.BoxedValue:
                    case ObjectDataType.ReferenceObject:
                    case ObjectDataType.ReferenceArray:
                    case ObjectDataType.Type:
                        return true;
                }
                return false;
            }
        }
        public CodeType codeType
        {
            get
            {
                switch (dataType)
                {
                    case ObjectDataType.Value:
                    case ObjectDataType.Object:
                    case ObjectDataType.Array:
                    case ObjectDataType.BoxedValue:
                    case ObjectDataType.ReferenceObject:
                    case ObjectDataType.ReferenceArray:
                    case ObjectDataType.Type:
                        return CodeType.Managed;
                    case ObjectDataType.NativeObject:
                        return CodeType.Native;
                    default:
                        return CodeType.Unknown;
                }
            }
        }

        public bool IsGameObject(CachedSnapshot cs)
        {
            return cs.NativeObjects.NativeTypeArrayIndex[nativeObjectIndex] == cs.NativeTypes.GameObjectIdx;
        }

        public bool IsTransform(CachedSnapshot cs)
        {
            var id = cs.NativeObjects.NativeTypeArrayIndex[nativeObjectIndex];
            if (id == cs.NativeTypes.TransformIdx || id == cs.NativeTypes.GameObjectIdx)
                return cs.NativeObjects.NativeTypeArrayIndex[nativeObjectIndex] == cs.NativeTypes.TransformIdx;
            return false;
        }

        public bool IsRootTransform(CachedSnapshot cs)
        {
            return cs != null && cs.HasSceneRootsAndAssetbundles && cs.SceneRoots.RootTransformInstanceIdHashSet.Contains(GetInstanceID(cs));
        }

        public bool IsRootGameObject(CachedSnapshot cs)
        {
            return cs != null && cs.HasSceneRootsAndAssetbundles && cs.SceneRoots.RootGameObjectInstanceIdHashSet.Contains(GetInstanceID(cs));
        }

        public string GetAssetPath(CachedSnapshot cs)
        {
            for (int i = 0; i < cs.SceneRoots.SceneIndexedRootTransformInstanceIds.Length; i++)
            {
                for (int ii = 0; ii < cs.SceneRoots.SceneIndexedRootTransformInstanceIds[i].Length; ii++)
                {
                    if (cs.SceneRoots.SceneIndexedRootTransformInstanceIds[i][ii].Equals(GetInstanceID(cs)))
                        return cs.SceneRoots.Path[i];
                }
            }
            return String.Empty;
        }

        public ObjectData[] GetAllReferencingObjects(CachedSnapshot cs)
        {
            return ObjectConnection.GetAllReferencingObjects(cs, displayObject);
        }

        public ObjectData[] GetAllReferencedObjects(CachedSnapshot cs)
        {
            return ObjectConnection.GetAllReferencedObjects(cs, displayObject);
        }

        public bool InvalidType()
        {
            return displayObject.dataType == ObjectDataType.Unknown;
        }

        public bool IsUnknownDataType()
        {
            return displayObject.dataType == ObjectDataType.Unknown;
        }
    }

    internal struct ObjectConnection
    {
        public static ObjectData[] GetAllReferencingObjects(CachedSnapshot snapshot, ObjectData obj)
        {
            var referencingObjects = new List<ObjectData>();
            long objIndex = -1;
            switch (obj.dataType)
            {
                case ObjectDataType.Array:
                case ObjectDataType.BoxedValue:
                case ObjectDataType.Object:
                {
                    if (snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(obj.hostManagedObjectPtr, out var idx))
                    {
                        objIndex = snapshot.ManagedObjectIndexToUnifiedObjectIndex(idx);
                        if (!snapshot.CrawledData.ConnectionsToMappedToUnifiedIndex.TryGetValue(objIndex, out var connectionIndicies))
                            break;

                        //add crawled connections
                        foreach (var i in connectionIndicies)
                        {
                            var c = snapshot.CrawledData.Connections[i];
                            switch (c.connectionType)
                            {
                                case ManagedConnection.ConnectionType.ManagedObject_To_ManagedObject:

                                    var objParent = ObjectData.FromManagedObjectIndex(snapshot, c.fromManagedObjectIndex);
                                    if (c.fieldFrom >= 0)
                                    {
                                        referencingObjects.Add(objParent.GetInstanceFieldBySnapshotFieldIndex(snapshot, c.fieldFrom, false));
                                    }
                                    else if (c.arrayIndexFrom >= 0)
                                    {
                                        referencingObjects.Add(objParent.GetArrayElement(snapshot, c.arrayIndexFrom, false));
                                    }
                                    else
                                    {
                                        referencingObjects.Add(objParent);
                                    }

                                    break;
                                case ManagedConnection.ConnectionType.ManagedType_To_ManagedObject:

                                    var objType = ObjectData.FromManagedType(snapshot, c.fromManagedType);
                                    if (c.fieldFrom >= 0)
                                    {
                                        referencingObjects.Add(objType.GetInstanceFieldBySnapshotFieldIndex(snapshot, c.fieldFrom, false));
                                    }
                                    else if (c.arrayIndexFrom >= 0)
                                    {
                                        referencingObjects.Add(objType.GetArrayElement(snapshot, c.arrayIndexFrom, false));
                                    }
                                    else
                                    {
                                        referencingObjects.Add(objType);
                                    }

                                    break;
                                case ManagedConnection.ConnectionType.UnityEngineObject:
                                    // these get at added in the loop at the end of the function
                                    // tried using a hash set to prevent duplicates but the lookup during add locks up the window
                                    // if there are more than about 50k references
                                    //referencingObjects.Add(ObjectData.FromNativeObjectIndex(snapshot, c.UnityEngineNativeObjectIndex));
                                    break;
                            }
                        }
                    }
                    break;
                }
                case ObjectDataType.NativeObject:
                    objIndex = snapshot.NativeObjectIndexToUnifiedObjectIndex(obj.nativeObjectIndex);
                    if (!snapshot.CrawledData.ConnectionsMappedToNativeIndex.TryGetValue(obj.nativeObjectIndex, out var connectionIndices))
                        break;

                    //add crawled connection
                    foreach (var i in connectionIndices)
                    {
                        switch (snapshot.CrawledData.Connections[i].connectionType)
                        {
                            case ManagedConnection.ConnectionType.ManagedObject_To_ManagedObject:
                            case ManagedConnection.ConnectionType.ManagedType_To_ManagedObject:
                                break;
                            case ManagedConnection.ConnectionType.UnityEngineObject:
                                referencingObjects.Add(ObjectData.FromManagedObjectIndex(snapshot, snapshot.CrawledData.Connections[i].UnityEngineManagedObjectIndex));
                                break;
                        }
                    }
                    break;
            }
            //add connections from the raw snapshot
            if (objIndex >= 0 && snapshot.Connections.ToFromMappedConnection.ContainsKey((int)objIndex))
            {
                foreach (var i in snapshot.Connections.ToFromMappedConnection[(int)objIndex])
                {
                    referencingObjects.Add(ObjectData.FromUnifiedObjectIndex(snapshot, i));
                }
            }

            return referencingObjects.ToArray();
        }

        public static int[] GetConnectedTransformInstanceIdsFromTransformInstanceId(CachedSnapshot snapshot, int instanceID)
        {
            HashSet<int> found = new HashSet<int>();
            var objectData = ObjectData.FromNativeObjectIndex(snapshot, snapshot.NativeObjects.instanceId2Index[instanceID]);
            if (snapshot.Connections.FromToMappedConnection.ContainsKey((int)objectData.GetUnifiedObjectIndex(snapshot)))
            {
                var list = snapshot.Connections.FromToMappedConnection[(int)objectData.GetUnifiedObjectIndex(snapshot)];
                foreach (var connection in list)
                {
                    objectData = ObjectData.FromUnifiedObjectIndex(snapshot, connection);
                    if (objectData.isNative && snapshot.NativeTypes.TransformIdx == snapshot.NativeObjects.NativeTypeArrayIndex[objectData.nativeObjectIndex])
                        found.Add(snapshot.NativeObjects.InstanceId[objectData.nativeObjectIndex]);
                }
            }

            int[] returnedObjectData = new int[found.Count];
            found.CopyTo(returnedObjectData);
            return returnedObjectData;
        }

        public static int GetGameObjectInstanceIdFromTransformInstanceId(CachedSnapshot snapshot, int instanceID)
        {
            var objectData = ObjectData.FromNativeObjectIndex(snapshot, snapshot.NativeObjects.instanceId2Index[instanceID]);
            if (snapshot.Connections.FromToMappedConnection.ContainsKey((int)objectData.GetUnifiedObjectIndex(snapshot)))
            {
                var list = snapshot.Connections.FromToMappedConnection[(int)objectData.GetUnifiedObjectIndex(snapshot)];
                foreach (var connection in list)
                {
                    objectData = ObjectData.FromUnifiedObjectIndex(snapshot, connection);
                    if (objectData.isNative && objectData.IsGameObject(snapshot) && snapshot.NativeObjects.ObjectName[objectData.nativeObjectIndex] == snapshot.NativeObjects.ObjectName[ObjectData.FromUnifiedObjectIndex(snapshot, connection).nativeObjectIndex])
                        return snapshot.NativeObjects.InstanceId[objectData.nativeObjectIndex];
                }
            }
            return -1;
        }

        public static ObjectData[] GenerateReferencesTo(CachedSnapshot snapshot, ObjectData obj)
        {
            var referencedObjects = new List<ObjectData>();
            long objIndex = -1;
            HashSet<long> foundUnifiedIndices = new HashSet<long>();
            switch (obj.dataType)
            {
                case ObjectDataType.Array:
                case ObjectDataType.BoxedValue:
                case ObjectDataType.Object:
                {
                    if (snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(obj.hostManagedObjectPtr, out var idx))
                    {
                        objIndex = snapshot.ManagedObjectIndexToUnifiedObjectIndex(idx);

                        if (!snapshot.CrawledData.ConnectionsFromMappedToUnifiedIndex.TryGetValue(objIndex, out var connectionIdxs))
                            break;

                        //add crawled connections
                        foreach (var i in connectionIdxs)
                        {
                            var c = snapshot.CrawledData.Connections[i];
                            switch (c.connectionType)
                            {
                                case ManagedConnection.ConnectionType.ManagedObject_To_ManagedObject:
                                    referencedObjects.Add(ObjectData.FromUnifiedObjectIndex(snapshot, c.GetUnifiedIndexTo(snapshot)));
                                    break;
                                case ManagedConnection.ConnectionType.UnityEngineObject:
                                    // these get at added in the loop at the end of the function
                                    // tried using a hash set to prevent duplicates but the lookup during add locks up the window
                                    // if there are more than about 50k references
                                    referencedObjects.Add(ObjectData.FromNativeObjectIndex(snapshot, c.UnityEngineNativeObjectIndex));
                                    break;
                            }
                        }
                    }
                    break;
                }
                case ObjectDataType.NativeObject:
                {
                    objIndex = snapshot.NativeObjectIndexToUnifiedObjectIndex(obj.nativeObjectIndex);
                    if (!snapshot.CrawledData.ConnectionsMappedToNativeIndex.TryGetValue(obj.nativeObjectIndex, out var connectionIdxs))
                        break;

                    //add crawled connection
                    foreach (var i in connectionIdxs)
                    {
                        switch (snapshot.CrawledData.Connections[i].connectionType)
                        {
                            case ManagedConnection.ConnectionType.ManagedObject_To_ManagedObject:
                            case ManagedConnection.ConnectionType.ManagedType_To_ManagedObject:
                                break;
                            case ManagedConnection.ConnectionType.UnityEngineObject:
                                var managedIndex = snapshot.CrawledData.Connections[i].UnityEngineManagedObjectIndex;
                                foundUnifiedIndices.Add(snapshot.ManagedObjectIndexToUnifiedObjectIndex(managedIndex));
                                referencedObjects.Add(ObjectData.FromManagedObjectIndex(snapshot, managedIndex));
                                break;
                        }
                    }
                    break;
                }
                case ObjectDataType.Type:
                {     //TODO this will need to be changed at some point to use the mapped searches
                    if (snapshot.TypeDescriptions.TypeIndexToArrayIndex.TryGetValue(obj.managedTypeIndex, out var idx))
                    {
                        //add crawled connections
                        for (int i = 0; i != snapshot.CrawledData.Connections.Count; ++i)
                        {
                            var c = snapshot.CrawledData.Connections[i];
                            if (c.connectionType == ManagedConnection.ConnectionType.ManagedType_To_ManagedObject && c.fromManagedType == idx)
                            {
                                if (c.fieldFrom >= 0)
                                {
                                    referencedObjects.Add(obj.GetInstanceFieldBySnapshotFieldIndex(snapshot, c.fieldFrom, false));
                                }
                                else if (c.arrayIndexFrom >= 0)
                                {
                                    referencedObjects.Add(obj.GetArrayElement(snapshot, c.arrayIndexFrom, false));
                                }
                                else
                                {
                                    var referencedObject = ObjectData.FromManagedObjectIndex(snapshot, c.toManagedObjectIndex);
                                    referencedObjects.Add(referencedObject);
                                }
                            }
                        }
                    }
                    break;
                }
            }

            //add connections from the raw snapshot
            if (objIndex >= 0 && snapshot.Connections.FromToMappedConnection.ContainsKey((int)objIndex))
            {
                var cns = snapshot.Connections.FromToMappedConnection[(int)objIndex];
                foreach (var i in cns)
                {
                    // Don't count Native -> Managed Connections again if they have been added based on m_CachedPtr entries
                    if (!foundUnifiedIndices.Contains(i))
                    {
                        foundUnifiedIndices.Add(i);
                        referencedObjects.Add(ObjectData.FromUnifiedObjectIndex(snapshot, i));
                    }
                }
            }
            return referencedObjects.ToArray();
        }

        public static ObjectData[] GetAllReferencedObjects(CachedSnapshot snapshot, ObjectData obj)
        {
            var referencedObjects = new List<ObjectData>();
            long objIndex = -1;
            HashSet<long> foundUnifiedIndices = new HashSet<long>();
            switch (obj.dataType)
            {
                case ObjectDataType.Array:
                case ObjectDataType.BoxedValue:
                case ObjectDataType.Object:
                {
                    if (snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(obj.hostManagedObjectPtr, out var idx))
                    {
                        objIndex = snapshot.ManagedObjectIndexToUnifiedObjectIndex(idx);

                        if (!snapshot.CrawledData.ConnectionsFromMappedToUnifiedIndex.TryGetValue(objIndex, out var connectionIdxs))
                            break;

                        //add crawled connections
                        foreach (var i in connectionIdxs)
                        {
                            var c = snapshot.CrawledData.Connections[i];
                            switch (c.connectionType)
                            {
                                case ManagedConnection.ConnectionType.ManagedObject_To_ManagedObject:
                                    if (c.fieldFrom >= 0)
                                    {
                                        referencedObjects.Add(obj.GetInstanceFieldBySnapshotFieldIndex(snapshot, c.fieldFrom, false));
                                    }
                                    else if (c.arrayIndexFrom >= 0)
                                    {
                                        referencedObjects.Add(obj.GetArrayElement(snapshot, c.arrayIndexFrom, false));
                                    }
                                    else
                                    {
                                        var referencedObject = ObjectData.FromManagedObjectIndex(snapshot, c.toManagedObjectIndex);
                                        referencedObjects.Add(referencedObject);
                                    }
                                    break;
                                case ManagedConnection.ConnectionType.UnityEngineObject:
                                    // these get at added in the loop at the end of the function
                                    // tried using a hash set to prevent duplicates but the lookup during add locks up the window
                                    // if there are more than about 50k references
                                    referencedObjects.Add(ObjectData.FromNativeObjectIndex(snapshot, c.UnityEngineNativeObjectIndex));
                                    break;
                            }
                        }
                    }
                    break;
                }
                case ObjectDataType.NativeObject:
                {
                    objIndex = snapshot.NativeObjectIndexToUnifiedObjectIndex(obj.nativeObjectIndex);
                    if (!snapshot.CrawledData.ConnectionsMappedToNativeIndex.TryGetValue(obj.nativeObjectIndex, out var connectionIdxs))
                        break;

                    //add crawled connection
                    foreach (var i in connectionIdxs)
                    {
                        switch (snapshot.CrawledData.Connections[i].connectionType)
                        {
                            case ManagedConnection.ConnectionType.ManagedObject_To_ManagedObject:
                            case ManagedConnection.ConnectionType.ManagedType_To_ManagedObject:
                                break;
                            case ManagedConnection.ConnectionType.UnityEngineObject:
                                // A ManagedConnection.ConnectionType.UnityEngineObject comes about because a Managed field's m_CachedPtr points at a Native Object
                                // while that connection is technically correct and correctly bidirectional, the Native -> Managed side of the connection
                                // should already be tracked via snapshot.Connections, aka GCHandles reported in the snapshot.
                                // To avoid double reporting this connection, track these in the hashmap to de-dup the list when adding connections based on GCHandle reporting
                                var managedIndex = snapshot.CrawledData.Connections[i].UnityEngineManagedObjectIndex;
                                foundUnifiedIndices.Add(snapshot.ManagedObjectIndexToUnifiedObjectIndex(managedIndex));
                                referencedObjects.Add(ObjectData.FromManagedObjectIndex(snapshot, managedIndex));
                                break;
                        }
                    }
                    break;
                }
                case ObjectDataType.Type:
                {     //TODO this will need to be changed at some point to use the mapped searches
                    if (snapshot.TypeDescriptions.TypeIndexToArrayIndex.TryGetValue(obj.managedTypeIndex, out var idx))
                    {
                        //add crawled connections
                        for (int i = 0; i != snapshot.CrawledData.Connections.Count; ++i)
                        {
                            var c = snapshot.CrawledData.Connections[i];
                            if (c.connectionType == ManagedConnection.ConnectionType.ManagedType_To_ManagedObject && c.fromManagedType == idx)
                            {
                                if (c.fieldFrom >= 0)
                                {
                                    referencedObjects.Add(obj.GetInstanceFieldBySnapshotFieldIndex(snapshot, c.fieldFrom, false));
                                }
                                else if (c.arrayIndexFrom >= 0)
                                {
                                    referencedObjects.Add(obj.GetArrayElement(snapshot, c.arrayIndexFrom, false));
                                }
                                else
                                {
                                    var referencedObject = ObjectData.FromManagedObjectIndex(snapshot, c.toManagedObjectIndex);
                                    referencedObjects.Add(referencedObject);
                                }
                            }
                        }
                    }
                    break;
                }
            }

            //add connections from the raw snapshot
            if (objIndex >= 0 && snapshot.Connections.FromToMappedConnection.ContainsKey((int)objIndex))
            {
                var cns = snapshot.Connections.FromToMappedConnection[(int)objIndex];
                foreach (var i in cns)
                {
                    // Don't count Native -> Managed Connections again if they have been added based on m_CachedPtr entries
                    if (!foundUnifiedIndices.Contains(i))
                    {
                        foundUnifiedIndices.Add(i);
                        referencedObjects.Add(ObjectData.FromUnifiedObjectIndex(snapshot, i));
                    }
                }
            }
            return referencedObjects.ToArray();
        }
    }
}

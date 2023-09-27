namespace Unity.MemoryProfiler.Editor
{
    struct ManagedObjectInfo
    {
        public ulong PtrObject;
        public ulong PtrTypeInfo;
        public int NativeObjectIndex;
        public int ManagedObjectIndex;
        public int ITypeDescription;
        public int Size;
        public int RefCount;
        public BytesAndOffset data;

        public bool IsKnownType => ITypeDescription >= 0;

        public bool IsValid()
        {
            return PtrObject != 0 && PtrTypeInfo != 0 && data.Bytes.IsCreated;
        }

        public static bool operator ==(ManagedObjectInfo lhs, ManagedObjectInfo rhs)
        {
            return lhs.PtrObject == rhs.PtrObject
                && lhs.PtrTypeInfo == rhs.PtrTypeInfo
                && lhs.NativeObjectIndex == rhs.NativeObjectIndex
                && lhs.ManagedObjectIndex == rhs.ManagedObjectIndex
                && lhs.ITypeDescription == rhs.ITypeDescription
                && lhs.Size == rhs.Size
                && lhs.RefCount == rhs.RefCount;
        }

        public static bool operator !=(ManagedObjectInfo lhs, ManagedObjectInfo rhs)
        {
            return !(lhs == rhs);
        }

        public override bool Equals(object obj)
        {
            if (obj is ManagedObjectInfo otherObj)
            {
                return this == otherObj;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return (PtrObject, PtrTypeInfo, NativeObjectIndex, ManagedObjectIndex, ITypeDescription, Size, RefCount).GetHashCode();
        }
    }
}

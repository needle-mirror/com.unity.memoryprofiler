using System;

namespace Unity.MemoryProfiler.Editor
{
    readonly struct ManagedConnection
    {
        public enum ConnectionType
        {
            ManagedObject_To_ManagedObject,
            ManagedType_To_ManagedObject,
            UnityEngineObject,
        }

        readonly int m_Index0;
        readonly int m_Index1;

        public readonly int FieldFrom;
        public readonly int ArrayIndexFrom;

        public readonly ConnectionType TypeOfConnection;

        public ManagedConnection(ConnectionType t, int from, int to, int fieldFrom, int arrayIndexFrom)
        {
            TypeOfConnection = t;
            m_Index0 = from;
            m_Index1 = to;
            FieldFrom = fieldFrom;
            ArrayIndexFrom = arrayIndexFrom;
        }

        public long GetUnifiedIndexFrom(CachedSnapshot snapshot)
        {
            switch (TypeOfConnection)
            {
                case ConnectionType.ManagedObject_To_ManagedObject:
                    return snapshot.ManagedObjectIndexToUnifiedObjectIndex(m_Index0);
                case ConnectionType.ManagedType_To_ManagedObject:
                    return m_Index0;
                case ConnectionType.UnityEngineObject:
                    return snapshot.NativeObjectIndexToUnifiedObjectIndex(m_Index0);
                default:
                    return -1;
            }
        }

        public long GetUnifiedIndexTo(CachedSnapshot snapshot)
        {
            switch (TypeOfConnection)
            {
                case ConnectionType.ManagedObject_To_ManagedObject:
                case ConnectionType.ManagedType_To_ManagedObject:
                case ConnectionType.UnityEngineObject:
                    return snapshot.ManagedObjectIndexToUnifiedObjectIndex(m_Index1);
                default:
                    return -1;
            }
        }

        public int FromManagedObjectIndex
        {
            get
            {
                switch (TypeOfConnection)
                {
                    case ConnectionType.ManagedObject_To_ManagedObject:
                    case ConnectionType.ManagedType_To_ManagedObject:
                        return m_Index0;
                }
                return -1;
            }
        }

        public int ToManagedObjectIndex
        {
            get
            {
                switch (TypeOfConnection)
                {
                    case ConnectionType.ManagedObject_To_ManagedObject:
                    case ConnectionType.ManagedType_To_ManagedObject:
                        return m_Index1;
                }
                return -1;
            }
        }

        public int FromManagedType
        {
            get
            {
                if (TypeOfConnection == ConnectionType.ManagedType_To_ManagedObject)
                {
                    return m_Index0;
                }
                return -1;
            }
        }

        public int UnityEngineNativeObjectIndex
        {
            get
            {
                if (TypeOfConnection == ConnectionType.UnityEngineObject)
                {
                    return m_Index0;
                }
                return -1;
            }
        }

        public int UnityEngineManagedObjectIndex
        {
            get
            {
                if (TypeOfConnection == ConnectionType.UnityEngineObject)
                {
                    return m_Index1;
                }
                return -1;
            }
        }

        public static ManagedConnection MakeUnityEngineObjectConnection(int NativeIndex, int ManagedIndex)
        {
            return new ManagedConnection(ConnectionType.UnityEngineObject, NativeIndex, ManagedIndex, 0, 0);
        }

        public static ManagedConnection MakeConnection(CachedSnapshot snapshot, int fromIndex, ulong fromPtr, int toIndex, ulong toPtr, int fromTypeIndex, int fromField, int fieldArrayIndexFrom)
        {
            if (fromIndex >= 0)
            {
                //from an object
#if DEBUG_VALIDATION
                if (fromField >= 0)
                {
                    if (snapshot.FieldDescriptions.IsStatic[fromField] == 1)
                    {
                        Debug.LogError("Cannot make a connection from an object using a static field.");
                    }
                }
#endif
                return new ManagedConnection(ConnectionType.ManagedObject_To_ManagedObject, fromIndex, toIndex, fromField, fieldArrayIndexFrom);
            }
            else if (fromTypeIndex >= 0)
            {
                //from a type static data
#if DEBUG_VALIDATION
                if (fromField >= 0)
                {
                    if (snapshot.FieldDescriptions.IsStatic[fromField] == 0)
                    {
                        Debug.LogError("Cannot make a connection from a type using a non-static field.");
                    }
                }
#endif
                return new ManagedConnection(ConnectionType.ManagedType_To_ManagedObject, fromTypeIndex, toIndex, fromField, fieldArrayIndexFrom);
            }
            else
            {
                throw new InvalidOperationException("Tried to add a Managed Connection without a valid source.");
            }
        }
    }
}

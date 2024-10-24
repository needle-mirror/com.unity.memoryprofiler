using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal struct UnifiedType
    {
        public static UnifiedType Invalid => new UnifiedType(null, default(ObjectData));
        public bool IsValid => (HasManagedType || HasNativeType) && NativeTypeName != null;
        public readonly ObjectData ManagedTypeData;
        public readonly bool ManagedTypeIsBaseTypeFallback;
        public readonly int NativeTypeIndex;
        public readonly int ManagedTypeIndex;
        public bool HasManagedType => ManagedTypeIndex >= 0;
        public bool HasNativeType => NativeTypeIndex >= 0;
        public bool IsUnifiedType => HasManagedType && HasNativeType;

        public readonly string NativeTypeName;
        public readonly string ManagedTypeName;

        public readonly bool IsUnityObjectType;
        public readonly bool IsMonoBehaviourType;
        public readonly bool IsComponentType;
        public readonly bool IsGameObjectType;
        public readonly bool IsTransformType;
        // Derived Meta Types:
        public bool IsSceneObjectType => IsComponentType || IsGameObjectType || IsTransformType;
        public bool IsAssetObjectType => IsValid && !IsSceneObjectType;

        public UnifiedType(CachedSnapshot snapshot, int nativeTypeIndex)
        {
            ManagedTypeIndex = -1;
            NativeTypeName = ManagedTypeName = string.Empty;
            ManagedTypeData = default;
            ManagedTypeIsBaseTypeFallback = false;
            if (nativeTypeIndex >= 0)
            {
                IsUnityObjectType = true;
                NativeTypeIndex = nativeTypeIndex;
                IsMonoBehaviourType = snapshot.NativeTypes.IsOrDerivesFrom(NativeTypeIndex, snapshot.NativeTypes.MonoBehaviourIdx);
                IsComponentType = snapshot.NativeTypes.IsOrDerivesFrom(NativeTypeIndex, snapshot.NativeTypes.ComponentIdx);
                IsGameObjectType = snapshot.NativeTypes.IsOrDerivesFrom(NativeTypeIndex, snapshot.NativeTypes.GameObjectIdx);
                IsTransformType = snapshot.NativeTypes.IsTransformOrRectTransform(NativeTypeIndex);
                NativeTypeName = snapshot.NativeTypes.TypeName[NativeTypeIndex];

                if (snapshot.CrawledData.NativeUnityObjectTypeIndexToManagedBaseTypeIndex.TryGetValue(NativeTypeIndex, out ManagedTypeIndex))
                {
                    // The Managed Crawler had found an object of this type using it's Managed Base Type,
                    // i.e. not a derived one like a MonoBehaviour (those are always in that dictionary but they don't exist without their Managed Shell)
                    ManagedTypeName = snapshot.TypeDescriptions.TypeDescriptionName[ManagedTypeIndex];
                    ManagedTypeData = ObjectData.FromManagedType(snapshot, ManagedTypeIndex);
                    ManagedTypeIsBaseTypeFallback = true;
                }
                else
                {
                    // reset to invalid in case TryGetValue sets this to 0
                    ManagedTypeIndex = -1;
                }
            }
            else
            {
                NativeTypeIndex = -1;
                IsUnityObjectType = IsMonoBehaviourType = IsComponentType = IsGameObjectType = IsTransformType = false;
            }
        }

        public UnifiedType(CachedSnapshot snapshot, ObjectData objectData)
        {
            ManagedTypeIsBaseTypeFallback = false;
            if (snapshot == null || !objectData.IsValid)
            {
                ManagedTypeData = default;
                NativeTypeIndex = -1;
                ManagedTypeIndex = -1;
                NativeTypeName = ManagedTypeName = string.Empty;
                IsUnityObjectType = IsMonoBehaviourType = IsComponentType = IsGameObjectType = IsTransformType = false;
                return;
            }
            if (objectData.isNative)
            {
                IsUnityObjectType = true;
                var nativeObjectData = objectData;
                NativeTypeIndex = snapshot.NativeObjects.NativeTypeArrayIndex[nativeObjectData.nativeObjectIndex];
                var managedObjectIndex = snapshot.NativeObjects.ManagedObjectIndex[nativeObjectData.nativeObjectIndex];
                if (managedObjectIndex >= 0)
                    ManagedTypeIndex = snapshot.CrawledData.ManagedObjects[managedObjectIndex].ITypeDescription;
                else if (snapshot.CrawledData.NativeUnityObjectTypeIndexToManagedBaseTypeIndex.TryGetValue(NativeTypeIndex, out ManagedTypeIndex))
                    ManagedTypeIsBaseTypeFallback = true;
                else
                    ManagedTypeIndex = -1;
            }
            else
            {
                IsUnityObjectType = false;
                ManagedTypeIndex = objectData.managedTypeIndex;
                if (snapshot.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex.ContainsKey(objectData.managedTypeIndex))
                {
                    IsUnityObjectType = true;
                    NativeTypeIndex = snapshot.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex[objectData.managedTypeIndex];
                }
                else
                    NativeTypeIndex = -1;
            }

            if (ManagedTypeIndex >= 0)
            {
                ManagedTypeName = snapshot.TypeDescriptions.TypeDescriptionName[ManagedTypeIndex];
                ManagedTypeData = ObjectData.FromManagedType(snapshot, ManagedTypeIndex);
            }
            else
            {
                ManagedTypeName = string.Empty;
                ManagedTypeData = default;
            }

            if (IsUnityObjectType && NativeTypeIndex >= 0)
            {
                IsMonoBehaviourType = snapshot.NativeTypes.IsOrDerivesFrom(NativeTypeIndex, snapshot.NativeTypes.MonoBehaviourIdx);
                IsComponentType = snapshot.NativeTypes.IsOrDerivesFrom(NativeTypeIndex, snapshot.NativeTypes.ComponentIdx);
                IsGameObjectType = snapshot.NativeTypes.IsOrDerivesFrom(NativeTypeIndex, snapshot.NativeTypes.GameObjectIdx);
                IsTransformType = snapshot.NativeTypes.IsTransformOrRectTransform(NativeTypeIndex) || /*Is the rest here necessary?*/ snapshot.NativeTypes.IsOrDerivesFrom(NativeTypeIndex, snapshot.NativeTypes.TransformIdx) || snapshot.NativeTypes.IsOrDerivesFrom(NativeTypeIndex, snapshot.NativeTypes.RectTransformIdx);
                NativeTypeName = snapshot.NativeTypes.TypeName[NativeTypeIndex];
            }
            else
            {
                IsUnityObjectType = IsMonoBehaviourType = IsComponentType = IsGameObjectType = IsTransformType = false;
                NativeTypeName = string.Empty;
            }
        }
    }

    internal struct UnifiedUnityObjectInfo
    {
        public static UnifiedUnityObjectInfo Invalid => new UnifiedUnityObjectInfo(null, UnifiedType.Invalid, default(ObjectData));
        public bool IsValid => Type.IsUnityObjectType && (NativeObjectIndex != -1 || ManagedObjectIndex != -1);

        public int NativeObjectIndex => NativeObjectData.nativeObjectIndex;
        public ObjectData NativeObjectData;
        public readonly long ManagedObjectIndex;
        public ObjectData ManagedObjectData;

        public UnifiedType Type;
        public int NativeTypeIndex => Type.NativeTypeIndex;
        public int ManagedTypeIndex => Type.ManagedTypeIndex;
        public string NativeTypeName => Type.NativeTypeName;
        public string ManagedTypeName => Type.ManagedTypeName;

        public ulong TotalSize => NativeSize + (ulong)ManagedSize;
        public int TotalRefCount => ManagedRefCount + NativeRefCount;

        public bool IsLeakedShell => !HasNativeSide && HasManagedSide;
        public bool IsFullUnityObjet => HasNativeSide && HasManagedSide;

        public bool IsComponent => Type.IsComponentType;
        public bool IsMonoBehaviour => Type.IsMonoBehaviourType;
        public bool IsGameObject => Type.IsGameObjectType;
        public bool IsTransform => Type.IsTransformType;
        // Derived Meta Types:
        // Scene Objects are GameObjects and Components, unless they are attached to a prefab (IsPersistent), then they are assets
        public bool IsSceneObject => Type.IsSceneObjectType && !IsPersistentAsset;
        public bool IsAssetObject => Type.IsAssetObjectType && !IsManager || Type.IsSceneObjectType && IsPersistentAsset;

        // Native Object Only info
        public bool HasNativeSide => NativeObjectIndex != -1;
        public readonly InstanceID InstanceId;
        public readonly ulong NativeSize;
        public readonly string NativeObjectName;
        public readonly HideFlags HideFlags;
        public readonly Format.ObjectFlags Flags;
        public readonly int NativeRefCount;
        public bool IsPersistentAsset => Flags.HasFlag(Format.ObjectFlags.IsPersistent) && !IsManager;
        public bool IsRuntimeCreated => InstanceId.IsRuntimeCreated();
        public bool IsManager => Flags.HasFlag(Format.ObjectFlags.IsManager);
        public bool IsDontUnload => Flags.HasFlag(Format.ObjectFlags.IsDontDestroyOnLoad) || HideFlags.HasFlag(HideFlags.DontUnloadUnusedAsset);

        // Managed Object Only info
        public bool HasManagedSide => ManagedObjectIndex != -1;
        public readonly int ManagedRefCount;
        public readonly long ManagedSize;

        public UnifiedUnityObjectInfo(CachedSnapshot snapshot, ObjectData unityObject)
            : this(snapshot, new UnifiedType(snapshot, unityObject), unityObject)
        { }

        public UnifiedUnityObjectInfo(CachedSnapshot snapshot, UnifiedType type, ObjectData unityObject)
        {
            Type = type;
            if (snapshot == null || !unityObject.IsValid || !type.IsValid || !type.IsUnityObjectType)
            {
                NativeObjectData = default;
                ManagedObjectData = default;
                ManagedObjectIndex = -1;
                InstanceId = InstanceID.None;
                NativeSize = 0;
                NativeObjectName = string.Empty;
                HideFlags = 0;
                Flags = 0;
                ManagedSize = ManagedRefCount = NativeRefCount = 0;
                return;
            }

            ManagedObjectInfo managedObjectInfo = default;
            // get the managed/native counterpart and/or type
            if (unityObject.isNative)
            {
                NativeObjectData = unityObject;
                ManagedObjectIndex = snapshot.NativeObjects.ManagedObjectIndex[NativeObjectData.nativeObjectIndex];
                ManagedObjectData = ObjectData.FromManagedObjectIndex(snapshot, ManagedObjectIndex);
                if (ManagedObjectData.IsValid)
                    managedObjectInfo = ManagedObjectData.GetManagedObject(snapshot);
            }
            else
            {
                ManagedObjectData = unityObject;
                managedObjectInfo = unityObject.GetManagedObject(snapshot);
                ManagedObjectIndex = managedObjectInfo.ManagedObjectIndex;
                if (managedObjectInfo.NativeObjectIndex >= -1)
                    NativeObjectData = ObjectData.FromNativeObjectIndex(snapshot, managedObjectInfo.NativeObjectIndex);
                else
                    NativeObjectData = default;
            }

            // Native Object Only
            if (NativeObjectData.IsValid)
            {
                Flags = NativeObjectData.GetFlags(snapshot);

                InstanceId = NativeObjectData.GetInstanceID(snapshot);
                NativeSize = snapshot.NativeObjects.Size[NativeObjectData.nativeObjectIndex];
                NativeObjectName = snapshot.NativeObjects.ObjectName[NativeObjectData.nativeObjectIndex];
                HideFlags = snapshot.NativeObjects.HideFlags[NativeObjectData.nativeObjectIndex];
                NativeRefCount = snapshot.NativeObjects.RefCount[NativeObjectData.nativeObjectIndex];
                // Discount the Native Reference to the Managed Object, that is established via a GCHandle
                if (ManagedObjectData.IsValid && NativeRefCount >= 1)
                    --NativeRefCount;
            }
            else
            {
                InstanceId = InstanceID.None;
                NativeSize = 0;
                NativeObjectName = string.Empty;
                HideFlags = 0;
                Flags = 0;
                NativeRefCount = 0;
            }

            // Managed Object Only
            if (ManagedObjectData.IsValid)
            {
                ManagedRefCount = managedObjectInfo.RefCount;
                // Discount the Managed Reference to the Native Object, that is established via m_CachedPtr
                if (NativeObjectData.IsValid && ManagedRefCount >= 1)
                    --ManagedRefCount;
                ManagedSize = managedObjectInfo.Size;
            }
            else
            {
                ManagedRefCount = 0;
                ManagedSize = 0;
            }
        }
    }
}

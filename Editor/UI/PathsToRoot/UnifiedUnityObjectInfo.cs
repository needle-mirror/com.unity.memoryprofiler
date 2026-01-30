using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal readonly struct UnifiedTypeAndName
    {
        public readonly string NativeTypeName;
        public readonly string ManagedTypeName;
        public readonly ObjectData ManagedTypeData;

        readonly UnifiedType m_TypeInfo;

        public static UnifiedTypeAndName Invalid => new UnifiedTypeAndName(null, CachedSnapshot.NativeTypeEntriesCache.InvalidTypeIndex);

        public bool IsValid => (HasManagedType || HasNativeType) && NativeTypeName != null;
        public readonly bool ManagedTypeIsBaseTypeFallback => m_TypeInfo.ManagedTypeIsBaseTypeFallback;
        public readonly int NativeTypeIndex => m_TypeInfo.NativeTypeIndex;
        public readonly int ManagedTypeIndex => m_TypeInfo.ManagedTypeIndex;
        public bool HasManagedType => m_TypeInfo.HasManagedType;
        public bool HasNativeType => m_TypeInfo.HasNativeType;
        public bool IsUnifiedType => m_TypeInfo.IsUnifiedType;

        public readonly bool IsUnityObjectType => m_TypeInfo.IsUnityObjectType;
        public readonly bool IsMonoBehaviourType => m_TypeInfo.IsMonoBehaviourType;
        public readonly bool IsComponentType => m_TypeInfo.IsComponentType;
        public readonly bool IsGameObjectType => m_TypeInfo.IsGameObjectType;
        public readonly bool IsTransformType => m_TypeInfo.IsTransformType;
        // Derived Meta Types:
        public bool IsSceneObjectType => m_TypeInfo.IsSceneObjectType;
        public bool IsAssetObjectType => m_TypeInfo.IsAssetObjectType;

        public static UnifiedTypeAndName GetTypeInfoForObjectData(CachedSnapshot snapshot, ObjectData objectData)
        {
            UnifiedType.GetTypeIndices(snapshot, objectData, out var nativeTypeIndex, out var managedTypeIndex,
                out _, out _);

            if (managedTypeIndex is CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid
                && nativeTypeIndex is not CachedSnapshot.NativeTypeEntriesCache.InvalidTypeIndex
                && snapshot.TypeDescriptions.UnifiedTypeInfoNative[nativeTypeIndex].ManagedTypeIndex is not CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid)
            {
                managedTypeIndex = snapshot.TypeDescriptions.UnifiedTypeInfoNative[nativeTypeIndex].ManagedTypeIndex;
                // The Managed Crawler had found an object of this type using it's Managed Base Type,
                // i.e. not a derived one like a MonoBehaviour (those are always in that dictionary but they don't exist without their Managed Shell)
            }
            var typeData = managedTypeIndex is CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid ? default : ObjectData.FromManagedType(snapshot, managedTypeIndex);
            return new UnifiedTypeAndName(snapshot, nativeTypeIndex, managedTypeIndex, typeData);
        }

        public UnifiedTypeAndName(CachedSnapshot snapshot, int nativeTypeIndex, int managedTypeIndex = -1, ObjectData managedTypeData = default)
        {
            ManagedTypeData = managedTypeData;
            if (managedTypeIndex is not CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid)
            {
                m_TypeInfo = snapshot.TypeDescriptions.UnifiedTypeInfoManaged[managedTypeIndex];
                if (!ManagedTypeData.IsValid)
                {
                    ManagedTypeData = ObjectData.FromManagedType(snapshot, managedTypeIndex);
                }
            }
            else if (nativeTypeIndex is not CachedSnapshot.NativeTypeEntriesCache.InvalidTypeIndex)
            {
                m_TypeInfo = snapshot.TypeDescriptions.UnifiedTypeInfoNative[nativeTypeIndex];
            }
            else
            {
                NativeTypeName = ManagedTypeName = null;
                m_TypeInfo = UnifiedType.Invalid;
                return;
            }

            NativeTypeName = m_TypeInfo.NativeTypeIndex is not CachedSnapshot.NativeTypeEntriesCache.InvalidTypeIndex ? snapshot.NativeTypes.TypeName[m_TypeInfo.NativeTypeIndex] : string.Empty;
            ManagedTypeName = m_TypeInfo.ManagedTypeIndex is not CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid ? snapshot.TypeDescriptions.TypeDescriptionName[m_TypeInfo.ManagedTypeIndex] : string.Empty;
        }

        public UnifiedTypeAndName(CachedSnapshot snapshot, UnifiedType typeInfo)
        {
            ManagedTypeData = default;
            m_TypeInfo = typeInfo;
            if (m_TypeInfo.ManagedTypeIndex is not CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid)
            {
                ManagedTypeData = ObjectData.FromManagedType(snapshot, typeInfo.ManagedTypeIndex);
            }
            else if (m_TypeInfo.NativeTypeIndex is CachedSnapshot.NativeTypeEntriesCache.InvalidTypeIndex)
            {
                NativeTypeName = ManagedTypeName = null;
                m_TypeInfo = UnifiedType.Invalid;
                return;
            }

            NativeTypeName = m_TypeInfo.NativeTypeIndex is not CachedSnapshot.NativeTypeEntriesCache.InvalidTypeIndex ? snapshot.NativeTypes.TypeName[m_TypeInfo.NativeTypeIndex] : string.Empty;
            ManagedTypeName = m_TypeInfo.ManagedTypeIndex is not CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid ? snapshot.TypeDescriptions.TypeDescriptionName[m_TypeInfo.ManagedTypeIndex] : string.Empty;
        }
    }

    // Unmanaged version
    internal readonly struct UnifiedType
    {
        public static UnifiedType Invalid => new UnifiedType(null, default(ObjectData));
        public bool IsValid => HasManagedType || HasNativeType;
        public readonly bool ManagedTypeIsBaseTypeFallback => ManagedBaseTypeIndexForNativeType == ManagedTypeIndex;
        public readonly int NativeTypeIndex;
        public readonly int ManagedTypeIndex;
        public readonly int ManagedBaseTypeIndexForNativeType;
        public bool HasManagedType => ManagedTypeIndex is not CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid;
        public bool HasNativeType => NativeTypeIndex is not CachedSnapshot.NativeTypeEntriesCache.InvalidTypeIndex;
        public bool IsUnifiedType => HasManagedType && HasNativeType;

        public readonly bool IsUnityObjectType;
        public readonly bool IsMonoBehaviourType;
        /// <summary>
        /// True for components, including ´MonoBehaviours´ (see: <seealso cref="IsMonoBehaviourType"/>
        /// BUT excluding ´Transforms´ and ´RectTransforms´ (see: <seealso cref="IsTransformType"/>).
        /// Transform types technically derive from the native ´Component´ type but behave differently in that
        /// there can only ever be 1 transform type instance per ´GameObject´ instance and, depending on how you want to read the scene hierarchy,
        /// the transform type instance roots/owns/holds the ´GameObject´ instance in memory or vice versa. And this combo of ´GameObject´ and ´Transfrom´
        /// root all the components on the ´GameObject´ (through a reference from the ´GameObject´).
        ///
        /// Contrasting to that, other Component type instances do NOT root/own/hold other <seealso cref="IsSceneObjectType"/> instance in memory.
        /// </summary>
        public readonly bool IsComponentType;
        public readonly bool IsGameObjectType;
        public readonly bool IsTransformType;
        // Derived Meta Types:
        public bool IsSceneObjectType => IsUnityObjectType && (IsComponentType || IsGameObjectType || IsTransformType);
        public bool IsAssetObjectType => IsUnityObjectType && IsValid && !IsSceneObjectType;

        public UnifiedType(CachedSnapshot snapshot, int nativeTypeIndex) : this(snapshot.NativeTypes, snapshot.TypeDescriptions, nativeTypeIndex)
        {
        }

        public UnifiedType(CachedSnapshot.NativeTypeEntriesCache nativeTypes, CachedSnapshot.TypeDescriptionEntriesCache typeDescriptions, int nativeTypeIndex,
            int managedTypeIndex = CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid,
            int managedBaseTypeIndex = CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid)
        {
            ManagedTypeIndex = managedTypeIndex is CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid ? managedBaseTypeIndex : managedTypeIndex;
            ManagedBaseTypeIndexForNativeType = managedBaseTypeIndex;
            if (nativeTypeIndex is not CachedSnapshot.NativeTypeEntriesCache.InvalidTypeIndex)
            {
                IsUnityObjectType = true;
                NativeTypeIndex = nativeTypeIndex;
                IsMonoBehaviourType = nativeTypes.IsOrDerivesFrom(NativeTypeIndex, nativeTypes.MonoBehaviourIdx);
                IsTransformType = nativeTypes.IsTransformOrRectTransform(NativeTypeIndex); //|| /*The rest here isn't necessary unless we want to over protect this into the future*/ snapshot.NativeTypes.IsOrDerivesFrom(NativeTypeIndex, snapshot.NativeTypes.TransformIdx) || snapshot.NativeTypes.IsOrDerivesFrom(NativeTypeIndex, snapshot.NativeTypes.RectTransformIdx);
                IsComponentType = !IsTransformType && nativeTypes.IsOrDerivesFrom(NativeTypeIndex, nativeTypes.ComponentIdx);
                IsGameObjectType = nativeTypes.IsOrDerivesFrom(NativeTypeIndex, nativeTypes.GameObjectIdx);

                // While initializing the UnifiedTypeInfo data, there is no point in checking the below as the managed crawler isn't running yet and therefore can't update the ManagedTypeIndex
                if ((typeDescriptions?.UnifiedTypeInfo.IsCreated ?? false) && managedTypeIndex is CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid)
                {
                    // if no managed type was provided, check if the managed base type is known
                    if (typeDescriptions.UnifiedTypeInfoNative[NativeTypeIndex].IsUnifiedType)
                    {
                        // The Managed Crawler had found an object of this type using it's Managed Base Type,
                        // i.e. not a derived one like a MonoBehaviour (those are always in that dictionary, but they don't exist without their Managed Shell)
                        ManagedTypeIndex = typeDescriptions.UnifiedTypeInfoNative[NativeTypeIndex].ManagedTypeIndex;
                    }
                }
            }
            else
            {
                NativeTypeIndex = CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid;
                IsUnityObjectType = (IsMonoBehaviourType = IsComponentType = IsGameObjectType = IsTransformType = false)
                                    // In case the native type for this managed type could not be found but a managed base type which did inherit from, or is, UnityEngine.Object, this is still a Unity Objects
                                    || ManagedBaseTypeIndexForNativeType is not CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid;
            }
        }

        public static void GetTypeIndices(CachedSnapshot snapshot, ObjectData objectData,
            out int nativeTypeIndex, out int managedTypeIndex,
            out bool isUnityObjectType, out int managedBaseTypeIndexForNativeType)
        {
            if (snapshot == null || !objectData.IsValid)
            {
                managedBaseTypeIndexForNativeType = CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid;
                nativeTypeIndex = CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid;
                managedTypeIndex = CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid;
                isUnityObjectType = false;
                return;
            }
            managedBaseTypeIndexForNativeType = CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid;
            managedTypeIndex = CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid;
            if (objectData.isNativeObject)
            {
                isUnityObjectType = true;
                var nativeObjectData = objectData;
                nativeTypeIndex = snapshot.NativeObjects.NativeTypeArrayIndex[nativeObjectData.nativeObjectIndex];
                var managedObjectIndex = snapshot.NativeObjects.ManagedObjectIndex[nativeObjectData.nativeObjectIndex];
                if (managedObjectIndex is not -1)
                    managedTypeIndex = snapshot.CrawledData.ManagedObjects[managedObjectIndex].ITypeDescription;
                if (managedTypeIndex is not CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid)
                    managedBaseTypeIndexForNativeType = snapshot.TypeDescriptions.UnifiedTypeInfoManaged[managedTypeIndex].ManagedBaseTypeIndexForNativeType;
                else if (snapshot.TypeDescriptions.UnifiedTypeInfoNative[nativeTypeIndex].ManagedTypeIndex is not CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid)
                {
                    managedTypeIndex = snapshot.TypeDescriptions.UnifiedTypeInfoNative[nativeTypeIndex].ManagedTypeIndex;
                    managedBaseTypeIndexForNativeType = snapshot.TypeDescriptions.UnifiedTypeInfoNative[nativeTypeIndex].ManagedBaseTypeIndexForNativeType;
                }
                else
                    managedTypeIndex = CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid;
            }
            else if (objectData.isManaged)
            {
                isUnityObjectType = false;
                managedTypeIndex = objectData.managedTypeIndex;
                if (snapshot.TypeDescriptions.UnifiedTypeInfoManaged[objectData.managedTypeIndex].IsUnityObjectType)
                {
                    isUnityObjectType = true;
                    nativeTypeIndex = snapshot.TypeDescriptions.UnifiedTypeInfoManaged[objectData.managedTypeIndex].NativeTypeIndex;
                }
                else
                    nativeTypeIndex = CachedSnapshot.NativeTypeEntriesCache.InvalidTypeIndex;
            }
            else
            {
                managedTypeIndex = CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid;
                nativeTypeIndex = CachedSnapshot.NativeTypeEntriesCache.InvalidTypeIndex;
                isUnityObjectType = false;
            }
        }

        public UnifiedType(CachedSnapshot snapshot, ObjectData objectData)
        {
            GetTypeIndices(snapshot, objectData, out NativeTypeIndex, out ManagedTypeIndex, out IsUnityObjectType, out ManagedBaseTypeIndexForNativeType);

            if (snapshot == null || !objectData.IsValid)
            {
                IsMonoBehaviourType = IsComponentType = IsGameObjectType = IsTransformType = false;
                return;
            }

            if (IsUnityObjectType && NativeTypeIndex is not CachedSnapshot.NativeTypeEntriesCache.InvalidTypeIndex)
            {
                IsMonoBehaviourType = snapshot.NativeTypes.IsOrDerivesFrom(NativeTypeIndex, snapshot.NativeTypes.MonoBehaviourIdx);
                IsTransformType = snapshot.NativeTypes.IsTransformOrRectTransform(NativeTypeIndex); //|| /*The rest here isn't necessary unless we want to over protect this into the future*/ snapshot.NativeTypes.IsOrDerivesFrom(NativeTypeIndex, snapshot.NativeTypes.TransformIdx) || snapshot.NativeTypes.IsOrDerivesFrom(NativeTypeIndex, snapshot.NativeTypes.RectTransformIdx);
                IsComponentType = !IsTransformType && snapshot.NativeTypes.IsOrDerivesFrom(NativeTypeIndex, snapshot.NativeTypes.ComponentIdx);
                IsGameObjectType = snapshot.NativeTypes.IsOrDerivesFrom(NativeTypeIndex, snapshot.NativeTypes.GameObjectIdx);
            }
            else
            {
                IsUnityObjectType = IsMonoBehaviourType = IsComponentType = IsGameObjectType = IsTransformType = false;
            }
        }
    }

    internal struct UnifiedUnityObjectInfo
    {
        public static UnifiedUnityObjectInfo Invalid => new UnifiedUnityObjectInfo(null, UnifiedTypeAndName.Invalid, default(ObjectData));
        public bool IsValid => Type.IsUnityObjectType && (NativeObjectIndex != -1 || ManagedObjectIndex != -1);

        public long NativeObjectIndex => NativeObjectData.nativeObjectIndex;
        public ObjectData NativeObjectData;
        public readonly long ManagedObjectIndex;
        public ObjectData ManagedObjectData;

        public UnifiedTypeAndName Type;
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
        public readonly EntityId EntityId;
        public readonly ulong NativeSize;
        public readonly string NativeObjectName;
        public readonly HideFlags HideFlags;
        public readonly Format.ObjectFlags Flags;
        public readonly int NativeRefCount;
        public bool IsPersistentAsset => Flags.HasFlag(Format.ObjectFlags.IsPersistent) && !IsManager;
        /// <summary>
        /// Prefer checking for <see cref="IsPersistentAsset"/> as more future proof (and generally more reliable) way of checking
        /// that this Object is related to a SerializedFile.
        /// SA: <seealso cref="EntityId.IsRuntimeCreated"/>
        /// </summary>
        public bool IsRuntimeCreated => EntityId.IsRuntimeCreated();
        public bool IsManager => Flags.HasFlag(Format.ObjectFlags.IsManager);
        public bool IsDontUnload => Flags.HasFlag(Format.ObjectFlags.IsDontDestroyOnLoad) || HideFlags.HasFlag(HideFlags.DontUnloadUnusedAsset);

        // Managed Object Only info
        public bool HasManagedSide => ManagedObjectIndex != -1;
        public readonly int ManagedRefCount;
        public readonly long ManagedSize;

        public UnifiedUnityObjectInfo(CachedSnapshot snapshot, ObjectData unityObject)
            : this(snapshot, UnifiedTypeAndName.GetTypeInfoForObjectData(snapshot, unityObject), unityObject)
        { }

        public UnifiedUnityObjectInfo(CachedSnapshot snapshot, UnifiedTypeAndName type, ObjectData unityObject)
        {
            Type = type;
            if (snapshot == null || !unityObject.IsValid || !type.IsValid || !type.IsUnityObjectType)
            {
                NativeObjectData = default;
                ManagedObjectData = default;
                ManagedObjectIndex = -1;
                EntityId = EntityId.None;
                NativeSize = 0;
                NativeObjectName = string.Empty;
                HideFlags = 0;
                Flags = 0;
                ManagedSize = ManagedRefCount = NativeRefCount = 0;
                return;
            }

            ManagedObjectInfo managedObjectInfo = default;
            // get the managed/native counterpart and/or type
            if (unityObject.isNativeObject)
            {
                NativeObjectData = unityObject;
                ManagedObjectIndex = snapshot.NativeObjects.ManagedObjectIndex[NativeObjectData.nativeObjectIndex];
                ManagedObjectData = ObjectData.FromManagedObjectIndex(snapshot, ManagedObjectIndex);
                if (ManagedObjectData.IsValid)
                    managedObjectInfo = ManagedObjectData.GetManagedObject(snapshot);
            }
            else if (unityObject.isManaged)
            {
                ManagedObjectData = unityObject;
                managedObjectInfo = unityObject.GetManagedObject(snapshot);
                ManagedObjectIndex = managedObjectInfo.ManagedObjectIndex;
                if (managedObjectInfo.NativeObjectIndex >= -1)
                    NativeObjectData = ObjectData.FromNativeObjectIndex(snapshot, managedObjectInfo.NativeObjectIndex);
                else
                    NativeObjectData = ObjectData.Invalid;
            }
            else
            {
                ManagedObjectData = ObjectData.Invalid;
                NativeObjectData = ObjectData.Invalid;
                ManagedObjectIndex = -1;
            }

            // Native Object Only
            if (NativeObjectData.IsValid)
            {
                Flags = NativeObjectData.GetFlags(snapshot);

                EntityId = NativeObjectData.GetEntityId(snapshot);
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
                EntityId = EntityId.None;
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

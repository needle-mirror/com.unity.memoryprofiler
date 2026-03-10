using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.MemoryProfiler.Editor.Extensions;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using Unity.Profiling;
using Unity.MemoryProfiler.Editor.UI;

// Pre com.unity.collections@2.1.0 NativeHashMap was not constraining its held data to unmanaged but to struct.
// NativeHashSet does not have the same issue, but for ease of use may get an alias below for EntityId.
#if !UNMANAGED_NATIVE_HASHMAP_AVAILABLE
using AddressToIndex = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<ulong, long>;
using AddressToIntIndex = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<ulong, int>;
#else
using AddressToIndex = Unity.Collections.NativeHashMap<ulong, long>;
using AddressToIntIndex = Unity.Collections.NativeHashMap<ulong, int>;
#endif
using Debug = UnityEngine.Debug;

namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
        //leave this as second to last thing to convert, also a major pain in the ass
        public class TypeDescriptionEntriesCache : IDisposable
        {
            public const int ITypeInvalid = -1;
            public const long UnifiedTypeArrayManagedSection = 0;
            public const long UnifiedTypeArrayNativeSection = 1;
            const int k_DefaultFieldProcessingBufferSize = 64;
            public const string UnityObjectTypeName = "UnityEngine.Object";
            public const string UnityNativeObjectPointerFieldName = "m_CachedPtr";
            public const string NativeCollectionsNamspaceAndTypePrefix = "Unity.Collections.Native";
            public int IFieldUnityObjectMCachedPtr { get; private set; }
            public int IFieldUnityObjectMCachedPtrOffset { get; private set; } = -1;

            const string k_UnityMonoBehaviourTypeName = "UnityEngine.MonoBehaviour";
            const string k_UnityScriptableObjectTypeName = "UnityEngine.ScriptableObject";
            const string k_UnityComponentObjectTypeName = "UnityEngine.Component";
            const string k_UnityGameObjectTypeName = "UnityEngine.GameObject";
            const string k_UnityTransformTypeName = "UnityEngine.Transform";
            const string k_UnityRectTransformTypeName = "UnityEngine.RectTransform";
            const string k_UnityEditorEditorTypeName = "UnityEditor.Editor";

            const string k_SystemObjectTypeName = "System.Object";
            const string k_SystemValueTypeName = "System.ValueType";
            const string k_SystemEnumTypeName = "System.Enum";

            const string k_SystemInt16Name = "System.Int16";
            const string k_SystemInt32Name = "System.Int32";
            const string k_SystemInt64Name = "System.Int64";

            const string k_SystemUInt16Name = "System.UInt16";
            const string k_SystemUInt32Name = "System.UInt32";

            const string k_SystemUInt64Name = "System.UInt64";
            const string k_SystemBoolName = "System.Boolean";
            const string k_SystemCharTypeName = "System.Char";
            const string k_SystemCharArrayTypeName = "System.Char[]";
            const string k_SystemDoubleName = "System.Double";
            const string k_SystemSingleName = "System.Single";
            const string k_SystemStringName = "System.String";
            const string k_SystemIntPtrName = "System.IntPtr";
            const string k_SystemVoidPtrName = "System.Void*";
            const string k_SystemBytePtrName = "System.Byte*";
            const string k_SystemByteName = "System.Byte";

            public int Count;
            public DynamicArray<TypeFlags> Flags = default;
            public DynamicArray<int> BaseOrElementTypeIndex = default;

            /// <summary>
            /// Type size, which for value types does not include a header that would be added if they get boxed onto the heap.
            /// For boxed value types (i.e. <seealso cref="HasFlag(int, TypeFlags)"/> for flag <seealso cref="TypeFlags.kValueType"/> is true)
            /// on the heap (rather than e.g. non-boxed value types) you'll need to add
            /// <seealso cref="VirtualMachineInformation.ObjectHeaderSize"/>.
            /// You can also use <see cref="GetMinSizeForHeapObjectOfType"/> instead of checking manually if you need to add the header size
            /// </summary>
            public DynamicArray<int> Size = default;
            /// <summary>
            /// Get's the size an object of this type would have on the heap.
            /// This method only returns minimal size for strings or arrays instances, as their size depends on their length
            /// and their full size can't be determined for all instances of their type, but only for a concrete object.
            /// For all other types, it returns the definitive size on the heap.
            ///
            /// For the full size of strings use <see cref="ManagedHeapArrayDataTools.ReadArrayObjectSizeInBytes"/> instead.
            /// For the full size of arrays use <see cref="StringTools.ReadStringObjectSizeInBytes"/ instead>.
            /// </summary>
            /// <param name="typeIndex"></param>
            /// <param name="vmInfo"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public int GetMinSizeForHeapObjectOfType(long typeIndex, in VirtualMachineInformation vmInfo)
                => Size[typeIndex] + (HasFlag(typeIndex, TypeFlags.kValueType) ? (int)vmInfo.ObjectHeaderSize : 0);

            public DynamicArray<ulong> TypeInfoAddress = default;
            enum TypeCategory
            {
                NotChecked = 0,
                Concrete,
                AbstractInterface,
                AbstractGeneric,
                IgnoreForHeapObjectTypeChecks,
            }
            DynamicArray<TypeCategory> m_TypeCategory = default;
            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public bool IsConcrete(long typeIndex) => m_TypeCategory[typeIndex] == TypeCategory.Concrete;
            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public bool IgnoreForHeapObjectTypeChecks(long typeIndex) => m_TypeCategory[typeIndex] == TypeCategory.IgnoreForHeapObjectTypeChecks;

            public string[] TypeDescriptionName;
            public string[] Assembly;

            public NestedDynamicArray<int> FieldIndices => m_FieldIndicesReadOp.CompleteReadAndGetNestedResults();
            NestedDynamicSizedArrayReadOperation<int> m_FieldIndicesReadOp;
            public NestedDynamicArray<byte> StaticFieldBytes => m_StaticFieldBytesReadOp.CompleteReadAndGetNestedResults();
            NestedDynamicSizedArrayReadOperation<byte> m_StaticFieldBytesReadOp;

            //secondary data, handled inside InitSecondaryItems
            NestedDynamicArray<int> m_FieldIndicesInstance; //includes all bases' instance fields
            NestedDynamicArray<int> m_FieldIndicesStatic;   //includes all bases' static fields
            NestedDynamicArray<int> m_FieldIndicesOwnedStatic; //includes only type's static fields
            // temporary processing buffer used inside of, and disposed in, InitSecondaryItems
            NativeList<int> m_FieldProcessingBuffer;

            public ref readonly NestedDynamicArray<int> FieldIndicesInstance => ref m_FieldIndicesInstance;
            public ref readonly NestedDynamicArray<int> FieldIndicesStatic => ref m_FieldIndicesStatic;
            public ref readonly NestedDynamicArray<int> FieldIndicesOwnedStatic => ref m_FieldIndicesOwnedStatic;

            public readonly int ITypeValueType = ITypeInvalid;
            public readonly int ITypeUnityObject = ITypeInvalid;
            public readonly int ITypeObject = ITypeInvalid;
            public readonly int ITypeEnum = ITypeInvalid;
            public readonly int ITypeInt16 = ITypeInvalid;
            public readonly int ITypeInt32 = ITypeInvalid;
            public readonly int ITypeInt64 = ITypeInvalid;
            public readonly int ITypeUInt16 = ITypeInvalid;
            public readonly int ITypeUInt32 = ITypeInvalid;
            public readonly int ITypeUInt64 = ITypeInvalid;
            public readonly int ITypeBool = ITypeInvalid;
            public readonly int ITypeChar = ITypeInvalid;
            public readonly int ITypeCharArray = ITypeInvalid;
            public readonly int ITypeDouble = ITypeInvalid;
            public readonly int ITypeSingle = ITypeInvalid;
            public readonly int ITypeString = ITypeInvalid;
            public readonly int ITypeIntPtr = ITypeInvalid;
            public readonly int ITypeVoidPtr = ITypeInvalid;
            public readonly int ITypeBytePtr = ITypeInvalid;
            public readonly int ITypeByte = ITypeInvalid;

            public readonly int ITypeUnityMonoBehaviour = ITypeInvalid;
            public readonly int ITypeUnityScriptableObject = ITypeInvalid;
            public readonly int ITypeUnityComponent = ITypeInvalid;
            public readonly int ITypeUnityGameObject = ITypeInvalid;
            public readonly int ITypeUnityTransform = ITypeInvalid;
            public readonly int ITypeUnityRectTransform = ITypeInvalid;
            public readonly int ITypeUnityEditorEditor = ITypeInvalid;
            public AddressToIntIndex TypeInfoToArrayIndex { get; private set; }

            // Well these also include native types so making them part of the managed type descriptions isn't that
            // clean but there isn't a very clean alternative right now so, good enough as any place. Consider moving...
            readonly NestedDynamicArray<UnifiedType> m_UnifiedTypeInfo = default;
            /// <summary>
            /// Only fully initialized after the Managed Crawler is done stitching up Objects.
            /// </summary>
            public ref readonly NestedDynamicArray<UnifiedType> UnifiedTypeInfo => ref m_UnifiedTypeInfo;
            public ref readonly DynamicArrayRef<UnifiedType> UnifiedTypeInfoManaged => ref m_UnifiedTypeInfo[UnifiedTypeArrayManagedSection];
            public ref readonly DynamicArrayRef<UnifiedType> UnifiedTypeInfoNative => ref m_UnifiedTypeInfo[UnifiedTypeArrayNativeSection];
            public NativeHashSet<int> PureCSharpTypeIndices { get; private set; }

            readonly NativeHashSet<int> m_UninstantiatableUnityBaseTypesWithSetNativeType = default;
            /// <summary>
            /// AKA: UnityEngine.Object & UnityEngine.Component
            /// If an object where a Scriptable type, its fallback would be set as <see cref="UninstantiatableUnityBaseTypesWithScriptingDefinedTypes"/> after
            /// <see cref="TypeDescriptionEntriesCache"/> has been constructed and <see cref="TypeDescriptionEntriesCache.InitSecondaryItems(FieldDescriptionEntriesCache, NativeTypeEntriesCache, VirtualMachineInformation, Dictionary{string, int})"/> called.
            /// </summary>
            public ref readonly NativeHashSet<int> UninstantiatableUnityBaseTypesWithSetNativeType => ref m_UninstantiatableUnityBaseTypesWithSetNativeType;

            readonly NativeHashSet<int> m_UninstantiatableUnityBaseTypesWithScriptingDefinedTypes = default;
            /// <summary>
            /// AKA: ScriptableObjects and MonoBehaviour
            /// </summary>
            public ref readonly NativeHashSet<int> UninstantiatableUnityBaseTypesWithScriptingDefinedTypes => ref m_UninstantiatableUnityBaseTypesWithScriptingDefinedTypes;

            readonly NativeHashSet<int> m_AllUninstantiatableUnityBaseTypes = default;
            /// <summary>
            /// A combined hashset of both <see cref="UninstantiatableUnityBaseTypesWithSetNativeType"/> and <see cref="UninstantiatableUnityBaseTypesWithScriptingDefinedTypes"/>.
            /// </summary>
            public ref readonly NativeHashSet<int> AllUninstantiatableUnityBaseTypes => ref m_AllUninstantiatableUnityBaseTypes;

#if ENTITY_ID_STRUCT_AVAILABLE && !ENTITY_ID_CHANGED_SIZE
            static TypeDescriptionEntriesCache()
            {
                Checks.IsTrue((typeof(EntityId) != typeof(UnityEngine.EntityId)), "The wrong type of EntityId struct is used, probably due to accidentally adding a 'using UnityEngine;' to this file.");
            }
#endif

            public TypeDescriptionEntriesCache(ref IFileReader reader, FieldDescriptionEntriesCache fieldDescriptions, NativeTypeEntriesCache nativeTypes, VirtualMachineInformation vmInfo)
            {
                Count = (int)reader.GetEntryCount(EntryType.TypeDescriptions_TypeIndex);

                TypeDescriptionName = new string[Count];
                Assembly = new string[Count];

                // granted, that capacity is more than likely too much but it beats reallocs. It's trimmed later.
                PureCSharpTypeIndices = new NativeHashSet<int>((int)Math.Max(0, Count - nativeTypes.Count), Allocator.Persistent);

                m_TypeCategory = new DynamicArray<TypeCategory>(Count, Allocator.Persistent, true);
                var unifiedTypeInfoBackingMemory = new DynamicArray<UnifiedType>(Count + nativeTypes.Count, Allocator.Persistent, false);
                var nativeTypeIndex = 0;
                for (long i = Count; i < unifiedTypeInfoBackingMemory.Count; i++, nativeTypeIndex++)
                {
                    unifiedTypeInfoBackingMemory[i] = new UnifiedType(nativeTypes, null, nativeTypeIndex);
                }
                for (long i = 0; i < Count; i++)
                {
                    unifiedTypeInfoBackingMemory[i] = new UnifiedType(nativeTypes, null, CachedSnapshot.NativeTypeEntriesCache.InvalidTypeIndex, (int)i);
                }
                using var unifiedTypeOffsets = new DynamicArray<long>(3, Allocator.Temp);
                var typeInfoSize = 0L;
                unsafe
                {
                    typeInfoSize = sizeof(UnifiedType);
                }
                unifiedTypeOffsets[UnifiedTypeArrayManagedSection] = 0;
                unifiedTypeOffsets[UnifiedTypeArrayNativeSection] = Count * typeInfoSize;
                unifiedTypeOffsets[2] = unifiedTypeInfoBackingMemory.Count * typeInfoSize;
                m_UnifiedTypeInfo = new NestedDynamicArray<UnifiedType>(unifiedTypeOffsets, unifiedTypeInfoBackingMemory);

                if (Count == 0)
                {
                    Flags = new DynamicArray<TypeFlags>(0, Allocator.Persistent);
                    BaseOrElementTypeIndex = new DynamicArray<int>(0, Allocator.Persistent);
                    Size = new DynamicArray<int>(0, Allocator.Persistent);
                    TypeInfoAddress = new DynamicArray<ulong>(0, Allocator.Persistent);
                    TypeInfoToArrayIndex = new AddressToIntIndex(0, Allocator.Persistent);
                    return;
                }

                Flags = reader.Read(EntryType.TypeDescriptions_Flags, 0, Count, Allocator.Persistent).Result.Reinterpret<TypeFlags>();
                BaseOrElementTypeIndex = reader.Read(EntryType.TypeDescriptions_BaseOrElementTypeIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                Size = reader.Read(EntryType.TypeDescriptions_Size, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                TypeInfoAddress = reader.Read(EntryType.TypeDescriptions_TypeInfoAddress, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
#if DEBUG_VALIDATION
                if (reader.FormatVersion == FormatVersion.SnapshotMinSupportedFormatVersion)
                {
                    // Nb! This code is left here for posterity in case anyone wonders what EntryType.TypeDescriptions_TypeIndex is, and if it is needed. No it is not.

                    // After thorough archeological digging, there seems to be no evidence that this array was ever needed
                    // At the latest after FormatVersion.StreamingManagedMemoryCaptureFormatVersion (9) it is definitely not needed
                    // as the indices reported in this map exactly to the indices in the array

                    var TypeIndex = reader.Read(EntryType.TypeDescriptions_TypeIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                    for (int i = 0; i < TypeIndex.Count; i++)
                    {
                        if (i != TypeIndex[i])
                        {
                            Debug.LogError("Attempted to load a broken Snapshot file from an ancient Unity version!");
                            break;
                        }
                    }
                }
#endif

                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.TypeDescriptions_Name, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.TypeDescriptions_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref TypeDescriptionName);

                    tmpSize = reader.GetSizeForEntryRange(EntryType.TypeDescriptions_Assembly, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.TypeDescriptions_Assembly, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref Assembly);
                }

                m_FieldIndicesReadOp = reader.AsyncReadDynamicSizedArray<int>(EntryType.TypeDescriptions_FieldIndices, 0, Count, Allocator.Persistent);

                m_StaticFieldBytesReadOp = reader.AsyncReadDynamicSizedArray<byte>(EntryType.TypeDescriptions_StaticFieldBytes, 0, Count, Allocator.Persistent);


                var typeNameToIndex = new Dictionary<string, int>();
                for (int i = 0; i < Count; ++i)
                {
                    typeNameToIndex[TypeDescriptionName[i]] = i;
                }

                typeNameToIndex.GetOrInitializeValue(UnityObjectTypeName, out ITypeUnityObject, ITypeInvalid);
#if DEBUG_VALIDATION //This shouldn't really happen
                if (ITypeUnityObject == ITypeInvalid)
                {
                    throw new Exception("Unable to find UnityEngine.Object");
                }
#endif
                typeNameToIndex.GetOrInitializeValue(k_SystemValueTypeName, out ITypeValueType, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemObjectTypeName, out ITypeObject, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemEnumTypeName, out ITypeEnum, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemCharTypeName, out ITypeChar, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemCharArrayTypeName, out ITypeCharArray, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemInt16Name, out ITypeInt16, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemInt32Name, out ITypeInt32, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemInt64Name, out ITypeInt64, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemIntPtrName, out ITypeIntPtr, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemVoidPtrName, out ITypeVoidPtr, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemBytePtrName, out ITypeBytePtr, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemStringName, out ITypeString, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemBoolName, out ITypeBool, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemSingleName, out ITypeSingle, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemByteName, out ITypeByte, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemDoubleName, out ITypeDouble, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemUInt16Name, out ITypeUInt16, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemUInt32Name, out ITypeUInt32, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemUInt64Name, out ITypeUInt64, ITypeInvalid);

                typeNameToIndex.GetOrInitializeValue(k_UnityMonoBehaviourTypeName, out ITypeUnityMonoBehaviour, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_UnityScriptableObjectTypeName, out ITypeUnityScriptableObject, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_UnityComponentObjectTypeName, out ITypeUnityComponent, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_UnityGameObjectTypeName, out ITypeUnityGameObject, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_UnityTransformTypeName, out ITypeUnityTransform, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_UnityRectTransformTypeName, out ITypeUnityRectTransform, ITypeInvalid);

                typeNameToIndex.GetOrInitializeValue(k_UnityEditorEditorTypeName, out ITypeUnityEditorEditor, ITypeInvalid);

                m_UninstantiatableUnityBaseTypesWithScriptingDefinedTypes = new NativeHashSet<int>(3, Allocator.Persistent);

                // There will never be instances of objects in the snapshot that have these key Unity native types as
                // while their managed type would be that of the corresponding managed base type
                // (i.e. UnityEngine.ScriptableObject/MonoBehaviour) as only their derived type can be instantiated.
                // As such, the managed types mapped to these native types are also always the managed base types.
                if (ITypeUnityScriptableObject is not ITypeInvalid)
                {
                    UpdateManagedToNativeTypeMapping(nativeTypes, ITypeUnityScriptableObject, nativeTypes.ScriptableObjectIdx, ref m_UninstantiatableUnityBaseTypesWithScriptingDefinedTypes, baseFallbackType: ITypeUnityScriptableObject);
                    // Not actually a discrete managed type so this shouldn't update the managed base type info for ITypeUnityScriptableObject, only the native info
                    UpdateManagedToNativeTypeMapping(nativeTypes, ITypeUnityScriptableObject, nativeTypes.EditorScriptableObjectIdx, baseFallbackType: ITypeUnityScriptableObject);
                }
                if (ITypeUnityMonoBehaviour is not ITypeInvalid)
                    UpdateManagedToNativeTypeMapping(nativeTypes, ITypeUnityMonoBehaviour, nativeTypes.MonoBehaviourIdx, ref m_UninstantiatableUnityBaseTypesWithScriptingDefinedTypes, baseFallbackType: ITypeUnityMonoBehaviour);

                m_UninstantiatableUnityBaseTypesWithSetNativeType = new NativeHashSet<int>(2, Allocator.Persistent);

                if (ITypeUnityComponent is not ITypeInvalid)
                    UpdateManagedToNativeTypeMapping(nativeTypes, ITypeUnityComponent, nativeTypes.ComponentIdx, ref m_UninstantiatableUnityBaseTypesWithSetNativeType, baseFallbackType: ITypeUnityComponent);

                if (ITypeUnityObject is not ITypeInvalid)
                {
                    UpdateManagedToNativeTypeMapping(nativeTypes, ITypeUnityObject, nativeTypes.BaseObjectIdx, ref m_UninstantiatableUnityBaseTypesWithSetNativeType, baseFallbackType: ITypeUnityObject);
                    // Not actually a discrete managed type so this shouldn't update the managed base type info for ITypeUnityComponent, only the native info
                    UpdateManagedToNativeTypeMapping(nativeTypes, ITypeUnityObject, nativeTypes.NamedObjectIdx, baseFallbackType: ITypeUnityObject);
                }

                m_AllUninstantiatableUnityBaseTypes = new NativeHashSet<int>(m_UninstantiatableUnityBaseTypesWithScriptingDefinedTypes.Count() + m_UninstantiatableUnityBaseTypesWithSetNativeType.Count(), Allocator.Persistent);

                foreach (var item in m_UninstantiatableUnityBaseTypesWithScriptingDefinedTypes)
                {
                    m_AllUninstantiatableUnityBaseTypes.Add(item);
                }
                foreach (var item in m_UninstantiatableUnityBaseTypesWithSetNativeType)
                {
                    m_AllUninstantiatableUnityBaseTypes.Add(item);
                }

                // GameObject, Transforms and RectTransforms can be instantiated and can't be more concrete, so we can update their type here and call it a day
                if (ITypeUnityGameObject is not ITypeInvalid)
                    UpdateManagedToNativeTypeMapping(nativeTypes, ITypeUnityGameObject, nativeTypes.GameObjectIdx, baseFallbackType: ITypeUnityGameObject);

                // Note: while having a managed shell type for Transforms is basically guaranteed as soon as there is even one MonoBehaviour in the runtime, the same is not necessarily true for RectTransforms
                if (ITypeUnityTransform is not ITypeInvalid && nativeTypes.TransformIdx is not NativeTypeEntriesCache.InvalidTypeIndex)
                    UpdateManagedToNativeTypeMapping(nativeTypes, ITypeUnityTransform, nativeTypes.TransformIdx, baseFallbackType: ITypeUnityTransform);
                if (ITypeUnityRectTransform is not ITypeInvalid && nativeTypes.RectTransformIdx is not NativeTypeEntriesCache.InvalidTypeIndex)
                    UpdateManagedToNativeTypeMapping(nativeTypes, ITypeUnityRectTransform, nativeTypes.RectTransformIdx, baseFallbackType: ITypeUnityRectTransform);

                InitSecondaryItems(fieldDescriptions, nativeTypes, vmInfo, typeNameToIndex);
            }

            // Check all bases' fields
            public bool HasAnyField(int iType)
            {
                return FieldIndicesInstance[iType].Count > 0 || FieldIndicesStatic[iType].Count > 0;
            }

            // Check all bases' fields
            public bool HasAnyStaticField(int iType)
            {
                return FieldIndicesStatic[iType].Count > 0;
            }

            // Check only the type's fields
            public bool HasStaticField(long iType)
            {
                return FieldIndicesOwnedStatic[iType].Count > 0;
            }

            /// <summary>
            /// Note: A Type may <see cref="HasStaticField"/> but no data for them, presumably because they haven't been initialized yet.
            /// </summary>
            /// <param name="iType"></param>
            /// <returns></returns>
            public bool HasStaticFieldData(long iType)
            {
                return StaticFieldBytes[iType].Count > 0;
            }

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public bool CouldThisFieldBeReadAsAnAddress(long arrayIndex, CachedSnapshot snapshot)
            {
                return (!HasFlag(arrayIndex, TypeFlags.kValueType) ||
                    (FieldIndicesInstance[arrayIndex].Count == 0 && Size[arrayIndex] == snapshot.VirtualMachineInformation.PointerSize));
            }

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public bool HasFlag(long arrayIndex, TypeFlags flag)
            {
                return (Flags[arrayIndex] & flag) == flag;
            }

            public int GetRank(long arrayIndex)
            {
                int r = (int)(Flags[arrayIndex] & TypeFlags.kArrayRankMask) >> 16;
                Checks.IsTrue(r >= 0);
                return r;
            }

            public int TypeInfo2ArrayIndex(ulong aTypeInfoAddress)
            {
                TypeInfoToArrayIndex.GetOrInitializeValue(aTypeInfoAddress, out var i, ITypeInvalid);
                return i;
            }

            static readonly ProfilerMarker k_TypeFieldArraysBuild = new ProfilerMarker("MemoryProfiler.TypeFields.TypeFieldArrayBuilding");

            static NestedDynamicArray<T> ConvertNestedNativeArraysToNestedDynamicArray<T>(NativeArray<NativeArray<T>> source, Allocator allocator) where T : unmanaged
            {
                var count = source.Length;

                // Calculate total element count
                long totalElements = 0;
                for (int i = 0; i < count; i++)
                    totalElements += source[i].Length;

                // Build offsets array (count + 1 entries).
                // Allocate as Temp since it's only needed during construction and disposed off right after.
                using var offsets = new DynamicArray<long>(count + 1, Allocator.Temp);
                long currentOffset = 0;
                for (int i = 0; i < count; i++)
                {
                    offsets[i] = currentOffset;
                    unsafe
                    {
                        currentOffset += source[i].Length * sizeof(T);
                    }
                }
                offsets[count] = currentOffset; // End offset

                // Build flat data array by copying directly from NativeArray
                var data = new DynamicArray<T>(totalElements, allocator);
                unsafe
                {
                    var dataPtr = data.GetUnsafeTypedPtr();
                    for (int i = 0; i < count; i++)
                    {
                        var array = source[i];
                        // MemCopy for efficiency
                        UnsafeUtility.MemCpy(dataPtr, array.GetUnsafePtr(), array.Length * sizeof(T));
                        dataPtr += array.Length;
                    }
                }

                return new NestedDynamicArray<T>(offsets, data);
            }

            enum FieldFindOptions
            {
                OnlyInstance,
                OnlyStatic
            }

            void AllFieldArrayIndexOf(int ITypeArrayIndex, FieldDescriptionEntriesCache fieldDescriptions, FieldFindOptions findOptions, bool includeBase)
            {
                //make sure we clear before we start crawling
                m_FieldProcessingBuffer.Clear();
                RecurseCrawlFields(ITypeArrayIndex, fieldDescriptions, findOptions, includeBase);
            }

            void RecurseCrawlFields(int typeIndex, FieldDescriptionEntriesCache fieldDescriptions, FieldFindOptions fieldFindOptions, bool crawlBase)
            {
                bool isValueType = HasFlag(typeIndex, TypeFlags.kValueType);
                if (crawlBase)
                {
                    int baseTypeIndex = BaseOrElementTypeIndex[typeIndex];
                    if (crawlBase && baseTypeIndex != -1 && !isValueType)
                    {
                        RecurseCrawlFields(baseTypeIndex, fieldDescriptions, fieldFindOptions, true);
                    }
                }

                var fieldIndices = FieldIndices[typeIndex];
                for (int i = 0; i < fieldIndices.Count; ++i)
                {
                    var iField = fieldIndices[i];

                    if (!FieldMatchesOptions(iField, fieldDescriptions, fieldFindOptions))
                        continue;

                    if (fieldDescriptions.TypeIndex[iField] == typeIndex && isValueType)
                    {
                        // this happens in primitive types like System.Single, which is a weird type that has a field of its own type.
                        continue;
                    }

                    if (fieldDescriptions.Offset[iField] == -1) //TODO: verify this assumption
                    {
                        // this is how we encode TLS fields. We don't support TLS fields yet.
                        continue;
                    }

                    m_FieldProcessingBuffer.Add(iField);
                }
            }

            bool FieldMatchesOptions(int fieldIndex, FieldDescriptionEntriesCache fieldDescriptions, FieldFindOptions options)
            {
                if (options == FieldFindOptions.OnlyStatic)
                {
                    return fieldDescriptions.IsStatic[fieldIndex] == 1;
                }
                if (options == FieldFindOptions.OnlyInstance)
                {
                    return fieldDescriptions.IsStatic[fieldIndex] == 0;
                }
                return false;
            }

            void InitSecondaryItems(FieldDescriptionEntriesCache fieldDescriptions, NativeTypeEntriesCache nativeTypes, VirtualMachineInformation vmInfo, Dictionary<string, int> typeNameToIndex)
            {
                TypeInfoToArrayIndex = new AddressToIntIndex((int)TypeInfoAddress.Count, Allocator.Persistent);
                for (int i = 0; i < TypeInfoAddress.Count; i++)
                {
                    TypeInfoToArrayIndex.Add(TypeInfoAddress[i], i);
                }

                using var hashmapOfSaveConcreteTypes = new NativeHashSet<int>(1000, Allocator.Temp);
                hashmapOfSaveConcreteTypes.Add(ITypeObject);
                hashmapOfSaveConcreteTypes.Add(ITypeValueType);

                // Include all Unity Base Types.
                // Inheriting from these doesn't mean the type can't still be an abstract type, but their count is low and the chances of these being concrete types is generally pretty high.
                // The off chance of these being abstract and false flagging them as concrete leading to invalid objects getting crawled due to that flag is worth it over the risk of not crawling
                // a Unity Object derived class due to funky inheritance chain issues.
                // That said, having the actual type flags would be vastly preferable.
                foreach (var item in m_AllUninstantiatableUnityBaseTypes)
                {
                    hashmapOfSaveConcreteTypes.Add(item);
                }
                // It might be that not everyone of these types is present in the snapshot, so in case an invalid one was added, remove it again.
                hashmapOfSaveConcreteTypes.Remove(ITypeInvalid);

                // The kind of types we know about but that don't start with I and a capital letter
                var knownOddInterfaces = new HashSet<int>();
                int getter;
                knownOddInterfaces.Add(typeNameToIndex.TryGetValue("System.Runtime.InteropServices._Attribute", out getter) ? getter : -1);
                knownOddInterfaces.Remove(ITypeInvalid);

                using (k_TypeFieldArraysBuild.Auto())
                {
                    // Use NativeArray of NativeArrays instead of managed jagged arrays
                    var fieldIndicesInstance = new NativeArray<NativeArray<int>>(Count, Allocator.Persistent);
                    var fieldIndicesStatic = new NativeArray<NativeArray<int>>(Count, Allocator.Persistent);
                    var fieldIndicesOwnedStatic = new NativeArray<NativeArray<int>>(Count, Allocator.Persistent);

                    // Use NativeList instead of managed List for processing buffer
                    m_FieldProcessingBuffer = new NativeList<int>(k_DefaultFieldProcessingBufferSize, Allocator.Temp);
                    try
                    {
                        var objectHeaderSize = (int)vmInfo.ObjectHeaderSize;
                        for (int i = 0; i < Count; ++i)
                        {
                            AllFieldArrayIndexOf(i, fieldDescriptions, FieldFindOptions.OnlyInstance, true);
                            var fieldIndicesInstanceArray = new NativeArray<int>(m_FieldProcessingBuffer.Length, Allocator.TempJob);
                            fieldIndicesInstanceArray.CopyFrom(m_FieldProcessingBuffer.AsArray());
                            fieldIndicesInstance[i] = fieldIndicesInstanceArray;

                            AllFieldArrayIndexOf(i, fieldDescriptions, FieldFindOptions.OnlyStatic, true);
                            var fieldIndicesStaticArray = new NativeArray<int>(m_FieldProcessingBuffer.Length, Allocator.TempJob);
                            fieldIndicesStaticArray.CopyFrom(m_FieldProcessingBuffer.AsArray());
                            fieldIndicesStatic[i] = fieldIndicesStaticArray;

                            AllFieldArrayIndexOf(i, fieldDescriptions, FieldFindOptions.OnlyStatic, false);
                            var fieldIndicesOwnedStaticArray = new NativeArray<int>(m_FieldProcessingBuffer.Length, Allocator.TempJob);
                            fieldIndicesOwnedStaticArray.CopyFrom(m_FieldProcessingBuffer.AsArray());
                            fieldIndicesOwnedStatic[i] = fieldIndicesOwnedStaticArray;

                            // There is a bug in the caluclation for the type sizes in the native capture code where it subtracts the header size from the class size for all types.
                            // However, in IL2CPP, the header size is only included if the type derives from object, which abstract types and pointer types (e.g. "void*") don't do.
                            // To fix it in the capture code, we need to expose more type flags and also we still need to be able to read snapshots from versions where it isn't fixed, so
                            // if negative sizes are present, double check that they don't have a base type and if so, assume the bug is present and adjust them accordingly.
                            if (Size[i] < 0)
                            {
                                if (Size[i] < 0 && BaseOrElementTypeIndex[i] < 0)
                                {
#if DEBUG_VALIDATION
                                    // pointers are the only value types that should get a negative size
                                    if (HasFlag(i, TypeFlags.kValueType))
                                        Checks.IsTrue(TypeDescriptionName[i].EndsWith('*'));
#endif
                                    // outside of value types, abstract generic base classes could end up with a negative size
                                    Size[i] += objectHeaderSize;
                                }
#if DEBUG_VALIDATION
                                if (Size[i] < 0)
                                    Debug.LogWarning($"Type {TypeDescriptionName[i]} has a negative size ({Size[i]}).");
#endif
                            }


                            var typeIndex = i;
                            if (DerivesFromTypes(typeIndex, in m_AllUninstantiatableUnityBaseTypes, out var managedBaseType))
                            {
                                UpdateManagedToNativeTypeMapping(nativeTypes, typeIndex, UnifiedTypeInfoManaged[managedBaseType].NativeTypeIndex, baseFallbackType: managedBaseType);
                            }
                            else
                            {
                                PureCSharpTypeIndices.Add(typeIndex);
                                var iTypeDescription = typeIndex;
                                var isConcrete = false;
                                while (iTypeDescription > ITypeInvalid)
                                {
                                    if (hashmapOfSaveConcreteTypes.Contains(iTypeDescription) ||
                                        m_TypeCategory[typeIndex] == TypeCategory.Concrete || HasFlag(iTypeDescription, TypeFlags.kArray) || HasFlag(iTypeDescription, TypeFlags.kValueType))
                                    {
                                        isConcrete = true;
                                        break;
                                    }
                                    iTypeDescription = BaseOrElementTypeIndex[iTypeDescription];
                                }
                                if (isConcrete)
                                {
                                    do
                                    {
                                        m_TypeCategory[typeIndex] = TypeCategory.Concrete;
                                        // go over all types between this type and the one that proofed this was derived from object and set them as well
                                        if (typeIndex == iTypeDescription)
                                            break;
                                        typeIndex = BaseOrElementTypeIndex[typeIndex];
                                    } while (typeIndex > ITypeInvalid);
                                }
                                else
                                {
                                    if (Size[typeIndex] == vmInfo.ObjectHeaderSize
                                        && FieldIndices[typeIndex].Count == 0)
                                    {
                                        var name = TypeDescriptionName[typeIndex].AsSpan();
                                        var genericBracket = name.IndexOf('<');
                                        if (genericBracket >= 0)
                                            name = name.Slice(0, genericBracket);
                                        var lastDot = name.LastIndexOf('.');
                                        if (lastDot >= 0)
                                            name = name.Slice(lastDot + 1);
                                        if (name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]) || knownOddInterfaces.Contains(typeIndex))
                                        {
                                            // This is super likely to be an interface, it can't have instances on the heap.
                                            m_TypeCategory[typeIndex] = TypeCategory.AbstractInterface;
                                        }
                                        else
                                        {
                                            // There is a veeeeery high chance that this is an abstract class which can't have instances on the heap.
                                            // or an interface not following the IInterface naming convention
                                            // however, it could still be concrete and inheriting from a generic base that wasn't reported, so categorize it as Unlcear we can't be sure and therefore shouldn't ignore it.
                                            m_TypeCategory[typeIndex] = TypeCategory.IgnoreForHeapObjectTypeChecks;
                                        }
                                    }
                                    else
                                    {
                                        bool EndOfGeneric(char c) => c == '>' || c == ',';
                                        var name = TypeDescriptionName[typeIndex].AsSpan();
                                        var genericBracket = name.IndexOf("<T");
                                        if (genericBracket >= 0 && name.Length > genericBracket + 1 &&
                                            (EndOfGeneric(name[genericBracket + 1]) ||
                                            (char.IsUpper(name[genericBracket + 1]) && (name.Length > genericBracket + 2) && (char.IsLower(name[genericBracket + 2]) || EndOfGeneric(name[genericBracket + 2])))))
                                        {
                                            // Pretty sure this is an abstract generic class, it can't have instances on the heap.
                                            m_TypeCategory[typeIndex] = TypeCategory.AbstractGeneric;
                                        }
                                        else
                                        {
                                            // We can't be sure if this is abstract or not, it might be concrete but inheriting from a generic base that wasn't reported
                                            m_TypeCategory[typeIndex] = TypeCategory.IgnoreForHeapObjectTypeChecks;
                                        }
                                    }
                                }
                            }
                        }

                        // Convert NativeArray<NativeList> to NestedDynamicArray
                        m_FieldIndicesInstance = ConvertNestedNativeArraysToNestedDynamicArray(fieldIndicesInstance, Allocator.Persistent);
                        m_FieldIndicesStatic = ConvertNestedNativeArraysToNestedDynamicArray(fieldIndicesStatic, Allocator.Persistent);
                        m_FieldIndicesOwnedStatic = ConvertNestedNativeArraysToNestedDynamicArray(fieldIndicesOwnedStatic, Allocator.Persistent);
                    }
                    finally
                    {
                        // Dispose all NativeArrays
                        for (int i = 0; i < Count; i++)
                        {
                            if (fieldIndicesInstance[i].IsCreated)
                                fieldIndicesInstance[i].Dispose();
                            if (fieldIndicesStatic[i].IsCreated)
                                fieldIndicesStatic[i].Dispose();
                            if (fieldIndicesOwnedStatic[i].IsCreated)
                                fieldIndicesOwnedStatic[i].Dispose();
                        }
                        fieldIndicesInstance.Dispose();
                        fieldIndicesStatic.Dispose();
                        fieldIndicesOwnedStatic.Dispose();
                        m_FieldProcessingBuffer.Dispose();
                    }
                }
                var fieldIndices = FieldIndices[ITypeUnityObject];
                long fieldIndicesIndex = -1;
                for (long i = 0; i < fieldIndices.Count; i++)
                {
                    if (fieldDescriptions.FieldDescriptionName[fieldIndices[i]] == UnityNativeObjectPointerFieldName)
                    {
                        fieldIndicesIndex = i;
                        break;
                    }
                }

                IFieldUnityObjectMCachedPtr = fieldIndicesIndex >= 0 ? FieldIndices[ITypeUnityObject][fieldIndicesIndex] : -1;

                IFieldUnityObjectMCachedPtrOffset = -1;

                if (IFieldUnityObjectMCachedPtr >= 0)
                {
                    IFieldUnityObjectMCachedPtrOffset = fieldDescriptions.Offset[IFieldUnityObjectMCachedPtr];
                }
#if UNMANAGED_NATIVE_HASHMAP_AVAILABLE
                PureCSharpTypeIndices.TrimExcess();
#endif

#if DEBUG_VALIDATION
                if (IFieldUnityObjectMCachedPtrOffset < 0)
                {
                    Debug.LogWarning("Could not find unity object instance id field or m_CachedPtr");
                    return;
                }
#endif
            }

            /// <summary>
            /// Completes any pending async read operations.
            /// Used for accurate timing when profiling snapshot loading.
            /// </summary>
            public void CompleteAsyncReadOperations()
            {
                // Force completion of async field indices read
                if (m_FieldIndicesReadOp.IsCreated)
                {
                    _ = FieldIndices; // Accessing the property forces completion
                }
                // Force completion of async static field bytes read
                if (m_StaticFieldBytesReadOp.IsCreated)
                {
                    _ = StaticFieldBytes; // Accessing the property forces completion
                }
            }

            void UpdateManagedToNativeTypeMapping(CachedSnapshot.NativeTypeEntriesCache nativeTypes, int managedType, int nativeType, ref NativeHashSet<int> hashmapOfUnityBaseTypes, int baseFallbackType = CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid)
            {
                UpdateManagedToNativeTypeMapping(nativeTypes, managedType, nativeType, baseFallbackType);
                hashmapOfUnityBaseTypes.Add(managedType);
            }

            public void UpdateManagedToNativeTypeMapping(CachedSnapshot.NativeTypeEntriesCache nativeTypes, int managedType, int nativeType, int baseFallbackType = CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid)
            {
                // Updating the mapping only makes sense if at least the managed types are valid
                Checks.IsTrue(managedType is not ITypeInvalid);

                // Updating the type means we sorta confirmed it is concrete
                // (As we currently assume that all Unity Types are concrete, which isn't necessarily the case but probable enough.
                // In the future we'd ideally be reporting type flags, but also, we're currently only using these to determine
                // if reading some heap bytes (pointed to from somewhere else) as a pointer to a type could reasonably be
                // an object header for a heap object. Since only concrete types could have objects on the heap,
                // including slightly more types than necessary as concrete is preferable to falsely excluding some.)
                m_TypeCategory[managedType] = TypeCategory.Concrete;

                var previousBaseType = UnifiedTypeInfoManaged[managedType].ManagedBaseTypeIndexForNativeType;
                var setFallBackType = baseFallbackType is not CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid;
                var onlyUpdateNativeType = false;

                // if we are setting a base type, only update info if it wasn't already set or the new base type is more specific
                if (setFallBackType && previousBaseType is not CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid
                    && (previousBaseType == baseFallbackType || !DerivesFrom(baseFallbackType, previousBaseType, true)))
                {
                    if (nativeType is not NativeTypeEntriesCache.InvalidTypeIndex)
                        previousBaseType = UnifiedTypeInfoNative[nativeType].ManagedBaseTypeIndexForNativeType;
                    // Check if we also do not need to update the native type info
                    if (previousBaseType is not CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid
                        && (previousBaseType == baseFallbackType || !DerivesFrom(baseFallbackType, previousBaseType, true)))
                        return;
                    onlyUpdateNativeType = true;
                }

                // Only update the fallback base type if it wasn't previously a Unity scripting type.
                // All Unity scripting types should get initialized correctly to their fallback types through InitSecondaryItems
                // And subsequently should not be updated to be more specific, as they have a multiple managed to one native(& managed base type) relationship.
                // Other base types such as UnityEngine.Object or Component have specific scripting types that map to their native types 1-to-1
                // so those should get updated to be more specifc.
                var updateFallbackType = setFallBackType && !UninstantiatableUnityBaseTypesWithScriptingDefinedTypes.Contains(previousBaseType);
                var fallbackTypeToSet = updateFallbackType ? baseFallbackType : previousBaseType;

                if (nativeType is not NativeTypeEntriesCache.InvalidTypeIndex
                    && updateFallbackType && UnifiedTypeInfoNative[nativeType].ManagedBaseTypeIndexForNativeType != fallbackTypeToSet)
                {
                    // The native type info never gets a more concrete managed type linked up to it than the base type.
                    UnifiedTypeInfoNative[nativeType] = new UnifiedType(nativeTypes, this, nativeType, fallbackTypeToSet, managedBaseTypeIndex: fallbackTypeToSet);
                }

                if (!onlyUpdateNativeType)
                {
                    // updating the managed type with a new native type mapping shouldn't change the managed type
                    Checks.CheckEquals(managedType, UnifiedTypeInfoManaged[managedType].ManagedTypeIndex);
                    UnifiedTypeInfoManaged[managedType] = new UnifiedType(nativeTypes, this, nativeType, managedType, managedBaseTypeIndex: fallbackTypeToSet);
                }

            }

            /// <summary>
            /// Past <see cref="InitSecondaryItems(FieldDescriptionEntriesCache, NativeTypeEntriesCache, VirtualMachineInformation, Dictionary{string, int})"/> being done, <see cref="DerivesFromUnityObject"/> should be used instead.
            /// </summary>
            /// <param name="iTypeDescription"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            private bool DerivesFromTypes(int iTypeDescription, in NativeHashSet<int> baseTypes, out int managedBaseType)
            {
                managedBaseType = ITypeInvalid;
                while (!baseTypes.Contains(iTypeDescription) && iTypeDescription >= 0)
                {
                    if (HasFlag(iTypeDescription, TypeFlags.kArray))
                        return false;
                    iTypeDescription = BaseOrElementTypeIndex[iTypeDescription];
                }
                if (baseTypes.Contains(iTypeDescription))
                {
                    managedBaseType = iTypeDescription;
                    return true;
                }
                return false;
            }

            /// <summary>
            /// After <see cref="InitSecondaryItems(FieldDescriptionEntriesCache, VirtualMachineInformation, Dictionary{string, int})"/>,
            /// this is the quickest way to check if a type derives from UnityObject.
            /// Also, <see cref="Managed.ManagedDataCrawler.ConnectNativeToManageObject(Managed.ManagedDataCrawler.IntermediateCrawlData)"/>
            /// adds further types to this can can't be checked with <see cref="DerivesFromTypes"/> as their managed base type is not properly reported,
            /// e.g. due to the use of a generic like ScriptableSingleton<T>.
            /// </summary>
            /// <param name="iTypeDescription"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public bool DerivesFromUnityObject(int iTypeDescription)
            {
                return UnifiedTypeInfoManaged[iTypeDescription].IsUnityObjectType;
            }

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public bool DerivesFrom(int iTypeDescription, int potentialBase, bool excludeArrayElementBaseTypes)
            {
                while (iTypeDescription != potentialBase && iTypeDescription >= 0)
                {
                    if (excludeArrayElementBaseTypes && HasFlag(iTypeDescription, TypeFlags.kArray))
                        return false;
                    iTypeDescription = BaseOrElementTypeIndex[iTypeDescription];
                }

                return iTypeDescription == potentialBase;
            }


            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public bool IsNativeContainerType(int managedTypeIndex)
            {
                return TypeDescriptionName[managedTypeIndex].StartsWith(NativeCollectionsNamspaceAndTypePrefix)
                    // only accept types that end on a generic bracket.
                    // E.g. Avoid parsing Unity.Collections.NativeArray<System.Int32>[] as a native collection
                    && TypeDescriptionName[managedTypeIndex].EndsWith('>');
            }

            public void Dispose()
            {
                Count = 0;
                Flags.Dispose();
                BaseOrElementTypeIndex.Dispose();
                Size.Dispose();
                TypeInfoAddress.Dispose();
                TypeDescriptionName = null;
                Assembly = null;
                if (m_FieldIndicesReadOp.IsCreated)
                {
                    // Dispose the read operation first to abort it ...
                    m_FieldIndicesReadOp.Dispose();
                    // ... before disposing the result, as otherwise we'd sync on a pending read op.
                    FieldIndices.Dispose();
                    m_FieldIndicesReadOp = default;
                }
                if (m_StaticFieldBytesReadOp.IsCreated)
                {
                    // Dispose the read operation first to abort it ...
                    m_StaticFieldBytesReadOp.Dispose();
                    // ... before disposing the result, as otherwise we'd sync on a pending read op.
                    StaticFieldBytes.Dispose();
                    m_StaticFieldBytesReadOp = default;
                }

                if (m_FieldIndicesInstance.IsCreated)
                    m_FieldIndicesInstance.Dispose();
                if (m_FieldIndicesStatic.IsCreated)
                    m_FieldIndicesStatic.Dispose();
                if (m_FieldIndicesOwnedStatic.IsCreated)
                    m_FieldIndicesOwnedStatic.Dispose();

                if (m_TypeCategory.IsCreated)
                    m_TypeCategory.Dispose();
                if (m_UnifiedTypeInfo.IsCreated)
                    m_UnifiedTypeInfo.Dispose();
                if (TypeInfoToArrayIndex.IsCreated)
                    TypeInfoToArrayIndex.Dispose();
                if (PureCSharpTypeIndices.IsCreated)
                    PureCSharpTypeIndices.Dispose();
                if (m_AllUninstantiatableUnityBaseTypes.IsCreated)
                    m_AllUninstantiatableUnityBaseTypes.Dispose();
                if (m_UninstantiatableUnityBaseTypesWithScriptingDefinedTypes.IsCreated)
                    m_UninstantiatableUnityBaseTypesWithScriptingDefinedTypes.Dispose();
                if (m_UninstantiatableUnityBaseTypesWithSetNativeType.IsCreated)
                    m_UninstantiatableUnityBaseTypesWithSetNativeType.Dispose();
            }
        }
    }
}

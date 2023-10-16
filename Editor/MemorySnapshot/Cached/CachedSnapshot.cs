using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Containers.Unsafe;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.MemoryProfiler.Editor.Extensions;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using Unity.Profiling;
using Unity.Profiling.Memory;
using UnityEditor;
using UnityEngine;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal static class TypeTools
    {
        public enum FieldFindOptions
        {
            OnlyInstance,
            OnlyStatic
        }

        static void RecurseCrawlFields(ref List<int> fieldsBuffer, int typeIndex, TypeDescriptionEntriesCache typeDescriptions, FieldDescriptionEntriesCache fieldDescriptions, FieldFindOptions fieldFindOptions, bool crawlBase)
        {
            bool isValueType = typeDescriptions.HasFlag(typeIndex, TypeFlags.kValueType);
            if (crawlBase)
            {
                int baseTypeIndex = typeDescriptions.BaseOrElementTypeIndex[typeIndex];
                if (crawlBase && baseTypeIndex != -1 && !isValueType)
                {
                    RecurseCrawlFields(ref fieldsBuffer, baseTypeIndex, typeDescriptions, fieldDescriptions, fieldFindOptions, true);
                }
            }


            var fieldIndices = typeDescriptions.FieldIndices[typeIndex];
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

                fieldsBuffer.Add(iField);
            }
        }

        public static void AllFieldArrayIndexOf(ref List<int> fieldsBuffer, int ITypeArrayIndex, TypeDescriptionEntriesCache typeDescriptions, FieldDescriptionEntriesCache fieldDescriptions, FieldFindOptions findOptions, bool includeBase)
        {
            //make sure we clear before we start crawling
            fieldsBuffer.Clear();
            RecurseCrawlFields(ref fieldsBuffer, ITypeArrayIndex, typeDescriptions, fieldDescriptions, findOptions, includeBase);
        }

        static bool FieldMatchesOptions(int fieldIndex, FieldDescriptionEntriesCache fieldDescriptions, FieldFindOptions options)
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
    }

    internal class CachedSnapshot : IDisposable
    {
        public bool Valid { get { return !m_Disposed && CrawledData.Crawled; } }
        bool m_Disposed = false;

        FormatVersion m_SnapshotVersion;

        public bool HasConnectionOverhaul
        {
            get { return m_SnapshotVersion >= FormatVersion.NativeConnectionsAsInstanceIdsVersion; }
        }

        public bool HasTargetAndMemoryInfo
        {
            get { return m_SnapshotVersion >= FormatVersion.ProfileTargetInfoAndMemStatsVersion; }
        }

        public bool HasMemoryLabelSizesAndGCHeapTypes
        {
            get { return m_SnapshotVersion >= FormatVersion.MemLabelSizeAndHeapIdVersion; }
        }

        public bool HasSceneRootsAndAssetbundles
        {
            get { return m_SnapshotVersion >= FormatVersion.SceneRootsAndAssetBundlesVersion; }
        }

        public bool HasGfxResourceReferencesAndAllocators
        {
            get { return m_SnapshotVersion >= FormatVersion.GfxResourceReferencesAndAllocatorsVersion; }
        }

        public bool HasNativeObjectMetaData
        {
            get { return m_SnapshotVersion >= FormatVersion.NativeObjectMetaDataVersion; }
        }

        public bool HasSystemMemoryRegionsInfo
        {
            get { return (m_SnapshotVersion >= FormatVersion.SystemMemoryRegionsVersion) && (SystemMemoryRegions.Count > 0); }
        }

        public bool HasSystemMemoryResidentPages
        {
            get { return (m_SnapshotVersion >= FormatVersion.SystemMemoryResidentPagesVersion) && (SystemMemoryResidentPages.Count > 0); }
        }

        public ManagedData CrawledData { internal set; get; }

        public class NativeAllocationSiteEntriesCache : IDisposable
        {
            public long Count;
            public DynamicArray<long> id = default;
            public DynamicArray<int> memoryLabelIndex = default;

            public NestedDynamicArray<ulong> callstackSymbols => m_callstackSymbolsReadOp?.CompleteReadAndGetNestedResults() ?? default;
            NestedDynamicSizedArrayReadOperation<ulong> m_callstackSymbolsReadOp;

            unsafe public NativeAllocationSiteEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeAllocationSites_Id);

                if (Count == 0)
                    return;

                id = reader.Read(EntryType.NativeAllocationSites_Id, 0, Count, Allocator.Persistent).Result.Reinterpret<long>();
                memoryLabelIndex = reader.Read(EntryType.NativeAllocationSites_MemoryLabelIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();

                m_callstackSymbolsReadOp = reader.AsyncReadDynamicSizedArray<ulong>(EntryType.NativeAllocationSites_CallstackSymbols, 0, Count, Allocator.Persistent);
            }

            public string GetReadableCallstackForId(NativeCallstackSymbolEntriesCache symbols, long id)
            {
                long entryIdx = -1;
                for (long i = 0; i < this.id.Count; ++i)
                {
                    if (this.id[i] == id)
                    {
                        entryIdx = i;
                        break;
                    }
                }

                return entryIdx < 0 ? string.Empty : GetReadableCallstack(symbols, entryIdx);
            }

            public string GetReadableCallstack(NativeCallstackSymbolEntriesCache symbols, long idx)
            {
                string readableStackTrace = "";

                var callstackSymbols = this.callstackSymbols[idx];

                for (int i = 0; i < callstackSymbols.Count; ++i)
                {
                    long symbolIdx = -1;
                    ulong targetSymbol = callstackSymbols[i];
                    for (int j = 0; j < symbols.Symbol.Count; ++i)
                    {
                        if (symbols.Symbol[j] == targetSymbol)
                        {
                            symbolIdx = i;
                            break;
                        }
                    }

                    if (symbolIdx < 0)
                        readableStackTrace += "<unknown>\n";
                    else
                        readableStackTrace += symbols.ReadableStackTrace[symbolIdx];
                }

                return readableStackTrace;
            }

            public void Dispose()
            {
                id.Dispose();
                memoryLabelIndex.Dispose();
                if (m_callstackSymbolsReadOp != null)
                {
                    callstackSymbols.Dispose();
                    m_callstackSymbolsReadOp.Dispose();
                    m_callstackSymbolsReadOp = null;
                }
                Count = 0;
            }
        }

        public class NativeRootReferenceEntriesCache : IDisposable
        {
            public long Count;
            public DynamicArray<long> Id = default;
            public DynamicArray<ulong> AccumulatedSize = default;
            public string[] AreaName;
            public string[] ObjectName;
            public Dictionary<long, long> IdToIndex;
            public readonly ulong ExecutableAndDllsReportedValue;
            public const string ExecutableAndDllsRootReferenceName = "ExecutableAndDlls";
            readonly long k_ExecutableAndDllsRootReferenceIndex = -1;

            public NativeRootReferenceEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeRootReferences_Id);

                AreaName = new string[Count];
                ObjectName = new string[Count];

                IdToIndex = new Dictionary<long, long>((int)Count);

                if (Count == 0)
                    return;

                Id = reader.Read(EntryType.NativeRootReferences_Id, 0, Count, Allocator.Persistent).Result.Reinterpret<long>();
                AccumulatedSize = reader.Read(EntryType.NativeRootReferences_AccumulatedSize, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();

                using (var tmpBuffer = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeRootReferences_AreaName, 0, Count);
                    tmpBuffer.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeRootReferences_AreaName, tmpBuffer, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmpBuffer, ref AreaName);

                    tmpSize = reader.GetSizeForEntryRange(EntryType.NativeRootReferences_ObjectName, 0, Count);
                    tmpBuffer.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeRootReferences_ObjectName, tmpBuffer, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmpBuffer, ref ObjectName);
                }
                for (long i = 0; i < Count; i++)
                {
                    if (k_ExecutableAndDllsRootReferenceIndex == -1 && ObjectName[i] == ExecutableAndDllsRootReferenceName)
                    {
                        k_ExecutableAndDllsRootReferenceIndex = i;
                        ExecutableAndDllsReportedValue = AccumulatedSize[i];
                    }
                    IdToIndex.Add(Id[i], i);
                }
            }

            public void Dispose()
            {
                Id.Dispose();
                AccumulatedSize.Dispose();
                Count = 0;
                AreaName = null;
                ObjectName = null;
                IdToIndex = null;
            }
        }

        public class NativeMemoryRegionEntriesCache : IDisposable
        {
            public long Count;
            public string[] MemoryRegionName;
            public DynamicArray<int> ParentIndex = default;
            public DynamicArray<ulong> AddressBase = default;
            public DynamicArray<ulong> AddressSize = default;
            public DynamicArray<int> FirstAllocationIndex = default;
            public DynamicArray<int> NumAllocations = default;
            public readonly bool UsesDynamicHeapAllocator = false;
            public readonly bool UsesSystemAllocator;
            public readonly long SwitchGPUAllocatorIndex = -1;

            const string k_DynamicHeapAllocatorName = "ALLOC_DEFAULT_MAIN";
            const string k_SwitchGPUAllocatorName = "ALLOC_GPU";

            public NativeMemoryRegionEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeMemoryRegions_AddressBase);
                MemoryRegionName = new string[Count];

                if (Count == 0)
                    return;

                ParentIndex = reader.Read(EntryType.NativeMemoryRegions_ParentIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                AddressBase = reader.Read(EntryType.NativeMemoryRegions_AddressBase, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                AddressSize = reader.Read(EntryType.NativeMemoryRegions_AddressSize, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                FirstAllocationIndex = reader.Read(EntryType.NativeMemoryRegions_FirstAllocationIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                NumAllocations = reader.Read(EntryType.NativeMemoryRegions_NumAllocations, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();

                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeMemoryRegions_Name, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeMemoryRegions_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref MemoryRegionName);
                }

                for (long i = 0; i < Count; i++)
                {
                    if (!UsesDynamicHeapAllocator && AddressSize[i] > 0 && MemoryRegionName[i].StartsWith(k_DynamicHeapAllocatorName))
                    {
                        UsesDynamicHeapAllocator = true;
                    }

                    if (SwitchGPUAllocatorIndex == -1 && MemoryRegionName[i].Equals(k_SwitchGPUAllocatorName))
                    {
                        SwitchGPUAllocatorIndex = i;
                    }

                    // Nothing left to check if we've found an instance of both
                    if (UsesDynamicHeapAllocator && SwitchGPUAllocatorIndex != -1)
                        break;
                }
                if (Count > 0)
                    UsesSystemAllocator = !UsesDynamicHeapAllocator;
            }

            public void Dispose()
            {
                Count = 0;
                MemoryRegionName = null;
                ParentIndex.Dispose();
                AddressBase.Dispose();
                AddressSize.Dispose();
                FirstAllocationIndex.Dispose();
                NumAllocations.Dispose();
            }
        }

        public class NativeMemoryLabelEntriesCache : IDisposable
        {
            public long Count;
            public string[] MemoryLabelName;
            public DynamicArray<ulong> MemoryLabelSizes = default;

            public NativeMemoryLabelEntriesCache(ref IFileReader reader, bool hasLabelSizes)
            {
                Count = reader.GetEntryCount(EntryType.NativeMemoryLabels_Name);
                MemoryLabelName = new string[Count];

                if (Count == 0)
                    return;

                if (hasLabelSizes)
                    MemoryLabelSizes = reader.Read(EntryType.NativeMemoryLabels_Size, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();

                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeMemoryLabels_Name, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeMemoryLabels_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref MemoryLabelName);
                }
            }

            public void Dispose()
            {
                Count = 0;
                MemoryLabelSizes.Dispose();
                MemoryLabelName = null;
            }

            public ulong GetLabelSize(string label)
            {
                if (Count <= 0)
                    return 0;

                var labelIndex = Array.IndexOf(MemoryLabelName, label);
                if (labelIndex == -1)
                    return 0;

                return MemoryLabelSizes[labelIndex];
            }
        }

        public class NativeCallstackSymbolEntriesCache : IDisposable
        {
            public long Count;
            public DynamicArray<ulong> Symbol = default;
            public string[] ReadableStackTrace;

            public NativeCallstackSymbolEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeCallstackSymbol_Symbol);
                ReadableStackTrace = new string[Count];

                if (Count == 0)
                    return;

                Symbol = reader.Read(EntryType.NativeCallstackSymbol_Symbol, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeCallstackSymbol_ReadableStackTrace, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeCallstackSymbol_ReadableStackTrace, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref ReadableStackTrace);
                }
            }

            public void Dispose()
            {
                Count = 0;
                Symbol.Dispose();
                ReadableStackTrace = null;
            }
        }

        public class NativeAllocationEntriesCache : IDisposable
        {
            public long Count;
            public DynamicArray<int> MemoryRegionIndex = default;
            public DynamicArray<long> RootReferenceId = default;
            public DynamicArray<ulong> Address = default;
            public DynamicArray<ulong> Size = default;
            public DynamicArray<int> OverheadSize = default;
            public DynamicArray<int> PaddingSize = default;
            public DynamicArray<long> AllocationSiteId = default;

            public NativeAllocationEntriesCache(ref IFileReader reader, bool allocationSites /*do not read allocation sites if they aren't present*/)
            {
                Count = reader.GetEntryCount(EntryType.NativeAllocations_Address);

                if (Count == 0)
                    return;

                MemoryRegionIndex = reader.Read(EntryType.NativeAllocations_MemoryRegionIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                RootReferenceId = reader.Read(EntryType.NativeAllocations_RootReferenceId, 0, Count, Allocator.Persistent).Result.Reinterpret<long>();
                Address = reader.Read(EntryType.NativeAllocations_Address, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                Size = reader.Read(EntryType.NativeAllocations_Size, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                OverheadSize = reader.Read(EntryType.NativeAllocations_OverheadSize, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                PaddingSize = reader.Read(EntryType.NativeAllocations_PaddingSize, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();

                if (allocationSites)
                    AllocationSiteId = reader.Read(EntryType.NativeAllocations_AllocationSiteId, 0, Count, Allocator.Persistent).Result.Reinterpret<long>();
            }

            public void Dispose()
            {
                Count = 0;
                MemoryRegionIndex.Dispose();
                RootReferenceId.Dispose();
                Address.Dispose();
                Size.Dispose();
                OverheadSize.Dispose();
                PaddingSize.Dispose();
                AllocationSiteId.Dispose();
            }
        }

        public unsafe class NativeTypeEntriesCache : IDisposable
        {
            public long Count;
            public string[] TypeName;
            public DynamicArray<int> NativeBaseTypeArrayIndex = default;
            const string k_Transform = "Transform";
            public int TransformIdx { get; private set; } = -1;

            const string k_GameObject = "GameObject";
            public int GameObjectIdx { get; private set; } = -1;

            const string k_MonoBehaviour = "MonoBehaviour";
            public int MonoBehaviourIdx { get; private set; } = -1;

            const string k_Component = "Component";
            public int ComponentIdx { get; private set; } = -1;

            const string k_ScriptableObject = "ScriptableObject";
            const int k_ScriptableObjectDefaultTypeArrayIndexOffsetFromEnd = 2;
            public int ScriptableObjectIdx { get; private set; } = -1;

            const string k_EditorScriptableObject = "EditorScriptableObject";
            public int EditorScriptableObjectIdx { get; private set; } = -1;
            const int k_EditorScriptableObjectDefaultTypeArrayIndexOffsetFromEnd = 1;

            public NativeTypeEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeTypes_Name);
                TypeName = new string[Count];

                if (Count == 0)
                    return;

                NativeBaseTypeArrayIndex = reader.Read(EntryType.NativeTypes_NativeBaseTypeArrayIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeTypes_Name, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeTypes_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref TypeName);
                }

                TransformIdx = Array.FindIndex(TypeName, x => x == k_Transform);
                GameObjectIdx = Array.FindIndex(TypeName, x => x == k_GameObject);
                MonoBehaviourIdx = Array.FindIndex(TypeName, x => x == k_MonoBehaviour);
                ComponentIdx = Array.FindIndex(TypeName, x => x == k_Component);

                // for the fakable types ScriptableObject and EditorScriptable Objects, with the current backend, Array.FindIndex is always going to hit the worst case
                // in the current format, these types are always added last. Assume that for speed, keep Array.FindIndex as fallback in case the format changes
                ScriptableObjectIdx = FindTypeWithHint(k_ScriptableObject, Count - k_ScriptableObjectDefaultTypeArrayIndexOffsetFromEnd);
                EditorScriptableObjectIdx = FindTypeWithHint(k_EditorScriptableObject, Count - k_EditorScriptableObjectDefaultTypeArrayIndexOffsetFromEnd);
            }

            int FindTypeWithHint(string typeName, long hintAtLikelyIndex)
            {
                if (TypeName[hintAtLikelyIndex] == typeName)
                    return (int)hintAtLikelyIndex;
                else
                    return Array.FindIndex(TypeName, x => x == typeName);
            }

            public bool DerivesFrom(int typeIndexToCheck, int baseTypeToCheckAgainst)
            {
                while (typeIndexToCheck != baseTypeToCheckAgainst && NativeBaseTypeArrayIndex[typeIndexToCheck] >= 0)
                {
                    typeIndexToCheck = NativeBaseTypeArrayIndex[typeIndexToCheck];
                }
                return typeIndexToCheck == baseTypeToCheckAgainst;
            }

            public void Dispose()
            {
                Count = 0;
                NativeBaseTypeArrayIndex.Dispose();
                TypeName = null;
            }
        }

        public unsafe class SceneRootEntriesCache : IDisposable
        {
            // the number of scenes
            public long Count;
            // the asset paths for the scenes
            public string[] AssetPath;
            // the scene names
            public string[] Name;
            //the paths to the scenes in the project
            public string[] Path;
            // the scene build index
            public DynamicArray<int> BuildIndex = default;
            //the number of roots in each of the scenes
            public DynamicArray<int> RootCounts = default;
            // each scenes offset into the main roots list
            public DynamicArray<int> RootOffsets = default;
            // first index is for the scene then the second is the array of ids for that scene
            public int[][] SceneIndexedRootTransformInstanceIds;
            public int[][] SceneIndexedRootGameObjectInstanceIds;
            // all of the root transform instance ids
            public DynamicArray<int> AllRootTransformInstanceIds = default;
            // all of the root gameobject instance ids
            public DynamicArray<int> AllRootGameObjectInstanceIds = default;
            // hash set of the ids to avoid duplication ( not sure we really need this)
            public HashSet<int> RootTransformInstanceIdHashSet = default;
            public HashSet<int> RootGameObjectInstanceIdHashSet = default;
            // tree structures for each scene of the transforms and gameobjects so that we can lookup the structure easily
            public TransformTree[] SceneHierarchies;

            public class TransformTree
            {
                public int InstanceID { get; private set; } = NativeObjectEntriesCache.InstanceIDNone;
                public int GameObjectID { get; set; } = NativeObjectEntriesCache.InstanceIDNone;
                public TransformTree Parent = null;

                static ReadOnlyCollection<TransformTree> s_EmptyList = new List<TransformTree>().AsReadOnly();
                List<TransformTree> m_Children = null;
                /// <summary>
                /// Use <see cref="AddChild(int)"/> or <see cref="AddChildren(ICollection{int})"/> to add child Transforms instead of adding to this list directly.
                /// </summary>
                public ReadOnlyCollection<TransformTree> Children => m_Children?.AsReadOnly() ?? s_EmptyList;

                public bool IsScene { get; private set; } = false;

                public TransformTree(bool isScene)
                {
                    IsScene = isScene;
                }

                public TransformTree(int instanceId)
                {
                    InstanceID = instanceId;
                }

                public void AddChild(int instanceId)
                {
                    // only a parent (aka Scene at the root) is allowed to have an invalid instance ID
                    // no recursion or self references are allowed either
                    if (instanceId == NativeObjectEntriesCache.InstanceIDNone
                        || instanceId == InstanceID
                        || (Parent != null && Parent.InstanceID == instanceId))
                        return;

                    var child = new TransformTree(instanceId);
                    child.Parent = this;
                    if (m_Children == null)
                        m_Children = new List<TransformTree>() { child };
                    else
                        m_Children.Add(child);
                }

                public void AddChildren(ICollection<int> instanceIds)
                {
                    foreach (var instanceId in instanceIds)
                    {
                        AddChild(instanceId);
                    }
                }
            }


            public SceneRootEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.SceneObjects_Name);
                AssetPath = new string[Count];
                Name = new string[Count];
                Path = new string[Count];


                if (Count == 0)
                    return;

                using (var tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.SceneObjects_Name, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.SceneObjects_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref Name);
                }

                using (var tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.SceneObjects_Path, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.SceneObjects_Path, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref Path);
                }

                BuildIndex = reader.Read(EntryType.SceneObjects_BuildIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();

                using (var tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.SceneObjects_AssetPath, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.SceneObjects_AssetPath, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref AssetPath);
                }

                SceneIndexedRootTransformInstanceIds = new int[Count][];
                var rootCount = reader.GetEntryCount(EntryType.SceneObjects_RootIds);
                RootCounts = reader.Read(EntryType.SceneObjects_RootIdCounts, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                RootOffsets = reader.Read(EntryType.SceneObjects_RootIdOffsets, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();

                AllRootTransformInstanceIds = reader.Read(EntryType.SceneObjects_RootIds, 0, rootCount, Allocator.Persistent).Result.Reinterpret<int>();
                RootTransformInstanceIdHashSet = new HashSet<int>();
                for (int i = 0; i < AllRootTransformInstanceIds.Count; i++)
                {
                    RootTransformInstanceIdHashSet.Add(AllRootTransformInstanceIds[i]);
                }
                for (int i = 0; i < Count; i++)
                {
                    SceneIndexedRootTransformInstanceIds[i] = new int[RootCounts[i]];
                    for (int ii = 0; ii < RootCounts[i]; ii++)
                    {
                        SceneIndexedRootTransformInstanceIds[i][ii] = AllRootTransformInstanceIds[ii + RootOffsets[i]];
                    }
                }

                SceneHierarchies = new TransformTree[Name.Length];
                for (int i = 0; i < Name.Length; i++)
                {
                    SceneHierarchies[i] = new TransformTree(true);
                    foreach (var ii in SceneIndexedRootTransformInstanceIds[i])
                    {
                        SceneHierarchies[i].AddChild(ii);
                    }
                }
            }

            public void Dispose()
            {
                Count = 0;
                AssetPath = null;
                Name = null;
                Path = null;
                BuildIndex.Dispose();
                RootCounts.Dispose();
                RootOffsets.Dispose();
                if (SceneIndexedRootTransformInstanceIds != null)
                {
                    for (int i = 0; i < SceneIndexedRootTransformInstanceIds.Length; i++)
                        SceneIndexedRootTransformInstanceIds[i] = null;
                }

                SceneIndexedRootTransformInstanceIds = null;
                AllRootTransformInstanceIds.Dispose();
                RootTransformInstanceIdHashSet = null;
                SceneHierarchies = null;
                AllRootGameObjectInstanceIds.Dispose();
                RootGameObjectInstanceIdHashSet = null;
                SceneIndexedRootGameObjectInstanceIds = null;
            }

            public void GenerateGameObjectData(CachedSnapshot snapshot)
            {
                AllRootGameObjectInstanceIds = new DynamicArray<int>(AllRootTransformInstanceIds.Count, Allocator.Persistent);
                {
                    var cachedList = new List<int>();
                    for (int i = 0; i < AllRootTransformInstanceIds.Count; i++)
                    {
                        AllRootGameObjectInstanceIds[i] = ObjectConnection.GetGameObjectInstanceIdFromTransformInstanceId(snapshot, AllRootTransformInstanceIds[i]);
                    }
                }
                RootGameObjectInstanceIdHashSet = new HashSet<int>();
                for (int i = 0; i < AllRootGameObjectInstanceIds.Count; i++)
                {
                    RootGameObjectInstanceIdHashSet.Add(AllRootGameObjectInstanceIds[i]);
                }

                SceneIndexedRootGameObjectInstanceIds = new int[Count][];
                for (int i = 0; i < Count; i++)
                {
                    SceneIndexedRootGameObjectInstanceIds[i] = new int[RootCounts[i]];
                    for (int ii = 0; ii < RootCounts[i]; ii++)
                    {
                        SceneIndexedRootGameObjectInstanceIds[i][ii] = AllRootGameObjectInstanceIds[ii + RootOffsets[i]];
                    }
                }
            }

            public void CreateTransformTrees(CachedSnapshot snapshot)
            {
                if (!snapshot.HasSceneRootsAndAssetbundles || SceneHierarchies == null) return;
                var cachedHashSet = new HashSet<int>();
                foreach (var hierarchy in SceneHierarchies)
                {
                    foreach (var child in hierarchy.Children)
                    {
                        AddTransforms(child, snapshot, cachedHashSet);
                    }
                }
            }

            void AddTransforms(TransformTree id, CachedSnapshot snapshot, HashSet<int> cachedHashSet)
            {
                id.GameObjectID = ObjectConnection.GetGameObjectInstanceIdFromTransformInstanceId(snapshot, id.InstanceID);
                if (ObjectConnection.TryGetConnectedTransformInstanceIdsFromTransformInstanceId(snapshot, id.InstanceID, id.Parent.InstanceID, ref cachedHashSet))
                {
                    id.AddChildren(cachedHashSet);
                    foreach (var child in id.Children)
                    {
                        AddTransforms(child, snapshot, cachedHashSet);
                    }
                }
            }
        }

        /// <summary>
        /// A list of gfx resources and their connections to native root id.
        /// </summary>
        public class NativeGfxResourcReferenceEntriesCache : IDisposable
        {
            /// <summary>
            /// Count of active gfx resources.
            /// </summary>
            public long Count;
            /// <summary>
            /// Gfx resource identifiers.
            /// </summary>
            public DynamicArray<ulong> GfxResourceId = default;
            /// <summary>
            /// Size of the gfx resource in bytes.
            /// </summary>
            public DynamicArray<ulong> GfxSize = default;
            /// <summary>
            /// Related native rootId.
            /// Native roots information is present in NativeRootReferenceEntriesCache table.
            /// NativeRootReferenceEntriesCache.idToIndex allows to map RootId to the index in the NativeRootReferenceEntriesCache table and retrive
            /// all available information about the root such as name, ram usage, etc.
            /// The relation is Many-to-one - Multiple entires in NativeGfxResourcReferenceEntriesCache can point to the same native root.
            /// </summary>
            public DynamicArray<long> RootId = default;

            /// <summary>
            /// Use to retrieve related gfx allocations size for the specific RootId.
            /// This is a derived acceleration structure built on top of the table data above.
            /// </summary>
            public Dictionary<long, ulong> RootIdToGfxSize;

            public NativeGfxResourcReferenceEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeGfxResourceReferences_Id);
                RootIdToGfxSize = new Dictionary<long, ulong>((int)Count);
                if (Count == 0)
                    return;

                GfxResourceId = reader.Read(EntryType.NativeGfxResourceReferences_Id, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                GfxSize = reader.Read(EntryType.NativeGfxResourceReferences_Size, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RootId = reader.Read(EntryType.NativeGfxResourceReferences_RootId, 0, Count, Allocator.Persistent).Result.Reinterpret<long>();

                for (int i = 0; i < Count; ++i)
                {
                    var id = RootId[i];
                    var gfxSize = GfxSize[i];

                    if (RootIdToGfxSize.TryGetValue(id, out var size))
                        RootIdToGfxSize[id] = size + gfxSize;
                    else
                        RootIdToGfxSize.Add(id, gfxSize);
                }
            }

            public void Dispose()
            {
                Count = 0;
                GfxResourceId.Dispose();
                GfxSize.Dispose();
                RootId.Dispose();
                RootIdToGfxSize = null;
            }
        }

        /// <summary>
        /// A table of all allocators which Unity uses to manage memory allocations in native code.
        /// All size values are in bytes.
        /// </summary>
        public class NativeAllocatorEntriesCache : IDisposable
        {
            /// <summary>
            /// Count of allocators.
            /// </summary>
            public long Count;
            /// <summary>
            /// Name of allocator.
            /// </summary>
            public string[] AllocatorName;
            /// <summary>
            /// Memory which was requested by Unity native systems from the allocator and is being used to store data.
            /// </summary>
            public DynamicArray<ulong> UsedSize = default;
            /// <summary>
            /// Total memory that was requested by allocator from System.
            /// May be larger than UsedSize to utilize pooling approach.
            /// </summary>
            public DynamicArray<ulong> ReservedSize = default;
            /// <summary>
            /// Total size of memory dedicated to allocations tracking.
            /// </summary>
            public DynamicArray<ulong> OverheadSize = default;
            /// <summary>
            /// Maximum amount of memory allocated with this allocator since app start.
            /// </summary>
            public DynamicArray<ulong> PeakUsedSize = default;
            /// <summary>
            /// Allocations count made via the specific allocator.
            /// </summary>
            public DynamicArray<ulong> AllocationCount = default;

            public NativeAllocatorEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeAllocatorInfo_AllocatorName);
                AllocatorName = new string[Count];

                if (Count == 0)
                    return;

                UsedSize = reader.Read(EntryType.NativeAllocatorInfo_UsedSize, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                ReservedSize = reader.Read(EntryType.NativeAllocatorInfo_ReservedSize, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                OverheadSize = reader.Read(EntryType.NativeAllocatorInfo_OverheadSize, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                PeakUsedSize = reader.Read(EntryType.NativeAllocatorInfo_PeakUsedSize, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                AllocationCount = reader.Read(EntryType.NativeAllocatorInfo_AllocationCount, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();

                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeAllocatorInfo_AllocatorName, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeAllocatorInfo_AllocatorName, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref AllocatorName);
                }
            }

            public void Dispose()
            {
                Count = 0;
                AllocatorName = null;
                UsedSize.Dispose();
                ReservedSize.Dispose();
                OverheadSize.Dispose();
                PeakUsedSize.Dispose();
                AllocationCount.Dispose();
            }
        }

        public class NativeObjectEntriesCache : IDisposable
        {
            public const int InstanceIDNone = 0;

            public long Count;
            public string[] ObjectName;
            public DynamicArray<int> InstanceId = default;
            public DynamicArray<ulong> Size = default;
            public DynamicArray<int> NativeTypeArrayIndex = default;
            public DynamicArray<HideFlags> HideFlags = default;
            public DynamicArray<ObjectFlags> Flags = default;
            public DynamicArray<ulong> NativeObjectAddress = default;
            public DynamicArray<long> RootReferenceId = default;
            public DynamicArray<int> ManagedObjectIndex = default;

            //secondary data
            public DynamicArray<int> RefCount = default;
            public Dictionary<ulong, int> NativeObjectAddressToInstanceId { private set; get; }
            public Dictionary<long, int> RootReferenceIdToIndex { private set; get; }
            public SortedDictionary<int, int> InstanceId2Index;

            public readonly ulong TotalSizes = 0ul;
            DynamicArray<int> MetaDataBufferIndicies = default;
            NestedDynamicArray<byte> MetaDataBuffers => m_MetaDataBuffersReadOp?.CompleteReadAndGetNestedResults() ?? default;
            NestedDynamicSizedArrayReadOperation<byte> m_MetaDataBuffersReadOp;

            unsafe public NativeObjectEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeObjects_InstanceId);
                NativeObjectAddressToInstanceId = new Dictionary<ulong, int>((int)Count);
                RootReferenceIdToIndex = new Dictionary<long, int>((int)Count);
                InstanceId2Index = new SortedDictionary<int, int>();
                ObjectName = new string[Count];

                if (Count == 0)
                    return;

                InstanceId = reader.Read(EntryType.NativeObjects_InstanceId, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                Size = reader.Read(EntryType.NativeObjects_Size, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                NativeTypeArrayIndex = reader.Read(EntryType.NativeObjects_NativeTypeArrayIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                HideFlags = reader.Read(EntryType.NativeObjects_HideFlags, 0, Count, Allocator.Persistent).Result.Reinterpret<HideFlags>();
                Flags = reader.Read(EntryType.NativeObjects_Flags, 0, Count, Allocator.Persistent).Result.Reinterpret<ObjectFlags>();
                NativeObjectAddress = reader.Read(EntryType.NativeObjects_NativeObjectAddress, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RootReferenceId = reader.Read(EntryType.NativeObjects_RootReferenceId, 0, Count, Allocator.Persistent).Result.Reinterpret<long>();
                ManagedObjectIndex = reader.Read(EntryType.NativeObjects_GCHandleIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                RefCount = new DynamicArray<int>(Count, Allocator.Persistent, true);

                using (var tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeObjects_Name, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeObjects_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref ObjectName);
                }

                for (long i = 0; i < NativeObjectAddress.Count; ++i)
                {
                    var id = InstanceId[i];
                    NativeObjectAddressToInstanceId.Add(NativeObjectAddress[i], id);
                    RootReferenceIdToIndex.Add(RootReferenceId[i], (int)i);
                    InstanceId2Index[id] = (int)i;
                    TotalSizes += Size[i];
                }

                //fallback for the legacy snapshot formats
                //create the managedObjectIndex array and make it -1 on each entry so they can be overridden during crawling
                //TODO: remove this when the new crawler lands :-/
                if (reader.FormatVersion < FormatVersion.NativeConnectionsAsInstanceIdsVersion)
                {
                    ManagedObjectIndex.Dispose();
                    ManagedObjectIndex = new DynamicArray<int>(Count, Allocator.Persistent);
                    for (int i = 0; i < Count; ++i)
                        ManagedObjectIndex[i] = -1;
                }

                // handle formats tht have the new metadata added for native objects
                if (reader.FormatVersion >= FormatVersion.NativeObjectMetaDataVersion)
                {
                    //get the array that tells us how to index the buffers for the actual meta data
                    MetaDataBufferIndicies = reader.Read(EntryType.ObjectMetaData_MetaDataBufferIndicies, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                    // loop the array and get the total number of entries we need
                    int sum = 0;
                    for (int i = 0; i < MetaDataBufferIndicies.Count; i++)
                    {
                        if (MetaDataBufferIndicies[i] != -1)
                            sum++;
                    }

                    m_MetaDataBuffersReadOp = reader.AsyncReadDynamicSizedArray<byte>(EntryType.ObjectMetaData_MetaDataBuffer, 0, sum, Allocator.Persistent);
                }
            }

            public ILongIndexedContainer<byte> MetaData(int nativeObjectIndex)
            {
                if (MetaDataBufferIndicies.Count == 0) return default;
                var bufferIndex = MetaDataBufferIndicies[nativeObjectIndex];
                if (bufferIndex == -1) return default(DynamicArrayRef<byte>);

                return MetaDataBuffers[bufferIndex];
            }

            public void Dispose()
            {
                Count = 0;
                InstanceId.Dispose();
                Size.Dispose();
                NativeTypeArrayIndex.Dispose();
                HideFlags.Dispose();
                Flags.Dispose();
                NativeObjectAddress.Dispose();
                RootReferenceId.Dispose();
                ManagedObjectIndex.Dispose();
                RefCount.Dispose();
                ObjectName = null;
                NativeObjectAddressToInstanceId = null;
                RootReferenceIdToIndex = null;
                InstanceId2Index = null;
                MetaDataBufferIndicies.Dispose();
                if (m_MetaDataBuffersReadOp != null)
                {
                    MetaDataBuffers.Dispose();
                    m_MetaDataBuffersReadOp.Dispose();
                    m_MetaDataBuffersReadOp = null;
                }
            }
        }

        public class SystemMemoryRegionEntriesCache : IDisposable
        {
            public enum MemoryType : ushort
            {
                // NB!: The same as in SystemInfoMemory.h
                Private = 0, // Private to this process allocations
                Mapped = 1,  // Allocations mapped to a file (dll/exe/etc)
                Shared = 2,  // Shared memory
                Device = 3,   // Shared device or driver memory (like GPU, sound cards, etc)
                Count
            }

            public long Count;
            public string[] RegionName;
            public DynamicArray<ulong> RegionAddress = default;
            public DynamicArray<ulong> RegionSize = default;
            public DynamicArray<ulong> RegionResident = default;
            public DynamicArray<ushort> RegionType = default;

            public SystemMemoryRegionEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.SystemMemoryRegions_Address);
                RegionName = new string[Count];

                if (Count == 0)
                    return;

                RegionAddress = reader.Read(EntryType.SystemMemoryRegions_Address, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RegionSize = reader.Read(EntryType.SystemMemoryRegions_Size, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RegionResident = reader.Read(EntryType.SystemMemoryRegions_Resident, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RegionType = reader.Read(EntryType.SystemMemoryRegions_Type, 0, Count, Allocator.Persistent).Result.Reinterpret<ushort>();

                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.SystemMemoryRegions_Name, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.SystemMemoryRegions_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref RegionName);
                }
            }

            public void Dispose()
            {
                Count = 0;
                RegionName = null;
                RegionAddress.Dispose();
                RegionSize.Dispose();
                RegionResident.Dispose();
                RegionType.Dispose();
            }
        }

        public class SystemMemoryResidentPagesEntriesCache : IDisposable
        {
            public readonly long Count;
            public readonly DynamicArray<ulong> RegionAddress;
            public readonly DynamicArray<int> RegionStartPageIndex;
            public readonly DynamicArray<int> RegionEndPageIndex;
            public readonly BitArray PageStates;
            public readonly UInt32 PageSize;

            public SystemMemoryResidentPagesEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.SystemMemoryResidentPages_Address);
                if (Count == 0)
                {
                    PageSize = 0;
                    PageStates = new BitArray(0);
                    RegionAddress = default;
                    RegionStartPageIndex = default;
                    RegionEndPageIndex = default;
                    return;
                }

                RegionAddress = reader.Read(EntryType.SystemMemoryResidentPages_Address, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RegionStartPageIndex = reader.Read(EntryType.SystemMemoryResidentPages_FirstPageIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                RegionEndPageIndex = reader.Read(EntryType.SystemMemoryResidentPages_LastPageIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();

                var tempPageStates = new BitArray[1];
                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.SystemMemoryResidentPages_PagesState, 0, 1);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.SystemMemoryResidentPages_PagesState, tmp, 0, 1);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref tempPageStates);
                    PageStates = tempPageStates[0];
                }

                unsafe
                {
                    UInt32 tempPageSize = 0;
                    byte* tempPageSizePtr = (byte*)&tempPageSize;
                    reader.ReadUnsafe(EntryType.SystemMemoryResidentPages_PageSize, tempPageSizePtr, sizeof(UInt32), 0, 1);
                    PageSize = tempPageSize;
                }
            }

            public void Dispose()
            {
                RegionAddress.Dispose();
                RegionStartPageIndex.Dispose();
                RegionEndPageIndex.Dispose();
            }

            public ulong CalculateResidentMemory(long regionIndex, ulong address, ulong size)
            {
                if ((Count == 0) || (size == 0))
                    return 0;

                var regionAddress = RegionAddress[regionIndex];
                var firstPageIndex = RegionStartPageIndex[regionIndex];
                var lastPageIndex = RegionEndPageIndex[regionIndex];

                // Calculate first and last page index in bitset PageState
                var addrDelta = address - regionAddress;
                var begPage = (int)(addrDelta / PageSize) + firstPageIndex;
                var endPage = (int)((addrDelta + size - 1) / PageSize) + firstPageIndex;
                if ((begPage < firstPageIndex) || (endPage > lastPageIndex))
                {
                    Debug.LogAssertion("Page range is outside of system region range. Please report a bug!");
                    return 0;
                }

                // Sum total for all pages in range
                ulong residentSize = 0;
                for (var p = begPage; p <= endPage; p++)
                {
                    if (PageStates[p])
                        residentSize += PageSize;
                }

                // As address might be not aligned, we need to substract
                // difference for the first and the last page
                if (PageStates[begPage])
                {
                    var head = address % PageSize;
                    residentSize -= head;
                }

                if (PageStates[endPage])
                {
                    var tail = (address + size) % PageSize;
                    if (tail > 0)
                        residentSize -= PageSize - tail;
                }

                return residentSize;
            }
        }

        public enum MemorySectionType : byte
        {
            GarbageCollector,
            VirtualMachine
        }

        // Eventual TODO: Add on demand load of sections, and unused chunks unload
        public class ManagedMemorySectionEntriesCache : IDisposable
        {
            static readonly ProfilerMarker k_CacheFind = new ProfilerMarker("ManagedMemorySectionEntriesCache.Find");
            public long Count;
            public DynamicArray<ulong> StartAddress = default;
            public DynamicArray<ulong> SectionSize = default;
            public DynamicArray<MemorySectionType> SectionType = default;
            public string[] SectionName = default;
            public NestedDynamicArray<byte> Bytes => m_BytesReadOp?.CompleteReadAndGetNestedResults() ?? default;
            NestedDynamicSizedArrayReadOperation<byte> m_BytesReadOp;
            ulong m_MinAddress;
            ulong m_MaxAddress;
            const ulong k_ReferenceBit = 1UL << 63;

            static readonly string k_VMSection = UnityEditor.L10n.Tr("Virtual Machine Memory Section");
            static readonly string k_GCSection = UnityEditor.L10n.Tr("Managed Heap Section");
            static readonly string k_ActiveGCSection = UnityEditor.L10n.Tr("Active Managed Heap Section");
            static readonly string k_StackSection = UnityEditor.L10n.Tr("Managed Stack Section");
            static readonly string k_ManagedMemorySection = UnityEditor.L10n.Tr("Managed Memory Section (unclear if Heap or Virtual Machine memory, please update Unity)");

            public readonly ulong VirtualMachineMemoryReserved = 0;
            // if the snapshot format is missing the VM section bit, this number will include VM memory
            public readonly ulong ManagedHeapMemoryReserved = 0;
            public readonly ulong TotalActiveManagedHeapSectionReserved = 0;
            public readonly ulong StackMemoryReserved = 0;

            public readonly long FirstAssumedActiveHeapSectionIndex = 0;
            public readonly long LastAssumedActiveHeapSectionIndex = 0;

            public ManagedMemorySectionEntriesCache(ref IFileReader reader, bool HasGCHeapTypes, bool readStackMemory)
            {
                Count = reader.GetEntryCount(readStackMemory ? EntryType.ManagedStacks_StartAddress : EntryType.ManagedHeapSections_StartAddress);
                m_MinAddress = m_MaxAddress = 0;

                if (Count == 0)
                    return;

                SectionType = new DynamicArray<MemorySectionType>(Count, Allocator.Persistent, true);
                SectionName = new string[Count];
                StartAddress = reader.Read(readStackMemory ? EntryType.ManagedStacks_StartAddress : EntryType.ManagedHeapSections_StartAddress, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();

                //long heapSectionIndex = 0;
                //long vmSectionIndex = 0;
                if (HasGCHeapTypes)
                {
                    for (long i = 0; i < StartAddress.Count; ++i)
                    {
                        var encoded = StartAddress[i];
                        StartAddress[i] = encoded & ~k_ReferenceBit; //unmask addr
                        var isVMSection = (encoded & k_ReferenceBit) == k_ReferenceBit;
                        SectionType[i] = isVMSection ? MemorySectionType.VirtualMachine : MemorySectionType.GarbageCollector; //get heaptype
                        // numbering the sections could be confusing as people might expect the numbers to stay comparable over time,
                        // but if one section is unloaded or merged/split in a following snapshot, people might confuse them as the same one
                        // also, grouping the columns by name doesn't work nicely then either so, only number them for debugging purposes
                        // bonus: waaaay less string memory usage and no GC.Allocs for these!
                        if (isVMSection)
                            SectionName[i] = k_VMSection;//"Managed Virtual Machine Memory Section " + vmSectionIndex++;
                        else
                            SectionName[i] = k_GCSection;//"Managed Heap Section " + heapSectionIndex++;
                    }
                }
                else
                {
                    for (long i = 0; i < StartAddress.Count; ++i)
                    {
                        SectionName[i] = k_ManagedMemorySection;
                    }
                }
                if (readStackMemory)
                {
                    for (long i = 0; i < Count; ++i)
                    {
                        SectionName[i] = k_StackSection;//"Managed Stack Section " + i;
                    }
                }

                var entryType = readStackMemory ? EntryType.ManagedStacks_Bytes : EntryType.ManagedHeapSections_Bytes;

                m_BytesReadOp = reader.AsyncReadDynamicSizedArray<byte>(entryType, 0, Count, Allocator.Persistent);

                SectionSize = new DynamicArray<ulong>(Count, Allocator.Persistent);
                // For Sorting we don't need the Async reading of the Managed Stack / Heap bytes to be loaded yet
                SortSectionEntries(ref StartAddress, ref SectionSize, ref SectionType, ref SectionName, ref m_BytesReadOp, readStackMemory);
                m_MinAddress = StartAddress[0];
                m_MaxAddress = StartAddress[Count - 1] + (ulong)m_BytesReadOp.UnsafeAccessToNestedDynamicSizedArray.Count(Count - 1);

                var foundLastAssumedActiveHeap = false;
                var foundFirstAssumedActiveHeap = false;

                for (long i = Count - 1; i >= 0; i--)
                {
                    if (readStackMemory)
                        StackMemoryReserved += SectionSize[i];
                    else
                    {
                        if (SectionType[i] == MemorySectionType.GarbageCollector)
                        {
                            ManagedHeapMemoryReserved += SectionSize[i];
                            if (!foundLastAssumedActiveHeap)
                            {
                                FirstAssumedActiveHeapSectionIndex = i;
                                LastAssumedActiveHeapSectionIndex = i;
                                foundLastAssumedActiveHeap = true;
                            }
                            else if (!foundFirstAssumedActiveHeap && StartAddress[i] + SectionSize[i] + VMTools.X64ArchPtrSize > StartAddress[FirstAssumedActiveHeapSectionIndex])
                            {
                                FirstAssumedActiveHeapSectionIndex = i;
                            }
                            else
                                foundFirstAssumedActiveHeap = true;
                        }
                        else
                            VirtualMachineMemoryReserved += SectionSize[i];
                    }
                }
                if (foundFirstAssumedActiveHeap && foundLastAssumedActiveHeap)
                {
                    for (long i = FirstAssumedActiveHeapSectionIndex; i <= LastAssumedActiveHeapSectionIndex; i++)
                    {
                        SectionName[i] = k_ActiveGCSection;
                    }
                }
                TotalActiveManagedHeapSectionReserved = StartAddress[LastAssumedActiveHeapSectionIndex] + SectionSize[LastAssumedActiveHeapSectionIndex] - StartAddress[FirstAssumedActiveHeapSectionIndex];
            }

            public BytesAndOffset Find(ulong address, VirtualMachineInformation virtualMachineInformation)
            {
                using (k_CacheFind.Auto())
                {
                    var bytesAndOffset = new BytesAndOffset();

                    if (address != 0 && address >= m_MinAddress && address < m_MaxAddress)
                    {
                        long idx = DynamicArrayAlgorithms.BinarySearch(StartAddress, address);
                        if (idx < 0)
                        {
                            // -1 means the address is smaller than the first StartAddress, early out with an invalid bytesAndOffset
                            if (idx == -1)
                                return bytesAndOffset;
                            // otherwise, a negative Index just means there was no direct hit and ~idx - 1 will give us the index to the next smaller StartAddress
                            idx = ~idx - 1;
                        }

                        if (address >= StartAddress[idx] && address < (StartAddress[idx] + (ulong)Bytes.Count(idx)))
                        {
                            bytesAndOffset = new BytesAndOffset(Bytes[idx], virtualMachineInformation.PointerSize, address - StartAddress[idx]);
                        }
                    }

                    return bytesAndOffset;
                }
            }

            readonly struct SortIndexHelper : IComparable<SortIndexHelper>
            {
                public readonly long Index;
                public readonly ulong StartAddress;

                public SortIndexHelper(ref long index, ref ulong startAddress)
                {
                    Index = index;
                    StartAddress = startAddress;
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public int CompareTo(SortIndexHelper other) => StartAddress.CompareTo(other.StartAddress);
            }

            static void SortSectionEntries(ref DynamicArray<ulong> startAddresses, ref DynamicArray<ulong> sizes, ref DynamicArray<MemorySectionType> associatedSectionType, ref string[] associatedSectionNames,
                ref NestedDynamicSizedArrayReadOperation<byte> associatedByteArrayReadOp, bool isStackMemory)
            {
                using var sortMapping = new DynamicArray<SortIndexHelper>(startAddresses.Count, Allocator.Temp);

                for (long i = 0; i < sortMapping.Count; ++i)
                {
                    sortMapping[i] = new SortIndexHelper(ref i, ref startAddresses[i]);
                }

                var startAddr = startAddresses;
                DynamicArrayAlgorithms.IntrospectiveSort(sortMapping, 0, startAddresses.Count);
                using var newSortedAddresses = new DynamicArray<ulong>(startAddresses.Count, Allocator.Temp);
                unsafe
                {
                    var newSortedSectionTypes = isStackMemory ? null : new MemorySectionType[startAddresses.Count];
                    var newSortedSectionNames = new string[startAddresses.Count];

                    for (long i = 0; i < startAddresses.Count; ++i)
                    {
                        long idx = sortMapping[i].Index;
                        newSortedAddresses[i] = startAddresses[idx];
                        newSortedSectionNames[i] = associatedSectionNames[idx];

                        if (!isStackMemory)
                            newSortedSectionTypes[i] = associatedSectionType[idx];
                    }

                    using (var sortedIndice = new DynamicArray<long>(startAddresses.Count, Allocator.Temp))
                    {
                        UnsafeUtility.MemCpyStride(sortedIndice.GetUnsafePtr(), sizeof(long), sortMapping.GetUnsafePtr(), sizeof(SortIndexHelper), sizeof(SortIndexHelper), (int)startAddresses.Count);
                        associatedByteArrayReadOp.UnsafeAccessToNestedDynamicSizedArray.Sort(sortedIndice);
                    }

                    UnsafeUtility.MemCpy(startAddresses.GetUnsafePtr(), newSortedAddresses.GetUnsafePtr(), sizeof(ulong) * startAddresses.Count);
                    for (long i = 0; i < startAddresses.Count; ++i)
                    {
                        sizes[i] = (ulong)associatedByteArrayReadOp.UnsafeAccessToNestedDynamicSizedArray.Count(i);
                        if (!isStackMemory)
                            associatedSectionType[i] = newSortedSectionTypes[i];
                    }
                    associatedSectionNames = newSortedSectionNames;

                }
                sortMapping.Dispose();
            }

            public void Dispose()
            {
                Count = 0;
                m_MinAddress = m_MaxAddress = 0;
                StartAddress.Dispose();
                SectionType.Dispose();
                SectionSize.Dispose();
                SectionName = null;
                if (m_BytesReadOp != null)
                {
                    Bytes.Dispose();
                    m_BytesReadOp.Dispose();
                    m_BytesReadOp = null;
                }
            }
        }

        //leave this as second to last thing to convert, also a major pain in the ass
        public class TypeDescriptionEntriesCache : IDisposable
        {
            public const int ITypeInvalid = -1;
            const int k_DefaultFieldProcessingBufferSize = 64;
            public const string UnityObjectTypeName = "UnityEngine.Object";
            public const string UnityNativeObjectPointerFieldName = "m_CachedPtr";
            public int IFieldUnityObjectMCachedPtr { get; private set; }
            public int IFieldUnityObjectMCachedPtrOffset { get; private set; } = -1;

            const string k_UnityMonoBehaviourTypeName = "UnityEngine.MonoBehaviour";
            const string k_UnityScriptableObjectTypeName = "UnityEngine.ScriptableObject";
            const string k_UnityComponentObjectTypeName = "UnityEngine.Component";

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
            const string k_SystemByteName = "System.Byte";

            public int Count;
            public DynamicArray<TypeFlags> Flags = default;
            public DynamicArray<int> BaseOrElementTypeIndex = default;
            public DynamicArray<int> Size = default;
            public DynamicArray<ulong> TypeInfoAddress = default;
            //public DynamicArray<int> TypeIndex = default;

            public string[] TypeDescriptionName;
            public string[] Assembly;

            public NestedDynamicArray<int> FieldIndices => m_FieldIndicesReadOp?.CompleteReadAndGetNestedResults() ?? default;
            NestedDynamicSizedArrayReadOperation<int> m_FieldIndicesReadOp;
            public NestedDynamicArray<byte> StaticFieldBytes => m_StaticFieldBytesReadOp?.CompleteReadAndGetNestedResults() ?? default;
            NestedDynamicSizedArrayReadOperation<byte> m_StaticFieldBytesReadOp;

            //secondary data, handled inside InitSecondaryItems
            public int[][] FieldIndicesInstance;//includes all bases' instance fields
            public int[][] fieldIndicesStatic;  //includes all bases' static fields
            public int[][] fieldIndicesOwnedStatic;  //includes only type's static fields

            public int ITypeValueType { get; private set; }
            public int ITypeUnityObject { get; private set; }
            public int ITypeObject { get; private set; }
            public int ITypeEnum { get; private set; }
            public int ITypeInt16 { get; private set; }
            public int ITypeInt32 { get; private set; }
            public int ITypeInt64 { get; private set; }
            public int ITypeUInt16 { get; private set; }
            public int ITypeUInt32 { get; private set; }
            public int ITypeUInt64 { get; private set; }
            public int ITypeBool { get; private set; }
            public int ITypeChar { get; private set; }
            public int ITypeCharArray { get; private set; }
            public int ITypeDouble { get; private set; }
            public int ITypeSingle { get; private set; }
            public int ITypeString { get; private set; }
            public int ITypeIntPtr { get; private set; }
            public int ITypeByte { get; private set; }

            public int ITypeUnityMonoBehaviour { get; private set; }
            public int ITypeUnityScriptableObject { get; private set; }
            public int ITypeUnityComponent { get; private set; }
            public Dictionary<ulong, int> TypeInfoToArrayIndex { get; private set; }
            // only fully initialized after the Managed Crawler is done stitching up Objects. Might be better to be moved over to ManagedData
            public Dictionary<int, int> UnityObjectTypeIndexToNativeTypeIndex { get; private set; }
            public HashSet<int> PureCSharpTypeIndices { get; private set; }

            public TypeDescriptionEntriesCache(ref IFileReader reader, FieldDescriptionEntriesCache fieldDescriptions)
            {
                Count = (int)reader.GetEntryCount(EntryType.TypeDescriptions_TypeIndex);

                TypeDescriptionName = new string[Count];
                Assembly = new string[Count];

                if (Count == 0)
                    return;

                Flags = reader.Read(EntryType.TypeDescriptions_Flags, 0, Count, Allocator.Persistent).Result.Reinterpret<TypeFlags>();
                BaseOrElementTypeIndex = reader.Read(EntryType.TypeDescriptions_BaseOrElementTypeIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                Size = reader.Read(EntryType.TypeDescriptions_Size, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                TypeInfoAddress = reader.Read(EntryType.TypeDescriptions_TypeInfoAddress, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
#if DEBUG_VALIDATION
                if(reader.FormatVersion == FormatVersion.SnapshotMinSupportedFormatVersion)
                {
                    // Nb! This code is left here for posterity in case anyone wonders what EntryType.TypeDescriptions_TypeIndex is, and if it is needed. No it is not.

                    // After thorough archeological digging, there seems to be no evidence that this array was ever needed
                    // At the latest after FormatVersion.StreamingManagedMemoryCaptureFormatVersion (9) it is definitely not needed
                    // as the indices reported in this map exactly to the indices in the array

                    var TypeIndex = reader.Read(EntryType.TypeDescriptions_TypeIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                    for (int i = 0; i < TypeIndex.Count; i++)
                    {
                        if(i != TypeIndex[i])
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

                //change to consume field descriptions instead
                InitSecondaryItems(this, fieldDescriptions);
            }

            // Check all bases' fields
            public bool HasAnyField(int iType)
            {
                return FieldIndicesInstance[iType].Length > 0 || fieldIndicesStatic[iType].Length > 0;
            }

            // Check all bases' fields
            public bool HasAnyStaticField(int iType)
            {
                return fieldIndicesStatic[iType].Length > 0;
            }

            // Check only the type's fields
            public bool HasStaticField(long iType)
            {
                return fieldIndicesOwnedStatic[iType].Length > 0;
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

            public bool HasFlag(int arrayIndex, TypeFlags flag)
            {
                return (Flags[arrayIndex] & flag) == flag;
            }

            public int GetRank(int arrayIndex)
            {
                int r = (int)(Flags[arrayIndex] & TypeFlags.kArrayRankMask) >> 16;
                Checks.IsTrue(r >= 0);
                return r;
            }

            public int TypeInfo2ArrayIndex(UInt64 aTypeInfoAddress)
            {
                int i;

                if (!TypeInfoToArrayIndex.TryGetValue(aTypeInfoAddress, out i))
                {
                    return -1;
                }
                return i;
            }

            static readonly ProfilerMarker k_TypeFieldArraysBuild = new ProfilerMarker("MemoryProfiler.TypeFields.TypeFieldArrayBuilding");
            void InitSecondaryItems(TypeDescriptionEntriesCache typeDescriptionEntries, FieldDescriptionEntriesCache fieldDescriptions)
            {
                TypeInfoToArrayIndex = Enumerable.Range(0, (int)TypeInfoAddress.Count).ToDictionary(x => TypeInfoAddress[x], x => x);
                UnityObjectTypeIndexToNativeTypeIndex = new Dictionary<int, int>();
                PureCSharpTypeIndices = new HashSet<int>();


                ITypeUnityObject = Array.FindIndex(TypeDescriptionName, x => x == UnityObjectTypeName);
#if DEBUG_VALIDATION //This shouldn't really happen
                if (ITypeUnityObject < 0)
                {
                    throw new Exception("Unable to find UnityEngine.Object");
                }
#endif

                using (k_TypeFieldArraysBuild.Auto())
                {
                    FieldIndicesInstance = new int[Count][];
                    fieldIndicesStatic = new int[Count][];
                    fieldIndicesOwnedStatic = new int[Count][];
                    List<int> fieldProcessingBuffer = new List<int>(k_DefaultFieldProcessingBufferSize);

                    for (int i = 0; i < Count; ++i)
                    {
                        TypeTools.AllFieldArrayIndexOf(ref fieldProcessingBuffer, i, typeDescriptionEntries, fieldDescriptions, TypeTools.FieldFindOptions.OnlyInstance, true);
                        FieldIndicesInstance[i] = fieldProcessingBuffer.ToArray();

                        TypeTools.AllFieldArrayIndexOf(ref fieldProcessingBuffer, i, typeDescriptionEntries, fieldDescriptions, TypeTools.FieldFindOptions.OnlyStatic, true);
                        fieldIndicesStatic[i] = fieldProcessingBuffer.ToArray();

                        TypeTools.AllFieldArrayIndexOf(ref fieldProcessingBuffer, i, typeDescriptionEntries, fieldDescriptions, TypeTools.FieldFindOptions.OnlyStatic, false);
                        fieldIndicesOwnedStatic[i] = fieldProcessingBuffer.ToArray();

                        var typeIndex = i;
                        if (DerivesFromUnityObject(typeIndex))
                            UnityObjectTypeIndexToNativeTypeIndex.Add(typeIndex, -1);
                        else
                            PureCSharpTypeIndices.Add(typeIndex);
                    }
                }
                var fieldIndices = typeDescriptionEntries.FieldIndices[ITypeUnityObject];
                long fieldIndicesIndex = -1;
                for (long i = 0; i < fieldIndices.Count; i++)
                {
                    if (fieldDescriptions.FieldDescriptionName[fieldIndices[i]] == UnityNativeObjectPointerFieldName)
                    {
                        fieldIndicesIndex = i;
                        break;
                    }
                }

                IFieldUnityObjectMCachedPtr = fieldIndicesIndex >= 0 ? typeDescriptionEntries.FieldIndices[ITypeUnityObject][fieldIndicesIndex] : -1;

                IFieldUnityObjectMCachedPtrOffset = -1;

                if (IFieldUnityObjectMCachedPtr >= 0)
                {
                    IFieldUnityObjectMCachedPtrOffset = fieldDescriptions.Offset[IFieldUnityObjectMCachedPtr];
                }

#if DEBUG_VALIDATION
                if (IFieldUnityObjectMCachedPtrOffset < 0)
                {
                    Debug.LogWarning("Could not find unity object instance id field or m_CachedPtr");
                    return;
                }
#endif
                ITypeValueType = Array.FindIndex(TypeDescriptionName, x => x == k_SystemValueTypeName);
                ITypeObject = Array.FindIndex(TypeDescriptionName, x => x == k_SystemObjectTypeName);
                ITypeEnum = Array.FindIndex(TypeDescriptionName, x => x == k_SystemEnumTypeName);
                ITypeChar = Array.FindIndex(TypeDescriptionName, x => x == k_SystemCharTypeName);
                ITypeCharArray = Array.FindIndex(TypeDescriptionName, x => x == k_SystemCharArrayTypeName);
                ITypeInt16 = Array.FindIndex(TypeDescriptionName, x => x == k_SystemInt16Name);
                ITypeInt32 = Array.FindIndex(TypeDescriptionName, x => x == k_SystemInt32Name);
                ITypeInt64 = Array.FindIndex(TypeDescriptionName, x => x == k_SystemInt64Name);
                ITypeIntPtr = Array.FindIndex(TypeDescriptionName, x => x == k_SystemIntPtrName);
                ITypeString = Array.FindIndex(TypeDescriptionName, x => x == k_SystemStringName);
                ITypeBool = Array.FindIndex(TypeDescriptionName, x => x == k_SystemBoolName);
                ITypeSingle = Array.FindIndex(TypeDescriptionName, x => x == k_SystemSingleName);
                ITypeByte = Array.FindIndex(TypeDescriptionName, x => x == k_SystemByteName);
                ITypeDouble = Array.FindIndex(TypeDescriptionName, x => x == k_SystemDoubleName);
                ITypeUInt16 = Array.FindIndex(TypeDescriptionName, x => x == k_SystemUInt16Name);
                ITypeUInt32 = Array.FindIndex(TypeDescriptionName, x => x == k_SystemUInt32Name);
                ITypeUInt64 = Array.FindIndex(TypeDescriptionName, x => x == k_SystemUInt64Name);

                ITypeUnityMonoBehaviour = Array.FindIndex(TypeDescriptionName, x => x == k_UnityMonoBehaviourTypeName);
                ITypeUnityScriptableObject = Array.FindIndex(TypeDescriptionName, x => x == k_UnityScriptableObjectTypeName);
                ITypeUnityComponent = Array.FindIndex(TypeDescriptionName, x => x == k_UnityComponentObjectTypeName);
            }

            public bool DerivesFromUnityObject(int iTypeDescription)
            {
                while (iTypeDescription != ITypeUnityObject && iTypeDescription >= 0)
                {
                    if (HasFlag(iTypeDescription, TypeFlags.kArray))
                        return false;
                    iTypeDescription = BaseOrElementTypeIndex[iTypeDescription];
                }
                return iTypeDescription == ITypeUnityObject;
            }

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

            public void Dispose()
            {
                Count = 0;
                Flags.Dispose();
                BaseOrElementTypeIndex.Dispose();
                Size.Dispose();
                TypeInfoAddress.Dispose();
                TypeDescriptionName = null;
                Assembly = null;
                if (m_FieldIndicesReadOp != null)
                {
                    FieldIndices.Dispose();
                    m_FieldIndicesReadOp.Dispose();
                    m_FieldIndicesReadOp = null;
                }
                if (m_StaticFieldBytesReadOp != null)
                {
                    StaticFieldBytes.Dispose();
                    m_StaticFieldBytesReadOp.Dispose();
                    m_StaticFieldBytesReadOp = null;
                }

                FieldIndicesInstance = null;
                fieldIndicesStatic = null;
                fieldIndicesOwnedStatic = null;
                ITypeValueType = ITypeInvalid;
                ITypeObject = ITypeInvalid;
                ITypeEnum = ITypeInvalid;
                TypeInfoToArrayIndex = null;
                UnityObjectTypeIndexToNativeTypeIndex = null;
                PureCSharpTypeIndices = null;
            }
        }

        public class FieldDescriptionEntriesCache : IDisposable
        {
            public long Count;
            public string[] FieldDescriptionName;
            public DynamicArray<int> Offset = default;
            public DynamicArray<int> TypeIndex = default;
            public DynamicArray<byte> IsStatic = default;

            unsafe public FieldDescriptionEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.FieldDescriptions_Name);
                FieldDescriptionName = new string[Count];

                if (Count == 0)
                    return;

                Offset = new DynamicArray<int>(Count, Allocator.Persistent);
                TypeIndex = new DynamicArray<int>(Count, Allocator.Persistent);
                IsStatic = new DynamicArray<byte>(Count, Allocator.Persistent);

                Offset = reader.Read(EntryType.FieldDescriptions_Offset, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                TypeIndex = reader.Read(EntryType.FieldDescriptions_TypeIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                IsStatic = reader.Read(EntryType.FieldDescriptions_IsStatic, 0, Count, Allocator.Persistent).Result.Reinterpret<byte>();

                using (var tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpBufferSize = reader.GetSizeForEntryRange(EntryType.FieldDescriptions_Name, 0, Count);
                    tmp.Resize(tmpBufferSize, false);
                    reader.Read(EntryType.FieldDescriptions_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref FieldDescriptionName);
                }
            }

            public void Dispose()
            {
                Count = 0;
                Offset.Dispose();
                TypeIndex.Dispose();
                IsStatic.Dispose();
                FieldDescriptionName = null;
            }
        }

        public class GCHandleEntriesCache : IDisposable
        {
            public DynamicArray<ulong> Target = default;
            public long Count;

            public GCHandleEntriesCache(ref IFileReader reader)
            {
                unsafe
                {
                    Count = reader.GetEntryCount(EntryType.GCHandles_Target);
                    if (Count == 0)
                        return;

                    Target = reader.Read(EntryType.GCHandles_Target, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                }
            }

            public void Dispose()
            {
                Count = 0;
                Target.Dispose();
            }
        }

        public class ConnectionEntriesCache : IDisposable
        {
            public long Count;

            // From/To with the same index forms a pair "from->to"
            public DynamicArray<int> From { private set; get; }
            public DynamicArray<int> To { private set; get; }

            // List of objects referencing an object with the specfic key
            public Dictionary<SourceIndex, List<SourceIndex>> ReferencedBy { get; private set; } = new Dictionary<SourceIndex, List<SourceIndex>>();

            // List of objects an object with the specific key is refereing to
            public Dictionary<SourceIndex, List<SourceIndex>> ReferenceTo { get; private set; } = new Dictionary<SourceIndex, List<SourceIndex>>();

#if DEBUG_VALIDATION // could be always present but currently only used for validation in the crawler
            public long IndexOfFirstNativeToGCHandleConnection = -1;
#endif

            unsafe public ConnectionEntriesCache(ref IFileReader reader, NativeObjectEntriesCache nativeObjects, long gcHandlesCount, bool connectionsNeedRemaping)
            {
                Count = reader.GetEntryCount(EntryType.Connections_From);

                // Set allocator to `temp` if we're going to discard the data later
                Allocator allocator = Allocator.Persistent;
                if (Count > 0 && connectionsNeedRemaping)
                    allocator = Allocator.Temp;

                From = new DynamicArray<int>(Count, allocator);
                To = new DynamicArray<int>(Count, allocator);

                if (Count == 0)
                    return;

                From = reader.Read(EntryType.Connections_From, 0, Count, allocator).Result.Reinterpret<int>();
                To = reader.Read(EntryType.Connections_To, 0, Count, allocator).Result.Reinterpret<int>();

                if (connectionsNeedRemaping)
                    RemapInstanceIdsToUnifiedIndex(nativeObjects, gcHandlesCount);

                for (int i = 0; i < Count; i++)
                {
                    var to = ToSourceIndex(To[i], gcHandlesCount);
                    var from = ToSourceIndex(From[i], gcHandlesCount);

                    ReferencedBy.GetAndAddToListOrCreateList(to, from);

                    ReferenceTo.GetAndAddToListOrCreateList(from, to);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            SourceIndex ToSourceIndex(int index, long gcHandlesCount)
            {
                if (index < gcHandlesCount)
                    return new SourceIndex(SourceIndex.SourceId.ManagedObject, index);

                return new SourceIndex(SourceIndex.SourceId.NativeObject, index - gcHandlesCount);
            }

            void RemapInstanceIdsToUnifiedIndex(NativeObjectEntriesCache nativeObjects, long gcHandlesCount)
            {
                var instanceIds = nativeObjects.InstanceId;
                var gcHandlesIndices = nativeObjects.ManagedObjectIndex;

                // Create two temporary acceleration structures:
                // - Native object InstanceID to GC object
                // - Native object InstanceID to Unified Index
                //
                // Unified Index - [0..gcHandlesCount)[0..nativeObjects.Count]
                var instanceIDToUnifiedIndex = new Dictionary<int, int>();
                var instanceIDToGcHandleIndex = new Dictionary<int, int>();
                for (int i = 0; i < instanceIds.Count; ++i)
                {
                    if (gcHandlesIndices[i] != -1)
                    {
                        instanceIDToGcHandleIndex.Add(instanceIds[i], gcHandlesIndices[i]);
                    }
                    instanceIDToUnifiedIndex.Add(instanceIds[i], (int)gcHandlesCount + i);
                }

#if DEBUG_VALIDATION
                if (instanceIDToGcHandleIndex.Count > 0)
                    IndexOfFirstNativeToGCHandleConnection = Count;
#endif

                // Connections - reported Native objects connections
                // Plus links between Native and Managed objects (instanceIDToGcHandleIndex)
                DynamicArray<int> newFrom = new DynamicArray<int>(Count + instanceIDToGcHandleIndex.Count, Allocator.Persistent);
                DynamicArray<int> newTo = new DynamicArray<int>(newFrom.Count, Allocator.Persistent);

                // Add all Native to Native connections reported in snapshot as Unified Index
                for (long i = 0; i < Count; ++i)
                {
                    newFrom[i] = instanceIDToUnifiedIndex[From[i]];
                    newTo[i] = instanceIDToUnifiedIndex[To[i]];
                }

                // Dispose of original data to save memory
                // as we no longer need it
                To.Dispose();
                From.Dispose();

                // Add all Managed to Native connections
                var enumerator = instanceIDToGcHandleIndex.GetEnumerator();
                for (long i = Count; i < newFrom.Count; ++i)
                {
                    enumerator.MoveNext();
                    newFrom[i] = instanceIDToUnifiedIndex[enumerator.Current.Key];
                    // elements in To that are `To[i] < gcHandlesCount` are indexes into the GCHandles list
                    newTo[i] = enumerator.Current.Value;
                }

                From = newFrom;
                To = newTo;
                Count = From.Count;
            }

            public void Dispose()
            {
                Count = 0;
                From.Dispose();
                To.Dispose();
                // Setting to default isn't necessary, but can avoid confusion when memory profiling the memory profiler
                // without this, Disposing works but leaves the properties backing field with an invalid DynamicArray
                // that looks like it isundisposed
                From = default;
                To = default;

                ReferencedBy = null;
                ReferenceTo = null;
            }
        }

        IFileReader m_Reader;
        public MetaData MetaData { get; private set; }
        public DateTime TimeStamp { get; private set; }
        public VirtualMachineInformation VirtualMachineInformation { get; private set; }
        public NativeAllocationSiteEntriesCache NativeAllocationSites;
        public TypeDescriptionEntriesCache TypeDescriptions;
        public NativeTypeEntriesCache NativeTypes;
        public NativeRootReferenceEntriesCache NativeRootReferences;
        public NativeObjectEntriesCache NativeObjects;
        public NativeMemoryRegionEntriesCache NativeMemoryRegions;
        public NativeMemoryLabelEntriesCache NativeMemoryLabels;
        public NativeCallstackSymbolEntriesCache NativeCallstackSymbols;
        public NativeAllocationEntriesCache NativeAllocations;
        public ManagedMemorySectionEntriesCache ManagedStacks;
        public ManagedMemorySectionEntriesCache ManagedHeapSections;
        public GCHandleEntriesCache GcHandles;
        public FieldDescriptionEntriesCache FieldDescriptions;
        public ConnectionEntriesCache Connections;

        public SortedNativeMemoryRegionEntriesCache SortedNativeRegionsEntries;
        public SortedManagedObjectsCache SortedManagedObjects;
        public SortedNativeAllocationsCache SortedNativeAllocations;
        public SortedNativeObjectsCache SortedNativeObjects;

        public SceneRootEntriesCache SceneRoots;
        public NativeAllocatorEntriesCache NativeAllocators;
        public NativeGfxResourcReferenceEntriesCache NativeGfxResourceReferences;

        public SystemMemoryRegionEntriesCache SystemMemoryRegions;
        public SystemMemoryResidentPagesEntriesCache SystemMemoryResidentPages;
        public EntriesMemoryMapCache EntriesMemoryMap;

        public CachedSnapshot(IFileReader reader)
        {
            unsafe
            {
                VirtualMachineInformation vmInfo;
                reader.ReadUnsafe(EntryType.Metadata_VirtualMachineInformation, &vmInfo, UnsafeUtility.SizeOf<VirtualMachineInformation>(), 0, 1);

                if (!VMTools.ValidateVirtualMachineInfo(vmInfo))
                {
                    throw new UnityException("Invalid VM info. Snapshot file is corrupted.");
                }

                m_Reader = reader;
                long ticks;
                reader.ReadUnsafe(EntryType.Metadata_RecordDate, &ticks, UnsafeUtility.SizeOf<long>(), 0, 1);
                TimeStamp = new DateTime(ticks);

                VirtualMachineInformation = vmInfo;
                m_SnapshotVersion = reader.FormatVersion;

                MetaData = new MetaData(reader);

                NativeAllocationSites = new NativeAllocationSiteEntriesCache(ref reader);
                FieldDescriptions = new FieldDescriptionEntriesCache(ref reader);
                TypeDescriptions = new TypeDescriptionEntriesCache(ref reader, FieldDescriptions);
                NativeTypes = new NativeTypeEntriesCache(ref reader);
                NativeRootReferences = new NativeRootReferenceEntriesCache(ref reader);
                NativeObjects = new NativeObjectEntriesCache(ref reader);
                NativeMemoryRegions = new NativeMemoryRegionEntriesCache(ref reader);
                NativeMemoryLabels = new NativeMemoryLabelEntriesCache(ref reader, HasMemoryLabelSizesAndGCHeapTypes);
                NativeCallstackSymbols = new NativeCallstackSymbolEntriesCache(ref reader);
                NativeAllocations = new NativeAllocationEntriesCache(ref reader, NativeAllocationSites.Count != 0);
                ManagedStacks = new ManagedMemorySectionEntriesCache(ref reader, false, true);
                ManagedHeapSections = new ManagedMemorySectionEntriesCache(ref reader, HasMemoryLabelSizesAndGCHeapTypes, false);
                GcHandles = new GCHandleEntriesCache(ref reader);
                Connections = new ConnectionEntriesCache(ref reader, NativeObjects, GcHandles.Count, HasConnectionOverhaul);
                SceneRoots = new SceneRootEntriesCache(ref reader);
                NativeGfxResourceReferences = new NativeGfxResourcReferenceEntriesCache(ref reader);
                NativeAllocators = new NativeAllocatorEntriesCache(ref reader);

                SystemMemoryRegions = new SystemMemoryRegionEntriesCache(ref reader);
                SystemMemoryResidentPages = new SystemMemoryResidentPagesEntriesCache(ref reader);

                SortedManagedObjects = new SortedManagedObjectsCache(this);

                SortedNativeRegionsEntries = new SortedNativeMemoryRegionEntriesCache(this);
                SortedNativeAllocations = new SortedNativeAllocationsCache(this);
                SortedNativeObjects = new SortedNativeObjectsCache(this);

                EntriesMemoryMap = new EntriesMemoryMapCache(this);

                CrawledData = new ManagedData(GcHandles.Count, Connections.Count);
                if (MemoryProfilerSettings.FeatureFlags.GenerateTransformTreesForByStatusTable_2022_09)
                    SceneRoots.CreateTransformTrees(this);
                SceneRoots.GenerateGameObjectData(this);
            }
        }

        public string FullPath
        {
            get
            {
                return m_Reader.FullPath;
            }
        }

        //Unified Object index are in that order: gcHandle, native object, crawled objects
        public long ManagedObjectIndexToUnifiedObjectIndex(long i)
        {
            // Invalid
            if (i < 0)
                return -1;

            // GC Handle
            if (i < GcHandles.Count)
                return i;

            // Managed objects, but not reported through GC Handle
            if (i < CrawledData.ManagedObjects.Count)
                return i + NativeObjects.Count;

            return -1;
        }

        public long NativeObjectIndexToUnifiedObjectIndex(long i)
        {
            if (i < 0)
                return -1;

            // Shift behind native objects
            if (i < NativeObjects.Count)
                return i + GcHandles.Count;

            return -1;
        }

        public int UnifiedObjectIndexToManagedObjectIndex(long i)
        {
            if (i < 0)
                return -1;

            if (i < GcHandles.Count)
                return (int)i;

            // If CrawledData.ManagedObjects includes GcHandles as first GcHandles.Count
            // than it makes sense as we want to remap only excess
            int firstCrawled = (int)(GcHandles.Count + NativeObjects.Count);
            int lastCrawled = (int)(NativeObjects.Count + CrawledData.ManagedObjects.Count);

            if (i >= firstCrawled && i < lastCrawled)
                return (int)(i - (int)NativeObjects.Count);

            return -1;
        }

        public int UnifiedObjectIndexToNativeObjectIndex(long i)
        {
            if (i < GcHandles.Count) return -1;
            int firstCrawled = (int)(GcHandles.Count + NativeObjects.Count);
            if (i < firstCrawled) return (int)(i - (int)GcHandles.Count);
            return -1;
        }

        public void Dispose()
        {
            if (!m_Disposed)
            {
                m_Disposed = true;
                NativeAllocationSites.Dispose();
                TypeDescriptions.Dispose();
                NativeTypes.Dispose();
                NativeRootReferences.Dispose();
                NativeObjects.Dispose();
                NativeMemoryRegions.Dispose();
                NativeMemoryLabels.Dispose();
                NativeCallstackSymbols.Dispose();
                NativeAllocations.Dispose();
                ManagedStacks.Dispose();
                ManagedHeapSections.Dispose();
                GcHandles.Dispose();
                FieldDescriptions.Dispose();
                Connections.Dispose();
                SceneRoots.Dispose();
                NativeGfxResourceReferences.Dispose();
                NativeAllocations.Dispose();
                NativeAllocators.Dispose();

                SystemMemoryRegions.Dispose();
                SystemMemoryResidentPages.Dispose();

                EntriesMemoryMap.Dispose();
                CrawledData.Dispose();
                CrawledData = null;

                SortedNativeRegionsEntries.Dispose();
                SortedManagedObjects.Dispose();
                SortedNativeAllocations.Dispose();
                SortedNativeObjects.Dispose();

                // Close and dispose the reader
                m_Reader.Close();
            }
        }

        public interface ISortedEntriesCache
        {
            void Preload();
            long Count { get; }
            ulong Address(long index);
            ulong Size(long index);
        }

        public abstract class IndirectlySortedEntriesCache<TSortComparer>
            : IDisposable, ISortedEntriesCache
            where TSortComparer : unmanaged, IRefComparer<long>
        {
            protected CachedSnapshot m_Snapshot;
            DynamicArray<long> m_Sorting;
            bool m_Loaded;

            protected IndirectlySortedEntriesCache(CachedSnapshot snapshot)
            {
                m_Snapshot = snapshot;
                m_Loaded = false;
            }

            public long this[long index]
            {
                get
                {
                    if (m_Loaded)
                        return m_Sorting[index];
                    // m_Loaded is more likely than not true, but we can't tell the optimizer that in C#,
                    // so have the less likely case as separate fallback branch
                    Preload();
                    return m_Sorting[index];
                }
            }

            public abstract long Count { get; }
            protected abstract TSortComparer SortingComparer { get; }
            unsafe DynamicArrayAlgorithms.ArraySortingData<long, TSortComparer> Comparer =>
                DynamicArrayAlgorithms.ArraySortingData<TSortComparer>.GetSortDataForSortingAnIndexingArray(in m_Sorting, SortingComparer);

            public abstract ulong Address(long index);

            public virtual void Preload()
            {
                if (!m_Sorting.IsCreated)
                {
                    m_Sorting = new DynamicArray<long>(Count, Allocator.Persistent);
                    var count = m_Sorting.Count;
                    for (long i = 0; i < count; ++i)
                        m_Sorting[i] = i;
                    var comparer = Comparer;
                    DynamicArrayAlgorithms.IntrospectiveSort(0, Count, ref comparer);
                }
                m_Loaded = true;
            }

            public abstract ulong Size(long index);

            public virtual void Dispose()
            {
                m_Sorting.Dispose();
            }

            /// <summary>
            /// Uses <see cref="DynamicArrayAlgorithms.BinarySearch"/> to quickly find an item
            /// which has a start <see cref="Address(long)"/> matching the provided <paramref name="address"/>,
            /// or, if <paramref name="onlyDirectAddressMatches"/> is false, where the <paramref name="address"/> falls between
            /// the items start <see cref="Address(long)"/> and last address (using <see cref="Size(long)"/>).
            ///
            /// If there are 0 sized items at the address, the last item will be returned.
            /// </summary>
            /// <param name="address"></param>
            /// <param name="onlyDirectAddressMatches"></param>
            /// <remarks> CAUTION: For data where regions can overlap, e.g. <seealso cref="SortedNativeMemoryRegionEntriesCache"/>
            /// this will find the deepest nested region, not any potential enclosing regions.</remarks>
            /// <returns> Index of the value within the <seealso cref="IndirectlySortedEntriesCache"/>.
            /// -1 means the item wasn't found.</returns>
            public long Find(ulong address, bool onlyDirectAddressMatches)
            {
                var idx = DynamicArrayAlgorithms.BinarySearch(this, address);
                if (idx < 0)
                {
                    // -1 means the address is smaller than the first Address, early out with -1
                    if (idx == -1 || onlyDirectAddressMatches)
                        return -1;
                    // otherwise, a negative Index just means there was no match of the any address range (yet matching with a range of size 0 if the address matches)
                    // and ~idx - 1 will give us the index to the next smaller Address
                    idx = ~idx - 1;
                }
                var foundAddress = Address(idx);
                if (address == foundAddress)
                    return idx;
                if (onlyDirectAddressMatches)
                    return -1;
                var size = Size(idx);
                if (address > foundAddress && (address < (foundAddress + size) || size == 0))
                {
                    return idx;
                }
                return -1;
            }
        }

        /// <summary>
        /// User for entry caches that don't have any overlaps and no items of size 0 (or if, not right next to each other,
        /// i.e. <see cref="SortedNativeObjects"/> are fine to use this instead of <see cref="IndirectlySortedEntriesCacheSortedByAddressAndSizeArray"/>
        /// as while some Native Objects may report a size of 0, their addresses will never match
        /// </summary>
        public abstract class IndirectlySortedEntriesCacheSortedByAddressArray : IndirectlySortedEntriesCache<DynamicArrayAlgorithms.IndexedArrayValueComparer<ulong>>
        {
            protected unsafe override DynamicArrayAlgorithms.IndexedArrayValueComparer<ulong> SortingComparer =>
                new DynamicArrayAlgorithms.IndexedArrayValueComparer<ulong>(in Addresses);
            public IndirectlySortedEntriesCacheSortedByAddressArray(CachedSnapshot snapshot) : base(snapshot) { }
            protected abstract ref readonly DynamicArray<ulong> Addresses { get; }
            public override ulong Address(long index) => Addresses[this[index]];
        }

        /// <summary>
        /// Used for entry caches that can have overlapping regions or those which border right next to each other while having sizes of 0
        /// </summary>
        public abstract class IndirectlySortedEntriesCacheSortedByAddressAndSizeArray : IndirectlySortedEntriesCache<DynamicArrayAlgorithms.IndexedArrayRangeValueComparer<ulong>>
        {
            protected unsafe override DynamicArrayAlgorithms.IndexedArrayRangeValueComparer<ulong> SortingComparer =>
                new DynamicArrayAlgorithms.IndexedArrayRangeValueComparer<ulong>(in Addresses, in Sizes);
            public IndirectlySortedEntriesCacheSortedByAddressAndSizeArray(CachedSnapshot snapshot) : base(snapshot) { }
            protected abstract ref readonly DynamicArray<ulong> Addresses { get; }
            protected abstract ref readonly DynamicArray<ulong> Sizes { get; }
            public override ulong Address(long index) => Addresses[this[index]];
            public override ulong Size(long index) => Sizes[this[index]];
        }


        public class SortedNativeMemoryRegionEntriesCache : IndirectlySortedEntriesCacheSortedByAddressAndSizeArray
        {
            public readonly DynamicArray<byte> RegionHierarchLayer;
            public SortedNativeMemoryRegionEntriesCache(CachedSnapshot snapshot) : base(snapshot)
            {
                RegionHierarchLayer = new DynamicArray<byte>(Count, Allocator.Persistent);
            }

            public override long Count => m_Snapshot.NativeMemoryRegions.Count;

            protected override ref readonly DynamicArray<ulong> Addresses => ref m_Snapshot.NativeMemoryRegions.AddressBase;
            protected override ref readonly DynamicArray<ulong> Sizes => ref m_Snapshot.NativeMemoryRegions.AddressSize;
            public string Name(long index) => m_Snapshot.NativeMemoryRegions.MemoryRegionName[this[index]];
            public int UnsortedParentRegionIndex(long index) => m_Snapshot.NativeMemoryRegions.ParentIndex[this[index]];
            public int UnsortedFirstAllocationIndex(long index) => m_Snapshot.NativeMemoryRegions.FirstAllocationIndex[this[index]];
            public int UnsortedNumAllocations(long index) => m_Snapshot.NativeMemoryRegions.NumAllocations[this[index]];

            public override void Preload()
            {
                base.Preload();
                var count = Count;
                if (count <= 0)
                    return;

                using var regionLayerStack = new DynamicArray<(sbyte, ulong)>(10, Allocator.Temp, memClear: false);
                regionLayerStack.Clear(stomp: false);
                regionLayerStack.Push(new(-1, Address(count - 1) + Size(count - 1)));
                for (long i = 0; i < count; i++)
                {
                    // avoid the copy
                    ref readonly var enclosingRegion = ref regionLayerStack.Peek();
                    var regionEnd = Address(i) + Size(i);

                    while (regionEnd > enclosingRegion.Item2)
                    {
                        // pop layer stack until the enclosung region encompases this region
                        regionLayerStack.Pop();
                        enclosingRegion = ref regionLayerStack.Peek();
                    }

                    var currentLayer = enclosingRegion.Item1;
                    regionLayerStack.Push(new(++currentLayer, Address(i)));
                    RegionHierarchLayer[i] = (byte)currentLayer;
                }
            }
            public override void Dispose()
            {
                base.Dispose();
                RegionHierarchLayer.Dispose();
            }
        }

        public class SortedManagedObjectsCache : IndirectlySortedEntriesCache<SortedManagedObjectsCache.IndexedManagedObjectAddressComparer>
        {
            /// <summary>
            /// This comparer is used to sort <seealso cref="IndirectlySortedEntriesCache.m_Sorting"/>
            /// (which is array of indices of, in the cas of this class, all <seealso cref="ManagedData.ManagedObjects"/>)
            /// based on the address of each object.
            ///
            /// This helper struct is needed as <seealso cref="ManagedObjectInfo"/> does not implement <seealso cref="IComparable{ManagedObjectInfo}"/>
            /// as it would be unclear what parameters it would compare by, and if it would forcibly be the address, that could be rather confusing.
            /// Especially since it is only needed here.
            /// </summary>
            public unsafe readonly struct IndexedManagedObjectAddressComparer : IRefComparer<long>
            {
                readonly ManagedObjectInfo* m_Ptr;
                public IndexedManagedObjectAddressComparer(in DynamicArray<ManagedObjectInfo> managedObjectInfos)
                {
                    m_Ptr = managedObjectInfos.GetUnsafeTypedPtr();
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public int Compare(ref long objectAIndex, ref long objectBIndex)
                {
                    // managed objects can't have a negative size, or be nested within other managed objects, so we don't need to compare sizes as well
                    return m_Ptr[objectAIndex].PtrObject.CompareTo(m_Ptr[objectBIndex].PtrObject);
                }
            }

            public SortedManagedObjectsCache(CachedSnapshot snapshot) : base(snapshot) { }


            public override long Count => m_Snapshot.CrawledData.ManagedObjects.Count;

            protected override IndexedManagedObjectAddressComparer SortingComparer => new IndexedManagedObjectAddressComparer(in m_Snapshot.CrawledData.ManagedObjects);

            public override ulong Address(long index) => m_Snapshot.CrawledData.ManagedObjects[this[index]].PtrObject;
            public override ulong Size(long index) => (ulong)m_Snapshot.CrawledData.ManagedObjects[this[index]].Size;
        }

        public class SortedNativeAllocationsCache : IndirectlySortedEntriesCacheSortedByAddressAndSizeArray
        {
            public SortedNativeAllocationsCache(CachedSnapshot snapshot) : base(snapshot) { }

            public override long Count => m_Snapshot.NativeAllocations.Count;

            protected override ref readonly DynamicArray<ulong> Addresses => ref m_Snapshot.NativeAllocations.Address;
            protected override ref readonly DynamicArray<ulong> Sizes => ref m_Snapshot.NativeAllocations.Size;

            public int MemoryRegionIndex(long index) => m_Snapshot.NativeAllocations.MemoryRegionIndex[this[index]];
            public long RootReferenceId(long index) => m_Snapshot.NativeAllocations.RootReferenceId[this[index]];
            public long AllocationSiteId(long index) => m_Snapshot.NativeAllocations.AllocationSiteId[this[index]];
            public int OverheadSize(long index) => m_Snapshot.NativeAllocations.OverheadSize[this[index]];
            public int PaddingSize(long index) => m_Snapshot.NativeAllocations.PaddingSize[this[index]];
        }

        public class SortedNativeObjectsCache : IndirectlySortedEntriesCacheSortedByAddressArray
        {
            public SortedNativeObjectsCache(CachedSnapshot snapshot) : base(snapshot) { }
            public override long Count => m_Snapshot.NativeObjects.Count;

            protected override ref readonly DynamicArray<ulong> Addresses => ref m_Snapshot.NativeObjects.NativeObjectAddress;
            public override ulong Size(long index) => m_Snapshot.NativeObjects.Size[this[index]];

            public string Name(long index) => m_Snapshot.NativeObjects.ObjectName[this[index]];
            public int InstanceId(long index) => m_Snapshot.NativeObjects.InstanceId[this[index]];
            public int NativeTypeArrayIndex(long index) => m_Snapshot.NativeObjects.NativeTypeArrayIndex[this[index]];
            public HideFlags HideFlags(long index) => m_Snapshot.NativeObjects.HideFlags[this[index]];
            public ObjectFlags Flags(long index) => m_Snapshot.NativeObjects.Flags[this[index]];
            public long RootReferenceId(long index) => m_Snapshot.NativeObjects.RootReferenceId[this[index]];
            public int Refcount(long index) => m_Snapshot.NativeObjects.RefCount[this[index]];
            public int ManagedObjectIndex(long index) => m_Snapshot.NativeObjects.ManagedObjectIndex[this[index]];
        }


        unsafe static void ConvertDynamicArrayByteBufferToManagedArray<T>(DynamicArray<byte> nativeEntryBuffer, ref T[] elements) where T : class
        {
            byte* binaryDataStream = nativeEntryBuffer.GetUnsafeTypedPtr();
            //jump over the offsets array
            long* binaryEntriesLength = (long*)binaryDataStream;
            binaryDataStream = binaryDataStream + sizeof(long) * (elements.Length + 1); //+1 due to the final element offset being at the end

            for (int i = 0; i < elements.Length; ++i)
            {
                byte* srcPtr = binaryDataStream + binaryEntriesLength[i];
                long actualLength = binaryEntriesLength[i + 1] - binaryEntriesLength[i];

                if (typeof(T) == typeof(string))
                {
                    var intLength = Convert.ToInt32(actualLength);
                    elements[i] = new string(unchecked((sbyte*)srcPtr), 0, intLength, System.Text.Encoding.UTF8) as T;
                }
                else if (typeof(T) == typeof(BitArray))
                {
                    byte[] temp = new byte[actualLength];
                    fixed (byte* dstPtr = temp)
                        UnsafeUtility.MemCpy(dstPtr, srcPtr, actualLength);

                    var arr = new BitArray(temp);
                    elements[i] = arr as T;
                }
                else
                {
                    Debug.LogError($"Use {nameof(NestedDynamicArray<byte>)} instead");
                    if (typeof(T) == typeof(byte[]))
                    {
                        var arr = new byte[actualLength];
                        fixed (void* dstPtr = arr)
                            UnsafeUtility.MemCpy(dstPtr, srcPtr, actualLength);
                        elements[i] = arr as T;
                    }
                    else if (typeof(T) == typeof(int[]))
                    {
                        var arr = new int[actualLength / UnsafeUtility.SizeOf<int>()];
                        fixed (void* dstPtr = arr)
                            UnsafeUtility.MemCpy(dstPtr, srcPtr, actualLength);
                        elements[i] = arr as T;
                    }
                    else if (typeof(T) == typeof(ulong[]))
                    {
                        var arr = new ulong[actualLength / UnsafeUtility.SizeOf<ulong>()];
                        fixed (void* dstPtr = arr)
                            UnsafeUtility.MemCpy(dstPtr, srcPtr, actualLength);
                        elements[i] = arr as T;
                    }
                    else if (typeof(T) == typeof(long[]))
                    {
                        var arr = new long[actualLength / UnsafeUtility.SizeOf<long>()];
                        fixed (void* dstPtr = arr)
                            UnsafeUtility.MemCpy(dstPtr, srcPtr, actualLength);
                        elements[i] = arr as T;
                    }
                    else
                    {
                        Debug.LogErrorFormat("Unsuported type provided for conversion, type name: {0}", typeof(T).FullName);
                    }
                }
            }
        }

        /// <summary>
        /// Compact (64bit) and type-safe index into CachedSnapshot data.
        ///
        /// Nb! SourceIndex doesn't provide safety checks that the correct CachedSnapshot
        /// is used with the index and it's user's responsibility to pair index with
        /// the correct snapshot.
        /// </summary>
        readonly public struct SourceIndex : IEquatable<SourceIndex>
        {
            const string kInvalidItemName = "<No name>";
            const int kSourceIdShift = 56;
            const long kSourceIdMask = 0xFF;
            const long kIndexMask = 0x00FFFFFFFFFFFFFF;

            public enum SourceId : byte
            {
                None,
                SystemMemoryRegion,
                NativeMemoryRegion,
                NativeAllocation,
                ManagedHeapSection,
                NativeObject,
                ManagedObject,
                NativeType,
                ManagedType,
                NativeRootReference,
                GfxResource,
            }

            readonly ulong m_Data;

            public readonly SourceId Id
            {
                get => (SourceId)(m_Data >> kSourceIdShift);
            }

            public readonly long Index
            {
                get => (long)(m_Data & kIndexMask);
            }

            public bool Valid => Id != SourceId.None;

            public SourceIndex(SourceId source, long index)
            {
                if (((int)source > kSourceIdMask) || (index > kIndexMask))
                    throw new ArgumentOutOfRangeException();

                m_Data = ((ulong)source << kSourceIdShift) | (ulong)index;
            }

            public string GetName(CachedSnapshot snapshot)
            {
                switch (Id)
                {
                    case SourceId.None:
                        return string.Empty;

                    case SourceId.SystemMemoryRegion:
                    {
                        var name = snapshot.SystemMemoryRegions.RegionName[Index];
                        if (string.IsNullOrEmpty(name))
                        {
                            var regionType = (SystemMemoryRegionEntriesCache.MemoryType)snapshot.SystemMemoryRegions.RegionType[Index];
                            if (regionType != SystemMemoryRegionEntriesCache.MemoryType.Mapped)
                                name = regionType.ToString();
                        }
                        return name;
                    }

                    case SourceId.NativeMemoryRegion:
                        return snapshot.NativeMemoryRegions.MemoryRegionName[Index];
                    case SourceId.NativeAllocation:
                    {
                        // Check if we have memory label roots information
                        if (snapshot.NativeAllocations.RootReferenceId.Count <= 0)
                            return kInvalidItemName;

                        // Check if allocation has memory label root
                        var rootReferenceId = snapshot.NativeAllocations.RootReferenceId[Index];
                        if (rootReferenceId <= 0)
                            return kInvalidItemName;

                        // Lookup native object index associated with memory label root
                        if (snapshot.NativeObjects.RootReferenceIdToIndex.TryGetValue(rootReferenceId, out var objectIndex))
                            return snapshot.NativeObjects.ObjectName[objectIndex];

                        // Try to see is memory label root associated with any memory area
                        if (snapshot.NativeRootReferences.IdToIndex.TryGetValue(rootReferenceId, out long rootIndex))
                            return snapshot.NativeRootReferences.AreaName[rootIndex] + ":" + snapshot.NativeRootReferences.ObjectName[rootIndex];

                        return kInvalidItemName;
                    }
                    case SourceId.NativeObject:
                        return snapshot.NativeObjects.ObjectName[Index];
                    case SourceId.NativeType:
                        return snapshot.NativeTypes.TypeName[Index];
                    case SourceId.NativeRootReference:
                        return snapshot.NativeRootReferences.ObjectName[Index];

                    case SourceId.ManagedHeapSection:
                        return snapshot.ManagedHeapSections.SectionName[Index];
                    case SourceId.ManagedObject:
                    {
                        return snapshot.CrawledData.ManagedObjects[Index].ProduceManagedObjectName(snapshot);
                    }
                    case SourceId.ManagedType:
                        return snapshot.TypeDescriptions.TypeDescriptionName[Index];


                    case SourceId.GfxResource:
                    {
                        // Get associated memory label root
                        var rootReferenceId = snapshot.NativeGfxResourceReferences.RootId[Index];
                        if (rootReferenceId <= 0)
                            return kInvalidItemName;

                        // Lookup native object index associated with memory label root
                        if (snapshot.NativeObjects.RootReferenceIdToIndex.TryGetValue(rootReferenceId, out var objectIndex))
                            return snapshot.NativeObjects.ObjectName[objectIndex];

                        // Try to see is memory label root associated with any memory area
                        if (snapshot.NativeRootReferences.IdToIndex.TryGetValue(rootReferenceId, out long rootIndex))
                            return snapshot.NativeRootReferences.AreaName[rootIndex] + ":" + snapshot.NativeRootReferences.ObjectName[rootIndex];

                        return kInvalidItemName;
                    }
                }

                Debug.Assert(false, $"Unknown source link type {Id}, please report a bug.");
                return kInvalidItemName;
            }

            public bool Equals(SourceIndex other) => m_Data == other.m_Data;

            public override bool Equals(object obj) => obj is SourceIndex other && Equals(other);

            public override int GetHashCode() => m_Data.GetHashCode();

            public override string ToString()
            {
                return $"(Source:{Id} Index:{Index})";
            }
        }

        public class EntriesMemoryMapCache : IDisposable
        {
            // We assume this address space not to be used in the real life
            const ulong kGraphicsResourcesStartFakeAddress = 0x8000_0000_0000_0000UL;

            public enum AddressPointType : byte
            {
                EndPoint,
                StartPoint,
            }

            readonly public struct AddressPoint : IComparable<AddressPoint>
            {
                public readonly ulong Address;
                public readonly long ChildrenCount;
                public readonly SourceIndex Source;
                public readonly AddressPointType PointType;

                public AddressPoint(ulong address, long childrenCount, SourceIndex source, AddressPointType pointType)
                {
                    Address = address;
                    ChildrenCount = childrenCount;
                    Source = source;
                    PointType = pointType;
                }

                public AddressPoint(AddressPoint point, long childrenCount)
                {
                    Address = point.Address;
                    Source = point.Source;
                    PointType = point.PointType;
                    ChildrenCount = childrenCount;
                }

                int IComparable<AddressPoint>.CompareTo(AddressPoint other)
                {
                    var ret = Address.CompareTo(other.Address);
                    if (ret == 0)
                    {
                        bool isEndPointA = PointType == AddressPointType.EndPoint;
                        bool isEndPointB = other.PointType == AddressPointType.EndPoint;

                        if (isEndPointA != isEndPointB)
                        {
                            // Comparing start to end point, end point should be first
                            return isEndPointA ? -1 : 1;
                        }
                        else
                        {
                            // Comparing start to start or end to end, prioritize by source type
                            ret = Source.Id.CompareTo(other.Source.Id);
                            return isEndPointA ? -ret : ret;
                        }
                    }
                    return ret;
                }
            }

            CachedSnapshot m_Snapshot;
            long m_ItemsCount;
            // The snapshot can easily contain int.MaxValue amount of objects, even more easily half that in Managed Objects
            // and very realistically more than int.MaxValue elements for the total that this list would contain.
            // This therefore has to be a long indexed data type like DynamicArray.
            DynamicArray<AddressPoint> m_CombinedData;

            public EntriesMemoryMapCache(CachedSnapshot snapshot)
            {
                m_Snapshot = snapshot;
                m_ItemsCount = 0;
                m_CombinedData = default;
            }

            internal EntriesMemoryMapCache(DynamicArray<AddressPoint> m_Data)
            {
                m_Snapshot = null;
                m_ItemsCount = m_Data.Count;
                m_CombinedData = m_Data;

                SortPoints();
                PostProcess();
            }

            public long Count
            {
                get
                {
                    // Count is used to trigger Build() from performance tests without using reflection and much other overhead.
                    // Adjust Performance tests if this logic changes.
                    Build();
                    return m_ItemsCount;
                }
            }

            public DynamicArray<AddressPoint> Data
            {
                get
                {
                    Build();
                    return m_CombinedData;
                }
            }

            public delegate void ForEachAction(long index, ulong address, ulong size, SourceIndex source);

            /// <summary>
            /// Iterates all children memory spans under specified parent index.
            /// Use -1 parentIndex for a root level
            /// </summary>
            public void ForEachChild(long parentIndex, ForEachAction action)
            {
                Build();

                if (m_ItemsCount == 0)
                    return;

                // Children iteration rules:
                // - Children go right after the parent in the data array
                // - If children starts not at the parent start address,
                //   we generate "fake" child of the parent type, which should be
                //   treated as "reserved" space
                // - If children ends before the parent end point,
                //   we generate "fake" child of the parent type, which should be
                //   treated as "reserved" space
                var index = parentIndex == -1 ? 0 : parentIndex;
                var endIndex = parentIndex == -1 ? m_ItemsCount - 1 : index + m_CombinedData[parentIndex].ChildrenCount + 1;
                var nextIndex = index + 1;
                while (index < endIndex)
                {
                    var point = m_CombinedData[index];
                    var nextPoint = m_CombinedData[nextIndex];
                    var size = nextPoint.Address - point.Address;

                    // Don't report on free, ignored or zero-sized spans
                    if ((point.Source.Id != SourceIndex.SourceId.None) && (size > 0))
                        action(index, point.Address, size, point.Source);

                    index = nextIndex;
                    nextIndex = index + 1 + nextPoint.ChildrenCount;
                }
            }

            /// <summary>
            /// Flat scan all memory regions
            /// </summary>
            public void ForEachFlat(ForEachAction action)
            {
                Build();

                if (m_ItemsCount == 0)
                    return;

                SourceIndex? currentSystemRegion = null;
                for (long i = 0; i < m_ItemsCount - 1; i++)
                {
                    var cur = m_CombinedData[i];

                    // Register current system region
                    if (cur.Source.Id == SourceIndex.SourceId.SystemMemoryRegion)
                        currentSystemRegion = cur.Source;
                    else if (cur.Source.Id == SourceIndex.SourceId.None)
                        currentSystemRegion = null;

                    // Ignore free memory regions
                    if (cur.Source.Id == SourceIndex.SourceId.None)
                        continue;

                    if (m_Snapshot.HasSystemMemoryRegionsInfo)
                    {
                        // Ignore items outside of system regions
                        // They exist due to time difference between allocation
                        // capture and regions capture
                        //
                        // Nb! Special case for gfx resources, as we add them
                        // with fake address and without system regions root
                        if (!currentSystemRegion.HasValue && (cur.Source.Id != SourceIndex.SourceId.GfxResource))
                            continue;
                    }

                    // Calculate size
                    var next = m_CombinedData[i + 1];
                    var size = next.Address - cur.Address;

                    // Ignore zero sized enitites
                    if (size == 0)
                        continue;

                    action(i, cur.Address, size, cur.Source);
                }
            }

            public delegate void ForEachFlatWithResidentSizeAction(long index, ulong address, ulong size, ulong residentSize, SourceIndex source);

            /// <summary>
            /// Flat scan all memory regions
            /// </summary>
            public void ForEachFlatWithResidentSize(ForEachFlatWithResidentSizeAction action)
            {
                Build();

                if (m_ItemsCount == 0)
                    return;

                SourceIndex? currentSystemRegion = null;
                for (long i = 0; i < m_ItemsCount - 1; i++)
                {
                    var cur = m_CombinedData[i];

                    // Register current system region
                    if (cur.Source.Id == SourceIndex.SourceId.SystemMemoryRegion)
                        currentSystemRegion = cur.Source;
                    else if (cur.Source.Id == SourceIndex.SourceId.None)
                        currentSystemRegion = null;

                    // Ignore free memory regions
                    if (cur.Source.Id == SourceIndex.SourceId.None)
                        continue;

                    // Calculate size
                    var next = m_CombinedData[i + 1];
                    var size = next.Address - cur.Address;

                    // Ignore zero sized enitites
                    if (size == 0)
                        continue;

                    var residentSize = 0UL;
                    if (m_Snapshot.HasSystemMemoryRegionsInfo)
                    {
                        if (cur.Source.Id == SourceIndex.SourceId.GfxResource)
                        {
                            // Special case for gfx resources
                            // We consider them to be always resident
                            // and they can be outside of system regions
                            residentSize = size;
                        }
                        else if (!currentSystemRegion.HasValue)
                        {
                            // Ignore items outside of system regions
                            // They exist due to time difference between allocation
                            // capture and regions capture
                            continue;
                        }
                        else if (m_Snapshot.HasSystemMemoryResidentPages)
                        {
                            // Calculate resident size based on the root
                            // system region this object resides in
                            residentSize = m_Snapshot.SystemMemoryResidentPages.CalculateResidentMemory(currentSystemRegion.Value.Index, cur.Address, size);
                        }
                    }

                    action(i, cur.Address, size, residentSize, cur.Source);
                }
            }

            public string GetName(long index)
            {
                if (m_Snapshot == null)
                    return string.Empty;

                var item = m_CombinedData[index];
                return item.Source.GetName(m_Snapshot);
            }

            public enum PointType
            {
                Free,
                Untracked,
                NativeReserved,
                Native,
                ManagedReserved,
                Managed,
                Device,
                Mapped,
                Shared,
                AndroidRuntime,
            }

            public PointType GetPointType(SourceIndex source)
            {
                switch (source.Id)
                {
                    case SourceIndex.SourceId.None:
                        return PointType.Free;
                    case SourceIndex.SourceId.SystemMemoryRegion:
                    {
                        if (m_Snapshot.MetaData.TargetInfo.HasValue)
                        {
                            switch (m_Snapshot.MetaData.TargetInfo.Value.RuntimePlatform)
                            {
                                case RuntimePlatform.Android:
                                {
                                    var name = m_Snapshot.SystemMemoryRegions.RegionName[source.Index];
                                    if (name.StartsWith("[anon:dalvik-"))
                                        return PointType.AndroidRuntime;
                                    else if (name.StartsWith("/dev/ashmem/dalvik-"))
                                        return PointType.AndroidRuntime;
                                    else if (name.StartsWith("/dev/"))
                                        return PointType.Device;
                                    break;
                                }
                                default:
                                    break;
                            }
                        }

                        var regionType = m_Snapshot.SystemMemoryRegions.RegionType[source.Index];
                        switch ((SystemMemoryRegionEntriesCache.MemoryType)regionType)
                        {
                            case SystemMemoryRegionEntriesCache.MemoryType.Device:
                                return PointType.Device;
                            case SystemMemoryRegionEntriesCache.MemoryType.Mapped:
                                return PointType.Mapped;
                            case SystemMemoryRegionEntriesCache.MemoryType.Shared:
                                return PointType.Shared;
                            default:
                                return PointType.Untracked;
                        }
                    }

                    case SourceIndex.SourceId.NativeMemoryRegion:
                        return PointType.NativeReserved;
                    case SourceIndex.SourceId.NativeAllocation:
                    {
                        if (m_Snapshot.MetaData.TargetInfo.HasValue &&
                            m_Snapshot.MetaData.TargetInfo.Value.RuntimePlatform == RuntimePlatform.Switch)
                        {
                            // On Switch we see some "Native" allocations which are actually graphics memory.
                            // This is likely an issue for us because Switch has the combination of being
                            // unified memory that uses BaseAllocators for graphics, with no VirtualQuery equivalent.
                            // See what region the memory was allocated in to let us decide how to tag it.
                            var memIndex = m_Snapshot.NativeAllocations.MemoryRegionIndex[source.Index];
                            var parentIndex = m_Snapshot.NativeMemoryRegions.ParentIndex[memIndex];
                            memIndex = (parentIndex != 0) ? parentIndex : memIndex;

                            if (memIndex == m_Snapshot.NativeMemoryRegions.SwitchGPUAllocatorIndex)
                                return PointType.Device;
                        }

                        return PointType.Native;
                    }
                    case SourceIndex.SourceId.NativeObject:
                        return PointType.Native;

                    case SourceIndex.SourceId.ManagedHeapSection:
                    {
                        var sectionType = m_Snapshot.ManagedHeapSections.SectionType[source.Index];
                        switch (sectionType)
                        {
                            case MemorySectionType.VirtualMachine:
                                return PointType.Managed;
                            case MemorySectionType.GarbageCollector:
                                return PointType.ManagedReserved;
                            default:
                                Debug.Assert(false, $"Unknown managed heap section type {sectionType}, please report a bug.");
                                return PointType.ManagedReserved;
                        }
                    }
                    case SourceIndex.SourceId.ManagedObject:
                        return PointType.Managed;

                    case SourceIndex.SourceId.GfxResource:
                        return PointType.Device;

                    default:
                        Debug.Assert(false, $"Unknown source link type {source.Id}, please report a bug.");
                        return PointType.Free;
                }
            }

            // These markers are used in Performance tests. If they are changed or no longer used, adjust the tests accordingly
            public const string BuildMarkerName = "EntriesMemoryMapCache.Build";
            public const string BuildAddPointsMarkerName = "EntriesMemoryMapCache.AddPoints";
            public const string BuildSortPointsMarkerName = "EntriesMemoryMapCache.SortPoints";
            public const string BuildPostProcessMarkerName = "EntriesMemoryMapCache.PostProcess";

            static readonly ProfilerMarker k_Build = new ProfilerMarker(BuildMarkerName);
            static readonly ProfilerMarker k_BuildAddPoints = new ProfilerMarker(BuildAddPointsMarkerName);
            static readonly ProfilerMarker k_BuildSortPoints = new ProfilerMarker(BuildSortPointsMarkerName);
            static readonly ProfilerMarker k_BuildPostProcess = new ProfilerMarker(BuildPostProcessMarkerName);

            void Build()
            {
                using var _ = k_Build.Auto();

                if (m_CombinedData.IsCreated)
                    return;

                using (k_BuildAddPoints.Auto())
                    AddPoints();

                using (k_BuildSortPoints.Auto())
                    SortPoints();

                using (k_BuildPostProcess.Auto())
                    PostProcess();
            }

            void AddPoints()
            {
                // We're building a sorted by address list of ranges with
                // a link to a source of the data
                var maxPointsCount = (
                    m_Snapshot.SystemMemoryRegions.Count +
                    m_Snapshot.NativeMemoryRegions.Count +
                    m_Snapshot.ManagedHeapSections.Count +
                    m_Snapshot.NativeAllocations.Count +
                    m_Snapshot.NativeObjects.Count +
                    m_Snapshot.CrawledData.ManagedObjects.Count +
                    m_Snapshot.NativeGfxResourceReferences.Count) * 2;
                m_CombinedData = new DynamicArray<AddressPoint>(maxPointsCount, Allocator.Persistent);

                // Add OS reported system memory regions
                m_ItemsCount = AddPoints(
                    m_ItemsCount,
                    m_CombinedData,
                    SourceIndex.SourceId.SystemMemoryRegion,
                    Convert.ToInt32(m_Snapshot.SystemMemoryRegions.Count),
                    (long i) =>
                    {
                        var address = m_Snapshot.SystemMemoryRegions.RegionAddress[i];
                        var size = m_Snapshot.SystemMemoryRegions.RegionSize[i];
                        var name = m_Snapshot.SystemMemoryRegions.RegionName[i];
                        var type = m_Snapshot.SystemMemoryRegions.RegionType[i];
                        return (true, address, size);
                    });

                // Add MemoryManager native allocators allocated memory
                // By default it's treated as "reserved" unless allocations
                // are reported
                m_ItemsCount = AddPoints(
                    m_ItemsCount,
                    m_CombinedData,
                    SourceIndex.SourceId.NativeMemoryRegion,
                    Convert.ToInt32(m_Snapshot.NativeMemoryRegions.Count),
                    (long i) =>
                    {
                        var address = m_Snapshot.NativeMemoryRegions.AddressBase[i];
                        var size = m_Snapshot.NativeMemoryRegions.AddressSize[i];
                        var name = m_Snapshot.NativeMemoryRegions.MemoryRegionName[i];

                        // We exclude "virtual" allocators as they report non-committed memory
                        bool valid = (size > 0) && !name.Contains("Virtual Memory") && address != 0UL;

                        return (valid, address, size);
                    });

                // Add Mono reported managed sections
                m_ItemsCount = AddPoints(
                    m_ItemsCount,
                    m_CombinedData,
                    SourceIndex.SourceId.ManagedHeapSection,
                    Convert.ToInt32(m_Snapshot.ManagedHeapSections.Count),
                    (long i) =>
                    {
                        var address = m_Snapshot.ManagedHeapSections.StartAddress[i];
                        var size = m_Snapshot.ManagedHeapSections.SectionSize[i];
                        var sectionType = m_Snapshot.ManagedHeapSections.SectionType[i];
                        bool valid = (size > 0);
                        return (valid, address, size);
                    });

                // Add individual native allocations
                m_ItemsCount = AddPoints(
                    m_ItemsCount,
                    m_CombinedData,
                    SourceIndex.SourceId.NativeAllocation,
                    Convert.ToInt32(m_Snapshot.NativeAllocations.Count),
                    (long i) =>
                    {
                        var address = m_Snapshot.NativeAllocations.Address[i];
                        var size = m_Snapshot.NativeAllocations.Size[i];
                        bool valid = (size > 0);
                        return (valid, address, size);
                    });

                // Add native objects
                m_ItemsCount = AddPoints(
                    m_ItemsCount,
                    m_CombinedData,
                    SourceIndex.SourceId.NativeObject,
                    Convert.ToInt32(m_Snapshot.NativeObjects.Count),
                    (long i) =>
                    {
                        var address = m_Snapshot.NativeObjects.NativeObjectAddress[i];
                        var size = m_Snapshot.NativeObjects.Size[i];
                        bool valid = (size > 0);
                        return (valid, address, size);
                    });

                // Add managed objects
                m_ItemsCount = AddPoints(
                    m_ItemsCount,
                    m_CombinedData,
                    SourceIndex.SourceId.ManagedObject,
                    Convert.ToInt32(m_Snapshot.CrawledData.ManagedObjects.Count),
                    (long i) =>
                    {
                        ref readonly var info = ref m_Snapshot.CrawledData.ManagedObjects[i];
                        var address = info.PtrObject;
                        var size = (ulong)info.Size;
                        bool valid = (size > 0);
                        return (valid, address, size);
                    });
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            bool IsEndPoint(AddressPoint p) => p.PointType == AddressPointType.EndPoint;

            // We use ChildCount to store begin/end pair IDs, so that
            // we can check that they are from the same pair
            // ChildrenCount is updated to be the actual ChildrenCount in PostProcess once a point is ended and removed from the processing stack.
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            long GetPointId(AddressPoint p) => p.ChildrenCount;

            void SortPoints()
            {
                DynamicArrayAlgorithms.IntrospectiveSort(m_CombinedData, 0, m_ItemsCount);
            }

            /// <summary>
            /// Scans all points and updates flags and childs count
            /// based on begin/end flags
            /// </summary>
            void PostProcess()
            {
                const int kMaxStackDepth = 16;
                var hierarchyStack = new long[kMaxStackDepth];
                var hierarchyStackCount = 0;
                for (long i = 0; i < m_ItemsCount; i++)
                {
                    var point = m_CombinedData[i];

                    if (IsEndPoint(point))
                    {
                        if (hierarchyStackCount <= 0)
                        {
                            // Lose end point. This is valid situation as memory snapshot
                            // capture process modifies memory and system, native and managed
                            // states might be slighly out of sync and have overlapping regions
                            m_CombinedData[i] = new AddressPoint(point.Address, 0, new SourceIndex(), point.PointType);
                            continue;
                        }

                        // We use ChildCount to store begin/end pair IDs, so that
                        // we can check that they are from the same pair
                        var startPointIndex = hierarchyStack[hierarchyStackCount - 1];
                        var startPoint = m_CombinedData[startPointIndex];
                        if (GetPointId(startPoint) != GetPointId(point))
                        {
                            // Non-matching end point. This is valid situation (see "lose end point" comment).
                            // Try to find matching starting point
                            var index = Array.FindIndex(hierarchyStack, 0, hierarchyStackCount, (x) => GetPointId(m_CombinedData[x]) == GetPointId(point));
                            if (index < 0)
                            {
                                // No starting point, ignore the point entirely
                                // and set it's source to the previous point source
                                // as it should be treated as a continuation of the
                                // previous object
                                m_CombinedData[i] = new AddressPoint(point.Address, 0, m_CombinedData[i - 1].Source, point.PointType);
                                continue;
                            }

                            TermiateUndercutRegions(index, hierarchyStackCount, hierarchyStack, i);
                            // if there is matching begin -> unwind stack to that point
                            startPointIndex = hierarchyStack[index];
                            hierarchyStackCount = index + 1;
                        }

                        // Remove from stack
                        hierarchyStackCount--;

                        // Replace start point id with actual children count
                        m_CombinedData[startPointIndex] = new AddressPoint(m_CombinedData[startPointIndex], i - startPointIndex - 1);

                        // Replace end point with continuation of the parent range
                        if (hierarchyStackCount > 0)
                        {
                            var parentPointIndex = hierarchyStack[hierarchyStackCount - 1];
                            var parentPoint = m_CombinedData[parentPointIndex];
                            m_CombinedData[i] = new AddressPoint(point.Address, 0, parentPoint.Source, point.PointType);
                        }
                        else
                        {
                            // Last in the hierarchy, restart free region range
                            m_CombinedData[i] = new AddressPoint(point.Address, 0, new SourceIndex(), point.PointType);
                        }
                    }
                    else
                    {
                        if (hierarchyStackCount > 0 && m_CombinedData[hierarchyStack[hierarchyStackCount - 1]].Source.Id == point.Source.Id)
                        {
                            // The element this element is supposedly nested within is of the same type. Nesting of same types points at incorrect data
                            var parentPointIndex = hierarchyStack[hierarchyStackCount - 1];
                            var parentPoint = m_CombinedData[parentPointIndex];

                            var startPointStackLevel = hierarchyStackCount - 1;
                            TermiateUndercutRegions(startPointStackLevel, hierarchyStackCount, hierarchyStack, i);
                            var startPointIndex = hierarchyStack[startPointStackLevel];
                            // Replace start point id with actual children count
                            m_CombinedData[startPointIndex] = new AddressPoint(m_CombinedData[startPointIndex], i - startPointIndex - 1);

                            hierarchyStackCount = startPointStackLevel;

#if DEBUG_VALIDATION
                            // For Native Objects this is a known issue as their GPU size was included in older versions of the backend
                            // So we only report this issue for newer snapshot versions as a reminder to fix it (i.e. bumping the above version is fine but).
                            // Other types having the same issue is not a known issue, so we'd want to know about it for these
                            if (point.Source.Id != SourceIndex.SourceId.NativeObject || m_Snapshot.m_SnapshotVersion > FormatVersion.SystemMemoryResidentPagesVersion)
                                Debug.LogWarning($"The snapshot contains faulty data, an item of type {point.Source.Id} was nested within an item of the same type (index {i})!");
#endif
                        }

                        hierarchyStack[hierarchyStackCount] = i;
                        hierarchyStackCount++;
                    }
                }
            }

            readonly struct NativeGfxResourceRegion : IComparable<NativeGfxResourceRegion>
            {
                public readonly long Index;
                public readonly ulong Size;
                public readonly ushort Type;

                public NativeGfxResourceRegion(long index, ulong size, ushort type)
                {
                    Index = index;
                    Size = size;
                    Type = type;
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public int CompareTo(NativeGfxResourceRegion other)
                {
                    // MemoryType.Device before MemoryType.Private
                    if (Type != other.Type)
                        return -Type.CompareTo(other.Type);

                    // Larger items first
                    return -Size.CompareTo(other.Size);
                }
            }

            void TermiateUndercutRegions(int fromStackLevel, int toStackLevel, long[] hierarchyStack, long currentCombinedDataIndex)
            {
                // Terminate all under-cut regions
                for (int j = fromStackLevel; j < toStackLevel; j++)
                {
                    var dataIndex = hierarchyStack[j];
                    m_CombinedData[dataIndex] = new AddressPoint(m_CombinedData[dataIndex], currentCombinedDataIndex - dataIndex - 1);
                }
            }

            // Adds a fixed number of points from a container of the specific type
            static long AddPoints(long index, DynamicArray<AddressPoint> data, SourceIndex.SourceId sourceName, long count, Func<long, (bool valid, ulong address, ulong size)> accessor)
            {
                for (long i = 0; i < count; i++)
                {
                    var (valid, address, size) = accessor(i);

                    if (!valid || (size == 0))
                        continue;

                    var id = index;
                    data[index++] = new AddressPoint(
                        address,
                        id,
                        new SourceIndex(sourceName, i),
                        AddressPointType.StartPoint
                    );

                    data[index++] = new AddressPoint(
                        address + size,
                        id,
                        new SourceIndex(sourceName, i),
                        AddressPointType.EndPoint
                    );
                }

                return index;
            }

            public void DebugPrintToFile()
            {
                using (var file = new StreamWriter("hierarchy.txt"))
                {
                    DebugPrintToFile(file, 0, m_ItemsCount, "");
                }

                using (var file = new StreamWriter("labels.txt"))
                {
                    for (long i = 0; i < m_Snapshot.NativeMemoryLabels.Count; i++)
                    {
                        var name = m_Snapshot.NativeMemoryLabels.MemoryLabelName[i];
                        var size = m_Snapshot.NativeMemoryLabels.MemoryLabelSizes[i];
                        file.WriteLine($"[{i:D8}] - {EditorUtility.FormatBytes((long)size),8} - {name}");
                    }
                }
                using (var file = new StreamWriter("rootreferences.txt"))
                {
                    for (long i = 0; i < m_Snapshot.NativeRootReferences.Count; i++)
                    {
                        var areaName = m_Snapshot.NativeRootReferences.AreaName[i];
                        var objectName = m_Snapshot.NativeRootReferences.ObjectName[i];
                        var size = m_Snapshot.NativeRootReferences.AccumulatedSize[i];
                        var id = m_Snapshot.NativeRootReferences.Id[i];
                        file.WriteLine($"[{i:D8}] - {EditorUtility.FormatBytes((long)size),8} - {id:D8} - {areaName}:{objectName}");
                    }
                }
            }

            void DebugPrintToFile(StreamWriter file, long start, long count, string prefix)
            {
                for (long i = 0; i < count; i++)
                {
                    var cur = m_CombinedData[start + i];

                    var size = 0UL;
                    if (start + i + 1 < m_CombinedData.Count)
                        size = m_CombinedData[start + i + 1].Address - cur.Address;

                    file.WriteLine($"{prefix,-5} [{start + i:D8}] - {cur.Address:X16} - {EditorUtility.FormatBytes((long)size),8} - {cur.ChildrenCount:D8} - {cur.Source.Id,20} - {GetName(start + i)}");

                    if (cur.ChildrenCount > 0)
                    {
                        DebugPrintToFile(file, start + i + 1, cur.ChildrenCount, prefix + "-");
                        i += cur.ChildrenCount;
                    }
                }
            }

            public void Dispose()
            {
                m_CombinedData.Dispose();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Containers.Unsafe;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using Unity.Profiling;
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

        static void RecurseCrawlFields(ref List<int> fieldsBuffer, int ITypeArrayIndex, TypeDescriptionEntriesCache typeDescriptions, FieldDescriptionEntriesCache fieldDescriptions, FieldFindOptions fieldFindOptions, bool crawlBase)
        {
            bool isValueType = typeDescriptions.HasFlag(ITypeArrayIndex, TypeFlags.kValueType);
            if (crawlBase)
            {
                int baseTypeIndex = typeDescriptions.BaseOrElementTypeIndex[ITypeArrayIndex];
                if (crawlBase && baseTypeIndex != -1 && !isValueType)
                {
                    int baseArrayIndex = typeDescriptions.TypeIndex2ArrayIndex(baseTypeIndex);
                    RecurseCrawlFields(ref fieldsBuffer, baseArrayIndex, typeDescriptions, fieldDescriptions, fieldFindOptions, true);
                }
            }


            int iTypeIndex = typeDescriptions.TypeIndex[ITypeArrayIndex];
            var fieldIndices = typeDescriptions.FieldIndices[ITypeArrayIndex];
            for (int i = 0; i < fieldIndices.Length; ++i)
            {
                var iField = fieldIndices[i];

                if (!FieldMatchesOptions(iField, fieldDescriptions, fieldFindOptions))
                    continue;

                if (fieldDescriptions.TypeIndex[iField] == iTypeIndex && isValueType)
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
            get { return m_SnapshotVersion >= FormatVersion.SystemMemoryRegionsVersion; }
        }

        public ManagedData CrawledData { internal set; get; }

        public class NativeAllocationSiteEntriesCache : IDisposable
        {
            public long Count;
            public DynamicArray<long> id = default;
            public DynamicArray<int> memoryLabelIndex = default;
            public ulong[][] callstackSymbols;

            unsafe public NativeAllocationSiteEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeAllocationSites_Id);
                callstackSymbols = new ulong[Count][];

                if (Count == 0)
                    return;

                id = reader.Read(EntryType.NativeAllocationSites_Id, 0, Count, Allocator.Persistent).Result.Reinterpret<long>();
                memoryLabelIndex = reader.Read(EntryType.NativeAllocationSites_MemoryLabelIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();

                using (var tmpBuffer = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeAllocationSites_CallstackSymbols, 0, Count);
                    tmpBuffer.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeAllocationSites_CallstackSymbols, tmpBuffer, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmpBuffer, ref callstackSymbols);
                }
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

                ulong[] callstackSymbols = this.callstackSymbols[idx];

                for (int i = 0; i < callstackSymbols.Length; ++i)
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
                callstackSymbols = null;
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

            const string k_DynamicHeapAllocatorName = "ALLOC_DEFAULT_MAIN";

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

                for (int i = 0; i < Count; i++)
                {
                    if (MemoryRegionName[i].StartsWith(k_DynamicHeapAllocatorName) && AddressSize[i] > 0)
                    {
                        UsesDynamicHeapAllocator = true;
                        break;
                    }
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
            public int ScriptableObjectIdx { get; private set; } = -1;

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
                ScriptableObjectIdx = Array.FindIndex(TypeName, x => x == k_ScriptableObject);
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

            //scondary data
            public DynamicArray<int> refcount = default;
            public Dictionary<ulong, int> nativeObjectAddressToInstanceId { private set; get; }
            public Dictionary<long, int> rootReferenceIdToIndex { private set; get; }
            public SortedDictionary<int, int> instanceId2Index;

            public readonly ulong TotalSizes = 0ul;
            DynamicArray<int> MetaDataBufferIndicies = default;
            byte[][] MetaDataBuffers;

            unsafe public NativeObjectEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeObjects_InstanceId);
                nativeObjectAddressToInstanceId = new Dictionary<ulong, int>((int)Count);
                rootReferenceIdToIndex = new Dictionary<long, int>((int)Count);
                instanceId2Index = new SortedDictionary<int, int>();
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
                refcount = new DynamicArray<int>(Count, Allocator.Persistent, true);

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
                    nativeObjectAddressToInstanceId.Add(NativeObjectAddress[i], id);
                    rootReferenceIdToIndex.Add(RootReferenceId[i], (int)i);
                    instanceId2Index[id] = (int)i;
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
                    //resize the data array
                    MetaDataBuffers = new byte[sum][];
                    using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.Temp))
                    {
                        var tmpSize = reader.GetSizeForEntryRange(EntryType.ObjectMetaData_MetaDataBuffer, 0, sum);
                        tmp.Resize(tmpSize, false);
                        reader.Read(EntryType.ObjectMetaData_MetaDataBuffer, tmp, 0, sum);
                        ConvertDynamicArrayByteBufferToManagedArray(tmp, ref MetaDataBuffers);
                    }
                }
            }

            public Byte[] MetaData(int nativeObjectIndex)
            {
                if (MetaDataBufferIndicies.Count == 0) return null;
                var bufferIndex = MetaDataBufferIndicies[nativeObjectIndex];
                if (bufferIndex == -1) return null;

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
                refcount.Dispose();
                ObjectName = null;
                nativeObjectAddressToInstanceId = null;
                instanceId2Index = null;
                MetaDataBufferIndicies.Dispose();
                MetaDataBuffers = null;
            }
        }

        public class SystemMemoryRegionEntriesCache : IDisposable
        {
            public enum MemoryType : ushort
            {
                // NB!: The same as in SystemInfo.h
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

            public SystemMemoryRegionEntriesCache(ref IFileReader reader, NativeMemoryRegionEntriesCache nativeMemoryRegions, ManagedMemorySectionEntriesCache managedMemoryRegions)
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

        public enum MemorySectionType : byte
        {
            GarbageCollector,
            VirtualMachine
        }

        //TODO: Add on demand load of sections, and unused chunks unload
        public class ManagedMemorySectionEntriesCache : IDisposable
        {
            ProfilerMarker CacheFind = new ProfilerMarker("ManagedMemorySectionEntriesCache.Find");
            public long Count;
            public DynamicArray<ulong> StartAddress = default;
            public DynamicArray<ulong> SectionSize = default;
            public DynamicArray<MemorySectionType> SectionType = default;
            public string[] SectionName = default;
            public byte[][] Bytes;
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
                Bytes = new byte[Count][];
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

                //use Persistent instead of TempJob so we don't bust the allocator to bits
                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.Persistent))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.ManagedHeapSections_Bytes, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(readStackMemory ? EntryType.ManagedStacks_Bytes : EntryType.ManagedHeapSections_Bytes, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref Bytes);
                }
                SectionSize = new DynamicArray<ulong>(Count, Allocator.Persistent);
                SortSectionEntries(ref StartAddress, ref SectionSize, ref SectionType, ref SectionName, ref Bytes, readStackMemory);
                m_MinAddress = StartAddress[0];
                m_MaxAddress = StartAddress[Count - 1] + (ulong)Bytes[Count - 1].LongLength;

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
                using (CacheFind.Auto())
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

                        if (address >= StartAddress[idx] && address < (StartAddress[idx] + (ulong)Bytes[idx].Length))
                        {
                            bytesAndOffset.bytes = Bytes[idx];
                            bytesAndOffset.offset = (int)(address - StartAddress[idx]);
                            bytesAndOffset.pointerSize = virtualMachineInformation.PointerSize;
                        }
                    }

                    return bytesAndOffset;
                }
            }

            static void SortSectionEntries(ref DynamicArray<ulong> startAddresses, ref DynamicArray<ulong> sizes, ref DynamicArray<MemorySectionType> associatedSectionType, ref string[] associatedSectionNames,
                ref byte[][] associatedByteArrays, bool isStackMemory)
            {
                var sortMapping = new int[startAddresses.Count];

                for (int i = 0; i < sortMapping.Length; ++i)
                    sortMapping[i] = i;

                var startAddr = startAddresses;
                Array.Sort(sortMapping, (x, y) => startAddr[x].CompareTo(startAddr[y]));

                var newSortedAddresses = new ulong[startAddresses.Count];
                var newSortedByteArrays = new byte[startAddresses.Count][];
                var newSortedSectionTypes = isStackMemory ? null : new MemorySectionType[startAddresses.Count];
                var newSortedSectionNames = new string[startAddresses.Count];

                for (long i = 0; i < startAddresses.Count; ++i)
                {
                    long idx = sortMapping[i];
                    newSortedAddresses[i] = startAddresses[idx];
                    newSortedByteArrays[i] = associatedByteArrays[idx];
                    newSortedSectionNames[i] = associatedSectionNames[idx];

                    if (!isStackMemory)
                        newSortedSectionTypes[i] = associatedSectionType[idx];
                }

                for (long i = 0; i < startAddresses.Count; ++i)
                {
                    startAddresses[i] = newSortedAddresses[i];
                    sizes[i] = (ulong)newSortedByteArrays[i].LongLength;
                    if (!isStackMemory)
                        associatedSectionType[i] = newSortedSectionTypes[i];
                }
                associatedByteArrays = newSortedByteArrays;
                associatedSectionNames = newSortedSectionNames;
            }

            public void Dispose()
            {
                Count = 0;
                m_MinAddress = m_MaxAddress = 0;
                StartAddress.Dispose();
                SectionType.Dispose();
                SectionSize.Dispose();
                Bytes = null;
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
            const string k_SystemDoubleName = "System.Double";
            const string k_SystemSingleName = "System.Single";
            const string k_SystemStringName = "System.String";
            const string k_SystemIntPtrName = "System.IntPtr";
            const string k_SystemByteName = "System.Byte";

            public long Count;
            public DynamicArray<TypeFlags> Flags = default;
            public DynamicArray<int> BaseOrElementTypeIndex = default;
            public DynamicArray<int> Size = default;
            public DynamicArray<ulong> TypeInfoAddress = default;
            public DynamicArray<int> TypeIndex = default;

            public string[] TypeDescriptionName;
            public string[] Assembly;
#if !UNITY_2021_2_OR_NEWER // TODO: || QUICK_SEARCH_AVAILABLE
            public string[] UniqueCurrentlyAvailableUnityAssemblyNames;
#endif
            public int[][] FieldIndices;
            public byte[][] StaticFieldBytes;

            //secondary data, handled inside InitSecondaryItems
            public int[][] FieldIndicesInstance;//includes all bases' instance fields
            public int[][] fieldIndicesStatic;  //includes all bases' static fields
            public int[][] fieldIndicesOwnedStatic;  //includes only type's static fields
            public bool[] HasStaticFields;

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
            public int ITypeDouble { get; private set; }
            public int ITypeSingle { get; private set; }
            public int ITypeString { get; private set; }
            public int ITypeIntPtr { get; private set; }
            public int ITypeByte { get; private set; }

            public int ITypeUnityMonoBehaviour { get; private set; }
            public int ITypeUnityScriptableObject { get; private set; }
            public int ITypeUnityComponent { get; private set; }
            public Dictionary<ulong, int> TypeInfoToArrayIndex { get; private set; }
            public Dictionary<int, int> TypeIndexToArrayIndex { get; private set; }
            // only fully initialized after the Managed Crawler is done stitching up Objects. Might be better to be moved over to ManagedData
            public Dictionary<int, int> UnityObjectTypeIndexToNativeTypeIndex { get; private set; }
            public HashSet<int> PureCSharpTypeIndices { get; private set; }

            public TypeDescriptionEntriesCache(ref IFileReader reader, FieldDescriptionEntriesCache fieldDescriptions)
            {
                Count = reader.GetEntryCount(EntryType.TypeDescriptions_TypeIndex);
                TypeDescriptionName = new string[Count];
                Assembly = new string[Count];
                FieldIndices = new int[Count][];
                StaticFieldBytes = new byte[Count][];

                if (Count == 0)
                    return;

                Flags = reader.Read(EntryType.TypeDescriptions_Flags, 0, Count, Allocator.Persistent).Result.Reinterpret<TypeFlags>();
                BaseOrElementTypeIndex = reader.Read(EntryType.TypeDescriptions_BaseOrElementTypeIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                Size = reader.Read(EntryType.TypeDescriptions_Size, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                TypeInfoAddress = reader.Read(EntryType.TypeDescriptions_TypeInfoAddress, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                TypeIndex = reader.Read(EntryType.TypeDescriptions_TypeIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();

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

                    tmpSize = reader.GetSizeForEntryRange(EntryType.TypeDescriptions_FieldIndices, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.TypeDescriptions_FieldIndices, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref FieldIndices);

                    tmpSize = reader.GetSizeForEntryRange(EntryType.TypeDescriptions_StaticFieldBytes, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.TypeDescriptions_StaticFieldBytes, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref StaticFieldBytes);
                }

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
            public bool HasStaticField(int iType)
            {
                return HasStaticFields[iType];
            }

            public bool HasFlag(int arrayIndex, TypeFlags flag)
            {
                return (Flags[arrayIndex] & flag) == flag;
            }

            public int GetRank(int arrayIndex)
            {
                int r = (int)(Flags[arrayIndex] & TypeFlags.kArrayRankMask) >> 16;
                return r;
            }

            public int TypeIndex2ArrayIndex(int typeIndex)
            {
                int i;
                if (!TypeIndexToArrayIndex.TryGetValue(typeIndex, out i))
                {
                    throw new Exception("typeIndex not found");
                }
                return i;
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

            static ProfilerMarker typeFieldArraysBuild = new ProfilerMarker("MemoryProfiler.TypeFields.TypeFieldArrayBuilding");
            void InitSecondaryItems(TypeDescriptionEntriesCache typeDescriptionEntries, FieldDescriptionEntriesCache fieldDescriptions)
            {
                TypeInfoToArrayIndex = Enumerable.Range(0, (int)TypeInfoAddress.Count).ToDictionary(x => TypeInfoAddress[x], x => x);
                TypeIndexToArrayIndex = Enumerable.Range(0, (int)TypeIndex.Count).ToDictionary(x => TypeIndex[x], x => x);
                UnityObjectTypeIndexToNativeTypeIndex = new Dictionary<int, int>();
                PureCSharpTypeIndices = new HashSet<int>();


                ITypeUnityObject = TypeIndex[Array.FindIndex(TypeDescriptionName, x => x == UnityObjectTypeName)];
#if DEBUG_VALIDATION //This shouldn't really happen
                if (ITypeUnityObject < 0)
                {
                    throw new Exception("Unable to find UnityEngine.Object");
                }
#endif

                using (typeFieldArraysBuild.Auto())
                {
                    HasStaticFields = new bool[Count];
                    FieldIndicesInstance = new int[Count][];
                    fieldIndicesStatic = new int[Count][];
                    fieldIndicesOwnedStatic = new int[Count][];
                    List<int> fieldProcessingBuffer = new List<int>(k_DefaultFieldProcessingBufferSize);

                    for (int i = 0; i < Count; ++i)
                    {
                        HasStaticFields[i] = false;
                        foreach (var iField in FieldIndices[i])
                        {
                            if (fieldDescriptions.IsStatic[iField] == 1)
                            {
                                HasStaticFields[i] = true;
                                break;
                            }
                        }

                        TypeTools.AllFieldArrayIndexOf(ref fieldProcessingBuffer, i, typeDescriptionEntries, fieldDescriptions, TypeTools.FieldFindOptions.OnlyInstance, true);
                        FieldIndicesInstance[i] = fieldProcessingBuffer.ToArray();

                        TypeTools.AllFieldArrayIndexOf(ref fieldProcessingBuffer, i, typeDescriptionEntries, fieldDescriptions, TypeTools.FieldFindOptions.OnlyStatic, true);
                        fieldIndicesStatic[i] = fieldProcessingBuffer.ToArray();

                        TypeTools.AllFieldArrayIndexOf(ref fieldProcessingBuffer, i, typeDescriptionEntries, fieldDescriptions, TypeTools.FieldFindOptions.OnlyStatic, false);
                        fieldIndicesOwnedStatic[i] = fieldProcessingBuffer.ToArray();

                        var typeIndex = typeDescriptionEntries.TypeIndex[i];
                        if (DerivesFromUnityObject(typeIndex))
                            UnityObjectTypeIndexToNativeTypeIndex.Add(typeIndex, -1);
                        else
                            PureCSharpTypeIndices.Add(typeIndex);
                    }
                }

                var fieldIndicesIndex = Array.FindIndex(
                    typeDescriptionEntries.FieldIndices[TypeIndexToArrayIndex[ITypeUnityObject]]
                    , iField => fieldDescriptions.FieldDescriptionName[iField] == UnityNativeObjectPointerFieldName);

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
                ITypeValueType = TypeIndex[Array.FindIndex(TypeDescriptionName, x => x == k_SystemValueTypeName)];
                ITypeObject = TypeIndex[Array.FindIndex(TypeDescriptionName, x => x == k_SystemObjectTypeName)];
                ITypeEnum = TypeIndex[Array.FindIndex(TypeDescriptionName, x => x == k_SystemEnumTypeName)];
                ITypeChar = TypeIndex[Array.FindIndex(TypeDescriptionName, x => x == k_SystemCharTypeName)];
                ITypeInt16 = TypeIndex[Array.FindIndex(TypeDescriptionName, x => x == k_SystemInt16Name)];
                ITypeInt32 = TypeIndex[Array.FindIndex(TypeDescriptionName, x => x == k_SystemInt32Name)];
                ITypeInt64 = TypeIndex[Array.FindIndex(TypeDescriptionName, x => x == k_SystemInt64Name)];
                ITypeIntPtr = TypeIndex[Array.FindIndex(TypeDescriptionName, x => x == k_SystemIntPtrName)];
                ITypeString = TypeIndex[Array.FindIndex(TypeDescriptionName, x => x == k_SystemStringName)];
                ITypeBool = TypeIndex[Array.FindIndex(TypeDescriptionName, x => x == k_SystemBoolName)];
                ITypeSingle = TypeIndex[Array.FindIndex(TypeDescriptionName, x => x == k_SystemSingleName)];
                ITypeByte = TypeIndex[Array.FindIndex(TypeDescriptionName, x => x == k_SystemByteName)];
                ITypeDouble = TypeIndex[Array.FindIndex(TypeDescriptionName, x => x == k_SystemDoubleName)];
                ITypeUInt16 = TypeIndex[Array.FindIndex(TypeDescriptionName, x => x == k_SystemUInt16Name)];
                ITypeUInt32 = TypeIndex[Array.FindIndex(TypeDescriptionName, x => x == k_SystemUInt32Name)];
                ITypeUInt64 = TypeIndex[Array.FindIndex(TypeDescriptionName, x => x == k_SystemUInt64Name)];

                ITypeUnityMonoBehaviour = TypeIndex[Array.FindIndex(TypeDescriptionName, x => x == k_UnityMonoBehaviourTypeName)];
                ITypeUnityScriptableObject = TypeIndex[Array.FindIndex(TypeDescriptionName, x => x == k_UnityScriptableObjectTypeName)];
                ITypeUnityComponent = TypeIndex[Array.FindIndex(TypeDescriptionName, x => x == k_UnityComponentObjectTypeName)];

#if !UNITY_2021_2_OR_NEWER // TODO: || QUICK_SEARCH_AVAILABLE
                var uniqueCurrentlyAvailableUnityAssemblyNames = new List<string>();
                var assemblyHashSet = new HashSet<string>();
                foreach (var assembly in Assembly)
                {
                    if (assemblyHashSet.Contains(assembly))
                        continue;
                    assemblyHashSet.Add(assembly);
                    if (assembly.StartsWith("Unity"))
                    {
                        try
                        {
                            System.Reflection.Assembly.Load(assembly);
                        }
                        catch (Exception)
                        {
                            // only add assemblies currently available
                            continue;
                        }
                        uniqueCurrentlyAvailableUnityAssemblyNames.Add(assembly);
                    }
                }
                UniqueCurrentlyAvailableUnityAssemblyNames = uniqueCurrentlyAvailableUnityAssemblyNames.ToArray();
#endif
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
                TypeIndex.Dispose();
                TypeDescriptionName = null;
                Assembly = null;
                FieldIndices = null;
                StaticFieldBytes = null;

                FieldIndicesInstance = null;
                fieldIndicesStatic = null;
                fieldIndicesOwnedStatic = null;
                HasStaticFields = null;
                ITypeValueType = ITypeInvalid;
                ITypeObject = ITypeInvalid;
                ITypeEnum = ITypeInvalid;
                TypeInfoToArrayIndex = null;
                TypeIndexToArrayIndex = null;
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
            public Dictionary<int, List<int>> ReferencedBy { get; private set; } = new Dictionary<int, List<int>>();

            // List of objects an object with the specific key is refereing to
            public Dictionary<int, List<int>> ReferenceTo { get; private set; } = new Dictionary<int, List<int>>();

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
                    if (ReferencedBy.TryGetValue(To[i], out var fromList))
                        fromList.Add(From[i]);
                    else
                        ReferencedBy[To[i]] = new List<int> { From[i] };

                    if (ReferenceTo.TryGetValue(From[i], out var toList))
                        toList.Add(To[i]);
                    else
                        ReferenceTo[From[i]] = new List<int> { To[i] };
                }
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
        public CaptureFlags CaptureFlags = 0;

        public SortedNativeMemoryRegionEntriesCache SortedNativeRegionsEntries;
        public SortedManagedMemorySectionEntriesCache SortedManagedStacksEntries;
        public SortedManagedMemorySectionEntriesCache SortedManagedHeapEntries;
        public SortedManagedObjectsCache SortedManagedObjects;
        public SortedNativeAllocationsCache SortedNativeAllocations;
        public SortedNativeObjectsCache SortedNativeObjects;

        public SceneRootEntriesCache SceneRoots;
        public NativeAllocatorEntriesCache NativeAllocators;
        public NativeGfxResourcReferenceEntriesCache NativeGfxResourceReferences;

        public SystemMemoryRegionEntriesCache SystemMemoryRegions;
        public MemoryEntriesHierarchyCache MemoryEntriesHierarchy;

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

                SystemMemoryRegions = new SystemMemoryRegionEntriesCache(ref reader, NativeMemoryRegions, ManagedHeapSections);

                if (GcHandles.Count > 0)
                    CaptureFlags |= CaptureFlags.ManagedObjects;
                if (NativeAllocations.Count > 0)
                    CaptureFlags |= CaptureFlags.NativeAllocations;
                if (NativeAllocationSites.Count > 0)
                    CaptureFlags |= CaptureFlags.NativeAllocationSites;
                if (NativeObjects.Count > 0)
                    CaptureFlags |= CaptureFlags.NativeObjects;
                if (NativeCallstackSymbols.Count > 0)
                    CaptureFlags |= CaptureFlags.NativeStackTraces;

                SortedManagedStacksEntries = new SortedManagedMemorySectionEntriesCache(ManagedStacks);
                SortedManagedHeapEntries = new SortedManagedMemorySectionEntriesCache(ManagedHeapSections);
                SortedManagedObjects = new SortedManagedObjectsCache(this);

                SortedNativeRegionsEntries = new SortedNativeMemoryRegionEntriesCache(this);
                SortedNativeAllocations = new SortedNativeAllocationsCache(this);
                SortedNativeObjects = new SortedNativeObjectsCache(this);

                if (HasSystemMemoryRegionsInfo)
                    MemoryEntriesHierarchy = new MemoryEntriesHierarchyCache(this);

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

                SystemMemoryRegions.Dispose();
            }
        }

        public interface ISortedEntriesCache
        {
            void Preload();
            int Count { get; }
            ulong Address(int index);
            ulong Size(int index);
        }

        public class SortedNativeMemoryRegionEntriesCache : ISortedEntriesCache
        {
            CachedSnapshot m_Snapshot;
            int[] m_Sorting;

            public SortedNativeMemoryRegionEntriesCache(CachedSnapshot snapshot)
            {
                m_Snapshot = snapshot;
            }

            public void Preload()
            {
                if (m_Sorting == null)
                {
                    m_Sorting = new int[m_Snapshot.NativeMemoryRegions.Count];

                    for (int i = 0; i < m_Sorting.Length; ++i)
                        m_Sorting[i] = i;

                    Array.Sort(m_Sorting, (x, y) => m_Snapshot.NativeMemoryRegions.AddressBase[x].CompareTo(m_Snapshot.NativeMemoryRegions.AddressBase[y]));
                }
            }

            int this[int index]
            {
                get
                {
                    Preload();
                    return m_Sorting[index];
                }
            }

            public int Count { get { return (int)m_Snapshot.NativeMemoryRegions.Count; } }
            public ulong Address(int index) { return m_Snapshot.NativeMemoryRegions.AddressBase[this[index]]; }
            public ulong Size(int index) { return m_Snapshot.NativeMemoryRegions.AddressSize[this[index]]; }

            public string Name(int index) { return m_Snapshot.NativeMemoryRegions.MemoryRegionName[this[index]]; }
            public int UnsortedParentRegionIndex(int index) { return m_Snapshot.NativeMemoryRegions.ParentIndex[this[index]]; }
            public int UnsortedFirstAllocationIndex(int index) { return m_Snapshot.NativeMemoryRegions.FirstAllocationIndex[this[index]]; }
            public int UnsortedNumAllocations(int index) { return m_Snapshot.NativeMemoryRegions.NumAllocations[this[index]]; }
        }

        //TODO: unify with the other old section entries as those are sorted by default now
        public class SortedManagedMemorySectionEntriesCache : ISortedEntriesCache
        {
            ManagedMemorySectionEntriesCache m_Entries;

            public SortedManagedMemorySectionEntriesCache(ManagedMemorySectionEntriesCache entries)
            {
                m_Entries = entries;
            }

            public void Preload()
            {
                //Dummy for the interface
            }

            public int Count { get { return (int)m_Entries.Count; } }
            public ulong Address(int index) { return m_Entries.StartAddress[index]; }
            public ulong Size(int index) { return (ulong)m_Entries.Bytes[index].Length; }
            public byte[] Bytes(int index) { return m_Entries.Bytes[index]; }
            public MemorySectionType SectionType(int index) { return m_Entries.SectionType[index]; }
        }

        public class SortedManagedObjectsCache : ISortedEntriesCache
        {
            CachedSnapshot m_Snapshot;
            int[] m_Sorting;

            public SortedManagedObjectsCache(CachedSnapshot snapshot)
            {
                m_Snapshot = snapshot;
            }

            public void Preload()
            {
                if (m_Sorting == null)
                {
                    m_Sorting = new int[m_Snapshot.CrawledData.ManagedObjects.Count];

                    for (int i = 0; i < m_Sorting.Length; ++i)
                        m_Sorting[i] = i;

                    Array.Sort(m_Sorting, (x, y) => m_Snapshot.CrawledData.ManagedObjects[x].PtrObject.CompareTo(m_Snapshot.CrawledData.ManagedObjects[y].PtrObject));
                }
            }

            ManagedObjectInfo this[int index]
            {
                get
                {
                    Preload();
                    return m_Snapshot.CrawledData.ManagedObjects[m_Sorting[index]];
                }
            }

            public int Count { get { return (int)m_Snapshot.CrawledData.ManagedObjects.Count; } }

            public ulong Address(int index) { return this[index].PtrObject; }
            public ulong Size(int index) { return (ulong)this[index].Size; }
        }

        public class SortedNativeAllocationsCache : ISortedEntriesCache
        {
            CachedSnapshot m_Snapshot;
            int[] m_Sorting;

            public SortedNativeAllocationsCache(CachedSnapshot snapshot)
            {
                m_Snapshot = snapshot;
            }

            public void Preload()
            {
                if (m_Sorting == null)
                {
                    m_Sorting = new int[m_Snapshot.NativeAllocations.Address.Count];

                    for (int i = 0; i < m_Sorting.Length; ++i)
                        m_Sorting[i] = i;

                    Array.Sort(m_Sorting, (x, y) => m_Snapshot.NativeAllocations.Address[x].CompareTo(m_Snapshot.NativeAllocations.Address[y]));
                }
            }

            int this[int index]
            {
                get
                {
                    Preload();
                    return m_Sorting[index];
                }
            }

            public int Count { get { return (int)m_Snapshot.NativeAllocations.Count; } }
            public ulong Address(int index) { return m_Snapshot.NativeAllocations.Address[this[index]]; }
            public ulong Size(int index) { return m_Snapshot.NativeAllocations.Size[this[index]]; }
            public int MemoryRegionIndex(int index) { return m_Snapshot.NativeAllocations.MemoryRegionIndex[this[index]]; }
            public long RootReferenceId(int index) { return m_Snapshot.NativeAllocations.RootReferenceId[this[index]]; }
            public long AllocationSiteId(int index) { return m_Snapshot.NativeAllocations.AllocationSiteId[this[index]]; }
            public int OverheadSize(int index) { return m_Snapshot.NativeAllocations.OverheadSize[this[index]]; }
            public int PaddingSize(int index) { return m_Snapshot.NativeAllocations.PaddingSize[this[index]]; }
        }

        public class SortedNativeObjectsCache : ISortedEntriesCache
        {
            CachedSnapshot m_Snapshot;
            int[] m_Sorting;

            public SortedNativeObjectsCache(CachedSnapshot snapshot)
            {
                m_Snapshot = snapshot;
            }

            public void Preload()
            {
                if (m_Sorting == null)
                {
                    m_Sorting = new int[m_Snapshot.NativeObjects.NativeObjectAddress.Count];

                    for (int i = 0; i < m_Sorting.Length; ++i)
                        m_Sorting[i] = i;

                    Array.Sort(m_Sorting, (x, y) => m_Snapshot.NativeObjects.NativeObjectAddress[x].CompareTo(m_Snapshot.NativeObjects.NativeObjectAddress[y]));
                }
            }

            int this[int index]
            {
                get
                {
                    Preload();
                    return m_Sorting[index];
                }
            }

            public int Count { get { return (int)m_Snapshot.NativeObjects.Count; } }
            public ulong Address(int index) { return m_Snapshot.NativeObjects.NativeObjectAddress[this[index]]; }
            public ulong Size(int index) { return m_Snapshot.NativeObjects.Size[this[index]]; }
            public string Name(int index) { return m_Snapshot.NativeObjects.ObjectName[this[index]]; }
            public int InstanceId(int index) { return m_Snapshot.NativeObjects.InstanceId[this[index]]; }
            public int NativeTypeArrayIndex(int index) { return m_Snapshot.NativeObjects.NativeTypeArrayIndex[this[index]]; }
            public HideFlags HideFlags(int index) { return m_Snapshot.NativeObjects.HideFlags[this[index]]; }
            public ObjectFlags Flags(int index) { return m_Snapshot.NativeObjects.Flags[this[index]]; }
            public long RootReferenceId(int index) { return m_Snapshot.NativeObjects.RootReferenceId[this[index]]; }
            public int Refcount(int index) { return m_Snapshot.NativeObjects.refcount[this[index]]; }
            public int ManagedObjectIndex(int index) { return m_Snapshot.NativeObjects.ManagedObjectIndex[this[index]]; }
        }


        unsafe static void ConvertDynamicArrayByteBufferToManagedArray<T>(DynamicArray<byte> nativeEntryBuffer, ref T[] elements) where T : class
        {
            byte* binaryDataStream = (byte*)nativeEntryBuffer.GetUnsafePtr();
            //jump over the offsets array
            long* binaryEntriesLength = (long*)binaryDataStream;
            binaryDataStream = binaryDataStream + sizeof(long) * (elements.Length + 1); //+1 due to the final element offset being at the end

            for (int i = 0; i < elements.Length; ++i)
            {
                byte* srcPtr = binaryDataStream + binaryEntriesLength[i];
                int actualLength = (int)(binaryEntriesLength[i + 1] - binaryEntriesLength[i]);

                if (typeof(T) == typeof(string))
                {
                    elements[i] = new string(unchecked((sbyte*)srcPtr), 0, actualLength, System.Text.Encoding.UTF8) as T;
                }
                else
                {
                    object arr = null;
                    if (typeof(T) == typeof(byte[]))
                    {
                        arr = new byte[actualLength];
                    }
                    else if (typeof(T) == typeof(int[]))
                    {
                        arr = new int[actualLength / UnsafeUtility.SizeOf<int>()];
                    }
                    else if (typeof(T) == typeof(ulong[]))
                    {
                        arr = new ulong[actualLength / UnsafeUtility.SizeOf<ulong>()];
                    }
                    else if (typeof(T) == typeof(long[]))
                    {
                        arr = new long[actualLength / UnsafeUtility.SizeOf<long>()];
                    }
                    else
                    {
                        Debug.LogErrorFormat("Unsuported type provided for conversion, type name: {0}", typeof(T).FullName);
                        return;
                    }

                    ulong handle = 0;
                    void* dstPtr = UnsafeUtility.PinGCArrayAndGetDataAddress(arr as Array, out handle);
                    UnsafeUtility.MemCpy(dstPtr, srcPtr, actualLength);
                    UnsafeUtility.ReleaseGCObject(handle);
                    elements[i] = arr as T;
                }
            }
        }

        readonly public struct SourceLink
        {
            public enum SourceId : byte
            {
                SystemMemoryRegion,
                NativeMemoryRegion,
                NativeAllocation,
                ManagedHeapSection,
            }

            [Flags]
            public enum SourceFlags : byte
            {
                None = 0,
                EndPoint = 1 << 1,
                StartPoint = 1 << 2,
            }

            public readonly SourceId Id;
            public readonly SourceFlags Flags;
            public readonly long Index;

            public SourceLink(SourceId source, long index, SourceFlags flags)
            {
                Id = source;
                Index = index;
                Flags = flags;
            }
        }

        public class MemoryEntriesHierarchyCache
        {
            public enum RegionType : byte
            {
                Free,
                Untracked,
                Reserved,
                Ignored,

                Native,
                Managed,
                Device,
                Mapped,
                Shared,
                AndroidRuntime,
            }

            public struct AddressPoint
            {
                public ulong Address;
                public SourceLink Source;
                public RegionType Type;

                // Nb! Through hierarchy build process ChildCount
                // is used as begin/end section ID
                public long ChildrenCount;
            }

            CachedSnapshot m_Snapshot;
            int m_ItemsCount;
            AddressPoint[] m_CombinedData;

            public MemoryEntriesHierarchyCache(CachedSnapshot snapshot)
            {
                m_Snapshot = snapshot;
                m_ItemsCount = 0;
                m_CombinedData = null;
            }

            internal MemoryEntriesHierarchyCache(AddressPoint[] m_Data)
            {
                m_Snapshot = null;
                m_ItemsCount = m_Data.Length;
                m_CombinedData = m_Data;

                SortPoints();
                Postprocess();
            }

            public int Count
            {
                get
                {
                    Build();
                    return m_ItemsCount;
                }
            }

            public AddressPoint[] Data
            {
                get
                {
                    Build();
                    return m_CombinedData;
                }
            }

            public SourceLink GetDataSource(int index)
            {
                Build();
                return m_CombinedData[index].Source;
            }

            public bool HasChildren(long index)
            {
                Build();
                return m_CombinedData[index].ChildrenCount > 0;
            }

            public IEnumerable<long> GetRoots()
            {
                Build();

                for (long i = 0; i < m_ItemsCount; i++)
                {
                    var cur = m_CombinedData[i];
                    if (cur.Type != RegionType.Free)
                        yield return i;

                    i += cur.ChildrenCount;
                }
            }

            public IEnumerable<long> GetChildren(long index)
            {
                Build();

                var root = m_CombinedData[index];
                for (long i = 0; i < root.ChildrenCount; i++)
                {
                    var childIndex = index + i + 1;
                    var child = m_CombinedData[childIndex];
                    if (child.Type != RegionType.Free)
                        yield return childIndex;

                    i += child.ChildrenCount;
                }
            }

            /// <summary>
            /// Flat scan all memory regions
            /// </summary>
            /// <param name="action"></param>
            public void ForEachFlat(Action<ulong, ulong, long, RegionType, SourceLink> action)
            {
                Build();

                if (m_ItemsCount == 0)
                    return;

                for (long i = 0; i < m_ItemsCount - 1; i++)
                {
                    var cur = m_CombinedData[i];
                    var next = m_CombinedData[i + 1];

                    // Ignore free memory regions
                    if (cur.Type == RegionType.Free)
                        continue;

                    var size = next.Address - cur.Address;
                    action(cur.Address, size, cur.ChildrenCount, cur.Type, cur.Source);
                }
            }

            public string GetName(long index)
            {
                if (m_Snapshot == null)
                    return String.Empty;

                var item = m_CombinedData[index];
                switch (item.Source.Id)
                {
                    case SourceLink.SourceId.SystemMemoryRegion:
                        return m_Snapshot.SystemMemoryRegions.RegionName[item.Source.Index];
                    case SourceLink.SourceId.NativeMemoryRegion:
                        return m_Snapshot.NativeMemoryRegions.MemoryRegionName[item.Source.Index];
                    case SourceLink.SourceId.NativeAllocation:
                    {
                        if (m_Snapshot.NativeAllocations.RootReferenceId.Count > 0)
                        {
                            var rootReferenceId = m_Snapshot.NativeAllocations.RootReferenceId[item.Source.Index];
                            if (rootReferenceId < m_Snapshot.NativeRootReferences.Count)
                                return m_Snapshot.NativeRootReferences.AreaName[rootReferenceId] + ":" + m_Snapshot.NativeRootReferences.ObjectName[rootReferenceId];
                        }
                        return "No Root Area";
                    }
                    case SourceLink.SourceId.ManagedHeapSection:
                        return m_Snapshot.ManagedHeapSections.SectionName[item.Source.Index];
                }

                return String.Empty;
            }

            void Build()
            {
                if (m_CombinedData != null)
                    return;

                AddPoints();

                SortPoints();
                Postprocess();
            }

            void AddPoints()
            {
                // We're building a sorted by address list of ranges with
                // a link to a source of the data
                var maxPointsCount = (
                    m_Snapshot.SystemMemoryRegions.Count +
                    m_Snapshot.NativeMemoryRegions.Count +
                    m_Snapshot.ManagedHeapSections.Count +
                    m_Snapshot.NativeAllocations.Count) * 2;
                m_CombinedData = new AddressPoint[maxPointsCount];

                // Add OS reported system memory regions
                m_ItemsCount = AddPoints(
                    m_ItemsCount,
                    m_CombinedData,
                    SourceLink.SourceId.SystemMemoryRegion,
                    Convert.ToInt32(m_Snapshot.SystemMemoryRegions.Count),
                    (int i) =>
                    {
                        var address = m_Snapshot.SystemMemoryRegions.RegionAddress[i];
                        var size = m_Snapshot.SystemMemoryRegions.RegionSize[i];
                        var name = m_Snapshot.SystemMemoryRegions.RegionName[i];
                        var type = m_Snapshot.SystemMemoryRegions.RegionType[i];
                        var sourceType = GetSystemRegionType(type, name);
                        return (true, address, size, sourceType);
                    });

                // Add MemoryManager native regions
                m_ItemsCount = AddPoints(
                    m_ItemsCount,
                    m_CombinedData,
                    SourceLink.SourceId.NativeMemoryRegion,
                    Convert.ToInt32(m_Snapshot.NativeMemoryRegions.Count),
                    (int i) =>
                    {
                        var address = m_Snapshot.NativeMemoryRegions.AddressBase[i];
                        var size = m_Snapshot.NativeMemoryRegions.AddressSize[i];
                        var name = m_Snapshot.NativeMemoryRegions.MemoryRegionName[i];
                        bool valid = (size > 0) && !name.Contains("Virtual Memory");
                        return (valid, address, size, RegionType.Native);
                    });

                // Add Mono reported managed sections
                m_ItemsCount = AddPoints(
                    m_ItemsCount,
                    m_CombinedData,
                    SourceLink.SourceId.ManagedHeapSection,
                    Convert.ToInt32(m_Snapshot.ManagedHeapSections.Count),
                    (int i) =>
                    {
                        var address = m_Snapshot.ManagedHeapSections.StartAddress[i];
                        var size = m_Snapshot.ManagedHeapSections.SectionSize[i];
                        bool valid = (size > 0);
                        return (valid, address, size, RegionType.Managed);
                    });

                // Add individual native allocations
                m_ItemsCount = AddPoints(
                    m_ItemsCount,
                    m_CombinedData,
                    SourceLink.SourceId.NativeAllocation,
                    Convert.ToInt32(m_Snapshot.NativeAllocations.Count),
                    (int i) =>
                    {
                        var address = m_Snapshot.NativeAllocations.Address[i];
                        var size = m_Snapshot.NativeAllocations.Size[i];
                        bool valid = (size > 0);
                        return (valid, address, size, RegionType.Native);
                    });
            }

            void SortPoints()
            {
                Array.Sort<AddressPoint>(
                    m_CombinedData,
                    0,
                    (int)m_ItemsCount,
                    Comparer<AddressPoint>.Create((AddressPoint a, AddressPoint b) =>
                    {
                        var ret = a.Address.CompareTo(b.Address);
                        if (ret == 0)
                        {
                            if (a.Source.Flags.HasFlag(SourceLink.SourceFlags.EndPoint) && !b.Source.Flags.HasFlag(SourceLink.SourceFlags.EndPoint))
                                return -1;
                            else if (!a.Source.Flags.HasFlag(SourceLink.SourceFlags.EndPoint) && b.Source.Flags.HasFlag(SourceLink.SourceFlags.EndPoint))
                                return 1;
                            else if (a.Source.Flags.HasFlag(SourceLink.SourceFlags.EndPoint) && b.Source.Flags.HasFlag(SourceLink.SourceFlags.EndPoint))
                                ret = -a.Source.Id.CompareTo(b.Source.Id);
                            else
                                ret = a.Source.Id.CompareTo(b.Source.Id);
                        }
                        return ret;
                    }));
            }

            /// <summary>
            /// Scans all points and updates flags and childs count
            /// based on begin/end flags
            /// </summary>
            void Postprocess()
            {
                const int kMaxStackDepth = 16;
                var hierarchyStack = new long[kMaxStackDepth];
                var hierarchyStackCount = 0;

                for (int i = 0; i < m_ItemsCount; i++)
                {
                    var cur = m_CombinedData[i];

                    if (!cur.Source.Flags.HasFlag(SourceLink.SourceFlags.StartPoint))
                    {
                        if (hierarchyStackCount <= 0)
                        {
                            // Lose end point. This is valid situation as memory snapshot
                            // capture process modifies memory and system, native and managed
                            // states might be slighly out of sync and have overlapping regions
                            m_CombinedData[i].ChildrenCount = 0;
                            m_CombinedData[i].Type = RegionType.Free;
                            continue;
                        }

                        // We use ChildCount to store begin/end pair IDs, so that
                        // we can check that they are from the same pair
                        var startIndex = hierarchyStack[hierarchyStackCount - 1];
                        var startItem = m_CombinedData[startIndex];
                        if (startItem.ChildrenCount != cur.ChildrenCount)
                        {
                            // Non-matching end point. This is valid situation (see Lose end point comment).
                            // Try to find matching starting point
                            var index = Array.FindIndex(hierarchyStack, 0, hierarchyStackCount, (x) => m_CombinedData[x].ChildrenCount == cur.ChildrenCount);
                            if (index < 0)
                            {
                                // No starting point, ignore the point entirely
                                m_CombinedData[i].ChildrenCount = 0;
                                m_CombinedData[i].Type = RegionType.Ignored;
                                continue;
                            }

                            // Terminate all cut regions
                            for (int j = index; j < hierarchyStackCount; j++)
                            {
                                var dataIndex = hierarchyStack[j];
                                m_CombinedData[dataIndex].ChildrenCount = i - dataIndex - 1;
                            }

                            // if there is matching begin -> unwind stack to that point
                            startIndex = hierarchyStack[index];
                            hierarchyStackCount = index + 1;
                        }

                        // Remove from stack
                        hierarchyStackCount--;

                        // Replace ChildCount with actual child count
                        m_CombinedData[startIndex].ChildrenCount = i - startIndex - 1;

                        // Based on hierarchy level "unknown" memory spans are:
                        // - free if it's on system regions level
                        // - untracked if it's inside system regions
                        // - reserved if it's inside native or heap sections
                        m_CombinedData[i].ChildrenCount = 0;
                        switch (hierarchyStackCount)
                        {
                            case 0:
                                m_CombinedData[i].Type = RegionType.Free;
                                break;
                            case 1:
                                m_CombinedData[i].Type = RegionType.Untracked;
                                break;
                            default:
                                m_CombinedData[i].Type = RegionType.Reserved;
                                break;
                        }
                    }
                    else
                    {
                        hierarchyStack[hierarchyStackCount] = i;
                        hierarchyStackCount++;
                    }
                }
            }

            RegionType GetSystemRegionType(ushort type, string name)
            {
                if (m_Snapshot.MetaData.TargetInfo.HasValue)
                {
                    switch (m_Snapshot.MetaData.TargetInfo.Value.RuntimePlatform)
                    {
                        case RuntimePlatform.Android:
                        {
                            if (name.StartsWith("[anon:dalvik-alloc") ||
                                name.StartsWith("[anon:dalvik-main") ||
                                name.StartsWith("[anon:dalvik-large") ||
                                name.StartsWith("[anon:dalvik-non") ||
                                name.StartsWith("[anon:dalvik-zygote"))
                            {
                                return RegionType.AndroidRuntime;
                            }
                            else if (name.StartsWith("/dev/"))
                            {
                                return RegionType.Device;
                            }
                            break;
                        }
                        default:
                            break;
                    }
                }

                switch ((SystemMemoryRegionEntriesCache.MemoryType)type)
                {
                    case SystemMemoryRegionEntriesCache.MemoryType.Private:
                        return RegionType.Native;
                    case SystemMemoryRegionEntriesCache.MemoryType.Mapped:
                        return RegionType.Mapped;
                    case SystemMemoryRegionEntriesCache.MemoryType.Device:
                        return RegionType.Device;
                    case SystemMemoryRegionEntriesCache.MemoryType.Shared:
                        return RegionType.Shared;
                }

                return RegionType.Native;
            }

            // Adds a fixed number of points from a container of the specific type
            static int AddPoints(int index, AddressPoint[] data, SourceLink.SourceId sourceName, int count, Func<int, (bool valid, ulong address, ulong size, RegionType type)> accessor)
            {
                for (int i = 0; i < count; i++)
                {
                    var (valid, address, size, type) = accessor(i);

                    if (!valid || (size == 0))
                        continue;

                    var id = index;
                    data[index++] = new AddressPoint()
                    {
                        Address = address,
                        Source = new SourceLink(sourceName, i, SourceLink.SourceFlags.StartPoint),
                        ChildrenCount = id,
                        Type = type,
                    };

                    data[index++] = new AddressPoint()
                    {
                        Address = address + size,
                        Source = new SourceLink(sourceName, 0, SourceLink.SourceFlags.EndPoint),
                        ChildrenCount = id,
                        Type = RegionType.Free,
                    };
                }

                return index;
            }

            public void DebugPrintToFile()
            {
                using (var file = new StreamWriter("hierarchy.txt"))
                {
                    DebugPrintToFile(file, 0, m_ItemsCount, "");
                }
            }

            void DebugPrintToFile(StreamWriter file, long start, long count, string prefix)
            {
                for (long i = 0; i < count; i++)
                {
                    var cur = m_CombinedData[start + i];

                    file.WriteLine(prefix + $"[{start + i:D8}] - {cur.Address:X16} - {cur.ChildrenCount:D8} - {cur.Source.Id,10} - {cur.Type,10} - {GetName(start + i)}");

                    if (cur.ChildrenCount > 0)
                    {
                        DebugPrintToFile(file, start + i + 1, cur.ChildrenCount, prefix + "-");
                        i += cur.ChildrenCount;
                    }
                }
            }
        }
    }
}

using System;
using System.IO;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using Unity.MemoryProfiler.Editor.UI;
using Unity.MemoryProfiler.Editor.Diagnostics;

// Pre com.unity.collections@2.1.0 NativeHashMap was not constraining its held data to unmanaged but to struct.
// NativeHashSet does not have the same issue, but for ease of use may get an alias below for EntityId.
#if !UNMANAGED_NATIVE_HASHMAP_AVAILABLE
#if !ENTITY_ID_CHANGED_SIZE
using InstanceIdToNativeObjectIndex = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<Unity.MemoryProfiler.Editor.EntityId, long>;
#else
using InstanceIdToNativeObjectIndex = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<UnityEngine.EntityId, long>;
#endif
using LongToLongHashMap = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<long, long>;
#else
#if !ENTITY_ID_CHANGED_SIZE
using InstanceIdToNativeObjectIndex = Unity.Collections.NativeHashMap<Unity.MemoryProfiler.Editor.EntityId, long>;
#else
using InstanceIdToNativeObjectIndex = Unity.Collections.NativeHashMap<UnityEngine.EntityId, long>;
#endif
using LongToLongHashMap = Unity.Collections.NativeHashMap<long, long>;
#endif

using Debug = UnityEngine.Debug;

#if !ENTITY_ID_CHANGED_SIZE
// the official EntityId lives in the UnityEngine namespace, which might be be added as a using via the IDE,
// so to avoid mistakenly using a version of this struct with the wrong size, alias it here.
using EntityId = Unity.MemoryProfiler.Editor.EntityId;
#else
// This should be greyed out by the IDE, otherwise you're missing an alias above
using UnityEngine;
using EntityId = UnityEngine.EntityId;
#endif

namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
        public unsafe class SceneRootEntriesCache : IDisposable
        {
            public const string DontDestroyOnLoadSceneName = "DontDestroyOnLoad";
            public readonly long DontDestroyOnLoadSceneIndex = -1;

            // the number of scenes
            public long SceneCount;
            public long SceneRootCount;
            public long PrefabRootCount;
            public long PrefabTransformCount;
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
            public NestedDynamicArray<SourceIndex> SceneIndexedRootTransformInstanceIds;
            /// <summary>
            /// All of the scene root transform entity ids, only used for loading in the data,
            /// and populating the Source Indices in <see cref="AllSceneRootTransformSourceIndices"/> and
            /// <see cref="SceneIndexedRootTransformInstanceIds"/> and disposed during <see cref="GenerateBaseData"/>
            /// </summary>
            DynamicArray<EntityId> m_AllSceneRootTransformInstanceIds = default;
            /// <summary>
            /// All of the scene root gameobject instance ids
            /// </summary>
            public DynamicArray<SourceIndex> AllSceneRootTransformSourceIndices = default;
            public DynamicArray<SourceIndex> AllPrefabRootTransformSourceIndices = default;
            /// <summary>
            /// Maps all Native Object indices of persistent GameObject and Transform instances (root and non-root)
            /// to Prefab Root Indices which index into <see cref="AllPrefabRootTransformSourceIndices"/> for their
            /// root transforms.
            /// </summary>
            public LongToLongHashMap NativeObjectIndexToPrefabRootIndex = default;

#if ENTITY_ID_STRUCT_AVAILABLE && !ENTITY_ID_CHANGED_SIZE
            static SceneRootEntriesCache()
            {
                Checks.IsTrue((typeof(EntityId) != typeof(UnityEngine.EntityId)), "The wrong type of EntityId struct is used, probably due to accidentally addin a 'using UnityEngine;' to this file.");
            }
#endif

            public SceneRootEntriesCache(ref IFileReader reader)
            {
                SceneCount = reader.GetEntryCount(EntryType.SceneObjects_Name);
                AssetPath = new string[SceneCount];
                Name = new string[SceneCount];
                Path = new string[SceneCount];

                if (SceneCount == 0)
                {
                    using var noOffsets = new DynamicArray<long>(1, Allocator.Temp);
                    noOffsets[0] = 0;
                    var sourceIds = new DynamicArray<SourceIndex>(0, Allocator.Persistent);
                    SceneIndexedRootTransformInstanceIds = new NestedDynamicArray<SourceIndex>(noOffsets, sourceIds);
                    return;
                }

                using (var tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.SceneObjects_Name, 0, SceneCount);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.SceneObjects_Name, tmp, 0, SceneCount);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref Name);
                }
                // Find the DontDestroyOnLoad Scene, which is always the last one unless someone changed the logic in the native NativeMemorySnapshot code.
                for (var i = SceneCount - 1; i >= 0; i--)
                {
                    if (Name[i] == DontDestroyOnLoadSceneName)
                    {
                        DontDestroyOnLoadSceneIndex = i;
                        break;
                    }
                }

                using (var tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.SceneObjects_Path, 0, SceneCount);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.SceneObjects_Path, tmp, 0, SceneCount);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref Path);
                }

                BuildIndex = reader.Read(EntryType.SceneObjects_BuildIndex, 0, SceneCount, Allocator.Persistent).Result.Reinterpret<int>();

                using (var tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.SceneObjects_AssetPath, 0, SceneCount);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.SceneObjects_AssetPath, tmp, 0, SceneCount);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref AssetPath);
                }

                SceneRootCount = reader.GetEntryCount(EntryType.SceneObjects_RootIds);
                RootCounts = reader.Read(EntryType.SceneObjects_RootIdCounts, 0, SceneCount, Allocator.Persistent).Result.Reinterpret<int>();
                RootOffsets = reader.Read(EntryType.SceneObjects_RootIdOffsets, 0, SceneCount, Allocator.Persistent).Result.Reinterpret<int>();

                if (reader.FormatVersion < FormatVersion.EntityIDAs8ByteStructs)
                {
                    // Read file has the old EntityId format
                    using var instanceIDInts = reader.Read(EntryType.SceneObjects_RootIds, 0, SceneRootCount, Allocator.Temp).Result.Reinterpret<int>();
                    // Clear the memory on alloc. The MemCpyStride in ConvertInstanceId won't initialize the blank spaces
                    m_AllSceneRootTransformInstanceIds = new DynamicArray<EntityId>(SceneRootCount, Allocator.Persistent, memClear: true);
                    instanceIDInts.ConvertInstanceIdIntsToEntityIds(ref m_AllSceneRootTransformInstanceIds);
                }
                else
                {
                    m_AllSceneRootTransformInstanceIds = reader.Read(EntryType.SceneObjects_RootIds, 0, SceneRootCount, Allocator.Persistent).Result.Reinterpret<EntityId>();
                }
            }

            public void Dispose()
            {
                SceneCount = 0;
                SceneRootCount = 0;
                PrefabRootCount = 0;
                PrefabTransformCount = 0;
                AssetPath = null;
                Name = null;
                Path = null;
                BuildIndex.Dispose();
                RootCounts.Dispose();
                RootOffsets.Dispose();
                if (SceneIndexedRootTransformInstanceIds.IsCreated)
                    SceneIndexedRootTransformInstanceIds.Dispose();
                else if (AllSceneRootTransformSourceIndices.IsCreated)
                    // Dispose only if it wasn't disposed as the backing data for SceneIndexedRootTransformInstanceIds
                    AllSceneRootTransformSourceIndices.Dispose();
                if (m_AllSceneRootTransformInstanceIds.IsCreated) // Normally already gets disposed during GenerateBaseData
                    m_AllSceneRootTransformInstanceIds.Dispose();
                AllPrefabRootTransformSourceIndices.Dispose();
                NativeObjectIndexToPrefabRootIndex.Dispose();
            }

            // TODO: Jobify, only needs
            // Connections.ReferenceTo
            // NativeObjects.NativeTypeArrayIndex
            // NativeObjects.Flags
            public void GenerateBaseData(CachedSnapshot snapshot, InstanceIdToNativeObjectIndex nativeObjectsInstanceId2Index, int gameObjectNativeTypeIndex)
            {
                AllPrefabRootTransformSourceIndices = new DynamicArray<SourceIndex>(0, 100, Allocator.Persistent);

                ProcessPrefabRoots(snapshot, gameObjectNativeTypeIndex);

                AllSceneRootTransformSourceIndices = new DynamicArray<SourceIndex>(SceneRootCount, Allocator.Persistent);
                {
                    for (long i = 0; i < SceneRootCount; i++)
                    {
                        var transformIndex = new SourceIndex(SourceIndex.SourceId.NativeObject, nativeObjectsInstanceId2Index[m_AllSceneRootTransformInstanceIds[i]]);
                        AllSceneRootTransformSourceIndices[i] = transformIndex;
                        var gameObjectIndex = ObjectConnection.GetGameObjectIndexFromTransformOrComponentIndex(snapshot, transformIndex, gameObjectNativeTypeIndex);
                        if (!snapshot.HasPrefabRootInfo)
                        {
                            // Mark these as roots if the info isn't there
                            snapshot.NativeObjects.Flags[transformIndex.Index] |= ObjectFlags.IsRoot;
                            if (gameObjectIndex.Valid) // with entities, it's possible to have a transform without a gameobject
                                snapshot.NativeObjects.Flags[gameObjectIndex.Index] |= ObjectFlags.IsRoot;
                        }
                    }
                    // Scene root transforms are now stored as Source Indices, so we can dispose of the loaded InstanceID/EntityId data
                    m_AllSceneRootTransformInstanceIds.Dispose();
                }
                if (!snapshot.HasSceneRootsAndAssetbundles || SceneCount == 0)
                {
                    NativeObjectIndexToPrefabRootIndex = new LongToLongHashMap(0, Allocator.Persistent);
                    return;
                }

                using var offsets = GetOffsetsForNestedSceneRoots(sizeof(SourceIndex), Allocator.Temp);

                SceneIndexedRootTransformInstanceIds = new NestedDynamicArray<SourceIndex>(offsets, AllSceneRootTransformSourceIndices);
            }

            DynamicArray<long> GetOffsetsForNestedSceneRoots(int sizeOfElements, Allocator allocator)
            {
                var offsets = new DynamicArray<long>(RootOffsets.Count + 1, allocator);
                for (long i = 0; i < RootOffsets.Count; i++)
                {
                    offsets[i] = RootOffsets[i] * sizeOfElements;
                }
                // Scene offsets are not reported as other nested arrays and are missing the total count added to the end
                offsets[RootOffsets.Count] = SceneRootCount * sizeOfElements;
                return offsets;
            }

            /// <summary>
            ///
            /// </summary>
            /// <param name="snapshot"></param>
            /// <param name="gameObjectNativeTypeIndex"></param>
            /// <param name="prefabObject"></param>
            /// <param name="nativeTypeInfo"></param>
            /// <param name="calculateAndFlagRecursively"> if false, only returns the root count,
            /// otherwise it includes the count of all child transforms recursively</param>
            /// <param name="addTempFlag"> if false, only returns the root count,
            /// otherwise it includes the count of all child transforms recursively</param>
            /// <returns></returns>
            long MarkPrefabRootFromPrefabAndCalculateHierarchySize(CachedSnapshot snapshot, int gameObjectNativeTypeIndex,
                SourceIndex prefabObject, in DynamicArrayRef<UnifiedType> nativeTypeInfo, ref NativeHashSet<SourceIndex> cachedHashset,
                bool calculateAndFlagRecursively = false, bool addTempFlag = false)
            {
                if (!snapshot.Connections.ReferenceTo.TryGetValue(prefabObject, out var refs))
                    return 0;
                var prefabTransformCount = 0L;
                var flagsToSet = ObjectFlags.IsRoot | (addTempFlag ? ObjectFlags.IsTemporarilyMarked : 0);
                foreach (var reference in refs)
                {
                    var transformReference = reference;
                    if (nativeTypeInfo[snapshot.NativeObjects.NativeTypeArrayIndex[reference.Index]].IsGameObjectType)
                    {
                        // redirect from GameObject reference to its Transform
                        transformReference = ObjectConnection.GetTransformIndexFromGameObject(snapshot, reference);
                    }
                    if (transformReference.Id is SourceIndex.SourceId.NativeObject
                        && snapshot.NativeObjects.Flags[transformReference.Index].HasFlag(ObjectFlags.IsPersistent)
                        && !snapshot.NativeObjects.Flags[transformReference.Index].HasFlag(ObjectFlags.IsTemporarilyMarked)
                        && nativeTypeInfo[snapshot.NativeObjects.NativeTypeArrayIndex[transformReference.Index]].IsTransformType)
                    {
                        AllPrefabRootTransformSourceIndices.Push(transformReference);

                        FlagTransformAndItsGameObject(snapshot, gameObjectNativeTypeIndex, transformReference, flagsToSet);

                        if (calculateAndFlagRecursively)
                        {
                            CountAndFlagChildTransforms(snapshot, gameObjectNativeTypeIndex, transformReference, default, addTempFlag, ref prefabTransformCount, ref cachedHashset);
                        }
                        else
                        {
                            ++prefabTransformCount;
                        }
                    }
                }
                return prefabTransformCount;
            }

            void CountAndFlagChildTransforms(CachedSnapshot snapshot, int gameObjectNativeTypeIndex, SourceIndex currentTransform, SourceIndex parentTransform, bool addTempFlag,
                ref long prefabTransformCount, ref NativeHashSet<SourceIndex> cachedHashset)
            {
                var stack = new DynamicArray<(SourceIndex child, SourceIndex parent)>(0, 10, Allocator.Temp);
                var flagsToSet = addTempFlag ? ObjectFlags.IsTemporarilyMarked : 0;

                stack.Push(new(currentTransform, parentTransform));

                while (stack.Count > 0)
                {
                    var (current, parent) = stack.Pop();
                    ++prefabTransformCount;

                    if (ObjectConnection.TryGetConnectedTransformIndicesFromTransformIndex(snapshot, current, parent, ref cachedHashset))
                    {
                        foreach (var reference in cachedHashset)
                        {
                            if (!snapshot.NativeObjects.Flags[reference.Index].HasFlag(ObjectFlags.IsTemporarilyMarked))
                            {
                                FlagTransformAndItsGameObject(snapshot, gameObjectNativeTypeIndex, reference, flagsToSet);
                                stack.Push(new(reference, current));
                            }
                        }
                    }
                }
            }

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            void FlagTransformAndItsGameObject(CachedSnapshot snapshot, int gameObjectNativeTypeIndex, SourceIndex transform, ObjectFlags flagsToSet)
            {
                var gameObject = ObjectConnection.GetGameObjectIndexFromTransformOrComponentIndex(snapshot, transform, gameObjectNativeTypeIndex, isPersistent: true);

                snapshot.NativeObjects.Flags[transform.Index] |= flagsToSet;
                if (gameObject.Valid) // with entities, it's possible to have a transform without a gameobject
                    snapshot.NativeObjects.Flags[gameObject.Index] |= flagsToSet;
            }

            // TODO: Jobify, only needs
            // Connections.ReferenceTo
            // NativeTypes.IsTransformOrRectTransform (aka a check for Transform and RectTransform types
            // NativeObjects.NativeTypeArrayIndex
            // NativeObjects.Flags
            void ProcessPrefabRoots(CachedSnapshot snapshot, int gameObjectNativeTypeIndex)
            {
                ref readonly var nativeTypeInfo = ref snapshot.TypeDescriptions.UnifiedTypeInfoNative;
                // count the prefabs to avoid hashmap growth
                PrefabTransformCount = 0L;
                if (!snapshot.HasPrefabRootInfo)
                {
                    // if there is no root info for prefabs yet, build it here
                    var cachedHashSetForMarking = new NativeHashSet<SourceIndex>(20, Allocator.Temp);
                    try
                    {
                        if (snapshot.MetaData.IsEditorCapture && snapshot.NativeTypes.PrefabIdx != NativeTypeEntriesCache.InvalidTypeIndex)
                        {
                            // In the Editor, we can use the Prefab object to grab the roots
                            for (long i = 0; i < snapshot.NativeObjects.Count; i++)
                            {
                                if (snapshot.NativeObjects.NativeTypeArrayIndex[i] == snapshot.NativeTypes.PrefabIdx)
                                {
                                    MarkPrefabRootFromPrefabAndCalculateHierarchySize(snapshot, gameObjectNativeTypeIndex,
                                        new SourceIndex(SourceIndex.SourceId.NativeObject, i),
                                        in nativeTypeInfo, ref cachedHashSetForMarking, calculateAndFlagRecursively: true,
                                        // since we process all SceneObjects that are not tied to a Prefab afterwards,
                                        // we need to flag the transforms we found here to avoid double counting
                                        addTempFlag: true
                                        );
                                }
                            }
                        }

                        using var lastTwoTransforms = new DynamicArray<SourceIndex>(0, 2, Allocator.Temp);
                        for (long i = 0; i < snapshot.NativeObjects.Count; i++)
                        {
                            var flags = snapshot.NativeObjects.Flags[i];

                            if (flags.HasFlag(ObjectFlags.IsPersistent) && nativeTypeInfo[snapshot.NativeObjects.NativeTypeArrayIndex[i]].IsTransformType)
                            {
                                ++PrefabTransformCount;
                                // ignore this object if it already got marked by its last child
                                if (flags.HasFlag(ObjectFlags.IsRoot) || flags.HasFlag(ObjectFlags.IsTemporarilyMarked))
                                    continue;
                                lastTwoTransforms.Clear(false);
                                var thisTransform = new SourceIndex(SourceIndex.SourceId.NativeObject, i);
                                var currentTransform = thisTransform;

                                // In lieu of prefab roots being reported with the IsRoot flag, and while EntityId is 4 bytes in size there is a fallback mechanism
                                // Essentially, each Transform has a list of children and then its parent reported as its last connection.
                                // That means that by always recursively taking the last Transform referenced from our current Transform,
                                // we would eventually cycle between the root and its last child. The root has a lower absolut InstanceId.

                                // So, grab up to 2 potential parents
                                for (int j = 0; j < 2; j++)
                                {
                                    var potentialParent = ObjectConnection.GetParentTransformOrLastChild(snapshot, currentTransform, isPersistent: true);
                                    if (!potentialParent.Valid)
                                        break;
                                    lastTwoTransforms.Push(potentialParent);
                                    // was the parent already marked?
                                    if (snapshot.NativeObjects.Flags[potentialParent.Index].HasFlag(ObjectFlags.IsRoot))
                                        break;
                                    currentTransform = potentialParent;
                                }
                                switch (lastTwoTransforms.Count)
                                {
                                    case 0:
                                    {
                                        // no valid parent nor child makes this a lone Transform, and a root
                                        AllPrefabRootTransformSourceIndices.Push(thisTransform);
                                        FlagTransformAndItsGameObject(snapshot, gameObjectNativeTypeIndex, thisTransform, ObjectFlags.IsRoot);
                                        break;
                                    }
                                    case 1:
                                        // The parent was already flagged as root. Nothing to do here
                                        break;
                                    case 2:
                                        // If the second one isn't a reference back, thisTransform is not a root.
                                        // While lastTwoTransforms[0] _might_ be a root, we'll come across it down the line. Drop it for now.
                                        if (lastTwoTransforms[1] == thisTransform)
                                        {
                                            // one of these two is the root.
                                            var thisInstanceId = snapshot.NativeObjects.InstanceId[thisTransform.Index].ConvertToIdInt();
                                            var otherInstanceId = snapshot.NativeObjects.InstanceId[lastTwoTransforms[0].Index].ConvertToIdInt();
                                            // There is a chance that transforms lower down the line than the root where loaded in first
                                            // But since we're down to a coin toss, this is a best guess attempt that is likely to be correct in MOST cases
                                            // And in case it's wrong, the effect is only mildly confusing.
                                            var prefabRootTransform = Math.Abs(thisInstanceId) < Math.Abs(otherInstanceId) ? thisTransform : lastTwoTransforms[0];
                                            AllPrefabRootTransformSourceIndices.Push(prefabRootTransform);
                                            FlagTransformAndItsGameObject(snapshot, gameObjectNativeTypeIndex, prefabRootTransform, ObjectFlags.IsRoot);
                                        }
                                        break;
                                    default:
                                        Debug.LogError($"Found {lastTwoTransforms.Count} elements when looking for the root. The data is faulty.");
                                        break;
                                }
                            }
                        }

                    }
                    catch
                    {
                        // don't throw the exception explicitly but do a generic rethrow in order to not stomp the callstack
                        throw;
                    }
                    finally
                    {
                        cachedHashSetForMarking.Dispose();
                    }

                    // Clear the temp flags
                    for (long i = 0; i < snapshot.NativeObjects.Count; i++)
                    {
                        if (snapshot.NativeObjects.Flags[i].HasFlag(ObjectFlags.IsTemporarilyMarked))
                        {
                            snapshot.NativeObjects.Flags[i] &= ~ObjectFlags.IsTemporarilyMarked;
                        }
                    }
                }
                else
                {
                    // If roots have been reported, we still want to count the amount of prefab transforms and collect their roots here.
                    for (long i = 0; i < snapshot.NativeObjects.Count; i++)
                    {
                        if (snapshot.NativeObjects.Flags[i].HasFlag(ObjectFlags.IsPersistent) && nativeTypeInfo[snapshot.NativeObjects.NativeTypeArrayIndex[i]].IsTransformType)
                        {
                            ++PrefabTransformCount;
                            if (snapshot.NativeObjects.Flags[i].HasFlag(ObjectFlags.IsRoot))
                                AllPrefabRootTransformSourceIndices.Push(new SourceIndex(SourceIndex.SourceId.NativeObject, i));
                        }
                    }
                }

                var lookupCount = PrefabTransformCount * 2;
                NativeObjectIndexToPrefabRootIndex = new LongToLongHashMap((int)lookupCount, Allocator.Persistent);

                PrefabRootCount = AllPrefabRootTransformSourceIndices.Count;
                // Process the prefab hierarchies
                ProcessPrefabHierarchies(snapshot, AllPrefabRootTransformSourceIndices, gameObjectNativeTypeIndex);
            }

            // TODO: Jobify, only needs
            // Connections.ReferenceTo
            // NativeTypes.IsTransformOrRectTransform (aka a check for Transform and RectTransform types
            // NativeObjects.NativeTypeArrayIndex
            // NativeObjects.Flags
            public void ProcessPrefabHierarchies(CachedSnapshot snapshot, DynamicArray<SourceIndex> prefabRootTransforms, int gameObjectNativeTypeIndex)
            {
                var cachedHashSet = new NativeHashSet<SourceIndex>(100, Allocator.Temp);
                var currentDepthLayer = new DynamicArray<(SourceIndex current, SourceIndex parent, long prefabIndex)>(0, prefabRootTransforms.Count, Allocator.Temp);
                var nextDepthLayer = new DynamicArray<(SourceIndex current, SourceIndex parent, long prefabIndex)>(0, prefabRootTransforms.Count, Allocator.Temp);

                // Nested Prefabs might reference into the hierarchy of other prefabs (i.e. not just at their roots)
                // so we process these breadth first and ignore reoccurences as they would only happen for nested prefabs.

                try
                {
                    var prefabIndex = 0L;
                    foreach (var rootTransform in prefabRootTransforms)
                    {
                        currentDepthLayer.Push((rootTransform, default, prefabIndex++));
                    }
                    while (currentDepthLayer.Count > 0)
                    {
                        var (current, parent, prefabIdx) = currentDepthLayer.Pop();

                        if (NativeObjectIndexToPrefabRootIndex.TryAdd(current.Index, prefabIdx))
                        {
                            // Only add the GameObject and its children if the Transform was successfully added and not part of a different prefab
                            var gameObjectIndex =
                                ObjectConnection.GetGameObjectIndexFromTransformOrComponentIndex(snapshot, current,
                                    gameObjectNativeTypeIndex);
                            if (gameObjectIndex.Valid) // with entities, it's possible to have a transform without a GameObject
                            {
                                if (!NativeObjectIndexToPrefabRootIndex.TryAdd(gameObjectIndex.Index, prefabIdx))
                                {
                                    throw new InvalidDataException($"Failed to add prefab gameObjectIndex at index {gameObjectIndex.Index} to NativeObjectIndexToPrefabRootIndex map.");
                                }
                            }

                            if (ObjectConnection.TryGetConnectedTransformIndicesFromTransformIndex(snapshot, current,
                                    parent, ref cachedHashSet))
                            {
                                foreach (var child in cachedHashSet)
                                {
                                    // outright ignore nested prefab roots
                                    if (!snapshot.NativeObjects.Flags[child.Index].HasFlag(ObjectFlags.IsRoot))
                                        nextDepthLayer.Push((child, current, prefabIdx));
                                }
                            }
                        }
                        // If the current depth layer is empty, swap the layers
                        if (currentDepthLayer.Count <= 0)
                            (currentDepthLayer, nextDepthLayer) = (nextDepthLayer, currentDepthLayer);
                    }
                }
                finally
                {
                    currentDepthLayer.Dispose();
                    nextDepthLayer.Dispose();
                    cachedHashSet.Dispose();
                }
                return;
            }
        }
    }
}

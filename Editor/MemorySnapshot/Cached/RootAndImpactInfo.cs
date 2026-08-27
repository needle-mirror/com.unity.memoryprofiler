#if DEBUG_VALIDATION
#define VALIDATE_ROOT_AND_IMPACT
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.MemoryProfiler.Editor.EnumerationUtilities;
using Unity.MemoryProfiler.Editor.Extensions;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.UI;
using Unity.MemoryProfiler.Editor.UIContentData;
using Unity.Profiling;
using Unity.Profiling.Memory;
using UnityEngine;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;
using Debug = UnityEngine.Debug;

namespace Unity.MemoryProfiler.Editor
{
    struct ReferencesIterator : IEnumerator<ReferencesIterator.IteratedReference>
    {
        public struct IteratedReference
        {
            public SourceIndex Reference;
            public BurstableBool IsOwned;
        }

        readonly long m_OwnedChildListCount;
        long m_OwnedChildIndex;
        SourceIndex m_OwnedSingleChild;
        DynamicArrayEnumerator<DynamicArrayRef<SourceIndex>, SourceIndex> m_OwnedReferencesIterator;
        readonly long m_UnownedChildListCount;
        long m_UnownedChildIndex;
        SourceIndex m_UnownedSingleChild;
        DynamicArrayEnumerator<DynamicArrayRef<SourceIndex>, SourceIndex> m_UnownedReferencesIterator;

        public ReferencesIterator(RootAndImpactInfo rootAndImpactInfo, SourceIndex referencesOwner, bool includeUnownedReferences)
        {
            ReferencesListInfo unownedReferencesListInfo;
            var ownedReferencesListInfo = SourceIndexToRootAndImpactInfoMapper.GetNestedElement(
                in rootAndImpactInfo.RootPathOwnedReferencesLookup, referencesOwner);
            m_OwnedChildListCount = ownedReferencesListInfo.HasMultipleChildren
                ? ReferencesListInfo.SourceIndexToCount(
                    rootAndImpactInfo.OwnedChildList[ownedReferencesListInfo.IndexInChildList])
                : (ownedReferencesListInfo.HasOneChild ? 1 : 0);
            if (includeUnownedReferences)
            {
                unownedReferencesListInfo = SourceIndexToRootAndImpactInfoMapper.GetNestedElement(
                    in rootAndImpactInfo.UnownedReferencesLookup, referencesOwner);
            }
            else
            {
                unownedReferencesListInfo = new ReferencesListInfo(noChildren: true);
            }
            m_UnownedChildListCount = unownedReferencesListInfo.HasMultipleChildren
                ? ReferencesListInfo.SourceIndexToCount(
                    rootAndImpactInfo.UnownedChildList[unownedReferencesListInfo.IndexInChildList])
                : (unownedReferencesListInfo.HasOneChild ? 1 : 0);
            m_OwnedChildIndex = -1;
            m_UnownedChildIndex = -1;
            unsafe
            {
                var offsetOwnedChildren = m_OwnedChildListCount > 1 ? ownedReferencesListInfo.IndexInChildList + 1 : 0;
                m_OwnedReferencesIterator = (DynamicArrayEnumerator<DynamicArrayRef<SourceIndex>, SourceIndex>)DynamicArrayRef<SourceIndex>.ConvertExistingDataToDynamicArrayRef(
                    rootAndImpactInfo.OwnedChildList.GetUnsafeTypedPtr() + offsetOwnedChildren, m_OwnedChildListCount)
                    .GetEnumerator();

                var offsetUnownedChildren = m_UnownedChildListCount > 1 ? unownedReferencesListInfo.IndexInChildList + 1 : 0;
                m_UnownedReferencesIterator = (DynamicArrayEnumerator<DynamicArrayRef<SourceIndex>, SourceIndex>)DynamicArrayRef<SourceIndex>.ConvertExistingDataToDynamicArrayRef(
                    rootAndImpactInfo.UnownedChildList.GetUnsafeTypedPtr() + offsetUnownedChildren, m_UnownedChildListCount)
                    .GetEnumerator();
            }
            m_OwnedSingleChild = ownedReferencesListInfo.SingleChild;
            m_UnownedSingleChild = unownedReferencesListInfo.SingleChild;
        }

        public bool MoveNext()
        {
            if (!m_OwnedReferencesIterator.MoveNext())
            {
                m_OwnedChildIndex = m_OwnedChildListCount;
                ++m_UnownedChildIndex;
                return m_UnownedReferencesIterator.MoveNext();
            }
            ++m_OwnedChildIndex;
            return true;
        }

        public void Reset()
        {
            m_OwnedChildIndex = -1;
            m_UnownedChildIndex = -1;
            m_OwnedReferencesIterator.Reset();
            m_UnownedReferencesIterator.Reset();
        }

        public IteratedReference Current
        {
            get
            {
                if (m_OwnedChildIndex < m_OwnedChildListCount)
                {
                    return new IteratedReference
                    {
                        Reference = m_OwnedChildListCount == 1 ? m_OwnedSingleChild : m_OwnedReferencesIterator.Current,
                        IsOwned = BurstableBool.TRUE
                    };
                }
                else if (m_UnownedChildIndex < m_UnownedChildListCount)
                {
                    return new IteratedReference
                    {
                        Reference = m_UnownedChildListCount == 1 ? m_UnownedSingleChild : m_UnownedReferencesIterator.Current,
                        IsOwned = BurstableBool.FALSE
                    };
                }
                else
                {
                    return new IteratedReference
                    {
                        Reference = default,
                        IsOwned = BurstableBool.FALSE
                    };
                }
            }
        }

        object IEnumerator.Current => Current;

        public void Dispose()
        {
            m_OwnedReferencesIterator.Dispose();
            m_UnownedReferencesIterator.Dispose();
        }
    }

    readonly struct ReferencesListInfo
    {
        /// <summary>
        /// Validated not to clash with <see cref="SourceIndex.SourceId"/> via test in
        /// CachedSnapshotTests.SourceIndexId_DoesNotClashWith_SingleChildBitFlag.
        /// </summary>
        internal const long OnlyChildFakeChildListIndexByte = 1L << 62;
        public const long NoEntriesIndex = -1L;
        /// <summary>
        /// In case of further bits flags getting added, update this bitmask accordingly.
        /// </summary>
        const long k_ChildListIndexBitmask = OnlyChildFakeChildListIndexByte;

        public readonly bool IsLeaf => m_ChildListIndex == NoEntriesIndex;
        public readonly bool HasOneChild => !IsLeaf && (m_ChildListIndex & OnlyChildFakeChildListIndexByte) != 0;
        public readonly bool HasMultipleChildren => !IsLeaf && (m_ChildListIndex & k_ChildListIndexBitmask) == 0;
        public readonly long IndexInChildList => IsLeaf ? NoEntriesIndex : m_ChildListIndex & ~OnlyChildFakeChildListIndexByte;
        public readonly unsafe SourceIndex SingleChild
        {
            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            get
            {
                var test = IndexInChildList;
                return *(SourceIndex*)&test;
            }
        }

        readonly long m_ChildListIndex;

        /// <summary>
        /// No children.
        /// </summary>
        /// <param name="noChildren"></param>
        public ReferencesListInfo(bool noChildren) : this(NoEntriesIndex, default, true)
        {
            Checks.IsTrue(noChildren);
        }
        /// <summary>
        /// Only non-owned references.
        /// </summary>
        /// <param name="indexInReferencesTo"></param>
        public ReferencesListInfo(long indexInReferencesTo) : this(indexInReferencesTo, default, false)
        {
            Checks.CheckNotEquals(indexInReferencesTo, NoEntriesIndex);
        }
        /// <summary>
        /// Only owned references.
        /// </summary>
        /// <param name="singleChild"></param>
        public ReferencesListInfo(SourceIndex singleChild) : this(NoEntriesIndex, singleChild, false)
        {
            Checks.IsTrue(singleChild.Valid);
        }

        public ReferencesListInfo(
            long indexOfChildCountInChildList,
            SourceIndex singleChild, bool noChildren)
        {
            // The information is encoded into this struct's long in one of 3 states:
            // 1. No children is encoded as -1
            // 2. A single child entry has the OnlyChildFakeChildListIndexByte flag set
            //    and directly contains the SourceIndex to the child.
            //    This allows for avoiding the lookup of the index in the child list.
            // 3. The index of the child within the childlist. The child's SourceIndex
            //    can be retrived by adding 1 to it and the owning object's index to its
            //    count entry in the child list.
            if (noChildren)
            {
                m_ChildListIndex = NoEntriesIndex;
            }
            else if (singleChild.Valid)
            {
                unsafe
                {
                    // Convert the child's SourceIndex to long and add the flag before storing it
                    m_ChildListIndex = *(long*)&singleChild | OnlyChildFakeChildListIndexByte;
                }
            }
            else
            {
                m_ChildListIndex = indexOfChildCountInChildList;
            }
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public static unsafe SourceIndex CountToSourceIndex(long count)
        {
            return *(SourceIndex*)&count;
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public static unsafe long SourceIndexToCount(SourceIndex count)
        {
            return *(long*)&count;
        }
    }

    readonly struct ShortestRootPathInfo
    {
        public bool Valid => Root.Valid;
        public bool IsRoot => Valid && !Parent.Valid && Parent.Index == (long)SourceIndex.SpecialNoneCase.Root;

        public readonly SourceIndex Root;
        public readonly SourceIndex Parent;
        public readonly long Depth;
        public readonly long IndexInParentsChildList;

        public ShortestRootPathInfo(SourceIndex root, SourceIndex parent, long depth, long indexInParentsChildList = ReferencesListInfo.NoEntriesIndex)
        {
            Root = root;
            Parent = parent;
            Depth = depth;

            if (!Parent.Valid)
            {
                Parent = new SourceIndex(SourceIndex.SourceId.None, (long)SourceIndex.SpecialNoneCase.Root);
                IndexInParentsChildList = ReferencesListInfo.NoEntriesIndex;
            }
            else
            {
                IndexInParentsChildList = indexInParentsChildList;
            }
        }
    }
    readonly struct SourceIndexToRootAndImpactInfoMapper
    {
        public const long IndexInNestedArrayForNativeRootReferences = 0;
        public const long IndexInNestedArrayForScenes = 1;
        public const long IndexInNestedArrayForPrefabs = 2;
        public const long IndexInNestedArrayForNativeObjects = 3;
        public const long IndexInNestedArrayForNativeAllocations = 4;
        public const long IndexInNestedArrayForManagedTypes = 5;
        public const long IndexInNestedArrayForGCHandles = 6;
        public const long IndexInNestedArrayForMangedObjects = 7;
        readonly long m_OffsetToNativeRootReferences;
        readonly long m_OffsetToScenes;
        readonly long m_OffsetToPrefabs;
        readonly long m_OffsetToNativeObjects;
        readonly long m_OffsetToNativeAllocations;
        readonly long m_OffsetToManagedTypes;
        readonly long m_OffsetToGcHandles;
        readonly long m_OffsetToManagedObjects;
        public readonly long TotalCount;
        internal SourceIndexToRootAndImpactInfoMapper(CachedSnapshot snapshot)
        {
            m_OffsetToNativeRootReferences = 0;
            m_OffsetToScenes = snapshot.NativeRootReferences.Count;
            m_OffsetToPrefabs = m_OffsetToScenes + snapshot.SceneRoots.SceneCount;
            m_OffsetToNativeObjects = m_OffsetToPrefabs + snapshot.SceneRoots.PrefabRootCount;
            m_OffsetToNativeAllocations = m_OffsetToNativeObjects + snapshot.NativeObjects.Count;
            m_OffsetToManagedTypes = m_OffsetToNativeAllocations + snapshot.NativeAllocations.Count;
            m_OffsetToGcHandles = m_OffsetToManagedTypes + snapshot.TypeDescriptions.Count;
            m_OffsetToManagedObjects = m_OffsetToGcHandles + snapshot.GcHandles.Count;
            TotalCount = m_OffsetToManagedObjects + snapshot.CrawledData.ManagedObjects.Count;
        }

        internal long SourceIndexToRootAndImpactGroupIndex(SourceIndex sourceIndex)
        {
            switch (sourceIndex.Id)
            {
                case SourceIndex.SourceId.None:
                    break;
                case SourceIndex.SourceId.SystemMemoryRegion:
                    break;
                case SourceIndex.SourceId.NativeMemoryRegion:
                    break;
                case SourceIndex.SourceId.NativeAllocation:
                    return m_OffsetToNativeAllocations + sourceIndex.Index;
                case SourceIndex.SourceId.ManagedHeapSection:
                    break;
                case SourceIndex.SourceId.NativeObject:
                    return m_OffsetToNativeObjects + sourceIndex.Index;
                case SourceIndex.SourceId.ManagedObject:
                    return m_OffsetToManagedObjects + sourceIndex.Index;
                case SourceIndex.SourceId.NativeType:
                    break;
                case SourceIndex.SourceId.ManagedType:
                    return m_OffsetToManagedTypes + sourceIndex.Index;
                case SourceIndex.SourceId.NativeRootReference:
                    return m_OffsetToNativeRootReferences + sourceIndex.Index;
                case SourceIndex.SourceId.GCHandleIndex:
                    return m_OffsetToGcHandles + sourceIndex.Index;
                case SourceIndex.SourceId.MemoryLabel:
                    break;
                case SourceIndex.SourceId.Scene:
                    return m_OffsetToScenes + sourceIndex.Index;
                case SourceIndex.SourceId.Prefab:
                    return m_OffsetToPrefabs + sourceIndex.Index;
                default:
                    break;
            }
            throw new NotImplementedException();
        }

        internal DynamicArray<long> GetOffsets<T>() where T : unmanaged
        {
            unsafe
            {
                return GetOffsets(sizeof(T));
            }
        }

        internal DynamicArray<long> GetOffsets(int typeSize)
        {
            var offsets = new DynamicArray<long>(IndexInNestedArrayForMangedObjects + 2, Allocator.Temp);
            offsets[IndexInNestedArrayForNativeRootReferences] = m_OffsetToNativeRootReferences * typeSize;
            offsets[IndexInNestedArrayForScenes] = m_OffsetToScenes * typeSize;
            offsets[IndexInNestedArrayForPrefabs] = m_OffsetToPrefabs * typeSize;
            offsets[IndexInNestedArrayForNativeObjects] = m_OffsetToNativeObjects * typeSize;
            offsets[IndexInNestedArrayForNativeAllocations] = m_OffsetToNativeAllocations * typeSize;
            offsets[IndexInNestedArrayForManagedTypes] = m_OffsetToManagedTypes * typeSize;
            offsets[IndexInNestedArrayForGCHandles] = m_OffsetToGcHandles * typeSize;
            offsets[IndexInNestedArrayForMangedObjects] = m_OffsetToManagedObjects * typeSize;
            offsets[offsets.Count - 1] = TotalCount * typeSize;
            return offsets;
        }

        /// <summary>
        /// Returns true when <paramref name="sourceId"/> is handled by <see cref="GetNestedArray"/> without
        /// throwing. The most important case is <see cref="SourceIndex.SourceId.None"/>, which is e.g. used
        /// for virtual group/structural nodes inside <see cref="RootAndImpactInfo"/> that don't correspond
        /// to tracked memory objects and therefore have no entry in any nested array.
        /// Callers that iterate arbitrary child references should guard with this check before constructing
        /// a <see cref="ReferencesIterator"/> or accessing impact data for a node.
        /// </summary>
        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public static bool IsSourceIdSupported(SourceIndex.SourceId sourceId)
        {
            switch (sourceId)
            {
                case SourceIndex.SourceId.NativeAllocation:
                case SourceIndex.SourceId.NativeObject:
                case SourceIndex.SourceId.ManagedObject:
                case SourceIndex.SourceId.ManagedType:
                case SourceIndex.SourceId.NativeRootReference:
                case SourceIndex.SourceId.GCHandleIndex:
                case SourceIndex.SourceId.Scene:
                case SourceIndex.SourceId.Prefab:
                    return true;
                default:
                    return false;
            }
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public static ref T GetNestedElement<T>(in NestedDynamicArray<T> data, SourceIndex index)
            where T : unmanaged
        {
            return ref GetNestedArray(in data, index.Id)[index.Index];
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public static ref readonly DynamicArrayRef<T> GetNestedArray<T>(in NestedDynamicArray<T> data, SourceIndex.SourceId sourceId)
            where T : unmanaged
        {
            var nestedIndex = sourceId switch
            {
                SourceIndex.SourceId.None => throw new NotImplementedException(),
                SourceIndex.SourceId.SystemMemoryRegion => throw new NotImplementedException(),
                SourceIndex.SourceId.NativeMemoryRegion => throw new NotImplementedException(),
                SourceIndex.SourceId.NativeAllocation => IndexInNestedArrayForNativeAllocations,
                SourceIndex.SourceId.ManagedHeapSection => throw new NotImplementedException(),
                SourceIndex.SourceId.NativeObject => IndexInNestedArrayForNativeObjects,
                SourceIndex.SourceId.ManagedObject => IndexInNestedArrayForMangedObjects,
                SourceIndex.SourceId.NativeType => throw new NotImplementedException(),
                SourceIndex.SourceId.ManagedType => IndexInNestedArrayForManagedTypes,
                SourceIndex.SourceId.NativeRootReference => IndexInNestedArrayForNativeRootReferences,
                SourceIndex.SourceId.GfxResource => throw new NotImplementedException(),
                SourceIndex.SourceId.GCHandleIndex => IndexInNestedArrayForGCHandles,
                SourceIndex.SourceId.MemoryLabel => throw new NotImplementedException(),
                SourceIndex.SourceId.Scene => IndexInNestedArrayForScenes,
                SourceIndex.SourceId.Prefab => IndexInNestedArrayForPrefabs,
                _ => throw new NotImplementedException()
            };
            return ref data[nestedIndex];
        }
    }
    struct MemoryImpact
    {
        /// <summary>
        /// The memory exclusively owned by this object. This does not include any shared memory,
        /// but does include the memory of this object itself.
        /// </summary>
        public NativeRootSize Exclusive;
        /// <summary>
        /// The memory shared with other objects, divided pro rata among all referees.
        /// This does not include the exclusively owned memory. For that add <see cref="Exclusive"/> or use <see cref="TotalImpact"/> to get both in one.
        /// </summary>
        public NativeRootSize SharedProRata;
        /// <summary>
        /// If this object where the sole owner of all its references, this would be the total memory attributed to it that would otherwise be shared.
        /// I.e. to get the total impact simulating no shared references, add <see cref="Exclusive"/> and <see cref="SharedInTotal"/>.
        /// </summary>
        public NativeRootSize SharedInTotal;

        /// <summary>
        /// The total memory impact attributed to this object, including exclusively owned (<see cref="Exclusive"/>)
        /// and the pro rata shared (<see cref="SharedProRata"/>) memory.
        /// </summary>
        public NativeRootSize TotalImpact => Exclusive + SharedProRata;

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public static MemoryImpact operator +(in MemoryImpact l, in NativeRootSize r) => new MemoryImpact
        {
            Exclusive = l.Exclusive + r,
            SharedProRata = l.SharedProRata + r,
            SharedInTotal = l.SharedInTotal + r,
        };

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public static MemoryImpact operator +(in MemoryImpact l, in MemoryImpact r) => new MemoryImpact
        {
            Exclusive = l.Exclusive + r.Exclusive,
            SharedProRata = l.SharedProRata + r.SharedProRata,
            SharedInTotal = l.SharedInTotal + r.SharedInTotal,
        };
    }
    /// <summary>
    /// Use this to get the root and impact information for each object.
    /// The <see cref="NestedDynamicArray{T}"/> structures are indexed by
    /// <see cref="SourceIndexToRootAndImpactInfoMapper.SourceIndexToRootAndImpactGroupIndex(SourceIndex)"/>
    /// and you can use <see cref="SourceIndexToRootAndImpactInfoMapper.GetNestedElement{T}(ref NestedDynamicArray{T}, SourceIndex)"/>
    /// to get information for a specific SourceIndex.
    /// </summary>
    class RootAndImpactInfo : IDisposable
    {
        public const int ProcessStepCount = 9;
        public bool SuccessfullyBuilt => m_Processed && OwnedChildList is { IsCreated: true, Count: > 0 };
        /// <summary>
        /// Shortest root path info for each item, entailing its root, its parent and its depth.
        ///
        /// Use <see cref="SourceIndexToRootAndImpactInfoMapper.GetNestedElement{T}(ref NestedDynamicArray{T}, SourceIndex)"/>
        /// to get the <see cref="ShortestRootPathInfo"/> for a specific SourceIndex.
        /// </summary>
        public ref NestedDynamicArray<ShortestRootPathInfo> ShortestPathInfo => ref m_ShortestPathInfo;
        NestedDynamicArray<ShortestRootPathInfo> m_ShortestPathInfo;

        /// <summary>
        /// References owned by the root path objects.
        /// Use this to get single child references or get the index to the child count in <see cref="OwnedChildList"/>,
        /// and the child indices following that.
        ///
        /// Use <see cref="SourceIndexToRootAndImpactInfoMapper.GetNestedElement{T}(ref NestedDynamicArray{T}, SourceIndex)"/>
        /// to get the <see cref="ReferencesListInfo"/> for a specific SourceIndex.
        /// </summary>
        public ref NestedDynamicArray<ReferencesListInfo> RootPathOwnedReferencesLookup => ref m_RootPathOwnedReferencesLookup;
        NestedDynamicArray<ReferencesListInfo> m_RootPathOwnedReferencesLookup;

        /// <summary>
        /// List of all owned child references. Use with <see cref="RootPathOwnedReferencesLookup"/> to get the index of the count.
        /// Use <see cref="ReferencesListInfo.SourceIndexToCount"/> to convert the SourceIndex at that index to a long count.
        /// Then the following entries are the child SourceIndex entries.
        /// </summary>
        public ref DynamicArray<SourceIndex> OwnedChildList => ref m_OwnedChildList;
        DynamicArray<SourceIndex> m_OwnedChildList;

        /// <summary>
        /// References to non-owned objects.
        /// Use this to get single child references or get the index to the child count in <see cref="UnownedChildList"/>,
        /// and the child indices following that.
        ///
        /// This excludes some references that would have messed with impact attribution, e.g.
        /// - references from the managed shell of a scene object in a prefab, if that managed shell
        ///   was how the prefab was referenced from outside the prefab.
        /// - references from a scene objects managed shells to its native object, if the native object hadn't been found yet.
        /// - references from outside the owning native object to one of its native allocations.
        ///
        /// For a more comprehensive list that includes such references, use <see cref="ObjectConnection.GetAllReferencingObjects(CachedSnapshot, SourceIndex, ref List{ObjectData}, HashSet{SourceIndex}, ObjectConnection.UnityObjectReferencesSearchMode, bool)"/>
        /// For showing object by their rooting structure, this is sufficient and avoids mis-attribution of impact.
        ///
        /// Use <see cref="SourceIndexToRootAndImpactInfoMapper.GetNestedElement{T}(ref NestedDynamicArray{T}, SourceIndex)"/>
        /// to get the <see cref="ReferencesListInfo"/> for a specific SourceIndex.
        /// </summary>
        public ref NestedDynamicArray<ReferencesListInfo> UnownedReferencesLookup => ref m_UnownedReferencesLookup;
        NestedDynamicArray<ReferencesListInfo> m_UnownedReferencesLookup;

        /// <summary>
        /// List of all non-owned references. Use with <see cref="UnownedReferencesLookup"/> to get the index of the count.
        /// Use <see cref="ReferencesListInfo.SourceIndexToCount"/> to convert the SourceIndex at that index to a long count.
        /// Then the following entries are the child SourceIndex entries.
        /// </summary>
        public ref DynamicArray<SourceIndex> UnownedChildList => ref m_UnownedChildList;
        DynamicArray<SourceIndex> m_UnownedChildList;

        /// <summary>
        /// The Memory impact attributed to each item.
        ///
        /// Use <see cref="SourceIndexToRootAndImpactInfoMapper.GetNestedElement{T}(ref NestedDynamicArray{T}, SourceIndex)"/>
        /// to get the <see cref="MemoryImpact"/> for a specific SourceIndex.
        /// </summary>
        public ref NestedDynamicArray<MemoryImpact> Impact => ref m_Impact;
        NestedDynamicArray<MemoryImpact> m_Impact;

        /// <summary>
        /// All roots that own any memory, ordered by their <see cref="OwnershipBucket"/>.
        /// </summary>
        public ref NestedDynamicArray<SourceIndex> Roots => ref m_Roots;
        NestedDynamicArray<SourceIndex> m_Roots;

        /// <summary>
        /// The number of distinct nodes in the rooted ownership forest: every root plus every owned
        /// descendant reached while building the path-to-root info. Each such node maps to exactly one
        /// row when the Roots &amp; Impact tree is flattened, so this is an exact size for pre-allocating
        /// that row list. It excludes unrooted-and-unreferenced allocations (which never enter the forest).
        /// </summary>
        public long OwnershipForestNodeCount => m_OwnershipForestNodeCount;
        long m_OwnershipForestNodeCount;

        public readonly SourceIndexToRootAndImpactInfoMapper SourceIndexToRootAndImpactInfo;

        public NativeRootSize ReferencedUnrootedMemory;
        public NativeRootSize UnreferencedUnrootedMemory;

        bool m_Processed = false;

        public struct Marker
        {
            // TODO: implement through flags to achieve burst compatibility
            // [Flags]
            // public enum MarkerFlags : byte
            // {
            //     FoundForRoot = 1 << 0,
            //     FoundForImpact = 1 << 1,
            //     FullyDistributedImpact = 1 << 2,
            // }
            //
            // MarkerFlags m_MarkerFlags;


            public bool FoundForRoot;
            public bool FoundForImpact;
            public bool FullyDistributedImpact;
        }

        /// <summary>Initial capacity for the delayed-deletion-queue set; this count is usually small or zero, so a modest reservation avoids early reallocation without over-committing.</summary>
        const int k_PersistentSceneObjectsInDelayedDeletionQueueDefaultCount = 16;

        NestedDynamicArray<Marker> m_Marker;
        NativeHashSet<SourceIndex> m_PersistentSceneObjectsInDelayedDeletionQueue;
        public bool IsPersistentSceneObjectInDelayedDeletionQueue(SourceIndex index) => m_PersistentSceneObjectsInDelayedDeletionQueue.Contains(index);

        /// <summary>Initial capacity for the broken-script-reference set; missing-script objects are usually few, so a modest reservation suffices.</summary>
        const int k_BrokenScriptReferenceObjectsDefaultCount = 16;
        // The exact native objects collected into the BrokenScriptReferences bucket (missing-script
        // MonoBehaviour/ScriptableObject instances). Recorded here so ObjectStatusComputation can flag
        // them precisely, without re-running the reference scan or mistaking a legitimately-rooted
        // missing-script object (e.g. a DontDestroyOnLoad one claimed by an earlier bucket) for a leak.
        NativeHashSet<SourceIndex> m_BrokenScriptReferenceObjects;
        public bool IsBrokenScriptReference(SourceIndex index) => m_BrokenScriptReferenceObjects.IsCreated && m_BrokenScriptReferenceObjects.Contains(index);

        /// <summary>Initial capacity for the referenced-unrooted-allocations set, sized to typical observed counts (which peak around 2k) to limit reallocation.</summary>
        const int k_ReferencedUnrootedNativeAllocationsDefaultCount = 512;
        // Unrooted native allocations (no native root reference) that were nonetheless reached via a
        // reference during the crawl — i.e. the ones counted into ReferencedUnrootedMemory rather than
        // UnreferencedUnrootedMemory. Stored sparsely (the overwhelming majority are unreferenced) so
        // ObjectStatusComputation can classify them as ReferencedUnrooted vs Unrooted post-build.
        NativeHashSet<SourceIndex> m_ReferencedUnrootedNativeAllocations;
        public bool IsReferencedUnrootedAllocation(SourceIndex index) =>
            m_ReferencedUnrootedNativeAllocations.IsCreated && m_ReferencedUnrootedNativeAllocations.Contains(index);

        /// <summary>
        /// The own-size of a managed type's static fields: committed = captured StaticFieldBytes (or,
        /// on format v20+, the resolved <see cref="TypeDescriptionEntriesCache.StaticFieldSlots"/> total
        /// via <see cref="TypeDescriptionEntriesCache.GetStaticFieldOwnByteSize"/>), and resident deduced
        /// by mapping <see cref="TypeDescriptionEntriesCache.TypeInfoAddress"/> to its SystemMemoryRegion
        /// (via the address-sorted <see cref="CachedSnapshot.SortedSystemMemoryRegions"/>) and querying the
        /// resident-page bitset. When the snapshot has no resident page info, residency is unknown so we
        /// assume fully resident (matching the rest of the tool).
        /// </summary>
        public MemorySize GetStaticFieldOwnSize(CachedSnapshot snapshot, long typeIndex)
        {
            var committed = (ulong)snapshot.TypeDescriptions.GetStaticFieldOwnByteSize(snapshot, typeIndex);
            if (committed == 0)
                return default;
            // No resident-page info: residency unknown, assume resident (committed == resident).
            if (!snapshot.HasSystemMemoryResidentPages)
                return new MemorySize(committed, committed);

            var address = snapshot.TypeDescriptions.TypeInfoAddress[typeIndex];
            // Fallback to fully-resident if the address can't be resolved to a region (not observed in
            // practice — the probe resolved 100% of static-bearing types — but avoids under-counting).
            var resident = committed;
            if (address != 0)
            {
                var sortedRegionIndex = snapshot.SortedSystemMemoryRegions.Find(address, onlyDirectAddressMatches: false);
                if (sortedRegionIndex >= 0)
                {
                    // Find returns an index into the sorted view; map it back to the original region index.
                    var regionIndex = snapshot.SortedSystemMemoryRegions[sortedRegionIndex];
                    resident = snapshot.SystemMemoryResidentPages.CalculateResidentMemory(
                        snapshot, regionIndex, address, committed, SourceIndex.SourceId.ManagedType);
                }
            }
            return new MemorySize(committed, resident);
        }

        // Maps a native-allocation index → the static-field storage (committed+resident) physically
        // contained in it. Static-field storage lives inside native allocations (the "occupied" VM heap;
        // confirmed by the reconciliation diagnostic — 100% of static bytes resolve to a VM-root allocation).
        // Those bytes are attributed as Managed to their ManagedType root, so they must not ALSO count as
        // Native on the allocation. AttributeImpact subtracts this per-allocation (precise + flatten-correct,
        // unlike a bulk subtraction from the VM root node which would leave child allocations over-sized in
        // a flattened view). Lazily built; small (one entry per static-bearing type) and GC-cleaned.
        Dictionary<long, MemorySize> m_StaticBytesPerNativeAllocation;

        /// <summary>
        /// The static-field storage that was ACTUALLY subtracted, per-allocation, from a crawled native
        /// allocation's own-size in <see cref="AttributeImpact"/>. <see cref="DeductStaticFieldNativeRemainder"/>
        /// uses it (against <see cref="StaticBytesMappedToVMRoot"/>) to remove the remainder that the precise
        /// per-allocation deduction couldn't reach.
        /// </summary>
        public MemorySize StaticBytesActuallyDeductedFromNative;

        /// <summary>
        /// Total static-field storage whose containing native allocation is rooted to the VM root. The static
        /// is also attributed as Managed to its ManagedType root, so <see cref="DeductStaticFieldNativeRemainder"/>
        /// removes the part not already removed per-allocation (this minus <see cref="StaticBytesActuallyDeductedFromNative"/>)
        /// from the VM root's managed impact to avoid double-counting.
        /// </summary>
        public MemorySize StaticBytesMappedToVMRoot;

        /// <summary>
        /// Static-field bytes whose storage does NOT resolve to a tracked native allocation but DOES fall
        /// inside an "untracked"-type <see cref="SourceIndex.SourceId.SystemMemoryRegion"/> (the case for
        /// IL2CPP captures that predate routing the VM through Unity's native allocators — the VM, including
        /// static storage, is reported as untracked OS memory). Keyed by the region's display name to match
        /// the model builder's per-name untracked grouping, so the builder can subtract each amount from the
        /// SPECIFIC untracked section it physically sits in (the static is attributed as Managed to its
        /// ManagedType root, so it must be removed from that untracked region to avoid double-counting).
        /// Null when no such static exists (e.g. modern captures, where static resolves to the VM root).
        /// </summary>
        public Dictionary<string, MemorySize> StaticBytesByUntrackedRegionName;

        void EnsureStaticBytesPerNativeAllocation(CachedSnapshot snapshot)
        {
            if (m_StaticBytesPerNativeAllocation != null)
                return;
            m_StaticBytesPerNativeAllocation = new Dictionary<long, MemorySize>();
            var types = snapshot.TypeDescriptions;
            var count = types.Count;
            for (long i = 0; i < count; i++)
            {
                var staticSize = GetStaticFieldOwnSize(snapshot, i);
                if (staticSize.Committed == 0)
                    continue;
                var address = types.TypeInfoAddress[i];
                if (address == 0)
                    continue;
                var sortedIndex = snapshot.SortedNativeAllocations.Find(address, onlyDirectAddressMatches: false);
                if (sortedIndex < 0)
                {
                    // Not in a tracked native allocation. On older captures the VM (and its static storage)
                    // is reported as untracked OS memory (IL2CPP pre-allocator-routing), not a native
                    // allocation. Resolve to the containing SystemMemoryRegion and, if it's an untracked-type
                    // region, record the static against that specific region so the model builder can subtract
                    // it from that exact untracked section.
                    RouteStaticToContainingRegion(snapshot, address, staticSize);
                    continue;
                }
                var allocationIndex = snapshot.SortedNativeAllocations[sortedIndex];
                m_StaticBytesPerNativeAllocation.TryGetValue(allocationIndex, out var existing);
                m_StaticBytesPerNativeAllocation[allocationIndex] = existing + staticSize;

                // Track static that maps to a VM-root allocation: DeductStaticFieldNativeRemainder removes the
                // remainder (static not reached by the precise per-allocation deduction) from the VM root's
                // managed impact, so the same bytes aren't counted as both Managed (on their ManagedType root)
                // and inside the VM allocation.
                var allocRootId = snapshot.NativeAllocations.RootReferenceId[allocationIndex];
                if (snapshot.NativeRootReferences.VMRootReferenceIndex.Valid
                    && snapshot.NativeRootReferences.IdToIndex.TryGetValue(allocRootId, out var nrrIdx)
                    && nrrIdx == snapshot.NativeRootReferences.VMRootReferenceIndex.Index)
                    StaticBytesMappedToVMRoot += staticSize;
            }
        }

        // Resolves a static-field storage address that is NOT inside a tracked native allocation to its
        // containing SystemMemoryRegion. If that region is an "untracked"-type region, accumulates the static
        // against the region's display name (matching the model builder's per-name untracked grouping) so the
        // builder can subtract it from that specific section. Otherwise it is only counted diagnostically.
        void RouteStaticToContainingRegion(CachedSnapshot snapshot, ulong address, MemorySize staticSize)
        {
            var sortedRegion = snapshot.SortedSystemMemoryRegions.Find(address, onlyDirectAddressMatches: false);
            if (sortedRegion < 0)
                return;
            var regionIndex = snapshot.SortedSystemMemoryRegions[sortedRegion];
            var regionSource = new SourceIndex(SourceIndex.SourceId.SystemMemoryRegion, regionIndex);
            switch (snapshot.EntriesMemoryMap.GetPointType(regionSource))
            {
                case EntriesMemoryMapCache.PointType.Untracked:
                case EntriesMemoryMapCache.PointType.Shared:
                case EntriesMemoryMapCache.PointType.Device:
                case EntriesMemoryMapCache.PointType.AndroidRuntime:
                {
                    var name = regionSource.GetName(snapshot);
                    StaticBytesByUntrackedRegionName ??= new Dictionary<string, MemorySize>();
                    StaticBytesByUntrackedRegionName.TryGetValue(name, out var existing);
                    StaticBytesByUntrackedRegionName[name] = existing + staticSize;
                    break;
                }
                default:
                    break;
            }
        }

        CachedProcessingData m_ProcessingData;

        public RootAndImpactInfo(CachedSnapshot snapshot)
        {
            SourceIndexToRootAndImpactInfo = new SourceIndexToRootAndImpactInfoMapper(snapshot);
            var initialCount = SourceIndexToRootAndImpactInfo.TotalCount;

            var shortestPathBaseData = new DynamicArray<ShortestRootPathInfo>(initialCount, initialCount, Allocator.Persistent, true);
            using var offsetsForSteps = SourceIndexToRootAndImpactInfo.GetOffsets<ShortestRootPathInfo>();
            m_ShortestPathInfo = new NestedDynamicArray<ShortestRootPathInfo>(offsetsForSteps, shortestPathBaseData);

            using var offsetsForReferenceInfo = SourceIndexToRootAndImpactInfo.GetOffsets<ReferencesListInfo>();
            var ownedReferences = new DynamicArray<ReferencesListInfo>(initialCount, initialCount, Allocator.Persistent, true);
            m_RootPathOwnedReferencesLookup = new NestedDynamicArray<ReferencesListInfo>(offsetsForReferenceInfo, ownedReferences);

            var unownedReferences = new DynamicArray<ReferencesListInfo>(initialCount, initialCount, Allocator.Persistent, true);
            m_UnownedReferencesLookup = new NestedDynamicArray<ReferencesListInfo>(offsetsForReferenceInfo, unownedReferences);

            using var offsetsForMemoryImpact = SourceIndexToRootAndImpactInfo.GetOffsets<MemoryImpact>();
            var exclusiveImpactData = new DynamicArray<MemoryImpact>(initialCount, initialCount, Allocator.Persistent, true);
            m_Impact = new NestedDynamicArray<MemoryImpact>(offsetsForMemoryImpact, exclusiveImpactData);

            using var offsetsForMarkers = SourceIndexToRootAndImpactInfo.GetOffsets<Marker>();
            var markerInfo = new DynamicArray<Marker>(initialCount, initialCount, Allocator.Persistent, true);
            m_Marker = new NestedDynamicArray<Marker>(offsetsForMarkers, markerInfo);

            m_PersistentSceneObjectsInDelayedDeletionQueue = new NativeHashSet<SourceIndex>(k_PersistentSceneObjectsInDelayedDeletionQueueDefaultCount, Allocator.Persistent);

            m_ReferencedUnrootedNativeAllocations = new NativeHashSet<SourceIndex>(k_ReferencedUnrootedNativeAllocationsDefaultCount, Allocator.Persistent);

            m_BrokenScriptReferenceObjects = new NativeHashSet<SourceIndex>(k_BrokenScriptReferenceObjectsDefaultCount, Allocator.Persistent);

            // Owned reference edges. Measured Count/TotalCount across representative snapshots tops out around
            // 0.70, so reserving the full TotalCount guarantees no grow-and-copy (the over-reservation is
            // modest and this is the larger of the two edge lists, where a late reallocation would be costly).
            m_OwnedChildList = new DynamicArray<SourceIndex>(0, initialCount, Allocator.Persistent, true);

            // Unowned reference edges. Measured Count/TotalCount: ~0.10 on sparse player captures, ~0.33–0.46
            // on dense editor captures. Reserve half of TotalCount so even the densest observed case fits
            // without a reallocation — on huge snapshots (millions of edges) a late grow-and-copy duplicates
            // the whole list and risks a transient 2x spike near the memory ceiling. Over-reserves sparse
            // captures somewhat, an acceptable editor-side cost versus repeated grow-and-copy.
            m_UnownedChildList = new DynamicArray<SourceIndex>(0, initialCount / 2, Allocator.Persistent, true);
        }

        public void Dispose()
        {
            m_ShortestPathInfo.Dispose();
            m_RootPathOwnedReferencesLookup.Dispose();
            m_UnownedReferencesLookup.Dispose();
            m_Impact.Dispose();
            m_PersistentSceneObjectsInDelayedDeletionQueue.Dispose();
            if (m_ReferencedUnrootedNativeAllocations.IsCreated)
                m_ReferencedUnrootedNativeAllocations.Dispose();
            if (m_BrokenScriptReferenceObjects.IsCreated)
                m_BrokenScriptReferenceObjects.Dispose();
            if (m_Marker.IsCreated) // gets disposed after ReadOrProcess is done
                m_Marker.Dispose();
            m_OwnedChildList.Dispose();
            m_UnownedChildList.Dispose();
            if (m_Roots.IsCreated)
                m_Roots.Dispose();

            // if processing was interrupted while suspended, dispose of its data here
            if (m_ProcessingData.ImpactStack.IsCreated)
                m_ProcessingData.Dispose();
        }

        struct CachedProcessingData : IDisposable
        {
            public long Count
            {
                [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
                get
                {
                    if (Stack.Count == 0)
                    {
                        SwapStacks();
                    }
                    return Stack.Count;
                }
            }
            public DynamicArray<ImpactProcessingStackStep> ImpactStack;
            public DynamicArray<SourceIndex> Stack;
            public DynamicArray<SourceIndex> RootsInCurrentOwnershipBucket;
            public DynamicArray<SourceIndex> AlternativeStack;
            public DynamicArray<SourceIndex> OwnedReferences;
            public DynamicArray<SourceIndex> UnownedReferences;
            public DynamicArray<SourceIndex> Roots;
            DynamicArray<long> m_OffsetsForRoots;
            public ConnectionCache Connections;
            public CachedProcessingData(Allocator allocator, long stackSize = 5000)
            {
                ImpactStack = new DynamicArray<ImpactProcessingStackStep>(0, stackSize, allocator);
                Stack = new DynamicArray<SourceIndex>(0, stackSize, allocator);
                RootsInCurrentOwnershipBucket = new DynamicArray<SourceIndex>(0, 2000, allocator);
                AlternativeStack = new DynamicArray<SourceIndex>(0, stackSize, allocator);

                OwnedReferences = new DynamicArray<SourceIndex>(0, 10, allocator);
                UnownedReferences = new DynamicArray<SourceIndex>(0, 10, allocator);
                Roots = new DynamicArray<SourceIndex>(0, 10, allocator);
                m_OffsetsForRoots = new DynamicArray<long>(0, (int)OwnershipBucket.Count + 1, allocator);
                m_OffsetsForRoots.Push(0L);

                Connections = new ConnectionCache();
            }

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            void SwapStacks()
            {
                (Stack, AlternativeStack) = (AlternativeStack, Stack);
            }

            public void Dispose()
            {
                ImpactStack.Dispose();
                Stack.Dispose();
                RootsInCurrentOwnershipBucket.Dispose();
                AlternativeStack.Dispose();
                OwnedReferences.Dispose();
                UnownedReferences.Dispose();
                if (Roots.IsCreated)
                    Roots.Dispose();
                if (m_OffsetsForRoots.IsCreated)
                    m_OffsetsForRoots.Dispose();
                Connections = null;
            }

            public void FinalizeRootBucket(OwnershipBucket bucket)
            {
#if DEBUG_VALIDATION
                Debug.Assert(m_OffsetsForRoots.Count - 1 == (int)bucket, "Finalizing root bucket out of order");
#endif
                unsafe
                {
                    m_OffsetsForRoots.Push(Roots.Count * sizeof(SourceIndex));
                }
            }

            public NestedDynamicArray<SourceIndex> TakeOwnershipOfRoots()
            {
#if DEBUG_VALIDATION
                Debug.Assert(m_OffsetsForRoots.Count - 1 == (int)OwnershipBucket.Count, "Not all root buckets finalized before taking ownership of roots");
#endif
                var result = new NestedDynamicArray<SourceIndex>(m_OffsetsForRoots, Roots);
                // clear out references to avoid double dispose, the caller took over the ownership of this native data.
                Roots = default;
                return result;
            }

            public class ConnectionCache
            {
                public const int DefaultCapacity = 128;
                public const int MaxCapacity = 1024 * 5;
                // we only care about non-duplicated connections and their SourceIndex,
                public HashSet<SourceIndex> References = new(DefaultCapacity);
            }
        }

        public const string ProcessStaticRootsMarkerName = "RootAndImpactInfo: Processing Static Roots";
        static readonly ProfilerMarker k_ProcessStaticRootsMarker = new ProfilerMarker(ProcessStaticRootsMarkerName);
        public const string ProcessNonSceneDontDestroyOnLoadMarkerName = "RootAndImpactInfo: Processing Non-Scene-Objects marked DontDestroyOnLoad";
        static readonly ProfilerMarker k_ProcessNonSceneDontDestroyOnLoadMarker = new ProfilerMarker(ProcessNonSceneDontDestroyOnLoadMarkerName);
        public const string ProcessSceneRootsMarkerName = "RootAndImpactInfo: Processing Scene Roots";
        static readonly ProfilerMarker k_ProcessSceneRootsMarker = new ProfilerMarker(ProcessSceneRootsMarkerName);
        public const string ProcessAssetBundlesMarkerName = "RootAndImpactInfo: Processing Asset Bundles";
        static readonly ProfilerMarker k_ProcessAssetBundlesMarker = new ProfilerMarker(ProcessAssetBundlesMarkerName);
        public const string ProcessNativeObjectRootsMarkerName = "RootAndImpactInfo: Processing Native Object Roots";
        static readonly ProfilerMarker k_ProcessNativeObjectRootsMarker = new ProfilerMarker(ProcessNativeObjectRootsMarkerName);
        public const string ProcessUnreferencedPrefabsMarkerName = "RootAndImpactInfo: Processing Unreferenced Prefabs";
        static readonly ProfilerMarker k_ProcessUnreferencedPrefabsMarker = new ProfilerMarker(ProcessUnreferencedPrefabsMarkerName);
        public const string ProcessGCHandleRootsMarkerName = "RootAndImpactInfo: Non-Unity-Object Referencing GC Handles";
        static readonly ProfilerMarker k_ProcessGCHandleRootsMarker = new ProfilerMarker(ProcessGCHandleRootsMarkerName);
        public const string ProcessNativeRootAllocationsMarkerName = "RootAndImpactInfo: Processing Native Root Allocations";
        static readonly ProfilerMarker k_ProcessNativeRootAllocationsMarker = new ProfilerMarker(ProcessNativeRootAllocationsMarkerName);
        public const string ValidatingAndFinalizingMarkerName = "RootAndImpactInfo: Validating and Finalizing";
        static readonly ProfilerMarker k_ValidatingAndFinalizingMarker = new ProfilerMarker(ValidatingAndFinalizingMarkerName);

        public IEnumerator<EnumerationStatus> ReadOrProcess(CachedSnapshot snapshot, EnumerationStatus status)
        {
            if (m_Processed || !snapshot.HasSceneRootsAndAssetbundles)
            {
                for (int i = 0; i < ProcessStepCount; i++)
                {
                    status.IncrementStep(
                        m_Processed
                        ? "Skipping already Processed Paths From Root data" : "Finalizing");
                }
                m_Marker.Dispose();
                m_Processed = true;
                yield break;
            }

            yield return status.IncrementStep("Process Paths From Root: Statically rooted");

            m_ProcessingData = new CachedProcessingData(Allocator.Persistent);
            try
            {
                using var _ = k_ProcessStaticRootsMarker.Auto();
                PreprocessAssetBundles(snapshot);
                CrawlStaticRoots(snapshot, ref m_ProcessingData);
            }
            catch
            {
                m_ProcessingData.Dispose();
                throw;
            }

            yield return status.IncrementStep("Process Paths From Root: DontDestroyOnLoad roots");

            try
            {
                using var _ = k_ProcessNonSceneDontDestroyOnLoadMarker.Auto();
                CrawlNonSceneDontDestroyOnLoadObjects(snapshot, ref m_ProcessingData);
            }
            catch
            {
                m_ProcessingData.Dispose();
                throw;
            }

            yield return status.IncrementStep("Process Paths From Root: Scene Roots");

            try
            {
                using var _ = k_ProcessSceneRootsMarker.Auto();
                CrawlSceneRootsAndDontDestroyOnLoadScene(snapshot, ref m_ProcessingData);
            }
            catch
            {
                m_ProcessingData.Dispose();
                throw;
            }

            yield return status.IncrementStep("Process Paths From Root: Asset Bundles");

            try
            {
                using var _ = k_ProcessAssetBundlesMarker.Auto();
                CrawlAssetBundles(snapshot, ref m_ProcessingData);
            }
            catch
            {
                m_ProcessingData.Dispose();
                throw;
            }

            yield return status.IncrementStep("Process Paths From Root: Native Object Roots");

            try
            {
                using var _ = k_ProcessNativeObjectRootsMarker.Auto();
                CrawlNativeObjectRoots(snapshot, ref m_ProcessingData);
            }
            catch
            {
                m_ProcessingData.Dispose();
                throw;
            }

            yield return status.IncrementStep("Process Paths From Root: Unreferenced Prefabs");

            try
            {
                using var _ = k_ProcessUnreferencedPrefabsMarker.Auto();
                CrawlUnreferencedPrefabs(snapshot, ref m_ProcessingData);
            }
            catch
            {
                m_ProcessingData.Dispose();
                throw;
            }

            yield return status.IncrementStep("Process Paths From Root: Loose GC Handles");

            try
            {
                using var _ = k_ProcessGCHandleRootsMarker.Auto();
                CrawlGCHandleRoots(snapshot, ref m_ProcessingData);
            }
            catch
            {
                m_ProcessingData.Dispose();
                throw;
            }

            yield return status.IncrementStep("Process Paths From Root: Native Root Allocations");

            try
            {
                using var _ = k_ProcessNativeRootAllocationsMarker.Auto();
                CrawlNativeRootAllocations(snapshot, ref m_ProcessingData);
                CrawlBrokenScriptReferenceObjects(snapshot, ref m_ProcessingData);
            }
            finally
            {
                m_Roots = m_ProcessingData.TakeOwnershipOfRoots();
                m_ProcessingData.Dispose();
            }

            yield return status.IncrementStep("Process Paths From Root: Validating and Finalizing");

            using var finalizingProfilingScope = k_ValidatingAndFinalizingMarker.Auto();
            RootNativeObjectAssociatedAllocations(snapshot);
            HandleUnreferenceUnrootedAllocations(snapshot);
            Validate(snapshot);
            DeductStaticFieldNativeRemainder(snapshot);
            // dispose of the marker array data here as we are done processing and have no further use for it.
            // If processing failed it will get cleaned up on Dispose instead.
            m_Marker.Dispose();
            m_Processed = true;
        }

        void CrawlStaticRoots(CachedSnapshot snapshot, ref CachedProcessingData data)
        {
            ref readonly var rootStepsTypes = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_ShortestPathInfo, SourceIndex.SourceId.ManagedType);

            var types = snapshot.TypeDescriptions;

            var count = types.Count;
            for (int i = 0; i < count; i++)
            {
                // Use HasStaticFieldData (static byte data present), not HasAnyStaticField (static
                // field-index info present): a type can have captured static bytes without field-index
                // info, and AttributeImpact sizes by StaticFieldBytes — so gating on field indices
                // would drop those bytes from attribution. Matches ManagedDataCrawler's predicate.
                // Format v20+ never populates StaticFieldBytes - HasStaticFieldSlotData is the
                // equivalent presence check against the resolved-address TypeDescriptions_StaticFieldSlots.
                if (types.HasStaticFieldData(i) || types.HasStaticFieldSlotData(snapshot, i))
                {
                    var rootSource = new SourceIndex(SourceIndex.SourceId.ManagedType, i);
                    rootStepsTypes[i] = new ShortestRootPathInfo(root: rootSource, parent: default, depth: 0);
                    data.RootsInCurrentOwnershipBucket.Push(rootSource);
                }
            }
            Crawl(snapshot, ref data, OwnershipBucket.StaticRoots);
        }

        /// <summary>
        /// Leaving DontDestroyOnLoad scene objects for the <seealso cref="CrawlSceneRootsAndDontDestroyOnLoadScene(CachedSnapshot, ref CachedProcessingData)"/> step.
        /// </summary>
        /// <param name="snapshot"></param>
        /// <param name="data"></param>
        void CrawlNonSceneDontDestroyOnLoadObjects(CachedSnapshot snapshot, ref CachedProcessingData data)
        {
            ref readonly var markerNativeObjects = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_Marker, SourceIndex.SourceId.NativeObject);
            var nativeObjects = snapshot.NativeObjects;
            var unifiedNativeTypes = snapshot.TypeDescriptions.UnifiedTypeInfoNative;
            using var prefabComponentsInDeletionQueue = new DynamicArray<SourceIndex>(0, 100, Allocator.Temp);
            // Collected only - SeedAndClaimOwnershipBucket decides which of these are genuine roots vs.
            // children of a same-bucket sibling, so none of them is marked FoundForRoot here.
            using var candidates = new DynamicArray<SourceIndex>(0, 2000, Allocator.Temp);
            // Dedups multiple DontDestroyOnLoad scene-object components that redirect to the same prefab
            // within this pass - distinct from FoundForRoot, which here only means "rooted by an earlier phase".
            using var collectedPrefabsThisPass = new NativeHashSet<SourceIndex>(16, Allocator.Temp);

            var count = nativeObjects.Count;
            for (long i = 0; i < count; i++)
            {
                // only parsing DontDestroyOnLoad flagged objects not yet found via static roots
                if (nativeObjects.Flags[i].HasFlag(ObjectFlags.IsDontDestroyOnLoad) && !markerNativeObjects[i].FoundForRoot)
                // The ObjectFlag is set depending on the HideFlags during the native object reporting for the memory capture.
                // The ObjectFlags.IsDontDestroyOnLoad includes HideFlags.DontUnloadUnusedAsset as well as the native only HideFlags.DontAllowDestruction
                // which is used to mark Scene Objects for internal use, such as "InternalIdentityTransform".
                // Checking for the hideflag itself is therefore superfluose here.
                //|| nativeObjects.HideFlags[i].HasFlag(HideFlags.DontUnloadUnusedAsset)
                {
                    var dontDestroyOnLoadObjectIndex = new SourceIndex(SourceIndex.SourceId.NativeObject, i);
                    ref readonly var typeInfo = ref unifiedNativeTypes[nativeObjects.NativeTypeArrayIndex[i]];
                    if (typeInfo.IsSceneObjectType)
                    {
                        if (nativeObjects.Flags[i].HasFlag(ObjectFlags.IsPersistent))
                        {
                            // We found a DontDestroyOnLoad marked prefab, or one of its components
                            // To avoid randomizing which part of the GameObject or its components we consider as the root,
                            // find the relevant Prefab index for it and only set that as the root.

                            // Note: marking anything but the root of the prefab as DontDestroy is kind of the wrong way to handle it,
                            // and the HideFlag does not get propagated past the GameObject and component that was marked by it, but it is technically possible.

                            var prefabIndex = GetPrefabIndexForNativeSceneObject(snapshot, dontDestroyOnLoadObjectIndex, in typeInfo);
                            if (!prefabIndex.Valid)
                            {
                                prefabComponentsInDeletionQueue.Push(dontDestroyOnLoadObjectIndex);
                                m_PersistentSceneObjectsInDelayedDeletionQueue.Add(dontDestroyOnLoadObjectIndex);
                                continue;
                            }
                            ref var prefabMarker = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, prefabIndex);
                            // Skip if rooted by an earlier phase, or if a previous component of this same prefab
                            // was already collected as a candidate in this pass (so we don't parse it multiple times).
                            if (prefabMarker.FoundForRoot || !collectedPrefabsThisPass.Add(prefabIndex))
                                continue;
                            // process the prefab itself, not one of its Scene Objects
                            dontDestroyOnLoadObjectIndex = prefabIndex;
                        }
                        else
                        {
                            // Ignore Scene Objects (unless they are prefabs, aka persistent) as they will be part of the DontDestroyOnLoadScene

                            // It shouldn't usually happen that a SceneObject would have the HideFlags.DontUnloadUnusedAsset set leading to it being
                            // reported with ObjectFlags.IsDontDestroyOnLoad. Instead of setting a hideflag on scene objects, users should marked them
                            // via Object.DontDestroyOnLoad, which would move it into the DontDestroyOnLoadScene, without setting the HideFlag.
                            // The Unity API however technically allows tagging GameObjects or components with the HideFlag, which then does not move them
                            // into the DontDestroyOnLoadScene.

                            // There is at least one known case where this can happen though: the GameObject "InternalIdentityTransform" created by the native code for the Renderer class,
                            // though it should be using DontAllowDestructionsince 2021.3.
                            // It and some other internal objects are also set to HideAndDontSave. If the HideAndDontSave flag is missing and the flag used to mark this
                            // object as not-to-be destroyed was DontUnloadUnusedAsset instead of DontAllowDestruction, it should be skipped as a root and potentially
                            // causing a warning to be logged.
                            if (NativeObjectEntriesCache.IsInvalidHideFlagUsageForNonPersistentSceneObjects(nativeObjects.HideFlags[i]))
                            {
                                if (nativeObjects.NativeTypeArrayIndex[i] == snapshot.NativeTypes.GameObjectIdx) // Only log for GameObjects to avoid logging repeat messages for each component
                                    Debug.LogWarningFormat(TextContent.NonpersistentSceneObjectWithInvalidDontDestroyOnLoadHideFlagWarningFormatString, "GameObject", nativeObjects.ObjectName[i]);

                                // Marking a non-prefab Scene Object as unloadable via the HideFlag doesn't keep it from getting unloaded with the scene,
                                // as that hideflag is not checked on scene objects during a scene unload, so for rooting purposes it doesn't matter for these
                                // to fall through the cracks here.
                                continue;
                            }
                        }
                    }
                    candidates.Push(dontDestroyOnLoadObjectIndex);
                }
            }
            SeedAndClaimOwnershipBucket(snapshot, ref data, OwnershipBucket.DontDestroyOnLoadObjects, candidates);

            foreach (var objectInDeletionQueue in prefabComponentsInDeletionQueue)
            {
                ref var marker = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, objectInDeletionQueue);
                if (marker.FoundForRoot)
                    continue;
                ref var rootStep = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_ShortestPathInfo, objectInDeletionQueue);
                rootStep = new ShortestRootPathInfo(root: objectInDeletionQueue, parent: default, 0);
                data.RootsInCurrentOwnershipBucket.Push(objectInDeletionQueue);
                marker.FoundForRoot = true;
            }
            Crawl(snapshot, ref data, OwnershipBucket.DontDestroyOnLoadObjectsInDeletionQueue);
        }

        void CrawlSceneRootsAndDontDestroyOnLoadScene(CachedSnapshot snapshot, ref CachedProcessingData data)
        {
            ref readonly var markerScenes = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_Marker, SourceIndex.SourceId.Scene);

            ref readonly var rootStepsScenes = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_ShortestPathInfo, SourceIndex.SourceId.Scene);

            var dontDestroyOnLoadSceneIndex = snapshot.SceneRoots.DontDestroyOnLoadSceneIndex;
            if (dontDestroyOnLoadSceneIndex >= 0)
            {
                var scene = new SourceIndex(SourceIndex.SourceId.Scene, dontDestroyOnLoadSceneIndex);
                // mark the DontDestroyOnLoad scene as found
                markerScenes[scene.Index].FoundForRoot = true;
                rootStepsScenes[scene.Index] = new ShortestRootPathInfo(root: scene, parent: default, depth: 0);
                data.RootsInCurrentOwnershipBucket.Push(scene);

                // Crawl DontDestroyOnLoad Scene first
                Crawl(snapshot, ref data, OwnershipBucket.DontDestroyOnLoadScene);
            }
            else
            {
                data.FinalizeRootBucket(OwnershipBucket.DontDestroyOnLoadScene);
            }
            // Crawl other scenes but also ignore the fake prefab scene (at rootTransformsByScene[Count])
            var count = snapshot.SceneRoots.SceneCount;
            for (int sceneIndex = 0; sceneIndex < count; sceneIndex++)
            {
                if (sceneIndex != dontDestroyOnLoadSceneIndex)
                {
                    var scene = new SourceIndex(SourceIndex.SourceId.Scene, sceneIndex);
                    // mark the DontDestroyOnLoad scene as found
                    markerScenes[scene.Index].FoundForRoot = true;
                    rootStepsScenes[scene.Index] = new ShortestRootPathInfo(root: scene, parent: default, depth: 0);
                    data.RootsInCurrentOwnershipBucket.Push(scene);
                }
            }
            // Crawl remaining scenes
            Crawl(snapshot, ref data, OwnershipBucket.SceneRoots);
        }

        static readonly bool k_ShouldRootAssetBundles = false;
        static readonly bool k_AssetBundlesOnlyOwnUnreferencedContents = true;
        void PreprocessAssetBundles(CachedSnapshot snapshot)
        {
            if (k_ShouldRootAssetBundles)
                // let them be rooted by other roots
                return;
            ref readonly var markerNativeObjects = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_Marker, SourceIndex.SourceId.NativeObject);
            foreach (var assetBundle in snapshot.NativeObjects.AssetBundles)
            {
                ref var marker = ref markerNativeObjects[assetBundle.Index];
                // Marking them as found for root and impact to avoid means that any asset bundle loading
                // managers that might be referencing them won't be treated as the rooting owners for them
                // and won't get their full contents attributed to them.
                marker.FoundForRoot = true;
                marker.FoundForImpact = true;
                // If we don't mark them as fully distributed here, their contents will get pro rata attributed
                // to the asset bundle and any direct links to the contained assets.
                // If we do mark them as fully distributed, only assets not otherwise referenced will be attributed to the asset bundle.
                marker.FullyDistributedImpact = k_AssetBundlesOnlyOwnUnreferencedContents;
            }
        }

        void CrawlAssetBundles(CachedSnapshot snapshot, ref CachedProcessingData data)
        {
            ref readonly var markerNativeObjects = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_Marker, SourceIndex.SourceId.NativeObject);
            ref readonly var rootStepsNativeObject = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_ShortestPathInfo, SourceIndex.SourceId.NativeObject);

            // Note: with k_ShouldRootAssetBundles == true, AssetBundles that are referenced directly by a script would've already been found.
            // That would attribute their sizes to whoever is managing the asset bundles, which is not be how we want to present this currently, hence k_ShouldRootAssetBundles == false.

            // The solution chosen here for that scenario was to mark all AssetBundles as "found" before crawling anything else and
            // then crawling them here regardless of that flag.
            // That leaves the AssetBundle roots to be floating on their own and owning any assets in them that are not otherwise used.
            // The impact for their assets that ARE used would be shared with all rooting references to them
            // (if k_AssetBundlesOnlyOwnUnreferencedContents is false, which it currently isn't),
            // UNLESS their root within the AssetBundle was not found in the same root bucket.

            foreach (var assetBundle in snapshot.NativeObjects.AssetBundles)
            {
                if (!k_ShouldRootAssetBundles || !markerNativeObjects[assetBundle.Index].FoundForRoot)
                {
                    rootStepsNativeObject[assetBundle.Index] = new ShortestRootPathInfo(root: assetBundle, parent: default, 0);
                    data.RootsInCurrentOwnershipBucket.Push(assetBundle);
                    ref var marker = ref markerNativeObjects[assetBundle.Index];
                    // Mark as found for root to avoid the unlikely scenario of them getting referenced
                    // by the content of another asset bundle.
                    marker.FoundForRoot = true;
                    // But importantly, we do uncheck them as found for impact here, so that their otherwise unreferenced
                    // content will get attributed to them.
                    marker.FoundForImpact = false;
                    if (k_AssetBundlesOnlyOwnUnreferencedContents)
                        marker.FullyDistributedImpact = false;
                }
            }
            Crawl(snapshot, ref data, OwnershipBucket.AssetBundles);
        }

        void CrawlNativeObjectRoots(CachedSnapshot snapshot, ref CachedProcessingData data)
        {
            ref readonly var markerNativeObjects = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_Marker, SourceIndex.SourceId.NativeObject);

            // Collected only - SeedAndClaimOwnershipBucket decides which of these are genuine roots vs.
            // children of a same-bucket sibling, so none of them is marked FoundForRoot here.
            using var candidates = new DynamicArray<SourceIndex>(0, 2000, Allocator.Temp);

            foreach (var nativeRoot in snapshot.ProcessedNativeRoots.Data)
            {
                var index = nativeRoot.NativeObjectOrRootIndex.Index;
                switch (nativeRoot.NativeObjectOrRootIndex.Id)
                {
                    case SourceIndex.SourceId.NativeObject:
                        // Ignore those already found
                        if (!markerNativeObjects[index].FoundForRoot)
                        {
                            // ignore persistent scene objects as they are handled below after all other assets have been processed
                            // splitting the processing up this way ensures that prefabs referenced by other assets (e.g. ScriptableObjects)
                            // are rooted to them and not treated as floating prefabs.
                            ref readonly var typeInfo = ref snapshot.TypeDescriptions.UnifiedTypeInfoNative[snapshot.NativeObjects.NativeTypeArrayIndex[index]];
                            if (typeInfo.IsSceneObjectType && snapshot.NativeObjects.Flags[index].HasFlag(ObjectFlags.IsPersistent))
                                continue;

                            candidates.Push(nativeRoot.NativeObjectOrRootIndex);

                            // TODO: Move this to Native Object processing on attributing sizes, or delete it if it's not needed there.
                            //if (snapshot.NativeRootReferences.Count > 0)
                            //{
                            //    var rootId = snapshot.NativeObjects.RootReferenceId[index];
                            //    var rootIndex = snapshot.NativeRootReferences.IdToIndex[rootId];
                            //    markerNativeRoots[rootIndex].Found = true;
                            //    rootStepsNativeRoots[rootIndex] = new PathToRootStep(nativeRoot.NativeObjectOrRootIndex, nativeRoot.NativeObjectOrRootIndex, 1);
                            //}
                        }
                        break;
                    case SourceIndex.SourceId.NativeRootReference:
                        // Ignore these until later as Prefab roots or GCHandle roots might lead to Native Allocations that they then own.
                        break;
                    case SourceIndex.SourceId.None:
                        break;
                    default:
                        throw new NotImplementedException($"{nativeRoot.NativeObjectOrRootIndex.Id}");
                }
            }
            SeedAndClaimOwnershipBucket(snapshot, ref data, OwnershipBucket.NativeObjectRoots, candidates);
        }

        void CrawlUnreferencedPrefabs(CachedSnapshot snapshot, ref CachedProcessingData data)
        {
            ref readonly var markerPrefabs = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_Marker, SourceIndex.SourceId.Prefab);
            ref readonly var rootStepsPrefabs = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_ShortestPathInfo, SourceIndex.SourceId.Prefab);
            // Now process any prefabs not yet found as they were unused and unreferenced assets
            var count = snapshot.SceneRoots.PrefabRootCount;
            for (int i = 0; i < count; i++)
            {
                var prefabIndex = new SourceIndex(SourceIndex.SourceId.Prefab, i);
                if (!markerPrefabs[prefabIndex.Index].FoundForRoot)
                {
                    markerPrefabs[prefabIndex.Index].FoundForRoot = true;
                    rootStepsPrefabs[prefabIndex.Index] = new ShortestRootPathInfo(root: prefabIndex, parent: default, depth: 0);
                    data.RootsInCurrentOwnershipBucket.Push(prefabIndex);
                }
            }
            Crawl(snapshot, ref data, OwnershipBucket.UnreferencedPrefabs);
        }

        void CrawlGCHandleRoots(CachedSnapshot snapshot, ref CachedProcessingData data)
        {
            ref readonly var markerGCHandles = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_Marker, SourceIndex.SourceId.GCHandleIndex);
            ref readonly var markerManagedObjects = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_Marker, SourceIndex.SourceId.ManagedObject);

            var count = snapshot.GcHandles.UniqueCount;
            var crawled = snapshot.CrawledData.Crawled;

            // Collected only - SeedAndClaimGCHandleOwnershipBucket decides which of these are genuine
            // roots vs. children of a same-bucket sibling's managed object graph, so none of them is
            // marked FoundForRoot here.
            using var candidateHandles = new DynamicArray<SourceIndex>(0, 2000, Allocator.Temp);
            using var candidateTargets = new NativeHashSet<SourceIndex>(2000, Allocator.Temp);

            for (int i = 0; i < count; i++)
            {
                // GCHandle index maps to the managed object index it targets — see ManagedData.
                // ResolveManagedObjectIndexForGCHandle for why this isn't always index i itself.
                var managedObjectIndex = snapshot.CrawledData.ResolveManagedObjectIndexForGCHandle(i);
                if (managedObjectIndex == ManagedData.InvalidObjectIndex)
                    continue; // handle target has no ManagedObjectInfo row (e.g. collected mid-capture)

                var managedTarget = new SourceIndex(SourceIndex.SourceId.ManagedObject, managedObjectIndex);
                // If the snapshot is not crawled, the managed objects data is not populated, even for the
                // GCHandle held ones, so this is important to check before indexing into their marker or
                // root path data. candidateTargets.Add failing means another handle already iterated in
                // this same sweep resolves to the identical managed object.
                if (!crawled || markerManagedObjects[managedObjectIndex].FoundForRoot || !candidateTargets.Add(managedTarget))
                {
                    // The handle's target object is already rooted (elsewhere, or by another handle in
                    // this same sweep pointing at the same object), so this GCHandle is NOT a rooting
                    // owner; mark it done without processing. It will never receive impact: AttributeImpact
                    // strikes GCHandles from any referee list that also contains a real (non-GCHandle) owner,
                    // so a co-referencing GCHandle never gets a pro-rata share that would strand here.
                    ref var gcHandleMarker = ref markerGCHandles[i];
                    gcHandleMarker.FoundForImpact = true;
                    gcHandleMarker.FullyDistributedImpact = true;
                    continue;
                }

                candidateHandles.Push(new SourceIndex(SourceIndex.SourceId.GCHandleIndex, i));
            }
            SeedAndClaimGCHandleOwnershipBucket(snapshot, ref data, candidateHandles, candidateTargets);
        }

        /// <summary>
        /// GCHandle counterpart of <see cref="SeedAndClaimOwnershipBucket"/>: computes which GCHandle
        /// targets in this same-bucket sweep have no in-bucket owner in the crawled managed reference
        /// graph before marking any of them FoundForRoot, so a same-bucket sibling's managed object graph
        /// can still claim a GCHandle's target as its child.
        /// </summary>
        /// <param name="candidateHandlesInDiscoveryOrder">Must not yet be marked FoundForRoot by the caller.</param>
        void SeedAndClaimGCHandleOwnershipBucket(CachedSnapshot snapshot, ref CachedProcessingData data,
            in DynamicArray<SourceIndex> candidateHandlesInDiscoveryOrder, in NativeHashSet<SourceIndex> candidateTargets)
        {
            ref readonly var markerGCHandles = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_Marker, SourceIndex.SourceId.GCHandleIndex);
            ref readonly var markerManagedObjects = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_Marker, SourceIndex.SourceId.ManagedObject);
            ref readonly var rootStepsGCHandles = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_ShortestPathInfo, SourceIndex.SourceId.GCHandleIndex);

            var seedStart = data.RootsInCurrentOwnershipBucket.Count;
            foreach (var gcHandleIndex in candidateHandlesInDiscoveryOrder)
            {
                var managedTarget = new SourceIndex(SourceIndex.SourceId.ManagedObject, snapshot.CrawledData.ResolveManagedObjectIndexForGCHandle(gcHandleIndex.Index));
                if (HasInBucketManagedOwner(snapshot, managedTarget, candidateTargets))
                    continue;

                markerManagedObjects[managedTarget.Index].FoundForRoot = true;
                rootStepsGCHandles[gcHandleIndex.Index] = new ShortestRootPathInfo(root: gcHandleIndex, parent: default, depth: 0);
                data.RootsInCurrentOwnershipBucket.Push(gcHandleIndex);
                markerGCHandles[gcHandleIndex.Index].FoundForRoot = true;
            }
            BuildPathFromRootInfo(snapshot, ref data, OwnershipBucket.GCHandleRoots, seedStart);

            // A candidate skipped above either had its target claimed by the walk from a sibling's root
            // (a real owner, exactly like a handle whose target was already rooted before this sweep -
            // see the identical handling in CrawlGCHandleRoots), or its target is still unclaimed because
            // it sits on a cycle entirely inside this bucket's induced managed reference graph. Break
            // each such cycle deterministically: the first remaining candidate in discovery order becomes
            // that cycle's root. Each round claims at least one node, so this always terminates.
            foreach (var gcHandleIndex in candidateHandlesInDiscoveryOrder)
            {
                if (markerGCHandles[gcHandleIndex.Index].FoundForRoot)
                    continue;

                var managedTarget = new SourceIndex(SourceIndex.SourceId.ManagedObject, snapshot.CrawledData.ResolveManagedObjectIndexForGCHandle(gcHandleIndex.Index));
                if (markerManagedObjects[managedTarget.Index].FoundForRoot)
                {
                    ref var gcHandleMarker = ref markerGCHandles[gcHandleIndex.Index];
                    gcHandleMarker.FoundForImpact = true;
                    gcHandleMarker.FullyDistributedImpact = true;
                    continue;
                }

                var tailStart = data.RootsInCurrentOwnershipBucket.Count;
                markerManagedObjects[managedTarget.Index].FoundForRoot = true;
                rootStepsGCHandles[gcHandleIndex.Index] = new ShortestRootPathInfo(root: gcHandleIndex, parent: default, depth: 0);
                data.RootsInCurrentOwnershipBucket.Push(gcHandleIndex);
                markerGCHandles[gcHandleIndex.Index].FoundForRoot = true;
                BuildPathFromRootInfo(snapshot, ref data, OwnershipBucket.GCHandleRoots, tailStart);
            }

#if DEBUG_VALIDATION
            foreach (var gcHandleIndex in candidateHandlesInDiscoveryOrder)
            {
                var managedTarget = new SourceIndex(SourceIndex.SourceId.ManagedObject, snapshot.CrawledData.ResolveManagedObjectIndexForGCHandle(gcHandleIndex.Index));
                Debug.Assert(markerManagedObjects[managedTarget.Index].FoundForRoot,
                    $"{managedTarget} (target of {gcHandleIndex}) was neither seeded nor claimed while seeding the GCHandle ownership bucket.");
            }
#endif

            FinishCrawl(snapshot, ref data, OwnershipBucket.GCHandleRoots);
        }

        /// <summary>
        /// True if some other candidate's target in the same GCHandle-roots sweep would claim
        /// <paramref name="managedTarget"/> as a child via a genuine field reference, i.e. it has
        /// in-degree > 0 in the induced subgraph over <paramref name="candidateTargets"/> within the
        /// crawled managed reference graph. Managed-object counterpart to <see cref="HasInBucketOwner"/>.
        /// </summary>
        bool HasInBucketManagedOwner(CachedSnapshot snapshot, in SourceIndex managedTarget, in NativeHashSet<SourceIndex> candidateTargets)
        {
            if (!snapshot.CrawledData.ConnectionsToMappedToSourceIndex.TryGetValue(managedTarget, out var connectionIndices))
                return false;

            foreach (var connectionIndex in connectionIndices)
            {
                var referrer = snapshot.CrawledData.Connections[connectionIndex].IndexFrom;
                if (!referrer.Equals(managedTarget) && candidateTargets.Contains(referrer))
                    return true;
            }
            return false;
        }

        void CrawlNativeRootAllocations(CachedSnapshot snapshot, ref CachedProcessingData data)
        {
            ref readonly var markerNativeRoots = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_Marker, SourceIndex.SourceId.NativeRootReference);

            ref readonly var rootStepsNativeRoots = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_ShortestPathInfo, SourceIndex.SourceId.NativeRootReference);

            foreach (var nativeRoot in snapshot.ProcessedNativeRoots.Data)
            {
                var index = nativeRoot.NativeObjectOrRootIndex.Index;
                switch (nativeRoot.NativeObjectOrRootIndex.Id)
                {
                    case SourceIndex.SourceId.NativeObject:
                        break;
                    case SourceIndex.SourceId.NativeRootReference:
                        markerNativeRoots[index].FoundForRoot = true;
                        rootStepsNativeRoots[index] = new ShortestRootPathInfo(nativeRoot.NativeObjectOrRootIndex, default, 0);
                        data.RootsInCurrentOwnershipBucket.Push(nativeRoot.NativeObjectOrRootIndex);
                        break;
                    case SourceIndex.SourceId.None:
                        break;
                    default:
                        throw new NotImplementedException($"{nativeRoot.NativeObjectOrRootIndex.Id}");
                }
            }
            Crawl(snapshot, ref data, OwnershipBucket.NativeRootAllocations);
        }

        /// <summary>
        /// Collects what should be the only unrooted native objects that remain, and therefore
        /// end up in the <see cref="OwnershipBucket.BrokenScriptReferences"/> bucket:
        /// native MonoBehaviour/ScriptableObject instances that are unreferenced and unrooted because their backing
        /// <c>MonoScript</c> is missing (see <see cref="UnreferencedNativeObjectIsBrokenScriptingTypeObject"/>). They
        /// have no real root, so they are rooted to themselves here to surface them as the actionable leak they are.
        /// </summary>
        void CrawlBrokenScriptReferenceObjects(CachedSnapshot snapshot, ref CachedProcessingData data)
        {
            ref readonly var rootStepInfoForNativeObjects = ref
                SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_ShortestPathInfo,
                    SourceIndex.SourceId.NativeObject);
            var nativeObjectIndex = 0L;
            foreach (var rootStep in rootStepInfoForNativeObjects)
            {
                if (!rootStep.Valid)
                {
                    if (UnreferencedNativeObjectIsBrokenScriptingTypeObject(snapshot, nativeObjectIndex))
                    {
                        var sourceIndex = new SourceIndex(SourceIndex.SourceId.NativeObject, nativeObjectIndex);
                        rootStepInfoForNativeObjects[nativeObjectIndex] =
                            new ShortestRootPathInfo(sourceIndex, default, 0);
                        data.RootsInCurrentOwnershipBucket.Push(sourceIndex);
                        m_BrokenScriptReferenceObjects.Add(sourceIndex);
                    }
                }
                ++nativeObjectIndex;
            }
            Crawl(snapshot, ref data, OwnershipBucket.BrokenScriptReferences);
        }

        /// <summary>
        /// Static-field bytes are attributed as Managed to their ManagedType roots, but the same bytes also
        /// physically reside in native memory: on modern captures inside the VM root's native allocations, on
        /// older captures in Untracked system regions (IL2CPP without Unity's allocator) or the managed VM heap
        /// (Mono). The per-allocation deduction in <see cref="AttributeImpact"/> removes the part that resolves
        /// to a CRAWLED native allocation (<see cref="StaticBytesActuallyDeductedFromNative"/>), which keeps the
        /// flattened per-allocation view correct, but <see cref="TypeInfoAddress"/> usually points at the small
        /// type-info struct rather than the (larger) static storage, so most static is left double-counted.
        /// This removes the REMAINDER in bulk from the native side so the table reconciles:
        ///  • VM-root remainder → subtracted from the VM root's native impact (modern).
        ///  • Untracked / managed-heap remainders are exposed for the model builder to subtract from those
        ///    region nodes (older captures), since those are not part of the ownership tree.
        /// </summary>
        void DeductStaticFieldNativeRemainder(CachedSnapshot snapshot)
        {
            var vmRoot = snapshot.NativeRootReferences.VMRootReferenceIndex;
            if (vmRoot.Valid && StaticBytesMappedToVMRoot.Committed > StaticBytesActuallyDeductedFromNative.Committed)
            {
                var remainder = StaticBytesMappedToVMRoot - StaticBytesActuallyDeductedFromNative;
                ref var vmImpact = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Impact, vmRoot);
                // VM-root allocations are reported as Managed (see the NativeAllocation case in
                // AttributeImpact), so they bubble into the VM root's Exclusive.ManagedSize. The static
                // remainder this removes (static double-counted as Managed on its ManagedType root) must
                // therefore be subtracted from the managed side, not the native side.
                var deduct = MemorySize.Min(remainder, vmImpact.Exclusive.ManagedSize);
                vmImpact.Exclusive.ManagedSize -= deduct;
            }
        }

        void HandleUnreferenceUnrootedAllocations(CachedSnapshot snapshot)
        {
            ref readonly var markerNativeAllocations = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_Marker, SourceIndex.SourceId.NativeAllocation);

            // A native allocation that spans multiple system memory regions is reported as multiple flat
            // entries, so ProcessNativeAllocation pushes its index to UnrootedNativeAllocationIndices once
            // PER REGION (and accumulates the per-region portions into AllocationSizes[i], which therefore
            // already holds the allocation's FULL size). We must add that full size exactly ONCE per
            // allocation here; counting it per-pushed-entry over-reports multi-region unrooted allocations
            // N-fold (a ~8.5 MB over-count on large snapshots, enough to make UnreferencedUnrootedMemory
            // exceed ProcessedNativeRoots.UnrootedMemory — the true total of ALL unrooted allocations).
            // Dedup by allocation index. (The referenced-unrooted set below is already idempotent, but
            // guarding the whole loop keeps both branches correct and cheap.)
            var countedAllocations = new HashSet<long>();
            foreach (var unrootedAllocation in snapshot.ProcessedNativeRoots.UnrootedNativeAllocationIndices)
            {
                if (!countedAllocations.Add(unrootedAllocation.Index))
                    continue;
                if (!markerNativeAllocations[unrootedAllocation.Index].FoundForRoot)
                {
                    UnreferencedUnrootedMemory.NativeSize += snapshot.ProcessedNativeRoots.AllocationSizes[unrootedAllocation.Index];
                    // This happens as a last step and unrooted allocations are ignored in Validate() so their roots staying invalid
                    // and their markers unset is fine, no need to incure extra computations for this.
                }
                else
                {
                    // Reached via a reference during the crawl — record it so ObjectStatusComputation
                    // can distinguish ReferencedUnrooted from Unrooted (the unreferenced majority).
                    m_ReferencedUnrootedNativeAllocations.Add(unrootedAllocation);
                }
            }
        }

        internal static bool UnreferencedNativeObjectIsBrokenScriptingTypeObject(CachedSnapshot snapshot, long nativeObjectIndex)
        {
            if (nativeObjectIndex is NativeObjectEntriesCache.InvalidObjectIndex)
                return false;
            var typeIndex = snapshot.NativeObjects.NativeTypeArrayIndex[nativeObjectIndex];
            var typeInfo = snapshot.TypeDescriptions.UnifiedTypeInfoNative[typeIndex];
            if (typeInfo.IsMonoBehaviourType
                || typeIndex == snapshot.NativeTypes.ScriptableObjectIdx
                || typeIndex == snapshot.NativeTypes.EditorScriptableObjectIdx)
            {
                // this is very likely a broken script reference, i.e. a scriptable type that no longer maps to a script asset.
                // validate that assumption by checking if it references a MonoScript asset
                var sourceIndex = new SourceIndex(SourceIndex.SourceId.NativeObject, nativeObjectIndex);
                if (snapshot.Connections.ReferenceTo.TryGetValue(sourceIndex, out var references))
                {
                    var scriptAssetTypeIndex = snapshot.NativeTypes.MonoScriptIdx;
                    foreach (var reference in references)
                    {
                        if (reference.Id is SourceIndex.SourceId.NativeObject
                            && snapshot.NativeObjects.NativeTypeArrayIndex[reference.Index] == scriptAssetTypeIndex)
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
            return false;
        }

        internal enum OwnershipBucket
        {
            StaticRoots,
            DontDestroyOnLoadObjects,
            DontDestroyOnLoadObjectsInDeletionQueue,
            DontDestroyOnLoadScene,
            SceneRoots,
            AssetBundles,
            NativeObjectRoots,
            UnreferencedPrefabs,
            GCHandleRoots,
            NativeRootAllocations,
            BrokenScriptReferences,
            Count
        }

        void Crawl(CachedSnapshot snapshot, ref CachedProcessingData data, OwnershipBucket ownershipBucket)
        {
            // go up the reference tree
            BuildPathFromRootInfo(snapshot, ref data, ownershipBucket);
            FinishCrawl(snapshot, ref data, ownershipBucket);
        }

        void FinishCrawl(CachedSnapshot snapshot, ref CachedProcessingData data, OwnershipBucket ownershipBucket)
        {
            // and back down the reference tree
            CalculateImpact(snapshot, ref data, ownershipBucket);

            // validate that the stacks are empty and therefore wouldn't interfere with the next run, otherwise there is a bug in the algorithm
            Checks.IsTrue(data.Stack.Count == 0, "Stack should be empty after crawling");
            Checks.IsTrue(data.AlternativeStack.Count == 0, "Alternative stack  should be empty after crawling");
            // But to be sure, clear for next run
            data.Stack.Clear(false);
            data.AlternativeStack.Clear(false);
        }

        /// <summary>
        /// Computes which candidates in the same ownership-bucket sweep are genuine roots (in-degree 0 in the
        /// induced subgraph over <paramref name="candidatesInDiscoveryOrder"/>) before marking anything FoundForRoot,
        /// so that a same-bucket sibling can still claim a candidate as its child.
        /// </summary>
        /// <param name="candidatesInDiscoveryOrder">Must not yet be marked FoundForRoot by the caller.</param>
        void SeedAndClaimOwnershipBucket(CachedSnapshot snapshot, ref CachedProcessingData data, OwnershipBucket bucket,
            in DynamicArray<SourceIndex> candidatesInDiscoveryOrder)
        {
            using var candidateSet = new NativeHashSet<SourceIndex>((int)candidatesInDiscoveryOrder.Count, Allocator.Temp);
            foreach (var candidate in candidatesInDiscoveryOrder)
                candidateSet.Add(candidate);

            var seedStart = data.RootsInCurrentOwnershipBucket.Count;
            foreach (var candidate in candidatesInDiscoveryOrder)
            {
                if (HasInBucketOwner(snapshot, candidate, candidateSet))
                    continue;
                ref var marker = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, candidate);
                marker.FoundForRoot = true;
                ref var rootStep = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_ShortestPathInfo, candidate);
                rootStep = new ShortestRootPathInfo(root: candidate, parent: default, depth: 0);
                data.RootsInCurrentOwnershipBucket.Push(candidate);
            }
            BuildPathFromRootInfo(snapshot, ref data, bucket, seedStart);

            // Anything still unclaimed here has only in-bucket owners that are themselves unclaimed,
            // i.e. it sits on a cycle entirely inside this bucket. Break each cycle deterministically:
            // the first remaining candidate in discovery order becomes that cycle's root. Each round
            // claims at least one node, so this always terminates with every candidate placed.
            foreach (var candidate in candidatesInDiscoveryOrder)
            {
                ref var marker = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, candidate);
                if (marker.FoundForRoot)
                    continue;
                var tailStart = data.RootsInCurrentOwnershipBucket.Count;
                marker.FoundForRoot = true;
                ref var rootStep = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_ShortestPathInfo, candidate);
                rootStep = new ShortestRootPathInfo(root: candidate, parent: default, depth: 0);
                data.RootsInCurrentOwnershipBucket.Push(candidate);
                BuildPathFromRootInfo(snapshot, ref data, bucket, tailStart);
            }

#if DEBUG_VALIDATION
            foreach (var candidate in candidatesInDiscoveryOrder)
                Debug.Assert(SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, candidate).FoundForRoot,
                    $"{candidate} was neither seeded nor claimed while seeding ownership bucket {bucket}.");
#endif

            FinishCrawl(snapshot, ref data, bucket);
        }

        /// <summary>
        /// True if some other member of the same candidate set would claim <paramref name="candidate"/> as a child,
        /// i.e. it has in-degree > 0 in the induced subgraph over <paramref name="candidateSet"/>. Uses the exact
        /// same <see cref="DetermineSceneObjectOwnership"/> decision that <see cref="BuildPathFromRootInfo"/> uses
        /// when walking references, so a candidate can never be counted as owned here without also being claimable there.
        /// </summary>
        bool HasInBucketOwner(CachedSnapshot snapshot, in SourceIndex candidate, in NativeHashSet<SourceIndex> candidateSet)
        {
            if (!snapshot.Connections.ReferencedBy.TryGetValue(candidate, out var referrers))
                return false;

            var candidateTypeInfo = GetTypeInfo(snapshot, in candidate);
            var candidateIsSceneObject = candidate.Id is SourceIndex.SourceId.NativeObject && candidateTypeInfo.IsSceneObjectType;

            foreach (var referrer in referrers)
            {
                if (referrer.Id != SourceIndex.SourceId.NativeObject || referrer.Equals(candidate) || !candidateSet.Contains(referrer))
                    continue;

                if (!candidateIsSceneObject)
                    return true; // matches the isOwned = true default BuildPathFromRootInfo uses for non-scene-object referencedItems

                var referrerTypeInfo = GetTypeInfo(snapshot, in referrer);
                var decision = DetermineSceneObjectOwnership(snapshot, referrer, in referrerTypeInfo, candidate, in candidateTypeInfo, out var redirectPrefabIndex);
#if DEBUG_VALIDATION
                // Both candidate and referrer are drawn from a same-bucket candidate set that already
                // excludes persistent scene objects, so referencedIsPersistent is always false here and
                // neither redirect branch of DetermineSceneObjectOwnership can trigger.
                Debug.Assert(decision != SceneObjectOwnershipDecision.RedirectToPrefab,
                    $"Unexpected prefab redirect while seeding: {referrer} -> {candidate}");
#endif
                if (decision == SceneObjectOwnershipDecision.Owned)
                    return true;
            }
            return false;
        }

        static SourceIndex GetPrefabIndexForNativeSceneObject(CachedSnapshot snapshot, SourceIndex sceneObject, in UnifiedType typeInfo)
        {
            var gameObjectOrTransform = (typeInfo.IsTransformType || typeInfo.IsGameObjectType) ?
                sceneObject : ObjectConnection.GetGameObjectIndexFromTransformOrComponentIndex(snapshot, sceneObject, snapshot.NativeTypes.GameObjectIdx);

            if (gameObjectOrTransform.Valid && snapshot.SceneRoots.NativeObjectIndexToPrefabRootIndex.TryGetValue(gameObjectOrTransform.Index, out var prefabIndex))
                return new SourceIndex(SourceIndex.SourceId.Prefab, prefabIndex);
            var transform = typeInfo.IsTransformType ? sceneObject : ObjectConnection.GetTransformIndexFromGameObject(snapshot, gameObjectOrTransform, true);
            if ((!transform.Valid || !gameObjectOrTransform.Valid))
            {
                // This scene object is detatched from its GameObject and/or Transform and has no name anymore
                // It is probably waiting for deletion on the main thread.
                return default;
            }
            var objectName = snapshot.NativeObjects.ObjectName[sceneObject.Index];
            var managedObjectIndex = snapshot.NativeObjects.ManagedObjectIndex[sceneObject.Index];
            var managedTypeIndex = managedObjectIndex != ManagedData.InvalidObjectIndex ? snapshot.CrawledData.ManagedObjects[managedObjectIndex].ITypeDescription : -1;
            var typeName = typeInfo.IsTransformType ? "Transform" : (managedTypeIndex != TypeDescriptionEntriesCache.ITypeInvalid ? snapshot.TypeDescriptions.TypeDescriptionName[managedTypeIndex] : snapshot.NativeTypes.TypeName[typeInfo.NativeTypeIndex]);
            throw new InvalidOperationException($"{sceneObject} named \"{objectName}\" (EntityId {snapshot.NativeObjects.InstanceId[sceneObject.Index]}) is a persistent scene object of type {typeName} {(typeInfo.IsTransformType ? "on" : $"on Transform {transform} on GameObject")} {gameObjectOrTransform} that does not map to a prefab! (HideFlags: {snapshot.NativeObjects.HideFlags[sceneObject.Index]})");
        }

        static void CreateConnectionToPrefab(CachedSnapshot snapshot, SourceIndex referee, SourceIndex prefabIndex)
        {
            var connectionIndex = snapshot.CrawledData.Connections.Count;
            var connection = ManagedConnection.MakePrefabConnection(referee, prefabIndex.Index);
            // presumably, we'll never have more connections than int max but technically,
            // Connections is long indexed, while the hashmap is int indexed.
            Debug.Assert(connectionIndex + 1 < int.MaxValue, "Creating more connections than can be handled");
            snapshot.CrawledData.Connections.Add(connection);
            snapshot.CrawledData.ConnectionsToMappedToSourceIndex.GetAndAddToListOrCreateList(
                snapshot.CrawledData.Connections[connectionIndex].IndexTo, (int)connectionIndex, Allocator.Persistent);
            snapshot.CrawledData.ConnectionsFromMappedToSourceIndex.GetAndAddToListOrCreateList(
                snapshot.CrawledData.Connections[connectionIndex].IndexFrom, (int)connectionIndex, Allocator.Persistent);
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        static void ProcessReferenceToPrefab(CachedSnapshot snapshot, in SourceIndex prefabIndex, in SourceIndex current, ref CachedProcessingData data, in NestedDynamicArray<Marker> marker)
        {
            ref var prefabMarker = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in marker, prefabIndex);
            var isOwned = !prefabMarker.FoundForRoot;

            // Create a new connection so it can be found when attributing the impact
            CreateConnectionToPrefab(snapshot, current, prefabIndex);

            if (isOwned)
            {
                prefabMarker.FoundForRoot = true;
                data.OwnedReferences.Push(prefabIndex);
            }
            else
            {
                // Register the prefab reference as unowned
                data.UnownedReferences.Push(prefabIndex);
            }
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        static UnifiedType GetTypeInfo(CachedSnapshot snapshot, in SourceIndex item)
        {
            if (item.Id is SourceIndex.SourceId.NativeObject)
                return snapshot.TypeDescriptions.UnifiedTypeInfoNative[snapshot.NativeObjects.NativeTypeArrayIndex[item.Index]];
            if (item.Id is SourceIndex.SourceId.ManagedObject && snapshot.CrawledData.ManagedObjects[item.Index].ITypeDescription >= 0)
                return snapshot.TypeDescriptions.UnifiedTypeInfoManaged[snapshot.CrawledData.ManagedObjects[item.Index].ITypeDescription];
            return default;
        }

        enum SceneObjectOwnershipDecision
        {
            Owned,
            Unowned,
            RedirectToPrefab,
            Ignore
        }

        /// <summary>
        /// Shared by <see cref="BuildPathFromRootInfo"/>'s reference walk and <see cref="SeedAndClaimOwnershipBucket"/>'s
        /// same-bucket seed selection so both agree on exactly which references count as ownership - a second,
        /// hand-derived predicate would risk drifting out of sync with this one.
        /// </summary>
        SceneObjectOwnershipDecision DetermineSceneObjectOwnership(CachedSnapshot snapshot,
            in SourceIndex current, in UnifiedType currentTypeInfo, in SourceIndex referencedItem, in UnifiedType referencedTypeInfo,
            out SourceIndex redirectPrefabIndex)
        {
            redirectPrefabIndex = default;

            var referencedIsPersistent = snapshot.NativeObjects.Flags[referencedItem.Index].HasFlag(ObjectFlags.IsPersistent);
            var currentIsNativeObject = current.Id is SourceIndex.SourceId.NativeObject;
            var currentIsPersistent = current.Id is SourceIndex.SourceId.Prefab
                || currentIsNativeObject && snapshot.NativeObjects.Flags[current.Index].HasFlag(ObjectFlags.IsPersistent);

            if (referencedIsPersistent && !currentIsPersistent)
            {
                // This is a reference from a managed or non-persistent native object to a prefab.
                // This reference and the prefab needs special parsing, as in:
                // 1. The reference gets redirected to the Prefab index.
                // 2. This only owns the prefab if this is the first time any part of that prefab was referenced
                //    as 2 references to different sub-GameObjects or components are treated as the same reference,
                //    i.e. a reference to a prefab.
                //    As this would usually mean that a non-prefab first referenced a managed shell,
                //    and the managed shell reference to its native object is now getting redirected to point at the prefab index/root

                var prefabIndex = GetPrefabIndexForNativeSceneObject(snapshot, referencedItem, in referencedTypeInfo);
                if (prefabIndex.Valid)
                {
                    redirectPrefabIndex = prefabIndex;
                    return SceneObjectOwnershipDecision.RedirectToPrefab;
                }
                else
                {
                    // With the prefeb being returned as invalid, this object is highly likely in the delayed deletion queue
                    // Add it to this hashset (so it can be excluded as problematic for referencing a Transform)
                    // but continue procesing as if it weren't an asset.
                    m_PersistentSceneObjectsInDelayedDeletionQueue.Add(referencedItem);
                }
            }
            else if (current.Id is SourceIndex.SourceId.ManagedObject)
            {
                // This is practically guaranteed to be a Scene Object's managed shell referencing back to its native object,
                // (unless someone felt hacky and got their hands on the pointer to a native object).

                // Managed references to non-persistent SceneObjects are non-owning, as the Scene Objects are more strongly
                // bound to the scene.
                // Scene and Prefab indexes are handled above if the referencedTypeInfo indicates this is a root Transform.

                // Further, it should be ignored for impact attribution, so don't add it as unowned reference.
                return SceneObjectOwnershipDecision.Ignore;
            }
            else if (referencedTypeInfo.IsTransformType)
            {
                // References to Transforms are only owned if they originate in another Transform, Scene, or Prefab
                if (currentIsNativeObject && currentTypeInfo.IsTransformType)
                {
                    if (referencedIsPersistent && currentIsPersistent)
                    {
                        // For references within Prefab hierarchies, ensure that the transforms
                        // belong to the same prefab, to avoid including already owned nested prefab hierarchies as owned
                        if (!snapshot.SceneRoots.NativeObjectIndexToPrefabRootIndex.TryGetValue(
                                current.Index, out var currentPrefabIndex))
                            currentPrefabIndex = -1;
                        if (!snapshot.SceneRoots.NativeObjectIndexToPrefabRootIndex.TryGetValue(
                                referencedItem.Index, out var referencedPrefabIndex))
                            referencedPrefabIndex = -1;
                        if (currentPrefabIndex != referencedPrefabIndex)
                        {
                            redirectPrefabIndex = new SourceIndex(SourceIndex.SourceId.Prefab, referencedPrefabIndex);
                            return SceneObjectOwnershipDecision.RedirectToPrefab;
                        }
                    }
                    return SceneObjectOwnershipDecision.Owned;
                }
                else
                    return (snapshot.NativeObjects.Flags[referencedItem.Index].HasFlag(ObjectFlags.IsRoot) && current.Id is SourceIndex.SourceId.Scene or SourceIndex.SourceId.Prefab)
                        ? SceneObjectOwnershipDecision.Owned : SceneObjectOwnershipDecision.Unowned;
            }
            else if (referencedTypeInfo.IsGameObjectType)
            {
                // References to GameObjects are only owned if they originate in their Transform
                return currentIsNativeObject && currentTypeInfo.IsTransformType ? SceneObjectOwnershipDecision.Owned : SceneObjectOwnershipDecision.Unowned;
            }
            else if (referencedTypeInfo.IsComponentType)
            {
                // References to Components are only owned if they originate in their GameObject
                return currentIsNativeObject && (currentTypeInfo.IsGameObjectType
                        // Sometimes AssetBundles hold references to a MonoBehaviour without a ManagedShell or GameObject
                        // (the only connection being to the asset bundle).
                        // In those cases, do root them to the Asset Bundle.
                        || currentTypeInfo.NativeTypeIndex == snapshot.NativeTypes.AssetBundleIdx
                        && referencedIsPersistent && referencedTypeInfo.IsMonoBehaviourType
                        && snapshot.NativeObjects.ManagedObjectIndex[referencedItem.Index] == CachedSnapshot.NativeObjectEntriesCache.InvalidObjectIndex
                        && snapshot.Connections.ReferencedBy.TryGetValue(referencedItem, out var monoBehaviourReferencedItems)
                        && monoBehaviourReferencedItems.Count == 1)
                    ? SceneObjectOwnershipDecision.Owned : SceneObjectOwnershipDecision.Unowned;
            }
            else
            {
                // Reached only by a scene object that is neither a Transform, GameObject, nor
                // Component. Every scene object is a GameObject or a Component (Transforms are
                // Components), so all hierarchy references are classified above. A Scene can
                // legitimately reference its scene objects, so that referrer keeps owning;
                // any other referrer here is unexpected.
                Checks.IsTrue(current.Id is SourceIndex.SourceId.Scene,
                    $"Found a reference from a {current.Id} to a scene object {referencedItem} that is neither a Transform, GameObject, nor Component, which is not supposed to happen.");
                // Where the assertion is compiled out (release), fail safe to non-owning for an
                // unexpected referrer rather than silently claiming ownership of the scene object.
                return current.Id is SourceIndex.SourceId.Scene ? SceneObjectOwnershipDecision.Owned : SceneObjectOwnershipDecision.Unowned;
            }
            // referencedIsPersistent && !currentIsPersistent, but the prefab lookup came back invalid: fall
            // back to the pre-chain default of owned, same as the original inline isOwned = true default.
            return SceneObjectOwnershipDecision.Owned;
        }

        void BuildPathFromRootInfo(CachedSnapshot snapshot, ref CachedProcessingData data, OwnershipBucket ownershipBucket, long seedStartIndex = 0)
        {
            if (seedStartIndex == 0)
                data.Stack.PushRange(data.RootsInCurrentOwnershipBucket);
            else
                for (long i = seedStartIndex; i < data.RootsInCurrentOwnershipBucket.Count; i++)
                    data.Stack.Push(data.RootsInCurrentOwnershipBucket[i]);

            while (data.Count > 0)
            {
                var current = data.Stack.Pop();

                ref var currentMarker = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, current);
                currentMarker.FoundForRoot = true;
                // Each node is popped (and marked) exactly once across all buckets, so this tallies the
                // full ownership forest for callers that need to size per-node collections.
                m_OwnershipForestNodeCount++;

                var currentTypeInfo = GetTypeInfo(snapshot, in current);

                if (current.Id is SourceIndex.SourceId.NativeRootReference)
                {
                    data.Connections.References.Clear();
                    // For native roots that are not associated with a native object, process all their native allocations that were not already found
                    // RootIndexToAllocations already only contains non-native-object associated roots
                    if (snapshot.ProcessedNativeRoots.RootIndexToAllocations.TryGetValue(current.Index, out var listOfAllocations))
                    {
                        foreach (var allocation in listOfAllocations)
                        {
                            data.Connections.References.Add(new SourceIndex(SourceIndex.SourceId.NativeAllocation, allocation));
                        }
                    }
                }
                else if (current.Id is SourceIndex.SourceId.NativeAllocation)
                {
                    // Native Allocations don't reference anything
                    data.Connections.References.Clear();
                }
                else if (current.Id is SourceIndex.SourceId.Prefab)
                {
                    data.Connections.References.Clear();
                    data.Connections.References.Add(snapshot.SceneRoots.AllPrefabRootTransformSourceIndices[current.Index]);
                }
                else if (current.Id is SourceIndex.SourceId.Scene)
                {
                    data.Connections.References.Clear();
                    foreach (var rootTransform in snapshot.SceneRoots.SceneIndexedRootTransformInstanceIds[current.Index])
                    {
                        data.Connections.References.Add(rootTransform);
                    }
                }
                else if (current.Id is SourceIndex.SourceId.GCHandleIndex)
                {
                    data.Connections.References.Clear();

                    // remove temporarily added "found" marker on the managed object so that this GCHandle can take its ownership
                    ref var managedObjectMark = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, new SourceIndex(SourceIndex.SourceId.ManagedObject, current.Index));
                    managedObjectMark.FoundForRoot = false;

                    // GCHandle index maps to the first managed object indices
                    data.Connections.References.Add(new SourceIndex(SourceIndex.SourceId.ManagedObject, current.Index));
                }
                else if (current.Id is SourceIndex.SourceId.NativeObject && currentTypeInfo.NativeTypeIndex == snapshot.NativeTypes.AssetBundleIdx)
                {
                    ref readonly var nativeTypeInfos = ref snapshot.TypeDescriptions.UnifiedTypeInfoNative;
                    // AssetBundles only reference their contained objects, but they reference every child and component of their prefabs
                    // To ensure the scene object hierarchy for these prefabs is built up correctly, we only consider the root transforms
                    // of the prefabs contained in the AssetBundle here, and redirect the reference from the AssetBundle to the
                    // prefab root transform via the prefab index.
                    data.Connections.References.Clear();
                    if (snapshot.Connections.ReferenceTo.TryGetValue(current, out var referencedItems))
                    {
                        foreach (var referencedItem in referencedItems)
                        {
                            if (referencedItem.Id is SourceIndex.SourceId.NativeObject)
                            {
                                var referencedTypeInfo = nativeTypeInfos[snapshot.NativeObjects.NativeTypeArrayIndex[referencedItem.Index]];
                                if (referencedTypeInfo.IsSceneObjectType && snapshot.NativeObjects.Flags[referencedItem.Index].HasFlag(ObjectFlags.IsPersistent))
                                {
                                    if (referencedTypeInfo.IsMonoBehaviourType
                                        && snapshot.NativeObjects.ManagedObjectIndex[referencedItem.Index] == CachedSnapshot.NativeObjectEntriesCache.InvalidObjectIndex
                                        && snapshot.Connections.ReferencedBy.TryGetValue(referencedItem, out var monoBehaviourReferencedItems)
                                        && monoBehaviourReferencedItems.Count == 1)
                                    {
                                        // Sometimes AssetBundles hold references to a MonoBehaviour without a ManagedShell or GameObject
                                        // (the only connection being to the asset bundle).
                                        // In those cases, do root them to the Asset Bundle.
                                        data.Connections.References.Add(referencedItem);
                                        continue;
                                    }

                                    // This is a reference to a prefab's scene object
                                    // ignore it if it is not the root transform
                                    if (!referencedTypeInfo.IsTransformType || !snapshot.NativeObjects.Flags[referencedItem.Index].HasFlag(ObjectFlags.IsRoot))
                                        continue;

                                    // Redirect prefab references to the prefab root transform
                                    var prefabIndex = new SourceIndex(SourceIndex.SourceId.Prefab, snapshot.SceneRoots.NativeObjectIndexToPrefabRootIndex[referencedItem.Index]);
                                    // There are likely multiple references to different scene objects or components of the same prefab from the AssetBundle
                                    // only add the prefab connection once

                                    if (!data.Connections.References.Contains(prefabIndex))
                                    {
                                        // and register a connection so it can be found when attributing the impact
                                        CreateConnectionToPrefab(snapshot, current, prefabIndex);
                                        data.Connections.References.Add(prefabIndex);
                                    }
                                    continue;
                                }
                            }
                            data.Connections.References.Add(referencedItem);
                        }
                    }
                }
                else
                {
                    ObjectConnection.GetAllReferencedObjects(snapshot, current, null, treatUnityObjectsAsOneObject: false, addManagedObjectsWithFieldInfo: false, foundSourceIndices: data.Connections.References);
                }

                ref var currentOwnedReferencesInfo = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_RootPathOwnedReferencesLookup, current);

                if (data.Connections.References.Count > 0)
                {
                    data.OwnedReferences.Clear(false);
                    data.UnownedReferences.Clear(false);

                    ref readonly var currentPathStep = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_ShortestPathInfo, current);

                    foreach (var referencedItem in data.Connections.References)
                    {
                        if (!snapshot.CrawledData.Crawled && referencedItem.Id is SourceIndex.SourceId.ManagedObject)
                            // When the snapshot is not crawled, the managed objects are not known,
                            // even for those referenced by native objects and held via GCHandles, so skip them.
                            continue;

                        ref var referencedItemMarker = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, referencedItem);
                        if (!referencedItemMarker.FoundForRoot)
                        {
                            var isOwned = true;
                            if (referencedItem.Id is SourceIndex.SourceId.NativeObject)
                            {
                                ref readonly var referencedTypeInfo = ref snapshot.TypeDescriptions.UnifiedTypeInfoNative[snapshot.NativeObjects.NativeTypeArrayIndex[referencedItem.Index]];

                                if (referencedTypeInfo.IsSceneObjectType)
                                {
                                    // Native Scene objects need special parsing to ensure that the Scene Object Hierarchy is build and rooted correctly
                                    var decision = DetermineSceneObjectOwnership(snapshot, current, in currentTypeInfo, referencedItem, in referencedTypeInfo, out var redirectPrefabIndex);
                                    switch (decision)
                                    {
                                        case SceneObjectOwnershipDecision.RedirectToPrefab:
                                            ProcessReferenceToPrefab(snapshot, redirectPrefabIndex, current, ref data, in m_Marker);
                                            continue;
                                        // Yes, at this point we abandoned the referencedItem as a reference to be put on the stack.
                                        // Not to worry, it will be found while processing the Hierarchy of the prefab.
                                        case SceneObjectOwnershipDecision.Ignore:
                                            // Further, it should be ignored for impact attribution, so don't add it as unowned reference.
                                            continue;
                                        case SceneObjectOwnershipDecision.Owned:
                                            isOwned = true;
                                            break;
                                        case SceneObjectOwnershipDecision.Unowned:
                                            isOwned = false;
                                            break;
                                    }
                                }
                            }
                            if (referencedItem.Id is SourceIndex.SourceId.NativeAllocation &&
                                snapshot.NativeObjects.RootReferenceIdToIndex.ContainsKey(snapshot.NativeAllocations.RootReferenceId[referencedItem.Index]))
                            {
                                // Native Objects own their allocations. If someone else happens to reference them, it's a non-owning reference
                                // and its impact shouldn't be attributed to it either.
                                continue;
                            }

                            if (isOwned)
                            {
                                referencedItemMarker.FoundForRoot = true;
                                data.OwnedReferences.Push(referencedItem);
                                continue;
                            }
                        }
                        data.UnownedReferences.Push(referencedItem);
                    }

                    if (data.OwnedReferences.Count > 0)
                    {
                        var childDepth = currentPathStep.Depth + 1;
                        if (data.OwnedReferences.Count == 1)
                        {
                            var onlyChild = data.OwnedReferences[0];
                            currentOwnedReferencesInfo = new ReferencesListInfo(onlyChild);

                            ref var childPathStep = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_ShortestPathInfo, onlyChild);
                            childPathStep = new ShortestRootPathInfo(currentPathStep.Root, current, childDepth);

                            data.AlternativeStack.Push(onlyChild);
                        }
                        else
                        {
                            currentOwnedReferencesInfo = new ReferencesListInfo(m_OwnedChildList.Count);
                            m_OwnedChildList.Push(ReferencesListInfo.CountToSourceIndex(data.OwnedReferences.Count));
                            m_OwnedChildList.PushRange(data.OwnedReferences);

                            var indexInChildList = 0L;
                            foreach (var ownedReference in data.OwnedReferences)
                            {
                                ref var childPathStep = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_ShortestPathInfo, ownedReference);
                                childPathStep = new ShortestRootPathInfo(currentPathStep.Root, current, childDepth, indexInChildList++);
                            }
                            data.AlternativeStack.PushRange(data.OwnedReferences);
                        }
                    }
                    else
                    {
                        currentOwnedReferencesInfo = new ReferencesListInfo(noChildren: true);
                    }
                    // handle further non-owned references
                    ref var unownedReferencesInfo = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_UnownedReferencesLookup, current);
                    if (data.UnownedReferences.Count > 0)
                    {
                        if (data.UnownedReferences.Count == 1)
                        {
                            unownedReferencesInfo = new ReferencesListInfo(data.UnownedReferences[0]);
                        }
                        else
                        {
                            unownedReferencesInfo = new ReferencesListInfo(m_UnownedChildList.Count);
                            UnownedChildList.Push(ReferencesListInfo.CountToSourceIndex(data.UnownedReferences.Count));
                            UnownedChildList.PushRange(data.UnownedReferences);
                        }
                    }
                    else
                    {
                        unownedReferencesInfo = new ReferencesListInfo(noChildren: true);
                    }

                }
                else
                {
                    currentOwnedReferencesInfo = new ReferencesListInfo(noChildren: true);
                    ref var unownedReferencesInfo = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_UnownedReferencesLookup, current);
                    unownedReferencesInfo = new ReferencesListInfo(noChildren: true);
                }

                if (data.Connections.References.Count > CachedProcessingData.ConnectionCache.MaxCapacity)
                {
                    data.Connections.References = new HashSet<SourceIndex>(CachedProcessingData.ConnectionCache.DefaultCapacity);
                }
            }
        }

        void CalculateImpact(CachedSnapshot snapshot, ref CachedProcessingData data, OwnershipBucket ownershipBucket)
        {
            // While the roots have to be processed in breadth first order to avoid finding shorter paths later on
            // and having to do a costly rewire of the path info,
            // the impact has to be calculated in depth first order to avoid getting stuck in cyclical references.
            foreach (var root in data.RootsInCurrentOwnershipBucket)
            {
                // for each root, recursively process down to find the leafs and attribute their sizes back up
                // but only if not already done for this root
                ref var rootMarker = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, root);
                if (rootMarker.FoundForRoot && !rootMarker.FoundForImpact)
                {
                    data.ImpactStack.Clear(false);
                    AddNextToImpactStack(ref data, root, ref rootMarker);
                    while (data.ImpactStack.Count > 0)
                    {
                        // Mutate the top step in place instead of Peek + Pop + Push: the previous pattern copied
                        // the ~60-byte ImpactProcessingStackStep struct out and back on every child just to persist
                        // an incremented counter. The ref must be re-acquired each iteration and never read after a
                        // call that can Push (AddNextToImpactStack), since a Push may reallocate the backing array;
                        // a realloc copies the (already-mutated) element across, so the increment is preserved.
                        ref var currentStep = ref data.ImpactStack[data.ImpactStack.Count - 1];
                        if (currentStep.ProcessedChildren)
                        {
                            // all children processed, attribute impact and pop
                            var current = currentStep.Current;
                            data.ImpactStack.Pop();
                            AttributeImpact(snapshot, ref data, current, ownershipBucket);
                        }
                        else
                        {
                            // still have children to process, push next child onto stack
                            if (currentStep.OwnedChildrenProcessed < currentStep.OwnedChildListCount)
                            {
                                var ownedChildIndex = currentStep.OwnedReferencesListInfo.HasOneChild ?
                                    currentStep.OwnedReferencesListInfo.SingleChild :
                                    m_OwnedChildList[currentStep.OwnedReferencesListInfo.IndexInChildList + 1 + currentStep.OwnedChildrenProcessed];
                                currentStep.OwnedChildrenProcessed++;
                                ref var marker = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, ownedChildIndex);
                                if (marker.FoundForRoot && !marker.FoundForImpact)
                                    AddNextToImpactStack(ref data, ownedChildIndex, ref marker); // may reallocate ImpactStack; do not read currentStep after this
                            }
                            else if (currentStep.UnownedChildrenProcessed < currentStep.UnownedChildListCount)
                            {
                                var unownedChildIndex = currentStep.UnownedReferencesListInfo.HasOneChild ?
                                    currentStep.UnownedReferencesListInfo.SingleChild :
                                    m_UnownedChildList[currentStep.UnownedReferencesListInfo.IndexInChildList + 1 + currentStep.UnownedChildrenProcessed];
                                currentStep.UnownedChildrenProcessed++;
                                ref var marker = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, unownedChildIndex);
                                if (marker.FoundForRoot && !marker.FoundForImpact)
                                {
                                    // Handle references to unowned scene objects differently to avoid having to reattribute them.
                                    if (unownedChildIndex.Id is SourceIndex.SourceId.NativeObject)
                                    {
                                        ref readonly var typeData = ref snapshot.TypeDescriptions.UnifiedTypeInfoNative[snapshot.NativeObjects.NativeTypeArrayIndex[unownedChildIndex.Index]];
                                        if (typeData.IsSceneObjectType)
                                        {
                                            // Scene Objects have a very clear ownership structure, so an unowned reference to a Scene Object can be safely ignored for impact attribution,
                                            // as it will be fully attributed when processing the owning hierarchy of the scene object itself.
                                            continue;
                                        }
                                    }

                                    AddNextToImpactStack(ref data, unownedChildIndex, ref marker); // may reallocate ImpactStack; do not read currentStep after this
                                }
                            }
                            else
                            {
                                throw new InvalidOperationException("Should not reach here");
                            }
                        }
                    }
                }
            }

            ValidateMarkers(snapshot);
            data.FinalizeRootBucket(ownershipBucket);
            data.RootsInCurrentOwnershipBucket.Clear(false);
        }

        struct ImpactProcessingStackStep
        {
            public SourceIndex Current;
            public ReferencesListInfo OwnedReferencesListInfo;
            public long OwnedChildListCount;
            public long OwnedChildrenProcessed;
            public ReferencesListInfo UnownedReferencesListInfo;
            public long UnownedChildListCount;
            public long UnownedChildrenProcessed;
            public bool ProcessedChildren => OwnedChildrenProcessed >= OwnedChildListCount && UnownedChildrenProcessed >= UnownedChildListCount;
        }

        void AddNextToImpactStack(ref CachedProcessingData data, SourceIndex current, ref Marker currentMarker)
        {
            currentMarker.FoundForImpact = true;
            var ownedReferencesListInfo = SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_RootPathOwnedReferencesLookup, current);
            var unownedReferencesListInfo = SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_UnownedReferencesLookup, current);
            var currentStep = new ImpactProcessingStackStep
            {
                Current = current,
                OwnedReferencesListInfo = ownedReferencesListInfo,
                OwnedChildListCount = ownedReferencesListInfo.HasMultipleChildren ?
                 ReferencesListInfo.SourceIndexToCount(m_OwnedChildList[ownedReferencesListInfo.IndexInChildList]) : (ownedReferencesListInfo.HasOneChild ? 1 : 0),
                UnownedReferencesListInfo = unownedReferencesListInfo,
                UnownedChildListCount = unownedReferencesListInfo.HasMultipleChildren ?
                 ReferencesListInfo.SourceIndexToCount(m_UnownedChildList[unownedReferencesListInfo.IndexInChildList]) : (unownedReferencesListInfo.HasOneChild ? 1 : 0),
            };
            data.ImpactStack.Push(currentStep);
        }

        void AttributeImpact(CachedSnapshot snapshot, ref CachedProcessingData data, SourceIndex current, OwnershipBucket ownershipBucket)
        {
            ref readonly var currentPathStep = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_ShortestPathInfo, current);
            ref var currentMarker = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, current);
            ref var currentImpact = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Impact, current);
            NativeRootSize ownSize = default;
            switch (current.Id)
            {
                case SourceIndex.SourceId.SystemMemoryRegion:
                case SourceIndex.SourceId.ManagedHeapSection:
                case SourceIndex.SourceId.NativeMemoryRegion:
                case SourceIndex.SourceId.GfxResource:
                case SourceIndex.SourceId.NativeType:
                case SourceIndex.SourceId.MemoryLabel:
                    // Not handled for impact
                    break;
                case SourceIndex.SourceId.ManagedType:
                    // Committed = captured static field bytes; resident is deduced from the type's
                    // static-storage address against the resident-page bitset (see GetStaticFieldOwnSize).
                    ownSize = new NativeRootSize
                    {
                        ManagedSize = GetStaticFieldOwnSize(snapshot, current.Index)
                    };
                    break;
                case SourceIndex.SourceId.NativeRootReference:
                    ownSize = new NativeRootSize
                    {
                        // NativeObjects are handled and the native sizes of allocations owned by e.g. managed
                        // references to the native allocations of e.g. UnsafeUtility.Malloc'd memory needs to be ignored,
                        // which is done by crawling and attributing the sizes of non-native-object-owned allocations up to
                        // their Native Root Reference. So unless we don't have Native Allocations captured,
                        // we don't need to attribute any native size for the root here because it is already accounted for.
                        NativeSize = !snapshot.MetaData.CaptureFlags.HasFlag(CaptureFlags.NativeAllocations) ?
                            snapshot.ProcessedNativeRoots.Data[current.Index].AccumulatedRootSizes.NativeSize : default,
                        // We do need to know the graphics size for this root though.
                        GfxSize = snapshot.ProcessedNativeRoots.Data[current.Index].AccumulatedRootSizes.GfxSize
                    };
                    // TODO: attribute native allocator utilization up, calculated in ProcessedNativeRoots
                    break;
                case SourceIndex.SourceId.GCHandleIndex:
                    // This is a floating GCHandle, which has no size of its own.
                    break;
                case SourceIndex.SourceId.Scene:
                    // All memory is held by SceneObjects (maybe safe for the name)
                    break;
                case SourceIndex.SourceId.NativeObject:
                    // native object own their native and graphics size
                    var processedNativeRootIndex = snapshot.ProcessedNativeRoots.RootIdToMappedIndex(snapshot.NativeObjects.RootReferenceId[current.Index]);
                    ownSize = snapshot.ProcessedNativeRoots.Data[processedNativeRootIndex].AccumulatedRootSizes;
                    // but their managed shell might be owned by someone else so, discount their managed size here
                    ownSize.ManagedSize = default;
                    // TODO: attribute native allocator utilization up, calculated in ProcessedNativeRoots
                    break;
                case SourceIndex.SourceId.ManagedObject:
                    ownSize = new NativeRootSize
                    {
                        ManagedSize = snapshot.ProcessedNativeRoots.ManagedSizes[current.Index]
                    };
                    // TODO: attribute managed heap page utilization up
                    break;
                case SourceIndex.SourceId.NativeAllocation:
                    var allocationNative = snapshot.ProcessedNativeRoots.AllocationSizes[current.Index];
                    // Subtract any static-field storage physically contained in this allocation: those bytes
                    // are attributed as Managed to their ManagedType root (see the ManagedType case above /
                    // GetStaticFieldOwnSize), so counting them here too would double-count. Done per-allocation
                    // (not as a bulk subtraction from the VM root) so the reduced size is correct in BOTH the
                    // hierarchical and flattened views.
                    EnsureStaticBytesPerNativeAllocation(snapshot);
                    if (m_StaticBytesPerNativeAllocation.TryGetValue(current.Index, out var staticInAllocation))
                    {
                        var actuallyDeducted = MemorySize.Min(staticInAllocation, allocationNative);
                        allocationNative -= actuallyDeducted;
                        StaticBytesActuallyDeductedFromNative += actuallyDeducted;
                    }
                    // The Mono/IL2CPP VM heap is allocated through Unity's native allocators, so its blocks
                    // surface as NativeAllocations rooted to the VM root, but logically they ARE the managed
                    // runtime's backing store. Report them as Managed (consistent with
                    // EntriesMemoryMapCache.GetPointType and the Summary / All Of Memory views) so the Roots &
                    // Impact VM bucket shows as managed. Channel-only: the snapshot total is unchanged. Static
                    // storage lives inside these VM allocations and is also attributed as Managed to its
                    // ManagedType root, so the per-allocation deduction above — and the bulk remainder in
                    // DeductStaticFieldNativeRemainder — remove it from the managed side for them.
                    var vmRootReference = snapshot.NativeRootReferences.VMRootReferenceIndex;
                    if (vmRootReference.Valid
                        && snapshot.NativeAllocations.RootReferenceId[current.Index] == snapshot.NativeRootReferences.Id[vmRootReference.Index])
                        ownSize = new NativeRootSize { ManagedSize = allocationNative };
                    else
                        ownSize = new NativeRootSize { NativeSize = allocationNative };
                    // TODO: attribute native allocator utilization up, calculated in ProcessedNativeRoots
                    break;
                case SourceIndex.SourceId.Prefab:
                    // Prefabs have no size of their own, it's all down to their Transform hierarchy
                    break;
                case SourceIndex.SourceId.None:
                default:
                    throw new NotImplementedException();
            }
            currentImpact.Exclusive += ownSize;

            // referees are collected in UnownedReferences
            data.UnownedReferences.Clear(false);
            switch (current.Id)
            {
                case SourceIndex.SourceId.None:
                case SourceIndex.SourceId.SystemMemoryRegion:
                case SourceIndex.SourceId.ManagedHeapSection:
                case SourceIndex.SourceId.NativeMemoryRegion:
                case SourceIndex.SourceId.GfxResource:
                case SourceIndex.SourceId.NativeType:
                case SourceIndex.SourceId.MemoryLabel:
                    // Not handled for impact
                    break;
                case SourceIndex.SourceId.ManagedType:
                    // This is a root
                    break;
                case SourceIndex.SourceId.NativeObject:
                    ref readonly var currentTypeInfo = ref snapshot.TypeDescriptions.UnifiedTypeInfoNative[snapshot.NativeObjects.NativeTypeArrayIndex[current.Index]];

                    if (currentTypeInfo.IsSceneObjectType)
                    {
                        // Scene Object hierarchy is rooted and attributed up to its parent Transform and from there to the scene or prefab,
                        // which acts as the root. None of these should be roots in and off themselves
                        if (currentPathStep.IsRoot)
                        {
                            if (ownershipBucket is not OwnershipBucket.BrokenScriptReferences)
                                Checks.IsTrue(snapshot.NativeObjects.Flags[current.Index].HasFlag(ObjectFlags.IsDontDestroyOnLoad), "Scene Objects should never be roots in and of themselves, unless they are marked as DontDestroyOnLoad.");
                        }
                        else if (currentPathStep.Parent.Id is SourceIndex.SourceId.Scene)
                        {
                            // Only Transforms are parented to a scene
                            Checks.IsTrue(currentTypeInfo.IsTransformType);
                            // attribute all impact up to the rooting scene
                            data.UnownedReferences.Push(currentPathStep.Parent);
                        }
                        else if (currentPathStep.Parent.Id is SourceIndex.SourceId.Prefab)
                        {
                            // Only Transforms are parented to a prefab
                            Checks.IsTrue(currentTypeInfo.IsTransformType);
                            // attribute all impact up to the rooting prefab
                            data.UnownedReferences.Push(currentPathStep.Parent);
                        }
                        else if (currentPathStep.Parent.Id is SourceIndex.SourceId.NativeObject)
                        {
                            var parentTypeIndex = snapshot.NativeObjects.NativeTypeArrayIndex[currentPathStep.Parent.Index];
                            ref readonly var parentTypeInfo = ref snapshot.TypeDescriptions.UnifiedTypeInfoNative[parentTypeIndex];
                            Checks.IsTrue(parentTypeInfo.IsSceneObjectType || parentTypeIndex == snapshot.NativeTypes.AssetBundleIdx); // Found a SceneObject rooted via something other than a SceneObject!
                            // Native Scene object have clear ownership. Attribute up to its parent
                            data.UnownedReferences.Push(currentPathStep.Parent);
                        }
                        else
                        {
                            var transformName = snapshot.NativeObjects.ObjectName[current.Index];
                            if (currentPathStep.Parent.Id is SourceIndex.SourceId.ManagedObject)
                            {
                                // Managed references to persistent scene objects in the delayed deletion queue are fine, everything else is suspicious.
                                if (!m_PersistentSceneObjectsInDelayedDeletionQueue.Contains(current))
                                {
                                    var managedTypeIndex = snapshot.CrawledData.ManagedObjects[currentPathStep.Parent.Index].ITypeDescription;
                                    var managedTypeName = snapshot.TypeDescriptions.TypeDescriptionName[managedTypeIndex];

                                    throw new InvalidOperationException($"For bucket {ownershipBucket}: Found a {currentPathStep.Parent.Id} of type {managedTypeName} as a parent to a Transform ({current} named \"{transformName}\"), which is not supposed to happen!");
                                }
                            }
                            else
                                throw new InvalidOperationException($"For bucket {ownershipBucket}: Found a {currentPathStep.Parent.Id} as a parent to a Transform ({current} named \"{transformName}\"), which is not supposed to happen!");
                        }
                    }
                    else if (currentPathStep.IsRoot)
                    {
                        // Hard-rooted native objects keep their accumulated impact: attributing it to referees
                        // would skip the terminal data.Roots registration below and hide their whole owned
                        // subtree from the Roots & Impact table (referee edges are not display edges).
                        // This means that e.g. any Non-SceneObject DontDestroyOnLoad native object that IS
                        // referenced by anything else than a statically rooted chain, including other objects
                        // in the same bucket will be attributed to the DoNotDestroyOnLoad bucket as a root.
                    }
                    else
                    {
                        // Other Native Objects attribute to all of their already found references
                        FindRefereesForImpactAttribution(snapshot, ref data, current);
                    }
                    break;
                case SourceIndex.SourceId.NativeAllocation:
                    var rootId = snapshot.NativeAllocations.RootReferenceId[current.Index];
                    if (rootId >= NativeRootReferenceEntriesCache.FirstValidRootId)
                    {
                        var rootIndex = snapshot.ProcessedNativeRoots.RootIdToMappedIndex(rootId);
                        var rootSourceIndex = snapshot.ProcessedNativeRoots.Data[rootIndex].NativeObjectOrRootIndex;

                        // if it is a native object, attribute it to the native object
                        if (rootSourceIndex.Id is SourceIndex.SourceId.NativeObject)
                        {
                            data.UnownedReferences.Push(rootSourceIndex);
                        }
                        else
                        {
                            // Could be attributed to any managed or native object or even a managed type
                            FindRefereesForImpactAttribution(snapshot, ref data, current);

                            // but if it is a native object or no one references it, attribute it to its root
                            if (data.UnownedReferences.Count == 0)
                            {
                                // This is an unreferenced native allocation, attribute it to its root
                                data.UnownedReferences.Push(rootSourceIndex);
                            }
                        }
                    }
                    else
                    {
                        // Could be attributed to any managed or native object or even a managed type
                        FindRefereesForImpactAttribution(snapshot, ref data, current);

                        if (data.UnownedReferences.Count == 0)
                        {
                            // Track the amount of Unrooted Memory that, is not rooted to a
                            // valid Native Root Reference / MemLabel, nor referenced via managed references

                            // Note: this should currently not happen as native allocations without a valid root allocation are not crawled
                            // and processed for impact because that would be overkill (nothing is referencing or refereed to by them after all)
                            // Instead they are handled directly in HandleUnreferenceUnrootedAllocations
                            UnreferencedUnrootedMemory += currentImpact.Exclusive;
                        }
                        else
                        {
                            // Track the amount of Unrooted Memory that, while not rooted to a
                            // valid Native Root Reference / MemLabel, is referenced via managed references
                            ReferencedUnrootedMemory += currentImpact.Exclusive;
                        }
                    }
                    break;
                case SourceIndex.SourceId.ManagedObject:
                    // Could be attributed to any managed or native object or even a managed type
                    FindRefereesForImpactAttribution(snapshot, ref data, current);
                    if (data.UnownedReferences.Count == 0 && currentPathStep.Parent.Valid && currentPathStep.Parent.Id is SourceIndex.SourceId.GCHandleIndex)
                    {
                        // This is a managed object that's exclusively held by a GCHandle, attribute it to the GCHandle.
                        data.UnownedReferences.Push(currentPathStep.Parent);
                    }
                    break;
                case SourceIndex.SourceId.NativeRootReference:
                    // This is a root
                    break;
                case SourceIndex.SourceId.GCHandleIndex:
                    // GCHandles are a root (if nothing else roots the managed object, but then it would not be found as a leaf referee)
                    break;
                case SourceIndex.SourceId.Scene:
                    // Scenes are a root
                    break;
                case SourceIndex.SourceId.Prefab:
                    // Could be attributed to any managed or native object or even a managed type
                    FindRefereesForImpactAttribution(snapshot, ref data, current);
                    break;
                default:
                    throw new NotImplementedException();
            }

            currentMarker.FullyDistributedImpact = true;

            if (data.UnownedReferences.Count > 0)
            {
                // Strike any would-be referees whose impact has already been fully distributed: adding to
                // them now would never re-bubble to a summed root and would be silently lost. The
                // direct-push paths above (scene→parent, native-allocation→root, managed→GCHandle) do not
                // filter these, unlike FindRefereesForImpactAttribution which already skips them.
                long liveReferees = 0;
                long liveGcHandleReferees = 0; // how many survivors are GCHandles (counted in the same pass)
#if DEBUG_VALIDATION
                var firstStruckReferee = default(SourceIndex);
#endif
                for (long u = 0; u < data.UnownedReferences.Count; u++)
                {
                    var candidate = data.UnownedReferences[u];
                    if (!SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, candidate).FullyDistributedImpact)
                    {
                        if (candidate.Id == SourceIndex.SourceId.GCHandleIndex)
                            liveGcHandleReferees++;
                        data.UnownedReferences[liveReferees++] = candidate;
                    }
#if DEBUG_VALIDATION
                    else if (!firstStruckReferee.Valid)
                        firstStruckReferee = candidate;
#endif
                }
                data.UnownedReferences.Resize(liveReferees, false);

                // If the surviving referees mix GCHandle(s) with non-GCHandle referees, strike the GCHandle(s).
                // A GCHandle that holds an object which is ALSO referenced by a real owner is runtime
                // bookkeeping, not a memory owner: attributing a pro-rata share to it would dilute the real
                // owner's ownership and — because such a GCHandle is non-floating and never processed for impact
                // (see CrawlGCHandleRoots) — that share would strand, never bubbling to a summed root. Removing
                // the GCHandle lets the impact bubble fully to the real owner(s), and be Exclusive when there is
                // exactly one. A GCHandle that is the SOLE referee is left intact — it is the legitimate
                // (floating) root of an object nothing else holds. The GCHandle count is gathered in the strike
                // loop above, so this extra compaction only runs in the (uncommon) mixed case — a referee list
                // with no GCHandle (the common case, e.g. a manager with many referrers) skips it entirely.
                if (liveGcHandleReferees > 0 && liveGcHandleReferees < liveReferees)
                {
                    long kept = 0;
                    for (long u = 0; u < liveReferees; u++)
                    {
                        var candidate = data.UnownedReferences[u];
                        if (candidate.Id != SourceIndex.SourceId.GCHandleIndex)
                            data.UnownedReferences[kept++] = candidate;
                    }
                    data.UnownedReferences.Resize(kept, false);
                }

                if (data.UnownedReferences.Count == 0)
                {
                    // Defensive fallback: every would-be referee was already fully distributed. This can
                    // only happen to scene/prefab hierarchy items (Transforms/GameObjects/Components),
                    // which attribute to their BFS shortest-path parent. Because the impact DFS skips
                    // unowned references into scene objects (see CalculateImpact), such an item is reached
                    // only via its owned hierarchy and is finalized in post-order, before its parent — so
                    // this path is not normally taken. It is retained for conservation safety: were a
                    // referee ever finalized out of order, dropping the impact here would lose bytes from
                    // the snapshot total. Rather than jumping straight to the DFS root, walk UP the
                    // path-parent chain to the nearest ancestor that is still ON THE STACK (FoundForImpact
                    // && !FullyDistributedImpact, so it WILL re-bubble this impact), falling back to the DFS
                    // root (always on the stack, since current is in its subtree). This preserves the
                    // hierarchy; it is conservation-neutral (same root) either way. Handled here, inside the
                    // Count > 0 block, so the else branch below need not treat these now virtually-
                    // unreferenced items as roots.
#if DEBUG_VALIDATION
                    // Only scene/prefab hierarchy items should ever need this redirect, and the redirected
                    // item must share the EXACT same root as the finalized referee it pointed at. Anything
                    // else indicates a NEW upstream ordering/attribution bug — alert so it isn't silently
                    // masked by the redirect.
                    Checks.IsTrue(
                        current.Id is SourceIndex.SourceId.NativeObject
                        && snapshot.TypeDescriptions.UnifiedTypeInfoNative[snapshot.NativeObjects.NativeTypeArrayIndex[current.Index]].IsSceneObjectType,
                        $"Unexpected impact redirect for non-scene-object {current} (finalized referee {firstStruckReferee}); only scene/prefab hierarchy items are expected to redirect.");
                    if (firstStruckReferee.Valid)
                        Checks.IsTrue(
                            SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_ShortestPathInfo, firstStruckReferee).Root == currentPathStep.Root,
                            $"Impact redirect: {current} and its finalized referee {firstStruckReferee} do not share the same root.");
#endif
                    // Find the nearest ancestor that is still on the stack; default to the DFS root.
                    var attributeTo = currentPathStep.Root;
                    var ancestor = currentPathStep.Parent;
                    while (ancestor.Valid)
                    {
                        ref readonly var ancestorMarker = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, ancestor);
                        if (ancestorMarker.FoundForImpact && !ancestorMarker.FullyDistributedImpact)
                        {
                            attributeTo = ancestor;
                            break;
                        }
                        ref readonly var ancestorStep = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_ShortestPathInfo, ancestor);
                        if (!ancestorStep.Parent.Valid)
                            break;
                        ancestor = ancestorStep.Parent;
                    }
#if DEBUG_VALIDATION
                    static void GetTypeAndName(CachedSnapshot snapshot, SourceIndex sourceIndex, out string typeName, out string objectName)
                    {
                        objectName = sourceIndex.GetName(snapshot);
                        typeName = sourceIndex.Id switch {
                            SourceIndex.SourceId.NativeObject => snapshot.NativeTypes.TypeName[snapshot.NativeObjects.NativeTypeArrayIndex[sourceIndex.Index]],
                            SourceIndex.SourceId.Scene => "Scene",
                            SourceIndex.SourceId.Prefab => "Prefab",
                            SourceIndex.SourceId.GCHandleIndex => "GCHandle",
                            SourceIndex.SourceId.ManagedType => "ManagedType",
                            SourceIndex.SourceId.ManagedObject => snapshot.TypeDescriptions.TypeDescriptionName[snapshot.CrawledData.ManagedObjects[sourceIndex.Index].ITypeDescription],
                            _ => sourceIndex.Id.ToString()
                            };
                    }
                    GetTypeAndName(snapshot, current, out var typeName, out var objectName);
                    GetTypeAndName(snapshot, attributeTo, out var attributeToTypeName, out var attributeToObjectName);
                    var mainMessage = $"Scene Object Impact reattribution: the {typeName} named \"{objectName}\" Index {current} had no referees and its impact went to -> {attributeToTypeName} named \"{attributeToObjectName}\" Index {attributeTo}";
                    // add one line per parent in the path for easier debugging of the hierarchy in the log
                    var pathParent = currentPathStep.Parent;
                    while (pathParent.Valid)
                    {
                        GetTypeAndName(snapshot, pathParent, out var parentTypeName, out var parentObjectName);
                        mainMessage += $"\n    Parent: {parentTypeName} named \"{parentObjectName}\" Index {pathParent}";
                        ref readonly var parentStep = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_ShortestPathInfo, pathParent);
                        pathParent = parentStep.Parent;
                    }
                    Debug.Log(mainMessage);
#endif
                    ref var attributeToImpact = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Impact, attributeTo);
                    attributeToImpact += currentImpact;
                }
                else if (data.UnownedReferences.Count == 1)
                {
                    var referee = data.UnownedReferences[0];
                    ref var fullyOwningRefereeImpact = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Impact, referee);
                    fullyOwningRefereeImpact += currentImpact;
                }
                else
                {
                    // split impact pro rata among all referees, both the formerly exclusive owned and the shared parts
                    var totalImpact = currentImpact.TotalImpact;
                    var sharedProRata = totalImpact.Divide((ulong)data.UnownedReferences.Count, out var remainder);
                    // using ulong for easier checks against remainder
                    var i = 0UL;
                    foreach (var reference in data.UnownedReferences)
                    {
                        ref var sharedOwnershipRefereeImpact = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Impact, reference);
                        sharedOwnershipRefereeImpact.SharedInTotal += totalImpact;
                        // distribute the remainder one byte at a time on a first comes first served basis
                        var splitRemainderFormerExclusive = new NativeRootSize
                        {
                            NativeSize = new MemorySize(remainder.NativeSize.Committed > i ? 1UL : 0, remainder.NativeSize.Resident > i ? 1UL : 0),
                            ManagedSize = new MemorySize(remainder.ManagedSize.Committed > i ? 1UL : 0, remainder.ManagedSize.Resident > i ? 1UL : 0),
                            GfxSize = new MemorySize(remainder.GfxSize.Committed > i ? 1UL : 0, remainder.GfxSize.Resident > i ? 1UL : 0),
                        };
                        sharedOwnershipRefereeImpact.SharedProRata += sharedProRata + splitRemainderFormerExclusive;
                        ++i;
                    }
                }
            }
            else
            {
                if (current.Id is SourceIndex.SourceId.Scene)
                {
                    // This was a rooting Scene, register it as fully processed root regardless of
                    // impact as we'd also want to list empty scenes to recreate the loaded scene state.
                    data.Roots.Push(current);
                }
                else if (current.Id is SourceIndex.SourceId.GCHandleIndex or SourceIndex.SourceId.ManagedType or SourceIndex.SourceId.NativeRootReference)
                {
                    // This was a managed type, a GCHandle rooted object, or a rooting
                    // non-object-associated Native Root, which is fine to not have any references
                    // pointed at it.
                    // Register it as fully processed root, but only if it has any impact at all.
                    if (currentImpact.TotalImpact.SumUp().Committed > 0)
                        data.Roots.Push(current);
                }
                else if (current.Id is SourceIndex.SourceId.NativeObject && snapshot.NativeTypes.AssetBundleIdx == snapshot.NativeObjects.NativeTypeArrayIndex[current.Index])
                {
                    // This was an unreferenced asset bundle acting as a root.
                    data.Roots.Push(current);
                }
                else if (current.Id is SourceIndex.SourceId.NativeObject && snapshot.NativeObjects.Flags[current.Index].HasFlag(ObjectFlags.IsDontDestroyOnLoad))
                {
                    // This was a DontDestroyOnLoad marked object, acting as a root.
                    data.Roots.Push(current);
                }
                else if (current.Id is SourceIndex.SourceId.NativeObject && snapshot.NativeObjects.Flags[current.Index].HasFlag(ObjectFlags.IsPersistent))
                {
                    // This was a loaded and no longer used asset, acting as a root.
                    data.Roots.Push(current);
                }
                else if (current.Id is SourceIndex.SourceId.NativeObject && snapshot.TypeDescriptions.UnifiedTypeInfoNative[snapshot.NativeObjects.NativeTypeArrayIndex[current.Index]].IsAssetObjectType)
                {
                    // This was a leaked dynamically created asset, acting as a root.
                    data.Roots.Push(current);
                }
                else if (current.Id is SourceIndex.SourceId.Prefab && ownershipBucket is OwnershipBucket.UnreferencedPrefabs or OwnershipBucket.DontDestroyOnLoadObjects)
                {
                    // This is an unreferenced prefab but that is to be expected for this bucket.
                    data.Roots.Push(current);
                }
                else if (current.Id is SourceIndex.SourceId.NativeObject &&
                         ownershipBucket is OwnershipBucket.BrokenScriptReferences)
                {
                    // A native object with a broken script reference, rooted to itself; expected for this bucket.
                    data.Roots.Push(current);
                }
                else
                {
                    // Did not expect this to be a root. Did something go wrong?
                    throw new InvalidOperationException($"{current} is an abandoned Leaf!");
                }
            }
        }

        /// <summary>
        /// Finds all registered referees to
        /// <see cref="SourceIndex.SourceId.ManagedObject"/>
        /// <see cref="SourceIndex.SourceId.NativeObject"/>
        /// <see cref="SourceIndex.SourceId.Prefab"/>
        /// that have been found but not yet fully processed for impact attribution
        /// and stores them in the <see cref="CachedProcessingData.UnownedReferences"/>.
        /// </summary>
        /// <param name="snapshot"></param>
        /// <param name="data"></param>
        /// <param name="referencedItem"></param>
        void FindRefereesForImpactAttribution(CachedSnapshot snapshot, ref CachedProcessingData data, SourceIndex referencedItem)
        {
            ObjectConnection.GetAllReferencingObjects(snapshot, referencedItem, null, data.Connections.References, ObjectConnection.UnityObjectReferencesSearchMode.Raw, ignoreRepeatedManagedReferences: true);

            foreach (var reference in data.Connections.References)
            {
                ref readonly var referenceMarker = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, reference);

                if (!referenceMarker.FullyDistributedImpact) // && referenceMarker.FoundForRoot // include this check to keep attribution strictly within the rooting bucket instead of sharing impact into later buckets
                {
                    data.UnownedReferences.Push(reference);
                }
            }

            if (data.Connections.References.Count > CachedProcessingData.ConnectionCache.MaxCapacity)
            {
                data.Connections.References = new HashSet<SourceIndex>(CachedProcessingData.ConnectionCache.DefaultCapacity);
            }
        }

        [Conditional("VALIDATE_ROOT_AND_IMPACT")]
        void ValidateMarkers(CachedSnapshot snapshot)
        {
            var success = true;
            for (var rootGroupIndex = 0L; rootGroupIndex < m_Marker.SectionCount; rootGroupIndex++)
            {
                var section = m_Marker[rootGroupIndex];
                var markerCount = m_Marker.Count(rootGroupIndex);
                for (var markerIdx = 0L; markerIdx < markerCount; markerIdx++)
                {
                    var marker = section[markerIdx];
                    if (marker.FoundForRoot && !marker.FullyDistributedImpact)
                    {
                        var sourceIndex = rootGroupIndex switch
                        {
                            SourceIndexToRootAndImpactInfoMapper.IndexInNestedArrayForNativeRootReferences
                                => new SourceIndex(SourceIndex.SourceId.NativeRootReference, markerIdx),
                            SourceIndexToRootAndImpactInfoMapper.IndexInNestedArrayForScenes
                                => new SourceIndex(SourceIndex.SourceId.Scene, markerIdx),
                            SourceIndexToRootAndImpactInfoMapper.IndexInNestedArrayForPrefabs
                                => new SourceIndex(SourceIndex.SourceId.Prefab, markerIdx),
                            SourceIndexToRootAndImpactInfoMapper.IndexInNestedArrayForNativeObjects
                                => new SourceIndex(SourceIndex.SourceId.NativeObject, markerIdx),
                            SourceIndexToRootAndImpactInfoMapper.IndexInNestedArrayForNativeAllocations
                                => new SourceIndex(SourceIndex.SourceId.NativeAllocation, markerIdx),
                            SourceIndexToRootAndImpactInfoMapper.IndexInNestedArrayForManagedTypes
                                => new SourceIndex(SourceIndex.SourceId.ManagedType, markerIdx),
                            SourceIndexToRootAndImpactInfoMapper.IndexInNestedArrayForGCHandles
                                => new SourceIndex(SourceIndex.SourceId.GCHandleIndex, markerIdx),
                            SourceIndexToRootAndImpactInfoMapper.IndexInNestedArrayForMangedObjects
                                => new SourceIndex(SourceIndex.SourceId.ManagedObject, markerIdx),
                            _ => throw new NotImplementedException(),
                        };
                        if (sourceIndex.Id is SourceIndex.SourceId.NativeObject)
                        {
                            // This could be an AssetBundle, which is fine to be a root without any impact attributed to as it could all be used.
                            if (snapshot.NativeTypes.AssetBundleIdx == snapshot.NativeObjects.NativeTypeArrayIndex[sourceIndex.Index])
                                continue;
                        }
                        success = false;
                        Debug.LogError($"Marker validation failed for {sourceIndex}: Found marker that has not fully distributed impact.");
                    }
                }
            }
            if (!success)
                throw new InvalidOperationException("Marker validation failed after impact calculation: Found markers that have not fully distributed impact.");
        }

        void RootNativeObjectAssociatedAllocations(CachedSnapshot snapshot)
        {
            ref readonly var rootStepInfoForNativeObjects = ref
                SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_ShortestPathInfo,
                    SourceIndex.SourceId.NativeObject);
            ref readonly var rootStepInfoForAllocations = ref
                SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_ShortestPathInfo,
                    SourceIndex.SourceId.NativeAllocation);
            ref readonly var markerInfoForAllocations = ref
                SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_Marker,
                    SourceIndex.SourceId.NativeAllocation);
            for (long i = 0; i < snapshot.NativeAllocations.Count; i++)
            {
                var rootId = snapshot.NativeAllocations.RootReferenceId[i];
                if (rootId < NativeRootReferenceEntriesCache.FirstValidRootId)
                {
                    // Unrooted native allocation are ignored here as we only care about allocations
                    // of Native Objects and want to avoid grabbing and invalid root index here.
                    // Unrooted allocations are getting either tracked as rooted to a managed reference or handled in HandleUnreferenceUnrootedAllocations
                    continue;
                }
                var nativeRootIndex = snapshot.ProcessedNativeRoots.RootIdToMappedIndex(rootId);
                var nativeObjectOrRootIndex = snapshot.ProcessedNativeRoots.Data[nativeRootIndex].NativeObjectOrRootIndex;
                if (nativeObjectOrRootIndex.Id is SourceIndex.SourceId.NativeObject)
                {
                    // Native Object associated allocations are not handled individually for rooting or impact attribution,
                    // their memory is attributed via ProcessedNativeRoots instead.
                    // That however means that their shortest path info is not filled in the main processing loop,
                    // so we need to fill it in here for when a user selects a loose allocation.
                    var parent = rootStepInfoForNativeObjects[nativeObjectOrRootIndex.Index];
                    rootStepInfoForAllocations[i] = new ShortestRootPathInfo(parent.Root, nativeObjectOrRootIndex, parent.Depth + 1);

                }
                else if (nativeObjectOrRootIndex.Id is SourceIndex.SourceId.None)
                {
                    // This native allocation was reported but did not map to a captured memory region
                    // (likely due to timing issues during capture, i.e. this allocation was created or freed during the capture
                    // process, likely for a native object)
                    if (snapshot.NativeObjects.RootReferenceIdToIndex.TryGetValue(rootId, out var nativeObjectIndex))
                    {
                        // If we found a native object index, we can use it to fill in the root step info.
                        var parent = rootStepInfoForNativeObjects[nativeObjectIndex];
                        var nativeObjectSourceIndex = new SourceIndex(SourceIndex.SourceId.NativeObject, nativeObjectIndex);
                        rootStepInfoForAllocations[i] = new ShortestRootPathInfo(parent.Root, nativeObjectSourceIndex, parent.Depth + 1);
                    }
                    else
                    {
                        // alternatively, fall back onto the NativeRootReference
                        var rootSourceIndex = new SourceIndex(SourceIndex.SourceId.NativeRootReference, nativeRootIndex);
                        ref var rootStepParent = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_ShortestPathInfo, rootSourceIndex);
                        if (!rootStepParent.Valid)
                        {
                            rootStepParent = new ShortestRootPathInfo(rootSourceIndex, default, 0);
#if VALIDATE_ROOT_AND_IMPACT
                            ref var markerParent = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, rootSourceIndex);
                            markerParent.FoundForRoot = true;
                            markerParent.FoundForImpact = true;
                            markerParent.FullyDistributedImpact = true;
#endif
                        }
                        rootStepInfoForAllocations[i] = new ShortestRootPathInfo(rootSourceIndex, rootSourceIndex, rootStepParent.Depth + 1);
                    }
                }

#if VALIDATE_ROOT_AND_IMPACT
                if (nativeObjectOrRootIndex.Id is SourceIndex.SourceId.NativeObject or SourceIndex.SourceId.None)
                {
                    // We also fill in the marker info for the later validation checks.
                    // Only for allocations associated with native objects or those without allocations mapped to reported memory regions.
                    ref var mark = ref markerInfoForAllocations[i];
                    mark.FoundForRoot = true;
                    mark.FoundForImpact = true;
                    mark.FullyDistributedImpact = true;
                }
#endif
            }
        }

        [Conditional("VALIDATE_ROOT_AND_IMPACT")]
        void Validate(CachedSnapshot snapshot)
        {
            for (var rootGroupIndex = 0L; rootGroupIndex < m_ShortestPathInfo.SectionCount; rootGroupIndex++)
            {
                switch (rootGroupIndex)
                {
                    case SourceIndexToRootAndImpactInfoMapper.IndexInNestedArrayForNativeRootReferences:
                        for (long i = 0; i < snapshot.NativeRootReferences.Count; i++)
                        {
                            var rootId = snapshot.NativeRootReferences.Id[i];
                            if (rootId < NativeRootReferenceEntriesCache.FirstValidRootId)
                                continue;
                            var processedRootIndex = snapshot.ProcessedNativeRoots.RootIdToMappedIndex(rootId);
                            var rootSourceIndex = snapshot.ProcessedNativeRoots.Data[processedRootIndex].NativeObjectOrRootIndex;
                            if (rootSourceIndex.Id is SourceIndex.SourceId.NativeRootReference)
                            {
                                var rootStep = m_ShortestPathInfo[rootGroupIndex][rootSourceIndex.Index];
                                var mark = m_Marker[rootGroupIndex][rootSourceIndex.Index];
                                Debug.Assert(rootStep.Valid, "Native Root Reference information should be valid");
                                Debug.Assert(rootStep.IsRoot, "Native Root Reference information should be a root");
                                Debug.Assert(mark.FoundForRoot, "Native Root Reference should be marked as found for root");
                                Debug.Assert(mark.FoundForImpact, "Native Root Reference should be marked as found for impact");
                                Debug.Assert(mark.FullyDistributedImpact, "Native Root Reference should impact should be fully distributed");
                            }
                        }
                        break;
                    case SourceIndexToRootAndImpactInfoMapper.IndexInNestedArrayForScenes:
                        for (long i = 0; i < snapshot.SceneRoots.SceneCount; i++)
                        {
                            var rootStep = m_ShortestPathInfo[rootGroupIndex][i];
                            var mark = m_Marker[rootGroupIndex][i];
                            Debug.Assert(rootStep.Valid, "Scene root information should be valid");
                            Debug.Assert(rootStep.IsRoot, "Scene root information should be a root");
                            Debug.Assert(mark.FoundForRoot, "Scene root should be marked as found for root");
                            Debug.Assert(mark.FoundForImpact, "Scene root should be marked as found for impact");
                            Debug.Assert(mark.FullyDistributedImpact, "Scene root should impact should be fully distributed");
                        }
                        break;
                    case SourceIndexToRootAndImpactInfoMapper.IndexInNestedArrayForPrefabs:
                        ref readonly var rootStepInfoForPrefabs = ref m_ShortestPathInfo[rootGroupIndex];
                        ref readonly var markerInfoForPrefabs = ref m_Marker[rootGroupIndex];
                        for (long i = 0; i < snapshot.SceneRoots.PrefabRootCount; i++)
                        {
                            ref readonly var rootStep = ref rootStepInfoForPrefabs[i];
                            ref readonly var mark = ref markerInfoForPrefabs[i];
                            Debug.Assert(rootStep.Valid, $"Prefab root {i} information should be valid");
                            // Debug.Assert(rootStep.Valid, "Prefab root information should be valid");
                            Debug.Assert(rootStep.IsRoot || (rootStep.Root.Valid && rootStep.Parent.Valid),
                            "Prefab should either be a root or have a root and a parent");
                            Debug.Assert(mark.FoundForRoot, "Prefab should be marked as found for root");
                            Debug.Assert(mark.FoundForImpact, "Prefab should be marked as found for impact");
                            Debug.Assert(mark.FullyDistributedImpact, "Prefab should impact should be fully distributed");
                        }
                        break;
                    case SourceIndexToRootAndImpactInfoMapper.IndexInNestedArrayForNativeObjects:
                        ref readonly var rootStepInfoForNativeObjects = ref m_ShortestPathInfo[rootGroupIndex];
                        ref readonly var markerInfoForNativeObjects = ref m_Marker[rootGroupIndex];
                        for (long i = 0; i < snapshot.NativeObjects.Count; i++)
                        {
                            ref readonly var rootStep = ref rootStepInfoForNativeObjects[i];
                            ref readonly var mark = ref markerInfoForNativeObjects[i];
                            Debug.Assert(rootStep.Valid, "Native object root information should be valid");
                            Debug.Assert(rootStep.IsRoot || (rootStep.Root.Valid && rootStep.Parent.Valid),
                            "Native object should either be a root or have a root and a parent");
                            Debug.Assert(mark.FoundForRoot, "Native object should be marked as found for root");
                            Debug.Assert(mark.FoundForImpact, "Native object should be marked as found for impact");
                            Debug.Assert(mark.FullyDistributedImpact, "Native object impact should be fully distributed");

                            if (!rootStep.Valid
                                || !(rootStep.IsRoot || (rootStep.Root.Valid && rootStep.Parent.Valid))
                                || !mark.FoundForRoot
                                || !mark.FoundForImpact
                                || !mark.FullyDistributedImpact)
                            {
                                // Only build this string if it actually failed.
                                Debug.LogError($"Native object failed validation: (index: {i}, name: {snapshot.NativeObjects.ObjectName[i]}, type: {snapshot.NativeTypes.TypeName[snapshot.NativeObjects.NativeTypeArrayIndex[i]]}, flags: {snapshot.NativeObjects.Flags[i]}, hideFlags: {snapshot.NativeObjects.HideFlags[i]})");
                                if (snapshot.NativeObjects.ManagedObjectIndex[i] >= 0)
                                {
                                    var managedIndex = snapshot.NativeObjects.ManagedObjectIndex[i];
                                    var moi = snapshot.CrawledData.ManagedObjects[managedIndex];
                                    Debug.LogError($"Native object failed validation Managed side: (index: {managedIndex}, type: {snapshot.TypeDescriptions.TypeDescriptionName[moi.ITypeDescription]})");
                                }
                            }
                        }
                        break;
                    case SourceIndexToRootAndImpactInfoMapper.IndexInNestedArrayForNativeAllocations:
                        ref readonly var rootStepInfoForAllocations = ref m_ShortestPathInfo[rootGroupIndex];
                        ref readonly var markerInfoForAllocations = ref m_Marker[rootGroupIndex];
                        for (long i = 0; i < snapshot.NativeAllocations.Count; i++)
                        {
                            ref readonly var rootStep = ref rootStepInfoForAllocations[i];
                            ref readonly var mark = ref markerInfoForAllocations[i];
                            var rootId = snapshot.NativeAllocations.RootReferenceId[i];
                            if (rootId < NativeRootReferenceEntriesCache.FirstValidRootId)
                            {
                                // Unrooted native allocation, some of which might be rooted via a managed reference to them and added to the RootedUnknownMemory tally.
                                // Ignore here.
                                continue;
                            }

                            if (!rootStep.Valid && snapshot.HasSystemMemoryRegionsInfo)
                            {
                                // No root step was computed for this rooted allocation. When capturing system
                                // regions an allocation can fall outside all of them and so be excluded from the
                                // memory map, in which case a missing root step is expected (a timing mismatch).
                                // Verify that by checking whether the address actually falls inside a reported
                                // system region — if it does NOT, this is the benign outside-regions case.
                                var address = snapshot.NativeAllocations.Address[i];
                                // Binary search finds the region whose start address is the largest that is still
                                // <= the allocation address; that region (if any) is the only candidate to contain it.
                                var searchIndex = DynamicArrayAlgorithms.BinarySearch(snapshot.SystemMemoryRegions.RegionAddress, address);
                                bool isInSystemRegion;
                                if (searchIndex >= 0)
                                {
                                    isInSystemRegion = true; // exact match on a region start address
                                }
                                else
                                {
                                    var insertionPoint = ~searchIndex;
                                    if (insertionPoint > 0)
                                    {
                                        var prevRegion = insertionPoint - 1;
                                        var regionStart = snapshot.SystemMemoryRegions.RegionAddress[prevRegion];
                                        var regionSize = snapshot.SystemMemoryRegions.RegionSize[prevRegion];
                                        isInSystemRegion = address < regionStart + regionSize;
                                    }
                                    else
                                    {
                                        isInSystemRegion = false; // address is before all known regions
                                    }
                                }

                                if (isInSystemRegion)
                                {
                                    // The allocation IS inside a reported system region, so it would normally have
                                    // produced a memory-map entry and a root step. There is ONE known-benign
                                    // exception: the profiler's OWN allocations during a live self-capture. The
                                    // capture is not atomic, so a profiler buffer created/resized while the snapshot
                                    // is being taken can sit inside a region yet never surface as its own entry in
                                    // the (non-overlapping) memory map — ProcessNativeAllocation never accumulates it
                                    // (its AllocationSizes stays 0) and it gets no root step. Ignore ONLY that exact
                                    // case: not in the memory map AND owned by a profiling-area root. Anything else —
                                    // an allocation that WAS in the map yet lacks a root step (a real consumption
                                    // bug), or a non-profiling allocation missing from the map (unexplained) — keeps
                                    // the error so genuine processing bugs are not masked.
                                    if (!snapshot.NativeRootReferences.IdToIndex.TryGetValue(rootId, out var rootIdx))
                                        rootIdx = NativeRootReferenceEntriesCache.InvalidRootIndex;
                                    var areaName = rootIdx >= NativeRootReferenceEntriesCache.FirstValidRootIndex ? snapshot.NativeRootReferences.AreaName[rootIdx] : "Unknown";
                                    var objectName = rootIdx >= NativeRootReferenceEntriesCache.FirstValidRootIndex ? snapshot.NativeRootReferences.ObjectName[rootIdx] : "Unknown";

                                    var notInMemoryMap = snapshot.ProcessedNativeRoots.AllocationSizes[i].Committed == 0;
                                    var isProfilingAreaAllocation = areaName != null && areaName.Contains("Profiling");
                                    if (!(notInMemoryMap && isProfilingAreaAllocation))
                                    {
                                        Debug.LogError($"Native Allocation in a reported system region has invalid root step: (index: {i}, rootId: {rootId}, AreaName: {areaName}, ObjectName: {objectName}, inMemoryMap: {!notInMemoryMap}, address: 0x{string.Format("{0:X16}", address)})");
                                    }
                                }
                                // else: address outside all known system regions — confirmed timing mismatch, silently skip.
                                continue;
                            }

                            // Root step is valid; all remaining properties must hold.
                            // Log an error only for genuinely unexpected validation failures.
                            if (rootStep.IsRoot
                                || !rootStep.Root.Valid
                                || !rootStep.Parent.Valid
                                || !mark.FoundForRoot
                                || !mark.FoundForImpact
                                || !mark.FullyDistributedImpact)
                            {
                                if (!snapshot.NativeRootReferences.IdToIndex.TryGetValue(rootId, out var rootIndex))
                                    rootIndex = NativeRootReferenceEntriesCache.InvalidRootIndex;
                                Debug.LogError($"Native Allocation failed validation: (index: {i}, rootId: {rootId}, AreaName: {(rootIndex >= NativeRootReferenceEntriesCache.FirstValidRootIndex ? snapshot.NativeRootReferences.AreaName[rootIndex] : "Unknown")}, ObjectName: {(rootIndex >= NativeRootReferenceEntriesCache.FirstValidRootIndex ? snapshot.NativeRootReferences.ObjectName[rootIndex] : "Unknown")}, address: 0x{string.Format("{0:X16}", snapshot.NativeAllocations.Address[i])})");
                                continue;
                            }
                        }
                        break;
                    case SourceIndexToRootAndImpactInfoMapper.IndexInNestedArrayForManagedTypes:
                        ref readonly var rootStepInfoForManagedTypes = ref m_ShortestPathInfo[rootGroupIndex];
                        ref readonly var markerInfoForManagedTypes = ref m_Marker[rootGroupIndex];
                        for (long i = 0; i < snapshot.TypeDescriptions.Count; i++)
                        {
                            ref readonly var mark = ref markerInfoForManagedTypes[i];
                            if (mark.FoundForRoot)
                            {
                                ref readonly var rootStep = ref rootStepInfoForManagedTypes[i];
                                Debug.Assert(rootStep.Valid, "Managed type root information should be valid");
                                Debug.Assert(rootStep.IsRoot, "Managed type should be a root");
                                Debug.Assert(mark.FoundForImpact, "Managed type should be marked as found for impact");
                                Debug.Assert(mark.FullyDistributedImpact, "Managed type impact should be fully distributed");
                            }
                        }
                        break;
                    case SourceIndexToRootAndImpactInfoMapper.IndexInNestedArrayForGCHandles:
                        ref readonly var rootStepInfoForGCHandles = ref m_ShortestPathInfo[rootGroupIndex];
                        ref readonly var markerInfoForGCHandles = ref m_Marker[rootGroupIndex];
                        ref readonly var rootStepInfoForGCHandleHeldObjects = ref m_ShortestPathInfo[SourceIndexToRootAndImpactInfoMapper.IndexInNestedArrayForMangedObjects];
                        ref readonly var markerInfoForGCHandleHeldObjects = ref m_Marker[SourceIndexToRootAndImpactInfoMapper.IndexInNestedArrayForMangedObjects];

                        var crawled = snapshot.CrawledData.Crawled;
                        for (long i = 0; i < snapshot.GcHandles.UniqueCount; i++)
                        {
                            ref readonly var gcRootStep = ref rootStepInfoForGCHandles[i];
                            ref readonly var gcMark = ref markerInfoForGCHandles[i];
                            if (gcMark.FoundForRoot)
                            {
                                Debug.Assert(gcRootStep.Valid, "GCHandle root information should be valid");
                                Debug.Assert(gcRootStep.IsRoot, "GCHandle should be a root");
                                Debug.Assert(gcMark.FoundForRoot, "GCHandle should be marked as found for root");
                                Debug.Assert(gcMark.FoundForImpact, "GCHandle should be marked as found for impact");
                                Debug.Assert(gcMark.FullyDistributedImpact, "GCHandle impact should be fully distributed");

                                var managedObjectIndexForValidation = snapshot.CrawledData.ResolveManagedObjectIndexForGCHandle(i);
                                if (managedObjectIndexForValidation == ManagedData.InvalidObjectIndex || !crawled)
                                    continue;
                                ref readonly var manageObjectMark = ref markerInfoForGCHandleHeldObjects[managedObjectIndexForValidation];
                                ref readonly var manageObjectRootStep = ref rootStepInfoForGCHandleHeldObjects[managedObjectIndexForValidation];
                                Debug.Assert(manageObjectRootStep.Valid, "Managed object root information should be valid");
                                Debug.Assert(!manageObjectRootStep.IsRoot, "Managed object should not be a root, the GCHandle is the root");
                                Debug.Assert(manageObjectMark.FoundForRoot, "Managed object should be marked as found for root");
                                Debug.Assert(manageObjectMark.FoundForImpact, "Managed object should be marked as found for impact");
                                Debug.Assert(manageObjectMark.FullyDistributedImpact, "Managed object impact should be fully distributed");

                                var gcHandleIndex = new SourceIndex(SourceIndex.SourceId.GCHandleIndex, i);
                                Debug.Assert(manageObjectRootStep.Root == gcHandleIndex, "Managed object should be rooted to the GCHandle");
                                Debug.Assert(manageObjectRootStep.Parent == gcHandleIndex, "Managed object should have the GCHandle as its parent");
                            }
                            else
                            {
                                Debug.Assert(!gcRootStep.Valid, "If the target of the GCHandle was found as referenced by something else, the GCHandle root information should be valid");
                                Debug.Assert(!gcRootStep.IsRoot, "If the target of the GCHandle was found as referenced by something else, the GCHandle should not be a root");
                                Debug.Assert(gcMark.FoundForImpact, "If the target of the GCHandle was found as referenced by something else, the GCHandle should be counted as found for impact");
                                Debug.Assert(gcMark.FullyDistributedImpact, "If the target of the GCHandle was found as referenced by something else, the GCHandle should be counted as fully distributed impact");
                            }
                        }
                        break;
                    case SourceIndexToRootAndImpactInfoMapper.IndexInNestedArrayForMangedObjects:
                        ref readonly var rootStepInfoForManagedObjects = ref m_ShortestPathInfo[rootGroupIndex];
                        ref readonly var markerInfoForManagedObjects = ref m_Marker[rootGroupIndex];
                        for (long i = 0; i < snapshot.CrawledData.ManagedObjects.Count; i++)
                        {
                            ref readonly var rootStep = ref rootStepInfoForManagedObjects[i];
                            ref readonly var mark = ref markerInfoForManagedObjects[i];
                            Debug.Assert(rootStep.Valid, "Managed object root information should be valid");
                            Debug.Assert(!rootStep.IsRoot, "Managed object should not be a root itself");
                            Debug.Assert(rootStep.Root.Valid && rootStep.Parent.Valid, "Managed object should have a root and a parent");
                            Debug.Assert(mark.FoundForRoot, "Managed object should be marked as found for root");
                            Debug.Assert(mark.FoundForImpact, "Managed object should be marked as found for impact");
                            Debug.Assert(mark.FullyDistributedImpact, "Managed object impact should be fully distributed");
                        }
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
        }
    }
}

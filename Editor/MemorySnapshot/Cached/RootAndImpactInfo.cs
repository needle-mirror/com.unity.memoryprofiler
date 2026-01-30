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
        NestedDynamicArray<Marker> m_Marker;

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

            m_OwnedChildList = new DynamicArray<SourceIndex>(0, initialCount, Allocator.Persistent, true);

            m_UnownedChildList = new DynamicArray<SourceIndex>(0, 3000, Allocator.Persistent, true);
        }

        public void Dispose()
        {
            m_ShortestPathInfo.Dispose();
            m_RootPathOwnedReferencesLookup.Dispose();
            m_UnownedReferencesLookup.Dispose();
            m_Impact.Dispose();
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

        public IEnumerator<EnumerationStatus> ReadOrProcess(CachedSnapshot snapshot, EnumerationStatus status)
        {
            if (m_Processed || !MemoryProfilerSettings.EnableRootsAndImpact || !snapshot.HasSceneRootsAndAssetbundles)
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
                CrawlNativeRootAllocations(snapshot, ref m_ProcessingData);
                CrawlUnrootedNativeObjects(snapshot, ref m_ProcessingData);
            }
            finally
            {
                m_Roots = m_ProcessingData.TakeOwnershipOfRoots();
                m_ProcessingData.Dispose();
            }

            yield return status.IncrementStep("Process Paths From Root: Validating and Finalizing");
            RootNativeObjectAssociatedAllocations(snapshot);
            HandleUnreferenceUnrootedAllocations(snapshot);
            Validate(snapshot);
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
                if (types.HasAnyStaticField(i))
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
                            ref var prefabMarker = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, prefabIndex);
                            // We only care about these if they are not yet marked as found so that we don't parse them multiple times
                            if (prefabMarker.FoundForRoot)
                                continue;
                            prefabMarker.FoundForRoot = true;
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
                            if (nativeObjects.HideFlags[i].HasFlag(HideFlags.DontUnloadUnusedAsset)
                                && !nativeObjects.HideFlags[i].HasFlag(NativeObjectEntriesCache.NativeDontAllowDestructionFlag)
                                && !nativeObjects.HideFlags[i].HasFlag(HideFlags.HideAndDontSave))
                            {
                                if (nativeObjects.NativeTypeArrayIndex[i] == snapshot.NativeTypes.GameObjectIdx) // Only log for GameObjects to avoid logging repeat messages for each component
                                    Debug.LogWarning($"The non-prefab GameObject named: {nativeObjects.ObjectName[i]} was marked with HideFlags.DontUnloadUnusedAsset while not being an Asset. That's not supported and the flag will be ignored by the Asset GC.");

                                // Marking a non-prefab Scene Object as unloadable via the HideFlag doesn't keep it from getting unloaded with the scene,
                                // as that hideflag is not checked on scene objects during a scene unload, so for rooting purposes it doesn't matter for these
                                // to fall through the cracks here.
                                continue;
                            }
                        }
                    }
                    ref var rootStep = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_ShortestPathInfo, dontDestroyOnLoadObjectIndex);
                    rootStep = new ShortestRootPathInfo(root: dontDestroyOnLoadObjectIndex, parent: default, 0);
                    data.RootsInCurrentOwnershipBucket.Push(dontDestroyOnLoadObjectIndex);
                    ref var marker = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, dontDestroyOnLoadObjectIndex);
                    marker.FoundForRoot = true;
                }
            }
            Crawl(snapshot, ref data, OwnershipBucket.DontDestroyOnLoadObjects);
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
            // The impact for their assets that ARE used would then be shared with all rooting references to them,
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
            ref readonly var rootStepsNativeObject = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_ShortestPathInfo, SourceIndex.SourceId.NativeObject);

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

                            markerNativeObjects[index].FoundForRoot = true;
                            rootStepsNativeObject[index] = new ShortestRootPathInfo(nativeRoot.NativeObjectOrRootIndex, default, 0);
                            data.RootsInCurrentOwnershipBucket.Push(nativeRoot.NativeObjectOrRootIndex);

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
            Crawl(snapshot, ref data, OwnershipBucket.NativeObjectRoots);
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

            ref readonly var rootStepsGCHandles = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_ShortestPathInfo, SourceIndex.SourceId.GCHandleIndex);

            var count = snapshot.GcHandles.UniqueCount;
            for (int i = 0; i < count; i++)
            {
                var gcHandleIndex = new SourceIndex(SourceIndex.SourceId.GCHandleIndex, i);
                // GCHandle index maps to the first managed object indices
                if (!markerManagedObjects[gcHandleIndex.Index].FoundForRoot)
                {
                    // temporarily mark managed object as found so other GCHandle roots don't try to own it
                    markerManagedObjects[gcHandleIndex.Index].FoundForRoot = true;

                    // This is a floating GCHandle, i.e. no one has claimed this object
                    rootStepsGCHandles[i] = new ShortestRootPathInfo(root: gcHandleIndex, parent: default, depth: 0);
                    data.RootsInCurrentOwnershipBucket.Push(gcHandleIndex);
                    markerGCHandles[gcHandleIndex.Index].FoundForRoot = true;
                }
                else
                {
                    markerGCHandles[gcHandleIndex.Index].FoundForImpact = true;
                    markerGCHandles[gcHandleIndex.Index].FullyDistributedImpact = true;
                }
            }
            Crawl(snapshot, ref data, OwnershipBucket.GCHandleRoots);
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

        void CrawlUnrootedNativeObjects(CachedSnapshot snapshot, ref CachedProcessingData data)
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
                    }
                }
                ++nativeObjectIndex;
            }
            Crawl(snapshot, ref data, OwnershipBucket.UnrootedNativeObjects);
        }

        void HandleUnreferenceUnrootedAllocations(CachedSnapshot snapshot)
        {
            ref readonly var markerNativeAllocations = ref SourceIndexToRootAndImpactInfoMapper.GetNestedArray(in m_Marker, SourceIndex.SourceId.NativeAllocation);

            // Process all unrooted allocations that haven't been found yet.
            // They don't reference anything so its just a process of adding their size to the unrooted and unreferenced tracking value
            foreach (var unrootedAllocation in snapshot.ProcessedNativeRoots.UnrootedNativeAllocationIndices)
            {
                if (!markerNativeAllocations[unrootedAllocation.Index].FoundForRoot)
                {
                    UnreferencedUnrootedMemory.NativeSize += snapshot.ProcessedNativeRoots.AllocationSizes[unrootedAllocation.Index];
                    // This happens as a last step and unrooted allocations are ignored in Validate() so their roots staying invalid
                    // and their markers unset is fine, no need to incure extra computations for this.
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
            DontDestroyOnLoadScene,
            SceneRoots,
            AssetBundles,
            NativeObjectRoots,
            UnreferencedPrefabs,
            GCHandleRoots,
            NativeRootAllocations,
            UnrootedNativeObjects,
            Count
        }

        void Crawl(CachedSnapshot snapshot, ref CachedProcessingData data, OwnershipBucket ownershipBucket)
        {
            // go up the reference tree
            BuildPathFromRootInfo(snapshot, ref data, ownershipBucket);
            // and back down the reference tree
            CalculateImpact(snapshot, ref data, ownershipBucket);

            // validate that the stacks are empty and therefore wouldn't interfere with the next run, otherwise there is a bug in the algorithm
            Checks.IsTrue(data.Stack.Count == 0, "Stack should be empty after crawling");
            Checks.IsTrue(data.AlternativeStack.Count == 0, "Alternative stack  should be empty after crawling");
            // But to be sure, clear for next run
            data.Stack.Clear(false);
            data.AlternativeStack.Clear(false);
        }

        static SourceIndex GetPrefabIndexForNativeSceneObject(CachedSnapshot snapshot, SourceIndex sceneObject, in UnifiedType typeInfo)
        {
            var gameObjectOrTransform = (typeInfo.IsTransformType || typeInfo.IsGameObjectType) ?
                sceneObject : ObjectConnection.GetGameObjectIndexFromTransformOrComponentIndex(snapshot, sceneObject, snapshot.NativeTypes.GameObjectIdx);

            Checks.IsTrue(gameObjectOrTransform.Valid);

            if (snapshot.SceneRoots.NativeObjectIndexToPrefabRootIndex.TryGetValue(gameObjectOrTransform.Index, out var prefabIndex))
                return new SourceIndex(SourceIndex.SourceId.Prefab, prefabIndex);
            var transform = typeInfo.IsTransformType ? sceneObject : ObjectConnection.GetTransformIndexFromGameObject(snapshot, gameObjectOrTransform, true);
            throw new InvalidOperationException($"{sceneObject} is a persistent scene object on {(typeInfo.IsTransformType ? "Transform" : $"Transform {transform} on GameObject")} {gameObjectOrTransform} that does not map to a prefab!");
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

        void BuildPathFromRootInfo(CachedSnapshot snapshot, ref CachedProcessingData data, OwnershipBucket ownershipBucket)
        {
            data.Stack.PushRange(data.RootsInCurrentOwnershipBucket);

            while (data.Count > 0)
            {
                var current = data.Stack.Pop();

                ref var currentMarker = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, current);
                currentMarker.FoundForRoot = true;

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

                    // remove temporarily added "found" marker on the managed object do that this GCHandle can take its ownership
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
                                        ProcessReferenceToPrefab(snapshot, prefabIndex, current, ref data, in m_Marker);
                                        continue;
                                        // Yes, at this point we abandoned the referencedItem as a reference to be put on the stack.
                                        // Not to worry, it will be found while processing the Hierarchy of the prefab.
                                    }
                                    else if (current.Id is SourceIndex.SourceId.ManagedObject)
                                    {
                                        // This is practically guaranteed to be a Scene Object's managed shell referencing back to its native object,
                                        // (unless someone felt hacky and got their hands on the pointer to a native object).

                                        // Managed references to non-persistent SceneObjects are non-owning, as the Scene Objects are more strongly
                                        // bound to the scene.
                                        // Scene and Prefab indexes are handled above if the referencedTypeInfo indicates this is a root Transform.

                                        // Further, it should be ignored for impact attribution, so don't add it as unowned reference.
                                        continue;
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
                                                    ProcessReferenceToPrefab(snapshot, new SourceIndex(
                                                        SourceIndex.SourceId.Prefab, referencedPrefabIndex),
                                                        current, ref data, in m_Marker);
                                                    continue;
                                                }
                                            }
                                            isOwned = true;
                                        }
                                        else
                                            isOwned = (snapshot.NativeObjects.Flags[referencedItem.Index].HasFlag(ObjectFlags.IsRoot) && current.Id is SourceIndex.SourceId.Scene or SourceIndex.SourceId.Prefab);
                                    }
                                    else if (referencedTypeInfo.IsGameObjectType)
                                    {
                                        // References to GameObjects are only owned if they originate in their Transform
                                        isOwned = currentIsNativeObject && currentTypeInfo.IsTransformType;
                                    }
                                    else if (referencedTypeInfo.IsComponentType)
                                    {
                                        // References to Components are only owned if they originate in their GameObject
                                        isOwned = currentIsNativeObject && (currentTypeInfo.IsGameObjectType
                                                // Sometimes AssetBundles hold references to a MonoBehaviour without a ManagedShell or GameObject
                                                // (the only connection being to the asset bundle).
                                                // In those cases, do root them to the Asset Bundle.
                                                || currentTypeInfo.NativeTypeIndex == snapshot.NativeTypes.AssetBundleIdx
                                                && referencedIsPersistent && referencedTypeInfo.IsMonoBehaviourType
                                                && snapshot.NativeObjects.ManagedObjectIndex[referencedItem.Index] == CachedSnapshot.NativeObjectEntriesCache.InvalidObjectIndex
                                                && snapshot.Connections.ReferencedBy.TryGetValue(referencedItem, out var monoBehaviourReferencedItems)
                                                && monoBehaviourReferencedItems.Count == 1);
                                    }
                                    else if (currentIsNativeObject)
                                    {
#if VALIDATE_ROOT_AND_IMPACT
                                        Debug.LogWarning($"Found a reference from a {current.Id} to a SceneObject {referencedItem} that might not be valid?");
#endif
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
                        var currentStep = data.ImpactStack.Peek();
                        if (currentStep.ProcessedChildren)
                        {
                            // all children processed, attribute impact and pop
                            data.ImpactStack.Pop();
                            AttributeImpact(snapshot, ref data, currentStep.Current, ownershipBucket);
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
                                data.ImpactStack.Pop();
                                data.ImpactStack.Push(currentStep);
                                ref var marker = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, ownedChildIndex);
                                if (marker.FoundForRoot && !marker.FoundForImpact)
                                    AddNextToImpactStack(ref data, ownedChildIndex, ref marker);
                            }
                            else if (currentStep.UnownedChildrenProcessed < currentStep.UnownedChildListCount)
                            {
                                var unownedChildIndex = currentStep.UnownedReferencesListInfo.HasOneChild ?
                                    currentStep.UnownedReferencesListInfo.SingleChild :
                                    m_UnownedChildList[currentStep.UnownedReferencesListInfo.IndexInChildList + 1 + currentStep.UnownedChildrenProcessed];
                                currentStep.UnownedChildrenProcessed++;
                                data.ImpactStack.Pop();
                                data.ImpactStack.Push(currentStep);
                                ref var marker = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_Marker, unownedChildIndex);
                                if (marker.FoundForRoot && !marker.FoundForImpact)
                                    AddNextToImpactStack(ref data, unownedChildIndex, ref marker);
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
                    var staticFieldByteCount = (ulong)snapshot.TypeDescriptions.StaticFieldBytes[current.Index].Count;
                    ownSize = new NativeRootSize
                    {
                        ManagedSize = new MemorySize(staticFieldByteCount, staticFieldByteCount)
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
                    ownSize = new NativeRootSize
                    {
                        NativeSize = snapshot.ProcessedNativeRoots.AllocationSizes[current.Index]
                    };
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
                            if (ownershipBucket is not OwnershipBucket.UnrootedNativeObjects)
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
                            throw new InvalidOperationException($"Found a {currentPathStep.Parent.Id} as a parent to a Transform, which is not supposed to happen!");
                        }
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
                if (data.UnownedReferences.Count == 1)
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
                         ownershipBucket is OwnershipBucket.UnrootedNativeObjects)
                {
                    // This is an otherwise unrooted native object but that is to be expected for this bucket.
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
                            Debug.Assert(rootStep.Valid, "Native allocation root information should be valid");
                            Debug.Assert(!rootStep.IsRoot, "Native allocation should not be a root itself");
                            Debug.Assert(rootStep.Root.Valid && rootStep.Parent.Valid, "Native allocation should have a root and a parent");
                            Debug.Assert(mark.FoundForRoot, "Native allocation should be marked as found for root");
                            Debug.Assert(mark.FoundForImpact, "Native allocation should be marked as found for impact");
                            Debug.Assert(mark.FullyDistributedImpact, "Native allocation impact should be fully distributed");

                            if (!rootStep.Valid
                                || !(rootStep.IsRoot || (rootStep.Root.Valid && rootStep.Parent.Valid))
                                || !mark.FoundForRoot
                                || !mark.FoundForImpact
                                || !mark.FullyDistributedImpact)
                            {
                                // Only build this string if it actually failed.
                                if (!snapshot.NativeRootReferences.IdToIndex.TryGetValue(rootId, out var rootIndex))
                                    rootIndex = NativeRootReferenceEntriesCache.InvalidRootIndex;
                                Debug.LogError($"Native Allocation failed validation: (index: {i}, rootId: {rootId}, AreaName: {(rootIndex >= NativeRootReferenceEntriesCache.FirstValidRootIndex ? snapshot.NativeRootReferences.AreaName[rootIndex] : "Unknown")}, ObjectName: {(rootIndex >= NativeRootReferenceEntriesCache.FirstValidRootIndex ? snapshot.NativeRootReferences.ObjectName[rootIndex] : "Unknown")}");
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

                                ref readonly var manageObjectMark = ref markerInfoForGCHandleHeldObjects[i];
                                ref readonly var manageObjectRootStep = ref rootStepInfoForGCHandleHeldObjects[i];
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

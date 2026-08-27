using System.Collections.Generic;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.EnumerationUtilities;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.UI;
using UnityEngine;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    /// <summary>
    /// Precomputes <see cref="ObjectStatus"/> for every native and managed object in the snapshot.
    /// Runs as an independent post-processing step after <see cref="RootAndImpactInfo"/>.
    /// Uses RootAndImpactInfo data (ownership, IsRoot) when available for more accurate classification,
    /// and falls back to flag-based checks (matching SelectedItemDetailsForTypesAndObjects logic) when not.
    /// Structural types (Scene, Prefab, ManagedType, GCHandle, NativeRootReference, NativeAllocation)
    /// return fixed statuses without per-element storage.
    /// </summary>
    class ObjectStatusComputation : System.IDisposable
    {
        public const int ProcessStepCount = 1;

        DynamicArray<ObjectStatus> m_NativeObjectStatuses;
        DynamicArray<ObjectStatus> m_ManagedObjectStatuses;
        /// <summary>
        /// Back-reference used by <see cref="GetStatus"/> to classify native allocations on the fly
        /// (referenced-unrooted vs rooted). The snapshot owns this computation, so the reference shares its lifetime.
        /// </summary>
        CachedSnapshot m_Snapshot;
        bool m_Processed;

        /// <summary>
        /// Get the precomputed <see cref="ObjectStatus"/> for any <see cref="SourceIndex"/>.
        /// </summary>
        public ObjectStatus GetStatus(SourceIndex sourceIndex)
        {
            if (!m_Processed)
                return ObjectStatus.None;

            switch (sourceIndex.Id)
            {
                case SourceIndex.SourceId.NativeObject:
                    return m_NativeObjectStatuses[sourceIndex.Index];
                case SourceIndex.SourceId.ManagedObject:
                    return m_ManagedObjectStatuses[sourceIndex.Index];
                case SourceIndex.SourceId.Scene:
                    return ObjectStatus.SceneRoot;
                case SourceIndex.SourceId.Prefab:
                    // Prefab entries are book-keeping units for their root Transform. Report that Transform's
                    // status so status filters targeting the Transform (e.g. an AssetBundle-related status)
                    // match the Prefab entry directly instead of muting it. The dedicated "Prefab" filter still
                    // selects these entries via a synthesized OR-in in RootsAndImpactModelBuilder; that is why
                    // ObjectStatus.PrefabRoot exists but is never returned here.
                    return m_Snapshot != null
                        ? GetStatus(m_Snapshot.SceneRoots.AllPrefabRootTransformSourceIndices[sourceIndex.Index])
                        : ObjectStatus.PrefabRoot;
                case SourceIndex.SourceId.ManagedType:
                    return ObjectStatus.ManagedTypeRoot;
                case SourceIndex.SourceId.GCHandleIndex:
                    return ObjectStatus.GCHandleEntry;
                case SourceIndex.SourceId.NativeRootReference:
                    return ObjectStatus.NativeRootAllocation;
                case SourceIndex.SourceId.NativeAllocation:
                    // A native allocation with no valid native root reference is either reached via a
                    // reference (ReferencedUnrooted) or genuinely unreferenced (Unrooted). The crawl
                    // recorded which unrooted allocations were referenced; consult that set so the
                    // status is correct in both the Roots & Impact and All Of Memory tables.
                    if (m_Snapshot != null
                        && m_Snapshot.RootAndImpactInfo.SuccessfullyBuilt
                        && m_Snapshot.NativeAllocations.RootReferenceId[sourceIndex.Index] < CachedSnapshot.NativeRootReferenceEntriesCache.FirstValidRootId)
                        return m_Snapshot.RootAndImpactInfo.IsReferencedUnrootedAllocation(sourceIndex)
                            ? ObjectStatus.ReferencedUnrooted
                            : ObjectStatus.Unrooted;
                    return ObjectStatus.NativeRootAllocation;
                // The aggregate of unrooted-and-unreferenced native allocations is stored under a
                // special None SourceIndex. Unlike rooted subsystem allocations, no owner could be
                // attributed to this memory, so it gets its own status.
                case SourceIndex.SourceId.None when sourceIndex.Index == (long)SourceIndex.SpecialNoneCase.UnrootedAllocation:
                    return ObjectStatus.Unrooted;
                // Graphics resources not associated with a native object — pure Graphics driver buffers.
                case SourceIndex.SourceId.GfxResource:
                    return ObjectStatus.GraphicsBuffers;
                // Leaf items in the "Reserved" group: managed or native memory regions not associated with any object or allocation.
                case SourceIndex.SourceId.NativeMemoryRegion:
                case SourceIndex.SourceId.ManagedHeapSection:
                    return ObjectStatus.Reserved;
                // Leaf items in the "Untracked" group: system memory regions not accounted for elsewhere.
                case SourceIndex.SourceId.SystemMemoryRegion:
                    return ObjectStatus.Untracked;
                default:
                    return ObjectStatus.None;
            }
        }

        public IEnumerator<EnumerationStatus> ReadOrProcess(CachedSnapshot snapshot, EnumerationStatus status)
        {
            m_Snapshot = snapshot;
            m_NativeObjectStatuses = new DynamicArray<ObjectStatus>(
                snapshot.NativeObjects.Count, snapshot.NativeObjects.Count, Allocator.Persistent, true);
            m_ManagedObjectStatuses = new DynamicArray<ObjectStatus>(
                snapshot.CrawledData.ManagedObjects.Count, snapshot.CrawledData.ManagedObjects.Count, Allocator.Persistent, true);

            ComputeNativeObjectStatuses(snapshot);
            ComputeManagedObjectStatuses(snapshot);
            m_Processed = true;

            yield return status.IncrementStep("Compute Object Statuses");
        }

        void ComputeNativeObjectStatuses(CachedSnapshot snapshot)
        {
            var nativeObjects = snapshot.NativeObjects;
            var unifiedNativeTypes = snapshot.TypeDescriptions.UnifiedTypeInfoNative;
            var count = nativeObjects.Count;

            // When RootAndImpactInfo is available, use ownership data for referenced/unreferenced classification
            var hasRootInfo = snapshot.RootAndImpactInfo.SuccessfullyBuilt;
            DynamicArrayRef<ShortestRootPathInfo> shortestPaths = default;
            // Membership bitset (by native object index) of AssetBundle objects, so a persistent asset
            // whose shortest-path root is a bundle can be classified as UnusedAssetInAssetBundle in O(1).
            // Only meaningful with ownership data; bundles are few but the lookup is per persistent asset.
            // Packed 64 flags per ulong (see SetBit/TestBit): 8x tighter than bool elements (which occupy a
            // full byte each) and burst-friendly, at ~1 bit per native object in the snapshot.
            DynamicArray<ulong> isAssetBundle = default;
            try
            {
                if (hasRootInfo)
                {
                    shortestPaths = SourceIndexToRootAndImpactInfoMapper.GetNestedArray(
                        in snapshot.RootAndImpactInfo.ShortestPathInfo, SourceIndex.SourceId.NativeObject);
                    ref readonly var assetBundles = ref snapshot.NativeObjects.AssetBundles;
                    if (assetBundles.Count > 0)
                    {
                        // Persistent, not Temp: the bitset spans ALL native objects and stays safe if this
                        // processing step ever moves off the main thread; it is disposed at the end of this
                        // method either way. memClear must stay true — the fill ORs bits into the words.
                        var elementCount = DynamicArrayAlgorithms.CountToUlongPackedBitsetCapacity(count);
                        isAssetBundle = new DynamicArray<ulong>(elementCount, elementCount, Allocator.Persistent, true);
                        for (long b = 0; b < assetBundles.Count; b++)
                            isAssetBundle.SetBit(assetBundles[b].Index);
                    }
                }

                for (long i = 0; i < count; i++)
                {
                    var flags = nativeObjects.Flags[i];

                    var isManager = (flags & ObjectFlags.IsManager) != 0;
                    if (isManager)
                    {
                        m_NativeObjectStatuses[i] = ObjectStatus.Manager;
                        continue;
                    }

                    // Native MonoBehaviour/ScriptableObject instances whose backing script is missing are
                    // collected into the Broken Script References bucket. Consult that exact set so a
                    // legitimately-rooted missing-script object (e.g. one claimed by the DontDestroyOnLoad
                    // bucket) isn't mistaken for this leak.
                    if (hasRootInfo && snapshot.RootAndImpactInfo.IsBrokenScriptReference(new SourceIndex(SourceIndex.SourceId.NativeObject, i)))
                    {
                        m_NativeObjectStatuses[i] = ObjectStatus.BrokenScriptReference;
                        continue;
                    }

                    // AssetBundle objects themselves: Used if any of their content is referenced from outside
                    // the bundle; otherwise Unused, or Leaked when nothing references the bundle object either.
                    // AssetBundles carry a native self-connection (the bundle references itself) as a capture
                    // artefact unique to them. It is neither an external content reference nor an incoming
                    // reference that keeps the bundle alive, so it — along with the bundle's own managed shell —
                    // is discounted here. This special-casing is scoped to bundles rather than the general
                    // crawl / RefCount paths (only AssetBundles exhibit the self-connection). The bundle's
                    // otherwise-unreferenced contents are tagged UnusedAssetInAssetBundle below.
                    if (hasRootInfo && isAssetBundle.IsCreated && isAssetBundle.TestBit(i))
                    {
                        var bundleIndex = new SourceIndex(SourceIndex.SourceId.NativeObject, i);
                        if (AssetBundleHasExternallyReferencedContent(snapshot, bundleIndex))
                            m_NativeObjectStatuses[i] = ObjectStatus.UsedAssetBundle;
                        else
                            m_NativeObjectStatuses[i] = AssetBundleHasNonSelfIncomingReference(snapshot, bundleIndex)
                                ? ObjectStatus.UnusedAssetBundle
                                : ObjectStatus.LeakedAssetBundle;
                        continue;
                    }

                    var isPersistent = (flags & ObjectFlags.IsPersistent) != 0;
                    var isPersistentAsset = isPersistent && !isManager;
                    var typeIndex = nativeObjects.NativeTypeArrayIndex[i];
                    ref readonly var typeInfo = ref unifiedNativeTypes[typeIndex];

                    // Check for delayed deletion queue (only available with RootAndImpactInfo)
                    if (hasRootInfo && isPersistentAsset && typeInfo.IsSceneObjectType)
                    {
                        var sourceIndex = new SourceIndex(SourceIndex.SourceId.NativeObject, i);
                        if (snapshot.RootAndImpactInfo.IsPersistentSceneObjectInDelayedDeletionQueue(sourceIndex))
                        {
                            m_NativeObjectStatuses[i] = ObjectStatus.InDelayedDeletionQueue;
                            continue;
                        }
                    }

                    var hideFlags = nativeObjects.HideFlags[i];
                    var isDontUnload = (flags & ObjectFlags.IsDontDestroyOnLoad) != 0 || (hideFlags & HideFlags.DontUnloadUnusedAsset) != 0;
                    var isSceneObject = typeInfo.IsSceneObjectType && !isPersistentAsset;
                    var isAssetObject = (typeInfo.IsAssetObjectType && !isManager) || (typeInfo.IsSceneObjectType && isPersistentAsset);

                    // Determine if the object is referenced by something else.
                    // With RootAndImpactInfo: IsRoot means self-rooted (unreferenced), !IsRoot means claimed by another root (referenced).
                    // BUT: Some objects (e.g. an unnamed MonoScript object referenced by the MonoManager) will end up as a root with native ref count.
                    // Without RootAndImpactInfo: fall back to managed ref count as a heuristic.
                    bool isReferenced = false;
                    if (hasRootInfo)
                    {
                        isReferenced = !shortestPaths[i].IsRoot;
                    }
                    if (!isReferenced)
                        isReferenced = NativeObjectHasNonSelfIncomingReference(snapshot, i);

                    if (isAssetObject && !isPersistentAsset)
                    {
                        // Dynamic Asset
                        if (isDontUnload)
                            m_NativeObjectStatuses[i] = ObjectStatus.DynamicAssetDontUnload;
                        else if (!isReferenced)
                            m_NativeObjectStatuses[i] = ObjectStatus.DynamicAssetLeaked;
                        else
                            m_NativeObjectStatuses[i] = ObjectStatus.DynamicAssetReferenced;
                    }
                    else if (isPersistentAsset)
                    {
                        if (isReferenced)
                        {
                            // With k_AssetBundlesOnlyOwnUnreferencedContents, a bundle becomes the shortest-path
                            // root of only its otherwise-unreferenced contents. So if the asset's root is a
                            // bundle, nothing outside the bundle keeps it loaded: unloading the bundle frees it.
                            if (hasRootInfo)
                            {
                                ref readonly var root = ref shortestPaths[i].Root;
                                if (root.Id == SourceIndex.SourceId.NativeObject && isAssetBundle.IsCreated && isAssetBundle.TestBit(root.Index))
                                    m_NativeObjectStatuses[i] = ObjectStatus.UnusedAssetInAssetBundle;
                                else
                                    m_NativeObjectStatuses[i] = ObjectStatus.UsedAsset;
                            }
                            else
                                m_NativeObjectStatuses[i] = ObjectStatus.UsedAsset;
                        }
                        else if (isDontUnload)
                            m_NativeObjectStatuses[i] = ObjectStatus.UnusedAssetDontUnload;
                        else
                            m_NativeObjectStatuses[i] = ObjectStatus.UnusedAsset;
                    }
                    else if (isSceneObject)
                    {
                        m_NativeObjectStatuses[i] = isDontUnload ? ObjectStatus.SceneObjectDontUnload : ObjectStatus.SceneObject;
                    }
                    else
                    {
                        m_NativeObjectStatuses[i] = ObjectStatus.None;
                    }
                }
            }
            finally
            {
                // Guarantees the Persistent scratch is freed even when the fill or status loop throws
                // on a malformed snapshot; nothing upstream owns or disposes this local.
                if (isAssetBundle.IsCreated)
                    isAssetBundle.Dispose();
            }
        }

        /// <summary>
        /// True when something other than the native object's own managed shell self-reference points at
        /// it: its native ref count, or its managed shell's ref count, with the mutual native/shell
        /// self-reference discounted. Operates purely on a native object index; managed objects are
        /// classified separately in <see cref="ComputeManagedObjectStatuses"/>.
        /// </summary>
        static bool NativeObjectHasNonSelfIncomingReference(CachedSnapshot snapshot, long nativeObjectIndex)
        {
            var managedObjectIndex = snapshot.NativeObjects.ManagedObjectIndex[nativeObjectIndex];
            var hasManagedShell = managedObjectIndex >= 0 && managedObjectIndex < snapshot.CrawledData.ManagedObjects.Count;
            var selfRefCountPerReferenceType = hasManagedShell ? 1 : 0;
            return (snapshot.NativeObjects.RefCount[nativeObjectIndex] - selfRefCountPerReferenceType) > 0
                || (hasManagedShell
                    && snapshot.CrawledData.ManagedObjects[managedObjectIndex].RefCount - selfRefCountPerReferenceType > 0);
        }

        /// <summary>
        /// True when the AssetBundle references content that is owned or referenced from outside the
        /// bundle (which makes the bundle <see cref="ObjectStatus.UsedAssetBundle"/>). The AssetBundle's
        /// native self-connection artefact and its own managed shell are discounted, as neither is
        /// externally-used content. Bundles are few, so iterating their references here is cheap.
        /// </summary>
        static bool AssetBundleHasExternallyReferencedContent(CachedSnapshot snapshot, SourceIndex bundleIndex)
        {
            var managedShellIndex = snapshot.NativeObjects.ManagedObjectIndex[bundleIndex.Index];
            var it = new ReferencesIterator(snapshot.RootAndImpactInfo, bundleIndex, includeUnownedReferences: true);
            try
            {
                while (it.MoveNext())
                {
                    var reference = it.Current;
                    // Owned (otherwise-unreferenced) contents don't make the bundle Used.
                    if (reference.IsOwned == BurstableBool.TRUE)
                        continue;
                    var referenced = reference.Reference;
                    // The AssetBundle self-connection artefact.
                    if (referenced.Equals(bundleIndex))
                        continue;
                    // The bundle's own managed shell.
                    if (managedShellIndex >= 0 && referenced.Id == SourceIndex.SourceId.ManagedObject && referenced.Index == managedShellIndex)
                        continue;
                    return true;
                }
            }
            finally
            {
                it.Dispose();
            }
            return false;
        }

        /// <summary>
        /// Like <see cref="NativeObjectHasNonSelfIncomingReference"/> but for AssetBundles: additionally
        /// discounts the AssetBundle's native self-connection artefact, so a bundle that nothing else
        /// references is classified <see cref="ObjectStatus.LeakedAssetBundle"/> rather than Unused. A
        /// genuine reference chain through the bundle's managed shell (e.g. a live field still holding the
        /// AssetBundle) still counts.
        /// </summary>
        static bool AssetBundleHasNonSelfIncomingReference(CachedSnapshot snapshot, SourceIndex bundleIndex)
        {
            var managedShellIndex = snapshot.NativeObjects.ManagedObjectIndex[bundleIndex.Index];
            var hasManagedShell = managedShellIndex >= 0 && managedShellIndex < snapshot.CrawledData.ManagedObjects.Count;

            // Native referrers other than the bundle itself (the self-connection artefact) and its own
            // managed shell. Unlike the RefCount-based check, this ignores the bundle's self-connection.
            if (snapshot.Connections.ReferencedBy.TryGetValue(bundleIndex, out var referrers))
            {
                foreach (var referrer in referrers)
                {
                    if (referrer.Equals(bundleIndex))
                        continue;
                    if (hasManagedShell && referrer.Id == SourceIndex.SourceId.ManagedObject && referrer.Index == managedShellIndex)
                        continue;
                    return true;
                }
            }

            // The managed shell being referenced by something other than its own native object is a
            // genuine reference chain through the shell (discounting the native<->shell self-reference).
            return hasManagedShell && snapshot.CrawledData.ManagedObjects[managedShellIndex].RefCount - 1 > 0;
        }

        void ComputeManagedObjectStatuses(CachedSnapshot snapshot)
        {
            var managedObjects = snapshot.CrawledData.ManagedObjects;
            ref readonly var unifiedManagedTypes = ref snapshot.TypeDescriptions.UnifiedTypeInfoManaged;
            var count = managedObjects.Count;
            var requiredByNativeCode = snapshot.CrawledData.IndicesOfManagedObjectsHeldByRequiredByNativeCodeAttribute;
            var pureManagedGCHandleHeld = snapshot.CrawledData.IndicesOfPureManagedObjectsHeldByNonNativeObjectRelatedGCHandle;
            var potentialUnityObjectGCHandleHeld = snapshot.CrawledData.IndicesOfManagedPotentialUnityObjectsHeldByNonNativeObjectRelatedGCHandle;

            for (long i = 0; i < count; i++)
            {
                var managedObject = managedObjects[i];
                var nativeObjectIndex = managedObject.NativeObjectIndex;
                var hasNativeSide = nativeObjectIndex >= 0 && nativeObjectIndex != NativeObjectEntriesCache.InvalidObjectIndex;

                var typeIndex = managedObject.ITypeDescription;
                var isUnityObjectType = typeIndex >= 0
                    && typeIndex < unifiedManagedTypes.Count
                    && unifiedManagedTypes[typeIndex].IsUnityObjectType;

                if (hasNativeSide)
                {
                    // Has native counterpart — inherit its status
                    m_ManagedObjectStatuses[i] = m_NativeObjectStatuses[nativeObjectIndex];
                }
                else if (isUnityObjectType)
                {
                    // Unity Object type but no native side — leaked shell
                    if (potentialUnityObjectGCHandleHeld.Contains(i))
                        m_ManagedObjectStatuses[i] = ObjectStatus.LeakedShellHeldByGCHandle;
                    else
                        m_ManagedObjectStatuses[i] = ObjectStatus.LeakedShell;
                }
                else
                {
                    // Pure managed object
                    if (requiredByNativeCode.Contains(i))
                        m_ManagedObjectStatuses[i] = ObjectStatus.UsedByNativeCode;
                    else if (pureManagedGCHandleHeld.Contains(i))
                        m_ManagedObjectStatuses[i] = ObjectStatus.GCHandleHeld;
                    else if (managedObject.RefCount > 0)
                        m_ManagedObjectStatuses[i] = ObjectStatus.ManagedObjectReferenced;
                    else
                        m_ManagedObjectStatuses[i] = ObjectStatus.UnknownLiveness;
                }
            }
        }

        public void Dispose()
        {
            if (m_NativeObjectStatuses.IsCreated)
                m_NativeObjectStatuses.Dispose();
            if (m_ManagedObjectStatuses.IsCreated)
                m_ManagedObjectStatuses.Dispose();
        }
    }
}

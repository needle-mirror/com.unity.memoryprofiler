using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.UIContentData;
using Unity.Profiling.Memory;
using UnityEngine;
using UnityEngine.UIElements;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;
using static Unity.MemoryProfiler.Editor.UI.TreeModelHelpers;


#if LIFECYCLE_APIS_AVAILABLE
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.MemoryProfiler.Editor.UI
{
    partial class RootsAndImpactModelBuilder
    {
        int m_ItemId;
        Dictionary<int, IndexableTreeViewListAndIndex<RootsAndImpactModel.ItemData>> m_IdToIndex;
        readonly int m_FlattenedPageSize;

        /// <summary>
        /// Rows handed to the tree view at once in the flattened view. UIToolkit fully materializes whatever
        /// container reaches SetRootItems into its own managed dictionaries, so a snapshot with millions of
        /// ownership-forest rows can only be flattened by paging over the native store instead.
        /// </summary>
        internal const int k_FlattenedPageSize = 2500;

        /// <summary>
        /// AND-mask over a traversal's visit counter deciding how often the cancellation token is polled.
        /// One less than a power of two, so a check lands every 65536 nodes.
        /// </summary>
        const int k_CancellationCheckMask = 0xFFFF;

        /// <summary>
        /// Parsed type-name components for the static-roots namespace/generic grouping, keyed by the node's
        /// SourceIndex (stable per type). The builder instance is reused across rebuilds (filter/flatten/status-mask
        /// changes, table-mode switches), so caching parses each type name once rather than re-scanning every full
        /// name string on every rebuild. Reset in <see cref="Build"/> when the loaded snapshot changes, since
        /// SourceIndex values are snapshot-relative.
        /// </summary>
        Dictionary<SourceIndex, ParsedTypeName> m_TypeNameComponentsCache;
        CachedSnapshot m_TypeNameComponentsCacheSnapshot;

        /// <summary>
        /// Bucket display names indexed by (int)OwnershipBucket for O(1) dispatch instead of a switch. Localized
        /// TextContent is captured at static-init; a locale change triggers a domain reload that re-runs this ctor
        /// with the fresh locale, while <c>[AutoStaticsCleanup]</c> resets the array on no-domain-reload transitions
        /// (Fast Enter Play Mode / CoreCLR ALC switch) so it can't pin a stale, previous-ALC string set.
        /// </summary>
#if LIFECYCLE_APIS_AVAILABLE
        [AutoStaticsCleanup]
#endif
        static readonly string[] k_BucketDisplayNames;

        /// <summary>
        /// Selection Details category id per <see cref="RootAndImpactInfo.OwnershipBucket"/>, indexed by (int)bucket.
        /// Used as the tree-item id of each top-level bucket header so it routes to its own description in
        /// <see cref="BreakdownDetailsViewControllerFactory"/>. Holds only constant ints (no localized strings), so it
        /// needs no statics-cleanup.
        /// </summary>
        static readonly int[] k_BucketCategories;

        static RootsAndImpactModelBuilder()
        {
            k_BucketDisplayNames = new string[(int)RootAndImpactInfo.OwnershipBucket.Count];
            k_BucketDisplayNames[(int)RootAndImpactInfo.OwnershipBucket.StaticRoots] = TextContent.RootsAndImpactBucketStaticRoots;
            k_BucketDisplayNames[(int)RootAndImpactInfo.OwnershipBucket.DontDestroyOnLoadObjects] = TextContent.RootsAndImpactBucketDontDestroyOnLoad;
            k_BucketDisplayNames[(int)RootAndImpactInfo.OwnershipBucket.DontDestroyOnLoadObjectsInDeletionQueue] = TextContent.RootsAndImpactBucketDontDestroyOnLoadDeletionQueue;
            k_BucketDisplayNames[(int)RootAndImpactInfo.OwnershipBucket.DontDestroyOnLoadScene] = TextContent.RootsAndImpactBucketDontDestroyOnLoadScene;
            k_BucketDisplayNames[(int)RootAndImpactInfo.OwnershipBucket.SceneRoots] = TextContent.RootsAndImpactBucketSceneRoots;
            k_BucketDisplayNames[(int)RootAndImpactInfo.OwnershipBucket.AssetBundles] = TextContent.RootsAndImpactBucketAssetBundles;
            k_BucketDisplayNames[(int)RootAndImpactInfo.OwnershipBucket.NativeObjectRoots] = TextContent.RootsAndImpactBucketNativeObjectRoots;
            k_BucketDisplayNames[(int)RootAndImpactInfo.OwnershipBucket.UnreferencedPrefabs] = TextContent.RootsAndImpactBucketUnreferencedPrefabs;
            k_BucketDisplayNames[(int)RootAndImpactInfo.OwnershipBucket.GCHandleRoots] = TextContent.RootsAndImpactBucketGCHandleRoots;
            k_BucketDisplayNames[(int)RootAndImpactInfo.OwnershipBucket.StackRoots] = TextContent.RootsAndImpactBucketStackRoots;
            k_BucketDisplayNames[(int)RootAndImpactInfo.OwnershipBucket.FinalizerRoots] = TextContent.RootsAndImpactBucketFinalizerRoots;
            k_BucketDisplayNames[(int)RootAndImpactInfo.OwnershipBucket.OtherRoots] = TextContent.RootsAndImpactBucketOtherRoots;
            k_BucketDisplayNames[(int)RootAndImpactInfo.OwnershipBucket.NonGCHeapSections] = TextContent.RootsAndImpactBucketNonGCHeapSections;
            k_BucketDisplayNames[(int)RootAndImpactInfo.OwnershipBucket.NativeRootAllocations] = TextContent.RootsAndImpactBucketNativeRootAllocations;
            k_BucketDisplayNames[(int)RootAndImpactInfo.OwnershipBucket.BrokenScriptReferences] = TextContent.RootsAndImpactBucketBrokenScriptReferences;

            k_BucketCategories = new int[(int)RootAndImpactInfo.OwnershipBucket.Count];
            k_BucketCategories[(int)RootAndImpactInfo.OwnershipBucket.StaticRoots] = (int)IAnalysisViewSelectable.Category.StaticRoots;
            k_BucketCategories[(int)RootAndImpactInfo.OwnershipBucket.DontDestroyOnLoadObjects] = (int)IAnalysisViewSelectable.Category.DontDestroyOnLoadObjects;
            k_BucketCategories[(int)RootAndImpactInfo.OwnershipBucket.DontDestroyOnLoadObjectsInDeletionQueue] = (int)IAnalysisViewSelectable.Category.DontDestroyOnLoadDeletionQueue;
            k_BucketCategories[(int)RootAndImpactInfo.OwnershipBucket.DontDestroyOnLoadScene] = (int)IAnalysisViewSelectable.Category.DontDestroyOnLoadScene;
            k_BucketCategories[(int)RootAndImpactInfo.OwnershipBucket.SceneRoots] = (int)IAnalysisViewSelectable.Category.SceneRoots;
            k_BucketCategories[(int)RootAndImpactInfo.OwnershipBucket.AssetBundles] = (int)IAnalysisViewSelectable.Category.AssetBundles;
            k_BucketCategories[(int)RootAndImpactInfo.OwnershipBucket.NativeObjectRoots] = (int)IAnalysisViewSelectable.Category.NativeObjectRoots;
            k_BucketCategories[(int)RootAndImpactInfo.OwnershipBucket.UnreferencedPrefabs] = (int)IAnalysisViewSelectable.Category.UnreferencedPrefabs;
            k_BucketCategories[(int)RootAndImpactInfo.OwnershipBucket.GCHandleRoots] = (int)IAnalysisViewSelectable.Category.GCHandleRoots;
            k_BucketCategories[(int)RootAndImpactInfo.OwnershipBucket.StackRoots] = (int)IAnalysisViewSelectable.Category.StackRoots;
            k_BucketCategories[(int)RootAndImpactInfo.OwnershipBucket.FinalizerRoots] = (int)IAnalysisViewSelectable.Category.FinalizerRoots;
            k_BucketCategories[(int)RootAndImpactInfo.OwnershipBucket.OtherRoots] = (int)IAnalysisViewSelectable.Category.OtherRoots;
            k_BucketCategories[(int)RootAndImpactInfo.OwnershipBucket.NonGCHeapSections] = (int)IAnalysisViewSelectable.Category.NonGCHeapSections;
            k_BucketCategories[(int)RootAndImpactInfo.OwnershipBucket.NativeRootAllocations] = (int)IAnalysisViewSelectable.Category.NativeRootAllocations;
            k_BucketCategories[(int)RootAndImpactInfo.OwnershipBucket.BrokenScriptReferences] = (int)IAnalysisViewSelectable.Category.BrokenScriptReferences;
        }

        public RootsAndImpactModelBuilder() : this(k_FlattenedPageSize)
        {
        }

        /// <summary>Test seam: a small page size lets the committed fixtures exercise multi-page behaviour.</summary>
        internal RootsAndImpactModelBuilder(int flattenedPageSize)
        {
            m_ItemId = (int)IAnalysisViewSelectable.Category.FirstDynamicId;
            m_FlattenedPageSize = Math.Max(1, flattenedPageSize);
        }

        public void ClearIdToIndexCache()
        {
            TreeModelHelpers.ClearIdToIndexCache(ref m_IdToIndex);
        }

        public RootsAndImpactModel Build(CachedSnapshot snapshot, in BuildArgs args)
        {
            m_ItemId = (int)IAnalysisViewSelectable.Category.FirstDynamicId;

            // Null when unfiltered. Method-local, not a field: a superseded build on this reused builder can still be running.
            var filteredImpactByItemId = args.SearchFilter != null
                ? new Dictionary<int, RootsAndImpactModel.FilteredImpact>()
                : null;

            // SourceIndex values are snapshot-relative, so the parsed-type-name cache is only valid for
            // the snapshot it was populated from. Reset it whenever the loaded snapshot changes (the
            // builder instance itself persists across rebuilds and across snapshots).
            if (!ReferenceEquals(m_TypeNameComponentsCacheSnapshot, snapshot))
            {
                m_TypeNameComponentsCache?.Clear();
                m_TypeNameComponentsCacheSnapshot = snapshot;
            }

            // No impact data for this snapshot. Surface the reason via the table view's error label — the
            // same mechanism the other breakdowns use — but distinguish the causes so the message isn't
            // misleading:
            if (!snapshot.RootAndImpactInfo.SuccessfullyBuilt)
            {
                // The snapshot's file format predates the data Roots & Impact needs (Unity 2022.1 or older).
                // This is the only genuinely "unsupported version" case.
                if (!snapshot.HasSceneRootsAndAssetbundles)
                    throw new UnsupportedSnapshotVersionException(TextContent.AttributedImpactInfoNotAvailableForOldSnapshots);

                // New enough to support the feature, but the data is missing because it was disabled when the
                // snapshot was opened, or because building it failed. AttributedImpactInfoNotAvailable covers
                // both ("reopen if you just enabled the setting, otherwise report a bug").
                throw new InvalidOperationException(TextContent.AttributedImpactInfoNotAvailable);
            }

            var rootAndImpactInfo = snapshot.RootAndImpactInfo;
            var rootNodes = new List<TreeViewItemData<RootsAndImpactModel.ItemData>>();

            // Upper-bound row estimate, only used for the "too large" safety-net message below.
            var expectedRows = rootAndImpactInfo.OwnershipForestNodeCount
                + snapshot.SystemMemoryRegions.ExecutableAndDllsCount
                + snapshot.ProcessedNativeRoots.VMAllocationCount
                + (int)RootAndImpactInfo.OwnershipBucket.Count;

            // Both modes back their rows with the native store and hand TreeViewItemData only a redirect into it,
            // which is what keeps either mode off the managed TreeDataController dictionary ceiling. Flattened
            // mode leaves rootNodes empty and lets the model materialize one page at a time from the store; tree
            // mode mirrors the emitted tree in the store 1:1, headers included.
            var store = new FlattenedRowStore<CompactFlatRow>();
            var rowSource = new RootsAndImpactRowSource(store, new List<string>(), snapshot);

            // Guards the remainder of this method: until a model is returned below, ownership of store's
            // native memory hasn't transferred anywhere, so any exception here has to dispose it explicitly
            // or it leaks.
            try
            {

                // Appends a synthetic/aggregate/header row, whose name is given directly rather than resolved from a
                // SourceIndex, and returns an ItemData redirect to it. Depth is always 0: none of these rows take part
                // in the per-node ownership traversal that AccumulateFilteredTotal's depth stack walks. bucketId drives
                // flat-mode bucket-name reveal, isRegionData marks rows the Hide Region Data toggle removes.
                RootsAndImpactModel.ItemData AppendRow(
                    int treeId, SourceIndex source, string specialName, NativeRootSize ownSize, NativeRootSize totalImpactValue,
                    MemorySize exclusiveImpactSum, MemorySize sharedProRataSum, MemorySize sharedInTotalSum,
                    ObjectStatus status, ObjectStatusWarningInfo warnInfo, int childCount, byte bucketId, bool isRegionData,
                    bool isGroupHeader = false, bool isFilterMatchAncestor = false)
                {
                    var specialNameId = specialName != null ? rowSource.AddSpecialName(specialName) : -1;
                    var row = new CompactFlatRow(treeId, source, ownSize, totalImpactValue, exclusiveImpactSum, sharedProRataSum,
                        sharedInTotalSum, status, warnInfo, childCount, specialNameId, depth: 0, bucketId, isRegionData,
                        isGroupHeader, isFilterMatchAncestor);
                    var index = store.Add(in row);
                    return new RootsAndImpactModel.ItemData(rowSource, index);
                }

                var totalImpact = new NativeRootSize();
                var totalExclusiveImpact = new MemorySize();
                // Own size never covers a row's subtree, so unlike the impact totals it accumulates at every depth.
                var totalOwnSize = new MemorySize();
                var totalSharedInTotal = new MemorySize();
                var totalSharedProRata = new MemorySize();

                // A search whose text matches a bucket's own header name (e.g. "Scenes", "Asset Bundles",
                // "Virtual Machine Memory") should reveal that whole bucket — the leaf objects inside it don't
                // carry the bucket name, so filtering them normally would empty the bucket and drop its header.
                // For such a bucket we rebuild its roots with the text filter disabled so the full contents show.
                var argsNoFilter = new BuildArgs(null, args.FlattenHierarchy, args.HideExclusiveOnlyItems,
                    args.HideRegionDataItems, args.CancellationToken);
                bool vmBucketNameMatches = args.SearchFilter != null
                    && args.SearchFilter.MatchesTextName(TextContent.RootsAndImpactBucketVirtualMachineMemory);
                // A search matching the "Managed Roots" parent name should reveal every sub-group nested under
                // it (GC Handle/Static/Stack/Finalizer/Other Roots, Frozen/Non-GC Heap), the same way matching
                // the Virtual Machine Memory name reveals its own re-homed children above.
                bool managedRootsBucketNameMatches = args.SearchFilter != null
                    && args.SearchFilter.MatchesTextName(TextContent.RootsAndImpactBucketManagedRoots);

                // Collected while iterating NativeRootAllocations — emitted as its own bucket after the loop.
                var vmChildren = new List<TreeViewItemData<RootsAndImpactModel.ItemData>>();
                var vmRootIndex = snapshot.NativeRootReferences.VMRootReferenceIndex;

                // Sub-groups nested under one "Managed Roots" parent header — collected here instead of being
                // added to rootNodes directly. Frozen/Non-GC Heap is a heap-section classification rather than a
                // root mechanism, but users think "why is this object alive" and it belongs in the same place as
                // an answer to that question.
                var managedRootsChildren = new List<TreeViewItemData<RootsAndImpactModel.ItemData>>();

                for (int b = 0; b < (int)RootAndImpactInfo.OwnershipBucket.Count; b++)
                {
                    var bucketRoots = rootAndImpactInfo.Roots[b];
                    if (bucketRoots.Count == 0)
                        continue;

                    var bucket = (RootAndImpactInfo.OwnershipBucket)b;
                    bool bucketNameMatches = (args.SearchFilter != null
                        && args.SearchFilter.MatchesTextName(BucketDisplayName(bucket)))
                        || (managedRootsBucketNameMatches && IsManagedRootsSubGroup(bucket));
                    // See ResolveSingleOwnedReference for why Stack/Finalizer/Other Roots build straight on
                    // their target instead of adding a wrapper node for the root itself.
                    bool resolveToTarget = bucket is RootAndImpactInfo.OwnershipBucket.StackRoots
                        or RootAndImpactInfo.OwnershipBucket.FinalizerRoots
                        or RootAndImpactInfo.OwnershipBucket.OtherRoots;
                    var bucketChildren = new List<TreeViewItemData<RootsAndImpactModel.ItemData>>();
                    for (long r = 0; r < bucketRoots.Count; r++)
                    {
                        args.CancellationToken.ThrowIfCancellationRequested();

                        var rootSource = bucketRoots[r];
                        var buildSource = resolveToTarget
                            ? ResolveSingleOwnedReference(rootAndImpactInfo, rootSource)
                            : rootSource;
                        bool isVmRoot = bucket == RootAndImpactInfo.OwnershipBucket.NativeRootAllocations
                            && vmRootIndex.Valid && rootSource == vmRootIndex;
                        // The VM root is re-homed into the Virtual Machine Memory bucket below, so it follows
                        // that bucket's name match rather than Native Root Allocations'.
                        bool revealWholeBucket = isVmRoot ? vmBucketNameMatches : bucketNameMatches;

                        if (args.FlattenHierarchy)
                        {
                            // The VM root is re-homed to the VM pseudo-bucket.
                            var bucketId = isVmRoot ? CompactFlatRow.BucketIdVirtualMachine : (byte)b;
                            try
                            {
                                BuildFlatSubTreeToStore(snapshot, rootAndImpactInfo, buildSource,
                                    store, ref totalImpact, ref totalExclusiveImpact, ref totalOwnSize,
                                    ref totalSharedInTotal, ref totalSharedProRata, 0, bucketId, args.CancellationToken);
                            }
                            catch (OutOfMemoryException)
                            {
                                store.Dispose();
                                throw new InvalidOperationException(string.Format(TextContent.RootsAndImpactFlattenedSnapshotTooLarge, expectedRows));
                            }
                        }
                        else
                        {
                            // Tree mode: build with filter applied (or without when the bucket name matched).
                            var rootNode = revealWholeBucket
                                ? BuildSubTree(snapshot, rootAndImpactInfo, buildSource, rowSource, ref totalOwnSize, in argsNoFilter, filteredImpactByItemId)
                                : BuildSubTree(snapshot, rootAndImpactInfo, buildSource, rowSource, ref totalOwnSize, in args, filteredImpactByItemId);
                            if (rootNode.HasValue)
                            {
                                if (isVmRoot)
                                    vmChildren.Add(rootNode.Value);
                                else
                                    bucketChildren.Add(rootNode.Value);
                            }
                        }
                    }

                    // Group static roots by namespace hierarchy with single-child collapsing
                    if (!args.FlattenHierarchy && bucket == RootAndImpactInfo.OwnershipBucket.StaticRoots && bucketChildren.Count > 0)
                        bucketChildren = GroupByNamespace(filteredImpactByItemId, rowSource, bucketChildren);

                    // Group frozen/Non-GC heap objects by managed type: which types occupy the frozen heap is
                    // what a user triages by, and there can be very many of these rows.
                    if (!args.FlattenHierarchy && bucket == RootAndImpactInfo.OwnershipBucket.NonGCHeapSections && bucketChildren.Count > 0)
                        bucketChildren = GroupManagedObjectsByType(filteredImpactByItemId, snapshot, rowSource, bucketChildren);

                    if (!args.FlattenHierarchy && bucketChildren.Count > 0)
                    {
                        // Each bucket header carries its own predefined category id so it routes to a dedicated Selection
                        // Details description; fall back to a plain incremented row id only if a bucket lacks a mapping.
                        var bucketCategory = BucketCategoryId(bucket);
                        var bucketItemId = bucketCategory >= 0 ? bucketCategory : m_ItemId++;
                        var bucketHeader = SumChildrenIntoGroupHeader(rowSource, bucketItemId,
                            BucketDisplayName(bucket), bucketChildren);
                        var bucketNode = new TreeViewItemData<RootsAndImpactModel.ItemData>(bucketItemId, bucketHeader, bucketChildren);
                        if (IsManagedRootsSubGroup(bucket))
                            managedRootsChildren.Add(bucketNode);
                        else
                            rootNodes.Add(bucketNode);
                        // The table total uses the discounted value so it agrees with the visible rows.
                        if (filteredImpactByItemId != null)
                            RecordFilteredForGroup(filteredImpactByItemId, bucketItemId, bucketChildren);
                        totalImpact += DisplayedTotalImpact(filteredImpactByItemId, bucketItemId, bucketHeader.TotalImpact);
                        totalExclusiveImpact += DisplayedExclusiveImpact(filteredImpactByItemId, bucketItemId, bucketHeader.ExclusiveImpactSum);
                        totalSharedInTotal += DisplayedSharedInTotal(filteredImpactByItemId, bucketItemId, bucketHeader.SharedInTotalSum);
                        totalSharedProRata += DisplayedSharedProRata(filteredImpactByItemId, bucketItemId, bucketHeader.SharedProRataSum);
                    }
                }

                if (!args.FlattenHierarchy && managedRootsChildren.Count > 0)
                {
                    var managedRootsItemId = (int)IAnalysisViewSelectable.Category.ManagedRoots;
                    var managedRootsHeader = SumChildrenIntoGroupHeader(rowSource, managedRootsItemId,
                        TextContent.RootsAndImpactBucketManagedRoots, managedRootsChildren);
                    rootNodes.Add(MakeGroupNode(filteredImpactByItemId, managedRootsItemId, managedRootsHeader, managedRootsChildren));
                }

                // One pass over EntriesMemoryMap to collect:
                //   • Unoccupied VirtualMachine-type heap sections (always needed for the VM bucket
                //     regardless of HideRegionDataItems, since this is intrinsic VM overhead)
                //   • All other non-object categories (used only when !HideRegionDataItems)
                var vmEmptyHeapSections = new MemorySize();
                var vmHeapRepresentativeSource = default(SourceIndex); // first VM heap section seen — for details panel
                var nativeReserved = new MemorySize();
                var managedEmpty = new MemorySize();
                // (Size, FirstSource) — the representative SourceIndex powers the SystemMemoryRegionDescription
                // details panel when one of these sub-items is selected.
                var untrackedByName = new Dictionary<string, (MemorySize Size, SourceIndex FirstSource)>();
                var executables = new MemorySize();

                snapshot.EntriesMemoryMap.ForEachFlatWithResidentSize(
                    (index, address, size, residentSize, source) =>
                {
                    var mem = new MemorySize(size, residentSize);
                    switch (source.Id)
                    {
                        case SourceIndex.SourceId.NativeMemoryRegion:
                            nativeReserved += mem;
                            break;

                        case SourceIndex.SourceId.ManagedHeapSection:
                        {
                            var sectionType = snapshot.ManagedHeapSections.SectionType[source.Index];
                            switch (sectionType)
                            {
                                case MemorySectionType.VirtualMachine:
                                    // The natively-allocated portions of VM-type sections are already
                                    // in the ownership tree via VMRootReferenceIndex. The flat iteration
                                    // here shows only the unoccupied gaps within those sections, which
                                    // space the VM has not allocated through Unity's native allocators.
                                    vmEmptyHeapSections += mem;
                                    if (!vmHeapRepresentativeSource.Valid)
                                        vmHeapRepresentativeSource = source;
                                    break;
                                case MemorySectionType.GarbageCollector:
                                    managedEmpty += mem;
                                    break;
                            }
                            break;
                        }

                        case SourceIndex.SourceId.SystemMemoryRegion:
                            switch (snapshot.EntriesMemoryMap.GetPointType(source))
                            {
                                case EntriesMemoryMapCache.PointType.Mapped:
                                    executables += mem;
                                    break;
                                case EntriesMemoryMapCache.PointType.Untracked:
                                case EntriesMemoryMapCache.PointType.Shared:
                                case EntriesMemoryMapCache.PointType.Device:
                                case EntriesMemoryMapCache.PointType.AndroidRuntime:
                                {
                                    var regionName = source.GetName(snapshot);
                                    if (!untrackedByName.TryGetValue(regionName, out var existing))
                                        existing = (new MemorySize(), source); // first source wins for details
                                    untrackedByName[regionName] = (existing.Size + mem, existing.FirstSource);
                                    break;
                                }
                            }
                            break;
                    }
                });

                // VM Heap Reserved child is region data; VM bucket from ownership tree is always emitted.
                // Fold the unoccupied VM heap sections into vmChildren when region data is shown.
                // Flat mode keeps region data in the store regardless (the model's Hide Region Data filter gates it).
                if (!args.HideRegionDataItems || args.FlattenHierarchy)
                {
                    if (vmEmptyHeapSections.Committed > 0
                        && (args.SearchFilter == null || args.FlattenHierarchy || vmBucketNameMatches
                            || args.SearchFilter.PassesFlat(TextContent.RootsAndImpactVMHeapWithoutNativeAllocations, ObjectStatus.VirtualMachine)))
                    {
                        var heapSize = new NativeRootSize { ManagedSize = vmEmptyHeapSections };
                        var heapItemId = m_ItemId++;
                        // enables ManagedMemoryHeapDescription in the details panel
                        var heapData = AppendRow(heapItemId, vmHeapRepresentativeSource, TextContent.RootsAndImpactVMHeapWithoutNativeAllocations,
                            heapSize, heapSize, heapSize.SumUp(), default, default, ObjectStatus.VirtualMachine,
                            ObjectStatusWarningInfo.FromStatus(ObjectStatus.VirtualMachine), childCount: 0,
                            bucketId: CompactFlatRow.BucketIdVirtualMachine, isRegionData: true);
                        totalOwnSize += heapData.OwnSizeSum;
                        if (args.FlattenHierarchy)
                        {
                            totalImpact += heapSize;
                            totalExclusiveImpact += heapData.ExclusiveImpactSum;
                            totalSharedInTotal += heapData.SharedInTotalSum;
                            totalSharedProRata += heapData.SharedProRataSum;
                        }
                        else
                            vmChildren.Add(new TreeViewItemData<RootsAndImpactModel.ItemData>(heapItemId, heapData));
                    }

                }

                // Virtual Machine Memory bucket — natively-rooted VM allocations + unoccupied VM heap.
                // Always emitted (not gated by HideRegionDataItems) so the ownership-tree VM items appear
                // regardless of the region data toggle.
                if (!args.FlattenHierarchy && vmChildren.Count > 0)
                {
                    // Do not give the bucket a SourceIndex since it is an aggregate of multiple sources;
                    // this also ensures it is treated as a top level Bucket Group node
                    var vmBucketItemId = (int)IAnalysisViewSelectable.Category.VirtualMachineMemory;
                    var vmBucketHeader = SumChildrenIntoGroupHeader(rowSource, vmBucketItemId,
                        TextContent.RootsAndImpactBucketVirtualMachineMemory, vmChildren, default);
                    rootNodes.Add(new TreeViewItemData<RootsAndImpactModel.ItemData>(vmBucketItemId, vmBucketHeader, vmChildren));
                    if (filteredImpactByItemId != null)
                        RecordFilteredForGroup(filteredImpactByItemId, vmBucketItemId, vmChildren);
                    totalImpact += DisplayedTotalImpact(filteredImpactByItemId, vmBucketItemId, vmBucketHeader.TotalImpact);
                    totalExclusiveImpact += DisplayedExclusiveImpact(filteredImpactByItemId, vmBucketItemId, vmBucketHeader.ExclusiveImpactSum);
                    totalSharedInTotal += DisplayedSharedInTotal(filteredImpactByItemId, vmBucketItemId, vmBucketHeader.SharedInTotalSum);
                    totalSharedProRata += DisplayedSharedProRata(filteredImpactByItemId, vmBucketItemId, vmBucketHeader.SharedProRataSum);
                }

                // Unrooted Native Subsystem Allocations — native allocations Unity reported that are
                // neither rooted to a native root reference nor referenced by anything the crawler can
                // reach. They are absent from the ownership tree, so without this node the table total
                // would fall short of the snapshot total. Sized from UnreferencedUnrootedMemory (NOT
                // ProcessedNativeRoots.UnrootedMemory) so the referenced-but-unrooted allocations that
                // ARE already attributed in the ownership tree are not double-counted.
                // This is allocation data, not region data, so it is emitted regardless of HideRegionDataItems.
                // Flat mode keeps the unrooted-allocations row in the store regardless (the model's Hide
                // Exclusive-Only filter gates it, since it presents as a pure-exclusive leaf).
                if (!args.HideExclusiveOnlyItems || args.FlattenHierarchy)
                {
                    var unreferencedUnrooted = rootAndImpactInfo.UnreferencedUnrootedMemory;
                    if (unreferencedUnrooted.NativeSize.Committed > 0
                        && (args.SearchFilter == null || args.FlattenHierarchy
                            || args.SearchFilter.PassesFlat(TextContent.RootsAndImpactBucketUnrootedNativeSubsystemAllocations, ObjectStatus.Unrooted)))
                    {
                        // The aggregate of unrooted-and-unreferenced allocations is keyed by this fake
                        // SourceIndex (the same one All Of Memory uses), which GetStatus maps to Unrooted.
                        var unrootedSource = new SourceIndex(SourceIndex.SourceId.None, (long)SourceIndex.SpecialNoneCase.UnrootedAllocation);
                        var unrootedSize = new NativeRootSize { NativeSize = unreferencedUnrooted.NativeSize };
                        var unrootedId = (int)IAnalysisViewSelectable.Category.UnrootedNativeSubsystemAllocations;
                        // Tree mode: group header so it satisfies the AllLeafNodes test (the fake
                        // unrooted SourceIndex is not "Valid"); flat mode: a plain leaf.
                        var unrootedData = AppendRow(unrootedId, unrootedSource, TextContent.RootsAndImpactBucketUnrootedNativeSubsystemAllocations,
                            unrootedSize, unrootedSize, unrootedSize.SumUp(), default, default, ObjectStatus.Unrooted,
                            ObjectStatusWarningInfo.FromStatus(ObjectStatus.Unrooted), childCount: 0,
                            bucketId: CompactFlatRow.BucketIdNone, isRegionData: false, isGroupHeader: !args.FlattenHierarchy);
                        if (!args.FlattenHierarchy)
                            rootNodes.Add(new TreeViewItemData<RootsAndImpactModel.ItemData>(unrootedId, unrootedData));
                        totalImpact += unrootedSize;
                        totalExclusiveImpact += unrootedData.ExclusiveImpactSum;
                        totalOwnSize += unrootedData.OwnSizeSum;
                        totalSharedInTotal += unrootedData.SharedInTotalSum;
                        totalSharedProRata += unrootedData.SharedProRataSum;
                    }
                }

                // Flat mode keeps region data in the store regardless (the model's Hide Region Data filter gates it).
                if (!args.HideRegionDataItems || args.FlattenHierarchy)
                {
                    // Static-field bytes whose storage falls inside an untracked region (older IL2CPP captures
                    // where the VM is reported as untracked OS memory, not native allocations) are attributed as
                    // Managed to their ManagedType root. Subtract them from the SPECIFIC untracked section they
                    // sit in so the same bytes aren't also counted here as native untracked (a double-count that
                    // otherwise leaves the table over by the static-field total on such snapshots).
                    var staticByUntracked = rootAndImpactInfo.StaticBytesByUntrackedRegionName;
                    if (staticByUntracked != null)
                    {
                        foreach (var kvp in staticByUntracked)
                        {
                            if (!untrackedByName.TryGetValue(kvp.Key, out var entry))
                                continue;
                            var reduced = entry.Size.Committed > kvp.Value.Committed ? entry.Size - kvp.Value : new MemorySize();
                            untrackedByName[kvp.Key] = (reduced, entry.FirstSource);
                        }
                    }

                    // The GfxSize already attributed to rooted objects in the ownership tree also
                    // appears in the OS-reported system memory map as Device-type regions (the Graphics
                    // Driver does not expose per-allocation addresses, so that memory cannot be matched
                    // to specific OS pages and ends up as untracked system memory).
                    // Subtract the attributed committed GfxSize from the largest untracked entries
                    // first to avoid double-counting, mirroring AllTrackedMemory's approach.
                    // Resident size is left unchanged: Device-type regions carry no resident data.
                    // The subtracted amount is the snapshot-wide accounted total rather than this view's own
                    // totalImpact, so that graphics memory with a known owner cannot reappear as Untracked whenever
                    // a view filter hides that owner.
                    var gfxToSubtract = snapshot.ProcessedNativeRoots.AccountedGraphicsResourceMemory.Committed;
                    if (gfxToSubtract > 0)
                    {
                        var sorted = new List<KeyValuePair<string, (MemorySize Size, SourceIndex FirstSource)>>(untrackedByName);
                        sorted.Sort((l, r) => -l.Value.Size.Committed.CompareTo(r.Value.Size.Committed));
                        for (int i = 0; i < sorted.Count; i++)
                        {
                            if (gfxToSubtract == 0) break;
                            var entry = sorted[i];
                            if (entry.Value.Size.Committed > gfxToSubtract)
                            {
                                // Partially reduce this entry; resident set to 0 per AllTrackedMemory convention.
                                untrackedByName[entry.Key] = (new MemorySize(entry.Value.Size.Committed - gfxToSubtract, 0), entry.Value.FirstSource);
                                gfxToSubtract = 0;
                            }
                            else
                            {
                                gfxToSubtract -= entry.Value.Size.Committed;
                                untrackedByName.Remove(entry.Key);
                            }
                        }
                    }

                    // Emit a top-level node for each non-empty category.
                    // • categoryId: when set to a predefined IAnalysisViewSelectable.Category value, the
                    //   factory routes to a category-specific SimpleDetailsViewController (with the right
                    //   description) instead of the generic NonTypedGroupDescription fallback.
                    // • source: stored SourceIndex for sub-items so the factory can show region-level details.
                    // isGroupHeader:true satisfies the AllLeafNodes test (no valid SourceIndex for aggregates).
                    // StatusCell uses ChildCount==0 (not IsGroupHeader) to show the status label on leaves.
                    void AddRegionNode(string name, NativeRootSize regionSize, ObjectStatus status, in BuildArgs args,
                        List<TreeViewItemData<RootsAndImpactModel.ItemData>> children = null,
                        int categoryId = -1,
                        SourceIndex source = default)
                    {
                        if (regionSize.SumUp().Committed == 0)
                            return;
                        // Flat mode builds the store unfiltered (the model re-selects later), so don't drop rows here.
                        if (args.SearchFilter != null && !args.FlattenHierarchy && !args.SearchFilter.PassesFlat(name, status))
                            return;
                        var itemId = categoryId >= 0 ? categoryId : m_ItemId++;
                        // Tree mode: isGroupHeader=true satisfies AllLeafNodes (no valid source).
                        // Flat mode: isGroupHeader=false so Flattened_ProducesNoGroupHeaders passes
                        //            and StatusCell treats the node as a leaf (shows status name).
                        // A header's own size is its children's, which the rows below already show and the total adds up.
                        var hasChildren = !args.FlattenHierarchy && children?.Count > 0;
                        var ownSize = hasChildren ? default : regionSize;
                        var itemData = AppendRow(itemId, source, name, ownSize, regionSize, regionSize.SumUp(), default, default,
                            status, ObjectStatusWarningInfo.FromStatus(status), children?.Count ?? 0,
                            bucketId: CompactFlatRow.BucketIdNone, isRegionData: true, isGroupHeader: !args.FlattenHierarchy);
                        if (!args.FlattenHierarchy)
                            rootNodes.Add(new TreeViewItemData<RootsAndImpactModel.ItemData>(itemId, itemData, children));
                        totalImpact += regionSize;
                        totalExclusiveImpact += itemData.ExclusiveImpactSum;
                        totalOwnSize += itemData.OwnSizeSum;
                        if (hasChildren)
                            foreach (var child in children)
                                totalOwnSize += child.data.OwnSizeSum;
                        totalSharedInTotal += itemData.SharedInTotalSum;
                        totalSharedProRata += itemData.SharedProRataSum;
                    }

                    AddRegionNode(TextContent.RootsAndImpactBucketReservedNative,
                        new NativeRootSize { NativeSize = nativeReserved }, ObjectStatus.Reserved, in args,
                        categoryId: (int)IAnalysisViewSelectable.Category.NativeReserved);
                    AddRegionNode(TextContent.RootsAndImpactBucketEmptyManagedHeap,
                        new NativeRootSize { ManagedSize = managedEmpty }, ObjectStatus.Reserved, in args,
                        categoryId: (int)IAnalysisViewSelectable.Category.ManagedReserved);

                    // Untracked: flat individual items in flatten mode; group header with named children otherwise.
                    if (!args.FlattenHierarchy && untrackedByName.Count > 0)
                    {
                        var sortedUntracked = new List<KeyValuePair<string, (MemorySize Size, SourceIndex FirstSource)>>(untrackedByName);
                        sortedUntracked.Sort((l, r) => -l.Value.Size.Committed.CompareTo(r.Value.Size.Committed));

                        var untrackedChildren = new List<TreeViewItemData<RootsAndImpactModel.ItemData>>(sortedUntracked.Count);
                        var untrackedTotal = new MemorySize();
                        foreach (var entry in sortedUntracked)
                        {
                            var childSize = new NativeRootSize { NativeSize = entry.Value.Size };
                            var childItemId = m_ItemId++;
                            // enables SystemMemoryRegionDescription in the details panel
                            var childData = AppendRow(childItemId, entry.Value.FirstSource, entry.Key, childSize, childSize,
                                childSize.SumUp(), default, default, ObjectStatus.Untracked,
                                ObjectStatusWarningInfo.FromStatus(ObjectStatus.Untracked), childCount: 0,
                                bucketId: CompactFlatRow.BucketIdNone, isRegionData: true);
                            untrackedChildren.Add(new TreeViewItemData<RootsAndImpactModel.ItemData>(childItemId, childData));
                            untrackedTotal += entry.Value.Size;
                        }
                        AddRegionNode(TextContent.RootsAndImpactBucketUntracked,
                            new NativeRootSize { NativeSize = untrackedTotal }, ObjectStatus.Untracked, in args,
                            untrackedChildren, categoryId: (int)IAnalysisViewSelectable.Category.Untracked);
                    }
                    else if (args.FlattenHierarchy && untrackedByName.Count > 0)
                    {
                        // Flat mode: emit each region individually, with its SourceIndex for the details panel.
                        var sortedUntracked = new List<KeyValuePair<string, (MemorySize Size, SourceIndex FirstSource)>>(untrackedByName);
                        sortedUntracked.Sort((l, r) => -l.Value.Size.Committed.CompareTo(r.Value.Size.Committed));
                        foreach (var entry in sortedUntracked)
                            AddRegionNode(entry.Key, new NativeRootSize { NativeSize = entry.Value.Size },
                                ObjectStatus.Untracked, in args, source: entry.Value.FirstSource);
                    }
                    // No else: when untrackedByName is empty the Untracked node would be zero-sized,
                    // and AddRegionNode skips zero-sized nodes anyway.

                    AddRegionNode(TextContent.RootsAndImpactBucketExecutables,
                        new NativeRootSize { NativeSize = executables }, ObjectStatus.ExecutablesAndMapped, in args,
                        categoryId: (int)IAnalysisViewSelectable.Category.ExecutablesAndMapped);
                }

                if (args.FlattenHierarchy)
                {
                    // The store deliberately holds every row unfiltered, so that later filter and Hide-toggle changes
                    // are an index re-selection in the model rather than a rebuild. totalImpact is that unfiltered
                    // baseline, and the initial ApplyFlatFilter narrows the first page and the size-bar total down to
                    // whatever is active right now.
                    var flatModel = new RootsAndImpactModel(rowSource, m_FlattenedPageSize,
                        totalImpact, snapshot.ProcessedNativeRoots.TotalMemoryInSnapshot, totalExclusiveImpact,
                        totalOwnSize, totalSharedInTotal, totalSharedProRata);
                    flatModel.ApplyFlatFilter(args.SearchFilter, args.HideExclusiveOnlyItems, args.HideRegionDataItems);
                    return flatModel;
                }

                // For the AssetBundle savings deep-link this sum of filtered top-level impacts equals the widget's
                // TotalAssetBundleRecoverable, and shrinks with the visible rows under further filtering.
                var modelTotalImpact = totalImpact;
                if (!args.FlattenHierarchy
                    && args.OriginalStatusMask == RootsAndImpactSummaryModelBuilder.UnloadableAssetBundleStatusMask
                    && snapshot.RootAndImpactInfo.SuccessfullyBuilt
                    && filteredImpactByItemId != null)
                {
                    var filteredTotal = default(NativeRootSize);
                    foreach (var node in rootNodes)
                        filteredTotal += filteredImpactByItemId.TryGetValue(node.id, out var filtered) ? filtered.TotalImpact : node.data.TotalImpact;
                    modelTotalImpact = filteredTotal;
                }

                return new RootsAndImpactModel(rootNodes, rowSource, modelTotalImpact, snapshot.ProcessedNativeRoots.TotalMemoryInSnapshot, totalExclusiveImpact, totalOwnSize, totalSharedInTotal, totalSharedProRata, filteredImpactByItemId);
            }
            catch
            {
                store.Dispose();
                throw;
            }
        }

        public RootsAndImpactModel Build(RootsAndImpactModel baseModel, IList<int> treeNodeIdFilter)
        {
            if (treeNodeIdFilter == null)
                return baseModel;

            var tree = new List<TreeViewItemData<RootsAndImpactModel.ItemData>>();
            var totalImpact = new NativeRootSize();
            var totalExclusiveImpact = new MemorySize();
            var totalOwnSize = new MemorySize();
            var totalSharedInTotal = new MemorySize();
            var totalSharedProRata = new MemorySize();
            var stack = new Stack<TreeViewItemData<RootsAndImpactModel.ItemData>>();
            foreach (var id in treeNodeIdFilter)
            {
                var item = baseModel.RootNodes.GetItemById(ref m_IdToIndex, id);
                stack.Push(item);
            }
            while (stack.Count > 0)
            {
                var treeNode = stack.Pop();
                if (treeNode.hasChildren)
                {
                    foreach (var child in treeNode.children)
                        stack.Push(child);
                }
                else
                {
                    tree.Add(new TreeViewItemData<RootsAndImpactModel.ItemData>(treeNode.id, treeNode.data));
                    totalImpact += treeNode.data.TotalImpact;
                    totalExclusiveImpact += treeNode.data.ExclusiveImpactSum;
                    totalOwnSize += treeNode.data.OwnSizeSum;
                    totalSharedInTotal += treeNode.data.SharedInTotalSum;
                    totalSharedProRata += treeNode.data.SharedProRataSum;
                }
            }

            // The tree's ItemData values are redirects into baseModel's row source; retain a reference so it
            // outlives baseModel if the table view controller disposes baseModel before this derived model.
            baseModel.RowSource?.Retain();
            return new RootsAndImpactModel(tree, baseModel.RowSource, totalImpact, baseModel.TotalSnapshotMemorySize, totalExclusiveImpact, totalOwnSize, totalSharedInTotal, totalSharedProRata);
        }

        static ObjectStatusWarningInfo AggregateWarningInfo(List<TreeViewItemData<RootsAndImpactModel.ItemData>> items)
        {
            var result = default(ObjectStatusWarningInfo);
            for (int i = 0; i < items.Count; i++)
                result |= items[i].data.WarnInfo;
            return result;
        }

        /// <summary>
        /// When both a text search and an explicit status button filter are active, a node is a
        /// "direct match" only if it satisfies BOTH independently (AND). Otherwise OR semantics apply.
        /// PassesFlat(name) and PassesFlat(status) each check their respective component alone.
        /// </summary>
        static bool NodePassesAllFilters(in BuildArgs args, string nodeName, ObjectStatus status) =>
            args.HasCombinedTextAndStatusFilter
                ? args.SearchFilter.PassesFlat(nodeName) && args.SearchFilter.PassesFlat(status)
                : args.SearchFilter.PassesFlat(nodeName, status);

        /// <summary>
        /// Same as the two-argument overload, but also matches on the item's own type name(s)
        /// (see <see cref="GetTypeNamesForFiltering"/>) so that searching for e.g. "System.Action" or a native
        /// type name finds every object of that type, not just its type root. Each candidate in typeNames that
        /// is null (sources with no distinct type name, or the check wasn't applicable) is skipped, in which
        /// case this reduces to the base overload.
        /// </summary>
        static bool NodePassesAllFilters(in BuildArgs args, string n, in TypeNamesForFiltering typeNames, ObjectStatus s) =>
            NodePassesAllFilters(in args, n, s)
            || (typeNames.Native != null && NodePassesAllFilters(in args, typeNames.Native, s))
            || (typeNames.Managed != null && NodePassesAllFilters(in args, typeNames.Managed, s))
            || (typeNames.ManagedBase != null && NodePassesAllFilters(in args, typeNames.ManagedBase, s));

        // A node passes if it matches on its own (name+status), OR via a synthesized status it also stands for:
        //   • a gfx-owning native object additionally matches ObjectStatus.GraphicsBuffers (surfacing it to the
        //     GraphicsBuffers filter directly instead of via a synthetic child row that gave the parent a
        //     "Mixed" status);
        //   • a Prefab book-keeping entry additionally matches ObjectStatus.PrefabRoot, so the dedicated
        //     "Prefab" filter still selects it even though its displayed status is forwarded from its root
        //     Transform (see ObjectStatusComputation.GetStatus).
        // For a pure text filter these branches are no-ops (they reduce to the same name check), so they only
        // affect status filtering.
        internal static bool NodePassesFilterIncludingSynthesizedStatuses(in BuildArgs args, string n, in TypeNamesForFiltering typeNames, ObjectStatus s, bool ownsGraphics, bool isPrefab) =>
            NodePassesAllFilters(in args, n, in typeNames, s)
            || (ownsGraphics && NodePassesAllFilters(in args, n, in typeNames, ObjectStatus.GraphicsBuffers))
            || (isPrefab && NodePassesAllFilters(in args, n, in typeNames, ObjectStatus.PrefabRoot));

        /// <summary>
        /// Up to three type-name candidates to also check against the search filter for a source whose own
        /// display name is an object/instance name rather than its type (e.g. a ManagedObject's name is a
        /// pointer/preview, and a NativeObject's name is its scene object name — neither is its type). Mirrors
        /// the native/managed/managed-base type name candidates <see cref="UnityObjectsModelBuilder"/> checks
        /// for the same reason. All three are null for sources with no distinct type name (including a
        /// ManagedType or NativeType root itself, whose Name already is the type name).
        /// </summary>
        internal readonly struct TypeNamesForFiltering
        {
            public readonly string Native;
            public readonly string Managed;
            public readonly string ManagedBase;

            public TypeNamesForFiltering(string native, string managed, string managedBase)
            {
                Native = native;
                Managed = managed;
                ManagedBase = managedBase;
            }
        }

        internal static TypeNamesForFiltering GetTypeNamesForFiltering(CachedSnapshot snapshot, in SourceIndex source)
        {
            switch (source.Id)
            {
                case SourceIndex.SourceId.ManagedObject:
                {
                    var info = snapshot.CrawledData.ManagedObjects[source.Index];
                    return new TypeNamesForFiltering(null, info.IsKnownType ? snapshot.TypeDescriptions.TypeDescriptionName[info.ITypeDescription] : null, null);
                }
                case SourceIndex.SourceId.NativeObject:
                {
                    var typeIndex = snapshot.NativeObjects.NativeTypeArrayIndex[source.Index];
                    var nativeTypeName = snapshot.NativeTypes.TypeName[typeIndex];

                    string managedTypeName = null;
                    var managedObjectIndex = snapshot.NativeObjects.ManagedObjectIndex[source.Index];
                    if (managedObjectIndex >= 0)
                    {
                        var info = snapshot.CrawledData.ManagedObjects[managedObjectIndex];
                        managedTypeName = info.IsKnownType ? snapshot.TypeDescriptions.TypeDescriptionName[info.ITypeDescription] : null;
                    }

                    // The managed base type registered for this native type (e.g. MonoBehaviour), distinct
                    // from the concrete instance type above — present even without a live managed shell.
                    string managedBaseTypeName = null;
                    var managedBaseTypeIndex = snapshot.TypeDescriptions.UnifiedTypeInfoNative[typeIndex].ManagedBaseTypeIndexForNativeType;
                    if (managedBaseTypeIndex is not CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid)
                        managedBaseTypeName = snapshot.TypeDescriptions.TypeDescriptionName[managedBaseTypeIndex];

                    return new TypeNamesForFiltering(nativeTypeName, managedTypeName, managedBaseTypeName);
                }
                default:
                    return default;
            }
        }

        /// <summary>
        /// Status-only filter matcher for the flat-mode discount walk; accepts exactly the nodes flat mode emits as
        /// rows, mirroring <see cref="NodePassesFilterIncludingSynthesizedStatuses"/>.
        /// </summary>
        readonly struct StatusFilterTargetMatcher : RootAndImpactInfo.IImpactPathTargetMatcher
        {
            readonly CachedSnapshot m_Snapshot;
            readonly BuildArgs m_Args;
            readonly bool m_FilterMatchesGraphicsOwners;

            public StatusFilterTargetMatcher(CachedSnapshot snapshot, in BuildArgs args)
            {
                m_Snapshot = snapshot;
                m_Args = args;
                m_FilterMatchesGraphicsOwners = args.SearchFilter.PassesFlat(ObjectStatus.GraphicsBuffers);
            }

            public bool Matches(SourceIndex node) =>
                NodePassesFilterIncludingSynthesizedStatuses(in m_Args, null, default,
                    m_Snapshot.ObjectStatusComputation.GetStatus(node),
                    ownsGraphics: m_FilterMatchesGraphicsOwners && ComputeOwnSize(m_Snapshot, node).GfxSize.Committed > 0,
                    isPrefab: node.Id == SourceIndex.SourceId.Prefab);
        }

        /// <summary>
        /// Matches nodes in one status set, independent of the build's filter. The all-savings deep-link discounts
        /// a row only against its own category, since the widget dedups each category alone.
        /// </summary>
        readonly struct StatusMaskTargetMatcher : RootAndImpactInfo.IImpactPathTargetMatcher
        {
            readonly CachedSnapshot m_Snapshot;
            readonly ulong m_StatusMask;

            public StatusMaskTargetMatcher(CachedSnapshot snapshot, ulong statusMask)
            {
                m_Snapshot = snapshot;
                m_StatusMask = statusMask;
            }

            public bool Matches(SourceIndex node) =>
                (m_StatusMask & (1ul << (int)m_Snapshot.ObjectStatusComputation.GetStatus(node))) != 0;
        }

        TreeViewItemData<RootsAndImpactModel.ItemData>? BuildSubTree(
            CachedSnapshot snapshot,
            RootAndImpactInfo rootAndImpactInfo,
            SourceIndex source,
            RootsAndImpactRowSource rowSource,
            ref MemorySize totalOwnSize,
            in BuildArgs args,
            Dictionary<int, RootsAndImpactModel.FilteredImpact> filteredImpactByItemId)
        {
            // Iterative traversal to avoid stack overflow on arbitrarily deep trees. Each node's row is reserved
            // in the store at first visit, in pre-order, and each TreeViewItemData only carries a redirect to it.
            // WarnInfo and ChildCount depend on which children survive the filter, so the reserved row starts out
            // a placeholder and is overwritten at finalize; a node the filter or a hide toggle drops has its row
            // truncated back out, which is only sound because such a row is always the store's current tail.
            // Ownership rows resolve their name from Source (SpecialNameId -1), so `name` here only ever feeds
            // filtering and is never stored in the row.
            // Each work item: (source, name, status, childrenToPop, insideMatchingSubtree, rowIndex, typeNamesForFilter)
            //   childrenToPop == -1  → first visit: enumerate children, reserve a row, re-push as finalize
            //                          insideMatchingSubtree = "a parent/ancestor matched the filter"
            //                          typeNamesForFilter is unset on input — recomputed fresh on first visit
            //   childrenToPop >= 0   → finalize: pop children and assemble node
            //                          insideMatchingSubtree = "this node OR any ancestor matched the filter"
            //                          typeNamesForFilter carries the value computed on this node's first visit
            var store = rowSource.Store;
            var workStack = new Stack<(SourceIndex Source, string Name, ObjectStatus Status, int ChildrenToPop, bool InsideMatchingSubtree, long RowIndex, TypeNamesForFiltering TypeNamesForFilter)>();
            var resultStack = new Stack<TreeViewItemData<RootsAndImpactModel.ItemData>?>();
            // Filtered contributions of children whose impact skipped their parent, keyed by the ancestor that received it.
            Dictionary<SourceIndex, RootsAndImpactModel.FilteredImpact> redirectedFilteredImpact = null;

            // Reused per first-visit below to avoid a per-node List<SourceIndex> allocation (millions of
            // nodes on large snapshots). Its contents are fully consumed (pushed onto the work stack) within
            // the same first-visit before any other node is processed, so reusing one instance is safe within
            // this traversal. Kept method-local rather than an instance field so that concurrent Build calls
            // (a superseded build is only cooperatively cancelled and may still be running when the next one
            // starts) don't share — and clear out from under each other — the same list.
            var childSources = new List<SourceIndex>();

            workStack.Push((source, source.GetName(snapshot), snapshot.ObjectStatusComputation.GetStatus(source), -1, false, -1, default));

            // A single root's subtree can span millions of nodes, so Build's per-root cancellation check isn't
            // granular enough on its own. Sampled rather than polled per node to keep it off the hot path.
            var visitCount = 0;

            while (workStack.Count > 0)
            {
                if ((++visitCount & k_CancellationCheckMask) == 0)
                    args.CancellationToken.ThrowIfCancellationRequested();

                var (src, name, status, childrenToPop, insideMatchingSubtree, rowIndex, typeNamesForFilter) = workStack.Pop();

                if (childrenToPop < 0)
                {
                    // First visit: enumerate children. Filtering is deferred to the finalize step so that
                    // post-order aggregation (WarnInfo, children list) can be used to decide inclusion.
                    // In flat mode the filter is applied per-item via RootsAndImpactModel.BuildRowFilterPredicate
                    // instead. The incoming typeNamesForFilter is unset on a first visit (see the work-item
                    // comment above) — compute the real value now and carry it into the finalize re-push below.
                    // Gated on an actual text filter (not just args.SearchFilter != null): a status-only
                    // filter can never match a type name, so skip the per-node lookup entirely for it.
                    typeNamesForFilter = args.HasTextFilter ? GetTypeNamesForFiltering(snapshot, src) : default;

                    // If this node matches the filter, its children are "inside a matching subtree":
                    // they represent the impact owned by the matching object and should always be shown.
                    // In flat mode treat every node as matching so the shortcut has no effect. Uses the same
                    // synthesized-status predicate as the finalize step's selfPassesFilter (below), so a node
                    // that only matches via a synthesized status (GraphicsBuffers, PrefabRoot) reveals its
                    // subtree exactly like a node that matches on its own name/type does.
                    bool selfMatches = args.SearchFilter == null || args.FlattenHierarchy
                        || NodePassesFilterIncludingSynthesizedStatuses(in args, name, in typeNamesForFilter, status,
                            ComputeOwnSize(snapshot, src).GfxSize.Committed > 0, src.Id == SourceIndex.SourceId.Prefab);
                    bool childrenInsideMatchingSubtree = insideMatchingSubtree || selfMatches;

                    childSources.Clear();
                    var supported = SourceIndexToRootAndImpactInfoMapper.IsSourceIdSupported(src.Id);
                    if (supported)
                    {
                        using (var iter = new ReferencesIterator(rootAndImpactInfo, src, includeUnownedReferences: false))
                        {
                            while (iter.MoveNext())
                                childSources.Add(iter.Current.Reference);
                        }
                    }

                    // Reserve the row now so its position in the store reflects pre-order visitation; the
                    // placeholder's WarnInfo/ChildCount/impact are overwritten once children are known, below.
                    var reservedRowIndex = -1L;
                    if (supported)
                    {
                        var placeholderRow = new CompactFlatRow(m_ItemId++, src, default, default, default, default, default,
                            status, default, childCount: 0, specialNameId: -1, depth: 0, CompactFlatRow.BucketIdNone, isRegionData: false);
                        reservedRowIndex = store.Add(in placeholderRow);
                    }

                    // Re-push self as the finalize step (with the "self-or-ancestor matched" flag),
                    // then push children in reverse so first child is processed first.
                    workStack.Push((src, name, status, childSources.Count, childrenInsideMatchingSubtree, reservedRowIndex, typeNamesForFilter));
                    for (int i = childSources.Count - 1; i >= 0; i--)
                    {
                        var childSrc = childSources[i];
                        workStack.Push((childSrc, childSrc.GetName(snapshot), snapshot.ObjectStatusComputation.GetStatus(childSrc), -1, childrenInsideMatchingSubtree, -1, default));
                    }
                }
                else
                {
                    // Finalize: pop child results (pushed in order, so top-of-stack is last child).
                    var children = new List<TreeViewItemData<RootsAndImpactModel.ItemData>>(childrenToPop);
                    for (int i = 0; i < childrenToPop; i++)
                    {
                        var childResult = resultStack.Pop();
                        if (childResult.HasValue)
                            children.Add(childResult.Value);
                    }
                    children.Reverse();

                    // Virtual group/structural nodes inside RootAndImpactInfo use SourceId.None as a
                    // stand-in; they have no entry in the nested impact arrays, so skip them. No row was
                    // reserved for them (see the `supported` gate above), so there is nothing to truncate.
                    if (!SourceIndexToRootAndImpactInfoMapper.IsSourceIdSupported(src.Id))
                    {
                        resultStack.Push(null);
                        continue;
                    }

                    ref readonly var impact = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in rootAndImpactInfo.Impact, src);
                    var ownSize = ComputeOwnSize(snapshot, src);

                    // TODO: Optionally surface the graphics resources owned by this native root as an
                    // explicit child row (carrying the node's own gfx) so they show as their own entries.
                    // Disabled for now because adding a GraphicsBuffers child gives the parent NativeObject a
                    // "Mixed" status, which muddies the UX. Revisit as an opt-in BuildArgs option. In the
                    // meantime, gfx-owning nodes are surfaced to the GraphicsBuffers status filter directly
                    // (see NodePassesFilterIncludingSynthesizedStatuses) rather than via a child node.
                    // if (ownSize.GfxSize.Committed > 0)
                    // {
                    //     bool gfxChildPasses = args.SearchFilter == null || args.FlattenHierarchy
                    //         || NodePassesAllFilters(in args, TextContent.RootsAndImpactGraphicsResourceChild, ObjectStatus.GraphicsBuffers);
                    //     if (gfxChildPasses)
                    //     {
                    //         var gfxOnly = new NativeRootSize { GfxSize = ownSize.GfxSize };
                    //         var gfxData = new RootsAndImpactModel.ItemData(
                    //             name: TextContent.RootsAndImpactGraphicsResourceChild,
                    //             source: src,
                    //             ownSize: gfxOnly,
                    //             totalImpact: gfxOnly,
                    //             exclusiveImpact: gfxOnly,
                    //             sharedProRata: default,
                    //             sharedInTotal: default,
                    //             status: ObjectStatus.GraphicsBuffers,
                    //             warnInfo: ObjectStatusWarningInfo.FromStatus(ObjectStatus.GraphicsBuffers),
                    //             childCount: 0);
                    //         children.Insert(0, new TreeViewItemData<RootsAndImpactModel.ItemData>(m_ItemId++, gfxData));
                    //     }
                    // }

                    // When HideExclusiveOnlyItems is true, hide leaf items that only have
                    // exclusive impact (not referenced by anything else). This keeps the tree focused
                    // on items with shared/cross-referenced impact.
                    var checkExclusiveOnly = args.HideExclusiveOnlyItems && children.Count == 0;
                    // we'll need the sums later anyway, so compute them here to avoid double work
                    MemorySize sharedProRataSum = checkExclusiveOnly ? impact.SharedProRata.SumUp() : default;
                    MemorySize exclusiveSum = checkExclusiveOnly ? impact.Exclusive.SumUp() : default;
                    if (checkExclusiveOnly && sharedProRataSum.Committed == 0 && exclusiveSum.Committed > 0)
                    {
                        // A leaf discard: children.Count == 0 guarantees this row is still the store's tail.
                        store.Truncate(rowIndex);
                        resultStack.Push(null);
                        continue;
                    }

                    // Filter in non-flat mode. A node is kept if:
                    //   • it is inside a matching subtree (a parent/self already matched → show its full impact), OR
                    //   • it has surviving children (some descendant matched → keep as ancestor).
                    // Nodes that don't directly match the filter are de-emphasized (isFilterMatchAncestor)
                    // regardless of whether they are ancestors above or descendants below the match.
                    // Flat mode applies its own filter via RootsAndImpactModel.BuildRowFilterPredicate.
                    bool isFilterMatchAncestor = false;
                    if (args.SearchFilter != null && !args.FlattenHierarchy)
                    {
                        bool selfPassesFilter = NodePassesFilterIncludingSynthesizedStatuses(in args, name, in typeNamesForFilter, status, ownSize.GfxSize.Committed > 0, src.Id == SourceIndex.SourceId.Prefab);
                        if (!insideMatchingSubtree && !selfPassesFilter && children.Count == 0)
                        {
                            // A leaf discard: children.Count == 0 guarantees this row is still the store's tail.
                            store.Truncate(rowIndex);
                            resultStack.Push(null);
                            continue;
                        }
                        isFilterMatchAncestor = !selfPassesFilter;
                    }

                    if (!checkExclusiveOnly)
                    {
                        // in case the sums were not already computed above, compute them now for the model
                        exclusiveSum = impact.Exclusive.SumUp();
                        sharedProRataSum = impact.SharedProRata.SumUp();
                    }

                    var warnInfo = ObjectStatusWarningInfo.FromStatus(status) | (children.Count > 0 ? AggregateWarningInfo(children) : default);
                    var treeId = store.ElementAt(rowIndex).TreeId;
                    store.ElementAt(rowIndex) = new CompactFlatRow(treeId, src, ownSize, impact.TotalImpact, exclusiveSum,
                        sharedProRataSum, impact.SharedInTotal.SumUp(), status, warnInfo, children.Count,
                        specialNameId: -1, depth: 0, CompactFlatRow.BucketIdNone, isRegionData: false,
                        isGroupHeader: false, isFilterMatchAncestor: isFilterMatchAncestor,
                        referrerCount: rootAndImpactInfo.GetReferrerCount(src),
                        parentExcludedFromSplit: rootAndImpactInfo.TryGetOrphanedSplitRedirect(src, out _));

                    var itemData = new RootsAndImpactModel.ItemData(rowSource, rowIndex);
                    totalOwnSize += itemData.OwnSizeSum;
                    if (filteredImpactByItemId != null && isFilterMatchAncestor && !insideMatchingSubtree)
                    {
                        var filtered = default(RootsAndImpactModel.FilteredImpact);
                        for (int i = 0; i < children.Count; i++)
                        {
                            filtered += ChildFilteredImpact(filteredImpactByItemId, children[i]);
                            if (children[i].data.ParentExcludedFromSplit
                                && rootAndImpactInfo.TryGetOrphanedSplitRedirect(children[i].data.Source, out var redirectTarget))
                            {
                                redirectedFilteredImpact ??= new Dictionary<SourceIndex, RootsAndImpactModel.FilteredImpact>();
                                redirectedFilteredImpact.TryGetValue(redirectTarget, out var pending);
                                redirectedFilteredImpact[redirectTarget] = pending + UnsplitFilteredImpact(filteredImpactByItemId, children[i]);
                            }
                        }
                        if (redirectedFilteredImpact != null && redirectedFilteredImpact.Remove(src, out var redirectedHere))
                            filtered += redirectedHere;

                        // Pro-rate the bundle's own cost by visible children over total root entries, as AssetBundleSavings does.
                        if (src.Id == SourceIndex.SourceId.NativeObject
                            && args.OriginalStatusMask.HasValue && ((args.OriginalStatusMask.Value & RootsAndImpactSummaryModelBuilder.UnloadableAssetBundleStatusMask) != 0)
                            && rootAndImpactInfo.TryGetAssetBundleSavings(src, out var savings))
                        {
                            var totalEntries = savings.UsedRootEntries + savings.UnusedRootEntries;
                            if (totalEntries > 0)
                            {
                                var ownShare = ownSize.ScaleByFraction((ulong)children.Count, (ulong)totalEntries);
                                filtered += new RootsAndImpactModel.FilteredImpact(ownShare, ownShare.SumUp(), default, default);
                            }
                        }
                        filteredImpactByItemId[treeId] = filtered;
                    }
                    resultStack.Push(new TreeViewItemData<RootsAndImpactModel.ItemData>(treeId, itemData, children.Count > 0 ? children : null));
                }
            }

            return resultStack.Count > 0 ? resultStack.Pop() : null;
        }

        // Small because it is allocated once per top-level root; DynamicArray grows on demand.
        const long k_DefaultStackSizeForFlatNodesCollection = 16;

        void BuildFlatSubTreeToStore(
            CachedSnapshot snapshot,
            RootAndImpactInfo rootAndImpactInfo,
            SourceIndex source,
            FlattenedRowStore<CompactFlatRow> store,
            ref NativeRootSize totalImpact,
            ref MemorySize totalExclusiveImpact,
            ref MemorySize totalOwnSize,
            ref MemorySize totalSharedInTotal,
            ref MemorySize totalSharedProRata,
            ushort depth,
            byte bucketId,
            CancellationToken cancellationToken)
        {
            // Iterative DFS writing CompactFlatRow records straight into the store. Follows BuildSubTree's
            // traversal but needs neither name resolution (SpecialNameId -1 defers that to rehydration) nor
            // filter-match ancestry: the store is always an unfiltered superset that the model re-selects over.
            //
            // Rows are reserved at first visit, in pre-order, because AccumulateFilteredTotal's depth stack
            // requires store order to be DFS pre-order. WarnInfo depends on descendants, so it and the rest of
            // the impact data are patched into the reserved slot at finalize.
            var workStack = new Stack<(SourceIndex Source, ObjectStatus Status, int ChildrenToPop, ushort Depth, long RowIndex)>();
            // Accumulates each node's aggregated WarnInfo in post-order; parent pops childrenToPop entries.
            var warnStack = new DynamicArray<ObjectStatusWarningInfo>(0, k_DefaultStackSizeForFlatNodesCollection, Allocator.Persistent);
            // Reused per first-visit to avoid a per-node List allocation. See BuildSubTree for rationale.
            var childSources = new List<SourceIndex>();

            try
            {
                workStack.Push((source, snapshot.ObjectStatusComputation.GetStatus(source), -1, depth, -1));

                // See BuildSubTree: sampled so a single huge root can still be abandoned promptly.
                var visitCount = 0;

                while (workStack.Count > 0)
                {
                    if ((++visitCount & k_CancellationCheckMask) == 0)
                        cancellationToken.ThrowIfCancellationRequested();

                    var (src, status, childrenToPop, nodeDepth, rowIndex) = workStack.Pop();

                    if (childrenToPop < 0)
                    {
                        // First visit: enumerate children (same gate as BuildSubTree).
                        childSources.Clear();
                        var supported = SourceIndexToRootAndImpactInfoMapper.IsSourceIdSupported(src.Id);
                        if (supported)
                        {
                            using (var iter = new ReferencesIterator(rootAndImpactInfo, src, includeUnownedReferences: false))
                            {
                                while (iter.MoveNext())
                                    childSources.Add(iter.Current.Reference);
                            }
                        }

                        // Reserve the row now so its position in the store reflects pre-order visitation; the
                        // placeholder's WarnInfo is overwritten once descendants have been visited, below.
                        var reservedRowIndex = -1L;
                        if (supported)
                        {
                            var placeholderRow = new CompactFlatRow(m_ItemId++, src, default, default, default, default, default,
                                status, default, childSources.Count, specialNameId: -1, nodeDepth, bucketId, isRegionData: false);
                            reservedRowIndex = store.Add(in placeholderRow);
                        }

                        workStack.Push((src, status, childSources.Count, nodeDepth, reservedRowIndex));
                        // Saturate rather than wrap: a wrapped depth would make a deep node look like a subtree root
                        // to AccumulateFilteredTotal and get its already-subsumed impact added to the total again.
                        var childDepth = nodeDepth < ushort.MaxValue ? (ushort)(nodeDepth + 1) : ushort.MaxValue;
                        for (int i = childSources.Count - 1; i >= 0; i--)
                        {
                            var childSrc = childSources[i];
                            workStack.Push((childSrc, snapshot.ObjectStatusComputation.GetStatus(childSrc), -1, childDepth, -1));
                        }
                    }
                    else
                    {
                        // Finalize: aggregate child WarnInfos and patch the reserved row with the real data.
                        // Virtual/structural nodes (SourceId.None) have no impact data and reserved no row; push a
                        // default sentinel so the parent's warnStack pop count stays balanced.
                        if (!SourceIndexToRootAndImpactInfoMapper.IsSourceIdSupported(src.Id))
                        {
                            // childrenToPop == 0 for unsupported sources (first-visit never enumerates their children).
                            warnStack.Push(default);
                            continue;
                        }

                        ref readonly var impact = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in rootAndImpactInfo.Impact, src);
                        var ownSize = ComputeOwnSize(snapshot, src);

                        var warnInfo = ObjectStatusWarningInfo.FromStatus(status);
                        for (int i = 0; i < childrenToPop; i++)
                            warnInfo |= warnStack.Pop();
                        warnStack.Push(warnInfo);

                        var treeId = store.ElementAt(rowIndex).TreeId;
                        store.ElementAt(rowIndex) = new CompactFlatRow(treeId, src, ownSize, impact.TotalImpact,
                            impact.Exclusive.SumUp(), impact.SharedProRata.SumUp(), impact.SharedInTotal.SumUp(),
                            status, warnInfo, childrenToPop, specialNameId: -1, nodeDepth, bucketId, isRegionData: false,
                            referrerCount: rootAndImpactInfo.GetReferrerCount(src),
                            parentExcludedFromSplit: rootAndImpactInfo.TryGetOrphanedSplitRedirect(src, out _));

                        totalOwnSize += ownSize.SumUp();
                        // Accumulated once, at the outermost finalize of this subtree (TotalImpact is transitive:
                        // it already covers every descendant's bytes, so summing it per node would multiply the
                        // total roughly by tree depth).
                        if (nodeDepth == depth)
                        {
                            totalImpact += impact.TotalImpact;
                            totalExclusiveImpact += impact.Exclusive.SumUp();
                            totalSharedInTotal += impact.SharedInTotal.SumUp();
                            totalSharedProRata += impact.SharedProRata.SumUp();
                        }
                    }
                }
            }
            finally
            {
                if (warnStack.IsCreated)
                    warnStack.Dispose();
            }
        }

        static NativeRootSize ComputeOwnSize(CachedSnapshot snapshot, SourceIndex source)
        {
            switch (source.Id)
            {
                case SourceIndex.SourceId.ManagedType:
                    // Single source of truth with AttributeImpact: committed = static field bytes,
                    // resident deduced from the type's static-storage address vs the resident-page bitset.
                    return new NativeRootSize { ManagedSize = snapshot.RootAndImpactInfo.GetStaticFieldOwnSize(snapshot, source.Index) };

                case SourceIndex.SourceId.NativeRootReference:
                    return new NativeRootSize
                    {
                        NativeSize = !snapshot.MetaData.CaptureFlags.HasFlag(CaptureFlags.NativeAllocations)
                            ? snapshot.ProcessedNativeRoots.Data[source.Index].AccumulatedRootSizes.NativeSize : default,
                        GfxSize = snapshot.ProcessedNativeRoots.Data[source.Index].AccumulatedRootSizes.GfxSize
                    };

                case SourceIndex.SourceId.NativeObject:
                    var processedRootIndex = snapshot.ProcessedNativeRoots.RootIdToMappedIndex(snapshot.NativeObjects.RootReferenceId[source.Index]);
                    var size = snapshot.ProcessedNativeRoots.Data[processedRootIndex].AccumulatedRootSizes;
                    size.ManagedSize = default; // managed shell may be owned by someone else
                    return size;

                case SourceIndex.SourceId.ManagedObject:
                    return new NativeRootSize { ManagedSize = snapshot.ProcessedNativeRoots.ManagedSizes[source.Index] };

                case SourceIndex.SourceId.NativeAllocation:
                    return new NativeRootSize { NativeSize = snapshot.ProcessedNativeRoots.AllocationSizes[source.Index] };

                case SourceIndex.SourceId.Scene:
                case SourceIndex.SourceId.Prefab:
                case SourceIndex.SourceId.GCHandleIndex:
                case SourceIndex.SourceId.ManagedRoot:
                case SourceIndex.SourceId.ManagedHeapSection:
                default:
                    return default;
            }
        }

        /// <summary>
        /// Whether <paramref name="bucket"/>'s header nests under the "Managed Roots" parent group instead of
        /// being a top-level node — the answers to "why is this object alive": GCHandle/Static/Stack/Finalizer/
        /// Other roots, plus Frozen/Non-GC Heap (a heap-section classification, not a root mechanism, but shown
        /// alongside the others for the same reason a user would look at them together).
        /// </summary>
        internal static bool IsManagedRootsSubGroup(RootAndImpactInfo.OwnershipBucket bucket) => bucket switch
        {
            RootAndImpactInfo.OwnershipBucket.GCHandleRoots => true,
            RootAndImpactInfo.OwnershipBucket.StaticRoots => true,
            RootAndImpactInfo.OwnershipBucket.StackRoots => true,
            RootAndImpactInfo.OwnershipBucket.FinalizerRoots => true,
            RootAndImpactInfo.OwnershipBucket.OtherRoots => true,
            RootAndImpactInfo.OwnershipBucket.NonGCHeapSections => true,
            _ => false,
        };

        static string BucketDisplayName(RootAndImpactInfo.OwnershipBucket bucket)
        {
            var index = (int)bucket;
            // Fall back to the enum name if the array was reset by statics cleanup and not yet rebuilt,
            // or if a new bucket value lacks a mapped name.
            if (k_BucketDisplayNames != null && index >= 0 && index < k_BucketDisplayNames.Length && k_BucketDisplayNames[index] != null)
                return k_BucketDisplayNames[index];
            return bucket.ToString();
        }

        /// <summary>
        /// Maps a <see cref="CompactFlatRow.BucketId"/> to its header display name for the flattened view's
        /// bucket-name reveal (a text search matching a bucket header reveals that whole bucket). Returns the
        /// Virtual Machine Memory name for the VM sentinel and <c>null</c> for the none sentinel.
        /// </summary>
        internal static string BucketDisplayNameForBucketId(byte bucketId)
        {
            if (bucketId == CompactFlatRow.BucketIdVirtualMachine)
                return TextContent.RootsAndImpactBucketVirtualMachineMemory;
            if (bucketId < (int)RootAndImpactInfo.OwnershipBucket.Count)
                return BucketDisplayName((RootAndImpactInfo.OwnershipBucket)bucketId);
            return null;
        }

        /// <summary>
        /// The <see cref="IAnalysisViewSelectable.Category"/> id used as the bucket header's tree-item id so it routes
        /// to its own Selection Details description. Falls back to -1 (caller assigns a plain dynamic id) if the array
        /// was reset by statics cleanup or a bucket lacks a mapped category.
        /// </summary>
        static int BucketCategoryId(RootAndImpactInfo.OwnershipBucket bucket)
        {
            var index = (int)bucket;
            if (k_BucketCategories != null && index >= 0 && index < k_BucketCategories.Length && k_BucketCategories[index] != 0)
                return k_BucketCategories[index];
            return -1;
        }

        /// <summary>
        /// Resolves a 1:1 root (Stack/Finalizer/Other Root) to the single object it owns, via one
        /// <see cref="ReferencesIterator"/> step — Stack/Finalizer/Other Roots have no status or identity of
        /// their own to show once their target reports its own Held status directly, so the tree/flat build
        /// skips straight to the target instead of adding a wrapper node for the root. Falls back to
        /// <paramref name="rootSource"/> itself if the root has no owned reference (only theoretically
        /// reachable: seeding already drops any root whose target never resolved before it reaches the tree
        /// build).
        /// </summary>
        static SourceIndex ResolveSingleOwnedReference(RootAndImpactInfo rootAndImpactInfo, SourceIndex rootSource)
        {
            using var iter = new ReferencesIterator(rootAndImpactInfo, rootSource, includeUnownedReferences: false);
            return iter.MoveNext() ? iter.Current.Reference : rootSource;
        }

        /// <summary>
        /// Regroups already-built per-root ManagedObject nodes under one synthetic header per managed type,
        /// the same post-hoc treatment <see cref="GroupByNamespace"/> gives Static Roots. Nodes whose source
        /// isn't a ManagedObject, or whose type failed to resolve during crawling, are passed through ungrouped.
        /// </summary>
        List<TreeViewItemData<RootsAndImpactModel.ItemData>> GroupManagedObjectsByType(
            Dictionary<int, RootsAndImpactModel.FilteredImpact> filteredImpactByItemId,
            CachedSnapshot snapshot,
            RootsAndImpactRowSource rowSource,
            List<TreeViewItemData<RootsAndImpactModel.ItemData>> objectNodes)
        {
            var objectsByType = new Dictionary<SourceIndex, List<TreeViewItemData<RootsAndImpactModel.ItemData>>>();
            var result = new List<TreeViewItemData<RootsAndImpactModel.ItemData>>();

            foreach (var node in objectNodes)
            {
                var source = node.data.Source;
                if (source.Id != SourceIndex.SourceId.ManagedObject)
                {
                    result.Add(node);
                    continue;
                }

                var typeIndex = snapshot.CrawledData.ManagedObjects[source.Index].ITypeDescription;
                if (typeIndex is TypeDescriptionEntriesCache.ITypeInvalid)
                {
                    result.Add(node);
                    continue;
                }
                var typeSource = new SourceIndex(SourceIndex.SourceId.ManagedType, typeIndex);
                if (!objectsByType.TryGetValue(typeSource, out var typeObjects))
                {
                    typeObjects = new List<TreeViewItemData<RootsAndImpactModel.ItemData>>();
                    objectsByType[typeSource] = typeObjects;
                }
                typeObjects.Add(node);
            }

            foreach (var kvp in objectsByType)
            {
                var typeGroupItemId = m_ItemId++;
                var typeGroupHeader = SumChildrenIntoGroupHeader(rowSource, typeGroupItemId,
                    kvp.Key.GetName(snapshot), kvp.Value, kvp.Key);
                result.Add(MakeGroupNode(filteredImpactByItemId, typeGroupItemId, typeGroupHeader, kvp.Value));
            }
            return result;
        }

        /// <summary>
        /// Groups type nodes by their namespace hierarchy with single-child collapsing.
        /// E.g., if types are in NamespaceA.Sub.TypeA, NamespaceA.Sub.TypeB, NamespaceA.TypeC,
        /// produces: NamespaceA { Sub { TypeA, TypeB }, TypeC }.
        /// Single-child intermediate namespaces are collapsed: NamespaceA.Sub instead of NamespaceA > Sub
        /// (but only if Sub has no siblings under NamespaceA).
        ///
        /// Generic types are handled specially: dots inside angle brackets are never treated as namespace
        /// separators. Multiple concrete instantiations of the same generic base are grouped under a
        /// placeholder header (e.g. "Dictionary&lt;T1, T2&gt;"). Within that, each type argument is
        /// namespace-grouped using the same trie-with-collapsing approach, with labels prefixed by
        /// "T1: ", "T2: ", etc. For multi-param generics the grouping is nested: T1's namespace trie
        /// contains T1 type leaves, each of which nests T2's namespace trie, and so on.
        /// </summary>
        List<TreeViewItemData<RootsAndImpactModel.ItemData>> GroupByNamespace(
            Dictionary<int, RootsAndImpactModel.FilteredImpact> filteredImpactByItemId,
            RootsAndImpactRowSource rowSource,
            List<TreeViewItemData<RootsAndImpactModel.ItemData>> typeNodes)
        {
            var root = new NamespaceTrieNode();

            foreach (var node in typeNodes)
            {
                // The type name is split into namespace segments + generic base/args once per type and
                // cached (see GetTypeNameComponents); the grouping below consumes the structured result
                // instead of re-scanning the full name string on every rebuild.
                var data = node.data;
                var components = GetTypeNameComponents(in data);

                var trieNode = root;
                foreach (var segment in components.NamespaceSegments)
                {
                    if (!trieNode.Children.TryGetValue(segment, out var child))
                    {
                        child = new NamespaceTrieNode();
                        trieNode.Children[segment] = child;
                    }
                    trieNode = child;
                }

                if (components.TypeArgs != null)
                {
                    if (!trieNode.GenericTypeInstances.TryGetValue(components.GenericBase, out var instances))
                    {
                        instances = new List<(List<string>, TreeViewItemData<RootsAndImpactModel.ItemData>)>();
                        trieNode.GenericTypeInstances[components.GenericBase] = instances;
                    }
                    instances.Add((components.TypeArgs, node));
                }
                else
                {
                    trieNode.TypeNodes.Add(node);
                }
            }

            return BuildNamespaceTreeFromTrie(filteredImpactByItemId, rowSource, root, string.Empty);
        }

        List<TreeViewItemData<RootsAndImpactModel.ItemData>> BuildNamespaceTreeFromTrie(
            Dictionary<int, RootsAndImpactModel.FilteredImpact> filteredImpactByItemId,
            RootsAndImpactRowSource rowSource,
            NamespaceTrieNode trieNode, string prefix)
        {
            var result = new List<TreeViewItemData<RootsAndImpactModel.ItemData>>();

            foreach (var kvp in trieNode.Children)
            {
                var segmentName = string.IsNullOrEmpty(prefix) ? kvp.Key : prefix + "." + kvp.Key;
                var child = kvp.Value;

                // Collapse single-child intermediate namespaces (no type or generic nodes at this level)
                while (child.Children.Count == 1 && child.TypeNodes.Count == 0 && child.GenericTypeInstances.Count == 0)
                {
                    string nextSegment = null;
                    NamespaceTrieNode nextChild = null;
                    foreach (var innerKvp in child.Children)
                    {
                        nextSegment = innerKvp.Key;
                        nextChild = innerKvp.Value;
                    }
                    segmentName = segmentName + "." + nextSegment;
                    child = nextChild;
                }

                var groupChildren = BuildNamespaceTreeFromTrie(filteredImpactByItemId, rowSource, child, string.Empty);

                if (groupChildren.Count == 0)
                    continue;
                if (groupChildren.Count == 1)
                {
                    result.Add(groupChildren[0]);  // single child — inline without namespace group wrapper
                    continue;
                }

                var groupItemId = m_ItemId++;
                var groupHeader = SumChildrenIntoGroupHeader(rowSource, groupItemId, segmentName, groupChildren);
                result.Add(MakeGroupNode(filteredImpactByItemId, groupItemId, groupHeader, groupChildren));
            }

            // Non-generic types at this namespace level
            foreach (var typeNode in trieNode.TypeNodes)
                result.Add(typeNode);

            // Generic types: one group per generic base (e.g. "Dictionary<T1, T2>"), with sub-groups
            // per type argument namespace (e.g. "T1: System" → "T1: String, ...") using the same
            // trie-with-collapsing approach as the outer namespace grouping.
            foreach (var kvp in trieNode.GenericTypeInstances)
            {
                var genericBase = kvp.Key;
                var instances = kvp.Value;

                var typeArgChildren = BuildTypeArgGroupTree(filteredImpactByItemId, rowSource, instances, 0);
                if (typeArgChildren.Count == 0) continue;

                // If the type-arg tree resolves to a single group header, merge its label into the
                // generic base label rather than creating a redundant single-child wrapper.
                // E.g. "Dictionary<T1>" → "T1: System" → children  becomes
                //      "Dictionary<T1> T1: System" → children directly.
                // Applied iteratively so cascading single-group levels all collapse.
                var effectiveBase = genericBase;
                while (typeArgChildren.Count == 1 && typeArgChildren[0].data.IsGroupHeader)
                {
                    effectiveBase = effectiveBase + " " + typeArgChildren[0].data.Name;
                    var next = new List<TreeViewItemData<RootsAndImpactModel.ItemData>>();
                    if (typeArgChildren[0].hasChildren)
                    {
                        foreach (var c in typeArgChildren[0].children)
                            next.Add(c);
                    }
                    typeArgChildren = next;
                    // The collapsed wrapper's row is never referenced by the final tree; it was appended as
                    // a header AFTER its own children, so it is always the store's current tail here — drop
                    // it rather than leaving an orphaned row behind.
                    rowSource.Store.Truncate(rowSource.Store.Count - 1);
                }

                if (typeArgChildren.Count == 0) continue;

                var genericGroupItemId = m_ItemId++;
                var genericGroupHeader = SumChildrenIntoGroupHeader(rowSource, genericGroupItemId, effectiveBase, typeArgChildren);
                result.Add(MakeGroupNode(filteredImpactByItemId, genericGroupItemId, genericGroupHeader, typeArgChildren));
            }

            return result;
        }

        /// <summary>
        /// Builds a hierarchical namespace-grouped tree for the type arguments of generic type instances.
        /// For each type argument (T1, T2, ...), builds a namespace trie with single-child collapsing,
        /// then recurses for the next type argument.
        /// </summary>
        List<TreeViewItemData<RootsAndImpactModel.ItemData>> BuildTypeArgGroupTree(
            Dictionary<int, RootsAndImpactModel.FilteredImpact> filteredImpactByItemId,
            RootsAndImpactRowSource rowSource,
            List<(List<string> TypeArgs, TreeViewItemData<RootsAndImpactModel.ItemData> Node)> instances,
            int typeArgIndex)
        {
            var totalArgs = instances[0].TypeArgs.Count;
            var argLabel = "T" + (typeArgIndex + 1);

            // Build a namespace trie for the current type argument.
            var root = new TypeArgTrieNode();
            foreach (var inst in instances)
            {
                var typeArg = inst.TypeArgs[typeArgIndex];
                var lastDot = TypeNameParsingUtility.FindLastOuterDot(typeArg);
                var ns = lastDot >= 0 ? typeArg.Substring(0, lastDot) : string.Empty;
                var shortName = lastDot >= 0 ? typeArg.Substring(lastDot + 1) : typeArg;

                var trieNode = root;
                if (ns.Length > 0)
                {
                    // Namespace segments for type args are plain identifiers — Split('.') is safe.
                    var segments = ns.Split('.');
                    foreach (var seg in segments)
                    {
                        if (!trieNode.Children.TryGetValue(seg, out var child))
                        {
                            child = new TypeArgTrieNode();
                            trieNode.Children[seg] = child;
                        }
                        trieNode = child;
                    }
                }

                if (!trieNode.TypeLeaves.TryGetValue(shortName, out var list))
                {
                    list = new List<(List<string>, TreeViewItemData<RootsAndImpactModel.ItemData>)>();
                    trieNode.TypeLeaves[shortName] = list;
                }
                list.Add(inst);
            }

            return BuildFromTypeArgTrie(filteredImpactByItemId, rowSource, root, argLabel, typeArgIndex, totalArgs);
        }

        List<TreeViewItemData<RootsAndImpactModel.ItemData>> BuildFromTypeArgTrie(
            Dictionary<int, RootsAndImpactModel.FilteredImpact> filteredImpactByItemId,
            RootsAndImpactRowSource rowSource,
            TypeArgTrieNode trie, string argLabel, int typeArgIndex, int totalArgs)
        {
            var result = new List<TreeViewItemData<RootsAndImpactModel.ItemData>>();

            // Namespace groups with single-child path collapsing.
            // Additionally, if a group would have only one child after collapsing, skip the group
            // wrapper entirely and inline that child — mirroring how the outer namespace trie only
            // groups where multiple items share a common prefix.
            foreach (var kvp in trie.Children)
            {
                var segmentName = kvp.Key;
                var child = kvp.Value;

                while (child.Children.Count == 1 && child.TypeLeaves.Count == 0)
                {
                    string nextSeg = null;
                    TypeArgTrieNode nextChild = null;
                    foreach (var inner in child.Children)
                    {
                        nextSeg = inner.Key;
                        nextChild = inner.Value;
                    }
                    segmentName = segmentName + "." + nextSeg;
                    child = nextChild;
                }

                var groupChildren = BuildFromTypeArgTrie(filteredImpactByItemId, rowSource, child, argLabel, typeArgIndex, totalArgs);
                if (groupChildren.Count == 1)
                    result.Add(groupChildren[0]);  // single child — inline without namespace group wrapper
                else if (groupChildren.Count > 1)
                {
                    var headerItemId = m_ItemId++;
                    var header = SumChildrenIntoGroupHeader(rowSource, headerItemId, argLabel + ": " + segmentName, groupChildren);
                    result.Add(MakeGroupNode(filteredImpactByItemId, headerItemId, header, groupChildren));
                }
            }

            // Type leaves at this namespace level.
            foreach (var kvp in trie.TypeLeaves)
            {
                var shortName = kvp.Key;
                var leafInstances = kvp.Value;

                if (typeArgIndex < totalArgs - 1)
                {
                    // More type arguments — recurse for the next one.
                    // Only wrap in a "Tn: ShortName" group if the next level produces ≥2 children;
                    // otherwise inline the single child (the type arg level adds no structure).
                    var subChildren = BuildTypeArgGroupTree(filteredImpactByItemId, rowSource, leafInstances, typeArgIndex + 1);
                    if (subChildren.Count == 1)
                        result.Add(subChildren[0]);  // single sub-result — skip type leaf wrapper
                    else if (subChildren.Count > 1)
                    {
                        var headerItemId = m_ItemId++;
                        var header = SumChildrenIntoGroupHeader(rowSource, headerItemId, argLabel + ": " + shortName, subChildren);
                        result.Add(MakeGroupNode(filteredImpactByItemId, headerItemId, header, subChildren));
                    }
                }
                else
                {
                    // Final type argument.
                    // Only wrap in a "Tn: ShortName" group when there are ≥2 instances sharing this
                    // short name; a single instance is emitted directly without a redundant wrapper.
                    if (leafInstances.Count == 1)
                    {
                        result.Add(leafInstances[0].Node);
                    }
                    else
                    {
                        var children = new List<TreeViewItemData<RootsAndImpactModel.ItemData>>();
                        foreach (var inst in leafInstances)
                            children.Add(inst.Node);
                        var headerItemId = m_ItemId++;
                        var header = SumChildrenIntoGroupHeader(rowSource, headerItemId, argLabel + ": " + shortName, children);
                        result.Add(MakeGroupNode(filteredImpactByItemId, headerItemId, header, children));
                    }
                }
            }

            return result;
        }

        class NamespaceTrieNode
        {
            public Dictionary<string, NamespaceTrieNode> Children = new Dictionary<string, NamespaceTrieNode>();
            public List<TreeViewItemData<RootsAndImpactModel.ItemData>> TypeNodes = new List<TreeViewItemData<RootsAndImpactModel.ItemData>>();
            /// <summary>Generic types at this namespace level: genericBasePlaceholder → [(parsedTypeArgs, node)].</summary>
            public Dictionary<string, List<(List<string> TypeArgs, TreeViewItemData<RootsAndImpactModel.ItemData> Node)>> GenericTypeInstances =
                new Dictionary<string, List<(List<string>, TreeViewItemData<RootsAndImpactModel.ItemData>)>>();
        }

        class TypeArgTrieNode
        {
            public Dictionary<string, TypeArgTrieNode> Children = new Dictionary<string, TypeArgTrieNode>();
            /// <summary>Short type name → instances with that short name for this type argument.</summary>
            public Dictionary<string, List<(List<string> TypeArgs, TreeViewItemData<RootsAndImpactModel.ItemData> Node)>> TypeLeaves =
                new Dictionary<string, List<(List<string>, TreeViewItemData<RootsAndImpactModel.ItemData>)>>();
        }

        /// <summary>
        /// Parses a type name into its generic base (with placeholder type parameters) and its concrete type arguments.
        /// Returns <c>null</c> type args for non-generic types.
        /// E.g. "Dictionary&lt;System.String, System.Int32&gt;" → ("Dictionary&lt;T1, T2&gt;", ["System.String", "System.Int32"]).
        /// </summary>
        public static (string GenericBase, List<string> TypeArgs) TryParseGenericType(string typeName)
        {
            // Find the '<' that opens the actual generic type arguments, skipping any '<>' compiler
            // tokens (e.g. the '<>' prefix in '<>c' and '<>c<T>' closure/display-class names).
            var genericStart = -1;
            for (int i = 0; i < typeName.Length; i++)
            {
                if (typeName[i] != '<') continue;
                if (i + 1 < typeName.Length && typeName[i + 1] == '>')
                {
                    i++;  // skip the paired '>'; the for-loop will advance past both chars
                    continue;
                }
                genericStart = i;
                break;
            }

            if (genericStart < 0)
                return (typeName, null);

            var baseName = typeName.Substring(0, genericStart);
            if (baseName.Length == 0)
                return (typeName, null);  // type name starts with a non-'<>' angle bracket — treat as plain

            // Find the '>' that closes the generic argument list, matching nested '<>' so the close
            // for "Dictionary<List<int>, int>" is the final '>' rather than the first one. The '>' is
            // not necessarily the last character (e.g. "List<int>[]"), so any trailing suffix is
            // captured and re-appended to the placeholder. A missing close ("Foo<") is treated as plain.
            var genericEnd = TypeNameParsingUtility.FindGenericArgumentListEnd(typeName, genericStart);
            if (genericEnd < 0)
                return (typeName, null);  // malformed: no matching '>' — treat as plain

            // Slice the argument list (between the matched '<' and '>') as a span to avoid an intermediate
            // Substring allocation; SplitTypeArgs only allocates the strings it actually returns.
            var typeArgs = TypeNameParsingUtility.SplitTypeArgs(typeName.AsSpan(genericStart + 1, genericEnd - genericStart - 1));
            var suffix = typeName.Substring(genericEnd + 1);

            var placeholder = baseName + "<";
            for (int i = 0; i < typeArgs.Count; i++)
            {
                if (i > 0) placeholder += ", ";
                placeholder += "T" + (i + 1);
            }
            placeholder += ">";
            placeholder += suffix;

            return (placeholder, typeArgs);
        }

        /// <summary>
        /// The structured components of a managed type name, parsed once for the namespace/generic
        /// grouping: the namespace split into segments, plus the generic base placeholder and concrete
        /// type arguments (<see cref="TypeArgs"/> is <c>null</c> for non-generic types). Caching these
        /// avoids re-scanning the full name string on every rebuild.
        /// </summary>
        public readonly struct ParsedTypeName
        {
            // Namespace segments outside any generic arguments, e.g. ["System", "Collections", "Generic"].
            // Empty (never null) when the type has no namespace.
            public readonly string[] NamespaceSegments;
            // For a generic type, the base with placeholder parameters, e.g. "Dictionary<T1, T2>".
            // For a non-generic type, the type part itself (unused by the grouping in that case).
            public readonly string GenericBase;
            // Concrete type arguments for a generic type, e.g. ["System.String", "System.Int32"];
            // null for a non-generic type.
            public readonly List<string> TypeArgs;

            public ParsedTypeName(string[] namespaceSegments, string genericBase, List<string> typeArgs)
            {
                NamespaceSegments = namespaceSegments;
                GenericBase = genericBase;
                TypeArgs = typeArgs;
            }
        }

        /// <summary>
        /// Returns the parsed components for a static-root node's type name, using a per-snapshot cache
        /// keyed by <see cref="RootsAndImpactModel.ItemData.Source"/>. A static root's source is a
        /// <see cref="SourceIndex.SourceId.ManagedType"/> whose name is the stable
        /// <c>TypeDescriptionName</c>, so the parse can be memoized across rebuilds. Non-type sources
        /// (not expected among static roots) are parsed without caching.
        /// </summary>
        ParsedTypeName GetTypeNameComponents(in RootsAndImpactModel.ItemData data)
        {
            var source = data.Source;
            if (source.Id != SourceIndex.SourceId.ManagedType)
                return ParseTypeNameComponents(data.Name);

            m_TypeNameComponentsCache ??= new Dictionary<SourceIndex, ParsedTypeName>();
            if (m_TypeNameComponentsCache.TryGetValue(source, out var cached))
                return cached;

            var parsed = ParseTypeNameComponents(data.Name);
            m_TypeNameComponentsCache[source] = parsed;
            return parsed;
        }

        /// <summary>
        /// Splits a managed type name into namespace segments + generic base/args in a single pass.
        /// The last '.' outside angle brackets separates namespace from type name (so generic arguments like
        /// "Dictionary<System.String, System.Int32>" are never split), and the type part is run
        /// through <see cref="TryParseGenericType"/>.
        /// </summary>
        public static ParsedTypeName ParseTypeNameComponents(string name)
        {
            var lastOuterDot = TypeNameParsingUtility.FindLastOuterDot(name);
            // lastOuterDot > 0 means there is a non-empty namespace prefix before it; lastOuterDot == 0
            // (name begins with '.') has an empty namespace and must NOT produce a stray empty segment.
            // Namespace segments never contain '<>', so a plain Split('.') is safe.
            var namespaceSegments = lastOuterDot > 0
                ? name.Substring(0, lastOuterDot).Split('.')
                : Array.Empty<string>();
            var typePart = lastOuterDot >= 0 ? name.Substring(lastOuterDot + 1) : name;

            var (genericBase, typeArgs) = TryParseGenericType(typePart);
            return new ParsedTypeName(namespaceSegments, genericBase, typeArgs);
        }

        /// <summary>
        /// A top-level row's contribution to the model's total exclusive size: its stored filtered value if it has
        /// one (see <see cref="RecordFilteredForGroup"/>), otherwise its own exclusive impact.
        /// </summary>
        static MemorySize DisplayedExclusiveImpact(
            Dictionary<int, RootsAndImpactModel.FilteredImpact> filteredImpactByItemId, int id, MemorySize ownExclusiveImpact)
        {
            return filteredImpactByItemId != null && filteredImpactByItemId.TryGetValue(id, out var filtered)
                ? filtered.ExclusiveImpactSum
                : ownExclusiveImpact;
        }

        /// <summary>
        /// Like <see cref="DisplayedExclusiveImpact"/>, for <see cref="RootsAndImpactModel.TotalImpactSize"/>.
        /// </summary>
        static NativeRootSize DisplayedTotalImpact(
            Dictionary<int, RootsAndImpactModel.FilteredImpact> filteredImpactByItemId, int id, NativeRootSize ownTotalImpact)
        {
            return filteredImpactByItemId != null && filteredImpactByItemId.TryGetValue(id, out var filtered)
                ? filtered.TotalImpact
                : ownTotalImpact;
        }

        /// <summary>
        /// <see cref="DisplayedExclusiveImpact"/> for the shared pro-rata column, so its model total is accumulated
        /// from the same displayed values the rows show.
        /// </summary>
        static MemorySize DisplayedSharedProRata(
            Dictionary<int, RootsAndImpactModel.FilteredImpact> filteredImpactByItemId, int id, MemorySize ownSharedProRata)
        {
            return filteredImpactByItemId != null && filteredImpactByItemId.TryGetValue(id, out var filtered)
                ? filtered.SharedProRataSum
                : ownSharedProRata;
        }

        /// <summary>
        /// <see cref="DisplayedExclusiveImpact"/> for the shared-in-total column, so its model total is accumulated
        /// from the same displayed values the rows show.
        /// </summary>
        static MemorySize DisplayedSharedInTotal(
            Dictionary<int, RootsAndImpactModel.FilteredImpact> filteredImpactByItemId, int id, MemorySize ownSharedInTotal)
        {
            return filteredImpactByItemId != null && filteredImpactByItemId.TryGetValue(id, out var filtered)
                ? filtered.SharedInTotalSum
                : ownSharedInTotal;
        }

        /// <summary>
        /// The filtered impact a child contributes to its ownership-tree parent: <see cref="UnsplitFilteredImpact"/>
        /// with the single-hop discount for that edge applied (see <see cref="RootAndImpactInfo.DiscountedTotalImpact"/>),
        /// so a split or redirected child does not count in full at every ancestor.
        /// </summary>
        static RootsAndImpactModel.FilteredImpact ChildFilteredImpact(
            Dictionary<int, RootsAndImpactModel.FilteredImpact> filteredImpactByItemId, TreeViewItemData<RootsAndImpactModel.ItemData> child)
        {
            var d = child.data;
            if (d.ParentExcludedFromSplit)
                return default;

            var contribution = UnsplitFilteredImpact(filteredImpactByItemId, child);

            if (d.ReferrerCount > RootAndImpactInfo.ReferrerData.SingleReferrer)
            {
                var share = contribution.TotalImpact.Divide((ulong)d.ReferrerCount, out var remainder);
                var arrivingTotal = share + RootAndImpactInfo.RemainderBonus(in remainder);
                // A divide lands in SharedProRata at the referee, never as Exclusive.
                return new RootsAndImpactModel.FilteredImpact(arrivingTotal, default, arrivingTotal.SumUp(), contribution.TotalImpact.SumUp());
            }

            return contribution;
        }

        /// <summary>The child's stored filtered impact if it has one, otherwise its own precalculated impact.</summary>
        static RootsAndImpactModel.FilteredImpact UnsplitFilteredImpact(
            Dictionary<int, RootsAndImpactModel.FilteredImpact> filteredImpactByItemId, TreeViewItemData<RootsAndImpactModel.ItemData> child)
        {
            if (filteredImpactByItemId != null && filteredImpactByItemId.TryGetValue(child.id, out var stored))
                return stored;
            var d = child.data;
            return new RootsAndImpactModel.FilteredImpact(d.TotalImpact, d.ExclusiveImpactSum, d.SharedProRataSum, d.SharedInTotalSum);
        }

        /// <summary>
        /// Sums the children's filtered contributions and records the result under the group's id. Only call while filtering.
        /// A group header is not its children's ownership parent, so no edge discount applies.
        /// </summary>
        static RootsAndImpactModel.FilteredImpact RecordFilteredForGroup(
            Dictionary<int, RootsAndImpactModel.FilteredImpact> filteredImpactByItemId, int id, List<TreeViewItemData<RootsAndImpactModel.ItemData>> children)
        {
            var filtered = default(RootsAndImpactModel.FilteredImpact);
            for (int i = 0; i < children.Count; i++)
                filtered += UnsplitFilteredImpact(filteredImpactByItemId, children[i]);
            filteredImpactByItemId[id] = filtered;
            return filtered;
        }

        /// <summary>
        /// Creates a group-header tree node and, while filtering, records its filtered impact.
        /// </summary>
        static TreeViewItemData<RootsAndImpactModel.ItemData> MakeGroupNode(
            Dictionary<int, RootsAndImpactModel.FilteredImpact> filteredImpactByItemId, int id, RootsAndImpactModel.ItemData header, List<TreeViewItemData<RootsAndImpactModel.ItemData>> children)
        {
            if (filteredImpactByItemId != null)
                RecordFilteredForGroup(filteredImpactByItemId, id, children);
            return new TreeViewItemData<RootsAndImpactModel.ItemData>(id, header, children);
        }

        static RootsAndImpactModel.ItemData SumChildrenIntoGroupHeader(
            RootsAndImpactRowSource rowSource,
            int treeId,
            string name,
            List<TreeViewItemData<RootsAndImpactModel.ItemData>> children,
            SourceIndex source = default)
        {
            var totalImpact = new NativeRootSize();
            var exclusiveImpactSum = new MemorySize();
            var sharedProRataSum = new MemorySize();
            var sharedInTotalSum = new MemorySize();
            var warnInfo = default(ObjectStatusWarningInfo);

            foreach (var child in children)
            {
                totalImpact += child.data.TotalImpact;
                exclusiveImpactSum += child.data.ExclusiveImpactSum;
                sharedProRataSum += child.data.SharedProRataSum;
                sharedInTotalSum += child.data.SharedInTotalSum;
                warnInfo |= child.data.WarnInfo;
            }

            var specialNameId = rowSource.AddSpecialName(name);
            // Group header has no own size; its impact is the sum of its children.
            var row = new CompactFlatRow(treeId, source, default, totalImpact, exclusiveImpactSum, sharedProRataSum,
                sharedInTotalSum, ObjectStatus.BucketGroup, warnInfo, children.Count, specialNameId,
                depth: 0, CompactFlatRow.BucketIdNone, isRegionData: false, isGroupHeader: true);
            var index = rowSource.Store.Add(in row);
            return new RootsAndImpactModel.ItemData(rowSource, index);
        }

        public readonly struct BuildArgs
        {
            public readonly CompoundSearchFilter SearchFilter;
            public readonly bool FlattenHierarchy;
            public readonly bool HideExclusiveOnlyItems;
            public readonly bool HideRegionDataItems;
            // True when both a text search and an explicit button-driven exclusive status filter
            // are active simultaneously in non-flat mode. In this case, a node is considered a
            // "direct match" only when it satisfies BOTH filters (AND semantics), so that nodes
            // matching just one component are de-emphasized rather than highlighted.
            public readonly bool HasCombinedTextAndStatusFilter;
            // True when a text search is active. A status-button-only filter (e.g. from
            // TableFilterManager) still supplies a non-null SearchFilter with no SearchStringFilter,
            // so callers that only need to react to actual text (like the per-node type-name lookup
            // in BuildSubTree) must check this instead of SearchFilter == null.
            public readonly bool HasTextFilter;
            /// <summary>
            /// Polled per root and, sampled, per traversed node, so that tearing down the view — which blocks
            /// until this build stops reading the snapshot — doesn't have to wait out a whole traversal.
            /// </summary>
            public readonly CancellationToken CancellationToken;
            /// <summary>
            /// The requested exclusive status mask, captured before the ctor converts it to an inclusive mask for
            /// non-flat mode. Identifies savings deep-links such as the AssetBundle one.
            /// </summary>
            public readonly ulong? OriginalStatusMask;

            public BuildArgs(
                CompoundSearchFilter searchStringFilter = null,
                bool flattenHierarchy = false,
                bool hideExclusiveOnlyItems = false,
                bool hideRegionDataItems = false,
                CancellationToken cancellationToken = default)
            {
                // Capture before conversion (which clears ExclusiveStatusMask).
                HasCombinedTextAndStatusFilter = !flattenHierarchy
                    && searchStringFilter?.ExclusiveStatusMask != null
                    && searchStringFilter?.SearchStringFilter != null;
                HasTextFilter = searchStringFilter?.SearchStringFilter != null;
                OriginalStatusMask = searchStringFilter?.ExclusiveStatusMask;

                if (!flattenHierarchy && searchStringFilter?.ExclusiveStatusMask != null)
                {
                    // Only a flattened hierarchy can apply a hard exclusive status mask — ancestors
                    // of matching nodes must still appear in tree mode. Re-issue the same filter with
                    // the exclusive mask promoted to an inclusive StatusFilter (no hard gate), so
                    // ancestors are included but non-matching nodes are still de-emphasized.
                    searchStringFilter = new CompoundSearchFilter(
                        searchStringFilter.SearchStringFilter?.FilterValue,
                        inclusiveStatusMask: searchStringFilter.ExclusiveStatusMask);
                }
                SearchFilter = searchStringFilter;
                FlattenHierarchy = flattenHierarchy;
                HideExclusiveOnlyItems = hideExclusiveOnlyItems;
                HideRegionDataItems = hideRegionDataItems;
                CancellationToken = cancellationToken;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.MemoryProfiler.Editor.Extensions;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UIElements;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Builds an AllTrackedMemoryModel.
    class AllTrackedMemoryModelBuilder
    {
        // These markers are used in Performance tests. If their usage changes, adjust the performance tests accordingly
        public const string BuildMarkerName = "AllTrackedMemoryModelBuilder.Build";
        public const string IterateHierarchyMarkerName = "AllTrackedMemoryModelBuilder.IterateHierarchy";
        public const string GenerateTreeMarkerName = "AllTrackedMemoryModelBuilder.GenerateTree";

        static readonly ProfilerMarker k_BuildMarker = new ProfilerMarker(BuildMarkerName);
        static readonly ProfilerMarker k_IterateHierarchyMarker = new ProfilerMarker(IterateHierarchyMarkerName);
        static readonly ProfilerMarker k_GenerateTreeMarker = new ProfilerMarker(GenerateTreeMarkerName);

        const string k_NativeGroupName = "Native";
        const string k_NativeObjectsGroupName = "Native Objects";
        const string k_NativeSubsystemsGroupName = "Unity Subsystems";

        const string k_ManagedGroupName = "Managed";
        const string k_ManagedObjectsGroupName = "Managed Objects";
        const string k_ManagedVMGroupName = "Virtual Machine";

        public const string GraphicsGroupName = SummaryTextContent.kAllMemoryCategoryGraphics;
        public const string UntrackedGroupName = "Untracked";
        const string k_UntrackedEstimatedGroupName = SummaryTextContent.kAllMemoryCategoryUntrackedEstimated;
        const string k_ExecutablesGroupName = "Executables & Mapped";
        const string k_UntrackedGraphicsName = "Graphics";

        const string k_AndroidRuntime = "Android Runtime";

        const string k_InvalidItemName = "Unknown";
        static readonly SourceIndex k_FakeInvalidlyRootedAllocationIndex = new SourceIndex(SourceIndex.SourceId.None, 1);
        const string k_ReservedItemName = "Reserved";

        int m_ItemId;

        public AllTrackedMemoryModelBuilder()
        {
            m_ItemId = (int)IAnalysisViewSelectable.Category.FirstDynamicId;
        }

        public AllTrackedMemoryModel Build(CachedSnapshot snapshot, in BuildArgs args)
        {
            using var _ = k_BuildMarker.Auto();

            if (!CanBuildBreakdownForSnapshot(snapshot))
                throw new UnsupportedSnapshotVersionException(snapshot);

            var tree = new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>();
            var totalMemory = new MemorySize();
            if (!args.ExcludeAll)
            {
                tree = BuildAllMemoryBreakdown(snapshot, args, out totalMemory);
                if (args.PathFilter != null)
                    tree = BuildItemsAtPathInTreeExclusively(args.PathFilter, tree);
            }

            var model = new AllTrackedMemoryModel(tree, totalMemory, args.SelectionProcessor);
            return model;
        }

        bool CanBuildBreakdownForSnapshot(CachedSnapshot snapshot)
        {
            return true;
        }

        class BuildContext
        {
            public MemorySize Total { get; set; }

            /// Native memory groups
            // Index in CachedSnapshot.NativeObjects <-> size
            public Dictionary<SourceIndex, MemorySize> NativeObjectIndex2SizeMap { get; private set; }

            // Index in CachedSnapshot.NativeRootReferences <-> size
            public Dictionary<SourceIndex, MemorySize> NativeRootReference2SizeMap { get; private set; }

            // Index in CachedSnapshot.NativeRootReferences <-> size
            public Dictionary<SourceIndex, Dictionary<SourceIndex, MemorySize>> NativeRootReference2UnsafeAllocations2SizeMap { get; private set; }

            // All native regions not known to be used by any object or allocation (reserved memory)
            // Index of CachedSnapshot.NativeMemoryRegions <-> size
            public Dictionary<SourceIndex, MemorySize> NativeRegionName2SizeMap { get; private set; }


            /// Managed memory groups
            // All managed objects grouped by type
            // Type source <-> list of objects of that type
            public Dictionary<SourceIndex, List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>> ManagedTypeName2ObjectsTreeMap { get; private set; }

            // All managed objects grouped by type and native Object Name
            // Type name <-> NativeObjectName <-> list of objects of that type
            public Dictionary<SourceIndex, Dictionary<string, List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>>> ManagedTypeName2NativeName2ObjectsTreeMap { get; private set; }

            // Sum of all memory allocated in managed sections marked as virtual machine
            public MemorySize ManagedMemoryVM { get; set; }

            // Sum of all memory allocated in managed sections to GC heap and not used by any managed object
            public MemorySize ManagedMemoryReserved { get; set; }

            /// Graphics memory group
            public MemorySize UntrackedGraphicsResources { get; set; }
            public Dictionary<SourceIndex, MemorySize> GfxObjectIndex2SizeMap { get; private set; }
            public Dictionary<SourceIndex, MemorySize> GfxReservedRegionIndex2SizeMap { get; private set; }

            /// System memory regions
            // Executables
            public Dictionary<string, MemorySize> ExecutablesName2SizeMap { get; private set; }

            /// Untracked. All memory we don't know anything useful about
            public Dictionary<string, MemorySize> UntrackedRegionsName2SizeMap { get; private set; }

            /// Android platform specific (total)
            public MemorySize AndroidRuntime { get; set; }

            public BuildContext()
            {
                NativeObjectIndex2SizeMap = new Dictionary<SourceIndex, MemorySize>();
                NativeRootReference2SizeMap = new Dictionary<SourceIndex, MemorySize>();
                NativeRootReference2UnsafeAllocations2SizeMap = new Dictionary<SourceIndex, Dictionary<SourceIndex, MemorySize>>();
                NativeRegionName2SizeMap = new Dictionary<SourceIndex, MemorySize>();

                ManagedTypeName2ObjectsTreeMap = new Dictionary<SourceIndex, List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>>();
                ManagedTypeName2NativeName2ObjectsTreeMap = new Dictionary<SourceIndex, Dictionary<string, List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>>>();

                GfxObjectIndex2SizeMap = new Dictionary<SourceIndex, MemorySize>();
                GfxReservedRegionIndex2SizeMap = new Dictionary<SourceIndex, MemorySize>();

                ExecutablesName2SizeMap = new Dictionary<string, MemorySize>();
                UntrackedRegionsName2SizeMap = new Dictionary<string, MemorySize>();
            }
        }

        /// <summary>
        /// Takes MemoryEntriesHierarchy and builds hierarchy tree out of it
        /// </summary>
        List<TreeViewItemData<AllTrackedMemoryModel.ItemData>> BuildAllMemoryBreakdown(
            CachedSnapshot snapshot,
            in BuildArgs args,
            out MemorySize total)
        {
            var context = BuildAllMemoryContext(snapshot, args);

            // Build hierarchy out of pre-built data structures
            var rootItems = new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>();
            using (k_GenerateTreeMarker.Auto())
            {
                if (BuildNativeTree(snapshot, args, context.NativeObjectIndex2SizeMap, context.NativeRootReference2SizeMap, context.NativeRootReference2UnsafeAllocations2SizeMap, context.NativeRegionName2SizeMap, out var nativeMemoryTree))
                    rootItems.Add(nativeMemoryTree);

                if (BuildManagedTree(snapshot, args, context.ManagedTypeName2ObjectsTreeMap, context.ManagedTypeName2NativeName2ObjectsTreeMap, context.ManagedMemoryVM, context.ManagedMemoryReserved, out var managedMemoryTree))
                    rootItems.Add(managedMemoryTree);

                if (BuildTreeFromGroupByNameMap(k_ExecutablesGroupName, (int)IAnalysisViewSelectable.Category.ExecutablesAndMapped, context.ExecutablesName2SizeMap, out var executablesTree))
                    rootItems.Add(executablesTree);

                // When we include Gfx resources we alter Untracked group to match the total size and need to make it clear in the table
                var untrackedName = args.BreakdownGfxResources ? k_UntrackedEstimatedGroupName : UntrackedGroupName;
                var selectionCategory = args.BreakdownGfxResources ? (int)IAnalysisViewSelectable.Category.UnknownEstimated : (int)IAnalysisViewSelectable.Category.Unknown;
                if (BuildTreeFromGroupByNameMap(untrackedName, selectionCategory, context.UntrackedRegionsName2SizeMap, out var unaccountedRegionsTree))
                    rootItems.Add(unaccountedRegionsTree);

                if (BuildGraphicsMemoryTree(snapshot, args, context.GfxObjectIndex2SizeMap, context.GfxReservedRegionIndex2SizeMap, out var graphicsTree))
                    rootItems.Add(graphicsTree);

                if (BuildAndroidRuntimeTree(args, context.AndroidRuntime, out var androidTree))
                    rootItems.Add(androidTree);
            }

            total = context.Total;

            return rootItems;
        }

        BuildContext BuildAllMemoryContext(
            CachedSnapshot snapshot,
            in BuildArgs args)
        {
            using var _ = k_IterateHierarchyMarker.Auto();

            var context = new BuildContext();
            var disambiguateUnityObjects = args.DisambiguateUnityObjects;

            var allocationRootsToSplit = new List<string>(MemoryProfilerSettings.AlwaysSplitRootAllocations);
            if (args.AllocationRootNamesToSplitIntoSuballocations != null)
                allocationRootsToSplit.AddRange(args.AllocationRootNamesToSplitIntoSuballocations);
            var areaAndObjectNamesToSplit = new (string, string)[allocationRootsToSplit.Count];

            for (var i = 0; i < allocationRootsToSplit.Count; i++)
            {
                var areaAndObjectName = allocationRootsToSplit[i].Split(':');
                areaAndObjectNamesToSplit[i] =
                    (areaAndObjectName[0],
                    areaAndObjectName.Length > 1 ? areaAndObjectName[1] : null);
            }

            for (long i = 0; i < snapshot.NativeRootReferences.Count; i++)
            {
                foreach (var areaAndObjectName in areaAndObjectNamesToSplit)
                {
                    if (snapshot.NativeRootReferences.AreaName[i] == areaAndObjectName.Item1)
                    {
                        if (string.IsNullOrEmpty(areaAndObjectName.Item2) || snapshot.NativeRootReferences.ObjectName[i] == areaAndObjectName.Item2)
                        {
                            context.NativeRootReference2UnsafeAllocations2SizeMap.Add(new SourceIndex(SourceIndex.SourceId.NativeRootReference, i), new Dictionary<SourceIndex, MemorySize>());
                        }
                    }
                }
            }

            if (MemoryProfilerSettings.FeatureFlags.EnableUnknownUnknownAllocationBreakdown_2024_10)
            {
                context.NativeRootReference2UnsafeAllocations2SizeMap.Add(k_FakeInvalidlyRootedAllocationIndex, new Dictionary<SourceIndex, MemorySize>());
            }

            // Extract all objects from the hierarchy and build group specific maps
            var filterArgs = args;
            snapshot.EntriesMemoryMap.ForEachFlatWithResidentSize((index, address, size, residentSize, source) =>
            {
                var memorySize = new MemorySize(size, residentSize);

                context.Total += memorySize;

                // Add items to respective group container
                switch (source.Id)
                {
                    case SourceIndex.SourceId.NativeObject:
                        ProcessNativeObject(snapshot, source, memorySize, filterArgs, context.NativeObjectIndex2SizeMap);
                        break;
                    case SourceIndex.SourceId.NativeAllocation:
                        ProcessNativeAllocation(snapshot, source, memorySize, filterArgs, context);
                        break;
                    case SourceIndex.SourceId.NativeMemoryRegion:
                        ProcessNativeRegion(snapshot, source, memorySize, filterArgs, context.NativeRegionName2SizeMap, context.GfxReservedRegionIndex2SizeMap);
                        break;
                    case SourceIndex.SourceId.ManagedObject:
                        ProcessManagedObject(snapshot, source, memorySize, filterArgs, context.ManagedTypeName2ObjectsTreeMap, disambiguateUnityObjects ? context.ManagedTypeName2NativeName2ObjectsTreeMap : null);
                        break;
                    case SourceIndex.SourceId.ManagedHeapSection:
                        ProcessManagedHeap(snapshot, source, memorySize, filterArgs, context);
                        break;

                    case SourceIndex.SourceId.SystemMemoryRegion:
                        ProcessSystemRegion(snapshot, source, memorySize, filterArgs, context);
                        break;

                    default:
                        Debug.Assert(false, $"Unknown memory region source id ({source.Id}), please report a bug");
                        break;
                }
            });

            // [Legacy] If we don't have SystemMemoryRegionsInfo, take the total value from legacy memory stats
            // Nb! If you change this, change similar code in AllTrackedMemoryModelBuilder / UnityObjectsModelBuilder / AllMemorySummaryModelBuilder
            var memoryStats = snapshot.MetaData.TargetMemoryStats;
            if (memoryStats.HasValue && !snapshot.HasSystemMemoryRegionsInfo && (memoryStats.Value.TotalVirtualMemory > 0))
            {
                var untracked = context.Total.Committed > memoryStats.Value.TotalVirtualMemory ? 0 : memoryStats.Value.TotalVirtualMemory - context.Total.Committed;

                context.UntrackedRegionsName2SizeMap[UntrackedGroupName] = new MemorySize(untracked, 0);
                context.Total = new MemorySize(memoryStats.Value.TotalVirtualMemory, 0);
            }
            // Add graphics resources to context separately, as we don't have them in memory map.
            // We compile different sources, trying to come with reasonable results:
            // - Platform-specific reported usage (memoryStats.Value.GraphicsUsedMemory)
            // - Sum of all system regions identified to be possible graphics resource mapping
            // - Estimated size of all tracked graphics resources created
            // after that we reassign "untracked" memory to the estimated graphics resources size
            {
                var graphicsUsedMemory = memoryStats?.GraphicsUsedMemory ?? 0;
                // Compensate if system regions graphics regions are smaller than platform-reported value
                var untrackedToReassign = new MemorySize();
                if (args.BreakdownGfxResources && (context.UntrackedGraphicsResources.Committed < graphicsUsedMemory))
                {
                    untrackedToReassign = new MemorySize(graphicsUsedMemory - context.UntrackedGraphicsResources.Committed, 0);
                    context.UntrackedGraphicsResources = new MemorySize(graphicsUsedMemory, 0);
                }

                // Add estimated resources
                if (snapshot.HasGfxResourceReferencesAndAllocators)
                {
                    // Add estimated graphics resources
                    ProcessGraphicsResources(snapshot, args, context.GfxObjectIndex2SizeMap, out var totalEstimatedGraphicsMemory);

                    // If accounted "untracked" graphics memory is less than estimated, reassign untracked memory
                    // to graphics resources. Otherwise, if we have more regions than estimated, just create "untracked" entity
                    if (totalEstimatedGraphicsMemory.Committed > context.UntrackedGraphicsResources.Committed)
                    {
                        untrackedToReassign += totalEstimatedGraphicsMemory - context.UntrackedGraphicsResources;
                        context.UntrackedGraphicsResources = new MemorySize();
                    }
                    else
                    {
                        context.UntrackedGraphicsResources -= totalEstimatedGraphicsMemory;
                    }
                }

                // Add untracked graphics resources to untracked map
                if (context.UntrackedGraphicsResources.Committed > 0)
                    AddItemSizeToMap(context.UntrackedRegionsName2SizeMap, k_UntrackedGraphicsName, context.UntrackedGraphicsResources);

                // Reduce untracked
                if (untrackedToReassign.Committed > 0)
                    ReduceUntrackedByGraphicsResourcesSize(snapshot, context.UntrackedRegionsName2SizeMap, untrackedToReassign);
            }

            return context;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void AddItemSizeToMap<TKey>(Dictionary<TKey, MemorySize> map, TKey key, MemorySize itemSize)
        {
            if (map.TryGetValue(key, out var itemTotals))
                map[key] = itemTotals + itemSize;
            else
                map.Add(key, itemSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool NameFilter(in BuildArgs args, in string name)
        {
            return args.NameFilter?.Passes(name) ?? true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool SearchFilter(in BuildArgs args, in string scope, in string name = null)
        {
            using var mainGroupFilter = args.SearchFilter?.OpenScope(scope);
            if (name == null)
                return mainGroupFilter?.ScopePasses ?? true;

            return mainGroupFilter?.Passes(name) ?? true;
        }

        void ProcessNativeObject(
            CachedSnapshot snapshot,
            SourceIndex source,
            MemorySize size,
            in BuildArgs args,
            Dictionary<SourceIndex, MemorySize> nativeObjectIndex2TotalMap)
        {
            var name = source.GetName(snapshot);

            // Apply name filter.
            if (!NameFilter(args, name))
                return;

            AddItemSizeToMap(nativeObjectIndex2TotalMap, source, size);
        }

        // Native allocation might be associated with an object or Unity "subsystem"
        // ProcessNativeAllocation should be able to register either in objects or allocations
        void ProcessNativeAllocation(
            CachedSnapshot snapshot,
            SourceIndex source,
            MemorySize size,
            in BuildArgs args,
            BuildContext context)
        {
            var nativeAllocations = snapshot.NativeAllocations;
            var rootReferenceId = nativeAllocations.RootReferenceId[source.Index];

            if (snapshot.MetaData.TargetInfo is { RuntimePlatform: RuntimePlatform.Switch }
                && snapshot.EntriesMemoryMap.GetPointType(source) == EntriesMemoryMapCache.PointType.Device)
            {
                context.UntrackedGraphicsResources += size;
                return;
            }

            string name = k_InvalidItemName;
            SourceIndex groupSource = new SourceIndex();
            if (rootReferenceId >= NativeRootReferenceEntriesCache.FirstValidRootIndex)
            {
                // Is this allocation associated with a native object?
                if (snapshot.NativeObjects.RootReferenceIdToIndex.TryGetValue(rootReferenceId, out var objectIndex))
                {
                    var nativeSource = new SourceIndex(SourceIndex.SourceId.NativeObject, objectIndex);
                    ProcessNativeObject(snapshot, nativeSource, size, args, context.NativeObjectIndex2SizeMap);
                    return;
                }

                // Extract name and type.
                if (snapshot.NativeRootReferences.IdToIndex.TryGetValue(rootReferenceId, out long groupIndex))
                {
                    groupSource = new SourceIndex(SourceIndex.SourceId.NativeRootReference, groupIndex);
                    name = groupSource.GetName(snapshot);
                }
            }
            else
            {
                groupSource = k_FakeInvalidlyRootedAllocationIndex;
            }

            // Apply name filter.
            if (!NameFilter(args, name))
                return;

            if (ShouldGroupBeSplitIntoAllocations(snapshot, context.NativeRootReference2UnsafeAllocations2SizeMap, ref groupSource, name, out var splitGroup))
            {
                if (!splitGroup.TryAdd(source, size) && groupSource.Valid && groupSource.Id == SourceIndex.SourceId.NativeRootReference)
                {
#if DEBUG_VALIDATION
                    // FIXME: Why would there be two entries for the same Native Allocations
                    Debug.Log($"Native Allocation at index {source.Index} address 0x{string.Format("{0:X16}", nativeAllocations.Address[source.Index])} Allocator {snapshot.NativeRootReferences.ObjectName[groupSource.Index]} was already processed");
                    //splitGroup[source] += size;
#endif
                }
            }

            // Attribute VM root to managed VM, rather than to native root references.
            if (groupSource == snapshot.NativeRootReferences.VMRootReferenceIndex)
                context.ManagedMemoryVM += size;
            else
                AddItemSizeToMap(context.NativeRootReference2SizeMap, groupSource, size);
        }

        bool ShouldGroupBeSplitIntoAllocations(
            CachedSnapshot snapshot,
            Dictionary<SourceIndex, Dictionary<SourceIndex, MemorySize>> nativeRootReference2UnsafeAllocations2SizeMap,
            ref SourceIndex groupSource, string name, out Dictionary<SourceIndex, MemorySize> splitGroup)
        {
            if (nativeRootReference2UnsafeAllocations2SizeMap.TryGetValue(groupSource, out var rootReferenceGroup))
            {
                splitGroup = rootReferenceGroup;
                return true;
            }
            else if (MemoryProfilerSettings.FeatureFlags.EnableUnknownUnknownAllocationBreakdown_2024_10 &&
                (!groupSource.Valid && name == k_InvalidItemName && nativeRootReference2UnsafeAllocations2SizeMap.TryGetValue(k_FakeInvalidlyRootedAllocationIndex, out var unknownGroup)))
            {
                groupSource = k_FakeInvalidlyRootedAllocationIndex;
                splitGroup = unknownGroup;
                return true;
            }
            splitGroup = null;
            return false;
        }

        void ProcessNativeRegion(
            CachedSnapshot snapshot,
            SourceIndex source,
            MemorySize size,
            in BuildArgs args,
            Dictionary<SourceIndex, MemorySize> regionsTotalMap,
            Dictionary<SourceIndex, MemorySize> gpuSizesMap)
        {
            var name = source.GetName(snapshot);

            // Apply name filter (only if don't collapse reserved)
            if (args.BreakdownNativeReserved && !NameFilter(args, name))
                return;

            if (snapshot.MetaData.TargetInfo is { RuntimePlatform: RuntimePlatform.Switch } &&
                snapshot.NativeMemoryRegions.ParentIndex[source.Index] == snapshot.NativeMemoryRegions.SwitchGPUAllocatorIndex)
                AddItemSizeToMap(gpuSizesMap, source, size);
            else
                AddItemSizeToMap(regionsTotalMap, source, size);
        }

        void ProcessManagedObject(
            CachedSnapshot snapshot,
            SourceIndex source,
            MemorySize size,
            in BuildArgs args,
            Dictionary<SourceIndex, List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>> typeObjectsMap,
            Dictionary<SourceIndex, Dictionary<string, List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>>> type2Name2ObjectsMap = null)
        {
            var name = source.GetName(snapshot);

            // Apply name filter.
            if (!NameFilter(args, name))
                return;

            ref readonly var managedObject = ref snapshot.CrawledData.ManagedObjects[source.Index];
            var nativeObjectIndex = managedObject.NativeObjectIndex;
            string id = null;
            if (type2Name2ObjectsMap != null && nativeObjectIndex > 0)
            {
                id = NativeObjectTools.ProduceNativeObjectId(nativeObjectIndex, snapshot);
            }

            // to add the object to corresponding type entry in the map, we need a valid type.
            var managedTypeIndex = managedObject.ITypeDescription;
            if (managedTypeIndex < 0)
                return;

            var groupSource = new SourceIndex(SourceIndex.SourceId.ManagedType, managedTypeIndex);

            if (args.SearchFilter != null)
            {
                using var outerGroupNameFilter = args.SearchFilter.OpenScope(k_ManagedGroupName);
                using var innerGroupNameFilter = args.SearchFilter.OpenScope(k_ManagedObjectsGroupName);
                // optimization, don't generate managed type name if the scope already passes
                if (!innerGroupNameFilter.ScopePasses)
                {
                    using var managedTypeNameFilter = args.SearchFilter.OpenScope(groupSource.GetName(snapshot));
                    using var managedNameFilter = args.SearchFilter.OpenScope(name);
                    // if id == null, this just checks if a parent scope passed
                    if (!managedNameFilter.Passes(id))
                        return;
                }
            }

            // Create item for managed object.
            var treeItem = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                m_ItemId++,
                new AllTrackedMemoryModel.ItemData(
                    id ?? name,
                    size,
                    source)
            );

            //  add it to the map
            if (id != null)
            {
                var namedObjectsOfThisType = type2Name2ObjectsMap.GetOrAdd(groupSource);
                namedObjectsOfThisType.GetAndAddToListOrCreateList(name, treeItem);
            }
            else
            {
                typeObjectsMap.GetAndAddToListOrCreateList(groupSource, treeItem);
            }
        }

        void ProcessManagedHeap(
            CachedSnapshot snapshot,
            SourceIndex source,
            MemorySize size,
            in BuildArgs args,
            BuildContext context)
        {
            // Apply name filter.
            var name = source.GetName(snapshot);
            if (!NameFilter(args, name))
                return;

            var managedHeaps = snapshot.ManagedHeapSections;
            var sectionType = managedHeaps.SectionType[source.Index];
            switch (sectionType)
            {
                case MemorySectionType.VirtualMachine:
                    context.ManagedMemoryVM += size;
                    break;
                case MemorySectionType.GarbageCollector:
                    context.ManagedMemoryReserved += size;
                    break;
                default:
                    Debug.Assert(false, $"Unknown managed memory section type ({sectionType}), plese report a bug.");
                    break;
            }
        }

        void ProcessSystemRegion(
            CachedSnapshot snapshot,
            SourceIndex source,
            MemorySize size,
            in BuildArgs args,
            BuildContext context)
        {
            // Apply name filter.
            var name = source.GetName(snapshot);
            if (!NameFilter(args, name))
                return;

            var regionType = snapshot.EntriesMemoryMap.GetPointType(source);
            switch (regionType)
            {
                case EntriesMemoryMapCache.PointType.Mapped:
                {
                    if (SearchFilter(args, k_ExecutablesGroupName, name))
                        AddItemSizeToMap(context.ExecutablesName2SizeMap, name, size);
                    break;
                }
                case EntriesMemoryMapCache.PointType.Shared:
                case EntriesMemoryMapCache.PointType.Untracked:
                {
                    if (SearchFilter(args, UntrackedGroupName, name))
                        AddItemSizeToMap(context.UntrackedRegionsName2SizeMap, name, size);
                    break;
                }
                case EntriesMemoryMapCache.PointType.Device:
                {
                    // Keep graphics in untracked if we don't detail graphics resources
                    if (!args.BreakdownGfxResources)
                    {
                        if (SearchFilter(args, UntrackedGroupName, name))
                            AddItemSizeToMap(context.UntrackedRegionsName2SizeMap, name, size);
                    }
                    else
                        context.UntrackedGraphicsResources += size;
                    break;
                }
                case EntriesMemoryMapCache.PointType.AndroidRuntime:
                {
                    context.AndroidRuntime += size;
                    break;
                }

                default:
                    Debug.Assert(false, $"Unknown memory region type ({regionType}), please report a bug.");
                    break;
            }
        }

        void ProcessGraphicsResources(
            CachedSnapshot snapshot,
            in BuildArgs args,
            Dictionary<SourceIndex, MemorySize> graphicsResourcesMap,
            out MemorySize totalGraphicsMemory)
        {
            totalGraphicsMemory = new MemorySize(0, 0);

            var nativeGfxResourceReferences = snapshot.NativeGfxResourceReferences;
            for (var i = 0; i < nativeGfxResourceReferences.Count; i++)
            {
                var source = new SourceIndex(SourceIndex.SourceId.GfxResource, i);

                var name = source.GetName(snapshot);
                if (!NameFilter(args, name))
                    continue;

                var size = 0UL;
                if (args.BreakdownGfxResources)
                {
                    size = nativeGfxResourceReferences.GfxSize[i];
                    if (size == 0)
                        continue;
                }

                var memorySize = new MemorySize(size, 0);
                AddItemSizeToMap(graphicsResourcesMap, source, memorySize);

                totalGraphicsMemory += memorySize;
            }
        }

        void ReduceUntrackedByGraphicsResourcesSize(CachedSnapshot snapshot, Dictionary<string, MemorySize> untrackedMap, MemorySize graphicsMemorySize)
        {
            var systemRegions = snapshot.SystemMemoryRegions;

            // Sort by size, biggest first
            var untrackedMem = untrackedMap.ToList();
            untrackedMem.Sort((l, r) => -l.Value.Committed.CompareTo(r.Value.Committed));
            for (int i = 0; i < untrackedMem.Count; i++)
            {
                var item = untrackedMem[i];
                if (item.Value.Committed > graphicsMemorySize.Committed)
                {
                    untrackedMap[item.Key] = new MemorySize(item.Value.Committed - graphicsMemorySize.Committed, 0);
                    return;
                }

                graphicsMemorySize -= item.Value;
                untrackedMap.Remove(item.Key);
            }
        }

        MemorySize SumListMemorySize(List<TreeViewItemData<AllTrackedMemoryModel.ItemData>> tree)
        {
            var total = new MemorySize();
            foreach (var item in tree)
                total += item.data.Size;

            return total;
        }

        bool BuildNativeTree(
            CachedSnapshot snapshot,
            in BuildArgs args,
            Dictionary<SourceIndex, MemorySize> nativeObjectIndex2TotalMap,
            Dictionary<SourceIndex, MemorySize> nativeRootReference2TotalMap,
            Dictionary<SourceIndex, Dictionary<SourceIndex, MemorySize>> nativeRootReference2UnsafeAllocations2SizeMap,
            Dictionary<SourceIndex, MemorySize> nativeRegionIndex2TotalMap,
            out TreeViewItemData<AllTrackedMemoryModel.ItemData> tree)
        {
            var treeItems = new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>();

            using var mainGroupNameFilter = args.SearchFilter?.OpenScope(k_NativeGroupName);
            // Build Native Objects tree
            {
                using var subGroupNameFilter = args.SearchFilter?.OpenScope(k_NativeObjectsGroupName);
                SourceIndex NativeObjectIndex2GroupKey(SourceIndex x) => new SourceIndex(SourceIndex.SourceId.NativeType, snapshot.NativeObjects.NativeTypeArrayIndex[x.Index]);

                string NativeObjectIndex2Name(SourceIndex x) => x.GetName(snapshot);

                string GroupKey2Name(SourceIndex x) => x.GetName(snapshot);
                SourceIndex GroupKey2Index(SourceIndex x) => x;

                if (args.DisambiguateUnityObjects)
                {
                    string NativeObjectIndex2InstanceId(SourceIndex x) => NativeObjectTools.ProduceNativeObjectId(x.Index, snapshot);

                    GroupItemsNested(nativeObjectIndex2TotalMap, NativeObjectIndex2InstanceId, NativeObjectIndex2Name, NativeObjectIndex2GroupKey, GroupKey2Index, GroupKey2Name, false, out var nativeObjectsGroupName2TreeMap, args.SearchFilter);
                    if (BuildTreeFromGroupByIdMap(k_NativeObjectsGroupName, m_ItemId++, false, GroupKey2Name, GroupKey2Index, nativeObjectsGroupName2TreeMap, out var unityObjectsRoot))
                        treeItems.Add(unityObjectsRoot);
                }
                else
                {
                    GroupItems(nativeObjectIndex2TotalMap, NativeObjectIndex2Name, NativeObjectIndex2GroupKey, GroupKey2Name, false, out var nativeObjectsGroupName2TreeMap, args.SearchFilter);
                    if (BuildTreeFromGroupByIdMap(k_NativeObjectsGroupName, m_ItemId++, false, GroupKey2Name, GroupKey2Index, nativeObjectsGroupName2TreeMap, out var unityObjectsRoot))
                        treeItems.Add(unityObjectsRoot);
                }
            }

            // Build Unity Subsystems tree
            {
                using var subGroupNameFilter = args.SearchFilter?.OpenScope(k_NativeSubsystemsGroupName);
                string NativeRootReference2Name(SourceIndex x) => x.Id != SourceIndex.SourceId.NativeRootReference ? k_InvalidItemName : snapshot.NativeRootReferences.ObjectName[x.Index];
                string NativeRootReference2GroupKey(SourceIndex x) => x.Id != SourceIndex.SourceId.NativeRootReference ? k_InvalidItemName : snapshot.NativeRootReferences.AreaName[x.Index];
                string GroupKey2Name(string x) => x;
                string Allocation2Name(SourceIndex source) => NativeAllocationTools.ProduceNativeAllocationName(source, snapshot, truncateTypeNames: true);

                GroupItems(nativeRootReference2TotalMap, NativeRootReference2Name, NativeRootReference2GroupKey, GroupKey2Name, false, out var nativeRootReferenceGroupName2TreeMap, args.SearchFilter, nativeRootReference2UnsafeAllocations2SizeMap, Allocation2Name);

                SourceIndex GroupKey2Index(string x) => new SourceIndex();
                if (BuildTreeFromGroupByIdMap(k_NativeSubsystemsGroupName, m_ItemId++, false, GroupKey2Name, GroupKey2Index, nativeRootReferenceGroupName2TreeMap, out var unitySubsystemsRoot))
                    treeItems.Add(unitySubsystemsRoot);
            }

            // Build Native Memory Regions tree, which are "Reserved" memory
            {
                using var subGroupNameFilter = args.SearchFilter?.OpenScope(k_ReservedItemName);
                string NativeRootRegion2Name(SourceIndex x) => x.GetName(snapshot);
                SourceIndex NativeRootRegion2GroupKey(SourceIndex x) => new SourceIndex(SourceIndex.SourceId.NativeMemoryRegion, snapshot.NativeMemoryRegions.ParentIndex[x.Index]);

                string GroupIndex2Name(SourceIndex x) => x.GetName(snapshot);
                GroupItems(nativeRegionIndex2TotalMap, NativeRootRegion2Name, NativeRootRegion2GroupKey, GroupIndex2Name, false, out var nativeRegionName2TreeMap, args.SearchFilter);
                if (args.BreakdownNativeReserved)
                {
                    SourceIndex GroupKey2Index(SourceIndex x) => x;
                    if (BuildTreeFromGroupByIdMap(k_ReservedItemName, (int)IAnalysisViewSelectable.Category.NativeReserved, false, GroupIndex2Name, GroupKey2Index, nativeRegionName2TreeMap, out var unityRegionsRoot))
                        treeItems.Add(unityRegionsRoot);
                }
                else
                {
                    if (BuildSingleFromGroupByIdMap(args, k_ReservedItemName, (int)IAnalysisViewSelectable.Category.NativeReserved, nativeRegionName2TreeMap, out var unityRegionsRoot))
                        treeItems.Add(unityRegionsRoot);
                }
            }

            // Add root item
            if (treeItems.Count > 0)
            {
                var totals = SumListMemorySize(treeItems);

                tree = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                    (int)IAnalysisViewSelectable.Category.Native,
                    new AllTrackedMemoryModel.ItemData(
                        k_NativeGroupName,
                        totals,
                        new SourceIndex()),
                    treeItems);
                return true;
            }

            tree = default;
            return false;
        }

        bool BuildManagedTree(
            CachedSnapshot snapshot,
            in BuildArgs args,
            Dictionary<SourceIndex, List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>> managedObjectsMap,
            Dictionary<SourceIndex, Dictionary<string, List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>>> managedObjectTypes2NativeNames2ObjectsMap,
            MemorySize managedMemoryVM,
            MemorySize managedMemoryReserved,
            out TreeViewItemData<AllTrackedMemoryModel.ItemData> tree)
        {
            var treeItems = new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>();

            // Build managed objects tree
            string GroupKey2Name(SourceIndex x) => x.GetName(snapshot);
            SourceIndex GroupKey2Index(SourceIndex x) => x;
            if (args.DisambiguateUnityObjects)
            {
                if (BuildTreeFromGroupByIdMap(k_ManagedObjectsGroupName, m_ItemId++, false, GroupKey2Name, GroupKey2Index, managedObjectTypes2NativeNames2ObjectsMap, out var managedObjectsRoot, managedObjectsMap))
                    treeItems.Add(managedObjectsRoot);
            }
            else if (BuildTreeFromGroupByIdMap(k_ManagedObjectsGroupName, m_ItemId++, false, GroupKey2Name, GroupKey2Index, managedObjectsMap, out var managedObjectsRoot))
                treeItems.Add(managedObjectsRoot);

            // SearchFilter doesn't have to be applied to the managed objects, as their Tree is already fully build by the context builder and filtered right there
            using var mainGroupNameFilter = args.SearchFilter?.OpenScope(k_ManagedGroupName);
            // Add "VM"
            if (SearchFilter(args, k_ManagedVMGroupName) && NameFilter(args, k_ManagedVMGroupName))
            {
                treeItems.Add(new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                m_ItemId++,
                new AllTrackedMemoryModel.ItemData(
                    k_ManagedVMGroupName,
                    managedMemoryVM,
                    new SourceIndex())));
            }

            // Add "Reserved"
            if (SearchFilter(args, k_ReservedItemName) && NameFilter(args, k_ReservedItemName))
            {
                treeItems.Add(new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                    (int)IAnalysisViewSelectable.Category.ManagedReserved,
                    new AllTrackedMemoryModel.ItemData(
                        k_ReservedItemName,
                        managedMemoryReserved,
                        new SourceIndex())));
            }

            // Add root item
            if (treeItems.Count > 0)
            {
                var totals = SumListMemorySize(treeItems);

                tree = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                    (int)IAnalysisViewSelectable.Category.Managed,
                    new AllTrackedMemoryModel.ItemData(
                        k_ManagedGroupName,
                        totals,
                        new SourceIndex()),
                    treeItems);
                return true;
            }

            tree = default;
            return false;
        }

        bool BuildGraphicsMemoryTree(
            CachedSnapshot snapshot,
            in BuildArgs args,
            Dictionary<SourceIndex, MemorySize> objectIndex2TotalMap,
            Dictionary<SourceIndex, MemorySize> gfxReserveRegionIndex2TotalMap,
            out TreeViewItemData<AllTrackedMemoryModel.ItemData> tree)
        {
            var treeItems = new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>();

            using var mainGroupNameFilter = args.SearchFilter?.OpenScope(GraphicsGroupName);

            var nativeObjects = snapshot.NativeObjects;
            var nativeGfxResourceReferences = snapshot.NativeGfxResourceReferences;

            // Accessors
            SourceIndex ObjectIndex2GroupKey(SourceIndex x)
            {
                if (x.Id == SourceIndex.SourceId.None)
                    return new SourceIndex();

                var rootReferenceId = nativeGfxResourceReferences.RootId[x.Index];
                if (rootReferenceId >= NativeRootReferenceEntriesCache.FirstValidRootIndex)
                {
                    if (nativeObjects.RootReferenceIdToIndex.TryGetValue(rootReferenceId, out var nativeObjectIndex))
                        return new SourceIndex(SourceIndex.SourceId.NativeType, nativeObjects.NativeTypeArrayIndex[nativeObjectIndex]);

                    // If the allocation is not associated with any native object, try use subsystem root to identify it.
                    if (snapshot.NativeRootReferences.IdToIndex.TryGetValue(rootReferenceId, out long groupIndex))
                        return new SourceIndex(SourceIndex.SourceId.NativeRootReference, groupIndex);
                }

                return new SourceIndex();
            }
            string ObjectIndex2Name(SourceIndex x) => x.GetName(snapshot);
            string GroupKey2Name(SourceIndex x) => x.GetName(snapshot);
            SourceIndex GroupKey2Index(SourceIndex x) => x;

            if (gfxReserveRegionIndex2TotalMap.Count > 0)
            {
                // Build Native Memory Regions tree, which are "Reserved" memory.
                //
                // This is currently a Switch-only path, where GPU reserved regions get recorded as native reserve regions.
                string NativeRootRegion2Name(SourceIndex x) => x.GetName(snapshot);
                SourceIndex NativeRootRegion2GroupKey(SourceIndex x) => new SourceIndex(SourceIndex.SourceId.NativeMemoryRegion, snapshot.NativeMemoryRegions.ParentIndex[x.Index]);

                string GroupIndex2Name(SourceIndex x) => x.GetName(snapshot);
                GroupItems(gfxReserveRegionIndex2TotalMap, NativeRootRegion2Name, NativeRootRegion2GroupKey, GroupIndex2Name, false, out var nativeRegionName2TreeMap, args.SearchFilter);

                if (args.BreakdownNativeReserved)
                {
                    SourceIndex GroupKey2Index2(SourceIndex x) => x;
                    if (BuildTreeFromGroupByIdMap(k_ReservedItemName, (int)IAnalysisViewSelectable.Category.GraphicsReserved, false, GroupIndex2Name, GroupKey2Index2, nativeRegionName2TreeMap, out var unityRegionsRoot))
                        treeItems.Add(unityRegionsRoot);
                }
                else
                {
                    if (BuildSingleFromGroupByIdMap(args, k_ReservedItemName, (int)IAnalysisViewSelectable.Category.GraphicsReserved, nativeRegionName2TreeMap, out var unityRegionsRoot))
                        treeItems.Add(unityRegionsRoot);
                }
            }

            // Build graphics resources tree
            // Mark items as "unreliable" if we're building without
            // including detailed resource information (it's done
            // when we want to keep real untracked).
            bool unreliable = !args.BreakdownGfxResources;
            if (args.DisambiguateUnityObjects)
            {
                string GraphicsResourceIndex2InstanceId(SourceIndex x)
                {
                    // Get associated memory label root
                    var rootReferenceId = snapshot.NativeGfxResourceReferences.RootId[x.Index];
                    // if the index is valid (RootId 0 is not valid), look up native object index associated with memory label root
                    if (rootReferenceId >= NativeRootReferenceEntriesCache.FirstValidRootIndex && snapshot.NativeObjects.RootReferenceIdToIndex.TryGetValue(rootReferenceId, out var objectIndex))
                        return NativeObjectTools.ProduceNativeObjectId(objectIndex, snapshot);
                    // Instance ID 0 is invalid
                    return "ID: 0";
                }

                GroupItemsNested(objectIndex2TotalMap, GraphicsResourceIndex2InstanceId, ObjectIndex2Name, ObjectIndex2GroupKey, GroupKey2Index, GroupKey2Name, unreliable, out var nestedObjectsGroupName2TreeMap, args.SearchFilter);
                return BuildTreeFromGroupByIdMap(GraphicsGroupName, (int)IAnalysisViewSelectable.Category.Graphics, unreliable, GroupKey2Name, GroupKey2Index, nestedObjectsGroupName2TreeMap, out tree, null, treeItems);
            }
            else
            {
                GroupItems(objectIndex2TotalMap, ObjectIndex2Name, ObjectIndex2GroupKey, GroupKey2Name, unreliable, out var objectsGroupName2TreeMap, args.SearchFilter);

                var selectionCategory = unreliable ? (int)IAnalysisViewSelectable.Category.GraphicsDisabled : (int)IAnalysisViewSelectable.Category.Graphics;
                return BuildTreeFromGroupByIdMap(GraphicsGroupName, selectionCategory, unreliable, GroupKey2Name, GroupKey2Index, objectsGroupName2TreeMap, out tree, treeItems);
            }
        }

        bool BuildAndroidRuntimeTree(in BuildArgs args,
            MemorySize androidRuntimeSize,
            out TreeViewItemData<AllTrackedMemoryModel.ItemData> tree)
        {
            if (NameFilter(args, k_AndroidRuntime) && SearchFilter(args, k_AndroidRuntime) && (androidRuntimeSize.Committed > 0))
            {
                tree = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                    (int)IAnalysisViewSelectable.Category.AndroidRuntime,
                    new AllTrackedMemoryModel.ItemData(k_AndroidRuntime, androidRuntimeSize, new SourceIndex()));
                return true;
            }

            tree = default;
            return false;
        }

        void GroupItems<T>(
            Dictionary<SourceIndex, MemorySize> itemIndex2SizeMap,
            Func<SourceIndex, string> itemIndex2ItemName,
            Func<SourceIndex, T> itemIndex2GroupKey,
            Func<T, string> groupKey2GroupName,
            bool unreliable,
            out Dictionary<T, List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>> group2itemsMap,
            IScopedFilter<string> searchFilter,
            Dictionary<SourceIndex, Dictionary<SourceIndex, MemorySize>> ítemsToSplitByAllocation = null,
            Func<SourceIndex, string> allocation2Name = null)
        {
            group2itemsMap = new Dictionary<T, List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>>();
            foreach (var item in itemIndex2SizeMap)
            {
                var itemName = itemIndex2ItemName(item.Key);
                var itemGroupKey = itemIndex2GroupKey(item.Key);

                // optimization, don't generate group names if the scope already passes
                if (!(searchFilter?.CurrentScopePasses ?? true))
                {
                    using var groupNameFilter = searchFilter?.OpenScope(groupKey2GroupName(itemGroupKey));
                    if (!(groupNameFilter?.Passes(itemName) ?? true))
                        continue;
                }
                List<TreeViewItemData<AllTrackedMemoryModel.ItemData>> listOfSingleAllocations = null;

                if (ítemsToSplitByAllocation?.TryGetValue(item.Key, out var allocationSizes) ?? false)
                {
                    listOfSingleAllocations = new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>(allocationSizes.Count);
                    int i = 0;
                    foreach (var allocation in allocationSizes)
                    {
                        var name = allocation2Name?.Invoke(allocation.Key) ?? $"Allocation {i++}";
                        listOfSingleAllocations.Add(new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                            m_ItemId++,
                            new AllTrackedMemoryModel.ItemData(name, allocation.Value, allocation.Key) { Unreliable = unreliable }));
                    }
                }

                // Create item for native object.
                var treeItem = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                    m_ItemId++,
                    new AllTrackedMemoryModel.ItemData(itemName, item.Value, item.Key) { Unreliable = unreliable },
                    children: listOfSingleAllocations
                );

                // Add object to corresponding type entry in map.
                group2itemsMap.GetAndAddToListOrCreateList(itemGroupKey, treeItem);
            }
        }

        void GroupItemsNested<T>(
            Dictionary<T, MemorySize> itemIndex2SizeMap,
            Func<T, string> itemIndex2ItemID,
            Func<T, string> itemIndex2ItemName,
            Func<T, T> itemIndex2GroupId,
            Func<T, SourceIndex> itemIndex2Index,
            Func<T, string> groupKey2GroupName,
            bool unreliable,
            out Dictionary<T, Dictionary<string, List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>>> typeGroup2NameGroup2ItemsMap,
            IScopedFilter<string> searchFilter)
        {
            typeGroup2NameGroup2ItemsMap = new Dictionary<T, Dictionary<string, List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>>>();
            foreach (var item in itemIndex2SizeMap)
            {
                var itemID = itemIndex2ItemID(item.Key);
                var itemName = itemIndex2ItemName(item.Key);
                var itemGroupId = itemIndex2GroupId(item.Key);
                var itemIndex = itemIndex2Index(item.Key);

                // optimization, don't generate group names if the scope already passes
                if (!(searchFilter?.CurrentScopePasses ?? true))
                {
                    using var groupNameFilter = searchFilter?.OpenScope(groupKey2GroupName(itemGroupId));
                    using var itemNameFilter = searchFilter?.OpenScope(itemName);
                    if (!(itemNameFilter?.Passes(itemID) ?? true))
                        continue;
                }

                // Create item for native object.
                var treeItem = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                    m_ItemId++,
                    new AllTrackedMemoryModel.ItemData(itemID, item.Value, itemIndex) { Unreliable = unreliable }
                );
                // Add object to corresponding type entry in map.
                var itemNameGroup = typeGroup2NameGroup2ItemsMap.GetOrAdd(itemGroupId);
                itemNameGroup.GetAndAddToListOrCreateList(itemName, treeItem);
            }
        }

        bool BuildTreeFromGroupByIdMap<T>(
            string groupName,
            int groupId,
            bool unreliable,
            Func<T, string> groupKey2Name,
            Func<T, SourceIndex> groupKey2Index,
            Dictionary<T, Dictionary<string, List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>>> typeGroup2NameGroup2ItemsMap,
            out TreeViewItemData<AllTrackedMemoryModel.ItemData> tree,
            Dictionary<T, List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>> typeGroup2itemsMap = null,
            List<TreeViewItemData<AllTrackedMemoryModel.ItemData>> treeItems = null)
        {
            treeItems ??= new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>(typeGroup2NameGroup2ItemsMap.Count);
            string nameGroupKey2Name(string nameGroupName) => nameGroupName;
            SourceIndex nameGroupKey2Index(string nameGroupName) => new SourceIndex();
            foreach (var group in typeGroup2NameGroup2ItemsMap)
            {
                var itemsGroupKey = group.Key;
                var itemsTree = group.Value;

                BuildTreeFromGroupByIdMap(groupKey2Name(itemsGroupKey), m_ItemId++, unreliable, nameGroupKey2Name, nameGroupKey2Index, itemsTree, out var groupRoot);

                // Merge items that are directly in this group with the ones in a subgroup (build up above) if there is a mix of both for this group.
                if (typeGroup2itemsMap != null && typeGroup2itemsMap.TryGetValue(itemsGroupKey, out var groupedItems))
                {
                    var groupItems = new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>(groupRoot.children);
                    groupItems.AddRange(groupedItems);

                    var itemsTreeSize = SumListMemorySize(groupItems);
                    groupRoot = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                        groupRoot.id,
                        new AllTrackedMemoryModel.ItemData(
                            groupRoot.data.Name,
                            itemsTreeSize,
                            groupKey2Index(itemsGroupKey),
                            childCount: groupItems.Count),
                        groupItems);

                    // Remove this group so all remaining groups without mixed grouping can be added later.
                    typeGroup2itemsMap.Remove(itemsGroupKey);
                }
                treeItems.Add(groupRoot);
            }

            if (typeGroup2itemsMap != null && typeGroup2itemsMap.Count > 0)
            {
                BuildTreeFromGroupByIdMap(groupName, m_ItemId++, unreliable, groupKey2Name, groupKey2Index, typeGroup2itemsMap, out var groupRoot);
                treeItems.AddRange(groupRoot.children);
            }

            // Add root item
            if (treeItems.Count > 0)
            {
                var totalSize = SumListMemorySize(treeItems);

                tree = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                    groupId,
                    new AllTrackedMemoryModel.ItemData(
                        groupName,
                        totalSize,
                        new SourceIndex(),
                        childCount: treeItems.Count),
                    treeItems);

                return true;
            }

            tree = default;
            return false;
        }

        bool BuildTreeFromGroupByIdMap<T>(
            string groupName,
            int groupId,
            bool unreliable,
            Func<T, string> groupKey2Name,
            Func<T, SourceIndex> groupKey2Index,
            Dictionary<T, List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>> group2itemsMap,
            out TreeViewItemData<AllTrackedMemoryModel.ItemData> tree,
            List<TreeViewItemData<AllTrackedMemoryModel.ItemData>> treeItems = null)
        {
            treeItems ??= new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>(group2itemsMap.Count);

            foreach (var group in group2itemsMap)
            {
                var itemsTree = group.Value;
                var itemsGroupName = groupKey2Name(group.Key);

                // Calculate type size from all its objects.
                var itemsTreeSize = SumListMemorySize(itemsTree);

                var typeItem = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                    m_ItemId++,
                    new AllTrackedMemoryModel.ItemData(itemsGroupName, itemsTreeSize, groupKey2Index(group.Key), childCount: itemsTree.Count) { Unreliable = unreliable },
                    itemsTree);
                treeItems.Add(typeItem);
            }

            // Add root item
            if (treeItems.Count > 0)
            {
                var totalSize = SumListMemorySize(treeItems);

                tree = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                    groupId,
                    new AllTrackedMemoryModel.ItemData(groupName, totalSize, new SourceIndex(), childCount: treeItems.Count) { Unreliable = unreliable },
                    treeItems);

                return true;
            }

            tree = default;
            return false;
        }

        bool BuildTreeFromGroupByNameMap(
            string groupName,
            int groupId,
            Dictionary<string, MemorySize> itemsMap,
            out TreeViewItemData<AllTrackedMemoryModel.ItemData> tree)
        {
            var treeItems = new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>(itemsMap.Count);
            foreach (var kvp in itemsMap)
            {
                // Extract data
                var name = kvp.Key;
                var size = kvp.Value;

                // Make tree view item
                var item = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                    m_ItemId++,
                    new AllTrackedMemoryModel.ItemData(
                        name,
                        size,
                        new SourceIndex()));
                treeItems.Add(item);
            }

            // Add root item
            if (treeItems.Count > 0)
            {
                var totalSize = SumListMemorySize(treeItems);

                tree = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                    groupId,
                    new AllTrackedMemoryModel.ItemData(
                        groupName,
                        totalSize,
                        new SourceIndex(),
                        childCount: treeItems.Count),
                    treeItems);

                return true;
            }

            tree = default;
            return false;
        }

        bool BuildSingleFromGroupByIdMap(
            in BuildArgs args,
            string groupName,
            int groupId,
            Dictionary<SourceIndex, List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>> group2itemsMap,
            out TreeViewItemData<AllTrackedMemoryModel.ItemData> tree)
        {
            // As it's collapses in single item, it can be filtered out
            if (!SearchFilter(args, groupName) || !NameFilter(args, groupName))
            {
                tree = default;
                return false;
            }

            // Add summary item if the group isn't empty
            if (group2itemsMap.Count > 0)
            {
                var totalSize = new MemorySize();
                foreach (var group in group2itemsMap)
                    totalSize += SumListMemorySize(group.Value);

                tree = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                    groupId,
                    new AllTrackedMemoryModel.ItemData(
                        groupName,
                        totalSize,
                        new SourceIndex()));

                return true;
            }

            tree = default;
            return false;
        }


        // Returns all items in the tree that pass the provided filterPath. Each filter is applied at one level in the tree, starting at the root. Children will be removed.
        List<TreeViewItemData<AllTrackedMemoryModel.ItemData>> BuildItemsAtPathInTreeExclusively(
            IEnumerable<ITextFilter> filterPath,
            IEnumerable<TreeViewItemData<AllTrackedMemoryModel.ItemData>> tree)
        {
            var itemsAtPath = new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>();

            var items = tree;
            var filterPathQueue = new Queue<ITextFilter>(filterPath);
            while (filterPathQueue.Count > 0)
            {
                var found = false;
                TreeViewItemData<AllTrackedMemoryModel.ItemData> itemOnPath = default;
                var filter = filterPathQueue.Dequeue();
                var sb = new System.Text.StringBuilder();
                foreach (var item in items)
                {
                    if (filter.Passes(item.data.Name))
                    {
                        found = true;
                        itemOnPath = item;

                        // If we are at the end of the path, continue iterating to collect all siblings that match path. Otherwise break to proceed to the next level.
                        if (filterPathQueue.Count == 0)
                            itemsAtPath.Add(item);
                        else
                            break;
                    }
                }
                // Search failed.
                if (!found)
                    break;

                // Search successful so far. Proceed to the next level in the tree.
                items = itemOnPath.children;
            }

            // Reconstruct the items without children.
            var exclusiveItems = new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>(itemsAtPath.Count);
            foreach (var item in itemsAtPath)
            {
                var itemWithoutChildren = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                    item.id,
                    item.data);
                exclusiveItems.Add(itemWithoutChildren);
            }

            return exclusiveItems;
        }

        internal readonly struct BuildArgs
        {
            public BuildArgs(
                IScopedFilter<string> searchFilter,
                ITextFilter nameFilter = null,
                IEnumerable<ITextFilter> pathFilter = null,
                bool excludeAll = false,
                bool breakdownNativeReserved = false,
                bool disambiguateUnityObjects = false,
                bool breakdownGfxResources = true,
                Action<int, AllTrackedMemoryModel.ItemData> selectionProcessor = null,
                string[] allocationRootNamesToSplitIntoSuballocations = null)
            {
                NameFilter = nameFilter;
                SearchFilter = searchFilter;
                PathFilter = pathFilter;
                ExcludeAll = excludeAll;
                BreakdownNativeReserved = breakdownNativeReserved;
                SelectionProcessor = selectionProcessor;
                DisambiguateUnityObjects = disambiguateUnityObjects;
                BreakdownGfxResources = breakdownGfxResources;
                AllocationRootNamesToSplitIntoSuballocations = allocationRootNamesToSplitIntoSuballocations;
            }

            /// <summary>
            /// Only leaf items that pass directly or as part of their parent scope will be included.
            /// </summary>
            public IScopedFilter<string> SearchFilter { get; }

            // Only items with a name that passes this filter will be included.
            public ITextFilter NameFilter { get; }

            // Only items with a path (of names) that passes this filter will be included.
            public IEnumerable<ITextFilter> PathFilter { get; }

            // If true, excludes all items. This is currently used by the All Of Memory Comparison functionality to show an empty table when particular comparison items (groups) are selected.
            public bool ExcludeAll { get; }

            // If true, breakdown into separate native allocators will be added for native reserved group
            public bool BreakdownNativeReserved { get; }

            // Selection processor for an item. Argument is the selected item object.
            public Action<int, AllTrackedMemoryModel.ItemData> SelectionProcessor { get; }

            // If true, Unity Objects will be named after their Instance ID and grouped under the item which represents the name
            public bool DisambiguateUnityObjects { get; }

            // If true, breakdown graphics resources into separate groups using estimated data
            // As we don't know exact resources location, resource reassignment will be used
            // what might introduce data inconsistencies and invalidates resident memory information
            public bool BreakdownGfxResources { get; }

            // The allocation rootes listed as "<AreaName>:<ObjectName>" that should get all allocations underneath them listed separately.
            public string[] AllocationRootNamesToSplitIntoSuballocations { get; }
        }
    }
}

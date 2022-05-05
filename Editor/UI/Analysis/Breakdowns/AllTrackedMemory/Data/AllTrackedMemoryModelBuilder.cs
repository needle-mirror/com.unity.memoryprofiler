#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Builds an AllTrackedMemoryModel.
    class AllTrackedMemoryModelBuilder
    {
        int m_ItemId;

        public AllTrackedMemoryModel Build(CachedSnapshot snapshot, in BuildArgs args)
        {
            if (!CanBuildBreakdownForSnapshot(snapshot))
                throw new ArgumentException("Unsupported snapshot version.", nameof(snapshot));

            var rootNodes = BuildAllTrackedMemoryBreakdown(snapshot, args);
            var totalSnapshotMemorySize = snapshot.MetaData.TargetMemoryStats.Value.TotalVirtualMemory;
            var model = new AllTrackedMemoryModel(rootNodes, totalSnapshotMemorySize);
            return model;
        }

        bool CanBuildBreakdownForSnapshot(CachedSnapshot snapshot)
        {
            // TargetAndMemoryInfo is required to obtain the total snapshot memory size and reserved sizes.
            if (!snapshot.HasTargetAndMemoryInfo)
                return false;

            return true;
        }

        List<TreeViewItemData<AllTrackedMemoryModel.ItemData>> BuildAllTrackedMemoryBreakdown(
            CachedSnapshot snapshot,
            in BuildArgs args)
        {
            var rootItems = new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>();

            if (TryBuildNativeMemoryTree(snapshot, args, out var nativeMemoryTree))
                rootItems.Add(nativeMemoryTree);

            if (TryBuildScriptingMemoryTree(snapshot, args, out var scriptingMemoryTree))
                rootItems.Add(scriptingMemoryTree);

            if (TryBuildGraphicsMemoryTree(snapshot, args, out var graphicsMemoryTree))
                rootItems.Add(graphicsMemoryTree);

            if (TryBuildExecutableAndDllsTree(snapshot, args, out var codeTree))
                rootItems.Add(codeTree);

            return rootItems;
        }

        bool TryBuildNativeMemoryTree(
            CachedSnapshot snapshot,
            in BuildArgs args,
            out TreeViewItemData<AllTrackedMemoryModel.ItemData> tree)
        {
            var accountedSize = 0UL;
            List<TreeViewItemData<AllTrackedMemoryModel.ItemData>> nativeItems = null;

            // Native Objects and Allocation Roots.
            {
                var nativeRootReferences = snapshot.NativeRootReferences;
                BuildNativeObjectsAndRootsTreeItems(snapshot,
                    args,
                    out accountedSize,
                    (long rootId) =>
                {
                    return nativeRootReferences.IdToIndex.TryGetValue(rootId, out var rootIndex) ? nativeRootReferences.AccumulatedSize[rootIndex] : 0;
                }, ref nativeItems);
            }

            // Native Temporary Allocators.
            {
                // They represent native heap which is not associated with any Unity object or native root
                // and thus can be represented in isolation.
                // Currently all temp allocators has "TEMP" string in their names.
                if (snapshot.HasGfxResourceReferencesAndAllocators)
                {
                    var nativeAllocatorsTotalSize = 0UL;

                    var nativeAllocators = snapshot.NativeAllocators;
                    var nativeAllocatorItems = new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>();

                    for (int i = 0; i != nativeAllocators.Count; ++i)
                    {
                        string allocatorName = nativeAllocators.AllocatorName[i];
                        if (!allocatorName.Contains("TEMP"))
                            continue;

                        // Filter by Native Allocator name. Skip objects that don't pass the name filter.
                        if (args.NameFilter != null && !args.NameFilter.TextPasses(allocatorName))
                            continue;

                        var size = nativeAllocators.ReservedSize[i];
                        var allocatorItem = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                            m_ItemId++,
                            new AllTrackedMemoryModel.ItemData(
                                allocatorName,
                                size)
                            );
                        nativeAllocatorItems.Add(allocatorItem);

                        nativeAllocatorsTotalSize += size;
                    }

                    accountedSize += nativeAllocatorsTotalSize;

                    if (nativeAllocatorItems.Count > 0)
                    {
                        var nativeAllocatorsItem = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                            m_ItemId++,
                            new AllTrackedMemoryModel.ItemData(
                                "Native Temporary Allocators",
                                nativeAllocatorsTotalSize,
                                childCount: nativeAllocatorItems.Count),
                            nativeAllocatorItems);
                        nativeItems.Add(nativeAllocatorsItem);
                    }
                }
            }

            // Reserved
            {
                // Only add 'Reserved' item if not applying a filter.
                if (args.NameFilter == null)
                {
                    var memoryStats = snapshot.MetaData.TargetMemoryStats.Value;
                    var totalSize = memoryStats.TotalReservedMemory - memoryStats.GraphicsUsedMemory - memoryStats.GcHeapReservedMemory;
                    if (totalSize > accountedSize)
                    {
                        var remainingNativeMemory = totalSize - accountedSize;
                        var item = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                            m_ItemId++,
                            new AllTrackedMemoryModel.ItemData(
                                "Reserved",
                                remainingNativeMemory)
                        );
                        nativeItems.Add(item);

                        accountedSize += remainingNativeMemory;
                    }
                }
            }

            // Total Native Heap.
            if (nativeItems.Count > 0)
            {
                tree = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                    m_ItemId++,
                    new AllTrackedMemoryModel.ItemData(
                        "Native Memory",
                        accountedSize),
                    nativeItems);
                return true;
            }

            tree = default;
            return false;
        }

        void BuildNativeObjectsAndRootsTreeItems(
            CachedSnapshot snapshot,
            in BuildArgs args,
            out ulong totalHeapSize,
            Func<long, ulong> rootIdToSizeFunc,
            ref List<TreeViewItemData<AllTrackedMemoryModel.ItemData>> nativeItems)
        {
            totalHeapSize = 0UL;
            if (nativeItems == null)
                nativeItems = new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>();
            var cachedAccountedNativeObjects = new Dictionary<long, long>();

            // Native Objects grouped by Native Type.
            {
                // Build type-index to type-objects map.
                var typeIndexToTypeObjectsMap = new Dictionary<int, List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>>();
                var nativeObjectsTotalSize = 0UL;
                var nativeObjects = snapshot.NativeObjects;
                var nativeObjectsCountInt32 = Convert.ToInt32(nativeObjects.Count);
                cachedAccountedNativeObjects.EnsureCapacity(nativeObjectsCountInt32);
                for (var i = 0L; i < nativeObjects.Count; i++)
                {
                    // Mark this object as visited for later roots iteration.
                    var rootId = nativeObjects.RootReferenceId[i];
                    cachedAccountedNativeObjects.Add(rootId, i);
                    ulong size = rootIdToSizeFunc(rootId);
                    // Ignore empty objects.
                    if (size == 0)
                        continue;

                    // Filter by Native Object name. Skip objects that don't pass the name filter.
                    var name = nativeObjects.ObjectName[i];
                    if (args.NameFilter != null && !args.NameFilter.TextPasses(name))
                        continue;

                    // Create selection processor.
                    var nativeObjectIndex = i;
                    var nativeObjectSelectionProcessor = args.NativeObjectSelectionProcessor;
                    void ProcessNativeObjectSelection()
                    {
                        nativeObjectSelectionProcessor?.Invoke(nativeObjectIndex);
                    }

                    // Create item for native object.
                    var item = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                        m_ItemId++,
                        new AllTrackedMemoryModel.ItemData(
                            name,
                            size,
                            ProcessNativeObjectSelection)
                    );

                    // Add object to corresponding type entry in map.
                    var typeIndex = nativeObjects.NativeTypeArrayIndex[i];
                    if (typeIndexToTypeObjectsMap.TryGetValue(typeIndex, out var typeObjects))
                        typeObjects.Add(item);
                    else
                        typeIndexToTypeObjectsMap.Add(typeIndex, new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>() { item });
                }

                // Build type-objects tree from map.
                var nativeTypes = snapshot.NativeTypes;
                var nativeTypeItems = new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>(typeIndexToTypeObjectsMap.Count);
                foreach (var kvp in typeIndexToTypeObjectsMap)
                {
                    var typeIndex = kvp.Key;
                    var typeObjects = kvp.Value;

                    // Calculate type size from all its objects.
                    var typeSize = 0UL;
                    foreach (var typeObject in typeObjects)
                        typeSize += typeObject.data.Size;

                    // Ignore empty types.
                    if (typeSize == 0)
                        continue;

                    // Create selection processor.
                    var nativeTypeSelectionProcessor = args.NativeTypeSelectionProcessor;
                    void ProcessNativeTypeSelection()
                    {
                        nativeTypeSelectionProcessor?.Invoke(typeIndex);
                    }

                    var typeName = nativeTypes.TypeName[typeIndex];
                    var typeItem = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                        m_ItemId++,
                        new AllTrackedMemoryModel.ItemData(
                            typeName,
                            typeSize,
                            ProcessNativeTypeSelection,
                            typeObjects.Count),
                        typeObjects);
                    nativeTypeItems.Add(typeItem);

                    // Accumulate type's size into total size of native objects.
                    nativeObjectsTotalSize += typeSize;
                }

                totalHeapSize += nativeObjectsTotalSize;

                if (nativeTypeItems.Count > 0)
                {
                    var nativeObjectsItem = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                        m_ItemId++,
                        new AllTrackedMemoryModel.ItemData(
                            "Unity Objects",
                            nativeObjectsTotalSize,
                            childCount: nativeTypeItems.Count),
                        nativeTypeItems);
                    nativeItems.Add(nativeObjectsItem);
                }
            }

            // Native Roots
            {
                var nativeRootsTotalSize = 0UL;
                var nativeRootReferences = snapshot.NativeRootReferences;
                var nativeRootAreaItems = new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>();

                // Get indices of all roots grouped by area.
                var rootAreas = new Dictionary<string, List<long>>();
                // Start from index 1 as 0 is executable and dll size!
                for (var i = 1; i < nativeRootReferences.Count; ++i)
                {
                    var accounted = cachedAccountedNativeObjects.TryGetValue(nativeRootReferences.Id[i], out _);
                    if (accounted)
                        continue;

                    var rootAreaName = nativeRootReferences.AreaName[i];
                    if (rootAreas.TryGetValue(rootAreaName, out var rootIndices))
                        rootIndices.Add(i);
                    else
                        rootAreas.Add(rootAreaName, new List<long>() { i });
                }

                // Build tree for roots per area.
                foreach (var kvp in rootAreas)
                {
                    var nativeRootAreaTotalSize = 0UL;
                    var rootAreaItems = new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>();
                    var rootIndices = kvp.Value;
                    foreach (var index in rootIndices)
                    {
                        var size = rootIdToSizeFunc(nativeRootReferences.Id[index]);
                        // Ignore empty roots.
                        if (size == 0)
                            continue;

                        // Filter by Native Root Reference object name. Skip objects that don't pass the name filter.
                        var objectName = nativeRootReferences.ObjectName[index];
                        if (args.NameFilter != null && !args.NameFilter.TextPasses(objectName))
                            continue;

                        var nativeRootItem = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                            m_ItemId++,
                            new AllTrackedMemoryModel.ItemData(
                                objectName,
                                size)
                            );
                        rootAreaItems.Add(nativeRootItem);

                        // Accumulate the root's size in its area's total.
                        nativeRootAreaTotalSize += size;
                    }

                    // Ignore empty areas.
                    if (nativeRootAreaTotalSize == 0)
                        continue;

                    var nativeRootAreaName = kvp.Key;
                    var nativeRootAreaItem = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                        m_ItemId++,
                        new AllTrackedMemoryModel.ItemData(
                            nativeRootAreaName,
                            nativeRootAreaTotalSize,
                            childCount: rootAreaItems.Count),
                        rootAreaItems);
                    nativeRootAreaItems.Add(nativeRootAreaItem);

                    nativeRootsTotalSize += nativeRootAreaTotalSize;
                }

                if (nativeRootAreaItems.Count > 0)
                {
                    var nativeRootsItem = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                        m_ItemId++,
                        new AllTrackedMemoryModel.ItemData(
                            "Unity Subsystems",
                            nativeRootsTotalSize,
                            childCount: nativeRootAreaItems.Count),
                        nativeRootAreaItems);
                    nativeItems.Add(nativeRootsItem);
                }

                totalHeapSize += nativeRootsTotalSize;
            }
        }

        bool TryBuildScriptingMemoryTree(
            CachedSnapshot snapshot,
            in BuildArgs args,
            out TreeViewItemData<AllTrackedMemoryModel.ItemData> tree)
        {
            var accountedSize = 0UL;
            var scriptingItems = new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>();

            // Empty Heap Space
            {
                const string k_EmptyHeapSpaceName = "Empty Heap Space";
                if (args.NameFilter != null && args.NameFilter.TextPasses(k_EmptyHeapSpaceName))
                {
                    var emptyHeapSpace = snapshot.ManagedHeapSections.ManagedHeapMemoryReserved - snapshot.CrawledData.ManagedObjectMemoryUsage;
                    var item = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                        m_ItemId++,
                        new AllTrackedMemoryModel.ItemData(
                            k_EmptyHeapSpaceName,
                            emptyHeapSpace)
                        );
                    scriptingItems.Add(item);

                    accountedSize += emptyHeapSpace;
                }
            }

            // Virtual Machine
            {
                var virtualMachineMemoryName = UIContentData.TextContent.DefaultVirtualMachineMemoryCategoryLabel;
                if (snapshot.MetaData.TargetInfo.HasValue)
                {
                    switch (snapshot.MetaData.TargetInfo.Value.ScriptingBackend)
                    {
                        case UnityEditor.ScriptingImplementation.Mono2x:
                            virtualMachineMemoryName = UIContentData.TextContent.MonoVirtualMachineMemoryCategoryLabel;
                            break;

                        case UnityEditor.ScriptingImplementation.IL2CPP:
                            virtualMachineMemoryName = UIContentData.TextContent.IL2CPPVirtualMachineMemoryCategoryLabel;
                            break;

                        case UnityEditor.ScriptingImplementation.WinRTDotNET:
                        default:
                            break;
                    }
                }

                if (args.NameFilter != null && args.NameFilter.TextPasses(virtualMachineMemoryName))
                {
                    var virtualMachineMemoryReserved = snapshot.ManagedHeapSections.VirtualMachineMemoryReserved;
                    var item = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                        m_ItemId++,
                        new AllTrackedMemoryModel.ItemData(
                            virtualMachineMemoryName,
                            virtualMachineMemoryReserved)
                        );
                    scriptingItems.Add(item);

                    accountedSize += virtualMachineMemoryReserved;
                }
            }

            // Objects (Grouped By Type).
            {
                // Build type-index to type-objects map.
                var managedObjectsTotalSize = 0UL;
                var managedObjects = snapshot.CrawledData.ManagedObjects;
                var typeIndexToTypeObjectsMap = new Dictionary<int, List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>>();
                for (var i = 0L; i < managedObjects.Count; i++)
                {
                    var size = Convert.ToUInt64(managedObjects[i].Size);

                    // Use native object name if possible.
                    var name = string.Empty;
                    var nativeObjectIndex = managedObjects[i].NativeObjectIndex;
                    if (nativeObjectIndex > 0)
                        name = snapshot.NativeObjects.ObjectName[nativeObjectIndex];

                    // Filter by Native Object name. Skip objects that don't pass the name filter.
                    if (args.NameFilter != null && !args.NameFilter.TextPasses(name))
                        continue;

                    // Create selection processor.
                    var managedObjectIndex = i;
                    var managedObjectSelectionProcessor = args.ManagedObjectSelectionProcessor;
                    void ProcessManagedObjectSelection()
                    {
                        managedObjectSelectionProcessor?.Invoke(managedObjectIndex);
                    }

                    // Create item for managed object.
                    var item = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                        m_ItemId++,
                        new AllTrackedMemoryModel.ItemData(
                            name,
                            size,
                            ProcessManagedObjectSelection)
                    );

                    // Add object to corresponding type entry in map.
                    var typeIndex = managedObjects[i].ITypeDescription;
                    if (typeIndex < 0)
                        continue;

                    if (typeIndexToTypeObjectsMap.TryGetValue(typeIndex, out var typeObjects))
                        typeObjects.Add(item);
                    else
                        typeIndexToTypeObjectsMap.Add(typeIndex, new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>() { item });
                }

                // Build type-objects tree from map.
                var managedTypes = snapshot.TypeDescriptions;
                var managedTypeItems = new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>(typeIndexToTypeObjectsMap.Count);
                foreach (var kvp in typeIndexToTypeObjectsMap)
                {
                    var typeIndex = kvp.Key;
                    var typeObjects = kvp.Value;

                    // Calculate type size from all its objects.
                    var typeSize = 0UL;
                    foreach (var typeObject in typeObjects)
                        typeSize += typeObject.data.Size;

                    // Create selection processor.
                    var managedTypeSelectionProcessor = args.ManagedTypeSelectionProcessor;
                    void ProcessManagedTypeSelection()
                    {
                        managedTypeSelectionProcessor?.Invoke(typeIndex);
                    }

                    var typeName = managedTypes.TypeDescriptionName[typeIndex];
                    var typeItem = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                        m_ItemId++,
                        new AllTrackedMemoryModel.ItemData(
                            typeName,
                            typeSize,
                            ProcessManagedTypeSelection,
                            typeObjects.Count),
                        typeObjects);
                    managedTypeItems.Add(typeItem);

                    // Accumulate type's size into total size of managed objects.
                    managedObjectsTotalSize += typeSize;
                }

                if (managedTypeItems.Count > 0)
                {
                    var item = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                        m_ItemId++,
                        new AllTrackedMemoryModel.ItemData(
                            "Managed Objects",
                            managedObjectsTotalSize,
                            childCount: managedTypeItems.Count),
                        managedTypeItems);
                    scriptingItems.Add(item);
                }

                accountedSize += managedObjectsTotalSize;
            }

            // Reserved (Unused)
            {
                // Only add 'Reserved' item if not applying a filter.
                if (args.NameFilter == null)
                {
                    var memoryStats = snapshot.MetaData.TargetMemoryStats.Value;
                    var reservedUnusedSize = memoryStats.GcHeapReservedMemory - memoryStats.GcHeapUsedMemory;
                    var item = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                        m_ItemId++,
                        new AllTrackedMemoryModel.ItemData(
                            "Reserved (Unused)",
                            reservedUnusedSize)
                        );
                    scriptingItems.Add(item);

                    accountedSize += reservedUnusedSize;
                }
            }

            if (scriptingItems.Count > 0)
            {
                tree = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                    m_ItemId++,
                    new AllTrackedMemoryModel.ItemData(
                        "Scripting Memory",
                        accountedSize),
                    scriptingItems);
                return true;
            }

            tree = default;
            return false;
        }

        bool TryBuildGraphicsMemoryTree(
            CachedSnapshot snapshot,
            in BuildArgs args,
            out TreeViewItemData<AllTrackedMemoryModel.ItemData> tree)
        {
            const string k_GraphicsMemoryName = "Graphics Memory";
            var graphicsItems = new List<TreeViewItemData<AllTrackedMemoryModel.ItemData>>();

            if (snapshot.HasGfxResourceReferencesAndAllocators)
            {
                var accountedSize = 0UL;

                var nativeGfxResourceReferences = snapshot.NativeGfxResourceReferences;
                BuildNativeObjectsAndRootsTreeItems(snapshot,
                    args,
                    out accountedSize,
                    (long rootId) =>
                {
                    return nativeGfxResourceReferences.RootIdToGfxSize.TryGetValue(rootId, out var size) ? size : 0;
                }, ref graphicsItems);

                // Only add 'Reserved' item if not applying a filter.
                if (args.NameFilter == null)
                {
                    var totalSize = snapshot.MetaData.TargetMemoryStats.Value.GraphicsUsedMemory;
                    if (totalSize > accountedSize)
                    {
                        var remainingGraphicsMemory = totalSize - accountedSize;
                        var item = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                            m_ItemId++,
                            new AllTrackedMemoryModel.ItemData(
                                "Reserved",
                                remainingGraphicsMemory)
                            );
                        graphicsItems.Add(item);

                        accountedSize += remainingGraphicsMemory;
                    }
                }

                if (graphicsItems.Count > 0)
                {
                    tree = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                        m_ItemId++,
                        new AllTrackedMemoryModel.ItemData(
                            "Graphics Memory",
                            accountedSize),
                        graphicsItems);
                    return true;
                }
            }
            else
            {
                // We don't have graphics allocator data, so display the graphics used memory on the root item.
                if (args.NameFilter != null && args.NameFilter.TextPasses(k_GraphicsMemoryName))
                {
                    var graphicsUsedMemory = snapshot.MetaData.TargetMemoryStats.Value.GraphicsUsedMemory;
                    tree = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                        m_ItemId++,
                        new AllTrackedMemoryModel.ItemData(
                            "Graphics Memory",
                            graphicsUsedMemory),
                        graphicsItems);
                    return true;
                }
            }

            tree = default;
            return false;
        }

        bool TryBuildExecutableAndDllsTree(
            CachedSnapshot snapshot,
            in BuildArgs args,
            out TreeViewItemData<AllTrackedMemoryModel.ItemData> tree)
        {
            const string k_ExecutableAndDllsName = "Executable And Dlls";
            if (args.NameFilter != null && !args.NameFilter.TextPasses(k_ExecutableAndDllsName))
            {
                tree = default;
                return false;
            }

            var executableAndDllsReportedValue = snapshot.NativeRootReferences.ExecutableAndDllsReportedValue;
            tree = new TreeViewItemData<AllTrackedMemoryModel.ItemData>(
                m_ItemId++,
                new AllTrackedMemoryModel.ItemData(
                    k_ExecutableAndDllsName,
                    executableAndDllsReportedValue)
                );
            return true;
        }

        internal readonly struct BuildArgs
        {
            public BuildArgs(
                ITextFilter nameFilter,
                IEnumerable<ITextFilter> pathFilter = null,
                Action<long> nativeObjectSelectionProcessor = null,
                Action<int> nativeTypeSelectionProcessor = null,
                Action<long> managedObjectSelectionProcessor = null,
                Action<int> managedTypeSelectionProcessor = null)
            {
                NameFilter = nameFilter;
                PathFilter = pathFilter;
                NativeObjectSelectionProcessor = nativeObjectSelectionProcessor;
                NativeTypeSelectionProcessor = nativeTypeSelectionProcessor;
                ManagedObjectSelectionProcessor = managedObjectSelectionProcessor;
                ManagedTypeSelectionProcessor = managedTypeSelectionProcessor;
            }

            public ITextFilter NameFilter { get; } // TODO

            public IEnumerable<ITextFilter> PathFilter { get; } // TODO

            // Selection processor for a Native Object item. Argument is the index of the native object.
            public Action<long> NativeObjectSelectionProcessor { get; }

            // Selection processor for a Native Type item. Argument is the index of the native type.
            public Action<int> NativeTypeSelectionProcessor { get; }

            // Selection processor for a Managed Object item. Argument is the index of the managed object.
            public Action<long> ManagedObjectSelectionProcessor { get; }

            // Selection processor for a Managed Type item. Argument is the index of the managed type.
            public Action<int> ManagedTypeSelectionProcessor { get; }
        }
    }
}
#endif

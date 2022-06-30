#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Builds a UnityObjectsModel.
    class UnityObjectsModelBuilder
    {
        protected int m_ItemId;

        public UnityObjectsModel Build(CachedSnapshot snapshot, in BuildArgs args)
        {
            if (!CanBuildBreakdownForSnapshot(snapshot))
                throw new UnsupportedSnapshotVersionException(snapshot);

            var rootNodes = BuildUnityObjectsGroupedByType(snapshot, args, out var itemTypeNamesMap);
            if (args.FlattenHierarchy)
                rootNodes = TreeModelUtility.RetrieveLeafNodesOfTree(rootNodes);

            var totalSnapshotMemorySize = snapshot.MetaData.TargetMemoryStats.Value.TotalVirtualMemory;
            var model = new UnityObjectsModel(rootNodes, itemTypeNamesMap, totalSnapshotMemorySize);
            return model;
        }

        protected static bool CanBuildBreakdownForSnapshot(CachedSnapshot snapshot)
        {
            // TargetAndMemoryInfo is required to obtain the total snapshot memory size.
            if (!snapshot.HasTargetAndMemoryInfo)
                return false;

            return true;
        }

        // Build a map of Unity-Object-Type-Name to Unity-Objects. Used by comparison views because type name is consistent across snapshots, whereas type index may not be.
        protected Dictionary<string, List<TreeViewItemData<UnityObjectsModel.ItemData>>> BuildUnityObjectTypeNameToUnityObjectsMapForSnapshot(
            CachedSnapshot snapshot,
            in BuildArgs args)
        {
            var typeIndexToTypeObjectsMap = BuildUnityObjectTypeIndexToUnityObjectsMapForSnapshot(
                snapshot,
                args);

            var typeNameToTypeObjectsMap = new Dictionary<string, List<TreeViewItemData<UnityObjectsModel.ItemData>>>(typeIndexToTypeObjectsMap.Count);
            foreach (var kvp in typeIndexToTypeObjectsMap)
            {
                var typeIndex = kvp.Key;
                var typeName = snapshot.NativeTypes.TypeName[typeIndex];
                typeNameToTypeObjectsMap.Add(typeName, kvp.Value);
            }

            return typeNameToTypeObjectsMap;
        }

        List<TreeViewItemData<UnityObjectsModel.ItemData>> BuildUnityObjectsGroupedByType(
            CachedSnapshot snapshot,
            in BuildArgs args,
            out Dictionary<int, string> outItemTypeNamesMap)
        {
            var typeIndexToTypeObjectsMap = BuildUnityObjectTypeIndexToUnityObjectsMapForSnapshot(
                snapshot,
                args);

            // Filter by potential duplicates, if necessary.
            if (args.PotentialDuplicatesFilter)
                typeIndexToTypeObjectsMap = FilterTypeIndexToTypeObjectsMapForPotentialDuplicatesAndGroup(
                    typeIndexToTypeObjectsMap,
                    args.UnityObjectTypeSelectionProcessor);

            // Build a tree of Unity Objects, grouped by Unity Object Type, from the map.
            var unityObjectsTree = new List<TreeViewItemData<UnityObjectsModel.ItemData>>(typeIndexToTypeObjectsMap.Count);
            outItemTypeNamesMap = new Dictionary<int, string>(typeIndexToTypeObjectsMap.Count);
            var nativeTypes = snapshot.NativeTypes;
            foreach (var kvp in typeIndexToTypeObjectsMap)
            {
                var typeIndex = kvp.Key;
                var typeObjects = kvp.Value;

                // Calculate the total size of the Unity Object Type by summing all of its Unity Objects.
                var typeNativeSize = 0UL;
                var typeManagedSize = 0UL;
                var typeGpuSize = 0UL;
                foreach (var typeObject in typeObjects)
                {
                    typeNativeSize += typeObject.data.NativeSize;
                    typeManagedSize += typeObject.data.ManagedSize;
                    typeGpuSize += typeObject.data.GpuSize;
                }

                // Store type names in a map, keyed off type index. Each item can use this map to look-up its type name without storing it per-item.
                var typeName = nativeTypes.TypeName[typeIndex];
                outItemTypeNamesMap.Add(typeIndex, typeName);

                // Create selection processor.
                var unityObjectTypeSelectionProcessor = args.UnityObjectTypeSelectionProcessor;
                void ProcessUnityObjectTypeSelection()
                {
                    unityObjectTypeSelectionProcessor?.Invoke(typeIndex);
                }

                // Create node for Unity Object Type.
                var node = new TreeViewItemData<UnityObjectsModel.ItemData>(
                    m_ItemId++,
                    new UnityObjectsModel.ItemData(
                        typeName,
                        typeNativeSize,
                        typeManagedSize,
                        typeGpuSize,
                        typeIndex,
                        ProcessUnityObjectTypeSelection,
                        typeObjects.Count),
                    typeObjects);
                unityObjectsTree.Add(node);
            }

            return unityObjectsTree;
        }

        Dictionary<int, List<TreeViewItemData<UnityObjectsModel.ItemData>>> BuildUnityObjectTypeIndexToUnityObjectsMapForSnapshot(
            CachedSnapshot snapshot,
            in BuildArgs args)
        {
            var typeIndexToTypeObjectsMap = new Dictionary<int, List<TreeViewItemData<UnityObjectsModel.ItemData>>>();
            var nativeTypes = snapshot.NativeTypes;
            var nativeObjects = snapshot.NativeObjects;
            for (var i = 0L; i < nativeObjects.Count; i++)
            {
                // Filter by Unity-Object-Instance-Id. If provided, skip objects that don't match the instance ID.
                var nativeObjectInstanceId = nativeObjects.InstanceId[i];
                if (args.UnityObjectInstanceIdFilter.HasValue &&
                    args.UnityObjectInstanceIdFilter.Value != nativeObjectInstanceId)
                    continue;

                // Filter by Unity-Object-Type-Name. Skip objects that don't pass the type name filter.
                var typeIndex = nativeObjects.NativeTypeArrayIndex[i];
                if (args.UnityObjectTypeNameFilter != null)
                {
                    var typeName = nativeTypes.TypeName[typeIndex];
                    if (!args.UnityObjectTypeNameFilter.TextPasses(typeName))
                        continue;
                }

                // Filter by Unity-Object-Name. Skip objects that don't pass the name filter.
                if (args.UnityObjectNameFilter != null)
                {
                    var nativeObjectName = nativeObjects.ObjectName[i];
                    if (!args.UnityObjectNameFilter.TextPasses(nativeObjectName))
                        continue;
                }

                // Get native object size.
                var nativeObjectSize = nativeObjects.Size[i];

                // Get managed object size, if necessary.
                var managedObjectSize = 0UL;
                var managedObjectIndex = nativeObjects.ManagedObjectIndex[i];
                if (managedObjectIndex >= 0)
                {
                    // This native object is linked to a managed object. Count the managed object's size.
                    var managedObject = snapshot.CrawledData.ManagedObjects[managedObjectIndex];
                    managedObjectSize = Convert.ToUInt64(managedObject.Size);
                }

                // Get GPU object size, if necessary.
                var gpuObjectSize = 0UL;
                {
                    // TODO
                }

                // Store the type index so an item can look-up its type name without storing it per-item.
                var typeNameLookupKey = typeIndex;

                // Create selection processor.
                var unityObjectSelectionProcessor = args.UnityObjectSelectionProcessor;
                void ProcessUnityObjectSelection()
                {
                    unityObjectSelectionProcessor?.Invoke(nativeObjectInstanceId);
                }

                // Create node for conceptual Unity Object.
                var name = nativeObjects.ObjectName[i];
                var item = new TreeViewItemData<UnityObjectsModel.ItemData>(
                    m_ItemId++,
                    new UnityObjectsModel.ItemData(
                        name,
                        nativeObjectSize,
                        managedObjectSize,
                        gpuObjectSize,
                        typeNameLookupKey,
                        ProcessUnityObjectSelection)
                );

                // Add node to corresponding type's list of Unity Objects.
                if (typeIndexToTypeObjectsMap.TryGetValue(typeIndex, out var typeObjects))
                    typeObjects.Add(item);
                else
                    typeIndexToTypeObjectsMap.Add(typeIndex, new List<TreeViewItemData<UnityObjectsModel.ItemData>>() { item });
            }

            return typeIndexToTypeObjectsMap;
        }

        // Filter the map for potential duplicates. These are objects with the same type, name, and size. Group duplicates under a single item.
        Dictionary<int, List<TreeViewItemData<UnityObjectsModel.ItemData>>> FilterTypeIndexToTypeObjectsMapForPotentialDuplicatesAndGroup(
            Dictionary<int, List<TreeViewItemData<UnityObjectsModel.ItemData>>> typeIndexToTypeObjectsMap,
            Action<int> duplicateUnityObjectSelectionProcessor)
        {
            var filteredTypeIndexToTypeObjectsMap = new Dictionary<int, List<TreeViewItemData<UnityObjectsModel.ItemData>>>();

            foreach (var typeIndexToTypeObjectsKvp in typeIndexToTypeObjectsMap)
            {
                // Break type objects into separate lists based on name & size.
                var typeObjects = typeIndexToTypeObjectsKvp.Value;
                var potentialDuplicateObjectsMap = new Dictionary<Tuple<string, ulong>, List<TreeViewItemData<UnityObjectsModel.ItemData>>>();
                foreach (var typeObject in typeObjects)
                {
                    var data = typeObject.data;
                    var nameSizeTuple = new Tuple<string, ulong>(data.Name, data.TotalSize);
                    if (potentialDuplicateObjectsMap.TryGetValue(nameSizeTuple, out var nameSizeTypeObjects))
                        nameSizeTypeObjects.Add(typeObject);
                    else
                        potentialDuplicateObjectsMap.Add(nameSizeTuple, new List<TreeViewItemData<UnityObjectsModel.ItemData>>() { typeObject });
                }

                // Create potential duplicate groups for lists that contain more than one item (duplicates).
                var potentialDuplicateItems = new List<TreeViewItemData<UnityObjectsModel.ItemData>>();
                var typeIndex = typeIndexToTypeObjectsKvp.Key;
                foreach (var potentialDuplicateObjectsKvp in potentialDuplicateObjectsMap)
                {
                    var potentialDuplicateObjects = potentialDuplicateObjectsKvp.Value;
                    if (potentialDuplicateObjects.Count > 1)
                    {
                        var potentialDuplicateData = potentialDuplicateObjects[0].data;

                        var duplicateCount = 0;
                        var potentialDuplicatesNativeSize = 0UL;
                        var potentialDuplicatesManagedSize = 0UL;
                        var potentialDuplicatesGpuSize = 0UL;
                        while (duplicateCount < potentialDuplicateObjects.Count)
                        {
                            potentialDuplicatesNativeSize += potentialDuplicateData.NativeSize;
                            potentialDuplicatesManagedSize += potentialDuplicateData.ManagedSize;
                            potentialDuplicatesGpuSize += potentialDuplicateData.GpuSize;

                            duplicateCount++;
                        }

                        // Create selection processor. Use Unity Object Type selection processor for now until we have an extensible Details view (that could show for example 'potential duplicate' view).
                        void ProcessDuplicateUnityObjectSelection()
                        {
                            duplicateUnityObjectSelectionProcessor?.Invoke(typeIndex);
                        }

                        var potentialDuplicateItem = new TreeViewItemData<UnityObjectsModel.ItemData>(
                            m_ItemId++,
                            new UnityObjectsModel.ItemData(
                                potentialDuplicateData.Name,
                                potentialDuplicatesNativeSize,
                                potentialDuplicatesManagedSize,
                                potentialDuplicatesGpuSize,
                                potentialDuplicateData.TypeNameLookupKey,
                                ProcessDuplicateUnityObjectSelection,
                                potentialDuplicateObjects.Count),
                            potentialDuplicateObjects);
                        potentialDuplicateItems.Add(potentialDuplicateItem);
                    }
                }

                // Add list containing duplicate type objects to corresponding type index in filtered map.
                if (potentialDuplicateItems.Count > 0)
                    filteredTypeIndexToTypeObjectsMap.Add(typeIndex, potentialDuplicateItems);
            }

            return filteredTypeIndexToTypeObjectsMap;
        }

        public readonly struct BuildArgs
        {
            public BuildArgs(
                ITextFilter unityObjectNameFilter = null,
                ITextFilter unityObjectTypeNameFilter = null,
                int? unityObjectInstanceIdFilter = null,
                bool flattenHierarchy = false,
                bool potentialDuplicatesFilter = false,
                Action<int> unityObjectSelectionProcessor = null,
                Action<int> unityObjectTypeSelectionProcessor = null)
            {
                UnityObjectNameFilter = unityObjectNameFilter;
                UnityObjectTypeNameFilter = unityObjectTypeNameFilter;
                UnityObjectInstanceIdFilter = unityObjectInstanceIdFilter;
                FlattenHierarchy = flattenHierarchy;
                PotentialDuplicatesFilter = potentialDuplicatesFilter;
                UnityObjectSelectionProcessor = unityObjectSelectionProcessor;
                UnityObjectTypeSelectionProcessor = unityObjectTypeSelectionProcessor;
            }

            // Only include Unity Objects with this name.
            public ITextFilter UnityObjectNameFilter { get; }

            // Only include Unity Objects with this type name.
            public ITextFilter UnityObjectTypeNameFilter { get; }

            // Only include the Unity Object with this instance ID. Null means do not filter by instance id. CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone (0) can be used to filter everything (used for comparison).
            public int? UnityObjectInstanceIdFilter { get; }

            // Flatten the hierarchy to a single level, removing all groups; transforms the tree into a list of its leaf nodes.
            public bool FlattenHierarchy { get; }

            // Only include Unity Objects that have multiple instances of the same type, the same name, and the same size. Groups these by their 'potential duplicate' name.
            public bool PotentialDuplicatesFilter { get; }

            // Selection processor for a Unity Object item. Argument is the native instance id of the native object.
            public Action<int> UnityObjectSelectionProcessor { get; }

            // Selection processor for a Unity Object Type item. Argument is the native type index of the object's native type.
            public Action<int> UnityObjectTypeSelectionProcessor { get; }
        }
    }
}
#endif

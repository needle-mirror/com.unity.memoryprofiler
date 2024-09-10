#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MemoryProfiler.Editor.Extensions;
using UnityEngine.UIElements;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Builds a UnityObjectsComparisonModel.
    class UnityObjectsComparisonModelBuilder : UnityObjectsModelBuilder
    {
        public UnityObjectsComparisonModel Build(
            CachedSnapshot snapshotA,
            CachedSnapshot snapshotB,
            BuildArgs args)
        {
            if (!CanBuildBreakdownForSnapshot(snapshotA))
                throw new UnsupportedSnapshotVersionException(snapshotA);

            if (!CanBuildBreakdownForSnapshot(snapshotB))
                throw new UnsupportedSnapshotVersionException(snapshotB);
            var nativeUnityObjectBaseTypesToDisambiguateByManagedTypeSnapshotA = BuildListOfNativeUnityObjectBaseTypesToDisambiguateByManagedType(snapshotA);
            var nativeUnityObjectBaseTypesToDisambiguateByManagedTypeSnapshotB = BuildListOfNativeUnityObjectBaseTypesToDisambiguateByManagedType(snapshotB);

            var typeNameToObjectNameAndObjectsMapA = BuildUnityObjectTypeNameToUnityObjectNameToObjectIdAndObjectsMapForSnapshot(
                snapshotA,
                nativeUnityObjectBaseTypesToDisambiguateByManagedTypeSnapshotA,
                args,
                out var totalMemoryInSnapshotA);
            var typeNameToObjectNameAndObjectsMapB = BuildUnityObjectTypeNameToUnityObjectNameToObjectIdAndObjectsMapForSnapshot(
                snapshotB,
                nativeUnityObjectBaseTypesToDisambiguateByManagedTypeSnapshotB,
                args,
                out var totalMemoryInSnapshotB);
            var rootNodes = BuildUnityObjectComparisonTree(
                typeNameToObjectNameAndObjectsMapA,
                typeNameToObjectNameAndObjectsMapB,
                args);

            if (args.FlattenHierarchy)
                rootNodes = TreeModelUtility.RetrieveLeafNodesOfTree(rootNodes);

            var model = new UnityObjectsComparisonModel(
                rootNodes,
                totalMemoryInSnapshotA,
                totalMemoryInSnapshotB);
            return model;
        }

        // Build a map of Unity-Object-Native-Type-Name to
        // (potentially) map of Unity-Object-Managed-Type-Name,
        // to map of Unity-Object-Name
        // to map of Instance Ids (if same session diff)
        // to Unity-Objects.
        Dictionary<string, MapOfManagedTypeOrObjectName2Objects>
            BuildUnityObjectTypeNameToUnityObjectNameToObjectIdAndObjectsMapForSnapshot(
            CachedSnapshot snapshot,
            HashSet<SourceIndex> nativeUnityObjectBaseTypesToDisambiguateByManagedTypeSnapshot,
            in BuildArgs args,
            out MemorySize totalMemoryInSnapshot)
        {
             BuildUnityObjectTypeIndexToUnityObjectsMapForSnapshot(
                snapshot,
                new UnityObjectsModelBuilder.BuildArgs(
                    args.SearchStringFilter,
                    args.UnityObjectNameFilter,
                    unityObjectInstanceIDFilter: args.UnityObjectInstanceIDFilter,
                    flattenHierarchy: args.FlattenHierarchy,
                    disambiguateByInstanceId: args.DisambiguateByInstanceId),
                nativeUnityObjectBaseTypesToDisambiguateByManagedTypeSnapshot,
                out var typeIndexToTypeObjectsMap,
                out var disambiguatedTypeIndexToTypeObjectsMap,
                out var nonDisambiguatedTechnicallyManagedTypeItems,
                out totalMemoryInSnapshot);

            var nativeTypeNameToManagedTypeToObjectNameMapOrAndObjectsMap = new Dictionary<string, MapOfManagedTypeOrObjectName2Objects>();

            foreach (var nativeTypeGroup in IterateAndBuildTypeGroups(typeIndexToTypeObjectsMap, snapshot))
            {
                nativeTypeNameToManagedTypeToObjectNameMapOrAndObjectsMap.Add(nativeTypeGroup.Item1,
                    new MapOfManagedTypeOrObjectName2Objects { MapOfNativeNames = nativeTypeGroup.Item2 });
            }
            foreach (var nativeTypeGroup in disambiguatedTypeIndexToTypeObjectsMap)
            {
                var nativeTypeName = nativeTypeGroup.Key.GetName(snapshot);
                var managedTypeToObjectMap = new MapOfManagedTypeOrObjectName2Objects();
                managedTypeToObjectMap.MapOfManagedTypeNames = new Dictionary<string, Dictionary<string, DictionaryOrList>>();
                var typeNameClashes = new Dictionary<string,string>();
                foreach (var managedTypeGroup in IterateAndBuildTypeGroups(nativeTypeGroup.Value, snapshot))
                {
                    if(!managedTypeToObjectMap.MapOfManagedTypeNames.TryAdd(managedTypeGroup.Item1, managedTypeGroup.Item2))
                    {
                        if (!typeNameClashes.ContainsKey(managedTypeGroup.Item1))
                        {
                            var firstExistingElementDictOrList = managedTypeToObjectMap.MapOfManagedTypeNames[managedTypeGroup.Item1].First().Value;
                            var firstExistingElement = firstExistingElementDictOrList.ListOfObjects?.First()
                                ?? firstExistingElementDictOrList.MapOfObjects.First().Value.First();
                            var existingElementTypeIndex = snapshot.CrawledData.ManagedObjects[firstExistingElement.data.Source.Index].ITypeDescription;
                            var existingElementAssemblyName = snapshot.TypeDescriptions.Assembly[existingElementTypeIndex];
                            typeNameClashes.Add(managedTypeGroup.Item1, existingElementAssemblyName);
                        }
                        var firstNewElementDictOrList = managedTypeGroup.Item2.First().Value;
                        var firstNewElement = firstNewElementDictOrList.ListOfObjects?.First()
                            ?? firstNewElementDictOrList.MapOfObjects.First().Value.First();
                        var newElementTypeIndex = snapshot.CrawledData.ManagedObjects[firstNewElement.data.Source.Index].ITypeDescription;
                        var newElementAssemblyName = snapshot.TypeDescriptions.Assembly[newElementTypeIndex];
                        managedTypeToObjectMap.MapOfManagedTypeNames.Add($"{managedTypeGroup.Item1}{UnityObjectsComparisonModel.AssemblyNameDisambiguationSeparator}{newElementAssemblyName})", managedTypeGroup.Item2);
                    }
                }
                foreach (var clashedTypeName in typeNameClashes)
                {
                    managedTypeToObjectMap.MapOfManagedTypeNames.Add($"{clashedTypeName.Key}{UnityObjectsComparisonModel.AssemblyNameDisambiguationSeparator}{clashedTypeName.Value})", managedTypeToObjectMap.MapOfManagedTypeNames[clashedTypeName.Key]);
                    managedTypeToObjectMap.MapOfManagedTypeNames.Remove(clashedTypeName.Key);
                }
                nativeTypeNameToManagedTypeToObjectNameMapOrAndObjectsMap.Add(nativeTypeName, managedTypeToObjectMap);
            }
            foreach (var nativeTypeGroup in IterateAndBuildTypeGroups(nonDisambiguatedTechnicallyManagedTypeItems, snapshot))
            {
                var nativeTypeName = nativeTypeGroup.Item1;
                if (nativeTypeNameToManagedTypeToObjectNameMapOrAndObjectsMap.ContainsKey(nativeTypeName))
                {
                    nativeTypeNameToManagedTypeToObjectNameMapOrAndObjectsMap[nativeTypeName].MapOfNativeNames = nativeTypeGroup.Item2;
                }
                else
                {
                    nativeTypeNameToManagedTypeToObjectNameMapOrAndObjectsMap.Add(nativeTypeName,
                    new MapOfManagedTypeOrObjectName2Objects { MapOfNativeNames = nativeTypeGroup.Item2 });
                }
            }

            return nativeTypeNameToManagedTypeToObjectNameMapOrAndObjectsMap;
        }

        IEnumerator<Tuple<string,Dictionary<string, DictionaryOrList>>> IterateAndBuildTypeGroups(Dictionary<SourceIndex, DictionaryOrList> typeGroups, CachedSnapshot snapshot)
        {
            foreach (var typeGroup in typeGroups)
            {
                var typeIndex = typeGroup.Key;

                var typeName = typeIndex.GetName(snapshot);
                Dictionary<string, DictionaryOrList> mapOfNativeNames = null;
                var dictOrList = typeGroup.Value;
                if (dictOrList.ListOfObjects != null)
                {
                    mapOfNativeNames = GroupByName(dictOrList.ListOfObjects, dictOrList.Reason);
                }
                if (dictOrList.MapOfObjects != null)
                {
                    mapOfNativeNames = new Dictionary<string, DictionaryOrList>(dictOrList.MapOfObjects.Count);
                    foreach (var item in dictOrList.MapOfObjects)
                    {
                        mapOfNativeNames.Add(item.Key, GroupById(item.Value, dictOrList.Reason));
                    }
                }
                yield return new (typeName, mapOfNativeNames);
            }
        }

        DictionaryOrList
            GroupById(List<TreeViewItemData<UnityObjectsModel.ItemData>> typeObjects, DictionaryOrList.SplitReason splitReason)
        {
            var map = new DictionaryOrList() {
                MapOfObjects = new Dictionary<string, List<TreeViewItemData<UnityObjectsModel.ItemData>>>(typeObjects.Count),
                Reason = splitReason
            };

            // Group type's objects by their Id. The Ids are inherently unique and the dictionary mapping is used for comparison lookups.
            foreach (var typeObject in typeObjects)
            {
                var objectName = typeObject.data.Name;
                map.MapOfObjects.Add(objectName, new List<TreeViewItemData<UnityObjectsModel.ItemData>>(){typeObject });
            }

            return map;
        }

        Dictionary<string, DictionaryOrList>
            GroupByName(List<TreeViewItemData<UnityObjectsModel.ItemData>> typeObjects, DictionaryOrList.SplitReason splitReason)
        {
            var map = new Dictionary<string, DictionaryOrList>();

            // Group type's objects by their object name.
            foreach (var typeObject in typeObjects)
            {
                var objectName = typeObject.data.Name;

                var typeObjectsByName = map.GetOrAdd(objectName);
                typeObjectsByName.Reason = splitReason;
                typeObjectsByName.ListOfObjects ??= new List<TreeViewItemData<UnityObjectsModel.ItemData>>();
                typeObjectsByName.ListOfObjects.Add(typeObject);
            }

            return map;
        }

        List<TreeViewItemData<UnityObjectsComparisonModel.ItemData>> BuildUnityObjectComparisonTree(
            Dictionary<string, MapOfManagedTypeOrObjectName2Objects> typeNameToObjectNameAndObjectsMapA,
            Dictionary<string, MapOfManagedTypeOrObjectName2Objects> typeNameToObjectNameAndObjectsMapB,
            in BuildArgs args)
        {
            var rootNodes = new List<TreeViewItemData<UnityObjectsComparisonModel.ItemData>>();
            foreach (var kvp in typeNameToObjectNameAndObjectsMapA)
            {
                var comparisonNodes = new List<TreeViewItemData<UnityObjectsComparisonModel.ItemData>>();

                var nativeTypeName = kvp.Key;
                var objectNameToTypeObjectsMapA = kvp.Value;

                if (!typeNameToObjectNameAndObjectsMapB.TryGetValue(
                    nativeTypeName,
                    out var objectNameToTypeObjectsMapB))
                    objectNameToTypeObjectsMapB = null;

                // Process as a map of native Type names to objects
                if (objectNameToTypeObjectsMapA.MapOfNativeNames != null)
                {
                    BuildUnityObjectComparisonSubtree(nativeTypeName, objectNameToTypeObjectsMapA.MapOfNativeNames, objectNameToTypeObjectsMapB?.MapOfNativeNames, comparisonNodes, args, nativeTypeName: nativeTypeName);
                }
                if (objectNameToTypeObjectsMapA.MapOfManagedTypeNames != null)
                {

                    BuildManagedUnityObjectComparisonSubtree(nativeTypeName,
                        objectNameToTypeObjectsMapA,
                        objectNameToTypeObjectsMapA.MapOfManagedTypeNames,
                        objectNameToTypeObjectsMapB?.MapOfManagedTypeNames,
                        comparisonNodes,
                        args);
                }
                if (objectNameToTypeObjectsMapB != null)
                {
                    // if there was a map in B, remove it to mark it processed
                    typeNameToObjectNameAndObjectsMapB.Remove(nativeTypeName);
                }

                if (comparisonNodes.Count > 0)
                {
                    // Create node for Unity Object Type.
                    var node = CreateTypeComparisonNodeForUnityObjectComparisonNodes(
                        nativeTypeName,
                        comparisonNodes,
                        args.UnityObjectTypeComparisonSelectionProcessor);
                    rootNodes.Add(node);
                }
            }

            // Any Unity Object Types remaining in B's map are exclusive to B. Create comparison nodes for all created objects and a Unity Object Type node to parent them.
            foreach (var kvp in typeNameToObjectNameAndObjectsMapB)
            {
                var nativeTypeName = kvp.Key;
                var objectNameToTypeObjectsMapB = kvp.Value;

                var comparisonNodes = new List<TreeViewItemData<UnityObjectsComparisonModel.ItemData>>();

                // Process as a map of native Type names to objects
                if (objectNameToTypeObjectsMapB.MapOfNativeNames != null)
                {
                    BuildUnityObjectComparisonSubtree(nativeTypeName, null, objectNameToTypeObjectsMapB.MapOfNativeNames, comparisonNodes, args, nativeTypeName: nativeTypeName);
                }
                if (objectNameToTypeObjectsMapB.MapOfManagedTypeNames != null)
                {
                    BuildManagedUnityObjectComparisonSubtree(nativeTypeName,
                        objectNameToTypeObjectsMapB,
                        null,
                        objectNameToTypeObjectsMapB.MapOfManagedTypeNames,
                        comparisonNodes,
                        args);
                }

                if (comparisonNodes.Count > 0)
                {
                    // Create node for Unity Object Type exclusive to B.
                    var node = CreateTypeComparisonNodeForUnityObjectComparisonNodes(
                        nativeTypeName,
                        comparisonNodes,
                        args.UnityObjectTypeComparisonSelectionProcessor);
                    rootNodes.Add(node);
                }
            }

            return rootNodes;
        }

        void BuildManagedUnityObjectComparisonSubtree(
            string nativeTypeName,
            MapOfManagedTypeOrObjectName2Objects baseMap,
            Dictionary<string, Dictionary<string, DictionaryOrList>> MapOfManagedTypeNamesA,
            Dictionary<string, Dictionary<string, DictionaryOrList>> MapOfManagedTypeNamesB,
            List<TreeViewItemData<UnityObjectsComparisonModel.ItemData>> comparisonNodes,
            in BuildArgs args)
        {
            foreach (var managedTypeNameToObjectMapKVPA in baseMap.MapOfManagedTypeNames)
            {
                var managedTypeName = managedTypeNameToObjectMapKVPA.Key;
                Dictionary<string, DictionaryOrList> managedTypeNameToObjectMapA, managedTypeNameToObjectMapB;
                managedTypeNameToObjectMapA = managedTypeNameToObjectMapB = null;
                MapOfManagedTypeNamesA?.TryGetValue(managedTypeName, out managedTypeNameToObjectMapA);
                MapOfManagedTypeNamesB?.TryGetValue(managedTypeName, out managedTypeNameToObjectMapB);

                var managedComparisonNodes = new List<TreeViewItemData<UnityObjectsComparisonModel.ItemData>>();
                BuildUnityObjectComparisonSubtree(managedTypeName, managedTypeNameToObjectMapA, managedTypeNameToObjectMapB, managedComparisonNodes, args,
                    nativeTypeName: nativeTypeName, managedTypeName: managedTypeName);

                if (managedComparisonNodes.Count > 0)
                {
                    // Create node for the Managed Object Type.
                    var node = CreateTypeComparisonNodeForUnityObjectComparisonNodes(
                        nativeTypeName,
                        managedComparisonNodes,
                        args.UnityObjectTypeComparisonSelectionProcessor,
                        managedTypeName: managedTypeName);
                    comparisonNodes.Add(node);
                }
            }
        }

        void BuildUnityObjectComparisonSubtree(
            string groupName,
            Dictionary<string, DictionaryOrList> objectNameToTypeObjectsMapA,
            Dictionary<string, DictionaryOrList> objectNameToTypeObjectsMapB,
            List<TreeViewItemData<UnityObjectsComparisonModel.ItemData>> comparisonNodes,
            in BuildArgs args,
            string nativeTypeName = null, string managedTypeName = null)
        {
            // Cache and reuse these if necessary
            DictionaryOrList dummyDictionaryOrListA = null;
            DictionaryOrList dummyDictionaryOrListB = null;
            Dictionary<string, DictionaryOrList> dummyNameToTypeObjectsMapA = null;
            Dictionary<string, DictionaryOrList> dummyNameToTypeObjectsMapB = null;

            if(objectNameToTypeObjectsMapA != null)
            {
                foreach (var objectNameToTypeObjectsKvp in objectNameToTypeObjectsMapA)
                {
                    // Check if object with name exists in B for this type.
                    var objectName = objectNameToTypeObjectsKvp.Key;
                    var typeObjectsA = objectNameToTypeObjectsKvp.Value;
                    DictionaryOrList typeObjectsB = null;

                    // Check if Object Name exists in both A and B.
                    if (objectNameToTypeObjectsMapB?.TryGetValue(
                        objectName,
                        out typeObjectsB) ?? false)
                    {
                        TreeViewItemData<UnityObjectsComparisonModel.ItemData> comparisonNode;
                        // Object with name exists in B for this type. Create a comparison node for all matched objects.
                        if (typeObjectsA.ListOfObjects != null)
                        {
                            comparisonNode = CreateComparisonNodeForUnityObjects(
                                typeObjectsA.ListOfObjects,
                                typeObjectsB.ListOfObjects,
                                objectName,
                                typeObjectsA.Reason == DictionaryOrList.SplitReason.InstanceIDs ? groupName : objectName,
                                typeObjectsA.Reason == DictionaryOrList.SplitReason.InstanceIDs ? nativeTypeName ?? groupName : groupName,
                                args.UnityObjectNameGroupComparisonSelectionProcessor);
                        }
                        else if (typeObjectsA.MapOfObjects != null)
                        {
                            // process sub items
                            if (dummyDictionaryOrListA == null)
                            {
                                dummyDictionaryOrListA = new DictionaryOrList();
                                dummyDictionaryOrListB = new DictionaryOrList();
                                dummyNameToTypeObjectsMapA = new Dictionary<string, DictionaryOrList>();
                                dummyNameToTypeObjectsMapB = new Dictionary<string, DictionaryOrList>();
                            }
                            // inherit the reason for the split
                            dummyDictionaryOrListA.Reason = dummyDictionaryOrListB.Reason = typeObjectsA.Reason;

                            var nodes = new List<TreeViewItemData<UnityObjectsComparisonModel.ItemData>>();
                            foreach (var itemA in typeObjectsA.MapOfObjects)
                            {
                                dummyNameToTypeObjectsMapA.Clear();
                                dummyNameToTypeObjectsMapB.Clear();
                                dummyDictionaryOrListA.ListOfObjects = itemA.Value;

                                if (typeObjectsB.MapOfObjects.TryGetValue(itemA.Key, out var itemsB))
                                {
                                    dummyDictionaryOrListB.ListOfObjects = itemsB;
                                    // remove and mark as processed
                                    typeObjectsB.MapOfObjects.Remove(itemA.Key);
                                }
                                else
                                    dummyDictionaryOrListB.ListOfObjects = null;

                                dummyNameToTypeObjectsMapA.Add(itemA.Key, dummyDictionaryOrListA);
                                dummyNameToTypeObjectsMapB.Add(itemA.Key, dummyDictionaryOrListB);

                                BuildUnityObjectComparisonSubtree(objectName, dummyNameToTypeObjectsMapA, dummyNameToTypeObjectsMapB, nodes, args,
                                    nativeTypeName: nativeTypeName, managedTypeName: managedTypeName);
                            }
                            // process items only in B
                            dummyDictionaryOrListA.ListOfObjects = null;
                            foreach (var itemB in typeObjectsB.MapOfObjects)
                            {
                                dummyNameToTypeObjectsMapB.Clear();
                                dummyDictionaryOrListB.ListOfObjects = itemB.Value;
                                dummyNameToTypeObjectsMapB.Add(itemB.Key, dummyDictionaryOrListB);
                                BuildUnityObjectComparisonSubtree(objectName, null, dummyNameToTypeObjectsMapB, nodes, args,
                                    nativeTypeName: nativeTypeName, managedTypeName: managedTypeName);
                            }
                            // remove and mark as processed
                            typeObjectsB.MapOfObjects.Clear();

                            if (objectName != nativeTypeName)
                                comparisonNode = CreateComparisonNodeForUnityObjectComparisonNodes(
                                    nodes, objectName, nativeTypeName,
                                    args.UnityObjectNameGroupComparisonSelectionProcessor);
                            else
                                comparisonNode = CreateTypeComparisonNodeForUnityObjectComparisonNodes(objectName, nodes, null);
                        }
                        else
                            throw new Exception($"Expected either {nameof(typeObjectsA.ListOfObjects)} or {nameof(typeObjectsA.MapOfObjects)} to not be null");

                        // Check the current filters include unchanged.
                        if (args.IncludeUnchanged)
                            comparisonNodes.Add(comparisonNode);
                        else
                        {
                            if (comparisonNode.data.HasChanged)
                                comparisonNodes.Add(comparisonNode);
                        }
                        // Remove from B's objects map (mark as processed).
                        objectNameToTypeObjectsMapB.Remove(objectName);
                    }
                    else
                    {
                        TreeViewItemData<UnityObjectsComparisonModel.ItemData> comparisonNode;
                        // This object name wasn't found in B for this type, so all this type's Unity Objects with this name are exclusive to A. Create a comparison node for all deleted objects.
                        if(typeObjectsA.ListOfObjects != null)
                        {
                            comparisonNode = CreateComparisonNodeForDeletedUnityObjects(
                                typeObjectsA.ListOfObjects,
                                objectName,
                                typeObjectsA.Reason == DictionaryOrList.SplitReason.InstanceIDs ? groupName : objectName,
                                typeObjectsA.Reason == DictionaryOrList.SplitReason.InstanceIDs ? nativeTypeName : groupName,
                                args.UnityObjectNameGroupComparisonSelectionProcessor);
                        }
                        else if(typeObjectsA.MapOfObjects != null)
                        {
                            if (dummyDictionaryOrListA == null)
                            {
                                dummyDictionaryOrListA = new DictionaryOrList();
                                dummyDictionaryOrListB = new DictionaryOrList();
                                dummyNameToTypeObjectsMapA = new Dictionary<string, DictionaryOrList>();
                                dummyNameToTypeObjectsMapB = new Dictionary<string, DictionaryOrList>();
                            }
                            // inherit the reason for the split
                            dummyDictionaryOrListA.Reason = dummyDictionaryOrListB.Reason = typeObjectsA.Reason;

                            var nodes = new List<TreeViewItemData<UnityObjectsComparisonModel.ItemData>>();
                            foreach (var itemA in typeObjectsA.MapOfObjects)
                            {
                                dummyNameToTypeObjectsMapA.Clear();
                                dummyDictionaryOrListA.ListOfObjects = itemA.Value;
                                dummyNameToTypeObjectsMapA.Add(itemA.Key, dummyDictionaryOrListA);
                                BuildUnityObjectComparisonSubtree(objectName, dummyNameToTypeObjectsMapA, null, nodes, args,
                                    nativeTypeName: nativeTypeName, managedTypeName: managedTypeName);
                            }
                            if (objectName != nativeTypeName)
                                comparisonNode = CreateComparisonNodeForUnityObjectComparisonNodes(
                                    nodes, objectName, nativeTypeName,
                                    args.UnityObjectNameGroupComparisonSelectionProcessor);
                            else
                                comparisonNode = CreateTypeComparisonNodeForUnityObjectComparisonNodes(objectName, nodes, null);
                        }
                        else
                            throw new Exception($"Expected either {nameof(typeObjectsA.ListOfObjects)} or {nameof(typeObjectsA.MapOfObjects)} to not be null");
                        comparisonNodes.Add(comparisonNode);
                    }
                }
            }

            if(objectNameToTypeObjectsMapB != null)
            {
                // Any Object Names remaining in B's map are exclusive to B. Create comparison nodes for each group of created objects of this type remaining.
                foreach (var objectNameToTypeObjectsKvp in objectNameToTypeObjectsMapB)
                {
                    var objectName = objectNameToTypeObjectsKvp.Key;
                    var typeObjectsB = objectNameToTypeObjectsKvp.Value;
                    TreeViewItemData<UnityObjectsComparisonModel.ItemData> comparisonNode;
                    if(typeObjectsB.ListOfObjects != null)
                    {
                        comparisonNode = CreateComparisonNodeForCreatedUnityObjects(
                            typeObjectsB.ListOfObjects,
                            objectName,
                            typeObjectsB.Reason == DictionaryOrList.SplitReason.InstanceIDs ? groupName : objectName,
                            typeObjectsB.Reason == DictionaryOrList.SplitReason.InstanceIDs ? nativeTypeName : groupName,
                            args.UnityObjectNameGroupComparisonSelectionProcessor);
                    }
                    else if(typeObjectsB.MapOfObjects != null)
                    {
                        if (dummyDictionaryOrListB == null)
                        {
                            dummyDictionaryOrListB = new DictionaryOrList();
                            dummyNameToTypeObjectsMapB = new Dictionary<string, DictionaryOrList>();
                        }
                        // inherit the reason for the split
                        dummyDictionaryOrListB.Reason = typeObjectsB.Reason;

                        var nodes = new List<TreeViewItemData<UnityObjectsComparisonModel.ItemData>>();
                        foreach (var itemB in typeObjectsB.MapOfObjects)
                        {
                            dummyNameToTypeObjectsMapB.Clear();
                            dummyDictionaryOrListB.ListOfObjects = itemB.Value;
                            dummyNameToTypeObjectsMapB.Add(itemB.Key, dummyDictionaryOrListB);
                            BuildUnityObjectComparisonSubtree(objectName, null, dummyNameToTypeObjectsMapB, nodes, args,
                                    nativeTypeName: nativeTypeName, managedTypeName: managedTypeName);
                        }
                        if (objectName != nativeTypeName)
                            comparisonNode = CreateComparisonNodeForUnityObjectComparisonNodes(
                                nodes, objectName, nativeTypeName,
                                args.UnityObjectNameGroupComparisonSelectionProcessor);
                        else
                        comparisonNode = CreateTypeComparisonNodeForUnityObjectComparisonNodes(objectName, nodes, null);
                    }
                    else
                        throw new Exception($"Expected either {nameof(typeObjectsB.ListOfObjects)} or {nameof(typeObjectsB.MapOfObjects)} to not be null");

                    comparisonNodes.Add(comparisonNode);
                }

                // Remove all processed Object Name groups from B's map.
                objectNameToTypeObjectsMapB.Clear();
            }
        }

        TreeViewItemData<UnityObjectsComparisonModel.ItemData> CreateComparisonNodeForCreatedUnityObjects(
            List<TreeViewItemData<UnityObjectsModel.ItemData>> createdObjects,
            string tableEntryName,
            string unityObjectName,
            string typeName,
            Action<string, string, string, SnapshotType> unityObjectNameGroupComparisonSelectionProcessor)
        {
            return CreateComparisonNodeForUnityObjects(
                null,
                createdObjects,
                tableEntryName,
                unityObjectName,
                typeName,
                unityObjectNameGroupComparisonSelectionProcessor);
        }

        TreeViewItemData<UnityObjectsComparisonModel.ItemData> CreateComparisonNodeForDeletedUnityObjects(
            List<TreeViewItemData<UnityObjectsModel.ItemData>> deletedObjects,
            string tableEntryName,
            string unityObjectName,
            string typeName,
            Action<string, string, string, SnapshotType> unityObjectNameGroupComparisonSelectionProcessor)
        {
            return CreateComparisonNodeForUnityObjects(
                deletedObjects,
                null,
                tableEntryName,
                unityObjectName,
                typeName,
                unityObjectNameGroupComparisonSelectionProcessor);
        }

        TreeViewItemData<UnityObjectsComparisonModel.ItemData> CreateComparisonNodeForUnityObjects(
            List<TreeViewItemData<UnityObjectsModel.ItemData>> unityObjectsA,
            List<TreeViewItemData<UnityObjectsModel.ItemData>> unityObjectsB,
            string tableEntryName,
            string unityObjectName,
            string nativeTypeName,
            Action<string, string, string, SnapshotType> unityObjectNameGroupComparisonSelectionProcessor)
        {
            var totalSizeInA = new MemorySize();
            var countInA = 0U;
            if (unityObjectsA != null)
            {
                foreach (var typeObject in unityObjectsA)
                {
                    totalSizeInA += typeObject.data.TotalSize;
                    countInA++;
                }
            }

            var totalSizeInB = new MemorySize();
            var countInB = 0U;
            if (unityObjectsB != null)
            {
                foreach (var typeObject in unityObjectsB)
                {
                    totalSizeInB += typeObject.data.TotalSize;
                    countInB++;
                }
            }

            var childCount = 0;
            void ProcessUnityObjectNameGroupComparisonSelection()
            {
                unityObjectNameGroupComparisonSelectionProcessor?.Invoke(unityObjectName, nativeTypeName,
                    tableEntryName.StartsWith(NativeObjectTools.NativeObjectIdFormatStringPrefix) ? tableEntryName : null,
                    unityObjectsA != null ? SnapshotType.Base : SnapshotType.Compared);
            }

            return new TreeViewItemData<UnityObjectsComparisonModel.ItemData>(
                m_ItemId++,
                new UnityObjectsComparisonModel.ItemData(
                    tableEntryName,
                    totalSizeInA,
                    totalSizeInB,
                    countInA,
                    countInB,
                    nativeTypeName,
                    ProcessUnityObjectNameGroupComparisonSelection,
                    childCount)
            );
        }

        TreeViewItemData<UnityObjectsComparisonModel.ItemData> CreateComparisonNodeForUnityObjectComparisonNodes(
            List<TreeViewItemData<UnityObjectsComparisonModel.ItemData>> comparisonNodes,
            string unityObjectName,
            string nativeTypeName,
            Action<string, string, string, SnapshotType> unityObjectNameGroupComparisonSelectionProcessor,
            string managedTypeName = null)
        {
            var totalSizeInA = new MemorySize();
            var totalSizeInB = new MemorySize();
            var countInA = 0U;
            var countInB = 0U;
            foreach (var comparisonNode in comparisonNodes)
            {
                totalSizeInA += comparisonNode.data.TotalSizeInA;
                totalSizeInB += comparisonNode.data.TotalSizeInB;
                countInA += comparisonNode.data.CountInA;
                countInB += comparisonNode.data.CountInB;
            }

            void ProcessUnityObjectTypeComparisonSelection()
            {
                // only filter by native type and name, don't filter by instance Id. A
                unityObjectNameGroupComparisonSelectionProcessor?.Invoke(unityObjectName, nativeTypeName, null,
                    countInA > 0 ? (countInB > 0 ? SnapshotType.Undefined : SnapshotType.Base) : (countInB > 0 ? SnapshotType.Compared : SnapshotType.Undefined));
            };

            // Create node for Unity Object Type.
            return new TreeViewItemData<UnityObjectsComparisonModel.ItemData>(
                m_ItemId++,
                new UnityObjectsComparisonModel.ItemData(
                    unityObjectName,
                    totalSizeInA,
                    totalSizeInB,
                    countInA,
                    countInB,
                    nativeTypeName,
                    ProcessUnityObjectTypeComparisonSelection,
                    comparisonNodes.Count),
                comparisonNodes);
        }

        TreeViewItemData<UnityObjectsComparisonModel.ItemData> CreateTypeComparisonNodeForUnityObjectComparisonNodes(
            string nativeTypeName,
            List<TreeViewItemData<UnityObjectsComparisonModel.ItemData>> comparisonNodes,
            Action<string> unityObjectTypeComparisonSelectionProcessor,
            string managedTypeName = null)
        {
            var totalSizeInA = new MemorySize();
            var totalSizeInB = new MemorySize();
            var countInA = 0U;
            var countInB = 0U;
            foreach (var comparisonNode in comparisonNodes)
            {
                totalSizeInA += comparisonNode.data.TotalSizeInA;
                totalSizeInB += comparisonNode.data.TotalSizeInB;
                countInA += comparisonNode.data.CountInA;
                countInB += comparisonNode.data.CountInB;
            }

            void ProcessUnityObjectTypeComparisonSelection()
            {
                unityObjectTypeComparisonSelectionProcessor?.Invoke(nativeTypeName);
            };

            // Create node for Unity Object Type.
            return new TreeViewItemData<UnityObjectsComparisonModel.ItemData>(
                m_ItemId++,
                new UnityObjectsComparisonModel.ItemData(
                    managedTypeName ?? nativeTypeName,
                    totalSizeInA,
                    totalSizeInB,
                    countInA,
                    countInB,
                    nativeTypeName,
                    ProcessUnityObjectTypeComparisonSelection,
                    comparisonNodes.Count),
                comparisonNodes);
        }

        public new readonly struct BuildArgs
        {
            public BuildArgs(
                IScopedFilter<string> searchStringFilter,
                ITextFilter unityObjectNameFilter,
                IInstancIdFilter unityObjectInstanceIDFilter,
                bool flattenHierarchy,
                bool includeUnchanged,
                bool disambiguateByInstanceId,
                Action<string, string, string, SnapshotType> unityObjectNameGroupComparisonSelectionProcessor,
                Action<string> unityObjectTypeComparisonSelectionProcessor)
            {
                SearchStringFilter = searchStringFilter;
                UnityObjectNameFilter = unityObjectNameFilter;
                UnityObjectInstanceIDFilter = unityObjectInstanceIDFilter;
                IncludeUnchanged = includeUnchanged;
                FlattenHierarchy = flattenHierarchy;
                DisambiguateByInstanceId = disambiguateByInstanceId && !flattenHierarchy;
                UnityObjectNameGroupComparisonSelectionProcessor = unityObjectNameGroupComparisonSelectionProcessor;
                UnityObjectTypeComparisonSelectionProcessor = unityObjectTypeComparisonSelectionProcessor;
            }


            // Only include tree paths that match this filter.
            public IScopedFilter<string> SearchStringFilter { get; }

            // Only include Unity Objects with a name that passes this filter.
            public ITextFilter UnityObjectNameFilter { get; }

            // Only include the Unity Object with this instance ID. Null means do not filter by instance id. CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone (0) can be used to filter everything (used for comparison).
            public IInstancIdFilter UnityObjectInstanceIDFilter { get; }

            // Include unchanged Unity Objects.
            public bool IncludeUnchanged { get; }

            // Flatten hierarchy to leaf nodes only (remove all categorization).
            public bool FlattenHierarchy { get; }

            /// <summary>
            /// Use Instance IDs to differentiate objects with the same name
            /// </summary>
            public bool DisambiguateByInstanceId { get; }

            /// <summary>
            /// Selection processor for a Unity Object Name Group comparison item.
            /// Unity Objects of the same type in each snapshot are grouped by name and then compared as groups.
            /// Arguments are
            /// the native object's name
            /// the native object's type's name (in both snapshots),
            /// the instance ID string or null,
            /// Which Snapshot this is in
            /// </summary>
            public Action<string, string, string, SnapshotType> UnityObjectNameGroupComparisonSelectionProcessor { get; }

            // Selection processor for a Unity Object Type comparison item. Argument is the native object's type's name (in both snapshots).
            public Action<string> UnityObjectTypeComparisonSelectionProcessor { get; }
        }

        public enum SnapshotType
        {
            Undefined,
            Base,
            Compared
        }

    }
}
#endif

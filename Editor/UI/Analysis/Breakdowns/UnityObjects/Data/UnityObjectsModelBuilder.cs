#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MemoryProfiler.Editor.Extensions;
using UnityEngine;
using UnityEngine.UIElements;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Builds a UnityObjectsModel.
    class UnityObjectsModelBuilder
    {
        // This filter is kind of a hack that is used to leave the Base and Compare tables empty.
        // To avoid the cost of having to rebuild a model that is entirely empty while iterating
        // over all Unity Objects in the snapshot, using this exact instance will sidestep the model generation entirely.
        public static readonly IInstancIdFilter ShowNoObjectsAtAllFilter = MatchesInstanceIdFilter.Create(CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone, null);

        protected int m_ItemId;

        public UnityObjectsModelBuilder()
        {
            m_ItemId = (int)IAnalysisViewSelectable.Category.FirstDynamicId;
        }

        public UnityObjectsModel Build(CachedSnapshot snapshot, in BuildArgs args)
        {
            if (!CanBuildBreakdownForSnapshot(snapshot))
                throw new UnsupportedSnapshotVersionException(snapshot);

            var nativeUnityObjectBaseTypesToDisambiguateByManagedType = BuildListOfNativeUnityObjectBaseTypesToDisambiguateByManagedType(snapshot);

            var rootNodes = BuildUnityObjectsGroupedByType(snapshot, nativeUnityObjectBaseTypesToDisambiguateByManagedType,
                args, out var totalMemoryInSnapshot);
            if (args.FlattenHierarchy)
                rootNodes = TreeModelUtility.RetrieveLeafNodesOfTree(rootNodes);

            var model = new UnityObjectsModel(rootNodes, totalMemoryInSnapshot, args.SelectionProcessor);
            return model;
        }

        protected static bool CanBuildBreakdownForSnapshot(CachedSnapshot snapshot)
        {
            return true;
        }

        protected static HashSet<SourceIndex> BuildListOfNativeUnityObjectBaseTypesToDisambiguateByManagedType(CachedSnapshot snapshot)
        {
            // These types are ones that Unity users extensively build upon.
            // They also always have a Managed Type and Object to their native Type and Object.
            // We can thus use the Managed Types of objects that derive from these to further differentiate the list by using the user defined types
            var listOfNativeTypes = new HashSet<SourceIndex>();
            if (snapshot.NativeTypes.EditorScriptableObjectIdx >= NativeTypeEntriesCache.FirstValidTypeIndex)
                listOfNativeTypes.Add(new SourceIndex(SourceIndex.SourceId.NativeType, snapshot.NativeTypes.EditorScriptableObjectIdx));

            if (snapshot.NativeTypes.ScriptableObjectIdx >= NativeTypeEntriesCache.FirstValidTypeIndex)
                listOfNativeTypes.Add(new SourceIndex(SourceIndex.SourceId.NativeType, snapshot.NativeTypes.ScriptableObjectIdx));

            if (snapshot.NativeTypes.MonoBehaviourIdx >= NativeTypeEntriesCache.FirstValidTypeIndex)
                listOfNativeTypes.Add(new SourceIndex(SourceIndex.SourceId.NativeType, snapshot.NativeTypes.MonoBehaviourIdx));

            return listOfNativeTypes;
        }

        List<TreeViewItemData<UnityObjectsModel.ItemData>> BuildUnityObjectsGroupedByType(
            CachedSnapshot snapshot,
            HashSet<SourceIndex> nativeUnityObjectBaseTypesToDisambiguateByManagedType,
            in BuildArgs args,
            out MemorySize totalMemoryInSnapshot)
        {
            BuildUnityObjectTypeIndexToUnityObjectsMapForSnapshot(
                snapshot,
                args,
                nativeUnityObjectBaseTypesToDisambiguateByManagedType,
                out var typeIndexToTypeObjectsMap,
                out var disambiguatedTypeIndexToTypeObjectsMap,
                out var nonDisambiguatedTechnicallyManagedTypeItems,
                out totalMemoryInSnapshot);

            // Filter by potential duplicates, if necessary.
            if (args.PotentialDuplicatesFilter)
            {
                typeIndexToTypeObjectsMap = FilterTypeIndexToTypeObjectsMapForPotentialDuplicatesAndGroup(
                    typeIndexToTypeObjectsMap);
                foreach (var nativeType in nativeUnityObjectBaseTypesToDisambiguateByManagedType)
                {
                    if (disambiguatedTypeIndexToTypeObjectsMap.ContainsKey(nativeType))
                    {
                        disambiguatedTypeIndexToTypeObjectsMap[nativeType] =
                            FilterTypeIndexToTypeObjectsMapForPotentialDuplicatesAndGroup(
                            disambiguatedTypeIndexToTypeObjectsMap[nativeType]);
                    }
                }
                nonDisambiguatedTechnicallyManagedTypeItems = FilterTypeIndexToTypeObjectsMapForPotentialDuplicatesAndGroup(
                    nonDisambiguatedTechnicallyManagedTypeItems);
            }

            // Build a tree of Unity Objects, grouped by Unity Object Type, from the map.
            var unityObjectsTree = new List<TreeViewItemData<UnityObjectsModel.ItemData>>(typeIndexToTypeObjectsMap.Count);
            foreach (var kvp in typeIndexToTypeObjectsMap)
            {
                unityObjectsTree.Add(CreateUnityObjectTypeGroup(snapshot, kvp));
            }

            foreach (var nativeTypeToManagedTypesKVP in disambiguatedTypeIndexToTypeObjectsMap)
            {
                var nativeUndisambiguatedItems = new KeyValuePair<SourceIndex, DictionaryOrList>(
                    nativeTypeToManagedTypesKVP.Key,
                    nonDisambiguatedTechnicallyManagedTypeItems.ContainsKey(nativeTypeToManagedTypesKVP.Key)
                    ? nonDisambiguatedTechnicallyManagedTypeItems[nativeTypeToManagedTypesKVP.Key] : null);

                var managedTypesCount = nativeTypeToManagedTypesKVP.Value.Count;
                if (managedTypesCount > 0)
                    unityObjectsTree.Add(CreateManagedUnityObjectTypeGroup(snapshot, nativeTypeToManagedTypesKVP, nativeUndisambiguatedItems));
            }

            return unityObjectsTree;
        }

        TreeViewItemData<UnityObjectsModel.ItemData> CreateManagedUnityObjectTypeGroup(CachedSnapshot snapshot,
            KeyValuePair<SourceIndex, Dictionary<SourceIndex, DictionaryOrList>> managedKvp,
            KeyValuePair<SourceIndex, DictionaryOrList> nativeUndisambiguatedItems)
        {
            var children = new List<TreeViewItemData<UnityObjectsModel.ItemData>>();
            foreach (var kvp2 in managedKvp.Value)
            {
                children.Add(CreateUnityObjectTypeGroup(snapshot, kvp2));
            }
            // Technically these shouldn't exist if Managed Objects are captured.
            // Practically they can come about as a race condition of the capture process (or because Managed Objects weren't captured).
            if (nativeUndisambiguatedItems.Value != null)
                children.Add(CreateUnityObjectTypeGroup(snapshot, nativeUndisambiguatedItems));

            return CreateGroupNode(snapshot.NativeTypes.TypeName[managedKvp.Key.Index], managedKvp.Key, children);
        }

        TreeViewItemData<UnityObjectsModel.ItemData> CreateUnityObjectTypeGroup(CachedSnapshot snapshot,
            KeyValuePair<SourceIndex, DictionaryOrList> kvp)
        {
            var typeSource = kvp.Key;
            var dictionaryOrList = kvp.Value;

            // Calculate the total size of the Unity Object Type by summing all of its Unity Objects.
            if(dictionaryOrList.ListOfObjects != null)
            {
                var typeObjects = dictionaryOrList.ListOfObjects;
                return CreateGroupNode(typeSource.GetName(snapshot), typeSource, dictionaryOrList.ListOfObjects);
            }
            else
            {
                var typeGroupList = new List<TreeViewItemData<UnityObjectsModel.ItemData>>();
                foreach (var listOfObjectsById in dictionaryOrList.MapOfObjects)
                {
                    typeGroupList.Add(CreateGroupNode(listOfObjectsById.Key, new SourceIndex(), listOfObjectsById.Value));
                }
                return CreateGroupNode(typeSource.GetName(snapshot), typeSource, typeGroupList);
            }
        }

        TreeViewItemData<UnityObjectsModel.ItemData> CreateGroupNode(string groupName, SourceIndex sourceIndex, List<TreeViewItemData<UnityObjectsModel.ItemData>> items)
        {
            var typeNativeSize = new MemorySize();
            var typeManagedSize = new MemorySize();
            var typeGpuSize = new MemorySize();
            foreach (var typeObject in items)
            {
                typeNativeSize += typeObject.data.NativeSize;
                typeManagedSize += typeObject.data.ManagedSize;
                typeGpuSize += typeObject.data.GpuSize;
            }

            // Create node for Unity Object Type.
            return new TreeViewItemData<UnityObjectsModel.ItemData>(
                m_ItemId++,
                new UnityObjectsModel.ItemData(
                    groupName,
                    typeNativeSize,
                    typeManagedSize,
                    typeGpuSize,
                    sourceIndex,
                    items.Count),
                items);
        }

        struct UnityObjectSize
        {
            public MemorySize Native;
            public MemorySize Managed;
            public MemorySize Gfx;

            public static UnityObjectSize operator +(UnityObjectSize l, UnityObjectSize r)
            {
                return new UnityObjectSize() { Native = l.Native + r.Native, Managed = l.Managed + r.Managed, Gfx = l.Gfx + r.Gfx };
            }
        };

        static void AccumulateValue(Dictionary<SourceIndex, UnityObjectSize> accumulator, SourceIndex index, MemorySize native, MemorySize managed, MemorySize gpu)
        {
            var sizeValue = new UnityObjectSize() { Native = native, Managed = managed, Gfx = gpu };
            if (accumulator.TryGetValue(index, out var storedValue))
                sizeValue += storedValue;

            accumulator[index] = sizeValue;
        }

        Dictionary<SourceIndex, UnityObjectSize> BuildNativeObjectIndexToSize(
            CachedSnapshot snapshot,
            out MemorySize _totalMemoryInSnapshot)
        {
            // Extract all native objects and related data from the hierarchy
            var nativeObjects = snapshot.NativeObjects;
            var managedObjects = snapshot.CrawledData.ManagedObjects; // only a copy of the dynamic array but only used as readonly shortcut, can't be ref readonly as it's used in an anonymous method
            var nativeAllocations = snapshot.NativeAllocations;
            var nativeGfxResourceReferences = snapshot.NativeGfxResourceReferences;
            var nativeObject2Size = new Dictionary<SourceIndex, UnityObjectSize>();
            var totalMemoryInSnapshot = new MemorySize();
            snapshot.EntriesMemoryMap.ForEachFlatWithResidentSize((index, address, size, residentSize, source) =>
            {
                var memorySize = new MemorySize(size, residentSize);

                totalMemoryInSnapshot += memorySize;

                // Add items to respective group container
                switch (source.Id)
                {
                    case SourceIndex.SourceId.NativeObject:
                        {
                            AccumulateValue(nativeObject2Size, source, memorySize, new MemorySize(), new MemorySize());
                            break;
                        }
                    case SourceIndex.SourceId.NativeAllocation:
                        {
                            var rootReferenceId = nativeAllocations.RootReferenceId[source.Index];
                            if (rootReferenceId <= 0)
                                break;

                            // Is this allocation associated with a native object?
                            if (!nativeObjects.RootReferenceIdToIndex.TryGetValue(rootReferenceId, out var nativeObjectIndex))
                                break;

                            AccumulateValue(nativeObject2Size, new SourceIndex(SourceIndex.SourceId.NativeObject, nativeObjectIndex), memorySize, new MemorySize(), new MemorySize());
                            break;
                        }
                    case SourceIndex.SourceId.ManagedObject:
                        {
                            // Do we have a native object associated with the managed object
                            var nativeObjectIndex = managedObjects[source.Index].NativeObjectIndex;
                            if (nativeObjectIndex < NativeTypeEntriesCache.FirstValidTypeIndex)
                                break;

                            AccumulateValue(nativeObject2Size, new SourceIndex(SourceIndex.SourceId.NativeObject, nativeObjectIndex), new MemorySize(), memorySize, new MemorySize());
                            break;
                        }

                    // Ignore regions of these types
                    case SourceIndex.SourceId.NativeMemoryRegion:
                    case SourceIndex.SourceId.ManagedHeapSection:
                    case SourceIndex.SourceId.SystemMemoryRegion:
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
                totalMemoryInSnapshot = new MemorySize(memoryStats.Value.TotalVirtualMemory, 0);

            // Add graphics resources separately, as we don't have them in memory map.
            if (snapshot.HasGfxResourceReferencesAndAllocators
                && snapshot.MetaData.TargetInfo.HasValue
                && snapshot.MetaData.UnityVersionMajor >= 2023)
                AddGraphicsResources(snapshot, nativeObject2Size);
            else
                AddLegacyGraphicsResources(snapshot, nativeObject2Size);

            _totalMemoryInSnapshot = totalMemoryInSnapshot;

            return nativeObject2Size;
        }

        void AddGraphicsResources(CachedSnapshot snapshot, Dictionary<SourceIndex, UnityObjectSize> nativeObject2Size)
        {
            var nativeGfxRes = snapshot.NativeGfxResourceReferences;
            for (var i = 0; i < nativeGfxRes.Count; i++)
            {
                var size = nativeGfxRes.GfxSize[i];
                if (size == 0)
                    continue;

                var rootReferenceId = nativeGfxRes.RootId[i];
                if (rootReferenceId <= 0)
                    continue;

                // Lookup native object index associated with memory label root
                if (!snapshot.NativeObjects.RootReferenceIdToIndex.TryGetValue(rootReferenceId, out var objectIndex))
                    continue;

                var memorySize = new MemorySize(size, 0);
                AccumulateValue(nativeObject2Size, new SourceIndex(SourceIndex.SourceId.NativeObject, objectIndex), new MemorySize(), new MemorySize(), memorySize);
            }
        }

        void AddLegacyGraphicsResources(CachedSnapshot snapshot, Dictionary<SourceIndex, UnityObjectSize> nativeObject2Size)
        {
            var nativeObjects = snapshot.NativeObjects;
            var nativeRootReferences = snapshot.NativeRootReferences;
            var keys = nativeObject2Size.Keys.ToList();
            foreach (var key in keys)
            {
                if (key.Id != SourceIndex.SourceId.NativeObject)
                    continue;

                var totalSize = nativeObjects.Size[key.Index];
                var rootReferenceId = nativeObjects.RootReferenceId[key.Index];
                if (rootReferenceId <= 0)
                    continue;

                if (!nativeRootReferences.IdToIndex.TryGetValue(rootReferenceId, out var rootReferenceIndex))
                    continue;

                var rootAccumulatedtSize = nativeRootReferences.AccumulatedSize[rootReferenceIndex];
                if (rootAccumulatedtSize >= totalSize)
                    continue;

                var record = nativeObject2Size[key];
                nativeObject2Size[key] = new UnityObjectSize() { Native = new MemorySize(rootAccumulatedtSize, 0), Managed = record.Managed, Gfx = new MemorySize(totalSize - rootAccumulatedtSize, 0) };
            }
        }

        protected void BuildUnityObjectTypeIndexToUnityObjectsMapForSnapshot(
            CachedSnapshot snapshot,
            in BuildArgs args,
            HashSet<SourceIndex> nativeUnityObjectBaseTypesToDisambiguateByManagedType,
            out Dictionary<SourceIndex, DictionaryOrList> typeIndexToTypeObjectsMap,
            out Dictionary<SourceIndex, Dictionary<SourceIndex, DictionaryOrList>> disambiguatedTypeIndexToTypeObjectsMap,
            out Dictionary<SourceIndex, DictionaryOrList> nonDisambiguatedObjectsOfDisambiguatedNativeTypes,
            out MemorySize totalMemoryInSnapshot)
        {
            // If filtering specifically for no objects, don't do any work at all.
            typeIndexToTypeObjectsMap = new Dictionary<SourceIndex, DictionaryOrList>();
            disambiguatedTypeIndexToTypeObjectsMap = new Dictionary<SourceIndex, Dictionary<SourceIndex, DictionaryOrList>>();
            nonDisambiguatedObjectsOfDisambiguatedNativeTypes = new Dictionary<SourceIndex, DictionaryOrList>();

            if (args.UnityObjectInstanceIDFilter == ShowNoObjectsAtAllFilter)
            {
                totalMemoryInSnapshot = new MemorySize();
                return;
            }

            var nativeObject2Size = BuildNativeObjectIndexToSize(snapshot, out totalMemoryInSnapshot);

            // Group objects by type
            var nativeTypes = snapshot.NativeTypes;
            var nativeObjects = snapshot.NativeObjects;
            foreach (var obj in nativeObject2Size)
            {
                if (!(args.UnityObjectInstanceIDFilter?.Passes(nativeObjects.InstanceId[obj.Key.Index], snapshot) ?? true))
                    continue;

                int managedTypeIndex = -1;
                string managedTypeName = null;
                if (nativeObjects.ManagedObjectIndex[obj.Key.Index] >= 0)
                {
                    managedTypeIndex = snapshot.CrawledData.ManagedObjects[nativeObjects.ManagedObjectIndex[obj.Key.Index]].ITypeDescription;
                    // Due to bug PROF-2420, some Native Objects report associated managed objects that are invalid
                    // and therefore have no type associated with them. This check is here to mitigate this bug,
                    // to avert a failure to create the Unity Objects table when that happens.
                    // The mitigation should probably stay in place even after that bug is fixed,
                    // as old snapshot data with that fault can't be fixed retroactively.
                    // (At max, the Managed Base Type can be used in some, but not all instances.)
                    if(managedTypeIndex >= 0)
                        managedTypeName = snapshot.TypeDescriptions.TypeDescriptionName[managedTypeIndex];
                }

                // Filter by Unity-Object-Type-Name. Skip objects that don't pass the type name filter.
                var typeIndex = nativeObjects.NativeTypeArrayIndex[obj.Key.Index];
                string nativeTypeName = null;
                if (args.UnityObjectTypeNameFilter != null)
                {
                    nativeTypeName = nativeTypes.TypeName[typeIndex];
                    if (!(args.UnityObjectTypeNameFilter.Passes(nativeTypeName) || (managedTypeName != null && args.UnityObjectTypeNameFilter.Passes(managedTypeName))))
                        continue;
                }

                // Filter by Unity-Object-Name. Skip objects that don't pass the name filter.
                var nativeObjectName = nativeObjects.ObjectName[obj.Key.Index];
                if (args.UnityObjectNameFilter != null)
                {
                    if (!args.UnityObjectNameFilter.Passes(nativeObjectName))
                        continue;
                }

                var itemName = args.DisambiguateByInstanceId ?
                    NativeObjectTools.ProduceNativeObjectId(obj.Key.Index, snapshot) : nativeObjectName;

                if (args.SearchStringFilter != null)
                {
                    // Check if Native Type Name still needs initializing
                    if(args.UnityObjectTypeNameFilter == null)
                        nativeTypeName = nativeTypes.TypeName[typeIndex];

                    string managedBaseTypeName = null;
                    if (snapshot.CrawledData.NativeUnityObjectTypeIndexToManagedBaseTypeIndex.TryGetValue(typeIndex, out var managedBaseTypeIndex)
                        && managedBaseTypeIndex >= 0)
                    {
                        // If there is a managed base type, also check the filter against that type name
                        managedBaseTypeName = snapshot.TypeDescriptions.TypeDescriptionName[managedBaseTypeIndex];
                    }

                    using var searchFilterScopeNativeTypeName = args.SearchStringFilter.OpenScope(nativeTypeName);
                    using var searchFilterScopeManagedBaseTypeName = args.SearchStringFilter.OpenScope(managedBaseTypeName);
                    using var searchFilterScopeManagedTypeName = args.SearchStringFilter.OpenScope(managedTypeName);
                    using var searchFilterScopeNativeObjectName = args.SearchStringFilter.OpenScope(nativeObjectName);
                    if ((args.DisambiguateByInstanceId ?
                        !searchFilterScopeNativeObjectName.Passes(itemName)
                        : !searchFilterScopeNativeObjectName.ScopePasses))
                    {
                        continue;
                    }
                }

                // Create node for conceptual Unity Object.
                var item = new TreeViewItemData<UnityObjectsModel.ItemData>(
                    m_ItemId++,
                    new UnityObjectsModel.ItemData(
                        itemName,
                        obj.Value.Native,
                        obj.Value.Managed,
                        obj.Value.Gfx,
                        obj.Key)
                );

                // Add node to corresponding type's list of Unity Objects.
                // - disambiguatedTypeIndexToTypeObjectsMap / nonDisambiguatedObjectsOfDisambiguatedNativeTypes lists
                // for objects types defined in BuildListOfNativeUnityObjectBaseTypesToDisambiguateByManagedType
                // - type based map for the rest
                var nativeTypeSourceIndex = new SourceIndex(SourceIndex.SourceId.NativeType, typeIndex);
                if (nativeUnityObjectBaseTypesToDisambiguateByManagedType.Contains(nativeTypeSourceIndex))
                {
                    if(managedTypeIndex >= 0)
                    {
                        var managedTypeMap = disambiguatedTypeIndexToTypeObjectsMap.GetOrAdd(nativeTypeSourceIndex);
                        AddObjectToTypeMap(managedTypeMap, new SourceIndex(SourceIndex.SourceId.ManagedType, managedTypeIndex), nativeObjectName, item, args);
                    }
                    else
                    {
                        AddObjectToTypeMap(nonDisambiguatedObjectsOfDisambiguatedNativeTypes, nativeTypeSourceIndex, nativeObjectName, item, args);
                    }
                }
                else
                {
                    AddObjectToTypeMap(typeIndexToTypeObjectsMap, nativeTypeSourceIndex, nativeObjectName, item, args);
                }
            }
        }

        void AddObjectToTypeMap(
            Dictionary<SourceIndex, DictionaryOrList> typeIndexToTypeObjectsMap,
            SourceIndex typeIndex,
            string nativeObjectName,
            TreeViewItemData<UnityObjectsModel.ItemData> item,
            in BuildArgs args)
        {
            List<TreeViewItemData<UnityObjectsModel.ItemData>> listOfObjects = null;
            var typeObjects = typeIndexToTypeObjectsMap.GetOrAdd(typeIndex);
            if (args.DisambiguateByInstanceId)
            {
                typeObjects.MapOfObjects ??= new Dictionary<string, List<TreeViewItemData<UnityObjectsModel.ItemData>>>();
                listOfObjects = typeObjects.MapOfObjects.GetOrAdd(nativeObjectName);
                typeObjects.Reason = DictionaryOrList.SplitReason.InstanceIDs;
            }
            else
            {
                typeObjects.ListOfObjects ??= new List<TreeViewItemData<UnityObjectsModel.ItemData>>();
                listOfObjects = typeObjects.ListOfObjects;
            }
            listOfObjects.Add(item);
        }

        // Filter the map for potential duplicates. These are objects with the same type, name, and size. Group duplicates under a single item.
        Dictionary<SourceIndex, DictionaryOrList> FilterTypeIndexToTypeObjectsMapForPotentialDuplicatesAndGroup(
            Dictionary<SourceIndex, DictionaryOrList> typeIndexToTypeObjectsMap)
        {
            var filteredTypeIndexToTypeObjectsMap = new Dictionary<SourceIndex, DictionaryOrList>();

            foreach (var typeIndexToTypeObjectsKvp in typeIndexToTypeObjectsMap)
            {
                var potentialDuplicateObjectsMap = new Dictionary<Tuple<string, MemorySize>, DictionaryOrList>();
                // Break type objects into separate lists based on name & size.
                Debug.Assert(typeIndexToTypeObjectsKvp.Value.ListOfObjects != null, "Potential Duplicates filtering can't yet be used together with Instance ID disambiguation");
                var typeObjects = typeIndexToTypeObjectsKvp.Value.ListOfObjects;
                foreach (var typeObject in typeObjects)
                {
                    var data = typeObject.data;
                    var nameSizeTuple = new Tuple<string, MemorySize>(data.Name, data.TotalSize);
                    var nameSizeTypeObjects = potentialDuplicateObjectsMap.GetOrAdd(nameSizeTuple);
                    nameSizeTypeObjects.ListOfObjects ??= new List<TreeViewItemData<UnityObjectsModel.ItemData>>();
                    nameSizeTypeObjects.ListOfObjects.Add(typeObject);
                }

                // Create potential duplicate groups for lists that contain more than one item (duplicates).
                var potentialDuplicateItems = new List<TreeViewItemData<UnityObjectsModel.ItemData>>();
                var typeIndex = typeIndexToTypeObjectsKvp.Key;
                foreach (var potentialDuplicateObjectsKvp in potentialDuplicateObjectsMap)
                {
                    var potentialDuplicateObjects = potentialDuplicateObjectsKvp.Value.ListOfObjects;
                    if (potentialDuplicateObjects.Count > 1)
                    {
                        var potentialDuplicateData = potentialDuplicateObjects[0].data;

                        var duplicateCount = 0;
                        var potentialDuplicatesNativeSize = new MemorySize();
                        var potentialDuplicatesManagedSize = new MemorySize();
                        var potentialDuplicatesGpuSize = new MemorySize();
                        while (duplicateCount < potentialDuplicateObjects.Count)
                        {
                            potentialDuplicatesNativeSize += potentialDuplicateData.NativeSize;
                            potentialDuplicatesManagedSize += potentialDuplicateData.ManagedSize;
                            potentialDuplicatesGpuSize += potentialDuplicateData.GpuSize;

                            duplicateCount++;
                        }

                        var potentialDuplicateItem = new TreeViewItemData<UnityObjectsModel.ItemData>(
                            m_ItemId++,
                            new UnityObjectsModel.ItemData(
                                potentialDuplicateData.Name,
                                potentialDuplicatesNativeSize,
                                potentialDuplicatesManagedSize,
                                potentialDuplicatesGpuSize,
                                potentialDuplicateData.Source,
                                potentialDuplicateObjects.Count),
                            potentialDuplicateObjects);
                        potentialDuplicateItems.Add(potentialDuplicateItem);
                    }
                }

                // Add list containing duplicate type objects to corresponding type index in filtered map.
                if (potentialDuplicateItems.Count > 0)
                    filteredTypeIndexToTypeObjectsMap.Add(typeIndex, new DictionaryOrList(){ ListOfObjects = potentialDuplicateItems });
            }

            return filteredTypeIndexToTypeObjectsMap;
        }

        public readonly struct BuildArgs
        {
            public BuildArgs(
                IScopedFilter<string> searchStringFilter = null,
                ITextFilter unityObjectNameFilter = null,
                ITextFilter unityObjectTypeNameFilter = null,
                IInstancIdFilter unityObjectInstanceIDFilter = null,
                bool flattenHierarchy = false,
                bool potentialDuplicatesFilter = false,
                bool disambiguateByInstanceId = false,
                Action<int, UnityObjectsModel.ItemData> selectionProcessor = null)
            {
                SearchStringFilter = searchStringFilter;
                UnityObjectNameFilter = unityObjectNameFilter;
                UnityObjectTypeNameFilter = unityObjectTypeNameFilter;
                UnityObjectInstanceIDFilter = unityObjectInstanceIDFilter;
                FlattenHierarchy = flattenHierarchy;
                PotentialDuplicatesFilter = potentialDuplicatesFilter;
                DisambiguateByInstanceId = disambiguateByInstanceId;
                SelectionProcessor = selectionProcessor;
            }

            // Only include tree paths that match this filter.
            public IScopedFilter<string> SearchStringFilter { get; }

            // Only include Unity Objects with this name.
            public ITextFilter UnityObjectNameFilter { get; }

            // Only include Unity Objects with this type name.
            public ITextFilter UnityObjectTypeNameFilter { get; }

            // Only include the Unity Object with this instance ID. Null means do not filter by instance id. CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone (0) can be used to filter everything (used for comparison).
            public IInstancIdFilter UnityObjectInstanceIDFilter { get; }

            // Flatten the hierarchy to a single level, removing all groups; transforms the tree into a list of its leaf nodes.
            public bool FlattenHierarchy { get; }

            // Only include Unity Objects that have multiple instances of the same type, the same name, and the same size. Groups these by their 'potential duplicate' name.
            public bool PotentialDuplicatesFilter { get; }

            /// <summary>
            /// Use Instance IDs to differentiate objects with the same name, only used in comparisons
            /// </summary>
            public bool DisambiguateByInstanceId { get; }

            // Selection processor for a Unity Object item. Argument is item data record from the model for the object.
            public Action<int, UnityObjectsModel.ItemData> SelectionProcessor { get; }
        }

        public class MapOfManagedTypeOrObjectName2Objects
        {
            public Dictionary<string, Dictionary<string, DictionaryOrList>> MapOfManagedTypeNames;
            public Dictionary<string, DictionaryOrList> MapOfNativeNames;
        }
        public class DictionaryOrList
        {
            public SplitReason Reason;
            public enum SplitReason
            {
                None,
                InstanceIDs,
            }
            public Dictionary<string, List<TreeViewItemData<UnityObjectsModel.ItemData>>> MapOfObjects;
            public List<TreeViewItemData<UnityObjectsModel.ItemData>> ListOfObjects;
        }
    }
}
#endif

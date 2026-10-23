using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.Extensions;
using UnityEditor.MemoryProfiler;
using UnityEngine;
using UnityEngine.UIElements;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;
using static Unity.MemoryProfiler.Editor.UI.TreeModelHelpers;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Builds a UnityObjectsModel.
    class UnityObjectsModelBuilder
    {
        /// <summary>Rows materialized into the tree view at once in the flattened, paginated view.</summary>
        const int k_FlattenedPageSize = 2500;

        protected int m_ItemId;
        readonly int m_FlattenedPageSize;

        public UnityObjectsModelBuilder()
            : this(k_FlattenedPageSize)
        {
        }

        /// <summary>Test seam: build the flattened model with a small page size so paging can be exercised on small fixtures.</summary>
        internal UnityObjectsModelBuilder(int flattenedPageSize)
        {
            m_ItemId = (int)IAnalysisViewSelectable.Category.FirstDynamicId;
            m_FlattenedPageSize = flattenedPageSize;
        }

        Dictionary<int, IndexableTreeViewListAndIndex<UnityObjectsModel.ItemData>> m_IdToIndex;

        public void ClearIdToIndexCache()
        {
            TreeModelHelpers.ClearIdToIndexCache(ref m_IdToIndex);
        }

        public UnityObjectsModel Build(UnityObjectsModel baseModel, IEnumerable<int> treeNodeIdFilter = null)
        {
            var tree = new List<TreeViewItemData<UnityObjectsModel.ItemData>>();

            var totalMemory = new MemorySize();

            if (treeNodeIdFilter != null)
            {
                var stack = new Stack<TreeViewItemData<UnityObjectsModel.ItemData>>();
                foreach (var id in treeNodeIdFilter)
                {
                    // the only place where the base model has to be assumed as non-null.
                    var item = baseModel.RootNodes.GetItemById(ref m_IdToIndex, id);
                    stack.Push(item);
                }
                while (stack.Count > 0)
                {
                    var treeNode = stack.Pop();
                    // only add leaf nodes, add children to the stack to be processed
                    if (treeNode.hasChildren)
                    {
                        foreach (var child in treeNode.children)
                        {
                            stack.Push(child);
                        }
                    }
                    else
                    {
                        tree.Add(new TreeViewItemData<UnityObjectsModel.ItemData>(treeNode.id, treeNode.data));
                        totalMemory += treeNode.data.TotalSize;
                    }
                }
            }
            // the base model could be null, but in an empty model, there are no selections to be made anyways.
            var model = CreateDerivedModel(tree, totalMemory, baseModel);
            return model;
        }

        /// <summary>
        /// Creates a derived model, based on a provided <paramref name="baseModel"/> and some <paramref name="rootNodes"/>.
        /// The <paramref name="baseModel"/> may be null if <paramref name="rootNodes"/> is an empty list.
        /// </summary>
        /// <param name="rootNodes"></param>
        /// <param name="totalMemory"></param>
        /// <param name="baseModel"></param>
        /// <returns></returns>
        protected UnityObjectsModel CreateDerivedModel(List<TreeViewItemData<UnityObjectsModel.ItemData>> rootNodes, MemorySize totalMemory, UnityObjectsModel baseModel)
        {
            return new UnityObjectsModel(rootNodes, totalMemory);
        }

        static ObjectStatusWarningInfo AggregateWarningInfo(List<TreeViewItemData<UnityObjectsModel.ItemData>> items)
        {
            var result = default(ObjectStatusWarningInfo);
            for (int i = 0; i < items.Count; i++)
                result |= items[i].data.WarningInfo;
            return result;
        }

        public UnityObjectsModel Build(CachedSnapshot snapshot, in BuildArgs args)
        {
            if (!CanBuildBreakdownForSnapshot(snapshot))
                throw new UnsupportedSnapshotVersionException(snapshot);

            var nativeUnityObjectBaseTypesToDisambiguateByManagedType = BuildListOfNativeUnityObjectBaseTypesToDisambiguateByManagedType(snapshot);

            if (!args.FlattenHierarchy)
            {
                var rootNodes = BuildUnityObjectsGroupedByType(snapshot, nativeUnityObjectBaseTypesToDisambiguateByManagedType, args, out var treeTotalMemoryInSnapshot);
                return new UnityObjectsModel(rootNodes, treeTotalMemoryInSnapshot);
            }

            // Flat mode: build the type maps, apply filters, then emit leaf items directly into the
            // native store without constructing the intermediate group-header TreeViewItemData nodes.
            BuildUnityObjectTypeIndexToUnityObjectsMapForSnapshot(
                snapshot, args, nativeUnityObjectBaseTypesToDisambiguateByManagedType,
                out var typeIndexToTypeObjectsMap,
                out var disambiguatedTypeIndexToTypeObjectsMap,
                out var nonDisambiguatedObjectsOfDisambiguatedNativeTypes,
                out var totalMemoryInSnapshot);

            if (args.PotentialDuplicatesFilter)
            {
                typeIndexToTypeObjectsMap = FilterTypeIndexToTypeObjectsMapForPotentialDuplicatesAndGroup(typeIndexToTypeObjectsMap);
                foreach (var nativeType in nativeUnityObjectBaseTypesToDisambiguateByManagedType)
                {
                    if (disambiguatedTypeIndexToTypeObjectsMap.ContainsKey(nativeType))
                        disambiguatedTypeIndexToTypeObjectsMap[nativeType] = FilterTypeIndexToTypeObjectsMapForPotentialDuplicatesAndGroup(disambiguatedTypeIndexToTypeObjectsMap[nativeType]);
                }
                nonDisambiguatedObjectsOfDisambiguatedNativeTypes = FilterTypeIndexToTypeObjectsMapForPotentialDuplicatesAndGroup(nonDisambiguatedObjectsOfDisambiguatedNativeTypes);
            }

            var store = new FlattenedRowStore<UnityObjectsCompactRow>();
            try
            {
                var names = new List<string>();
                var totalMemorySize = new MemorySize();
                var totalNativeSize = new MemorySize();
                var totalManagedSize = new MemorySize();
                var totalGpuSize = new MemorySize();
                CollectTypeMapLeafNodesToStore(typeIndexToTypeObjectsMap, store, names, ref totalMemorySize, ref totalNativeSize, ref totalManagedSize, ref totalGpuSize);
                foreach (var innerMap in disambiguatedTypeIndexToTypeObjectsMap.Values)
                    CollectTypeMapLeafNodesToStore(innerMap, store, names, ref totalMemorySize, ref totalNativeSize, ref totalManagedSize, ref totalGpuSize);
                CollectTypeMapLeafNodesToStore(nonDisambiguatedObjectsOfDisambiguatedNativeTypes, store, names, ref totalMemorySize, ref totalNativeSize, ref totalManagedSize, ref totalGpuSize);
                return new UnityObjectsModel(store, names, m_FlattenedPageSize, totalMemorySize, totalNativeSize, totalManagedSize, totalGpuSize, totalMemoryInSnapshot);
            }
            catch
            {
                store.Dispose();
                throw;
            }
        }

        // Iterates a type map and emits each leaf object into the store. Items in the map are either
        // individual leaf objects (no children) or potential-duplicate group nodes whose children are
        // the individual objects; both cases are handled by EmitItemToStore.
        static void CollectTypeMapLeafNodesToStore(
            Dictionary<SourceIndex, DictionaryOrList> map,
            FlattenedRowStore<UnityObjectsCompactRow> store,
            List<string> names,
            ref MemorySize totalMemorySize,
            ref MemorySize totalNativeSize,
            ref MemorySize totalManagedSize,
            ref MemorySize totalGpuSize)
        {
            foreach (var typeObjects in map.Values)
            {
                if (typeObjects.ListOfObjects != null)
                {
                    foreach (var item in typeObjects.ListOfObjects)
                        EmitItemToStore(item, store, names, ref totalMemorySize, ref totalNativeSize, ref totalManagedSize, ref totalGpuSize);
                }
                else if (typeObjects.MapOfObjects != null)
                {
                    foreach (var objList in typeObjects.MapOfObjects.Values)
                    {
                        foreach (var item in objList)
                            EmitItemToStore(item, store, names, ref totalMemorySize, ref totalNativeSize, ref totalManagedSize, ref totalGpuSize);
                    }
                }
            }
        }

        // Emits a leaf item into the store. If the item is a group node (potential-duplicate group),
        // emits its children instead (they are the actual individual objects, one level deep at most).
        static void EmitItemToStore(
            TreeViewItemData<UnityObjectsModel.ItemData> item,
            FlattenedRowStore<UnityObjectsCompactRow> store,
            List<string> names,
            ref MemorySize totalMemorySize,
            ref MemorySize totalNativeSize,
            ref MemorySize totalManagedSize,
            ref MemorySize totalGpuSize)
        {
            if (item.hasChildren)
            {
                foreach (var child in item.children)
                {
                    names.Add(child.data.Name);
                    store.Add(new UnityObjectsCompactRow(child.id, child.data, names.Count - 1));
                    totalMemorySize += child.data.TotalSize;
                    totalNativeSize += child.data.NativeSize;
                    totalManagedSize += child.data.ManagedSize;
                    totalGpuSize += child.data.GpuSize;
                }
            }
            else
            {
                names.Add(item.data.Name);
                store.Add(new UnityObjectsCompactRow(item.id, item.data, names.Count - 1));
                totalMemorySize += item.data.TotalSize;
                totalNativeSize += item.data.NativeSize;
                totalManagedSize += item.data.ManagedSize;
                totalGpuSize += item.data.GpuSize;
            }
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
            if (dictionaryOrList.ListOfObjects != null)
            {
                var typeObjects = dictionaryOrList.ListOfObjects;
                return CreateGroupNode(typeSource.GetName(snapshot), typeSource, dictionaryOrList.ListOfObjects);
            }
            else
            {
                var typeGroupList = new List<TreeViewItemData<UnityObjectsModel.ItemData>>();
                foreach (var listOfObjectsById in dictionaryOrList.MapOfObjects)
                {
                    var representativeSource = listOfObjectsById.Value.Count > 0
                        ? listOfObjectsById.Value[0].data.Source
                        : new SourceIndex();
                    typeGroupList.Add(CreateGroupNode(listOfObjectsById.Key, representativeSource, listOfObjectsById.Value));
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
                    items.Count)
                { WarningInfo = AggregateWarningInfo(items) },
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

            public override string ToString()
            {
                return $"(Native: {Native}, Managed: {Managed}, Graphics: {Gfx})";
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

            var processedNativeRoots = snapshot.ProcessedNativeRoots;
            for (long i = 0; i < processedNativeRoots.Count; i++)
            {
                var itemIndex = processedNativeRoots.Data[i].NativeObjectOrRootIndex;
                switch (itemIndex.Id)
                {
                    case SourceIndex.SourceId.NativeObject:
                        var size = processedNativeRoots.Data[i].AccumulatedRootSizes;
                        AccumulateValue(nativeObject2Size, itemIndex, size.NativeSize, size.ManagedSize, size.GfxSize);
                        break;
                    default:
                        break;
                }
            }
            var totalMemoryInSnapshot = processedNativeRoots.TotalMemoryInSnapshot;

            // [Legacy] If we don't have SystemMemoryRegionsInfo, take the total value from legacy memory stats
            // Nb! If you change this, change similar code in AllTrackedMemoryModelBuilder / UnityObjectsModelBuilder / AllMemorySummaryModelBuilder
            var memoryStats = snapshot.MetaData.TargetMemoryStats;
            if (memoryStats.HasValue && !snapshot.HasSystemMemoryRegionsInfo && (memoryStats.Value.TotalVirtualMemory > 0))
                totalMemoryInSnapshot = new MemorySize(memoryStats.Value.TotalVirtualMemory, 0);

            _totalMemoryInSnapshot = totalMemoryInSnapshot;

            return nativeObject2Size;
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

            var nativeObject2Size = BuildNativeObjectIndexToSize(snapshot, out totalMemoryInSnapshot);

            // Group objects by type
            var nativeTypes = snapshot.NativeTypes;
            var nativeObjects = snapshot.NativeObjects;
            foreach (var obj in nativeObject2Size)
            {
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
                    if (managedTypeIndex >= 0)
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
                if (string.IsNullOrEmpty(nativeObjectName))
                    nativeObjectName = CachedSnapshot.InvalidItemName;
                if (args.UnityObjectNameFilter != null)
                {
                    if (!args.UnityObjectNameFilter.Passes(nativeObjectName))
                        continue;
                }

                var itemName = args.DisambiguateByInstanceId ?
                    NativeObjectTools.ProduceNativeObjectNameAndId(obj.Key.Index, snapshot) : nativeObjectName;

                // Grab precomputed status before search filter so it can be searched
                var status = snapshot.ObjectStatusComputation.GetStatus(obj.Key);

                if (args.SearchFilter != null)
                {
                    // Check if Native Type Name still needs initializing
                    if (args.UnityObjectTypeNameFilter == null)
                        nativeTypeName = nativeTypes.TypeName[typeIndex];

                    string managedBaseTypeName = null;
                    var managedBaseTypeIndex = snapshot.TypeDescriptions.UnifiedTypeInfoNative[typeIndex].ManagedBaseTypeIndexForNativeType;
                    if (managedBaseTypeIndex is not CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid)
                    {
                        // If there is a managed base type, also check the filter against that type name
                        managedBaseTypeName = snapshot.TypeDescriptions.TypeDescriptionName[managedBaseTypeIndex];
                    }

                    // Open type-name scopes for propagation, then use Passes() on the last scope for the
                    // leaf-level name+status check without pushing an extra scope onto the stack.
                    using var scopeNativeTypeName = args.SearchFilter.OpenScope(nativeTypeName);
                    using var scopeManagedBaseTypeName = args.SearchFilter.OpenScope(managedBaseTypeName);
                    using var scopeManagedTypeName = args.SearchFilter.OpenScope(managedTypeName);
                    var checkName = args.DisambiguateByInstanceId ? itemName : nativeObjectName;
                    // A Unity Object that owns graphics memory has a GraphicsBuffer behind it, so surface it
                    // to the GraphicsBuffers status filter directly (in addition to matching on its own
                    // name+status). For a pure text filter this is a no-op, since the GraphicsBuffers branch
                    // reduces to the same name check.
                    var ownsGraphics = obj.Value.Gfx.Committed > 0;
                    if (!scopeManagedTypeName.ScopePasses
                        && !scopeManagedTypeName.Passes(checkName, status)
                        && !(ownsGraphics && scopeManagedTypeName.Passes(checkName, ObjectStatus.GraphicsBuffers)))
                        continue;
                }

                // Create node for conceptual Unity Object.
                var item = new TreeViewItemData<UnityObjectsModel.ItemData>(
                    m_ItemId++,
                    new UnityObjectsModel.ItemData(
                        itemName,
                        obj.Value.Native,
                        obj.Value.Managed,
                        obj.Value.Gfx,
                        obj.Key,
                        status: status)
                );

                // Add node to corresponding type's list of Unity Objects.
                // - disambiguatedTypeIndexToTypeObjectsMap / nonDisambiguatedObjectsOfDisambiguatedNativeTypes lists
                // for objects types defined in BuildListOfNativeUnityObjectBaseTypesToDisambiguateByManagedType
                // - type based map for the rest
                var nativeTypeSourceIndex = new SourceIndex(SourceIndex.SourceId.NativeType, typeIndex);
                if (nativeUnityObjectBaseTypesToDisambiguateByManagedType.Contains(nativeTypeSourceIndex))
                {
                    if (managedTypeIndex >= 0)
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
                var potentialDuplicateObjectsMap = new Dictionary<Tuple<string, ulong>, DictionaryOrList>();
                // Break type objects into separate lists based on name & size.
                Debug.Assert(typeIndexToTypeObjectsKvp.Value.ListOfObjects != null, "Potential Duplicates filtering can't yet be used together with Instance ID disambiguation");
                var typeObjects = typeIndexToTypeObjectsKvp.Value.ListOfObjects;
                foreach (var typeObject in typeObjects)
                {
                    var data = typeObject.data;
                    // using ulong instead of MemorySize to avoid differing residency state to prevent objects from being evaluated as potential duplicates.
                    var nameSizeTuple = new Tuple<string, ulong>(data.Name, data.NativeSize.Committed + data.ManagedSize.Committed);
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
                                potentialDuplicateObjects.Count)
                            { WarningInfo = AggregateWarningInfo(potentialDuplicateObjects) },
                            potentialDuplicateObjects);
                        potentialDuplicateItems.Add(potentialDuplicateItem);
                    }
                }

                // Add list containing duplicate type objects to corresponding type index in filtered map.
                if (potentialDuplicateItems.Count > 0)
                    filteredTypeIndexToTypeObjectsMap.Add(typeIndex, new DictionaryOrList() { ListOfObjects = potentialDuplicateItems });
            }

            return filteredTypeIndexToTypeObjectsMap;
        }

        public readonly struct BuildArgs
        {
            public BuildArgs(
                CompoundSearchFilter searchFilter = null,
                ITextFilter unityObjectNameFilter = null,
                ITextFilter unityObjectTypeNameFilter = null,
                bool flattenHierarchy = false,
                bool potentialDuplicatesFilter = false,
                bool disambiguateByInstanceId = false)
            {
                SearchFilter = searchFilter;
                UnityObjectNameFilter = unityObjectNameFilter;
                UnityObjectTypeNameFilter = unityObjectTypeNameFilter;
                FlattenHierarchy = flattenHierarchy;
                PotentialDuplicatesFilter = potentialDuplicatesFilter;
                DisambiguateByInstanceId = disambiguateByInstanceId;
            }
            /// <summary>
            /// Only include tree paths that match this filter.
            /// </summary>
            public CompoundSearchFilter SearchFilter { get; }

            // Only include Unity Objects with this name.
            public ITextFilter UnityObjectNameFilter { get; }

            // Only include Unity Objects with this type name.
            public ITextFilter UnityObjectTypeNameFilter { get; }

            // Flatten the hierarchy to a single level, removing all groups; transforms the tree into a list of its leaf nodes.
            public bool FlattenHierarchy { get; }

            // Only include Unity Objects that have multiple instances of the same type, the same name, and the same size. Groups these by their 'potential duplicate' name.
            public bool PotentialDuplicatesFilter { get; }

            /// <summary>
            /// Use Instance IDs to differentiate objects with the same name, only used in comparisons
            /// </summary>
            public bool DisambiguateByInstanceId { get; }
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

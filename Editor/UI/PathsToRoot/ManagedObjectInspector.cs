using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.Database;
using Unity.MemoryProfiler.Editor.UI.PathsToRoot;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class ManagedObjectInspector : TreeView
    {
        Dictionary<ulong, int> m_IdentifyingPointerToTreeItemId = new Dictionary<ulong, int>();
        ManagedObjectInspectorItem m_Root;
        CachedSnapshot m_CachedSnapshot;
        ObjectData m_CurrentSelectionObjectData;
        DetailFormatter m_Formatter;
        int m_InspectorID = 0;
        bool truncateTypeNames = MemoryProfilerSettings.MemorySnapshotTruncateTypes;

        Queue<ReferencePendingProcessing> m_ReferencesPendingProcessing = new Queue<ReferencePendingProcessing>();
        Dictionary<int, ReferencePendingProcessing> m_ExpansionStopGapsByTreeViewItemId = new Dictionary<int, ReferencePendingProcessing>();

        const int k_MaxDepthIncrement = 3;
        const int k_MaxArrayIncrement = 20;

        public static bool HidePointers { get; private set; } = true;

        internal struct ReferencePendingProcessing
        {
            public ManagedObjectInspectorItem Root;
            public ObjectData ObjectData;
            public int ArrayIndexToContinueAt;
        }

        enum DetailsPanelColumns
        {
            Name,
            Value,
            Type,
            Size, // Field and referenced?
            Notes,
        }

        public ManagedObjectInspector(IUIStateHolder uiStateHolder, int managedObjectInspectorID, TreeViewState state, MultiColumnHeaderWithTruncateTypeName multiColumnHeader)
            : base(state, multiColumnHeader)
        {
            m_InspectorID = managedObjectInspectorID;
            m_Root = new ManagedObjectInspectorItem(m_InspectorID);
            m_Root.children = new List<TreeViewItem>();
            useScrollView = false;
            columnIndexForTreeFoldouts = 0;
            showAlternatingRowBackgrounds = true;
            showBorder = true;
            rowHeight = 20f;
            MemoryProfilerSettings.TruncateStateChanged += OnTruncateStateChanged;
            Reload();

            multiColumnHeader.TruncationChangedViaThisHeader += (truncate) =>
                MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInSelectionDetailsPanel, MemoryProfilerAnalytics.SelectionDetailsPanelInteractionType>(
                    truncate ? MemoryProfilerAnalytics.SelectionDetailsPanelInteractionType.ManagedObjectTypeNameTruncationWasEnabled : MemoryProfilerAnalytics.SelectionDetailsPanelInteractionType.ManagedObjectTypeNameTruncationWasDisabled);

            multiColumnHeader.sortingChanged += OnSortingChanged;
        }

        void OnTruncateStateChanged()
        {
            truncateTypeNames = MemoryProfilerSettings.MemorySnapshotTruncateTypes;
        }

        protected override TreeViewItem BuildRoot()
        {
            SetupDepthsFromParentsAndChildren(m_Root);
            return m_Root;
        }

        public void SetupManagedObject(CachedSnapshot snapshot, ObjectData managedObjectData)
        {
            Clear();

            m_CachedSnapshot = snapshot;
            if (m_CachedSnapshot != null)
                m_Formatter = new DetailFormatter(m_CachedSnapshot);

            m_CurrentSelectionObjectData = managedObjectData;

            if (m_CurrentSelectionObjectData.isManaged)
            {
                m_ReferencesPendingProcessing.Enqueue(new ReferencePendingProcessing { ObjectData = m_CurrentSelectionObjectData, Root = m_Root });

                ProcessQueue(k_MaxDepthIncrement);
            }

            Reload();
        }

        void ProcessQueue(int maxDepth)
        {
            // TODO: switch to proper lazy initialization of the tree past a certain depth
            while (m_ReferencesPendingProcessing.Count > 0)
            {
                var currentItem = m_ReferencesPendingProcessing.Dequeue();

                if (currentItem.Root.children == null)
                    currentItem.Root.children = new List<TreeViewItem>();
                if (currentItem.Root.depth > maxDepth)
                {
                    // emergency break for product stability
                    // TODO: Lazy expand instead

                    // first, check that this wouldn't have been recursive anyways
                    var identifyingPointer = GetIdentifyingPointer(currentItem.ObjectData, m_CachedSnapshot);
                    if (identifyingPointer != 0)
                    {
                        int recursiveTreeViewId;
                        if (m_IdentifyingPointerToTreeItemId.TryGetValue(identifyingPointer, out recursiveTreeViewId))
                        {
                            currentItem.Root.MarkRecursiveOrDuplicate(recursiveTreeViewId);
                            if (currentItem.Root.IsRecursive)
                                continue;
                        }
                    }
                    // if not, add a placeholder to continue processing as needed by the user
                    var stopGapChild = new ManagedObjectInspectorItem(m_InspectorID, currentItem);
                    stopGapChild.depth = rootItem.depth + 1;
                    m_ExpansionStopGapsByTreeViewItemId.Add(stopGapChild.id, currentItem);
                    currentItem.Root.AddChild(stopGapChild);
                    continue;
                }
                if (currentItem.ObjectData.isNative)
                    AddNativeObject(currentItem.Root, currentItem.ObjectData, m_CachedSnapshot);
                else if (currentItem.ObjectData.dataType == ObjectDataType.Array || currentItem.ObjectData.dataType == ObjectDataType.ReferenceArray)
                    AddArrayElements(currentItem.Root, currentItem.ObjectData, m_CachedSnapshot, currentItem.ArrayIndexToContinueAt);
                else
                    AddFields(currentItem.Root, currentItem.ObjectData, m_CachedSnapshot);
            }
        }

        public void LinkWasClicked(int treeViewId, bool recursiveSelection = false)
        {
            ReferencePendingProcessing pendingProcessing;
            if (m_ExpansionStopGapsByTreeViewItemId.TryGetValue(treeViewId, out pendingProcessing))
            {
                m_ExpansionStopGapsByTreeViewItemId.Remove(treeViewId);
                if (pendingProcessing.ArrayIndexToContinueAt > 0)
                {
                    if (pendingProcessing.Root.children.Count > pendingProcessing.ArrayIndexToContinueAt)
                        pendingProcessing.Root.children.RemoveAt(pendingProcessing.ArrayIndexToContinueAt);
                }
                else
                {
                    pendingProcessing.Root.children.Clear();
                }
                m_ReferencesPendingProcessing.Enqueue(pendingProcessing);
                ProcessQueue(pendingProcessing.Root.depth + k_MaxDepthIncrement);
                Reload();
                MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInSelectionDetailsPanel, MemoryProfilerAnalytics.SelectionDetailsPanelInteractionType>(
                    MemoryProfilerAnalytics.SelectionDetailsPanelInteractionType.ManagedObjectShowMoreLinkWasClicked);
            }
            else
            {
                SetSelection(new List<int> { treeViewId }, TreeViewSelectionOptions.RevealAndFrame);
                if (recursiveSelection)
                    MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInSelectionDetailsPanel, MemoryProfilerAnalytics.SelectionDetailsPanelInteractionType>(
                        MemoryProfilerAnalytics.SelectionDetailsPanelInteractionType.ManagedObjectRecursiveInNotesWasClicked);
                else
                    MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInSelectionDetailsPanel, MemoryProfilerAnalytics.SelectionDetailsPanelInteractionType>(
                        MemoryProfilerAnalytics.SelectionDetailsPanelInteractionType.ManagedObjectDuplicateInNotesWasClicked);
            }
        }

        void AddFields(ManagedObjectInspectorItem root, ObjectData obj, CachedSnapshot cs)
        {
            if (!obj.IsValid)
                return;
            if (obj.dataType == ObjectDataType.ReferenceObject)
            {
                var managedObjectInfo = obj.GetManagedObject(cs);
                if (!managedObjectInfo.IsValid())
                    return;
                obj = ObjectData.FromManagedObjectIndex(cs, managedObjectInfo.ManagedObjectIndex);
                if (!obj.IsValid)
                    return;
            }
            var identifyingPointer = GetIdentifyingPointer(obj, cs);
            if (identifyingPointer != 0)
            {
                int recursiveTreeViewId;
                if (m_IdentifyingPointerToTreeItemId.TryGetValue(identifyingPointer, out recursiveTreeViewId))
                {
                    root.MarkRecursiveOrDuplicate(recursiveTreeViewId);
                    if (root.IsRecursive)
                        return;
                }
                else
                    m_IdentifyingPointerToTreeItemId.Add(identifyingPointer, root.id);
            }

            var isString = cs.TypeDescriptions.ITypeString == obj.managedTypeIndex;
            if (isString && obj.dataType != ObjectDataType.Type)
            {
                root.Value = StringTools.ReadString(obj.managedObjectData, out _, cs.VirtualMachineInformation);
                return;
            }

            var fieldList = BuildFieldList(obj);

            var isUnityObject = cs.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex.ContainsKey(obj.managedTypeIndex);

            for (int i = 0; i < fieldList.Length; i++)
            {
                var fieldByIndex = obj.GetInstanceFieldBySnapshotFieldIndex(cs, fieldList[i], false);
                var name = cs.FieldDescriptions.FieldDescriptionName[fieldList[i]];
                var v = GetValue(fieldByIndex);
                var typeIdx = cs.FieldDescriptions.TypeIndex[fieldList[i]];
                var typename = cs.TypeDescriptions.TypeDescriptionName[typeIdx];
                var isStatic = cs.FieldDescriptions.IsStatic[fieldList[i]] == 1;

                var fieldSize = GetFieldSize(cs, fieldByIndex);

                var childItem = new ManagedObjectInspectorItem(m_InspectorID, name, typename, v, isStatic, GetIdentifyingPointer(fieldByIndex, cs), fieldSize);
                childItem.depth = root.depth + 1;

                bool invalidIntPtr = false;
                const string k_NativeArrayTypePrefix = "Unity.Collections.NativeArray<";
                if (isUnityObject && fieldByIndex.fieldIndex == cs.TypeDescriptions.IFieldUnityObjectMCachedPtr)
                {
                    ulong nativeObjectPointer;
                    if (fieldByIndex.managedObjectData.TryReadPointer(out nativeObjectPointer) == BytesAndOffset.PtrReadError.Success)
                    {
                        if (nativeObjectPointer == 0)
                        {
                            childItem = new ManagedObjectInspectorItem(m_InspectorID, name, typename, v + " (Leaked Managed Shell)", isStatic, GetIdentifyingPointer(fieldByIndex, cs), fieldSize);
                        }
                        else
                        {
                            EnqueueNativeObject(childItem, nativeObjectPointer, cs);
                        }
                    }
                }
                else if (typeIdx == cs.TypeDescriptions.ITypeIntPtr)
                {
                    invalidIntPtr = ProcessIntPtr(cs, fieldByIndex, childItem, v, root);
                }
                else if (typename.StartsWith(k_NativeArrayTypePrefix))
                {
                    var countOfGenericOpen = 1;
                    var countOfGenericClose = 0;
                    unsafe
                    {
                        fixed(char* c = typename)
                        {
                            char* it = c + k_NativeArrayTypePrefix.Length;
                            for (int charPos = k_NativeArrayTypePrefix.Length; charPos < typename.Length; charPos++)
                            {
                                if (*it == '<')
                                    ++countOfGenericOpen;
                                else if (*it == '>')
                                    ++countOfGenericClose;
                                ++it;
                            }
                        }
                    }
                    if (countOfGenericOpen == countOfGenericClose && typename.EndsWith(">"))
                    {
                        // only parse types named "NativeArrays" that that end on the generic bracke they opened for the Native Array.
                        // E.g. Avoid parsing Unity.Collections.NativeArray<System.Int32>[] or a speculative Unity.Collections.NativeArray<System.Int32>.Something<T>
                        ulong pointerToMBufferData;
                        if (fieldByIndex.managedObjectData.TryReadPointer(out pointerToMBufferData) == BytesAndOffset.PtrReadError.Success)
                        {
                            FindNativeAllocationOrRegion(cs, pointerToMBufferData, childItem, v, "m_Buffer", "void*");
                        }
                    }
                }

                root.AddChild(childItem);

                if (!invalidIntPtr && (fieldByIndex.dataType == ObjectDataType.Array || fieldByIndex.dataType == ObjectDataType.ReferenceArray ||
                                       fieldByIndex.dataType == ObjectDataType.Object || fieldByIndex.dataType == ObjectDataType.ReferenceObject))
                {
                    if (v != "null")
                        m_ReferencesPendingProcessing.Enqueue(new ReferencePendingProcessing { ObjectData = fieldByIndex, Root = childItem });
                }
            }
        }

        bool ProcessIntPtr(CachedSnapshot cs, ObjectData fieldByIndex, ManagedObjectInspectorItem childItem, string value, ManagedObjectInspectorItem root)
        {
            ulong pointer;
            if (fieldByIndex.managedObjectData.TryReadPointer(out pointer) == BytesAndOffset.PtrReadError.Success)
            {
                if (pointer == 0)
                {
                    childItem.Value = "null";
                    return true;
                }
                if (pointer == ulong.MaxValue)
                {
                    childItem.Value = "invalid";
                    return true;
                }
                else
                {
                    int objectIdentifyer;
                    if (cs.CrawledData.MangedObjectIndexByAddress.TryGetValue(pointer, out objectIdentifyer))
                    {
                        m_ReferencesPendingProcessing.Enqueue(new ReferencePendingProcessing { ObjectData = ObjectData.FromManagedObjectIndex(cs, objectIdentifyer), Root = childItem });
                    }
                    else if (cs.NativeObjects.nativeObjectAddressToInstanceId.TryGetValue(pointer, out objectIdentifyer))
                    {
                        EnqueueNativeObject(childItem, pointer, cs);
                    }
                    else
                    {
                        var data = cs.ManagedHeapSections.Find(pointer, cs.VirtualMachineInformation);
                        if (data.IsValid)
                        {
                            bool wasAlreadyCrawled;
                            var moi = Crawler.ParseObjectHeader(cs, new Crawler.StackCrawlData() { ptr = pointer }, out wasAlreadyCrawled, true, data);
                            if (moi.IsValid())
                            {
                                m_ReferencesPendingProcessing.Enqueue(new ReferencePendingProcessing { ObjectData = ObjectData.FromManagedObjectInfo(cs, moi), Root = childItem });
#if DEBUG_VALIDATION
                                Debug.LogError("Managed Object Inspector found a possible Managed Object that the crawler missed!");
#endif
                            }
                            else
                            {
                                int iHeapSection = -1;
                                for (int iManageHeapSection = 0; iManageHeapSection < cs.SortedManagedHeapEntries.Count; iManageHeapSection++)
                                {
                                    if (cs.SortedManagedHeapEntries.Address(iManageHeapSection) + cs.SortedManagedHeapEntries.Size(iManageHeapSection) < pointer)
                                        continue;
                                    if (cs.SortedManagedHeapEntries.Address(iManageHeapSection) < pointer)
                                    {
                                        iHeapSection = iManageHeapSection;
                                    }
                                    break;
                                }

                                var bytes = "";
                                var maxBytesAvailable = data.bytes.Length - data.offset;
                                for (int b = 0; b < maxBytesAvailable && b < 20; b++)
                                {
                                    bytes += data.bytes[data.offset + b].ToString("X") + " ";
                                }
                                // lets get some debug info
                                if (cs.SortedManagedHeapEntries.SectionType(iHeapSection) == CachedSnapshot.MemorySectionType.GarbageCollector)
                                    // some unsafe pointer outside of a fixed? Sounds Dangerous
                                    childItem.Value = "(IntPtr) -> Managed Heap @" + value + " Data: " + bytes;
                                else
                                    // likely pointing at some Mono Object, e.g. vtable or the like
                                    childItem.Value = "(IntPtr) -> Virtual Machine @" + value + " Data: " + bytes;
                            }
                        }
                        else
                        {
                            FindNativeAllocationOrRegion(cs, pointer, childItem, value);
                        }
                    }
                }
            }
            return false;
        }

        void FindNativeAllocationOrRegion(CachedSnapshot cs, ulong pointer, ManagedObjectInspectorItem childItem, string value, string nativeFieldName = "(IntPtr)", string nativeAllocationTypeName = "Native Allocation")
        {
            if (pointer == 0)
            {
                childItem.Value = "Null / Uninitialized";
                return;
            }
            else if (pointer == ulong.MaxValue)
            {
                childItem.Value = "Invalid";
                return;
            }
            int nativeRegion = -1;
            string nativeRegionPath = null;
            bool buildFullNativePath = false;
            for (int iRegion = 0; iRegion < cs.SortedNativeRegionsEntries.Count; iRegion++)
            {
                if (cs.SortedNativeRegionsEntries.Address(iRegion) + cs.SortedNativeRegionsEntries.Size(iRegion) < pointer)
                    continue;
                if (cs.SortedNativeRegionsEntries.Address(iRegion) <= pointer)
                {
                    // found a region, continue searching though, there could be a sub-region
                    nativeRegion = iRegion;
                    if (nativeRegionPath == null || !buildFullNativePath)
                        nativeRegionPath = cs.SortedNativeRegionsEntries.Name(iRegion);
                    else
                        nativeRegionPath += " / " + cs.SortedNativeRegionsEntries.Name(iRegion);
                }
                if (cs.SortedNativeRegionsEntries.Address(iRegion) + cs.SortedNativeRegionsEntries.Size(iRegion) > pointer)
                    break;
            }
            if (nativeRegion >= 0)
            {
                int nativeAllocation = -1;
                for (int iAlloc = 0; iAlloc < cs.SortedNativeAllocations.Count; iAlloc++)
                {
                    if (cs.SortedNativeAllocations.Address(iAlloc) + cs.SortedNativeAllocations.Size(iAlloc) < pointer)
                        continue;
                    if (cs.SortedNativeAllocations.Address(iAlloc) <= pointer)
                    {
                        // found an allocation
                        nativeAllocation = iAlloc;
                    }
                    break;
                }
                if (nativeAllocation >= 0)
                {
                    var rootReference = cs.SortedNativeAllocations.RootReferenceId(nativeAllocation);
                    var allocationName = cs.NativeRootReferences.AreaName[rootReference] + " / " + cs.NativeRootReferences.ObjectName[rootReference] + " / " + value;
                    var nativeObjectItem = new ManagedObjectInspectorItem(m_InspectorID, nativeFieldName, nativeAllocationTypeName,
                        allocationName, false, pointer, cs.SortedNativeAllocations.Size(nativeAllocation));
                    nativeObjectItem.depth = childItem.depth + 1;
                    childItem.AddChild(nativeObjectItem);
                    //Debug.LogError("Managed Object Inspector found a possible pointer to Native Allocation that the crawler missed!");
                }
                else
                {
                    var nativeObjectItem = new ManagedObjectInspectorItem(m_InspectorID, nativeFieldName, "Native Region", nativeRegionPath + " / " + value, false, pointer, 0ul);
                    nativeObjectItem.depth = childItem.depth + 1;
                    childItem.AddChild(nativeObjectItem);
                    //Debug.LogError("Managed Object Inspector found a possible pointer to Native Memory that the crawler missed!");
                }
            }
            else
            {
                // This pointer points out of range! Could be IL2CPP Virtual Machine Memory but it could just be entirely broken
                childItem.Value = nativeFieldName + " -> Not in Tracked Memory! @" + value;
            }
        }

        ulong GetFieldSize(CachedSnapshot cs, ObjectData fieldByIndex)
        {
            switch (fieldByIndex.dataType)
            {
                case ObjectDataType.Object:
                case ObjectDataType.BoxedValue:
                case ObjectDataType.Array:
                    return (ulong)fieldByIndex.GetManagedObject(cs).Size;
                case ObjectDataType.ReferenceObject:
                case ObjectDataType.ReferenceArray:
                    return (ulong)cs.VirtualMachineInformation.PointerSize;
                case ObjectDataType.Type:
                case ObjectDataType.Value:
                    return (ulong)cs.TypeDescriptions.Size[fieldByIndex.managedTypeIndex];
                case ObjectDataType.NativeObject:
                    return cs.NativeObjects.Size[fieldByIndex.nativeObjectIndex];
                default:
                    return 0;
            }
        }

        ulong GetIdentifyingPointer(ObjectData obj, CachedSnapshot cs)
        {
            var address = obj.GetObjectPointer(cs);
            if (obj.m_Parent != null && obj.m_Parent.obj.GetObjectPointer(cs) == address)
                return 0;
            return address;
        }

        void AddArrayElements(ManagedObjectInspectorItem root, ObjectData arrayObject, CachedSnapshot cs, int arrayIndexToStartAt = 0)
        {
            if (!arrayObject.IsValid)
                return;
            if (arrayObject.dataType == ObjectDataType.ReferenceArray)
            {
                var managedObjectInfo = arrayObject.GetManagedObject(cs);
                if (!managedObjectInfo.IsValid())
                    return;
                arrayObject = ObjectData.FromManagedObjectIndex(cs, managedObjectInfo.ManagedObjectIndex);
                if (!arrayObject.IsValid)
                    return;
            }
            var unifiedObjectIndex = GetIdentifyingPointer(arrayObject, cs);
            if (unifiedObjectIndex != 0)
            {
                int recursiveTreeViewId;
                if (m_IdentifyingPointerToTreeItemId.TryGetValue(unifiedObjectIndex, out recursiveTreeViewId))
                {
                    root.MarkRecursiveOrDuplicate(recursiveTreeViewId);
                    if (root.IsRecursive)
                        return;
                }
                else
                    m_IdentifyingPointerToTreeItemId.Add(unifiedObjectIndex, root.id);
            }

            var arrayInfo = arrayObject.GetArrayInfo(cs);
            int arrayElementCount = arrayInfo.length;

            for (int i = arrayIndexToStartAt; i < arrayElementCount; i++)
            {
                if (i - arrayIndexToStartAt >= k_MaxArrayIncrement)
                {
                    var continueArray = new ReferencePendingProcessing { ObjectData = arrayObject, Root = root, ArrayIndexToContinueAt = i};
                    // if not, add a placeholder to continue processing as needed by the user
                    var stopGapChild = new ManagedObjectInspectorItem(m_InspectorID, continueArray);
                    stopGapChild.depth = rootItem.depth + 1;
                    m_ExpansionStopGapsByTreeViewItemId.Add(stopGapChild.id, continueArray);
                    root.AddChild(stopGapChild);
                    return;
                }

                var fieldByIndex = arrayObject.GetArrayElement(m_CachedSnapshot, arrayInfo, i, true);
                var v = GetValue(fieldByIndex);
                var typename = cs.TypeDescriptions.TypeDescriptionName[arrayInfo.elementTypeDescription];
                string name = null;
                if (arrayObject.IsField())
                {
                    name = arrayObject.GetFieldName(cs);
                }
                else
                {
                    var parentIsJaggedArrayContainer = !string.IsNullOrEmpty(root.DisplayName) && root.DisplayName.Contains("[]");
                    name = parentIsJaggedArrayContainer ? root.DisplayName : typename;
                    var leadingSpace = parentIsJaggedArrayContainer ? string.Empty : " [";
                    var followingSpace = parentIsJaggedArrayContainer ? string.Empty : "]";
                    var indexOfFirstEmptyArrayBrackets = name.IndexOf("[]");
                    if (indexOfFirstEmptyArrayBrackets >= 0)
                        name = name.Insert(indexOfFirstEmptyArrayBrackets + (parentIsJaggedArrayContainer ? 1 : 0), $"{leadingSpace}{arrayInfo.IndexToRankedString(i)}{followingSpace}");
                    else
                        name += $"{leadingSpace}{arrayInfo.IndexToRankedString(i)}{followingSpace}";
                }
                var fieldSize = GetFieldSize(cs, fieldByIndex);

                var childItem = new ManagedObjectInspectorItem(m_InspectorID, name, typename, v, false, GetIdentifyingPointer(fieldByIndex, cs), fieldSize);
                childItem.depth = root.depth + 1;
                root.AddChild(childItem);

                if (fieldByIndex.dataType == ObjectDataType.Array || fieldByIndex.dataType == ObjectDataType.ReferenceArray ||
                    fieldByIndex.dataType == ObjectDataType.Object || fieldByIndex.dataType == ObjectDataType.ReferenceObject)
                {
                    m_ReferencesPendingProcessing.Enqueue(new ReferencePendingProcessing { ObjectData = fieldByIndex, Root = childItem });
                }
                else if (fieldByIndex.dataType == ObjectDataType.Value && cs.TypeDescriptions.TypeDescriptionName[arrayInfo.elementTypeDescription].StartsWith("Unity.Collections.NativeArray<"))
                {
                    ulong pointerToMBufferData;
                    if (fieldByIndex.managedObjectData.TryReadPointer(out pointerToMBufferData) == BytesAndOffset.PtrReadError.Success)
                    {
                        FindNativeAllocationOrRegion(cs, pointerToMBufferData, childItem, v, "m_Buffer", "void*");
                    }
                }
                else if (arrayInfo.elementTypeDescription == cs.TypeDescriptions.ITypeIntPtr)
                {
                    ProcessIntPtr(cs, fieldByIndex, childItem, v, root);
                }
            }
        }

        void EnqueueNativeObject(ManagedObjectInspectorItem root, ulong address, CachedSnapshot cs)
        {
            if (address == 0)
                return;
            var instanceId = cs.NativeObjects.nativeObjectAddressToInstanceId[address];
            if (instanceId == 0)
                return;
            var index = cs.NativeObjects.instanceId2Index[instanceId];
            var nativeObjectData = ObjectData.FromNativeObjectIndex(cs, index);
            if (!nativeObjectData.IsValid)
                return;
            m_ReferencesPendingProcessing.Enqueue(new ReferencePendingProcessing { ObjectData = nativeObjectData, Root = root });
        }

        void AddNativeObject(ManagedObjectInspectorItem root, ObjectData nativeObjectData, CachedSnapshot cs)
        {
            var identifyingPointer = nativeObjectData.GetObjectPointer(cs);
            var nativeObjectItem = new ManagedObjectInspectorItem(m_InspectorID, "Native Reference", nativeObjectData.GenerateTypeName(cs),
                cs.NativeObjects.ObjectName[nativeObjectData.nativeObjectIndex], false, identifyingPointer, cs.NativeObjects.Size[nativeObjectData.nativeObjectIndex]);
            nativeObjectItem.depth = root.depth + 1;
            root.AddChild(nativeObjectItem);

            root = nativeObjectItem;

            if (identifyingPointer != 0)
            {
                int recursiveTreeViewId;
                if (m_IdentifyingPointerToTreeItemId.TryGetValue(identifyingPointer, out recursiveTreeViewId))
                {
                    root.MarkRecursiveOrDuplicate(recursiveTreeViewId);
                    if (root.IsRecursive)
                        return;
                }
                else
                    m_IdentifyingPointerToTreeItemId.Add(identifyingPointer, root.id);
            }

            var referencedObjects = nativeObjectData.GetAllReferencedObjects(cs);

            int referencedObjectCount = referencedObjects.Length;

            for (int i = 0; i < referencedObjectCount; i++)
            {
                var referencedObject = referencedObjects[i];
                if (referencedObject.isNative)
                {
                    // For now, don't go recursive on Native Object <-> Native Object
                    //if (referencedObject.nativeObjectIndex >= 0)
                    //    m_ReferencesPendingProcessing.Enqueue(new ReferencePendingProcessing { objectData = referencedObject, root = root });
                    continue;
                }
                var v = GetValue(referencedObject);
                var name = "Native Reference";
                var typename = referencedObject.GenerateTypeName(cs);
                var childItem = new ManagedObjectInspectorItem(m_InspectorID, name, typename, v, false, referencedObject.GetObjectPointer(cs), 0ul);
                childItem.depth = root.depth + 1;
                root.AddChild(childItem);

                if (referencedObject.dataType == ObjectDataType.Array || referencedObject.dataType == ObjectDataType.ReferenceArray ||
                    referencedObject.dataType == ObjectDataType.Object || referencedObject.dataType == ObjectDataType.ReferenceObject)
                {
                    m_ReferencesPendingProcessing.Enqueue(new ReferencePendingProcessing { ObjectData = referencedObject, Root = childItem });
                }
            }
        }

        string GetValue(ObjectData od)
        {
            if (!od.IsValid)
                return "failed";
            switch (od.dataType)
            {
                case ObjectDataType.BoxedValue:
                    return m_Formatter.FormatValueType(od.GetBoxedValue(m_CachedSnapshot, true), false);
                case ObjectDataType.Value:
                    return m_Formatter.FormatValueType(od, false);
                case ObjectDataType.Object:
                    return m_Formatter.FormatObject(od, false);
                case ObjectDataType.Array:
                    return m_Formatter.FormatArray(od);
                case ObjectDataType.ReferenceObject:
                {
                    ulong ptr = od.GetReferencePointer();
                    if (ptr == 0)
                    {
                        return "null";
                    }
                    else
                    {
                        var o = ObjectData.FromManagedPointer(m_CachedSnapshot, ptr);
                        if (!o.IsValid)
                            return "failed to read object";
                        return m_Formatter.FormatObject(o, false);
                    }
                }
                case ObjectDataType.ReferenceArray:
                {
                    ulong ptr = od.GetReferencePointer();
                    if (ptr == 0)
                    {
                        return "null";
                    }
                    var arr = ObjectData.FromManagedPointer(m_CachedSnapshot, ptr);
                    if (!arr.IsValid)
                        return "failed to read pointer";
                    return m_Formatter.FormatArray(arr);
                }
                case ObjectDataType.Type:
                    return m_CachedSnapshot.TypeDescriptions.TypeDescriptionName[od.managedTypeIndex];
                case ObjectDataType.NativeObject:
                    return m_Formatter.FormatPointer(m_CachedSnapshot.NativeObjects.NativeObjectAddress[od.nativeObjectIndex]);
                default:
                    return "<uninitialized type>";
            }
        }

        private int[] BuildFieldList(ObjectData obj)
        {
            int[] fl;
            List<int> fields = new List<int>();
            switch (obj.dataType)
            {
                case ObjectDataType.Type:
                    //take all static field
                    fl = m_CachedSnapshot.TypeDescriptions.fieldIndicesStatic[obj.managedTypeIndex];
                    return fl;
                case ObjectDataType.BoxedValue:
                case ObjectDataType.Object:
                case ObjectDataType.Value:
                case ObjectDataType.ReferenceObject:
                    fields.AddRange(m_CachedSnapshot.TypeDescriptions.fieldIndicesStatic[obj.managedTypeIndex]);
                    fields.AddRange(m_CachedSnapshot.TypeDescriptions.FieldIndicesInstance[obj.managedTypeIndex]);
                    break;
            }
            fl = fields.ToArray();
            return fl;
        }

        public void DoGUI(Rect rect)
        {
            //GUI.Label(new Rect(rect.x, rect.y, rect.width, 30), "Object Details");
            //rect.height -= 30;
            //rect.y += 30;
            OnGUI(rect);
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var item = args.item;
            for (int i = 0; i < args.GetNumVisibleColumns(); ++i)
            {
                CellGUI(args.GetCellRect(i), item, (DetailsPanelColumns)args.GetColumn(i), ref args);
            }
        }

        void CellGUI(Rect cellRect, TreeViewItem item, DetailsPanelColumns column, ref RowGUIArgs args)
        {
            // Center cell rect vertically (makes it easier to place controls, icons etc in the cells)
            CenterRectUsingSingleLineHeight(ref cellRect);
            var detailsPanelItem = (ManagedObjectInspectorItem)item;

            switch (column)
            {
                case DetailsPanelColumns.Name:
                    var indent = GetContentIndent(item);
                    cellRect.x += indent;
                    cellRect.width -= indent;
                    if (detailsPanelItem.PendingProcessing)
                    {
                        if (EditorGUICompatibilityHelper.DrawLinkLabel(detailsPanelItem.DisplayName, cellRect))
                            LinkWasClicked(detailsPanelItem.id);
                    }
                    else
                        GUI.Label(cellRect, detailsPanelItem.DisplayName);
                    break;
                case DetailsPanelColumns.Value:
                    GUI.Label(cellRect, detailsPanelItem.Value);
                    break;
                case DetailsPanelColumns.Type:
                    GUI.Label(cellRect, truncateTypeNames ? PathsToRootDetailView.TruncateTypeName(detailsPanelItem.TypeName) : detailsPanelItem.TypeName);
                    break;
                case DetailsPanelColumns.Size:
                    GUI.Label(cellRect, detailsPanelItem.Size);
                    break;
                case DetailsPanelColumns.Notes:
                    if (detailsPanelItem.IsDuplicate)
                    {
                        if (EditorGUICompatibilityHelper.DrawLinkLabel(detailsPanelItem.Notes, cellRect))
                            LinkWasClicked(detailsPanelItem.ExistingItemId, detailsPanelItem.IsRecursive);
                    }
                    else
                        GUI.Label(cellRect, detailsPanelItem.Notes);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(column), column, null);
            }
        }

        public void OnDisable()
        {
            MemoryProfilerSettings.TruncateStateChanged -= OnTruncateStateChanged;
        }

        public void Clear()
        {
            m_IdentifyingPointerToTreeItemId.Clear();
            m_Root.children = new List<TreeViewItem>();
            m_Root.depth = -1;
        }

        public static MultiColumnHeaderState CreateDefaultMultiColumnHeaderState()
        {
            var columns = new[]
            {
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("Name"),
                    headerTextAlignment = TextAlignment.Left,
                    sortedAscending = true,
                    canSort = false,
                    sortingArrowAlignment = TextAlignment.Right,
                    width = 200,
                    minWidth = 60,
                    autoResize = true,
                    allowToggleVisibility = false
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("Value"),
                    headerTextAlignment = TextAlignment.Left,
                    sortedAscending = true,
                    canSort = false,
                    sortingArrowAlignment = TextAlignment.Right,
                    width = 140,
                    minWidth = 40,
                    autoResize = false,
                    allowToggleVisibility = false,
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("Type"),
                    headerTextAlignment = TextAlignment.Left,
                    sortedAscending = true,
                    canSort = false,
                    sortingArrowAlignment = TextAlignment.Right,
                    width = 60,
                    minWidth = 40,
                    autoResize = false,
                    allowToggleVisibility = false,
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("Size"),
                    headerTextAlignment = TextAlignment.Left,
                    sortedAscending = true,
                    canSort = false,
                    sortingArrowAlignment = TextAlignment.Right,
                    width = 60,
                    minWidth = 40,
                    autoResize = false,
                    allowToggleVisibility = false,
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("Notes"),
                    headerTextAlignment = TextAlignment.Left,
                    sortedAscending = true,
                    canSort = false,
                    sortingArrowAlignment = TextAlignment.Right,
                    width = 60,
                    minWidth = 40,
                    autoResize = false,
                    allowToggleVisibility = false,
                }
            };
            return new MultiColumnHeaderState(columns);
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            base.SelectionChanged(selectedIds);

            MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInSelectionDetailsPanel, MemoryProfilerAnalytics.SelectionDetailsPanelInteractionType>(
                MemoryProfilerAnalytics.SelectionDetailsPanelInteractionType.SelectionInManagedObjectTableWasUsed);
        }

        protected override void ExpandedStateChanged()
        {
            base.ExpandedStateChanged();

            MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInSelectionDetailsPanel, MemoryProfilerAnalytics.SelectionDetailsPanelInteractionType>(
                MemoryProfilerAnalytics.SelectionDetailsPanelInteractionType.ManagedObjectTreeViewElementWasRevealed);
        }

        void OnSortingChanged(MultiColumnHeader multiColumnHeader)
        {
            // this table is currently unsortable. This is here in case we change our minds about that eventually.
            MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInSelectionDetailsPanel, MemoryProfilerAnalytics.SelectionDetailsPanelInteractionType>(
                MemoryProfilerAnalytics.SelectionDetailsPanelInteractionType.ManagedObjectTableSortingWasChanged);

            if (multiColumnHeader != null && multiColumnHeader.sortedColumnIndex != -1)
            {
                MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.SortedColumnEvent>();
                MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.SortedColumnEvent() {
                    viewName = "Managed Object Inspector",
                    Ascending = multiColumnHeader.IsSortedAscending(multiColumnHeader.sortedColumnIndex),
                    shown = multiColumnHeader.sortedColumnIndex,
                    fileName = multiColumnHeader.GetColumn(multiColumnHeader.sortedColumnIndex).headerContent.text
                });
            }
        }
    }
}

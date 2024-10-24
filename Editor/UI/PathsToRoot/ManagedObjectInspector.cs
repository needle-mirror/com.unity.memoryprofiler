using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.MemoryProfiler.Editor.UI.PathsToRoot;
using Unity.MemoryProfiler.Editor.UIContentData;
#if DEBUG_VALIDATION
using UnityEditor;
#endif
using Unity.MemoryProfiler.Editor.Managed;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
#if INSTANCE_ID_CHANGED
using TreeView = UnityEditor.IMGUI.Controls.TreeView<int>;
using TreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#endif

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
        const int k_MaxTotalItemProcessingIncrement = 1000;

        public static bool HidePointers { get => true; }

        internal struct ReferencePendingProcessing
        {
            public ManagedObjectInspectorItem Root;
            public ObjectData ObjectData;
            public int IndexToContinueAt;
            public ProcessableObjectType Type { get; }
            public ArrayInfo ArrayInfo;
            public int[] FieldList;

            internal enum ProcessableObjectType
            {
                NativeObject,
                ManagedObject,
                ManagedArray,
                ManagedField,
            }

            /// <summary>
            /// Use this constructor if an Object, Array or a field referencing an Object, Array or Value type should be processed.
            /// </summary>
            /// <param name="parent"></param>
            /// <param name="objectData"></param>
            public ReferencePendingProcessing(ManagedObjectInspectorItem parent, ObjectData objectData)
            {
                Root = parent;
                ObjectData = objectData;
                Type = GetObjectType(objectData.dataType);
                // Value Types should not be treated as fields when using this constructor, but as objects that need to be unrolled fully.
                if (Type == ProcessableObjectType.ManagedField)
                    Type = ProcessableObjectType.ManagedObject;
                ArrayInfo = null;
                FieldList = null;
                IndexToContinueAt = Type == ProcessableObjectType.ManagedArray ? 0 : -1;
            }

            /// <summary>
            /// Use this constructor just for iterating over the fields of one object.
            /// For fields referencing Objects, Arrays or Value types that need further processing and get added to the queue,
            /// use <see cref="ReferencePendingProcessing.ReferencePendingProcessing(ManagedObjectInspectorItem, ObjectData)"/> instead.
            /// </summary>
            /// <param name="parent"></param>
            /// <param name="objectData"></param>
            /// <param name="fieldList"></param>
            /// <param name="fieldIndex"></param>
            public ReferencePendingProcessing(ManagedObjectInspectorItem parent, ObjectData objectData, int[] fieldList, int fieldIndex)
            {
                Root = parent;
                ObjectData = objectData;
                Type = ProcessableObjectType.ManagedField;
#if DEBUG_VALIDATION
                Debug.Assert(objectData.IsField());
#endif
                ArrayInfo = null;
                FieldList = fieldList;
                IndexToContinueAt = fieldIndex;
            }

            /// <summary>
            /// Use this constructor for Array field processing.
            /// </summary>
            /// <param name="parent"></param>
            /// <param name="objectData">
            /// When used as a delayed continue entry, make sure that this is not the array element
            /// at which processing left off, but the parent array</param>
            /// <param name="arrayInfo"></param>
            /// <param name="arrayIndexToContinueAt"></param>
            public ReferencePendingProcessing(ManagedObjectInspectorItem parent, ObjectData objectData, ArrayInfo arrayInfo, int arrayIndexToContinueAt)
            {
                Root = parent;
                ObjectData = objectData;
                Type = ProcessableObjectType.ManagedField;
#if DEBUG_VALIDATION
                Debug.Assert(objectData.IsArrayItem() || objectData.dataType == ObjectDataType.Array);
#endif
                ArrayInfo = arrayInfo;
                FieldList = null;
                IndexToContinueAt = arrayIndexToContinueAt;
            }

            static ProcessableObjectType GetObjectType(ObjectDataType dataType)
            {
                switch (dataType)
                {
                    case ObjectDataType.Value:
                        return ProcessableObjectType.ManagedField;
                    case ObjectDataType.ReferenceObject:
                    case ObjectDataType.BoxedValue:
                    case ObjectDataType.Type:
                    case ObjectDataType.Object:
                        return ProcessableObjectType.ManagedObject;
                    case ObjectDataType.ReferenceArray:
                    case ObjectDataType.Array:
                        return ProcessableObjectType.ManagedArray;
                    case ObjectDataType.NativeObject:
                        return ProcessableObjectType.NativeObject;
                    default:
                        throw new NotImplementedException();
                }
            }
        }

        enum DetailsPanelColumns
        {
            Name,
            Value,
            Type,
            Size, // Field and referenced?
            Notes,
        }

        public ManagedObjectInspector(int managedObjectInspectorID, TreeViewState state, MultiColumnHeaderWithTruncateTypeName multiColumnHeader)
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
                m_ReferencesPendingProcessing.Enqueue(new ReferencePendingProcessing(m_Root, m_CurrentSelectionObjectData));

                ProcessQueue(k_MaxDepthIncrement);
            }

            Reload();
        }

        void ProcessQueue(int maxDepth)
        {
            var processed = 0;
            // TODO: switch to proper lazy initialization of the tree past a certain depth
            while (m_ReferencesPendingProcessing.Count > 0)
            {
                var currentItem = m_ReferencesPendingProcessing.Dequeue();

                if (currentItem.Root.children == null)
                    currentItem.Root.children = new List<TreeViewItem>();
                if (++processed > k_MaxTotalItemProcessingIncrement || currentItem.Root.depth > maxDepth)
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
                switch (currentItem.Type)
                {
                    case ReferencePendingProcessing.ProcessableObjectType.NativeObject:
                        AddNativeObject(currentItem.Root, currentItem.ObjectData, m_CachedSnapshot);
                        break;
                    case ReferencePendingProcessing.ProcessableObjectType.ManagedObject:
                        ProcessManagedObjectFields(currentItem, m_CachedSnapshot);
                        break;
                    case ReferencePendingProcessing.ProcessableObjectType.ManagedArray:
                        ProcessManagedArrayElements(currentItem, m_CachedSnapshot);
                        break;
                    case ReferencePendingProcessing.ProcessableObjectType.ManagedField:
                        ProcessField(currentItem, m_CachedSnapshot);
                        break;
                    default:
                        break;
                }
            }
        }

        public void LinkWasClicked(int treeViewId, bool recursiveSelection = false)
        {
            ReferencePendingProcessing pendingProcessing;
            if (m_ExpansionStopGapsByTreeViewItemId.TryGetValue(treeViewId, out pendingProcessing))
            {
                m_ExpansionStopGapsByTreeViewItemId.Remove(treeViewId);
                if (pendingProcessing.IndexToContinueAt > 0)
                {
                    if (pendingProcessing.Root.children.Count > pendingProcessing.IndexToContinueAt)
                        pendingProcessing.Root.children.RemoveAt(pendingProcessing.IndexToContinueAt);
                }
                else
                {
                    pendingProcessing.Root.children.Clear();
                }
                m_ReferencesPendingProcessing.Enqueue(pendingProcessing);
                ProcessQueue(pendingProcessing.Root.depth + k_MaxDepthIncrement);
                Reload();
            }
            else
            {
                SetSelection(new List<int> { treeViewId }, TreeViewSelectionOptions.RevealAndFrame);
            }
        }

        void ProcessManagedObjectFields(ReferencePendingProcessing item, CachedSnapshot snapshot)
        {
            if (!ValidateManagedObject(ref item.ObjectData, snapshot))
                return;

            if (item.ObjectData.dataType != ObjectDataType.Value && CheckRecursion(item.Root, GetIdentifyingPointer(item.ObjectData, snapshot)))
                return;

            // strings don't get their fields processed, they write their value into the field's root
            if (snapshot.TypeDescriptions.ITypeString == item.ObjectData.managedTypeIndex && item.ObjectData.dataType != ObjectDataType.Type)
            {
                item.Root.Value = StringTools.ReadString(item.ObjectData.managedObjectData, out _, snapshot.VirtualMachineInformation);
                return;
            }

            if (item.Root.depth >= 0 && item.Root.ManagedTypeIndex != item.ObjectData.managedTypeIndex)
            {
                // if we are adding multiline object info and the object type does not match the field type, add it in brackets.
                // Type names in brackets in the Value column signify that the item is not of the type indicated by the field type
                var actualTypeName = snapshot.TypeDescriptions.TypeDescriptionName[item.ObjectData.managedTypeIndex];
                item.Root.Value = FormatFieldValueWithContentTypeNotMatchingFieldType(null, actualTypeName);
            }

            var fieldList = BuildFieldList(item.ObjectData, snapshot);
            var elementCount = fieldList.Length;

            for (var i = 0; i < elementCount; i++)
            {
                ProcessField(new ReferencePendingProcessing(item.Root,
                    item.ObjectData.GetFieldByFieldDescriptionsIndex(snapshot, fieldList[i], false), fieldList, i)
                , snapshot);
            }
        }

        void ProcessField(ReferencePendingProcessing info, CachedSnapshot snapshot)
        {
            var i = info.IndexToContinueAt;
            string v = null;
            var typeIdx = info.ArrayInfo != null ? info.ArrayInfo.ElementTypeDescription : snapshot.FieldDescriptions.TypeIndex[info.FieldList[i]];
            var actualFielTypeIdx = typeIdx;
            if (info.ObjectData.dataType == ObjectDataType.ReferenceArray || info.ObjectData.dataType == ObjectDataType.ReferenceObject)
            {
                var referencedObject = info.ObjectData;
                if (ValidateManagedObject(ref referencedObject, snapshot))
                {
                    // Get the objects actual type, i.e. not e.g. System.Object in an array of that type, when the objects are actual implementations of other types
                    actualFielTypeIdx = referencedObject.managedTypeIndex;

                    if (referencedObject.dataType == ObjectDataType.BoxedValue)
                    {
                        referencedObject = referencedObject.GetBoxedValue(snapshot, true);
                    }
                    if (actualFielTypeIdx != typeIdx)
                    {
                        // if we are adding single line object info and the object type does not match the field type (e.g. because of boxing), add it in brackets.
                        // Type names in brackets in the Value column signify that the item is not of the type indicated by the field type
                        var actualTypeName = snapshot.TypeDescriptions.TypeDescriptionName[actualFielTypeIdx];
                        v = FormatFieldValueWithContentTypeNotMatchingFieldType(GetValue(referencedObject, info.Root), actualTypeName);
                    }
                }
            }
            v ??= GetValue(info.ObjectData, info.Root);
            var typeName = snapshot.TypeDescriptions.TypeDescriptionName[typeIdx];
            var fieldSize = GetFieldSize(snapshot, info.ObjectData);
            string name;
            var isStatic = false;
            if (info.ObjectData.IsField())
            {
                // Handle Field information
                name = snapshot.FieldDescriptions.FieldDescriptionName[info.FieldList[i]];
                isStatic = snapshot.FieldDescriptions.IsStatic[info.FieldList[i]] == 1;
            }
            else
            {
                Debug.Assert(info.ObjectData.IsArrayItem());

                name = GetArrayEntryName(info.ObjectData.Parent.Obj, info.ObjectData, info.ArrayInfo,
                    truncateTypeNames ? PathsToRootDetailView.TruncateTypeName(typeName) : typeName,
                    info.Root, snapshot);
            }

            var childItem = new ManagedObjectInspectorItem(m_InspectorID, name, typeIdx, typeName, v, isStatic, GetIdentifyingPointer(info.ObjectData, snapshot), fieldSize);
            childItem.depth = info.Root.depth + 1;

            ProcessSpecialFieldsAndQueueChildElements(info.ObjectData, actualFielTypeIdx, snapshot, ref childItem);

            info.Root.AddChild(childItem);
        }

        void ProcessIntPtr(CachedSnapshot cs, ObjectData fieldByIndex, ManagedObjectInspectorItem childItem, string value, ManagedObjectInspectorItem root)
        {
            ulong pointer;
            if (fieldByIndex.managedObjectData.TryReadPointer(out pointer) != BytesAndOffset.PtrReadError.Success)
            {
                childItem.Value = "Failed to read pointer!";
                return;
            }
            if (pointer == 0)
            {
                childItem.Value = "null";
                return;
            }
            if (pointer == ulong.MaxValue)
            {
                childItem.Value = "invalid";
                return;
            }

            if (cs.CrawledData.MangedObjectIndexByAddress.TryGetValue(pointer, out var objectIdentifyer))
            {
                m_ReferencesPendingProcessing.Enqueue(new ReferencePendingProcessing(childItem, ObjectData.FromManagedObjectIndex(cs, objectIdentifyer)));
            }
            else if (cs.NativeObjects.NativeObjectAddressToInstanceId.TryGetValue(pointer, out var _))
            {
                EnqueueNativeObject(childItem, pointer, cs);
            }
            else
            {
                var data = cs.ManagedHeapSections.Find(pointer, cs.VirtualMachineInformation);
                if (data.IsValid)
                {
                    // TODO: caveat these objects as "potential managed objects" as beyond them fitting into the managed heap, we can't be sure they are actually managed objects or just random bytes pointed to.
                    // We do however ensure that they at least fit the type of the field they are in.
                    var moi = ManagedDataCrawler.ParseObjectHeader(cs, pointer, out var wasAlreadyCrawled, true, data, fieldByIndex.managedTypeIndex);
                    if (moi.IsValid())
                    {
                        m_ReferencesPendingProcessing.Enqueue(new ReferencePendingProcessing(childItem, ObjectData.FromManagedObjectInfo(cs, moi)));
#if DEBUG_VALIDATION
                        Debug.LogError("Managed Object Inspector found a possible Managed Object that the crawler missed!");
#endif
                    }
                    else
                    {
                        int iHeapSection = -1;
                        for (int iManageHeapSection = 0; iManageHeapSection < cs.ManagedHeapSections.Count; iManageHeapSection++)
                        {
                            if (cs.ManagedHeapSections.StartAddress[iManageHeapSection] + cs.ManagedHeapSections.SectionSize[iManageHeapSection] < pointer)
                                continue;
                            if (cs.ManagedHeapSections.StartAddress[iManageHeapSection] <= pointer && cs.ManagedHeapSections.StartAddress[iManageHeapSection] + cs.ManagedHeapSections.SectionSize[iManageHeapSection] > pointer)
                            {
                                iHeapSection = iManageHeapSection;
                            }
                            break;
                        }

                        // when pointers are hidden, value is likely empty. For these details here though, we always provide the pointer info as string
                        if (HidePointers && string.IsNullOrEmpty(value))
                            value = DetailFormatter.FormatPointer(pointer);

                        var bytes = "";
                        var maxBytesAvailable = (ulong)data.Bytes.Count - data.Offset;
                        for (var b = 0u; b < maxBytesAvailable && b < 20; b++)
                        {
                            bytes += data.Bytes[(long)data.Offset + b].ToString("X") + " ";
                        }
                        // lets get some debug info
                        if (cs.ManagedHeapSections.SectionType[iHeapSection] == CachedSnapshot.MemorySectionType.GarbageCollector)
                            // some unsafe pointer outside of a fixed? Sounds Dangerous
                            childItem.Value = $"(IntPtr) -> Managed Heap @{value} Data: {bytes}";
                        else
                            // likely pointing at some Mono Object, e.g. vtable or the like
                            childItem.Value = $"(IntPtr) -> Virtual Machine @{value} Data: {bytes}";
                    }
                }
                else
                {
                    FindNativeAllocationOrRegion(cs, pointer, childItem, value);
                }
            }
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
            // when pointers are hidden, value is likely empty. For these details here though, we always provide the pointer info as string
            if (HidePointers && string.IsNullOrEmpty(value))
                value = DetailFormatter.FormatPointer(pointer);
            string nativeRegionPath = null;
            bool buildFullNativePath = false;
            var nativeRegion = cs.SortedNativeRegionsEntries.Find(pointer, onlyDirectAddressMatches: false);
            if (nativeRegion >= 0)
            {
                nativeRegionPath = cs.SortedNativeRegionsEntries.Name(nativeRegion);

                if (buildFullNativePath)
                {
                    var foundRegionInLayer = cs.SortedNativeRegionsEntries.RegionHierarchLayer[nativeRegion];
                    if (foundRegionInLayer > 0)
                    {
                        // search backwards for parent regions.
                        for (var iRegion = nativeRegion - 1; iRegion >= 0; iRegion--)
                        {
                            if (cs.SortedNativeRegionsEntries.RegionHierarchLayer[iRegion] >= foundRegionInLayer
                                || cs.SortedNativeRegionsEntries.Address(iRegion) + cs.SortedNativeRegionsEntries.Size(iRegion) < pointer)
                                continue;
                            if (cs.SortedNativeRegionsEntries.Address(iRegion) <= pointer)
                            {
                                nativeRegionPath += $"{cs.SortedNativeRegionsEntries.Name(iRegion)} / {nativeRegionPath}";
                                foundRegionInLayer = cs.SortedNativeRegionsEntries.RegionHierarchLayer[nativeRegion];
                                // found a parent region, continue searching if it is not on layer 0 though as there should be another parent-region enclosing this one
                                if (foundRegionInLayer == 0)
                                    break;
                            }
                        }
                    }
                }

                var nativeAllocation = cs.SortedNativeAllocations.Find(pointer, onlyDirectAddressMatches: false);
                if (nativeAllocation >= 0)
                {
                    var rootReference = cs.SortedNativeAllocations.RootReferenceId(nativeAllocation);
                    var allocationName = value;
                    if (rootReference < cs.NativeRootReferences.Count)
                    {
                        allocationName = $"{cs.NativeRootReferences.AreaName[rootReference]} / {cs.NativeRootReferences.ObjectName[rootReference]} / {value}";
                    }
                    var nativeObjectItem = new ManagedObjectInspectorItem(m_InspectorID, nativeFieldName, -1, nativeAllocationTypeName,
                        allocationName, false, pointer, cs.SortedNativeAllocations.Size(nativeAllocation));
                    nativeObjectItem.depth = childItem.depth + 1;
                    childItem.AddChild(nativeObjectItem);
                    //Debug.LogError("Managed Object Inspector found a possible pointer to Native Allocation that the crawler missed!");
                }
                else
                {
                    var nativeObjectItem = new ManagedObjectInspectorItem(m_InspectorID, nativeFieldName, -1, "Native Region", $"{nativeRegionPath} / {value}", false, pointer, 0ul);
                    nativeObjectItem.depth = childItem.depth + 1;
                    childItem.AddChild(nativeObjectItem);
                    //Debug.LogError("Managed Object Inspector found a possible pointer to Native Memory that the crawler missed!");
                }
            }
            else
            {
                // This pointer points out of range! Could be IL2CPP Virtual Machine Memory but it could just be entirely broken
                childItem.Value = $"{nativeFieldName} -> Not in Tracked Memory! @{value}";
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong GetIdentifyingPointer(ObjectData obj, CachedSnapshot cs)
        {
            var address = obj.GetObjectPointer(cs);
            return address;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool ValidateManagedObject(ref ObjectData obj, CachedSnapshot cs)
        {
            if (!obj.IsValid)
                return false;
            if (obj.dataType == ObjectDataType.ReferenceObject || obj.dataType == ObjectDataType.ReferenceArray)
            {
                var validObj = obj.GetReferencedObject(cs);
                if (!validObj.IsValid)
                    return false;
                obj = validObj;
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool CheckRecursion(ManagedObjectInspectorItem root, ulong identifyingPointer)
        {
            if (identifyingPointer != 0)
            {
                if (m_IdentifyingPointerToTreeItemId.TryGetValue(identifyingPointer, out var recursiveTreeViewId))
                {
                    root.MarkRecursiveOrDuplicate(recursiveTreeViewId);
                    if (root.IsRecursive)
                        return true;
                }
                else
                    m_IdentifyingPointerToTreeItemId.Add(identifyingPointer, root.id);
            }
            return false;
        }

        void ProcessManagedArrayElements(ReferencePendingProcessing item, CachedSnapshot snapshot)
        {
            if (item.ArrayInfo == null)
            {
                // Only Validate and check recursion if this is not a continuation
                if (!ValidateManagedObject(ref item.ObjectData, snapshot))
                    return;

                if (CheckRecursion(item.Root, GetIdentifyingPointer(item.ObjectData, snapshot)))
                    return;

                item.ArrayInfo = item.ObjectData.GetArrayInfo(snapshot);
                item.IndexToContinueAt = 0;
            }

            var elementCount = item.ArrayInfo.Length;
            var elementToStopAt = Math.Min(elementCount, item.IndexToContinueAt + k_MaxArrayIncrement);

            for (var i = item.IndexToContinueAt; i < elementToStopAt; i++)
            {
                var field = new ReferencePendingProcessing(item.Root,
                    item.ObjectData.GetArrayElement(m_CachedSnapshot, item.ArrayInfo, i, true),
                    item.ArrayInfo, i);
                ProcessField(field, snapshot);
            }

            // if not all elements were added, add a placeholder to continue processing as needed by the user
            if (elementToStopAt < elementCount)
            {
                item.IndexToContinueAt = elementToStopAt;
                var stopGapChild = new ManagedObjectInspectorItem(m_InspectorID, item);
                stopGapChild.depth = rootItem.depth + 1;
                m_ExpansionStopGapsByTreeViewItemId.Add(stopGapChild.id, item);
                item.Root.AddChild(stopGapChild);
                return;
            }
        }

        void ProcessSpecialFieldsAndQueueChildElements(ObjectData field, int actualFielTypeIndex, CachedSnapshot cs, ref ManagedObjectInspectorItem fieldRootEntry)
        {
            const string k_NativeArrayTypePrefix = "Unity.Collections.NativeArray<";
            // for array elements, field.IsField() is false
            if (field.IsField() && field.fieldIndex == cs.TypeDescriptions.IFieldUnityObjectMCachedPtr
                // The field index alone doesn't make this field the m_CachedPtr field, the parent also needs inherit from UnityEngine.Object.
                && cs.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex.ContainsKey(field.Parent.Obj.managedTypeIndex))
            {
                if (field.managedObjectData.TryReadPointer(out var nativeObjectPointer) == BytesAndOffset.PtrReadError.Success)
                {
                    if (nativeObjectPointer == 0)
                    {
                        fieldRootEntry = new ManagedObjectInspectorItem(m_InspectorID, fieldRootEntry.DisplayName, fieldRootEntry.ManagedTypeIndex, fieldRootEntry.TypeName, $"{fieldRootEntry.Value} {TextContent.LeakedManagedShellHint}", fieldRootEntry.IsStatic, GetIdentifyingPointer(field, cs), fieldRootEntry.Size);
                    }
                    else
                    {
                        EnqueueNativeObject(fieldRootEntry, nativeObjectPointer, cs);
                    }
                }
            }
            else if (fieldRootEntry.ManagedTypeIndex == cs.TypeDescriptions.ITypeIntPtr)
            {
                ProcessIntPtr(cs, field, fieldRootEntry, fieldRootEntry.Value, fieldRootEntry);
            }
            else if ((field.IsArrayItem() || field.dataType == ObjectDataType.Value) && fieldRootEntry.TypeName.StartsWith(k_NativeArrayTypePrefix))
            {
                var countOfGenericOpen = 1;
                var countOfGenericClose = 0;
                unsafe
                {
                    fixed (char* c = fieldRootEntry.TypeName)
                    {
                        char* it = c + k_NativeArrayTypePrefix.Length;
                        for (var charPos = k_NativeArrayTypePrefix.Length; charPos < fieldRootEntry.TypeName.Length; charPos++)
                        {
                            if (*it == '<')
                                ++countOfGenericOpen;
                            else if (*it == '>')
                                ++countOfGenericClose;
                            ++it;
                        }
                    }
                }
                if (countOfGenericOpen == countOfGenericClose && fieldRootEntry.TypeName.EndsWith(">"))
                {
                    // only parse types named "NativeArrays" that that end on the generic bracket they opened for the Native Array.
                    // E.g. Avoid parsing Unity.Collections.NativeArray<System.Int32>[]
                    if (field.managedObjectData.TryReadPointer(out var pointerToMBufferData) == BytesAndOffset.PtrReadError.Success)
                    {
                        FindNativeAllocationOrRegion(cs, pointerToMBufferData, fieldRootEntry, fieldRootEntry.Value, "m_Buffer", "void*");
                    }
                }
            }
            else if (field.dataType == ObjectDataType.Value &&
                    cs.TypeDescriptions.FieldIndicesInstance[fieldRootEntry.ManagedTypeIndex].Length == 1 &&
                    GetFirstInstanceFieldIfValueType(cs, field, fieldRootEntry.ManagedTypeIndex, out var childField))
            {
                // For Value type fields with exactly one value type field, show the value in-line to avoid having to expand too much.
                // Give the type name of the field in brackets after the value to clarify it is not the same as the original field type
                // For an enum field, this looks like e.g. : m_MyEnumValue | 0 (System.Int32) | MyEnumType
                ProcessField(new ReferencePendingProcessing(fieldRootEntry, childField, cs.TypeDescriptions.FieldIndicesInstance[fieldRootEntry.ManagedTypeIndex], 0), cs);
                var fieldInfo = fieldRootEntry.children[0] as ManagedObjectInspectorItem;
                // Type names in brackets in the Value column signify that the item is not of the type indicated by the field type
                fieldRootEntry.Value = FormatFieldValueWithContentTypeNotMatchingFieldType(fieldInfo.Value, fieldInfo.TypeName);
                fieldRootEntry.children.Clear();
            }
            else if ((field.dataType == ObjectDataType.Value &&
                    cs.TypeDescriptions.FieldIndicesInstance[fieldRootEntry.ManagedTypeIndex].Length >= 1) ||
                field.dataType == ObjectDataType.Array || field.dataType == ObjectDataType.ReferenceArray ||
                field.dataType == ObjectDataType.Object || field.dataType == ObjectDataType.ReferenceObject)
            {
                // string and null are fully handled by GetValue()
                if (fieldRootEntry.Value != "null" && actualFielTypeIndex != cs.TypeDescriptions.ITypeString)
                    m_ReferencesPendingProcessing.Enqueue(new ReferencePendingProcessing(fieldRootEntry, field));
            }
        }

        /// <summary>
        /// Helper method, no actual safety checks, just for easier to read syntax in calling code.
        /// </summary>
        /// <param name="cs"></param>
        /// <param name="field"></param>
        /// <param name="fieldRootManagedTypeIndex"></param>
        /// <param name="firstField"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool GetFirstInstanceFieldIfValueType(CachedSnapshot cs, ObjectData field, int fieldRootManagedTypeIndex, out ObjectData firstField)
        {
            firstField = field.GetFieldByFieldDescriptionsIndex(cs, cs.TypeDescriptions.FieldIndicesInstance[fieldRootManagedTypeIndex][0], false);
            return firstField.dataType == ObjectDataType.Value;
        }

        static string GetArrayEntryName(ObjectData arrayObject, ObjectData arrayElement, ArrayInfo arrayInfo, string typeName, ManagedObjectInspectorItem root, CachedSnapshot cs)
        {
            // Handle Array element information
            if (arrayObject.IsField())
            {
                return arrayObject.GetFieldName(cs);
            }
            else
            {
                var parentIsJaggedArrayContainer = !string.IsNullOrEmpty(root.DisplayName) && root.DisplayName.Contains("[]");
                var name = parentIsJaggedArrayContainer ? root.DisplayName : typeName;
                var leadingSpace = parentIsJaggedArrayContainer ? string.Empty : " [";
                var followingSpace = parentIsJaggedArrayContainer ? string.Empty : "]";
                var indexOfFirstEmptyArrayBrackets = name.IndexOf("[]");
                if (indexOfFirstEmptyArrayBrackets >= 0)
                    name = name.Insert(indexOfFirstEmptyArrayBrackets + (parentIsJaggedArrayContainer ? 1 : 0), $"{leadingSpace}{arrayInfo.IndexToRankedString(arrayElement.arrayIndex)}{followingSpace}");
                else
                    name += $"{leadingSpace}{arrayInfo.IndexToRankedString(arrayElement.arrayIndex)}{followingSpace}";
                return name;
            }
        }

        void EnqueueNativeObject(ManagedObjectInspectorItem root, ulong address, CachedSnapshot cs)
        {
            if (address == 0)
                return;
            if (!cs.NativeObjects.NativeObjectAddressToInstanceId.ContainsKey(address)) return;
            var instanceId = cs.NativeObjects.NativeObjectAddressToInstanceId[address];
            if (instanceId == InstanceID.None)
                return;
            var index = cs.NativeObjects.InstanceId2Index[instanceId];
            var nativeObjectData = ObjectData.FromNativeObjectIndex(cs, index);
            if (!nativeObjectData.IsValid)
                return;
            m_ReferencesPendingProcessing.Enqueue(new ReferencePendingProcessing(root, nativeObjectData));
        }

        void AddNativeObject(ManagedObjectInspectorItem root, ObjectData nativeObjectData, CachedSnapshot cs)
        {
            var identifyingPointer = nativeObjectData.GetObjectPointer(cs);
            var nativeObjectItem = new ManagedObjectInspectorItem(m_InspectorID, "Native Reference", -1, nativeObjectData.GenerateTypeName(cs),
                cs.NativeObjects.ObjectName[nativeObjectData.nativeObjectIndex], false, identifyingPointer, cs.NativeObjects.Size[nativeObjectData.nativeObjectIndex]);
            nativeObjectItem.depth = root.depth + 1;
            root.AddChild(nativeObjectItem);

            root = nativeObjectItem;

            if (CheckRecursion(root, identifyingPointer))
                return;

            var referencedObjects = nativeObjectData.GetAllReferencedObjects(cs);

            int referencedObjectCount = referencedObjects.Length;

            for (int i = 0; i < referencedObjectCount; i++)
            {
                var referencedObject = referencedObjects[i];
                if (referencedObject.isNative)
                {
                    // For now, don't go deeper on Native Object <-> Native Object
                    //if (referencedObject.nativeObjectIndex >= 0 && referencedObject.nativeObjectIndex != nativeObjectData.nativeObjectIndex)
                    //    m_ReferencesPendingProcessing.Enqueue(new ReferencePendingProcessing { objectData = referencedObject, root = root });
                    continue;
                }
                var v = GetValue(referencedObject, root);
                var name = "Native Reference";
                var typename = referencedObject.GenerateTypeName(cs);
                var childItem = new ManagedObjectInspectorItem(m_InspectorID, name, -1, typename, v, false, referencedObject.GetObjectPointer(cs), 0ul);
                childItem.depth = root.depth + 1;
                root.AddChild(childItem);

                if (referencedObject.dataType == ObjectDataType.Array || referencedObject.dataType == ObjectDataType.ReferenceArray ||
                    referencedObject.dataType == ObjectDataType.Object || referencedObject.dataType == ObjectDataType.ReferenceObject)
                {
                    m_ReferencesPendingProcessing.Enqueue(new ReferencePendingProcessing(childItem, referencedObject));
                }
            }
        }

        string FormatFieldValueWithContentTypeNotMatchingFieldType(string value, string actualTypeName)
        {
            if (truncateTypeNames)
                actualTypeName = PathsToRootDetailView.TruncateTypeName(actualTypeName);
            if (value == null)
                return $"({actualTypeName})";
            else
                return $"{value} ({actualTypeName})";
        }

        string GetValue(ObjectData od, ManagedObjectInspectorItem parent)
        {
            if (!od.IsValid || !od.HasValidFieldOrArrayElementData(m_CachedSnapshot))
                return "failed to read data";
            if (od.managedObjectData.Bytes.Count == 0
                && m_CachedSnapshot.FieldDescriptions.IsStatic[od.fieldIndex] == 1
                && m_CachedSnapshot.TypeDescriptions.HasStaticFieldData(od.managedTypeIndex))
            {
                return "uninitialized static field data";
            }
            switch (od.dataType)
            {
                case ObjectDataType.BoxedValue:
                    return m_Formatter.FormatValueType(od.GetBoxedValue(m_CachedSnapshot, true), false, truncateTypeNames);
                case ObjectDataType.Value:
                    return (HidePointers && od.managedTypeIndex == m_CachedSnapshot.TypeDescriptions.ITypeIntPtr) ?
                        string.Empty
                        : m_Formatter.FormatValueType(od, false, truncateTypeNames);
                case ObjectDataType.Object:
                    return m_Formatter.FormatObject(od, false, truncateTypeNames);
                case ObjectDataType.Array:
                    return m_Formatter.FormatArray(od, truncateTypeNames);
                case ObjectDataType.ReferenceObject:
                {
                    ulong ptr = od.GetReferencePointer();
                    if (ptr == 0)
                    {
                        return "null";
                    }
                    else
                    {
                        var o = ObjectData.FromManagedPointer(m_CachedSnapshot, ptr, od.managedTypeIndex);
                        if (!o.IsValid)
                            return "failed to read object";
                        if (o.dataType == ObjectDataType.BoxedValue)
                            return m_Formatter.FormatValueType(o.GetBoxedValue(m_CachedSnapshot, true), false, truncateTypeNames);
                        return m_Formatter.FormatObject(o, false, truncateTypeNames);
                    }
                }
                case ObjectDataType.ReferenceArray:
                {
                    ulong ptr = od.GetReferencePointer();
                    if (ptr == 0)
                    {
                        return "null";
                    }
                    var arr = ObjectData.FromManagedPointer(m_CachedSnapshot, ptr, od.managedTypeIndex);
                    if (!arr.IsValid)
                        return "failed to read pointer";
                    return m_Formatter.FormatArray(arr, truncateTypeNames);
                }
                case ObjectDataType.Type:
                    return m_CachedSnapshot.TypeDescriptions.TypeDescriptionName[od.managedTypeIndex];
                case ObjectDataType.NativeObject:
                    return HidePointers ? string.Empty : DetailFormatter.FormatPointer(m_CachedSnapshot.NativeObjects.NativeObjectAddress[od.nativeObjectIndex]);
                default:
                    return "<uninitialized type>";
            }
        }

        static int[] BuildFieldList(ObjectData obj, CachedSnapshot snapshot)
        {
            List<int> fields = new List<int>();
            switch (obj.dataType)
            {
                case ObjectDataType.Type:
                    //take all static field
                    return snapshot.TypeDescriptions.fieldIndicesStatic[obj.managedTypeIndex]; ;
                case ObjectDataType.BoxedValue:
                case ObjectDataType.Object:
                case ObjectDataType.Value:
                case ObjectDataType.ReferenceObject:
                    fields.AddRange(snapshot.TypeDescriptions.fieldIndicesStatic[obj.managedTypeIndex]);
                    fields.AddRange(snapshot.TypeDescriptions.FieldIndicesInstance[obj.managedTypeIndex]);
                    break;
            }
            return fields.ToArray();
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
    }
}

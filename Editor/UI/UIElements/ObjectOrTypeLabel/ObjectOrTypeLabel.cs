using System;
#if !UNITY_6000_0_OR_NEWER
using System.Collections.Generic;
#endif
using Unity.MemoryProfiler.Editor.UI.PathsToRoot;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
#if UNITY_6000_0_OR_NEWER
    [UxmlElement]
#endif
    internal partial class ObjectOrTypeLabel : VisualElement, IDisposable
    {
        const string k_UxmlAssetGuid = "d5780d6f2a7371a48bd79da612d8b6c4";

        public enum DataType
        {
            PureCSharpType,
            NativeUnityType,
            UnifiedUnityType,

            ManagedObject,
            NativeObject,
            UnifiedUnityObject,

            GCHandle,

            LeakedShell,
            LeakedDynamicAsset,
        }

        DataType m_DataType = DataType.UnifiedUnityObject;

#if UNITY_6000_0_OR_NEWER
        [UxmlAttribute]
#endif
        public DataType Type
        {
            get { return m_DataType; }
            private set
            {
                m_DataType = value;
                m_DataTypeIcon.ClearClassList();
                m_DataTypeIcon.AddToClassList(k_ClassIconItem);
                m_DataTypeIcon.AddToClassList(k_ClassDataTypeIcon);
                switch (value)
                {
                    case DataType.ManagedObject:
                    case DataType.PureCSharpType:
                        m_DataTypeIcon.AddToClassList(k_ClassDataTypeManaged);
                        m_DataTypeIcon.tooltip = TextContent.DataTypeManagedTooltip;
                        break;
                    case DataType.NativeUnityType:
                    case DataType.NativeObject:
                        m_DataTypeIcon.AddToClassList(k_ClassDataTypeNative);
                        m_DataTypeIcon.tooltip = TextContent.DataTypeNativeTooltip;
                        break;
                    case DataType.GCHandle:
                        m_DataTypeIcon.AddToClassList(k_ClassDataTypeGCHandle);
                        m_DataTypeIcon.tooltip = TextContent.DataTypeGCHandleTooltip;
                        break;
                    case DataType.UnifiedUnityType:
                    case DataType.UnifiedUnityObject:
                        m_DataTypeIcon.AddToClassList(k_ClassDataTypeUnified);
                        m_DataTypeIcon.tooltip = TextContent.DataTypeUnifiedUnityTooltip;
                        break;
                    case DataType.LeakedShell:
                        m_DataTypeIcon.AddToClassList(k_ClassDataTypeLeakedShell);
                        m_DataTypeIcon.tooltip = TextContent.DataTypeLeakedShellTooltip;
                        break;
                    case DataType.LeakedDynamicAsset:
                        m_DataTypeIcon.AddToClassList(k_ClassDataTypeLeakedDynamicAsset);
                        m_DataTypeIcon.tooltip = TextContent.DataTypeLeakedDynamicAssetTooltip;
                        break;
                    default:
                        m_DataTypeIcon.AddToClassList(k_ClassDataTypeUnknown);
                        break;
                }
            }
        }

        ObjectDataType m_ObjectDataType = ObjectDataType.Unknown;
        public ObjectDataType ObjectDataType
        {
            get { return m_ObjectDataType; }
            private set
            {
                m_ObjectDataType = value;
                m_DataTypeIcon.ClearClassList();
                m_DataTypeIcon.AddToClassList(k_ClassIconItem);
                m_DataTypeIcon.AddToClassList(k_ClassDataTypeIcon);
                switch (m_ObjectDataType)
                {
                    case ObjectDataType.Value:
                    case ObjectDataType.Object:
                    case ObjectDataType.Array:
                    case ObjectDataType.BoxedValue:
                    case ObjectDataType.ReferenceObject:
                    case ObjectDataType.ReferenceArray:
                    case ObjectDataType.Type:
                        Type = DataType.PureCSharpType;
                        break;
                    case ObjectDataType.NativeObject:
                    case ObjectDataType.NativeAllocation:
                        Type = DataType.NativeObject;
                        break;
                    case ObjectDataType.GCHandle:
                        Type = DataType.GCHandle;
                        break;
                    case ObjectDataType.Unknown:
                    default:
                        Type = DataType.LeakedShell;
                        break;
                }
            }
        }

        string m_ManagedTypeName = string.Empty;

#if UNITY_6000_0_OR_NEWER
        [UxmlAttribute]
#endif
        public string ManagedTypeName
        {
            get
            {
                return m_ManagedTypeName;
            }
            set
            {
                m_ManagedTypeName = value;
                UpdateLabelContent();
            }
        }

        string m_NativeTypeName = string.Empty;

#if UNITY_6000_0_OR_NEWER
        [UxmlAttribute]
#endif
        public string NativeTypeName
        {
            get
            {
                return m_NativeTypeName;
            }
            set
            {
                m_NativeTypeName = value;
                UpdateLabelContent();
            }
        }

        string m_NativeObjectName = string.Empty;
        CachedSnapshot.SourceIndex m_SourceIndex;

#if UNITY_6000_0_OR_NEWER
        [UxmlAttribute]
#endif
        public string NativeObjectName
        {
            get
            {
                return m_NativeObjectName;
            }
            set
            {
                m_NativeObjectName = value;
                UpdateLabelContent();
            }
        }

        bool m_HideTypeInfoForObjects = false;
#if UNITY_6000_0_OR_NEWER
        [UxmlAttribute]
#endif
        public bool HideTypeInfoForObjects
        {
            get
            {
                return m_HideTypeInfoForObjects;
            }
            set
            {
                m_HideTypeInfoForObjects = value;
                UpdateLabelContent();
            }
        }

        /// <summary>
        /// Use this as a using var when updating the label content in batch to avoid unnecessary intermediate updates.
        /// The label will be updated once at the end of the scope.
        /// </summary>
        struct DelayedLabelUpdateScope : IDisposable
        {
            ObjectOrTypeLabel m_Label;
            public DelayedLabelUpdateScope(ObjectOrTypeLabel label)
            {
                m_Label = label;
                m_Label.m_IgnoreLabelContentUpdates = true;
            }
            public void Dispose()
            {
                m_Label.m_IgnoreLabelContentUpdates = false;
                m_Label.UpdateLabelContent();
            }
        }
        bool m_IgnoreLabelContentUpdates = false;

        public Action<ContextualMenuPopulateEvent> ContextMenuOpening = delegate { };

        public override VisualElement contentContainer
        {
            get { return null; }
        }

        VisualElement m_Root;
        VisualElement m_DataTypeIcon;
        Image m_TypeIcon;
        Label m_Label;
        Label m_SecondaryLabel;
        Button m_DocumentationButton;

        bool m_NeedsDisposal;

        const string k_ClassIconItem = "type-icon";
        const string k_ClassDataTypeIcon = "data-type-icon";
        const string k_ClassDataTypeNative = "data-type-icon__native";
        const string k_ClassDataTypeGCHandle = "data-type-icon__managed"; // Why is this the script file icon and not the same as PathsToRootDetailView.Styles.CSharpIconContent?
        const string k_ClassDataTypeManaged = "data-type-icon__managed";
        const string k_ClassDataTypeUnified = "data-type-icon__unified";
        const string k_ClassDataTypeLeakedShell = "data-type-icon__leaked-shell";
        const string k_ClassDataTypeLeakedDynamicAsset = "data-type-icon__leaked-dynamic-asset";
        const string k_ClassDataTypeUnknown = "no-icon__icon";

        public ObjectOrTypeLabel()
        {
            // Construct from a template
            m_Root = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);

            // Setup hierarchy
            hierarchy.Add(m_Root);
            style.flexShrink = 0;

            // Gather references & setup
            m_DataTypeIcon = m_Root.Q("object-or-type-label__data-type-icon");
            m_TypeIcon = m_Root.Q<Image>("object-or-type-label__type-icon");
            m_Label = m_Root.Q<Label>("object-or-type-label__text");
            m_SecondaryLabel = m_Root.Q<Label>("object-or-type-label__secondary-text");
            m_DocumentationButton = m_Root.Q<Button>("object-or-type-label__documentation-button");

            this.AddManipulator(new ContextualMenuManipulator(PopulateOptionMenu));

            UIElementsHelper.SetVisibility(m_DocumentationButton, false /*!string.IsNullOrEmpty(m_DocumentationLink)*/);
        }

        void PopulateOptionMenu(ContextualMenuPopulateEvent evt)
        {
            if (NativeTypeName == PathsToRootDetailView.Styles.NoObjectSelected)
                return;

            evt.menu.AppendAction(TextContent.TruncateTypeName, (a) =>
            {
                MemoryProfilerSettings.ToggleTruncateTypes();
            }, MemoryProfilerSettings.MemorySnapshotTruncateTypes ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            evt.menu.AppendSeparator();
            ContextMenuOpening(evt);
        }

        public string GetTitle(bool truncateManagedType = false)
        {
            string name;
            if (string.IsNullOrEmpty(m_NativeObjectName))
                name = string.Empty;
            else
                name = m_NativeObjectName;

            if (m_HideTypeInfoForObjects &&
                m_SourceIndex.Id is CachedSnapshot.SourceIndex.SourceId.NativeObject
                or CachedSnapshot.SourceIndex.SourceId.ManagedObject
                or CachedSnapshot.SourceIndex.SourceId.NativeAllocation)
                return name;

            // These data types have no type name worth showing next to their name (e.g. a GCHandle's "type"
            // is just the GCHandle icon/tooltip, not a separate name), so skip straight to the name.
            if (Type is DataType.GCHandle)
                return name;

            // Apply the name and format it as surrounded by parentheses if it could have a native name.
            // If it could have a name but doesn't, the empty quotes will indicate that.
            // For managed objects with their address as names or string preview values, we want to show the name without quotes to avoid visual clutter. 
            var text = Type is DataType.UnifiedUnityObject or DataType.NativeObject or DataType.LeakedDynamicAsset or DataType.LeakedShell ? $"\"{name}\" " :
                (string.IsNullOrEmpty(name) ? $"{name} " : name);

            var managedTypeName = truncateManagedType ? PathsToRootDetailView.TruncateTypeName(m_ManagedTypeName) : m_ManagedTypeName;
            if (!string.IsNullOrEmpty(m_ManagedTypeName))
                text += managedTypeName;

            if (m_NativeTypeName != managedTypeName)
                text += (string.IsNullOrEmpty(m_ManagedTypeName) || string.IsNullOrEmpty(m_NativeTypeName) ? string.Empty : " : ") + m_NativeTypeName;

            return text;
        }

        public void SetLabelData(CachedSnapshot cs, UnifiedTypeAndName typeInfo)
        {
            InitializeIfNeeded();
            ShowIcons(true);
            GUIContent typeIcon;
            if (typeInfo.ManagedTypeIndex >= 0)
            {
                typeIcon = Icons.GetIcon(typeInfo.ManagedTypeData, typeInfo.ManagedTypeName, cs);
                m_TypeIcon.tooltip = typeInfo.ManagedTypeName;
            }
            else
            {
                typeIcon = Icons.GetIcon(typeInfo.ManagedTypeData /*aka invalid*/, typeInfo.NativeTypeName, cs);
                m_TypeIcon.tooltip = typeInfo.NativeTypeName;
            }
            m_TypeIcon.image = typeIcon?.image;
            Type = typeInfo.IsUnifiedType ? DataType.UnifiedUnityType : (typeInfo.HasManagedType ? DataType.PureCSharpType : DataType.NativeUnityType);
            m_SourceIndex = typeInfo.HasManagedType ? new CachedSnapshot.SourceIndex(CachedSnapshot.SourceIndex.SourceId.ManagedType, typeInfo.ManagedTypeIndex)
                : new CachedSnapshot.SourceIndex(CachedSnapshot.SourceIndex.SourceId.NativeType, typeInfo.NativeTypeIndex);

            // Delay label updating until the end of this call, but use the properties to update the backing fields used for the UXML attributed fields.
            using var _ = new DelayedLabelUpdateScope(this);
            ManagedTypeName = typeInfo.ManagedTypeName;
            NativeTypeName = typeInfo.NativeTypeName;
            NativeObjectName = string.Empty;
        }

        public void SetLabelData(CachedSnapshot cs, UnifiedUnityObjectInfo unityObjectInfo)
        {
            InitializeIfNeeded();
            ShowIcons(true);
            if (!unityObjectInfo.HasNativeSide && !unityObjectInfo.Type.HasNativeType && unityObjectInfo.HasManagedSide)
            {
                SetLabelData(cs, pureCSharpObject: unityObjectInfo.ManagedObjectData, typeInfo: unityObjectInfo.Type);
                return;
            }
            GUIContent typeIcon;
            if (unityObjectInfo.HasManagedSide)
            {
                typeIcon = Icons.GetIcon(unityObjectInfo.ManagedObjectData, unityObjectInfo.ManagedTypeName, cs);
                m_TypeIcon.tooltip = unityObjectInfo.ManagedTypeName;
            }
            else
            {
                typeIcon = Icons.GetIcon(unityObjectInfo.NativeObjectData, unityObjectInfo.NativeTypeName, cs);
                m_TypeIcon.tooltip = unityObjectInfo.NativeTypeName;
            }
            m_TypeIcon.image = typeIcon?.image;

            m_SourceIndex = unityObjectInfo.HasManagedSide ? unityObjectInfo.ManagedObjectData.GetSourceLink(cs) : unityObjectInfo.NativeObjectData.GetSourceLink(cs);
            Type = GetDataTypeFromStatus(cs, in unityObjectInfo, m_SourceIndex);

            // Delay label updating until the end of this call, but use the properties to update the backing fields used for the UXML attributed fields.
            using var _ = new DelayedLabelUpdateScope(this);
            ManagedTypeName = unityObjectInfo.ManagedTypeName;
            NativeTypeName = unityObjectInfo.NativeTypeName;
            NativeObjectName = unityObjectInfo.NativeObjectName;
        }

        /// <summary>
        /// Derives the data type — and with it the leaked-object icon — from the precomputed
        /// <see cref="ObjectStatus"/>, so this label cannot contradict the status reported for the same object
        /// in the tables and the details panel. Snapshots whose status computation did not run report
        /// <see cref="ObjectStatus.None"/>; those fall back to the <see cref="UnifiedUnityObjectInfo"/> predicates.
        /// </summary>
        static DataType GetDataTypeFromStatus(CachedSnapshot cs, in UnifiedUnityObjectInfo unityObjectInfo, CachedSnapshot.SourceIndex sourceIndex)
        {
            var status = cs?.ObjectStatusComputation?.GetStatus(sourceIndex) ?? ObjectStatus.None;
            switch (status)
            {
                case ObjectStatus.LeakedShell:
                case ObjectStatus.LeakedShellHeldByGCHandle:
                    return DataType.LeakedShell;
                case ObjectStatus.DynamicAssetLeaked:
                    return DataType.LeakedDynamicAsset;
                case ObjectStatus.None:
                    return unityObjectInfo.IsFullUnityObject
                        ? (unityObjectInfo.IsLeakedDynamicAsset ? DataType.LeakedDynamicAsset : DataType.UnifiedUnityObject)
                        : (unityObjectInfo.IsLeakedShell ? DataType.LeakedShell : DataType.NativeObject);
                default:
                    return unityObjectInfo.IsFullUnityObject ? DataType.UnifiedUnityObject : DataType.NativeObject;
            }
        }

        public void SetLabelData(CachedSnapshot cs, string typeName, string objectName, DataType dataType = DataType.NativeUnityType, bool showDataTypeIcon = false)
        {
            InitializeIfNeeded();
            ShowIcons(showDataTypeIcon, onlyDataTypeIcon: true);
            m_TypeIcon.tooltip = typeName;
            m_TypeIcon.image = null;
            Type = dataType;
            m_SourceIndex = default;

            // Delay label updating until the end of this call, but use the properties to update the backing fields used for the UXML attributed fields.
            using var _ = new DelayedLabelUpdateScope(this);
            ManagedTypeName = string.Empty;
            NativeTypeName = typeName;
            NativeObjectName = objectName;
        }


        public void SetLabelData(CachedSnapshot cs, ObjectData pureCSharpObject, UnifiedTypeAndName typeInfo)
        {
            InitializeIfNeeded();
            ShowIcons(true);
            var typeIcon = Icons.GetIcon(pureCSharpObject, typeInfo.ManagedTypeName, cs);
            m_TypeIcon.tooltip = typeInfo.ManagedTypeName;
            m_TypeIcon.image = typeIcon?.image;
            Type = DataType.ManagedObject;
            m_SourceIndex = pureCSharpObject.GetSourceLink(cs);

            // Delay label updating until the end of this call, but use the properties to update the backing fields used for the UXML attributed fields.
            using var _ = new DelayedLabelUpdateScope(this);
            ManagedTypeName = typeInfo.ManagedTypeName;
            NativeTypeName = string.Empty;
            NativeObjectName = string.Empty;
        }

        void UpdateLabelContent()
        {
            if (!m_IgnoreLabelContentUpdates && m_Label != null)
            {
                m_Label.text = GetTitle(MemoryProfilerSettings.MemorySnapshotTruncateTypes);
                m_Label.tooltip = m_Label.text;
            }
        }

        void ShowIcons(bool show, bool onlyDataTypeIcon = false)
        {
            UIElementsHelper.SetVisibility(m_DataTypeIcon, show);
            UIElementsHelper.SetVisibility(m_TypeIcon, !onlyDataTypeIcon && show);
        }

        public void SetSecondaryText(string text)
        {
            m_SecondaryLabel.text = text;
            UIElementsHelper.SetElementDisplay(m_SecondaryLabel, !string.IsNullOrEmpty(text));
        }

        public void SetToNoObjectSelected()
        {
            // Reset Type first: GetTitle() short-circuits on some Types (e.g. GCHandle) before ever
            // looking at NativeTypeName, which would otherwise suppress the placeholder text below if
            // this label previously displayed one of those types.
            Type = DataType.NativeUnityType;
            ManagedTypeName = "";
            NativeObjectName = "";
            NativeTypeName = PathsToRootDetailView.Styles.NoObjectSelected;
            m_TypeIcon.image = Icons.NoIconContent?.image;
            UIElementsHelper.SetVisibility(m_DataTypeIcon, false);
            UIElementsHelper.SetVisibility(m_TypeIcon, true);
        }

        public bool TrySetLabelDataFromSourceIndexForTableEntry(CachedSnapshot snapshot, CachedSnapshot.SourceIndex source, string itemName, out string displayName)
        {
            displayName = itemName;
            if (string.IsNullOrEmpty(displayName))
                displayName = CachedSnapshot.InvalidItemName;
            return TrySetLabelDataFromSourceIndex(snapshot, source, displayName);
        }

        public bool TrySetLabelDataFromSourceIndex(CachedSnapshot snapshot, CachedSnapshot.SourceIndex source, string displayName = null)
        {
            switch (source.Id)
            {
                case CachedSnapshot.SourceIndex.SourceId.ManagedObject:
                case CachedSnapshot.SourceIndex.SourceId.NativeObject:
                case CachedSnapshot.SourceIndex.SourceId.ManagedType:
                case CachedSnapshot.SourceIndex.SourceId.NativeType:
                case CachedSnapshot.SourceIndex.SourceId.NativeRootReference:
                case CachedSnapshot.SourceIndex.SourceId.NativeAllocation:
                case CachedSnapshot.SourceIndex.SourceId.GfxResource:
                case CachedSnapshot.SourceIndex.SourceId.GCHandleIndex:
                case CachedSnapshot.SourceIndex.SourceId.Prefab:
                case CachedSnapshot.SourceIndex.SourceId.Scene:
                    SetLabelData(snapshot, source, displayName);
                    return true;
                default:
                    return false;
            }
        }

        public void SetLabelData(CachedSnapshot cs, CachedSnapshot.SourceIndex source, string itemName = null)
        {
            switch (source.Id)
            {
                case CachedSnapshot.SourceIndex.SourceId.ManagedObject:
                {
                    var od = ObjectData.FromManagedObjectIndex(cs, (int)source.Index);
                    ShowIcons(true);
                    SetLabelData(cs, new UnifiedUnityObjectInfo(cs, od));
                    if (!string.IsNullOrEmpty(itemName))
                        NativeObjectName = itemName;
                    break;
                }
                case CachedSnapshot.SourceIndex.SourceId.NativeObject:
                {
                    var od = ObjectData.FromNativeObjectIndex(cs, (int)source.Index);
                    ShowIcons(true);
                    SetLabelData(cs, new UnifiedUnityObjectInfo(cs, od));
                    if (!string.IsNullOrEmpty(itemName))
                        NativeObjectName = itemName;
                    break;
                }
                case CachedSnapshot.SourceIndex.SourceId.ManagedType:
                {
                    var od = ObjectData.FromManagedType(cs, (int)source.Index);
                    ShowIcons(true);
                    SetLabelData(cs, UnifiedTypeAndName.GetTypeInfoForObjectData(cs, od));
                    break;
                }
                case CachedSnapshot.SourceIndex.SourceId.NativeType:
                {
                    var typeInfo = new UnifiedTypeAndName(cs, (int)source.Index);
                    ShowIcons(true);
                    SetLabelData(cs, typeInfo);
                    break;
                }
                case CachedSnapshot.SourceIndex.SourceId.NativeRootReference:
                {
                    var typeName = cs.NativeRootReferences.AreaName[source.Index];
                    SetLabelData(cs, typeName, string.IsNullOrEmpty(itemName) ? cs.NativeRootReferences.ObjectName[source.Index] : itemName);
                    ShowIcons(true);
                    break;
                }
                case CachedSnapshot.SourceIndex.SourceId.NativeAllocation:
                {
                    var rootReference = cs.NativeAllocations.RootReferenceId[source.Index];
                    if (rootReference <= CachedSnapshot.NativeRootReferenceEntriesCache.InvalidRootId
                        || !cs.NativeRootReferences.IdToIndex.TryGetValue(rootReference, out var rootIndex)
                        || rootIndex == CachedSnapshot.NativeRootReferenceEntriesCache.InvalidRootIndex)
                    {
                        // Only set the long error message outside of table views
                        if (!string.IsNullOrEmpty(itemName))
                            SetLabelData(cs, itemName, string.Empty);
                        else
                            SetLabelData(cs, TextContent.UnrootedAllocationLabel, string.Empty);
                        return;
                    }
                    var typeName = cs.NativeRootReferences.AreaName[rootIndex];
                    var objectName = cs.NativeRootReferences.ObjectName[rootIndex];
                    objectName += " " + NativeAllocationTools.ProduceNativeAllocationName(source, cs, truncateTypeNames: true);
                    SetLabelData(cs, typeName, objectName);
                    ShowIcons(true);
                    break;
                }
                case CachedSnapshot.SourceIndex.SourceId.GfxResource:
                {
                    var od = ObjectData.FromGfxResourceIndex(cs, (int)source.Index);
                    ShowIcons(true);
                    SetLabelData(cs, new UnifiedUnityObjectInfo(cs, od));
                    if (!string.IsNullOrEmpty(itemName))
                        NativeObjectName = itemName;
                    break;
                }
                case CachedSnapshot.SourceIndex.SourceId.GCHandleIndex:
                {
                    if (string.IsNullOrEmpty(itemName))
                        SetLabelData(cs, itemName, string.Empty, DataType.GCHandle);
                    else
                        SetLabelData(cs, TextContent.GCHandleTypeName, source.GetName(cs), DataType.GCHandle);
                    ShowIcons(true);
                    break;
                }
                case CachedSnapshot.SourceIndex.SourceId.Prefab:
                    SetStructuralLabelData(cs, source, itemName, Icons.PrefabIconContent, TextContent.ObjectStatusPrefabRoot);
                    break;
                case CachedSnapshot.SourceIndex.SourceId.Scene:
                    SetStructuralLabelData(cs, source, itemName, Icons.SceneIconContent, TextContent.ObjectStatusSceneRoot);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Prefab and Scene entries are structural book-keeping units (a Prefab stands in for its root
        /// Transform, a Scene for its root objects) and have no managed/native type of their own. Show their
        /// name with the matching editor icon rather than treating them as an untyped, icon-less entry.
        /// </summary>
        void SetStructuralLabelData(CachedSnapshot cs, CachedSnapshot.SourceIndex source, string itemName, GUIContent icon, string typeName)
        {
            InitializeIfNeeded();
            ShowIcons(true);
            m_TypeIcon.image = icon?.image;
            m_TypeIcon.tooltip = typeName;
            Type = DataType.NativeUnityType;
            m_SourceIndex = source;

            var name = string.IsNullOrEmpty(itemName) ? source.GetName(cs) : itemName;

            using var _ = new DelayedLabelUpdateScope(this);
            ManagedTypeName = string.Empty;
            NativeTypeName = string.Empty;
            NativeObjectName = name;
        }

        void InitializeIfNeeded()
        {
            if (!m_NeedsDisposal)
            {
                m_NeedsDisposal = true;
                MemoryProfilerSettings.TruncateStateChanged += OnTruncateStateChanged;
            }
        }

        public void Dispose()
        {
            if (m_NeedsDisposal)
            {
                m_NeedsDisposal = false;
                MemoryProfilerSettings.TruncateStateChanged -= OnTruncateStateChanged;
            }
        }

        void OnTruncateStateChanged()
        {
            // re-set type name so it gets properly updated (truncated or untruncated) via the properties
            ManagedTypeName = m_ManagedTypeName;
            NativeTypeName = m_NativeTypeName;
            NativeObjectName = m_NativeObjectName;
        }


#if !UNITY_6000_0_OR_NEWER
        /// <summary>
        /// Instantiates a <see cref="ObjectOrTypeLabel"/> using the data read from a UXML file.
        /// </summary>
        public new class UxmlFactory : UxmlFactory<ObjectOrTypeLabel, UxmlTraits> { }

        /// <summary>
        /// Defines <see cref="UxmlTraits"/> for the <see cref="ObjectOrTypeLabel"/>.
        /// </summary>
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlEnumAttributeDescription<DataType> m_DataType = new UxmlEnumAttributeDescription<DataType> { name = "data-type", defaultValue = DataType.UnifiedUnityObject };
            UxmlStringAttributeDescription m_NativeTypeName = new UxmlStringAttributeDescription { name = "native-type-name", defaultValue = "GameObject" };
            UxmlStringAttributeDescription m_ManagedTypeName = new UxmlStringAttributeDescription { name = "managed-type-name", defaultValue = "UnityEngine.GameObject" };
            UxmlStringAttributeDescription m_NativeObjectName = new UxmlStringAttributeDescription { name = "managed-object-name", defaultValue = "Cube" };

            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
            {
                get
                {
                    // can't contain anything
                    yield break;
                }
            }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var objectOrTypeLabel = ((ObjectOrTypeLabel)ve);
                var dataType = m_DataType.GetValueFromBag(bag, cc);
                var nativeType = m_NativeTypeName.GetValueFromBag(bag, cc);
                var nativeObjectName = m_NativeObjectName.GetValueFromBag(bag, cc);
                var managedType = m_ManagedTypeName.GetValueFromBag(bag, cc);

                objectOrTypeLabel.Type = dataType;
                objectOrTypeLabel.ManagedTypeName = managedType;
                objectOrTypeLabel.NativeObjectName = nativeObjectName;
                objectOrTypeLabel.NativeTypeName = nativeType;
            }
        }
#endif
    }
}

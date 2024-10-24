using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UI.PathsToRoot;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
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

            LeakedShell,
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
                    case DataType.UnifiedUnityType:
                    case DataType.UnifiedUnityObject:
                        m_DataTypeIcon.AddToClassList(k_ClassDataTypeUnified);
                        m_DataTypeIcon.tooltip = TextContent.DataTypeUnifiedUnityTooltip;
                        break;
                    case DataType.LeakedShell:
                        m_DataTypeIcon.AddToClassList(k_ClassDataTypeLeakedShell);
                        m_DataTypeIcon.tooltip = TextContent.DataTypeLeakedShellTooltip;
                        break;
                    default:
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
                        Type = DataType.NativeObject;
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

        public Action<ContextualMenuPopulateEvent> ContextMenuOpening = delegate { };

        public override VisualElement contentContainer
        {
            get { return null; }
        }

        VisualElement m_Root;
        VisualElement m_DataTypeIcon;
        Image m_TypeIcon;
        Label m_Label;
        Button m_DocumentationButton;

        bool m_NeedsDisposal;

        const string k_ClassIconItem = "type-icon";
        const string k_ClassDataTypeIcon = "data-type-icon";
        const string k_ClassDataTypeNative = "data-type-icon__native";
        const string k_ClassDataTypeManaged = "data-type-icon__managed";
        const string k_ClassDataTypeUnified = "data-type-icon__unified";
        const string k_ClassDataTypeLeakedShell = "data-type-icon__leaked-shell";
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
            string text;
            if (string.IsNullOrEmpty(m_NativeObjectName))
                text = string.Empty;
            else
                text = $"\"{m_NativeObjectName}\" ";

            var managedTypeName = truncateManagedType ? PathsToRootDetailView.TruncateTypeName(m_ManagedTypeName) : m_ManagedTypeName;
            if (!string.IsNullOrEmpty(m_ManagedTypeName))
                text += managedTypeName;

            if (m_NativeTypeName != managedTypeName)
                text += (string.IsNullOrEmpty(m_ManagedTypeName) || string.IsNullOrEmpty(m_NativeTypeName) ? string.Empty : " : ") + m_NativeTypeName;

            return text;
        }

        public void SetLabelData(CachedSnapshot cs, UnifiedType typeInfo)
        {
            InitializeIfNeeded();
            GUIContent typeIcon;
            if (typeInfo.ManagedTypeIndex >= 0)
            {
                typeIcon = PathsToRoot.PathsToRootDetailTreeViewItem.GetIcon(typeInfo.ManagedTypeData, typeInfo.ManagedTypeName, cs);
                m_TypeIcon.tooltip = typeInfo.ManagedTypeName;
            }
            else
            {
                typeIcon = PathsToRoot.PathsToRootDetailTreeViewItem.GetIcon(typeInfo.ManagedTypeData /*aka invalid*/, typeInfo.NativeTypeName, cs);
                m_TypeIcon.tooltip = typeInfo.NativeTypeName;
            }
            m_TypeIcon.image = typeIcon.image;
            Type = typeInfo.IsUnifiedType ? DataType.UnifiedUnityType : (typeInfo.HasManagedType ? DataType.PureCSharpType : DataType.NativeUnityType);

            ManagedTypeName = typeInfo.ManagedTypeName;
            NativeTypeName = typeInfo.NativeTypeName;
            NativeObjectName = string.Empty;
        }

        public void SetLabelData(CachedSnapshot cs, UnifiedUnityObjectInfo unityObjectInfo)
        {
            InitializeIfNeeded();
            GUIContent typeIcon;
            if (unityObjectInfo.HasManagedSide)
            {
                typeIcon = PathsToRoot.PathsToRootDetailTreeViewItem.GetIcon(unityObjectInfo.ManagedObjectData, unityObjectInfo.ManagedTypeName, cs);
                m_TypeIcon.tooltip = unityObjectInfo.ManagedTypeName;
            }
            else
            {
                typeIcon = PathsToRoot.PathsToRootDetailTreeViewItem.GetIcon(unityObjectInfo.NativeObjectData, unityObjectInfo.NativeTypeName, cs);
                m_TypeIcon.tooltip = unityObjectInfo.NativeTypeName;
            }
            m_TypeIcon.image = typeIcon.image;

            Type = unityObjectInfo.IsFullUnityObjet ? DataType.UnifiedUnityObject : (unityObjectInfo.IsLeakedShell ? DataType.LeakedShell : DataType.NativeUnityType);

            ManagedTypeName = unityObjectInfo.ManagedTypeName;
            NativeTypeName = unityObjectInfo.NativeTypeName;
            NativeObjectName = unityObjectInfo.NativeObjectName;
        }

        public void SetLabelData(CachedSnapshot cs, string typeName, string objectName)
        {
            InitializeIfNeeded();
            m_TypeIcon.tooltip = typeName;
            m_TypeIcon.image = null;

            Type = DataType.NativeUnityType;

            ManagedTypeName = string.Empty;
            NativeTypeName = typeName;
            NativeObjectName = objectName;
        }


        public void SetLabelData(CachedSnapshot cs, ObjectData pureCSharpObject, UnifiedType typeInfo)
        {
            InitializeIfNeeded();
            var typeIcon = PathsToRoot.PathsToRootDetailTreeViewItem.GetIcon(pureCSharpObject, typeInfo.ManagedTypeName, cs);
            m_TypeIcon.tooltip = typeInfo.ManagedTypeName;
            m_TypeIcon.image = typeIcon.image;
            Type = DataType.ManagedObject;

            ManagedTypeName = typeInfo.ManagedTypeName;
            NativeTypeName = string.Empty;
            NativeObjectName = string.Empty;
        }

        void UpdateLabelContent()
        {
            if (m_Label != null)
            {
                m_Label.text = GetTitle(MemoryProfilerSettings.MemorySnapshotTruncateTypes);
                m_Label.tooltip = m_Label.text;
            }
        }

        void ShowIcons(bool show)
        {
            UIElementsHelper.SetVisibility(m_DataTypeIcon, show);
            UIElementsHelper.SetVisibility(m_TypeIcon, show);
        }

        public void SetToNoObjectSelected()
        {
            ManagedTypeName = "";
            NativeObjectName = "";
            NativeTypeName = PathsToRootDetailView.Styles.NoObjectSelected;
            m_TypeIcon.image = PathsToRootUtils.NoIconContent.image;
            UIElementsHelper.SetVisibility(m_DataTypeIcon, false);
            UIElementsHelper.SetVisibility(m_TypeIcon, true);
        }

        public void SetLabelData(CachedSnapshot cs, CachedSnapshot.SourceIndex source)
        {
            switch (source.Id)
            {
                case CachedSnapshot.SourceIndex.SourceId.ManagedObject:
                {
                    var od = ObjectData.FromManagedObjectIndex(cs, (int)source.Index);
                    ShowIcons(true);
                    SetLabelData(cs, new UnifiedUnityObjectInfo(cs, od));
                    break;
                }
                case CachedSnapshot.SourceIndex.SourceId.NativeObject:
                {
                    var od = ObjectData.FromNativeObjectIndex(cs, (int)source.Index);
                    ShowIcons(true);
                    SetLabelData(cs, new UnifiedUnityObjectInfo(cs, od));
                    break;
                }
                case CachedSnapshot.SourceIndex.SourceId.ManagedType:
                {
                    var od = ObjectData.FromManagedType(cs, (int)source.Index);
                    ShowIcons(true);
                    SetLabelData(cs, new UnifiedType(cs, od));
                    break;
                }
                case CachedSnapshot.SourceIndex.SourceId.NativeType:
                {
                    var typeInfo = new UnifiedType(cs, (int)source.Index);
                    ShowIcons(true);
                    SetLabelData(cs, typeInfo);
                    break;
                }
                case CachedSnapshot.SourceIndex.SourceId.NativeRootReference:
                {
                    var typeName = cs.NativeRootReferences.AreaName[source.Index];
                    var objectName = cs.NativeRootReferences.ObjectName[source.Index];
                    ShowIcons(true);
                    SetLabelData(cs, typeName, objectName);
                    break;
                }
                case CachedSnapshot.SourceIndex.SourceId.NativeAllocation:
                {
                    var rootReference = cs.NativeAllocations.RootReferenceId[source.Index];
                    if (rootReference <= 0)
                    {
                        SetLabelData(cs, "Unrooted Allocation - This is a bug in Unity's source code", "");
                        return;
                    }
                    var typeName = cs.NativeRootReferences.AreaName[rootReference];
                    var objectName = cs.NativeRootReferences.ObjectName[rootReference];
                    objectName += " " + NativeAllocationTools.ProduceNativeAllocationName(source, cs, truncateTypeNames: true);
                    ShowIcons(true);
                    SetLabelData(cs, typeName, objectName);
                    break;
                }
                case CachedSnapshot.SourceIndex.SourceId.GfxResource:
                {
                    var od = ObjectData.FromGfxResourceIndex(cs, (int)source.Index);
                    ShowIcons(true);
                    SetLabelData(cs, new UnifiedUnityObjectInfo(cs, od));
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
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

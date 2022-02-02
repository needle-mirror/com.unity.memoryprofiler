using System;
using System.Collections;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UI.PathsToRoot;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class ObjectOrTypeLabel : VisualElement, IDisposable
    {
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

        public string ManagedTypeName
        {
            get
            {
                if (m_ManagedTypeLabel != null)
                {
                    return m_ManagedTypeLabel.text;
                }
                return m_ManagedTypeName;
            }
            set
            {
                m_ManagedTypeName = value;
                if (m_ManagedTypeLabel != null)
                {
                    m_ManagedTypeLabel.text = MemoryProfilerSettings.MemorySnapshotTruncateTypes ? PathsToRootDetailView.TruncateTypeName(value) : value;
                }
            }
        }

        string m_NativeTypeName = string.Empty;

        public string NativeTypeName
        {
            get
            {
                if (m_NativeTypeLabel != null)
                {
                    return m_NativeTypeLabel.text;
                }
                return m_NativeTypeName;
            }
            set
            {
                m_NativeTypeName = value;
                if (m_NativeTypeLabel != null)
                {
                    if (value != m_ManagedTypeLabel.text)
                        m_NativeTypeLabel.text = (string.IsNullOrEmpty(m_ManagedTypeName) || string.IsNullOrEmpty(value) ? string.Empty : " : ") + value;
                    else
                        m_NativeTypeLabel.text = string.Empty;
                }
            }
        }

        string m_NativeObjectName = string.Empty;

        public string NativeObjectName
        {
            get
            {
                if (m_NativeObjectNameLabel != null)
                {
                    return m_NativeObjectNameLabel.text;
                }
                return m_NativeObjectName;
            }
            set
            {
                m_NativeObjectName = value;
                if (m_NativeObjectNameLabel != null)
                {
                    if (string.IsNullOrEmpty(value))
                        m_NativeObjectNameLabel.text = string.Empty;
                    else
                        m_NativeObjectNameLabel.text = $"\"{value}\"";
                }
            }
        }

        // Idea for later: jump to documentation for native Unity Types, or Unity Objects with type names in Unity owned namespaces?
        //string m_DocumentationLink = null;
        //public string DocumentationLink
        //{
        //    get { return m_DocumentationLink; }
        //    set
        //    {
        //        if (m_DocumentationLink == value)
        //            return;
        //        m_DocumentationLink = value;
        //        UIElementsHelper.SetVisibility(m_DocumentationButton, !string.IsNullOrEmpty(m_DocumentationLink));
        //    }
        //}

        public override VisualElement contentContainer
        {
            get { return null; }
        }

        VisualElement m_Root;
        VisualElement m_DataTypeIcon;
        Image m_TypeIcon;
        Label m_ManagedTypeLabel;
        Label m_NativeTypeLabel;
        Label m_NativeObjectNameLabel;
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
            var objectOrTypeLabelViewTree = AssetDatabase.LoadAssetAtPath(ResourcePaths.ObjectOrTypeLabelUxmlPath, typeof(VisualTreeAsset)) as VisualTreeAsset;

            m_Root = objectOrTypeLabelViewTree.Clone();

            // clear out the style sheets defined in the template uxml file so they can be applied from here in the order of: 1. theming, 2. base
            m_Root.styleSheets.Clear();
            var themeStyle = AssetDatabase.LoadAssetAtPath(EditorGUIUtility.isProSkin ? ResourcePaths.WindowDarkStyleSheetPath : ResourcePaths.WindowLightStyleSheetPath, typeof(StyleSheet)) as StyleSheet;
            m_Root.styleSheets.Add(themeStyle);

            var windowStyle = AssetDatabase.LoadAssetAtPath(ResourcePaths.WindowCommonStyleSheetPath, typeof(StyleSheet)) as StyleSheet;
            m_Root.styleSheets.Add(windowStyle);

            hierarchy.Add(m_Root);

            this.AddManipulator(new ContextualMenuManipulator((binder) => PopulateOptionMenu(binder)));

            style.flexShrink = 0;

            m_DataTypeIcon = m_Root.Q("object-or-type-label__data-type-icon");
            m_TypeIcon = m_Root.Q<Image>("object-or-type-label__type-icon");
            m_ManagedTypeLabel = m_Root.Q<Label>("object-or-type-label__managed-type-name");
            m_NativeTypeLabel = m_Root.Q<Label>("object-or-type-label__native-type-name");
            m_NativeObjectNameLabel = m_Root.Q<Label>("object-or-type-label__native-object-name");
            m_DocumentationButton = m_Root.Q<Button>("object-or-type-label__documentation-button");
            //m_DocumentationButton.tooltip = TextContent.OpenManualTooltip;
            //m_DocumentationButton.clickable.clicked += OpenDocumentation;

            UIElementsHelper.SetVisibility(m_DocumentationButton, false /*!string.IsNullOrEmpty(m_DocumentationLink)*/);

            Setup();
        }

        void PopulateOptionMenu(ContextualMenuPopulateEvent binder)
        {
            binder.menu.AppendAction(TextContent.TruncateTypeName, (a) =>
            {
                MemoryProfilerSettings.ToggleTruncateTypes();
            }, MemoryProfilerSettings.MemorySnapshotTruncateTypes ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
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
            Type = typeInfo.IsUnifiedtyType ? DataType.UnifiedUnityType : (typeInfo.HasManagedType ? DataType.PureCSharpType : DataType.NativeUnityType);

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

        void Init()
        {
            Setup();
        }

        void Setup()
        {
            //UIElementsHelper.SetVisibility(m_DocumentationButton, !string.IsNullOrEmpty(m_DocumentationLink));
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

        //void OpenDocumentation()
        //{
        //    Application.OpenURL(DocumentationLink);
        //}

        /// <summary>
        /// Instantiates a <see cref="ObjectOrTypeLabel"/> using the data read from a UXML file.
        /// </summary>
        public new class UxmlFactory : UxmlFactory<ObjectOrTypeLabel, UxmlTraits> {}

        /// <summary>
        /// Defines <see cref="UxmlTraits"/> for the <see cref="ObjectOrTypeLabel"/>.
        /// </summary>
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlEnumAttributeDescription<DataType> m_DataType = new UxmlEnumAttributeDescription<DataType> { name = "data-type", defaultValue = DataType.UnifiedUnityObject };
            UxmlStringAttributeDescription m_NativeTypeName = new UxmlStringAttributeDescription { name = "native-type-name", defaultValue = "GameObject" };
            UxmlStringAttributeDescription m_ManagedTypeName = new UxmlStringAttributeDescription { name = "managed-type-name", defaultValue = "UnityEngine.GameObject" };
            UxmlStringAttributeDescription m_NativeObjectName = new UxmlStringAttributeDescription { name = "managed-object-name", defaultValue = "Cube" };
            //UxmlStringAttributeDescription m_DocumentationLink = new UxmlStringAttributeDescription { name = "documentation-link", defaultValue = null };
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
                //var docLink = m_DocumentationLink.GetValueFromBag(bag, cc);

                objectOrTypeLabel.Type = dataType;
                objectOrTypeLabel.ManagedTypeName = managedType;
                objectOrTypeLabel.NativeObjectName = nativeObjectName;
                objectOrTypeLabel.NativeTypeName = nativeType;
                //objectOrTypeLabel.DocumentationLink = docLink;

                objectOrTypeLabel.Init();
            }
        }
    }
}

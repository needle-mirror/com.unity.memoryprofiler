using System;
using System.Collections;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class InfoBox : VisualElement
    {
        IssueLevel m_IssueLevel = IssueLevel.Info;
        public IssueLevel IssueLevel
        {
            get { return m_IssueLevel; }
            set
            {
                m_IssueLevel = value;
                m_Icon.ClearClassList();
                m_Icon.AddToClassList(k_ClassIconItem);
                switch (value)
                {
                    case IssueLevel.Info:
                        m_Icon.AddToClassList(k_ClassIconTypeInfo);
                        break;
                    case IssueLevel.Warning:
                        m_Icon.AddToClassList(k_ClassIconTypeWarning);
                        break;
                    case IssueLevel.Error:
                        m_Icon.AddToClassList(k_ClassIconTypeError);
                        break;
                    default:
                        throw new NotImplementedException(value.ToString());
                }
            }
        }

        string m_MessageContent = string.Empty;

        public string Message
        {
            get
            {
                if (m_Message != null)
                {
                    return m_Message.text;
                }
                return m_MessageContent;
            }
            set
            {
                m_MessageContent = value;
                if (m_Message != null)
                {
                    m_Message.text = value;
                }
            }
        }

        string m_DocumentationLink = null;
        public string DocumentationLink
        {
            get { return m_DocumentationLink; }
            set
            {
                if (m_DocumentationLink == value)
                    return;
                m_DocumentationLink = value;
                UIElementsHelper.SetVisibility(m_DocumentationButton, !string.IsNullOrEmpty(m_DocumentationLink));
            }
        }

        public override VisualElement contentContainer
        {
            get { return null; }
        }

        VisualElement m_Root;
        VisualElement m_Icon;
        Label m_Message;
        Button m_DocumentationButton;

        const string k_ClassIconItem = "info-box__icon";
        const string k_ClassIconTypeInfo = "info-box__info-icon";
        const string k_ClassIconTypeWarning = "info-box__warning-icon";
        const string k_ClassIconTypeError = "info-box__error-icon";

        public InfoBox()
        {
            var infoBoxViewTree = AssetDatabase.LoadAssetAtPath(ResourcePaths.InfoBoxUxmlPath, typeof(VisualTreeAsset)) as VisualTreeAsset;

            m_Root = infoBoxViewTree.Clone();

            // clear out the style sheets defined in the template uxml file so they can be applied from here in the order of: 1. theming, 2. base
            m_Root.styleSheets.Clear();
            var themeStyle = AssetDatabase.LoadAssetAtPath(EditorGUIUtility.isProSkin ? ResourcePaths.WindowDarkStyleSheetPath : ResourcePaths.WindowLightStyleSheetPath, typeof(StyleSheet)) as StyleSheet;
            m_Root.styleSheets.Add(themeStyle);

            var windowStyle = AssetDatabase.LoadAssetAtPath(ResourcePaths.WindowCommonStyleSheetPath, typeof(StyleSheet)) as StyleSheet;
            m_Root.styleSheets.Add(windowStyle);

            hierarchy.Add(m_Root);

            style.flexShrink = 1;

            m_Icon = m_Root.Q("info-box__icon");
            m_Message = m_Root.Q<Label>("info-box__message-text");
            m_DocumentationButton = m_Root.Q<Button>("info-box__documentation-button");
            m_DocumentationButton.tooltip = TextContent.OpenManualTooltip;
            m_DocumentationButton.clickable.clicked += OpenDocumentation;

            UIElementsHelper.SetVisibility(m_DocumentationButton, !string.IsNullOrEmpty(m_DocumentationLink));

            Setup();
        }

        void Init()
        {
            Setup();
        }

        void Setup()
        {
            UIElementsHelper.SetVisibility(m_DocumentationButton, !string.IsNullOrEmpty(m_DocumentationLink));
        }

        void OpenDocumentation()
        {
            Application.OpenURL(DocumentationLink);
        }

        /// <summary>
        /// Instantiates a <see cref="InfoBox"/> using the data read from a UXML file.
        /// </summary>
        public new class UxmlFactory : UxmlFactory<InfoBox, UxmlTraits> {}

        /// <summary>
        /// Defines <see cref="UxmlTraits"/> for the <see cref="InfoBox"/>.
        /// </summary>
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlEnumAttributeDescription<IssueLevel> m_IssueLevel = new UxmlEnumAttributeDescription<IssueLevel> { name = "issue-level", defaultValue = IssueLevel.Info };
            UxmlStringAttributeDescription m_Message = new UxmlStringAttributeDescription { name = "message", defaultValue = "Info about something..." };
            UxmlStringAttributeDescription m_DocumentationLink = new UxmlStringAttributeDescription { name = "documentation-link", defaultValue = null };
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
                var infoBox = ((InfoBox)ve);
                var issueLevel = m_IssueLevel.GetValueFromBag(bag, cc);
                var message = m_Message.GetValueFromBag(bag, cc);
                var docLink = m_DocumentationLink.GetValueFromBag(bag, cc);

                infoBox.IssueLevel = issueLevel;
                infoBox.Message = message;
                infoBox.DocumentationLink = docLink;

                infoBox.Init();
            }
        }
    }
}

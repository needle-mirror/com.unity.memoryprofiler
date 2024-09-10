using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
#if UNITY_6000_0_OR_NEWER
    [UxmlElement]
#endif
    internal partial class InfoBox : VisualElement
    {
        const string k_UxmlAssetGuid = "3212e6591d8f2cf4d86dcc1b3687cf9d";

        public enum IssueType
        {
            Info,
            Warning,
            Error
        }

        IssueType m_IssueLevel = IssueType.Info;

#if UNITY_6000_0_OR_NEWER
        [UxmlAttribute]
#endif
        public IssueType IssueLevel
        {
            get { return m_IssueLevel; }
            set
            {
                m_IssueLevel = value;
                m_Icon.ClearClassList();
                m_Icon.AddToClassList(k_ClassIconItem);
                switch (value)
                {
                    case IssueType.Info:
                        m_Icon.AddToClassList(k_ClassIconTypeInfo);
                        break;
                    case IssueType.Warning:
                        m_Icon.AddToClassList(k_ClassIconTypeWarning);
                        break;
                    case IssueType.Error:
                        m_Icon.AddToClassList(k_ClassIconTypeError);
                        break;
                    default:
                        throw new NotImplementedException(value.ToString());
                }
            }
        }

        string m_MessageContent = string.Empty;

#if UNITY_6000_0_OR_NEWER
        [UxmlAttribute]
#endif
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

#if UNITY_6000_0_OR_NEWER
        [UxmlAttribute]
#endif
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
            // Construct from a template
            m_Root = UIElementsHelper.InstantiateAssetByGUID(k_UxmlAssetGuid);

            // Setup hierarchy
            hierarchy.Add(m_Root);
            style.flexShrink = 1;

            // Gather references & setup
            m_Icon = m_Root.Q("info-box__icon");
            m_Message = m_Root.Q<Label>("info-box__message-text");
            m_DocumentationButton = m_Root.Q<Button>("info-box__documentation-button");
            m_DocumentationButton.tooltip = TextContent.OpenManualTooltip;
            m_DocumentationButton.clickable.clicked += OpenDocumentation;

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

#if !UNITY_6000_0_OR_NEWER
        /// <summary>
        /// Instantiates a <see cref="InfoBox"/> using the data read from a UXML file.
        /// </summary>
        public new class UxmlFactory : UxmlFactory<InfoBox, UxmlTraits> { }

        /// <summary>
        /// Defines <see cref="UxmlTraits"/> for the <see cref="InfoBox"/>.
        /// </summary>
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlEnumAttributeDescription<IssueType> m_IssueLevel = new UxmlEnumAttributeDescription<IssueType> { name = "issue-level", defaultValue = IssueType.Info };
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
#endif
    }
}

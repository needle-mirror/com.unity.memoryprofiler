using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    public enum IssueLevel
    {
        Info,
        Warning,
        Error,
    }
    internal class TopIssueItem : VisualElement, IComparable<TopIssueItem>
    {
        static class Styles
        {
            public const string RootClass = "top-ten-issues-section__list__item";
            public const string FoldoutClass = "top-ten-issues-section__list__item__foldout";
            public const string FoldoutContentClass = "top-ten-issues-section__list__item__foldout-content";
            public const string FoldoutToggleClass = "top-ten-issues-section__list__item__foldout-toggle";
            public const string IconClass = "issue-icon";
            public const string DetailsMessageClass = "top-ten-issues-section__list__item__details";
            public const string DocumentationButtonClass = "top-ten-issues-section__list__item__details__documentation-button";
            public const string InvestigateButtonClass = "top-ten-issues-section__list__item__details__investigate-button";

            public const string InfoClass = "issue-icon__info-icon";
            public const string WarningClass = "issue-icon__warning-icon";
            public const string ErrorClass = "issue-icon__error-icon";
        }

        public string Message { get; private set; }

        IssueLevel m_IssueLevel = IssueLevel.Warning;
        public IssueLevel IssueLevel
        {
            get { return m_IssueLevel; }
            private set
            {
                if (m_IssueLevel == value)
                    return;
                if (m_Icon != null)
                {
                    switch (m_IssueLevel)
                    {
                        case IssueLevel.Info:
                            m_Icon.RemoveFromClassList(Styles.InfoClass);
                            break;
                        case IssueLevel.Warning:
                            m_Icon.RemoveFromClassList(Styles.WarningClass);
                            break;
                        case IssueLevel.Error:
                            m_Icon.RemoveFromClassList(Styles.ErrorClass);
                            break;
                        default:
                            break;
                    }
                    switch (value)
                    {
                        case IssueLevel.Info:
                            m_Icon.AddToClassList(Styles.InfoClass);
                            break;
                        case IssueLevel.Warning:
                            m_Icon.AddToClassList(Styles.WarningClass);
                            break;
                        case IssueLevel.Error:
                            m_Icon.AddToClassList(Styles.ErrorClass);
                            break;
                        default:
                            break;
                    }
                }
                m_IssueLevel = value;
            }
        }

        public float Priority { get; private set; }
        public string Details { get; private set; }

        string m_DocumentationLink = null;
        public string DocumentationLink
        {
            get { return m_DocumentationLink; }
            private set
            {
                if (m_DocumentationLink == value)
                    return;
                m_DocumentationLink = value;
                UIElementsHelper.SetVisibility(m_DocumentationButton, !string.IsNullOrEmpty(m_DocumentationLink));
            }
        }

        Action m_InvestigateAction = null;
        public Action InvestigateAction
        {
            get { return m_InvestigateAction; }
            private set
            {
                if (m_InvestigateAction == value)
                    return;
                m_InvestigateAction = value;
                UIElementsHelper.SetVisibility(m_InvestigateButton, m_InvestigateAction != null);
            }
        }

        VisualElement m_Icon;
        Foldout m_Foldout;
        Label m_DetailsLabel;
        Button m_DocumentationButton;
        Button m_InvestigateButton;

        public override VisualElement contentContainer
        {
            get { return null; }
        }

        public TopIssueItem(string messageText, string details = null, IssueLevel issueLevel = IssueLevel.Warning, float priority = 10f, string documentationLink = null, Action investigateAction = null) : this()
        {
            Init(messageText, details, issueLevel, priority, documentationLink, investigateAction);
        }

        public TopIssueItem() : base()
        {
            AddToClassList(Styles.RootClass);

            m_Icon = new VisualElement();
            m_Icon.AddToClassList(Styles.IconClass);
            m_Icon.AddToClassList(Styles.WarningClass);
            hierarchy.Add(m_Icon);

            m_Foldout = new Foldout();
            m_Foldout.AddToClassList(Styles.FoldoutClass);
            hierarchy.Add(m_Foldout);
            m_Foldout.Q("unity-content").AddToClassList(Styles.FoldoutContentClass);
            m_Foldout.value = false;
            m_Foldout.RegisterValueChangedCallback((evt) =>
                MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInPage, MemoryProfilerAnalytics.PageInteractionType>(
                    evt.newValue ? MemoryProfilerAnalytics.PageInteractionType.ATopIssueWasRevealed : MemoryProfilerAnalytics.PageInteractionType.ATopIssueWasHidden));

            this.Q<Toggle>("", "unity-foldout__toggle").AddToClassList(Styles.FoldoutToggleClass);

            m_DetailsLabel = new Label();
            m_DetailsLabel.AddToClassList(Styles.DetailsMessageClass);
            m_Foldout.Add(m_DetailsLabel);

            m_InvestigateButton = new Button(Investigate) { text = "Investigate" };
            m_InvestigateButton.AddToClassList(Styles.InvestigateButtonClass);
            m_Foldout.Add(m_InvestigateButton);
            UIElementsHelper.SetVisibility(m_InvestigateButton, m_InvestigateAction != null);

            m_DocumentationButton = new Button(OpenDocumentation) { tooltip = TextContent.OpenManualTooltip };
            m_DocumentationButton.AddToClassList(Styles.DocumentationButtonClass);
            m_DocumentationButton.AddToClassList(GeneralStyles.HelpIconButtonClass);
            m_DocumentationButton.AddToClassList(GeneralStyles.IconButtonClass);
            m_Foldout.Add(m_DocumentationButton);
            UIElementsHelper.SetVisibility(m_DocumentationButton, !string.IsNullOrEmpty(m_DocumentationLink));
        }

        void Investigate()
        {
            if (m_InvestigateAction != null)
            {
                m_InvestigateAction();
                MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInPage, MemoryProfilerAnalytics.PageInteractionType>(
                    MemoryProfilerAnalytics.PageInteractionType.ATopIssueInvestigateButtonWasClicked);
            }
        }

        void OpenDocumentation()
        {
            Application.OpenURL(DocumentationLink);
            MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInPage, MemoryProfilerAnalytics.PageInteractionType>(
                MemoryProfilerAnalytics.PageInteractionType.ATopIssueDocumentationButtonWasClicked);
        }

        void Init(string messageText, string details = null, IssueLevel issueLevel = IssueLevel.Warning, float priority = 10f, string documentationLink = null, Action investigateAction = null)
        {
            IssueLevel = issueLevel;
            Message = messageText;
            Details = details;
            Priority = priority;
            DocumentationLink = documentationLink;
            InvestigateAction = investigateAction;

            m_Foldout.text = messageText;
            m_DetailsLabel.text = details;
            Priority = priority;
        }

        public int CompareTo(TopIssueItem other)
        {
            int order = ((int)IssueLevel).CompareTo((int)other.IssueLevel);

            if (order == 0)
            {
                order = Priority.CompareTo(other.Priority);
            }
            if (order == 0)
            {
                order = Message.CompareTo(other.Message);
            }
            return order;
        }

        /// <summary>
        /// Instantiates a <see cref="TopIssueItem"/> using the data read from a UXML file.
        /// </summary>
        public new class UxmlFactory : UxmlFactory<TopIssueItem, UxmlTraits> {}

        /// <summary>
        /// Defines <see cref="UxmlTraits"/> for the <see cref="TopIssueItem"/>.
        /// </summary>
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlStringAttributeDescription m_Message = new UxmlStringAttributeDescription { name = "message", defaultValue = "Memory usage is too high." };
            UxmlStringAttributeDescription m_Details = new UxmlStringAttributeDescription { name = "details", defaultValue = "Lower Memory usage is always better." };
            UxmlStringAttributeDescription m_DocumentationLink = new UxmlStringAttributeDescription { name = "documentation-link", defaultValue = "" };
            UxmlEnumAttributeDescription<IssueLevel> m_IssueLevel = new UxmlEnumAttributeDescription<IssueLevel> { name = "issue-level", defaultValue = IssueLevel.Warning };
            UxmlFloatAttributeDescription m_Priority = new UxmlFloatAttributeDescription { name = "priority", defaultValue = 10f };

            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
            {
                get
                {
                    // Can't contain anything
                    yield break;
                }
            }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var message = m_Message.GetValueFromBag(bag, cc);
                var details = m_Details.GetValueFromBag(bag, cc);
                var docuemntationLink = m_DocumentationLink.GetValueFromBag(bag, cc);
                var issueLevel = m_IssueLevel.GetValueFromBag(bag, cc);
                var priority = m_Priority.GetValueFromBag(bag, cc);

                ((TopIssueItem)ve).Init(message, details, issueLevel, priority, docuemntationLink);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class SimpleDetailsViewController : ViewController
    {
        const string k_UxmlAssetGuid = "dce00b5b2cee4394b9ffaceac3da7925";
        const string k_UxmlIdentifierTitle = "memory-profiler-simple-details__title";
        const string k_UxmlIdentifierDescription = "memory-profiler-simple-details__desc__text";
        const string k_UxmlIdentifierDocumentationButton = "memory-profiler-simple-details__desc__documentation-link-button";

        // State
        string m_Title;
        string m_Description;
        string m_DocumentationURL;
        readonly Action m_InspectAction;
        readonly string m_InspectLabel;
        readonly string m_InspectTooltip;

        // View
        Label m_TitleLabel;
        TextField m_DescriptionText;
        Button m_DocumentationButton;
        Button m_InspectButton;

        public SimpleDetailsViewController(string title, string description, string documentationUrl,
            MemorySummaryRowActionProvider rowActionProvider = null, MemorySummaryModel.Row row = default)
        {
            m_Title = title;
            m_Description = description;
            m_DocumentationURL = documentationUrl;
            m_InspectAction = rowActionProvider?.GetAction(row);
            m_InspectLabel = rowActionProvider?.Label;
            m_InspectTooltip = rowActionProvider?.Tooltip;
        }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");

            GatherReferencesInView(view);

            return view;
        }

        protected override void ViewLoaded()
        {
            base.ViewLoaded();
            RefreshView();
        }

        void GatherReferencesInView(VisualElement view)
        {
            m_TitleLabel = view.Q<Label>(k_UxmlIdentifierTitle);
            m_DescriptionText = view.Q<TextField>(k_UxmlIdentifierDescription);
            m_DocumentationButton = view.Q<Button>(k_UxmlIdentifierDocumentationButton);
        }

        void RefreshView()
        {
            m_TitleLabel.text = m_Title;

            m_DescriptionText.value = m_Description;
            m_DescriptionText.isReadOnly = true;
            m_DocumentationButton.clickable.clicked += OpenDocumentation;
            m_DocumentationButton.tooltip = UIContentData.TextContent.OpenManualTooltip;
            UIElementsHelper.SetVisibility(m_DocumentationButton, !string.IsNullOrEmpty(m_DocumentationURL));

            if (m_InspectAction != null && !string.IsNullOrEmpty(m_InspectLabel))
            {
                m_InspectButton = new Button(m_InspectAction) { text = m_InspectLabel, tooltip = m_InspectTooltip };
                var container = m_DocumentationButton.parent ?? View;
                container.Add(m_InspectButton);
            }
        }

        void OpenDocumentation()
        {
            Application.OpenURL(m_DocumentationURL);

            // Track documentation open count in the details view
            MemoryProfilerAnalytics.AddSelectionDetailsViewInteraction(MemoryProfilerAnalytics.SelectionDetailsViewInteraction.DocumentationOpenCount);
        }
    }
}

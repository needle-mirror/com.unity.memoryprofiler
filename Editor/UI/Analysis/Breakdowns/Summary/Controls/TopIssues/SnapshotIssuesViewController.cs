using System;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class SnapshotIssuesViewController : ViewController
    {
        const string k_UxmlAssetGuid = "d13df8c4e2cc4ae419c8a909d99fe77f";

        const string k_FoldoutClass = "snapshot-issues__issue__foldout";
        const string k_DetailsMessageClass = "snapshot-issues__issue__details";

        const string k_IconClass = "snapshot-issues__issue-icon";
        const string k_IconInfoClass = "snapshot-issues__issue-icon__info-icon";
        const string k_IconWarningClass = "snapshot-issues__issue-icon__warn-icon";
        const string k_IconErrorClass = "snapshot-issues__issue-icon__error-icon";

        // Model.
        readonly SnapshotIssuesModel m_Model;

        // View
        VisualElement m_IssuesContainer;

        public SnapshotIssuesViewController(SnapshotIssuesModel model)
        {
            m_Model = model;
        }

        public bool HasIssues
        {
            get
            {
                return m_Model.Issues.Count > 0;
            }
        }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");

            return view;
        }

        protected override void ViewLoaded()
        {
            GatherViewReferences();
            RefreshView();
        }

        void GatherViewReferences()
        {
            m_IssuesContainer = View.Q("snapshot-issues__list");
        }

        void RefreshView()
        {
            m_IssuesContainer.Clear();
            foreach (var issue in m_Model.Issues)
            {
                MakeItem(m_IssuesContainer, issue);
            }
        }

        void MakeItem(VisualElement root, SnapshotIssuesModel.Issue issue)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;

            var icon = new VisualElement();
            icon.AddToClassList(k_IconClass);
            switch (issue.IssueLevel)
            {
                case SnapshotIssuesModel.IssueLevel.Error:
                    icon.AddToClassList(k_IconErrorClass);
                    break;
                case SnapshotIssuesModel.IssueLevel.Warning:
                    icon.AddToClassList(k_IconWarningClass);
                    break;
                default:
                    icon.AddToClassList(k_IconInfoClass);
                    break;
            }
            container.Add(icon);

            var foldout = new Foldout();
            foldout.AddToClassList(k_FoldoutClass);
            foldout.value = false;
            foldout.text = issue.Summary;
            container.Add(foldout);

            var detailsLabel = new Label();
            detailsLabel.AddToClassList(k_DetailsMessageClass);
            detailsLabel.text = issue.Details;
            foldout.Add(detailsLabel);

            root.Add(container);
        }
    }
}

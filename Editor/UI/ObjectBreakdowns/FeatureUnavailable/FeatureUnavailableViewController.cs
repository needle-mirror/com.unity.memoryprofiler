using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class FeatureUnavailableViewController : ViewController
    {
        const string k_UxmlIdentifier_DescriptionLabel = "feature-unavailable-view__description-label";

        // Data.
        readonly string m_Description;

        // View.
        InfoBox m_InfoBox;

        public FeatureUnavailableViewController(string description)
        {
            m_Description = description;
        }

        protected override VisualElement LoadView()
        {
            var infobox = new InfoBox() { name = k_UxmlIdentifier_DescriptionLabel };
            infobox.IssueLevel = IssueLevel.Info;
            infobox.Message = m_Description;

            return infobox;
        }

        protected override void ViewLoaded()
        {
            GatherViewReferences();
            m_InfoBox.Message = m_Description;
        }

        void GatherViewReferences()
        {
            m_InfoBox = View.Q<InfoBox>(k_UxmlIdentifier_DescriptionLabel);
        }
    }
}

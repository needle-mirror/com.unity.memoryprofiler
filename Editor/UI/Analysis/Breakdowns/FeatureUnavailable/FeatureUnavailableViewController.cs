using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class FeatureUnavailableViewController : ViewController
    {
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
            m_InfoBox = new InfoBox()
            {
                IssueLevel = InfoBox.IssueType.Info
            };

            return m_InfoBox;
        }

        protected override void ViewLoaded()
        {
            m_InfoBox.Message = m_Description;
        }
    }
}

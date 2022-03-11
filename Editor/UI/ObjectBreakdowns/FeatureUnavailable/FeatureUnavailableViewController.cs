using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class FeatureUnavailableViewController : ViewController
    {
        const string k_UxmlAssetGuid = "1944351f5c342df42b601336568b179f";
        const string k_UssAssetGuid = "f04268068498c2c45bb35e7ed40b5254";
        const string k_UxmlIdentifier_DescriptionLabel = "feature-unavailable-view__description-label";

        // Data.
        readonly string m_Description;

        // View.
        Label m_DescriptionLabel;

        public FeatureUnavailableViewController(string description)
        {
            m_Description = description;
        }

        protected override VisualElement LoadView()
        {
            var uxmlAssetPath = AssetDatabase.GUIDToAssetPath(k_UxmlAssetGuid);
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlAssetPath);
            var view = uxml.CloneTree();
            view.style.flexGrow = 1;

            // Unity 2019 does not support the <Style> element, so we must load the USS manually.
            var ussAssetPath = AssetDatabase.GUIDToAssetPath(k_UssAssetGuid);
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussAssetPath);
            view.styleSheets.Add(uss);

            return view;
        }

        protected override void ViewLoaded()
        {
            GatherViewReferences();
            m_DescriptionLabel.text = m_Description;
        }

        void GatherViewReferences()
        {
            m_DescriptionLabel = View.Q<Label>(k_UxmlIdentifier_DescriptionLabel);
        }
    }
}

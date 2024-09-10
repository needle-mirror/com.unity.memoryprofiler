#if UNITY_2022_1_OR_NEWER
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
#if UNITY_6000_0_OR_NEWER
    [UxmlElement]
#endif
    partial class UnityObjectsDescriptionCell : VisualElement
    {
        const string k_UxmlAssetGuid = "1fec5c07542077c4d81d5cb90b89c7b3";
        const string k_UxmlIdentifier_Icon = "unity-objects-description-cell__icon";
        const string k_UxmlIdentifier_Label = "unity-objects-description-cell__label";
        const string k_UxmlIdentifier_SecondaryLabel = "unity-objects-description-cell__secondary-label";

        VisualElement m_Icon;
        Label m_Label;
        Label m_SecondaryLabel;

        public static UnityObjectsDescriptionCell Instantiate()
        {
            var cell = (UnityObjectsDescriptionCell)ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            cell.Initialize();
            return cell;
        }

        public void SetTypeName(string typeName, string managedTypeName = null)
        {
            var iconName = IconNameForType(typeName);
            var icon = IconUtility.LoadBuiltInIconWithName(iconName);
            if (icon == null)
                icon = Icons.NoIcon;
            m_Icon.style.backgroundImage = icon;
            m_Icon.tooltip = typeName;
        }

        public void SetText(string text)
        {
            m_Label.text = text;
            UIElementsHelper.SetElementDisplay(m_Label, !string.IsNullOrEmpty(text));
        }

        public void SetSecondaryText(string text)
        {
            m_SecondaryLabel.text = text;
            UIElementsHelper.SetElementDisplay(m_SecondaryLabel, !string.IsNullOrEmpty(text));
        }

        void Initialize()
        {
            m_Icon = this.Q<VisualElement>(k_UxmlIdentifier_Icon);
            m_Label = this.Q<Label>(k_UxmlIdentifier_Label);
            m_SecondaryLabel = this.Q<Label>(k_UxmlIdentifier_SecondaryLabel);
        }

        static string IconNameForType(string typeName)
        {
            // MonoBehaviour is the only built-in type that doesn't follow the naming convention of "{type-name} Icon". It uses "cs Script" instead of "MonoBehaviour".
            if (typeName.Equals("MonoBehaviour"))
                typeName = "cs Script";

            return $"{typeName} Icon";
        }

#if !UNITY_6000_0_OR_NEWER
        public new class UxmlFactory : UxmlFactory<UnityObjectsDescriptionCell> {}
#endif
    }
}
#endif

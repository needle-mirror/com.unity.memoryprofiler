#if UNITY_2022_1_OR_NEWER
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
#if UNITY_6000_0_OR_NEWER
    [UxmlElement]
#endif
    partial class AllTrackedMemoryDescriptionCell : VisualElement
    {
        const string k_UxmlAssetGuid = "d3870303fc2b9fe44955a75502a2acbe";
        const string k_UxmlIdentifier_Label = "all-tracked-memory-description-cell__label";
        const string k_UxmlIdentifier_SecondaryLabel = "all-tracked-memory-description-cell__secondary-label";

        Label m_Label;
        Label m_SecondaryLabel;

        void Initialize()
        {
            m_Label = this.Q<Label>(k_UxmlIdentifier_Label);
            m_SecondaryLabel = this.Q<Label>(k_UxmlIdentifier_SecondaryLabel);
        }

        public static AllTrackedMemoryDescriptionCell Instantiate()
        {
            var cell = (AllTrackedMemoryDescriptionCell)ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            cell.Initialize();
            return cell;
        }

        public void SetText(string text)
        {
            // UITK Label supports undocumented escape formatting
            // We need to escape all `\` to make sure that paths don't trigger it
            var _text = text.Replace("\\", "\\\\");

            m_Label.text = _text;
        }

        public void SetSecondaryText(string text)
        {
            // UITK Label supports undocumented escape formatting
            // We need to escape all `\` to make sure that paths don't trigger it
            var _text = text.Replace("\\", "\\\\");

            m_SecondaryLabel.text = _text;
        }

#if !UNITY_6000_0_OR_NEWER
        public new class UxmlFactory : UxmlFactory<AllTrackedMemoryDescriptionCell> {}
#endif
    }
}
#endif

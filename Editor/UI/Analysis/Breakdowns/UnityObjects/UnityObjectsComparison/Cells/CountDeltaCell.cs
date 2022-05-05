#if UNITY_2022_1_OR_NEWER
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class CountDeltaCell : VisualElement
    {
        const string k_UxmlAssetGuid = "196a7eaa275d6374fac3957485130321";
        const string k_UxmlIdentifier_Label = "count-delta-cell__label";
        const string k_UxmlIdentifier_Icon = "count-delta-cell__increase-icon";

        Label m_Label;
        VisualElement m_IncreaseIcon;

        public static CountDeltaCell Instantiate()
        {
            var cell = (CountDeltaCell)ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            cell.Initialize();
            return cell;
        }

        public void SetCountDelta(int countDelta)
        {
            var countText = new System.Text.StringBuilder();

            var isCountPositive = countDelta > 0;
            if (isCountPositive)
                countText.Append("+");
            countText.Append($"{countDelta:N0}");

            m_Label.text = countText.ToString();
            UIElementsHelper.SetElementDisplay(m_IncreaseIcon, isCountPositive);
        }

        void Initialize()
        {
            m_Label = this.Q<Label>(k_UxmlIdentifier_Label);
            m_IncreaseIcon = this.Q<VisualElement>(k_UxmlIdentifier_Icon);

            // Make this a cell with a darkened background. This requires quite a bit of styling to be compatible with tree view selection styling, so that is why it is its own class.
            AddToClassList("dark-tree-view-cell");
        }

        public new class UxmlFactory : UxmlFactory<CountDeltaCell> {}
    }
}
#endif

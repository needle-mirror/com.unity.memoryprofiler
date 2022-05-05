#if UNITY_2022_1_OR_NEWER
using System;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class DeltaBarCell : VisualElement
    {
        const string k_UxmlAssetGuid = "44d6ee5b4285ab849bc6a0c4e6c7f526";
        const string k_UxmlIdentifier_NegativeBar = "delta-bar-cell__negative-bar";
        const string k_UxmlIdentifier_PositiveBar = "delta-bar-cell__positive-bar";

        ProgressBar m_NegativeBar;
        ProgressBar m_PositiveBar;

        void Initialize()
        {
            m_NegativeBar = this.Q<ProgressBar>(k_UxmlIdentifier_NegativeBar);
            m_PositiveBar = this.Q<ProgressBar>(k_UxmlIdentifier_PositiveBar);

            // Make this a cell with a darkened background. This requires quite a bit of styling to be compatible with tree view selection styling, so that is why it is its own class.
            AddToClassList("dark-tree-view-cell");
        }

        public static DeltaBarCell Instantiate()
        {
            var cell = (DeltaBarCell)ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            cell.Initialize();
            return cell;
        }

        // A value between -1 and 1, representing a proprtional delta.
        public void SetDeltaScalar(float proportionalDelta)
        {
            var negativeProgress = Math.Clamp(proportionalDelta * -1, 0f, 1f);
            m_NegativeBar.SetProgress(negativeProgress);
            m_NegativeBar.visible = negativeProgress != 0f;

            var positiveProgress = Math.Clamp(proportionalDelta, 0f, 1f);
            m_PositiveBar.SetProgress(positiveProgress);
            m_PositiveBar.visible = positiveProgress != 0f;
        }

        public new class UxmlFactory : UxmlFactory<DeltaBarCell> {}
    }
}
#endif

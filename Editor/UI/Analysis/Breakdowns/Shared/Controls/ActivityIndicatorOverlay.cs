#if UNITY_2022_1_OR_NEWER
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
#if UNITY_6000_0_OR_NEWER
    [UxmlElement]
#endif
    partial class ActivityIndicatorOverlay : VisualElement
    {
        readonly ActivityIndicator m_ActivityIndicator;

        public ActivityIndicatorOverlay()
        {
            m_ActivityIndicator = new ActivityIndicator();
            Add(m_ActivityIndicator);
        }

        public void Show()
        {
            SetEnabled(true);
            m_ActivityIndicator.StartAnimating();
        }

        public void Hide()
        {
            m_ActivityIndicator.StopAnimating();
            SetEnabled(false);
        }

#if !UNITY_6000_0_OR_NEWER
        public new class UxmlFactory : UxmlFactory<ActivityIndicatorOverlay> {}
#endif
    }
}
#endif

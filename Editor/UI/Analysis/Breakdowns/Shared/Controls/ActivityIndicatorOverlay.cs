using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
#if UNITY_6000_0_OR_NEWER
    [UxmlElement]
#endif
    partial class ActivityIndicatorOverlay : VisualElement
    {
        const string k_UssAssetGuid = "7a23b4563c611d3409531dfcc7519181";
        const string k_UssClass_Dark = "activity-indicator__dark";
        const string k_UssClass_Light = "activity-indicator__light";
        readonly ActivityIndicator m_ActivityIndicator;

        public ActivityIndicatorOverlay()
        {
            m_ActivityIndicator = new ActivityIndicator();
            Add(m_ActivityIndicator);

            var ussAssetPath = AssetDatabase.GUIDToAssetPath(new GUID(k_UssAssetGuid));
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(ussAssetPath));

            var themeUssClass = (EditorGUIUtility.isProSkin) ? k_UssClass_Dark : k_UssClass_Light;
            AddToClassList(themeUssClass);
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
        public new class UxmlFactory : UxmlFactory<ActivityIndicatorOverlay> { }
#endif
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace Unity.MemoryProfiler.Editor
{
    internal static class UIElementsHelper
    {
        public static void SetScrollViewVerticalScrollerVisibility(ScrollView view, bool alwaysOn)
        {
#if UNITY_2021_1_OR_NEWER
            view.verticalScrollerVisibility = alwaysOn ? ScrollerVisibility.AlwaysVisible : ScrollerVisibility.Auto;
#else
            view.showVertical = alwaysOn;
#endif
        }

        public static void SwitchVisibility(VisualElement first, VisualElement second, bool showFirst = true)
        {
            SetVisibility(first, showFirst);
            SetVisibility(second, !showFirst);
        }

        public static VisualElement Clone(this VisualTreeAsset tree, VisualElement target = null, string styleSheetPath = null, Dictionary<string, VisualElement> slots = null)
        {
            var ret = tree.CloneTree();
            if (!string.IsNullOrEmpty(styleSheetPath))
                ret.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(styleSheetPath));
            if (target != null)
                target.Add(ret);
            ret.style.flexGrow = 1f;
            return ret;
        }

        public static Rect GetRect(this VisualElement element)
        {
            return new Rect(element.LocalToWorld(element.contentRect.position), element.contentRect.size);
        }

        public static void SetVisibility(VisualElement element, bool visible)
        {
            SetElementDisplay(element, visible);
        }

        public static void SetElementDisplay(VisualElement element, bool value)
        {
            if (element == null)
                return;

            element.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
            element.style.visibility = value ? Visibility.Visible : Visibility.Hidden;
        }

        public static void SetBarWidthInPercent(this IStyle style, float percentage)
        {
            style.maxWidth = style.width = new Length(percentage, LengthUnit.Percent);
        }

        public static bool TemplateSourceEquals(this TemplateContainer container, VisualTreeAsset visualTreeAsset)
        {
#if UNITY_2021_2_OR_NEWER
            return container.templateSource.Equals(visualTreeAsset);
#else
            return true;
#endif
        }

        public static Image GetImageWithClasses(string[] classNames)
        {
            var img = new Image();
            foreach (var className in classNames)
            {
                img.AddToClassList(className);
            }
            img.style.alignSelf = Align.Center;
            return img;
        }

        public static void RegisterClickEvent(this VisualElement element, Action callback)
        {
#if UNITY_2020_1_OR_NEWER
            element.RegisterCallback<ClickEvent>((e) => callback());
#else
            element.AddManipulator(new Clickable(callback));
#endif
        }
    }
}

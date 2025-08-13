using System;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace Unity.MemoryProfiler.Editor
{
    internal class MemoryProfilerHyperLinkClickedEventArgs
    {
        public EditorWindow window;
        public Dictionary<string, string> hyperLinkData { get; private set; }

        public static MemoryProfilerHyperLinkClickedEventArgs ConvertEventArguments(EditorWindow window, HyperLinkClickedEventArgs args)
        {
            return new MemoryProfilerHyperLinkClickedEventArgs() { window = window, hyperLinkData = args.hyperLinkData };
        }
    }

    internal static class EditorGUICompatibilityHelper
    {

#if !UNITY_6000_0_OR_NEWER
        const int k_MaxHtmltagLength = 128;
#else
        const int k_MaxHtmltagLength = 256;
#endif
        public static readonly int MaxFileNameLengthForLinkTags = k_MaxHtmltagLength - "<link=\"href='' ".Length;
        static class Styles
        {
            public static readonly GUIStyle LinkTextLabel = new GUIStyle(EditorStyles.label);
            static Styles()
            {
                LinkTextLabel.richText = true;
            }
        }

        public static event Action<MemoryProfilerHyperLinkClickedEventArgs> hyperLinkClicked = delegate { };
        const string k_hyperLinkClickedEventName = "hyperLinkClicked";

        static EditorGUICompatibilityHelper()
        {
            EditorGUI.hyperLinkClicked -= OnHyperLinkClicked;
            EditorGUI.hyperLinkClicked += OnHyperLinkClicked;
        }

        [RuntimeInitializeOnLoadMethod]
        static void RuntimeInitialize()
        {
            EditorGUI.hyperLinkClicked -= OnHyperLinkClicked;
            EditorGUI.hyperLinkClicked += OnHyperLinkClicked;
        }

        static void OnHyperLinkClicked(EditorWindow window, HyperLinkClickedEventArgs args)
        {
            hyperLinkClicked(MemoryProfilerHyperLinkClickedEventArgs.ConvertEventArguments(window, args));
        }

        public static bool DrawLinkLabel(string text, Rect rect)
        {
            var size = Styles.LinkTextLabel.CalcSize(new GUIContent(text));
            var clickableRect = new Rect(rect.x, rect.y, size.x, size.y);
            EditorGUI.LabelField(rect, text, Styles.LinkTextLabel);
            EditorGUIUtility.AddCursorRect(clickableRect, MouseCursor.Link);

            if (Event.current.isMouse && Event.current.button == 0 && Event.current.type == EventType.MouseDown && clickableRect.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                return true;
            }
            return false;
        }
    }
}

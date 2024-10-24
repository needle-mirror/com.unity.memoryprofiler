using System;
#if !UNITY_2021_2_OR_NEWER
using System.Reflection;
#endif
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace Unity.MemoryProfiler.Editor
{
    internal class MemoryProfilerHyperLinkClickedEventArgs
    {
        public EditorWindow window;
        public Dictionary<string, string> hyperLinkData { get; private set; }

#if UNITY_2021_2_OR_NEWER
#else
        static PropertyInfo s_eventArghyperlinkInfos;
#endif
        static MemoryProfilerHyperLinkClickedEventArgs()
        {
#if UNITY_2021_2_OR_NEWER
#else
            var eventArgType = typeof(EditorGUILayout).GetNestedType("HyperLinkClickedEventArgs", BindingFlags.NonPublic);
            s_eventArghyperlinkInfos = eventArgType.GetProperty("hyperlinkInfos", BindingFlags.Public | BindingFlags.Instance | BindingFlags.GetProperty);
#endif
        }

#if UNITY_2021_2_OR_NEWER
        public static MemoryProfilerHyperLinkClickedEventArgs ConvertEventArguments(EditorWindow window, HyperLinkClickedEventArgs args)
        {
            return new MemoryProfilerHyperLinkClickedEventArgs() { window = window, hyperLinkData = args.hyperLinkData };
        }

#else
        public static MemoryProfilerHyperLinkClickedEventArgs ConvertEventArguments(object sender, EventArgs args)
        {
            return new MemoryProfilerHyperLinkClickedEventArgs() { window = EditorWindow.focusedWindow, hyperLinkData = s_eventArghyperlinkInfos.GetValue(args) as Dictionary<string, string> };
        }

#endif
    }

    internal static class EditorGUICompatibilityHelper
    {
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

#if !UNITY_2021_2_OR_NEWER
        static EventHandler s_EventHandler;
#endif
        static EditorGUICompatibilityHelper()
        {
#if UNITY_2021_2_OR_NEWER
            EditorGUI.hyperLinkClicked -= OnHyperLinkClicked;
            EditorGUI.hyperLinkClicked += OnHyperLinkClicked;
#else

            var assembly = typeof(EditorGUI).Assembly;
            var editorGUIType = typeof(EditorGUI);
            var internalEvent = editorGUIType.GetEvent(k_hyperLinkClickedEventName, BindingFlags.NonPublic | BindingFlags.Static);
            var eventAdder = internalEvent.GetAddMethod(true);
            var eventRemover = internalEvent.GetRemoveMethod(true);
            if (s_EventHandler == null)
                s_EventHandler = OnHyperLinkClicked;
            eventRemover.Invoke(null, new object[] { s_EventHandler });
            eventAdder.Invoke(null, new object[] { s_EventHandler });
#endif
        }

#if UNITY_2021_2_OR_NEWER
        static void OnHyperLinkClicked(EditorWindow window, HyperLinkClickedEventArgs args)
        {
            hyperLinkClicked(MemoryProfilerHyperLinkClickedEventArgs.ConvertEventArguments(window, args));
        }

#else
        static void OnHyperLinkClicked(object sender, EventArgs args)
        {
            hyperLinkClicked(MemoryProfilerHyperLinkClickedEventArgs.ConvertEventArguments(sender, args));
        }

#endif

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

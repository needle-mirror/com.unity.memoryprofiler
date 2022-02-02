using UnityEngine.Internal;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using System;
#if UNITY_2020_1_OR_NEWER
using UnityEditor.Networking.PlayerConnection;
using UnityEngine.Networking.PlayerConnection;
using ConnectionGUI = UnityEditor.Networking.PlayerConnection.PlayerConnectionGUI;
#else
using ConnectionUtility = UnityEditor.Experimental.Networking.PlayerConnection.EditorGUIUtility;
using ConnectionGUI = UnityEditor.Experimental.Networking.PlayerConnection.EditorGUI;
using UnityEngine.Experimental.Networking.PlayerConnection;
#endif

namespace Unity.MemoryProfiler.Editor
{
    internal static class PreferencesCompatibilityHelper
    {
        static MethodInfo s_Show;
        const string k_InternalClassName = "UnityEditor.SettingsWindow";
        static readonly Type[] k_ParameterTypes = new Type[] { typeof(SettingsScope), typeof(string) };
        static readonly object[] s_ShowParams = new object[] { SettingsScope.User, MemoryProfilerSettingsEditor.SettingsPath };

        //static PreferencesCompatibilityHelper()
        //{
        //    var assembly = typeof(SettingsProvider).Assembly;
        //    var internalClass = assembly.GetType(k_InternalClassName);
        //    s_Show = internalClass.GetMethod("Show", k_ParameterTypes);
        //}

        public static void OpenProfilerPreferences()
        {
            var assembly = typeof(SettingsProvider).Assembly;
            var internalClass = assembly.GetType(k_InternalClassName);

            s_Show = internalClass.GetMethod("Show", BindingFlags.Static | BindingFlags.NonPublic, null, k_ParameterTypes, null);
            var settings = s_Show.Invoke(null, s_ShowParams);
            if (settings == null)
            {
                Debug.LogError("Could not find Preferences for 'Analysis/Memory Profler'");
            }
        }
    }
}

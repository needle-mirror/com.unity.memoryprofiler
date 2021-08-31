using UnityEngine.Internal;
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
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
    internal static class PlayerConnectionCompatibilityHelper
    {
#if UNITY_2021_2_OR_NEWER
        static ConstructorInfo s_ConstructConnectionWindow;
        static MethodInfo s_GetToolbarContent;
        const string k_ConnectionTreeWindowTypeName = "UnityEditor.Networking.PlayerConnection.ConnectionTreeViewWindow";
        const string k_ConnectionUIHelperTypeName = "UnityEditor.Networking.PlayerConnection.ConnectionUIHelper";
        static object[] s_1Params = new object[1];
#else
        static MethodInfo s_AddItemsToMenu;
#endif
        static object[] s_2Params = new object[2];
        const string k_InternalInterfaceName =
#if UNITY_2020_1_OR_NEWER
            "UnityEditor.Networking.PlayerConnection.IConnectionStateInternal";
#else
            "UnityEditor.Experimental.Networking.PlayerConnection.IConnectionStateInternal";
#endif

        static PlayerConnectionCompatibilityHelper()
        {
            var assembly = typeof(ConnectionGUI).Assembly;
            var internalInterface = assembly.GetType(k_InternalInterfaceName);
#if UNITY_2021_2_OR_NEWER
            var connectionWindowType = assembly.GetType(k_ConnectionTreeWindowTypeName);
            if(connectionWindowType == null)
            {
                Debug.LogWarning("In Unity Editor Versions from 2021.2.0a1 to 2021.2.0a19 the connection dropdown doesn't work. Please update Unity. If you are on a 2021.2.0a19 or newer and see this, please report a bug.");
                return;
            }
            s_ConstructConnectionWindow = connectionWindowType.GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, CallingConventions.HasThis, new Type[] { internalInterface.UnderlyingSystemType, typeof(Rect) }, null);

            var uiHelperInterface = assembly.GetType(k_ConnectionUIHelperTypeName);
            s_GetToolbarContent = uiHelperInterface.GetMethod("GetToolbarContent", BindingFlags.Static | BindingFlags.Public);
#else
            s_AddItemsToMenu = internalInterface.GetMethod("AddItemsToMenu");
#endif
        }

        public static void ShowTargetSelectionDropdownMenu(IConnectionState connectionState, Rect rect)
        {
#if UNITY_2021_2_OR_NEWER
            s_2Params[0] = connectionState;
            s_2Params[1] = rect;
            if (s_ConstructConnectionWindow == null)
            {
                Debug.LogWarning("In Unity Editor Versions from 2021.2.0a1 to 2021.2.0a19 the connection dropdown doesn't work. Please update Unity. If you are on a 2021.2.0a19 or newer and see this, please report a bug.");
                return;
            }
            var windowContent = s_ConstructConnectionWindow.Invoke(s_2Params) as PopupWindowContent;
            PopupWindow.Show(rect, windowContent);
#else
            var menu = new GenericMenu();
            s_2Params[0] = menu;
            s_2Params[1] = rect;
            s_AddItemsToMenu.Invoke(connectionState, s_2Params);
            menu.DropDown(rect);
#endif
        }

        public static void ShowTargetSelectionDropdownMenu(IConnectionState connectionState, Rect rect, GenericMenu menu)
        {
#if UNITY_2021_2_OR_NEWER
            ShowTargetSelectionDropdownMenu(connectionState, rect);
#else
            s_2Params[0] = menu;
            s_2Params[1] = rect;
            s_AddItemsToMenu.Invoke(connectionState, s_2Params);
            menu.DropDown(rect);
#endif
        }

        public static string GetPlayerDisplayName(string playerName)
        {
#if UNITY_2021_2_OR_NEWER
            if (s_GetToolbarContent == null)
                return playerName;
            s_1Params[0] = playerName;
            return s_GetToolbarContent.Invoke(null, s_1Params) as string;
#else
            return playerName;
#endif
        }
    }
}

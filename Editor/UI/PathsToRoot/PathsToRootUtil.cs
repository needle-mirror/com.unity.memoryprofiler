using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI.PathsToRoot
{
    static class PathsToRootUtils
    {
        internal const string ObjectFlagsInfoHeader = "ObjectFlags: \n";
        internal const string IsDontDestroyOnLoadInfo = "IsDontDestroyOnLoad - Specifies that the object is marked as DontDestroyOnLoad.\n\n";
        internal const string IsPersistentInfo = "IsPersistent - Specifies that the object is set as persistent.\nThis is e.g. the case for any object connected to a file (also referred to as Asset), and Unity's subsystems (also referred to as Managers).\n\n";
        internal const string IsManagerInfo = "IsManager - Specifies that the object is marked as a Manager, i.e. an entity that manages one of Unity's engine subsystems.\n\n";
        internal const string HideFlagsInfoHeader = "HideFlags: \n";
        internal const string HideInHierarchyInfo = "HideInHierarchy - The object will not appear in the hiearachy.\n\n";
        internal const string HideInInspectorInfo = "HideInInspector - It is not possible to view this item in the inspector.\n\n";
        internal const string DontSaveInEditorInfo = "DontSaveInEditor - The object will not be saved to the scene in the editor.\n\n";
        internal const string NotEditableInfo = "NotEditable - The object will not be editable in the inspector.\n\n";
        internal const string DontSaveInBuildInfo = "DontSaveInBuild - The object will not be saved when building a player.\n\n";
        internal const string DontUnloadUnusedAssetInfo = "DontUnloadUnusedAsset - The object will not be unloaded by 'Resources.UnloadUnusedAssets()' calls, neither by explicit calls, nor implicit ones that are triggered when a Scene is non-additively unloaded.\n\n";
        internal const string DontSaveInfo = "DontSave - The object will not be saved to the Scene. It will not be destroyed when a new Scene is loaded. It is a shortcut for HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor | HideFlags.DontUnloadUnusedAsset.\n\n";
        internal const string HideAndDontSaveInfo = "HideAndDontSave - The GameObject is not shown in the Hierarchy, not saved to to Scenes, and not unloaded by Resources.UnloadUnusedAssets.";

        internal static readonly GUIContent FlagIcon = EditorGUIUtility.IconContent("console.infoicon");
        internal static readonly GUIContent NoIconContent = new GUIContent(Icons.NoIcon, "no icon for type");
        internal static readonly GUIContent CSScriptIconContent = EditorGUIUtility.IconContent("cs Script Icon", "");

        internal static Dictionary<string, GUIContent> iconContent = new Dictionary<string, GUIContent>();
    }
}

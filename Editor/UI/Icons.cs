using System.Collections.Generic;
using System.IO;
using Unity.MemoryProfiler.Editor.UI.PathsToRoot;
using UnityEditor;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal static class Icons
    {
        public const string IconFolder = "Packages/com.unity.memoryprofiler/Editor/UI/Icons/";

        public static readonly Texture2D NoIcon = IconUtility.LoadIconAtPath(Icons.IconFolder + "NoIconIcon.png", true);
        public static readonly Texture2D MemoryProfilerWindowTabIcon = IconUtility.LoadIconAtPath(IconFolder + "Memory Profiler.png", false);


        internal static readonly GUIContent FlagIcon = EditorGUIUtility.IconContent("console.infoicon");
        internal static readonly GUIContent NoIconContent = null; // no icon is cleaner // new GUIContent(Icons.NoIcon, "no icon for type");
        internal static readonly GUIContent CSScriptIconContent = EditorGUIUtility.IconContent("cs Script Icon", "");
        /// <summary>
        /// Built-in editor icon for the structural Prefab entries that (currently) only the Roots &amp; Impact
        /// table surfaces.
        /// </summary>
        internal static readonly GUIContent PrefabIconContent = EditorGUIUtility.IconContent("Prefab Icon");
        /// <summary>
        /// Built-in editor icon for the structural Scene entries. This is the same Unity-logo icon the snapshot
        /// list uses to denote Editor captures.
        /// </summary>
        internal static readonly GUIContent SceneIconContent = EditorGUIUtility.IconContent("SceneAsset Icon");

        internal static Dictionary<string, GUIContent> iconContent = new Dictionary<string, GUIContent>();

        public static GUIContent GetIcon(ObjectData data, string typeName, CachedSnapshot cs)
        {
            if (data.dataType is ObjectDataType.Scene)
                return PathsToRootDetailView.Styles.SceneIcon;
            if (string.IsNullOrEmpty(typeName))
                return Icons.NoIconContent;
            if (typeName == "AssetBundle")
            {
                return Icons.NoIconContent;
            }
            if (Icons.iconContent.TryGetValue(typeName, out var content))
            {
                return content;
            }
            if (!typeName.Contains("<unknown>") && cs != null)
            {
                var n = string.Concat(typeName.Split(Path.GetInvalidFileNameChars()));
                n = n.Replace("UnityEngine.", "");
                n = n.Replace("UnityEditor.", "");

                var tex = IconUtility.LoadBuiltInIconWithName(n + " Icon");
                if (tex != null)
                {
                    Icons.iconContent.Add(typeName, new GUIContent((Texture)tex));
                    return Icons.iconContent[typeName];
                }

                if (n == "MonoBehaviour")
                {
                    return Icons.CSScriptIconContent;
                }

                if (!data.isManaged) return Icons.NoIconContent;


                if (data.managedTypeIndex == -1)
                    return Icons.CSScriptIconContent;


                var idx = cs.TypeDescriptions.BaseOrElementTypeIndex[data.managedTypeIndex];
                if (idx != -1 && cs.TypeDescriptions.TypeDescriptionName[idx] == "UnityEngine.MonoBehaviour")
                {
                    return Icons.CSScriptIconContent;
                }

                if (data.isManaged)
                    return Icons.CSScriptIconContent;
            }

            return Icons.NoIconContent;
        }
    }
}

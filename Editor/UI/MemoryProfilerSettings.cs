using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Text;
using System;

namespace Unity.MemoryProfiler.Editor
{
    internal static class MemoryProfilerSettings
    {
        // Opt-Out Dialog keys:
        public const string HeapWarningWindowOptOutKey = "Unity.MemoryProfiler.HeapWarningPopup";

        const string k_LastImportPathPrefKey = "Unity.MemoryProfiler.Editor.MemoryProfilerLastImportPath";
        const string k_SnapshotPathEditorPerf = "Unity.MemoryProfiler.Editor.MemorySnapshotStoragePath";
        const string k_DefaultPath = "./MemoryCaptures";

        public static string MemorySnapshotStoragePath
        {
            get
            {
                return EditorPrefs.GetString(k_SnapshotPathEditorPerf, k_DefaultPath);
            }
            set
            {
                EditorPrefs.SetString(k_SnapshotPathEditorPerf, value);
            }
        }

        public static string LastImportPath
        {
            get
            {
                return SessionState.GetString(k_LastImportPathPrefKey, Application.dataPath);
            }
            set
            {
                SessionState.SetString(k_LastImportPathPrefKey, value);
            }
        }

        public static string AbsoluteMemorySnapshotStoragePath
        {
            get
            {
                string folderPath = MemoryProfilerSettings.MemorySnapshotStoragePath;
                //split the string
                var pathTokens = folderPath.Split(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                if (pathTokens.Length == 0)
                    return null;

                StringBuilder pathSb = new StringBuilder();
                if(!pathTokens[0].StartsWith(".")) //ensure that we are a relative path
                {
                    Debug.LogError(folderPath + " Is not a valid relative path, as it doesn't start with './'. Please change the path for memory snapshots in the Preferences.");
                    return null;
                }

                if (!pathTokens[0].StartsWith("..")) //relative path first set to start in ./
                {
                    pathSb.Append(Application.dataPath.Replace("/Assets", ""));
                }

                for (int i = 1; i < pathTokens.Length; ++i)
                {
                    pathSb.Append(Path.DirectorySeparatorChar);
                    pathSb.Append(pathTokens[i]);
                }

                var res = pathSb.ToString();
                try
                {
                    //will throw for invalid paths
                    Path.GetFullPath(res);
                }
                catch(Exception)
                {
                    Debug.LogError(folderPath + " Is not a valid relative path, it has more instances of '../' than folders above the project folder. Please change the path for memory snapshots in the Preferences.");
                    return null;
                }

                return res;
            }
        }

        public static void ResetMemorySnapshotStoragePathToDefault()
        {
            EditorPrefs.SetString(k_SnapshotPathEditorPerf, k_DefaultPath);
        }

        public static void ResetAllOptOutModalDialogSettings()
        {
            EditorPrefs.SetBool(HeapWarningWindowOptOutKey, false);
        }
    }

    internal class MemoryProfilerSettingsEditor
    {
        class Content
        {
            public static readonly GUIContent SnapshotPathLabel = EditorGUIUtility.TrTextContent("Memory Snapshot Storage Path");
            public static readonly string OnlyRelativePaths = L10n.Tr("Only relative paths are allowed");
            public static readonly string OKButton = L10n.Tr("OK");
            public static readonly string InvalidPathWindow = L10n.Tr("Invalid Path");

            public static readonly GUIContent ResetOptOutDialogsButton = EditorGUIUtility.TrTextContent("Reset Opt-Out settings for dialog prompts", "All dialogs that you have previously opted out of will show up again when they get triggered.");
        }
        const string k_RootPathSignifier = "./";
        const string k_PathOneUpSignifier = "../";

        [SettingsProvider()]
        static SettingsProvider CreateSettingsProvider()
        {
            var provider = new SettingsProvider("Preferences/Analysis/MemoryProfiler", SettingsScope.User)
            {
                guiHandler = searchConext =>
                {
                    PreferencesGUI();
                }
            };
            provider.PopulateSearchKeywordsFromGUIContentProperties<Content>();
            return provider;
        }

        static void PreferencesGUI()
        {
            float layoutMaxWidth = 500;
            float s_DefaultLabelWidth = 250;
            float m_LabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = s_DefaultLabelWidth;
            GUILayout.BeginHorizontal(GUILayout.MaxWidth(layoutMaxWidth));
            GUILayout.Space(10);
            GUILayout.BeginVertical();
            GUILayout.Space(10);
            {
                EditorGUI.BeginChangeCheck();
                var prevControl = GUI.GetNameOfFocusedControl();
                var val = EditorGUILayout.DelayedTextField(Content.SnapshotPathLabel, MemoryProfilerSettings.MemorySnapshotStoragePath);
                if (EditorGUI.EndChangeCheck())
                {
                    if (!(val.StartsWith(k_RootPathSignifier) || val.StartsWith(k_PathOneUpSignifier)))
                    {
                        if (EditorUtility.DisplayDialog(Content.InvalidPathWindow, Content.OnlyRelativePaths, Content.OKButton))
                        {
                            GUI.FocusControl(prevControl);
                            var currentlySavedPath = MemoryProfilerSettings.MemorySnapshotStoragePath;
                            // in case this faulty path has actually been saved, fix it back to default
                            if (!(currentlySavedPath.StartsWith(k_RootPathSignifier) || currentlySavedPath.StartsWith(k_PathOneUpSignifier)))
                                MemoryProfilerSettings.ResetMemorySnapshotStoragePathToDefault();
                        }
                    }
                    else
                    {
                        MemoryProfilerSettings.MemorySnapshotStoragePath = val;
                        var collectionPath = MemoryProfilerSettings.AbsoluteMemorySnapshotStoragePath;
                        var info = new DirectoryInfo(collectionPath);
                        if (!info.Exists)
                        {
                            info = Directory.CreateDirectory(collectionPath);
                            if (!info.Exists)
                                throw new UnityException("Failed to create directory, with provided preferences path: " + collectionPath);
                        }
                    }
                }
                if (GUILayout.Button(Content.ResetOptOutDialogsButton))
                {
                    MemoryProfilerSettings.ResetAllOptOutModalDialogSettings();
                }
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            EditorGUIUtility.labelWidth = m_LabelWidth;
        }
    }
}

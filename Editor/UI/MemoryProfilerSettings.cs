using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Unity.MemoryProfiler.Editor.UI;
using Unity.Profiling.Memory;

#if UNITY_2021_2_OR_NEWER
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Unity.MemoryProfiler.Editor.MemoryProfilerModule")]
#endif

namespace Unity.MemoryProfiler.Editor
{
    internal static class MemoryProfilerSettings
    {
        // Opt-Out Dialog keys:
        public const string HeapWarningWindowOptOutKey = "Unity.MemoryProfiler.HeapWarningPopup";

        const string k_LastImportPathPrefKey = "Unity.MemoryProfiler.Editor.MemoryProfilerLastImportPath";
        const string k_SnapshotPathEditorPerf = "Unity.MemoryProfiler.Editor.MemorySnapshotStoragePath";
        const string k_MemoryProfilerPackageOverridesMemoryModuleUIEditorPerf = "Unity.MemoryProfiler.Editor.MemoryProfilerPackageOverridesMemoryModuleUI";
        const string k_DefaultPath = "./MemoryCaptures";
        const string k_TruncateTypes = "Unity.MemoryProfiler.Editor.MemoryProfilerTruncateTypes";
        const string k_DefaultCopyOptionKey = "Unity.MemoryProfiler.Editor.DefaultCopySelectedItemTitleOption";
        const string k_ShowReservedMemoryBreakdown = "Unity.MemoryProfiler.Editor.ShowReservedMemoryBreakdown";
        const string k_ShowMemoryMapView = "Unity.MemoryProfiler.Editor.ShowMemoryMapView";
        const string k_SnapshotCaptureFlagsKey = "Unity.MemoryProfiler.Editor.MemoryProfilerSnapshotCaptureFlags";
        const string k_SnapshotCaptureCaptureWithScreenshot = "Unity.MemoryProfiler.Editor.MemoryProfilerSnapshotCaptureWithScreenshot";
        const string k_SnapshotGCCollectWhenCapturingEditor = "Unity.MemoryProfiler.Editor.MemoryProfilerSnapshotGCCollectWhenCapturingEditor";
        const string k_CloseSnapshotsWhenCapturingEditor = "Unity.MemoryProfiler.Editor.MemoryProfilerCloseSnapshotsWhenCapturingEditor";
        const string k_ObjectDetailsReferenceSectionVisibleKey = "Unity.MemoryProfiler.Editor.MemoryProfilerObjectDetailsReferenceSectionVisibility";
        const string k_ObjectDetailsReferenceSectionSizeKey = "Unity.MemoryProfiler.Editor.MemoryProfilerObjectDetailsReferenceSectionSize";

        public static event Action TruncateStateChanged;

        internal static class FeatureFlags
        {
            // For By Status and Path From Roots
            public static bool GenerateTransformTreesForByStatusTable_2022_09 => false;
        }

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

        public static bool UsingDefaultMemorySnapshotStoragePath() => MemorySnapshotStoragePath.Equals(k_DefaultPath, StringComparison.Ordinal);

        public static bool MemorySnapshotTruncateTypes
        {
            get
            {
                return EditorPrefs.GetBool(k_TruncateTypes);
            }
            private set
            {
                EditorPrefs.SetBool(k_TruncateTypes, value);
            }
        }

        public static SelectedItemDetailsPanel.CopyDetailsOption DefaultCopyDetailsOption
        {
            get
            {
                return (SelectedItemDetailsPanel.CopyDetailsOption)EditorPrefs.GetInt(k_DefaultCopyOptionKey,
                    (int)SelectedItemDetailsPanel.CopyDetailsOption.FullTitle);
            }
            set
            {
                EditorPrefs.SetInt(k_DefaultCopyOptionKey, (int)value);
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
                if (!pathTokens[0].StartsWith(".")) //ensure that we are a relative path
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
                catch (Exception)
                {
                    Debug.LogError(folderPath + " Is not a valid relative path, it has more instances of '../' than folders above the project folder. Please change the path for memory snapshots in the Preferences.");
                    return null;
                }

                return res;
            }
        }

#if UNITY_2021_2_OR_NEWER
        public static bool MemoryProfilerPackageOverridesMemoryModuleUI
        {
            get
            {
                return EditorPrefs.GetBool(k_MemoryProfilerPackageOverridesMemoryModuleUIEditorPerf, true);
            }
            set
            {
                if (value == MemoryProfilerPackageOverridesMemoryModuleUI)
                    return;
                if (value)
                    InstallUIOverride();
                else
                    UninstallUIOverride();
                EditorPrefs.SetBool(k_MemoryProfilerPackageOverridesMemoryModuleUIEditorPerf, value);
            }
        }
        public static event Action InstallUIOverride = delegate {};
        public static event Action UninstallUIOverride = delegate {};

        public static bool ShowReservedMemoryBreakdown
        {
            get
            {
                return EditorPrefs.GetBool(k_ShowReservedMemoryBreakdown, false);
            }
            set
            {
                if (value == ShowReservedMemoryBreakdown)
                    return;
                EditorPrefs.SetBool(k_ShowReservedMemoryBreakdown, value);
            }
        }

        public static bool ShowMemoryMapView
        {
            get
            {
                return EditorPrefs.GetBool(k_ShowMemoryMapView, false);
            }
            set
            {
                if (value == ShowMemoryMapView)
                    return;
                EditorPrefs.SetBool(k_ShowMemoryMapView, value);
            }
        }
#endif

        public static void ResetMemorySnapshotStoragePathToDefault()
        {
            EditorPrefs.SetString(k_SnapshotPathEditorPerf, k_DefaultPath);
        }

        public static void ResetAllOptOutModalDialogSettings()
        {
            EditorPrefs.SetBool(HeapWarningWindowOptOutKey, false);
        }

        public static void ToggleTruncateTypes()
        {
            EditorPrefs.SetBool(k_TruncateTypes, !MemorySnapshotTruncateTypes);
            TruncateStateChanged?.Invoke();
        }

        public static CaptureFlags MemoryProfilerCaptureFlags
        {
            get
            {
                return (CaptureFlags)EditorPrefs.GetInt(k_SnapshotCaptureFlagsKey, (int)(CaptureFlags.ManagedObjects | CaptureFlags.NativeObjects | CaptureFlags.NativeAllocations));
            }
            set
            {
                EditorPrefs.SetInt(k_SnapshotCaptureFlagsKey, (int)value);
            }
        }

        public static bool CaptureWithScreenshot
        {
            get
            {
                return EditorPrefs.GetBool(k_SnapshotCaptureCaptureWithScreenshot, true);
            }
            set
            {
                EditorPrefs.SetBool(k_SnapshotCaptureCaptureWithScreenshot, value);
            }
        }

        public static bool GCCollectWhenCapturingEditor
        {
            get
            {
                return EditorPrefs.GetBool(k_SnapshotGCCollectWhenCapturingEditor, true);
            }
            set
            {
                EditorPrefs.SetBool(k_SnapshotGCCollectWhenCapturingEditor, value);
            }
        }

        public static bool CloseSnapshotsWhenCapturingEditor
        {
            get
            {
                return EditorPrefs.GetBool(k_CloseSnapshotsWhenCapturingEditor, true);
            }
            set
            {
                EditorPrefs.SetBool(k_CloseSnapshotsWhenCapturingEditor, value);
            }
        }

        public static bool ObjectDetailsReferenceSectionVisible
        {
            get
            {
                return EditorPrefs.GetBool(k_ObjectDetailsReferenceSectionVisibleKey, true);
            }
            set
            {
                EditorPrefs.SetBool(k_ObjectDetailsReferenceSectionVisibleKey, value);
            }
        }

        public static float ObjectDetailsReferenceSectionSize
        {
            get
            {
                return EditorPrefs.GetFloat(k_ObjectDetailsReferenceSectionSizeKey, float.NaN);
            }
            set
            {
                EditorPrefs.SetFloat(k_ObjectDetailsReferenceSectionSizeKey, value);
            }
        }
    }
}

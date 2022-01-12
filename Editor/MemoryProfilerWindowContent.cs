using UnityEngine;
using UnityEditor;

using System.Collections.Generic;
using System;
using System.Text;
using System.Collections;
using System.Runtime.CompilerServices;
using System.IO;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Profiling.Memory.Experimental;
using UnityEditorInternal;
#if UNITY_2019_1_OR_NEWER
using UnityEngine.UIElements;
using UnityEditor.UIElements;
#else
using UnityEngine.Experimental.UIElements;
using UnityEditor.Experimental.UIElements;
#endif
#if UNITY_2019_3_OR_NEWER
using UnityEngine.Profiling.Experimental;
#endif

#if UNITY_2020_1_OR_NEWER
using UnityEditor.Networking.PlayerConnection;
using UnityEngine.Networking.PlayerConnection;
#else
using ConnectionUtility = UnityEditor.Experimental.Networking.PlayerConnection.EditorGUIUtility;
using ConnectionGUI = UnityEditor.Experimental.Networking.PlayerConnection.EditorGUI;
using UnityEngine.Experimental.Networking.PlayerConnection;
#endif


using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.EditorCoroutines.Editor;
using Unity.MemoryProfiler.Editor.UI;
using Unity.MemoryProfiler.Editor.Legacy;
using Unity.MemoryProfiler.Editor.Legacy.LegacyFormats;
using Unity.MemoryProfiler.Editor.EnumerationUtilities;

namespace Unity.MemoryProfiler.Editor.UIContentData
{
    internal static class TextContent
    {
        static GUIContent s_Title = new GUIContent("Memory Profiler");
        public static GUIContent Title
        {
            get
            {
                s_Title.image = Icons.MemoryProfilerWindowTabIcon;
                return s_Title;
            }
        }

        public static readonly GUIContent NoneView = new GUIContent("None", "");
        public static readonly GUIContent SummaryView = new GUIContent("Summary", "");
        public static readonly GUIContent MemoryMapView = new GUIContent("Memory Map", "Show Snapshot as Memory Map");
        public static readonly GUIContent MemoryMapViewDiff = new GUIContent("Memory Map Diff", "Show Snapshot Diff as Memory Map");
        public static readonly GUIContent TreeMapView = new GUIContent("Tree Map", "Show Snapshot as Memory Tree");
        public const string TableMapViewRoot = "Table/";
        public const string RawDataTableMapViewRoot = "Raw Data/";
        public const string DiffRawDataTableMapViewRoot = "Diff Raw Data/";
        public static readonly GUIContent SnapshotOptionMenuItemDelete = new GUIContent("Delete", "Deletes the snapshot file from disk.");
        public static readonly GUIContent SnapshotOptionMenuItemRename = new GUIContent("Rename", "Renames the snapshot file on disk.");
        public static readonly GUIContent SnapshotOptionMenuItemBrowse = new GUIContent("Open Folder", "Opens the folder where the snapshot file is located on disk.");
        public static readonly GUIContent SnapshotCaptureFlagsDropDown = new GUIContent("Capture", "Captures a memory snapshot with the specified types of data. Warning, this can take a moment.");
        public static readonly GUIContent SnapshotImportButton = new GUIContent("Import", "Imports a memory snapshot.");
        public static readonly GUIContent CaptureManagedObjectsItem = new GUIContent("Managed Objects");
        public static readonly GUIContent CaptureNativeObjectsItem = new GUIContent("Native Objects");
        public static readonly GUIContent CaptureNativeAllocationsItem = new GUIContent("Native Allocations");
        public static readonly GUIContent CaptureScreenshotItem = new GUIContent("Screenshot");
        public static readonly GUIContent CloseSnapshotsItem = new GUIContent("Close open snapshots before capturing Editor");
        public static readonly GUIContent GCCollectItem = new GUIContent("Run Garbage Collection before capturing Editor");

        public static readonly GUIContent OpenSettingsOption = new GUIContent("Open Preferences");

        public static readonly string UnknownSession = "Unknown Session";
        public static readonly string SessionName = "Session {0}";
        public static readonly GUIContent SessionFoldoutLabel = new GUIContent("{0} - {1}", "{0} - Unity {1} - Session ID: {2}");

        public static readonly string TotalUsedMemory = "Total Used: {0}";
        public static readonly GUIContent TotalAvailableSystemResources = new GUIContent("Hardware Resources: {0}", "Hardware Resources: {0} ({1} RAM + {2} VRAM)");
        public static readonly GUIContent TotalAvailableSystemResourcesUnifiedStatusUnknown = new GUIContent("Hardware Resources: {0}", "Hardware Resources: {0} Memory");
        public static readonly GUIContent TotalAvailableSystemResourcesUnified = new GUIContent("Hardware Resources: {0}", "Hardware Resources: {0} Unified Memory");

        public const string DefaultVirtualMachineMemoryCategoryLabel = "Virtual Machine (Scripting)";
        public const string MonoVirtualMachineMemoryCategoryLabel = "Virtual Machine (Mono)";
        public const string IL2CPPVirtualMachineMemoryCategoryLabel = "Virtual Machine (IL2CPP)";

        public const string ImportSnapshotWindowTitle = "Import snapshot file";
        public const string DeleteSnapshotDialogTitle = "Delete Snapshot";
        public const string DeleteSnapshotDialogMessage = "Are you sure you want to permanently delete this snapshot file?";
        public const string DeleteSnapshotDialogAccept = "OK";
        public const string DeleteSnapshotDialogCancel = "Cancel";

        public const string RenameSnapshotDialogTitle = "Rename Open Snapshot";
        public const string RenameSnapshotDialogMessage = "Renaming an open snapshot will close it. Are you sure you want to close the snapshot?";
        public const string RenameSnapshotDialogAccept = "OK";
        public const string RenameSnapshotDialogCancel = "Cancel";

        public const string HeapWarningWindowTitle = "Warning!";
        public const string HeapWarningWindowContent = "Memory snapshots contain all memory in the managed heap of your Unity Player or Editor as raw data at the moment of capture. " +
            "This might include passwords, server keys, access tokens and other personally identifying data. " +
            "Please use special caution when sharing snapshots. For more information on this, please visit the Memory Profiler Documentation.";
        public const string HeapWarningWindowOK = "Take Snapshot";

        public static readonly string[] MemorySnapshotImportWindowFileExtensions = new string[] { "MemorySnapshot", "snap", "Bitbucket MemorySnapshot", "memsnap,memsnap2,memsnap3" };
        public const string RawCategoryName = "Raw";
        public const string DiffRawCategoryName = "Diff Raw";

        public const string InstanceIdPingingOnlyWorksInSameSessionMessage = "Pinging objects only works for snapshots taken in the current editor session, as it relies on instance IDs. Current Editor Session ID:{0}, Snapshot Session ID: {1}";

        public const string MemoryUsageUnavailableMessage = "The Memory Usage Overview is not available with this snapshot. \n" + PreSnapshotVersion11UpdgradeInfoMemoryOverview;

        public static readonly string OpenManualTooltip = L10n.Tr("Open the relevant documentation entry.");

#if UNITY_2021_2_OR_NEWER
        public const string PreSnapshotVersion11UpdgradeInfoMemoryOverview = "Make sure to take snapshots with Unity version 2021.2.0a12 or newer, to be able to see the memory overview. See the documentation for more info.";
        public const string PreSnapshotVersion11UpdgradeInfo = "Make sure to upgrade to Unity version 2021.2.0a12 or newer, to be able to utilize this tool to the full extent. See the documentation for more info.";
#elif UNITY_2021_1_OR_NEWER
        public const string PreSnapshotVersion11UpdgradeInfoMemoryOverview = "Make sure to take snapshots with Unity version 2021.1.9f1 or newer, to be able to see the memory overview. See the documentation for more info.";
        public const string PreSnapshotVersion11UpdgradeInfo = "Make sure to upgrade to Unity version 2021.1.9f1 or newer, to be able to utilize this tool to the full extent. See the documentation for more info.";
#elif UNITY_2020_1_OR_NEWER
        public const string PreSnapshotVersion11UpdgradeInfoMemoryOverview = "Make sure to take snapshots with Unity version 2020.3.12f1 or newer, to be able to see the memory overview. See the documentation for more info.";
        public const string PreSnapshotVersion11UpdgradeInfo = "Make sure to upgrade to Unity version 2020.3.12f1 or newer, to be able to utilize this tool to the full extent. See the documentation for more info.";
#else
        public const string PreSnapshotVersion11UpdgradeInfoMemoryOverview = "Make sure to take snapshots with Unity version 2019.4.29f1 or newer, to be able to see the memory overview. See the documentation for more info.";
        public const string PreSnapshotVersion11UpdgradeInfo = "Make sure to upgrade to Unity version 2019.4.29f1 or newer, to be able to utilize this tool to the full extent. See the documentation for more info.";
#endif
    }

    internal static class DocumentationUrls
    {
        public const string LatestPackageVersionUrl = "https://docs.unity3d.com/Packages/com.unity.memoryprofiler@latest/";
        //public const string CurrentPackageVersionBaseUrl = "https://docs.unity3d.com/Packages/com.unity.memoryprofiler@0.4/";
        public const string LatestPackageVersionBaseUrl = "https://docs.unity3d.com/Packages/com.unity.memoryprofiler@latest/index.html?subfolder=/";
        const string k_AnchorChar = "%23";
        public const string WorkbenchHelp = "manual/workbench.html";
        public const string IndexHelp = "manual/index.html";
        public const string OpenSnapshotsPane = LatestPackageVersionBaseUrl + WorkbenchHelp + k_AnchorChar + "open-snapshots-pane";
        public const string AnalysisWindowHelp = LatestPackageVersionBaseUrl + "manual/main-view.html";
        public const string CaptureFlagsHelp = "https://docs.unity3d.com/ScriptReference/Profiling.Memory.Experimental.CaptureFlags.html";
        public const string Requirements = LatestPackageVersionBaseUrl + IndexHelp + k_AnchorChar + "requirements";
    }

    internal static class FileExtensionContent
    {
        public const string SnapshotTempFileName = "temp.tmpsnap";

#if UNITY_2019_3_OR_NEWER
        public const string SnapshotTempScreenshotFileExtension = ".tmppng";
#endif
        public const string SnapshotScreenshotFileExtension = ".png";
        public const string SnapshotFileExtension = ".snap";
        public const string SnapshotFileNamePart = "Snapshot-";
        public const string ConvertedSnapshotTempFileName = "ConvertedSnaphot.tmpsnap";
    }

    internal static class ResourcePaths
    {
        public const string PackageResourcesPath = "Packages/com.unity.memoryprofiler/Package Resources/";
        public const string UxmlFilesPath = PackageResourcesPath + "UXML/";
        public const string GeneralUseUxmlFilesPath = UxmlFilesPath + "GeneralUse/";
        public const string WindowUxmlPath = UxmlFilesPath + "MemoryProfilerWindow.uxml";
        public const string SnapshotWindowUxmlElementsPath = UxmlFilesPath + "SubElements/SnapshotWindow/";
        public const string AnalysisWindowUxmlElementsPath = UxmlFilesPath + "SubElements/AnalysisWindow/";
        // TODO: remove
        public const string SnapshotListItemUxmlPath = SnapshotWindowUxmlElementsPath + "SnapshotListItem.uxml";
        public const string SessionListItemUxmlPath = SnapshotWindowUxmlElementsPath + "SessionListItem.uxml";
        public const string SessionListSnapshotItemUxmlPath = SnapshotWindowUxmlElementsPath + "SnapshotListItem.uxml";
        public const string StyleSheetsPath = PackageResourcesPath + "StyleSheets/";
        public const string WindowCommonStyleSheetPath = StyleSheetsPath + "MemoryProfilerWindow_style.uss";
        public const string WindowLightStyleSheetPath = StyleSheetsPath + "MemoryProfilerWindow_style_light.uss";
        public const string WindowDarkStyleSheetPath = StyleSheetsPath + "MemoryProfilerWindow_style_dark.uss";
        public const string WindowNewThemingStyleSheetPath = StyleSheetsPath + "MemoryProfilerWindow_style_newTheming.uss";

        public const string AnalysisWindowUxmlPath = AnalysisWindowUxmlElementsPath + "AnalysisWindow.uxml";
        public const string SummaryPaneUxmlPath = AnalysisWindowUxmlElementsPath + "SummaryPane/SummaryPane.uxml";
        public const string TopIssuesUxmlPath = AnalysisWindowUxmlElementsPath + "SummaryPane/TopTenIssues.uxml";
        public const string MemoryUsageSummaryUxmlPath = AnalysisWindowUxmlElementsPath + "MemoryUsageSummary.uxml";

        public const string MemoryUsageBreakdownUxmlPath = AnalysisWindowUxmlElementsPath + "MemoryUsageBreakdown.uxml";
        public const string MemoryUsageBreakdownLegendNameAndColorUxmlPath = AnalysisWindowUxmlElementsPath + "MemoryUsageBreakdownLegendNameAndColor.uxml";
        public const string MemoryUsageBreakdownLegendSizeUxmlPath = AnalysisWindowUxmlElementsPath + "MemoryUsageBreakdownLegendSize.uxml";

        public const string RibbonUxmlPath = GeneralUseUxmlFilesPath + "Ribbon.uxml";
        public const string RibbonUssPath = StyleSheetsPath + "Ribbon.uss";
        public const string RibbonDarkUssPath = StyleSheetsPath + "Ribbon_dark.uss";
        public const string RibbonLightUssPath = StyleSheetsPath + "Ribbon_light.uss";

        public const string InfoBoxUxmlPath = GeneralUseUxmlFilesPath + "InfoBox.uxml";

        public static string WindowUxmlPathStyled
        {
            get
            {
                if (EditorGUIUtility.isProSkin)
                    return UxmlFilesPath + "MemoryProfilerWindowBase_dark.uxml";
                return UxmlFilesPath + "MemoryProfilerWindowBase_light.uxml";
            }
        }
    }
}

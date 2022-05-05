using UnityEngine;
using UnityEditor;
using Unity.MemoryProfiler.Editor.UI;

namespace Unity.MemoryProfiler.Editor.UIContentData
{
    internal static class TextContent
    {
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
        public const string InstanceIdPingingOnlyWorksInNewerUnityVersions = "Pinging objects based on their Instance ID does not work in this Unity version. To enable that functionality, please update your Unity installation to 2021.2.0a12, 2021.1.9, 2020.3.12f1, 2019.4.29f1 or newer.";

        public const string MemoryUsageUnavailableMessage = "The Memory Usage Overview is not available with this snapshot.\n" + PreSnapshotVersion11UpdgradeInfoMemoryOverview;

        public static readonly string OpenManualTooltip = L10n.Tr("Open the relevant documentation entry.");
        public const string InvalidObjectPleaseReportABugMessageShort = "Failed To read Object, please report a bug via 'Help > Report a Bug'.";
        public const string PleaseReportABugMessage = "please report a bug via the Window Menu Bar option 'Help > Report a Bug'.";
        public const string InvalidObjectPleaseReportABugMessage = PleaseReportABugMessage + " Please attach this snapshot, info on how to find this object in the snapshot, and a project to reproduce this with.";

        public static readonly string TruncateTypeName = "Truncate Managed Type Names";
        public static readonly string TreeMapNotAvailableInDiffTooltip = "Tree Map is not available when Comparing Snapshots";

        public static readonly string CopyToClipboardButtonTooltip = "Copy To Clipboard";
        public static readonly string CopyTitleToClipboardContextClickItem = "Copy {0}";
        public static readonly string CopyButtonDropdownOptionFullTitle = "Full Title";
        public static readonly string CopyButtonDropdownOptionObjectName = "Object Name";
        public static readonly string CopyButtonDropdownOptionManagedTypeName = "Managed Type Name";
        public static readonly string CopyButtonDropdownOptionNativeTypeName = "Native Type Name";

        public static string ResidentMemoryDescription => "The application footprint in physical memory. It inculdes all Unity and non-Unity allocations resident in memory at the time of the capture.";

        public static string ExecutablesAndMappedDescription => "Memory taken up by the build code of the application, including all shared libraries and assemblies, managed and native. This value is not yet reported consistently on all platforms." +
        "\n\nYou can reduce this memory usage by using a higher code stripping level and by reducing your dependencies on different modules and libraries.";
        public static string NativeDescription => "Native memory, used by Objects such as:" +
        "- Scene Objects (Game Objects and their Components)," +
        "- Assets and Managers" +
        "- Native Allocations including Native Arrays and other Native Containers" +
        "- CPU side of Graphics Asset memory" +
        "- And other" +
        "\n\nThis doesn't include Audio and Graphics, which are shown in separate categories." +
        "\n\nYou can inspect these categories further in the All of Memory Breakdown.";
        public static string ProfilerDescription => "This is the Profiler overhead, which includes memory used to collect and send Profiler frame data, take memory captures or process the received Profiler frame data in the Editor.";
        public static string GraphicsDescription => "Memory used by the Graphics Driver and the GPU to visualize your application. This includes display buffers, RenderTextures, Textures, Meshes, Animations." +
        "\n\nNote: not all these objects' memory is represented in this category. For example, Read/Write enabled graphics assets need to retain a copy in CPU accessible memory, which doubles their memory usage. Also, not necessarily all memory from these Type of objects resides in GPU memory.";
        public static string ManagedDescription => "Contains all Virtual Machine and Managed Heap memory" +
        "\n\nThe Managed Heap contains data related to Managed Objects and the space that has been reserved for them. It is managed by the Scripting Garbage Collector, so that any managed objects that no longer have references chain to a root are collected." +
        "\n\nThe used amount in the Managed Memory is made up of memory used for Managed objects and of empty space that cannot be returned." +
        "\n\nThe 'reserved' amount in this category may be quickly be reused if needed, or it will be returned to the system every 6th GC.Collect sweep.";
        public static string AudioDescription => "Memory used for Unity's Audio system, including AudioClips and playback buffers.";
        public static string UnknownDescription => "Memory that the memory profiler cannot yet account for, due to platform specific requirements, potential bugs or other gaps in memory tracking. " +
        "\n\nTo analyze this memory further, you will need to use a platform specific profiler";

        public static string ManagedDomainDescription => "The Virtual Machine memory contains data that the Scripting Backend (Mono or IL2CPP) needs in order to function, e.g.:\n" +
        "- Type MetaData for every used managed Type, including:\n" +
        "  - The definition of their fields and functions (vTable)\n" +
        "  - Static field data\n" +
        "- Data needed for Generic Types\n" +
        "\n\n When the Memory Profiler takes a capture, all Types that it finds while reporting the contents of the Managed Heap are initialized and their Type MetaData is therefore 'inflated' with data." +
        "A growth in Virtual Machine Memory between the first and second capture in a session can usually be mostly attributed to that. " +
        "Uninitialized types are those for which no one accessed any Type specific data yet, and their static constructor, explicit or implicit, has therefore not been called yet. " +
        "This could often be the type of arrays or generic Collections where their data has so far only been handled as a generic collection.";
        public static string ManagedObjectsDescription => "Memory used for Managed Objects, i.e. those that can be created and used from C# scripting code based on Types defined in C# scripting code. " +
        "This number only includes objects that are still referenced or held otherwise alive. Managed objects that the Garbage Collector still has to collect could reside in the Empty Active / Fragmented Heap Space.";
        public static string EmptyActiveHeapDescription => "This snapshot version does not yet contain a definitive report on which Managed Heap Section is the Active one. " +
        "Therefore the Contiguous Managed Heap section at the highest virtual address value is assumed to be the highest one, which is likely to be correct. " +
        "This bar shows the amount of unused memory in that section." +
        k_SharedEmptyHeapDescription;
        public static string EmptyFragmentedHeapDescription => "This bar represents the empty space in all Managed Heap sections that are not the Active Heap section. " +
        "\nFor some reason this memory can not yet be returned to the system. " +
        "This can be due to the fact that there are still active Managed Objects in it that do not occupy all of the space. " +
        "Heap Sections can only be allocated from the system or released back to the system in Pages (4KB on most platforms) and any partially occupied page is therefore impossible to release, until it is no longer used. " +
        k_SharedEmptyHeapDescription;

        const string k_SharedEmptyHeapDescription =
            "\n\nWhen allocating new managed memory for Managed Objects, the Active Heap Section is checked for empty space first, which is the fastest way to allocate it. " +
            "If there is not enough Contiguous empty space in it, the Scripting VM needs to scan the Empty Block list and the remaining heap sections for space, which takes longer. " +
            "If no fitting place is found for the Object, GC Collection is triggered. When using the Incremental GC, a new heap section is immediately allocated (expanding the heap) for the new Object, " +
            "while the Garbage Collection happens asynchronously. When it is not used, a new section is only allocated if there is not enough space after the collection either." +
            "\n\nThis Memory could also still be occupied by Objects that have been abandoned after the last GC.Alloc sweep and are waiting for collection in the next one.";

        public const string UsedByNativeCodeStatus = "Empty Array Required by Unity's subsystems";
        public const string UsedByNativeCodeHint = "This array's Type is marked with a [UsedByNativeCode] or [RequiredByNativeCode] Attribute in the Unity code-base and the array exists so that the Type is not compiled out on build. It is held in memory via a GCHandle. You can search the public C# reference repository for those attributes https://github.com/Unity-Technologies/UnityCsReference/.";

        public const string HeldByGCHandleStatus = "Held Alive By GCHandle";
        public const string HeldByGCHandleHint = "This Object is pinned or otherwise held in memory because a GCHandle was allocated for it.";

        public const string UnkownLivenessReasonStatus = "Bug: Liveness Reason Unknown";
        public const string UnkownLivenessReasonHint = "There is no reference pointing to this object and no GCHandle reported for it. This is a Bug, please report it using 'Help > Report a Bug' and attach the snapshot to the report.";

        public static GUIContent SearchInSceneButton = new GUIContent("Search In Scene", "This is a Scene Object. Click here to search for an object with this name and type in the currently open Scene(s).");
        public static GUIContent SearchInProjectButton = new GUIContent("Search In Project", "This is a Asset. Click here to search for an object with this name and type in the Project Browser.");
        public static GUIContent SearchButtonCantSearch = new GUIContent("Can't Search", "The object is neither a Scene Object nor an Asset, so it can't be searched for with the Project or Scene search");

        const string k_SelectInEditorLabel = "Select in Editor";
        public static GUIContent SelectInEditorButton100PercentCertain = new GUIContent(k_SelectInEditorLabel, "This exact Object was found in the Editor! " + k_SelectInEditorSharedTooltipPart);
        public static GUIContent SelectInEditorButtonLessCertain = new GUIContent(k_SelectInEditorLabel, "A close match to this object was found in the Editor. " + k_SelectInEditorSharedTooltipPart);
        const string k_SelectInEditorSharedTooltipPart = "Click here to select it and ping it.";
        public static GUIContent SelectInEditorButtonNotFound = new GUIContent(k_SelectInEditorLabel, "This object wasn't found in the Editor.");
        public static GUIContent SelectInEditorButtonFoundTooMany = new GUIContent(k_SelectInEditorLabel, "No precise enough match for this object was found in the Editor.");
        public static GUIContent SelectInEditorButtonFoundTooManyToProcess = new GUIContent(k_SelectInEditorLabel, "There where too many possible instances of this type of object to process if any of those could be this Object.");
        public static GUIContent SelectInEditorButtonTypeMissmatch = new GUIContent(k_SelectInEditorLabel, "Failed to search for this Type of object because its managed Type information was lacking.");
        // Leading space is needed as it is added to the tool-tips of the content above
        public const string SelectInEditorTooltipCanSearch = " Use the Search button instead.";
        public const string SelectInEditorTooltipTrySearch = " Try the Search button instead.";

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

        public const string DataTypeNativeTooltip = "This is a Native Unity Object or Type (i.e. one that is derived from UnityEngine.Object).";
        public const string DataTypeManagedTooltip = "This is an Object of a pure C# type or such a Type itself (i.e. one that is not derived from UnityEngine.Object).";
        public const string DataTypeUnifiedUnityTooltip = "This is a full Unity Object (i.e. one that is derived from UnityEngine.Object), containing a Managed Shell and a Native Unity Object, or the Managed Type of such an Object.";
        public const string DataTypeLeakedShellTooltip = "This is a leaked Managed Shell object of a Unity Object (i.e. one that is derived from UnityEngine.Object). The Native Unity Object has been destroyed already but a reference to the Managed Wrapper likely kept it alive.";
    }

    internal static class DocumentationUrls
    {
        public const string LatestPackageVersionUrl = "https://docs.unity3d.com/Packages/com.unity.memoryprofiler@latest/";
        //public const string CurrentPackageVersionBaseUrl = "https://docs.unity3d.com/Packages/com.unity.memoryprofiler@0.4/";
        public const string LatestPackageVersionBaseUrl = "https://docs.unity3d.com/Packages/com.unity.memoryprofiler@latest/index.html?subfolder=/";
        const string k_AnchorChar = "%23";
        public const string WorkbenchHelp = "manual/snapshots-component.html";
        public const string IndexHelp = "manual/index.html";
        public const string OpenSnapshotsPane = LatestPackageVersionBaseUrl + WorkbenchHelp + k_AnchorChar + "snapshots-component#open-snapshots-pane";
        public const string AnalysisWindowHelp = LatestPackageVersionBaseUrl + "manual/main-component.html";
#if UNITY_2022_3_OR_NEWER || UNITY_2023_2_OR_NEWER
        public const string CaptureFlagsHelp = "https://docs.unity3d.com/ScriptReference/Unity.Profiling.Memory.CaptureFlags.html";
#elif UNITY_2022_2_OR_NEWER || UNITY_2023_1_OR_NEWER
        public const string CaptureFlagsHelp = "https://docs.unity3d.com/2022.2/Documentation/ScriptReference/Unity.Profiling.Memory.CaptureFlags.html";
#else
        public const string CaptureFlagsHelp = "https://docs.unity3d.com/ScriptReference/Profiling.Memory.Experimental.CaptureFlags.html";
#endif
        public const string Requirements = LatestPackageVersionBaseUrl + IndexHelp + k_AnchorChar + "requirements";

        public const string UntrackedMemoryDocumentation = LatestPackageVersionBaseUrl + IndexHelp + k_AnchorChar + "known-limitations";
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
        public const string DetailsPanelUxmlElementsPath = UxmlFilesPath + "SubElements/DetailsPanel/";
        // TODO: remove
        public const string SnapshotListItemUxmlPath = SnapshotWindowUxmlElementsPath + "SnapshotListItem.uxml";
        public const string SessionListItemUxmlPath = SnapshotWindowUxmlElementsPath + "SessionListItem.uxml";
        public const string SessionListSnapshotItemUxmlPath = SnapshotWindowUxmlElementsPath + "SnapshotListItem.uxml";
        public const string StyleSheetsPath = PackageResourcesPath + "StyleSheets/";
        public const string WindowCommonStyleSheetPath = StyleSheetsPath + "MemoryProfilerWindow_style.uss";
        public const string WindowLightStyleSheetPath = StyleSheetsPath + "MemoryProfilerWindow_style_light.uss";
        public const string WindowDarkStyleSheetPath = StyleSheetsPath + "MemoryProfilerWindow_style_dark.uss";
        public const string WindowNewThemingStyleSheetPath = StyleSheetsPath + "MemoryProfilerWindow_style_newTheming.uss";

        public const string RibbonUxmlPath = GeneralUseUxmlFilesPath + "Ribbon.uxml";
        public const string RibbonUssPath = StyleSheetsPath + "Ribbon.uss";
        public const string RibbonDarkUssPath = StyleSheetsPath + "Ribbon_dark.uss";
        public const string RibbonLightUssPath = StyleSheetsPath + "Ribbon_light.uss";

        public const string InfoBoxUxmlPath = GeneralUseUxmlFilesPath + "InfoBox.uxml";
        public const string ObjectOrTypeLabelUxmlPath = UxmlFilesPath + "SubElements/" + "ObjectOrTypeLabel.uxml";
        public const string SelectedItemDetailsGroupUxmlPath = DetailsPanelUxmlElementsPath + "SelectedItemDetailsGroup.uxml";
        public const string SelectedItemDetailsGroupedItemUxmlPath = DetailsPanelUxmlElementsPath + "SelectedItemDetailsGroupedItem.uxml";

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

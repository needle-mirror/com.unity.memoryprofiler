using UnityEngine;
using UnityEditor;
using Unity.MemoryProfiler.Editor.UI;

namespace Unity.MemoryProfiler.Editor.UIContentData
{
    internal static class TextContent
    {
        public static readonly GUIContent SnapshotOptionMenuItemDelete = new GUIContent("Delete", "Deletes the snapshot file from disk.");
        public static readonly GUIContent SnapshotOptionMenuItemRename = new GUIContent("Rename", "Renames the snapshot file on disk.");
        public static readonly GUIContent SnapshotOptionMenuItemBrowse = new GUIContent("Open Folder", "Opens the folder where the snapshot file is located on disk.");
        public static readonly GUIContent CaptureManagedObjectsItem = new GUIContent("Managed Objects");
        public static readonly GUIContent CaptureNativeObjectsItem = new GUIContent("Native Objects");
        public static readonly GUIContent CaptureNativeAllocationsItem = new GUIContent("Native Allocations");
        public static readonly GUIContent CaptureScreenshotItem = new GUIContent("Screenshot");
        public static readonly GUIContent CloseSnapshotsItem = new GUIContent("Close open snapshots before capturing Editor");
        public static readonly GUIContent GCCollectItem = new GUIContent("Run Garbage Collection before capturing Editor");

        public static readonly GUIContent OpenSettingsOption = new GUIContent("Open Preferences");

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

        public const string InstanceIdPingingOnlyWorksInSameSessionMessage = "Pinging objects only works for snapshots taken in the current editor session, as it relies on instance IDs. Current Editor Session ID:{0}, Snapshot Session ID: {1}";
        public const string InstanceIdPingingOnlyWorksInNewerUnityVersions = "Pinging objects based on their Instance ID does not work in this Unity version. To enable that functionality, please update your Unity installation to 2021.2.0a12, 2021.1.9, 2020.3.12f1, 2019.4.29f1 or newer.";

        public static readonly string OpenManualTooltip = L10n.Tr("Open the relevant documentation entry.");
        public const string InvalidObjectPleaseReportABugMessageShort = "Failed To read Object, please report a bug via 'Help > Report a Bug'.";
        public const string PleaseReportABugMessage = "please report a bug via the Window Menu Bar option 'Help > Report a Bug'.";
        public const string InvalidObjectPleaseReportABugMessage = PleaseReportABugMessage + " Please attach this snapshot, info on how to find this object in the snapshot, and a project to reproduce this with.";

        public static readonly string TruncateTypeName = "Truncate Managed Type Names";

        public static readonly string CopyToClipboardButtonTooltip = "Copy To Clipboard";
        public static readonly string CopyTitleToClipboardContextClickItem = "Copy {0}";
        public static readonly string CopyButtonDropdownOptionFullTitle = "Full Title";
        public static readonly string CopyButtonDropdownOptionObjectName = "Object Name";
        public static readonly string CopyButtonDropdownOptionManagedTypeName = "Managed Type Name";
        public static readonly string CopyButtonDropdownOptionNativeTypeName = "Native Type Name";

        public const string SummaryViewName = "Summary";
        public const string UnityObjectsViewName = "Unity Objects";
        public const string AllOfMemoryViewName = "All Of Memory";
        public const string MemoryMapViewName = "Memory Map";

        const string k_ComparisonSuffix = " Comparison";
        public static string GetComparisonViewName(string viewName) => viewName + k_ComparisonSuffix;

        public static string ResidentMemoryDescription => "The application footprint in physical memory. It includes all Unity and non-Unity allocations resident in memory at the time of the capture.";

        public static string ExecutablesAndMappedDescription => "Memory taken up by the build code of the application, including all shared libraries and assemblies, managed and native. This value is not yet reported consistently on all platforms." +
        "\n\nYou can reduce this memory usage by using a higher code stripping level and by reducing your dependencies on different modules and libraries.";
        public static string NativeDescription => "Native memory, used by objects such as:" +
        "\n- Scene Objects (Game Objects and their Components)," +
        "\n- Assets and Managers" +
        "\n- Native Allocations including Native Arrays and other Native Containers" +
        "\n- CPU side of Graphics Asset memory" +
        "\n- And other" +
        "\n\nThis doesn't include Graphics, which is shown in a separate category." +
        $"\n\nYou can inspect these categories further in the {AllOfMemoryViewName} Breakdown." +
        $"\n\nNote: Values in {SummaryViewName} and {AllOfMemoryViewName} views might be different as they use different ways of grouping items together.";
        public static string NativeReservedDescription => "Reserved memory is memory that Unity allocated from the system (OS) but isn't used by any Unity objects at the moment of the capture. " +
            "There are many reasons why Unity might allocate memory for it internal allocators:" +
            "\n- Loading of resources" +
            "\n- Direct allocation by the user" +
            "\n- Memory-heavy computations" +
            "\n- Creation & destruction of GameObjects and their components" +
            "\n\nMost Unity allocators allocate memory in chunks, and when a chunk isn't used anymore it's released back to the system. " +
            "If you observe a high value of reserved memory it's most probably caused by fragmentation. " +
            "When memory is fragmented small objects might still reside in a chunk and cause it to remain allocated by Unity. " +
            "You can investigate which allocator has the highest reserved memory value by turning \"Show reserved memory breakdown\" in Memory Profiler settings. " +
            "You can read more about different types of Unity allocators and their settings on \"Memory allocator customization\" documentation page. " +
            "\n\nIf you want to reduce reserved memory, the general recommendations are:" +
            "\n- Unload your assets before loading a new scene" +
            "\n- Use \"temp\" allocator for short-living allocations if you use unsafe utilities API" +
            "\n- Customize allocators for your specific needs if Unity pre-allocate too much on start(recommended only for advanced users)";
        public static string GraphicsEstimatedDescription => "Estimated memory used by the Graphics Driver and the GPU to visualize your application." +
            $"\nThe information is based on the tracking of graphics resource allocations within Unity. This includes RenderTextures, Textures, Meshes, Animations and other graphics buffers which are allocated by Unity or Scripting API. Use {AllOfMemoryViewName} tab to explore graphics resources." +
            $"\nNot all these objects' memory is represented in this category. For example, Read/Write enabled graphics assets need to retain a copy in CPU-accessible memory, which doubles their total memory usage. Use {UnityObjectsViewName} tab to explore total memory usage of Unity Objects. " +
            "Also, not necessarily all memory from these type of objects resides in GPU memory. Memory Profiler is unable to get exact residence information for graphics resources.";
        public static string GraphicsEstimatedDisabledDescription => "Estimated memory used by the Graphics Driver and the GPU to visualize your application." +
            "\nThe information is based on the process memory regions reported by the operating system. This includes display buffers, RenderTextures, Textures, Meshes, Animations." +
            "\n\nNote: The current platform does not provide device memory information and we can not determine resident memory details of graphics memory. " +
            $"We defer analysis to the '{AllTrackedMemoryModelBuilder.UntrackedGroupName}' group which accurately represents resident and allocated memory status and is based on memory regions information provided by the operating system. " +
            $"And we keep '{AllTrackedMemoryModelBuilder.GraphicsGroupName}' group only for reference as a disabled view item." +
            "\n\nUse 'Allocated Memory' view to inspect Graphics memory in more details.";
        public static string ManagedDescription => "Contains all Virtual Machine and Managed Heap memory" +
        "\n\nThe Managed Heap contains data related to Managed Objects and the space that has been reserved for them. It is managed by the Scripting Garbage Collector, so that any managed objects that no longer have references chain to a root are collected." +
        "\n\nThe used amount in the Managed Memory is made up of memory used for Managed objects and of empty space that cannot be returned." +
        "\n\nThe 'reserved' amount in this category may be quickly be reused if needed, or it will be returned to the system every 6th GC.Collect sweep.";
        public static string ManagedReservedDescription => "Reserved memory is memory that Unity Managed Heap allocated from the system (OS) but isn't used by any Unity objects at the moment of the capture. " +
            "\n\nManaged Heap is allocated in blocks which store managed objects of similar size. Each block can store some amount of such objects and if it stays empty for several GC passes the block is released to the OS. " +
            "Managed Heap blocks might get fragmented and still contain just a few objects out of a capacity of thousands. Such blocks are still considered used, so their memory can’t be returned to the system and they count towards 'reserved'.";
        public static string AudioDescription => "Memory used for Unity's Audio system, including AudioClips and playback buffers.";
        public static string UntrackedDescription => "Memory that the memory profiler cannot yet account for, due to platform specific requirements, potential bugs or other gaps in memory tracking. " +
            $"\nThe size of {AllTrackedMemoryModelBuilder.UntrackedGroupName} memory is determined by analyzing all allocated and resident memory regions of the process and subtracting known regions which Unity native and managed memory allocators use." +
            "\nTo analyze this memory further, you will need to use a platform specific profiler." +
            $"\n\nNote: {AllTrackedMemoryModelBuilder.UntrackedGroupName} memory might include a portion of '{AllTrackedMemoryModelBuilder.GraphicsGroupName}' group. " +
            $"We do know accurate information about {AllTrackedMemoryModelBuilder.UntrackedGroupName} memory regions, but we are not able to determine contribution of individual graphics resources to the specific memory region." +
            $"\nThus we display {AllTrackedMemoryModelBuilder.UntrackedGroupName} regions according to the system information and disable '{AllTrackedMemoryModelBuilder.GraphicsGroupName}' group in the view.";
        public static string UntrackedEstimatedDescription => "Memory that the memory profiler cannot yet account for, due to platform specific requirements, potential bugs or other gaps in memory tracking. " +
            $"\nThe size of {AllTrackedMemoryModelBuilder.UntrackedGroupName} memory is determined by analyzing all allocated and resident memory regions of the process and subtracting known regions which Unity native and managed memory allocators." +
            "\nTo analyze this memory further, you will need to use a platform specific profiler." +
            $"\n\n*: When calculating Allocated size of {AllTrackedMemoryModelBuilder.UntrackedGroupName} memory we also subtract the size of '{AllTrackedMemoryModelBuilder.GraphicsGroupName}'. " +
            "We do know that certain types of memory regions are allocated for the graphics device, however we are unable to determine mapping of individual graphics resources to those regions. " +
            $"Thus we subtract the total '{AllTrackedMemoryModelBuilder.GraphicsGroupName}' size from regions which belong to the graphics device, and then from the biggest regions if we are unable to determine device regions." +
            $"\n\nThis means that the numbers for the {AllTrackedMemoryModelBuilder.UntrackedGroupName} category in 'Allocated Memory' mode are different from the numbers in 'Resident Memory' and 'Allocated and Resident Memory' modes to allow for in depth inspection." +
            $"\n - Use 'Allocated and Resident Memory' or 'Resident Memory' views to accurately inspect {AllTrackedMemoryModelBuilder.UntrackedGroupName} memory and its residency status." +
            "\n - Use 'Allocated Memory' view to inspect Graphics memory in more details.";

        public static string ManagedDomainDescription => "The Virtual Machine memory contains data that the Scripting Backend (Mono or IL2CPP) needs in order to function, e.g.:\n" +
        "- Type MetaData for every used managed Type, including:\n" +
        "  - The definition of their fields and functions (vTable)\n" +
        "  - Static field data\n" +
        "- Data needed for Generic Types\n" +
        "\n\nWhen the Memory Profiler takes a capture, all Types that it finds while reporting the contents of the Managed Heap are initialized and their Type MetaData is therefore 'inflated' with data. " +
        "A growth in Virtual Machine Memory between the first and second capture in a session can usually be mostly attributed to that. " +
        "Uninitialized types are those for which no one accessed any Type-specific data yet, and their static constructor, explicit or implicit, has therefore not been called yet. " +
        "This could often be the type of arrays or generic Collections where their data has so far only been handled as a generic collection.";
        public static string ManagedObjectsDescription => "Memory used for Managed Objects, i.e. those that can be created and used from C# scripting code based on Types defined in C# scripting code. " +
        "This number only includes objects that are still referenced or held otherwise alive. Managed objects that the Garbage Collector still has to collect could reside in the Empty Active / Fragmented Heap Space.";
        public static string EmptyActiveHeapDescription => "This snapshot version does not yet contain a definitive report on which Managed Heap Section is the Active one. " +
        "Therefore the Contiguous Managed Heap section at the highest virtual address value is assumed to be the highest one, which is likely to be correct. " +
        "This bar shows the amount of unused memory in that section." +
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
        public const string DataTypeLeakedShellTooltip = "This is a " + LeakedManagedShellName + " object of a Unity Object (i.e. one that is derived from UnityEngine.Object). The Native Unity Object has been destroyed already but a reference to the Managed Wrapper likely kept it alive.";

        public const string LeakedManagedShellName = "Leaked Managed Shell";
        public const string LeakedManagedShellHint = "(" + LeakedManagedShellName + ")";

        public const string NonTypedGroupDescription = "The selected item is a group of similar elements";

        public const string NoResidentMemoryInformationOldSnapshot = "Detailed breakdown of resident allocations is not available. This information was added in 2023.1 and this snapshot was taken with an older Unity version.";
        public const string NoResidentMemoryInformationNotSupported = "Detailed breakdown of resident allocations is not available. This platform is not supported.";
        public const string ResidentAndAllocatedMemoryAreIdentical = "On this platform, there is no distinction between Allocated Memory, and Resident Memory on Device. Therefore, the Memory profiler will present information exclusively about Allocated Memory.";

        public const string SystemMemoryRegionDescription = "Region as reported by the OS";
        public const string NativeAllocationDescription = "Native Allocation registered by Unity Memory Manager";
        public const string ManagedMemoryHeapDescription = "Allocation made by Mono/IL2CPP for GC pool";
        public const string NativeMemoryRegionDescription = "This is a memory chunk allocated by Unity Allocator. " +
            "Most Unity allocators allocate memory in chunks, and when a chunk isn't used anymore it's released back to the system. " +
            "You can read more about different types of Unity allocators and their settings on \"Memory allocator customization\" documentation page." +
            "\n\nYou can investigate which object holds specific allocator chunk by turning \"Show Memory Map view\" in Memory Profiler settings. " +
            "In Memory Map view you can see which objects resides in a specific allocator chunk.";
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
    }
}

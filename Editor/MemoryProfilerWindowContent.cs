using Unity.MemoryProfiler.Editor.UI;
using UnityEditor;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UIContentData
{
    internal static class TextContent
    {
        public static readonly GUIContent SnapshotOptionMenuItemDelete = L10n.TextContent("Delete", "Deletes the snapshot file from disk.");
        public static readonly GUIContent SnapshotOptionMenuItemRename = L10n.TextContent("Rename", "Renames the snapshot file on disk.");
        public static readonly GUIContent SnapshotOptionMenuItemBrowse = L10n.TextContent("Open Folder", "Opens the folder where the snapshot file is located on disk.");
        public static readonly GUIContent CaptureManagedObjectsItem = L10n.TextContent("Managed Objects");
        public static readonly GUIContent CaptureNativeObjectsItem = L10n.TextContent("Native Objects");
        public static readonly GUIContent CaptureNativeAllocationsItem = L10n.TextContent("Native Allocations");
        public static readonly GUIContent CaptureScreenshotItem = L10n.TextContent("Screenshot");
        public static readonly GUIContent CloseSnapshotsItem = L10n.TextContent("Close open snapshots before capturing Editor");
        public static readonly GUIContent GCCollectItem = L10n.TextContent("Run Garbage Collection before capturing Editor");

        public static readonly GUIContent OpenSettingsOption = L10n.TextContent("Open Preferences");

        public static readonly string ImportSnapshotWindowTitle = L10n.Tr("Import snapshot file");
        public static readonly string DeleteSnapshotDialogTitle = L10n.Tr("Delete Snapshot");
        public static readonly string DeleteSnapshotDialogMessage = L10n.Tr("Are you sure you want to permanently delete this snapshot file?");
        public static readonly string DeleteSnapshotDialogAccept = L10n.Tr("OK");
        public static readonly string DeleteSnapshotDialogCancel = L10n.Tr("Cancel");

        public static readonly string RenameSnapshotDialogTitle = L10n.Tr("Rename Open Snapshot");
        public static readonly string RenameSnapshotDialogMessage = L10n.Tr("Renaming an open snapshot will close it. Are you sure you want to close the snapshot?");
        public static readonly string RenameSnapshotDialogAccept = L10n.Tr("OK");
        public static readonly string RenameSnapshotDialogCancel = L10n.Tr("Cancel");

        public static readonly string HeapWarningWindowTitle = L10n.Tr("Warning!");
        public static readonly string HeapWarningWindowContent = L10n.Tr("Memory snapshots contain all memory in the managed heap of your Unity Player or Editor as raw data at the moment of capture. " +
            "This might include passwords, server keys, access tokens and other personally identifying data. " +
            "Please use special caution when sharing snapshots. For more information on this, please visit the Memory Profiler Documentation.");
        public static readonly string HeapWarningWindowOK = L10n.Tr("Take Snapshot");
        public static readonly string HeapWarningWindowCancel = L10n.Tr("Cancel");

        public static readonly string[] MemorySnapshotImportWindowFileExtensions = new string[] { "MemorySnapshot", "snap", "Bitbucket MemorySnapshot", "memsnap,memsnap2,memsnap3" };

        public static readonly string InstanceIdPingingOnlyWorksInSameSessionMessage = L10n.Tr("Pinging objects only works for snapshots taken in the current editor session, as it relies on instance IDs. Current Editor Session ID:{0}, Snapshot Session ID: {1}");
        public static readonly string InstanceIdPingingOnlyWorksInNewerUnityVersions = L10n.Tr("Pinging objects based on their Instance ID does not work in this Unity version. To enable that functionality, please update your Unity installation to 2021.2.0a12, 2021.1.9, 2020.3.12f1, 2019.4.29f1 or newer.");

        public static readonly string OpenManualTooltip = L10n.Tr("Open the relevant documentation entry.");
        public static readonly string PleaseReportABugMessage = L10n.Tr("Please report a bug via the Window Menu Bar option 'Help > Report a Bug'.");
        public static readonly string PleaseAttatchASnapshotAndDescriptionForFindingThisItemMessage = L10n.Tr("Please attach this snapshot, info on how to find this object in the snapshot, and a project to reproduce this with.");
        public static readonly string InvalidObjectPleaseReportABugMessageShort = L10n.Tr("Failed to read Object.") + " " + PleaseReportABugMessage;
        public static readonly string InvalidStringDataPleaseReportABugMessageShort = L10n.Tr("<Invalid String data>") + " " + PleaseReportABugMessage;
        public static readonly string InvalidCharArrayDataPleaseReportABugMessageShort = L10n.Tr("<Invalid char[] data>") + " " + PleaseReportABugMessage;
        public static readonly string InvalidStringDataOnlyPreviewPleaseReportABugMessageShort = L10n.Tr(" <ERROR: Could not only read string preview data from snapshot>") + " " + PleaseReportABugMessage;
        public static readonly string ConnectionDropdownIssueWarningLogMessage = L10n.Tr("In Unity Editor Versions from 2021.2.0a1 to 2021.2.0a19 the connection drop-down doesn't work. Please update Unity. If you are on a 2021.2.0a19 or newer and see this:") + " " + PleaseReportABugMessage;

        public static readonly string TruncateTypeName = L10n.Tr("Truncate Managed Type Names");

        public static readonly string CopyToClipboardButtonTooltip = L10n.Tr("Copy To Clipboard");
        public static readonly string CopyTitleToClipboardContextClickItem = L10n.Tr("Copy {0}");
        public static readonly string CopyButtonDropdownOptionFullTitle = L10n.Tr("Full Title");
        public static readonly string CopyButtonDropdownOptionObjectName = L10n.Tr("Object Name");
        public static readonly string CopyButtonDropdownOptionManagedTypeName = L10n.Tr("Managed Type Name");
        public static readonly string CopyButtonDropdownOptionNativeTypeName = L10n.Tr("Native Type Name");

        public const string SummaryViewName = "Summary";
        public const string UnityObjectsViewName = "Unity Objects";
        public const string RootsAndImpactViewName = "Roots & Impact";
        public const string AllOfMemoryViewName = "All Of Memory";
        public const string MemoryMapViewName = "Memory Map";

        const string k_ComparisonSuffix = " Comparison";
        public static string GetComparisonViewName(string viewName) => viewName + k_ComparisonSuffix;

        // Table Texts

        public static readonly string FlattenHierarchy = L10n.Tr("Flatten Hierarchy");
        public static readonly string ShowUnchanged = L10n.Tr("Show Unchanged");

        public static readonly string BaseTableTitle = L10n.Tr("Base");
        public static readonly string ComparedTableTitle = L10n.Tr("Compared");

        public static readonly string NotAvailable = L10n.Tr("N/A");

        public static readonly string ShowPotentialDuplicatesOnly = L10n.Tr("Show Potential Duplicates Only");
        public static readonly string ShowPotentialDuplicatesOnlyTooltip = L10n.Tr("Show potential duplicate Unity Objects only. Potential duplicates, which are Unity Objects of the same type, name, and size, might represent the same asset loaded multiple times in memory.");

        public static readonly string DescriptionTableColumnHeader = L10n.Tr("Description");
        public static readonly string UnityObjectsNameColumnHeaderTooltip = L10n.Tr("The name of the Unity Object or its type.");
        public static readonly string AllocatedSizeTableColumnHeader = L10n.Tr("Allocated Size");
        public static readonly string PercentImpactTableColumnHeader = L10n.Tr("% Impact");
        public static readonly string ResidentSizeTableColumnHeader = L10n.Tr("Resident Size");
        public static readonly string NativeSizeTableColumnHeader = L10n.Tr("Native Size");
        public static readonly string ManagedSizeTableColumnHeader = L10n.Tr("Managed Size");
        public static readonly string GraphicsSizeTableColumnHeader = L10n.Tr("Graphics Size");
        public static readonly string StatusTableColumnHeader = L10n.Tr("Status");
        public static readonly string StatusInAColumnHeader = L10n.Tr("Status In A");
        public static readonly string StatusInBColumnHeader = L10n.Tr("Status In B");

        // Table Column Headers - Comparison Status toggle
        public static readonly string CompareStatusToggleText = L10n.Tr("Compare Status");

        // Object Status display names (shown in Status column)
        public static readonly string ObjectStatusBrokenScriptReference = L10n.Tr("Broken Script Reference");
        public static readonly string ObjectStatusBrokenScriptReferenceTooltip = L10n.Tr("A native MonoBehaviour or ScriptableObject whose backing script (MonoScript) is missing, for example after a script was deleted, renamed, or failed to compile. Nothing references it and no root keeps it alive, yet it isn't cleaned up automatically, so it is effectively a leak. Resolve it by restoring the missing script or destroying the object.");
        public static readonly string ObjectStatusLeakedShell = L10n.Tr("Leaked Shell");
        public static readonly string ObjectStatusLeakedShellHeldByGCHandle = L10n.Tr("Leaked Shell (GCHandle)");
        public static readonly string ObjectStatusLeakedShellTooltip = L10n.Tr("Leaked Shells are Unity Objects that have had their native memory unloaded via Destroy but their managed shell is still held alive via managed references (or GCHandles). They might appear as small and insignificant on their own but check their Impact in the Roots & Impact table to gauge how critical they are. These occur naturally during a scene or forced AssetBundle unload but should not stick around for too long. If they do, remember to clear all references from outside the their intended life time (e.g. Scene or AssetBundle).");
        public static readonly string ObjectStatusInDelayedDeletionQueue = L10n.Tr("Queued for Deletion");
        public static readonly string ObjectStatusInDelayedDeletionQueueTooltip = L10n.Tr("Objects Queued for Deletion are being processed in a background thread and will disappear shortly.");
        public static readonly string ObjectStatusDynamicAssetLeaked = L10n.Tr("Leaked Dynamic Asset");
        public static readonly string ObjectStatusDynamicAssetLeakedTooltip = L10n.Tr("Dynamically created Assets that have been leaked (no remaining references) before they could have gotten properly released via Object.Destroy. Resources.UnloadUnusedAssets() could unload them but does not run automatically, so that uncontrolled leaks can lead to an Out Of Memory crash.");
        public static readonly string ObjectStatusDynamicAssetReferenced = L10n.Tr("Dynamic Asset");
        public static readonly string ObjectStatusDynamicAssetDontUnload = L10n.Tr("Dynamic Asset (DDoL)");
        public static readonly string ObjectStatusDynamicAssetDontUnloadTooltip = L10n.Tr("Dynamic Assets marked as Don't Destroy on Load will not be unloaded unless explicitly calling Destroy() on them.");
        public static readonly string ObjectStatusUsedAsset = L10n.Tr("Used Asset");
        public static readonly string ObjectStatusUnusedAsset = L10n.Tr("Unused Asset");
        public static readonly string ObjectStatusUnusedAssetTooltip = L10n.Tr("Unused Assets are Unity Objects that are not currently referenced by any other objects and could be unloaded, e.g. via Resources.UnloadUnusedAssets().");
        public static readonly string ObjectStatusUnusedAssetDontUnload = L10n.Tr("Unused Asset (DDoL)");
        public static readonly string ObjectStatusUnusedAssetDontUnloadTooltip = L10n.Tr("Unused Assets marked as Don't Destroy on Load are an indication of potential resource waste, or could be some form of a Singleton pattern.");
        public static readonly string ObjectStatusUnusedAssetInAssetBundle = L10n.Tr("Unused Asset (AssetBundle)");
        public static readonly string ObjectStatusUnusedAssetInAssetBundleTooltip = L10n.Tr("An Asset that is only kept loaded because a loaded AssetBundle references it. Nothing outside the bundle keeps it alive, so unloading the AssetBundle (e.g. via AssetBundle.Unload(true)) would free it.");
        public static readonly string ObjectStatusUsedAssetBundle = L10n.Tr("AssetBundle");
        public static readonly string ObjectStatusUsedAssetBundleTooltip = L10n.Tr("A loaded AssetBundle that still has at least one containing asset in use and therefore can't be unloaded.");
        public static readonly string ObjectStatusUnusedAssetBundle = L10n.Tr("Unused AssetBundle");
        public static readonly string ObjectStatusUnusedAssetBundleTooltip = L10n.Tr("A loaded AssetBundle whose contents are not used. The whole bundle, including its contents, could be unloaded.");
        public static readonly string ObjectStatusLeakedAssetBundle = L10n.Tr("Leaked AssetBundle");
        public static readonly string ObjectStatusLeakedAssetBundleTooltip = L10n.Tr("An unused AssetBundle that nothing references anymore: no AssetBundle manager, Addressables, or user code holds it. It cannot be unloaded by name and only Resources.UnloadUnusedAssets() can reclaim it, so it is effectively a leak.");
        public static readonly string ObjectStatusSceneObject = L10n.Tr("Scene Object");
        public static readonly string ObjectStatusSceneObjectDontUnload = L10n.Tr("Scene Object (DDoL)");
        public static readonly string ObjectStatusSceneObjectDontUnloadTooltip = L10n.Tr("Scene Objects marked as Don't Destroy on Load are moved to a separate scene that persists across scene loads.");
        public static readonly string ObjectStatusManager = L10n.Tr("Manager");
        public static readonly string ObjectStatusUsedByNativeCode = L10n.Tr("Used by Native Code");
        public static readonly string ObjectStatusUsedByNativeCodeTooltip = L10n.Tr("A managed object that is held alive because of a [UsedByNativeCode] or [RequiredByNativeCode] attribute in Unity's source code.");
        public static readonly string ObjectStatusGCHandleHeld = L10n.Tr("GCHandle Held");
        public static readonly string ObjectStatusManagedObjectReferenced = L10n.Tr("Referenced Managed Object");
        public static readonly string ObjectStatusUnknownLiveness = L10n.Tr("Unknown Liveness");
        public static readonly string ObjectStatusManagedTypeRoot = L10n.Tr("Managed Type");
        public static string ObjectStatusManagedTypeRootTooltip => string.Format(ObjectStatusVirtualRootTooltipFormat, ObjectStatusManagedTypeRoot);
        public static readonly string ObjectStatusNativeRootAllocation = L10n.Tr("Native Allocation");
        public static readonly string ObjectStatusGCHandleEntry = L10n.Tr("GCHandle");
        public static string ObjectStatusGCHandleEntryTooltip => string.Format(ObjectStatusVirtualRootTooltipFormat, ObjectStatusGCHandleEntry);
        public static readonly string ObjectStatusPrefabRoot = L10n.Tr("Prefab");
        public static string ObjectStatusPrefabRootTooltip => string.Format(ObjectStatusVirtualRootTooltipFormat, ObjectStatusPrefabRoot);
        public static readonly string ObjectStatusSceneRoot = L10n.Tr("Scene");
        public static string ObjectStatusSceneRootTooltip => string.Format(ObjectStatusVirtualRootTooltipFormat, ObjectStatusSceneRoot);
        public static readonly string ObjectStatusVirtualRootTooltipFormat = L10n.Tr("\"{0}\" entries aren't technically objects and do not appear on their own in any views aside from Roots & Impact as well as Reference tables.");

        public static readonly string ObjectStatusUnrooted = L10n.Tr("Unrooted");
        public static readonly string ObjectStatusUnrootedTooltip = L10n.Tr("Native subsystem memory Unity reported that is neither rooted to a native root reference nor referenced by anything the crawler can reach, so no owner could be attributed to it. Specifically for unrooted and unreferenced memory.");
        public static readonly string ObjectStatusReferencedUnrooted = L10n.Tr("Unrooted (Referenced)");
        public static readonly string ObjectStatusReferencedUnrootedTooltip = L10n.Tr("A native allocation with no native root reference that is kept alive only by managed or other references the crawler can follow. Unlike fully unrooted memory, this is attributed to its referencing owner in the ownership tree.");
        public static readonly string ObjectStatusUntracked = L10n.Tr("Untracked");
        public static readonly string ObjectStatusReserved = L10n.Tr("Reserved");
        public static readonly string ObjectStatusExecutablesAndMapped = L10n.Tr("Executables & Mapped");
        public static readonly string ObjectStatusGraphicsBuffers = L10n.Tr("Graphics Buffer");
        public static readonly string ObjectStatusGraphicsBuffersTooltip = L10n.Tr("Tables that do not list Graphics Buffers as separate from their Native Objects (e.g. the Unity Objects and Roots & Impact tables) will not contain any items with this status, but filtering for it will surface any Native Objects associated with Graphics Buffers.");
        public static readonly string ObjectStatusVirtualMachine = L10n.Tr("Virtual Machine");
        /// <summary>Used in comparison status columns when a name group contains objects with more than one distinct status. Tooltip lists all present statuses.</summary>
        public static readonly string ObjectStatusMixed = L10n.Tr("Mixed");
        /// <summary>Tooltip format for status cells on group nodes that contain warning-status descendants. {0} = count, {1} = status name(s).</summary>
        public static readonly string ObjectStatusWarningDescendantCountTooltip = L10n.Tr("{0} objects with potentially problematic statuses: {1}");
        /// <summary>Tooltip on the row wrapper of a disabled status filter toggle, explaining why it cannot be used in the current table. Fallback used only if no other view surfaces the status.</summary>
        public static readonly string StatusUnavailableInTableTooltip = L10n.Tr("This status will not appear in this table.");
        /// <summary>Tooltip on a disabled status filter toggle, naming the view(s) that DO surface the status. {0} = a single view name or comma-separated list of view names.</summary>
        public static readonly string StatusUnavailableInTableInspectElsewhereTooltip = L10n.Tr("This view can never contain any entries with this status. To inspect these, use one of the following views: {0}");
        /// <summary>Secondary label on a Mixed status cell showing how many distinct status types are present. {0} = type count.</summary>
        public static readonly string ObjectStatusMixedTypeCount = L10n.Tr("({0})");
        /// <summary>Secondary label on a status cell with multiple warning objects. {0} = warning object count.</summary>
        public static readonly string ObjectStatusWarningCountLabel = L10n.Tr("({0} Warnings)");
        /// <summary>Secondary label when a Mixed cell also contains multiple warning objects. {0} = type count, {1} = warning object count.</summary>
        public static readonly string ObjectStatusMixedTypesAndWarningCount = L10n.Tr("({0}) ({1} Warnings)");
        /// <summary>Roots &amp; Impact secondary label: number of warning-status objects among this row's referenced (descendant) objects. {0} = count.</summary>
        public static readonly string RootsAndImpactWarningsInReferencesLabel = L10n.Tr("({0} Warnings in references)");
        /// <summary>Roots &amp; Impact secondary label when a group row is Mixed and also has reference warnings. {0} = distinct child status type count, {1} = reference warning count.</summary>
        public static readonly string RootsAndImpactMixedTypesAndWarningsInReferences = L10n.Tr("({0}) ({1} Warnings in references)");
        /// <summary>Roots &amp; Impact status tooltip line listing only the warning ("potentially problematic") statuses found on the row and the objects it references, with the count of such objects. {0} = warning object count, {1} = comma-separated warning status names.</summary>
        public static readonly string RootsAndImpactProblematicStatusesTooltip = L10n.Tr("Potentially problematic ({0} object(s)): {1}");
        /// <summary>Roots &amp; Impact status tooltip line listing the non-warning statuses found on the row and the objects it references, shown beneath the problematic line. {0} = comma-separated status names.</summary>
        public static readonly string RootsAndImpactOtherStatusesTooltip = L10n.Tr("Other statuses present: {0}");
        /// <summary>Roots &amp; Impact status tooltip listing the statuses found on the row and the objects it references when none of them are warning statuses. {0} = comma-separated status names.</summary>
        public static readonly string RootsAndImpactStatusesPresentTooltip = L10n.Tr("Statuses present: {0}");
        /// <summary>Label preceding the status filter dropdown, clarifying that the dropdown filters the table by object status.</summary>
        public static readonly string ObjectStatusFilterLabel = L10n.Tr("Filter by Status");
        public static readonly string StatusFilterSelectAll = L10n.Tr("Select all");
        /// <summary>Label shown in the status filter button when all status groups are unchecked (filter to ObjectStatus.None only).</summary>
        public static readonly string ObjectStatusNoneLabel = L10n.Tr("None");
        public static readonly string ObjectStatusFilterButtonTooltip = L10n.Tr("Choose one or multiple statuses to filter the table results by.");
        public static readonly string ObjectStatusAny = L10n.Tr("Any Status");
        public static readonly string ObjectStatusAll = L10n.Tr("All Statuses");
        public static readonly string ObjectStatusAllLeaked = L10n.Tr("All Leaked");
        public static readonly string ObjectStatusAllLeakedTooltip = L10n.Tr("Filter for all statuses of clearly Leaked objects.");
        public static readonly string ObjectStatusDontDestroy = L10n.Tr("Don't Destroy Objects");
        public static readonly string ObjectStatusDontDestroyTooltip = L10n.Tr("Filter for all statuses of Don't Destroy on Load (DDoL) flagged objects. As these get never cleaned up automatically, they can be a source of leaks.");
        public static readonly string ObjectStatusNormalAssets = L10n.Tr("Normal Assets");
        public static readonly string ObjectStatusNormalAssetsTooltip = L10n.Tr("Filter for all statuses of Loaded Assets (i.e. persistent file associated data) that are not marked as Don't Destroy on Load (DDoL), as well as Dynamically created Assets that are still referenced.");
        public static readonly string ObjectStatusNormalSceneObjects = L10n.Tr("Normal Scene Objects");
        public static readonly string ObjectStatusNormalSceneObjectsTooltip = L10n.Tr("Scene Objects that are not marked as Don't Destroy on Load and therefore moved into the DontDestroyOnLoadScene.");
        public static readonly string ObjectStatusOtherManagedRootingStatuses = L10n.Tr("Other Managed Statuses");
        public static readonly string ObjectStatusAllManagedRootingStatusesTooltip = L10n.Tr("Filter for all statuses associated with Managed Roots");
        public static readonly string ObjectStatusAllNativeSubsystemLeftOvers = L10n.Tr("Native Subsystem");
        public static readonly string ObjectStatusAllNativeSubsystemLeftOversTooltip = L10n.Tr("An assortment of statuses associated with Unity's Native Subsystems, such as Managers, native allocations or objects handled by the delayed deletion queue.");
        public static readonly string ObjectStatusAllOtherStatuses = L10n.Tr("Other Statuses");

        public static readonly string GraphicsResidentMemoryUnreliableTooltip = L10n.Tr(
            "The memory profiler cannot certainly attribute which part of the " +
            "resident memory belongs to graphics, as some of it might be included in the \"untracked\" memory.\n\n" +
            "Change focus to \"Allocated Memory\" to inspect graphics in detail.\n\n" +
            "We also recommend using a platform profiler for checking the residency status of graphics memory.");
        public static readonly string GraphicsSizeNotAvailableTooltip = L10n.Tr(
            "The memory profiler cannot certainly attribute which part of the resident memory belongs to graphics." +
            "\n\nChange focus to 'Allocated Memory' to inspect graphics memory." +
            "\n\nWe also recommend using a platform profiler for checking the residency status of graphics memory.");

        // Roots & Impact View
        public static readonly string RootsAndImpactNameColumnHeader = L10n.Tr("Name");
        public static readonly string RootsAndImpactTotalImpactAllocatedColumnHeader = L10n.Tr("Allocated Total Impact");
        public static readonly string RootsAndImpactNameColumnHeaderTooltip = L10n.Tr("The name of the item or root group.");
        public static readonly string RootsAndImpactTotalImpactColumnHeaderTooltip = L10n.Tr("The Total Impact of this item, that is its exclusive impact (which includes its own size) and its shared impact. The total of this column is the sum of all items in the table without double counting shared memory. Shows Allocated or Resident amounts depending on the chosen view mode.");
        public static readonly string RootsAndImpactTotalImpactResidentColumnHeader = L10n.Tr("Resident Total Impact");
        public static readonly string RootsAndImpactOwnSizeColumnHeader = L10n.Tr("Own Size");
        public static readonly string RootsAndImpactOwnSizeColumnHeaderTooltip = L10n.Tr("This item's size excluding any referenced items.");
        public static readonly string RootsAndImpactExclusiveImpactColumnHeader = L10n.Tr("Exclusive");
        public static readonly string RootsAndImpactExclusiveImpactColumnHeaderTooltip = L10n.Tr("The memory the item itself and the exclusive impact of any exclusively referenced items.");
        public static readonly string RootsAndImpactSharedProRataColumnHeader = L10n.Tr("Shared");
        public static readonly string RootsAndImpactSharedProRataColumnHeaderTooltip = L10n.Tr("This item's portion of memory shared proportionally among all referencing items.");
        public static readonly string RootsAndImpactSharedInTotalColumnHeader = L10n.Tr("Shared (non-distributed)");
        public static readonly string RootsAndImpactSharedInTotalColumnHeaderTooltip = L10n.Tr("This item's portion of memory shared among all referencing items without distribution. As this accumulates when attributing the impact up the reference chain, this can exceed even the total memory. High values here can indicate a very deep and wide reference structure, bubbling central reference holders to the top of the table.");
        public static readonly string RootsAndImpactNativeImpactColumnHeader = L10n.Tr("Native Impact");
        public static readonly string RootsAndImpactManagedImpactColumnHeader = L10n.Tr("Managed Impact");
        public static readonly string RootsAndImpactGfxImpactColumnHeader = L10n.Tr("Graphics Impact");
        public static readonly string RootsAndImpactFlattenToggle = L10n.Tr("Flatten");
        public static readonly string RootsAndImpactHideExclusiveToggle = L10n.Tr("Hide Exclusive Owned");
        public static readonly string RootsAndImpactHideExclusiveToggleTooltip = L10n.Tr("Hides any rows without shared impact to help focus on the remaining items that DO have a bigger memory impact than their own size.");
        public static readonly string RootsAndImpactRegionDataToggle = L10n.Tr("Hide Non-Object data");
        public static readonly string RootsAndImpactRegionDataToggleTooltip = L10n.Tr("Hides non-object data such as reserved native regions, empty managed heap regions, Untracked and Executable and Mapped files.");
        public static readonly string RootsAndImpactAllocatedMemoryInTableFormatString = L10n.Tr("Allocated Memory Impact In Table: {0}");
        public static readonly string RootsAndImpactResidentMemoryInTableFormatString = L10n.Tr("Resident Memory Impact In Table: {0}");
        public static readonly string RootsAndImpactBucketStaticRoots = L10n.Tr("Static Roots");
        public static readonly string RootsAndImpactBucketDontDestroyOnLoad = L10n.Tr("Don't Destroy on Load");
        public static readonly string RootsAndImpactBucketDontDestroyOnLoadDeletionQueue = L10n.Tr("Don't Destroy on Load (Deletion Queue)");
        public static readonly string RootsAndImpactBucketDontDestroyOnLoadScene = L10n.Tr("Don't Destroy on Load Scene");
        public static readonly string RootsAndImpactBucketSceneRoots = L10n.Tr("Scenes");
        public static readonly string RootsAndImpactBucketAssetBundles = L10n.Tr("Asset Bundles");
        public static readonly string RootsAndImpactBucketNativeObjectRoots = L10n.Tr("Native Object Roots");
        public static readonly string RootsAndImpactBucketUnreferencedPrefabs = L10n.Tr("Unreferenced Prefabs");
        public static readonly string RootsAndImpactBucketGCHandleRoots = L10n.Tr("GC Handle Roots");
        public static readonly string RootsAndImpactBucketNativeRootAllocations = L10n.Tr("Native Root Allocations");
        public static readonly string RootsAndImpactBucketBrokenScriptReferences = L10n.Tr("Broken Script References");
        /// <summary>Roots &amp; Impact Selection Details description for the Broken Script References bucket header.</summary>
        public static readonly string RootsAndImpactBrokenScriptReferencesDescription = L10n.Tr(
            "Native MonoBehaviour or ScriptableObject instances whose backing script (MonoScript) could not be found, " +
            "for example after a script was deleted or renamed, or failed to compile. Because they no longer map to a " +
            "managed type, nothing references them and no root keeps them alive, yet they are not cleaned up automatically. " +
            "Each entry here is a leak you can usually resolve by restoring the missing script or destroying the object.");
        /// <summary>Roots &amp; Impact Selection Details description for the Static Roots bucket header.</summary>
        public static readonly string RootsAndImpactStaticRootsDescription = L10n.Tr(
            "Memory kept alive by managed static fields. Statics persist for the whole lifetime of the loaded domain and " +
            "are only released when your code explicitly clears the reference (or on a domain reload / play-mode exit in " +
            "the Editor), which makes them the most common source of slow, accumulating leaks: a static collection, cache, " +
            "or event that is never cleared keeps everything it references alive — up to whole scenes' worth of objects. " +
            "Entries are grouped by namespace and type.");
        /// <summary>Roots &amp; Impact Selection Details description for the Don't Destroy on Load bucket header.</summary>
        public static readonly string RootsAndImpactDontDestroyOnLoadDescription = L10n.Tr(
            "Assets marked as DontDestroyOnLoad via Object.DontDestroyOnLoad() or their HideFlags. They survive scene " +
            "unloads and Resources.UnloadUnusedAssets() (the \"Asset GC\") and are only released when something explicitly " +
            "destroys them, so anything left here that you no longer need is effectively a leak for the rest of the session." +
            "Components and their GameObjects are moved into the DontDestroyOnLoad scene when they are marked as DontDestroyOnLoad and won't be found here.");
        /// <summary>Roots &amp; Impact Selection Details description for the Don't Destroy on Load (Deletion Queue) bucket header.</summary>
        public static readonly string RootsAndImpactDontDestroyOnLoadDeletionQueueDescription = L10n.Tr(
            "DontDestroyOnLoad objects that were already queued for delayed destruction but were still in memory when the " +
            "snapshot was taken. Catching objects in this state is timing-dependent and usually rare; they should be " +
            "released shortly after capture.");
        /// <summary>Roots &amp; Impact Selection Details description for the Don't Destroy on Load Scene bucket header.</summary>
        public static readonly string RootsAndImpactDontDestroyOnLoadSceneDescription = L10n.Tr(
            "The special DontDestroyOnLoad scene itself and the GameObjects and Components moved into it via Object.DontDestroyOnLoad(). Like a regular " +
            "scene it roots its hierarchy, but unlike a regular scene it is never unloaded, so its contents persist for the " +
            "entire session unless destroyed explicitly.");
        /// <summary>Roots &amp; Impact Selection Details description for the Scenes bucket header.</summary>
        public static readonly string RootsAndImpactSceneRootsDescription = L10n.Tr(
            "Regular loaded scenes and the GameObject and Component hierarchies they root. This memory is released when the " +
            "scene unloads, so the impact shown here is roughly what you would reclaim by unloading the scene. Expand a " +
            "scene to find which part of its hierarchy holds the most memory.");
        /// <summary>Roots &amp; Impact Selection Details description for the Asset Bundles bucket header.</summary>
        public static readonly string RootsAndImpactAssetBundlesDescription = L10n.Tr(
            "Assets kept loaded only because their AssetBundle is loaded. The impact excludes assets that are also " +
            "referenced from outside the bundle, so it reflects what you could actually free by unloading or re-slicing " +
            "the bundle — useful for reasoning about bundle granularity.");
        /// <summary>Roots &amp; Impact Selection Details description for the Native Object Roots bucket header.</summary>
        public static readonly string RootsAndImpactNativeObjectRootsDescription = L10n.Tr(
            "Native Unity objects that act as roots because they are a native subsystem manager, an Unused Asset or a Leaked Dynamic Asset." +
            "or because a subsystem holds them directly, rather than a managed reference, a scene, or a bundle. " +
            "They live for as long as that subsystem keeps them registered, or the unusue or leaked assets are collected by the next invocation of the \"Asset GC\": Resources.UnloadUnusedAssets().");
        /// <summary>Roots &amp; Impact Selection Details description for the Unreferenced Prefabs bucket header.</summary>
        public static readonly string RootsAndImpactUnreferencedPrefabsDescription = L10n.Tr(
            "Prefab assets that nothing in the loaded content references and that the next Resources.UnloadUnusedAssets() " +
            "(Asset GC) will unload. They are surfaced as their own root because no longer-lived root claims them.");
        /// <summary>Roots &amp; Impact Selection Details description for the GC Handle Roots bucket header.</summary>
        public static readonly string RootsAndImpactGCHandleRootsDescription = L10n.Tr(
            "Managed objects kept alive by a GCHandle that isn't tied to a Unity object — for example handles allocated by " +
            "native plugins, interop or managed code. While the handle is not freed, the scripting garbage collector cannot collect the " +
            "object it pins.");
        /// <summary>Roots &amp; Impact Selection Details description for the Native Root Allocations bucket header.</summary>
        public static readonly string RootsAndImpactNativeRootAllocationsDescription = L10n.Tr(
            "Native allocations that Unity's subsystems register as roots — native memory a subsystem owns directly rather " +
            "than through a Unity object. They live for as long as the owning subsystem keeps them around.");
        public static readonly string RootsAndImpactBucketUnrootedNativeSubsystemAllocations = L10n.Tr("Unrooted Native Subsystem Allocations");
        public static readonly string RootsAndImpactBucketVirtualMachineMemory = L10n.Tr("Virtual Machine Memory");
        public static readonly string RootsAndImpactGraphicsResourceChild = L10n.Tr("Graphics Resources (Estimated)");
        public static readonly string RootsAndImpactVMHeapWithoutNativeAllocations = L10n.Tr("VM Heap Without Unity Tracked Native Allocations");
        public static readonly string RootsAndImpactBucketReservedNative = L10n.Tr("Reserved Native Memory");
        public static readonly string RootsAndImpactBucketEmptyManagedHeap = L10n.Tr("Empty Managed Heap");
        public static readonly string RootsAndImpactBucketUntracked = L10n.Tr("Untracked");
        public static readonly string RootsAndImpactBucketExecutables = L10n.Tr("Executables & Mapped");

        // Table Column Headers - Comparison
        public static readonly string CountDifferenceTableColumnHeader = L10n.Tr("Count Difference");
        public static readonly string SizeDifferenceBarTableColumnHeader = L10n.Tr("Size Difference Bar");
        public static readonly string SizeDifferenceTableColumnHeader = L10n.Tr("Size Difference");
        public static readonly string SizeInTableColumnHeader = L10n.Tr("Size In {0}");
        public static readonly string CountInTableColumnHeader = L10n.Tr("Count In {0}");

        // Table Column Headers - Memory Map
        public static readonly string AddressTableColumnHeader = L10n.Tr("Address");
        public static readonly string TypeTableColumnHeader = L10n.Tr("Type");
        public static readonly string NameTableColumnHeader = L10n.Tr("Name");

        // Breakdown Mode Labels
        public static readonly string AllocatedMemoryBreakdownMode = L10n.Tr("Allocated Memory");
        public static readonly string ResidentMemoryBreakdownMode = L10n.Tr("Resident Memory on Device");
        public static readonly string AllocatedAndResidentMemoryBreakdownMode = L10n.Tr("Allocated and Resident Memory on Device");

        // Size Bar Labels
        public static readonly string AllocatedMemoryInTableFormat = L10n.Tr("Allocated Memory In Table: {0}");
        public static readonly string TotalMemoryInSnapshotFormat = L10n.Tr("Total Memory In Snapshot: {0}");
        public static readonly string ResidentMemoryInTableFormat = L10n.Tr("Resident Memory In Table: {0}");
        public static readonly string TotalResidentMemoryInSnapshotFormat = L10n.Tr("Total Resident Memory In Snapshot: {0}");

        // Summary Total Labels
        public static readonly string TotalAllocatedFormat = L10n.Tr("Total Allocated: {0}");
        public static readonly string TotalFormat = L10n.Tr("Total: {0}");

        // Unsupported snapshot Warning
        public static readonly string OutdatedSnapshotWarning = L10n.Tr("Memory Snapshot `{0}` is made with unsupported Unity version ({1}).");
        public static readonly string UnclearViewLoadingIssue = L10n.Tr("Something went wrong while loading the view, check the console or try again. Please report a bug if the issue persists.");

        // Tab Descriptions
        public static readonly string UnityObjectsTabDescription = L10n.Tr("A breakdown of memory contributing to all Unity Objects.");
        public static readonly string RootsAndImpactTabDescription = L10n.Tr("This view groups memory by what keeps it alive and shows the full cost of each owner - including everything it references.");
        public static readonly string AllTrackedMemoryTabDescription = L10n.Tr("A breakdown of all tracked memory that Unity knows about.");
        public static readonly string MemoryMapTabDescription = L10n.Tr("A memory map of all allocations reported for the process.");
        public static readonly string UnityObjectsComparisonTabDescription = L10n.Tr("A comparison of memory contributing to all Unity Objects in each capture.");
        public static readonly string AllTrackedMemoryComparisonTabDescription = L10n.Tr("A comparison of all tracked memory in each capture.");

        // Details Panel
        public static readonly string NoDetailsAvailable = L10n.Tr("No details available");
        public static readonly GUIContent CopyFilePath = L10n.TextContent("Copy File Path");
        public static readonly GUIContent CopyFilePathAndLine = L10n.TextContent("Copy File Path and Line");
        public static readonly GUIContent CopyLineNumber = L10n.TextContent("Copy Line Number");

        // Snapshot Tags
        public static readonly string SnapshotTagBase = "A";
        public static readonly string SnapshotTagCompare = "B";

        // Rename Validation
        public static readonly string RenameWarningEmpty = L10n.Tr("Name shouldn't be empty");
        public static readonly string RenameWarningInvalidCharacters = L10n.Tr("Name contains invalid characters");
        public static readonly string RenameWarningPathTooLong = L10n.Tr("File path is too long");
        public static readonly string RenameWarningDuplicate = L10n.Tr("Snapshot with the same name already exist");

        // Internal Mode Callstack Context Menu
        public static readonly string OpenCallstacksForUnknownRoot = L10n.Tr("Open Callstacks Table for Unknown Root (if callstack info was captured)");
        public static readonly string OpenInvertedCallstacksForUnknownRoot = L10n.Tr("Open (Inverted) Callstacks Table for Unknown Root (if callstack info was captured)");

        // ObjectOrTypeLabel
        public static readonly string UnrootedAllocationLabel = L10n.Tr("Unrooted Allocation - This is a bug in Unity's source code");
        public static readonly string GCHandleTypeName = L10n.Tr("GCHandle");
        public static readonly string SceneNameSuffix = L10n.Tr("Scene");
        public static readonly string PrefabNameSuffix = L10n.Tr("Prefab");

        // Details Panel Row Labels
        public static readonly string TypeDescriptionText = L10n.Tr("The selected item is a Type.");
        public static readonly string StatusRowLabel = L10n.Tr("Status");
        public static readonly string HintRowLabel = L10n.Tr("Hint");
        public static readonly string ManagedTypeRowLabel = L10n.Tr("Managed Type");
        public static readonly string NativeTypeRowLabel = L10n.Tr("Native Type");
        public static readonly string PrefabNameRowLabel = L10n.Tr("Prefab Name");
        public static readonly string SceneNameRowLabel = L10n.Tr("Scene Name");
        public static readonly string ScenePathRowLabel = L10n.Tr("Scene Path");
        public static readonly string NativeSizeRowLabel = L10n.Tr("Native Size");
        public static readonly string GraphicsSizeRowLabel = L10n.Tr("Graphics Size");
        public static readonly string RootIdRowLabel = L10n.Tr("Root ID");
        public static readonly string GfxResourceIdRowLabel = L10n.Tr("Gfx Resource ID");
        public static readonly string AreaRowLabel = L10n.Tr("Area");
        public static readonly string ObjectNameRowLabel = L10n.Tr("Object Name");
        public static readonly string SizeRowLabel = L10n.Tr("Size");
        public static readonly string ChildCountRowLabel = L10n.Tr("Child Count");
        public static readonly string ManagedSizeRowLabel = L10n.Tr("Managed Size");
        public static readonly string LengthRowLabel = L10n.Tr("Length");
        public static readonly string RankRowLabel = L10n.Tr("Rank");
        public static readonly string StringValueRowLabel = L10n.Tr("String Value");
        public static readonly string StringDataUnavailable = L10n.Tr("(String data unavailable)");
        public static readonly string ReferencedByRowLabel = L10n.Tr("Referenced By");
        public static readonly string FoundReferencesRowLabel = L10n.Tr("Found References");
        public static readonly string ManagedAddressRowLabel = L10n.Tr("Managed Address");
        public static readonly string NativeAddressRowLabel = L10n.Tr("Native Address");
        public static readonly string NativeIndexRowLabel = L10n.Tr("Native Index");
        public static readonly string ManagedIndexRowLabel = L10n.Tr("Managed Index");
        public static readonly string TotalAllocatedRowLabel = L10n.Tr("Total Allocated");
        public static readonly string TotalResidentSizeRowLabel = L10n.Tr("Total Resident Size");
        public static readonly string RuntimeEstimationRowLabel = L10n.Tr("Runtime estimation");
        public static readonly string RuntimeEstimationTooltip = L10n.Tr("This is the value that would've been returned by calling GetRuntimeMemorySizeLong at runtime. It combines native and gpu allocation estimates.");
        public static readonly string FlagsRowLabel = L10n.Tr("Flags");
        public static readonly string HideFlagsRowLabel = "HideFlags";
        public static readonly string NoneLabel = L10n.Tr("None");
        public static readonly string BugRowLabel = L10n.Tr("Bug!");
        public static readonly string EntityIdRowLabel = "Entity ID";
        public static readonly string InstanceIdRowLabel = "Instance ID";
        public static readonly string SelfReferencesRowLabel = L10n.Tr("Self References");
        public static readonly string SelfReferencesSuffix = L10n.Tr(" + 2 Self References");
        public static readonly string SelfReferencesHint = L10n.Tr("The Managed and Native parts of this UnityEngine.Object reference each other. This is normal.");
        public static readonly string SelfReferencesOnlyConnectionHint = L10n.Tr("Nothing else references them though so the Native part keeps the Managed part alive.");
        public static readonly string InvalidManagedObjectAssociationMessage = L10n.Tr("This Native Object is associated with an invalid Managed Object, please report a bug via the Window Menu Bar option 'Help > Report a Bug'. Please attach this snapshot, info on how to find this object in the snapshot, and a project to reproduce this with.");
        public static readonly string InvalidObjectLabel = L10n.Tr("Invalid Object");
        public static readonly string NativeManagerStatus = L10n.Tr("Native Manager");
        public static readonly string NativeManagerHint = L10n.Tr("This is Native Manager that is represents one of Unity's subsystems.");
        public static readonly string HideAllocationListButton = L10n.Tr("Hide Allocation List");
        public static readonly string ListAllAllocationsButton = L10n.Tr("List all Allocations");
        public static readonly string PotentialLogicFlawLabel = L10n.Tr("Potential Logic Flaw?");
        public static readonly string PotentialLogicFlawContent = L10n.Tr("This multidimensional array has a zero sized dimension and therefore no elements. Is this intended?");
        public static readonly string TipRowLabel = L10n.Tr("Tip");
        public static readonly string TipUnnamedDynamicAssetFormat = L10n.Tr("This dynamically created Asset doesn't have a name, which will make it harder to find its source. " +
            "As a first step, search your entire project code for any instances of {0} " +
            "and every 'Instantiate()' or similar call that would create an instance of this type, " +
            "and make sure you set the '.name' property of the resulting object to something that will make it easier to understand what this object is being created for.");

        // Leaked Shell Object Status & Hint
        public static readonly string LeakedShellStatusFormat = L10n.Tr("{0} " + k_LeakedManagedShellName + " of {1}");
        public static readonly string LeakedShellStatusReferenced = L10n.Tr("Referenced");
        public static readonly string LeakedShellStatusCollectable = L10n.Tr("GC.Collect()-able");
        public static readonly string LeakedShellOfSceneObject = L10n.Tr("a Scene Object");
        public static readonly string LeakedShellOfAsset = L10n.Tr("an Asset");
        public static readonly string LeakedShellHintFormat = L10n.Tr("This Unity Object is a " + k_LeakedManagedShellName + ". That means this object's type derives from UnityEngine.Object " +
            "and the object therefore, normally, has a Native Object accompanying it. " +
            "If it is used by Managed (C#) Code, a Managed Shell Object is created to allow access to the Native Object. " +
            "In this case, the Native Object has been destroyed, either via Destroy() or because the {0} it was in was unloaded. " +
            "After the Native Object was destroyed, the Managed Garbage Collector hasn't yet collected this object. {1}");
        public static readonly string LeakedShellHintSceneLabel = L10n.Tr("Scene");
        public static readonly string LeakedShellHintAssetBundleLabel = L10n.Tr("Asset Bundle");
        public static readonly string LeakedShellHintReferencedEnding = L10n.Tr("This is because the Managed Shell is still being referenced and can therefore not yet be collected. " +
            "You can fix this by explicitly setting each field referencing this Object to null (comparing it to null will claim it already is null, as it acts as a \"Fake Null\" object), " +
            "or by ensuring that each referencing object is no longer referenced itself, so that all of them can be unloaded.");
        public static readonly string LeakedShellHintUnreferencedEnding = L10n.Tr("Nothing is referencing this Object anymore, so it should be collected with the next GC.Collect.");

        // Dynamic Asset Status & Hint
        // Format: "{0}{1}{2}{3} {4}" where:
        //   {0} = "Referenced " or ""
        //   {1} = "DontDestroyOnLoad " or "Leaked " or ""
        //   {2} = "Dynamically" or "dynamically" (capitalized when {0} and {1} are empty)
        //   {3} = " & runtime created" or " & build-time created" or " created" (future: EntityId 8-byte snapshots can't distinguish runtime vs build-time)
        //   {4} = "Asset"
        public static readonly string DynamicAssetStatusFormat = "{0}{1}{2}{3} {4}";
        public static readonly string DynamicAssetStatusReferenced = L10n.Tr("Referenced ");
        public static readonly string DynamicAssetStatusDontDestroyOnLoad = "DontDestroyOnLoad ";
        public static readonly string DynamicAssetStatusLeaked = L10n.Tr("Leaked ");
        public static readonly string DynamicAssetStatusDynamically = L10n.Tr("Dynamically");
        public static readonly string DynamicAssetStatusDynamicallyLower = L10n.Tr("dynamically");
        public static readonly string DynamicAssetStatusRuntimeCreatedSuffix = L10n.Tr(" & runtime created");
        public static readonly string DynamicAssetStatusBuildTimeCreatedSuffix = L10n.Tr(" & build-time created");
        public static readonly string DynamicAssetStatusCreatedSuffix = L10n.Tr(" created");
        public static readonly string DynamicAssetStatusAsset = L10n.Tr("Asset");
        public static readonly string DynamicAssetHintFormat = L10n.Tr("This is a dynamically created Asset Type object, that was either Instantiated, implicitly duplicated or explicitly constructed via {0}{1}");
        public static readonly string DynamicAssetHintDontDestroyOnLoad = L10n.Tr("It is marked as 'DontDestroyOnLoad', so that it will never be unloaded by a Scene unload or an explicit call to 'Resources.UnloadUnusedAssets()'." +
            " If you want to get rid of it, you will need to call 'Destroy()' on it or not mark it as 'DontDestroyOnLoad'");
        public static readonly string DynamicAssetHintReferenced = L10n.Tr("It is still referenced, but if it should no longer be used, it will need to be unloaded explicitly by calling 'Destroy()' on it or " +
            "removing all references to it and relying on the more costly and broad sweeping indirect method of " + k_TriggerAssetGCHint);
        public static readonly string DynamicAssetHintLeakedFormat = L10n.Tr("This object is not referenced anymore. It is therefore leaked! " +
            "Remember to unload these objects by explicitly calling 'Destroy()' on them, or via the more costly and broad sweeping indirect method of " + k_TriggerAssetGCHint);
        const string k_TriggerAssetGCHint = "triggering 'Resources.UnloadUnusedAssets()', explicitly or e.g. via a non-additive Scene unload.";

        // Persistent Asset Hint
        public static readonly string PersistentAssetStatusFormat = L10n.Tr("{0}{1}{2}Asset");
        public static readonly string PersistentAssetStatusUsed = L10n.Tr("Used ");
        public static readonly string PersistentAssetStatusUnused = L10n.Tr("Unused ");
        public static readonly string PersistentAssetStatusDontDestroyOnLoad = "DontDestroyOnLoad ";
        public static readonly string PersistentAssetStatusRuntimeCreated = L10n.Tr("Runtime Created ");
        public static readonly string PersistentAssetStatusLoaded = L10n.Tr("Loaded ");
        public static readonly string PersistentAssetHintRuntimeCreated = L10n.Tr("This is an Asset that was created at runtime and later associated with a file.");
        public static readonly string PersistentAssetHintUsed = L10n.Tr("This is an Asset that is used by something in your Application. " +
            "If you didn't expect to see this Asset at this point in your application's lifetime, check the References panel to see what is using it. " +
            "If you expected it to be smaller, check the Asset's Import settings.");
        public static readonly string PersistentAssetHintDontDestroyOnLoad = L10n.Tr("This is an Asset that appears to no longer be used by anything in your Application but is held in Memory because it is marked as 'DontDestroyOnLoad'. " +
            "To unload it, you need to call 'Destroy()' on it or not mark it as 'DontDestroyOnLoad'");
        public static readonly string PersistentAssetHintBrokenScript = L10n.Tr("This object lost its connection to its Script file. Fix it up or delete it.");
        public static readonly string PersistentAssetHintUnused = L10n.Tr("This is an Asset that appears to no longer be used by anything in your Application. " +
            "It may have been used earlier but now is just waiting for the next sweep of 'Resources.UnloadUnusedAssets()' to unload it. You can test that hypothesis by " + k_TriggerAssetGCHint);

        // Scene Object Status & Hint
        public static readonly string SceneObjectStatusFormat = L10n.Tr("{0}{1}Scene Object");
        public static readonly string SceneObjectStatusRuntimeCreated = L10n.Tr("Runtime Created ");
        public static readonly string SceneObjectStatusLoaded = L10n.Tr("Loaded ");
        public static readonly string SceneObjectStatusDontDestroyOnLoad = "DontDestroyOnLoad ";
        public static readonly string SceneObjectHintIntro = L10n.Tr("This is a Scene Object, i.e. a GameObject or a Component on it. ");
        public static readonly string SceneObjectHintInstantiated = L10n.Tr("It was instantiated after the Scene was loaded. ");
        public static readonly string SceneObjectHintLoaded = L10n.Tr("It was loaded in as part of a Scene. ");
        public static readonly string SceneObjectHintDontDestroyOnLoad = L10n.Tr("It is marked as 'DontDestroyOnLoad' so to unload it, you would need to call 'Destroy()' on it, or not mark it as 'DontDestroyOnLoad'");
        public static readonly string SceneObjectHintUnloadGameObject = L10n.Tr("Its Native Memory will be unloaded once the Scene it resides in is unloaded or the GameObject or its parents are destroyed via 'Destroy()'. ");
        public static readonly string SceneObjectHintUnloadComponent = L10n.Tr("Its Native Memory will be unloaded once the Scene it resides in is unloaded or the GameObject it is attached to or its parents are destroyed via 'Destroy()'. ");
        public static readonly string SceneObjectHintManagedShellWarning = L10n.Tr("Its Managed memory may live on as a Leaked Shell Object if something else that was not unloaded with it still references it.");

        // Memory Impact Labels
        public static readonly string TotalImpactRowLabel = L10n.Tr("Total Impact");
        public static readonly string OwnedImpactRowLabel = L10n.Tr("Owned Impact");
        public static readonly string SharedImpactRowLabel = L10n.Tr("Shared Impact");
        public static readonly string NativeImpactRowLabel = L10n.Tr("Native Impact");
        public static readonly string ManagedImpactRowLabel = L10n.Tr("Managed Impact");
        public static readonly string GraphicsImpactRowLabel = L10n.Tr("Graphics Impact");
        public static readonly string ExclusivelyOwnedReferencedMemoryLabel = L10n.Tr("Exclusively Owned Referenced Memory");
        public static readonly string SharedReferencedMemoryLabel = L10n.Tr("Shared Referenced Memory");
        public static readonly string ImpactTooltipFormat = L10n.Tr("Allocated: {0} ({1:N0} B) \nResident: {2} ({3:N0} B)\n\n{4}");
        public static readonly string NativeAndManagedRefCountFormat = L10n.Tr("({0} Native + {1} Managed)");

        // Callstack Buttons and Labels
        public static readonly string ExportCallstacksJsonButton = L10n.Tr("Export Callstacks for Root (json)");
        public static readonly string ExportInvertedCallstacksJsonButton = L10n.Tr("Export (Inverted) Callstacks for Root (json)");
        public static readonly string ExportCallstacksCsvButton = L10n.Tr("Export Callstacks for Root (csv)");
        public static readonly string ExportInvertedCallstacksCsvButton = L10n.Tr("Export (Inverted) Callstacks for Root (csv)");
        public static readonly string OpenCallstacksTableButton = L10n.Tr("Open Callstacks Table for Root");
        public static readonly string OpenInvertedCallstacksTableButton = L10n.Tr("Open (Inverted) Callstacks Table for Root");
        public static readonly string CopyCallStackButtonFormat = L10n.Tr("Copy {0} Call Stack(s)");
        public static readonly string CopyFirstCallStacksButtonFormat = L10n.Tr("Copy First {0} Call Stack(s)");
        public static readonly string CopyAllCallStacksButton = L10n.Tr("Copy All Call Stacks");
        public static readonly string CopyAllCallStacksTooltipFormat = L10n.Tr("Copy All {0} Call Stacks to the clipboard.");
        public static readonly string CopyCallStacksTooltipFormat = L10n.Tr("Copy {0} Call Stack(s) to the clipboard.");
        public static readonly string ShownCallStacksRowLabel = L10n.Tr("Shown Call Stacks");
        public static readonly string CallStacksRowLabel = L10n.Tr("Call Stacks");
        public static readonly string FurtherCallStacksRowLabel = L10n.Tr("Further Call Stacks");
        public static readonly string AllocationsCountRowLabel = L10n.Tr("Allocations Count");
        public static readonly string ClickableCallStacksToggle = L10n.Tr("Clickable Call Stacks");
        public static readonly string ClickableCallStacksTooltip = L10n.Tr("Call Stacks can either be clickable (leading to the source file) or selectable. Toggle this off if you want them to be selectable.");
        public static readonly string ShowAddressInCallStacksToggle = L10n.Tr("Show Address in Call Stacks");
        public static readonly string ShowAddressInCallStacksTooltip = L10n.Tr("Show or hide Address in Call Stacks.");

        // Selected Item Details Text - Summary View

        public static readonly string ResidentMemoryDescription = L10n.Tr("The application footprint in physical memory. It includes all Unity and non-Unity allocations resident in memory at the time of the capture.");

        public static readonly string ExecutablesAndMappedDescription = L10n.Tr("Memory taken up by the build code of the application, including all shared libraries and assemblies, managed and native. This value is not yet reported consistently on all platforms." +
        "\n\nYou can reduce this memory usage by using a higher code stripping level and by reducing your dependencies on different modules and libraries.");
        public static readonly string NativeDescription = L10n.Tr("Native memory, used by objects such as:" +
        "\n- Scene Objects (Game Objects and their Components)," +
        "\n- Assets and Managers" +
        "\n- Native Allocations including Native Arrays and other Native Containers" +
        "\n- CPU side of Graphics Asset memory" +
        "\n- And other" +
        "\n\nThis doesn't include Graphics, which is shown in a separate category." +
        $"\n\nYou can inspect these categories further in the {AllOfMemoryViewName} Breakdown." +
        $"\n\nNote: Values in {SummaryViewName} and {AllOfMemoryViewName} views might be different as they use different ways of grouping items together.");
        public static readonly string NativeReservedDescription = L10n.Tr("Reserved memory is memory that Unity allocated from the system (OS) but isn't used by any Unity objects or native allocations at the moment of the capture. " +
            "There are many reasons why Unity might allocate memory for it internal allocators:" +
            "\n- Loading of resources" +
            "\n- Direct allocation by the user" +
            "\n- Memory-heavy computations" +
            "\n- Creation & destruction of GameObjects and their components" +
            "\n\nMost Unity allocators allocate memory in chunks, and when a chunk isn't used anymore it's released back to the system. " +
            "If you observe a high value of reserved memory it's most probably caused by fragmentation. " +
            "When memory is fragmented small objects might still reside in a chunk and cause it to remain allocated by Unity. " +
            "You can investigate which allocator has the highest reserved memory value by turning on \"Show reserved memory breakdown\" in the Memory Profiler settings. " +
            "You can read more about different types of Unity allocators and their settings on the \"Memory allocator customization\" documentation page. " +
            "\n\nIf you want to reduce reserved memory, the general recommendations are:" +
            "\n- Unload your assets before loading a new scene" +
            "\n- Use \"temp\" allocator for short-living allocations if you use unsafe utilities API" +
            "\n- Customize allocators for your specific needs if Unity pre-allocate too much on start (recommended only for advanced users)");
        public static readonly string GraphicsEstimatedDescription = L10n.Tr("Estimated memory used by the Graphics Driver and the GPU to visualize your application." +
            $"\nThe information is based on the tracking of graphics resource allocations within Unity. This includes RenderTextures, Textures, Meshes, Animations and other graphics buffers which are allocated by Unity or Scripting API. Use {AllOfMemoryViewName} tab to explore graphics resources." +
            $"\nNot all these objects' memory is represented in this category. For example, Read/Write enabled graphics assets need to retain a copy in CPU-accessible memory, which doubles their total memory usage. Use {UnityObjectsViewName} tab to explore total memory usage of Unity Objects. " +
            "Also, not necessarily all memory from these type of objects resides in GPU memory. Memory Profiler is unable to get exact residence information for graphics resources.");
        public static string GraphicsEstimatedDisabledDescription => L10n.Tr("Estimated memory used by the Graphics Driver and the GPU to visualize your application." +
            "\nThe information is based on the process memory regions reported by the operating system. This includes display buffers, RenderTextures, Textures, Meshes, Animations." +
            "\n\nNote: The current platform does not provide device memory information and we can not determine resident memory details of graphics memory. " +
            $"We defer analysis to the '{AllTrackedMemoryModelBuilder.UntrackedGroupName}' group which accurately represents resident and allocated memory status and is based on memory regions information provided by the operating system. " +
            $"And we keep '{AllTrackedMemoryModelBuilder.GraphicsGroupName}' group only for reference as a disabled view item." +
            "\n\nUse 'Allocated Memory' view to inspect Graphics memory in more details.");
        public static readonly string ManagedDescription = L10n.Tr("Contains all Virtual Machine and Managed Heap memory" +
        "\n\nThe Managed Heap contains data related to Managed Objects and the space that has been reserved for them. It is managed by the Scripting Garbage Collector, so that any managed objects that no longer have references chain to a root are collected." +
        "\n\nThe used amount in the Managed Memory is made up of memory used for Managed objects and of empty space that cannot be returned." +
        "\n\nThe 'reserved' amount in this category may quickly be reused if needed, or it will be returned to the system every 6th GC.Collect sweep.");
        public static readonly string ManagedReservedDescription = L10n.Tr("Reserved memory is memory that the Unity Managed Heap allocated from the system (OS) but isn't used by any managed objects at the moment of the capture. " +
            "\n\nThe Managed Heap is allocated in blocks which store managed objects of similar size. Each block can store some amount of such objects and if it stays empty for several GC passes the block is released to the OS at page size granularity. " +
            "Managed Heap blocks might get fragmented and still contain just a few objects out of a capacity of thousands. Pages that still contain objects are still considered used, so their memory can't be returned to the system and they count towards 'reserved'.");
        public static readonly string AudioDescription = L10n.Tr("Memory used for Unity's Audio system, including AudioClips and playback buffers.");
        public static string UntrackedDescription => L10n.Tr("Memory that the memory profiler cannot yet account for, due to platform specific requirements, potential bugs or other gaps in memory tracking. " +
            $"\nThe size of {AllTrackedMemoryModelBuilder.UntrackedGroupName} memory is determined by analyzing all allocated and resident memory regions of the process and subtracting known regions which Unity native and managed memory allocators use." +
            "\nTo analyze this memory further, you will need to use a platform specific profiler." +
            $"\n\nNote: {AllTrackedMemoryModelBuilder.UntrackedGroupName} memory might include a portion of '{AllTrackedMemoryModelBuilder.GraphicsGroupName}' group. " +
            $"We do know accurate information about {AllTrackedMemoryModelBuilder.UntrackedGroupName} memory regions, but we are not able to determine contribution of individual graphics resources to the specific memory region." +
            $"\nThus we display {AllTrackedMemoryModelBuilder.UntrackedGroupName} regions according to the system information and disable '{AllTrackedMemoryModelBuilder.GraphicsGroupName}' group in the view.");
        public static string UntrackedEstimatedDescription => L10n.Tr("Memory that the memory profiler cannot yet account for, due to platform specific requirements, potential bugs or other gaps in memory tracking. " +
            $"\nThe size of {AllTrackedMemoryModelBuilder.UntrackedGroupName} memory is determined by analyzing all allocated and resident memory regions of the process and subtracting known regions which Unity native and managed memory allocators." +
            "\nTo analyze this memory further, you will need to use a platform specific profiler." +
            $"\n\n*: When calculating Allocated size of {AllTrackedMemoryModelBuilder.UntrackedGroupName} memory we also subtract the size of '{AllTrackedMemoryModelBuilder.GraphicsGroupName}'. " +
            "We do know that certain types of memory regions are allocated for the graphics device, however we are unable to determine mapping of individual graphics resources to those regions. " +
            $"Thus we subtract the total '{AllTrackedMemoryModelBuilder.GraphicsGroupName}' size from regions which belong to the graphics device, and then from the biggest regions if we are unable to determine device regions." +
            $"\n\nThis means that the numbers for the {AllTrackedMemoryModelBuilder.UntrackedGroupName} category in 'Allocated Memory' mode are different from the numbers in 'Resident Memory' and 'Allocated and Resident Memory' modes to allow for in depth inspection." +
            $"\n - Use 'Allocated and Resident Memory' or 'Resident Memory' views to accurately inspect {AllTrackedMemoryModelBuilder.UntrackedGroupName} memory and its residency status." +
            "\n - Use 'Allocated Memory' view to inspect Graphics memory in more details.");

        public static readonly string ManagedDomainDescription = L10n.Tr("The Virtual Machine memory contains data that the Scripting Backend (Mono or IL2CPP) needs in order to function, e.g.:\n" +
        "- Type MetaData for every used managed Type, including:\n" +
        "  - The definition of their fields and functions (vTable)\n" +
        "  - Static field data\n" +
        "- Data needed for Generic Types\n" +
        "\n\nWhen the Memory Profiler takes a capture, all Types that it finds while reporting the contents of the Managed Heap are initialized and their Type MetaData is therefore 'inflated' with data. " +
        "A growth in Virtual Machine Memory between the first and second capture in a session can usually be mostly attributed to that. " +
        "Uninitialized types are those for which no one accessed any Type-specific data yet, and their static constructor, explicit or implicit, has therefore not been called yet. " +
        "This could often be the type of arrays or generic Collections where their data has so far only been handled as a generic collection.");
        public static readonly string ManagedDomainDescriptionStaticFieldAddendumOneSnapshot = L10n.Tr("\n\n{0} of the VM memory are taken up by static field data.");
        public static readonly string ManagedDomainDescriptionStaticFieldAddendumDiff = L10n.Tr("\n\n{0} in snapshot A and {1} in snapshot B of the VM memory are taken up by static field data.");
        public static readonly string ManagedObjectsDescription = L10n.Tr("Memory used for Managed Objects, i.e. those that can be created and used from C# scripting code based on Types defined in C# scripting code. " +
        "This number only includes objects that are still referenced or held otherwise alive. Managed objects that the Garbage Collector still has to collect could reside in the Empty Active / Fragmented Heap Space.");
        public static readonly string EmptyActiveHeapDescription = L10n.Tr("This snapshot version does not yet contain a definitive report on which Managed Heap Section is the Active one. " +
        "Therefore the Contiguous Managed Heap section at the highest virtual address value is assumed to be the highest one, which is likely to be correct. " +
        "This bar shows the amount of unused memory in that section." +
        "\n\nWhen allocating new managed memory for Managed Objects, the Active Heap Section is checked for empty space first, which is the fastest way to allocate it. " +
        "If there is not enough Contiguous empty space in it, the Scripting VM needs to scan the Empty Block list and the remaining heap sections for space, which takes longer. " +
        "If no fitting place is found for the Object, GC Collection is triggered. When using the Incremental GC, a new heap section is immediately allocated (expanding the heap) for the new Object, " +
        "while the Garbage Collection happens asynchronously. When it is not used, a new section is only allocated if there is not enough space after the collection either." +
        "\n\nThis Memory could also still be occupied by Objects that have been abandoned after the last GC.Alloc sweep and are waiting for collection in the next one.");

        /// <summary>Selection Details description for the Virtual Machine Memory group (Roots &amp; Impact and All Of Memory).</summary>
        public static readonly string VirtualMachineMemoryDescription = L10n.Tr(
            "Native memory owned by the scripting virtual machine itself: type metadata, JIT/AOT " +
            "structures, and the bookkeeping behind the managed heap, plus any reserved-but-unoccupied managed-heap " +
            "sections. This is intrinsic runtime overhead rather than memory attributable to a specific object, and it " +
            "scales with the amount and complexity of code and types in your build.");
        /// <summary>Selection Details description for the Unrooted Native Subsystem Allocations group (Roots &amp; Impact and All Of Memory).</summary>
        public static readonly string UnrootedNativeSubsystemAllocationsDescription = L10n.Tr(
            "Native allocations Unity reported that are neither registered to a native root nor (in the Roots & Impact view) reachable from anything the " +
            "crawler can trace. Because no owner could be attributed to them, they're shown as their own group so the table " +
            "still reconciles with the snapshot total. A large or growing value here can indicate native memory that is " +
            "leaking or that the profiler cannot yet attribute.\n\n" +
            "You can get more information on these in the All Of Memory view when capturing from a Unity 6000.3+ Player or Editor started with the commandline option '-enable-memoryprofiler-callstacks'");

        public static readonly string TopUnityObjectCategoriesDescription = L10n.Tr("To further analyze this Unity Object type's memory usage, switch to the Unity Objects tab.");
        public static readonly string MemoryProfilerModuleUnityObjectUsageDescription = L10n.Tr("To further analyze this Unity Object type's memory usage, open the Memory Profiler and take a snapshot.");

        public static readonly string MemoryProfilerModuleGCAllocBreakdownName = L10n.Tr("GC Allocated In Frame");
        public static readonly string MemoryProfilerModuleGCAllocMainThread = L10n.Tr("GC.Alloc on Main Thread");
        public static readonly string MemoryProfilerModuleGCAllocMainThreadDescription = L10n.Tr("Manage allocations that happened on Main Thread");
        public static readonly string MemoryProfilerModuleGCAllocMainThreadEditorOnly = L10n.Tr("GC.Alloc on Main Thread [Editor Only]");
        public static readonly string MemoryProfilerModuleGCAllocMainThreadEditorOnlyDescription = L10n.Tr("Manage allocations that happened on Main Thread but would only ever happen in the Editor");
        public static readonly string MemoryProfilerModuleGCAllocOtherThreads = L10n.Tr("GC.Alloc on other threads");
        public static readonly string MemoryProfilerModuleGCAllocOtherThreadsDescription = L10n.Tr("Manage allocations that happened on other threads");

        // Selected Item Details Text - Individual Object View

        public static readonly string NativeObjectHeaderForImpactDifferentiator = L10n.Tr("Native Object");
        public static readonly string ManagedShellHeaderForImpactDifferentiator = L10n.Tr("Managed Shell");
        public static readonly string AttributedImpactInfo = L10n.Tr("Represents the total memory kept alive by this object, including memory it exclusively references, partial amounts of memory shared with other objects, and its own size.");
        public static readonly string AttributedImpactInfoNotAvailable = L10n.Tr("Impact and Path-To-Root data could not be generated for this snapshot. Try reopening it, and if the problem persists, please report a bug.");
        public static readonly string AttributedImpactInfoNotAvailableForOldSnapshots = L10n.Tr("Impact and Path-To-Root info can't be generated for snapshots taken from Unity versions 2022.1 or older.");
        /// <summary>Shown when the flattened Roots &amp; Impact view would produce too many rows to fit in memory. {0} = approximate row count.</summary>
        public static readonly string RootsAndImpactFlattenedSnapshotTooLarge = L10n.Tr("This snapshot has too many entries (~{0:N0}) to list in the flattened Roots & Impact view. Turn off flattening to inspect it as a hierarchy instead.");
        /// <summary>Tooltip on the previous-page arrow of the flattened pager.</summary>
        public static readonly string PagerPreviousPage = L10n.Tr("Previous page");
        /// <summary>Tooltip on the next-page arrow of the flattened pager.</summary>
        public static readonly string PagerNextPage = L10n.Tr("Next page");
        /// <summary>Tooltip set on the Description column header when the flat view has more rows than the fast-sort threshold; warns the user sorting may be slow. {0} = row count (formatted).</summary>
        public static readonly string FlattenedNameSortSlowTooltip = L10n.Tr("Sorting {0:N0} rows by name resolves every name from the snapshot and may take a moment, especially when combined with other sort columns. Size and status columns alone sort natively and are near instant.");
        /// <summary>Tooltip set on the Description column header when the flat view has more rows than name sorting supports; clicking the header has no effect on the sort order, but other active sort columns still apply. {0} = row count (formatted).</summary>
        public static readonly string FlattenedNameSortDisabledTooltip = L10n.Tr("Sorting by name is disabled for this many rows ({0:N0}) to avoid running out of memory; any other active sort columns still apply. Size and status columns alone sort natively and are unaffected.");
        public static readonly string AttributedImpactTotalTooltip = L10n.Tr("This is how much memory this object is responsible for keeping alive, including memory for which it shares its ownership with other objects (divided up amongst these owners), memory it exclusively references, as well as its own size.");
        const string k_AttributedImpactExclusivelyOwnedSharedImpactAddendum = "If an object exclusively owns all owners of a shared object, their impact is gathered into its shared impact.";
        public static readonly string AttributedImpactExclusiveTooltip = L10n.Tr("This is how much memory this object is exclusively responsible for keeping alive, which is comprised of memory it exclusively references, as well as its own size. " +
            k_AttributedImpactExclusivelyOwnedSharedImpactAddendum);
        public static readonly string AttributedImpactSharedTooltip = L10n.Tr("This is the amount of memory for which this object shares its ownership with other objects. The referenced memory has been divided up amongst these owners. " +
            k_AttributedImpactExclusivelyOwnedSharedImpactAddendum);

        public static readonly string NonpersistentSceneObjectWithInvalidDontDestroyOnLoadHideFlagWarningFormatString = L10n.Tr("The non-prefab {0} named: \"{1}\" was marked with HideFlags.DontUnloadUnusedAsset while not being an Asset. That's not supported and the flag will be ignored by the Asset GC, which means it likely does not behave as you would expect it to and could lead to further unexpected behavior and errors. Fix this in your code by switching away from using the hideFlags property, using the method Object.DontDestroyOnLoad() instead. This will then move your non-prefab scene objects into the DontDestroyOnLoad scene, thereby actually ensuring that they won't be unloaded.");

        public static readonly string RepeatReferencesLabel = L10n.Tr("Repeat References");
        public static readonly string RepeatReferencesTooltip = L10n.Tr("'Repeat References' are references from managed objects already listed in the 'Referenced By' list but e.g. originating from a different field or array index. The 'Referenced By' list does not list each such reference for the sake of brevity.");
        public static readonly string NonUnityObjectGCHandleReferencesLabel = L10n.Tr("GCHandles");
        public static readonly string NonUnityObjectGCHandleReferencesTooltip = L10n.Tr("'GCHandles' are references from GCHandles, excluding those held by the native side of a Unity Object. Existing native Unity Object references via GCHandles are listed in the 'Referenced By' list as a reference from the native object, instead of via its GCHandle.");

        public static readonly string UsedByNativeCodeStatus = L10n.Tr("Empty Array Required by Unity's subsystems");
        public static readonly string UsedByNativeCodeHint = L10n.Tr("This array's Type is marked with a [UsedByNativeCode] or [RequiredByNativeCode] Attribute in the Unity code-base and the array exists so that the Type is not compiled out on build. It is held in memory via a GCHandle. You can search the public C# reference repository for those attributes https://github.com/Unity-Technologies/UnityCsReference/.");

        public static readonly string HeldByGCHandleStatus = L10n.Tr("Held Alive By GCHandle");
        public static readonly string HeldByGCHandleHint = L10n.Tr("This object is pinned or otherwise held in memory because a GCHandle was allocated for it.");

        public static readonly string InDelayedDeletionQueueStatus = L10n.Tr("Queued for Delayed Deletion");
        public static readonly string InDelayedDeletionQueueHint = L10n.Tr("This Asset was queued for deletion off of the main thread and is waiting for deletion to happen on the main thread. This is also why it has no name anymore.");

        public static readonly string GCHandleHeldManagedObjectStatus = HeldByGCHandleStatus;
        public static readonly string GCHandleHeldManagedObjectHint = L10n.Tr("This managed object is pinned or otherwise held in memory because a GCHandle was allocated for it.");

        public static readonly string LeakedShellHeldByGCHandleStatus = L10n.Tr(k_LeakedManagedShellName + " Held Alive By GCHandle");
        public static readonly string LeakedShellHeldByGCHandleHint = L10n.Tr("This Unity Object is held alive by a GCHandle, yet no native object holding its GCHandle was captured in the snapshot. This could be because some other code took a GCHandle to it, or due to a bug where native objects could change between taking a snapshot of the managed heap and capturing native objects. This was fixed in 6000.0.16f1, 2022.3.43f1, and 2021.3.44f1 so if you are on Unity versions higher than those, look for code that allocates GCHandles.");

        public static readonly string ReferencedManagedObjectStatus = L10n.Tr("Referenced Managed Object");
        public static readonly string ReferencedManagedObjectHint = L10n.Tr("This managed object is being referenced. If you did not expect to see it, check the references panel to see where those references are coming from");

        public static readonly string UnknownLivenessReasonStatus = L10n.Tr("Bug: Liveness Reason Unknown");
        public static readonly string UnknownLivenessReasonHint = L10n.Tr("There is no reference pointing to this object and no GCHandle reported for it. This is a Bug, please report it using 'Help > Report a Bug' and attach the snapshot to the report.");

        public static readonly string GCHandleStatus = L10n.Tr("A GCHandle");
        const string k_GCHandleDescription = "A GCHandle that was allocated to reference this managed object.";
        public static readonly string GCHandleHint = L10n.Tr(k_GCHandleDescription + " This GCHandle is not associated with the native object behind the managed shell of a Unity Object. Check your code if you call GCHandle.Alloc(), UnsafeUtility.PinGCArrayAndGetDataAddress(), or UnsafeUtility.PinGCObjectAndGetAddress() anywhere.");

        public static readonly string PrefabStatus = L10n.Tr("A Prefab");
        public static readonly string PrefabHint = L10n.Tr("The Prefab class does not really exist at runtime so this is more of a virtual Prefab entry used by the Memory Profiler as a root for the Prefab's root transform. If you see something referencing this Prefab where you would expect that reference to be targeting something deeper into the Prefab, this is expected as the Memory Profiler redirects that reference to the root of the Prefab for a cleaner Hierarchy reconstruction of the Prefab.");
        public static readonly string SceneStatus = L10n.Tr("A Loaded Scene");
        public static readonly string DontDestroyOnLoadSceneStatus = L10n.Tr("The Don't Destroy On Load Scene");
        public static readonly string DontDestroyOnLoadSceneHint = L10n.Tr("The Don't Destroy On Load Scene is where Unity moves all GameObjects that were marked via Object.DontDestroyOnLoad(). To unload objects in it, call Destroy() on them or find who marked them as DontDestroyOnLoad and remove that call.");

        public static readonly string InvalidObjectErrorBoxMessage = L10n.Tr("This is an invalid Managed Object, i.e. the Memory Profiler could not identify it's type and data. You can help us in finding and fixing this issue.") + PleaseReportABugMessage + PleaseAttatchASnapshotAndDescriptionForFindingThisItemMessage;

        const string k_UnrootedAllocationsNonInternalNote = "\nNote: It is likely that this memory is actually needed, but without it being properly rooted, there is no way to tell." +
            "\nUnity's staff is making a best effort attempt to catch these internally, but there are a myriad of ways of using the engine so that it is impossible to catch all possible scenarios.";

        const string k_UnrootedUnrootedAllocationsErrorBoxMessage = "This is a bug in the native code of the engine, please file a bug report. " +
            "Chances are high that every single allocation here is a separate bug to be fixed by a different team and should be treated as such. " +
            "By their very nature, User facing releases lack the information needed to differentiate these allocations in a meaningful way. " +
            "To get an approximation of what constitutes a separate vs a duplicate bug the byte size of each allocation should be used (size in byte is given when hovering the Native Size). " +
            k_UnrootedAllocationsNonInternalNote;

        public static readonly string UnrootedUnrootedAllocationsErrorBoxMessage = L10n.Tr(k_UnrootedUnrootedAllocationsErrorBoxMessage);

        public static readonly string UnrootedUnrootedAllocations_6000_3_ErrorBoxMessage = L10n.Tr("These allocations stem from bugs in the native code of the engine. " +
            "You can get more detail on these by starting your 6.3+ Unity Player with the command-line option '-enable-memoryprofiler-callstacks', " +
            "which might not result in fully symbolized callstacks for each of these allocations, but will break them down by their Memory Labels. " +
            "We are working to reduce the amount of these starting with Unity version 6.5 but might not have repro cases for all instances and Memory Labels, " +
            "so if there are any Memory Labels that stand out or just keep growing over time, please file a bug report with your project and snapshots taken with callstack information. " +
            k_UnrootedAllocationsNonInternalNote);

        public static readonly string UnrootedUnrootedAllocationsErrorBoxMessageInternalMode = L10n.Tr(k_UnrootedUnrootedAllocationsErrorBoxMessage +
            "\n\nThose with access to Unity's source code can compile the engine with ENABLE_STACKS_ON_ALL_ALLOCS set to 1 in MemoryProfiler.h to see where in the code base it was allocated from, and with that info disambiguate the issue further. " +
            "\nWhen capturing Editors or Players using Unity 6000.3 or newer, the same can be achieved by starting them with the commandline option '-enable-memoryprofiler-callstacks', though callstacks might not be getting properly symbolicated. ");

        public static readonly string NativeAllocationFoundReferencesHint = L10n.Tr(
            "Only references from Managed Objects to this allocation are found. References from the stack or native code are not found. " +
            "0 'Found References' does not necessarily mean that this allocation is leaked.");

        public static readonly string NativeAllocationRootedByNativeObjectInfoBoxMessage = L10n.Tr("This is an allocation that is part of a Native Object.");
        public static readonly string NativeAllocationRootedByNativeRootReferenceInfoBoxMessage = L10n.Tr("This is an allocation that is rooted to a native Unity system or part of a C++ object that is not exposed to C# via a UnityEngine.Object typed wrapper.");

        public static readonly string NativeAllocationInternalModeCallStacksInfoBoxMessage = L10n.Tr(
            "If you are using Unity 6000.3 or newer you can add '-enable-memoryprofiler-callstacks' as a commandline option to the Player or Editor you want to capture a snapshot from " +
            "to get call-stacks for these native allocations. " +
            "If you are on an older version and have access to Unity's source code, you can compile the engine with ENABLE_STACKS_ON_ALL_ALLOCS set to 1 in MemoryProfiler.h.");

        const string k_NativeAllocationInternalModeDisambiguateAllocationsButtonTooltipBase = "Clicking this will reload the table and then all allocations under this root will be ";
        public static readonly string NativeAllocationInternalModeDisambiguateAllocationsButtonTooltipReveal = L10n.Tr(k_NativeAllocationInternalModeDisambiguateAllocationsButtonTooltipBase + "shown.");
        public static readonly string NativeAllocationInternalModeDisambiguateAllocationsButtonTooltipHide = L10n.Tr(k_NativeAllocationInternalModeDisambiguateAllocationsButtonTooltipBase + "hidden.");

        public static readonly string UnrootedGraphicsResourceErrorBoxMessage = L10n.Tr(
            "This graphics resource was not associated to a root in Unity's native code. This is a bug in the engine, please file a bug report.");

        public static readonly string GraphicsResourceWithSnapshotWithCallStacksInfoBoxMessage = L10n.Tr(
            "Callstacks are not reported for Gfx Resources themselves, so any callstacks listed here relate to their root. " +
            "Use the Gfx Resource ID and the native define ENABLE_DEBUG_GFXALLOCATION_CALLSTACK_INFO in MemoryProfiler.h to get more details when debugging Unity's Native Code.");

        public static readonly GUIContent SearchInSceneButton = L10n.TextContent("Search In Scene", "This is a Scene Object. Click here to search for an object with this name and type in the currently open Scene(s).");
        public static readonly GUIContent SearchInProjectButton = L10n.TextContent("Search In Project", "This is a Asset. Click here to search for an object with this name and type in the Project Browser.");
        public static readonly GUIContent SearchButtonCantSearch = L10n.TextContent("Can't Search", "The object is neither a Scene Object nor an Asset, so it can't be searched for with the Project or Scene search");

        const string k_SelectInEditorLabel = "Select in Editor";
        const string k_SelectInEditorSharedTooltipPart = "Click here to select it and ping it.";
        public static readonly GUIContent SelectInEditorButton100PercentCertain = L10n.TextContent(k_SelectInEditorLabel, "This exact Object was found in the Editor! " + k_SelectInEditorSharedTooltipPart);
        public static readonly GUIContent SelectInEditorButtonLessCertain = L10n.TextContent(k_SelectInEditorLabel, "A close match to this object was found in the Editor. " + k_SelectInEditorSharedTooltipPart);
        public static readonly GUIContent SelectInEditorButtonNotFound = L10n.TextContent(k_SelectInEditorLabel, "This object wasn't found in the Editor.");
        public static readonly GUIContent SelectInEditorButtonFoundTooMany = L10n.TextContent(k_SelectInEditorLabel, "No precise enough match for this object was found in the Editor.");
        public static readonly GUIContent SelectInEditorButtonFoundTooManyToProcess = L10n.TextContent(k_SelectInEditorLabel, "There were too many possible instances of this type of object to process if any of those could be this Object.");
        public static readonly GUIContent SelectInEditorButtonTypeMismatch = L10n.TextContent(k_SelectInEditorLabel, "Failed to search for this Type of object because its managed Type information was lacking.");
        // Leading space is added at the concatenation site where these are appended to tool-tips
        public static readonly string SelectInEditorTooltipCanSearch = L10n.Tr("Use the Search button instead.");
        public static readonly string SelectInEditorTooltipTrySearch = L10n.Tr("Try the Search button instead.");

        public static readonly string SelectInEditorButtonSearchTimeout = L10n.Tr("Searching the object timed out, is the search index fully built yet?");
        public static readonly string SelectInEditorButtonSearchTimeoutSearchIndexWasNotYetBuilt = L10n.Tr("Searching the object timed out because the search index has not yet been built. Please check the Background Tasks window for details.");
        public static readonly string SelectInEditorButtonSearching = L10n.Tr("Please wait, the Editor is still trying to locate this object.");
        public static readonly string SelectInEditorButtonSearchingWhileIndexIsBuilding = L10n.Tr("Please wait, the Editor is still trying to locate this object. The search index has not yet been built so this can take a moment. Please check the Background Tasks window for details.");

        public static readonly string DataTypeGCHandleTooltip = L10n.Tr("This is a GCHandle (one that is not held by a Native Unity Object).");
        public static readonly string DataTypeNativeTooltip = L10n.Tr("This is a Native Unity Object or Type (i.e. one that is derived from UnityEngine.Object).");
        public static readonly string DataTypeManagedTooltip = L10n.Tr("This is an Object of a pure C# type or such a Type itself (i.e. one that is not derived from UnityEngine.Object).");
        public static readonly string DataTypeUnifiedUnityTooltip = L10n.Tr("This is a full Unity Object (i.e. one that is derived from UnityEngine.Object), containing a Managed Shell and a Native Unity Object, or the Managed Type of such an Object.");
        public static readonly string DataTypeLeakedShellTooltip = L10n.Tr("This is a " + k_LeakedManagedShellName + " object of a Unity Object (i.e. one that is derived from UnityEngine.Object). The Native Unity Object has been destroyed already but a reference to the Managed Wrapper likely kept it alive.");
        public static readonly string DataTypeLeakedDynamicAssetTooltip = L10n.Tr("This is a " + k_LeakedDynamicAssetName + " object (i.e. an object that is not persistent (i.e. stored to disk) that is derived from a subtype of UnityEngine.Object that is usually stored as an Asset on disk). Nothing is referencing it anymore and it won't be collected by the managed GC because its native side holds a GCHandle to the Managed Shell. Resources.UnloadUnusedAssets would clean this up but doesn't run automatically when memory is low, so leaks of this type can lead to an Out Of Memory crash if left unchecked. Remember to call Object.Destroy() or Object.DestroyImmediately() on these before losing the reference to them.");

        const string k_LeakedManagedShellName = "Leaked Managed Shell";
        public static readonly string LeakedManagedShellName = L10n.Tr(k_LeakedManagedShellName);
        public static readonly string LeakedManagedShellHint = "(" + LeakedManagedShellName + ")";
        const string k_LeakedDynamicAssetName = "Leaked Dynamic Asset";
        public static readonly string LeakedDynamicAssetName = L10n.Tr(k_LeakedDynamicAssetName);

        public static readonly string NonTypedGroupDescription = L10n.Tr("The selected item is a group of similar elements");

        public static readonly string NoResidentMemoryInformationOldSnapshot = L10n.Tr("Detailed breakdown of resident allocations is not available. This information was added in 2023.1 and this snapshot was taken with an older Unity version.");
        public static readonly string NoResidentMemoryInformationNotSupported = L10n.Tr("Detailed breakdown of resident allocations is not available. This platform is not supported.");
        public static readonly string ResidentAndAllocatedMemoryAreIdentical = L10n.Tr("On this platform, there is no distinction between Allocated Memory, and Resident Memory on Device. Therefore, the Memory profiler will present information exclusively about Allocated Memory.");

        public static readonly string SystemMemoryRegionDescription = L10n.Tr("Region as reported by the OS");
        public static readonly string NativeAllocationDescription = L10n.Tr("Native Allocation registered by Unity Memory Manager");
        public static readonly string ManagedMemoryHeapDescription = L10n.Tr("Allocation made by Mono/IL2CPP for GC pool");
        public static readonly string NativeMemoryRegionDescription = L10n.Tr("This is a memory chunk allocated by Unity Allocator. " +
            "Most Unity allocators allocate memory in chunks, and when a chunk isn't used anymore it's released back to the system. " +
            "You can read more about different types of Unity allocators and their settings on \"Memory allocator customization\" documentation page." +
            "\n\nYou can investigate which object holds specific allocator chunk by turning \"Show Memory Map view\" in Memory Profiler settings. " +
            "In Memory Map view you can see which objects resides in a specific allocator chunk.");
        public static readonly string GCHandleDescription = L10n.Tr(k_GCHandleDescription);
    }

    internal static class DocumentationUrls
    {
        public const string LatestPackageVersionUrl = "https://docs.unity3d.com/Packages/com.unity.memoryprofiler@latest/";
        //public const string CurrentPackageVersionBaseUrl = "https://docs.unity3d.com/Packages/com.unity.memoryprofiler@0.4/";
        public const string LatestPackageVersionBaseUrl = "https://docs.unity3d.com/Packages/com.unity.memoryprofiler@latest/index.html?subfolder=/";
        const string k_AnchorChar = "%23";
        public const string WorkbenchHelp = "manual/snapshots-component.html";
        public const string IndexHelp = "manual/index.html";
        public const string MemoryOnDeviceHelp = "manual/memory-on-device.html";
        public const string OpenSnapshotsPane = LatestPackageVersionBaseUrl + WorkbenchHelp + k_AnchorChar + "snapshots-component#open-snapshots";
        public const string AnalysisWindowHelp = LatestPackageVersionBaseUrl + "manual/main-component.html";
        public const string CaptureFlagsHelp = "https://docs.unity3d.com/ScriptReference/Unity.Profiling.Memory.CaptureFlags.html";

        public const string UntrackedMemoryDocumentation = LatestPackageVersionBaseUrl + MemoryOnDeviceHelp + k_AnchorChar + "untracked-memory";
    }

    internal static class FileExtensionContent
    {
        public const string SnapshotTempFileName = "temp.tmpsnap";

        public const string SnapshotTempScreenshotFileExtension = ".tmppng";
        public const string SnapshotScreenshotFileExtension = ".png";
        public const string SnapshotFileExtension = ".snap";
    }
}

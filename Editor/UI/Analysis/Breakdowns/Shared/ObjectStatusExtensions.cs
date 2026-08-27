using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEngine;

#if LIFECYCLE_APIS_AVAILABLE
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.MemoryProfiler.Editor.UI
{
    static partial class ObjectStatusExtensions
    {
        public static ulong AllStatuses { get; private set; }
        /// <summary>Mask of every "clearly leaked" status, offered as a one-click preset group in the filter dropdown.</summary>
        public static ulong AllLeakedStatusesMask { get; private set; }

        /// <summary>Builds a bitmask (bit N = <see cref="ObjectStatus"/> N) from the given statuses.</summary>
        static ulong MaskOf(params ObjectStatus[] statuses)
        {
            ulong mask = 0;
            foreach (var s in statuses)
                mask |= 1ul << (int)s;
            return mask;
        }

        /// <summary>
        /// Bitmask (bit N = ObjectStatus N) of statuses that will never appear in the Unity Objects table.
        /// Used to disable the corresponding toggles in the status filter dropdown for that table.
        /// NOTE: GraphicsBuffers is intentionally NOT excluded: Unity Objects that own graphics memory are
        /// surfaced to that filter directly (see UnityObjectsModelBuilder's leaf filter check).
        /// </summary>
        public static readonly ulong UnityObjectsTableUnavailableStatuses = MaskOf(
            ObjectStatus.ExecutablesAndMapped,
            ObjectStatus.GCHandleEntry,
            ObjectStatus.GCHandleHeld,
            ObjectStatus.LeakedShell,
            ObjectStatus.LeakedShellHeldByGCHandle,
            ObjectStatus.ManagedTypeRoot,
            ObjectStatus.NativeRootAllocation,
            ObjectStatus.PrefabRoot,
            ObjectStatus.InDelayedDeletionQueue,
            ObjectStatus.ManagedObjectReferenced,
            ObjectStatus.Reserved,
            ObjectStatus.SceneRoot,
            ObjectStatus.UnknownLiveness,
            ObjectStatus.Untracked,
            ObjectStatus.UsedByNativeCode,
            ObjectStatus.VirtualMachine,
            ObjectStatus.Unrooted,
            ObjectStatus.ReferencedUnrooted);

        /// <summary>
        /// Bitmask (bit N = ObjectStatus N) of statuses that will never appear in the All Of Memory table.
        /// Used to disable the corresponding toggles in the status filter dropdown for that table.
        /// </summary>
        public static readonly ulong AllOfMemoryTableUnavailableStatuses = MaskOf(
            ObjectStatus.GCHandleEntry,
            ObjectStatus.PrefabRoot,
            ObjectStatus.SceneRoot,
            ObjectStatus.ManagedTypeRoot);

        /// <summary>
        /// Bitmask (bit N = ObjectStatus N) of statuses that will never appear in the Roots And Impact table.
        /// Used to disable the corresponding toggles in the status filter dropdown for that table.
        /// </summary>
        public static readonly ulong RootsAndImpactTableUnavailableStatuses = 0;

        /// <summary>
        /// The status-filtered table views paired with the statuses each can never contain. Used to tell
        /// the user, for a status disabled in one view's filter dropdown, which other view(s) DO surface
        /// it (a view contains a status when its unavailable-status mask lacks that status' bit).
        /// Populated in the static constructor, after the masks above are initialized.
        /// </summary>
        static readonly (string ViewName, ulong UnavailableStatuses)[] k_StatusFilteredTableViews;

        /// <summary>Bitmask with a bit set for every <see cref="ObjectStatus"/> value that passes <see cref="IsWarningStatus"/>.</summary>
        public static ulong WarningStatusesBitmask { get; private set; }

        /// <summary>
        /// Pre-computed alphabetical sort rank for each <see cref="ObjectStatus"/>, indexed by enum value.
        /// Only consulted for non-warning items: warning items sort by -WarningCount so they bubble to the top
        /// (see <see cref="GetStatusSortKey(ObjectStatusWarningInfo)"/>).
        /// </summary>
        static int[] k_StatusSortKeys;
        /// <summary>Sort rank for the "Mixed" placeholder shown when a group contains multiple statuses.</summary>
        static int k_MixedStatusSortKey;
        public static ReadOnlyCollection<string> StatusMaskChoiceNames => k_StatusMaskChoiceNames.AsReadOnly();
        public static ReadOnlyCollection<string> StatusMaskChoiceTooltips => k_StatusMaskChoiceTooltips.AsReadOnly();
        public static ReadOnlyCollection<ulong> StatusMaskValues => k_StatusMaskValues.AsReadOnly();

        public static ReadOnlySpan<ObjectStatus> DisplayStatusValues => new(k_DisplayStatusValues);
        public static int CountOfGroupMaskChoices => k_CountOfGroupMaskChoices;

        /// <summary>
        /// Cached display names indexed by enum value for O(1) lookup without switch overhead.
        /// Length covers all enum values including None (index 0).
        /// </summary>
#if LIFECYCLE_APIS_AVAILABLE
        [AutoStaticsCleanup]
#endif
        static readonly string[] k_DisplayNames;
#if LIFECYCLE_APIS_AVAILABLE
        [AutoStaticsCleanup]
#endif
        static readonly string[] k_PerStatusTooltips;
#if LIFECYCLE_APIS_AVAILABLE
        [AutoStaticsCleanup]
#endif
        static readonly ObjectStatus[] k_DisplayStatusValues;

#if LIFECYCLE_APIS_AVAILABLE
        [AutoStaticsCleanup]
#endif
        static readonly List<string> k_StatusMaskChoiceNames;
#if LIFECYCLE_APIS_AVAILABLE
        [AutoStaticsCleanup]
#endif
        static readonly List<string> k_StatusMaskChoiceTooltips;
#if LIFECYCLE_APIS_AVAILABLE
        [AutoStaticsCleanup]
#endif
        static readonly List<ulong> k_StatusMaskValues;
        static readonly int k_CountOfGroupMaskChoices;

        static ObjectStatusExtensions()
        {
            var values = (ObjectStatus[])Enum.GetValues(typeof(ObjectStatus));
            var maxValue = 0;
            foreach (var v in values)
            {
                var intVal = (int)v;
                if (intVal > maxValue) maxValue = intVal;
            }

            // the bit masks are stored as ulongs and the ObjectStatus enum itself is of size byte, so don't measure against sizeof(ObjectStatus)
            Debug.Assert(maxValue < sizeof(ulong) * 8, $"Max value of ObjectStatus enum exceeds the bit count of the mask. The current mask implementation is ulong based (64 bits); revise the mask type if more statuses are needed.");

            k_DisplayNames = new string[maxValue + 1];
            k_PerStatusTooltips = new string[maxValue + 1];
            k_DisplayStatusValues = new ObjectStatus[maxValue + 1];
            for (int i = 0; i <= maxValue; i++)
            {
                k_DisplayNames[i] = string.Empty;
                k_PerStatusTooltips[i] = null; // default to no tooltip, which means the display name will be used as tooltip
                k_DisplayStatusValues[i] = ObjectStatus.None;
            }

            k_StatusMaskChoiceNames = new List<string>();
            k_StatusMaskChoiceTooltips = new List<string>();
            k_StatusMaskValues = new List<ulong>();
            static int SetMaskName(ObjectStatus status, string displayName, string tooltip = null)
            {
                k_StatusMaskChoiceNames.Add(displayName);
                k_StatusMaskChoiceTooltips.Add(tooltip ?? displayName);
                var statusMask = (int)status;
                var statusMaskBit = statusMask > 0 ? 1ul << statusMask : 0ul;
                k_StatusMaskValues.Add(statusMaskBit);

                return k_StatusMaskValues.Count - 1;
            }
            static void AddToMaskGroup(int index, ObjectStatus status)
            {
                var statusMask = (int)status;
                var statusMaskBit = statusMask > 0 ? 1ul << statusMask : 0ul;
                k_StatusMaskValues[index] = k_StatusMaskValues[index] | statusMaskBit;
            }

            // "Any" option is bitmask 0, i.e. no filtering based on mask
            var anyStatusIndex = SetMaskName(ObjectStatus.None, TextContent.ObjectStatusAny);
            // In contrast, "All" is all bits set for all defined statuses.
            // This also serves as a convenient way to flip all toggles on in the UI so users
            // can then deselect individual statuses to filter things down,
            // i.e. building an exclusion filter instead of an inclusion list.
            var allStatusesIndex = SetMaskName(ObjectStatus.None, TextContent.ObjectStatusAll);

            // Groups are registered in English-alphabetical order so that they appear correctly in
            // the dropdown without a runtime sort in the default (untranslated) case.
            // If translations reorder the display names, StatusFilterDropDownWindow will re-sort.

            // "All Leaked"
            var allLeakedGroupIndex = SetMaskName(ObjectStatus.LeakedShell, TextContent.ObjectStatusAllLeaked, TextContent.ObjectStatusAllLeakedTooltip);
            AddToMaskGroup(allLeakedGroupIndex, ObjectStatus.LeakedShellHeldByGCHandle);
            AddToMaskGroup(allLeakedGroupIndex, ObjectStatus.DynamicAssetLeaked);
            AddToMaskGroup(allLeakedGroupIndex, ObjectStatus.BrokenScriptReference);
            AddToMaskGroup(allLeakedGroupIndex, ObjectStatus.LeakedAssetBundle);
            AllLeakedStatusesMask = k_StatusMaskValues[allLeakedGroupIndex];

            // "Don't Destroy Objects"
            var allDontDestroyGroupIndex = SetMaskName(ObjectStatus.DynamicAssetDontUnload, TextContent.ObjectStatusDontDestroy, TextContent.ObjectStatusDontDestroyTooltip);
            AddToMaskGroup(allDontDestroyGroupIndex, ObjectStatus.UnusedAssetDontUnload);
            AddToMaskGroup(allDontDestroyGroupIndex, ObjectStatus.SceneObjectDontUnload);

            // "Native Subsystem"
            var allOtherNativeStatusesGroupIndex = SetMaskName(ObjectStatus.Manager, TextContent.ObjectStatusAllNativeSubsystemLeftOvers, TextContent.ObjectStatusAllNativeSubsystemLeftOversTooltip);
            AddToMaskGroup(allOtherNativeStatusesGroupIndex, ObjectStatus.NativeRootAllocation);
            AddToMaskGroup(allOtherNativeStatusesGroupIndex, ObjectStatus.InDelayedDeletionQueue);
            AddToMaskGroup(allOtherNativeStatusesGroupIndex, ObjectStatus.GraphicsBuffers);
            AddToMaskGroup(allOtherNativeStatusesGroupIndex, ObjectStatus.Unrooted);
            AddToMaskGroup(allOtherNativeStatusesGroupIndex, ObjectStatus.ReferencedUnrooted);

            // "Normal Assets"
            var allAssetsGroupIndex = SetMaskName(ObjectStatus.UsedAsset, TextContent.ObjectStatusNormalAssets, TextContent.ObjectStatusNormalAssetsTooltip);
            AddToMaskGroup(allAssetsGroupIndex, ObjectStatus.UnusedAsset);
            AddToMaskGroup(allAssetsGroupIndex, ObjectStatus.UnusedAssetInAssetBundle);
            AddToMaskGroup(allAssetsGroupIndex, ObjectStatus.UsedAssetBundle);
            AddToMaskGroup(allAssetsGroupIndex, ObjectStatus.UnusedAssetBundle);
            AddToMaskGroup(allAssetsGroupIndex, ObjectStatus.DynamicAssetReferenced);
            AddToMaskGroup(allAssetsGroupIndex, ObjectStatus.PrefabRoot);

            // "Normal Scene Objects"
            var allSceneObjectsGroupIndex = SetMaskName(ObjectStatus.SceneObject, TextContent.ObjectStatusNormalSceneObjects, TextContent.ObjectStatusNormalSceneObjectsTooltip);
            AddToMaskGroup(allSceneObjectsGroupIndex, ObjectStatus.SceneRoot);

            // "Other Managed Roots"
            var allManagedStatusesGroupIndex = SetMaskName(ObjectStatus.ManagedObjectReferenced, TextContent.ObjectStatusOtherManagedRootingStatuses);
            AddToMaskGroup(allManagedStatusesGroupIndex, ObjectStatus.UsedByNativeCode);
            AddToMaskGroup(allManagedStatusesGroupIndex, ObjectStatus.GCHandleHeld);
            AddToMaskGroup(allManagedStatusesGroupIndex, ObjectStatus.ManagedTypeRoot);
            AddToMaskGroup(allManagedStatusesGroupIndex, ObjectStatus.GCHandleEntry);
            AddToMaskGroup(allManagedStatusesGroupIndex, ObjectStatus.UnknownLiveness);

            // "Other Statuses"
            var allOtherStatusesGroupIndex = SetMaskName(ObjectStatus.Untracked, TextContent.ObjectStatusAllOtherStatuses);
            AddToMaskGroup(allOtherStatusesGroupIndex, ObjectStatus.Reserved);
            AddToMaskGroup(allOtherStatusesGroupIndex, ObjectStatus.ExecutablesAndMapped);
            AddToMaskGroup(allOtherStatusesGroupIndex, ObjectStatus.VirtualMachine);

            k_CountOfGroupMaskChoices = k_StatusMaskChoiceNames.Count;

            void SetDisplayName(ObjectStatus status, string displayName, string tooltip = null)
            {
                var index = (int)status;
                if (index >= 0 && index < k_DisplayNames.Length)
                {
                    k_DisplayNames[index] = displayName;
                    k_PerStatusTooltips[index] = tooltip;
                    k_DisplayStatusValues[index] = status;
                    SetMaskName(status, displayName, tooltip);
                    AddToMaskGroup(allStatusesIndex, status);
                }
            }

            // Individual entries registered in English-alphabetical order so the mask dropdown
            // sub-items are already sorted for the default (untranslated) case. StatusFilterDropDownGroupNode
            // re-sorts by GetStatusSortKey() at runtime to handle translated display names.
            SetDisplayName(ObjectStatus.UsedAssetBundle, TextContent.ObjectStatusUsedAssetBundle, TextContent.ObjectStatusUsedAssetBundleTooltip);                // "AssetBundle"
            SetDisplayName(ObjectStatus.BrokenScriptReference, TextContent.ObjectStatusBrokenScriptReference, TextContent.ObjectStatusBrokenScriptReferenceTooltip); // "Broken Script Reference"
            SetDisplayName(ObjectStatus.DynamicAssetReferenced, TextContent.ObjectStatusDynamicAssetReferenced);                                    // "Dynamic Asset"
            SetDisplayName(ObjectStatus.DynamicAssetDontUnload, TextContent.ObjectStatusDynamicAssetDontUnload, TextContent.ObjectStatusDynamicAssetDontUnloadTooltip); // "Dynamic Asset (DDoL)"
            SetDisplayName(ObjectStatus.ExecutablesAndMapped, TextContent.ObjectStatusExecutablesAndMapped);                                        // "Executables & Mapped"
            SetDisplayName(ObjectStatus.GCHandleEntry, TextContent.ObjectStatusGCHandleEntry, TextContent.ObjectStatusGCHandleEntryTooltip);        // "GCHandle"
            SetDisplayName(ObjectStatus.GCHandleHeld, TextContent.ObjectStatusGCHandleHeld);                                                        // "GCHandle Held"
            SetDisplayName(ObjectStatus.GraphicsBuffers, TextContent.ObjectStatusGraphicsBuffers, TextContent.ObjectStatusGraphicsBuffersTooltip);  // "Graphics Buffer"
            SetDisplayName(ObjectStatus.LeakedAssetBundle, TextContent.ObjectStatusLeakedAssetBundle, TextContent.ObjectStatusLeakedAssetBundleTooltip);          // "Leaked AssetBundle"
            SetDisplayName(ObjectStatus.DynamicAssetLeaked, TextContent.ObjectStatusDynamicAssetLeaked, TextContent.ObjectStatusDynamicAssetLeakedTooltip); // "Leaked Dynamic Asset"
            SetDisplayName(ObjectStatus.LeakedShell, TextContent.ObjectStatusLeakedShell, TextContent.ObjectStatusLeakedShellTooltip);              // "Leaked Shell"
            SetDisplayName(ObjectStatus.LeakedShellHeldByGCHandle, TextContent.ObjectStatusLeakedShellHeldByGCHandle, TextContent.ObjectStatusLeakedShellTooltip); // "Leaked Shell (GCHandle)"
            SetDisplayName(ObjectStatus.ManagedTypeRoot, TextContent.ObjectStatusManagedTypeRoot, TextContent.ObjectStatusManagedTypeRootTooltip);  // "Managed Type"
            SetDisplayName(ObjectStatus.Manager, TextContent.ObjectStatusManager);                                                                  // "Manager"
            SetDisplayName(ObjectStatus.NativeRootAllocation, TextContent.ObjectStatusNativeRootAllocation);                                        // "Native Allocation"
            SetDisplayName(ObjectStatus.PrefabRoot, TextContent.ObjectStatusPrefabRoot, TextContent.ObjectStatusPrefabRootTooltip);                 // "Prefab"
            SetDisplayName(ObjectStatus.InDelayedDeletionQueue, TextContent.ObjectStatusInDelayedDeletionQueue, TextContent.ObjectStatusInDelayedDeletionQueueTooltip); // "Queued for Deletion"
            SetDisplayName(ObjectStatus.ManagedObjectReferenced, TextContent.ObjectStatusManagedObjectReferenced);                                  // "Referenced Managed Object"
            SetDisplayName(ObjectStatus.Reserved, TextContent.ObjectStatusReserved);                                                                // "Reserved"
            SetDisplayName(ObjectStatus.SceneRoot, TextContent.ObjectStatusSceneRoot, TextContent.ObjectStatusSceneRootTooltip);                    // "Scene"
            SetDisplayName(ObjectStatus.SceneObject, TextContent.ObjectStatusSceneObject);                                                          // "Scene Object"
            SetDisplayName(ObjectStatus.SceneObjectDontUnload, TextContent.ObjectStatusSceneObjectDontUnload, TextContent.ObjectStatusSceneObjectDontUnloadTooltip); // "Scene Object (DDoL)"
            SetDisplayName(ObjectStatus.UnknownLiveness, TextContent.ObjectStatusUnknownLiveness);                                                  // "Unknown Liveness"
            SetDisplayName(ObjectStatus.Unrooted, TextContent.ObjectStatusUnrooted, TextContent.ObjectStatusUnrootedTooltip);                       // "Unrooted"
            SetDisplayName(ObjectStatus.ReferencedUnrooted, TextContent.ObjectStatusReferencedUnrooted, TextContent.ObjectStatusReferencedUnrootedTooltip); // "Unrooted (Referenced)"
            SetDisplayName(ObjectStatus.Untracked, TextContent.ObjectStatusUntracked);                                                              // "Untracked"
            SetDisplayName(ObjectStatus.UnusedAsset, TextContent.ObjectStatusUnusedAsset, TextContent.ObjectStatusUnusedAssetTooltip);              // "Unused Asset"
            SetDisplayName(ObjectStatus.UnusedAssetInAssetBundle, TextContent.ObjectStatusUnusedAssetInAssetBundle, TextContent.ObjectStatusUnusedAssetInAssetBundleTooltip); // "Unused Asset (AssetBundle)"
            SetDisplayName(ObjectStatus.UnusedAssetDontUnload, TextContent.ObjectStatusUnusedAssetDontUnload, TextContent.ObjectStatusUnusedAssetDontUnloadTooltip); // "Unused Asset (DDoL)"
            SetDisplayName(ObjectStatus.UnusedAssetBundle, TextContent.ObjectStatusUnusedAssetBundle, TextContent.ObjectStatusUnusedAssetBundleTooltip);          // "Unused AssetBundle"
            SetDisplayName(ObjectStatus.UsedAsset, TextContent.ObjectStatusUsedAsset);                                                              // "Used Asset"
            SetDisplayName(ObjectStatus.UsedByNativeCode, TextContent.ObjectStatusUsedByNativeCode, TextContent.ObjectStatusUsedByNativeCodeTooltip); // "Used by Native Code"
            SetDisplayName(ObjectStatus.VirtualMachine, TextContent.ObjectStatusVirtualMachine);                                                    // "Virtual Machine"

            AllStatuses = k_StatusMaskValues[allStatusesIndex];

            // Registry of the status-filtered table views and the statuses each cannot show. Built here
            // (not as a field initializer) so the *TableUnavailableStatuses masks above are guaranteed
            // assigned. Roots & Impact (mask 0) surfaces every status, so the list is never empty.
            k_StatusFilteredTableViews = new (string, ulong)[]
            {
                (TextContent.RootsAndImpactViewName, RootsAndImpactTableUnavailableStatuses),
                (TextContent.UnityObjectsViewName, UnityObjectsTableUnavailableStatuses),
                (TextContent.AllOfMemoryViewName, AllOfMemoryTableUnavailableStatuses),
            };

            ulong warningMask = 0;
            for (int i = 1; i < k_DisplayNames.Length; i++)
            {
                if (((ObjectStatus)i).IsWarningStatus())
                    warningMask |= 1ul << i;
            }
            WarningStatusesBitmask = warningMask;

            // Build alphabetical sort keys for status column sorting.
            // "Mixed" (multi-status group placeholder) is inserted as a virtual entry so it lands at
            // its natural translated position. Empty display names (None / unregistered) sort last.
            const int k_MixedVirtualStatusValue = -1;
            var sortEntries = new List<(string name, int statusValue)>(maxValue + 2);
            for (int i = 0; i <= maxValue; i++)
                sortEntries.Add((k_DisplayNames[i], i));
            sortEntries.Add((TextContent.ObjectStatusMixed, k_MixedVirtualStatusValue));

            sortEntries.Sort((a, b) =>
            {
                bool aEmpty = string.IsNullOrEmpty(a.name);
                bool bEmpty = string.IsNullOrEmpty(b.name);
                if (aEmpty && bEmpty) return 0;
                if (aEmpty) return 1;
                if (bEmpty) return -1;
                return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
            });

            k_StatusSortKeys = new int[maxValue + 1];
            k_MixedStatusSortKey = 0;
            for (int rank = 0; rank < sortEntries.Count; rank++)
            {
                if (sortEntries[rank].statusValue == k_MixedVirtualStatusValue)
                    k_MixedStatusSortKey = rank;
                else
                    k_StatusSortKeys[sortEntries[rank].statusValue] = rank;
            }
        }

        /// <summary>
        /// Returns true for statuses that indicate a clearly problematic condition
        /// (leaked shell, leaked dynamic asset, broken script reference, or unknown liveness).
        /// Used to display a warning indicator in status cells and propagate warning counts up group nodes.
        /// </summary>
        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public static bool IsWarningStatus(this ObjectStatus status)
            => status == ObjectStatus.LeakedShell
            || status == ObjectStatus.LeakedShellHeldByGCHandle
            || status == ObjectStatus.DynamicAssetLeaked
            || status == ObjectStatus.BrokenScriptReference
            || status == ObjectStatus.LeakedAssetBundle
            || status == ObjectStatus.UnknownLiveness;

        /// <summary>
        /// Pre-computed alphabetical sort rank for a single status.
        /// Used for non-warning items; warning items use <see cref="GetStatusSortKey(ObjectStatusWarningInfo)"/> which returns <c>-WarningCount</c>.
        /// </summary>
        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public static int GetStatusSortKey(this ObjectStatus status)
        {
            var index = (int)status;
            return index >= 0 && index < k_StatusSortKeys.Length ? k_StatusSortKeys[index] : int.MaxValue;
        }

        /// <summary>
        /// Sort key for a status cell:
        /// <list type="bullet">
        ///   <item>Absent (Bitmask == 0): <c>int.MaxValue</c> — always last.</item>
        ///   <item>Has warnings: <c>-WarningCount</c> — bubbles to top; most warnings first.</item>
        ///   <item>No warnings, single status: alphabetical rank of the status name.</item>
        ///   <item>No warnings, multiple statuses: alphabetical rank of the "Mixed" placeholder.</item>
        /// </list>
        /// </summary>
        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public static int GetStatusSortKey(this ObjectStatusWarningInfo info)
        {
            if (info.Bitmask == 0)
                return int.MaxValue; // absent → always last

            if (info.WarningCount > 0)
                return -info.WarningCount; // warnings bubble to top; most warnings sorts first

            // No warnings: sort alphabetically by the represented status.
            var nonWarningBits = info.Bitmask & ~1ul; // strip the None (bit 0) sentinel
            if (nonWarningBits == 0)
                return ObjectStatus.None.GetStatusSortKey();
            if ((nonWarningBits & (nonWarningBits - 1)) != 0)
                return k_MixedStatusSortKey; // multiple non-warning statuses → "Mixed" position

            // Single non-warning status: recover its enum value from the single set bit.
            int bitIndex = 0;
            ulong b = nonWarningBits;
            while ((b >>= 1) != 0) bitIndex++;
            return ((ObjectStatus)bitIndex).GetStatusSortKey();
        }

        public static string ToDisplayName(this ObjectStatus status)
        {
            var index = (int)status;
            if (index >= 0 && index < k_DisplayNames.Length)
                return k_DisplayNames[index];
            return string.Empty;
        }

        public static string GetTooltip(this ObjectStatus status)
        {
            var index = (int)status;
            if (index >= 0 && index < k_DisplayNames.Length)
                return k_PerStatusTooltips[index] ?? status.ToDisplayName();
            return string.Empty;
        }

        /// <summary>
        /// Builds the tooltip for a status filter toggle that is disabled because the current table can
        /// never contain that status. Names the other table view(s) whose unavailable-status mask does
        /// NOT exclude <paramref name="statusBit"/>, i.e. the views where the status CAN be inspected.
        /// </summary>
        /// <param name="statusBit">A single-status bitmask (1u &lt;&lt; (int)status), matching the value
        /// the dropdown sub-item carries.</param>
        /// <returns>
        /// The "inspect in {views}" tooltip listing every view that surfaces the status; falls back to
        /// <see cref="UIContentData.TextContent.StatusUnavailableInTableTooltip"/> in the degenerate case
        /// where no registered view contains it.
        /// </returns>
        public static string GetStatusUnavailableTooltip(ulong statusBit)
        {
            List<string> views = null;
            foreach (var view in k_StatusFilteredTableViews)
            {
                // The current (calling) view excludes this status, so it is naturally omitted here.
                if ((view.UnavailableStatuses & statusBit) == 0)
                    (views ??= new List<string>()).Add(view.ViewName);
            }

            if (views == null || views.Count == 0)
                return TextContent.StatusUnavailableInTableTooltip;

            return string.Format(TextContent.StatusUnavailableInTableInspectElsewhereTooltip, string.Join(", ", views));
        }

        /// <summary>
        /// Returns true when a group's status composition changed meaningfully between two snapshots.
        /// Each bitmask encodes which <see cref="ObjectStatus"/> values are present in the group
        /// (bit N = status with value N is present; bit 0 / None is excluded).
        /// A bitmask of zero means the group is absent on that side; existence-only changes
        /// (add/remove) are already captured by size and count deltas, so both sides must be
        /// non-zero for a status difference to count as a status change.
        /// </summary>
        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public static bool HasStatusChangedBetweenSnapshots(ulong statusBitmaskInA, ulong statusBitmaskInB)
            => statusBitmaskInA != 0 && statusBitmaskInB != 0 && statusBitmaskInA != statusBitmaskInB;

        /// <summary>
        /// Computes the display text and optional tooltip for a status bitmask in a comparison cell.
        /// <list type="bullet">
        /// <item>Bitmask == 0 (absent): empty text, no tooltip.</item>
        /// <item>Exactly one status bit set: the status display name, no tooltip.</item>
        /// <item>Multiple status bits set: <see cref="UIContentData.TextContent.ObjectStatusMixed"/>
        /// as text, comma-separated list of present status names as tooltip.</item>
        /// </list>
        /// </summary>
        public static void GetStatusDisplayInfo(ulong bitmask, int warningCount, out string displayText, out string tooltip)
        {
            if (bitmask == 0)
            {
                displayText = string.Empty;
                tooltip = string.Empty;
                return;
            }

            // Single status: exactly one bit set
            if ((bitmask & (bitmask - 1)) == 0)
            {
                // Find the set bit index
                int bitIndex = 0;
                ulong remaining = bitmask;
                while ((remaining >>= 1) != 0) bitIndex++;
                displayText = ((ObjectStatus)bitIndex).ToDisplayName();
                if (warningCount >= 1)
                    tooltip = string.Format(TextContent.ObjectStatusWarningDescendantCountTooltip, warningCount, displayText);
                else
                {
                    tooltip = ((ObjectStatus)bitIndex).GetTooltip();
                }
                return;
            }

            // Multiple statuses: build tooltip from all set bits (skip bit 0 / None).
            displayText = TextContent.ObjectStatusMixed;
            var joined = JoinStatusNames(bitmask);
            if (joined.Length == 0)
            {
                tooltip = string.Empty;
            }
            else
            {
                tooltip = warningCount >= 1
                    ? string.Format(TextContent.ObjectStatusWarningDescendantCountTooltip, warningCount, joined)
                    : joined;
            }

        }

        /// <summary>
        /// Joins the display names of every status set in <paramref name="bitmask"/> (skipping None / bit 0)
        /// into a comma-separated string in ascending enum order. Returns an empty string when no named
        /// status is set. <c>string.Join</c> avoids the O(N²) intermediate allocations of repeated
        /// concatenation.
        /// </summary>
        public static string JoinStatusNames(ulong bitmask)
        {
            var names = new List<string>();
            for (int i = 1; i < 64; i++)
            {
                if (((bitmask >> i) & 1) == 0)
                    continue;
                var name = ((ObjectStatus)i).ToDisplayName();
                if (name.Length > 0)
                    names.Add(name);
            }
            return names.Count == 0 ? string.Empty : string.Join(", ", names);
        }

        /// <summary>
        /// Precompute which ObjectStatus values match a search string (case-insensitive contains).
        /// Call once per search query, then use <see cref="MatchesSearchString"/> per object
        /// instead of calling <see cref="ToDisplayName"/> + string.Contains per object.
        /// Returns null if no statuses match (caller should skip the status scope entirely).
        /// </summary>
        public static HashSet<ObjectStatus> GetMatchingStatuses(string searchText)
        {
            if (string.IsNullOrEmpty(searchText))
                return null;

            HashSet<ObjectStatus> result = null;
            for (int i = 1; i < k_DisplayNames.Length; i++) // skip None (0)
            {
                var displayName = k_DisplayNames[i];
                if (displayName.Length > 0 && displayName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                {
                    if (result == null)
                        result = new HashSet<ObjectStatus>();
                    result.Add((ObjectStatus)i);
                }
            }
            return result;
        }
    }
}

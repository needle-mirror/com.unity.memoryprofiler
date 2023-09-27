using System;
using System.Collections.Generic;
using System.Text;
using Unity.MemoryProfiler.Editor.Format;
using Unity.Profiling.Memory;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// </summary>
    internal class SnapshotIssuesModelBuilder
    {
        const string kEditorHint = "Editor capture! Get better insights by building and profiling a development build, as memory behaves quite differently in the Editor.";
        const string kEditorHintDiffBoth = "Comparing Editor captures! Get better insights by building and profiling a development build, as memory behaves quite differently in the Editor.";
        const string kEditorHintDiffOne = "Comparing an Editor capture to a Player Capture!";
        const string kEditorHintDiffOneDetails = "Get better insights by building and profiling a development build, as memory behaves quite differently in the Editor. These differences will make this comparison look quite odd.";
        const string kEditorHintDetails = "The memory used in the Editor is not cleanly separated into Play Mode and Edit Mode, but is a fluid mix. Things might be held in memory by editor related functionality, load and unload into memory at different points in time and can take up a different amount than it would in a build on your targeted hardware. Make a development build, attach the Memory Profiler to it and take a capture from that Player to get a more accurate and relevant picture of your memory usage.";

        const string kOldSnapshotWarning = "Snapshot from an outdated Unity version that is not fully supported.";
        static readonly string kOldSnapshotWarningContent = "The functionality of the Memory Profiler package version 0.4 and newer builds on data only reported in newer Unity versions.";

        const string kSystemAllocatorWarning = "System Allocator is used. It is generally advised to use the Dynamic Heap Allocator instead.";
        const string kSystemAllocatorWarningDetailsDiffA = "System Allocator is used in snapshot A.";
        const string kSystemAllocatorWarningDetailsDiffB = "System Allocator is used in snapshot B.";
        const string kSystemAllocatorWarningDetailsDiffBoth = "System Allocator is used in both snapshots.";
        const string kSystemAllocatorWarningDetails = "Dynamic Heap Allocator is generally more efficient than the System Allocator. Additionally, Native Objects can be allocated outside of Native Regions when using the System Allocator.";

        const string kNoIssuesFound = "No Issues detected.";
        const string kEnumerating3 = "{0}, {1} and {2}";
        const string kEnumerating2 = "{0} and {1}";
        const string kCaptureFlagsMissing = "{0} were not captured.";

        const string kCaptureFlagsNativeObjects = "Native Objects";
        const string kCaptureFlagsNativeObjectsDetails = "To capture and inspect Native Object memory usage, select the Native Objects option from the capture options in the drop-down menu of the Capture button or via {0}.{1} when using the API to take a capture.";
        static readonly string kFullCaptureFlagsNativeObjectsDetails = string.Format(kCaptureFlagsNativeObjectsDetails, nameof(CaptureFlags), nameof(CaptureFlags.NativeObjects));

        const string kCaptureFlagsNativeAllocations = "Native Allocations";
        const string kCaptureFlagsNativeAllocationsDetails = "To capture and inspect Native Allocations, select the Native Allocations option from the capture options in the drop-down menu of the Capture button or via {0}.{1} when using the API to take a capture. Also note: Without Native Allocations being captured, no determination can be made regarding whether or not the System Allocator was used.";
        static readonly string kFullCaptureFlagsNativeAllocationsDetails = string.Format(kCaptureFlagsNativeAllocationsDetails, nameof(CaptureFlags), nameof(CaptureFlags.NativeAllocations));

        const string kCaptureFlagsManagedObjects = "Managed Objects";
        const string kCaptureFlagsManagedObjectsDetails = "To capture and inspect Managed Object memory usage, select the Managed Objects option from the capture options in the drop-down menu of the Capture button or via {0}.{1} when using the API to take a capture.";
        static readonly string kFullCaptureFlagsManagedObjectsDetails = string.Format(kCaptureFlagsManagedObjectsDetails, nameof(CaptureFlags), nameof(CaptureFlags.ManagedObjects));

        const string kCaptureFlagsDiff = "Comparing snapshots with different Capture options.";
        const string kCaptureFlagsDiffDetailsBaseString = "{0}";
        const string kCaptureFlagsDiffDetails = "{0} were not captured in snapshot {1}.";
        const string kCaptureFlagsDiffDetailsAllWereCaptured = "All details were captured in snapshot {0}.";

        const string kCrossSessionDiff = "Comparing snapshots from different sessions.";
        const string kUnknownSessionDiff = "Comparing snapshots with an unknown session.";
        const string kCrossSessionDiffDetails = "The Memory Profiler can only compare snapshots at full detail level when both originate from the same (known) session. In all other cases, memory layout, addresses and instance IDs can not be assumed to be comparable.";

        const string kCrossUnityVersionDiff = "Comparing snapshots from different Unity versions.";
        const string kCrossUnityVersionDiffDetail = "Snapshot A was taken from Unity version '{0}', while snapshot B was taken from Unity version '{1}'. Some change in memory usage might be due to the different base Unity versions used while some might be due to changes in the project.";

        /// <summary>
        /// Not All Capture Flags are important to check to raise issues for their presence or absence
        /// e.g. <see cref="CaptureFlags.NativeStackTraces"/> and/or <see cref="CaptureFlags.NativeAllocationSites"/>
        /// might have been requested (or not) but that is currently not relevant as there is no UI for them.
        /// It also in general does not matter for comparisons if one but not the other has these defined.
        ///
        /// Further, future flags might be added and old package versions would have no idea what those are
        /// and if they would be important to raise issues on these, so only check these cleared list of flags.
        /// </summary>
        const CaptureFlags kCaptureFlagsRelevantForIssueEntries = (CaptureFlags.ManagedObjects | CaptureFlags.NativeObjects | CaptureFlags.NativeAllocations);

        // Data.
        readonly CachedSnapshot m_BaseSnapshot;
        readonly CachedSnapshot m_CompareSnapshot;

        public SnapshotIssuesModelBuilder(CachedSnapshot baseSnapshot, CachedSnapshot compareSnapshot)
        {
            m_BaseSnapshot = baseSnapshot;
            m_CompareSnapshot = compareSnapshot;
        }

        public SnapshotIssuesModel Build()
        {
            var issues = new List<SnapshotIssuesModel.Issue>();

            if (m_CompareSnapshot == null)
                GatherIssuesSingle(issues, m_BaseSnapshot);
            else
                GatherIssuesCompare(issues, m_BaseSnapshot, m_CompareSnapshot);

            // Sort by issue level, reverse order so that higher priority issues are first
            issues.Sort((a, b) =>
            {
                return -a.IssueLevel.CompareTo(b.IssueLevel);
            });

            return new SnapshotIssuesModel(issues);
        }

        void GatherIssuesSingle(List<SnapshotIssuesModel.Issue> results, CachedSnapshot snapshot)
        {
            if (snapshot.MetaData.IsEditorCapture)
                AddIssue(results, kEditorHint, kEditorHintDetails, SnapshotIssuesModel.IssueLevel.Warning);

            if (!snapshot.HasTargetAndMemoryInfo)
                AddIssue(results, kOldSnapshotWarning, kOldSnapshotWarningContent, SnapshotIssuesModel.IssueLevel.Warning);

            if (snapshot.NativeMemoryRegions.UsesSystemAllocator)
                AddIssue(results, kSystemAllocatorWarning, kSystemAllocatorWarningDetails, SnapshotIssuesModel.IssueLevel.Warning);

            AddCaptureFlagsInfo(results, snapshot);
        }

        void GatherIssuesCompare(List<SnapshotIssuesModel.Issue> results, CachedSnapshot snapshotA, CachedSnapshot snapshotB)
        {
            // Check snapshot source - warn if it's Editor
            if (snapshotA.MetaData.IsEditorCapture)
            {
                if (snapshotB.MetaData.IsEditorCapture)
                    AddIssue(results, kEditorHintDiffBoth, kEditorHintDetails, SnapshotIssuesModel.IssueLevel.Warning);
                else
                    AddIssue(results, kEditorHintDiffOne, kEditorHintDiffOneDetails, SnapshotIssuesModel.IssueLevel.Warning);
            }
            else if (snapshotB.MetaData.IsEditorCapture)
                AddIssue(results, kEditorHintDiffOne, kEditorHintDiffOneDetails, SnapshotIssuesModel.IssueLevel.Warning);

            // mask flags to only contain flags relevant for issue entries
            var flagsA = snapshotA.MetaData.CaptureFlags & kCaptureFlagsRelevantForIssueEntries;
            var flagsB = snapshotB.MetaData.CaptureFlags & kCaptureFlagsRelevantForIssueEntries;
            // Compare capture flags
            if (flagsA != flagsB)
            {
                var strBuilder = new StringBuilder();
                var missingSupportedCaptureFlagCountA = CheckMissingCaptureFlags(flagsA, out bool noNativeObjectsA, out bool noNativeAllocationsA, out bool noManagedObjectsA);

                var missingSupportedCaptureFlagCountB = CheckMissingCaptureFlags(flagsB, out bool noNativeObjectsB, out bool noNativeAllocationsB, out bool noManagedObjectsB);

                if (missingSupportedCaptureFlagCountA == 0)
                    strBuilder.AppendFormat(kCaptureFlagsDiffDetailsAllWereCaptured, "A");
                else
                    strBuilder.AppendFormat(kCaptureFlagsDiffDetails, BuildCaptureFlagEnumeration(kCaptureFlagsDiffDetailsBaseString, missingSupportedCaptureFlagCountA, noNativeObjectsA, noNativeAllocationsA, noManagedObjectsA), "A");
                strBuilder.AppendLine();
                if (missingSupportedCaptureFlagCountB == 0)
                    strBuilder.AppendFormat(kCaptureFlagsDiffDetailsAllWereCaptured, "B");
                else
                    strBuilder.AppendFormat(kCaptureFlagsDiffDetails, BuildCaptureFlagEnumeration(kCaptureFlagsDiffDetailsBaseString, missingSupportedCaptureFlagCountB, noNativeObjectsB, noNativeAllocationsB, noManagedObjectsB), "B");
                AddIssue(results, kCaptureFlagsDiff, strBuilder.ToString(), SnapshotIssuesModel.IssueLevel.Warning);
            }
            else
                AddCaptureFlagsInfo(results, snapshotA);

            // Warn if it's not from the same session
            if (snapshotA.MetaData.SessionGUID == 0 || snapshotB.MetaData.SessionGUID == 0)
            {
                AddIssue(results, kUnknownSessionDiff, kCrossSessionDiffDetails, SnapshotIssuesModel.IssueLevel.Info);
            }
            else if (snapshotA.MetaData.SessionGUID != snapshotB.MetaData.SessionGUID)
            {
                AddIssue(results, kCrossSessionDiff, kCrossSessionDiffDetails, SnapshotIssuesModel.IssueLevel.Info);
            }

            // Warn if system allocator is used
            if (snapshotA.NativeMemoryRegions.UsesSystemAllocator || snapshotB.NativeMemoryRegions.UsesSystemAllocator)
            {
                var strBuilder = new StringBuilder();
                if (snapshotA.NativeMemoryRegions.UsesSystemAllocator == snapshotB.NativeMemoryRegions.UsesSystemAllocator)
                    strBuilder.AppendLine(kSystemAllocatorWarningDetailsDiffBoth);
                else if (snapshotA.NativeMemoryRegions.UsesSystemAllocator)
                    strBuilder.AppendLine(kSystemAllocatorWarningDetailsDiffA);
                else
                    strBuilder.AppendLine(kSystemAllocatorWarningDetailsDiffB);

                strBuilder.Append(kSystemAllocatorWarningDetails);
                AddIssue(results, kSystemAllocatorWarning, strBuilder.ToString(), SnapshotIssuesModel.IssueLevel.Warning);
            }

            // Warn if different Unity versions
            if (snapshotA.MetaData.UnityVersion != snapshotB.MetaData.UnityVersion)
            {
                var strBuilder = new StringBuilder(kCrossUnityVersionDiffDetail.Length + snapshotA.MetaData.UnityVersion.Length + snapshotB.MetaData.UnityVersion.Length);
                strBuilder.AppendFormat(kCrossUnityVersionDiffDetail, snapshotA.MetaData.UnityVersion, snapshotB.MetaData.UnityVersion);
                AddIssue(results, kCrossUnityVersionDiff, strBuilder.ToString(), SnapshotIssuesModel.IssueLevel.Info);
            }
        }

        public void AddIssue(List<SnapshotIssuesModel.Issue> results, string message, string tooltip, SnapshotIssuesModel.IssueLevel issueLevel)
        {
            results.Add(new SnapshotIssuesModel.Issue()
            {
                Summary = message,
                IssueLevel = issueLevel,
                Details = tooltip,
            });
        }

        void AddCaptureFlagsInfo(List<SnapshotIssuesModel.Issue> results, CachedSnapshot snapshot)
        {
            // mask flags to only contain flags relevant for issue entries
            var flags = snapshot.MetaData.CaptureFlags & kCaptureFlagsRelevantForIssueEntries;

            var missingSupportedCaptureFlagCount = CheckMissingCaptureFlags(flags, out bool noNativeObjects, out bool noNativeAllocations, out bool noManagedObjects);
            if (missingSupportedCaptureFlagCount > 0)
            {
                var detailsStringBuilder = new StringBuilder();
                var issueTitle = BuildCaptureFlagEnumeration(kCaptureFlagsMissing, missingSupportedCaptureFlagCount, noNativeObjects, noNativeAllocations, noManagedObjects);
                switch (missingSupportedCaptureFlagCount)
                {
                    case 3:
                        detailsStringBuilder.AppendLine(kFullCaptureFlagsNativeObjectsDetails);
                        detailsStringBuilder.AppendLine(kFullCaptureFlagsNativeAllocationsDetails);
                        detailsStringBuilder.Append(kFullCaptureFlagsManagedObjectsDetails);
                        AddIssue(results, issueTitle, detailsStringBuilder.ToString(), SnapshotIssuesModel.IssueLevel.Info);
                        break;
                    case 2:
                        detailsStringBuilder.AppendLine(noNativeObjects ? kFullCaptureFlagsNativeObjectsDetails : kFullCaptureFlagsNativeAllocationsDetails);
                        detailsStringBuilder.Append(noNativeAllocations ? (noNativeObjects ? kFullCaptureFlagsNativeAllocationsDetails : kFullCaptureFlagsManagedObjectsDetails) : kFullCaptureFlagsManagedObjectsDetails);

                        AddIssue(results, issueTitle, detailsStringBuilder.ToString(), SnapshotIssuesModel.IssueLevel.Info);
                        break;
                    case 1:
                        if (noNativeObjects)
                            AddIssue(results, issueTitle, kFullCaptureFlagsNativeObjectsDetails, SnapshotIssuesModel.IssueLevel.Info);
                        else if (noNativeAllocations)
                            AddIssue(results, issueTitle, kFullCaptureFlagsNativeAllocationsDetails, SnapshotIssuesModel.IssueLevel.Info);
                        else if (noManagedObjects)
                            AddIssue(results, issueTitle, kFullCaptureFlagsManagedObjectsDetails, SnapshotIssuesModel.IssueLevel.Info);
                        break;
                    default:
                        break;
                }
            }
        }

        int CheckMissingCaptureFlags(CaptureFlags flags, out bool noNativeObjects, out bool noNativeAllocations, out bool noManagedObjects)
        {
            int missingSupportedCaptureFlagCount = 0;
            noNativeObjects = !flags.HasFlag(CaptureFlags.NativeObjects);
            missingSupportedCaptureFlagCount += noNativeObjects ? 1 : 0;
            noNativeAllocations = !flags.HasFlag(CaptureFlags.NativeAllocations);
            missingSupportedCaptureFlagCount += noNativeAllocations ? 1 : 0;
            noManagedObjects = !flags.HasFlag(CaptureFlags.ManagedObjects);
            missingSupportedCaptureFlagCount += noManagedObjects ? 1 : 0;
            return missingSupportedCaptureFlagCount;
        }

        string BuildCaptureFlagEnumeration(string captureFlagsMissingBaseString, int missingSupportedCaptureFlagCount, bool noNativeObjects, bool noNativeAllocations, bool noManagedObjects)
        {
            switch (missingSupportedCaptureFlagCount)
            {
                case 3:
                    return string.Format(captureFlagsMissingBaseString, string.Format(kEnumerating3, kCaptureFlagsNativeObjects, kCaptureFlagsNativeAllocations, kCaptureFlagsManagedObjects));
                case 2:
                    return string.Format(captureFlagsMissingBaseString, string.Format(kEnumerating2,
                        noNativeObjects ? kCaptureFlagsNativeObjects : kCaptureFlagsNativeAllocations,
                        noNativeAllocations ? (noNativeObjects ? kCaptureFlagsNativeAllocations : kCaptureFlagsManagedObjects) : kCaptureFlagsManagedObjects));
                case 1:
                    if (noNativeObjects)
                        return string.Format(captureFlagsMissingBaseString, kCaptureFlagsNativeObjects);
                    else if (noNativeAllocations)
                        return string.Format(captureFlagsMissingBaseString, kCaptureFlagsNativeAllocations);
                    else if (noManagedObjects)
                        return string.Format(captureFlagsMissingBaseString, kCaptureFlagsManagedObjects);
                    else
                        throw new NotImplementedException();
                default:
                    throw new NotImplementedException();
            }
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor;
using System;
using Unity.MemoryProfiler.Editor.Format;
using System.Text;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class TopIssues
    {
        static class Content
        {
            public static readonly string EditorHint = L10n.Tr("Editor capture! Get better insights by building and profiling a development build, as memory behaves quite differently in the Editor.");
            public static readonly string EditorHintDiffBoth = L10n.Tr("Comparing Editor captures! Get better insights by building and profiling a development build, as memory behaves quite differently in the Editor.");
            public static readonly string EditorHintDiffOne = L10n.Tr("Comparing an Editor capture to a Player Capture!");
            public static readonly string EditorHintDiffOneDetails = L10n.Tr("Get better insights by building and profiling a development build, as memory behaves quite differently in the Editor. These differences will make this comparison look quite odd.");
            public static readonly string EditorHintDetails = L10n.Tr("The memory used in the Editor is not cleanly separated into Play Mode and Edit Mode, but is a fluid mix. Things might be held in memory by editor related functionality, load and unload into memory at different points in time and can take up a different amount than it would in a build on your targeted hardware. Make a development build, attach the Memory Profiler to it and take a capture from that Player to get a more accurate and relevant picture of your memory usage.");

            public static readonly string OldSnapshotWarning = L10n.Tr("Snapshot from an outdated Unity version that is not fully supported.");
            public static readonly string OldSnapshotWarningContent = L10n.Tr("The functionality of the Memory Profiler package version 0.4 and newer builds on data only reported in newer Unity versions. " + TextContent.PreSnapshotVersion11UpdgradeInfo);

            public static readonly string SystemAllocatorWarning = L10n.Tr("System Allocator is used. It is generally advised to use the Dynamic Heap Allocator instead.");
            public static readonly string SystemAllocatorWarningDetailsDiffA = L10n.Tr("System Allocator is used in snapshot A.");
            public static readonly string SystemAllocatorWarningDetailsDiffB = L10n.Tr("System Allocator is used in snapshot B.");
            public static readonly string SystemAllocatorWarningDetailsDiffBoth = L10n.Tr("System Allocator is used in both snapshots.");
            public static readonly string SystemAllocatorWarningDetails = L10n.Tr("Dynamic Heap Allocator is generally more efficient than the System Allocator. Additionally, Native Objects can be allocated outside of Native Regions when using the System Allocator, so the Fragmentation page can't determine as well how much contiguous memory belongs to their initial allocation.");

            public static readonly string NoIssuesFound = L10n.Tr("No Issues detected.");
            public static readonly string Enumerating3 = L10n.Tr("{0}, {1} and {2}");
            public static readonly string Enumerating2 = L10n.Tr("{0} and {1}");
            public static readonly string CaptureFlagsMissing = L10n.Tr("{0} were not captured.");

            public static readonly string CaptureFlagsNativeObjects = L10n.Tr("Native Objects");
            public static readonly string CaptureFlagsNativeObjectsDetails = L10n.Tr("To capture and inspect Native Object memory usage, select the Native Objects option from the capture options in the drop-down menu of the Capture button or via {0}.{1} when using the API to take a capture.");
            public static string FullCaptureFlagsNativeObjectsDetails => string.Format(Content.CaptureFlagsNativeObjectsDetails, nameof(CaptureFlags), nameof(CaptureFlags.NativeObjects));

            public static readonly string CaptureFlagsNativeAllocations = L10n.Tr("Native Allocations");
            public static readonly string CaptureFlagsNativeAllocationsDetails = L10n.Tr("To capture and inspect Native Allocations, select the Native Allocations option from the capture options in the drop-down menu of the Capture button or via {0}.{1} when using the API to take a capture. Also note: Without Native Allocations being captured, no determination can be made regarding whether or not the System Allocator was used.");
            public static string FullCaptureFlagsNativeAllocationsDetails => string.Format(Content.CaptureFlagsNativeAllocationsDetails, nameof(CaptureFlags), nameof(CaptureFlags.NativeAllocations));

            public static readonly string CaptureFlagsManagedObjects = L10n.Tr("Managed Objects");
            public static readonly string CaptureFlagsManagedObjectsDetails = L10n.Tr("To capture and inspect Managed Object memory usage, select the Managed Objects option from the capture options in the drop-down menu of the Capture button or via {0}.{1} when using the API to take a capture.");
            public static string FullCaptureFlagsManagedObjectsDetails => string.Format(Content.CaptureFlagsManagedObjectsDetails, nameof(CaptureFlags), nameof(CaptureFlags.ManagedObjects));

            public static readonly string CaptureFlagsDiff = L10n.Tr("Comparing snapshots with different Capture options.");
            public static readonly string CaptureFlagsDiffDetailsBaseString = L10n.Tr("{0}");
            public static readonly string CaptureFlagsDiffDetails = L10n.Tr("{0} were not captured in snapshot {1}.");
            public static readonly string CaptureFlagsDiffDetailsAllWereCaptured = L10n.Tr("All details were captured in snapshot {0}.");


            public static readonly string CrossSessionDiff = L10n.Tr("Comparing snapshots from different sessions.");
            public static readonly string UnknownSessionDiff = L10n.Tr("Comparing snapshots with an unknown session.");
            public static readonly string CrossSessionDiffDetails = L10n.Tr("The Memory Profiler can only compare snapshots at full detail level when both originate from the same (known) session. In all other cases, memory layout, addresses and instance IDs can not be assumed to be comparable.");


            public static readonly string CrossUnityVersionDiff = L10n.Tr("Comparing snapshots from different Unity versions.");
            public static readonly string CrossUnityVersionDiffDetail = L10n.Tr("Snapshot A was taken from Unity version '{0}', while snapshot B was taken from Unity version '{1}'. Some change in memory usage might be due to the different base Unity versions used while some might be due to changes in the project.");
        }

        VisualElement m_Root;
        VisualElement m_Content;
        Label m_NoIssuesFoundLabel;
        IssueTypeCount m_InfoCount;
        IssueTypeCount m_WarningCount;
        IssueTypeCount m_ErrorCount;

        struct IssueTypeCount
        {
            public Label Label;
            public VisualElement Icon;

            int m_Count;
            public int Count
            {
                get { return m_Count; }
                set
                {
                    m_Count = value;
                    Label.text = value.ToString();
                    UIElementsHelper.SetVisibility(Label, m_Count > 0);
                    UIElementsHelper.SetVisibility(Icon, m_Count > 0);
                }
            }
        }

        public TopIssues(VisualElement root)
        {
            if (root.childCount == 0)
            {
                VisualTreeAsset memoryUsageSummaryViewTree;
                memoryUsageSummaryViewTree = AssetDatabase.LoadAssetAtPath(ResourcePaths.TopIssuesUxmlPath, typeof(VisualTreeAsset)) as VisualTreeAsset;

                m_Root = memoryUsageSummaryViewTree.Clone(root).Q("top-ten-issues-section");
            }
            else
            {
                m_Root = root;
            }
            m_Content = m_Root.Q("issue-list", "top-ten-issues-section__list");
            m_NoIssuesFoundLabel = new Label(Content.NoIssuesFound) { name = "top-ten-issues-section__content__no-issues-found" };
            m_NoIssuesFoundLabel.AddToClassList("top-ten-issues-section__content__no-issues-found");

            m_InfoCount.Label = m_Root.Q<Label>("top-ten-issues-section__header__message-bubbles__info-count");
            m_InfoCount.Icon = m_Root.Q("top-ten-issues-section__header__message-bubbles__info-icon");

            m_WarningCount.Label = m_Root.Q<Label>("top-ten-issues-section__header__message-bubbles__warning-count");
            m_WarningCount.Icon = m_Root.Q("top-ten-issues-section__header__message-bubbles__warning-icon");

            m_ErrorCount.Label = m_Root.Q<Label>("top-ten-issues-section__header__message-bubbles__error-count");
            m_ErrorCount.Icon = m_Root.Q("top-ten-issues-section__header__message-bubbles__error-icon");

            var foldout = m_Root.Q<Foldout>("top-ten-issues-section__header__foldout");

            foldout.RegisterValueChangedCallback((evt) =>
                MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInPage, MemoryProfilerAnalytics.PageInteractionType>(
                    evt.newValue ? MemoryProfilerAnalytics.PageInteractionType.AllTopIssuesWasRevealed : MemoryProfilerAnalytics.PageInteractionType.AllTopIssuesWasHidden));

            ClearContent();
        }

        void ClearContent()
        {
            UIElementsHelper.SetVisibility(m_NoIssuesFoundLabel, true);
            m_Content.Clear();
            m_Content.Add(m_NoIssuesFoundLabel);
            m_InfoCount.Count = 0;
            m_WarningCount.Count = 0;
            m_ErrorCount.Count = 0;
        }

        public void InitializeIssues(CachedSnapshot snapshot)
        {
            ClearContent();
            if (snapshot.MetaData.Platform.Contains("Editor"))
                AddIssue(Content.EditorHint, Content.EditorHintDetails, IssueLevel.Warning, priority: 100);

            if (!snapshot.HasTargetAndMemoryInfo)
                AddIssue(Content.OldSnapshotWarning, Content.OldSnapshotWarningContent, IssueLevel.Warning, priority: 200, DocumentationUrls.Requirements);


            if (snapshot.NativeMemoryRegions.UsesSystemAllocator)
                AddIssue(Content.SystemAllocatorWarning, Content.SystemAllocatorWarningDetails, IssueLevel.Warning);

            AddCaptureFlagsInfo(snapshot);
        }

        public void InitializeIssues(CachedSnapshot snapshotA, CachedSnapshot snapshotB)
        {
            ClearContent();
            if (snapshotA.MetaData.Platform.Contains("Editor"))
            {
                if (snapshotB.MetaData.Platform.Contains("Editor"))
                    AddIssue(Content.EditorHintDiffBoth, Content.EditorHintDetails, IssueLevel.Warning, priority: 100);
                else
                    AddIssue(Content.EditorHintDiffOne, Content.EditorHintDiffOneDetails, IssueLevel.Warning, priority: 100);
            }
            else if (snapshotB.MetaData.Platform.Contains("Editor"))
                AddIssue(Content.EditorHintDiffOne, Content.EditorHintDiffOneDetails, IssueLevel.Warning, priority: 100);

            if (snapshotA.CaptureFlags != snapshotB.CaptureFlags)
            {
                var strBuilder = new StringBuilder();
                bool noNativeObjectsA, noNativeAllocationsA, noManagedObjectsA;
                var missingSupportedCaptureFlagCountA = CheckMissingCaptureFlags(snapshotA.CaptureFlags, out noNativeObjectsA, out noNativeAllocationsA, out noManagedObjectsA);

                bool noNativeObjectsB, noNativeAllocationsB, noManagedObjectsB;
                var missingSupportedCaptureFlagCountB = CheckMissingCaptureFlags(snapshotB.CaptureFlags, out noNativeObjectsB, out noNativeAllocationsB, out noManagedObjectsB);
                if (missingSupportedCaptureFlagCountA == 0)
                    strBuilder.AppendFormat(Content.CaptureFlagsDiffDetailsAllWereCaptured, "A");
                else
                    strBuilder.AppendFormat(Content.CaptureFlagsDiffDetails, BuildCaptureFlagEnumeration(Content.CaptureFlagsDiffDetailsBaseString, missingSupportedCaptureFlagCountA, noNativeObjectsA, noNativeAllocationsA, noManagedObjectsA), "A");
                strBuilder.AppendLine();
                if (missingSupportedCaptureFlagCountB == 0)
                    strBuilder.AppendFormat(Content.CaptureFlagsDiffDetailsAllWereCaptured, "B");
                else
                    strBuilder.AppendFormat(Content.CaptureFlagsDiffDetails, BuildCaptureFlagEnumeration(Content.CaptureFlagsDiffDetailsBaseString, missingSupportedCaptureFlagCountB, noNativeObjectsB, noNativeAllocationsB, noManagedObjectsB), "B");
                AddIssue(Content.CaptureFlagsDiff, strBuilder.ToString(), IssueLevel.Warning, documentationURL: DocumentationUrls.CaptureFlagsHelp);
            }
            else
                AddCaptureFlagsInfo(snapshotA);

            if (snapshotA.MetaData.SessionGUID == 0 || snapshotB.MetaData.SessionGUID == 0)
            {
                AddIssue(Content.UnknownSessionDiff, Content.CrossSessionDiffDetails, IssueLevel.Info);
            }
            else if (snapshotA.MetaData.SessionGUID != snapshotB.MetaData.SessionGUID)
            {
                AddIssue(Content.CrossSessionDiff, Content.CrossSessionDiffDetails, IssueLevel.Info);
            }

            if (snapshotA.NativeMemoryRegions.UsesSystemAllocator || snapshotB.NativeMemoryRegions.UsesSystemAllocator)
            {
                var strBuilder = new StringBuilder();
                if (snapshotA.NativeMemoryRegions.UsesSystemAllocator == snapshotB.NativeMemoryRegions.UsesSystemAllocator)
                    strBuilder.AppendLine(Content.SystemAllocatorWarningDetailsDiffBoth);
                else if (snapshotA.NativeMemoryRegions.UsesSystemAllocator)
                    strBuilder.AppendLine(Content.SystemAllocatorWarningDetailsDiffA);
                else
                    strBuilder.AppendLine(Content.SystemAllocatorWarningDetailsDiffB);

                strBuilder.Append(Content.SystemAllocatorWarningDetails);
                AddIssue(Content.SystemAllocatorWarning, strBuilder.ToString(), IssueLevel.Warning);
            }

            if (snapshotA.MetaData.UnityVersion != snapshotB.MetaData.UnityVersion)
            {
                var strBuilder = new StringBuilder(Content.CrossUnityVersionDiffDetail.Length + snapshotA.MetaData.UnityVersion.Length + snapshotB.MetaData.UnityVersion.Length);
                strBuilder.AppendFormat(Content.CrossUnityVersionDiffDetail, snapshotA.MetaData.UnityVersion, snapshotB.MetaData.UnityVersion);
                AddIssue(Content.CrossUnityVersionDiff, strBuilder.ToString(), IssueLevel.Info);
            }
        }

        public void AddIssue(string message, string tooltip, IssueLevel issueLevel = IssueLevel.Warning, float priority = 10f, string documentationURL = null, Action investigateAction = null)
        {
            UIElementsHelper.SetVisibility(m_NoIssuesFoundLabel, false);
            var issue = new TopIssueItem(message, tooltip, issueLevel, priority, documentationURL, investigateAction);
            var insertIndex = 0;
            foreach (var child in m_Content.Children())
            {
                if (child is TopIssueItem && (child as TopIssueItem).CompareTo(issue) <= 0)
                    break;
                ++insertIndex;
            }
            m_Content.Insert(insertIndex, issue);

            switch (issueLevel)
            {
                case IssueLevel.Info:
                    ++m_InfoCount.Count;
                    break;
                case IssueLevel.Warning:
                    ++m_WarningCount.Count;
                    break;
                case IssueLevel.Error:
                    ++m_ErrorCount.Count;
                    break;
                default:
                    break;
            }
        }

        void AddCaptureFlagsInfo(CachedSnapshot snapshot)
        {
            bool noNativeObjects, noNativeAllocations, noManagedObjects;
            var missingSupportedCaptureFlagCount = CheckMissingCaptureFlags(snapshot.CaptureFlags, out noNativeObjects, out noNativeAllocations, out noManagedObjects);
            if (missingSupportedCaptureFlagCount > 0)
            {
                var detailsStringBuilder = new StringBuilder();
                var issueTitle = BuildCaptureFlagEnumeration(Content.CaptureFlagsMissing, missingSupportedCaptureFlagCount, noNativeObjects, noNativeAllocations, noManagedObjects);
                switch (missingSupportedCaptureFlagCount)
                {
                    case 3:
                        detailsStringBuilder.AppendLine(Content.FullCaptureFlagsNativeObjectsDetails);
                        detailsStringBuilder.AppendLine(Content.FullCaptureFlagsNativeAllocationsDetails);
                        detailsStringBuilder.Append(Content.FullCaptureFlagsManagedObjectsDetails);
                        AddIssue(issueTitle, detailsStringBuilder.ToString(), IssueLevel.Info, documentationURL: DocumentationUrls.CaptureFlagsHelp);;
                        break;
                    case 2:
                        detailsStringBuilder.AppendLine(noNativeObjects ? Content.FullCaptureFlagsNativeObjectsDetails : Content.FullCaptureFlagsNativeAllocationsDetails);
                        detailsStringBuilder.Append(noNativeAllocations ? (noNativeObjects ? Content.FullCaptureFlagsNativeAllocationsDetails : Content.FullCaptureFlagsManagedObjectsDetails) : Content.FullCaptureFlagsManagedObjectsDetails);

                        AddIssue(issueTitle, detailsStringBuilder.ToString(), IssueLevel.Info, documentationURL: DocumentationUrls.CaptureFlagsHelp);
                        break;
                    case 1:
                        if (noNativeObjects)
                            AddIssue(issueTitle, Content.FullCaptureFlagsNativeObjectsDetails, IssueLevel.Info, documentationURL: DocumentationUrls.CaptureFlagsHelp);
                        else if (noNativeAllocations)
                            AddIssue(issueTitle, Content.FullCaptureFlagsNativeAllocationsDetails, IssueLevel.Info, documentationURL: DocumentationUrls.CaptureFlagsHelp);
                        else if (noManagedObjects)
                            AddIssue(issueTitle, Content.FullCaptureFlagsManagedObjectsDetails, IssueLevel.Info, documentationURL: DocumentationUrls.CaptureFlagsHelp);
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
            noNativeAllocations = !flags.HasFlag(Format.CaptureFlags.NativeAllocations);
            missingSupportedCaptureFlagCount += noNativeAllocations ? 1 : 0;
            noManagedObjects = !flags.HasFlag(Format.CaptureFlags.ManagedObjects);
            missingSupportedCaptureFlagCount += noManagedObjects ? 1 : 0;
            return missingSupportedCaptureFlagCount;
        }

        string BuildCaptureFlagEnumeration(string captureFlagsMissingBaseString, int missingSupportedCaptureFlagCount, bool noNativeObjects, bool noNativeAllocations, bool noManagedObjects)
        {
            switch (missingSupportedCaptureFlagCount)
            {
                case 3:
                    return string.Format(captureFlagsMissingBaseString, string.Format(Content.Enumerating3, Content.CaptureFlagsNativeObjects, Content.CaptureFlagsNativeAllocations, Content.CaptureFlagsManagedObjects));
                case 2:
                    return string.Format(captureFlagsMissingBaseString, string.Format(Content.Enumerating2,
                        noNativeObjects ? Content.CaptureFlagsNativeObjects : Content.CaptureFlagsNativeAllocations,
                        noNativeAllocations ? (noNativeObjects ? Content.CaptureFlagsNativeAllocations : Content.CaptureFlagsManagedObjects) : Content.CaptureFlagsManagedObjects));
                case 1:
                    if (noNativeObjects)
                        return string.Format(captureFlagsMissingBaseString, Content.CaptureFlagsNativeObjects);
                    else if (noNativeAllocations)
                        return string.Format(captureFlagsMissingBaseString, Content.CaptureFlagsNativeAllocations);
                    else if (noManagedObjects)
                        return string.Format(captureFlagsMissingBaseString, Content.CaptureFlagsManagedObjects);
                    else
                        throw new NotImplementedException();
                default:
                    throw new NotImplementedException();
            }
        }

        public void SetVisibility(bool visible)
        {
            UIElementsHelper.SetVisibility(m_Root, visible);
        }
    }
}

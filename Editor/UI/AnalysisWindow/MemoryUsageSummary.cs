using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using System;
using System.Collections.Generic;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class MemoryUsageSummary
    {
        VisualElement m_MemoryUsageSummary;

        InfoBox m_OldSnapshotInfo;

        MemoryUsageBreakdown m_HighLevelBreakdown;
        MemoryUsageBreakdown m_CommittedTrackingStatusBreakdown;
        MemoryUsageBreakdown m_ManagedBreakdown;
        MemoryUsageBreakdown m_AllocationBreakdown;

        Label m_SelectionLabel;

        public MemoryUsageSummary(VisualElement root)
        {
            if (root.childCount == 0)
            {
                VisualTreeAsset memoryUsageSummaryViewTree;
                memoryUsageSummaryViewTree = AssetDatabase.LoadAssetAtPath(ResourcePaths.MemoryUsageSummaryUxmlPath, typeof(VisualTreeAsset)) as VisualTreeAsset;

                m_MemoryUsageSummary = memoryUsageSummaryViewTree.Clone(root);
            }
            else
            {
                m_MemoryUsageSummary = root;
            }

            m_OldSnapshotInfo = m_MemoryUsageSummary.Q<InfoBox>("memory-usage-summary-section__old-snapshot-info-box");
            m_OldSnapshotInfo.DocumentationLink = DocumentationUrls.Requirements;
            UIElementsHelper.SetVisibility(m_OldSnapshotInfo, false);

            m_HighLevelBreakdown = m_MemoryUsageSummary.Q<MemoryUsageBreakdown>("memory-usage-summary-section__high-level-summary");
            m_CommittedTrackingStatusBreakdown = m_MemoryUsageSummary.Q<MemoryUsageBreakdown>("memory-usage-summary-section__high-level-summary__top-level");
            m_ManagedBreakdown = m_MemoryUsageSummary.Q<MemoryUsageBreakdown>("memory-usage-summary-section__managed-memory");
            m_AllocationBreakdown = m_MemoryUsageSummary.Q<MemoryUsageBreakdown>("memory-usage-summary-section__allocation-types-summary");
            m_HighLevelBreakdown.Setup();
            m_CommittedTrackingStatusBreakdown.Setup();
            m_ManagedBreakdown.Setup();
            m_AllocationBreakdown.Setup();
            UIElementsHelper.SetVisibility(m_ManagedBreakdown.parent, true);
            // TODO: fix calculation of data and unhide
            UIElementsHelper.SetVisibility(m_AllocationBreakdown, false);

            m_SelectionLabel = m_MemoryUsageSummary.Q<Label>("memory-usage-summary-section__selection-label");
            // TODO: Implement Selection tracking and Highlighting
            UIElementsHelper.SetVisibility(m_SelectionLabel, false);

            m_Used = new List<ulong[]>();
            m_Reserved = new List<ulong[]>();

            for (int i = 0; i < Enum.GetValues(typeof(BreakdownOrder)).Length; i++)
            {
                m_Used.Add(new ulong[Enum.GetValues(typeof(Columns)).Length]);
                m_Reserved.Add(new ulong[Enum.GetValues(typeof(Columns)).Length]);
            }
        }

        ulong[] m_TotalCommitedUsed = new ulong[3];
        ulong[] m_TotalCommitedReserved = new ulong[3];

        enum BreakdownOrder
        {
            ManagedHeap = 0,
            ManagedDomain,
            Graphics,
            Audio,
            Other,
            Profiler,
            ExecutableAndDlls,
        }

        List<ulong[]> m_Used;
        List<ulong[]> m_Reserved;
        ulong[] m_systemUsed = new ulong[2];

        enum Columns
        {
            A = 0,
            B = 1,
            Diff = 2
        }

        public void SetSummaryValues(CachedSnapshot snapshotA, CachedSnapshot snapshotB = null)
        {
            SetBAndDiffVisibility(snapshotB != null);

            if (m_HighLevelBreakdown == null)
                return;
            if ((!snapshotA.HasTargetAndMemoryInfo || !snapshotA.HasMemoryLabelSizesAndGCHeapTypes) ||
                (snapshotB != null && (!snapshotB.HasTargetAndMemoryInfo || !snapshotB.HasMemoryLabelSizesAndGCHeapTypes)))
            {
                m_OldSnapshotInfo.Message = TextContent.MemoryUsageUnavailableMessage;
                UIElementsHelper.SetVisibility(m_OldSnapshotInfo, true);
                UIElementsHelper.SetVisibility(m_HighLevelBreakdown, false);
                UIElementsHelper.SetVisibility(m_CommittedTrackingStatusBreakdown, false);
                UIElementsHelper.SetVisibility(m_ManagedBreakdown.parent, false);
                return;
            }
            else
            {
                UIElementsHelper.SetVisibility(m_OldSnapshotInfo, false);
                UIElementsHelper.SetVisibility(m_HighLevelBreakdown, true);
                UIElementsHelper.SetVisibility(m_CommittedTrackingStatusBreakdown, true);
                UIElementsHelper.SetVisibility(m_ManagedBreakdown.parent, true);
            }

            m_systemUsed[0] = m_systemUsed[1] = 0ul;
            if (snapshotA.MetaData.TargetMemoryStats.HasValue)
            {
                GetHighLevelBreakdownValues(snapshotA, 0, out m_systemUsed[0]);
            }

            if (snapshotB != null && snapshotB.MetaData.TargetMemoryStats.HasValue)
            {
                GetHighLevelBreakdownValues(snapshotB, 1, out m_systemUsed[1]);
            }

            var totalIsKnown = m_systemUsed[0] != 0ul;

            if (!totalIsKnown)
                m_systemUsed[0] = m_TotalCommitedReserved[0];

            // TODO: for Diff view, offer normalized option and set this to the bigger of the two snapshots totals
            var maxToNormalizeTo = m_systemUsed;

            m_HighLevelBreakdown.SetValues(m_systemUsed, m_Reserved, m_Used, false, maxToNormalizeTo, totalIsKnown);
            m_CommittedTrackingStatusBreakdown.SetValues(m_systemUsed, new List<ulong[]> {m_TotalCommitedReserved}, new List<ulong[]> {m_TotalCommitedUsed}, false, maxToNormalizeTo, totalIsKnown);

            // managed breakdown

            ulong[] totalManagedMemory = new ulong[2];
            totalManagedMemory[0] = snapshotA.ManagedHeapSections.ManagedHeapMemoryReserved + snapshotA.ManagedHeapSections.VirtualMachineMemoryReserved + snapshotA.ManagedStacks.StackMemoryReserved;
            if (snapshotB != null)
                totalManagedMemory[1] = snapshotB.ManagedHeapSections.ManagedHeapMemoryReserved + snapshotB.ManagedHeapSections.VirtualMachineMemoryReserved + snapshotB.ManagedStacks.StackMemoryReserved;

            // TODO: for Diff view, offer normalized option and set this to the bigger of the two snapshots totals
            maxToNormalizeTo = totalManagedMemory;

            List<ulong[]> reserved = new List<ulong[]>();
            List<ulong[]> used = new List<ulong[]>();
            var hasValidManagedData = GetHighLevelBreakDownForSnapshot(snapshotA, (int)Columns.A, totalManagedMemory[0], maxToNormalizeTo, reserved, used);
            if (hasValidManagedData && snapshotB != null)
                hasValidManagedData = GetHighLevelBreakDownForSnapshot(snapshotB, (int)Columns.B, totalManagedMemory[1], maxToNormalizeTo, reserved, used);

            if (hasValidManagedData)
            {
                UIElementsHelper.SetVisibility(m_ManagedBreakdown, true);
                m_ManagedBreakdown.SetValues(totalManagedMemory, reserved, null, false, maxToNormalizeTo, true);
            }
            else
            {
                UIElementsHelper.SetVisibility(m_ManagedBreakdown, false);
            }

            // Allocation breakdown:
            if (m_AllocationBreakdown.visible)
            {
                ulong[] totalReservedMemory = new ulong[2];
                // add up all allocations
                totalReservedMemory[0] = 0ul;
                //for (long i = 0; i < snapshot.NativeAllocations.Count; i++)
                //{
                //    totalReservedMemory += snapshot.NativeAllocations.Size[i];
                //}
                for (long i = 0; i < snapshotA.NativeMemoryRegions.Count; i++)
                {
                    if (!snapshotA.NativeMemoryRegions.MemoryRegionName[i].Contains("Virtual Memory"))
                        totalReservedMemory[0] += snapshotA.NativeMemoryRegions.AddressSize[i];
                }
                //if (snapshot.HasMemoryLabelSizesAndGCHeapTypes)
                //{
                //    for (long i = 0; i < snapshot.NativeMemoryLabels.Count; i++)
                //    {
                //        totalReservedMemory += snapshot.NativeMemoryLabels.MemoryLabelSizes[i];
                //    }
                //}
                totalReservedMemory[0] += snapshotA.ManagedHeapSections.ManagedHeapMemoryReserved + snapshotA.ManagedHeapSections.VirtualMachineMemoryReserved + snapshotA.ManagedStacks.StackMemoryReserved;

                if (snapshotA.MetaData.TargetMemoryStats.HasValue)
                {
                    var memoryStats2 = snapshotA.MetaData.TargetMemoryStats.Value;
                    totalReservedMemory[0] += memoryStats2.GraphicsUsedMemory + /*memoryStats2.AudioUsedMemory +*/ memoryStats2.TempAllocatorUsedMemory;
                }

                {
                    used = new List<ulong[]>();
                    reserved = new List<ulong[]>();
                    for (int i = 0; i < 2; i++)
                    {
                        used.Add(new ulong[3]);
                        reserved.Add(new ulong[3]);
                    }

                    reserved[0][(int)Columns.A] = used[0][(int)Columns.A] = snapshotA.CrawledData.ManagedObjectMemoryUsage + snapshotA.NativeObjects.TotalSizes;

                    // TODO: figure out what's missing from totalReservedMemory at this point and why that is less than all Object memory
                    reserved[1][(int)Columns.A] = used[1][(int)Columns.A] = (ulong)Math.Max(0, ((long)totalReservedMemory[0] - (long)reserved[0][(int)Columns.A]));


                    if (snapshotB != null)
                    {
                        reserved[0][(int)Columns.B] = used[0][(int)Columns.A] = snapshotB.CrawledData.ManagedObjectMemoryUsage + snapshotB.NativeObjects.TotalSizes;

                        // TODO: figure out what's missing from totalReservedMemory at this point and why that is less than all Object memory
                        reserved[1][(int)Columns.B] = used[1][(int)Columns.B] = (ulong)Math.Max(0, ((long)totalReservedMemory[0] - (long)reserved[0][(int)Columns.B]));
                    }

                    if (snapshotA.MetaData.TargetMemoryStats.HasValue)
                        totalReservedMemory[0] = snapshotA.MetaData.TargetMemoryStats.Value.TotalVirtualMemory;

                    if (snapshotB != null && snapshotB.MetaData.TargetMemoryStats.HasValue)
                        totalReservedMemory[1] = snapshotB.MetaData.TargetMemoryStats.Value.TotalVirtualMemory;

                    // TODO: for Diff view, offer normalized option and set this to the bigger of the two snapshots totals
                    maxToNormalizeTo = totalReservedMemory;

                    m_AllocationBreakdown.SetValues(m_systemUsed,  reserved , null, false, maxToNormalizeTo, true);
                }
            }
        }

        bool GetHighLevelBreakDownForSnapshot(CachedSnapshot cs, int column, ulong totalReservedMemory, ulong[] maxToNormalizeTo, List<ulong[]> reserved, List<ulong[]> used)
        {
            if (cs.HasMemoryLabelSizesAndGCHeapTypes && cs.CaptureFlags.HasFlag(Format.CaptureFlags.ManagedObjects))
            {
                for (int i = 0; i < 4; i++)
                {
                    used.Add(new ulong[3]);
                    reserved.Add(new ulong[3]);
                }
                reserved[0][column] = used[0][column] = cs.ManagedHeapSections.VirtualMachineMemoryReserved;
                reserved[1][column] = used[1][column] = cs.CrawledData.ManagedObjectMemoryUsage;
                reserved[2][column] = used[2][column] = cs.ManagedHeapSections.StartAddress[cs.ManagedHeapSections.LastAssumedActiveHeapSectionIndex]
                    + cs.ManagedHeapSections.SectionSize[cs.ManagedHeapSections.LastAssumedActiveHeapSectionIndex]
                    - cs.ManagedHeapSections.StartAddress[cs.ManagedHeapSections.FirstAssumedActiveHeapSectionIndex]
                    - cs.CrawledData.ActiveHeapMemoryUsage;
                reserved[3][column] = used[3][column] = cs.ManagedHeapSections.ManagedHeapMemoryReserved - reserved[1][column] - reserved[2][column];
                return true;
            }
            return false;
        }

        void GetHighLevelBreakdownValues(CachedSnapshot cs, int column, out ulong systemUsedMemory)
        {
            var snapshotTargetMemoryStats = cs.MetaData.TargetMemoryStats.Value;
            systemUsedMemory = snapshotTargetMemoryStats.TotalVirtualMemory;

            var reservedMemoryAttributedToSpecificCategories =
                snapshotTargetMemoryStats.GcHeapReservedMemory
                + snapshotTargetMemoryStats.GraphicsUsedMemory
                + snapshotTargetMemoryStats.ProfilerReservedMemory
                + snapshotTargetMemoryStats.AudioUsedMemory;
            var usedMemoryAttributedToSpecificCategories =
                snapshotTargetMemoryStats.GcHeapUsedMemory
                + snapshotTargetMemoryStats.GraphicsUsedMemory
                + snapshotTargetMemoryStats.ProfilerUsedMemory
                + snapshotTargetMemoryStats.AudioUsedMemory;

            var unityMemoryReserved = snapshotTargetMemoryStats.TotalReservedMemory - reservedMemoryAttributedToSpecificCategories;
            var unityMemoryUsed = snapshotTargetMemoryStats.TotalUsedMemory - usedMemoryAttributedToSpecificCategories;

            if (reservedMemoryAttributedToSpecificCategories > snapshotTargetMemoryStats.TotalReservedMemory)
            {
#if ENABLE_MEMORY_PROFILER_DEBUG
                Debug.LogError("Reserved Memory attributed to categories is bigger than known reserved memory by " + EditorUtility.FormatBytes((long)(reservedMemoryAttributedToSpecificCategories - snapshotTargetMemoryStats.TotalReservedMemory)));
#endif
                unityMemoryReserved = 0;
            }
            if (usedMemoryAttributedToSpecificCategories > snapshotTargetMemoryStats.TotalUsedMemory)
            {
#if ENABLE_MEMORY_PROFILER_DEBUG
                Debug.LogError("Used Memory attributed to categories is bigger than known used memory by " + EditorUtility.FormatBytes((long)(usedMemoryAttributedToSpecificCategories - snapshotTargetMemoryStats.TotalUsedMemory)));
#endif
                unityMemoryUsed = 0;
            }

            // TODO: Temp (memoryStats2.TempAllocatorUsedMemory) should likely get it's own section in the bar

            m_Reserved[(int)BreakdownOrder.ManagedHeap][column] = snapshotTargetMemoryStats.GcHeapReservedMemory;
            m_Used[(int)BreakdownOrder.ManagedHeap][column] = snapshotTargetMemoryStats.GcHeapUsedMemory;

            m_Reserved[(int)BreakdownOrder.ManagedDomain][column] = cs.ManagedHeapSections.VirtualMachineMemoryReserved;
            m_Used[(int)BreakdownOrder.ManagedDomain][column] = cs.ManagedHeapSections.VirtualMachineMemoryReserved;

            m_Reserved[(int)BreakdownOrder.Graphics][column] = m_Used[(int)BreakdownOrder.Graphics][column] = snapshotTargetMemoryStats.GraphicsUsedMemory;

            m_Reserved[(int)BreakdownOrder.Audio][column] = m_Used[(int)BreakdownOrder.Audio][column] = snapshotTargetMemoryStats.AudioUsedMemory;

            m_Reserved[(int)BreakdownOrder.Other][column] = unityMemoryReserved;
            m_Used[(int)BreakdownOrder.Other][column] = unityMemoryUsed;

            m_Used[(int)BreakdownOrder.ExecutableAndDlls][column] = m_Reserved[(int)BreakdownOrder.ExecutableAndDlls][column] = 0ul;
            for (int i = 0; i < cs.NativeRootReferences.ObjectName.Length; i++)
            {
                if (cs.NativeRootReferences.ObjectName[i] == "ExecutableAndDlls")
                {
                    m_Used[(int)BreakdownOrder.ExecutableAndDlls][column] = m_Reserved[(int)BreakdownOrder.ExecutableAndDlls][column] = cs.NativeRootReferences.AccumulatedSize[i];
                    break;
                }
            }

            m_Reserved[(int)BreakdownOrder.Profiler][column] = snapshotTargetMemoryStats.ProfilerReservedMemory;
            m_Used[(int)BreakdownOrder.Profiler][column] = snapshotTargetMemoryStats.ProfilerUsedMemory;

            m_TotalCommitedReserved[column] = snapshotTargetMemoryStats.TotalReservedMemory + m_Reserved[(int)BreakdownOrder.ManagedDomain][column] + m_Reserved[(int)BreakdownOrder.ExecutableAndDlls][column];
            m_TotalCommitedUsed[column] = snapshotTargetMemoryStats.TotalUsedMemory + m_Used[(int)BreakdownOrder.ManagedDomain][column] + m_Used[(int)BreakdownOrder.ExecutableAndDlls][column];
        }

        void SetBAndDiffVisibility(bool visibility)
        {
            m_HighLevelBreakdown.SetBAndDiffVisibility(visibility);
            m_CommittedTrackingStatusBreakdown.SetBAndDiffVisibility(visibility);
            m_ManagedBreakdown.SetBAndDiffVisibility(visibility);
            m_AllocationBreakdown.SetBAndDiffVisibility(visibility);
        }
    }
}

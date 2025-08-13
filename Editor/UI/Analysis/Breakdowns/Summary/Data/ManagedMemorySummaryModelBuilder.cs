using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEngine;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Managed VM state for memory usage breakdown table view model builder
    /// </summary>
    internal class ManagedMemorySummaryModelBuilder : IMemorySummaryModelBuilder<MemorySummaryModel>
    {
        CachedSnapshot m_SnapshotA;
        CachedSnapshot m_SnapshotB;

        public ManagedMemorySummaryModelBuilder(CachedSnapshot snapshotA, CachedSnapshot snapshotB)
        {
            m_SnapshotA = snapshotA;
            m_SnapshotB = snapshotB;
        }

        public MemorySummaryModel Build()
        {
            Summary a, b;
            BuildSummary(m_SnapshotA, out a);
            if (m_SnapshotB != null)
                BuildSummary(m_SnapshotB, out b);
            else
                b = new Summary();

            bool compareMode = m_SnapshotB != null;
            return new MemorySummaryModel(
                SummaryTextContent.kManagedMemoryTitle,
                SummaryTextContent.kManagedMemoryDescription,
                compareMode,
                a.Total.Committed,
                b.Total.Committed,
                new List<MemorySummaryModel.Row>() {
                    new MemorySummaryModel.Row(SummaryTextContent.kManagedMemoryCategoryVM, a.VM, b.VM, "virtual-machine",
                    TextContent.ManagedDomainDescription +
                    (m_SnapshotB != null ? string.Format(TextContent.ManagedDomainDescriptionStaticFieldAddendumDiff, EditorUtility.FormatBytes(a.VMStaticFields), EditorUtility.FormatBytes(b.VMStaticFields))
                    : string.Format(TextContent.ManagedDomainDescriptionStaticFieldAddendumOneSnapshot, EditorUtility.FormatBytes(a.VMStaticFields)))
                    , null),
                    new MemorySummaryModel.Row(SummaryTextContent.kManagedMemoryCategoryObjects, a.Objects, b.Objects, "objects", TextContent.ManagedObjectsDescription, null),
                    new MemorySummaryModel.Row(SummaryTextContent.kManagedMemoryCategoryFreeHeap, a.EmptyHeapSpace, b.EmptyHeapSpace, "free-in-active-heap-section", TextContent.EmptyActiveHeapDescription, null),
                },
                null);
        }

        void BuildSummary(CachedSnapshot cs, out Summary res)
        {
            CalculateTotal(cs, out res);

            // Add VM root.
            MemorySize vmRootSize;
            if (cs.NativeRootReferences.VMRootReferenceIndex.Valid)
            {
                // use root reference data with resident information as build for the ProcessedNativeRoots if the a VMRootReferenceIndex was found for the snapshot
                vmRootSize = cs.ProcessedNativeRoots.Data[cs.NativeRootReferences.VMRootReferenceIndex.Index].AccumulatedRootSizes.NativeSize;
            }
            else
            {
                var vmRootSizeCommitted = cs.NativeRootReferences.AccumulatedSizeOfVMRoot;
                vmRootSize = new MemorySize(vmRootSizeCommitted, 0);
            }
            res.VM += vmRootSize;
            res.Total += vmRootSize;
        }

        static void CalculateTotal(CachedSnapshot cs, out Summary res)
        {
            var summary = new Summary();
            cs.EntriesMemoryMap.ForEachFlatWithResidentSize((_, address, size, residentSize, source) =>
            {
                switch (source.Id)
                {
                    case SourceIndex.SourceId.ManagedHeapSection:
                    {
                        var memSize = new MemorySize(size, residentSize);
                        summary.Total += memSize;
                        var sectionType = cs.ManagedHeapSections.SectionType[source.Index];
                        switch (sectionType)
                        {
                            case MemorySectionType.VirtualMachine:
                                summary.VM += memSize;
                                break;
                            case MemorySectionType.GarbageCollector:
                                summary.EmptyHeapSpace += memSize;
                                break;
                            default:
                                Debug.Assert(false, $"Unknown managed memory section type ({sectionType}), plese report a bug.");
                                break;
                        }
                        return;
                    }
                    case SourceIndex.SourceId.ManagedObject:
                    {
                        var memSize = new MemorySize(size, residentSize);
                        summary.Total += memSize;
                        summary.Objects += memSize;
                        return;
                    }
                    default:
                        return;
                }
            });
            summary.VMStaticFields = 0;
            for (long i = 0; i < cs.TypeDescriptions.Count; i++)
            {
                summary.VMStaticFields += cs.TypeDescriptions.StaticFieldBytes.Count(i);
            }

            res = summary;
        }

        struct Summary
        {
            public MemorySize Total;

            public MemorySize VM;
            public long VMStaticFields;
            public MemorySize Objects;
            public MemorySize EmptyHeapSpace;
        }
    }
}

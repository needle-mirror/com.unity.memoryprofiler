using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Managed VM state for memory usage breakdown table view model builder
    /// </summary>
    internal class ManagedMemoryBreakdownModelBuilder : IMemoryBreakdownModelBuilder<MemoryBreakdownModel>
    {
        CachedSnapshot m_SnapshotA;
        CachedSnapshot m_SnapshotB;

        public ManagedMemoryBreakdownModelBuilder(CachedSnapshot snapshotA, CachedSnapshot snapshotB)
        {
            m_SnapshotA = snapshotA;
            m_SnapshotB = snapshotB;
        }

        public MemoryBreakdownModel Build()
        {
            Summary a, b;
            CalculateSummary(m_SnapshotA, out a);
            if (m_SnapshotB != null)
                CalculateSummary(m_SnapshotB, out b);
            else
                b = new Summary();

            bool compareMode = m_SnapshotB != null;
            return new MemoryBreakdownModel(
                "Managed Memory",
                compareMode,
                a.Total,
                b.Total,
                new List<MemoryBreakdownModel.Row>() {
                    new MemoryBreakdownModel.Row("Virtual Machine", a.ScriptVM, 0, b.ScriptVM, 0, "virtual-machine", TextContent.ManagedDomainDescription, null),
                    new MemoryBreakdownModel.Row("Objects", a.Objects, 0, b.Objects, 0, "objects", TextContent.ManagedObjectsDescription, null),
                    new MemoryBreakdownModel.Row("Empty Active Heap Space", a.EmptyActiveHeapSpace, 0, b.EmptyActiveHeapSpace, 0, "free-in-active-heap-section", TextContent.EmptyActiveHeapDescription, null),
                    new MemoryBreakdownModel.Row("Empty Fragmented Heap Space", a.EmptyFragmentedHeapSpace, 0, b.EmptyFragmentedHeapSpace, 0, "lost-to-fragmentation", TextContent.EmptyFragmentedHeapDescription, null)
                });
        }

        void CalculateSummary(CachedSnapshot cs, out Summary res)
        {
            res = new Summary();

            // We add `ScriptingNativeRuntime` as IL2CPP reports memory to
            // memory manager under that label
            ulong scriptingNativeTracked = cs.NativeMemoryLabels?.GetLabelSize("ScriptingNativeRuntime") ?? 0;
            res.ScriptVM = cs.ManagedHeapSections.VirtualMachineMemoryReserved + scriptingNativeTracked;

            res.Objects = cs.CrawledData.ManagedObjectMemoryUsage;
            res.EmptyActiveHeapSpace = cs.CrawledData.ActiveHeapMemoryEmptySpace;

            // We assume that anything that isn't reported as free or used
            // is lost due to fragmentation (?)
            res.EmptyFragmentedHeapSpace = cs.ManagedHeapSections.ManagedHeapMemoryReserved
                - res.Objects
                - res.EmptyActiveHeapSpace;

            res.Total = cs.ManagedHeapSections.ManagedHeapMemoryReserved
                + res.ScriptVM
                + cs.ManagedStacks.StackMemoryReserved;
        }

        struct Summary
        {
            public ulong Total;

            public ulong ScriptVM;
            public ulong Objects;
            public ulong EmptyActiveHeapSpace;
            public ulong EmptyFragmentedHeapSpace;
        }
    }
}

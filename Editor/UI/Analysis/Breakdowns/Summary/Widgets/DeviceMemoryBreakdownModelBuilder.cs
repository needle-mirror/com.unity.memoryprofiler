using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Device (physical) memory usage status and warning levels.
    /// </summary>
    internal class DeviceMemoryBreakdownModelBuilder : IMemoryBreakdownModelBuilder<DeviceMemoryBreakdownModel>
    {
        CachedSnapshot m_SnapshotA;
        CachedSnapshot m_SnapshotB;

        public DeviceMemoryBreakdownModelBuilder(CachedSnapshot snapshotA, CachedSnapshot snapshotB)
        {
            m_SnapshotA = snapshotA;
            m_SnapshotB = snapshotB;
        }

        public DeviceMemoryBreakdownModel Build()
        {
            DeviceMemoryBreakdownModel.State a, b;
            CalculateTotals(m_SnapshotA, out a);
            if (m_SnapshotB != null)
                CalculateTotals(m_SnapshotB, out b);
            else
                b = new DeviceMemoryBreakdownModel.State();

            bool compareMode = m_SnapshotB != null;
            return new DeviceMemoryBreakdownModel(
                "Memory usage on device",
                compareMode,
                a,
                b,
                new List<MemoryBreakdownModel.Row>() {
                    new MemoryBreakdownModel.Row("Total Resident", a.Resident, 0, b.Resident, 0, "resident", TextContent.ResidentMemoryDescription, null),
                });
        }

        private void CalculateTotals(CachedSnapshot cs, out DeviceMemoryBreakdownModel.State summary)
        {
            summary = new DeviceMemoryBreakdownModel.State();

            // Calculates total values based on system regions for the newer captures
            // For the old captures use supplied pre-calculated values
            if (cs.HasSystemMemoryRegionsInfo && cs.MetaData.TargetMemoryStats.HasValue)
            {
                // Calculate total committed and resident from system regions
                summary.Resident = 0;
                for (int i = 0; i < cs.SystemMemoryRegions.Count; i++)
                {
                    var residentSize = cs.SystemMemoryRegions.RegionResident[i];

                    summary.Resident += residentSize;
                }
            }

            if (cs.MetaData.TargetInfo.HasValue)
            {
                // TODO: Change this when memory levels becomes available in the snapshot
                summary.MaximumAvailable = cs.MetaData.TargetInfo.Value.TotalPhysicalMemory;
                summary.WarningLevel = summary.MaximumAvailable;
                summary.CriticalLevel = summary.MaximumAvailable;
            }
        }
    }
}

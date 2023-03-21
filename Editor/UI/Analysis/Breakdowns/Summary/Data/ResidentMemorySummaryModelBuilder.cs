using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Device (physical) memory usage status and warning levels.
    /// </summary>
    internal class ResidentMemorySummaryModelBuilder : IMemorySummaryModelBuilder<MemorySummaryModel>
    {
        const string k_WarningMessageTemplate = "On {0}, all memory that is Allocated is also Resident on device";
        const string k_WarningMessageSingle = "this platform";
        const string k_WarningMessageComparePlatformA = "Platform A";
        const string k_WarningMessageComparePlatformB = "Platform B";
        const string k_WarningMessageCompareBothPlatforms = "these platforms";

        CachedSnapshot m_SnapshotA;
        CachedSnapshot m_SnapshotB;

        public ResidentMemorySummaryModelBuilder(CachedSnapshot snapshotA, CachedSnapshot snapshotB)
        {
            m_SnapshotA = snapshotA;
            m_SnapshotB = snapshotB;
        }

        public MemorySummaryModel Build()
        {
            MemorySize a, b;
            CalculateTotals(m_SnapshotA, out a);
            if (m_SnapshotB != null)
                CalculateTotals(m_SnapshotB, out b);
            else
                b = new MemorySize();

            bool compareMode = m_SnapshotB != null;
            return new MemorySummaryModel(
                SummaryTextContent.kResidentMemoryTitle,
                SummaryTextContent.kResidentMemoryDescription,
                compareMode,
                a.Committed,
                b.Committed,
                new List<MemorySummaryModel.Row>() {
                    new MemorySummaryModel.Row(SummaryTextContent.kResidentMemoryCategoryResident, a, b, "resident", TextContent.ResidentMemoryDescription, null),
                },
                MakeResidentMemoryWarning());
        }

        private void CalculateTotals(CachedSnapshot cs, out MemorySize total)
        {
            var _total = new MemorySize();

            // For the newer captures calculates total values based on system regions and resident pages
            // For the old captures use system regions only, which might produce slightly less accurate
            // values.
            if (cs.HasSystemMemoryRegionsInfo && cs.HasSystemMemoryResidentPages)
            {
                // Calculate total committed and resident from system regions
                cs.EntriesMemoryMap.ForEachFlatWithResidentSize((_, address, size, residentSize, source) =>
                {
                    _total += new MemorySize(size, residentSize);
                });
            }
            else if (cs.HasSystemMemoryRegionsInfo)
            {
                for (int i = 0; i < cs.SystemMemoryRegions.Count; i++)
                    _total += new MemorySize(cs.SystemMemoryRegions.RegionSize[i], cs.SystemMemoryRegions.RegionResident[i]);
            }

            total = _total;
        }

        public string MakeResidentMemoryWarning()
        {
            if (m_SnapshotA == null)
                return string.Empty;

            // Single snapshot mode
            bool warnPlatformA = m_SnapshotA.MetaData.TargetInfo.HasValue && PlatformsHelper.IsResidentMemoryBlacklistedPlatform(m_SnapshotA.MetaData.TargetInfo.Value.RuntimePlatform);
            if (m_SnapshotB == null)
            {
                if (warnPlatformA)
                    return string.Format(k_WarningMessageTemplate, k_WarningMessageSingle);
                else
                    return string.Empty;
            }

            // Compare mode
            string platformText;
            bool warnPlatformB = m_SnapshotB.MetaData.TargetInfo.HasValue && PlatformsHelper.IsResidentMemoryBlacklistedPlatform(m_SnapshotB.MetaData.TargetInfo.Value.RuntimePlatform);
            if (warnPlatformA && !warnPlatformB)
                platformText = k_WarningMessageComparePlatformA;
            else if (!warnPlatformA && warnPlatformB)
                platformText = k_WarningMessageComparePlatformB;
            else if (warnPlatformA && warnPlatformB)
                platformText = k_WarningMessageCompareBothPlatforms;
            else
                return string.Empty;

            return string.Format(k_WarningMessageTemplate, platformText);
        }
    }
}

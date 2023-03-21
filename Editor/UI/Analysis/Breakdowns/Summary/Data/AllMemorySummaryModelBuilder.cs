using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEngine;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// All process memory usage as seen by OS broken down
    /// into a set of pre-defined high-level categories.
    /// </summary>
    internal class AllMemorySummaryModelBuilder : IMemorySummaryModelBuilder<MemorySummaryModel>
    {
        const string kPlatformIdAndroid = "dalvik";
        static readonly Dictionary<string, (string name, string descr)> k_CategoryPlatformSpecific = new Dictionary<string, (string, string)>() {
            { kPlatformIdAndroid, (SummaryTextContent.kAllMemoryCategoryAndroid, SummaryTextContent.kAllMemoryCategoryDescriptionAndroid) }
        };

        CachedSnapshot m_SnapshotA;
        CachedSnapshot m_SnapshotB;

        public AllMemorySummaryModelBuilder(CachedSnapshot snapshotA, CachedSnapshot snapshotB)
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

            // Add fixed categories
            var rows = new List<MemorySummaryModel.Row>() {
                    new MemorySummaryModel.Row(SummaryTextContent.kAllMemoryCategoryNative, a.Native, b.Native, "native", TextContent.NativeDescription, null) { CategoryId = IAnalysisViewSelectable.Category.Native },
                    new MemorySummaryModel.Row(SummaryTextContent.kAllMemoryCategoryManaged, a.Managed, b.Managed, "managed", TextContent.ManagedDescription, null) { CategoryId = IAnalysisViewSelectable.Category.Managed },
                    new MemorySummaryModel.Row(SummaryTextContent.kAllMemoryCategoryMappedFiles, a.ExecutablesAndMapped, b.ExecutablesAndMapped, "executables", TextContent.ExecutablesAndMappedDescription, null)  { CategoryId = IAnalysisViewSelectable.Category.ExecutablesAndMapped },
                    new MemorySummaryModel.Row(SummaryTextContent.kAllMemoryCategoryGraphics, a.GraphicsAndDrivers, b.GraphicsAndDrivers, "gfx", TextContent.GraphicsEstimatedDescription, null)  { CategoryId = IAnalysisViewSelectable.Category.Graphics, SortPriority = MemorySummaryModel.RowSortPriority.Low, ResidentSizeUnavailable = true },
                    new MemorySummaryModel.Row(SummaryTextContent.kAllMemoryCategoryUntrackedEstimated, a.Untracked, b.Untracked, "other", TextContent.UntrackedEstimatedDescription, DocumentationUrls.UntrackedMemoryDocumentation)  { CategoryId = IAnalysisViewSelectable.Category.Unknown, SortPriority = MemorySummaryModel.RowSortPriority.ShowLast, ResidentSizeUnavailable = true },
                };

            // Add platform-specific categories
            // Merge two platform specific containers into table rows
            if ((a.PlatformSpecific != null) || (b.PlatformSpecific != null))
            {
                var keysA = a.PlatformSpecific?.Keys.ToArray() ?? new string[0];
                var keysB = b.PlatformSpecific?.Keys.ToArray() ?? new string[0];
                var keys = keysA.Union(keysB);
                foreach (var key in keys)
                {
                    MemorySize valueA = new MemorySize(), valueB = new MemorySize();
                    a.PlatformSpecific?.TryGetValue(key, out valueA);
                    b.PlatformSpecific?.TryGetValue(key, out valueB);

                    // Don't show zero-sized sections
                    if ((valueA.Committed == 0) && (valueB.Committed == 0))
                        continue;

                    k_CategoryPlatformSpecific.TryGetValue(key, out var info);
                    rows.Add(new MemorySummaryModel.Row(info.name, valueA, valueB, key, info.descr, null));
                }
            }

            bool compareMode = m_SnapshotB != null;
            return new MemorySummaryModel(
                SummaryTextContent.kAllMemoryTitle,
                HasResidentMemory() ? SummaryTextContent.kAllMemoryDescriptionWithResident : SummaryTextContent.kAllMemoryDescription,
                compareMode,
                a.Total.Committed,
                b.Total.Committed,
                rows,
                null
            );
        }

        void BuildSummary(CachedSnapshot cs, out Summary summary)
        {
            // Calculate totals based on known objects
            CalculateTotals(cs, out summary);

            var memoryStats = cs.MetaData.TargetMemoryStats;
            if (memoryStats.HasValue)
            {
                // [Legacy] If we don't have SystemMemoryRegionsInfo, take total value from legacy memory stats
                // Nb! If you change this, change similar code in AllTrackedMemoryModelBuilder / UnityObjectsModelBuilder / AllMemorySummaryModelBuilder
                if (!cs.HasSystemMemoryRegionsInfo && (memoryStats.Value.TotalVirtualMemory > 0))
                {
                    summary.Untracked = new MemorySize(memoryStats.Value.TotalVirtualMemory - summary.Total.Committed, 0);
                    summary.Total = new MemorySize(memoryStats.Value.TotalVirtualMemory, 0);
                }

                // System regions report less graphics memory than we have estimated through
                // all known graphics resources. In that case we "reassign" untracked category
                // to graphics category
                if (summary.GraphicsAndDrivers.Committed < memoryStats.Value.GraphicsUsedMemory)
                {
                    // We can't increase graphics memory for more than untracked
                    var delta = Math.Min(memoryStats.Value.GraphicsUsedMemory - summary.GraphicsAndDrivers.Committed, summary.Untracked.Committed);

                    summary.Untracked = new MemorySize(summary.Untracked.Committed - delta, 0);
                    summary.GraphicsAndDrivers = new MemorySize(summary.GraphicsAndDrivers.Committed + delta, 0);
                    summary.EstimatedGraphicsAndDrivers = true;
                }
            }

            // Add Mono or IL2CPP VM allocations
            // Mono VM size is a sum of all reported VM allocations plus scripting memory label overheads
            // IL2CPP VM size comes entirely from the scripting memory label
            ulong scriptingNativeTracked = cs.NativeMemoryLabels?.GetLabelSize("ScriptingNativeRuntime") ?? 0;
            ReassignMemoryToAnotherCategory(ref summary.Managed, ref summary.Native, scriptingNativeTracked);
        }

        bool HasResidentMemory()
        {
            return m_SnapshotA.HasSystemMemoryResidentPages || (m_SnapshotB?.HasSystemMemoryResidentPages ?? false);
        }

        static void CalculateTotals(CachedSnapshot cs, out Summary summary)
        {
            var _summary = new Summary();

            var data = cs.EntriesMemoryMap.Data;
            cs.EntriesMemoryMap.ForEachFlatWithResidentSize((_, address, size, residentSize, source) =>
            {
                var type = cs.EntriesMemoryMap.GetPointType(source);

                var memorySize = new MemorySize(size, residentSize);

                _summary.Total += memorySize;
                switch (type)
                {
                    case EntriesMemoryMapCache.PointType.Native:
                    case EntriesMemoryMapCache.PointType.NativeReserved:
                        _summary.Native += memorySize;
                        break;

                    case EntriesMemoryMapCache.PointType.Managed:
                    case EntriesMemoryMapCache.PointType.ManagedReserved:
                        _summary.Managed += memorySize;
                        break;

                    case EntriesMemoryMapCache.PointType.Mapped:
                        _summary.ExecutablesAndMapped += memorySize;
                        break;

                    case EntriesMemoryMapCache.PointType.Device:
                        _summary.GraphicsAndDrivers += memorySize;
                        break;

                    case EntriesMemoryMapCache.PointType.Shared:
                    case EntriesMemoryMapCache.PointType.Untracked:
                        _summary.Untracked += memorySize;
                        break;

                    case EntriesMemoryMapCache.PointType.AndroidRuntime:
                    {
                        if (_summary.PlatformSpecific == null)
                            _summary.PlatformSpecific = new Dictionary<string, MemorySize>();
                        if (_summary.PlatformSpecific.TryGetValue(kPlatformIdAndroid, out var value))
                            _summary.PlatformSpecific[kPlatformIdAndroid] = value + memorySize;
                        else
                            _summary.PlatformSpecific[kPlatformIdAndroid] = memorySize;
                        break;
                    }

                    default:
                        Debug.Assert(false, "Unknown point type, please report a bug");
                        break;
                }
            });

            summary = _summary;
        }

        static void ReassignMemoryToAnotherCategory(ref MemorySize target, ref MemorySize source, ulong size)
        {
            var committedDelta = Math.Min(source.Committed, size);
            if (committedDelta <= 0)
                return;

            // As we don't know how resident memory is spread, we reassign proportionally
            var residentDelta = source.Resident * committedDelta / source.Committed;

            var deltaSize = new MemorySize(committedDelta, residentDelta);
            source -= deltaSize;
            target += deltaSize;
        }

        struct Summary
        {
            // Total
            public MemorySize Total;

            // Breakdown
            public MemorySize Native;
            public MemorySize Managed;
            public MemorySize ExecutablesAndMapped;
            public MemorySize GraphicsAndDrivers;
            public MemorySize Untracked;

            public Dictionary<string, MemorySize> PlatformSpecific;

            // True when platform does not provide graphics device regions information and we use estimated value in the summary view.
            public bool EstimatedGraphicsAndDrivers;
        }
    }
}

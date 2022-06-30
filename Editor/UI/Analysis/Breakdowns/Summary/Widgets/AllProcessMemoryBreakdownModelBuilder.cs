using System.Collections.Generic;
using System.Linq;
using Unity.MemoryProfiler.Editor.UIContentData;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// All process memory usage as seen by OS broken down
    /// into a set of pre-defined high-level categories.
    /// </summary>
    internal class AllProcessMemoryBreakdownModelBuilder : IMemoryBreakdownModelBuilder<MemoryBreakdownModel>
    {
        const string k_CategoryManaged = "Managed";
        const string k_CategoryDrivers = "Graphics";
        const string k_CategoryAudio = "Audio";
        const string k_CategoryUnityOther = "Native";
        const string k_CategoryUnityProfiler = "Profiler";
        const string k_CategoryMappedFiles = "Executables & Mapped";
        const string k_CategoryUnknown = "Unknown";
        static readonly Dictionary<string, (string name, string descr)> k_CategoryPlatformSpecific = new Dictionary<string, (string, string)>() {
            { "dalvik", ("Android Runtime", "Android Runtime (ART) is the managed runtime used by applications and some system services on Android." +
                "ART as the runtime executes the Dalvik Executable format and Dex bytecode specification." +
                "\n\nTo profile Android Runtime use platform native tools such as Android Studio.") }
        };

        CachedSnapshot m_SnapshotA;
        CachedSnapshot m_SnapshotB;

        public AllProcessMemoryBreakdownModelBuilder(CachedSnapshot snapshotA, CachedSnapshot snapshotB)
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

            // Add fixed categories
            var rows = new List<MemoryBreakdownModel.Row>() {
                    new MemoryBreakdownModel.Row(k_CategoryManaged, a.Managed, a.ManagedUsed, b.Managed, b.ManagedUsed, "managed", TextContent.ManagedDescription, null),
                    new MemoryBreakdownModel.Row(k_CategoryDrivers, a.GraphicsAndDrivers, 0, b.GraphicsAndDrivers, 0, "gfx", TextContent.GraphicsDescription, null),
                    new MemoryBreakdownModel.Row(k_CategoryAudio, a.Audio, 0, b.Audio, 0, "audio", TextContent.AudioDescription, null),
                    new MemoryBreakdownModel.Row(k_CategoryUnityOther, a.NativeOther, a.NativeOtherUsed, b.NativeOther, b.NativeOtherUsed, "unity-other", TextContent.NativeDescription, null),
                    new MemoryBreakdownModel.Row(k_CategoryUnityProfiler, a.Profiler, a.ProfilerUsed, b.Profiler, b.ProfilerUsed, "profiler", TextContent.ProfilerDescription, null),
                    new MemoryBreakdownModel.Row(k_CategoryMappedFiles, a.ExecutablesAndMapped, 0, b.ExecutablesAndMapped, 0, "executables", TextContent.ExecutablesAndMappedDescription, null),
                    new MemoryBreakdownModel.Row(k_CategoryUnknown, a.Unknown, 0, b.Unknown, 0, "unknown", TextContent.UnknownDescription, DocumentationUrls.UntrackedMemoryDocumentation),
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
                    ulong valueA = 0, valueB = 0;
                    a.PlatformSpecific?.TryGetValue(key, out valueA);
                    b.PlatformSpecific?.TryGetValue(key, out valueB);

                    // Don't show zero-sized sections
                    if ((valueA == 0) && (valueB == 0))
                        continue;

                    k_CategoryPlatformSpecific.TryGetValue(key, out var info);
                    rows.Add(new MemoryBreakdownModel.Row(info.name, valueA, 0, valueB, 0, key, info.descr, null));
                }
            }

            bool compareMode = m_SnapshotB != null;
            return new MemoryBreakdownModel(
                "Total Committed Memory",
                compareMode,
                a.TotalCommitted,
                b.TotalCommitted,
                rows
            );
        }

        private void CalculateSummary(CachedSnapshot cs, out Summary summary)
        {
            summary = new Summary();

            // Extracts information about tracked categories from a snapshot
            if (cs.MetaData.TargetMemoryStats.HasValue)
            {
                summary.Tracked = cs.MetaData.TargetMemoryStats.Value.TotalReservedMemory;
                summary.TrackedUsed = cs.MetaData.TargetMemoryStats.Value.TotalUsedMemory;

                summary.Managed = cs.MetaData.TargetMemoryStats.Value.GcHeapReservedMemory;
                summary.ManagedUsed = cs.MetaData.TargetMemoryStats.Value.GcHeapUsedMemory;

                summary.Audio = cs.MetaData.TargetMemoryStats.Value.AudioUsedMemory;
                summary.GraphicsAndDrivers = cs.MetaData.TargetMemoryStats.Value.GraphicsUsedMemory;

                summary.Profiler = cs.MetaData.TargetMemoryStats.Value.ProfilerReservedMemory;
                summary.ProfilerUsed = cs.MetaData.TargetMemoryStats.Value.ProfilerUsedMemory;

                // For backward compatibility, we need to substract GraphicsAndDrivers
                // as GraphicsAndDrivers might be recalculated from System Regions later
                summary.Tracked -= summary.GraphicsAndDrivers;
                summary.TrackedUsed -= summary.GraphicsAndDrivers;
            }

            // Add Mono or IL2CPP VM allocations
            // Mono VM size is a sum of all reported VM allocations plus scripting memory label overheads
            // IL2CPP VM size comes entirely from the scripting memory label
            ulong scriptingNativeTracked = cs.NativeMemoryLabels?.GetLabelSize("ScriptingNativeRuntime") ?? 0;
            summary.Managed += cs.ManagedHeapSections.VirtualMachineMemoryReserved + scriptingNativeTracked;
            summary.ManagedUsed += cs.ManagedHeapSections.VirtualMachineMemoryReserved + scriptingNativeTracked;

            // Calculates total values based on system regions for the newer captures
            // For the old captures use supplied pre-calculated values
            if (cs.HasSystemMemoryRegionsInfo && cs.SystemMemoryRegions.Count > 0 && cs.MetaData.TargetMemoryStats.HasValue)
            {
                summary.PlatformSpecific = new Dictionary<string, ulong>();
                CalculateTotalsFromSystemRegions(cs, ref summary.TotalCommitted, ref summary.GraphicsAndDrivers, ref summary.ExecutablesAndMapped, ref summary.PlatformSpecific);

                // GraphicsAndDrivers - sum of all driver mapped memory
                // On Windows, we use pre-calculated value if no regions is marked as device type
                if ((summary.GraphicsAndDrivers == 0) && cs.MetaData.TargetMemoryStats.HasValue)
                    summary.GraphicsAndDrivers = cs.MetaData.TargetMemoryStats.Value.GraphicsUsedMemory;
            }
            else if (cs.MetaData.TargetMemoryStats.HasValue)
            {
                summary.TotalCommitted = cs.MetaData.TargetMemoryStats.Value.TotalVirtualMemory;
                summary.GraphicsAndDrivers = cs.MetaData.TargetMemoryStats.Value.GraphicsUsedMemory;
                summary.ExecutablesAndMapped = cs.NativeRootReferences.ExecutableAndDllsReportedValue;

                // Some platforms might not report total committed correctly
                if (summary.TotalCommitted == 0)
                    summary.TotalCommitted = summary.Tracked;
            }

            // Native Other - sum of all tracked memory we don't
            // have an individual category for in the UI.
            summary.NativeOther = summary.Tracked -
                summary.Audio -
                summary.Managed -
                summary.Profiler;
            summary.NativeOtherUsed = summary.TrackedUsed -
                summary.Audio -
                summary.ManagedUsed -
                summary.ProfilerUsed;

            var platformSpecificTotal = summary.PlatformSpecific?.Select(x => x.Value).Aggregate(0UL, (x, acc) => acc + x) ?? 0;

            // Nb! Tracked already includes MemoryManager tracked memory and GC heap
            // Graphics memory was substracted from Tracked earlier
            var totalCategorized = summary.Tracked +
                summary.GraphicsAndDrivers +
                summary.ExecutablesAndMapped +
                platformSpecificTotal;
            summary.Unknown = totalCategorized <= summary.TotalCommitted ? summary.TotalCommitted - totalCategorized : 0;
        }

        private static void CalculateTotalsFromSystemRegions(
            CachedSnapshot cs,
            ref ulong totalCommitted,
            ref ulong graphicsAndDrivers,
            ref ulong executablesAndMapped,
            ref Dictionary<string, ulong> platformSpecific)
        {
            // Calculate total committed and resident from system regions
            totalCommitted = 0;
            for (int i = 0; i < cs.SystemMemoryRegions.Count; i++)
            {
                var size = cs.SystemMemoryRegions.RegionSize[i];
                totalCommitted += size;
            }

            // Calculate graphics & drivers and mapped files from memory summary
            // We need to do cross-section with tracked allocations to eliminate tracked
            // areas from system regions, as some platforms might mistakenly report
            // some areas as mapped while they're allocatet by Unity.
            // For that we do flatted hierarchy scan
            ulong _graphicsAndDrivers = 0;
            ulong _executablesAndMapped = 0;
            var _platformSpecific = new Dictionary<string, ulong>();
            _platformSpecific["dalvik"] = 0;
            cs.MemoryEntriesHierarchy.ForEachFlat((address, size, childrenCount, type, source) =>
            {
                if (source.Id != SourceLink.SourceId.SystemMemoryRegion)
                    return;

                switch (type)
                {
                    case MemoryEntriesHierarchyCache.RegionType.AndroidRuntime:
                        _platformSpecific["dalvik"] += size;
                        break;
                    case MemoryEntriesHierarchyCache.RegionType.Device:
                        _graphicsAndDrivers += size;
                        break;
                    case MemoryEntriesHierarchyCache.RegionType.Mapped:
                        _executablesAndMapped += size;
                        break;
                }
            });

            graphicsAndDrivers = _graphicsAndDrivers;
            executablesAndMapped = _executablesAndMapped;
            platformSpecific = _platformSpecific;
        }

        struct Summary
        {
            // Totals
            public ulong TotalCommitted;

            // Breakdown of TotalCommitted
            public ulong Tracked;
            public ulong ExecutablesAndMapped;
            public ulong GraphicsAndDrivers;
            public ulong Unknown;

            // Breakdown of Tracked
            public ulong Audio;
            public ulong Managed;
            public ulong Profiler;
            public ulong NativeOther;

            // Used doesn't account to totals and only specify
            // amount of used memory in the respective sub-category
            public ulong TrackedUsed;
            public ulong ProfilerUsed;
            public ulong ManagedUsed;
            public ulong NativeOtherUsed;

            public Dictionary<string, ulong> PlatformSpecific;
        }
    }
}

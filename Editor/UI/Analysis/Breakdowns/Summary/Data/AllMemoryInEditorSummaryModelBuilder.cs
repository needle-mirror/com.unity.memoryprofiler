using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// All process memory usage as seen by OS broken down
    /// into a set of pre-defined high-level categories.
    /// </summary>
    internal class AllMemoryInEditorSummaryModelBuilder : IMemorySummaryModelBuilder<MemorySummaryModel>
    {
        public AllMemoryInEditorSummaryModelBuilder()
        {
        }

        public long Frame { get; set; }

        public MemorySummaryModel Build()
        {
            var total = 0UL;
            var rows = new List<MemorySummaryModel.Row>();
            using (var data = ProfilerDriver.GetRawFrameDataView((int)Frame, 0))
            {
                GetCounterValue(data, "Total Reserved Memory", out var totalTrackedReserved);
                GetCounterValue(data, "Total Used Memory", out var totalTrackedUsed);
                GetCounterValue(data, "Gfx Reserved Memory", out var gfxTracked);
                GetCounterValue(data, "GC Reserved Memory", out var managedTrackedReserved);
                GetCounterValue(data, "GC Used Memory", out var managedTrackedUsed);

                // Older editors might not have the counter, in that case use total tracked
                if (!GetCounterValue(data, "System Used Memory", out total))
                    total = totalTrackedReserved;

                // For platforms which don't report total committed, it might be too small
                if (total < totalTrackedReserved)
                    total = totalTrackedReserved;

                var nativeReserved = totalTrackedReserved - Math.Min(managedTrackedReserved + gfxTracked, totalTrackedReserved);
                var nativeUsed = totalTrackedUsed - Math.Min(managedTrackedUsed + gfxTracked, totalTrackedUsed);

                var untracked = total - totalTrackedReserved;

                rows = new List<MemorySummaryModel.Row>() {
                        new MemorySummaryModel.Row(SummaryTextContent.kAllMemoryCategoryNative, nativeReserved, nativeUsed, 0, 0, "native", TextContent.NativeDescription, null),
                        new MemorySummaryModel.Row(SummaryTextContent.kAllMemoryCategoryManaged, managedTrackedReserved, managedTrackedUsed, 0, 0, "managed", TextContent.ManagedDescription, null),
                        new MemorySummaryModel.Row(SummaryTextContent.kAllMemoryCategoryGraphics, gfxTracked, 0, 0, 0, "gfx", TextContent.GraphicsEstimatedDescription, null),
                        new MemorySummaryModel.Row(SummaryTextContent.kAllMemoryCategoryUntrackedEstimated, untracked, 0, 0, 0, "other", TextContent.UntrackedEstimatedDescription, DocumentationUrls.UntrackedMemoryDocumentation),
                    };
            }

            return new MemorySummaryModel(
                SummaryTextContent.kAllMemoryTitle,
                String.Empty,
                false,
                total,
                0,
                rows,
                null
            );
        }

        private bool GetCounterValue(RawFrameDataView data, string counterName, out ulong value)
        {
            if (!data.valid)
            {
                value = 0;
                return false;
            }

            var markerId = data.GetMarkerId(counterName);
            value = (ulong)data.GetCounterValueAsLong(markerId);
            return true;
        }
    }
}

using System.Collections.Generic;
using UnityEditorInternal;
using Unity.MemoryProfiler.Editor.UIContentData;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Device (physical) memory usage status and warning levels.
    /// </summary>
    internal class ResidentMemoryInEditorSummaryModelBuilder : BaseProfilerModuleSummaryBuilder<MemorySummaryModel>
    {
        public bool FrameHasTotalCommitedMemoryCounter { get; private set; }

        List<MemorySummaryModel.Row> rows = new List<MemorySummaryModel.Row>();
        public override MemorySummaryModel Build()
        {
            var committed = 0UL;
            var resident = 0UL;
            using (var data = ProfilerDriver.GetRawFrameDataView((int)Frame, 0))
            {
                GetCounterValue(data, "Total Reserved Memory", out var totalTrackedReserved);

                // Use system reported value as total value
                // Older editors might not have the counter, in that case use total tracked
                FrameHasTotalCommitedMemoryCounter = GetCounterValue(data, "App Committed Memory", out committed);
                if (!FrameHasTotalCommitedMemoryCounter)
                    committed = totalTrackedReserved;

                // For platforms which don't report total committed, it might be too small
                if (committed < totalTrackedReserved)
                    committed = totalTrackedReserved;

                if (!GetCounterValue(data, "App Resident Memory", out resident))
                    if (!GetCounterValue(data, "System Used Memory", out resident))
                        GetCounterValue(data, "Total Used memory", out resident);
            }
            rows.Clear();
            rows.Add(new MemorySummaryModel.Row(SummaryTextContent.kResidentMemoryCategoryResident, committed, resident, 0, 0, "resident", TextContent.ResidentMemoryDescription, null));
            return new MemorySummaryModel(
                SummaryTextContent.kResidentMemoryTitle,
                SummaryTextContent.kResidentMemoryDescription,
                false,
                committed,
                0,
                rows,
                null);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Profiling;
using UnityEditorInternal;
using Unity.MemoryProfiler.Editor.UIContentData;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Device (physical) memory usage status and warning levels.
    /// </summary>
    internal class ResidentMemoryInEditorSummaryModelBuilder : IMemorySummaryModelBuilder<MemorySummaryModel>
    {
        public ResidentMemoryInEditorSummaryModelBuilder()
        {
        }

        public long Frame { get; set; }

        public MemorySummaryModel Build()
        {
            var committed = 0UL;
            var resident = 0UL;
            using (var data = ProfilerDriver.GetRawFrameDataView((int)Frame, 0))
            {
                GetCounterValue(data, "Total Reserved Memory", out var totalTrackedReserved);

                // Use system reported value as total value
                // Older editors might not have the counter, in that case use total tracked
                if (!GetCounterValue(data, "System Used Memory", out committed))
                    committed = totalTrackedReserved;

                // For platforms which don't report total committed, it might be too small
                if (committed < totalTrackedReserved)
                    committed = totalTrackedReserved;

                if (!GetCounterValue(data, "System Used Memory", out resident))
                    GetCounterValue(data, "Total Used memory", out resident);
            }

            return new MemorySummaryModel(
                SummaryTextContent.kResidentMemoryTitle,
                SummaryTextContent.kResidentMemoryDescription,
                false,
                committed,
                0,
                new List<MemorySummaryModel.Row>() {
                    new MemorySummaryModel.Row(SummaryTextContent.kResidentMemoryCategoryResident, committed, resident, 0, 0, "resident", TextContent.ResidentMemoryDescription, null),
                },
                null);
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

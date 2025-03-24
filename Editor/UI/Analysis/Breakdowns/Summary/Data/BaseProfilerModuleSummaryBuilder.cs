using System.Collections.Generic;
using UnityEditor.Profiling;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal abstract class BaseProfilerModuleSummaryBuilder<T> : IMemoryProfilerModuleSummaryModelBuilder<T> where T : MemorySummaryModel
    {
        public BaseProfilerModuleSummaryBuilder()
        {
        }

        public long Frame { get; set; }

        public abstract T Build();

        protected void AddCounter(List<MemorySummaryModel.Row> rows, string name, RawFrameDataView data, string counterNameBytes, string counterNameCount, string rowDescription)
        {
            GetCounterValue(data, counterNameBytes, out var bytes);
            var count = MemorySummaryModel.Row.InvalidCount;
            if (!string.IsNullOrEmpty(counterNameCount))
                GetCounterValueLong(data, counterNameCount, out count);
            rows.Add(new MemorySummaryModel.Row(name, bytes, 0, 0, 0, $"grp-{rows.Count + 1}", rowDescription, null, (long)count));
        }

        protected bool GetCounterValue(RawFrameDataView data, string counterName, out ulong value)
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

        protected bool GetCounterValueLong(RawFrameDataView data, string counterName, out long value)
        {
            if (!data.valid)
            {
                value = 0;
                return false;
            }

            var markerId = data.GetMarkerId(counterName);
            value = data.GetCounterValueAsLong(markerId);
            return true;
        }
    }
}

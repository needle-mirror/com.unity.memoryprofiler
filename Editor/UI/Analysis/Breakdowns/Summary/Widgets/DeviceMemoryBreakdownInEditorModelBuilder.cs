using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Profiling;
using UnityEditorInternal;
using Unity.MemoryProfiler.Editor.UIContentData;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal interface IMemoryBreakdownModelBuilder<T> where T : MemoryBreakdownModel

    {
        T Build();
    }

    /// <summary>
    /// Device (physical) memory usage status and warning levels.
    /// </summary>
    internal class DeviceMemoryInEditorWidgetModelBuilder : IMemoryBreakdownModelBuilder<DeviceMemoryBreakdownModel>
    {
        public DeviceMemoryInEditorWidgetModelBuilder()
        {
        }

        public long Frame { get; set; }

        public DeviceMemoryBreakdownModel Build()
        {
            DeviceMemoryBreakdownModel.State state = new DeviceMemoryBreakdownModel.State();
            // TODO: Change when connected device memory size will become availalble
            state.MaximumAvailable = ((ulong)SystemInfo.systemMemorySize) * 1024 * 1024;
            state.WarningLevel = state.MaximumAvailable;
            state.CriticalLevel = state.MaximumAvailable;
            using (var data = ProfilerDriver.GetRawFrameDataView((int)Frame, 0))
            {
                if (!GetCounterValue(data, "System Used Memory", out state.Resident))
                    GetCounterValue(data, "Total Used memory", out state.Resident);
            }

            return new DeviceMemoryBreakdownModel(
                "Memory usage on device",
                false,
                state,
                new DeviceMemoryBreakdownModel.State(),
                new List<MemoryBreakdownModel.Row>() {
                    new MemoryBreakdownModel.Row("Total Resident", state.Resident, 0, 0, 0, "resident", TextContent.ResidentMemoryDescription, null),
                });
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

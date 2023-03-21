using System.Collections.Generic;
using System.Linq;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// In-Editor realtime Unity objects memory usage summary model builder for table view controller
    /// Collects data from counters and builds a model representation.
    /// </summary>
    internal class UnityObjectsMemoryInEditorSummaryModelBuilder : IMemorySummaryModelBuilder<MemorySummaryModel>
    {
        public UnityObjectsMemoryInEditorSummaryModelBuilder()
        {
        }

        public long Frame { get; set; }

        public MemorySummaryModel Build()
        {
            var rows = new List<MemorySummaryModel.Row>();
            using (var data = ProfilerDriver.GetRawFrameDataView((int)Frame, 0))
            {
                AddCounter(rows, "Textures", data, "Texture Memory");
                AddCounter(rows, "Render Textures", data, "Render Textures Bytes");
                AddCounter(rows, "Meshes", data, "Mesh Memory");
                AddCounter(rows, "Materials", data, "Material Memory");
                AddCounter(rows, "Animations", data, "AnimationClip Memory");
                AddCounter(rows, "Audio", data, "AudioClip Memory");
            }

            // Sort
            rows.Sort((l, r) =>
            {
                return -l.BaseSize.Committed.CompareTo(r.BaseSize.Committed);
            });

            // Totals for the breadkown bar
            var total = rows.Aggregate(0UL, (x, acc) => acc.BaseSize.Committed + x);

            return new MemorySummaryModel(
                SummaryTextContent.kUnityObjectsTitle,
                string.Empty,
                false,
                total,
                0,
                rows,
                null);
        }

        void AddCounter(List<MemorySummaryModel.Row> rows, string name, RawFrameDataView data, string counterName)
        {
            GetCounterValue(data, counterName, out var value);
            rows.Add(new MemorySummaryModel.Row(name, value, 0, 0, 0, $"grp-{rows.Count + 1}", TextContent.ManagedObjectsDescription, null));
        }

        bool GetCounterValue(RawFrameDataView data, string counterName, out ulong value)
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

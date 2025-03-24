using System.Collections.Generic;
using System.Linq;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditorInternal;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// In-Editor realtime Unity objects memory usage summary model builder for table view controller
    /// Collects data from counters and builds a model representation.
    /// </summary>
    internal class UnityObjectsMemoryInEditorSummaryModelBuilder : BaseProfilerModuleSummaryBuilder<MemorySummaryModel>
    {
        List<MemorySummaryModel.Row> rows = new List<MemorySummaryModel.Row>();
        public override MemorySummaryModel Build()
        {
            rows.Clear();
            using (var data = ProfilerDriver.GetRawFrameDataView((int)Frame, 0))
            {
                AddCounter(rows, "Textures", data, "Texture Memory", "Texture Count", TextContent.MemoryProfilerModuleUnityObjectUsageDescription);
                AddCounter(rows, "Render Textures", data, "Render Textures Bytes", "Render Textures Count", TextContent.MemoryProfilerModuleUnityObjectUsageDescription);
                AddCounter(rows, "Meshes", data, "Mesh Memory", "Mesh Count", TextContent.MemoryProfilerModuleUnityObjectUsageDescription);
                AddCounter(rows, "Materials", data, "Material Memory", "Material Count", TextContent.MemoryProfilerModuleUnityObjectUsageDescription);
                AddCounter(rows, "Animations", data, "AnimationClip Memory", "AnimationClip Count", TextContent.MemoryProfilerModuleUnityObjectUsageDescription);
                AddCounter(rows, "Audio", data, "AudioClip Memory", "AudioClip Count", TextContent.MemoryProfilerModuleUnityObjectUsageDescription);
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
    }
}

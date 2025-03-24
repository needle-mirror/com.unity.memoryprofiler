using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditorInternal;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// All GC.Alloc memory usage broken down by main thread vs others with Editor Only allocations separated out.
    /// </summary>
    internal class GCAllocMemorySummaryModelBuilder : BaseProfilerModuleSummaryBuilder<MemorySummaryModel>
    {
        Stack<(int, ulong)> m_EditorOnlySampleStack = new Stack<(int, ulong)>();

        enum GCAllocType
        {
            Default,
            EditorOnly,
        }

        List<MemorySummaryModel.Row> rows = new List<MemorySummaryModel.Row>();
        public override MemorySummaryModel Build()
        {
            var total = 0UL;
            using (var data = ProfilerDriver.GetRawFrameDataView((int)Frame, 0))
            {
                GetCounterValue(data, "GC Allocated In Frame", out var totalAllocatedInFrame);
                GetCounterValue(data, "GC Allocation In Frame Count", out var totalAllocCountInFrame);

                var gcAllocMarker = data.GetMarkerId("GC.Alloc");
                var gcAllocOnMain = 0UL;
                var gcAllocCountOnMain = 0L;

                var gcAllocOnMainEditorOnly = 0UL;
                var gcAllocCountOnMainEditorOnly = 0L;

                m_EditorOnlySampleStack.Clear();

                GCAllocType gcAllocType = GCAllocType.Default;

                for (int i = 0; i < data.sampleCount; i++)
                {
                    if (data.GetSampleFlags(i).HasFlag(Profiling.LowLevel.MarkerFlags.AvailabilityEditor))
                    {
                        m_EditorOnlySampleStack.Push((i, data.GetSampleStartTimeNs(i) + data.GetSampleTimeNs(i)));
                        gcAllocType = GCAllocType.EditorOnly;
                        continue;
                    }
                    while (m_EditorOnlySampleStack.Count > 0 && data.GetSampleStartTimeNs(i) > m_EditorOnlySampleStack.Peek().Item2)
                    {
                        m_EditorOnlySampleStack.Pop();
                        if (m_EditorOnlySampleStack.Count <= 0)
                            gcAllocType = GCAllocType.Default;
                    }
                    if (data.GetSampleMarkerId(i) == gcAllocMarker)
                    {
                        switch (gcAllocType)
                        {
                            case GCAllocType.Default:
                                gcAllocOnMain += (ulong)data.GetSampleMetadataAsLong(i, 0);
                                ++gcAllocCountOnMain;
                                break;
                            case GCAllocType.EditorOnly:
                                gcAllocOnMainEditorOnly += (ulong)data.GetSampleMetadataAsLong(i, 0);
                                ++gcAllocCountOnMainEditorOnly;
                                break;
                            default:
                                break;
                        }
                    }
                }
                var totalAllocatedInFrameInRemainingThreads = totalAllocatedInFrame - gcAllocOnMain;
                var totalAllocatedCountInFrameInRemainingThreads = (long)totalAllocCountInFrame - gcAllocCountOnMain;

                total = totalAllocatedInFrame;
                rows.Clear();

                rows.Add(new MemorySummaryModel.Row(TextContent.MemoryProfilerModuleGCAllocMainThread, gcAllocOnMain, gcAllocOnMain, 0, 0, "managed", TextContent.MemoryProfilerModuleGCAllocMainThreadDescription, null, gcAllocCountOnMain));
                if (gcAllocCountOnMainEditorOnly > 0)
                    rows.Add(new MemorySummaryModel.Row(TextContent.MemoryProfilerModuleGCAllocMainThreadEditorOnly, gcAllocOnMainEditorOnly, gcAllocOnMainEditorOnly, 0, 0, "native", TextContent.MemoryProfilerModuleGCAllocMainThreadEditorOnlyDescription, null, gcAllocCountOnMainEditorOnly));
                rows.Add(new MemorySummaryModel.Row(TextContent.MemoryProfilerModuleGCAllocOtherThreads, totalAllocatedInFrameInRemainingThreads, totalAllocatedInFrameInRemainingThreads, 0, 0, "gfx", TextContent.MemoryProfilerModuleGCAllocOtherThreadsDescription, null, totalAllocatedCountInFrameInRemainingThreads));
            }

            return new MemorySummaryModel(
                TextContent.MemoryProfilerModuleGCAllocBreakdownName,
                String.Empty,
                false,
                total,
                0,
                rows,
                null
            );
        }
    }
}

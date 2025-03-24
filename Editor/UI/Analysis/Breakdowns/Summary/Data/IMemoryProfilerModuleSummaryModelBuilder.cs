namespace Unity.MemoryProfiler.Editor.UI
{
    internal interface IMemoryProfilerModuleSummaryModelBuilder<T> : IMemorySummaryModelBuilder<T> where T : MemorySummaryModel
    {
        long Frame { get; set; }
    }
}

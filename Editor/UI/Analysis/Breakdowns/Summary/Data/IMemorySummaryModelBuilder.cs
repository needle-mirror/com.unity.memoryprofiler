namespace Unity.MemoryProfiler.Editor.UI
{
    internal interface IMemorySummaryModelBuilder<T> where T : MemorySummaryModel
    {
        T Build();
    }
}

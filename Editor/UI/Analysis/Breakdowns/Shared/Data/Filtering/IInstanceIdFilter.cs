namespace Unity.MemoryProfiler.Editor.UI
{
    interface IInstancIdFilter : ITableFilter<int>
    {
        public CachedSnapshot SourceSnapshot { get; }
    }
}

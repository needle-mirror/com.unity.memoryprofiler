namespace Unity.MemoryProfiler.Editor.UI
{
    interface ITableFilter<T>
    {
        T Value { get; }
        bool Passes(T value, CachedSnapshot snapshot = null);
    }
}

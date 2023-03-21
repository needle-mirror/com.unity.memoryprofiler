#if UNITY_2022_1_OR_NEWER
namespace Unity.MemoryProfiler.Editor.UI
{
    interface ITableFilter<T>
    {
        T Value { get; }
        bool Passes(T value, CachedSnapshot snapshot = null);
    }
}
#endif

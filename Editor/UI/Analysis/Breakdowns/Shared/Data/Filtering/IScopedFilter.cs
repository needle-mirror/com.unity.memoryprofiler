#if UNITY_2022_1_OR_NEWER
using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Scope filters are used for nested structures where the filter could pass for any layer of the structure's hierachy.
    /// If a filter passes for a non leaf scope, deeper scopes don't have to run the test. However, a definitive fail can
    /// only be checked for the leaf nodes. Failing a non-leaf node therefore can't be used to exclude branches early.
    /// </summary>
    /// <typeparam name="TType"></typeparam>
    interface IScopedFilter<TType>
    {
        TType FilterValue { get; }
        bool CurrentScopePasses { get; }
        IFilterScope<TType> OpenScope(TType scopeName, CachedSnapshot cachedSnapshot = null);

        public interface IFilterScope<T> : IDisposable, ITableFilter<T>
        {
            bool ScopePasses { get; }
        }
    }
}
#endif

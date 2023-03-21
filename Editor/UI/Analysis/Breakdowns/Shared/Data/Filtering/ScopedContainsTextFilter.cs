#if UNITY_2022_1_OR_NEWER
using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    class ScopedContainsTextFilter : ScopedFilter<ContainsTextFilter, string>
    {
        ScopedContainsTextFilter(ContainsTextFilter filter) : base(filter) { }

        public static ScopedContainsTextFilter Create(string text)
        {
            return string.IsNullOrEmpty(text) ? null : new ScopedContainsTextFilter(ContainsTextFilter.Create(text));
        }
    }
}
#endif

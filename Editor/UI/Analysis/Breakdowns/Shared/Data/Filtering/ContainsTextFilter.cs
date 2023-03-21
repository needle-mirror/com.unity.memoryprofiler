#if UNITY_2022_1_OR_NEWER
using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    class ContainsTextFilter : ITextFilter
    {
        public string Value { get; }

        ContainsTextFilter(string text)
        {
            Value = text;
        }

        /// <summary>
        /// Cannot create a text filter with a null filter string or an empty filter string.
        /// If this filter should match for any input, use <see cref="MatchesAllTextFilter"/> instead.
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static ContainsTextFilter Create(string text)
        {
            // Cannot create a text filter with a null filter string. Note that string.Empty is allowed, which will pass non-null strings (i.e. 'contains any text').
            if (string.IsNullOrEmpty(text))
                return null;

            return new ContainsTextFilter(text);
        }

        // Test if the provided 'text' passes the filter. Returns true if 'text' is not null and contains the filter's 'Text', according to an ordinal, case-insensitive comparison. Otherwise, returns false.
        public bool Passes(string text, CachedSnapshot cachedSnapshot = null)
        {
            if (text == null)
                return false;

            if (!text.Contains(Value, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }
    }
}
#endif

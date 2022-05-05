#if UNITY_2022_1_OR_NEWER
using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    class ContainsTextFilter : ITextFilter
    {
        string Text { get; }

        ContainsTextFilter(string text)
        {
            Text = text;
        }

        public static ContainsTextFilter Create(string text)
        {
            // Cannot create a text filter with a null filter string. Note that string.Empty is allowed, which will pass non-null strings (i.e. 'contains any text').
            if (text == null)
                return null;

            return new ContainsTextFilter(text);
        }

        // Test if the provided 'text' passes the filter. Returns true if 'text' is not null and contains the filter's 'Text', according to an ordinal, case-insensitive comparison. Otherwise, returns false.
        public bool TextPasses(string text)
        {
            if (text == null)
                return false;

            if (!text.Contains(Text, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }
    }
}
#endif

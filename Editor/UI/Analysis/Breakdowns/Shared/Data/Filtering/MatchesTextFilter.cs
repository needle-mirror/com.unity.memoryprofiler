#if UNITY_2022_1_OR_NEWER
using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    class MatchesTextFilter : ITextFilter
    {
        string Text { get; }

        MatchesTextFilter(string text)
        {
            Text = text;
        }

        public static MatchesTextFilter Create(string text)
        {
            // Cannot create a text filter with a null filter string. Note that string.Empty is allowed, which will pass null or empty strings (i.e. 'matches empty text').
            if (text == null)
                return null;

            return new MatchesTextFilter(text);
        }

        // Test if the provided 'text' passes the filter. Returns true if 'text' is equal to the filter's 'Text', according to an ordinal, case-insensitive comparison. If 'text' is null, returns true if the filter's 'Text' is an empty string. Otherwise, returns false.
        public bool TextPasses(string text)
        {
            if (text == null)
                return string.IsNullOrEmpty(Text);

            return text.Equals(Text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
#endif

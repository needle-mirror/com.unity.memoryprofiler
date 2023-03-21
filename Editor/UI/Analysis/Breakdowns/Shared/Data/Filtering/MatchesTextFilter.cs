#if UNITY_2022_1_OR_NEWER
using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    class MatchesTextFilter : ITextFilter
    {
        readonly string m_Text;
        public string Value => m_Text;

        MatchesTextFilter(string text)
        {
            m_Text = text;
        }


        public static MatchesTextFilter Create(string text)
        {
            // Cannot create a text filter with a null filter string. Note that string.Empty is allowed, which will pass null or empty strings (i.e. 'matches empty text').
            if (text == null)
                return null;

            return new MatchesTextFilter(text);
        }

        /// <summary>
        /// Test if the provided '<paramref name="text"/>' passes the filter.
        /// </summary>
        /// <param name="text"></param>
        /// <returns>
        /// Returns true if '<paramref name="text"/>' is equal to the filter's 'Text', according to an ordinal, case-insensitive comparison.
        /// If '<paramref name="text"/>' is null, returns true if the filter's 'Text' is an empty string.
        /// Otherwise, returns false.</returns>
        public bool Passes(string text, CachedSnapshot cachedSnapshot = null)
        {
            if (text == null)
                return string.IsNullOrEmpty(m_Text);

            return text.Equals(m_Text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
#endif

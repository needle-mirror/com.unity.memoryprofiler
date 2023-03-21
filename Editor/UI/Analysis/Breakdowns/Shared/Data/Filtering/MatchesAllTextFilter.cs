#if UNITY_2022_1_OR_NEWER
using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    class MatchesAllTextFilter : ITextFilter
    {
        public string Value => null;

        MatchesAllTextFilter()
        {
        }

        public static MatchesAllTextFilter Create()
        {
            return new MatchesAllTextFilter();
        }

        /// <summary>
        /// Test if the provided '<paramref name="text"/>' passes the filter.
        /// Spoiler Alert, all texts pass!
        /// </summary>
        /// <param name="text"></param>
        /// <returns>true</returns>
        public bool Passes(string text, CachedSnapshot cachedSnapshot = null) => true;
    }
}
#endif

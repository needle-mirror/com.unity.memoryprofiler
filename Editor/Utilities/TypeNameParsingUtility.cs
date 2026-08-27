using System;
using System.Collections.Generic;

namespace Unity.MemoryProfiler.Editor
{
    /// <summary>
    /// Low-level, allocation-light primitives for scanning managed type-name strings while respecting
    /// angle-bracket (generic) nesting. These are deliberately pure string helpers (no snapshot access)
    /// so they can be unit-tested in isolation and shared between the different type-name consumers
    /// (e.g. the Roots &amp; Impact namespace/generic grouping and PathsToRoot's type-name truncation).
    /// The scanning helpers take <see cref="ReadOnlySpan{T}"/> so callers can pass a slice of a larger
    /// string without an intermediate Substring allocation (a <c>string</c> converts implicitly).
    /// </summary>
    internal static class TypeNameParsingUtility
    {
        /// <summary>
        /// Returns the index of the last '.' in <paramref name="typeName"/> that is not inside angle
        /// brackets, or -1 if there is none. Used to separate a namespace prefix from the type name
        /// without being fooled by dots inside generic arguments (e.g. "Dictionary&lt;System.String, System.Int32&gt;").
        /// </summary>
        public static int FindLastOuterDot(ReadOnlySpan<char> typeName)
        {
            int depth = 0, lastDot = -1;
            for (int i = 0; i < typeName.Length; i++)
            {
                var c = typeName[i];
                if (c == '<') ++depth;
                else if (c == '>') --depth;
                else if (c == '.' && depth == 0) lastDot = i;
            }
            return lastDot;
        }

        /// <summary>
        /// Splits a comma-separated type-argument string at top-level commas (respecting angle-bracket
        /// nesting) and trims each entry.
        /// E.g. "System.String, Dictionary&lt;int, float&gt;" → ["System.String", "Dictionary&lt;int, float&gt;"].
        /// The returned <see cref="List{T}"/> and its entries are necessarily allocated (consumers need
        /// real strings for dictionary keys / recursion); the span input merely avoids the caller having
        /// to Substring the argument list out of the larger type name first.
        /// </summary>
        public static List<string> SplitTypeArgs(ReadOnlySpan<char> argsStr)
        {
            var result = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < argsStr.Length; i++)
            {
                var c = argsStr[i];
                if (c == '<') ++depth;
                else if (c == '>') --depth;
                else if (c == ',' && depth == 0)
                {
                    result.Add(argsStr.Slice(start, i - start).Trim().ToString());
                    start = i + 1;
                }
            }
            if (start < argsStr.Length)
                result.Add(argsStr.Slice(start).Trim().ToString());
            return result;
        }

        /// <summary>
        /// Given the index of a '&lt;' that opens a generic argument list, returns the index of the '&gt;'
        /// that closes it, matching nested '&lt;&gt;' so the close for "Dictionary&lt;List&lt;int&gt;, int&gt;"
        /// is the final '&gt;' rather than the first one. Returns -1 if there is no matching close (malformed
        /// input such as "Foo&lt;").
        /// </summary>
        /// <param name="typeName">The full type-name string.</param>
        /// <param name="openBracketIndex">The index of the opening '&lt;' to match.</param>
        public static int FindGenericArgumentListEnd(ReadOnlySpan<char> typeName, int openBracketIndex)
        {
            var depth = 0;
            for (int i = openBracketIndex; i < typeName.Length; i++)
            {
                if (typeName[i] == '<') ++depth;
                else if (typeName[i] == '>' && --depth == 0)
                    return i;
            }
            return -1;
        }
    }
}

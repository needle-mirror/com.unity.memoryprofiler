using System;
using System.IO;

namespace Unity.MemoryProfiler.Editor
{
    static class PathHelpers
    {
        public static string NormalizePath(string path)
        {
            if (path == null)
                return null;

            return Path.GetFullPath(new Uri(path).LocalPath)
                       .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       .ToUpperInvariant();
        }

        public static bool IsSamePath(string path1, string path2)
        {
            return NormalizePath(path1) == NormalizePath(path2);
        }
    }
}

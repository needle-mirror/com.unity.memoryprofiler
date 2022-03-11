using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal static class Icons
    {
        public const string IconFolder = "Packages/com.unity.memoryprofiler/Package Resources/Icons/";

        public static Texture2D MemoryProfilerWindowTabIcon => IconUtility.LoadIconAtPath(IconFolder + "Memory Profiler.png", false);
    }
}

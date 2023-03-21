using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal static class Icons
    {
        public const string IconFolder = "Packages/com.unity.memoryprofiler/Editor/UI/Icons/";

        public static readonly Texture2D NoIcon = IconUtility.LoadIconAtPath(Icons.IconFolder + "NoIconIcon.png", true);
        public static readonly Texture2D MemoryProfilerWindowTabIcon = IconUtility.LoadIconAtPath(IconFolder + "Memory Profiler.png", false);
    }
}

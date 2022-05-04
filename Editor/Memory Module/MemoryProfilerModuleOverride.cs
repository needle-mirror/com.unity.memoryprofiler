#if UNITY_2021_2_OR_NEWER
using UnityEngine;
using UnityEditor;
using Unity.Profiling.Editor;
using System;

namespace Unity.MemoryProfiler.Editor.MemoryProfilerModule
{
    public enum ProfilerMemoryView
    {
        Simple = 0,
        Detailed = 1
    }
    [Serializable]
    internal class MemoryProfilerModuleOverride
    {
        static MemoryProfilerModuleOverride s_Instance;

        [SerializeField]
        public ProfilerMemoryView ShowDetailedMemoryPane = ProfilerMemoryView.Simple;

        [SerializeField]
        public bool Normalized = false;

        [InitializeOnLoadMethod]
        static void InitializeOverride()
        {
            s_Instance = new MemoryProfilerModuleOverride();
            if (MemoryProfilerSettings.MemoryProfilerPackageOverridesMemoryModuleUI)
                InstallUIOverride();
            MemoryProfilerSettings.InstallUIOverride += InstallUIOverride;
            MemoryProfilerSettings.UninstallUIOverride += UninstallUIOverride;
        }

        public static void InstallUIOverride()
        {
            MemoryProfilerModuleBridge.CreateDetailsViewController = CreateDetailsViewController;
        }

        public static void UninstallUIOverride()
        {
            MemoryProfilerModuleBridge.CreateDetailsViewController = null;
        }

        static ProfilerModuleViewController CreateDetailsViewController(ProfilerWindow profilerWindow)
        {
            return new MemoryProfilerModuleViewController(profilerWindow, s_Instance);
        }
    }
}
#endif

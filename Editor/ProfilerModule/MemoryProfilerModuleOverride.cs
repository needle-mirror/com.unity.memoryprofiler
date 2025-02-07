using UnityEngine;
using UnityEditor;
using Unity.Profiling.Editor;
using System;
using Unity.MemoryProfiler.Editor;

namespace Unity.MemoryProfiler.MemoryProfilerModule.Editor
{
    enum ProfilerMemoryView
    {
        Simple = 0,
        Detailed = 1
    }
    [Serializable]
    class MemoryProfilerModuleOverride
    {
        // This is an Editor Only class and handled via its InitializeOnLoadMethod.
#pragma warning disable UDR0001 // Domain Reload Analyzer
        static MemoryProfilerModuleOverride s_Instance;
#pragma warning restore UDR0001 // Domain Reload Analyzer

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

#pragma warning disable UDR0001 // Domain Reload Analyzer
            MemoryProfilerSettings.InstallUIOverride += InstallUIOverride;
            MemoryProfilerSettings.UninstallUIOverride += UninstallUIOverride;
#pragma warning restore UDR0001 // Domain Reload Analyzer
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

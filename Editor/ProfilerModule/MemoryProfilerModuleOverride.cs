using UnityEngine;
using UnityEditor;
using Unity.Profiling.Editor;
using System;
using Unity.MemoryProfiler.Editor;
#if !MEMORY_PROFILER_MODULE_BINDING_USES_CORRECT_NAMESPACE
using Unity.MemoryProfiler.Editor.MemoryProfilerModule;
#endif
#if ENABLE_CORECLR
using Unity.Scripting.LifecycleManagement;
#endif

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
        // Only used from tests.
        internal static int InstantiationCount { get; private set; }
#pragma warning restore UDR0001 // Domain Reload Analyzer

        [SerializeField]
        public ProfilerMemoryView ShowDetailedMemoryPane = ProfilerMemoryView.Simple;

        [SerializeField]
        public bool Normalized = false;

#if ENABLE_CORECLR
        [AfterAssemblyLoaded]
#else
        [InitializeOnLoadMethod]
#endif
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

#if ENABLE_CORECLR
        [BeforeCodeUnloading]
#endif
        static void UnloadOverride()
        {
            MemoryProfilerSettings.InstallUIOverride -= InstallUIOverride;
            MemoryProfilerSettings.UninstallUIOverride -= UninstallUIOverride;
            s_Instance = null;
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
            ++InstantiationCount;
            return new MemoryProfilerModuleViewController(profilerWindow, s_Instance);
        }
    }
}

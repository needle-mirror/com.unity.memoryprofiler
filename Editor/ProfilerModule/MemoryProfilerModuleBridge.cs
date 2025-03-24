using System;
#if !MEMORY_PROFILER_MODULE_WILL_BIND_BRIDGE_AUTOMATICALLY
using System.Reflection;
#endif
using Unity.Profiling.Editor;
using UnityEditor;

#if MEMORY_PROFILER_MODULE_BINDING_USES_CORRECT_NAMESPACE
// This should technically be the correct namespace...
// ...but main editor code is using reflection with this namespace to find the CreateDetailsViewController func to automaticaly hook up the override,
// so we'll retain the bad namespace ordering here for the time being
namespace Unity.MemoryProfiler.MemoryProfilerModule.Editor
#else
namespace Unity.MemoryProfiler.Editor.MemoryProfilerModule
#endif
{
    internal static class MemoryProfilerModuleBridge
    {
#if MEMORY_PROFILER_MODULE_WILL_BIND_BRIDGE_AUTOMATICALLY  // aka Unity Version >= 2022.2.0a12
        // Called via reflection from MemoryProfilerModule in trunk on 2022.2 and later.
        public static Func<ProfilerWindow, ProfilerModuleViewController> CreateDetailsViewController { get; set; }
#else
        static FieldInfo s_CreateDetailsViewControllerMemberInfo;
        public static Func<ProfilerWindow, ProfilerModuleViewController> CreateDetailsViewController
        {
            get
            {
                return s_CreateDetailsViewControllerMemberInfo.GetValue(null) as
                    Func<ProfilerWindow, ProfilerModuleViewController>;
            }

            set { s_CreateDetailsViewControllerMemberInfo.SetValue(null, value); }
        }

        static MemoryProfilerModuleBridge()
        {
            var profilerWindowType = typeof(ProfilerWindow);
            var assembly = profilerWindowType.Assembly;
            var type = assembly.GetType("UnityEditorInternal.Profiling.MemoryProfilerOverrides");
            s_CreateDetailsViewControllerMemberInfo = type.GetField("CreateDetailsViewController");
        }

#endif // MEMORY_PROFILER_MODULE_WILL_BIND_BRIDGE_AUTOMATICALLY
    }
}

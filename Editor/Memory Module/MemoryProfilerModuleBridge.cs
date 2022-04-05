#if UNITY_2021_2_OR_NEWER
using System;
using System.Reflection;
using Unity.Profiling.Editor;
using UnityEditor;

namespace Unity.MemoryProfiler.Editor.MemoryProfilerModule
{
    internal static class MemoryProfilerModuleBridge
    {
#if MEMORY_PROFILER_MODULE_WILL_BIND_BRIDGE_AUTOMATICALLY
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
#endif

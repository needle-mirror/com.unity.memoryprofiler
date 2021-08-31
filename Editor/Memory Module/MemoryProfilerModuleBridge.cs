#if UNITY_2021_2_OR_NEWER
using System;
using System.Reflection;
using Unity.Profiling.Editor;
using UnityEditor;
using UnityEditor.Profiling;

namespace Unity.MemoryProfiler.Editor.MemoryProfilerModule
{
    internal static class MemoryProfilerModuleBridge
    {
        public static Func<ProfilerWindow, ProfilerModuleViewController> CreateDetailsViewController
        {
            get
            {
                return s_CreateDetailsViewControllerMemberInfo.GetValue(null) as Func<ProfilerWindow, ProfilerModuleViewController>;
            }

            set
            {
                s_CreateDetailsViewControllerMemberInfo.SetValue(null, value);
            }
        }

        static FieldInfo s_CreateDetailsViewControllerMemberInfo;
        static Type s_MemoryProfilerModuleType;
        static MethodInfo s_GetMemoryProfilerModuleMethodInfo;
        static MethodInfo s_GetSimpleMemoryPaneTextMethodInfo;
        static MethodInfo s_GetPlatformSpecificTextMethodInfo;
        static MethodInfo s_DrawDetailedMemoryPaneMethodInfo; 
        static MethodInfo s_TakeCaptureMethodInfo;
        static FieldInfo s_GatherObjectReferencesFieldInfo;

        static MemoryProfilerModuleBridge()
        {
            var profilerWindowType = typeof(ProfilerWindow);
            var assembly = profilerWindowType.Assembly;
            var type = assembly.GetType("UnityEditorInternal.Profiling.MemoryProfilerOverrides");
            s_CreateDetailsViewControllerMemberInfo = type.GetField("CreateDetailsViewController");

            s_MemoryProfilerModuleType = assembly.GetType("UnityEditorInternal.Profiling.MemoryProfilerModule");

            s_GetMemoryProfilerModuleMethodInfo = profilerWindowType.GetMethod("GetProfilerModuleByType", BindingFlags.Instance | BindingFlags.NonPublic);

            s_GetSimpleMemoryPaneTextMethodInfo = s_MemoryProfilerModuleType.GetMethod("GetSimpleMemoryPaneText", BindingFlags.Static | BindingFlags.NonPublic);
            s_GetPlatformSpecificTextMethodInfo = s_MemoryProfilerModuleType.GetMethod("GetPlatformSpecificText", BindingFlags.Static | BindingFlags.NonPublic);
            s_DrawDetailedMemoryPaneMethodInfo = s_MemoryProfilerModuleType.GetMethod("DrawDetailedMemoryPane", BindingFlags.Instance | BindingFlags.NonPublic);
            s_TakeCaptureMethodInfo = s_MemoryProfilerModuleType.GetMethod("RefreshMemoryData", BindingFlags.Instance | BindingFlags.NonPublic);
            s_GatherObjectReferencesFieldInfo = s_MemoryProfilerModuleType.GetField("m_GatherObjectReferences", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        public static object GetMemoryProfilerModule(ProfilerWindow window)
        {
            return s_GetMemoryProfilerModuleMethodInfo.Invoke(window, new object[] { s_MemoryProfilerModuleType });
        }

        public static string GetSimpleMemoryPaneText(this object module, RawFrameDataView data, ProfilerWindow window, bool a)
        {
            return s_GetSimpleMemoryPaneTextMethodInfo.Invoke(null, new object[] { data, window, a }) as string;
        }
        public static string GetPlatformSpecificText(this object module, RawFrameDataView data, ProfilerWindow window)
        {
            return s_GetPlatformSpecificTextMethodInfo.Invoke(null, new object[] { data, window}) as string;
        }

        public static void DrawDetailedMemoryPane(this object module)
        {
            s_DrawDetailedMemoryPaneMethodInfo.Invoke(module, null);
        }
        public static void TakeCapture(this object module)
        {
            s_TakeCaptureMethodInfo.Invoke(module, null);
        }

        public static bool GetGatherObjectReferences(this object module)
        {
            return (bool)s_GatherObjectReferencesFieldInfo.GetValue(module);
        }

        public static void SetGatherObjectReferences(this object module, bool value)
        {
            s_GatherObjectReferencesFieldInfo.SetValue(module, value);
        }
    }
}
#endif

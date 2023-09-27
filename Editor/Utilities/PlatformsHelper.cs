using System;
using Unity.MemoryProfiler.Editor.UI;
using UnityEditor;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor
{
    internal static class PlatformsHelper
    {
        public const RuntimePlatform GameCoreXboxSeries = (RuntimePlatform)36 /*RuntimePlatform.GameCoreXboxSeries*/;
        public const RuntimePlatform GameCoreXboxOne = (RuntimePlatform)37 /*RuntimePlatform.GameCoreXboxOne*/;
        public const RuntimePlatform PS5 = (RuntimePlatform)38 /*RuntimePlatform.PS5*/;
        public const RuntimePlatform CloudRendering = (RuntimePlatform)35 /*RuntimePlatform.CloudRendering*/;
        public const RuntimePlatform LinuxServer = (RuntimePlatform)43 /*RuntimePlatform.LinuxServer*/;
        public const RuntimePlatform WindowsServer = (RuntimePlatform)44 /*RuntimePlatform.WindowsServer*/;
        public const RuntimePlatform OSXServer = (RuntimePlatform)45 /*RuntimePlatform.OSXServer*/;
        public const RuntimePlatform EmbeddedLinuxArm64 = (RuntimePlatform)39 /*RuntimePlatform.EmbeddedLinuxArm64*/;
        public const RuntimePlatform EmbeddedLinuxArm32 = (RuntimePlatform)40 /*RuntimePlatform.EmbeddedLinuxArm32*/;
        public const RuntimePlatform EmbeddedLinuxX64 = (RuntimePlatform)41 /*RuntimePlatform.EmbeddedLinuxX64*/;
        public const RuntimePlatform EmbeddedLinuxX86 = (RuntimePlatform)42 /*RuntimePlatform.EmbeddedLinuxX86*/;

        static readonly RuntimePlatform[] k_PlatformsHideResidentMemory = new RuntimePlatform[] { RuntimePlatform.PS4, RuntimePlatform.PS5, RuntimePlatform.Switch, RuntimePlatform.WebGLPlayer };

        public static BuildTarget GetBuildTarget(this RuntimePlatform runtimePlatform)
        {
            BuildTarget buildTarget = BuildTarget.NoTarget;
            switch (runtimePlatform)
            {
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer:
                case OSXServer:
                    buildTarget = BuildTarget.StandaloneOSX;
                    break;
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:
                case WindowsServer:
                    buildTarget = BuildTarget.StandaloneWindows;
                    break;
                case RuntimePlatform.IPhonePlayer:
                    buildTarget = BuildTarget.iOS;
                    break;
                case RuntimePlatform.tvOS:
                    buildTarget = BuildTarget.tvOS;
                    break;
                case RuntimePlatform.Android:
                    buildTarget = BuildTarget.Android;
                    break;
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxEditor:
                case LinuxServer:
                    buildTarget = BuildTarget.StandaloneLinux64;
                    break;
#if UNITY_2021_2_OR_NEWER
                case RuntimePlatform.EmbeddedLinuxArm32:
                case RuntimePlatform.EmbeddedLinuxArm64:
                case RuntimePlatform.EmbeddedLinuxX64:
                case RuntimePlatform.EmbeddedLinuxX86:
                    buildTarget = BuildTarget.EmbeddedLinux;
                    break;
#endif
                case RuntimePlatform.WebGLPlayer:
                    buildTarget = BuildTarget.WebGL;
                    break;
                case RuntimePlatform.WSAPlayerX86:
                case RuntimePlatform.WSAPlayerX64:
                case RuntimePlatform.WSAPlayerARM:
                    buildTarget = BuildTarget.WSAPlayer;
                    break;
                case RuntimePlatform.PS4:
                    buildTarget = BuildTarget.PS4;
                    break;
                case PS5:
                    buildTarget = BuildTarget.PS5;
                    break;
                case RuntimePlatform.XboxOne:
                    buildTarget = BuildTarget.XboxOne;
                    break;
                case GameCoreXboxSeries:
                case GameCoreXboxOne:
                    buildTarget = BuildTarget.GameCoreXboxOne;
                    break;
                case RuntimePlatform.Switch:
                    buildTarget = BuildTarget.Switch;
                    break;
#if !UNITY_2022_2_OR_NEWER
                case RuntimePlatform.Lumin:
                    buildTarget = BuildTarget.Lumin;
                    break;
#endif
#if !UNITY_2023_1_OR_NEWER
                case RuntimePlatform.Stadia:
                    buildTarget = BuildTarget.Stadia;
                    break;
#endif
#if UNITY_2022_2_OR_NEWER
                case RuntimePlatform.QNXArm32:
                case RuntimePlatform.QNXArm64:
                case RuntimePlatform.QNXX64:
                case RuntimePlatform.QNXX86:
                    buildTarget = BuildTarget.QNX;
                    break;
#endif
                case CloudRendering:
#if UNITY_2022_2_OR_NEWER
                    buildTarget = BuildTarget.LinuxHeadlessSimulation;
#else
                    buildTarget = BuildTarget.CloudRendering;
#endif
                    break;
                default:
                    // Unknown target
                    break;
            }
            return buildTarget;
        }

        public static RuntimePlatform GetRuntimePlatform(string platformName)
        {
            if (string.IsNullOrEmpty(platformName) || !Enum.IsDefined(typeof(RuntimePlatform), platformName))
                return (RuntimePlatform)~0;

            return (RuntimePlatform)Enum.Parse(typeof(RuntimePlatform), platformName);
        }

        public static bool RuntimePlatformIsEditorPlatform(RuntimePlatform runtimePlatform)
        {
            return runtimePlatform == RuntimePlatform.OSXEditor ||
                runtimePlatform == RuntimePlatform.WindowsEditor ||
                runtimePlatform == RuntimePlatform.LinuxEditor;
        }

        public static bool SameRuntimePlatformAsEditorPlatform(RuntimePlatform runtimePlatform)
        {
            switch (runtimePlatform)
            {
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer:
                case OSXServer:
                    return Application.platform == RuntimePlatform.OSXEditor;
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:
                case WindowsServer:
                    return Application.platform == RuntimePlatform.WindowsEditor;
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxEditor:
                case EmbeddedLinuxArm64:
                case EmbeddedLinuxArm32:
                case EmbeddedLinuxX64:
                case EmbeddedLinuxX86:
                case LinuxServer:
                    return Application.platform == RuntimePlatform.LinuxEditor;
                default:
                    return false;
            }
        }

        public static Texture GetPlatformIcon(RuntimePlatform platform)
        {
            Texture icon = null;

            // Try to use builtin Editor icon.
            var builtinIconName = GetPlatformIconName(platform);
            if (builtinIconName != null)
                icon = IconUtility.LoadBuiltInIconWithName(builtinIconName);

            // Fallback to NoIcon.
            if (icon == null)
                icon = Icons.NoIcon;

            return icon;
        }

        public static string GetPlatformIconName(RuntimePlatform platform)
        {
            string name;
            switch (platform)
            {
#if UNITY_2021_2_OR_NEWER
                case RuntimePlatform.LinuxServer:
                case RuntimePlatform.OSXServer:
                case RuntimePlatform.WindowsServer:
                    name = "DedicatedServer";
                    break;
#endif

#if UNITY_2023_1_OR_NEWER
                case RuntimePlatform.LinuxHeadlessSimulation:
                    name = "LinuxHeadlessSimulation";
                    break;
#endif
                default:
                {
                    var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(platform.GetBuildTarget());
                    if (buildTargetGroup == BuildTargetGroup.Unknown)
                        return null;

                    switch (buildTargetGroup)
                    {
                        case BuildTargetGroup.WSA:
                            name = "Metro";
                            break;
                        default:
                            name = buildTargetGroup.ToString();
                            break;
                    }
                }
                break;
            }

            return "BuildSettings." + name + " On.png";
        }

        /// Platforms on which we don't show detailed resident memory breakdown.
        /// It could because information is unreliable or doesn't make much sense (all memory is resident).
        public static bool IsResidentMemoryBlacklistedPlatform(RuntimePlatform platform)
        {
            return Array.IndexOf(k_PlatformsHideResidentMemory, platform) != -1;
        }
    }
}

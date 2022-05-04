using System;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor
{
    internal static class PlatformsHelper
    {
        public const RuntimePlatform GameCoreXboxSeries = (RuntimePlatform)36 /*RuntimePlatform.GameCoreXboxSeries*/;
        public const RuntimePlatform GameCoreXboxOne = (RuntimePlatform)37 /*RuntimePlatform.GameCoreXboxOne*/;
        public const RuntimePlatform PS5 = (RuntimePlatform)38 /*RuntimePlatform.PS5*/;
        public const RuntimePlatform LinuxServer = (RuntimePlatform)43 /*RuntimePlatform.LinuxServer*/;
        public const RuntimePlatform WindowsServer = (RuntimePlatform)44 /*RuntimePlatform.WindowsServer*/;
        public const RuntimePlatform OSXServer = (RuntimePlatform)45 /*RuntimePlatform.OSXServer*/;
        public const RuntimePlatform EmbeddedLinuxArm64 = (RuntimePlatform)39 /*RuntimePlatform.EmbeddedLinuxArm64*/;
        public const RuntimePlatform EmbeddedLinuxArm32 = (RuntimePlatform)40 /*RuntimePlatform.EmbeddedLinuxArm32*/;
        public const RuntimePlatform EmbeddedLinuxX64 = (RuntimePlatform)41 /*RuntimePlatform.EmbeddedLinuxX64*/;
        public const RuntimePlatform EmbeddedLinuxX86 = (RuntimePlatform)42 /*RuntimePlatform.EmbeddedLinuxX86*/;

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
                case RuntimePlatform.Android:
                    buildTarget = BuildTarget.Android;
                    break;
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxEditor:
                case LinuxServer:
                case EmbeddedLinuxArm32:
                case EmbeddedLinuxArm64:
                case EmbeddedLinuxX64:
                case EmbeddedLinuxX86:
                    buildTarget = BuildTarget.StandaloneLinux64;
                    break;
                case RuntimePlatform.WebGLPlayer:
                    buildTarget = BuildTarget.WebGL;
                    break;
                case RuntimePlatform.WSAPlayerX86:
                case RuntimePlatform.WSAPlayerX64:
                case RuntimePlatform.WSAPlayerARM:
                    buildTarget = BuildTarget.WSAPlayer;
                    break;
                case RuntimePlatform.PS4:
                case PS5:
                    buildTarget = BuildTarget.PS4;
                    break;
                case RuntimePlatform.XboxOne:
                case GameCoreXboxSeries:
                case GameCoreXboxOne:
                    buildTarget = BuildTarget.XboxOne;
                    break;
                case RuntimePlatform.tvOS:
                    buildTarget = BuildTarget.tvOS;
                    break;
                case RuntimePlatform.Switch:
                    buildTarget = BuildTarget.Switch;
                    break;
                case RuntimePlatform.Lumin:
                    buildTarget = BuildTarget.Lumin;
                    break;
                case RuntimePlatform.Stadia:
                    buildTarget = BuildTarget.Stadia;
                    break;
                default:
                    // Unknown target
                    //return BuildTarget.NoTarget;
                    break;
            }
            return buildTarget;
        }

        public static ulong GetPlatformSpecificTotalAvailableMemory(ProfileTargetInfo targetInfo)
        {
            switch (targetInfo.RuntimePlatform)
            {
                case RuntimePlatform.IPhonePlayer:
                // unified
                case RuntimePlatform.tvOS:
                // same as iOS, unified
                case RuntimePlatform.PS4:
                case PS5:
                // PlayStation uses unified memory
                case RuntimePlatform.XboxOne:
                case GameCoreXboxSeries:
                case GameCoreXboxOne:
                // XBox uses unified memory
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer:
                case OSXServer:
                // OSX uses separate RAM & VRAM ... unless it's using an M1 Pro or M1 Max chip... So, default to assume it's unified, rather undercount it than over
                case RuntimePlatform.Android:
                case EmbeddedLinuxArm32:
                case EmbeddedLinuxArm64:
                // some are unified ... ???!!! So, default to assume it's unified, rather undercount it than over
                case EmbeddedLinuxX64:
                case EmbeddedLinuxX86:
                // ??? default to assume it's unified, rather undercount it than over
                case RuntimePlatform.WebGLPlayer:
                // ??? default to assume it's unified, rather undercount it than over
                case RuntimePlatform.Lumin:
                // ??? default to assume it's unified, rather undercount it than over
                case RuntimePlatform.Stadia:
                // ??? default to assume it's unified, rather undercount it than over
                case RuntimePlatform.Switch:
                    // Switch uses unified memory
                    // so totalGraphicsMemory is included in totalPhysicalMemory
                    return targetInfo.TotalPhysicalMemory;


                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:
                case WindowsServer:
                // Windows uses separate RAM & VRAM
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxEditor:
                case LinuxServer:
                // Linux uses separate RAM & VRAM
                case RuntimePlatform.WSAPlayerX86:
                case RuntimePlatform.WSAPlayerX64:
                case RuntimePlatform.WSAPlayerARM:
                    // Windows Store apps use ???
                    return targetInfo.TotalPhysicalMemory + targetInfo.TotalGraphicsMemory;
                default:
                    // Unknown target is assumed to be non unified
                    return targetInfo.TotalPhysicalMemory + targetInfo.TotalGraphicsMemory;
            }
        }

        public static GUIContent GetPlatformSpecificTotalAvailableMemoryText(ProfileTargetInfo targetInfo)
        {
            ulong totalAvailableMemory = GetPlatformSpecificTotalAvailableMemory(targetInfo);
            switch (targetInfo.RuntimePlatform)
            {
                case RuntimePlatform.IPhonePlayer:
                // unified
                case RuntimePlatform.tvOS:
                // same as iOS, unified
                case RuntimePlatform.PS4:
                case PS5:
                // PlayStation uses unified memory
                case RuntimePlatform.XboxOne:
                case GameCoreXboxSeries:
                case GameCoreXboxOne:
                // XBox uses unified memory
                case RuntimePlatform.Switch:
                    // Switch uses unified memory
                    // so totalGraphicsMemory is included in totalPhysicalMemory
                    return new GUIContent(
                        text: string.Format(TextContent.TotalAvailableSystemResourcesUnified.text,
                            EditorUtility.FormatBytes((long)totalAvailableMemory)),
                        tooltip: string.Format(TextContent.TotalAvailableSystemResourcesUnified.tooltip,
                            EditorUtility.FormatBytes((long)totalAvailableMemory)));

                case RuntimePlatform.Lumin:
                // ???
                case RuntimePlatform.Stadia:
                // ???
                case RuntimePlatform.WebGLPlayer:
                // ???
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer:
                case OSXServer:
                // OSX uses separate RAM & VRAM ... unless it's using an M1 Pro or M1 Max chip... So, default to assume it's unified, rather undercount it than over
                case RuntimePlatform.Android:
                case EmbeddedLinuxArm32:
                case EmbeddedLinuxArm64:
                    // some are unified ... ???!!! So, default to assume it's unified, rather undercount it than over
                    return new GUIContent(
                        text: string.Format(TextContent.TotalAvailableSystemResourcesUnifiedStatusUnknown.text,
                            EditorUtility.FormatBytes((long)totalAvailableMemory)),
                        tooltip: string.Format(TextContent.TotalAvailableSystemResourcesUnifiedStatusUnknown.tooltip,
                            EditorUtility.FormatBytes((long)totalAvailableMemory)));

                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:
                case WindowsServer:
                // Windows uses separate RAM & VRAM
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxEditor:
                case LinuxServer:
                // Linux uses separate RAM & VRAM
                case RuntimePlatform.WSAPlayerX86:
                case RuntimePlatform.WSAPlayerX64:
                case RuntimePlatform.WSAPlayerARM:
                    // Windows Store presumably apps use RAM & VRAM
                    return new GUIContent(
                        text: string.Format(TextContent.TotalAvailableSystemResources.text,
                            EditorUtility.FormatBytes((long)totalAvailableMemory)),
                        tooltip: string.Format(TextContent.TotalAvailableSystemResources.tooltip,
                            EditorUtility.FormatBytes((long)totalAvailableMemory),
                            EditorUtility.FormatBytes((long)targetInfo.TotalPhysicalMemory),
                            EditorUtility.FormatBytes((long)targetInfo.TotalGraphicsMemory)));
                default:
                    // Unknown target is assumed to be non unified
                    return new GUIContent(
                        text: string.Format(TextContent.TotalAvailableSystemResources.text,
                            EditorUtility.FormatBytes((long)totalAvailableMemory)),
                        tooltip: string.Format(TextContent.TotalAvailableSystemResources.tooltip,
                            EditorUtility.FormatBytes((long)totalAvailableMemory),
                            EditorUtility.FormatBytes((long)targetInfo.TotalPhysicalMemory),
                            EditorUtility.FormatBytes((long)targetInfo.TotalGraphicsMemory)));
            }
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
    }
}

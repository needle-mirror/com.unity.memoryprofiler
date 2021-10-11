using UnityEditor;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor
{
    internal static class PlatformsHelper
    {
        public const RuntimePlatform GameCoreXboxSeries = (RuntimePlatform)36 /*RuntimePlatform.GameCoreXboxSeries*/;
        public const RuntimePlatform GameCoreXboxOne = (RuntimePlatform)37 /*RuntimePlatform.GameCoreXboxOne*/;
        public const RuntimePlatform PS5 = (RuntimePlatform)38 /*RuntimePlatform.PS5*/;

        public static BuildTarget GetBuildTarget(this RuntimePlatform runtimePlatform)
        {
            BuildTarget buildTarget = BuildTarget.NoTarget;
            switch (runtimePlatform)
            {
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer:
                    buildTarget = BuildTarget.StandaloneOSX;
                    break;
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:
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
    }
}

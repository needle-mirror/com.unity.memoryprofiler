using System.Runtime.CompilerServices;

namespace Unity.MemoryProfiler.Editor
{
    static class MethodImplementationHelper
    {
        public const MethodImplOptions AggressiveInlining = MethodImplOptions.AggressiveInlining | AggressiveOptimization;
        public const MethodImplOptions AggressiveOptimization = (MethodImplOptions)512;
    }
}

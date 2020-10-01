using System;
using Unity.MemoryProfiler.Editor.Diagnostics;

namespace Unity.MemoryProfiler.Editor.Utilities.Math
{
    internal static class MathFunc
    {
        public static uint ToNextPow2(uint n)
        {
            --n;
            n |= (n >> 1);
            n |= (n >> 2);
            n |= (n >> 4);
            n |= (n >> 8);
            n |= (n >> 16);
            ++n;
            return n;
        }
    }
}

namespace Unity.MemoryProfiler.Editor
{
    //TODO: deprecate and delete
    internal static class MathExt
    {
        public static ulong Clamp(this ulong value, ulong min, ulong max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}

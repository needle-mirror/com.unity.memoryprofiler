using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Diagnostics;

namespace Unity.MemoryProfiler.Editor.Containers.Memory
{
    static class UnsafeDataUtility
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static T ReadArrayElement<T>(void* buffer, long idx) where T : unmanaged
        {
            return *(T*)((byte*)buffer + (idx * sizeof(T)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static void WriteArrayElement<T>(void* buffer, long idx, ref T src) where T : unmanaged
        {
            *(T*)((byte*)buffer + (idx * sizeof(T))) = src;
        }
    }
}

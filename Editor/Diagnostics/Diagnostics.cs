using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.Diagnostics
{
    internal struct Checks
    {
        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG"), MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckEntryTypeValueIsValidAndThrow(EntryType val)
        {
            if (val == EntryType.Count || (int)val < 0)
                throw new UnityException($"Invalid Entry type: {val}");
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG"), MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckEntryTypeFormatIsValidAndThrow(EntryFormat val, EntryFormat val2)
        {
            if (val != val2)
                throw new UnityException("Invalid Entry type format");
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG"), MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckIndexOutOfBoundsAndThrow(long index, long count)
        {
            if (index >= count)
                throw new ArgumentOutOfRangeException($"Index out of bounds. Expected smaller than {count} but received {index}.");
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG"), MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckIndexInRangeAndThrow(long index, long count)
        {
            if (index < 0 || index >= count)
                throw new ArgumentOutOfRangeException($"Index out of bounds. Expected [0, {count}) but received {index}.", "index");
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG"), MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckCountGreaterZeroAndThrowArgumentException(long count)
        {
            if (count <= 0)
                if (count < 0)
                    throw new ArgumentException($"Count less then 0. Received {count}.", "count");
                else
                    throw new ArgumentException($"Count is 0.", "count");
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG"), MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckCountWithinReasonAndThrowArgumentException(long count, long capacity)
        {
            CheckCountGreaterZeroAndThrowArgumentException(count);
            if (count > long.MaxValue - capacity)
                throw new ArgumentException($"Total capacity would exceed long-indexable range.", "count");
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG"), MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckCountGreaterZeroAndThrowInvalidOperationException(long count)
        {
            if (count <= 0)
                if (count < 0)
                    throw new InvalidOperationException($"Count less then 0. Received {count}.");
                else
                    throw new InvalidOperationException($"Count is 0.");
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG"), MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckEquals<T>(T rhs, T lhs) where T : IEquatable<T>
        {
            if (!rhs.Equals(lhs))
                throw new Exception($"Expected: {rhs}, but actual value was: {lhs}.");
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG"), MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckEqualsEnum<T>(T rhs, T lhs) where T : unmanaged, Enum
        {
            unsafe
            {
                switch (sizeof(T))
                {
                    case 1:
                        if (*(byte*)&rhs != *(byte*)&lhs)
                            throw new Exception($"Expected: {rhs}, but actual value was: {lhs}.");
                        break;
                    case 2:
                        if (*(ushort*)&rhs != *(ushort*)&lhs)
                            throw new Exception($"Expected: {rhs}, but actual value was: {lhs}.");
                        break;
                    case 4:
                        if (*(uint*)&rhs != *(uint*)&lhs)
                            throw new Exception($"Expected: {rhs}, but actual value was: {lhs}.");
                        break;
                    case 8:
                        if (*(ulong*)&rhs != *(ulong*)&lhs)
                            throw new Exception($"Expected: {rhs}, but actual value was: {lhs}.");
                        break;
                    default:
                        throw new Exception("Unsupported enum size.");
                }
            }
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG"), MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckNotEquals<T>(T rhs, T lhs) where T : IEquatable<T>
        {
            if (EqualityComparer<T>.Default.Equals(rhs, lhs))
                throw new Exception($"Expected comparands to be different, but they were the same. Values: {rhs} {lhs}");
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG"), MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckNotNull(object obj)
        {
            if (obj == null)
                throw new Exception("Expected provided parameter to be non-null");
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG"), MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckFileExistsAndThrow(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(string.Format("File not found at provided path: {0}", path));
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG"), MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowExceptionGeneric<T>(string message) where T : Exception, new()
        {
            var except = (T)Activator.CreateInstance(typeof(T), message);
            throw except;
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG"), MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void IsTrue(bool condition)
        {
            if (!condition)
                throw new Exception("Expected condition to be true, but was false.");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.Diagnostics
{
    internal struct Checks
    {
        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG")]
        public static void CheckEntryTypeValueIsValidAndThrow(EntryType val)
        {
            if (val == EntryType.Count || (int)val < 0)
                throw new UnityException($"Invalid Entry type: {val}");
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG")]
        public static void CheckEntryTypeFormatIsValidAndThrow(EntryFormat val, EntryFormat val2)
        {
            if (val != val2)
                throw new UnityException("Invalid Entry type format");
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG")]
        public static void CheckIndexOutOfBoundsAndThrow(long index, long count)
        {
            if (index >= count)
                throw new ArgumentOutOfRangeException($"Index out of bounds. Expected smaller than {count} but received {index}.");
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG")]
        public static void CheckIndexInRangeAndThrow(long index, long count)
        {
            if (index < 0 || index > count)
                throw new ArgumentOutOfRangeException($"Index out of bounds. Expected [0, {count}) but received {index}.");
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG")]
        public static void CheckEquals<T>(T rhs, T lhs)
        {
            if (!EqualityComparer<T>.Default.Equals(rhs, lhs))
                throw new Exception($"Expected: {rhs}, but actual value was: {lhs}.");
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG")]
        public static void CheckNotEquals<T>(T rhs, T lhs)
        {
            if (EqualityComparer<T>.Default.Equals(rhs, lhs))
                throw new Exception(string.Format("Expected comparands to be different, but they were the same. Value: {0}", rhs));
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG")]
        public static void CheckNotNull(object obj)
        {
            if (obj == null)
                throw new Exception("Expected provided parameter to be non-null");
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG")]
        public static void CheckFileExistsAndThrow(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(string.Format("File not found at provided path: {0}", path));
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG")]
        public static void ThrowExceptionGeneric<T>(string message) where T : Exception, new()
        {
            var except = (T)Activator.CreateInstance(typeof(T), message);
            throw except;
        }

        [Conditional("ENABLE_MEMORY_PROFILER_DEBUG")]
        public static void IsTrue(bool condition)
        {
            if (!condition)
                throw new Exception("Expected condition to be true, but was false.");
        }
    }
}

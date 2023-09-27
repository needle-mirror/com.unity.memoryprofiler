using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security;
using Unity.MemoryProfiler.Editor.Containers;
using UnityEngine;
namespace Unity.MemoryProfiler.Editor
{

    /// <summary>
    /// <see cref="BitConverter"/> can't deal with byte pointers or <see cref="ILongIndexedContainer{T}"/> so this class is a copy of its functions that the Memory Profiler uses, but converted to take ILongIndexedContainer<byte> values.
    /// </summary>
    internal static class BitConverterExt
    {
        /// <summary>
        /// Returns a Unicode character converted from two bytes at a specified position
        /// in a ILongIndexedContainer of bytes.
        /// </summary>
        /// <param name="value">A ILongIndexedContainer of bytes to parse.</param>
        /// <param name="startIndex">The starting position within value.</param>
        /// <returns>A character formed by two bytes beginning at startIndex.</returns>
        /// <exception cref="ArgumentNullException">The ILongIndexedContainer value is not created.</exception>
        /// <exception cref="ArgumentOutOfRangeException">startIndex is less than zero or greater than the length of value minus 1.</exception>
        /// <exception cref="ArgumentException">startIndex equals the length of value minus 1.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static char ToChar(ILongIndexedContainer<byte> value, ulong startIndex)
        {
            return (char)ToInt16(value, startIndex);
        }

        /// <summary>
        /// Returns a 16-bit signed integer converted from two bytes at a specified position
        /// in a ILongIndexedContainer of bytes.
        /// </summary>
        /// <param name="value">A ILongIndexedContainer of bytes to parse.</param>
        /// <param name="startIndex">The starting position within value.</param>
        /// <returns>A 16-bit signed integer formed by two bytes beginning at startIndex.</returns>
        /// <exception cref="ArgumentNullException">The ILongIndexedContainer value is not created.</exception>
        /// <exception cref="ArgumentOutOfRangeException">startIndex is less than zero or greater than the length of value minus 1.</exception>
        /// <exception cref="ArgumentException">startIndex equals the length of value minus 1.</exception>
        [SecuritySafeCritical]
        public unsafe static short ToInt16(ILongIndexedContainer<byte> value, ulong startIndex)
        {
            if (!value.IsCreated)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (startIndex < 0 || startIndex >= (ulong)value.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex), $"{nameof(startIndex)} is out of range at {startIndex} on a length of {value.Count}");
            }

            if (startIndex > (ulong)value.Count - 2)
            {
                throw new ArgumentException("Start index plus lenght is bigger than array is long", nameof(startIndex));
            }

            unsafe
            {
                byte* ptr = value.GetUnsafeTypedPtr() + startIndex;
                if (startIndex % 2 == 0)
                {
                    return *(short*)ptr;
                }

                if (BitConverter.IsLittleEndian)
                {
                    return (short)(*ptr | (ptr[1] << 8));
                }

                return (short)((*ptr << 8) | ptr[1]);
            }
        }

        /// <summary>
        /// Returns a 32-bit signed integer converted from four bytes at a specified position
        /// in a ILongIndexedContainer of bytes.
        /// </summary>
        /// <param name="value">A ILongIndexedContainer of bytes to parse.</param>
        /// <param name="startIndex">The starting position within value.</param>
        /// <returns>A 32-bit signed integer formed by four bytes beginning at startIndex.</returns>
        /// <exception cref="ArgumentNullException">The ILongIndexedContainer value is not created.</exception>
        /// <exception cref="ArgumentOutOfRangeException">startIndex is less than zero or greater than the length of value minus 1.</exception>
        /// <exception cref="ArgumentException">startIndex is greater than or equal to the length of value minus 3, and is less
        /// than or equal to the length of value minus 1.</exception>
        [SecuritySafeCritical]
        public unsafe static int ToInt32(ILongIndexedContainer<byte> value, ulong startIndex)
        {
            if (!value.IsCreated)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (startIndex < 0 || startIndex >= (ulong)value.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex), $"{nameof(startIndex)} is out of range at {startIndex} on a length of {value.Count}");
            }

            if (startIndex > (ulong)value.Count - 4)
            {
                throw new ArgumentException("Start index plus lenght is bigger than array is long", nameof(startIndex));
            }

            unsafe
            {
                byte* ptr = value.GetUnsafeTypedPtr() + startIndex;
                if (startIndex % 4 == 0)
                {
                    return *(int*)ptr;
                }

                if (BitConverter.IsLittleEndian)
                {
                    return *ptr | (ptr[1] << 8) | (ptr[2] << 16) | (ptr[3] << 24);
                }

                return (*ptr << 24) | (ptr[1] << 16) | (ptr[2] << 8) | ptr[3];
            }
        }

        /// <summary>
        /// Returns a 64-bit signed integer converted from eight bytes at a specified position
        /// in a ILongIndexedContainer of bytes.
        /// </summary>
        /// <param name="value">A ILongIndexedContainer of bytes to parse.</param>
        /// <param name="startIndex">The starting position within value.</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">The ILongIndexedContainer value is not created.</exception>
        /// <exception cref="ArgumentOutOfRangeException">startIndex is less than zero or greater than the length of value minus 1.</exception>
        /// <exception cref="ArgumentException">startIndex is greater than or equal to the length of value minus 7, and is less
        /// than or equal to the length of value minus 1.</exception>
        [SecuritySafeCritical]

        public unsafe static long ToInt64(ILongIndexedContainer<byte> value, ulong startIndex)
        {
            if (!value.IsCreated)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (startIndex < 0 || startIndex >= (ulong)value.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex), $"{nameof(startIndex)} is out of range at {startIndex} on a length of {value.Count}");
            }

            if (startIndex > (ulong)value.Count - 8)
            {
                throw new ArgumentException("Start index plus lenght is bigger than array is long", nameof(startIndex));
            }

            unsafe
            {
                byte* ptr = value.GetUnsafeTypedPtr() + startIndex;
                if (startIndex % 8 == 0)
                {
                    return *(long*)ptr;
                }

                if (BitConverter.IsLittleEndian)
                {
                    int num = *ptr | (ptr[1] << 8) | (ptr[2] << 16) | (ptr[3] << 24);
                    int num2 = ptr[4] | (ptr[5] << 8) | (ptr[6] << 16) | (ptr[7] << 24);
                    return (uint)num | ((long)num2 << 32);
                }

                int num3 = (*ptr << 24) | (ptr[1] << 16) | (ptr[2] << 8) | ptr[3];
                int num4 = (ptr[4] << 24) | (ptr[5] << 16) | (ptr[6] << 8) | ptr[7];
                return (uint)num4 | ((long)num3 << 32);
            }
        }

        /// <summary>
        /// Returns a 16-bit unsigned integer converted from two bytes at a specified position
        /// in a ILongIndexedContainer of bytes.
        /// </summary>
        /// <param name="value">A ILongIndexedContainer of bytes to parse.</param>
        /// <param name="startIndex">The starting position within value.</param>
        /// <returns>A 16-bit unsigned integer formed by two bytes beginning at startIndex.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentOutOfRangeException">startIndex is less than zero or greater than the length of value minus 1.</exception>
        /// <exception cref="ArgumentException">startIndex equals the length of value minus 1.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort ToUInt16(ILongIndexedContainer<byte> value, ulong startIndex)
        {
            return (ushort)ToInt16(value, startIndex);
        }

        /// <summary>
        /// Returns a 32-bit unsigned integer converted from four bytes at a specified position
        /// in a ILongIndexedContainer of bytes.
        /// </summary>
        /// <param name="value">A ILongIndexedContainer of bytes to parse.</param>
        /// <param name="startIndex">The starting position within value.</param>
        /// <returns>A 32-bit unsigned integer formed by four bytes beginning at startIndex.</returns>
        /// <exception cref="ArgumentNullException">value is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">startIndex is less than zero or greater than the length of value minus 1.</exception>
        /// <exception cref="ArgumentException">startIndex is greater than or equal to the length of value minus 3, and is less
        /// than or equal to the length of value minus 1.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ToUInt32(ILongIndexedContainer<byte> value, ulong startIndex)
        {
            return (uint)ToInt32(value, startIndex);
        }

        /// <summary>
        /// Returns a 64-bit unsigned integer converted from eight bytes at a specified position
        /// in a ILongIndexedContainer of bytes.
        /// </summary>
        /// <param name="value">A ILongIndexedContainer of bytes to parse.</param>
        /// <param name="startIndex">The starting position within value.</param>
        /// <returns>A 64-bit unsigned integer formed by the eight bytes beginning at startIndex.</returns>
        /// <exception cref="ArgumentNullException">value is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">startIndex is less than zero or greater than the length of value minus 1.</exception>
        /// <exception cref="ArgumentException">startIndex is greater than or equal to the length of value minus 7, and is less
        /// than or equal to the length of value minus 1.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ToUInt64(ILongIndexedContainer<byte> value, ulong startIndex)
        {
            return (ulong)ToInt64(value, startIndex);
        }

        /// <summary>
        /// Returns a single-precision floating point number converted from four bytes at
        /// a specified position in a ILongIndexedContainer of bytes.
        /// </summary>
        /// <param name="value">A ILongIndexedContainer of bytes to parse.</param>
        /// <param name="startIndex">The starting position within value.</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">value is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">startIndex is less than zero or greater than the length of value minus 1.</exception>
        /// <exception cref="ArgumentException">startIndex is greater than or equal to the length of value minus 3, and is less
        /// than or equal to the length of value minus 1. </exception>
        [SecuritySafeCritical, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static float ToSingle(ILongIndexedContainer<byte> value, ulong startIndex)
        {
            int num = ToInt32(value, startIndex);
            return *(float*)(&num);
        }

        /// <summary>
        /// Returns a double-precision floating point number converted from eight bytes at
        /// a specified position in a ILongIndexedContainer of bytes.
        /// </summary>
        /// <param name="value">A ILongIndexedContainer of bytes to parse.</param>
        /// <param name="startIndex">The starting position within value.</param>
        /// <returns>A double precision floating point number formed by eight bytes beginning at startIndex.</returns>
        /// <exception cref="ArgumentNullException">value is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">startIndex is less than zero or greater than the length of value minus 1.</exception>
        /// <exception cref="ArgumentException">startIndex is greater than or equal to the length of value minus 7, and is less
        /// than or equal to the length of value minus 1.</exception>
        [SecuritySafeCritical, MethodImpl(MethodImplOptions.AggressiveInlining)]

        public unsafe static double ToDouble(ILongIndexedContainer<byte> value, ulong startIndex)
        {
            long num = ToInt64(value, startIndex);
            return *(double*)(&num);
        }

        /// <summary>
        /// Returns a Boolean value converted from the byte at a specified position in a
        /// ILongIndexedContainer of bytes.
        /// </summary>
        /// <param name="value">A ILongIndexedContainer of bytes to parse.</param>
        /// <param name="startIndex">The starting position within value.</param>
        /// <returns>true if the byte at startIndex in value is nonzero; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException">value is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">startIndex is less than zero or greater than the length of value minus 1.</exception>

        public static bool ToBoolean(ILongIndexedContainer<byte> value, ulong startIndex)
        {
            if (!value.IsCreated)
            {
                throw new ArgumentNullException("value");
            }

            if (startIndex < 0 || startIndex >= (ulong)value.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex), $"{nameof(startIndex)} is out of range at {startIndex} on a length of {value.Count}");
            }
            if (value[(long)startIndex] != 0)
            {
                return true;
            }
            return false;
        }
    }
}

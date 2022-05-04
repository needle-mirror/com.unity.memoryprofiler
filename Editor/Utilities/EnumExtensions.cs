using System;
using System.Runtime.CompilerServices;
using System.Text;
#if !CSHARP_7_3_OR_NEWER
using Unity.Collections.LowLevel.Unsafe;
#endif

namespace Unity.MemoryProfiler.Editor.Extensions
{
    internal static class EnumExtensions
    {
#if CSHARP_7_3_OR_NEWER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#else
        [MethodImpl((MethodImplOptions)256)]
#endif
        internal static ulong GetValueUnsigned<TEnum>(this TEnum enumValue) where TEnum :
#if CSHARP_7_3_OR_NEWER
        unmanaged, Enum
#else
        struct
#endif
        {
            unsafe
            {
#if CSHARP_7_3_OR_NEWER
                switch (sizeof(TEnum))
                {
                    case 1:
                        return (*(byte*)(&enumValue));
                    case 2:
                        return (*(ushort*)(&enumValue));
                    case 4:
                        return (*(uint*)(&enumValue));
                    case 8:
                        return (*(ulong*)(&enumValue));
                    /* default can't happen but will flag in coverage tests if missing */
                    default:
                        throw new Exception("Size does not match a known Enum backing type.");
                }
#else
                switch (UnsafeUtility.SizeOf<TEnum>())
                {
                    case 1:
                    {
                        byte value = 0;
                        UnsafeUtility.CopyStructureToPtr(ref enumValue, &value);
                        return value;
                    }
                    case 2:
                    {
                        ushort value = 0;
                        UnsafeUtility.CopyStructureToPtr(ref enumValue, &value);
                        return value;
                    }
                    case 4:
                    {
                        uint value = 0;
                        UnsafeUtility.CopyStructureToPtr(ref enumValue, &value);
                        return value;
                    }
                    case 8:
                    {
                        ulong value = 0UL;
                        UnsafeUtility.CopyStructureToPtr(ref enumValue, &value);
                        return value;
                    }
                    /* default can't happen but will flag in coverage tests if missing */
                    default:
                        throw new Exception("Size does not match a known Enum backing type.");
                }
#endif
            }
        }

#if CSHARP_7_3_OR_NEWER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#else
        [MethodImpl((MethodImplOptions)256)]
#endif
        internal static long GetValue<TEnum>(this TEnum enumValue) where TEnum :
#if CSHARP_7_3_OR_NEWER
        unmanaged, Enum
#else
        struct
#endif
        {
            unsafe
            {
#if CSHARP_7_3_OR_NEWER
                switch (sizeof(TEnum))
                {
                    case 1:
                        return (*(sbyte*)(&enumValue));
                    case 2:
                        return (*(short*)(&enumValue));
                    case 4:
                        return (*(int*)(&enumValue));
                    case 8:
                        return (*(long*)(&enumValue));
                    /* default can't happen but will flag in coverage tests if missing */
                    default:
                        throw new Exception("Size does not match a known Enum backing type.");
                }
#else
                switch (UnsafeUtility.SizeOf<TEnum>())
                {
                    case 1:
                    {
                        sbyte value = 0;
                        UnsafeUtility.CopyStructureToPtr(ref enumValue, &value);
                        return value;
                    }
                    case 2:
                    {
                        short value = 0;
                        UnsafeUtility.CopyStructureToPtr(ref enumValue, &value);
                        return value;
                    }
                    case 4:
                    {
                        int value = 0;
                        UnsafeUtility.CopyStructureToPtr(ref enumValue, &value);
                        return value;
                    }
                    case 8:
                    {
                        long value = 0UL;
                        UnsafeUtility.CopyStructureToPtr(ref enumValue, &value);
                        return value;
                    }
                    /* default can't happen but will flag in coverage tests if missing */
                    default:
                        throw new Exception("Size does not match a known Enum backing type.");
                }
#endif
            }
        }

#if CSHARP_7_3_OR_NEWER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#else
        [MethodImpl((MethodImplOptions)256)]
#endif
        internal static TEnum ConvertToEnum<TEnum>(byte enumValue) where TEnum :
#if CSHARP_7_3_OR_NEWER
        unmanaged, Enum
#else
        struct
#endif
        {
            unsafe
            {
#if CSHARP_7_3_OR_NEWER
                if (sizeof(TEnum) == 1)
                    return (*(TEnum*)(&enumValue));
                throw new Exception("Size does not match Enum backing type.");
#else
                if (UnsafeUtility.SizeOf<TEnum>() == 1)
                {
                    TEnum value = default(TEnum);
                    UnsafeUtility.CopyStructureToPtr(ref enumValue, &value);
                    return value;
                }
                throw new Exception("Size does not match Enum backing type.");
#endif
            }
        }

#if CSHARP_7_3_OR_NEWER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#else
        [MethodImpl((MethodImplOptions)256)]
#endif
        internal static TEnum ConvertToEnum<TEnum>(short enumValue) where TEnum :
#if CSHARP_7_3_OR_NEWER
        unmanaged, Enum
#else
        struct
#endif
        {
            unsafe
            {
#if CSHARP_7_3_OR_NEWER
                if (sizeof(TEnum) == 2)
                    return (*(TEnum*)(&enumValue));
                throw new Exception("Size does not match Enum backing type.");
#else
                if (UnsafeUtility.SizeOf<TEnum>() == 2)
                {
                    TEnum value = default(TEnum);
                    UnsafeUtility.CopyStructureToPtr(ref enumValue, &value);
                    return value;
                }
                throw new Exception("Size does not match Enum backing type.");
#endif
            }
        }

#if CSHARP_7_3_OR_NEWER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#else
        [MethodImpl((MethodImplOptions)256)]
#endif
        internal static TEnum ConvertToEnum<TEnum>(int enumValue) where TEnum :
#if CSHARP_7_3_OR_NEWER
        unmanaged, Enum
#else
        struct
#endif
        {
            unsafe
            {
#if CSHARP_7_3_OR_NEWER
                if (sizeof(TEnum) == 4)
                    return (*(TEnum*)(&enumValue));
                throw new Exception("Size does not match Enum backing type.");
#else
                if (UnsafeUtility.SizeOf<TEnum>() == 4)
                {
                    TEnum value = default(TEnum);
                    UnsafeUtility.CopyStructureToPtr(ref enumValue, &value);
                    return value;
                }
                throw new Exception("Size does not match Enum backing type.");
#endif
            }
        }

#if CSHARP_7_3_OR_NEWER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#else
        [MethodImpl((MethodImplOptions)256)]
#endif
        internal static TEnum ConvertToEnum<TEnum>(long enumValue) where TEnum :
#if CSHARP_7_3_OR_NEWER
        unmanaged, Enum
#else
        struct
#endif
        {
            unsafe
            {
#if CSHARP_7_3_OR_NEWER
                if (sizeof(TEnum) == 8)
                    return (*(TEnum*)(&enumValue));
                throw new Exception("Size does not match Enum backing type.");
#else
                if (UnsafeUtility.SizeOf<TEnum>() == 8)
                {
                    TEnum value = default(TEnum);
                    UnsafeUtility.CopyStructureToPtr(ref enumValue, &value);
                    return value;
                }
                throw new Exception("Size does not match Enum backing type.");
#endif
            }
        }
    }
}

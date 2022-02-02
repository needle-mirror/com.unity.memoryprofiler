using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MemoryProfiler.Editor.Format
{
    [Flags]
    internal enum CaptureFlags : uint
    {
        ManagedObjects = 1 << 0,
        NativeObjects = 1 << 1,
        NativeAllocations = 1 << 2,
        NativeAllocationSites = 1 << 3,
        NativeStackTraces = 1 << 4,
    }

    internal class MetaData
    {
        const string k_UnknownPlatform = "Unknown Platform";
        const string k_UnknownProductName = "Unknown Project";
        const string k_UnknownUnityVersion = "Unknown";
        [NonSerialized]
        public string Content;
        [NonSerialized]
        public string Platform;
        public Texture2D Screenshot;
        public uint SessionGUID;
        [NonSerialized]
        public string ProductName;
        [NonSerialized]
        internal ProfileTargetInfo? TargetInfo = null;
        [NonSerialized]
        internal ProfileTargetMemoryStats? TargetMemoryStats = null;

        [NonSerialized] // to be shown in UI as e.g. $"Unity version '{MetaData.UnityVersion}'" to clarify that this is not the project version
        public string UnityVersion;
        public const uint InvalidSessionGUID = 0;
        public MetaData(IFileReader reader)
        {
            unsafe
            {
                Checks.CheckEquals(true, reader.HasOpenFile);

                //common meta path
                var op = reader.Read(QueriedSnapshot.EntryType.Metadata_UserMetadata, 0, 1, Allocator.TempJob);
                using (var legacyBuffer = op.Result)
                {
                    if (reader.FormatVersion >= FormatVersion.ProfileTargetInfoAndMemStatsVersion)
                    {
                        ProfileTargetInfo info;
                        reader.ReadUnsafe(EntryType.ProfileTarget_Info, &info, UnsafeUtility.SizeOf<ProfileTargetInfo>(), 0, 1);
                        TargetInfo = info;

                        ProfileTargetMemoryStats memStats;
                        reader.ReadUnsafe(EntryType.ProfileTarget_MemoryStats, &memStats, UnsafeUtility.SizeOf<ProfileTargetMemoryStats>(), 0, 1);
                        TargetMemoryStats = memStats;

                        Deserialize(legacyBuffer, info, this);
                    }
                    else
                    {
                        DeserializeLegacyMetadata(legacyBuffer, this);
                    }
                }
            }
        }

        unsafe static void DeserializeLegacyMetadata(DynamicArray<byte> buffer, MetaData meta)
        {
            meta.SessionGUID = InvalidSessionGUID;
            meta.ProductName = k_UnknownProductName;
            meta.UnityVersion = k_UnknownUnityVersion;
            if (buffer.Count == 0)
            {
                meta.Content = "";
                meta.Platform = k_UnknownPlatform;
                meta.Screenshot = null;
                return;
            }
            byte* bufferPtr = (byte*)buffer.GetUnsafePtr();
            int contentLength = *(int*)bufferPtr;
            long offset = 0;
            offset += sizeof(int);
            if (contentLength == 0)
                meta.Content = "";
            else
            {
                meta.Content = new string('A', contentLength);
                int copySize = sizeof(char) * contentLength;
                fixed(char* cntPtr = meta.Content)
                {
                    UnsafeUtility.MemCpy(cntPtr, bufferPtr + offset, copySize);
                }

                offset += copySize;
                if (offset >= buffer.Count)
                    return;
            }

            contentLength = *(int*)(bufferPtr + offset);
            offset += sizeof(int);

            if (contentLength == 0)
                meta.Platform = k_UnknownPlatform;
            else
            {
                meta.Platform = new string('A', contentLength);
                int copySize = sizeof(char) * contentLength;
                fixed(char* cntPtr = meta.Platform)
                {
                    UnsafeUtility.MemCpy(cntPtr, bufferPtr + offset, copySize);
                }

                offset += copySize;
                if (offset >= buffer.Count)
                    return;
            }

            contentLength = *(int*)(bufferPtr + offset);
            offset += sizeof(int);

            if (contentLength == 0)
                meta.Screenshot = null;
            else
            {
                byte[] pixels = new byte[contentLength]; //texturePixels
                fixed(byte* pxPtr = pixels)
                {
                    UnsafeUtility.MemCpy(pxPtr, bufferPtr + offset, contentLength);
                }
                offset += contentLength;

                int width = *(int*)(bufferPtr + offset);
                offset += sizeof(int);
                int height = *(int*)(bufferPtr + offset);
                offset += sizeof(int);
                int format = *(int*)(bufferPtr + offset);
                offset += sizeof(int);

                meta.Screenshot = new Texture2D(width, height, (TextureFormat)format, false);
                meta.Screenshot.LoadRawTextureData(pixels);
                meta.Screenshot.Apply();
            }
        }

        unsafe static void Deserialize(DynamicArray<byte> legacyDataBuffer, ProfileTargetInfo targetInfo, MetaData meta)
        {
            byte* bufferPtr = (byte*)legacyDataBuffer.GetUnsafePtr();
            int contentLength = bufferPtr != null ? *(int*)bufferPtr : 0;

            if (contentLength == 0)
                meta.Content = "";
            else
            {
                long offset = sizeof(int);
                meta.Content = new string('A', contentLength);
                int copySize = sizeof(char) * contentLength;
                fixed(char* cntPtr = meta.Content)
                {
                    UnsafeUtility.MemCpy(cntPtr, bufferPtr + offset, copySize);
                }
            }

            meta.Platform = targetInfo.RuntimePlatform.ToString();
            meta.SessionGUID = targetInfo.SessionGUID;
            meta.ProductName = targetInfo.ProductName;
            meta.UnityVersion = targetInfo.UnityVersion;
        }
    }

    [Flags]
    internal enum ObjectFlags
    {
        IsDontDestroyOnLoad = 0x1,
        IsPersistent = 0x2,
        IsManager = 0x4,
    }

    [Flags]
    internal enum TypeFlags
    {
        kNone = 0,
        kValueType = 1 << 0,
        kArray = 1 << 1,
        kArrayRankMask = unchecked((int)0xFFFF0000)
    }


    internal struct VirtualMachineInformation
    {
        public int PointerSize { get; internal set; }
        public int ObjectHeaderSize { get; internal set; }
        public int ArrayHeaderSize { get; internal set; }
        public int ArrayBoundsOffsetInHeader { get; internal set; }
        public int ArraySizeOffsetInHeader { get; internal set; }
        public int AllocationGranularity { get; internal set; }
    }

    [StructLayout(LayoutKind.Sequential, Size = 260)]
    internal unsafe struct ProfileTargetMemoryStats : IEquatable<ProfileTargetMemoryStats>
    {
        const int k_FreeBlockPowOf2BucketCount = 32;
        const int k_PaddingSize = 32;

        public readonly ulong TotalVirtualMemory;
        public readonly ulong TotalUsedMemory;
        public readonly ulong TotalReservedMemory;
        public readonly ulong TempAllocatorUsedMemory;
        public readonly ulong GraphicsUsedMemory;
        public readonly ulong AudioUsedMemory;
        public readonly ulong GcHeapUsedMemory;
        public readonly ulong GcHeapReservedMemory;
        public readonly ulong ProfilerUsedMemory;
        public readonly ulong ProfilerReservedMemory;
        public readonly ulong MemoryProfilerUsedMemory;
        public readonly ulong MemoryProfilerReservedMemory;
        public readonly uint FreeBlockBucketCount;
        fixed uint m_FreeBlockBuckets[k_FreeBlockPowOf2BucketCount];
        fixed byte m_Padding[k_PaddingSize];

        public bool Equals(ProfileTargetMemoryStats other)
        {
            unsafe
            {
                fixed(void* freeBlocks = m_FreeBlockBuckets)
                return TotalVirtualMemory == other.TotalVirtualMemory
                    && TotalUsedMemory == other.TotalUsedMemory
                    && TempAllocatorUsedMemory == other.TempAllocatorUsedMemory
                    && TotalReservedMemory == other.TotalReservedMemory
                    && GraphicsUsedMemory == other.GraphicsUsedMemory
                    && AudioUsedMemory == other.AudioUsedMemory
                    && GcHeapUsedMemory == other.GcHeapUsedMemory
                    && GcHeapReservedMemory == other.GcHeapReservedMemory
                    && ProfilerUsedMemory == other.ProfilerUsedMemory
                    && ProfilerReservedMemory == other.ProfilerReservedMemory
                    && MemoryProfilerUsedMemory == other.MemoryProfilerUsedMemory
                    && MemoryProfilerReservedMemory == other.MemoryProfilerReservedMemory
                    && FreeBlockBucketCount == other.FreeBlockBucketCount
                    && UnsafeUtility.MemCmp(freeBlocks,  other.m_FreeBlockBuckets, sizeof(uint) * k_FreeBlockPowOf2BucketCount) == 0;
            }
        }
    };

    [StructLayout(LayoutKind.Sequential, Size = 512)]
    internal unsafe struct ProfileTargetInfo : IEquatable<ProfileTargetInfo>
    {
        const int k_UnityVersionBufferSize = 16;
        const int k_ProductNameBufferSize = 256;
        // decrease value when adding new members to target info
        const int k_FormatPaddingSize = 192;

        public readonly uint SessionGUID;
        public readonly RuntimePlatform RuntimePlatform;
        public readonly GraphicsDeviceType GraphicsDeviceType;
        public readonly ulong TotalPhysicalMemory;
        public readonly ulong TotalGraphicsMemory;
        public readonly ScriptingImplementation ScriptingBackend;
        public readonly double TimeSinceStartup;
        readonly uint m_UnityVersionLength;
        fixed byte m_UnityVersionBuffer[k_UnityVersionBufferSize];
        readonly uint m_ProductNameLength;
        fixed byte m_ProductNameBuffer[k_ProductNameBufferSize];
        // space for later expansion of the format
        fixed byte m_Padding[k_FormatPaddingSize];

        public string UnityVersion
        {
            get
            {
                fixed(byte* ptr = m_UnityVersionBuffer)
                return MakeStringFromBuffer(ptr, m_UnityVersionLength);
            }
        }
        public string ProductName
        {
            get
            {
                fixed(byte* ptr = m_ProductNameBuffer)
                return MakeStringFromBuffer(ptr, m_ProductNameLength);
            }
        }

        unsafe string MakeStringFromBuffer(byte* srcPtr, uint length)
        {
            if (length == 0)
                return string.Empty;

            string str = new string('A', (int)length);
            fixed(char* dstPtr = str)
            {
                UnsafeUtility.MemCpyStride(dstPtr, UnsafeUtility.SizeOf<char>(), srcPtr, UnsafeUtility.SizeOf<byte>(), UnsafeUtility.SizeOf<byte>(), (int)length);
            }

            return str;
        }

        public bool Equals(ProfileTargetInfo other)
        {
            unsafe
            {
                fixed(void* prod = m_ProductNameBuffer, version = m_UnityVersionBuffer)
                return SessionGUID == other.SessionGUID
                    && RuntimePlatform == other.RuntimePlatform
                    && GraphicsDeviceType == other.GraphicsDeviceType
                    && TotalPhysicalMemory == other.TotalPhysicalMemory
                    && TotalGraphicsMemory == other.TotalGraphicsMemory
                    && ScriptingBackend == other.ScriptingBackend
                    && TimeSinceStartup == other.TimeSinceStartup
                    && m_UnityVersionLength == other.m_UnityVersionLength
                    && m_ProductNameLength == other.m_ProductNameLength
                    && UnsafeUtility.MemCmp(prod, other.m_ProductNameBuffer, m_ProductNameLength) == 0
                    && UnsafeUtility.MemCmp(version, other.m_UnityVersionBuffer, m_UnityVersionLength) == 0;
            }
        }
    };
}

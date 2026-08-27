using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using Unity.Profiling.Memory;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MemoryProfiler.Editor.Format
{
    internal class MetaData
    {
        const string k_UnknownPlatform = "Unknown Platform";
        const string k_UnknownProductName = "Unknown Project";
        const string k_UnknownUnityVersion = "Unknown";
        [NonSerialized]
        public string Description;
        [NonSerialized]
        public string Platform;
        public string PlatformExtra;
        public bool IsEditorCapture;

        public uint SessionGUID { get; internal set; }
        [NonSerialized]
        public string ProductName;
        [NonSerialized]
        internal ProfileTargetInfo? TargetInfo = null;
        [NonSerialized]
        internal ProfileTargetMemoryStats? TargetMemoryStats = null;

        [NonSerialized] // to be shown in UI as e.g. $"Unity version '{MetaData.UnityVersion}'" to clarify that this is not the project version
        public string UnityVersion;
        public int UnityVersionMajor = -1;
        public int UnityVersionMinor = -1;
        public int UnityVersionPatch = -1;
        public UnityReleaseType UnityVersionReleaseType = UnityReleaseType.Unknown;
        public int UnityVersionPrerelease = 0;
        public enum UnityReleaseType
        {
            Unknown,
            Alpha, // letter a
            Beta, // letter b
            Full, // or Final, letter f
        }

        public const uint InvalidSessionGUID = 0;
        public CaptureFlags CaptureFlags;

        /// <summary>
        ///
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="importLegacyScreenshot">Set to true if you want legacy screenshots to be imported.
        /// If you do, remember to DestroyImmediate the generated texture that gets stored in <see cref="LegacyFormatEncodedScreenshot"/>.</param>
        public MetaData(IFileReader reader)
        {
            unsafe
            {
                Checks.CheckEquals(true, reader.HasOpenFile);

                //common meta path, step 1: read the capture flags in
                {
                    using var flagsOp = reader.Read(EntryType.Metadata_CaptureFlags, 0, 1, Allocator.TempJob);
                    using var flags = flagsOp.Result;
                    CaptureFlags = flags.Reinterpret<CaptureFlags>()[0]; // This data is uint32_t, but so is the enum
                }

                //common meta path, step 2: read the legacy metadata
                using var op = reader.Read(EntryType.Metadata_UserMetadata, 0, 1, Allocator.TempJob);

                using (var legacyBuffer = op.Result)
                {
                    if (reader.FormatVersion >= FormatVersion.ProfileTargetInfoAndMemStatsVersion)
                    {
                        ProfileTargetInfo info;
                        reader.ReadUnsafe(EntryType.ProfileTarget_Info, &info, sizeof(ProfileTargetInfo), 0, 1);
                        TargetInfo = info;

                        ProfileTargetMemoryStats memStats;
                        reader.ReadUnsafe(EntryType.ProfileTarget_MemoryStats, &memStats, sizeof(ProfileTargetMemoryStats), 0, 1);
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
            meta.IsEditorCapture = false;
            if (buffer.Count == 0)
            {
                meta.Description = "";
                meta.Platform = k_UnknownPlatform;
                return;
            }
            byte* bufferPtr = buffer.GetUnsafeTypedPtr();
            int contentLength = *(int*)bufferPtr;
            long offset = 0;
            offset += sizeof(int);
            if (contentLength == 0)
                meta.Description = "";
            else
            {
                meta.Description = new string('A', contentLength);
                int copySize = sizeof(char) * contentLength;
                fixed (char* cntPtr = meta.Description)
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
                fixed (char* cntPtr = meta.Platform)
                {
                    UnsafeUtility.MemCpy(cntPtr, bufferPtr + offset, copySize);
                }
                meta.IsEditorCapture = meta.Platform.Contains("Editor");

                offset += copySize;
                if (offset >= buffer.Count)
                    return;
            }
        }

        unsafe static void Deserialize(DynamicArray<byte> legacyDataBuffer, ProfileTargetInfo targetInfo, MetaData meta)
        {
            byte* bufferPtr = legacyDataBuffer.GetUnsafeTypedPtr();
            int contentLength = bufferPtr != null ? *(int*)bufferPtr : 0;

            long offset = 0;
            offset += sizeof(int);
            if (contentLength == 0)
                meta.Description = "";
            else
            {
                meta.Description = new string('A', contentLength);
                int copySize = sizeof(char) * contentLength;
                fixed (char* cntPtr = meta.Description)
                {
                    UnsafeUtility.MemCpy(cntPtr, bufferPtr + offset, copySize);
                }

                offset += copySize;
                if (offset >= legacyDataBuffer.Count)
                    return;
            }

            contentLength = bufferPtr != null ? *(int*)(bufferPtr + offset) : 0;
            offset += sizeof(int);

            if (contentLength != 0)
                meta.PlatformExtra = "";
            else
            {
                meta.PlatformExtra = new string('A', contentLength);
                int copySize = sizeof(char) * contentLength;
                fixed (char* cntPtr = meta.Platform)
                {
                    UnsafeUtility.MemCpy(cntPtr, bufferPtr + offset, copySize);
                }
            }

            meta.Platform = targetInfo.RuntimePlatform.ToString();
            meta.IsEditorCapture = meta.Platform.Contains("Editor");
            meta.SessionGUID = targetInfo.SessionGUID;
            meta.ProductName = targetInfo.ProductName;
            meta.UnityVersion = targetInfo.UnityVersion;
            var snapshotUnityVersion = meta.UnityVersion.Split('.');
            if (snapshotUnityVersion.Length > 0 && int.TryParse(snapshotUnityVersion[0], out var versionMajor))
                meta.UnityVersionMajor = versionMajor;
            if (snapshotUnityVersion.Length > 1 && int.TryParse(snapshotUnityVersion[1], out var versionMinor))
                meta.UnityVersionMinor = versionMinor;
            if (snapshotUnityVersion.Length > 2)
            {
                var patchVersion = snapshotUnityVersion[2].AsSpan();
                for (int i = 0; i < patchVersion.Length; i++)
                {
                    // patch version numbers contain letters like "f1" so only read until the digits end
                    if (!char.IsDigit(patchVersion[i]))
                    {
                        meta.UnityVersionReleaseType = patchVersion[i] switch
                        {
                            'a' => UnityReleaseType.Alpha,
                            'b' => UnityReleaseType.Beta,
                            'f' => UnityReleaseType.Full,
                            _ => UnityReleaseType.Unknown
                        };
                        patchVersion = patchVersion.Slice(0, i);
                    }
                }
                if (patchVersion.Length > 0 && int.TryParse(patchVersion, out var versionPatch))
                    meta.UnityVersionPatch = versionPatch;
                else
                    meta.UnityVersionPatch = meta.UnityVersionReleaseType == UnityReleaseType.Unknown ? -1 : 0;

                if (meta.UnityVersionReleaseType is not UnityReleaseType.Unknown)
                {
                    var prereleaseVersion = snapshotUnityVersion[2].AsSpan().Slice(patchVersion.Length);
                    while (prereleaseVersion.Length > 0 && !char.IsDigit(prereleaseVersion[0]))
                    {
                        prereleaseVersion = prereleaseVersion.Slice(1);
                    }
                    if (prereleaseVersion.Length > 0 && int.TryParse(prereleaseVersion, out var versionPrerelease))
                        meta.UnityVersionPrerelease = versionPrerelease;
                    else
                        meta.UnityVersionPrerelease = 0;
                }
            }
        }

        public bool UnityVersionEqualOrNewer(int major, int minor = 0, int patch = 0, UnityReleaseType releaseType = UnityReleaseType.Full, int preRelease = 1)
        {
            return UnityVersionMajor > major
                || (UnityVersionMajor == major && UnityVersionMinor > minor)
                || (UnityVersionMajor == major && UnityVersionMinor == minor && UnityVersionPatch > patch)
                || (UnityVersionMajor == major && UnityVersionMinor == minor && UnityVersionPatch == patch && UnityVersionReleaseType > releaseType)
                || (UnityVersionMajor == major && UnityVersionMinor == minor && UnityVersionPatch == patch && UnityVersionReleaseType == releaseType && UnityVersionPrerelease >= preRelease);
        }

        /// <summary>
        /// Only returns true if the version fits within the specific Major and Minor version stream and in that stream is equal or newer.
        /// </summary>
        /// <param name="major"></param>
        /// <param name="minor"></param>
        /// <param name="patch"></param>
        /// <param name="releaseType"></param>
        /// <param name="preRelease"></param>
        /// <returns></returns>
        public bool UnityVersionEqualOrNewerWithinMinorRelease(int major, int minor = 0, int patch = 0, UnityReleaseType releaseType = UnityReleaseType.Full, int preRelease = 1)
        {
            return (UnityVersionMajor == major && UnityVersionMinor == minor && UnityVersionPatch > patch)
                   || (UnityVersionMajor == major && UnityVersionMinor == minor && UnityVersionPatch == patch && UnityVersionReleaseType > releaseType)
                   || (UnityVersionMajor == major && UnityVersionMinor == minor && UnityVersionPatch == patch && UnityVersionReleaseType == releaseType && UnityVersionPrerelease >= preRelease);
        }

        public bool UnityVersionEqualOrOlder(int major, int minor)
        {
            return UnityVersionMajor < major || (UnityVersionMajor == major
                                                 && UnityVersionMinor <= minor);
        }
        public bool UnityVersionEqualOrOlder(int major, int minor, int patch)
        {
            return UnityVersionMajor < major || (UnityVersionMajor == major
                   && (UnityVersionMinor < minor || (UnityVersionMinor == minor && UnityVersionPatch <= patch)));
        }
    }

    [Flags]
    internal enum ObjectFlags
    {
        IsDontDestroyOnLoad = 0x1,
        IsPersistent = 0x2,
        IsManager = 0x4,
        IsRoot = 0x8,
        /// <summary>
        /// Only used for some algorithms to flag objects while working on them.
        /// Remember to always unset the flag once you're done with it.
        /// </summary>
        IsTemporarilyMarked = 0x10,
    }

    [Flags]
    internal enum TypeFlags
    {
        kNone = 0,
        kValueType = 1 << 0,
        kArray = 1 << 1,
        // CoreCLR-populated type-shape bits. Mono/IL2CPP write 0. Format-neutral (bit-tested, no version bump needed).
        kInterface = 1 << 2,
        kAbstract = 1 << 3,
        kSealed = 1 << 4,
        kEnum = 1 << 5,
        kNested = 1 << 6,
        kExplicitLayout = 1 << 7,
        kArrayRankMask = unchecked((int)0xFFFF0000)
    }


    /// <summary>
    /// Exact on-disk layout of the <see cref="EntryType.Metadata_VirtualMachineInformation"/> entry
    /// (six <c>uint32</c>s, unchanged since the earliest supported format version). Used ONLY as the
    /// target of the raw file read in <see cref="CachedSnapshot"/>'s constructor. Never add fields here
    /// or read it wider than <c>sizeof(VirtualMachineInformationFileData)</c> — <c>FileReader</c>
    /// asserts the requested read size against the entry's own recorded on-disk size
    /// (<c>Checks.CheckEquals(rangeByteSize, bufferLength)</c>), so any mismatch throws immediately on
    /// every snapshot. Derived/computed values (that don't come from the file) belong on
    /// <see cref="VirtualMachineInformation"/> instead, set after this raw struct is read.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct VirtualMachineInformationFileData
    {
        public readonly uint PointerSize;
        public readonly uint ObjectHeaderSize;
        public readonly uint ArrayHeaderSize;
        public readonly uint ArrayBoundsOffsetInHeader;
        public readonly uint ArraySizeOffsetInHeader;
        public readonly uint AllocationGranularity;
        public VirtualMachineInformationFileData(
            uint pointerSize,
            uint objectHeaderSize = 0,
            uint arrayHeaderSize = 0,
            uint arrayBoundsOffsetInHeader = 0,
            uint arraySizeOffsetInHeader = 0,
            uint allocationGranularity = 0)
        {
            PointerSize = pointerSize;
            ObjectHeaderSize = objectHeaderSize;
            ArrayHeaderSize = arrayHeaderSize;
            ArrayBoundsOffsetInHeader = arrayBoundsOffsetInHeader;
            ArraySizeOffsetInHeader = arraySizeOffsetInHeader;
            AllocationGranularity = allocationGranularity;
        }
    }

    internal readonly struct VirtualMachineInformation
    {
        public readonly uint PointerSize { get; }
        public readonly uint ObjectHeaderSize { get; }
        public readonly uint ArrayHeaderSize { get; }
        public readonly uint ArrayBoundsOffsetInHeader { get; }
        public readonly uint ArraySizeOffsetInHeader { get; }
        public readonly uint AllocationGranularity { get; }

        /// <summary>
        /// Byte stride between successive out-of-line array-bounds entries (Mono/IL2CPP layout only;
        /// unused and left at its default for CoreCLR, see
        /// <see cref="ManagedHeapArrayDataTools.IsCoreClrArrayLayout"/>). Computed once via
        /// <see cref="GetArrayBoundsStride"/> during construction (<see cref="CachedSnapshot(IFileReader, bool)"/>), not read
        /// from the file — kept here so the array-crawling hot path (<see cref="ManagedHeapArrayDataTools.GetArrayInfo"/>
        /// and friends, called once per dimension of every array in the heap) is a plain field read
        /// instead of re-deriving the per-backend stride from <c>ScriptingBackend</c> on every call.
        /// </summary>
        public readonly ulong ArrayBoundsStride { get; }

        public VirtualMachineInformation(
            VirtualMachineInformationFileData rawVmInfo,
            ScriptingImplementation? scriptingBackend = null) :
            this(
                rawVmInfo.PointerSize,
                rawVmInfo.ObjectHeaderSize,
                rawVmInfo.ArrayHeaderSize,
                rawVmInfo.ArrayBoundsOffsetInHeader,
                rawVmInfo.ArraySizeOffsetInHeader,
                rawVmInfo.AllocationGranularity,
                scriptingBackend)
        { }

        public VirtualMachineInformation(
            uint pointerSize,
            uint objectHeaderSize = 0,
            uint arrayHeaderSize = 0,
            uint arrayBoundsOffsetInHeader = 0,
            uint arraySizeOffsetInHeader = 0,
            uint allocationGranularity = 0,
            ScriptingImplementation? scriptingBackend = null)
        {
            PointerSize = pointerSize;
            ObjectHeaderSize = objectHeaderSize;
            ArrayHeaderSize = arrayHeaderSize;
            ArrayBoundsOffsetInHeader = arrayBoundsOffsetInHeader;
            ArraySizeOffsetInHeader = arraySizeOffsetInHeader;
            AllocationGranularity = allocationGranularity;
            ArrayBoundsStride = GetArrayBoundsStride(scriptingBackend, pointerSize);
        }

        /// <summary>
        /// Byte stride between successive out-of-line bounds entries (Mono/IL2CPP only; CoreCLR
        /// never reaches this, see <see cref="ManagedHeapArrayDataTools.IsCoreClrArrayLayout"/>).
        /// Mono's <see cref="MonoArrayBounds"/> is <c>{int32 length, int32 lower_bound}</c> = 8 bytes regardless of architecture bitness
        /// (<c>mono_array_size_t</c> is a fixed 32-bit type unless <c>MONO_BIG_ARRAYS</c> is defined,
        /// which Unity does not define). IL2CPP's <c>Il2CppArrayBounds</c> is
        /// <c>{uintptr_t length, int32 lower_bound}</c> (<c>il2cpp-object-internals.h</c>): its
        /// <c>length</c> field is pointer-sized, so the struct is 16 bytes on a 64-bit IL2CPP build
        /// (8-byte length + 4-byte lower_bound, padded to the 8-byte pointer alignment) but only 8
        /// bytes on a 32-bit build (4 + 4, already aligned) — i.e. <c>2 * pointerSize</c> either way.
        /// <paramref name="scriptingBackend"/> is null on captures older than
        /// <c>FormatVersion.ProfileTargetInfoAndMemStatsVersion</c> (see
        /// <see cref="MetaData.TargetInfo"/>). Those captures predate this distinction and CANNOT be
        /// assumed to be IL2CPP — 64-bit Mono standalone players (Windows/macOS/Linux) predate this
        /// format version by years, so a null backend falls back to the historical fixed 8-byte
        /// stride, same as before this per-backend stride existed. This means a rank &gt;= 2 array on
        /// a pre-v11 64-bit IL2CPP capture still reads with the wrong stride — fixing that needs a way
        /// to actually determine the legacy backend, which is a separate, larger piece of work, not a
        /// guess made here.
        ///
        /// Called exactly once per snapshot, from the constructor which is called from the snapshot's
        /// <see cref="CachedSnapshot(IFileReader, bool)"/> Constructor once
        /// <see cref="MetaData.TargetInfo"/>is available, and cached on
        /// <see cref="ArrayBoundsStride"/> — the array-crawling call sites
        /// below (invoked once per dimension of every array on the heap) read that field directly
        /// rather than re-deriving it here on every call.
        /// </summary>
        internal static ulong GetArrayBoundsStride(ScriptingImplementation? scriptingBackend, uint pointerSize)
        {
            return scriptingBackend == ScriptingImplementation.IL2CPP
                ? 2ul * pointerSize
                : 8ul;
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = 264)]
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
        // + 4 bytes for alignment

        public bool Equals(ProfileTargetMemoryStats other)
        {
            if (k_PaddingSize == 32) // Stand-in for static_assert with "not all code paths return a value". Update this function if a new member has been added.
                unsafe
                {
                    fixed (void* freeBlocks = m_FreeBlockBuckets)
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
                            && UnsafeUtility.MemCmp(freeBlocks, other.m_FreeBlockBuckets, sizeof(uint) * k_FreeBlockPowOf2BucketCount) == 0;
                }
        }

        internal bool CompareFreeBlockBucketsToFragmentionInfo(NativeArray<int> FreeBlockBuckets)
        {
            unsafe
            {
                fixed (void* freeBlocks = m_FreeBlockBuckets)
                    return FreeBlockBucketCount == FreeBlockBuckets.Length
                           && UnsafeUtility.MemCmp(freeBlocks, FreeBlockBuckets.GetUnsafePtr(), sizeof(uint) * k_FreeBlockPowOf2BucketCount) == 0;
            }
        }
    };

    [StructLayout(LayoutKind.Sequential, Size = 512)]
    internal unsafe struct ProfileTargetInfo : IEquatable<ProfileTargetInfo>
    {
#if UNITY_6000_7_OR_NEWER
        public const ScriptingImplementation CoreCLRScriptingImplementation = ScriptingImplementation.CoreCLR;
#else
#pragma warning disable CS0618 // Type or member is obsolete
        public const ScriptingImplementation CoreCLRScriptingImplementation = ScriptingImplementation.CoreCLR;
#pragma warning restore CS0618 // Type or member is obsolete
#endif

        const int k_UnityVersionBufferSize = 16;
        const int k_ProductNameBufferSize = 256;
        // decrease value when adding new members to target info
        const int k_FormatPaddingSize = 184;

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
        public readonly float ScalableBufferManagerWidth;
        public readonly float ScalableBufferManagerHeight;
        // space for later expansion of the format
        fixed byte m_Padding[k_FormatPaddingSize];

        public string UnityVersion
        {
            get
            {
                fixed (byte* ptr = m_UnityVersionBuffer)
                    return MakeStringFromBuffer(ptr, m_UnityVersionLength);
            }
        }
        public string ProductName
        {
            get
            {
                fixed (byte* ptr = m_ProductNameBuffer)
                    return MakeStringFromBuffer(ptr, m_ProductNameLength);
            }
        }

        string MakeStringFromBuffer(byte* srcPtr, uint length)
        {
            if (length == 0)
                return string.Empty;

            ReadOnlySpan<byte> tmp = new(srcPtr, (int)length);
            var str = System.Text.Encoding.UTF8.GetString(tmp);

            return str;
        }

        public bool Equals(ProfileTargetInfo other)
        {
            if (k_FormatPaddingSize == 184) // Stand-in for static_assert with "not all code paths return a value". Update this function if a new member has been added.
                unsafe
                {
                    fixed (void* prod = m_ProductNameBuffer, version = m_UnityVersionBuffer)
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
                            && UnsafeUtility.MemCmp(version, other.m_UnityVersionBuffer, m_UnityVersionLength) == 0
                            && ScalableBufferManagerWidth == other.ScalableBufferManagerWidth
                            && ScalableBufferManagerHeight == other.ScalableBufferManagerHeight
                            ;
                }
        }
    };
}

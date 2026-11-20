using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
#if LIFECYCLE_APIS_AVAILABLE
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.MemoryProfiler.Editor.Containers
{
    internal static class ByteBufferReader
    {
        public static unsafe T ReadValue<T>(ref int pos, Byte* bytes) where T : unmanaged
        {
            T val = *(T*)(bytes + pos);
            pos += sizeof(T);
            pos = Align(pos);
            return val;
        }

        public static unsafe string ReadString(ref int pos, Byte* bytes)
        {
            var strLen = ReadValue<Int32>(ref pos, bytes);
            if (strLen == 0)
                return String.Empty;

            string val = new string((sbyte*)bytes, pos, strLen);
            pos += strLen;
            pos = Align(pos);
            return val;
        }

        static unsafe T ReadValueUnchecked<T>(ref int pos, Byte* bytes) where T : unmanaged
        {
            T val = *(T*)(bytes + pos);
            pos += sizeof(T);
            return val;
        }

        public static unsafe T[] ReadArray<T>(ref int pos, Byte* bytes, int count) where T : unmanaged
        {
            T[] arr = new T[count];
            for (int i = 0; i < count; i++)
            {
                arr[i] = ReadValueUnchecked<T>(ref pos, bytes);
            }
            pos = Align(pos);

            return arr;
        }

        static int Align(int pos)
        {
            int alignCheck = pos % 4;
            if (alignCheck != 0)
                pos += 4 - alignCheck;

            return pos;
        }
    }
#if LIFECYCLE_APIS_AVAILABLE
    [NoAutoStaticsCleanup]
#endif
    internal static class MetaDataHelpers
    {
        enum AvailableMetaDataTypes
        {
            AudioClip,
            Mesh,
            RenderTexture,
            Shader,
            Texture,
            Texture2D,
            VisualEffectAsset,
            VFXManager,
        }

        static Dictionary<string, AvailableMetaDataTypes> s_TypeNameToEnum = new()
        {
            { "AudioClip", AvailableMetaDataTypes.AudioClip },
            { "Mesh", AvailableMetaDataTypes.Mesh },
            { "RenderTexture", AvailableMetaDataTypes.RenderTexture },
            { "Shader", AvailableMetaDataTypes.Shader },
            { "Texture", AvailableMetaDataTypes.Texture },
            { "Texture2D", AvailableMetaDataTypes.Texture2D },
            { "VisualEffectAsset", AvailableMetaDataTypes.VisualEffectAsset },
            { "VFXManager", AvailableMetaDataTypes.VFXManager },
        };

        static List<(string, string)> MetaDataStringFactory(ILongIndexedContainer<byte> bytes, AvailableMetaDataTypes key, CachedSnapshot cs)
        {
            switch (key)
            {
                case AvailableMetaDataTypes.AudioClip: return new AudioClipMetaData(bytes).GenerateInfoStrings(cs);
                case AvailableMetaDataTypes.Mesh: return new MeshMetaData(bytes).GenerateInfoStrings(cs);
                case AvailableMetaDataTypes.RenderTexture: return new RenderTextureMetaData(bytes).GenerateInfoStrings(cs);
                case AvailableMetaDataTypes.Shader: return new ShaderMetaData(bytes).GenerateInfoStrings(cs);
                case AvailableMetaDataTypes.Texture: return new TextureMetaData(bytes).GenerateInfoStrings(cs);
                case AvailableMetaDataTypes.Texture2D: return new Texture2DMetaData(bytes).GenerateInfoStrings(cs);
                case AvailableMetaDataTypes.VisualEffectAsset: return new VisualEffectAssetMetaData(bytes).GenerateInfoStrings(cs);
                case AvailableMetaDataTypes.VFXManager: return new VFXManagerMetaData(bytes).GenerateInfoStrings(cs);
                default: throw new ArgumentOutOfRangeException();
            }
        }

        internal static bool GenerateMetaDataString(CachedSnapshot cs, long nativeObjectIndex, out List<(string, string)> metaDataStrings)
        {
            if (!cs.HasNativeObjectMetaData || nativeObjectIndex == CachedSnapshot.NativeObjectEntriesCache.InvalidObjectIndex)
            {
                metaDataStrings = null;
                return false;
            }
            int typeIndex = cs.NativeObjects.NativeTypeArrayIndex[nativeObjectIndex];
            string typeName = cs.NativeTypes.TypeName[typeIndex];
            if (s_TypeNameToEnum.TryGetValue(typeName, out var key) && cs.NativeObjects.MetaData(nativeObjectIndex).Count != 0)
            {
                metaDataStrings = MetaDataStringFactory(cs.NativeObjects.MetaData(nativeObjectIndex), key, cs);
                return true;
            }
            metaDataStrings = null;
            return false;
        }

        internal static string GenerateVersionMismatchWarning(int playerVersion, int packageVersion)
        {
            return playerVersion > packageVersion ?
                $"Package format version {packageVersion} is lower than {playerVersion} read from the snapshot. Some data will likely be omitted. Update to a newer version of the memory profiler package to get all data" :
                $"Package format version {packageVersion} is greater than {playerVersion} read from the snapshot. Some data may be inaccurate.";
        }
    }

    interface IMetaDataBuffer
    {
        public bool VersionMatches();
        public List<(string, string)> GenerateInfoStrings(CachedSnapshot cachedSnapshot);
    }

    internal struct TextureMetaData : IMetaDataBuffer
    {
        const int kPackageFormatVersion = 1;
        int version;
        public TextureFormat textureFormat;
        public GraphicsFormat graphicsFormat;
        public int width;
        public int height;
        public int MipMapCount;
        Byte isReadable;
        public bool ReadWriteEnabled => isReadable == 1;

        enum ByteIndicies
        {
            IsReadable = 0,
        }

        public bool HasMipMaps()
        {
            return MipMapCount > 1;
        }

        public bool VersionMatches()
        {
            return version == kPackageFormatVersion;
        }

        public unsafe TextureMetaData(ILongIndexedContainer<byte> bytes)
        {
            byte* ptr = bytes.GetUnsafeTypedPtr();
            int pos = 0;
            version = ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
            textureFormat = (TextureFormat)ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
            graphicsFormat = (GraphicsFormat)ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
            width = ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
            height = ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
            MipMapCount = ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
            var byteArray = ByteBufferReader.ReadArray<Byte>(ref pos, ptr, 3);
            isReadable = byteArray[(int)ByteIndicies.IsReadable];
        }

        public List<(string, string)> GenerateInfoStrings(CachedSnapshot cs)
        {
            List<(string, string)> infoStrings = new List<(string, string)>();
            if (!VersionMatches())
            {
                infoStrings.Add(("Warning", MetaDataHelpers.GenerateVersionMismatchWarning(version, kPackageFormatVersion)));
            }
            infoStrings.Add(("Width", width.ToString()));
            infoStrings.Add(("Height", height.ToString()));
            infoStrings.Add(("Read/Write", ReadWriteEnabled ? "Enabled" : "Disabled"));
            infoStrings.Add(("CPU Format", textureFormat.ToString()));
            infoStrings.Add(("GPU Format", graphicsFormat.ToString()));
            infoStrings.Add(("MipMap Count", MipMapCount.ToString()));
            return infoStrings;
        }
    }

    internal struct Texture2DMetaData : IMetaDataBuffer
    {
        enum ByteIndicies
        {
            IsReadable = 0,
            MipStreaming = 1,
            LoadedMipLevel = 2
        }
        const int kPackageFormatVersion = 1;
        public int version;
        public TextureFormat TextureFormat;
        public GraphicsFormat GraphicsTextureFormat;
        public int Width;
        public int Height;
        public int MipMapCount;
        Byte isReadable;
        public bool ReadWriteEnabled => isReadable == 1;
        Byte mipStreaming;
        public bool MipMapStreamingEnabled => mipStreaming == 1;
        Byte loadedMipLevel;
        public int LoadedMipMapLevel => loadedMipLevel;

        public bool MipMapped => MipMapCount > 1;

        public ulong streamingInfoOffset;
        public int streamingInfoSize;
        public string streamingPath;

        public unsafe Texture2DMetaData(ILongIndexedContainer<byte> bytes)
        {
            byte* ptr = bytes.GetUnsafeTypedPtr();
            int pos = 0;
            version = ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
            TextureFormat = (TextureFormat)ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
            GraphicsTextureFormat = (GraphicsFormat)ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
            Width = ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
            Height = ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
            MipMapCount = ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
            var byteArray = ByteBufferReader.ReadArray<Byte>(ref pos, ptr, 3);
            isReadable = byteArray[(int)ByteIndicies.IsReadable];
            mipStreaming = byteArray[(int)ByteIndicies.MipStreaming];
            loadedMipLevel = byteArray[(int)ByteIndicies.LoadedMipLevel];
            streamingInfoSize = ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
            streamingInfoOffset = ByteBufferReader.ReadValue<UInt64>(ref pos, ptr);
            streamingPath = ByteBufferReader.ReadString(ref pos, ptr);
        }

        public List<(string, string)> GenerateInfoStrings(CachedSnapshot cachedSnapshot)
        {
            List<(string, string)> infoStrings = new List<(string, string)>();
            if (!VersionMatches())
            {
                infoStrings.Add(("Warning", MetaDataHelpers.GenerateVersionMismatchWarning(version, kPackageFormatVersion)));
            }

            infoStrings.Add(("Width", Width.ToString()));
            infoStrings.Add(("Height", Height.ToString()));
            infoStrings.Add(("Read/Write", ReadWriteEnabled ? "Enabled" : "Disabled"));
            infoStrings.Add(("CPU Format", TextureFormat.ToString()));
            infoStrings.Add(("GPU Format", GraphicsTextureFormat.ToString()));
            infoStrings.Add(("MipMap Count", MipMapCount.ToString()));
            infoStrings.Add(("MipMap Streaming", MipMapStreamingEnabled ? "Enabled" : "Disabled"));
            infoStrings.Add(("Loaded MipMap", LoadedMipMapLevel.ToString()));
            if (!cachedSnapshot.MetaData.IsEditorCapture)
            {
                infoStrings.Add(("Streaming Info Offset", streamingInfoOffset.ToString()));
                infoStrings.Add(("Streaming Info Size", streamingInfoSize.ToString()));
                infoStrings.Add(("Path", streamingPath));
            }
            return infoStrings;
        }

        public bool VersionMatches()
        {
            return version == kPackageFormatVersion;
        }
    }

    internal struct MeshMetaData : IMetaDataBuffer
    {
        enum ByteIndicies
        {
            IndexFormat = 0,
            BonesPerVertex = 1,
            IsReadable = 2,
        }
        const int kPackageFormatVersion = 1;
        const int kNumberOfVertexAttributes = 14;
        public int version;

        public ulong IndexCount;
        public ulong SubMeshCount;

        public int VertexCount;
        public int Bones;

        Byte indexFormat;
        public IndexFormat IndexFormat => (IndexFormat)indexFormat;

        Byte bonesPerVertex;
        public int BonesPerVertex => bonesPerVertex;

        Byte isReadable;
        public bool ReadWriteEnabled => isReadable == 1;


        Byte[] channelformats;
        Byte[] channeldimensions;
        Byte[] channelStreams;

        public unsafe MeshMetaData(ILongIndexedContainer<byte> bytes)
        {
            byte* ptr = bytes.GetUnsafeTypedPtr();
            int pos = 0;
            version = ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
            VertexCount = ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
            IndexCount = ByteBufferReader.ReadValue<UInt64>(ref pos, ptr);
            SubMeshCount = ByteBufferReader.ReadValue<UInt64>(ref pos, ptr);
            Bones = ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
            var byteArray = ByteBufferReader.ReadArray<Byte>(ref pos, ptr, 3);
            indexFormat = byteArray[(int)ByteIndicies.IndexFormat];
            bonesPerVertex = byteArray[(int)ByteIndicies.BonesPerVertex];
            isReadable = byteArray[(int)ByteIndicies.IsReadable];
            channelformats = ByteBufferReader.ReadArray<Byte>(ref pos, ptr, kNumberOfVertexAttributes);
            channeldimensions = ByteBufferReader.ReadArray<Byte>(ref pos, ptr, kNumberOfVertexAttributes);
            channelStreams = ByteBufferReader.ReadArray<Byte>(ref pos, ptr, kNumberOfVertexAttributes);
        }

        public VertexAttributeFormat GetChannelFormat(VertexAttribute channel)
        {
            return (VertexAttributeFormat)channelformats[(int)channel];
        }

        public int GetChannelDimensions(VertexAttribute channel)
        {
            return channeldimensions[(int)channel];
        }

        public int GetChannelStreams(VertexAttribute channel)
        {
            return channelStreams[(int)channel];
        }

        public bool ValidChannel(VertexAttribute channel)
        {
            return channeldimensions[(int)channel] != 0;
        }

        public List<(string, string)> GenerateInfoStrings(CachedSnapshot cs)
        {
            List<(string, string)> infoStrings = new List<(string, string)>();
            if (!VersionMatches())
            {
                infoStrings.Add(("Warning", MetaDataHelpers.GenerateVersionMismatchWarning(version, kPackageFormatVersion)));
            }

            infoStrings.Add(("Vertices", VertexCount.ToString()));
            foreach (VertexAttribute attribute in Enum.GetValues(typeof(VertexAttribute)))
            {
                if (ValidChannel(attribute))
                {
                    //the inspector replaces Texcoord with UV so we are just matching that behaviour here
                    infoStrings.Add((Enum.GetName(typeof(VertexAttribute), attribute).Replace("TexCoord", "UV"), $"{GetChannelFormat(attribute)} x {GetChannelDimensions(attribute)}, stream {GetChannelStreams(attribute)}"));
                }
            }
            infoStrings.Add(("Indices", $"{IndexCount}, {IndexFormat}"));
            infoStrings.Add(("Read/Write", ReadWriteEnabled ? "Enabled" : "Disabled"));
            infoStrings.Add(("Bones", Bones.ToString()));
            infoStrings.Add(("Max Bones/Vertex", bonesPerVertex.ToString()));
            infoStrings.Add(("SubMesh Count", SubMeshCount.ToString()));
            return infoStrings;
        }

        public bool VersionMatches()
        {
            return version == kPackageFormatVersion;
        }
    }

    internal struct RenderTextureMetaData : IMetaDataBuffer
    {
        // TODO: record "original" mip count: UUM-11336

#pragma warning disable 0649
        const int kPackageFormatVersion = 3;
        public int version;

        public int Width;
        public int Height;
        public int ScaledWidth;
        public int ScaledHeight;
        public GraphicsFormat ColorFormat;
        public GraphicsFormat DepthStencilFormat;
        public int MipMapCount;
        public RenderTextureMemoryless MemoryLessMode;
        public FilterMode TexFilterMode;
        public int AnisoLevel;
        public int AntiAliasing;
        public TextureDimension TexDimension;
        public TextureWrapMode WrapModeU;
        public TextureWrapMode WrapModeV;
        public TextureWrapMode WrapModeW;
        public bool RandomWriteEnabled;
        public bool AutoGenerateMips;
        public bool UseDynamicScaling;
        public bool UseResolveColorSurface;
        public bool UseResolveDepthSurface;
#pragma warning restore 0649

        [Flags]
        enum BitIndices : byte
        {
            RandomWriteEnabled = 1 << 0,
            AutoGenerateMips = 1 << 1,
            UseDynamicScaling = 1 << 2,
            UseResolveColorSurface = 1 << 3,
            UseResolveDepthSurface = 1 << 4,
        }

        struct MetaDataPackedV2
        {
#pragma warning disable 0649
            public int Width;
            public int Height;
            public short colorFormat;
            public short depthStencilFormat;
            public sbyte mipmapCount;
            public sbyte memorylessMode;
            public sbyte anisoFilteringMode;
            public sbyte anisoFilteringLevel;
            public sbyte antiAliasingLevel;
            public sbyte texDimensionality;
            public sbyte wrapModeU;
            public sbyte wrapModeV;
            public sbyte wrapModeW;
            public BitIndices boolFlags;
#pragma warning restore 0649
        }

        public unsafe RenderTextureMetaData(ILongIndexedContainer<byte> bytes)
        {
            ScaledWidth = ScaledHeight = 0;

            byte* ptr = bytes.GetUnsafeTypedPtr();
            int pos = 0;
            version = ByteBufferReader.ReadValue<Int32>(ref pos, ptr);

            if (version == 1)
            {
                // Version 1 is just the base class Texture, so limited info.
                pos += 4; // Skip format info that doesn't apply to RTs

                ColorFormat = (GraphicsFormat)ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
                Width = ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
                Height = ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
                MipMapCount = ByteBufferReader.ReadValue<Int32>(ref pos, ptr);

                // Unused in v1, but need setting
                DepthStencilFormat = 0;
                MemoryLessMode = 0;
                TexFilterMode = 0;
                AnisoLevel = AntiAliasing = 0;
                TexDimension = 0;
                WrapModeU = WrapModeV = WrapModeW = 0;
                RandomWriteEnabled = AutoGenerateMips = UseDynamicScaling = UseResolveColorSurface = UseResolveDepthSurface = false;
            }
            else
            {
                MetaDataPackedV2 metaData = ByteBufferReader.ReadValue<MetaDataPackedV2>(ref pos, ptr);
                Width = metaData.Width;
                Height = metaData.Height;
                ColorFormat = (GraphicsFormat)metaData.colorFormat;
                DepthStencilFormat = (GraphicsFormat)metaData.depthStencilFormat;
                MipMapCount = metaData.mipmapCount;
                MemoryLessMode = (RenderTextureMemoryless)metaData.memorylessMode;
                TexFilterMode = (FilterMode)metaData.anisoFilteringMode;
                AnisoLevel = metaData.anisoFilteringLevel;
                AntiAliasing = metaData.antiAliasingLevel;
                TexDimension = (TextureDimension)metaData.texDimensionality;
                WrapModeU = (TextureWrapMode)metaData.wrapModeU;
                WrapModeV = (TextureWrapMode)metaData.wrapModeV;
                WrapModeW = (TextureWrapMode)metaData.wrapModeW;
                RandomWriteEnabled = metaData.boolFlags.HasFlag(BitIndices.RandomWriteEnabled);
                AutoGenerateMips = metaData.boolFlags.HasFlag(BitIndices.AutoGenerateMips);
                UseDynamicScaling = metaData.boolFlags.HasFlag(BitIndices.UseDynamicScaling);
                UseResolveColorSurface = metaData.boolFlags.HasFlag(BitIndices.UseResolveColorSurface);
                UseResolveDepthSurface = metaData.boolFlags.HasFlag(BitIndices.UseResolveDepthSurface);
            }
        }

        public List<(string, string)> GenerateInfoStrings(CachedSnapshot cachedSnapshot)
        {
            List<(string, string)> infoStrings = new List<(string, string)>();
            if (!VersionMatches())
            {
                infoStrings.Add(("Warning", MetaDataHelpers.GenerateVersionMismatchWarning(version, kPackageFormatVersion)));
            }

            infoStrings.Add(("Width", Width.ToString()));
            infoStrings.Add(("Height", Height.ToString()));

            if (version > 1)
            {
                infoStrings.Add(("Dynamic Scaling", UseDynamicScaling ? "Enabled" : "Disabled"));
                if (UseDynamicScaling)
                {
                    ScaledWidth = (int)Math.Ceiling(Width * cachedSnapshot.MetaData.TargetInfo.Value.ScalableBufferManagerWidth);
                    ScaledHeight = (int)Math.Ceiling(Height * cachedSnapshot.MetaData.TargetInfo.Value.ScalableBufferManagerHeight);

                    infoStrings.Add(("Scaled Width", ScaledWidth.ToString()));
                    infoStrings.Add(("Scaled Height", ScaledHeight.ToString()));
                }
            }

            infoStrings.Add(("Color Format", ColorFormat.ToString()));
            if (version > 1)
            {
                infoStrings.Add(("Depth Stencil Format", DepthStencilFormat.ToString()));
                infoStrings.Add(("Generate MipMaps", AutoGenerateMips ? "Automatic" : "Manual"));
            }

            infoStrings.Add(("MipMaps", MipMapCount.ToString()));
            if (version > 1)
            {
                infoStrings.Add(("Memoryless Mode", MemoryLessMode.ToString()));
                infoStrings.Add(("Texture Filtering", TexFilterMode.ToString()));
                infoStrings.Add(("Anisotropic Filtering", AnisoLevel.ToString()));
                infoStrings.Add(("Anti-aliasing Level", AntiAliasing.ToString()));
                infoStrings.Add(("Dimensionality", TexDimension.ToString()));

                infoStrings.Add(("Texture Wrapping U", WrapModeU.ToString()));
                infoStrings.Add(("Texture Wrapping V", WrapModeV.ToString()));
                infoStrings.Add(("Texture Wrapping W", WrapModeW.ToString()));

                infoStrings.Add(("Random Write", RandomWriteEnabled ? "Enabled" : "Disabled"));
            }

            if (version > 2)
            {
                string colour = UseResolveColorSurface ? "Color" : "";
                string depth = UseResolveDepthSurface ? "Depth" : "";
                string comma = (UseResolveColorSurface && UseResolveDepthSurface) ? ", " : "";
                string resolveSurfaces =
                    (UseResolveColorSurface || UseResolveDepthSurface) ? colour + comma + depth : "None";
                infoStrings.Add(("Resolve Surface(s)", resolveSurfaces));
            }
            return infoStrings;
        }

        public bool VersionMatches()
        {
            return version == kPackageFormatVersion;
        }
    }

    internal struct AudioClipMetaData : IMetaDataBuffer
    {
#pragma warning disable 0649
        const int kPackageFormatVersion = 1;
        public int Version;

        public float LengthSecs;
        public int Frequency;
        public int Bitrate;
        public int Samples;
        public AudioCompressionFormat CompressionFormat;
        public AudioDataLoadState LoadState;
        public int Channels;
        public AudioClipLoadType LoadType;
        public bool LoadInBackground;
        public bool IsAmbisonic;
#pragma warning restore 0649

        [Flags]
        enum BitIndices : byte
        {
            LoadInBackground = 1 << 0,
            IsAmbisonic = 1 << 1,
        }

        struct MetaDataPacked
        {
#pragma warning disable 0649
            public float LengthSecs;
            public Int32 Frequency;
            public Int32 Bitrate;
            public Int32 Samples;
            public sbyte CompressionFormat;
            public sbyte LoadState;
            public sbyte Channels;
            public sbyte LoadType;
            public BitIndices boolFlags;
#pragma warning restore 0649
        }

        public unsafe AudioClipMetaData(ILongIndexedContainer<byte> bytes)
        {
            byte* ptr = bytes.GetUnsafeTypedPtr();
            int pos = 0;
            // Version loaded separately at first, in case the rest of the layout is altered in newer versions
            Version = ByteBufferReader.ReadValue<Int32>(ref pos, ptr);

            MetaDataPacked metaData = ByteBufferReader.ReadValue<MetaDataPacked>(ref pos, ptr);
            LengthSecs = metaData.LengthSecs;
            Frequency = metaData.Frequency;
            Bitrate = metaData.Bitrate;
            Samples = metaData.Samples;
            CompressionFormat = (AudioCompressionFormat)metaData.CompressionFormat;

            // Load state enum in Native is 5 values (two types of Loading) but in Managed is only 4.
            if (metaData.LoadState > 1)
                LoadState = (AudioDataLoadState)(metaData.LoadState - 1);
            else
                LoadState = (AudioDataLoadState)metaData.LoadState;

            Channels = metaData.Channels;
            LoadType = (AudioClipLoadType)metaData.LoadType;
            LoadInBackground = metaData.boolFlags.HasFlag(BitIndices.LoadInBackground);
            IsAmbisonic = metaData.boolFlags.HasFlag(BitIndices.IsAmbisonic);
        }

        public List<(string, string)> GenerateInfoStrings(CachedSnapshot cachedSnapshot)
        {
            List<(string, string)> infoStrings = new List<(string, string)>();
            if (!VersionMatches())
            {
                infoStrings.Add(("Warning", MetaDataHelpers.GenerateVersionMismatchWarning(Version, kPackageFormatVersion)));
            }

            TimeSpan ts = TimeSpan.FromSeconds(LengthSecs);
            string LengthStr = (LengthSecs == 0xffffffff) ? "Unlimited" : String.Format("{0:00}:{1:00}.{2:000}", ts.Minutes, ts.Seconds, ts.Milliseconds);

            infoStrings.Add(("Length", LengthStr));
            infoStrings.Add(("Frequency", $"{Frequency:N0} Hz"));
            infoStrings.Add(("Bitrate", $"{Bitrate:N0} Bps"));
            infoStrings.Add(("Samples", $"{Samples:N0}"));
            infoStrings.Add(("Compression", CompressionFormat.ToString()));
            infoStrings.Add(("Load state", LoadState.ToString()));
            infoStrings.Add(("Channels", Channels == 1 ? "Mono" : Channels == 2 ? "Stereo" : (Channels - 1) + ".1"));
            infoStrings.Add(("Load Type", LoadType.ToString()));
            infoStrings.Add(("Load in background", LoadInBackground ? "Yes" : "No"));
            infoStrings.Add(("Ambisonic", IsAmbisonic ? "Yes" : "No"));

            return infoStrings;
        }

        public bool VersionMatches()
        {
            return Version == kPackageFormatVersion;
        }
    }

    internal struct ShaderMetaData : IMetaDataBuffer
    {
        const int kPackageFormatVersion = 1;
        public int Version;

        public UInt64 NumVariants;

        public unsafe ShaderMetaData(ILongIndexedContainer<byte> bytes)
        {
            byte* ptr = bytes.GetUnsafeTypedPtr();
            int pos = 0;

            Version = ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
            pos += 4; // Skip padding
            NumVariants = ByteBufferReader.ReadValue<UInt64>(ref pos, ptr);
        }

        public List<(string, string)> GenerateInfoStrings(CachedSnapshot cachedSnapshot)
        {
            List<(string, string)> infoStrings = new List<(string, string)>();
            if (!VersionMatches())
            {
                infoStrings.Add(("Warning", MetaDataHelpers.GenerateVersionMismatchWarning(Version, kPackageFormatVersion)));
            }

            infoStrings.Add(("Variants", $"{NumVariants:N0}"));

            return infoStrings;
        }

        public bool VersionMatches()
        {
            return Version == kPackageFormatVersion;
        }
    }

    internal struct VisualEffectAssetMetaData : IMetaDataBuffer
    {
        const int kPackageFormatVersion = 1;
        public int Version;
        public int BatchCount;
        public int BatchSize;
        public int CPUBuffersCount;
        public List<string> CPUBufferNames;
        public List<int> CPUBufferSizes;
        public int GPUBuffersCount;
        public List<string> GPUBufferNames;
        public List<int> GPUBufferSizes;
        public int BakedTextureSize;

        public unsafe VisualEffectAssetMetaData(ILongIndexedContainer<byte> bytes)
        {
            BatchCount = 0;
            BatchSize = 0;
            CPUBuffersCount = 0;
            CPUBufferNames = null;
            CPUBufferSizes = null;
            GPUBuffersCount = 0;
            GPUBufferNames = null;
            GPUBufferSizes = null;
            BakedTextureSize = 0;

            byte* ptr = bytes.GetUnsafeTypedPtr();
            int pos = 0;

            Version = ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
            // Other versions may use a different layout, and the counts read below drive further reads
            if (Version != kPackageFormatVersion)
                return;

            BatchCount = ByteBufferReader.ReadValue<int>(ref pos, ptr);
            if (BatchCount > 0)
            {
                BatchSize = ByteBufferReader.ReadValue<int>(ref pos, ptr);
                CPUBuffersCount = ByteBufferReader.ReadValue<int>(ref pos, ptr);
                CPUBufferNames = new List<string>(CPUBuffersCount);
                CPUBufferSizes = new List<int>(CPUBuffersCount);
                for (int i = 0; i < CPUBuffersCount; i++)
                {
                    CPUBufferNames.Add(ByteBufferReader.ReadString(ref pos, ptr));
                    CPUBufferSizes.Add(ByteBufferReader.ReadValue<int>(ref pos, ptr));
                }

                GPUBuffersCount = ByteBufferReader.ReadValue<int>(ref pos, ptr);
                GPUBufferNames = new List<string>(GPUBuffersCount);
                GPUBufferSizes = new List<int>(GPUBuffersCount);
                for (int i = 0; i < GPUBuffersCount; i++)
                {
                    GPUBufferNames.Add(ByteBufferReader.ReadString(ref pos, ptr));
                    GPUBufferSizes.Add(ByteBufferReader.ReadValue<int>(ref pos, ptr));
                }

                BakedTextureSize = ByteBufferReader.ReadValue<int>(ref pos, ptr);
            }
        }

        public bool VersionMatches()
        {
            return Version == kPackageFormatVersion;
        }

        public List<(string, string)> GenerateInfoStrings(CachedSnapshot cachedSnapshot)
        {
            List<(string, string)> infoStrings = new List<(string, string)>();

            if (!VersionMatches())
            {
                infoStrings.Add(("Warning",
                    MetaDataHelpers.GenerateVersionMismatchWarning(Version, kPackageFormatVersion)));
                return infoStrings;
            }

            infoStrings.Add(("Batch Count", BatchCount.ToString()));
            if (BatchCount > 0)
            {
                infoStrings.Add(("Batch Size", BatchSize.ToString()));

                // Only buffers with a non-zero size are listed, so count those and find the last one to use └ for it
                int lastCPUIndex = -1;
                int nonEmptyCPUBuffers = 0;
                for (int i = 0; i < CPUBuffersCount; i++)
                {
                    if (CPUBufferSizes[i] != 0) { lastCPUIndex = i; nonEmptyCPUBuffers++; }
                }
                infoStrings.Add(("CPU Buffer Count", FormatBatchedCount(nonEmptyCPUBuffers)));
                for (int i = 0; i < CPUBuffersCount; i++)
                {
                    if (CPUBufferSizes[i] != 0)
                        infoStrings.Add(($"{(i == lastCPUIndex ? "└" : "├")} {CPUBufferNames[i]}", FormatBatchedBytes(CPUBufferSizes[i])));
                }

                int lastGPUIndex = -1;
                int nonEmptyGPUBuffers = 0;
                for (int i = 0; i < GPUBuffersCount; i++)
                {
                    if (GPUBufferSizes[i] != 0) { lastGPUIndex = i; nonEmptyGPUBuffers++; }
                }
                infoStrings.Add(("GPU Buffer Count", FormatBatchedCount(nonEmptyGPUBuffers)));
                for (int i = 0; i < GPUBuffersCount; i++)
                {
                    if (GPUBufferSizes[i] != 0)
                        infoStrings.Add(($"{(i == lastGPUIndex ? "└" : "├")} {GPUBufferNames[i]}", FormatBatchedBytes(GPUBufferSizes[i])));
                }

                infoStrings.Add(("Baked Texture", FormatBatchedBytes(BakedTextureSize)));
            }
            return infoStrings;
        }

        // With several batches, show the total first, followed by the per batch breakdown
        string FormatBatchedCount(int perBatchCount)
        {
            return BatchCount == 1 ? perBatchCount.ToString() : $"{(long)BatchCount * perBatchCount} ({BatchCount} x {perBatchCount})";
        }

        string FormatBatchedBytes(int perBatchSize)
        {
            return BatchCount == 1 ? EditorUtility.FormatBytes(perBatchSize) : $"{EditorUtility.FormatBytes((long)BatchCount * perBatchSize)} ({BatchCount} x {EditorUtility.FormatBytes(perBatchSize)})";
        }
    }

    internal struct VFXManagerMetaData : IMetaDataBuffer
    {
        const int kPackageFormatVersion = 1;
        public int Version;
        public int GlobalCPUInputArgsSize;
        public int GlobalGPUInputArgsSize;
        public int GlobalIndirectArgsSize;
        public int QuadIndexBufferSize;
        public int OctagonIndexBufferSize;
        public int HexahedronIndexBufferSize;

        public unsafe VFXManagerMetaData(ILongIndexedContainer<byte> bytes)
        {
            GlobalCPUInputArgsSize = 0;
            GlobalGPUInputArgsSize = 0;
            GlobalIndirectArgsSize = 0;
            QuadIndexBufferSize = 0;
            OctagonIndexBufferSize = 0;
            HexahedronIndexBufferSize = 0;

            byte* ptr = bytes.GetUnsafeTypedPtr();
            int pos = 0;

            Version = ByteBufferReader.ReadValue<Int32>(ref pos, ptr);
            // Other versions may use a different, possibly shorter, layout
            if (Version != kPackageFormatVersion)
                return;

            GlobalCPUInputArgsSize = ByteBufferReader.ReadValue<int>(ref pos, ptr);
            GlobalGPUInputArgsSize = ByteBufferReader.ReadValue<int>(ref pos, ptr);
            GlobalIndirectArgsSize = ByteBufferReader.ReadValue<int>(ref pos, ptr);
            QuadIndexBufferSize = ByteBufferReader.ReadValue<int>(ref pos, ptr);
            OctagonIndexBufferSize = ByteBufferReader.ReadValue<int>(ref pos, ptr);
            HexahedronIndexBufferSize = ByteBufferReader.ReadValue<int>(ref pos, ptr);
        }

        public bool VersionMatches()
        {
            return Version == kPackageFormatVersion;
        }

        public List<(string, string)> GenerateInfoStrings(CachedSnapshot cachedSnapshot)
        {
            List<(string, string)> infoStrings = new List<(string, string)>();
            if (!VersionMatches())
            {
                infoStrings.Add(("Warning",
                    MetaDataHelpers.GenerateVersionMismatchWarning(Version, kPackageFormatVersion)));
                return infoStrings;
            }

            var groups = new (string label, (string name, int size)[] buffers)[]
            {
                ("Indirect Command Buffers", new[]
                {
                    ("Global CPU Input Args", GlobalCPUInputArgsSize),
                    ("Global GPU Input Args", GlobalGPUInputArgsSize),
                    ("Global Indirect Args", GlobalIndirectArgsSize),
                }),
                ("Index Buffers", new[]
                {
                    ("Quad Index Buffer", QuadIndexBufferSize),
                    ("Octagon Index Buffer", OctagonIndexBufferSize),
                    ("Hexahedron Index Buffer", HexahedronIndexBufferSize),
                }),
            };

            foreach (var group in groups)
            {
                // Only buffers with a non-zero size are listed, so count those and find the last one to use └ for it
                int lastIndex = -1;
                int nonEmptyBuffers = 0;
                for (int i = 0; i < group.buffers.Length; i++)
                {
                    if (group.buffers[i].size > 0) { lastIndex = i; nonEmptyBuffers++; }
                }
                infoStrings.Add((group.label, nonEmptyBuffers.ToString()));
                for (int i = 0; i < group.buffers.Length; i++)
                {
                    if (group.buffers[i].size > 0)
                        infoStrings.Add(($"{(i == lastIndex ? "└" : "├")} {group.buffers[i].name}", EditorUtility.FormatBytes(group.buffers[i].size)));
                }
            }

            return infoStrings;
        }
    }
}

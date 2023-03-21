using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

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

    internal static class MetaDataHelpers
    {
        enum AvailableMetaDataTypes
        {
            Texture2D,
            Mesh,
            Texture,
            RenderTexture,
        }

        static Dictionary<string, AvailableMetaDataTypes> s_TypeNameToEnum = new()
        {
            { "Texture2D", AvailableMetaDataTypes.Texture2D },
            { "Mesh", AvailableMetaDataTypes.Mesh },
            { "Texture", AvailableMetaDataTypes.Texture },
            { "RenderTexture", AvailableMetaDataTypes.RenderTexture },
        };

        static List<(string, string)> MetaDataStringFactory(DynamicArray<byte> bytes, AvailableMetaDataTypes key, CachedSnapshot cs)
        {
            switch (key)
            {
                case AvailableMetaDataTypes.Texture2D:
                    return new Texture2DMetaData(bytes).GenerateInfoStrings(cs);
                case AvailableMetaDataTypes.Mesh:
                    return new MeshMetaData(bytes).GenerateInfoStrings(cs);
                case AvailableMetaDataTypes.Texture:
                    return new TextureMetaData(bytes).GenerateInfoStrings(cs);
                case AvailableMetaDataTypes.RenderTexture:
                    return new RenderTextureMetaData(bytes).GenerateInfoStrings(cs);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        internal static bool GenerateMetaDataString(CachedSnapshot cs, int nativeObjectIndex, out List<(string, string)> metaDataStrings)
        {
            if (!cs.HasNativeObjectMetaData)
            {
                metaDataStrings = null;
                return false;
            }
            int typeIndex = cs.NativeObjects.NativeTypeArrayIndex[nativeObjectIndex];
            string typeName = cs.NativeTypes.TypeName[typeIndex];
            if (s_TypeNameToEnum.TryGetValue(typeName, out var key))
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
        public int SnapshotFormatVersion { get; }
        public int PackageFormatVersion { get; }
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

        public int SnapshotFormatVersion => kPackageFormatVersion;
        public int PackageFormatVersion => version;

        public bool VersionMatches()
        {
            return version == kPackageFormatVersion;
        }

        public unsafe TextureMetaData(DynamicArray<byte> bytes)
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

        public unsafe Texture2DMetaData(DynamicArray<byte> bytes)
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

        public int SnapshotFormatVersion => version;
        public int PackageFormatVersion => kPackageFormatVersion;

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

        public unsafe MeshMetaData(DynamicArray<byte> bytes)
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

        public int SnapshotFormatVersion => version;
        public int PackageFormatVersion => kPackageFormatVersion;

        public bool VersionMatches()
        {
            return version == kPackageFormatVersion;
        }
    }

    internal struct RenderTextureMetaData : IMetaDataBuffer
    {
        // TODO: record "original" mip count: UUM-11336

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
        }

        public unsafe RenderTextureMetaData(DynamicArray<byte> bytes)
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

        public int SnapshotFormatVersion => version;
        public int PackageFormatVersion => kPackageFormatVersion;

        public bool VersionMatches()
        {
            return version == kPackageFormatVersion;
        }
    }
}

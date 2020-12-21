using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Diagnostics;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.Format.ModularMemorySnapshot
{
    public struct CaptureMetaData : IDisposable
    {
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct TextureDesc
        {
            public int Width;
            public int Height;
            public TextureFormat Format;
        }

        GCHandle m_Platform;
        GCHandle m_Content;
        GCHandle m_Texture;

        public string Platform { get { return m_Platform.Target as string; } }
        public string Content { get { return m_Content.Target as string; } }
        public Texture2D Screenshot
        {
            get { return m_Texture.Target as Texture2D; }
            /*can be set internally as formats above 8 store the file separately*/
            internal set
            {
                if (m_Texture.IsAllocated)
                    m_Texture.Free();

                m_Texture = GCHandle.Alloc(value);
            }
        }

        public CaptureMetaData(NativeArray<byte> binaryData)
        {
            unsafe
            {
                Screenshot = null;

                if (binaryData.Length == 0)
                    return;

                int offset = 0;
                var dataPtr = (byte*)binaryData.GetUnsafePtr();
                string cnt;
                if (!ReadString(dataPtr, binaryData.Length, ref offset, out cnt))
                    return;
                m_Content = GCHandle.Alloc(cnt);
                dataPtr += offset;
                string plat;
                if (!ReadString(dataPtr, binaryData.Length, ref offset, out plat))
                    return;
                m_Platform = GCHandle.Alloc(plat);
                dataPtr += offset;
                Texture2D tex;
                if (!ReadTexture(dataPtr, binaryData.Length, ref offset, out tex))
                    return;
                Screenshot = tex;
            }
        }

        unsafe static bool ReadString(byte* binaryData, int length, ref int offset, out string output)
        {
            output = null;
            if (offset + sizeof(int) >= length)
                return false;

            int dataLength = *(int*)binaryData;
            binaryData = binaryData + sizeof(int);
            offset += sizeof(int);

            if (dataLength == 0)
                return false;

            var str = new string('_', dataLength);
            fixed(char* dstPtr = str)
            {
                UnsafeUtility.MemCpy(dstPtr, binaryData, dataLength * sizeof(char));
            }
            offset += dataLength * sizeof(char);
            output = str;
            return true;
        }

        unsafe static bool ReadTexture(byte* binaryData, int length, ref int offset, out Texture2D texture)
        {
            texture = null;

            if (offset + sizeof(int) >= length)
                return false;

            int initialOffset = offset;
            int dataLength = *(int*)binaryData;
            binaryData += sizeof(int);
            if (dataLength < sizeof(int) * 3)
                return false;
            TextureDesc desc = *(TextureDesc*)binaryData;
            binaryData += sizeof(TextureDesc);
            offset += sizeof(TextureDesc);

            int remainingData = dataLength - (offset - initialOffset);
            if (remainingData < 1)
                return false; //we don't have enough data to read

            var texData = new byte[remainingData];

            fixed(byte* texDataPtr = texData)
            {
                UnsafeUtility.MemCpy(texDataPtr, binaryData, remainingData);
            }

            var tex = new Texture2D(desc.Width, desc.Height, desc.Format, false);
            tex.LoadRawTextureData(texData);
            return true;
        }

        public void Dispose()
        {
            if (m_Content.IsAllocated)
                m_Content.Free();
            if (m_Platform.IsAllocated)
                m_Platform.Free();
            if (m_Texture.IsAllocated)
                m_Texture.Free();
        }
    }
}

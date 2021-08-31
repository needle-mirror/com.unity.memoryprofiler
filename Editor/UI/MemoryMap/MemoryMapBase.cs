using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.MemoryProfiler.Editor.UI.MemoryMap
{
    internal static class MathExt
    {
        public static ulong Clamp(this ulong value, ulong min, ulong max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }

    //selection begin, selection end, selection data is valid, unused
    internal struct MemoryMapSelectionData
    {
        public static MemoryMapSelectionData k_Default { get { return new MemoryMapSelectionData() { m_Data = new Vector4(0, 0, -1, -1) }; } }
        public Vector4 Data { get { return m_Data; }  }

        public float SelectionBegin { get { return m_Data.x; } set { m_Data.x = value; UpdateValidState(); } }
        public float SelectionEnd { get { return m_Data.y; } set { m_Data.y = value; UpdateValidState(); } }

        void UpdateValidState()
        {
            if (m_Data.x == m_Data.y || m_Data.x == 0.0f || m_Data.y == 0.0f)
                m_Data.z = -1;
            else
                m_Data.z = 1;
        }

        Vector4 m_Data;
    }

    internal abstract class MemoryMapBase : IDisposable
    {
        public const int k_RowCacheSize = 512;       // Height of texture that store visible rows + extra data to preload data.
        protected ulong m_BytesInRow = 8 * 1024 * 1024;
        protected ulong m_HighlightedAddrMin = ulong.MinValue;
        protected ulong m_HighlightedAddrMax = ulong.MinValue;
        Material m_Material;
        Texture2D m_Texture;
        public Rect MemoryMapRect = new Rect();

        long m_MinCachedRow = 0;
        long m_MaxCachedRow = 0;
        int m_MinCachedGroup = 0;
        int m_MaxCachedGroup = 0;

        public bool ForceRepaint { get; set; }

        NativeArray<Color32> m_RawTexture;

        protected List<MemoryGroup> m_Groups = new List<MemoryGroup>();


        public struct AddressLabel
        {
            public string Text;
            public Rect TextRect;
        }

        public struct EntryRange
        {
            public int Begin;
            public int End;
        }

        protected enum EntryColors
        {
            Region = 0,
            Allocation = 1,
            Object = 2,
            VirtualMemory = 3
        }

        protected Color32[] m_ColorNative;
        protected Color32[] m_ColorManaged;
        protected Color32[] m_ColorManagedStack;

        public struct MemoryGroup
        {
            public ulong AddressBegin;
            public ulong AddressEnd;
            public float RowsOffsetY;
            public ulong RowsOffset;
            public ulong RowsCount;
            public AddressLabel[] Labels;
            public float MinY { get { return RowsOffsetY; } }
            public float MaxY { get { return RowsOffsetY + (float)RowsCount * Styles.MemoryMap.RowPixelHeight; } }
            public long RowsStart { get { return (long)RowsOffset; } }
            public long RowsEnd { get { return (long)(RowsOffset + RowsCount); } }
        }

        protected class CappedNativeObjectsColection : CachedSnapshot.ISortedEntriesCache
        {
            public CappedNativeObjectsColection(CachedSnapshot.SortedNativeObjectsCache nativeObjects)
            {
                m_Count = nativeObjects.Count;
                m_Addresses = new ulong[m_Count];
                m_Sizes = new ulong[m_Count];
                for (int i = 0; i < m_Count; i++)
                {
                    m_Addresses[i] = nativeObjects.Address(i);
                    m_Sizes[i] = nativeObjects.Size(i);
                    if (i > 0 && m_Addresses[i - 1] + m_Sizes[i - 1] > m_Addresses[i])
                    {
                        // Native Object Memory can be reported as one lump sum but might be allocated in non-contiguous chunks
                        // Until we have more precise reporting in the backend, clamp sizes down wherever their memory is clearly exceeding logical amounts,
                        // i.e. clamp the last address of assumed-to-be-contiguous memory of the previous object to where this object starts
                        m_Sizes[i - 1] = m_Addresses[i] - m_Addresses[i - 1];

                        // TODO: Once we have better reporting, uncomment this and make sure all NativeObject types are reporting reasonable base sizes
                        // Debug.LogError("Object with impossible contiguous base size detected");
                    }
                }
            }
            int m_Count;
            public int Count => m_Count;
            ulong[] m_Addresses;
            public ulong Address(int index) => m_Addresses[index];

            public void Preload() { }

            ulong[] m_Sizes;
            public ulong Size(int index) => m_Sizes[index];
            public void SetSize(int index, ulong value) => m_Sizes[index] = value;
        }

        string m_MaterialName;
        const string k_MemoryMapTextureSlot = "MemoryMapBackingTextureSlot";
        Texture2D[] m_TextureSlots;

        public MemoryMapBase(int textureSlots = 1, string materialName = "Resources/MemoryMap")
        {
            ForceRepaint = false;

            m_MaterialName = materialName;

            m_TextureSlots = new Texture2D[textureSlots];

            m_ColorNative = new Color32[Enum.GetNames(typeof(EntryColors)).Length];
            m_ColorNative[(int)EntryColors.Allocation] = ProfilerColors.currentColors[(int)RegionType.Native];
            m_ColorNative[(int)EntryColors.Region] = new Color32((byte)(m_ColorNative[(int)EntryColors.Allocation].r * 0.6f), (byte)(m_ColorNative[(int)EntryColors.Allocation].g * 0.6f), (byte)(m_ColorNative[(int)EntryColors.Allocation].b * 0.6f), byte.MaxValue);
            m_ColorNative[(int)EntryColors.Object] = new Color(1, 1, 0, 1.0f);
            m_ColorNative[(int)EntryColors.VirtualMemory] = new Color32((byte)(m_ColorNative[(int)EntryColors.Allocation].r * 0.3f), (byte)(m_ColorNative[(int)EntryColors.Allocation].g * 0.3f), (byte)(m_ColorNative[(int)EntryColors.Allocation].b * 0.3f), byte.MaxValue);

            m_ColorManaged = new Color32[Enum.GetNames(typeof(EntryColors)).Length];
            // The managed "Allocation" category is used for Managed Virtual Machine Regions
            m_ColorManaged[(int)EntryColors.Allocation] = new Color(0.05f, 0.20f, 0.35f, 1.0f);
            m_ColorManaged[(int)EntryColors.Region] = new Color(0.05f, 0.40f, 0.55f, 1.0f);
            m_ColorManaged[(int)EntryColors.Object] = new Color(0.45f, 0.80f, 0.95f, 1.0f);//ProfilerColors.currentColors[(int)RegionType.Managed];
            m_ColorManaged[(int)EntryColors.VirtualMemory] = new Color32((byte)(m_ColorManaged[(int)EntryColors.Allocation].r * 0.3f), (byte)(m_ColorManaged[(int)EntryColors.Allocation].g * 0.3f), (byte)(m_ColorManaged[(int)EntryColors.Allocation].b * 0.3f), byte.MaxValue);

            // Not actually supported yet so, color these red to raise, well, red flags...
            m_ColorManagedStack = new Color32[Enum.GetNames(typeof(EntryColors)).Length];
            m_ColorManagedStack[(int)EntryColors.Allocation] = Color.red;// ProfilerColors.currentColors[(int)RegionType.ManagedStack];
            m_ColorManagedStack[(int)EntryColors.Region] = Color.red;// ProfilerColors.currentColors[(int)RegionType.ManagedStack];
            m_ColorManagedStack[(int)EntryColors.Object] = Color.red;//new Color(0.87f, 0.29f, 0.68f, 1.0f);
            m_ColorManagedStack[(int)EntryColors.VirtualMemory] = Color.red;//new Color32((byte)(m_ColorManagedStack[(int)EntryColors.Allocation].r * 0.3f), (byte)(m_ColorManagedStack[(int)EntryColors.Allocation].g * 0.3f), (byte)(m_ColorManagedStack[(int)EntryColors.Allocation].b * 0.3f), byte.MaxValue);
        }

        protected void PrepareSortedData(CachedSnapshot.ISortedEntriesCache[] caches)
        {
            ProgressBarDisplay.UpdateProgress(0.0f, "Sorting data ...");

            long entriesCount = 0;
            long entriesProcessed = 0;

            for (int i = 0; i < caches.Length; ++i)
            {
                entriesCount += caches[i].Count;
            }

            if (entriesCount > 0)
            {
                for (int i = 0; i < caches.Length; ++i)
                {
                    CachedSnapshot.ISortedEntriesCache cache = caches[i];
                    cache.Preload();
                    entriesProcessed += cache.Count;

                    ProgressBarDisplay.UpdateProgress((float)((100 * entriesProcessed) / entriesCount) / 100.0f);
                }
            }
        }

        public void AddGroup(ulong beginRow, ulong endRow)
        {
            float       prevGroupSpace = 1.0f;

            MemoryGroup prevGroup;

            if (m_Groups.Count == 0)
            {
                prevGroup.RowsOffsetY = 0;
                prevGroup.RowsOffset = 0;
                prevGroup.RowsCount = 0;
                prevGroupSpace = 0.25f;
            }
            else
            {
                prevGroup = m_Groups[m_Groups.Count - 1];
            }

            MemoryGroup group;
            group.AddressBegin  = (beginRow / m_BytesInRow) * m_BytesInRow;
            group.AddressEnd    = ((endRow + m_BytesInRow - 1) / m_BytesInRow) * m_BytesInRow;
            group.RowsOffsetY   = prevGroup.RowsOffsetY + prevGroup.RowsCount * Styles.MemoryMap.RowPixelHeight +  prevGroupSpace * Styles.MemoryMap.HeaderHeight;
            group.RowsOffset    = prevGroup.RowsOffset + prevGroup.RowsCount;
            group.RowsCount     = (group.AddressEnd - group.AddressBegin) / m_BytesInRow;

            group.Labels = new AddressLabel[1 + (group.RowsCount - 1) / Styles.MemoryMap.SubAddressStepInRows];
            group.Labels[0].TextRect = new Rect(16.0f, group.RowsOffsetY - 0.25f * Styles.MemoryMap.HeaderHeight, Styles.MemoryMap.HeaderWidth, Styles.MemoryMap.HeaderHeight);
            group.Labels[0].Text = String.Format("0x{0:X15}", group.AddressBegin);

            for (int i = 1; i < group.Labels.Length; ++i)
            {
                group.Labels[i].TextRect = new Rect(4.0f, group.RowsOffsetY - 0.90f * Styles.MemoryMap.HeaderHeight + (float)i * Styles.MemoryMap.SubAddressStepInRows * Styles.MemoryMap.RowPixelHeight, Styles.MemoryMap.HeaderWidth, Styles.MemoryMap.HeaderHeight);
                group.Labels[i].Text = String.Format("0x{0:X15}", group.AddressBegin + (ulong)i * Styles.MemoryMap.SubAddressStepInRows * m_BytesInRow);
            }

            m_Groups.Add(group);
        }

        int FindFirstGroup(float yMin, int startGroup)
        {
            int minGroup = startGroup;

            while (minGroup < m_Groups.Count && m_Groups[minGroup].RowsOffsetY + m_Groups[minGroup].RowsCount * Styles.MemoryMap.RowPixelHeight < yMin)
            {
                minGroup++;
            }

            return minGroup;
        }

        public Material BindDefaultMaterial()
        {
            if (m_Material == null)
            {
                m_Material = new Material(Shader.Find(m_MaterialName));
                m_Material.hideFlags = HideFlags.HideAndDontSave;
#if UNITY_2021_1_OR_NEWER
                m_Material.SetInteger("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m_Material.SetInteger("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m_Material.SetInteger("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                m_Material.SetInteger("_ZWrite", 0);
#else
                m_Material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m_Material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m_Material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                m_Material.SetInt("_ZWrite", 0);
#endif
            }

            m_Material.SetPass(0);

            for (int i = 0; i < m_TextureSlots.Length; ++i)
                m_Material.SetTexture("_Input" + i, m_TextureSlots[i]);

            MemoryMapSelectionData selectionData = MemoryMapSelectionData.k_Default;

            for (int i = 0; i < m_Groups.Count; ++i)
            {
                if (m_Groups[i].AddressBegin <= m_HighlightedAddrMin && m_HighlightedAddrMin <= m_Groups[i].AddressEnd)
                {
                    selectionData.SelectionBegin = m_Groups[i].RowsOffset + (float)(m_HighlightedAddrMin - m_Groups[i].AddressBegin) / (float)m_BytesInRow;
                }

                if (m_Groups[i].AddressBegin <= m_HighlightedAddrMax && m_HighlightedAddrMax <= m_Groups[i].AddressEnd)
                {
                    selectionData.SelectionEnd = m_Groups[i].RowsOffset + (float)(m_HighlightedAddrMax - m_Groups[i].AddressBegin) / (float)m_BytesInRow;
                }
            }

            m_Material.SetVector("_SelectionData", selectionData.Data);
            return m_Material;
        }

        public abstract void OnRenderMap(ulong addressMin, ulong addressMax, int slot);


        public void FlushTextures(float yMin, float yMax)
        {
            bool flushTexture = false;

            long rowsCount = (long)(m_Groups[m_Groups.Count - 1].RowsOffset + m_Groups[m_Groups.Count - 1].RowsCount);

            for (int slot = 0; slot < m_TextureSlots.Length; ++slot)
                flushTexture |= (m_TextureSlots[slot] == null || m_TextureSlots[slot].width != (int)MemoryMapRect.width);

            long visibleRows = (long)((yMax - yMin) / Styles.MemoryMap.RowPixelHeight);

            int minGroup = FindFirstGroup(yMin, 0);
            int minGroupRowOffset = (int)((yMin - m_Groups[minGroup].RowsOffsetY) / Styles.MemoryMap.RowPixelHeight);

            long minRow = (long)m_Groups[minGroup].RowsOffset + minGroupRowOffset;
            long maxRow = Math.Min(minRow + visibleRows, rowsCount);

            // If rows are already in cache do nothing ...
            if (!flushTexture && m_MinCachedRow <= minRow && maxRow <= m_MaxCachedRow && !ForceRepaint)
                return;

            for (int slot = 0; slot < m_TextureSlots.LongLength; ++slot)
            {
                m_Texture = m_TextureSlots[slot];

                // ... otherwise rebuild cache.
                long cacheSize = Math.Max(0, (long)k_RowCacheSize - visibleRows) / 2;
                m_MinCachedRow = Math.Max(0, minRow - cacheSize / 2);
                m_MaxCachedRow = Math.Min(m_MinCachedRow + k_RowCacheSize, rowsCount);

                m_MinCachedGroup = minGroup;
                while (m_MinCachedGroup > 0 && m_MinCachedRow < m_Groups[m_MinCachedGroup].RowsStart)
                    m_MinCachedGroup--;

                m_MaxCachedGroup = minGroup;
                while (m_MaxCachedGroup + 1 < m_Groups.Count && m_Groups[m_MaxCachedGroup + 1].RowsStart < m_MaxCachedRow)
                    m_MaxCachedGroup++;

                ulong addressMin = m_Groups[m_MinCachedGroup].AddressBegin + ((ulong)m_MinCachedRow - m_Groups[m_MinCachedGroup].RowsOffset) * m_BytesInRow;
                ulong addressMax = m_Groups[m_MaxCachedGroup].AddressBegin + ((ulong)m_MaxCachedRow - m_Groups[m_MaxCachedGroup].RowsOffset) * m_BytesInRow;

                if (flushTexture)
                {
                    if (m_Texture != null)
                        UnityEngine.Object.DestroyImmediate(m_Texture);

                    m_Texture = m_TextureSlots[slot] = new Texture2D((int)MemoryMapRect.width, k_RowCacheSize, TextureFormat.RGBA32, false, true);
                    m_Texture.name = k_MemoryMapTextureSlot;
                    m_Texture.wrapMode = TextureWrapMode.Clamp;
                    m_Texture.filterMode = FilterMode.Point;
                }

                m_RawTexture = m_Texture.GetRawTextureData<Color32>();

                unsafe
                {
                    var ptr = m_RawTexture.GetUnsafePtr();
                    var alpha = new Color32(0, 0, 0, 0);
                    UnsafeUtility.MemCpyReplicate(ptr, &alpha, UnsafeUtility.SizeOf<Color32>(), m_RawTexture.Length);
                }

                OnRenderMap(addressMin, addressMax, slot);

                // Sacrificing first pixel of row for information about group and it position in it.
                for (int i = 0; i < m_Groups.Count; ++i)
                {
                    if (m_MinCachedRow <= m_Groups[i].RowsEnd && m_Groups[i].RowsStart <= m_MaxCachedRow)
                    {
                        long begin = Math.Max(m_MinCachedRow, m_Groups[i].RowsStart);
                        long end   = Math.Min(m_MaxCachedRow, m_Groups[i].RowsEnd);

                        for (long j = begin; j < end; j++)
                        {
                            Color c = new Color(j, j == begin ? 1.0f : 0 , j + 1 == end ? 1.0f : 0 , 1.0f);

                            m_RawTexture[(int)((j % m_Texture.height) * m_Texture.width)] = c;
                        }
                    }
                }

                m_Texture.Apply(false);
            }

            ForceRepaint = false;
        }

        const int k_PixelToCheck = 10;

        public void RenderStrip(MemoryGroup group, ulong addrBegin, ulong addrEnd, Color32 color)
        {
            ulong diffToBeginOfGroup = (addrBegin - group.AddressBegin);

            ulong rowInGroupMin = diffToBeginOfGroup / m_BytesInRow;

            ulong diffToBeginOfRow = (diffToBeginOfGroup % m_BytesInRow);

            float  x0 = diffToBeginOfRow / (float)m_BytesInRow * MemoryMapRect.width;

            int xDelta = (int)(((long)(addrEnd % m_BytesInRow) - (long)(addrBegin % m_BytesInRow)) / (float)m_BytesInRow * MemoryMapRect.width);
            ulong rowDelta = (ulong)((addrEnd - addrBegin + diffToBeginOfRow) / m_BytesInRow);
            int texelX0 = (int)x0;
            int texelX1 = texelX0 + xDelta;

            var firstRow = (long)(group.RowsOffset + rowInGroupMin);
            var lastRow = (long)(group.RowsOffset + rowInGroupMin + rowDelta);

            int texelBegin = (int)firstRow * m_Texture.width + texelX0;
            int texelEnd = Math.Max( (int)lastRow * m_Texture.width + texelX1, texelBegin + 1); // min size of 1 pixel

            for (int x = texelBegin; x < texelEnd; x++)
            {
                m_RawTexture[x % m_RawTexture.Length] = color;
            }
        }

        public void RenderStrip(MemoryGroup group, ulong addrBegin, ulong addrEnd, Func<Color32, Color32> func)
        {
            ulong diffToBeginOfGroup = (addrBegin - group.AddressBegin);

            ulong rowInGroupMin = diffToBeginOfGroup / m_BytesInRow;

            ulong diffToBeginOfRow = (diffToBeginOfGroup % m_BytesInRow);

            float  x0 = diffToBeginOfRow / (float)m_BytesInRow * MemoryMapRect.width;

            int xDelta = (int)(((long)(addrEnd % m_BytesInRow) - (long)(addrBegin % m_BytesInRow)) / (float)m_BytesInRow * MemoryMapRect.width);
            ulong rowDelta = (ulong)((addrEnd - addrBegin + diffToBeginOfRow) / m_BytesInRow);
            int texelX0 = (int)x0;
            int texelX1 = texelX0 + xDelta;

            var firstRow = (long)(group.RowsOffset + rowInGroupMin);
            var lastRow = (long)(group.RowsOffset + rowInGroupMin + rowDelta);

            int texelBegin = (int)firstRow * m_Texture.width + texelX0;
            int texelEnd = Math.Max( (int)lastRow * m_Texture.width + texelX1, texelBegin + 1); // min size of 1 pixel

            for (int x = texelBegin; x < texelEnd; x++)
            {
                m_RawTexture[x % m_RawTexture.Length] = func(m_RawTexture[x % m_RawTexture.Length]);
            }
        }

        public void Render<T>(T data, List<EntryRange> ranges, int i, ulong addressMin, ulong addressMax, Color32 color) where T : CachedSnapshot.ISortedEntriesCache
        {
            for (int j = ranges[i].Begin; j < ranges[i].End; ++j)
            {
                ulong addr = (ulong)data.Address(j);
                ulong size = (ulong)data.Size(j);

                ulong stripAddrBegin = addr.Clamp(addressMin, addressMax);
                ulong stripAddrEnd   = (addr + size).Clamp(addressMin, addressMax);

                if (stripAddrBegin != stripAddrEnd)
                {
                    RenderStrip(m_Groups[i], stripAddrBegin, stripAddrEnd, color);
                }
            }
        }

        public void Render<T>(T data, List<EntryRange> ranges, int i, ulong addressMin, ulong addressMax, Func<Color32, Color32> func) where T : CachedSnapshot.ISortedEntriesCache
        {
            for (int j = ranges[i].Begin; j < ranges[i].End; ++j)
            {
                ulong addr = (ulong)data.Address(j);
                ulong size = (ulong)data.Size(j);

                ulong stripAddrBegin = addr.Clamp(addressMin,  addressMax);
                ulong stripAddrEnd   = (addr + size).Clamp(addressMin,  addressMax);

                if (stripAddrBegin != stripAddrEnd)
                {
                    RenderStrip(m_Groups[i], stripAddrBegin, stripAddrEnd, func);
                }
            }
        }

        public static Color32 Add(Color32 c1, Color32 c2)
        {
            return new Color32((byte)(c1.r  + c2.r), (byte)(c1.g  + c2.g), (byte)(c1.b  + c2.b), (byte)(c1.a + c2.a));
        }

        public static Color32 Max(Color32 c1, Color32 c2)
        {
            return new Color32(Math.Max(c1.r, c2.r), Math.Max(c1.g, c2.g), Math.Max(c1.b, c2.b), Math.Max(c1.a, c2.a));
        }

        public void RenderDiff<T>(T data0, List<EntryRange> ranges0, T data1, List<EntryRange> ranges1, int i, ulong addressMin, ulong addressMax) where T : CachedSnapshot.ISortedEntriesCache
        {
            MemoryGroup group = m_Groups[i];

            int index0 = ranges0[i].Begin;
            int lastIndex0 = ranges0[i].End;

            while (index0 < lastIndex0 && data0.Address(index0) + data0.Size(index0) <= addressMin) index0++;

            int index1 = ranges1[i].Begin;
            int lastIndex1 = ranges1[i].End;

            while (index1 < lastIndex1 && data1.Address(index1) + data1.Size(index1) <= addressMin) index1++;

            while (index0 < lastIndex0 || index1 < lastIndex1)
            {
                ulong addr0 = index0 < lastIndex0 ? data0.Address(index0) : addressMax;
                ulong addr1 = index1 < lastIndex1 ? data1.Address(index1) : addressMax;

                if (addr0 >= addressMax && addr1 >= addressMax)
                {
                    // There is no need to process entries anymore because I assuming they are sorted.
                    return;
                }

                ulong size0 = index0 < lastIndex0 ? data0.Size(index0) : 0;
                ulong size1 = index1 < lastIndex1 ? data1.Size(index1) : 0;

                if (addr0 == addr1 && size0 == size1)
                {
                    ulong stripAddrBegin = addr0.Clamp(addressMin, addressMax);
                    ulong stripAddrEnd   = (addr0 + size0).Clamp(addressMin, addressMax);

                    if (stripAddrBegin != stripAddrEnd)
                    {
                        RenderStrip(group, stripAddrBegin, stripAddrEnd, (Color32 c) => Add(c, new Color32(0x00, 0x00, 0x0F, 0x00)));
                    }

                    index0++;
                    index1++;
                }
                else
                {
                    if (addr0 < addr1 || (addr0 == addr1 && size0 != size1))
                    {
                        ulong stripAddrBegin = addr0.Clamp(addressMin, addressMax);
                        ulong stripAddrEnd   = (addr0 + size0).Clamp(addressMin, addressMax);

                        if (stripAddrBegin != stripAddrEnd)
                        {
                            RenderStrip(group, stripAddrBegin, stripAddrEnd, (Color32 c) => Add(c, new Color32(0x0F, 0x00, 0x00, 0x00)));
                        }

                        index0++;
                    }

                    if (addr0 > addr1 || (addr0 == addr1 && size0 != size1))
                    {
                        ulong stripAddrBegin = addr1.Clamp(addressMin, addressMax);
                        ulong stripAddrEnd   = (addr1 + size1).Clamp(addressMin, addressMax);

                        if (stripAddrBegin != stripAddrEnd)
                        {
                            RenderStrip(group, stripAddrBegin, stripAddrEnd, (Color32 c) => Add(c, new Color32(0x00, 0x0F, 0x00, 0x00)));
                        }

                        index1++;
                    }
                }
            }
        }

        public void RenderGroups(float yMin, float yMax)
        {
            float u0 = 0.0f;
            float u1 = m_Texture.width;

            GL.Begin(GL.QUADS);
            for (int i = 0; i < m_Groups.Count; ++i)
            {
                if (yMin < m_Groups[i].RowsOffsetY + m_Groups[i].RowsCount * Styles.MemoryMap.RowPixelHeight && m_Groups[i].RowsOffsetY < yMax)
                {
                    float v0 = m_Groups[i].RowsStart;
                    float v1 = m_Groups[i].RowsEnd;
                    float s =  m_Groups[i].RowsCount;

                    GL.TexCoord3(u1, v0, 0); GL.Vertex3(MemoryMapRect.xMax, m_Groups[i].MinY, 0f);
                    GL.TexCoord3(u0, v0, 0); GL.Vertex3(Styles.MemoryMap.HeaderWidth, m_Groups[i].MinY,  0f);
                    GL.TexCoord3(u0, v1, s); GL.Vertex3(Styles.MemoryMap.HeaderWidth, m_Groups[i].MaxY, 0f);
                    GL.TexCoord3(u1, v1, s); GL.Vertex3(MemoryMapRect.xMax,       m_Groups[i].MaxY, 0f);
                }
            }
            GL.End();
        }

        public void RenderGroupLabels(float yMin, float yMax)
        {
            for (int i = 0; i < m_Groups.Count; ++i)
            {
                if (m_Groups[i].RowsOffsetY > yMax)
                {
                    return; // Groups are sorted so there is no need to process anymore
                }

                if (yMin < m_Groups[i].RowsOffsetY + m_Groups[i].RowsCount * Styles.MemoryMap.RowPixelHeight)
                {
                    GUI.Label(m_Groups[i].Labels[0].TextRect, m_Groups[i].Labels[0].Text, Styles.MemoryMap.TimelineBar);

                    for (int j = 1; j < m_Groups[i].Labels.Length && m_Groups[i].Labels[j].TextRect.yMax < yMax; ++j)
                    {
                        GUI.Label(m_Groups[i].Labels[j].TextRect, m_Groups[i].Labels[j].Text, Styles.MemoryMap.AddressSub);
                    }
                }
            }
        }

        public ulong MouseToAddress(Vector2 vCursor)
        {
            int i = m_MinCachedGroup;

            while (i <= m_MaxCachedGroup && vCursor.y > m_Groups[i].MaxY)
            {
                i++;
            }

            if (i <= m_MaxCachedGroup)
            {
                if (vCursor.y >= m_Groups[i].MinY)
                {
                    ulong Addr = m_Groups[i].AddressBegin;
                    Addr += (ulong)(Math.Floor((vCursor.y - m_Groups[i].MinY) / Styles.MemoryMap.RowPixelHeight) * m_BytesInRow);

                    if (vCursor.x >= MemoryMapRect.x)
                        Addr += (ulong)(((vCursor.x - MemoryMapRect.x) * m_BytesInRow) / MemoryMapRect.width);

                    return Addr;
                }

                return m_Groups[i].AddressBegin;
            }

            return 0;
        }

        public void Dispose()
        {
            if (m_TextureSlots != null)
            {
                for (int i = 0; i < m_TextureSlots.Length; ++i)
                    UnityEngine.Object.DestroyImmediate(m_TextureSlots[i]);
            }
        }
    }
}

using System;
using System.Runtime.CompilerServices;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.UI.PathsToRoot;
using UnityEditor;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal static class ManagedHeapArrayDataTools
    {
        const string k_ArrayClosedSqBrackets = "[]";

        /// <summary>
        /// True when this capture's arrays have no out-of-line bounds pointer at all (CoreCLR).
        /// Mono/IL2CPP arrays store a genuine, independently-allocated <c>bounds</c> pointer at
        /// <see cref="VirtualMachineInformation.ArrayBoundsOffsetInHeader"/> — null for an SZARRAY,
        /// pointing at a separate bounds blob otherwise (confirmed against real source:
        /// <c>mono_array_new_full_checked</c>, <c>il2cpp::vm::Array::NewFull</c>). CoreCLR's
        /// <c>ArrayBase</c> has no such field — <c>scripting_array_bounds_offset_in_array_object_header()</c>
        /// is hard-coded to equal <c>scripting_array_object_header_size()</c>, so this offset equality is
        /// the layout's signal rather than an actual pointer to dereference. On CoreCLR, length/rank data
        /// is always inline: total count at <see cref="VirtualMachineInformation.ArraySizeOffsetInHeader"/>,
        /// and for a genuine multi-dimensional array, per-rank {length, lowerBound} int32 pairs starting
        /// immediately at <see cref="VirtualMachineInformation.ArrayHeaderSize"/> (matches dotnet-runtime's
        /// <c>ArrayBase::GetBoundsPtr</c>/<c>GetLowerBoundsPtr</c>).
        /// </summary>
        internal static bool IsCoreClrArrayLayout(VirtualMachineInformation vmInfo)
        {
            return vmInfo.ArrayBoundsOffsetInHeader == vmInfo.ArrayHeaderSize;
        }

        /// <summary>
        /// Reads a CoreCLR array's rank-length array directly from its inline layout — no bounds
        /// pointer to dereference. Rank 1 is always just the total element count. For rank > 1,
        /// CoreCLR stores all dimension lengths contiguously first, THEN all lower bounds
        /// contiguously (<c>INT32 bounds[rank]; INT32 lowerBounds[rank];</c> — dotnet-runtime's
        /// object.h, <c>ArrayBase</c> layout comment) — NOT interleaved {length, lowerBound} pairs
        /// per dimension like Mono/IL2CPP's out-of-line bounds blob. Only the lengths are read here.
        /// </summary>
        static int[] ReadCoreClrArrayRankLength(CachedSnapshot data, BytesAndOffset arrayData, VirtualMachineInformation vmInfo, int iTypeDescriptionArrayType)
        {
            var rank = data.TypeDescriptions.GetRank(iTypeDescriptionArrayType);
            if (rank <= 1)
                return new int[1] { arrayData.Add(vmInfo.ArraySizeOffsetInHeader).ReadInt32() };

            var cursor = arrayData.Add(vmInfo.ArrayHeaderSize);
            var result = new int[rank];
            for (int i = 0; i < rank; i++)
            {
                result[i] = cursor.ReadInt32();
                cursor = cursor.Add(4);
            }
            return result;
        }

        /// <summary>
        /// Offset from the start of an array object to its first element's bytes. On CoreCLR, a
        /// rank > 1 array carries an inline bounds block (<c>rank</c> lengths followed by <c>rank</c>
        /// lower bounds, each <c>int32</c>) between the header and the element data — see
        /// <see cref="ReadCoreClrArrayRankLength"/>. Rank &lt;= 1 has no such block. Only meaningful
        /// when <see cref="IsCoreClrArrayLayout"/> is true; Mono/IL2CPP's bounds live out-of-line via
        /// a pointer, so their element data always starts right after the header.
        /// </summary>
        internal static ulong GetCoreClrArrayDataOffset(VirtualMachineInformation vmInfo, int rank)
        {
            if (rank <= 1)
                return vmInfo.ArrayHeaderSize;
            return vmInfo.ArrayHeaderSize + (ulong)rank * 2 * sizeof(int);
        }

        /// <summary>
        /// Offset from the start of an array object to its first element's bytes, looking up the
        /// array's rank when needed. Prefer <see cref="GetCoreClrArrayDataOffset"/> directly if the
        /// rank is already known, to avoid a redundant <see cref="TypeDescriptionEntriesCache.GetRank"/>
        /// lookup.
        /// </summary>
        internal static ulong GetArrayDataOffset(CachedSnapshot data, VirtualMachineInformation vmInfo, int iTypeDescriptionArrayType)
        {
            return IsCoreClrArrayLayout(vmInfo)
                ? GetCoreClrArrayDataOffset(vmInfo, data.TypeDescriptions.GetRank(iTypeDescriptionArrayType))
                : vmInfo.ArrayHeaderSize;
        }

        public static ArrayInfo GetArrayInfo(CachedSnapshot data, BytesAndOffset arrayData, int iTypeDescriptionArrayType)
        {
            var virtualMachineInformation = data.VirtualMachineInformation;
            var arrayInfo = new ArrayInfo();
            arrayInfo.BaseAddress = 0;
            arrayInfo.ArrayTypeDescription = iTypeDescriptionArrayType;


            arrayInfo.Header = arrayData;

            if (IsCoreClrArrayLayout(virtualMachineInformation))
            {
                arrayInfo.Rank = ReadCoreClrArrayRankLength(data, arrayInfo.Header, virtualMachineInformation, iTypeDescriptionArrayType);
                arrayInfo.Length = 1;
                for (int i = 0; i < arrayInfo.Rank.Length; i++)
                    arrayInfo.Length *= arrayInfo.Rank[i];
                arrayInfo.Data = arrayInfo.Header.Add(GetCoreClrArrayDataOffset(virtualMachineInformation, arrayInfo.Rank.Length));
            }
            else
            {
                arrayInfo.Data = arrayInfo.Header.Add(virtualMachineInformation.ArrayHeaderSize);
                ulong bounds;
                arrayInfo.Header.Add(virtualMachineInformation.ArrayBoundsOffsetInHeader).TryReadPointer(out bounds);

                if (bounds == 0)
                {
                    arrayInfo.Length = arrayInfo.Header.Add(virtualMachineInformation.ArraySizeOffsetInHeader).ReadInt32();
                    arrayInfo.Rank = new int[1] { arrayInfo.Length };
                }
                else
                {
                    int rank = data.TypeDescriptions.GetRank(iTypeDescriptionArrayType);
                    arrayInfo.Rank = new int[rank];

                    if (data.FindHeapBytes(bounds, out var cursor, out var onDemandScope))
                    {
                        using (onDemandScope)
                        {
                            var stride = virtualMachineInformation.ArrayBoundsStride;
                            arrayInfo.Length = 1;
                            for (int i = 0; i != rank; i++)
                            {
                                var l = cursor.ReadInt32();
                                arrayInfo.Length *= l;
                                arrayInfo.Rank[i] = l;
                                cursor = cursor.Add(stride);
                            }
                        }
                    }
                    else
                    {
                        //object has corrupted data
                        arrayInfo.Length = 0;
                        for (int i = 0; i != rank; i++)
                        {
                            arrayInfo.Rank[i] = -1;
                        }
                    }
                }
            }

            arrayInfo.ElementTypeDescription = data.TypeDescriptions.BaseOrElementTypeIndex[iTypeDescriptionArrayType];
            if (arrayInfo.ElementTypeDescription == -1) //We currently do not handle uninitialized types as such override the type, making it return pointer size
            {
                arrayInfo.ElementTypeDescription = iTypeDescriptionArrayType;
            }
            if (data.TypeDescriptions.HasFlag(arrayInfo.ElementTypeDescription, TypeFlags.kValueType))
            {
                arrayInfo.ElementSize = (uint)data.TypeDescriptions.Size[arrayInfo.ElementTypeDescription];
            }
            else
            {
                arrayInfo.ElementSize = virtualMachineInformation.PointerSize;
            }
            return arrayInfo;
        }

        public static int GetArrayElementSize(CachedSnapshot data, int iTypeDescriptionArrayType)
        {
            int iElementTypeDescription = data.TypeDescriptions.BaseOrElementTypeIndex[iTypeDescriptionArrayType];
            if (data.TypeDescriptions.HasFlag(iElementTypeDescription, TypeFlags.kValueType))
            {
                return data.TypeDescriptions.Size[iElementTypeDescription];
            }
            return (int)data.VirtualMachineInformation.PointerSize;
        }

        public static string ArrayRankToString(int[] rankLength)
        {
            string o = "";
            for (int i = 0; i < rankLength.Length; ++i)
            {
                if (o.Length > 0)
                {
                    o += ", ";
                }
                o += rankLength[i].ToString();
            }
            return o;
        }

        public static string ArrayRankIndexToString(int[] rankLength, long index)
        {
            var o = "";
            var remainder = index;
            // go through the ranks, back to front. i.e. for the array int[2,2,2], index 1 is [0,0,1]
            for (int i = rankLength.Length - 1; i > 0; i--)
            {
                var l = rankLength[i];
                switch (l)
                {
                    case 0:
                        // Apparently you can have 0 length multidimensional arrays...
                        // Practically, there are zero elements in these so all of this is null and void
                        return $"Invalid Index into zero sized array: {index}. Array Ranks: {ArrayRankToString(rankLength)}";

                    case 1:
                        o = ", 0" + o;
                        break;

                    default:
                        var rankIndex = remainder % l;
                        o = ", " + rankIndex.ToString() + o;
                        remainder /= l;
                        break;
                }
            }
            if (remainder >= rankLength[0])
                return $"Invalid Index: {index}. Array {(rankLength.Length > 1 ? "Ranks" : "Length")}: {ArrayRankToString(rankLength)}";
            o = remainder + o;
            return o;
        }

        public static int[] ReadArrayRankLength(CachedSnapshot data, ulong address, int iTypeDescriptionArrayType, VirtualMachineInformation virtualMachineInformation)
        {
            if (iTypeDescriptionArrayType < 0) return null;

            if (!data.FindHeapBytes(address, out BytesAndOffset bo, out var onDemandScope))
                return null;

            using (onDemandScope)
            {
                if (IsCoreClrArrayLayout(virtualMachineInformation))
                    return ReadCoreClrArrayRankLength(data, bo, virtualMachineInformation, iTypeDescriptionArrayType);

                bo.Add(virtualMachineInformation.ArrayBoundsOffsetInHeader).TryReadPointer(out var bounds);

                if (bounds == 0)
                {
                    return new int[1] { bo.Add(virtualMachineInformation.ArraySizeOffsetInHeader).ReadInt32() };
                }

                // Try GetHeapBytes first if we know the section index
                bool boundsLoaded = false;
                BytesAndOffset cursor = default;
                OnDemandHeapSectionCache.HeapSectionCacheReferenceScope onDemandScope2 = default;

                if (bo.IsValid && bo.HeapSectionIndex >= 0)
                {
                    var startAddress = data.ManagedHeapSections.StartAddress[bo.HeapSectionIndex];
                    // Only attempt GetHeapBytes if the bounds pointer is within the same heap section as the array header
                    if (bounds >= startAddress && bounds < startAddress + data.ManagedHeapSections.SectionSize[bo.HeapSectionIndex]
                        && data.GetHeapBytes(bo.HeapSectionIndex, bounds, out cursor, out onDemandScope2))
                    {
                        boundsLoaded = cursor.IsValid;
                    }
                }

                // Fallback to FindHeapBytes if GetHeapBytes failed
                if (!boundsLoaded && data.FindHeapBytes(bounds, out cursor, out onDemandScope2))
                {
                    boundsLoaded = cursor.IsValid;
                }

                if (!boundsLoaded)
                {
                    onDemandScope2.Dispose();
                    return null;
                }

                using (onDemandScope2)
                {
                    int rank = data.TypeDescriptions.GetRank(iTypeDescriptionArrayType);
                    int[] l = new int[rank];
                    var stride = virtualMachineInformation.ArrayBoundsStride;
                    for (int i = 0; i != rank; i++)
                    {
                        l[i] = cursor.ReadInt32();
                        cursor = cursor.Add(stride);
                    }
                    return l;
                }
            }
        }

        public static long ReadArrayLength(CachedSnapshot data, ulong address, int iTypeDescriptionArrayType)
        {
            if (iTypeDescriptionArrayType < 0)
            {
                return 0;
            }

            if (!data.FindHeapBytes(address, out BytesAndOffset bo, out var onDemandLoadedTemporaryHeapDataCopyToBeDisposedOfWhenDone))
                return 0;
            using (onDemandLoadedTemporaryHeapDataCopyToBeDisposedOfWhenDone)
            {
                return ReadArrayLength(data, bo, iTypeDescriptionArrayType);
            }
        }

        /// <summary>
        /// Must be called with valid heap bytes loaded into <paramref name="arrayData"/>,
        /// potentially refreshed via on-demand loading if needed using
        /// <see cref="BytesAndOffset.RefetchHeapBytes(CachedSnapshot, out Containers.DynamicArray{byte})"/>.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="arrayData"></param>
        /// <param name="iTypeDescriptionArrayType"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public static long ReadArrayLength(CachedSnapshot data, BytesAndOffset arrayData, int iTypeDescriptionArrayType)
        {
            return ReadArrayLength(data, arrayData, iTypeDescriptionArrayType, out var _);
        }

        /// <summary>
        /// Must be called with valid heap bytes loaded into <paramref name="arrayData"/>,
        /// potentially refreshed via on-demand loading if needed using
        /// <see cref="BytesAndOffset.RefetchHeapBytes(CachedSnapshot, out Containers.DynamicArray{byte})"/>.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="arrayData"></param>
        /// <param name="iTypeDescriptionArrayType"></param>
        /// <param name="rank"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static long ReadArrayLength(CachedSnapshot data, BytesAndOffset arrayData, int iTypeDescriptionArrayType, out int rank)
        {
            rank = -1;
            if (iTypeDescriptionArrayType < 0) return 0;

            var virtualMachineInformation = data.VirtualMachineInformation;
            var bo = arrayData;

            if (IsCoreClrArrayLayout(virtualMachineInformation))
            {
                var rankLength = ReadCoreClrArrayRankLength(data, bo, virtualMachineInformation, iTypeDescriptionArrayType);
                rank = rankLength.Length;
                long coreClrLength = 1;
                for (int i = 0; i < rankLength.Length; i++)
                    coreClrLength *= rankLength[i];
                return coreClrLength;
            }

            bo.Add(virtualMachineInformation.ArrayBoundsOffsetInHeader).TryReadPointer(out var bounds);

            if (bounds == 0)
                return bo.Add(virtualMachineInformation.ArraySizeOffsetInHeader).ReadInt32();

            // Try GetHeapBytes first if we know the section index
            bool boundsLoaded = false;
            BytesAndOffset cursor = default;
            OnDemandHeapSectionCache.HeapSectionCacheReferenceScope onDemandScope = default;

            if (arrayData.IsValid && arrayData.HeapSectionIndex >= 0)
            {
                if (data.GetHeapBytes(arrayData.HeapSectionIndex, bounds, out cursor, out onDemandScope))
                {
                    boundsLoaded = cursor.IsValid;
                }
            }

            // Fallback to FindHeapBytes if GetHeapBytes failed
            if (!boundsLoaded)
            {
                onDemandScope.Dispose();

                data.FindHeapBytes(bounds, out cursor, out onDemandScope);
                boundsLoaded = cursor.IsValid;
            }

            using (onDemandScope)
            {
                // Multidimensional arrays can have a total LongLength > int.MaxValue
                long length = 0;

                if (boundsLoaded)
                {
                    length = 1;
                    rank = data.TypeDescriptions.GetRank(iTypeDescriptionArrayType);
                    var stride = virtualMachineInformation.ArrayBoundsStride;
                    for (int i = 0; i < rank; i++)
                    {
                        length *= cursor.ReadInt32();
#if ENABLE_MEMORY_PROFILER_DEBUG
                        if (length < 0)
                        {
                            var arrayInfo = ManagedHeapArrayDataTools.GetArrayInfo(data, arrayData, iTypeDescriptionArrayType);
                            throw new InvalidOperationException($"Array length overflow. An array of type {data.TypeDescriptions.TypeDescriptionName[iTypeDescriptionArrayType]} with the dimensions [{ArrayRankToString(arrayInfo.Rank)}] overflowed long at rank {rank}]");
                        }
#endif
                        cursor = cursor.Add(stride);
                    }
                }
                Checks.IsTrue(length >= 0);
                return length;
            }
        }

        public static long ReadArrayObjectSizeInBytes(CachedSnapshot data, ulong address, int iTypeDescriptionArrayType)
        {
            var arrayLength = ReadArrayLength(data, address, iTypeDescriptionArrayType);

            var virtualMachineInformation = data.VirtualMachineInformation;
            var typeIndex = data.TypeDescriptions.BaseOrElementTypeIndex[iTypeDescriptionArrayType];
            var isValueType = data.TypeDescriptions.HasFlag(typeIndex, TypeFlags.kValueType);

            var elementSize = isValueType ? (uint)data.TypeDescriptions.Size[typeIndex] : virtualMachineInformation.PointerSize;
            var dataOffset = GetArrayDataOffset(data, virtualMachineInformation, iTypeDescriptionArrayType);
            return (long)dataOffset + elementSize * arrayLength;
        }

        /// <summary>
        /// Must be called with valid heap bytes loaded into <paramref name="arrayData"/>,
        /// potentially refreshed via on-demand loading if needed using
        /// <see cref="BytesAndOffset.RefetchHeapBytes(CachedSnapshot, out Containers.DynamicArray{byte})"/>.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="arrayData"></param>
        /// <param name="iTypeDescriptionArrayType"></param>
        /// <returns></returns>
        public static long ReadArrayObjectSizeInBytes(CachedSnapshot data, BytesAndOffset arrayData, int iTypeDescriptionArrayType)
        {
            var arrayLength = ReadArrayLength(data, arrayData, iTypeDescriptionArrayType);
            var virtualMachineInformation = data.VirtualMachineInformation;

            var typeIndex = data.TypeDescriptions.BaseOrElementTypeIndex[iTypeDescriptionArrayType];
            if (typeIndex == -1) // check added as element type index can be -1 if we are dealing with a class member (eg: Dictionary.Entry) whose type is uninitialized due to their generic data not getting inflated a.k.a unused types
            {
                typeIndex = iTypeDescriptionArrayType;
            }

            var isValueType = data.TypeDescriptions.HasFlag(typeIndex, TypeFlags.kValueType);
            var elementSize = isValueType ? (uint)data.TypeDescriptions.Size[typeIndex] : virtualMachineInformation.PointerSize;

            var dataOffset = GetArrayDataOffset(data, virtualMachineInformation, iTypeDescriptionArrayType);
            return (long)dataOffset + elementSize * arrayLength;
        }

        internal static string GenerateArrayDescription(CachedSnapshot cachedSnapshot, ArrayInfo arrayInfo, long arrayIndex, bool truncateTypeName, bool includeTypeName)
        {
            var arrayTypeName = cachedSnapshot.TypeDescriptions.TypeDescriptionName[arrayInfo.ArrayTypeDescription];
            var name = includeTypeName ? arrayTypeName : string.Empty;
            name = truncateTypeName ? PathsToRootDetailView.TruncateTypeName(name) : name;
            var sb = new System.Text.StringBuilder(name);
            var rankString = arrayIndex >= 0 ? arrayInfo.IndexToRankedString(arrayIndex) : arrayInfo.ArrayRankToString();
            switch (arrayInfo.Rank.Length)
            {
                case 1:
                    int nestedArrayCount = CountArrayOfArrays(arrayTypeName);
                    sb.Replace(k_ArrayClosedSqBrackets, string.Empty);
                    sb.Append('[');
                    sb.Append(rankString);
                    sb.Append(']');
                    for (int i = 1; i < nestedArrayCount; ++i)
                    {
                        sb.Append(k_ArrayClosedSqBrackets);
                    }
                    break;
                default:
                    var lastOpen = name.LastIndexOf('[');
                    var lastClose = name.LastIndexOf(']');
                    if (lastOpen >= 0)
                    {
                        sb.Remove(lastOpen + 1, lastClose - lastOpen - 1);
                        sb.Insert(lastOpen + 1, rankString);
                    }
                    else
                    {
                        sb.Append('[');
                        sb.Append(rankString);
                        sb.Append(']');
                    }
                    break;
            }
            return sb.ToString();
        }

        static int CountArrayOfArrays(string typename)
        {
            int count = 0;

            int iter = 0;
            while (true)
            {
                int idxFound = typename.IndexOf(k_ArrayClosedSqBrackets, iter);
                if (idxFound == -1)
                    break;
                ++count;
                iter = idxFound + k_ArrayClosedSqBrackets.Length;
                if (iter >= typename.Length)
                    break;
            }

            return count;
        }
    }
}

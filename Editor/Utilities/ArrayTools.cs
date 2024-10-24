using System;
using System.Runtime.CompilerServices;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.UI.PathsToRoot;

namespace Unity.MemoryProfiler.Editor
{
    internal static class ManagedHeapArrayDataTools
    {
        const string k_ArrayClosedSqBrackets = "[]";

        public static ArrayInfo GetArrayInfo(CachedSnapshot data, BytesAndOffset arrayData, int iTypeDescriptionArrayType)
        {
            var virtualMachineInformation = data.VirtualMachineInformation;
            var arrayInfo = new ArrayInfo();
            arrayInfo.BaseAddress = 0;
            arrayInfo.ArrayTypeDescription = iTypeDescriptionArrayType;


            arrayInfo.Header = arrayData;
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

                var cursor = data.ManagedHeapSections.Find(bounds, virtualMachineInformation);
                if (cursor.IsValid)
                {
                    arrayInfo.Length = 1;
                    for (int i = 0; i != rank; i++)
                    {
                        var l = cursor.ReadInt32();
                        arrayInfo.Length *= l;
                        arrayInfo.Rank[i] = l;
                        cursor = cursor.Add(8);
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
                        // Apparently (https://unity.slack.com/archives/CHVTMBEF5/p1706904839459539?thread_ts=1706808095.762939&cid=CHVTMBEF5)
                        // you can have 0 length multidimensional arrays...
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

        public static int[] ReadArrayRankLength(CachedSnapshot data, CachedSnapshot.ManagedMemorySectionEntriesCache heap, UInt64 address, int iTypeDescriptionArrayType, VirtualMachineInformation virtualMachineInformation)
        {
            if (iTypeDescriptionArrayType < 0) return null;

            var bo = heap.Find(address, virtualMachineInformation);
            ulong bounds;
            bo.Add(virtualMachineInformation.ArrayBoundsOffsetInHeader).TryReadPointer(out bounds);

            if (bounds == 0)
            {
                return new int[1] { bo.Add(virtualMachineInformation.ArraySizeOffsetInHeader).ReadInt32() };
            }

            var cursor = heap.Find(bounds, virtualMachineInformation);
            int rank = data.TypeDescriptions.GetRank(iTypeDescriptionArrayType);
            int[] l = new int[rank];
            for (int i = 0; i != rank; i++)
            {
                l[i] = cursor.ReadInt32();
                cursor = cursor.Add(8);
            }
            return l;
        }

        public static long ReadArrayLength(CachedSnapshot data, UInt64 address, int iTypeDescriptionArrayType)
        {
            if (iTypeDescriptionArrayType < 0)
            {
                return 0;
            }

            var heap = data.ManagedHeapSections;
            var bo = heap.Find(address, data.VirtualMachineInformation);
            return ReadArrayLength(data, bo, iTypeDescriptionArrayType);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long ReadArrayLength(CachedSnapshot data, BytesAndOffset arrayData, int iTypeDescriptionArrayType)
        {
            return ReadArrayLength(data, arrayData, iTypeDescriptionArrayType, out var _);
        }

        public static long ReadArrayLength(CachedSnapshot data, BytesAndOffset arrayData, int iTypeDescriptionArrayType, out int rank)
        {
            rank = -1;
            if (iTypeDescriptionArrayType < 0) return 0;

            var virtualMachineInformation = data.VirtualMachineInformation;
            var heap = data.ManagedHeapSections;
            var bo = arrayData;

            bo.Add(virtualMachineInformation.ArrayBoundsOffsetInHeader).TryReadPointer(out var bounds);

            if (bounds == 0)
                return bo.Add(virtualMachineInformation.ArraySizeOffsetInHeader).ReadInt32();

            var cursor = heap.Find(bounds, virtualMachineInformation);
            // Multidimensional arrays can have a total LongLength > int.MaxValue
            long length = 0;

            if (cursor.IsValid)
            {
                length = 1;
                rank = data.TypeDescriptions.GetRank(iTypeDescriptionArrayType);
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
                    cursor = cursor.Add(8);
                }
            }
            Checks.IsTrue(length >= 0);
            return length;
        }

        public static long ReadArrayObjectSizeInBytes(CachedSnapshot data, UInt64 address, int iTypeDescriptionArrayType)
        {
            var arrayLength = ReadArrayLength(data, address, iTypeDescriptionArrayType);

            var virtualMachineInformation = data.VirtualMachineInformation;
            var typeIndex = data.TypeDescriptions.BaseOrElementTypeIndex[iTypeDescriptionArrayType];
            var isValueType = data.TypeDescriptions.HasFlag(typeIndex, TypeFlags.kValueType);

            var elementSize = isValueType ? (uint)data.TypeDescriptions.Size[typeIndex] : virtualMachineInformation.PointerSize;
            return virtualMachineInformation.ArrayHeaderSize + elementSize * arrayLength;
        }

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

            return virtualMachineInformation.ArrayHeaderSize + elementSize * arrayLength;
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

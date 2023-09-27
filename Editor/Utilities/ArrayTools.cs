using System;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.MemoryProfiler.Editor.Format;

namespace Unity.MemoryProfiler.Editor
{
    internal static class ManagedHeapArrayDataTools
    {
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

        public static string ArrayRankIndexToString(int[] rankLength, int index)
        {
            string o = "";
            int remainder = index;
            for (int i = 1; i < rankLength.Length; ++i)
            {
                if (o.Length > 0)
                {
                    o += ", ";
                }
                var l = rankLength[i];
                int rankIndex = remainder / l;
                o += rankIndex.ToString();
                remainder = remainder - rankIndex * l;
            }
            if (o.Length > 0)
            {
                o += ", ";
            }
            o += remainder;
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

        public static int ReadArrayLength(CachedSnapshot data, UInt64 address, int iTypeDescriptionArrayType)
        {
            if (iTypeDescriptionArrayType < 0)
            {
                return 0;
            }

            var heap = data.ManagedHeapSections;
            var bo = heap.Find(address, data.VirtualMachineInformation);
            return ReadArrayLength(data, bo, iTypeDescriptionArrayType);
        }

        public static int ReadArrayLength(CachedSnapshot data, BytesAndOffset arrayData, int iTypeDescriptionArrayType)
        {
            if (iTypeDescriptionArrayType < 0) return 0;

            var virtualMachineInformation = data.VirtualMachineInformation;
            var heap = data.ManagedHeapSections;
            var bo = arrayData;

            ulong bounds;
            bo.Add(virtualMachineInformation.ArrayBoundsOffsetInHeader).TryReadPointer(out bounds);

            if (bounds == 0)
                return bo.Add(virtualMachineInformation.ArraySizeOffsetInHeader).ReadInt32();

            var cursor = heap.Find(bounds, virtualMachineInformation);
            var length = 0;

            if (cursor.IsValid)
            {
                length = 1;
                int rank = data.TypeDescriptions.GetRank(iTypeDescriptionArrayType);
                for (int i = 0; i < rank; i++)
                {
                    length *= cursor.ReadInt32();
                    cursor = cursor.Add(8);
                }
            }
            Checks.IsTrue(length >= 0);
            return length;
        }

        public static int ReadArrayObjectSizeInBytes(CachedSnapshot data, UInt64 address, int iTypeDescriptionArrayType)
        {
            var arrayLength = ReadArrayLength(data, address, iTypeDescriptionArrayType);

            var virtualMachineInformation = data.VirtualMachineInformation;
            var typeIndex = data.TypeDescriptions.BaseOrElementTypeIndex[iTypeDescriptionArrayType];
            var isValueType = data.TypeDescriptions.HasFlag(typeIndex, TypeFlags.kValueType);

            var elementSize = isValueType ? (uint)data.TypeDescriptions.Size[typeIndex] : virtualMachineInformation.PointerSize;
            return (int)(virtualMachineInformation.ArrayHeaderSize + elementSize * arrayLength);
        }

        public static int ReadArrayObjectSizeInBytes(CachedSnapshot data, BytesAndOffset arrayData, int iTypeDescriptionArrayType)
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

            return (int)(virtualMachineInformation.ArrayHeaderSize + elementSize * arrayLength);
        }
    }
}

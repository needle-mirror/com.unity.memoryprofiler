using System;
using Unity.MemoryProfiler.Editor.Format;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor
{
    static class StringTools
    {
        // After 8000 chars, StringBuilder will ring buffer the strings and our UI breaks. Also see https://referencesource.microsoft.com/#mscorlib/system/text/stringbuilder.cs,76
        const int k_StringBuilderMaxCap = 8000;
        // However, 8000 chars is quite a bit more than would be necessarily helpful for the memory profiler, so trim it further
        const int k_AdditionalTrimForLongStrings = 6000;
        public const int MaxStringLengthToRead = k_StringBuilderMaxCap - k_AdditionalTrimForLongStrings - 10 /*Buffer for ellipsis, quotes and spaces*/;
        public const string Elipsis = " [...]";

        public static string ReadString(this BytesAndOffset bo, out int fullLength, VirtualMachineInformation virtualMachineInformation)
        {
            fullLength = -1;
            return ReadStringInternal(bo, ref fullLength, virtualMachineInformation);
        }

        public static string ReadString(this ManagedObjectInfo managedObjectInfo, CachedSnapshot snapshot)
        {
            int fullLength = -1;
            return ReadStringInternal(managedObjectInfo.data, ref fullLength, snapshot.VirtualMachineInformation);
        }

        public static string ReadCharArray(this ManagedObjectInfo managedObjectInfo, CachedSnapshot snapshot)
        {
            return ReadCharArray(managedObjectInfo.data, ManagedHeapArrayDataTools.GetArrayInfo(snapshot, managedObjectInfo.data, managedObjectInfo.ITypeDescription).Length, snapshot.VirtualMachineInformation);
        }

        public static string ReadCharArray(this BytesAndOffset bo, int fullLength, VirtualMachineInformation virtualMachineInformation)
        {
            return ReadStringInternal(bo, ref fullLength, virtualMachineInformation).Replace((char)0, ' ');
        }

        static string ReadStringInternal(this BytesAndOffset bo, ref int fullLength, VirtualMachineInformation virtualMachineInformation, int maxLengthToRead = MaxStringLengthToRead)
        {
            BytesAndOffset firstChar = bo;
            if (fullLength < 0)
            {
                // parsing a string with an object header
                bo = bo.Add(virtualMachineInformation.ObjectHeaderSize);
                fullLength = bo.ReadInt32();
                firstChar = bo.Add(sizeof(int));
            }
            else
            {
                // pasring a char [] with an array header
                bo = bo.Add(virtualMachineInformation.ArrayHeaderSize);
                firstChar = bo;
            }


            if (fullLength < 0 || (ulong)fullLength * 2 > (ulong)bo.Bytes.Count - bo.Offset - sizeof(int))
            {
#if DEBUG_VALIDATION
                Debug.LogError("Found a String Object of impossible length.");
#endif
                fullLength = 0;
            }

            unsafe
            {
                if (fullLength > maxLengthToRead)
                {
                    var cappedLength = maxLengthToRead;
                    return $"{System.Text.Encoding.Unicode.GetString(firstChar.GetUnsafeOffsetTypedPtr(), cappedLength * 2)}{Elipsis}";
                }
                else
                    return System.Text.Encoding.Unicode.GetString(firstChar.GetUnsafeOffsetTypedPtr(), fullLength * 2);
            }
        }

        public static string ReadFirstStringLine(this ManagedObjectInfo moi, VirtualMachineInformation virtualMachineInformation, bool addQuotes)
        {
            return ReadFirstStringLineInternal(moi.data, virtualMachineInformation, addQuotes, -1);
        }

        public static string ReadFirstStringLine(this BytesAndOffset bo, VirtualMachineInformation virtualMachineInformation, bool addQuotes)
        {
            return ReadFirstStringLineInternal(bo, virtualMachineInformation, addQuotes, -1);
        }

        public static string ReadFirstCharArrayLine(this ManagedObjectInfo managedObjectInfo, CachedSnapshot snapshot, bool addQuotes)
        {
            return ReadFirstCharArrayLine(managedObjectInfo.data, snapshot.VirtualMachineInformation, addQuotes, ManagedHeapArrayDataTools.GetArrayInfo(snapshot, managedObjectInfo.data, managedObjectInfo.ITypeDescription).Length);
        }

        public static string ReadFirstCharArrayLine(this BytesAndOffset bo, VirtualMachineInformation virtualMachineInformation, bool addQuotes, int fullLength)
        {
            return ReadFirstStringLineInternal(bo, virtualMachineInformation, addQuotes, fullLength).Replace((char)0, ' ');
        }

        static string ReadFirstStringLineInternal(this BytesAndOffset bo, VirtualMachineInformation virtualMachineInformation, bool addQuotes, int fullLength)
        {
            const int maxCharsInLine = 30;
            var str = ReadStringInternal(bo, ref fullLength, virtualMachineInformation, maxCharsInLine);
            var firstLineBreak = str.IndexOf('\n');
            if (firstLineBreak >= 0)
            {
                if (firstLineBreak < maxCharsInLine && str.Length > maxCharsInLine)
                {
                    // reduce our working set
                    str = str.Substring(0, Math.Min(str.Length, maxCharsInLine));
                }
                str = str.Replace("\n", "\\n");
                str = str.Replace("\r", "\\r");
                str += Elipsis;
            }
            if (addQuotes)
            {
                if (firstLineBreak >= 0)
                    return $"\"{str}"; // open ended quote
                return $"\"{str}\"";
            }
            else
            {
                return str;
            }
        }

        public static int ReadStringObjectSizeInBytes(this BytesAndOffset bo, VirtualMachineInformation virtualMachineInformation)
        {
            var lengthPointer = bo.Add(virtualMachineInformation.ObjectHeaderSize);
            var length = lengthPointer.ReadInt32();
            if (length < 0 || (ulong)length * 2 > (ulong)bo.Bytes.Count - bo.Offset - sizeof(int))
            {
#if DEBUG_VALIDATION
                Debug.LogError("Found a String Object of impossible length.");
#endif
                length = 0;
            }

            return (int)virtualMachineInformation.ObjectHeaderSize + /*lengthfield*/ 4 + (length * /*utf16=2bytes per char*/ 2) + /*2 zero terminators*/ 2;
        }
    }
}

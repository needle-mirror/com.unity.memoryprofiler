using System;
using System.Runtime.CompilerServices;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.UIContentData;
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

        public static string ReadFullStringOrGetPreview(this ManagedObjectInfo managedObjectInfo, CachedSnapshot snapshot, out int fullLength, bool addQuotes)
        {
            if (managedObjectInfo.ITypeDescription != snapshot.TypeDescriptions.ITypeString)
            {
                throw new InvalidOperationException("ERROR: trying to read a managed object as a string that is not a string.");
            }
            // We want to ideally get the full string data for the selection details but for shorter strings,
            // the cached preview data may be sufficient so get those first
            var stringWasShortenedForPreview = false;
            string str = null;
            fullLength = 0; // Initialize out parameter

            if (managedObjectInfo.ManagedObjectIndex >= 0 && snapshot.TableEntryNames?.TryGetPreview(managedObjectInfo.ManagedObjectIndex, out var preview) == true)
            {
                str = preview;
                if (str.Length > Elipsis.Length)
                {
                    var span = str.AsSpan();
                    if (str[str.Length - 1] == '"')
                    {
                        span = span.Slice(0, span.Length - 1);
                    }
                    if (span.EndsWith(Elipsis))
                    {
                        // Preview was shortened, so we can't rely on it for full string data
                        stringWasShortenedForPreview = true;
                    }
                }
                if (!addQuotes)
                {
                    str = StripQuotes(str);
                }
                // If the preview string contains an ellipsis, we keep it.
                // Estimate string length from preview
                fullLength = str.Length;
            }

            if (stringWasShortenedForPreview)
            {
                str = ReadString(managedObjectInfo.data, out fullLength, snapshot.VirtualMachineInformation);
                return addQuotes ? $"\"{str}\"" : str;
            }

            return str;
        }

        public static string GetPreviewOrReadFirstLine(this ManagedObjectInfo managedObjectInfo, CachedSnapshot snapshot, bool addQuotes)
        {
            // First, check the cache.
            if (snapshot.TableEntryNames?.TryGetPreview(managedObjectInfo.ManagedObjectIndex, out var preview) ?? false)
            {
                if (addQuotes)
                    return preview;
                return StripQuotes(preview);
            }
            // Otherwise read the first line of the string.
            return ReadFirstStringLine(managedObjectInfo.data, snapshot.VirtualMachineInformation, addQuotes);
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        static string StripQuotes(string str)
        {
            if (str.Length >= 1)
            {
                var firstChar = str[0] == '"' ? 1 : 0;
                var length = str.Length - firstChar - (str[str.Length - 1] == '"' ? 1 : 0);
                return str.Substring(firstChar, length); // Remove quotes if the exist
            }
            return str;
        }

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

            if (bo.IsValid && !bo.CouldFitAllocation(virtualMachineInformation.ObjectHeaderSize + sizeof(Int32)))
            {
                const string k_Error = "Error: Invalid String Object";
                fullLength = k_Error.Length;
                return k_Error;
            }

            if (fullLength < 0)
            {
                // parsing a string with an object header
                bo = bo.Add(virtualMachineInformation.ObjectHeaderSize);
                fullLength = bo.ReadInt32();
                firstChar = bo.Add(sizeof(int));
            }
            else
            {
                // parsing a char [] with an array header
                bo = bo.Add(virtualMachineInformation.ArrayHeaderSize);
                firstChar = bo;
            }

            if (fullLength < 0 || !firstChar.CouldFitAllocation((long)(fullLength) * sizeof(char)))
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
                    return $"{System.Text.Encoding.Unicode.GetString(firstChar.GetUnsafeOffsetTypedPtr(), cappedLength * sizeof(char))}{Elipsis}";
                }
                else
                    return System.Text.Encoding.Unicode.GetString(firstChar.GetUnsafeOffsetTypedPtr(), fullLength * sizeof(char));
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
            const int maxCharsInLine = TableEntryNameCache.MaxPreviewLength;
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

        public static int ReadStringObjectSizeInBytes(this BytesAndOffset bo, VirtualMachineInformation virtualMachineInformation, out bool valid, bool logError = true)
        {
            if (!bo.CouldFitAllocation(virtualMachineInformation.ObjectHeaderSize + sizeof(int)))
            {
                valid = false;
                return 0;
            }
            var lengthPointer = bo.Add(virtualMachineInformation.ObjectHeaderSize);
            var length = lengthPointer.ReadInt32();
            var data = lengthPointer.Add(sizeof(int));

            if (length < 0 || !data.CouldFitAllocation((long)(length) * sizeof(char)))
            {
#if DEBUG_VALIDATION
                if (logError)
                    Debug.LogError("Found a String Object of impossible length.");
#endif
                length = 0;
                valid = false;
            }
            else
                valid = true;

            return (int)virtualMachineInformation.ObjectHeaderSize + /*lengthfield*/ sizeof(int) + (length * /*utf16=2bytes per char*/ sizeof(char));
        }
    }
}

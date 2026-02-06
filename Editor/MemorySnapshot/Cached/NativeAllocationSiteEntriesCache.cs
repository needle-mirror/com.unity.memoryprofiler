using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using UnityEditor;

namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
        public class NativeAllocationSiteEntriesCache : IDisposable
        {
            // Call Side Ids are pointers, so 0 is an invalid Site Id
            public const ulong SiteIdNullPointer = 0;
            public long Count;
            public DynamicArray<ulong> Id = default;
            public DynamicArray<int> MemoryLabelIndex = default;
            public readonly Dictionary<ulong, long> IdToIndex;

            public NestedDynamicArray<ulong> callstackSymbols => m_callstackSymbolsReadOp.CompleteReadAndGetNestedResults();
            NestedDynamicSizedArrayReadOperation<ulong> m_callstackSymbolsReadOp;

            unsafe public NativeAllocationSiteEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeAllocationSites_Id);

                if (Count == 0)
                    return;

                Id = reader.Read(EntryType.NativeAllocationSites_Id, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                MemoryLabelIndex = reader.Read(EntryType.NativeAllocationSites_MemoryLabelIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();

                m_callstackSymbolsReadOp = reader.AsyncReadDynamicSizedArray<ulong>(EntryType.NativeAllocationSites_CallstackSymbols, 0, Count, Allocator.Persistent);
                IdToIndex = new Dictionary<ulong, long>((int)Count);
                for (long idx = 0; idx < Count; idx++)
                {
                    IdToIndex[Id[idx]] = idx;
                }
            }

            public readonly struct ReadableCallstack
            {
                public readonly string MemLabel;
                public readonly string Callstack;
                public readonly List<KeyValuePair<int, string>> FileLinkHashToFileName;
                public ReadableCallstack(string memLabel, string callstack, List<KeyValuePair<int, string>> fileLinkHashToFileName)
                {
                    MemLabel = memLabel;
                    Callstack = callstack;
                    FileLinkHashToFileName = fileLinkHashToFileName;
                }
            }

            public readonly struct CallStackInfo : IEquatable<CallStackInfo>
            {
                public readonly ulong Id;
                public readonly long Index;
                public readonly int MemoryLabelIndex;
                public readonly DynamicArrayRef<ulong> CallstackSymbols;
                // siteId is an address so 0 is an invalid site Id. However, this is handled via TryGet on IdToIndex in GetCallStackInfo
                // Checking the Index and that there is actual symbols should suffice here
                public readonly bool Valid => /* Id != NullPointerId &&*/ Index >= 0 && CallstackSymbols.IsCreated;

                public CallStackInfo(ulong id, long index, int memoryLabelIndex, DynamicArrayRef<ulong> callstackSymbols)
                {
                    Id = id;
                    Index = index;
                    MemoryLabelIndex = memoryLabelIndex;
                    CallstackSymbols = callstackSymbols;
                }

                public bool Equals(CallStackInfo other)
                {
                    // TODO: Check if Call Site ID is a sufficient comparison.
                    // Instinct says two different call stacks might end in the same site ID.

                    // Ignoring Index and MemoryLabelIndex as those are irrelevant for the sameness of the callstack
                    return Id == other.Id && CallstackSymbols.Equals(other.CallstackSymbols);
                }

                public readonly int GetHashCode(CallStackInfo obj) => obj.GetHashCode();
                public override int GetHashCode() => HashCode.Combine(Id, CallstackSymbols);
            }

            public CallStackInfo GetCallStackInfo(ulong id)
            {
                if (id != SiteIdNullPointer && IdToIndex.TryGetValue(id, out var index))
                    return new CallStackInfo(id, index, MemoryLabelIndex[index], callstackSymbols[index]);
                return new CallStackInfo(id, -1, -1, new DynamicArrayRef<ulong>());
            }

            public string GetMemLabelName(CachedSnapshot snapshot, SourceIndex nativeAllocationIndex)
            {
                if (Count <= 0 || !nativeAllocationIndex.Valid || nativeAllocationIndex.Id is not SourceIndex.SourceId.NativeAllocation)
                    return null;
                var siteId = snapshot.NativeAllocations.AllocationSiteId[nativeAllocationIndex.Index];
                var memLabelIndex = GetMemLabel(siteId);

                string memLabelName = memLabelIndex.Valid ? snapshot.NativeMemoryLabels.MemoryLabelName[memLabelIndex.Index] : CachedSnapshot.UnknownMemlabelName;
                return memLabelName;
            }

            static readonly SourceIndex k_FakeInvalidlyLabeledAllocationIndex = new SourceIndex(SourceIndex.SourceId.None, (long)SourceIndex.SpecialNoneCase.UnknownMemLabel);
            public SourceIndex GetMemLabel(ulong siteId)
            {
                if (siteId == NativeAllocationSiteEntriesCache.SiteIdNullPointer
                    || !IdToIndex.TryGetValue(siteId, out var siteIndex))
                    siteIndex = -1;
                var memLabelIndex = siteIndex >= 0 ? MemoryLabelIndex[siteIndex] : -1;
                if (memLabelIndex <= CachedSnapshot.NativeMemoryLabelEntriesCache.InvalidMemLabelIndex)
                    memLabelIndex = -1;
                var memLabelSourceIndex = memLabelIndex >= 0 ? new SourceIndex(SourceIndex.SourceId.MemoryLabel, memLabelIndex) : k_FakeInvalidlyLabeledAllocationIndex;
                return memLabelSourceIndex;
            }

            public ReadableCallstack GetReadableCallstackForId(NativeMemoryLabelEntriesCache memLabels, NativeCallstackSymbolEntriesCache symbols, ulong id)
            {
                long entryIdx = -1;
                for (long i = 0; i < Id.Count; ++i)
                {
                    if (Id[i] == id)
                    {
                        entryIdx = i;
                        break;
                    }
                }

                return entryIdx < 0 ? new ReadableCallstack(string.Empty, string.Empty, null) : GetReadableCallstack(memLabels, symbols, entryIdx);
            }

            public void AppendCallstackLine(NativeCallstackSymbolEntriesCache symbols, ulong targetSymbol, StringBuilder stringBuilder,
                List<KeyValuePair<int, string>> fileLinkHashToFileName = null, bool simplifyCallStacks = true, bool clickableCallStacks = true, bool terminateWithLineBreak = true)
            {
                if (symbols.SymbolToIndex.TryGetValue(targetSymbol, out var symbolIdx))
                {
                    // Format of symbols is: "0x0000000000000000 (Unity) OptionalNativeNamespace::NativeClass<PotentialTemplateType>::NativeMethod (at C:/Path/To/CPPorHeaderFile.h:428)\n"
                    // or "0x0000000000000000 (Unity) Managed.Namespace.List.ClassName:MethodName (parametertypes,separated,by,comma) (at C:/Path/To/CSharpFile.cs:13)\n"
                    // or "0x0000000000000000 (KERNEL32) BaseThreadInitThunk\n"
                    // or "0x0000000000000000 ((<unknown>)) \n"
                    try
                    {
                        var symbol = symbols.ReadableStackTrace[symbolIdx].AsSpan();
                        var firstCharIndexOfAssemblyName = symbol.IndexOf('(');
                        if (firstCharIndexOfAssemblyName > 0)
                        {
                            if (symbol[firstCharIndexOfAssemblyName + 1] == '(')
                            {
                                if (simplifyCallStacks)
                                    stringBuilder.AppendLine("<unknown>");
                                else
                                    stringBuilder.Append(symbol);
                                return;
                            }
                            if (!simplifyCallStacks)
                            {
                                var address = symbol.Slice(0, firstCharIndexOfAssemblyName);
                                stringBuilder.Append(address);
                            }
                            symbol = symbol.Slice(firstCharIndexOfAssemblyName);
                        }
                        var lastCharIndexOfMethodName = symbol.LastIndexOf('(');
                        if (lastCharIndexOfMethodName <= 0)
                        {
                            if (!terminateWithLineBreak)
                            {
                                var indexOfLineBreak = symbol.IndexOf('\n');
                                if (indexOfLineBreak > 0)
                                    symbol = symbol.Slice(0, indexOfLineBreak);
                            }
                            stringBuilder.Append(symbol);
                            return;
                        }
                        var methodName = symbol.Slice(0, lastCharIndexOfMethodName);
                        symbol = symbol.Slice(lastCharIndexOfMethodName);

                        stringBuilder.Append(methodName);

                        const string k_FileNamePrefix = "(at ";

                        var fileNameStart = symbol.IndexOf(k_FileNamePrefix) + k_FileNamePrefix.Length;
                        var fileNameEndIndex = symbol.LastIndexOf(':');
                        var fileNameLength = fileNameEndIndex - fileNameStart;
                        if (clickableCallStacks && fileNameLength > 0)
                        {
                            var fileName = symbol.Slice(fileNameStart, fileNameLength).ToString();
                            var lineNumberEndIndex = symbol.LastIndexOf(')');
                            var lineNumberChars = symbol.Slice(fileNameEndIndex + 1, lineNumberEndIndex - fileNameEndIndex - 1);

                            if (!int.TryParse(lineNumberChars, out var lineNumber))
                                lineNumber = 0;

                            if (fileLinkHashToFileName != null)
                            {
                                // TextCore htlm tags have a character limit of 128 in Unity 2022 and 256 in Unity 6+,
                                // so putting the full file path as the link might break. Using a hash also means less chars wasted.
                                var fileNameHash = fileName.GetHashCode();
                                fileLinkHashToFileName.Add(new KeyValuePair<int, string>(fileNameHash, fileName));

                                stringBuilder.AppendFormat("\t(at <link=\"href='{0}' line='{1}'\"><color={3}><u>{2}:{1}</u></color></link>){4}", fileNameHash, lineNumber, fileName, EditorGUIUtility.isProSkin ? "#40a0ff" : "#0000FF", terminateWithLineBreak ? '\n' : "");
                            }
                            else
                            {
                                stringBuilder.AppendFormat("\t(at <link=\"href='{0}' line='{1}'\"><color={3}><u>{2}:{1}</u></color></link>){4}", fileName, lineNumber, fileName, EditorGUIUtility.isProSkin ? "#40a0ff" : "#0000FF", terminateWithLineBreak ? '\n' : "");
                            }
                        }
                        else
                        {
                            if (!terminateWithLineBreak)
                            {
                                var indexOfLineBreak = symbol.IndexOf('\n');
                                if (indexOfLineBreak > 0)
                                    symbol = symbol.Slice(0, indexOfLineBreak);
                            }
                            stringBuilder.Append(symbol);
                        }
                    }
                    catch (Exception)
                    {
                        stringBuilder.AppendLine(symbols.ReadableStackTrace[symbolIdx]);
                    }
                }
                else
                {
                    stringBuilder.AppendLine("<unknown>");
                }
            }

            public ReadableCallstack GetReadableCallstack(NativeMemoryLabelEntriesCache memLabels, NativeCallstackSymbolEntriesCache symbols, long idx, bool simplifyCallStacks = true, bool clickableCallStacks = true)
            {
                var stringBuilder = new StringBuilder();

                var callstackSymbols = this.callstackSymbols[idx];
                var fileLinkHashToFileName = new List<KeyValuePair<int, string>>((int)callstackSymbols.Count);
                for (long i = 0; i < callstackSymbols.Count; ++i)
                {
                    ulong targetSymbol = callstackSymbols[i];
                    AppendCallstackLine(symbols, targetSymbol, stringBuilder, fileLinkHashToFileName, simplifyCallStacks, clickableCallStacks);
                }

                return new ReadableCallstack(
                    MemoryLabelIndex[idx] >= NativeMemoryLabelEntriesCache.InvalidMemLabelIndex ?
                    memLabels.MemoryLabelName[MemoryLabelIndex[idx]] : CachedSnapshot.UnknownMemlabelName,
                    stringBuilder.ToString(), fileLinkHashToFileName);
            }


            public void Dispose()
            {
                Id.Dispose();
                MemoryLabelIndex.Dispose();
                if (m_callstackSymbolsReadOp.IsCreated)
                {
                    // Dispose the read operation first to abort it ...
                    m_callstackSymbolsReadOp.Dispose();
                    // ... before disposing the result, as otherwise we'd sync on a pending read op.
                    callstackSymbols.Dispose();
                    m_callstackSymbolsReadOp = default;
                }
                Count = 0;
            }
        }

    }
}

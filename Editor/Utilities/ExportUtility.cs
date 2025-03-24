using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEditor;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;
using static Unity.MemoryProfiler.Editor.CallstacksTreeWindow;
using UnityEngine.UIElements;
using System.Collections;
using System;
using Unity.MemoryProfiler.Editor.UI;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor
{
    static class ExportUtility
    {
        public const int InvalidMappedAreaId = -1;
        public const string InvalidMappedAreaName = null;

        /// <summary>
        /// A function that takes the plaform name and Unity version as string and provides an <see cref="ICallstackMapping"/> for a snapshot from that platfrom and version.
        /// </summary>
        public static Action<string, string, Action<ICallstackMapping, Exception>> CallstackMappingProvider { get; set; } = null;

        public interface ICallstackMapping
        {
            Dictionary<string, int> AreaNameToId { get; }

            List<string> GetMappedAreas();
            bool IsAreaIgnored(int areaId);

            int TryMap(string callstack, out string areaName);

            bool UpdateMapping(string callstack, int areaId);
            void SaveMapping();
            void ClearNonExplicitMappings();
        }

        public static void WriteNativeRootCallstackInfoToJson(CachedSnapshot snapshot, long nativeRootId, bool invertedCallstacks = false)
        {
            void Callback(ICallstackMapping mappingInfo, Exception exception)
            {
                if (exception != null)
                {
                    if (exception is not OperationCanceledException)
                        Debug.LogException(exception);
                    return;
                }
                var symbolTree = CallstacksUtility.BuildSymbolNodeTree(snapshot, nativeRootId, latestStackEntryAsRoot: !invertedCallstacks);
                var model = CallstacksTreeWindow.GenerateTreeViewModel(snapshot, symbolTree, mappingInfo);
                var splitModel = CallstacksTreeWindow.SplitModelByAreaId(model, mappingInfo, !invertedCallstacks);
                WriteTreeToJson(snapshot, splitModel);
            }
            CallstackMappingProvider?.Invoke(snapshot.MetaData.Platform, snapshot.MetaData.UnityVersion, Callback);
        }

        public static void OpenCallstacksWindowForNativeRoot(CachedSnapshot snapshot, long nativeRootId, bool invertedCallstacks = false, bool callstackWindowOwnsSnapshot = false)
        {
            var clearedBar = false;

            void Callback(ICallstackMapping mappingInfo, Exception exception)
            {
                try
                {
                    if (exception != null)
                    {
                        if (exception is not OperationCanceledException)
                            Debug.LogException(exception);
                        return;
                    }
                    ProgressBarDisplay.ShowBar("Opening Callstack Window");
                    ProgressBarDisplay.UpdateProgress(0.3f, "buidling symbol tree");
                    var symbolTree = CallstacksUtility.BuildSymbolNodeTree(snapshot, nativeRootId, latestStackEntryAsRoot: !invertedCallstacks);
                    ProgressBarDisplay.ClearBar();
                    CallstacksUtility.OpenCallstackTreeWindow(snapshot, mappingInfo, invertedCallstacks, symbolTree, true, callstackWindowOwnsSnapshot);
                    clearedBar = true;
                }
                finally
                {
                    if (!clearedBar)
                    {
                        ProgressBarDisplay.ClearBar();
                        snapshot.Dispose();
                    }
                }
            }
            CallstackMappingProvider?.Invoke(snapshot.MetaData.Platform, snapshot.MetaData.UnityVersion, Callback);
        }

        public static void WriteNativeRootCallstackInfoToCsv(CachedSnapshot snapshot, long nativeRootId, bool invertedCallstacks = false)
        {
            var path = EditorUtility.SaveFilePanel("Export CSV by Call Stack", MemoryProfilerSettings.LastImportPath, "AllocationsByCallstack", "csv");
            var symbolTree = CallstacksUtility.BuildSymbolNodeTree(snapshot, nativeRootId, latestStackEntryAsRoot: !invertedCallstacks);
            var streamWriter = new StreamWriter(path);
            streamWriter.AutoFlush = true;

            ProgressBarDisplay.ShowBar("Writing Callstack Info to CSV");
            ProgressBarDisplay.UpdateProgress(0.3f, "writing lines ... ");
            long lineCount = 0;
            try
            {
                // Write Header
                streamWriter.WriteLine("\"Call-stack\";\"Item Index\";\"Size in Byte\";");

                foreach (var entry in CallstacksUtility.WalkCallstackNodes(snapshot, symbolTree))
                {
                    switch (entry.ItemIndex.Id)
                    {
                        case CachedSnapshot.SourceIndex.SourceId.None:
                            break;
                        case CachedSnapshot.SourceIndex.SourceId.SystemMemoryRegion:
                            break;
                        case CachedSnapshot.SourceIndex.SourceId.NativeMemoryRegion:
                            break;
                        case CachedSnapshot.SourceIndex.SourceId.NativeAllocation:
                            if (++lineCount % 5000 == 0)
                                ProgressBarDisplay.UpdateProgress(0.4f, string.Format("Written {0} lines", lineCount));
                            streamWriter.WriteLine($"\"{entry.Callstack}\";\"{entry.ItemIndex}\";\"{snapshot.NativeAllocations.Size[entry.ItemIndex.Index]}\";");
                            break;
                        case CachedSnapshot.SourceIndex.SourceId.ManagedHeapSection:
                            break;
                        case CachedSnapshot.SourceIndex.SourceId.NativeObject:
                            break;
                        case CachedSnapshot.SourceIndex.SourceId.ManagedObject:
                            break;
                        case CachedSnapshot.SourceIndex.SourceId.NativeType:
                            break;
                        case CachedSnapshot.SourceIndex.SourceId.ManagedType:
                            break;
                        case CachedSnapshot.SourceIndex.SourceId.NativeRootReference:
                            break;
                        case CachedSnapshot.SourceIndex.SourceId.GfxResource:
                            break;
                        default:
                            break;
                    }
                }
            }
            finally
            {
                ProgressBarDisplay.ClearBar();
                streamWriter.Flush();
                streamWriter.Close();
                EditorUtility.RevealInFinder(path);
            }
        }

        class JsonStreamWriter
        {
            public ICollection StackToUseForDepth;
            StreamWriter m_Writer;
            const char k_Tab = '\t';
            int k_BaseIndentWithStack = 2;

            public JsonStreamWriter(StreamWriter writer)
            {
                m_Writer = writer;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Write(char value)
            {
                m_Writer.Write(value);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Write(string value)
            {
                m_Writer.Write(value);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void WriteWithDepth(char value)
            {
                WriteDepth();
                m_Writer.Write(value);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void WriteWithDepth(char value, int extraDepth)
            {
                WriteExtraDepth(extraDepth);
                m_Writer.Write(value);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void WriteWithDepth(string value)
            {
                WriteDepth();
                m_Writer.Write(value);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void WriteWithDepth(string value, int extraDepth)
            {
                WriteExtraDepth(extraDepth);
                m_Writer.Write(value);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void WriteLineWithDepth(string value)
            {
                WriteDepth();
                m_Writer.WriteLine(value);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void WriteLineWithDepth(string value, int extraDepth)
            {
                WriteExtraDepth(extraDepth);
                m_Writer.WriteLine(value);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void WriteLineWithDepth(char value)
            {
                WriteDepth();
                m_Writer.WriteLine(value);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void WriteLineWithDepth(char value, int extraDepth)
            {
                WriteExtraDepth(extraDepth);
                m_Writer.WriteLine(value);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void WriteDepth()
            {
                if (StackToUseForDepth != null)
                    WriteDepth(k_BaseIndentWithStack + StackToUseForDepth.Count * 2);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void WriteExtraDepth(int additionalDepth)
            {
                if (StackToUseForDepth != null)
                    WriteDepth(k_BaseIndentWithStack + StackToUseForDepth.Count * 2 + additionalDepth);
                else
                    WriteDepth(additionalDepth);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void WriteDepth(int depth)
            {
                for (int i = 0; i < depth; i++)
                {
                    m_Writer.Write(k_Tab);
                }
            }

            public void DataToJson(CachedSnapshot snapshot, in TreeViewItemData<SymbolTreeViewItemData> data)
            {
                // Start the class entry
                WriteLineWithDepth('{');
                var unterminatedLine = false;
                if (data.data.CallstackEntry != null)
                {
                    WriteWithDepth($"\"Callstack\": \"{data.data.CallstackEntry}\"", 1);
                    unterminatedLine = true;
                }

                if (data.data.AreaId != ExportUtility.InvalidMappedAreaId)
                {
                    if (unterminatedLine)
                        m_Writer.WriteLine(',');
                    unterminatedLine = true;
                    WriteWithDepth($"\"Area\": \"{data.data.AreaName}\"", 1);
                }

                if (data.data.ItemIndex.Valid)
                {
                    if (unterminatedLine)
                        m_Writer.WriteLine(',');
                    WriteLineWithDepth($"\"Item Index\": \"{data.data.ItemIndex}\",", 1);
                    unterminatedLine = true;
                    var name = data.data.ItemIndex.Id == SourceIndex.SourceId.NativeAllocation ?
                        NativeAllocationTools.ProduceNativeAllocationName(data.data.ItemIndex, snapshot, true) :
                        data.data.ItemIndex.GetName(snapshot);
                    WriteWithDepth($"\"Name\": \"{name}\"", 1);
                }

                if (data.data.Size > 0)
                {
                    if (unterminatedLine)
                        m_Writer.WriteLine(',');
                    WriteWithDepth($"\"Size in B\": \"{data.data.Size}\"", 1);
                    unterminatedLine = true;
                }

                if (unterminatedLine)
                    m_Writer.WriteLine(data.children is List<TreeViewItemData<SymbolTreeViewItemData>> rootList && rootList.Count > 0 ? ',' : "");

                //not terminating the class here because the main wrtier algo does that after writing out the potentially nested child entries.
            }

            public void DataToJson(CachedSnapshot snapshot, in CallstackSymbolNode data)
            {
                // Start the class entry
                WriteLineWithDepth('{');
                var unterminatedLine = false;
                if (data.Symbol != 0)
                {
                    var callstack = new StringBuilder();
                    snapshot.NativeAllocationSites.AppendCallstackLine(snapshot.NativeCallstackSymbols, data.Symbol, callstack, simplifyCallStacks: true, clickableCallStacks: false, terminateWithLineBreak: false);
                    WriteWithDepth($"\"Callstack\": \"{callstack}\"", 1);
                    unterminatedLine = true;
                }

                if (data.Values?.Count > 0)
                {
                    if (unterminatedLine)
                        m_Writer.WriteLine(',');
                    WriteLineWithDepth($"\"Items\": [", 1);

                    var last = data.Values.Count - 1;
                    for (int i = 0; i < data.Values.Count; i++)
                    {
                        WriteLineWithDepth('{', 2);
                        WriteLineWithDepth($"\"Index\": \"{data.Values[i]}\",", 3);
                        if (data.Values[i].Id == SourceIndex.SourceId.NativeAllocation)
                            WriteLineWithDepth($"\"Size in B\": \"{snapshot.NativeAllocations.Size[data.Values[i].Index]}\",", 3);
                        var name = data.Values[i].Id == SourceIndex.SourceId.NativeAllocation ?
                            NativeAllocationTools.ProduceNativeAllocationName(data.Values[i], snapshot, true) :
                            data.Values[i].GetName(snapshot);
                        WriteLineWithDepth($"\"Name\": \"{name}\"", 3);

                        if (i < last)
                            WriteLineWithDepth("},", 2);
                        else
                            WriteLineWithDepth('}', 2);
                    }

                    WriteWithDepth(']', 1);
                    unterminatedLine = true;
                }

                //if (data.data.Size > 0)
                //{
                //    if (unterminatedLine)
                //        m_Writer.WriteLine(',');
                //    WriteWithDepth($"\"Size\": \"{data.data.Size}\"", 1);
                //    unterminatedLine = true;
                //}

                if (unterminatedLine)
                    m_Writer.WriteLine(data.ChildNodes?.Count > 0 ? ',' : "");

                //not terminating the class here because the main wrtier algo does that after writing out the potentially nested child entries.
            }
        }

        public static void WriteTreeToJson(CachedSnapshot snapshot, CallstackSymbolNode rootNode, bool mergeSingleEntryBranches = false)
        {
            var path = EditorUtility.SaveFilePanel("Export json by Call-Stack", MemoryProfilerSettings.LastImportPath, "AllocationsByCallstack", "json");
            if (path == null)
                return;
            var streamWriter = new StreamWriter(path);
            var jsonWriter = new JsonStreamWriter(streamWriter);
            streamWriter.AutoFlush = true;
            ProgressBarDisplay.ShowBar("Writing Callstack Info to JSON");
            ProgressBarDisplay.UpdateProgress(0.1f, "Writing out json objects ... ");
            try
            {
                jsonWriter.WriteLineWithDepth('{');
                const string childrenJson = "\"Children\": [";
                jsonWriter.WriteLineWithDepth("\"Base Nodes\": [", 1);
                var roots = new List<CallstackSymbolNode>(rootNode.ChildNodes.Values);
                for (int rootIndex = 0; rootIndex < roots.Count; rootIndex++)
                {
                    var root = roots[rootIndex];

                    var nodeStack = new Stack<(IEnumerator<CallstackSymbolNode>, int, int)>();
                    jsonWriter.StackToUseForDepth = nodeStack;

                    jsonWriter.DataToJson(snapshot, root);
                    if (root.ChildNodes?.Values is Dictionary<ulong, CallstackSymbolNode>.ValueCollection rootList && rootList.Count > 0)
                        nodeStack.Push((rootList.GetEnumerator(), rootList.Count, rootList.Count));
                    else
                    {
                        jsonWriter.WriteLineWithDepth("},");
                        continue;
                    }
                    ProgressBarDisplay.UpdateProgress((float)roots.Count / rootIndex, string.Format("Writing out json objects for branch {0}/{1}", rootIndex + 1, roots.Count));
                    // start writing out the roots child notes
                    jsonWriter.WriteLineWithDepth(childrenJson, -1);
                    while (nodeStack.Count > 0)
                    {
                        var nodeIterator = nodeStack.Pop();

                        if (nodeIterator.Item1.MoveNext())
                        {
                            // Write out the child of the current nodeIterator
                            var remainingSiblings = --nodeIterator.Item2;
                            nodeStack.Push(nodeIterator);
                            jsonWriter.DataToJson(snapshot, nodeIterator.Item1.Current);

                            if (nodeIterator.Item1.Current.ChildNodes?.Values is Dictionary<ulong, CallstackSymbolNode>.ValueCollection list && list.Count > 0)
                            {
                                // start listing the current nodeIterator child's children
                                jsonWriter.WriteLineWithDepth(childrenJson, 1);
                                nodeStack.Push((list.GetEnumerator(), list.Count, list.Count));
                                continue;
                            }
                            if (remainingSiblings > 0)
                            {
                                // the current node's child had no children to list but the child has siblings, close the json class with a comma
                                jsonWriter.WriteLineWithDepth("},");
                                continue;
                            }
                            else
                            {
                                // This was the last child of the current node, and it had no children close it out
                                // close last Node Data class
                                jsonWriter.WriteLineWithDepth('}');
                                nodeIterator = nodeStack.Pop();
                            }
                        }

                        // if it had children, close the array
                        if (nodeIterator.Item3 > 0)
                        {
                            jsonWriter.WriteLineWithDepth("]", 1);
                        }

                        // close the node class
                        if (nodeStack.Count > 0)
                        {
                            if (nodeStack.Peek().Item2 > 0)
                                // it has siblings
                                jsonWriter.WriteLineWithDepth("},");
                            else
                                jsonWriter.WriteLineWithDepth("}");
                        }
                    }
                    if (rootIndex < roots.Count - 1)
                        // the root had siblings
                        jsonWriter.WriteLineWithDepth("},");
                    else
                        jsonWriter.WriteLineWithDepth("}");
                }
                jsonWriter.StackToUseForDepth = null;
                jsonWriter.WriteLineWithDepth(']', 1);
                streamWriter.WriteLine('}');
            }
            finally
            {
                ProgressBarDisplay.ClearBar();
                streamWriter.Flush();
                streamWriter.Close();
                EditorUtility.RevealInFinder(path);
            }
        }

        /// <summary>
        /// Similar to <see cref="WriteTreeToJson(CachedSnapshot, CallstackSymbolNode, bool)"/> but using a built TreeView model with sizes per level.
        /// </summary>
        /// <param name="snapshot"></param>
        /// <param name="model"></param>
        /// <param name="mergeSingleEntryBranches"></param>
        public static void WriteTreeToJson(CachedSnapshot snapshot, TreeViewItemData<SymbolTreeViewItemData> model, bool mergeSingleEntryBranches = false)
        {
            var roots = model.children as IList<TreeViewItemData<SymbolTreeViewItemData>>;

            var path = EditorUtility.SaveFilePanel("Export json by Call-Stack", MemoryProfilerSettings.LastImportPath, "AllocationsByCallstack", "json");
            if (string.IsNullOrEmpty(path))
                return;
            var streamWriter = new StreamWriter(path);
            var jsonWriter = new JsonStreamWriter(streamWriter);
            streamWriter.AutoFlush = true;
            ProgressBarDisplay.ShowBar("Writing Callstack Info to JSON");
            ProgressBarDisplay.UpdateProgress(0.1f, "Writing out json objects ... ");
            try
            {
                jsonWriter.WriteLineWithDepth('{');
                jsonWriter.WriteLineWithDepth($"\"Total Size\": \"{EditorUtility.FormatBytes((long)model.data.Size)}\",", 1);
                jsonWriter.WriteLineWithDepth($"\"Total Size in B\": {model.data.Size},", 1);
                const string childrenJson = "\"Children\": [";
                jsonWriter.WriteLineWithDepth("\"Base Nodes\": [", 1);
                for (int rootIndex = 0; rootIndex < roots.Count; rootIndex++)
                {
                    var root = roots[rootIndex];

                    var nodeStack = new Stack<(IEnumerator<TreeViewItemData<SymbolTreeViewItemData>>, int, int)>();
                    jsonWriter.StackToUseForDepth = nodeStack;

                    jsonWriter.DataToJson(snapshot, root);
                    if (root.children is List<TreeViewItemData<SymbolTreeViewItemData>> rootList && rootList.Count > 0)
                        nodeStack.Push((rootList.GetEnumerator(), rootList.Count, rootList.Count));
                    else
                    {
                        jsonWriter.WriteLineWithDepth("},");
                        continue;
                    }
                    ProgressBarDisplay.UpdateProgress((float)roots.Count / rootIndex, string.Format("Writing out json objects for branch {0}/{1}", rootIndex + 1, roots.Count));
                    // start writing out the roots child notes
                    jsonWriter.WriteLineWithDepth(childrenJson, -1);
                    while (nodeStack.Count > 0)
                    {
                        var nodeIterator = nodeStack.Pop();

                        if (nodeIterator.Item1.MoveNext())
                        {
                            // Write out the child of the current nodeIterator
                            var remainingSiblings = --nodeIterator.Item2;
                            nodeStack.Push(nodeIterator);
                            jsonWriter.DataToJson(snapshot, nodeIterator.Item1.Current);

                            if (nodeIterator.Item1.Current.children is List<TreeViewItemData<SymbolTreeViewItemData>> list && list.Count > 0)
                            {
                                // start listing the current nodeIterator child's children
                                jsonWriter.WriteLineWithDepth(childrenJson, 1);
                                nodeStack.Push((list.GetEnumerator(), list.Count, list.Count));
                                continue;
                            }
                            if (remainingSiblings > 0)
                            {
                                // the current node's child had no children to list but the child has siblings, close the json class with a comma
                                jsonWriter.WriteLineWithDepth("},");
                                continue;
                            }
                            else
                            {
                                // This was the last child of the current node, and it had no children close it out
                                // close last Node Data class
                                jsonWriter.WriteLineWithDepth('}');
                                nodeIterator = nodeStack.Pop();
                            }
                        }

                        // if it had children, close the array
                        if (nodeIterator.Item3 > 0)
                        {
                            jsonWriter.WriteLineWithDepth("]", 1);
                        }

                        // close the node class
                        if (nodeStack.Count > 0)
                        {
                            if (nodeStack.Peek().Item2 > 0)
                                // it has siblings
                                jsonWriter.WriteLineWithDepth("},");
                            else
                                jsonWriter.WriteLineWithDepth("}");
                        }
                    }
                    if (rootIndex < roots.Count - 1)
                        // the root had siblings
                        jsonWriter.WriteLineWithDepth("},");
                    else
                        jsonWriter.WriteLineWithDepth("}");
                }
                jsonWriter.StackToUseForDepth = null;
                jsonWriter.WriteLineWithDepth(']', 1);
                streamWriter.WriteLine('}');
            }
            finally
            {
                ProgressBarDisplay.ClearBar();
                streamWriter.Flush();
                streamWriter.Close();
                EditorUtility.RevealInFinder(path);
            }
        }
    }
}

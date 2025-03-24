using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Diagnostics;
using UnityEditor;
using UnityEngine.UIElements;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;
using static Unity.MemoryProfiler.Editor.CallstacksTreeWindow;
using static Unity.MemoryProfiler.Editor.ExportUtility;

namespace Unity.MemoryProfiler.Editor
{
    readonly struct PartialCallstackSymbolsRef<T> : IEquatable<PartialCallstackSymbolsRef<T>>, IEqualityComparer<PartialCallstackSymbolsRef<T>>
        where T : unmanaged
    {
        readonly DynamicArrayRef<T> m_CallstackRef;
        public readonly long Depth;
        readonly bool m_Inverted;
        readonly int m_HashCode;

        public readonly T this[long idx]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Checks.CheckIndexInRangeAndThrow(idx, Depth);
                if (m_Inverted)
                    return m_CallstackRef[m_CallstackRef.Count - 1 - idx];
                else
                    return m_CallstackRef[idx];
            }
        }

        public PartialCallstackSymbolsRef(in DynamicArrayRef<T> callstackRef, long index, bool inverted)
        {
            m_CallstackRef = callstackRef;
            Depth = inverted ? callstackRef.Count - index : index + 1;
            m_Inverted = inverted;
            m_HashCode = BuildHashCode(callstackRef, Depth, inverted);
        }

        public PartialCallstackSymbolsRef<T> GetParentSymbolChain()
        {
            var indexForCutoff = m_Inverted ? m_CallstackRef.Count - (Depth - 1) : Depth - 2;
            return new PartialCallstackSymbolsRef<T>(m_CallstackRef, indexForCutoff, m_Inverted);
        }

        [Burst.BurstCompile(CompileSynchronously = true, DisableDirectCall = false, OptimizeFor = Burst.OptimizeFor.Performance)]
        static int BuildHashCode(in DynamicArrayRef<T> callstackRef, long depth, bool inverted)
        {
            if (depth <= 0)
                return 0;

            if (inverted)
            {
                var hashCode = HashCode.Combine(callstackRef[callstackRef.Count - 1], depth); ;
                for (long i = 1; i < depth; ++i)
                {
                    hashCode = HashCode.Combine(hashCode, callstackRef[callstackRef.Count - 1 - i]);
                }
                return hashCode;
            }
            else
            {
                var hashCode = HashCode.Combine(callstackRef[0], depth);
                for (long i = 1; i < depth; ++i)
                {
                    hashCode = HashCode.Combine(hashCode, callstackRef[i]);
                }
                return hashCode;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => m_HashCode;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHashCode(PartialCallstackSymbolsRef<T> partialCallstackSymbolsRef) => partialCallstackSymbolsRef.m_HashCode;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(PartialCallstackSymbolsRef<T> other) => m_HashCode == other.m_HashCode;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is PartialCallstackSymbolsRef<T> other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(PartialCallstackSymbolsRef<T> x, PartialCallstackSymbolsRef<T> y) => x.Equals(y);


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(PartialCallstackSymbolsRef<T> x, PartialCallstackSymbolsRef<T> y) => x.m_HashCode == y.m_HashCode;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(PartialCallstackSymbolsRef<T> x, PartialCallstackSymbolsRef<T> y) => x.m_HashCode != y.m_HashCode;

        public override string ToString()
        {
            if (Depth > 0)
                return $"(Depth: {Depth}, LastSymbol: {m_CallstackRef[Depth - 1]} HashCode: {m_HashCode})";
            return "(Invalid PartialCallstackSymbolsRef)";
        }
    }

    struct CallstackSymbolNode : CallstacksUtility.INode<ulong, SourceIndex, CallstackSymbolNode>
    {
        public PartialCallstackSymbolsRef<ulong> ParentSymbolsChain { get; set; }
        public ulong Key { get; set; }
        public ulong Symbol => Key;
        public List<SourceIndex> Values { get; set; }
        public Dictionary<ulong, CallstackSymbolNode> ChildNodes { get; set; }
    }

    static class CallstacksUtility
    {
        /// <summary>
        ///
        /// </summary>
        /// <param name="snapshot"></param>
        /// <param name="nativeRootId">The Root Reference ID from <see cref="CachedSnapshot.NativeAllocationEntriesCache.RootReferenceId"/> for which the symbol tree should be build, or <c><see cref="CachedSnapshot.NativeRootReferenceEntriesCache.InvalidRootId"/> -1</c> if all should be build.</param>
        /// <param name="latestStackEntryAsRoot">Build tree nodes such that the latest stack entry is the root and the closer it gets to the leaves, the older the stack entries.
        /// For a tree it this means that they all share MemoryManager::Allocate as the latest entry and root the Thread start as the oldest (if the collected depth suffices).
        /// Especially when the callstack depth does not suffice, this can lead to a cleaner tree.
        /// </param>
        /// <returns>The root node for the tree.</returns>
        public static CallstackSymbolNode BuildSymbolNodeTree(CachedSnapshot snapshot, long nativeRootId, bool latestStackEntryAsRoot = true)
        {
            var buildAll = nativeRootId < NativeRootReferenceEntriesCache.InvalidRootId;
            var nativeAllocs = snapshot.NativeAllocations;
            var symbolTree = new CallstackSymbolNode
            {
                ChildNodes = new Dictionary<ulong, CallstackSymbolNode>()
            };
            for (long nativeAllocationIndex = 0; nativeAllocationIndex < nativeAllocs.Count; nativeAllocationIndex++)
            {
                if (nativeAllocs.RootReferenceId[nativeAllocationIndex] == nativeRootId || buildAll)
                {
                    var siteId = snapshot.NativeAllocations.AllocationSiteId[nativeAllocationIndex];
                    if (siteId == NativeAllocationSiteEntriesCache.SiteIdNullPointer)
                        continue;

                    var callstackInfo = snapshot.NativeAllocationSites.GetCallStackInfo(siteId);
                    if (!callstackInfo.Valid || callstackInfo.CallstackSymbols.Count <= 0)
                        continue;
                    var currentNode = symbolTree;
                    var lastSymbolIndex = callstackInfo.CallstackSymbols.Count - 1;
                    if (latestStackEntryAsRoot)
                    {
                        for (long symbolIndex = 0; symbolIndex < lastSymbolIndex; symbolIndex++)
                            currentNode.GetOrAddNode(callstackInfo.CallstackSymbols, symbolIndex, false, out currentNode);
                    }
                    else
                    {
                        // iterate in reverse as callstacks are listed in the order of latest stack entry first, i.e. at index 0.
                        for (long symbolIndex = lastSymbolIndex; symbolIndex >= 0; --symbolIndex)
                            currentNode.GetOrAddNode(callstackInfo.CallstackSymbols, symbolIndex, true, out currentNode);
                        lastSymbolIndex = 0;
                    }
                    currentNode.GetOrAddToNodeList(callstackInfo.CallstackSymbols, lastSymbolIndex, !latestStackEntryAsRoot, new SourceIndex(SourceIndex.SourceId.NativeAllocation, nativeAllocationIndex));
                }
            }
            return symbolTree;
        }

        public struct CallstackNodeWalker
        {
            public ulong Symbol { get; internal set; }
            public ulong ParentSymbol { get; internal set; }
            public PartialCallstackSymbolsRef<ulong> ParentSymbolsChain { get; internal set; }
            public StringBuilder Callstack { get; internal set; }
            public SourceIndex ItemIndex { get; internal set; }
        }

        struct CallstackNodeStackEntry
        {
            public CallstackSymbolNode Node;
            public Dictionary<ulong, CallstackSymbolNode>.Enumerator ChildNodeIterator;
            public StringBuilder SymbolicatedCallstack;
        }

        /// <summary>
        /// Walk the call stack node graph and yield for each item.
        /// </summary>
        /// <param name="snapshot"></param>
        /// <param name="rootNode"></param>
        /// <param name="yieldCombinedCallstack">If true, each yield contains all stack lines up to the current entry. If false, only the current stack line will be yielded. Mostly usefull for building Tree View UIs.</param>
        /// <param name="yieldPerCallstackLine">If true, each stack line will be yielded, even if it does not contain an indexed item entry. Usefull for building Tree View UIs.</param>
        /// <param name="yieldCallstackLinesAfterItems">If true and if <paramref name="yieldPerCallstackLine"/> is true, it will yield the parent for any previous items after the items. Usefull for building UI TK Tree View UIs, rather than IMGUI ones.</param>
        /// <returns></returns>
        public static IEnumerable<CallstackNodeWalker> WalkCallstackNodes(CachedSnapshot snapshot, CallstackSymbolNode rootNode, bool yieldCombinedCallstack = true, bool yieldPerCallstackLine = false, bool yieldCallstackLinesAfterItems = true)
        {
            var stack = new Stack<CallstackNodeStackEntry>();
            //stack.Push(new CallstackNodeStackEntry
            //    {
            //        Node = rootNode,
            //        ChildCount = rootNode.ChildNodes.Count,
            //        ChildCountRemaining = rootNode.ChildNodes.Count,
            //        ChildNodeIterator = rootNode.ChildNodes.GetEnumerator(),
            //        SymbolicatedCallstack = new StringBuilder(),
            //    });
            foreach (var rootChild in rootNode.ChildNodes)
            {
                var firstCallstackLine = new StringBuilder();

                snapshot.NativeAllocationSites.AppendCallstackLine(snapshot.NativeCallstackSymbols, rootChild.Key, firstCallstackLine, simplifyCallStacks: true, clickableCallStacks: false, terminateWithLineBreak: yieldCombinedCallstack);
                stack.Push(new CallstackNodeStackEntry
                {
                    Node = rootChild.Value,
                    ChildNodeIterator = rootChild.Value.ChildNodes.GetEnumerator(),
                    SymbolicatedCallstack = firstCallstackLine,
                });
                while (stack.Count > 0)
                {
                    var currentNodeLevel = stack.Pop();
                    if (currentNodeLevel.SymbolicatedCallstack == null)
                    {
                        var currentNodeLevelCallstack = new StringBuilder(yieldCombinedCallstack && stack.TryPeek(out var parentScope) && parentScope.SymbolicatedCallstack != null
                            ? parentScope.SymbolicatedCallstack.ToString() : string.Empty);
                        snapshot.NativeAllocationSites.AppendCallstackLine(snapshot.NativeCallstackSymbols, (ulong)currentNodeLevel.Node.Symbol, currentNodeLevelCallstack, simplifyCallStacks: true, clickableCallStacks: false, terminateWithLineBreak: yieldCombinedCallstack);
                        currentNodeLevel.SymbolicatedCallstack = currentNodeLevelCallstack;
                    }
                    while (currentNodeLevel.ChildNodeIterator.MoveNext())
                    {
                        stack.Push(currentNodeLevel);

                        var currentNode = currentNodeLevel.ChildNodeIterator.Current;

                        var callstack = new StringBuilder(yieldCombinedCallstack ? currentNodeLevel.SymbolicatedCallstack.ToString() : string.Empty);
                        snapshot.NativeAllocationSites.AppendCallstackLine(snapshot.NativeCallstackSymbols, (ulong)currentNode.Key, callstack, simplifyCallStacks: true, clickableCallStacks: false, terminateWithLineBreak: yieldCombinedCallstack);

                        currentNodeLevel = new CallstackNodeStackEntry
                        {
                            Node = currentNode.Value,
                            ChildNodeIterator = currentNode.Value.ChildNodes.GetEnumerator(),
                            SymbolicatedCallstack = callstack,
                        };
                    }

                    if (yieldPerCallstackLine && !yieldCallstackLinesAfterItems)
                    {
                        yield return new CallstackNodeWalker
                        {
                            Symbol = currentNodeLevel.Node.Symbol,
                            ParentSymbol = stack.TryPeek(out var parentNode) ? parentNode.Node.Symbol : 0,
                            ParentSymbolsChain = currentNodeLevel.Node.ParentSymbolsChain,
                            Callstack = currentNodeLevel.SymbolicatedCallstack,
                            ItemIndex = default,
                        };
                    }
                    // does the element have values to print?
                    if (currentNodeLevel.Node.Values?.Count > 0)
                    {
                        foreach (var itemIndex in currentNodeLevel.Node.Values)
                        {
                            yield return new CallstackNodeWalker
                            {
                                Symbol = currentNodeLevel.Node.Symbol,
                                ParentSymbol = stack.TryPeek(out var parentNode) ? parentNode.Node.Symbol : 0,
                                ParentSymbolsChain = currentNodeLevel.Node.ParentSymbolsChain,
                                // When yielding per callstack line, the callstack for this line is reported either before or after all of these items
                                Callstack = yieldPerCallstackLine ? null : currentNodeLevel.SymbolicatedCallstack,
                                ItemIndex = itemIndex,
                            };
                        }
                    }
                    if (yieldPerCallstackLine && yieldCallstackLinesAfterItems)
                    {
                        yield return new CallstackNodeWalker
                        {
                            Symbol = currentNodeLevel.Node.Symbol,
                            ParentSymbol = stack.TryPeek(out var parentNode) ? parentNode.Node.Symbol : 0,
                            ParentSymbolsChain = currentNodeLevel.Node.ParentSymbolsChain,
                            Callstack = currentNodeLevel.SymbolicatedCallstack,
                            ItemIndex = default,
                        };
                    }
                }
            }
        }

        public static void OpenCallstackTreeWindow(CachedSnapshot snapshot, ICallstackMapping mappingInfo, bool usesInvertedCallstacks, CallstackSymbolNode symbolTree, bool splitModelByArea = false, bool callstackWindowOwnsSnapshot = false)
        {
            var window = EditorWindow.GetWindow<CallstacksTreeWindow>("Call stacks");
            window.SetRoot(snapshot, mappingInfo, usesInvertedCallstacks, symbolTree, false, splitModelByArea, callstackWindowOwnsSnapshot);
        }

        public interface INode<TKey, TValue, TNode>
            where TNode : INode<TKey, TValue, TNode>, new()
            where TKey : unmanaged
        {
            PartialCallstackSymbolsRef<TKey> ParentSymbolsChain { get; set; }
            TKey Key { get; set; }
            List<TValue> Values { get; set; }
            Dictionary<TKey, TNode> ChildNodes { get; set; }
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static void GetOrAddToNodeList<TKey, TValue, TNode>(this INode<TKey, TValue, TNode> parentNode, TKey key, TValue valueToAdd)
        //    where TNode : INode<TKey, TValue, TNode>, new()
        //    where TKey : unmanaged
        //{
        //    if (parentNode.ChildNodes.TryGetValue(key, out var childNode))
        //    {
        //        if (childNode.Values == null)
        //        {
        //            childNode.Values = new List<TValue>();
        //            // store back in the Dictionary in case TNode is a struct
        //            parentNode.ChildNodes[key] = childNode;
        //        }
        //        childNode.Values.Add(valueToAdd);
        //    }
        //    else
        //    {
        //        childNode = new TNode();
        //        childNode.ChildNodes = new Dictionary<TKey, TNode>();
        //        childNode.Values = new List<TValue>{ valueToAdd };
        //        childNode.Key = key;
        //        parentNode.ChildNodes.Add(key, childNode);
        //    }
        //}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetOrAddToNodeList<TKey, TValue, TNode>(this INode<TKey, TValue, TNode> parentNode, in DynamicArrayRef<TKey> parentKeyChain, long index, bool inverted, TValue valueToAdd)
            where TNode : INode<TKey, TValue, TNode>, new()
            where TKey : unmanaged
        {
            var key = parentKeyChain[index];
            if (parentNode.ChildNodes.TryGetValue(key, out var childNode))
            {
                if (childNode.Values == null)
                {
                    childNode.Values = new List<TValue>();
                    // store back in the Dictionary in case TNode is a struct
                    parentNode.ChildNodes[key] = childNode;
                }
                childNode.Values.Add(valueToAdd);
            }
            else
            {
                childNode = new TNode();
                childNode.ChildNodes = new Dictionary<TKey, TNode>();
                childNode.Values = new List<TValue> { valueToAdd };
                childNode.Key = key;
                childNode.ParentSymbolsChain = new PartialCallstackSymbolsRef<TKey>(in parentKeyChain, index, inverted);
                parentNode.ChildNodes.Add(key, childNode);
            }
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //static void GetOrAddNode<TKey, TValue, TNode>(this INode<TKey, TValue, TNode> parentNode, TKey key, out TNode childNode)
        //    where TNode : INode<TKey, TValue, TNode>, new()
        //    where TKey : unmanaged
        //{
        //    if (!parentNode.ChildNodes.TryGetValue(key, out childNode))
        //    {
        //        childNode = new TNode();
        //        childNode.ChildNodes = new Dictionary<TKey, TNode>();
        //        childNode.Key = key;
        //        parentNode.ChildNodes.Add(key, childNode);
        //    }
        //}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void GetOrAddNode<TKey, TValue, TNode>(this INode<TKey, TValue, TNode> parentNode, in DynamicArrayRef<TKey> parentKeyChain, long index, bool inverted, out TNode childNode)
            where TNode : INode<TKey, TValue, TNode>, new()
            where TKey : unmanaged
        {
            var key = parentKeyChain[index];
            if (!parentNode.ChildNodes.TryGetValue(key, out childNode))
            {
                childNode = new TNode();
                childNode.ChildNodes = new Dictionary<TKey, TNode>();
                childNode.Key = key;
                childNode.ParentSymbolsChain = new PartialCallstackSymbolsRef<TKey>(in parentKeyChain, index, inverted);
                parentNode.ChildNodes.Add(key, childNode);
            }
        }
    }
}

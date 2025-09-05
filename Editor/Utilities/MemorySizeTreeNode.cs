using System;
using System.Collections.Generic;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    class MemorySizeTreeNode : IEquatable<MemorySizeTreeNode>
    {
        public MemorySize MemorySize;
        public Dictionary<SourceIndex, MemorySizeTreeNode> ChildNodes;

        public MemorySizeTreeNode() : this(default, null)
        {
        }

        public MemorySizeTreeNode(MemorySize memorySize, Dictionary<SourceIndex, MemorySizeTreeNode> childNodes = null)
        {
            MemorySize = memorySize;
            ChildNodes = childNodes;
        }

        public static bool operator ==(MemorySizeTreeNode l, MemorySizeTreeNode r) => l.Equals(r);
        public static bool operator !=(MemorySizeTreeNode l, MemorySizeTreeNode r) => !(l == r);

        public bool Equals(MemorySizeTreeNode other)
        {
            return (MemorySize == other.MemorySize)
                && (ChildNodes == other.ChildNodes);
        }
        public override bool Equals(object obj) => obj is MemorySize other && Equals(other);

        public override int GetHashCode() => (MemorySize, ChildNodes).GetHashCode();

        public override string ToString()
        {
            return $"(MemorySize: {MemorySize.ToString()} ChildNodes.Count: {ChildNodes?.Count.ToString() ?? "Null"})";
        }
    }
}

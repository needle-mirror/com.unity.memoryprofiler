namespace Unity.MemoryProfiler.Editor.UI
{
    // Aggregates warning-related status data for a tree node.
    // For leaf nodes, constructed from the node's own ObjectStatus via FromStatus().
    // For group nodes, computed by ORing children's bitmasks and summing their WarningCounts
    // in a single pass via the | operator.
    readonly struct ObjectStatusWarningInfo
    {
        // Bitmask of every ObjectStatus value present in this subtree.
        // Bit N corresponds to (ObjectStatus)N. For a leaf: exactly one bit (or 0 for None).
        // Enables recovering the status via GetStatus() and filtering for warnings via
        // ObjectStatusExtensions.WarningStatusesBitmask without a second traversal.
        public readonly ulong Bitmask;

        // Total count of objects whose status passes IsWarningStatus() in this subtree.
        public readonly int WarningCount;

        ObjectStatusWarningInfo(ulong bitmask, int warningCount)
        {
            Bitmask = bitmask;
            WarningCount = warningCount;
        }

        // Encodes a single leaf's status. Returns default (all-zero) for ObjectStatus.None.
        public static ObjectStatusWarningInfo FromStatus(ObjectStatus status)
        {
            if (status == ObjectStatus.None)
                return default;
            return new ObjectStatusWarningInfo(1ul << (int)status, status.IsWarningStatus() ? 1 : 0);
        }

        // Combines this node's info with another's: ORs bitmasks, sums warning counts.
        // Used to aggregate children's info into a parent group node.
        public static ObjectStatusWarningInfo operator |(ObjectStatusWarningInfo a, ObjectStatusWarningInfo b)
            => new ObjectStatusWarningInfo(a.Bitmask | b.Bitmask, a.WarningCount + b.WarningCount);

        // Recovers the single ObjectStatus from the bitmask.
        // Returns ObjectStatus.None when the bitmask is 0 (absent) or has multiple bits set (group).
        public ObjectStatus GetStatus()
        {
            if (Bitmask == 0 || (Bitmask & (Bitmask - 1)) != 0)
                return ObjectStatus.None;
            int bitIndex = 0;
            ulong remaining = Bitmask;
            while ((remaining >>= 1) != 0) bitIndex++;
            return (ObjectStatus)bitIndex;
        }
    }
}

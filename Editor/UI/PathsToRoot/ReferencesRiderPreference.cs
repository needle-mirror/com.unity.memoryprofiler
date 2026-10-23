using System;
using UnityEditor;

namespace Unity.MemoryProfiler.Editor.UI.PathsToRoot
{
    /// <summary>
    /// Owns the per-table default and <see cref="SessionState"/> persistence for which rider
    /// (<see cref="PathsToRootDetailView.ActiveTree"/>) the References panel opens on, so the two
    /// cannot drift apart. Keyed per <see cref="BreakdownTableId"/> only, not per table+snapshot:
    /// this is a reading-style preference, not a property of the capture, and <see cref="SessionState"/>
    /// has no key enumeration or bulk-erase, so snapshot-keyed entries could never be pruned.
    /// </summary>
    static class ReferencesRiderPreference
    {
        public static PathsToRootDetailView.ActiveTree GetDefault(BreakdownTableId table) => table switch
        {
            BreakdownTableId.RootsAndImpact => PathsToRootDetailView.ActiveTree.ReferencesTo,
            _ => PathsToRootDetailView.ActiveTree.ShortestPath,
        };

        static string Key(BreakdownTableId table)
            => $"com.unity.memoryprofiler.{nameof(PathsToRootDetailView)}.rider.{table}";

        /// <summary>
        /// Reads the stored rider for <paramref name="table"/>, falling back to <see cref="GetDefault"/>
        /// when nothing was stored yet or the stored value is not a valid <see cref="PathsToRootDetailView.ActiveTree"/>
        /// (e.g. a stale value from a since-reshaped enum) — an invalid value would make <c>BuildRoot</c>'s switch throw.
        /// </summary>
        public static PathsToRootDetailView.ActiveTree Get(BreakdownTableId table)
        {
            var storedValue = SessionState.GetInt(Key(table), (int)GetDefault(table));
            if (!Enum.IsDefined(typeof(PathsToRootDetailView.ActiveTree), storedValue))
                return GetDefault(table);

            return (PathsToRootDetailView.ActiveTree)storedValue;
        }

        public static void Set(BreakdownTableId table, PathsToRootDetailView.ActiveTree tree)
        {
            SessionState.SetInt(Key(table), (int)tree);
        }

        internal static void EraseForTests(BreakdownTableId table)
        {
            SessionState.EraseInt(Key(table));
        }
    }
}

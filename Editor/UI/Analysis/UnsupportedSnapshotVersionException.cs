using System;
using Unity.MemoryProfiler.Editor.UIContentData;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class UnsupportedSnapshotVersionException : Exception
    {
        public UnsupportedSnapshotVersionException(CachedSnapshot snapshot)
            : base(String.Format(TextContent.OutdatedSnapshotWarning, snapshot.FullPath, snapshot.MetaData.UnityVersion))
        {
        }

        // For breakdowns that are unavailable for a more specific reason than the snapshot's Unity version
        // (e.g. Roots & Impact data wasn't generated for this capture).
        public UnsupportedSnapshotVersionException(string message)
            : base(message)
        {
        }
    }
}

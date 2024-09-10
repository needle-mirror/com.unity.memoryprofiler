using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.MemoryProfiler.Editor
{
    internal class SnapshotFileListModel : IEquatable<SnapshotFileListModel>
    {
        public SnapshotFileListModel(
            DateTime snapshotDirectoryLastWriteTimestampUtc,
            IReadOnlyDictionary<uint, string> sessionNames,
            IReadOnlyList<SnapshotFileModel> allSnapshots,
            IReadOnlyList<uint> sortedSessionIds,
            IReadOnlyDictionary<uint, IGrouping<uint, SnapshotFileModel>> sessionsMap)
        {
            SnapshotDirectoryLastWriteTimestampUtc = snapshotDirectoryLastWriteTimestampUtc;
            SessionNames = sessionNames;
            AllSnapshots = allSnapshots;
            SortedSessionIds = sortedSessionIds;
            SessionsMap = sessionsMap;
        }

        public DateTime SnapshotDirectoryLastWriteTimestampUtc { get; private set; }
        public IReadOnlyDictionary<uint, string> SessionNames { get; }
        public IReadOnlyList<SnapshotFileModel> AllSnapshots { get; }
        public IReadOnlyList<uint> SortedSessionIds { get; }
        public IReadOnlyDictionary<uint, IGrouping<uint, SnapshotFileModel>> SessionsMap { get; }

        /// <summary>
        /// Updates the timestamp. Only call this if a new build of the model is equal to the old one, except for the timestamp.
        /// </summary>
        /// <param name="newTimestamp"></param>
        public void UpdateTimeStamp(DateTime newTimestamp)
        {
            SnapshotDirectoryLastWriteTimestampUtc = newTimestamp;
        }

        public bool Equals(SnapshotFileListModel other)
        {
            // purposefully ignoring SnapshotDirectoryTimestampUtc
            // the timestamp is irrelevant for the comparison as the content might be the same regardless of the timestamp

            if (ReferenceEquals(this, other))
                return true;
            if (other is null
                || AllSnapshots.Count != other.AllSnapshots.Count
                || SessionNames.Count != other.SessionNames.Count
                || (SessionsMap?.Count ?? -1) != (other.SessionsMap?.Count ?? -1)
                || (SortedSessionIds?.Count ?? -1) != (other.SortedSessionIds?.Count ?? -1))
                return false;

            // Go over all sorted session ids and compare the snapshots in each session to make sure they are the same
            for (int i = 0; i < SortedSessionIds?.Count; ++i)
            {
                var sessionId = SortedSessionIds[i];

                if (sessionId != other.SortedSessionIds[i]
                    || sessionId != other.SortedSessionIds[i]
                    || SessionNames[sessionId] != other.SessionNames[sessionId])
                    return false;
                var snapshotsInThisSession = SessionsMap[sessionId].GetEnumerator();
                var othersSnapshotsInThisSession = other.SessionsMap[sessionId].GetEnumerator();
                while (snapshotsInThisSession.MoveNext())
                {
                    // Make sure the other session has the same snapshot
                    if (!othersSnapshotsInThisSession.MoveNext()) return false;
                    if (!snapshotsInThisSession.Current.Equals(othersSnapshotsInThisSession.Current)) return false;
                }
                // Make sure the other session doesn't have more snapshots
                if (othersSnapshotsInThisSession.MoveNext()) return false;
            }
            return true;
        }
    }
}

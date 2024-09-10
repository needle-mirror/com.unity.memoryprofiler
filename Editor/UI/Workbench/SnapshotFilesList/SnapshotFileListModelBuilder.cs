using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.MemoryProfiler.Editor
{
    internal class SnapshotFileListModelBuilder
    {
        const string k_SessionNameTemplate = "Session {0}";
        IReadOnlyList<SnapshotFileModel> m_AllSnapshots;
        DateTime m_SnapshotDirectoryLastWriteTimestampUtc;

        public SnapshotFileListModelBuilder(IReadOnlyList<SnapshotFileModel> allSnapshots, DateTime snapshotDirectoryLastWriteTimestampUtc)
        {
            m_AllSnapshots = allSnapshots;
            m_SnapshotDirectoryLastWriteTimestampUtc = snapshotDirectoryLastWriteTimestampUtc;
        }

        /// <summary>
        /// Maps all sessions to the unique SessionId and generates session names
        /// </summary>
        /// <returns></returns>
        public SnapshotFileListModel Build()
        {
            var sessionNames = new Dictionary<uint, string>();

            if (MakeSortedSessionsListIds(m_AllSnapshots, out var sortedSessionIds, out var sessionsMap))
            {
                // Make session name based on the sorted order
                uint generatedSessionId = 1;
                foreach (var sessionId in sortedSessionIds)
                {
                    var children = sessionsMap[sessionId];
                    var sessionName = string.Format(k_SessionNameTemplate, generatedSessionId);
                    sessionNames[sessionId] = sessionName;
                    generatedSessionId++;
                }
            }

            return new SnapshotFileListModel(
                m_SnapshotDirectoryLastWriteTimestampUtc,
                sessionNames,
                m_AllSnapshots,
                sortedSessionIds,
                sessionsMap);
        }


        /// <summary>
        /// A utility function that makes a sorted list of snapshots sessions and dictionary of sorted list of snapshots inside each session
        /// </summary>
        /// <param name="snapshots">List of all snapshots to process</param>
        /// <param name="sortedSessionIds">Returned list of sorted sessions</param>
        /// <param name="sessionsMap">Returned dictionary of lists for each session id</param>
        /// <returns>True if successeful</returns>
        static bool MakeSortedSessionsListIds(in IReadOnlyList<SnapshotFileModel> snapshots, out List<uint> sortedSessionIds, out Dictionary<uint, IGrouping<uint, SnapshotFileModel>> sessionsMap)
        {
            if (snapshots.Count <= 0)
            {
                sortedSessionIds = null;
                sessionsMap = null;
                return false;
            }

            // Pre-sort snapshots
            var sortedSnapshots = new List<SnapshotFileModel>(snapshots);
            sortedSnapshots.Sort((l, r) => l.Timestamp.CompareTo(r.Timestamp));

            // Group snapshots by sessionId
            var _sessionsMap = sortedSnapshots.ToLookup(x => x.SessionId).ToDictionary(x => x.Key);

            // Sort sessionId list so that generated names order is the same as visual order in UI
            var _sortedSessionIds = _sessionsMap.Keys.ToList();
            _sortedSessionIds.Sort((l, r) => _sessionsMap[l].First().Timestamp.CompareTo(_sessionsMap[r].First().Timestamp));

            sessionsMap = _sessionsMap;
            sortedSessionIds = _sortedSessionIds;
            return true;
        }
    }
}

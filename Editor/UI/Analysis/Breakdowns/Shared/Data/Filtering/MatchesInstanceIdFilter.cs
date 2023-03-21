using System;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI
{
    class MatchesInstanceIdFilter : IInstancIdFilter
    {
        public CachedSnapshot SourceSnapshot { get; }

        public int Value => m_InstanceID;

        readonly int m_InstanceID;

        MatchesInstanceIdFilter(int InstanceID, CachedSnapshot sourceSnapshot)
        {
            m_InstanceID = InstanceID;
            SourceSnapshot = sourceSnapshot;
        }

        /// <summary>
        /// Creates a <see cref="MatchesInstanceIdFilter"/> and checks its validity.
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="sourceSnapshot">Needed for disambiguation in comparisons. Should only be null for limited cases such as user initiated searches.</param>
        /// <returns>null if not a valid filter, otherwise a <see cref="MatchesInstanceIdFilter"/> that fits the passed parameters.</returns>
        public static MatchesInstanceIdFilter Create(int instanceId, CachedSnapshot sourceSnapshot)
        {
#if DEBUG_VALIDATION
            // Cannot create a text filter without passing a snapshot for disambiguation in comparisons
            if(sourceSnapshot == null && instanceId != CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone)
            {
                Debug.LogWarning("Creating a Source Filter without providing a snapshot is not allowed unless it is a User initiated search");
                return null;
            }
#endif

            return new MatchesInstanceIdFilter(instanceId, sourceSnapshot);
        }

        /// <summary>
        /// Test if the provided '<paramref name="instanceID"/>' (which referes to a Unity Object in the optionally provided <paramref name="snapshot"/>) passes the filter.
        /// </summary>
        /// <param name="instanceID"></param>
        /// <param name="snapshot"></param>
        /// <returns>
        /// If a <paramref name="snapshot"/> is provided, returns false if the passed '<paramref name="snapshot"/>' indicates
        /// that the index could not relate to the snapshot that this filter's Instance ID relates to.
        /// Returns true if '<paramref name="instanceID"/>' is equal to the filter's InstanceID.
        /// Otherwise, returns false.</returns>
        public bool Passes(int instanceID, CachedSnapshot snapshot = null)
        {
            // if snapshots are provided, the sessiong GUID needs to match. Instance IDs stay valid accross sessions.
            if (snapshot != null && SourceSnapshot != null &&
                snapshot.MetaData.SessionGUID != SourceSnapshot.MetaData.SessionGUID)
                return false;

            return instanceID == m_InstanceID;
        }
    }
}

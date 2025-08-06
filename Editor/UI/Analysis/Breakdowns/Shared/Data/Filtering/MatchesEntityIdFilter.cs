using Debug = UnityEngine.Debug;
#if !ENTITY_ID_CHANGED_SIZE
// the official EntityId lives in the UnityEngine namespace, which might be be added as a using via the IDE,
// so to avoid mistakenly using a version of this struct with the wrong size, alias it here.
using EntityId = Unity.MemoryProfiler.Editor.EntityId;
#else
using EntityId = UnityEngine.EntityId;
// This should be greyed out by the IDE, otherwise you're missing an alias above
using UnityEngine;
#endif

namespace Unity.MemoryProfiler.Editor.UI
{
    class MatchesInstanceIdFilter : IEntityIdFilter
    {
        public CachedSnapshot SourceSnapshot { get; }

        public EntityId Value => m_InstanceID;

        readonly EntityId m_InstanceID;

        MatchesInstanceIdFilter(EntityId EntityId, CachedSnapshot sourceSnapshot)
        {
            m_InstanceID = EntityId;
            SourceSnapshot = sourceSnapshot;
        }

        /// <summary>
        /// Creates a <see cref="MatchesInstanceIdFilter"/> and checks its validity.
        /// </summary>
        /// <param name="entityId"></param>
        /// <param name="sourceSnapshot">Needed for disambiguation in comparisons. Should only be null for limited cases such as user initiated searches.</param>
        /// <returns>null if not a valid filter, otherwise a <see cref="MatchesInstanceIdFilter"/> that fits the passed parameters.</returns>
        public static MatchesInstanceIdFilter Create(EntityId entityId, CachedSnapshot sourceSnapshot)
        {
#if DEBUG_VALIDATION
            // Cannot create a text filter without passing a snapshot for disambiguation in comparisons
            if(sourceSnapshot == null && entityId != CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone)
            {
                Debug.LogWarning("Creating a Source Filter without providing a snapshot is not allowed unless it is a User initiated search");
                return null;
            }
#endif

            return new MatchesInstanceIdFilter(entityId, sourceSnapshot);
        }

        /// <summary>
        /// Test if the provided '<paramref name="entityId"/>' (which referes to a Unity Object in the optionally provided <paramref name="snapshot"/>) passes the filter.
        /// </summary>
        /// <param name="entityId"></param>
        /// <param name="snapshot"></param>
        /// <returns>
        /// If a <paramref name="snapshot"/> is provided, returns false if the passed '<paramref name="snapshot"/>' indicates
        /// that the index could not relate to the snapshot that this filter's Instance ID relates to.
        /// Returns true if '<paramref name="entityId"/>' is equal to the filter's EntityId.
        /// Otherwise, returns false.</returns>
        public bool Passes(EntityId entityId, CachedSnapshot snapshot = null)
        {
            // if snapshots are provided, the sessiong GUID needs to match. Instance IDs stay valid accross sessions.
            if (snapshot != null && SourceSnapshot != null &&
                snapshot.MetaData.SessionGUID != SourceSnapshot.MetaData.SessionGUID)
                return false;

            return entityId == m_InstanceID;
        }
    }
}

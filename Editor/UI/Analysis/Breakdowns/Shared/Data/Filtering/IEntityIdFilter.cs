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
    interface IEntityIdFilter : ITableFilter<EntityId>
    {
        public CachedSnapshot SourceSnapshot { get; }
    }
}

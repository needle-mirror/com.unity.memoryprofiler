using UnityEngine;
namespace Unity.MemoryProfiler.Editor.UI
{
    interface IInstancIdFilter : ITableFilter<InstanceID>
    {
        public CachedSnapshot SourceSnapshot { get; }
    }
}

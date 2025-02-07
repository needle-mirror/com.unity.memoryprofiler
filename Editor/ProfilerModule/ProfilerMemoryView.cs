using System.ComponentModel;

// Old and wrong namespace and assembly name. Kept around to not break Samver / force a major version bump.
// This file and the assmbly with the same name as this namespace should be moved into a separate folder with the next minor version bump to isolate it, creating a new assembly named Unity.MemoryProfiler.MemoryProfilerModule.Editor in its stead.
namespace Unity.MemoryProfiler.Editor.MemoryProfilerModule
{
    // This API should have not been exposed as public. It's kinda not worth it to deprecate and bump the major version for this one though.
    [EditorBrowsable(EditorBrowsableState.Never)] // aka: undoc / hide from docs
    public enum ProfilerMemoryView
    {
        Simple = 0,
        Detailed = 1
    }
}

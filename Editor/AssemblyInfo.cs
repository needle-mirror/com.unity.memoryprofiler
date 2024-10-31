using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Unity.MemoryProfiler.Editor.Tests")]
[assembly: InternalsVisibleTo("Unity.MemoryProfiler.Editor.PerformanceTests")]
[assembly: InternalsVisibleTo("Unity.MemoryProfiler.TestProject.Editor.Tests")]
// Moq uses DynamicProxyGenAssembly2 to generate proxies, which therefore requires access to internals to generate proxies of internal classes.
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

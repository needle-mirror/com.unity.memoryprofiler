namespace Unity.MemoryProfiler.Editor.Format
{
    public interface IReader
    {
        string FullPath { get; }
        uint FormatVersion { get; }
    }
}

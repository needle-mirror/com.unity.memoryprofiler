namespace Unity.MemoryProfiler.Editor.Format.QueriedSnapshot
{
    public enum ReadError : ushort
    {
        None = 0,
        Success,
        InProgress,
        FileReadFailed,
        FileNotFound,
        InvalidHeaderSignature,
        InvalidDirectorySignature,
        InvalidFooterSignature,
        InvalidChapterLocation,
        InvalidChapterSectionVersion,
        InvalidBlockSectionVersion,
        InvalidBlockSectionCount,
        InvalidEntryFormat,
        EmptyFormatEntry
    }
}

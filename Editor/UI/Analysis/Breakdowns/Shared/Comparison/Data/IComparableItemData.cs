#if UNITY_2022_1_OR_NEWER
namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// TODO: Move to private in the next major version
    /// </summary>
    [System.Obsolete("Will be removed in a future release")]
    public interface IComparableItemData
    {
        // The name of this item.
        string Name { get; }

        // The total size of this item, in bytes.
        ulong Size { get; }
    }

    interface IPrivateComparableItemData
    {
        // The name of this item.
        string Name { get; }

        // The total size of this item, in bytes.
        MemorySize Size { get; }
    }
}
#endif

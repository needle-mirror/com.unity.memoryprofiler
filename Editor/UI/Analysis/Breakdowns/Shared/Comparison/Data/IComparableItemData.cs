#if UNITY_2022_1_OR_NEWER
namespace Unity.MemoryProfiler.Editor.UI
{
    interface IComparableItemData
    {
        // The name of this item.
        string Name { get; }

        // The total size of this item, in bytes.
        MemorySize Size { get; }
    }
}
#endif

#if UNITY_2022_1_OR_NEWER
namespace Unity.MemoryProfiler.Editor.UI
{
    interface ITextFilter
    {
        bool TextPasses(string text);
    }
}
#endif

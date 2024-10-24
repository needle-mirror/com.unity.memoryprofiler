using System;
using Unity.MemoryProfiler.Editor.UIContentData;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal static class BreakdownDetailsViewControllerFactory
    {
        public static ViewController Create(CachedSnapshot snapshot, int itemId, string name, int childCount, CachedSnapshot.SourceIndex source)
        {
            if (IAnalysisViewSelectable.IsPredefinedCategory(itemId))
            {
                return (IAnalysisViewSelectable.Category)itemId switch
                {
                    IAnalysisViewSelectable.Category.Native => new SimpleDetailsViewController(name, TextContent.NativeDescription, string.Empty),
                    IAnalysisViewSelectable.Category.Managed => new SimpleDetailsViewController(name, TextContent.ManagedDescription, string.Empty),
                    IAnalysisViewSelectable.Category.ExecutablesAndMapped => new SimpleDetailsViewController(name, TextContent.ExecutablesAndMappedDescription, string.Empty),
                    IAnalysisViewSelectable.Category.Graphics => new SimpleDetailsViewController(name, TextContent.GraphicsEstimatedDescription, string.Empty),
                    IAnalysisViewSelectable.Category.GraphicsDisabled => new SimpleDetailsViewController(name, TextContent.GraphicsEstimatedDisabledDescription, string.Empty),
                    IAnalysisViewSelectable.Category.Unknown => new SimpleDetailsViewController(name, TextContent.UntrackedDescription, string.Empty),
                    IAnalysisViewSelectable.Category.UnknownEstimated => new SimpleDetailsViewController(name, TextContent.UntrackedEstimatedDescription, string.Empty),
                    IAnalysisViewSelectable.Category.AndroidRuntime => new SimpleDetailsViewController(name, SummaryTextContent.kAllMemoryCategoryDescriptionAndroid, string.Empty),

                    IAnalysisViewSelectable.Category.NativeReserved => new SimpleDetailsViewController(name, TextContent.NativeReservedDescription, string.Empty),
                    IAnalysisViewSelectable.Category.ManagedReserved => new SimpleDetailsViewController(name, TextContent.ManagedReservedDescription, string.Empty),
                    IAnalysisViewSelectable.Category.GraphicsReserved => new SimpleDetailsViewController(name, TextContent.NativeReservedDescription, string.Empty),

                    _ => throw new ArgumentException("Invalid or unknown item type"),
                };
            }

            return source.Id switch
            {
                CachedSnapshot.SourceIndex.SourceId.SystemMemoryRegion => new SimpleDetailsViewController(name, TextContent.SystemMemoryRegionDescription, string.Empty),
                CachedSnapshot.SourceIndex.SourceId.ManagedHeapSection => new SimpleDetailsViewController(name, TextContent.ManagedMemoryHeapDescription, string.Empty),
                CachedSnapshot.SourceIndex.SourceId.NativeMemoryRegion => new SimpleDetailsViewController(name, TextContent.NativeMemoryRegionDescription, string.Empty),

                CachedSnapshot.SourceIndex.SourceId.NativeAllocation => new ObjectDetailsViewController(snapshot, source, name: name, description: TextContent.NativeAllocationDescription),
                CachedSnapshot.SourceIndex.SourceId.NativeObject or
                CachedSnapshot.SourceIndex.SourceId.ManagedObject or
                CachedSnapshot.SourceIndex.SourceId.GfxResource => new ObjectDetailsViewController(snapshot, source, name: name),

                CachedSnapshot.SourceIndex.SourceId.NativeRootReference => new ObjectDetailsViewController(snapshot, source, childCount),
                CachedSnapshot.SourceIndex.SourceId.NativeType or
                CachedSnapshot.SourceIndex.SourceId.ManagedType => new TypeDetailsViewController(snapshot, source, childCount),

                _ => new SimpleDetailsViewController(name, TextContent.NonTypedGroupDescription, string.Empty),
            };
        }
    }
}

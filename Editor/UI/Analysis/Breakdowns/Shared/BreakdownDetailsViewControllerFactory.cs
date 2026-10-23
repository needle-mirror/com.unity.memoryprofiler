using System;
using Unity.MemoryProfiler.Editor.UIContentData;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal static class BreakdownDetailsViewControllerFactory
    {
        public static ViewController Create(CachedSnapshot snapshot, int itemId, string name, int childCount, CachedSnapshot.SourceIndex source, BreakdownTableId table,
            ITableItemLocator currentTableLocator = null, AnalysisTabBarController tabController = null)
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
                    IAnalysisViewSelectable.Category.Untracked => new SimpleDetailsViewController(name, TextContent.UntrackedDescription, string.Empty),

                    IAnalysisViewSelectable.Category.VirtualMachineMemory => new SimpleDetailsViewController(name, TextContent.VirtualMachineMemoryDescription, string.Empty),
                    IAnalysisViewSelectable.Category.UnrootedNativeSubsystemAllocations => new SimpleDetailsViewController(name, TextContent.UnrootedNativeSubsystemAllocationsDescription, string.Empty),
                    IAnalysisViewSelectable.Category.UntrackedEstimated => new SimpleDetailsViewController(name, TextContent.UntrackedEstimatedDescription, string.Empty),
                    IAnalysisViewSelectable.Category.AndroidRuntime => new SimpleDetailsViewController(name, SummaryTextContent.kAllMemoryCategoryDescriptionAndroid, string.Empty),

                    IAnalysisViewSelectable.Category.NativeReserved => new SimpleDetailsViewController(name, TextContent.NativeReservedDescription, string.Empty),
                    IAnalysisViewSelectable.Category.ManagedReserved => new SimpleDetailsViewController(name, TextContent.ManagedReservedDescription, string.Empty),
                    IAnalysisViewSelectable.Category.GraphicsReserved => new SimpleDetailsViewController(name, TextContent.NativeReservedDescription, string.Empty),

                    IAnalysisViewSelectable.Category.BrokenScriptReferences => new SimpleDetailsViewController(name, TextContent.RootsAndImpactBrokenScriptReferencesDescription, string.Empty),
                    IAnalysisViewSelectable.Category.StaticRoots => new SimpleDetailsViewController(name, TextContent.RootsAndImpactStaticRootsDescription, string.Empty),
                    IAnalysisViewSelectable.Category.DontDestroyOnLoadObjects => new SimpleDetailsViewController(name, TextContent.RootsAndImpactDontDestroyOnLoadDescription, string.Empty),
                    IAnalysisViewSelectable.Category.DontDestroyOnLoadDeletionQueue => new SimpleDetailsViewController(name, TextContent.RootsAndImpactDontDestroyOnLoadDeletionQueueDescription, string.Empty),
                    IAnalysisViewSelectable.Category.DontDestroyOnLoadScene => new SimpleDetailsViewController(name, TextContent.RootsAndImpactDontDestroyOnLoadSceneDescription, string.Empty),
                    IAnalysisViewSelectable.Category.SceneRoots => new SimpleDetailsViewController(name, TextContent.RootsAndImpactSceneRootsDescription, string.Empty),
                    IAnalysisViewSelectable.Category.AssetBundles => new SimpleDetailsViewController(name, TextContent.RootsAndImpactAssetBundlesDescription, string.Empty),
                    IAnalysisViewSelectable.Category.NativeObjectRoots => new SimpleDetailsViewController(name, TextContent.RootsAndImpactNativeObjectRootsDescription, string.Empty),
                    IAnalysisViewSelectable.Category.UnreferencedPrefabs => new SimpleDetailsViewController(name, TextContent.RootsAndImpactUnreferencedPrefabsDescription, string.Empty),
                    IAnalysisViewSelectable.Category.GCHandleRoots => new SimpleDetailsViewController(name, TextContent.RootsAndImpactGCHandleRootsDescription, string.Empty),
                    IAnalysisViewSelectable.Category.StackRoots => new SimpleDetailsViewController(name, TextContent.RootsAndImpactStackRootsDescription, string.Empty),
                    IAnalysisViewSelectable.Category.FinalizerRoots => new SimpleDetailsViewController(name, TextContent.RootsAndImpactFinalizerRootsDescription, string.Empty),
                    IAnalysisViewSelectable.Category.OtherRoots => new SimpleDetailsViewController(name, TextContent.RootsAndImpactOtherRootsDescription, string.Empty),
                    IAnalysisViewSelectable.Category.ManagedRoots => new SimpleDetailsViewController(name, TextContent.RootsAndImpactManagedRootsDescription, string.Empty),
                    IAnalysisViewSelectable.Category.NonGCHeapSections => new SimpleDetailsViewController(name, TextContent.RootsAndImpactNonGCHeapSectionsDescription, string.Empty),
                    IAnalysisViewSelectable.Category.NativeRootAllocations => new SimpleDetailsViewController(name, TextContent.RootsAndImpactNativeRootAllocationsDescription, string.Empty),

                    _ => throw new ArgumentException("Invalid or unknown item type"),
                };
            }

            return source.Id switch
            {
                CachedSnapshot.SourceIndex.SourceId.SystemMemoryRegion => new SimpleDetailsViewController(name, TextContent.SystemMemoryRegionDescription, string.Empty),
                CachedSnapshot.SourceIndex.SourceId.ManagedHeapSection => new SimpleDetailsViewController(name, TextContent.ManagedMemoryHeapDescription, string.Empty),
                CachedSnapshot.SourceIndex.SourceId.NativeMemoryRegion => new SimpleDetailsViewController(name, TextContent.NativeMemoryRegionDescription, string.Empty),

                CachedSnapshot.SourceIndex.SourceId.NativeAllocation => new ObjectDetailsViewController(snapshot, source, table, currentTableLocator, tabController, name: name, description: TextContent.NativeAllocationDescription),
                CachedSnapshot.SourceIndex.SourceId.NativeObject or
                CachedSnapshot.SourceIndex.SourceId.ManagedObject or
                CachedSnapshot.SourceIndex.SourceId.GfxResource => new ObjectDetailsViewController(snapshot, source, table, currentTableLocator, tabController, name: name),

                CachedSnapshot.SourceIndex.SourceId.NativeRootReference => new ObjectDetailsViewController(snapshot, source, table, currentTableLocator, tabController, childCount),
                CachedSnapshot.SourceIndex.SourceId.NativeType or
                CachedSnapshot.SourceIndex.SourceId.ManagedType => new TypeDetailsViewController(snapshot, source, childCount),

                CachedSnapshot.SourceIndex.SourceId.GCHandleIndex => new SimpleDetailsViewController(name, TextContent.GCHandleDescription, string.Empty),

                // Prefab entries are book-keeping units with no native/managed type of their own, so use the
                // Prefab source itself here (routes to the prefab-specific HandlePrefabDetails panel) rather
                // than the generic untyped-group ("group of items") panel.
                CachedSnapshot.SourceIndex.SourceId.Prefab => new ObjectDetailsViewController(snapshot, source, table, currentTableLocator, tabController, name: name),

                // Scene entries have their own scene-aware detail panel (name, path, memory impact); route
                // to it instead of the generic untyped-group ("group of items") panel.
                CachedSnapshot.SourceIndex.SourceId.Scene => new ObjectDetailsViewController(snapshot, source, table, currentTableLocator, tabController, name: name),

                _ => new SimpleDetailsViewController(name, TextContent.NonTypedGroupDescription, string.Empty),
            };
        }
    }
}

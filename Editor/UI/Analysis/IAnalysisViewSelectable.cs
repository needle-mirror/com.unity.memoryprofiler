namespace Unity.MemoryProfiler.Editor.UI
{
    interface IAnalysisViewSelectable
    {
        /// <summary>
        /// A list of categories with pre-defined fixed identifiers
        /// </summary>
        public enum Category : int
        {
            None,
            Native,
            NativeReserved,
            Managed,
            ManagedReserved,
            ExecutablesAndMapped,
            Graphics,
            GraphicsDisabled,
            GraphicsReserved,
            Untracked,
            UntrackedEstimated,
            AndroidRuntime,
            VirtualMachineMemory,
            UnrootedNativeSubsystemAllocations,
            BrokenScriptReferences,
            StaticRoots,
            DontDestroyOnLoadObjects,
            DontDestroyOnLoadDeletionQueue,
            DontDestroyOnLoadScene,
            SceneRoots,
            AssetBundles,
            NativeObjectRoots,
            UnreferencedPrefabs,
            GCHandleRoots,
            ManagedRoots,
            StackRoots,
            FinalizerRoots,
            OtherRoots,
            NonGCHeapSections,
            NativeRootAllocations,
            // This needs to be at the end, as it's used for indexing the beginning of categories not in this enum:
            FirstDynamicId
        }

        /// <summary>
        /// Makes an attempt to select a category from the list of pre-defined categories.
        /// Might fail if specific view can't handle this category
        /// </summary>
        /// <param name="category"></param>
        /// <returns>Returns false if the view can't select this category</returns>
        public bool TrySelectCategory(Category category);

        /// <summary>
        /// A function to validate is it dynamic or pre-defined category
        /// </summary>
        /// <param name="category"></param>
        /// <returns></returns>
        public static bool IsPredefinedCategory(int categoryId)
        {
            var id = (Category)categoryId;
            return id > Category.None && id < Category.FirstDynamicId;
        }
    }

    /// <summary>
    /// A view that can be deep-linked to with a preset <see cref="ObjectStatus"/> filter mask, as the
    /// Potential Savings widget does for Roots &amp; Impact.
    /// </summary>
    interface IRootsAndImpactStatusFilterTarget
    {
        /// <summary>
        /// Applies an exclusive <see cref="ObjectStatus"/> filter mask (bit N = status N allowed) and a flatten
        /// preference, then rebuilds. Deferred until the view has loaded if called earlier.
        /// </summary>
        void ApplyStatusMaskPreset(ulong exclusiveStatusMask, bool flatten);
    }

    /// <summary>A deep-link preset stashed until the Roots &amp; Impact view can apply it.</summary>
    readonly struct StatusMaskPreset
    {
        public readonly ulong ExclusiveStatusMask;
        public readonly bool Flatten;

        public StatusMaskPreset(ulong exclusiveStatusMask, bool flatten)
        {
            ExclusiveStatusMask = exclusiveStatusMask;
            Flatten = flatten;
        }
    }
}

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
            Unknown,
            UnknownEstimated,
            AndroidRuntime,
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
}

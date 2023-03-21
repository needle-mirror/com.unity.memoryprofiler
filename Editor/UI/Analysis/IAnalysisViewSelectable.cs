namespace Unity.MemoryProfiler.Editor.UI
{
    interface IAnalysisViewSelectable
    {
        /// <summary>
        /// A list of categories with pre-defined fixed identifiers
        /// </summary>
        public enum Category : int
        {
            None = 0,
            Native = 1,
            NativeReserved = 2,
            Managed = 3,
            ManagedReserved = 4,
            ExecutablesAndMapped = 5,
            Graphics = 6,
            GraphicsDisabled = 7,
            Unknown = 8,
            UnknownEstimated = 9,
            AndroidRuntime = 10,
            FirstDynamicId = 11
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

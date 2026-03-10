namespace Unity.MemoryProfiler.Editor.UI
{
    static class SummaryTextContent
    {
        /// <summary>
        /// All Memory summary
        /// </summary>
        public const string kAllMemoryTitle = "Allocated Memory Distribution";
        public const string kAllMemoryDescription = "Displays how your allocated memory is distributed across memory areas.";
        public const string kAllMemoryDescriptionWithResident = kAllMemoryDescription + " Hover with your cursor on the categories, to see how much of the memory allocated is currently resident on device.";

        public const string kAllMemoryCategoryNative = "Native";
        public const string kAllMemoryCategoryManaged = "Managed";
        public const string kAllMemoryCategoryGraphics = "Graphics (Estimated)";
        public const string kAllMemoryCategoryUntrackedEstimated = "Untracked*";
        public const string kAllMemoryCategoryMappedFiles = "Executables & Mapped";
        public const string kAllMemoryCategoryAndroid = "Android Runtime";

        public const string kAllMemoryCategoryDescriptionAndroid = "Android Runtime (ART) is the managed runtime used by applications and some system services on Android." +
                "ART as the runtime executes the Dalvik Executable format and Dex bytecode specification." +
                "\n\nTo profile Android Runtime use platform native tools such as Android Studio.";


        /// <summary>
        /// Managed Memory summary
        /// </summary>
        public const string kManagedMemoryTitle = "Managed Heap Utilization";
        public const string kManagedMemoryDescription = "Displays a breakdown of the memory that Unity's Scripting VM manages, which includes managed heap memory used for Managed Objects, Empty Heap Space that might've previously been used by Objects or been reserved during the last heap expansion, and memory used by a virtual machine itself.";
        public const string kManagedMemoryCategoryVM = "Virtual Machine";
        public const string kManagedMemoryCategoryObjects = "Objects";
        public const string kManagedMemoryCategoryFreeHeap = "Empty Heap Space";


        /// <summary>
        /// Resident Memory summary
        /// </summary>
        public const string kResidentMemoryTitle = "Memory Usage On Device";
        public const string kResidentMemoryDescription = "Displays how much memory you have allocated to the system, and how " +
            "much of that memory is currently Resident on device. Allocated Memory can be higher " +
            "than the maximum available on device without causing issues.";
        public const string kResidentMemoryCategoryResident = "Total Resident On Device";


        /// <summary>
        /// Top Unity Objects summary
        /// </summary>
        public const string kUnityObjectsTitle = "Top Unity Objects Categories";
        public const string kUnityObjectsDescription = "Displays which types of Unity Objects use the most memory in the snapshot.";
        public const string kUnityObjectsCategoryOther = "Others";

        /// <summary>
        /// Summary View
        /// </summary>
        public const string PreSnapshotVersion11UpgradeInfoMemoryOverview = "Make sure to take snapshots with Unity version 2021.2.0a12 or newer, to be able to see the memory overview. See the documentation for more info.";
        public const string PreSnapshotVersion11UpgradeInfo = "Make sure to upgrade to Unity version 2021.2.0a12 or newer, to be able to utilize this tool to the full extent. See the documentation for more info.";

        public const string MemoryUsageUnavailableMessage = "The Memory Usage Overview is not available with this snapshot.\n" + PreSnapshotVersion11UpgradeInfoMemoryOverview;
    }
}

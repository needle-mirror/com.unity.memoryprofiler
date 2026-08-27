# What's new in version 1.2.0-pre.1

Summary of changes in Memory Profiler version 1.2.0-pre.1.

### Added
- Added the **Roots & Impact** view to the Memory Profiler window which breaks down the memory usage of a snapshot by what roots it to memory. Roots are grouped by the root static fields type. This view also shows the memory impact of each entry, which is the memory it owns and the amount of memory it keeps alive with other roots.
- Added the ability to enable or disable optional columns from the table header context menu.
- Added a **Status** column to the **Unity Objects** and **All Of Memory** tables, which includes comparison tables and object statuses that you can search and filter.
- Added pagination to the flattened mode of the **Roots & Impact** and **Unity Objects** tables so that very large snapshots don't risk running out of memory or stalling the UI.
- Added object statuses for AssetBundle objects and assets kept loaded only through a loaded AssetBundle.

### Changed
- Removed the preference to disable the Roots & Impact and Impact & Root calculations added in 1.1.10. These calculations now always run when opening a snapshot that supports them.
- Reduced the amount of memory an open snapshot uses.
- Improved the performance of entry selection and filtering the **Base** and **Compared** tables in comparison mode.
- Enabled UI translation based on availability and your Editor preferences.

### Fixed
- Improved the accuracy of the ****Impact"** figures shown in the **Selection Details** and the **Roots & Impact** view, which fixed double-counted or misattributed memory.
- Fixed issues with the comparison mode of the **All Of Memory**, **Unity Objects**, and **Memory Map** tables and when handling contiguous system regions or Native Objects.
- Fixed an issue where multi-dimensional (rank 2 or higher) managed arrays on captures from 64-bit IL2CPP players crawled as length 0, which under-reported the array's size.
- Fixed an issue where table searching ran a search task for each character. Search now starts when you select return or deselect the search field.

For a full list of changes and updates in this version, see the [Memory Profiler Changelog](xref:changelog).

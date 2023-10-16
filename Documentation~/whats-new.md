# What's new in version 1.1.0

Summary of changes in Memory Profiler version 1.1.0 (formerly 1.1.0-pre.3).

### Fixed
- Fixed an exception on capturing when the default snapshot storage path was used (./MemoryCaptures) but did not exist. It now gets created if it is missing. Custom set storage paths will still not be created as issues with these need user input to get resolved properly.
- Fixed an ArgumentOutOfRangeException in ManagedObjectInspector on selecting some object entries in the All of Memory and Unity Objects table. The field inspector UI was trying to display the managed field values of static fields that hadn't been initialized.
- Fixed Snapshot opening triggering a synchronous search via SearchService to initialize it for the Select, Find and Asset Preview functionality. As that could trigger SearchService to start indexing, this might have lead to longer stalls on opening snapshots.
- Fixed string rendering when strings included the ´\r´ character ([PROFB-113](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-113)).
- Fixed a crash on opening snapshots with very large managed memory usage ([PROFB-156](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-156)).
- Fixed messaging for resident memory breakdown data availability. Detailed resident memory breakdown data is available for snapshots taken from Unity versions 2023.1 or newer.
- Fixed the Unity Object and All Of Memory table UI so that the table mode dropdown does not disappear in a narrow window size. ([PROFB-110](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-110))
- Disabled resident memory visualization in "Unity Objects" and "All of Memory" tables for WebGL platform. WebGL doesn't provide residency status.
- Fixed bug that detailed information isn't showed for graphics resources.
- Fixed a bug when you can't switch snapshot if snapshot was previously open in compare mode.
- Fixed a managed memory leak in the Memory Profiler Module UI that the package inserts into the Profiler Window ([PROFB-160](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-160)). Also reduced the impact of a Mesh memory leak caused by UI TK ([UUM-46520](https://issuetracker.unity3d.com/product/unity/issues/guid/UUM-46520)).
- Fixed the display of the memory usage bar diagrams in the Memory Profiler Module UI that the package inserts into the Profiler Window ([PRFOB-165](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-165)).
- Fixed issue with Unity Objects view that caused some managed objects not to group and shown as separate items.

### Changed
- Documentation updated.
- The "Search In Project" button now searches in the Assets folder _and_ in Packages (related to ([PROFB-54](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-54))).
- Improved the performance of selecting items in the All Of Memory and Unity Objects tables for objects with managed memory. This affected in particular objects which have a lot of entries (their own or nested fields) displayed in their Managed Fields in the Selection Details panel. Beyond improving the performance in general, 'Continue...' entries, which can be clicked to get further entries added to the view, now not only appear in fields 4 layers down and for big arrays, but also after a total of 1000 field entries have been added to the view.

For a full list of changes and updates in this version, see the [Memory Profiler Changelog](xref:changelog).

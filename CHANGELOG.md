# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.2.0-preview.1] - 2020-01-09
### Changed
 - Fixed an issue with the snapshot crawler going out of bounds, when scanning an array object's binary data
 - Upgraded the dependency on EditorCoroutines package to 0.1.0-preview.1.
 
## [0.1.0-preview.9] - 2019-11-22
### Changed
 - Fixed an issue where the Memory Profiler's progress bar would keep being displayed after the window was closed.
 - Fixed a UI issue where items selected inside a table would no longer be highlighted.
 - The package is now no longer compatible with the following version range of the Editor: 2020.1.0a0 - 2020.1.0a14.

## [0.1.0-preview.8] - 2019-11-08
### Changed
 - Fixed the MetaDataInjector warning when using the obsolete EditorApplication.scriptingRuntimeVersion in Unity versions newer than 2019.3.
 - Improved native connection stitching to managed objects.
 - Integrated v.10 snapshot support where native connections are dumped as Object ID instead of indices.
 - Optimized the snapshot crawling process, by reducing the number of heap look-ups.
 - Optimized snapshot heap lookup functionality, to use binary search instead of linear search.
 - Fixed an issue when importing a snapshot via the Import window would not copy the file into the Memory Captures folder.
 - Fixed a number of UI issues related to the Editor theming update.
 - The package is now no longer compatible with the following version range of the Editor: 19.3.0a1 - 19.3.0b9.
 ### Removed
 - Removed unnecessary Profiling abstraction code present in the package.
 - Removed XML loading support in preparation to deprecate XML usage in the package, and to provide users with an interface to create their own tables.


## [0.1.0-preview.7] - 2019-08-02
### Added
 - Added Screenshots getting taken on capture for Unity versions starting from 2019.3 and up.
 - Added window tab icon.
 - Added a popup warning about the potential for sharing personally identifying or otherwise sensitive data when sharing snapshots.
 - Added info to the documentation regarding potential sensitive data contained in snapshot files.
 - Added a button to the preferences to reset the opt-out decisions for above mentioned warning popup.
 - Added byte size formatting for size columns in the tables.
 - Added "Open Folder" option to the snapshot options menu.

### Changed
 - Fixed snapshot file rename functionality ([case 1131905](https://issuetracker.unity3d.com/product/unity/issues/guid/1131905/)).
 - Fixed snapshot file and meta data fields overlapping in 2019.3.x.
 - Fixed new snapshot folders getting created with every character change to the path in the Memory Profiler preferences ([case 1162851](https://issuetracker.unity3d.com/product/unity/issues/guid/1162851/)).
 - Fixed a numerical overflow when parsing snapshots with large amounts of objects and/or allocations.
 - Fixed compile issues due to the removed "EditorApplication.scriptingRuntimeVersion" API.
 - Fixed the target selection drop-down which stopped working in 2019.3
 - Moved the Memory Profiler preferences under Analysis/Memory Profiler in the Preferences window to group it with Profiler Window Settings.
 - Fixed alternating table row colors as well as row selection in 2019.3
 - Fixed link text color being hard to read in the light editor skin.

### Removed
 - Metadata collection for Scripting Runtime version for Unity >= 2019.3 since "EditorApplication.scriptingRuntimeVersion" was removed.

## [0.1.0-preview.6] - 2019-04-03
### Changed
 - Fixed dangling subscriber for OnPlaymodeChanged.
 - Fixed broken metadata injector, implemented metadata processors will now be called.
 - Fixed incorrect referencing of managed objects to native objects that did not own them.
 - Fixed style sheet warnings.
 - Changed the display string of uninitialized types from "Unknown Type" to "Uninitialized Type".
 - Fixed missing references for managed objects.
 - Fixed incorrect disposal of sidebar delegate.

## [0.1.0-preview.5] - 2019-01-29
### Added
 - Added progress bar displays for actions like opening/importing snapshots.
 - Restored binary compatibility with the 2017.4 memory snapshot format.
 - Added handling for duplicate GC handles present in the snapshot.

### Changed
 - Lowered the number of GC allocations when crawling a snapshot.
 - Upgraded dependency on EditorCoroutines package to 0.0.2-preview.1.
 - Changed the initialization of the managed memory sections to not overallocate.
 - Fixed an issue where selecting a region on the memory map would expand the table underneath onto the whole window.
 - Fixed an issue where closing either snapshot (with two snapshots loaded) would close the other one instead.
 - Resolved UI issues with upstream UI elements versions.

### Removed
 - Removed links in columns: Native Object Name, Native Size. For the Native Objects table.

## [0.1.0-preview.4] - 2019-01-02
### Added
 - Added on demand computing for tables with the purpose of speeding up the snapshot opening process.
 - Added better handling for corrupted snapshots, in order to avoid having the UI become non-responsive.

### Changed
 - Changed the managed data crawler to use a stack based approach in order to avoid stack overflows when processing large amounts of managed object references.
 - Fixed an issue where attempting to rename a snapshot with two snapshots open would cause an I/O sharing violation, due to the other snapshot being closed instead.
 - Changed capture sequence to first output to a temporary (.tmpsnap) file, to avoid having the Workbench's refresh functionality(triggered during application focus) try to access a snapshot that currently being streamed from a remote target.

## [0.1.0-preview.3] - 2018-12-17
### Added
 - Added enable callback for the capture button, to support cases where the compilation guards get triggered by building the Player.
 - Added missing deregister step for the compilation callbacks to the OnDisable method.

## [0.1.0-preview.2] - 2018-12-12
### Added
 - Added documentation for the package.
 - Added a table display underneath the "TreeMap" in order to display data about the currently selected object.
 - Added metadata injection functionality, to allow users to specify their metadata collection in a simple way.
 - Added "Diff" functionality for the "MemoryMap".
 - Added import functionality for old snapshot formats that were exported via the "Bitbucket Memory Profiler".
 - Added platform icons for snapshots whose metadata contains the platform from which they were taken.
 - Added basic file management functionality (rename, delete) for the "Workbench". It can be found under the option cogwheel of the snapshots.
 - Added the "Open Snapshots View" to the "Workbench", where users can Diff the last two open snapshots.

### Changed
 - Reworked the "MemoryMap" to display memory allocations in a more intuitive way, allowing better understanding of the captured memory layout.
 - Reworked the "Workbench" to manage the snapshot directory and display all snaphot files contained in it. The "Workbench" default directory resides in "[ProjectRoot]/MemoryCaptures".
 - General UX improvements.

### Removed
 - Removed "Diff" button from the snapshot entries inside the "Workbench".
 - Removed "Delete" button from the snapshot entries inside the "Workbench". Delete can instead be found in the menu under the options cogwheel of the snapshot.

## [0.1.0-preview.1] - 2018-11-15
### Added
 - Added memory snapshot crawler.
 - Added data caching for the crawled snapshot.
 - Added database and tables for displaying processed object data.
 - Added "Diff" functionality to tables, in order to allow the user to compare two different snapshots.
 - Migrated the "TreeMap" view from the bitbucket memory profiler.
 - Added XML syntax for defining tables, with default tables being defined inside "memview.xml".
 - Added the concept of a "Workbench" to allow the user to save a list of known snapshots.
 - Added the "MemoryMap" view to allow users to see the allocated memory space for their application.

### This is the first release of *Unity Package Memory Profiler*.
 Source code release of the Memory Profiler package, with no added documentation.

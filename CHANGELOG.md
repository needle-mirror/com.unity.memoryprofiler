# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.2.9-preview.1] - 2021-03-17
### Changed
  - Updated from Unity Distribution License to Unity Companion License
  - Fixed an issue with setting capture folder path, failing to correctly parse the provided path


## [0.2.8-preview.2] - 2021-01-18
### Changed
  - Fixed an issue with the capture button style not appearing on 2018.4 Editors.


## [0.2.8-preview.1] - 2021-01-14
### Changed
  - Fixed an issue with heap section sorting and remapping which would cause managed objects to sometimes fail to be crawled.


## [0.2.7-preview.1] - 2020-12-21
### Added
 - Added a capture flags selection dropdown onto the Capture button.
 - Added tooltips to display the complete value of table entries such as multiline strings, which are being turncated.
### Changed
  - Fixed an issue with the reference table, where value type fields would be skipped when displaying the reference target.
  - Fixed an issue where multiple match filters could be added to one column.
  - Fixed an issue where the Native Instance ID column in the All Objects Table would not be display some entries. ([case 1278247](https://issuetracker.unity3d.com/product/unity/issues/guid/1278247/)).
  - Fixed an issue with the Tree Map where history events would not be applied to the view, after clicking the previous view button ([case 1299864](https://issuetracker.unity3d.com/product/unity/issues/guid/1299864/)).
  - Fixed an issue where the table pane would attempt to change GUI state in between layout and repaint events.
  - Fixed an issue with multiline strings being displayed incorrectly inside their row ([case 1275855](https://issuetracker.unity3d.com/product/unity/issues/guid/1275855/)).
  - Changed the Address column formatting to show pointer values in hex rather than decimal, for raw data tables.
  - Renamed the Owned Size column into Managed Size for the All Objects table and Size for Managed and Native Objects tables.
  - Renamed the Target Size column into Field Target Size for All Objects and Managed Objects tables.
  - Fixed an issue with filters UI not being displayed for the Memory Map.
  - Fixed an issue with Memory Map selection being incorrect after loading a snapshot ([case 1276377](https://issuetracker.unity3d.com/product/unity/issues/guid/1276377/)).
### Removed
  - Removed the profile target concatenation from the capture button, as the currently selected target is already visible on the target dropdown.


## [0.2.6-preview.1] - 2020-10-01
### Added
  - Added search field delay when filtering table entries via the match filter.
  - Added a dialog box when attempting to rename a snapshot to same name as an existing one ([case 1273417](https://fogbugz.unity3d.com/f/cases/1273417/)).
  - Added a dialog box when attempting to rename a snapshot with invalid characters ([case 1274987](https://fogbugz.unity3d.com/f/cases/1274987/)).
  - Added name tags for snapshot preview screenshots, to avoid confusion when the user takes a snapshot with the memory profiler window open.
  - Added name tag for memory map backing memory texture.
  
### Changed
  - Removed unneeded finalizer from CachedSnasphot, which could crash the Editor.
  - Fixed an issue inside the VM data validation tools, where we would not catch invalid VM info data.
  - Fixed incorrect parsing of table headers as callstack site ids when attempting to retrieve callstacks for a native allocation.
  - Fixed texture color space to linear inside the MemoryMap view creation ([case 1261948](https://fogbugz.unity3d.com/f/cases/1261948/)).
  - Fixed potential alpha overflows inside the MemoryMap, where some regions on the memory map might have become transparent.
  - Secured the MemorySnapshot backend so that we handle array sizes larger than int.Max gracefully.
  - Fixed an issue with reading entries null entries from reference arrays, where this would cause a NullReferenceException.
  - Changed the snapshot crawler to discard unknown type data as some types are VM internal.
    Note: The following versions are able to capture all type data: 2020.2b2, 2020.1.5f1, 2019.4.10f1.
  - Changed table diffing to be multi-key, thus making the diffing operation accurate with regards to matching object as "Same".
  - Changed the Address column formatting to show pointer values in hex rather than decimal, for object tables.
  - Changed the Name column so that it only shows object or field name depending on the expanded item.
  - Fixed some static fields not showing up in the Reference table.
  - Fixed a leak where the snapshot collection would never clean up it's Texture2D.
  - Fixed a leak where the memory map's backing texture would never be cleaned up during flush.
  - Fixed an issue where snapshots could be imported twice ([case 1271886](https://fogbugz.unity3d.com/f/cases/1271886/)).
  - Fixed an issue showing "None" in the diff table's match filter, when matching by "Diff" column value.
  - Fixed an issue where the snapshot deletion message would not to explicitly state that the file will be permanently removed.
  - Fixed an issue where the naming convention used for snapshots inside the memory map diff view was incorrect.
  - Fixed an issue where the snapshots used inside memory map would not swap when the swap button was pressed.
  - Fixed an issue where snapshot file list was sorted alphabetically ([case 1276092](https://fogbugz.unity3d.com/f/cases/1276092/)).
  - Fixed an issue where open snapshots could end up losing their open state while still open in the tool ([case 1275288](https://fogbugz.unity3d.com/f/cases/1275288/)).
  - Fixed an issue where the snapshot file list wouldn't update when the editor would change focus ([case 1275835](https://fogbugz.unity3d.com/f/cases/1275835/)).


## [0.2.5-preview.1] - 2020-07-01
### Changed
 - Fixed up some issues with connections not being added properly during crawling.
 - Fixed up an issue when diffing two snapshots where invalid object would cause the system to throw ([case 1236254](https://issuetracker.unity3d.com/product/unity/issues/guid/1236254/)).
 - Optimized the crawling of native connections.
 - Reduced crawling operation memory footprint by ~30%.
 - Fixed Memory Map address label text spilling over ([case 1256489](https://issuetracker.unity3d.com/product/unity/issues/guid/1256489/)).
 - Fixed Memory Map interfering with Profiler Line Charts ([case 1260533](https://issuetracker.unity3d.com/product/unity/issues/guid/1260533/)).


## [0.2.4-preview.1] - 2020-06-10
### Changed
 - Fixed an issue with the Memory Profiler Window failing to load it's resources when opening during playmode.
 - Upgraded the dependency on EditorCoroutines package to 1.0.0.
 - Fixed a compilation issue with Unity 2019.4 ([case 1254424](https://issuetracker.unity3d.com/product/unity/issues/guid/1254424/)).
 - Fixed up two unused variable warnings.


## [0.2.3-preview.2] - 2020-03-18
### Changed
 - Upgraded the dependency on EditorCoroutines package to 0.1.0-preview.2.
 - Fixed an issue with the layouting of the Memory Map's legend. 


## [0.2.3-preview.1] - 2020-03-04
### Changed
 - Fixed an issue where selecting an object of type System.String would raise exceptions when attempting to retrieve field memory metrics([case 1224644](https://issuetracker.unity3d.com/product/unity/issues/guid/1224644/)).
 - Fixed an issue where matching string objects based on their value, was not retrieving the correct object data. 


## [0.2.2-preview.1] - 2020-02-26
### Changed
 - Fixed incorrect layouting behavior when scrolling down or up inside a table.
 - Improved snapshot crawler performance, by allocating crawled data in blocks.
 - Fixed an issue with the "Owned size" column for managed objects, which caused the displayed value to be incorrect.
 - Fixed an UI issue where tables would throw exceptions when scrolling too fast. 
 - Improved Treemap UI performance.

## [0.2.1-preview.2] - 2020-02-18
### Changed
 - Improved snapshot crawler performance by reducing the number of Exception objects being creating during snapshot crawling.
 - Fixed an issue where the crawler would skip some managed object fields depending on offset.


## [0.2.1-preview.1] - 2020-02-10
### Added
 - Added MetadataCollect abstract class in order to provide a better, performant way to inject new collectors into the metadata collection system.

### Changed
 - Fixed an issue with the native object connections cache, where invalid native object references would not be skipped.
 
### Removed
 - Removed IMetadataCollect interface and the method of injection used for it, as it would degrade runtime performance each time the injection would occur.


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

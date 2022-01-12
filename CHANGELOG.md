# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.4.4-preview.2] - 2022-01-12

### Added
 - Added the Unity version a capture was made in to the tool-tip of the Session label of opened snapshots.

### Fixed
 - Fixed tables drawing blank areas when scrolled past row 60000 and scrolling to the right.
 - Fixed the Fragmentation view's Memory Map drawing of Managed Memory regions while comparing snapshots when they changed size between captures. They would previously show the entire region as changed and not show which objects within them were new or deleted. [(Case 1388611)](https://issuetracker.unity3d.com/product/unity/issues/guid/1388611/)
 - Fixed an IndexOutOfBoundsException when sorting size tables in the Memory Map's Object list and switching to the Region or Allocations lists.
 - Fixed the row size option in the Fragmentation view not being properly stored across sessions for Fragmentation analysis of snapshots in comparison mode. 
 - Fixed an issue where the crawler would find impossibly big string objects, inflating Managed Object Memory and causing exceptions when these were to be drawn in a table [(Case 1393878)](https://issuetracker.unity3d.com/product/unity/issues/guid/1393878/).

### Changed
 - Renamed the `References` column to `Referenced By` to improve clarity slightly.

## [0.4.3-preview.1] - 2021-12-13

### Added
 - Added a toggle to the Memory Usage Summary when comparing snapshots to switch between normalizing the bars to their respective total, or scaling them to the bigger of the two.

### Fixed
 - Fixed an exception that occurred when creating a new UI Document via `Create > UI Toolkit > UI Document`.
 - Fixed an Index Out Of Bounds exception in ManagedMemorySectionEntriesCache.Find on opening some snapshots, particularly IL2CPP snapshots.
 - Fixed an issue which affected the crawling of some snapshots that prevented Managed Objects of types inheriting from UnityEngine.Object from being properly connected with their Native Objects and therefore not showing their Native Sizes, Instance IDs and Native Object Names in the tables [(Case 1383114)](https://issuetracker.unity3d.com/product/unity/issues/guid/1383114/).
 - Fixed "Failed To read Object, please report a bug." Objects appearing in snapshots with only one or two managed heap sections.
 - Fixed "abnormal mesh bounds" error to not appear on opening snapshots.
 - Fixed total available memory so it no longer adds GPU memory on unified memory platforms.
 - Fixed `Memory Usage` breakdown categories adding up to more than the `Total` value [(Case 1381034)](https://fogbugz.unity3d.com/f/cases/1381034/).

### Changed
 - Renamed the Memory Usage Breakdown category `Other` to `Other Native Memory` to improve clarity slightly. It shows all native memory usage tracked by Unity's Memory Manager that does not show up in the other, more specific categories.
 - Renamed the Memory Usage Breakdown category `Virtual Machine` to `Virtual Machine (IL2CPP)`, `Virtual Machine (Mono)` or `Virtual Machine (Scripting)` depending on which scripting back-end is used, or if two different ones are used when comparing snapshots.

## [0.4.2-preview.1] - 2021-10-22

### Fixed
 - Fixed an exception thrown by the Tree Map when opening a snapshot that contains a group of types that collectively "use" 0 B, preventing the opening of the snapshot [(Case 1368289)](https://issuetracker.unity3d.com/product/unity/issues/guid/1368289/). 
 - Fixed compilation on earlier 2019.4 patch versions which failed due to usage of some newer RuntimePlatform enum values.
 - Fixed the profiler target drop-down to no longer cause a TargetParameterCountException and draw as blank button on 2022.1.0a13 or newer.
 - Fixed the snapshot rename work-flow when renaming an open snapshot so that the name input field gets the keyboard focus after closing the dialog prompt.

## [0.4.1-preview.1] - 2021-09-21

### Added
 - Added two capture options when capturing the Editor, allowing you to choose whether or not the Memory Profiler should close all open snapshots and trigger a Garbage Collection before taking the capture. The default behavior is to do both of these to reduce the noise when capturing the memory usage in the Editor.

### Changed
 - Changed capture logic when capturing snapshots from a Player to not close open snapshots and trigger a Garbage Collection in the capturing Editor.
 - Fixed platform icons so they show up for more platforms.
 - Fixed the Memory Profiler Module UI so it shows non-broken, single data set Memory Breakdown bars and a functional object data list again.
 - Fixed a calculation for the `Memory Usage` breakdown where `Virtual Machine` memory was faultily subtracted from `Other`. 
 - Fixed Managed Memory Breakdown bars not swapping A and B bars when swapping the Snapshot.
 - Fixed the value of Untracked so it stays as `Unknown` on sorting, instead of changing to `0 B`.
 - Fixed Allocation tables to not show allocations without a proper root object id as associated with `ExecutableAndDlls` but instead as having an `Unkown` root object.

## [0.4.0-preview.2] - 2021-09-01

### Changed
 - Fixed error messages appearing about `90deg` and `270deg` being an `UnsupportedUnit` for Unity versions before 2021.2.
 - The 'A' icon in the break down table header is now hidden when in single snapshot mode.

## [0.4.0-preview.1] - 2021-08-31

Recommended Unity versions to upgrade to when using this package version:
 - 2019.x
   - 2019.4.29f1 or newer
 - 2020.x
   - 2020.3.12f1 or newer
 - 2021.1
   - 2021.1.9f1 or newer
 - 2021.2
   - 2021.2.0a12 or newer
or any newer version of Unity.

### Added
 - Added a collapsible "Memory Usage Overview" section containing a high level breakdown of the Memory usage.
   - It shows the same breakdowns as the Profiler Window's simple Memory Profiler panel, adding two more categories which are untracked in the Profiler Window: Virtual Machine and Executable and DLLs
   - It additionally shows a breakdown of Managed Memory usage into Virtual Machine (currently only available when using the Mono scripting backend), Objects and the two kinds of free reserved space, either in the Active Heap section, or as Fragmented Heap in older sections. 
     Note that Fragmented Heap Space can be reused for Managed Object memory, if new objects fit into contiguous free space in it. You can use the Fragmentation page to analyze your memory usage if you seem to have a lot of fragmented memory. 
     This breakdown should help you determine if it might be worth to dig deeper into Fragmentation or your Managed Object usage, or if there are other areas to focus on. 
 - Added a collapsible "Top Issues" section containing some identified issues with the opened snapshot. On opening a snapshot, checks will be run and entries added to the list.
   Note: There are a lot of memory usage scenarios that might be problematic but are not clear cut enough for an algorithm without any context of the project to make any determination on. 
   This list can therefore not be exhaustive but aims to offer a helpful start into memory investigations.
   More checks will be added going forward. The current set of checks will add:
   - A warning if the snapshot was taken from a Unity Editor, as memory usage in the Editor might be misleading compared to memory usage in a build.
   - A warning if the System Allocator is used.
   - An info entry if the snapshot was taken with some options for capturing disabled.
   - A warning when comparing snapshots that were taken with different capturing options being enabled.
   - An info entry when comparing snapshots taken from different sessions or an unknown session, as this will affect the detail level at which differences can be checked.
   - An info entry when comparing snapshots taken from different Unity versions, as observed changes in memory could be due to the version change instead of a change in the project. The version numbers are shown if they are known.
 - Added a view selection Ribbon
   - Renamed the Memory Map view to Fragmentation page.
   - Moved all Table views into the new Objects and Allocations page.
   - Renamed the Tree Map view to Summary page.
     - This is an intermediate step in a larger UI refactor. In a next step, the Summary page will get new top level summary sections and the Tree Map view will be moved to the Objects and Allocations page.
 - Added an option to the drop-down of the Capture button to not take a screenshot on capture.
 - Added Virtual Machine memory as a category to Memory Map. This relies on changes made to the snapshot data and only works for any snapshots taken from Unity versions 2021.2.0a12, 2021.1.9, 2020.3.12f1, 2019.4.29f1 or newer. Only builds made against the Mono Scripting Back-end report Virtual Machine memory usage. IL2CPP does, as of yet, not report any of its Virtual Machine memory usage. This also addresses [(Case 987987)](https://issuetracker.unity3d.com/product/unity/issues/guid/987987/)
 - Added a Button to open the package's documentation to the top right corner.
 - Added a Menu button to the top right corner, containing an option to show the Memory Profiler's Preferences window.
 - Added a "Snapshot Window" Button to the top left corner to toggle the visibility of the open snapshots pane and the snapshots list.
 - Added the ability to set match row filtering for string based rows to check for exact (case-insensitive) matches additionally to the existing option to filter if it contains the searched string.
 - Added a replacement for the details view of the [Profiler Window](https://docs.unity3d.com/Manual/ProfilerWindow.html)'s [Memory Profiler module](https://docs.unity3d.com/Manual/ProfilerMemory.html). If the Package is installed, it will provide the UI for the Memory Profiler module for Unity versions of 2021.2 or newer.
 - Added a setting to `Preferences / Analysis / Memory Profiler / Replace Memory UI in Profiler Window` to toggle the Memory Profiler Module replacement on or off.
 - Added "All Native Allocations" table which lists the Memory Region, Area and Root Object names for each Allocation. 

### Changed
 - Changed the snapshot list UI by: 
   - Moving the open snapshots section to the top
   - Adding a bar to break-down the total allocated memory vs. the systems maximally available memory to each snapshot (only works for snapshots taken form 2021.2.0a12, 2021.1.9, 2020.3.12f1, 2019.4.29f1 or newer)
   - Grouping snapshots taken from the same session together and listing the project name along side them (only works for snapshots taken form 2021.2.0a12, 2021.1.9, 2020.3.12f1, 2019.4.29f1 or newer, older snapshots are grouped into the "Unknown Session")
   - Moved snapshots options (Rename, Delete, Open Folder) into right-click context menu.
   - Snapshots are now refereed to as "A" or "B" when comparing them.
 - Fixed table UI so that it no longer starts glitching when looking at tables of over 60000 rows.
 - Fixed an issue where the Memory Map would draw Native Objects over other Native Objects and over the end of their Native Region. The UI falsely assumed that an Object's memory is contiguous, starting from the address to the Object's header. In reality it may consist of several buffers and other allocations distributed across different memory regions. Right now, the memory reporting of Native UnityEngine.Objects is not detailed enough (broken down to each allocation) to clarify just where all of an Object's memory is residing. Current Native Objects being drawn in the Memory Map therefore is not 100% guaranteed to be showing the correct amount of contiguous memory owned by this object, but at least it no longer draws over other Objects or outside of the Region or Allocation it resides in. [(Case 1278205)](https://issuetracker.unity3d.com/product/unity/issues/guid/1278205/)
 - Fixed Memory map colors for Objects and Allocations drawn outside of regions. [(Case 1278203)](https://issuetracker.unity3d.com/product/unity/issues/guid/1278203/)
 - Fixed Memory Map to not draw anything or a white background outside of actual memory address usage. The pixels for these regions where no longer correctly cleared to full transparency.
 - Fixed Memory Map to not draw Virtual Machine memory as Managed Stack Memory in improbably high virtual address space. (Reporting of Managed Stack memory is not yet supported. Virtual Machine memory was miss-read as stack memory by the crawler.)
 - Fixed Memory Map Diff to draw lines correctly again as green, red, yellow or purple instead of drawing all lines as red.
 - Changed "Native Memory Regions" table to "All Memory Regions" table, naming Managed Memory Regions as either "Managed Heap Memory Section" or "Virtual Machine Memory Section" and showing this table in Memory Map details section when opting to look at Regions.
 - Changed Memory map to show the new "All Native Allocations" table when displaying allocations to provide more accessible information on the details of the allocations.
 - Changed "Import" button to use an icon instead of text and moved it to the right side of the toolbar.
 - Changed the link text for the Native Instance ID column to ping the object it belongs to, if the capture was taken from the current Editor session.
 - Clarified UI of the snapshot list to show more clearly which snapshots are open [(Case 1129613)](https://issuetracker.unity3d.com/product/unity/issues/guid/1129613/)
 - Changed loading of Snapshot preview screenshots to happen asynchronously to speed up opening the window and rebuilding it after Domain Reload.
 - Improved Memory Map selection workflow, hinting that the table below it needs a selection to show anything and clarifying why there might be no data.
 - Fixed Memory Map deselection not affecting the view immediately.
 - Improved handling when taking a snapshot and waiting for the screenshot to be transmitted takes unusually long.
 - Fixed removing of sort filters via the 'x' button to actually remove the filter from the column.
 - Improved table names in the table view drop-down to format the amount of rows with thousand-separators.
 - Fixed filters to only use "Pretty Names" for the columns they apply to.
 - Changed Tree Map table to explicitly show the filters applied to the table when selecting a type block in the map. This also, for the first time, allows clearing the filtering after the first such selection was made.
 - Improved the Memory Module simple view in the Profiler Window (this package replaces the default one) by adding a scrollbar.
 - Fixed Tree Map to include 0B sized Native Objects in the count for objects of their Type.
 - Fixed match filter input field so it always gets the keyboard focus on adding the filter [(Case 1359045)](https://issuetracker.unity3d.com/product/unity/issues/guid/1359045/)

### Removed
 - Removed support for Unity 2018.4. Old snapshots can still be imported but the package of the UI no longer works with 2018.4.
 - Removed the option to toggle off "Pretty Names" in the table views. It is now an unchangeable default.
 - Removed `Managed Global` from references list of managed objects.


## [0.2.10-preview.1] - 2021-07-30
### Changed
  - Fixed an issue where snapshots taken on UWP using the .NET scripting backend, could not be loaded by the package.

### Removed
  - Removed ReflectionUtility as it was no longer used by the package.


## [0.2.9-preview.3] - 2021-05-26
### Changed
  - Fixed an issue with the creation of several string buffers inside the snapshot file reader, where buffer memory would not be zero initialized.
  - Fixed an issue where compilation against Unity Editor 2021.2 was broken.
  - Fixed target connection dropdown being broken on 2021.2.


## [0.2.9-preview.2] - 2021-05-26
### Changed
  - Fixed an unused variable warning for kCurrentVersion ([case 1329193](https://fogbugz.unity3d.com/f/cases/1329193/))


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
  - Added a dialog box when attempting to rename a snapshot to same name as an existing one ([case 1273417](https://issuetracker.unity3d.com/product/unity/issues/guid/1273417/)).
  - Added a dialog box when attempting to rename a snapshot with invalid characters ([case 1274987](https://issuetracker.unity3d.com/product/unity/issues/guid/1274987/)).
  - Added name tags for snapshot preview screenshots, to avoid confusion when the user takes a snapshot with the memory profiler window open.
  - Added name tag for memory map backing memory texture.

### Changed
  - Removed unneeded finalizer from CachedSnasphot, which could crash the Editor.
  - Fixed an issue inside the VM data validation tools, where we would not catch invalid VM info data.
  - Fixed incorrect parsing of table headers as callstack site ids when attempting to retrieve callstacks for a native allocation.
  - Fixed texture color space to linear inside the MemoryMap view creation ([case 1261948](https://issuetracker.unity3d.com/product/unity/issues/guid/1261948/)).
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
  - Fixed an issue where snapshots could be imported twice ([case 1271886](https://issuetracker.unity3d.com/product/unity/issues/guid/1271886/)).
  - Fixed an issue showing "None" in the diff table's match filter, when matching by "Diff" column value.
  - Fixed an issue where the snapshot deletion message would not to explicitly state that the file will be permanently removed.
  - Fixed an issue where the naming convention used for snapshots inside the memory map diff view was incorrect.
  - Fixed an issue where the snapshots used inside memory map would not swap when the swap button was pressed.
  - Fixed an issue where snapshot file list was sorted alphabetically ([case 1276092](https://issuetracker.unity3d.com/product/unity/issues/guid/1276092/)).
  - Fixed an issue where open snapshots could end up losing their open state while still open in the tool ([case 1275288](https://issuetracker.unity3d.com/product/unity/issues/guid/1275288/)).
  - Fixed an issue where the snapshot file list wouldn't update when the editor would change focus ([case 1275835](https://issuetracker.unity3d.com/product/unity/issues/guid/1275835/)).


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

---
uid: changelog
---
# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [1.1.0] - 2023-10-16
- Bump version to 1.1.0 from 1.1.0-pre.3.

## [1.1.0-pre.3] - 2023-09-27

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

## [1.1.0-pre.1] - 2023-05-04

### Added
- Added thumbnailing for screenshots to improve startup speed.

### Changed
- Deprecated accidentally exposed as public IComparableItemData interface.
- Updated window icon.

### Fixed
- Fixed an empty MonoBehaviour entry being displayed in the Unity Objects table when 'Show Potential Duplicates' was checked and there are no potential duplicate MonoBehaviours.
- Fixed Summary view tracking of Graphics memory on Switch.
- Fixed the Summary breakdown bar's total label overlapping with its legend when using small window sizes.
- Fixed not showing negative values when comparing snapshots in Summary view ([PROFB-72](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-72)).

## [1.1.0-exp.2] - 2023-04-20

### Added
- Added metadata support for AudioClip and Shader objects.

### Fixed
- Fixed an ArgumentNullException for a parameter named `e` in `MemoryBreakdownLegendViewController.GatherColumnReferences` ([PROFB-97](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-97)).
- Fixed Selection Details foldouts so that they retain their expansion status for the duration of an Editor session and not change on every selection change.
- Fixed screenshots remaining after deleting snapshots.
- Fixed situations where the highlighted element in tables did not reflect the displayed selection but could not be clicked to show its details in the selection, particularly when the selected row item would change due to search filtering the table, switching back to a previously visited view.
- Fixed leaks of native UnsafeUtility.Malloc(Persistent) allocations that occurred when the Editor recompiled code while a snapshot was open.
- Fixed leaks of Persistent NativeArray allocations via the Snapshot FileReader occurring with every (attempted) opening of a snapshot.
- Fixed Unity Objects view not showing graphics resources for snapshots made with Unity 2022.1 and older
- Added a tooltip for graphics items in All of Memory Table in "Resident" and "Allocated and Resident" views for a better explanation of why some elements are grayed out
- Removed not-actionable console warning for iOS captures about reported overlapping Native Objects allocations
- Fixed error if you try to open a snapshot while capturing
- Fixed error reported if you try to change table view mode with dropdown while the view is still loading
- Fixed snapshot renaming issues in situations when warning or error dialog is shown

## [1.1.0-exp.1] - 2023-03-21

### Added
- Added a dropdown to the Unity Objects and All Of Memory views that allows switching between showing all sizes as they relate to Resident Memory, Allocated Memory or both.
- Added support for RenderTexture metadata in memory captures.
- Added tooltips to sizes in Selection Details view, showing the memory usage in bytes.

### Changed
- Reduced managed memory usage when opening snapshots by loading the managed heap bytes into native instead of managed memory containers.
- Changed the naming of managed object entries in tables when they are not connected to a native Unity Object from "No Name" to their address. Strings and char arrays additionally show their first characters after their address value. Entries for Unity Objects that had their native object destroyed further get the postfix "(Leaked Shell)" after their address.
- Changed Unity Object comparison for same session comparisons to further distinguish Unity Objects by their Instance IDs, unless the table is flattened.
- Improved Preview, Search and Select In Editor functionality, especially for Scene Objects.
- Objects of types inheriting from MonoBehaviour or ScriptableObject are now grouped under their managed type name in the Unity Objects table.

### Fixed
- General layout improvements in details view.
- Fixed missing allocators information in Native->Reserved breakdown in compare mode of the All of Memory view.
- Fixed Managed Fields table to show details for struct data.
- Searching the tables now correctly finds items by their type name or overarching group name.
- Fixed Exceptions when opening snapshots with a managed heap bigger than 2 GB and contiguous managed heap sections bigger than 2 GB ([PROFB-41](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-41)).
- Fixed IndexOutOfBoundsException when selecting Managed Type groups in the All Of Memory table if all instances are Leaked Shell objects.
- Fixed a bug where the Select In Editor button was available, even though the selected object did not clearly refer to one particular item. Clicking the button could therefore select the wrong item. ([PROFB-54](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-54))
- Fixed captures failing to save when the product name contained illegal characters ([PROFB-63](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-63)).
- Fixed NullReferenceException when selecting a PrefabImporter in the the Unity Objects and All Of Memory tables ([PROFB-58](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-58))
- Fixed Search in Editor button being enabled when nothing can be searched for ([PROFB-59](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-59)).
- Fixed the issue that finding managed objects by their type was impossible ([PROFB-64](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-64)).
- Fixed an IndexOutOfRangeException when inspecting the details of a managed object with a pointer to a native allocation that has no root name reported ([PROFB-66](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-66)).

## [1.0.0] - 2022-08-26

### Fixed
- Fixed typos in the UI.
- Fixed 'Destroying assets is not permitted to avoid data loss' error when previewing TerrainData objects.
- Minor styling adjustments to Light theme.
- Fixed script upgrader updating the memory profiler code.

## [1.0.0-pre.3] - 2022-06-30

### Added
- All Of Memory Comparison functionality.
- Added the ability to sort by multiple columns on Unity Objects, All Of Memory, Unity Objects Comparison, and All Of Memory Comparison table column headers. CTRL-click a table column header to add it as an additional (secondary, tertiary etc.) sorting column.
- List of executables and mapped files if snapshot is made with Unity 2022.2 or newer.

### Changed
- Close a snapshot by clicking the new 'Close' button, instead of selecting an open snapshot.
- Remove A/B snapshot colors.
- Remove Swap Snapshots button.

### Fixed
- Renaming a snapshot no longer allows duplicate file names with different casing, which naturally caused file system errors.
- Fixed detail panel reference view not showing all the references for selected object.

## [1.0.0-pre.2] - 2022-05-05

### Changed
- Changed details view UI to hide references section if selected item doesn't have references.

### Fixed
- Fixed a crash in DynamicArray.Resize [(Case 1426543)](https://issuetracker.unity3d.com/product/unity/issues/guid/1426543/).
- Selecting a Unity Object Type in the Unity Objects Comparison view now clears the selection in the Details view.
- Fixed `Value cannot be Null` error and infinite Circular References when expanding types with self references in the Managed Fields table in the Selected Item Details panel.

## [1.0.0-pre.1] - 2022-05-04

### Added
- Added Unity Objects comparison view when comparing two snapshots.

### Removed
- Removed accidentally exposed as public internal API
- Removed Fragmentation, Objects & Allocations, and Tree Map legacy tabs.

### Changed
- Changed minimal supported version to 2022.2.
- Use Editor built-in platform icons in the Snapshot panel.
- Moved Unity Objects and All Of Memory breakdowns to their own tabs.
- Moved Potential Duplicates to an option within Unity Objects.

### Fixed
- Fixed an issue whereby tab bar text would overlap with details text on sufficiently small window sizes.
- Fixed an exception occurring when loading an unsupported snapshot and opening the Unity Objects breakdown.
- Fixed an issue whereby a 'feature not available due to pre 2022.1' message can be shown incorrectly.

## [0.7.1-preview.1] - 2022-05-10

### Fixed
- Fixed a crash in DynamicArray.Resize [(Case 1426543)](https://issuetracker.unity3d.com/product/unity/issues/guid/1426543/).

## [0.7.0-preview.2] - 2022-05-04

### Fixed
- Fixed the layout of entries in the Selected Item Details panel.

## [0.7.0-preview.1] - 2022-05-04

### Added
- Progress bar when opening the Tree Map for the first time.

### Fixed
- Fixed Displaying of strings longer than 8000 characters breaking the Selected Item Details UI by capping the maximum length of strings to 8000 before displaying them.
- Fixed Compilation on Unity 2021.1.
- Fixed a possible TempJob allocation leak when failing to reading a corrupt snapshot.
- Fixed a NullReferenceException that was thrown upon trying to go back to having Untracked Memory selected while in Fragmentation tab using Memory Profiler (Case 1401201).
- Fixed type name headers in Details panel running off-screen for long type names ([Case 1401535](https://issuetracker.unity3d.com/product/unity/issues/guid/1401535/)).
- Fixed NullPointer exception when clicking on the selected item label in the References panel if no item is selected.

### Changed
- Sped up snapshot opening times.
- Moved All Object Tree Map and Table to its own Page.
  - The Memory Usage Summary on the Summary Page will now be always unfolded on switching to this page.
  - For all other pages containing the Memory Usage Summary, it defaults to being folded up and will remember its state on each page for the current Session of the Editor.
- Optimized Tree Map generation. This reduces time to open Tree Map by 25%.
- Remove reflection usage for opening the Preferences window focused on the Memory Profiler's settings.
- Renamed "Top Issues" to "Potential Capture Issues".
- Adjusted the colors of the bar diagrams in the Memory Usage Overview foldout to improve their readability and clarity.
- Adjusted Engine and Editor API usage to reflect Memory Profiler APIs moving out of Experimental namespaces in 2022.2.0a13.

### Removed
- Removed View History buttons from the toolbar. With the removal of links in tables in version 0.5.0-preview.1, these buttons had become largely redundant.
- Removed sorting of the tables for the Memory Summary bars until pending rework on them is conducted.

## [0.6.0-preview.1] - 2022-03-11

### Added
- Added the 'Unity Objects' view to show a breakdown of memory contributing to all Unity Objects. This enables you to more easily understand the total memory impact of the assets and objects in your project by showing you their total, native, and managed sizes in one place. Unity Objects are grouped by their types, such as Texture2D, Material, and Mesh. Use the 'Search By Name' functionality to filter the view to specific objects. Use the 'Flatten Hierarchy' functionality to remove the Type groupings.
- Added the 'Potential Duplicates' view to show a breakdown of all potential duplicate Unity Objects. Potential duplicates, which are Unity Objects of the same type, name, and size, might represent the same asset loaded multiple times in memory.
- Added the 'All Tracked Memory' view to show a breakdown of all tracked memory that Unity knows about.
- Added References To functionality. A list of the objects that the current selection has "references to" will now be displayed in the References To tabbed section of the details panel.

### Fixed
- Removed two instances of Reflection for loading icons.

## [0.5.1-preview.1] - 2022-02-25

### Added
 - Added tooltip to the Referenced By section to explain what it means. "Displays a list of other entities that reference the selected object."

### Fixed
 - Fixed Managed Type truncation for generic types to only truncate the type that holds the generic type arguments.
 - Removed "Examples/SearchService/Providers" Menu item.
 - Fixed issue where filters would be added to the spreadsheet tables and could not be removed.
 - Fixed issue where selecting something in the references tree view would cause the spreadsheet to repaint and lose latent selection.
 - Fixed Duplicate entries appearing in searches in the reference view.
 - Fixed NotImplementedException being thrown when traversing history from the fragmentation panel to a high-level element breakdown selection.
 - Fixed the main selection not being recreated in the references view when navigating back in history from other views to the details panel.
 - Fixed issue where removing filters would leave the wrong selection in the table view.
 - Fixed references view selection causing table expansion state to reset.
 - Fixed Errors and Exceptions breaking the Memory Profiler Window when minimizing, maximizing or docking and undocking it.
 - Improved "Select in Editor" search accuracy for Assets and Scene Objects in 2021.1 or newer editors.
 - Improved "Open in Quick Search" search string when searching for Scene Objects in 2021.1 or newer editors.

### Changed
 - Changed Snapshot preview screen-shot textures to get compressed in memory to reduce memory overhead.
 - Changed Type name truncation so that all type names are truncated not just the initial type name.
 - Changed the Referenced By tree view to not contain the selected object.
   - The selected object that the references are calculated for are now displayed above the tree view.
   - The Referenced By button now show the number of direct connections in its label.
 - The import buttons has been moved to left side of the memory profiler window toolbar.
 - The snapshots panel toggle text has been removed.
 - The details panel toggle text has been removed and the icon has been changed to use the inspector icon.

## [0.5.0-preview.1] - 2022-02-02

### Added
 - Added selection events to the view history.
 - Added details panel. This is context aware and will show extra information about whatever the current selection is.
   - Referenced By (Raw) section: This section of the details panel shows the connections to the current selection, where applicable. This can be used to determine the paths to objects that are holding objects in memory and is currently designed as a 1:1 match to the references column click through from the object tables.
   - Selection Details section: This section of the details panel displays, where possible, additional information about the currently selected item e.g. the fields on a managed object or the description of the currently selected memory usage overview category.
     - Showing these details is currently only implemented for Managed or Native Objects, Managed or Native Types and the breakdown categories in the Memory Usage Overview.
     - If an Object is selected that inherits from UnityEngine.Object (a "Unity Object") that has a Managed Shell to its Native Object, the Selection Details treat it as one object, giving you all the info for both of them and list out details separately as well were useful.
     - Where applicable, the selected item is searched for in the editor. If there is exactly one item found in the editor which match the search criteria, which is either the Instance ID (if the capture is from the current Editor Session) or its Name and Type, two things become available
       - The "Select in Editor" button to ping the object in the Scene Hierarchy or Project Browser and to select the object so it's details are shown in any open Inspector view.
       - If applicable, a preview is loaded from the object in the Editor. (NOTE: This is NOT how the object necessarily looked in the build. The search logic may have found a different object or it may have been changed since the build was made. This is just to potentially help in identifying the Object in the capture faster.)
     - Regardless of the results of the search for a specific object, the new "Search in Scene/Project" button can always help in finding Assets or Scene Objects quickly.
     - In Unity 2021.1 and newer, an additional button will open Quick Search.
- Added the ability to hide table columns and reduced the set of columns shown by default down to the most commonly useful ones. The reasoning for hiding these is:
   -  With the managed fields moved to the Selection Details section, the "Static" and "Field Target Size" columns have become redundant, and so has the ability to expand Managed Objects to see their fields, and the "Address" column to see the field layout clearer.
   -  With the Selection Details section showing a Unity Object's managed and Native info as one, the Managed Objects no longer show their Native Object's size and instead of splitting their size into a Managed Size and a Native Size column, the tables now only show one Size column and one Name Column by default.
   -  The Native Instance ID is also shown in the Selection Details under the Advanced tab. The most important info from it is considered for the "Status" displayed in the "Basic" info section (i.e. negative = Runtime Created, Positive = Loaded from file).
   -  If you took a snapshot of the current editor session, the Instance ID is used by the "Select in Editor" button in the Selection Details section to select the instance. It therefore no longer needs to be displayed as a link in the table.
   -  All other links in the tables have also been disabled, as they would only help see further details for an object, that now live in the Selection Details section
 - Added a "Count" and a "Total Size" label above each table that dynamically adjusts as you filter the table.
 - The Managed Fields inspector in the Selection Details section has some additions over what was previously shown in the tables:
   - It links up NativeArray fields that point to native Allocations with these allocations for easier analysis of DOTS memory usage.
   - For IntPtr fields, it tries to find the Object, Allocation or Region that the IntPtr points at, and shows the information it finds.
   - For UnityEngine.Object.m_CachedPointer it shows the Native Object that the Managed Shell Object points to.
   - Recursive reference chains are caught and truncated so that it is safe to use Alt/Option + LMB click to expand the tree all the way. Though right now, to keep processing times low, it only searches 3 levels deep at a time.
 - Added toggle to truncate type names in the details panel. It can be accessed through the kebab menu icon and the context menu of both the managed object inspector and referenced by tree views.

### Fixed
 - Fixed Fragmentation page to show an Allocations table with root object and area names when comparing snapshots.

### Changed
 - Memory Usage Overview categories can now be selected, either via their colored bar or via their row in the table below it. Selecting the category will show a description of what is encompassed in this category in the Selection Details section.
 - Reduced the amount of memory used for snapshot preview images in the Editor.
 - The Name column in the tables now shows managed string content in quotes.
 - On capturing or importing a snapshot, the Snapshot side bar is now toggled visible if it was not before. It also scrolls to reveal the new snapshot.

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

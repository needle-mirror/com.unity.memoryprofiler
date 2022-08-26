# What's new in version 1.0.0

Summary of changes in Memory Profiler version 1.0.0. This includes changes from the 1.0.0-pre.1, 1.0.0-pre.2 and 1.0.0-pre.3 releases.

## Added

* Added the __All Of Memory__ snapshots comparison tab.
* Added a __Close__ button to close snapshots. Single-clicking a selected snapshot no longer closes it.
* Added Unity Objects breakdown comparison between two captures.

## Updated

* Removed the __Swap__ button from the __Compare Snapshots__ workflow. To change which snapshots you compare, you must now close one of the currently selected snapshots before you select a new one.
* Removed colors from the __A__ and __B__ UI elements in snapshots comparisons.
* The details view UI now hides the __References__ section if the selected item doesn't have any references.
* Removed the Objects and Allocations tab.
* Removed the Fragmentation tab.
* Removed the Tree Map view.
* Promoted the Unity Objects and All Of Memory views to their own tabs.

## Fixed

* Renaming a snapshot no longer allows you to use the same name as another snapshot with some letters in a different case.
* Fixed the detail panel reference view not showing all references for the selected object.
* Selecting a Unity Object Type in the Unity Objects Comparison view now clears the selection in the Details view.
* Fixed `Value cannot be Null` error and infinite Circular References when expanding types with self references in the Managed Fields table in the Selected Item Details panel.
* Fixed an exception occurring when loading an unsupported snapshot and opening the Unity Objects breakdown.

For a full list of changes and updates in this version, see the [Memory Profiler Changelog](xref:changelog).

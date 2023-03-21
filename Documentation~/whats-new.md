# What's new in version 1.1.0-exp.1

Summary of changes in Memory Profiler version 1.1.0-exp.1.

### Added
- Added a dropdown to the Unity Objects and All Of Memory views that allows switching between showing all sizes as they relate to Resident Memory, Allocated Memory or both.
- Added support for RenderTexture metadata in memory captures.
- Added tooltips to sizes in details view, showing the memory usage in bytes.

### Changed
- Reduced managed memory usage when opening snapshots by loading the managed heap bytes into native instead of managed memory container.
- Changed the naming of managed object entries in tables when they are not connected to a native Unity Object from "No Name" to their address. Strings and char arrays additionally show their first characters after their address value. Entries for Unity Objects that had their native object destroyed further get the postfix "(Leaked Shell)" after their address.
- Changed Unity Object comparison for same session comparisons to further distinguish Unity Objects by their Instance IDs, unless the table is flattened.
- Improved Preview, Search and Select In Editor functionality, especially for Scene Objects.
- Objects of types inheriting from MonoBehaviour or ScriptableObject are now grouped under their managed type name in the Unity Objects table.

### Fixed
- General layout improvements in details view.
- Fixed missing allocators information in Native->Reserved breakdown in compare mode of All of Memory view.
- Fixed Managed Fields table to show details for struct data.
- Searching the tables now correctly finds items by their type name or overarching group name.
- Fixed Exceptions when opening snapshots with a managed heap bigger than 2 GB and contiguous managed heap sections bigger than 2 GB ([PROFB-41](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-41)).
- Fixed IndexOutOfBoundsException when selecting Managed Type groups in All Of Memory table if all instances are Leaked Shell objects.
- Fixed a bug where the Select In Editor button was available, even though the selected object did not clearly refer to one particular item. Clicking the button could therefore select the wrong item. ([PROFB-54](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-54))
- Fixed captures failing to save when the product name contained illegal characters ([PROFB-63](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-63)).
- Fixed NullReferenceException when selecting a PrefabImporter in the UnityObjects or All Of Memory tables ([PROFB-58](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-58))
- Fixed Search in Editor button being enabled when nothing can be searched for ([PROFB-59](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-59)).
- Fixed the issue that finding managed objects by their type was impossible ([PROFB-64](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-64)).

For a full list of changes and updates in this version, see the [Memory Profiler Changelog](xref:changelog).

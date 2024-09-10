# What's new in version 1.1.1

Summary of changes in Memory Profiler version 1.1.1.

### Added
- Added the name of the assembly to the Type details when selecting a Managed Type group.

### Fixed
- Fixed navigating with arrow keys not updating reference list in All of Memory view ([PROFB-153](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-153)).
- Fixed retention of Snapshot list expansion and scroll state so that it does not expand all items unexpectedly but remembers which sessions were collapsed and which extended ([PROFB-196](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-196)).
- Fixed dropdown text being clipped when "Allocated and Resident Memory on Device" was selected.
- Fixed an issue whereby graphics allocations not associated with a native object would display under a "No Name" category in the All of Memory table.
- Fixed issues with selecting array objects, or objects that had managed fields with arrays, when the arrays where multidimensional with one or more dimensions of size 0.
- Fixed exception when opening the Unity Objects table in Comparison mode when there are multiple MonoBehaviour or ScriptableObject types with the same name but from different assemblies.
- Fixed exception when opening the All Of Memory table in Comparison mode when either of the snapshots is from a pre 2022.2 runtime and the untracked amount is negative.
- Fixed an ArgumentOutOfRangeException when inspecting some Managed Objects ([PROFB-223](https://issuetracker.unity3d.com/product/unity/issues/guid/PROFB-223)).
- Fixed longer string values not being displayed in the Managed Fields Inspector.
- Fixed an issue that caused IL2CPP VM memory to not be calculated correctly in the Summary and All Of Memory table.
- Remove deprecated UxmlFactory/UxmlTraits API usage on Unity 6.
- Add divider on "Capture" toolbar button dropdown.
- Reattributed IL2CPP VM memory from Native to Managed in the All Of Memory table.

### Changed
- Allowed Snapshots to reside in Subfolders within the configured memory snapshot path.
- Improved Snapshots folder monitoring: Changes to the folder are now reflected in the Editor without it requiring to acquire focus first.
- Improved display of managed arrays in the Selected Item Details and Managed Fields.

For a full list of changes and updates in this version, see the [Memory Profiler Changelog](xref:changelog).

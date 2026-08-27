# Memory Profiler preferences reference

Explore the Memory Profiler preferences you can use to configure snapshot storage, analysis views, and reference calculations when you analyze memory in your project.

Use the Memory Profiler preferences to change where snapshots are stored and how the [Memory Profiler window](memory-profiler-window-reference.md) and built-in [Memory Profiler module](xref:um-profiler-memory) display memory data. To open the preferences, go to __Edit__ &gt; __Preferences__ &gt; __Analysis__ &gt; __Memory Profiler__. For more information about relative snapshot paths, refer to [Snapshots introduction](snapshots-concepts.md).

| **Property** | **Description** |
| :--- | :--- |
| **Memory Snapshot Storage Path** | Sets the folder where the Memory Profiler saves memory snapshot files. The path must be relative to the project folder, for example `./MemoryCaptures`. Use the preferences title bar menu to revert this path to the default. |
| **Replace Memory UI in Profiler Window** | When enabled, replaces the details panel of the built-in Memory Profiler module in the [Profiler](xref:um-profiler) window with the Memory Profiler package interface. |
| **Reset Opt-Out settings for dialog prompts** | Resets all opt-out settings for Memory Profiler dialog prompts so those prompts can appear again. |
| **Truncate Managed Type Names** | When enabled, shortens fully qualified managed type names in Memory Profiler tables and views to improve readability. |
| **Show reserved native memory breakdown** | When enabled, displays reserved native memory broken down by individual allocators in the **All Of Memory** view. |
| **Show Memory Map view** | When enabled, displays the **Memory Map** view with a low-level breakdown of operating system memory regions. |
| **Enable Shortest Path to Root** | When enabled, the Memory Profiler calculates root paths when a snapshot opens. This increases snapshot opening time but enables the **Shortest Path To Root** tab in the [References panel](references-component.md) and **Impact** values in the [Selection Details panel](selection-details-component.md). Disable this option if snapshot opening fails or is too slow, and report the issue so the Memory Profiler team can address it. Enabled by default. |
| **Parse pointer sized fields as ref. to native** | When enabled, treats pointer-sized fields in managed types as potential references to native allocations when opening a snapshot, not only fields on types in the `Unity.Entities` namespace. This can improve native reference discovery but increases snapshot opening time and can produce false positives. Use this option if your code stores native addresses in non-pointer fields. |

## Additional resources

* [Memory Profiler window reference](memory-profiler-window-reference.md)
* [Snapshots introduction](snapshots-concepts.md)
* [References panel reference](references-component.md)
* [Selection Details panel reference](selection-details-component.md)
* [Capture and import snapshots](snapshot-capture.md)

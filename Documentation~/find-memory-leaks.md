# Find memory leaks

Memory leaks cause your application to perform worse over time and might lead to a crash.

Memory leaks typically happen for one of the following reasons:

* Missing code to release objects from memory.
* Unintentional references keeping objects in memory.

To identify memory leaks, capture and compare multiple snapshots. Refer to [Compare two snapshots](snapshots-comparison.md) for more information.

## Detect memory leaks after scene unload

Leaks often result from user-allocated objects or resources not released after a scene unload.

To identify this type of leak perform the following steps:

1. Open **Window** &gt; **Analysis** &gt; **Memory Profiler**.
1. Use the [Attach to Player](memory-profiler-window-reference.md#memory-profiler-toolbar) dropdown to set the source as a running Player.
1. Load an empty [scene](xref:um-creating-scenes) and [create a snapshot](snapshot-capture.md) of it.
1. Load the test scene, play partway through, then unload it or change to an empty scene. To fully unload assets, call [`Resources.UnloadUnusedAssets`](xref:UnityEngine.Resources.UnloadUnusedAssets) or load two new scenes consecutively.
1. Take another snapshot and optionally close the Player.
1. [Compare snapshots](snapshots-comparison.md) and use any of the [Memory Profiler tabs](memory-profiler-window-reference.md) to inspect the snapshots.

Increased memory usage in the second snapshot might indicate a memory leak.

## Additional resources

* [Compare two snapshots](snapshots-comparison.md)
* [Memory Profiler window reference](memory-profiler-window-reference.md)
* [Analyzing Unity object memory leaks](managed-shell-objects.md)

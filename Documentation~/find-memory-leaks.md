# Find memory leaks

Memory leaks cause your application to perform worse over time and might eventually lead to a crash.

Memory leaks typically happen for one of two reasons:

* Your project lacks code to release an object from memory, which leads to the object remaining in memory permanently.
* An object stays in memory because of an unintentional reference.

You need to capture and compare multiple snapshots to identify memory leaks. To learn how to do this, see [Compare two snapshots](snapshots-comparison.md).

## Find memory leaks that happen after Scene unload

There are multiple ways that memory leaks happen. A common cause of leaks is user-allocated objects or resources that your code doesn't release after your application unloads a __Scene__.

To identify this type of leak with the Memory Profiler package:

1. Attach the Memory Profiler to a running Player. For instructions on how to attach the Memory Profiler to a Player, see [Capture a snapshot](snapshot-capture.md).
1. Load an empty [Scene](https://docs.unity3d.com/Manual/CreatingScenes.html) in the Player.
1. [Create a snapshot](snapshot-capture.md) of the empty Scene.
1. Load the Scene you want to test for leaks.
1. Play partway through the Scene.
1. Unload the Scene or switch to an empty Scene. To fully unload Assets in the last opened Scene, you need to either call [Resources.UnloadUnusedAssets](https://docs.unity3d.com/ScriptReference/Resources.UnloadUnusedAssets.html) or load into two new Scenes (e.g. Load Empty Scene twice).
1. Take another snapshot. You can optionally close the Player after you take this snapshot.
1. Follow the instructions in [Compare two snapshots](snapshots-comparison.md) to open and compare both snapshots.
1. Use any of the three [Memory Profiler window](memory-profiler-window-reference.md) tabs to evaluate the two snapshots. Any increase in memory use in the second snapshot is potentially due to a memory leak.

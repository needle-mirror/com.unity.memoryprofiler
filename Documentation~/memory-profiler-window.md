# Getting started

To use the Memory Profiler, open its window (menu: __Window &gt; Analysis &gt; Memory Profiler__)

The Memory Profiler window has three main areas:

![Memory Profiler window breakdown](images/memory-profiler-windoW.png)<br/>*The Memory Profiler window*

__A__ The [Snapshots panel](snapshots-panel.md), which contains all the memory snapshots in your Project, and allows you to compare snapshots.<br/>
__B__ The [Main view](main-view.md), which displays the in-depth memory data of your snapshot.

At the top of the window there are the following controls:

|__Control__|__Function__|
|:---|:---|
|__Toggle snapshot panel__| Expand or hide the snapshots panel.
|__Attach to Player__| Select which target to take a memory snapshot of. You can select Play Mode in the Editor, any Player you have running, or the Editor itself. You can also click __Enter IP__ in the drop-down to manually enter the IP address of the device you want to profile your application on. For more information, see the User Manual documentation on [Profiling your application](https://docs.unity3d.com/Documentation/Manual/profiler-profiling-applications.html).|
|__Capture__| Select this button to take a memory snapshot. This operation might take a few seconds, depending on the size of your application. Once the Memory Profiler has captured the snapshot, it appears in the __Snapshots panel__.|
|__Import__| Import a saved capture file. For more information, see [Import a snapshot](#import-a-snapshot) in this documentation.|
|__View history__| Use the arrow buttons to switch between views you previously opened.|
|__Load view__| Load a saved view.|

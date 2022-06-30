# The Memory Profiler window

The __Memory Profiler__ package operates in its own window inside the Editor.

To open the Memory Profiler window, go to  __Window__ &gt; __Analysis__ and select __Memory Profiler__.

The Memory Profiler window has four components:

![Memory Profiler window breakdown](images/memory-profiler-window.png)<br/>*The Memory Profiler window*

__A__ The [Snapshots component](snapshots-component.md), which contains all the memory snapshots in your Project, and allows you to compare snapshots.<br/>
__B__ The [Main component](main-component.md), which displays the in-depth memory data of your snapshot.</br>
__C__ The [References component](references-component.md) shows where the selected object references or is referenced by another object.</br>
__D__ The [Selection Details component](selection-details-component.md) displays more detailed information about the selected object.</br>

At the top of the window there are the following controls:

|__Control__|__Function__|
|:---|:---|
|__Toggle snapshots component__| Expand or hide the snapshots component.
|__Attach to Player__| Choose a target to take a snapshot of. You can select Play Mode in the Editor, any Player you have running, or the Editor itself. You can also click __Enter IP__ in the drop-down to manually enter the IP address of the device you want to profile your application on. For more information, see [Profiling your application](https://docs.unity3d.com/Documentation/Manual/profiler-profiling-applications.html).|
|__Capture__| Select this button to take a memory snapshot. This operation might take a few seconds, depending on the size of your application. Once the Memory Profiler has captured the snapshot, it appears in the __Snapshots component__.|
|__Import__| Import a saved capture file.|

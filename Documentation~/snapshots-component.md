# Snapshots component

The Snapshots component displays a list of memory snapshots in your project. You can select an individual snapshot for analysis, or compare any two snapshots. The Memory Profiler package can capture snapshots of the Unity Editor or of a running Player.

> [!NOTE]
> Editor-only snapshots don't include a screenshot.

![The Snapshots component](images/snapshots-component.png)

A - [Open Snapshots Pane](#open-snapshots-pane) </br>
B - List of available snapshots </br>

Each snapshot in the list of available snapshots displays a screenshot of the profiling target during the capture, a default name, and the time and date of the capture. If there are snapshots in the list from more than one session, Unity sorts them into logical groups, which you can click on to expand or collapse them.

Unity stores the date on each snapshot in Universal Coordinated Time (UTC) format and converts it to your computer's local time. Hover your cursor over the date to see which Project the snapshot is from.

## Path to captured snapshots

When you create a snapshot for the first time, Unity creates a sub-folder in your Project folder called `MemoryCaptures`. By default, the Memory Profiler stores all snapshots in this folder.

To define a storage path for the snapshots,  go to __Edit__ &gt; __Preferences__ &gt; __Analysis__ &gt; __MemoryProfiler__ and edit the __Memory Snapshot Storage Path__ property. The default value is "MemoryCapture".

The path in this property is relative, which means it must start with:
* "./" if the `MemoryCaptures` folder is within the Project folder
* "../" if the `MemoryCaptures` folder is one level above the `Project` folder in the hierarchy.

![Memory Profiler Preferences](images/preferences-memory-profiler.png) <br/> *Memory Profiler Preferences*

## Capture a snapshot

You can capture a snapshot from the Editor, from an application running in Play Mode in the Editor, or from a player running on your local machine or connected device. Use the __Attach to Player__ dropdown in the Profiler Window toolbar to choose between these options.

By default, the Memory Profiler captures Editor snapshots. When an application is running in Play Mode or in a Player, Unity adds those options to the dropdown menu. They don't appear in the dropdown if no application is running.

After you choose a capture target in the dropdown menu, you can use either of two buttons to capture a new snapshot:

* The __Capture New Snapshot__ button is visible in the Memory Profiler window when you have no snapshots selected
* The __Capture__ button is always visible in the menu bar in the Memory Profiler window

Both buttons perform the same operation. The __Capture__ button on the toolbar also has a dropdown menu alongside it, which you can use to configure the snapshot.

You can also capture a memory snapshot through a script. For information about how to captures snapshots this way, see the [Memory Profiler.TakeSnapshot](https://docs.unity3d.com/ScriptReference/Profiling.Memory.Experimental.MemoryProfiler.TakeSnapshot.html) Scripting API documentation. For more information about using custom metadata with snapshots in code, see [MetaData](https://docs.unity3d.com/ScriptReference/Profiling.Memory.Experimental.MetaData.html) and [Add Player hook](tips-and-troubleshooting.md#add-player-hook).

## Import snapshots

If you already have access to existing memory snapshots, you can import them into the Memory Profiler. There are two ways to import a snapshot:

* [Copy the snapshot into your `Project` folder](#copy-snapshots-into-the-project-folder)
* [Use the __Import__ button in the __Snapshots component__](#use-the-import-button-in-the-snapshots-component)

### Copy snapshots into the Project folder

1. Inside your Project folder, find or create a folder named `MemoryCaptures`.
2. Copy the snapshot files to this folder.
3. Open the [Memory Profiler window](memory-profiler-window.md), and you can see the added snapshot in the Snapshots component.

### Use the __Import__ button in the __Snapshots component__

1. In the Memory Profiler window toolbar, click on the __Import__ button. This opens a file browser window.
2. In the file browser window, locate and open the memory snapshot you want to import. When you import a .snap file, Unity copies the file to your `MemoryCaptures` folder. Unity creates this folder if it doesn't already exist.

## Opening snapshots

To open a single snapshot and view its associated data, select the snapshot from the list in the Snapshot Panel with a single click.

To open two snapshots and compare them, enable the __Compare Snapshots__ mode in the [Open Snapshots pane](#open-snapshots-pane), then select the two snapshots you want to compare from the list.

[The Main component](main-component.md) then displays different visualizations of the snapshot data.

> [!WARNING]
> Click on a snapshot to open it. Opening a snapshot can be a long process because of how much data a snapshot can contain. Clicking the snapshot name doesn't open the snapshot; instead, this opens a renaming text box which you can use to rename the snapshot.

## Open Snapshots pane

The __Open Snapshots__ pane displays the currently selected snapshot or snapshots. By default, the __Single Snapshot__ mode is active, which enables you to view one snapshot at a time. Select the __Compare Snapshots__ mode to choose two snapshots to compare to each other. When in __Compare Snapshots__ mode, Unity keeps both snapshots in active memory to minimize the time needed to switch between them. For more information, see [Compare two snapshots](workflow-objects-comparison).

Unity displays details about any selected snapshot in the __Open Snapshots__ pane, including:

* The screenshot associated with the snapshot
* The snapshot's name
* The time and date of capture
* The session you captured it in
* The project you used to capture it
* Icons to indicate the platform and application you used to capture it (whether in the Editor or a Player)
* The total memory used by your application during the snapshot, and the total resources available at the time

# Open, import and capture snapshots

## Capture a snapshot

You can capture a snapshot from the Editor, from an application running in Play mode in the Editor, or from a player running on your local machine or connected device. Use the __Attach to Player__ dropdown in the Profiler Window toolbar to choose between these options.

By default, the Memory Profiler captures Editor snapshots. When an application is running in Play mode or in a Player, Unity adds those options to the dropdown menu. They don't appear in the dropdown if no application is running.

After you choose a capture target in the dropdown menu, you can use the following buttons to capture a new snapshot:

* The __Capture New Snapshot__ button is visible in the Memory Profiler window when you have no snapshots selected.
* The __Capture__ button is always visible in the control bar in the Memory Profiler window.

Both buttons perform the same operation. Alongside the __Capture__ button on the control bar there is a __Choose Capture Flags__ dropdown menu, which you can use to configure the snapshot. See the table entry in [Memory Profiler window](memory-profiler-window-reference.md) for more information.

You can also capture a memory snapshot through a script. For information about how to capture snapshots this way, see the [Memory Profiler.TakeSnapshot](https://docs.unity3d.com/ScriptReference/Profiling.Memory.Experimental.MemoryProfiler.TakeSnapshot.html) Scripting API documentation. For more information about using custom metadata with snapshots in code, see [MetaData](https://docs.unity3d.com/ScriptReference/Profiling.Memory.Experimental.MetaData.html) and [Add Player hook](snapshots-concepts.md#add-player-hook).

## Import snapshots

If you already have access to existing memory snapshots, you can import them into the Memory Profiler. You can import a snapshot in any of the following ways:

* [Copy the snapshot into your `Project` folder](#copy-snapshots-into-the-project-folder)
* [Use the __Import__ button in the __Snapshots component__](#use-the-import-button-in-the-snapshots-component)

### Copy snapshots into the Project folder

1. Inside your Project folder, find or create a folder named `MemoryCaptures`.
2. Copy the snapshot files to this folder.
3. Open the [Memory Profiler window](memory-profiler-window-reference.md), and you can see the added snapshot in the Snapshots component.

### Use the Import button in the Snapshots component

1. In the Memory Profiler window toolbar, click on the __Import__ button. This opens a file browser window.
2. In the file browser window, locate and open the memory snapshot you want to import. When you import a .snap file, Unity copies the file to your `MemoryCaptures` folder. Unity creates this folder if it doesn't already exist.

## Opening snapshots

To open a single snapshot and view its associated data, select the snapshot from the list in the Snapshot Panel with a single click. Opening a snapshot might take a long time because a snapshot can contain a lot of data.

To open two snapshots and compare them, enable the __Compare Snapshots__ mode in the [Open Snapshots pane](snapshots-component.md#open-snapshots-pane), then select the two snapshots you want to compare from the list.

[The Main component](main-component.md) then displays different visualizations of the snapshot data.

> [!TIP]
> Clicking the snapshot name doesn't open the snapshot; instead, this opens a renaming text box which you can use to rename the snapshot.

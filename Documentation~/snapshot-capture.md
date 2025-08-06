# Capture and import snapshots

To capture a snapshot in the Memory Profiler window:

1. Open the Memory Profiler window: **Window** &gt; **Analysis** &gt; **Memory Profiler**.
1. Use the [Attach to Player](memory-profiler-window-reference.md#memory-profiler-toolbar) dropdown in the toolbar of the Memory Profiler window to set a source for the snapshot. You can capture a snapshot from the following sources:

    * The Unity Editor.
    * A player running on your local machine or device.

1. Use one of the following buttons to capture a snapshot:

    * __Capture New Snapshot__ in the Memory Profiler window when you have no snapshots selected.
    * __Capture__ button in the toolbar.

Use the **Capture** dropdown menu to configure the snapshot. For more information, refer to [Memory Profiler window reference](memory-profiler-window-reference.md#memory-profiler-toolbar).


> [!NOTE]
> There is no clean separation between the memory used for Play mode and memory used for the Editor that runs in Play mode, so Editor snapshots always contain more memory and have a different memory usage behavior than a Player does, even if it runs on the same platform as the Editor. Taking Editor snapshots is therefore only recommended for faster iteration flows while optimizing, whereas the final result of optimization work should always be checked by analyzing the usage of a Player build.

## Open snapshots

To open a single snapshot and view its associated data, select the snapshot from the list in the [Snapshot panel](snapshots-component.md) with a single click. Opening a snapshot might take a long time because a snapshot can contain a lot of data.

To open two snapshots and compare them, select the __Compare Snapshots__ tab, then select the two snapshots you want to compare from the list.

[The Main panel](main-component.md) then displays different visualizations of the snapshot data.

> [!TIP]
> Clicking the snapshot name doesn't open the snapshot. Instead, this opens a renaming text box which you can use to rename the snapshot. You can also open a context menu via a right click on the snapshot to rename or delete the snapshot, as well as opening its containing folder.


## Import snapshots

If you already have access to existing memory snapshots, you can import them into the Memory Profiler. You can import a snapshot in any of the following ways:

* [Copy the snapshot into your `Project` folder](#copy-snapshots-into-the-project-folder)
* [Use the Import button in the Memory Profiler window](#use-the-import-button-in-the-memory-profiler-window)

### Copy snapshots into the Project folder

1. Inside your Project folder, find or create a folder named `MemoryCaptures`.
1. Copy the snapshot files to this folder.

The Memory Profiler window then displays the snapshots in the [snapshots panel](snapshots-component.md).

### Use the Import button in the Memory Profiler window

1. Open the Memory Profiler window: **Window** &gt; **Analysis** &gt; **Memory Profiler**.
1. In the toolbar, select the __Import__ button. This opens a file browser window.
1. In the file browser window, locate and open the memory snapshot you want to import.

When you import a `.snap` file, Unity copies the file to your `MemoryCaptures` folder. Unity creates this folder if it doesn't already exist.

## Additional resources

* [Snapshots introduction](snapshots-concepts.md)
* [Snapshots panel reference](snapshots-component.md)
* [Compare two snapshots](snapshots-comparison.md)

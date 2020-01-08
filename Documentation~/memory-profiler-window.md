# Getting started

To use the Memory Profiler, open its window (menu: __Window &gt; Analysis &gt; Memory Profiler__)

The Memory Profiler window has three main areas:

![Memory Profiler window breakdown](images/memory-profiler-windoW.png)<br/>*The Memory Profiler window*

**A** The [Workbench](workbench.md), which contains all the memory snapshots in your Project, and allows you to compare snapshots.<br/>
**B** The [Main view](main-view.md), which displays the in-depth memory data of your snapshot. 

At the top of the window there are the following controls:

|**Control**|**Function**|
|:---|:---|
|**Attach to Player**| Select which target to take a memory snapshot of. You can select Play Mode in the Editor, any Player you have running, or the Editor itself. You can also click **Enter IP** in the drop-down to manually enter the IP address of the device you want to profile your application on. For more information, see the User Manual documentation on [Profiling your application](https://docs.unity3d.com/Documentation/Manual/profiler-profiling-applications.html).|
|**Capture**| Select this button to take a memory snapshot. This operation might take a few seconds, depending on the size of your application. Once the Memory Profiler has captured the snapshot, it appears in the **Workbench**.|
|**Import**| Import a saved capture file. For more information, see [Import a snapshot](#import-a-snapshot) in this documentation.|
|**View**| When you open a memory snapshot, use this drop-down menu to select how you would like to view the data. You can either choose **Tree Map**, **Memory Map**, or **Table view**. For more information on these views, see the documentation on [The Main View](main-view).|
|**View history**| Use the arrow buttons to switch between views you previously opened.|
|**Load view**| Load a saved view.|

## Capture a memory snapshot

To capture a snapshot, you can either chose to profile the Editor, Play Mode, or a player running on your local machine or connected device.

When you enter Play Mode, or have a Player running on your computer or connected device, they appear in the __Attach to Player__ dropdown at the top of the window.

Choose one of the options from the dropdown menu, and then select the __Capture__ button. This operation might take a few moments to capture. **Note:** The __Capture__ button is labelled __Capture Editor__ when the Memory Profiler targets the Unity Editor (not in Play Mode). When you select Play Mode, or a running player, the button is labelled __Capture Player__.

Once you have captured a snapshot, it appears in the __Workbench__. 

When you capture a snapshot of the Editor in Play Mode, it also includes any memory the Editor uses. The memory usage in Play Mode is not representative of the memory usage on a device. You should always capture snapshots from a built Player on the targeted platform for the most accurate memory profiling.

You can also trigger a memory snapshot at any moment you decide, without any interaction with the Memory Profiler window. Use the [Memory Profiler Scripting API](https://docs.unity3d.com/ScriptReference/Profiling.Memory.Experimental.MemoryProfiler.TakeSnapshot.html) to set up capture logic in a script inside your Player. For information about the custom [MetaData](https://docs.unity3d.com/ScriptReference/Profiling.Memory.Experimental.MetaData.html) collection, see [Add Player hook](tips-and-troubleshooting.md#add-player-hook).

### Snapshot location
When you create a snapshot for the first time, the Editor creates a sub-folder under your `Project` folder called `MemoryCaptures`. This folder is where the Memory Profiler stores all of your snapshots.

By default, the Memory Profiler stores all snapshots in `<Path/of/Your/ProjectFolder>/MemoryCaptures`. To change the default path go to __Preferences &gt; Analysis &gt; Profiling &gt; MemoryProfiler__.

![Memory Profiler Preferences](images/preferences-memory-profiler.png) <br/> *Memory Profiler Preferences*

**Note**: The path must be relative, i.e., it must start with “./” or “../” to denote its location within, or one folder hierarchy above, the `Project` folder respectively.

## Import a snapshot

To import a snapshot, you can do this via the Project folder, or via Workbench.

To import via the Project folder, perform the following steps:

1. Inside your Project folder, find the folder named `MemoryCaptures`, or create it if it does not exist. 
1. Copy the snapshot files to this folder.
1. Open the [Memory Profiler window](memory-profiler-window), and you can see the added snapshot in the [Workbench](workbench) panel. 

To import via Workbench:

1. In the Memory Profiler, select the __Import__ button. This action opens a file dialog.
1. Locate and open the memory snapshot you want to import. If you choose to import a .snap file, Unity copies the file to your `MemoryCaptures` folder. 

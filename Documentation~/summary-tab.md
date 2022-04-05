# Summary tab

This tab displays general information about the state of memory in the selected snapshot. You can investigate the information the Summary tab displays in more detail in the other available views.

![The Main Component in the Memory Profiler window](images/main-component.png)
<br/>*The Summary tab in the Main component*

 The Summary tab contains the following sections:

* The [Memory Usage Overview](#memory-usage-overview) describes how each type of memory is allocated, compared to the total memory used in the snapshot.
* [Potential Capture Issue](#potential-capture-issues) displays warnings and errors you might have with the snapshot.

## Memory Usage Overview

This view displays information about how the total memory allocated during the snapshot is split between the different types of memory. You can select any element in this view to see more detailed information about it in the [Selection Details component](selection-details-component.md).

The Memory Usage Overview displays the data in three sections:

* The __Committed Memory Tracking Status__ section displays the proportion of total memory that was tracked and untracked when Unity captured the snapshot.
* The __Memory Usage__ section displays how the tracked memory in the snapshot was divided between the different types of available memory.
* The __Managed Memory__ section displays a breakdown of the memory that Unity manages which you can't affect, such as memory in the managed heap, memory used by a virtual machine, or any empty memory pre-allocated for similar purposes.

## Potential Capture Issues

This section contains an exhaustive list of any issues the snapshot might have. For example, if you capture an Editor snapshot, this section displays a warning about how to separate your application's memory profile from the Editor's profile.

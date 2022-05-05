# Summary tab

This tab displays general information about the state of memory in the selected snapshot or snapshots. You can investigate the information the Summary tab displays in more detail in the other available tabs.

![The Main Component in the Memory Profiler window](images/summary-tab.png)
<br/>*The Summary tab in the Main component*

 This tab contains the following sections:

* The tips section displays any contextual warnings or additional useful information about the snapshots, such as potential issues with your snapshot and insight about how to understand an Editor capture.
* Total Committed Memory displays how each type of memory is allocated, compared to the total memory used in the snapshot.
* Managed Memory displays a breakdown of the memory that Unity manages which you can't affect, such as memory in the managed heap, memory used by a virtual machine, or any empty memory pre-allocated for similar purposes.
* The Top Unity Objects Categories section displays which types of Unity Objects use the most memory in the snapshot.

Hover over any of the bars in the tab to highlight the corresponding label, and vice versa. Continue hovering your cursor over the bar or label to display how much that element contributes to the total, as a percentage.

You can also select any element to display more detailed information about it in the [Selection Details component](selection-details-component). To investigate any of the sections in more depth, select the __Inspect__ button to open either the [All Of Memory tab](all-memory-tab) (from the Total Committed Memory section or the Managed Memory section) or the [Unity Objects tab](unity-objects-tab) (from the Top Unity Objects Categories section).

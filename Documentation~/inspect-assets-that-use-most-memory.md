# Inspect the assets that use the most memory

Start by identifying which objects are the best candidates for optimization:

1. Open a snapshot - to do this, follow the instructions in [Opening snapshots](snapshot-capture.md#opening-snapshots).
2. Open the [Unity Objects tab](unity-objects-tab.md).

The Memory Profiler window sorts the table in descending order by default. If you've changed the sort order, select the __Total Size__ column header to change the sort order back to descending order for this workflow. This ensures that the objects with the highest memory use are easily visible at the top of the table.

You can now search through the results in two ways:

* Expand groups to show individual objects within the group.
* Enable the __Flatten hierarchy__ property to show only individual objects in the table.

If you aren't sure which objects are most likely to use excessive memory, leave the __Flatten hierarchy__ property disabled and look at the groups to see where the largest objects are likely to be. Enable the property if you're confident about most of your assets but suspect there are a small number of outliers using too much memory.

Enable the __Show Potential Duplicates Only__ property to see objects that the Memory Profiler identifies as potentially being duplicates of each other. You can see more detailed information about these objects with the [References component](references-component.md) and the [Selection Details component](selection-details-component.md). Use this information to determine whether the objects are expected duplicates, such as multiple instances of a Prefab that should exist in a scene, or problematic duplicates, such as objects that are created unintentionally or objects that Unity hasn't disposed of correctly.

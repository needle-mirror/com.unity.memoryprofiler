# Inspect the assets that use the most memory

To identify which objects use the most memory and are the best candidates for optimization:

1. [Create or import a snapshot](snapshot-capture.md).
2. Open the [Unity Objects tab](main-component.md#unity-objects-tab).

The Memory Profiler window sorts the table in descending order by default. If you have changed the sort order, select the __Total Size__ column header to change the sort order back to descending order. This ensures that the objects with the highest memory use are visible at the top of the table.

## Find assets that use significant memory

You can search through the results in the following ways:

* Expand groups to display individual objects within the group.
* Enable the __Flatten hierarchy__ property to display only individual objects in the table.

If you're not sure which objects are most likely to use excessive memory, disable the __Flatten hierarchy__ property and inspect the groups to determine where the largest objects are. Enable the property if you're confident about most of your assets but suspect there are a small number of outliers using too much memory.

Enable the __Show Potential Duplicates Only__ property to filter the table and display only objects that the Memory Profiler has flagged as duplicates. You can examine these objects in detail using the [References panel](references-component.md) and the [Selection Details panel](selection-details-component.md). This information helps you determine whether the duplicates are intentional, such as multiple instances of a prefab meant to exist in a scene, or unintentional, such as objects created by mistake or not disposed of by Unity.

## Additional resources

* [Capture and import snapshots](snapshot-capture.md)
* [Analyzing Unity object memory leaks](managed-shell-objects.md)

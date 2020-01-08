# Tree Map

The Tree Map takes the memory data of a snapshot and visually groups it under different object categories. The size of each category represents how large its memory footprint is, compared with other categories.

![Tree Map view in the Memory Profiler window](images/tree-map-memory-profiler.png)<br/>*Memory Profiler Main View in Tree Map view*

When you select an object category in the Tree Map, the Memory Profiler displays all of the objects within that category. You can then select the individual objects in that category, and a white rectangle highlights your selection.

![Tree map with selected object](images/tree-map-selected-object.png)<br/> *Tree Map view with a selected object in the Texture2D category*

## Tree Map table view

The [Table view](table-view.md) below the Tree Map, lists all of the objects in a selected object category. If you haven't selected an object category, it contains all of the objects in the given memory snapshot. When you select one of the objects in an object category, the table view jumps to the row that relates to that object.
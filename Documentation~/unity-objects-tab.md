# Unity Objects tab

The __Unity Objects__ tab shows any Unity objects that use memory. Use this information to identify areas where you can eliminate duplicate memory entries or to find which objects use the most memory. Use the search bar to find entries in the table which contain the text you enter.

> [!NOTE]
> The search bar can only search for items that contain text in the target objects name. This will be changed to include other search functionality in future updates.

By default, the table lists all relevant objects by **Total Size** in descending order. You can click on a column header name to sort the table by that column, or right-click on a column name to open a sub-menu to hide or show any column.

The **Total Size % Bar** column displays the data as a percentage of the **Total Memory In Table** value. All measurement bars, including the __Total Memory In table__ bar, adjust dynamically based on the select object in the table.

![The Unity Objects tab](images/unity-objects-tab.png)
</br>*The Unity Objects tab*

## Modifier toggles

There are two toggles you can use to change which entries the table displays, which are both disabled by default:

* Enable the __Flatten Hierarchy__ toggle to remove the parent-child abstraction from the table and list all objects as single entries instead. For example, if __Flatten Hierarchy__ was enabled in the above screenshot, the __RenderTexture__ at the top of the list would be replaced with ten individual entries, instead of a dropdown menu with ten child entries.
* Enable the __Show Potential Duplicates Only__ toggle to only show instances where objects might be separate instances of the same object.

The __Show Potential Duplicates Only__ toggle populates the table with information about duplicated memory use. When this toggle is enabled, the Memory Profiler window groups any objects in the table with the same name, size, and type together. You can then look through the list to separate any similar objects that should be independent from those that are two instances of the same object.

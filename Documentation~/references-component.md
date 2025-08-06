# References panel reference

When you select an object in the [Memory Profiler window](memory-profiler-window-reference.md), its references are displayed in the **References** section, located at the top of the right-hand panel.

![The References panel](images/references-component.png)</br>_The References panel._

The References section has the following tabs:

|**Tab**|**Description**|
|---|---|
|__Referenced By__| Displays the tree of references leading to the selected object. By default, the entries in this tab are collapsed and only display the reference in the chain that is closest to the selected object. Select objects that belong to a hierarchy to expand them and display the full reference hierarchy of the object and its roots.|
|__References To__| Displays a list of objects that the selected object references. Only displays all directly referenced objects but not the objects referenced by these.|

The References section displays the selected object's name regardless of whether it contains any references. If a selected object contains references or if another object references the selected object, then the references are displayed in the appropriate tab.

## Additional resources

* [Snapshots panel reference](snapshots-component.md)
* [Main panel reference](main-component.md)
* [Selection Details panel reference](selection-details-component.md)

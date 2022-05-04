# Selection Details

This view displays detailed information about an item you select in either the [Main view](main-view) or the [References view](references-view).  The contents of this view changes dynamically based on the selected object. The Selection Details view can contextually display the following groups of data:

* Basic - displays high level information about any selected object.
* Help - displays text to explain the status of the object in more detail.
* Advanced - displays more detailed information about the object than the Basic group. The Selection Details panel doesn't display this group for all types of objects.
* Preview - displays a preview of how an object appears in the Editor or your application e.g. Shaders.
* Managed Fields - displays a table including any managed fields the selected object contains and information about those fields.

## Basic

This data group contains three entries:

* The **Size** entry displays the size of the object in memory, and how much of that total is native or managed memory.
* The **Referenced By** entry displays how many other objects reference the selected object and how many self-references the object has. The [References view](references-view) provides more details about these references.
* The **Status** entry displays the type of object selected, whether or not it's used anywhere in the application and, if applicable, how it's used.

## Help

This data group contains text to explain the **Status** section of the [Basic](#basic) data group in more detail, and provide some insight into how to use this information. The text can consist only of a paragraph of text, or can include individual definitions for some terms, for example, explaining the meaning of the phrase "self-references", that might be used in other data groups.

## Advanced

This data group contains any of the following entries:

* Instance ID - the unique ID associated with this object in this snapshot.
* Flags - displays a list of active Flags on the object.
* HideFlags - displays a list of active HideFlags on the object.
* Native Address - the memory location where the native component of this object exists. Only visible on objects that use native memory.
* Managed Address - the memory location where the managed component of this object exists. Only visible on objects that use managed memory.

## Preview

This data group displays a preview of how some objects appear in the application or Editor. This group is only visible for the following object types:

* Shaders
* Meshes
* Textures
* Materials
* Audio clips

## Managed Fields

This data group displays a table containing any fields in managed memory that the selected object contains. Some entries may involved hierarchies; select the parent entry to expand or hide sub-entries. The table displays the folowing columns:

* Name - the name of the field.
* Value - the current value of the field when the snapshot was captured.
* Type - the data type of the field.
* Size - the amount of memory the field used when the snapshot was captured.
* Notes - any additional or supplementary information relevant to the field.

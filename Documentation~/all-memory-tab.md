# All Of Memory tab

The __All Of Memory__ tab is only visible in the __Single Snapshot__ mode. This tab displays a breakdown of all the memory in the snapshot that Unity tracks. The memory usage visualized in this tab usually contains large sections of memory that either Unity or the current platform manages. Use this tab to see how much of your application's memory use isn't related to Unity objects, or to identify memory problems in your application that aren't visible in the [Unity objects tab](unity-objects-tab.md).

![The All Of Memory tab](images/all-of-memory-tab.png)
<br/>The All Of Memory tab

The table in this tab displays the proportion of tracked memory that each entry uses. By default, the rows in the table are collapsed. Select the arrow icon in the description column of any row to expand it and see the child members of that row. Select the arrow icon in any expanded row to collapse it.

The __All Of Memory__ tab splits tracked memory into four different top-level categories. The following table describes each category:

|__Category__|__Description__|
|:---|:---|
|__Scripting Memory__| Displays the memory usage of your scripts or C# plug-ins in your project. Expand the __Managed Objects__ subgroup to see the memory use of individual data types in scripts throughout your project. This could, for example, help you to identify any data structures in your scripts that might need optimization.|
|__Native Memory__| Displays all memory that Unity needs to run the Editor or related background processes. This includes all native C++ code and memory by native objects, such as GameObjects you use in a Scene. Expand this group to see the different kinds of native memory that Unity tracks.</br></br>The __Unity Objects__ subgroup displays memory that any Unity object in your application, such as a Shader or Texture2D, uses. Use this information to find areas where you could optimize memory use; you can then find these objects in the [Unity Objects tab](unity-objects-tab.md) to inspect them in more detail.</br></br>The __Unity Subsystems__ subgroup displays memory that installed modules or systems use. You can find which modules use the most memory and, if any aren't used, uninstall them to reduce how much memory your application needs.|
|__Executables And Dlls__| Displays the memory of other plug-ins or executable scripts in your application.|
|__Graphics Memory__| Displays how much memory the GPU uses to store objects for rendering. For example, if the CPU passes a Texture2D to the GPU so the GPU can access it for rendering, then the amount of memory the GPU uses to store that Texture2D is displayed in this group.|

The __Native Memory__ and __Scripting Memory__ groups have a __Reserved__ subgroup which contains memory that Unity needs to run the Editor or other background processes. For more information on how to adjust how Unity reserves memory for this purpose, see [Memory allocator customization](https://docs.unity3d.com/Manual/memory-allocator-customization.html).

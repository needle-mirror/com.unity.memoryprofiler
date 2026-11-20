# Main panel reference

The main panel of the Memory Profiler window displays various data visualizations about the selected memory [snapshot](snapshots.md) and is divided into the following tabs:

- [**Summary** tab](#summary-tab): Provides an overview of how the memory in your application is distributed.
- [**Unity Objects** tab](#unity-objects-tab): Provides a breakdown of how much memory each type of Unity object in the snapshot uses.
- [**All Of Memory** tab](#all-of-memory-tab): Provides a breakdown of all the tracked memory in the snapshot, not limited to Unity objects.
- [**Memory Map** tab](#memory-map-tab): A breakdown of the allocated memory in the snapshot as it is laid out in the virtual address space.
- [**Roots & Impact** tab](roots-and-impact.md): Attributes the memory in the snapshot to the roots that keep it allocated, and shows the impact each root has on memory.

![The main panel in the Memory Profiler window, with the Summary tab open displaying some data.](images/summary-tab.png)<br/>_The main panel in the Memory Profiler window, with the Summary tab open._

## Summary tab

The Summary tab displays general information about the state of memory in the selected snapshot or snapshots. You can investigate the information the Summary tab displays in more detail in the other available tabs.

![The Summary tab](images/summary-tab.png)</br>_The Summary tab._

The top of the tab sometimes displays contextual warnings or additional useful information about the snapshots, such as potential issues with your snapshot and insight about how to understand an Editor capture.

The following table describes the sections available in the Summary tab:

| **Section** | **Description** |
| :-- | :-- |
| **Memory Usage On Device** | The entire application's RAM footprint on the device. This is the memory allocated as part of the application's process at the time of the capture, broken down into how much of it is resident (mapped to physical memory), and how much is only allocated in memory. It includes memory tracked by Unity's memory manager and any untracked memory. |
| **Allocated Memory Distribution** | Displays how the allocated memory is distributed across different memory categories. |
| **Managed Heap Utilization** | Displays a breakdown of the memory that Unity's scripting virtual machine (VM) manages, which includes managed heap memory used for managed objects, empty heap space that might've previously been used by objects or been reserved during a heap expansion, and memory used by a virtual machine itself. |
| **Top Unity Objects Categories** | Displays which types of Unity Objects use the most memory in the snapshot. |

<!-- prettier-ignore -->
> [!TIP]
> Select any of the entries in the charts to display further information about the categories in the [Selection Details panel](selection-details-component.md).
> Hover over any of the bars in the tab to highlight the corresponding label, and vice versa. For the __Allocated Memory Distribution__ metric, the Memory Profiler displays how much of each category is resident memory or allocated, if that information is available. Continue hovering your cursor over the bar or label to display a tooltip that displays how much that element contributes to the total, as a percentage.

# Unity Objects tab

The Unity Objects tab displays any Unity objects that allocated memory, how much native, graphics, and managed memory each object uses, and the combined total. Use this information to identify areas where you can remove duplicate memory entries or to find which objects use the most memory.

![The Unity Objects tab](images/unity-objects-tab.png)</br>_The Unity Objects tab._

<!-- prettier-ignore -->
> [!NOTE]
> The search bar can only search for the text displayed in the Description column.

### Memory mode

Use the dropdown menu at the top of the tab to choose what memory usage to display. Choose from:

| **Option** | **Description** |
| --- | --- |
| **Allocated Memory** | Displays only allocated memory. **% Impact** is calculated against the allocated memory in the table. |
| **Resident Memory on Device** | Displays only resident memory. **% Impact** is calculated against the resident memory in the table. |
| **Allocated and Resident Memory on Device** | Displays allocated and resident memory. **% Impact** is calculated against the allocated memory in the table and highlights the portion of allocated memory that is resident. |

For more information about resident memory refer to [Memory usage on device](memory-on-device.md).

### Table information

The following columns are available in the Unity Objects table:

| **Column** | **Description** |
| :-- | :-- |
| **Description** | The name of each individual object or group of objects. Can't be disabled. |
| **Allocated Size** | The amount of memory the object or group of objects allocated, in bytes. |
| **Resident Size** | The amount of memory of the object or group of objects that is resident, in bytes. |
| **% Impact** | The amount of memory the object or group of objects uses as a percentage of the **Allocated Memory In Table** value, or the **Resident Memory In Table** if the table is in **Resident Memory on Device** mode. All measurement bars, including the **Allocated Memory In Table**/**Resident Memory In Table** bar, adjust dynamically based on the search filter applied to the table. |
| **Native Size** | The amount of native memory which makes up the **Allocated Size** value. |
| **Managed Size** | The amount of managed memory which makes up the **Allocated Size** value. |
| **Graphics Size** | The amount of graphics memory which makes up the **Allocated Size** value. |

## Display options

Use the checkboxes at the bottom of the window to change how the data is displayed:

| **Option** | **Description** |
| --- | --- |
| **Flatten Hierarchy** | Expands all objects into single entries instead of collapsible groups. |
| **Show Potential Duplicates Only** | Displays only instances where objects might be separate instances of the same object. When you enable this toggle, the Memory Profiler window groups any objects in the table with the same name, size, and type together. |

# All Of Memory tab

The **All Of Memory** tab displays a breakdown of all the memory in the selected snapshot.

The memory usage displayed in this tab contains large sections of memory allocated from the following places:

- Through Unity and its graphics API integration.
- Direct from the current platform's operating system. This type of allocation bypasses Unity's memory manager, making it **Untracked**. Memory can be directly allocated in this way through native plug-in code, or with C# methods such as `Marshall.AllocHGlobal`.

Use this tab to understand how much of your application's memory use isn't related to Unity objects, or to identify memory problems in your application that aren't visible in the [Unity objects tab](#unity-objects-tab).

![The All Of Memory tab](images/all-of-memory-tab.png)<br/>_The All Of Memory tab._

### Memory mode

Use the dropdown menu at the top of the tab to choose what memory usage to display. Choose from:

| **Option** | **Description** |
| --- | --- |
| **Allocated Memory** | Displays only allocated memory. **% Impact** is calculated against the allocated memory in the table. |
| **Resident Memory on Device** | Displays only resident memory. **% Impact** is calculated against the resident memory in the table. |
| **Allocated and Resident Memory on Device** | Displays allocated and resident memory. **% Impact** is calculated against the allocated memory in the table. |

### Table information

The table in this tab displays the proportion of tracked memory that each entry uses. Select the arrow icon in the description column of any row to expand it and display the child members of that row.

Tracked memory is divided into the following different top-level categories:

| **Category** | **Description** |
| :-- | :-- |
| **Managed** | Displays all VM and [managed heap memory allocations](xref:um-performance-managed-memory-introduction). The reserved amount in this category might be quickly reused if needed and if any new allocations fit within one of the free spaces. For more information, refer to [Managed memory](xref:performance-managed-memory). |
| **Native** | Displays all memory that Unity allocated internally for different native objects, such as GameObjects and their components, assets and managers, native allocations including NativeArrays and other native containers, and the CPU side of graphics asset memory. This doesn't include graphics, which is in a separate category. Expand this group to display the different kinds of native memory that Unity tracks: <ul><li>**Unity Objects**: Displays memory that any Unity object in your application, such as a Shader or Texture2D, uses.</li><li>**Unity Subsystems**: Displays memory that different Unity modules or systems, and native plug-ins that allocate their memory through the [Memory Manager API for low-level native plug-ins](xref:um-low-level-native-plugin-memory-manager-api), use. You can find which modules use the most memory and, if any aren't used, [deactivate them in the Package Manager](xref:um-upm-ui-disable) or remove the plug-ins from your project to reduce how much memory your application needs.</li></ul> |
| **Executables & Mapped** | Displays the memory taken up by the build code of the application, including all shared libraries and assemblies, managed and native. This value isn't reported consistently on all platforms. You can reduce this memory usage by using a higher code stripping level and by reducing your dependencies on different modules and libraries. |
| **Graphics (Estimated)** | Displays an approximation of how much memory the graphics driver and the GPU uses to visualize your application. The information is based on the tracking of graphics resource allocations within Unity. For detailed information on how this metric tracks allocations, refer to [Graphics memory](memory-on-device.md#graphics-memory). |
| **Untracked** | Memory that the Memory Profiler can't account for, due to platform specific requirements, potential bugs, or other gaps in memory tracking. The size of Untracked memory is determined by analyzing all allocated regions of the process and subtracting it from the amount of memory known to Unity memory manager. To analyze this memory further, you need to use a platform specific profiler. If the table mode is not set to **Allocated Memory**, this category includes **Graphics (Estimated)**. |

The **Native** and **Managed** groups have a **Reserved** subgroup which contains memory that Unity allocated but doesn't use at the moment of capture. For more information on how to adjust how Unity reserves memory for this purpose, refer to[Memory allocator customization](xref:um-memory-allocator-customization).

For more information on Resident Memory refer to [Memory usage on device](memory-on-device.md).

## Memory Map tab

The Memory Map tab displays the allocated and resident size of all memory allocations in the snapshot. Select a memory address to learn more about the memory stored in this location.

![The Memory Map tab](images/memory-map-tab.png)</br>_The Memory Map tab._

To use the Memory Map tab, enable it in the Preferences window:

1. Go to **Edit** > **Preferences**.
2. Select **Analysis** > **Memory Profiler**.
3. Enable the **Show Memory Map view** checkbox.

The following columns are available in the Memory Map table:

| **Category** | **Description** |
| :-- | :-- |
| **Address** | The address of a specific chunk of memory. |
| **Type** | The type of memory allocation. |
| **Allocated Size** | The amount of memory allocated to the process at this address. |
| **Resident Size** | The amount of allocated memory at this address that exists in the physical memory (RAM) of this device. |
| **Name** | The name of the object, the root of an allocation, the allocator name, a mapped file's name and path or an identifier supplied by the operating system for each top level entry. |

## Additional resources

- [Snapshots panel reference](snapshots-component.md)
- [References panel reference](references-component.md)
- [Selection Details panel reference](selection-details-component.md)

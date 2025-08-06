# Memory usage on devices

The Memory Profiler collects data for resident memory usage, and allocated memory:

* **Allocated memory**: The total amount of memory allocated to a process. The resource might be in physical memory (in that case, it is called Resident) or swapped out to a secondary storage, such as a page file on a disk.
* **Resident memory**: A portion of the allocated memory of the process that's currently in the physical memory (RAM) of the device. It indicates how much memory demand there is on the target device.

![Diagram of memory layout.](images/memory-on-device-all-memory.png)<br/>_Diagram of memory layout._

A **Resident Memory** metric shown on the Summary tab is only available for projects made with Unity 2022.2 or later and more detailed information on other pages is only available in projects made with 2023.1 or later. Older projects only have the **Allocated Memory** metric available. To view resident memory data on older projects, use platform-specific profiling tools.

Additionally, all Allocated Memory on the PS4, PS5, Nintendo Switch, and WebGL platforms is Resident Memory On Device.

## Resident memory information in the Memory Profiler

The tabs in the [Main panel](main-component.md) of the Memory Profiler window provide data for resident memory usage, and allocated memory.

### Summary tab resident memory

The [Summary tab](main-component.md#summary-tab) provides a general overview of the impact on physical memory with the __Total Resident on Device__ metric. If your application needs to run on a platform with limited memory, the __Total Resident on Device__ metric can help you review low-memory warnings and out-of-memory evictions. It's best practice to not use over 70% of the total physical memory available on a device.

![The Summary tab](images/memory-on-device-summary.png)

### All of Memory and Unity Objects tab resident memory

For detailed analysis, use the [Unity Objects](main-component.md#unity-objects-tab), and [All Of Memory](main-component.md#all-of-memory-tab) tabs. Select __Resident on Device__ or __Allocated and Resident on Device__ from the dropdown menu and sort by __Resident size__ to get a list of objects that contribute most to the total physical memory used.

> [!NOTE]
> These options are only available for captures of projects made with Unity 2023.1 and later.

![All of Memory view](images/memory-on-device-all-of-memory.png)

## Memory types

The Memory Profiler tracks different types of memory as follows:


### Managed memory

Managed memory is primarily resident memory because the [managed heap](xref:um-performance-managed-memory) and the [garbage collector](xref:um-performance-garbage-collector) accesses objects regularly.

### Graphics memory

The __Graphics memory (estimated)__ value is an estimation because for most platforms, Unity doesn't have access to information on the exact usage of graphics resources. Unity estimates the size based on available information such as width, height, depth, and pixel format. It also means that information about graphics resources' residency status isn't available. For usability reasons, all graphics objects are displayed only in the **Allocated** view mode.

The information is based on the tracking of graphics resource allocations within Unity. This includes RenderTextures, textures, meshes, animations and other graphics buffers which are allocated by Unity or Scripting API. Not all these objects' memory is represented in this category. For example, Read/Write enabled graphics assets need to store a copy in CPU-accessible memory, which doubles their total memory usage. Also, not all memory from these type of objects is in GPU memory.

When Unity requests the driver to allocate a resource, Unity's memory manager creates a book-keeping unit associated with the resource ID and a size calculated based on the parameters specified for the resource and how much memory would be expected to be needed for by the specific driver to handle that resource.

On systems with separated graphics memory (VRAM) this is the RAM-side allocation needed to re-upload the resource to the GPU if for example, an app context switch forced the GPU to flush the VRAM out. The VRAM usage is likely to be similar, but not guaranteed. To get information on VRAM usage and memory bandwidth related performance issues, use a GPU manufacturer specific profiler.

Because the address of the memory isn't known, the Memory Profiler can't determine if graphics memory is resident or not and instead lists it as Untracked in all table modes except for __Allocated Memory__. Some graphics drivers might also keep previously used memory in reserve for future use. If there is no __Reserved__ group under Graphics, it is likely that this reserved amount is __Untracked__ for this driver or platform combination.

### Untracked memory

__Untracked__ is all memory reported by the operating system as allocated by the application but which lacks reliable information on the source of the allocation. This memory might be attributed to areas such as native plug-ins, OS libraries, or thread stacks. On some platforms, and in projects made with Unity 2023.1 and newer, the Memory Profiler provides additional insights into what might have allocated that memory in the group breakdown, with the names for these allocations coming directly from the OS.

To investigate this memory usage further, use native platform specific profilers and compare the names used by these tools to those shown in Unity's Memory Profiler. To get a better understanding of what the different names mean, review the documentation for the specific platform and its profiling tools.

### Native memory

Native memory contains all Unity non-managed allocations used by objects, and includes the __Reserved memory__ metric. Reserved memory is memory allocated by Unity's memory manager but not used by any Unity object during capture. Reserved memory can be resident, which means that there might have been an object that was recently deleted.

To access additional information about reserved memory:

* Go to the Memory Profiler settings (**Unity** > **Settings** > **Analysis** > **Memory Profiler**)
* Enable **Show reserved memory breakdown**

By default, this setting is disabled, because the Reserved breakdown doesn’t always contain enough actionable information. For more information about the Unity memory manager and allocation strategies, refer to [Customize allocators documentation](xref:um-memory-allocator-customization).

### Platform-specific memory

On some platforms, the Memory Profiler displays additional platform-specific groups if they're a significant size, such as Android Runtime on Android.

Android Runtime has the following considerations:

* On some versions, Android Runtime pre-allocates a significant amount of memory but never uses it. In that case, allocated memory doesn't add to the application memory footprint and only the resident part of it needs to be considered.
* If the Android Runtime resident memory is taking up a significant amount of the application memory footprint, use the Android Studio profiler to analyze allocations done in Java.
* Although Android doesn't have a page file or memory compression by default, the Linux kernel allows applications to overcommit and allocate more memory than is physically available.
* When capturing, make sure you understand the device you’re using. Some vendors supply the Android Linux kernel with memory compression (zRAM) or vendor-custom page swap file tools.

## Additional resources

* [Main panel reference](main-component.md)
* [Customize allocators documentation](xref:um-memory-allocator-customization)
* [Garbage collector documentation](xref:um-performance-garbage-collector)

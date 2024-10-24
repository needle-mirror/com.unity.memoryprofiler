# Memory Usage On Device

The [Summary](summary-tab.md), [Unity Objects](unity-objects-tab.md), and [All Of Memory](all-memory-tab.md) views provide data for resident memory usage, and allocated memory. Resident memory of a process is a fraction of the allocated memory of the process that's currently in physical memory.

![Resident Memory](images/memory-on-device-all-memory.png)<br/>_Diagram of memory layout._

The Resident Memory metric is only available for projects made with Unity 2022.2 or later. Older projects only have the Allocated Memory metric available. To view Resident Memory data on older projects, use platform-specific profiling tools.

Additionally, all Allocated Memory on the PS4, PS5, Switch, and WebGL platforms is Resident Memory On Device.

## Summary view

![The Summary view](images/memory-on-device-summary.png)

The Summary view provides a general overview of the impact on physical memory with the __Total Resident on Device__ metric. If your application needs to run on a platform with limited memory, the Total Resident on Device metric can help your review low-memory warnings and out-of-memory evictions. It's best practice to not use over 70% of the total physical memory available on a device.

## All of Memory and Unity Objects view

For detailed analysis, you can use [Unity Objects](unity-objects-tab.md), and [All Of Memory](all-memory-tab.md) views. Select __Resident on Device__ or __Allocated and Resident on Device__ from the dropdown menu and sort by __Resident size__ to get a list of objects that contribute most to the total physical memory used. These options are only available for captures of projects made with Unity 2023.1 and later.

![All of Memory view](images/memory-on-device-all-of-memory.png)

## Resident memory best practices

When analyzing resident memory usage, remember:

* __Managed memory__ is primarily resident because the [managed heap](xref:um-performance-managed-memory) and the [garbage collector](xref:um-performance-garbage-collector) accesses objects regularly.
* The __Graphics memory (estimated)__ value is an estimation because for most platforms, Unity doesn't have access to information on the exact usage of graphics resources. Unity estimates the size based on available information such as width, height, depth, and pixel format. It also means that information about graphics resources’ residency status isn't available. For usability reasons, all graphics objects are displayed only in the Allocated view mode.
* __Untracked__ is all memory reported by the operating system as allocated by the application but which lacks solid information on the source of the allocation. This memory might be attributed to areas such as native plug-ins, OS libraries, or thread stacks. On some platforms, and in projects made with Unity 2023.1 and newer, the Memory Profiler provides additional insights into what might have allocated that memory in the group breakdown.

### Native memory

__Native memory__ contains all Unity non-managed allocations used by objects, and includes the __Reserved memory__ metric. Reserved memory is memory allocated by the Unity Memory Manager but not used by any Unity object during capture. Reserved memory can be resident, which means that there might have been an object that was recently deleted.

To access additional information about reserved memory, go to the Memory Profiler settings and enabling the **Show reserved memory breakdown** setting. By default, this is disabled, because then Reserved breakdown doesn’t always contain enough actionable information and requires a deep understanding of how Unity Memory Manager works.

For more information about the Unity Memory Manager and allocation strategies, refer to [Customize allocators documentation](https://docs.unity3d.com/Manual/memory-allocator-customization.html).

### Platform-specific memory

On some platforms, the Memory Profiler displays additional platform-specific groups if they’re a significant size, such as Android Runtime on Android.

Here are some notes on Android Runtime:
* On some versions, Android Runtime tends to pre-allocate a significant amount of memory but never uses it. In that case, allocated memory doesn’t add to the application memory footprint and only the resident part of it needs to be considered.
* If the Android Runtime resident part is taking up a significant amount of the application memory footprint, use the Android Studio profiler to analyze allocations done in Java.
* Although Android doesn’t have a page file or memory compression by default, the Linux kernel allows applications to overcommit and allocate more memory than is physically available.
* When capturing, make sure you understand the device you’re using. Some vendors supply the Android Linux kernel with memory compression (zRAM) or vendor-custom page swap file tools.

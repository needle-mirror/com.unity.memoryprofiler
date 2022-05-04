# Memory Profiler

The Memory Profiler is a tool you can use to identify areas in your Unity Project, and the Unity Editor, where you can reduce memory usage. You can use it to capture, inspect, and compare memory snapshots. A memory snapshot is a record of how the memory in your application is organized at the point in a frame when the snapshot was taken.

When you record some data with the Memory Profiler, you can access the data through the [Memory Profiler window](memory-profiler-window) in the Unity Editor. This window provides an overview of native and managed memory allocations. Use this data to profile your application and to detect memory leaks and fragmentation.

This package is separate to the built-in [Memory Profiler module](https://docs.unity3d.com/Manual/ProfilerMemory.html), and you can use both tools to profile your application. The Memory Profiler package is designed to provide more detailed information about your application's memory allocations.

Unity recommends that you become familiar with the built-in Profiler before you use this package. For more information, see [Profiler overview](https://docs.unity3d.com/Manual/Profiler.html).

## Preview package

This package is available as a preview, so it is not ready for production use. The features and documentation in this package might change before it is verified for release.

## Installing Memory Profiler

To install this package, follow the instructions in the [Package Manager documentation](https://docs.unity3d.com/Manual/upm-ui.html).
While the package is in preview, it doesn't appear in your Package Manager window by default.

In Unity version 2018.4 or 2019.4, open the __Package Manager__ window. Select the __Advanced__ drop-down menu and enable __Show preview packages__.

In Unity version 2020.3, go to __Project Settings__ > __Package Manager__ > __Advanced Settings__  and enable the __Preview Packages__ property.

In Unity version 2021.2 or later, you can select the `+` button in the Package Manager window, select the option __Add By Name__ and enter `com.unity.memoryprofiler` to install the package. Or for any Unity version newer than 2021.2.0a5, you can click [this link to install it by name](com.unity3d.kharma:upmpackage/com.unity.memoryprofiler).

## Requirements

This version of the Memory Profiler is compatible with the following versions of the Unity Editor:

| Unity Version | Package Version | Minimum Unity Version | Recommended Unity Version |
|----------------|---------|------------|---------------------|
| 2021.2 or newer| 0.6.x   | 2021.1.0a1 | 2020.2.0a12 or newer|
| 2020.x         | 0.6.x   | 2020.1.0a1 | 2020.3.12f1 or newer|
| 2019.x         | 0.6.x   | 2019.3.0f1 | 2019.4.29f1 or newer|
| 2018.x         | 0.2.x   | 2018.3.3f1 | 2018.4.14f1 or newer|

When you install the Memory Profiler package, Unity automatically installs the [Editor Coroutines](https://docs.unity3d.com/Packages/com.unity.editorcoroutines@0.0/manual/index.html) package as a dependency.

## Known limitations

The Memory Profiler package has the following known limitations:

* Only Development Builds, and the Editor can use the Memory Profiler. This limitation means that you can’t profile release builds. The Unity Profiler has the same limitation. For more information on profiling applications, see the User Manual documentation on [Profiling your application](https://docs.unity3d.com/Manual/profiler-profiling-applications.html).
* The memory capture allocates memory, so in some cases the system might run out of memory and crash the application.
* When you take a capture, it might temporarily freeze the capture target (the Editor or remote application). The length of this freeze depends on the complexity and memory usage of the application.
* The Memory profiler does not have support for single threaded platforms, such as WebGL.
* If you want to take a screenshot to accompany the memory snapshot, this only works with Players built with a Unity 2019.3 or newer. For more details, see the [Troubleshooting](tips-and-troubleshooting.md#define-snapshot-metadata) area of this documentation.
* Some of the data in the binary copy of the managed heap that is included in the snapshot file is not displayed in the UI. See the next section for more information.
* Snapshots taken from Unity patch versions earlier than 2021.2.0a12, 2021.1.9f1, 2020.3.12f1, and 2019.4.29f1 do not contain data improvements that allow you to make full use of the workflows added in package versions 0.4.x and higher. These improvements include:
  * an improved high-level overview of memory usage.
  * improved snapshot grouping and information in the snapshot list.
  * correct reference tracking between `UnityEngine.Objects`.
* The Memory Profiler does not report Managed Virtual Machine memory for the [IL2CPP scripting backend](https://docs.unity3d.com/Manual/IL2CPP.html). This means that the Memory Profiler Package UI cannot show this memory usage and includes it in the `Untracked Memory` category.
* The `System Used Memory` ProfilerCounter is not yet supported on all Platforms and Unity versions. The high-level Memory Breakdowns UI uses this counter to show the total amount of memory the App is using, according to the OS. When the Platform and Unity version do not implement that counter, it will show up with an `Unknown` size in the `Untracked Memory` category.
* There might be discrepancies between what the Memory Profiler reports and what native platform profilers report on some platforms or graphics APIs. This can happen because Unity's Native Memory usage is handled through the Memory Manager which handles all Native CPU Allocations made by Unity code. Native Object types (types that inherit from `UnityEngine.Object`) implement a callback to report their memory usage. Each callback implementation builds on the Native CPU Allocations for the specific objects, including the GPU Memory size which consists of calculated estimations. This can cause flaws in the implementation of the callbacks or the estimation calculations. If this happens, please report a bug.
* Native Plugin memory usage appears in the `Untracked Memory` category. This is because Plugins cannot allocate their Native memory through the Memory Manager, or report their Native memory usage to the Memory Manager. This means that the Memory Manager and Memory Profiler cannot get any insights on Native Plugin memory usage.
* When you use static methods on the  [GCHandle](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.gchandle), apply a `.Free()` call to any `GCHandle` you create when your code no longer needs it. Otherwise, Unity cannot release this object from memory. If the `GCHandle` is not stored in a member variable of any `System.Object` on the Heap, the Memory Profiler can not track this `GCHandle` as a reference. If no other member fields hold a reference to the targeted `System.Object`'s Label, it will have Reference Count of 0 and it will not be collected by the Garbage Collector.

* There are the following gaps in memory tracking that contribute to the amount of `Untracked Memory`: 
  * Native Plugin allocations
  * The size of Executable and DLLs on some platforms
  * Virtual Machine memory used by [IL2CPP](https://docs.unity3d.com/Manual/IL2CPP.html)
  * Application Stack memory
  * Memory allocated using [Marshal.AllocHGlobal](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.marshal.allochglobal)

### Known Issues

* The Memory Profiler reports the wrong Total Committed Memory value for Android devices on Unity versions before 2021.1.0a1 or before 2020.2.0b8. It reports the total RAM that the device has, not the total amount of RAM used. [(Case 1267773)](https://issuetracker.unity3d.com/product/unity/issues/guid/1267773/)

### Data concerns when sharing snapshots

The memory snapshots you take with the Memory Profiler UI or the [Memory Profiler API](https://docs.unity3d.com/2018.3/Documentation/ScriptReference/Profiling.Memory.Experimental.MemoryProfiler.html) contain the entire contents of the managed heap of the Player or Editor instance you are capturing.

You can see most of the data through the Memory Profiler UI, with the exception of managed allocations that do not have a garbage collection handle. These allocations might be related to Mono type data, leaked managed data, or allocations that the garbage collector has already collected and released but the memory section they were located in hasn’t been overwritten with new data. The latter happens because garbage-collected memory is not "stomped" for performance reasons.

The kind of data that you can explore in areas such as the __All Managed Objects__ view gives you an idea of what data could be included in the memory snapshot. The Memory profiler includes any object instance of a class, all fields on that object, as well as the class’ statics excluding literals such as const values.

The Memory Profiler stores fields depending on the data type:

* It stores [value types](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/value-types) by their value
* It stores [reference types](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/reference-types) as pointer addresses. The UI resolves any pointer address as a link to the object the address points to.

For example, string type fields might indicate via their name what the string they point to means. So searching for a field named "Password", "Credentials" or "Token" might identify strings with sensitive data. If Unity has garbage collected the object that points to the string, or even the string itself, the data might still be there. However, it is no longer easily identifiable, unless you have a rough idea of what you're looking for or part of the string value to search for.

**Note:** The previous section only mentions string data as potentially problematic, but this issue isn't exclusive to strings and might happen in other forms as well, such as field or type names.

One way to safeguard against accidentally disclosing personally-identifying information, credentials or similar confidential data when you share snapshot files outside of your teams, is to put that data into constant fields. Constant fields bake that data into the binary executable, which the Memory Profiler does not capture. However, a binary executable might be de-compiled by users and expose the data that way.

You can only take a memory snapshot in development Players, so these fields might be non-const in release builds, which will make it more difficult to get to the data, but not entirely impossible.

If you have any further questions regarding this topic, use the [Unity Forum thread](https://forum.unity.com/threads/data-concerns-when-sharing-snapshots.718916/) to discuss this issue.

## Package contents

The following table indicates the root folders in the package where you can find useful resources:

|Location|Description|
|:---|:---|
|`Documentation~`|Contains the documentation for the package.|
|`Tests`|Contains the unit tests for the package.|

## Document revision history

|Date|Reason|
|:---|:---|
|Aug 31, 2021|Updated the [Installing Memory Profiler](#installing-memory-profiler) section to fit the installation process through the Package Manager in each supported Unity version.<br/>Updated the [Requirements](#requirements) section to clarify Unity Version compatibility for this Package.<br/>Updated the [Known limitations Section](#known-limitations) to address the current gap in Tracked Memory and which data improvements come with the Unity versions supported by version 0.4.0 of this package. |
|Dec 03, 2019|Fixed Table of Contents and amended minor spelling and style mistakes.|
|Aug 01, 2019|Updated documentation. Updated the [Known limitations Section](#known-limitations) with regards to screenshots on snapshots. Added [Data concerns section](#data-concerns-when-sharing-snapshots). Matches package version 0.1.0-preview.7|
|Dec 12, 2018|Updated documentation. Matches package version 0.1.0-preview.2|
|Nov 15, 2018|Documentation created. Matches package version 0.1.0-preview.2|

# Memory Profiler

The Memory Profiler is a tool you can use to identify areas in your Unity Project, and the Unity Editor, where you can reduce memory usage. You can use it to capture, inspect, and compare memory snapshots.

When you install the Memory Profiler, you can access its data through the Memory Profiler [window in the Unity Editor](memory-profiler-window). It gives you an overview of native and managed memory allocations, and can help you detect memory leaks and fragmentation.

You can use it to profile the memory of any Unity Project. The Memory Profiler is separate to the in-built [Unity Profiler](https://docs.unity3d.com/Manual/Profiler.html), and you can use both tools to profile your application. The Memory Profiler package is designed to give you more detailed information about the memory allocations in your application.

## Preview package

This package is available as a preview, so it is not ready for production use. The features and documentation in this package might change before it is verified for release.

## Installing Memory Profiler

To install this package, follow the instructions in the [Package Manager documentation](https://docs.unity3d.com/Manual/upm-ui.html). Because it's a preview package, in the Package Manager window, you must enable  __Preview Packages__. Select the __Advanced__ drop-down at the top of the window, then enable __Show preview packages__.

## Requirements
This version of the Memory Profiler is compatible with the following versions of the Unity Editor:

* 2018.3 and later.

When you install the Memory Profiler package it automatically installs the [Editor Coroutines](https://docs.unity3d.com/Packages/com.unity.editorcoroutines@0.0/manual/index.html) package as a dependency.

## Known limitations
The Memory Profiler package has the following known limitations:

* Only Development Builds, and the Editor can use the Memory Profiler. This limitation means that you can’t profile release builds. The Unity Profiler has the same limitation. For more information on profiling applications, see the User Manual documentation on [Profiling your application](https://docs.unity3d.com/Manual/profiler-profiling-applications.html).
* The memory capture allocates memory, so in some cases the system might run out of memory and crash the application.
* When you take a capture, it might temporarily freeze the capture target (the Editor or remote application). The length of this freeze depends on the complexity and memory usage of the application.
* The Memory profiler does not have support for single threaded platforms, such as WebGL.
* If you want to take a screenshot to accompany the memory snapshot, this only works with Players built with a Unity 2019.3 or newer. For more details, see the [Troubleshooting](tips-and-troubleshooting.md#define-snapshot-metadata) area of this documentation.
* Some of the data in the binary copy of the managed heap that is included in the snapshot file is not displayed in the UI. See the next section for more information.

### Data concerns when sharing snapshots
The memory snapshots you take with the Memory Profiler UI or the [Memory Profiler API](https://docs.unity3d.com/2018.3/Documentation/ScriptReference/Profiling.Memory.Experimental.MemoryProfiler.html) contain the entire contents of the managed heap of the Player or Editor instance you are capturing.

You can see most of the data through the Memory Profiler UI, with the exception of managed allocations that do not have a garbage collection handle. These allocations might be related to Mono type data, leaked managed data, or allocations that the garbage collector has already collected and released but the memory section they were located in hasn’t been overwritten with new data. The latter happens because garbage-collected memory is not "stomped" for performance reasons.

The kind of data that you can explore in areas such as the __All Managed Objects__ view gives you an idea of what data could be included in the memory snapshot. The Memory profiler includes any object instance of a class, all fields on that object, as well as the class’ statics excluding literals such as const values.

The Memory Profiler stores fields depending on the data type: 
* It stores [Value types](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/value-types) by their value
* It stores [reference types](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/reference-types) as pointer addresses. The UI resolves any pointer address as a link to the object the address points to.

For example, string type fields might indicate via their name what the string they point to means. So searching for a field named "Password", "Credentials" or "Token" might identify strings with sensitive data. However, if Unity has garbage collected the object that points to the string, or even the string itself, the data might still be there. However, it is no longer easily identifiable, unless you have a rough idea of what you're looking for or part of the string value to search for.

**Note:** The previous section only mentions string data as potentially problematic, however, this issue is not exclusive to strings and might happen in other forms as well, such as field or type names.

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
|Dec 03, 2019| Fixed Table of Contents and amended minor spelling and style mistakes.|
|Aug 01, 2019|Updated documentation. Updated the [Known limitations Section](#known-limitations) with regards to screenshots on snapshots. Added [Data concerns section](#data-concerns-when-sharing-snapshots). Matches package version 0.1.0-preview.7|
|Dec 12, 2018|Updated documentation. Matches package version 0.1.0-preview.2|
|Nov 15, 2018|Documentation created. Matches package version 0.1.0-preview.2|

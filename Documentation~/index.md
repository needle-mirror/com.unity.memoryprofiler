# About Memory Profiler

Use the Memory Profiler package to identify potential areas in your Unity project (and the Unity Editor itself) where you can reduce [memory usage](https://github.com/google/cadvisor/issues/913#issuecomment-150663233). For example, use the Memory Profiler to capture, inspect, and compare [memory snapshots](https://en.wikipedia.org/wiki/Snapshot_(computer_storage)).

The Memory Profiler package has a [window](memory-profiler-window.md) in the Editor with an overview of native and managed memory allocations and can help you detect [memory leaks](https://en.wikipedia.org/wiki/Memory_leak) and [fragmentation](https://en.wikipedia.org/wiki/Fragmentation_(computing)).

The Memory Profiler package creates a unified solution allowing you to profile both small projects on mobile devices and big AAA projects on high-end machines.

You can also import snapshots taken from the [Bitbucket Profiler](https://bitbucket.org/Unity-Technologies/memoryprofiler) and use them within the Memory Profiler.

## Memory Profiler vs. Profiler

The Memory Profiler package is entirely separate from the inbuilt Unity [Profiler](https://docs.unity3d.com/Manual/Profiler.html), even though they share some terminology.

## Who can use the Memory Profiler?

The Memory Profiler package is mostly for advanced Unity users that desire to understand the memory used in their Project. However, no knowledge of memory profiling tools is required to make use of this package.

# Installing Memory Profiler

To install this package, follow the instructions in the [Package Manager documentation](https://docs.unity3d.com/Packages/com.unity.package-manager-ui@latest/index.html).

>**Note**: While this package is in preview, the Package Manager needs to be configured to show __Preview Packages__. (Under the __Advanced__ drop-down menu, enable __Show preview packages__.) Then search for the Memory Profiler package.

<a name="UsingPackageName"></a>

# Using Memory Profiler

To learn how to use the Memory Profiler package in your project, read the [manual](manual.md).

# Technical details
## Requirements
This version of the Memory Profiler is compatible with the following versions of the Unity Editor:

* 2018.3 and later.

> **Note**: When you install the Memory Profiler package it will automatically install the [Editor Coroutines](https://docs.unity3d.com/Packages/com.unity.editorcoroutines@0.0/manual/index.html) package as a dependency.

## Known limitations
Memory Profiler version 0.1.0-preview.2 includes the following known limitations:

* Only development builds, and the Editor can use the Memory Profiler. This limitation means that users can’t profile release builds. (This is the same limitation as Profiler.)
* The capture itself allocates memory so in some cases the system may run out of memory and crash the application.
* Taking a capture may temporarily freeze the capture target (the Editor or remote application). The length of this freeze depends on the complexity and memory usage of the application.
* Single threaded platforms, such as WebGL, are not supported.
* Taking a screenshot to accompany the memory snapshot only works with Players build with a Unity version of 2019.3 or newer.[More details](tips-and-troubleshooting.md#define-snapshot-metadata)
* Some data that is contained in the binary copy of the managed heap included in the snapshot file is currently not displayed in the UI. [More details below](#data-concerns-when-sharing-snapshots)

## Data concerns when sharing snapshots
The memory snapshots you take with the Memory Profiler UI or the [Memory Profiler API](https://docs.unity3d.com/2018.3/Documentation/ScriptReference/Profiling.Memory.Experimental.MemoryProfiler.html) contain the entire contents of the managed heap of the Player or Editor instance you are capturing.

You can see most of the data through the Memory Profiler UI in this package with the exception of managed allocations that do not have a garbage collection handle. These allocations might be related to Mono type data, leaked managed data, or allocations that the garbage collector has already collected and released but the memory section they were located in hasn’t been overwritten with new data. The latter happens because garbage-collected memory is not "stomped" for performance reasons.

The kind of data that you can explore in areas such as the All Managed Objects view gives you an idea of what data could be included in the memory snapshot. Any object instance of a class, all fields on that object, as well as the class’ statics excluding literals such as const values are included.

Fields are stored depending on the data type: [Value types](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/value-types) are stored by their value, and [reference types](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/reference-types) are stored as pointer addresses. The UI resolves any pointer address as a link to the object the address points to.

For example, string type fields might indicate via their name what the string they are pointing to means. So searching for a field named "Password", "Credentials" or "Token" could identify strings with sensitive data. However, if the object that points to the string, or even the string itself has been garbage collected, the data might still be there, but is no longer easily identifiable, unless you have a rough idea what you're looking for or part of the string value to search for.

Note: The above section only mentions string data as potentially problematic, however, this issue is not exclusive to strings and could come in other forms as well, including as field or type names.

One way to safeguard against accidentally disclosing personally-identifying information, credentials or similar confidential data when you share snapshot files outside of your teams, is to put that data into constant fields. Constant fields bake that data into the binary executable, which is not captured by the Memory Profiler. However, be aware that a binary executable might be de-compiled by users and expose the data that way.

Taking a memory snapshot is currently only possible in development Players, so these fields could be non-const in release builds, which will make it trickier to get to the data, albeit not entirely impossible.

If you have any further questions regarding this topic, please let us know through our [forum thread](https://forum.unity.com/threads/data-concerns-when-sharing-snapshots.718916/) on this issue.

## Package contents
The following table indicates the root folders in the package where you can find useful resources:

|Location|Description|
|---|---|
|`Documentation~`|Contains the documentation for the package.|
|`Tests`|Contains the unit tests for the package.|

## Document revision history
|Date|Reason|
|---|---|
|Aug 01, 2019|Updated documentation. Updated the [Known limitations Section](#known-limitations) with regards to screenshots on snapshots. Added [Data concerns section](#data-concerns-when-sharing-snapshots). Matches package version 0.1.0-preview.7|
|Dec 12, 2018|Updated documentation. Matches package version 0.1.0-preview.2|
|Nov 15, 2018|Documentation created. Matches package version 0.1.0-preview.2|

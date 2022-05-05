# Memory Profiler

The Memory Profiler is a tool you can use to identify areas in your Unity Project, and the Unity Editor, where you can reduce memory usage. You can use it to capture, inspect, and compare memory snapshots. A memory snapshot is a record of how the memory in your application is organized at the point in a frame when the snapshot was taken.

![](images/index-page-screenshot.png)
The Memory Profiler window

When you record some data with the Memory Profiler, you can access the data through the [Memory Profiler window](memory-profiler-window.md) in the Unity Editor. This window provides an overview of native and managed memory allocations. Use this data to profile your application and to detect memory leaks and fragmentation.

This package is separate to the built-in [Memory Profiler module](https://docs.unity3d.com/Manual/ProfilerMemory.html), and each is useful for different purposes. The Memory Profiler package is designed to provide more detailed information about your application's memory allocations.

## Installing Memory Profiler

To install this package, follow the instructions in the [Package Manager documentation](https://docs.unity3d.com/Manual/upm-ui.html).

In Unity version 2021.3 or later, select the `+` button in the Package Manager window, select the option __Add By Name__ and enter `com.unity.memoryprofiler` to install the package. For any Unity version newer than 2021.2.0a5, you can click [this link to install it by name](com.unity3d.kharma:upmpackage/com.unity.memoryprofiler).

## Requirements

This version of the Memory Profiler is compatible with the following versions of the Unity Editor:

| Unity Version | Package Version | Minimum Unity Version | Recommended Unity Version |
|----------------|---------|------------|---------------------|
| 2022.2 or newer| 1.0.x   | 2022.2.0a16 | 2022.2.0b1  or newer|
| 2021.x         | 0.7.x   | 2021.1.0a1 | 2021.3.3f1 or newer|
| 2020.x         | 0.7.x   | 2020.1.0a1 | 2020.3.35f1 or newer|
| 2019.x         | 0.7.x   | 2019.3.0f1 | 2019.4.29f1 or newer|

When you install the Memory Profiler package, Unity automatically installs the [Editor Coroutines](https://docs.unity3d.com/Packages/com.unity.editorcoroutines@0.0/manual/index.html) package as a dependency.

## Package contents

The following table indicates the root folders in the package where you can find useful resources:

|Location|Description|
|:---|:---|
|`Documentation~`|Contains the documentation for the package.|
|`Tests`|Contains the unit tests for the package.|

## Additional resources

* [Profiler overview](https://docs.unity3d.com/Manual/Profiler.html)
* [Ultimate guide to profiling Unity games](https://resources.unity.com/games/ultimate-guide-to-profiling-unity-games)

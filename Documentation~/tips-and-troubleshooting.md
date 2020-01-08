# Memory Profiler tips and troubleshooting

When you use the Memory Profiler package, you should be aware of the following:

## Ignore snapshot files

Add the .snap extension to your version control system’s [ignore file](https://www.atlassian.com/git/tutorials/saving-changes/gitignore) to avoid committing memory snapshot files to your repository. Memory snapshot files might take up large amounts of disk space.

## Define snapshot metadata

When you capture a snapshot, you can generate [MetaData](https://docs.unity3d.com/2018.3/Documentation/ScriptReference/Profiling.Memory.Experimental.MetaData.html) on the Player side. If the Player was built from a Project that has the Memory Profiler package installed, it generates some default metadata.

The default metadata consists of:
* [MetaData.content](https://docs.unity3d.com/2018.3/Documentation/ScriptReference/Profiling.Memory.Experimental.MetaData-content.html): Contains the Project's name, and the scripting version when you capture the Editor.
* [MetaData.platform](https://docs.unity3d.com/2018.3/Documentation/ScriptReference/Profiling.Memory.Experimental.MetaData-platform.html): Contains the [RuntimePlatform](https://docs.unity3d.com/ScriptReference/RuntimePlatform.html) of the Player or the Editor that you captured, and stores it as a string.
* [MetaData.screenshot](https://docs.unity3d.com/2018.3/Documentation/ScriptReference/Profiling.Memory.Experimental.MetaData-screenshot.html): A screenshot the Memory Profiler takes at the moment of the capture. Its size is under 480x240 pixels.

When you capture a snapshot, you should define some metadata on the Player side so you can get a good overview of the content of your snapshot. There are two ways to do so:

* When you don't have the Memory Profiler package in your Project, but want to add metadata to your snapshots, register a listener to [MemoryProfiler.createMetaData](https://docs.unity3d.com/2018.3/Documentation/ScriptReference/Profiling.Memory.Experimental.MemoryProfiler-createMetaData.html).
* If you have the package in your Project, keep the default data or write a metadata collector. For more information see __Add Player hook__ below.

## Add Player hook

To define custom metadata in a Project that has the Memory Profiler package installed, create a class that inherits from `Unity.MemoryProfiler.IMetadataCollect`.

You need to implement `void CollectMetadata(MetaData data)` in which you fill `data` with the information you want. You can create multiple classes that inherit from `Unity.MemoryProfiler.IMetadataCollect` but their `CollectMetadata` methods have no defined call order.

If you have a class that inherits from `Unity.MemoryProfiler.IMetadataCollect`, it does not generate the default metadata described in __Define snapshot metadata__. If you want to keep some or all of the default metadata, go to the file `com.unity.memoryprofiler/Runtime/MetadataInjector.cs` and copy the content you want to keep from `DefaultCollect(MetaData data)` into your implementation.
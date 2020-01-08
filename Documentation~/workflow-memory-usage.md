# How to inspect memory usage

You can use the Memory Profiler to get an overview of the memory usage in your Unity Project.

This example shows you how to capture memory snapshots with the Memory Profiler and use the [Tree Map](tree-map) to explore the memory landscape of your project.

## Inspect memory usage

1. Inside the Memory Profiler window, open a memory snapshot that you want to inspect. By default, the memory snapshot opens in the [Tree Map](tree-map) view.
2. Take a look at the different object categories in the Tree Map. The Tree Map gives you a visual indication of the categories the are the most memory intensive and orders them by size.
3. Click one of the category boxes in the Tree Map to see which specific objects contribute to the memory footprint in that category.
4. Click one of the objects inside a category to select it. When you select an object, the Memory Profiler displays a table below the Tree Map with detailed data on the object, including its references.
5. Go through the [Table](table) of objects, starting with the largest, and identify targets for size reduction. [Texture](https://docs.unity3d.com/Manual/Textures.html) objects, [shader](https://docs.unity3d.com/Manual/Shaders.html) variants, and preallocated buffers are typically good candidates for memory optimization.

 **Tip**: Inspect your memory usage regularly, even at the earliest stages of production, to minimize the risk of not being able to fit your product on the target device.
# Analyzing Unity object memory leaks

The objects and types that Unity uses to build your application are partially implemented in native code, with a significant part of their data stored in native memory managed by Unity's memory manager.

These objects typically appear as types inheriting from `UnityEngine.Object`, which corresponds to a native counterpart called `NamedObject`. The Memory Profiler refers to these objects as __Unity Objects__.

## How Unity manages references to native objects

For your C# scripts to interact with native objects, Unity creates managed wrapper objects on demand. These wrapper objects are called managed shells.

Unity can load objects that don't have any C# code handling as native objects, such as a `BoxCollider` or a `Texture`. The first time these objects are accessed from C# code, Unity creates a managed shell, such as `UnityEngine.BoxCollider` or `UnityEngine.Texture2D`. This is tracked in the Profiler as a GC.Alloc event. Unity caches the managed shell and reuses it until the native object is destroyed. Unity objects derived from `MonoBehaviour` or `ScriptableObject` always have a managed shell as soon as they are created.

> [!TIP]
> Caching managed shells explicitly in scripts doesn't reduce the amount of managed memory that Unity allocates. However, avoiding using methods like `GetComponent` or properties on built-in Unity object types such as `GameObject.transform` in performance critical sections can reduce the amount of time that your code crosses over to native code to retrieve the handle to the managed shell. This is a minor CPU optimization that might lead to a high managed memory footprint to store the pointer, and might impact on [managed shell memory leaks](#managed-shell-memory-leaks).

## Managed shell memory leaks

If your code holds a reference to a Unity object whose native part has been destroyed (for example when a scene with the object in is unloaded, or when `UnityEngine.Object.Destroy` is called), its managed shell remains in memory.

Because of the overloaded `==` operator and the implicit conversion to `bool` for managed types inheriting from `UnityEngine.Object`, this reference might appear to be `== null` and implicitly convert to `false`. This behavior is why such objects are sometimes referred to as fake null objects.

Once a Unity object becomes a leaked managed shell it serves no further purpose, so monitoring these type of objects and reducing their count can be a good way to reduce memory usage.

## Memory impact of managed shell memory leaks

The memory impact of leaked managed shells depends on the type of objects:

* For most Unity native types, leaked shells hold minimal memory because they reference native objects selectively. For instance, a leaked `Material` shell doesn't retain its texture references.
* For custom C# types, such as classes derived from `MonoBehaviour` or `ScriptableObject`, the impact can be severe if managed shells hold references to memory-intensive data (such as large arrays or textures). Leaking such references prevents both managed and native memory from being freed during `Resources.UnloadUnusedAssets` or scene unloads.

## Finding managed shell memory leaks

To find managed shell memory leaks:

1. Open the [All Of Memory tab](main-component.md#all-of-memory-tab) of the Main panel.
1. Enter `“(Leaked Managed Shell)”` into the search field.

Use the __Managed Fields__ data group in the [Selection Details panel](selection-details-component.md) and the __References To__ tab of the [References panel](references-component.md) to find out what references these objects still hold onto.

To ensure that Unity properly unloads a leaked managed shell in future:

1. Use the __Referenced By__ tab on the [References panel](references-component.md) to find out what is referencing them.
1. Find reference to these in the C# code.
1. Manually set these references to `null`.

> [!TIP]
> You don't always need to set all references to `null`. For example, if the references are confined to objects in a scene that are eventually unloaded.

## Additional resources

* [Memory Profiler reference](memory-profiler-window-section.md)
* [Find memory leaks](find-memory-leaks.md)

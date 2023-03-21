# Managed Shell Objects

## What are Managed Shells?
In Unity a lot of the objects and types used in building your application have some part of them implemented in native code and often a good chunk of their data stored in native allocations that are handled by Unity's memory manager under the hood. When you can interact with these in your C# scripts via our Scripting API, you usually encounter these as types that inherit from `UnityEngine.Object`, which has an equivalent on the native side as NamedObject. For simplicity sake, we call these __Unity Objects__ in the Memory Profiler.

For your scripts to be able to interact with the native objects and their memory, Unity creates managed wrapper objects for each of the objects your managed code interacts with, on demand. Their on demand creation means that, for example, a `BoxCollider` or a `Texture` that is loaded into memory without any C# code handling that loading (e.g. a scene load for an object with such a component and a `Renderer`, that references a `Material` that references the `Texture`) will just be created as a native object. If C# code then queries the `BoxCollider` component, or accesses component fields referencing the `Texture`, Unity will create their managed wrapper of the managed types `UnityEngine.BoxCollider` and `UnityEngine.Texture2D`. Once such a wrapper is created, it is cached and held onto by the native object, until it is destroyed, meaning subsequent access to it won't create a new wrapper.

## What are Leaked Managed Shells?

The native part of a __Unity Object__ can be destroyed, e.g. because the __Scene__ that a `GameObject` or `Component` resides in has been unloaded, or because `UnityEngine.Object.Destroy()` is called on these. If C# code holds a reference to a __Unity Object__, after it has been destroyed, it keeps the managed wrapper object, its __Managed Shell__, in memory. Due to an overload of the `==` operator and the implicit conversion to bool for managed types inheriting from `UnityEngine.Object`, this reference may appear to be `== null` and implicitly converts to `false`. This is why these objects are sometimes called __"fake null"__ objects.

## How Bad are Leaked Manage Shells?

The impact on your memory usage for holding on to these __Leaked Managed Shell__ objects is often not huge, as the majority memory held by most of these type of objects is native memory. Most of the types in Unity's API layer, e.g. the Material in the example above, also only reference other __Unity Objects__ through native references and only expose properties that will fetch the __Managed Shell__ object for these when queried. A __Leaked Managed Shell__ of a Material does therefore not hold on to the __Texture Asset__.

The same is __not__ true for your own C# types. If e.g. your `MonoBehaviour` or `ScriptableObject` derived types hold a reference to a Texture, or managed types that may consume a lot of memory, like huge arrays or other collections, leaking a managed shell of such a type can have devastating effects on your memory usage beyond the small amount held by just the __Leaked Managed Shell__ itself as the referenced memory will be kept from being unloaded. In case of __Asset__ type __Unity Objects__, i.e. __Unity Objects__ that are not __Game Objects__ or their __Components__, such references will not only keep their Managed Shells but also their native memory from being freed up by `Resources.UnloadUnusedAssets()` or a destructive Scene unload.

## How to Analyze Leaked Managed Shells?

If you enter `“(Leaked Managed Shell)”` into the search field of the [All Of Memory](all-memory-tab.md) table, you can get a quick overview of all of these in your snapshot and check if any of these could be problematic. You can see what they might still hold on to via the __Managed Fields__ data group in the [Selection Details component](selection-details-component.md) and the __References To__ tab of the [Referencing component](references-component.md).

If you want to ensure that a Leaked Managed Shell will be properly unloaded in the future:

 1. Go over the references shown in __Referenced By__ tab on the [References component](references-component.md) to find out what is referencing them.
 2. Find reference to these in the C# code.
 3. Make sure to manually set these references to `null`.

Once that is done, the managed __Garbage Collector__ will take care of the rest.

## Counter Indicators

Always nulling all references to __Unity Objects__ can be a premature optimization. For example, if the references are confined to objects in a scene that will eventually be unloaded and there is no cumulative effect because it is only a finite set of objects, it can be safe to ignore these. That said, once a __Unity Object__ becomes a __Leaked Managed Shell__, it serves no further purpose, so monitoring these type of objects via the Memory Profiler and reducing their count can be a good way to keep abreast of instances where paying closer attention to your lifetime management of these objects could be usefull to avoid surprises.

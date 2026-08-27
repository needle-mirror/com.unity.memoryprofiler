# Attribute memory to its roots

Use the Memory Profiler's **Roots & Impact** view to attribute the memory in a snapshot to the roots that keep it allocated, so you can decide which areas of memory to target for cleanup based on their impact.

The Roots & Impact view groups all the memory in a snapshot by what roots it: the static field, scene, AssetBundle, or other system that keeps it from being released. For each entry it shows the impact that entry has on memory: the memory it keeps alive on its own, plus its share of any memory it keeps alive together with other roots. This is different from a call stack, which tells you where memory was allocated but not what keeps it alive.

To understand the rooting and impact attribution model behind the view, refer to [Root ownership and impact attribution](root-ownership-and-impact-attribution.md). To learn how Unity handles the managed and native memory of Unity objects, refer to [Analyzing Unity object memory leaks](managed-shell-objects.md). The Roots & Impact view uses the same rooting, status, and impact data shown for a single object in the [References panel's](references-component.md) **Shortest Path To Root** column and the [Selection Details panel](selection-details-component.md).

> [!NOTE]
> To use the Roots & Impact view, you need to capture a snapshot. For more information, refer to [Capture or import a snapshot](snapshot-capture.md).

## Open the Roots & Impact view

To open the Roots and Impact view:

1. Open **Window** > **Analysis** > **Memory Profiler**.
1. Select a snapshot in the [Snapshots panel](snapshots-component.md) to open it.
1. In the [Main panel](main-component.md), select the **Roots & Impact** tab.

## Interpret the Roots & Impact view

The Roots & Impact view groups memory under the root that keeps that memory allocated. Each type of root is grouped by its expected lifetime. The lifetime of each root group also determines the strength with which each group holds its references in memory. This view displays the following key groups in descending order:

- Static fields
- `DontDestroyOnLoad` objects
- Scenes
- AssetBundles

Refer to [Root ownership and impact attribution](root-ownership-and-impact-attribution.md#root-priority-order) for the full ordered list of root groups and to learn what each group contains.

Each root group entry displays its impact in the following columns:

| **Column** | **Description** |
| --- | --- |
| **Exclusive** | Memory that only this entry keeps alive. If you release this entry, you release this memory. |
| **Shared** | This entry's proportional share of memory that's kept alive through more than one reference path. The view divides such memory across the referees on each object's shortest path to root and attributes a fraction to each. |
| **Shared (non-distributed)** | The shared memory this entry touches, counted in full as if this entry were its sole owner. Use it to spot central hub entries that reference a lot of shared memory even when their proportional Shared value is small. Hidden by default. To display this column, select it in the column header's context menu. |
| **Total** | The sum of the Exclusive and Shared memory. |

By default, the view sorts entries by **Total** impact, so the largest contributors appear first. This often resembles the root-group order, because stronger root types tend to capture everything they reference and later groups capture only the memory left over.

> [!NOTE]
> Impact values cover native, managed, and graphics memory. You can display a column for each memory type in the context menu of the column header. You can also use that context menu to show Allocated, Allocated and Resident, or Resident values.

> [!NOTE]
> A shared memory entry can also be the single owner of memory. For example, if an entry reaches the same object through two of its own references, the Shared view attributes that object's memory to the entry as two shared fractions. The **Shared (non-distributed)** column shows the full amount regardless of how it's split between entries under different roots or distant relatives within a single root's reference graph.

## Find and prioritize leaked objects

To filter the view to the objects that the Memory Profiler can algorithmically identify unambiguously as leaked:

1. Open the **Status** filter dropdown. The table displays all statuses by default.
1. Clear **Select all**, then enable only the **All Leaked** status group.

How the results appear depends on whether you flatten the hierarchy:

- In the default hierarchical view, matching objects appear in place, with the non-matching ancestors and descendants that form their context shown greyed out.
- When you enable the flatten option, the view lists only the matching objects as a flat, sortable list. Sort by the **Total** column to rank them by overall impact.

> [!TIP]
> A leaked object's size can be small, but the memory it keeps alive through its references can be significant, including native and graphics memory. To accurately assess impact in each section, sort by **Total**.

The greyed-out context in the hierarchical view helps you reason about leaks in ways the flat list can't (without manually inspecting each item's reference panel):

- Judge how long a leak is likely to persist from the root it sits under. A leaked shell under a scene that's about to unload might clear with the scene, unless something in a longer-lived root group also references it.
- Spot a common source. The path from the root down to a leak is that object's shortest path to root, so when many leaks share a parent, such as one statically referenced collection or event, that parent rises to the top as the first thing to clear.
- Expand the children of a leak to learn what memory it keeps alive. This is the first place you can inspect those references as a tree, rather than the first-level **Referenced By** list in the References panel.

> [!NOTE]
> In the flattened view, an entry's **Total** value includes the impact of any matching descendants that also appear as their own rows, such as a leaked shell that roots another leaked shell. The per-row **Total** values therefore overlap and don't sum to **Total Impact In Table**, which counts each contribution only once.

Leaked dynamic assets are unreferenced, so they appear one level below their root group. Leaked shells always appear at least one level below the root group, sometimes only under a `GCHandle` entry and sometimes much further down. To learn more about leaked managed shells, refer to [Analyzing Unity object memory leaks](managed-shell-objects.md).

To check for runtime-created assets that accumulate over time, enable the **All Leaked** status group, then look for entries with the **Leaked Dynamic Asset** status.

> [!TIP]
> The **Broken Script References** group identifies leaks where the backing script of a native MonoBehaviour or ScriptableObject instance was deleted, renamed, or failed to compile. These instances carry the **Broken Script Reference** status, included in the **All Leaked** filter, because they have no reference or root that keeps them alive but aren't cleaned up automatically. To resolve the leak caused by each entry, restore the missing script or destroy the object.

## Inspect memory held by static references

To find out how much memory managed static references keep alive:

1. Expand the **Static Types** group at the top of the table.
1. Browse the entries organized by namespace and type.

> [!TIP]
> Your code keeps memory under the **Static Types** group alive for the lifetime of the application unless it explicitly clears the references. A common source of leaks is a static reference into a scene that keeps that scene's objects alive after the scene unloads, which can turn the managed objects into leaked shells.

## Inspect the memory a scene keeps alive

To find out which scene, and which part of its hierarchy, keeps the most memory alive:

1. Locate the **Scenes** group in the table.
1. Expand a scene to reveal its hierarchy down to individual GameObjects and components.

Each hierarchy level displays its own impact, so you can identify the parts of a scene that contribute the most to memory usage.

## Inspect memory retained by an AssetBundle

To find out how much memory in an AssetBundle is loaded but not currently used:

1. Locate the **Asset Bundles** group in the table.
1. Expand a bundle to view the assets it keeps loaded.

Any asset shown under a bundle is loaded only because the bundle is loaded. The bundle's impact excludes memory shared with assets that are referenced from outside the bundle, so it reflects what you could free by unloading or re-slicing the bundle. Use this to reason about bundle granularity.

## Show all memory in the snapshot

By default, the **Roots & Impact** view hides parts of memory that have no impact beyond their own size, as well as the more abstract, less actionable categories such as reserved managed and native memory, untracked memory, and virtual machine memory.

To show these categories, enable the corresponding toggles at the bottom of the view. With every category shown, the total of the table reconciles with the total memory in the snapshot.

## Additional resources

- [Root ownership and impact attribution](root-ownership-and-impact-attribution.md)
- [Find memory leaks](find-memory-leaks.md)
- [Analyzing Unity object memory leaks](managed-shell-objects.md)
- [References panel reference](references-component.md)
- [Selection Details panel reference](selection-details-component.md)

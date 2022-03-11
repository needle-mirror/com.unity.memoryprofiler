#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Data model to describe the available Object Breakdowns of a snapshot.
    class ObjectBreakdownsModel
    {
        public ObjectBreakdownsModel(CachedSnapshot snapshot, List<Option> breakdowns)
        {
            Snapshot = snapshot;
            Breakdowns = breakdowns;
        }

        // The selected memory snapshot.
        public CachedSnapshot Snapshot { get; }

        // Available object breakdowns.
        public List<Option> Breakdowns { get; }

        // Create a model containing the default available Object Breakdowns.
        public static ObjectBreakdownsModel CreateDefault(CachedSnapshot snapshot)
        {
            return new ObjectBreakdownsModel(snapshot, new List<Option>()
            {
                new Option("Unity Objects", "A breakdown of memory contributing to all Unity Objects.", (CachedSnapshot s) => {
                    return new UnityObjectsBreakdownViewController(s);
                }),
                new Option("Potential Duplicates", "A breakdown of memory showing all potential duplicate Unity Objects. Potential duplicates, which are Unity Objects of the same type, name, and size, might represent the same asset loaded multiple times in memory.", (CachedSnapshot s) => {
                    return new UnityObjectsBreakdownViewController(s, true);
                }),
                new Option("All Tracked Memory", "A breakdown of all tracked memory that Unity knows about.", (CachedSnapshot s) => {
                    return new AllTrackedMemoryBreakdownViewController(s);
                })
            });
        }

        // Option represents a single available object-breakdown.
        public readonly struct Option
        {
            public Option(string displayName, string description, Func<CachedSnapshot, ViewController> createViewControllerCallback)
            {
                DisplayName = displayName;
                Description = description;
                CreateViewControllerCallback = createViewControllerCallback;
            }

            public string DisplayName { get; }

            public string Description { get; }

            Func<CachedSnapshot, ViewController> CreateViewControllerCallback { get; }

            public ViewController CreateViewController(CachedSnapshot snapshot)
            {
                return CreateViewControllerCallback(snapshot);
            }
        }
    }
}
#endif

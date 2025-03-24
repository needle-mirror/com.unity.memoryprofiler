using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MemoryProfiler.Editor.UIContentData;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Top N Unity objects memory usage summary model builder for table view controller
    /// Collects data from a captured memory snapshot and builds a model representation.
    /// </summary>
    internal class UnityObjectsMemorySummaryModelBuilder : IMemorySummaryModelBuilder<MemorySummaryModel>
    {
        readonly string[] k_UxmlCategoryStyleIds = {
            "grp-1",
            "grp-2",
            "grp-3",
            "grp-4",
            "grp-5"
        };
        readonly string k_UxmlCategoryStyleOther = "other";

        CachedSnapshot m_BaseSnapshot;
        CachedSnapshot m_ComparedSnapshot;

        record RowSize(ulong BaseSize, ulong ComparedSize);

        public UnityObjectsMemorySummaryModelBuilder(CachedSnapshot baseSnapshot, CachedSnapshot comparedSnapshot)
        {
            m_BaseSnapshot = baseSnapshot;
            m_ComparedSnapshot = comparedSnapshot;
        }

        public MemorySummaryModel Build()
        {
            // Calculate joint total for snapshots *A* and *B*
            // Nb! We can't use type index, as it might be different for
            // different snapshots that's why name is used instead
            ulong totalBase = 0, totalCompared = 0;
            var typeToSizeMap = new Dictionary<string, RowSize>();
            CalculateTotals(m_BaseSnapshot, ref totalBase, typeToSizeMap, (size, val) => { return new RowSize(val.BaseSize + size, val.ComparedSize); });
            if (m_ComparedSnapshot != null)
                CalculateTotals(m_ComparedSnapshot, ref totalCompared, typeToSizeMap, (size, val) => { return new RowSize(val.BaseSize, val.ComparedSize + size); });

            // Sort
            var list = typeToSizeMap.ToList();
            list.Sort((l, r) =>
            {
                var totalL = l.Value.BaseSize + l.Value.ComparedSize;
                var totalR = r.Value.BaseSize + r.Value.ComparedSize;
                return -totalL.CompareTo(totalR);
            });

            // Pick top k_MaxTopElements and make table rows
            ulong totalTop10Base = 0, totalTop10Compared = 0;
            var rows = new List<MemorySummaryModel.Row>();
            for (int i = 0; i < list.Count && i < k_UxmlCategoryStyleIds.Length; i++)
            {
                var item = list[i];
                rows.Add(new MemorySummaryModel.Row(item.Key, item.Value.BaseSize, 0, item.Value.ComparedSize, 0, k_UxmlCategoryStyleIds[i], TextContent.TopUnityObjectCategoriesDescription, null));

                totalTop10Base += item.Value.BaseSize;
                totalTop10Compared += item.Value.ComparedSize;
            }

            // Add "other" item for everything outside of top k_MaxTopElements
            if (list.Count > k_UxmlCategoryStyleIds.Length)
                rows.Add(new MemorySummaryModel.Row(SummaryTextContent.kUnityObjectsCategoryOther, totalBase - totalTop10Base, 0, totalCompared - totalTop10Compared, 0, k_UxmlCategoryStyleOther, TextContent.TopUnityObjectCategoriesDescription, null) { SortPriority = MemorySummaryModel.RowSortPriority.ShowLast });

            bool compareMode = m_ComparedSnapshot != null;
            return new MemorySummaryModel(
                SummaryTextContent.kUnityObjectsTitle,
                SummaryTextContent.kUnityObjectsDescription,
                compareMode,
                totalBase,
                totalCompared,
                rows,
                null);
        }

        record UnityObjectSize(ulong Native, ulong Managed)
        {
            public ulong TotalSize => Native + Managed;

            public static UnityObjectSize operator +(UnityObjectSize l, UnityObjectSize r) => new UnityObjectSize(l.Native + r.Native, l.Managed + r.Managed);
        }

        static void CalculateTotals(CachedSnapshot cs, ref ulong totalValue, Dictionary<string, RowSize> typeIndexToSizeMap, Func<ulong, RowSize, RowSize> updater)
        {
            // Sum values from entites memory map
            // TODO: use NativeHashmap for the Dictionary to avoid GC Allocs and use ref record structs for UnityObjectSize to avoid their allocs
            var objectsSize = new Dictionary<long, UnityObjectSize>();

            var processedNativeRoots = cs.ProcessedNativeRoots;
            for (long i = 0; i < processedNativeRoots.Count; i++)
            {
                ref readonly var data = ref processedNativeRoots.Data[i];
                switch (data.NativeObjectOrRootIndex.Id)
                {
                    case SourceIndex.SourceId.NativeObject:
                        var size = data.AccumulatedRootSizes;
                        var objectSize = data.AccumulatedRootSizes.SumUp().Committed;
                        AddNativeObject(objectsSize, data.NativeObjectOrRootIndex.Index, objectSize, 0);
                        break;
                    default:
                        break;
                }
            }

            // Group by type name and copy results
            totalValue = 0;
            foreach (var o in objectsSize)
            {
                AddToTypeGroup(cs, o.Key, o.Value.TotalSize, typeIndexToSizeMap, updater);
                totalValue += o.Value.TotalSize;
            }
        }

        static void AddNativeObject(Dictionary<long, UnityObjectSize> objectsSizeMap, long index, ulong nativeSize, ulong managedSize)
        {
            var size = new UnityObjectSize(nativeSize, managedSize);
            if (objectsSizeMap.TryGetValue(index, out var storedValue))
                size += storedValue;
            objectsSizeMap[index] = size;
        }

        static void AddToTypeGroup(CachedSnapshot snapshot, long index, ulong size, Dictionary<string, RowSize> typeIndexToSizeMap, Func<ulong, RowSize, RowSize> updater)
        {
            var typeIndex = snapshot.NativeObjects.NativeTypeArrayIndex[index];
            var typeName = snapshot.NativeTypes.TypeName[typeIndex];
            if (typeIndexToSizeMap.TryGetValue(typeName, out var accumulatedSize))
                typeIndexToSizeMap[typeName] = updater(size, accumulatedSize);
            else
                typeIndexToSizeMap[typeName] = updater(size, new RowSize(0, 0));
        }
    }
}

using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.UIContentData;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Builds the "Potential Savings" Summary widget. Each row sums Roots &amp; Impact <see cref="MemoryImpact"/>
    /// over an <see cref="ObjectStatus"/> mask, so it reconciles with the Roots &amp; Impact view deep-linked with that mask.
    /// </summary>
    internal class RootsAndImpactSummaryModelBuilder : IMemorySummaryModelBuilder<MemorySummaryModel>
    {
        readonly string[] k_UxmlCategoryStyleIds = {
            "grp-1",
            "grp-2",
            "grp-3",
            "grp-4",
            "grp-5"
        };

        internal enum TypeOfRootIssues : byte
        {
            LeakedShells,
            LeakedDynamicallyCreatedAssets,
            UnloadableAssetBundleMemory,
            UnloadableAssetMemory,
            BrokenScriptReferences,
        }

        // Indexed by TypeOfRootIssues; order must match that enum.
        static readonly string[] TypeOfRootIssueNames = new string[]
        {
            TextContent.PotentialSavingsLeakedManagedShells,
            TextContent.PotentialSavingsLeakedDynamicAssets,
            TextContent.PotentialSavingsUnusedAssetsInAssetBundles,
            TextContent.PotentialSavingsUnloadableAssets,
            TextContent.PotentialSavingsBrokenScriptReferences,
        };

        /// <summary>
        /// The <see cref="ObjectStatus"/> mask a category aggregates, which is also the filter its deep-link applies.
        /// </summary>
        internal static ulong MaskForIssue(TypeOfRootIssues issue)
        {
            switch (issue)
            {
                case TypeOfRootIssues.LeakedShells:
                    return Bit(ObjectStatus.LeakedShell) | Bit(ObjectStatus.LeakedShellHeldByGCHandle);
                case TypeOfRootIssues.LeakedDynamicallyCreatedAssets:
                    // Excludes DynamicAssetDontUnload: DDoL dynamic assets are often retained intentionally.
                    return Bit(ObjectStatus.DynamicAssetLeaked);
                case TypeOfRootIssues.UnloadableAssetBundleMemory:
                    return Bit(ObjectStatus.UnusedAssetInAssetBundle) | Bit(ObjectStatus.UnusedAssetBundle) | Bit(ObjectStatus.LeakedAssetBundle);
                case TypeOfRootIssues.UnloadableAssetMemory:
                    return Bit(ObjectStatus.UnusedAsset) | Bit(ObjectStatus.UnusedAssetDontUnload);
                case TypeOfRootIssues.BrokenScriptReferences:
                    return Bit(ObjectStatus.BrokenScriptReference);
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Whether this category's deep-link opens Roots &amp; Impact flattened. AssetBundles open expanded so
        /// bundle groups can be assessed for granularity.
        /// </summary>
        internal static bool FlattenForIssue(TypeOfRootIssues issue)
            => issue != TypeOfRootIssues.UnloadableAssetBundleMemory;

        /// <summary>Bit (bit N = <see cref="ObjectStatus"/> N) for a single status.</summary>
        static ulong Bit(ObjectStatus status) => 1ul << (int)status;

        /// <summary>Union of all category masks, for the widget-level Inspect deep-link.</summary>
        public static readonly ulong AllSavingsStatusMask =
            MaskForIssue(TypeOfRootIssues.LeakedShells)
            | MaskForIssue(TypeOfRootIssues.LeakedDynamicallyCreatedAssets)
            | MaskForIssue(TypeOfRootIssues.UnloadableAssetBundleMemory)
            | MaskForIssue(TypeOfRootIssues.UnloadableAssetMemory)
            | MaskForIssue(TypeOfRootIssues.BrokenScriptReferences);

        /// <summary>
        /// Deep-link mask of the AssetBundle row. The deep-linked table takes its header total from
        /// <see cref="RootAndImpactInfo.TotalAssetBundleRecoverable"/>, the same value the row shows.
        /// </summary>
        public static readonly ulong UnloadableAssetBundleStatusMask = MaskForIssue(TypeOfRootIssues.UnloadableAssetBundleMemory);

        /// <summary>
        /// The status mask of the savings category <paramref name="status"/> belongs to, if any, so the deep-linked
        /// table can dedup per category like this builder does.
        /// </summary>
        internal static bool TryGetCategoryMask(ObjectStatus status, out ulong categoryMask)
        {
            if (TryMapStatusToIssue(status, out var issue))
            {
                categoryMask = MaskForIssue(issue);
                return true;
            }
            categoryMask = 0;
            return false;
        }

        /// <summary>Maps an object's status to the savings category that aggregates it, if any.</summary>
        internal static bool TryMapStatusToIssue(ObjectStatus status, out TypeOfRootIssues issue)
        {
            switch (status)
            {
                case ObjectStatus.LeakedShell:
                case ObjectStatus.LeakedShellHeldByGCHandle:
                    issue = TypeOfRootIssues.LeakedShells;
                    return true;
                case ObjectStatus.DynamicAssetLeaked:
                    issue = TypeOfRootIssues.LeakedDynamicallyCreatedAssets;
                    return true;
                case ObjectStatus.UnusedAssetInAssetBundle:
                case ObjectStatus.UnusedAssetBundle:
                case ObjectStatus.LeakedAssetBundle:
                    // UsedAssetBundle is unmapped: its reclaimable content is already in TotalAssetBundleRecoverable.
                    issue = TypeOfRootIssues.UnloadableAssetBundleMemory;
                    return true;
                case ObjectStatus.UnusedAsset:
                case ObjectStatus.UnusedAssetDontUnload:
                    issue = TypeOfRootIssues.UnloadableAssetMemory;
                    return true;
                case ObjectStatus.BrokenScriptReference:
                    issue = TypeOfRootIssues.BrokenScriptReferences;
                    return true;
                default:
                    issue = default;
                    return false;
            }
        }

        readonly string k_UxmlCategoryStyleOther = "other";

        CachedSnapshot m_BaseSnapshot;
        CachedSnapshot m_ComparedSnapshot;

        /// <summary>
        /// Per-category size accumulators. <c>TotalDedup</c> applies <see cref="ApplySavingsAncestorDiscount"/>, the same
        /// discount as <see cref="RootsAndImpactModelBuilder.CollectFlatNodes"/>, so it matches the deep-linked table header.
        /// </summary>
        struct RowSize
        {
            public ulong BaseExclusive;
            public ulong BaseTotalDedup;
            public ulong ComparedExclusive;
            public ulong ComparedTotalDedup;
        }

        public RootsAndImpactSummaryModelBuilder(CachedSnapshot baseSnapshot, CachedSnapshot comparedSnapshot)
        {
            m_BaseSnapshot = baseSnapshot;
            m_ComparedSnapshot = comparedSnapshot;
        }

        public MemorySummaryModel Build()
        {
            ulong totalBase = 0, totalCompared = 0;
            var typeToSizeMap = new RowSize[TypeOfRootIssueNames.Length];
            CalculateTotals(m_BaseSnapshot, ref totalBase, typeToSizeMap, isBase: true);
            if (m_ComparedSnapshot != null)
                CalculateTotals(m_ComparedSnapshot, ref totalCompared, typeToSizeMap, isBase: false);

            // Sort by deduplicated total impact, descending, to match R&I ordering.
            var list = new List<(TypeOfRootIssues Issue, RowSize Size)>();
            for (int i = 0; i < typeToSizeMap.Length; i++)
            {
                var size = typeToSizeMap[i];
                if (size.BaseTotalDedup > 0 || size.ComparedTotalDedup > 0)
                    list.Add(((TypeOfRootIssues)i, size));
            }
            list.Sort((l, r) =>
                (r.Size.BaseTotalDedup + r.Size.ComparedTotalDedup).CompareTo(l.Size.BaseTotalDedup + l.Size.ComparedTotalDedup));

            ulong totalTopBase = 0, totalTopCompared = 0;
            // Summed from the clamped per-row values so the total equals the sum of the displayed Exclusive amounts.
            ulong exclusiveTotalBase = 0, exclusiveTotalCompared = 0;
            var rows = new List<MemorySummaryModel.Row>();
            for (int i = 0; i < list.Count && i < k_UxmlCategoryStyleIds.Length; i++)
            {
                var item = list[i];
                var description = TextContent.TopRootsAndImpactDescription;
                var baseExclusive = Math.Min(item.Size.BaseExclusive, item.Size.BaseTotalDedup);
                var comparedExclusive = Math.Min(item.Size.ComparedExclusive, item.Size.ComparedTotalDedup);
                rows.Add(new MemorySummaryModel.Row(
                    TypeOfRootIssueNames[(int)item.Issue],
                    item.Size.BaseTotalDedup, // committed channel: total impact
                    baseExclusive, // resident channel: exclusive impact
                    item.Size.ComparedTotalDedup,
                    comparedExclusive,
                    k_UxmlCategoryStyleIds[i],
                    description,
                    null)
                {
                    StatusMask = MaskForIssue(item.Issue),
                    DeepLinkFlatten = FlattenForIssue(item.Issue),
                });

                totalTopBase += item.Size.BaseTotalDedup;
                totalTopCompared += item.Size.ComparedTotalDedup;
                exclusiveTotalBase += baseExclusive;
                exclusiveTotalCompared += comparedExclusive;
            }

            if (list.Count > k_UxmlCategoryStyleIds.Length)
                rows.Add(new MemorySummaryModel.Row(SummaryTextContent.RootsAndImpactCategoryOther, totalBase - totalTopBase, 0, totalCompared - totalTopCompared, 0, k_UxmlCategoryStyleOther, TextContent.TopRootsAndImpactDescription, null) { SortPriority = MemorySummaryModel.RowSortPriority.ShowLast });

            bool compareMode = m_ComparedSnapshot != null;
            return new MemorySummaryModel(
                SummaryTextContent.RootsAndImpactTitle,
                SummaryTextContent.RootsAndImpactDescription,
                compareMode,
                totalBase,
                totalCompared,
                rows,
                null)
            {
                ResidentTotalA = exclusiveTotalBase,
                ResidentTotalB = exclusiveTotalCompared,
            };
        }

        /// <summary>
        /// Sums impact per savings category from each object's <see cref="ObjectStatus"/>. Managed objects with a
        /// native side are skipped so an object and its shell, which share a status, are not counted twice.
        /// <see cref="ApplySavingsAncestorDiscount"/> removes what a same-category ancestor already counts. The
        /// AssetBundle category comes from <see cref="ComputeAssetBundleSavingsCategory"/> instead.
        /// </summary>
        static void CalculateTotals(CachedSnapshot cs, ref ulong totalValue,
            RowSize[] typeIndexToSizeMap, bool isBase)
        {
            totalValue = 0;
            ref readonly var impact = ref cs.RootAndImpactInfo.Impact;
            var statuses = cs.ObjectStatusComputation;

            bool hasRootInfo = cs.RootAndImpactInfo.SuccessfullyBuilt;

            for (long i = 0; i < cs.NativeObjects.Count; i++)
            {
                var sourceIndex = new SourceIndex(SourceIndex.SourceId.NativeObject, i);
                var status = statuses.GetStatus(sourceIndex);
                if (!TryMapStatusToIssue(status, out var issue))
                    continue;

                if (issue is TypeOfRootIssues.UnloadableAssetBundleMemory)
                    continue;

                ref readonly var objectImpact = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in impact, sourceIndex);
                var exclusive = objectImpact.Exclusive.SumUp().Committed;
                var total = objectImpact.TotalImpact.SumUp().Committed;

                if (hasRootInfo)
                    ApplySavingsAncestorDiscount(cs, sourceIndex, issue, in objectImpact, ref exclusive, ref total);
                AddToTypeGroup(typeIndexToSizeMap, issue, exclusive, total, isBase);
                totalValue += total;
            }

            var managedObjectIndex = -1L;
            foreach (var managedObject in cs.CrawledData.ManagedObjects)
            {
                ++managedObjectIndex;
                var sourceIndex = new SourceIndex(SourceIndex.SourceId.ManagedObject, managedObjectIndex);
                if (!TryMapStatusToIssue(statuses.GetStatus(sourceIndex), out var issue))
                    continue;

                // The AssetBundle category is counted once, as a whole, via ComputeAssetBundleSavingsCategory below.
                if (issue is TypeOfRootIssues.UnloadableAssetBundleMemory)
                    continue;

                // A managed object with a native side was already counted in the native loop.
                var nativeObjectIndex = managedObject.NativeObjectIndex;
                var hasNativeSide = nativeObjectIndex >= 0 && nativeObjectIndex != NativeObjectEntriesCache.InvalidObjectIndex;
                if (hasNativeSide)
                    continue;

                ref readonly var objectImpact = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in impact, sourceIndex);
                var exclusive = objectImpact.Exclusive.SumUp().Committed;
                var total = objectImpact.TotalImpact.SumUp().Committed;

                if (hasRootInfo)
                    ApplySavingsAncestorDiscount(cs, sourceIndex, issue, in objectImpact, ref exclusive, ref total);

                AddToTypeGroup(typeIndexToSizeMap, issue, exclusive, total, isBase);
                totalValue += total;
            }

            if (hasRootInfo)
            {
                ComputeAssetBundleSavingsCategory(cs, out var abTotal, out var abExclusive);
                var abTotalCommitted = abTotal.SumUp().Committed;
                AddToTypeGroup(typeIndexToSizeMap, TypeOfRootIssues.UnloadableAssetBundleMemory, abExclusive.Committed, abTotalCommitted, isBase);
                totalValue += abTotalCommitted;
            }
        }

        /// <summary>
        /// The AssetBundle category total, shared with the deep-linked table headers so they reconcile:
        /// <see cref="RootAndImpactInfo.TotalAssetBundleRecoverable"/> plus bundle managed shells whose ownership
        /// root is not a bundle, which that total misses.
        /// </summary>
        internal static void ComputeAssetBundleSavingsCategory(CachedSnapshot cs, out NativeRootSize total, out MemorySize exclusive)
        {
            total = cs.RootAndImpactInfo.TotalAssetBundleRecoverable;
            exclusive = cs.RootAndImpactInfo.TotalAssetBundleRecoverableExclusive.SumUp();

            ref readonly var impact = ref cs.RootAndImpactInfo.Impact;
            ref readonly var allPaths = ref cs.RootAndImpactInfo.ShortestPathInfo;
            var statuses = cs.ObjectStatusComputation;
            var managedObjectIndex = -1L;
            foreach (var managedObject in cs.CrawledData.ManagedObjects)
            {
                ++managedObjectIndex;
                var sourceIndex = new SourceIndex(SourceIndex.SourceId.ManagedObject, managedObjectIndex);
                if (!TryMapStatusToIssue(statuses.GetStatus(sourceIndex), out var issue)
                    || issue != TypeOfRootIssues.UnloadableAssetBundleMemory)
                    continue;

                var nativeObjectIndex = managedObject.NativeObjectIndex;
                var hasNativeSide = nativeObjectIndex >= 0 && nativeObjectIndex != NativeObjectEntriesCache.InvalidObjectIndex;
                if (!hasNativeSide || OwnershipRootIsAssetBundle(cs, in allPaths, sourceIndex))
                    continue;

                ref readonly var objectImpact = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in impact, sourceIndex);
                if (cs.RootAndImpactInfo.TryGetImpactPathToMatch(sourceIndex, new OwnershipRootTargetMatcher(cs.RootAndImpactInfo), out var path)
                    && path.SplitHops > 0)
                {
                    // A divided share is never Exclusive; matches ChildFilteredImpact in RootsAndImpactModelBuilder.
                    total += objectImpact.TotalImpact.ScaleByFraction(path.Fraction);
                }
                else
                {
                    total += objectImpact.TotalImpact;
                    exclusive += objectImpact.Exclusive.SumUp();
                }
            }
        }

        /// <summary>
        /// Matches ownership roots, so the impact walk folds in every shared hop up to the top, as the table's
        /// per-edge discount does.
        /// </summary>
        readonly struct OwnershipRootTargetMatcher : RootAndImpactInfo.IImpactPathTargetMatcher
        {
            readonly RootAndImpactInfo m_RootAndImpactInfo;
            public OwnershipRootTargetMatcher(RootAndImpactInfo rootAndImpactInfo) => m_RootAndImpactInfo = rootAndImpactInfo;
            public bool Matches(SourceIndex node) =>
                SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_RootAndImpactInfo.ShortestPathInfo, node).IsRoot;
        }

        /// <summary>True if the ownership root of <paramref name="sourceIndex"/> is an AssetBundle native object.</summary>
        static bool OwnershipRootIsAssetBundle(CachedSnapshot cs, in NestedDynamicArray<ShortestRootPathInfo> allPaths, SourceIndex sourceIndex)
        {
            var root = SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in allPaths, sourceIndex).Root;
            return root.Id == SourceIndex.SourceId.NativeObject
                && cs.NativeObjects.NativeTypeArrayIndex[root.Index] == cs.NativeTypes.AssetBundleIdx;
        }

        static void AddToTypeGroup(RowSize[] typeIndexToSizeMap, TypeOfRootIssues type, ulong exclusive, ulong totalDedup, bool isBase)
        {
            ref var row = ref typeIndexToSizeMap[(int)type];
            if (isBase)
            {
                row.BaseExclusive += exclusive;
                row.BaseTotalDedup += totalDedup;
            }
            else
            {
                row.ComparedExclusive += exclusive;
                row.ComparedTotalDedup += totalDedup;
            }
        }

        /// <summary>
        /// Discounts <paramref name="exclusiveImpact"/> / <paramref name="totalImpact"/> by what the first node of
        /// <paramref name="issue"/>'s category on the impact path already accounts for, using the same
        /// <see cref="RootAndImpactInfo.DiscountedTotalImpact"/> rule as <see cref="RootsAndImpactModelBuilder.CollectFlatNodes"/>.
        /// Other categories are ignored because the deep-linked table filters to this category only.
        /// </summary>
        internal static void ApplySavingsAncestorDiscount(
            CachedSnapshot cs, SourceIndex current, TypeOfRootIssues issue,
            in MemoryImpact objectImpact, ref ulong exclusiveImpact, ref ulong totalImpact)
        {
            var rootAndImpactInfo = cs.RootAndImpactInfo;
            if (!rootAndImpactInfo.TryGetImpactPathToMatch(current,
                new SameCategoryTargetMatcher(cs.ObjectStatusComputation, issue), out var path))
                return;

            var discountedTotal = RootAndImpactInfo.DiscountedTotalImpact(
                objectImpact.TotalImpact, rootAndImpactInfo.GetReferrerCount(current), in path,
                out var exclusiveFullyEntailed);
            totalImpact = discountedTotal.SumUp().Committed;
            if (exclusiveFullyEntailed)
                exclusiveImpact = 0;
        }

        /// <summary>Matches nodes whose status maps to the given savings category.</summary>
        readonly struct SameCategoryTargetMatcher : RootAndImpactInfo.IImpactPathTargetMatcher
        {
            readonly ObjectStatusComputation m_Statuses;
            readonly TypeOfRootIssues m_Issue;

            public SameCategoryTargetMatcher(ObjectStatusComputation statuses, TypeOfRootIssues issue)
            {
                m_Statuses = statuses;
                m_Issue = issue;
            }

            public bool Matches(SourceIndex node) =>
                TryMapStatusToIssue(m_Statuses.GetStatus(node), out var issue) && issue == m_Issue;
        }
    }
}

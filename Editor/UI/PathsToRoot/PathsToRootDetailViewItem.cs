using System;
using System.Collections.Generic;
using System.IO;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEngine;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;
#if IMGUI_TREEVIEW_API_CHANGED
using TreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
using TreeView = UnityEditor.IMGUI.Controls.TreeView<int>;
#else
using UnityEditor.IMGUI.Controls;
#endif

namespace Unity.MemoryProfiler.Editor.UI.PathsToRoot
{
    internal sealed class PathsToRootDetailTreeViewItem : TreeViewItem
    {
        static readonly GUIContent s_DashContent = new GUIContent("—");
        public int CircularRefId { get; private set; } = -1;
        public ObjectData Data { get; }
        public string TypeName { get; }
        public string TruncatedTypeName { get; }

        static int s_IdGenerator;
        public GUIContent TypeIcon { get; private set; }
        public GUIContent ObjectFlags { get; private set; }

        public bool IsSceneRoot(CachedSnapshot cs)
        {
            return Data.IsSceneRootTransform(cs);
        }

        public string AssetPath(CachedSnapshot cs)
        {
            return Data.dataType is ObjectDataType.Scene || IsSceneRoot(cs) ? Data.GetAssetPath(cs) : String.Empty;
        }

        public string FlagsInfo = "";
        public string ToolTipMsg;
        public bool HasCircularReference;
        bool needsIcon = true;

        // Resolved lazily by EnsureImpactInfo (on first CellGUI access, not at row-build time) because
        // RawReferences/ReferencesTo rows are built on a background thread where touching RootAndImpactInfo
        // races the snapshot-unload abort, and because only a handful of built rows are ever drawn.
        // Deliberately NOT copied by the copy constructor below: clones re-resolve lazily against whatever
        // selection is current when they are next drawn, rather than carrying a stale resolution forward.
        public bool ImpactInfoResolved;
        public int SortOriginalOrder = -1;
        public long RelativeDepth;
        public bool HasPathInfo;
        public bool HasDifferentRootThanSelection;
        public long ReferrerCount;
        public bool SelectionReceivedShare;
        public ulong TotalImpactBytes;
        public ulong ShareOfImpactBytes;
        public GUIContent DepthCellContent;
        public GUIContent ReferrerCountCellContent;
        public GUIContent ShareOfImpactCellContent;
        public GUIContent TotalImpactCellContent;

        public PathsToRootDetailTreeViewItem(bool Allocation = false)
        {
            depth = -1;
            children = new List<TreeViewItem>();
            TypeName = TruncatedTypeName = "";
            id = s_IdGenerator++;
            if (Allocation)
            {
                displayName = PathsToRootDetailView.Styles.NoInspectableObjectSelected;
                needsIcon = false;
            }
        }

        /// <summary>
        /// Internal for testing purposes.
        /// </summary>
        internal PathsToRootDetailTreeViewItem(string displayName, string typeName, string truncatedTypeName) : this()
        {
            this.displayName = displayName;
            TypeName = typeName;
            TruncatedTypeName = truncatedTypeName;
        }

        public PathsToRootDetailTreeViewItem(PathsToRootDetailTreeViewItem other)
        {
            id = other.id;
            id = other.id;
            depth = other.depth;
            Data = other.Data;
            TypeName = other.TypeName;
            TruncatedTypeName = other.TruncatedTypeName;
            displayName = other.displayName;
            children = other.children != null ? new List<TreeViewItem>(other.children) : null;
            TypeIcon = other.TypeIcon;
            icon = (Texture2D)TypeIcon?.image;
            ObjectFlags = other.ObjectFlags;
            FlagsInfo = other.FlagsInfo;
            ToolTipMsg = other.ToolTipMsg;
            HasCircularReference = other.HasCircularReference;
            needsIcon = other.needsIcon;
        }

        public PathsToRootDetailTreeViewItem(ObjectData data, CachedSnapshot cachedSnapshot, PathsToRootDetailTreeViewItem potentialParent, bool truncateTypeNames, PathsToRootDetailView.ActiveTree treeType = PathsToRootDetailView.ActiveTree.RawReferences) : this(data, cachedSnapshot, truncateTypeNames, treeType)
        {
            HasCircularReference = CircularReferenceCheck(potentialParent);
        }

        public PathsToRootDetailTreeViewItem(ObjectData data, CachedSnapshot cachedSnapshot, bool truncateTypeNames, PathsToRootDetailView.ActiveTree treeType = PathsToRootDetailView.ActiveTree.RawReferences)
        {
            id = s_IdGenerator++;
            depth = -1;
            Data = data;
            children = new List<TreeViewItem>();
            HasCircularReference = false;
            if (cachedSnapshot != null)
            {
                TypeName = data.GenerateTypeName(cachedSnapshot, truncateTypeName: false);
                TruncatedTypeName = data.GenerateTypeName(cachedSnapshot, truncateTypeName: true);
                // PathsToRootDetailTreeView will choose whether to show TypeName or TruncatedTypeName
                // It thereby updates the view immediately on switching the settings
                // GetDisplayName is a bit more involved and will only in a few cases include the type name
                // currently, that display name will not get updated immediately as the setting changes
                displayName = GetDisplayName(data, cachedSnapshot, truncateTypeNames, treeType);

                SetObjectFlagsDataAndToolTip(data, cachedSnapshot);
            }
            else
            {
                TypeName = TruncatedTypeName = "";
                displayName = "";
                ObjectFlags = null;
                ToolTipMsg = "";
            }
        }

        string GetDisplayName(ObjectData data, CachedSnapshot cachedSnapshot, bool truncateTypeNames, PathsToRootDetailView.ActiveTree treeType)
        {
            ManagedObjectInfo managedObjectInfo;
            var referencedItemName = "";
            ObjectData displayObject = data.displayObject;
            // for ReferencesTo Items reported as field data, we need to adjust
            if (treeType is PathsToRootDetailView.ActiveTree.ReferencesTo && (data.IsField() || data.IsArrayItem()))
            {
                // The field info comes from the parent
                displayObject = data.Parent.Obj;
                // but we also want to show the referenced target held by the field
                referencedItemName = $"{data.GenerateObjectName(cachedSnapshot)} referenced by: ";
            }
            switch (displayObject.dataType)
            {
                case ObjectDataType.NativeObject:
                    return referencedItemName + cachedSnapshot.NativeObjects.GetNonEmptyObjectName(displayObject.nativeObjectIndex);
                case ObjectDataType.Unknown:
                    return referencedItemName + "<unknown>";
                case ObjectDataType.Value:
                    if (!displayObject.IsField())
                    {
                        Debug.LogError("Connection via a value that is not a fieldshould not happen.");
                        return referencedItemName + "Connection to Value";
                    }
                    var fieldType = cachedSnapshot.FieldDescriptions.TypeIndex[displayObject.fieldIndex];
                    // fieldType is -1 for CoreCLR pointer/function-pointer fields, which are pointer
                    // fields by definition, so there's no TypeDescriptions row to check against. Gate on
                    // HasBackendManagedGraph so a negative TypeIndex on another backend isn't assumed to be a
                    // pointer field (and doesn't fall through to indexing TypeDescriptionName with it below).
                    var isPointerField = fieldType < 0
                        ? cachedSnapshot.HasBackendManagedGraph
                        : fieldType == cachedSnapshot.TypeDescriptions.ITypeIntPtr || cachedSnapshot.TypeDescriptions.TypeDescriptionName[fieldType].EndsWith('*');
                    var referencingNativeData = data.codeType == CodeType.Native;
                    return referencedItemName + displayObject.GetFieldDescription(cachedSnapshot, truncateTypeNames: truncateTypeNames)
                        + (!referencingNativeData && !isPointerField ? " (Boehm reads pointer sized field as potential pointers)" : "");

                case ObjectDataType.BoxedValue:
                case ObjectDataType.Object:
                    if (displayObject.isManaged)
                    {
                        if (displayObject.IsField())
                        {
                            return referencedItemName + displayObject.GetFieldDescription(cachedSnapshot, truncateTypeNames: truncateTypeNames);
                        }
                        managedObjectInfo = displayObject.GetManagedObject(cachedSnapshot);
                        if (managedObjectInfo.NativeObjectIndex != -1)
                        {
                            return referencedItemName + cachedSnapshot.NativeObjects.GetNonEmptyObjectName(managedObjectInfo.NativeObjectIndex);
                        }
                        if (managedObjectInfo.ITypeDescription == cachedSnapshot.TypeDescriptions.ITypeString)
                        {
                            return StringTools.GetPreviewOrReadFirstLine(displayObject.GetManagedObject(cachedSnapshot), cachedSnapshot, true);
                        }
                    }
                    return $"{referencedItemName}[0x{displayObject.hostManagedObjectPtr:x8}]";
                case ObjectDataType.Array:
                    while (!data.IsArrayItem() && data.dataType != ObjectDataType.Array && data.dataType != ObjectDataType.ReferenceArray)
                    {
                        // The display object is an array, so there must be an array somewhere in its parents.
                        // Don't grab the display object directly, as that would get rid of the array element info.
                        data = data.Parent.Obj;
                    }
                    return referencedItemName + data.GenerateArrayDescription(cachedSnapshot, truncateTypeName: truncateTypeNames);
                case ObjectDataType.ReferenceObject:
                case ObjectDataType.ReferenceArray:
                    if (displayObject.IsField()) return referencedItemName + displayObject.GetFieldDescription(cachedSnapshot, truncateTypeNames: truncateTypeNames);
                    if (displayObject.IsArrayItem()) return referencedItemName + displayObject.GenerateArrayDescription(cachedSnapshot, truncateTypeName: truncateTypeNames);
                    managedObjectInfo = displayObject.GetManagedObject(cachedSnapshot);
                    if (managedObjectInfo.NativeObjectIndex != -1)
                    {
                        return referencedItemName + cachedSnapshot.NativeObjects.GetNonEmptyObjectName(managedObjectInfo.NativeObjectIndex);
                    }

                    return $"{referencedItemName}Unknown {displayObject.dataType}. Is not a field or array item";
                case ObjectDataType.Type:
                    var fieldName = string.Empty;
                    if (data.IsField())
                        fieldName = $".{data.GetFieldName(cachedSnapshot)}";
                    var typeName = truncateTypeNames ? TruncatedTypeName : TypeName;
                    return $"{referencedItemName}Static field type reference on {typeName}{fieldName}";
                case ObjectDataType.GCHandle:
                    return displayObject.GenerateObjectName(cachedSnapshot);
                case ObjectDataType.NativeAllocation: // should not be present outside of as a referencesToItem with field information to display
                    return data.nonObjectIndex.ToString();
                case ObjectDataType.Prefab:
                {
                    var prefabRootTransform = cachedSnapshot.SceneRoots.AllPrefabRootTransformSourceIndices[data.nonObjectIndex];
                    return cachedSnapshot.NativeObjects.GetNonEmptyObjectName(prefabRootTransform.Index);
                }
                case ObjectDataType.Scene:
                    return cachedSnapshot.SceneRoots.Name[data.nonObjectIndex];
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public bool CircularReferenceCheck(PathsToRootDetailTreeViewItem potentialParent)
        {
            var current = potentialParent.parent as PathsToRootDetailTreeViewItem;

            while (current != null)
            {
                if (current.Data.Equals(Data))
                {
                    CircularRefId = current.id;
                    return true;
                }

                current = current.parent as PathsToRootDetailTreeViewItem;
            }

            CircularRefId = -1;
            return false;
        }

        public void SetIcons(CachedSnapshot cs)
        {
            if (!needsIcon) return;
            TypeIcon = Icons.GetIcon(Data, TypeName, cs);
            icon = (Texture2D)TypeIcon?.image;
            ObjectFlags = string.IsNullOrEmpty(FlagsInfo) ? null : new GUIContent(Icons.FlagIcon.image, ToolTipMsg);
        }

        void SetObjectFlagsDataAndToolTip(ObjectData data, CachedSnapshot cachedSnapshot)
        {
            ToolTipMsg = "";
            FlagsInfo = "";
            GetObjectFlagsStrings(data, cachedSnapshot, ref ToolTipMsg, ref FlagsInfo, ref ToolTipMsg, ref FlagsInfo);
        }

        public static void GetObjectFlagsStrings(ObjectData data, CachedSnapshot cachedSnapshot, ref string flagsNames, ref string flagsExplanations, ref string hideFlagsNames, ref string hideFlagsExplanations, bool lineBreak = true)
        {
            if (data.nativeObjectIndex != -1)
            {
                var flags = data.GetFlags(cachedSnapshot);
                if (flags != 0x0 && lineBreak)
                {
                    flagsExplanations += PathsToRootUtils.ObjectFlagsInfoHeader;
                }

                if ((flags & Format.ObjectFlags.IsDontDestroyOnLoad) != 0)
                {
                    flagsNames += " 'IsDontDestroyOnLoad'" + (lineBreak ? " \n" : ",");
                    flagsExplanations += PathsToRootUtils.IsDontDestroyOnLoadInfo;
                }
                if ((flags & Format.ObjectFlags.IsPersistent) != 0)
                {
                    flagsNames += " 'IsPersistent'" + (lineBreak ? " \n" : ",");
                    flagsExplanations += PathsToRootUtils.IsPersistentInfo;
                }
                if ((flags & Format.ObjectFlags.IsManager) != 0)
                {
                    flagsNames += " 'IsManager'" + (lineBreak ? " \n" : ",");
                    flagsExplanations += PathsToRootUtils.IsManagerInfo;
                }
                if ((flags & Format.ObjectFlags.IsRoot) != 0)
                {
                    flagsNames += " 'IsRoot'";
                    flagsExplanations += PathsToRootUtils.IsRootInfo;
                }
                if (!string.IsNullOrEmpty(flagsNames) && flagsNames.LastIndexOf(',') == flagsNames.Length - 1)
                    flagsNames = flagsNames.Substring(0, flagsNames.Length - 1);

                var hideFlags = cachedSnapshot.NativeObjects.HideFlags[data.nativeObjectIndex];
                if (hideFlags != 0x0 && lineBreak)
                {
                    hideFlagsExplanations += PathsToRootUtils.HideFlagsInfoHeader;
                }
                if ((hideFlags & HideFlags.DontSave) != 0)
                {
                    hideFlagsNames += " 'HideFlags.DontSave'" + (lineBreak ? " \n" : ",");
                    hideFlagsExplanations += PathsToRootUtils.DontSaveInfo;
                }
                if ((hideFlags & HideFlags.NotEditable) != 0)
                {
                    hideFlagsNames += " 'HideFlags.NotEditable'" + (lineBreak ? " \n" : ",");
                    hideFlagsExplanations += PathsToRootUtils.NotEditableInfo;
                }
                if ((hideFlags & HideFlags.HideInHierarchy) != 0)
                {
                    hideFlagsNames += " 'HideFlags.HideInHierarchy'" + (lineBreak ? " \n" : ",");
                    hideFlagsExplanations += PathsToRootUtils.HideInHierarchyInfo;
                }
                if ((hideFlags & HideFlags.HideInInspector) != 0)
                {
                    hideFlagsNames += " 'HideFlags.HideInInspector'" + (lineBreak ? " \n" : ",");
                    hideFlagsExplanations += PathsToRootUtils.HideInInspectorInfo;
                }
                if ((hideFlags & HideFlags.DontSaveInEditor) != 0)
                {
                    hideFlagsNames += " 'HideFlags.DontSaveInEditor'" + (lineBreak ? " \n" : ",");
                    hideFlagsExplanations += PathsToRootUtils.DontSaveInEditorInfo;
                }
                if ((hideFlags & HideFlags.DontUnloadUnusedAsset) != 0)
                {
                    hideFlagsNames += " 'HideFlags.DontUnloadUnusedAsset'" + (lineBreak ? " \n" : ",");
                    hideFlagsExplanations += PathsToRootUtils.DontUnloadUnusedAssetInfo;
                }
                if ((hideFlags & HideFlags.HideAndDontSave) != 0)
                {
                    hideFlagsNames += " 'HideFlags.HideAndDontSave' ";
                    hideFlagsExplanations += PathsToRootUtils.HideAndDontSaveInfo;
                }
                if (!string.IsNullOrEmpty(hideFlagsNames) && hideFlagsNames.LastIndexOf(',') == hideFlagsNames.Length - 1)
                    hideFlagsNames = hideFlagsNames.Substring(0, hideFlagsNames.Length - 1);
            }
        }

        public bool HasFlags()
        {
            return ObjectFlags != null;
        }

        /// <summary>
        /// Resolves and caches this row's Depth/Referrers/Share of Impact/Total Impact cell content for
        /// <paramref name="selection"/>. Idempotent per row per selection (see <see cref="ImpactInfoResolved"/>);
        /// the view must not call this until it is about to draw the row. <paramref name="selectionTargets"/> and
        /// <paramref name="otherPartTargets"/> are the items each half of the selection references directly.
        /// </summary>
        public void EnsureImpactInfo(CachedSnapshot snapshot, SourceIndex selection, SourceIndex otherPartOfSelection,
            HashSet<SourceIndex> selectionTargets, HashSet<SourceIndex> otherPartTargets)
        {
            if (ImpactInfoResolved)
                return;
            ImpactInfoResolved = true;

            var row = Data.displayObject.GetSourceLink(snapshot);
            var rowSupported = snapshot.RootAndImpactInfo.SuccessfullyBuilt
                && row.Valid && SourceIndexToRootAndImpactInfoMapper.IsSourceIdSupported(row.Id);

            if (!rowSupported)
            {
                DepthCellContent = new GUIContent("?");
                ReferrerCountCellContent = s_DashContent;
                ShareOfImpactCellContent = s_DashContent;
                TotalImpactCellContent = s_DashContent;
                return;
            }

            ref readonly var rowStep = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in snapshot.RootAndImpactInfo.ShortestPathInfo, row);
            if (!rowStep.Valid)
            {
                DepthCellContent = new GUIContent("?");
            }
            else
            {
                ref readonly var selectionStep = ref GetPathStepIfSupported(snapshot, in selection);
                ref readonly var otherStep = ref GetPathStepIfSupported(snapshot, in otherPartOfSelection);
                HasPathInfo = TryComputeRelativeDepth(in selectionStep, in otherStep, in rowStep, out RelativeDepth, out HasDifferentRootThanSelection);

                var depthTooltip = HasPathInfo ? TextContent.PathsToRootRelativeDepthTooltip : TextContent.PathsToRootRelativeDepthAbsoluteTooltip;
                if (HasDifferentRootThanSelection)
                    depthTooltip += TextContent.PathsToRootDifferentRootTooltipAddendum;
                DepthCellContent = new GUIContent(FormatDepth(RelativeDepth, HasPathInfo, HasDifferentRootThanSelection), depthTooltip);
            }

            ref readonly var impact = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in snapshot.RootAndImpactInfo.Impact, row);
            var totalSize = impact.TotalImpact.SumUp();
            TotalImpactBytes = totalSize.Committed;
            TotalImpactCellContent = new GUIContent(EditorUtility.FormatBytes((long)totalSize.Committed), MakeByteSizeTooltip(totalSize));

            var referrerData = snapshot.RootAndImpactInfo.GetReferrerData(in row);
            if (referrerData.IsNeverRecorded)
            {
                ReferrerCountCellContent = s_DashContent;
                ShareOfImpactCellContent = s_DashContent;
                return;
            }

            // A root has no referee to divide across (ReferrerData.NoReferrer == -1); display that as 0.
            ReferrerCount = referrerData.IsRoot ? 0 : referrerData.Count;
            ReferrerCountCellContent = new GUIContent(ReferrerCount.ToString(), string.Format(TextContent.PathsToRootReferrerCountTooltipFormat, ReferrerCount));

            // TryGetReceivedShareOfImpact only checks completion order, so each half must also reference row itself.
            var info = snapshot.RootAndImpactInfo;
            var receivedShare = default(NativeRootSize);
            if (selection.Valid && selectionTargets.Contains(row)
                && info.TryGetReceivedShareOfImpact(in selection, in row, out var selectionShare, out _))
            {
                receivedShare = selectionShare;
                SelectionReceivedShare = true;
            }
            if (otherPartOfSelection.Valid && otherPartTargets.Contains(row)
                && info.TryGetReceivedShareOfImpact(in otherPartOfSelection, in row, out var otherPartShare, out _)
                && TryGetPortionPassedOn(info, in otherPartOfSelection, in selection, selectionTargets, in otherPartShare, out var passedOn))
            {
                receivedShare += passedOn;
                SelectionReceivedShare = true;
            }
            if (!SelectionReceivedShare)
            {
                ShareOfImpactCellContent = new GUIContent("—", TextContent.PathsToRootShareOfImpactNotReceivedTooltip);
                return;
            }

            var shareSize = receivedShare.SumUp();
            ShareOfImpactBytes = shareSize.Committed;
            // This is an even pro-rata split of row's impact across its referrers; AttributeImpact actually
            // hands the leftover byte(s) per channel to one specific referrer, which isn't identifiable from
            // the data available here, so the figure is an approximation - marked with "≈" rather than
            // shown as exact.
            ShareOfImpactCellContent = new GUIContent("≈ " + EditorUtility.FormatBytes((long)shareSize.Committed),
                MakeByteSizeTooltip(shareSize) + TextContent.PathsToRootShareOfImpactApproximateTooltipAddendum);
        }

        /// <summary>
        /// How much of <paramref name="amount"/>, received by <paramref name="from"/>, then reached <paramref name="to"/>:
        /// all of it if <paramref name="to"/> exclusively owns <paramref name="from"/>, an even share if <paramref name="to"/>
        /// is one of several referrers that received <paramref name="from"/>'s impact, otherwise none.
        /// Internal for testing purposes.
        /// </summary>
        internal static bool TryGetPortionPassedOn(RootAndImpactInfo info, in SourceIndex from, in SourceIndex to, HashSet<SourceIndex> toTargets,
            in NativeRootSize amount, out NativeRootSize portion)
        {
            portion = default;
            if (!to.Valid)
                return false;
            switch (info.GetExclusiveOwnership(in to, in from))
            {
                case RootAndImpactInfo.ExclusiveOwnershipResult.FirstExclusivelyOwnsSecond:
                    portion = amount;
                    return true;
                case RootAndImpactInfo.ExclusiveOwnershipResult.SecondExclusivelyOwnsFirst:
                    return false;
            }
            if (!toTargets.Contains(from) || !info.TryGetReceivedShareOfImpact(in to, in from, out _, out var otherReferrerCount))
                return false;
            portion = amount.Divide((ulong)(otherReferrerCount + 1), out _);
            return true;
        }

        /// <summary>
        /// Formats a Depth cell: "↑ n" closer to the root than the selection, "↓ n" further away, "0" at the same
        /// depth, or the plain absolute depth without path info; bracketed when the row has a different root.
        /// </summary>
        internal static string FormatDepth(long depth, bool isRelative, bool differentRoot)
        {
            string text;
            if (!isRelative || depth == 0)
                text = depth.ToString();
            else if (depth < 0)
                text = "↑ " + (-depth);
            else
                text = "↓ " + depth;
            return differentRoot ? $"({text})" : text;
        }

        static string MakeByteSizeTooltip(MemorySize size)
            => string.Format(TextContent.PathsToRootByteSizeTooltipFormat, size.Committed.ToString("N0"), EditorUtility.FormatBytes((long)size.Resident));

        static readonly ShortestRootPathInfo s_InvalidPathStep = default;

        static ref readonly ShortestRootPathInfo GetPathStepIfSupported(CachedSnapshot snapshot, in SourceIndex index)
        {
            if (!index.Valid || !SourceIndexToRootAndImpactInfoMapper.IsSourceIdSupported(index.Id))
                return ref s_InvalidPathStep;
            return ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in snapshot.RootAndImpactInfo.ShortestPathInfo, index);
        }

        /// <summary>
        /// <paramref name="relativeDepth"/> = <paramref name="row"/>'s depth minus the reference's (<paramref name="selection"/>
        /// if valid, else <paramref name="otherPartOfSelection"/>) — negative is closer to the root, positive is a
        /// referenced child, 0 is a same-depth cross-branch reference. Returns false (and reports the absolute
        /// depth instead) only when neither reference part is valid, since there is then nothing to compare against.
        /// <paramref name="differentRoot"/> compares against BOTH reference parts (a native object and its managed
        /// shell can have different roots), so it is only true when <paramref name="row"/>'s root matches neither.
        /// </summary>
        internal static bool TryComputeRelativeDepth(
            in ShortestRootPathInfo selection, in ShortestRootPathInfo otherPartOfSelection,
            in ShortestRootPathInfo row, out long relativeDepth, out bool differentRoot)
        {
            if (!selection.Valid && !otherPartOfSelection.Valid)
            {
                relativeDepth = row.Depth;
                differentRoot = false;
                return false;
            }

            var reference = selection.Valid ? selection : otherPartOfSelection;
            relativeDepth = row.Depth - reference.Depth;

            differentRoot = true;
            if (selection.Valid && row.Root == selection.Root)
                differentRoot = false;
            if (otherPartOfSelection.Valid && row.Root == otherPartOfSelection.Root)
                differentRoot = false;

            return true;
        }



        public void AddChild(PathsToRootDetailTreeViewItem child)
        {
            if (children == null)
                children = new List<TreeViewItem>();

            children.Add(child);

            if (child != null)
                child.parent = this;
        }

        public bool IsGameObjectOrTransform(CachedSnapshot cs)
        {
            return Data.IsGameObject(cs) || Data.IsTransform(cs);
        }

        public bool HasGameObjectOrTransformParent(CachedSnapshot cs)
        {
            PathsToRootDetailTreeViewItem current = this;
            while (current.parent != null)
            {
                if (current.IsGameObjectOrTransform(cs))
                    return true;

                current = (PathsToRootDetailTreeViewItem)current.parent;
            }
            return false;
        }
    }
}

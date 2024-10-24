using System;
using UnityEditor;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.UIContentData;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;
using System.Collections.Generic;
using System.Text;
using static Unity.MemoryProfiler.Editor.CachedSnapshot.NativeAllocationSiteEntriesCache;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class SelectedItemDetailsForTypesAndObjects
    {
#pragma warning disable 0649
        long m_CurrentSelectionIdx;
#pragma warning restore 0649
        ObjectData m_CurrentSelectionObjectData;
        CachedSnapshot m_CachedSnapshot;
        SelectedItemDetailsPanel m_UI;

        string k_StatusLabelText = "Status";
        string k_HintLabelText = "Hint";

        public SelectedItemDetailsForTypesAndObjects(CachedSnapshot snapshot, SelectedItemDetailsPanel detailsUI)
        {
            m_CachedSnapshot = snapshot;
            m_UI = detailsUI;
        }

        public void SetSelection(CachedSnapshot.SourceIndex source, string fallbackName = null, string fallbackDescription = null, long childCount = -1)
        {
            m_CurrentSelectionObjectData = ObjectData.FromSourceLink(m_CachedSnapshot, source);
            var type = new UnifiedType(m_CachedSnapshot, m_CurrentSelectionObjectData);
            switch (source.Id)
            {
                case CachedSnapshot.SourceIndex.SourceId.NativeObject:
                case CachedSnapshot.SourceIndex.SourceId.ManagedObject:
                    HandleObjectDetails(type);
                    break;
                case CachedSnapshot.SourceIndex.SourceId.GfxResource:
                    if (m_CurrentSelectionObjectData.IsValid)
                        HandleObjectDetails(type);
                    else
                        HandleGfxResourceDetails(source, fallbackName, fallbackDescription);
                    break;
                case CachedSnapshot.SourceIndex.SourceId.NativeType:
                case CachedSnapshot.SourceIndex.SourceId.ManagedType:
                    HandleTypeDetails(type);
                    break;
                case CachedSnapshot.SourceIndex.SourceId.NativeAllocation:
                    HandleNativeAllocationDetails(source, fallbackName, fallbackDescription);
                    break;
                case CachedSnapshot.SourceIndex.SourceId.NativeRootReference:
                    HandleNativeRootReferenceDetails(source, fallbackName, fallbackDescription, childCount);
                    break;
                default:
                    break;
            }
        }

        const string k_TriggerAssetGCHint = "triggering 'Resources.UnloadUnusedAssets()', explicitly or e.g. via a non-additive Scene unload.";

        internal void HandleTypeDetails(UnifiedType type)
        {
            if (!type.IsValid)
                return;

            m_UI.SetItemName(type);

            if (type.ManagedTypeData.IsValid && !type.ManagedTypeIsBaseTypeFallback)
                m_UI.SetManagedObjectInspector(type.ManagedTypeData);

            m_UI.SetDescription("The selected item is a Type.");

            if (type.HasManagedType)
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Managed Type", type.ManagedTypeName);
            if (type.HasNativeType)
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Native Type", type.NativeTypeName);
        }

        internal void HandleObjectDetails(UnifiedType type)
        {
            if (!m_CurrentSelectionObjectData.IsValid)
                return;

            var selectedUnityObject = new UnifiedUnityObjectInfo(m_CachedSnapshot, type, m_CurrentSelectionObjectData);

            if (!selectedUnityObject.IsValid)
            {
                if (m_CurrentSelectionObjectData.isManaged)
                {
                    HandlePureCSharpObjectDetails(type);
                }
                else
                {
                    // What even is this?
                    throw new NotImplementedException();
                }
            }
            else
            {
                HandleUnityObjectDetails(selectedUnityObject);
            }
        }

        internal void HandleNativeAllocationDetails(CachedSnapshot.SourceIndex source, string fallbackName, string fallbackDescription)
        {
            m_UI.SetItemName(source);
            var nativeSize = (long)m_CachedSnapshot.NativeAllocations.Size[source.Index];
            var references = ObjectConnection.GetAllReferencingObjects(m_CachedSnapshot, source);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Native Size", EditorUtility.FormatBytes(nativeSize), $"{nativeSize:N0} B");
            if (MemoryProfilerSettings.FeatureFlags.ShowFoundReferencesForNativeAllocations_2024_10)
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Found References", references.Length.ToString(), TextContent.NativeAllocationFoundReferencesHint);

            if (MemoryProfilerSettings.FeatureFlags.EnableUnknownUnknownAllocationBreakdown_2024_10 &&
                m_CachedSnapshot.NativeAllocations.RootReferenceId[source.Index] <= 0)
            {
                m_UI.AddInfoBox(SelectedItemDetailsPanel.GroupNameBasic, new InfoBox()
                {
                    IssueLevel = (InfoBox.IssueType)SnapshotIssuesModel.IssueLevel.Error,
                    Message =
                    MemoryProfilerSettings.InternalModeOrSnapshotWithCallSites(m_CachedSnapshot) ?
                    TextContent.UnknownUnknownAllocationsErrorBoxMessageInternalMode
                    : TextContent.UnknownUnknownAllocationsErrorBoxMessage,
                });
            }

            if (m_CachedSnapshot.NativeCallstackSymbols.Count > 0)
            {
                AddCallStacksInfoToUI(source);
            }
            // Give a hint to internal users or those with the potential to get call stacks.
            else if (MemoryProfilerSettings.InternalModeOrSnapshotWithCallSites(m_CachedSnapshot))
            {
                m_UI.AddInfoBox(SelectedItemDetailsPanel.GroupNameBasic, new InfoBox()
                {
                    IssueLevel = (InfoBox.IssueType)SnapshotIssuesModel.IssueLevel.Info,
                    Message = TextContent.NativeAllocationInternalModeCallStacksInfoBoxMessage,
                });
            }
        }

        internal void HandleGfxResourceDetails(CachedSnapshot.SourceIndex source, string fallbackName, string fallbackDescription)
        {
            m_UI.SetItemName(fallbackName);
            var gfxSize = (long)m_CachedSnapshot.NativeGfxResourceReferences.GfxSize[source.Index];
            var rootId = (long)m_CachedSnapshot.NativeGfxResourceReferences.RootId[source.Index];
            var gfxResourceId = (long)m_CachedSnapshot.NativeGfxResourceReferences.GfxResourceId[source.Index];
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Graphics Size", EditorUtility.FormatBytes(gfxSize), $"{gfxSize:N0} B");
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, "Root ID", rootId.ToString());
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, "Gfx Resource ID", gfxResourceId.ToString());

            if (rootId <= 0)
            {
                m_UI.AddInfoBox(SelectedItemDetailsPanel.GroupNameBasic, new InfoBox()
                {
                    IssueLevel = (InfoBox.IssueType)SnapshotIssuesModel.IssueLevel.Error,
                    Message = TextContent.UnrootedGraphcisResourceErrorBoxMessage,
                });
            }
            else if (m_CachedSnapshot.NativeCallstackSymbols.Count > 0)
            {
                m_UI.AddInfoBox(SelectedItemDetailsPanel.GroupNameCallStacks, new InfoBox()
                {
                    IssueLevel = (InfoBox.IssueType)SnapshotIssuesModel.IssueLevel.Info,
                    Message = TextContent.GraphcisResourceWithSnapshotWithCallStacksInfoBoxMessage,
                });
                AddCallStacksInfoToUI(source);
            }
        }

        internal void HandleNativeRootReferenceDetails(CachedSnapshot.SourceIndex source, string fallbackName, string fallbackDescription, long childCount = -1)
        {
            m_UI.SetItemName(source);
            GetRootReferenceName(m_CachedSnapshot, source, out var areaName, out var objectName);

            if (!string.IsNullOrEmpty(areaName))
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Area", areaName);

            if (!string.IsNullOrEmpty(objectName))
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Object Name", objectName);

            var accumultatedSize = (long)m_CachedSnapshot.NativeRootReferences.AccumulatedSize[source.Index];
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Size", EditorUtility.FormatBytes(accumultatedSize), $"{accumultatedSize:N0} B");

            if (childCount > 0)
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Child Count", childCount.ToString());

            if (MemoryProfilerSettings.FeatureFlags.EnableDynamicAllocationBreakdown_2024_10)
            {
                var listOfRootNames = MemoryProfilerSettings.AllocationRootsToSplit;
                var areaAndObjectName = $"{areaName}:{objectName}";
                var alreadySplit = false;
                var addButton = true;

                for (int i = 0; i < listOfRootNames.Length; i++)
                {
                    if (listOfRootNames[i] == areaAndObjectName)
                    {
                        alreadySplit = true;
                        break;
                    }
                }

                listOfRootNames = MemoryProfilerSettings.AlwaysSplitRootAllocations;
                for (int i = 0; i < listOfRootNames.Length; i++)
                {
                    if (listOfRootNames[i] == areaAndObjectName)
                    {
                        // No button for always split roots
                        addButton = false;
                        break;
                    }
                }

                if (addButton)
                    m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Rooted Allocations Display", // title is not shown
                        alreadySplit ? "Hide Allocation List" : "List all Allocations",
                        tooltip:
                            alreadySplit ?
                            TextContent.NativeAllocationInternalModeDisambiguateAllocationsButtonTooltipHide :
                            TextContent.NativeAllocationInternalModeDisambiguateAllocationsButtonTooltipReveal,
                        SelectedItemDynamicElementOptions.Button, () =>
                        {
                            ToggleNativeRootReferenceToListForDisseminationOfAllocations(source, !alreadySplit);
                            // refresh the UI
                            m_UI.Clear();
                            HandleNativeRootReferenceDetails(source, fallbackName, fallbackDescription, childCount);
                        });
            }
        }

        static void GetRootReferenceName(CachedSnapshot snapshot, SourceIndex sourceIndex, out string areaName, out string objectName)
        {
            areaName = "";
            objectName = "";
            if (sourceIndex.Id != SourceIndex.SourceId.NativeRootReference)
                return;
            areaName = snapshot.NativeRootReferences.AreaName[sourceIndex.Index];
            objectName = snapshot.NativeRootReferences.ObjectName[sourceIndex.Index];
        }

        void ToggleNativeRootReferenceToListForDisseminationOfAllocations(CachedSnapshot.SourceIndex source, bool addToListToSplit)
        {
            var list = new List<string>(MemoryProfilerSettings.AllocationRootsToSplit);
            GetRootReferenceName(m_CachedSnapshot, source, out var areaName, out var objectName);
            var areaAndObjectName = $"{areaName}:{objectName}";
            if (addToListToSplit)
            {
                if (list.Contains(areaAndObjectName))
                    return; // Don't add it again
                list.Add(areaAndObjectName);
            }
            else
                list.Remove(areaAndObjectName);
            MemoryProfilerSettings.AllocationRootsToSplit = list.ToArray();
        }

        internal void HandleInvalidObjectDetails(UnifiedType type, out string statusSummary)
        {
            statusSummary = "Invalid Object";
            m_UI.SetItemName("Invalid Object");

            m_UI.AddInfoBox(SelectedItemDetailsPanel.GroupNameCallStacks, new InfoBox()
            {
                IssueLevel = (InfoBox.IssueType)SnapshotIssuesModel.IssueLevel.Info,
                Message = TextContent.InvalidObjectErrorBoxMessage,
            });
        }

        internal void HandlePureCSharpObjectDetails(UnifiedType type)
        {
            // Pure C# Type Objects
            m_UI.SetItemName(m_CurrentSelectionObjectData, type);

            var managedObjectInfo = m_CurrentSelectionObjectData.GetManagedObject(m_CachedSnapshot);
            var managedSize = managedObjectInfo.Size;
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Managed Size", EditorUtility.FormatBytes(managedSize), $"{managedSize:N0} B");
            if (m_CurrentSelectionObjectData.dataType == ObjectDataType.Array)
            {
                var arrayInfo = m_CurrentSelectionObjectData.GetArrayInfo(m_CachedSnapshot);
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Length", arrayInfo.Length.ToString());
                if (arrayInfo.Rank.Length > 1)
                {
                    m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Rank", m_CurrentSelectionObjectData.GenerateArrayDescription(m_CachedSnapshot, includeTypeName: false));
                    for (int i = 0; i < arrayInfo.Rank.Length; i++)
                    {
                        if (arrayInfo.Rank[i] == 0)
                        {
                            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Potential Logic Flaw?", "This multidimensional array has a zero sized dimension and therefore no elements. Is this intended?");
                            break;
                        }
                    }
                }
            }
            else if (type.ManagedTypeIndex == m_CachedSnapshot.TypeDescriptions.ITypeString)
            {
                var str = StringTools.ReadString(managedObjectInfo.data, out var fullLength, m_CachedSnapshot.VirtualMachineInformation);
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Length", fullLength.ToString());
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "String Value", $"\"{str}\"", options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel | SelectedItemDynamicElementOptions.ShowTitle);
            }
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Referenced By", managedObjectInfo.RefCount.ToString());
            m_UI.SetManagedObjectInspector(m_CurrentSelectionObjectData);

            var references = ObjectConnection.GetAllReferencingObjects(m_CachedSnapshot,
                ObjectData.FromManagedObjectInfo(m_CachedSnapshot, managedObjectInfo));
            // GCHandles that do not belong to a Native Object increase the RefCount without adding an entry to the references list.
            if (managedObjectInfo.RefCount > references.Length)
            {
                bool heldByGCHandle = false;
                for (long i = 0; i < m_CachedSnapshot.GcHandles.Count; i++)
                {
                    if (m_CachedSnapshot.GcHandles.Target[i] == managedObjectInfo.PtrObject)
                    {
                        heldByGCHandle = true;
                        break;
                    }
                }
                if (heldByGCHandle)
                {
                    if (m_CurrentSelectionObjectData.dataType == ObjectDataType.Array && m_CurrentSelectionObjectData.GetArrayInfo(m_CachedSnapshot).Length == 0 &&
                        m_CurrentSelectionObjectData.GetArrayInfo(m_CachedSnapshot).Rank.Length == 1 &&
                        m_CachedSnapshot.TypeDescriptions.TypeDescriptionName[m_CurrentSelectionObjectData.managedTypeIndex].StartsWith("Unity"))
                    {
                        m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, TextContent.UsedByNativeCodeStatus, TextContent.UsedByNativeCodeHint);
                        m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, TextContent.UsedByNativeCodeHint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
                    }
                    else
                    {
                        m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, TextContent.HeldByGCHandleStatus, TextContent.HeldByGCHandleHint);
                        m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, TextContent.HeldByGCHandleHint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
                    }
                }
                else
                {
                    // This has to be an issue in the Managed Crawler, the UI or some other processing of the snapshot,
                    // as Objects without a GCHandle or a reference to them aren't found by the Managed Crawler.
                    // If we eventually attempt to read "empty" managed heap space, we may find Managed Objects
                    // with no references that are about to be collected (outside of a Free Block), or already collected (inside a Free Block).
                    // TODO: adjust this logic when that happens and make sure to mark or list these objects somehow/somewhere so we can exclude them here
                    m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, TextContent.UnkownLivenessReasonStatus, TextContent.UnkownLivenessReasonHint);
                    m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, TextContent.UnkownLivenessReasonHint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
                }
            }

            var objectAddress = m_CurrentSelectionObjectData.GetObjectPointer(m_CachedSnapshot, false);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, "Managed Address", DetailFormatter.FormatPointer(objectAddress));
        }

        internal void HandleUnityObjectDetails(UnifiedUnityObjectInfo selectedUnityObject)
        {
            m_UI.SetItemName(selectedUnityObject);

            // Unity Objects
            if (selectedUnityObject.IsFullUnityObjet)
            {
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Size",
                    $"{EditorUtility.FormatBytes((long)selectedUnityObject.TotalSize)} ({EditorUtility.FormatBytes((long)selectedUnityObject.NativeSize)} Native + {EditorUtility.FormatBytes((long)selectedUnityObject.ManagedSize)} Managed) ",
                    $"{selectedUnityObject.TotalSize:N0} B ({selectedUnityObject.NativeSize:N0} B Native + {selectedUnityObject.ManagedSize:N0} B Managed) ");
            }
            else
            {
                if (selectedUnityObject.HasNativeSide)
                    m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Native Size", EditorUtility.FormatBytes((long)selectedUnityObject.NativeSize),
                        $"{selectedUnityObject.NativeSize:N0} B");
                else
                    m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Managed Size", EditorUtility.FormatBytes(selectedUnityObject.ManagedSize),
                        $"{selectedUnityObject.ManagedSize:N0} B");
            }

            var refCountExtra = (selectedUnityObject.IsFullUnityObjet && selectedUnityObject.TotalRefCount > 0) ? $"({selectedUnityObject.NativeRefCount} Native + {selectedUnityObject.ManagedRefCount} Managed)" : string.Empty;
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Referenced By", $"{selectedUnityObject.TotalRefCount} {refCountExtra}{(selectedUnityObject.IsFullUnityObjet ? " + 2 Self References" : "")}");

            if (selectedUnityObject.IsFullUnityObjet && !selectedUnityObject.ManagedObjectData.IsValid)
            {
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Bug!", "This Native Object is associated with an invalid Managed Object, " + TextContent.InvalidObjectPleaseReportABugMessage);
            }

            if (MetaDataHelpers.GenerateMetaDataString(m_CachedSnapshot, selectedUnityObject.NativeObjectIndex, out var metaData))
            {
                foreach (var tuple in metaData)
                {
                    if (tuple.Item1 == "Warning")
                    {
                        var infoBox = new InfoBox();
                        infoBox.IssueLevel = (InfoBox.IssueType)SnapshotIssuesModel.IssueLevel.Warning;
                        infoBox.Message = tuple.Item2;
                        m_UI.AddInfoBox(SelectedItemDetailsPanel.GroupNameMetaData, infoBox);
                    }
                    else
                    {
                        m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameMetaData, tuple.Item1, tuple.Item2, "");
                    }
                }
            }

            // Debug info
            if (selectedUnityObject.HasNativeSide) m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, "Instance ID", selectedUnityObject.InstanceId.ToString());
            if (selectedUnityObject.HasNativeSide)
            {
                var flagsLabel = "";
                var flagsTooltip = "";
                var hideFlagsLabel = "";
                var hideFlagsTooltip = "";
                PathsToRoot.PathsToRootDetailTreeViewItem.GetObjectFlagsStrings(selectedUnityObject.NativeObjectData, m_CachedSnapshot,
                    ref flagsLabel, ref flagsTooltip,
                    ref hideFlagsLabel, ref hideFlagsTooltip, false);
                if (string.IsNullOrEmpty(flagsLabel))
                    flagsLabel = "None";
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, "Flags", flagsLabel, flagsTooltip);
                if (string.IsNullOrEmpty(hideFlagsLabel))
                    hideFlagsLabel = "None";
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, "HideFlags", hideFlagsLabel, hideFlagsTooltip);
            }

            if (selectedUnityObject.HasNativeSide) m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, "Native Address", DetailFormatter.FormatPointer(selectedUnityObject.NativeObjectData.GetObjectPointer(m_CachedSnapshot, false)));
            if (selectedUnityObject.HasManagedSide) m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, "Managed Address", DetailFormatter.FormatPointer(selectedUnityObject.ManagedObjectData.GetObjectPointer(m_CachedSnapshot, false)));

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameDebug, "Selected Index", $"U:{m_CurrentSelectionIdx}");
            if (selectedUnityObject.HasNativeSide) m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameDebug, "Native Index", $"U:{selectedUnityObject.NativeObjectData.GetUnifiedObjectIndex(m_CachedSnapshot)} N:{selectedUnityObject.NativeObjectData.nativeObjectIndex}");
            if (selectedUnityObject.HasManagedSide) m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameDebug, "Managed Index", $"U:{selectedUnityObject.ManagedObjectData.GetUnifiedObjectIndex(m_CachedSnapshot)} M:{selectedUnityObject.ManagedObjectData.GetManagedObjectIndex(m_CachedSnapshot)}");

            UpdateStatusAndHint(selectedUnityObject);

            if (selectedUnityObject.IsFullUnityObjet)
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, "Self References", "The Managed and Native parts of this UnityEngine.Object reference each other. This is normal."
                    + (selectedUnityObject.TotalRefCount == 0 ? " Nothing else references them though so the Native part keeps the Managed part alive." : ""));

            if (selectedUnityObject.HasManagedSide)
            {
                m_UI.SetManagedObjectInspector(selectedUnityObject.ManagedObjectData);
            }
            if (selectedUnityObject.HasNativeSide && m_CachedSnapshot.NativeCallstackSymbols.Count > 0)
                AddCallStacksInfoToUI(new SourceIndex(SourceIndex.SourceId.NativeObject, selectedUnityObject.NativeObjectIndex));
        }

        void AddCallStacksInfoToUI(SourceIndex sourceIndex)
        {
            var rootId = sourceIndex.Id switch
            {
                SourceIndex.SourceId.NativeObject => m_CachedSnapshot.NativeObjects.RootReferenceId[sourceIndex.Index],
                SourceIndex.SourceId.NativeAllocation => m_CachedSnapshot.NativeAllocations.RootReferenceId[sourceIndex.Index],
                SourceIndex.SourceId.GfxResource => m_CachedSnapshot.NativeGfxResourceReferences.RootId[sourceIndex.Index],
                SourceIndex.SourceId.NativeRootReference => sourceIndex.Index,
                _ => throw new NotImplementedException()
            };
            var areaAndObjectName = m_CachedSnapshot.NativeAllocations.ProduceAllocationNameForRootReferenceId(m_CachedSnapshot, rootId, higlevelObjectNameOnlyIfAvailable: false);
            var callstackCount = 0L;
            var furthercallstackCount = 0L;
            var allocationCount = 0L;

            List<(string, string, string)> BuildCallStackTexts(long maxEntries, out long callstackCount, out long furthercallstackCount, out long allocationCount, bool forCopy = false)
            {
                var callStacks = new DynamicArray<CachedSnapshot.NativeAllocationSiteEntriesCache.CallStackInfo>(10, Collections.Allocator.Temp);
                callStacks.Clear(false);
                callstackCount = furthercallstackCount = allocationCount = 0;

                var callStackTexts = new List<(string, string, string)>((int)maxEntries);
                BuildCallStackInfo(
                    ref callStackTexts, ref allocationCount, ref callstackCount, ref furthercallstackCount, ref callStacks,
                    sourceIndex, areaAndObjectName, maxUniqueEntries: maxEntries,
                    clickableCallStacks: forCopy ? false : MemoryProfilerSettings.ClickableCallStacks,
                    simplifyCallStacks: forCopy ? false : MemoryProfilerSettings.AddressInCallStacks);

                callStacks.Dispose();
                return callStackTexts;
            }

            var callStackTexts = BuildCallStackTexts(10, out callstackCount, out furthercallstackCount, out allocationCount);
            if (callstackCount == 0)
                return;

            void CopyAllCallStacksToClipboard(List<(string, string, string)> callStackTexts)
            {
                StringBuilder stringBuilder = new StringBuilder();
                foreach (var item in callStackTexts)
                {
                    stringBuilder.AppendFormat("{0}\n{1}{2}\n\n", item.Item1, item.Item2, item.Item3);
                }
                EditorGUIUtility.systemCopyBuffer = stringBuilder.ToString();
            }

            var copyButtonText = $"Copy {(furthercallstackCount > 0 ? "First " : (callstackCount > 1 ? "All " : ""))}{callstackCount} Call Stack{(callstackCount > 1 ? "s" : "")}";

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks, copyButtonText, copyButtonText, copyButtonText + " to the clipboard.",
                SelectedItemDynamicElementOptions.Button, () => CopyAllCallStacksToClipboard(BuildCallStackTexts(callstackCount, out var _, out var _, out var _, forCopy: true)));

            if (furthercallstackCount > 0)
            {
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks, $"Copy All Call Stacks", "Copy All Call Stacks", $"Copy All {callstackCount + furthercallstackCount} Call Stacks to the clipboard.",
                    SelectedItemDynamicElementOptions.Button, () => CopyAllCallStacksToClipboard(BuildCallStackTexts(callstackCount + furthercallstackCount, out var _, out var _, out var _, forCopy: true)));

                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks, $"Further Call Stacks", furthercallstackCount.ToString());
            }

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks, "Allocations Count", allocationCount.ToString());
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks, $"{(furthercallstackCount > 0 ? "Shown " : "")}Call Stacks", callstackCount.ToString());

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks, "Clickable Call Stacks", "Clickable Call Stacks",
                "Call Stacks can either be clickable (leading to the source file) or selectable. Toggle this off if you want them to be selectable.",
                SelectedItemDynamicElementOptions.Toggle | (MemoryProfilerSettings.ClickableCallStacks ? SelectedItemDynamicElementOptions.ToggleOn : 0), () =>
               {
                   MemoryProfilerSettings.ClickableCallStacks = !MemoryProfilerSettings.ClickableCallStacks;
                   m_UI.ClearGroup(SelectedItemDetailsPanel.GroupNameCallStacks);
                   AddCallStacksInfoToUI(sourceIndex);
               });

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks, "Show Address in Call Stacks", "Show Address in Call Stacks",
                "Show or hide Address in Call Stacks.",
                SelectedItemDynamicElementOptions.Toggle | (MemoryProfilerSettings.AddressInCallStacks ? SelectedItemDynamicElementOptions.ToggleOn : 0), () =>
               {
                   MemoryProfilerSettings.AddressInCallStacks = !MemoryProfilerSettings.AddressInCallStacks;
                   m_UI.ClearGroup(SelectedItemDetailsPanel.GroupNameCallStacks);
                   AddCallStacksInfoToUI(sourceIndex);
               });


            const bool k_UseFullDetailsPanelWidth = true;
            foreach (var text in callStackTexts)
            {
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks,
                    text.Item1, k_UseFullDetailsPanelWidth ? $"{text.Item2}{text.Item3}" : text.Item2 + text.Item3, options:
                    (k_UseFullDetailsPanelWidth ? 0 : SelectedItemDynamicElementOptions.ShowTitle) | SelectedItemDynamicElementOptions.SubFoldout |
                    // TextField (which enables Selectable Label) does not properly support rich text, so these options are mutually exclusive
                    (MemoryProfilerSettings.ClickableCallStacks ? SelectedItemDynamicElementOptions.EnableRichText : SelectedItemDynamicElementOptions.SelectableLabel)
                    );
            }

        }

        /// <summary>
        /// Builds the call stack info for the given rootId and adds it to the given lists.
        /// </summary>
        /// <param name="callStackTexts"></param>
        /// <param name="allocationCount"></param>
        /// <param name="callstackCount"></param>
        /// <param name="furtherCallstacks">pass -1 if you don't care to know, though then the <paramref name="allocationCount"/> might also be under reported</param>
        /// <param name="callStacks"></param>
        /// <param name="rootId"></param>
        /// <param name="areaAndObjectName"></param>
        /// <param name="startIndex"></param>
        void BuildCallStackInfo(ref List<(string, string, string)> callStackTexts, ref long allocationCount,
            ref long callstackCount, ref long furtherCallstacks,
            ref DynamicArray<CachedSnapshot.NativeAllocationSiteEntriesCache.CallStackInfo> callStacks, SourceIndex sourceIndex,
            string areaAndObjectName, long startIndex = 0, long maxUniqueEntries = 10, bool clickableCallStacks = true, bool simplifyCallStacks = true)
        {
            var rootId = sourceIndex.Id switch
            {
                SourceIndex.SourceId.NativeObject => m_CachedSnapshot.NativeObjects.RootReferenceId[sourceIndex.Index],
                SourceIndex.SourceId.NativeAllocation => m_CachedSnapshot.NativeAllocations.RootReferenceId[sourceIndex.Index],
                SourceIndex.SourceId.GfxResource => m_CachedSnapshot.NativeGfxResourceReferences.RootId[sourceIndex.Index],
                SourceIndex.SourceId.NativeRootReference => sourceIndex.Index,
                _ => throw new NotImplementedException()
            };

            if (sourceIndex.Id == SourceIndex.SourceId.NativeAllocation)
            {
                BuildCallStackInfo(ref callStackTexts, ref allocationCount, ref callstackCount, ref furtherCallstacks,
                    ref callStacks, sourceIndex.Index, areaAndObjectName,
                    maxUniqueEntries, clickableCallStacks, simplifyCallStacks);
                return;
            }

            for (long i = startIndex; i < m_CachedSnapshot.NativeAllocations.RootReferenceId.Count; i++)
            {
                if (m_CachedSnapshot.NativeAllocations.RootReferenceId[i] == rootId)
                {
                    var continueBuilding = BuildCallStackInfo(ref callStackTexts, ref allocationCount, ref callstackCount, ref furtherCallstacks,
                        ref callStacks, i, areaAndObjectName,
                        maxUniqueEntries, clickableCallStacks, simplifyCallStacks);
                    if (!continueBuilding)
                        break;
                }
            }
        }

        /// <summary>
        /// Builds the call stack info for the given rootId and adds it to the given lists.
        /// </summary>
        /// <param name="callStackTexts"></param>
        /// <param name="allocationCount"></param>
        /// <param name="callstackCount"></param>
        /// <param name="furtherCallstacks">pass -1 if you don't care to know, though then the <paramref name="allocationCount"/> might also be under reported</param>
        /// <param name="callStacks"></param>
        /// <param name="rootId"></param>
        /// <param name="areaAndObjectName"></param>
        /// <param name="startIndex"></param>
        /// <returns> Returns false if <paramref name="maxUniqueEntries"/> has been reached and further allocations should not be examined
        /// (e.g. for gathering the <paramref name="furtherCallstacks"/> count) at this time. </returns>
        bool BuildCallStackInfo(ref List<(string, string, string)> callStackTexts, ref long allocationCount,
            ref long callstackCount, ref long furtherCallstacks,
            ref DynamicArray<CachedSnapshot.NativeAllocationSiteEntriesCache.CallStackInfo> callStacks, long nativeAllocationIndex,
            string areaAndObjectName, long maxUniqueEntries = 10, bool clickableCallStacks = true, bool simplifyCallStacks = true)
        {
            ++allocationCount;
            var siteId = m_CachedSnapshot.NativeAllocations.AllocationSiteId[nativeAllocationIndex];
            if (siteId == CachedSnapshot.NativeAllocationSiteEntriesCache.SiteIdNullPointer)
                return true;
            var callstackInfo = m_CachedSnapshot.NativeAllocationSites.GetCallStackInfo(siteId);
            if (!callstackInfo.Valid)
                return true;
            var address = m_CachedSnapshot.NativeAllocations.Address[nativeAllocationIndex];
            var regionIndex = m_CachedSnapshot.NativeAllocations.MemoryRegionIndex[nativeAllocationIndex];
            var region = m_CachedSnapshot.NativeMemoryRegions.MemoryRegionName[regionIndex];
            var isNew = true;
            for (long j = 0; j < callStacks.Count; j++)
            {
                if (callStacks[j].Equals(callstackInfo))
                {
                    var texts = callStackTexts[(int)j];
                    texts.Item2 += $"\nAnd Allocation {DetailFormatter.FormatPointer(address)} made in {region} : {areaAndObjectName}";
                    callStackTexts[(int)j] = texts;
                    isNew = false;
                    break;
                }
            }
            if (!isNew)
                return true;

            if (callStackTexts.Count >= maxUniqueEntries)
            {
                if (furtherCallstacks == -1)
                {
                    --allocationCount;
                    return false;
                }
                ++furtherCallstacks;
                return true;
            }
            var callstack = m_CachedSnapshot.NativeAllocationSites.GetReadableCallstack(
                m_CachedSnapshot.NativeCallstackSymbols, callstackInfo.Index, simplifyCallStacks: simplifyCallStacks, clickableCallStacks: clickableCallStacks);
            if (!string.IsNullOrEmpty(callstack))
            {
                callStackTexts.Add((
                    $"Call Stack {++callstackCount}",
                    $"Allocation {DetailFormatter.FormatPointer(address)} made in {region} : {areaAndObjectName}",
                    $"\n\n{callstack}"));
            }
            return true;
        }


        internal void HandleGroupDetails(string title, string description)
        {
            m_UI.SetItemName(title);
            m_UI.SetDescription(description);
        }

        void UpdateStatusAndHint(UnifiedUnityObjectInfo m_SelectedUnityObject)
        {
            if (m_SelectedUnityObject.IsLeakedShell)
            {
                UpdateStatusAndHintForLeakedShellObject(m_SelectedUnityObject);
            }
            else if (m_SelectedUnityObject.IsAssetObject && !m_SelectedUnityObject.IsPersistentAsset)
            {
                UpdateStatusAndHintForDynamicAssets(m_SelectedUnityObject);
            }
            else if (m_SelectedUnityObject.IsPersistentAsset)
            {
                UpdateStatusAndHintForPersistentAssets(m_SelectedUnityObject);
            }
            else if (m_SelectedUnityObject.IsSceneObject)
            {
                UpdateStatusAndHintForSceneObjects(m_SelectedUnityObject);
            }
            else if (m_SelectedUnityObject.IsManager)
            {
                UpdateStatusAndHintForManagers(m_SelectedUnityObject);
            }
        }

        void UpdateStatusAndHintForLeakedShellObject(UnifiedUnityObjectInfo selectedUnityObject)
        {
            var statusSummary = (selectedUnityObject.ManagedRefCount > 0 ? "Referenced " : "GC.Collect()-able ") +
                $"{TextContent.LeakedManagedShellName} of " +
                (selectedUnityObject.IsSceneObject ? "a Scene Object" : "an Asset");

            var hint = $"This Unity Object is a {TextContent.LeakedManagedShellName}. That means this object's type derives from UnityEngine.Object " +
                "and the object therefore, normally, has a Native Object accompanying it. " +
                "If it is used by Managed (C#) Code, a Managed Shell Object is created to allow access to the Native Object. " +
                "In this case, the Native Object has been destroyed, either via Destroy() or because the " +
                (selectedUnityObject.IsSceneObject ? "Scene" : "Asset Bundle") + " it was in was unloaded. " +
                "After the Native Object was destroyed, the Managed Garbage Collector hasn't yet collected this object. " +
                (selectedUnityObject.ManagedRefCount > 0
                    ? "This is because the Managed Shell is still being referenced and can therefore not yet be collected. " +
                    "You can fix this by explicitly setting each field referencing this Object to null (comparing it to null will claim it already is null, as it acts as a \"Fake Null\" object), " +
                    "or by ensuring that each referencing object is no longer referenced itself, so that all of them can be unloaded."
                    : "Nothing is referencing this Object anymore, so it should be collected with the next GC.Collect.");

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, statusSummary, hint);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, hint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
        }

        void UpdateStatusAndHintForDynamicAssets(UnifiedUnityObjectInfo selectedUnityObject)
        {
            var statusSummary = string.Empty;
            if (selectedUnityObject.TotalRefCount > 0)
                statusSummary += "Referenced ";

            // State
            if (selectedUnityObject.IsDontUnload)
                statusSummary += "DontDestroyOnLoad ";
            else if (selectedUnityObject.TotalRefCount == 0)
                statusSummary += "Leaked ";

            // Runtime created or Combined Scene Meshes
            if (selectedUnityObject.IsRuntimeCreated)
                statusSummary += $"{(string.IsNullOrEmpty(statusSummary) ? 'D' : 'd')}ynamically & run-time created ";
            else
                statusSummary += $"{(string.IsNullOrEmpty(statusSummary) ? 'D' : 'd')}ynamically & build-time created ";

            statusSummary += "Asset";

            var newObjectTypeConstruction = "'new " + (selectedUnityObject.Type.HasManagedType ? selectedUnityObject.ManagedTypeName : selectedUnityObject.NativeTypeName) + "()'. ";

            var hint = "This is a dynamically created Asset Type object, that was either Instantiated, implicitly duplicated or explicitly constructed via " +
                newObjectTypeConstruction +
                (selectedUnityObject.IsDontUnload ? "It is marked as 'DontDestroyOnLoad', so that it will never be unloaded by a Scene unload or an explicit call to 'Resources.UnloadUnusedAssets()'." +
                    " If you want to get rid of it, you will need to call 'Destroy()' on it or not mark it as 'DontDestroyOnLoad'"
                    : ((selectedUnityObject.TotalRefCount > 0 ? "It is still referenced, but if it should no longer be used, it will need to be unloaded explicitly by calling 'Destroy()' on it or "
                        : "This object is not referenced anymore. It is therefore leaked! ") +
                        "Remember to unload these objects by explicitly calling 'Destroy()' on them, or via the more costly and broad sweeping indirect method of " +
                        k_TriggerAssetGCHint)
                );

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, statusSummary, hint);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, hint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);

            if (selectedUnityObject.TotalRefCount == 0 && !selectedUnityObject.IsDontUnload
                && selectedUnityObject.IsRuntimeCreated && string.IsNullOrEmpty(selectedUnityObject.NativeObjectName))
            {
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, "Tip", "This leaked dynamically created Asset doesn't have a name, which will make it harder to find its source. " +
                    "As a first step, search your entire project code for any instances of " + newObjectTypeConstruction +
                    " and every 'Instantiate()' or similar call that would create an instance of this type, " +
                    "and make sure you set the '.name' property of the resulting object to something that will make it easier to understand what this object is being created for.");
            }
        }

        void UpdateStatusAndHintForPersistentAssets(UnifiedUnityObjectInfo selectedUnityObject)
        {
            var statusSummary = (selectedUnityObject.TotalRefCount > 0 ? "Used " : "Unused ") +
                (selectedUnityObject.IsDontUnload ? "DontDestroyOnLoad " : string.Empty) +
                (selectedUnityObject.IsRuntimeCreated ? "Runtime Created " : "Loaded ") +
                "Asset";

            string hint;
            if (selectedUnityObject.IsRuntimeCreated)
                hint = "This is an Asset that was created at runtime and later associated with a file.";
            else
            {
                if (selectedUnityObject.TotalRefCount > 0)
                {
                    hint = "This is an Asset that is used by something in your Application. " +
                        "If you didn't expect to see this Asset at this point in your application's lifetime, check the References panel to see what is using it. " +
                        "If you expected it to be smaller, check the Asset's Import settings.";
                }
                else if (selectedUnityObject.IsDontUnload)
                {
                    hint = "This is an Asset that appears to no longer be used by anything in your Application but his held in Memory because it is marked as 'DontDestroyOnLoad'. " +
                        "To unload it, you need to call 'Destroy()' on it or not mark it as 'DontDestroyOnLoad'";
                }
                else
                {
                    hint = "This is an Asset that appears to no longer be used by anything in your Application. " +
                        "It may have been used earlier but now is just waiting for the next sweep of 'Resources.UnloadUnusedAssets()' to unload it. You can test that hypothesis by " +
                        k_TriggerAssetGCHint;
                }
            }

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, statusSummary, hint);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, hint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
        }

        void UpdateStatusAndHintForSceneObjects(UnifiedUnityObjectInfo selectedUnityObject)
        {
            var statusSummary = (selectedUnityObject.IsRuntimeCreated ? "Runtime Created " : "Loaded ") +
                (selectedUnityObject.IsDontUnload ? "DontDestroyOnLoad " : string.Empty) +
                "Scene Object";

            var hint = "This is a Scene Object, i.e. a GameObject or a Component on it. " +
                (selectedUnityObject.IsRuntimeCreated ? "It was instantiated after the Scene was loaded. " : "It was loaded in as part of a Scene. ") +
                (selectedUnityObject.IsDontUnload ? "It is marked as 'DontDestroyOnLoad' so to unload it, you would need to call 'Destroy()' on it, or not mark it as 'DontDestroyOnLoad'" :
                    "Its Native Memory will be unloaded once the Scene it resides in is unloaded or the GameObject " +
                    (selectedUnityObject.IsGameObject ? "" : "it is attached to ") +
                    "or its parents are destroyed via 'Destroy()'. " +
                    (selectedUnityObject.HasManagedSide ? "Its Managed memory may live on as a Leaked Shell Object if something else that was not unloaded with it still references it." : ""));

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, statusSummary, hint);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, hint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
        }

        void UpdateStatusAndHintForManagers(UnifiedUnityObjectInfo selectedUnityObject)
        {
            var statusSummary = "Native Manager";
            var hint = "This is Native Manager that is represents one of Unity's subsystems.";

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, statusSummary, hint);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, hint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
        }

        /***
         * Current List of Statuses:
         * - "Native Manager"
         * - "Referenced Leaked Managed Shell of a Scene Object"
         * - "Referenced Leaked Managed Shell of an Asset"
         *
         * - "Dynamically & run-time created Asset"
         * - "Dynamically & build-time created Asset"
         * - "Referenced dynamically & run-time created Asset"
         * - "Referenced dynamically & build-time created Asset "
         * - "Referenced DontDestroyOnLoad dynamically & run-time created Asset"
         * - "Referenced DontDestroyOnLoad dynamically & build-time created Asset"
         * - "Referenced Leaked dynamically & run-time created Asset"
         * - "Referenced Leaked dynamically & build-time created Asset"
         *
         * - "Used Runtime Created Asset"
         * - "Used Loaded Asset"
         * - "Used DontDestroyOnLoad Runtime Created Asset"
         * - "Used DontDestroyOnLoad Loaded Asset"
         * - "Unused Runtime Created Loaded Asset"
         * - "Unused Loaded Asset"
         * - "Unused DontDestroyOnLoad Runtime Created Loaded Asset"
         * - "Unused DontDestroyOnLoad Loaded Asset"
         *
         * - "Runtime Created Scene Object"
         * - "Runtime Created DontDestroyOnLoad Scene Object"
         * - "Loaded Scene Object"
         * - "Loaded DontDestroyOnLoad Scene Object"
         *
         * - "Empty Array Required by Unity's subsystems"
         *   - "This array's Type is marked with a [UsedByNativeCode] or [RequiredByNativeCode] Attribute in the Unity code-base and the array exists so that the Type is not compiled out on build. It is held in memory via a GCHandle. You can search the public C# reference repository for those attributes https://github.com/Unity-Technologies/UnityCsReference/."
         * - "Held Alive By GCHandle"
         *   - "This Object is pinned or otherwise held in memory because a GCHandle was allocated for it."
         * - "Bug: Liveness Reason Unknown"
         *   - "There is no reference pointing to this object and no GCHandle reported for it. This is a Bug, please report it using 'Help > Report a Bug' and attach the snapshot to the report."
         *
         *
         * Theoretically possible in the future but not yet found by the memory profiler:
         * - "GC.Collect()-able Leaked Managed Shell of a Scene Object"
         * - "GC.Collect()-able Leaked Managed Shell of an Asset"
         *
         */
    }
}

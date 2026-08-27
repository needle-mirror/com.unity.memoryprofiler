using System;
using System.Collections.Generic;
using System.Text;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class SelectedItemDetailsForTypesAndObjects
    {
        // The unique refcount is currently not flat out available for all objects after crawling and would require further processing, wheras managedObjectInfo.RefCount IS available.
        // We currently don't need the refcount at scale, e.g. in a table column, but just in the Selected Item details, where it is more helpfull to match the Reference list count above it.
        // Keeping this option open in case this changes and reworking the refcount to work at scale turns out not to scale too well when we do need that refcount statt
        const bool k_UseUniqueRefCountInUI = true;

        ObjectData m_CurrentSelectionObjectData;
        CachedSnapshot m_CachedSnapshot;
        SelectedItemDetailsPanel m_UI;

        string k_StatusLabelText = TextContent.StatusRowLabel;
        string k_HintLabelText = TextContent.HintRowLabel;

        public SelectedItemDetailsForTypesAndObjects(CachedSnapshot snapshot, SelectedItemDetailsPanel detailsUI)
        {
            m_CachedSnapshot = snapshot;
            m_UI = detailsUI;
        }

        public void SetSelection(CachedSnapshot.SourceIndex source, string fallbackName = null, string fallbackDescription = null, long childCount = -1)
        {
            m_CurrentSelectionObjectData = ObjectData.FromSourceLink(m_CachedSnapshot, source);
            var type = UnifiedTypeAndName.GetTypeInfoForObjectData(m_CachedSnapshot, m_CurrentSelectionObjectData);
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
                case SourceIndex.SourceId.GCHandleIndex:
                    HandleGCHandleDetails(source);
                    break;
                case SourceIndex.SourceId.Prefab:
                    HandlePrefabDetails(source);
                    break;
                case SourceIndex.SourceId.Scene:
                    HandleSceneDetails(source);
                    break;
                default:
                    break;
            }
        }

        internal void HandleTypeDetails(UnifiedTypeAndName type)
        {
            if (!type.IsValid)
                return;

            m_UI.SetItemName(type);

            if (type.ManagedTypeData.IsValid)
                m_UI.SetManagedObjectInspector(type.ManagedTypeData);

            m_UI.SetDescription(TextContent.TypeDescriptionText);

            if (type.HasManagedType)
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.ManagedTypeRowLabel, type.ManagedTypeName);
            if (type.HasNativeType)
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.NativeTypeRowLabel, type.NativeTypeName);

            AddMemoryImpactInfo(m_CurrentSelectionObjectData.GetSourceLink(m_CachedSnapshot));
        }

        internal void HandleGCHandleDetails(CachedSnapshot.SourceIndex sourceIndex)
        {
            if (!sourceIndex.Valid)
                return;

            m_UI.SetItemName(sourceIndex);

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, TextContent.GCHandleStatus, TextContent.GCHandleHint);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, TextContent.GCHandleHint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
        }

        internal void HandlePrefabDetails(CachedSnapshot.SourceIndex sourceIndex)
        {
            if (!sourceIndex.Valid)
                return;
            var rootTransform = m_CachedSnapshot.SceneRoots.AllPrefabRootTransformSourceIndices[sourceIndex.Index];
            var rootTrabsformName = rootTransform.GetName(m_CachedSnapshot);
            m_UI.SetItemName(sourceIndex, $"\"{rootTrabsformName}\" : {TextContent.PrefabNameSuffix}");

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.PrefabNameRowLabel, rootTrabsformName);

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, TextContent.PrefabStatus, TextContent.PrefabHint);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, TextContent.PrefabHint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
            AddMemoryImpactInfo(sourceIndex);
        }

        internal void HandleSceneDetails(CachedSnapshot.SourceIndex sourceIndex)
        {
            if (!sourceIndex.Valid)
                return;
            var objData = ObjectData.FromSourceLink(m_CachedSnapshot, sourceIndex);
            var sceneName = sourceIndex.GetName(m_CachedSnapshot);
            m_UI.SetItemName(sourceIndex, $"\"{sceneName}\" : {TextContent.SceneNameSuffix}");

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.SceneNameRowLabel, sceneName);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.ScenePathRowLabel, objData.GetAssetPath(m_CachedSnapshot));

            if (m_CachedSnapshot.SceneRoots.DontDestroyOnLoadSceneIndex == sourceIndex.Index)
            {
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, TextContent.DontDestroyOnLoadSceneStatus, TextContent.DontDestroyOnLoadSceneHint);
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, TextContent.DontDestroyOnLoadSceneHint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
            }
            else
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, TextContent.SceneStatus);

            AddMemoryImpactInfo(sourceIndex);
        }

        internal void HandleObjectDetails(UnifiedTypeAndName type)
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

            var references = new List<ObjectData>();

            var rootId = m_CachedSnapshot.NativeAllocations.RootReferenceId[source.Index];
            if (rootId >= NativeRootReferenceEntriesCache.FirstValidRootId)
            {
                if (m_CachedSnapshot.NativeObjects.RootReferenceIdToIndex.TryGetValue(rootId, out var nativeObjectIndex))
                {
                    m_UI.SetItemName(new SourceIndex(SourceIndex.SourceId.NativeObject, nativeObjectIndex));
                    m_UI.AddInfoBox(SelectedItemDetailsPanel.GroupNameBasic, new InfoBox()
                    {
                        IssueLevel = (InfoBox.IssueType)SnapshotIssuesModel.IssueLevel.Info,
                        Message = TextContent.NativeAllocationRootedByNativeObjectInfoBoxMessage,
                    });

                }
                else if (m_CachedSnapshot.NativeRootReferences.IdToIndex.TryGetValue(rootId, out var nativeRootReferenceIndex))
                {
                    m_UI.SetItemName(new SourceIndex(SourceIndex.SourceId.NativeRootReference, nativeRootReferenceIndex));
                    m_UI.AddInfoBox(SelectedItemDetailsPanel.GroupNameBasic, new InfoBox()
                    {
                        IssueLevel = (InfoBox.IssueType)SnapshotIssuesModel.IssueLevel.Info,
                        Message = TextContent.NativeAllocationRootedByNativeRootReferenceInfoBoxMessage,
                    });
                }
            }

            ObjectConnection.GetAllReferencingObjects(m_CachedSnapshot, source, references);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.NativeSizeRowLabel, EditorUtility.FormatBytes(nativeSize), $"{nativeSize:N0} B");
            if (MemoryProfilerSettings.FeatureFlags.ShowFoundReferencesForNativeAllocations_2024_10)
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.FoundReferencesRowLabel, references.Count.ToString(), TextContent.NativeAllocationFoundReferencesHint);

#if DEBUG_VALIDATION
            // really only useful when debugging the memory profiler
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameDebug, "Allocation Index", source.Index.ToString());
#endif
            if (MemoryProfilerSettings.EnableUnrootedAllocationBreakdown(m_CachedSnapshot)
                && rootId <= NativeRootReferenceEntriesCache.InvalidRootIndex)
            {
                var message = TextContent.UnrootedUnrootedAllocationsErrorBoxMessageInternalMode;
                if (!MemoryProfilerSettings.InternalMode)
                {
                    if (m_CachedSnapshot.MetaData?.UnityVersionEqualOrNewer(6000, 3) ?? false)
                        message = TextContent.UnrootedUnrootedAllocations_6000_3_ErrorBoxMessage;
                    else
                        message = TextContent.UnrootedUnrootedAllocationsErrorBoxMessage;
                }
                m_UI.AddInfoBox(SelectedItemDetailsPanel.GroupNameBasic, new InfoBox()
                {
                    IssueLevel = (InfoBox.IssueType)SnapshotIssuesModel.IssueLevel.Error,
                    Message = message,
                });
            }

            if (m_CachedSnapshot.NativeCallstackSymbols.Count > 0)
            {
                AddCallStacksInfoToUI(source);
            }
            // Give a hint to internal users or those with the potential to get call stacks,
            // but only if they didn't already get the info for Unrooted allocations
            else if (MemoryProfilerSettings.EnableUnrootedAllocationBreakdown(m_CachedSnapshot)
                && m_CachedSnapshot.NativeAllocations.RootReferenceId[source.Index] > 0)
            {
                m_UI.AddInfoBox(SelectedItemDetailsPanel.GroupNameBasic, new InfoBox()
                {
                    IssueLevel = (InfoBox.IssueType)SnapshotIssuesModel.IssueLevel.Info,
                    Message = TextContent.NativeAllocationInternalModeCallStacksInfoBoxMessage,
                });
            }
        }

        void AddCallstacksExportAndTreeViewButtons(long rootReferenceId)
        {
            var exportJsonCallstacksButtonText = TextContent.ExportCallstacksJsonButton;
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks, exportJsonCallstacksButtonText, exportJsonCallstacksButtonText, options: SelectedItemDynamicElementOptions.Button,
                onInteraction: () =>
                {
                    ExportUtility.WriteNativeRootCallstackInfoToJson(m_CachedSnapshot, rootReferenceId);
                });

            var exportJsonInvertedCallstacksButtonText = TextContent.ExportInvertedCallstacksJsonButton;
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks, exportJsonInvertedCallstacksButtonText, exportJsonInvertedCallstacksButtonText, options: SelectedItemDynamicElementOptions.Button,
                onInteraction: () =>
                {
                    ExportUtility.WriteNativeRootCallstackInfoToJson(m_CachedSnapshot, rootReferenceId, invertedCallstacks: true);
                });

            var exportCsvCallstacksButtonText = TextContent.ExportCallstacksCsvButton;
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks, exportCsvCallstacksButtonText, exportCsvCallstacksButtonText, options: SelectedItemDynamicElementOptions.Button,
                onInteraction: () =>
                {
                    ExportUtility.WriteNativeRootCallstackInfoToCsv(m_CachedSnapshot, rootReferenceId);
                });

            var exportCsvInvertedCallstacksButtonText = TextContent.ExportInvertedCallstacksCsvButton;
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks, exportCsvInvertedCallstacksButtonText, exportCsvInvertedCallstacksButtonText, options: SelectedItemDynamicElementOptions.Button,
                onInteraction: () =>
                {
                    ExportUtility.WriteNativeRootCallstackInfoToCsv(m_CachedSnapshot, rootReferenceId, invertedCallstacks: true);
                });

            var openCallstacksButtonText = TextContent.OpenCallstacksTableButton;
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks, openCallstacksButtonText, openCallstacksButtonText, options: SelectedItemDynamicElementOptions.Button,
                onInteraction: () =>
                {
                    ExportUtility.OpenCallstacksWindowForNativeRoot(m_CachedSnapshot, rootReferenceId);
                });

            var openInvertedCallstacksButtonText = TextContent.OpenInvertedCallstacksTableButton;
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks, openInvertedCallstacksButtonText, openInvertedCallstacksButtonText, options: SelectedItemDynamicElementOptions.Button,
                onInteraction: () =>
                {
                    ExportUtility.OpenCallstacksWindowForNativeRoot(m_CachedSnapshot, rootReferenceId, invertedCallstacks: true);
                });
        }

        internal void HandleGfxResourceDetails(CachedSnapshot.SourceIndex source, string fallbackName, string fallbackDescription)
        {
            m_UI.SetItemName(fallbackName);
            var gfxSize = (long)m_CachedSnapshot.NativeGfxResourceReferences.GfxSize[source.Index];
            var rootId = (long)m_CachedSnapshot.NativeGfxResourceReferences.RootId[source.Index];
            var gfxResourceId = (long)m_CachedSnapshot.NativeGfxResourceReferences.GfxResourceId[source.Index];
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.GraphicsSizeRowLabel, EditorUtility.FormatBytes(gfxSize), $"{gfxSize:N0} B");
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, TextContent.RootIdRowLabel, rootId.ToString());
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, TextContent.GfxResourceIdRowLabel, gfxResourceId.ToString());

            if (rootId <= 0)
            {
                m_UI.AddInfoBox(SelectedItemDetailsPanel.GroupNameBasic, new InfoBox()
                {
                    IssueLevel = (InfoBox.IssueType)SnapshotIssuesModel.IssueLevel.Error,
                    Message = TextContent.UnrootedGraphicsResourceErrorBoxMessage,
                });
            }
            else if (m_CachedSnapshot.NativeCallstackSymbols.Count > 0)
            {
                m_UI.AddInfoBox(SelectedItemDetailsPanel.GroupNameCallStacks, new InfoBox()
                {
                    IssueLevel = (InfoBox.IssueType)SnapshotIssuesModel.IssueLevel.Info,
                    Message = TextContent.GraphicsResourceWithSnapshotWithCallStacksInfoBoxMessage,
                });
                AddCallStacksInfoToUI(source);
            }
        }

        internal void HandleNativeRootReferenceDetails(CachedSnapshot.SourceIndex source, string fallbackName, string fallbackDescription, long childCount = -1)
        {
            m_UI.SetItemName(source);
            GetRootReferenceName(m_CachedSnapshot, source, out var areaName, out var objectName);

            if (!string.IsNullOrEmpty(areaName))
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.AreaRowLabel, areaName);

            if (!string.IsNullOrEmpty(objectName))
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.ObjectNameRowLabel, objectName);

            var accumultatedSize = (long)m_CachedSnapshot.NativeRootReferences.AccumulatedSize[source.Index];
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.SizeRowLabel, EditorUtility.FormatBytes(accumultatedSize), $"{accumultatedSize:N0} B");

            if (childCount > 0)
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.ChildCountRowLabel, childCount.ToString());

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
                        alreadySplit ? TextContent.HideAllocationListButton : TextContent.ListAllAllocationsButton,
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

        internal void HandleInvalidObjectDetails(UnifiedTypeAndName type, out string statusSummary)
        {
            statusSummary = TextContent.InvalidObjectLabel;
            m_UI.SetItemName(TextContent.InvalidObjectLabel);

            m_UI.AddInfoBox(SelectedItemDetailsPanel.GroupNameCallStacks, new InfoBox()
            {
                IssueLevel = (InfoBox.IssueType)SnapshotIssuesModel.IssueLevel.Info,
                Message = TextContent.InvalidObjectErrorBoxMessage,
            });
        }

        void AddMemoryImpactInfo(SourceIndex nativeIndex, SourceIndex managedIndex)
        {
            if (!m_CachedSnapshot.RootAndImpactInfo.SuccessfullyBuilt)
            {
                AddMemoryImpactAvailabilityMessage();
                return;
            }

            if (nativeIndex.Valid && managedIndex.Valid)
            {
                // Both of these objects might own some impact of their counterpart so just adding their impacts
                // together would lead to double counting.
                AddMemoryImpactInfo(nativeIndex, TextContent.NativeObjectHeaderForImpactDifferentiator);
                AddMemoryImpactInfo(managedIndex, TextContent.ManagedShellHeaderForImpactDifferentiator);
            }
            else if (nativeIndex.Valid)
            {
                AddMemoryImpactInfo(nativeIndex);
            }
            else if (managedIndex.Valid)
            {
                AddMemoryImpactInfo(managedIndex);
            }
        }

        void AddMemoryImpactAvailabilityMessage()
        {
            if (!m_CachedSnapshot.RootAndImpactInfo.SuccessfullyBuilt)
            {
                // If impact info wasn't built, it means we're on an older snapshot or the build failed.
                // Message that to the user.
                if (m_CachedSnapshot.HasSceneRootsAndAssetbundles)
                {
                    m_UI.AddInfoBox(SelectedItemDetailsPanel.GroupNameImpact, new InfoBox
                    {
                        IssueLevel = InfoBox.IssueType.Info,
                        Message = TextContent.AttributedImpactInfoNotAvailable
                    });
                }
                else
                {
                    m_UI.AddInfoBox(SelectedItemDetailsPanel.GroupNameImpact, new InfoBox
                    {
                        IssueLevel = InfoBox.IssueType.Info,
                        Message = TextContent.AttributedImpactInfoNotAvailableForOldSnapshots
                    });
                }
            }
        }

        void AddMemoryImpactInfo(SourceIndex index, string unityObjectPartDifferentiator = "")
        {
            if (!m_CachedSnapshot.RootAndImpactInfo.SuccessfullyBuilt)
            {
                AddMemoryImpactAvailabilityMessage();
                return;
            }
            var impact =
                SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_CachedSnapshot.RootAndImpactInfo.Impact,
                    index);

            var totalImpact = impact.TotalImpact;
            var totalSum = totalImpact.SumUp();
            var exclusiveSum = impact.Exclusive.SumUp();
            var sharedSum = impact.SharedProRata.SumUp();
            var groupName = SelectedItemDetailsPanel.GroupNameImpact;
            var addLineSpacing = false;
            if (totalSum.Committed > 0)
            {
                if (string.IsNullOrEmpty(unityObjectPartDifferentiator) || index.Id is SourceIndex.SourceId.NativeObject)
                    m_UI.AddInfoBox(SelectedItemDetailsPanel.GroupNameImpact, new InfoBox
                    {
                        IssueLevel = InfoBox.IssueType.Info,
                        Message = TextContent.AttributedImpactInfo
                    });

                if (!string.IsNullOrEmpty(unityObjectPartDifferentiator))
                {
                    groupName = unityObjectPartDifferentiator;
                    m_UI.AddSubGroup(SelectedItemDetailsPanel.GroupNameImpact, groupName);
                }

                if (exclusiveSum.Committed > 0 && sharedSum.Committed > 0)
                {
                    AddMemoryImpactInfo(groupName, index, totalImpact, TextContent.TotalImpactRowLabel, TextContent.AttributedImpactTotalTooltip);
                    addLineSpacing = true;
                }
            }

            if (exclusiveSum.Committed > 0)
            {
                m_UI.AddDynamicElement(groupName, /*string.Empty,*/ $"{(addLineSpacing ? '\n' : "")}<b>{TextContent.ExclusivelyOwnedReferencedMemoryLabel}</b>", null, null, options: SelectedItemDynamicElementOptions.EnableRichText | SelectedItemDynamicElementOptions.ShowTitle);
                AddMemoryImpactInfo(groupName, index, impact.Exclusive, TextContent.OwnedImpactRowLabel, TextContent.AttributedImpactExclusiveTooltip);
                addLineSpacing = true;
            }

            if (sharedSum.Committed > 0)
            {
                m_UI.AddDynamicElement(groupName,  /*string.Empty,*/ $"{(addLineSpacing ? '\n' : "")}<b>{TextContent.SharedReferencedMemoryLabel}</b>", null, options: SelectedItemDynamicElementOptions.EnableRichText | SelectedItemDynamicElementOptions.ShowTitle);
                AddMemoryImpactInfo(groupName, index, impact.SharedProRata, TextContent.SharedImpactRowLabel, TextContent.AttributedImpactSharedTooltip);
            }
        }

        void AddMemoryImpactInfo(string groupName, SourceIndex index, NativeRootSize totalImpact, string summedImpactLabel = null, string tooltip = null)
        {
            summedImpactLabel ??= TextContent.TotalImpactRowLabel;
            tooltip ??= TextContent.AttributedImpactTotalTooltip;
            bool multipleSizesToDisplay = (totalImpact.NativeSize.Committed > 0 ? 1 : 0) + (totalImpact.GfxSize.Committed > 0 ? 1 : 0) + (totalImpact.ManagedSize.Committed > 0 ? 1 : 0) > 1;
            if (multipleSizesToDisplay)
            {
                var total = totalImpact.SumUp();
                AddImpactGroupSizeUILine(groupName, summedImpactLabel, total.Committed, total.Resident, tooltip);
            }
            if (totalImpact.NativeSize.Committed > 0)
            {
                var titleText = MultiValueListLabel(TextContent.NativeImpactRowLabel, multipleSizesToDisplay, isLast: !multipleSizesToDisplay);
                AddImpactGroupSizeUILine(groupName, titleText, totalImpact.NativeSize.Committed, totalImpact.NativeSize.Resident, tooltip);
            }

            if (totalImpact.ManagedSize.Committed > 0)
            {
                string titleText = MultiValueListLabel(TextContent.ManagedImpactRowLabel, hasMultiple: totalImpact.GfxSize.Committed > 0 || totalImpact.NativeSize.Committed > 0, isLast: totalImpact.GfxSize.Committed == 0);
                AddImpactGroupSizeUILine(groupName, titleText, totalImpact.ManagedSize.Committed, totalImpact.ManagedSize.Resident, tooltip);
            }

            if (totalImpact.GfxSize.Committed > 0)
            {
                var titleText = MultiValueListLabel(TextContent.GraphicsImpactRowLabel, multipleSizesToDisplay, isLast: true);
                AddImpactGroupSizeUILine(groupName, titleText, totalImpact.GfxSize.Committed, totalImpact.GfxSize.Resident, tooltip);
            }
        }

        void AddImpactGroupSizeUILine(string groupName, string description, ulong committed, ulong resident, string tooltipMessage = "")
        {
            m_UI.AddDynamicElement(groupName, description, EditorUtility.FormatBytes((long)committed),
                string.Format(TextContent.ImpactTooltipFormat, EditorUtility.FormatBytes((long)committed), committed, EditorUtility.FormatBytes((long)resident), resident, tooltipMessage));
        }

        static string MultiValueListLabel(string title, bool hasMultiple, bool isLast)
        {
            return (hasMultiple ? (isLast ? "└ " : "├ ") + title : title);
        }

        internal void HandlePureCSharpObjectDetails(UnifiedTypeAndName type)
        {
            // Pure C# Type Objects
            m_UI.SetItemName(m_CurrentSelectionObjectData, type);

            var managedObjectInfo = m_CurrentSelectionObjectData.GetManagedObject(m_CachedSnapshot);
            var managedSize = managedObjectInfo.Size;
            var precomputedStatus = m_CachedSnapshot.ObjectStatusComputation.GetStatus(
                new SourceIndex(SourceIndex.SourceId.ManagedObject, managedObjectInfo.ManagedObjectIndex));

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.ManagedSizeRowLabel, EditorUtility.FormatBytes(managedSize), $"{managedSize:N0} B");
            if (m_CurrentSelectionObjectData.dataType == ObjectDataType.Array)
            {
                var arrayInfo = m_CurrentSelectionObjectData.GetArrayInfo(m_CachedSnapshot);
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.LengthRowLabel, arrayInfo.Length.ToString());
                if (arrayInfo.Rank.Length > 1)
                {
                    m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.RankRowLabel, m_CurrentSelectionObjectData.GenerateArrayDescription(m_CachedSnapshot, includeTypeName: false));
                    for (int i = 0; i < arrayInfo.Rank.Length; i++)
                    {
                        if (arrayInfo.Rank[i] == 0)
                        {
                            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.PotentialLogicFlawLabel, TextContent.PotentialLogicFlawContent);
                            break;
                        }
                    }
                }
            }
            else if (type.ManagedTypeIndex == m_CachedSnapshot.TypeDescriptions.ITypeString)
            {
                var str = StringTools.ReadFullStringOrGetPreview(managedObjectInfo, m_CachedSnapshot, out var fullLength, true);

                if (str != null)
                {
                    m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.LengthRowLabel, fullLength.ToString());
                    m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.StringValueRowLabel, $"{str}", options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel | SelectedItemDynamicElementOptions.ShowTitle);
                }
                else
                {
                    // String data is unavailable
                    m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.StringValueRowLabel, TextContent.StringDataUnavailable, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.ShowTitle);
                }
            }
            AddMemoryImpactInfo(m_CurrentSelectionObjectData.GetSourceLink(m_CachedSnapshot));

            GetDetailedManagedReferenceCounts(managedObjectInfo.ManagedObjectIndex, -1, out var uniqueReferenceCount, out var _, out var _, out var repeatRefCount, out var nonUnityObjectGCHandleCount, out _);

            var refCount = k_UseUniqueRefCountInUI ? uniqueReferenceCount : managedObjectInfo.RefCount;

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.ReferencedByRowLabel, refCount.ToString());

            if (!k_UseUniqueRefCountInUI && repeatRefCount > 0)
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.RepeatReferencesLabel, repeatRefCount.ToString(), tooltip: TextContent.RepeatReferencesTooltip);

            if (nonUnityObjectGCHandleCount > 0)
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.NonUnityObjectGCHandleReferencesLabel, nonUnityObjectGCHandleCount.ToString(), tooltip: TextContent.NonUnityObjectGCHandleReferencesTooltip);

            m_UI.SetManagedObjectInspector(m_CurrentSelectionObjectData);

            // GCHandles that do not belong to a Native Object increase the RefCount and adding an entry to the references list.
            // For older patch release versions of Unity there was a timing issue Native Objects could be changed between the moment where GCHandles
            // got reported and where the Native Objects got reported, meaning we would end up with a GCHandle that was technically supposed to belong to a
            // Native Object that since got destroyed. This would now be listed as a non-Native-Object reference in the references table and there is no way
            // to reasonably call that out as a potential bug in the captured runtime version (something that was previously attempted by checking
            // the (non-unique, non-GCHandle-including) references list count against the RefCount, and then reported as UnkownLivenessReasonStatus.
            switch (precomputedStatus)
            {
                case ObjectStatus.UsedByNativeCode:
                case ObjectStatus.GCHandleHeld:
                case ObjectStatus.LeakedShellHeldByGCHandle:
                    UpdateStatusForGCHandleHeldObjectWithoutNativeObject(managedObjectInfo.ManagedObjectIndex, false);
                    break;
                case ObjectStatus.ManagedObjectReferenced:
                    m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, TextContent.ReferencedManagedObjectStatus, TextContent.ReferencedManagedObjectHint);
                    m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, TextContent.ReferencedManagedObjectHint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
                    break;
                case ObjectStatus.UnknownLiveness:
                    m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, TextContent.UnknownLivenessReasonStatus, TextContent.UnknownLivenessReasonHint);
                    m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, TextContent.UnknownLivenessReasonHint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
                    break;
                default:
                    break;
            }

            var objectAddress = m_CurrentSelectionObjectData.GetObjectPointer(m_CachedSnapshot, false);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, TextContent.ManagedAddressRowLabel, DetailFormatter.FormatPointer(objectAddress));
        }

        void GetDetailedManagedReferenceCounts(long managedObjectIndex, long nativeObjectIndex, out long uniqueReferenceCount, out long uniqueManagedReferenceCount, out long uniqueNativeReferenceCount, out long repeatRefCount, out long nonUnityObjectGCHandleCount, out bool heldByGCHandle)
        {
            if (managedObjectIndex < 0)
            {
                uniqueManagedReferenceCount = 0;
                uniqueReferenceCount = uniqueNativeReferenceCount = nativeObjectIndex == NativeObjectEntriesCache.InvalidObjectIndex
                    ? 0
                    : m_CachedSnapshot.NativeObjects.RefCount[nativeObjectIndex];
                repeatRefCount = 0;
                nonUnityObjectGCHandleCount = 0;
                heldByGCHandle = false;
                return;
            }
            var uniqueReferences = new List<ObjectData>();
            ObjectConnection.GetAllReferencingObjects(m_CachedSnapshot,
                new SourceIndex(SourceIndex.SourceId.ManagedObject, managedObjectIndex), uniqueReferences, searchMode: ObjectConnection.UnityObjectReferencesSearchMode.TreatAsOneObject, ignoreRepeatedManagedReferences: true);
            var references = new List<ObjectData>();
            ObjectConnection.GetAllReferencingObjects(m_CachedSnapshot,
                new SourceIndex(SourceIndex.SourceId.ManagedObject, managedObjectIndex), references, searchMode: ObjectConnection.UnityObjectReferencesSearchMode.TreatAsOneObject, ignoreRepeatedManagedReferences: false);

            uniqueReferenceCount = uniqueReferences.Count;
            repeatRefCount = references.Count - uniqueReferenceCount;

            uniqueManagedReferenceCount = uniqueNativeReferenceCount = nonUnityObjectGCHandleCount = 0;
            for (int i = 0; i < uniqueReferenceCount; i++)
            {
                switch (uniqueReferences[i].dataType)
                {
                    case ObjectDataType.Value:
                    case ObjectDataType.Object:
                    case ObjectDataType.Array:
                    case ObjectDataType.BoxedValue:
                    case ObjectDataType.ReferenceObject:
                    case ObjectDataType.ReferenceArray:
                    case ObjectDataType.Type:
                        ++uniqueManagedReferenceCount;
                        break;
                    case ObjectDataType.NativeObject:
                        ++uniqueNativeReferenceCount;
                        break;
                    case ObjectDataType.GCHandle:
                        ++nonUnityObjectGCHandleCount;
                        break;
                    case ObjectDataType.Unknown:
                    case ObjectDataType.NativeAllocation:
                    default:
                        throw new NotImplementedException();
                }
            }
            // The first indices are reserved for objects held by GCHandles (see ManagedData.IsGCHandleTarget).
            heldByGCHandle = m_CachedSnapshot.CrawledData.IsGCHandleTarget(managedObjectIndex, m_CachedSnapshot.GcHandles.UniqueCount);
        }

        private void AddBasicGroupSizeUILine(string description, ulong value, string tooltipMessage = "")
        {
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, description, EditorUtility.FormatBytes((long)value), $"{tooltipMessage} {value:N0} B");
        }

        internal void HandleUnityObjectDetails(UnifiedUnityObjectInfo selectedUnityObject)
        {
            m_UI.SetItemName(selectedUnityObject);
            NativeRootSize rootSize = default;
            if (selectedUnityObject.HasNativeSide)
            {
                var rootId = m_CachedSnapshot.NativeObjects.RootReferenceId[selectedUnityObject.NativeObjectIndex];
                var mappedIndex = m_CachedSnapshot.ProcessedNativeRoots.RootIdToMappedIndex(rootId);
                rootSize = m_CachedSnapshot.ProcessedNativeRoots.Data[mappedIndex].AccumulatedRootSizes;
            }

            var precomputedStatus = selectedUnityObject.HasNativeSide
                ? m_CachedSnapshot.ObjectStatusComputation.GetStatus(
                    new SourceIndex(SourceIndex.SourceId.NativeObject, selectedUnityObject.NativeObjectIndex))
                : ObjectStatus.None;

            var nativeSize = rootSize.NativeSize.Committed;
            var managedSize = selectedUnityObject.HasManagedSide ? selectedUnityObject.ManagedSize : 0;

            var graphicsSize = rootSize.GfxSize.Committed;

            bool multipleSizesToDisplay = (nativeSize > 0 ? 1 : 0) + (graphicsSize > 0 ? 1 : 0) + (managedSize > 0 ? 1 : 0) > 1;
            var totalAllocated = nativeSize;

            if (multipleSizesToDisplay)
            {
                totalAllocated += (ulong)managedSize + graphicsSize;
                AddBasicGroupSizeUILine(TextContent.TotalAllocatedRowLabel, totalAllocated);
            }

            if (nativeSize > 0)
            {
                var titleText = MultiValueListLabel(TextContent.NativeSizeRowLabel, multipleSizesToDisplay, isLast: !multipleSizesToDisplay);
                AddBasicGroupSizeUILine(titleText, nativeSize);
            }

            if (selectedUnityObject.HasManagedSide)
            {
                string titleText = MultiValueListLabel(TextContent.ManagedSizeRowLabel, hasMultiple: graphicsSize > 0 || nativeSize > 0, isLast: graphicsSize == 0);
                AddBasicGroupSizeUILine(titleText, (ulong)selectedUnityObject.ManagedSize);
            }

            if (graphicsSize > 0)
            {
                var titleText = MultiValueListLabel(TextContent.GraphicsSizeRowLabel, multipleSizesToDisplay, isLast: true);
                AddBasicGroupSizeUILine(titleText, graphicsSize);
            }

            var totalResidentSize = rootSize.NativeSize.Resident + rootSize.ManagedSize.Resident;
            if (totalResidentSize > 0)
            {
                AddBasicGroupSizeUILine(TextContent.TotalResidentSizeRowLabel, totalResidentSize);
            }

            if (selectedUnityObject.HasNativeSide)
            {
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.RuntimeEstimationRowLabel,
                    $"{EditorUtility.FormatBytes((long)selectedUnityObject.NativeSize)}",
                    $"{selectedUnityObject.NativeSize:N0} B\n\n{TextContent.RuntimeEstimationTooltip}");
            }

            AddMemoryImpactInfo(selectedUnityObject.HasNativeSide ? new SourceIndex(SourceIndex.SourceId.NativeObject, selectedUnityObject.NativeObjectIndex) : default,
                selectedUnityObject.HasManagedSide ? new SourceIndex(SourceIndex.SourceId.ManagedObject, selectedUnityObject.ManagedObjectIndex) : default);

            GetDetailedManagedReferenceCounts(selectedUnityObject.ManagedObjectIndex, selectedUnityObject.NativeObjectIndex, out var uniqueReferenceCount, out var uniqueManagedReferenceCount, out var uniqueNativeReferenceCount, out var repeatRefCount, out var nonUnityObjectGCHandleCount, out _);

            var refCount = k_UseUniqueRefCountInUI ? uniqueReferenceCount : selectedUnityObject.TotalRefCount;
            var nativeRefCount = k_UseUniqueRefCountInUI ? uniqueNativeReferenceCount : selectedUnityObject.NativeRefCount;
            var managedRefCount = k_UseUniqueRefCountInUI ? uniqueManagedReferenceCount : selectedUnityObject.ManagedRefCount;

            var refCountExtra = (selectedUnityObject.IsFullUnityObject && selectedUnityObject.TotalRefCount > 0) ? string.Format(TextContent.NativeAndManagedRefCountFormat, nativeRefCount, managedRefCount) : string.Empty;
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.ReferencedByRowLabel, $"{refCount} {refCountExtra}{(selectedUnityObject.IsFullUnityObject ? TextContent.SelfReferencesSuffix : "")}");

            if (!k_UseUniqueRefCountInUI && repeatRefCount > 0)
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.RepeatReferencesLabel, repeatRefCount.ToString(), tooltip: TextContent.RepeatReferencesTooltip);

            if (nonUnityObjectGCHandleCount > 0)
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.NonUnityObjectGCHandleReferencesLabel, nonUnityObjectGCHandleCount.ToString(), tooltip: TextContent.NonUnityObjectGCHandleReferencesTooltip);

            if (selectedUnityObject.IsFullUnityObject && !selectedUnityObject.ManagedObjectData.IsValid)
            {
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, TextContent.BugRowLabel, TextContent.InvalidManagedObjectAssociationMessage);
            }

            if (selectedUnityObject.HasNativeSide && MetaDataHelpers.GenerateMetaDataString(m_CachedSnapshot, selectedUnityObject.NativeObjectIndex, out var metaData))
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
            var instanceIdLabel = m_CachedSnapshot.MetaData.UnityVersionEqualOrNewer(6000, 3, 0) ? TextContent.EntityIdRowLabel : TextContent.InstanceIdRowLabel;
            if (selectedUnityObject.HasNativeSide) m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, instanceIdLabel, selectedUnityObject.EntityId.ToString());
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
                    flagsLabel = TextContent.NoneLabel;
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, TextContent.FlagsRowLabel, flagsLabel, flagsTooltip);
                if (string.IsNullOrEmpty(hideFlagsLabel))
                    hideFlagsLabel = TextContent.NoneLabel;
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, TextContent.HideFlagsRowLabel, hideFlagsLabel, hideFlagsTooltip);
            }

            if (selectedUnityObject.HasNativeSide) m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, TextContent.NativeAddressRowLabel, DetailFormatter.FormatPointer(selectedUnityObject.NativeObjectData.GetObjectPointer(m_CachedSnapshot, false)));
            if (selectedUnityObject.HasManagedSide) m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, TextContent.ManagedAddressRowLabel, DetailFormatter.FormatPointer(selectedUnityObject.ManagedObjectData.GetObjectPointer(m_CachedSnapshot, false)));

            if (selectedUnityObject.HasNativeSide) m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameDebug, TextContent.NativeIndexRowLabel, selectedUnityObject.NativeObjectData.nativeObjectIndex.ToString());
            if (selectedUnityObject.HasManagedSide) m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameDebug, TextContent.ManagedIndexRowLabel, selectedUnityObject.ManagedObjectData.GetManagedObjectIndex(m_CachedSnapshot).ToString());

            UpdateStatusAndHint(selectedUnityObject, precomputedStatus);

            if (selectedUnityObject.IsFullUnityObject)
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, TextContent.SelfReferencesRowLabel, TextContent.SelfReferencesHint
                    + (selectedUnityObject.TotalRefCount == 0 ? " " + TextContent.SelfReferencesOnlyConnectionHint : ""));

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

            List<(string, string, string, List<KeyValuePair<int, string>>)> BuildCallStackTexts(long maxEntries, out long callstackCount, out long furthercallstackCount, out long allocationCount, bool forCopy = false)
            {
                var callStacks = new DynamicArray<CachedSnapshot.NativeAllocationSiteEntriesCache.CallStackInfo>(10, Collections.Allocator.Temp);
                callStacks.Clear(false);
                callstackCount = furthercallstackCount = allocationCount = 0;

                var callStackTexts = new List<(string, string, string, List<KeyValuePair<int, string>>)>((int)maxEntries);
                BuildCallStackInfo(
                    ref callStackTexts, ref allocationCount, ref callstackCount, ref furthercallstackCount, ref callStacks,
                    sourceIndex, areaAndObjectName, maxUniqueEntries: maxEntries,
                    clickableCallStacks: forCopy ? false : MemoryProfilerSettings.ClickableCallStacks,
                    simplifyCallStacks: forCopy ? false : !MemoryProfilerSettings.AddressInCallStacks);

                callStacks.Dispose();
                return callStackTexts;
            }

            var callStackTexts = BuildCallStackTexts(10, out callstackCount, out furthercallstackCount, out allocationCount);
            if (callstackCount == 0)
                return;

            void CopyAllCallStacksToClipboard(List<(string, string, string, List<KeyValuePair<int, string>>)> callStackTexts)
            {
                StringBuilder stringBuilder = new StringBuilder();
                foreach (var item in callStackTexts)
                {
                    stringBuilder.AppendFormat("{0}\n{1}{2}\n\n", item.Item1, item.Item2, item.Item3);
                }
                EditorGUIUtility.systemCopyBuffer = stringBuilder.ToString();
            }

            var copyButtonText = furthercallstackCount > 0
                ? string.Format(TextContent.CopyFirstCallStacksButtonFormat, callstackCount)
                : string.Format(TextContent.CopyCallStackButtonFormat, callstackCount);
            var copyButtonTooltip = string.Format(TextContent.CopyCallStacksTooltipFormat, callstackCount);

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks, copyButtonText, copyButtonText, copyButtonTooltip,
                SelectedItemDynamicElementOptions.Button, () => CopyAllCallStacksToClipboard(BuildCallStackTexts(callstackCount, out var _, out var _, out var _, forCopy: true)));

            if (furthercallstackCount > 0)
            {
                var copyAllTooltip = string.Format(TextContent.CopyAllCallStacksTooltipFormat, callstackCount + furthercallstackCount);
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks, TextContent.CopyAllCallStacksButton, TextContent.CopyAllCallStacksButton, copyAllTooltip,
                    SelectedItemDynamicElementOptions.Button, () => CopyAllCallStacksToClipboard(BuildCallStackTexts(callstackCount + furthercallstackCount, out var _, out var _, out var _, forCopy: true)));

                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks, TextContent.FurtherCallStacksRowLabel, furthercallstackCount.ToString());
            }

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks, TextContent.AllocationsCountRowLabel, allocationCount.ToString());
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks, furthercallstackCount > 0 ? TextContent.ShownCallStacksRowLabel : TextContent.CallStacksRowLabel, callstackCount.ToString());

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks, TextContent.ClickableCallStacksToggle, TextContent.ClickableCallStacksToggle,
                TextContent.ClickableCallStacksTooltip,
                SelectedItemDynamicElementOptions.Toggle | (MemoryProfilerSettings.ClickableCallStacks ? SelectedItemDynamicElementOptions.ToggleOn : 0), () =>
               {
                   MemoryProfilerSettings.ClickableCallStacks = !MemoryProfilerSettings.ClickableCallStacks;
                   m_UI.ClearGroup(SelectedItemDetailsPanel.GroupNameCallStacks);
                   AddCallStacksInfoToUI(sourceIndex);
               });

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks, TextContent.ShowAddressInCallStacksToggle, TextContent.ShowAddressInCallStacksToggle,
                TextContent.ShowAddressInCallStacksTooltip,
                SelectedItemDynamicElementOptions.Toggle | (MemoryProfilerSettings.AddressInCallStacks ? SelectedItemDynamicElementOptions.ToggleOn : 0), () =>
               {
                   MemoryProfilerSettings.AddressInCallStacks = !MemoryProfilerSettings.AddressInCallStacks;
                   m_UI.ClearGroup(SelectedItemDetailsPanel.GroupNameCallStacks);
                   AddCallStacksInfoToUI(sourceIndex);
               });

            if (MemoryProfilerSettings.InternalMode && ExportUtility.LoadCallstackMappingProviderConfig != null)
            {
                AddCallstacksExportAndTreeViewButtons(rootId);
            }

            const bool k_UseFullDetailsPanelWidth = true;
            foreach (var text in callStackTexts)
            {
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameCallStacks,
                    text.Item1, k_UseFullDetailsPanelWidth ? $"{text.Item2}{text.Item3}" : text.Item2 + text.Item3, options:
                    (k_UseFullDetailsPanelWidth ? 0 : SelectedItemDynamicElementOptions.ShowTitle) | SelectedItemDynamicElementOptions.SubFoldout |
                    // TextField (which enables Selectable Label) does not properly support rich text, so these options are mutually exclusive
                    (MemoryProfilerSettings.ClickableCallStacks ? SelectedItemDynamicElementOptions.EnableRichText : SelectedItemDynamicElementOptions.SelectableLabel)
                    );
                if (text.Item4 != null)
                {
                    foreach (var item in text.Item4)
                    {
                        m_UI.AddLinkHashToLinkText(item.Key, item.Value);
                    }
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
        void BuildCallStackInfo(ref List<(string, string, string, List<KeyValuePair<int, string>>)> callStackTexts, ref long allocationCount,
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
        bool BuildCallStackInfo(ref List<(string, string, string, List<KeyValuePair<int, string>>)> callStackTexts, ref long allocationCount,
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
                m_CachedSnapshot.NativeMemoryLabels, m_CachedSnapshot.NativeCallstackSymbols,
                callstackInfo.Index, simplifyCallStacks: simplifyCallStacks, clickableCallStacks: clickableCallStacks);
            if (!string.IsNullOrEmpty(callstack.Callstack))
            {
                callStackTexts.Add((
                    $"Call Stack {++callstackCount}",
                    $"Allocation {DetailFormatter.FormatPointer(address)} made with label {callstack.MemLabel} in {region} : {areaAndObjectName}",
                    $"\n\n{callstack.Callstack}",
                    callstack.FileLinkHashToFileName));
            }
            return true;
        }


        internal void HandleGroupDetails(string title, string description)
        {
            m_UI.SetItemName(title);
            m_UI.SetDescription(description);
        }

        /// <summary>
        /// Which Selection Details handler renders a selected Unity object with a given <see cref="ObjectStatus"/>.
        /// <see cref="None"/> means that status carries no per-object details in this panel (structural/region/
        /// pure-managed statuses a selected Unity object never reaches here).
        /// </summary>
        internal enum DetailHandlerKind
        {
            None,
            DelayedDeletionQueue,
            LeakedShell,
            LeakedShellHeldByGCHandle,
            BrokenScriptReference,
            Manager,
            DynamicAsset,
            PersistentAsset,
            SceneObject,
        }

        /// <summary>
        /// Maps an <see cref="ObjectStatus"/> to the Selection Details handler for it. Written as a switch
        /// expression with NO discard arm on purpose: adding a new <see cref="ObjectStatus"/> without classifying
        /// it here raises CS8509, which is a build error under TestProject's <c>-warnaserror+</c>. That makes the
        /// "new status silently shows no details" gap a compile-time failure rather than a UI regression. Pure and
        /// side-effect free so <c>SelectedItemDetailsStatusDispatchTests</c> can verify the mapping without UI.
        /// </summary>
        // CS8524 (the *unnamed* enum value isn't covered) is suppressed: every NAMED status is handled below, and
        // a runtime value is always a defined enum member. We deliberately keep no discard arm so that adding a new
        // named status still trips CS8509 (a -warnaserror+ build error) — suppressing CS8524 keeps that gate while
        // not forcing a catch-all that would defeat it.
#pragma warning disable CS8524
        internal static DetailHandlerKind ResolveDetailHandler(ObjectStatus precomputedStatus) => precomputedStatus switch
        {
            ObjectStatus.InDelayedDeletionQueue => DetailHandlerKind.DelayedDeletionQueue,
            ObjectStatus.LeakedShell => DetailHandlerKind.LeakedShell,
            ObjectStatus.LeakedShellHeldByGCHandle => DetailHandlerKind.LeakedShellHeldByGCHandle,
            ObjectStatus.BrokenScriptReference => DetailHandlerKind.BrokenScriptReference,
            ObjectStatus.Manager => DetailHandlerKind.Manager,
            ObjectStatus.DynamicAssetLeaked or ObjectStatus.DynamicAssetReferenced or ObjectStatus.DynamicAssetDontUnload => DetailHandlerKind.DynamicAsset,
            ObjectStatus.UsedAsset or ObjectStatus.UnusedAsset or ObjectStatus.UnusedAssetDontUnload
                or ObjectStatus.UnusedAssetInAssetBundle or ObjectStatus.LeakedAssetBundle
                or ObjectStatus.UsedAssetBundle or ObjectStatus.UnusedAssetBundle => DetailHandlerKind.PersistentAsset,
            ObjectStatus.SceneObject or ObjectStatus.SceneObjectDontUnload => DetailHandlerKind.SceneObject,
            // Statuses a selected Unity object never carries into this panel (structural roots, memory regions,
            // pure-managed-object statuses). They intentionally render no per-object status/hint.
            ObjectStatus.None
                or ObjectStatus.UsedByNativeCode or ObjectStatus.GCHandleHeld or ObjectStatus.ManagedObjectReferenced
                or ObjectStatus.UnknownLiveness or ObjectStatus.ManagedTypeRoot or ObjectStatus.NativeRootAllocation
                or ObjectStatus.GCHandleEntry or ObjectStatus.PrefabRoot or ObjectStatus.SceneRoot
                or ObjectStatus.GraphicsBuffers or ObjectStatus.Untracked or ObjectStatus.Reserved
                or ObjectStatus.ExecutablesAndMapped or ObjectStatus.VirtualMachine or ObjectStatus.Unrooted
                or ObjectStatus.ReferencedUnrooted or ObjectStatus.BucketGroup => DetailHandlerKind.None,
        };
#pragma warning restore CS8524

        void UpdateStatusAndHint(UnifiedUnityObjectInfo selectedUnityObject, ObjectStatus precomputedStatus)
        {
            switch (ResolveDetailHandler(precomputedStatus))
            {
                case DetailHandlerKind.DelayedDeletionQueue:
                    UpdateStatusAndHintForObjectsInDelayedDeletionQueue(selectedUnityObject);
                    break;
                case DetailHandlerKind.LeakedShell:
                    UpdateStatusAndHintForLeakedShellObject(selectedUnityObject);
                    break;
                case DetailHandlerKind.LeakedShellHeldByGCHandle:
                    UpdateStatusForGCHandleHeldObjectWithoutNativeObject(selectedUnityObject.ManagedObjectIndex, true);
                    break;
                case DetailHandlerKind.BrokenScriptReference:
                    UpdateStatusAndHintForBrokenScriptReference(selectedUnityObject);
                    break;
                case DetailHandlerKind.Manager:
                    UpdateStatusAndHintForManagers(selectedUnityObject);
                    break;
                case DetailHandlerKind.DynamicAsset:
                    UpdateStatusAndHintForDynamicAssets(selectedUnityObject);
                    break;
                case DetailHandlerKind.PersistentAsset:
                    UpdateStatusAndHintForPersistentAssets(selectedUnityObject, precomputedStatus);
                    break;
                case DetailHandlerKind.SceneObject:
                    UpdateStatusAndHintForSceneObjects(selectedUnityObject);
                    break;
                case DetailHandlerKind.None:
                    break;
            }
        }

        void UpdateStatusForGCHandleHeldObjectWithoutNativeObject(long managedObjectIndex, bool isUnityObjectType)
        {
            if (m_CachedSnapshot.CrawledData.IndicesOfManagedObjectsHeldByRequiredByNativeCodeAttribute.Contains(managedObjectIndex))
            {
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, TextContent.UsedByNativeCodeStatus, TextContent.UsedByNativeCodeHint);
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, TextContent.UsedByNativeCodeHint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
            }
            else if (!isUnityObjectType && m_CachedSnapshot.CrawledData.IndicesOfPureManagedObjectsHeldByNonNativeObjectRelatedGCHandle.Contains(managedObjectIndex))
            {
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, TextContent.GCHandleHeldManagedObjectStatus, TextContent.GCHandleHeldManagedObjectHint);
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, TextContent.GCHandleHeldManagedObjectHint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
            }
            else if (isUnityObjectType || m_CachedSnapshot.CrawledData.IndicesOfManagedPotentialUnityObjectsHeldByNonNativeObjectRelatedGCHandle.Contains(managedObjectIndex))
            {
                // There may be some types that do not get properly found to be Unity Object types but which have objects with m_CachedPtr fields
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, TextContent.LeakedShellHeldByGCHandleStatus, TextContent.LeakedShellHeldByGCHandleHint);
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, TextContent.LeakedShellHeldByGCHandleHint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
            }
            else
            {
                // this case should normally be captured via IndicesOfPureManagedObjectsHeldByNonNativeObjectRelatedGCHandle
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, TextContent.HeldByGCHandleStatus, TextContent.HeldByGCHandleHint);
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, TextContent.HeldByGCHandleHint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
            }
        }

        void UpdateStatusAndHintForObjectsInDelayedDeletionQueue(UnifiedUnityObjectInfo selectedUnityObject)
        {
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, TextContent.InDelayedDeletionQueueStatus, TextContent.InDelayedDeletionQueueHint);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, TextContent.InDelayedDeletionQueueHint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
        }

        void UpdateStatusAndHintForLeakedShellObject(UnifiedUnityObjectInfo selectedUnityObject)
        {
            var statusSummary = string.Format(TextContent.LeakedShellStatusFormat,
                selectedUnityObject.ManagedRefCount > 0 ? TextContent.LeakedShellStatusReferenced : TextContent.LeakedShellStatusCollectable,
                selectedUnityObject.IsSceneObject ? TextContent.LeakedShellOfSceneObject : TextContent.LeakedShellOfAsset);

            var hint = string.Format(TextContent.LeakedShellHintFormat,
                selectedUnityObject.IsSceneObject ? TextContent.LeakedShellHintSceneLabel : TextContent.LeakedShellHintAssetBundleLabel,
                selectedUnityObject.ManagedRefCount > 0 ? TextContent.LeakedShellHintReferencedEnding : TextContent.LeakedShellHintUnreferencedEnding);

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, statusSummary, hint);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, hint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
        }

        void UpdateStatusAndHintForDynamicAssets(UnifiedUnityObjectInfo selectedUnityObject)
        {
            var referenced = selectedUnityObject.TotalRefCount > 0 ? TextContent.DynamicAssetStatusReferenced : string.Empty;
            var state = selectedUnityObject.IsDontUnload ? TextContent.DynamicAssetStatusDontDestroyOnLoad
                : selectedUnityObject.TotalRefCount == 0 ? TextContent.DynamicAssetStatusLeaked
                : string.Empty;
            var needsUpperCase = string.IsNullOrEmpty(referenced) && string.IsNullOrEmpty(state);
            var dynamically = needsUpperCase ? TextContent.DynamicAssetStatusDynamically : TextContent.DynamicAssetStatusDynamicallyLower;
            var creationSuffix = m_CachedSnapshot.HasEntityIDAs8ByteStructs ? TextContent.DynamicAssetStatusCreatedSuffix : selectedUnityObject.IsRuntimeCreated ? TextContent.DynamicAssetStatusRuntimeCreatedSuffix : TextContent.DynamicAssetStatusBuildTimeCreatedSuffix;

            var statusSummary = string.Format(TextContent.DynamicAssetStatusFormat,
                referenced, state, dynamically, creationSuffix, TextContent.DynamicAssetStatusAsset);

            var newObjectTypeConstruction = "'new " + (selectedUnityObject.Type.HasManagedType ? selectedUnityObject.ManagedTypeName : selectedUnityObject.NativeTypeName) + "()'. ";

            string hintEnding;
            if (selectedUnityObject.IsDontUnload)
                hintEnding = TextContent.DynamicAssetHintDontDestroyOnLoad;
            else if (selectedUnityObject.TotalRefCount > 0)
                hintEnding = TextContent.DynamicAssetHintReferenced;
            else
                hintEnding = TextContent.DynamicAssetHintLeakedFormat;

            var hint = string.Format(TextContent.DynamicAssetHintFormat, newObjectTypeConstruction, hintEnding);

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, statusSummary, hint);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, hint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);

            if ((m_CachedSnapshot.HasEntityIDAs8ByteStructs || selectedUnityObject.IsRuntimeCreated) && string.IsNullOrEmpty(selectedUnityObject.NativeObjectName))
            {
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, TextContent.TipRowLabel, string.Format(TextContent.TipUnnamedDynamicAssetFormat, newObjectTypeConstruction));
            }
        }

        void UpdateStatusAndHintForBrokenScriptReference(UnifiedUnityObjectInfo selectedUnityObject)
        {
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, TextContent.ObjectStatusBrokenScriptReference, TextContent.PersistentAssetHintBrokenScript);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, TextContent.PersistentAssetHintBrokenScript, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
        }

        void UpdateStatusAndHintForPersistentAssets(UnifiedUnityObjectInfo selectedUnityObject, ObjectStatus precomputedStatus)
        {
            // AssetBundle-specific statuses get their dedicated label + hint. This must include
            // UnusedAssetInAssetBundle: such an asset has TotalRefCount > 0 (the bundle references it),
            // so the generic path below would misreport it as a used asset.
            if (precomputedStatus is ObjectStatus.UsedAssetBundle or ObjectStatus.UnusedAssetBundle
                or ObjectStatus.LeakedAssetBundle or ObjectStatus.UnusedAssetInAssetBundle)
            {
                var (bundleStatus, bundleHint) = precomputedStatus switch
                {
                    ObjectStatus.UsedAssetBundle => (TextContent.ObjectStatusUsedAssetBundle, TextContent.ObjectStatusUsedAssetBundleTooltip),
                    ObjectStatus.UnusedAssetBundle => (TextContent.ObjectStatusUnusedAssetBundle, TextContent.ObjectStatusUnusedAssetBundleTooltip),
                    ObjectStatus.UnusedAssetInAssetBundle => (TextContent.ObjectStatusUnusedAssetInAssetBundle, TextContent.ObjectStatusUnusedAssetInAssetBundleTooltip),
                    _ => (TextContent.ObjectStatusLeakedAssetBundle, TextContent.ObjectStatusLeakedAssetBundleTooltip),
                };
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, bundleStatus, bundleHint);
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, bundleHint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
                return;
            }

            var statusSummary = string.Format(TextContent.PersistentAssetStatusFormat,
                selectedUnityObject.TotalRefCount > 0 ? TextContent.PersistentAssetStatusUsed : TextContent.PersistentAssetStatusUnused,
                selectedUnityObject.IsDontUnload ? TextContent.PersistentAssetStatusDontDestroyOnLoad : string.Empty,
                m_CachedSnapshot.HasEntityIDAs8ByteStructs ? "" : (selectedUnityObject.IsRuntimeCreated ? TextContent.PersistentAssetStatusRuntimeCreated : TextContent.PersistentAssetStatusLoaded));

            string hint;
            if (!m_CachedSnapshot.HasEntityIDAs8ByteStructs && selectedUnityObject.IsRuntimeCreated)
                hint = TextContent.PersistentAssetHintRuntimeCreated;
            else if (selectedUnityObject.TotalRefCount > 0)
                hint = TextContent.PersistentAssetHintUsed;
            else if (selectedUnityObject.IsDontUnload)
                hint = TextContent.PersistentAssetHintDontDestroyOnLoad;
            // Fallback for snapshots without RootAndImpactInfo: there is no precomputed BrokenScriptReference
            // status to route these to UpdateStatusAndHintForBrokenScriptReference, so detect them here.
            else if (RootAndImpactInfo.UnreferencedNativeObjectIsBrokenScriptingTypeObject(m_CachedSnapshot, selectedUnityObject.NativeObjectIndex))
                hint = TextContent.PersistentAssetHintBrokenScript;
            else
                hint = TextContent.PersistentAssetHintUnused;

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, statusSummary, hint);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, hint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
        }

        void UpdateStatusAndHintForSceneObjects(UnifiedUnityObjectInfo selectedUnityObject)
        {
            var statusSummary = string.Format(TextContent.SceneObjectStatusFormat,
                m_CachedSnapshot.HasEntityIDAs8ByteStructs ? "" : (selectedUnityObject.IsRuntimeCreated ? TextContent.SceneObjectStatusRuntimeCreated : TextContent.SceneObjectStatusLoaded),
                selectedUnityObject.IsDontUnload ? TextContent.SceneObjectStatusDontDestroyOnLoad : string.Empty);

            var hint = TextContent.SceneObjectHintIntro +
                (m_CachedSnapshot.HasEntityIDAs8ByteStructs ? "" : (selectedUnityObject.IsRuntimeCreated ? TextContent.SceneObjectHintInstantiated : TextContent.SceneObjectHintLoaded)) +
                (selectedUnityObject.IsDontUnload ? TextContent.SceneObjectHintDontDestroyOnLoad :
                    (selectedUnityObject.IsGameObject ? TextContent.SceneObjectHintUnloadGameObject : TextContent.SceneObjectHintUnloadComponent) +
                    (selectedUnityObject.HasManagedSide ? TextContent.SceneObjectHintManagedShellWarning : ""));

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, statusSummary, hint);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, hint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);

            if (NativeObjectEntriesCache.IsInvalidHideFlagUsageForNonPersistentSceneObjects(selectedUnityObject.HideFlags))
            {
                var typeName = selectedUnityObject.ManagedTypeIndex >= 0 ? selectedUnityObject.ManagedTypeName : selectedUnityObject.NativeTypeName;
                m_UI.AddInfoBox(SelectedItemDetailsPanel.GroupNameBasic, new InfoBox()
                {
                    IssueLevel = (InfoBox.IssueType)SnapshotIssuesModel.IssueLevel.Warning,
                    Message = string.Format(TextContent.NonpersistentSceneObjectWithInvalidDontDestroyOnLoadHideFlagWarningFormatString, typeName, selectedUnityObject.NativeObjectName),
                });
            }
        }

        void UpdateStatusAndHintForManagers(UnifiedUnityObjectInfo selectedUnityObject)
        {
            var statusSummary = TextContent.NativeManagerStatus;
            var hint = TextContent.NativeManagerHint;

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, statusSummary, hint);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, hint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
        }

        /***
         * Current List of Statuses:
         * - "Native Manager"
         * - "Referenced Leaked Managed Shell of a Scene Object"
         * - "Referenced Leaked Managed Shell of an Asset"
         *
         * - "Dynamically & runtime created Asset"
         * - "Dynamically & build-time created Asset"
         * - "Referenced dynamically & runtime created Asset"
         * - "Referenced dynamically & build-time created Asset "
         * - "Referenced DontDestroyOnLoad dynamically & runtime created Asset"
         * - "Referenced DontDestroyOnLoad dynamically & build-time created Asset"
         * - "Referenced Leaked dynamically & runtime created Asset"
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

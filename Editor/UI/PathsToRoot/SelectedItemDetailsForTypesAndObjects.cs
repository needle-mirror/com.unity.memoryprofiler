using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.Database;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class SelectedItemDetailsForTypesAndObjects : SelectionDetailsProducer
    {
        IUIStateHolder m_uiStateHolder;
        long m_CurrentSelectionIdx;
        ObjectData m_CurrentSelectionObjectData;
        CachedSnapshot m_CachedSnapshot;
        ISelectedItemDetailsUI m_UI;

        string k_StatusLabelText = "Status";
        string k_HintLabelText = "Hint";

        public SelectedItemDetailsForTypesAndObjects(IUIStateHolder uiStateHolder)
        {
            m_uiStateHolder = uiStateHolder;
            uiStateHolder.UIState.CustomSelectionDetailsFactory.RegisterCustomDetailsDrawer(MemorySampleSelectionType.NativeObject, this);
            uiStateHolder.UIState.CustomSelectionDetailsFactory.RegisterCustomDetailsDrawer(MemorySampleSelectionType.ManagedObject, this);
            uiStateHolder.UIState.CustomSelectionDetailsFactory.RegisterCustomDetailsDrawer(MemorySampleSelectionType.UnifiedObject, this);
            uiStateHolder.UIState.CustomSelectionDetailsFactory.RegisterCustomDetailsDrawer(MemorySampleSelectionType.ManagedType, this);
            uiStateHolder.UIState.CustomSelectionDetailsFactory.RegisterCustomDetailsDrawer(MemorySampleSelectionType.NativeType, this);
        }

        /// <summary>
        /// call <see cref="OnShowDetailsForSelection(ISelectedItemDetailsUI, MemorySampleSelection, out string)"/> instead
        /// </summary>
        /// <param name="ui"></param>
        /// <param name="memorySampleSelection"></param>
        public override void OnShowDetailsForSelection(ISelectedItemDetailsUI ui, MemorySampleSelection memorySampleSelection)
        {
            throw new NotImplementedException();
        }

        internal override void OnShowDetailsForSelection(ISelectedItemDetailsUI ui, MemorySampleSelection memorySampleSelection, out string summary)
        {
            m_CachedSnapshot = memorySampleSelection.GetSnapshotItemIsPresentIn(m_uiStateHolder.UIState);
            m_UI = ui;
            UnifiedType type = default;
            switch (memorySampleSelection.Type)
            {
                case MemorySampleSelectionType.NativeObject:
                    m_CurrentSelectionIdx = m_CachedSnapshot.NativeObjectIndexToUnifiedObjectIndex(memorySampleSelection.ItemIndex);

                    m_CurrentSelectionObjectData = ObjectData.FromUnifiedObjectIndex(m_CachedSnapshot, m_CurrentSelectionIdx);
                    type = new UnifiedType(m_CachedSnapshot, m_CurrentSelectionObjectData);
                    HandleObjectDetails(m_CachedSnapshot, memorySampleSelection, type, out summary);
                    break;
                case MemorySampleSelectionType.ManagedObject:
                    m_CurrentSelectionIdx = m_CachedSnapshot.ManagedObjectIndexToUnifiedObjectIndex(memorySampleSelection.ItemIndex);

                    m_CurrentSelectionObjectData = ObjectData.FromUnifiedObjectIndex(m_CachedSnapshot, m_CurrentSelectionIdx);
                    type = new UnifiedType(m_CachedSnapshot, m_CurrentSelectionObjectData);
                    if (m_CurrentSelectionObjectData.IsValid)
                        HandleObjectDetails(m_CachedSnapshot, memorySampleSelection, type, out summary);
                    else
                        HandleInvalidObjectDetails(m_CachedSnapshot, memorySampleSelection, type, out summary);
                    break;
                case MemorySampleSelectionType.UnifiedObject:
                    m_CurrentSelectionIdx = memorySampleSelection.ItemIndex;

                    m_CurrentSelectionObjectData = ObjectData.FromUnifiedObjectIndex(m_CachedSnapshot, m_CurrentSelectionIdx);
                    type = new UnifiedType(m_CachedSnapshot, m_CurrentSelectionObjectData);
                    HandleObjectDetails(m_CachedSnapshot, memorySampleSelection, type, out summary);
                    break;
                case MemorySampleSelectionType.ManagedType:
                    m_CurrentSelectionIdx = memorySampleSelection.ItemIndex;

                    m_CurrentSelectionObjectData = ObjectData.FromManagedType(m_CachedSnapshot, (int)m_CurrentSelectionIdx);
                    type = new UnifiedType(m_CachedSnapshot, m_CurrentSelectionObjectData);
                    HandleTypeDetails(type, out summary);
                    break;
                case MemorySampleSelectionType.NativeType:
                    m_CurrentSelectionIdx = memorySampleSelection.ItemIndex;

                    type = new UnifiedType(m_CachedSnapshot, (int)m_CurrentSelectionIdx);
                    HandleTypeDetails(type, out summary);
                    break;
                default:
                    summary = null;
                    break;
            }
        }

        public override void OnClearSelectionDetails(ISelectedItemDetailsUI detailsUI)
        {
            base.OnClearSelectionDetails(detailsUI);
            m_CurrentSelectionIdx = -1;
            m_CurrentSelectionObjectData = default;
            m_CachedSnapshot = null;
            m_UI = null;
        }

        const string k_TriggerAssetGCHint = "triggering 'Resources.UnloadUnusedAssets()', explicitly or e.g. via a non-additive Scene unload.";

        internal void HandleTypeDetails(UnifiedType type, out string statusSummary)
        {
            if (type.IsValid)
            {
                m_UI.SetItemName(type);
                if (type.ManagedTypeData.IsValid && !type.ManagedTypeIsBaseTypeFallback)
                {
                    m_UI.SetManagedObjectInspector(type.ManagedTypeData);
                }
                m_UI.SetDescription("The selected item is a Type.");
                if (type.HasManagedType) m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Managed Type", type.ManagedTypeName);
                if (type.HasNativeType) m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Native Type", type.NativeTypeName);
                if (type.IsUnifiedtyType)
                {
                    statusSummary = "Unified Type";
                }
                else if (type.HasManagedType)
                {
                    statusSummary = "Managed Type";
                }
                else
                {
                    statusSummary = "Native Type";
                }
            }
            else
            {
                statusSummary = "Invalid Type";
            }
        }

        internal void HandleObjectDetails(CachedSnapshot snapshot, MemorySampleSelection memorySampleSelection, UnifiedType type, out string statusSummary)
        {
            if (!m_CurrentSelectionObjectData.IsValid)
            {
                statusSummary = "Invalid Object";
                return;
            }

            var selectedUnityObject = new UnifiedUnityObjectInfo(m_CachedSnapshot, type, m_CurrentSelectionObjectData);

            if (!selectedUnityObject.IsValid)
            {
                if (m_CurrentSelectionObjectData.isManaged)
                {
                    HandlePureCSharpObjectDetails(snapshot, memorySampleSelection, type, out statusSummary);
                }
                else
                {
                    // What even is this?
                    throw new NotImplementedException();
                }
            }
            else
            {
                HandleUnityObjectDetails(snapshot, memorySampleSelection, selectedUnityObject, out statusSummary);
            }
        }

        internal void HandleInvalidObjectDetails(CachedSnapshot snapshot, MemorySampleSelection memorySampleSelection, UnifiedType type, out string statusSummary)
        {
            //if (m_CurrentSelectionObjectData.IsValid)
            //    return;

            //var selectedUnityObject = new UnifiedUnityObjectInfo(m_CachedSnapshot, type, m_CurrentSelectionObjectData);

            statusSummary = "Invalid Object";
            m_UI.SetItemName("Invalid Object");
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Bug!", "This is an invalid Managed Object, i.e. the Memory Profiler could not identify it's type and data. To help us in finding and fixing this issue, " + TextContent.InvalidObjectPleaseReportABugMessage);
        }

        internal void HandlePureCSharpObjectDetails(CachedSnapshot snapshot, MemorySampleSelection memorySampleSelection, UnifiedType type, out string statusSummary)
        {
            // Pure C# Type Objects
            m_UI.SetItemName(m_CurrentSelectionObjectData, type);

            var managedObjectInfo = m_CurrentSelectionObjectData.GetManagedObject(m_CachedSnapshot);
            var managedSize = managedObjectInfo.Size;
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Managed Size", EditorUtility.FormatBytes(managedSize));
            if (m_CurrentSelectionObjectData.dataType == ObjectDataType.Array)
            {
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Length", m_CurrentSelectionObjectData.GetArrayInfo(snapshot).length.ToString());
            }
            else if (type.ManagedTypeIndex == snapshot.TypeDescriptions.ITypeString)
            {
                var str = StringTools.ReadString(managedObjectInfo.data, out var fullLength, snapshot.VirtualMachineInformation);
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Length", fullLength.ToString());
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "String Value", $"{str}\"", options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel | SelectedItemDynamicElementOptions.ShowTitle);
            }
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Referenced By", managedObjectInfo.RefCount.ToString());
            m_UI.SetManagedObjectInspector(m_CurrentSelectionObjectData);

            if (managedObjectInfo.RefCount == 0)
            {
                bool heldByGCHandle = false;
                for (long i = 0; i < snapshot.GcHandles.Count; i++)
                {
                    if (snapshot.GcHandles.Target[i] == managedObjectInfo.PtrObject)
                    {
                        heldByGCHandle = true;
                        break;
                    }
                }
                if (heldByGCHandle)
                {
                    if (m_CurrentSelectionObjectData.dataType == ObjectDataType.Array && m_CurrentSelectionObjectData.GetArrayInfo(snapshot).length == 0 &&
                        snapshot.TypeDescriptions.TypeDescriptionName[m_CurrentSelectionObjectData.managedTypeIndex].StartsWith("Unity"))
                    {
                        statusSummary = "Managed Object - UsedByNativeCode";
                        m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, TextContent.UsedByNativeCodeStatus, TextContent.UsedByNativeCodeHint);
                        m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, TextContent.UsedByNativeCodeHint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
                    }
                    else
                    {
                        statusSummary = "Managed Object - HeldByGCHandle";
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
                    statusSummary = "Managed Object - Unknown Liveness";
                    m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, TextContent.UnkownLivenessReasonStatus, TextContent.UnkownLivenessReasonHint);
                    m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, TextContent.UnkownLivenessReasonHint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
                }
            }
            else
            {
                statusSummary = "Managed Object - Referenced";
            }
        }

        internal void HandleUnityObjectDetails(CachedSnapshot snapshot, MemorySampleSelection memorySampleSelection, UnifiedUnityObjectInfo selectedUnityObject, out string statusSummary)
        {
            m_UI.SetItemName(selectedUnityObject);

            // Unity Objects
            //if (m_SelectedUnityObject.HasNativeSide) m_UI.AddDynamicElement("Native Object Name", m_SelectedUnityObject.NativeObjectName);
            //if (m_SelectedUnityObject.NativeTypeIndex >= 0) m_UI.AddDynamicElement("Native Type", m_SelectedUnityObject.NativeTypeName);
            if (selectedUnityObject.IsFullUnityObjet)
            {
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Size", $"{EditorUtility.FormatBytes((long)selectedUnityObject.TotalSize)} ({EditorUtility.FormatBytes((long)selectedUnityObject.NativeSize)} Native + {EditorUtility.FormatBytes((long)selectedUnityObject.ManagedSize)} Managed) ");
            }
            else
            {
                if (selectedUnityObject.HasNativeSide)
                    m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Native Size", EditorUtility.FormatBytes((long)selectedUnityObject.NativeSize));
                else
                    m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Managed Size", EditorUtility.FormatBytes(selectedUnityObject.ManagedSize));
            }
            //if (m_SelectedUnityObject.HasNativeSide) m_UI.AddDynamicElement("Native Size", EditorUtility.FormatBytes((long)m_SelectedUnityObject.NativeSize));
            //if (m_SelectedUnityObject.HasManagedSide) m_UI.AddDynamicElement("Managed Size", EditorUtility.FormatBytes(m_SelectedUnityObject.ManagedSize));

            var refCountExtra = (selectedUnityObject.IsFullUnityObjet && selectedUnityObject.TotalRefCount > 0) ? $"({selectedUnityObject.NativeRefCount} Native + {selectedUnityObject.ManagedRefCount} Managed)" : string.Empty;
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Referenced By", $"{selectedUnityObject.TotalRefCount} {refCountExtra}{(selectedUnityObject.IsFullUnityObjet?" + 2 Self References":"")}");

            if (selectedUnityObject.IsFullUnityObjet && !selectedUnityObject.ManagedObjectData.IsValid)
            {
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, "Bug!", "This Native Object is associated with an invalid Managed Object, " + TextContent.InvalidObjectPleaseReportABugMessage);
            }
            // Debug info
            if (selectedUnityObject.HasNativeSide) m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, "Instance ID", selectedUnityObject.InstanceId.ToString());
            if (selectedUnityObject.HasNativeSide)
            {
                var flagsLabel = "";
                var flagsTooltip = "";
                var hideFlagsLabel = "";
                var hideFlagsTooltip = "";
                PathsToRoot.PathsToRootDetailTreeViewItem.GetObjectFlagsStrings(selectedUnityObject.NativeObjectData, snapshot,
                    ref flagsLabel, ref flagsTooltip,
                    ref hideFlagsLabel, ref hideFlagsTooltip, false);
                if (string.IsNullOrEmpty(flagsLabel))
                    flagsLabel = "None";
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, "Flags", flagsLabel, flagsTooltip);
                if (string.IsNullOrEmpty(hideFlagsLabel))
                    hideFlagsLabel = "None";
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, "HideFlags", hideFlagsLabel, hideFlagsTooltip);
            }

            if (selectedUnityObject.HasNativeSide) m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, "Native Address", selectedUnityObject.NativeObjectData.GetObjectPointer(snapshot, false).ToString("X"));
            if (selectedUnityObject.HasManagedSide) m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameAdvanced, "Managed Address", selectedUnityObject.ManagedObjectData.GetObjectPointer(snapshot, false).ToString("X"));

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameDebug, "Selected Index", $"U:{m_CurrentSelectionIdx}");
            if (selectedUnityObject.HasNativeSide) m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameDebug, "Native Index", $"U:{selectedUnityObject.NativeObjectData.GetUnifiedObjectIndex(snapshot)} N:{selectedUnityObject.NativeObjectData.nativeObjectIndex}");
            if (selectedUnityObject.HasManagedSide) m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameDebug, "Managed Index", $"U:{selectedUnityObject.ManagedObjectData.GetUnifiedObjectIndex(snapshot)} M:{selectedUnityObject.ManagedObjectData.GetManagedObjectIndex(snapshot)}");

            UpdateStatusAndHint(selectedUnityObject, out statusSummary);

            if (selectedUnityObject.IsFullUnityObjet)
                m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, "Self References", "The Managed and Native parts of this UnityEngine.Object reference each other. This is normal."
                    + (selectedUnityObject.TotalRefCount == 0 ? " Nothing else references them though so the Native part keeps the Managed part alive." : ""));

            if (selectedUnityObject.HasManagedSide)
            {
                m_UI.SetManagedObjectInspector(selectedUnityObject.ManagedObjectData);
            }
        }

        void UpdateStatusAndHint(UnifiedUnityObjectInfo m_SelectedUnityObject, out string statusSummary)
        {
            if (m_SelectedUnityObject.IsLeakedShell)
            {
                UpdateStatusAndHintForLeakedShellObject(m_SelectedUnityObject, out statusSummary);
            }
            else if (m_SelectedUnityObject.IsAssetObject && !m_SelectedUnityObject.IsAsset)
            {
                UpdateStatusAndHintForDynamicAssets(m_SelectedUnityObject, out statusSummary);
            }
            else if (m_SelectedUnityObject.IsAsset)
            {
                UpdateStatusAndHintForPersistentAssets(m_SelectedUnityObject, out statusSummary);
            }
            else if (m_SelectedUnityObject.IsSceneObject)
            {
                UpdateStatusAndHintForSceneObjects(m_SelectedUnityObject, out statusSummary);
            }
            else if (m_SelectedUnityObject.IsManager)
            {
                UpdateStatusAndHintForManagers(m_SelectedUnityObject, out statusSummary);
            }
            else
            {
                statusSummary = null;
                // well, what DO we have here?
                // TODO: do something with the hideflags?
            }
        }

        void UpdateStatusAndHintForLeakedShellObject(UnifiedUnityObjectInfo selectedUnityObject, out string statusSummary)
        {
            statusSummary = string.Empty;
            var hint = string.Empty;
            if (selectedUnityObject.ManagedRefCount > 0)
            {
                statusSummary += "Referenced ";
            }
            else
            {
                statusSummary += "GC.Collect()-able ";
            }
            statusSummary += "Leaked Managed Shell of ";
            if (selectedUnityObject.IsSceneObject)
            {
                statusSummary += "a Scene Object";
            }
            else
            {
                statusSummary += "an Asset";
            }
            hint = "This Unity Object is a Leaked Managed Shell. That means this object's type derives from UnityEngine.Object " +
                "and the object therefore, normally, has a Native Object accompanying it. " +
                "If it is used by Managed (C#) Code, a Managed Shell Object is created to allow access to the Native Object. " +
                "In this case, the Native Object has been destroyed, either via Destroy() or because the +" +
                (selectedUnityObject.IsSceneObject ? "Scene" : "Asset Bundle") + " it was in was unloaded. " +
                "After the Native Object was destroyed, the Managed Garbage Collector hasn't yet collected this object." +
                (selectedUnityObject.ManagedRefCount > 0
                    ? "This is because the Managed Shell is still being referenced and can therefore not yet be collected. " +
                    "You can fix this by explicitly setting each field referencing this Object to null (comparing it to null will claim it already is null, as it acts as a \"Fake Null\" object), " +
                    "or by ensuring that each referencing object is no longer referenced itself, so that all of them can be unloaded."
                    : "Nothing is referencing this Object anymore, so it should be collected with the next GC.Collect.");

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, statusSummary, hint);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, hint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
        }

        void UpdateStatusAndHintForDynamicAssets(UnifiedUnityObjectInfo selectedUnityObject, out string statusSummary)
        {
            statusSummary = string.Empty;
            var hint = string.Empty;
            if (selectedUnityObject.TotalRefCount > 0)
            {
                statusSummary += "Referenced ";
            }

            if (selectedUnityObject.IsDontUnload)
            {
                statusSummary += "DontDestroyOnLoad ";
            }
            else if (selectedUnityObject.TotalRefCount == 0)
            {
                statusSummary += "Leaked ";
            }

            if (selectedUnityObject.IsRuntimeCreated)
            {
                statusSummary += $"{(string.IsNullOrEmpty(statusSummary)?'D':'d')}ynamically & run-time created ";
            }
            else
            {
                // e.g. Combined Scene Meshes
                statusSummary += $"{(string.IsNullOrEmpty(statusSummary)?'D':'d')}ynamically & build-time created ";
            }

            statusSummary += "Asset";

            var newObjectTypeConstruction = "'new " + (selectedUnityObject.Type.HasManagedType ? selectedUnityObject.ManagedTypeName : selectedUnityObject.NativeTypeName) + "()'. ";

            hint = "This is a dynamically created Asset Type object, that was either Instantiated, implicitly duplicated or explicitly constructed via " +
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

        void UpdateStatusAndHintForPersistentAssets(UnifiedUnityObjectInfo selectedUnityObject, out string statusSummary)
        {
            statusSummary = string.Empty;
            var hint = string.Empty;
            if (selectedUnityObject.TotalRefCount > 0)
            {
                statusSummary += "Used ";
            }
            else
            {
                statusSummary += "Unused ";
            }

            if (selectedUnityObject.IsDontUnload)
            {
                statusSummary += "DontDestroyOnLoad ";
            }

            if (selectedUnityObject.IsRuntimeCreated)
            {
                statusSummary += "Runtime Created ";
            }
            else
            {
                statusSummary += "Loaded ";
            }
            statusSummary += "Asset";


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

        void UpdateStatusAndHintForSceneObjects(UnifiedUnityObjectInfo selectedUnityObject, out string statusSummary)
        {
            statusSummary = string.Empty;
            var hint = string.Empty;
            if (selectedUnityObject.IsRuntimeCreated)
            {
                statusSummary += "Runtime Created ";
            }
            else
            {
                statusSummary += "Loaded ";
            }

            if (selectedUnityObject.IsDontUnload)
            {
                statusSummary += "DontDestroyOnLoad ";
            }

            statusSummary += "Scene Object";

            hint = "This is a Scene Object, i.e. a GameObject or a Component on it. " +
                (selectedUnityObject.IsRuntimeCreated ? "It was instantiated after the Scene was loaded. " : "It was loaded in as part of a Scene. ") +
                (selectedUnityObject.IsDontUnload ? "It is marked as 'DontDestroyOnLoad' so to unload it, you would need to call 'Destroy()' on it, or not mark it as 'DontDestroyOnLoad'" :
                    "Its Native Memory will be unloaded once the Scene it resides in is unloaded or the GameObject " +
                    (selectedUnityObject.IsGameObject ? "" : "it is attached to ") +
                    "or its parents are destroyed via 'Destroy()'. " +
                    (selectedUnityObject.HasManagedSide ? "Its Managed memory may live on as a Leaked Shell Object if something else that was not unloaded with it still references it." : ""));

            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameBasic, k_StatusLabelText, statusSummary, hint);
            m_UI.AddDynamicElement(SelectedItemDetailsPanel.GroupNameHelp, k_HintLabelText, hint, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
        }

        void UpdateStatusAndHintForManagers(UnifiedUnityObjectInfo selectedUnityObject, out string statusSummary)
        {
            statusSummary = "Native Manager";
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

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if INSTANCE_ID_CHANGED
using TreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
using TreeView = UnityEditor.IMGUI.Controls.TreeView<int>;
#else
using UnityEditor.IMGUI.Controls;
#endif

namespace Unity.MemoryProfiler.Editor.UI.PathsToRoot
{
    internal sealed class PathsToRootDetailTreeViewItem : TreeViewItem
    {
        public int CircularRefId { get; private set; } = -1;
        public ObjectData Data { get; }
        public string TypeName { get; }
        public string TruncatedTypeName { get; }

        static int s_IdGenerator;
        public GUIContent TypeIcon { get; private set; }
        public GUIContent ObjectFlags { get; private set; }

        public bool IsRoot(CachedSnapshot cs)
        {
            return Data.IsRootGameObject(cs);
        }

        public string AssetPath(CachedSnapshot cs)
        {
            return IsRoot(cs) ? Data.GetAssetPath(cs) : String.Empty;
        }

        public string FlagsInfo = "";
        public string ToolTipMsg;
        public bool HasCircularReference;
        bool needsIcon = true;

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

        public PathsToRootDetailTreeViewItem(ObjectData data, CachedSnapshot cachedSnapshot, PathsToRootDetailTreeViewItem potentialParent, bool truncateTypeNames, bool referencesToItem = false) : this(data, cachedSnapshot, truncateTypeNames, referencesToItem)
        {
            HasCircularReference = CircularReferenceCheck(potentialParent);
        }

        public PathsToRootDetailTreeViewItem(ObjectData data, CachedSnapshot cachedSnapshot, bool truncateTypeNames, bool referencesToItem = false)
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
                displayName = GetDisplayName(data, cachedSnapshot, truncateTypeNames, referencesToItem);

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

        string GetDisplayName(ObjectData data, CachedSnapshot cachedSnapshot, bool truncateTypeNames, bool referencesToItem)
        {
            ManagedObjectInfo managedObjectInfo;
            var referencedItemName = "";
            ObjectData displayObject = data.displayObject;
            // for ReferencesTo Items reported as field data, we need to adjust
            if (referencesToItem && (data.IsField() || data.IsArrayItem()))
            {
                // The field info comes from the parent
                displayObject = data.Parent.Obj;
                // but we also want to show the referenced target held by the field
                referencedItemName = $"{data.GenerateObjectName(cachedSnapshot)} referenced by: ";
            }
            switch (displayObject.dataType)
            {
                case ObjectDataType.NativeObject:
                    var s = cachedSnapshot.NativeObjects.ObjectName[displayObject.nativeObjectIndex];
                    return referencedItemName + (string.IsNullOrEmpty(s) ? "Unnamed Object" : s);
                case ObjectDataType.Unknown:
                    return referencedItemName + "<unknown>";
                case ObjectDataType.Value:
                    if (!displayObject.IsField())
                    {
                        Debug.LogError("Connection via a value that is not a fieldshould not happen.");
                        return referencedItemName + "Connection to Value";
                    }
                    var fieldType = cachedSnapshot.FieldDescriptions.TypeIndex[displayObject.fieldIndex];
                    var isPointerField = fieldType == cachedSnapshot.TypeDescriptions.ITypeIntPtr || cachedSnapshot.TypeDescriptions.TypeDescriptionName[fieldType].EndsWith('*');
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
                            return referencedItemName + cachedSnapshot.NativeObjects.ObjectName[managedObjectInfo.NativeObjectIndex];
                        }
                        if (managedObjectInfo.ITypeDescription == cachedSnapshot.TypeDescriptions.ITypeString)
                        {
                            return StringTools.ReadFirstStringLine(managedObjectInfo.data, cachedSnapshot.VirtualMachineInformation, false);
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
                        return referencedItemName + cachedSnapshot.NativeObjects.ObjectName[managedObjectInfo.NativeObjectIndex];
                    }

                    return $"{referencedItemName}Unknown {displayObject.dataType}. Is not a field or array item";
                case ObjectDataType.Type:
                    var fieldName = string.Empty;
                    if (data.IsField())
                        fieldName = $".{data.GetFieldName(cachedSnapshot)}";
                    var typeName = truncateTypeNames ? TruncatedTypeName : TypeName;
                    return $"{referencedItemName}Static field type reference on {typeName}{fieldName}";
                case ObjectDataType.NativeAllocation: // should not be present outside of as a referencesToItem with field information to display
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
            TypeIcon = GetIcon(Data, TypeName, cs);
            icon = (Texture2D)TypeIcon.image;
            ObjectFlags = string.IsNullOrEmpty(FlagsInfo) ? null : new GUIContent(PathsToRootUtils.FlagIcon.image, ToolTipMsg);
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
                    flagsNames += " 'IsManager'";
                    flagsExplanations += PathsToRootUtils.IsManagerInfo;
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

        public static GUIContent GetIcon(ObjectData data, string typeName, CachedSnapshot cs)
        {
            if (typeName == "AssetBundle")
            {
                return PathsToRootUtils.NoIconContent;
            }
            if (PathsToRootUtils.iconContent.TryGetValue(typeName, out var content))
            {
                return content;
            }
            if (!typeName.Contains("<unknown>") && cs != null)
            {
                var n = string.Concat(typeName.Split(Path.GetInvalidFileNameChars()));
                n = n.Replace("UnityEngine.", "");
                n = n.Replace("UnityEditor.", "");

                var tex = IconUtility.LoadBuiltInIconWithName(n + " Icon");
                if (tex != null)
                {
                    PathsToRootUtils.iconContent.Add(typeName, new GUIContent((Texture)tex));
                    return PathsToRootUtils.iconContent[typeName];
                }

                if (n == "MonoBehaviour")
                {
                    return PathsToRootUtils.CSScriptIconContent;
                }

                if (!data.isManaged) return GUIContent.none;


                if (data.managedTypeIndex == -1)
                    return PathsToRootUtils.CSScriptIconContent;


                var idx = cs.TypeDescriptions.BaseOrElementTypeIndex[data.managedTypeIndex];
                if (idx != -1 && cs.TypeDescriptions.TypeDescriptionName[idx] == "UnityEngine.MonoBehaviour")
                {
                    return PathsToRootUtils.CSScriptIconContent;
                }

                if (data.isManaged)
                    return PathsToRootUtils.CSScriptIconContent;
            }

            return PathsToRootUtils.NoIconContent;
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

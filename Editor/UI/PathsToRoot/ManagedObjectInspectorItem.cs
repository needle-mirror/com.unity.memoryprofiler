using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;
using UnityEditor;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class ManagedObjectInspectorItem : TreeViewItem
    {
        public bool PendingProcessing => m_ReferencePendingProcessingWhenExpanding.ObjectData.IsValid;
        public bool IsDuplicate => m_ExistingTreeViewItemId != -1;
        public bool IsRecursive => m_DuplicationType == DuplicationType.Recursive || m_DuplicationType == DuplicationType.RecursiveToRoot;
        public int ExistingItemId => m_ExistingTreeViewItemId;

        const string HyperlinkManagedObjectInspectorIdTag = "ManagedObjectInspectorId";
        const string HyperlinktargetItemIdTag = "ManagedObjectInspectorItemId";

        static int s_IdGenerator;
        int m_InspectorID;
        long m_Idx;
        ulong m_IdentifyintPointer;
        bool m_IsStatic;
        int m_ExistingTreeViewItemId = -1;
        DuplicationType m_DuplicationType = DuplicationType.None;

        ManagedObjectInspector.ReferencePendingProcessing m_ReferencePendingProcessingWhenExpanding;

        enum DuplicationType
        {
            None,
            Duplicate,
            Recursive,
            RecursiveToRoot,
        }

        public ManagedObjectInspectorItem(int managedInspectorId)
        {
            m_InspectorID = managedInspectorId;
            DisplayName = "";
            id = s_IdGenerator++;
            depth = -1;
        }

        public ManagedObjectInspectorItem(int managedInspectorId, ManagedObjectInspector.ReferencePendingProcessing referencePendingProcessingWhenExpanding)
        {
            m_InspectorID = managedInspectorId;
            m_Idx = -1;
            m_ReferencePendingProcessingWhenExpanding = referencePendingProcessingWhenExpanding;
            id = s_IdGenerator++;
            depth = -1;
            DisplayName = $"<a {HyperlinkManagedObjectInspectorIdTag}=\"{m_InspectorID}\" {HyperlinktargetItemIdTag}=\"{id}\">Continue ...</a>";
        }

        internal ManagedObjectInspectorItem(int managedInspectorId, long idx, CachedSnapshot cs)
        {
            m_InspectorID = managedInspectorId;
            m_Idx = idx;
            id = s_IdGenerator++;
            DisplayName = cs.FieldDescriptions.FieldDescriptionName[m_Idx];
            var typeIdx = cs.FieldDescriptions.TypeIndex[idx];
            TypeName = cs.TypeDescriptions.TypeDescriptionName[typeIdx];
            depth = -1;
            children = new List<TreeViewItem>();
        }

        internal ManagedObjectInspectorItem(int managedInspectorId, string name, string type, string value, bool isStatic, ulong identifyingPointer, ulong size)
        {
            m_InspectorID = managedInspectorId;
            id = s_IdGenerator++;
            DisplayName = name;
            TypeName = type;
            if (ManagedObjectInspector.HidePointers && value.StartsWith("0x"))
                Value = string.Empty;
            else
                Value = value;
            Size = size > 0 ? EditorUtility.FormatBytes((long)size) : string.Empty;
            depth = -1;
            m_IsStatic = isStatic;
            m_IdentifyintPointer = identifyingPointer;
            children = new List<TreeViewItem>();
        }

        public void MarkRecursiveOrDuplicate(int existingTreeViewItemId)
        {
            m_ExistingTreeViewItemId = existingTreeViewItemId;
            m_DuplicationType = DuplicationType.Duplicate;
            var currentParent = parent;
            while (currentParent != null)
            {
                if (currentParent.id == existingTreeViewItemId || m_IdentifyintPointer != 0 && (currentParent as ManagedObjectInspectorItem).m_IdentifyintPointer == m_IdentifyintPointer)
                {
                    if (currentParent.parent == null)
                        m_DuplicationType = DuplicationType.RecursiveToRoot;
                    else
                        m_DuplicationType = DuplicationType.Recursive;
                    m_ExistingTreeViewItemId = currentParent.id;
                    break;
                }
                currentParent = currentParent.parent;
            }
        }

        public static bool TryParseHyperlink(Dictionary<string, string> hyperLinkData, out int inspectorId, out int treeViewId)
        {
            string tagText;
            if (hyperLinkData.TryGetValue(HyperlinkManagedObjectInspectorIdTag, out tagText) && int.TryParse(tagText, out inspectorId))
            {
                if (hyperLinkData.TryGetValue(HyperlinktargetItemIdTag, out tagText) && int.TryParse(tagText, out treeViewId))
                {
                    return true;
                }
            }
            inspectorId = treeViewId = 0;
            return false;
        }

        public string Value { get; internal set; }

        public string TypeName
        {
            get;
        }

        public string Size
        {
            get;
        }

        public string DisplayName
        {
            get;
        }


        public string Notes
        {
            get
            {
                if (IsDuplicate)
                {
                    switch (m_DuplicationType)
                    {
                        case DuplicationType.Duplicate:
                            return $"<a {HyperlinkManagedObjectInspectorIdTag}=\"{m_InspectorID}\" {HyperlinktargetItemIdTag}=\"{m_ExistingTreeViewItemId}\">Also referenced by other fields in this table</a>";
                        case DuplicationType.Recursive:
                            return $"<a {HyperlinkManagedObjectInspectorIdTag}=\"{m_InspectorID}\" {HyperlinktargetItemIdTag}=\"{m_ExistingTreeViewItemId}\">Circular Reference</a>";
                        case DuplicationType.RecursiveToRoot:
                            return "The Selected Object";
                        default:
                            break;
                    }
                }
                if (m_IsStatic)
                    return "Static";
                return "";
            }
        }
    }
}

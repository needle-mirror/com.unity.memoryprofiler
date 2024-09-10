using System.Collections.Generic;
using UnityEditor;
using System.Runtime.CompilerServices;
#if INSTANCE_ID_CHANGED
using TreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
#else
using UnityEditor.IMGUI.Controls;
#endif

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class ManagedObjectInspectorItem : TreeViewItem
    {
        public bool PendingProcessing => m_ReferencePendingProcessingWhenExpanding.ObjectData.IsValid;
        public bool IsDuplicate => m_ExistingTreeViewItemId != -1;
        public bool IsRecursive => m_DuplicationType == DuplicationType.Recursive || m_DuplicationType == DuplicationType.RecursiveToRoot;
        public readonly bool IsStatic;
        public readonly int ManagedTypeIndex = -1;
        public int ExistingItemId => m_ExistingTreeViewItemId;

        const string HyperlinkManagedObjectInspectorIdTag = "ManagedObjectInspectorId";
        const string HyperlinktargetItemIdTag = "ManagedObjectInspectorItemId";

        static int s_IdGenerator;
        int m_InspectorID;
        long m_Idx;
        ulong m_IdentifyingPointer;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ManagedObjectInspectorItem(int managedInspectorId, string name, int managedTypeIndex, string type, string value, bool isStatic, ulong identifyingPointer, ulong size)
            : this(managedInspectorId, name, managedTypeIndex, type, value, isStatic, identifyingPointer,
                  // TODO: Lazy generate byte size when proting this to UI TK Multicolumn Tree Views, as this is ~30% of the cost of using this constructor,
                  // or 968ms of 3246ms (total constructor cost) when selecting a relatively mundane managed TypeConverter in the All Of Memory table
                  EditorUtility.FormatBytes((long)size))
        { }

        internal ManagedObjectInspectorItem(int managedInspectorId, string name, int managedTypeIndex, string type, string value, bool isStatic, ulong identifyingPointer, string size)
        {
            m_InspectorID = managedInspectorId;
            id = s_IdGenerator++;
            DisplayName = name;
            ManagedTypeIndex = managedTypeIndex;
            TypeName = type;
            if (ManagedObjectInspector.HidePointers && value.Length >= DetailFormatter.PointerNameMinLength)
            {
                // string.StartsWith("0x"... but without all the Globalization and safety non-sense
                // string.StartsWith used to be ~50%, or 1694ms of 2256ms, of the cost of this constructor
                unsafe
                {
                    fixed (char* ptr = value)
                    {
                        if (*ptr == '0' && *(ptr + 1) == 'x')
                            value = string.Empty;
                        else
                            Value = value;
                    }
                }
            }
            else
                Value = value;
            Size = size;
            depth = -1;
            IsStatic = isStatic;
            m_IdentifyingPointer = identifyingPointer;
            children = new List<TreeViewItem>();
        }

        public void MarkRecursiveOrDuplicate(int existingTreeViewItemId)
        {
            m_ExistingTreeViewItemId = existingTreeViewItemId;
            m_DuplicationType = DuplicationType.Duplicate;
            var currentParent = parent;
            while (currentParent != null)
            {
                if (currentParent.id == existingTreeViewItemId || m_IdentifyingPointer != 0 && (currentParent as ManagedObjectInspectorItem).m_IdentifyingPointer == m_IdentifyingPointer)
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
                if (IsStatic)
                    return "Static";
                return "";
            }
        }
    }
}

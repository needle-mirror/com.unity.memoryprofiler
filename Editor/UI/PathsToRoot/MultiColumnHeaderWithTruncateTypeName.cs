using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI.PathsToRoot
{
    public class MultiColumnHeaderWithTruncateTypeName : MultiColumnHeader
    {
        public MultiColumnHeaderWithTruncateTypeName(MultiColumnHeaderState state)
            : base(state) {}

        protected override void AddColumnHeaderContextMenuItems(GenericMenu menu)
        {
            base.AddColumnHeaderContextMenuItems(menu);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(TextContent.TruncateTypeName), MemoryProfilerSettings.MemorySnapshotTruncateTypes, MemoryProfilerSettings.ToggleTruncateTypes);
        }
    }
}

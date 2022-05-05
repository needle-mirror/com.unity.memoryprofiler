using System;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI.PathsToRoot
{
    class MultiColumnHeaderWithTruncateTypeName : MultiColumnHeader
    {
        /// <summary>
        /// only for analytics to track which table the change came from.
        /// For adjusting the display styling subscribe to the global <see cref="MemoryProfilerSettings.TruncateStateChanged"/> event instead.
        /// </summary>
        public event Action<bool> TruncationChangedViaThisHeader = delegate { };
        public MultiColumnHeaderWithTruncateTypeName(MultiColumnHeaderState state)
            : base(state) { }

        protected override void AddColumnHeaderContextMenuItems(GenericMenu menu)
        {
            base.AddColumnHeaderContextMenuItems(menu);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(TextContent.TruncateTypeName), MemoryProfilerSettings.MemorySnapshotTruncateTypes,
                () =>
                {
                    MemoryProfilerSettings.ToggleTruncateTypes();
                    TruncationChangedViaThisHeader(MemoryProfilerSettings.MemorySnapshotTruncateTypes);
                });
        }
    }
}

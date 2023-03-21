using UnityEngine;
using UnityEditor;
using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class GeneralStyles
    {
        public static readonly int InitialWorkbenchWidth = 200;

        public static readonly string PlatformIconName = "preview-image__platform-icon";
        public static readonly string EditorIconName = "preview-image__editor-icon";
        public const string PlatformIconClassName = "platform-icon";
        public const string PlatformIconEditorClassName = "Editor";
        public const string ImageTintColorClassSnapshotA = "image-tint-color__snapshot-a";
        public const string ImageTintColorClassSnapshotB = "image-tint-color__snapshot-b";

        public const string IconButtonClass = "icon-button";
        public const string HelpIconButtonClass = "icon-button__help-icon";
        public const string MenuIconButtonClass = "icon-button__menu-icon";
    }
}

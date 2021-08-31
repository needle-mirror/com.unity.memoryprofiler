using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class ToolbarDropDownButton : DropDownButton
    {
        const string k_StyleClass = "unity-toolbar-menu";
        const string k_ToolbarButtonStyleClass = "unity-toolbar-button";
        const string k_ButtonStyleClass = "unity-button";

        public ToolbarDropDownButton() : base()
        {
            AddToClassList(k_StyleClass);
            AddToClassList(k_ToolbarButtonStyleClass);
            RemoveFromClassList(k_ButtonStyleClass);
            Init();
        }

        protected override void Init()
        {
            base.Init();
        }

        /// <summary>
        /// Instantiates a <see cref="ToolbarDropDownButton"/> using the data read from a UXML file.
        /// </summary>
        public new class UxmlFactory : UxmlFactory<ToolbarDropDownButton, UxmlTraits> {}

        /// <summary>
        /// Defines <see cref="UxmlTraits"/> for the <see cref="ToolbarDropDownButton"/>.
        /// </summary>
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
            {
                get { yield break; }
            }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var button = ((ToolbarDropDownButton)ve);

                button.Init();
            }
        }
    }
}

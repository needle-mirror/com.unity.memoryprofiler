using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class RibbonButton : Button
    {
        bool m_Toggled;
        public bool Toggled {
            get { return m_Toggled; }
            set
            {
                m_Toggled = value;
                if (m_Toggled)
                    AddToClassList(StyleClassToggled);
                else
                    RemoveFromClassList(StyleClassToggled);
            }
        }

        public const string StyleClass = "ribbon__button";
        public const string StyleClassToggled = "ribbon__button--toggled";
        public RibbonButton()
        {
            AddToClassList(StyleClass);
        }

        void Init()
        {

        }

        /// <summary>
        /// Instantiates a <see cref="Ribbon"/> using the data read from a UXML file.
        /// </summary>
        public new class UxmlFactory : UxmlFactory<RibbonButton, UxmlTraits> { }

        /// <summary>
        /// Defines <see cref="UxmlTraits"/> for the <see cref="RibbonButton"/>.
        /// </summary>
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlStringAttributeDescription m_Text = new UxmlStringAttributeDescription { name = "text", defaultValue = "Button" };
            UxmlBoolAttributeDescription m_Toggled = new UxmlBoolAttributeDescription { name = "toggled", defaultValue = false };
            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
            {
                get { yield break; }
            }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var button = ((RibbonButton)ve);
                var text = m_Text.GetValueFromBag(bag, cc);
                button.text = text;

                var toggled = m_Toggled.GetValueFromBag(bag, cc);
                if (toggled)
                    button.AddToClassList(StyleClassToggled);
                else
                    button.RemoveFromClassList(StyleClassToggled);
                button.Toggled = toggled;

                ((RibbonButton)ve).Init();
            }
        }
    }
}

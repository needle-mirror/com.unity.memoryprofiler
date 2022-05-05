using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class DropDownButton : Button
    {
        const string k_StyleClass = "drop-down-button";
        const string k_BaseArrowStyleClass = "unity-base-popup-field__arrow";
        const string k_ArrowStyleClass = "toolbar__drop-down__arrow";

        Label m_Label;
        VisualElement m_DropDownArrow;

        [SerializeField]
        string m_Text;
        public string ButtonText
        {
            get => m_Text;
            set
            {
                base.text = "";
                m_Text = value;
                m_Label.text = value;
            }
        }

        public DropDownButton() : base()
        {
            AddToClassList(k_StyleClass);

            m_Label = new Label(m_Text);
            hierarchy.Add(m_Label);

            m_DropDownArrow = new VisualElement();
            m_DropDownArrow.AddToClassList(k_BaseArrowStyleClass);
            m_DropDownArrow.AddToClassList(k_ArrowStyleClass);
            hierarchy.Add(m_DropDownArrow);
            Init();
        }

        protected virtual void Init()
        {
            base.text = "";
        }

        /// <summary>
        /// Instantiates a <see cref="DropDownButton"/> using the data read from a UXML file.
        /// </summary>
        public new class UxmlFactory : UxmlFactory<DropDownButton, UxmlTraits> { }

        /// <summary>
        /// Defines <see cref="UxmlTraits"/> for the <see cref="DropDownButton"/>.
        /// </summary>
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlStringAttributeDescription m_Text = new UxmlStringAttributeDescription { name = "button-text", defaultValue = "Button" };
            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
            {
                get { yield break; }
            }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var button = ((DropDownButton)ve);
                var text = m_Text.GetValueFromBag(bag, cc);
                button.ButtonText = text;

                button.Init();
            }
        }
    }
}

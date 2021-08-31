using System;
using System.Collections;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class Ribbon : VisualElement
    {
        static class Content
        {
            // Technically Rbbon is a UI controll that could be more agnostic of this specific implementation for the Memory Profiler.
            // While this content may be reused in other places of the Memory Profiler and therefore makes sense to be located on TextContent,
            // keeping a Content class here makes it easier to keep things separated and helps when copying it out into other tools.
            public static readonly string OpenManualTooltip = TextContent.OpenManualTooltip;
        }
        static class Styles
        {
            public static readonly string HelpIconButtonClass = GeneralStyles.HelpIconButtonClass;
            public static readonly string MenuIconButtonClass = GeneralStyles.MenuIconButtonClass;
        }

        Align m_Alignment;
        public Align Alignment
        {
            get { return m_Alignment; }
            private set
            {
                switch (value)
                {
                    case Align.Auto:
                    case Align.FlexStart:
                        m_Alignment = value;
                        SetLeftAligned();
                        break;
                    case Align.Center:
                        m_Alignment = value;
                        SetCenterAligned();
                        break;
                    case Align.FlexEnd:
                    case Align.Stretch:
                    default:
                        Debug.LogError("Ribbons can only be left of center aligned");
                        break;
                }
            }
        }

        public bool ShowHelpButton { get; private set; }
        public bool ShowMenuButton { get; private set; }
        public int InitialOption { get; private set; }

        public int m_CurrentOption = 0;
        public int CurrentOption
        {
            get { return m_CurrentOption; }
            set { m_CurrentOption = value; RefreshButtonToggleStates(); }
        }

        public override VisualElement contentContainer
        {
            get { return m_Content; }
        }

        public event Action<int> Clicked = delegate {};
        public event Action HelpClicked = delegate {};
        public event Action MenuClicked = delegate {};

        VisualElement m_Root;
        VisualElement m_Content;
        VisualElement m_Container;
        List<RibbonButton> m_Buttons = new List<RibbonButton>();
        VisualElement m_OptionsAndInfos;
        Button m_Menu;
        Button m_Help;

        const string k_ContainerClassCenterAligned = "ribbon__container--centered";
        const string k_ClassCenterAligned = "ribbon--centered";
        const string k_ContainerClassLeftAligned = "ribbon__container--left-aligned";
        const string k_ClassLeftAligned = "ribbon--left-aligned";

        public Ribbon()
        {
            var ribbonViewTree = AssetDatabase.LoadAssetAtPath(ResourcePaths.RibbonUxmlPath, typeof(VisualTreeAsset)) as VisualTreeAsset;


            m_Root = ribbonViewTree.Clone();

            // clear out the style sheets defined in the template uxml file so they can be applied from here in the order of: 1. theming, 2. base
            m_Root.styleSheets.Clear();
            var themeStyle = AssetDatabase.LoadAssetAtPath(EditorGUIUtility.isProSkin ? ResourcePaths.RibbonDarkUssPath : ResourcePaths.RibbonLightUssPath, typeof(StyleSheet)) as StyleSheet;
            m_Root.styleSheets.Add(themeStyle);

            var ribbonStyle = AssetDatabase.LoadAssetAtPath(ResourcePaths.RibbonUssPath, typeof(StyleSheet)) as StyleSheet;
            m_Root.styleSheets.Add(ribbonStyle);

            hierarchy.Add(m_Root);

            style.flexShrink = 0;

            m_Container = m_Root.Q("ribbon__container");
            m_Content = m_Root.Q("ribbon__buttons");
            m_OptionsAndInfos = m_Root.Q("options-and-info");
            m_Menu = m_OptionsAndInfos.Q<Button>("", Styles.MenuIconButtonClass);
            m_Help = m_OptionsAndInfos.Q<Button>("", Styles.HelpIconButtonClass);
            m_Help.tooltip = Content.OpenManualTooltip;
            m_CurrentOption = InitialOption;
            Setup();
        }

        void Init()
        {
            m_CurrentOption = InitialOption;
            Setup();
        }

        public void Setup()
        {
            GatherElements();

            if (m_Buttons.Count > 0)
            {
                UnregisterCallback<GeometryChangedEvent>(OnPostDisplaySetup);
                SetupElements();
            }
            else
                RegisterCallback<GeometryChangedEvent>(OnPostDisplaySetup);
        }

        void GatherElements()
        {
            m_Buttons.Clear();
            if (m_Content == null)
                return;
            for (int i = 0; i < m_Content.childCount; i++)
            {
                if (m_Content[i] is RibbonButton)
                {
                    m_Buttons.Add(m_Content[i] as RibbonButton);
                }
            }
        }

        void OnPostDisplaySetup(GeometryChangedEvent evt)
        {
            GatherElements();
            if (m_Buttons.Count > 0)
            {
                UnregisterCallback<GeometryChangedEvent>(OnPostDisplaySetup);
                SetupElements();
            }
        }

        void SetupElements()
        {
            if (m_Alignment == Align.Center)
                SetCenterAligned();
            else
                SetLeftAligned();

            UIElementsHelper.SetVisibility(m_Help, ShowHelpButton);
            m_Help.clickable.clicked += HelpClicked;
            UIElementsHelper.SetVisibility(m_Menu, ShowMenuButton);
            m_Menu.clickable.clicked += MenuClicked;

            RefreshButtonToggleStates();
            for (int i = 0; i < m_Buttons.Count; i++)
            {
                // copy index value to local scope before enclosing to ensure it isn't Count-1
                int buttonIndex = i;
                m_Buttons[i].clickable.clicked += () => ButtonClicked(buttonIndex);
            }
        }

        void RefreshButtonToggleStates()
        {
            m_CurrentOption = Mathf.Clamp(m_CurrentOption, 0, Mathf.Max(0, m_Buttons.Count - 1));
            for (int i = 0; i < m_Buttons.Count; i++)
            {
                m_Buttons[i].Toggled = i == m_CurrentOption;
            }
        }

        void ButtonClicked(int index)
        {
            m_CurrentOption = index;
            RefreshButtonToggleStates();
            Clicked(index);
        }

        void SetCenterAligned()
        {
            m_Container.RemoveFromClassList(k_ContainerClassLeftAligned);
            m_Container.AddToClassList(k_ContainerClassCenterAligned);
            m_Content.RemoveFromClassList(k_ClassLeftAligned);
            m_Content.AddToClassList(k_ClassCenterAligned);
        }

        void SetLeftAligned()
        {
            m_Container.RemoveFromClassList(k_ContainerClassCenterAligned);
            m_Container.AddToClassList(k_ContainerClassLeftAligned);
            m_Content.RemoveFromClassList(k_ClassCenterAligned);
            m_Content.AddToClassList(k_ClassLeftAligned);
        }

        /// <summary>
        /// Instantiates a <see cref="Ribbon"/> using the data read from a UXML file.
        /// </summary>
        public new class UxmlFactory : UxmlFactory<Ribbon, UxmlTraits> {}

        /// <summary>
        /// Defines <see cref="UxmlTraits"/> for the <see cref="Ribbon"/>.
        /// </summary>
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlEnumAttributeDescription<Align> m_Align = new UxmlEnumAttributeDescription<Align> { name = "alignment", defaultValue = Align.Center };
            UxmlBoolAttributeDescription m_ShowHelp = new UxmlBoolAttributeDescription { name = "show-help-button", defaultValue = false };
            UxmlBoolAttributeDescription m_ShowMenu = new UxmlBoolAttributeDescription { name = "show-menu-button", defaultValue = false };
            UxmlIntAttributeDescription m_InitialOption = new UxmlIntAttributeDescription { name = "initial-option", defaultValue = 0 };
            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
            {
                get
                {
                    // can only contain ribbon buttons
                    yield return new UxmlChildElementDescription(typeof(RibbonButton));
                }
            }


            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var ribbon = ((Ribbon)ve);
                var align = m_Align.GetValueFromBag(bag, cc);
                var help = m_ShowHelp.GetValueFromBag(bag, cc);
                var menu = m_ShowMenu.GetValueFromBag(bag, cc);
                var initialOption = m_InitialOption.GetValueFromBag(bag, cc);

                ribbon.Alignment = align;
                ribbon.ShowHelpButton = help;
                ribbon.ShowMenuButton = menu;
                ribbon.InitialOption = initialOption;

                ((Ribbon)ve).Init();
            }
        }
    }
}

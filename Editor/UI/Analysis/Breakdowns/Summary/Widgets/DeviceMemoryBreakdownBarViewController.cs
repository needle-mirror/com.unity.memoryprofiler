using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class DeviceMemoryBreakdownBarViewController : MemoryBreakdownBarViewController
    {
        const string k_UxmlMemoryUsageBarContainerA = "memory-usage-breakdown__bar__container-a";
        const string k_UxmlMemoryUsageBarContainerB = "memory-usage-breakdown__bar__container-b";
        const string k_UxmlMemoryUsageBarBreakdownBar = "memory-usage-breakdown__bar";
        const string k_UxmlMemoryUsageBarCellCritical = "memory-usage-breakdown__bar__element-critical";
        const string k_UxmlMemoryUsageBarCellCriticalColor = "memory-usage-breakdown__bar__element-critical-color";

        const string k_UxmlStateContainer = "memory-usage-breakdown__device__container";
        const string k_UxmlStyleGaugeCellBase = "memory-usage-breakdown__device__state-";
        const string k_UxmlStyleGaugeCellRemainder = "memory-usage-breakdown__device__state-remainder";

        const string k_UxmlStyleBarSignpost = "memory-usage-breakdown__bar__signpost";
        const string k_UxmlStyleBarSignpostPole = "memory-usage-breakdown__bar__signpost-pole";
        const string k_UxmlStyleBarSignpostIcon = "memory-usage-breakdown__bar__signpost-icon-";

        const string k_SignpostToolTipWarning = "Beyond this threshold, the application might be using more memory than recommended for the final release";
        const string k_SignpostToolTipCritical = "The application is at risk of not running correctly on a device or might get a low memory warning";
        const string k_SignpostToolTipMaximum = "Amount of hardware memory installed on the device";

        // Model.
        readonly DeviceMemoryBreakdownModel m_Model;

        // View.
        VisualElement m_ContainerA;
        VisualElement m_BarA;

        VisualElement m_ContainerB;
        VisualElement m_BarB;

        public DeviceMemoryBreakdownBarViewController(DeviceMemoryBreakdownModel model)
            : base(model)
        {
            m_Model = model;

            Debug.Assert(m_Model.Rows.Count == 1, "Device memory breakdown expects model with only 1 record about resident memory");
        }

        protected override void GatherViewReferences()
        {
            base.GatherViewReferences();

            m_ContainerA = View.Q(k_UxmlMemoryUsageBarContainerA);
            m_BarA = m_ContainerA.Q(k_UxmlMemoryUsageBarBreakdownBar);

            m_ContainerB = View.Q(k_UxmlMemoryUsageBarContainerB);
            m_BarB = m_ContainerB.Q(k_UxmlMemoryUsageBarBreakdownBar);
        }

        protected override void RefreshView()
        {
            base.RefreshView();

            // Maximum value for both bars
            var maxValue = Math.Max(m_Model.StateA.MaximumAvailable, m_Model.StateB.MaximumAvailable);

            // Build breakdown bar normal/warning/critical gauge for snapshot *A*
            var maxValueBarA = Normalize ? m_Model.StateA.MaximumAvailable : maxValue;
            MakeBarGauge(m_ContainerA, m_Model.StateA, maxValueBarA);
            MakeCriticalSection(m_BarA, m_Model.StateA);

            // Build breakdown bar normal/warning/critical gauge for snapshot *B*
            if (m_Model.CompareMode)
            {
                var maxValueBarB = Normalize ? m_Model.StateB.MaximumAvailable : maxValue;
                MakeBarGauge(m_ContainerB, m_Model.StateB, maxValueBarB);
                MakeCriticalSection(m_BarB, m_Model.StateB);
            }
        }

        void MakeBarGauge(VisualElement root, DeviceMemoryBreakdownModel.State state, ulong maxValue)
        {
            // Create gauge container
            var bar = root.Q(k_UxmlStateContainer);
            if (bar == null)
            {
                bar = new VisualElement();
                bar.name = k_UxmlStateContainer;
                bar.AddToClassList(k_UxmlStateContainer);
            }

            // Remove all old bar parts
            bar.Clear();

            // Create cells
            MakeBarGaugeCell(bar, "normal", state.WarningLevel, maxValue, k_SignpostToolTipWarning);
            MakeBarGaugeCell(bar, "warning", state.CriticalLevel - state.WarningLevel, maxValue, k_SignpostToolTipCritical);
            MakeBarGaugeCell(bar, "critical", state.MaximumAvailable - state.CriticalLevel, maxValue, k_SignpostToolTipMaximum);
            MakeBarGaugeRemainder(bar, maxValue - state.MaximumAvailable, maxValue);
            root.Add(bar);
            bar.SendToBack();
        }

        void MakeBarGaugeCell(VisualElement root, string style, ulong size, ulong total, string signpostTooltip)
        {
            // Background
            float sizePercent = ((float)size * 100) / total;
            var section = new VisualElement();
            section.AddToClassList(k_UxmlStyleGaugeCellBase + style);
            section.style.flexGrow = (float)Math.Round(sizePercent);
            root.Add(section);

            //// Signpost indicator
            //var signicon = new VisualElement();
            //signicon.AddToClassList(k_UxmlStyleBarSignpostIcon + style);

            //var signpole = new BackgroundPattern();
            //signpole.AddToClassList(k_UxmlStyleBarSignpostPole);

            //var signpost = new VisualElement();
            //signpost.tooltip = signpostTooltip;
            //signpost.AddToClassList(k_UxmlStyleBarSignpost);
            //signpost.Add(signicon);
            //signpost.Add(signpole);
            //root.Add(signpost);
            //signpost.BringToFront();

            //// Callback to position signpost at the right edge of a section
            //section.RegisterCallback<GeometryChangedEvent>((e) => UpdateSignpostMarker(e, signpost));
        }

        void MakeBarGaugeRemainder(VisualElement root, ulong value, ulong total)
        {
            if (value >= total)
                return;

            float widthInPercent = ((float)value) / total * 100;

            var cell = new VisualElement();
            cell.AddToClassList(k_UxmlStyleGaugeCellRemainder);
            cell.style.flexGrow = (float)Math.Round(widthInPercent, 1);
            cell.style.marginLeft = cell.style.marginRight = (StyleLength)1.5;
            root.Add(cell);
        }

        void MakeCriticalSection(VisualElement root, DeviceMemoryBreakdownModel.State state)
        {
            if (root.childCount <= 0)
                return;
            if (state.Resident <= state.CriticalLevel)
                return;

            var overrun = state.Resident - state.CriticalLevel;
            float widthInPercent = ((float)overrun) / state.Resident * 100;

            // Bar children - sections of the bar
            var element = root.Children().First();

            var cell = new VisualElement();
            cell.AddToClassList(k_UxmlMemoryUsageBarCellCritical);
            cell.style.SetBarWidthInPercent(widthInPercent);
            element.Add(cell);

            var colorFiller = new VisualElement();
            colorFiller.AddToClassList(k_UxmlMemoryUsageBarCellCriticalColor);
            cell.Add(colorFiller);
        }

        void UpdateSignpostMarker(GeometryChangedEvent evnt, VisualElement element)
        {
            element.style.left = Mathf.Floor(evnt.newRect.xMax - element.layout.width / 2);
        }
    }
}

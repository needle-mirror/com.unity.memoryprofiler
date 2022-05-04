using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.Database;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal abstract class SelectionDetailsProducer
    {
        internal virtual void OnShowDetailsForSelection(ISelectedItemDetailsUI detailsUI, MemorySampleSelection selection, out string summary)
        {
            summary = null;
            OnShowDetailsForSelection(detailsUI, selection);
        }

        public abstract void OnShowDetailsForSelection(ISelectedItemDetailsUI detailsUI, MemorySampleSelection selection);

        public virtual void OnClearSelectionDetails(ISelectedItemDetailsUI detailsUI) {}
    }

    internal class SelectionDetailsFactory
    {
        Dictionary<MemorySampleSelectionType, List<SelectionDetailsProducer>> m_RegisteredProducers
            = new Dictionary<MemorySampleSelectionType, List<SelectionDetailsProducer>>();

        public void RegisterCustomDetailsDrawer(MemorySampleSelectionType selectionType, SelectionDetailsProducer producer)
        {
            if (!m_RegisteredProducers.ContainsKey(selectionType))
                m_RegisteredProducers.Add(selectionType, new List<SelectionDetailsProducer>());
            m_RegisteredProducers[selectionType].Add(producer);
        }

        public void DeregisterCustomDetailsDrawer(MemorySampleSelectionType selectionType, SelectionDetailsProducer producer)
        {
            m_RegisteredProducers[selectionType].Remove(producer);
        }

        internal bool Produce(MemorySampleSelection selection, ISelectedItemDetailsUI selectedItemDetailsUI, out string summary)
        {
            bool success = false;
            summary = null;
            List<SelectionDetailsProducer> producers;
            if (m_RegisteredProducers.TryGetValue(selection.Type, out producers))
            {
                foreach (var producer in producers)
                {
                    producer.OnShowDetailsForSelection(selectedItemDetailsUI, selection, out summary);
                    success = true;
                }
            }
            return success;
        }

        internal void Clear(MemorySampleSelectionType selectionType, ISelectedItemDetailsUI selectedItemDetailsUI)
        {
            List<SelectionDetailsProducer> producers;
            if (m_RegisteredProducers.TryGetValue(selectionType, out producers))
            {
                foreach (var producer in producers)
                {
                    producer.OnClearSelectionDetails(selectedItemDetailsUI);
                }
            }
        }
    }
}

using System.Collections.Generic;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal interface ISelectionDetailsProducer
    {
        void OnShowDetailsForSelection(ISelectedItemDetailsUI ui, MemorySampleSelection memorySampleSelection);
        void OnShowDetailsForSelection(ISelectedItemDetailsUI ui, MemorySampleSelection memorySampleSelection, out string summary);
        void OnClearSelectionDetails(ISelectedItemDetailsUI detailsUI);
    }

    internal class SelectionDetailsFactory
    {
        Dictionary<MemorySampleSelectionType, List<ISelectionDetailsProducer>> m_RegisteredProducers
            = new Dictionary<MemorySampleSelectionType, List<ISelectionDetailsProducer>>();

        public void RegisterCustomDetailsDrawer(MemorySampleSelectionType selectionType, ISelectionDetailsProducer producer)
        {
            if (!m_RegisteredProducers.ContainsKey(selectionType))
                m_RegisteredProducers.Add(selectionType, new List<ISelectionDetailsProducer>());
            m_RegisteredProducers[selectionType].Add(producer);
        }

        public void DeregisterCustomDetailsDrawer(MemorySampleSelectionType selectionType, ISelectionDetailsProducer producer)
        {
            if (m_RegisteredProducers.TryGetValue(selectionType, out var producersList))
                producersList.Remove(producer);
        }

        internal bool Produce(MemorySampleSelection selection, ISelectedItemDetailsUI selectedItemDetailsUI, out string summary)
        {
            bool success = false;
            summary = null;
            List<ISelectionDetailsProducer> producers;
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
            List<ISelectionDetailsProducer> producers;
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

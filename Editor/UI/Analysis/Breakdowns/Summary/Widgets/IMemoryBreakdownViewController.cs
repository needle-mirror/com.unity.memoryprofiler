using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal interface IMemoryBreakdownViewController
    {
        event Action<MemoryBreakdownModel, int> OnRowSelected;
        event Action<MemoryBreakdownModel, int> OnRowDeselected;
        event Action<MemoryBreakdownModel> OnInspectDetails;

        bool Normalized { get; set; }

        void ClearSelection();

        void GetRowDescription(int index, out string name, out string descr, out string docsUrl);
    }
}

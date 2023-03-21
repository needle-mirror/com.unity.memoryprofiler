using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal interface IMemorySummaryViewController
    {
        event Action<MemorySummaryModel, int> OnRowSelected;

        bool Normalized { get; set; }

        void ClearSelection();
        ViewController MakeSelection(int rowId);
    }
}

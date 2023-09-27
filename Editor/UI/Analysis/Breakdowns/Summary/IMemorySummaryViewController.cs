using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal interface IMemorySummaryViewController : IDisposable
    {
        event Action<MemorySummaryModel, int> OnRowSelected;

        bool Normalized { get; set; }

        void ClearSelection();
        ViewController MakeSelection(int rowId);
        void Update();
    }
}

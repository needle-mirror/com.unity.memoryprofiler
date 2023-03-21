namespace Unity.MemoryProfiler.Editor.UI
{
    internal interface ISelectionDetails
    {
        public void SetSelection(ViewController controller);
        public void ClearSelection();
    }
}

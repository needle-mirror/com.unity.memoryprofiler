using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class ProgressBar : VisualElement
    {
        const string k_UxmlClass = "progress-bar";
        const string k_UxmlClass_Fill = "progress-bar__fill";

        public ProgressBar()
        {
            AddToClassList(k_UxmlClass);

            Fill = new VisualElement()
            {
                style =
                {
                    flexGrow = 1,
                }
            };
            Fill.AddToClassList(k_UxmlClass_Fill);
            Add(Fill);

            SetProgress(0);
        }

        public VisualElement Fill { get; }

        public void SetProgress(float progress)
        {
            var clampedProgress = Mathf.Clamp01(progress);
            Fill.style.width = new StyleLength(Length.Percent(clampedProgress * 100));
        }

        public new class UxmlFactory : UxmlFactory<ProgressBar> { }
    }
}

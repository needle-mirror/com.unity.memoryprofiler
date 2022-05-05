#if UNITY_2022_1_OR_NEWER
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // A UI Component used to display a size quantity as a fraction of a whole, such as on the Unity Objects breakdown tables.
    // Contains a progress bar to visually show the relative size, along with a footer that contains two labels for describing the values, one left-aligned and one right-aligned.
    class DetailedSizeBar : VisualElement
    {
        const string k_UxmlClass = "detailed-size-bar";
        const string k_UxmlClass_Bar = "detailed-size-bar__bar";
        const string k_UxmlClass_Footer = "detailed-size-bar__footer";
        const string k_UxmlClass_SizeLabel = "detailed-size-bar__size-label";
        const string k_UxmlClass_TotalLabel = "detailed-size-bar__total-label";

        public DetailedSizeBar()
        {
            Bar = new ProgressBar()
            {
                style =
                {
                    flexGrow = 1,
                }
            };
            Bar.AddToClassList(k_UxmlClass_Bar);
            Add(Bar);

            var footer = new VisualElement()
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                }
            };
            footer.AddToClassList(k_UxmlClass_Footer);
            Add(footer);

            SizeLabel = new Label()
            {
                style =
                {
                    flexGrow = 1,
                }
            };
            SizeLabel.AddToClassList(k_UxmlClass_SizeLabel);
            footer.Add(SizeLabel);

            TotalLabel = new Label();
            TotalLabel.AddToClassList(k_UxmlClass_TotalLabel);
            footer.Add(TotalLabel);

            AddToClassList(k_UxmlClass);
        }

        public ProgressBar Bar { get; }

        Label SizeLabel { get; }

        Label TotalLabel { get; }

        public void SetRelativeSize(float relativeSize)
        {
            Bar.SetProgress(relativeSize);
        }

        public void SetSizeText(string text)
        {
            SizeLabel.text = text;
        }

        public void SetTotalText(string text)
        {
            TotalLabel.text = text;
        }

        public new class UxmlFactory : UxmlFactory<DetailedSizeBar> {}
    }
}
#endif

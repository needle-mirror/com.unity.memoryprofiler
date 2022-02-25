using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Unity.MemoryProfiler.Editor.UI;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
[assembly: UxmlNamespacePrefix("Unity.MemoryProfiler.Editor", "MemoryProfiler")]
namespace Unity.MemoryProfiler.Editor
{
    internal class WorkbenchSplitter : VisualElement
    {
        public VisualElement LeftPane { get; private set; }
        public VisualElement RightPane { get; private set; }

        public event Action<float> LeftPaneWidthChanged = delegate {};

        VisualElement m_DragLine;

        public new class UxmlFactory : UxmlFactory<WorkbenchSplitter, UxmlTraits> {}

        public float workbenchWidth { get { return LeftPane.style.width.value.value; } set { LeftPane.style.width = value; } }

        public WorkbenchSplitter() : this(GeneralStyles.InitialWorkbenchWidth) {}

        public WorkbenchSplitter(float initialWorkbenchWidth)
        {
            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Row;

            VisualElement leftChild = null;
            VisualElement rightChild = null;
            if (childCount > 0)
            {
                var children = Children().GetEnumerator();
                children.MoveNext();
                leftChild = children.Current;
                if (children.MoveNext())
                    rightChild = children.Current;
                children.Dispose();
            }

            LeftPane = new VisualElement();
            LeftPane.name = "splitterLeftPane";

            LeftPane.style.width = initialWorkbenchWidth;
            Add(LeftPane);

            if (leftChild != null)
            {
                Remove(leftChild);
                LeftPane.Add(leftChild);
            }

            var dragLineAnchor = new VisualElement();
            dragLineAnchor.name = "splitterDraglineAnchor";
            Add(dragLineAnchor);

            m_DragLine = new VisualElement();
            m_DragLine.name = "splitterDragline";
            var resizer = new SquareResizer(LeftPane);
            m_DragLine.AddManipulator(resizer);
            resizer.LeftPaneWidthChanged += (f) => LeftPaneWidthChanged(f);

            dragLineAnchor.Add(m_DragLine);

            RightPane = new VisualElement();
            RightPane.style.flexGrow = 1;
            Add(RightPane);


            if (rightChild != null)
            {
                Remove(rightChild);
                LeftPane.Add(rightChild);
            }
        }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlFloatAttributeDescription m_WorkbenchWidth =
                new UxmlFloatAttributeDescription { name = "workbench-width", defaultValue = GeneralStyles.InitialWorkbenchWidth };

            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
            {
                get
                {
                    yield return new UxmlChildElementDescription(typeof(VisualElement));
                }
            }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var ate = ve as WorkbenchSplitter;

                ate.workbenchWidth = m_WorkbenchWidth.GetValueFromBag(bag, cc);
            }
        }

        class SquareResizer : MouseManipulator
        {
            Vector2 m_Start;
            protected bool m_Active;
            VisualElement m_LeftPane;

            public event Action<float> LeftPaneWidthChanged = delegate {};

            public SquareResizer(VisualElement leftPane)
            {
                m_LeftPane = leftPane;
                activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
                m_Active = false;
            }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<MouseDownEvent>(OnMouseDown);
                target.RegisterCallback<MouseMoveEvent>(OnMouseMove);
                target.RegisterCallback<MouseUpEvent>(OnMouseUp);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<MouseDownEvent>(OnMouseDown);
                target.UnregisterCallback<MouseMoveEvent>(OnMouseMove);
                target.UnregisterCallback<MouseUpEvent>(OnMouseUp);
            }

            protected void OnMouseDown(MouseDownEvent e)
            {
                if (m_Active)
                {
                    e.StopImmediatePropagation();
                    return;
                }

                if (CanStartManipulation(e))
                {
                    m_Start = e.localMousePosition;

                    m_Active = true;
                    target.CaptureMouse();
                    e.StopPropagation();
                }
            }

            protected void OnMouseMove(MouseMoveEvent e)
            {
                if (!m_Active || !target.HasMouseCapture())
                    return;

                Vector2 diff = e.localMousePosition - m_Start;

                m_LeftPane.style.width = m_LeftPane.layout.width + diff.x;

                if (diff.x != 0)
                    LeftPaneWidthChanged(m_LeftPane.layout.width);

                e.StopPropagation();
            }

            protected void OnMouseUp(MouseUpEvent e)
            {
                if (!m_Active || !target.HasMouseCapture() || !CanStopManipulation(e))
                    return;

                m_Active = false;
                target.ReleaseMouse();
                e.StopPropagation();
            }
        }
    }
}

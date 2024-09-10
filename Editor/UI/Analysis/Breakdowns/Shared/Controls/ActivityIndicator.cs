#if UNITY_2022_1_OR_NEWER
using System.Collections;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
#if UNITY_6000_0_OR_NEWER
    [UxmlElement]
#endif
    partial class ActivityIndicator : VisualElement
    {
        // Animation rotation speed, measured in degrees-per-second. Defaults to 360 degrees-per-second.
        float m_RotationSpeed = 360f;

        public bool IsAnimating { get; private set; }

        public void StartAnimating()
        {
            IsAnimating = true;
            EditorCoroutineUtility.StartCoroutine(Animate(), this);
        }

        public void StopAnimating()
        {
            IsAnimating = false;
        }

        IEnumerator Animate()
        {
            float lastRotationAngle = 0f;
            double lastUpdateTime = EditorApplication.timeSinceStartup;
            while (IsAnimating)
            {
                var currentTime = EditorApplication.timeSinceStartup;
                var deltaTime = System.Convert.ToSingle(currentTime - lastUpdateTime);

                var increment = (m_RotationSpeed * deltaTime);
                var rotationAngle = lastRotationAngle + increment;
                style.rotate = new StyleRotate(new Rotate(rotationAngle));

                lastRotationAngle = rotationAngle;
                lastUpdateTime = currentTime;

                yield return null;
            }
        }

#if !UNITY_6000_0_OR_NEWER
        public new class UxmlFactory : UxmlFactory<ActivityIndicator> {}
#endif
    }
}
#endif

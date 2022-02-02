using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class MemoryUsageDial : VisualElement
    {
        byte m_ValueCutoff = 0;
        int m_Percentage = 55;
        public int Percentage
        {
            get { return m_Percentage; }
            set
            {
                SetValue(value);
            }
        }
        byte m_ThresholdYellow = 0;
        int m_ThresholdYellowPercentage = 50;
        public int YellowThresholdPercentage
        {
            get { return m_ThresholdYellowPercentage; }
            set
            {
                SetThresholds(value, m_ThresholdRedPercentage);
            }
        }
        byte m_ThresholdRed = 0;
        int m_ThresholdRedPercentage = 75;
        public int RedThresholdPercentage
        {
            get { return m_ThresholdRedPercentage; }
            set
            {
                SetThresholds(m_ThresholdYellowPercentage, value);
            }
        }

        VisualElement m_Indicator;
        VisualElement m_IndicatorRoot;
        Label m_Label;
        Texture2D m_BaseTexture;
        Texture2D m_Texture;

        static readonly Color32 k_Green = new Color32(136, 176, 49, byte.MaxValue);
        static readonly Color32 k_Yellow = new Color32(221, 124, 69, byte.MaxValue);
        static readonly Color32 k_Red = new Color32(219, 89, 81, byte.MaxValue);
        static readonly ushort[] k_Indices = new ushort[]
        {
            0, 1, 2,
            1, 3, 2,
        };

        public MemoryUsageDial() : base()
        {
            SetThresholds(m_ThresholdYellowPercentage, m_ThresholdRedPercentage, force: true);
            RegenerateTexture();
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            generateVisualContent = GenerateVisualContent;
        }

        ~MemoryUsageDial()
        {
            // Finalizer might fire outside of Main Thread, punt texture cleanup over to main thread via coroutines. The Texture must own this coroutine since this Dial is about to cross the Jordan.
            Unity.EditorCoroutines.Editor.EditorCoroutineUtility.StartCoroutine(TextureCleanup(), m_Texture);
            m_Texture = null;
        }

        IEnumerator TextureCleanup()
        {
            yield return null;
            if (m_Texture)
                UnityEngine.Object.DestroyImmediate(m_Texture);
        }

        public void SetThresholds(int yellowPercentage, int redPercentage, bool force = false)
        {
            if (!force && yellowPercentage == m_ThresholdYellowPercentage && redPercentage == m_ThresholdRedPercentage)
                return;
            m_ThresholdYellowPercentage = yellowPercentage;
            m_ThresholdRedPercentage = redPercentage;
            m_ThresholdYellow = (byte)(byte.MaxValue * (yellowPercentage / 100f));
            m_ThresholdRed = (byte)(byte.MaxValue * (redPercentage / 100f));
            RegenerateTexture();
        }

        void SetValue(int percentage, bool force = false)
        {
            if (!force && Percentage == percentage)
                return;

            if (m_IndicatorRoot == null)
            {
                InitVisualChildElements();
            }
            var f = percentage / 100f;
            if (m_IndicatorRoot != null)
                m_IndicatorRoot.transform.rotation = Quaternion.Euler(0, 0, 180 * f);
            if (m_Label != null)
                m_Label.text = string.Format("{0:0}%", percentage);
            m_Percentage = percentage;
            m_ValueCutoff = (byte)((percentage / 100f) * byte.MaxValue);
            RegenerateTexture();
            MarkDirtyRepaint();
        }

        void Init(int value, int yellow, int red)
        {
            InitVisualChildElements();
            SetThresholds(yellow, red, force: true);
            SetValue(value, force: true);
        }

        void InitVisualChildElements()
        {
            m_IndicatorRoot = this.Q("memory-usage-dial__root");
            m_Indicator = this.Q("memory-usage-dial__indicator");
            m_Label = this.Q<Label>("memory-usage-dial__label");
        }

        void GenerateVisualContent(MeshGenerationContext obj)
        {
            if (m_Texture == null)
                RegenerateTexture();
            Quad(contentRect.position, contentRect.size, Color.white, m_Texture, obj);
        }

        void Quad(Vector2 pos, Vector2 size, Color color, Texture2D texture2D, MeshGenerationContext context)
        {
            var mesh = context.Allocate(4, 6, texture2D);
            var x0 = pos.x;
            var y0 = pos.y;

            var x1 = pos.x + size.x;
            var y1 = pos.y + size.y;

            mesh.SetNextVertex(new Vertex()
            {
                position = new Vector3(x0, y0, Vertex.nearZ),
                tint = color,
                uv = new Vector2(0, 1) * mesh.uvRegion.size + mesh.uvRegion.position
            });
            mesh.SetNextVertex(new Vertex()
            {
                position = new Vector3(x1, y0, Vertex.nearZ),
                tint = color,
                uv = new Vector2(1, 1) * mesh.uvRegion.size + mesh.uvRegion.position
            });
            mesh.SetNextVertex(new Vertex()
            {
                position = new Vector3(x0, y1, Vertex.nearZ),
                tint = color,
                uv = new Vector2(0, 0) * mesh.uvRegion.size + mesh.uvRegion.position
            });

            mesh.SetNextVertex(new Vertex()
            {
                position = new Vector3(x1, y1, Vertex.nearZ),
                tint = color,
                uv = new Vector2(1, 0) * mesh.uvRegion.size + mesh.uvRegion.position
            });

            mesh.SetAllIndices(k_Indices);
        }

        void OnCustomStyleResolved(CustomStyleResolvedEvent e)
        {
            RegenerateTexture();
        }

        void RegenerateTexture()
        {
            if (m_BaseTexture == null)
            {
#if UNITY_2020_1_OR_NEWER
                m_BaseTexture = resolvedStyle.backgroundImage.texture;
#else
                m_BaseTexture = EditorGUIUtility.Load("Packages/com.unity.memoryprofiler/Package Resources/Icons/MemoryUsageDial_Ring.png") as Texture2D;
#endif
            }
            if (m_BaseTexture == null)
                return;

            if (m_Texture != null)
            {
                if (m_Texture.width != m_BaseTexture.width || m_Texture.height != m_BaseTexture.height)
                {
                    UnityEngine.Object.DestroyImmediate(m_Texture);
                    m_Texture = new Texture2D((int)m_BaseTexture.width, m_BaseTexture.height, TextureFormat.RGBA32, false, true);
                }
            }
            else
                m_Texture = new Texture2D((int)m_BaseTexture.width, m_BaseTexture.height, TextureFormat.RGBA32, false, true);

            m_Texture.name = "MemoryUsageDial Generated";
            m_Texture.wrapMode = TextureWrapMode.Clamp;
            m_Texture.filterMode = FilterMode.Point;
            m_Texture.hideFlags = HideFlags.HideAndDontSave;

            var rawTexture = m_Texture.GetRawTextureData<Color32>();
            var rawBaseTexture = m_BaseTexture.GetRawTextureData<Color32>();
            unsafe
            {
                var ptr = rawTexture.GetUnsafePtr();
                var ptr2 = rawBaseTexture.GetUnsafePtr();
                UnsafeUtility.MemCpy(ptr, ptr2, rawTexture.Length * UnsafeUtility.SizeOf<Color32>());

                Color32* c = (Color32*)ptr;
                for (int i = 0; i < rawTexture.Length; ++i, ++c)
                {
                    var a = c->a;
                    if (a <= 0)
                        continue;

                    if (c->r > m_ValueCutoff)
                        a = (byte)(a / 2);

                    if (c->r > m_ThresholdRed)
                        *c = k_Red;
                    else if (c->r > m_ThresholdYellow)
                        *c = k_Yellow;
                    else
                        *c = k_Green;

                    c->a = a;
                }
            }
            m_Texture.Apply(false, false);
        }

        /// <summary>
        /// Instantiates a <see cref="MemoryUsageDial"/> using the data read from a UXML file.
        /// </summary>
        public new class UxmlFactory : UxmlFactory<MemoryUsageDial, UxmlTraits> {}

        /// <summary>
        /// Defines <see cref="UxmlTraits"/> for the <see cref="MemoryUsageDial"/>.
        /// </summary>
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlIntAttributeDescription m_Value = new UxmlIntAttributeDescription { name = "percentage", defaultValue = 55 };
            UxmlIntAttributeDescription m_YellowThreshold = new UxmlIntAttributeDescription { name = "yellow-threshold-percentage", defaultValue = 50 };
            UxmlIntAttributeDescription m_RedThreshold = new UxmlIntAttributeDescription { name = "red-threshold-percentage", defaultValue = 75 };

            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
            {
                get { yield break; }
            }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var value = m_Value.GetValueFromBag(bag, cc);
                var yellow = m_YellowThreshold.GetValueFromBag(bag, cc);
                var red = m_RedThreshold.GetValueFromBag(bag, cc);

                ((MemoryUsageDial)ve).Init(value, yellow, red);
            }
        }
    }
}

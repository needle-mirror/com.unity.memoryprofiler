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
    internal class BackgroundPattern : VisualElement
    {
        public float Scale { get; set; }

        Texture2D m_Texture;

        static readonly ushort[] k_Indices = new ushort[] {
            0, 1, 2,
            1, 3, 2,
        };

        public BackgroundPattern() : base()
        {
            generateVisualContent = GenerateVisualContent;
        }


        void Init(float scale)
        {
            Scale = scale;
        }

        void GenerateVisualContent(MeshGenerationContext obj)
        {
            if(m_Texture == null)
            {
#if UNITY_2020_1_OR_NEWER
                m_Texture = resolvedStyle.backgroundImage.texture;
#else
                m_Texture = EditorGUIUtility.Load("Packages/com.unity.memoryprofiler/Package Resources/Icons/Profiler.UnknownMemoryBackground.png") as Texture2D;
#endif
            }
            if (m_Texture == null)
                return;
            TiledQuads(new Vector2(0,0), localBound.size, Color.white, Scale, m_Texture, obj);
        }

        void TiledQuads(Vector2 pos, Vector2 size, Color color, float scale, Texture2D texture2D, MeshGenerationContext context)
        {
            var tW = texture2D.width * scale;
            var tH = texture2D.height * scale;

            var xRepeat = size.x / tW;
            var yRepeat = size.y / tH;
            var xTiles = (int)xRepeat;
            var xTaper = xRepeat - xTiles;
            if (xTaper <= 0)
                xTaper = 1;
            else
                ++xTiles;
            var yTiles = (int)yRepeat;
            var yTaper = yRepeat - yTiles;
            if (yTaper <= 0)
                yTaper = 1;
            else
                ++yTiles;

            var totalTiles = xTiles * yTiles;

            var uvOffset = new Vector2(0,0);
            var lastRowUvOffset = new Vector2(0, 1 - yTaper);

            // if more quads are needed than ushort allows for, split the mesh.
            const int k_VertextCountPerQuad = 4;
            const int k_IndexCountPerQuad = 6;
            int quadIndexToSplitMeshAt = totalTiles;
            if (totalTiles * k_IndexCountPerQuad > ushort.MaxValue)
               quadIndexToSplitMeshAt = short.MaxValue / k_IndexCountPerQuad;
            
            var mesh = context.Allocate(k_VertextCountPerQuad * quadIndexToSplitMeshAt, k_IndexCountPerQuad * quadIndexToSplitMeshAt, texture2D);
            ushort vertextOffset = 0;

            for (int q = 0; q < totalTiles; q++)
            {
                var x = q % xTiles;
                var y = q / xTiles;

                var x0 = pos.x + tW * x;
                var y0 = pos.y + tH * y;

                var applicableXTaper = (x == xTiles - 1 ? xTaper : 1);
                var applicableYTaper = (y == yTiles - 1 ? yTaper : 1);

                QuadA(new Vector2(x0, y0),
                    new Vector2(tW * applicableXTaper, tH * applicableYTaper),
                    new Vector2(applicableXTaper, 1),
                    (y == yTiles - 1 ? lastRowUvOffset : uvOffset),
                    color, mesh, vertextOffset);

                vertextOffset += k_VertextCountPerQuad;
                
                // Check if the current mesh is full and the next one is needed
                if (q >= quadIndexToSplitMeshAt && q + 1 < totalTiles)
                {
                    // calculate next split
                    var remainingQuads = totalTiles - q;
                    quadIndexToSplitMeshAt = remainingQuads;
                    if (remainingQuads * k_IndexCountPerQuad > ushort.MaxValue)
                        quadIndexToSplitMeshAt = short.MaxValue / k_IndexCountPerQuad;
                    vertextOffset = 0;
                    mesh = context.Allocate(k_VertextCountPerQuad * quadIndexToSplitMeshAt, k_IndexCountPerQuad * quadIndexToSplitMeshAt, texture2D);
                }
            }
        }

        void QuadA(Vector2 pos, Vector2 size, Vector2 uvSize, Vector2 uvOffset, Color color, MeshWriteData mesh, ushort vertextOffset)
        {
            var x0 = pos.x;
            var y0 = pos.y;

            var x1 = pos.x + size.x;
            var y1 = pos.y + size.y;

            mesh.SetNextVertex(new Vertex()
            {
                position = new Vector3(x0, y0, Vertex.nearZ),
                tint = color,
                uv = new Vector2(0, 1) * uvSize * mesh.uvRegion.size + mesh.uvRegion.position
            });
            mesh.SetNextVertex(new Vertex()
            {
                position = new Vector3(x1, y0, Vertex.nearZ),
                tint = color,
                uv = new Vector2(1, 1) * uvSize * mesh.uvRegion.size + mesh.uvRegion.position
            });
            mesh.SetNextVertex(new Vertex()
            {
                position = new Vector3(x0, y1, Vertex.nearZ),
                tint = color,
                uv = new Vector2(0, uvOffset.y) * uvSize * mesh.uvRegion.size + mesh.uvRegion.position
            });
            mesh.SetNextVertex(new Vertex()
            {
                position = new Vector3(x1, y1, Vertex.nearZ),
                tint = color,
                uv = new Vector2(1, uvOffset.y) * uvSize * mesh.uvRegion.size + mesh.uvRegion.position
            });

            for (int i = 0; i < k_Indices.Length; i++)
            {
                mesh.SetNextIndex((ushort)(k_Indices[i] + vertextOffset));
            }
        }

        /// <summary>
        /// Instantiates a <see cref="BackgroundPattern"/> using the data read from a UXML file.
        /// </summary>
        public new class UxmlFactory : UxmlFactory<BackgroundPattern, UxmlTraits> { }

        /// <summary>
        /// Defines <see cref="UxmlTraits"/> for the <see cref="BackgroundPattern"/>.
        /// </summary>
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlFloatAttributeDescription m_Scale = new UxmlFloatAttributeDescription { name = "scale", defaultValue = 1f };

            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
            {
                get { yield break; }
            }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var scale = m_Scale.GetValueFromBag(bag, cc);

                ((BackgroundPattern)ve).Init(scale);
            }
        }
    }
}

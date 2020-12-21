Shader "Resources/MemoryMapDiff"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _SrcBlend ("SrcBlend", Int) = 5.0 // SrcAlpha
        _DstBlend ("DstBlend", Int) = 10.0 // OneMinusSrcAlpha
        _ZWrite ("ZWrite", Int) = 1.0 // On
        _ZTest ("ZTest", Int) = 4.0 // LEqual
        _Cull ("Cull", Int) = 0.0 // Off
        _ZBias ("ZBias", Float) = 0.0
        // using Vector instead of Color to avoid automatic Color space correction on setting the color.
        _ColorNotModified ("ColorNotModified", Vector) = (1,1,1,1)
        _ColorDeallocated ("ColorDeallocated", Vector) = (1,1,1,1)
        _ColorModified ("ColorModified", Vector) = (1,1,1,1)
        _ColorAllocated ("ColorAllocated", Vector) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull [_Cull]
            Offset [_ZBias], [_ZBias]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4    _ColorNotModified;
            float4    _ColorDeallocated;
            float4    _ColorModified;
            float4    _ColorAllocated;

            sampler2D _Input0;
            float4    _Input0_TexelSize;

            sampler2D _GUIClipTexture;
            uniform float SelectBegin;
            uniform float SelectEnd;
            uniform float4 _SelectionData;

            uniform float4x4 unity_GUIClipTextureMatrix;

            struct appdata_t {
                float4 vertex : POSITION;
                float3 texel : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                fixed3 texel : TEXCOORD0;
                float2 clipUV : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texel = v.texel;

                float3 eyePos = UnityObjectToViewPos(v.vertex);
                o.clipUV = mul(unity_GUIClipTextureMatrix, float4(eyePos.xy, 0, 1.0));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float rowHeight = 24.0;
                float rowHalf = rowHeight*0.5;

                float4 clip = float4(1,1,1,tex2D(_GUIClipTexture, i.clipUV).a);
                float2 uv = i.texel.xy;

                float2 offsetUV = uv*_Input0_TexelSize.xy;

                float4 group = tex2D(_Input0, float2(0.0f, frac(offsetUV.y))); // index , first line in group, last line in group, 1

                float4 left =  tex2D(_Input0, frac(offsetUV - float2(_Input0_TexelSize.x, 0)));
                float4 right = tex2D(_Input0, frac(offsetUV + float2( _Input0_TexelSize.x, 0)));
                float4 diff = tex2D(_Input0, frac(offsetUV));

                float4 color = float4(0,0,0, diff.a);

                float entry = 0;
                if (diff.r > 0 && diff.g > 0 && diff.b == 0)
                {
                    color.rgb += _ColorModified; entry = 0.1;
                }
                else if (diff.r > 0)
                {
                    color.rgb += _ColorDeallocated; entry = 0.1;
                }
                else if (diff.g > 0)
                {
                    color.rgb += _ColorAllocated; entry = 0.1;
                }
                else
                if (diff.r == 0.0 && diff.g == 0.0 && diff.b > 0)
                {
                    color.rgb  = _ColorNotModified.rgb; entry = 0.1;
                }

                float r = frac(i.texel.y);
                float lineBottom = smoothstep(0, 1, (1.0-r)*rowHalf);    // horizontal lines 1...N

                r *= 1.0 + 1.0/rowHeight;

                float lineTop = smoothstep(0,1,saturate(i.texel.z*rowHalf));  // horizontal line 0

                float linesVertical = smoothstep(0,1,saturate(i.texel.x*0.5));      // left edge of region


                if (color.a != left.a)
                {
                    if (right.a != color.a)
                        linesVertical *= 0.9;
                    else
                        linesVertical *= 0.1;
                }

                float  addr = floor(i.texel.y) + offsetUV.x;
                float4 selectColor = float4(1,0.4,0,1);
                float4 lineTopColor = float4(0,0,0,1);
                float4 lineBottomColor = float4(0,0,0,1);

                color = lerp(float4(0,0,0,1), color, linesVertical);

                float SelectBegin = _SelectionData[0];
                float SelectEnd = _SelectionData[1];
                bool isValid = floor(_SelectionData[2]) == 1;

                if (isValid && SelectBegin <= addr && addr <= SelectEnd )
                {
                    color.rgb = color.a > 0
                        ? color.rgb+selectColor*0.35
                        : selectColor;

                    lineTopColor    = selectColor;
                    lineBottomColor = selectColor;
                }

                if (isValid && SelectBegin-1 <= addr && addr <= SelectEnd-1 && group.z < 0.5)
                {
                    lineBottomColor = selectColor;
                }

                if (isValid && SelectBegin <= addr && addr - _Input0_TexelSize.x*0.95 < SelectBegin)
                {
                    lineBottom = 0;
                    lineBottomColor = selectColor;
                }

                if (isValid && SelectEnd < addr + _Input0_TexelSize.x*0.95 && addr <= SelectEnd)
                {
                    lineBottom = 0;
                    lineBottomColor = selectColor;
                }

                color = float4(color.rgb, color.a > 0 ? entry > 0 ? 1.0 : 0.5 : 0.4);

                return lerp(lineTopColor, lerp(lineBottomColor, color, lineBottom),  lineTop)*clip;
            }
            ENDCG
        }
    }
}
//30 MB

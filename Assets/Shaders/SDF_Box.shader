// Unity SDF Shape Shader (UV space)
// Author: GitHub Copilot
Shader "Custom/SDF_Box"
{
    Properties {
        _Color ("Color", Color) = (1,1,1,1)
        _Roundness ("Corner Radius", Float) = 0.0
        _Smooth ("Edge Smoothness", Float) = 0.01
        _TexelSize ("Emulated Texel Size", Vector) = (0,0,0,0)
    }
    SubShader {
    Tags { "RenderType"="Transparent" "Queue"="Transparent+1000" }
    LOD 100
    Blend SrcAlpha OneMinusSrcAlpha
    Cull Off
    Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };
            fixed4 _Color;
            float _Roundness;
            float _Smooth;
            float2 _TexelSize;
            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            float sdRoundBox(float2 p, float2 b, float r) {
                float2 d = abs(p) - b + r;
                return length(max(d,0.0)) + min(max(d.x,d.y),0.0) - r;
            }
            fixed4 frag (v2f i) : SV_Target {
                float2 center = float2(0.5, 0.5);
                float2 halfSize = float2(0.5 - _Roundness, 0.5 - _Roundness);
                float2 p = i.uv - center;
                float d = sdRoundBox(p, halfSize, _Roundness);
                float alpha = smoothstep(_Smooth, 0.0, -d);
                return fixed4(_Color.rgb, _Color.a * (1.0 - alpha));
            }
            ENDCG
        }
    }
}

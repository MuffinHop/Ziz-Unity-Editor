// Unity SDF Shape Shader (UV space)
// Author: GitHub Copilot
Shader "Custom/SDF_Capsule"
{
    Properties {
        _Color ("Color", Color) = (1,1,1,1)
        _Radius ("Radius", Float) = 0.3
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
            float _Radius;
            float _Smooth;
            float2 _TexelSize;
            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            // Capsule from (0.2,0.5) to (0.8,0.5)
            float sdCapsule(float2 p, float2 a, float2 b, float r) {
                float2 pa = p - a, ba = b - a;
                float h = clamp(dot(pa,ba)/dot(ba,ba),0.0,1.0);
                return length(pa - ba*h) - r;
            }
            fixed4 frag (v2f i) : SV_Target {
                float2 a = float2(0.2,0.5);
                float2 b = float2(0.8,0.5);
                float d = sdCapsule(i.uv, a, b, _Radius);
                float alpha = smoothstep(_Smooth, 0.0, -d);
                return fixed4(_Color.rgb, _Color.a * (1.0 - alpha));
            }
            ENDCG
        }
    }
}

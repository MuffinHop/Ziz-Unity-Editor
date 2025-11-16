// Unity SDF Shape Shader (UV space)
// Author: GitHub Copilot
Shader "Custom/SDF_CircleRing"
{
    Properties {
        _Color ("Color", Color) = (1,1,1,1)
        _Radius ("Radius", Float) = 0.4
        _Thickness ("Ring Thickness", Float) = 0.1
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
            float _Thickness;
            float _Smooth;
            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            float sdCircleRing(float2 p, float2 center, float radius, float thickness) {
                float dist = length(p - center);
                return abs(dist - radius) - thickness * 0.5;
            }
            fixed4 frag (v2f i) : SV_Target {
                float2 center = float2(0.5, 0.5);
                float d = sdCircleRing(i.uv, center, _Radius, _Thickness);
                float alpha = smoothstep(_Smooth, 0.0, -d);
                return fixed4(_Color.rgb, _Color.a * (1.0 - alpha));
            }
            ENDCG
        }
    }
}

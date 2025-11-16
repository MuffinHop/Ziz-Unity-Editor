// Unity SDF Shape Shader (UV space)
// Author: GitHub Copilot
Shader "Custom/SDF_Cross"
{
    Properties {
        _Color ("Color", Color) = (1,1,1,1)
        _Thickness ("Bar Thickness", Float) = 0.15
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
            float _Thickness;
            float _Smooth;
            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            float sdCross(float2 p, float thickness) {
                float2 d = abs(p - 0.5);
                float cross = min(d.x, d.y) - thickness * 0.5;
                return cross;
            }
            fixed4 frag (v2f i) : SV_Target {
                float d = sdCross(i.uv, _Thickness);
                float alpha = smoothstep(_Smooth, 0.0, -d);
                return fixed4(_Color.rgb, _Color.a * (1.0 - alpha));
            }
            ENDCG
        }
    }
}

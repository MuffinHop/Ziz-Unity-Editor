// Unity SDF Shape Shader (UV space)
// Author: GitHub Copilot
Shader "Custom/SDF_Star"
{
    Properties {
        _Color ("Color", Color) = (1,1,1,1)
        _Points ("Points", Float) = 5
        _Inner ("Inner Radius", Float) = 0.3
        _Outer ("Outer Radius", Float) = 0.5
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
            float _Points;
            float _Inner;
            float _Outer;
            float _Smooth;
            float2 _TexelSize;
            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            float sdStar(float2 p, float r1, float r2, float n) {
                float2 c = float2(0.5,0.5);
                float2 d = p - c;
                float a = atan2(d.y, d.x);
                float l = length(d);
                float k = 3.14159265/n;
                float m = fmod(a, 2.0*k) - k;
                float r = lerp(r1, r2, abs(cos(n*m)));
                return l - r;
            }
            fixed4 frag (v2f i) : SV_Target {
                float d = sdStar(i.uv, _Outer, _Inner, _Points);
                float alpha = smoothstep(_Smooth, 0.0, -d);
                return fixed4(_Color.rgb, _Color.a * (1.0 - alpha));
            }
            ENDCG
        }
    }
}

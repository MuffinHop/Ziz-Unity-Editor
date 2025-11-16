// Unity SDF Shape Shader (UV space)
// Author: GitHub Copilot
Shader "Custom/SDF_Triangle"
{
    Properties {
        _Color ("Color", Color) = (1,1,1,1)
        _Smooth ("Edge Smoothness", Float) = 0.01
        _TexelSize ("Emulated Texel Size", Vector) = (0,0,0,0)
    }
    SubShader {
    Tags { "RenderType"="Transparent" 
    "Queue"="Transparent+1000" }
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
            float _Smooth;
            float2 _TexelSize;
            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            // SDF for equilateral triangle centered at (0.5,0.5)
            float sdEquilateralTriangle(float2 p) {
                p = p * 2.0 - 1.0;
                const float k = sqrt(3.0);
                p.x = abs(p.x) - 0.5;
                p.y = p.y + 0.288675;
                if( p.x + k*p.y > 0.0 ) p = float2( p.x - k*p.y, -k*p.x - p.y ) / 2.0;
                p.x -= clamp( p.x, -1.0, 0.0 );
                return -length(p)*sign(p.y);
            }
            fixed4 frag (v2f i) : SV_Target {
                float d = sdEquilateralTriangle(i.uv);
                float alpha = smoothstep(_Smooth, 0.0, -d);
                return fixed4(_Color.rgb, _Color.a * (1.0 - alpha));
            }
            ENDCG
        }
    }
}

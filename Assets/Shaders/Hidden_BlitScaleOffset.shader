Shader "Hidden/BlitScaleOffset" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _ScaleX ("ScaleX", Float) = 1
        _ScaleY ("ScaleY", Float) = 1
        _OffsetX ("OffsetX", Float) = 0
        _OffsetY ("OffsetY", Float) = 0
    }
    SubShader {
        Tags { "RenderType"="Opaque" }
        Pass {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            sampler2D _MainTex;
            float _ScaleX; float _ScaleY; float _OffsetX; float _OffsetY;
            v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            fixed4 frag(v2f i) : SV_Target {
                float2 uv = i.uv * float2(_ScaleX, _ScaleY) + float2(_OffsetX, _OffsetY);
                return tex2D(_MainTex, uv);
            }
            ENDCG
        }
    }
}

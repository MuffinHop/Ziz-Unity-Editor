Shader "Custom/NonLit-Textured-VertexColor"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

        // First pass: Write to depth buffer only for opaque parts
        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Back
            ColorMask 0  // Don't write color, only depth
            AlphaTest Greater 0.5

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                
                // Quantize depth to 16-bit precision
                float depth = o.vertex.z / o.vertex.w;
                depth = floor(depth * 65536.0) / 65536.0;
                o.vertex.z = depth * o.vertex.w;
                
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 texColor = tex2D(_MainTex, i.uv);
                fixed4 finalColor = texColor * i.color;
                
                // Alpha test: GL_GREATER, 0.5 - discard if alpha <= 0.5
                if (finalColor.a <= 0.5)
                    discard;
                
                return finalColor;
            }
            ENDCG
        }

        // Second pass: Alpha blending
        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                
                // Quantize depth to 16-bit precision
                float depth = o.vertex.z / o.vertex.w;
                depth = floor(depth * 65536.0) / 65536.0;
                o.vertex.z = depth * o.vertex.w;
                
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 texColor = tex2D(_MainTex, i.uv);
                fixed4 finalColor = texColor * i.color;
                
                // Alpha test: GL_GREATER, 0.5 - discard if alpha <= 0.5
                if (finalColor.a <= 0.5)
                    discard;
                
                return finalColor;
            }
            ENDCG
        }
    }
}
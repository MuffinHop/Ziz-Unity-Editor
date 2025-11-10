Shader "Actor/VertexColoursWithVertexLighting"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float3 vertexLighting : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                
                // Compute vertex lighting
                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                
                // Directional light contribution
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float diffuse = max(0.0, dot(worldNormal, lightDir));
                
                // Ambient + directional light
                o.vertexLighting = float3(0.3, 0.3, 0.3) + _LightColor0.rgb * diffuse;
                
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Apply vertex lighting to color
                fixed3 lit = i.color.rgb * i.vertexLighting;
                
                return fixed4(lit, i.color.a);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}

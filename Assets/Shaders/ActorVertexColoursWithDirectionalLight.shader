Shader "Actor/VertexColoursWithDirectionalLight"
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
                float3 worldNormal : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Normalize the interpolated normal
                float3 normal = normalize(i.worldNormal);
                
                // Get the directional light direction (from _WorldSpaceLightPos0)
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                
                // Calculate diffuse lighting
                float diffuse = max(0.3, dot(normal, lightDir)); // 0.3 ambient minimum
                
                // Apply lighting to vertex color
                fixed3 lit = i.color.rgb * diffuse;
                
                return fixed4(lit, i.color.a);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}

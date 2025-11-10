Shader "Actor/TextureAndVertexColoursAndDirectionalLight"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
    }
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
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 texColor = tex2D(_MainTex, i.uv);
                
                // Normalize the interpolated normal
                float3 normal = normalize(i.worldNormal);
                
                // Get the directional light direction
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                
                // Calculate diffuse lighting
                float diffuse = max(0.3, dot(normal, lightDir)); // 0.3 ambient minimum
                
                // Apply lighting to texture and vertex color
                fixed3 lit = texColor.rgb * i.color.rgb * diffuse;
                float alpha = texColor.a * i.color.a;
                
                return fixed4(lit, alpha);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}

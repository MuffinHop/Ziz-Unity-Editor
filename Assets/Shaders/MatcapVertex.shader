Shader "Custom/MatcapVertex"
{
    Properties
    {
        _MainTex ("Matcap Texture", 2D) = "white" {}
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
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 matcapUV : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                
                // Transform normal to view space and normalize
                float3 viewNormal = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, v.normal));
                
                // Compute matcap UV (normal in view space, mapped to 0-1)
                o.matcapUV = viewNormal.xy * 0.47 + 0.5;
                
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample the matcap texture
                return tex2D(_MainTex, i.matcapUV);
            }
            ENDCG
        }
    }
}
Shader "Hidden/ResolutionEmulator"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _SourceResolution("Source Resolution", Vector) = (1920, 1080, 0, 0)
        _TargetResolution("Target Resolution", Vector) = (320, 240, 0, 0)
    }
    
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100
        
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
            };
            
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _SourceResolution;
            float4 _TargetResolution;
            
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            
            fixed4 frag(v2f i) : SV_Target
            {
                // Calculate pixel size in screen space for the target resolution
                float2 pixelSize = 1.0 / _TargetResolution.xy;
                
                // Snap the UV to the nearest pixel center in target resolution space
                float2 snappedUV = (floor(i.uv / pixelSize) + 0.5) * pixelSize;
                
                // Clamp to valid texture coordinates to prevent sampling outside bounds
                snappedUV = clamp(snappedUV, 0.0, 1.0);
                
                // Sample at the snapped position
                fixed4 col = tex2D(_MainTex, snappedUV);
                return col;
            }
            ENDCG
        }
    }
}


Shader "psx/PhongLitImproved" {
    Properties {
        _MainTex("Base (RGB)", 2D) = "white" {}
        _SpecColor("Specular Color", Color) = (1,1,1,1)
        _Shininess("Shininess", Range(4,128)) = 32
        _NormalQuantSteps("Normal Quant Steps", Range(0,32)) = 0
        _ColorSteps("Color Steps (per channel)", Range(0,32)) = 0
        _SnapX("Snap X Pixels", Float) = 160
        _SnapY("Snap Y Pixels", Float) = 120
        _WobbleStrength("Wobble UV Dist Scale", Float) = 0
        _UseVertexColor("Use Vertex Colors", Float) = 1
        _TextureStrength("Texture Strength", Range(0,1)) = 1
        _LightSteps("Diffuse Light Steps", Range(0,16)) = 0
    }
    SubShader {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        LOD 200
        Pass {
            Cull Back
            ZWrite On
            ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            sampler2D _MainTex; float4 _MainTex_ST; float4 _SpecColor; float _Shininess; float _NormalQuantSteps; float _ColorSteps; float _SnapX; float _SnapY; float _WobbleStrength; float _UseVertexColor; float _TextureStrength; float _LightSteps;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 wPos:TEXCOORD1; float3 wNrm:TEXCOORD2; float4 vColor:COLOR0; UNITY_FOG_COORDS(3) };
            float3 QuantizeNormal(float3 n){ if(_NormalQuantSteps<=0.5) return n; float s=_NormalQuantSteps; return normalize(round(n*s)/s);} float3 QuantizeColor(float3 c){ if(_ColorSteps<=0.5) return c; float s=_ColorSteps-1; return round(c*s)/s;} float StepLight(float x){ if(_LightSteps<=0.5) return x; float s=_LightSteps-1; return round(x*s)/s; }
            v2f vert(appdata v){ v2f o; float4 clip=UnityObjectToClipPos(v.vertex); float invW=1.0/clip.w; float2 ndc=clip.xy*invW; ndc.x=floor(_SnapX*ndc.x)/_SnapX; ndc.y=floor(_SnapY*ndc.y)/_SnapY; clip.xy=ndc*clip.w; o.pos=clip; o.wPos=mul(unity_ObjectToWorld,v.vertex).xyz; o.wNrm=UnityObjectToWorldNormal(v.normal); float2 uv=TRANSFORM_TEX(v.uv,_MainTex); uv+=_WobbleStrength*distance(_WorldSpaceCameraPos,o.wPos)*0.002; o.uv=uv; o.vColor=v.color; UNITY_TRANSFER_FOG(o,o.pos); return o; }
            float4 frag(v2f i):SV_Target{ float3 N=normalize(i.wNrm); N=QuantizeNormal(N); float3 L=normalize(_WorldSpaceLightPos0.xyz); float3 V=normalize(_WorldSpaceCameraPos - i.wPos); float3 H=normalize(L+V); float NdotL=max(0,dot(N,L)); NdotL=StepLight(NdotL); float3 diffuse=NdotL*_LightColor0.rgb; float NdotH=max(0,dot(N,H)); float3 specular=pow(NdotH,_Shininess)*_SpecColor.rgb*_LightColor0.rgb; float3 ambient=UNITY_LIGHTMODEL_AMBIENT.rgb; float3 texCol=tex2D(_MainTex,i.uv).rgb; float3 vcol=(_UseVertexColor>0.5)? i.vColor.rgb : 1; float3 albedo=lerp(1,texCol,_TextureStrength)*vcol; float3 lit=albedo*(ambient+diffuse)+specular; lit=QuantizeColor(lit); UNITY_APPLY_FOG(i.fogCoord, lit); return float4(lit,1); }
            ENDCG
        }
    }
    Fallback "Diffuse"
}

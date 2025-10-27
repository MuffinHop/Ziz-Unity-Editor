Shader "MatCapGenerator/PBR"
{
    Properties {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _Roughness ("Roughness", Range(0,1)) = 0.25
        _UseACES ("Use ACES Tonemap", Float) = 0
        _SpecularTint ("Specular Tint", Range(0,1)) = 0.0
        _LightDir ("Light Direction", Vector) = (0.577, 0.577, 0.577, 0)
        _LightColor ("Light Color", Color) = (1,1,1,1)
        _EnvCube ("Environment Cubemap", Cube) = "_Skybox" {}
        _UseIBL ("Use IBL (1 = env sample)", Float) = 1
        _Exposure ("Exposure", Float) = 1.0
        _GamutClamp ("Clamp Output", Float) = 1
    }

    SubShader {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }
        Cull Off ZWrite Off ZTest Always

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            UNITY_DECLARE_TEXCUBE(_EnvCube);
            float4 _BaseColor;
            float _Metallic;
            float _Roughness;
            float _SpecularTint;
            float4 _LightDir;
            float4 _LightColor;
            float _UseIBL;
            float _Exposure;
            float _GamutClamp;
            float _UseACES;

            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert (appdata_base v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            // PBR helpers
            static const float PI = 3.14159265359;

            float3 fresnel_schlick(float cosTheta, float3 F0)
            {
                return F0 + (1 - F0) * pow(1 - cosTheta, 5);
            }

            float Distribution_GGX(float NdotH, float roughness)
            {
                float a = roughness * roughness;
                float a2 = a * a;
                float denom = (NdotH * NdotH) * (a2 - 1.0) + 1.0;
                return a2 / (PI * denom * denom + 1e-6);
            }

            float Geometry_SchlickGGX(float NdotV, float k)
            {
                return NdotV / (NdotV * (1.0 - k) + k + 1e-6);
            }

            float Geometry_Smith(float NdotV, float NdotL, float k)
            {
                return Geometry_SchlickGGX(NdotV, k) * Geometry_SchlickGGX(NdotL, k);
            }

            float3 linearToGamma(float3 c) {
                return pow(saturate(c), 1.0/2.2);
            }

            // ACES RRT+ODT approximation (Narkowicz fit)
            float3 RRTAndODTFit(float3 v) {
                float3 a = v * (v + 0.0245786) - 0.000090537;
                float3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
                return a / b;
            }

            float3 sampleEnv(float3 R, float roughness)
            {
                // Simple LOD strategy: sample mip based on roughness
                #ifdef UNITY_SAMPLE_TEXCUBE_LOD
                    return UNITY_SAMPLE_TEXCUBE_LOD(_EnvCube, R, roughness * 8.0).rgb;
                #else
                    // Fallback: single-sample environment using Unity macro
                    return UNITY_SAMPLE_TEXCUBE(_EnvCube, R).rgb;
                #endif
            }

            float4 frag (v2f i) : SV_Target {
                // Map uv (0..1) to sphere coords (-1..1)
                float2 uv = i.uv * 2.0 - 1.0;
                float2 xy = uv;
                float r2 = dot(xy, xy);
                if (r2 > 1.0) {
                    // outside sphere - output transparent black
                    return float4(0,0,0,0);
                }

                // Reconstruct sphere normal in view-space (assuming view vector = (0,0,1))
                float nz = sqrt(saturate(1.0 - r2));
                float3 N = normalize(float3(xy.x, xy.y, nz));

                // Camera/view vector points toward viewer (0,0,1)
                float3 V = float3(0, 0, 1);
                float3 L;
                // Light direction param is given in world-like space; assume normalized
                L = normalize(_LightDir.xyz);

                float3 H = normalize(V + L);

                float NdotV = saturate(dot(N, V));
                float NdotL = saturate(dot(N, L));
                float NdotH = saturate(dot(N, H));
                float VdotH = saturate(dot(V, H));

                // Approximate F0 from metallic and base color
                float3 albedo = _BaseColor.rgb;
                float3 F0 = lerp(float3(0.04,0.04,0.04), albedo, _Metallic);

                // Cook-Torrance BRDF
                float rough = max(_Roughness, 0.05); // avoid extreme
                float D = Distribution_GGX(NdotH, rough);
                float k = (rough + 1.0);
                k = k * k / 8.0; // UE-style remapping
                float G = Geometry_Smith(NdotV, NdotL, k);
                float3 F = fresnel_schlick(VdotH, F0);

                float3 spec = (D * G) * F / max(4.0 * NdotV * NdotL, 1e-6);

                // Diffuse term (energy-conserving)
                float3 kd = (1.0 - F) * (1.0 - _Metallic);
                float3 diffuse = kd * albedo / PI;

                // Direct lighting
                float3 radiance = _LightColor.rgb;
                float3 Lo = (diffuse + spec) * radiance * NdotL;

                // IBL/reflection
                float3 ambient = float3(0,0,0);
                if (_UseIBL > 0.5) {
                    // Get reflection vector
                    float3 R = reflect(-V, N);
                    float3 env = sampleEnv(R, rough);
                    // Multiply environment by Fresnel for specular IBL contribution
                    ambient = env * F;
                    // Add diffuse ambient via irradiance approximation (simple lambertian env)
                    // For high quality, provide an irradiance cubemap; here use env as a proxy
                    ambient += env * diffuse;
                } else {
                    ambient = float3(0.03, 0.03, 0.03) * albedo; // fallback ambient
                }

                float3 color = Lo + ambient;
                color *= _Exposure;

                if (_UseACES > 0.5) {
                    // Apply simple ACES RRT+ODT fit and clamp
                    color = RRTAndODTFit(color);
                    color = saturate(color);
                } else {
                    if (_GamutClamp > 0.5) {
                        color = saturate(color);
                    }
                }

                // Convert to gamma for display export
                float3 outCol = linearToGamma(color);
                return float4(outCol, 1.0);
            }

            ENDCG
        }
    }

    FallBack Off
}

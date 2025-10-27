Shader "MatCapGenerator/PBR"
{
    Properties {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _Roughness ("Roughness", Range(0,1)) = 0.25
        _UseACES ("Use ACES Tonemap", Float) = 0
        _SpecularTint ("Specular Tint", Range(0,1)) = 0.0
    _SSSColor ("SSS Color", Color) = (1,0.8,0.7,1)
    _SSSStrength ("SSS Strength", Range(0,1)) = 0.0
    _SSSScale ("SSS Scale", Range(0,1)) = 0.5
    _UseFresnel ("Use Fresnel Diffuse Mod", Float) = 1
    _FresnelPower ("Fresnel Power", Range(0.1,5)) = 1.0
    _FresnelTint ("Fresnel Tint", Color) = (1,1,1,1)
    _FresnelChannelMask ("Fresnel Channel Mask", Vector) = (1,1,1,0)
    _FresnelTintStrength ("Fresnel Tint Strength", Range(0,1)) = 0.5
    _FresnelTintStart ("Fresnel Tint Start", Range(0,1)) = 0.0
    _FresnelTintEnd ("Fresnel Tint End", Range(0,1)) = 1.0
        _LightDir ("Light Direction", Vector) = (0.577, 0.577, 0.577, 0)
        _LightColor ("Light Color", Color) = (1,1,1,1)
        _EnvCube ("Environment Cubemap", Cube) = "_Skybox" {}
        _UseIBL ("Use IBL (1 = env sample)", Float) = 1
        _SpecularIBLIntensity ("Specular IBL Intensity", Float) = 1.0
        _DiffuseIBLIntensity ("Diffuse IBL Intensity", Float) = 1.0
    _IBLIntensity ("IBL Intensity", Float) = 1.0
        _EnvMipScale ("Env Mip Scale", Float) = 8.0
        _SpecularRoughnessFalloff ("Specular Roughness Falloff", Range(0.1,2.0)) = 0.5
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
            float _SpecularIBLIntensity;
            float _DiffuseIBLIntensity;
            float _IBLIntensity;
            float _SpecularRoughnessFalloff;
            float _Exposure;
            float _EnvMipScale;
            float _GamutClamp;
            float _UseACES;
            float4 _SSSColor;
            float _SSSStrength;
            float _SSSScale;
            float _UseFresnel;
            float _FresnelPower;
            float4 _FresnelTint;
            float _FresnelTintStart;
            float _FresnelTintEnd;
            float _FresnelTintStrength;
            float4 _FresnelChannelMask;

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
                // Simpler single-sample LOD: perceptual mapping (rough^2) -> LOD
                #ifdef UNITY_SAMPLE_TEXCUBE_LOD
                    float perceptual = roughness * roughness;
                    float lod = saturate(perceptual) * _EnvMipScale;
                    return UNITY_SAMPLE_TEXCUBE_LOD(_EnvCube, R, lod).rgb;
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

                // Apply Fresnel-based diffuse falloff (optional)
                if (_UseFresnel > 0.5) {
                    // Fresnel factor based on view angle (N·V)
                    float3 Fview = fresnel_schlick(NdotV, F0);

                    // Channel mask lets user pick which channels of the Fresnel to sample (R/G/B)
                    float3 mask = _FresnelChannelMask.rgb;
                    float maskSum = mask.r + mask.g + mask.b;
                    float fviewMasked;
                    if (maskSum <= 1e-5) {
                        // fallback to red channel if mask is zero
                        fviewMasked = Fview.r;
                    } else {
                        fviewMasked = dot(Fview, mask) / maskSum;
                    }

                    float fmodScalar = pow(saturate(fviewMasked), _FresnelPower);

                    // Apply a colored tint at grazing angles — use smoothstep to control how sharply the tint ramps in
                    float s = smoothstep(_FresnelTintStart, _FresnelTintEnd, fmodScalar);
                    float tintScalar = s * _FresnelTintStrength;
                    float3 tint = saturate(_FresnelTint.rgb) * tintScalar;

                    // Reduce diffuse per-channel where tint is present
                    diffuse *= (1.0 - tint);
                }

                // Direct lighting
                float3 radiance = _LightColor.rgb;
                float3 Lo = (diffuse + spec) * radiance * NdotL;

                // IBL/reflection
                float3 ambient = float3(0,0,0);
                if (_UseIBL > 0.5) {
                    // Get reflection vector
                    float3 R = reflect(-V, N);
                    float3 env = sampleEnv(R, rough);

                    // For specular IBL use Fresnel evaluated with view/reflection (dot V,R)
                    float3 F_ibl = fresnel_schlick(saturate(dot(V, R)), F0);

                        // Specular contribution from environment (scaled)
                        // Use a softer falloff controlled by _SpecularRoughnessFalloff so you can tune
                        // how quickly specular energy drops as roughness increases.
                        float perceptual = rough * rough;
                        // softer curve: pow(1 - perceptual, falloff) where falloff in (0.1..2)
                        float specScale = pow(saturate(1.0 - perceptual), _SpecularRoughnessFalloff);
                        // Gentle view attenuation: lerp so grazing angles don't abruptly kill reflections
                        float nv = saturate(NdotV);
                        float viewAtten = lerp(0.7, 1.0, nv);
                        float3 specIBL = env * specScale * viewAtten * pow(_SpecularIBLIntensity, 6.0);

                    // Diffuse environment: use a view-independent diffuse term so metallicness
                    // cleanly controls how much diffuse env is present (metals -> no diffuse).
                    float3 diffuseIBLTerm = (1.0 - _Metallic) * albedo / PI;
                    float3 diffIBL = env * diffuseIBLTerm * _DiffuseIBLIntensity;

                    ambient = (specIBL + diffIBL) * _IBLIntensity;
                } else {
                    ambient = float3(0.03, 0.03, 0.03) * albedo * _IBLIntensity; // fallback ambient
                }

                // --- Subsurface Scattering Approximation ---
                // Backscattering term: stronger when NdotL is small (light behind surface)
                float sss_back = 0.0;
                if (_SSSStrength > 0.001) {
                    // Use a smooth falloff controlled by SSSScale
                    float inv = 1.0 - NdotL; // 0 when lit directly, 1 when opposite
                    float power = 1.0 + _SSSScale * 10.0; // scale to control sharpness
                    sss_back = pow(saturate(inv), power) * _SSSStrength;

                    // Rim scattering (view-dependent) for thin objects
                    float sss_rim = pow(saturate(1.0 - NdotV), 2.0 + _SSSScale * 6.0) * (_SSSStrength * 0.5);

                    float3 sss_col = _SSSColor.rgb * (1.0 - _Metallic);
                    // Add backscatter and rim into ambient diffuse-like term
                    ambient += sss_col * (sss_back + sss_rim);
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

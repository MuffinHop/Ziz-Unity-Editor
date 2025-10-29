Shader "MatCapGenerator/PBR"
{
    Properties {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _Roughness ("Roughness", Range(0,1)) = 0.25
        _Emissive ("Emissive", Color) = (0,0,0,1)
        _Transparency ("Transparency", Range(0,1)) = 0.0
        _IOR ("IOR", Range(1,2)) = 1.5
        _UseACES ("Use ACES Tonemap", Float) = 0
        _NumSamples ("Num Samples", Int) = 16
        _NumBounces ("Num Bounces", Int) = 4
        _SkyColorTop ("Sky Color Top", Color) = (0.7,0.8,1.0,1)
        _SkyColorHorizon ("Sky Color Horizon", Color) = (0.35,0.4,0.5,1)
        _PointIntensity ("Point Light Intensity", Float) = 1.0
        _DirectionalIntensity ("Directional Light Intensity", Float) = 1.0
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

            static const float PI = 3.14159265359;
            static const float PHI = 1.61803398875;

            // Blue noise functions from Shadertoy wllSR4
            float gold_noise(float2 xy, float seed) {
                return frac(tan(distance(xy * PHI, xy) * seed) * xy.x);
            }

            float3 blueNoise(float2 uv, float time) {
                float3 noise = float3(0.0, 0.0, 0.0);
                for (int i = 0; i < 4; i++) {
                    float2 p = floor(uv) + float2(i % 2, i / 2);
                    noise += float3(gold_noise(p, time + float(i)), gold_noise(p + float2(1, 0), time + float(i)), gold_noise(p + float2(0, 1), time + float(i)));
                }
                return noise / 4.0;
            }

            float4 _BaseColor;
            float _Metallic;
            float _Roughness;
            float4 _Emissive;
            float _Transparency;
            float _IOR;
            float4 _SkyColorTop;
            float4 _SkyColorHorizon;
            float _UseACES;
            int _NumSamples;
            int _NumBounces;
            float4 _PointLightPos;
            float4 _PointLightColor;
            float4 _LightDir;
            float4 _LightColor;
            float _PointIntensity;
            float _DirectionalIntensity;

            // Material struct
            struct Material {
                float3 albedo;
                float metallic;
                float roughness;
                float3 emissive;
                float transparency;
                float ior;
            };

            // getMaterial function
            Material getMaterial(float3 p, inout float3 n) {
                Material mat;
                mat.albedo = _BaseColor.rgb;
                mat.metallic = _Metallic;
                mat.roughness = _Roughness;
                mat.emissive = _Emissive.rgb;
                mat.transparency = _Transparency;
                mat.ior = _IOR;
                return mat;
            }

            // Disney BRDF functions
            float F_Schlick(float f0, float f90, float u) {
                return f0 + (f90 - f0) * pow(1. - u, 5.);
            }

            float3 evalDisneyDiffuse(Material mat, float NoL, float NoV, float LoH, float roughness) {
                float FD90 = 0.5 + 2. * roughness * LoH * LoH;
                float a = F_Schlick(1., FD90, NoL);
                float b = F_Schlick(1., FD90, NoV);
                return mat.albedo * (a * b / PI);
            }

            float D_GTR(float roughness, float NoH, float k) {
                float a = roughness * roughness;
                float t = 1. + (a - 1.) * NoH * NoH;
                return (a - 1.) / (PI * log(a) * t);
            }

            float GeometryTerm(float NoL, float NoV, float roughness) {
                float a = roughness;
                float a2 = a * a;
                float lambdaV = NoV * sqrt((-NoV * a2 + NoV) * NoV + a2);
                float lambdaL = NoL * sqrt((-NoL * a2 + NoL) * NoL + a2);
                return 0.5 / (lambdaV + lambdaL + 1e-6);
            }

            float3 evalDisneySpecular(Material mat, float3 F, float NoH, float NoV, float NoL) {
                float roughness = pow(mat.roughness, 2.);
                float D = D_GTR(roughness, NoH, 2.);
                float G = GeometryTerm(NoL, NoV, pow(0.5 + mat.roughness * .5, 2.));
                float3 spec = D * F * G / (4. * NoL * NoV);
                return spec;
            }

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

            // ACES RRT+ODT approximation (Narkowicz fit)
            float3 RRTAndODTFit(float3 v) {
                float3 a = v * (v + 0.0245786) - 0.000090537;
                float3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
                return a / b;
            }

            float3 linearToGamma(float3 c) {
                return pow(saturate(c), 1.0/2.2);
            }

            float3 GetSkyGradient(float3 dir) {
                float3 colorTop = _SkyColorTop.rgb;
                float3 colorHorizon = _SkyColorHorizon.rgb;
                float fBlend = saturate(dir.y);
                return lerp(colorHorizon, colorTop, fBlend);
            }

            // Helper functions for path tracing
            float intersectSphere(float3 ro, float3 rd, float3 center, float radius) {
                float3 oc = ro - center;
                float a = dot(rd, rd);
                float b = 2.0 * dot(oc, rd);
                float c = dot(oc, oc) - radius * radius;
                float disc = b * b - 4.0 * a * c;
                if (disc < 0.0) return -1.0;
                float t = (-b - sqrt(disc)) / (2.0 * a);
                if (t > 0.0) return t;
                return -1.0;
            }

            float random(float seed, float2 uv, inout float noiseIndex) {
                // Use blue noise for better distribution
                float3 bn = blueNoise(uv * 100.0, seed + noiseIndex);
                noiseIndex += 1.0;
                return bn.x;
            }

            void basis(float3 n, out float3 t, out float3 b) {
                float3 up = abs(n.z) < 0.999 ? float3(0, 0, 1) : float3(1, 0, 0);
                t = normalize(cross(up, n));
                b = cross(n, t);
            }

            float3 randomHemisphereDirection(float3 nor, inout float seed, float2 uv, inout float noiseIndex) {
                float u = random(seed, uv, noiseIndex);
                seed += 1.0;
                float v = random(seed, uv, noiseIndex);
                seed += 1.0;
                float theta = 2.0 * PI * u;
                float phi = acos(sqrt(v)); // Cosine-weighted
                float3 dir = float3(sin(phi) * cos(theta), sin(phi) * sin(theta), cos(phi));
                float3 t, b;
                basis(nor, t, b);
                return t * dir.x + b * dir.y + nor * dir.z;
            }

            // BRDF functions
            float D_GGX(float NdotH, float roughness) {
                float a = roughness * roughness;
                float a2 = a * a;
                float denom = NdotH * NdotH * (a2 - 1.0) + 1.0;
                return a2 / (PI * denom * denom);
            }

            float G_SchlickGGX(float NdotV, float k) {
                return NdotV / (NdotV * (1.0 - k) + k);
            }

            float G_Smith(float NdotV, float NdotL, float k) {
                return G_SchlickGGX(NdotV, k) * G_SchlickGGX(NdotL, k);
            }

            float3 F_Schlick(float cosTheta, float3 F0) {
                return F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);
            }

#define LIGHTPATHLENGTH 2

struct LightPathNode {
    float3 color;
    float3 position;
    float3 normal;
};

LightPathNode lpNodes[LIGHTPATHLENGTH];

float getWeightForPath(int e, int l) {
    return float(e + l + 2);
}

float intersectShadow(float3 ro, float3 rd, float dist) {
    float t = intersectSphere(ro, rd, float3(0.0, 0.0, 0.0), 1.0);
    return t > 0.00001 && t < dist;
}

void constructLightPath(inout float seed, float2 uv, inout float noiseIndex) {
    float3 ro = _PointLightPos.xyz;
    float3 rd = randomHemisphereDirection(float3(0, 1, 0), seed, uv, noiseIndex);
    float3 color = _PointLightColor.rgb * _PointIntensity;

    for (int i = 0; i < LIGHTPATHLENGTH; i++) {
        lpNodes[i].position = lpNodes[i].color = lpNodes[i].normal = float3(0.0, 0.0, 0.0);
    }

    for (int i = 0; i < LIGHTPATHLENGTH; i++) {
        float t = intersectSphere(ro, rd, float3(0.0, 0.0, 0.0), 1.0);
        if (t > 0.0) {
            float3 pos = ro + rd * t;
            float3 nor = normalize(pos);
            ro = pos;
            color *= _BaseColor.rgb;

            lpNodes[i].position = pos;
            lpNodes[i].color = color;
            lpNodes[i].normal = nor;

            rd = randomHemisphereDirection(nor, seed, uv, noiseIndex);
        } else break;
    }
}

            float4 frag (v2f i) : SV_Target {
                float2 uv = i.uv * 2.0 - 1.0;
                float3 ro = float3(0.0, 0.0, 1.45); // Closer camera for matcap zoom
                float3 rd = normalize(float3(uv.x, uv.y, -1.0));
                float t = intersectSphere(ro, rd, float3(0.0, 0.0, 0.0), 1.0);
                if (t < 0.0) return float4(GetSkyGradient(rd), 0.0); // transparent background

                float3 col = float3(0.0, 0.0, 0.0);
                float3 F0 = lerp(float3(0.04, 0.04, 0.04), _BaseColor.rgb, _Metallic);
                float k = (_Roughness + 1.0) * (_Roughness + 1.0) / 8.0;
                float noiseIndex = 0.0;

                float seed = i.uv.x * 1000.0 + i.uv.y * 1000.0 + _Time.y * 1000.0;
                constructLightPath(seed, uv, noiseIndex);

                // Single sample evaluation
                float3 currentRo = ro;
                float3 currentRd = rd;
                float3 throughput = float3(1.0, 1.0, 1.0);
                bool inside = false;

                // Single bounce evaluation
                for (int b = 0; b < 1; b++) {
                    float t = intersectSphere(currentRo, currentRd, float3(0.0, 0.0, 0.0), 1.0);
                    if (t < 0.0) {
                        col += throughput * GetSkyGradient(currentRd);
                        break;
                    }

                    float3 pos = currentRo + currentRd * t;
                    float3 nor = normalize(pos);
                    float3 V = -currentRd;
                    Material mat = getMaterial(pos, nor);
                    col += mat.emissive * throughput;

                    // Direct light with BRDF
                    float3 L_dir = normalize(_LightDir.xyz);
                    float NdotL_dir = saturate(dot(nor, L_dir));
                    if (NdotL_dir > 0.0) {
                        float3 H_dir = normalize(V + L_dir);
                        float NdotV = saturate(dot(nor, V));
                        float NdotH_dir = saturate(dot(nor, H_dir));
                        float VdotH_dir = saturate(dot(V, H_dir));
                        float D_dir = D_GGX(NdotH_dir, _Roughness);
                        float G_dir = G_Smith(NdotV, NdotL_dir, k);
                        float3 F_dir = F_Schlick(VdotH_dir, F0);
                        float3 spec_dir = D_dir * G_dir * F_dir / (4.0 * NdotV * NdotL_dir + 1e-6);
                        float3 kd_dir = (1.0 - F_dir) * (1.0 - _Metallic) * _BaseColor.rgb / PI;
                        float3 brdf_dir = kd_dir + spec_dir;
                        float3 radiance_dir = _LightColor.rgb * _DirectionalIntensity;
                        col += throughput * brdf_dir * radiance_dir * NdotL_dir;
                    }

                    // Point light
                    float3 L = normalize(_PointLightPos.xyz - pos);
                    float NdotL = saturate(dot(nor, L));
                    if (NdotL > 0.0) {
                        float3 H = normalize(V + L);
                        float NdotV = saturate(dot(nor, V));
                        float NdotH = saturate(dot(nor, H));
                        float VdotH = saturate(dot(V, H));
                        float D = D_GGX(NdotH, _Roughness);
                        float G = G_Smith(NdotV, NdotL, k);
                        float3 F = F_Schlick(VdotH, F0);
                        float3 spec = D * G * F / (4.0 * NdotV * NdotL + 1e-6);
                        float3 kd = (1.0 - F) * (1.0 - _Metallic) * _BaseColor.rgb / PI;
                        float3 brdf = kd + spec;
                        float dist = length(_PointLightPos.xyz - pos);
                        float3 radiance = _PointLightColor.rgb / (dist * dist) * _PointIntensity;
                        col += throughput * brdf * radiance * NdotL;
                    }

                    // Bidirectional connections for diffuse
                    if (mat.transparency == 0.0) {
                        for (int i = 0; i < LIGHTPATHLENGTH; i++) {
                            if (lpNodes[i].position.x == 0.0 && lpNodes[i].position.y == 0.0 && lpNodes[i].position.z == 0.0) continue;
                            float3 lp = lpNodes[i].position - pos;
                            float3 lpn = normalize(lp);
                            float3 lc = lpNodes[i].color;
                            if (!intersectShadow(pos + nor * 0.001, lpn, length(lp))) {
                                float weight = saturate(dot(lpn, nor)) * saturate(dot(-lpn, lpNodes[i].normal)) / dot(lp, lp);
                                col += lc * throughput * weight / getWeightForPath(0, i);
                            }
                        }
                    }

                    // Transparency effect
                    if (mat.transparency > 0.0) {
                        float cosTheta = dot(V, nor);
                        bool entering = (!inside && cosTheta > 0.0) || (inside && cosTheta < 0.0);
                        float eta = entering ? (1.0 / mat.ior) : mat.ior;
                        float3 F = F_Schlick(abs(cosTheta), float3(0.04, 0.04, 0.04));
                        float rnd = random(seed, uv, noiseIndex);
                        seed += 1.0;

                        if (rnd < F.r) {
                            currentRd = reflect(currentRd, nor);
                            throughput *= F;
                        } else {
                            currentRd = refract(currentRd, nor, eta);
                            if (length(currentRd) == 0.0) {
                                currentRd = reflect(currentRd, nor);
                                throughput *= F;
                            } else {
                                throughput *= (1.0 - F);
                                inside = !inside;
                            }
                        }
                        currentRo = pos + nor * 0.001 * (entering ? -1.0 : 1.0);
                    } else {
                        break;
                    }
                }

                // Tonemap
                if (_UseACES > 0.5) {
                    col = RRTAndODTFit(col);
                    col = saturate(col);
                }
                return float4(linearToGamma(col), 1.0);
            }
            ENDCG
        }
    }

    FallBack Off
}

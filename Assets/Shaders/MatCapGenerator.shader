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
        _SkyColorTop ("Sky Color Top", Color) = (0.7,0.8,1.0,1)
        _SkyColorHorizon ("Sky Color Horizon", Color) = (0.35,0.4,0.5,1)
        _LightDir ("Directional Light Direction", Vector) = (0.577,0.577,0.577,0)
        _LightColor ("Directional Light Color", Color) = (1,1,1,1)
        _DirectionalIntensity ("Directional Light Intensity", Float) = 1.0
        _PointLightPos ("Point Light Position", Vector) = (0,0,0,0)
        _PointLightColor ("Point Light Color", Color) = (0,0,0,1)
        _PointIntensity ("Point Light Intensity", Float) = 1.0
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

            //-----------------------------------------------------
            // Constants and Variables
            //-----------------------------------------------------
            
            static const float PI = 3.14159265359;
            static const float PHI = 1.61803398875;
            static const float eps = 0.00001;
            
            #define LIGHTPATHLENGTH 2
            #define EYEPATHLENGTH 3

            float4 _BaseColor;
            float _Metallic;
            float _Roughness;
            float4 _Emissive;
            float _Transparency;
            float _IOR;
            float4 _SkyColorTop;
            float4 _SkyColorHorizon;
            float _UseACES;
            float4 _PointLightPos;
            float4 _PointLightColor;
            float4 _LightDir;
            float4 _LightColor;
            float _PointIntensity;
            float _DirectionalIntensity;

            // keep these hardcoded for now
            #define _NumSamples 64
            #define _NumBounces 2

            //-----------------------------------------------------
            // Noise and Random Functions
            //-----------------------------------------------------
            
            float hash1(inout float seed) {
                return frac(sin(seed += 0.1)*43758.5453123);
            }

            float2 hash2(inout float seed) {
                return frac(sin(float2(seed+=0.1,seed+=0.1))*float2(43758.5453123,22578.1459123));
            }

            float3 hash3(inout float seed) {
                return frac(sin(float3(seed+=0.1,seed+=0.1,seed+=0.1))*float3(43758.5453123,22578.1459123,19642.3490423));
            }

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

            //-----------------------------------------------------
            // Material System
            //-----------------------------------------------------
            
            struct Material {
                float3 albedo;
                float metallic;
                float roughness;
                float3 emissive;
                float transparency;
                float ior;
            };

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

            //-----------------------------------------------------
            // Sampling Functions
            //-----------------------------------------------------

            float3 cosWeightedRandomHemisphereDirection(float3 n, inout float seed) {
                float2 r = hash2(seed);
                
                float3 uu = normalize(cross(n, float3(0.0,1.0,1.0)));
                float3 vv = cross(uu, n);
                
                float ra = sqrt(r.y);
                float rx = ra*cos(6.2831*r.x); 
                float ry = ra*sin(6.2831*r.x);
                float rz = sqrt(1.0-r.y);
                float3 rr = float3(rx*uu + ry*vv + rz*n);
                
                return normalize(rr);
            }

            float3 randomSphereDirection(inout float seed) {
                float2 h = hash2(seed) * float2(2.,6.28318530718) - float2(1,0);
                float phi = h.y;
                return float3(sqrt(1.-h.x*h.x)*float2(sin(phi),cos(phi)),h.x);
            }

            float3 sampleLight(float3 ro, inout float seed) {
                float3 n = randomSphereDirection(seed) * 0.5; // Light sphere radius
                return _PointLightPos.xyz + n;
            }

            //-----------------------------------------------------
            // Disney BRDF Functions
            //-----------------------------------------------------
            float F_Schlick(float f0, float f90, float u) {
                return f0 + (f90 - f0) * pow(1. - u, 5.);
            }

            float3 F_Schlick(float cosTheta, float3 F0) {
                return F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);
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

            float D_GGX(float NdotH, float roughness) {
                float a = roughness * roughness;
                float a2 = a * a;
                float denom = NdotH * NdotH * (a2 - 1.0) + 1.0;
                return a2 / (PI * denom * denom);
            }

            float GeometryTerm(float NoL, float NoV, float roughness) {
                float a = roughness;
                float a2 = a * a;
                float lambdaV = NoV * sqrt((-NoV * a2 + NoV) * NoV + a2);
                float lambdaL = NoL * sqrt((-NoL * a2 + NoL) * NoL + a2);
                return 0.5 / (lambdaV + lambdaL + 1e-6);
            }

            float G_SchlickGGX(float NdotV, float k) {
                return NdotV / (NdotV * (1.0 - k) + k);
            }

            float G_Smith(float NdotV, float NdotL, float k) {
                return G_SchlickGGX(NdotV, k) * G_SchlickGGX(NdotL, k);
            }

            float3 evalDisneySpecular(Material mat, float3 F, float NoH, float NoV, float NoL) {
                float roughness = pow(mat.roughness, 2.);
                float D = D_GTR(roughness, NoH, 2.);
                float G = GeometryTerm(NoL, NoV, pow(0.5 + mat.roughness * .5, 2.));
                float3 spec = D * F * G / (4. * NoL * NoV);
                return spec;
            }

            //-----------------------------------------------------
            // Vertex Shader and Utilities
            //-----------------------------------------------------

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

            //-----------------------------------------------------
            // Intersection and Scene Functions
            //-----------------------------------------------------
            
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

            bool intersectShadow(float3 ro, float3 rd, float dist) {
                float t = intersectSphere(ro, rd, float3(0.0, 0.0, 0.0), 1.0);
                return t > eps && t < dist;
            }

            //-----------------------------------------------------
            // Path Tracing System
            //-----------------------------------------------------

            // Enhanced BRDF ray generation with proper transparency handling
            float3 getBRDFRay(float3 n, float3 rd, Material mat, inout bool specularBounce, inout float seed) {
                specularBounce = false;
                
                // For transparent materials, handle in the main path tracing loop
                if (mat.transparency > 0.0) {
                    specularBounce = true;
                    return rd; // Will be handled in traceEyePath
                }
                
                float3 r = cosWeightedRandomHemisphereDirection(n, seed);
                
                // Check if material is specular (metallic or very smooth)
                if (mat.metallic > 0.5 || mat.roughness < 0.1) {
                    specularBounce = true;
                    
                    float ndotr = dot(rd, n);
                    
                    // Simple reflection for metallic materials
                    if (mat.metallic > 0.8) {
                        return reflect(rd, n);
                    }
                    
                    // Fresnel reflection for dielectric materials
                    float r0 = (1.0 - mat.ior) / (1.0 + mat.ior);
                    r0 *= r0;
                    float fresnel = r0 + (1.0 - r0) * pow(1.0 - abs(ndotr), 5.0);
                    
                    if (hash1(seed) < fresnel) {
                        return reflect(rd, n);
                    } else {
                        // Mix with diffuse for rough dielectrics
                        return normalize(lerp(r, reflect(rd, n), 1.0 - mat.roughness));
                    }
                } else {
                    // Diffuse material
                    return r;
                }
            }

            struct LightPathNode {
                float3 color;
                float3 position;
                float3 normal;
            };

            LightPathNode lpNodes[LIGHTPATHLENGTH];

            float getWeightForPath(int e, int l) {
                return float(e + l + 2);
            }

            void constructLightPath(inout float seed) {
                float3 ro = randomSphereDirection(seed);
                float3 rd = cosWeightedRandomHemisphereDirection(ro, seed);
                ro = _PointLightPos.xyz - ro * 0.5; // Light sphere radius
                float3 color = _PointLightColor.rgb * _PointIntensity;

                for (int i = 0; i < LIGHTPATHLENGTH; i++) {
                    lpNodes[i].position = lpNodes[i].color = lpNodes[i].normal = float3(0.0, 0.0, 0.0);
                }

                bool specularBounce;
                
                for (int i = 0; i < LIGHTPATHLENGTH; i++) {
                    float t = intersectSphere(ro, rd, float3(0.0, 0.0, 0.0), 1.0);
                    if (t > 0.0) {
                        float3 pos = ro + rd * t;
                        float3 nor = normalize(pos);
                        ro = pos;
                        color *= _BaseColor.rgb;

                        lpNodes[i].position = pos;
                        Material mat = getMaterial(pos, nor);
                        if (mat.metallic < 0.5) lpNodes[i].color = color;
                        lpNodes[i].normal = nor;

                        rd = getBRDFRay(nor, rd, mat, specularBounce, seed);
                    } else break;
                }
            }

            // Enhanced eye path tracing with transparency support
            float3 traceEyePath(float3 ro, float3 rd, bool bidirectTrace, inout float seed, float2 uv) {
                float3 tcol = float3(0.0, 0.0, 0.0);
                float3 fcol = float3(1.0, 1.0, 1.0);
                
                bool specularBounce = false; // Start with diffuse for proper light sampling
                int jdiff = 0;
                bool inside = false; // Track if we're inside a transparent object
                
                for (int j = 0; j < EYEPATHLENGTH; ++j) {
                    float t = intersectSphere(ro, rd, float3(0.0, 0.0, 0.0), 1.0);
                    if (t < 0.0) {
                        return tcol + fcol * GetSkyGradient(rd);
                    }
                    
                    float3 pos = ro + t * rd;
                    float3 normal = normalize(pos);
                    Material mat = getMaterial(pos, normal);
                    
                    // Add emissive contribution
                    tcol += fcol * mat.emissive;
                    
                    // Add direct lighting contribution for non-transparent materials
                    if (mat.transparency <= 0.0) {
                        // Sample directional light with proper BRDF
                        if (_DirectionalIntensity > 0.0) {
                            float3 dirLightDir = normalize(-_LightDir.xyz);
                            if (!intersectShadow(pos, dirLightDir, 1000.0)) {
                                float NdotL = saturate(dot(dirLightDir, normal));
                                
                                if (NdotL > 0.0) {
                                    float3 viewDir = normalize(-rd);
                                    float3 halfDir = normalize(dirLightDir + viewDir);
                                    float NdotH = saturate(dot(normal, halfDir));
                                    float NdotV = saturate(dot(normal, viewDir));
                                    float HdotL = saturate(dot(halfDir, dirLightDir));
                                    
                                    // Fresnel for dielectrics
                                    float3 F0 = lerp(float3(0.04, 0.04, 0.04), mat.albedo, mat.metallic);
                                    float3 F = F_Schlick(HdotL, F0);
                                    
                                    // Diffuse contribution
                                    float3 kD = (1.0 - mat.metallic);
                                    float3 diffuse = kD * mat.albedo / PI;
                                    
                                    // Specular contribution
                                    float roughness = mat.roughness * mat.roughness;
                                    float D = D_GGX(NdotH, roughness);
                                    float G = GeometryTerm(NdotL, NdotV, roughness);
                                    float3 specular = (D * F * G) / max(4.0 * NdotL * NdotV, 0.001);
                                    
                                    // Apply soft falloff to final lighting
                                    float lightFalloff = pow(NdotL, 1.7); // Softer than linear
                                    float3 dirLightContrib = (diffuse + specular) * lightFalloff * _LightColor.rgb * _DirectionalIntensity;
                                    tcol += fcol * dirLightContrib;
                                }
                            }
                        }
                        
                        // Sample point light with proper BRDF and attenuation
                        if (_PointIntensity > 0.0 && length(_PointLightPos.xyz) > 0.1) {
                            float3 ld = _PointLightPos.xyz - pos;
                            float3 nld = normalize(ld);
                            float dist = length(ld);
                            
                            if (!intersectShadow(pos, nld, dist)) {
                                float NdotL = saturate(dot(nld, normal));
                                
                                if (NdotL > 0.0) {
                                    float3 viewDir = normalize(-rd);
                                    float3 halfDir = normalize(nld + viewDir);
                                    float NdotH = saturate(dot(normal, halfDir));
                                    float NdotV = saturate(dot(normal, viewDir));
                                    float HdotL = saturate(dot(halfDir, nld));
                                    
                                    // Fresnel for dielectrics
                                    float3 F0 = lerp(float3(0.04, 0.04, 0.04), mat.albedo, mat.metallic);
                                    float3 F = F_Schlick(HdotL, F0);
                                    
                                    // Diffuse contribution
                                    float3 kD = (1.0 - mat.metallic);
                                    float3 diffuse = kD * mat.albedo / PI;
                                    
                                    // Specular contribution
                                    float roughness = mat.roughness * mat.roughness;
                                    float D = D_GGX(NdotH, roughness);
                                    float G = GeometryTerm(NdotL, NdotV, roughness);
                                    float3 specular = (D * F * G) / max(4.0 * NdotL * NdotV, 0.001);
                                    
                                    // Apply soft falloff to final lighting
                                    float lightFalloff = pow(NdotL, 1.7); // Softer than linear
                                    
                                    // Distance attenuation
                                    float attenuation = 1.0 / (1.0 + 0.1 * dist + 0.01 * dist * dist);
                                    float3 pointLightContrib = (diffuse + specular) * lightFalloff * _PointLightColor.rgb * _PointIntensity * attenuation;
                                    tcol += fcol * pointLightContrib;
                                }
                            }
                        }
                    }
                    
                    // Handle transparency/refraction (simplified fake approximation)
                    if (mat.transparency > 0.0) {
                        float ndotr = dot(rd, normal);
                        
                        // Simple Fresnel for transparency effect
                        float fresnel = pow(1.0 - abs(ndotr), 2.0) * 0.5 + 0.5;
                        
                        // Mix between reflection and transmission based on Fresnel
                        if (hash1(seed) < fresnel * 0.3) {
                            // Reflection path (rare for transparent materials)
                            rd = reflect(rd, normal);
                            specularBounce = true;
                        } else {
                            // Transmission path - perturb ray slightly for fake subsurface
                            float3 perturbDir = normalize(normal + randomSphereDirection(seed) * 0.2);
                            rd = normalize(rd + perturbDir * (1.0 - mat.transparency) * 0.15);
                            specularBounce = false;
                            // Attenuate color as it passes through transparent material
                            fcol *= lerp(float3(1.0, 1.0, 1.0), mat.albedo, mat.transparency * 0.3);
                        }
                        
                        ro = pos + normal * eps * sign(ndotr);
                        continue; // Skip the rest of the loop for transparent materials
                    }
                    
                    // Direct light sampling (bidirectional technique) - DISABLED FOR DEBUGGING
                    /*
                    if (bidirectTrace) {
                        // Sample point light if it's enabled (intensity > 0)
                        if (_PointIntensity > 0.0) {
                            float3 ld = sampleLight(pos, seed) - pos;
                            float3 nld = normalize(ld);
                            
                            if (!intersectShadow(pos, nld, length(ld))) {
                                float lightRadius = 0.5;
                                float cos_a_max = sqrt(1.0 - clamp(lightRadius * lightRadius / dot(_PointLightPos.xyz - pos, _PointLightPos.xyz - pos), 0.0, 1.0));
                                float weight = 2.0 * (1.0 - cos_a_max);
                                
                                float NdotL = saturate(dot(nld, normal));
                                float3 lightContrib = fcol * _PointLightColor.rgb * _PointIntensity * weight * NdotL;
                                
                                // Apply material response
                                if (mat.metallic > 0.5) {
                                    // Metallic materials use base color as specular color
                                    lightContrib *= mat.albedo;
                                } else {
                                    // Dielectric materials use base color as diffuse albedo
                                    lightContrib *= mat.albedo;
                                }
                                
                                tcol += lightContrib / getWeightForPath(jdiff, -1);
                            }
                        }
                        
                        // Sample directional light if it's enabled (intensity > 0)
                        if (_DirectionalIntensity > 0.0) {
                            float3 dirLightDir = normalize(-_LightDir.xyz);
                            if (!intersectShadow(pos, dirLightDir, 1000.0)) {
                                float NdotL = saturate(dot(dirLightDir, normal));
                                if (NdotL > 0.0) {
                                    float3 dirLightContrib = fcol * _LightColor.rgb * _DirectionalIntensity * NdotL;
                                    
                                    // Apply material response
                                    if (mat.metallic > 0.5) {
                                        // Metallic materials use base color as specular color
                                        dirLightContrib *= mat.albedo;
                                    } else {
                                        // Dielectric materials use base color as diffuse albedo
                                        dirLightContrib *= mat.albedo;
                                    }
                                    
                                    tcol += dirLightContrib / getWeightForPath(jdiff, -1);
                                }
                            }
                        }
                        
                        // Connect to light path nodes
                        if (mat.metallic < 0.5) {
                            for (int i = 0; i < LIGHTPATHLENGTH; ++i) {
                                float3 lp = lpNodes[i].position - pos;
                                float3 lpn = normalize(lp);
                                float3 lc = lpNodes[i].color;
                                
                                if (length(lpNodes[i].position) > 0.0 && !intersectShadow(pos, lpn, length(lp))) {
                                    float weight = saturate(dot(lpn, normal)) 
                                                 * saturate(dot(-lpn, lpNodes[i].normal)) 
                                                 * clamp(1.0 / dot(lp, lp), 0.0, 1.0);
                                    
                                    tcol += lc * fcol * weight / getWeightForPath(jdiff, i);
                                }
                            }
                        }
                    }
                    */
                    
                    // BRDF evaluation and next ray generation for opaque materials
                    rd = getBRDFRay(normal, rd, mat, specularBounce, seed);
                    
                    // Apply material color based on PBR metallic workflow
                    if (specularBounce) {
                        // For specular/metallic materials, color is applied to specular reflection
                        if (mat.metallic > 0.5) {
                            fcol *= mat.albedo; // Metallic materials use base color as specular color
                        }
                        // Dielectric specular reflections don't tint the color
                    } else {
                        // For diffuse bounces, always apply albedo
                        fcol *= mat.albedo;
                    }
                    
                    // Russian roulette termination
                    float maxComponent = max(fcol.r, max(fcol.g, fcol.b));
                    if (j > 2 && hash1(seed) > maxComponent) {
                        break;
                    }
                    if (maxComponent > 0.0) {
                        fcol /= maxComponent;
                    }
                    
                    ro = pos + normal * eps;
                    
                    if (!specularBounce) jdiff++; 
                    else jdiff = 0;
                }  
                
                return tcol;
            }

            float4 frag (v2f i) : SV_Target {
                float2 uv = i.uv * 2.0 - 1.0;
                float3 ro = float3(0.0, 0.0, 1.45); // Camera position for matcap
                float3 rd = normalize(float3(uv.x, uv.y, -1.0));
                
                float t = intersectSphere(ro, rd, float3(0.0, 0.0, 0.0), 1.0);
                if (t < 0.0) return float4(GetSkyGradient(rd), 0.0);

                float3 col = float3(0.0, 0.0, 0.0);
                
                // Multi-sample bidirectional path tracing
                for (int sample = 0; sample < _NumSamples; sample++) {
                    float seed = i.uv.x + i.uv.y * 3.43121412313 + _Time.y + float(sample) * 0.1;
                    
                    // Anti-aliasing jitter
                    float2 jitter = hash2(seed) * 2.0 - 1.0;
                    float3 jitteredRd = normalize(float3(uv.x + jitter.x * 0.001, uv.y + jitter.y * 0.001, -1.0));
                    
                    // Construct light paths for bidirectional tracing
                    constructLightPath(seed);
                    
                    // Trace eye path with bidirectional connections
                    col += traceEyePath(ro, jitteredRd, true, seed, uv);
                }
                
                col /= float(_NumSamples);

                // Tonemap and gamma correct
                if (_UseACES > 0.5) {
                    col = RRTAndODTFit(col);
                }
                
                return float4(linearToGamma(saturate(col)), 1.0);
            }
            ENDCG
        }
    }

    FallBack Off
}

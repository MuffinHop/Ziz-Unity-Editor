Shader "Custom/RaymarchScene" {
    Properties {
        _PointLightPos ("Point Light Position (x,y,z)", Vector) = (0,1,0,0)
        _PointLightColor ("Point Light Color (rgb)", Color) = (1,0.8,0.6,1)
        _DirectionalLightDir ("Directional Light Direction (x,y,z)", Vector) = (0,-1,0,0)
        _DirectionalLightColor ("Directional Light Color (rgb)", Color) = (0.8,0.7,0.6,1)
        _Albedo ("Albedo", Color) = (0.2,0.3,0.7,1)
        _FirstRef ("First Reflection", Range(0,1)) = 0.1
        _Smoothness ("Smoothness", Range(0,1)) = 0.9
        _Transparency ("Transparency", Range(0,1)) = 0.7
        _IOR ("Index of Refraction", Range(1,2)) = 1.333
        _ExtinctionScale ("Extinction Scale", Range(0,10)) = 2.0
        _FogDensity ("Fog Density", Range(0,1)) = 0.01
        _PointLightIntensity ("Point Light Intensity", Range(0,100)) = 10.0
        _DirectionalLightIntensity ("Directional Light Intensity", Range(0,100)) = 8.0
    }
    SubShader {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }
        Cull Off ZWrite Off ZTest Always
        Pass {
            ZWrite Off
            Cull Off
             CGPROGRAM
             #pragma vertex vert
             #pragma fragment frag
             #include "UnityCG.cginc"
 
             struct v2f {
                 float4 pos : SV_POSITION;
                 float2 uv : TEXCOORD0;
             };
 
             v2f vert(appdata_base v) {
                 v2f o;
                 o.pos = UnityObjectToClipPos(v.vertex);
                 o.uv = v.texcoord;
                 return o;
             }
 
             float4 _PointLightPos;
             float4 _PointLightColor;
             float4 _DirectionalLightDir;
             float4 _DirectionalLightColor;
             float4 _Albedo;
             float _FirstRef;
             float _Smoothness;
             float _Transparency;
             float _IOR;
            float _ExtinctionScale;
            float _FogDensity;
            float _PointLightIntensity;
            float _DirectionalLightIntensity;
 
             #define MAX_STEPS 100
             #define MAX_DIST 100.0
             #define SURF_DIST 0.001
             #define PI 3.14159265359

            struct Ray {
                float3 origin;
                float3 direction;
            };

            struct Surface {
                float3 normal;
                float3 reflectionDir;
                float3 transmissionDir;
                float3 albedo;
                float smoothness;
                float transparency;
                float ior;
            };

            float DistSphere(float3 p, float r) {
                return length(p) - r;
            }

            float DistCombineUnionTransparent(float d1, float d2, float k) {
                return min(d1, d2);
            }

            float2 GetDistanceScene(float3 p, inout bool inWater, inout bool wasInWater) {
                float d = DistSphere(p, 1.0);
                int mat = 1; // water
                if (d < 0.0) {
                    inWater = true;
                    wasInWater = true;
                } else {
                    inWater = false;
                }
                return float2(d, mat);
            }

            float3 GetSceneNormal(float3 p) {
                bool inWaterDummy = false;
                bool wasInWaterDummy = false;
                float2 e = float2(1e-3, 0);
                float3 n = GetDistanceScene(p, inWaterDummy, wasInWaterDummy).x - float3(
                    GetDistanceScene(p - e.xyy, inWaterDummy, wasInWaterDummy).x,
                    GetDistanceScene(p - e.yxy, inWaterDummy, wasInWaterDummy).x,
                    GetDistanceScene(p - e.yyx, inWaterDummy, wasInWaterDummy).x
                );
                return normalize(n);
            }

            Surface GetObjectMaterial(int matId, float3 p) {
                Surface s;
                if (matId == 1) { // water
                    s.albedo = _Albedo.rgb;
                    s.smoothness = _Smoothness;
                    s.transparency = _Transparency;
                    s.ior = 1.0 / _IOR;
                }
                s.normal = GetSceneNormal(p);
                s.reflectionDir = reflect(normalize(p - float3(0,0,3)), s.normal);
                s.transmissionDir = refract(normalize(p - float3(0,0,3)), s.normal, s.ior);
                return s;
            }

            float Raymarch(Ray ray, inout bool inWater, inout bool wasInWater) {
                float dO = 0.0;
                for (int i = 0; i < MAX_STEPS; i++) {
                    float3 p = ray.origin + ray.direction * dO;
                    float2 dS = GetDistanceScene(p, inWater, wasInWater);
                    dO += dS.x;
                    if (dS.x < SURF_DIST || dO > MAX_DIST) break;
                }
                return dO;
            }

            float insidemarch(Ray ray, inout bool inWater, inout bool wasInWater) {
                float dO = 0.0;
                for (int i = 0; i < 50; i++) {
                    float3 p = ray.origin + ray.direction * dO;
                    float2 dS = GetDistanceScene(p, inWater, wasInWater);
                    dO += dS.x;
                    if (!inWater || dO > MAX_DIST) break;
                }
                return dO;
            }

            float GetBlinnPhongIntensity(float3 normal, float3 lightDir, float3 viewDir, float smoothness) {
                float3 halfDir = normalize(lightDir + viewDir);
                return pow(max(dot(normal, halfDir), 0.0), smoothness * 128.0);
            }

            float Fresnel(float cosTheta, float r0) {
                return r0 + (1.0 - r0) * pow(1.0 - cosTheta, 5.0);
            }

            float GetShadow(float3 p, float3 lightDir) {
                Ray shadowRay;
                shadowRay.origin = p + lightDir * 0.01;
                shadowRay.direction = lightDir;
                bool inWaterDummy = false;
                bool wasInWaterDummy = false;
                float d = Raymarch(shadowRay, inWaterDummy, wasInWaterDummy);
                return d < MAX_DIST ? 0.0 : 1.0;
            }

            float GetAmbientOcclusion(float3 p, float3 n) {
                bool inWaterDummy = false;
                bool wasInWaterDummy = false;
                float ao = 0.0;
                float weight = 1.0;
                for (int i = 1; i <= 5; i++) {
                    float d = 0.1 * i;
                    ao += weight * (d - GetDistanceScene(p + n * d, inWaterDummy, wasInWaterDummy).x);
                    weight *= 0.5;
                }
                return 1.0 - ao;
            }

            float3 GetSkyColor(float3 dir) {
                return lerp(float3(0.5, 0.7, 1.0), float3(0.1, 0.2, 0.4), dir.y * 0.5 + 0.5);
            }

            float3 ShadeSurfacePrimary(Surface surf, float3 viewDir, float3 p);
            float3 ShadeSurfaceSecondary(Surface surf, float3 viewDir, float3 p);

            float3 GetSceneColourSecondary(Ray ray, int maxBounces) {
                float3 color = float3(0,0,0);
                Ray currRay = ray;
                float3 currAtten = float3(1,1,1);
                for (int bounce = 0; bounce < maxBounces; ++bounce) {
                    bool inWaterDummy = false;
                    bool wasInWaterDummy = false;
                    float d = Raymarch(currRay, inWaterDummy, wasInWaterDummy);
                    if (d > MAX_DIST) {
                        color += currAtten * GetSkyColor(currRay.direction);
                        break;
                    }
                    float3 p = currRay.origin + currRay.direction * d;
                    Surface surf = GetObjectMaterial(1, p);
                    float3 localColor = ShadeSurfaceSecondary(surf, currRay.direction, p);
                    color += currAtten * localColor;
                    // Choose next bounce: reflection or transmission
                    if (surf.smoothness > 0.0) {
                        currRay.origin = p + surf.normal * 0.01;
                        currRay.direction = surf.reflectionDir;
                        currAtten *= surf.smoothness;
                    } else if (surf.transparency > 0.0) {
                        currRay.origin = p - surf.normal * 0.01;
                        currRay.direction = surf.transmissionDir;
                        currAtten *= surf.transparency;
                    } else {
                        break;
                    }
                }
                return color;
            }


float3 GetTransmission(Ray ray, float3 hitPos, Surface surf, inout bool inWater, inout bool wasInWater, int maxBounces) {
    Ray transRay;
    transRay.origin = hitPos - surf.normal * 0.01;
    transRay.direction = surf.transmissionDir;
    float exitDist = insidemarch(transRay, inWater, wasInWater);
    float3 exitPos = transRay.origin + transRay.direction * exitDist;
    float3 extinction = exp(-surf.albedo * exitDist * _ExtinctionScale);
    Ray exitRay;
    exitRay.origin = exitPos;
    float3 exitNormal = GetSceneNormal(exitPos);
    float3 refr = refract(transRay.direction, -exitNormal, 1.0 / surf.ior);
    if (length(refr) < 1e-5) {
        exitRay.direction = reflect(transRay.direction, exitNormal);
    } else {
        exitRay.direction = refract(refr, exitNormal, surf.ior);
        if (length(exitRay.direction) < 1e-5) {
            exitRay.direction = reflect(refr, exitNormal);
        }
    }
    float3 transColor = GetSceneColourSecondary(exitRay, maxBounces - 1) * extinction;
    return transColor;
}

             float3 ShadeSurfaceSecondary(Surface surf, float3 viewDir, float3 p) {
                 float3 ambient = GetSkyColor(surf.normal) * 0.3;
                 float3 pointLightPos = _PointLightPos.xyz;
                 float3 pointLightDir = normalize(pointLightPos - p);
                 float pointDist = length(pointLightPos - p);
                 // apply intensity multiplier
                 float3 pointLight = _PointLightColor.rgb * _PointLightIntensity * max(dot(surf.normal, pointLightDir), 0.0) / (pointDist * pointDist) * GetShadow(p, pointLightDir);
                 // Directional light from mouse direction
                 float3 mouseDir = _DirectionalLightDir;
                 float3 dirLight = _DirectionalLightColor.rgb * _DirectionalLightIntensity * max(dot(surf.normal, -mouseDir), 0.0) * GetShadow(p, -mouseDir);
                 float specular = GetBlinnPhongIntensity(surf.normal, -mouseDir, viewDir, surf.smoothness);
                 float3 diffuse = (ambient + pointLight + dirLight) * surf.albedo * (1.0 - surf.transparency);
                 return diffuse + specular * dirLight;
             }
float3 GetReflection(Ray ray, float3 hitPos, Surface surf, int maxBounces) {
    Ray reflRay;
    reflRay.origin = hitPos + surf.normal * 0.01;
    reflRay.direction = surf.reflectionDir;
    float3 reflColor = GetSceneColourSecondary(reflRay, maxBounces - 1);
    return reflColor;
}


             float3 ShadeSurfacePrimary(Surface surf, float3 viewDir, float3 p) {
                 float3 ambient = GetSkyColor(surf.normal) * 0.3;
                 float3 pointLightPos = _PointLightPos;
                 float3 pointLightDir = normalize(pointLightPos - p);
                 float pointDist = length(pointLightPos - p);
                 float3 pointLight = _PointLightColor.rgb * _PointLightIntensity * max(dot(surf.normal, pointLightDir), 0.0) / (pointDist * pointDist) * GetShadow(p, pointLightDir);
                 // Directional light from mouse direction
                 float3 mouseDir = _DirectionalLightDir;
                 float3 dirLight = _DirectionalLightColor.rgb * _DirectionalLightIntensity * max(dot(surf.normal, -mouseDir), 0.0) * GetShadow(p, -mouseDir);
                 float specular = GetBlinnPhongIntensity(surf.normal, -mouseDir, viewDir, surf.smoothness);
                 float fresnel = Fresnel(max(dot(viewDir, surf.normal), 0.0), _FirstRef);
                 float3 diffuse = (ambient + pointLight + dirLight) * surf.albedo * (1.0 - surf.transparency);
                 Ray reflRay;
                 reflRay.origin = p;
                 reflRay.direction = viewDir;
                 int recursionDepth = 2;
                 float3 refl = GetReflection(reflRay, p, surf, recursionDepth) * fresnel;
                 bool inWaterTrans = false;
                 bool wasInWaterTrans = false;
                 Ray transRay;
                 transRay.origin = p;
                 transRay.direction = viewDir;
                // transmission is weighted by (1 - fresnel). Transmission already applies extinction.
                float3 trans = GetTransmission(transRay, p, surf, inWaterTrans, wasInWaterTrans, recursionDepth) * (1.0 - fresnel);
                 return diffuse + refl + trans + specular * dirLight;
             }
 
            // Apply exponential fog/atmosphere. wasInWater allows a stronger fog inside the medium.
            float3 ApplyAtmosphere(float3 color, float dist, float3 dir, bool wasInWater) {
                // exponential fog (physically motivated): T = exp(-density * dist)
                float boost = wasInWater ? 3.0 : 1.0;
                float fogTrans = exp(-dist * _FogDensity * boost);
                float3 sky = float3(0.5, 0.7, 1.0);
                // Directional light flare
                float dirFlare = pow(max(dot(dir, -_DirectionalLightDir.xyz), 0.0), 20.0);
                sky += _DirectionalLightColor.rgb * _DirectionalLightIntensity * dirFlare * 0.02;
                color = lerp(sky, color, fogTrans);
                // Point light flare (glare)
                float3 pointLightPos = _PointLightPos.xyz;
                float3 vToLight = pointLightPos - (dir * dist);
                float fPointDot = dot(vToLight, dir);
                fPointDot = clamp(fPointDot, 0.0, dist);
                float3 vClosestPoint = dir * fPointDot;
                float fDist = length(vClosestPoint - pointLightPos);
                color += _PointLightColor.rgb * _PointLightIntensity * 0.01 / (fDist * fDist + 1e-4);
                return color;
            }
 
             float3 GetSceneColourPrimary(Ray ray) {
                 bool inWater = false;
                 bool wasInWater = false;
                 float d = Raymarch(ray, inWater, wasInWater);
                 if (d > MAX_DIST) return GetSkyColor(ray.direction);
                 float3 p = ray.origin + ray.direction * d;
                 Surface surf = GetObjectMaterial(1, p); // assuming water
                 float3 color = ShadeSurfacePrimary(surf, ray.direction, p);
                color = ApplyAtmosphere(color, d, ray.direction, wasInWater);
                 return color;
             }
 
             Ray GetCameraRayLookat(float2 uv, float3 camPos, float3 lookAt, float zoom) {
                 float3 forward = normalize(lookAt - camPos);
                 float3 right = normalize(cross(float3(0,1,0), forward));
                 float3 up = cross(forward, right);
                 float3 center = camPos + forward * zoom;
                 float3 i = center + uv.x * right + uv.y * up;
                 Ray ray;
                 ray.origin = camPos;
                 ray.direction = normalize(i - camPos);
                 return ray;
             }
 
             float3 Tonemap(float3 color) {
                 return 1.0 - exp2(-color);
             }
 
             float Falloff(float2 uv) {
                 return 1.0 - dot(uv, uv) * 0.5;
             }
 
             fixed4 frag(v2f i) : SV_Target {
                 float2 uv = (i.uv - 0.5) * 2.0;
                 uv.x *= _ScreenParams.x / _ScreenParams.y;
                 float3 camPos = float3(0,0,3);
                 float3 lookAt = float3(0,0,0);
                 Ray ray = GetCameraRayLookat(uv, camPos, lookAt, 1.0);
                 float3 color = GetSceneColourPrimary(ray);
                 color *= Falloff(uv);
                 color = Tonemap(color);
                return fixed4(color, 1.0);
             }
             ENDCG
         }
     }
     Fallback "Diffuse"
 }
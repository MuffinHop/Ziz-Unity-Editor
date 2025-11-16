Shader "Custom/RaymarchWater"
{
    Properties
    {
        _PointLightPos ("Point Light Position (x,y,z)", Vector) = (0,1,0,0)
        _PointLightColor ("Point Light Color (rgb)", Color) = (1,0.8,0.6,1)
        _PointLightIntensity ("Point Light Intensity", Range(0,100)) = 10.0
        _DirectionalLightDir ("Directional Light Direction (x,y,z)", Vector) = (0,-1,0,0)
        _DirectionalLightColor ("Directional Light Color (rgb)", Color) = (0.8,0.7,0.6,1)
        _DirectionalLightIntensity ("Directional Light Intensity", Range(0,100)) = 8.0
        _Exposure("Exposure", Float) = 1.5
        _MaterialFirstRef ("Material First Ref", Range(0,1)) = 0.1
        _MaterialSmoothness ("Material Smoothness", Range(0,1)) = 0.9
        _MaterialTransparency ("Material Transparency", Range(0,1)) = 0.2
        _MaterialReflectIndx ("Material Reflect Index", Range(0,2)) = 0.749625
        _MaterialAlbedo ("Material Albedo", Color) = (0.2,0.3,0.7,1)
        _MaterialParam ("Material Param", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            // Properties
            float4 _PointLightPos;
            float4 _PointLightColor;
            float _PointLightIntensity;
            float4 _DirectionalLightDir;
            float4 _DirectionalLightColor;
            float _DirectionalLightIntensity;
            float _Exposure;
            float _MaterialFirstRef;
            float _MaterialSmoothness;
            float _MaterialTransparency;
            float _MaterialReflectIndx;
            float3 _MaterialAlbedo;
            float2 _MaterialParam;

            // Convert GLSL-like names to HLSL-friendly
            #define iResolution _ScreenParams.xy

            // -----------------------
            // constants and macros
            // -----------------------
            #define kRaymarchMaxIter 64
            #define ENABLE_AMBIENT_OCCLUSION
            #define DOUBLE_SIDED_TRANSPARENCY
            #define ENABLE_SPECULAR
            #define ENABLE_REFLECTIONS
            #define ENABLE_TRANSPARENCY
            #define ENABLE_SHADOWS
            #define ENABLE_DIRECTIONAL_LIGHT
            #define ENABLE_DIRECTIONAL_LIGHT_FLARE
            #define ENABLE_POINT_LIGHT
            #define ENABLE_POINT_LIGHT_FLARE

            static const float kPI = 3.141592654;
            static const float kTwoPI = kPI * 2.0;
            static const float kNoTransparency = -1.0;
            static const float kTransparency = 1.0;
            static const float kInverseTransparency = 0.0;
            static const float kRaymarchEpsilon = 0.01;
            static const float kFarClip = 12.0;

            // Globals used across functions (per-pixel, initialized in frag)
            float inWater;
            float wasInWater;

            // small helpers not present in HLSL by default (safe re-implementations)
            float stepH(float edge, float x) { return x < edge ? 0.0 : 1.0; }
            float fractH(float x) { return x - floor(x); }
            float2 fractH(float2 v) { return v - floor(v); }
            float3 reflectH(float3 I, float3 N) { return reflect(I, N); }

            // Refract helper (returns zero vector if total internal reflection)
            float3 refractH(float3 I, float3 N, float eta)
            {
                float cosi = dot(-I, N);
                float k = 1.0 - eta * eta * (1.0 - cosi * cosi);
                if (k < 0.0) return float3(0.0, 0.0, 0.0);
                return eta * I + (eta * cosi - sqrt(k)) * N;
            }

            // -----------------------
            // types
            // -----------------------
            struct Ray { float3 origin; float3 dir; float startdist; float len; };
            struct HitInfo { float3 pos; float dist; float3 id; };
            struct Surface { float3 normal; float3 reflection; float3 transmission; };
            struct Material { float3 albedo; float firstref; float smoothness; float2 param; float transparency; float reflectindx; };
            struct Shading { float3 diffuse; float3 specular; };
            struct PointLight { float3 pos; float3 color; };
            struct DirectionalLight { float3 dir; float3 color; };

            // -----------------------
            // math helpers
            // -----------------------
            float2x2 rot2(float th)
            {
                float2 a = sin(float2(1.5707963, 0) + th);
                return float2x2(a.x, -a.y, a.y, a.x);
            }

            float rand2(float2 co) {
                return frac(sin(dot(co, float2(12.9898,78.233))) * 43758.5453);
            }
            float rand1(float co) { return rand2(float2(co, co)); }

            float smin(float a, float b, float k) {
                float res = exp(-k*a) + exp(-k*b);
                return -log(res) / k;
            }

            float sdSphere(float3 p, float s) { return length(p) - s; }

            float3x3 rotationMatrix(float3 axis, float angle) {
                axis = normalize(axis);
                float s = sin(angle);
                float c = cos(angle);
                float oc = 1.0 - c;
                float3x3 m;
                m[0] = float3(oc * axis.x * axis.x + c,           oc * axis.x * axis.y - axis.z * s,  oc * axis.z * axis.x + axis.y * s);
                m[1] = float3(oc * axis.x * axis.y + axis.z * s,  oc * axis.y * axis.y + c,           oc * axis.y * axis.z - axis.x * s);
                m[2] = float3(oc * axis.z * axis.x - axis.y * s,  oc * axis.y * axis.z + axis.x * s,  oc * axis.z * axis.z + c);
                return m;
            }

            // random / noise functions
            float Noise(float2 p)
            {
                float2 s = sin(p * 0.6345) + sin(p * 1.62423);
                return dot(s, float2(0.125, 0.125)) + 0.5;
            }
            float2 hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1,311.7)), dot(p, float2(269.5,183.3)));
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
            }
            float perlinnoise(float2 p)
            {
                const float K1 = 0.366025404;
                const float K2 = 0.211324865;
                float2 i = floor(p + (p.x+p.y)*K1);
                float2 a = p - i + (i.x+i.y)*K2;
                float2 o = a.x > a.y ? float2(1.0, 0.0) : float2(0.0, 1.0);
                float2 b = a - o + K2;
                float2 c = a - 1.0 + 2.0*K2;
                float3 h = max(float3(0.5 - dot(a,a), 0.5 - dot(b,b), 0.5 - dot(c,c)), 0.0);
                float3 n = h*h*h*h * float3(dot(a, hash2(i + 0.0)), dot(b, hash2(i + o)), dot(c, hash2(i + 1.0)));
                return dot(n, float3(70.0,70.0,70.0));
            }

            // -----------------------
            // CSG helpers
            // -----------------------
            float4 DistCombineUnion(const in float4 v1, const in float4 v2)
            {
                // mix(v1, v2, step(v2.x, v1.x))  -> select smaller distance
                return lerp(v1, v2, stepH(v2.x, v1.x));
            }

            float4 DistCombineUnionTransparent(const in float4 v1, const in float4 v2, const in float fTransparentScale)
            {
                float4 vScaled = float4(v2.x * ((fTransparentScale * 2.0) - 1.0), v2.yzw);
                return lerp(v1, vScaled, stepH(vScaled.x, v1.x) * stepH(0.0, fTransparentScale));
            }

            float4 DistCombineIntersect(const in float4 v1, const in float4 v2)
            {
                return lerp(v2, v1, stepH(v2.x, v1.x));
            }

            float4 DistCombineSubtract(const in float4 v1, const in float4 v2)
            {
                return DistCombineIntersect(v1, float4(-v2.x, v2.yzw));
            }

            const float kMaterialIdWall = 1.0;
            const float kMaterialIdPipe = 2.0;
            const float kMaterialIdWater = 3.0;

            // -----------------------
            // Scene SDF
            // returns float4: x=distance, y=materialId, z,w = extra params
            // -----------------------
            float4 GetDistanceScene(const in float3 pos, const in float fTransparentScale)
            {
                float4 vResult = float4(10000.0, -1.0, 0.0, 0.0);
                float distfWater = 10000.0;

                // a sphere used as a "water body"
                distfWater = min(sdSphere(pos, 0.9), distfWater);

                // If inWater flag indicates previously inside, tweak sign (mimicking original)
                if (inWater == 1.0)
                    distfWater = max(-distfWater, sdSphere(pos, 11111.0));

                float4 vDistWater = float4(distfWater, kMaterialIdWater, pos.x, pos.y);
                vResult = DistCombineUnionTransparent(vResult, vDistWater, fTransparentScale);
                vResult.y = 1.0;
                return vResult;
            }

            float GetRayFirstStep(const in Ray ray) { return ray.startdist; }

            Material GetObjectMaterial(const in HitInfo hitInfo)
            {
                Material mat;
                // water-like material
                mat.firstref = _MaterialFirstRef;
                mat.smoothness = _MaterialSmoothness;
                mat.transparency = _MaterialTransparency;
                mat.reflectindx = _MaterialReflectIndx;
                mat.albedo = _MaterialAlbedo;
                mat.param = _MaterialParam;
                return mat;
            }

            float3 GetSkyGradient(const in float3 dir)
            {
                float3 colorTop = float3(0.7, 0.8, 1.0);
                float3 colorHorizon = colorTop * 0.5;
                float fBlend = saturate(dir.y);
                return lerp(colorHorizon, colorTop, fBlend);
            }

            PointLight GetPointLight()
            {
                PointLight result;
                result.pos = _PointLightPos.xyz;
                result.color = _PointLightColor.rgb * _PointLightIntensity;
                return result;
            }

            DirectionalLight GetDirectionalLight()
            {
                DirectionalLight result;
                result.dir = normalize(_DirectionalLightDir.xyz);
                result.color = _DirectionalLightColor.rgb * _DirectionalLightIntensity;
                return result;
            }

            float3 GetAmbientLight(const in float3 normal) { return GetSkyGradient(normal); }

            // forward-declare shadow helper so Metal compiler can see it before use
            float GetShadow(const float3 pos, const float3 normal, const float3 vLightDir, const float fLightDistance);

            // -----------------------
            // Subsurface scattering helpers (new)
            // -----------------------

            // Simple direct lighting estimator used by the SSS integrator (cheap)
            float3 ComputeDirectLightingAt(const in float3 pos, const in float3 normal, const in Material material)
            {
                // Ambient
                float3 col = GetAmbientLight(normal) * 0.35;

                // Point light (attenuated, with optional shadow)
                PointLight pl = GetPointLight();
                float3 toLight = pl.pos - pos;
                float distPL = length(toLight) + 1e-6;
                float3 Lp = normalize(toLight);
                float NdotL = max(0.0, dot(normal, Lp));
                float att = 1.0 / (distPL * distPL + 1e-6);
                float sh = GetShadow(pos, normal, Lp, distPL); // keeps realism; costs some raymarching
                col += pl.color * (NdotL * att * sh) * 0.6;

                // Directional light
                DirectionalLight dl = GetDirectionalLight();
                float3 Ld = -dl.dir;
                float NdotD = max(0.0, dot(normal, Ld));
                float shD = GetShadow(pos, normal, Ld, 10.0);
                col += dl.color * (NdotD * shD) * 0.4;

                return col;
            }

            float3 GetSceneNormal(const in float3 pos, const in float fTransparentScale);
            // Single-scattering approximator along the interior refracted ray.
            // Samples N steps inside the medium, accumulating attenuated direct lighting.
            float3 SampleSubsurface(const in Ray insideRay, const in Material material, const in float travelLen)
            {
                const int kSSSSamples = 8; // moderate quality / cost
                float3 accum = float3(0.0, 0.0, 0.0);
                float weightSum = 0.0;

                // scalar extinction derived from albedo (heuristic)
                float sigma = saturate((material.albedo.x + material.albedo.y + material.albedo.z) / 3.0) * 3.0 + 0.001;
                float step = max(travelLen / (float)kSSSSamples, 0.001);

                // start slightly inside to avoid self-intersection
                float tBase = max(insideRay.startdist + 0.001, 0.0);

                for (int i = 0; i < kSSSSamples; ++i)
                {
                    float t = tBase + (i + 0.5) * step;
                    if (t > travelLen) break;

                    float3 samplePos = insideRay.origin + insideRay.dir * t;

                    // quick check that we are still inside the medium (distance < 0)
                    float d = GetDistanceScene(samplePos, kTransparency).x;
                    if (d > 0.01) // left the medium early
                        break;

                    float3 n = GetSceneNormal(samplePos, kTransparency);

                    // Evaluate direct light at this sample
                    float3 L = ComputeDirectLightingAt(samplePos, n, material);

                    // Transmittance from entry to sample (Beer-Lambert scalar approximation)
                    float tau = sigma * t;
                    float Tr = exp(-tau);

                    // single-scattering contribution (attenuated)
                    float scatter = (1.0 - Tr); // fraction scattered up to this depth
                    float3 contrib = L * scatter * Tr; // attenuate by remaining transmittance (heuristic)

                    accum += contrib;
                    weightSum += 1.0;
                }

                if (weightSum > 0.0)
                    return accum / weightSum;
                return float3(0.0, 0.0, 0.0);
            }

            float3 GetAmbientLight(const in float3 normal);

            // -----------------------
            // Raymarching: normals, marchers
            // -----------------------
            float3 GetSceneNormal(const in float3 pos, const in float fTransparentScale)
            {
                const float fDelta = 0.025;
                float3 vOffset1 = float3(fDelta, -fDelta, -fDelta);
                float3 vOffset2 = float3(-fDelta, -fDelta, fDelta);
                float3 vOffset3 = float3(-fDelta, fDelta, -fDelta);
                float3 vOffset4 = float3(fDelta, fDelta, fDelta);

                float f1 = GetDistanceScene(pos + vOffset1, fTransparentScale).x;
                float f2 = GetDistanceScene(pos + vOffset2, fTransparentScale).x;
                float f3 = GetDistanceScene(pos + vOffset3, fTransparentScale).x;
                float f4 = GetDistanceScene(pos + vOffset4, fTransparentScale).x;

                float3 normal = vOffset1 * f1 + vOffset2 * f2 + vOffset3 * f3 + vOffset4 * f4;
                return normalize(normal);
            }

            void Raymarch(const in Ray ray, out HitInfo result, const int maxIter, const float fTransparentScale)
            {
                result.dist = GetRayFirstStep(ray);
                result.id = float3(0.0, 0.0, 0.0);
                for (int i = 0; i <= kRaymarchMaxIter; i++)
                {
                    result.pos = ray.origin + ray.dir * result.dist;
                    float4 vSceneDist = GetDistanceScene(result.pos, fTransparentScale);

                    if ( (vSceneDist.x < 0.01))
                    {
                        inWater = 1.0;
                        wasInWater = inWater;
                    }
                    else
                    {
                        result.id = vSceneDist.yzw;
                        result.id.x += 0.01;
                        result.dist = result.dist + vSceneDist.x;
                    }
                }
                if (result.dist >= ray.len)
                {
                    result.dist = 1000.0;
                    result.pos = ray.origin + ray.dir * result.dist;
                    result.id = float3(0.0, 0.0, 0.0);
                }
            }

            void insidemarch(const in Ray ray, out HitInfo result, const int maxIter, const float fTransparentScale)
            {
                result.dist = GetRayFirstStep(ray);
                result.id = float3(0.0, 0.0, 0.0);
                for (int i = 0; i <= kRaymarchMaxIter/3; i++)
                {
                    result.pos = ray.origin + ray.dir * result.dist;
                    float4 vSceneDist = GetDistanceScene(result.pos, fTransparentScale);
                    result.id = vSceneDist.yzw;
                    result.dist = result.dist + vSceneDist.x;
                    if (vSceneDist.y != kMaterialIdWater) return;
                }

                if (result.dist >= ray.len)
                {
                    result.dist = 1000.0;
                    result.pos = ray.origin + ray.dir * result.dist;
                    result.id = float3(0.0, 0.0, 0.0);
                }
            }

            // soft shadow trace (original used a heuristic)
            float traceToLight(float3 rayPosition, float3 normalVector, float3 lightDir, float raylightdist)
            {
                float3 ro = rayPosition;
                float3 rd = lightDir;
                float t = 0.1;
                float k = raylightdist;
                float res = 1.0;
                for (int i = 0; i < 12; i++)
                {
                    float h = GetDistanceScene(ro + rd * t, kTransparency).x;
                    h = max(h, 0.0);
                    res = min(res, k * h / t);
                    t += clamp(h, 0.001, 0.9);
                }
                return clamp(res, 0.1, 9.0);
            }

            float GetShadow(const in float3 pos, const in float3 normal, const in float3 vLightDir, const in float fLightDistance)
            {
                return traceToLight(pos, normal, vLightDir, fLightDistance);
            }

            // ambient occlusion using DF
            float GetAmbientOcclusion(const in HitInfo intersection, const in Surface surface)
            {
                float3 pos = intersection.pos;
                float3 normal = surface.normal;
                float fAmbientOcclusion = 1.0;
                float fDist = 0.0;
                for (int i = 0; i <= 5; i++)
                {
                    fDist += 0.05;
                    float4 vSceneDist = GetDistanceScene(pos + normal * fDist, kTransparency);
                    fAmbientOcclusion *= 1.0 - max(0.0, (fDist - vSceneDist.x) * 0.1 / fDist);
                }
                return fAmbientOcclusion;
            }

            // -----------------------
            // Lighting / shading
            // -----------------------
            #define kFogDensity 0.01

            void ApplyAtmosphere(inout float3 col, const in Ray ray, const in HitInfo hitInfo)
            {
                // fog
                float fFogAmount = exp(hitInfo.dist * -kFogDensity * (1.0 + wasInWater));
                float3 cFog = GetSkyGradient(ray.dir);

                // directional flare addition (approx)
                DirectionalLight directionalLight = GetDirectionalLight();
                float fDirDot = saturate(dot(-directionalLight.dir, ray.dir));
                cFog += directionalLight.color * pow(fDirDot, 20.0);

                col = lerp(cFog, col, fFogAmount);

                // point light glare
                PointLight pointLight = GetPointLight();
                float3 vToLight = pointLight.pos - ray.origin;
                float fPointDot = dot(vToLight, ray.dir);
                fPointDot = clamp(fPointDot, 0.0, hitInfo.dist);
                float3 vClosestPoint = ray.origin + ray.dir * fPointDot;
                float fDist = length(vClosestPoint - pointLight.pos);
                col += pointLight.color * 0.01 / (fDist * fDist + 1e-6);
            }

            float Schlick(const in float3 vHalf, const in float3 vView, const in float firstref, const in float fSmoothFactor)
            {
                float fDot = dot(vHalf, -vView);
                fDot = saturate((1.0 - fDot));
                float fDotPow = pow(fDot, 5.0);
                return firstref + (1.0 - firstref) * fDotPow * fSmoothFactor;
            }

            float3 ApplyFresnel(const in float3 vDiffuse, const in float3 vSpecular, const in float3 normal, const in float3 vView, const in Material material)
            {
                float3 vReflect = reflect(-vView, normal); // reflect expects I,N where I points towards surface: use -vView as original used reflect(vView, normal) with vView = ray.dir
                float3 vHalf = normalize(vReflect + -vView);
                float fFresnel = Schlick(vHalf, vView, material.firstref, material.smoothness * 0.9 + 0.1);
                return lerp(vDiffuse, vSpecular, fFresnel.xxx);
            }

            float GetBlinnPhongIntensity(const in float3 vIncidentDir, const in float3 vLightDir, const in float3 normal, const in float smoothness)
            {
                float3 vHalf = normalize(vLightDir - vIncidentDir);
                float fNdotH = max(0.0, dot(vHalf, normal));
                float fSpecPower = exp2(4.0 + 6.0 * smoothness);
                float fSpecIntensity = (fSpecPower + 2.0) * 0.125;
                return pow(fNdotH, fSpecPower) * fSpecIntensity;
            }

            Shading ApplyPointLight(const in PointLight light, const in float3 vSurfacePos, const in float3 vIncidentDir, const in float3 normal, const in Material material)
            {
                Shading shading;
                float3 vToLight = light.pos - vSurfacePos;
                float3 vLightDir = normalize(vToLight);
                float fLightDistance = length(vToLight);
                float fAttenuation = 1.0 / (fLightDistance * fLightDistance + 1e-6);
                float fShadowFactor = GetShadow(vSurfacePos, normal, vLightDir, fLightDistance);
                float3 vIncidentLight = light.color * max(0.0, fShadowFactor * fAttenuation * dot(vLightDir, normal) / (1.0 + material.transparency));
                shading.diffuse = vIncidentLight;
                shading.specular = GetBlinnPhongIntensity(vIncidentDir, vLightDir, normal, material.smoothness) * vIncidentLight;
                return shading;
            }

            Shading ApplyDirectionalLight(const in DirectionalLight light, const in float3 vSurfacePos, const in float3 vIncidentDir, const in float3 normal, const in Material material)
            {
                Shading shading;
                const float kShadowRayLength = 10.0;
                float3 vLightDir = -light.dir;
                float fShadowFactor = GetShadow(vSurfacePos, normal, vLightDir, kShadowRayLength);
                float3 vIncidentLight = light.color * fShadowFactor * max(0.0, dot(vLightDir, normal) / (1.0 + material.transparency));
                shading.diffuse = vIncidentLight;
                shading.specular = GetBlinnPhongIntensity(vIncidentDir, vLightDir, normal, material.smoothness) * vIncidentLight;
                return shading;
            }

            float3 ShadeSurface(const in Ray ray, const in HitInfo hitInfo, const in Surface surface, const in Material material)
            {
                float3 cScene = 0;
                Shading shading;
                shading.diffuse = float3(0,0,0);
                shading.specular = float3(0,0,0);
                float fAmbientOcclusion = GetAmbientOcclusion(hitInfo, surface);
                float3 vAmbientLight = GetAmbientLight(surface.normal) * fAmbientOcclusion;
                shading.diffuse += vAmbientLight;
                shading.specular += surface.reflection;

                PointLight pointLight = GetPointLight();
                Shading pointLighting = ApplyPointLight(pointLight, hitInfo.pos, ray.dir, surface.normal, material);
                shading.diffuse += pointLighting.diffuse;
                shading.specular += pointLighting.specular;

                DirectionalLight directionalLight = GetDirectionalLight();
                Shading directionLighting = ApplyDirectionalLight(directionalLight, hitInfo.pos, ray.dir, surface.normal, material);
                shading.diffuse += directionLighting.diffuse;
                shading.specular += directionLighting.specular;

                float3 vDiffuseReflection = shading.diffuse * material.albedo * (1.0 - material.transparency);
                vDiffuseReflection = lerp(vDiffuseReflection, surface.transmission, material.transparency.xxx);

                #if 1 // ENABLE_SPECULAR
                    cScene = ApplyFresnel(vDiffuseReflection, shading.specular, surface.normal, ray.dir, material);
                #else
                    cScene = vDiffuseReflection;
                #endif

                return cScene;
            }

            // forward-declare
            float3 GetSceneColourSecondary(const in Ray ray);

            float3 GetReflection(const in Ray ray, const in HitInfo hitInfo, const in Surface surface)
            {
                // reflections are traced as secondary rays
                const float fSeparation = 0.1;
                Ray reflectRay;
                reflectRay.dir = reflect(ray.dir, surface.normal);
                reflectRay.origin = hitInfo.pos;
                reflectRay.len = 16.0;
                reflectRay.startdist = fSeparation / abs(dot(reflectRay.dir, surface.normal) + 1e-6);
                return GetSceneColourSecondary(reflectRay);
            }

            float3 GetTransmission(const in Ray ray, const in HitInfo hitInfo, const in Surface surface, const in Material material)
            {
                inWater = 0.0;

                #ifdef ENABLE_TRANSPARENCY
                {
                    const float fSeparation = 0.05;

                    // Trace until outside transparent object
                    Ray refractRay;

                    // we dont handle total internal reflection (in that case refract returns a zero length vector)
                    refractRay.dir = refractH(ray.dir, surface.normal, material.reflectindx);
                    refractRay.origin = hitInfo.pos;
                    refractRay.len = 16.0;
                    refractRay.startdist = fSeparation / abs(dot(refractRay.dir, surface.normal));

                    #ifdef DOUBLE_SIDED_TRANSPARENCY

                    HitInfo hitInfo2;
                    insidemarch(refractRay, hitInfo2, 32, kInverseTransparency);
                    float3 normal = GetSceneNormal(hitInfo2.pos, kInverseTransparency);

                    // get colour from rest of scene
                    Ray refractRay2;
                    refractRay2.dir = refractH(refractRay.dir, normal, 1.0 / material.reflectindx);
                    refractRay2.origin = hitInfo2.pos;
                    refractRay2.len = 16.0;
                    refractRay2.startdist = 0.0;

                    float fExtinctionDist = hitInfo2.dist;
                    float transparencity = material.transparency / (1.0 + distance(hitInfo2.pos, hitInfo.pos) * 4.0);
                    float nontransparencity = 1.0 - transparencity;

                    float3 vSceneColour = material.albedo * GetSceneColourSecondary(refractRay2) * nontransparencity;

                    #else

                    float3 vSceneColour = GetSceneColourSecondary(refractRay);
                    float fExtinctionDist = 0.5;

                    #endif

                    float3 cMaterialExtinction = material.albedo;
                    // extinction should really be exp(-) but this is a nice hack to get RGB
                    float3 cExtinction = (1.7 / (1.0 + (cMaterialExtinction * fExtinctionDist)));

                    return vSceneColour * cExtinction;
                }
                #else
                return GetSkyGradient(reflectH(ray.dir, surface.normal));
                #endif
            }

            // Secondary rays: no reflections/transparency (used for reflection/refraction)
            float3 GetSceneColourSecondary(const in Ray ray)
            {
                HitInfo hitInfo;
                inWater = 0.0;
                Raymarch(ray, hitInfo, 64, kNoTransparency);
                float3 cScene;

                if (hitInfo.id.x < 0.5)
                {
                    cScene = GetSkyGradient(ray.dir);
                }
                else
                {
                    Surface surface;
                    surface.normal = GetSceneNormal(hitInfo.pos, kNoTransparency);
                    Material material = GetObjectMaterial(hitInfo);
                    surface.reflection = GetSkyGradient(reflect(ray.dir, surface.normal));
                    surface.transmission = float3(0.0, 0.0, 0.0);
                    material.transparency = 0.0;
                    cScene = ShadeSurface(ray, hitInfo, surface, material);
                }

                ApplyAtmosphere(cScene, ray, hitInfo);
                return cScene;
            }

            float3 GetSceneColourPrimary(const in Ray ray)
            {
                HitInfo intersection;
                Raymarch(ray, intersection, 80, kTransparency);
                float3 cScene;

                if (intersection.id.x < 0.5)
                {
                    cScene = GetSkyGradient(ray.dir);
                }
                else
                {
                    Surface surface;
                    surface.normal = GetSceneNormal(intersection.pos, kTransparency);
                    Material material = GetObjectMaterial(intersection);
                    surface.reflection = GetReflection(ray, intersection, surface);
                    surface.transmission = float3(0.0, 0.0, 0.0);
                    if (material.transparency > 0.0)
                        surface.transmission = GetTransmission(ray, intersection, surface, material);
                    cScene = ShadeSurface(ray, intersection, surface, material) ;
                }

                ApplyAtmosphere(cScene, ray, intersection);
                return cScene;
            }

            // camera ray construction (simple pinhole)
            void GetCameraRayLookat(const in float3 pos, const in float3 vInterest, const in float2 fragCoord, out Ray ray)
            {
                float3 vForwards = normalize(vInterest - pos);
                float3 vUp = float3(0.0, 1.0, 0.0);

                float2 vUV = (fragCoord.xy / iResolution.xy);
                float2 vViewCoord = vUV * 2.0 - 1.0;
                float fRatio = iResolution.x / max(0.0001, iResolution.y);
                vViewCoord.y /= fRatio;

                ray.origin = pos;
                float3 vRight = normalize(cross(vForwards, vUp));
                float3 vRealUp = cross(vRight, vForwards);
                ray.dir = normalize(vRight * vViewCoord.x + vRealUp * vViewCoord.y + vForwards);
                ray.startdist = 0.0;
                ray.len = kFarClip;
            }

            float3 Tonemap(const in float3 cCol)
            {
                return 1.0 - exp2(-cCol);
            }

            // -----------------------
            // vertex / fragment
            // -----------------------
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

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // frag coord in pixels
                float2 fragCoord = i.uv * _ScreenParams.xy;

                // initialize per-pixel globals
                inWater = 0.0;
                wasInWater = 0.0;

                Ray ray;
                float3 lookAt = float3(0.0, 0.0, 0.0);
                float3 ro = float3(0.0, 0.0, 3.0);
                GetCameraRayLookat(ro, lookAt, fragCoord, ray);

                float3 cScene = GetSceneColourPrimary(ray);

                float exposure = _Exposure;
                float2 uv = fragCoord / _ScreenParams.xy;
                float2 coord = (uv - 0.5) * (_ScreenParams.x / _ScreenParams.y) * 2.0;
                float Falloff = 0.2;
                float rf = sqrt(dot(coord, coord)) * Falloff;
                float rf2_1 = rf * rf + 1.0;
                float e = 1.2 / (rf2_1 * rf2_1);

                float3 outc = e * Tonemap(cScene * exposure);
                return float4(saturate(outc), 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}

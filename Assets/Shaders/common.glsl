// Common shared values for shaders

struct Material {
    vec3 color;
    float metalness;
    float roughness;
    float dielectric;
    vec3 emissionColor;
    float emissionStrength;
};

// Materials for the 3 objects
Material materials[3] = Material[](
    Material(vec3(1.0, 1.0, 1.0), 0.1, 0.8, 0.9, vec3(0.0), 0.0),
    Material(vec3(1.0, 0.77, 0.9), 0.14, 0.8, 0.7, vec3(0.0), 0.0), // Cheese
    Material(vec3(0.2, 0.2, 0.3), 0.8, 0.4, 0.1, vec3(1.0, 0.6, 0.2), 2.5)  // Voronoi Cylinder is now emissive
);

const float PI = 3.14159265359;

// 3D simplex noise function for surface imperfections
vec3 mod289(vec3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
vec4 mod289(vec4 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
vec4 permute(vec4 x) { return mod289(((x*34.0)+1.0)*x); }
vec4 taylorInvSqrt(vec4 r) { return 1.79284291400159 - 0.85373472095314 * r; }
float snoise(vec3 v) {
    const vec2 C = vec2(1.0/6.0, 1.0/3.0);
    const vec4 D = vec4(0.0, 0.5, 1.0, 2.0);
    vec3 i = floor(v + dot(v, C.yyy));
    vec3 x0 = v - i + dot(i, C.xxx);
    vec3 g = step(x0.yzx, x0.xyz);
    vec3 l = 1.0 - g;
    vec3 i1 = min(g.xyz, l.zxy);
    vec3 i2 = max(g.xyz, l.zxy);
    vec3 x1 = x0 - i1 + C.xxx;
    vec3 x2 = x0 - i2 + C.yyy;
    vec3 x3 = x0 - D.yyy;
    i = mod289(i);
    vec4 p = permute(permute(permute(i.z + vec4(0.0, i1.z, i2.z, 1.0)) + i.y + vec4(0.0, i1.y, i2.y, 1.0)) + i.x + vec4(0.0, i1.x, i2.x, 1.0));
    float n_ = 0.142857142857;
    vec3 ns = n_ * D.wyz - D.xzx;
    vec4 j = p - 49.0 * floor(p * ns.z * ns.z);
    vec4 x_ = floor(j * ns.z);
    vec4 y_ = floor(j - 7.0 * x_);
    vec4 x = x_ * ns.x + ns.yyyy;
    vec4 y = y_ * ns.x + ns.yyyy;
    vec4 h = 1.0 - abs(x) - abs(y);
    vec4 b0 = vec4(x.xy, y.xy);
    vec4 b1 = vec4(x.zw, y.zw);
    vec4 s0 = floor(b0)*2.0 + 1.0;
    vec4 s1 = floor(b1)*2.0 + 1.0;
    vec4 sh = -step(h, vec4(0.0));
    vec4 a0 = b0.xzyw + s0.xzyw*sh.xxyy;
    vec4 a1 = b1.xzyw + s1.xzyw*sh.zzww;
    vec3 p0 = vec3(a0.xy,h.x);
    vec3 p1 = vec3(a0.zw,h.y);
    vec3 p2 = vec3(a1.xy,h.z);
    vec3 p3 = vec3(a1.zw,h.w);
    vec4 norm = taylorInvSqrt(vec4(dot(p0,p0), dot(p1,p1), dot(p2,p2), dot(p3,p3)));
    p0 *= norm.x;
    p1 *= norm.y;
    p2 *= norm.z;
    p3 *= norm.w;
    vec4 m = max(0.6 - vec4(dot(x0,x0), dot(x1,x1), dot(x2,x2), dot(x3,x3)), 0.0);
    m = m * m;
    return 42.0 * dot(m*m, vec4(dot(p0,x0), dot(p1,x1), dot(p2,x2), dot(p3,x3)));
}

// Generates a cosine-weighted random direction in a hemisphere oriented by N
vec3 cosineSampleHemisphere(vec2 xi, vec3 N) {
    // Create a local coordinate system around N
    vec3 up = abs(N.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0);
    vec3 tangent = normalize(cross(up, N));
    vec3 bitangent = cross(N, tangent);

    // Generate a sample on a disk
    float r = sqrt(xi.x);
    float theta = 2.0 * PI * xi.y;
    
    // Project disk sample to hemisphere
    float x = r * cos(theta);
    float y = r * sin(theta);
    float z = sqrt(max(0.0, 1.0 - xi.x));

    return x * tangent + y * bitangent + z * N;
}


float distributionGGX(vec3 N, vec3 H, float roughness) {
  float a = roughness*roughness;
  float a2 = a*a;
  float NdotH = max(dot(N, H), 0.0);
  float NdotH2 = NdotH*NdotH;
  float nom = a2;
  float denom = (NdotH2 * (a2 - 1.0) + 1.0);
  denom = PI * denom * denom;
  return nom / denom;
}

float geometrySchlickGGX(float NdotV, float roughness) {
  float r = (roughness + 1.0);
  float k = (r*r) / 8.0;
  return NdotV / (NdotV * (1.0 - k) + k);
}

float geometrySmith(vec3 N, vec3 V, vec3 L, float roughness) {
  float NdotV = max(dot(N, V), 0.0);
  float NdotL = max(dot(N, L), 0.0);
  float ggx2 = geometrySchlickGGX(NdotV, roughness);
  float ggx1 = geometrySchlickGGX(NdotL, roughness);
  return ggx1 * ggx2;
}

vec3 fresnelSchlick(float cosTheta, vec3 F0) {
  return F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);
}

// --- Voronoi Noise Functions ---
vec2 hash2( vec2 p ) {
    p = vec2( dot(p,vec2(127.1,311.7)),
              dot(p,vec2(269.5,183.3)) );
    return -1.0 + 2.0*fract(sin(p)*43758.5453123);
}

float voronoi( vec2 x ) {
    vec2 i = floor(x);
    vec2 f = fract(x);

    float min_dist = 1.0;

    for (int j = -1; j <= 1; j++) {
        for (int k = -1; k <= 1; k++) {
            vec2 neighbor = vec2(float(j), float(k));
            vec2 point = hash2(i + neighbor);
            vec2 diff = neighbor + point - f;
            float dist = length(diff);
            min_dist = min(min_dist, dist);
        }
    }
    return min_dist;
}


float luminance(vec3 c) {
  return dot(c, vec3(0.299, 0.587, 0.114));
}

// --- Background Functions --- 

float hash1(vec2 p) {
    p = fract(p * vec2(5.3983, 5.4427));
    p += dot(p.yx, p.xy + vec2(21.5351, 14.3137));
    return fract(p.x * p.y * 95.4337);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f*f*(3.0-2.0*f);
    return mix(mix(hash1(i+vec2(0,0)), hash1(i+vec2(1,0)),f.x),
               mix(hash1(i+vec2(0,1)), hash1(i+vec2(1,1)),f.x),f.y);
}


mat3 rotationtexmatrix(vec3 axis, float angle) {
    axis = normalize(axis);
    float s = sin(angle);
    float c = cos(angle);
    float oc = 1.0 - c;
    
    return mat3(oc * axis.x * axis.x + c,           oc * axis.x * axis.y - axis.z * s,  oc * axis.z * axis.x + axis.y * s,
                oc * axis.x * axis.y + axis.z * s,  oc * axis.y * axis.y + c,           oc * axis.y * axis.z - axis.x * s,
                oc * axis.z * axis.x - axis.y * s,  oc * axis.y * axis.z + axis.x * s,  oc * axis.z * axis.z + c);
}

// Returns the sun direction, optionally animated or controlled by mouse
vec3 getSunDir(vec2 iMouse, vec2 iResolution) {
    // If iMouse.x and iMouse.y are both zero, use a fixed direction
    if(iMouse.x == 0.0 && iMouse.y == 0.0) {
        return normalize(vec3(0.3, 0.5, -0.2));
    }
    return normalize(vec3(iMouse.x/iResolution.x*2.0 - 1.0, iMouse.y/iResolution.y*2.0 - 1.0, -0.2));
}

// Improved sun intensity for direct lighting (includes sharper disk and atmospheric attenuation)
float getSunIntensity(vec3 normal, vec3 sunDir) {
    float sunAngle = max(dot(normal, sunDir), 0.0);
    // Use a much softer power for the sun disk to ensure it has a visible area.
    float sunDisk = pow(sunAngle, 16.0); 
    float sunGlow = pow(sunAngle, 2.0);
    // Add a base intensity to ensure surfaces are never completely black from direct light.
    return sunDisk * 10.0 + sunGlow * 0.5 + 0.1;
}

// Improved sky color with Rayleigh/Mie scattering, horizon tint, and sun glow
vec3 getSkyColor(vec3 rayDir, vec3 sunDir) {
    // Rayleigh blue gradient (zenith to horizon)
    float rayleigh = clamp(rayDir.y * 0.7 + 0.5, 0.0, 1.0);
    vec3 zenithColor = vec3(0.12, 0.22, 0.45);
    vec3 horizonColor = vec3(0.7, 0.85, 1.0);
    vec3 sky = mix(horizonColor, zenithColor, pow(rayleigh, 1.5));

    // Mie scattering (haze near horizon)
    float mie = pow(1.0 - rayDir.y, 2.0) * 0.25;
    sky += vec3(0.95, 0.85, 0.7) * mie;

    // Sun disk and glow
    float sunAngle = max(dot(rayDir, sunDir), 0.0);
    float sunDisk = pow(sunAngle, 2048.0);
    float sunGlow = pow(sunAngle, 8.0);
    sky += vec3(1.0, 0.95, 0.8) * sunDisk * 12.0;
    sky += vec3(1.0, 0.95, 0.8) * sunGlow * 0.7;

    // Subtle horizon tint (warm color near horizon)
    float horizonTint = smoothstep(0.0, 0.25, 1.0 - rayDir.y);
    sky = mix(sky, vec3(1.0, 0.9, 0.7), horizonTint * 0.15);

    // Vignette for realism
    float vignette = smoothstep(0.9, 0.2, length(rayDir.xy));
    sky *= mix(1.0, 0.7, vignette);

    // Exposure and gamma
    sky = 1.0 - exp(-sky * 1.7);
    sky = pow(sky, vec3(1.0/2.2));
    return sky;
}

// Shared direct lighting function for PBR
vec3 calculateDirectLighting(
    vec3 color,
    float metalness,
    float roughness,
    float dielectric,
    vec3 normal,
    vec3 viewDir,
    vec3 lightDir,
    vec3 radiance
) {
    vec3 F0 = mix(vec3(dielectric), color, metalness);
    vec3 kS = fresnelSchlick(max(dot(normal, viewDir), 0.0), F0);
    vec3 kD = 1.0 - kS;
    kD *= 1.0 - metalness;
    kD *= 1.2; // Boost diffuse for clay
    vec3 H = normalize(viewDir + lightDir);
    float NDF = distributionGGX(normal, H, roughness);
    float G = geometrySmith(normal, viewDir, lightDir, roughness);
    vec3 numerator = NDF * G * kS;
    float denominator = 4.0 * max(dot(normal, viewDir), 0.0) * max(dot(normal, lightDir), 0.0);
    vec3 specular = numerator / max(denominator, 0.001);
    vec3 Lo = (kD * color / PI + specular) * radiance * max(dot(normal, lightDir), 0.0);
    return Lo;
}
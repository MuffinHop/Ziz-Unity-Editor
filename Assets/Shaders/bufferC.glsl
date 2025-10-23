// Buffer C: Denoising and Composition
vec3 sunDir;

void mainImage(out vec4 fragColor, in vec2 fragCoord) {
    // --- Read G-Buffer ---
    vec4 data = texelFetch(iChannel0, ivec2(fragCoord), 0);
    
    sunDir = getSunDir(iMouse.xy, iResolution.xy);
    // Check for sky (negative depth value)
    if(data.b < 0.0) {
        // Sky was already rendered in Buffer B, so just pass it through
        fragColor = texelFetch(iChannel1, ivec2(fragCoord), 0);
        return;
    }
    
    vec2 normalXY = data.rg * 2.0 - 1.0;
    float normalZ = sqrt(max(0.0, 1.0 - dot(normalXY, normalXY)));
    vec3 N = normalize(vec3(normalXY, normalZ));
    float depth = data.b * 10.0;
    
    // Decode material ID safely
    int matId = int(data.a * 3.0);
    matId = clamp(matId, 0, 2);

    // --- Denoise Indirect Light ---
    vec3 indirectLight = vec3(0.0);
    float totalWeight = 0.0;
    const int radius = 4;

    for (int y = -radius; y <= radius; y++) {
        for (int x = -radius; x <= radius; x++) {
            ivec2 offset = ivec2(x, y);
            ivec2 sampleCoord = ivec2(fragCoord) + offset;

            // Read neighbor G-Buffer data
            vec4 neighborData = texelFetch(iChannel0, sampleCoord, 0);
            if (neighborData.b < 0.0) continue;
            
            vec2 neighborNormalXY = neighborData.rg * 2.0 - 1.0;
            vec3 neighborN = normalize(vec3(neighborNormalXY, sqrt(max(0.0, 1.0 - dot(neighborNormalXY, neighborNormalXY)))));
            float neighborDepth = neighborData.b * 10.0;

            // Calculate weights
            float depthDiff = abs(depth - neighborDepth);
            float normalDiff = acos(clamp(dot(N, neighborN), -1.0, 1.0));
            
            // Weight based on depth and normal similarity
            float weight = exp(-depthDiff * 2.0 - normalDiff * 5.0 - float(x*x + y*y) * 0.1);

            // Accumulate
            indirectLight += texelFetch(iChannel1, sampleCoord, 0).rgb * weight;
            totalWeight += weight;
        }
    }
    if (totalWeight > 0.0) {
        indirectLight /= totalWeight;
    } else {
        indirectLight = texelFetch(iChannel1, ivec2(fragCoord), 0).rgb;
    }

    // --- Recalculate Direct Lighting (Lo) ---
    vec3 color = materials[matId].color;
    float metalness_val = materials[matId].metalness;
    float roughness_val = materials[matId].roughness;
    float dielectric_val = materials[matId].dielectric;
    vec3 emissionColor = materials[matId].emissionColor;
    float emissionStrength = materials[matId].emissionStrength;
    vec2 uv = (fragCoord - 0.5 * iResolution.xy) / iResolution.y;
    vec3 rd = normalize(vec3(uv, -1));
    vec3 viewDir = -rd;
    vec3 F0 = mix(vec3(dielectric_val), color, metalness_val);
    vec3 kS = fresnelSchlick(max(dot(N, viewDir), 0.0), F0);
    vec3 kD = 1.0 - kS;
    kD *= 1.0 - metalness_val;
    kD *= 1.2; // Boost diffuse for clay
    vec3 lightDir = sunDir;
    float sunIntensity = getSunIntensity(N, sunDir);
    vec3 radiance = vec3(sunIntensity);

    vec3 H = normalize(viewDir + lightDir);
    float NDF = distributionGGX(N, H, roughness_val);
    float G = geometrySmith(N, viewDir, lightDir, roughness_val);
    vec3 numerator = NDF * G * kS;
    float denominator = 4.0 * max(dot(N, viewDir), 0.0) * max(dot(N, lightDir), 0.0);
    vec3 specular = numerator / max(denominator, 0.001);
    vec3 Lo = calculateDirectLighting(color, metalness_val, roughness_val, dielectric_val, N, viewDir, lightDir, radiance);

    // --- Final Composition ---
    // The indirect contribution is modulated by the surface's BRDF
    vec3 finalIndirect = indirectLight * kS;
    vec3 emission = emissionColor * emissionStrength;
    vec3 finalColor = Lo + finalIndirect + emission;

    fragColor = vec4(finalColor, 1.0);
}

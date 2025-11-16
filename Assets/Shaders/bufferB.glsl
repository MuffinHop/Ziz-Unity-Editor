// Buffer B: PBR calculation
vec3 sunDir;

// Hashing function for pseudo-random numbers
vec2 hash(vec2 p) {
    p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
    return -1.0 + 2.0 * fract(sin(p) * 43758.5453123);
}

// Reconstructs world-space position from screen UV and depth (t)
vec3 reconstructPosition(vec2 uv, float t) {
    vec3 ro = vec3(0,0,5);
    vec3 rd = normalize(vec3((uv - 0.5 * iResolution.xy) / iResolution.y, -1));
    return ro + rd * t;
}
// Simple procedural sky - no atmospheric constants needed
vec3 skyColor(vec3 rayDir) {
    // Blue sky gradient from horizon to zenith
    float t = clamp(rayDir.y * 2.0 + 0.5, 0.0, 1.0);
    vec3 horizonColor = vec3(0.5, 0.7, 1.0);
    vec3 zenithColor = vec3(0.1, 0.3, 0.8);
    vec3 sky = mix(horizonColor, zenithColor, t);
    
    // Sun disk
    float sun = pow(max(dot(rayDir, sunDir), 0.0), 128.0);
    sky += vec3(1.0, 0.9, 0.7) * sun * 10.0;
    
    return sky;
}

// Generates a sample direction for a rough surface using GGX importance sampling
vec3 importanceSampleGGX(vec2 xi, vec3 N, float roughness) {
    float a = roughness * roughness;
    float phi = 2.0 * PI * xi.x;
    float cosTheta = sqrt((1.0 - xi.y) / (1.0 + (a * a - 1.0) * xi.y));
    float sinTheta = sqrt(1.0 - cosTheta * cosTheta);
    
    // Tangent space coordinates
    vec3 T = normalize(cross(N, vec3(0.0, 0.0, 1.0)));
    vec3 B = cross(N, T);
    
    // Sample direction in world space
    vec3 sampleDir = normalize(sinTheta * cos(phi) * T + 
                               sinTheta * sin(phi) * B + 
                               cosTheta * N);
    
    return sampleDir;
}

// --- SSAO Calculation ---
float calculateSSAO(vec3 p, vec3 normal, vec2 fragCoord) {
    float occlusion = 0.0;
    const int NUM_AO_SAMPLES = 128;
    const float AO_RADIUS = 0.3;

    for (int i = 0; i < NUM_AO_SAMPLES; i++) {
        // Generate sample point in hemisphere around the normal
        vec3 sampleDir = normalize(vec3(hash(fragCoord + float(i)).xy,hash(fragCoord + sin(float(i)/10.0)).x));
        if (dot(sampleDir, normal) < 0.0) {
            sampleDir = -sampleDir;
        }
        
        vec3 samplePos = p + sampleDir * AO_RADIUS;

        // Project sample point to screen space
        vec3 screen_p = samplePos - vec3(0,0,5);
        vec2 uv_proj = screen_p.xy / -screen_p.z;
        vec2 sampleUV = uv_proj * iResolution.y + 0.5 * iResolution.xy;

        // Get depth from buffer at sample location
        float buffer_t = texelFetch(iChannel0, ivec2(sampleUV), 0).b * 10.0;
        
        // If the depth buffer is closer than our sample, it's occluded
        float sample_t = length(samplePos - vec3(0,0,5));
        if (buffer_t > 0.01 && buffer_t < sample_t) {
            occlusion += 1.0;
        }
    }
    
    // Return occlusion factor (1.0 = not occluded, 0.0 = fully occluded)
    return 1.0 - pow( (occlusion / float(NUM_AO_SAMPLES)), 3.0);
}


void mainImage(out vec4 fragColor, in vec2 fragCoord) {

      sunDir = normalize(vec3(iMouse.x/iResolution.x*2.0 - 1.0, iMouse.y/iResolution.y*2.0 - 1.0, -0.2));
  vec4 data = texelFetch(iChannel0, ivec2(fragCoord), 0);
  
  // Check if we're looking at the sky (negative depth is sentinel value)
  if(data.b < 0.0) {
    vec2 uv = (fragCoord - 0.5 * iResolution.xy) / iResolution.y;
    vec3 rayDir = normalize(vec3(uv.x, uv.y, -1.0));
    vec3 color = skyColor(rayDir);
    
    // Store sky color in alpha = 1.0 to distinguish from objects
    fragColor = vec4(color, 1.0);
    return;
  }
  
  vec2 normalXY = data.rg * 2.0 - 1.0;
  float normalZ = sqrt(max(0.0, 1.0 - dot(normalXY, normalXY)));
  vec3 normal = normalize(vec3(normalXY, normalZ));
  float depth = data.b * 10.0;
  
  // Decode material ID: 0.16 -> 0, 0.5 -> 1, 0.83 -> 2
  int matId = int(data.a * 3.0);
  matId = clamp(matId, 0, 2); // Ensure valid index
  
  // Access material properties correctly
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
  vec3 kS = fresnelSchlick(max(dot(normal, viewDir), 0.0), F0);
  vec3 kD = 1.0 - kS;
  kD *= 1.0 - metalness_val;
  kD *= 1.2; // Boost diffuse for clay
  vec3 lightDir = normalize(sunDir);
  vec3 radiance = vec3(1.0);
  vec3 H = normalize(viewDir + lightDir);
  float NDF = distributionGGX(normal, H, roughness_val);
  float G = geometrySmith(normal, viewDir, lightDir, roughness_val);
  vec3 numerator = NDF * G * kS;
  float denominator = 4.0 * max(dot(normal, viewDir), 0.0) * max(dot(normal, lightDir), 0.0);
  vec3 specular = numerator / max(denominator, 0.001);
  vec3 Lo = (kD * color / PI + specular) * radiance * max(dot(normal, lightDir), 0.0);
  
  // --- Calculate SSAO ---
  float ao = calculateSSAO(reconstructPosition(fragCoord, depth), normal, fragCoord) ;

  // SSGI - Screen Space Global Illumination (Advanced)
  vec3 indirectLight = vec3(0.0);
  const int NUM_SAMPLES = 64;
  const float MAX_RAY_DIST = 1.5;
  const int MAX_STEPS = 8;
  const int BINARY_SEARCH_STEPS = 4;

  vec3 p = reconstructPosition(fragCoord, depth);

  // Apply the same procedural noise to the normal as the final shader will use
  // This makes reflections match the bumpy surface
  float noise = snoise(p * 4.0) * 0.5 + 0.5;
  normal = normalize(normal + noise * 0.1);

  for(int s = 0; s < NUM_SAMPLES; s++) {
      vec2 xi = hash(fragCoord + sin(float(s)));
      
      // Use GGX importance sampling based on roughness
      vec3 H = importanceSampleGGX(xi, normal, roughness_val);
      vec3 sampleDir = normalize(reflect(-viewDir, H));

      // Only trace rays that bounce away from the surface normal
      if(dot(sampleDir, normal) <= 0.0) continue;

      // Dither the ray start to break up banding artifacts
      float ray_dist = mix(0.01, 0.05, xi.x); 
      
      // Flag to track if we found a hit
      bool hitFound = false;

      for(int i = 0; i < MAX_STEPS; i++) {
          if(ray_dist > MAX_RAY_DIST) break;

          vec3 ray_p = p + sampleDir * ray_dist;
          
          // Project world position back to screen space
          vec3 screen_p = ray_p - vec3(0,0,5);
          vec2 uv_proj = screen_p.xy / -screen_p.z;
          vec2 rayUV = uv_proj * iResolution.y + 0.5 * iResolution.xy;

          if (rayUV.x < 0.0 || rayUV.x > iResolution.x || rayUV.y < 0.0 || rayUV.y > iResolution.y) break;

          vec4 sampleData = texelFetch(iChannel0, ivec2(rayUV), 0);
          float buffer_t = sampleData.b * 10.0;
          
          // Compare distance from camera to ray position vs distance in buffer
          float ray_t = length(ray_p - vec3(0,0,5));
          
          // Add a thickness to the depth check to avoid self-intersection and floating point issues
          float thickness = 0.1 * ray_dist / MAX_RAY_DIST;

          if (buffer_t > 0.01 && ray_t > buffer_t - thickness) {
              // Refine intersection with binary search
              float search_end = ray_dist;
              float search_start = ray_dist - (ray_dist / float(i + 1));
              for(int j=0; j < BINARY_SEARCH_STEPS; j++) {
                  float mid = (search_start + search_end) * 0.5;
                  vec3 p_search = p + sampleDir * mid;
                  vec3 screen_p_search = p_search - vec3(0,0,5);
                  vec2 uv_search = screen_p_search.xy / -screen_p_search.z;
                  vec2 searchUV = uv_search * iResolution.y + 0.5 * iResolution.xy;
                  
                  float t_search = texelFetch(iChannel0, ivec2(searchUV), 0).b * 10.0;
                  float ray_t_search = length(p_search - vec3(0,0,5));

                  if(ray_t_search > t_search - thickness) {
                      search_end = mid;
                  } else {
                      search_start = mid;
                  }
              }

              vec3 hit_p = p + sampleDir * search_end;
              vec3 screen_p_hit = hit_p - vec3(0,0,5);
              vec2 uv_hit = screen_p_hit.xy / -screen_p_hit.z;
              vec2 hitUV = uv_hit * iResolution.y + 0.5 * iResolution.xy;

              vec4 hitData = texelFetch(iChannel0, ivec2(hitUV), 0);
              
              // Check if reflection ray hit sky
              if (hitData.b < 0.0) {
                  // Use our sky function for reflections
                  indirectLight += skyColor(normalize(sampleDir)) * kS;
              } else {
                  // Hit an object
                  int hitMatId = int(hitData.a * 3.0);
                  hitMatId = clamp(hitMatId, 0, 2); // Ensure valid index
                  
                  // Correctly access hit object's color from materials array
                  vec3 hitColor = materials[hitMatId].color;
                  vec3 hitEmission = materials[hitMatId].emissionColor * materials[hitMatId].emissionStrength;
                  indirectLight += hitColor * kS + hitEmission;
              }
              hitFound = true;
              break;
          }
          
          // Check if we're going outside screen bounds
          if (rayUV.x < 0.0 || rayUV.x > iResolution.x || rayUV.y < 0.0 || rayUV.y > iResolution.y) {
              break;
          }
          
          // Adapt step size based on distance
          ray_dist += ray_dist / float(i + 1);
      }
      
      // If ray didn't hit anything, it hit the sky
      if (!hitFound) {
          indirectLight += skyColor(normalize(sampleDir)) * kS;
      }
  }
  indirectLight /= float(NUM_SAMPLES);

  // Apply AO to the indirect light and add a small ambient term
  vec3 ambient = vec3(0.05) * color;
  vec3 emission = emissionColor * emissionStrength;
  vec3 finalColor = Lo + indirectLight * ao + ambient + emission;

  fragColor = vec4(finalColor, 1.0);
}
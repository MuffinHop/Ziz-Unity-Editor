// Buffer A: Ray marcher outputting G-buffer data

// --- SDF Primitives and Operations ---
float sdSphere(vec3 p, float s) {
    return length(p) - s;
}

float sdBox(vec3 p, vec3 b) {
    vec3 q = abs(p) - b;
    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
}

float sdCylinder(vec3 p, vec2 h) {
  vec2 d = abs(vec2(length(p.xz),p.y)) - h;
  return min(max(d.x,d.y),0.0) + length(max(d,0.0));
}

float opS(float d1, float d2) { // Subtraction
    return max(-d1, d2);
}

float opU(float d1, float d2) { // Union
    return min(d1, d2);
}

float noise_3(in vec3 p);
float getGrey(vec3 p){ return dot(p, vec3(0.299, 0.587, 0.114)); }
vec3 tex3D( sampler2D tex, in vec3 p, in vec3 n ){
    n = max(n*n, 0.001);
    n /= (n.x + n.y + n.z );  
    
   
	return (texture(tex, p.yz)*n.x + texture(tex, p.zx)*n.y + texture(tex, p.xy)*n.z).xyz;
    
}
vec3 doBumpMap( sampler2D tex, in vec3 p, in vec3 nor, float bumpfactor) {
   
    const float eps = 0.001;
    vec3 grad = vec3( getGrey(tex3D(tex, vec3(p.x-eps, p.y, p.z), nor)),
                      getGrey(tex3D(tex, vec3(p.x, p.y-eps, p.z), nor)),
                      getGrey(tex3D(tex, vec3(p.x, p.y, p.z-eps), nor)));
    
    grad = (grad - getGrey(tex3D(tex,  p , nor)))/eps;
    grad -= nor*dot(nor, grad);
    return normalize( nor + grad*bumpfactor );
}
// --- Scene Definition ---

// Returns distance to a single object based on material ID
float map_single(vec3 p, vec3 dir, int matId) {
    if (matId == 0) {
        return sdSphere(p - vec3(0, -0.2, 0), 1.0);
    } else if (matId == 1) {
        vec3 cheesePos = p - vec3(-1.0, -1.0, 0.0);
        cheesePos*= rotationtexmatrix(vec3(0.0,0.0,1.0), 0.7);
        return sdBox(cheesePos, vec3(0.1, 1.6, 0.1));
    } else if (matId == 2) {
        vec3 cylPos = p - vec3(0.0, -2.0, 0.0);
        float v = 1.0-textureLod(iChannel0, p.xz/16. + p.xy/80., 0.).x * 4.0;
        float dist = sdCylinder(cylPos, vec2(3.5, 0.3)) - (1.0 - v) * 0.1;
        dist = max(dist,-sdCylinder(cylPos, vec2(2.0, 0.7)));
        return dist;
    }
    return 1e10;
}

vec2 map(vec3 p, vec3 dir) {
    // Define distances to each object
    float sphere1Dist = map_single(p, dir, 0);
    float cheeseBlockDist = map_single(p, dir, 1);
    float cylinderDist = map_single(p, dir, 2);
    
    // Explicitly check which object is closer to determine material ID
    float minDist = sphere1Dist;
    float matId = 0.0;

    if (cheeseBlockDist < minDist) {
        minDist = cheeseBlockDist;
        matId = 1.0;
    }
    if (cylinderDist < minDist) {
        minDist = cylinderDist;
        matId = 2.0;
    }
    
    return vec2(minDist, matId);
}

// --- Normal Calculation for SDFs ---
vec3 calcNormal(vec3 p, int matId) {
    const float h = 0.0001;
    const vec2 k = vec2(1, -1);
    return normalize( k.xyy * map_single(p + k.xyy*h, k.xyy, matId) +
                      k.yyx * map_single(p + k.yyx*h, k.yyx, matId) +
                      k.yxy * map_single(p + k.yxy*h, k.yxy, matId) +
                      k.xxx * map_single(p + k.xxx*h, k.xxx, matId) );
}


void mainImage(out vec4 fragColor, in vec2 fragCoord) {
  vec2 uv = (fragCoord - 0.5 * iResolution.xy) / iResolution.y;
  vec3 ro = vec3(0,0,7);
  vec3 rd = normalize(vec3(uv, -1));
  
  float t = 0.0;
  int matId = -1;

  for(int i = 0; i < 200; i++) {
    vec3 p = ro + rd * t;
    vec2 res = map(p, rd);
    float d = res.x;

    if(d < 0.001) {
      matId = int(res.y);
      break;
    }
    t += d;
    if(t > 100.0) break;
  }

  if(t < 100.0 && matId >= 0) {
    vec3 p = ro + rd * t;
    vec3 n = calcNormal(p, matId);
    // Encode material ID: 0 -> 0.25, 1 -> 0.75, 2 -> 1.25 (but will be scaled)
    float matIdEncoded = (float(matId) + 0.5) / 3.0;
    fragColor = vec4(n.xy * 0.5 + 0.5, t / 10.0, matIdEncoded);
  } else {
    // Use -1.0 as sentinel for "no hit - render sky"
    fragColor = vec4(0.0, 0.0, -1.0, -1.0);
  }
}
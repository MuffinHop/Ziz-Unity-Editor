shader_type screen;

uniform float haze_strength : hint_range(0.0, 0.05) = 0.02;
uniform float haze_distance : hint_range(0.0, 1.0) = 0.6;
uniform float haze_falloff : hint_range(0.1, 2.0) = 1.0;
uniform float haze_speed : hint_range(0.1, 5.0) = 1.0;
uniform float haze_frequency : hint_range(0.5, 10.0) = 3.0;

// Simple 2D Perlin noise function
float rand(float2 n) { return fract(sin(dot(n, float2(12.9898, 78.233))) * 43758.5453); }
float noise(float2 p) {
    float2 i = floor(p);
    float2 f = fract(p);
    float a = rand(i);
    float b = rand(i + float2(1.0, 0.0));
    float c = rand(i + float2(0.0, 1.0));
    float d = rand(i + float2(1.0, 1.0));
    float2 u = f * f * (3.0 - 2.0 * f);
    return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

void fragment() {
    float depth = textureLod(SCREEN_TEXTURE, SCREEN_UV, 0.0).a;
    float haze_mask = smoothstep(haze_distance, 1.0, depth);
    haze_mask = pow(haze_mask, haze_falloff);

    float t = TIME * haze_speed;
    float2 uv = SCREEN_UV;

    // Perlin noise based distortion, animated over time
    float2 noise_uv = uv * haze_frequency + t;
    float n = noise(noise_uv);
    float2 offset = float2(
        sin(uv.y * 40.0 + t) * n,
        cos(uv.x * 40.0 - t) * n
    ) * haze_strength * haze_mask;

    COLOR = texture(SCREEN_TEXTURE, uv + offset);
}

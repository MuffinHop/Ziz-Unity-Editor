#include "m_math.h"
#include <math.h>

// Ripped code from Unity Shader Graph functions and tried to make it as Ziz-compatibnle as possible

// Artistic

void Unity_ChannelMixer_float(float3 In, float3 _ChannelMixer_Red, float3 _ChannelMixer_Green, float3 _ChannelMixer_Blue, float3* Out)
{
    Out->x = M_DOT3(In, _ChannelMixer_Red);
    Out->y = M_DOT3(In, _ChannelMixer_Green);
    Out->z = M_DOT3(In, _ChannelMixer_Blue);
}

void Unity_Contrast_float(float3 In, float Contrast, float3* Out)
{
    float midpoint = powf(0.5f, 2.2f);
    Out->x = (In.x - midpoint) * Contrast + midpoint;
    Out->y = (In.y - midpoint) * Contrast + midpoint;
    Out->z = (In.z - midpoint) * Contrast + midpoint;
}

void Unity_Hue_Degrees_float(float3 In, float Offset, float3* Out)
{
    float4 K = {0.0f, -1.0f / 3.0f, 2.0f / 3.0f, -1.0f};
    float4 P = {In.z, K.w, In.y, K.x};
    if (In.z < In.y) {
        P.x = In.y; P.y = K.x; P.z = In.z; P.w = K.w;
    }
    float4 Q = {P.x, In.x, P.y, P.z};
    if (P.x < In.x) {
        Q.x = In.x; Q.y = P.z; Q.z = P.w; Q.w = P.x;
    }
    float D = Q.x - M_MIN(Q.w, Q.y);
    float E = 1e-10f;
    float3 hsv = {fabsf(Q.z + (Q.w - Q.y) / (6.0f * D + E)), D / (Q.x + E), Q.x};

    float hue = hsv.x + Offset / 360.0f;
    hsv.x = (hue < 0) ? hue + 1 : (hue > 1) ? hue - 1 : hue;

    float4 K2 = {1.0f, 2.0f / 3.0f, 1.0f / 3.0f, 3.0f};
    float3 P2 = {fabsf(frac(hsv.x + K2.x) * 6.0f - K2.w), fabsf(frac(hsv.x + K2.y) * 6.0f - K2.w), fabsf(frac(hsv.x + K2.z) * 6.0f - K2.w)};
    *Out = (float3){hsv.z * (1.0f - saturate(P2.x - K2.x)), hsv.z * (1.0f - saturate(P2.y - K2.x)), hsv.z * (1.0f - saturate(P2.z - K2.x))};
}

void Unity_Hue_Radians_float(float3 In, float Offset, float3* Out)
{
    // Similar to above, but Offset in radians
    float4 K = {0.0f, -1.0f / 3.0f, 2.0f / 3.0f, -1.0f};
    float4 P = {In.z, K.w, In.y, K.x};
    if (In.z < In.y) {
        P.x = In.y; P.y = K.x; P.z = In.z; P.w = K.w;
    }
    float4 Q = {P.x, In.x, P.y, P.z};
    if (P.x < In.x) {
        Q.x = In.x; Q.y = P.z; Q.z = P.w; Q.w = P.x;
    }
    float D = Q.x - M_MIN(Q.w, Q.y);
    float E = 1e-10f;
    float3 hsv = {fabsf(Q.z + (Q.w - Q.y) / (6.0f * D + E)), D / (Q.x + E), Q.x};

    float hue = hsv.x + Offset;
    hsv.x = (hue < 0) ? hue + 1 : (hue > 1) ? hue - 1 : hue;

    float4 K2 = {1.0f, 2.0f / 3.0f, 1.0f / 3.0f, 3.0f};
    float3 P2 = {fabsf(frac(hsv.x + K2.x) * 6.0f - K2.w), fabsf(frac(hsv.x + K2.y) * 6.0f - K2.w), fabsf(frac(hsv.x + K2.z) * 6.0f - K2.w)};
    *Out = (float3){hsv.z * (1.0f - saturate(P2.x - K2.x)), hsv.z * (1.0f - saturate(P2.y - K2.x)), hsv.z * (1.0f - saturate(P2.z - K2.x))};
}

void Unity_InvertColors_float4(float4 In, float4 InvertColors, float4* Out)
{
    Out->x = fabsf(InvertColors.x - In.x);
    Out->y = fabsf(InvertColors.y - In.y);
    Out->z = fabsf(InvertColors.z - In.z);
    Out->w = fabsf(InvertColors.w - In.w);
}

void Unity_ReplaceColor_float(float3 In, float3 From, float3 To, float Range, float Fuzziness, float3* Out)
{
    float3 diff = {In.x - From.x, In.y - From.y, In.z - From.z};
    float Distance = M_LENGHT3(diff);
    *Out = (float3){To.x + (In.x - To.x) * saturate((Distance - Range) / M_MAX(Fuzziness, 1e-5f)),
                    To.y + (In.y - To.y) * saturate((Distance - Range) / M_MAX(Fuzziness, 1e-5f)),
                    To.z + (In.z - To.z) * saturate((Distance - Range) / M_MAX(Fuzziness, 1e-5f))};
}

void Unity_Saturation_float(float3 In, float Saturation, float3* Out)
{
    float luma = M_DOT3(In, (float3){0.2126729f, 0.7151522f, 0.0721750f});
    float3 luma_vec = {luma, luma, luma};
    *Out = (float3){luma_vec.x + Saturation * (In.x - luma_vec.x),
                    luma_vec.y + Saturation * (In.y - luma_vec.y),
                    luma_vec.z + Saturation * (In.z - luma_vec.z)};
}

void Unity_WhiteBalance_float(float3 In, float Temperature, float Tint, float3* Out)
{
    float t1 = Temperature * 10 / 6;
    float t2 = Tint * 10 / 6;
    float x = 0.31271f - t1 * (t1 < 0 ? 0.1f : 0.05f);
    float standardIlluminantY = 2.87f * x - 3 * x * x - 0.27509507f;
    float y = standardIlluminantY + t2 * 0.05f;
    float X = y * x / y;
    float Z = y * (1 - x - y) / y;
    float L = 0.7328f * X + 0.4296f * y - 0.1624f * Z;
    float M = -0.7036f * X + 1.6975f * y + 0.0061f * Z;
    float S = 0.0030f * X + 0.0136f * y + 0.9834f * Z;
    float3 w2 = {L, M, S};
    float3 balance = {0.949237f / w2.x, 1.03542f / w2.y, 1.08728f / w2.z};
    float3 lms = {0.7328f * In.x + 0.4296f * In.y - 0.1624f * In.z,
                  -0.7036f * In.x + 1.6975f * In.y + 0.0061f * In.z,
                  0.0030f * In.x + 0.0136f * In.y + 0.9834f * In.z};
    lms.x *= balance.x;
    lms.y *= balance.y;
    lms.z *= balance.z;
    Out->x = 1.0966f * lms.x - 0.2789f * lms.y - 0.1831f * lms.z;
    Out->y = -0.3121f * lms.x + 1.1649f * lms.y + 0.0853f * lms.z;
    Out->z = 0.0134f * lms.x + 0.0426f * lms.y + 0.9305f * lms.z;
}

void Unity_Blend_Burn_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    Out->x = 1.0f - (1.0f - Blend.x) / Base.x;
    Out->y = 1.0f - (1.0f - Blend.y) / Base.y;
    Out->z = 1.0f - (1.0f - Blend.z) / Base.z;
    Out->w = 1.0f - (1.0f - Blend.w) / Base.w;
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_Darken_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    Out->x = M_MIN(Blend.x, Base.x);
    Out->y = M_MIN(Blend.y, Base.y);
    Out->z = M_MIN(Blend.z, Base.z);
    Out->w = M_MIN(Blend.w, Base.w);
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_Difference_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    Out->x = fabsf(Blend.x - Base.x);
    Out->y = fabsf(Blend.y - Base.y);
    Out->z = fabsf(Blend.z - Base.z);
    Out->w = fabsf(Blend.w - Base.w);
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_Dodge_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    Out->x = Base.x / (1.0f - Blend.x);
    Out->y = Base.y / (1.0f - Blend.y);
    Out->z = Base.z / (1.0f - Blend.z);
    Out->w = Base.w / (1.0f - Blend.w);
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_Divide_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    Out->x = Base.x / (Blend.x + 1e-9f);
    Out->y = Base.y / (Blend.y + 1e-9f);
    Out->z = Base.z / (Blend.z + 1e-9f);
    Out->w = Base.w / (Blend.w + 1e-9f);
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_Exclusion_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    Out->x = Blend.x + Base.x - (2.0f * Blend.x * Base.x);
    Out->y = Blend.y + Base.y - (2.0f * Blend.y * Base.y);
    Out->z = Blend.z + Base.z - (2.0f * Blend.z * Base.z);
    Out->w = Blend.w + Base.w - (2.0f * Blend.w * Base.w);
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_HardLight_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    float4 result1 = {1.0f - 2.0f * (1.0f - Base.x) * (1.0f - Blend.x), 1.0f - 2.0f * (1.0f - Base.y) * (1.0f - Blend.y), 1.0f - 2.0f * (1.0f - Base.z) * (1.0f - Blend.z), 1.0f - 2.0f * (1.0f - Base.w) * (1.0f - Blend.w)};
    float4 result2 = {2.0f * Base.x * Blend.x, 2.0f * Base.y * Blend.y, 2.0f * Base.z * Blend.z, 2.0f * Base.w * Blend.w};
    float4 zeroOrOne = {Blend.x > 0.5f ? 1.0f : 0.0f, Blend.y > 0.5f ? 1.0f : 0.0f, Blend.z > 0.5f ? 1.0f : 0.0f, Blend.w > 0.5f ? 1.0f : 0.0f};
    *Out = (float4){result2.x * zeroOrOne.x + (1 - zeroOrOne.x) * result1.x, result2.y * zeroOrOne.y + (1 - zeroOrOne.y) * result1.y, result2.z * zeroOrOne.z + (1 - zeroOrOne.z) * result1.z, result2.w * zeroOrOne.w + (1 - zeroOrOne.w) * result1.w};
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_HardMix_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    Out->x = Blend.x > 1 - Base.x ? 1.0f : 0.0f;
    Out->y = Blend.y > 1 - Base.y ? 1.0f : 0.0f;
    Out->z = Blend.z > 1 - Base.z ? 1.0f : 0.0f;
    Out->w = Blend.w > 1 - Base.w ? 1.0f : 0.0f;
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_Lighten_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    Out->x = M_MAX(Blend.x, Base.x);
    Out->y = M_MAX(Blend.y, Base.y);
    Out->z = M_MAX(Blend.z, Base.z);
    Out->w = M_MAX(Blend.w, Base.w);
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_LinearBurn_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    Out->x = Base.x + Blend.x - 1.0f;
    Out->y = Base.y + Blend.y - 1.0f;
    Out->z = Base.z + Blend.z - 1.0f;
    Out->w = Base.w + Blend.w - 1.0f;
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_LinearDodge_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    Out->x = Base.x + Blend.x;
    Out->y = Base.y + Blend.y;
    Out->z = Base.z + Blend.z;
    Out->w = Base.w + Blend.w;
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_LinearLight_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    Out->x = Blend.x < 0.5f ? M_MAX(Base.x + (2 * Blend.x) - 1, 0) : M_MIN(Base.x + 2 * (Blend.x - 0.5f), 1);
    Out->y = Blend.y < 0.5f ? M_MAX(Base.y + (2 * Blend.y) - 1, 0) : M_MIN(Base.y + 2 * (Blend.y - 0.5f), 1);
    Out->z = Blend.z < 0.5f ? M_MAX(Base.z + (2 * Blend.z) - 1, 0) : M_MIN(Base.z + 2 * (Blend.z - 0.5f), 1);
    Out->w = Blend.w < 0.5f ? M_MAX(Base.w + (2 * Blend.w) - 1, 0) : M_MIN(Base.w + 2 * (Blend.w - 0.5f), 1);
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_LinearLightAddSub_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    Out->x = Blend.x + 2.0f * Base.x - 1.0f;
    Out->y = Blend.y + 2.0f * Base.y - 1.0f;
    Out->z = Blend.z + 2.0f * Base.z - 1.0f;
    Out->w = Blend.w + 2.0f * Base.w - 1.0f;
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_Multiply_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    Out->x = Base.x * Blend.x;
    Out->y = Base.y * Blend.y;
    Out->z = Base.z * Blend.z;
    Out->w = Base.w * Blend.w;
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_Negation_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    Out->x = 1.0f - fabsf(1.0f - Blend.x - Base.x);
    Out->y = 1.0f - fabsf(1.0f - Blend.y - Base.y);
    Out->z = 1.0f - fabsf(1.0f - Blend.z - Base.z);
    Out->w = 1.0f - fabsf(1.0f - Blend.w - Base.w);
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_Overlay_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    float4 result1 = {1.0f - 2.0f * (1.0f - Base.x) * (1.0f - Blend.x), 1.0f - 2.0f * (1.0f - Base.y) * (1.0f - Blend.y), 1.0f - 2.0f * (1.0f - Base.z) * (1.0f - Blend.z), 1.0f - 2.0f * (1.0f - Base.w) * (1.0f - Blend.w)};
    float4 result2 = {2.0f * Base.x * Blend.x, 2.0f * Base.y * Blend.y, 2.0f * Base.z * Blend.z, 2.0f * Base.w * Blend.w};
    float4 zeroOrOne = {Base.x > 0.5f ? 1.0f : 0.0f, Base.y > 0.5f ? 1.0f : 0.0f, Base.z > 0.5f ? 1.0f : 0.0f, Base.w > 0.5f ? 1.0f : 0.0f};
    *Out = (float4){result2.x * zeroOrOne.x + (1 - zeroOrOne.x) * result1.x, result2.y * zeroOrOne.y + (1 - zeroOrOne.y) * result1.y, result2.z * zeroOrOne.z + (1 - zeroOrOne.z) * result1.z, result2.w * zeroOrOne.w + (1 - zeroOrOne.w) * result1.w};
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_PinLight_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    float4 check = {Blend.x > 0.5f ? 1.0f : 0.0f, Blend.y > 0.5f ? 1.0f : 0.0f, Blend.z > 0.5f ? 1.0f : 0.0f, Blend.w > 0.5f ? 1.0f : 0.0f};
    float4 result1 = {check.x * M_MAX(2.0f * (Base.x - 0.5f), Blend.x), check.y * M_MAX(2.0f * (Base.y - 0.5f), Blend.y), check.z * M_MAX(2.0f * (Base.z - 0.5f), Blend.z), check.w * M_MAX(2.0f * (Base.w - 0.5f), Blend.w)};
    *Out = (float4){result1.x + (1.0f - check.x) * M_MIN(2.0f * Base.x, Blend.x), result1.y + (1.0f - check.y) * M_MIN(2.0f * Base.y, Blend.y), result1.z + (1.0f - check.z) * M_MIN(2.0f * Base.z, Blend.z), result1.w + (1.0f - check.w) * M_MIN(2.0f * Base.w, Blend.w)};
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_Screen_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    Out->x = 1.0f - (1.0f - Blend.x) * (1.0f - Base.x);
    Out->y = 1.0f - (1.0f - Blend.y) * (1.0f - Base.y);
    Out->z = 1.0f - (1.0f - Blend.z) * (1.0f - Base.z);
    Out->w = 1.0f - (1.0f - Blend.w) * (1.0f - Base.w);
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_SoftLight_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    float4 result1 = {2.0f * Base.x * Blend.x + Base.x * Base.x * (1.0f - 2.0f * Blend.x), 2.0f * Base.y * Blend.y + Base.y * Base.y * (1.0f - 2.0f * Blend.y), 2.0f * Base.z * Blend.z + Base.z * Base.z * (1.0f - 2.0f * Blend.z), 2.0f * Base.w * Blend.w + Base.w * Base.w * (1.0f - 2.0f * Blend.w)};
    float4 result2 = {sqrtf(Base.x) * (2.0f * Blend.x - 1.0f) + 2.0f * Base.x * (1.0f - Blend.x), sqrtf(Base.y) * (2.0f * Blend.y - 1.0f) + 2.0f * Base.y * (1.0f - Blend.y), sqrtf(Base.z) * (2.0f * Blend.z - 1.0f) + 2.0f * Base.z * (1.0f - Blend.z), sqrtf(Base.w) * (2.0f * Blend.w - 1.0f) + 2.0f * Base.w * (1.0f - Blend.w)};
    float4 zeroOrOne = {Blend.x > 0.5f ? 1.0f : 0.0f, Blend.y > 0.5f ? 1.0f : 0.0f, Blend.z > 0.5f ? 1.0f : 0.0f, Blend.w > 0.5f ? 1.0f : 0.0f};
    *Out = (float4){result2.x * zeroOrOne.x + (1 - zeroOrOne.x) * result1.x, result2.y * zeroOrOne.y + (1 - zeroOrOne.y) * result1.y, result2.z * zeroOrOne.z + (1 - zeroOrOne.z) * result1.z, result2.w * zeroOrOne.w + (1 - zeroOrOne.w) * result1.w};
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_Subtract_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    Out->x = Base.x - Blend.x;
    Out->y = Base.y - Blend.y;
    Out->z = Base.z - Blend.z;
    Out->w = Base.w - Blend.w;
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_VividLight_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    float4 result1 = {1.0f - (1.0f - Blend.x) / (2.0f * Base.x), 1.0f - (1.0f - Blend.y) / (2.0f * Base.y), 1.0f - (1.0f - Blend.z) / (2.0f * Base.z), 1.0f - (1.0f - Blend.w) / (2.0f * Base.w)};
    float4 result2 = {Blend.x / (2.0f * (1.0f - Base.x)), Blend.y / (2.0f * (1.0f - Base.y)), Blend.z / (2.0f * (1.0f - Base.z)), Blend.w / (2.0f * (1.0f - Base.w))};
    float4 zeroOrOne = {Base.x > 0.5f ? 1.0f : 0.0f, Base.y > 0.5f ? 1.0f : 0.0f, Base.z > 0.5f ? 1.0f : 0.0f, Base.w > 0.5f ? 1.0f : 0.0f};
    *Out = (float4){result2.x * zeroOrOne.x + (1 - zeroOrOne.x) * result1.x, result2.y * zeroOrOne.y + (1 - zeroOrOne.y) * result1.y, result2.z * zeroOrOne.z + (1 - zeroOrOne.z) * result1.z, result2.w * zeroOrOne.w + (1 - zeroOrOne.w) * result1.w};
    *Out = (float4){Base.x + Opacity * (Out->x - Base.x), Base.y + Opacity * (Out->y - Base.y), Base.z + Opacity * (Out->z - Base.z), Base.w + Opacity * (Out->w - Base.w)};
}

void Unity_Blend_Overwrite_float4(float4 Base, float4 Blend, float Opacity, float4* Out)
{
    *Out = (float4){Base.x + Opacity * (Blend.x - Base.x), Base.y + Opacity * (Blend.y - Base.y), Base.z + Opacity * (Blend.z - Base.z), Base.w + Opacity * (Blend.w - Base.w)};
}

void Unity_Dither_float4(float4 In, float4 ScreenPosition, float4* Out)
{
    float2 uv = {ScreenPosition.x * 1.0f, ScreenPosition.y * 1.0f}; // Assuming _ScreenParams is 1,1
    float DITHER_THRESHOLDS[16] = {1.0f / 17.0f, 9.0f / 17.0f, 3.0f / 17.0f, 11.0f / 17.0f, 13.0f / 17.0f, 5.0f / 17.0f, 15.0f / 17.0f, 7.0f / 17.0f, 4.0f / 17.0f, 12.0f / 17.0f, 2.0f / 17.0f, 10.0f / 17.0f, 16.0f / 17.0f, 8.0f / 17.0f, 14.0f / 17.0f, 6.0f / 17.0f};
    int index = ((int)uv.x % 4) * 4 + ((int)uv.y % 4);
    *Out = (float4){In.x - DITHER_THRESHOLDS[index], In.y - DITHER_THRESHOLDS[index], In.z - DITHER_THRESHOLDS[index], In.w - DITHER_THRESHOLDS[index]};
}

void Unity_ChannelMask_RedGreen_float4(float4 In, float4* Out)
{
    *Out = (float4){0, 0, In.z, In.w};
}

void Unity_ColorMask_float(float3 In, float3 MaskColor, float Range, float Fuzziness, float4* Out)
{
    float3 diff = {In.x - MaskColor.x, In.y - MaskColor.y, In.z - MaskColor.z};
    float Distance = M_LENGHT3(diff);
    float mask = saturate(1 - (Distance - Range) / M_MAX(Fuzziness, 1e-5f));
    *Out = (float4){mask, mask, mask, mask};
}

void Unity_NormalBlend_float(float3 A, float3 B, float3* Out)
{
    *Out = (float3){A.x + B.x, A.y + B.y, A.z * B.z};
    M_NORMALIZE3(*Out, *Out);
}

void Unity_NormalFromHeight_Tangent_float(float In, float Strength, float3 Position, float3x3 TangentMatrix, float3* Out)
{
    // Simplified, assuming derivatives
    float dHdx = 0.01f; // Placeholder
    float dHdy = 0.01f;
    float3 surfGrad = {dHdx, dHdy, 1.0f};
    *Out = (float3){TangentMatrix[2].x - Strength * surfGrad.x, TangentMatrix[2].y - Strength * surfGrad.y, TangentMatrix[2].z - Strength * surfGrad.z};
    M_NORMALIZE3(*Out, *Out);
}

void Unity_NormalFromHeight_World_float(float In, float Strength, float3 Position, float3x3 TangentMatrix, float3* Out)
{
    // Similar to above
    *Out = (float3){TangentMatrix[2].x, TangentMatrix[2].y, TangentMatrix[2].z};
}

void Unity_NormalFromTexture_float(float In, float2 UV, float Offset, float Strength, float3* Out)
{
    // Placeholder
    *Out = (float3){0, 0, 1};
}

void Unity_NormalReconstructZ_float(float2 In, float3* Out)
{
    float reconstructZ = sqrtf(1.0f - saturate(M_DOT2(In, In)));
    *Out = (float3){In.x, In.y, reconstructZ};
}

void Unity_NormalStrength_float(float3 In, float Strength, float3* Out)
{
    *Out = (float3){In.x * Strength, In.y * Strength, lerp(1.0f, In.z, saturate(Strength))};
}

void Unity_NormalUnpack_float(float4 In, float3* Out)
{
    // Placeholder
    *Out = (float3){In.x, In.y, In.z};
}

void Unity_NormalUnpackRGB_float(float4 In, float3* Out)
{
    *Out = (float3){In.x * 2 - 1, In.y * 2 - 1, In.z * 2 - 1};
}

void Unity_ColorspaceConversion_RGB_RGB_float(float3 In, float3* Out)
{
    *Out = In;
}

void Unity_ColorspaceConversion_RGB_RGB_float(float3 In, float3* Out)
{
    // Linear to sRGB
    *Out = (float3){In.x <= 0.0031308f ? In.x * 12.92f : powf(In.x, 1.0f / 2.4f) * 1.055f - 0.055f,
                    In.y <= 0.0031308f ? In.y * 12.92f : powf(In.y, 1.0f / 2.4f) * 1.055f - 0.055f,
                    In.z <= 0.0031308f ? In.z * 12.92f : powf(In.z, 1.0f / 2.4f) * 1.055f - 0.055f};
}

void Unity_ColorspaceConversion_RGB_RGB_float(float3 In, float3* Out)
{
    // RGB to HSV
    float4 K = {0.0f, -1.0f / 3.0f, 2.0f / 3.0f, -1.0f};
    float4 P = lerp((float4){In.z, K.w, In.y, K.x}, (float4){In.y, K.x, In.z, K.w}, In.z < In.y ? 1.0f : 0.0f);
    float4 Q = lerp((float4){P.x, In.x, P.y, P.z}, (float4){In.x, P.z, P.w, P.x}, P.x < In.x ? 1.0f : 0.0f);
    float D = Q.x - M_MIN(Q.w, Q.y);
    float E = 1e-10f;
    *Out = (float3){fabsf(Q.z + (Q.w - Q.y) / (6.0f * D + E)), D / (Q.x + E), Q.x};
}

void Unity_ColorspaceConversion_RGB_RGB_float(float3 In, float3* Out)
{
    // sRGB to Linear
    *Out = (float3){In.x <= 0.04045f ? In.x / 12.92f : powf((In.x + 0.055f) / 1.055f, 2.4f),
                    In.y <= 0.04045f ? In.y / 12.92f : powf((In.y + 0.055f) / 1.055f, 2.4f),
                    In.z <= 0.04045f ? In.z / 12.92f : powf((In.z + 0.055f) / 1.055f, 2.4f)};
}

void Unity_ColorspaceConversion_RGB_RGB_float(float3 In, float3* Out)
{
    *Out = In;
}

void Unity_ColorspaceConversion_RGB_RGB_float(float3 In, float3* Out)
{
    // HSV to RGB
    float4 K = {1.0f, 2.0f / 3.0f, 1.0f / 3.0f, 3.0f};
    float3 P = {fabsf(fmodf(In.x + K.x, 6.0f) - K.w), fabsf(fmodf(In.x + K.y, 6.0f) - K.w), fabsf(fmodf(In.x + K.z, 6.0f) - K.w)};
    *Out = (float3){In.z * (1.0f - saturate(P.x - K.x)), In.z * (1.0f - saturate(P.y - K.x)), In.z * (1.0f - saturate(P.z - K.x))};
}

void Unity_ColorspaceConversion_RGB_RGB_float(float3 In, float3* Out)
{
    // HSV to Linear
    float4 K = {1.0f, 2.0f / 3.0f, 1.0f / 3.0f, 3.0f};
    float3 P = {fabsf(fmodf(In.x + K.x, 6.0f) - K.w), fabsf(fmodf(In.x + K.y, 6.0f) - K.w), fabsf(fmodf(In.x + K.z, 6.0f) - K.w)};
    float3 RGB = {In.z * (1.0f - saturate(P.x - K.x)), In.z * (1.0f - saturate(P.y - K.x)), In.z * (1.0f - saturate(P.z - K.x))};
    *Out = (float3){RGB.x <= 0.04045f ? RGB.x / 12.92f : powf((RGB.x + 0.055f) / 1.055f, 2.4f),
                    RGB.y <= 0.04045f ? RGB.y / 12.92f : powf((RGB.y + 0.055f) / 1.055f, 2.4f),
                    RGB.z <= 0.04045f ? RGB.z / 12.92f : powf((RGB.z + 0.055f) / 1.055f, 2.4f)};
}

void Unity_ColorspaceConversion_RGB_RGB_float(float3 In, float3* Out)
{
    *Out = In;
}

// Channel

void Unity_Combine_float(float R, float G, float B, float A, float4* RGBA, float3* RGB, float2* RG)
{
    *RGBA = (float4){R, G, B, A};
    *RGB = (float3){R, G, B};
    *RG = (float2){R, G};
}

void Unity_Flip_float4(float4 In, float4 Flip, float4* Out)
{
    *Out = (float4){(Flip.x * -2 + 1) * In.x, (Flip.y * -2 + 1) * In.y, (Flip.z * -2 + 1) * In.z, (Flip.w * -2 + 1) * In.w};
}

// Input

void Unity_Time_float(float* Time, float* SineTime, float* CosineTime, float* DeltaTime, float* SmoothDeltaTime)
{
    *Time = ctoy_get_time();
    *SineTime = sinf(*Time);
    *CosineTime = cosf(*Time);
    *DeltaTime = 0.16f; // Placeholder
    *SmoothDeltaTime = 0.16f;   // Placeholder
}

void Unity_Vector1_float(float In, float* Out)
{
    *Out = In;
}

void Unity_Vector2_float(float2 In, float2* Out)
{
    *Out = In;
}

void Unity_Vector3_float(float3 In, float3* Out)
{
    *Out = In;
}

void Unity_Vector4_float(float4 In, float4* Out)
{
    *Out = In;
}

void Unity_Matrix2x2_float(float2x2 In, float2x2* Out)
{
    *Out = In;
}

void Unity_Matrix3x3_float(float3x3 In, float3x3* Out)
{
    *Out = In;
}

void Unity_Matrix4x4_float(float4x4 In, float4x4* Out)
{
    *Out = In;
}

void Unity_Texture2D_float(sprite_t* In, sprite_t** Out)
{
    *Out = In;
}

void Unity_Texture2DArray_float(sprite_t* In, sprite_t** Out)
{
    *Out = In;
}

void Unity_Texture3D_float(sprite_t* In, sprite_t** Out)
{
    *Out = In;
}

void Unity_Cubemap_float(sprite_t* In, sprite_t** Out)
{
    *Out = In;
}

void Unity_SamplerState_float(void* In, void** Out)
{
    *Out = In;
}

void Unity_Constant_float(float In, float* Out)
{
    *Out = In;
}

void Unity_Property_float(float In, float* Out)
{
    *Out = In;
}

// Math

void Unity_Add_float(float A, float B, float* Out)
{
    *Out = A + B;
}

void Unity_Add_float2(float2 A, float2 B, float2* Out)
{
    M_ADD2(*Out, A, B);
}

void Unity_Add_float3(float3 A, float3 B, float3* Out)
{
    M_ADD3(*Out, A, B);
}

void Unity_Add_float4(float4 A, float4 B, float4* Out)
{
    M_ADD4(*Out, A, B);
}

void Unity_Subtract_float(float A, float B, float* Out)
{
    *Out = A - B;
}

void Unity_Subtract_float2(float2 A, float2 B, float2* Out)
{
    M_SUB2(*Out, A, B);
}

void Unity_Subtract_float3(float3 A, float3 B, float3* Out)
{
    M_SUB3(*Out, A, B);
}

void Unity_Subtract_float4(float4 A, float4 B, float4* Out)
{
    M_SUB4(*Out, A, B);
}

void Unity_Multiply_float(float A, float B, float* Out)
{
    *Out = A * B;
}

void Unity_Multiply_float2(float2 A, float2 B, float2* Out)
{
    M_MUL2(*Out, A, B);
}

void Unity_Multiply_float3(float3 A, float3 B, float3* Out)
{
    M_MUL3(*Out, A, B);
}

void Unity_Multiply_float4(float4 A, float4 B, float4* Out)
{
    M_MUL4(*Out, A, B);
}

void Unity_Divide_float(float A, float B, float* Out)
{
    *Out = A / B;
}

void Unity_Divide_float2(float2 A, float2 B, float2* Out)
{
    M_DIV2(*Out, A, B);
}

void Unity_Divide_float3(float3 A, float3 B, float3* Out)
{
    M_DIV3(*Out, A, B);
}

void Unity_Divide_float4(float4 A, float4 B, float4* Out)
{
    M_DIV4(*Out, A, B);
}

void Unity_Power_float(float A, float B, float* Out)
{
    *Out = powf(A, B);
}

void Unity_SquareRoot_float(float In, float* Out)
{
    *Out = sqrtf(In);
}

void Unity_Log_float(float In, float* Out)
{
    *Out = logf(In);
}

void Unity_Exp_float(float In, float* Out)
{
    *Out = expf(In);
}

void Unity_Absolute_float(float In, float* Out)
{
    *Out = fabsf(In);
}

void Unity_Negate_float(float In, float* Out)
{
    *Out = -In;
}

void Unity_Sign_float(float In, float* Out)
{
    *Out = In > 0 ? 1.0f : (In < 0 ? -1.0f : 0.0f);
}

void Unity_Floor_float(float In, float* Out)
{
    *Out = floorf(In);
}

void Unity_Ceil_float(float In, float* Out)
{
    *Out = ceilf(In);
}

void Unity_Round_float(float In, float* Out)
{
    *Out = roundf(In);
}

void Unity_Truncate_float(float In, float* Out)
{
    *Out = truncf(In);
}

void Unity_Fraction_float(float In, float* Out)
{
    *Out = In - floorf(In);
}

void Unity_Modulo_float(float A, float B, float* Out)
{
    *Out = fmodf(A, B);
}

void Unity_Maximum_float(float A, float B, float* Out)
{
    *Out = M_MAX(A, B);
}

void Unity_Minimum_float(float A, float B, float* Out)
{
    *Out = M_MIN(A, B);
}

void Unity_Clamp_float(float In, float Min, float Max, float* Out)
{
    *Out = M_CLAMP(In, Min, Max);
}

void Unity_Saturate_float(float In, float* Out)
{
    *Out = M_CLAMP(In, 0.0f, 1.0f);
}

void Unity_Lerp_float(float A, float B, float T, float* Out)
{
    *Out = A + T * (B - A);
}

void Unity_Lerp_float2(float2 A, float2 B, float2 T, float2* Out)
{
    Out->x = A.x + T.x * (B.x - A.x);
    Out->y = A.y + T.y * (B.y - A.y);
}

void Unity_Lerp_float3(float3 A, float3 B, float3 T, float3* Out)
{
    Out->x = A.x + T.x * (B.x - A.x);
    Out->y = A.y + T.y * (B.y - A.y);
    Out->z = A.z + T.z * (B.z - A.z);
}

void Unity_Lerp_float4(float4 A, float4 B, float4 T, float4* Out)
{
    Out->x = A.x + T.x * (B.x - A.x);
    Out->y = A.y + T.y * (B.y - A.y);
    Out->z = A.z + T.z * (B.z - A.z);
    Out->w = A.w + T.w * (B.w - A.w);
}

void Unity_Smoothstep_float(float Edge1, float Edge2, float In, float* Out)
{
    float t = M_CLAMP((In - Edge1) / (Edge2 - Edge1), 0.0f, 1.0f);
    *Out = t * t * (3.0f - 2.0f * t);
}

void Unity_OneMinus_float(float In, float* Out)
{
    *Out = 1.0f - In;
}

void Unity_Reciprocal_float(float In, float* Out)
{
    *Out = 1.0f / In;
}

void Unity_DegreesToRadians_float(float In, float* Out)
{
    *Out = In * M_DEG_TO_RAD;
}

void Unity_RadiansToDegrees_float(float In, float* Out)
{
    *Out = In * M_RAD_TO_DEG;
}

void Unity_Distance_float(float3 A, float3 B, float* Out)
{
    *Out = M_LENGHT3((float3){A.x - B.x, A.y - B.y, A.z - B.z});
}

void Unity_Length_float(float3 In, float* Out)
{
    *Out = M_LENGHT3(In);
}

void Unity_Normalize_float(float3 In, float3* Out)
{
    M_NORMALIZE3(*Out, In);
}

void Unity_CrossProduct_float(float3 A, float3 B, float3* Out)
{
    M_CROSS3(*Out, A, B);
}

void Unity_DotProduct_float(float3 A, float3 B, float* Out)
{
    *Out = M_DOT3(A, B);
}
void Unity_MatrixConstruction_CameraProjection_float(float4x4* Out)
{
    // Construct perspective projection matrix
    float fov = GetCameraFov();
    float aspect = GetCameraAspect();
    float zNear = GetCameraZNear();
    float zFar = GetCameraZFar();
    
    float f = 1.0f / tanf(fov * M_PI / 360.0f);
    float* mat = (float*)Out;
    mat[0] = f / aspect;
    mat[1] = 0.0f;
    mat[2] = 0.0f;
    mat[3] = 0.0f;
    mat[4] = 0.0f;
    mat[5] = f;
    mat[6] = 0.0f;
    mat[7] = 0.0f;
    mat[8] = 0.0f;
    mat[9] = 0.0f;
    mat[10] = (zFar + zNear) / (zNear - zFar);
    mat[11] = -1.0f;
    mat[12] = 0.0f;
    mat[13] = 0.0f;
    mat[14] = (2.0f * zFar * zNear) / (zNear - zFar);
    mat[15] = 0.0f;
}

void Unity_MatrixConstruction_ModelView_float(float4x4* Out)
{
    m_mat4_identity((float*)Out);
}

void Unity_MatrixConstruction_ViewProjection_float(float4x4* Out)
{
    m_mat4_identity((float*)Out);
}

void Unity_MatrixConstruction_WorldViewProjection_float(float4x4* Out)
{
    m_mat4_identity((float*)Out);
}

void Unity_MatrixConstruction_ObjectToWorld_float(float4x4* Out)
{
    m_mat4_identity((float*)Out);
}

void Unity_MatrixConstruction_WorldToObject_float(float4x4* Out)
{
    m_mat4_identity((float*)Out);
}

void Unity_MatrixConstruction_Transpose_float(float4x4 In, float4x4* Out)
{
    m_mat4_transpose((float*)Out, (float*)&In);
}

void Unity_MatrixConstruction_Inverse_float(float4x4 In, float4x4* Out)
{
    m_mat4_inverse((float*)Out, (float*)&In);
}

void Unity_MatrixMultiply_float(float4x4 A, float4x4 B, float4x4* Out)
{
    m_mat4_mul((float*)Out, (float*)&A, (float*)&B);
}

void Unity_MatrixMultiplyVector_float(float4x4 M, float4 V, float4* Out)
{
    m_mat4_transform4(Out, (float*)&M, &V);
}

void Unity_Blackbody_float(float Temperature, float3* Out)
{
    float3 color = {255.0f, 255.0f, 255.0f};
    color.x = 56100000.0f * powf(Temperature, -3.0f / 2.0f) + 148.0f;
    color.y = 100.04f * logf(Temperature) - 623.6f;
    if (Temperature > 6500.0f) color.y = 35200000.0f * powf(Temperature, -3.0f / 2.0f) + 184.0f;
    color.z = 194.18f * logf(Temperature) - 1448.6f;
    color.x = M_CLAMP(color.x, 0.0f, 255.0f);
    color.y = M_CLAMP(color.y, 0.0f, 255.0f);
    color.z = M_CLAMP(color.z, 0.0f, 255.0f);
    *Out = (float3){color.x / 255.0f, color.y / 255.0f, color.z / 255.0f};
    if (Temperature < 1000.0f) {
        Out->x *= Temperature / 1000.0f;
        Out->y *= Temperature / 1000.0f;
        Out->z *= Temperature / 1000.0f;
    }
}

typedef struct {
    int type;
    int colorsLength;
    int alphasLength;
    float4 colors[8];
    float alphas[8];
} Gradient;

Gradient Unity_Gradient_float()
{
    Gradient g;
    g.type = 1;
    g.colorsLength = 4;
    g.alphasLength = 4;
    g.colors[0] = (float4){0.1f, 0.1f, 0.1f, 1.0f};
    g.colors[1] = (float4){0.2f, 0.2f, 0.2f, 1.0f};
    g.colors[2] = (float4){0.3f, 0.3f, 0.3f, 1.0f};
    g.colors[3] = (float4){0.4f, 0.4f, 0.4f, 1.0f};
    g.alphas[0] = 0.1f;
    g.alphas[1] = 0.25f;
    g.alphas[2] = 0.5f;
    g.alphas[3] = 1.0f;
    return g;
}

void Unity_SampleGradient_float(Gradient gradient, float Time, float4* Out)
{
    // Clamp Time to 0-1
    Time = Time < 0.0f ? 0.0f : (Time > 1.0f ? 1.0f : Time);
    
    int numStops = gradient.colorsLength;
    if (numStops <= 0) {
        *Out = (float4){0.0f, 0.0f, 0.0f, 1.0f};
        return;
    }
    
    if (numStops == 1) {
        *Out = gradient.colors[0];
        Out->w = gradient.alphas[0];
        return;
    }
    
    // Interpolate
    float t = Time * (numStops - 1);
    int index = (int)t;
    float frac = t - index;
    
    if (index >= numStops - 1) {
        index = numStops - 1;
        frac = 0.0f;
    }
    
    float4 c1 = gradient.colors[index];
    float4 c2 = gradient.colors[index + 1];
    float a1 = gradient.alphas[index];
    float a2 = gradient.alphas[index + 1];
    
    Out->x = c1.x + frac * (c2.x - c1.x);
    Out->y = c1.y + frac * (c2.y - c1.y);
    Out->z = c1.z + frac * (c2.z - c1.z);
    Out->w = a1 + frac * (a2 - a1);
}

// Procedural

void Unity_Checkerboard_float(float2 UV, float3 ColorA, float3 ColorB, float2 Frequency, float3* Out)
{
    float2 p = {UV.x * Frequency.x, UV.y * Frequency.y};
    int ix = (int)floorf(p.x);
    int iy = (int)floorf(p.y);
    if ((ix + iy) % 2 == 0) *Out = ColorA; else *Out = ColorB;
}

void Unity_GradientNoise_float(float2 UV, float Scale, float* Out)
{
    float2 p = {UV.x * Scale, UV.y * Scale};
    float2 ip = {floorf(p.x), floorf(p.y)};
    float2 fp = {p.x - ip.x, p.y - ip.y};
    float d00 = dot((float2){ip.x, ip.y}, (float2){127.1f, 311.7f});
    float d01 = dot((float2){ip.x, ip.y + 1}, (float2){127.1f, 311.7f});
    float d10 = dot((float2){ip.x + 1, ip.y}, (float2){127.1f, 311.7f});
    float d11 = dot((float2){ip.x + 1, ip.y + 1}, (float2){127.1f, 311.7f});
    d00 = frac(sin(d00) * 43758.5453f);
    d01 = frac(sin(d01) * 43758.5453f);
    d10 = frac(sin(d10) * 43758.5453f);
    d11 = frac(sin(d11) * 43758.5453f);
    float2 t = {smoothstep(0, 1, fp.x), smoothstep(0, 1, fp.y)};
    *Out = lerp(lerp(d00, d01, t.y), lerp(d10, d11, t.y), t.x);
}

void Unity_SimpleNoise_float(float2 UV, float Scale, float* Out)
{
    float t = 0.0f;
    float2 p = {UV.x * Scale, UV.y * Scale};
    float2 ip = {floorf(p.x), floorf(p.y)};
    float2 fp = {p.x - ip.x, p.y - ip.y};
    for (int i = -1; i <= 1; i++)
    for (int j = -1; j <= 1; j++)
    {
        float2 offset = {i, j};
        float2 o = {ip.x + offset.x, ip.y + offset.y};
        float d = dot(o, (float2){127.1f, 311.7f});
        float w = exp(-dot((float2){fp.x - offset.x, fp.y - offset.y}, (float2){fp.x - offset.x, fp.y - offset.y}) * 4.0f);
        t += w * frac(sin(d) * 43758.5453f);
    }
    *Out = t;
}

void Unity_Voronoi_float(float2 UV, float AngleOffset, float CellDensity, float* Out, float* Cells)
{
    float2 g = {floorf(UV.x * CellDensity), floorf(UV.y * CellDensity)};
    float2 f = {UV.x * CellDensity - g.x, UV.y * CellDensity - g.y};
    float t = 8.0f;
    float3 res = {8.0f, 0.0f, 0.0f};

    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float2 lattice = {x, y};
            float2 offset = {sin((g.x + lattice.x) * AngleOffset + 0.5f) * 0.5f + 0.5f, cos((g.y + lattice.y) * AngleOffset + 0.5f) * 0.5f + 0.5f};
            float d = M_LENGHT2((float2){lattice.x + offset.x - f.x, lattice.y + offset.y - f.y});
            if (d < res.x)
            {
                res.x = d;
                res.y = offset.x;
                res.z = offset.y;
            }
        }
    }
    *Out = res.x;
    *Cells = res.y;
}

// UV

void Unity_TilingAndOffset_float(float2 UV, float2 Tiling, float2 Offset, float2* Out)
{
    Out->x = UV.x * Tiling.x + Offset.x;
    Out->y = UV.y * Tiling.y + Offset.y;
}

void Unity_Triplanar_float(float3 Position, float3 Normal, float3 Scale, float3 Offset, float Blend, void* Sampler, void* Texture, float4* Out)
{
    // Placeholder
    *Out = (float4){0.5f, 0.5f, 0.5f, 1.0f};
}

void Unity_Rotate_float(float2 UV, float2 Center, float Rotation, float2* Out)
{
    float2 delta = {UV.x - Center.x, UV.y - Center.y};
    float s = sinf(Rotation);
    float c = cosf(Rotation);
    Out->x = c * delta.x - s * delta.y + Center.x;
    Out->y = s * delta.x + c * delta.y + Center.y;
}

void Unity_Spherize_float(float2 UV, float2 Center, float Strength, float2 Offset, float2* Out)
{
    float2 delta = {UV.x - Center.x, UV.y - Center.y};
    float delta2 = M_DOT2(delta, delta);
    float delta4 = delta2 * delta2;
    float2 delta_offset = {delta4 * Strength, delta4 * Strength};
    Out->x = UV.x + delta.x * delta_offset.x + Offset.x;
    Out->y = UV.y + delta.y * delta_offset.y + Offset.y;
}

void Unity_Twirl_float(float2 UV, float2 Center, float Strength, float2 Offset, float2* Out)
{
    float2 delta = {UV.x - Center.x, UV.y - Center.y};
    float angle = Strength * M_LENGHT2(delta);
    float x = cosf(angle) * delta.x - sinf(angle) * delta.y;
    float y = sinf(angle) * delta.x + cosf(angle) * delta.y;
    Out->x = x + Center.x + Offset.x;
    Out->y = y + Center.y + Offset.y;
}

// Utility

void Unity_Branch_float(float Predicate, float True, float False, float* Out)
{
    *Out = Predicate ? True : False;
}

void Unity_Branch_float2(float Predicate, float2 True, float2 False, float2* Out)
{
    *Out = Predicate ? True : False;
}

void Unity_Branch_float3(float Predicate, float3 True, float3 False, float3* Out)
{
    *Out = Predicate ? True : False;
}

void Unity_Branch_float4(float Predicate, float4 True, float4 False, float4* Out)
{
    *Out = Predicate ? True : False;
}

void Unity_Preview_float(float In, float* Out)
{
    *Out = In;
}

void Unity_Preview_float2(float2 In, float2* Out)
{
    *Out = In;
}

void Unity_Preview_float3(float3 In, float3* Out)
{
    *Out = In;
}

void Unity_Preview_float4(float4 In, float4* Out)
{
    *Out = In;
}

void Unity_SceneColor_float(float4 UV, float3* Out)
{
    *Out = (float3){0.5f, 0.5f, 0.5f};
}

void Unity_SceneDepth_Raw_float(float4 UV, float* Out)
{
    *Out = 0.5f;
}

float4 SAMPLE_TEXTURE2D(sprite_t* tex, void* samp, float2 uv)
{
    if (!tex || !tex->data || tex->width <= 0 || tex->height <= 0) {
        return (float4){0.0f, 0.0f, 0.0f, 1.0f};
    }
    
    // Clamp UV to 0-1
    uv.x = uv.x < 0.0f ? 0.0f : (uv.x > 1.0f ? 1.0f : uv.x);
    uv.y = uv.y < 0.0f ? 0.0f : (uv.y > 1.0f ? 1.0f : uv.y);
    
    // Nearest neighbor sampling
    int x = (int)(uv.x * (tex->width - 1));
    int y = (int)(uv.y * (tex->height - 1));
    
    int index = (y * tex->width + x) * tex->channels;
    unsigned char r = tex->data[index];
    unsigned char g = tex->data[index + 1];
    unsigned char b = tex->data[index + 2];
    unsigned char a = (tex->channels >= 4) ? tex->data[index + 3] : 255;
    
    return (float4){r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f};
}

// Math
void Unity_Absolute_float4(float4 In, float4* Out)
{
    Out->x = fabsf(In.x);
    Out->y = fabsf(In.y);
    Out->z = fabsf(In.z);
    Out->w = fabsf(In.w);
}

void Unity_Exponential_float4(float4 In, float4* Out)
{
    Out->x = expf(In.x);
    Out->y = expf(In.y);
    Out->z = expf(In.z);
    Out->w = expf(In.w);
}

void Unity_Exponential2_float4(float4 In, float4* Out)
{
    Out->x = exp2f(In.x);
    Out->y = exp2f(In.y);
    Out->z = exp2f(In.z);
    Out->w = exp2f(In.w);
}

void Unity_Length_float4(float4 In, float* Out)
{
    *Out = M_LENGHT4(In);
}

void Unity_Log_float4(float4 In, float4* Out)
{
    Out->x = logf(In.x);
    Out->y = logf(In.y);
    Out->z = logf(In.z);
    Out->w = logf(In.w);
}

void Unity_Log2_float4(float4 In, float4* Out)
{
    Out->x = log2f(In.x);
    Out->y = log2f(In.y);
    Out->z = log2f(In.z);
    Out->w = log2f(In.w);
}

void Unity_Log10_float4(float4 In, float4* Out)
{
    Out->x = log10f(In.x);
    Out->y = log10f(In.y);
    Out->z = log10f(In.z);
    Out->w = log10f(In.w);
}

void Unity_Modulo_float4(float4 A, float4 B, float4* Out)
{
    Out->x = fmodf(A.x, B.x);
    Out->y = fmodf(A.y, B.y);
    Out->z = fmodf(A.z, B.z);
    Out->w = fmodf(A.w, B.w);
}

void Unity_Negate_float4(float4 In, float4* Out)
{
    Out->x = -In.x;
    Out->y = -In.y;
    Out->z = -In.z;
    Out->w = -In.w;
}

void Unity_Normalize_float4(float4 In, float4* Out)
{
    float l = M_LENGHT4(In);
    if (l > 0) {
        float m = 1.0f / l;
        Out->x = In.x * m;
        Out->y = In.y * m;
        Out->z = In.z * m;
        Out->w = In.w * m;
    } else {
        Out->x = Out->y = Out->z = Out->w = 0.0f;
    }
}

void Unity_Posterize_float4(float4 In, float4 Steps, float4* Out)
{
    Out->x = floorf(In.x / (1.0f / Steps.x)) * (1.0f / Steps.x);
    Out->y = floorf(In.y / (1.0f / Steps.y)) * (1.0f / Steps.y);
    Out->z = floorf(In.z / (1.0f / Steps.z)) * (1.0f / Steps.z);
    Out->w = floorf(In.w / (1.0f / Steps.w)) * (1.0f / Steps.w);
}

void Unity_Reciprocal_float4(float4 In, float4* Out)
{
    Out->x = 1.0f / In.x;
    Out->y = 1.0f / In.y;
    Out->z = 1.0f / In.z;
    Out->w = 1.0f / In.w;
}

void Unity_ReciprocalSquareRoot_float4(float4 In, float4* Out)
{
    Out->x = 1.0f / sqrtf(In.x);
    Out->y = 1.0f / sqrtf(In.y);
    Out->z = 1.0f / sqrtf(In.z);
    Out->w = 1.0f / sqrtf(In.w);
}

void Unity_Add_float4(float4 A, float4 B, float4* Out)
{
    M_ADD4(*Out, A, B);
}

void Unity_Divide_float4(float4 A, float4 B, float4* Out)
{
    M_DIV4(*Out, A, B);
}

void Unity_Multiply_float4_float4(float4 A, float4 B, float4* Out)
{
    M_MUL4(*Out, A, B);
}

void Unity_Multiply_float4_float4x4(float4 A, float4x4 B, float4* Out)
{
    m_mat4_transform4(Out, (float*)&B, &A);
}

void Unity_Multiply_float4x4_float4x4(float4x4 A, float4x4 B, float4x4* Out)
{
    m_mat4_mul((float*)Out, (float*)&A, (float*)&B);
}

void Unity_Power_float4(float4 A, float4 B, float4* Out)
{
    Out->x = powf(A.x, B.x);
    Out->y = powf(A.y, B.y);
    Out->z = powf(A.z, B.z);
    Out->w = powf(A.w, B.w);
}

void Unity_SquareRoot_float4(float4 In, float4* Out)
{
    Out->x = sqrtf(In.x);
    Out->y = sqrtf(In.y);
    Out->z = sqrtf(In.z);
    Out->w = sqrtf(In.w);
}

void Unity_Subtract_float4(float4 A, float4 B, float4* Out)
{
    M_SUB4(*Out, A, B);
}

void Unity_DDX_float4(float4 In, float4* Out)
{
    // Placeholder
    *Out = In;
}

void Unity_DDXY_float4(float4 In, float4* Out)
{
    // Placeholder
    *Out = In;
}

void Unity_DDY_float4(float4 In, float4* Out)
{
    // Placeholder
    *Out = In;
}

void Unity_InverseLerp_float4(float4 A, float4 B, float4 T, float4* Out)
{
    Out->x = (T.x - A.x) / (B.x - A.x);
    Out->y = (T.y - A.y) / (B.y - A.y);
    Out->z = (T.z - A.z) / (B.z - A.z);
    Out->w = (T.w - A.w) / (B.w - A.w);
}

void Unity_Lerp_float4(float4 A, float4 B, float4 T, float4* Out)
{
    Out->x = A.x + T.x * (B.x - A.x);
    Out->y = A.y + T.y * (B.y - A.y);
    Out->z = A.z + T.z * (B.z - A.z);
    Out->w = A.w + T.w * (B.w - A.w);
}

void Unity_Smoothstep_float4(float4 Edge1, float4 Edge2, float4 In, float4* Out)
{
    Out->x = smoothstep(Edge1.x, Edge2.x, In.x);
    Out->y = smoothstep(Edge1.y, Edge2.y, In.y);
    Out->z = smoothstep(Edge1.z, Edge2.z, In.z);
    Out->w = smoothstep(Edge1.w, Edge2.w, In.w);
}

void Unity_Clamp_float4(float4 In, float4 Min, float4 Max, float4* Out)
{
    Out->x = M_CLAMP(In.x, Min.x, Max.x);
    Out->y = M_CLAMP(In.y, Min.y, Max.y);
    Out->z = M_CLAMP(In.z, Min.z, Max.z);
    Out->w = M_CLAMP(In.w, Min.w, Max.w);
}

void Unity_Fraction_float4(float4 In, float4* Out)
{
    Out->x = In.x - floorf(In.x);
    Out->y = In.y - floorf(In.y);
    Out->z = In.z - floorf(In.z);
    Out->w = In.w - floorf(In.w);
}

void Unity_Maximum_float4(float4 A, float4 B, float4* Out)
{
    Out->x = M_MAX(A.x, B.x);
    Out->y = M_MAX(A.y, B.y);
    Out->z = M_MAX(A.z, B.z);
    Out->w = M_MAX(A.w, B.w);
}

void Unity_Minimum_float4(float4 A, float4 B, float4* Out)
{
    Out->x = M_MIN(A.x, B.x);
    Out->y = M_MIN(A.y, B.y);
    Out->z = M_MIN(A.z, B.z);
    Out->w = M_MIN(A.w, B.w);
}

void Unity_OneMinus_float4(float4 In, float4* Out)
{
    Out->x = 1.0f - In.x;
    Out->y = 1.0f - In.y;
    Out->z = 1.0f - In.z;
    Out->w = 1.0f - In.w;
}

void Unity_RandomRange_float(float2 Seed, float Min, float Max, float* Out)
{
    float randomno = frac(sin(M_DOT2(Seed, (float2){12.9898f, 78.233f})) * 43758.5453f);
    *Out = Min + randomno * (Max - Min);
}

void Unity_Remap_float4(float4 In, float2 InMinMax, float2 OutMinMax, float4* Out)
{
    Out->x = OutMinMax.x + (In.x - InMinMax.x) * (OutMinMax.y - OutMinMax.x) / (InMinMax.y - InMinMax.x);
    Out->y = OutMinMax.y + (In.y - InMinMax.x) * (OutMinMax.y - OutMinMax.x) / (InMinMax.y - InMinMax.x);
    Out->z = OutMinMax.x + (In.z - InMinMax.x) * (OutMinMax.y - OutMinMax.x) / (InMinMax.y - InMinMax.x);
    Out->w = OutMinMax.x + (In.w - InMinMax.x) * (OutMinMax.y - OutMinMax.x) / (InMinMax.y - InMinMax.x);
}

void Unity_Saturate_float4(float4 In, float4* Out)
{
    Out->x = M_CLAMP(In.x, 0.0f, 1.0f);
    Out->y = M_CLAMP(In.y, 0.0f, 1.0f);
    Out->z = M_CLAMP(In.z, 0.0f, 1.0f);
    Out->w = M_CLAMP(In.w, 0.0f, 1.0f);
}

void Unity_Ceiling_float4(float4 In, float4* Out)
{
    Out->x = ceilf(In.x);
    Out->y = ceilf(In.y);
    Out->z = ceilf(In.z);
    Out->w = ceilf(In.w);
}

void Unity_Floor_float4(float4 In, float4* Out)
{
    Out->x = floorf(In.x);
    Out->y = floorf(In.y);
    Out->z = floorf(In.z);
    Out->w = floorf(In.w);
}

void Unity_Round_float4(float4 In, float4* Out)
{
    Out->x = roundf(In.x);
    Out->y = roundf(In.y);
    Out->z = roundf(In.z);
    Out->w = roundf(In.w);
}

void Unity_Sign_float4(float4 In, float4* Out)
{
    Out->x = In.x > 0 ? 1.0f : (In.x < 0 ? -1.0f : 0.0f);
    Out->y = In.y > 0 ? 1.0f : (In.y < 0 ? -1.0f : 0.0f);
    Out->z = In.z > 0 ? 1.0f : (In.z < 0 ? -1.0f : 0.0f);
    Out->w = In.w > 0 ? 1.0f : (In.w < 0 ? -1.0f : 0.0f);
}

void Unity_Step_float4(float4 Edge, float4 In, float4* Out)
{
    Out->x = In.x >= Edge.x ? 1.0f : 0.0f;
    Out->y = In.y >= Edge.y ? 1.0f : 0.0f;
    Out->z = In.z >= Edge.z ? 1.0f : 0.0f;
    Out->w = In.w >= Edge.w ? 1.0f : 0.0f;
}

void Unity_Truncate_float4(float4 In, float4* Out)
{
    Out->x = truncf(In.x);
    Out->y = truncf(In.y);
    Out->z = truncf(In.z);
    Out->w = truncf(In.w);
}

void Unity_Arccosine_float4(float4 In, float4* Out)
{
    Out->x = acosf(In.x);
    Out->y = acosf(In.y);
    Out->z = acosf(In.z);
    Out->w = acosf(In.w);
}

void Unity_Arcsine_float4(float4 In, float4* Out)
{
    Out->x = asinf(In.x);
    Out->y = asinf(In.y);
    Out->z = asinf(In.z);
    Out->w = asinf(In.w);
}

void Unity_Arctangent_float4(float4 In, float4* Out)
{
    Out->x = atanf(In.x);
    Out->y = atanf(In.y);
    Out->z = atanf(In.z);
    Out->w = atanf(In.w);
}

void Unity_MatrixConstruction_Row_float(float4 M0, float4 M1, float4 M2, float3 M3, float4x4* Out4x4, float3x3* Out3x3, float2x2* Out2x2)
{
    // Construct 4x4 matrix from rows
    (*Out4x4)[0] = M0;
    (*Out4x4)[1] = M1;
    (*Out4x4)[2] = M2;
    (*Out4x4)[3] = (float4){M3.x, M3.y, M3.z, 1.0f};
    
    // Construct 3x3 matrix (top-left)
    (*Out3x3)[0] = (float3){M0.x, M0.y, M0.z};
    (*Out3x3)[1] = (float3){M1.x, M1.y, M1.z};
    (*Out3x3)[2] = (float3){M2.x, M2.y, M2.z};
    
    // Construct 2x2 matrix (top-left)
    (*Out2x2)[0] = (float2){M0.x, M0.y};
    (*Out2x2)[1] = (float2){M1.x, M1.y};
}

void Unity_MatrixConstruction_Column_float(float4 M0, float4 M1, float4 M2, float3 M3, float4x4* Out4x4, float3x3* Out3x3, float2x2* Out2x2)
{
    // Construct 4x4 matrix from columns
    (*Out4x4)[0] = (float4){M0.x, M1.x, M2.x, M3.x};
    (*Out4x4)[1] = (float4){M0.y, M1.y, M2.y, M3.y};
    (*Out4x4)[2] = (float4){M0.z, M1.z, M2.z, M3.z};
    (*Out4x4)[3] = (float4){M0.w, M1.w, M2.w, 1.0f};
    
    // Construct 3x3 matrix (top-left)
    (*Out3x3)[0] = (float3){M0.x, M1.x, M2.x};
    (*Out3x3)[1] = (float3){M0.y, M1.y, M2.y};
    (*Out3x3)[2] = (float3){M0.z, M1.z, M2.z};
    
    // Construct 2x2 matrix (top-left)
    (*Out2x2)[0] = (float2){M0.x, M1.x};
    (*Out2x2)[1] = (float2){M0.y, M1.y};
}

void Unity_MatrixDeterminant_float4x4(float4x4 In, float* Out)
{
    // Compute determinant of 4x4 matrix using cofactor expansion
    float m00 = In[0].x, m01 = In[0].y, m02 = In[0].z, m03 = In[0].w;
    float m10 = In[1].x, m11 = In[1].y, m12 = In[1].z, m13 = In[1].w;
    float m20 = In[2].x, m21 = In[2].y, m22 = In[2].z, m23 = In[2].w;
    float m30 = In[3].x, m31 = In[3].y, m32 = In[3].z, m33 = In[3].w;
    
    *Out = m00 * (m11*(m22*m33 - m23*m32) - m12*(m21*m33 - m23*m31) + m13*(m21*m32 - m22*m31)) -
           m01 * (m10*(m22*m33 - m23*m32) - m12*(m20*m33 - m23*m30) + m13*(m20*m32 - m22*m30)) +
           m02 * (m10*(m21*m33 - m23*m31) - m11*(m20*m33 - m23*m30) + m13*(m20*m31 - m21*m30)) -
           m03 * (m10*(m21*m32 - m22*m31) - m11*(m20*m32 - m22*m30) + m12*(m20*m31 - m21*m30));
}

void Unity_MatrixTranspose_float4x4(float4x4 In, float4x4* Out)
{
    m_mat4_transpose((float*)Out, (float*)&In);
}

void Unity_Clamp_float4(float4 In, float4 Min, float4 Max, float4* Out)
{
    Out->x = M_CLAMP(In.x, Min.x, Max.x);
    Out->y = M_CLAMP(In.y, Min.y, Max.y);
    Out->z = M_CLAMP(In.z, Min.z, Max.z);
    Out->w = M_CLAMP(In.w, Min.w, Max.w);
}

void Unity_Fraction_float4(float4 In, float4* Out)
{
    Out->x = In.x - floorf(In.x);
    Out->y = In.y - floorf(In.y);
    Out->z = In.z - floorf(In.z);
    Out->w = In.w - floorf(In.w);
}

void Unity_Maximum_float4(float4 A, float4 B, float4* Out)
{
    Out->x = M_MAX(A.x, B.x);
    Out->y = M_MAX(A.y, B.y);
    Out->z = M_MAX(A.z, B.z);
    Out->w = M_MAX(A.w, B.w);
}

void Unity_Minimum_float4(float4 A, float4 B, float4* Out)
{
    Out->x = M_MIN(A.x, B.x);
    Out->y = M_MIN(A.y, B.y);
    Out->z = M_MIN(A.z, B.z);
    Out->w = M_MIN(A.w, B.w);
}

void Unity_OneMinus_float4(float4 In, float4* Out)
{
    Out->x = 1.0f - In.x;
    Out->y = 1.0f - In.y;
    Out->z = 1.0f - In.z;
    Out->w = 1.0f - In.w;
}

void Unity_RandomRange_float(float2 Seed, float Min, float Max, float* Out)
{
    float randomno = frac(sin(M_DOT2(Seed, (float2){12.9898f, 78.233f})) * 43758.5453f);
    *Out = Min + randomno * (Max - Min);
}

void Unity_Remap_float4(float4 In, float2 InMinMax, float2 OutMinMax, float4* Out)
{
    Out->x = OutMinMax.x + (In.x - InMinMax.x) * (OutMinMax.y - OutMinMax.x) / (InMinMax.y - InMinMax.x);
    Out->y = OutMinMax.y + (In.y - InMinMax.x) * (OutMinMax.y - OutMinMax.x) / (InMinMax.y - InMinMax.x);
    Out->z = OutMinMax.x + (In.z - InMinMax.x) * (OutMinMax.y - OutMinMax.x) / (InMinMax.y - InMinMax.x);
    Out->w = OutMinMax.x + (In.w - InMinMax.x) * (OutMinMax.y - OutMinMax.x) / (InMinMax.y - InMinMax.x);
}

void Unity_Saturate_float4(float4 In, float4* Out)
{
    Out->x = M_CLAMP(In.x, 0.0f, 1.0f);
    Out->y = M_CLAMP(In.y, 0.0f, 1.0f);
    Out->z = M_CLAMP(In.z, 0.0f, 1.0f);
    Out->w = M_CLAMP(In.w, 0.0f, 1.0f);
}

void Unity_Ceiling_float4(float4 In, float4* Out)
{
    Out->x = ceilf(In.x);
    Out->y = ceilf(In.y);
    Out->z = ceilf(In.z);
    Out->w = ceilf(In.w);
}

void Unity_Floor_float4(float4 In, float4* Out)
{
    Out->x = floorf(In.x);
    Out->y = floorf(In.y);
    Out->z = floorf(In.z);
    Out->w = floorf(In.w);
}

void Unity_Round_float4(float4 In, float4* Out)
{
    Out->x = roundf(In.x);
    Out->y = roundf(In.y);
    Out->z = roundf(In.z);
    Out->w = roundf(In.w);
}

void Unity_Sign_float4(float4 In, float4* Out)
{
    Out->x = In.x > 0 ? 1.0f : (In.x < 0 ? -1.0f : 0.0f);
    Out->y = In.y > 0 ? 1.0f : (In.y < 0 ? -1.0f : 0.0f);
    Out->z = In.z > 0 ? 1.0f : (In.z < 0 ? -1.0f : 0.0f);
    Out->w = In.w > 0 ? 1.0f : (In.w < 0 ? -1.0f : 0.0f);
}

void Unity_Step_float4(float4 Edge, float4 In, float4* Out)
{
    Out->x = In.x >= Edge.x ? 1.0f : 0.0f;
    Out->y = In.y >= Edge.y ? 1.0f : 0.0f;
    Out->z = In.z >= Edge.z ? 1.0f : 0.0f;
    Out->w = In.w >= Edge.w ? 1.0f : 0.0f;
}

void Unity_Truncate_float4(float4 In, float4* Out)
{
    Out->x = truncf(In.x);
    Out->y = truncf(In.y);
    Out->z = truncf(In.z);
    Out->w = truncf(In.w);
}

void Unity_Arccosine_float4(float4 In, float4* Out)
{
    Out->x = acosf(In.x);
    Out->y = acosf(In.y);
    Out->z = acosf(In.z);
    Out->w = acosf(In.w);
}

void Unity_Arcsine_float4(float4 In, float4* Out)
{
    Out->x = asinf(In.x);
    Out->y = asinf(In.y);
    Out->z = asinf(In.z);
    Out->w = asinf(In.w);
}

void Unity_Arctangent_float4(float4 In, float4* Out)
{
    Out->x = atanf(In.x);
    Out->y = atanf(In.y);
    Out->z = atanf(In.z);
    Out->w = atanf(In.w);
}

void Unity_Camera_float(out float3 Position, out float3 Direction, out float3 Up, out float3 Right, out float4 Projection, out float4 InverseProjection, out float4 View, out float4 InverseView, out float4 ViewProjection, out float4 InverseViewProjection)
{
    // Placeholder: Requires Unity camera data
    Position = GetCameraPosition();
    Direction = GetCameraLookAt();
    Up = GetCameraUp();
    Right = (float3){1, 0, 0};
    Projection = (float4){1, 0, 0, 0};
    InverseProjection = (float4){1, 0, 0, 0};
    View = (float4){1, 0, 0, 0};
    InverseView = (float4){1, 0, 0, 0};
    ViewProjection = (float4){1, 0, 0, 0};
    InverseViewProjection = (float4){1, 0, 0, 0};
}

void Unity_ObjectToWorld_float(float3 Position, out float3 Out)
{
    // Placeholder: Requires Unity transform
    Out = Position;
}

void Unity_WorldToObject_float(float3 Position, out float3 Out)
{
    // Placeholder: Requires Unity transform
    Out = Position;
}

void Unity_ViewDirection_float(float3 Position, out float3 Out)
{
    // Placeholder: Requires camera data
    Out = (float3){0, 0, 1};
}

void Unity_NormalVector_float(float3 Normal, out float3 Out)
{
    Out = Normal;
}

void Unity_TangentVector_float(float3 Tangent, out float3 Out)
{
    Out = Tangent;
}

void Unity_BitangentVector_float(float3 Bitangent, out float3 Out)
{
    Out = Bitangent;
}

void Unity_Position_float(float3 Position, out float3 Out)
{
    Out = Position;
}

void Unity_ScreenPosition_float(float4 Position, out float4 Out)
{
    // Placeholder: Requires screen space
    Out = Position;
}

void Unity_UV_float(float2 UV, out float2 Out)
{
    Out = UV;
}

void Unity_VertexColor_float(float4 Color, out float4 Out)
{
    Out = Color;
}

void Unity_VertexID_float(out float Out)
{
    // Placeholder: Requires vertex data
    Out = 0.0f;
}

void Unity_InstanceID_float(out float Out)
{
    // Placeholder: Requires instance data
    Out = 0.0f;
}

void Unity_FaceSign_float(out float Out)
{
    // Placeholder: Requires face data
    Out = 1.0f;
}

// Additional Math functions

void Unity_All_float(float Predicate, out float Out)
{
    Out = Predicate;
}

void Unity_All_float2(float2 Predicate, out float Out)
{
    Out = (Predicate.x != 0 && Predicate.y != 0) ? 1.0f : 0.0f;
}

void Unity_All_float3(float3 Predicate, out float Out)
{
    Out = (Predicate.x != 0 && Predicate.y != 0 && Predicate.z != 0) ? 1.0f : 0.0f;
}

void Unity_All_float4(float4 Predicate, out float Out)
{
    Out = (Predicate.x != 0 && Predicate.y != 0 && Predicate.z != 0 && Predicate.w != 0) ? 1.0f : 0.0f;
}

void Unity_Any_float(float Predicate, out float Out)
{
    Out = Predicate;
}

void Unity_Any_float2(float2 Predicate, out float Out)
{
    Out = (Predicate.x != 0 || Predicate.y != 0) ? 1.0f : 0.0f;
}

void Unity_Any_float3(float3 Predicate, out float Out)
{
    Out = (Predicate.x != 0 || Predicate.y != 0 || Predicate.z != 0) ? 1.0f : 0.0f;
}

void Unity_Any_float4(float4 Predicate, out float Out)
{
    Out = (Predicate.x != 0 || Predicate.y != 0 || Predicate.z != 0 || Predicate.w != 0) ? 1.0f : 0.0f;
}

void Unity_IsNaN_float(float In, out float Out)
{
    Out = isnan(In) ? 1.0f : 0.0f;
}

void Unity_IsNaN_float2(float2 In, out float2 Out)
{
    Out.x = isnan(In.x) ? 1.0f : 0.0f;
    Out.y = isnan(In.y) ? 1.0f : 0.0f;
}

void Unity_IsNaN_float3(float3 In, out float3 Out)
{
    Out.x = isnan(In.x) ? 1.0f : 0.0f;
    Out.y = isnan(In.y) ? 1.0f : 0.0f;
    Out.z = isnan(In.z) ? 1.0f : 0.0f;
}

void Unity_IsNaN_float4(float4 In, out float4 Out)
{
    Out.x = isnan(In.x) ? 1.0f : 0.0f;
    Out.y = isnan(In.y) ? 1.0f : 0.0f;
    Out.z = isnan(In.z) ? 1.0f : 0.0f;
    Out.w = isnan(In.w) ? 1.0f : 0.0f;
}

void Unity_IsInfinite_float(float In, out float Out)
{
    Out = isinf(In) ? 1.0f : 0.0f;
}

void Unity_IsInfinite_float2(float2 In, out float2 Out)
{
    Out.x = isinf(In.x) ? 1.0f : 0.0f;
    Out.y = isinf(In.y) ? 1.0f : 0.0f;
}

void Unity_IsInfinite_float3(float3 In, out float3 Out)
{
    Out.x = isinf(In.x) ? 1.0f : 0.0f;
    Out.y = isinf(In.y) ? 1.0f : 0.0f;
    Out.z = isinf(In.z) ? 1.0f : 0.0f;
}

void Unity_IsInfinite_float4(float4 In, out float4 Out)
{
    Out.x = isinf(In.x) ? 1.0f : 0.0f;
    Out.y = isinf(In.y) ? 1.0f : 0.0f;
    Out.z = isinf(In.z) ? 1.0f : 0.0f;
    Out.w = isinf(In.w) ? 1.0f : 0.0f;
}

void Unity_Comparison_float(float A, float B, out float Out)
{
    Out = (A == B) ? 1.0f : 0.0f;
}

void Unity_Comparison_float2(float2 A, float2 B, out float2 Out)
{
    Out.x = (A.x == B.x) ? 1.0f : 0.0f;
    Out.y = (A.y == B.y) ? 1.0f : 0.0f;
}

void Unity_Comparison_float3(float3 A, float3 B, out float3 Out)
{
    Out.x = (A.x == B.x) ? 1.0f : 0.0f;
    Out.y = (A.y == B.y) ? 1.0f : 0.0f;
    Out.z = (A.z == B.z) ? 1.0f : 0.0f;
}

void Unity_Comparison_float4(float4 A, float4 B, out float4 Out)
{
    Out.x = (A.x == B.x) ? 1.0f : 0.0f;
    Out.y = (A.y == B.y) ? 1.0f : 0.0f;
    Out.z = (A.z == B.z) ? 1.0f : 0.0f;
    Out.w = (A.w == B.w) ? 1.0f : 0.0f;
}

void Unity_Arctangent2_float(float Y, float X, out float Out)
{
    Out = atan2f(Y, X);
}

void Unity_Cosine_float(float In, out float Out)
{
    Out = cosf(In);
}

void Unity_Sine_float(float In, out float Out)
{
    Out = sinf(In);
}

void Unity_Tangent_float(float In, out float Out)
{
    Out = tanf(In);
}

void Unity_HyperbolicCosine_float(float In, out float Out)
{
    Out = coshf(In);
}

void Unity_HyperbolicSine_float(float In, out float Out)
{
    Out = sinhf(In);
}

void Unity_HyperbolicTangent_float(float In, out float Out)
{
    Out = tanhf(In);
}

void Unity_DegreesToRadians_float(float In, out float Out)
{
    Out = In * M_DEG_TO_RAD;
}

void Unity_RadiansToDegrees_float(float In, out float Out)
{
    Out = In * M_RAD_TO_DEG;
}

// Additional Procedural functions

void Unity_Noise_float(float2 UV, float Scale, out float Out)
{
    // Placeholder: Simple noise implementation
    Out = sinf(UV.x * Scale) * cosf(UV.y * Scale);
}

void Unity_Noise_float3(float3 Position, float Scale, out float Out)
{
    // Placeholder
    Out = sinf(Position.x * Scale) * cosf(Position.y * Scale) * sinf(Position.z * Scale);
}

void Unity_Noise_float4(float4 Position, float Scale, out float Out)
{
    // Placeholder
    Out = sinf(Position.x * Scale) * cosf(Position.y * Scale) * sinf(Position.z * Scale) * cosf(Position.w * Scale);
}

// Additional UV functions

void Unity_PolarCoordinates_float(float2 UV, float2 Center, float RadialScale, float LengthScale, out float2 Out)
{
    float2 delta = {UV.x - Center.x, UV.y - Center.y};
    float radius = M_LENGHT2(delta) * RadialScale;
    float angle = atan2f(delta.y, delta.x) * LengthScale;
    Out.x = radius;
    Out.y = angle;
}

void Unity_RadialShear_float(float2 UV, float2 Center, float Strength, float2 Offset, out float2 Out)
{
    float2 delta = {UV.x - Center.x, UV.y - Center.y};
    float angle = atan2f(delta.y, delta.x);
    float radius = M_LENGHT2(delta);
    float shear = Strength * radius;
    float newAngle = angle + shear;
    Out.x = Center.x + cosf(newAngle) * radius + Offset.x;
    Out.y = Center.y + sinf(newAngle) * radius + Offset.y;
}

void Unity_RadialZoom_float(float2 UV, float2 Center, float Zoom, float2 Offset, out float2 Out)
{
    float2 delta = {UV.x - Center.x, UV.y - Center.y};
    float radius = M_LENGHT2(delta);
    float angle = atan2f(delta.y, delta.x);
    float newRadius = radius * Zoom;
    Out.x = Center.x + cosf(angle) * newRadius + Offset.x;
    Out.y = Center.y + sinf(angle) * newRadius + Offset.y;
}

// Additional Utility functions

void Unity_SceneColor_float(float4 UV, out float3 Out)
{
    // Unimplementable: Requires scene color access
    Out = (float3){0.5f, 0.5f, 0.5f};
}

void Unity_SceneDepth_float(float4 UV, out float Out)
{
    // Unimplementable: Requires depth buffer
    Out = 0.5f;
}

void Unity_SceneDepth_Raw_float(float4 UV, out float Out)
{
    // Unimplementable: Requires depth buffer
    Out = 0.5f;
}

void Unity_ScreenParams_float(out float4 Out)
{
    // Placeholder: Screen parameters
    Out = (float4){320, 240, 1.0f/320, 1.0f/240};
}

void Unity_ZBufferParams_float(out float4 Out)
{
    // Placeholder: Z buffer parameters
    Out = (float4){1, 0, 0, 0};
}

void Unity_ProjectionParams_float(out float4 Out)
{
    // Placeholder: Projection parameters
    Out = (float4){1, 0, 0, 0};
}

void Unity_CameraProjection_float(out float4x4 Out)
{
    // Placeholder
    m_mat4_identity((float*)&Out);
}

void Unity_CameraInvProjection_float(out float4x4 Out)
{
    // Placeholder
    m_mat4_identity((float*)&Out);
}

void Unity_CameraView_float(out float4x4 Out)
{
    // Placeholder
    m_mat4_identity((float*)&Out);
}

void Unity_CameraInvView_float(out float4x4 Out)
{
    // Placeholder
    m_mat4_identity((float*)&Out);
}

void Unity_CameraViewProjection_float(out float4x4 Out)
{
    // Placeholder
    m_mat4_identity((float*)&Out);
}

void Unity_CameraInvViewProjection_float(out float4x4 Out)
{
    // Placeholder
    m_mat4_identity((float*)&Out);
}

void Unity_ObjectToWorld_float(out float4x4 Out)
{
    // Placeholder
    m_mat4_identity((float*)&Out);
}

void Unity_WorldToObject_float(out float4x4 Out)
{
    // Placeholder
    m_mat4_identity((float*)&Out);
}

void Unity_AbsoluteWorldSpacePosition_float(out float3 Out)
{
    // Placeholder
    Out = (float3){0, 0, 0};
}

void Unity_RelativeWorldSpacePosition_float(out float3 Out)
{
    // Placeholder
    Out = (float3){0, 0, 0};
}

void Unity_AbsoluteWorldSpaceViewDirection_float(out float3 Out)
{
    // Placeholder
    Out = (float3){0, 0, 1};
}

void Unity_RelativeWorldSpaceViewDirection_float(out float3 Out)
{
    // Placeholder
    Out = (float3){0, 0, 1};
}

void Unity_WorldSpaceNormal_float(float3 Normal, out float3 Out)
{
    Out = Normal;
}

void Unity_ObjectSpacePosition_float(out float3 Out)
{
    // Placeholder
    Out = (float3){0, 0, 0};
}

void Unity_ObjectSpaceNormal_float(out float3 Out)
{
    // Placeholder
    Out = (float3){0, 1, 0};
}

void Unity_ObjectSpaceTangent_float(out float3 Out)
{
    // Placeholder
    Out = (float3){1, 0, 0};
}

void Unity_ObjectSpaceBitangent_float(out float3 Out)
{
    // Placeholder
    Out = (float3){0, 0, 1};
}

void Unity_ObjectSpaceViewDirection_float(out float3 Out)
{
    // Placeholder
    Out = (float3){0, 0, 1};
}

void Unity_TangentSpaceNormal_float(out float3 Out)
{
    // Placeholder
    Out = (float3){0, 0, 1};
}

void Unity_TangentSpaceTangent_float(out float3 Out)
{
    // Placeholder
    Out = (float3){1, 0, 0};
}

void Unity_TangentSpaceBitangent_float(out float3 Out)
{
    // Placeholder
    Out = (float3){0, 1, 0};
}

void Unity_TangentSpaceViewDirection_float(out float3 Out)
{
    // Placeholder
    Out = (float3){0, 0, 1};
}

void Unity_TangentSpaceLightDirection_float(out float3 Out)
{
    // Placeholder
    Out = (float3){0, 0, 1};
}

void Unity_TangentSpaceReflection_float(out float3 Out)
{
    // Placeholder
    Out = (float3){0, 0, 1};
}

void Unity_WorldSpaceReflection_float(out float3 Out)
{
    // Placeholder
    Out = (float3){0, 0, 1};
}

void Unity_ObjectSpaceReflection_float(out float3 Out)
{
    // Placeholder
    Out = (float3){0, 0, 1};
}

void Unity_TangentSpaceReflection_float3(float3 ViewDir, float3 Normal, out float3 Out)
{
    float3 reflectDir = {ViewDir.x - 2 * M_DOT3(ViewDir, Normal) * Normal.x,
                         ViewDir.y - 2 * M_DOT3(ViewDir, Normal) * Normal.y,
                         ViewDir.z - 2 * M_DOT3(ViewDir, Normal) * Normal.z};
    M_NORMALIZE3(Out, reflectDir);
}

void Unity_WorldSpaceReflection_float3(float3 ViewDir, float3 Normal, out float3 Out)
{
    float3 reflectDir = {ViewDir.x - 2 * M_DOT3(ViewDir, Normal) * Normal.x,
                         ViewDir.y - 2 * M_DOT3(ViewDir, Normal) * Normal.y,
                         ViewDir.z - 2 * M_DOT3(ViewDir, Normal) * Normal.z};
    M_NORMALIZE3(Out, reflectDir);
}

void Unity_ObjectSpaceReflection_float3(float3 ViewDir, float3 Normal, out float3 Out)
{
    float3 reflectDir = {ViewDir.x - 2 * M_DOT3(ViewDir, Normal) * Normal.x,
                         ViewDir.y - 2 * M_DOT3(ViewDir, Normal) * Normal.y,
                         ViewDir.z - 2 * M_DOT3(ViewDir, Normal) * Normal.z};
    M_NORMALIZE3(Out, reflectDir);
}

void Unity_Refraction_float(float3 ViewDir, float3 Normal, float IOR, out float3 Out)
{
    // Simplified refraction
    float eta = 1.0f / IOR;
    float cosTheta = M_DOT3(ViewDir, Normal);
    float k = 1.0f - eta * eta * (1.0f - cosTheta * cosTheta);
    if (k < 0) {
        Out = (float3){0, 0, 0}; // Total internal reflection
    } else {
        Out.x = eta * ViewDir.x - (eta * cosTheta + sqrtf(k)) * Normal.x;
        Out.y = eta * ViewDir.y - (eta * cosTheta + sqrtf(k)) * Normal.y;
        Out.z = eta * ViewDir.z - (eta * cosTheta + sqrtf(k)) * Normal.z;
    }
}

void Unity_FresnelEffect_float(float3 Normal, float3 ViewDir, float Power, out float Out)
{
    float NdotV = M_MAX(M_DOT3(Normal, ViewDir), 0.0f);
    Out = powf(1.0f - NdotV, Power);
}

void Unity_FresnelEffect_float3(float3 Normal, float3 ViewDir, float Power, out float3 Out)
{
    float NdotV = M_MAX(M_DOT3(Normal, ViewDir), 0.0f);
    float fresnel = powf(1.0f - NdotV, Power);
    Out = (float3){fresnel, fresnel, fresnel};
}

void Unity_ReflectionProbe_float(float3 Position, float3 Normal, float LOD, out float3 Out)
{
    // Unimplementable: Requires reflection probe
    Out = (float3){0.5f, 0.5f, 0.5f};
}

void Unity_ReflectionProbeNode_float(float3 ViewDir, float3 Normal, float LOD, out float3 Out)
{
    // Unimplementable: Requires reflection probe
    Out = (float3){0.5f, 0.5f, 0.5f};
}

void Unity_SampleReflectionProbe_float(float3 Position, float3 Normal, float LOD, out float3 Out)
{
    // Unimplementable: Requires reflection probe
    Out = (float3){0.5f, 0.5f, 0.5f};
}

void Unity_SampleReflectionProbeNode_float(float3 ViewDir, float3 Normal, float LOD, out float3 Out)
{
    // Unimplementable: Requires reflection probe
    Out = (float3){0.5f, 0.5f, 0.5f};
}

void Unity_LightColor_float(out float3 Out)
{
    // Placeholder: Light color
    Out = (float3){1, 1, 1};
}

void Unity_LightDirection_float(out float3 Out)
{
    // Placeholder: Light direction
    Out = (float3){0, 0, 1};
}

void Unity_LightAttenuation_float(out float Out)
{
    // Placeholder: Light attenuation
    Out = 1.0f;
}

void Unity_Ambient_float(out float3 Out)
{
    // Placeholder: Ambient light
    Out = (float3){0.2f, 0.2f, 0.2f};
}

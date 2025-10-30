// Artistic

void Unity_ChannelMixer_float(float3 In, float3 _ChannelMixer_Red, float3 _ChannelMixer_Green, float3 _ChannelMixer_Blue, out float3 Out)
{
    Out = float3(dot(In, _ChannelMixer_Red), dot(In, _ChannelMixer_Green), dot(In, _ChannelMixer_Blue));
}
void Unity_Contrast_float(float3 In, float Contrast, out float3 Out)
{
    float midpoint = pow(0.5, 2.2);
    Out = (In - midpoint) * Contrast + midpoint;
}
void Unity_Hue_Degrees_float(float3 In, float Offset, out float3 Out)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 P = lerp(float4(In.bg, K.wz), float4(In.gb, K.xy), step(In.b, In.g));
    float4 Q = lerp(float4(P.xyw, In.r), float4(In.r, P.yzx), step(P.x, In.r));
    float D = Q.x - min(Q.w, Q.y);
    float E = 1e-10;
    float3 hsv = float3(abs(Q.z + (Q.w - Q.y)/(6.0 * D + E)), D / (Q.x + E), Q.x);

    float hue = hsv.x + Offset / 360;
    hsv.x = (hue < 0)
            ? hue + 1
            : (hue > 1)
                ? hue - 1
                : hue;

    float4 K2 = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 P2 = abs(frac(hsv.xxx + K2.xyz) * 6.0 - K2.www);
    Out = hsv.z * lerp(K2.xxx, saturate(P2 - K2.xxx), hsv.y);
}
void Unity_Hue_Radians_float(float3 In, float Offset, out float3 Out)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 P = lerp(float4(In.bg, K.wz), float4(In.gb, K.xy), step(In.b, In.g));
    float4 Q = lerp(float4(P.xyw, In.r), float4(In.r, P.yzx), step(P.x, In.r));
    float D = Q.x - min(Q.w, Q.y);
    float E = 1e-10;
    float3 hsv = float3(abs(Q.z + (Q.w - Q.y)/(6.0 * D + E)), D / (Q.x + E), Q.x);

    float hue = hsv.x + Offset;
    hsv.x = (hue < 0)
            ? hue + 1
            : (hue > 1)
                ? hue - 1
                : hue;

    // HSV to RGB
    float4 K2 = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 P2 = abs(frac(hsv.xxx + K2.xyz) * 6.0 - K2.www);
    Out = hsv.z * lerp(K2.xxx, saturate(P2 - K2.xxx), hsv.y);
}
void Unity_InvertColors_float4(float4 In, float4 InvertColors, out float4 Out)
{
    Out = abs(InvertColors - In);
}
void Unity_ReplaceColor_float(float3 In, float3 From, float3 To, float Range, float Fuzziness, out float3 Out)
{
    float Distance = distance(From, In);
    Out = lerp(To, In, saturate((Distance - Range) / max(Fuzziness, e-f)));
}
void Unity_Saturation_float(float3 In, float Saturation, out float3 Out)
{
    float luma = dot(In, float3(0.2126729, 0.7151522, 0.0721750));
    Out =  luma.xxx + Saturation.xxx * (In - luma.xxx);
}
void Unity_WhiteBalance_float(float3 In, float Temperature, float Tint, out float3 Out)
{
    // Range ~[-1.67;1.67] works best
    float t1 = Temperature * 10 / 6;
    float t2 = Tint * 10 / 6;

    // Get the CIE xy chromaticity of the reference white point.
    // Note: 0.31271 = x value on the D65 white point
    float x = 0.31271 - t1 * (t1 < 0 ? 0.1 : 0.05);
    float standardIlluminantY = 2.87 * x - 3 * x * x - 0.27509507;
    float y = standardIlluminantY + t2 * 0.05;

    // Calculate the coefficients in the LMS space.
    float3 w1 = float3(0.949237, 1.03542, 1.08728); // D65 white point

    // CIExyToLMS
    float Y = 1;
    float X = Y * x / y;
    float Z = Y * (1 - x - y) / y;
    float L = 0.7328 * X + 0.4296 * Y - 0.1624 * Z;
    float M = -0.7036 * X + 1.6975 * Y + 0.0061 * Z;
    float S = 0.0030 * X + 0.0136 * Y + 0.9834 * Z;
    float3 w2 = float3(L, M, S);

    float3 balance = float3(w1.x / w2.x, w1.y / w2.y, w1.z / w2.z);

    float3x3 LIN_2_LMS_MAT = {
        3.90405e-1, 5.49941e-1, 8.92632e-3,
        7.08416e-2, 9.63172e-1, 1.35775e-3,
        2.31082e-2, 1.28021e-1, 9.36245e-1
    };

    float3x3 LMS_2_LIN_MAT = {
        2.85847e+0, -1.62879e+0, -2.48910e-2,
        -2.10182e-1,  1.15820e+0,  3.24281e-4,
        -4.18120e-2, -1.18169e-1,  1.06867e+0
    };

    float3 lms = mul(LIN_2_LMS_MAT, In);
    lms *= balance;
    Out = mul(LMS_2_LIN_MAT, lms);
}

void Unity_Blend_Burn_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    Out =  1.0 - (1.0 - Blend)/Base;
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_Darken_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    Out = min(Blend, Base);
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_Difference_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    Out = abs(Blend - Base);
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_Dodge_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    Out = Base / (1.0 - Blend);
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_Divide_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    Out = Base / (Blend + 0.000000000001);
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_Exclusion_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    Out = Blend + Base - (2.0 * Blend * Base);
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_HardLight_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    float4 result1 = 1.0 - 2.0 * (1.0 - Base) * (1.0 - Blend);
    float4 result2 = 2.0 * Base * Blend;
    float4 zeroOrOne = step(Blend, 0.5);
    Out = result2 * zeroOrOne + (1 - zeroOrOne) * result1;
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_HardMix_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    Out = step(1 - Base, Blend);
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_Lighten_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    Out = max(Blend, Base);
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_LinearBurn_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    Out = Base + Blend - 1.0;
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_LinearDodge_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    Out = Base + Blend;
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_LinearLight_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    Out = Blend < 0.5 ? max(Base + (2 * Blend) - 1, 0) : min(Base + 2 * (Blend - 0.5), 1);
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_LinearLightAddSub_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    Out = Blend + 2.0 * Base - 1.0;
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_Multiply_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    Out = Base * Blend;
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_Negation_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    Out = 1.0 - abs(1.0 - Blend - Base);
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_Overlay_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    float4 result1 = 1.0 - 2.0 * (1.0 - Base) * (1.0 - Blend);
    float4 result2 = 2.0 * Base * Blend;
    float4 zeroOrOne = step(Base, 0.5);
    Out = result2 * zeroOrOne + (1 - zeroOrOne) * result1;
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_PinLight_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    float4 check = step (0.5, Blend);
    float4 result1 = check * max(2.0 * (Base - 0.5), Blend);
    Out = result1 + (1.0 - check) * min(2.0 * Base, Blend);
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_Screen_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    Out = 1.0 - (1.0 - Blend) * (1.0 - Base);
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_SoftLight_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    float4 result1 = 2.0 * Base * Blend + Base * Base * (1.0 - 2.0 * Blend);
    float4 result2 = sqrt(Base) * (2.0 * Blend - 1.0) + 2.0 * Base * (1.0 - Blend);
    float4 zeroOrOne = step(0.5, Blend);
    Out = result2 * zeroOrOne + (1 - zeroOrOne) * result1;
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_Subtract_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    Out = Base - Blend;
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_VividLight_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    float4 result1 = 1.0 - (1.0 - Blend) / (2.0 * Base);
    float4 result2 = Blend / (2.0 * (1.0 - Base));
    float4 zeroOrOne = step(0.5, Base);
    Out = result2 * zeroOrOne + (1 - zeroOrOne) * result1;
    Out = lerp(Base, Out, Opacity);
}
void Unity_Blend_Overwrite_float4(float4 Base, float4 Blend, float Opacity, out float4 Out)
{
    Out = lerp(Base, Blend, Opacity);
}
void Unity_Dither_float4(float4 In, float4 ScreenPosition, out float4 Out)
{
    float2 uv = ScreenPosition.xy * _ScreenParams.xy;
    float DITHER_THRESHOLDS[16] =
    {
        1.0 / 17.0,  9.0 / 17.0,  3.0 / 17.0, 11.0 / 17.0,
        13.0 / 17.0,  5.0 / 17.0, 15.0 / 17.0,  7.0 / 17.0,
        4.0 / 17.0, 12.0 / 17.0,  2.0 / 17.0, 10.0 / 17.0,
        16.0 / 17.0,  8.0 / 17.0, 14.0 / 17.0,  6.0 / 17.0
    };
    uint index = (uint(uv.x) % 4) * 4 + uint(uv.y) % 4;
    Out = In - DITHER_THRESHOLDS[index];
}
void Unity_ChannelMask_RedGreen_float4(float4 In, out float4 Out)
{
    Out = float4(0, 0, In.b, In.a);
}
void Unity_ColorMask_float(float3 In, float3 MaskColor, float Range, float Fuzziness, out float4 Out)
{
    float Distance = distance(MaskColor, In);
    Out = saturate(1 - (Distance - Range) / max(Fuzziness, 1e-5));
}
void Unity_NormalBlend_float(float3 A, float3 B, out float3 Out)
{
    Out = normalize(float3(A.rg + B.rg, A.b * B.b));
}
void Unity_NormalFromHeight_Tangent_float(float In, float Strength, float3 Position, float3x3 TangentMatrix, out float3 Out)
{
    float3 worldDerivativeX = ddx(Position);
    float3 worldDerivativeY = ddy(Position);

    float3 crossX = cross(TangentMatrix[2].xyz, worldDerivativeX);
    float3 crossY = cross(worldDerivativeY, TangentMatrix[2].xyz);
    float d = dot(worldDerivativeX, crossY);
    float sgn = d < 0.0 ? (-1.f) : 1.f;
    float surface = sgn / max(0.00000000000001192093f, abs(d));

    float dHdx = ddx(In);
    float dHdy = ddy(In);
    float3 surfGrad = surface * (dHdx*crossY + dHdy*crossX);
    Out = normalize(TangentMatrix[2].xyz - (Strength * surfGrad));
    Out = TransformWorldToTangent(Out, TangentMatrix);
}
void Unity_NormalFromHeight_World_float(float In, float Strength, float3 Position, float3x3 TangentMatrix, out float3 Out)
{
    float3 worldDerivativeX = ddx(Position);
    float3 worldDerivativeY = ddy(Position);

    float3 crossX = cross(TangentMatrix[2].xyz, worldDerivativeX);
    float3 crossY = cross(worldDerivativeY, TangentMatrix[2].xyz);
    float d = dot(worldDerivativeX, crossY);
    float sgn = d < 0.0 ? (-1.f) : 1.f;
    float surface = sgn / max(0.00000000000001192093f, abs(d));

    float dHdx = ddx(In);
    float dHdy = ddy(In);
    float3 surfGrad = surface * (dHdx*crossY + dHdy*crossX);
    Out = normalize(TangentMatrix[2].xyz - (Strength * surfGrad));
}
void Unity_NormalFromTexture_float(Texture texture, SamplerState Sampler, float2 UV, float Offset, float Strength, out float3 Out)
{
    Offset = pow(Offset, 3) * 0.1;
    float2 offsetU = float2(UV.x + Offset, UV.y);
    float2 offsetV = float2(UV.x, UV.y + Offset);
    float normalSample = Texture.Sample(Sampler, UV);
    float uSample = Texture.Sample(Sampler, offsetU);
    float vSample = Texture.Sample(Sampler, offsetV);
    float3 va = float3(1, 0, (uSample - normalSample) * Strength);
    float3 vb = float3(0, 1, (vSample - normalSample) * Strength);
    Out = normalize(cross(va, vb));
}
void Unity_NormalReconstructZ_float(float2 In, out float3 Out)
{
    float reconstructZ = sqrt(1.0 - saturate(dot(In.xy, In.xy)));
    float3 normalVector = float3(In.x, In.y, reconstructZ);
    Out = normalize(normalVector);
}
void Unity_NormalStrength_float(float3 In, float Strength, out float3 Out)
{
    Out = {precision}3(In.rg * Strength, lerp(1, In.b, saturate(Strength)));
}
void Unity_NormalUnpack_float(float4 In, out float3 Out)
{
    Out = UnpackNormalmapRGorAG(In);
}
void Unity_NormalUnpackRGB_float(float4 In, out float3 Out)
{
    Out = UnpackNormalmapRGB(In);
}
void Unity_ColorspaceConversion_RGB_RGB_float(float3 In, out float3 Out)
{
    Out =  In;
}
void Unity_ColorspaceConversion_RGB_RGB_float(float3 In, out float3 Out)
{
    float3 linearRGBLo = In / 12.92;;
    float3 linearRGBHi = pow(max(abs((In + 0.055) / 1.055), 1.192092896e-07), float3(2.4, 2.4, 2.4));
    Out = float3(In <= 0.04045) ? linearRGBLo : linearRGBHi;
}
void Unity_ColorspaceConversion_RGB_RGB_float(float3 In, out float3 Out)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 P = lerp(float4(In.bg, K.wz), float4(In.gb, K.xy), step(In.b, In.g));
    float4 Q = lerp(float4(P.xyw, In.r), float4(In.r, P.yzx), step(P.x, In.r));
    float D = Q.x - min(Q.w, Q.y);
    float  E = 1e-10;
    Out = float3(abs(Q.z + (Q.w - Q.y)/(6.0 * D + E)), D / (Q.x + E), Q.x);
}
void Unity_ColorspaceConversion_RGB_RGB_float(float3 In, out float3 Out)
{
    float3 sRGBLo = In * 12.92;
    float3 sRGBHi = (pow(max(abs(In), 1.192092896e-07), float3(1.0 / 2.4, 1.0 / 2.4, 1.0 / 2.4)) * 1.055) - 0.055;
    Out = float3(In <= 0.0031308) ? sRGBLo : sRGBHi;
}
void Unity_ColorspaceConversion_RGB_RGB_float(float3 In, out float3 Out)
{
    Out = In;
}
void Unity_ColorspaceConversion_RGB_RGB_float(float3 In, out float3 Out)
{
    float3 sRGBLo = In * 12.92;
    float3 sRGBHi = (pow(max(abs(In), 1.192092896e-07), float3(1.0 / 2.4, 1.0 / 2.4, 1.0 / 2.4)) * 1.055) - 0.055;
    float3 Linear = float3(In <= 0.0031308) ? sRGBLo : sRGBHi;
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 P = lerp(float4(Linear.bg, K.wz), float4(Linear.gb, K.xy), step(Linear.b, Linear.g));
    float4 Q = lerp(float4(P.xyw, Linear.r), float4(Linear.r, P.yzx), step(P.x, Linear.r));
    float D = Q.x - min(Q.w, Q.y);
    float  E = 1e-10;
    Out = float3(abs(Q.z + (Q.w - Q.y)/(6.0 * D + E)), D / (Q.x + E), Q.x);
}
void Unity_ColorspaceConversion_RGB_RGB_float(float3 In, out float3 Out)
{
    float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 P = abs(frac(In.xxx + K.xyz) * 6.0 - K.www);
    Out = In.z * lerp(K.xxx, saturate(P - K.xxx), In.y);
}
void Unity_ColorspaceConversion_RGB_RGB_float(float3 In, out float3 Out)
{
    float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 P = abs(frac(In.xxx + K.xyz) * 6.0 - K.www);
    float3 RGB = In.z * lerp(K.xxx, saturate(P - K.xxx), In.y);
    float3 linearRGBLo = RGB / 12.92;
    float3 linearRGBHi = pow(max(abs((RGB + 0.055) / 1.055), 1.192092896e-07), float3(2.4, 2.4, 2.4));
    Out = float3(RGB <= 0.04045) ? linearRGBLo : linearRGBHi;
}
void Unity_ColorspaceConversion_RGB_RGB_float(float3 In, out float3 Out)
{
    Out = In;
}

//Channel

void Unity_Combine_float(float R, float G, float B, float A, out float4 RGBA, out float3 RGB, out float2 RG)
{
    RGBA = float4(R, G, B, A);
    RGB = float3(R, G, B);
    RG = float2(R, G);
}

void Unity_Flip_float4(float4 In, float4 Flip, out float4 Out)
{
    Out = (Flip * -2 + 1) * In;
}

// Input

void Unity_Time_float(out float Time, out float SineTime, out float CosineTime, out float DeltaTime, out float SmoothDeltaTime)
{
    Time = _Time.y;
    SineTime = _SinTime.w;
    CosineTime = _CosTime.w;
    DeltaTime = unity_DeltaTime.x;
    SmoothDeltaTime = unity_DeltaTime.z;
}

void Unity_Vector1_float(float In, out float Out)
{
    Out = In;
}

void Unity_Vector2_float(float2 In, out float2 Out)
{
    Out = In;
}

void Unity_Vector3_float(float3 In, out float3 Out)
{
    Out = In;
}

void Unity_Vector4_float(float4 In, out float4 Out)
{
    Out = In;
}

void Unity_Matrix2x2_float(float2x2 In, out float2x2 Out)
{
    Out = In;
}

void Unity_Matrix3x3_float(float3x3 In, out float3x3 Out)
{
    Out = In;
}

void Unity_Matrix4x4_float(float4x4 In, out float4x4 Out)
{
    Out = In;
}

void Unity_Texture2D_float(Texture2D In, out Texture2D Out)
{
    Out = In;
}

void Unity_Texture2DArray_float(Texture2DArray In, out Texture2DArray Out)
{
    Out = In;
}

void Unity_Texture3D_float(Texture3D In, out Texture3D In)
{
    Out = In;
}

void Unity_Cubemap_float(Cubemap In, out Cubemap Out)
{
    Out = In;
}

void Unity_SamplerState_float(SamplerState In, out SamplerState Out)
{
    Out = Out;
}

void Unity_Constant_float(float In, out float Out)
{
    Out = In;
}

void Unity_Property_float(float In, out float Out)
{
    Out = In;
}

// Math

void Unity_Add_float(float A, float B, out float Out)
{
    Out = A + B;
}

void Unity_Add_float2(float2 A, float2 B, out float2 Out)
{
    Out = A + B;
}

void Unity_Add_float3(float3 A, float3 B, out float3 Out)
{
    Out = A + B;
}

void Unity_Add_float4(float4 A, float4 B, out float4 Out)
{
    Out = A + B;
}

void Unity_Subtract_float(float A, float B, out float Out)
{
    Out = A - B;
}

void Unity_Subtract_float2(float2 A, float2 B, out float2 Out)
{
    Out = A - B;
}

void Unity_Subtract_float3(float3 A, float3 B, out float3 Out)
{
    Out = A - B;
}

void Unity_Subtract_float4(float4 A, float4 B, out float4 Out)
{
    Out = A - B;
}

void Unity_Multiply_float(float A, float B, out float Out)
{
    Out = A * B;
}

void Unity_Multiply_float2(float2 A, float2 B, out float2 Out)
{
    Out = A * B;
}

void Unity_Multiply_float3(float3 A, float3 B, out float3 Out)
{
    Out = A * B;
}

void Unity_Multiply_float4(float4 A, float4 B, out float4 Out)
{
    Out = A * B;
}

void Unity_Divide_float(float A, float B, out float Out)
{
    Out = A / B;
}

void Unity_Divide_float2(float2 A, float2 B, out float2 Out)
{
    Out = A / B;
}

void Unity_Divide_float3(float3 A, float3 B, out float3 Out)
{
    Out = A / B;
}

void Unity_Divide_float4(float4 A, float4 B, out float4 Out)
{
    Out = A / B;
}

void Unity_Power_float(float A, float B, out float Out)
{
    Out = pow(A, B);
}

void Unity_SquareRoot_float(float In, out float Out)
{
    Out = sqrt(In);
}

void Unity_Log_float(float In, out float Out)
{
    Out = log(In);
}

void Unity_Exp_float(float In, out float Out)
{
    Out = exp(In);
}

void Unity_Absolute_float(float In, out float Out)
{
    Out = abs(In);
}

void Unity_Negate_float(float In, out float Out)
{
    Out = -In;
}

void Unity_Sign_float(float In, out float Out)
{
    Out = sign(In);
}

void Unity_Floor_float(float In, out float Out)
{
    Out = floor(In);
}

void Unity_Ceil_float(float In, out float Out)
{
    Out = ceil(In);
}

void Unity_Round_float(float In, out float Out)
{
    Out = round(In);
}

void Unity_Truncate_float(float In, out float Out)
{
    Out = trunc(In);
}

void Unity_Fraction_float(float In, out float Out)
{
    Out = frac(In);
}

void Unity_Modulo_float(float A, float B, out float Out)
{
    Out = fmod(A, B);
}

void Unity_Maximum_float(float A, float B, out float Out)
{
    Out = max(A, B);
}

void Unity_Minimum_float(float A, float B, out float Out)
{
    Out = min(A, B);
}

void Unity_Clamp_float(float In, float Min, float Max, out float Out)
{
    Out = clamp(In, Min, Max);
}

void Unity_Saturate_float(float In, out float Out)
{
    Out = saturate(In);
}

void Unity_Lerp_float(float A, float B, float T, out float Out)
{
    Out = lerp(A, B, T);
}

void Unity_Lerp_float2(float2 A, float2 B, float2 T, out float2 Out)
{
    Out = lerp(A, B, T);
}

void Unity_Lerp_float3(float3 A, float3 B, float3 T, out float3 Out)
{
    Out = lerp(A, B, T);
}

void Unity_Lerp_float4(float4 A, float4 B, float4 T, out float4 Out)
{
    Out = lerp(A, B, T);
}

void Unity_Smoothstep_float(float Edge1, float Edge2, float In, out float Out)
{
    Out = smoothstep(Edge1, Edge2, In);
}

void Unity_OneMinus_float(float In, out float Out)
{
    Out = 1 - In;
}

void Unity_Reciprocal_float(float In, out float Out)
{
    Out = 1 / In;
}

void Unity_DegreesToRadians_float(float In, out float Out)
{
    Out = radians(In);
}

void Unity_RadiansToDegrees_float(float In, out float Out)
{
    Out = degrees(In);
}

void Unity_Distance_float(float3 A, float3 B, out float Out)
{
    Out = distance(A, B);
}

void Unity_Length_float(float3 In, out float Out)
{
    Out = length(In);
}

void Unity_Normalize_float(float3 In, out float3 Out)
{
    Out = normalize(In);
}

void Unity_CrossProduct_float(float3 A, float3 B, out float3 Out)
{
    Out = cross(A, B);
}

void Unity_DotProduct_float(float3 A, float3 B, out float Out)
{
    Out = dot(A, B);
}

void Unity_MatrixConstruction_CameraProjection_float(out float4x4 Out)
{
    Out = UNITY_MATRIX_P;
}

void Unity_MatrixConstruction_ModelView_float(out float4x4 Out)
{
    Out = UNITY_MATRIX_MV;
}

void Unity_MatrixConstruction_ViewProjection_float(out float4x4 Out)
{
    Out = UNITY_MATRIX_VP;
}

void Unity_MatrixConstruction_WorldViewProjection_float(out float4x4 Out)
{
    Out = UNITY_MATRIX_MVP;
}

void Unity_MatrixConstruction_ObjectToWorld_float(out float4x4 Out)
{
    Out = unity_ObjectToWorld;
}

void Unity_MatrixConstruction_WorldToObject_float(out float4x4 Out)
{
    Out = unity_WorldToObject;
}

void Unity_MatrixConstruction_Transpose_float(float4x4 In, out float4x4 Out)
{
    Out = transpose(In);
}

void Unity_MatrixConstruction_Inverse_float(float4x4 In, out float4x4 Out)
{
    Out = inverse(In);
}

void Unity_MatrixMultiply_float(float4x4 A, float4x4 B, out float4x4 Out)
{
    Out = mul(A, B);
}

void Unity_MatrixMultiplyVector_float(float4x4 M, float4 V, out float4 Out)
{
    Out = mul(M, V);
}

void Unity_Blackbody_float(float Temperature, out float3 Out)
{
    float3 color = float3(255.0, 255.0, 255.0);
    color.x = 56100000. * pow(Temperature,(-3.0 / 2.0)) + 148.0;
    color.y = 100.04 * log(Temperature) - 623.6;
    if (Temperature > 6500.0) color.y = 35200000.0 * pow(Temperature,(-3.0 / 2.0)) + 184.0;
    color.z = 194.18 * log(Temperature) - 1448.6;
    color = clamp(color, 0.0, 255.0)/255.0;
    if (Temperature < 1000.0) color *= Temperature/1000.0;
    Out = color;
}

struct Gradient
{
    int type;
    int colorsLength;
    int alphasLength;
    float colors[8];
    float alphas[8];
};
Gradient Unity_Gradient_float()
{
    Gradient g;
    g.type = 1;
    g.colorsLength = 4;
    g.alphasLength = 4;
    g.colors[0] = 0.1;
    g.colors[1] = 0.2;
    g.colors[2] = 0.3;
    g.colors[3] = 0.4;
    g.colors[4] = 0;
    g.colors[5] = 0;
    g.colors[6] = 0;
    g.colors[7] = 0;
    g.alphas[0] = 0.1;
    g.alphas[1] = 0.2;
    g.alphas[2] = 0.3;
    g.alphas[3] = 0.4;
    g.alphas[4] = 0;
    g.alphas[5] = 0;
    g.alphas[6] = 0;
    g.alphas[7] = 0;
    return g;
}
void Unity_SampleGradient_float(float4 Gradient, float Time, out float4 Out)
{
    float3 color = Gradient.colors[0].rgb;
    [unroll]
    for (int c = 1; c < 8; c++)
    {
        float colorPos = saturate((Time - Gradient.colors[c-1].w) / (Gradient.colors[c].w - Gradient.colors[c-1].w)) * step(c, Gradient.colorsLength-1);
        color = lerp(color, Gradient.colors[c].rgb, lerp(colorPos, step(0.01, colorPos), Gradient.type));
    }
#ifndef UNITY_COLORSPACE_GAMMA
    color = SRGBToLinear(color);
#endif
    float alpha = Gradient.alphas[0].x;
    [unroll]
    for (int a = 1; a < 8; a++)
    {
        float alphaPos = saturate((Time - Gradient.alphas[a-1].y) / (Gradient.alphas[a].y - Gradient.alphas[a-1].y)) * step(a, Gradient.alphasLength-1);
        alpha = lerp(alpha, Gradient.alphas[a].x, lerp(alphaPos, step(0.01, alphaPos), Gradient.type));
    }
    Out = float4(color, alpha);
}



// Procedural

void Unity_Checkerboard_float(float2 UV, float3 ColorA, float3 ColorB, float2 Frequency, out float3 Out)
{
    UV = (UV.xy + 0.5) * Frequency;
    float4 derivatives = float4(ddx(UV), ddy(UV));
    float2 duv_length = sqrt(float2(dot(derivatives.xz, derivatives.xz), dot(derivatives.yw, derivatives.yw)));
    float width = 1.0;
    float2 distance3 = 4.0 * abs(frac(UV + 0.25) - 0.5) - width;
    float2 scale = 0.35 / duv_length.xy;
    float freqLimiter = sqrt(clamp(1.1f - max(duv_length.x, duv_length.y), 0.0, 1.0));
    float2 vector_angle = float2(angle(distance3.x), angle(distance3.y));
    float2 adjustScale = saturate((0.5 * width - distance3) / scale);
    float2 pattern = adjustScale.x * adjustScale.y;
    pattern *= freqLimiter;
    Out = lerp(ColorA, ColorB, pattern.x * pattern.y);
}

void Unity_GradientNoise_float(float2 UV, float Scale, out float Out)
{
    float2 p = UV * Scale;
    float2 ip = floor(p);
    float2 fp = frac(p);
    float d00 = dot(ip, float2(127.1, 311.7));
    float d01 = dot(ip + float2(0, 1), float2(127.1, 311.7));
    float d10 = dot(ip + float2(1, 0), float2(127.1, 311.7));
    float d11 = dot(ip + float2(1, 1), float2(127.1, 311.7));
    d00 = frac(sin(d00) * 43758.5453);
    d01 = frac(sin(d01) * 43758.5453);
    d10 = frac(sin(d10) * 43758.5453);
    d11 = frac(sin(d11) * 43758.5453);
    float2 t = smoothstep(0, 1, fp);
    Out = lerp(lerp(d00, d01, t.y), lerp(d10, d11, t.y), t.x);
}

void Unity_SimpleNoise_float(float2 UV, float Scale, out float Out)
{
    float t = 0.0;
    float2 p = UV * Scale;
    float2 ip = floor(p);
    float2 fp = frac(p);
    for (int i = -1; i <= 1; i++)
    for (int j = -1; j <= 1; j++)
    {
        float2 offset = float2(i, j);
        float2 o = ip + offset;
        float d = dot(o, float2(127.1, 311.7));
        float w = exp(-dot(fp - offset, fp - offset) * 4.0);
        t += w * frac(sin(d) * 43758.5453);
    }
    Out = t;
}

void Unity_Voronoi_float(float2 UV, float AngleOffset, float CellDensity, out float Out, out float Cells)
{
    float2 g = floor(UV * CellDensity);
    float2 f = frac(UV * CellDensity);
    float t = 8.0;
    float3 res = float3(8.0, 0.0, 0.0);

    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float2 lattice = float2(x, y);
            float2 offset = float2(
                sin((g + lattice) * AngleOffset + 0.5) * 0.5 + 0.5,
                cos((g + lattice) * AngleOffset + 0.5) * 0.5 + 0.5
            );
            float d = distance(lattice + offset, f);
            if (d < res.x)
            {
                res = float3(d, offset.x, offset.y);
                Out = res.x;
                Cells = res.y;
            }
        }
    }
}

// UV

void Unity_TilingAndOffset_float(float2 UV, float2 Tiling, float2 Offset, out float2 Out)
{
    Out = UV * Tiling + Offset;
}

void Unity_Triplanar_float(float3 Position, float3 Normal, float3 Scale, float3 Offset, float Blend, SamplerState Sampler, Texture2D Texture, out float4 Out)
{
    float3 blend = abs(Normal);
    blend = normalize(max(blend, 0.00001));
    blend /= dot(blend, 1.0);

    float2 uvX = Position.zy * Scale.xy + Offset.xy;
    float2 uvY = Position.xz * Scale.xy + Offset.xy;
    float2 uvZ = Position.xy * Scale.xy + Offset.xy;

    float4 x = SAMPLE_TEXTURE2D(Texture, Sampler, uvX);
    float4 y = SAMPLE_TEXTURE2D(Texture, Sampler, uvY);
    float4 z = SAMPLE_TEXTURE2D(Texture, Sampler, uvZ);

    Out = x * blend.x + y * blend.y + z * blend.z;
}

void Unity_Rotate_float(float2 UV, float2 Center, float Rotation, out float2 Out)
{
    UV -= Center;
    float s = sin(Rotation);
    float c = cos(Rotation);
    float2x2 rMatrix = float2x2(c, -s, s, c);
    rMatrix *= 0.5;
    rMatrix += 0.5;
    rMatrix = rMatrix * 2 - 1;
    UV = mul(UV, rMatrix);
    UV += Center;
    Out = UV;
}

void Unity_Spherize_float(float2 UV, float2 Center, float Strength, float2 Offset, out float2 Out)
{
    float2 delta = UV - Center;
    float delta2 = dot(delta.xy, delta.xy);
    float delta4 = delta2 * delta2;
    float2 delta_offset = delta4 * Strength;
    Out = UV + delta * delta_offset + Offset;
}

void Unity_Twirl_float(float2 UV, float2 Center, float Strength, float2 Offset, out float2 Out)
{
    float2 delta = UV - Center;
    float angle = Strength * length(delta);
    float x = cos(angle) * delta.x - sin(angle) * delta.y;
    float y = sin(angle) * delta.x + cos(angle) * delta.y;
    Out = float2(x, y) + Center + Offset;
}

// Utility

void Unity_Branch_float(float Predicate, float True, float False, out float Out)
{
    Out = Predicate ? True : False;
}

void Unity_Branch_float2(float Predicate, float2 True, float2 False, out float2 Out)
{
    Out = Predicate ? True : False;
}

void Unity_Branch_float3(float Predicate, float3 True, float3 False, out float3 Out)
{
    Out = Predicate ? True : False;
}

void Unity_Branch_float4(float Predicate, float4 True, float4 False, out float4 Out)
{
    Out = Predicate ? True : False;
}


void Unity_Preview_float(float In, out float Out)
{
    Out = In;
}

void Unity_Preview_float2(float2 In, out float2 Out)
{
    Out = In;
}

void Unity_Preview_float3(float3 In, out float3 Out)
{
    Out = In;
}

void Unity_Preview_float4(float4 In, out float4 Out)
{
    Out = In;
}
void Unity_SceneColor_float(float4 UV, out float3 Out) {
    
}
void Unity_SceneDepth_Raw_float(float4 UV, out float Out)
{

}
void SAMPLE_TEXTURE2D(Texture2D tex, SamplerState samp, float2 uv)
{
    return tex.Sample(samp, uv);
}

// Math
void Unity_Absolute_float4(float4 In, out float4 Out)
{
    Out = abs(In);
}
void Unity_Exponential_float4(float4 In, out float4 Out)
{
    Out = exp(In);
}
void Unity_Exponential2_float4(float4 In, out float4 Out)
{
    Out = exp2(In);
}
void Unity_Length_float4(float4 In, out float Out)
{
    Out = length(In);
}
void Unity_Log_float4(float4 In, out float4 Out)
{
    Out = log(In);
}
void Unity_Log2_float4(float4 In, out float4 Out)
{
    Out = log2(In);
}
void Unity_Log10_float4(float4 In, out float4 Out)
{
    Out = log10(In);
}
void Unity_Modulo_float4(float4 A, float4 B, out float4 Out)
{
    Out = fmod(A, B);
}
void Unity_Negate_float4(float4 In, out float4 Out)
{
    Out = -1 * In;
}
void Unity_Normalize_float4(float4 In, out float4 Out)
{
    Out = normalize(In);
}
void Unity_Posterize_float4(float4 In, float4 Steps, out float4 Out)
{
    Out = floor(In / (1 / Steps)) * (1 / Steps);
}
void Unity_Reciprocal_float4(float4 In, out float4 Out)
{
    Out = 1.0/In;
}
void Unity_ReciprocalSquareRoot_float4(float4 In, out float4 Out)
{
    Out = rsqrt(In);
}
void Unity_Add_float4(float4 A, float4 B, out float4 Out)
{
    Out = A + B;
}
void Unity_Divide_float4(float4 A, float4 B, out float4 Out)
{
    Out = A / B;
}
void Unity_Multiply_float4_float4(float4 A, float4 B, out float4 Out)
{
    Out = A * B;
}
void Unity_Multiply_float4_float4x4(float4 A, float4x4 B, out float4 Out)
{
    Out = mul(A, B);
}
void Unity_Multiply_float4x4_float4x4(float4x4 A, float4x4 B, out float4x4 Out)
{
    Out = mul(A, B);
}
void Unity_Power_float4(float4 A, float4 B, out float4 Out)
{
    Out = pow(A, B);
}
void Unity_SquareRoot_float4(float4 In, out float4 Out)
{
    Out = sqrt(In);
}
void Unity_Subtract_float4(float4 A, float4 B, out float4 Out)
{
    Out = A - B;
}
void Unity_DDX_float4(float4 In, out float4 Out)
{
    Out = ddx(In);
}
void Unity_DDXY_float4(float4 In, out float4 Out)
{
    Out = ddxy(In);
}
void Unity_DDY_float4(float4 In, out float4 Out)
{
    Out = ddy(In);
}
void Unity_InverseLerp_float4(float4 A, float4 B, float4 T, out float4 Out)
{
    Out = (T - A)/(B - A);
}
void Unity_Lerp_float4(float4 A, float4 B, float4 T, out float4 Out)
{
    Out = lerp(A, B, T);
}
void Unity_Smoothstep_float4(float4 Edge1, float4 Edge2, float4 In, out float4 Out)
{
    Out = smoothstep(Edge1, Edge2, In);
}
void Unity_MatrixConstruction_Row_float(float4 M0, float4 M1, float4 M2, float3 M3, out float4x4 Out4x4, out float3x3 Out3x3, out float2x2 Out2x2)
{
    Out4x4 = float4x4(M0.x, M0.y, M0.z, M0.w, M1.x, M1.y, M1.z, M1.w, M2.x, M2.y, M2.z, M2.w, M3.x, M3.y, M3.z, M3.w);
    Out3x3 = float3x3(M0.x, M0.y, M0.z, M1.x, M1.y, M1.z, M2.x, M2.y, M2.z);
    Out2x2 = float2x2(M0.x, M0.y, M1.x, M1.y);
}
void Unity_MatrixConstruction_Column_float(float4 M0, float4 M1, float4 M2, float3 M3, out float4x4 Out4x4, out float3x3 Out3x3, out float2x2 Out2x2)
{
    Out4x4 = float4x4(M0.x, M1.x, M2.x, M3.x, M0.y, M1.y, M2.y, M3.y, M0.z, M1.z, M2.z, M3.z, M0.w, M1.w, M2.w, M3.w);
    Out3x3 = float3x3(M0.x, M1.x, M2.x, M0.y, M1.y, M2.y, M0.z, M1.z, M2.z);
    Out2x2 = float2x2(M0.x, M1.x, M0.y, M1.y);
}
void Unity_MatrixDeterminant_float4x4(float4x4 In, out float Out)
{
    Out = determinant(In);
}
float2 _MatrixSplit_M0 = float2(In[0].r, In[0].g);
float2 _MatrixSplit_M1 = float2(In[1].r, In[1].g);
float2 _MatrixSplit_M2 = float2(0, 0);
float2 _MatrixSplit_M3 = float2(0, 0);
void Unity_MatrixTranspose_float4x4(float4x4 In, out float4x4 Out)
{
    Out = transpose(In);
}
void Unity_Clamp_float4(float4 In, float4 Min, float4 Max, out float4 Out)
{
    Out = clamp(In, Min, Max);
}
void Unity_Fraction_float4(float4 In, out float4 Out)
{
    Out = frac(In);
}
void Unity_Maximum_float4(float4 A, float4 B, out float4 Out)
{
    Out = max(A, B);
}
void Unity_Minimum_float4(float4 A, float4 B, out float4 Out)
{
    Out = min(A, B);
}
void Unity_OneMinus_float4(float4 In, out float4 Out)
{
    Out = 1 - In;
}
void Unity_RandomRange_float(float2 Seed, float Min, float Max, out float Out)
{
    float randomno =  frac(sin(dot(Seed, float2(12.9898, 78.233)))*43758.5453);
    Out = lerp(Min, Max, randomno);
}
void Unity_Remap_float4(float4 In, float2 InMinMax, float2 OutMinMax, out float4 Out)
{
    Out = OutMinMax.x + (In - InMinMax.x) * (OutMinMax.y - OutMinMax.x) / (InMinMax.y - InMinMax.x);
}
void Unity_Saturate_float4(float4 In, out float4 Out)
{
    Out = saturate(In);
}
void Unity_Ceiling_float4(float4 In, out float4 Out)
{
    Out = ceil(In);
}
void Unity_Floor_float4(float4 In, out float4 Out)
{
    Out = floor(In);
}
void Unity_Round_float4(float4 In, out float4 Out)
{
    Out = round(In);
}
void Unity_Sign_float4(float4 In, out float4 Out)
{
    Out = sign(In);
}
void Unity_Step_float4(float4 Edge, float4 In, out float4 Out)
{
    Out = step(Edge, In);
}
void Unity_Truncate_float4(float4 In, out float4 Out)
{
    Out = trunc(In);
}
void Unity_Arccosine_float4(float4 In, out float4 Out)
{
    Out = acos(In);
}
void Unity_Arcsine_float4(float4 In, out float4 Out)
{
    Out = asin(In);
}
void Unity_Arctangent_float4(float4 In, out float4 Out)
{
    Out = atan(In);
}
// Unity SDF Shape Shader (UV space)
// Author: GitHub Copilot
Shader "Custom/SDF_Arrow"
{
    Properties {
        _Color ("Color", Color) = (1,1,1,1)
        _Smooth ("Edge Smoothness", Float) = 0.01
        _HeadSize ("Head Size", Float) = 0.25
        _Thickness ("Shaft Thickness", Float) = 0.08
        _TexelSize ("Emulated Texel Size", Vector) = (0,0,0,0)
    }
    SubShader {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1000" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };
            fixed4 _Color;
            float _HeadSize;
            float _Thickness;
            float _Smooth;
            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            float sdCapsule(float2 p, float2 a, float2 b, float r) {
                float2 pa = p - a, ba = b - a;
                float h = clamp(dot(pa,ba)/dot(ba,ba), 0.0, 1.0);
                return length(pa - ba*h) - r;
            }
            float sdTriangle(float2 p, float2 a, float2 b, float2 c) {
                // Edge vectors
                float2 ba = b - a;
                float2 cb = c - b;
                float2 ac = a - c;
                
                // Point vectors
                float2 pa = p - a;
                float2 pb = p - b;
                float2 pc = p - c;
                
                // Calculate edge distances
                float2 nor = float2(ba.y, -ba.x);
                nor = normalize(nor);
                float h = dot(pa, nor);
                
                // Calculate closest distance to each edge
                float d1 = dot(pa,ba)/dot(ba,ba) < 0.0 ? length(pa) : 
                          dot(pa,ba)/dot(ba,ba) > 1.0 ? length(p-b) : 
                          abs(h);
                          
                nor = float2(cb.y, -cb.x);
                nor = normalize(nor);
                h = dot(pb, nor);
                float d2 = dot(pb,cb)/dot(cb,cb) < 0.0 ? length(pb) : 
                          dot(pb,cb)/dot(cb,cb) > 1.0 ? length(p-c) : 
                          abs(h);
                          
                nor = float2(ac.y, -ac.x);
                nor = normalize(nor);
                h = dot(pc, nor);
                float d3 = dot(pc,ac)/dot(ac,ac) < 0.0 ? length(pc) : 
                          dot(pc,ac)/dot(ac,ac) > 1.0 ? length(p-a) : 
                          abs(h);
                
                float d = min(min(d1, d2), d3);
                
                // Determine inside/outside
                float s = sign(ba.x*ac.y - ba.y*ac.x);
                
                return d * s;
            }
            fixed4 frag (v2f i) : SV_Target {
                float2 shaftA = float2(0.2,0.5);
                float2 shaftB = float2(0.7,0.5);
                float2 headTip = float2(0.8,0.5);
                float2 dir = normalize(headTip - shaftA);
                float2 ortho = float2(-dir.y, dir.x);
                float2 headL = headTip - dir * _HeadSize + ortho * _HeadSize * 0.5;
                float2 headR = headTip - dir * _HeadSize - ortho * _HeadSize * 0.5;
                // Calculate distances
                float dHeadRaw = sdTriangle(i.uv, headTip, headL, headR);
                // Apply thickness to the triangle SDF
                float dHead = abs(dHeadRaw) - _Thickness;
                
                // Combine shapes
                float d = dHead;
                
                float alpha;
                if (_Smooth <= 0.0001) {
                    // For hard edges, use step function
                    alpha = step(d, 0.0);
                } else {
                    // For smooth edges, use smoothstep
                    float smoothness = max(_Smooth, 0.00001);
                    alpha = 1.0 - smoothstep(-smoothness, smoothness, d);
                }
                
                // Ensure alpha is properly clamped
                alpha = saturate(alpha);
                return fixed4(_Color.rgb, _Color.a * alpha);
            }
            ENDCG
        }
    }
}
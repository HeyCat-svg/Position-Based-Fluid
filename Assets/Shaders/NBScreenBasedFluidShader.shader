Shader "Custom/NBScreenBasedFluidShader" {
    Properties {
        _ParticleTex ("ParticleTexture", 2D) = "white" {}
        _ParticleRad("ParticleRadius", Range(0.01, 3)) = 0.05
        _FilterRad("GaussFilterRadius", Range(0.01, 5)) = 1
        _NormalSampleRad("NormalSampleRadius", Range(0.01, 3)) = 1
        _VolumeRad("VolumeRadius", Range(0.001, 3)) = 0.005
        _SampleDelta("SampleDelta", Range(0.1, 2)) = 1
        _WaterColor("Water Color", Color) = (0.756, 0.90244, 1.0)
    }
    SubShader {
        // pass 0: conpute linear depth
        Pass {
            Tags { "RenderType" = "Opaque" }
            ZWrite On

            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct Particle {
                float3 oldPos;
                float3 newPos;
                float3 deltaP;
                float3 velocity;
                float3 deltaV;
                float3 force;
                float3 vorticity;
                int3 gridCoord;
                float lambda;
                float mass;
                float invMass;
                int rigbodyParticleIdx;
            };

            sampler2D _ParticleTex;
            float _ParticleRad;

            StructuredBuffer<Particle> _Particles;
            StructuredBuffer<int> _IsNarrowBand;

            struct v2g {
                float4 pos : SV_POSITION;
                float2 tex : TEXCORRD0;
                int isNarrowBand : TEXCOORD1;
                int isRigbody : TEXCOORD2;
            };

            struct g2f {
                float4 pos : SV_POSITION;
                float2 tex : TEXCOORD0;
                float linearDepth : TEXTCOORD1;     // [0, far]->[0, 1]
            };

            v2g vert (uint id : SV_VertexID) {
                v2g output;
                output.pos = float4(_Particles[id].oldPos, 1);
                output.tex = float2(0, 0);
                output.isNarrowBand = _IsNarrowBand[id];
                output.isRigbody = (_Particles[id].rigbodyParticleIdx != -1) ? 1 : 0;
                return output;
            }

            [maxvertexcount(4)]
            void geom(point v2g input[1], inout TriangleStream<g2f> outStream) {
                g2f output;
                // 非NarrowBand粒子 应该被剔除
                if (input[0].isNarrowBand != 1 || input[0].isRigbody) {
                    output.pos = float4(1, 1, 1, 1e-3);  // 在裁剪阶段会被剔除
                    output.tex = float2(0, 0);
                    output.linearDepth = 0;
                    outStream.Append(output);
                    outStream.Append(output);
                    outStream.Append(output);
                    outStream.RestartStrip();
                    return;
                }

                float4 viewPos = mul(UNITY_MATRIX_V, input[0].pos);
                float linearDepth = abs(viewPos.z) / _ProjectionParams.z;

                for (int x = 0; x < 2; ++x) {
                    for (int y = 0; y < 2; ++y) {
                        float2 tex = float2(x, y);
                        output.tex = tex;
                        output.pos = viewPos + float4((tex * 2 - float2(1, 1)) * _ParticleRad, 0, 0);
                        output.linearDepth = linearDepth;
                        output.pos = mul(UNITY_MATRIX_P, output.pos);
                        outStream.Append(output);
                    }
                }
                outStream.RestartStrip();
            }

            float4 frag (g2f i) : SV_Target {
                fixed4 col = tex2D(_ParticleTex, i.tex);
                clip(col.a - 0.3);
                // return fixed4(i.linearDepth, i.linearDepth, i.linearDepth, 1);
                // float projDepth = i.pos.z / i.pos.w;
                float linearDepth = i.linearDepth;
                return float4(linearDepth, linearDepth, linearDepth, 1);
            }
            ENDCG
        }

        // pass 1: Gauss vertical filter
        Pass {
            Tags { "RenderType" = "Opaque" }
            ZWrite Off
            ZTest Off

            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct a2v {
                float4 vertex : POSITION;
                float2 tex : TEXCOORD0;
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                float2 tex : TEXCOORD0;
            };

            static const float GaussWeight[7] = { 0.0205, 0.0855, 0.232, 0.324, 0.232, 0.0855, 0.0205 };

            sampler2D _FilterSource;
            float4 _FilterSource_TexelSize;
            float _FilterRad;

            v2f vert(a2v i) {
                v2f o;
                o.vertex = UnityObjectToClipPos(i.vertex);
                o.tex = i.tex;
                return o;
            }

            float4 frag(v2f input) : SV_Target{
                float3 sum = float3(0, 0, 0);
                float3 curCol = tex2D(_FilterSource, input.tex).xyz;
                if (all(curCol < 1e-4)) {
                    return float4(0, 0, 0, 1);
                }
                for (int i = 0; i < 7; ++i) {
                    float2 uv = float2(
                        input.tex.x, input.tex.y + (i - 3) * _FilterSource_TexelSize.y * _FilterRad);
                    float3 targetCol = tex2D(_FilterSource, uv).xyz;
                    targetCol = (all(curCol < 1e-4)) ? curCol : targetCol;
                    float3 diff = curCol - targetCol;
                    sum += exp(-dot(diff, diff)) * GaussWeight[i] * targetCol;
                }
                return float4(sum, 1.0);
            }

            ENDCG
        }

        // pass 2: Gauss horizontal filter
        Pass {
            Tags { "RenderType" = "Opaque" }
            ZWrite Off
            ZTest Off

            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct a2v {
                float4 vertex : POSITION;
                float2 tex : TEXCOORD0;
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                float2 tex : TEXCOORD0;
            };

            static const float GaussWeight[7] = { 0.0205, 0.0855, 0.232, 0.324, 0.232, 0.0855, 0.0205 };

            sampler2D _FilterSource;
            float4 _FilterSource_TexelSize;
            float _FilterRad;

            v2f vert(a2v i) {
                v2f o;
                o.vertex = UnityObjectToClipPos(i.vertex);
                o.tex = i.tex;
                return o;
            }

            float4 frag(v2f input) : SV_Target{
                float3 sum = float3(0, 0, 0);
                float3 curCol = tex2D(_FilterSource, input.tex).xyz;
                if (all(curCol < 1e-4)) {
                    return float4(0, 0, 0, 1);
                }
                for (int i = 0; i < 7; ++i) {
                    float2 uv = float2(
                        input.tex.x + (i - 3) * _FilterSource_TexelSize.x * _FilterRad, input.tex.y);
                    float3 targetCol = tex2D(_FilterSource, uv).xyz;
                    targetCol = (all(curCol < 1e-4)) ? curCol : targetCol;
                    float3 diff = curCol - targetCol;
                    sum += exp(-dot(diff, diff)) * GaussWeight[i] * targetCol;
                }
                return float4(sum, 1.0);
            }

            ENDCG
        }

        // pass 3: normal estimation
        Pass {
            Tags { "RenderType" = "Opaque" }
            ZWrite Off
            ZTest Off

            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag

            struct a2v {
                float4 vertex : POSITION;
                float2 tex : TEXCOORD0;
            };

            struct v2f {
                float4 vertex : POSITION;
                float2 tex : TEXCOORD0;
            };

            sampler2D _DepthRT;
            float4 _DepthRT_TexelSize;
            float _NormalSampleRad;

            v2f vert(a2v i) {
                v2f o;
                o.vertex = UnityObjectToClipPos(i.vertex);
                o.tex = i.tex;
                return o;
            }

            float4 frag(v2f i) : COLOR{
                float depth = tex2D(_DepthRT, i.tex).x;
                if (depth < 1e-4) {
                    return float4(0, 0, 0, 1);
                }
                float2 xy = float2(2, 2);
                float depthx2 = tex2D(_DepthRT, float2(i.tex.x + _DepthRT_TexelSize.x * _NormalSampleRad, i.tex.y)).x;
                if (depthx2 < 1e-4) {
                    depthx2 = depth;
                    xy.x--;
                }
                float depthx1 = tex2D(_DepthRT, float2(i.tex.x - _DepthRT_TexelSize.x * _NormalSampleRad, i.tex.y)).x;
                if (depthx1 < 1e-4) {
                    depthx1 = depth;
                    xy.x--;
                }
                float depthy2 = tex2D(_DepthRT, float2(i.tex.x, i.tex.y + _DepthRT_TexelSize.y * _NormalSampleRad)).x;
                if (depthy2 < 1e-4) {
                    depthy2 = depth;
                    xy.y--;
                }
                float depthy1 = tex2D(_DepthRT, float2(i.tex.x, i.tex.y - _DepthRT_TexelSize.y * _NormalSampleRad)).x;
                if (depthy1 < 1e-4) {
                    depthy1 = depth;
                    xy.y--;
                }
                float dx = (depthx2 - depthx1) / (xy.x * _DepthRT_TexelSize.x * _NormalSampleRad + 1e-4);
                float dy = (depthy2 - depthy1) / (xy.y * _DepthRT_TexelSize.y * _NormalSampleRad + 1e-4);
                return float4(0.5 * normalize(float3(dx, dy, -1)) + 0.5, 1);
            }
            ENDCG
        }

        // pass 4: volume restoration
        Pass{
            Tags { "RenderType" = "Transparent" }
            ZWrite Off
            ZTest Off
            Blend One One

            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct GridInfo {
                int2 particleRange;
                float3 barycenter;
                int particleNum;
                int layer;
            };

            float _VolumeRad;
            float _Aspect;

            StructuredBuffer<GridInfo> _GridInfoBuffer;

            struct v2g {
                float4 pos : POSITION;
                int n : TEXCOORD0;              // particle num in a grid cell
            };

            struct g2f {
                float4 pos : SV_POSITION;
                float2 center : TEXCOORD0;
                float2 screenPos : TEXCOORD1;
                int n : TEXTCOORD2;
            };

            v2g vert(uint id : SV_VertexID) {
                v2g output;
                output.pos = float4(_GridInfoBuffer[id].barycenter, 1.0);
                output.n = _GridInfoBuffer[id].particleNum;
                return output;
            }

            [maxvertexcount(4)]
            void geom(point v2g input[1], inout TriangleStream<g2f> outStream) {
                g2f output;
                // 非NarrowBand粒子 应该被剔除
                if (input[0].n <= 0) {
                    output.pos = float4(1, 1, 1, 1e-3);  // 在裁剪阶段会被剔除
                    output.center = float2(0, 0);
                    output.screenPos = float2(0, 0);
                    output.n = 0;
                    outStream.Append(output);
                    outStream.Append(output);
                    outStream.Append(output);
                    outStream.RestartStrip();
                    return;
                }

                float4 viewPos = mul(UNITY_MATRIX_V, input[0].pos);
                float4 projPos = mul(UNITY_MATRIX_P, viewPos);
                float2 center = 0.5 * (projPos.xy / projPos.w) + float2(0.5, 0.5);
                float panelScale = 0.14 * abs(viewPos.z);        // view.z越大 panel越大

                for (int x = 0; x < 2; ++x) {
                    for (int y = 0; y < 2; ++y) {
                        float2 tex = float2(x, y);
                        float4 offset = float4((tex * 2 - float2(1, 1)) * panelScale, 0, 0);
                        // offset.y *= _Aspect;
                        output.pos = mul(UNITY_MATRIX_P, viewPos + offset);
                        output.center = center;
                        output.screenPos = 0.5 * (output.pos.xy / output.pos.w) + 0.5;
                        output.n = input[0].n;
                        outStream.Append(output);
                    }
                }
                outStream.RestartStrip();
            }

            float4 frag(g2f i) : SV_Target{
                float2 offset = i.screenPos - i.center;
                offset.x *= _Aspect;
                float dist = length(offset);
                // clip(_VolumeRad - dist);
                float x = dist * 80;
                float gaussVal = i.n * exp(-x * x * 0.5) * 0.005;

                // return float4(1, 1, 1, 1);
                return float4(gaussVal, gaussVal, gaussVal, 1);
            }
            ENDCG
        }

        // pass 5: down/up sample
        Pass {
            Tags { "RenderType" = "Opaque" }
            ZWrite Off
            ZTest Off

            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct a2v {
                float4 vertex : POSITION;
                float2 tex : TEXCOORD0;
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                float2 tex : TEXCOORD0;
            };

            sampler2D _SampleSource;
            float4 _SampleSource_TexelSize;
            float _SampleDelta;

            v2f vert(a2v i) {
                v2f o;
                o.vertex = UnityObjectToClipPos(i.vertex);
                o.tex = i.tex;
                return o;
            }

            float4 frag(v2f i) : SV_Target{
                float4 o = _SampleSource_TexelSize.xyxy * float2(-_SampleDelta, _SampleDelta).xxyy;
                float4 sum = tex2D(_SampleSource, i.tex + o.xy) + tex2D(_SampleSource, i.tex + o.zy) +
                    tex2D(_SampleSource, i.tex + o.xw) + tex2D(_SampleSource, i.tex + o.zw);
                return float4(0.25 * sum.xyz, 1.0);
            }

            ENDCG
        }

        // pass 6: light attenuation
        Pass {
            Tags { "RenderType" = "Opaque" }
            ZWrite Off
            ZTest Off

            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct a2v {
                float4 vertex : POSITION;
                float2 tex : TEXCOORD0;
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                float2 tex : TEXCOORD0;
            };

            sampler2D _VolumeTex;

            v2f vert(a2v i) {
                v2f o;
                o.vertex = UnityObjectToClipPos(i.vertex);
                o.tex = i.tex;
                return o;
            }

            float4 frag(v2f i) : SV_Target{
                float vol = tex2D(_VolumeTex, i.tex).x;
                if (vol < 1e-4) {
                    return float4(0, 0, 0, 1);
                }
                return float4(float3(0.756, 0.90244, 1.0) * exp(-2 * vol), 1);
            }

            ENDCG
        }

        // pass 7: final light rendering
        Pass {
            Tags { "RenderType" = "Opaque" "LightMode" = "ForwardBase" }
            ZWrite Off
            ZTest Off

            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase

            #include "UnityCG.cginc"

            struct a2v {
                float4 vertex : POSITION;
                float2 tex : TEXCOORD0;
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                float2 tex : TEXCOORD0;
            };

            float _TanHalfFOV;              // tan vertical half FOV
            float _Aspect;
            float4 _WaterColor;
            sampler2D _NormalMap;
            sampler2D _VolumeMap;
            sampler2D _DepthMap;            // linear depth
            float4 _LightColor0;            // unity builtin var

            v2f vert(a2v i) {
                v2f o;
                o.vertex = UnityObjectToClipPos(i.vertex);
                o.tex = i.tex;
                return o;
            }

            float4 frag(v2f i) : SV_Target{
                // rebuild view pos from linear depth
                float viewDepth = tex2D(_DepthMap, i.tex).x * _ProjectionParams.z;
                clip(viewDepth - 1e-4);
                float2 uv_ = 2.0 * i.tex - 1.0;
                float3 viewPos = viewDepth * float3(uv_.x * _TanHalfFOV * _Aspect, uv_.y * _TanHalfFOV, -1);    // view space is right-hand-coord

                // convert light pos or light dir to view space;
                float4 viewLightPos = mul(UNITY_MATRIX_V, _WorldSpaceLightPos0);
                float3 viewLightDir = normalize(viewLightPos.xyz - viewLightPos.w * viewPos);

                float3 viewNormal = tex2D(_NormalMap, i.tex).rgb;     // get normal from normal map
                float3 viewDir = normalize(mul(UNITY_MATRIX_V, _WorldSpaceCameraPos).xyz - viewPos);
                float3 halfDir = 0.5 * (viewDir + viewLightDir);
                float attenuation = exp(-2 * tex2D(_VolumeMap, i.tex).x);

                half3 albedo = _WaterColor.rgb;
                // half3 ambient = max(ShadeSH9(half4(0.0, 1.0, 0.0, 1.0)), ShadeSH9(half4(0.0, -1.0, 0.0, 1.0)));
                half3 ambient = float3(0.2, 0.2, 0.2);
                half nl = saturate(dot(viewNormal, viewLightDir));
                half nh = max(dot(viewNormal, halfDir), 1e-5);
                half3 diff = albedo * nl;
                half3 spec = pow(nh, 20) * albedo * float3(1, 1, 1);    // gradient * albedo * specColor
                
                half3 col = ambient * albedo + (diff + spec) * _LightColor0.rgb * attenuation;

                return float4(col, 1.0);
            }

            ENDCG
        }
    }
}

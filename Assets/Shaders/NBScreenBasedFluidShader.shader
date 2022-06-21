Shader "Custom/NBScreenBasedFluidShader" {
    Properties {
        _ParticleTex ("ParticleTexture", 2D) = "white" {}
        _ParticleRad("ParticleRadius", Range(0.01, 3)) = 0.05
        _FilterRad("GaussFilterRadius", Range(0.01, 3)) = 1
    }
    SubShader {
        Tags { "RenderType"="Opaque" }
        
        // pass 0: conpute linear depth
        Pass {
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

            sampler2D _DepthFilterRT;
            float4 _DepthFilterRT_TexelSize;
            float _FilterRad;

            v2f vert(a2v i) {
                v2f o;
                o.vertex = UnityObjectToClipPos(i.vertex);
                o.tex = i.tex;
                return o;
            }

            float4 frag(v2f input) : SV_Target{
                float3 sum = float3(0, 0, 0);
                for (int i = 0; i < 7; ++i) {
                    float2 uv = float2(
                        input.tex.x, input.tex.y + (i - 3) * _DepthFilterRT_TexelSize.y * _FilterRad);
                    sum += GaussWeight[i] * tex2D(_DepthFilterRT, uv).xyz;
                }
                return float4(sum, 1.0);
            }

            ENDCG
        }

        // pass 2: Gauss horizontal filter
        Pass {
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

            sampler2D _DepthFilterRTTmp;
            float4 _DepthFilterRTTmp_TexelSize;
            float _FilterRad;

            v2f vert(a2v i) {
                v2f o;
                o.vertex = UnityObjectToClipPos(i.vertex);
                o.tex = i.tex;
                return o;
            }

            float4 frag(v2f input) : SV_Target{
                float3 sum = float3(0, 0, 0);
                for (int i = 0; i < 7; ++i) {
                    float2 uv = float2(
                        input.tex.x + (i - 3) * _DepthFilterRTTmp_TexelSize.x * _FilterRad, input.tex.y);
                    sum += GaussWeight[i] * tex2D(_DepthFilterRTTmp, uv).xyz;
                }
                return float4(sum, 1.0);
            }

            ENDCG
        }

        // pass 3: normal estimation
        Pass {
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

            v2f vert(a2v i) {
                v2f o;
                o.vertex = UnityObjectToClipPos(i.vertex);
                o.tex = i.tex;
                return o;
            }

            float4 frag(v2f i) : COLOR{
                float dx =
                    (tex2D(_DepthRT, float2(i.tex.x + _DepthRT_TexelSize.x, i.tex.y)).x -
                        tex2D(_DepthRT, float2(i.tex.x - _DepthRT_TexelSize.x, i.tex.y)).x) / (2.0 * _DepthRT_TexelSize.x);
                float dy =
                    (tex2D(_DepthRT, float2(i.tex.x, i.tex.y + _DepthRT_TexelSize.y)).x -
                        tex2D(_DepthRT, float2(i.tex.x, i.tex.y - _DepthRT_TexelSize.y)).x) / (2.0 * _DepthRT_TexelSize.y);
                return float4(dx, dy, 1, 1);
            }

            ENDCG
        }
    }
}

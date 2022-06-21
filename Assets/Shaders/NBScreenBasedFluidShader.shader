Shader "Custom/NBScreenBasedFluidShader" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _ParticleRad("ParticleRadius", Range(0.01, 1)) = 0.05
    }
    SubShader {
        Tags { "RenderType"="Opaque" }

        Pass {
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

            sampler2D _MainTex;
            float _ParticleRad;

            StructuredBuffer<float4> _Particles;
            StructuredBuffer<int> _IsNarrowBand;

            struct v2g {
                float4 pos : SV_POSITION;
                float2 tex : TEXCORRD0;
                int isNarrowBand : TEXCOORD1;
            };

            struct g2f {
                float4 pos : SV_POSITION;
                float2 tex : TEXCOORD0;
            };

            v2g vert (uint id : SV_VertexID) {
                v2g output;
                output.pos = _Particles[id];
                output.tex = float2(0, 0);
                output.isNarrowBand = _IsNarrowBand[id];
                return output;
            }

            [maxvertexcount(4)]
            void geom(point v2g input[1], inout TriangleStream<g2f> outStream) {
                g2f output;
                // 非NarrowBand粒子 应该被剔除
                if (input[0].isNarrowBand != 1) {
                    output.pos = float4(1, 1, 1, 1e-3);  // 在裁剪阶段会被剔除
                    output.tex = float2(0, 0);
                    outStream.Append(output);
                    outStream.Append(output);
                    outStream.Append(output);
                    outStream.RestartStrip();
                    return;
                }

                float4 viewPos = mul(UNITY_MATRIX_V, input[0].pos);

                for (int x = 0; x < 2; ++x) {
                    for (int y = 0; y < 2; ++y) {
                        float2 tex = float2(x, y);
                        output.tex = tex;
                        output.pos = viewPos + float4((tex * 2 - float2(1, 1)) * _ParticleRad, 0, 0);
                        output.pos = mul(UNITY_MATRIX_P, output.pos);
                        outStream.Append(output);
                    }
                }
                outStream.RestartStrip();
            }

            fixed4 frag (g2f i) : SV_Target {
                fixed4 col = tex2D(_MainTex, i.tex);
                clip(col.a - 0.3);
                return fixed4(col.xyz, 1);
            }
            ENDCG
        }
    }
}

Shader "Custom/PBF2D_GPU" {
	Properties{
		_MainTex("Texture", 2D) = "white" {}
		_ParticleRad("ParticleRadius", Range(0.01, 1)) = 0.05
	}

		SubShader{
			ZWrite Off
			Blend SrcAlpha OneMinusSrcAlpha

			Pass{
				CGPROGRAM

				#pragma target 5.0
				#pragma vertex vert
				#pragma geometry geom
				#pragma fragment frag

				#include "UnityCG.cginc"

				sampler2D _MainTex;
				float _ParticleRad;

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
				StructuredBuffer<Particle> _Particles;
				StructuredBuffer<Particle> _SortedParticles;
				StructuredBuffer<int2> _GridParticlePair;
				StructuredBuffer<int2> _GridBuffer;
				StructuredBuffer<int> _IsNarrowBand;
				float3 _GridDim;

				struct v2g {
					float4 pos : SV_POSITION;
					float2 tex : TEXCOORD0;
					float4 col : COLOR;
					int isRigbody : TEXCOORD1;
					int isNarrowBand : TEXCOORD2;
				};

				struct g2f {
					float4 pos : SV_POSITION;
					float2 tex : TEXCOORD0;
					float4 col : COLOR;
				};

				v2g vert(uint id : SV_VertexID) {
					v2g output;
					output.pos = float4(_Particles[id].oldPos, 1);
					output.tex = float2(0, 0);
					// output.col = float4(0.5 + normalize(_Particles[id].velocity) / 2, 0.5, 1); 
					/*int3 gridCoord = _SortedParticles[id].gridCoord;
					int gridIdx = gridCoord.x + gridCoord.y * _GridDim.x + gridCoord.z * _GridDim.x * _GridDim.y;
					float c = (float)(id - _GridBuffer[gridIdx].x) / (_GridBuffer[gridIdx].y - _GridBuffer[gridIdx].x);
					output.col = float4(c.xxx, 1);*/
					// output.col = (_Particles[id].mass > 1) ? float4(1, 0, 0, 1) : float4(1, 1, 1, 1);
					// output.col = float4(0.7 + 0.3 *_Particles[id].velocity, 0.7,1);
					output.col = (_Particles[id].rigbodyParticleIdx != -1) ? float4(1, 0, 0, 1) : float4(1, 1, 1, 1);
					output.isRigbody = (_Particles[id].rigbodyParticleIdx != -1) ? 1 : 0;
					output.isNarrowBand = _IsNarrowBand[id];
					return output;
				}

				[maxvertexcount(4)]
				void geom(point v2g input[1], inout TriangleStream<g2f> outStream) {
					g2f output;

					// 非NarrowBand粒子 应该被剔除
					if (input[0].isNarrowBand != 1 && input[0].isRigbody != 1) {
						output.pos = float4(1, 1, 1, 1e-3);  // 在裁剪阶段会被剔除
						output.tex = float2(0, 0);
						output.col = input[0].col;
						outStream.Append(output);
						outStream.Append(output);
						outStream.Append(output);
						outStream.RestartStrip();
						return;
					}

					float4 viewPos = mul(UNITY_MATRIX_V, input[0].pos);
					float4 col = input[0].col;

					for (int x = 0; x < 2; x++) {
						for (int y = 0; y < 2; y++) {
							float r = (input[0].isRigbody > 1e-3) ? _ParticleRad * 5 : _ParticleRad;
							float2 tex = float2(x, y);
							output.tex = tex;
							output.pos = viewPos + float4((tex * 2 - float2(1, 1)) * r, 0, 0);
							output.pos = mul(UNITY_MATRIX_P, output.pos);
							output.col = col;
							outStream.Append(output);
						}
					}
					outStream.RestartStrip();
				}

				fixed4 frag(g2f i) : COLOR{
					float4 col = tex2D(_MainTex, i.tex) * i.col;
					if (col.a < 0.3) discard;
					return fixed4(col.xyz, 1);
				}

				ENDCG
			}
		}
}
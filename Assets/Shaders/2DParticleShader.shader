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
				};
				StructuredBuffer<Particle> _Particles;
				StructuredBuffer<Particle> _SortedParticles;
				StructuredBuffer<int2> _GridParticlePair;
				StructuredBuffer<int2> _GridBuffer;
				float3 _GridDim;

				struct v2g {
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
					output.col = (_Particles[id].mass > 1) ? float4(1, 0, 0, 1) : float4(1, 1, 1, 1);
					// output.col = float4(0.7 + 0.3 *_Particles[id].velocity, 0.7,1);
					return output;
				}

				[maxvertexcount(4)]
				void geom(point v2g input[1], inout TriangleStream<v2g> outStream) {
					v2g output;

					float4 pos = input[0].pos;
					float4 col = input[0].col;

					float4x4 billboardMatrix = UNITY_MATRIX_V;
					billboardMatrix._m03 =
					billboardMatrix._m13 =
					billboardMatrix._m23 =
					billboardMatrix._m33 = 0;
					billboardMatrix = transpose(billboardMatrix);

					for (int x = 0; x < 2; x++) {
						for (int y = 0; y < 2; y++) {
							float2 tex = float2(x, y);
							output.tex = tex;

							output.pos = pos + mul(billboardMatrix, float4((tex * 2 - float2(1, 1)) * _ParticleRad, 0, 0));
							// output.pos = pos + mul(float4((tex * 2 - float2(1, 1)) * _ParticleRad, 0, 1), billboardMatrix);
							output.pos = mul(UNITY_MATRIX_VP, output.pos);

							output.col = col;

							outStream.Append(output);
						}
					}

					outStream.RestartStrip();
				}

				fixed4 frag(v2g i) : COLOR{
					float4 col = tex2D(_MainTex, i.tex) * i.col;
					if (col.a < 0.3) discard;
					return fixed4(col.xyz, 1);
				}

				ENDCG
			}
		}
}
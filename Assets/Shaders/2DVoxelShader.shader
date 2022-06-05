﻿Shader "Custom/Voxel2D" {
	Properties{
		_MainTex("Texture", 2D) = "white" {}
		_ParticleRad("ParticleRadius", Range(0.0001, 1)) = 0.05
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

				struct Voxel {
					float3 distGrad;
					float3 position;
					float distance;
					float isInner;			// 0:outside 1:inside
				};
				StructuredBuffer<Voxel> _Voxels;
				int _VoxelNum;
				float3 _VoxelDim;

				struct v2g {
					float4 pos : SV_POSITION;
					float2 tex : TEXCOORD0;
					float4 col : COLOR;
				};

				v2g vert(uint id : SV_VertexID) {
					v2g output;
					output.pos = mul(UNITY_MATRIX_M, float4(_Voxels[id].position, 1));
					output.tex = float2(0, 0);
					output.col = (_Voxels[id].isInner < 1e-3) ? float4(0, 0, 0, 0) : float4(1, 1, 1, 1);
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
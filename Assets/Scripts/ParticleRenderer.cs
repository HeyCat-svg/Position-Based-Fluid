using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PositionBasedFluid {
    public class ParticleRenderer : MonoBehaviour {

        public PBF_GPU m_Simulator;
        public Material m_ParticleRenderMat;

        void OnRenderObject() {
            Vector3Int m_GridDim = m_Simulator.GetGridDim();
            m_ParticleRenderMat.SetPass(0);
            m_ParticleRenderMat.SetBuffer("_Particles", m_Simulator.GetBuffer());
            m_ParticleRenderMat.SetVector("_GridDim", new Vector4(m_GridDim.x, m_GridDim.y, m_GridDim.z, 0));
            ComputeBuffer buffer = m_Simulator.GetSortedGridParticlePair();
            if (buffer != null) {
                m_ParticleRenderMat.SetBuffer("_GridParticlePair", buffer);
            }
            m_ParticleRenderMat.SetBuffer("_SortedParticles", m_Simulator.GetSortedParticleBuffer());
            m_ParticleRenderMat.SetBuffer("_GridBuffer", m_Simulator.GetGridBuffer());
            Graphics.DrawProceduralNow(MeshTopology.Points, m_Simulator.GetParticleNum());
        }
    }
}
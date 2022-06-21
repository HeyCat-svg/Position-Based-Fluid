using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using PositionBasedFluid.DataStructure;

namespace PositionBasedFluid {
    public class ParticleRenderer : MonoBehaviour {

        public PBF_GPU m_Simulator;
        public Material m_ParticleRenderMat;
        public ComputeShader m_RenderCS;
        public Camera m_MainCamera;
        public int m_NMin = 8;

        int m_GridNum = 0;
        int PBF_BLOCK_SIZE;
        int m_ParticleNum = 0;

        // Compute Shader Kernel IDs
        int m_InitGridInfoKernel;
        int m_FindLayer1Kernel;
        int m_FindLayer2Kernel;
        int m_ClearNarrowBandParticleKernel;
        int m_FindNarrowBandParticleKernel;

        // Render Targets
        int m_DepthRT;
        int m_DepthFilterRT;

        MaterialPropertyBlock m_MatPropBlock;

        CommandBuffer m_ParticleRenderCmd;  // 计算particle深度
        CommandBuffer m_GaussFilterCmd;     // filter depth map
        CommandBuffer m_NormalMapCmd;       // 计算normal map
        CommandBuffer m_VolumeCmd;          // 计算volume

        // buffer don't need to be inited
        ComputeBuffer m_GridBuffer;
        ComputeBuffer m_SortedParticleBuffer;
        // buffer need to be inited
        ComputeBuffer m_GridInfoBuffer;
        ComputeBuffer m_IsNarrowBandBuffer;


        void OnRenderObject() {
            // return;
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
            m_ParticleRenderMat.SetBuffer("_IsNarrowBand", m_IsNarrowBandBuffer);
            Graphics.DrawProceduralNow(MeshTopology.Points, m_Simulator.GetSimParticleNum());
        }

        void OnDestroy () {
            //if (m_MainCamera != null) {
            //    m_MainCamera.RemoveCommandBuffer(CameraEvent.AfterEverything, m_ParticleRenderCmd);
            //}
            if (m_GridInfoBuffer != null) {
                m_GridInfoBuffer.Release();
                m_GridInfoBuffer = null;
            }
            if (m_IsNarrowBandBuffer != null) {
                m_IsNarrowBandBuffer.Release();
                m_IsNarrowBandBuffer = null;
            }
        }

        void RenderNarrowBandParticles() {

        }

        void InitCommandBuffer() {
            int m_DepthRT = Shader.PropertyToID("_DepthRT");

            m_MatPropBlock = new MaterialPropertyBlock();
            m_MatPropBlock.SetBuffer("_Particles", m_Simulator.GetParticlePosBuffer());
            m_MatPropBlock.SetBuffer("_IsNarrowBand", m_IsNarrowBandBuffer);

            // render Narrow Band Particle
            m_ParticleRenderCmd = new CommandBuffer();
            m_ParticleRenderCmd.name = "Particle Render";
            m_ParticleRenderCmd.GetTemporaryRT(m_DepthRT, -1, -1, 24, FilterMode.Bilinear);
            m_ParticleRenderCmd.SetRenderTarget(m_DepthRT);
            m_ParticleRenderCmd.ClearRenderTarget(true, true, Color.black);
            m_ParticleRenderCmd.DrawProcedural(
                Matrix4x4.identity, m_ParticleRenderMat, 0, MeshTopology.Points, m_ParticleNum, 1, m_MatPropBlock);
            m_ParticleRenderCmd.Blit(m_DepthRT, BuiltinRenderTextureType.CameraTarget);

            // 
            m_MainCamera.AddCommandBuffer(CameraEvent.AfterEverything, m_ParticleRenderCmd);
            Debug.Log("Render command added!");
        }

        public void Init() {
            if (m_Simulator == null || m_RenderCS == null) {
                return;
            }
            m_GridNum = m_Simulator.GetGridCellNum();
            PBF_BLOCK_SIZE = m_Simulator.GetSimBlockSize();
            m_ParticleNum = m_Simulator.GetParticleNum();

            m_InitGridInfoKernel = m_RenderCS.FindKernel("InitGridInfo");
            m_FindLayer1Kernel = m_RenderCS.FindKernel("FindLayer1");
            m_FindLayer2Kernel = m_RenderCS.FindKernel("FindLayer2");
            m_ClearNarrowBandParticleKernel = m_RenderCS.FindKernel("ClearNarrowBandParticle");
            m_FindNarrowBandParticleKernel = m_RenderCS.FindKernel("FindNarrowBandParticle");

            Vector3Int gridDim = m_Simulator.GetGridDim();
            m_RenderCS.SetInt("_GridNum", m_GridNum);
            m_RenderCS.SetInts("_GridDim", new int[3] { gridDim.x, gridDim.y, gridDim.z });
            m_RenderCS.SetInt("_NMin", m_NMin);

            m_GridBuffer = m_Simulator.GetGridBuffer();
            m_GridInfoBuffer = new ComputeBuffer(m_Simulator.GetGridCellNum(), Marshal.SizeOf(typeof(GridInfo)));
            m_IsNarrowBandBuffer = new ComputeBuffer(m_ParticleNum, Marshal.SizeOf(typeof(int)));

            // InitCommandBuffer();
        }

        public void CollectGridInfo() {
            m_SortedParticleBuffer = m_Simulator.GetSortedParticleBuffer();

            // init grid info
            m_RenderCS.SetBuffer(m_InitGridInfoKernel, "_GridBuffer", m_GridBuffer);
            m_RenderCS.SetBuffer(m_InitGridInfoKernel, "_GridInfoBuffer", m_GridInfoBuffer);
            m_RenderCS.SetBuffer(m_InitGridInfoKernel, "_ParticleBufferSorted", m_SortedParticleBuffer);
            m_RenderCS.Dispatch(m_InitGridInfoKernel, m_GridNum / PBF_BLOCK_SIZE, 1, 1);

            // find layer 1
            m_RenderCS.SetBuffer(m_FindLayer1Kernel, "_GridInfoBuffer", m_GridInfoBuffer);
            m_RenderCS.Dispatch(m_FindLayer1Kernel, m_GridNum / PBF_BLOCK_SIZE, 1, 1);

            // find layer 2
            m_RenderCS.SetBuffer(m_FindLayer2Kernel, "_GridInfoBuffer", m_GridInfoBuffer);
            m_RenderCS.Dispatch(m_FindLayer2Kernel, m_GridNum / PBF_BLOCK_SIZE, 1, 1);

            // clear narrow band
            m_RenderCS.SetBuffer(m_ClearNarrowBandParticleKernel, "_IsNarrowBand", m_IsNarrowBandBuffer);
            m_RenderCS.Dispatch(m_ClearNarrowBandParticleKernel, m_ParticleNum / PBF_BLOCK_SIZE, 1, 1);

            // find narrow band particle
            m_RenderCS.SetBuffer(m_FindNarrowBandParticleKernel, "_GridInfoBuffer", m_GridInfoBuffer);
            m_RenderCS.SetBuffer(m_FindNarrowBandParticleKernel, "_ParticleBufferSorted", m_SortedParticleBuffer);
            m_RenderCS.SetBuffer(m_FindNarrowBandParticleKernel, "_IsNarrowBand", m_IsNarrowBandBuffer);
            m_RenderCS.Dispatch(m_FindNarrowBandParticleKernel, m_GridNum / PBF_BLOCK_SIZE, 1, 1);
        }
    }
}
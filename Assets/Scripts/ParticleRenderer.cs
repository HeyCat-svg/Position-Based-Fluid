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
        public Material m_DebugParticleRenderMat;
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
        int m_NormalMapRT;
        int m_VolumeRT;
        int m_VolumeFilterRT;
        int m_LightAttenuationRT;

        MaterialPropertyBlock m_MatPropBlock;
        CommandBuffer m_ParticleRenderCmd;  // 计算particle深度

        // buffer don't need to be inited
        ComputeBuffer m_GridBuffer;
        ComputeBuffer m_SortedParticleBuffer;
        // buffer need to be inited
        ComputeBuffer m_GridInfoBuffer;
        ComputeBuffer m_IsNarrowBandBuffer;


        void OnRenderObject() {
            // return;
            Vector3Int m_GridDim = m_Simulator.GetGridDim();
            m_DebugParticleRenderMat.SetPass(0);
            m_DebugParticleRenderMat.SetBuffer("_Particles", m_Simulator.GetBuffer());
            m_DebugParticleRenderMat.SetVector("_GridDim", new Vector4(m_GridDim.x, m_GridDim.y, m_GridDim.z, 0));
            ComputeBuffer buffer = m_Simulator.GetSortedGridParticlePair();
            if (buffer != null) {
                m_DebugParticleRenderMat.SetBuffer("_GridParticlePair", buffer);
            }
            m_DebugParticleRenderMat.SetBuffer("_SortedParticles", m_Simulator.GetSortedParticleBuffer());
            m_DebugParticleRenderMat.SetBuffer("_GridBuffer", m_Simulator.GetGridBuffer());
            m_DebugParticleRenderMat.SetBuffer("_IsNarrowBand", m_IsNarrowBandBuffer);
            Graphics.DrawProceduralNow(MeshTopology.Points, m_Simulator.GetSimParticleNum());
        }

        void OnDestroy () {
            if (m_MainCamera != null) {
                m_MainCamera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, m_ParticleRenderCmd);
            }
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
            m_DepthRT = Shader.PropertyToID("_DepthRT");
            int depthRTDepthBuffer = Shader.PropertyToID("_DepthBuffer");
            m_DepthFilterRT = Shader.PropertyToID("_DepthFilterRT");
            int depthFilterRT = Shader.PropertyToID("_DepthFilterRTTmp");
            m_NormalMapRT = Shader.PropertyToID("_NormalMap");
            m_VolumeRT = Shader.PropertyToID("_VolumeRT");
            m_VolumeFilterRT = Shader.PropertyToID("_VolumeFilterRT");
            int volumeFilterRT = Shader.PropertyToID("_VolumeFilterRTTmp");
            m_LightAttenuationRT = Shader.PropertyToID("_LightAttenuationRT");

            m_MatPropBlock = new MaterialPropertyBlock();
            m_MatPropBlock.SetBuffer("_Particles", m_Simulator.GetBuffer());
            m_MatPropBlock.SetBuffer("_IsNarrowBand", m_IsNarrowBandBuffer);
            m_MatPropBlock.SetBuffer("_GridInfoBuffer", m_GridInfoBuffer);

            // render Narrow Band Particle
            m_ParticleRenderCmd = new CommandBuffer{ name = "Particle Depth" };
            m_ParticleRenderCmd.GetTemporaryRT(m_DepthRT, -1, -1, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBFloat);
            m_ParticleRenderCmd.GetTemporaryRT(depthRTDepthBuffer, -1, -1, 24, FilterMode.Bilinear, RenderTextureFormat.Depth);
            m_ParticleRenderCmd.SetRenderTarget(m_DepthRT, (RenderTargetIdentifier)depthRTDepthBuffer);
            m_ParticleRenderCmd.ClearRenderTarget(true, true, Color.black);
            m_ParticleRenderCmd.DrawProcedural(
                Matrix4x4.identity, m_ParticleRenderMat, 0, MeshTopology.Points, m_ParticleNum, 1, m_MatPropBlock);
            
            // Depth Gauss Filter
            m_ParticleRenderCmd.GetTemporaryRT(m_DepthFilterRT, Screen.width / 2, Screen.height / 2, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBFloat);
            m_ParticleRenderCmd.GetTemporaryRT(depthFilterRT, Screen.width / 2, Screen.height / 2, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBFloat);
            m_ParticleRenderMat.SetFloat("SampleDelta", 1.0f);           // down sample 正常像素采样间距
            m_ParticleRenderCmd.SetGlobalTexture("_SampleSource", m_DepthRT);
            m_ParticleRenderCmd.Blit(m_DepthRT, m_DepthFilterRT, m_ParticleRenderMat, 5);
            float[] depthR = new float[3] { 1.0f, 1.8f, 2.6f };
            for (int i = 0; i < 3; ++i) {
                m_ParticleRenderMat.SetFloat("_FilterRad", depthR[i]);
                m_ParticleRenderCmd.SetGlobalTexture("_FilterSource", m_DepthFilterRT);
                m_ParticleRenderCmd.Blit(m_DepthFilterRT, depthFilterRT, m_ParticleRenderMat, 1);   // vertical filter
                m_ParticleRenderCmd.SetGlobalTexture("_FilterSource", depthFilterRT);
                m_ParticleRenderCmd.Blit(depthFilterRT, m_DepthFilterRT, m_ParticleRenderMat, 2);   // horizontal filter
            }
            m_ParticleRenderMat.SetFloat("SampleDelta", 0.5f);           // up sample 原像素大小一半采样间距
            m_ParticleRenderCmd.SetGlobalTexture("_SampleSource", m_DepthFilterRT);
            m_ParticleRenderCmd.Blit(m_DepthFilterRT, m_DepthRT, m_ParticleRenderMat, 5);

            // normal map
            m_ParticleRenderCmd.GetTemporaryRT(m_NormalMapRT, -1, -1, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBFloat);
            m_ParticleRenderCmd.Blit(m_DepthRT, m_NormalMapRT, m_ParticleRenderMat, 3);

            // Volume restoration
            m_MatPropBlock.SetFloat("_Aspect", (float)Screen.width / Screen.height);
            m_ParticleRenderCmd.GetTemporaryRT(m_VolumeRT, -1, -1, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBFloat);
            m_ParticleRenderCmd.SetRenderTarget(m_VolumeRT);
            m_ParticleRenderCmd.ClearRenderTarget(true, true, Color.black);
            m_ParticleRenderCmd.DrawProcedural(
                Matrix4x4.identity, m_ParticleRenderMat, 4, MeshTopology.Points, m_GridNum, 1, m_MatPropBlock);

            // Volume Gauss Filter
            m_ParticleRenderCmd.GetTemporaryRT(m_VolumeFilterRT, Screen.width / 2, Screen.height / 2, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBFloat);
            m_ParticleRenderCmd.GetTemporaryRT(volumeFilterRT, Screen.width / 2, Screen.height / 2, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBFloat);
            m_ParticleRenderMat.SetFloat("SampleDelta", 1.0f);           // down sample 原像素大小一半采样间距
            m_ParticleRenderCmd.SetGlobalTexture("_SampleSource", m_VolumeRT);
            m_ParticleRenderCmd.Blit(m_VolumeRT, m_VolumeFilterRT, m_ParticleRenderMat, 5);
            for (int i = 0; i < 3; ++i) {
                m_ParticleRenderMat.SetFloat("_FilterRad", i + 1.0f);
                m_ParticleRenderCmd.SetGlobalTexture("_FilterSource", m_VolumeFilterRT);
                m_ParticleRenderCmd.Blit(m_VolumeFilterRT, volumeFilterRT, m_ParticleRenderMat, 1);   // vertical filter
                m_ParticleRenderCmd.SetGlobalTexture("_FilterSource", volumeFilterRT);
                m_ParticleRenderCmd.Blit(volumeFilterRT, m_VolumeFilterRT, m_ParticleRenderMat, 2);   // horizontal filter
            }
            m_ParticleRenderMat.SetFloat("SampleDelta", 1.0f);           // up sample 原像素大小一半采样间距
            m_ParticleRenderCmd.SetGlobalTexture("_SampleSource", m_VolumeFilterRT);
            m_ParticleRenderCmd.Blit(m_VolumeFilterRT, m_VolumeRT, m_ParticleRenderMat, 5);

            // light attenuation
            m_ParticleRenderCmd.GetTemporaryRT(m_LightAttenuationRT, -1, -1, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBFloat);
            m_ParticleRenderCmd.SetGlobalTexture("_VolumeTex", m_VolumeRT);
            m_ParticleRenderCmd.Blit(m_VolumeRT, m_LightAttenuationRT, m_ParticleRenderMat, 6);

            m_ParticleRenderCmd.Blit(m_LightAttenuationRT, BuiltinRenderTextureType.CameraTarget);
            m_MainCamera.AddCommandBuffer(CameraEvent.BeforeImageEffects, m_ParticleRenderCmd);

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
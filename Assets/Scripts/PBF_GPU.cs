using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using PositionBasedFluid.DataStructure;


namespace PositionBasedFluid {
    public class PBF_GPU : MonoBehaviour {

        const int SORT_BLOCK_SIZE = 512;
        const int PBF_BLOCK_SIZE = 512;

        int m_ParticleNum;
        AABB m_Border;
        float m_Poly6Coeff;
        float m_SpikyCoeff;
        float m_GridH;          // should be 2H
        Vector3Int m_GridDim;   // grid dimension
        int m_GridCellNum;      // gird cell num ceiling to multiples of PBF_BLOCK_SIZE
        float m_WQ;             // dominator of s_corr

        Particle[] m_ParticleArray;

        int m_SortKernel;       // CompareAndExchange in sort CS
        int m_UpdateKernel;     // Update in PBF CS
        int m_Hash2GridKernel;
        int m_ClearGridBufferKernel;
        int m_BuildGridBufferKernel;
        int m_ReorderParticleBufferKernel;
        int m_ComputeLambdaKernel;
        int m_ComputeDeltaPAndCollisionKernel;
        int m_UpdatePosKernel;
        int m_UpdateVelKernel;
        int m_ComputeVorticityAndDeltaVKernel;
        int m_ApplyForceAndUpdatePVKernel;

        ComputeBuffer m_ParticleBuffer_A;       // need to rearrange particle seq from a buffer to another
        ComputeBuffer m_ParticleBuffer_B;
        ComputeBuffer m_GridParticlePair_A;     // int2[grid idx, particle idx]
        ComputeBuffer m_GridParticlePair_B;
        ComputeBuffer m_GridBuffer;             // int2[start idx, end idx]

        ComputeBuffer m_SortedGridParticlePair;

        public enum Mode { NUM_1k, NUM_4k, NUM_8k, NUM_16k, NUM_32k, NUM_65k, NUM_130k, NUM_260k };
        [SerializeField]
        [Header("Particle Num")]
        public Mode m_Mode = Mode.NUM_8k;
        [Header("Fluid Domain")]
        public Vector3 m_BorderMin, m_BorderMax;
        public bool showGrid = true;
        [Header("Gravity")]
        public Vector3 m_Gravity = new Vector3(0, -9.8f, 0);
        [Header("dt")]
        public float m_dt = 0.008f;
        [Header("Iterations")]
        public int m_IterNum = 10;

        [Header("Rest Density")]
        public float REST_DENSITY = 1.0f;
        [Header("Grid cell width")]
        public float H = 1.2f;
        [Header("S_corr k coeff")]
        public float K = 0.1f;
        [Header("S_coor power")]
        public int N = 4;
        [Header("S_coor delta q magnitude")]
        [Range(0.0f, 1.0f)]
        public float Q_MAG = 0.3f;
        [Header("Lambda Epsilon ")]
        public float EPSILON_LAMBDA = 150.0f;
        [Header("Vorticity Epsilon")]
        public float EPSILON_VORTICITY = 0.1f;
        [Header("Viscosity C")]
        public float C_VISCOSITY = 0.01f;

        public ComputeShader m_PBFCS;
        public ComputeShader m_SortCS;

        void Start() {
            InitPrivateVars();
            InitComputeBuffer();
            InitComputeShader();
            InitParticles();

            Debug.Log(1.0f / m_WQ);
        }

        void Update() {
            PBFUpdate();
            // DebugUpdate();
        }

        void OnDestroy() {
            DestroyComputeBuffer();
        }

        void OnDrawGizmos() {
            // Gizmos.DrawWireCube(m_Border.GetCenter(), m_Border.GetRange());
            if (showGrid) {
                Gizmos.color = Color.blue;
                for (int i = 0; i < m_GridDim.y; ++i) {
                    for (int j = 0; j < m_GridDim.z; ++j) {
                        Gizmos.DrawLine(new Vector3(m_BorderMin.x, i * m_GridH, j * m_GridH), new Vector3(m_BorderMax.x, i * m_GridH, j * m_GridH));
                    }
                }
                for (int i = 0; i < m_GridDim.x; ++i) {
                    for (int j = 0; j < m_GridDim.z; ++j) {
                        Gizmos.DrawLine(new Vector3(i * m_GridH, m_BorderMin.y, j * m_GridH), new Vector3(i * m_GridH, m_BorderMax.y, j * m_GridH));
                    }
                }
                for (int i = 0; i < m_GridDim.x; ++i) {
                    for (int j = 0; j < m_GridDim.y; ++j) {
                        Gizmos.DrawLine(new Vector3(i * m_GridH, j * m_GridH, m_BorderMin.z), new Vector3(i * m_GridH, j * m_GridH, m_BorderMax.z));
                    }
                }
            }
        }

        void InitPrivateVars() {
            switch (m_Mode) {
                case Mode.NUM_1k:
                    m_ParticleNum = 1024; break;
                case Mode.NUM_4k:
                    m_ParticleNum = 4096; break;
                case Mode.NUM_8k:
                    m_ParticleNum = 8192; break;
                case Mode.NUM_16k:
                    m_ParticleNum = 16384; break;
                case Mode.NUM_32k:
                    m_ParticleNum = 32768; break;
                case Mode.NUM_65k:
                    m_ParticleNum = 65530; break;
                case Mode.NUM_130k:
                    m_ParticleNum = 131072; break;
                case Mode.NUM_260k:
                    m_ParticleNum = 262144; break;
                default:
                    m_ParticleNum = 8192; break;
            }

            m_Border = new AABB(m_BorderMin, m_BorderMax);
            m_Poly6Coeff = 315.0f / (64.0f * Mathf.PI * Mathf.Pow(H, 9));
            m_SpikyCoeff = -45.0f / (Mathf.PI * Mathf.Pow(H, 6));
            m_GridH = 2 * H;
            m_GridDim = new Vector3Int(
                Mathf.CeilToInt(m_Border.GetXAxis() / m_GridH),
                Mathf.CeilToInt(m_Border.GetYAxis() / m_GridH),
                Mathf.CeilToInt(m_Border.GetZAxis() / m_GridH));
            m_GridCellNum = Mathf.CeilToInt(m_GridDim.x * m_GridDim.y * m_GridDim.z / (float)PBF_BLOCK_SIZE) * PBF_BLOCK_SIZE;
            m_WQ = m_Poly6Coeff * Mathf.Pow(H * H - Q_MAG * Q_MAG * H * H, 3);
        }

        void InitComputeBuffer() {
            m_ParticleBuffer_A = new ComputeBuffer(m_ParticleNum, Marshal.SizeOf(typeof(Particle)));
            m_ParticleBuffer_B = new ComputeBuffer(m_ParticleNum, Marshal.SizeOf(typeof(Particle)));
            m_GridParticlePair_A = new ComputeBuffer(m_ParticleNum, Marshal.SizeOf(typeof(Vector2Int)));
            m_GridParticlePair_B = new ComputeBuffer(m_ParticleNum, Marshal.SizeOf(typeof(Vector2Int)));
            m_GridBuffer = new ComputeBuffer(m_GridCellNum, Marshal.SizeOf(typeof(Vector2Int)));
        }

        void DestroyComputeBuffer() {
            if (m_ParticleBuffer_A != null) {
                m_ParticleBuffer_A.Release();
                m_ParticleBuffer_A = null;
            }
            if (m_ParticleBuffer_B != null) {
                m_ParticleBuffer_B.Release();
                m_ParticleBuffer_B = null;
            }
            if (m_GridParticlePair_A != null) {
                m_GridParticlePair_A.Release();
                m_GridParticlePair_A = null;
            }
            if (m_GridParticlePair_B != null) {
                m_GridParticlePair_B.Release();
                m_GridParticlePair_B = null;
            }
            if (m_GridBuffer != null) {
                m_GridBuffer.Release();
                m_GridBuffer = null;
            }
        }

        void InitComputeShader() {
            // PBF CS
            m_UpdateKernel = m_PBFCS.FindKernel("Update");
            m_Hash2GridKernel = m_PBFCS.FindKernel("Hash2Grid");
            m_ClearGridBufferKernel = m_PBFCS.FindKernel("ClearGridBuffer");
            m_BuildGridBufferKernel = m_PBFCS.FindKernel("BuildGridBuffer");
            m_ReorderParticleBufferKernel = m_PBFCS.FindKernel("ReorderParticleBuffer");
            m_ComputeLambdaKernel = m_PBFCS.FindKernel("ComputeLambda");
            m_ComputeDeltaPAndCollisionKernel = m_PBFCS.FindKernel("ComputeDeltaPAndCollision");
            m_UpdatePosKernel = m_PBFCS.FindKernel("UpdatePos");
            m_UpdateVelKernel = m_PBFCS.FindKernel("UpdateVel");
            m_ComputeVorticityAndDeltaVKernel = m_PBFCS.FindKernel("ComputeVorticityAndDeltaV");
            m_ApplyForceAndUpdatePVKernel = m_PBFCS.FindKernel("ApplyForceAndUpdatePV");

            m_PBFCS.SetInt("_ParticleNum", m_ParticleNum);
            m_PBFCS.SetFloat("_Poly6Coeff", m_Poly6Coeff);
            m_PBFCS.SetFloat("_SpickyCoeff", m_SpikyCoeff);
            m_PBFCS.SetFloat("_GridH", m_GridH);
            m_PBFCS.SetInts("_GridDim", new int[3]{ m_GridDim.x, m_GridDim.y, m_GridDim.z });
            m_PBFCS.SetFloat("_WQInv", 1.0f / m_WQ);
            m_PBFCS.SetFloats("_BorderMin", new float[3] { m_BorderMin.x, m_BorderMin.y, m_BorderMin.z });
            m_PBFCS.SetFloats("_BorderMax", new float[3] { m_BorderMax.x, m_BorderMax.y, m_BorderMax.z });
            m_PBFCS.SetFloats("_Gravity", new float[3] { m_Gravity.x, m_Gravity.y, m_Gravity.z });
            m_PBFCS.SetFloat("_dt", m_dt);
            m_PBFCS.SetFloat("_dtInv", 1.0f / m_dt);
            m_PBFCS.SetFloat("_RestDensity", REST_DENSITY);
            m_PBFCS.SetFloat("_H", H);
            m_PBFCS.SetFloat("_K", K);
            m_PBFCS.SetInt("_N", N);
            m_PBFCS.SetFloat("_EpsilonLambda", EPSILON_LAMBDA);
            m_PBFCS.SetFloat("_EpsilonVorticity", EPSILON_VORTICITY);
            m_PBFCS.SetFloat("_CViscosity", C_VISCOSITY);

            // sort CS
            m_SortKernel = m_SortCS.FindKernel("CompareAndExchange");
        }

        void InitParticles() {
            m_ParticleArray = new Particle[m_ParticleNum];
            AABB waterDomain = new AABB(m_BorderMin + 0.1f * m_Border.GetRange(), m_BorderMax - 0.1f * m_Border.GetRange());
            // AABB waterDomain = m_Border;
            // part particles with mass 2
            for (int i = 0; i < m_ParticleNum / 4; ++i) {
                Vector3 pos = new Vector3(
                    waterDomain.minPos.x + Random.value * waterDomain.GetRange().x,
                    waterDomain.minPos.y + Random.value * waterDomain.GetRange().y,
                    waterDomain.minPos.z + Random.value * waterDomain.GetRange().z);
                m_ParticleArray[i] = new Particle(pos, m_Gravity, 1);
            }
            // another half particles with mass 1
            for (int i = m_ParticleNum / 4; i < m_ParticleNum; ++i) {
                Vector3 pos = new Vector3(
                    waterDomain.minPos.x + Random.value * waterDomain.GetRange().x,
                    waterDomain.minPos.y + Random.value * waterDomain.GetRange().y,
                    waterDomain.minPos.z + Random.value * waterDomain.GetRange().z);
                m_ParticleArray[i] = new Particle(pos, m_Gravity, 1);
            }
            m_ParticleBuffer_A.SetData(m_ParticleArray);
        }

        ComputeBuffer GPUSort() {
            int sortNum = m_ParticleNum;
            ComputeBuffer input = m_GridParticlePair_A;
            ComputeBuffer output = m_GridParticlePair_B;

            for (uint levelMask = 0x10; levelMask <= m_ParticleNum; levelMask <<= 1) {
                m_SortCS.SetInt("_LevelMask", (int)levelMask);
                for (uint level = levelMask >> 1; level > 0; level >>= 1) {
                    m_SortCS.SetInt("_Level", (int)level);
                    m_SortCS.SetBuffer(m_SortKernel, "_Input", input);
                    m_SortCS.SetBuffer(m_SortKernel, "_Output", output);
                    m_SortCS.Dispatch(m_SortKernel, sortNum / SORT_BLOCK_SIZE, 1, 1);

                    // swap buffer
                    ComputeBuffer tmp = input;
                    input = output;
                    output = tmp;
                }
            }

            return input;       // output swap to input after the last exchange
        }

        void DebugUpdate() {
            int kernel = m_PBFCS.FindKernel("DebugKernel");
            m_PBFCS.SetBuffer(kernel, "_ParticleBufferUnsorted", m_ParticleBuffer_A);
            m_PBFCS.SetBuffer(kernel, "_ParticleBufferSorted", m_ParticleBuffer_B);
            m_PBFCS.Dispatch(kernel, m_ParticleNum / PBF_BLOCK_SIZE, 1, 1);

            // swap buffer
            ComputeBuffer tmp = m_ParticleBuffer_A;
            m_ParticleBuffer_A = m_ParticleBuffer_B;
            m_ParticleBuffer_B = tmp;
        }

        void PBFUpdate() {
            // update velocity and new position
            m_PBFCS.SetBuffer(m_UpdateKernel, "_ParticleBufferUnsorted", m_ParticleBuffer_A);
            m_PBFCS.Dispatch(m_UpdateKernel, m_ParticleNum / PBF_BLOCK_SIZE, 1, 1);

            // hash particle to grid cell
            m_PBFCS.SetBuffer(m_Hash2GridKernel, "_ParticleBufferUnsorted", m_ParticleBuffer_A);
            m_PBFCS.SetBuffer(m_Hash2GridKernel, "_GridParticlePair", m_GridParticlePair_A);
            m_PBFCS.Dispatch(m_Hash2GridKernel, m_ParticleNum / PBF_BLOCK_SIZE, 1, 1);

            // clear grid buffer
            m_PBFCS.SetBuffer(m_ClearGridBufferKernel, "_GridBuffer", m_GridBuffer);
            m_PBFCS.Dispatch(m_ClearGridBufferKernel, m_GridCellNum / PBF_BLOCK_SIZE, 1, 1);

            // sort
            ComputeBuffer sortedBuffer = GPUSort();
            m_SortedGridParticlePair = sortedBuffer;

            // build grid buffer
            m_PBFCS.SetBuffer(m_BuildGridBufferKernel, "_GridParticlePair", sortedBuffer);
            m_PBFCS.SetBuffer(m_BuildGridBufferKernel, "_GridBuffer", m_GridBuffer);
            m_PBFCS.Dispatch(m_BuildGridBufferKernel, m_ParticleNum / PBF_BLOCK_SIZE, 1, 1);

            // reorder particle buffer
            m_PBFCS.SetBuffer(m_ReorderParticleBufferKernel, "_GridParticlePair", sortedBuffer);
            m_PBFCS.SetBuffer(m_ReorderParticleBufferKernel, "_ParticleBufferUnsorted", m_ParticleBuffer_A);
            m_PBFCS.SetBuffer(m_ReorderParticleBufferKernel, "_ParticleBufferSorted", m_ParticleBuffer_B);
            m_PBFCS.Dispatch(m_ReorderParticleBufferKernel, m_ParticleNum / PBF_BLOCK_SIZE, 1, 1);

            // PBD start
            for (int i = 0; i < m_IterNum; ++i) {
                // compute lambda
                m_PBFCS.SetBuffer(m_ComputeLambdaKernel, "_ParticleBufferSorted", m_ParticleBuffer_B);
                m_PBFCS.SetBuffer(m_ComputeLambdaKernel, "_GridBuffer", m_GridBuffer);
                m_PBFCS.Dispatch(m_ComputeLambdaKernel, m_ParticleNum / PBF_BLOCK_SIZE, 1, 1);

                // compute deltaP and deal with collision
                m_PBFCS.SetBuffer(m_ComputeDeltaPAndCollisionKernel, "_ParticleBufferSorted", m_ParticleBuffer_B);
                m_PBFCS.SetBuffer(m_ComputeDeltaPAndCollisionKernel, "_GridBuffer", m_GridBuffer);
                m_PBFCS.Dispatch(m_ComputeDeltaPAndCollisionKernel, m_ParticleNum / PBF_BLOCK_SIZE, 1, 1);

                // update position
                m_PBFCS.SetBuffer(m_UpdatePosKernel, "_ParticleBufferSorted", m_ParticleBuffer_B);
                m_PBFCS.Dispatch(m_UpdatePosKernel, m_ParticleNum / PBF_BLOCK_SIZE, 1, 1);
            }

            // update velocity
            m_PBFCS.SetBuffer(m_UpdateVelKernel, "_ParticleBufferSorted", m_ParticleBuffer_B);
            m_PBFCS.Dispatch(m_UpdateVelKernel, m_ParticleNum / PBF_BLOCK_SIZE, 1, 1);

            // compute vorticity and deltaV (vorticity confinement and viscosity)
            m_PBFCS.SetBuffer(m_ComputeVorticityAndDeltaVKernel, "_ParticleBufferSorted", m_ParticleBuffer_B);
            m_PBFCS.SetBuffer(m_ComputeVorticityAndDeltaVKernel, "_GridBuffer", m_GridBuffer);
            m_PBFCS.Dispatch(m_ComputeVorticityAndDeltaVKernel, m_ParticleNum / PBF_BLOCK_SIZE, 1, 1);

            // apply force and update Position Velocity
            m_PBFCS.SetBuffer(m_ApplyForceAndUpdatePVKernel, "_ParticleBufferSorted", m_ParticleBuffer_B);
            m_PBFCS.SetBuffer(m_ApplyForceAndUpdatePVKernel, "_GridBuffer", m_GridBuffer);
            m_PBFCS.Dispatch(m_ApplyForceAndUpdatePVKernel, m_ParticleNum / PBF_BLOCK_SIZE, 1, 1);

            // swap buffer
            ComputeBuffer tmp = m_ParticleBuffer_A;
            m_ParticleBuffer_A = m_ParticleBuffer_B;
            m_ParticleBuffer_B = tmp;
        }

        public ComputeBuffer GetBuffer() {
            return m_ParticleBuffer_A;
        }

        public int GetParticleNum() {
            return m_ParticleNum;
        }

        public Vector3Int GetGridDim() {
            return m_GridDim;
        }

        public ComputeBuffer GetSortedGridParticlePair() {
            return m_SortedGridParticlePair;
        }

        public ComputeBuffer GetSortedParticleBuffer() {
            return m_ParticleBuffer_B;
        }

        public ComputeBuffer GetGridBuffer() {
            return m_GridBuffer;
        }
    }
}


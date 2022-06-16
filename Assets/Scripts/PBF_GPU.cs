using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using PositionBasedFluid.DataStructure;


namespace PositionBasedFluid {
    public class PBF_GPU : MonoBehaviour {

        const int SORT_BLOCK_SIZE = 64;
        const int PBF_BLOCK_SIZE = 64;

        int m_ParticleNum;
        int m_RigbodyNum;
        int m_RigbodyParticleNum;
        AABB m_Border;
        float m_Poly6Coeff;
        float m_SpikyCoeff;
        float m_GridH;          // should be 2H
        Vector3Int m_GridDim;   // grid dimension
        int m_GridCellNum;      // gird cell num ceiling to multiples of PBF_BLOCK_SIZE
        float m_WQ;             // dominator of s_corr

        int m_SimParticleNum = 0;           // 喷溅粒子数目 应该为PBF_BLOCK_SIZE的整数倍
        int m_SimUnitParticleNum;       // 每次喷溅的粒子数目
        float m_SplashInterval;             // 两次喷溅之间的时间（秒
        float m_SimTime = 0;                // 模拟总时间 dt*iter

        Particle[] m_ParticleArray;                 // 模拟的粒子 包括刚体粒子
        RigidbodyData[] m_Rigidbodys;               // 刚体总体信息
        RigidbodyParticle[] m_RigidbodyParticles;   // 刚体粒子
        Matrix4x4[] m_RigbodyLocal2World;           // matrix of static rig from CPU to GPU

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
        int m_InitRigbodySumBorderKernel;
        int m_AddBarycenterKernel;
        int m_UpdateRigbodySumBorderKernel;
        int m_ComputeBarycenterKernel;
        int m_UpdateRWorldAndComputeMatAKernel;
        int m_AddMatAKernel;
        int m_ComputeRotationAndLocal2WorldMatKernel;
        int m_UpdateStaticLocal2WorldKernel;
        int m_UpdateRigbodyParticlePosKernel;

        ComputeBuffer m_ParticleBuffer_A;           // need to rearrange particle seq from a buffer to another
        ComputeBuffer m_ParticleBuffer_B;
        ComputeBuffer m_GridParticlePair_A;         // int2[grid idx, particle idx]
        ComputeBuffer m_GridParticlePair_B;
        ComputeBuffer m_GridBuffer;                 // int2[start idx, end idx]
        ComputeBuffer m_RigidbodyDataBuffer;        // 刚体总体信息
        ComputeBuffer m_RigidbodyParticleBuffer;    // 刚体粒子信息
        ComputeBuffer m_Local2WorldBuffer;          // buffer store local2world matrix

        ComputeBuffer m_SortedGridParticlePair;

        public enum Mode { NUM_1k, NUM_4k, NUM_8k, NUM_16k, NUM_32k, NUM_65k, NUM_130k, NUM_260k };
        public enum InitMode { PLACE, SPLASH };
        [SerializeField]
        [Header("Particle Num")]
        public Mode m_Mode = Mode.NUM_8k;
        [Header("Init Mode")]
        public InitMode m_InitMode = InitMode.PLACE;
        [Header("Fluid Domain")]
        public Vector3 m_BorderMin, m_BorderMax;
        public bool showGrid = true;
        public bool showBorder = true;
        [Header("Gravity")]
        public Vector3 m_Gravity = new Vector3(0, -9.8f, 0);
        [Header("dt")]
        public float m_dt = 0.008f;
        [Header("Iterations")]
        public int m_IterNum = 10;
        [Header("Splash Config")]
        public GameObject m_SplashPosture;
        public float m_SplashRadius = 6;
        public float m_SplashSpeed = 5;         // 粒子沿着forward喷射速度（秒

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

        public RigidbodyManager m_RigidbodyManager;

        void Start() {
            // 首先初始化RigidbodyManager
            if (m_RigidbodyManager != null) {
                m_RigidbodyManager.Init();
                m_RigidbodyManager.GetRigbodyParticleArray(out m_Rigidbodys, out m_RigidbodyParticles);
            }

            InitPrivateVars();
            InitComputeBuffer();
            InitComputeShader();
            InitParticles();

            Debug.Log(1.0f / m_WQ);
            Debug.Log(0.0f * float.MaxValue * 2.0f);
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
            if (showBorder) {
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(0.5f * (m_BorderMin + m_BorderMax), m_BorderMax - m_BorderMin);
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

            m_SimTime = 0;
            m_SimParticleNum = m_ParticleNum;
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

            m_RigbodyNum = (m_Rigidbodys == null) ? 0 : m_Rigidbodys.Length;
            m_RigbodyParticleNum = (m_RigidbodyParticles == null) ? 0 : m_RigidbodyParticles.Length;
        }

        void InitComputeBuffer() {
            Vector2Int[] tmpArray = new Vector2Int[m_ParticleNum];
            for (int i = 0; i < m_ParticleNum; ++i) {
                // 针对喷溅粒子多余粒子排序问题 需要把无关粒子列于array右侧 因此gridIdx需要初始化为最大
                tmpArray[i] = new Vector2Int(m_GridCellNum, -1);
            }

            m_ParticleBuffer_A = new ComputeBuffer(m_ParticleNum, Marshal.SizeOf(typeof(Particle)));
            m_ParticleBuffer_B = new ComputeBuffer(m_ParticleNum, Marshal.SizeOf(typeof(Particle)));
            m_GridParticlePair_A = new ComputeBuffer(m_ParticleNum, Marshal.SizeOf(typeof(Vector2Int)));
            m_GridParticlePair_A.SetData(tmpArray);
            m_GridParticlePair_B = new ComputeBuffer(m_ParticleNum, Marshal.SizeOf(typeof(Vector2Int)));
            m_GridParticlePair_B.SetData(tmpArray);
            m_GridBuffer = new ComputeBuffer(m_GridCellNum, Marshal.SizeOf(typeof(Vector2Int)));
        
            if (m_RigbodyNum != 0) {
                m_RigidbodyDataBuffer = new ComputeBuffer(m_RigbodyNum, Marshal.SizeOf(typeof(RigidbodyData)));
                m_RigidbodyDataBuffer.SetData(m_Rigidbodys);

                m_RigbodyLocal2World = new Matrix4x4[m_RigbodyNum];
                for (int i = 0; i < m_RigbodyNum; ++i) {
                    m_RigbodyLocal2World[i] = Matrix4x4.identity;
                }
                m_Local2WorldBuffer = new ComputeBuffer(m_RigbodyNum, Marshal.SizeOf(typeof(Matrix4x4)));
                m_Local2WorldBuffer.SetData(m_RigbodyLocal2World);
            }
            else {
                m_RigidbodyDataBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(RigidbodyData)));
                m_Local2WorldBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(Matrix4x4)));
            }
            if (m_RigbodyParticleNum != 0) {
                m_RigidbodyParticleBuffer = new ComputeBuffer(m_RigbodyParticleNum, Marshal.SizeOf(typeof(RigidbodyParticle)));
                m_RigidbodyParticleBuffer.SetData(m_RigidbodyParticles);
            }
            else {
                m_RigidbodyParticleBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(RigidbodyParticle)));
            }
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
            if (m_RigidbodyDataBuffer != null) {
                m_RigidbodyDataBuffer.Release();
                m_RigidbodyDataBuffer = null;
            }
            if (m_RigidbodyParticleBuffer != null) {
                m_RigidbodyParticleBuffer.Release();
                m_RigidbodyParticleBuffer = null;
            }
            if (m_Local2WorldBuffer != null) {
                m_Local2WorldBuffer.Release();
                m_Local2WorldBuffer = null;
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
            m_InitRigbodySumBorderKernel = m_PBFCS.FindKernel("InitRigbodySumBorder");
            m_AddBarycenterKernel = m_PBFCS.FindKernel("AddBarycenter");
            m_UpdateRigbodySumBorderKernel = m_PBFCS.FindKernel("UpdateRigbodySumBorder");
            m_ComputeBarycenterKernel = m_PBFCS.FindKernel("ComputeBarycenter");
            m_UpdateRWorldAndComputeMatAKernel = m_PBFCS.FindKernel("UpdateRWorldAndComputeMatA");
            m_AddMatAKernel = m_PBFCS.FindKernel("AddMatA");
            m_ComputeRotationAndLocal2WorldMatKernel = m_PBFCS.FindKernel("ComputeRotationAndLocal2WorldMat");
            m_UpdateStaticLocal2WorldKernel = m_PBFCS.FindKernel("UpdateStaticLocal2World");
            m_UpdateRigbodyParticlePosKernel = m_PBFCS.FindKernel("UpdateRigbodyParticlePos");

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
            m_PBFCS.SetInt("_SimParticleNum", m_ParticleNum);

            // sort CS
            m_SortKernel = m_SortCS.FindKernel("CompareAndExchange");
        }

        void InitParticles() {
            m_ParticleArray = new Particle[m_ParticleNum];
            int fluidParticleStartIdx = 0;

            // 先初始化刚体粒子
            if (m_RigbodyParticleNum < m_ParticleNum) {
                fluidParticleStartIdx = m_RigbodyParticleNum;
                for (int i = 0; i < m_RigbodyParticleNum; ++i) {
                    m_ParticleArray[i] = new Particle(
                        m_RigidbodyParticles[i].posWorld,
                        m_Gravity,
                        m_Rigidbodys[m_RigidbodyParticles[i].rigbodyIdx].mass,
                        i);
                }
            }

            // 再初始化流体粒子
            // 喷溅模式全体粒子初始化在某圆面上 初始速度一样
            // 放置模式全体粒子初始化在domain内的正方体中
            if (m_InitMode == InitMode.PLACE) {
                AABB waterDomain = new AABB(m_BorderMin + 0.1f * m_Border.GetRange(), m_BorderMax - 0.1f * m_Border.GetRange());
                for (int i = fluidParticleStartIdx; i < m_ParticleNum; ++i) {
                    Vector3 pos = new Vector3(
                        waterDomain.minPos.x + Random.value * waterDomain.GetRange().x,
                        waterDomain.minPos.y + Random.value * waterDomain.GetRange().y,
                        waterDomain.minPos.z + Random.value * waterDomain.GetRange().z);
                    m_ParticleArray[i] = new Particle(pos, m_Gravity, 1);
                }
            }
            else if (m_InitMode == InitMode.SPLASH) {
                Vector3 splashForward = m_SplashPosture.transform.forward;
                float restH = 0.5f * m_GridH;
                float r = Mathf.Max(m_SplashRadius, restH);
                int dim = Mathf.FloorToInt(r / restH);
                // 计算一个面需要的粒子数
                List<Vector3> faceP = new List<Vector3>();
                for (int y = -dim; y <= dim; ++y) {
                    for (int x = -dim; x <= dim; ++x) {
                        Vector2 p = new Vector2(x, y) * restH;
                        if (p.magnitude <= r) {
                            faceP.Add(m_SplashPosture.transform.TransformPoint(new Vector3(x, y, 0) * restH));
                        }
                    }
                }
                int facePNum = 0, layerNum = 0, remainder = 0;
                // 截面粒子数小于simP的最小单位
                if (faceP.Count <= PBF_BLOCK_SIZE) {
                    facePNum = faceP.Count;
                    layerNum = Mathf.CeilToInt(PBF_BLOCK_SIZE / (float)facePNum);
                    remainder = PBF_BLOCK_SIZE % facePNum;
                }
                else {
                    facePNum = faceP.Count / PBF_BLOCK_SIZE * PBF_BLOCK_SIZE;
                    layerNum = 1;
                    remainder = facePNum;
                }
                m_SimUnitParticleNum = facePNum * (layerNum - 1) + remainder;
                m_SplashInterval = 0.5f * layerNum * restH / m_SplashSpeed;
                // 初始化喷溅粒子
                int i = fluidParticleStartIdx;
                // 添加padding粒子
                int paddingNum = PBF_BLOCK_SIZE - m_RigbodyParticleNum % PBF_BLOCK_SIZE;
                for (int j = 0; j < paddingNum; ++j) {
                    Vector3 pos = new Vector3(
                       m_Border.minPos.x + Random.value * m_Border.GetRange().x,
                       m_Border.minPos.y + Random.value * m_Border.GetRange().y,
                       m_Border.minPos.z + Random.value * m_Border.GetRange().z);
                    m_ParticleArray[i + j] = new Particle(pos, m_Gravity, 1);
                }
                i += paddingNum;
                // 喷溅粒子添加
                while (i != m_ParticleNum) {
                    for (int layer = 0; layer < layerNum - 1; ++layer) {
                        for (int pIdx = 0; pIdx < facePNum; ++pIdx) {
                            m_ParticleArray[i] = new Particle(
                                faceP[pIdx] - layer * restH * splashForward,
                                m_Gravity, 1, -1, splashForward * m_SplashSpeed);
                            if ((++i) == m_ParticleNum) {
                                goto endLoop;
                            }
                        }
                    }
                    for (int pIdx = 0; pIdx < remainder; ++pIdx) {
                        m_ParticleArray[i] = new Particle(
                            faceP[pIdx] - (layerNum - 1) * restH * splashForward,
                            m_Gravity, 1, -1, splashForward * m_SplashSpeed);
                        if ((++i) == m_ParticleNum) {
                            goto endLoop;
                        }
                    }
                }
                endLoop:;
            }
            m_ParticleBuffer_A.SetData(m_ParticleArray);
            m_ParticleBuffer_B.SetData(m_ParticleArray);    // bug fixed: 未被模拟的粒子信息如果只复制到Abuffer则会在交换过程中丢失
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

        void ShapeMatching(int simParticleNum) {
            // compute barycenter
            m_PBFCS.SetBuffer(m_InitRigbodySumBorderKernel, "_RigbodyData", m_RigidbodyDataBuffer);
            m_PBFCS.Dispatch(m_InitRigbodySumBorderKernel, m_RigbodyNum, 1, 1);

            int size = m_RigidbodyManager.GetRigbodyMaxParticleNum();
            while (size != 1) {
                int step = (size / 2 * 2 == size) ? size / 2 : (size / 2 + 1);
                m_PBFCS.SetInt("_AddStep", step);
                m_PBFCS.SetBuffer(m_AddBarycenterKernel, "_RigbodyData", m_RigidbodyDataBuffer);
                m_PBFCS.SetBuffer(m_AddBarycenterKernel, "_RigbodyParticles", m_RigidbodyParticleBuffer);
                m_PBFCS.Dispatch(m_AddBarycenterKernel, m_RigbodyNum, size / 2, 1);

                m_PBFCS.SetBuffer(m_UpdateRigbodySumBorderKernel, "_RigbodyData", m_RigidbodyDataBuffer);
                m_PBFCS.Dispatch(m_UpdateRigbodySumBorderKernel, m_RigbodyNum, 1, 1);

                size = step;
            }

            m_PBFCS.SetBuffer(m_ComputeBarycenterKernel, "_RigbodyData", m_RigidbodyDataBuffer);
            m_PBFCS.SetBuffer(m_ComputeBarycenterKernel, "_RigbodyParticles", m_RigidbodyParticleBuffer);
            m_PBFCS.Dispatch(m_ComputeBarycenterKernel, m_RigbodyNum, 1, 1);

            // compute matrix A
            m_PBFCS.SetBuffer(m_UpdateRWorldAndComputeMatAKernel, "_RigbodyData", m_RigidbodyDataBuffer);
            m_PBFCS.SetBuffer(m_UpdateRWorldAndComputeMatAKernel, "_RigbodyParticles", m_RigidbodyParticleBuffer);
            m_PBFCS.Dispatch(m_UpdateRWorldAndComputeMatAKernel, m_RigbodyParticleNum, 1, 1);

            m_PBFCS.SetBuffer(m_InitRigbodySumBorderKernel, "_RigbodyData", m_RigidbodyDataBuffer);
            m_PBFCS.Dispatch(m_InitRigbodySumBorderKernel, m_RigbodyNum, 1, 1);

            size = m_RigidbodyManager.GetRigbodyMaxParticleNum();
            while (size != 1) {
                int step = (size / 2 * 2 == size) ? size / 2 : (size / 2 + 1);
                m_PBFCS.SetInt("_AddStep", step);
                m_PBFCS.SetBuffer(m_AddMatAKernel, "_RigbodyData", m_RigidbodyDataBuffer);
                m_PBFCS.SetBuffer(m_AddMatAKernel, "_RigbodyParticles", m_RigidbodyParticleBuffer);
                m_PBFCS.Dispatch(m_AddMatAKernel, m_RigbodyNum, size / 2, 1);

                m_PBFCS.SetBuffer(m_UpdateRigbodySumBorderKernel, "_RigbodyData", m_RigidbodyDataBuffer);
                m_PBFCS.Dispatch(m_UpdateRigbodySumBorderKernel, m_RigbodyNum, 1, 1);

                size = step;
            }

            // svd and compute local2world
            m_PBFCS.SetBuffer(m_ComputeRotationAndLocal2WorldMatKernel, "_RigbodyData", m_RigidbodyDataBuffer);
            m_PBFCS.SetBuffer(m_ComputeRotationAndLocal2WorldMatKernel, "_RigbodyParticles", m_RigidbodyParticleBuffer);
            m_PBFCS.Dispatch(m_ComputeRotationAndLocal2WorldMatKernel, m_RigbodyNum, 1, 1);

            if (m_RigidbodyManager.HasStaticRigbody()) {
                StaticRigbodyUpdate();
            }
            
            // update rigbody position
            m_PBFCS.SetBuffer(m_UpdateRigbodyParticlePosKernel, "_RigbodyData", m_RigidbodyDataBuffer);
            m_PBFCS.SetBuffer(m_UpdateRigbodyParticlePosKernel, "_RigbodyParticles", m_RigidbodyParticleBuffer);
            m_PBFCS.SetBuffer(m_UpdateRigbodyParticlePosKernel, "_ParticleBufferSorted", m_ParticleBuffer_B);
            m_PBFCS.Dispatch(m_UpdateRigbodyParticlePosKernel, simParticleNum / PBF_BLOCK_SIZE, 1, 1);
        }

        // 更新刚体loacl2world
        void StaticRigbodyUpdate() {
            for (int i = 0; i < m_RigbodyNum; ++i) {
                if (m_Rigidbodys[i].isStatic == 1) {
                    m_RigbodyLocal2World[i] = m_RigidbodyManager.GetRigbodyLocal2World(i);
                }
            }
            m_Local2WorldBuffer.SetData(m_RigbodyLocal2World);

            m_PBFCS.SetBuffer(m_UpdateStaticLocal2WorldKernel, "_RigbodyData", m_RigidbodyDataBuffer);
            m_PBFCS.SetBuffer(m_UpdateStaticLocal2WorldKernel, "_Local2World", m_Local2WorldBuffer);
            m_PBFCS.Dispatch(m_UpdateStaticLocal2WorldKernel, m_RigbodyNum, 1, 1);
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
            m_SimTime += m_dt;
            int simParticleNum = m_ParticleNum;
            if (m_InitMode == InitMode.SPLASH) {
                m_SimParticleNum = Mathf.Min(Mathf.CeilToInt(m_RigbodyParticleNum / (float)PBF_BLOCK_SIZE) * PBF_BLOCK_SIZE +
                    (int)(m_SimTime / m_SplashInterval) * m_SimUnitParticleNum, m_ParticleNum);
                simParticleNum = m_SimParticleNum;
                if (m_SimParticleNum == 0) {
                    return;
                }
            }

            // update velocity and new position
            m_PBFCS.SetBuffer(m_UpdateKernel, "_ParticleBufferUnsorted", m_ParticleBuffer_A);
            m_PBFCS.Dispatch(m_UpdateKernel, simParticleNum / PBF_BLOCK_SIZE, 1, 1);

            // hash particle to grid cell
            m_PBFCS.SetBuffer(m_Hash2GridKernel, "_ParticleBufferUnsorted", m_ParticleBuffer_A);
            m_PBFCS.SetBuffer(m_Hash2GridKernel, "_GridParticlePair", m_GridParticlePair_A);
            m_PBFCS.Dispatch(m_Hash2GridKernel, simParticleNum / PBF_BLOCK_SIZE, 1, 1);

            // clear grid buffer
            m_PBFCS.SetBuffer(m_ClearGridBufferKernel, "_GridBuffer", m_GridBuffer);
            m_PBFCS.Dispatch(m_ClearGridBufferKernel, m_GridCellNum / PBF_BLOCK_SIZE, 1, 1);

            // sort
            ComputeBuffer sortedBuffer = GPUSort();
            m_SortedGridParticlePair = sortedBuffer;

            // build grid buffer
            m_PBFCS.SetBuffer(m_BuildGridBufferKernel, "_GridParticlePair", sortedBuffer);
            m_PBFCS.SetBuffer(m_BuildGridBufferKernel, "_GridBuffer", m_GridBuffer);
            m_PBFCS.Dispatch(m_BuildGridBufferKernel, simParticleNum / PBF_BLOCK_SIZE, 1, 1);

            // reorder particle buffer
            m_PBFCS.SetBuffer(m_ReorderParticleBufferKernel, "_GridParticlePair", sortedBuffer);
            m_PBFCS.SetBuffer(m_ReorderParticleBufferKernel, "_ParticleBufferUnsorted", m_ParticleBuffer_A);
            m_PBFCS.SetBuffer(m_ReorderParticleBufferKernel, "_ParticleBufferSorted", m_ParticleBuffer_B);
            m_PBFCS.Dispatch(m_ReorderParticleBufferKernel, simParticleNum / PBF_BLOCK_SIZE, 1, 1);

            // PBD start
            for (int i = 0; i < m_IterNum; ++i) {
                // compute lambda
                m_PBFCS.SetBuffer(m_ComputeLambdaKernel, "_ParticleBufferSorted", m_ParticleBuffer_B);
                m_PBFCS.SetBuffer(m_ComputeLambdaKernel, "_GridBuffer", m_GridBuffer);
                m_PBFCS.Dispatch(m_ComputeLambdaKernel, simParticleNum / PBF_BLOCK_SIZE, 1, 1);

                // compute deltaP and deal with collision
                m_PBFCS.SetBuffer(m_ComputeDeltaPAndCollisionKernel, "_ParticleBufferSorted", m_ParticleBuffer_B);
                m_PBFCS.SetBuffer(m_ComputeDeltaPAndCollisionKernel, "_GridBuffer", m_GridBuffer);
                m_PBFCS.SetBuffer(m_ComputeDeltaPAndCollisionKernel, "_RigbodyParticles", m_RigidbodyParticleBuffer);
                m_PBFCS.SetBuffer(m_ComputeDeltaPAndCollisionKernel, "_RigbodyData", m_RigidbodyDataBuffer);
                m_PBFCS.Dispatch(m_ComputeDeltaPAndCollisionKernel, simParticleNum / PBF_BLOCK_SIZE, 1, 1);

                // update position
                m_PBFCS.SetBuffer(m_UpdatePosKernel, "_ParticleBufferSorted", m_ParticleBuffer_B);
                m_PBFCS.Dispatch(m_UpdatePosKernel, simParticleNum / PBF_BLOCK_SIZE, 1, 1);
            }

            // update velocity
            m_PBFCS.SetBuffer(m_UpdateVelKernel, "_ParticleBufferSorted", m_ParticleBuffer_B);
            m_PBFCS.Dispatch(m_UpdateVelKernel, simParticleNum / PBF_BLOCK_SIZE, 1, 1);

            // compute vorticity and deltaV (vorticity confinement and viscosity)
            m_PBFCS.SetBuffer(m_ComputeVorticityAndDeltaVKernel, "_ParticleBufferSorted", m_ParticleBuffer_B);
            m_PBFCS.SetBuffer(m_ComputeVorticityAndDeltaVKernel, "_GridBuffer", m_GridBuffer);
            m_PBFCS.Dispatch(m_ComputeVorticityAndDeltaVKernel, simParticleNum / PBF_BLOCK_SIZE, 1, 1);

            // apply force and update Position Velocity
            m_PBFCS.SetBuffer(m_ApplyForceAndUpdatePVKernel, "_ParticleBufferSorted", m_ParticleBuffer_B);
            m_PBFCS.SetBuffer(m_ApplyForceAndUpdatePVKernel, "_GridBuffer", m_GridBuffer);
            m_PBFCS.SetBuffer(m_ApplyForceAndUpdatePVKernel, "_RigbodyParticles", m_RigidbodyParticleBuffer);
            m_PBFCS.Dispatch(m_ApplyForceAndUpdatePVKernel, simParticleNum / PBF_BLOCK_SIZE, 1, 1);

            // rigbody shape matching
            if (m_RigbodyNum != 0) {
                ShapeMatching(simParticleNum);
            }

            // swap buffer
            ComputeBuffer tmp = m_ParticleBuffer_A;
            m_ParticleBuffer_A = m_ParticleBuffer_B;
            m_ParticleBuffer_B = tmp;
        }

        public ComputeBuffer GetBuffer() {
            return m_ParticleBuffer_A;
        }

        public int GetParticleNum() {
            return m_SimParticleNum;
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


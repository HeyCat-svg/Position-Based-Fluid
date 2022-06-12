using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PositionBasedFluid.DataStructure;


namespace PositionBasedFluid {
    public class RigidbodyManager : MonoBehaviour {

        int m_RigbodyMaxParticleNum = 0;

        public MyRigidbody[] m_Rigidbodys;
 
        void Start() {
        
        }

        void Update() {}

        public void Init() {
            // 刚体的整个初始化顺序 MeshVoxel->MyRigidbody->RigidbodyManager
            int bodyNum = m_Rigidbodys.Length;
            for (int i = 0; i < bodyNum; ++i) {
                m_Rigidbodys[i].Init(i);
            }
        }

        public void GetRigbodyParticleArray(
            out RigidbodyData[] rigidbodys, out RigidbodyParticle[] particles) {
            
            List<RigidbodyData> bodyList = new List<RigidbodyData>();
            List<RigidbodyParticle> particleList = new List<RigidbodyParticle>();
            int startIdx = 0;
            int bodyNum = m_Rigidbodys.Length;
            for (int i = 0; i < bodyNum; ++i) {
                RigidbodyParticle[] _particles = m_Rigidbodys[i].GetRigidParticles();
                int particleNum = _particles.Length;
                m_RigbodyMaxParticleNum = (particleNum > m_RigbodyMaxParticleNum) ? particleNum : m_RigbodyMaxParticleNum;
                // 构造刚体数据
                bodyList.Add(new RigidbodyData(
                    startIdx,
                    startIdx + particleNum - 1,
                    Matrix4x4.TRS(m_Rigidbodys[i].transform.localPosition, 
                        m_Rigidbodys[i].transform.rotation, new Vector3(1, 1, 1)),
                    m_Rigidbodys[i].GetBarycenter(),
                    m_Rigidbodys[i].GetMass())); ;
                // 构造刚体粒子数据
                particleList.AddRange(_particles);
                // startIdx后移
                startIdx += particleNum;
            }

            rigidbodys = bodyList.ToArray();
            particles = particleList.ToArray();
        }

        public int GetRigbodyMaxParticleNum() {
            return m_RigbodyMaxParticleNum;
        }
    }
}


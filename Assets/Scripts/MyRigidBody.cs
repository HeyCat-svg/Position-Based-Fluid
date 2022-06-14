using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PositionBasedFluid.DataStructure;

namespace PositionBasedFluid { 
    [RequireComponent(typeof(MeshVoxel))]
    public class MyRigidbody : MonoBehaviour {

        MeshVoxel m_MeshVoxel;
        Vector3 m_Barycenter;                   // 局部空间的重心坐标
        RigidbodyParticle[] m_RigidParticles;   // 组成刚体的粒子
        int m_ParticleNum;                      // 刚体粒子数
        int m_RigbodyIdx = -1;                  // 刚体在场景中的序号

        public float m_Mass = 1.0f;
        public bool isStatic = false;
        
    
        void Start() {}

        void Update() {
        
        }

        // 参数rigbodyIdx代表场景中第几个刚体
        public void Init(int rigbodyIdx) {
            m_MeshVoxel = GetComponent<MeshVoxel>();
            if (m_MeshVoxel == null) {
                return;
            }
            // 先初始化MeshVoxel
            m_MeshVoxel.Init();
            float uniformScale = m_MeshVoxel.GetUniformScale();     // 获取mash的全局缩放

            m_RigbodyIdx = rigbodyIdx;
            // 计算局部空间的重心坐标 以及 统计粒子数目
            m_Barycenter = Vector3.zero;
            m_ParticleNum = 0;
            Voxel[] voxels = m_MeshVoxel.GetVoxels();
            int voxelNum = voxels.Length;
            for (int i = 0; i < voxelNum; ++i) {
                if (voxels[i].isInner > 1e-3) {
                    m_Barycenter += voxels[i].position;
                    m_ParticleNum++;
                }
            }
            m_Barycenter /= (float)m_ParticleNum;
            m_Barycenter *= uniformScale;       // 重心受到缩放影响
            // 构造刚体粒子数组
            m_RigidParticles = new RigidbodyParticle[m_ParticleNum];
            int curParticleIdx = 0;
            for (int i = 0; i < voxelNum; ++i) {
                if (voxels[i].isInner > 1e-3) {
                    m_RigidParticles[curParticleIdx] = new RigidbodyParticle(
                        voxels[i].position * uniformScale - m_Barycenter,     // rLocal需要经过全局缩放
                        transform.TransformPoint(voxels[i].position),
                        voxels[i].distGrad,
                        Mathf.Abs(voxels[i].distance) * uniformScale,
                        rigbodyIdx);
                    curParticleIdx++;
                }
            }
        }

        public Vector3 GetBarycenter() {
            return m_Barycenter;
        }

        public float GetMass() {
            if (isStatic) {
                return float.MaxValue;
            }
            return m_Mass;
        }

        public bool GetIsStatic() {
            return isStatic;
        }

        public RigidbodyParticle[] GetRigidParticles() {
            return m_RigidParticles;
        }
    }
}


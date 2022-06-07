using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PositionBasedFluid { 
    [RequireComponent(typeof(MeshVoxel))]
    public class MyRigidBody : MonoBehaviour {

        MeshVoxel m_MeshVoxel;
        Vector3 m_Barycenter;           // 局部空间的重心坐标

        public float m_Mass = 1.0f;
        
    
        void Start() {
            m_MeshVoxel = GetComponent<MeshVoxel>();
        }

        void Update() {
        
        }

        Vector3 GetBarycenter() {
            return Vector3.zero;
        }
    }
}


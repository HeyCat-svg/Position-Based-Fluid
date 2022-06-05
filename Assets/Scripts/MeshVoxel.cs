using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using PositionBasedFluid.DataStructure;

namespace PositionBasedFluid {

    public class MeshVoxel : MonoBehaviour {

        Accel m_AccelStruct;
        Voxel[] m_Voxels;
        Vector3Int m_VoxelDim;
        AABB m_BoundingBox;
        int m_VoxelNum = 0;
        ComputeBuffer m_VoxelBuffer;

        public Mesh m_Mesh;
        public float m_VoxelH = 0.1f;
        public Material m_VoxelRenderMat;
        
        

        void Start() {
            Init();
        }


        void Update() {

        }

        void OnRenderObject() {
            if (m_VoxelRenderMat == null) {
                return;
            }
            m_VoxelRenderMat.SetPass(0);
            m_VoxelRenderMat.SetBuffer("_Voxels", m_VoxelBuffer);
            m_VoxelRenderMat.SetInt("_VoxelNum", m_VoxelNum);
            m_VoxelRenderMat.SetVector("_VoxelDim", new Vector4(m_VoxelDim.x, m_VoxelDim.y, m_VoxelDim.z, 0));
            Graphics.DrawProceduralNow(MeshTopology.Points, m_VoxelNum);
        }

        void OnDestroy() {
            if (m_VoxelBuffer != null) {
                m_VoxelBuffer.Release();
                m_VoxelBuffer = null;
            }
        }

        void Init() {
            if (m_Mesh == null || m_VoxelRenderMat == null) {
                return;
            }
            m_AccelStruct = new Accel(m_Mesh);
            m_BoundingBox = m_AccelStruct.GetBoundingBox();
            m_VoxelDim = new Vector3Int(
                Mathf.CeilToInt(m_BoundingBox.GetXAxis() / m_VoxelH),
                Mathf.CeilToInt(m_BoundingBox.GetYAxis() / m_VoxelH),
                Mathf.CeilToInt(m_BoundingBox.GetZAxis() / m_VoxelH));
            m_Voxels = new Voxel[m_VoxelDim.x * m_VoxelDim.y * m_VoxelDim.z];

            m_VoxelNum = m_Voxels.Length;
            for (int i = 0; i < m_VoxelNum; ++i) {
                Vector3 p = VoxelCoord2VoxelPos(VoxelIdx2Coord(i));
                m_Voxels[i] = new Voxel(m_AccelStruct.CheckInnerRegion(p), p);
            }

            // init compute buffer
            m_VoxelBuffer = new ComputeBuffer(m_VoxelNum, Marshal.SizeOf(typeof(Voxel)));
            m_VoxelBuffer.SetData(m_Voxels);
        }

        Vector3Int VoxelIdx2Coord(int idx) {
            if (idx < 0 || idx >= m_VoxelNum) {
                return Vector3Int.zero;
            }
            return new Vector3Int(
                idx % m_VoxelDim.x,
                idx / m_VoxelDim.x % m_VoxelDim.y,
                idx / (m_VoxelDim.x * m_VoxelDim.y));
        }

        int VoxelCoord2Idx(Vector3Int coord) {
            if (coord.x < 0 || coord.x >= m_VoxelDim.x ||
                coord.y < 0 || coord.y >= m_VoxelDim.y ||
                coord.z < 0 || coord.z >= m_VoxelDim.z) {
                return -1;
            }
            return coord.x + coord.y * m_VoxelDim.x + coord.z * m_VoxelDim.x * m_VoxelDim.z;
        }

        Vector3 VoxelCoord2VoxelPos(Vector3Int coord) {
            if (coord.x < 0 || coord.x >= m_VoxelDim.x ||
                coord.y < 0 || coord.y >= m_VoxelDim.y ||
                coord.z < 0 || coord.z >= m_VoxelDim.z) {
                return Vector3.zero;
            }
            return m_BoundingBox.minPos + 
                new Vector3(coord.x + 0.5f, coord.y + 0.5f, coord.z + 0.5f) * m_VoxelH;
        }
    }
}



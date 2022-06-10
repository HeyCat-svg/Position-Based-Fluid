using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using PositionBasedFluid.DataStructure;

namespace PositionBasedFluid {
    [RequireComponent(typeof(MyRigidbody))]
    public class MeshVoxel : MonoBehaviour {

        bool isInit = false;
        Accel m_AccelStruct;
        Voxel[] m_Voxels;
        Vector3Int m_VoxelDim;
        AABB m_BoundingBox;
        int m_VoxelNum = 0;
        ComputeBuffer m_VoxelBuffer;

        float m_MaxDistance;
        float m_LocalVoxelH;

        public Mesh m_Mesh;
        public float m_VoxelH = 0.1f;
        public Material m_VoxelRenderMat;
        public float m_UniformScale = 1.0f;
        public bool showVoxels = false;
        public bool showGradient = false;


        void Start() {
        }

        void Update() {

        }

        void OnRenderObject() {
            if (m_VoxelRenderMat == null || !isInit) {
                return;
            }
            if (showVoxels) {
                m_VoxelRenderMat.SetPass(0);
                m_VoxelRenderMat.SetBuffer("_Voxels", m_VoxelBuffer);
                m_VoxelRenderMat.SetInt("_VoxelNum", m_VoxelNum);
                m_VoxelRenderMat.SetVector("_VoxelDim", new Vector4(m_VoxelDim.x, m_VoxelDim.y, m_VoxelDim.z, 0));
                m_VoxelRenderMat.SetFloat("_MaxDistance", m_MaxDistance);
                m_VoxelRenderMat.SetMatrix("_Local2WorldMatrix", transform.localToWorldMatrix);
                Graphics.DrawProceduralNow(MeshTopology.Points, m_VoxelNum);
            }
            if (showGradient) {
                m_VoxelRenderMat.SetPass(1);
                m_VoxelRenderMat.SetBuffer("_Voxels", m_VoxelBuffer);
                m_VoxelRenderMat.SetInt("_VoxelNum", m_VoxelNum);
                m_VoxelRenderMat.SetVector("_VoxelDim", new Vector4(m_VoxelDim.x, m_VoxelDim.y, m_VoxelDim.z, 0));
                m_VoxelRenderMat.SetMatrix("_Local2WorldMatrix", transform.localToWorldMatrix);
                Graphics.DrawProceduralNow(MeshTopology.Points, m_VoxelNum);
            }
        }

        void OnDestroy() {
            if (m_VoxelBuffer != null) {
                m_VoxelBuffer.Release();
                m_VoxelBuffer = null;
            }
        }

        public void Init() {
            if (m_Mesh == null || m_VoxelRenderMat == null) {
                return;
            }

            transform.localScale = new Vector3(m_UniformScale, m_UniformScale, m_UniformScale);
            m_LocalVoxelH = m_VoxelH / m_UniformScale;      // m_VoxelH是世界空间中的grid间隔
            m_AccelStruct = new Accel(m_Mesh);
            m_BoundingBox = m_AccelStruct.GetBoundingBox();
            m_VoxelDim = new Vector3Int(
                Mathf.CeilToInt(m_BoundingBox.GetXAxis() / m_LocalVoxelH),
                Mathf.CeilToInt(m_BoundingBox.GetYAxis() / m_LocalVoxelH),
                Mathf.CeilToInt(m_BoundingBox.GetZAxis() / m_LocalVoxelH));
            m_Voxels = new Voxel[m_VoxelDim.x * m_VoxelDim.y * m_VoxelDim.z];

            m_VoxelNum = m_Voxels.Length;
            for (int i = 0; i < m_VoxelNum; ++i) {
                Vector3 p = VoxelCoord2VoxelPos(VoxelIdx2Coord(i));
                m_Voxels[i] = new Voxel(m_AccelStruct.CheckInnerRegion(p), p);
            }

            // 计算有向距离和梯度
            ComputeDistanceField();

            // 计算debug变量
            ComputeDebugVar();

            // init compute buffer
            m_VoxelBuffer = new ComputeBuffer(m_VoxelNum, Marshal.SizeOf(typeof(Voxel)));
            m_VoxelBuffer.SetData(m_Voxels);

            isInit = true;
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
                new Vector3(coord.x + 0.5f, coord.y + 0.5f, coord.z + 0.5f) * m_LocalVoxelH;
        }

        // 暴力粗略搜索空间点到mesh的最小距离
        void ComputeDistanceField() {
            if (m_Mesh == null) {
                return;
            }
            List<Vector3> vertices = new List<Vector3>();
            m_Mesh.GetVertices(vertices);
            int[] trisArray = m_Mesh.GetTriangles(0);
            int triNum = trisArray.Length / 3;
            for (int i = 0; i < triNum; ++i) {
                Vector3 center = 1.0f / 3.0f * (
                    vertices[trisArray[3 * i]] +
                    vertices[trisArray[3 * i + 1]] +
                    vertices[trisArray[3 * i + 2]]);
                for (int j = 0; j < m_VoxelNum; ++j) {
                    float dist = (center - m_Voxels[j].position).magnitude;
                    m_Voxels[j].distGrad = (dist < m_Voxels[j].distance) ? (center - m_Voxels[j].position).normalized : m_Voxels[j].distGrad;
                    m_Voxels[j].distance = (dist < m_Voxels[j].distance) ? dist : m_Voxels[j].distance; 
                }
            }
            // 距离变成有向距离 同时应用放缩scale（不需要scale？因为grad在local2world时被放缩过了）
            for (int i = 0; i < m_VoxelNum; ++i) {
                m_Voxels[i].distance = (m_Voxels[i].isInner < 1e-3) ?
                    Mathf.Abs(m_Voxels[i].distance) : -Mathf.Abs(m_Voxels[i].distance);
                // m_Voxels[i].distance *= m_UniformScale;
            }
            //// 计算有向距离场梯度
            //for (int i = 0; i < m_VoxelNum; ++i) {
            //    Vector3Int voxelCoord = VoxelIdx2Coord(i);
            //    Vector3 distMin = Vector3.zero, distMax = Vector3.zero, dominator = Vector3.zero;       // 梯度差分的左右两端和分母
            //    for (int j = 0; j < 3; ++j) {
            //        if (voxelCoord[j] - 1 < 0) {
            //            distMin[j] = m_Voxels[i].distance;
            //            dominator[j] = 0;
            //        }
            //        else {
            //            Vector3Int coord = voxelCoord;
            //            coord[j] -= 1;
            //            distMin[j] = m_Voxels[VoxelCoord2Idx(coord)].distance;
            //            dominator[j] = m_LocalVoxelH;
            //        }
            //        if (voxelCoord[j] + 1 >= m_VoxelDim[j]) {
            //            distMax[j] = m_Voxels[i].distance;
            //            dominator[j] += 0;
            //        }
            //        else {
            //            Vector3Int coord = voxelCoord;
            //            coord[j] += 1;
            //            distMax[j] = m_Voxels[VoxelCoord2Idx(coord)].distance;
            //            dominator[j] += m_LocalVoxelH;
            //        }
            //    }
            //    // 计算差分
            //    distMax -= distMin;
            //    for (int j = 0; j < 3; ++j) {
            //        distMax[j] /= dominator[j];
            //    }
            //    m_Voxels[i].distGrad = distMax.normalized;
            //}
        }

        void ComputeDebugVar() {
            // 计算mesh内部的最大距离
            m_MaxDistance = 0;
            for (int i = 0; i < m_VoxelNum; ++i) {
                m_MaxDistance = (m_Voxels[i].distance < m_MaxDistance) ? m_Voxels[i].distance : m_MaxDistance;
            }
        }

        public Voxel[] GetVoxels() {
            return m_Voxels;
        }
    }
}



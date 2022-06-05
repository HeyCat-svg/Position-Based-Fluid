using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PositionBasedFluid.DataStructure;

namespace PositionBasedFluid {
    public class Accel {
        class Node {
            public AABB bound;
            public List<int> tris;
            public Node left = null;
            public Node right = null;

            public Node(AABB _bound, Node _left = null, Node _right = null) {
                bound = _bound;
                left = _left;
                right = _right;
            }
        }

        Mesh m_Mesh;
        Triangle[] m_MeshTriangles;
        Node m_TreeRoot = null;
        int m_MaxDepth = 0, m_LeafNum = 0, m_NodeNum = 0;
        int m_SplitTermination = 5;         // Ҷ�ӽڵ������������С�ڸ�ֵʱ���ٷ���

        void Split(Node node, int depth) {
            m_NodeNum++;
            m_MaxDepth = (depth > m_MaxDepth) ? depth : m_MaxDepth;
            if (node != null && node.tris.Count <= m_SplitTermination) {
                m_LeafNum++;
                return;
            }

            int sortAxis = -1;
            Vector3 boundRange = node.bound.GetRange();
            if (boundRange.x > boundRange.y && boundRange.x > boundRange.z) {
                sortAxis = 0;       // sort in x axis
            }
            else if (boundRange.y > boundRange.x && boundRange.y > boundRange.z) {
                sortAxis = 1;       // sort in y axis
            }
            else {
                sortAxis = 2;       // sort in x axis
            }
            node.tris.Sort((int x, int y) => {
                if (m_MeshTriangles[x].GetCenter()[sortAxis] < m_MeshTriangles[y].GetCenter()[sortAxis]) {
                    return -1;
                }
                else if (m_MeshTriangles[x].GetCenter()[sortAxis] == m_MeshTriangles[y].GetCenter()[sortAxis]) {
                    return 0;
                }
                else {
                    return 1;
                }
            });

            // �ؽ����ҽڵ�������ΰ�Χ��
            int triNum = node.tris.Count;
            Vector3 minPos = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 maxPos = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < triNum / 2; ++i) {
                AABB box = m_MeshTriangles[node.tris[i]].GetBoundingBox();
                for (int j = 0; j < 3; ++j) {
                    minPos[j] = (box.minPos[j] < minPos[j]) ? box.minPos[j] : minPos[j];
                    maxPos[j] = (box.maxPos[j] > maxPos[j]) ? box.maxPos[j] : maxPos[j];
                }
            }
            node.left = new Node(new AABB(minPos, maxPos));
            node.left.tris = new List<int>(node.tris.GetRange(0, triNum / 2));

            minPos = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            maxPos = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = triNum / 2; i < triNum; ++i) {
                AABB box = m_MeshTriangles[node.tris[i]].GetBoundingBox();
                for (int j = 0; j < 3; ++j) {
                    minPos[j] = (box.minPos[j] < minPos[j]) ? box.minPos[j] : minPos[j];
                    maxPos[j] = (box.maxPos[j] > maxPos[j]) ? box.maxPos[j] : maxPos[j];
                }
            }
            node.right = new Node(new AABB(minPos, maxPos));
            node.right.tris = new List<int>(node.tris.GetRange(triNum / 2, triNum - triNum / 2));

            node.tris.Clear();
            Split(node.left, depth + 1);
            Split(node.right, depth + 1);
        }

        // ����node�������ڵ�p����������Ƭ����
        int CheckInnerRegionHelper(Node node, Vector3 p) {
            if (node == null) {
                return 0;
            }
            // ����Χ��
            if (!node.bound.CheckCover(p)) {
                return 0;
            }
            // Ҷ�ӽڵ�
            if (node.left == null || node.right == null) {
                int triNum = node.tris.Count;
                int coverTriNum = 0;
                for (int i = 0; i < triNum; ++i) {
                    if (m_MeshTriangles[node.tris[i]].CheckCover(p)) {
                        coverTriNum++;
                    }
                }
                return coverTriNum;
            }

            return CheckInnerRegionHelper(node.left, p) + CheckInnerRegionHelper(node.right, p);
        }

        public Accel(Mesh mesh) {
            SetMesh(mesh);
        }

        public void SetMesh(Mesh mesh) {
            m_Mesh = mesh;
            Build();
        }

        public void Build() {
            if (m_Mesh == null) {
                return;
            }
            // ���������
            List<Vector3> vertices = new List<Vector3>();
            m_Mesh.GetVertices(vertices);
            int[] triangles = m_Mesh.GetTriangles(0);
            int triNum = triangles.Length / 3;
            m_MeshTriangles = new Triangle[triNum];
            for (int i = 0; i < triNum; ++i) {
                m_MeshTriangles[i] = new Triangle(
                    vertices[triangles[3 * i + 0]],
                    vertices[triangles[3 * i + 1]],
                    vertices[triangles[3 * i + 2]]);
            }
            // ����mesh��Χ��
            Vector3 minPos = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 maxPos = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            int vertexNum = vertices.Count;
            for (int i = 0; i < vertexNum; ++i) {
                for (int j = 0; j < 3; ++j) {
                    minPos[j] = Mathf.Min(minPos[j], vertices[i][j]);
                    maxPos[j] = Mathf.Max(maxPos[j], vertices[i][j]);
                }
            }
            // ���������ڵ�
            m_TreeRoot = new Node(new AABB(minPos, maxPos));
            m_TreeRoot.tris = new List<int>();
            for (int i = 0; i < triNum; ++i) {
                m_TreeRoot.tris.Add(i);
            }
            // ��ʼ����
            m_MaxDepth = 0; m_LeafNum = 0; m_NodeNum = 0;
            Split(m_TreeRoot, 1);
            Debug.Log("MaxDepth:" + m_MaxDepth + "LeafNum:" + m_LeafNum + "NodeNum:" + m_NodeNum);
        }

        public void Clear() {
            m_TreeRoot = null;  // GC���񲢲���Ҫ�ֶ��ͷ�
        }

        // ����p�Ƿ���ģ���ڲ�
        public bool CheckInnerRegion(Vector3 p) {
            int triCoverNum = CheckInnerRegionHelper(m_TreeRoot, p);
            if (triCoverNum % 2 == 0) {
                return false;
            }
            else {
                return true;
            }
        }

        public AABB GetBoundingBox() {
            if (m_TreeRoot == null) {
                return new AABB(Vector3.zero, Vector3.zero);
            }
            return m_TreeRoot.bound;
        }
    }
}
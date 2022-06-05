using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace PositionBasedFluid.DataStructure {
    public struct AABB {
        public Vector3 minPos, maxPos;

        public AABB(Vector3 _minPos, Vector3 _maxPos) {
            minPos = _minPos;
            maxPos = _maxPos;
        }

        public float GetXAxis() {
            return maxPos.x - minPos.x;
        }

        public float GetYAxis() {
            return maxPos.y - minPos.y;
        }

        public float GetZAxis() {
            return maxPos.z - minPos.z;
        }

        public Vector3 GetRange() {
            return maxPos - minPos;
        }

        public Vector3 GetCenter() {
            return 0.5f * (minPos + maxPos);
        }

        public bool CheckCover(Vector3 p, int axis = 2) {
            switch (axis) {
                case 0:             // x axis
                    if (p.y >= minPos.y && p.y <= maxPos.y &&
                        p.z >= minPos.z && p.z <= maxPos.z &&
                        p.x < maxPos.x) {
                        return true;
                    }
                    else {
                        return false;
                    }
                case 1:             // y axis
                    if (p.x >= minPos.x && p.x <= maxPos.x &&
                        p.z >= minPos.z && p.z <= maxPos.z &&
                        p.y < maxPos.y) {
                        return true;
                    }
                    else {
                        return false;
                    }
                case 2:             // z axis
                    if (p.x >= minPos.x && p.x <= maxPos.x &&
                        p.y >= minPos.y && p.y <= maxPos.y &&
                        p.z < maxPos.z) {
                        return true;
                    }
                    else {
                        return false;
                    }
            }

            return false;
        }
    }

    public struct Triangle {
        public Vector3[] points;

        public Triangle(Vector3 p0, Vector3 p1, Vector3 p2) {
            points = new Vector3[3] { p0, p1, p2 };
        }

        public Vector3 this[int index] {
            get { return points[index]; }
            set { points[index] = value; }
        }

        // ��������
        public Vector3 GetCenter() {
            Vector3 ret = Vector3.zero;
            for (int i = 0; i < 3; ++i) {
                ret += points[i];
            }
            return ret / 3.0f;
        }

        // ����AABB��Χ��
        public AABB GetBoundingBox() {
            Vector3 minPos = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 maxPos = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < 3; ++i) {
                for (int j = 0; j < 3; ++j) {
                    minPos[j] = Mathf.Min(minPos[j], points[i][j]);
                    maxPos[j] = Mathf.Max(maxPos[j], points[i][j]);
                }
            }
            return new AABB(minPos, maxPos);
        }

        public Vector3 Barycentric(Vector3 p, int axis = 2) {
            Vector2[] projTriPts = new Vector2[3];
            Vector2 projP = new Vector2();
            switch (axis) {
                case 0:
                    for (int i = 0; i < 3; ++i) {
                        projTriPts[i] = new Vector2(points[i].y, points[i].z);
                    }
                    projP = new Vector2(p.y, p.z);
                    break;
                case 1:
                    for (int i = 0; i < 3; ++i) {
                        projTriPts[i] = new Vector2(points[i].x, points[i].z);
                    }
                    projP = new Vector2(p.x, p.z);
                    break;
                case 2:
                    for (int i = 0; i < 3; ++i) {
                        projTriPts[i] = new Vector2(points[i].x, points[i].y);
                    }
                    projP = new Vector2(p.x, p.y);
                    break;
            }
            // compute barycentric
            Vector3 ret = Vector3.Cross(
                new Vector3(projTriPts[1].x - projTriPts[0].x, projTriPts[2].x - projTriPts[0].x, projTriPts[0].x - projP.x),
                new Vector3(projTriPts[1].y - projTriPts[0].y, projTriPts[2].y - projTriPts[0].y, projTriPts[0].y - projP.y));
            if (Mathf.Abs(ret.z) < 1e-5) {      // �޽�
                return new Vector3(-1, 1, 1);
            }
            return new Vector3(1.0f - (ret.x + ret.y) / ret.z, ret.x / ret.z, ret.y / ret.z);
        }

        public bool CheckCover(Vector3 p, int axis = 2) {
            Vector3 baryCoord = Barycentric(p, axis);
            // ͶӰ���Ƿ��غ�
            if (baryCoord.x < 0 || baryCoord.y < 0 || baryCoord.z < 0) {    // ͶӰ�治�غ�
                return false;
            }
            // ����������Ƿ��ڵ��ǰ��
            float depth = 0;
            for (int i = 0; i < 3; ++i) {
                depth += baryCoord[i] * points[i][axis];
            }
            if (p[axis] < depth) {
                return true;
            }
            else {
                return false;
            }
        }

    }

    public struct Particle {
        public Vector3 oldPos;
        public Vector3 newPos;
        public Vector3 deltaP;
        public Vector3 velocity;
        public Vector3 deltaV;
        public Vector3 force;
        public Vector3 vorticity;
        public Vector3Int gridCoord;
        public float lambda;
        public float mass;
        public float invMass;

        public Particle(Vector3 pos, Vector3 gravity, float mass) {
            this.oldPos = pos;
            this.newPos = Vector3.zero;
            this.deltaP = Vector3.zero;
            this.velocity = new Vector3(10, 0, 0);
            this.deltaV = Vector3.zero;
            this.force = gravity;
            this.vorticity = Vector3.zero;
            this.gridCoord = new Vector3Int(-1, -1, -1);
            this.lambda = 0;
            this.mass = mass;
            if (mass == float.MaxValue) {
                this.invMass = 0;
            }
            else {
                this.invMass = 1.0f / mass;
            }
        }
    }

    public struct Voxel {
        public Vector3 distGrad;    // ������볡�ݶ�
        public Vector3 position;    // Voxel �������ڵ�mesh�ֲ�����
        public float distance;      // �������
        public float isInner;       // �Ƿ���mesh�ڲ�

        public Voxel(bool _isInner, Vector3 pos) {
            this.isInner = (_isInner) ? 1.0f : 0.0f;
            this.distance = 0;
            this.distGrad = Vector3.zero;
            this.position = pos;
        }
    }
}
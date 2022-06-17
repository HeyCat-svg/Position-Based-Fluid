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

        // 返回重心
        public Vector3 GetCenter() {
            Vector3 ret = Vector3.zero;
            for (int i = 0; i < 3; ++i) {
                ret += points[i];
            }
            return ret / 3.0f;
        }

        // 返回AABB包围盒
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
            if (Mathf.Abs(ret.z) < 1e-5) {      // 无解
                return new Vector3(-1, 1, 1);
            }
            return new Vector3(1.0f - (ret.x + ret.y) / ret.z, ret.x / ret.z, ret.y / ret.z);
        }

        public bool CheckCover(Vector3 p, int axis = 2) {
            Vector3 baryCoord = Barycentric(p, axis);
            // 投影面是否重合
            if (baryCoord.x < 0 || baryCoord.y < 0 || baryCoord.z < 0) {    // 投影面不重合
                return false;
            }
            // 检查三角形是否在点的前方
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
        public int rigbodyParticleIdx;

        public Particle(Vector3 pos, Vector3 gravity, float mass, int _rigbodyParticleIdx = -1, Vector3 vel = default(Vector3)) {
            this.oldPos = pos;
            this.newPos = pos;
            this.deltaP = Vector3.zero;
            this.velocity = vel;
            this.deltaV = Vector3.zero;
            this.force = Vector3.zero;
            this.vorticity = Vector3.zero;
            this.gridCoord = new Vector3Int(-1, -1, -1);
            this.lambda = 0;
            this.mass = mass;
            if (mass == float.MaxValue) {
                this.mass = 1;            // 给一个相对来说比较大的值
                this.invMass = 0;
            }
            else {
                this.invMass = 1.0f / mass;
            }
            this.rigbodyParticleIdx = _rigbodyParticleIdx;
        }
    }

    public struct RigidbodyParticle {
        public Vector3 rLocal;              // 局部空间重心到粒子的位移
        public Vector3 rWorld;              // 世界空间重心到粒子的位移
        public Vector3 posWorld;            // 刚体粒子的世界位置
        public Vector3 distGrad;            // 距离场梯度 
        public Vector3 barycenterSum;       // 重心求和的中间变量
        public Matrix4x4 ASum;              // 协方差矩阵求和的中间变量
        public float distance;              // 有向距离 经过abs转成正的
        public int rigbodyIdx;              // 指向刚体整体信息
        
        public RigidbodyParticle(Vector3 _rLocal, Vector3 _posWorld, Vector3 _distGrad, float _distance, int _rigbodyIdx) {
            rLocal = _rLocal;
            rWorld = _rLocal;
            posWorld = _posWorld;
            distGrad = _distGrad;
            barycenterSum = Vector3.zero;
            ASum = Matrix4x4.zero;
            distance = _distance;
            rigbodyIdx = _rigbodyIdx;
        }
    }

    public struct RigidbodyData {
        public Vector2Int particleIdxRange;     // [startIdx, endIdx]两端闭的
        public Matrix4x4 local2world;           // mesh 空间到世界坐标
        public Vector3 barycenter;              // 局部重心坐标
        public Vector3 worldBaryCenter;         // 世界重心坐标
        public float mass;                      // 每个粒子的质量
        public int sumBorder;                   // 刚体粒子属性求和的idx上界
        public int isStatic;                    // 刚体是否是静止的

        public RigidbodyData(int startIdx, int endIdx, Matrix4x4 l2w, Vector3 _barycenter, float _mass, bool _isStatic = false) {
            particleIdxRange = new Vector2Int(startIdx, endIdx);
            local2world = l2w;
            barycenter = _barycenter;
            worldBaryCenter = _barycenter;
            mass = _mass;
            sumBorder = endIdx;
            isStatic = (_isStatic) ? 1 : 0;
        }
    }

    public struct Voxel {
        public Vector3 distGrad;    // 有向距离场梯度
        public Vector3 position;    // Voxel 中心所在的mesh局部坐标
        public float distance;      // 有向距离
        public float isInner;       // 是否在mesh内部

        public Voxel(bool _isInner, Vector3 pos) {
            this.isInner = (_isInner) ? 1.0f : 0.0f;
            this.distance = float.MaxValue;
            this.distGrad = Vector3.zero;
            this.position = pos;
        }
    }

    public struct GridInfo {
        public Vector2Int particleRange;
        public Vector3 barycenter;
        public int particleNum;
        public int layer;
    }
}
using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 刀光：记录剑根和剑尖的轨迹，拼成一条渐隐的条带（比 TrailRenderer 更像"一刀"）
    public class BladeTrail : MonoBehaviour
    {
        public Transform baseT, tipT;
        public bool emitting;
        public float life = 0.16f;
        public Color color = new Color(1f, 0.55f, 0.2f, 1f);
        public float intensity = 2.4f;

        struct P { public Vector3 a, b; public float t; }
        readonly List<P> pts = new List<P>();
        Mesh mesh;
        Material mat;
        MeshRenderer rend;
        GameObject holder;

        // 剑尖最近的运动方向（没在挥就是零向量）：斩击弧面用它判断从哪边扫过来
        public Vector3 TipVel
        {
            get
            {
                int n = pts.Count;
                if (n < 2) return Vector3.zero;
                return pts[n - 1].b - pts[Mathf.Max(0, n - 6)].b;
            }
        }
        Vector3[] v; Vector2[] uv; Color[] col; int[] tri;

        void Start()
        {
            holder = new GameObject(name + "_Trail");
            mesh = new Mesh();
            mesh.MarkDynamic();
            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            rend = holder.AddComponent<MeshRenderer>();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // 条带的 u 是"新旧"（1 最新），头部固定在 1，旧的部分靠顶点色 alpha 碎掉
            mat = SlashFx.NewMat(false);
            mat.SetFloat("_Head", 1.04f);
            mat.SetFloat("_Tail", -1f);
            mat.SetFloat("_Seed", Random.value * 7f);
            rend.sharedMaterial = mat;
        }

        void OnDestroy() { if (holder) Destroy(holder); if (mat) Destroy(mat); if (mesh) Destroy(mesh); }

        void LateUpdate()
        {
            if (!baseT || !tipT || mat == null) return;
            SlashFx.SetColors(mat, color, false);
            mat.SetFloat("_Intensity", intensity * 0.8f);
            float now = Time.time;
            if (emitting)
            {
                var p = new P { a = baseT.position, b = tipT.position, t = now };
                if (pts.Count > 0)
                {
                    var last = pts[pts.Count - 1];
                    int sub = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(last.b, p.b) / 0.2f), 1, 10);
                    for (int i = 1; i < sub; i++)
                    {
                        float k = i / (float)sub;
                        pts.Add(new P { a = Vector3.Lerp(last.a, p.a, k), b = Vector3.Slerp(last.b - last.a, p.b - p.a, k) + Vector3.Lerp(last.a, p.a, k), t = Mathf.Lerp(last.t, now, k) });
                    }
                }
                pts.Add(p);
            }
            pts.RemoveAll(q => now - q.t > life);
            Build(now);
        }

        void Build(float now)
        {
            mesh.Clear();
            int n = pts.Count;
            if (n < 2) return;
            if (v == null || v.Length < n * 2)
            {
                int cap = Mathf.NextPowerOfTwo(n) * 2;
                v = new Vector3[cap]; uv = new Vector2[cap]; col = new Color[cap]; tri = new int[cap * 3];
            }
            for (int i = 0; i < n; i++)
            {
                float age = Mathf.Clamp01((now - pts[i].t) / life);
                float u = 1f - age;
                // 靠剑根的一侧随时间往剑尖收，尾巴越拖越细
                v[i * 2] = Vector3.Lerp(pts[i].a, pts[i].b, 0.2f + age * 0.55f); v[i * 2 + 1] = pts[i].b;
                uv[i * 2] = new Vector2(u, 0f); uv[i * 2 + 1] = new Vector2(u, 1f);
                var c = new Color(1f, 1f, 1f, (1f - age) * (1f - age));
                col[i * 2] = c; col[i * 2 + 1] = c;
            }
            int tc = (n - 1) * 6;
            var tris = new int[tc];
            for (int i = 0; i < n - 1; i++)
            {
                int k = i * 6, a = i * 2;
                tris[k] = a; tris[k + 1] = a + 1; tris[k + 2] = a + 2;
                tris[k + 3] = a + 1; tris[k + 4] = a + 3; tris[k + 5] = a + 2;
            }
            mesh.SetVertices(v, 0, n * 2);
            mesh.SetUVs(0, uv, 0, n * 2);
            mesh.SetColors(col, 0, n * 2);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }
    }
}

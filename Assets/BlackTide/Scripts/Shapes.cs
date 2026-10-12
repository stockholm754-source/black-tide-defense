using UnityEngine;

namespace BlackTide
{
    // 攻击判定形状（只在水平面上判断），预警圈和伤害判定共用同一份数据，保证"看到的就是会挨打的"
    public struct AttackShape
    {
        public enum Kind { Circle, Cone, Ring, Line }
        public Kind kind;
        public Vector3 origin, fwd;
        public float radius, inner, angle, length, width;

        public static AttackShape Circle(Vector3 c, float r) => new AttackShape { kind = Kind.Circle, origin = c, fwd = Vector3.forward, radius = r };
        public static AttackShape Cone(Vector3 o, Vector3 f, float r, float ang) => new AttackShape { kind = Kind.Cone, origin = o, fwd = Flat(f), radius = r, angle = ang };
        public static AttackShape Ring(Vector3 c, float rin, float rout) => new AttackShape { kind = Kind.Ring, origin = c, fwd = Vector3.forward, inner = rin, radius = rout };
        public static AttackShape Line(Vector3 o, Vector3 f, float len, float w) => new AttackShape { kind = Kind.Line, origin = o, fwd = Flat(f), length = len, width = w };

        public static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized; }

        public bool Contains(Vector3 p, float r)
        {
            Vector3 d = p - origin; d.y = 0f;
            float dist = d.magnitude;
            switch (kind)
            {
                case Kind.Circle: return dist <= radius + r;
                case Kind.Ring: return dist >= inner - r && dist <= radius + r;
                case Kind.Cone:
                    if (dist > radius + r) return false;
                    if (dist < r + 0.4f) return true;
                    float a = Vector3.Angle(fwd, d);
                    return a <= angle * 0.5f + Mathf.Rad2Deg * Mathf.Asin(Mathf.Min(1f, r / dist));
                case Kind.Line:
                    float along = Vector3.Dot(d, fwd);
                    float side = Mathf.Abs(Vector3.Dot(d, Vector3.Cross(Vector3.up, fwd)));
                    return along >= -r && along <= length + r && side <= width * 0.5f + r;
            }
            return false;
        }

        public Telegraph Show(float dur, Color col)
        {
            switch (kind)
            {
                case Kind.Circle: return Telegraph.Make(Telegraph.Sector(0f, radius, 360f, 72), origin, Quaternion.identity, dur, col, false);
                case Kind.Ring: return Telegraph.Make(Telegraph.Sector(inner, radius, 360f, 72), origin, Quaternion.identity, dur, col, false);
                case Kind.Cone: return Telegraph.Make(Telegraph.Sector(0f, radius, angle, 40), origin, Quaternion.LookRotation(fwd), dur, col, true);
                default: return Telegraph.Make(Telegraph.Rect(length, width), origin, Quaternion.LookRotation(fwd), dur, col, true);
            }
        }
    }

    public class Telegraph : MonoBehaviour
    {
        Material mat;
        float t0, dur, fadeT;
        bool resolved;

        public static Telegraph Make(Mesh m, Vector3 pos, Quaternion rot, float dur, Color col, bool sides)
        {
            var go = new GameObject("Telegraph");
            go.transform.SetPositionAndRotation(new Vector3(pos.x, 0.05f, pos.z), rot);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var t = go.AddComponent<Telegraph>();
            t.mat = new Material(SliceAssets.I.telegraphShader);
            t.mat.SetColor("_Color", col);
            t.mat.SetFloat("_Sides", sides ? 1f : 0f);
            r.sharedMaterial = t.mat;
            t.t0 = Time.time;
            t.dur = Mathf.Max(0.01f, dur);
            return t;
        }

        void Update()
        {
            if (!resolved)
            {
                mat.SetFloat("_Progress", Mathf.Clamp01((Time.time - t0) / dur));
                return;
            }
            fadeT += Time.deltaTime;
            float k = 1f - fadeT / 0.3f;
            mat.SetFloat("_Flash", Mathf.Max(0f, k) * 0.35f);
            mat.SetFloat("_Fade", Mathf.Max(0f, k));
            if (fadeT > 0.3f) Destroy(gameObject);
        }

        public void Resolve() { resolved = true; mat.SetFloat("_Progress", 1f); }
        public void Cancel() { if (this) Destroy(gameObject); }

        // 扇形/圆/环：uv.x 沿角度 0→1，uv.y 沿半径 0→1
        public static Mesh Sector(float rIn, float rOut, float angDeg, int seg)
        {
            var m = new Mesh();
            var v = new Vector3[(seg + 1) * 2];
            var uv = new Vector2[v.Length];
            var tri = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float u = i / (float)seg;
                float a = Mathf.Deg2Rad * (-angDeg * 0.5f + angDeg * u);
                Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                v[i * 2] = d * rIn; v[i * 2 + 1] = d * rOut;
                uv[i * 2] = new Vector2(u, 0f); uv[i * 2 + 1] = new Vector2(u, 1f);
            }
            for (int i = 0; i < seg; i++)
            {
                int k = i * 6, a = i * 2;
                tri[k] = a; tri[k + 1] = a + 1; tri[k + 2] = a + 2;
                tri[k + 3] = a + 1; tri[k + 4] = a + 3; tri[k + 5] = a + 2;
            }
            m.vertices = v; m.uv = uv; m.triangles = tri; m.RecalculateBounds();
            return m;
        }

        public static Mesh Rect(float len, float w)
        {
            var m = new Mesh();
            float h = w * 0.5f;
            m.vertices = new[] { new Vector3(-h, 0, 0), new Vector3(h, 0, 0), new Vector3(-h, 0, len), new Vector3(h, 0, len) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.RecalculateBounds();
            return m;
        }
    }
}

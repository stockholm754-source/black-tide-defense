using UnityEngine;

namespace BlackTide
{
    // 把 Tripo 武器模型套到原版 KayKit 武器的位置上：
    // 用主成分分析找出两把武器的长轴和刃宽方向，按"尖端/头部"定方向，握把位置按原版比例对齐，长度按原版缩放
    public static class WeaponFit
    {
        public struct Shape
        {
            public Vector3 center, axis, wide; // axis 指向尖端（剑）或斧头（斧）
            public float min, max;             // 沿 axis 的范围（相对 center）
            public float Len => max - min;
        }

        public static Shape Analyze(Vector3[] v, bool axe)
        {
            var s = new Shape();
            Vector3 c = Vector3.zero;
            foreach (var p in v) c += p;
            c /= Mathf.Max(1, v.Length);
            // 协方差
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p0 in v)
            {
                var p = p0 - c;
                xx += p.x * p.x; xy += p.x * p.y; xz += p.x * p.z; yy += p.y * p.y; yz += p.y * p.z; zz += p.z * p.z;
            }
            Vector3 Mul(Vector3 a) => new Vector3(xx * a.x + xy * a.y + xz * a.z, xy * a.x + yy * a.y + yz * a.z, xz * a.x + yz * a.y + zz * a.z);
            Vector3 e1 = new Vector3(1, 0.7f, 0.3f).normalized;
            for (int i = 0; i < 60; i++) e1 = Mul(e1).normalized;
            Vector3 e2 = Vector3.Cross(e1, new Vector3(0.3f, 1, 0.5f)).normalized;
            for (int i = 0; i < 60; i++) { var m = Mul(e2); m -= Vector3.Dot(m, e1) * e1; e2 = m.normalized; }

            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in v) { float t = Vector3.Dot(p - c, e1); lo = Mathf.Min(lo, t); hi = Mathf.Max(hi, t); }

            bool flip;
            if (!axe)
            {
                // 剑：尖端离重心更远
                flip = -lo > hi;
            }
            else
            {
                // 斧：斧头那一端横向更宽
                float spreadHi = 0, spreadLo = 0, span = hi - lo;
                foreach (var p in v)
                {
                    float t = Vector3.Dot(p - c, e1);
                    float w = Mathf.Abs(Vector3.Dot(p - c, e2));
                    if (t > hi - span * 0.3f) spreadHi = Mathf.Max(spreadHi, w);
                    if (t < lo + span * 0.3f) spreadLo = Mathf.Max(spreadLo, w);
                }
                flip = spreadLo > spreadHi;
            }
            if (flip) { e1 = -e1; float t = lo; lo = -hi; hi = -t; }

            if (axe)
            {
                // 斧刃方向：斧头部分在 e2 上的偏向（三阶矩）
                float m3 = 0, span = hi - lo;
                foreach (var p in v)
                {
                    float t = Vector3.Dot(p - c, e1);
                    if (t < hi - span * 0.35f) continue;
                    float w = Vector3.Dot(p - c, e2);
                    m3 += w * w * w;
                }
                if (m3 < 0) e2 = -e2;
            }
            s.center = c; s.axis = e1; s.wide = e2; s.min = lo; s.max = hi;
            return s;
        }

        static Vector3[] Verts(Transform root, out Renderer rend)
        {
            rend = null;
            var list = new System.Collections.Generic.List<Vector3>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh; if (!m) continue;
                var M = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (var p in m.vertices) list.Add(M.MultiplyPoint3x4(p));
                if (!rend) rend = mf.GetComponent<Renderer>();
            }
            return list.ToArray();
        }

        // refWeapon：已经在跟随的原版武器副本。返回新武器的渲染器；原版渲染器会被隐藏
        public static Renderer Fit(Transform refWeapon, GameObject tripoPrefab, Material mat, bool axe, float lengthMul)
        {
            if (!refWeapon || !tripoPrefab) return null;
            var kv = Verts(refWeapon, out var kRend);
            var k = Analyze(kv, axe);
            float gripT = Vector3.Dot(-k.center, k.axis); // 原版的枢轴就是握把
            float gripFrac = (gripT - k.min) / Mathf.Max(1e-4f, k.Len);
            foreach (var r in refWeapon.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

            var t = Object.Instantiate(tripoPrefab, refWeapon);
            t.name = "TripoMesh";
            t.transform.localPosition = Vector3.zero; t.transform.localRotation = Quaternion.identity; t.transform.localScale = Vector3.one;
            var tv = Verts(t.transform, out var tRend);
            var w = Analyze(tv, axe);

            float scale = k.Len * lengthMul / Mathf.Max(1e-4f, w.Len);
            Quaternion R = Quaternion.LookRotation(k.axis, k.wide) * Quaternion.Inverse(Quaternion.LookRotation(w.axis, w.wide));
            Vector3 tGrip = w.center + w.axis * (w.min + gripFrac * w.Len);
            Vector3 kGrip = k.center + k.axis * gripT;
            t.transform.localRotation = R;
            t.transform.localScale = Vector3.one * scale;
            t.transform.localPosition = kGrip - R * (tGrip * scale);

            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = true;
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            return tRend;
        }
    }
}

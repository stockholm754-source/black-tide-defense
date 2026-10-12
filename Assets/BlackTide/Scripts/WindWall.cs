using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 青岚 · 风障：10 秒风墙。附近敌人减速 40%；Boss 冲锋撞墙会被挡下并大幅削架势；Boss 贴着墙时受到的架势伤害 ×2
    public class WindWall : MonoBehaviour
    {
        public static readonly List<WindWall> All = new List<WindWall>();
        public const float SlowRadius = 2.4f, Length = 7f, Dur = 10f;
        public Vector3 a, b;
        public HeroBase owner;
        float until;
        Telegraph tel;
        ParticleSystem ps;

        public static WindWall Spawn(HeroBase owner, Vector3 center, Vector3 tangent)
        {
            center.y = 0f; tangent.y = 0f; tangent.Normalize();
            var go = new GameObject("风障");
            go.transform.SetPositionAndRotation(center, Quaternion.LookRotation(tangent));
            var w = go.AddComponent<WindWall>();
            w.owner = owner;
            w.a = center - tangent * Length * 0.5f;
            w.b = center + tangent * Length * 0.5f;
            w.until = Time.time + Dur;
            var col = owner.Color;
            w.tel = Telegraph.Make(Telegraph.Rect(Length, 0.9f), w.a, Quaternion.LookRotation(tangent), Dur, new Color(col.r, col.g, col.b, 0.55f), true);
            w.ps = Sheet(go.transform, col);
            for (int i = 0; i <= 6; i++) FX.Dust(Vector3.Lerp(w.a, w.b, i / 6f), 2, new Color(0.7f, 0.85f, 0.8f, 0.5f));
            All.Add(w);
            return w;
        }

        // 沿墙升起的风幕
        static ParticleSystem Sheet(Transform parent, Color col)
        {
            var go = new GameObject("Sheet");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
            main.startColor = new Color(col.r, col.g, col.b, 0.75f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            var em = ps.emission; em.rateOverTime = 160f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(0.3f, 0.1f, Length); sh.rotation = new Vector3(-90f, 0f, 0f);
            var c = ps.colorOverLifetime; c.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            c.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = FX.NewMat(SliceAssets.I.softDot, true, Color.white, 2f);
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.12f; r.lengthScale = 2f;
            return ps;
        }

        public float Dist(Vector3 p)
        {
            p.y = 0f;
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * t);
        }

        public static bool Near(Vector3 p, float r)
        {
            foreach (var w in All) if (w && w.Dist(p) <= r) return true;
            return false;
        }

        // 线段 p0→p1 是否穿过某道风墙（Boss 冲锋用）
        public static WindWall Crosses(Vector3 p0, Vector3 p1)
        {
            foreach (var w in All)
            {
                if (!w) continue;
                if (SegSeg(p0, p1, w.a, w.b)) return w;
            }
            return null;
        }

        static float Cross(Vector3 u, Vector3 v) => u.x * v.z - u.z * v.x;
        static bool SegSeg(Vector3 p, Vector3 p2, Vector3 q, Vector3 q2)
        {
            Vector3 r = p2 - p, s = q2 - q;
            float d = Cross(r, s);
            if (Mathf.Abs(d) < 1e-6f) return false;
            float t = Cross(q - p, s) / d, u = Cross(q - p, r) / d;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        void Update()
        {
            foreach (var e in Combat.enemies)
            {
                if (!e || e.dead || Dist(e.transform.position) > SlowRadius + e.radius) continue;
                var mc = e.GetComponent<IMob>();
                if (mc != null) mc.Slow(Time.time + 0.2f);
                else if (e.bossCtl) e.bossCtl.slowUntil = Time.time + 0.2f;
            }
            if (Time.time >= until || !owner)
            {
                if (tel) tel.Resolve();
                if (ps) { ps.Stop(); ps.transform.SetParent(null); Destroy(ps.gameObject, 1.2f); }
                Destroy(gameObject);
            }
        }

        void OnDestroy() { All.Remove(this); }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 青岚 · 旋流：风涡把杂兵吸到一起 2 秒，分 4 跳造成伤害；碰到 Boss 只削一次架势
    public class Vortex : MonoBehaviour
    {
        HeroBase owner;
        float Radius = 5f, Dur = 2f;   // 风眼（青岚专属增益）会放大
        float t0, nextTick;
        int ticks = 4;
        bool bossHit, burst;
        Telegraph tel;
        Transform spin;
        static readonly List<Damageable> tmp = new List<Damageable>();

        public static void Spawn(HeroBase owner, Vector3 c)
        {
            c.y = 0f;
            var go = new GameObject("旋流");
            go.transform.position = c;
            var v = go.AddComponent<Vortex>();
            v.owner = owner;
            v.t0 = Time.time;
            v.Radius *= Rogue.VortexScale(owner);
            v.Dur += Rogue.VortexExtra(owner);
            v.ticks = Mathf.RoundToInt(v.Dur * 2f);
            v.burst = Rogue.VortexBurst(owner);
            v.nextTick = Time.time + 0.15f;
            v.tel = AttackShape.Circle(c, v.Radius).Show(v.Dur, new Color(owner.Color.r, owner.Color.g, owner.Color.b, 0.28f));
            v.spin = Wind(go.transform, owner.Color, v.Radius);
            FX.Ring(c, v.Radius, 0.6f, 0.45f, new Color(owner.Color.r, owner.Color.g, owner.Color.b, 0.9f), 0.2f);
            FX.Dust(c, 10, new Color(0.6f, 0.75f, 0.7f, 0.5f));
            Feel.I.Sfx("dash", owner.IsControlled ? 0.8f : 0.4f, 0.05f, 0.7f);
        }

        // 螺旋上升的风
        static Transform Wind(Transform parent, Color col, float Radius)
        {
            var go = new GameObject("Wind");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
            main.startColor = new Color(col.r, col.g, col.b, 0.8f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 300;
            var em = ps.emission; em.rateOverTime = 120f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = Radius * 0.9f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.orbitalY = new ParticleSystem.MinMaxCurve(5f);
            vel.radial = new ParticleSystem.MinMaxCurve(-2.2f);
            vel.y = new ParticleSystem.MinMaxCurve(1.8f);   // x/y/z 必须是同一种曲线模式
            vel.x = new ParticleSystem.MinMaxCurve(0f); vel.z = new ParticleSystem.MinMaxCurve(0f);
            var col2 = ps.colorOverLifetime; col2.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
            col2.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = FX.NewMat(SliceAssets.I.softDot, true, Color.white, 2.2f);
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.08f; r.lengthScale = 1.5f;
            return go.transform;
        }

        void Update()
        {
            if (!owner) { Destroy(gameObject); return; }
            Vector3 c = transform.position;
            float until = t0 + Dur;
            tmp.Clear();
            foreach (var e in Combat.enemies) if (e && !e.dead && Vector3.Distance(Flat(e.transform.position), c) <= Radius + e.radius) tmp.Add(e);
            foreach (var e in tmp)
            {
                var mc = e.GetComponent<IMob>();
                if (mc != null) mc.Pull(c, until);
                else if (!bossHit && e.bossCtl)
                {
                    bossHit = true;
                    owner.DealHit(e, 0.6f, 60f, 0f, (Flat(e.transform.position) - c).normalized, false, "vortex", 0f, false);
                }
            }
            if (ticks > 0 && Time.time >= nextTick)
            {
                ticks--;
                nextTick += 0.5f;
                foreach (var e in tmp) owner.DealHit(e, 0.5f, 8f, 0f, (c - Flat(e.transform.position)).normalized, false, "vortex", 0f, false);
                FX.Ring(c, Radius * 0.8f, 0.5f, 0.3f, new Color(owner.Color.r, owner.Color.g, owner.Color.b, 0.6f), 0.12f);
            }
            if (spin) spin.Rotate(0f, 400f * Time.deltaTime, 0f);
            if (Time.time >= until)
            {
                if (tel) tel.Resolve();
                if (burst)   // 风眼 Lv3：结束时炸开
                {
                    foreach (var e in Combat.enemies.ToArray())
                        if (e && !e.dead && Vector3.Distance(Flat(e.transform.position), c) <= Radius + e.radius)
                            owner.DealHit(e, 2f, 30f, 4f, (Flat(e.transform.position) - c).normalized, true, "vortex");
                    FX.Ring(c, 0.4f, Radius + 0.5f, 0.4f, new Color(owner.Color.r, owner.Color.g, owner.Color.b, 1f), 0.2f);
                }
                var ps = spin ? spin.GetComponent<ParticleSystem>() : null;
                if (ps) { ps.Stop(); spin.SetParent(null); Destroy(spin.gameObject, 1.2f); }
                Destroy(gameObject);
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}

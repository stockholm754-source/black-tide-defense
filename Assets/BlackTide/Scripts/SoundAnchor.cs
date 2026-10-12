using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 澄音 · 锚音：8 秒音锚。范围内队友每秒回 2.5% 生命、受到伤害 −20%；敌人第一次进入定身 1.5 秒（Boss 改为削架势）
    public class SoundAnchor : MonoBehaviour
    {
        HeroBase owner;
        const float Radius = 4f, Dur = 8f;
        float until, nextHeal, nextPulse;
        readonly HashSet<Damageable> touched = new HashSet<Damageable>();
        static readonly List<Damageable> tmp = new List<Damageable>();
        Telegraph tel;

        public static void Spawn(HeroBase owner, Vector3 c)
        {
            c.y = 0f;
            var go = new GameObject("锚音");
            go.transform.position = c;
            var s = go.AddComponent<SoundAnchor>();
            s.owner = owner;
            s.until = Time.time + Dur;
            s.nextHeal = Time.time + 0.2f;
            var col = owner.Color;
            s.tel = Telegraph.Make(Telegraph.Sector(Radius - 0.35f, Radius, 360f, 72), c, Quaternion.identity, Dur, new Color(col.r, col.g, col.b, 0.7f), false);
            FX.Ring(c, 0.3f, Radius, 0.4f, new Color(col.r, col.g, col.b, 0.95f), 0.16f);
            FX.Sparks(c + Vector3.up * 0.5f, Vector3.up, 24, Color.Lerp(col, Color.white, 0.4f));
            Feel.I.Sfx("perfect_dodge", owner.IsControlled ? 0.6f : 0.3f, 0.05f, 0.8f);
        }

        void Update()
        {
            if (!owner) { Destroy(gameObject); return; }
            Vector3 c = transform.position;
            var col = owner.Color;
            foreach (var h in Combat.heroes)
                if (h && !h.dmg.dead && Vector3.Distance(Flat(h.transform.position), c) <= Radius) h.AddMod("taken", "anchor", -0.2f, 0.25f);
            if (Time.time >= nextHeal)
            {
                nextHeal += 1f;
                foreach (var h in Combat.heroes)
                    if (h && !h.dmg.dead && Vector3.Distance(Flat(h.transform.position), c) <= Radius) owner.Heal(h, h.dmg.maxHp * 0.025f);
            }
            if (Time.time >= nextPulse)
            {
                nextPulse = Time.time + 0.8f;
                FX.Ring(c, 0.4f, Radius, 0.7f, new Color(col.r, col.g, col.b, 0.35f), 0.07f);
            }
            tmp.Clear();
            foreach (var e in Combat.enemies)
                if (e && !e.dead && !touched.Contains(e) && Vector3.Distance(Flat(e.transform.position), c) <= Radius + e.radius) tmp.Add(e);
            foreach (var e in tmp)
            {
                touched.Add(e);
                var mc = e.GetComponent<IMob>();
                if (mc != null) { mc.Root(1.5f); FX.Ring(e.transform.position, 0.2f, 1.4f, 0.4f, new Color(col.r, col.g, col.b, 0.9f)); }
                else owner.DealHit(e, 0.4f, 60f, 0f, (Flat(e.transform.position) - c).normalized, false, "anchor", 0f, false);
            }
            if (Time.time >= until)
            {
                if (tel) tel.Resolve();
                Destroy(gameObject);
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}

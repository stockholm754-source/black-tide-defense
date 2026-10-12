using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 潮汐触手（M5，58 号模型）：黑潮王二阶段起召唤，原地不动，每 4 秒挑 9 米内最近的人砸一下（1.3 秒预警，3 米圆）。
    // 血量约 5 只轻波，可以打掉；算一只轻波的击杀（经验、贝币、增益计数）。黑潮王死了全部消失。
    public class TideTentacle : MonoBehaviour
    {
        public static readonly List<TideTentacle> all = new List<TideTentacle>();
        Damageable dmg;
        float nextSlam, slamAt = -1f;
        AttackShape shape;
        Telegraph tel;
        Combat.Threat threat;
        static readonly Color Red = new Color(1f, 0.28f, 0.14f, 0.9f);

        public static void Spawn(Vector3 p, BossController owner)
        {
            p.y = 0f;
            var go = new GameObject("潮汐触手");
            go.transform.position = p;
            var prop = SliceAssets.I.MapProp("58_tide_tentacle");
            if (prop != null && prop.model)
                ArenaBuilder.Prop(go.transform, prop, "58_tide_tentacle", p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), 4.5f, false, ArenaBuilder.StoneTint, ArenaBuilder.Teal * 2f, 0.2f, out _);
            else
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.Destroy(c.GetComponent<Collider>());
                c.transform.SetParent(go.transform, false);
                c.transform.localPosition = Vector3.up * 2.2f; c.transform.localScale = new Vector3(1.2f, 2.2f, 1.2f);
                c.GetComponent<Renderer>().sharedMaterial = ArenaBuilder.Lit(null, new Color(0.08f, 0.12f, 0.14f), 0.3f);
            }
            var t = go.AddComponent<TideTentacle>();
            t.Init(p);
            all.Add(t);
            FX.Tide(p, Vector3.up, 50);
            FX.Ring(p, 0.3f, 3f, 0.5f, new Color(0.35f, 1f, 0.92f, 1f), 0.2f);
        }

        void Init(Vector3 p)
        {
            dmg = gameObject.AddComponent<Damageable>();
            dmg.team = Team.Enemy;
            dmg.maxHp = dmg.hp = 560f * 5f * Coop.HpMultFor(Coop.ErosionAt(p));
            dmg.staggerMax = 99999f;
            dmg.radius = 0.9f; dmg.height = 4.5f;
            dmg.tier = 0;
            dmg.onDeath = () => Die(true);
            var col = gameObject.AddComponent<CapsuleCollider>();
            col.radius = 0.8f; col.height = 4.5f; col.center = new Vector3(0f, 2.25f, 0f);
            dmg.RefreshRenderers();
            Combat.enemies.Add(dmg);
            nextSlam = Time.time + 2f;
        }

        void Update()
        {
            if (!dmg || dmg.dead) return;
            if (slamAt < 0f && Time.time >= nextSlam)
            {
                HeroBase best = null; float bd = 9f;
                foreach (var h in Combat.heroes)
                {
                    if (!h || h.Downed) continue;
                    float d = Vector3.Distance(Flat(h.transform.position), Flat(transform.position));
                    if (d < bd) { bd = d; best = h; }
                }
                if (!best) { nextSlam = Time.time + 1f; return; }
                shape = AttackShape.Circle(Flat(best.transform.position), 3f);
                tel = shape.Show(1.3f, Red);
                threat = new Combat.Threat { owner = dmg, shape = shape, resolveAt = Time.time + 1.3f };
                Combat.threats.Add(threat);
                slamAt = Time.time + 1.3f;
            }
            if (slamAt > 0f && Time.time >= slamAt)
            {
                slamAt = -1f; nextSlam = Time.time + 4f;
                if (tel) tel.Resolve();
                if (threat != null) { Combat.threats.Remove(threat); threat = null; }
                foreach (var h in Combat.heroes.ToArray())
                {
                    if (!h || h.dmg.dead) continue;
                    bool inside = shape.Contains(h.transform.position, 0.5f);
                    if (h.Invulnerable) { if (h.InPerfectWindow && (inside || shape.Contains(h.dodgeStartPos, 0.5f))) h.PerfectDodge(); continue; }
                    if (!inside) continue;
                    h.dmg.TakeHit(new HitInfo { damage = 170f * Coop.EnemyDmgMultAt(transform.position), knockback = 4f, dir = Flat(h.transform.position - shape.origin).normalized, heavy = true, kind = "boss", attacker = gameObject });
                }
                FX.Ring(shape.origin, 0.4f, 3.2f, 0.35f, new Color(0.35f, 1f, 0.92f, 1f), 0.2f);
                FX.Dust(shape.origin, 10, new Color(0.25f, 0.22f, 0.3f, 0.85f));
                Feel.I.Sfx("boss_slam", 0.6f, 0.05f, 1.2f);
            }
        }

        void Die(bool credit)
        {
            all.Remove(this);
            if (tel) tel.Cancel();
            if (threat != null) Combat.threats.Remove(threat);
            if (dmg) Combat.enemies.Remove(dmg);
            if (credit && dmg) Coop.OnKill(dmg);
            FX.Tide(transform.position + Vector3.up * 2f, Vector3.up, 40);
            Destroy(gameObject, 0.05f);
        }

        public static void KillAll() { foreach (var t in all.ToArray()) if (t) t.Die(false); all.Clear(); }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}

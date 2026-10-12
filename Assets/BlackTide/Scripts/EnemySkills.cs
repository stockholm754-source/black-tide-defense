using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 敌人招式扩充（2026-10-09）：杂兵、精英原来每种只有一招，打法单一，声骸和武器也没东西可借。
    // 这里放几个兵种共用的招式部件：结算对英雄的伤害（含完美闪避判定）、钉地矛阵、黑潮泥沼。
    public static class EnemySkills
    {
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        // 对英雄结算一个范围：在闪避无敌里算完美闪避；extra 给命中的人加控制
        public static int HitHeroes(AttackShape s, float damage, float knock, bool heavy, GameObject attacker, System.Action<HeroBase> extra = null)
        {
            int n = 0;
            foreach (var h in Combat.heroes.ToArray())
            {
                if (!h || h.dmg.dead) continue;
                bool inside = s.Contains(h.transform.position, 0.5f);
                if (h.Invulnerable) { if (h.InPerfectWindow && (inside || s.Contains(h.dodgeStartPos, 0.5f))) h.PerfectDodge(); continue; }
                if (!inside) continue;
                Vector3 d = Flat(h.transform.position - s.origin); if (d.sqrMagnitude < 0.01f) d = s.fwd;
                h.dmg.TakeHit(new HitInfo { damage = damage, knockback = knock, heavy = heavy, dir = d.normalized, kind = "minion", attacker = attacker });
                if (!h.dmg.lastHitAccepted) continue;   // 被格挡 / 拒绝的这一下不附加控制（Codex 审阅 R2）；护盾吸收仍算命中
                extra?.Invoke(h);
                n++;
            }
            return n;
        }

        // 定身（钉地）：移动速度归零，闪避照样能用（闪避位移不走移动速度）
        public static void Pin(HeroBase h, float dur)
        {
            h.AddMod("speed", "pin", -1f, dur);
            FX.Ring(h.transform.position, 0.2f, 1.2f, dur, new Color(1f, 0.45f, 0.2f, 0.8f), 0.12f);
            if (h.IsControlled) Combat.Toast("被钉住了！（闪避可以脱身）", new Color(1f, 0.55f, 0.35f));
        }
    }

    // 珊瑚投矛手的钉地矛阵：目标脚下和身边各一个圈，1.2 秒后地里刺出珊瑚矛，圈里的人受伤并被钉住 1.2 秒
    public class PinTrap : MonoBehaviour
    {
        readonly List<(AttackShape s, Telegraph tel, Combat.Threat th)> parts = new List<(AttackShape, Telegraph, Combat.Threat)>();
        float at, damage;
        Damageable owner;

        public static void Spawn(Damageable owner, Vector3 center, float damage)
        {
            var go = new GameObject("钉地矛阵");
            var p = go.AddComponent<PinTrap>();
            p.owner = owner; p.damage = damage; p.at = Time.time + 1.2f;
            Vector3[] pts = { center, center + Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * 2.6f, center + Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * 2.6f };
            foreach (var q in pts)
            {
                var s = AttackShape.Circle(new Vector3(q.x, 0f, q.z), 1.5f);
                var tel = s.Show(1.2f, new Color(1f, 0.42f, 0.18f, 0.75f));
                var th = new Combat.Threat { owner = owner, shape = s, resolveAt = p.at };
                Combat.threats.Add(th);
                p.parts.Add((s, tel, th));
            }
        }

        void Update()
        {
            if (Time.time < at) return;
            foreach (var (s, tel, th) in parts)
            {
                if (tel) tel.Resolve();
                Combat.threats.Remove(th);
                EnemySkills.HitHeroes(s, damage, 1f, false, owner ? owner.gameObject : null, h => EnemySkills.Pin(h, 1.2f));
                for (int i = 0; i < 5; i++) FX.Sparks(s.origin + Random.insideUnitSphere * 1f + Vector3.up * 0.2f, Vector3.up, 4, new Color(1f, 0.6f, 0.5f));
                FX.Ring(s.origin, 0.2f, 1.7f, 0.3f, new Color(1f, 0.55f, 0.4f, 1f), 0.25f);
            }
            Feel.I.Sfx("hit_armor", 0.4f, 0.1f, 0.9f);
            Destroy(gameObject);
        }

        void OnDestroy() { foreach (var p in parts) Combat.threats.Remove(p.th); }
    }

    // 潮囊自爆后留下的黑潮泥沼：5 秒，里面的人移动速度 −45%
    public class TarPool : MonoBehaviour
    {
        float r, until, born;
        bool vsEnemies;   // true：声骸放的，减速敌人
        Material mat;

        public static void Spawn(Vector3 c, float r, float dur, bool vsEnemies = false, Color? col = null)
        {
            var go = new GameObject("黑潮泥沼");
            go.transform.position = new Vector3(c.x, 0.04f, c.z);
            var p = go.AddComponent<TarPool>();
            p.r = r; p.born = Time.time; p.until = Time.time + dur; p.vsEnemies = vsEnemies;
            go.AddComponent<MeshFilter>().sharedMesh = Telegraph.Sector(0f, r, 360f, 48);
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            p.mat = new Material(SliceAssets.I.telegraphShader);
            p.mat.SetColor("_Color", col ?? new Color(0.05f, 0.35f, 0.33f, 0.55f));
            p.mat.SetFloat("_Progress", 1f); p.mat.SetFloat("_Sides", 0f);
            mr.sharedMaterial = p.mat;
        }

        void Update()
        {
            float left = until - Time.time;
            if (left <= 0f) { if (mat) Destroy(mat); Destroy(gameObject); return; }
            if (mat) mat.SetFloat("_Fade", Mathf.Clamp01(left / 0.6f) * Mathf.Clamp01((Time.time - born) / 0.3f));
            if (Random.value < 0.25f) FX.Tide(transform.position + Random.insideUnitSphere * r * 0.7f, Vector3.up, 1);
            Vector3 c = transform.position; c.y = 0f;
            if (vsEnemies)
            {
                foreach (var e in Combat.enemies)
                {
                    if (!e || e.dead) continue;
                    Vector3 q = e.transform.position; q.y = 0f;
                    if (Vector3.Distance(q, c) > r) continue;
                    var m = e.GetComponent<IMob>();
                    if (m != null) m.Slow(Time.time + 0.3f);
                }
                return;
            }
            foreach (var h in Combat.heroes)
            {
                if (!h || h.Downed) continue;
                Vector3 p = h.transform.position; p.y = 0f;
                if (Vector3.Distance(p, c) <= r) h.AddMod("speed", "tar", -0.45f, 0.2f);
            }
        }
    }
}

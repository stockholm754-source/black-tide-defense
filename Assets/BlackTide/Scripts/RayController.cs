using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 残像·刺鳐：远程小怪（网页原型 q_ranged：保持 6.5 米距离，吐黑潮弹）。
    // 模型没有骨骼：悬浮、起伏、转弯时倾斜、受击后仰都用代码做。
    // 2026-10-09 招式扩充：被贴近（7 米内）时俯冲——蓄力 0.6 秒，沿直线冲过 10 米，路上的人挨打（8–11 秒一次）。
    public class RayController : MonoBehaviour, IMob
    {
        enum S { Spawn, Chase, Windup, Recover, Hurt, Dead, DiveWind, Dive }
        S state;
        float t;
        public Damageable dmg;
        Transform body;
        HeroBase target;
        float retargetAt, nextAttackAt, strafe = 1f, strafeT, bank, recoil;
        Vector3 knockVel, lastPos, aimDir;
        Combat.Threat threat;
        float phase, nextDiveAt;
        Vector3 diveFrom, diveTo;
        Telegraph diveTel;

        public bool dormant;
        public Vector3 home;
        public float leash;
        public System.Action onWake;
        float slowUntil, rootedUntil, pullUntil;
        Vector3 pullCenter;
        public static RayController Last;

        const float KeepDist = 6.5f, Range = 11f, Speed = 3.0f, WindupT = 0.6f, BoltSpeed = 10f, BoltDmg = 70f, Hover = 1.35f;

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        public void Setup(Transform model, float hpMult)
        {
            body = model;
            dmg = gameObject.AddComponent<Damageable>();
            dmg.team = Team.Enemy;
            int team = Mathf.Max(1, Combat.heroes.Count);
            float teamScale = team >= 3 ? 2.4f : team == 2 ? 1.7f : 1f;
            dmg.maxHp = dmg.hp = 560f * teamScale * hpMult * Coop.OpeningHpRelief();   // DS-028：前期启动宽限（只削前 3–6 分钟）
            dmg.radius = 0.8f; dmg.height = 1.8f;
            dmg.tier = 0;
            dmg.onHit = OnHit;
            dmg.onDeath = OnDeath;
            var col = gameObject.AddComponent<CapsuleCollider>();
            col.radius = 0.9f; col.height = 1f; col.center = new Vector3(0, Hover, 0); col.direction = 2;
            Combat.enemies.Add(dmg);
            Last = this;
            phase = Random.value * 10f;
            lastPos = transform.position;
            nextAttackAt = Time.time + Random.Range(1.2f, 2.2f);
            FX.Tide(transform.position + Vector3.up, Vector3.up, 20);
        }

        public void Wake() { if (!dormant) return; dormant = false; dmg.dormant = false; onWake?.Invoke(); }
        public void Pull(Vector3 c, float until) { pullCenter = c; pullUntil = Mathf.Max(pullUntil, until); }
        public void Root(float dur) { rootedUntil = Mathf.Max(rootedUntil, Time.time + dur); }
        public void Slow(float until) { slowUntil = Mathf.Max(slowUntil, until); }
        bool InLeash(HeroBase h) => leash <= 0f || Vector3.Distance(Flat(h.transform.position), Flat(home)) <= leash;

        void Update()
        {
            t += Time.deltaTime;
            if (state == S.Dead) { TickDead(); return; }
            if (knockVel.sqrMagnitude > 0.01f)
            {
                transform.position = Combat.ClampToArena(transform.position + knockVel * Time.deltaTime, 0.8f);
                knockVel = Vector3.Lerp(knockVel, Vector3.zero, Time.deltaTime * 8f);
            }
            if (Time.time < pullUntil)
            {
                Vector3 to = Flat(pullCenter - transform.position);
                if (to.magnitude > 0.6f) transform.position = Combat.ClampToArena(transform.position + to.normalized * Mathf.Min(7f, to.magnitude * 3f) * Time.deltaTime, 0.8f);
            }
            if (dormant)
            {
                var near = Combat.NearestHero(transform.position);
                if (near && Vector3.Distance(Flat(near.transform.position), Flat(transform.position)) < 12f) Wake();
                Animate(Vector3.zero);
                if (dormant) return;
            }
            if (!target || target.dmg.dead || Time.time >= retargetAt || !InLeash(target))
            {
                var nt = Combat.NearestHero(transform.position, 2f);
                target = nt && InLeash(nt) ? nt : null;
                retargetAt = Time.time + Random.Range(2f, 3.5f);
            }
            Vector3 move = Vector3.zero;
            if (!target)
            {
                if (leash > 0f && Flat(home - transform.position).magnitude > 2f) move = Nav.Dir(this, transform.position, home);
            }
            else
            {
                Vector3 to = Flat(target.transform.position - transform.position);
                float d = to.magnitude;
                Face(to);
                switch (state)
                {
                    case S.Spawn:
                        if (t > 0.8f) Enter(S.Chase);
                        break;
                    case S.Chase:
                        if (d > Range - 1f) move = Nav.Dir(this, transform.position, target.transform.position);
                        else if (d < KeepDist - 1.5f) move = -to.normalized;
                        else
                        {
                            if (Time.time > strafeT) { strafe = Random.value < 0.5f ? 1f : -1f; strafeT = Time.time + Random.Range(1.2f, 2.5f); }
                            move = Vector3.Cross(Vector3.up, to.normalized) * strafe * 0.7f;
                        }
                        if (d < 7f && Time.time >= nextDiveAt && Time.time >= rootedUntil) { StartDive(to); break; }
                        if (d < Range && Time.time >= nextAttackAt && Time.time >= rootedUntil) StartWindup(to);
                        break;
                    case S.Windup:
                        // 蓄力：眼睛越来越亮，嘴前聚一团黑潮
                        if (Random.value < 0.6f) FX.Tide(Muzzle, Vector3.up, 1);
                        if (t >= WindupT) Fire();
                        break;
                    case S.Recover:
                        if (t > 0.5f) { nextAttackAt = Time.time + Random.Range(1.8f, 2.8f); Enter(S.Chase); }
                        break;
                    case S.Hurt:
                        if (t > 0.3f) Enter(S.Chase);
                        break;
                    case S.DiveWind:
                        if (Random.value < 0.5f) FX.Tide(Muzzle, Vector3.up, 1);
                        if (t >= 0.6f) Dive();
                        break;
                    case S.Dive:
                        {
                            float k = Mathf.Clamp01(t / 0.35f);
                            transform.position = Combat.ClampToArena(Vector3.Lerp(diveFrom, diveTo, 1f - (1f - k) * (1f - k)), 0.8f);
                            if (k >= 1f) { nextAttackAt = Time.time + Random.Range(1.2f, 2f); Enter(S.Recover); }
                        }
                        break;
                }
            }
            if (Time.time < rootedUntil || state == S.Windup || state == S.DiveWind || state == S.Dive) move = Vector3.zero;
            float sp = Speed * (Time.time < slowUntil ? 0.6f : 1f);
            if (move.sqrMagnitude > 0.001f) transform.position = Combat.ClampToArena(transform.position + move * sp * Time.deltaTime, 0.8f);
            Separate();
            Animate(move);
        }

        Vector3 Muzzle => transform.position + Vector3.up * Hover + transform.forward * 1.1f;

        void Enter(S s) { state = s; t = 0f; }

        void Face(Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 240f * Time.deltaTime);
        }

        void Separate()
        {
            foreach (var e in Combat.enemies)
            {
                if (!e || e == dmg || e.dead) continue;
                Vector3 d = Flat(transform.position - e.transform.position);
                float min = dmg.radius + e.radius;
                if (d.magnitude < min && d.magnitude > 0.01f) transform.position += d.normalized * (min - d.magnitude) * 0.5f;
            }
        }

        // 悬浮起伏、按横向速度倾斜（像鳐鱼滑翔），受击后仰
        void Animate(Vector3 move)
        {
            if (!body) return;
            phase += Time.deltaTime;
            Vector3 vel = (transform.position - lastPos) / Mathf.Max(1e-4f, Time.deltaTime);
            lastPos = transform.position;
            float side = Vector3.Dot(vel, transform.right);
            bank = Mathf.Lerp(bank, Mathf.Clamp(-side * 7f, -28f, 28f), Time.deltaTime * 4f);
            recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime * 80f);
            float bob = Mathf.Sin(phase * 2.1f) * 0.18f;
            float glide = Mathf.Sin(phase * 1.3f) * 4f;
            body.localPosition = new Vector3(0f, Hover + bob, 0f);
            body.localRotation = Quaternion.Euler(-recoil + (state == S.Windup ? -10f * Mathf.Clamp01(t / WindupT) : 0f), 0f, bank + glide);
        }

        void StartWindup(Vector3 to)
        {
            aimDir = to.normalized;
            Enter(S.Windup);
            dmg.Flash(new Color(0.3f, 1f, 0.95f), 0.6f);
            // 登记成威胁：AI 队友会看到这条弹道并闪避
            ClearThreat();
            float dist = Mathf.Min(Range, to.magnitude + 1f);
            threat = new Combat.Threat { owner = dmg, shape = AttackShape.Line(Flat(transform.position), aimDir, dist, 1.4f), resolveAt = Time.time + WindupT + dist / BoltSpeed };
            Combat.threats.Add(threat);
        }

        void Fire()
        {
            if (target) aimDir = Flat(target.transform.position - transform.position).normalized;
            EnemyBolt.Fire(Muzzle, aimDir, BoltSpeed, Range, BoltDmg * Coop.EnemyDmgMultAt(transform.position), gameObject, threat);
            threat = null;
            recoil = 14f;
            Feel.I.Sfx("dash", 0.25f, 0.1f, 1.6f);
            Enter(S.Recover);
        }

        void ClearThreat() { if (threat != null) Combat.threats.Remove(threat); threat = null; }

        void StartDive(Vector3 to)
        {
            aimDir = to.normalized;
            transform.rotation = Quaternion.LookRotation(aimDir);
            Enter(S.DiveWind);
            nextDiveAt = Time.time + Random.Range(8f, 11f);
            var s = AttackShape.Line(Flat(transform.position), aimDir, 10f, 1.8f);
            diveTel = s.Show(0.6f, new Color(1f, 0.3f, 0.16f, 0.75f));
            ClearThreat();
            threat = new Combat.Threat { owner = dmg, shape = s, resolveAt = Time.time + 0.6f };
            Combat.threats.Add(threat);
        }

        void Dive()
        {
            if (diveTel) diveTel.Resolve();
            var s = threat != null ? threat.shape : AttackShape.Line(Flat(transform.position), aimDir, 10f, 1.8f);
            ClearThreat();
            EnemySkills.HitHeroes(s, 60f * Coop.EnemyDmgMultAt(transform.position), 3f, false, gameObject);
            diveFrom = transform.position;
            diveTo = Combat.ClampToArena(transform.position + aimDir * 10f, 0.8f);
            Feel.I.Sfx("dash", 0.35f, 0.1f, 1.3f);
            FX.Tide(transform.position + Vector3.up, -aimDir, 12);
            Enter(S.Dive);
        }

        void OnHit(HitInfo h, float dealt)
        {
            Wake();
            if (dmg.dead) return;
            knockVel = Flat(h.dir) * h.knockback * 1.6f;
            recoil = h.heavy ? 26f : 14f;
            if (state == S.Windup || state == S.DiveWind) { ClearThreat(); if (diveTel) diveTel.Cancel(); Enter(S.Hurt); nextAttackAt = Time.time + 1f; }
        }

        void OnDeath()
        {
            ClearThreat();
            Enter(S.Dead);
            Feel.I.Sfx("enemy_death", 0.7f, 0.05f, 1.2f);
            FX.Tide(transform.position + Vector3.up * Hover, Vector3.up, 40);
            Combat.enemies.Remove(dmg);
            Coop.OnKill(dmg);
            foreach (var c in GetComponents<Collider>()) c.enabled = false;
        }

        // 死亡：坠落、翻过来、沉进地里
        void TickDead()
        {
            if (!body) { Destroy(gameObject); return; }
            float k = Mathf.Clamp01(t / 0.5f);
            body.localPosition = new Vector3(0f, Mathf.Lerp(Hover, 0.15f, k * k) - Mathf.Max(0f, t - 1.6f) * 0.8f, 0f);
            body.localRotation = Quaternion.Euler(-recoil, 0f, Mathf.Lerp(bank, 160f, k));
            if (t > 3f) Destroy(gameObject);
        }

        void OnDestroy() { ClearThreat(); }
    }

    // 敌方弹道：黑潮弹。能闪避（闪避中算完美闪避）、会被风障挡住
    public class EnemyBolt : MonoBehaviour
    {
        Vector3 vel, start;
        float range, damage;
        GameObject owner;
        Combat.Threat threat;
        Transform head;
        Camera cam;

        public static void Fire(Vector3 from, Vector3 dir, float speed, float range, float damage, GameObject owner, Combat.Threat threat)
        {
            var go = new GameObject("TideBolt");
            go.transform.position = from;
            var b = go.AddComponent<EnemyBolt>();
            b.vel = dir * speed; b.start = from; b.range = range; b.damage = damage; b.owner = owner; b.threat = threat;
            b.cam = Camera.main;
            var col = new Color(0.25f, 0.95f, 0.9f);
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.25f; tr.minVertexDistance = 0.05f; tr.widthMultiplier = 0.5f;
            tr.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            tr.material = FX.NewMat(SliceAssets.I.softDot, true, col, 2.5f);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.alignment = LineAlignment.View;
            var h = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(h.GetComponent<Collider>());
            h.transform.SetParent(go.transform, false);
            h.transform.localScale = Vector3.one * 0.85f;
            var r = h.GetComponent<Renderer>();
            r.sharedMaterial = FX.NewMat(SliceAssets.I.softDot, true, Color.Lerp(col, Color.white, 0.3f), 3f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            b.head = h.transform;
        }

        void Update()
        {
            Vector3 p0 = transform.position, p1 = p0 + vel * Time.deltaTime;
            if (WindWall.Crosses(new Vector3(p0.x, 0, p0.z), new Vector3(p1.x, 0, p1.z)))
            {
                FX.Ring(p1, 0.1f, 1.2f, 0.25f, new Color(0.4f, 1f, 0.85f, 0.9f), 0.08f);
                Die();
                return;
            }
            foreach (var h in Combat.heroes)
            {
                if (!h || h.dmg.dead) continue;
                Vector3 hp = h.transform.position + Vector3.up * 1.2f;
                if (DistToSeg(hp, p0, p1) > 0.85f) continue;
                if (h.Invulnerable)
                {
                    if (h.InPerfectWindow) h.PerfectDodge();
                    continue;
                }
                Vector3 dir = vel.normalized; dir.y = 0f;
                h.dmg.TakeHit(new HitInfo { damage = damage, knockback = 1.5f, dir = dir, kind = "bolt", attacker = owner });
                FX.Tide(hp, -dir, 14);
                Die();
                return;
            }
            transform.position = p1;
            if (head && cam) head.rotation = cam.transform.rotation;
            if (Vector3.Distance(start, p1) > range) Die();
        }

        static float DistToSeg(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }

        void Die()
        {
            if (threat != null) Combat.threats.Remove(threat);
            threat = null;
            if (head) Destroy(head.gameObject);
            enabled = false;
            Destroy(gameObject, 0.25f);
        }
    }
}

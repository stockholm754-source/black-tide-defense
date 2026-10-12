using UnityEngine;

namespace BlackTide
{
    // 巨浪·浮游水母（54 号模型，第四批）：浮在离地约 2 米处，慢慢飘向最近的人，停在约 4 米外；
    // 每 6 秒放一圈冲击波环（内 1.2 米、外 6 米，预警 1.2 秒）——贴近它或拉开距离都安全，站在中间最危险。
    // 能被旋流吸、被音锚定身、被风障减速（IMob）。
    // 2026-10-09 招式扩充：电流束缚——4–10 米外的人被一道电流拴住 2 秒，往水母身边拖、减速；闪避能挣断（9 秒一次）。
    public class DriftJelly : MonoBehaviour, IMob
    {
        public Damageable dmg;
        Transform body;
        float nextPulse, pulseAt = -1f, rootedUntil, slowUntil, pullUntil, bob;
        Vector3 pullCenter, home;
        float leash;
        AttackShape shape;
        Telegraph tel;
        Combat.Threat threat;
        bool dormant;
        float nextTether, tetherAt = -1f, tetherUntil;
        HeroBase tethered;
        LineRenderer beam;
        Telegraph tetherTel;
        System.Action onWake;

        public static DriftJelly Spawn(Vector3 p, float hpMult, float leash, System.Action wake)
        {
            p.y = 0f;
            var go = new GameObject("巨浪·浮游水母");
            go.transform.position = p;
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(go.transform, false);
            bodyGo.transform.localPosition = Vector3.up * 2f;
            var prop = SliceAssets.I.MapProp("54_drift_jelly");
            if (prop != null && prop.model)
                ArenaBuilder.Prop(bodyGo.transform, prop, "54_drift_jelly", p + Vector3.up * 2f, Quaternion.identity, 2.6f, true, Color.white, ArenaBuilder.Teal * 2.6f, 0.4f, out _);
            else
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(s.GetComponent<Collider>());
                s.transform.SetParent(bodyGo.transform, false);
                s.transform.localScale = new Vector3(2.4f, 1.2f, 2.4f);
                s.GetComponent<Renderer>().sharedMaterial = ArenaBuilder.Lit(null, new Color(0.06f, 0.1f, 0.14f), 0.6f);
            }
            var j = go.AddComponent<DriftJelly>();
            j.body = bodyGo.transform;
            j.home = p; j.leash = leash; j.onWake = wake;
            j.dmg = go.AddComponent<Damageable>();
            j.dmg.team = Team.Enemy;
            int team = Mathf.Max(1, Combat.heroes.Count);
            j.dmg.maxHp = j.dmg.hp = 560f * (team >= 3 ? 2.4f : team == 2 ? 1.7f : 1f) * hpMult;
            j.dmg.radius = 1.2f; j.dmg.height = 3f;
            j.dmg.tier = 1;
            j.dmg.onHit = (h, d) => j.Wake();
            j.dmg.onDeath = j.Die;
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = 1.1f; col.height = 2f; col.center = Vector3.up * 2f;
            j.dmg.RefreshRenderers();
            Combat.enemies.Add(j.dmg);
            j.dormant = true; j.dmg.dormant = true;
            j.nextPulse = Time.time + 3f;
            return j;
        }

        public void Pull(Vector3 center, float until) { pullCenter = center; pullUntil = Mathf.Max(pullUntil, until); }
        public void Root(float dur) { rootedUntil = Mathf.Max(rootedUntil, Time.time + dur); }
        public void Slow(float until) { slowUntil = Mathf.Max(slowUntil, until); }
        public void Wake()
        {
            if (!dormant) return;
            dormant = false; dmg.dormant = false;
            nextPulse = Time.time + 2f;
            onWake?.Invoke();
        }

        void Update()
        {
            if (!dmg || dmg.dead) return;
            bob += Time.deltaTime;
            body.localPosition = Vector3.up * (2f + Mathf.Sin(bob * 1.6f) * 0.25f);
            body.Rotate(0f, 20f * Time.deltaTime, 0f);
            Vector3 me = Flat(transform.position);
            if (Time.time < pullUntil)
            {
                Vector3 to = Flat(pullCenter) - me;
                if (to.magnitude > 0.6f) transform.position = Combat.ClampToArena(transform.position + to.normalized * Mathf.Min(5f, to.magnitude * 2f) * Time.deltaTime, 1f);
            }
            var near = Combat.NearestHero(transform.position);
            if (dormant)
            {
                if (near && Vector3.Distance(Flat(near.transform.position), me) < 12f) Wake();
                else return;
            }
            TickTether();
            if (pulseAt > 0f)
            {
                if (Time.time >= pulseAt) Pulse();
                return;
            }
            if (!near) return;
            Vector3 d = Flat(near.transform.position) - me;
            bool inLeash = leash <= 0f || Vector3.Distance(Flat(near.transform.position), Flat(home)) <= leash;
            if (inLeash && Time.time >= nextPulse && d.magnitude < 9f)
            {
                shape = AttackShape.Ring(me, 1.2f, 6f);
                tel = shape.Show(1.2f, new Color(1f, 0.42f, 0.18f, 0.7f));   // 敌方伤害范围统一红橙
                threat = new Combat.Threat { owner = dmg, shape = shape, resolveAt = Time.time + 1.2f };
                Combat.threats.Add(threat);
                pulseAt = Time.time + 1.2f;
                return;
            }
            bool rooted = Time.time < rootedUntil;
            Vector3 goal = inLeash ? Flat(near.transform.position) : Flat(home);
            Vector3 g = goal - me;
            float stop = inLeash ? 4f : 1f;
            if (!rooted && g.magnitude > stop)
            {
                float sp = 1.6f * (Time.time < slowUntil ? 0.6f : 1f);
                transform.position = Combat.ClampToArena(transform.position + g.normalized * sp * Time.deltaTime, 1f);
            }
        }

        void Pulse()
        {
            pulseAt = -1f; nextPulse = Time.time + 6f;
            if (tel) tel.Resolve();
            if (threat != null) { Combat.threats.Remove(threat); threat = null; }
            foreach (var h in Combat.heroes.ToArray())
            {
                if (!h || h.dmg.dead) continue;
                bool inside = shape.Contains(h.transform.position, 0.5f);
                if (h.Invulnerable) { if (h.InPerfectWindow && (inside || shape.Contains(h.dodgeStartPos, 0.5f))) h.PerfectDodge(); continue; }
                if (inside) h.dmg.TakeHit(new HitInfo { damage = 180f * Coop.EnemyDmgMultAt(transform.position), knockback = 4f, heavy = true, dir = Flat(h.transform.position - transform.position).normalized, kind = "minion", attacker = gameObject });
            }
            FX.Ring(shape.origin, 1.2f, 6.4f, 0.5f, new Color(0.35f, 1f, 0.9f, 1f), 0.35f);
            Feel.I.Sfx("boss_slam", 0.5f, 0.05f, 1.5f);
        }

        void TickTether()
        {
            Vector3 me = Flat(transform.position);
            if (tethered)
            {
                bool broke = tethered.Downed || tethered.Invulnerable || Time.time > tetherUntil;
                if (broke)
                {
                    if (tethered.Invulnerable && tethered.IsControlled) Combat.Toast("挣断了电流束缚", new Color(0.7f, 0.95f, 1f));
                    tethered = null; if (beam) beam.enabled = false; nextTether = Time.time + 9f;
                    return;
                }
                Vector3 hp = Flat(tethered.transform.position);
                Vector3 to = me - hp;
                if (to.magnitude > 2.5f) tethered.Push(to.normalized * 2.4f * Time.deltaTime);
                tethered.AddMod("speed", "tether", -0.35f, 0.2f);
                beam.enabled = true;
                beam.SetPosition(0, body.position); beam.SetPosition(1, tethered.transform.position + Vector3.up * 1.2f);
                if (Random.value < 0.3f) FX.Sparks(Vector3.Lerp(body.position, tethered.transform.position + Vector3.up * 1.2f, Random.value), Random.onUnitSphere, 1, new Color(0.6f, 0.95f, 1f));
                return;
            }
            if (tetherAt > 0f)
            {
                if (Time.time < tetherAt) return;
                tetherAt = -1f;
                if (tetherTel) { tetherTel.Resolve(); tetherTel = null; }
                var near = Combat.NearestHero(transform.position);
                if (!near || near.Invulnerable || Vector3.Distance(Flat(near.transform.position), me) > 11f) { nextTether = Time.time + 4f; return; }
                tethered = near; tetherUntil = Time.time + 2f;
                if (!beam)
                {
                    beam = new GameObject("Beam").AddComponent<LineRenderer>();
                    beam.transform.SetParent(transform, false);
                    beam.positionCount = 2; beam.widthMultiplier = 0.18f; beam.useWorldSpace = true;
                    beam.sharedMaterial = FX.NewMat(SliceAssets.I.softDot, true, new Color(0.55f, 0.95f, 1f), 3f);
                }
                Feel.I.Sfx("hit_armor", 0.4f, 0.1f, 1.8f);
                if (near.IsControlled) Combat.Toast("被电流束缚！（闪避挣断）", new Color(0.6f, 0.95f, 1f));
                return;
            }
            if (Time.time < nextTether || pulseAt > 0f) return;
            var h = Combat.NearestHero(transform.position);
            if (!h) return;
            float d = Vector3.Distance(Flat(h.transform.position), me);
            if (d < 4f || d > 10f) return;
            // 0.5 秒前摇：水母发亮，地上一条细线（不造成伤害，只是控制；登记成威胁让 AI 队友闪避）
            tetherAt = Time.time + 0.5f;
            dmg.Flash(new Color(0.6f, 0.95f, 1f), 0.8f);
            var s = AttackShape.Line(me, (Flat(h.transform.position) - me).normalized, d + 1f, 0.8f);
            tetherTel = s.Show(0.5f, new Color(0.55f, 0.85f, 1f, 0.6f));
            var thr = new Combat.Threat { owner = dmg, shape = s, resolveAt = tetherAt };
            Combat.threats.Add(thr);
            StartCoroutine(RemoveLater(thr, 0.55f));
        }

        System.Collections.IEnumerator RemoveLater(Combat.Threat th, float t) { yield return new WaitForSeconds(t); Combat.threats.Remove(th); }

        void Die()
        {
            if (beam) beam.enabled = false;
            if (tetherTel) tetherTel.Cancel();
            tethered = null;
            if (tel) tel.Cancel();
            if (threat != null) Combat.threats.Remove(threat);
            Combat.enemies.Remove(dmg);
            Coop.OnKill(dmg);
            FX.Tide(transform.position + Vector3.up * 2f, Vector3.up, 60);
            Destroy(gameObject, 0.05f);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}

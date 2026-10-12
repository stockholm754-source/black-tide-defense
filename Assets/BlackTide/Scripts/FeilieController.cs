using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    [System.Serializable]
    public class AttackDef
    {
        public string anim;
        public float speed = 1f;
        public float[] hits;          // 命中时刻（动画归一化时间）
        public float cancelAt, endAt;  // 可接下一招 / 结束
        public float lunge, lungeFrom, lungeTo;
        public float range, arc;
        public float dmg, stagger, knock;
        public float hitstop, shake;
        public float arcRoll;          // 斩光平面的滚转角：0 横斩，90 竖劈
        public string swingSfx;
        public float swingAt;
        public bool heavyLast;
    }

    // 绯烈：主C · 长刃 · 热熔 —— 三段连招、断焰突进叠灼痕、焚城消耗灼痕（打失衡目标 ×1.6）
    // 被动：造成伤害的 6% 转为护盾（上限 30% 生命，5 秒不打就衰减）
    public class FeilieController : HeroBase
    {
        public override HeroId Id => HeroId.Feilie;
        public override string HeroName => "绯烈";
        public override string Role => "主C";
        public static readonly Color Col = new Color(1f, 0.42f, 0.28f);
        public override Color Color => Col;
        public override string SkillName => "断焰";
        public override string UltName => "焚城";
        public override float SkillCd => 6f;
        public override string PipName => "灼痕";
        public override int Pips => embers;
        public override int PipMax => 5;
        protected override string IdleAnim => "2H_Melee_Idle";
        protected override bool AuthoredAttackMotion => !UseGenericBasic;

        BladeTrail trail;
        Renderer swordRenderer;
        public int embers;
        float shieldGainT;

        AttackDef[] combo;
        AttackDef counterAtk;
        AttackDef cur;
        int comboIdx;
        float lastAttackEnd = -9f;
        int nextHit;
        bool swung;
        readonly HashSet<Damageable> hitThisSwing = new HashSet<Damageable>();
        readonly List<Damageable> tmp = new List<Damageable>();
        Vector3 lungeDir;
        float lungeDone, lungeTotal;
        Vector3 dashFrom, dashTo;
        bool ultFired;

        public void Setup(GameObject model, Transform swordBase, Transform swordTip, Renderer sword)
        {
            SetupBase(model, 1600f);
            trail = gameObject.AddComponent<BladeTrail>();
            trail.baseT = swordBase; trail.tipT = swordTip;
            swordRenderer = sword;

            combo = new[]
            {
                new AttackDef { anim = "2H_Melee_Attack_Slice", speed = 1.35f, hits = new[] { 0.36f }, cancelAt = 0.52f, endAt = 0.78f, lunge = 1.4f, lungeFrom = 0.18f, lungeTo = 0.38f,
                    range = 3.5f, arc = 160f, dmg = 1.15f, stagger = 14f, knock = 2.5f, hitstop = 0.06f, shake = 0.16f, arcRoll = 0f, swingSfx = "swing_1", swingAt = 0.27f },
                new AttackDef { anim = "2H_Melee_Attack_Chop", speed = 1.5f, hits = new[] { 0.5f }, cancelAt = 0.62f, endAt = 0.84f, lunge = 1.7f, lungeFrom = 0.38f, lungeTo = 0.52f,
                    range = 3.9f, arc = 70f, dmg = 1.6f, stagger = 20f, knock = 3f, hitstop = 0.085f, shake = 0.3f, arcRoll = 90f, swingSfx = "swing_2", swingAt = 0.42f },
                new AttackDef { anim = "2H_Melee_Attack_Spin", speed = 1.3f, hits = new[] { 0.3f, 0.5f }, cancelAt = 0.6f, endAt = 0.74f, lunge = 1.2f, lungeFrom = 0.2f, lungeTo = 0.5f,
                    range = 3.7f, arc = 360f, dmg = 1.3f, stagger = 18f, knock = 5f, hitstop = 0.075f, shake = 0.3f, arcRoll = 0f, swingSfx = "swing_heavy", swingAt = 0.24f, heavyLast = true },
            };
            counterAtk = new AttackDef { anim = "2H_Melee_Attack_Slice", speed = 1.9f, hits = new[] { 0.36f }, cancelAt = 0.5f, endAt = 0.72f, lunge = 2.6f, lungeFrom = 0.1f, lungeTo = 0.36f,
                range = 4.2f, arc = 200f, dmg = 3.2f, stagger = 70f, knock = 4f, hitstop = 0.15f, shake = 0.55f, arcRoll = 0f, swingSfx = "swing_heavy", swingAt = 0.25f, heavyLast = true };

            anim.Play(IdleAnim, 0f);
        }

        protected override void OnEnter(State s) { if (trail) trail.emitting = false; }
        protected override void SetNativeWeaponVisible(bool v) { if (swordRenderer) swordRenderer.enabled = v; if (trail) trail.emitting = false; }

        protected override void LateTick()
        {
            // 剑的热熔辉光随灼痕层数变亮；反击就绪时发白
            if (swordRenderer)
            {
                Color glow = Time.time < counterUntil ? new Color(2.2f, 2.0f, 1.8f) : new Color(1f, 0.38f, 0.1f) * (0.25f + embers * 0.45f + (state == State.Ult ? 2.5f : 0f));
                dmg.SetBaseEmission(swordRenderer, glow);
            }
            // 被动护盾：5 秒没有造成伤害就开始衰减
            if (dmg.shield > 0f && shieldUntil <= 0f && Time.time - shieldGainT > 5f)
                dmg.shield = Mathf.Max(0f, dmg.shield - dmg.maxHp * 0.05f * Time.deltaTime);
        }

        protected override void OnDealt(float dealt)
        {
            dmg.shield = Mathf.Min(dmg.maxHp * 0.3f, dmg.shield + dealt * 0.06f);
            shieldGainT = Time.time;
        }

        protected override void ShieldExpired() { } // 澄音给的护盾到期后，剩下的按被动规则衰减

        static Vector3 Fl(Vector3 v) { v.y = 0f; return v; }

        // ---------------- 普攻 ----------------
        protected override void StartBasic(bool counter)
        {
            if (counter) { StartAttack(counterAtk, true); return; }
            if (state != State.Attack && Time.time - lastAttackEnd > 0.55f) comboIdx = 0;
            StartAttack(combo[comboIdx], false);
            comboIdx = (comboIdx + 1) % combo.Length;
        }

        void StartAttack(AttackDef a, bool counter)
        {
            cur = a;
            Enter(State.Attack);
            nextHit = 0; swung = false; lungeDone = 0f;
            hitThisSwing.Clear();
            // 软锁定：瞄准方向 70° 内最近的敌人
            Vector3 aim = AimDir();
            var t = AttackTarget(6f, 70f);
            Vector3 dir = aim;
            if (t) { dir = Fl(t.transform.position - transform.position).normalized; }
            Face(dir, Mathf.Max(720f, 180f / Mathf.Max(0.06f, a.hits[0] * anim.Length(a.anim) / a.speed - 0.03f)));
            lungeDir = dir;
            float lunge = a.lunge;
            if (t)
            {
                float gap = Vector3.Distance(Fl(t.transform.position), Fl(transform.position)) - t.radius - 1.3f;
                lunge = Mathf.Clamp(gap, 0f, a.lunge * 1.6f);
            }
            lungeTotal = lunge;
            anim.Play(a.anim, ActionBlend(a.hits[0] * anim.Length(a.anim) / a.speed), a.speed);
            if (counter) { if (FeelOn) Feel.I.Sfx("ult_charge", 0.5f, 0.05f, 1.6f); trail.color = new Color(1f, 0.95f, 0.85f); }
            else trail.color = new Color(1f, 0.5f, 0.18f);
            AddOverclock("basic");
        }

        protected override void TickAttack()
        {
            float len = anim.Length(cur.anim) / cur.speed;
            float n = stateT / len;
            if (nextHit == 0) lungeDir = TrackedAttackDirection(lungeDir);
            if (nextHit == 0) Face(lungeDir, Mathf.Max(720f, 180f / Mathf.Max(0.06f, cur.hits[0] * len - 0.03f)));

            float lt = Mathf.InverseLerp(cur.lungeFrom, cur.lungeTo, n);
            float eased = Mathf.SmoothStep(0f, 1f, lt);
            float step = (eased - lungeDone) * lungeTotal;
            lungeDone = eased;
            Gravity(AttackDisplacement(lungeDir, step));

            trail.emitting = n > cur.hits[0] - 0.12f && n < cur.hits[cur.hits.Length - 1] + 0.08f;
            if (!swung && n >= cur.swingAt) { swung = true; Feel.I.Sfx(cur.swingSfx, FeelOn ? 0.75f : 0.3f, 0.08f); }

            while (nextHit < cur.hits.Length && n >= cur.hits[nextHit])
            {
                bool last = nextHit == cur.hits.Length - 1;
                if (cur.hits.Length > 1) hitThisSwing.Clear();
                DoHit(cur, last && cur.heavyLast);
                nextHit++;
            }

            // 闪避任何时刻都能取消；技能/大招/接力在第一次命中后可取消；过了 cancelAt 可以接下一段
            if (Buffered(ref bufDodge)) { StartDodge(); return; }
            if (n > cur.hits[0] && TryActions(false)) return;
            if (n >= cur.cancelAt && (Buffered(ref bufAttack) || attackHeld))
            {
                bufAttack = Time.time;
                TryActions(true);
                return;
            }
            if (n >= cur.endAt)
            {
                lastAttackEnd = Time.time;
                Enter(State.Move);
            }
        }

        void DoHit(AttackDef a, bool heavy)
        {
            Vector3 fwd = transform.forward;
            Vector3 c = transform.position + Vector3.up * 1.15f;
            Quaternion arcRot = Quaternion.LookRotation(fwd) * Quaternion.Euler(0f, 0f, a.arcRoll);
            float arcAng = Mathf.Min(a.arc, 240f);
            Color arcCol = a == counterAtk ? new Color(1f, 0.95f, 0.85f, 0.9f) : new Color(1f, 0.5f, 0.16f, 0.85f);
            // 弧面从剑实际挥过来的那一侧扫出去
            int sweep = Vector3.Dot(trail.TipVel, transform.right) >= 0f ? 1 : -1;
            bool chop = a.arcRoll > 45f;
            Vector3 cut = chop ? Vector3.down : transform.right * sweep;
            // 下劈：弧面立在身前，从头顶劈到脚前的地面
            if (chop) FX.SlashArc(c, Quaternion.LookRotation(fwd + Vector3.up * 0.84f, transform.right), a.range * 0.4f, a.range * 0.8f, 110f, 0.12f, arcCol, 1);
            else FX.SlashArc(c, arcRot, a.range * 0.55f, a.range + 0.2f, a.arc >= 359f ? 340f : arcAng, 0.12f, arcCol, sweep);

            Combat.Gather(transform.position, fwd, a.range, a.arc, tmp);
            int landed = 0;
            foreach (var e in tmp.ToArray())
            {
                if (hitThisSwing.Contains(e)) continue;
                hitThisSwing.Add(e);
                Vector3 dir = Fl(e.transform.position - transform.position).normalized;
                float dealt = DealHit(e, a.dmg, a.stagger, a.knock, dir, heavy || a == counterAtk, "melee");
                if (dealt <= 0f) continue;
                landed++;
                Vector3 hp = e.transform.position + Vector3.up * Mathf.Min(1.6f, e.height * 0.45f) - dir * e.radius * 0.6f;
                FX.Sparks(hp, dir + Vector3.up * 0.3f, heavy ? 40 : 24, new Color(1f, 0.75f, 0.4f));
                Color hitCol = a == counterAtk ? new Color(0.9f, 0.95f, 1f) : new Color(1f, 0.7f, 0.35f);
                FX.Impact(hp, cut, heavy ? 3.2f : 2.2f, hitCol, heavy);
                FX.Tide(hp, dir, heavy ? 14 : 7);
                Gain(heavy ? 7f : 4.5f, a == counterAtk ? 12f : 1.9f);
            }
            if (landed > 0 && FeelOn)
            {
                float hs = a.hitstop * (heavy ? 1.6f : 1f) + Mathf.Min(landed - 1, 3) * 0.01f;
                Feel.I.HitStop(hs);
                Feel.I.Shake(a.shake * (heavy ? 1.5f : 1f));
                if (heavy) { Feel.I.PulseHit(0.7f); Feel.I.Sfx("hit_heavy", 0.95f); }
                else Feel.I.SfxRandom("hit", 3, 0.85f);
            }
            else if (landed > 0) Feel.I.SfxRandom("hit", 3, 0.35f);
            if (landed > 0 && a.arcRoll > 45f) { FX.Ring(transform.position + transform.forward * 2.4f, 0.4f, 2.6f, 0.3f, new Color(1f, 0.55f, 0.25f, 0.8f), 0.18f, true); FX.Dust(transform.position + transform.forward * 2.4f, 6, new Color(0.35f, 0.33f, 0.36f, 0.7f)); }
        }

        // ---------------- 断焰（E）：突进斩，路径上的敌人都挨打，叠 1 层灼痕 ----------------
        protected override void StartSkill()
        {
            Vector3 aim = AimDir();
            var t = Combat.NearestEnemy(transform.position, aim, 9f, 50f);
            Vector3 dir = aim;
            float dist = 7f;
            if (t)
            {
                dir = Fl(t.transform.position - transform.position).normalized;
                dist = Mathf.Clamp(Vector3.Distance(Fl(t.transform.position), Fl(transform.position)) + t.radius * 0.5f + 1.5f, 3f, 8f);
            }
            Face(dir, 0f);
            if (embers >= 5 && Rogue.EmberCdCut(this)) skillReadyAt = Time.time + (skillReadyAt - Time.time) * 0.5f;   // 熔火余烬 Lv3：灼痕满层时断焰冷却减半
            dashFrom = transform.position;
            dashTo = Combat.ClampToArena(transform.position + dir * dist, 0.8f);
            Enter(State.Skill);
            invulnUntil = Time.time + 0.32f;
            hitThisSwing.Clear();
            anim.Play("2H_Melee_Attack_Stab", 0.04f, 1.8f, 0.05f);
            trail.color = new Color(1f, 0.45f, 0.12f);
            Feel.I.Sfx("dash", FeelOn ? 0.9f : 0.35f, 0.05f);
            Gain(0f, 9f);
        }

        protected override void TickSkill()
        {
            const float dashT = 0.17f;
            float k = Mathf.Clamp01(stateT / dashT);
            Vector3 target = Vector3.Lerp(dashFrom, dashTo, 1f - (1f - k) * (1f - k));
            Vector3 delta = target - transform.position; delta.y = 0f;
            Gravity(delta);
            trail.emitting = stateT < dashT + 0.05f;
            if (stateT < dashT + 0.02f)
            {
                FX.Embers(transform.position + Vector3.up, 2);
                var line = AttackShape.Line(dashFrom, dashTo - dashFrom, Vector3.Distance(dashFrom, transform.position) + 1f, 3.2f);
                foreach (var e in Combat.enemies.ToArray())
                {
                    if (!e || e.dead || hitThisSwing.Contains(e)) continue;
                    if (!line.Contains(e.transform.position, e.radius)) continue;
                    hitThisSwing.Add(e);
                    float dealt = DealHit(e, 2.2f, 28f, 3.5f, (dashTo - dashFrom).normalized, true, "skill");
                    if (dealt <= 0f) continue;
                    Vector3 hp = e.transform.position + Vector3.up * Mathf.Min(1.6f, e.height * 0.45f);
                    FX.Sparks(hp, (dashTo - dashFrom).normalized, 34, new Color(1f, 0.6f, 0.25f));
                    FX.ImpactFlash(hp, 3f, new Color(1f, 0.6f, 0.25f));
                    FX.Embers(hp, 14);
                    if (FeelOn) { Feel.I.HitStop(0.085f); Feel.I.Shake(0.35f); Feel.I.Sfx("hit_heavy", 0.9f); }
                    Gain(12f, 0f);
                    embers = Mathf.Min(5, embers + Rogue.EmberGain(this));   // 熔火余烬：一次叠 2 层
                }
            }
            if (stateT > dashT && TryActions(false)) return;
            if (stateT > 0.42f) { if (!TryActions(true)) Enter(State.Move); }
        }

        // ---------------- 焚城（Q）：消耗灼痕的蓄力斩，打失衡目标 ×1.6 ----------------
        protected override void StartUlt()
        {
            Vector3 aim = AimDir();
            var t = Combat.NearestEnemy(transform.position, aim, 8f, 80f);
            Face(t ? Fl(t.transform.position - transform.position).normalized : aim, 0f);
            Enter(State.Ult);
            ultFired = false;
            anim.Play("2H_Melee_Attack_Chop", 0.05f, 0.95f);
            Feel.I.Sfx("ult_charge", FeelOn ? 1f : 0.45f, 0.02f);
            trail.color = new Color(1f, 0.6f, 0.2f);
        }

        protected override void TickUlt()
        {
            float len = anim.Length("2H_Melee_Attack_Chop") / 0.95f;
            float n = stateT / len;
            Gravity(Vector3.zero);
            if (!ultFired && n < 0.5f && Random.value < 0.6f) FX.Embers(transform.position + Vector3.up * Random.Range(0.3f, 2.4f) + Random.insideUnitSphere * 1.2f, 1);
            trail.emitting = n > 0.4f && n < 0.6f;
            if (!ultFired && n >= 0.5f)
            {
                ultFired = true;
                Vector3 center = transform.position + transform.forward * 2.2f;
                float mult = 5.2f + 1.4f * embers;
                int landed = 0;
                foreach (var e in Combat.enemies.ToArray())
                {
                    if (!e || e.dead) continue;
                    if (!AttackShape.Circle(center, 5.6f).Contains(e.transform.position, e.radius)) continue;
                    float dealt = DealHit(e, mult, 130f, 7f, Fl(e.transform.position - transform.position).normalized, true, "ult", 0.6f);
                    if (dealt <= 0f) continue;
                    landed++;
                    FX.Sparks(e.Center, Vector3.up, 40, new Color(1f, 0.6f, 0.25f));
                }
                if (embers > 0 && Rogue.EmberHeal(this) > 0f) Heal(this, dmg.maxHp * Rogue.EmberHeal(this) * embers);   // 熔火余烬：按消耗的灼痕回血
                embers = 0;
                Gain(0f, 10f);
                FX.Ring(center, 0.5f, 6.5f, 0.5f, new Color(1f, 0.5f, 0.15f, 1f), 0.22f, true);
                FX.Ring(center, 0.3f, 4f, 0.35f, new Color(1f, 0.85f, 0.6f, 1f), 0.1f);
                FX.Embers(center, 60);
                FX.Dust(center, 14, new Color(0.3f, 0.28f, 0.3f, 0.8f));
                FX.Sparks(center, Vector3.up, 50, new Color(1f, 0.7f, 0.3f));
                Feel.I.Sfx("ult_hit", FeelOn ? 1f : 0.5f, 0.02f);
                if (FeelOn)
                {
                    Feel.I.HitStop(landed > 0 ? 0.22f : 0.1f);
                    Feel.I.Shake(1f);
                    Feel.I.PulseHit(1f);
                }
                else Feel.I.Shake(0.25f);
            }
            if (n >= 0.82f) Enter(State.Move);
        }

        // ---------------- 接力 ----------------
        // 变奏：回旋斩一圈；延奏：接力对象 8 秒内伤害 +15%
        protected override void DoIntro()
        {
            anim.Play("2H_Melee_Attack_Spinning", 0.05f, 1.4f);
            trail.color = new Color(1f, 0.55f, 0.2f);
            trail.emitting = true;
            Vector3 c = transform.position;
            foreach (var e in Combat.enemies.ToArray())
                if (e && !e.dead && Vector3.Distance(Fl(e.transform.position), Fl(c)) <= 3.4f + e.radius)
                    DealHit(e, 3f, 30f, 4f, Fl(e.transform.position - c).normalized, true, "intro");
            FX.Ring(c, 0.4f, 3.6f, 0.35f, new Color(1f, 0.55f, 0.2f, 1f), 0.16f);
            FX.Embers(c + Vector3.up, 30);
            Feel.I.Sfx("swing_heavy", FeelOn ? 0.9f : 0.4f, 0.05f);
        }

        protected override void ApplyOutro(HeroBase to) { to.AddMod("dmg", "outro_feilie", 0.15f, 8f); }
    }
}

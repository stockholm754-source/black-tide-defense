using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // HeroBase 的武器部分（2026-10-09，见 Run/Weapons.cs 的体系说明）：
    //   · 手持原生种类 → 角色自己的普攻（StartBasic / TickAttack）；非原生种类 → 下面的通用普攻模组，伤害 ×0.87；
    //   · V 放手持武器的武器技能（State.WSkill）；X / 滚轮切换武器栏；
    //   · 手上的模型：非原生武器挂在手上（双刃、双枪两只手），同时藏起角色原来的武器。
    public abstract partial class HeroBase
    {
        [HideInInspector] public float bufWeapon = -9f;
        readonly List<GameObject> held = new List<GameObject>();
        public bool UseGenericBasic => kit.Active != null && kit.Active.def.type != Weapons.NativeType(Id);
        public bool WeaponSkillReady => kit.Active != null && Time.time >= kit.Active.skillReadyAt;

        // 原生武器的显示（各角色覆盖：绯烈的剑、青岚的手枪、澄音的音感仪）
        protected virtual void SetNativeWeaponVisible(bool v) { }

        public void RefreshWeapon()
        {
            kit.Rebuild();
            foreach (var g in held) if (g) { g.SetActive(false); Destroy(g); }   // 先隐藏：Destroy 要到帧末才生效
            held.Clear();
            bool generic = UseGenericBasic;
            SetNativeWeaponVisible(!generic);
            if (!generic || anim == null || anim.animator == null) return;
            var w = kit.Active;
            var prefab = Weapons.Model(w.def, out _, out _);
            if (!prefab) return;
            var mat = Weapons.Mat(w.def);
            float len = Weapons.HoldLength(w.def.type);
            var ty = w.def.type;
            bool headFar = ty == WType.重武器 || ty == WType.长柄 || ty == WType.法杖 || ty == WType.音感仪;
            float grip = ty == WType.长柄 || ty == WType.法杖 ? 0.3f : ty == WType.重武器 ? 0.12f : 0.22f;
            var r = HandHeld.Attach(transform, anim.animator, HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm, prefab, mat, len, headFar, grip);
            if (r) { r.gameObject.name = "Held"; held.Add(r.gameObject); }
            if (Weapons.TwoHanded(w.def.type))
            {
                var l = HandHeld.Attach(transform, anim.animator, HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm, prefab, mat, len, headFar, grip);
                if (l) { l.gameObject.name = "Held"; held.Add(l.gameObject); }
            }
        }

        public void SwitchWeapon(int dir)
        {
            int n = kit.active;
            for (int i = 1; i <= 3; i++)
            {
                int k = ((kit.active + dir * i) % 3 + 3) % 3;
                if (kit.weapons[k] != null) { n = k; break; }
            }
            if (n == kit.active) return;
            kit.active = n;
            RefreshWeapon();
            if (IsControlled)
            {
                Combat.Toast($"切换到 {kit.Active.Name}（{kit.Active.def.type}）· V「{kit.Active.SkillDef.name}」", Weapons.StarCol[kit.Active.star]);
                Feel.I.Sfx("swing_1", 0.4f, 0.05f, 1.6f);
            }
        }

        // ---------------- 通用普攻模组 ----------------
        class Mv
        {
            public string anim; public float speed = 1f, hitT, endT, cancelT;
            public int shape;                 // 0 扇形、1 直线、2 弹道、3 圆
            public float range, arc, mult, stagger, knock, lunge;
            public bool heavy;
        }
        static Mv M(string a, float sp, float hit, float end, float cancel, int shape, float range, float arc, float mult, float stagger, float knock, float lunge = 0f, bool heavy = false)
            => new Mv { anim = a, speed = sp, hitT = hit, endT = end, cancelT = cancel, shape = shape, range = range, arc = arc, mult = mult, stagger = stagger, knock = knock, lunge = lunge, heavy = heavy };
        static readonly Dictionary<WType, Mv[]> Moves = new Dictionary<WType, Mv[]>
        {
            // 长刃：均衡三段（和绯烈同一套动作，数值略低）
            { WType.长刃, new[] { M("2H_Melee_Attack_Slice", 1.35f, 0.27f, 0.58f, 0.42f, 0, 3.5f, 150f, 1.1f, 14f, 2.5f, 1.2f), M("2H_Melee_Attack_Chop", 1.5f, 0.33f, 0.62f, 0.46f, 0, 3.8f, 80f, 1.5f, 20f, 3f, 1.4f), M("2H_Melee_Attack_Spin", 1.3f, 0.3f, 0.64f, 0.5f, 3, 3.6f, 360f, 1.4f, 18f, 5f, 1f, true) } },
            // 重武器：慢、重、架势高
            { WType.重武器, new[] { M("2H_Melee_Attack_Chop", 1.1f, 0.45f, 0.85f, 0.65f, 0, 3.9f, 120f, 2.1f, 45f, 4f, 0.8f, true), M("2H_Melee_Attack_Spin", 1.0f, 0.45f, 0.9f, 0.7f, 3, 4f, 360f, 2.4f, 55f, 6f, 0.6f, true) } },
            // 长柄：距离远、直线
            { WType.长柄, new[] { M("2H_Melee_Attack_Stab", 1.5f, 0.25f, 0.5f, 0.38f, 1, 4.6f, 1.6f, 1.05f, 12f, 2f, 0.8f), M("2H_Melee_Attack_Stab", 1.6f, 0.24f, 0.48f, 0.36f, 1, 4.6f, 1.6f, 1.15f, 12f, 2f, 0.8f), M("2H_Melee_Attack_Slice", 1.3f, 0.3f, 0.6f, 0.45f, 0, 4.3f, 180f, 1.4f, 20f, 3f, 0.6f, true) } },
            // 双刃：快、短、多段
            { WType.双刃, new[] { M("Dualwield_Melee_Attack_Slice", 1.9f, 0.16f, 0.36f, 0.26f, 0, 2.8f, 120f, 0.65f, 7f, 1f, 0.8f), M("Dualwield_Melee_Attack_Chop", 2f, 0.16f, 0.36f, 0.26f, 0, 2.8f, 120f, 0.65f, 7f, 1f, 0.8f), M("Dualwield_Melee_Attack_Stab", 2f, 0.16f, 0.38f, 0.28f, 1, 3.3f, 1.4f, 0.7f, 8f, 1f, 1f), M("Dualwield_Melee_Attack_Slice", 1.7f, 0.2f, 0.45f, 0.32f, 3, 2.8f, 360f, 0.9f, 12f, 3f, 0.4f, true) } },
            // 法杖：中距离施法弹
            { WType.法杖, new[] { M("Spellcast_Shoot", 1.8f, 0.2f, 0.45f, 0.3f, 2, 12f, 0.35f, 0.95f, 8f, 1f), M("Spellcast_Shoot", 1.9f, 0.19f, 0.43f, 0.3f, 2, 12f, 0.35f, 0.95f, 8f, 1f), M("Spellcast_Shoot", 1.6f, 0.24f, 0.55f, 0.38f, 2, 13f, 0.6f, 1.5f, 20f, 3f, 0f, true) } },
            // 双枪：快速连射
            { WType.双枪, new[] { M("1H_Ranged_Shoot", 2.4f, 0.1f, 0.28f, 0.2f, 2, 11f, 0.3f, 0.55f, 5f, 0.5f), M("1H_Ranged_Shoot", 2.4f, 0.1f, 0.28f, 0.2f, 2, 11f, 0.3f, 0.55f, 5f, 0.5f), M("1H_Ranged_Shoot", 1.8f, 0.14f, 0.4f, 0.3f, 2, 12f, 0.5f, 0.95f, 12f, 1.5f, 0f, true) } },
            // 音感仪：音波弹，命中后给身边最缺血的队友一点回复
            { WType.音感仪, new[] { M("Spellcast_Shoot", 2f, 0.2f, 0.45f, 0.3f, 2, 10f, 0.5f, 0.85f, 6f, 1f), M("Spellcast_Shoot", 2f, 0.2f, 0.45f, 0.3f, 2, 10f, 0.5f, 0.85f, 6f, 1f), M("Spellcast_Shoot", 1.7f, 0.24f, 0.55f, 0.38f, 2, 11f, 0.8f, 1.3f, 12f, 2f, 0f, true) } },
        };
        int gIdx; float gLastEnd = -9f; bool gFired, gAttacking; Mv gCur;
        Vector3 gAimDir;
        float gLungeDone;
        static readonly List<Damageable> gTmp = new List<Damageable>();

        void GenericStartBasic(bool counter)
        {
            var mvs = Moves[kit.Active.def.type];
            if (Time.time - gLastEnd > 0.8f) gIdx = 0;
            gCur = mvs[gIdx % mvs.Length];
            gIdx++;
            gFired = false; gAttacking = true;
            Vector3 aim = AimDir();
            var t = AttackTarget(gCur.shape == 2 ? 14f : 6f, 70f);
            gAimDir = t ? Flat(t.transform.position - transform.position).normalized : aim;
            gLungeDone = 0f;
            Face(gAimDir, gCur.shape == 2 ? 0f : Mathf.Max(720f, 180f / Mathf.Max(0.06f, gCur.hitT - 0.03f)));
            Enter(State.Attack);
            gAttacking = true;
            anim.Play(gCur.anim, ActionBlend(gCur.hitT), gCur.speed);
            if (counter) { gCur = new Mv { anim = gCur.anim, speed = gCur.speed, hitT = gCur.hitT, endT = gCur.endT, cancelT = gCur.cancelT, shape = gCur.shape, range = gCur.range * 1.15f, arc = Mathf.Max(gCur.arc, 160f), mult = gCur.mult * 2.6f, stagger = gCur.stagger * 3f, knock = gCur.knock, lunge = gCur.lunge, heavy = true }; }
            Feel.I.Sfx(gCur.heavy ? "swing_heavy" : "swing_1", FeelOn ? 0.55f : 0.2f, 0.1f, Weapons.Ranged(kit.Active.def.type) ? 1.8f : 1f);
        }

        void GenericTickBasic()
        {
            float t = stateT;
            if (!gFired) gAimDir = TrackedAttackDirection(gAimDir);
            if (!gFired) Face(gAimDir, Mathf.Max(720f, 180f / Mathf.Max(0.06f, gCur.hitT - 0.03f)));
            Vector3 fwd = transform.forward;
            if (gCur.lunge > 0f && gLungeDone < 1f)
            {
                float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / gCur.hitT));
                Gravity(AttackDisplacement(gAimDir, (progress - gLungeDone) * gCur.lunge));
                gLungeDone = progress;
            }
            else Gravity(Vector3.zero);
            if (!gFired && t >= gCur.hitT) { gFired = true; GenericHit(); }
            if (Buffered(ref bufDodge)) { gAttacking = false; gLastEnd = Time.time; StartDodge(); return; }
            if (t >= gCur.cancelT && attackHeld) bufAttack = Time.time;
            if (t >= gCur.cancelT && TryActions(true)) { gLastEnd = Time.time; return; }
            if (t >= gCur.endT) { gAttacking = false; gLastEnd = Time.time; Enter(State.Move); }
        }

        // 带"漫画"标记的武器：这把武器打出来的斩击、命中、冲击环、火花都换成美漫画法
        bool ComicWeapon => kit.Active != null && kit.Active.def.comic;

        void GenericHit()
        {
            FX.Comic = FX.ComicStyle;
            try { GenericHitCore(); } finally { FX.Comic = FX.ComicStyle; }
        }

        void GenericHitCore()
        {
            var w = kit.Active; Color col = Weapons.StarCol[w.star];
            Vector3 c = Flat(transform.position), fwd = Flat(transform.forward).normalized;
            if (gCur.shape == 2)
            {
                Vector3 from = transform.position + Vector3.up * 1.3f + fwd * 0.6f;
                var t = Combat.NearestEnemy(transform.position, fwd, gCur.range, 40f);
                Vector3 dir = t ? Flat(t.transform.position - transform.position).normalized : fwd;
                Color pc = w.def.type == WType.法杖 ? new Color(0.5f, 0.9f, 1f) : w.def.type == WType.音感仪 ? new Color(0.95f, 0.85f, 0.5f) : new Color(1f, 0.85f, 0.6f);
                Projectile.Fire(this, from, dir, w.def.type == WType.双枪 ? 30f : 22f, gCur.range, gCur.arc, gCur.mult * 0.87f, gCur.stagger, pc, gCur.heavy ? 0.8f : 0.5f, gCur.heavy, "shot");
                FX.ImpactFlash(from, 0.8f, pc);
                if (w.def.type == WType.音感仪)
                {
                    HeroBase low = null; float lf = 0.95f;
                    foreach (var o in Coop.Alive()) { float f = o.dmg.hp / o.dmg.maxHp; if (f < lf && Vector3.Distance(o.transform.position, transform.position) < 10f) { lf = f; low = o; } }
                    if (low) Heal(low, low.dmg.maxHp * (gCur.heavy ? 0.02f : 0.01f));
                }
                AddOverclock("basic");
                return;
            }
            int gSweep = gIdx % 2 == 0 ? 1 : -1;   // 连段左右交替挥
            AttackShape s = gCur.shape == 1 ? AttackShape.Line(c, fwd, gCur.range, gCur.arc) : gCur.shape == 3 ? AttackShape.Circle(c, gCur.range) : AttackShape.Cone(c, fwd, gCur.range, gCur.arc);
            int landed = 0;
            foreach (var e in Combat.enemies.ToArray())
            {
                if (!e || e.dead || !s.Contains(e.transform.position, e.radius)) continue;
                Vector3 d = Flat(e.transform.position - transform.position); if (d.sqrMagnitude < 0.01f) d = fwd;
                if (DealHit(e, gCur.mult * 0.87f, gCur.stagger, gCur.knock, d.normalized, gCur.heavy, "melee") > 0f)
                {
                    landed++;
                    Vector3 hp = e.transform.position + Vector3.up * Mathf.Min(1.6f, e.height * 0.45f);
                    FX.Sparks(hp, d.normalized + Vector3.up * 0.3f, gCur.heavy ? 24 : 12, Color.Lerp(col, Color.white, 0.4f));
                    FX.Impact(hp, gCur.shape == 1 ? fwd : transform.right * gSweep, gCur.heavy ? 2.6f : 1.8f, col, gCur.heavy);
                    Gain(gCur.heavy ? 6f : 3.5f, 1.5f);
                }
            }
            Vector3 ac = transform.position + Vector3.up * 1.15f;
            if (gCur.shape == 1) { for (int i = 1; i <= 5; i++) FX.Sparks(ac + fwd * gCur.range * i / 5f, fwd, 2, col); }
            else FX.SlashArc(ac, Quaternion.LookRotation(fwd), gCur.range * 0.5f, gCur.range, gCur.shape == 3 ? 340f : Mathf.Min(gCur.arc, 240f), 0.12f, new Color(col.r, col.g, col.b, 0.85f), gSweep);
            if (landed > 0)
            {
                if (FeelOn) { Feel.I.HitStop(gCur.heavy ? 0.08f : 0.045f); Feel.I.Shake(gCur.heavy ? 0.3f : 0.12f); Feel.I.SfxRandom("hit", 3, 0.8f); }
                OnBasicLanded(gCur.heavy, false);
            }
            AddOverclock("basic");
        }

        // ---------------- 武器技能（V） ----------------
        WSkill wsk; bool wFired; int wStep; Vector3 wFrom, wTo; float wDashT = -1f; Damageable wTarget;

        bool TryWeaponSkill()
        {
            if (kit.Active == null || !WeaponSkillReady || !Buffered(ref bufWeapon)) return false;
            wsk = kit.Active.SkillDef;
            kit.Active.skillReadyAt = Time.time + wsk.cd * Coop.CdMultAt(transform.position);
            Vector3 aim = AimDir();
            wTarget = Combat.NearestEnemy(transform.position, aim, 12f, 90f);
            Face(wTarget ? Flat(wTarget.transform.position - transform.position).normalized : aim, 0f);
            wFired = false; wStep = 0; wDashT = -1f;
            Enter(State.WSkill);
            anim.Play(wsk.anim, 0.05f, wsk.speed);
            invulnUntil = Mathf.Max(invulnUntil, Time.time + 0.15f);
            AddOverclock("skill");
            if (FeelOn) Feel.I.Sfx("ult_charge", 0.4f, 0.05f, 1.7f);
            return true;
        }

        int WHit(AttackShape s, float mult, float stagger, float knock, bool heavy, System.Action<Damageable> extra = null)
        {
            int n = 0;
            foreach (var e in Combat.enemies.ToArray())
            {
                if (!e || e.dead || !s.Contains(e.transform.position, e.radius)) continue;
                Vector3 d = Flat(e.transform.position - s.origin); if (d.sqrMagnitude < 0.01f) d = s.fwd;
                if (DealHit(e, mult, stagger, knock, d.normalized, heavy, "wskill") > 0f) { n++; extra?.Invoke(e); }
            }
            if (n > 0 && FeelOn) { Feel.I.HitStop(heavy ? 0.09f : 0.05f); Feel.I.Shake(heavy ? 0.35f : 0.18f); Feel.I.Sfx(heavy ? "hit_heavy" : "hit_1", 0.8f); }
            return n;
        }
        void Dash(Vector3 to, float dur) { wFrom = transform.position; wTo = Combat.ClampToArena(to, 0.8f); wDashT = dur; }
        static void Mob(Damageable e, System.Action<IMob> a) { var m = e ? e.GetComponent<IMob>() : null; if (m != null) a(m); }

        void TickWSkill()
        {
            FX.Comic = FX.ComicStyle;
            try { TickWSkillCore(); } finally { FX.Comic = FX.ComicStyle; }
        }

        void TickWSkillCore()
        {
            float t = stateT;
            Vector3 c = Flat(transform.position), fwd = Flat(transform.forward).normalized;
            Color col = Weapons.StarCol[kit.Active != null ? kit.Active.star : 3];
            if (wDashT > 0f)
            {
                float k = Mathf.Clamp01(t / wDashT);
                Vector3 p = Vector3.Lerp(wFrom, wTo, 1f - (1f - k) * (1f - k));
                Vector3 dl = p - transform.position; dl.y = 0f; Gravity(dl);
            }
            else Gravity(Vector3.zero);
            // 每个技能：wStep 计已经结算的段数
            bool At(float time) { if (wStep >= 0 && t >= time && !wFired) { wFired = true; return true; } return false; }
            bool Step(int i, float time) { if (wStep == i && t >= time) { wStep++; return true; } return false; }
            switch (wsk.id)
            {
                case "slash_wave":
                    if (At(0.2f)) { WHit(AttackShape.Line(c, fwd, 12f, 2f), 2.5f, 25f, 2f, true); for (int i = 1; i <= 10; i++) FX.Sparks(transform.position + Vector3.up * 1.1f + fwd * 1.2f * i, fwd, 3, col); FX.SlashArc(transform.position + Vector3.up * 1.1f, Quaternion.LookRotation(fwd), 1f, 3f, 120f, 0.15f, col); }
                    break;
                case "flame_slash":
                    if (At(0.3f)) { WHit(AttackShape.Cone(c, fwd, 4.5f, 140f), 3f, 40f, 3f, true, e => WeaponAffix.Ignite(this, e, 0.6f)); FX.Embers(c + fwd * 2f + Vector3.up, 30); FX.SlashArc(transform.position + Vector3.up * 1.2f, Quaternion.LookRotation(Vector3.down, fwd), 2f, 4.5f, 90f, 0.18f, new Color(1f, 0.5f, 0.15f, 0.9f)); }
                    break;
                case "leap_slam":
                    if (Step(0, 0.02f)) { Vector3 to = wTarget && Vector3.Distance(Flat(wTarget.transform.position), c) < 8.5f ? Flat(wTarget.transform.position) - fwd * (wTarget.radius + 0.6f) : c + fwd * 3f; Dash(to, 0.3f); }
                    if (At(0.36f)) { WHit(AttackShape.Circle(c, 4.5f), 3.2f, 80f, 5f, true); FX.Ring(c, 0.4f, 4.8f, 0.4f, col, 0.25f, true); FX.Dust(c, 14, new Color(0.4f, 0.38f, 0.36f, 0.8f)); if (FeelOn) Feel.I.Sfx("boss_slam", 0.7f, 0.05f, 1.2f); }
                    break;
                case "whirl":
                    if (Step(0, 0.25f) || Step(1, 0.55f)) { WHit(AttackShape.Circle(c, 4f), 1.6f, 25f, 4f, wStep == 2); FX.SlashArc(transform.position + Vector3.up * 1.1f, Quaternion.LookRotation(fwd), 2f, 4f, 340f, 0.15f, col); }
                    break;
                case "pierce_dash":
                case "duanyan":
                    if (Step(0, 0.02f)) { wFrom = transform.position; Dash(transform.position + fwd * 7f, 0.18f); }
                    if (At(0.2f)) { float m = wsk.id == "duanyan" ? 2.2f : 2.8f; WHit(AttackShape.Line(Flat(wFrom), fwd, 7.5f, wsk.id == "duanyan" ? 3.2f : 1.8f), m, 28f, 3.5f, true); if (wsk.id == "duanyan") FX.Embers(transform.position + Vector3.up, 20); else for (int i = 1; i <= 6; i++) FX.Sparks(Flat(wFrom) + fwd * i + Vector3.up, fwd, 2, col); }
                    break;
                case "sweep":
                    if (At(0.28f)) { WHit(AttackShape.Cone(c, fwd, 5f, 200f), 2.2f, 30f, 4f, true); FX.SlashArc(transform.position + Vector3.up * 1.1f, Quaternion.LookRotation(fwd), 2.5f, 5f, 200f, 0.16f, col); }
                    break;
                case "shadow_step":
                    if (Step(0, 0.02f))
                    {
                        var tg = Combat.NearestEnemy(transform.position, fwd, 10f, 360f);
                        if (tg) { Vector3 behind = Flat(tg.transform.position) + Flat(tg.transform.position - transform.position).normalized * (tg.radius + 1f); FX.Tide(transform.position + Vector3.up, Vector3.up, 16); Dash(behind, 0.12f); }
                    }
                    for (int i = 0; i < 4; i++) if (Step(i + 1, 0.2f + i * 0.16f)) { WHit(AttackShape.Circle(c, 2.8f), 0.9f, 12f, 1f, i == 3); FX.SlashArc(transform.position + Vector3.up * 1.2f, Quaternion.LookRotation(fwd) * Quaternion.Euler(0, 0, i * 50f), 1.4f, 2.8f, 200f, 0.1f, new Color(0.85f, 0.55f, 1f)); if (i % 2 == 1) anim.Play("Dualwield_Melee_Attack_Chop", 0.03f, 2f); }
                    break;
                case "flurry":
                    for (int i = 0; i < 6; i++) if (Step(i, 0.12f + i * 0.11f)) { WHit(AttackShape.Circle(c, 3f), 0.55f, 8f, 0.5f, i == 5); FX.SlashArc(transform.position + Vector3.up * 1.1f, Quaternion.LookRotation(Quaternion.Euler(0, i * 60f, 0) * fwd), 1.2f, 3f, 120f, 0.08f, col); if (i % 2 == 1) anim.Play(i % 4 == 1 ? "Dualwield_Melee_Attack_Slice" : "Dualwield_Melee_Attack_Chop", 0.03f, 2.2f); }
                    break;
                case "tide_orbs":
                    if (At(0.22f))
                    {
                        gTmp.Clear(); foreach (var e in Combat.enemies) if (e && !e.dead && !e.dormant && Vector3.Distance(Flat(e.transform.position), c) < 14f) gTmp.Add(e);
                        gTmp.Sort((a, b) => Vector3.Distance(a.transform.position, transform.position).CompareTo(Vector3.Distance(b.transform.position, transform.position)));
                        for (int i = 0; i < 3; i++) { Vector3 dir = i < gTmp.Count ? Flat(gTmp[i].transform.position - transform.position).normalized : Quaternion.Euler(0, (i - 1) * 20f, 0) * fwd; Projectile.Fire(this, transform.position + Vector3.up * 1.3f + fwd * 0.6f, dir, 20f, 14f, 0.5f, 1.3f, 15f, new Color(0.4f, 0.95f, 1f), 0.7f, true, "wskill"); }
                    }
                    break;
                case "thunder_call":
                    {
                        Vector3 aimC = wTarget ? Flat(wTarget.transform.position) : c + fwd * 6f;
                        for (int i = 0; i < 3; i++) if (Step(i, 0.25f + i * 0.15f)) { Vector3 q = aimC + (i == 0 ? Vector3.zero : Quaternion.Euler(0, i * 137f, 0) * Vector3.forward * 1.8f); WHit(AttackShape.Circle(q, 2f), 1.8f, 30f, 1f, true); Rogue.Bolt(q + Vector3.up * 9f, q, new Color(0.8f, 0.65f, 1f)); FX.Strike(q, new Color(0.8f, 0.65f, 1f, 1f)); FX.Ring(q, 0.2f, 2.2f, 0.3f, new Color(0.8f, 0.65f, 1f, 1f), 0.2f); }
                    }
                    break;
                case "pierce_shot":
                    if (At(0.12f)) { WHit(AttackShape.Line(c, fwd, 16f, 1.2f), 2.8f, 30f, 2f, true); Vector3 a = transform.position + Vector3.up * 1.3f; for (int i = 1; i <= 10; i++) FX.Sparks(a + fwd * 1.6f * i, fwd, 2, new Color(1f, 0.9f, 0.6f)); FX.ImpactFlash(a + fwd * 0.6f, 1.6f, new Color(1f, 0.9f, 0.6f)); }
                    break;
                case "scatter":
                    if (At(0.12f)) { WHit(AttackShape.Cone(c, fwd, 8f, 60f), 2.6f, 25f, 4f, true); for (int i = -3; i <= 3; i++) { Vector3 d = Quaternion.Euler(0, i * 9f, 0) * fwd; for (int k = 1; k <= 4; k++) FX.Sparks(transform.position + Vector3.up * 1.3f + d * 2f * k, d, 1, new Color(1f, 0.85f, 0.55f)); } }
                    break;
                case "resonance":
                    if (At(0.25f)) { WHit(AttackShape.Circle(c, 6f), 1.4f, 20f, 2f, false); foreach (var o in Coop.Alive()) if (Vector3.Distance(Flat(o.transform.position), c) <= 10f) Heal(o, o.dmg.maxHp * 0.06f); FX.Ring(c, 0.4f, 6.4f, 0.5f, new Color(0.95f, 0.85f, 0.5f, 1f), 0.2f); }
                    break;
                case "rally":
                    if (At(0.25f)) { foreach (var o in Coop.Alive()) { o.AddMod("dmg", "w_rally", 0.15f, 6f); FX.Ring(o.transform.position, 0.3f, 1.6f, 0.4f, new Color(1f, 0.8f, 0.4f, 0.9f), 0.12f); } FX.Ring(c, 0.4f, 8f, 0.6f, new Color(1f, 0.8f, 0.4f, 0.9f), 0.15f); if (FeelOn) Combat.Toast("号令：全队伤害 +15%", new Color(1f, 0.8f, 0.4f)); }
                    break;
                case "javelin_throw":
                    if (At(0.25f)) { WHit(AttackShape.Line(c, fwd, 18f, 1.8f), 3.4f, 40f, 3f, true); Vector3 a = transform.position + Vector3.up * 1.6f; for (int i = 1; i <= 12; i++) FX.Sparks(a + fwd * 1.5f * i, fwd, 2, new Color(1f, 0.7f, 0.6f)); }
                    break;
                case "ice_lances":
                    if (At(0.25f)) for (int i = -1; i <= 1; i++) { Vector3 d = Quaternion.Euler(0, i * 15f, 0) * fwd; WHit(AttackShape.Line(c, d, 14f, 1.4f), 1.5f, 15f, 1f, false, e => Mob(e, m => m.Slow(Time.time + 3f))); for (int k = 1; k <= 8; k++) FX.Sparks(transform.position + Vector3.up * 1.4f + d * 1.7f * k, d, 2, new Color(0.75f, 0.95f, 1f)); }
                    break;
                case "triple_thrust":
                    for (int i = 0; i < 3; i++) if (Step(i, 0.18f + i * 0.2f)) { WHit(AttackShape.Line(c, fwd, 6f, 1.6f), 1.3f, 15f, 1.5f, i == 2); for (int k = 1; k <= 5; k++) FX.Sparks(transform.position + Vector3.up * 1.2f + fwd * 1.2f * k, fwd, 1, col); if (i > 0) anim.Play("2H_Melee_Attack_Stab", 0.03f, 2.2f); }
                    break;
                case "vortex":
                    if (At(0.22f)) Vortex.Spawn(this, ClampReach(AimPoint, 9f));
                    break;
                case "anchor":
                    if (At(0.25f)) SoundAnchor.Spawn(this, ClampReach(AimPoint, 8f));
                    break;
            }
            if (t >= wsk.lockT)
            {
                if (Buffered(ref bufDodge)) { StartDodge(); return; }
                if (!TryActions(true)) Enter(State.Move);
            }
        }
    }
}

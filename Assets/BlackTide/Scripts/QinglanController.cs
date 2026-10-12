using UnityEngine;

namespace BlackTide
{
    // 青岚：副C · 双枪 · 气动 —— 中距离连射；旋流聚怪削韧；风障控场（挡冲锋、罩倒地队友、贴墙架势翻倍）
    // 延奏比别人长：接力对象 12 秒内伤害 +12%、削韧 +20%
    public class QinglanController : HeroBase
    {
        public override HeroId Id => HeroId.Qinglan;
        public override string HeroName => "青岚";
        public override string Role => "副C";
        public static readonly Color Col = new Color(0.52f, 0.9f, 0.42f);   // 2026-10-09：青绿 → 草绿（气动），和黑潮敌人的青色拉开
        public override Color Color => Col;
        public override string SkillName => "旋流";
        public override string UltName => "风障";
        public override float SkillCd => 9f;
        public override float PreferDist => 5.5f;
        public override float Reach => 9f;
        public override bool Ranged => true;
        protected override string RunAnim => "Running_B";
        protected override void SetNativeWeaponVisible(bool v) { foreach (var hh in GetComponentsInChildren<HandHeld>(true)) if (hh.name == "Pistol") hh.gameObject.SetActive(v); }

        Transform handR, handL;
        int shot;
        bool fired, heavyShot, counterShot;
        float endAt;
        Vector3 castPos;
        bool cast;

        public void Setup(GameObject model, Transform right, Transform left)
        {
            SetupBase(model, 1520f);
            dmgScale = 0.9f;   // 实测输出占比 31%，目标 27%（SPEC 第 8 节：43 / 27 / 30）
            moveSpeed = 6.8f;
            handR = right; handL = left;
            anim.Play(IdleAnim, 0f);
        }

        Vector3 Muzzle(bool right)
        {
            Transform m = right ? handR : handL;
            Vector3 p = m ? m.position : transform.position + Vector3.up * 1.3f + transform.right * (right ? 0.35f : -0.35f);
            p.y = Mathf.Clamp(p.y, 1.0f, 1.6f);
            return p + transform.forward * 0.35f;
        }

        // ---------------- 普攻：两短一长的连射 ----------------
        protected override void StartBasic(bool counter)
        {
            Vector3 aim = AimDir();
            var t = AttackTarget(Reach + 1f, 35f);
            Face(t ? Flat(t.transform.position - transform.position) : aim, 0f);
            counterShot = counter;
            heavyShot = !counter && shot == 2;
            Enter(State.Attack);
            fired = false;
            endAt = counter ? 0.36f : heavyShot ? 0.42f : 0.28f;
            anim.Play("1H_Ranged_Shoot", 0.05f, heavyShot || counter ? 1.6f : 2.4f, 0.2f);
        }

        protected override void TickAttack()
        {
            Gravity(moveIn * moveSpeed * 0.35f * Time.deltaTime);  // 射击时可以慢慢挪
            if (!fired) Face(TrackedAttackDirection(transform.forward), 0f);
            if (!fired && stateT >= 0.07f) { fired = true; Fire(); }
            if (Buffered(ref bufDodge)) { StartDodge(); return; }
            if (fired && TryActions(false)) return;
            if (stateT >= endAt)
            {
                if (attackHeld || Buffered(ref bufAttack)) { bool c = Time.time < counterUntil; if (c) counterUntil = 0f; StartBasic(c); }
                else Enter(State.Move);
            }
        }

        void Fire()
        {
            bool right = shot % 2 == 0;
            Vector3 from = Muzzle(right);
            Vector3 dir = transform.forward;
            if (counterShot)
            {
                // 反击：双枪齐射一发贯穿性的重弹
                Projectile.Fire(this, from, dir, 34f, 12f, 0.7f, 2.6f, 60f, Col, 1.1f, true, "counter");
                FX.ImpactFlash(from, 1.6f, Color.Lerp(Col, Color.white, 0.5f));
                if (FeelOn) Feel.I.Sfx("ult_charge", 0.5f, 0.05f, 1.8f);
            }
            else
            {
                Projectile.Fire(this, from, dir, 30f, 11f, 0.3f, heavyShot ? 0.9f : 0.52f, heavyShot ? 12f : 5f, Col, heavyShot ? 0.7f : 0.45f, heavyShot, "shot");
                FX.ImpactFlash(from, heavyShot ? 1.1f : 0.7f, Color.Lerp(Col, Color.white, 0.5f));
                shot = (shot + 1) % 3;
            }
            Feel.I.Sfx("shot", FeelOn ? 0.6f : 0.2f, 0.06f, 1f);
            AddOverclock("basic");
        }

        protected override void OnBasicLanded(bool heavy, bool counter) => Gain(counter ? 10f : 1.8f, counter ? 12f : 0.85f);

        // ---------------- 旋流（E） ----------------
        protected override void StartSkill()
        {
            castPos = ClampReach(AimPoint, 9f);
            Face(castPos - transform.position, 0f);
            Enter(State.Skill);
            cast = false;
            anim.Play("Throw", 0.05f, 1.9f, 0.15f);
        }

        protected override void TickSkill()
        {
            Gravity(Vector3.zero);
            if (!cast && stateT >= 0.22f)
            {
                cast = true;
                Vortex.Spawn(this, castPos);
                Gain(16f, 10f);
            }
            if (cast && Buffered(ref bufDodge)) { StartDodge(); return; }
            if (stateT >= 0.45f) Enter(State.Move);
        }

        // ---------------- 风障（Q）：在瞄准点立一道与视线垂直的风墙 ----------------
        protected override void StartUlt()
        {
            castPos = ClampReach(AimPoint, 8f);
            Face(castPos - transform.position, 0f);
            Enter(State.Ult);
            cast = false;
            anim.Play("Spellcast_Raise", 0.05f, 2.2f);
            Feel.I.Sfx("ult_charge", FeelOn ? 0.8f : 0.35f, 0.02f, 1.3f);
        }

        protected override void TickUlt()
        {
            Gravity(Vector3.zero);
            if (!cast && stateT >= 0.3f)
            {
                cast = true;
                Vector3 look = Flat(castPos - transform.position);
                if (look.sqrMagnitude < 0.01f) look = transform.forward;
                var w = WindWall.Spawn(this, castPos, Vector3.Cross(Vector3.up, look.normalized));
                foreach (var e in Combat.enemies.ToArray())
                    if (e && !e.dead && w.Dist(e.transform.position) <= WindWall.SlowRadius + e.radius)
                        DealHit(e, 1.6f, 30f, 2f, look.normalized, true, "ult");
                Gain(0f, 10f);
                Feel.I.Sfx("boss_swing", FeelOn ? 0.8f : 0.35f, 0.05f, 1.4f);
                if (FeelOn) Feel.I.Shake(0.3f);
            }
            if (stateT >= 0.6f) Enter(State.Move);
        }

        // ---------------- 接力 ----------------
        // 变奏：身边一圈气爆；延奏：12 秒伤害 +12%、削韧 +20%
        protected override void DoIntro()
        {
            anim.Play("Spellcast_Shoot", 0.05f, 1.8f);
            Vector3 c = transform.position;
            foreach (var e in Combat.enemies.ToArray())
                if (e && !e.dead && Vector3.Distance(Flat(e.transform.position), Flat(c)) <= 3.8f + e.radius)
                    DealHit(e, 2.6f, 36f, 4f, Flat(e.transform.position - c).normalized, true, "intro");
            FX.Ring(c, 0.4f, 4f, 0.35f, new Color(Col.r, Col.g, Col.b, 1f), 0.16f);
            FX.Dust(c, 12, new Color(0.7f, 0.85f, 0.8f, 0.6f));
            Feel.I.Sfx("dash", FeelOn ? 0.9f : 0.4f, 0.05f, 0.8f);
        }

        protected override void ApplyOutro(HeroBase to)
        {
            to.AddMod("dmg", "outro_qinglan", 0.12f, 12f);
            to.AddMod("stagger", "outro_qinglan_s", 0.2f, 12f);
        }
    }
}

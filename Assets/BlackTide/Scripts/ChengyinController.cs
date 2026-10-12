using UnityEngine;

namespace BlackTide
{
    // 澄音：辅助 · 音感仪 · 衍射 —— 时机与风险管理
    // 锚音：回血减伤、定身杂兵；静默：全队护盾 + 打断 Boss 当前招式（包括咏唱）、3 秒禁招
    // 被动：治疗量积累静电，满 10 层落雷（原稿：奶雷，让辅助不在战斗里隐身）；救人更快，她救起的队友残像降一级
    // 延奏「净频」：接力对象超频 −25、回复 10% 生命
    public class ChengyinController : HeroBase
    {
        public override HeroId Id => HeroId.Chengyin;
        public override string HeroName => "澄音";
        public override string Role => "辅助";
        public static readonly Color Col = new Color(0.91f, 0.76f, 0.35f);
        public override Color Color => Col;
        public override string SkillName => "锚音";
        public override string UltName => "静默";
        public override float SkillCd => 14f;
        public override string PipName => "静电";
        public override int Pips => staticStacks;
        public override int PipMax => Rogue.StaticMax(this);   // 雷音共振：10 → 6
        public override float PreferDist => 6.5f;
        public override float Reach => 9.5f;
        public override bool Ranged => true;
        public override float RescueHold => 1.0f;
        protected override string RunAnim => "Running_B";

        Transform hand;
        int shot, staticStacks;
        float staticHeal, healAccum;
        bool fired, heavyShot, counterShot, cast;
        float endAt;
        Vector3 castPos;
        Transform[] orbs;
        Transform instr;      // 漂浮的音感仪（Tripo 模型）
        float pulse, spin;
        protected override void SetNativeWeaponVisible(bool v) { if (instr) instr.gameObject.SetActive(v); if (orbs != null) foreach (var o in orbs) if (o) o.gameObject.SetActive(v); }

        public void Setup(GameObject model, Transform castHand, GameObject instrument = null, Material instrumentMat = null)
        {
            SetupBase(model, 1450f);
            dmgScale = 1.1f;   // 实测输出占比 26–28%，目标 30%（SPEC 第 8 节：43 / 27 / 30）
            moveSpeed = 6.6f;
            hand = castHand;
            if (instrument) { instr = BuildInstrument(instrument, instrumentMat); }
            else
            {
                // 还没有音感仪模型时：三颗绕身旋转的光晶占位
                orbs = new Transform[3];
                for (int i = 0; i < 3; i++)
                {
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Destroy(q.GetComponent<Collider>());
                    q.name = "Orb";
                    q.transform.SetParent(transform, false);
                    q.transform.localScale = Vector3.one * 0.45f;
                    var r = q.GetComponent<Renderer>();
                    r.sharedMaterial = FX.NewMat(SliceAssets.I.softDot, true, Col, 3f);
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    orbs[i] = q.transform;
                }
            }
            anim.Play(IdleAnim, 0f);
        }

        // 音感仪：圆环朝前、手柄朝下，浮在身体右前方；长轴（圆环 + 手柄）约 0.8 米
        Transform BuildInstrument(GameObject prefab, Material mat)
        {
            var holder = new GameObject("音感仪").transform;
            holder.SetParent(transform, false);
            var m = Instantiate(prefab, holder).transform;
            m.localPosition = Vector3.zero; m.localRotation = Quaternion.identity; m.localScale = Vector3.one;
            var verts = new System.Collections.Generic.List<Vector3>();
            foreach (var mf in m.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue;
                var M = holder.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (var p in mf.sharedMesh.vertices) verts.Add(M.MultiplyPoint3x4(p));
            }
            var s = WeaponFit.Analyze(verts.ToArray(), false);
            // 圆环那一端在次轴上更宽：让 axis 指向手柄（窄的一端）
            float spanLo = 0f, spanHi = 0f;
            foreach (var p in verts)
            {
                float t = Vector3.Dot(p - s.center, s.axis);
                float w = Mathf.Abs(Vector3.Dot(p - s.center, s.wide));
                if (t < s.min + s.Len * 0.35f) spanLo = Mathf.Max(spanLo, w);
                if (t > s.max - s.Len * 0.35f) spanHi = Mathf.Max(spanHi, w);
            }
            Vector3 handle = spanHi > spanLo ? -s.axis : s.axis;
            Vector3 normal = Vector3.Cross(s.axis, s.wide).normalized;
            float scale = 0.8f / Mathf.Max(1e-4f, s.Len);
            Quaternion R = Quaternion.Inverse(Quaternion.LookRotation(normal, -handle));   // 圆环面朝 +Z，手柄朝 -Y
            m.localRotation = R;
            m.localScale = Vector3.one * scale;
            m.localPosition = -(R * s.center) * scale;
            foreach (var r in m.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            return holder;
        }

        protected override void LateTick()
        {
            if (instr)
            {
                // 漂在右前方，轻轻上下浮动、左右摇摆（手柄始终朝下）；出手时放大一下；静电层数越多摆得越急
                pulse = Mathf.Max(0f, pulse - Time.deltaTime * 4f);
                spin += Time.deltaTime * (1.6f + staticStacks * 0.35f);
                float bob = Mathf.Sin(Time.time * 2.2f) * 0.07f;
                instr.position = transform.position + transform.right * 0.6f + transform.forward * 0.35f + Vector3.up * (1.45f + bob);
                instr.rotation = Quaternion.LookRotation(transform.forward, Vector3.up) * Quaternion.Euler(-20f, Mathf.Sin(spin) * 18f, Mathf.Sin(spin * 0.7f) * 10f);
                instr.localScale = Vector3.one * (1f + pulse * pulse * 0.3f);
                instr.gameObject.SetActive(!Downed);
                return;
            }
            if (orbs == null) return;
            float t = Time.time * 2.2f;
            for (int i = 0; i < orbs.Length; i++)
            {
                float a = t + i * Mathf.PI * 2f / 3f;
                orbs[i].localPosition = new Vector3(Mathf.Cos(a) * 0.75f, 1.55f + Mathf.Sin(t * 1.7f + i) * 0.12f, Mathf.Sin(a) * 0.75f);
                orbs[i].localScale = Vector3.one * (0.4f + staticStacks * 0.03f);
                if (cam) orbs[i].rotation = cam.transform.rotation;
                orbs[i].gameObject.SetActive(!Downed);
            }
        }

        Vector3 Muzzle()
        {
            pulse = 1f;
            if (instr) return instr.position + transform.forward * 0.2f;
            Vector3 p = hand ? hand.position : transform.position + Vector3.up * 1.4f;
            p.y = Mathf.Clamp(p.y, 1.1f, 1.7f);
            return p + transform.forward * 0.4f;
        }

        // ---------------- 普攻：音波 ----------------
        protected override void StartBasic(bool counter)
        {
            Vector3 aim = AimDir();
            var t = AttackTarget(Reach + 1f, 35f);
            Face(t ? Flat(t.transform.position - transform.position) : aim, 0f);
            counterShot = counter;
            heavyShot = !counter && shot == 2;
            Enter(State.Attack);
            fired = false;
            endAt = counter ? 0.4f : heavyShot ? 0.5f : 0.42f;
            anim.Play("Spellcast_Shoot", 0.05f, 1.7f, 0.15f);
        }

        protected override void TickAttack()
        {
            Gravity(moveIn * moveSpeed * 0.3f * Time.deltaTime);
            if (!fired) Face(TrackedAttackDirection(transform.forward), 0f);
            if (!fired && stateT >= 0.12f)
            {
                fired = true;
                if (counterShot) Projectile.Fire(this, Muzzle(), transform.forward, 24f, 12f, 0.8f, 2.4f, 50f, Col, 1.2f, true, "counter");
                else
                {
                    Projectile.Fire(this, Muzzle(), transform.forward, 22f, 11f, 0.45f, heavyShot ? 0.85f : 0.55f, heavyShot ? 8f : 4f, Col, heavyShot ? 0.9f : 0.6f, heavyShot, "wave");
                    shot = (shot + 1) % 3;
                }
                FX.Ring(Muzzle(), 0.1f, 0.7f, 0.2f, new Color(Col.r, Col.g, Col.b, 0.8f), 0.05f);
                Feel.I.Sfx("resonance_shot", FeelOn ? 0.55f : 0.18f, 0.06f, 1f);
                AddOverclock("basic");
            }
            if (Buffered(ref bufDodge)) { StartDodge(); return; }
            if (fired && TryActions(false)) return;
            if (stateT >= endAt)
            {
                if (attackHeld || Buffered(ref bufAttack)) { bool c = Time.time < counterUntil; if (c) counterUntil = 0f; StartBasic(c); }
                else Enter(State.Move);
            }
        }

        protected override void OnBasicLanded(bool heavy, bool counter) => Gain(counter ? 10f : 3.5f, counter ? 12f : 1f);

        // ---------------- 锚音（E） ----------------
        protected override void StartSkill()
        {
            castPos = ClampReach(AimPoint, 8f);
            Face(castPos - transform.position, 0f);
            Enter(State.Skill);
            cast = false;
            anim.Play("Spellcast_Raise", 0.05f, 2.4f);
        }

        protected override void TickSkill()
        {
            Gravity(Vector3.zero);
            if (!cast && stateT >= 0.25f)
            {
                cast = true;
                SoundAnchor.Spawn(this, castPos);
                Gain(20f, 12f);
            }
            if (cast && Buffered(ref bufDodge)) { StartDodge(); return; }
            if (stateT >= 0.5f) Enter(State.Move);
        }

        // ---------------- 静默（Q） ----------------
        protected override void StartUlt()
        {
            Enter(State.Ult);
            cast = false;
            anim.Play("Spellcast_Long", 0.05f, 2.6f);
            Feel.I.Sfx("ult_charge", FeelOn ? 0.9f : 0.45f, 0.02f, 1.2f);
        }

        protected override void TickUlt()
        {
            Gravity(Vector3.zero);
            if (!cast && stateT >= 0.35f)
            {
                cast = true;
                foreach (var h in Coop.Alive()) Heal(h, h.dmg.maxHp * 0.25f, true, 8f);
                var b = Combat.NearestBoss(transform.position, 14f);
                if (b && !b.dmg.dead && Vector3.Distance(Flat(b.transform.position), Flat(transform.position)) <= 14f + b.dmg.radius)
                {
                    b.Silence(3f, "静默");
                    DealHit(b.dmg, 2f, 60f, 0f, Flat(b.transform.position - transform.position).normalized, true, "ult");
                    FX.Ring(b.transform.position, 0.5f, 5f, 0.5f, new Color(Col.r, Col.g, Col.b, 1f), 0.2f);
                }
                Gain(0f, 8f);
                FX.Ring(transform.position, 0.4f, 14f, 0.6f, new Color(Col.r, Col.g, Col.b, 0.9f), 0.12f);
                FX.Sparks(transform.position + Vector3.up * 1.5f, Vector3.up, 40, Color.Lerp(Col, Color.white, 0.4f));
                Feel.I.Sfx("perfect_dodge", FeelOn ? 1f : 0.5f, 0f, 0.6f);
                if (FeelOn) { Feel.I.PulseHit(0.6f); Feel.I.Shake(0.3f); }
            }
            if (stateT >= 0.7f) Enter(State.Move);
        }

        // ---------------- 静电：治疗量每达到目标 4% 生命叠 1 层，满 10 层对最近的敌人落雷 ----------------
        protected override void OnHealed(HeroBase target, float amount, bool shield)
        {
            if (!shield) energy = Mathf.Min(energyMax, energy + 0.6f);
            staticHeal += amount;
            healAccum += amount;
            float per = target.dmg.maxHp * 0.04f;
            while (staticHeal >= per && staticStacks < Rogue.StaticMax(this)) { staticHeal -= per; staticStacks++; }
            if (staticStacks >= Rogue.StaticMax(this)) Discharge();
        }

        void Discharge()
        {
            Damageable target = null; float bd = 12f;
            foreach (var e in Combat.enemies)
            {
                if (!e || e.dead) continue;
                float d = Vector3.Distance(Flat(e.transform.position), Flat(transform.position));
                if (d < bd) { bd = d; target = e; }
            }
            if (!target) return;   // 没目标就保留满层
            Vector3 c = Flat(target.transform.position);
            float mult = 5.2f + healAccum * 0.3f / baseAtk;
            foreach (var e in Combat.enemies.ToArray())
                if (e && !e.dead && Vector3.Distance(Flat(e.transform.position), c) <= 3.5f + e.radius)
                    DealHit(e, mult, 45f, 1f, Vector3.down, true, "static", 0f, false);
            for (int i = 0; i < 6; i++) FX.Sparks(c + Vector3.up * (1f + i * 1.2f), Vector3.down, 8, new Color(1f, 0.95f, 0.6f));
            FX.ImpactFlash(c + Vector3.up * 1.5f, 4f, new Color(1f, 0.9f, 0.5f));
            FX.Ring(c, 0.3f, 3.8f, 0.35f, new Color(1f, 0.9f, 0.5f, 1f), 0.14f);
            Feel.I.Sfx("hit_heavy", FeelOn ? 0.8f : 0.4f, 0.05f, 1.4f);
            int lv = rogue.Lv("chengyin_thunder");   // 雷音共振：落雷后再跳到附近的敌人
            if (lv > 0)
            {
                Rogue.Chain(this, c + Vector3.up * 1.2f, target, lv + 1, mult * 0.45f);
                if (lv >= 3) foreach (var h in Coop.Alive()) h.AddMod("dmg", "static_res", 0.15f, 3f);
            }
            staticStacks = 0; healAccum = 0f;
            if (FeelOn) Combat.Toast("静电落雷", Col);
        }

        // ---------------- 接力 ----------------
        // 变奏：全队回 8%，身边一圈音爆；延奏「净频」：超频 −25、回复 10%
        protected override void DoIntro()
        {
            anim.Play("Spellcast_Raise", 0.05f, 2.4f);
            foreach (var h in Coop.Alive()) Heal(h, h.dmg.maxHp * 0.08f);
            Vector3 c = transform.position;
            foreach (var e in Combat.enemies.ToArray())
                if (e && !e.dead && Vector3.Distance(Flat(e.transform.position), Flat(c)) <= 3.2f + e.radius)
                    DealHit(e, 1.2f, 18f, 2f, Flat(e.transform.position - c).normalized, false, "intro");
            FX.Ring(c, 0.4f, 3.4f, 0.4f, new Color(Col.r, Col.g, Col.b, 1f), 0.14f);
        }

        protected override void ApplyOutro(HeroBase to)
        {
            to.overclock = Mathf.Max(0f, to.overclock - 25f);
            Heal(to, to.dmg.maxHp * 0.1f);
        }
    }
}

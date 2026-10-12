using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    public enum HeroId { Feilie, Qinglan, Chengyin }

    // 三个角色共用：移动、闪避与完美闪避、受击、护盾、倒地与救援、协奏接力、超频、加成、伤害结算。
    // 输入来自玩家（键鼠）或 AI（AllyBrain），两边写同一组缓冲，后面的逻辑完全一样。
    public abstract partial class HeroBase : MonoBehaviour
    {
        public enum State { Move, Attack, Skill, Ult, Relay, Dodge, Hurt, Downed, Rescue, Revive, Echo, WSkill }
        public State state;

        public abstract HeroId Id { get; }
        public abstract string HeroName { get; }
        public abstract string Role { get; }
        public abstract Color Color { get; }
        public abstract string SkillName { get; }
        public abstract string UltName { get; }
        public abstract float SkillCd { get; }
        public virtual string PipName => "";
        public virtual int Pips => 0;
        public virtual int PipMax => 0;
        public virtual float PreferDist => 1.6f;   // AI 站位：到目标边缘的距离
        public virtual float Reach => 2.6f;        // AI 判断够得着的普攻距离
        public virtual bool Ranged => false;
        public virtual float RescueHold => 1.5f;
        protected virtual string IdleAnim => "Idle";
        protected virtual string RunAnim => "Running_A";

        public float moveSpeed = 6.4f;
        public float baseAtk = 100f;
        public float dmgScale = 1f;                // 职业输出校准系数
        public CameraRig rig;
        protected CharacterController cc;
        public CharacterAnim anim;
        public Damageable dmg;
        protected Camera cam;

        // ---- 资源 ----
        public float energy, energyMax = 100f;       // 共鸣能量：满了放解放（Q）
        public float concerto, concertoMax = 100f;   // 协奏能量：满了可以接力
        public float overclock;                      // 超频值：侵蚀 ≥ 45% 才累积
        int ocAlert;
        public float skillReadyAt;
        public float echoReadyAt;
        public float shieldUntil;

        // ---- 成长（M2）：等级、经验、解锁。数值见 Growth ----
        public int level = Growth.BaseLevel;
        public float exp;
        public int EffLevel => Mathf.Max(1, level - (penalized ? 1 : 0));   // 超时复活掉 1 级
        // ---- 经济（M3）：贝币、武器品质、快捷栏。数值见 Economy ----
        public readonly Kit kit = new Kit();
        float itemAiT;
        // ---- 肉鸽（M4）：个人构筑。数值见 Rogue ----
        public readonly RogueState rogue = new RogueState();
        float thunderUntil, nextThunder, nextSwords, lastHurtT = -99f;
        public float MaxHpNow => baseMaxHp * Growth.HpMult(EffLevel) * (1f + Rogue.HpBonus(this));
        public void RefreshMaxHp()
        {
            float ratio = dmg.maxHp > 0f ? dmg.hp / dmg.maxHp : 1f;
            dmg.maxHp = MaxHpNow;
            if (!dmg.dead) dmg.hp = dmg.maxHp * ratio;
        }
        public bool EchoUnlocked => level >= Growth.EchoAt;
        public bool UltUnlocked => level >= Growth.UltAt;
        public bool TeamUnlocked => level >= Growth.TeamAt;
        public bool UltReady => UltUnlocked && energy >= energyMax;
        public bool EchoReady => EchoUnlocked && Time.time >= echoReadyAt;
        public const float EchoCd = 15f, EchoRadius = 3.6f;
        // 信使的强化声骸（M4，原稿：击败信使必定获得顶级强化声骸，全队一人一个）：范围 5.5 米、冷却 11 秒、伤害和架势更高，
        // 命中正在咏唱的 Boss 直接打满架势（打断咏唱）——M5 黑潮之心「信使声骸能打断大部分招式」靠它
        public bool echoEmpowered;
        public EchoDef echo;                          // 装备的声骸（Echoes）；null = 原来的通用爆发
        public float echoParryUntil;                  // 潮刃剑士声骸：这段时间内下一次挨打被挡下
        public System.Action<HitInfo> onEchoParry;
        public void SetInvulnerable(float dur) { invulnUntil = Mathf.Max(invulnUntil, Time.time + dur); }
        bool EchoParry(HitInfo h)
        {
            if (Time.time >= echoParryUntil || h.kind == "chant") return false;
            echoParryUntil = 0f;
            onEchoParry?.Invoke(h);
            return true;
        }
        public float EchoCdNow => echoEmpowered ? 11f : echo != null ? echo.cd : EchoCd;
        public float EchoR => echoEmpowered ? 5.5f : EchoRadius;
        public int chasers;                          // 正在追这个角色的杂兵数（动态仇恨用）

        // ---- 统计 ----
        public float dmgDone, healDone;
        public int perfectDodges, relaysDone, rescues;

        // ---- 加成：同标签只保留最新一个（延奏、音锚减伤等） ----
        class Mod { public string stat, tag; public float value, until; }
        readonly List<Mod> mods = new List<Mod>();
        public void AddMod(string stat, string tag, float value, float dur)
        {
            mods.RemoveAll(m => m.tag == tag);
            mods.Add(new Mod { stat = stat, tag = tag, value = value, until = Time.time + dur });
        }
        public float ModSum(string stat)
        {
            float s = 0f;
            foreach (var m in mods) if (m.stat == stat && Time.time < m.until) s += m.value;
            return s;
        }

        // ---- 输入缓冲：玩家和 AI 都写这里 ----
        public static bool Autopilot;                // F1：玩家操控的角色也交给 AI
        public bool IsControlled => Combat.player == this;
        public bool UsesBrain => !IsControlled || Autopilot;
        public AllyBrain brain;
        [HideInInspector] public Vector3 AimPoint, moveIn;
        // DS-015：自动索敌（近处残血优先）与调试无敌位
        public static bool SoftLock = true;
        public static bool God;
        [HideInInspector] public bool attackHeld, interactHeld;
        [HideInInspector] public float bufAttack = -9f, bufDodge = -9f, bufSkill = -9f, bufUlt = -9f, bufEcho = -9f;
        [HideInInspector] public HeroBase relayTarget;
        const float BufferTime = 0.32f;

        // ---- 闪避 ----
        protected Vector3 dodgeDir;
        public Vector3 dodgeStartPos;
        public float dodgeStartT = -9f;
        protected float invulnUntil;
        public float counterUntil;
        protected float stateT, hurtUntil;

        // ---- 倒地与救援（原稿：15 秒倒计时；救起时刷出残像，连续倒地越多残像越强；超时复活会掉级，击败遗留残像恢复） ----
        public const float DownTime = 15f, RescueRange = 2.2f;
        public float downedUntil, rescueProgress;
        public HeroBase rescuer;
        HeroBase rescueTarget;
        public int downCount;
        public bool penalized;
        float baseMaxHp;                             // 6 级时的生命（各角色 Setup 传进来的值）

        public bool Downed => state == State.Downed;
        public bool Invulnerable => Time.time < invulnUntil;
        public bool InPerfectWindow => Time.time - dodgeStartT < 0.24f + Rogue.PerfectBonus(this);   // 疾步、镜步共鸣加宽
        protected bool FeelOn => IsControlled;   // 顿帧、震屏、大号数字只给玩家操控的角色，AI 打的不抖屏

        protected static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        protected void SetupBase(GameObject model, float hp)
        {
            cc = gameObject.AddComponent<CharacterController>();
            cc.radius = 0.55f; cc.height = 2.2f; cc.center = new Vector3(0f, 1.1f, 0f);
            cc.slopeLimit = 45f; cc.stepOffset = 0.3f; cc.skinWidth = 0.05f;
            // 队友之间不互相碰撞：三个人挤成一团时角色控制器会互相卡死（2026-10-09 跑局里队长被队友夹住原地跑了十几分钟）
            foreach (var o in Combat.heroes) if (o && o.cc) Physics.IgnoreCollision(cc, o.cc);
            anim = gameObject.AddComponent<CharacterAnim>();
            anim.Init(model.GetComponentInChildren<Animator>());
            anim.CalibrateLocomotion();
            if (anim.animator.isHuman)
            {
                var feet = anim.animator.gameObject.AddComponent<FootPlantIK>();
                feet.hero = this;
            }
            dmg = gameObject.AddComponent<Damageable>();
            baseMaxHp = hp;
            level = Growth.StartLevel;
            dmg.team = Team.Player; dmg.maxHp = dmg.hp = hp * Growth.HpMult(level); dmg.radius = 0.55f; dmg.height = 2.2f;
            dmg.canBeHit = h => !Downed && (h.kind == "chant" || (!Invulnerable && !God && !EchoParry(h)));
            kit.weapons[0] = Weapons.Make(Weapons.NativeDef(Id), 3); kit.active = 0; kit.Rebuild();   // DS-028：开局 3★（3★ 起带词条，等于开局就有一条词条）
            // DS-028 开局启动包：免费给「连锁打击」Lv1（普攻溅射 1 个敌人）——
            // 前期不用赌三选一就能"一刀一排"，把被围殴换成割草；三名队友都给，前期是三人在打。
            var startCard = Rogue.Def("cleave");
            if (startCard != null && rogue.Lv(startCard.id) <= 0) { rogue.owned.Add(startCard); rogue.levels[startCard.id] = 1; }
            dmg.onHit = OnHurt;
            dmg.onDeath = () => GoDown(false);
            cam = Camera.main;
            brain = new AllyBrain(this);
            Combat.heroes.Add(this);
        }

        protected virtual void Update()
        {
            if (UsesBrain) { walkHeld = false; CancelAttackApproach(); if (lockedTarget) SetLockedTarget(null); brain.Tick(); } else ReadPlayerInput();
            if (Growth.Enabled && UsesBrain) Economy.TickAI(this, ref itemAiT);
            stateT += Time.deltaTime;
            TickResources();
            BeginMotionFrame();
            switch (state)
            {
                case State.Move: TickMove(); break;
                case State.Attack: if (gAttacking) GenericTickBasic(); else TickAttack(); break;
                case State.WSkill: TickWSkill(); break;
                case State.Skill: TickSkill(); break;
                case State.Ult: TickUlt(); break;
                case State.Relay: Gravity(Vector3.zero); if (stateT > 0.5f) Enter(State.Move); break;
                case State.Dodge: TickDodge(); break;
                case State.Hurt: if (Time.time > hurtUntil) Enter(State.Move); else Gravity(Vector3.zero); break;
                case State.Downed: TickDowned(); break;
                case State.Rescue: TickRescue(); break;
                case State.Revive: Gravity(Vector3.zero); if (stateT > 0.85f) Enter(State.Move); break;
                case State.Echo: TickEcho(); break;
            }
            EndMotionFrame();
            if (IsControlled && rig) rig.aimPoint = AimPoint;
            if (Time.time < thunderUntil && Time.time >= nextThunder) Thunder();
            Rogue.TickHero(this, ref nextSwords);
            Arsenal.Tick(this);   // DS-019：弹幕军火库（环绕飞刃/追击弹幕/火焰新星）
            LateTick();
        }

        protected virtual void LateTick() { }

        // 雷电场（M4 前期增益）：解放后 21 秒，每 3 秒劈 10 米内最近的一个敌人
        void Thunder()
        {
            nextThunder = Time.time + Rogue.ThunderInterval(this);
            Damageable best = null; float bd = 10f;
            foreach (var e in Combat.enemies)
            {
                if (!e || e.dead || e.dormant) continue;
                float d = Vector3.Distance(Flat(e.transform.position), Flat(transform.position));
                if (d < bd) { bd = d; best = e; }
            }
            if (!best) return;
            DealHit(best, Rogue.ThunderCrit(this) ? 3.2f : 2f, 20f, 0f, Vector3.zero, false, "thunder");
            Vector3 p = best.transform.position;
            for (int i = 0; i < 6; i++) FX.Sparks(p + Vector3.up * (6f - i), Vector3.down, 3, new Color(0.75f, 0.7f, 1f));
            FX.Ring(p, 0.2f, 1.6f, 0.3f, new Color(0.75f, 0.7f, 1f, 1f), 0.12f);
            FX.Strike(p, new Color(0.75f, 0.7f, 1f, 1f));   // DS-010：落雷贴片（KB-003）
            int tc = Rogue.ThunderChain(this);   // DS-019b：Lv4 起落雷连锁
            if (tc > 0) Rogue.Chain(this, p, best, tc, 0.8f);
        }

        void ReadPlayerInput()
        {
            interactHeld = false;
            walkHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (Downed || GameInput.CombatBlocked)
            {
                moveIn = Vector3.zero; attackHeld = false;
                bufAttack = bufDodge = bufSkill = bufUlt = bufEcho = bufWeapon = -9f;
                relayTarget = null; CancelAttackApproach(); return;
            }
            float now = Time.time;
            Vector2 stick = GameInput.Move;
            var v = new Vector3(stick.x, 0f, stick.y);
            if (cam)
            {
                Vector3 f = cam.transform.forward; f.y = 0f; f.Normalize();
                Vector3 r = cam.transform.right; r.y = 0f; r.Normalize();
                v = f * v.z + r * v.x;
            }
            moveIn = v;
            RefreshCombatTarget();
            if (!SoftLock) AimPoint = Flat(transform.position) + (v.sqrMagnitude > 0.01f ? v.normalized : transform.forward) * 8f;
            if (Growth.Enabled)
            {
                for (int i = 0; i < Economy.Slots; i++) if (Input.GetKeyDown(KeyCode.Alpha3 + i)) Economy.Use(this, i, AimPoint);
                if (GameInput.Down(GameAction.Item)) Economy.Use(this, GameInput.ItemSlot, AimPoint);
                if (GameInput.Down(GameAction.TeamItem)) Economy.UseTeam(this);
                if (GameInput.Down(GameAction.Vessel) && RunDirector.I) RunDirector.I.UseVessel(this);
            }
            if (GameInput.Down(GameAction.Attack)) bufAttack = now;
            attackHeld = GameInput.AttackHeld;
            if (attackHeld && now - bufAttack > 0.25f && state == State.Move) bufAttack = now;
            if (GameInput.Down(GameAction.Dodge)) bufDodge = now;
            if (GameInput.Down(GameAction.Skill)) bufSkill = now;
            if (GameInput.Down(GameAction.Ult)) bufUlt = now;
            if (GameInput.Down(GameAction.Echo)) bufEcho = now;
            if (GameInput.Down(GameAction.WeaponSkill)) bufWeapon = now;
            if (state == State.Move || state == State.Attack)
            {
                if (GameInput.Down(GameAction.WeaponNext) || Input.mouseScrollDelta.y < -0.1f) SwitchWeapon(1);
                else if (Input.mouseScrollDelta.y > 0.1f) SwitchWeapon(-1);
            }
            if (GameInput.Down(GameAction.Relay1)) relayTarget = Coop.Teammate(this, 0);
            if (GameInput.Down(GameAction.Relay2)) relayTarget = Coop.Teammate(this, 1);
            interactHeld = GameInput.InteractHeld;
        }

        void TickResources()
        {
            if (overclock > 0f)
            {
                overclock = Mathf.Max(0f, overclock - Coop.OcDecay * Time.deltaTime);
                while (ocAlert > 0 && overclock < Coop.OcAlerts[ocAlert - 1] - 8f) ocAlert--;
            }
            if (shieldUntil > 0f && Time.time > shieldUntil) { shieldUntil = 0f; ShieldExpired(); }
            dmg.damageTakenMult = Mathf.Max(0.2f, 1f + ModSum("taken") + Rogue.TakenBonus(this));
            // 复苏（M4 中期增益）：5 秒没受伤后每秒回 2%
            float regen = Rogue.RegenRate(this);
            if (regen > 0f && !Downed && Time.time - lastHurtT > 5f) dmg.hp = Mathf.Min(dmg.maxHp, dmg.hp + dmg.maxHp * regen * Time.deltaTime);
        }

        protected virtual void ShieldExpired() { dmg.shield = 0f; }

        // ---------------- 通用工具 ----------------
        protected bool Buffered(ref float t) { if (Time.time - t <= BufferTime) { t = -9f; return true; } return false; }

        protected void Gravity(Vector3 horizontal)
        {
            // 立即提交水平位移，随后同一帧的命中形状才能使用移动后的真实位置。
            // 重力每帧只提交一次，避免组合动作重复下压。
            Vector3 gravity = !motionFrame || !motionSubmitted ? Vector3.down * 6f * Time.deltaTime : Vector3.zero;
            if (motionFrame) motionSubmitted = true;
            cc.Move(horizontal + gravity);
            transform.position = Combat.ClampToArena(transform.position, 0.6f);
        }

        protected void Face(Vector3 dir, float degPerSec)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return;
            var target = Quaternion.LookRotation(dir);
            transform.rotation = degPerSec <= 0f ? target : Quaternion.RotateTowards(transform.rotation, target, degPerSec * Time.deltaTime);
        }

        protected Vector3 AimDir()
        {
            Vector3 d = AimPoint - transform.position; d.y = 0f;
            return d.sqrMagnitude > 0.01f ? d.normalized : transform.forward;
        }

        protected Vector3 ClampReach(Vector3 to, float max)
        {
            Vector3 d = Flat(to - transform.position);
            if (d.magnitude > max) d = d.normalized * max;
            return Combat.ClampToArena(Flat(transform.position) + d, 0.5f);
        }

        protected void Enter(State s)
        {
            if (s != State.Move) CancelAttackApproach();
            if (state == State.Move && s != State.Move) { moveVelocity = moveDamping = Vector3.zero; }
            state = s;
            stateT = 0f;
            gAttacking = false;
            OnEnter(s);
            if (s == State.Ult && rogue.Has("thunder")) { thunderUntil = Time.time + 21f; nextThunder = Time.time + 0.5f; }
            if (s == State.Move && moveIn.sqrMagnitude <= 0.01f) anim.Loop(IdleAnim, 1f, 0.12f);
        }
        protected virtual void OnEnter(State s) { }

        // 行动优先级：闪避 > 接力 > 技能 > 解放 > 普攻
        protected bool TryActions(bool allowAttack)
        {
            if (Buffered(ref bufDodge)) { StartDodge(); return true; }
            if (relayTarget)
            {
                var to = relayTarget; relayTarget = null;
                if (to != this && !to.Downed && concerto >= concertoMax) { StartRelay(to); return true; }
            }
            if (Time.time >= skillReadyAt && Buffered(ref bufSkill))
            {
                skillReadyAt = Time.time + SkillCd * Coop.CdMultAt(transform.position) * Rogue.CdMult(this);
                StartSkill();
                Rogue.OnSkill(this);
                AddOverclock("skill");
                return true;
            }
            if (UltReady && Buffered(ref bufUlt))
            {
                energy = 0f;
                StartUlt();
                Rogue.OnUlt(this);
                AddOverclock("ult");
                return true;
            }
            if (EchoReady && Buffered(ref bufEcho))
            {
                echoReadyAt = Time.time + EchoCdNow * Coop.CdMultAt(transform.position);
                StartEcho();
                AddOverclock("echo");
                return true;
            }
            if (TryWeaponSkill()) return true;   // 武器技能（V）
            // 还没解锁的键：提示几级解锁（只给玩家）
            if (IsControlled && !UltUnlocked && Buffered(ref bufUlt)) Combat.Toast($"共鸣解放 {Growth.UltAt} 级解锁", new Color(0.8f, 0.8f, 0.85f));
            if (IsControlled && !EchoUnlocked && Buffered(ref bufEcho)) Combat.Toast($"声骸技能 {Growth.EchoAt} 级解锁", new Color(0.8f, 0.8f, 0.85f));
            if (allowAttack && Buffered(ref bufAttack))
            {
                if (approaching) return false;
                if (BeginAttackApproach()) return true;
                bool counter = Time.time < counterUntil;
                if (counter) { counterUntil = 0f; rogue.counterHitUntil = Time.time + 0.9f; }   // 回锋：反击这一套的伤害加成
                if (UseGenericBasic) GenericStartBasic(counter); else StartBasic(counter);   // 手持非原生种类的武器：通用普攻模组
                return true;
            }
            return false;
        }

        protected abstract void StartBasic(bool counter);
        protected abstract void StartSkill();
        protected abstract void StartUlt();
        protected abstract void DoIntro();                 // 变奏：接力时自己的入场攻击
        protected abstract void ApplyOutro(HeroBase to);   // 延奏：给接力对象的效果
        protected virtual void TickAttack() { Enter(State.Move); }
        protected virtual void TickSkill() { Enter(State.Move); }
        protected virtual void TickUlt() { Enter(State.Move); }

        // ---------------- 移动 ----------------
        void TickMove()
        {
            if (TryRescue()) return;
            if (TryActions(true)) return;
            if (TickAttackApproach()) return;
            MoveLocomotion();
        }

        // ---------------- 闪避（完美闪避：闪避开始 0.24 秒内躲过攻击；原稿取消时缓，改为下一击反击） ----------------
        protected void StartDodge()
        {
            Vector3 mv = moveIn;
            rogue.lastDodgeT = Time.time;
            dodgeDir = mv.sqrMagnitude > 0.01f ? mv.normalized : -transform.forward;
            float forward = Vector3.Dot(dodgeDir, transform.forward), side = Vector3.Dot(dodgeDir, transform.right);
            string clip = Mathf.Abs(forward) >= Mathf.Abs(side) ? (forward >= 0f ? "Dodge_Forward" : "Dodge_Backward") : (side >= 0f ? "Dodge_Right" : "Dodge_Left");
            if (!anim.Has(clip)) { clip = "Dodge_Forward"; Face(dodgeDir, 0f); }
            Enter(State.Dodge);
            dodgeStartPos = transform.position;
            dodgeStartT = Time.time;
            invulnUntil = Time.time + 0.3f;
            anim.Play(clip, 0.055f, anim.Length(clip) / DodgeDuration);
            if (FeelOn) Feel.I.Sfx("dodge", 0.7f, 0.1f);
            FX.Dust(transform.position, 4, new Color(0.4f, 0.4f, 0.45f, 0.5f));
        }

        void TickDodge()
        {
            const float dur = DodgeDuration, dist = DodgeDistance;
            float k0 = Mathf.Clamp01((stateT - Time.deltaTime) / dur), k1 = Mathf.Clamp01(stateT / dur);
            float e0 = DodgeProgress(k0), e1 = DodgeProgress(k1);
            Gravity(dodgeDir * (e1 - e0) * dist);
            if (stateT >= dur)
            {
                if (TryActions(true)) return;
                if (stateT >= dur + DodgeRecovery) Enter(State.Move);
            }
        }

        public void PerfectDodge()
        {
            if (Time.time < counterUntil) return;
            counterUntil = Time.time + 1.1f;
            invulnUntil = Mathf.Max(invulnUntil, Time.time + 0.35f);
            energy = Mathf.Min(energyMax, energy + 20f);
            concerto = Mathf.Min(concertoMax, concerto + 12f);
            perfectDodges++;
            GamepadFeedback.Perfect(this);
            Coop.perfectDodges++;
            Rogue.OnPerfectDodge(this);
            dmg.Flash(new Color(0.8f, 0.9f, 1f), 1f);
            FX.Ring(transform.position, 0.3f, 3.2f, 0.35f, new Color(0.8f, 0.9f, 1f, 0.9f), 0.12f);
            if (!FeelOn) return;
            Feel.I.Sfx("perfect_dodge", 0.9f, 0.02f);
            Feel.I.HitStop(0.06f, 0.05f);
            Feel.I.PulseHit(0.45f);
            DamageText.Status(transform.position + Vector3.up * 2.8f, "完美闪避", new Color(0.85f, 0.95f, 1f), 44);
        }

        // ---------------- 受击 ----------------
        protected virtual bool SuperArmor => state == State.Ult || state == State.Relay || state == State.Rescue;

        void OnHurt(HitInfo h, float dealt)
        {
            GamepadFeedback.Hurt(this,h.heavy);
            Vector3 np = transform.position + Vector3.up * 2.6f;
            if (FeelOn)
            {
                Feel.I.Sfx("player_hurt", 0.9f);
                Feel.I.Shake(h.heavy ? 0.55f : 0.35f);
                Feel.I.PulseHurt(h.heavy ? 1f : 0.7f);
                Feel.I.HitStop(h.heavy ? 0.08f : 0.05f);
            }
            DamageText.Taken(np, dealt, h.heavy, FeelOn);
            if (dmg.lastShieldAbsorbed > 1f) DamageText.Hit(null, np + Vector3.up * 0.4f, dmg.lastShieldAbsorbed, DT.Shield);
            FX.Sparks(transform.position + Vector3.up * 1.3f, h.dir + Vector3.up * 0.4f, 12, new Color(1f, 0.35f, 0.3f));
            lastHurtT = Time.time;
            if (FeelOn) RogueUI.CloseOnHurt();   // DS-006 P1：选卡面板打开时无法自卫，挨打就收起面板（待选保留）
            if (dmg.dead || SuperArmor || Rogue.IgnoreStagger(this, h)) return;
            invulnUntil = Time.time + 0.45f;
            hurtUntil = Time.time + (h.heavy ? 0.5f : 0.32f);
            Enter(State.Hurt);
            Face(-h.dir, 0f);
            anim.Play(h.heavy ? "Hit_B" : "Hit_A", 0.03f, 1.3f);
            cc.Move(Flat(h.dir) * h.knockback * 0.35f);
        }

        // ---------------- 倒地 / 救援 / 复活 ----------------
        void GoDown(bool overload)
        {
            if (Downed) return;
            // 共鸣护盾（M4 后期增益）：致命伤改成 3 秒无敌；超频满倒地不拦
            if (!overload && Rogue.TryGuard(this))
            {
                dmg.dead = false; dmg.hp = Mathf.Max(1f, dmg.maxHp * 0.05f);
                invulnUntil = Time.time + 3f;
                FX.Ring(transform.position, 0.3f, 2.6f, 0.6f, new Color(1f, 0.9f, 0.5f, 1f), 0.2f);
                if (IsControlled) Combat.Toast("共鸣护盾：3 秒无敌", new Color(1f, 0.9f, 0.5f));
                return;
            }
            if (state == State.Rescue && rescueTarget) { rescueTarget.rescuer = null; rescueTarget.rescueProgress = 0f; rescueTarget = null; }
            dmg.dead = true; dmg.hp = 0f; dmg.shield = 0f;
            Enter(State.Downed);
            downedUntil = Time.time + DownTime + Rogue.DownTimeBonus(this);
            rescueProgress = 0f; rescuer = null;
            downCount++;
            Coop.downs++;
            anim.Play("Death_A", 0.08f, 1f);
            FX.Tide(transform.position + Vector3.up, Vector3.up, 40);
            FX.Ring(transform.position, 0.3f, 2.4f, 0.5f, new Color(0.3f, 0.95f, 0.9f, 0.9f));
            Combat.Toast($"{HeroName}{(overload ? "超频过载" : "")}倒地 · 15 秒内靠近按 {GameInput.Hint("F","A")} 救援", new Color(1f, 0.45f, 0.4f));
            if (FeelOn) Feel.I.PulseHurt(1f);
        }

        void TickDowned()
        {
            Gravity(Vector3.zero);
            if (Random.value < 0.3f) FX.Tide(transform.position + Vector3.up * 0.3f + Random.insideUnitSphere * 0.6f, Vector3.up, 1);
            if (rescuer && rescuer.state != State.Rescue) { rescuer = null; rescueProgress = 0f; }
            if (Time.time >= downedUntil) Revive(null);
        }

        // 救援：按住 F 靠近倒地的队友。救援动作中无敌（原稿：特定无敌动画）
        bool TryRescue()
        {
            if (!interactHeld) return false;
            HeroBase best = null; float bd = RescueRange;
            foreach (var h in Combat.heroes)
            {
                if (h == this || !h.Downed || (h.rescuer && h.rescuer != this)) continue;
                float d = Vector3.Distance(Flat(h.transform.position), Flat(transform.position));
                if (d <= bd) { bd = d; best = h; }
            }
            if (!best) return false;
            rescueTarget = best;
            best.rescuer = this;
            best.rescueProgress = 0f;
            Face(best.transform.position - transform.position, 0f);
            Enter(State.Rescue);
            anim.Play("Interact", 0.1f, 1f);
            return true;
        }

        void TickRescue()
        {
            Gravity(Vector3.zero);
            invulnUntil = Mathf.Max(invulnUntil, Time.time + 0.1f);
            if (!rescueTarget || !rescueTarget.Downed || !interactHeld || rescueTarget.rescuer != this)
            {
                if (rescueTarget && rescueTarget.rescuer == this) { rescueTarget.rescuer = null; rescueTarget.rescueProgress = 0f; }
                rescueTarget = null;
                Enter(State.Move);
                return;
            }
            rescueTarget.rescueProgress += Time.deltaTime * Rogue.RescueSpeed(this);
            if (Random.value < 0.4f) FX.Tide(rescueTarget.transform.position + Vector3.up * 0.5f, Vector3.up, 1);
            if (rescueTarget.rescueProgress >= RescueHold)
            {
                var t = rescueTarget;
                rescueTarget = null;
                rescues++;
                t.Revive(this);
                Enter(State.Move);
            }
        }

        public void Revive(HeroBase by)
        {
            int idx = Mathf.Min(downCount - 1, 3);
            dmg.dead = false;
            overclock = 0f; ocAlert = 0;
            rescuer = null; rescueProgress = 0f;
            Vector3 side = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * 2.6f;
            Vector3 rp = Combat.ClampToArena(transform.position + side, 1f);
            if (by)
            {
                dmg.hp = dmg.maxHp * (rogue.Has("rescue") ? 0.6f : 0.3f);
                int ri = by.Id == HeroId.Chengyin ? idx - 1 : idx;   // 澄音救起的队友，残像降一级
                if (ri >= 0)
                {
                    SliceGame.I.SpawnResidual(rp, ri, null);
                    Combat.Toast($"{by.HeroName}救起{HeroName} · 黑潮凝出{SliceGame.ResidualName(ri)}残像", new Color(0.4f, 1f, 0.9f));
                }
                else Combat.Toast($"{by.HeroName}救起{HeroName}，没有残像", new Color(0.5f, 1f, 0.7f));
            }
            else
            {
                penalized = true;                  // 掉 1 级：生命、伤害都按低一级算（6 级时：生命 2.7 → 2.4、伤害 2.0 → 1.9）
                dmg.maxHp = MaxHpNow;
                dmg.hp = dmg.maxHp * 0.5f;
                SliceGame.I.SpawnResidual(rp, Mathf.Max(0, idx), this);
                Combat.Toast($"{HeroName}超时复活 · 掉 1 级，击败遗留残像可恢复", new Color(1f, 0.5f, 0.4f));
            }
            invulnUntil = Time.time + 1.5f;
            Enter(State.Revive);
            anim.Play("Lie_StandUp", 0.1f, 2.6f, 0.2f);
            FX.Ring(transform.position, 0.3f, 3f, 0.5f, new Color(0.5f, 1f, 0.7f, 0.9f));
        }

        // 团灭后整队在安全处复活（开放地图）
        public void Respawn(Vector3 pos, float hpRatio)
        {
            if (rescueTarget && rescueTarget.rescuer == this) { rescueTarget.rescuer = null; rescueTarget.rescueProgress = 0f; }
            rescueTarget = null;
            dmg.dead = false;
            dmg.hp = dmg.maxHp * hpRatio;
            dmg.shield = 0f;
            overclock = 0f; ocAlert = 0;
            rescuer = null; rescueProgress = 0f;
            downCount = 0;
            cc.enabled = false; transform.position = pos; cc.enabled = true;
            invulnUntil = Time.time + 2f;
            Enter(State.Revive);
            anim.Play("Lie_StandUp", 0.1f, 2.6f, 0.2f);
            FX.Ring(pos, 0.3f, 3f, 0.5f, new Color(0.5f, 1f, 0.7f, 0.9f));
        }

        public void ClearPenalty()
        {
            if (!penalized) return;
            penalized = false;
            float ratio = dmg.hp / dmg.maxHp;
            dmg.maxHp = MaxHpNow;
            dmg.hp = dmg.dead ? 0f : dmg.maxHp * ratio;
            Combat.Toast($"{HeroName}夺回了等级", new Color(0.5f, 1f, 0.7f));
        }

        // ---------------- 成长：经验与升级（只有击杀给经验，见 Growth.OnKill） ----------------
        public void AddExp(float x)
        {
            if (level >= Growth.MaxLevel) return;
            exp += x;
            int before = level;
            int cap = Growth.LevelCap;
            while (level < cap && exp >= Growth.ExpNeed(level)) { exp -= Growth.ExpNeed(level); level++; }
            if (level >= Growth.MaxLevel) exp = 0f;
            if (level != before) LevelUp(before);
        }

        void LevelUp(int from)
        {
            // 生命上限提高的部分直接补上（倒地中的不回血）
            float oldMax = dmg.maxHp;
            dmg.maxHp = MaxHpNow;
            if (!dmg.dead) dmg.hp = Mathf.Min(dmg.maxHp, dmg.hp + Mathf.Max(0f, dmg.maxHp - oldMax));
            Vector3 p = transform.position;
            FX.Ring(p, 0.3f, 3.4f, 0.6f, new Color(1f, 0.85f, 0.45f, 0.95f), 0.16f);
            FX.Ring(p, 0.2f, 2.2f, 0.45f, new Color(Color.r, Color.g, Color.b, 0.9f), 0.12f);
            FX.Sparks(p + Vector3.up * 1.2f, Vector3.up, 24, new Color(1f, 0.88f, 0.55f));
            FX.Embers(p + Vector3.up * 0.4f, 18);
            if (RunDirector.I) RunDirector.I.Event("levelup", "", $"{HeroName} {from}->{level}");
            if (!IsControlled) return;
            string unlock = "";
            for (int lv = from + 1; lv <= level; lv++) { string u = Growth.UnlockText(lv); if (u != "") unlock += "  ·  " + u; }
            Combat.Toast($"升到 {level} 级{unlock}", new Color(1f, 0.85f, 0.45f));
            Feel.I.Sfx("perfect_dodge", 0.7f, 0f, 1.3f);
            Feel.I.PulseHit(0.35f);
        }

        // ---------------- 声骸技能（R，2 级解锁）：通用的近身爆发，按网页原型 echo（倍率 1.4 → ×2、削韧 18 → ×3） ----------------
        static readonly List<Damageable> echoTmp = new List<Damageable>();
        bool echoFired;

        protected virtual void StartEcho()
        {
            Enter(State.Echo);
            echoFired = false;
            anim.Play("Spellcast_Raise", 0.05f, 2.4f);
            if (FeelOn) Feel.I.Sfx("ult_charge", 0.45f, 0.05f, 1.8f);
        }

        void TickEcho()
        {
            Gravity(Vector3.zero);
            if (!echoFired && stateT >= 0.2f)
            {
                echoFired = true;
                if (echo != null && echo.id != "messenger") { Echoes.Cast(this, echo); Gain(8f, 6f); }   // 声骸：召唤敌人幽影出招
                else
                {
                if (echo != null) Echoes.Cast(this, echo);   // 信使声骸：幽影陪着出现，效果走下面的强化爆发
                Vector3 c = Flat(transform.position);
                echoTmp.Clear();
                foreach (var e in Combat.enemies)
                    if (e && !e.dead && Vector3.Distance(Flat(e.transform.position), c) <= EchoR + e.radius) echoTmp.Add(e);
                if (rogue.Has("vortex"))
                {
                    // 飓风场：先把 8 米内的杂兵拉到身边，再一起吃声骸伤害
                    foreach (var e in Combat.enemies)
                    {
                        if (!e || e.dead || e.dormant || e.bossCtl || echoTmp.Contains(e)) continue;
                        Vector3 d = Flat(e.transform.position) - c;
                        if (d.magnitude > 8f) continue;
                        e.transform.position = Combat.ClampToArena(c + d.normalized * Mathf.Min(d.magnitude, 1.8f), e.radius);
                        echoTmp.Add(e);
                    }
                    FX.Ring(transform.position, 8f, 1f, 0.35f, new Color(0.6f, 1f, 0.9f, 0.8f), 0.2f);
                }
                foreach (var e in echoTmp)
                {
                    bool cut = echoEmpowered && e.bossCtl && e.bossCtl.Chanting;
                    bool king = echoEmpowered && e.bossCtl && e.bossCtl.sovereign;
                    if (king) e.bossCtl.Silence(1.6f, HeroName + "的信使声骸");
                    DealHit(e, echoEmpowered ? (king ? 4.5f * 2.5f : 4.5f) : 2.8f, cut && !king ? 99999f : echoEmpowered ? 108f : 54f, 4f, Flat(e.transform.position - transform.position).normalized, true, "echo");
                }
                if (rogue.Has("echo_blast"))
                {
                    // 共鸣爆炸：6 米爆炸，倍率 1.5（约一只轻波 1/3 的血，原稿）
                    foreach (var e in Combat.enemies.ToArray())   // 快照：打死的敌人会从列表里移除
                        if (e && !e.dead && !e.dormant && Vector3.Distance(Flat(e.transform.position), c) <= 6f + e.radius)
                            DealHit(e, Rogue.EchoBlastMult(this), 20f, 2f, Flat(e.transform.position - transform.position).normalized, false, "echo");
                    FX.Ring(transform.position, 0.5f, 6.4f, 0.45f, new Color(0.5f, 0.95f, 1f, 1f), 0.3f);
                }
                Color ec = new Color(0.45f, 0.95f, 1f);
                FX.Ring(transform.position, 0.4f, EchoR + 0.6f, 0.4f, new Color(ec.r, ec.g, ec.b, 0.95f), 0.18f);
                if (echoEmpowered) FX.Ring(transform.position, 0.3f, EchoR + 1.2f, 0.5f, new Color(1f, 0.4f, 0.85f, 0.9f), 0.12f);
                FX.Sparks(transform.position + Vector3.up * 1f, Vector3.up, 26, Color.Lerp(ec, Color.white, 0.4f));
                FX.Tide(transform.position + Vector3.up * 0.6f, Vector3.up, 20);
                Feel.I.Sfx("ult_hit", FeelOn ? 0.6f : 0.3f, 0.05f, 1.5f);
                if (FeelOn && echoTmp.Count > 0) { Feel.I.HitStop(0.05f); Feel.I.Shake(0.25f); }
                Gain(8f, 6f);
                }
            }
            if (echoFired && Buffered(ref bufDodge)) { StartDodge(); return; }
            if (stateT >= 0.45f) Enter(State.Move);
        }

        // ---------------- 协奏接力 ----------------
        void StartRelay(HeroBase to)
        {
            concerto = 0f;
            relaysDone++;
            Enter(State.Relay);
            invulnUntil = Time.time + 0.5f;
            DoIntro();
            ApplyOutro(to);
            Rogue.OnRelay(this, to);
            Vector3 a = transform.position + Vector3.up * 1.4f, b = to.transform.position + Vector3.up * 1.4f;
            for (int i = 0; i <= 6; i++) FX.Sparks(Vector3.Lerp(a, b, i / 6f), Vector3.up, 3, Color);
            FX.Ring(to.transform.position, 0.3f, 2.6f, 0.45f, new Color(Color.r, Color.g, Color.b, 0.9f), 0.14f);
            if (FeelOn || to.IsControlled) Combat.Toast($"协奏接力  {HeroName} → {to.HeroName}", Color);
            Coop.Relay(this, to);
        }

        // ---------------- 结算：英雄 → 敌人 ----------------
        // 职业系数、延奏和超频增伤、架势加成、风障旁 Boss 架势翻倍、仇恨记录、统计
        public float DealHit(Damageable e, float mult, float stagger, float knock, Vector3 dir, bool heavy, string kind, float brokenBonus = 0f, bool canCrit = true)
        {
            if (!e || e.dead) return 0f;
            bool broken = e.Broken;
            bool crit = canCrit && Random.value < 0.12f + Rogue.CritChance(this);
            float ocBonus = overclock / 100f * Coop.OcMaxBonus;
            float d = baseAtk * dmgScale * mult * Growth.DmgMult(EffLevel) * Economy.WeaponMult(kit.weaponStar) * (1f + ModSum("dmg") + ocBonus + Rogue.DmgBonus(this, e) + WeaponAffix.DmgBonus(this, e, kind)) * (crit ? 1.6f + Rogue.CritDmg(this) : 1f) * Random.Range(0.95f, 1.05f);
            if (broken) d *= 1f + brokenBonus;
            float s = stagger * (1f + ModSum("stagger") + Rogue.StaggerBonus(this, e) + WeaponAffix.StaggerBonus(this));
            bool isBoss = e.bossCtl;
            if (isBoss && WindWall.Near(e.transform.position, e.radius + 0.8f)) s *= 2f;
            var info = new HitInfo { damage = d, stagger = s, knockback = knock, point = e.Center, dir = dir, heavy = heavy, crit = crit, kind = kind, attacker = gameObject };
            float dealt = e.TakeHit(info);
            if (dealt <= 0f) return 0f;
            GamepadFeedback.Hit(this,heavy,kind);
            dmgDone += dealt;
            if (isBoss) e.bossCtl.RecordHit(this, dealt);
            OnDealt(dealt);
            if (crit) Rogue.OnCrit(this, e, kind);
            if (kind == "basic" || kind == "heavy") Rogue.OnMeleeHit(this, e);   // DS-019b：连锁打击溅射
            WeaponAffix.OnHit(this, e, dealt, crit, kind);
            Vector3 np = e.transform.position + Vector3.up * (Mathf.Min(1.6f, e.height * 0.45f) + 0.8f);
            if (crit) FX.Crit(e.Center, FeelOn ? 2.4f : 1.5f);
            var dk = DamageText.FromHit(kind, crit, broken, FeelOn);
            DamageText.Hit(e, np, dealt, dk, dk == DT.Ally ? Color.Lerp(Color, Color.white, 0.45f) : (Color?)null);
            if (!broken && e.Broken) DamageText.Status(np + Vector3.up * 0.7f, "失衡!", new Color(1f, 0.55f, 0.22f), FeelOn ? 46 : 32);   // 这一下打空了架势
            return dealt;
        }

        protected virtual void OnDealt(float dealt) { }

        // 远程弹道命中（青岚、澄音）
        public virtual void ProjectileHit(Damageable e, Projectile p)
        {
            float dealt = DealHit(e, p.mult, p.stagger, p.knock, p.dir, p.heavy, p.kind);
            if (dealt <= 0f) return;
            Vector3 hp = e.transform.position + Vector3.up * Mathf.Min(1.6f, e.height * 0.45f) - p.dir * e.radius * 0.6f;
            FX.Sparks(hp, -p.dir + Vector3.up * 0.3f, p.heavy ? 16 : 7, Color.Lerp(Color, Color.white, 0.3f));
            FX.ImpactFlash(hp, p.heavy ? 1.8f : 1.1f, Color);
            OnBasicLanded(p.heavy, p.kind == "counter");
            if (FeelOn)
            {
                if (p.heavy) { Feel.I.HitStop(0.04f); Feel.I.Shake(0.12f); }
                Feel.I.SfxRandom("hit", 3, 0.45f);
            }
        }

        protected virtual void OnBasicLanded(bool heavy, bool counter) { }

        // ---------------- 治疗与护盾（澄音的静电在 OnHealed 里结算） ----------------
        public void Heal(HeroBase target, float amount, bool shield = false, float shieldDur = 8f)
        {
            if (!target || target.Downed || amount <= 0f) return;
            float done;
            if (shield)
            {
                float before = target.dmg.shield;
                target.dmg.shield = Mathf.Max(target.dmg.shield, amount);
                target.shieldUntil = Mathf.Max(target.shieldUntil, Time.time + shieldDur);
                done = target.dmg.shield - before;
                if (done > 0f) FX.HexShield(target.dmg, 1.15f, shieldDur, new Color(0.5f, 0.8f, 1f, 0.6f));   // DS-027：坚守=六边盾片（不再是护盾泡）
            }
            else
            {
                float before = target.dmg.hp;
                target.dmg.hp = Mathf.Min(target.dmg.maxHp, target.dmg.hp + amount);
                done = target.dmg.hp - before;
                if (done > target.dmg.maxHp * 0.03f) DamageText.Heal(target.transform.position + Vector3.up * 2.7f, done, target.IsControlled || IsControlled);
            }
            if (done <= 0f) return;
            healDone += done;
            OnHealed(target, done, shield);
        }

        protected virtual void OnHealed(HeroBase target, float amount, bool shield) { }

        // ---------------- 超频（侵蚀 ≥ 45%）：越打伤害越高，满 100 倒地 ----------------
        public void AddOverclock(string kind, float amount = -1f)
        {
            float e = Coop.ErosionAt(transform.position);
            if (e < 45f || Downed) return;
            float g = (amount >= 0f ? amount : Coop.OcGain(kind, e)) * Rogue.OcGainMult(this);
            if (g <= 0f) return;
            overclock = Mathf.Min(100f, overclock + g);
            while (ocAlert < Coop.OcAlerts.Length && overclock >= Coop.OcAlerts[ocAlert])
            {
                if (IsControlled) Combat.Toast($"超频 {Coop.OcAlerts[ocAlert]:0}%  · 伤害 +{overclock / 100f * Coop.OcMaxBonus * 100f:0}%", new Color(0.4f, 1f, 0.95f));
                ocAlert++;
            }
            if (overclock >= 100f) GoDown(true);
        }

        public void Gain(float en, float co)
        {
            energy = Mathf.Min(energyMax, energy + en * Rogue.EnergyMult(this));
            concerto = Mathf.Min(concertoMax, concerto + co * Rogue.ConcertoMult(this));
        }

        // 被 Boss 推挤
        public void Push(Vector3 v) { if (cc && cc.enabled) cc.Move(v); }
    }
}

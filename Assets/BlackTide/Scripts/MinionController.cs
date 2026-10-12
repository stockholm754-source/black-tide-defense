using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 残像：会被打断、会被击退的杂兵。
    // 选目标用动态仇恨（已经被很多杂兵追的角色会被降权）；能被旋流吸、被音锚定身、被风障减速。
    // 倒地救援刷出的残像也用它：更大、更硬、更痛；超时复活留下的「遗留残像」被打死时，主人恢复等级。
    // 第四批兵种（Kind，设计见 Design/art_batch4_enemies.md）：每种对应一种应对方式——
    //   盾卫：正面来的伤害只吃 20%，绕后、打失衡或被旋流拉扯时破防；潮囊：贴近鼓胀 1 秒后自爆，被打死也会爆；
    //   咏唱者：远处咏唱 2.5 秒给周围友军上护盾，重击或打掉它 15% 的血就打断；潮刃剑士：三连斩，练完美闪避；
    //   珊瑚投矛手：远程直线投矛（预警 1.4 秒），风障能挡；唤潮祭司：远处治疗友军、召唤残像，优先击杀。
    // 2026-10-09 招式扩充（每种 2–3 招，按级别给，分工：出伤 / 范围 / 控制 / 治疗 / 增益）：
    //   残像：劈砍 + 扑击（4–7 米突进）；盾卫：盾击 + 护卫光环（5 米内友军受伤 −30%，先拆盾卫）；
    //   潮囊：自爆 + 死后留下 5 秒黑潮泥沼（减速）；咏唱者：群体护盾 / 迅捷祝福（友军加速、出手更快）交替；
    //   重甲潮兵：重砸 + 冲锋撞击（5–11 米）+ 半血以下怒吼（自己受伤 −40%、身边友军 −25%，4 秒）；
    //   潮刃剑士：三连斩 + 突进刺 + 格挡架势（1.2 秒内被打会立刻反击，别硬砍）；
    //   珊瑚投矛手：贯穿投矛 + 钉地矛阵（三个圈，被刺中定身 1.2 秒）。
    public class MinionController : MonoBehaviour, IMob
    {
        public enum Kind { Claw, Shield, Bloat, Chanter, Duelist, Javelin, Priest }
        public Kind kind;
        public bool summoned;   // DS-018：黑潮王召唤的残像（死亡掉治疗微粒）
        public bool hunter;     // DS-018b：黑潮追猎者（存活计数用）
        public bool tide;       // 割草方向重做：黑潮涌浪的兵（存活计数用；经验/贝币打折，不掉声骸武器）

        enum S { Spawn, Chase, Windup, Strike, Hurt, Cast, Dead, Parry }
        S state;
        float t;
        CharacterAnim anim;
        public Damageable dmg;
        Telegraph tel;
        AttackShape shape;
        Combat.Threat threat;
        void ClearThreat() { if (threat != null) Combat.threats.Remove(threat); threat = null; }
        public Vector3 knockVel;   // DS-019b：漩涡卡外部牵引（原本仅内部击退）
        float nextAttackAt;
        float animLen;
        const string AtkAnim = "1H_Melee_Attack_Chop";
        const float Impact = 0.55f, Windup = 0.62f;
        // 当前这一下的参数（爪兵就是上面两个常数）
        float curWindup = Windup, curDmg = 85f;
        string curAnim = AtkAnim;
        bool curHeavy;

        HeroBase target;
        float retargetAt;
        public float slowUntil;
        float rootedUntil, pullUntil;
        Vector3 pullCenter;
        float dmgMult = 1f, reach = 2.3f, coneR = 2.8f;
        HeroBase residualOf;
        public bool IsResidual => residualOf;   // 倒地队友留下的遗留残像（不掉声骸）
        // 开放地图：节点里的敌人先待命，有人走近或挨打就苏醒；leash > 0 时只追节点范围内的人，没人就回家
        public bool dormant;
        public Vector3 home;
        public float leash;
        public System.Action onWake;
        public static MinionController Last;   // 刚生成的那只（节点用来登记）

        // 第四批兵种的状态
        int combo;
        bool comboNext, exploded, blockedThisHit, castHeal;
        float nextCastAt, castDmg, castDur, bloatDeathAt = -1f, lastBlockFx;
        Telegraph castTel;
        readonly List<MinionController> summons = new List<MinionController>();
        public System.Action<MinionController> onSummon;   // 祭司召唤出来的残像交给节点登记

        // 招式扩充
        enum Sp { None, Lunge, Charge }
        Sp curSp;
        float nextSpecialAt, nextRoarAt, nextParryAt, nextAuraTick;
        public float hasteUntil;              // 咏唱者的迅捷祝福
        bool brute, parried, castHaste;
        int castCount;
        Vector3 dashFrom, dashTo; float dashT0 = -1f, dashDur;
        GameObject guardRing;
        bool Hasted => Time.time < hasteUntil;

        float Speed => (kind == Kind.Bloat ? 2.7f : kind == Kind.Shield ? 3.3f : 3.9f) * (Hasted ? 1.35f : 1f);
        float KeepAway => kind == Kind.Chanter ? 7f : kind == Kind.Javelin ? 11f : kind == Kind.Priest ? 9f : 0f;
        bool Caster => kind == Kind.Chanter || kind == Kind.Priest;

        public void Wake()
        {
            if (!dormant) return;
            dormant = false;
            dmg.dormant = false;
            onWake?.Invoke();
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        public void Setup(GameObject modelGo, float hpMult = 1f, float dmgMul = 1f, float size = 1f, HeroBase residualOwner = null)
        {
            anim = gameObject.AddComponent<CharacterAnim>();
            anim.Init(modelGo.GetComponentInChildren<Animator>(), true);   // 敌人离屏不驱动骨骼（割草密度的 CPU 预算）
            dmg = gameObject.AddComponent<Damageable>();
            dmg.team = Team.Enemy;
            int team = Mathf.Max(1, Combat.heroes.Count);
            float teamScale = team >= 3 ? 2.4f : team == 2 ? 1.7f : 1f;
            dmg.maxHp = dmg.hp = 560f * teamScale * hpMult * Coop.OpeningHpRelief();   // DS-028：前期启动宽限（只削前 3–6 分钟）
            dmg.radius = 0.55f * size; dmg.height = 2.1f * size;
            dmg.onHit = OnHit;
            dmg.onDeath = OnDeath;
            dmgMult = dmgMul;
            reach = 2.3f * Mathf.Sqrt(size); coneR = 2.8f * Mathf.Sqrt(size);
            residualOf = residualOwner;
            var col = gameObject.AddComponent<CapsuleCollider>();
            col.radius = 0.45f * size; col.height = 2f * size; col.center = new Vector3(0, size, 0);
            Combat.enemies.Add(dmg);
            Last = this;
            dmg.tier = dmgMul >= 1.5f ? 1 : 0;
            animLen = anim.Length(AtkAnim);
            anim.Play("Spawn_Ground_Skeletons", 0f, 2.4f);
            brute = gameObject.name.Contains("重甲");
            nextSpecialAt = Time.time + Random.Range(2.5f, 4.5f);
            FX.Tide(transform.position, Vector3.up, 25);
            nextAttackAt = Time.time + Random.Range(1.5f, 2.5f);
        }

        // 第四批兵种：在 Setup 之后调，按兵种改血量和参数（血量系数让一个节点的总血量和换兵种之前差不多）
        public void SetKind(Kind k)
        {
            kind = k;
            float hpK = 1f;
            switch (k)
            {
                case Kind.Shield:
                    hpK = 1.5f;
                    dmg.staggerMax = 220f; dmg.breakDuration = 3f;
                    dmg.damageFilter = ShieldFilter;
                    GuardRing();
                    break;
                case Kind.Bloat: hpK = 0.6f; break;
                case Kind.Chanter: hpK = 0.8f; nextCastAt = Time.time + Random.Range(3f, 5f); break;
                case Kind.Duelist: hpK = 0.8f; dmg.damageFilter = ParryFilter; nextParryAt = Time.time + Random.Range(4f, 7f); break;
                case Kind.Javelin: hpK = 0.6f; nextAttackAt = Time.time + 2f; break;
                case Kind.Priest: hpK = 0.7f; nextCastAt = Time.time + Random.Range(4f, 6f); dmg.priority = true; break;
            }
            dmg.maxHp *= hpK; dmg.hp = dmg.maxHp;
        }

        public void Pull(Vector3 center, float until) { pullCenter = center; pullUntil = Mathf.Max(pullUntil, until); }
        public void Root(float dur) { rootedUntil = Mathf.Max(rootedUntil, Time.time + dur); }
        public void Slow(float until) { slowUntil = Mathf.Max(slowUntil, until); }

        void SetTarget(HeroBase h)
        {
            if (target == h) return;
            if (target) target.chasers = Mathf.Max(0, target.chasers - 1);
            target = h;
            if (target) target.chasers++;
        }

        void Update()
        {
            t += Time.deltaTime;
            if (knockVel.sqrMagnitude > 0.01f)
            {
                transform.position = Combat.ClampToArena(transform.position + knockVel * Time.deltaTime, 0.6f);
                knockVel = Vector3.Lerp(knockVel, Vector3.zero, Time.deltaTime * 9f);
            }
            if (state == S.Dead)
            {
                if (bloatDeathAt > 0f && Time.time >= bloatDeathAt) { bloatDeathAt = -1f; Explode(); }
                // 倒地后溶解成黑潮：缩小、往下沉、冒粒子（原来只是慢慢沉进地里）
                if (t > 1.1f)
                {
                    float k = Mathf.Clamp01((t - 1.1f) / 0.7f);
                    transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.15f, k * k);
                    transform.position += Vector3.down * Time.deltaTime * 0.6f;
                    if (Random.value < 0.6f) FX.Tide(transform.position + Vector3.up * 0.5f, Vector3.up, 2);
                }
                if (t > 1.85f) { FX.Tide(transform.position + Vector3.up * 0.4f, Vector3.up, 12); Destroy(gameObject); }
                return;
            }
            // 旋流：被吸向中心
            if (Time.time < pullUntil)
            {
                Vector3 to = Flat(pullCenter - transform.position);
                if (to.magnitude > 0.6f) transform.position = Combat.ClampToArena(transform.position + to.normalized * Mathf.Min(7f, to.magnitude * 3f) * Time.deltaTime, 0.6f);
            }
            // 待命：有人走近（11 米）就苏醒
            if (dormant)
            {
                if (state != S.Spawn || t > 1.7f) anim.Loop("Idle", 1f);
                var near = Combat.NearestHero(transform.position);
                if (near && Vector3.Distance(Flat(near.transform.position), Flat(transform.position)) < 11f) Wake();
                else return;
            }
            // 选目标：目标倒地或每隔一会儿重新挑（动态仇恨）；有节点范围时只追范围内的人
            if (!target || target.dmg.dead || Time.time >= retargetAt || (leash > 0f && !InLeash(target)))
            {
                SetTarget(null);   // 先把自己从旧目标的追兵数里去掉，再按"距离 + 追兵数 × 2 米"重新挑
                var nt = Combat.NearestHero(transform.position, 2f);
                SetTarget(nt && (leash <= 0f || InLeash(nt)) ? nt : null);
                retargetAt = Time.time + Random.Range(2f, 3.5f);
            }
            Separate();
            if (!target)
            {
                if (state == S.Cast) InterruptCast(false);
                // 没人可追：有家就回家
                Vector3 back = Flat(home - transform.position);
                if (leash > 0f && back.magnitude > 2f && state == S.Chase)
                {
                    Vector3 homeDir = Nav.Dir(this, transform.position, home);
                    Face(homeDir, 420f);
                    anim.Loop("Running_A", 1.05f);
                    transform.position = Combat.ClampToArena(transform.position + homeDir * 3.9f * Time.deltaTime, 0.6f);
                }
                else if (state == S.Chase) anim.Loop("Idle", 1f);
                return;
            }
            Vector3 dir = Flat(target.transform.position - transform.position);
            float d = dir.magnitude;
            bool rooted = Time.time < rootedUntil || Time.time < pullUntil;
            if (dashT0 >= 0f)
            {
                float k = Mathf.Clamp01((Time.time - dashT0) / dashDur);
                transform.position = Combat.ClampToArena(Vector3.Lerp(dashFrom, dashTo, 1f - (1f - k) * (1f - k)), 0.6f);
                if (Random.value < 0.5f) FX.Dust(transform.position, 1, new Color(0.35f, 0.38f, 0.4f, 0.6f));
                if (k >= 1f) dashT0 = -1f;
            }
            if (kind == Kind.Shield) TickGuardAura();
            if (Hasted && Random.value < 0.2f) FX.Embers(transform.position + Vector3.up * Random.Range(0.6f, 1.8f), 1);
            switch (state)
            {
                case S.Spawn:
                    if (t > 1.7f) Enter(S.Chase);
                    break;
                case S.Chase:
                    if (kind == Kind.Bloat) { TickBloat(dir, d, rooted); break; }
                    if (KeepAway > 0f) { TickRanged(dir, d, rooted); break; }
                    Face(dir, kind == Kind.Shield ? 160f : 420f);   // 盾卫转身慢，给绕后的机会
                    if (!rooted && TrySpecial(dir, d)) break;
                    if (d < reach && Time.time >= nextAttackAt && !rooted) { StartAttack(dir); break; }
                    if (d > 1.7f && !rooted)
                    {
                        float sp = Speed * (Time.time < slowUntil ? 0.6f : 1f);
                        anim.Loop("Running_A", 1.05f * sp / 3.9f);
                        Vector3 step = d > 5f ? Nav.Dir(this, transform.position, target.transform.position) : transform.forward;
                        if (d > 5f) Face(step, 600f);
                        transform.position = Combat.ClampToArena(transform.position + step * sp * Time.deltaTime, 0.6f);
                    }
                    else anim.Loop(kind == Kind.Shield ? "Blocking" : "Idle", 1f);
                    break;
                case S.Windup:
                    if (kind == Kind.Javelin) Face(Flat(shape.fwd), 0f);
                    if (t >= curWindup)
                    {
                        if (kind == Kind.Bloat) { Explode(); exploded = true; dmg.TakeHit(new HitInfo { damage = 1e9f, kind = "self" }); break; }
                        anim.Speed = 1.1f; Hit(); Enter(S.Strike);
                    }
                    break;
                case S.Strike:
                    if (comboNext && t > 0.12f) { comboNext = false; StartAttack(Flat(target.transform.position - transform.position)); break; }
                    if (t > 0.45f) { combo = 0; nextAttackAt = Time.time + (Random.Range(1.3f, 2.1f) + (kind == Kind.Javelin ? 2f : 0f)) * (Hasted ? 0.65f : 1f); Enter(S.Chase); }
                    break;
                case S.Cast:
                    Face(dir, 120f);
                    if (Random.value < 0.4f) FX.Sparks(transform.position + Vector3.up * Random.Range(0.3f, 2f), Vector3.up, 1, castHeal || kind == Kind.Priest ? new Color(0.4f, 1f, 0.8f) : new Color(0.75f, 0.45f, 1f));
                    if (t >= castDur) ResolveCast();
                    break;
                case S.Hurt:
                    if (t > 0.36f) Enter(S.Chase);
                    break;
                case S.Parry:
                    Face(dir, 360f);
                    anim.Loop("Blocking", 1f);
                    if (Random.value < 0.35f) FX.Sparks(transform.position + Vector3.up * 1.4f + transform.forward * 0.5f, Vector3.up, 1, new Color(0.7f, 0.9f, 1f));
                    if (t > 1.2f) { nextParryAt = Time.time + Random.Range(9f, 13f); Enter(S.Chase); }
                    break;
            }
        }

        void Enter(S s) { state = s; t = 0f; }

        void Face(Vector3 dir, float deg)
        {
            if (dir.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), deg * Time.deltaTime);
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
            foreach (var h in Combat.heroes)
            {
                if (!h || h.Downed) continue;
                Vector3 dp = Flat(transform.position - h.transform.position);
                float min = dmg.radius + 0.5f;
                if (dp.magnitude < min && dp.magnitude > 0.01f) transform.position += dp.normalized * (min - dp.magnitude);
            }
        }

        // ---------------- 近战（爪兵、盾卫、剑士）----------------
        void StartAttack(Vector3 to)
        {
            curSp = Sp.None;
            transform.rotation = Quaternion.LookRotation(to);
            if (kind == Kind.Duelist)
            {
                // 三连斩：第一下正常预警，后两下很快（0.38 秒），每下伤害约一半
                string[] a = { "Dualwield_Melee_Attack_Slice", "Dualwield_Melee_Attack_Chop", "Dualwield_Melee_Attack_Stab" };
                curAnim = a[combo % 3];
                curWindup = combo == 0 ? 0.6f : 0.38f;
                curDmg = 85f * 0.55f; curHeavy = combo == 2;
                shape = AttackShape.Cone(Flat(transform.position), to, coneR * 0.95f, 80f);
            }
            else
            {
                curAnim = AtkAnim; curWindup = Windup; curDmg = 85f; curHeavy = dmgMult > 1.5f;
                shape = AttackShape.Cone(Flat(transform.position), to, coneR, 100f);
            }
            Begin(new Color(1f, 0.3f, 0.16f, 0.75f));
        }

        void Begin(Color col)
        {
            if (Hasted) curWindup *= 0.85f;
            tel = shape.Show(curWindup, col);
            ClearThreat();
            threat = new Combat.Threat { owner = dmg, shape = shape, resolveAt = Time.time + curWindup };
            Combat.threats.Add(threat);
            float len = anim.Length(curAnim);
            anim.Play(curAnim, 0.08f, Impact * len / curWindup);
            Enter(S.Windup);
        }

        void Hit()
        {
            ClearThreat();
            if (tel) tel.Resolve();
            Feel.I.Sfx(kind == Kind.Javelin ? "boss_swing" : "swing_3", 0.45f, 0.1f, 1.3f);
            if (kind == Kind.Javelin)
            {
                Vector3 a = shape.origin + Vector3.up * 1.4f, b = a + shape.fwd * shape.length;
                for (int i = 0; i <= 8; i++) FX.Sparks(Vector3.Lerp(a, b, i / 8f), shape.fwd, 2, new Color(1f, 0.6f, 0.55f));
            }
            foreach (var h in Combat.heroes.ToArray())
            {
                if (!h || h.dmg.dead) continue;
                bool inside = shape.Contains(h.transform.position, 0.5f);
                if (h.Invulnerable)
                {
                    if (h.InPerfectWindow && (inside || shape.Contains(h.dodgeStartPos, 0.5f))) h.PerfectDodge();
                    continue;
                }
                if (!inside) continue;
                // 投矛被风障挡住（原稿：青岚的风障能挡远程）
                if (kind == Kind.Javelin && WindWall.Crosses(Flat(transform.position), Flat(h.transform.position))) continue;
                h.dmg.TakeHit(new HitInfo { damage = curDmg * dmgMult * Coop.EnemyDmgMultAt(transform.position), knockback = curHeavy ? 4f : 2f, heavy = curHeavy, dir = Flat(h.transform.position - transform.position).normalized, kind = "minion", attacker = gameObject });
            }
            if (kind == Kind.Duelist && combo < 2 && curSp == Sp.None) { combo++; comboNext = true; }
            if (curSp == Sp.Lunge || curSp == Sp.Charge)
            {
                // 突进：判定在出手瞬间结算，身体 0.18（冲锋 0.3）秒冲到线段末端
                dashFrom = transform.position;
                dashTo = Combat.ClampToArena(shape.origin + shape.fwd * Mathf.Max(0.5f, shape.length - 0.8f), 0.6f);
                dashT0 = Time.time; dashDur = curSp == Sp.Charge ? 0.3f : 0.18f;
                anim.Play("Running_A", 0.03f, 2.2f);
                if (curSp == Sp.Charge) { Feel.I.Sfx("boss_swing", 0.5f, 0.05f, 0.9f); FX.Dust(transform.position, 10, new Color(0.35f, 0.38f, 0.4f, 0.7f)); }
                curSp = Sp.None;
            }
        }

        // ---------------- 招式扩充 ----------------
        bool TrySpecial(Vector3 dir, float d)
        {
            if (Time.time < nextSpecialAt) return false;
            Vector3 me = Flat(transform.position);
            if (brute)
            {
                if (dmg.hp < dmg.maxHp * 0.6f && Time.time >= nextRoarAt) { Roar(); return true; }
                if (d > 5f && d < 11f)
                {
                    transform.rotation = Quaternion.LookRotation(dir);
                    curSp = Sp.Charge; curAnim = "1H_Melee_Attack_Chop"; curWindup = 0.9f; curDmg = 120f; curHeavy = true;
                    shape = AttackShape.Line(me, dir, Mathf.Min(d + 2f, 12f), 2.4f);
                    Begin(new Color(1f, 0.3f, 0.16f, 0.75f));
                    nextSpecialAt = Time.time + Random.Range(7f, 10f);
                    return true;
                }
                return false;
            }
            if (kind == Kind.Claw && d > 3.5f && d < 7f)
            {
                transform.rotation = Quaternion.LookRotation(dir);
                curSp = Sp.Lunge; curAnim = "1H_Melee_Attack_Stab"; curWindup = 0.55f; curDmg = 70f; curHeavy = false;
                shape = AttackShape.Line(me, dir, d + 0.8f, 1.6f);
                Begin(new Color(1f, 0.3f, 0.16f, 0.75f));
                nextSpecialAt = Time.time + Random.Range(6f, 9f);
                return true;
            }
            if (kind == Kind.Duelist)
            {
                if (d < 3.5f && Time.time >= nextParryAt)
                {
                    Enter(S.Parry);
                    anim.Play("Blocking", 0.05f, 1f);
                    dmg.Flash(new Color(0.7f, 0.9f, 1f), 0.8f);
                    FX.Ring(transform.position, 0.3f, 1.6f, 0.35f, new Color(0.7f, 0.9f, 1f, 0.9f), 0.12f);
                    return true;
                }
                if (d > 4f && d < 8.5f)
                {
                    transform.rotation = Quaternion.LookRotation(dir);
                    curSp = Sp.Lunge; curAnim = "Dualwield_Melee_Attack_Stab"; curWindup = 0.45f; curDmg = 75f; curHeavy = false; combo = 0;
                    shape = AttackShape.Line(me, dir, d + 1f, 1.4f);
                    Begin(new Color(1f, 0.3f, 0.16f, 0.75f));
                    nextSpecialAt = Time.time + Random.Range(5f, 8f);
                    return true;
                }
            }
            return false;
        }

        // 重甲潮兵：怒吼（半血以下，14 秒一次）
        void Roar()
        {
            nextRoarAt = Time.time + 14f;
            anim.Play("Taunt", 0.05f, 1.3f);
            Vector3 me = Flat(transform.position);
            dmg.guardUntil = Time.time + 4f; dmg.guardMul = 0.6f;
            foreach (var e in Combat.enemies)
            {
                if (!e || e.dead || e == dmg || Vector3.Distance(Flat(e.transform.position), me) > 6f) continue;
                if (e.guardUntil < Time.time + 4f) { e.guardUntil = Time.time + 4f; e.guardMul = 0.75f; }
                FX.Ring(e.transform.position, 0.3f, 1.4f, 0.35f, new Color(1f, 0.55f, 0.3f, 0.9f), 0.12f);
            }
            FX.Ring(me, 0.4f, 6f, 0.5f, new Color(1f, 0.5f, 0.25f, 0.9f), 0.2f);
            FX.Dust(me, 12, new Color(0.4f, 0.35f, 0.3f, 0.7f));
            Feel.I.Sfx("summon", 0.55f, 0.05f, 0.7f);
            if (Combat.player && Vector3.Distance(Combat.player.transform.position, transform.position) < 18f) Combat.Toast("重甲潮兵怒吼：它和身边的敌人暂时减伤", new Color(1f, 0.6f, 0.35f));
            nextAttackAt = Time.time + 1f;
            Enter(S.Strike);
        }

        // 盾卫：护卫光环（5 米内友军受伤 −30%），脚下一圈淡蓝的环
        void GuardRing()
        {
            guardRing = new GameObject("GuardRing");
            guardRing.transform.SetParent(transform, false);
            guardRing.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            guardRing.AddComponent<MeshFilter>().sharedMesh = Telegraph.Sector(4.75f, 5f, 360f, 64);
            var r = guardRing.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var m = new Material(SliceAssets.I.telegraphShader);
            m.SetColor("_Color", new Color(0.45f, 0.75f, 1f, 0.22f)); m.SetFloat("_Progress", 1f); m.SetFloat("_Sides", 0f);   // 常驻光环只留淡边（Codex 画面审阅 V1）
            r.sharedMaterial = m;
        }

        void TickGuardAura()
        {
            if (dormant || state == S.Dead || Time.time < nextAuraTick) return;
            nextAuraTick = Time.time + 0.25f;
            Vector3 me = Flat(transform.position);
            foreach (var e in Combat.enemies)
            {
                if (!e || e.dead || e == dmg || Vector3.Distance(Flat(e.transform.position), me) > 5f) continue;
                if (e.guardUntil > Time.time + 0.4f && e.guardMul <= 0.7f) continue;   // 已经有更强的减伤（怒吼）
                e.guardUntil = Time.time + 0.4f; e.guardMul = 0.7f;
                if (Random.value < 0.15f) FX.Sparks(e.transform.position + Vector3.up * 1.2f, Vector3.up, 1, new Color(0.55f, 0.8f, 1f));
            }
        }

        // 潮刃剑士：格挡架势里被打，几乎不掉血，并且立刻反击
        float ParryFilter(HitInfo h)
        {
            if (state != S.Parry) return 1f;
            parried = true;
            return 0.1f;
        }

        // 盾卫：正面（朝向 ±50°）来的伤害只吃 20%；失衡、被旋流拉扯、受击硬直时破防
        float ShieldFilter(HitInfo h)
        {
            if (dmg.Broken || Time.time < pullUntil || state == S.Hurt || state == S.Windup) return 1f;
            Vector3 src = -Flat(h.dir);
            if (src.sqrMagnitude < 1e-4f) return 1f;
            if (Vector3.Dot(transform.forward, src.normalized) < 0.64f) return 1f;
            blockedThisHit = true;
            if (Time.time - lastBlockFx > 0.15f)
            {
                if (Time.time - lastBlockFx > 1f) DamageText.Status(transform.position + Vector3.up * (dmg.height + 0.4f), "格挡", new Color(0.65f, 0.85f, 1f), 30);   // 正面打盾：提示绕后
                lastBlockFx = Time.time;
                FX.Sparks(transform.position + Vector3.up * 1.2f + transform.forward * 0.6f, transform.forward, 8, new Color(0.7f, 0.95f, 1f));
                Feel.I.Sfx("hit_armor", 0.5f, 0.1f, 1.2f);
            }
            return 0.2f;
        }

        // ---------------- 潮囊：贴近鼓胀自爆 ----------------
        void TickBloat(Vector3 dir, float d, bool rooted)
        {
            Face(dir, 300f);
            if (d < 2.6f && !rooted)
            {
                curAnim = "Taunt"; curWindup = 1.0f; curDmg = 140f; curHeavy = true;
                shape = AttackShape.Circle(Flat(transform.position), 3.2f);
                Begin(new Color(1f, 0.42f, 0.18f, 0.8f));   // 敌方伤害范围统一红橙
                return;
            }
            if (rooted) { anim.Loop("Idle", 1f); return; }
            float sp = Speed * (Time.time < slowUntil ? 0.6f : 1f);
            anim.Loop("Walking_D_Skeletons", 1f);
            Vector3 step = d > 5f ? Nav.Dir(this, transform.position, target.transform.position) : dir.normalized;
            transform.position = Combat.ClampToArena(transform.position + step * sp * Time.deltaTime, 0.6f);
        }

        void Explode()
        {
            if (tel) tel.Resolve();
            ClearThreat();
            Vector3 c = Flat(transform.position);
            var sh = AttackShape.Circle(c, 3.2f);
            foreach (var h in Combat.heroes.ToArray())
            {
                if (!h || h.dmg.dead) continue;
                bool inside = sh.Contains(h.transform.position, 0.5f);
                if (h.Invulnerable) { if (h.InPerfectWindow && (inside || sh.Contains(h.dodgeStartPos, 0.5f))) h.PerfectDodge(); continue; }
                if (inside) h.dmg.TakeHit(new HitInfo { damage = 140f * dmgMult * Coop.EnemyDmgMultAt(transform.position), knockback = 5f, heavy = true, dir = Flat(h.transform.position - c).normalized, kind = "minion", attacker = gameObject });
            }
            FX.Ring(c, 0.4f, 3.6f, 0.4f, new Color(0.35f, 1f, 0.9f, 1f), 0.3f);
            FX.Tide(c + Vector3.up, Vector3.up, 60);
            TarPool.Spawn(c, 3.2f, 5f);   // 死后留下 5 秒黑潮泥沼
            Feel.I.Sfx("boss_slam", 0.55f, 0.05f, 1.4f);
        }

        // ---------------- 远程和施法（咏唱者、投矛手、祭司）：保持距离 ----------------
        void TickRanged(Vector3 dir, float d, bool rooted)
        {
            Face(dir, 360f);
            if (Caster && Time.time >= nextCastAt && !rooted && d < 18f) { StartCast(); return; }
            if (kind == Kind.Javelin && d <= 16f && Time.time >= nextSpecialAt && Time.time >= nextAttackAt - 0.8f && !rooted)
            {
                PinTrap.Spawn(dmg, Flat(target.transform.position), 45f * dmgMult * Coop.EnemyDmgMultAt(transform.position));
                anim.Play("Throw", 0.05f, 1.2f);
                nextSpecialAt = Time.time + Random.Range(9f, 12f);
                nextAttackAt = Mathf.Max(nextAttackAt, Time.time + 1.6f);
                Enter(S.Strike);
                return;
            }
            if (kind == Kind.Javelin && d <= 17f && Time.time >= nextAttackAt && !rooted)
            {
                curAnim = "Throw"; curWindup = 1.4f; curDmg = 85f; curHeavy = true;
                shape = AttackShape.Line(Flat(transform.position), dir, 18f, 1.6f);
                Begin(new Color(1f, 0.3f, 0.16f, 0.75f));
                return;
            }
            // 被贴脸时也会挠一下（咏唱者、祭司）
            if (Caster && d < reach && Time.time >= nextAttackAt && !rooted) { StartAttack(dir); return; }
            float want = KeepAway;
            Vector3 mv = Vector3.zero;
            bool backing = false;
            if (d < want - 2f) { mv = -dir.normalized; backing = true; }
            else if (d > want + 3f) mv = d > 5f ? Nav.Dir(this, transform.position, target.transform.position) : dir.normalized;
            if (mv != Vector3.zero && !rooted)
            {
                float sp = Speed * 0.8f * (Time.time < slowUntil ? 0.6f : 1f);
                anim.Loop(backing ? "Walking_Backwards" : "Running_A", 1f);
                transform.position = Combat.ClampToArena(transform.position + mv * sp * Time.deltaTime, 0.6f);
            }
            else anim.Loop("Idle_Combat", 1f);
        }

        void StartCast()
        {
            castDmg = 0f;
            castHeal = false;
            if (kind == Kind.Priest)
            {
                // 有友军掉到 70% 血以下就治疗，否则召唤（场上自己召出来的最多 3 只）
                summons.RemoveAll(m => !m || m.dmg.dead);
                foreach (var e in Combat.enemies)
                    if (e && !e.dead && e != dmg && e.team == Team.Enemy && e.hp < e.maxHp * 0.7f && Vector3.Distance(Flat(e.transform.position), Flat(transform.position)) <= 12f) { castHeal = true; break; }
                if (!castHeal && summons.Count >= 3) { nextCastAt = Time.time + 4f; return; }
            }
            castHaste = kind == Kind.Chanter && (castCount++ % 2 == 1);
            castDur = kind == Kind.Chanter ? (castHaste ? 1.8f : 2.5f) : 2f;
            anim.Play(castHeal || kind == Kind.Chanter ? "Spellcasting" : "Spellcast_Summon", 0.15f, 1f);
            Color c = castHaste ? new Color(1f, 0.6f, 0.25f, 0.25f) : kind == Kind.Chanter ? new Color(0.75f, 0.45f, 1f, 0.25f) : new Color(0.4f, 1f, 0.8f, 0.25f);   // 施法范围不伤人，比伤害预警淡
            castTel = AttackShape.Circle(Flat(transform.position), kind == Kind.Chanter ? 10f : 12f).Show(castDur, c);
            if (kind == Kind.Chanter) dmg.priority = true;   // 咏唱者施法时才是优先目标（教「打断」）
            Enter(S.Cast);
        }

        void ResolveCast()
        {
            if (castTel) castTel.Resolve();
            castTel = null;
            Vector3 p = Flat(transform.position);
            if (castHaste)
            {
                // 迅捷祝福：10 米内的残像 6 秒加速（移动 +35%、蓄力 −15%、出手间隔 −35%）
                foreach (var e in Combat.enemies)
                {
                    if (!e || e.dead || Vector3.Distance(Flat(e.transform.position), p) > 10f) continue;
                    var mc = e.GetComponent<MinionController>();
                    if (!mc) continue;
                    mc.hasteUntil = Time.time + 6f;
                    FX.Ring(e.transform.position, 0.3f, 1.6f, 0.4f, new Color(1f, 0.6f, 0.25f, 0.9f), 0.12f);
                }
                FX.Ring(p, 0.5f, 10f, 0.6f, new Color(1f, 0.6f, 0.25f, 0.9f), 0.15f);
            }
            else if (kind == Kind.Chanter)
            {
                // 周围 10 米的友军护盾：各自最大生命的 30%（对 Boss 封顶为咏唱者自己血量的 4 倍）
                foreach (var e in Combat.enemies)
                {
                    if (!e || e.dead || e.team != Team.Enemy || Vector3.Distance(Flat(e.transform.position), p) > 10f) continue;
                    e.shield = Mathf.Max(e.shield, Mathf.Min(e.maxHp * 0.3f, dmg.maxHp * 4f));
                    FX.Ring(e.transform.position, 0.3f, 1.6f, 0.4f, new Color(0.75f, 0.45f, 1f, 0.9f), 0.12f);
                }
                FX.Ring(p, 0.5f, 10f, 0.6f, new Color(0.75f, 0.45f, 1f, 0.9f), 0.15f);
            }
            else if (castHeal)
            {
                foreach (var e in Combat.enemies)
                {
                    if (!e || e.dead || e.team != Team.Enemy || Vector3.Distance(Flat(e.transform.position), p) > 12f) continue;
                    e.hp = Mathf.Min(e.maxHp, e.hp + Mathf.Min(e.maxHp * 0.2f, dmg.maxHp * 6f));
                    FX.Sparks(e.transform.position + Vector3.up, Vector3.up, 8, new Color(0.4f, 1f, 0.8f));
                }
                FX.Ring(p, 0.5f, 12f, 0.6f, new Color(0.4f, 1f, 0.8f, 0.9f), 0.15f);
            }
            else
            {
                for (int i = summons.Count; i < 3; i++)
                {
                    float a = (i / 3f) * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
                    Vector3 sp = Combat.ClampToArena(transform.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 2.5f, 1f);
                    SliceGame.I.SpawnMinion(sp, 1f, 1f, 1f, null);
                    var m = Last;
                    if (!m || m == this) continue;
                    m.home = home; m.leash = leash;
                    summons.Add(m);
                    onSummon?.Invoke(m);
                }
            }
            nextCastAt = Time.time + (kind == Kind.Chanter ? 10f : 12f);
            nextAttackAt = Time.time + 0.8f;
            if (kind == Kind.Chanter) dmg.priority = false;
            Enter(S.Chase);
        }

        void InterruptCast(bool fx)
        {
            if (castTel) castTel.Cancel();
            castTel = null;
            nextCastAt = Time.time + 5f;
            if (kind == Kind.Chanter) dmg.priority = false;
            if (fx)
            {
                DamageText.Status(transform.position + Vector3.up * (dmg.height + 0.5f), "打断!", new Color(1f, 0.92f, 0.7f), 40);
                FX.Ring(transform.position, 0.3f, 2f, 0.35f, new Color(1f, 0.85f, 0.45f, 1f), 0.15f);
                Feel.I.Sfx("stagger_break", 0.45f, 0.05f, 1.4f);
            }
            Enter(S.Hurt);
            anim.Play("Hit_B", 0.03f, 1.2f);
        }

        bool InLeash(HeroBase h) => Vector3.Distance(Flat(h.transform.position), Flat(home)) <= leash;

        void OnHit(HitInfo h, float dealt)
        {
            Wake();
            if (dmg.dead) return;
            if (blockedThisHit) { blockedThisHit = false; anim.Play("Block_Hit", 0.03f, 1.4f); return; }   // 盾卫挡住：不硬直、不击退
            if (parried)
            {
                parried = false;
                Vector3 to = h.attacker ? Flat(h.attacker.transform.position - transform.position) : transform.forward;
                if (to.sqrMagnitude < 0.01f) to = transform.forward;
                transform.rotation = Quaternion.LookRotation(to);
                curSp = Sp.None; combo = 2; curAnim = "Dualwield_Melee_Attack_Chop"; curWindup = 0.28f; curDmg = 110f; curHeavy = true;
                shape = AttackShape.Cone(Flat(transform.position), to, coneR * 1.15f, 130f);
                FX.Sparks(transform.position + Vector3.up * 1.4f + to.normalized * 0.5f, -to.normalized, 14, new Color(0.8f, 0.95f, 1f));
                Feel.I.Sfx("hit_armor", 0.7f, 0.05f, 1.3f);
                nextParryAt = Time.time + Random.Range(9f, 13f);
                Begin(new Color(1f, 0.3f, 0.16f, 0.8f));
                return;
            }
            if (state == S.Cast)
            {
                // 施法有霸体：重击、或者累计打掉 15% 的血才打断
                castDmg += dealt;
                if (h.heavy || castDmg > dmg.maxHp * 0.15f || dmg.Broken) InterruptCast(true);
                return;
            }
            if (kind == Kind.Bloat && state == S.Windup) return;   // 鼓胀中打不断，只能躲或者先打死
            if (tel) tel.Cancel();
            ClearThreat();
            comboNext = false; combo = 0;
            knockVel = Flat(h.dir) * h.knockback * 2.2f / Mathf.Max(1f, dmg.height / 2.1f);
            Enter(S.Hurt);
            anim.Play(h.heavy ? "Hit_B" : "Hit_A", 0.03f, 1.4f);
        }

        void OnDeath()
        {
            if (tel) tel.Cancel();
            if (castTel) castTel.Cancel();
            ClearThreat();
            SetTarget(null);
            Enter(S.Dead);
            if (guardRing) Destroy(guardRing);
            anim.Play("Death_C_Skeletons", 0.05f, 1.2f);
            Feel.I.SfxAt("enemy_death", transform.position, 0.8f);
            FX.Tide(transform.position + Vector3.up, Vector3.up, 35);
            Combat.enemies.Remove(dmg);
            Coop.OnKill(dmg);
            if (hunter) Hunters.Alive = Mathf.Max(0, Hunters.Alive - 1);   // DS-018b
            if (tide) Tide.Alive = Mathf.Max(0, Tide.Alive - 1);           // 割草方向重做：涌浪存活计数
            // DS-018：黑潮王召唤的残像死亡 → 周围英雄回 4% 生命（奖励割草，平抑决战生存曲线）
            if (summoned)
            {
                Vector3 fp = Flat(transform.position);
                foreach (var h in Combat.heroes)
                    if (h && !h.Downed && Vector3.Distance(Flat(h.transform.position), fp) < 7f)
                        h.Heal(h, h.dmg.maxHp * 0.04f);
                FX.Ring(transform.position + Vector3.up * 0.6f, 0.1f, 2.2f, 0.3f, new Color(0.35f, 1f, 0.55f, 0.8f), 0.12f);
            }
            foreach (var c in GetComponents<Collider>()) c.enabled = false;
            if (residualOf) residualOf.ClearPenalty();
            // 潮囊被打死：0.6 秒后原地爆开（有预警，远程打它最安全）
            if (kind == Kind.Bloat && !exploded)
            {
                exploded = true;
                shape = AttackShape.Circle(Flat(transform.position), 3.2f);
                tel = shape.Show(0.6f, new Color(1f, 0.42f, 0.18f, 0.8f));
                threat = new Combat.Threat { owner = dmg, shape = shape, resolveAt = Time.time + 0.6f };
                Combat.threats.Add(threat);
                bloatDeathAt = Time.time + 0.6f;
            }
        }

        void OnDestroy() { if (target) target.chasers = Mathf.Max(0, target.chasers - 1); }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 怒涛 · 潮汐骑：慢蓄力、快出手；黑潮咏唱；召唤残像；架势打空会失衡
    public class BossController : MonoBehaviour
    {
        public enum S { Intro, Chase, Windup, Strike, Charge, Recover, Chant, Summon, Broken, Dead }
        public S state;
        public string bossName = "怒涛 · 潮汐骑";

        public Damageable dmg;
        CharacterAnim anim;
        Transform model;
        BladeTrail trail;
        float t, fightT;

        // 当前招式
        string atk;
        string atkAnim;
        float windup, impactNorm, animLen, recoverT;
        float[] extraImpacts;
        int impactIdx;
        AttackShape shape;
        Telegraph tel;
        Combat.Threat threat;
        void ClearThreat() { if (threat != null) Combat.threats.Remove(threat); threat = null; ClearExtras(); }
        float atkDmg;
        bool atkHeavy;
        bool hitDone;
        Vector3 chargeFrom, chargeDir;
        float nextAttackAt;
        readonly Dictionary<string, float> cds = new Dictionary<string, float>();

        // 咏唱 / 召唤
        public float chantStart, chantDur = 4.6f;
        float nextChantAt = 16f;
        bool summoned70, summoned35;
        Telegraph chantTel;
        float brokenAnimFrozenAt;
        float jitter;
        Vector3 modelBase;

        public bool Chanting => state == S.Chant;

        // ---- 黑潮王（M5，海啸级，设计见 Design/m5_finale.md）：三个阶段；二阶段起召唤潮汐触手、王之咏唱；三阶段心脏暴露、环形潮汐波 ----
        public float dmgMul = 1f;   // 漩涡奖励「周围 Boss 伤害 −10%」
        // ---- 第四批怒涛（variant）：frost 霜潮女巫、thunder 雷锚巨兵、reaper 湮灭刺客；null = 潮汐骑 ----
        public string variant;
        Vector3 blinkTo;
        readonly List<Telegraph> extraTels = new List<Telegraph>();
        readonly List<Combat.Threat> extraThreats = new List<Combat.Threat>();
        readonly List<Vector3> chainAt = new List<Vector3>();
        void ClearExtras()
        {
            foreach (var x in extraTels) if (x) x.Cancel();
            foreach (var x in extraThreats) Combat.threats.Remove(x);
            extraTels.Clear(); extraThreats.Clear();
        }
        public void MakeVariant(string v, string name)
        {
            variant = v;
            bossName = name;
            if (v != "frost") nextChantAt = 9999f;   // 只有女巫会咏唱（冻结咏唱）
        }
        public bool sovereign;
        public int phase = 1;
        // 2026-10-09：8 局全胜零团灭、全队最低平均血量 41–54%，终局不够「紧张但有希望」→ 黑潮王伤害 +25%（决战用时已达标，不加血）
        public const float SovereignDmg = 1.25f;
        Light heartLight;
        float nextTentacleAt;
        float nextSummonAt;   // DS-018：残像召唤节奏
        bool warnedSummon;    // DS-018：召唤提示只说一次
        public void MakeSovereign(Light heart)
        {
            sovereign = true;
            heartLight = heart;
            bossName = "海啸 · 黑潮王";
            nextChantAt = 9999f;            // 一阶段不咏唱
            summoned70 = summoned35 = true; // 不召唤残像，二阶段起召唤触手
        }

        void TickPhase()
        {
            float hpr = dmg.hp / dmg.maxHp;
            if (phase == 1 && hpr < 0.6f)
            {
                phase = 2;
                nextChantAt = fightT + 6f;
                nextTentacleAt = 0f;
                Combat.Toast("黑潮王 · 第二阶段：潮汐触手、王之咏唱（信使声骸可以直接打断）", new Color(0.4f, 0.95f, 0.9f));
                if (RunDirector.I) RunDirector.I.Event("sovereign_phase", "", "2");
            }
            if (phase == 2 && hpr < 0.3f)
            {
                phase = 3;
                nextChantAt = Mathf.Min(nextChantAt, fightT + 10f);
                Combat.Toast("黑潮王 · 第三阶段：心脏暴露，攻击更猛，注意环形潮汐", new Color(1f, 0.5f, 0.45f));
                FX.Ring(transform.position, 1f, 14f, 0.8f, new Color(0.35f, 1f, 0.92f, 1f), 0.25f);
                if (RunDirector.I) RunDirector.I.Event("sovereign_phase", "", "3");
            }
            if (heartLight) heartLight.intensity = Mathf.Lerp(heartLight.intensity, phase == 3 ? 9f + Mathf.Sin(Time.time * 6f) * 2f : 3f, Time.deltaTime * 3f);
            // 二阶段起：场上触手少于 3 根，每 30 秒补齐
            if (phase >= 2 && Time.time >= nextTentacleAt && state == S.Chase)
            {
                nextTentacleAt = Time.time + 30f;
                int alive = TideTentacle.all.Count;
                for (int i = alive; i < 3; i++)
                {
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    Vector3 p = Combat.ClampToArena(home + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(8f, 14f), 1.5f);
                    TideTentacle.Spawn(p, this);
                }
                if (alive < 3) Combat.Toast("黑潮王唤出潮汐触手", new Color(0.4f, 0.95f, 0.9f));
            }
            // DS-018：黑潮王周期性从黑潮边缘召唤残像群四面八方涌来（割草爽点 + 干扰走位 + 喂链式词条）
            if (sovereign && phase >= 2 && state == S.Chase && Time.time >= nextSummonAt)
            {
                nextSummonAt = Time.time + Random.Range(24f, 32f);
                int count = phase == 3 ? 8 : 6;
                SliceGame.spawningSummoned = true;
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        float a = Random.Range(0f, Mathf.PI * 2f);
                        Vector3 p = Combat.ClampToArena(home + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(9f, 15f), 2f);
                        if (SliceGame.I) SliceGame.I.SpawnMinionTripo(p, 0.55f, 1.2f, 1f, null);
                    }
                }
                finally { SliceGame.spawningSummoned = false; }
                if (!warnedSummon) { warnedSummon = true; Combat.Toast("黑潮涌动——残像从四面八方涌来", new Color(0.4f, 0.95f, 0.9f)); }
            }
        }
        public float ChantProgress => state == S.Chant ? Mathf.Clamp01((Time.time - chantStart) / chantDur) : 0f;

        // ---- 仇恨（原稿：优先打最近的；5 秒内某人攻击次数超过当前目标 4 倍或伤害超过 3 倍就转移；目标 6 秒没打它就重选） ----
        HeroBase target;
        float targetLastHitT;
        readonly List<(HeroBase h, float t, float d)> aggro = new List<(HeroBase, float, float)>();
        HeroBase P => target;
        public HeroBase Target => target;
        public System.Action<BossController> onDefeated;   // 开放地图里由节点接管（竞技场里为空，走结算）
        // 开放地图：待命时躺在地上（苏醒动画停在第一帧），有人走近或挨打才起身；只追节点范围内的人，没人就回家
        public bool dormant;
        public Vector3 home;
        public float leash;
        public System.Action onWake;
        public static BossController Last;
        public void Wake()
        {
            if (!dormant) return;
            dormant = false;
            dmg.dormant = false;
            if (anim.current == "Skeletons_Awaken_Standing") { anim.Speed = 0.9f; t = 0f; }   // 躺着的：起身
            else { Enter(S.Chase); nextAttackAt = Time.time + 0.8f; }                         // 游荡中的信使：直接开打
            onWake?.Invoke();
        }

        // 待命姿势：躺在地上（苏醒动画停在第一帧），或者边走边游荡（信使）
        public void HoldPose(bool walking)
        {
            if (walking) anim.Play("Walking_D_Skeletons", 0.1f, 1.1f);
            else anim.Speed = 0f;
        }

        // 待命时被节点带着走（信使游荡）
        public void Patrol(Vector3 to)
        {
            if (!dormant) return;
            Vector3 d = Flat(to - transform.position);
            if (d.sqrMagnitude > 0.0004f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 120f * Time.deltaTime);
            transform.position = new Vector3(to.x, transform.position.y, to.z);
            home = transform.position;
            anim.Loop("Walking_D_Skeletons", 1.1f, 0.3f);
        }
        bool InLeash(HeroBase h) => leash <= 0f || Vector3.Distance(Flat(h.transform.position), Flat(home)) <= leash;
        public float slowUntil;
        float silencedUntil;
        public bool Silenced => Time.time < silencedUntil;
        public bool ChargingWindup => state == S.Windup && atk == "charge";
        public float ChantIn => Mathf.Max(0f, nextChantAt - fightT);

        public void RecordHit(HeroBase h, float d)
        {
            aggro.Add((h, Time.time, d));
            if (h == target) targetLastHitT = Time.time;
        }

        void PickTarget()
        {
            if (target && !target.dmg.dead)
            {
                aggro.RemoveAll(l => Time.time - l.t > 5f);
                int cn = 0; float cd = 0f;
                foreach (var l in aggro) if (l.h == target) { cn++; cd += l.d; }
                foreach (var h in Combat.heroes)
                {
                    if (!h || h == target || h.dmg.dead) continue;
                    int n = 0; float d = 0f;
                    foreach (var l in aggro) if (l.h == h) { n++; d += l.d; }
                    if (!InLeash(h)) continue;
                    if ((n >= 4 && n > cn * 4) || (d > Mathf.Max(1f, cd) * 3f && d > dmg.maxHp * 0.02f)) { target = h; targetLastHitT = Time.time; return; }
                }
                if (Time.time - targetLastHitT <= 6f && InLeash(target)) return;
            }
            var nearest = Combat.NearestHero(transform.position);
            if (nearest && !InLeash(nearest)) nearest = null;
            if (nearest != target) targetLastHitT = Time.time;
            target = nearest;
        }
        static readonly Color Red = new Color(1f, 0.28f, 0.14f, 0.9f);

        public void Setup(GameObject modelGo, Transform axeBase, Transform axeTip)
        {
            model = modelGo.transform;
            modelBase = model.localPosition;
            anim = gameObject.AddComponent<CharacterAnim>();
            anim.Init(modelGo.GetComponentInChildren<Animator>());
            dmg = gameObject.AddComponent<Damageable>();
            dmg.team = Team.Enemy;
            // 队伍人数缩放（SPEC：两人 ×1.7、三人 ×2.4）。架势上限也要提高：
            // 三人削韧速度约为单人的 2.5 倍，实测上限 930 时每 10 秒失衡一次，失衡就不稀罕了
            int team = Mathf.Max(1, Combat.heroes.Count);
            float hpScale = team >= 3 ? 2.4f : team == 2 ? 1.7f : 1f;
            dmg.maxHp = dmg.hp = 38000f * hpScale;
            dmg.staggerMax = team >= 3 ? 1700f : team == 2 ? 1200f : 620f;
            dmg.radius = 1.35f;
            dmg.height = 5.2f;
            dmg.onHit = OnHit;
            dmg.onBreak = OnBreak;
            dmg.onDeath = OnDeath;
            var col = gameObject.AddComponent<CapsuleCollider>();
            col.radius = 1.2f; col.height = 5f; col.center = new Vector3(0, 2.5f, 0);
            trail = gameObject.AddComponent<BladeTrail>();
            trail.baseT = axeBase; trail.tipT = axeTip; trail.color = new Color(0.3f, 0.95f, 0.9f); trail.intensity = 2f; trail.life = 0.2f;
            Combat.enemies.Add(dmg);
            dmg.bossCtl = this;
            dmg.tier = 2;
            Last = this;
            Combat.bosses.Add(this);
            if (!Combat.boss) Combat.boss = this;
            state = S.Intro;
            anim.Play("Skeletons_Awaken_Standing", 0f, 0.9f);
        }

        float Dist => Vector3.Distance(Flat(transform.position), Flat(P.transform.position));
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        Vector3 ToPlayer => Flat(P.transform.position - transform.position).normalized;

        void Face(Vector3 dir, float degPerSec)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), degPerSec * Time.deltaTime);
        }

        void Update()
        {
            if (state == S.Dead) return;
            if (dormant)
            {
                var near = Combat.NearestHero(transform.position);
                if (near && Vector3.Distance(Flat(near.transform.position), Flat(transform.position)) < 14f) Wake();
                else return;
            }
            PickTarget();
            if (sovereign && state != S.Intro) TickPhase();
            if (variant == "thunder" && Random.value < 0.2f) FX.Sparks(transform.position + Vector3.up * Random.Range(1f, 4.5f) + Random.insideUnitSphere * 1.2f, Random.onUnitSphere, 2, new Color(0.75f, 0.6f, 1f));
            if (P == null)
            {
                // 没人可追：回到节点中心
                Vector3 back = Flat(home - transform.position);
                if (leash > 0f && back.magnitude > 3f && state == S.Chase)
                {
                    Face(back, 150f);
                    anim.Loop("Walking_D_Skeletons", 1.25f, 0.25f);
                    transform.position = Combat.ClampToArena(transform.position + transform.forward * 3.6f * Time.deltaTime, dmg.radius);
                }
                else if (state == S.Chase) anim.Loop("Idle_Combat", 1f, 0.25f);
                return;
            }
            t += Time.deltaTime;
            if (state != S.Intro) fightT += Time.deltaTime;
            // 受击抖动（霸体，不硬直，只抖模型）
            jitter = Mathf.Max(0f, jitter - Time.deltaTime * 5f);
            model.localPosition = modelBase + (jitter > 0f ? Random.insideUnitSphere * jitter * 0.12f : Vector3.zero);
            Separate();

            switch (state)
            {
                case S.Intro:
                    if (t > 2.2f) { Feel.I.Sfx("summon", 0.8f, 0f, 0.7f); Enter(S.Chase); }
                    break;
                case S.Chase: TickChase(); break;
                case S.Windup: TickWindup(); break;
                case S.Strike: TickStrike(); break;
                case S.Charge: TickCharge(); break;
                case S.Recover:
                    if (t > recoverT) Enter(S.Chase);
                    else if (t > recoverT * 0.5f) Face(ToPlayer, 60f);
                    break;
                case S.Chant: TickChant(); break;
                case S.Summon: TickSummon(); break;
                case S.Broken: TickBroken(); break;
            }
        }

        void Enter(S s)
        {
            state = s;
            t = 0f;
            trail.emitting = false;
            if (s == S.Chase) anim.Play("Idle_Combat", 0.25f, 1f);
        }

        void Separate()
        {
            float min = dmg.radius + 0.7f;
            foreach (var h in Combat.heroes)
            {
                if (!h || h.Downed) continue;
                Vector3 to = Flat(h.transform.position - transform.position);
                float d = to.magnitude;
                if (d < min && d > 0.01f) h.Push(to.normalized * (min - d));
            }
        }

        // ---------------- 追击与选招 ----------------
        void TickChase()
        {
            Face(ToPlayer, 150f);
            float d = Dist;
            if (Silenced) { anim.Loop("Idle_Combat", 1f, 0.25f); return; }   // 静默：几秒内不能出招
            if (fightT >= nextChantAt && d < 26f) { StartChant(); return; }
            if (sovereign) { SovereignChoose(d); return; }
            if (variant != null) { VariantChoose(d); return; }
            float hpr = dmg.hp / dmg.maxHp;
            if (!summoned70 && hpr < 0.7f) { summoned70 = true; StartSummon(); return; }
            if (!summoned35 && hpr < 0.35f) { summoned35 = true; StartSummon(); return; }

            if (Time.time >= nextAttackAt)
            {
                var opts = new List<(string id, float w)>();
                if (d <= 5.6f && Ready("sweep")) opts.Add(("sweep", 3f));
                if (d <= 4.6f && Ready("spin")) opts.Add(("spin", 2f));
                if (d <= 10f && d > 3f && Ready("slam")) opts.Add(("slam", 2.2f));
                if (d >= 7f && d <= 18f && Ready("charge")) opts.Add(("charge", 3f));
                if (opts.Count > 0)
                {
                    float sum = 0f; foreach (var o in opts) sum += o.w;
                    float r = Random.value * sum;
                    foreach (var o in opts) { r -= o.w; if (r <= 0f) { StartAttack(o.id); return; } }
                    StartAttack(opts[opts.Count - 1].id);
                    return;
                }
            }
            if (d > 3.6f)
            {
                bool far = d > 11f;
                anim.Loop(far ? "Running_C" : "Walking_D_Skeletons", far ? 1.1f : 1.25f, 0.25f);
                float sp = (far ? 6.2f : 3.6f) * (Time.time < slowUntil ? 0.6f : 1f);
                transform.position = Combat.ClampToArena(transform.position + transform.forward * sp * Time.deltaTime, dmg.radius);
            }
            else anim.Loop("Idle_Combat", 1f, 0.25f);
        }

        bool Ready(string id) => !cds.TryGetValue(id, out var c) || Time.time >= c;

        void VariantChoose(float d)
        {
            if (Time.time >= nextAttackAt)
            {
                var opts = new List<(string id, float w)>();
                switch (variant)
                {
                    case "frost":
                        if (d <= 16f && Ready("icelance")) opts.Add(("icelance", 3f));
                        if (d <= 14f && Ready("frostfield")) opts.Add(("frostfield", 2.5f));
                        if (d <= 5.6f && Ready("sweep")) opts.Add(("sweep", 2f));
                        break;
                    case "thunder":
                        if (d <= 8f && Ready("quake")) opts.Add(("quake", 3f));
                        if (d <= 18f && Ready("chain")) opts.Add(("chain", 2.5f));
                        if (d <= 5.6f && Ready("sweep")) opts.Add(("sweep", 2f));
                        if (d >= 7f && d <= 18f && Ready("charge")) opts.Add(("charge", 2f));
                        break;
                    case "reaper":
                        if (Ready("blink") && Combat.heroes.Count > 0) opts.Add(("blink", 3f));
                        if (d <= 4.8f && Ready("flurry")) opts.Add(("flurry", 3f));
                        if (d <= 5.6f && Ready("sweep")) opts.Add(("sweep", 1.5f));
                        break;
                }
                if (opts.Count > 0)
                {
                    float sum = 0f; foreach (var o in opts) sum += o.w;
                    float r = Random.value * sum;
                    foreach (var o in opts) { r -= o.w; if (r <= 0f) { StartAttack(o.id); return; } }
                    StartAttack(opts[opts.Count - 1].id);
                    return;
                }
            }
            // 走位：女巫保持 8 米左右，刺客更快
            if (variant == "frost" && d < 5f)
            {
                anim.Loop("Walking_Backwards", 1f, 0.25f);
                transform.position = Combat.ClampToArena(transform.position - transform.forward * 3f * Time.deltaTime, dmg.radius);
                return;
            }
            float stop = variant == "frost" ? 9f : 3.6f;
            if (d > stop)
            {
                bool far = d > 11f;
                anim.Loop(far ? "Running_C" : "Walking_D_Skeletons", far ? 1.1f : 1.25f, 0.25f);
                float sp = (far ? 6.2f : 3.6f) * (variant == "reaper" ? 1.25f : 1f) * (Time.time < slowUntil ? 0.6f : 1f);
                transform.position = Combat.ClampToArena(transform.position + transform.forward * sp * Time.deltaTime, dmg.radius);
            }
            else anim.Loop("Idle_Combat", 1f, 0.25f);
        }

        void SovereignChoose(float d)
        {
            if (Time.time >= nextAttackAt)
            {
                var opts = new List<(string id, float w)>();
                if (d <= 9f && Ready("thrust3")) opts.Add(("thrust3", 3f));
                if (d <= 7f && Ready("tidesweep")) opts.Add(("tidesweep", 3f));
                if (d >= 7f && d <= 18f && Ready("charge")) opts.Add(("charge", 2.5f));
                if (phase >= 2 && d <= 10f && d > 3f && Ready("slam")) opts.Add(("slam", 2f));
                if (phase >= 3 && d <= 12f && Ready("tidewave")) opts.Add(("tidewave", 4f));
                if (opts.Count > 0)
                {
                    float sum = 0f; foreach (var o in opts) sum += o.w;
                    float r = Random.value * sum;
                    foreach (var o in opts) { r -= o.w; if (r <= 0f) { StartAttack(o.id); return; } }
                    StartAttack(opts[opts.Count - 1].id);
                    return;
                }
            }
            if (d > 4f)
            {
                bool far = d > 11f;
                anim.Loop(far ? "Running_C" : "Walking_D_Skeletons", far ? 1f : 1.15f, 0.25f);
                float sp = (far ? 5.6f : 3.4f) * (Time.time < slowUntil ? 0.6f : 1f);
                transform.position = Combat.ClampToArena(transform.position + transform.forward * sp * Time.deltaTime, dmg.radius);
            }
            else anim.Loop("Idle_Combat", 1f, 0.25f);
        }

        void StartAttack(string id)
        {
            atk = id;
            impactIdx = 0; hitDone = false; extraImpacts = null;
            Vector3 fwd = ToPlayer;
            transform.rotation = Quaternion.LookRotation(fwd);
            Vector3 pos = Flat(transform.position);
            switch (id)
            {
                case "sweep":
                    atkAnim = "2H_Melee_Attack_Slice"; windup = 0.85f; impactNorm = 0.36f; recoverT = 0.75f; atkDmg = 230f; atkHeavy = false;
                    shape = AttackShape.Cone(pos, fwd, 6.4f, 150f); cds[id] = Time.time + 2.6f; break;
                case "spin":
                    atkAnim = "2H_Melee_Attack_Spin"; windup = 1.2f; impactNorm = 0.3f; extraImpacts = new[] { 0.5f }; recoverT = 0.9f; atkDmg = 190f; atkHeavy = false;
                    shape = AttackShape.Circle(pos, 5.8f); cds[id] = Time.time + 7f; break;
                case "slam":
                    atkAnim = "2H_Melee_Attack_Chop"; windup = 1.1f; impactNorm = 0.5f; recoverT = 1.0f; atkDmg = 290f; atkHeavy = true;
                    Vector3 tp = Flat(P.transform.position);
                    Vector3 off = Vector3.ClampMagnitude(tp - pos, 8.5f);
                    if (off.magnitude < 3.2f) off = fwd * 3.2f;
                    shape = AttackShape.Circle(pos + off, 3.5f); cds[id] = Time.time + 4.5f; break;
                case "icelance":  // 霜潮女巫：冰枪，直线
                    atkAnim = "Spellcast_Shoot"; windup = 1.0f; impactNorm = 0.45f; recoverT = 0.8f; atkDmg = 220f; atkHeavy = false;
                    shape = AttackShape.Line(pos, fwd, 16f, 1.8f); cds[id] = Time.time + 3f; break;
                case "frostfield":// 霜潮女巫：在目标脚下冻出 4 米冰面（8 秒，减速 40%）
                    atkAnim = "Spellcasting"; windup = 1.2f; impactNorm = 0.5f; recoverT = 0.8f; atkDmg = 150f; atkHeavy = false;
                    shape = AttackShape.Circle(Flat(P.transform.position), 4f); cds[id] = Time.time + 9f; break;
                case "quake":     // 雷锚巨兵：重锤砸地，5.5 米
                    atkAnim = "2H_Melee_Attack_Chop"; windup = 1.3f; impactNorm = 0.5f; recoverT = 1.1f; atkDmg = 300f; atkHeavy = true;
                    shape = AttackShape.Circle(pos + fwd * 2f, 5.5f); cds[id] = Time.time + 6f; break;
                case "chain":     // 雷锚巨兵：连锁闪电，每人脚下一个圈；挨打的人 4 米内的队友再挨一次（逼散开）
                    atkAnim = "Spellcast_Raise"; windup = 1.3f; impactNorm = 0.5f; recoverT = 0.9f; atkDmg = 160f; atkHeavy = false;
                    chainAt.Clear();
                    foreach (var h in Combat.heroes) if (h && !h.Downed) chainAt.Add(Flat(h.transform.position));
                    shape = AttackShape.Circle(chainAt.Count > 0 ? chainAt[0] : pos, 2.2f); cds[id] = Time.time + 10f; break;
                case "blink":     // 湮灭刺客：闪现到离它最远的人（优先辅助）背后斩击
                {
                    HeroBase vic = null; float far = -1f;
                    foreach (var h in Combat.heroes)
                    {
                        if (!h || h.Downed) continue;
                        float dd = Vector3.Distance(h.transform.position, transform.position) + (h.Id == HeroId.Chengyin ? 6f : 0f);
                        if (dd > far) { far = dd; vic = h; }
                    }
                    if (!vic) vic = P;
                    Vector3 vp = Flat(vic.transform.position), away = Flat(vic.transform.position - transform.position).normalized;
                    blinkTo = Combat.ClampToArena(vp + away * 1.8f, dmg.radius);
                    atkAnim = "Dualwield_Melee_Attack_Chop"; windup = 0.9f; impactNorm = 0.4f; recoverT = 0.9f; atkDmg = 260f; atkHeavy = true;
                    shape = AttackShape.Cone(Flat(blinkTo), -away, 3.6f, 120f); cds[id] = Time.time + 7f;
                    FX.Sparks(transform.position + Vector3.up * 2f, Vector3.up, 20, new Color(1f, 0.25f, 0.45f));
                    break;
                }
                case "flurry":    // 湮灭刺客：三连斩
                    atkAnim = "Dualwield_Melee_Attack_Slice"; windup = 0.55f; impactNorm = 0.3f; extraImpacts = new[] { 0.48f, 0.66f }; recoverT = 0.7f; atkDmg = 120f; atkHeavy = false;
                    shape = AttackShape.Cone(pos, fwd, 4.2f, 100f); cds[id] = Time.time + 3.5f; break;
                case "thrust3":   // 三叉戟三连刺：一条直线连续三下
                    atkAnim = "2H_Melee_Attack_Stab"; windup = 0.9f; impactNorm = 0.3f; extraImpacts = new[] { 0.48f, 0.66f }; recoverT = 0.8f; atkDmg = 200f; atkHeavy = false;
                    shape = AttackShape.Line(pos, fwd, 9.5f, 2.6f); cds[id] = Time.time + 3.2f; break;
                case "tidesweep": // 潮涌横扫：大扇形
                    atkAnim = "2H_Melee_Attack_Slice"; windup = 1.0f; impactNorm = 0.36f; recoverT = 0.9f; atkDmg = 270f; atkHeavy = true;
                    shape = AttackShape.Cone(pos, fwd, 7.5f, 200f); cds[id] = Time.time + 4f; break;
                case "tidewave":  // 环形潮汐：贴身 3.5 米内安全，往外到 13 米都挨打
                    atkAnim = "2H_Melee_Attack_Chop"; windup = 1.6f; impactNorm = 0.5f; recoverT = 1.1f; atkDmg = 320f; atkHeavy = true;
                    shape = AttackShape.Ring(pos, 3.5f, 13f); cds[id] = Time.time + 9f; break;
                case "charge":
                    atkAnim = "Block"; windup = 0.95f; impactNorm = 0f; recoverT = 1.0f; atkDmg = 240f; atkHeavy = true;
                    chargeFrom = pos; chargeDir = fwd;
                    shape = AttackShape.Line(pos, fwd, 14f, 3f); cds[id] = Time.time + 8f; break;
            }
            if (sovereign) atkDmg *= SovereignDmg * (phase >= 3 ? 1.2f : 1f);   // 心脏暴露：伤害 +20%
            animLen = anim.Length(atkAnim);
            // 关键：蓄力阶段放慢动画，让"出手帧"正好落在预警结束；出手后恢复正常速度
            float windSpeed = impactNorm > 0f ? impactNorm * animLen / windup : 1f;
            anim.Play(atkAnim, 0.12f, windSpeed);
            tel = shape.Show(windup, Red);
            ClearThreat();
            threat = new Combat.Threat { owner = dmg, shape = shape, resolveAt = Time.time + windup };
            Combat.threats.Add(threat);
            Feel.I.Sfx("telegraph", 0.5f, 0.05f);
            if (id == "chain")
                for (int i = 1; i < chainAt.Count; i++)
                {
                    var sh = AttackShape.Circle(chainAt[i], 2.2f);
                    extraTels.Add(sh.Show(windup, new Color(0.75f, 0.6f, 1f, 0.85f)));
                    var th = new Combat.Threat { owner = dmg, shape = sh, resolveAt = Time.time + windup };
                    extraThreats.Add(th); Combat.threats.Add(th);
                }
            Enter(S.Windup);
        }

        void TickWindup()
        {
            if (atk == "charge") Face(chargeDir, 0f);
            if (t >= windup - 0.12f && !trail.emitting && atk != "charge") { trail.emitting = true; Feel.I.Sfx("boss_swing", 0.9f, 0.05f); }
            if (t < windup) return;
            if (atk == "charge")
            {
                tel.Resolve();
                if (threat != null) threat.resolveAt = Time.time + 0.15f;
                anim.Play("Running_C", 0.05f, 2.4f);
                Feel.I.Sfx("boss_swing", 1f, 0.05f, 0.8f);
                Enter(S.Charge);
                return;
            }
            anim.Speed = 1.15f;
            if (atk == "blink")
            {
                FX.Tide(transform.position + Vector3.up, Vector3.up, 30);
                transform.position = new Vector3(blinkTo.x, transform.position.y, blinkTo.z);
                transform.rotation = Quaternion.LookRotation(shape.fwd);
                FX.Sparks(transform.position + Vector3.up * 2f, Vector3.up, 30, new Color(1f, 0.25f, 0.45f));
            }
            Impact();
            Enter(S.Strike);
        }

        void Impact()
        {
            if (atk == "chain") { ChainImpact(); return; }
            bool resolvedHit = ResolveOnHeroes(atkDmg, atkHeavy);
            if (atk == "frostfield") FrostZone.Spawn(shape.origin, shape.radius, 8f);
            if (tel) tel.Resolve();
            Vector3 c = shape.origin;
            switch (atk)
            {
                case "slam":
                    Feel.I.Sfx("boss_slam", 1f, 0.05f);
                    Feel.I.Shake(0.6f);
                    FX.Ring(c, 0.5f, 4.6f, 0.4f, new Color(0.35f, 1f, 0.92f, 1f), 0.2f);
                    FX.Dust(c, 16, new Color(0.25f, 0.22f, 0.3f, 0.85f));
                    FX.Tide(c, Vector3.up, 40);
                    break;
                case "spin":
                    Feel.I.Shake(0.3f);
                    FX.Ring(transform.position, 1f, 6f, 0.35f, new Color(0.35f, 1f, 0.92f, 0.8f), 0.12f);
                    break;
                case "tidewave":
                    Feel.I.Sfx("boss_slam", 1f, 0.05f, 0.8f);
                    Feel.I.Shake(0.8f);
                    FX.Ring(c, 3.5f, 13f, 0.6f, new Color(0.35f, 1f, 0.92f, 1f), 0.5f);
                    FX.Tide(c, Vector3.up, 80);
                    break;
                default:
                    Feel.I.Shake(0.3f);
                    break;
            }
            if (resolvedHit) Feel.I.Shake(0.2f);
        }

        // 连锁闪电：每个圈结算一次；被劈中的人 4 米内的队友再挨一次（站得越散越安全）
        void ChainImpact()
        {
            var hit = new List<HeroBase>();
            var shapes = new List<AttackShape>();
            foreach (var c in chainAt) shapes.Add(AttackShape.Circle(c, 2.2f));
            foreach (var h in Combat.heroes.ToArray())
            {
                if (!h || h.dmg.dead) continue;
                bool inside = false, was = false;
                foreach (var s in shapes) { inside |= s.Contains(h.transform.position, 0.5f); was |= s.Contains(h.dodgeStartPos, 0.5f); }
                if (h.Invulnerable) { if (h.InPerfectWindow && (inside || was)) h.PerfectDodge(); continue; }
                if (!inside) continue;
                h.dmg.TakeHit(new HitInfo { damage = atkDmg * dmgMul * Coop.EnemyDmgMultAt(transform.position), knockback = 2f, dir = Vector3.up, heavy = false, kind = "boss", attacker = gameObject });
                hit.Add(h);
            }
            foreach (var a in hit)
                foreach (var b in Combat.heroes)
                {
                    if (!b || b == a || b.dmg.dead || b.Invulnerable) continue;
                    if (Vector3.Distance(Flat(a.transform.position), Flat(b.transform.position)) > 4f) continue;
                    b.dmg.TakeHit(new HitInfo { damage = 120f * dmgMul * Coop.EnemyDmgMultAt(transform.position), knockback = 1f, dir = Vector3.up, heavy = false, kind = "boss", attacker = gameObject });
                    Vector3 p0 = a.transform.position + Vector3.up * 1.2f, p1 = b.transform.position + Vector3.up * 1.2f;
                    for (int i = 0; i <= 5; i++) FX.Sparks(Vector3.Lerp(p0, p1, i / 5f), Vector3.up, 2, new Color(0.75f, 0.6f, 1f));
                }
            foreach (var c in chainAt)
            {
                for (int i = 0; i < 6; i++) FX.Sparks(c + Vector3.up * (6f - i), Vector3.down, 3, new Color(0.75f, 0.6f, 1f));
                FX.Ring(c, 0.2f, 2.4f, 0.3f, new Color(0.75f, 0.6f, 1f, 1f), 0.15f);
            }
            if (tel) tel.Resolve();
            foreach (var x in extraTels) if (x) x.Resolve();
            extraTels.Clear();
            ClearThreat();
            Feel.I.Sfx("ult_hit", 0.7f, 0.05f, 1.4f);
            Feel.I.Shake(0.4f);
        }

        // 判定全队：在范围内就挨打；如果是闪避中，且闪避开始的位置在范围内、时机够准，算完美闪避
        bool ResolveOnHeroes(float damage, bool heavy)
        {
            bool any = false;
            foreach (var h in Combat.heroes.ToArray())
            {
                if (!h || h.dmg.dead) continue;
                bool inside = shape.Contains(h.transform.position, 0.5f);
                bool wasInside = shape.Contains(h.dodgeStartPos, 0.5f);
                if (h.Invulnerable)
                {
                    if (h.InPerfectWindow && (wasInside || inside)) h.PerfectDodge();
                    continue;
                }
                if (!inside) continue;
                Vector3 dir = Flat(h.transform.position - transform.position).normalized;
                h.dmg.TakeHit(new HitInfo { damage = damage * dmgMul * Coop.EnemyDmgMultAt(transform.position), stagger = 0f, knockback = heavy ? 5f : 3f, dir = dir, heavy = heavy, kind = "boss", attacker = gameObject });
                any = true;
            }
            return any;
        }

        void TickStrike()
        {
            float speed = 1.15f;
            float n = impactNorm + t * speed / animLen;
            if (extraImpacts != null && impactIdx < extraImpacts.Length && n >= extraImpacts[impactIdx])
            {
                impactIdx++;
                Feel.I.Sfx("boss_swing", 0.9f, 0.05f);
                Impact();
            }
            trail.emitting = n < (extraImpacts != null ? extraImpacts[extraImpacts.Length - 1] + 0.08f : impactNorm + 0.1f);
            if (extraImpacts != null && impactIdx < extraImpacts.Length && threat != null)
                threat.resolveAt = Time.time + (extraImpacts[impactIdx] - n) * animLen / speed;
            else ClearThreat();
            if (n >= 0.9f || t > 2.5f)
            {
                nextAttackAt = Time.time + Random.Range(0.3f, 0.9f);
                Enter(S.Recover);
                anim.Play("Idle_Combat", 0.3f, 1f);
            }
        }

        void TickCharge()
        {
            const float dur = 0.5f, len = 14f;
            float k = Mathf.Clamp01(t / dur);
            Vector3 dest = Combat.ClampToArena(chargeFrom + chargeDir * len * k, dmg.radius);
            // 风障挡冲锋：撞墙停下，架势大幅下降
            var wall = WindWall.Crosses(Flat(transform.position) + chargeDir * dmg.radius, Flat(dest) + chargeDir * dmg.radius);
            if (wall)
            {
                ClearThreat();
                Feel.I.Sfx("hit_armor", 1f, 0.05f, 0.7f);
                Feel.I.Shake(0.5f);
                Color wc = wall.owner ? wall.owner.Color : new Color(0.3f, 0.79f, 0.65f);
                FX.Ring(transform.position + chargeDir * dmg.radius, 0.3f, 3.5f, 0.4f, new Color(wc.r, wc.g, wc.b, 1f), 0.18f);
                FX.Dust(transform.position + chargeDir * dmg.radius, 14, new Color(0.6f, 0.75f, 0.7f, 0.7f));
                Combat.Toast("风障挡住了冲锋", wc);
                if (wall.owner) wall.owner.DealHit(dmg, 0.8f, 120f, 0f, -chargeDir, true, "wall", 0f, false);
                if (state == S.Charge)
                {
                    anim.Play("Hit_B", 0.05f, 1f);
                    nextAttackAt = Time.time + Random.Range(0.8f, 1.4f);
                    Enter(S.Recover);
                    recoverT = 1.2f;
                }
                return;
            }
            transform.position = new Vector3(dest.x, transform.position.y, dest.z);
            if (!hitDone)
            {
                var near = AttackShape.Circle(Flat(transform.position), dmg.radius + 0.6f);
                foreach (var h in Combat.heroes.ToArray())
                {
                    if (!h || h.dmg.dead) continue;
                    if (!near.Contains(h.transform.position, 0.5f)) continue;
                    if (h.Invulnerable) { if (h.InPerfectWindow) h.PerfectDodge(); continue; }
                    hitDone = true;
                    h.dmg.TakeHit(new HitInfo { damage = atkDmg * dmgMul * Coop.EnemyDmgMultAt(transform.position), knockback = 7f, dir = chargeDir, heavy = true, kind = "boss", attacker = gameObject });
                    Feel.I.Sfx("hit_heavy", 0.8f, 0.05f, 0.7f);
                }
            }
            if (threat != null) { threat.shape = AttackShape.Circle(Flat(transform.position + chargeDir * 1.2f), dmg.radius + 1.2f); threat.resolveAt = Time.time + 0.12f; }
            if (Random.value < 0.5f) FX.Dust(transform.position, 1, new Color(0.25f, 0.22f, 0.3f, 0.6f));
            if (k >= 1f)
            {
                ClearThreat();
                Feel.I.Shake(0.3f);
                anim.Play("2H_Melee_Attack_Stab", 0.1f, 1.2f, 0.3f);
                nextAttackAt = Time.time + Random.Range(0.4f, 1f);
                Enter(S.Recover);
            }
        }

        // ---------------- 黑潮咏唱 ----------------
        void StartChant()
        {
            Enter(S.Chant);
            chantStart = Time.time;
            anim.Play("Spellcasting", 0.2f, 0.8f);
            chantTel = AttackShape.Circle(Flat(transform.position), 40f).Show(chantDur, new Color(0.3f, 0.95f, 0.9f, 0.3f));
            dmg.staggerTakenMult = 1.7f; // 咏唱时更容易被打空架势：鼓励主动进攻
            Feel.I.Loop("chant_loop", 0.8f);
            Feel.I.chantLevel = 1f;
            Combat.Toast(sovereign ? "王之咏唱 · 用信使声骸（R）直接打断，或打空架势" : variant == "frost" ? "冻结咏唱 · 打空架势或用静默打断，否则全队冻住" : "黑潮咏唱 · 打空架势或用静默打断", new Color(0.4f, 0.95f, 0.9f));
        }

        void TickChant()
        {
            if (Random.value < 0.5f) FX.Tide(transform.position + Vector3.up * Random.Range(0.5f, 4.5f) + Random.insideUnitSphere * 1.5f, Vector3.up, 1);
            if (Time.time - chantStart < chantDur) return;
            EndChant();
            // 爆发：全场伤害，闪避无效
            Feel.I.Sfx("chant_blast", 1f, 0f);
            Feel.I.Shake(1f);
            Feel.I.PulseHit(1f);
            FX.Ring(transform.position, 1f, 30f, 0.9f, new Color(0.65f, 0.3f, 1f, 1f), 0.08f);
            FX.Tide(transform.position + Vector3.up * 2f, Vector3.up, 120);
            // 打全队；侵蚀 ≥ 45% 时全队超频 +15
            foreach (var h in Combat.heroes.ToArray())
            {
                if (!h || h.dmg.dead) continue;
                h.dmg.TakeHit(new HitInfo { damage = (sovereign ? 760f * SovereignDmg : 560f) * dmgMul * Coop.EnemyDmgMultAt(transform.position), knockback = 6f, dir = Flat(h.transform.position - transform.position).normalized, heavy = true, kind = "chant", attacker = gameObject });
                if (!h.dmg.dead) h.AddOverclock("chant", sovereign ? 25f : Coop.OcChantGain);
                if (variant == "frost" && !h.dmg.dead) { h.AddMod("speed", "freeze", -1f, 1.5f); FX.Ring(h.transform.position, 0.3f, 1.6f, 1.5f, new Color(0.7f, 0.9f, 1f, 0.9f), 0.3f); }
            }
            Coop.chantsHit++;
            nextChantAt = fightT + (sovereign && phase >= 3 ? 18f : 26f);
            nextAttackAt = Time.time + 1.2f;
            anim.Play("Idle_Combat", 0.3f, 1f);
            Enter(S.Recover);
            recoverT = 1.2f;
        }

        void EndChant()
        {
            Feel.I.StopLoop();
            Feel.I.chantLevel = 0f;
            dmg.staggerTakenMult = 1f;
            if (chantTel) chantTel.Cancel();
        }

        // ---------------- 召唤残像 ----------------
        void StartSummon()
        {
            Enter(S.Summon);
            anim.Play("Spellcast_Summon", 0.2f, 2.2f);
            Feel.I.Sfx("summon", 1f, 0f);
            Combat.Toast("潮汐骑唤出残像", new Color(0.4f, 0.95f, 0.9f));
        }

        bool spawnedThisCast;
        void TickSummon()
        {
            if (!spawnedThisCast && t > 1.1f)
            {
                spawnedThisCast = true;
                for (int i = 0; i < 3; i++)
                {
                    float a = (i / 3f) * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
                    Vector3 p = Combat.ClampToArena(transform.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 4.5f, 1f);
                    SliceGame.I.SpawnMinion(p);
                }
            }
            if (t > 2.1f) { spawnedThisCast = false; nextAttackAt = Time.time + 0.6f; Enter(S.Chase); }
        }

        // ---------------- 受击 / 失衡 / 死亡 ----------------
        void OnHit(HitInfo h, float dealt)
        {
            Wake();
            jitter = Mathf.Max(jitter, h.heavy ? 1f : 0.5f);
        }

        void OnBreak()
        {
            bool wasChanting = state == S.Chant;
            if (wasChanting) { EndChant(); Coop.chantsCut++; }
            Coop.breaks++;
            if (tel) tel.Cancel();
            ClearThreat();
            trail.emitting = false;
            Enter(S.Broken);
            anim.Play("Hit_B", 0.05f, 0.7f);
            brokenAnimFrozenAt = 0.55f;
            Feel.I.Sfx("stagger_break", 1f, 0f);
            Feel.I.HitStop(0.22f);
            Feel.I.Shake(0.8f);
            Feel.I.PulseHit(1f);
            FX.Ring(transform.position, 1f, 7f, 0.45f, new Color(1f, 0.7f, 0.3f, 1f), 0.15f);
            FX.Sparks(transform.position + Vector3.up * 3f, Vector3.up, 60, new Color(1f, 0.8f, 0.4f));
            Combat.Toast(wasChanting ? "咏唱被打断 · 失衡！伤害 +50%" : "失衡！伤害 +50%", new Color(1f, 0.75f, 0.35f));
            if (wasChanting) nextChantAt = fightT + (sovereign && phase >= 3 ? 16f : 22f);
        }

        // 静默：打断当前的蓄力招式和咏唱，几秒内不能出招（不会失衡）
        public void Silence(float dur, string by)
        {
            if (state == S.Dead) return;
            DamageText.Status(transform.position + Vector3.up * (dmg.height + 0.6f), "打断!", new Color(1f, 0.92f, 0.7f), 48);
            silencedUntil = Mathf.Max(silencedUntil, Time.time + dur);
            bool chanting = state == S.Chant;
            if (chanting)
            {
                EndChant();
                nextChantAt = fightT + 22f;
                Coop.chantsCut++;
                Combat.Toast($"{by}打断了黑潮咏唱", new Color(1f, 0.85f, 0.45f));
            }
            if (chanting || state == S.Windup || state == S.Charge)
            {
                if (tel) tel.Cancel();
                ClearThreat();
                trail.emitting = false;
                anim.Play("Hit_A", 0.05f, 0.8f);
                Enter(S.Recover);
                recoverT = dur;
                nextAttackAt = silencedUntil;
            }
        }

        void TickBroken()
        {
            if (brokenAnimFrozenAt > 0f && t >= brokenAnimFrozenAt) { anim.Speed = 0.04f; brokenAnimFrozenAt = 0f; }
            if (Random.value < 0.25f) FX.Sparks(transform.position + Vector3.up * Random.Range(2f, 4.5f), Vector3.up, 1, new Color(1f, 0.75f, 0.35f));
            float left = dmg.brokenUntil - Time.time;
            if (left <= 0.9f && anim.current != "Skeletons_Awaken_Standing") anim.Play("Skeletons_Awaken_Standing", 0.15f, 1.5f);
            if (left <= 0f) { nextAttackAt = Time.time + 0.4f; Enter(S.Chase); }
        }

        void OnDeath()
        {
            if (state == S.Chant) EndChant();
            if (tel) tel.Cancel();
            ClearThreat();
            state = S.Dead;
            trail.emitting = false;
            anim.Play("Death_C_Skeletons", 0.05f, 0.8f);
            Feel.I.HitStop(0.45f, 0.01f);
            Feel.I.Shake(1f);
            Feel.I.PulseHit(1f);
            Feel.I.Sfx("ult_hit", 1f, 0f, 0.7f);
            Feel.I.Sfx("enemy_death", 1f, 0f, 0.6f);
            FX.Tide(transform.position + Vector3.up * 2.5f, Vector3.up, 150);
            FX.Ring(transform.position, 1f, 12f, 0.8f, new Color(0.35f, 1f, 0.92f, 1f), 0.1f);
            Combat.enemies.Remove(dmg);
            Combat.bosses.Remove(this);
            if (sovereign) TideTentacle.KillAll();
            Coop.OnKill(dmg);
            if (onDefeated != null) onDefeated(this); else SliceGame.I.OnBossDefeated();
        }
    }
}

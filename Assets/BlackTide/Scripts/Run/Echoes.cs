using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 声骸（2026-10-09 按原稿重做）。原稿：「击败残像有几率掉落声骸」（轻波 10%、巨浪 30%，Boss 更高）；
    // 「声骸技能可对应本体梦魇声骸技能」——打赢谁，就能召唤它打出它的招；个人只带 1 个；
    // 黑潮信使必掉 3 个强化声骸，固定分配到个人，对黑潮王有奇效。
    // M2 时简化成了所有人一样的 R 范围爆发（没有掉落、没有「这是谁的声骸」）。现在：
    //   · 敌人死亡按档位概率掉落它自己的声骸（地上一块发光的声骸结晶，写着名字）；
    //   · 每人装 1 个，走近按 F 替换（旧的留在地上，可以换回来）；没装时 R 还是原来的通用爆发；
    //   · 按 R：身边召唤这个敌人的青色幽影，播放它自己的出招动作，打出它的招牌招式（判定改成打敌人）；
    //   · 幽影直接复制敌人死时的模型（去掉战斗组件，保留骨骼、动画、武器），所以每种敌人自动有自己的样子和动作；
    //   · 共鸣爆炸、飓风场两张卡对声骸幽影也生效（在幽影的位置吸怪、爆炸）。
    public class EchoDef
    {
        public string id, name, desc, anim;
        public int tier;              // 0 轻波 / 1 巨浪 / 2 怒涛 / 3 信使
        public float cd;
        public static readonly string[] TierName = { "轻波", "巨浪", "怒涛", "信使" };
        public static readonly Color[] TierCol = { new Color(0.55f, 1f, 0.65f), new Color(0.45f, 0.72f, 1f), new Color(1f, 0.78f, 0.35f), new Color(1f, 0.45f, 0.85f) };
        public Color Col => TierCol[Mathf.Clamp(tier, 0, 3)];
    }

    public static class Echoes
    {
        static EchoDef D(string id, string name, int tier, float cd, string anim, string desc) => new EchoDef { id = id, name = name, tier = tier, cd = cd, anim = anim, desc = desc };
        // 2026-10-09 第二版（用户定位）：声骸有冷却、召唤幽影脱手释放，定位是**机制**（控制、领域、治疗、增益、标记、格挡），不是出伤效率；
        // 快而高伤的招留给武器的武器技能。每个声骸取材于这个敌人的「机制招」（见 MinionController 头注释的招式表）。
        public static readonly EchoDef[] All =
        {
            D("claw", "残像", 0, 12f, "1H_Melee_Attack_Stab", "牵制：幽影立刻缠住 9 米内最近的 3 个杂兵，定身 2 秒"),
            D("shield", "残像·盾卫", 0, 16f, "Blocking", "护卫：幽影在原地举盾 6 秒，5 米内的队友受到的伤害 −30%"),
            D("bloat", "残像·潮囊", 0, 14f, "Walking_D_Skeletons", "泥沼：幽影滚到敌群里爆开，留下 5 秒黑潮泥沼，敌人在里面减速"),
            D("chanter", "残像·咏唱者", 0, 16f, "Spellcasting", "群体护盾：12 米内队友获得 15% 生命的护盾"),
            D("ray", "残像·刺鳐", 0, 12f, "", "标记：幽影俯冲过一条直线，被撞到的敌人 6 秒内受到的伤害 +20%"),
            D("brute", "巨浪·重甲潮兵", 1, 20f, "Taunt", "怒吼：10 米内队友 5 秒受伤 −35%，身边的敌人被震退"),
            D("duelist", "巨浪·潮刃剑士", 1, 15f, "Blocking", "格挡：3 秒内你受到的下一次攻击被完全挡下，幽影立刻反击（3 倍）"),
            D("javelin", "巨浪·珊瑚投矛手", 1, 16f, "Throw", "钉地矛阵：14 米内最近的 3 个敌人脚下 0.45 秒后刺出珊瑚矛，杂兵定身 2 秒"),
            D("priest", "巨浪·唤潮祭司", 1, 22f, "Spellcast_Summon", "唤潮：12 米内队友回复 18% 生命"),
            D("jelly", "巨浪·浮游水母", 1, 16f, "", "电流牵引：把 9 米内的杂兵拉到幽影身边，再放电减速 3 秒"),
            D("knight", "怒涛·潮汐骑", 2, 24f, "2H_Melee_Attack_Spin", "战吼：全队 8 秒伤害 +20%，回复 15 共鸣能量"),
            D("frost", "怒涛·霜潮女巫", 2, 22f, "Spellcast_Shoot", "冰冻领域：前方 6 米范围 5 秒，敌人先冻住 1.5 秒、之后持续减速"),
            D("thunder", "怒涛·雷锚巨兵", 2, 22f, "2H_Melee_Attack_Chop", "震地：周围 7 米的杂兵定身 2.5 秒；Boss 积累大量架势（打空时打断）"),
            D("reaper", "怒涛·湮灭收割者", 2, 20f, "Dualwield_Melee_Attack_Slice", "影遁：你 1.5 秒无敌、3 秒移动速度 +40%，原地的幽影把 8 米内的杂兵牵引过去，2.6 秒后爆开"),
            D("messenger", "黑潮信使", 3, 11f, "Spellcast_Raise", "强化声骸：5.5 米爆发（4.5 倍），打断 Boss 读条；对黑潮王伤害 ×2.5 并打断任何招式"),
        };
        public static EchoDef Def(string id) { foreach (var d in All) if (d.id == id) return d; return null; }

        // ---------------- 掉落 ----------------
        // 原稿：轻波 10%、巨浪 30%；Boss「更高概率」——取 60%。倒地队友留下的遗留残像不掉
        static readonly float[] DropChance = { 0.10f, 0.30f, 0.60f };
        static Transform templates;
        static readonly Dictionary<string, GameObject> tpl = new Dictionary<string, GameObject>();

        public static string KindOf(Damageable e)
        {
            if (!e) return null;
            if (e.bossCtl)
            {
                var b = e.bossCtl;
                if (b.sovereign || e.gameObject.name.Contains("信使")) return null;
                return b.variant == "frost" ? "frost" : b.variant == "thunder" ? "thunder" : b.variant == "reaper" ? "reaper" : "knight";
            }
            var mc = e.GetComponent<MinionController>();
            if (mc)
            {
                if (e.gameObject.name.Contains("重甲")) return "brute";
                switch (mc.kind)
                {
                    case MinionController.Kind.Shield: return "shield";
                    case MinionController.Kind.Bloat: return "bloat";
                    case MinionController.Kind.Chanter: return "chanter";
                    case MinionController.Kind.Duelist: return "duelist";
                    case MinionController.Kind.Javelin: return "javelin";
                    case MinionController.Kind.Priest: return "priest";
                    default: return "claw";
                }
            }
            if (e.GetComponent<RayController>()) return "ray";
            if (e.GetComponent<DriftJelly>()) return "jelly";
            return null;
        }

        // Coop.OnKill 调（只在开放地图）
        public static void OnKill(Damageable e)
        {
            if (!RunDirector.I || !e) return;
            var mc = e.GetComponent<MinionController>();
            if (mc && mc.IsResidual) return;
            if (e.tide) return;      // 涌浪不掉声骸：怪潮化之后按原掉落率会把声骸系统淹没
            string id = KindOf(e);
            var d = Def(id);
            if (d == null) return;
            if (!tpl.TryGetValue(id, out var t) || !t) MakeTemplate(id, e.gameObject);   // 第一次见到这种敌人死：记下它的样子
            if (Random.value >= DropChance[Mathf.Clamp(d.tier, 0, 2)]) return;
            Vector3 p = e.transform.position; p.y = 0f;
            EchoPickup.Spawn(d, p + new Vector3(Random.Range(-0.8f, 0.8f), 0f, Random.Range(-0.8f, 0.8f)));
            if (RunDirector.I) RunDirector.I.Event("echo_drop", "", d.id);
        }

        // 信使声骸的样子：信使节点打完时从 Boss 身上记
        public static void Remember(string id, GameObject src) { if (src && (!tpl.TryGetValue(id, out var t) || !t)) MakeTemplate(id, src); }

        static void MakeTemplate(string id, GameObject src)
        {
            if (!templates) { var g = new GameObject("EchoTemplates"); g.SetActive(false); templates = g.transform; }
            var c = Object.Instantiate(src, templates);   // 父物体没激活：复制体的脚本不会跑 Awake
            c.name = "Echo_" + id;
            c.transform.localPosition = Vector3.zero; c.transform.localRotation = Quaternion.identity; c.transform.localScale = Vector3.one;
            // 只留骨骼、动画、网格、武器跟随
            foreach (var mb in c.GetComponentsInChildren<MonoBehaviour>(true)) if (!(mb is WeaponFollow)) Object.DestroyImmediate(mb);
            foreach (var ps in c.GetComponentsInChildren<ParticleSystem>(true)) Object.DestroyImmediate(ps);
            foreach (var comp in c.GetComponentsInChildren<Component>(true))
            {
                if (!comp || comp is Transform || comp is Animator || comp is MeshFilter || comp is WeaponFollow) continue;
                if (comp is MeshRenderer || comp is SkinnedMeshRenderer) continue;
                Object.DestroyImmediate(comp);
            }
            tpl[id] = c;
        }

        // ---------------- 装备 ----------------
        public static void Equip(HeroBase h, EchoDef d, bool dropOld)
        {
            if (!h) return;
            if (dropOld && h.echo != null && h.echo != d) EchoPickup.Spawn(h.echo, h.transform.position + h.transform.right * 1.2f, true);
            h.echo = d;
            h.echoEmpowered = d != null && d.id == "messenger";   // 信使声骸的特殊效果（打断、对黑潮王）沿用原来的强化爆发
            if (RunDirector.I && d != null) RunDirector.I.Event("echo_equip", "", $"{h.HeroName} {d.id}");
        }

        // ---------------- 施放（HeroBase.TickEcho 出手那一刻调） ----------------
        static Material phantomBase;
        public static void Cast(HeroBase h, EchoDef d)
        {
            Vector3 hp = h.transform.position; hp.y = 0f;
            var target = Nearest(hp, 14f);
            Vector3 dir = target ? Flat(target.transform.position - hp).normalized : Flat(h.transform.forward).normalized;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Vector3 at = d.tier >= 2 ? hp + side * 2.6f - dir * 0.8f : hp + side * 1.2f + dir * 0.6f;     // 幽影站在身侧（大型的站远一点，别挡住角色）
            var ph = Phantom(d, at, dir);
            ph.Setup(h, d, dir, target);
        }

        static EchoPhantom Phantom(EchoDef d, Vector3 at, Vector3 dir)
        {
            GameObject go;
            if (tpl.TryGetValue(d.id, out var t) && t) { go = Object.Instantiate(t); go.SetActive(true); }
            else go = new GameObject("Echo_" + d.id);   // 还没见过这种敌人（比如信使声骸先到手）：没有身体，只有特效
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(dir));
            go.transform.localScale = Vector3.one * (d.tier >= 2 ? 0.85f : 1f);
            if (!phantomBase)
            {
                phantomBase = new Material(Shader.Find("BlackTide/Rim"));
                phantomBase.SetColor("_Color", new Color(0.45f, 1f, 0.95f));
                phantomBase.SetFloat("_Power", 1.7f);
            }
            var m = new Material(phantomBase);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var ms = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < ms.Length; i++) ms[i] = m;
                r.sharedMaterials = ms;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var ph = go.AddComponent<EchoPhantom>();
            ph.mat = m;
            return ph;
        }

        // ---------------- 工具 ----------------
        public static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        public static Damageable Nearest(Vector3 p, float r)
        {
            Damageable best = null; float bd = r;
            foreach (var e in Combat.enemies)
            {
                if (!e || e.dead || e.dormant) continue;
                float d = Vector3.Distance(Flat(e.transform.position), Flat(p));
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }
        public static int Hit(HeroBase h, AttackShape s, float mult, float stagger, float knock, bool heavy, System.Action<Damageable> extra = null)
        {
            int n = 0;
            foreach (var e in Combat.enemies.ToArray())
            {
                if (!e || e.dead || !s.Contains(e.transform.position, e.radius)) continue;
                Vector3 kd = Flat(e.transform.position - s.origin); if (kd.sqrMagnitude < 0.01f) kd = s.fwd;
                if (h.DealHit(e, mult, stagger, knock, kd.normalized, heavy, "echo") > 0f) n++;
                extra?.Invoke(e);
            }
            return n;
        }
    }

    // 声骸幽影：按时间轴出招，结束后淡出
    public class EchoPhantom : MonoBehaviour
    {
        public Material mat;
        HeroBase h; EchoDef d; Vector3 dir; Damageable target;
        Animator an;
        float t, life = 1.2f;
        readonly List<(float at, System.Action act)> steps = new List<(float, System.Action)>();
        Vector3 moveFrom, moveTo; float moveT0 = -1f, moveDur;

        float bright = 1.5f, fill = 0.4f;   // 轮廓亮度、身体填充：大型幽影面积大，在泛光下容易糊成一团白，压低
        public void Setup(HeroBase hero, EchoDef def, Vector3 aim, Damageable tgt)
        {
            h = hero; d = def; dir = aim; target = tgt;
            if (d.tier >= 2) { bright = 1.1f; fill = 0.22f; }
            an = GetComponentInChildren<Animator>();
            if (an) { an.Rebind(); an.speed = 1f; if (!string.IsNullOrEmpty(d.anim)) an.CrossFadeInFixedTime(d.anim, 0.05f); }
            FX.Ring(transform.position, 0.3f, 2.2f, 0.35f, new Color(0.45f, 1f, 0.95f, 0.9f), 0.12f);
            FX.Tide(transform.position + Vector3.up, Vector3.up, 14);
            if (h.IsControlled) Feel.I.Sfx("summon", 0.5f, 0.05f, 1.3f);
            Plan();
            // 卡牌联动：飓风场（出手前把 8 米内的杂兵拉到幽影身边）
            if (h.rogue.Has("vortex"))
            {
                Vector3 c = Echoes.Flat(transform.position + dir * 1.5f);
                foreach (var e in Combat.enemies)
                {
                    if (!e || e.dead || e.dormant || e.bossCtl) continue;
                    Vector3 dd = Echoes.Flat(e.transform.position) - c;
                    if (dd.magnitude > 8f) continue;
                    e.transform.position = Combat.ClampToArena(c + dd.normalized * Mathf.Min(dd.magnitude, 1.8f), e.radius);
                }
                FX.Ring(c, 8f, 1f, 0.35f, new Color(0.6f, 1f, 0.9f, 0.8f), 0.2f);
            }
        }

        void At(float time, System.Action a) => steps.Add((time, a));
        void Move(Vector3 to, float dur) { moveFrom = transform.position; moveTo = to; moveT0 = t; moveDur = dur; }
        Vector3 P => Echoes.Flat(transform.position);

        void Impact(float r, Color c) { FX.Ring(P, 0.3f, r, 0.35f, c, 0.2f); FX.Sparks(P + Vector3.up, Vector3.up, 16, c); if (h.IsControlled) { Feel.I.Shake(0.25f); Feel.I.HitStop(0.04f); } }
        void Blast()
        {
            // 卡牌联动：共鸣爆炸
            if (!h.rogue.Has("echo_blast")) return;
            float r = Rogue.EchoBlastRange(h);   // DS-019b：Lv4/5 扩大
            Echoes.Hit(h, AttackShape.Circle(P, r), Rogue.EchoBlastMult(h), 20f, 2f, false);
            // DS-027：协奏=定向扇面声波（朝释放方向推开），不再是一个青圆环
            Vector3 dir = P - h.transform.position; dir.y = 0f;
            // The hit is circular at P. Radiating waveform spokes share that center and range.
            FX.SoundPulse(P, r, new Color(0.45f, 0.95f, 0.95f));
            FX.Sparks(P + Vector3.up * 0.5f, dir.normalized, 10, new Color(0.6f, 1f, 1f));
        }

        // 声骸的控制（定身、减速、牵引）同时让敌人露出破绽：2.5 秒内受到的伤害 +15%（2026-10-09 验证后加：机制声骸既控场也帮队伍出伤）
        static void Mob(Damageable e, System.Action<IMob> a)
        {
            var m = e ? e.GetComponent<IMob>() : null;
            if (m == null) return;
            a(m);
            if (e.vulnUntil < Time.time + 2.5f) { e.vulnUntil = Time.time + 2.5f; e.vulnMul = Mathf.Max(e.vulnMul, 1.15f); }
        }
        List<Damageable> NearestN(Vector3 c, float r, int n)
        {
            var l = new List<Damageable>();
            foreach (var e in Combat.enemies) if (e && !e.dead && !e.dormant && Vector3.Distance(Echoes.Flat(e.transform.position), c) <= r) l.Add(e);
            l.Sort((a, b) => Vector3.Distance(Echoes.Flat(a.transform.position), c).CompareTo(Vector3.Distance(Echoes.Flat(b.transform.position), c)));
            if (l.Count > n) l.RemoveRange(n, l.Count - n);
            return l;
        }

        void Plan()
        {
            Color teal = new Color(0.45f, 1f, 0.95f, 1f);
            Vector3 hp0 = Echoes.Flat(h.transform.position);
            switch (d.id)
            {
                case "claw":   // 牵制：缠住最近 3 个敌人
                    At(0.3f, () =>
                    {
                        foreach (var e in NearestN(P, 9f, 3))
                        {
                            Mob(e, m => m.Root(2f));
                            h.DealHit(e, 0.8f, 20f, 0f, Echoes.Flat(e.transform.position - P).normalized, false, "echo");
                            Rogue.Bolt(transform.position + Vector3.up * 1.2f, e.Center, teal);
                            FX.Ring(e.transform.position, 0.2f, 1.2f, 2f, new Color(0.45f, 1f, 0.95f, 0.7f), 0.12f);
                        }
                        Impact(2f, teal); Blast();
                    });
                    break;
                case "shield":   // 护卫：原地举盾 6 秒
                    life = 6.2f;
                    At(0.1f, () => FX.Ring(P, 5f, 4.9f, 6f, new Color(0.45f, 0.75f, 1f, 0.6f), 0.05f));
                    for (float tt = 0.1f; tt < 6f; tt += 0.25f)
                        At(tt, () => { foreach (var x in Coop.Alive()) if (Vector3.Distance(Echoes.Flat(x.transform.position), P) <= 5f) x.AddMod("taken", "echo_guard", -0.3f, 0.35f); });
                    At(0.2f, Blast);
                    break;
                case "bloat":   // 泥沼
                    life = 1.4f;
                    At(0.05f, () => { var tg = target ? Echoes.Flat(target.transform.position) : P + dir * 5f; Move(tg, 0.6f); });
                    At(0.7f, () => { Echoes.Hit(h, AttackShape.Circle(P, 3.6f), 1.5f, 40f, 4f, true); FX.Tide(P + Vector3.up, Vector3.up, 50); Impact(3.8f, teal); TarPool.Spawn(P, 4f, 5f, true); if (h.IsControlled) Feel.I.Sfx("boss_slam", 0.5f, 0.05f, 1.4f); Blast(); foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false; });
                    break;
                case "chanter":   // 群体护盾
                    life = 1.4f;
                    At(0.9f, () =>
                    {
                        foreach (var x in Coop.Alive()) if (Vector3.Distance(Echoes.Flat(x.transform.position), P) <= 12f) h.Heal(x, x.dmg.maxHp * 0.15f, true, 8f);
                        FX.Ring(P, 0.4f, 12f, 0.6f, new Color(0.75f, 0.45f, 1f, 0.9f), 0.12f); Blast();
                        if (h.IsControlled) Feel.I.Sfx("chant_blast", 0.4f, 0.05f, 1.3f);
                    });
                    break;
                case "ray":   // 标记：俯冲过一条直线
                    transform.position += Vector3.up * 1.2f;
                    At(0.15f, () => Move(transform.position + dir * 12f, 0.35f));
                    At(0.3f, () =>
                    {
                        var s = AttackShape.Line(hp0, dir, 13f, 2.2f);
                        Echoes.Hit(h, s, 0.8f, 10f, 1f, false, e => { e.vulnUntil = Time.time + 6f; e.vulnMul = 1.2f; FX.Ring(e.transform.position, 0.2f, 1.3f, 0.5f, new Color(1f, 0.85f, 0.4f, 0.9f), 0.12f); });
                        if (h.IsControlled) Feel.I.Sfx("dash", 0.4f, 0.1f, 1.4f);
                    });
                    At(0.6f, Blast);
                    break;
                case "brute":   // 怒吼
                    life = 1.4f;
                    At(0.5f, () =>
                    {
                        foreach (var x in Coop.Alive()) if (Vector3.Distance(Echoes.Flat(x.transform.position), P) <= 10f) { x.AddMod("taken", "echo_roar", -0.35f, 5f); FX.Ring(x.transform.position, 0.3f, 1.6f, 0.4f, new Color(1f, 0.6f, 0.3f, 0.9f), 0.12f); }
                        Echoes.Hit(h, AttackShape.Circle(P, 5f), 0.5f, 60f, 7f, true);
                        FX.Ring(P, 0.4f, 10f, 0.6f, new Color(1f, 0.55f, 0.25f, 0.9f), 0.15f); FX.Dust(P, 12, new Color(0.4f, 0.35f, 0.3f, 0.7f));
                        if (h.IsControlled) { Feel.I.Sfx("summon", 0.6f, 0.05f, 0.7f); Feel.I.Shake(0.3f); }
                        Blast();
                    });
                    break;
                case "duelist":   // 格挡：下一次挨打被挡下并反击
                    life = 3.2f;
                    transform.position = h.transform.position - dir * 1.2f;
                    At(0.05f, () =>
                    {
                        h.echoParryUntil = Time.time + 3f;
                        h.onEchoParry = hit =>
                        {
                            Vector3 c = Echoes.Flat(h.transform.position);
                            Echoes.Hit(h, AttackShape.Circle(c, 3.5f), 3f, 80f, 4f, true);
                            if (an) an.CrossFadeInFixedTime("Dualwield_Melee_Attack_Chop", 0.03f);
                            FX.SlashArc(h.transform.position + Vector3.up * 1.2f, Quaternion.LookRotation(dir), 1.5f, 3.6f, 300f, 0.15f, teal);
                            FX.Sparks(h.transform.position + Vector3.up * 1.3f, Vector3.up, 20, new Color(0.8f, 0.95f, 1f));
                            if (h.IsControlled) { Feel.I.Sfx("perfect_dodge", 0.9f, 0.02f, 1.1f); Feel.I.HitStop(0.08f); DamageText.Status(h.transform.position + Vector3.up * 2.8f, "格挡反击", new Color(0.8f, 0.95f, 1f), 44); }
                            life = Mathf.Min(life, t + 0.5f);
                        };
                        FX.Ring(h.transform.position, 0.3f, 1.5f, 0.3f, new Color(0.8f, 0.95f, 1f, 0.9f), 0.12f);
                    });
                    break;
                case "javelin":   // 钉地矛阵：反过来钉敌人
                    life = 1.4f;
                    At(0.4f, () =>
                    {
                        foreach (var e in NearestN(P, 14f, 3))
                        {
                            var c = Echoes.Flat(e.transform.position);
                            FX.Ring(c, 1.6f, 1.5f, 0.45f, new Color(1f, 0.6f, 0.45f, 0.8f), 0.3f);
                            var ee = e;
                            At(t + 0.45f, () =>
                            {
                                if (!ee || ee.dead) return;
                                Mob(ee, m => m.Root(2f));
                                h.DealHit(ee, 1.2f, 30f, 0f, Vector3.up, false, "echo");
                                for (int k = 0; k < 4; k++) FX.Sparks(Echoes.Flat(ee.transform.position) + Random.insideUnitSphere * 0.8f, Vector3.up, 4, new Color(1f, 0.6f, 0.5f));
                            });
                        }
                        if (h.IsControlled) Feel.I.Sfx("boss_swing", 0.45f, 0.05f, 1.5f);
                    });
                    At(1f, Blast);
                    break;
                case "priest":   // 唤潮
                    life = 1.4f;
                    At(0.8f, () =>
                    {
                        foreach (var x in Coop.Alive()) if (Vector3.Distance(Echoes.Flat(x.transform.position), P) <= 12f) h.Heal(x, x.dmg.maxHp * 0.18f);
                        FX.Ring(P, 0.4f, 12f, 0.6f, new Color(0.4f, 1f, 0.75f, 0.9f), 0.12f);
                        FX.Tide(P + Vector3.up, Vector3.up, 30); Blast();
                    });
                    break;
                case "jelly":   // 电流牵引
                    transform.position += Vector3.up * 1.6f;
                    life = 1.8f;
                    At(0.2f, () =>
                    {
                        foreach (var e in NearestN(P, 9f, 12)) if (!e.bossCtl) { Mob(e, m => m.Pull(P, Time.time + 1.1f)); Rogue.Bolt(transform.position, e.Center, new Color(0.6f, 0.95f, 1f)); }
                        if (h.IsControlled) Feel.I.Sfx("hit_armor", 0.5f, 0.1f, 1.8f);
                    });
                    At(1.3f, () =>
                    {
                        Echoes.Hit(h, AttackShape.Circle(P, 3.5f), 1.2f, 30f, 0f, false, e => Mob(e, m => m.Slow(Time.time + 3f)));
                        FX.Ring(P, 0.4f, 3.8f, 0.4f, new Color(0.6f, 0.95f, 1f, 1f), 0.3f); Blast();
                    });
                    break;
                case "knight":   // 战吼
                    life = 1.6f;
                    At(0.5f, () =>
                    {
                        foreach (var x in Coop.Alive()) { x.AddMod("dmg", "echo_warcry", 0.2f, 8f); x.Gain(15f, 0f); FX.Ring(x.transform.position, 0.3f, 1.8f, 0.4f, new Color(1f, 0.8f, 0.4f, 0.9f), 0.12f); }
                        FX.Ring(P, 0.4f, 8f, 0.6f, new Color(1f, 0.8f, 0.4f, 0.9f), 0.15f);
                        if (h.IsControlled) { Feel.I.Sfx("summon", 0.7f, 0.05f, 0.85f); Feel.I.PulseHit(0.4f); Combat.Toast("潮汐战吼：全队伤害 +20%", new Color(1f, 0.8f, 0.4f)); }
                        Blast();
                    });
                    break;
                case "frost":   // 冰冻领域
                    life = 1.6f;
                    At(0.45f, () =>
                    {
                        Vector3 c = target ? Echoes.Flat(target.transform.position) : P + dir * 6f;
                        if (Vector3.Distance(c, P) > 10f) c = P + (c - P).normalized * 10f;
                        Echoes.Hit(h, AttackShape.Circle(c, 6f), 1f, 30f, 0f, false, e => Mob(e, m => m.Root(1.5f)));
                        TarPool.Spawn(c, 6f, 5f, true, new Color(0.6f, 0.9f, 1f, 0.45f));
                        Vector3 a = transform.position + Vector3.up * 1.6f;
                        for (int k = 0; k <= 8; k++) FX.Sparks(Vector3.Lerp(a, c + Vector3.up, k / 8f), dir, 2, new Color(0.75f, 0.95f, 1f));
                        if (h.IsControlled) Feel.I.Sfx("ult_hit", 0.45f, 0.05f, 1.6f);
                        Blast();
                    });
                    break;
                case "thunder":   // 震地定身
                    life = 1.6f;
                    At(0.6f, () =>
                    {
                        Echoes.Hit(h, AttackShape.Circle(P, 7f), 1.5f, 120f, 2f, true, e => Mob(e, m => m.Root(2.5f)));
                        for (int k = 0; k < 6; k++) { float ang = k * 60f; Vector3 q = P + Quaternion.Euler(0, ang, 0) * Vector3.forward * Random.Range(2f, 6f); Rogue.Bolt(q + Vector3.up * 8f, q, new Color(0.8f, 0.7f, 1f)); }
                        Impact(7.2f, new Color(0.8f, 0.7f, 1f, 1f));
                        if (h.IsControlled) { Feel.I.Sfx("boss_slam", 0.7f, 0.05f, 1.1f); Feel.I.Shake(0.5f); }
                        Blast();
                    });
                    break;
                case "reaper":   // 影遁：无敌 + 加速，幽影留在原地当诱饵
                    life = 3f;
                    transform.position = h.transform.position;
                    At(0.05f, () =>
                    {
                        h.SetInvulnerable(1.5f);
                        h.AddMod("speed", "echo_shadow", 0.4f, 3f);
                        FX.Tide(h.transform.position + Vector3.up, Vector3.up, 24);
                        foreach (var e in NearestN(P, 8f, 6)) Mob(e, m => m.Pull(P, Time.time + 0.8f));
                        if (h.IsControlled) { Feel.I.Sfx("dodge", 0.8f, 0.05f, 0.8f); Combat.Toast("影遁", new Color(0.85f, 0.6f, 1f)); }
                    });
                    At(2.6f, () => { Echoes.Hit(h, AttackShape.Circle(P, 3f), 1f, 30f, 2f, false); FX.Tide(P + Vector3.up, Vector3.up, 30); Blast(); });
                    break;
                default:   // 信使：效果在 HeroBase 的强化爆发里结算，幽影只是陪着出现
                    life = 1f;
                    break;
            }
        }

        void OnDestroy() { if (mat) Destroy(mat); }   // 每个幽影自己的材质（共享模板和基础材质不在这里释放）

        void Update()
        {
            t += Time.deltaTime;
            if (moveT0 >= 0f)
            {
                float k = Mathf.Clamp01((t - moveT0) / Mathf.Max(0.01f, moveDur));
                var p = Vector3.Lerp(moveFrom, moveTo, 1f - (1f - k) * (1f - k)); p.y = transform.position.y;
                transform.position = p;
                if (k >= 1f) moveT0 = -1f;
            }
            for (int i = 0; i < steps.Count; i++)
                if (t >= steps[i].at) { var a = steps[i].act; steps.RemoveAt(i); i--; if (h) a(); }
            // 淡入 0.12 秒、淡出 0.3 秒
            float vis = Mathf.Clamp01(t / 0.12f) * Mathf.Clamp01((life - t) / 0.3f);
            if (mat) { mat.SetFloat("_Intensity", bright * vis); mat.SetFloat("_Flash", fill * vis); }
            if (t >= life && steps.Count == 0) Destroy(gameObject);
        }
    }

    // 地上的声骸：发光的声骸结晶 + 名字，走近按 F 装备（没装声骸时自动装），AI 路过时按档位自动换
    public class EchoPickup : MonoBehaviour
    {
        public EchoDef d;
        float born, nextHint, nextPulse;
        Transform crystal;
        static Font font;

        public static void Spawn(EchoDef d, Vector3 p, bool dropped = false)
        {
            var go = new GameObject("声骸·" + d.name);
            go.transform.position = new Vector3(p.x, 0f, p.z);
            var pk = go.AddComponent<EchoPickup>();
            pk.d = d; pk.born = Time.time + (dropped ? 1.5f : 0.3f);   // 刚换下来的不会马上又被自己捡回去
            FX.LootColumn(go.transform.position, d.Col);   // CL-003：V26 掉落光柱——怪潮里地上掉东西必须一眼能看见
            var prop = SliceAssets.I.MapProp("28_echo_crystal");
            Color c = d.Col;
            if (prop != null && prop.model)
            {
                var holder = new GameObject("Crystal").transform;
                holder.SetParent(go.transform, false);
                holder.localPosition = Vector3.up * 0.9f;
                ArenaBuilder.Prop(holder, prop, "28_echo_crystal", go.transform.position + Vector3.up * 0.9f, Quaternion.identity, d.tier >= 2 ? 1.7f : 1.3f, false, c, c * 3f, 0.6f, out _);
                pk.crystal = holder;
            }
            var l = new GameObject("Glow").AddComponent<Light>();
            l.transform.SetParent(go.transform, false); l.transform.localPosition = Vector3.up * 1.2f;
            l.type = LightType.Point; l.color = c; l.intensity = 3.5f; l.range = 5f; l.shadows = LightShadows.None;
            // 名字
            if (!font) font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" }, 48);
            var tgo = new GameObject("Label");
            tgo.transform.SetParent(go.transform, false);
            tgo.transform.localPosition = Vector3.up * 2.3f;
            var tm = tgo.AddComponent<TextMesh>();
            tm.font = font; tm.fontSize = 48; tm.characterSize = 0.06f; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            int dot = d.name.IndexOf('·');
            tm.text = $"{(dot >= 0 ? d.name.Substring(dot + 1) : d.name)}\n<size=30>{EchoDef.TierName[Mathf.Clamp(d.tier, 0, 3)]}声骸</size>"; tm.color = c;
            tgo.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            FX.Ring(go.transform.position, 0.2f, 2f, 0.5f, new Color(c.r, c.g, c.b, 0.9f), 0.15f);
            if (Combat.player && Vector3.Distance(Combat.player.transform.position, go.transform.position) < 25f && !dropped)
                Combat.Toast($"掉落{EchoDef.TierName[Mathf.Clamp(d.tier, 0, 3)]}声骸：{d.name}（走近按 {GameInput.Hint("F","A")} 装备）", c);
        }

        void Update()
        {
            if (crystal) { crystal.Rotate(0f, 60f * Time.deltaTime, 0f); crystal.localPosition = Vector3.up * (0.9f + Mathf.Sin(Time.time * 2f) * 0.12f); }
            if (Time.time >= nextPulse) { nextPulse = Time.time + 1.4f; FX.Ring(transform.position, 0.3f, 1.8f, 0.6f, new Color(d.Col.r, d.Col.g, d.Col.b, 0.7f), 0.12f); }   // 定时脉冲，远处也看得到
            var cam = Camera.main;
            var label = transform.Find("Label");
            if (cam && label) label.rotation = cam.transform.rotation;
            if (Time.time < born) return;
            foreach (var h in Combat.heroes)
            {
                if (!h || h.Downed) continue;
                float dist = Vector3.Distance(Echoes.Flat(h.transform.position), Echoes.Flat(transform.position));
                if (h.UsesBrain)
                {
                    // AI：路过（3 米内）时，没有声骸或者这个档位更高就换
                    if (dist < 3f && (h.echo == null || (d.tier > h.echo.tier && h.echo.id != "messenger"))) { Take(h); return; }
                    continue;
                }
                if (dist > 2f) continue;
                if (h.echo == null) { Take(h); return; }
                if (Time.time >= nextHint) { nextHint = Time.time + 3f; Combat.Toast($"按 {GameInput.Hint("F","A")} 换上 {d.name}（当前：{h.echo.name}）：{d.desc}", d.Col); }
                if ((!GameInput.CombatBlocked && GameInput.Down(GameAction.Interact))) { Take(h); return; }
            }
            if (Time.time - born > 150f) Destroy(gameObject);   // 两分半没人捡就消失
        }

        void Take(HeroBase h)
        {
            Echoes.Equip(h, d, true);
            FX.Ring(h.transform.position, 0.3f, 2.4f, 0.4f, new Color(d.Col.r, d.Col.g, d.Col.b, 0.9f), 0.14f);
            if (h.IsControlled || h.UsesBrain && Combat.player) Combat.Toast($"{h.HeroName} 装备声骸：{d.name}（R）· {d.desc}", d.Col);
            if (h.IsControlled) Feel.I.Sfx("perfect_dodge", 0.6f, 0f, 1.4f);
            Destroy(gameObject);
        }
    }
}

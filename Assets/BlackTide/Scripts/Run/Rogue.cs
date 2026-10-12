using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 流派（2026-10-09 构筑重做）：每张卡属于一个流派，同流派的卡（含升级层数）攒到 2 / 5 层触发流派共鸣
    public enum School { 潮噬, 雷鸣, 镜步, 协奏, 坚守, 诅咒 }
    public enum CardRarity { 普通, 稀有, 传说 }

    public class BuffDef
    {
        public string id, name;
        public School school;
        public CardRarity rarity;
        public string[] lv;          // 每一级的描述（数组长度 = 最高等级）
        public bool team;            // 团队效果：一人选到全队生效，之后谁都不会再刷到
        public bool repeat;          // 池子空了时的保底条目，可以重复选
        public HeroId? only;         // 角色专属
        public int MaxLv => lv.Length;
        public string Desc(int level) => lv[Mathf.Clamp(level, 1, lv.Length) - 1];
    }

    // 每个角色的构筑
    public class RogueState
    {
        public readonly List<BuffDef> owned = new List<BuffDef>();       // 选到过的卡（不重复，升级只改等级）
        public readonly Dictionary<string, int> levels = new Dictionary<string, int>();
        public int pending;                    // 还没选的次数
        public BuffDef[] offer;                // 当前的三张卡（没选之前不换）
        public int picks;                      // 已经选了几次（决定稀有度权重）
        public float guardReadyAt, lastDodgeT = -9f, bulwarkReadyAt, counterHitUntil;
        // 武器词条的时效状态（WeaponAffix 2026-10-10 扩充词条用）
        public float carnageUntil, thundersurgeUntil, phantomUntil, aftertoneUntil, ironwillUntil, lastChainHitT;
        public int hitChain;
        public int refreshKills, minor;
        // 割草方向重做：共鸣超载（自己杀够数量攒一次全屏脉冲）
        public int overloadKills;
        public float overloadReadyAt = -1f;
        public int hunger; public float hungerUntil;
        public readonly List<School> extra = new List<School>();   // 武器词条带来的流派层数（每条 1 层）
        public int affixVersion = -1;
        public bool Has(string id) => levels.ContainsKey(id);
        public int Lv(string id) => levels.TryGetValue(id, out int v) ? v : 0;
        public int Count(string id) => id == "minor" ? minor : Lv(id);
        public int SchoolPts(School s)
        {
            int n = 0;
            foreach (var b in owned) if (b.school == s) n += Lv(b.id);
            foreach (var x in extra) if (x == s) n++;
            return n;
        }
        public int Tier(School s) { int p = SchoolPts(s); return p >= Rogue.Tier2 ? 2 : p >= Rogue.Tier1 ? 1 : 0; }
    }

    // 肉鸽（M4；2026-10-09 打磨重做）。
    // 获得方式不变：团队击杀计数（轻波 1、巨浪 8、怒涛 30）过门槛时每人一次三选一（门槛由 RunSim 定，一局约 6–8 次）。
    // 重做：原来 27 张多是「伤害 +20%」的数值卡，卡之间没有关系。现在——
    //   · 五个流派：潮噬（击杀滚雪球）、雷鸣（暴击触发闪电）、镜步（完美闪避反打）、协奏（技能循环和接力）、坚守（护盾和续航）；
    //   · 同流派攒到 2 层、5 层触发流派共鸣（升级也算层数），一局 6–8 次选择，专精一个流派刚好能到 5 层；
    //   · 选到已有的卡 = 升级（最高 3 级），刷卡时有一定概率刷出已有卡的升级；
    //   · 稀有度：越往后越容易刷到稀有、传说；
    //   · 角色专属传说卡（改技能机制）；
    //   · 诅咒卡：收益大、代价也大，第 3 次选择起可能出现在第三个位置。
    public static class Rogue
    {
        // ---------------- 计分和门槛（DS-019：门槛减半——一局从 6–8 次三选一提至 12–16 次，配合弹幕军火库） ----------------
        public static int[] Thresholds = { 0, 10, 30, 75, 135, 200 };
        public static int Step = 50;
        public static int Points(int q, int j, int n) => q + 8 * j + 30 * n;
        public static int Unlocks(int points) => Unlocks(points, Thresholds, Step);
        public static int Unlocks(int points, int[] th, int step)
        {
            int k = 0;
            foreach (var t in th) if (points >= t) k++;
            int last = th[th.Length - 1];
            if (points > last && step > 0) k += (points - last) / step;
            return k;
        }
        public static int TeamPoints => Points(Coop.killsQ, Coop.killsJ, Coop.killsN);

        public const int Tier1 = 2, Tier2 = 5;

        // ---------------- 流派 ----------------
        public static readonly Color[] SchoolCol =
        {
            new Color(0.74f, 0.5f, 1f),     // 潮噬 紫
            new Color(1f, 0.86f, 0.38f),    // 雷鸣 金
            new Color(0.56f, 0.82f, 1f),    // 镜步 冰蓝
            new Color(0.52f, 1f, 0.62f),    // 协奏 绿
            new Color(1f, 0.66f, 0.36f),    // 坚守 琥珀
            new Color(1f, 0.32f, 0.36f),    // 诅咒 血红
        };
        public static readonly string[] SchoolTag =
        {
            "击杀滚雪球", "暴击引雷", "完美闪避反打", "技能循环与接力", "护盾与续航", "代价换收益",
        };
        public static readonly string[,] Resonance =
        {
            { "击杀回复 3 共鸣能量、1 协奏能量", "全队击杀巨浪及以上时，全队 8 秒伤害 +15%" },
            { "暴击率 +5%", "暴击时 20% 概率放出闪电链（4 跳，每跳 0.8 倍）" },
            { "完美闪避判定 +0.04 秒", "完美闪避后 4 秒伤害 +25%" },
            { "接力时自己回复 10% 生命", "放出共鸣解放时，全队 8 秒伤害 +15%" },
            { "受到的伤害 −8%", "生命低于 30% 时获得 30% 生命的护盾（冷却 45 秒）" },
            { "", "" },
        };
        public static readonly Color[] RarityCol = { new Color(0.75f, 0.78f, 0.82f), new Color(0.45f, 0.72f, 1f), new Color(1f, 0.72f, 0.3f) };

        static BuffDef B(string id, string name, School s, CardRarity r, params string[] lv) => new BuffDef { id = id, name = name, school = s, rarity = r, lv = lv };

        public static readonly BuffDef[] All =
        {
            // 潮噬：击杀滚雪球
            B("leech", "汲潮", School.潮噬, CardRarity.普通, "队伍每击杀一个敌人，回复 1.5% 生命", "队伍每击杀一个敌人，回复 2.2% 生命", "队伍每击杀一个敌人，回复 3% 生命"),
            B("refresh", "清剿刷新", School.潮噬, CardRarity.普通, "队伍每击杀 10 个轻波，重置共鸣技能（E）", "每击杀 8 个轻波，重置共鸣技能（E）", "每击杀 6 个轻波，重置共鸣技能（E）"),
            B("hunger", "噬潮", School.潮噬, CardRarity.稀有, "你每次击杀叠 1 层（5 秒不击杀清空），每层伤害 +2%，最多 10 层", "最多 15 层", "最多 20 层"),
            B("tide_burst", "潮爆", School.潮噬, CardRarity.稀有, "你击杀的敌人炸开，3.5 米内造成 1.2 倍伤害（可以连锁）", "炸开伤害 1.7 倍", "炸开伤害 2.2 倍，范围 4.5 米", "炸开伤害 2.8 倍，范围 5.5 米", "炸开伤害 3.4 倍，范围 6.5 米，连环爆炸"),
            B("execute", "处决", School.潮噬, CardRarity.普通, "对失衡（架势被打空）的敌人伤害 +25%", "对失衡的敌人伤害 +40%", "对失衡的敌人伤害 +55%"),
            new BuffDef { id = "feilie_ember", name = "熔火余烬", school = School.潮噬, rarity = CardRarity.传说, only = HeroId.Feilie,
                lv = new[] { "断焰每命中一个敌人叠 2 层灼痕；焚城每消耗 1 层灼痕回复 2% 生命", "焚城每层回复 3% 生命", "焚城每层回复 4% 生命，灼痕满 5 层时断焰冷却 −50%" } },
            // 雷鸣：暴击引雷
            B("crit", "暴击强化", School.雷鸣, CardRarity.普通, "暴击率 +8%，暴击伤害 +20%", "暴击率 +13%，暴击伤害 +30%", "暴击率 +18%，暴击伤害 +40%"),
            B("chain", "连锁闪电", School.雷鸣, CardRarity.稀有, "暴击时 25% 概率放出闪电链：跳 4 个敌人，每跳 0.8 倍伤害", "概率 35%", "概率 45%，跳 6 个敌人", "概率 55%，跳 8 个敌人", "概率 65%，跳 10 个敌人，闪电伤害 +40%"),
            B("thunder", "雷电场", School.雷鸣, CardRarity.稀有, "放出共鸣解放后 21 秒内，每 3 秒向身边的敌人落雷", "每 2.5 秒落雷", "每 2 秒落雷，落雷必定暴击", "每 1.5 秒落雷，落雷连锁 2 个敌人", "每 1 秒落雷，连锁 3 个敌人，必暴击"),
            B("swords", "利剑出鞘", School.雷鸣, CardRarity.普通, "每 25 秒向身边最多 3 个敌人各射出一把剑", "每 20 秒", "每 15 秒，最多 5 个敌人", "每 12 秒，最多 6 个敌人", "每 10 秒，最多 8 个敌人"),
            B("break", "破韧", School.雷鸣, CardRarity.普通, "对巨浪及以上的敌人，架势积累 +25%", "架势积累 +40%", "架势积累 +55%"),
            new BuffDef { id = "chengyin_thunder", name = "雷音共振", school = School.雷鸣, rarity = CardRarity.传说, only = HeroId.Chengyin,
                lv = new[] { "静电满层从 10 层降到 6 层；落雷后再跳到附近 2 个敌人", "跳 3 个敌人", "跳 4 个敌人，落雷给全队 3 秒 15% 伤害加成" } },
            // 镜步：完美闪避反打
            B("dodge_guard", "闪身护体", School.镜步, CardRarity.普通, "闪避后 2 秒内受到的伤害 −30%", "−40%", "−50%"),
            B("swift", "疾步", School.镜步, CardRarity.普通, "完美闪避判定窗口 +0.05 秒", "+0.08 秒", "+0.11 秒"),
            B("riposte", "回锋", School.镜步, CardRarity.稀有, "完美闪避后的反击伤害 +60%", "反击伤害 +90%", "反击伤害 +120%"),
            B("afterimage", "残影", School.镜步, CardRarity.稀有, "完美闪避时原地留下残影，0.4 秒后爆炸，3.5 米内 2.5 倍伤害", "爆炸 3.5 倍", "爆炸 4.5 倍，范围 5 米", "爆炸 5.5 倍，范围 5.8 米", "爆炸 6.5 倍，范围 6.5 米，0.2 秒后第二道残影"),
            B("flow", "心流", School.镜步, CardRarity.传说, "完美闪避时 50% 概率重置共鸣技能（E）", "75% 概率", "必定重置，并回复 10 共鸣能量"),
            // 协奏：技能循环与接力
            B("cd", "共鸣效率", School.协奏, CardRarity.普通, "共鸣技能（E）冷却 −15%", "冷却 −22%", "冷却 −30%"),
            B("energy", "能量充盈", School.协奏, CardRarity.普通, "共鸣能量获取 +25%（更快放解放）", "+35%", "+45%"),
            B("concerto", "协奏增幅", School.协奏, CardRarity.普通, "协奏能量获取 +30%（更快接力）", "+45%", "+60%"),
            B("resonance", "余韵", School.协奏, CardRarity.稀有, "接力后，接力对象 8 秒伤害 +15%", "+22%", "+30%"),
            B("echo_blast", "共鸣爆炸", School.协奏, CardRarity.普通, "声骸技能（R）额外引发 6 米爆炸，1.5 倍伤害", "爆炸 2.2 倍", "爆炸 3 倍", "爆炸 4 倍，范围 7.5 米", "爆炸 5 倍，范围 9 米"),
            B("vortex", "飓风场", School.协奏, CardRarity.普通, "声骸技能（R）先把 8 米内的杂兵拉到身边"),
            new BuffDef { id = "qinglan_eye", name = "风眼", school = School.协奏, rarity = CardRarity.传说, only = HeroId.Qinglan,
                lv = new[] { "旋流半径 +30%，持续时间 +1 秒", "半径 +45%", "半径 +60%，旋流结束时炸开（2 倍伤害）" } },
            new BuffDef { id = "team_revive", name = "团队复活", school = School.协奏, rarity = CardRarity.传说, team = true,
                lv = new[] { "本局第一次团灭时，全队以 50% 生命原地复活，30 秒内伤害 +20%（全队生效）" } },
            // 坚守：护盾与续航
            B("hp", "生命强化", School.坚守, CardRarity.普通, "最大生命 +15%", "+22%", "+30%"),
            B("dr", "伤害减免", School.坚守, CardRarity.普通, "受到的伤害 −12%", "−18%", "−24%"),
            B("regen", "复苏", School.坚守, CardRarity.普通, "5 秒没受伤后，每秒回复 2% 生命", "每秒 3%", "每秒 4%"),
            B("shieldbash", "盾势", School.坚守, CardRarity.稀有, "身上有护盾时伤害 +15%（绯烈的被动护盾、澄音的护盾都算）", "+22%", "+30%"),
            B("calm", "静心", School.坚守, CardRarity.稀有, "超频值积累 −20%", "−35%", "−50%"),
            B("tenacity", "坚韧", School.坚守, CardRarity.普通, "杂兵的攻击不会打出受击硬直"),
            B("undying", "不屈", School.坚守, CardRarity.普通, "倒地后的救援倒计时 +10 秒，你救人快 40%"),
            B("guard", "共鸣护盾", School.坚守, CardRarity.传说, "受到致命伤时改为 3 秒无敌（冷却 120 秒）"),
            // 诅咒：代价换收益（AI 不选）
            B("blood_pact", "血契", School.诅咒, CardRarity.稀有, "伤害 +35%；最大生命 −25%"),
            B("glass", "孤注", School.诅咒, CardRarity.稀有, "暴击伤害 +80%；受到的伤害 +25%"),
            B("tide_gift", "潮之馈赠", School.诅咒, CardRarity.稀有, "在黑潮覆盖范围内伤害 +30%；超频值积累 +40%"),
            B("frenzy", "狂潮", School.诅咒, CardRarity.稀有, "共鸣技能（E）冷却 −40%；每次放共鸣技能扣 4% 当前生命"),
            // DS-019 弹幕军火库（每卡 5 级，割草向自动攻击）
            B("orbit", "环绕飞刃", School.镜步, CardRarity.稀有, "2 把飞刃环绕你旋转，1.5 米内 1.1 倍伤害", "3 把飞刃，伤害 1.3 倍", "4 把飞刃，伤害 1.5 倍", "5 把飞刃，转速 +40%", "6 把飞刃，伤害 1.9 倍，转速 +60%"),
            B("homing", "追击弹幕", School.雷鸣, CardRarity.稀有, "每 4 秒自动射出 3 枚追踪弹（0.9 倍伤害）", "4 枚追踪弹", "每 3.4 秒 5 枚", "每 2.8 秒 6 枚，伤害 1.1 倍", "每 2.2 秒 8 枚，伤害 1.3 倍"),
            B("nova", "火焰新星", School.潮噬, CardRarity.稀有, "每 6 秒以你为中心爆发火焰，4 米内 1.4 倍伤害", "范围 4.8 米，伤害 1.7 倍", "每 5 秒，范围 5.6 米", "范围 6.4 米，伤害 2.2 倍", "每 4 秒，范围 7 米，伤害 2.6 倍"),
            // DS-019b 弹幕军火库第二波
            B("net", "雷霆之网", School.雷鸣, CardRarity.稀有, "每 5 秒从你身上放出闪电链，跳 3 个敌人（0.9 倍伤害）", "每 4.6 秒，跳 4 个", "每 4.2 秒，跳 5 个", "每 3.8 秒，跳 6 个，伤害 1.1 倍", "每 3 秒，跳 7 个，伤害 1.3 倍"),
            B("maelstrom", "漩涡", School.镜步, CardRarity.稀有, "每 8 秒在瞄准点生成漩涡，1.2 秒内吸住 6 米内敌人并减速", "半径 6.8 米，持续 1.4 秒", "每 7 秒，持续 1.6 秒", "半径 8.4 米，持续 1.8 秒", "每 6 秒，半径 9.2 米，持续 2 秒"),
            B("cleave", "连锁打击", School.坚守, CardRarity.稀有, "普攻命中时向 3.4 米内另 1 个敌人溅射（0.64 倍伤害）", "溅射 2 个，0.73 倍", "溅射 3 个，0.82 倍", "范围 4.6 米，溅射 4 个", "溅射 5 个，1.0 倍，范围 5 米"),
            // 割草方向重做（Claude 总工 2026-10-11）：第二批军火库。描述里的数字与 Arsenal.cs / OnPerfectDodge 实现一致。
            B("scythe", "潮汐镰刀", School.潮噬, CardRarity.稀有, "每 3.7 秒横扫 6 米内最多 6 个敌人（1.1 倍）；生命低于 14% 的非 Boss 直接收割", "最多 8 个，1.2 倍，残血线 16%", "每 3 秒，最多 10 个，范围 7.3 米，1.3 倍", "最多 12 个，1.4 倍，残血线 20%", "每 2.3 秒，最多 14 个，范围 8.5 米，1.55 倍，残血线 22%"),
            B("bounce", "弹跳雷", School.雷鸣, CardRarity.稀有, "每 4.2 秒掷出一发弹跳雷，命中后跳向最多 3 个别的敌人（每跳衰减 12%）", "跳 4 个，每 3.9 秒", "跳 5 个，首跳 1.24 倍", "跳 6 个，每 3.3 秒", "跳 7 个，每 3 秒，首跳 1.4 倍"),
            B("vents", "地火喷口", School.潮噬, CardRarity.稀有, "每 4.7 秒在附近敌人脚下喷出 3 处地火，2.6 秒内每 0.4 秒灼烧一次（每次 0.45 倍）", "4 处", "每 4 秒 5 处，每次 0.51 倍", "6 处，每 3.6 秒", "每 3.3 秒 7 处，每次 0.57 倍"),
            B("gale", "风涡扫荡", School.协奏, CardRarity.稀有, "每 5.6 秒放一圈向外推进的风涡，扫过的敌人受 1.1 倍伤害并被推开", "推至 9.8 米，每 5.2 秒", "1.3 倍，推至 11.2 米", "每 4.4 秒，推至 12.6 米", "每 4 秒，推至 14 米，1.5 倍"),
            B("overload", "共鸣超载", School.潮噬, CardRarity.传说, "你每击杀 16 个敌人释放一次共鸣脉冲：13 米内所有敌人受 1.4 倍伤害", "每 14 个一次，1.6 倍", "每 12 个，15 米，1.8 倍", "每 10 个，16 米", "每 8 个一次，17 米，2.2 倍"),
            B("sigil", "镜步残阵", School.镜步, CardRarity.稀有, "完美闪避时沿闪避路径留下 3 枚符阵，敌人踏入即爆（1.2 倍）并定身 0.5 秒", "4 枚，1.5 倍", "5 枚，1.8 倍，定身 0.8 秒"),
            // 池子空了的保底
            new BuffDef { id = "minor", name = "小幅强化", school = School.坚守, rarity = CardRarity.普通, repeat = true, lv = new[] { "伤害 +5%（可重复）" } },
        };
        public static BuffDef Def(string id) { foreach (var b in All) if (b.id == id) return b; return null; }

        public static bool teamRevive, teamReviveUsed;
        static int given;            // 已经发给每个人的次数（全队一样）

        public static void Reset() { teamRevive = teamReviveUsed = false; given = 0; delayed.Clear(); WeaponAffix.Reset(); }

        // RunDirector 每帧调：计数过门槛就给每人一次；结算延迟效果（潮爆、闪电链、残影）
        public static void Tick()
        {
            int u = Unlocks(TeamPoints);
            while (given < u)
            {
                given++;
                foreach (var h in Combat.heroes) if (h) h.rogue.pending++;
                if (RunDirector.I) RunDirector.I.Event("buff_unlock", "", $"n={given} points={TeamPoints}");
                if (Combat.player && !HeroBase.Autopilot) Combat.Toast($"团队击杀计数 {TeamPoints}：解锁第 {given} 次增益（{GameInput.Hint("B","LS 按下")} 选择）", new Color(1f, 0.85f, 0.5f));
            }
            foreach (var h in Combat.heroes)
                if (h && h.rogue.pending > 0 && h.UsesBrain) AutoPick(h);
            for (int i = 0; i < delayed.Count; i++)
                if (Time.time >= delayed[i].at) { var a = delayed[i].act; delayed.RemoveAt(i); i--; a(); }
            WeaponAffix.Tick();
        }

        // 延迟结算：在 DealHit / 击杀回调里直接打别的敌人会在遍历敌人列表时改列表（之前共鸣爆炸出过这个错），统一推迟到下一帧以后
        struct Delayed { public float at; public System.Action act; }
        static readonly List<Delayed> delayed = new List<Delayed>();
        static void Later(float dt, System.Action a) => delayed.Add(new Delayed { at = Time.time + dt, act = a });
        public static void Delay(float dt, System.Action a) => Later(dt, a);

        // ---------------- 刷卡 ----------------
        // 稀有度权重随选择次数变化：第 1–3 次 普通 70 / 稀有 28 / 传说 2；第 4–6 次 50 / 40 / 10；之后 35 / 45 / 20
        static float RarityW(CardRarity r, int picks)
        {
            int k = picks < 3 ? 0 : picks < 6 ? 1 : 2;
            float[,] w = { { 70, 28, 2 }, { 50, 40, 10 }, { 35, 45, 20 } };
            return w[k, (int)r];
        }

        // 割草方向重做（CL-001 窗口数据）：headless 实测选中的卡里割草卡只占 31%（17/54），
        // 用户要的是「更多割草技能」，所以在**发卡**和**AI 评分**两处都提权，不改卡本身强度。
        static readonly HashSet<string> MowerIds = new HashSet<string> { "orbit", "homing", "nova", "net", "maelstrom", "cleave", "scythe", "bounce", "vents", "gale", "overload", "sigil" };
        public static bool IsMower(BuffDef b) => b != null && MowerIds.Contains(b.id);

        public static BuffDef[] Offer(HeroBase h, System.Random rng = null) => Offer(h.rogue, h.Id, rng);
        public static BuffDef[] Offer(RogueState s, HeroId who, System.Random rng = null)
        {
            if (s.offer != null) return s.offer;
            System.Func<double> R = () => rng != null ? rng.NextDouble() : Random.value;
            var o = new List<BuffDef>();
            bool curseSlot = s.picks >= 2 && R() < 0.3;
            for (int k = 0; k < 3; k++)
            {
                bool wantCurse = curseSlot && k == 2;
                var cand = new List<BuffDef>(); var w = new List<float>();
                foreach (var b in All)
                {
                    if (b.repeat || o.Contains(b)) continue;
                    if ((b.school == School.诅咒) != wantCurse) continue;
                    if (b.only.HasValue && b.only.Value != who) continue;
                    if (b.team && teamRevive) continue;
                    int lv = s.Lv(b.id);
                    if (lv >= b.MaxLv) continue;
                    float wt = RarityW(b.rarity, s.picks);
                    if (lv > 0) wt *= 1.3f;                                         // 升级
                    else if (b.school != School.诅咒 && s.SchoolPts(b.school) > 0) wt *= 1.8f;   // 已经在走的流派更容易刷到
                    if (b.only.HasValue) wt *= 2f;                                  // 专属卡
                    if (IsMower(b)) wt *= 1.6f;                                     // CL-001：割草卡提权（实测被选占比 31%）
                    cand.Add(b); w.Add(wt);
                }
                if (cand.Count == 0) { o.Add(Def("minor")); continue; }
                float sum = 0f; foreach (var x in w) sum += x;
                float r = (float)R() * sum;
                int pick = 0;
                for (; pick < cand.Count - 1; pick++) { r -= w[pick]; if (r <= 0f) break; }
                o.Add(cand[pick]);
            }
            // 保证三张卡至少有两个不同流派（全同流派时把第一张换成别的流派）
            if (o[0].school == o[1].school && o[1].school == o[2].school)
            {
                foreach (var b in All)
                    if (!b.repeat && b.school != o[0].school && b.school != School.诅咒 && !s.Has(b.id) && (!b.only.HasValue || b.only.Value == who) && !(b.team && teamRevive)) { o[0] = b; break; }
            }
            return s.offer = o.ToArray();
        }

        public static void Pick(HeroBase h, BuffDef b)
        {
            var s = h.rogue;
            if (s.pending <= 0 || b == null) return;
            var tierBefore = b.school != School.诅咒 ? s.Tier(b.school) : 0;
            if (b.repeat) s.minor++;
            else if (s.Has(b.id)) s.levels[b.id]++;
            else { s.owned.Add(b); s.levels[b.id] = 1; }
            s.pending--; s.picks++; s.offer = null;
            if (b.team) teamRevive = true;
            if (b.id == "hp" || b.id == "blood_pact") h.RefreshMaxHp();
            int lv = b.repeat ? s.minor : s.Lv(b.id);
            if (RunDirector.I) RunDirector.I.Event("buff", "", $"{h.HeroName} {b.name} Lv{lv} {b.school}");
            if (h.IsControlled || b.team) Combat.Toast($"{h.HeroName} 获得{(lv > 1 && !b.repeat ? "升级" : "增益")}：{b.name}{(lv > 1 && !b.repeat ? " Lv" + lv : "")}（{b.Desc(lv)}）", SchoolCol[(int)b.school]);
            if (b.school != School.诅咒)
            {
                int tierAfter = s.Tier(b.school);
                if (tierAfter > tierBefore && h.IsControlled)
                {
                    Combat.Toast($"流派共鸣 · {b.school} {(tierAfter == 2 ? "Ⅱ" : "Ⅰ")}：{Resonance[(int)b.school, tierAfter - 1]}", SchoolCol[(int)b.school]);
                    FX.Ring(h.transform.position, 0.3f, 4f, 0.5f, SchoolCol[(int)b.school], 0.2f);
                    Feel.I.PulseHit(0.5f);
                }
            }
        }

        // ---------------- AI 选卡（按角色偏好的流派 + 已经在走的流派；规则确定，不随机） ----------------
        static readonly Dictionary<HeroId, School[]> Pref = new Dictionary<HeroId, School[]>
        {
            { HeroId.Feilie, new[] { School.潮噬, School.雷鸣, School.坚守 } },
            { HeroId.Qinglan, new[] { School.雷鸣, School.协奏, School.镜步 } },
            { HeroId.Chengyin, new[] { School.坚守, School.协奏, School.雷鸣 } },
        };
        public static float Score(HeroId who, RogueState s, BuffDef b, int wipes, float erosion)
        {
            if (b.school == School.诅咒) return -5f;
            float sc = 1f;
            if (Pref.TryGetValue(who, out var pref))
                for (int i = 0; i < pref.Length; i++) if (pref[i] == b.school) sc += 3f - i;
            sc += Mathf.Min(s.SchoolPts(b.school), 4) * 0.8f;     // 已经在走的流派继续走（冲 5 层共鸣）
            sc += (int)b.rarity * 1.2f;
            if (b.only.HasValue) sc += 2f;
            if (b.school == School.坚守 && wipes > 0) sc += 2f;
            if (b.id == "calm" && erosion >= 40f) sc += 2.5f;
            if (b.id == "team_revive") sc += 4f;
            if (b.repeat) sc = 0f;
            if (IsMower(b)) sc += 2f;                       // CL-001：AI 也往割草构筑靠
            return sc;
        }
        public static BuffDef Choose(HeroId who, RogueState s, BuffDef[] o, int wipes, float erosion)
        {
            BuffDef best = null; float bs = float.MinValue;
            foreach (var b in o) { float x = Score(who, s, b, wipes, erosion); if (x > bs) { bs = x; best = b; } }
            return best;
        }
        public static void AutoPick(HeroBase h)
        {
            while (h.rogue.pending > 0)
                Pick(h, Choose(h.Id, h.rogue, Offer(h), RunDirector.I ? RunDirector.I.wipes : 0, Coop.erosion));
        }

        // ---------------- 数值查询（HeroBase 结算时调用） ----------------
        static float L(RogueState s, string id, float a, float b, float c) { int v = s.Lv(id); return v == 0 ? 0f : v == 1 ? a : v == 2 ? b : c; }
        static float L5(RogueState s, string id, float a, float b, float c, float d, float e) { int v = s.Lv(id); return v <= 0 ? 0f : v == 1 ? a : v == 2 ? b : v == 3 ? c : v == 4 ? d : e; }   // DS-019b：5 级卡取值

        public static float DmgBonus(HeroBase h, Damageable target)
        {
            var s = h.rogue; float d = 0f;
            d += 0.05f * s.minor;
            if (s.hunger > 0 && Time.time < s.hungerUntil) d += 0.02f * s.hunger;
            if (target && target.Broken) d += L(s, "execute", 0.25f, 0.4f, 0.55f);
            if (h.dmg.shield > 1f) d += L(s, "shieldbash", 0.15f, 0.22f, 0.3f);
            if (Time.time < s.counterHitUntil) d += L(s, "riposte", 0.6f, 0.9f, 1.2f);
            if (s.Has("blood_pact")) d += 0.35f;
            if (s.Has("tide_gift") && Coop.coverageRadius >= 0f && Coop.InTide(h.transform.position)) d += 0.3f;
            if (teamReviveBuffUntil > Time.time) d += 0.2f;
            return d;
        }
        public static float CritChance(HeroBase h) => L(h.rogue, "crit", 0.08f, 0.13f, 0.18f) + (h.rogue.Tier(School.雷鸣) >= 1 ? 0.05f : 0f);
        public static float CritDmg(HeroBase h) => L(h.rogue, "crit", 0.2f, 0.3f, 0.4f) + (h.rogue.Has("glass") ? 0.8f : 0f);
        public static float StaggerBonus(HeroBase h, Damageable target) => target && target.tier >= 1 ? L(h.rogue, "break", 0.25f, 0.4f, 0.55f) : 0f;
        public static float HpBonus(HeroBase h) => L(h.rogue, "hp", 0.15f, 0.22f, 0.3f) - (h.rogue.Has("blood_pact") ? 0.25f : 0f);
        public static float TakenBonus(HeroBase h)
        {
            var s = h.rogue; float t = 0f;
            t -= L(s, "dr", 0.12f, 0.18f, 0.24f);
            if (Time.time - s.lastDodgeT < 2f) t -= L(s, "dodge_guard", 0.3f, 0.4f, 0.5f);
            if (s.Tier(School.坚守) >= 1) t -= 0.08f;
            if (s.Has("glass")) t += 0.25f;
            t += WeaponAffix.TakenBonus(h);
            return t;
        }
        public static float CdMult(HeroBase h) => (1f - L(h.rogue, "cd", 0.15f, 0.22f, 0.3f)) * (h.rogue.Has("frenzy") ? 0.6f : 1f);
        public static float OcGainMult(HeroBase h) => (1f - L(h.rogue, "calm", 0.2f, 0.35f, 0.5f)) * (h.rogue.Has("tide_gift") ? 1.4f : 1f);
        public static float RescueSpeed(HeroBase h) => h.rogue.Has("undying") ? 1.4f : 1f;
        public static float ConcertoMult(HeroBase h) => 1f + L(h.rogue, "concerto", 0.3f, 0.45f, 0.6f);
        public static float EnergyMult(HeroBase h) => 1f + L(h.rogue, "energy", 0.25f, 0.35f, 0.45f);
        public static float DownTimeBonus(HeroBase h) => h.rogue.Has("undying") ? 10f : 0f;
        public static float RegenRate(HeroBase h) => L(h.rogue, "regen", 0.02f, 0.03f, 0.04f);
        public static float PerfectBonus(HeroBase h) => L(h.rogue, "swift", 0.05f, 0.08f, 0.11f) + (h.rogue.Tier(School.镜步) >= 1 ? 0.04f : 0f);
        public static float ThunderInterval(HeroBase h) { int lv = h.rogue.Lv("thunder"); return lv >= 5 ? 1f : lv >= 4 ? 1.5f : lv >= 3 ? 2f : lv == 2 ? 2.5f : 3f; }
        public static bool ThunderCrit(HeroBase h) => h.rogue.Lv("thunder") >= 3;
        public static int ThunderChain(HeroBase h) { int lv = h.rogue.Lv("thunder"); return lv >= 5 ? 3 : lv >= 4 ? 2 : 0; }   // DS-019b：Lv4 起落雷连锁
        public static float EchoBlastMult(HeroBase h) => L5(h.rogue, "echo_blast", 1.5f, 2.2f, 3f, 4f, 5f);
        public static float EchoBlastRange(HeroBase h) { int lv = h.rogue.Lv("echo_blast"); return lv >= 5 ? 9f : lv >= 4 ? 7.5f : 6f; }   // DS-019b
        public static int Leech(HeroBase h) => h.rogue.Lv("leech");
        // 坚韧：杂兵（轻波、巨浪，不含 Boss）的攻击不打硬直
        public static bool IgnoreStagger(HeroBase h, HitInfo hit)
        {
            if (!h.rogue.Has("tenacity") || !hit.attacker) return false;
            var d = hit.attacker.GetComponentInParent<Damageable>();
            return d && !d.bossCtl;
        }
        // 角色专属
        public static int EmberGain(HeroBase h) => h.rogue.Has("feilie_ember") ? 2 : 1;
        public static float EmberHeal(HeroBase h) => L(h.rogue, "feilie_ember", 0.02f, 0.03f, 0.04f);
        public static bool EmberCdCut(HeroBase h) => h.rogue.Lv("feilie_ember") >= 3;
        public static int StaticMax(HeroBase h) => h.rogue.Has("chengyin_thunder") ? 6 : 10;
        public static float VortexScale(HeroBase h) => 1f + L(h.rogue, "qinglan_eye", 0.3f, 0.45f, 0.6f);
        public static float VortexExtra(HeroBase h) => h.rogue.Has("qinglan_eye") ? 1f : 0f;
        public static bool VortexBurst(HeroBase h) => h.rogue.Lv("qinglan_eye") >= 3;

        // DS-019b 连锁打击：普攻命中向附近敌人溅射（HeroBase.DealHit 里对 basic/heavy 触发）
        static readonly List<Damageable> cleaveList = new List<Damageable>();
        public static void OnMeleeHit(HeroBase h, Damageable e)
        {
            int lv = h.rogue.Lv("cleave");
            if (lv <= 0) return;
            float r = 3f + 0.4f * lv;
            float mult = 0.55f + 0.09f * lv;
            Vector3 c = Flat(e.transform.position);
            cleaveList.Clear();
            foreach (var x in Combat.enemies)
            {
                if (!x || x.dead || x.dormant || x == e) continue;
                if (Vector3.Distance(Flat(x.transform.position), c) <= r + x.radius) cleaveList.Add(x);
            }
            cleaveList.Sort((a, b) => Vector3.Distance(a.transform.position, c).CompareTo(Vector3.Distance(b.transform.position, c)));
            for (int i = 0; i < Mathf.Min(lv, cleaveList.Count); i++)
            {
                var x = cleaveList[i];
                Vector3 dir = Flat(x.transform.position - c).normalized;
                h.DealHit(x, mult, 10f, 2f, dir, false, "cleave");
                FX.Sparks(x.Center, dir, 3, new Color(1f, 0.85f, 0.6f));
            }
        }

        // ---------------- 事件钩子 ----------------
        // 暴击（HeroBase.DealHit）：连锁闪电、雷鸣Ⅱ
        public static void OnCrit(HeroBase h, Damageable e, string kind)
        {
            if (kind == "chain") return;   // 闪电链自己不再触发闪电链
            var s = h.rogue;
            float p = L5(s, "chain", 0.25f, 0.35f, 0.45f, 0.55f, 0.65f);
            int lvc = s.Lv("chain");
            int jumps = lvc >= 5 ? 10 : lvc >= 4 ? 8 : lvc >= 3 ? 6 : 4;
            float mult = lvc >= 5 ? 1.1f : 0.8f;
            if (s.Tier(School.雷鸣) >= 2) p = 1f - (1f - p) * 0.8f;
            if (p <= 0f || Random.value >= p) return;
            Vector3 from = e.Center;
            Later(0.06f, () => Chain(h, from, e, jumps, mult));
        }

        static readonly List<Damageable> chainHit = new List<Damageable>();
        public static void Chain(HeroBase h, Vector3 from, Damageable first, int jumps, float mult)
        {
            if (!h) return;
            chainHit.Clear();
            if (first) chainHit.Add(first);
            Vector3 a = from;
            for (int j = 0; j < jumps; j++)
            {
                Damageable best = null; float bd = 7f;
                foreach (var x in Combat.enemies)
                {
                    if (!x || x.dead || x.dormant || chainHit.Contains(x)) continue;
                    float d = Vector3.Distance(x.Center, a);
                    if (d < bd) { bd = d; best = x; }
                }
                if (!best) break;
                chainHit.Add(best);
                Vector3 b = best.Center;
                Bolt(a, b, new Color(1f, 0.9f, 0.5f));
                var target = best; Vector3 dir = (b - a); dir.y = 0f;
                Later(0.04f * j, () => { if (h && target && !target.dead) h.DealHit(target, mult, 10f, 0.5f, dir.normalized, false, "chain"); });
                a = b;
            }
            Feel.I.Sfx("hit_armor", h.IsControlled ? 0.5f : 0.2f, 0.1f, 1.7f);
        }

        // 闪电：一串折线火花
        public static void Bolt(Vector3 a, Vector3 b, Color c)
        {
            const int n = 7;
            Vector3 prev = a;
            for (int i = 1; i <= n; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / (float)n);
                if (i < n) p += Random.insideUnitSphere * 0.35f;
                FX.Sparks(p, (p - prev).normalized, 2, c);
                FX.BoltStrip(prev, p, c);   // DS-010：闪电带贴片（KB-003）
                prev = p;
            }
            FX.ImpactFlash(b, 1.4f, c);
        }

        // 完美闪避（HeroBase.PerfectDodge）：残影、心流、镜步Ⅱ
        public static void OnPerfectDodge(HeroBase h)
        {
            var s = h.rogue;
            s.counterHitUntil = Time.time + 1.6f;   // 回锋：完美闪避后 1.6 秒内的反击
            if (s.Has("afterimage"))
            {
                Vector3 p = h.dodgeStartPos; p.y = 0f;
                FX.SlashScar(p, h.transform.forward, 2.0f, new Color(0.6f, 0.85f, 1f));   // DS-027：镜步=斜向刀痕（不再是冰蓝圆环）
                float mult = L5(s, "afterimage", 2.5f, 3.5f, 4.5f, 5.5f, 6.5f);
                int lva = s.Lv("afterimage");
                float r = lva >= 5 ? 6.5f : lva >= 4 ? 5.8f : lva >= 3 ? 5f : 3.5f;
                FX.Ring(p, 0.3f, 1.2f, 0.4f, new Color(0.56f, 0.82f, 1f, 0.9f), 0.1f);
                Later(0.4f, () =>
                {
                    if (!h) return;
                    foreach (var e in Combat.enemies.ToArray())
                        if (e && !e.dead && !e.dormant && Vector3.Distance(Flat(e.transform.position), p) <= r + e.radius)
                            h.DealHit(e, mult, 30f, 3f, Flat(e.transform.position - p).normalized, true, "afterimage");
                    FX.Ring(p, 0.4f, r + 0.4f, 0.4f, new Color(0.56f, 0.82f, 1f, 1f), 0.2f);
                    FX.Sparks(p + Vector3.up, Vector3.up, 26, new Color(0.75f, 0.9f, 1f));
                    Feel.I.Sfx("ult_hit", h.IsControlled ? 0.55f : 0.25f, 0.05f, 1.6f);
                    if (lva >= 5) Later(0.22f, () =>   // DS-019b：Lv5 第二道残影
                    {
                        if (!h) return;
                        foreach (var e in Combat.enemies.ToArray())
                            if (e && !e.dead && !e.dormant && Vector3.Distance(Flat(e.transform.position), p) <= r + e.radius)
                                h.DealHit(e, mult * 0.7f, 30f, 3f, Flat(e.transform.position - p).normalized, true, "afterimage");
                        FX.Ring(p, 0.4f, r + 0.4f, 0.4f, new Color(0.56f, 0.82f, 1f, 1f), 0.2f);
                    });
                });
            }
            // 割草方向重做：镜步残阵——沿刚闪过去的这条路埋符阵，追你的敌人踩上就炸（区域封锁，被围时犁出一条路）
            int lvs = s.Lv("sigil");
            if (lvs > 0) Arsenal.ArmSigils(h, h.dodgeStartPos, h.transform.position, 2 + lvs, lvs >= 3 ? 1.8f : lvs == 2 ? 1.5f : 1.2f, lvs >= 3 ? 0.8f : 0.5f);
            float flow = L(s, "flow", 0.5f, 0.75f, 1f);
            if (flow > 0f && Random.value < flow)
            {
                h.skillReadyAt = 0f;
                if (s.Lv("flow") >= 3) h.Gain(10f, 0f);
            }
            if (s.Tier(School.镜步) >= 2) h.AddMod("dmg", "mirror_res", 0.25f, 4f);
            WeaponAffix.OnPerfectDodge(h);
        }

        // 接力（HeroBase.StartRelay）：余韵、协奏Ⅰ
        public static void OnRelay(HeroBase from, HeroBase to)
        {
            var s = from.rogue;
            float r = L(s, "resonance", 0.15f, 0.22f, 0.3f);
            if (r > 0f && to) to.AddMod("dmg", "resonance", r, 8f);
            if (s.Tier(School.协奏) >= 1) from.dmg.hp = Mathf.Min(from.dmg.maxHp, from.dmg.hp + from.dmg.maxHp * 0.1f);
        }

        // 放出解放：协奏Ⅱ
        public static void OnUlt(HeroBase h)
        {
            if (h.rogue.Tier(School.协奏) < 2) return;
            foreach (var x in Combat.heroes) if (x && !x.Downed) x.AddMod("dmg", "concerto_res", 0.15f, 8f);
        }

        // 放出共鸣技能：狂潮的代价
        public static void OnSkill(HeroBase h)
        {
            if (h.rogue.Has("frenzy")) h.dmg.hp = Mathf.Max(1f, h.dmg.hp * 0.96f);
        }

        // 每帧（HeroBase.Update）：噬潮层数过期、坚守Ⅱ护盾、利剑出鞘
        public static void TickHero(HeroBase h, ref float nextSwords)
        {
            var s = h.rogue;
            WeaponAffix.Sync(h);
            if (s.hunger > 0 && Time.time >= s.hungerUntil) s.hunger = 0;
            if (s.Tier(School.坚守) >= 2 && !h.Downed && Time.time >= s.bulwarkReadyAt && h.dmg.hp < h.dmg.maxHp * 0.3f)
            {
                s.bulwarkReadyAt = Time.time + 45f;
                h.Heal(h, h.dmg.maxHp * 0.3f, true, 6f);
                FX.Ring(h.transform.position, 0.3f, 2.4f, 0.4f, SchoolCol[(int)School.坚守], 0.16f);
                if (h.IsControlled) Combat.Toast("坚守 · 危急护盾", SchoolCol[(int)School.坚守]);
            }
            TickSwords(h, ref nextSwords);
        }

        // 战地补给（旧卡，已移除）：保留空实现，RunDirector 清除节点时还会调
        public static void OnNodeCleared() { }

        // 利剑出鞘：向 12 米内最近的 3（5）个敌人各射一剑
        static readonly List<Damageable> near = new List<Damageable>();
        static void TickSwords(HeroBase h, ref float nextT)
        {
            int lv = h.rogue.Lv("swords");
            if (lv == 0 || Time.time < nextT || h.Downed) return;
            near.Clear();
            Vector3 p = Flat(h.transform.position);
            foreach (var e in Combat.enemies)
            {
                if (!e || e.dead || e.dormant) continue;
                if (Vector3.Distance(p, Flat(e.transform.position)) < 12f) near.Add(e);
            }
            if (near.Count == 0) return;
            int lvs = h.rogue.Lv("swords");
            nextT = Time.time + (lvs >= 5 ? 10f : lvs >= 4 ? 12f : lvs >= 3 ? 15f : lvs == 2 ? 20f : 25f);
            near.Sort((a, b) => Vector3.Distance(a.transform.position, p).CompareTo(Vector3.Distance(b.transform.position, p)));
            var list = near.GetRange(0, Mathf.Min(lvs >= 5 ? 8 : lvs >= 4 ? 6 : lvs >= 3 ? 5 : 3, near.Count));
            Later(0f, () =>
            {
                foreach (var e in list)
                {
                    if (!h || !e || e.dead) continue;
                    Vector3 dir = Flat(e.transform.position - h.transform.position);
                    h.DealHit(e, 1.5f, 15f, 1.5f, dir.normalized, false, "sword");
                    Vector3 a = h.transform.position + Vector3.up * 2.4f, b = e.transform.position + Vector3.up * 1.2f;
                    for (int k = 0; k <= 5; k++) FX.Sparks(Vector3.Lerp(a, b, k / 5f), dir.normalized, 2, new Color(0.85f, 0.95f, 1f));
                }
            });
        }

        // 共鸣护盾：致命伤改成 3 秒无敌。返回 true = 这次不倒地
        public static bool TryGuard(HeroBase h)
        {
            var s = h.rogue;
            if (!s.Has("guard") || Time.time < s.guardReadyAt) return false;
            s.guardReadyAt = Time.time + 120f;
            if (RunDirector.I) RunDirector.I.Event("buff_guard", "", h.HeroName);
            return true;
        }

        // 团队复活：本局第一次团灭时触发
        public static float teamReviveBuffUntil;
        public static bool TryTeamRevive()
        {
            if (!teamRevive || teamReviveUsed) return false;
            teamReviveUsed = true;
            teamReviveBuffUntil = Time.time + 30f;
            return true;
        }

        // ---------------- 击杀：汲潮、清剿刷新、噬潮、潮爆、潮噬共鸣 ----------------
        public static void OnKill(Damageable e)
        {
            HeroBase killer = e.lastAttacker ? e.lastAttacker.GetComponent<HeroBase>() : null;
            // 割草方向重做：共鸣超载攒次数（Arsenal.Pulse 的脉冲自己打死的不计，避免自我循环引爆）
            if (killer && !Arsenal.SuppressCount)
            {
                int ol = killer.rogue.Lv("overload");
                if (ol > 0 && ++killer.rogue.overloadKills >= 18 - 2 * ol) { killer.rogue.overloadKills = 0; killer.rogue.overloadReadyAt = Time.time; }
            }
            bool elite = e.tier >= 1 || e.bossCtl;
            bool teamBuff = false;
            foreach (var h in Combat.heroes)
            {
                if (!h || h.Downed) continue;
                var s = h.rogue;
                int lk = s.Lv("leech");
                if (lk > 0) h.dmg.hp = Mathf.Min(h.dmg.maxHp, h.dmg.hp + h.dmg.maxHp * (lk >= 3 ? 0.03f : lk == 2 ? 0.022f : 0.015f));
                int rf = s.Lv("refresh");
                if (rf > 0 && e.tier == 0 && ++s.refreshKills >= (rf >= 3 ? 6 : rf == 2 ? 8 : 10)) { s.refreshKills = 0; h.skillReadyAt = 0f; }
                if (elite && s.Tier(School.潮噬) >= 2) teamBuff = true;
            }
            if (teamBuff) foreach (var h in Combat.heroes) if (h && !h.Downed) h.AddMod("dmg", "devour_res", 0.15f, 8f);
            if (!killer) return;
            var ks = killer.rogue;
            int hl = ks.Lv("hunger");
            if (hl > 0) { ks.hunger = Mathf.Min(hl >= 3 ? 20 : hl == 2 ? 15 : 10, ks.hunger + 1); ks.hungerUntil = Time.time + 5f; }
            if (ks.Tier(School.潮噬) >= 1) killer.Gain(3f, 1f);
            int tb = ks.Lv("tide_burst");
            if (tb > 0)
            {
                Vector3 c = Flat(e.transform.position);
                float mult = tb >= 5 ? 3.4f : tb >= 4 ? 2.8f : tb >= 3 ? 2.2f : tb == 2 ? 1.7f : 1.2f;
                float r = tb >= 5 ? 6.5f : tb >= 4 ? 5.5f : tb >= 3 ? 4.5f : 3.5f;
                Later(0.15f, () =>
                {
                    if (!killer) return;
                    foreach (var x in Combat.enemies.ToArray())
                        if (x && !x.dead && !x.dormant && Vector3.Distance(Flat(x.transform.position), c) <= r + x.radius)
                            killer.DealHit(x, mult, 12f, 2.5f, Flat(x.transform.position - c).normalized, false, "burst");
                    // BigBurst supplies the single pressure front; no duplicate ring on every kill.
                    FX.BigBurst(c, r, SchoolCol[(int)School.潮噬]);   // DS-025：分层大爆炸（地裂+双冲击环+亮核+尘雾+碎片）
                    FX.Tide(c + Vector3.up * 0.8f, Vector3.up, 24);
                    Feel.I.Sfx("chant_blast", killer.IsControlled ? 0.35f : 0.15f, 0.1f, 1.5f);
                });
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}

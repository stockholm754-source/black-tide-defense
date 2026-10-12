using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BlackTide
{
    // 公式版快速模拟（不开 Play）：按路线 AI 走一局，用标定过的公式算清点用时、团灭和成长。
    // 和实时跑局共用：节点布局（MapLayout）、路线打分（RunDirector.RouteScore）、侵蚀曲线、等级表和经验（Growth）。
    // 标定数据来自实测跑局：清点用时见 Tools/calib_sim.py，团灭风险见 Tools/calib_wipe.py，赶路见 HANDOFF「公式模拟」。
    public static class RunSim
    {
        // ---------------- 标定常数 ----------------
        // 清点用时 = 固定开销（营地多 50 秒：三片之间要走路）+ 有效血量 / (k × 等级伤害倍率^a)
        // 有效血量以「一只轻波」为单位，再乘该处的侵蚀血量倍率。a = 2：等级带来的不只是伤害表，还有声骸、解放、团队技能
        public const float ClearBase = 2f, CampExtra = 50f, KMob = 0.24f, KBoss = 0.14f, LevelExp = 2f;
        // 团灭风险率（每秒）= h_类型 × (敌人伤害倍率 / 等级生命倍率)^p
        public const float HazardP = 0.9f;
        public const float HMob = 0.00102f, HNest = 0.00202f, HRift = 0.0063f, HWild = 0.00137f, HMessenger = 0.00817f;
        // 赶路：离节点约 25 米时敌人就醒了；之后约 6 米/秒
        public const float EngageRange = 25f, TravelSpeed = 6f;
        public const float BossHp = 38000f / 560f;   // 怒涛血量 / 轻波血量 ≈ 67.9
        public const float DurNoise = 0.3f;          // 用时的对数正态噪声
        // 道具消耗（M3，估计值，等实测再标定）：每人每个节点喝几瓶治疗；强化剂在 Boss 战里大约覆盖 60% 的时间；瘴气期每段路 25% 的概率要用净化器
        static float HealUse(NodeType t) => t == NodeType.Pack ? 0.2f : t == NodeType.Stronghold ? 0.5f : t == NodeType.Camp ? 0.8f : t == NodeType.Rift || t == NodeType.Messenger ? 2f : 1.5f;
        public const float TonicUptime = 0.6f, PurifierUsePerLeg = 0.25f;
        // 倒地风险率（每秒，和等级、侵蚀基本无关）。每次倒地救起刷一只残像，打死给经验和 Buff 计数（实测额外击杀的主要来源之一）
        public const float DMob = 0.0027f, DNest = 0.0105f, DRift = 0.0102f, DWild = 0.0054f, DMessenger = 0.0185f;
        public const float ChengyinRescue = 0.4f;    // 澄音救起的比例（她救起的少刷一级残像）
        static readonly float[] ResidualHp = { 1f, 4f, 6f, 9f };   // SliceGame.SpawnResidual
        // Boss 血量降到 70% 和 35% 时各召唤 3 只残像（BossController.TickSummon，不吃侵蚀倍率）

        public class Config
        {
            public RunDirector.Strategy strategy = RunDirector.Strategy.稳健发育;
            public int seed = 1;
            public float minutes = 30f;
            public float[] tierExp = { Growth.TierExp(0), Growth.TierExp(1), Growth.TierExp(2), Growth.TierExp(3) };
            public float messengerExp = Growth.MessengerExp;
            public int purifyForMax = Growth.PurifyForMax;
            public float tempo = Tempo * PostM3Boost, hazard = HazardScale;   // 全局校正（回测拟合，见 Calibrate）；M3 之后再乘清怪加速（CalibrateM3）
            public bool economy = true;                          // M3：贝币、商人、道具、武器品质（关掉 = M2 时的模型，回测用）
            public float coinBase = Economy.CoinBase;
            public bool finale = true, vortex = true;            // vortex = false：路线 AI 不打漩涡（对照组，看漩涡值不值）                           // M5：黑潮之心开放后按路线条件去打黑潮王，打完（或团灭）这局结束
            public float sovereignHp = RunDirector.SovereignHpMul, sovereignHazard = SovereignHazard;
        }
        // 终局（M5）的估计值，等实测回标：有信使声骸时黑潮王战输出 ×1.3（三人每 11 秒一发 ×2.5 的声骸 + 打断省下的时间），风险 ×0.6（王之咏唱基本都能打断）
        // 黑潮王的风险率：信使的风险率在回测里被高估（模拟贪信使团灭 2.8 次，实测 0 次），不能直接借用；按设计目标「有声骸约 75% 胜率」扫出来，实测后回标
        public const float HSovereign = HMessenger, DSovereign = DMessenger, EchoKingRate = 1.3f, EchoKingHazard = 0.6f;
        // 菜单「终局：扫黑潮王血量」2026-10-09：血量 ×3、风险 0.5 → 稳健路线有声骸 252 秒 / 胜率 89%、没声骸 59%；激进 235 秒 / 70%；贪信使 233 秒 / 73%（估计值，跑局后回标）
        public static float SovereignHazard = 0.5f;
        // 全局校正：单节点公式拟合出来的用时和风险，在整局里系统性偏慢、偏危险（邻近节点的敌人会被一起清掉、实测样本里激进路线的团灭偏多）。
        // 用回测拟合两个总系数，让三条路线的等级检查点、净化进度和团灭数贴合实测（菜单「回测标定」，2026-10-09：误差 25 → 8.3）。
        // 已知局限：激进路线实测 22 分钟前团灭 8 次，模拟只有 2–3 次，模拟会低估激进路线的风险（实测每条路线只有一局，也可能是那局运气差）
        public const float Tempo = 1.6f, HazardScale = 0.8f;   // 2026-10-09 用两组共 6 局实测重新标定（原为 1.4 / 1.0）

        public class Enemy { public int tier; public float hp, maxHp; public bool boss, s70, s35; }
        public class Sample { public float t, erosion, level, exp, coins, earned, star; public int purified, killsQ, killsJ, killsN, wipes, cleared, downs; }
        public class Event { public float t; public string kind, node, detail; }

        public class Result
        {
            public Config cfg;
            public readonly List<Sample> samples = new List<Sample>();
            public readonly List<Event> events = new List<Event>();
            public readonly Dictionary<NodeType, List<float>> clearTimes = new Dictionary<NodeType, List<float>>();
            public int wipes, purified;
            // 终局
            public bool reachedFinale, victory, echo, forced, vesselUsed;
            public float vortexMin = -1f;
            public float finaleMin = -1f, finaleSec, finaleLv;
            // 经济（三人平均）：总收入、总花费、剩余；按原稿「约能买 75%」统计各品质买了限购量的几成
            public Kit[] kits;
            public int shopVisits;
            public float Avg(System.Func<Kit, float> f) { float s = 0f; foreach (var k in kits) s += f(k); return s / kits.Length; }
            public float BoughtShare(Rarity r)
            {
                float b = 0f, lim = 0f;
                foreach (var d in Economy.Items)
                {
                    if (d.rarity != r) continue;
                    if (d.team) { b += Economy.teamBoughtSim; lim += d.limit; continue; }
                    foreach (var k in kits) { b += k.Bought(d.id); lim += d.limit; }
                }
                return lim > 0f ? b / lim : 0f;
            }
            public float LevelAt(float minute)
            {
                Sample best = samples.Count > 0 ? samples[0] : null;
                foreach (var s in samples) if (s.t <= minute) best = s;
                return best != null ? best.level : 1f;
            }
            public float MinuteOf(string kind, string node = null, int nth = 1)
            {
                int k = 0;
                foreach (var e in events) if (e.kind == kind && (node == null || e.node == node) && ++k == nth) return e.t;
                return -1f;
            }
            public float LevelAfter(string kind, string node = null, int nth = 1)
            {
                float t = MinuteOf(kind, node, nth);
                return t < 0f ? -1f : LevelAt(t + 0.01f);
            }
        }

        // ---------------- 节点里的敌人（和 RunDirector.Spawn 一致） ----------------
        public static List<Enemy> Compose(NodeDef d, System.Random rng)
        {
            var L = new List<Enemy>();
            void Q(int n) { for (int i = 0; i < n; i++) L.Add(new Enemy { tier = 0, hp = 1f }); }
            void J(int n, float hp) { for (int i = 0; i < n; i++) L.Add(new Enemy { tier = 1, hp = hp }); }
            void Qh(float hp) => L.Add(new Enemy { tier = 0, hp = hp });   // 第四批轻波兵种 / 孵化囊（按血量系数）
            switch (d.type)
            {
                case NodeType.Pack: Q(3 + 2); Qh(rng.Next(2) == 0 ? 0.6f : 1.5f); break;   // 3 爪兵 + 潮囊或盾卫 + 2 刺鳐
                case NodeType.Stronghold: J(1, 7f); Q(3 + 1); Qh(1.5f); Qh(0.8f); break;   // 较弱巨浪 + 3 爪兵 + 盾卫 + 咏唱者 + 1 刺鳐
                case NodeType.Camp: J(1, 6f); for (int k = 0; k < 3; k++) Q(3 + rng.Next(0, 2) + 1); break;
                case NodeType.Nest: J(2, 10f); J(1, 8f); J(1, 6f); J(1, 7f); Qh(8f); Q(2); break;   // 2 重甲 + 剑士 + 投矛手 + 祭司 + 孵化囊（估计孵出 2 只）
                case NodeType.WildBoss: J(1, 8f); J(1, 6.4f); L.Add(new Enemy { tier = 2, hp = BossHp, boss = true }); break;
                case NodeType.Rift: Q(6 + 2); Qh(1.5f); Qh(1.5f); Qh(0.8f); Qh(0.8f); J(2, 6f); J(1, 3.6f); J(1, 4.2f); Qh(8f); Q(2); L.Add(new Enemy { tier = 2, hp = BossHp, boss = true }); break;
                case NodeType.Messenger: L.Add(new Enemy { tier = 2, hp = BossHp * 2f, boss = true }); break;
            }
            foreach (var e in L) e.maxHp = e.hp;
            // 杂兵先死、Boss 最后（范围技能先清小的）
            L.Sort((a, b) => a.boss == b.boss ? a.hp.CompareTo(b.hp) : a.boss ? 1 : -1);
            return L;
        }

        static float Hazard(NodeType t) =>
            t == NodeType.Rift ? HRift : t == NodeType.Messenger ? HMessenger : t == NodeType.Nest ? HNest : t == NodeType.WildBoss || t == NodeType.Vortex ? HWild : HMob;
        static float DownRate(NodeType t) =>
            t == NodeType.Rift ? DRift : t == NodeType.Messenger ? DMessenger : t == NodeType.Nest ? DNest : t == NodeType.WildBoss || t == NodeType.Vortex ? DWild : DMob;
        static float ErosionBonus(NodeType t) => t == NodeType.Nest ? 10f : t == NodeType.Messenger ? 20f : 0f;
        static float Gauss(System.Random r) => Mathf.Sqrt(-2f * Mathf.Log(1f - (float)r.NextDouble())) * Mathf.Cos(2f * Mathf.PI * (float)r.NextDouble());

        // ---------------- 跑一局 ----------------
        public static Result Run(Config cfg)
        {
            var R = new Result { cfg = cfg };
            var rng = new System.Random(cfg.seed);
            var defs = new List<NodeDef>();
            NodeDef heartDef = null;
            foreach (var d in MapLayout.Nodes) { if (d.type == NodeType.Heart) heartDef = d; else if (d.type != NodeType.Merchant) defs.Add(d); }
            float forceAt = -1f;
            var left = new Dictionary<NodeDef, List<Enemy>>();
            foreach (var d in defs) left[d] = Compose(d, rng);
            var done = new HashSet<NodeDef>();

            float t = 0f, erosion = 0f, speed = 1f, exp = 0f, nextSample = 0f;
            // 漩涡和净世之器（和 RunDirector 一致）
            NodeDef vortexDef = null, weakDef = null;
            bool vortexDone = false, vessel = false, vortexFight = false;
            float vesselUntil = -1f, savedE = 0f;
            int level = 1, wipes = 0, purified = 0, kq = 0, kj = 0, kn = 0, downs = 0;
            Vector2 pos = MapLayout.Spawn;
            float M() => t / 60f;
            // ---- 经济（和实时跑局同一套规则：Economy） ----
            Economy.Reset(); Economy.teamBoughtSim = 0;
            var ids = new[] { HeroId.Feilie, HeroId.Qinglan, HeroId.Chengyin };
            var kits = new[] { new Kit(), new Kit(), new Kit() };
            R.kits = kits;
            var shops = new List<(NodeDef d, Shop s)>();
            var srng = new System.Random(MapBuilder.Seed + 31);
            foreach (var d in MapLayout.Nodes) if (d.type == NodeType.Merchant) shops.Add((d, Economy.MakeShop(d.id, d.id == "M5", srng)));
            float WM() { if (!cfg.economy) return 1f; float s = 0f; foreach (var k in kits) s += Economy.WeaponMult(k.weaponStar); return s / kits.Length; }
            void GiveCoin(float mult)
            {
                if (!cfg.economy) return;
                foreach (var k in kits) k.Earn(cfg.coinBase * mult * Growth.TeamShare);
            }
            void Weapon(int star) { Kit lo = kits[0]; foreach (var k in kits) if (k.weaponStar < lo.weaponStar) lo = k; if (star > lo.weaponStar) lo.weaponStar = star; }
            int Round(float x) => (int)x + (rng.NextDouble() < x - (int)x ? 1 : 0);
            int keys = 0;   // 巢穴钥匙（和 RunDirector 一致：营地 0–3、据点 1；巢穴要一把）
            var unlocked = new HashSet<NodeDef>();
            float Lv() => level >= Growth.MaxLevel ? Growth.MaxLevel : level + Mathf.Min(0.99f, exp / Growth.ExpNeed(level));
            void Ev(string kind, string node, string detail) => R.events.Add(new Event { t = M(), kind = kind, node = node, detail = detail });
            float Coverage() => MapLayout.RadiusForCoverage(MapLayout.CoverageAt(M()));
            float ErosionAt(Vector2 p) => MapLayout.TideDist(p) <= Coverage() ? erosion : 0f;
            void SampleNow()
            {
                R.samples.Add(new Sample { t = M(), erosion = erosion, level = Lv(), exp = exp, purified = purified, killsQ = kq, killsJ = kj, killsN = kn, wipes = wipes, cleared = done.Count, downs = downs,
                    coins = R.Avg(k => k.coins), earned = R.Avg(k => k.earned), star = R.Avg(k => k.weaponStar) });
            }
            void Advance(float dt)
            {
                while (dt > 0f)
                {
                    float step = Mathf.Min(dt, 1f);
                    if (vesselUntil > t) erosion = 0f;
                    else
                    {
                        if (vesselUntil > 0f) { erosion = savedE; vesselUntil = -1f; }
                        erosion = Mathf.Clamp(erosion + RunDirector.ErosionRate(M()) * step / 60f * speed * (vortexFight ? 1.5f : 1f), 0f, 100f);
                    }
                    t += step; dt -= step;
                    if (t >= nextSample) { nextSample += 5f; SampleNow(); }
                }
            }
            void GiveExp(float raw)
            {
                exp += raw * Growth.TeamShare;
                int cap = cfg.purifyForMax > 0 && purified < cfg.purifyForMax ? Growth.MaxLevel - 1 : Growth.MaxLevel;
                while (level < cap && exp >= Growth.ExpNeed(level)) { exp -= Growth.ExpNeed(level); level++; Ev("levelup", "", level.ToString()); }
                if (level >= Growth.MaxLevel) exp = 0f;
            }

            SampleNow();
            float end = cfg.minutes * 60f;
            while (t < end)
            {
                // 商人（和 RunDirector.PickNext / TickShops 一致）：领队有钱有想买的，60 米内有没逛过（或 5 分钟前逛过）的商人就先去
                if (cfg.economy && Economy.WantsShop(kits[0], erosion, M(), true, ids[0]))
                {
                    (NodeDef d, Shop s) go = (null, null); float gd = RunDirector.ShopDetour;
                    foreach (var sh in shops)
                    {
                        if (!(sh.s.visits == 0 || M() - sh.s.lastVisit >= RunDirector.ShopRevisitMin)) continue;
                        float dd = Vector2.Distance(pos, sh.d.pos);
                        if (dd < gd) { gd = dd; go = sh; }
                    }
                    if (go.s != null)
                    {
                        Advance(gd / TravelSpeed + 8f);
                        pos = go.d.pos;
                        go.s.visits++; go.s.lastVisit = M(); R.shopVisits++;
                        for (int i = 0; i < 3; i++) Economy.AutoBuy(kits[i], go.s, ids[i], kits, erosion, M(), true);
                        Ev("shop", go.d.id, $"coins={kits[0].coins:0}");
                        continue;
                    }
                }

                // 选下一个节点（和实时跑局同一个打分）
                NodeDef target = null; float bs = float.MaxValue;
                bool msgrDone = false; foreach (var d in done) if (d.type == NodeType.Messenger) msgrDone = true;
                bool forcedNow = false;
                if (cfg.finale && vortexDef == null && M() >= MapLayout.VortexAt)
                {
                    var spot = MapLayout.VortexSpot(pos, d => done.Contains(d));
                    vortexDef = new NodeDef("V1", "黑潮漩涡", NodeType.Vortex, spot.x, spot.y, MapLayout.Vortex.radius, 2);
                    var vl = new List<Enemy> { new Enemy { tier = 2, hp = BossHp * 1.3f, maxHp = BossHp * 1.3f, boss = true } };
                    for (int i = 0; i < 12; i++) vl.Insert(0, new Enemy { tier = 0, hp = 1f, maxHp = 1f });
                    left[vortexDef] = vl;
                }
                if (cfg.finale && heartDef != null)
                {
                    if (erosion >= 100f && forceAt < 0f) forceAt = t + 60f;
                    forcedNow = forceAt > 0f && t >= forceAt;
                    bool vortexNear = cfg.vortex && vortexDef != null && !vortexDone && Vector2.Distance(pos, vortexDef.pos) < RunDirector.VortexDetour;
                    if (!forcedNow && vortexNear) target = vortexDef;
                    else if (forcedNow || (purified >= RunDirector.PurifyToOpen && RunDirector.FinaleReady(cfg.strategy, Lv(), M(), purified, msgrDone, erosion)))
                    {
                        target = heartDef;
                        float hpK = BossHp * cfg.sovereignHp * (weakDef == heartDef ? 0.9f : 1f);
                        if (!left.ContainsKey(heartDef)) left[heartDef] = new List<Enemy> { new Enemy { tier = 3, hp = hpK, maxHp = BossHp * cfg.sovereignHp, boss = true } };
                        if (forcedNow) pos = heartDef.pos;   // 强制终局：直接拉到门前
                    }
                }
                if (target == null)
                foreach (var d in defs)
                {
                    if (done.Contains(d)) continue;
                    if (cfg.economy && d.type == NodeType.Nest && !unlocked.Contains(d) && keys == 0) continue;
                    float s = RunDirector.RouteScore(d, Vector2.Distance(pos, d.pos), M(), cfg.strategy);
                    if (s < bs) { bs = s; target = d; }
                }
                if (target == null) { Advance(end - t); break; }

                if (cfg.economy && target.type == NodeType.Nest && !unlocked.Contains(target)) { keys--; unlocked.Add(target); }
                // 赶路
                float dist = Vector2.Distance(pos, target.pos);
                Advance(Mathf.Max(2f, (dist - EngageRange) / TravelSpeed) * Mathf.Exp(0.2f * Gauss(rng)));
                pos = target.pos;
                if (cfg.economy && M() >= Miasma.StartMin && M() < Miasma.EndMin)
                    foreach (var k in kits) if (k.Has("purifier") > 0 && rng.NextDouble() < PurifierUsePerLeg) k.Take("purifier");

                // 打：每 2 秒一个刻度，按当前等级和侵蚀算进度；按风险率掷团灭
                var foes = left[target];
                float noise = Mathf.Exp(DurNoise * Gauss(rng));
                float overhead = (ClearBase + (target.type == NodeType.Camp ? CampExtra : 0f)) * noise;
                Advance(overhead);
                bool wiped = false;
                vortexFight = target == vortexDef;
                // 黑潮王的血量在刷出来时按当时的侵蚀定死（和游戏一致），之后净世之器把侵蚀清零只降它的伤害
                float hmKing = Coop.HpMultFor(Mathf.Min(100f, ErosionAt(target.pos)));
                if (target == heartDef && vessel) { vessel = false; R.vesselUsed = true; savedE = erosion; erosion = 0f; vesselUntil = t + 90f; }
                float tonicBoost = 1f;
                if (cfg.economy)
                {
                    bool big = target.type != NodeType.Pack && target.type != NodeType.Stronghold;
                    bool bossy = big && target.type != NodeType.Camp;
                    int tonics = 0;
                    foreach (var k in kits)
                    {
                        if (bossy && k.Take("tonic")) tonics++;
                        if (big && k.Take("bomb"))
                        {
                            // 炸弹倍率 9 ≈ 1.6 只轻波的血（6 级、2★）；杂兵战一次炸到约 3 只
                            float one = 1.6f * Growth.DmgRaw(level) / Growth.DmgRaw(Growth.BaseLevel) * WM();
                            foes[0].hp = Mathf.Max(1e-3f, foes[0].hp - one * (foes[0].boss ? 1f : 3f));
                        }
                        if (bossy && erosion >= 45f) k.Take("stab");
                    }
                    tonicBoost = 1f + 0.15f * tonics / 3f * TonicUptime;
                    if (bossy && Economy.teamItemCount > 0 && rng.NextDouble() < 0.6) Economy.teamItemCount = 0;
                }
                float fightStart = t;
                int nodeDowns = 0;
                while (foes.Count > 0 && t < end)
                {
                    float e = Mathf.Min(100f, ErosionAt(target.pos) + ErosionBonus(target.type));
                    float hm = Coop.HpMultFor(e);
                    float k = foes[foes.Count - 1].boss ? KBoss : KMob;
                    bool king = target == heartDef;
                    if (king && msgrDone) k *= EchoKingRate;
                    float rate = cfg.tempo * k * Mathf.Pow(Growth.DmgRaw(level), LevelExp) * WM() * tonicBoost / ((king ? hmKing : hm) * noise);   // 每秒打掉多少只轻波的血（侵蚀前的单位）；武器品质、强化剂只乘伤害
                    float x = (1f + (hm - 1f) * 0.5f) / Growth.HpRaw(level);
                    const float dt = 2f;
                    float budget = rate * dt;
                    // 不吃侵蚀倍率的敌人（召唤物、残像）按当前倍率折算进同一单位
                    void AddUnscaled(int tier, float hp) => foes.Insert(0, new Enemy { tier = tier, hp = hp / hm, maxHp = hp / hm });
                    while (budget > 0f && foes.Count > 0)
                    {
                        var f = foes[0];
                        float take = Mathf.Min(budget, f.hp);
                        f.hp -= take; budget -= take;
                        if (king)
                        {
                            // 黑潮王：二、三阶段各补 3 根触手（每根约 5 只轻波的血，吃侵蚀倍率）
                            if (f.boss && !f.s70 && f.hp < f.maxHp * 0.6f) { f.s70 = true; for (int i = 0; i < 3; i++) foes.Insert(0, new Enemy { tier = 0, hp = 5f, maxHp = 5f }); }
                            if (f.boss && !f.s35 && f.hp < f.maxHp * 0.3f) { f.s35 = true; for (int i = 0; i < 3; i++) foes.Insert(0, new Enemy { tier = 0, hp = 5f, maxHp = 5f }); }
                        }
                        else
                        {
                        if (f.boss && !f.s70 && f.hp < f.maxHp * 0.7f) { f.s70 = true; for (int i = 0; i < 3; i++) AddUnscaled(0, 1f); }
                        if (f.boss && !f.s35 && f.hp < f.maxHp * 0.35f) { f.s35 = true; for (int i = 0; i < 3; i++) AddUnscaled(0, 1f); }
                        }
                        if (f.hp <= 1e-4f)
                        {
                            foes.Remove(f);
                            if (f.tier >= 2) kn++; else if (f.tier == 1) kj++; else kq++;
                            GiveExp(f.boss && target.type == NodeType.Messenger ? cfg.messengerExp : cfg.tierExp[Mathf.Min(f.tier, 3)]);
                            GiveCoin(f.boss && target.type == NodeType.Messenger ? 70f : f.tier >= 3 ? 80f : f.tier >= 2 ? 30f : f.tier == 1 ? 5f : 1f);
                            if (cfg.economy && f.tier == 0 && rng.NextDouble() < Economy.LightWeaponChance) Weapon(3);
                        }
                    }
                    Advance(dt);
                    float hz = king ? HSovereign * cfg.sovereignHazard * (msgrDone ? EchoKingHazard : 1f) : Hazard(target.type);
                    if (foes.Count > 0 && rng.NextDouble() < 1f - Mathf.Exp(-cfg.hazard * hz * Mathf.Pow(x, HazardP) * dt)) { wiped = true; break; }
                    // 倒地 → 救起时刷残像：同一个人在本节点第几次倒地决定残像强度（三人平摊），澄音救起的降一级
                    if (foes.Count > 0 && rng.NextDouble() < 1f - Mathf.Exp(-(king ? DSovereign : DownRate(target.type)) * dt))
                    {
                        nodeDowns++; downs++;
                        int idx = Mathf.Min(3, (nodeDowns - 1) / 3) - (rng.NextDouble() < ChengyinRescue ? 1 : 0);
                        if (idx >= 0) AddUnscaled(idx == 0 ? 0 : 1, ResidualHp[idx]);
                    }
                }

                vortexFight = false;
                if (target == heartDef && (foes.Count == 0 || wiped))
                {
                    R.reachedFinale = true; R.victory = foes.Count == 0; R.echo = msgrDone; R.forced = forcedNow;
                    R.finaleMin = fightStart / 60f; R.finaleSec = t - fightStart + overhead; R.finaleLv = R.LevelAt(R.finaleMin);
                    if (wiped) wipes++;
                    Ev(R.victory ? "victory" : "defeat", heartDef.id, $"{R.finaleSec:0}s");
                    SampleNow();
                    break;
                }
                if (foes.Count == 0)
                {
                    done.Add(target);
                    float dur = t - fightStart + overhead;
                    if (!R.clearTimes.TryGetValue(target.type, out var lst)) R.clearTimes[target.type] = lst = new List<float>();
                    lst.Add(dur);
                    Ev("clear", target.id, $"{dur:0}s");
                    if (cfg.economy)
                    {
                        Weapon(Economy.NodeWeapon(target.type, rng));
                        keys += target.type == NodeType.Camp ? RunDirector.CampKeys(rng) : target.type == NodeType.Stronghold ? 1 : 0;
                        float hu = HealUse(target.type);
                        foreach (var k in kits) for (int i = Round(hu); i > 0; i--) k.Take("heal");
                    }
                    if (target == vortexDef)
                    {
                        // 漩涡奖励：增速永久 −10%，最近的 Boss 节点血量 −10%，净世之器
                        vortexDone = true; vessel = true; R.vortexMin = M();
                        speed = Mathf.Max(0.5f, speed - 0.1f);
                        float bd = float.MaxValue;
                        foreach (var d in defs)
                        {
                            if (done.Contains(d) || (d.type != NodeType.WildBoss && d.type != NodeType.Rift && d.type != NodeType.Messenger)) continue;
                            float dd = Vector2.Distance(d.pos, vortexDef.pos); if (dd < bd) { bd = dd; weakDef = d; }
                        }
                        if (heartDef != null && Vector2.Distance(heartDef.pos, vortexDef.pos) < bd) weakDef = heartDef;
                        if (weakDef != null && left.TryGetValue(weakDef, out var wl)) foreach (var en in wl) if (en.boss) en.hp *= 0.9f;
                        Ev("vortex", vortexDef.id, $"weak={(weakDef != null ? weakDef.id : "-")}");
                    }
                    if (target.type == NodeType.Rift)
                    {
                        purified++;
                        erosion = Mathf.Max(0f, erosion - 15f);
                        GiveExp(0f);
                        Ev("purify", target.id, $"erosion={erosion:0}");
                    }
                }
                else if (wiped)
                {
                    // 团灭（和 RunDirector.OnWipe 一致）：首次增速 +5%，之后每次 +10% 并且侵蚀 +15%；3 秒后在离黑潮最远的已清节点复活
                    wipes++;
                    speed += wipes == 1 ? 0.05f : 0.1f;
                    if (wipes >= 2) erosion = Mathf.Min(100f, erosion + 15f);
                    Ev("wipe", target.id, $"lv={Lv():0.0}");
                    if (cfg.economy) foreach (var k in kits) while (k.Take("heal")) { }   // 团灭前治疗剂基本都喝光了
                    Advance(3f);
                    Vector2 safe = MapLayout.Spawn; float far = -1f;
                    foreach (var d in done) if (MapLayout.TideDist(d.pos) > far) { far = MapLayout.TideDist(d.pos); safe = d.pos; }
                    pos = safe;
                }
            }
            R.wipes = wipes; R.purified = purified;
            Economy.teamBoughtSim = Economy.teamBought;
            return R;
        }

        // ---------------- 批量：三条路线 × N 个种子，输出汇总 ----------------
        public static string Batch(int seeds, float minutes, out List<Result> all, System.Action<Config> tweak = null)
        {
            all = new List<Result>();
            var sb = new StringBuilder();
            sb.AppendLine($"公式模拟：三条路线 × {seeds} 个种子，每局 {minutes:0} 分钟（原稿时长）");
            var c0 = new Config(); tweak?.Invoke(c0);
            sb.AppendLine($"经验：轻波 {c0.tierExp[0]} 巨浪 {c0.tierExp[1]} 怒涛 {c0.tierExp[2]} 信使 {c0.messengerExp}；10 级门槛 {(c0.purifyForMax > 0 ? "开" : "关")}");
            sb.AppendLine(c0.economy ? $"经济：开，轻波 {c0.coinBase} 贝币，基础道具 {Economy.BasicPrice}" : "经济：关（M2 模型）");
            foreach (RunDirector.Strategy st in System.Enum.GetValues(typeof(RunDirector.Strategy)))
            {
                var rs = new List<Result>();
                for (int s = 1; s <= seeds; s++)
                {
                    var c = new Config { strategy = st, seed = s * 7919 + (int)st, minutes = minutes };
                    tweak?.Invoke(c);
                    rs.Add(Run(c));
                }
                all.AddRange(rs);
                string Stat(System.Func<Result, float> f)
                {
                    var v = new List<float>();
                    foreach (var r in rs) { float x = f(r); if (x >= 0f) v.Add(x); }
                    if (v.Count == 0) return "—";
                    v.Sort();
                    float mean = 0f; foreach (var x in v) mean += x; mean /= v.Count;
                    return $"{mean:0.0}（{v[(int)(v.Count * 0.1f)]:0.0}–{v[Mathf.Min(v.Count - 1, (int)(v.Count * 0.9f))]:0.0}{(v.Count < rs.Count ? $"，{v.Count}/{rs.Count} 局达到" : "")}）";
                }
                sb.AppendLine($"\n== {st}");
                sb.AppendLine($"  等级  3 分钟 {Stat(r => r.LevelAt(3f))}   7 分钟 {Stat(r => r.LevelAt(7f))}   10.5 分钟 {Stat(r => r.LevelAt(10.5f))}   18 分钟 {Stat(r => r.LevelAt(18f))}   25 分钟 {Stat(r => r.LevelAt(25f))}");
                sb.AppendLine($"  第 2 次净化：第 {Stat(r => r.MinuteOf("purify", null, 2))} 分钟，此时 {Stat(r => r.LevelAfter("purify", null, 2))} 级");
                sb.AppendLine($"  打完信使：第 {Stat(r => r.MinuteOf("clear", "X1"))} 分钟，此时 {Stat(r => r.LevelAfter("clear", "X1"))} 级");
                sb.AppendLine($"  满 10 级：第 {Stat(r => { float m = -1f; foreach (var e in r.events) if (e.kind == "levelup" && e.detail == "10") m = e.t; return m; })} 分钟");
                sb.AppendLine($"  团灭 {Stat(r => r.wipes)} 次   净化 {Stat(r => r.purified)} 个");
                if (rs[0].cfg.finale)
                {
                    sb.AppendLine($"  漩涡：打掉 {Stat(r => r.vortexMin)} 分钟   净世之器用在决战 {Stat(r => r.vesselUsed ? 100f : 0f)}%");
                    sb.AppendLine($"  终局：第 {Stat(r => r.reachedFinale ? r.finaleMin : -1f)} 分钟进入，{Stat(r => r.reachedFinale ? r.finaleLv : -1f)} 级，决战 {Stat(r => r.reachedFinale ? r.finaleSec : -1f)} 秒，胜率 {Stat(r => r.reachedFinale ? (r.victory ? 100f : 0f) : -1f)}%，强制 {Stat(r => r.reachedFinale ? (r.forced ? 100f : 0f) : -1f)}%，有信使声骸 {Stat(r => r.reachedFinale ? (r.echo ? 100f : 0f) : -1f)}%");
                }
                RunSim.Sample At(Result r, float m) { var b = r.samples[0]; foreach (var s in r.samples) if (s.t <= m) b = s; return b; }
                if (rs[0].cfg.economy)
                {
                    RunSim.Sample Sm(Result r, float m) { var b = r.samples[0]; foreach (var s in r.samples) if (s.t <= m) b = s; return b; }
                    sb.AppendLine($"  贝币（每人）：收入 7 分钟 {Stat(r => Sm(r, 7f).earned)}  18 分钟 {Stat(r => Sm(r, 18f).earned)}  全局 {Stat(r => r.Avg(k => k.earned))}；花掉 {Stat(r => r.Avg(k => k.spent))}；剩 {Stat(r => r.Avg(k => k.coins))}");
                    sb.AppendLine($"  买到限购量的比例：基础 {Stat(r => r.BoughtShare(Rarity.基础) * 100f)}%  珍稀 {Stat(r => r.BoughtShare(Rarity.珍稀) * 100f)}%  传说（团队） {Stat(r => r.BoughtShare(Rarity.传说) * 100f)}%   逛商人 {Stat(r => r.shopVisits)} 次");
                    sb.AppendLine($"  增益解锁次数（团队计数 轻波 1 / 巨浪 8 / 怒涛 30）：18 分钟 {Stat(r => Rogue.Unlocks(BuffPoints(Sm(r, 18f))))}  22 分钟 {Stat(r => Rogue.Unlocks(BuffPoints(Sm(r, 22f))))}  25 分钟 {Stat(r => Rogue.Unlocks(BuffPoints(Sm(r, 25f))))}");
                    sb.AppendLine($"  武器（三人平均星级）：7 分钟 {Stat(r => Sm(r, 7f).star)}  18 分钟 {Stat(r => Sm(r, 18f).star)}  25 分钟 {Stat(r => Sm(r, 25f).star)}");
                }
                sb.AppendLine($"  第 22 分钟：轻波击杀 {Stat(r => At(r, 22f).killsQ)}  巨浪 {Stat(r => At(r, 22f).killsJ)}  怒涛 {Stat(r => At(r, 22f).killsN)}  倒地 {Stat(r => At(r, 22f).downs)}  净化 {Stat(r => At(r, 22f).purified)}");
                var ct = new StringBuilder("  清点用时（秒，平均）：");
                foreach (NodeType nt in System.Enum.GetValues(typeof(NodeType)))
                {
                    var v = new List<float>();
                    foreach (var r in rs) if (r.clearTimes.TryGetValue(nt, out var l)) v.AddRange(l);
                    if (v.Count == 0) continue;
                    float m = 0f; foreach (var x in v) m += x;
                    ct.Append($"{NodeName(nt)} {m / v.Count:0}  ");
                }
                sb.AppendLine(ct.ToString());
            }
            return sb.ToString();
        }

        public static int BuffPoints(Sample s) => Rogue.Points(s.killsQ, s.killsJ, s.killsN);

        // ---------------- 回测标定：用实测局拟合 Tempo 和 HazardScale ----------------
        // 每组实测 = 当时的经验值 + 三条路线（稳健发育、激进净化、贪信使）各一局：
        // 等级（3、7、10.5、18 分钟）、净化数（18、22 分钟）、第 22 分钟前的团灭次数
        class Measured
        {
            public string name; public float[] tierExp; public float messengerExp;
            public float[][] lv, pur; public float[] wipes22;
        }
        static readonly Measured[] Sets =
        {
            // 2026-10-08 晚：网页原型经验值。Captures/runs/run_20261008_232133 / 232601 / 232902
            new Measured { name = "旧经验 10/80/450", tierExp = new[] { 10f, 80f, 450f, 800f }, messengerExp = 900f,
                lv = new[] { new[] { 3.41f, 5.09f, 6.60f, 9.26f }, new[] { 3.41f, 5.53f, 6.63f, 8.10f }, new[] { 3.76f, 5.16f, 5.81f, 8.15f } },
                pur = new[] { new[] { 2f, 2f }, new[] { 2f, 2f }, new[] { 1f, 1f } }, wipes22 = new[] { 2f, 8f, 3f } },
            // 2026-10-09：原稿比例经验值。Captures/runs/run_20261009_105723 / 110031 / 110336
            new Measured { name = "新经验 11/55/330", tierExp = new[] { 11f, 55f, 330f, 880f }, messengerExp = 770f,
                lv = new[] { new[] { 3.17f, 5.44f, 6.67f, 8.30f }, new[] { 3.72f, 5.63f, 7.25f, 8.91f }, new[] { 3.65f, 5.13f, 5.40f, 8.87f } },
                pur = new[] { new[] { 1f, 1f }, new[] { 3f, 3f }, new[] { 2f, 2f } }, wipes22 = new[] { 1f, 1f, 0f } },
        };
        static readonly float[] LvAt = { 3f, 7f, 10.5f, 18f }, PurAt = { 18f, 22f };

        public static string Calibrate(int seeds = 40)
        {
            var sb = new StringBuilder("回测标定（误差 = 等级差² 之和 + 0.5 × 净化数差² 之和 + 0.1 × 团灭数差² 之和）\n");
            float best = float.MaxValue, bt = 1f, bh = 1f;
            float[] tempos = { 1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.8f };
            float[] hazards = { 0.4f, 0.6f, 0.8f, 1f, 1.2f, 1.5f };
            foreach (var tp in tempos)
                foreach (var hz in hazards)
                {
                    float err = Backtest(tp, hz, seeds, null);
                    if (err < best) { best = err; bt = tp; bh = hz; }
                }
            sb.AppendLine($"最优 Tempo = {bt}，HazardScale = {bh}，误差 {best:0.00}（不校正时 {Backtest(1f, 1f, seeds, null):0.00}）");
            Backtest(bt, bh, seeds, sb);
            return sb.ToString();
        }

        static float Backtest(float tempo, float hazard, int seeds, StringBuilder log)   // 实测局都在 M3 之前，回测时关掉经济
        {
            float err = 0f;
            foreach (var m in Sets)
            {
                log?.AppendLine($" 【{m.name}】");
                for (int si = 0; si < 3; si++)
                {
                    var st = (RunDirector.Strategy)si;
                    var lv = new float[4]; var pur = new float[2]; float wp = 0f;
                    for (int s = 1; s <= seeds; s++)
                    {
                        var r = Run(new Config { strategy = st, seed = s * 7919 + si, minutes = 22.5f, tierExp = m.tierExp, messengerExp = m.messengerExp, purifyForMax = 0, economy = false, finale = false, tempo = tempo, hazard = hazard });
                        for (int i = 0; i < 4; i++) lv[i] += r.LevelAt(LvAt[i]) / seeds;
                        for (int i = 0; i < 2; i++) { var b = r.samples[0]; foreach (var x in r.samples) if (x.t <= PurAt[i]) b = x; pur[i] += b.purified / (float)seeds; }
                        { var b = r.samples[0]; foreach (var x in r.samples) if (x.t <= 22f) b = x; wp += b.wipes / (float)seeds; }
                    }
                    for (int i = 0; i < 4; i++) err += (lv[i] - m.lv[si][i]) * (lv[i] - m.lv[si][i]);
                    for (int i = 0; i < 2; i++) err += 0.5f * (pur[i] - m.pur[si][i]) * (pur[i] - m.pur[si][i]);
                    err += 0.1f * (wp - m.wipes22[si]) * (wp - m.wipes22[si]);
                    log?.AppendLine($"  {st}：等级 模拟 {lv[0]:0.00}/{lv[1]:0.00}/{lv[2]:0.00}/{lv[3]:0.00} 实测 {m.lv[si][0]}/{m.lv[si][1]}/{m.lv[si][2]}/{m.lv[si][3]}；净化 模拟 {pur[0]:0.0}/{pur[1]:0.0} 实测 {m.pur[si][0]}/{m.pur[si][1]}；团灭 模拟 {wp:0.0} 实测 {m.wipes22[si]}");
                }
            }
            return err;
        }

        // ---------------- M3 之后的回测：武器、增益、新兵种让清怪变快，模拟里没有逐项建模，用一个总的加速系数拟合 ----------------
        // 实测：2026-10-09 修复后三局（Captures/runs/run_20261009_162812 稳健、163112 激进、162303 贪信使），经验 11/55/330，信使 770
        static readonly float[][] M3Lv = { new[] { 4.10f, 5.61f, 8.10f, 9.10f }, new[] { 3.43f, 6.50f, 6.85f, 9.15f }, new[] { 3.43f, 4.36f, 6.99f, 8.10f } };
        // 第二组：经验 8.8（run_20261009_164259 稳健、164600 激进、164900 贪信使）
        static readonly float[][] M3Lv88 = { new[] { 3.42f, 5.92f, 7.27f, 8.42f }, new[] { 2.86f, 4.64f, 5.92f, 7.45f }, new[] { 3.81f, 6.19f, 7.72f, 9.11f } };
        static readonly float[] M3LvAt = { 3f, 7f, 10.5f, 14f };
        public static float PostM3Boost = 1.4f;   // 2026-10-09：修复后三局拟合 1.2；加上经验 8.8 的三局后误差在 1.3–1.5 几乎持平（每路线一局，噪声大），取 1.4。M2 时期的回测显式传 tempo，不受影响

        public static float[] LevelsFor(RunDirector.Strategy st, float boost, float expScale, int seeds, float[] at, out float finaleMin, out float finaleLv, out float win, out float forced)
        {
            var lv = new float[at.Length]; finaleMin = finaleLv = win = forced = 0f; int n = 0;
            for (int s = 1; s <= seeds; s++)
            {
                var c = new Config { strategy = st, seed = s * 7919 + (int)st, minutes = 30f, tempo = Tempo * boost };
                for (int i = 0; i < 4; i++) c.tierExp[i] *= expScale;
                c.messengerExp *= expScale;
                var r = Run(c);
                for (int i = 0; i < at.Length; i++) lv[i] += r.LevelAt(at[i]) / seeds;
                if (r.reachedFinale) { n++; finaleMin += r.finaleMin; finaleLv += r.finaleLv; if (r.victory) win++; if (r.forced) forced++; }
            }
            if (n > 0) { finaleMin /= n; finaleLv /= n; win /= n; forced /= n; }
            return lv;
        }

        public static string CalibrateM3(int seeds = 40)
        {
            var sb = new StringBuilder("M3 之后的回测：拟合清怪加速系数（误差 = 三局 3 / 7 / 10.5 / 14 分钟等级差² 之和）\n");
            float best = float.MaxValue, bb = 1f;
            foreach (float b in new[] { 1f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.8f, 2f })
            {
                float err = 0f;
                var line = new StringBuilder();
                // 两组实测：经验 11（按当前 ExpBase 换算倍数）和 8.8
                foreach (var (data, exp) in new[] { (M3Lv, 11f), (M3Lv88, 8.8f) })
                    for (int si = 0; si < 3; si++)
                    {
                        var lv = LevelsFor((RunDirector.Strategy)si, b, exp / Growth.ExpBase, seeds, M3LvAt, out _, out _, out _, out _);
                        for (int i = 0; i < 4; i++) err += (lv[i] - data[si][i]) * (lv[i] - data[si][i]);
                        line.Append($"{exp}:{(RunDirector.Strategy)si} {lv[0]:0.0}/{lv[1]:0.0}/{lv[2]:0.0}/{lv[3]:0.0}  ");
                    }
                sb.AppendLine($"加速 ×{b}：误差 {err:0.00}   {line}");
                if (err < best) { best = err; bb = b; }
            }
            sb.AppendLine($"最优加速 ×{bb}（误差 {best:0.00}）；实测 稳健 4.1/5.6/8.1/9.1  激进 3.4/6.5/6.9/9.2  贪信使 3.4/4.4/7.0/8.1");
            PostM3Boost = bb;
            return sb.ToString();
        }

        // 带着拟合的加速系数扫经验倍数：目标（原稿心流）3 分钟 3 级、7 分钟 5 级、10.5 分钟 6–6.2 级，终局前约 8.5–9 级
        public static string ExpSweep(float boost, int seeds = 40)
        {
            var sb = new StringBuilder($"经验扫描（清怪加速 ×{boost}）：三条路线平均\n经验倍数（轻波经验）| 3 / 7 / 10.5 / 14 分钟等级 | 进终局分钟 / 等级 | 胜率 | 强制终局\n");
            float[] at = { 3f, 7f, 10.5f, 14f };
            foreach (float k in new[] { 1f, 0.9f, 0.8f, 0.7f, 0.6f, 0.5f })
            {
                var lv = new float[4]; float fm = 0, fl = 0, w = 0, fo = 0;
                for (int si = 0; si < 3; si++)
                {
                    var l = LevelsFor((RunDirector.Strategy)si, boost, k, seeds, at, out var a, out var b, out var c, out var d);
                    for (int i = 0; i < 4; i++) lv[i] += l[i] / 3f;
                    fm += a / 3f; fl += b / 3f; w += c / 3f; fo += d / 3f;
                }
                sb.AppendLine($"×{k}（{Growth.ExpBase * k:0.0}）| {lv[0]:0.00} / {lv[1]:0.00} / {lv[2]:0.00} / {lv[3]:0.00} | 第 {fm:0.0} 分钟 / {fl:0.0} 级 | {w * 100:0}% | {fo * 100:0}%");
            }
            return sb.ToString();
        }

        public static string NodeName(NodeType t) => t switch
        {
            NodeType.Stronghold => "据点", NodeType.Camp => "营地", NodeType.Nest => "巢穴", NodeType.Rift => "裂隙",
            NodeType.WildBoss => "野图怒涛", NodeType.Messenger => "信使", NodeType.Pack => "残像群", NodeType.Vortex => "漩涡", _ => t.ToString()
        };
    }
}

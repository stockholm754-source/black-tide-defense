using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace BlackTide
{
    // 一个节点的运行时状态：队伍走近才刷敌人（先待命），清空即完成
    public class NodeRuntime
    {
        public NodeDef def;
        public enum St { Idle, Spawned, Active, Cleared }
        public St st;
        public readonly List<Damageable> enemies = new List<Damageable>();
        public BossController boss;
        public float spawnT = -1f, activeT = -1f, clearT = -1f;   // 一局时间（分钟）
        public Vector3 pos;                                       // 信使会游荡，其他节点就是 def 的位置
        public Shop shop;                                         // 商人节点的库存（M3）
        public bool locked;                                       // 精英巢穴：要一把钥匙才能挑战（M3）
        public bool weakened;                                     // 漩涡奖励：这里的 Boss 血量、伤害 −10%
        public int waves; public float nextWave;                  // 漩涡的召唤波次
        public Transform vortexFx;
        public Transform seal;
        public bool Done => st == St.Cleared;
        public bool Fightable => def.type != NodeType.Merchant && (def.type != NodeType.Heart || (RunDirector.I && RunDirector.I.heartOpen)) && (def.type != NodeType.Vortex || (RunDirector.I && RunDirector.I.vortexOpen));
        public NodeRuntime(NodeDef d) { def = d; pos = d.World; }
    }

    // 开放地图一局的总控（M1）：节点、侵蚀与黑潮扩散、路线 AI、团灭、时间轴记录
    public class RunDirector : MonoBehaviour
    {
        public static RunDirector I;

        // ---- 可调参数 ----
        public enum Strategy { 稳健发育, 激进净化, 贪信使 }
        public static Strategy strategy = Strategy.稳健发育;
        public static float LengthScale = 1f;                    // 一局时长系数：1 = 原稿（侵蚀 18 分钟满）；0.4 = 压缩版（F5）
        static readonly float[] Speeds = { 1f, 2f, 4f, 8f };      // F4：游戏倍速（加速跑局用）
        int speedIdx;

        // ---- 状态 ----
        public float runSeconds;                                 // 游戏内经过的秒数
        public float RunMinutes => runSeconds / 60f / LengthScale; // 换算到原稿的时间轴
        public float erosionSpeed = 1f;                          // 团灭会让黑潮加速（原稿：首次 +5%，同一挑战二次再 +10%）
        public int purified, wipes;
        public int PurifyRelief = 8;                             // 净化一个裂隙降的侵蚀百分点（割草方向重做：原 −15 会让黑潮被无限压回去）
        public int keys;                                         // 队伍的巢穴钥匙（营地宝箱 0–3 把，据点探员 1 把）
        readonly List<string> protocolsGiven = new List<string>();
        // ---- 终局（M5，设计见 Design/m5_finale.md）----
        public bool heartOpen, finaleStarted, ended, victory;
        // ---- 黑潮漩涡和净世之器（M5，原稿决胜期）----
        public bool vortexOpen;
        public int worldVessel;
        float vesselUntil = -1f, savedErosion;
        public bool VesselActive => vesselUntil > 0f && runSeconds < vesselUntil;
        NodeRuntime VortexNode { get { foreach (var n in nodes) if (n.def.type == NodeType.Vortex) return n; return null; } }
        bool VortexFighting { get { var v = VortexNode; return v != null && v.st == NodeRuntime.St.Active; } }
        public float finaleStartMin = -1f, forceAtSec = -1f;
        public const int PurifyToOpen = 2;          // 原稿：净化足够数量的裂隙后黑潮之心显形
        // 2026-10-09 整局验证后调：硬仗（信使 170 秒、巢穴 186 秒、终局 337 秒）太拖，全程零倒地太安全 → 精英 / Boss 血量 ×0.8、黑潮王 3 → 2.6、敌人伤害 ×1.15（Coop.EnemyDmgScale）
        public static float EliteHpScale = 0.9f;   // 第四局（去掉卡路后）0.8 太快：16 分钟、终局 105 秒，回调到 0.9
        public static float SovereignHpMul = 4f;    // 黑潮王血量倍数（相对潮汐骑，再乘侵蚀倍率）。
            // 原 ×3 的依据是 RunSim「扫黑潮王血量」：约 233–252 秒。**那个预测已经失效**——RunSim 不认识 Tide，
            // 也没算节点密度翻倍。headless 实测 ×3 时决战 79 秒（10.43 分钟满级 + 19 次选择的构筑直接秒掉三阶段）。
            // 先取 ×4 让三个阶段都有展开时间；新口径下**不追求拖长决战**（用户明确「硬拖反而煎熬」），
            // 目标是「终局同时是一群怪」而不是「boss 打很久」。跑局窗口按 finale_sec 再校。
        NodeRuntime Heart { get { foreach (var n in nodes) if (n.def.type == NodeType.Heart) return n; return null; } }
        public readonly List<NodeRuntime> nodes = new List<NodeRuntime>();
        NodeRuntime routeTarget;
        float routePickAt;
        Transform tideRing, tideFill;
        float nextSample, nextSave;
        readonly StringBuilder csv = new StringBuilder(), events = new StringBuilder();
        string logPath;
        static readonly Vector3[] messengerPath = { new Vector3(4, 0, 4), new Vector3(44, 0, -12), new Vector3(30, 0, 30), new Vector3(-8, 0, 30), new Vector3(-20, 0, -6) };
        int messengerWp;
        static readonly float[] Checkpoints = { 3f, 7f, 10.5f, 14f, 18f, 22f };
        static readonly string[] CheckpointTargets = { "3", "5", "6", "-", "-", "-" };
        int checkpoint;

        void Awake()
        {
            I = this;
            foreach (var d in MapLayout.Nodes) nodes.Add(new NodeRuntime(d));
            nodes.Add(new NodeRuntime(MapLayout.Vortex));
            Coop.erosion = 0f;
            Coop.tideCenter = new Vector3(MapLayout.TideCenter.x, 0f, MapLayout.TideCenter.y);
            Coop.coverageRadius = 0f;
            Coop.teamSkillCharges = 0;   // 原稿：团队技能来自净化裂隙（每个裂隙给 2 次）
            Coop.protocols.Clear();
            Economy.Reset();
            Intel.Reset();
            Rogue.Reset();
            foreach (var n in nodes) if (n.def.type == NodeType.Nest) n.locked = true;
            var shopRng = new System.Random(MapBuilder.Seed + 31);
            foreach (var n in nodes) if (n.def.type == NodeType.Merchant) n.shop = Economy.MakeShop(n.def.id, n.def.id == "M5", shopRng);
            Feel.SetBaseScale(1f);
            BuildTideMarker();
            string dir = Path.Combine(Application.dataPath, "..", "Captures", "runs");
            Directory.CreateDirectory(dir);
            logPath = Path.Combine(dir, "run_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            csv.AppendLine("t_real,t_run_min,erosion,coverage_r,purified,cleared,killsQ,killsJ,killsN,team_hp,downs,wipes,team_skills,leader_x,leader_z,target,strategy,length_scale,team_level,exp_total,coins_avg,earned_avg,spent_avg,star_avg,items_used,keys,revealed,buff_points,buff_unlocks,alive_now,alive_tide,fps_avg,fps_min,alive_awake");
            events.AppendLine("t_run_min,event,node,detail");
            Event("start", "", $"strategy={strategy} length={LengthScale}");
        }

        void Start()
        {
            new GameObject("MapUI").AddComponent<MapUI>().Build();
            new GameObject("ShopUI").AddComponent<ShopUI>().Build();
            new GameObject("RogueUI").AddComponent<RogueUI>().Build();
            new GameObject("TutorialUI").AddComponent<TutorialUI>().Build();   // DS-011：H 键教学面板
            new GameObject("ConsoleUI").AddComponent<ConsoleUI>().Build();     // DS-015：` / F7 控制台
            new GameObject("NodeLabels").AddComponent<NodeLabels>().Build(nodes);   // DS-017：节点价值标签（裂隙/信使/黑潮之心）
            new GameObject("Hunters").AddComponent<Hunters>().Build();             // DS-018b：黑潮追猎者
            new GameObject("CyberGrade").AddComponent<CyberGrade>().Build();        // DS-024：赛博朋克屏幕层（霓虹/危险/报错）
            new GameObject("Tide").AddComponent<Tide>().Build();                   // 割草方向重做：黑潮涌浪（持续怪潮）
            Miasma.Spawn(new GameObject("Miasma").transform);
            new GameObject("Atmosphere").AddComponent<Atmosphere>().Build();   // 黑潮里的雨、雾、闪电
            new GameObject("NodeBeacons").AddComponent<NodeBeacons>().Build(nodes);   // 未清目标的光柱（引导）
            foreach (var n in nodes) if (n.locked) n.seal = MakeSeal(n);
            // 商人站在货车旁，面朝出生点方向（M3 商店的交互点）
            var A = SliceAssets.I;
            foreach (var n in nodes)
                if (n.def.type == NodeType.Merchant && SliceGame.I && A.merchantTripo)
                {
                    Vector3 toSpawn = (new Vector3(MapLayout.Spawn.x, 0, MapLayout.Spawn.y) - n.pos).normalized;
                    Vector3 side = Vector3.Cross(Vector3.up, toSpawn);
                    SliceGame.I.SpawnNpc("商人·" + n.def.id, A.merchantTripo, A.merchantAlbedo, n.pos + toSpawn * 1.2f + side * 2.4f, toSpawn, 1.75f);
                }
        }

        // ---------------- 每帧 ----------------
        void Update()
        {
            if (ended) return;   // 结算后整局停住（之前 AI 会继续跑图，事件记录里多出结算后的清除）
            float dtMin = Time.deltaTime / 60f / LengthScale;
            runSeconds += Time.deltaTime;
            float dtReal = Time.unscaledDeltaTime;               // 割草方向重做：帧时间仪表（判据 G6）
            ftSum += dtReal; ftN++;
            if (dtReal > ftWorst) ftWorst = dtReal;
            float m = RunMinutes;

            // 侵蚀值：原稿分三段匀速增长（0–3 分钟到 15%、3–10 分钟到 45%、10–18 分钟到 100%）；净化裂隙直接 −15%
            if (VesselActive) Coop.erosion = 0f;
            else
            {
                if (vesselUntil > 0f) { Coop.erosion = savedErosion; vesselUntil = -1f; Combat.Toast("净世之器失效：侵蚀恢复", new Color(0.75f, 0.5f, 1f)); Event("vessel_end", "", $"erosion={savedErosion:0}"); }
                Coop.erosion = Mathf.Clamp(Coop.erosion + ErosionRate(m) * dtMin * erosionSpeed * (VortexFighting ? 1.5f : 1f), 0f, 100f);
            }
            // 黑潮覆盖范围只随时间扩散（净化降的是侵蚀值，不是范围）
            Coop.coverageRadius = MapLayout.RadiusForCoverage(MapLayout.CoverageAt(m));
            UpdateTideMarker();
            MapBuilder.SetTide(MapLayout.TideCenter, Coop.coverageRadius);

            foreach (var n in nodes) TickNode(n, m);
            if (ended) return;
            TickVortex(m);
            TickFinale(m);
            TickShops();
            Miasma.TickHeroes();
            Intel.Tick();
            Rogue.Tick();
            UpdateFocus();
            Keys();

            if (runSeconds >= nextSample) { nextSample = runSeconds + 5f; Sample(); }
            // 心流检查点（SPEC 3.4）：3 分钟 3 级、7 分钟 5 级、10–11 分钟 6 级
            while (checkpoint < Checkpoints.Length && m >= Checkpoints[checkpoint])
            {
                Event("checkpoint", "", $"min={Checkpoints[checkpoint]} lv={Growth.TeamFracLevel():0.00} target={CheckpointTargets[checkpoint]}");
                checkpoint++;
            }
            if (Time.unscaledTime >= nextSave) { nextSave = Time.unscaledTime + 30f; Save(); }
        }

        void Keys()
        {
            if (Input.GetKeyDown(KeyCode.F4))
            {
                speedIdx = (speedIdx + 1) % Speeds.Length;
                Feel.SetBaseScale(Speeds[speedIdx]);
                Combat.Toast($"游戏倍速 ×{Speeds[speedIdx]:0}", Color.white);
            }
            if (Input.GetKeyDown(KeyCode.F5))
            {
                LengthScale = LengthScale > 0.7f ? 0.4f : 1f;
                Combat.Toast(LengthScale < 1f ? "压缩版：侵蚀 7.2 分钟满（重开生效更准确）" : "原稿时长：侵蚀 18 分钟满", Color.white);
            }
            if (Input.GetKeyDown(KeyCode.F6))
            {
                strategy = (Strategy)(((int)strategy + 1) % 3);
                routeTarget = null;
                Combat.Toast($"路线 AI：{strategy}", Color.white);
                Event("strategy", "", strategy.ToString());
            }
        }

        // 焦点 Boss：离玩家最近、醒着的 Boss（血条和镜头用）
        void UpdateFocus()
        {
            var p = Combat.player;
            BossController best = null; float bd = 30f;
            if (p)
                foreach (var b in Combat.bosses)
                {
                    if (!b || b.dmg.dead || b.dormant) continue;
                    float d = Vector3.Distance(b.transform.position, p.transform.position);
                    if (d < bd) { bd = d; best = b; }
                }
            Combat.boss = best;
            var rig = Camera.main ? Camera.main.GetComponent<CameraRig>() : null;
            if (rig) rig.focus2 = best ? best.transform : null;
        }

        // ---------------- 节点 ----------------
        void TickNode(NodeRuntime n, float m)
        {
            if (!n.Fightable || n.Done) return;
            if (n.def.type == NodeType.Messenger) MoveMessenger(n);
            if (n.st == NodeRuntime.St.Idle)
            {
                if (n.locked) { TickLocked(n); return; }
                // 队伍走近（节点范围外 26 米）才刷敌人
                foreach (var h in Combat.heroes)
                    if (h && Vector3.Distance(Flat(h.transform.position), Flat(n.pos)) < n.def.radius + 26f) { Spawn(n, m); break; }
                return;
            }
            n.enemies.RemoveAll(e => !e || e.dead);
            if (n.st == NodeRuntime.St.Spawned)
            {
                foreach (var e in n.enemies) if (!e.dormant) { n.st = NodeRuntime.St.Active; n.activeT = m; Event("engage", n.def.id, n.def.name); break; }
                if (n.st == NodeRuntime.St.Active && n.def.type == NodeType.Heart) StartFinale(n, m);
            }
            if (n.enemies.Count == 0 && (!n.boss || n.boss.dmg.dead)) Clear(n, m);
        }

        void Clear(NodeRuntime n, float m)
        {
            n.st = NodeRuntime.St.Cleared;
            n.clearT = m;
            float dur = n.activeT >= 0f ? (m - n.activeT) * 60f * LengthScale : 0f;
            Event("clear", n.def.id, $"{n.def.name} {dur:0}s lv={Growth.TeamFracLevel():0.00}");
            if (n.def.type == NodeType.Heart) { EndRun(true); return; }
            if (n.def.type == NodeType.Vortex) VortexReward(n);
            foreach (var h in Combat.heroes) h.downCount = 0;   // 原稿：清完一个资源点，连续倒地次数归零
            Rogue.OnNodeCleared();
            // 武器掉落（原稿）：营地宝箱 4★、巢穴 3★/4★、裂隙 4★/5★，给当前武器最差的人
            int star = Economy.NodeWeapon(n.def.type, new System.Random(n.def.id.GetHashCode() ^ MapBuilder.Seed ^ 99));
            if (star > 0) Economy.GiveWeapon(Economy.Lowest(), star, n.def.type == NodeType.Camp ? "营地宝箱" : n.def.name);
            // 钥匙（原稿）：营地宝箱 0–3 把（中间多两头少），据点的探员固定 1 把
            int k = n.def.type == NodeType.Camp ? CampKeys(new System.Random(n.def.id.GetHashCode() ^ MapBuilder.Seed ^ 7)) : n.def.type == NodeType.Stronghold ? 1 : 0;
            if (k > 0) { keys += k; Combat.Toast($"获得巢穴钥匙 ×{k}（共 {keys} 把）", new Color(0.85f, 0.65f, 1f)); Event("key", n.def.id, $"+{k} total={keys}"); }
            if (n.def.type == NodeType.Messenger)
            {
                // 原稿：信使必掉 3 个强化声骸，固定分配到个人（原来装的声骸留在脚边，可以换回来）
                if (n.boss) Echoes.Remember("messenger", n.boss.gameObject);
                foreach (var h in Combat.heroes) if (h) Echoes.Equip(h, Echoes.Def("messenger"), true);
                Weapons.Drop(Weapons.Make(Weapons.Def("messenger_glaive"), 5), n.pos + Vector3.right * 2f);   // 信使长戟（5★，三段突刺）
                Combat.Toast($"击败黑潮信使：全队装备信使声骸（{GameInput.Hint("R","LB")} 范围更大、冷却 11 秒，能打断 Boss 读条，对黑潮王有奇效）", new Color(1f, 0.45f, 0.85f));
                Event("echo_up", n.def.id, "");
            }
            if (n.def.type == NodeType.Stronghold)
            {
                Intel.Scout(n, nodes);
                Combat.Toast("探员的情报：揭开一片迷雾，标出精英巢穴、野图怒涛和信使的位置", new Color(0.95f, 0.85f, 0.45f));
                Event("intel", n.def.id, $"revealed={Intel.RevealedShare():0.00}");
            }
            if (n.def.type == NodeType.Stronghold && SliceGame.I && SliceAssets.I.scoutTripo)
            {
                // 据点清除后，被困的探员现身（M3：交情报、开迷雾）
                var lead = Combat.player ? Combat.player.transform.position : n.pos;
                Vector3 f = lead - n.pos; f.y = 0f;
                SliceGame.I.SpawnNpc("探员·" + n.def.id, SliceAssets.I.scoutTripo, SliceAssets.I.scoutAlbedo, n.pos, f.normalized, 1.72f);
            }
            if (n.def.type == NodeType.Rift)
            {
                // 净化裂隙：侵蚀 −8%（割草方向重做：原稿 −15% 让黑潮可以无限压回去，时间经济失效——
                // 实测 13:13 那局净化 3 次、侵蚀峰值才 46%、forced=0。新口径下侵蚀是密度旋钮，
                // 净化仍给团队技能和揭图，但不再是续命按钮），获得团队技能 2 次
                purified++;
                Coop.erosion = Mathf.Max(0f, Coop.erosion - PurifyRelief);
                Coop.teamSkillCharges += 2;
                // 战略协议：抽一种这局还没抽过的（第一个裂隙也可能抽到潮汐共鸣以外的）
                var cand = new List<string>(); foreach (var id in Coop.ProtocolIds) if (!protocolsGiven.Contains(id)) cand.Add(id);
                string pid = cand.Count > 0 ? cand[new System.Random(n.def.id.GetHashCode() ^ MapBuilder.Seed ^ 13).Next(cand.Count)] : "tide";
                protocolsGiven.Add(pid);
                Coop.protocols.Add(pid); Coop.protocols.Add(pid);
                Combat.Toast($"战略协议：{Coop.ProtocolName(pid)} ×2（{Coop.ProtocolDesc(pid)}）", new Color(1f, 0.85f, 0.45f));
                Event("protocol", n.def.id, Coop.ProtocolName(pid));
                foreach (var h in Combat.heroes) if (h) h.AddExp(0f);   // 开了 10 级门槛时，净化后补升
                Combat.Toast($"净化{n.def.name} · 侵蚀 −{PurifyRelief:0}% · 团队技能 +2 次（已净化 {purified}/3）", new Color(0.4f, 1f, 0.95f));
                FX.Ring(n.pos, 1f, n.def.radius + 4f, 0.9f, new Color(0.4f, 1f, 0.95f, 1f), 0.25f);
                FX.Tide(n.pos + Vector3.up * 2f, Vector3.up, 120);
                Event("purify", n.def.id, $"erosion={Coop.erosion:0}");
                Hunters.OnRift();   // DS-018b：净化惊动黑潮——立即刷一波追猎者
                if (purified >= PurifyToOpen && !heartOpen) OpenHeart();
            }
            else Combat.Toast($"{n.def.name} 已清除", new Color(0.9f, 0.95f, 1f));
            // 清场收尾：玩家在场时 0.4 秒四分之一速的慢镜头 + 一圈清场光环
            if (Combat.player && Vector3.Distance(Flat(Combat.player.transform.position), Flat(n.pos)) < n.def.radius + 16f && n.def.type != NodeType.Pack && Feel.I != null)
            {
                Feel.I.HitStop(0.4f, 0.25f);
                FX.Ring(n.pos, 1f, n.def.radius + 6f, 0.9f, new Color(0.9f, 0.95f, 1f, 0.8f), 0.15f);
                Feel.I.Sfx("perfect_dodge", 0.6f, 0f, 0.8f);
            }
        }

        // 每种节点刷什么：数值按网页原型 SPEC 第 3.2 节和遭遇预设（轻波 q、巨浪 j、怒涛 n），血量再乘该位置的侵蚀倍率
        void Spawn(NodeRuntime n, float m)
        {
            n.st = NodeRuntime.St.Spawned;
            n.spawnT = m;
            var g = SliceGame.I;
            float erosionBonus = n.def.type == NodeType.Nest ? 10f : n.def.type == NodeType.Messenger ? 20f : 0f;   // 原稿：巢穴按侵蚀 +10% 计，信使按下一级侵蚀
            float hpE = Coop.HpMultFor(Mathf.Min(100f, Coop.ErosionAt(n.pos) + erosionBonus));
            var rng = new System.Random(n.def.id.GetHashCode() ^ MapBuilder.Seed);
            Vector3 c = n.pos;
            float R = n.def.radius;
            Vector3 At(float r, float deg) { float a = deg * Mathf.Deg2Rad; return Combat.ClampToArena(c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r, 1f); }
            float Rnd(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            void Minion(Vector3 p, int tier, float hpMul, System.Action wakeGroup)
            {
                // 轻波：血 ×1、伤害 ×1；巨浪：血 ×10、伤害 ×2.6（网页原型：巨浪血 10 倍、伤害 220 对轻波 60），体型放大
                float hp = tier == 0 ? 1f : 10f * hpMul * EliteHpScale;
                float dm = tier == 0 ? 1f : 2.6f;
                float size = tier == 0 ? 1f : 1.5f;
                g.SpawnMinion(p, hp * hpE, dm, size, null);
                var mc = MinionController.Last;
                if (!mc) return;
                mc.dormant = true; mc.dmg.dormant = true;
                mc.home = mc.transform.position; mc.leash = R + 14f;
                mc.dmg.tier = tier;
                mc.onWake = wakeGroup;
                n.enemies.Add(mc.dmg);
            }
            void Var(Vector3 p, MinionController.Kind k, int tier, float hpMul, System.Action wakeGroup)
            {
                float hp = tier == 0 ? 1f : 10f * hpMul * EliteHpScale;
                float dm = tier == 0 ? 1f : 2.6f;
                var mc = g.SpawnVariant(p, k, hp * hpE, dm);
                if (!mc) return;
                mc.dormant = true; mc.dmg.dormant = true;
                mc.home = mc.transform.position; mc.leash = R + 14f;
                mc.dmg.tier = tier;
                mc.onWake = wakeGroup;
                mc.onSummon = s => n.enemies.Add(s.dmg);
                n.enemies.Add(mc.dmg);
            }
            void Jelly(Vector3 p, float hpMul, System.Action wakeGroup) { var j = DriftJelly.Spawn(p, 10f * hpMul * hpE * EliteHpScale, R + 14f, wakeGroup); if (j) n.enemies.Add(j.dmg); }
            void Pod(Vector3 p) { var b = BroodPod.Spawn(p, n, hpE); if (b) n.enemies.Add(b.GetComponent<Damageable>()); }
            void Boss(Vector3 p, float hpMul, string name)
            {
                if (n.def.type == NodeType.Messenger) g.SpawnMessenger(p);
                else if (n.def.type == NodeType.Heart) g.SpawnSovereign(p);
                else if (n.def.type == NodeType.WildBoss && WildVariant(n.def.id) != null) g.SpawnWildVariant(p, WildVariant(n.def.id), name);
                else g.SpawnBoss(p);
                var b = BossController.Last;
                if (!b) return;
                b.bossName = name;
                b.dmg.maxHp *= hpMul * hpE * (n.def.type == NodeType.Heart ? 1f : EliteHpScale); b.dmg.hp = b.dmg.maxHp;
                if (n.weakened) { b.dmg.hp *= 0.9f; b.dmgMul = 0.9f; }
                b.dormant = true; b.dmg.dormant = true;
                b.HoldPose(n.def.type == NodeType.Messenger);
                b.home = b.transform.position; b.leash = R + 8f;
                b.onDefeated = _ => { };
                if (n.def.type == NodeType.Messenger) b.dmg.expValue = Growth.MessengerExp;   // 信使单独定经验（设计目标：标准路线打完信使 8.5–9 级）
                n.boss = b;
                n.enemies.Add(b.dmg);
            }
            void Ray(Vector3 p, System.Action wakeGroup)
            {
                var rc = g.SpawnRay(p, hpE);
                if (!rc) return;
                rc.dormant = true; rc.dmg.dormant = true;
                rc.home = rc.transform.position; rc.leash = R + 16f;
                rc.onWake = wakeGroup;
                n.enemies.Add(rc.dmg);
            }
            // 同一组的敌人一起苏醒（原稿：据点「攻击一个即可获得全体仇恨」）
            System.Action WakeAll() => () => { foreach (var e in n.enemies) Wake(e); };

            switch (n.def.type)
            {
                case NodeType.Pack:         // 游荡残像：割草方向重做 6 → 14 只（加密度不加硬度，每只仍 1–2 刀）
                    for (int i = 0; i < 8; i++) Minion(At(Rnd(1f, R * 0.7f), Rnd(0, 360)), 0, 1f, WakeAll());
                    for (int i = 0; i < 2; i++) Var(At(Rnd(1f, R * 0.6f), Rnd(0, 360)), rng.Next(2) == 0 ? MinionController.Kind.Bloat : MinionController.Kind.Shield, 0, 1f, WakeAll());
                    for (int i = 0; i < 4; i++) Ray(At(R * 0.8f, i * 90f + Rnd(0, 45)), WakeAll());
                    break;
                case NodeType.Stronghold:   // 据点：割草方向重做 7 → 11 只（外圈首站，密度温和，别把开局压垮）
                    Minion(c, 1, 0.7f, WakeAll());
                    for (int i = 0; i < 6; i++) Minion(At(Rnd(2.5f, R * 0.7f), i * 60f + Rnd(-15, 15)), 0, 1f, WakeAll());
                    for (int i = 0; i < 2; i++) Var(At(Rnd(2.5f, R * 0.5f), 216f + i * 60f + Rnd(-15, 15)), MinionController.Kind.Shield, 0, 1f, WakeAll());
                    Var(At(R * 0.75f, 288f + Rnd(-15, 15)), MinionController.Kind.Chanter, 0, 1f, WakeAll());
                    for (int i = 0; i < 2; i++) Ray(At(R * 0.85f, i * 180f + Rnd(0, 60)), WakeAll());
                    break;
                case NodeType.Camp:         // 营地：分三片散布，各自苏醒；巨浪在第一片
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 cc = At(R * 0.55f, k * 120f + Rnd(-20, 20));
                        var group = new List<Damageable>();
                        System.Action wake = () => { foreach (var e in group) Wake(e); };
                        int before = n.enemies.Count;
                        if (k == 0) Minion(cc, 1, 0.6f, wake);
                        int cnt = 7 + rng.Next(0, 3);   // 割草方向重做：每片 3–4 → 7–9，营地合计 15 → 约 26
                        for (int i = 0; i < cnt; i++)
                        {
                            Vector3 mp = Combat.ClampToArena(cc + new Vector3(Rnd(-2.5f, 2.5f), 0, Rnd(-2.5f, 2.5f)), 1f);
                            if (i == 0 && k == 1) Var(mp, MinionController.Kind.Bloat, 0, 1f, wake);
                            else if (i == 0 && k == 2) Var(mp, MinionController.Kind.Shield, 0, 1f, wake);
                            else Minion(mp, 0, 1f, wake);
                        }
                        Ray(Combat.ClampToArena(cc + new Vector3(Rnd(-4f, 4f), 0, Rnd(-4f, 4f)), 1f), wake);   // 每片一只刺鳐（网页原型营地 9 + 3）
                        for (int i = before; i < n.enemies.Count; i++) group.Add(n.enemies[i]);
                    }
                    break;
                case NodeType.Nest:         // 精英巢穴：割草方向重做 6 → 14（5 只巨浪 + 8 只随扈轻波 + 孵化囊持续出兵）
                    Minion(c, 1, 1f, WakeAll());
                    Minion(At(Rnd(3f, R * 0.5f), 72f), 1, 1f, WakeAll());
                    Var(At(Rnd(3f, R * 0.5f), 144f), MinionController.Kind.Duelist, 1, 1f, WakeAll());
                    Var(At(R * 0.7f, 216f), MinionController.Kind.Javelin, 1, 1f, WakeAll());
                    Var(At(R * 0.7f, 288f), MinionController.Kind.Priest, 1, 1f, WakeAll());
                    for (int i = 0; i < 8; i++) Minion(At(Rnd(3f, R * 0.9f), Rnd(0, 360)), 0, 1f, WakeAll());
                    Pod(At(R * 0.55f, 0f));
                    break;
                case NodeType.WildBoss:     // 野图怒涛：潮汐骑 + 2 只巨浪
                    Boss(c, 1f, WildName(n.def.id));
                    Minion(At(R * 0.6f, 90f), 1, 0.8f, WakeAll());
                    if (WildVariant(n.def.id) == null) Jelly(At(R * 0.6f, 270f), 0.8f, WakeAll());   // 西崖潮汐骑带浮游水母
                    else Var(At(R * 0.6f, 270f), MinionController.Kind.Duelist, 1, 0.8f, WakeAll());
                    break;
                case NodeType.Rift:         // 黑潮裂隙：怒涛 + 4 只较弱巨浪 + 10 只轻波，一起苏醒（网页原型遭遇预设 rift）
                    Boss(c, 1f, "裂隙守卫 · 潮汐骑");
                    Minion(At(R * 0.55f, 45f), 1, 0.6f, WakeAll());
                    Jelly(At(R * 0.55f, 135f), 0.6f, WakeAll());
                    Var(At(R * 0.8f, 225f), MinionController.Kind.Javelin, 1, 0.6f, WakeAll());
                    Var(At(R * 0.8f, 315f), MinionController.Kind.Priest, 1, 0.6f, WakeAll());
                    for (int i = 0; i < 16; i++) Minion(At(Rnd(4f, R * 0.85f), Rnd(0, 360)), 0, 1f, WakeAll());   // 割草方向重做：裂隙 18 → 约 34 只
                    for (int i = 0; i < 5; i++) Var(At(Rnd(4f, R * 0.6f), Rnd(0, 360)), MinionController.Kind.Shield, 0, 1f, WakeAll());
                    for (int i = 0; i < 4; i++) Var(At(R * 0.85f, i * 90f + Rnd(0, 45)), MinionController.Kind.Chanter, 0, 1f, WakeAll());
                    Pod(At(R * 0.5f, 180f));
                    for (int i = 0; i < 4; i++) Ray(At(R * 0.9f, i * 90f + 45f), WakeAll());
                    break;
                case NodeType.Messenger:    // 黑潮信使：怒涛 ×2 血量，按下一级侵蚀计算（原稿）
                    Boss(c, 2f, "黑潮信使");
                    break;
                case NodeType.Vortex:       // 黑潮漩涡：强化怒涛（血量 ×1.3），开战后每 20 秒召唤一波 4 只轻波，共 3 波
                    Boss(c, 1.3f, "黑潮漩涡 · 潮汐骑");
                    n.waves = 0; n.nextWave = 0f;
                    break;
                case NodeType.Heart:        // 黑潮王（海啸级）：二阶段起自己召唤触手
                    Boss(c, SovereignHpMul, "海啸 · 黑潮王");
                    if (n.boss) { n.boss.dmg.tier = 3; n.boss.leash = 0f; n.boss.dmg.expValue = Growth.TierExp(3); }
                    break;
            }
            Event("spawn", n.def.id, $"{n.def.name} enemies={n.enemies.Count} erosionHp={hpE:0.00}");
        }

        // 四个野图怒涛各不相同（原稿：野外 4–6 只不同的怒涛）：盐沼霜潮女巫、西崖潮汐骑、潮门雷锚巨兵、北岸湮灭刺客
        static string WildVariant(string id) => id == "W1" ? "frost" : id == "W3" ? "thunder" : id == "W4" ? "reaper" : null;
        static string WildName(string id) => id == "W1" ? "怒涛 · 霜潮女巫" : id == "W3" ? "怒涛 · 雷锚巨兵" : id == "W4" ? "怒涛 · 湮灭刺客" : "怒涛 · 潮汐骑";

        static void Wake(Damageable e)
        {
            if (!e || e.dead || !e.dormant) return;
            var mc = e.GetComponent<IMob>(); if (mc != null) { mc.Wake(); return; }
            if (e.bossCtl) e.bossCtl.Wake();
        }

        // 信使待命时沿一条环线游荡
        void MoveMessenger(NodeRuntime n)
        {
            if (n.boss && !n.boss.dormant) return;
            Vector3 wp = messengerPath[messengerWp];
            if (Vector3.Distance(Flat(n.pos), Flat(wp)) < 1.5f) messengerWp = (messengerWp + 1) % messengerPath.Length;
            n.pos = Vector3.MoveTowards(n.pos, wp, 2.2f * Time.deltaTime);
            if (n.boss) n.boss.Patrol(n.pos);
        }

        // ---------------- 路线 AI（自动代打时由它带队） ----------------
        Vector3 stuckPos; float stuckT, lastRouteCall;
        readonly HashSet<NodeRuntime> skipped = new HashSet<NodeRuntime>();

        public Vector3 RouteStep(HeroBase leader)
        {
            if (ended) return Vector3.zero;
            // 卡住检测：赶路时 20 秒（游戏时间）没挪动 1 米，记一笔，并暂时跳过这个目标（打仗时不调用这里，所以要重置计时）
            if (Time.time - lastRouteCall > 1f) { stuckPos = leader.transform.position; stuckT = Time.time; }
            lastRouteCall = Time.time;
            if (Vector3.Distance(Flat(leader.transform.position), Flat(stuckPos)) > 1f) { stuckPos = leader.transform.position; stuckT = Time.time; }
            else if (Time.time - stuckT > 20f && routeTarget != null)
            {
                Event("stuck", routeTarget.def.id, $"pos={leader.transform.position.x:0};{leader.transform.position.z:0}");
                skipped.Add(routeTarget);
                routeTarget = null;
                stuckT = Time.time;
            }
            if (routeTarget == null || routeTarget.Done || Time.time >= routePickAt)
            {
                var prev = routeTarget;
                routeTarget = PickNext(leader);
                routePickAt = Time.time + 4f;
                if (routeTarget != null && routeTarget != prev) Event("route", routeTarget.def.id, routeTarget.def.name);
            }
            if (routeTarget == null) return Vector3.zero;
            Vector3 goal = routeTarget.pos;
            // 节点已经刷了敌人：走向最近的那只（营地分几片，走到中心未必能惊动每一片）
            float best = float.MaxValue;
            foreach (var e in routeTarget.enemies)
            {
                if (!e || e.dead) continue;
                float d = Vector3.Distance(Flat(e.transform.position), Flat(leader.transform.position));
                if (d < best) { best = d; goal = e.transform.position; }
            }
            if (Vector3.Distance(Flat(leader.transform.position), Flat(goal)) < 2.5f) return Vector3.zero;
            return Nav.Dir(leader, leader.transform.position, goal);
        }

        // ---------------- 黑潮漩涡（M5） ----------------
        public const float VortexDetour = 70f;

        void TickVortex(float m)
        {
            var v = VortexNode;
            if (v == null) return;
            if (!vortexOpen && m >= MapLayout.VortexAt && !finaleStarted)
            {
                var lead = Combat.player ? Combat.player.transform.position : new Vector3(MapLayout.Spawn.x, 0f, MapLayout.Spawn.y);
                var spot = MapLayout.VortexSpot(new Vector2(lead.x, lead.z), d => { foreach (var n in nodes) if (n.def == d) return n.Done; return false; });
                v.pos = Combat.ClampToArena(new Vector3(spot.x, 0f, spot.y), 2f);
                vortexOpen = true;
                Intel.Mark(v.def.id);
                v.vortexFx = MakeVortexFx(v.pos);
                Combat.Toast("黑潮漩涡出现了：挑战期间侵蚀增速 +50%，击败后增速永久 −10% 并获得净世之器", new Color(0.75f, 0.5f, 1f));
                Event("vortex_open", v.def.id, $"pos={v.pos.x:0};{v.pos.z:0}");
            }
            if (v.vortexFx) v.vortexFx.Rotate(0f, 40f * Time.deltaTime, 0f);
            // 召唤波次
            if (v.st == NodeRuntime.St.Active && v.waves < 3 && Time.time >= v.nextWave)
            {
                v.nextWave = Time.time + 20f;
                v.waves++;
                float hpE = Coop.HpMultFor(Coop.ErosionAt(v.pos));
                for (int i = 0; i < 4; i++)
                {
                    float a = (i / 4f + v.waves * 0.13f) * Mathf.PI * 2f;
                    var p = Combat.ClampToArena(v.pos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 6f, 1f);
                    SliceGame.I.SpawnMinion(p, hpE, 1f, 1f, null);
                    var mc = MinionController.Last;
                    if (!mc) continue;
                    mc.home = mc.transform.position; mc.leash = v.def.radius + 14f;
                    mc.dmg.tier = 0;
                    v.enemies.Add(mc.dmg);
                    FX.Tide(p, Vector3.up, 20);
                }
                Event("vortex_wave", v.def.id, v.waves.ToString());
            }
        }

        Transform MakeVortexFx(Vector3 p)
        {
            var root = new GameObject("VortexFx").transform;
            root.position = p;
            var prop = SliceAssets.I.MapProp("30_tide_vortex");
            if (prop != null && prop.model)
                ArenaBuilder.Prop(root, prop, "30_tide_vortex", p, Quaternion.identity, 7f, true, ArenaBuilder.StoneTint, new Color(0.7f, 0.35f, 1f) * 3f, 0.2f, out _);
            var ring = new GameObject("Ring");
            ring.transform.SetParent(root, false);
            ring.transform.localPosition = Vector3.up * 0.05f;
            ring.AddComponent<MeshFilter>().sharedMesh = Telegraph.Sector(MapLayout.Vortex.radius - 0.6f, MapLayout.Vortex.radius, 360f, 96);
            var r = ring.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mat = new Material(SliceAssets.I.telegraphShader);
            mat.SetColor("_Color", new Color(0.7f, 0.35f, 1f, 0.85f)); mat.SetFloat("_Progress", 1f); mat.SetFloat("_Sides", 0f);
            r.sharedMaterial = mat;
            var lg = new GameObject("VortexLight").AddComponent<Light>();
            lg.transform.SetParent(root, false); lg.transform.localPosition = Vector3.up * 3f;
            lg.type = LightType.Point; lg.color = new Color(0.7f, 0.35f, 1f); lg.intensity = 4f; lg.range = 16f; lg.shadows = LightShadows.None;
            return root;
        }

        void VortexReward(NodeRuntime v)
        {
            erosionSpeed = Mathf.Max(0.5f, erosionSpeed - 0.1f);
            worldVessel = 1;
            if (v.vortexFx) Destroy(v.vortexFx.gameObject);
            // 周围的 Boss（原稿：覆盖最近 Boss 的最小圆）：离漩涡最近、还没清掉的 Boss 节点，当前血量 −10%、伤害 −10%
            NodeRuntime best = null; float bd = float.MaxValue;
            foreach (var n in nodes)
            {
                if (n.Done || n == v) continue;
                if (n.def.type != NodeType.WildBoss && n.def.type != NodeType.Rift && n.def.type != NodeType.Messenger && n.def.type != NodeType.Heart) continue;
                float d = Vector3.Distance(Flat(n.pos), Flat(v.pos));
                if (d < bd) { bd = d; best = n; }
            }
            if (best != null)
            {
                best.weakened = true;
                if (best.boss && !best.boss.dmg.dead) { best.boss.dmg.hp *= 0.9f; best.boss.dmgMul = 0.9f; }
            }
            Combat.Toast($"击败黑潮漩涡：侵蚀增速永久 −10%，{(best != null ? best.def.name + "的 Boss 血量、伤害 −10%，" : "")}获得净世之器（7）", new Color(0.75f, 0.5f, 1f));
            Event("vortex_clear", v.def.id, $"erosionSpeed={erosionSpeed:0.00} weakened={(best != null ? best.def.id : "-")}");
        }

        // 净世之器：1.5 分钟（原稿时间轴）内侵蚀清零并暂停、全队技能和声骸冷却刷新；时限到了侵蚀恢复原值。每局 1 次
        public void UseVessel(HeroBase by)
        {
            if (worldVessel <= 0 || VesselActive) return;
            worldVessel = 0;
            savedErosion = Coop.erosion;
            Coop.erosion = 0f;
            vesselUntil = runSeconds + 90f * LengthScale;
            foreach (var h in Combat.heroes) if (h) { h.skillReadyAt = 0f; h.echoReadyAt = 0f; }
            FX.Ring(by.transform.position, 1f, 30f, 1f, new Color(0.75f, 0.5f, 1f, 1f), 0.2f);
            Combat.Toast($"{by.HeroName}使用净世之器：1.5 分钟内侵蚀清零，全队冷却刷新", new Color(0.75f, 0.5f, 1f));
            Event("vessel", "", $"{by.HeroName} saved={savedErosion:0} finale={(finaleStarted ? 1 : 0)}");
        }

        // ---------------- 终局（M5） ----------------
        // 三条路线什么时候去打黑潮王：稳健等到 9 级（或第 21 分钟、侵蚀 95%）；激进净化完 3 个裂隙且 8.5 级（或第 19 分钟）；贪信使打完信使且 8.5 级（或第 21 分钟）
        public bool ReadyForFinale(float m)
        {
            bool msgr = false; foreach (var n in nodes) if (n.def.type == NodeType.Messenger && n.Done) msgr = true;
            return FinaleReady(strategy, Growth.TeamFracLevel(), m, purified, msgr, Coop.erosion);
        }
        // 公式模拟共用
        public static bool FinaleReady(Strategy st, float lv, float m, int purified, bool msgr, float erosion)
        {
            switch (st)
            {
                case Strategy.稳健发育: return lv >= 9f || m >= 21f || erosion >= 95f;
                case Strategy.激进净化: return (purified >= 3 && lv >= 8.5f) || m >= 19f;
                default: return (msgr && lv >= 8.5f) || m >= 21f;
            }
        }

        void OpenHeart()
        {
            heartOpen = true;
            var h = Heart;
            if (h != null) Intel.Mark(h.def.id);
            Combat.Toast("黑潮之心显形：可以随时进入环形门挑战黑潮王", new Color(0.35f, 1f, 0.92f));
            Event("heart_open", h != null ? h.def.id : "", $"purified={purified}");
        }

        void StartFinale(NodeRuntime n, float m)
        {
            if (finaleStarted) return;
            finaleStarted = true;
            finaleStartMin = m;
            n.seal = MakeSeal(n);
            Combat.Toast("黑潮王现身 · 黑潮之心已封闭，击败它或全军覆没", new Color(1f, 0.5f, 0.45f));
            Event("finale_start", n.def.id, $"lv={Growth.TeamFracLevel():0.00} purified={purified} echo={(Combat.player && Combat.player.echoEmpowered ? 1 : 0)} forced={(forceAtSec > 0f && runSeconds >= forceAtSec ? 1 : 0)}");
        }

        void TickFinale(float m)
        {
            var heart = Heart;
            if (heart == null) return;
            // 侵蚀满 100% 后 60 秒（原稿时间轴）强制终局：全队拉到环形门前
            if (!finaleStarted && Coop.erosion >= 100f && forceAtSec < 0f)
            {
                forceAtSec = runSeconds + 60f * LengthScale;
                Combat.Toast("侵蚀已满：60 秒后强制进入终局", new Color(1f, 0.5f, 0.45f));
                Event("force_warn", heart.def.id, "");
            }
            if (!finaleStarted && forceAtSec > 0f && runSeconds >= forceAtSec)
            {
                if (!heartOpen) OpenHeart();
                Vector3 toSpawn = (new Vector3(MapLayout.Spawn.x, 0f, MapLayout.Spawn.y) - heart.pos).normalized;
                int i = 0;
                foreach (var h in Combat.heroes) if (h) h.Respawn(heart.pos + toSpawn * (heart.def.radius - 4f) + Quaternion.Euler(0, 90, 0) * toSpawn * (i++ - 1) * 2.2f, Mathf.Max(0.5f, h.dmg.hp / h.dmg.maxHp));
                if (heart.st == NodeRuntime.St.Idle) Spawn(heart, m);
                foreach (var e in heart.enemies) Wake(e);
                Event("force_finale", heart.def.id, "");
            }
            if (!finaleStarted) return;
            // 净世之器：自动代打时决战一开始就用；玩家自己操控时，20 秒内没用就由队友用掉（原稿：「谁用、何时用」是团队决策）
            if (worldVessel > 0 && (HeroBase.Autopilot || (m - finaleStartMin) * 60f * LengthScale > 20f))
            {
                HeroBase by = null; foreach (var h in Combat.heroes) if (h && !h.Downed && h.UsesBrain) { by = h; break; }
                if (by) UseVessel(by);
            }
            // 封印：人留在圈里
            foreach (var h in Combat.heroes)
            {
                if (!h) continue;
                Vector3 d = Flat(h.transform.position - heart.pos);
                float lim = heart.def.radius - 0.5f;
                if (d.magnitude > lim)
                {
                    var cc = h.GetComponent<CharacterController>();
                    if (cc) cc.Move(-d.normalized * (d.magnitude - lim));
                }
            }
        }

        void EndRun(bool win)
        {
            if (ended) return;
            ended = true; victory = win;
            float m = RunMinutes;
            float fd = finaleStartMin >= 0f ? (m - finaleStartMin) * 60f * LengthScale : 0f;
            Event("run_end", "H1", $"win={(win ? 1 : 0)} finale_s={fd:0} lv={Growth.TeamFracLevel():0.00}");
            var sb = new StringBuilder("key,value\n");
            void KV(string k, object v) => sb.AppendLine($"{k},{v}");
            KV("result", win ? "victory" : "defeat"); KV("strategy", strategy); KV("length_scale", LengthScale);
            KV("run_min", m.ToString("0.00")); KV("finale_start_min", finaleStartMin.ToString("0.00")); KV("finale_sec", fd.ToString("0"));
            KV("forced", forceAtSec > 0f && finaleStartMin * 60f * LengthScale >= forceAtSec - 1f ? 1 : 0);
            KV("team_level", Growth.TeamFracLevel().ToString("0.00")); KV("purified", purified); KV("wipes", wipes);
            KV("kills_q", Coop.killsQ); KV("kills_j", Coop.killsJ); KV("kills_n", Coop.killsN);
            KV("buff_points", Rogue.TeamPoints); KV("buff_unlocks", Rogue.Unlocks(Rogue.TeamPoints)); KV("team_skills", Coop.teamSkills);
            bool echo = false; foreach (var h in Combat.heroes) if (h && h.echoEmpowered) echo = true;
            KV("messenger_echo", echo ? 1 : 0);
            foreach (var h in Combat.heroes)
            {
                if (!h) continue;
                var names = new List<string>(); foreach (var b in h.rogue.owned) names.Add(b.name);
                KV($"{h.Id}_level", h.level); KV($"{h.Id}_star", h.kit.weaponStar); KV($"{h.Id}_earned", h.kit.earned.ToString("0")); KV($"{h.Id}_spent", h.kit.spent.ToString("0"));
                KV($"{h.Id}_buffs", string.Join(" ", names));
            }
            try { File.WriteAllText(logPath + "_summary.csv", sb.ToString(), new UTF8Encoding(true)); } catch (System.Exception ex) { Debug.LogWarning(ex.Message); }
            Save();
            HeroBase.Autopilot = false;
            EndUI.Show(win, fd, sb.ToString());
        }

        // ---------------- 钥匙和巢穴门（M3） ----------------
        public static int CampKeys(System.Random r) { double x = r.NextDouble(); return x < 0.15 ? 0 : x < 0.5 ? 1 : x < 0.85 ? 2 : 3; }
        float lockToastT;

        void TickLocked(NodeRuntime n)
        {
            foreach (var h in Combat.heroes)
            {
                if (!h) continue;
                Vector3 d = Flat(h.transform.position - n.pos);
                float dist = d.magnitude;
                if (dist > n.def.radius + 4f) continue;
                if (keys > 0)
                {
                    keys--;
                    n.locked = false;
                    if (n.seal) Destroy(n.seal.gameObject);
                    Combat.Toast($"用掉一把钥匙，{n.def.name}的封印解除（剩 {keys} 把）", new Color(0.85f, 0.65f, 1f));
                    FX.Ring(n.pos, 1f, n.def.radius + 3f, 0.8f, new Color(0.8f, 0.55f, 1f, 1f), 0.3f);
                    Event("unlock", n.def.id, $"keys_left={keys}");
                    return;
                }
                // 没钥匙：挡在封印外面
                if (dist < n.def.radius + 1.5f)
                {
                    Vector3 outDir = dist > 0.01f ? d / dist : (new Vector3(MapLayout.Spawn.x, 0f, MapLayout.Spawn.y) - n.pos).normalized;
                    var cc = h.GetComponent<CharacterController>();
                    if (cc) cc.Move(outDir * (n.def.radius + 1.5f - dist));
                }
                if (Time.time > lockToastT && dist < n.def.radius + 3f) { lockToastT = Time.time + 6f; Combat.Toast($"{n.def.name}被封印了：需要一把巢穴钥匙（营地宝箱、情报据点的探员给）", new Color(0.85f, 0.65f, 1f)); }
            }
        }

        Transform MakeSeal(NodeRuntime n)
        {
            var go = new GameObject("NestSeal_" + n.def.id);
            go.transform.position = n.pos + Vector3.up * 0.05f;
            go.AddComponent<MeshFilter>().sharedMesh = Telegraph.Sector(n.def.radius, n.def.radius + 0.6f, 360f, 96);
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mat = new Material(SliceAssets.I.telegraphShader);
            mat.SetColor("_Color", new Color(0.8f, 0.5f, 1f, 0.85f));
            mat.SetFloat("_Progress", 1f);
            mat.SetFloat("_Sides", 0f);
            r.sharedMaterial = mat;
            return go.transform;
        }

        // ---------------- 商人（M3）：队伍到了商人旁边，AI 角色各自按规则买（玩家自己操控时按 G 自己买） ----------------
        public const float ShopDetour = 60f, ShopRevisitMin = 5f;
        public static bool ShopOpenFor(NodeRuntime n, float m) => n.shop.visits == 0 || m - n.shop.lastVisit >= ShopRevisitMin;

        void TickShops()
        {
            float m = RunMinutes;
            foreach (var n in nodes)
            {
                if (n.shop == null) continue;
                bool here = false;
                foreach (var h in Combat.heroes) if (h && !h.Downed && Vector3.Distance(Flat(h.transform.position), Flat(n.pos)) < ShopUI.Range + 1f) here = true;
                if (!here || m - n.shop.lastVisit < 1f) continue;
                n.shop.visits++;
                n.shop.lastVisit = m;
                var team = new List<Kit>(); foreach (var h in Combat.heroes) if (h) team.Add(h.kit);
                foreach (var h in Combat.heroes)
                {
                    if (!h || !h.UsesBrain) continue;
                    var log = new List<string>();
                    Economy.AutoBuy(h.kit, n.shop, h.Id, team, Coop.erosion, m, true, log);
                    // 实测里出现过 detail 为空的 buy 事件（ShopUI 同路径有 log.Count>0 判断，这里漏了）。
                    // 分开记：真买到了 = buy；来了但一件都买不动 = buy_skip + 原因，后者是货架/限购的诊断信号。
                    if (log.Count > 0) Event("buy", n.def.id, $"{h.HeroName} coins={h.kit.coins:0} {string.Join(" ", log)}");
                    else Event("buy_skip", n.def.id, $"{h.HeroName} coins={h.kit.coins:0}");
                }
                if (routeTarget == n) { routeTarget = null; routePickAt = 0f; }
            }
        }

        public string RouteName => routeTarget != null ? routeTarget.def.name : "-";
        public NodeRuntime RouteNode => routeTarget;

        // 三种路线（SPEC 第 0 节要比较的就是它们）：
        // 稳健发育：由外往内，先把外圈据点、营地吃完再去裂隙；激进净化：2.5 分钟后直奔裂隙；贪信使：4 分钟后先找信使
        NodeRuntime PickNext(HeroBase leader)
        {
            float m = RunMinutes;
            NodeRuntime best = null; float bestScore = float.MaxValue;
            if (Economy.WantsShop(leader.kit, Coop.erosion, m, true, leader.Id))
            {
                foreach (var n in nodes)
                {
                    if (n.shop == null || !ShopOpenFor(n, m) || skipped.Contains(n)) continue;   // 卡住过的商人也跳过（否则会反复选回去）
                    float d = Vector3.Distance(Flat(leader.transform.position), Flat(n.pos));
                    if (d < ShopDetour && d < bestScore) { bestScore = d; best = n; }
                }
                if (best != null) return best;
            }
            var vtx = VortexNode;
            if (vortexOpen && vtx != null && !vtx.Done && !skipped.Contains(vtx) && Vector3.Distance(Flat(leader.transform.position), Flat(vtx.pos)) < VortexDetour) return vtx;
            var heart = Heart;
            if (heartOpen && heart != null && !heart.Done && ReadyForFinale(m)) return heart;
            foreach (var n in nodes)
            {
                if (!n.Fightable || n.Done) continue;
                if (n.def.type == NodeType.Heart || n.def.type == NodeType.Vortex) continue;
                if (n.locked && keys == 0) continue;
                float d = Vector3.Distance(Flat(leader.transform.position), Flat(n.pos));
                float s = RouteScore(n.def, d, m, strategy) + (skipped.Contains(n) ? 500f : 0f);
                if (s < bestScore) { bestScore = s; best = n; }
            }
            return best;
        }

        // 路线打分（越小越优先）。实时跑局和公式模拟（RunSim）共用这一份
        public static float RouteScore(NodeDef def, float dist, float m, Strategy st)
        {
            float s = dist;
            switch (st)
            {
                case Strategy.稳健发育:
                    s += def.band * 70f;
                    if (def.type == NodeType.Rift && m < 7f) s += 120f;
                    if (def.type == NodeType.Messenger) s += m < 14f ? 400f : 0f;
                    break;
                case Strategy.激进净化:
                    if (def.type == NodeType.Rift && m > 2.5f) s -= 140f;
                    if (def.type == NodeType.Messenger) s += 300f;
                    s += def.band * 25f;
                    break;
                case Strategy.贪信使:
                    // KB-007（DeepSeek 2026-10-10，用户授权接手）：7.4 经验下 4–5 级冲信使会被怒涛×2 血量反复团灭
                    // （RUN-001 实测：5.9 分钟 4 级进场 → 双团灭 → 侵蚀螺旋 → 强制终局落败）。
                    // 等级 ≥6 再优先信使（8.8 时代自然进信使约 7 级；6 级门保留"贪"的取舍、躲开送死开局）
                    if (def.type == NodeType.Messenger && m > 4f && Growth.TeamFracLevel() >= 6f) s -= 220f;
                    s += def.band * 40f;
                    break;
            }
            if (def.type == NodeType.Pack) s -= 15f;   // 顺路的残像群
            return s;
        }

        // 侵蚀增长速度（%/分钟，原稿三段匀速：0–3 分钟到 15%、3–10 分钟到 45%、10–18 分钟到 100%）
        public static float ErosionRate(float m) => m < 3f ? 5f : m < 10f ? 30f / 7f : 55f / 8f;

        // ---------------- 团灭（原稿：首次团灭黑潮增速 +5%；同一挑战第二次团灭再 +15% 侵蚀、增速 +10%） ----------------
        public void OnWipe()
        {
            // 团队复活（M4 后期增益）：本局第一次团灭时全队半血原地站起来，不算团灭
            if (Rogue.TryTeamRevive())
            {
                foreach (var h in Combat.heroes) if (h) h.Respawn(h.transform.position, 0.5f);
                Coop.wiped = false;
                Event("team_revive", routeTarget != null ? routeTarget.def.id : "", "");
                Combat.Toast("团队复活：全队以 50% 生命站起来 · 30 秒内伤害 +20%", new Color(1f, 0.9f, 0.5f));
                return;
            }
            if (finaleStarted && Economy.teamItem == "shield" && Economy.teamItemCount > 0)
            {
                // 原稿：护盾共鸣器 / 团队复活币类道具只在终局生效。这里把队伍格里的护盾共鸣器当复活币用（优先于增益复活）
                Economy.teamItemCount = 0; Economy.teamItem = null;
                foreach (var h in Combat.heroes) if (h) h.Respawn(h.transform.position, 0.5f);
                Coop.wiped = false;
                Event("item_revive", "H1", "");
                Combat.Toast("护盾共鸣器：全队以 50% 生命复活", new Color(0.9f, 0.95f, 1f));
                return;
            }
            if (finaleStarted) { wipes++; Event("wipe", "H1", "finale"); EndRun(false); return; }
            wipes++;
            erosionSpeed += wipes == 1 ? 0.05f : 0.1f;
            if (wipes >= 2) Coop.erosion = Mathf.Min(100f, Coop.erosion + 15f);
            Event("wipe", routeTarget != null ? routeTarget.def.id : "", $"erosionSpeed={erosionSpeed:0.00}");
            Combat.Toast($"全队覆灭 · 黑潮增速 +{(wipes == 1 ? 5 : 10)}%（第 {wipes} 次）· 3 秒后在安全处复活", new Color(1f, 0.45f, 0.4f));
            Invoke(nameof(Regroup), 3f);
        }

        void Regroup()
        {
            // 复活点：离黑潮最远的已清除节点，没有就回出生点
            Vector3 p = new Vector3(MapLayout.Spawn.x, 0f, MapLayout.Spawn.y);
            float far = -1f;
            foreach (var n in nodes)
                if (n.Done && n.Fightable && MapLayout.TideDist(n.def.pos) > far) { far = MapLayout.TideDist(n.def.pos); p = n.pos; }
            int i = 0;
            foreach (var h in Combat.heroes) h.Respawn(Combat.ClampToArena(p + Quaternion.Euler(0, i++ * 120f, 0) * Vector3.forward * 2.5f, 1f), 0.5f);
            Coop.wiped = false;
            routeTarget = null;
        }

        // ---------------- 黑潮范围标记：一圈青色描边 + 半透明的覆盖区 ----------------
        void BuildTideMarker()
        {
            Transform Make(string name, Mesh mesh, Color col)
            {
                var go = new GameObject(name);
                go.transform.position = Coop.tideCenter + Vector3.up * 0.04f;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                var mat = new Material(SliceAssets.I.telegraphShader);
                mat.SetColor("_Color", col);
                mat.SetFloat("_Progress", 1f);
                mat.SetFloat("_Sides", 0f);
                r.sharedMaterial = mat;
                return go.transform;
            }
            tideFill = Make("TideCoverage", Telegraph.Sector(0f, 1f, 360f, 160), new Color(0.05f, 0.35f, 0.33f, 0f));   // 地面着色器已经画出黑潮范围，这块半透明圆盘不再显示
            tideRing = Make("TideEdge", Telegraph.Sector(0.992f, 1f, 360f, 320), new Color(0.3f, 0.95f, 0.9f, 0f));   // 地面着色器自己画前沿，这条贴花边线（半径大时有近 1 米宽）不再显示
        }

        void UpdateTideMarker()
        {
            float r = Mathf.Max(0.5f, Coop.coverageRadius);
            if (tideFill) tideFill.localScale = new Vector3(r, 1f, r);
            if (tideRing) tideRing.localScale = new Vector3(r, 1f, r);
        }

        // ---------------- 时间轴记录 ----------------
        public string StatusLine
        {
            get
            {
                int cleared = 0; foreach (var n in nodes) if (n.Done && n.Fightable) cleared++;
                return $"第 {RunMinutes:0.0} 分钟    {Growth.TeamFracLevel():0.0} 级    侵蚀 {Coop.erosion:0}%    净化 {purified}/3    清除 {cleared}    倍速 ×{Speeds[speedIdx]:0}    路线 {strategy}{(HeroBase.Autopilot ? $" → {RouteName}" : "")}";
            }
        }

        // 割草方向重做：性能与密度仪表（每 5 秒采样，跑局窗口据此定阈值）
        float ftSum; int ftN; float ftWorst;

        void Sample()
        {
            int cleared = 0; foreach (var n in nodes) if (n.Done && n.Fightable) cleared++;
            float hp = 0f; int cnt = 0;
            foreach (var h in Combat.heroes) { hp += h.dmg.hp / h.dmg.maxHp; cnt++; }
            var p = Combat.player ? Combat.player.transform.position : Vector3.zero;
            // 帧率用非缩放时间：倍速跑局时这是真实每帧成本（判据 G6）
            float fpsAvg = ftN > 0 && ftSum > 0f ? ftN / ftSum : 0f;
            float fpsMin = ftWorst > 0f ? 1f / ftWorst : 0f;
            ftSum = 0f; ftN = 0; ftWorst = 0f;
            int awake = 0; foreach (var e in Combat.enemies) if (e && !e.dormant) awake++;   // 真实同屏压力（alive_now 含待命怪）
            csv.AppendLine(string.Join(",", Time.unscaledTime.ToString("0.0"), RunMinutes.ToString("0.00"), Coop.erosion.ToString("0.0"), Coop.coverageRadius.ToString("0"),
                purified, cleared, Coop.killsQ, Coop.killsJ, Coop.killsN, (cnt > 0 ? hp / cnt : 0f).ToString("0.00"), Coop.downs, wipes, Coop.teamSkills,
                p.x.ToString("0"), p.z.ToString("0"), routeTarget != null ? routeTarget.def.id : "-", strategy, LengthScale,
                Growth.TeamFracLevel().ToString("0.00"), Growth.expTotal.ToString("0"), EcoStats(), keys, Intel.RevealedShare().ToString("0.00"), Rogue.TeamPoints, Rogue.Unlocks(Rogue.TeamPoints),
                Combat.enemies.Count, Tide.Alive, fpsAvg.ToString("0.0"), fpsMin.ToString("0.0"), awake));
        }

        string EcoStats()
        {
            float c = 0f, e = 0f, s = 0f, st = 0f; int n = 0, used = 0;
            foreach (var h in Combat.heroes) { if (!h) continue; c += h.kit.coins; e += h.kit.earned; s += h.kit.spent; st += h.kit.weaponStar; n++; foreach (var u in h.kit.used.Values) used += u; }
            if (n == 0) return lastEco;
            return lastEco = string.Join(",", (c / n).ToString("0"), (e / n).ToString("0"), (s / n).ToString("0"), (st / n).ToString("0.00"), used);
        }

        string lastEco = "0,0,0,2,0";

        public void Event(string kind, string node, string detail)
        {
            events.AppendLine($"{RunMinutes:0.00},{kind},{node},{detail.Replace(',', ';')}");
            Debug.Log($"RUN_EVENT {RunMinutes:0.00} {kind} {node} {detail}");
        }

        void Save()
        {
            try
            {
                File.WriteAllText(logPath + ".csv", csv.ToString(), new UTF8Encoding(true));
                File.WriteAllText(logPath + "_events.csv", events.ToString(), new UTF8Encoding(true));
            }
            catch (System.Exception ex) { Debug.LogWarning("RunLog save failed: " + ex.Message); }
        }

        void OnDestroy()
        {
            if (I != this) return;
            Sample();
            Event("end", "", $"t={RunMinutes:0.0}min wipes={wipes} lv={Growth.TeamFracLevel():0.00} exp={Growth.expTotal:0}");
            Save();
            Feel.SetBaseScale(1f);
            I = null;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}

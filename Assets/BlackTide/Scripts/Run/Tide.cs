using UnityEngine;

namespace BlackTide
{
    // 割草方向重做 A 块：黑潮涌浪（Claude 总工 2026-10-11，用户拍板「爽快、割草、更多敌人」）。
    //
    // 症状（实测 run_20261011_131308，用户亲手一局 19.3 分钟）：刷怪是**纯节点驱动**——
    // RunDirector.Spawn 只在玩家走近节点时触发，节点清完场上就归零，跑图阶段没有靶子可割。
    // 一局总击杀只有 266，空窗时间约占三成。
    //
    // 这里加一条不停歇的怪潮：兵从黑潮方向成队向玩家推进。密度由侵蚀与局时驱动。
    // 新口径下**侵蚀是「密度旋钮」，不是「团灭威胁」**：怪多 → 杀得多 → 能量、经验、Buff 点数来得快
    // → 构筑更早成型，是正反馈爽点。唯一保留的压力来源是超频满 100 倒地。
    //
    // 性能：每只怪一个 Animator + 一个 MinionController.Update。主要开销是骨骼求值，
    // 已把敌人 Animator 改成 CullCompletely（见 CharacterAnim.Init 的 cullOffscreen）。
    // aliveCap / totalCap 是硬预算，宁可不刷也不突破；阈值全部暴露，跑局窗口按 CSV 的
    // alive_now / fps_avg / fps_min 实测调，不拍脑袋定死。设计目标见 Design/割草方向重做.md。
    public class Tide : MonoBehaviour
    {
        public static Tide I;
        public static int Alive;            // 场上涌浪活体数（MinionController.OnDeath 递减）

        [Header("启动门槛")]
        public float startErosion = 5f;     // 侵蚀到这个值才开始 trickle（此前只有节点怪；比追猎者的 30% 早得多）
        public string startToast = "黑潮开始涌动——敌人会从潮里持续过来";

        [Header("密度（侵蚀 + 局时驱动）")]
        public float intervalHi = 10f;      // 低密度时的波间隔（秒，游戏时间）
        public float intervalLo = 4f;       // 高密度时的波间隔
        public int squadLo = 4;             // 低密度每波只数
        public int squadHi = 14;            // 高密度每波只数
        public float erosionWeight = 0.72f; // 密度里侵蚀的占比（其余给局时）
        public float saturateMin = 16f;     // 局时到这一点算满

        [Header("预算（性能硬上限）")]
        public int aliveCap = 46;           // 涌浪自己的上限
        public int totalCap = 62;           // 全场敌人上限（含节点怪、追猎者、Boss 召唤）

        [Header("生成位置")]
        public float spawnNear = 22f;       // 距玩家的近环
        public float spawnFar = 38f;        // 距玩家的远环
        public float biasHalfAngle = 70f;   // 朝黑潮方向的张角（±度）

        [Header("精英混入")]
        public float eliteErosion = 50f;    // 侵蚀到这个值开始混入巨浪
        public float eliteMaxP = 0.35f;     // 每波混入一只巨浪的最高概率

        [Header("决战期间（黑潮王自己也在召怪，压低一点）")]
        public float finaleSquadMul = 0.5f;
        public float finaleIntervalMul = 1.5f;

        float nextWave = 1e9f;
        bool warned;

        public void Build()
        {
            I = this;
            Alive = 0;
            nextWave = 1e9f;
            warned = false;
        }

        void OnDestroy() { if (I == this) I = null; }

        // 密度 0–1：侵蚀占主导，局时兜底（防止玩家零净化拖时间就永远不难受）
        float Density()
        {
            float m = RunDirector.I ? RunDirector.I.RunMinutes : 0f;
            float ero = Mathf.Clamp01(Coop.erosion / 100f);
            float time = Mathf.Clamp01(m / saturateMin);
            return Mathf.Clamp01(ero * erosionWeight + time * (1f - erosionWeight));
        }

        void Update()
        {
            if (!Combat.player) return;
            if (Coop.erosion < startErosion) return;

            if (!warned)
            {
                warned = true;
                Combat.Toast(startToast, new Color(0.3f, 0.95f, 0.9f));
                nextWave = Time.time + 2f;
            }
            if (Time.time < nextWave) return;

            float d = Density();
            bool finale = RunDirector.I && RunDirector.I.finaleStarted;
            int squad = Mathf.RoundToInt(Mathf.Lerp(squadLo, squadHi, d));
            if (finale) squad = Mathf.Max(2, Mathf.RoundToInt(squad * finaleSquadMul));

            // 预算：涌浪上限 + 全场**交战**上限取小的那个。
            // ⚠️ 这里绝不能直接用 Combat.enemies.Count——它包含还没苏醒的待命节点怪（实测常态 38–100 只），
            // 那样 totalCap − count 常年为负，涌浪会被自己的预算饿死（15:17 那局 alive_tide 多数采样为 0 就是这个 bug）。
            int room = Mathf.Min(aliveCap - Alive, totalCap - ActiveEnemies());
            nextWave = Time.time + Mathf.Lerp(intervalHi, intervalLo, d)
                       * (finale ? finaleIntervalMul : 1f);
            if (room <= 0) return;
            squad = Mathf.Min(squad, room);

            SpawnSquad(squad, d);
        }

        // 只数"醒着的"敌人：待命的不构成同屏压力，也不该占怪潮预算
        static int ActiveEnemies()
        {
            int n = 0;
            foreach (var e in Combat.enemies) if (e && !e.dormant) n++;
            return n;
        }

        void SpawnSquad(int count, float d)
        {
            var pc = Flat(Combat.player.transform.position);

            // 生成点偏向黑潮中心一侧：读起来是「潮里涌出来的」，不是随机刷脸
            Vector3 toTide = Flat(Coop.tideCenter - pc);
            float baseAng = toTide.sqrMagnitude > 1f
                ? Mathf.Atan2(toTide.z, toTide.x)
                : Random.Range(0f, Mathf.PI * 2f);
            float a = baseAng + Random.Range(-biasHalfAngle, biasHalfAngle) * Mathf.Deg2Rad;
            float r = Random.Range(spawnNear, spawnFar);
            Vector3 c = Combat.ClampToArena(pc + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r, 2f);

            // 一波一个召唤圈：可读出「这一队要来了」，又不糊屏
            FX.SummonCircle(c, 0.7f, new Color(0.3f, 0.95f, 0.9f));

            bool elite = d > 0.35f && Coop.erosion >= eliteErosion && Random.value < Mathf.Clamp01((d - 0.35f) / 0.65f) * eliteMaxP;

            Rogue.Delay(0.7f, () =>
            {
                if (!I || !SliceGame.I || !Combat.player) return;
                for (int i = 0; i < count; i++)
                {
                    // 队列里挨个复检预算（同样只数已苏醒的）：这波刷到一半可能已经被节点战占满
                    if (Alive >= aliveCap || ActiveEnemies() >= totalCap) return;
                    bool bossOfWave = elite && i == 0;
                    Vector3 p = Combat.ClampToArena(
                        c + new Vector3(Random.Range(-4.5f, 4.5f), 0f, Random.Range(-4.5f, 4.5f)), 1f);

                    SliceGame.spawningTide = true;
                    bool ok;
                    try
                    {
                        // 轻波血量固定 ×1（设计目标 G4：保持 1–2 刀一个，加密度不加硬度）
                        ok = SliceGame.I.SpawnMinionTripo(p, 1f, 1f, bossOfWave ? 1.3f : 1f, null);
                    }
                    finally { SliceGame.spawningTide = false; }
                    if (!ok) return;

                    var mc = MinionController.Last;
                    if (!mc) return;
                    mc.dmg.tide = true;
                    mc.tide = true;                       // OnDeath 递减用
                    Alive++;
                    mc.dmg.RefreshRenderers();
                }
            });
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}

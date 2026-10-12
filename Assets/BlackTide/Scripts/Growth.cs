using UnityEngine;

namespace BlackTide
{
    // 成长（M2）：只有击杀给经验；等级表来自原稿（网页原型 SPEC 3.1 / gameConfig.ts levels）
    // 现有角色数值是按约 6 级（伤害 ×2.0、生命 ×2.7）调出来的，所以这里统一归一到 6 级：
    // 实际伤害 = 现有基础值 × 等级伤害倍率 / 2.0，生命同理 / 2.7 —— 6 级时手感和竞技场完全一样。
    // 竞技场（非开放地图）不走成长，固定 6 级。
    public static class Growth
    {
        public const int MaxLevel = 10, BaseLevel = 6;
        public const int EchoAt = 2, UltAt = 3, TeamAt = 5;     // 原稿：2 级声骸、3 级共鸣解放、5 级团队技能
        public const float TeamShare = 0.85f;                   // 三人时每人拿 0.85 倍经验
        static readonly float[] DmgTable = { 1.0f, 1.3f, 1.5f, 1.7f, 1.9f, 2.0f, 2.1f, 2.2f, 2.3f, 2.5f };
        static readonly float[] HpTable = { 1.0f, 1.5f, 1.8f, 2.1f, 2.4f, 2.7f, 3.0f, 3.3f, 3.9f, 4.5f };

        public static bool Enabled => SliceGame.RunMode;
        public static int StartLevel => Enabled ? 1 : BaseLevel;

        static int Idx(int lv) => Mathf.Clamp(lv, 1, MaxLevel) - 1;
        public static float DmgRaw(int lv) => DmgTable[Idx(lv)];
        public static float HpRaw(int lv) => HpTable[Idx(lv)];
        public static float DmgMult(int lv) => DmgTable[Idx(lv)] / DmgTable[BaseLevel - 1];
        public static float HpMult(int lv) => HpTable[Idx(lv)] / HpTable[BaseLevel - 1];

        // 第 n 级升到 n+1 级所需经验：100 + 96 × (n−1)，满级累计 4356
        public static float ExpNeed(int lv) => 100f + 96f * (lv - 1);

        // 击杀经验 = 轻波基础值 × 等级倍数。倍数用原稿「货币经验获取」一节：巨浪 5 倍、怒涛 30 倍、海啸 80 倍；
        // 信使按原稿「40–80 倍轻波」取 70 倍（RunDirector 里设）。原稿规定经验只来自击杀，挑战和宝箱不产出。
        // 用三局实测击杀数对设计检查点拟合，原稿这组比例几乎就是最优解（网页原型的 80 / 450 偏离了原稿，后期升得太快）。
        // 基础值用公式模拟（RunSim，含「升级慢 → 清得慢」的反馈）扫出来：11 时标准路线 3 / 7 / 10.5 分钟 = 3.4 / 4.9 / 6.2 级，打完信使 9.0 级。
        // 推导见 HANDOFF「M2 经验校准」「公式模拟」和 Tools/fit_exp.py。
        // 2026-10-09 重调：11 → 8.8。M3–M5 之后（武器、增益、新兵种）清怪快了约 1.2 倍，实测中段快约 1 级；
        // 菜单「经验重调（M3 之后回测 + 扫描）」：×0.8 时三条路线平均 3 / 7 / 10.5 分钟 = 3.1 / 5.0 / 6.4 级，约 8.6 级进终局（Captures/sim/sim_exp_*.txt）
        // 2026-10-09 第二次（同日）：8.8 实测三局平均 3 / 7 / 10.5 分钟 = 3.4 / 5.6 / 7.0 级，中段仍快约 0.9 级；
        // 扫描里经验每降 10%，10.5 分钟约降 0.5 级 → 再降约 16%：8.8 → 7.4（模拟预测约 3.2 / 4.9 / 6.4 级）
        // 2026-10-11 第三次（割草方向重做 CL-001）：7.4 → 3.7。节点密度翻倍 + 怪潮之后，
        // headless 实测 10.43 分钟就满级 10.00（原 19.3 分钟才 9.97），击杀率 12→45/分钟。
        // 目标是「同样的局长、多 3–4 倍的击杀」，所以按击杀增倍数回调，而不是砍怪。
        // ⚠️ RunSim 不认识 Tide，这条不能用公式模拟预测，下一窗口用 team_level 曲线实测校准。
        public const float ExpBase = 3.7f;
        static readonly float[] TierMult = { 1f, 5f, 30f, 80f };
        public static float TierExp(int tier) => ExpBase * TierMult[Mathf.Clamp(tier, 0, 3)];
        public static float ExpOf(Damageable e) => e.expValue > 0f ? e.expValue : TierExp(e.tier);
        public const float MessengerExp = ExpBase * 70f;

        // 10 级门槛（可选规则，默认关）：设成 3 = 净化满 3 个裂隙之前最高 9 级，经验照常累积，净化后补升。
        // 拟合结果里第三个裂隙之前约 9.7 级，靠数值已经满足「10 级要第三个裂隙」，所以先不开
        public const int PurifyForMax = 0;
        public static int LevelCap => Enabled && RunDirector.I && RunDirector.I.purified < PurifyForMax ? MaxLevel - 1 : MaxLevel;

        public static float expTotal;   // 本局每人累计拿到的经验（验收用）

        public static void Reset() { expTotal = 0f; }

        // 割草方向重做：涌浪兵是持续刷出来的，全额给经验会让满级更早（实测 15.4 分钟已经 9 级）。
        // 打折而不是清零——割草的击杀必须有回报，否则打它没意义；额度留给跑局窗口按 team_level 曲线调。
        public const float TideExpMul = 0.35f, TideEliteExpMul = 0.55f;

        public static void OnKill(Damageable e)
        {
            if (!Enabled || !e) return;
            GiveTeam(ExpOf(e) * (e.tide ? (e.tier >= 1 ? TideEliteExpMul : TideExpMul) : 1f));
        }

        static void GiveTeam(float raw)
        {
            float x = raw * TeamShare;
            expTotal += x;
            foreach (var h in Combat.heroes) if (h) h.AddExp(x);
        }

        // 小数等级：等级 + 当前经验进度（曲线和检查点对照用）
        public static float FracLevel(HeroBase h)
        {
            if (h.level >= MaxLevel) return MaxLevel;
            return h.level + Mathf.Min(0.99f, h.exp / ExpNeed(h.level));   // 卡在 9 级门槛时显示 9.99
        }

        static float lastTeamLevel;
        public static float TeamFracLevel()
        {
            float s = 0f; int n = 0;
            foreach (var h in Combat.heroes) if (h) { s += FracLevel(h); n++; }
            // 停止 Play 时角色先被销毁、记录后写，这时沿用最后一次的有效值（否则结束行会记成 0 级）
            if (n > 0) lastTeamLevel = s / n;
            return lastTeamLevel;
        }

        public static string UnlockText(int lv) =>
            lv == EchoAt ? "解锁声骸技能（R）" :
            lv == UltAt ? "解锁共鸣解放（Q）" :
            lv == TeamAt ? "解锁团队技能（协奏接力）" : "";
    }
}

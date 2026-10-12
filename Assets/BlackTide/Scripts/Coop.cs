using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 共斗规则（数值来源：网页原型 SPEC.md 第 3、4 节 / gameConfig.ts）
    // 协奏接力与团队技能、侵蚀与超频参数、全灭判定、本场统计
    public static class Coop
    {
        // ---- 协奏接力 → 团队技能：6 秒内两名不同角色先后接力 ----
        public const float RelayWindow = 6f;
        public static int teamSkillCharges = 2;          // 对应 SPEC 遭遇预设「野图怒涛」ts = 2
        // 战略协议（M4，原稿：净化裂隙「为团队解锁一个强大的战略协议技能」）：每个裂隙抽一种没抽过的，给 2 次；团队技能按顺序用掉。
        // 列表空时（竞技场）团队技能就是原来的潮汐共鸣
        public static readonly List<string> protocols = new List<string>();
        public static readonly string[] ProtocolIds = { "tide", "wall", "purge", "chrono" };
        public static string ProtocolName(string id) => id == "wall" ? "潮壁协议" : id == "purge" ? "净潮协议" : id == "chrono" ? "时序协议" : "潮汐共鸣";
        public static string ProtocolDesc(string id) => id == "wall" ? "全队 40% 护盾 8 秒，推开周围敌人并积累大量架势" :
            id == "purge" ? "9 米大范围高伤害，侵蚀 −5%" : id == "chrono" ? "全队技能和声骸冷却刷新、共鸣能量 +50、6 秒内伤害 +25%" : "大范围爆发，全队回复 15% 生命";
        static readonly List<(HeroBase h, float t)> relays = new List<(HeroBase, float)>();
        public static float lastRelayT = -99f;
        public static HeroBase lastRelayer;

        // ---- 侵蚀与超频 ----
        public static float erosion = 40f;               // 竞技场：野图怒涛预设约 40%，F3 切换；开放地图：由 RunDirector 按时间推进
        // 黑潮覆盖范围（SPEC 决定 9：侵蚀值全局增长，只对黑潮覆盖范围内的敌人和玩家生效）。coverageRadius < 0 表示到处都生效（竞技场）
        public static float coverageRadius = -1f;
        public static Vector3 tideCenter;
        public static bool InTide(Vector3 p)
        {
            if (coverageRadius < 0f) return true;
            p.y = 0f; Vector3 c = tideCenter; c.y = 0f;
            return Vector3.Distance(p, c) <= coverageRadius;
        }
        public static float ErosionAt(Vector3 p) => InTide(p) ? erosion : 0f;
        public static bool OcActive => erosion >= 45f;
        public static bool OcActiveAt(Vector3 p) => ErosionAt(p) >= 45f;
        public const float OcDecay = 0.8f, OcMaxBonus = 0.35f, OcGainScale = 0.8f, OcChantGain = 15f;
        public static readonly float[] OcAlerts = { 20f, 50f, 80f };
        static readonly Dictionary<string, Vector2> ocGain = new Dictionary<string, Vector2>
        {
            { "basic", new Vector2(0.1f, 0.2f) }, { "skill", new Vector2(5f, 10f) }, { "ult", new Vector2(10f, 20f) }, { "echo", new Vector2(7.5f, 15f) },
        };
        // 原文数值：侵蚀 45% → 100% 线性插值；原型实测后整体 ×0.8、衰减 0.8/秒
        public static float OcGain(string kind, float e)
        {
            if (e < 45f || !ocGain.TryGetValue(kind, out var g)) return 0f;
            return Mathf.Lerp(g.x, g.y, (e - 45f) / 55f) * OcGainScale;
        }
        // 敌人血量随侵蚀上涨，按原稿分段（0→1.0、15→1.2、45→1.6、100→2.5）；伤害取其一半涨幅（网页原型 enemyDmgFromHp = 0.5）
        public static float HpMultFor(float e)
        {
            if (e <= 15f) return Mathf.Lerp(1f, 1.2f, e / 15f);
            if (e <= 45f) return Mathf.Lerp(1.2f, 1.6f, (e - 15f) / 30f);
            return Mathf.Lerp(1.6f, 2.5f, (e - 45f) / 55f);
        }
        public static float EnemyHpMultAt(Vector3 p) => HpMultFor(ErosionAt(p));
        // DS-028 前期启动宽限（用户反馈「前期敌人多且血厚、玩家启动太差 → 煎熬」）：
        // 前 3 分钟小兵血量 ×0.65，到第 6 分钟线性回到 1.0。只削前期，中后期一点不动。
        // 落点在 MinionController/RayController 设置 maxHp 的唯一位置，因此对节点怪/涌浪/追猎者/Boss 召唤一体生效，Boss 本体不受影响。
        public static float OpeningHpRelief()
        {
            if (!RunDirector.I) return 1f;
            float m = RunDirector.I.RunMinutes;
            if (m >= 6f) return 1f;
            return Mathf.Lerp(0.65f, 1f, Mathf.Clamp01((m - 3f) / 3f));
        }
        public static float EnemyDmgScale = 1.25f;   // 2026-10-09 整局验证：全程零倒地，敌人伤害整体 ×1.15
        // 割草方向重做：开局宽限。实测两局都在第一分钟掉到 60–65% 血（出生点 40 秒内挤着 S1/S2/P1 三个点），
        // 13:34 那局干脆 0.6 分钟就退了——第一印象是「被围殴」而不是「我在割」。
        // 首 60 秒小兵伤害 ×0.7，到 90 秒线性回到正常。注意 BossController 也用这个系数，
        // 但宽限只在前 1.5 分钟，实测里最早接战的怒涛在 2.5 分钟、黑潮王在 18 分钟，都落在窗口外；竞技场 RunDirector 为空也不受影响。
        public static float OpeningGrace()
        {
            if (!RunDirector.I) return 1f;
            float m = RunDirector.I.RunMinutes;
            if (m >= 1.5f) return 1f;
            return Mathf.Lerp(0.7f, 1f, Mathf.Clamp01((m - 1f) / 0.5f));
        }
        public static float EnemyDmgMultAt(Vector3 p) => (1f + (HpMultFor(ErosionAt(p)) - 1f) * 0.5f) * EnemyDmgScale * OpeningGrace();
        // 技能冷却随侵蚀线性增加，最多 +10%（侵蚀 15% 时达到）
        public static float CdMultAt(Vector3 p) => 1f + 0.1f * Mathf.Clamp01(ErosionAt(p) / 15f);

        // ---- 本场统计（结算和验收用） ----
        public static float startT;
        public static int teamSkills, chantsCut, chantsHit, breaks, downs, perfectDodges;
        public static bool wiped;
        // 击杀计数（按等级）：原稿的 Buff 计数 轻波 1 / 巨浪 8 / 怒涛 30
        public static int killsQ, killsJ, killsN;
        public static System.Action<Damageable> onKill;
        // 割草方向重做：收割连击（1.6 秒内连续击杀的计数）。连击数计的是「打中多少下」，
        // 但一屏刷掉几百只的味道来自「收割了多少只」，两个都要看得见。DamageText 的连击行读这两个字段。
        public static int killStreak;
        public static float killStreakUntil;

        public static void OnKill(Damageable e)
        {
            if (e.tier >= 2) killsN++; else if (e.tier == 1) killsJ++; else killsQ++;
            if (Time.time > killStreakUntil) killStreak = 0;
            killStreak++; killStreakUntil = Time.time + 1.6f;
            KillFeedback(e);
            Growth.OnKill(e);
            Economy.OnKill(e);
            Rogue.OnKill(e);
            Echoes.OnKill(e);
            Weapons.OnKill(e);
            onKill?.Invoke(e);
        }

        // 击杀反馈（2026-10-09 打磨）：最后一刀要和普通一刀不一样——敌人炸成一团黑潮、扩一圈光环，玩家附近时补一点顿帧
        static void KillFeedback(Damageable e)
        {
            if (!e) return;
            bool elite = e.tier >= 1 || e.bossCtl;
            Vector3 c = e.Center;
            FX.Tide(c, Vector3.up, elite ? 70 : 28);
            FX.Sparks(c, Vector3.up, elite ? 30 : 12, new Color(0.6f, 1f, 0.95f));
            FX.Ring(e.transform.position, 0.2f, elite ? 4.2f : 2.2f, elite ? 0.5f : 0.32f, new Color(0.4f, 1f, 0.92f, 0.85f), elite ? 0.2f : 0.12f);
            var p = Combat.player;
            if (!p || Feel.I == null) return;
            Vector3 d = p.transform.position - e.transform.position; d.y = 0f;
            if (d.magnitude > 14f) return;   // 远处的击杀不打断节奏
            if (e.bossCtl) return;           // Boss 死亡有自己的演出
            // 割草方向重做：顿帧必须限流。原来每只近身击杀都要 0.03 秒顿帧，一波群体爆炸炸掉二三十只
            // 就会把时间连续冻住——几百只的场面下这不是打击感，是卡屏。改成最快每 0.1 秒一次，
            // 并且群体击杀时用一次更强的震屏代替多次顿帧（让「一下刷掉一片」被感知成一下，而不是很多下）。
            if (Time.time < nextKillFeelAt) return;
            nextKillFeelAt = Time.time + 0.1f;
            Feel.I.HitStop(elite ? 0.09f : 0.03f);
            if (elite) { Feel.I.Shake(0.45f); Feel.I.PulseHit(0.5f); }
        }
        static float nextKillFeelAt;

        public static void Reset()
        {
            relays.Clear();
            teamSkillCharges = 2;
            protocols.Clear();
            lastRelayT = -99f; lastRelayer = null;
            teamSkills = chantsCut = chantsHit = breaks = downs = perfectDodges = 0;
            killsQ = killsJ = killsN = 0;
            killStreak = 0; killStreakUntil = 0f; nextKillFeelAt = 0f;
            Growth.Reset();
            onKill = null;
            coverageRadius = -1f;
            wiped = false;
            startT = Time.time;
        }

        public static IEnumerable<HeroBase> Alive()
        {
            foreach (var h in Combat.heroes) if (h && !h.dmg.dead) yield return h;
        }

        public static HeroBase Teammate(HeroBase self, int k)
        {
            int i = 0;
            foreach (var h in Combat.heroes)
            {
                if (h == self) continue;
                if (i == k) return h;
                i++;
            }
            return null;
        }

        public static Vector3 Centroid()
        {
            Vector3 c = Vector3.zero; int n = 0;
            foreach (var h in Alive()) { c += h.transform.position; n++; }
            return n > 0 ? c / n : Vector3.zero;
        }

        public static void Relay(HeroBase from, HeroBase to)
        {
            relays.RemoveAll(r => !r.h || Time.time - r.t > RelayWindow);
            HeroBase prev = null;
            foreach (var r in relays) if (r.h != from) { prev = r.h; break; }
            relays.Add((from, Time.time));
            lastRelayT = Time.time; lastRelayer = from;
            // 团队技能 5 级解锁（原稿）：两名接力者都要到 5 级
            if (prev && teamSkillCharges > 0 && prev.TeamUnlocked && from.TeamUnlocked)
            {
                teamSkillCharges--;
                relays.Clear();
                TeamSkill(prev, from);
            }
        }

        static readonly List<Damageable> tmp = new List<Damageable>();

        // 团队技能「潮汐共鸣」：以 Boss（没有就以最近的敌人）为中心的大范围爆发，全队获得护盾
        static void TeamSkill(HeroBase a, HeroBase b)
        {
            string pid = protocols.Count > 0 ? protocols[0] : "tide";
            if (protocols.Count > 0) protocols.RemoveAt(0);
            if (RunDirector.I) RunDirector.I.Event("team_skill", "", $"{ProtocolName(pid)} {a.HeroName}+{b.HeroName}");
            if (pid != "tide") { Protocol(pid, a, b); return; }
            Vector3 c = Centroid();
            Damageable center = null;
            var nb = Combat.NearestBoss(c, 14f);
            if (nb) center = nb.dmg;
            else
            {
                float best = float.MaxValue;
                foreach (var e in Combat.enemies)
                {
                    if (!e || e.dead) continue;
                    float d = Vector3.Distance(e.transform.position, c);
                    if (d < best) { best = d; center = e; }
                }
            }
            Vector3 p = center ? center.transform.position : c;
            p.y = 0f;
            var alive = new List<HeroBase>(Alive());
            tmp.Clear();
            foreach (var e in Combat.enemies) if (e && !e.dead && Vector3.Distance(e.transform.position, p) <= 6.5f + e.radius) tmp.Add(e);
            foreach (var h in alive)
            {
                foreach (var e in tmp)
                {
                    Vector3 dir = e.transform.position - h.transform.position; dir.y = 0f;
                    h.DealHit(e, 8f, 240f / Mathf.Max(1, alive.Count), 6f, dir.normalized, true, "team", 0f, false);
                }
                h.Heal(h, h.dmg.maxHp * 0.15f, true, 8f);
            }
            teamSkills++;
            FX.Ring(p, 0.5f, 9f, 0.7f, new Color(1f, 0.55f, 0.25f, 1f), 0.25f);
            FX.Ring(p, 0.3f, 7.5f, 0.6f, new Color(0.3f, 0.95f, 0.85f, 1f), 0.2f);
            FX.Ring(p, 0.2f, 6f, 0.5f, new Color(1f, 0.85f, 0.45f, 1f), 0.16f);
            FX.Tide(p + Vector3.up * 2f, Vector3.up, 120);
            FX.Embers(p + Vector3.up, 60);
            FX.Sparks(p + Vector3.up * 2f, Vector3.up, 80, new Color(1f, 0.85f, 0.5f));
            Feel.I.Sfx("ult_hit", 1f, 0f, 0.85f);
            Feel.I.HitStop(0.2f);
            Feel.I.Shake(1f);
            Feel.I.PulseHit(1f);
            Combat.Toast($"团队技能 · 潮汐共鸣（{a.HeroName} × {b.HeroName}）  剩余 {teamSkillCharges} 次", new Color(1f, 0.85f, 0.45f));
        }

        static void Protocol(string pid, HeroBase a, HeroBase b)
        {
            Vector3 c = Centroid(); c.y = 0f;
            var alive = new List<HeroBase>(Alive());
            Color col = pid == "wall" ? new Color(0.9f, 0.95f, 1f) : pid == "purge" ? new Color(0.35f, 1f, 0.9f) : new Color(1f, 0.85f, 0.45f);
            switch (pid)
            {
                case "wall":
                    foreach (var h in alive) { h.dmg.shield = Mathf.Max(h.dmg.shield, h.dmg.maxHp * 0.4f); h.shieldUntil = Time.time + 8f; }
                    tmp.Clear();
                    foreach (var e in Combat.enemies) if (e && !e.dead && !e.dormant && Vector3.Distance(Flat(e.transform.position), c) <= 6.5f + e.radius) tmp.Add(e);
                    foreach (var e in tmp) a.DealHit(e, 2f, 120f, 8f, Flat(e.transform.position - c).normalized, true, "team", 0f, false);
                    break;
                case "purge":
                {
                    Vector3 p = c;
                    var nb = Combat.NearestBoss(c, 14f);
                    if (nb) { p = nb.transform.position; p.y = 0f; }
                    tmp.Clear();
                    foreach (var e in Combat.enemies) if (e && !e.dead && !e.dormant && Vector3.Distance(Flat(e.transform.position), p) <= 9f + e.radius) tmp.Add(e);
                    foreach (var h in alive) foreach (var e in tmp) h.DealHit(e, 10f, 80f, 6f, Flat(e.transform.position - p).normalized, true, "team", 0f, false);
                    erosion = Mathf.Max(0f, erosion - 5f);
                    FX.Tide(p + Vector3.up * 2f, Vector3.up, 160);
                    c = p;
                    break;
                }
                case "chrono":
                    foreach (var h in alive)
                    {
                        h.skillReadyAt = 0f; h.echoReadyAt = 0f;
                        h.Gain(50f, 0f);
                        h.AddMod("dmg", "chrono", 0.25f, 6f);
                    }
                    break;
            }
            teamSkills++;
            FX.Ring(c, 0.5f, 9f, 0.7f, new Color(col.r, col.g, col.b, 1f), 0.25f);
            FX.Ring(c, 0.3f, 6f, 0.5f, new Color(1f, 1f, 1f, 0.9f), 0.16f);
            FX.Sparks(c + Vector3.up * 2f, Vector3.up, 80, col);
            Feel.I.Sfx("ult_hit", 1f, 0f, 0.85f);
            Feel.I.HitStop(0.15f);
            Feel.I.Shake(0.8f);
            Feel.I.PulseHit(0.8f);
            Combat.Toast($"团队技能 · {ProtocolName(pid)}（{a.HeroName} × {b.HeroName}）：{ProtocolDesc(pid)}  剩余 {teamSkillCharges} 次", col);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        public static void Tick()
        {
            if (wiped || Combat.heroes.Count == 0) return;
            foreach (var h in Combat.heroes) if (h && !h.Downed) return;
            wiped = true;
            if (SliceGame.I) SliceGame.I.OnTeamWipe();
        }

        public static string Report(float fightTime)
        {
            float total = 0f;
            foreach (var h in Combat.heroes) total += h.dmgDone;
            var sb = new System.Text.StringBuilder();
            foreach (var h in Combat.heroes)
                sb.Append($"{h.HeroName} {(total > 0 ? h.dmgDone / total * 100f : 0f):0}%  ");
            return $"输出占比  {sb}\n团队技能 {teamSkills}  失衡 {breaks}  打断咏唱 {chantsCut}  吃到咏唱 {chantsHit}  倒地 {downs}  完美闪避 {perfectDodges}";
        }
    }
}

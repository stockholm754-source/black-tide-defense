using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BlackTide
{
    // 公式模拟的菜单：不开 Play，几秒算完。结果写到 Captures/sim/
    public static class RunSimMenu
    {
        const int Seeds = 40;
        const float Minutes = 30f;

        [MenuItem("BlackTide/公式模拟/三条路线（现行数值）")]
        public static void Current() => Go("current", null);

        // 回测：用实测那三局当时的经验值（网页原型 10/80/450，信使 900），对照 HANDOFF「M2 实测」检验模拟器本身准不准
        [MenuItem("BlackTide/公式模拟/回测（实测时的旧经验值）")]
        public static void Backtest() => Go("backtest", c => { c.tierExp = new[] { 10f, 80f, 450f, 800f }; c.messengerExp = 900f; c.purifyForMax = 0; c.economy = false; });

        // M5：漩涡值不值——同样的种子，打漩涡 vs 不打
        [MenuItem("BlackTide/公式模拟/终局：打不打漩涡")]
        public static void VortexCompare()
        {
            var sb = new StringBuilder("漩涡对照：三条路线 × 40 局，同种子\n路线 | 打不打 | 进终局分钟 | 进终局等级 | 决战秒 | 胜率 | 强制终局 | 净化数\n");
            foreach (RunDirector.Strategy st in System.Enum.GetValues(typeof(RunDirector.Strategy)))
                foreach (bool vx in new[] { true, false })
                {
                    float min = 0, lv = 0, sec = 0, pur = 0; int n = 0, win = 0, forced = 0;
                    for (int s = 1; s <= Seeds; s++)
                    {
                        var r = RunSim.Run(new RunSim.Config { strategy = st, seed = s * 7919 + (int)st, minutes = 32f, vortex = vx });
                        if (!r.reachedFinale) continue;
                        n++; min += r.finaleMin; lv += r.finaleLv; sec += r.finaleSec; pur += r.purified;
                        if (r.victory) win++; if (r.forced) forced++;
                    }
                    sb.AppendLine($"{st} | {(vx ? "打" : "不打")} | {min / n:0.0} | {lv / n:0.0} | {sec / n:0} | {100f * win / n:0}% | {100f * forced / n:0}% | {pur / n:0.0}");
                }
            string dir = Path.Combine(Application.dataPath, "..", "Captures", "sim");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"sim_vortex_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            Debug.Log("SIM_VORTEX → " + path + "\n" + sb);
        }

        // M5：扫黑潮王血量倍数。目标（原稿心流）：约 8.5 级、有信使声骸时决战约 240 秒；没声骸明显更难
        [MenuItem("BlackTide/公式模拟/终局：扫黑潮王血量")]
        public static void SovereignSweep()
        {
            var sb = new StringBuilder("黑潮王扫描：血量倍数 × 风险系数，三条路线 × 40 局（数字：均值）\n血量倍数 | 路线 | 进终局比例 | 进终局分钟 | 进终局等级 | 有声骸：用时 / 胜率 | 没声骸：用时 / 胜率 | 强制终局比例\n");
            foreach (float hp in new[] { 2f, 3f, 4f, 5f, 6f })
            foreach (float hz in new[] { 0.1f, 0.2f, 0.35f, 0.5f })
                foreach (RunDirector.Strategy st in System.Enum.GetValues(typeof(RunDirector.Strategy)))
                {
                    int n = 0, reach = 0, forced = 0, eN = 0, eW = 0, nN = 0, nW = 0; float min = 0, lv = 0, eT = 0, nT = 0;
                    for (int s = 1; s <= Seeds; s++)
                    {
                        var r = RunSim.Run(new RunSim.Config { strategy = st, seed = s * 7919 + (int)st, minutes = 32f, sovereignHp = hp, sovereignHazard = hz });
                        n++;
                        if (!r.reachedFinale) continue;
                        reach++; min += r.finaleMin; lv += r.finaleLv; if (r.forced) forced++;
                        if (r.echo) { eN++; eT += r.finaleSec; if (r.victory) eW++; } else { nN++; nT += r.finaleSec; if (r.victory) nW++; }
                    }
                    string E(int c, float tt, int w) => c > 0 ? $"{tt / c:0} 秒 / {100f * w / c:0}%（{c} 局）" : "—";
                    sb.AppendLine($"{hp,4} | {hz,4} | {st} | {100f * reach / n:0}% | {(reach > 0 ? min / reach : 0):0.0} | {(reach > 0 ? lv / reach : 0):0.0} | {E(eN, eT, eW)} | {E(nN, nT, nW)} | {(reach > 0 ? 100f * forced / reach : 0):0}%");
                }
            string dir = Path.Combine(Application.dataPath, "..", "Captures", "sim");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"sim_sovereign_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            Debug.Log("SIM_SOVEREIGN → " + path + "\n" + sb);
        }

        // M4：扫增益门槛。每个方案三条路线 × 40 局，看「进入终局前」（按 22 分钟算）每人能解锁几次。
        // 原稿目标：保底约 4、普通约 6、完美约 10。这里把所有局的 10% 分位当保底、稳健路线均值当普通、所有局的 90% 分位当完美。
        [MenuItem("BlackTide/公式模拟/肉鸽：扫增益门槛")]
        public static void BuffSweep()
        {
            var plans = new (string name, int[] th, int step)[]
            {
                ("原稿 0/20/60/150/270/400 +50", new[] { 0, 20, 60, 150, 270, 400 }, 50),
                ("原稿门槛，之后 +100", new[] { 0, 20, 60, 150, 270, 400 }, 100),
                ("原稿 ×1.5，之后 +100", new[] { 0, 30, 90, 225, 405, 600 }, 100),
                ("0/40/120/250/400/600 +150", new[] { 0, 40, 120, 250, 400, 600 }, 150),
                ("0/40/120/250/420/650 +200", new[] { 0, 40, 120, 250, 420, 650 }, 200),
                ("原稿 ×2，之后 +150", new[] { 0, 40, 120, 300, 540, 800 }, 150),
            };
            // 先跑一遍拿到每局 22 分钟的击杀数（门槛不影响战斗，只算一次）
            var runs = new List<(RunDirector.Strategy st, RunSim.Sample s18, RunSim.Sample s22, RunSim.Sample s25)>();
            foreach (RunDirector.Strategy st in System.Enum.GetValues(typeof(RunDirector.Strategy)))
                for (int s = 1; s <= Seeds; s++)
                {
                    var r = RunSim.Run(new RunSim.Config { strategy = st, seed = s * 7919 + (int)st, minutes = 25.5f });
                    RunSim.Sample At(float m) { var b = r.samples[0]; foreach (var x in r.samples) if (x.t <= m) b = x; return b; }
                    runs.Add((st, At(18f), At(22f), At(25f)));
                }
            var sb = new StringBuilder("增益门槛扫描：三条路线 × 40 局；数字是每人解锁次数\n方案 | 22 分钟：稳健 / 激进 / 贪信使（均值） | 全部局 10% 分位（保底）| 90% 分位（完美）| 18 / 25 分钟稳健均值\n");
            foreach (var p in plans)
            {
                int U(RunSim.Sample x) => Rogue.Unlocks(RunSim.BuffPoints(x), p.th, p.step);
                float Mean(RunDirector.Strategy st, System.Func<(RunDirector.Strategy, RunSim.Sample, RunSim.Sample, RunSim.Sample), int> f) { float t = 0; int n = 0; foreach (var r in runs) if (r.st == st) { t += f(r); n++; } return t / n; }
                var all = new List<int>(); foreach (var r in runs) all.Add(U(r.s22)); all.Sort();
                sb.AppendLine($"{p.name} | {Mean(RunDirector.Strategy.稳健发育, r => U(r.Item3)):0.0} / {Mean(RunDirector.Strategy.激进净化, r => U(r.Item3)):0.0} / {Mean(RunDirector.Strategy.贪信使, r => U(r.Item3)):0.0} | {all[all.Count / 10]} | {all[all.Count * 9 / 10]} | {Mean(RunDirector.Strategy.稳健发育, r => U(r.Item2)):0.0} / {Mean(RunDirector.Strategy.稳健发育, r => U(r.Item4)):0.0}");
            }
            var pts = new List<int>(); foreach (var r in runs) pts.Add(RunSim.BuffPoints(r.s22)); pts.Sort();
            sb.AppendLine($"\n22 分钟团队计数：10% 分位 {pts[pts.Count / 10]}，中位 {pts[pts.Count / 2]}，90% 分位 {pts[pts.Count * 9 / 10]}");
            string dir = Path.Combine(Application.dataPath, "..", "Captures", "sim");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"sim_buff_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            Debug.Log("SIM_BUFF → " + path + "\n" + sb);
        }

        // M3 之后：用修复后的三局实测拟合清怪加速系数，再扫经验倍数
        [MenuItem("BlackTide/公式模拟/经验重调（M3 之后回测 + 扫描）")]
        public static void ExpRetune()
        {
            string a = RunSim.CalibrateM3(Seeds);
            string b = RunSim.ExpSweep(RunSim.PostM3Boost, Seeds);
            string dir = Path.Combine(Application.dataPath, "..", "Captures", "sim");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"sim_exp_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, a + "\n" + b, new UTF8Encoding(true));
            Debug.Log("SIM_EXP → " + path + "\n" + a + "\n" + b);
        }

        // M3：扫货币基础值，看各品质买到限购量的几成（目标：基础、珍稀约 75%，传说买到 1 个）
        [MenuItem("BlackTide/公式模拟/经济：扫货币基础值")]
        public static void CoinSweep()
        {
            var sb = new StringBuilder("经济扫描：每档三条路线 × 40 局，数字是平均值\n轻波贝币 | 路线 | 收入 | 花掉 | 剩 | 基础% | 珍稀% | 传说% | 18 分钟星级 | 7/18 分钟等级\n");
            foreach (float cb in new[] { 2f, 3f, 4f, 5f, 6f, 8f, 10f })
                foreach (RunDirector.Strategy st in System.Enum.GetValues(typeof(RunDirector.Strategy)))
                {
                    float inc = 0, sp = 0, left = 0, b0 = 0, b1 = 0, b2 = 0, star = 0, l7 = 0, l18 = 0; int n = 40;
                    for (int s = 1; s <= n; s++)
                    {
                        var r = RunSim.Run(new RunSim.Config { strategy = st, seed = s * 7919 + (int)st, minutes = 25f, coinBase = cb });
                        inc += r.Avg(k => k.earned); sp += r.Avg(k => k.spent); left += r.Avg(k => k.coins);
                        b0 += r.BoughtShare(Rarity.基础); b1 += r.BoughtShare(Rarity.珍稀); b2 += r.BoughtShare(Rarity.传说);
                        RunSim.Sample at18 = r.samples[0]; foreach (var x in r.samples) if (x.t <= 18f) at18 = x;
                        star += at18.star; l7 += r.LevelAt(7f); l18 += r.LevelAt(18f);
                    }
                    sb.AppendLine($"{cb,4} | {st} | {inc / n:0} | {sp / n:0} | {left / n:0} | {b0 / n * 100:0} | {b1 / n * 100:0} | {b2 / n * 100:0} | {star / n:0.00} | {l7 / n:0.0}/{l18 / n:0.0}");
                }
            string dir = Path.Combine(Application.dataPath, "..", "Captures", "sim");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"sim_coins_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            Debug.Log("SIM_COINS → " + path + "\n" + sb);
        }

        // 重新拟合 Tempo / HazardScale（有了新的实测局以后，先把 RunSim 里的 Measured* 换成新数据）
        [MenuItem("BlackTide/公式模拟/回测标定（拟合全局系数）")]
        public static void Calib() => Debug.Log("SIM_CALIB\n" + RunSim.Calibrate(60));

        public static string Go(string tag, System.Action<RunSim.Config> tweak)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string report = RunSim.Batch(Seeds, Minutes, out var all, tweak);
            string dir = Path.Combine(Application.dataPath, "..", "Captures", "sim");
            Directory.CreateDirectory(dir);
            string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string basePath = Path.Combine(dir, $"sim_{tag}_{stamp}");
            File.WriteAllText(basePath + ".txt", report, new UTF8Encoding(true));
            File.WriteAllText(basePath + "_curves.csv", Curves(all), new UTF8Encoding(true));
            Debug.Log($"SIM_REPORT {tag} ({sw.ElapsedMilliseconds} ms) → {basePath}.txt\n{report}");
            return report;
        }

        // 每 0.5 分钟：各路线的平均等级、侵蚀、净化数、团灭数（画曲线用）
        static string Curves(List<RunSim.Result> all)
        {
            var sb = new StringBuilder("strategy,minute,level_mean,level_p10,level_p90,erosion_mean,purified_mean,wipes_mean\n");
            var byStrat = new Dictionary<RunDirector.Strategy, List<RunSim.Result>>();
            foreach (var r in all) { if (!byStrat.TryGetValue(r.cfg.strategy, out var l)) byStrat[r.cfg.strategy] = l = new List<RunSim.Result>(); l.Add(r); }
            foreach (var kv in byStrat)
                for (float m = 0f; m <= Minutes + 0.01f; m += 0.5f)
                {
                    var lv = new List<float>(); float ero = 0f, pur = 0f, wp = 0f;
                    foreach (var r in kv.Value)
                    {
                        RunSim.Sample s = r.samples[0];
                        foreach (var x in r.samples) if (x.t <= m) s = x;
                        lv.Add(s.level); ero += s.erosion; pur += s.purified; wp += s.wipes;
                    }
                    lv.Sort();
                    float mean = 0f; foreach (var x in lv) mean += x; mean /= lv.Count;
                    int n = kv.Value.Count;
                    sb.AppendLine($"{kv.Key},{m:0.0},{mean:0.00},{lv[(int)(n * 0.1f)]:0.00},{lv[Mathf.Min(n - 1, (int)(n * 0.9f))]:0.00},{ero / n:0.0},{pur / n:0.00},{wp / n:0.00}");
                }
            return sb.ToString();
        }
    }
}

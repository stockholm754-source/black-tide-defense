# -*- coding: utf-8 -*-
"""
DS-003b · 多局聚合统计（KB-008 分位口径）
用法：
  python run_summary.py <Captures/runs 目录> --files run_a.csv run_b.csv ... [--label 标签]
输出：按路线聚合 —— 局数 / 胜率 / 决战时长中位[p10,p90] / 入口等级中位 / 团灭中位 / 检查点中位
判定（分位口径）：决战中位 ∈ [150,260]s、胜率 ≥ 2/3、团灭率 ≤ 1/3（来源 RUN-002_analysis.md）
"""
import sys, os, io, statistics
from run_report import analyze, TH

def main():
    args = sys.argv[1:]
    if "--files" not in args:
        print("用法: python run_summary.py <dir> --files run_*.csv ...")
        return
    i = args.index("--files")
    runs_dir = args[i - 1]
    end = args.index("--label") if "--label" in args else len(args)
    files = args[i + 1:end]
    label = ""
    if "--label" in args:
        label = args[args.index("--label") + 1]

    by = {}
    for fn in files:
        p = os.path.join(runs_dir, fn)
        ev = p.replace(".csv", "_events.csv")
        try:
            r = analyze(p, ev)
        except Exception as e:
            print("skip %s: %s" % (fn, e))
            continue
        by.setdefault(r["strategy"], []).append((fn, r))

    out = []
    out.append("# 多局聚合 %s（%d 局）" % (label or "", len(files)))
    out.append("")
    for strat in ("稳健发育", "激进净化", "贪信使"):
        if strat not in by:
            continue
        runs = by[strat]
        n = len(runs)
        wins = sum(1 for _, r in runs if r["win"] == "1")
        fin = sorted(r["finale"] for _, r in runs)
        med = lambda v: statistics.median(v)
        ent = sorted(r["entry_lv"] for _, r in runs)
        wp = sorted(r["wipes"] for _, r in runs)
        lv105 = sorted(r["lv105"] for _, r in runs)
        out.append("== %s  n=%d  胜 %d/%d（%.0f%%）" % (strat, n, wins, n, 100.0 * wins / n))
        out.append("   决战(秒) 中位 %.0f [p10 %.0f, p90 %.0f] | 入口等级中位 %.2f | 团灭中位 %.1f | 10.5min 等级中位 %.2f" %
                   (med(fin), fin[max(0, n // 10 - 1)], fin[min(n - 1, n * 9 // 10)], med(ent), med(wp), med(lv105)))
        for fn, r in runs:
            out.append("     · %s  win=%s finale=%ds entry=%.2f wipes=%d downs=%d" %
                       (fn, r["win"], r["finale"], r["entry_lv"], r["wipes"], r["downs"]))
        # 分位口径判定
        flags = []
        if not (150 <= med(fin) <= 260):
            flags.append("决战中位 %.0f 不在 150–260" % med(fin))
        if 100.0 * wins / n < 66.7:
            flags.append("胜率 %.0f%% < 2/3" % (100.0 * wins / n))
        if sum(wp) / n > 1.0 / 3.0:
            flags.append("团灭率 %.0f%% > 1/3" % (100.0 * sum(wp) / n))
        out.append("   >>> 分位判定：%s" % ("PASS" if not flags else "FAIL（" + "；".join(flags) + "）"))
        out.append("")
    text = "\n".join(out)
    print(text)

if __name__ == "__main__":
    main()

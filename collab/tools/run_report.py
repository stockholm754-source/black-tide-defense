# -*- coding: utf-8 -*-
"""
DS-003 · RUN-001 跑局数据管线
用法：
  python run_report.py <Captures/runs 目录> [--write <输出md>]
行为：
  1. 扫描 run_*.csv（跳过 _events/_summary），配对其 _events.csv；
  2. 计算 3/7/10.5/14 分钟检查点等级、终局入口时间与等级、决战时长、倒地/团灭、最低平均血量；
  3. 用 DS-002_report.md 的判定表出 PASS/FAIL；
  4. 检测该局的经验时代（ExpBase≈7.4 当前 / ≈8.8 旧窗口），旧窗口只出参考值不判定。
只读 CSV，不碰工程。
"""
import csv, io, os, re, sys, glob

# ---- DS-002 判定表阈值（来源：collab/reports/DS-002_report.md 第 3 节）----
TH = {
    "lv10.5": (5.5, 6.8, "10.5 分钟等级（贪信使允许偏快）"),
    "lv14":   (7.0, 8.6, "14 分钟等级"),
    "entry":  (8.2, 9.0, "终局入口等级（原稿 8.5±0.5）"),
    "finale": (150.0, 260.0, "决战时长 150–260s（原稿 240；<150s=终局压力不足，候选 SovereignHpMul ×3.5）"),
    "downs":  (1, 10**9, "倒地 ≥1 次（紧张但有希望）"),
    "wipes":  (0, 1, "团灭 0–1"),
}

def level_interp(rows, t):
    prev = None
    for r in rows:
        if r["t"] >= t:
            if prev is None:
                return r["lv"]
            w = (t - prev["t"]) / (r["t"] - prev["t"])
            return prev["lv"] + w * (r["lv"] - prev["lv"])
        prev = r
    return rows[-1]["lv"]

def analyze(csv_path, ev_path):
    rows = []
    with io.open(csv_path, "r", encoding="utf-8", errors="replace") as f:
        for r in csv.DictReader(f):
            rows.append(dict(
                t=float(r["t_run_min"]), lv=float(r["team_level"]),
                hp=float(r["team_hp"]), exp=float(r["exp_total"]),
                downs=int(r["downs"]), wipes=int(r["wipes"]),
                killsQ=int(r["killsQ"]), killsJ=int(r["killsJ"]), killsN=int(r["killsN"]),
                items=int(r["items_used"]) if "items_used" in r else 0,
            ))
    strategy = "?"
    finale = 0
    win = "?"
    with io.open(ev_path, "r", encoding="utf-8", errors="replace") as f:
        for line in f:
            m = re.search(r"strategy=(\S+)", line)
            if m:
                strategy = m.group(1)
            m = re.search(r"finale_s=(\d+)", line)
            if m:
                finale = int(m.group(1))
            m = re.search(r"win=(\d)", line)
            if m:
                win = m.group(1)
    last = rows[-1]
    entry_t = last["t"] - finale / 60.0
    res = dict(
        strategy=strategy, win=win, end_t=last["t"], finale=finale, entry_t=entry_t,
        lv3=level_interp(rows, 3.0), lv7=level_interp(rows, 7.0),
        lv105=level_interp(rows, 10.5), lv14=level_interp(rows, 14.0),
        entry_lv=level_interp(rows, entry_t),
        downs=last["downs"], wipes=last["wipes"], min_hp=min(r["hp"] for r in rows),
        exp_end=last["exp"], items=last["items"],
        kills=(last["killsQ"], last["killsJ"], last["killsN"]),
    )
    # 经验时代检测（辅助提示，不决定判定）：exp_total = 0.85*ExpBase*(q+5j+30n+海啸/信使…)
    # 分母只数了轻波/巨浪/怒涛，所以含 Boss(80×)/信使(70×) 的局 ratio 会偏高：
    #   7.4 无 Boss 局 ≈7.4，含 Boss ≈8.7；8.8 无 Boss ≈10.3，含 Boss 更高。
    q, j, n = res["kills"]
    denom = 0.85 * (q + 5 * j + 30 * n)
    ratio = res["exp_end"] / denom if denom > 0 else -1
    if ratio < 0:
        era = "无击杀数据"
    elif ratio <= 9.2:
        era = "7.4(当前)"
    elif ratio >= 9.9:
        era = "旧值(8.8/11)"
    else:
        era = "灰区(人工确认)"
    res["era"] = era
    res["ratio"] = ratio
    return res

def judge(res):
    checks = []
    def chk(name, val, lo, hi, label):
        ok = lo <= val <= hi
        checks.append((name, val, lo, hi, ok, label))
    chk("lv10.5", res["lv105"], *TH["lv10.5"])
    chk("lv14", res["lv14"], *TH["lv14"])
    chk("entry", res["entry_lv"], *TH["entry"])
    chk("finale", res["finale"], *TH["finale"])
    chk("downs", res["downs"], *TH["downs"])
    chk("wipes", res["wipes"], *TH["wipes"])
    return checks

def main():
    runs_dir = sys.argv[1] if len(sys.argv) > 1 else r"C:\Users\Administrator\Documents\gpt\outputs\black-tide-slice\Captures\runs"
    write_to = None
    if "--write" in sys.argv:
        write_to = sys.argv[sys.argv.index("--write") + 1]
    if "--files" in sys.argv:
        i = sys.argv.index("--files")
        mains = [os.path.join(runs_dir, f) for f in sys.argv[i + 1:] if not f.startswith("--")]
    else:
        mains = sorted(glob.glob(os.path.join(runs_dir, "run_*.csv")))
        mains = [p for p in mains if not p.endswith("_events.csv") and not p.endswith("_summary.csv")]
    lines = []
    def emit(s=""):
        lines.append(s)
    for csv_path in mains:
        ev = csv_path.replace(".csv", "_events.csv")
        if not os.path.exists(ev):
            continue
        try:
            res = analyze(csv_path, ev_path=ev)
        except Exception as e:
            emit("== %s 解析失败：%s" % (os.path.basename(csv_path), e))
            continue
        name = os.path.basename(csv_path)
        # 调试/编译短局：不刷判定（完整跑局 ≥12 分钟或 win=1）
        if res["end_t"] < 12 and res["win"] != "1":
            emit("== %s  %s  ratio=%.2f(%s)  [调试短局，跳过判定]" % (name, res["strategy"], res["ratio"], res["era"]))
            continue
        emit("== %s  %s  ratio=%.2f(%s)" % (name, res["strategy"], res["ratio"], res["era"]))
        emit("   时长 %.1f min | 决战 %ds | win=%s | 倒地 %d | 团灭 %d | 最低平均血量 %.0f%% | 道具 %d" %
             (res["end_t"], res["finale"], res["win"], res["downs"], res["wipes"], res["min_hp"] * 100, res["items"]))
        emit("   3/7/10.5/14min 等级：%.2f / %.2f / %.2f / %.2f | 入口 %.1fmin 等级 %.2f | exp_total %.0f" %
             (res["lv3"], res["lv7"], res["lv105"], res["lv14"], res["entry_t"], res["entry_lv"], res["exp_end"]))
        if res["era"].startswith("旧值") or res["era"].startswith("灰区"):
            emit("   [%s：以下判定可能不适用，仅供参考；跑局窗口请用 --files 指定新局文件]" % res["era"])
        fails = 0
        for name2, val, lo, hi, ok, label in judge(res):
            mark = "PASS" if ok else "FAIL"
            if not ok:
                fails += 1
            emit("   [%s] %-10s %.2f ∈ [%.1f, %.1f]  %s" % (mark, name2, val, lo, hi, label))
        if fails == 0:
            emit("   >>> 总体判定：PASS（可进入下一轮数值决策）")
        else:
            emit("   >>> 总体判定：FAIL（%d 项不达标；回调预案见 DS-002_report.md 第 3 节）" % fails)
        emit("")
    text = "\n".join(lines)
    if write_to:
        with io.open(write_to, "w", encoding="utf-8") as f:
            f.write("# RUN-001 跑局结果（自动生成）\n\n")
            f.write("生成：`collab/tools/run_report.py`。阈值来源 `collab/reports/DS-002_report.md`。\n\n")
            f.write(text)
        print("written: " + write_to)
    else:
        print(text)

if __name__ == "__main__":
    main()

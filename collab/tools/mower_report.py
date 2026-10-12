#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
割草方向判据报告（CL-001 仪表的读表端）。

用法：
    python mower_report.py <runs目录> [--files a.csv b.csv c.csv]

按 Design/割草方向重做.md 的 G1–G7 逐条给 PASS/FAIL，并把口径写清楚：
  - 只吃 csv 里的列名（DictReader），旧档缺新列时明确说"不适用"，不假装通过。
  - headless（-batchmode）跑局的 fps_* 不代表真实帧率（不渲染），脚本会标注来源。

判据：
  G1 场上敌人中位 >= 20，峰值 >= 45
  G2 空窗率（alive_now == 0 的采样占比） < 12%
  G3 一局总击杀 >= 900
  G4 单只轻波 1–2 刀         -> 引擎外算不了，看 kills_q / 交战时长旁证
  G5 决战 100–160 秒          -> 读 summary 的 finale_sec
  G6 最差帧率                 -> 见上面的 headless 警告
  G7 选择次数 >= 12 且割草卡占比 >= 50%
"""
import argparse
import csv
import io
import os
import statistics
import sys

MOWER_IDS = {"orbit", "homing", "nova", "net", "maelstrom", "cleave",
             "scythe", "bounce", "vents", "gale", "overload", "sigil"}


def read_csv(path):
    with io.open(path, encoding="utf-8-sig") as f:
        return list(csv.DictReader(f))


def read_summary(path):
    out = {}
    if not os.path.exists(path):
        return out
    with io.open(path, encoding="utf-8-sig") as f:
        for row in csv.reader(f):
            if len(row) >= 2:
                out[row[0]] = row[1]
    return out


def f(v, d=0.0):
    try:
        return float(v)
    except (TypeError, ValueError):
        return d


def analyze(run_csv, events_csv=None, summary_csv=None, headless=False):
    rows = read_csv(run_csv)
    if not rows:
        return ["空文件"], {}
    cols = rows[0].keys()
    res, note = [], {}
    name = os.path.basename(run_csv)

    # ---- G1 / G2：密度与空窗 ----
    # 口径：alive_now 含**待命的节点怪**（实测常态 38–100 只，它们不构成同屏压力）。
    # 有 alive_awake 列时一律以 awake 判定；只有旧档才退回 alive_now 并标注口径。
    col = "alive_awake" if "alive_awake" in cols else "alive_now"
    if col in cols:
        alive = [int(f(r[col])) for r in rows]
        med = statistics.median(alive)
        pk = max(alive)
        idle = sum(1 for a in alive if a == 0) / float(len(alive)) * 100.0
        tag = "" if col == "alive_awake" else "（旧口径：含待命怪，偏高）"
        res.append(("G1 同屏敌人", "PASS" if med >= 20 and pk >= 45 else "FAIL",
                    "中位 %.0f（目标 ≥20）· 峰值 %d（目标 ≥45）%s" % (med, pk, tag)))
        res.append(("G2 空窗率", "PASS" if idle < 12.0 else "FAIL",
                    "%.1f%% 的采样场上无敌（目标 <12%%）%s" % (idle, tag)))
        if col == "alive_awake" and "alive_now" in cols:
            tot = [int(f(r["alive_now"])) for r in rows]
            res.append(("待命怪占比", "INFO",
                        "中位总数 %.0f vs 中位交战 %.0f —— 差值即未接战的节点怪" %
                        (statistics.median(tot), med)))
        if "alive_tide" in cols:
            tide = [int(f(r.get("alive_tide", 0))) for r in rows]
            res.append(("涌浪在场", "INFO",
                        "中位 %.0f · 峰值 %d · 有 %d/%d 个采样为 0（>1/3 说明怪潮没起来）" %
                        (statistics.median(tide), max(tide), sum(1 for t in tide if t == 0), len(tide))))
    else:
        res.append(("G1/G2 密度", "N/A", "这份档没有任何 alive 列（改动前的构建），不参与判定"))

    # ---- G3：总击杀（同时给速率——局长会随构筑速度变，绝对值不可比）----
    k = (int(f(rows[-1].get("killsQ", 0))) + int(f(rows[-1].get("killsJ", 0))) +
         int(f(rows[-1].get("killsN", 0))))
    mins = max(0.1, f(rows[-1].get("t_run_min")))
    rate = k / mins
    # 2026-10-11 修订口径：原版 13:13 实测是 266 击杀 / 19.3 分钟 = 14/分钟。
    # 「一屏刷掉几百只」要的是**速率**上台阶，不是把局长拖回去。目标 ≥45/分钟（约 3 倍原版）。
    res.append(("G3 击杀", "PASS" if rate >= 45.0 else "FAIL",
                "共 %d 只 / %.1f 分钟 = **%.0f 只每分钟**（目标 ≥45；原版 14/分钟）" % (k, mins, rate)))

    # ---- G6：帧率（口径警告）----
    if "fps_min" in cols:
        lo = min(f(r["fps_min"]) for r in rows if f(r["fps_min"]) > 0)
        avg = statistics.mean([f(r["fps_avg"]) for r in rows if f(r["fps_avg"]) > 0]) if True else 0
        tag = "FAIL" if lo < 45 else "PASS"
        warn = "（headless batchmode 不渲染，只反映 CPU 模拟成本，不能当帧率验收）" if headless else ""
        res.append(("G6 最差帧率", tag if not headless else "看上下文",
                    "最差 %.0f fps · 均 %.0f fps%s" % (lo, avg, warn)))

    # ---- G5 / 等级 / 经济：优先 summary，退回 csv ----
    s = read_summary(summary_csv) if summary_csv else {}
    if s:
        fin = f(s.get("finale_sec"))
        res.append(("G5 决战时长", "PASS" if 100.0 <= fin <= 160.0 else "FAIL",
                    "%.0f 秒（目标 100–160；13:13 实测 69）" % fin))
        lv = f(s.get("team_level"))
        res.append(("满级时点", "INFO",
                    "结束 %.2f 级 · 跑 %s 分钟 · 侵蚀满 %s" % (lv, s.get("run_min"), s.get("forced"))))
        res.append(("净化 / 倒地 / 团灭", "INFO",
                    "净化 %s · 倒地 %s · 团灭 %s · 结果 %s" %
                    (s.get("purified"), s.get("wipes"), s.get("wipes"), s.get("result"))))
        if "buff_unlocks" in s:
            bu = int(f(s.get("buff_unlocks")))
            res.append(("G7 选择次数", "PASS" if bu >= 12 else "FAIL",
                        "%d 次（目标 ≥12）" % bu))
    elif "team_level" in cols:
        lv = [f(r["team_level"]) for r in rows]
        mins = [f(r["t_run_min"]) for r in rows]
        at9 = next((m for m, l in zip(mins, lv) if l >= 9.0), None)
        res.append(("满级时点", "INFO",
                    "9 级于 %s 分钟 · 结束 %.2f 级" % (("%.1f" % at9) if at9 else "未达", lv[-1])))

    # ---- 经验/经济斜率（从 csv 直接看趋势）----
    if "coins_avg" in cols:
        c0 = f(rows[len(rows) // 4]["coins_avg"])
        c1 = f(rows[-1]["coins_avg"])
        res.append(("经济斜率", "INFO",
                    "1/4 局时 %.0f 币 → 结束 %.0f 币（13:13 实测 799→1292 为溢出）" % (c0, c1)))

    # ---- 事件流：卡构成与异常 ----
    buffs = []
    if events_csv and os.path.exists(events_csv):
        for r in read_csv(events_csv):
            if r.get("event") == "buff":
                buffs.append(r.get("detail", ""))
        buys_empty = [r for r in read_csv(events_csv) if r.get("event") == "buy_skip"]
        if buys_empty:
            res.append(("买不动事件", "INFO", "%d 次 buy_skip（货架/限购信号）" % len(buys_empty)))
    if buffs:
        mower = sum(1 for b in buffs if any(i in b for i in []))  # 中文名匹配见下
        cn = {"环绕飞刃", "追击弹幕", "火焰新星", "雷霆之网", "漩涡", "连锁打击",
              "潮汐镰刀", "弹跳雷", "地火喷口", "风涡扫荡", "共鸣超载", "镜步残阵"}
        mower = sum(1 for b in buffs if any(c in b for c in cn))
        res.append(("G7 卡构成", "PASS" if mower / float(len(buffs)) >= 0.5 else "FAIL",
                    "选中 %d 张，其中割草卡 %d 张 = %.0f%%（目标 ≥50%%）" %
                    (len(buffs), mower, 100.0 * mower / len(buffs))))

    return res, note


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("runs_dir")
    ap.add_argument("--files", nargs="*", default=None)
    ap.add_argument("--headless", action="store_true", help="batchmode 跑局：帧率列不可当验收")
    a = ap.parse_args()

    files = a.files
    if not files:
        files = sorted(fn for fn in os.listdir(a.runs_dir)
                       if fn.startswith("run_") and fn.endswith(".csv") and not fn.endswith(("_events.csv", "_summary.csv")))
        files = files[-3:]

    for fn in files:
        base = fn[:-4]
        run = os.path.join(a.runs_dir, fn)
        ev = os.path.join(a.runs_dir, base + "_events.csv")
        sm = os.path.join(a.runs_dir, base + "_summary.csv")
        if not os.path.exists(run):
            print("缺文件：%s" % run)
            continue
        print("\n=== %s ===" % fn)
        res, note = analyze(run, ev if os.path.exists(ev) else None,
                            sm if os.path.exists(sm) else None, a.headless)
        for k, v, d in res:
            print("  [%-4s] %-12s %s" % (v, k, d))
        if note.get("tide_share") is not None:
            print("  [INFO] 涌浪占同屏敌人 %.0f%%" % note["tide_share"])


if __name__ == "__main__":
    main()

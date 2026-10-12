"""Summarize script-driven Unity motion evidence and make before/after proofs."""
import csv
import json
import math
from collections import defaultdict
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2] / "Captures" / "motion_20261011"
BASE = ROOT / "baseline"
AFTER = ROOT / "tuned2"

def load(folder):
    grouped = defaultdict(list)
    with (folder / "motion.csv").open(encoding="utf-8") as f:
        for row in csv.DictReader(f):
            grouped[row["case"]].append(row)
    return grouped

def summarize(grouped):
    result = {}
    for name, rows in grouped.items():
        result[name] = {
            "clips": list(dict.fromkeys(r["clip"] for r in rows)),
            "peak_speed": max(float(r["speed"]) for r in rows),
            "last_speed": float(rows[-1]["speed"]),
            "final_x": float(rows[-1]["x"]),
            "final_z": float(rows[-1]["z"]),
            "final_yaw": float(rows[-1]["yaw"]),
            "damage": float(rows[0]["targetHp"]) - float(rows[-1]["targetHp"]),
        }
        if "animationRate" in rows[-1]:
            result[name]["late_animation_rate"] = sum(float(r["animationRate"]) for r in rows[-30:]) / 30
    return result

before, after = load(BASE), load(AFTER)
if (ROOT / "locomotion-final" / "motion.csv").exists():
    after.update(load(ROOT / "locomotion-final"))
summary = {"baseline": summarize(before), "tuned": summarize(after), "checks": {}}
checks = summary["checks"]
for case, expected, xy in [
    ("dodge_forward", "Dodge_Forward", (0, 4.8)),
    ("dodge_back", "Dodge_Backward", (0, -4.8)),
    ("dodge_left", "Dodge_Left", (-4.8, 0)),
    ("dodge_right", "Dodge_Right", (4.8, 0)),
]:
    r = summary["tuned"][case]
    checks[case + "_clip"] = expected in r["clips"]
    checks[case + "_distance"] = math.hypot(r["final_x"] - xy[0], r["final_z"] - xy[1]) < 0.02
    checks[case + "_facing"] = min(r["final_yaw"], 360 - r["final_yaw"]) < 1
    dodge_rows = [r for r in after[case] if r["state"] == "Dodge"]
    checks[case + "_invulnerability"] = all((r["invulnerable"] == "True") == (float(r["time"]) < 0.3 - 0.0002) for r in dodge_rows if abs(float(r["time"]) - 0.3) > 0.002)
checks["slow_walk"] = "Walking_A" in summary["tuned"]["slow"]["clips"]
checks["ctrl_walk"] = "Walking_A" in summary["tuned"]["ctrl_walk"]["clips"]
checks["stop_idle"] = after["stop"][-1]["clip"] == "2H_Melee_Idle"
checks["wall_idle"] = after["wall"][-1]["clip"] == "2H_Melee_Idle" and float(after["wall"][-1]["speed"]) < 0.05
checks["combo_three_attacks"] = len([c for c in summary["tuned"]["combo"]["clips"] if "Attack" in c]) >= 3
checks["combo_hits"] = summary["tuned"]["combo"]["damage"] > 0
checks["chase_hits"] = summary["tuned"]["chase"]["damage"] > 0
checks["cancel_dodge_attack"] = "Dodge_Right" in summary["tuned"]["cancel"]["clips"] and any("Attack" in c for c in summary["tuned"]["cancel"]["clips"])
checks["skill_hits"] = summary["tuned"]["skill"]["damage"] > 0
checks["ult_hits"] = summary["tuned"]["ult"]["damage"] > 0
checks["weapon_skill_hits"] = summary["tuned"]["weapon_skill"]["damage"] > 0
checks["turn_attack_hits"] = summary["tuned"]["turn_attack"]["damage"] > 0
checks["generic_hits"] = summary["tuned"]["generic"]["damage"] > 0
checks["ai_clears_walk_input"] = len(after["ai_walk_reset"]) > 10 and all(r["walkHeld"] == "False" for r in after["ai_walk_reset"][2:])
first_running = next(r for r in after["run"] if r["clip"] == "Running_A")
checks["run_transition_no_rate_overshoot"] = float(first_running["speed"]) < 4.5 and float(first_running["animationRate"]) < 0.75
if (ROOT / "grip-final" / "motion.csv").exists():
    grip = load(ROOT / "grip-final")
    summary["grip_final"] = summarize(grip)
    checks["final_generic_hits"] = summary["grip_final"]["generic"]["damage"] > 0
    checks["final_ai_clears_walk_input"] = all(r["walkHeld"] == "False" for r in grip["ai_walk_reset"][2:])
    checks["final_runtime_errors_zero"] = "errors=0" in (ROOT / "grip-final" / "result.txt").read_text(encoding="utf-8")
summary["pass"] = all(checks.values())
(ROOT / "motion_report.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")

font_path = Path("C:/Windows/Fonts/msyh.ttc")
font = ImageFont.truetype(str(font_path), 18) if font_path.exists() else ImageFont.load_default()
small = ImageFont.truetype(str(font_path), 16) if font_path.exists() else font
names = {"slow": "减速 / 慢走", "reverse": "跑动反向", "dodge_back": "后撤闪避", "dodge_left": "侧向闪避", "combo": "三段挥砍", "chase": "攻击追击"}
frames = []
for name in ["slow", "reverse", "dodge_back", "dodge_left", "combo", "chase"]:
    a_files = sorted(BASE.glob(f"*_{name}_*.jpg"))
    final_folder = ROOT / "locomotion-final" if name in ("slow", "reverse") and (ROOT / "locomotion-final" / "motion.csv").exists() else AFTER
    b_files = sorted(final_folder.glob(f"*_{name}_*.jpg"))
    for i, (a, b) in enumerate(zip(a_files, b_files)):
        if i % 2: continue
        canvas = Image.new("RGB", (640, 278), "#111922")
        canvas.paste(Image.open(a).resize((320, 240)), (0, 38))
        canvas.paste(Image.open(b).resize((320, 240)), (320, 38))
        draw = ImageDraw.Draw(canvas)
        draw.text((8, 7), "修改前 · " + names[name], font=small, fill="white")
        draw.text((328, 7), "修改后 · 同一测试脚本", font=small, fill="#9fe1ca")
        draw.line((320, 0, 320, 278), fill="#536271", width=2)
        frames.append(canvas.quantize(colors=96, method=Image.Quantize.FASTOCTREE))
frames[0].save(ROOT / "motion_before_after.gif", save_all=True, append_images=frames[1:], duration=200, loop=0, optimize=True)

sheet = Image.new("RGB", (960, 398 * 3), "#111922")
for row, (name, sample) in enumerate([("slow", "060"), ("dodge_back", "030"), ("chase", "078")]):
    a = next(BASE.glob(f"*_{name}_{sample}.jpg"))
    final_folder = ROOT / "locomotion-final" if name == "slow" and (ROOT / "locomotion-final" / "motion.csv").exists() else AFTER
    b = next(final_folder.glob(f"*_{name}_{sample}.jpg"))
    panel = Image.new("RGB", (960, 398), "#111922")
    panel.paste(Image.open(a).resize((480, 360)), (0, 38))
    panel.paste(Image.open(b).resize((480, 360)), (480, 38))
    draw = ImageDraw.Draw(panel)
    draw.text((12, 7), "修改前 · " + names[name], font=font, fill="white")
    draw.text((492, 7), "修改后 · " + names[name], font=font, fill="#9fe1ca")
    sheet.paste(panel, (0, row * 398))
sheet.save(ROOT / "motion_comparison.jpg", quality=92)
print(json.dumps({"pass": summary["pass"], "checks": len(checks), "failed": [k for k,v in checks.items() if not v], "proof": str(ROOT / "motion_before_after.gif")}, ensure_ascii=False))
if not summary["pass"]:
    raise SystemExit(1)

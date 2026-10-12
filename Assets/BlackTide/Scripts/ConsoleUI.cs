using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlackTide
{
    // DS-015 局内控制台 / 修改器（用户要求）：`（反引号）或 F7 呼出。
    // 打开时玩家站立不动（与商店同规则），游戏继续运行。输入命令回车执行，help 查看全部。
    public class ConsoleUI : MonoBehaviour
    {
        public static ConsoleUI I;
        public static bool IsOpen => I && I.panel && I.panel.activeSelf;

        Font font;
        GameObject panel;
        InputField input;
        Text history;
        readonly List<string> lines = new List<string>();

        public void Build()
        {
            I = this;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Consolas", "Arial" }, 24);
            if (!FindObjectOfType<EventSystem>()) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var cgo = new GameObject("ConsoleCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var sc = cgo.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            var root = cgo.GetComponent<RectTransform>();

            panel = Img(root, "ConsolePanel", new Vector2(0.5f, 0f), new Vector2(0, 150), new Vector2(1680, 300), new Color(0.015f, 0.02f, 0.03f, 0.96f)).gameObject;
            var prt = panel.GetComponent<RectTransform>();
            history = Label(prt, "", 17, TextAnchor.LowerLeft, new Color(0.85f, 0.9f, 0.95f), new Vector2(0.5f, 1f), new Vector2(-820, -16), new Vector2(1640, 210));
            history.alignment = TextAnchor.LowerLeft;
            history.verticalOverflow = VerticalWrapMode.Overflow;
            Label(prt, "控制台 · 输入命令回车执行 · help 查看全部 · ` 或 F7 关闭", 15, TextAnchor.UpperRight, new Color(0.55f, 0.65f, 0.72f), new Vector2(0.5f, 1f), new Vector2(820, -10), new Vector2(700, 20));

            // 输入行
            var inGo = new GameObject("ConsoleInput", typeof(RectTransform), typeof(Image), typeof(InputField));
            inGo.transform.SetParent(prt, false);
            var irt = inGo.GetComponent<RectTransform>();
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0f);
            irt.anchoredPosition = new Vector2(0, 18); irt.sizeDelta = new Vector2(1640, 42);
            inGo.GetComponent<Image>().color = new Color(0.06f, 0.09f, 0.12f, 0.98f);
            input = inGo.GetComponent<InputField>();
            input.lineType = InputField.LineType.SingleLine;
            var tg = new GameObject("Text", typeof(RectTransform), typeof(Text));
            tg.transform.SetParent(inGo.transform, false);
            var t = tg.GetComponent<Text>();
            t.font = font; t.fontSize = 20; t.alignment = TextAnchor.MiddleLeft; t.color = Color.white; t.supportRichText = false;
            var trt = t.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(12, 4); trt.offsetMax = new Vector2(-12, -4);
            var phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            phGo.transform.SetParent(inGo.transform, false);
            var ph = phGo.GetComponent<Text>();
            ph.font = font; ph.fontSize = 20; ph.alignment = TextAnchor.MiddleLeft; ph.color = new Color(0.45f, 0.55f, 0.6f, 0.8f); ph.text = "输入命令（help 查看全部）";
            var prt2 = ph.rectTransform; prt2.anchorMin = Vector2.zero; prt2.anchorMax = Vector2.one; prt2.offsetMin = new Vector2(12, 4); prt2.offsetMax = new Vector2(-12, -4);
            input.textComponent = t;
            input.placeholder = ph;
            input.onEndEdit.AddListener(OnSubmit);

            panel.SetActive(false);
            Log("控制台就绪。输入 help 查看命令。");
        }

        void OnSubmit(string s)
        {
            if (string.IsNullOrEmpty(s) || input.wasCanceled) return;
            string result = Exec(s);
            Log("> " + s);
            Log("  " + result);
            input.text = "";
            input.ActivateInputField();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.BackQuote) || Input.GetKeyDown(KeyCode.F7))
            {
                bool open = !panel.activeSelf;
                panel.SetActive(open);
                if (open) { input.text = ""; input.ActivateInputField(); }
                else input.DeactivateInputField();
            }
            if (panel.activeSelf && Input.GetKeyDown(KeyCode.Escape)) { panel.SetActive(false); input.DeactivateInputField(); }
        }

        void Log(string s)
        {
            lines.Add(s);
            while (lines.Count > 14) lines.RemoveAt(0);
            history.text = string.Join("\n", lines.ToArray());
        }

        // ---------------- 命令实现 ----------------
        static string Exec(string raw)
        {
            var p = Combat.player;
            var parts = raw.Trim().Split(' ');
            string c = parts[0].ToLower();
            float arg = 0f;
            bool hasArg = parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out arg);
            string id = parts.Length > 1 ? parts[1] : "";
            if (c == "help")
                return "god 无敌开关 | hp [n] 设/满血 | energy 满能量 | concerto 满协奏\n" +
                       "coins n 加贝币 | exp n 加经验 | star n 设武器星级(2-5) | affix id 加词条\n" +
                       "echo id 装备声骸 | buff id 获得卡牌 | kills n 加击杀计数\n" +
                       "erosion n 设侵蚀 | overclock n 设超频 | speed x 倍速 | dmg x 伤害倍率\n" +
                       "style [0/1/2] 特效风格（0写实 1美漫 2霓虹故障） | power x 特效尺寸倍率 | density x 碎片密度 | autopilot 自动代打 | force 侵蚀拉满 | time min 设局内时间";
            if (p == null) return "角色还没生成";
            switch (c)
            {
                case "god": HeroBase.God = !HeroBase.God; return "无敌：" + (HeroBase.God ? "开" : "关");
                case "hp":
                    if (hasArg && arg > 0f) p.dmg.hp = Mathf.Min(p.dmg.maxHp, arg);
                    else p.dmg.hp = p.dmg.maxHp;
                    return $"生命 {p.dmg.hp:0}/{p.dmg.maxHp:0}";
                case "energy": p.Gain(100f, 0f); return "能量已满";
                case "concerto": p.Gain(0f, 100f); return "协奏已满";
                case "coins": p.kit.coins += arg; return $"贝币 +{arg:0} → {p.kit.coins:0}";
                case "exp": p.AddExp(arg); return $"经验 +{arg:0} → Lv.{p.level}（{p.exp:0}）";
                case "star": p.kit.weaponStar = (int)Mathf.Clamp(arg, 2, 5); return $"手持武器星级 → {p.kit.weaponStar}★";
                case "affix":
                    {
                        var w = p.kit.Active;
                        if (w == null) return "没装备武器";
                        var d = WeaponAffix.Def(id);
                        if (d == null) return "未知词条";
                        if (!w.affixes.Contains(id)) w.affixes.Add(id);
                        p.kit.Rebuild();
                        return "已加词条：" + d.name;
                    }
                case "echo":
                    {
                        var d = Echoes.Def(id);
                        if (d == null) return "未知声骸（claw/shield/bloat/chanter/ray/brute/duelist/javelin/priest/jelly/knight/frost/thunder/reaper/messenger）";
                        Echoes.Equip(p, d, true);
                        return "装备声骸：" + d.name;
                    }
                case "buff":
                    {
                        var d = Rogue.Def(id);
                        if (d == null) return "未知卡牌";
                        var rs = p.rogue;
                        if (!rs.levels.ContainsKey(d.id)) { rs.owned.Add(d); rs.levels[d.id] = 0; }
                        if (rs.levels[d.id] < d.MaxLv) rs.levels[d.id]++;
                        Combat.Toast($"控制台：获得 {d.name} Lv{rs.levels[d.id]}", new Color(0.85f, 0.9f, 1f));
                        return $"获得 {d.name} Lv{rs.levels[d.id]}（流派层数实时生效）";
                    }
                case "kills": Coop.killsQ += (int)arg; return $"击杀计数 +{(int)arg} → {Coop.killsQ}";
                case "erosion": Coop.erosion = Mathf.Clamp(arg, 0f, 100f); return "侵蚀 → " + Coop.erosion.ToString("0");
                case "overclock": p.overclock = Mathf.Clamp(arg, 0f, 100f); return "超频 → " + p.overclock.ToString("0");
                case "speed": Feel.SetBaseScale(Mathf.Clamp(arg > 0 ? arg : 1f, 1f, 16f)); return "倍速 → x" + Mathf.Clamp(arg > 0 ? arg : 1f, 1f, 16f).ToString("0");
                case "dmg": p.dmgScale = arg > 0.01f ? arg : 1f; return "伤害倍率 → " + p.dmgScale.ToString("0.0");
                case "style":
                    // CL-005：0 写实 / 1 美漫 / 2 霓虹故障；不带参数就循环
                    if (hasArg) FX.SetStyle((FX.VfxStyle)Mathf.Clamp(Mathf.RoundToInt(arg), 0, 2));
                    else FX.NextStyle();
                    return "特效风格 → " + FX.StyleName(FX.Style) + "（0 写实 / 1 美漫 / 2 霓虹故障）";
                case "autopilot": HeroBase.Autopilot = !HeroBase.Autopilot; return "自动代打：" + (HeroBase.Autopilot ? "开" : "关");
                case "power":
                    FX.Power = Mathf.Clamp(arg > 0.1f ? arg : 1.65f, 0.5f, 4f);
                    return "特效尺寸倍率 → x" + FX.Power.ToString("0.00") + "（大一点就往上调）";
                case "density":
                    FX.Density = Mathf.Clamp(arg > 0.1f ? arg : 1.8f, 0.5f, 5f);
                    return "特效密度倍率 → x" + FX.Density.ToString("0.00") + "（碎片多少）";
                case "force": Coop.erosion = 100f; return "侵蚀已拉满（60 秒后强制终局）";
                case "time":
                    if (RunDirector.I) { RunDirector.I.runSeconds = Mathf.Max(0f, arg) * 60f * RunDirector.LengthScale; return "局内时间 → " + arg.ToString("0.0") + " 分钟"; }
                    return "不在跑局模式";
                case "softlock": HeroBase.SoftLock = !HeroBase.SoftLock; return "自动追击：" + (HeroBase.SoftLock ? "开" : "关");
                default: return "未知命令：" + c + "（输入 help）";
            }
        }

        Image Img(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 sz, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = sz;
            var img = go.GetComponent<Image>();
            img.color = c; img.raycastTarget = true;
            return img;
        }
        Text Label(Transform parent, string s, int size, TextAnchor a, Color c, Vector2 anchor, Vector2 pos, Vector2 sz)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = sz;
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = a; t.color = c; t.text = s; t.supportRichText = true;
            t.raycastTarget = false;
            var sh = go.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.8f); sh.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }
    }
}

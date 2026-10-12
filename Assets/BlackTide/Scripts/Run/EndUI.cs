using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlackTide
{
    // 一局的结算页（M5）：胜负、决战用时和整局数据，数据和 run_*_summary.csv 是同一份
    public static class EndUI
    {
        static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "strategy", "路线" }, { "run_min", "总时长（分钟，原稿时间轴）" }, { "finale_sec", "决战用时（秒）" }, { "forced", "强制终局" },
            { "team_level", "队伍等级" }, { "purified", "净化裂隙" }, { "wipes", "团灭" }, { "kills_q", "击杀 轻波" }, { "kills_j", "击杀 巨浪" }, { "kills_n", "击杀 怒涛" },
            { "buff_unlocks", "增益次数" }, { "team_skills", "团队技能" }, { "messenger_echo", "信使声骸" },
        };

        public static void Show(bool win, float finaleSec, string csv)
        {
            var font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" }, 24);
            var cgo = new GameObject("EndCanvas", typeof(Canvas), typeof(CanvasScaler));
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            var sc = cgo.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            var root = cgo.GetComponent<RectTransform>();

            var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            var brt = bg.GetComponent<RectTransform>();
            brt.SetParent(root, false);
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(980f, 760f);
            bg.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.05f, 0.9f);

            Text T(string s, int size, Color c, float y, TextAnchor a, float h)
            {
                var go = new GameObject("T", typeof(RectTransform), typeof(Text));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(brt, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, y); rt.sizeDelta = new Vector2(900f, h);
                var t = go.GetComponent<Text>();
                t.font = font; t.fontSize = size; t.color = c; t.text = s; t.alignment = a; t.supportRichText = true;
                t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
                return t;
            }
            T(win ? "黑潮王已被击败" : "挑战失败", 46, win ? new Color(0.45f, 1f, 0.9f) : new Color(1f, 0.5f, 0.45f), -30f, TextAnchor.UpperCenter, 60f);
            T(win ? $"决战用时 {finaleSec:0} 秒" : "全军覆没于黑潮之心", 22, new Color(0.9f, 0.92f, 0.95f), -96f, TextAnchor.UpperCenter, 30f);

            var kv = new Dictionary<string, string>();
            foreach (var line in csv.Split('\n')) { int i = line.IndexOf(','); if (i > 0) kv[line.Substring(0, i)] = line.Substring(i + 1).Trim(); }
            var left = new System.Text.StringBuilder();
            foreach (var p in Names) if (kv.TryGetValue(p.Key, out var v)) left.AppendLine($"<color=#a9b2bf>{p.Value}</color>  {(p.Key == "forced" || p.Key == "messenger_echo" ? (v == "1" ? "是" : "否") : v)}");
            var l = T(left.ToString(), 20, Color.white, -150f, TextAnchor.UpperLeft, 420f);
            l.rectTransform.anchoredPosition = new Vector2(-240f, -150f); l.rectTransform.sizeDelta = new Vector2(420f, 520f);
            var right = new System.Text.StringBuilder();
            foreach (var h in Combat.heroes)
            {
                if (!h) continue;
                kv.TryGetValue($"{h.Id}_buffs", out var bf);
                right.AppendLine($"<color=#ffd27a>{h.HeroName}</color>  {h.level} 级 · {h.kit.weaponStar}★ · 贝币 收 {h.kit.earned:0} / 花 {h.kit.spent:0}\n<size=16><color=#c8d0da>{(string.IsNullOrEmpty(bf) ? "没有增益" : bf)}</color></size>");
            }
            var r = T(right.ToString(), 19, Color.white, -150f, TextAnchor.UpperLeft, 420f);
            r.rectTransform.anchoredPosition = new Vector2(220f, -150f); r.rectTransform.sizeDelta = new Vector2(480f, 520f);
            T($"按 {GameInput.Hint("F2","LT+Menu")} 重新挑战", 17, new Color(0.7f, 0.75f, 0.82f), -700f, TextAnchor.UpperCenter, 30f);
        }
    }
}

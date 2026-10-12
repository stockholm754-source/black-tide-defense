using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlackTide
{
    // DS-020 手柄按键图标提示（Codex CX-005 收尾）：
    // 在保留既有文字提示的前提下，额外增加一行「按键图标 + 说明」芯片；仅手柄模式显示。
    // 用法：PadHints.Create(父节点, "X:普攻|B:闪避|RB:技能", 锚点, 轴心, 位置)。
    public class PadHints : MonoBehaviour
    {
        static Font font;
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        RectTransform rt;
        string spec;
        Vector2 anchor, pivot, pos;
        int fontSize;
        float iconSize;
        bool? lastPad;

        public static PadHints Create(Transform parent, string spec, Vector2 anchor, Vector2 pivot, Vector2 pos, int fontSize = 15, float iconSize = 24f, bool showWithKeyboard = false)
        {
            var go = new GameObject("PadHints", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            var ph = go.AddComponent<PadHints>();
            ph.rt = rt; ph.spec = spec; ph.anchor = anchor; ph.pivot = pivot; ph.pos = pos;
            ph.fontSize = fontSize; ph.iconSize = iconSize; ph.showWithKeyboard = showWithKeyboard;
            ph.Build();
            return ph;
        }
        bool showWithKeyboard;

        /// <summary>设备切换时切换显示（键盘模式隐藏）。</summary>
        void Update()
        {
            bool pad = GameInput.UsingPad || showWithKeyboard;
            if (lastPad != pad)
            {
                lastPad = pad;
                gameObject.SetActive(pad);
            }
        }

        public static Sprite Icon(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            string key = token.Trim().ToLower();
            if (cache.TryGetValue(key, out var s)) return s;
            var tex = Resources.Load<Texture2D>("UI/Pad/" + key);
            Sprite sp = null;
            if (tex) sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            cache[key] = sp;
            return sp;
        }

        void Build()
        {
            if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" }, 20);
            var chips = new List<RectTransform>();
            float x = 0f;
            foreach (var part in spec.Split('|'))
            {
                var bits = part.Split(':');
                string keys = bits[0];
                string label = bits.Length > 1 ? bits[1] : "";
                // 组合键 LT+Y：两个图标中间加号
                float groupStart = x;
                foreach (var k in keys.Split('+'))
                {
                    var img = ChipImage(rt, k.Trim());
                    if (img) { img.rectTransform.anchoredPosition = new Vector2(x + iconSize * 0.5f, 0f); x += iconSize + 3f; }
                    if (k != keys.Split('+')[keys.Split('+').Length - 1])
                    {
                        var plus = ChipLabel(rt, "+", fontSize);
                        plus.rectTransform.anchoredPosition = new Vector2(x + 5f, 0f);
                        x += 12f;
                    }
                }
                if (label.Length > 0)
                {
                    var t = ChipLabel(rt, label, fontSize);
                    Canvas.ForceUpdateCanvases();
                    float w = Mathf.Max(t.preferredWidth, 10f) + 6f;
                    t.rectTransform.anchoredPosition = new Vector2(x + w * 0.5f, 0f);
                    x += w;
                }
                x += 16f;
                chips.Add(rt);   // 占位，实际按 x 排布
            }
            float total = Mathf.Max(1f, x - 16f);
            rt.sizeDelta = new Vector2(total, Mathf.Max(iconSize, fontSize + 6f));
            // 以 rt 中心为基准左对齐排布
            foreach (Transform c in rt) c.GetComponent<RectTransform>().anchoredPosition += new Vector2(-total * 0.5f, 0f);
        }

        Image ChipImage(Transform parent, string token)
        {
            var sp = Icon(token);
            if (!sp) return null;
            var go = new GameObject("Ico_" + token, typeof(RectTransform), typeof(Image));
            var irt = go.GetComponent<RectTransform>();
            irt.SetParent(parent, false);
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(iconSize, iconSize);
            var img = go.GetComponent<Image>();
            img.sprite = sp; img.preserveAspect = true; img.raycastTarget = false;
            return img;
        }

        Text ChipLabel(Transform parent, string text, int size)
        {
            var go = new GameObject("Txt", typeof(RectTransform), typeof(Text));
            var trt = go.GetComponent<RectTransform>();
            trt.SetParent(parent, false);
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(size * text.Length * 1.15f + 8f, size + 8f);
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = TextAnchor.MiddleCenter;
            t.color = new Color(0.82f, 0.88f, 0.94f); t.text = text; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var sh = go.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.8f); sh.effectDistance = new Vector2(1, -1);
            return t;
        }
    }
}

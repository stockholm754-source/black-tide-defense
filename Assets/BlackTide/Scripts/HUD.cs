using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlackTide
{
    // 界面：Boss 血条/架势/咏唱、玩家血量/能量/灼痕/技能、伤害数字、中央提示、结算
    public class HUD : MonoBehaviour
    {
        Font font;
        RectTransform canvasRt;
        Canvas canvas;
        RectTransform pHp, pHpGhost, pEn, bHp, bHpGhost, bStg, chantFill;
        GameObject bossPanel, chantPanel;
        Text pHpText, bossTag, toast, endText, skillEText, skillQText, skillRText, timerText, pExpText;
        RectTransform skillECd, skillQCd, skillRCd, pExp, skillVCd;
        Text skillVText, weaponLabel;
        Text inputHint;
        readonly List<Text> skillKeys = new List<Text>();
        Image skillRFrame;
        static readonly Color ExpCol = new Color(0.55f, 0.85f, 1f);
        Image skillQFrame;
        readonly Image[] skillIcons = new Image[4], skillArt = new Image[4];
        Image toastBanner;
        Image[] pips;
        Text pName, pipLabel, coopHint, bossNameText;
        float toastUntil;
        Image eroIcon, ocIcon;                  // DS-010（KB-005）：侵蚀 / 超频压力图标
        Text eroText, ocText, etaText;          // DS-017：压力数值与预估
        Image eroBarBg, forceBarBg;             // DS-017：侵蚀进度条 / 强制决战倒计时条
        Image eroFillImg, forceFillImg;
        RectTransform eroFillRt, forceFillRt;
        bool warned60, warned85;                // DS-017：60% / 85% 各警示一次

        // 队伍栏
        class Frame { public HeroBase h; public RectTransform hp, co, shield, xp; public Text name, status; public Image bg; }
        readonly List<Frame> frames = new List<Frame>();
        readonly List<Text> downLabels = new List<Text>();
        float pGhost = 1f, bGhost = 1f;

        class Num { public Text t; public Vector3 w; public float t0; public int kind; public Vector2 jitter; }
        readonly List<Num> nums = new List<Num>();
        readonly Stack<Text> pool = new Stack<Text>();
        Camera cam;

        static readonly Color Panel = new Color(0.03f, 0.04f, 0.06f, 0.72f);
        static readonly Color HpCol = new Color(0.86f, 0.26f, 0.2f);
        static readonly Color Gold = new Color(1f, 0.78f, 0.32f);
        static readonly Color BossCol = new Color(0.25f, 0.82f, 0.78f);
        static readonly Color StgCol = new Color(1f, 0.66f, 0.25f);

        // DS-017：压力条底框+填充（右锚）
        Image Bar2(RectTransform parent, Vector2 pos, Vector2 sz, Color bg, out RectTransform fillRt)
        {
            var go = new GameObject("BarBg", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.anchoredPosition = pos; rt.sizeDelta = sz;
            var img = go.GetComponent<Image>();
            img.color = bg; img.raycastTarget = false;
            var fgo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            var frt = fgo.GetComponent<RectTransform>();
            frt.SetParent(rt, false);
            frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(1f, 1f);
            frt.offsetMin = new Vector2(1f, 1f); frt.offsetMax = new Vector2(-1f, -1f);
            fgo.GetComponent<Image>().color = Color.white;
            fgo.GetComponent<Image>().raycastTarget = false;
            fillRt = frt;
            return img;
        }

        public void Build()
        {
            cam = Camera.main;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "PingFang SC", "Arial" }, 32);
            var cgo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            cgo.transform.SetParent(transform, false);
            canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var sc = cgo.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            canvasRt = cgo.GetComponent<RectTransform>();

            // ---- Boss ----
            bossPanel = new GameObject("Boss", typeof(RectTransform));
            var bp = bossPanel.GetComponent<RectTransform>();
            bp.SetParent(canvasRt, false);
            Anchor(bp, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -48), new Vector2(960, 90));
            bossNameText = Label(bp, Combat.boss ? Combat.boss.bossName : "Boss", 26, TextAnchor.LowerLeft, Color.white, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -2), new Vector2(600, 34), true);
            bossTag = Label(bp, "", 20, TextAnchor.LowerRight, StgCol, new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, -2), new Vector2(420, 34), true);
            Bar(bp, new Vector2(0, -40), new Vector2(960, 20), BossCol, out bHp, out bHpGhost);
            UISkin.Border((RectTransform)bHp.parent, 4, new Vector2(12f, 10f));
            Bar(bp, new Vector2(0, -64), new Vector2(960, 7), StgCol, out bStg, out _);
            chantPanel = new GameObject("Chant", typeof(RectTransform));
            var cp = chantPanel.GetComponent<RectTransform>();
            cp.SetParent(bp, false);
            Anchor(cp, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -80), new Vector2(560, 34));
            Bar(cp, new Vector2(0, -20), new Vector2(560, 12), new Color(0.75f, 0.5f, 1f), out chantFill, out _);
            Label(cp, "黑潮咏唱", 18, TextAnchor.UpperCenter, new Color(0.85f, 0.7f, 1f), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, 4), new Vector2(560, 24), true);
            chantPanel.SetActive(false);

            // ---- 玩家 ----
            var pp = new GameObject("Player", typeof(RectTransform)).GetComponent<RectTransform>();
            pp.SetParent(canvasRt, false);
            Anchor(pp, Vector2.zero, Vector2.zero, new Vector2(56, 120), new Vector2(560, 120));
            pp.pivot = Vector2.zero; pp.anchoredPosition = new Vector2(56, 52);
            pName = Label(pp, "", 28, TextAnchor.LowerLeft, Color.white, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 6), new Vector2(520, 36), true);
            Bar(pp, new Vector2(0, -16), new Vector2(440, 18), HpCol, out pHp, out pHpGhost);
            UISkin.Border((RectTransform)pHp.parent, 3, new Vector2(8f, 7f));
            pHpText = Label(pp, "", 16, TextAnchor.MiddleRight, new Color(1, 1, 1, 0.85f), new Vector2(0, 1), new Vector2(0, 1), new Vector2(-124, -16), new Vector2(200, 20), true);
            Bar(pp, new Vector2(0, -42), new Vector2(440, 8), Gold, out pEn, out _);
            Bar(pp, new Vector2(0, -78), new Vector2(440, 4), ExpCol, out pExp, out _);
            pExpText = Label(pp, "", 14, TextAnchor.MiddleRight, new Color(0.7f, 0.88f, 1f, 0.85f), new Vector2(0, 1), new Vector2(0, 1), new Vector2(240, -62), new Vector2(200, 18), true);
            pips = new Image[10];
            for (int i = 0; i < 10; i++)
            {
                var img = Box(pp, new Vector2(0, 1), new Vector2(i * 15, -62), new Vector2(11, 10), new Color(0.25f, 0.12f, 0.08f, 0.9f));
                pips[i] = img;
            }
            pipLabel = Label(pp, "", 15, TextAnchor.MiddleLeft, new Color(1f, 0.6f, 0.35f), new Vector2(0, 1), new Vector2(0, 1), new Vector2(160, -58), new Vector2(120, 20), true);
            coopHint = Label(pp, "", 18, TextAnchor.LowerLeft, new Color(1f, 0.9f, 0.6f), new Vector2(0, 1), new Vector2(0, 1), new Vector2(200, 44), new Vector2(700, 26), true);
            SkillBox(pp, new Vector2(460, -14), "E", "", out skillECd, out skillEText, out _);
            SkillBox(pp, new Vector2(540, -14), "Q", "", out skillQCd, out skillQText, out skillQFrame);
            SkillBox(pp, new Vector2(620, -14), "R", "", out skillRCd, out skillRText, out skillRFrame);
            SkillBox(pp, new Vector2(700, -14), "V", "", out skillVCd, out skillVText, out _);   // 武器技能
            weaponLabel = Label(pp, "", 15, TextAnchor.LowerLeft, new Color(0.9f, 0.9f, 0.95f), new Vector2(0, 1), new Vector2(0, 1), new Vector2(460, 74), new Vector2(620, 40), true);

            // ---- 队伍栏（左侧） ----
            for (int i = 0; i < Combat.heroes.Count; i++)
            {
                var h = Combat.heroes[i];
                var fr = new Frame { h = h };
                var root = new GameObject("Party" + i, typeof(RectTransform)).GetComponent<RectTransform>();
                root.SetParent(canvasRt, false);
                Anchor(root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40, 170 - i * 92), new Vector2(330, 80));
                fr.bg = Box(root, new Vector2(0, 1), new Vector2(-10, 6), new Vector2(330, 80), new Color(0.03f, 0.04f, 0.06f, 0.45f));
                fr.bg.rectTransform.pivot = new Vector2(0, 1);
                Box(root, new Vector2(0, 1), new Vector2(-10, 6), new Vector2(5, 80), h.Color).rectTransform.pivot = new Vector2(0, 1);
                fr.name = Label(root, "", 22, TextAnchor.LowerLeft, Color.white, new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, 4), new Vector2(320, 28), true);
                fr.name.rectTransform.pivot = new Vector2(0, 1);
                Bar(root, new Vector2(8, -29), new Vector2(270, 3), ExpCol, out fr.xp, out _);
                Bar(root, new Vector2(8, -36), new Vector2(270, 12), HpCol, out fr.hp, out fr.shield);
                Bar(root, new Vector2(8, -53), new Vector2(270, 6), h.Color, out fr.co, out _);
                fr.status = Label(root, "", 16, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.8f), new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -62), new Vector2(320, 20), true);
                frames.Add(fr);
                var dl = Label(canvasRt, "", 22, TextAnchor.MiddleCenter, new Color(1f, 0.6f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400, 60), true);
                dl.gameObject.SetActive(false);
                downLabels.Add(dl);
            }

            // ---- 操作提示 ----
            inputHint = Label(canvasRt, "", 17, TextAnchor.LowerRight, new Color(0.85f, 0.88f, 0.95f, 0.65f), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-28, 28), new Vector2(690, 78), true);
            inputHint.lineSpacing = 1.2f;
            // DS-020（CX-005 收尾）：手柄按键图标行——仅手柄模式显示；上面三行文字提示保持不变，不打断 121 项回归断言
            PadHints.Create(canvasRt, "X:普攻|B:闪避|RB:技能|RT:大招|LB:声骸|Y:武器技|A:拾取|LS:选卡", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-28, 114), 15, 24);
            timerText = Label(canvasRt, "", 18, TextAnchor.UpperLeft, new Color(0.85f, 0.88f, 0.95f, 0.7f), new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -30), new Vector2(400, 30), true);

            // DS-017 局势仪表盘：右上压力账本——侵蚀值/进度条/到满预估 + 满后常驻红色强制决战倒计时 + 超频
            eroIcon = UISkin.Image(canvasRt, 62, new Vector2(1, 1), new Vector2(-150f, -60f), new Vector2(40f, 40f));
            eroText = Label(canvasRt, "", 20, TextAnchor.MiddleLeft, new Color(0.85f, 0.9f, 0.95f), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-128f, -58f), new Vector2(140f, 26f), true);
            eroBarBg = Bar2(canvasRt, new Vector2(-28f, -72f), new Vector2(180f, 8f), new Color(0f, 0f, 0f, 0.45f), out eroFillRt);
            eroFillImg = eroFillRt.GetComponent<Image>();
            eroFillImg.color = new Color(0.3f, 0.95f, 0.9f);
            forceBarBg = Bar2(canvasRt, new Vector2(-28f, -72f), new Vector2(180f, 8f), new Color(0f, 0f, 0f, 0.45f), out forceFillRt);
            forceFillImg = forceFillRt.GetComponent<Image>();
            forceFillImg.color = new Color(1f, 0.3f, 0.25f);
            forceBarBg.gameObject.SetActive(false);
            etaText = Label(canvasRt, "", 14, TextAnchor.MiddleLeft, new Color(0.62f, 0.72f, 0.78f), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-128f, -90f), new Vector2(280f, 20f), true);
            ocIcon = UISkin.Image(canvasRt, 63, new Vector2(1, 1), new Vector2(-150f, -122f), new Vector2(32f, 32f));
            ocIcon.gameObject.SetActive(false);
            ocText = Label(canvasRt, "", 16, TextAnchor.MiddleLeft, new Color(0.85f, 0.9f, 0.95f), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-128f, -120f), new Vector2(140f, 24f), true);

            // ---- 中央提示 / 结算 ----
            toast = Label(canvasRt, "", 34, TextAnchor.MiddleCenter, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 350), new Vector2(1400, 60), true);
            toastBanner = UISkin.Image(canvasRt, 9, new Vector2(0.5f, 0.5f), new Vector2(0, 350), new Vector2(1120, 82));
            toastBanner.preserveAspect = false;
            toast.transform.SetAsLastSibling();
            endText = Label(canvasRt, "", 52, TextAnchor.MiddleCenter, Gold, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(1400, 220), true);
            endText.lineSpacing = 1.1f;

            // 伤害跳字交给 DamageText（分层、合并、跟随目标；2026-10-09）。旧接口转发过去，兼容别处的调用
            gameObject.AddComponent<DamageText>().Init(canvasRt, canvas, font);
            Combat.onNumber = (w, v, k) => DamageText.Hit(null, w, v, k == 1 ? DT.Crit : k == 2 ? DT.Broken : k == 3 ? DT.Taken : k == 4 ? DT.Ult : DT.Normal);
            Combat.onTinted = (w, s, c, big) => DamageText.Status(w, s, c, big ? 30 : 22);
            Combat.onToast = ShowToast;
        }

        // ------------ 构件 ------------
        static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = min; rt.anchorMax = max; rt.pivot = new Vector2(0.5f, 0.5f);
            if (min == max) rt.pivot = min;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
        }

        Image Box(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color c)
        {
            var go = new GameObject("Box", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Anchor(rt, anchor, anchor, pos, size);
            var img = go.GetComponent<Image>(); img.color = c; img.raycastTarget = false;
            return img;
        }

        void Bar(Transform parent, Vector2 pos, Vector2 size, Color c, out RectTransform fill, out RectTransform ghost)
        {
            var bg = Box(parent, new Vector2(0, 1), pos, size, Panel);
            var frame = bg.gameObject.AddComponent<Outline>(); frame.effectColor = new Color(0, 0, 0, 0.8f); frame.effectDistance = new Vector2(1.5f, -1.5f);
            var g = Box(bg.transform, Vector2.zero, Vector2.zero, Vector2.zero, new Color(1f, 0.95f, 0.85f, 0.7f));
            ghost = g.rectTransform; ghost.anchorMin = Vector2.zero; ghost.anchorMax = Vector2.one; ghost.sizeDelta = Vector2.zero; ghost.pivot = Vector2.zero;
            var f = Box(bg.transform, Vector2.zero, Vector2.zero, Vector2.zero, c);
            fill = f.rectTransform; fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one; fill.sizeDelta = Vector2.zero; fill.pivot = Vector2.zero;
            var hl = Box(f.transform, new Vector2(0, 1), Vector2.zero, Vector2.zero, new Color(1, 1, 1, 0.18f));
            hl.rectTransform.anchorMin = new Vector2(0, 0.55f); hl.rectTransform.anchorMax = Vector2.one; hl.rectTransform.sizeDelta = Vector2.zero;
        }

        static void SetFill(RectTransform rt, float f) { if (rt) rt.anchorMax = new Vector2(Mathf.Clamp01(f), 1f); }

        Text Label(Transform parent, string s, int size, TextAnchor a, Color c, Vector2 amin, Vector2 amax, Vector2 pos, Vector2 sz, bool shadow)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Anchor(rt, amin, amax, pos, sz);
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = a; t.color = c; t.text = s; t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
            if (shadow) { var sh = go.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.85f); sh.effectDistance = new Vector2(2, -2); }
            return t;
        }

        void SkillBox(Transform parent, Vector2 pos, string key, string name, out RectTransform cd, out Text sub, out Image frame)
        {
            frame = Box(parent, new Vector2(0, 1), pos + new Vector2(0, 4), new Vector2(68, 68), new Color(0.08f, 0.09f, 0.12f, 0.9f));
            int index = "EQRV".IndexOf(key);
            skillIcons[index] = UISkin.Image(frame.transform, 40, new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(46, 46));
            var c = Box(frame.transform, Vector2.zero, Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0.65f));
            cd = c.rectTransform; cd.anchorMin = Vector2.zero; cd.anchorMax = new Vector2(1, 0); cd.sizeDelta = Vector2.zero; cd.pivot = Vector2.zero;
            skillArt[index] = UISkin.Image(frame.transform, 1, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(80, 80));
            skillKeys.Add(Label(frame.transform, key, 19, TextAnchor.MiddleCenter, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 22), new Vector2(48, 24), true));
            sub = Label(frame.transform, name, 15, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.7f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -49), new Vector2(92, 22), true);
        }

        void SetSkillArt(int index, int icon, bool ready, bool unlocked)
        {
            skillIcons[index].sprite = UISkin.Get(icon);
            skillIcons[index].enabled = skillIcons[index].sprite != null;
            skillIcons[index].color = unlocked ? Color.white : new Color(0.45f, 0.45f, 0.45f, 0.7f);
            skillArt[index].sprite = UISkin.Get(ready ? 2 : 1);
            skillArt[index].color = new Color(1f, 1f, 1f, unlocked ? 1f : 0.5f);
        }

        // ------------ 事件 ------------
        public void ShowToast(string s, Color c)
        {
            toast.text = s; toast.color = c; toastUntil = Feel.Clock + 1.6f;
            toast.transform.localScale = Vector3.one * 1.25f;
        }

        public void ShowEnd(string s) { endText.text = s; }

        // AI 队友打出的数字、治疗：小一号、带角色颜色，不抢玩家自己的反馈
        void SpawnTinted(Vector3 w, string text, Color c, bool big)
        {
            var t = pool.Count > 0 ? pool.Pop() : Label(canvasRt, "", 30, TextAnchor.MiddleCenter, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 60), true);
            t.gameObject.SetActive(true);
            t.text = text;
            t.color = c;
            t.fontSize = big ? 30 : 22;
            t.transform.SetAsLastSibling();
            nums.Add(new Num { t = t, w = w, t0 = Feel.Clock, kind = 5, jitter = new Vector2(Random.Range(-30f, 30f), Random.Range(-10f, 15f)) });
        }

        void SpawnNumber(Vector3 w, float v, int kind)
        {
            var t = pool.Count > 0 ? pool.Pop() : Label(canvasRt, "", 30, TextAnchor.MiddleCenter, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 60), true);
            t.gameObject.SetActive(true);
            t.text = Mathf.RoundToInt(v).ToString();
            switch (kind)
            {
                case 1: t.color = new Color(1f, 0.85f, 0.3f); t.fontSize = 50; t.text += "!"; break;
                case 2: t.color = new Color(1f, 0.62f, 0.28f); t.fontSize = 46; break;
                case 3: t.color = new Color(1f, 0.32f, 0.3f); t.fontSize = 32; t.text = "-" + t.text; break;
                case 4: t.color = new Color(1f, 0.72f, 0.3f); t.fontSize = 76; break;
                default: t.color = new Color(1f, 0.97f, 0.92f); t.fontSize = 38; break;
            }
            t.transform.SetAsLastSibling();
            nums.Add(new Num { t = t, w = w, t0 = Feel.Clock, kind = kind, jitter = new Vector2(Random.Range(-40f, 40f), Random.Range(-10f, 20f)) });
        }

        static string ShortEcho(string n) { int i = n.IndexOf('·'); return i >= 0 ? n.Substring(i + 1) : n; }   // 「巨浪·潮刃剑士」→「潮刃剑士」

        void LateUpdate()
        {
            if (!cam) cam = Camera.main;
            var p = Combat.player;
            if (p)
            {
                string[] keys = GameInput.UsingPad ? new[] { "RB", "RT", "LB", "Y" } : new[] { "E", "Q", "R", "V" };
                for (int i=0;i<skillKeys.Count && i<keys.Length;i++) skillKeys[i].text=keys[i];
                const string target = "自动追击 · 近处残血优先";
                inputHint.text = GameInput.UsingPad ? $"左摇杆 移动 · X 普攻 · B 闪避 · A 交互\nLT+X 换人 · LT+Y 换武器 · LS 选卡 · Menu 教学\n{target}" : $"WASD 移动 · 左键 普攻 · 空格 / 右键 闪避\n1 / 2 接力 · F 拾取 / 救援 · Tab 换人\n{target}";
                pName.text = $"{p.HeroName}  <size=18><color=#c9b8a8>{p.Role}</color></size>  <size=20><color=#ffe08a>Lv.{p.EffLevel}</color></size>{(p.penalized ? "<size=16><color=#ff9a7a> 掉级</color></size>" : "")}";
                bool maxed = p.level >= Growth.MaxLevel;
                SetFill(pExp, maxed ? 1f : p.exp / Growth.ExpNeed(p.level));
                pExpText.text = !Growth.Enabled ? "" : maxed ? "满级" : p.level >= Growth.LevelCap && p.exp >= Growth.ExpNeed(p.level) ? $"净化 {Growth.PurifyForMax} 个裂隙解锁 10 级" : $"经验 {p.exp:0} / {Growth.ExpNeed(p.level):0}";
                pName.color = Color.Lerp(p.Color, Color.white, 0.55f);
                float hp = p.dmg.hp / p.dmg.maxHp;
                SetFill(pHp, hp);
                pGhost = Mathf.Max(hp, Mathf.MoveTowards(pGhost, hp, Feel.DT * 0.6f));
                SetFill(pHpGhost, pGhost);
                pHpText.text = $"{Mathf.CeilToInt(p.dmg.hp)} / {Mathf.RoundToInt(p.dmg.maxHp)}";
                SetFill(pEn, p.energy / p.energyMax);
                for (int i = 0; i < pips.Length; i++)
                {
                    pips[i].gameObject.SetActive(i < p.PipMax);
                    pips[i].color = i < p.Pips ? p.Color : new Color(0.2f, 0.14f, 0.1f, 0.9f);
                }
                pipLabel.text = p.PipName;
                pipLabel.color = p.Color;
                pipLabel.rectTransform.anchoredPosition = new Vector2(p.PipMax * 15 + 10, -58);
                float cdLeft = Mathf.Max(0f, p.skillReadyAt - Time.time);
                skillECd.anchorMax = new Vector2(1f, Mathf.Clamp01(cdLeft / Mathf.Max(0.01f, p.SkillCd * Coop.CdMultAt(p.transform.position) * Rogue.CdMult(p))));
                skillEText.text = cdLeft > 0f ? cdLeft.ToString("0.0") : p.SkillName;
                float en = p.energy / p.energyMax;
                skillQCd.anchorMax = new Vector2(1f, p.UltUnlocked ? Mathf.Clamp01(1f - en) : 1f);
                bool ready = p.UltReady;
                skillQText.text = !p.UltUnlocked ? $"{Growth.UltAt} 级解锁" : ready ? p.UltName : Mathf.FloorToInt(en * 100f) + "%";
                float eLeft = Mathf.Max(0f, p.echoReadyAt - Time.time);
                skillRCd.anchorMax = new Vector2(1f, !p.EchoUnlocked ? 1f : Mathf.Clamp01(eLeft / Mathf.Max(0.01f, p.EchoCdNow * Coop.CdMultAt(p.transform.position))));
                skillRText.text = !p.EchoUnlocked ? $"{Growth.EchoAt} 级解锁" : eLeft > 0f ? eLeft.ToString("0.0") : p.echo != null ? ShortEcho(p.echo.name) : "声骸";
                var aw = p.kit.Active;
                int heroIcon = p.Id == HeroId.Feilie ? 40 : p.Id == HeroId.Qinglan ? 42 : 44;
                SetSkillArt(0, heroIcon, cdLeft <= 0f, true);
                SetSkillArt(1, heroIcon + 1, ready, p.UltUnlocked);
                SetSkillArt(2, p.echo != null ? UISkin.Echo(p.echo.id) : -1, eLeft <= 0f && p.EchoUnlocked, p.EchoUnlocked);
                SetSkillArt(3, aw != null ? 30 + (int)aw.def.type : -1, p.WeaponSkillReady, aw != null);
                if (aw != null)
                {
                    float vLeft = Mathf.Max(0f, aw.skillReadyAt - Time.time);
                    skillVCd.anchorMax = new Vector2(1f, Mathf.Clamp01(vLeft / Mathf.Max(0.01f, aw.SkillDef.cd)));
                    skillVText.text = vLeft > 0f ? vLeft.ToString("0.0") : aw.SkillDef.name;
                }
                // 武器栏：手持的高亮，X / 滚轮切换
                var ws = new System.Text.StringBuilder();
                for (int i = 0; i < 3; i++)
                {
                    var w = p.kit.weapons[i];
                    string n = w == null ? "<color=#5a606a>—</color>" : $"<color=#{ColorUtility.ToHtmlStringRGB(Weapons.StarCol[w.star])}>{w.Name}</color>";
                    ws.Append(i == p.kit.active ? $"<b>▶ {n}</b>   " : $"{n}   ");
                }
                weaponLabel.text = ws + $"<size=13><color=#8d96a3>{GameInput.Hint("X / 滚轮","LT + Y")} 切换</color></size>";
                // 协奏提示：自己满了提示按键；队友刚接力时提示跟上（凑团队技能）
                string hint = "";
                if (p.concerto >= p.concertoMax)
                {
                    var m0 = Coop.Teammate(p, 0); var m1 = Coop.Teammate(p, 1);
                    hint = $"协奏已满 · {GameInput.Hint("1","LT+LB")} 接力给{(m0 ? m0.HeroName : "-")}  {GameInput.Hint("2","LT+RB")} 接力给{(m1 ? m1.HeroName : "-")}";
                    if (Coop.lastRelayer && Coop.lastRelayer != p && Time.time - Coop.lastRelayT < Coop.RelayWindow && Coop.teamSkillCharges > 0 && p.TeamUnlocked && Coop.lastRelayer.TeamUnlocked)
                        hint = $"{Coop.lastRelayer.HeroName}刚接力 · {Coop.RelayWindow - (Time.time - Coop.lastRelayT):0.0} 秒内接力触发团队技能！";
                }
                coopHint.text = hint;
                skillQFrame.color = ready ? new Color(0.35f, 0.16f, 0.06f, 0.95f) * (0.8f + 0.2f * Mathf.Sin(Feel.Clock * 8f)) + new Color(0, 0, 0, 0.3f) : new Color(0.08f, 0.09f, 0.12f, 0.9f);
            }
            var b = Combat.boss;
            bossPanel.SetActive(b != null);
            if (b)
            {
                bossNameText.text = b.bossName;
                float hp = b.dmg.hp / b.dmg.maxHp;
                SetFill(bHp, hp);
                bGhost = Mathf.Max(hp, Mathf.MoveTowards(bGhost, hp, Feel.DT * 0.25f));
                SetFill(bHpGhost, bGhost);
                SetFill(bStg, b.dmg.Broken ? (b.dmg.brokenUntil - Time.time) / b.dmg.breakDuration : b.dmg.stagger / b.dmg.staggerMax);
                bossTag.text = b.dmg.Broken ? $"失衡 {(b.dmg.brokenUntil - Time.time):0.0}s  伤害 +50%" : b.Chanting ? "咏唱中 · 架势伤害 ×1.7" : "";
                bossTag.color = b.Chanting && !b.dmg.Broken ? new Color(0.8f, 0.6f, 1f) : StgCol;
                chantPanel.SetActive(b.Chanting);
                if (b.Chanting) SetFill(chantFill, b.ChantProgress);
            }
            if (SliceGame.I) timerText.text = SliceGame.RunMode ? $"{SliceGame.I.TimerText}    团队技能 ×{Coop.teamSkillCharges}{(Coop.protocols.Count > 0 ? "（下一次：" + Coop.ProtocolName(Coop.protocols[0]) + "）" : "")}" : $"{SliceGame.I.TimerText}    侵蚀 {Coop.erosion:0}%    团队技能 ×{Coop.teamSkillCharges}";
            // DS-010（KB-005）：压力数值与超频图标显隐
            {
                float ero = Coop.erosion;
                float oc = p ? p.overclock : 0f;
                var rd = RunDirector.I;
                bool forced = rd && rd.forceAtSec > 0f && rd.runSeconds < rd.forceAtSec && !rd.finaleStarted;
                eroIcon.color = ero >= 100f ? new Color(1f, 0.55f, 0.5f) : Color.white;
                eroText.text = $"侵蚀 {ero:0}%";
                // 到满预估（只有侵蚀没满时）
                float toFullMin = 0f;
                if (rd && ero < 100f) toFullMin = (100f - ero) / Mathf.Max(0.001f, RunDirector.ErosionRate(rd.RunMinutes)) / 60f;
                if (forced)
                {
                    eroBarBg.gameObject.SetActive(false);
                    forceBarBg.gameObject.SetActive(true);
                    float remain = Mathf.Max(0f, rd.forceAtSec - rd.runSeconds);
                    forceFillRt.anchorMax = new Vector2(Mathf.Clamp01(remain / (60f * RunDirector.LengthScale)), 1f);
                    forceFillRt.anchorMin = new Vector2(0f, 0f);
                    etaText.text = $"<color=#ff4d40>强制决战 {Mathf.CeilToInt(remain):0}s</color>";
                }
                else
                {
                    eroBarBg.gameObject.SetActive(true);
                    forceBarBg.gameObject.SetActive(false);
                    eroFillRt.anchorMax = new Vector2(Mathf.Clamp01(ero / 100f), 1f);
                    eroFillRt.anchorMin = new Vector2(0f, 0f);
                    etaText.text = ero >= 100f ? "侵蚀已满——立即前往黑潮之心" : $"≈{toFullMin:0.0} 分钟满 · 满后 60 秒强制决战";
                }
                // 60% / 85% 各警示一次（不刷屏）
                if (!warned60 && ero >= 60f) { warned60 = true; Combat.Toast("侵蚀过半——时间在流逝", new Color(1f, 0.7f, 0.4f)); }
                if (!warned85 && ero >= 85f) { warned85 = true; Combat.Toast("侵蚀逼近满值——尽快做出抉择", new Color(1f, 0.45f, 0.4f)); }
                ocIcon.gameObject.SetActive(oc > 0.5f);
                ocIcon.color = oc >= 92f ? new Color(1f, 0.55f, 0.5f) : Color.white;
                ocText.text = oc > 0.5f ? $"超频 {oc:0}%" : "";
            }
            TickParty();

            // 中央提示
            float tl = toastUntil - Feel.Clock;
            toast.gameObject.SetActive(tl > 0f);
            toastBanner.gameObject.SetActive(tl > 0f);
            toastBanner.color = new Color(1f, 1f, 1f, Mathf.Clamp01(tl / 0.4f));
            if (tl > 0f)
            {
                toast.transform.localScale = Vector3.Lerp(toast.transform.localScale, Vector3.one, Feel.DT * 14f);
                var c = toast.color; c.a = Mathf.Clamp01(tl / 0.4f); toast.color = c;
            }

            // 伤害数字：弹出放大→上浮→淡出
            for (int i = nums.Count - 1; i >= 0; i--)
            {
                var n = nums[i];
                float age = Feel.Clock - n.t0;
                float life = n.kind == 4 ? 1.4f : n.kind == 5 ? 0.75f : 0.9f;
                if (age > life || !cam) { n.t.gameObject.SetActive(false); pool.Push(n.t); nums.RemoveAt(i); continue; }
                Vector3 sp = cam.WorldToScreenPoint(n.w);
                if (sp.z < 0f) { n.t.gameObject.SetActive(false); continue; }
                var uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, sp, uiCam, out var local);
                float pop = age < 0.08f ? Mathf.Lerp(1.7f, 1f, age / 0.08f) : 1f;
                n.t.rectTransform.anchoredPosition = local + n.jitter + new Vector2(0f, age * 70f);
                n.t.transform.localScale = Vector3.one * pop;
                var c = n.t.color; c.a = Mathf.Clamp01((life - age) / 0.3f); n.t.color = c;
            }
        }
    
        void TickParty()
        {
            for (int i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                var h = f.h;
                if (!h) continue;
                bool me = h == Combat.player;
                int key = me ? 0 : (Coop.Teammate(Combat.player, 0) == h ? 1 : 2);
                string relayKey=GameInput.Hint(key.ToString(),key==1 ? "LT+LB" : "LT+RB");
                f.name.text = $"{(me ? "▶ " : $"<size=16><color=#9aa4ae>[{relayKey}]</color></size> ")}<color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(h.Color, Color.white, 0.35f))}>{h.HeroName}</color>  <size=15><color=#9aa4ae>{h.Role}{(me && HeroBase.Autopilot ? " · 自动" : me ? "" : " · AI")}</color></size>  <size=16><color=#ffe08a>Lv.{h.EffLevel}</color></size>";
                SetFill(f.xp, h.level >= Growth.MaxLevel ? 1f : h.exp / Growth.ExpNeed(h.level));
                f.bg.color = me ? new Color(0.1f, 0.09f, 0.07f, 0.6f) : new Color(0.03f, 0.04f, 0.06f, 0.45f);
                SetFill(f.hp, h.dmg.hp / h.dmg.maxHp);
                SetFill(f.co, h.concerto / h.concertoMax);
                SetFill(f.shield, (h.dmg.hp + h.dmg.shield) / h.dmg.maxHp);
                string st;
                if (h.Downed) st = $"<color=#ff7a6a>倒地 {Mathf.Max(0f, h.downedUntil - Time.time):0}s</color>{(h.rescuer ? $"  {h.rescuer.HeroName}救援中" : "")}";
                else
                {
                    st = h.concerto >= h.concertoMax ? "<color=#ffe08a>协奏已满</color>" : $"协奏 {h.concerto:0}";
                    if (Coop.OcActiveAt(h.transform.position)) st += $"   <color=#66f2e6>超频 {h.overclock:0}</color>";
                    if (h.dmg.shield > 1f) st += $"   <color=#e8eef0>盾 {h.dmg.shield:0}</color>";
                    if (h.penalized) st += "   <color=#ff9a7a>掉级</color>";
                }
                f.status.text = st;

                // 倒地标记：头顶倒计时 + 救援进度
                var dl = downLabels[i];
                bool show = h.Downed && cam;
                dl.gameObject.SetActive(show);
                if (show)
                {
                    Vector3 sp = cam.WorldToScreenPoint(h.transform.position + Vector3.up * 1.6f);
                    if (sp.z < 0f) { dl.gameObject.SetActive(false); continue; }
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, sp, null, out var local);
                    dl.rectTransform.anchoredPosition = local;
                    float left = Mathf.Max(0f, h.downedUntil - Time.time);
                    if (h.rescuer)
                    {
                        float k = Mathf.Clamp01(h.rescueProgress / h.rescuer.RescueHold);
                        int n = Mathf.RoundToInt(k * 10f);
                        dl.text = $"<color=#7fffd0>救援 {new string('■', n)}{new string('□', 10 - n)}</color>";
                    }
                    else dl.text = $"{h.HeroName} 倒地 {left:0}s\n<size=16>靠近按住 {GameInput.Hint("F","A")} 救援</size>";
                }
            }
        }
}
}

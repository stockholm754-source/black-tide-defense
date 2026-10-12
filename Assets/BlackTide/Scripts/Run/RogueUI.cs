using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlackTide
{
    // 增益三选一界面（M4；2026-10-09 构筑重做后显示流派、稀有度、等级、流派共鸣进度）：右侧常驻「团队击杀计数 / 下一个门槛 / 待选次数」；按 B 打开三张卡，点击或按 Z / X / C 选。
    // 原稿：选项框「点击打开」，不强制弹出，不暂停（多人里不能让一个人卡住全队）。打开时玩家角色站着不动（同商店）。
    public class RogueUI : MonoBehaviour
    {
        public static RogueUI I;
        public static bool IsOpen => I && I.panel && I.panel.activeSelf;

        Font font;
        RectTransform root;
        Text counter, owned, choiceHint;
        GameObject panel;
        readonly Button[] cards = new Button[3];
        readonly Text[] cardText = new Text[3];
        readonly Image[] cardBg = new Image[3];
        readonly Image[] cardBadge = new Image[3];

        public void Build()
        {
            I = this;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" }, 24);
            var cgo = new GameObject("RogueCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 21;
            var sc = cgo.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            root = cgo.GetComponent<RectTransform>();

            // 右侧（小地图下面）的计数
            counter = Label(root, "", 19, TextAnchor.UpperRight, new Color(1f, 0.88f, 0.6f), new Vector2(1f, 1f), new Vector2(-28f, -300f), new Vector2(420f, 60f), new Vector2(1f, 1f));

            // 三选一面板
            var bg = Box(root, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(1120f, 700f), new Color(0.02f, 0.03f, 0.05f, 0.94f));
            panel = bg.gameObject;
            GamepadUI.Attach(panel, () => IsOpen && !TutorialUI.IsOpen && !ConsoleUI.IsOpen, () => panel.SetActive(false), true);
            var prt = bg.rectTransform;
            choiceHint=Label(prt, "", 24, TextAnchor.MiddleCenter, new Color(1f, 0.88f, 0.6f), new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(1000f, 40f));
            PadHints.Create(prt, "Left:选择|Right:选择|A:确认|B:返回", new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -76f), 15, 22);   // DS-020：手柄图标行
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                cardBg[i] = Box(prt, new Vector2(0.5f, 0.5f), new Vector2(-355f + i * 355f, 24f), new Vector2(330f, 500f), Color.white);
                cardBg[i].sprite = UISkin.Get(5);
                cards[i] = cardBg[i].gameObject.AddComponent<Button>();
                cards[i].onClick.AddListener(() => PickPlayer(k));
                var cb = cards[i].colors; cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f); cards[i].colors = cb;
                cardBadge[i] = UISkin.Image(cardBg[i].transform, 20, new Vector2(0.5f, 1f), new Vector2(0f, -46f), new Vector2(60f, 60f));
                cardText[i] = Label(cardBg[i].rectTransform, "", 19, TextAnchor.UpperLeft, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0, -38), new Vector2(198f, 364f));
                cardText[i].horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            owned = Label(prt, "", 16, TextAnchor.UpperLeft, new Color(0.8f, 0.85f, 0.92f), new Vector2(0.5f, 0f), new Vector2(0f, 62f), new Vector2(1060f, 90f));
            owned.horizontalOverflow = HorizontalWrapMode.Wrap;
            panel.SetActive(false);
        }

        void Update()
        {
            // DS-028：面板打开 = 全局暂停（Time.timeScale=0，敌人/弹幕/侵蚀/冷却全冻）
            // 声明式同步，覆盖所有开/关路径（B 键、Esc、手柄 B、受击收起、选完自动关）
            Feel.Pause(panel.activeSelf);
            var h = Combat.player;
            if (!h) return;
            choiceHint.text=GameInput.Hint("选择一个增益（1 / 2 / 3 或点击；B 关闭，稍后再选）","选择一个增益（摇杆 / 十字键左右选择 · A 确认 · B 返回）");
            int pts = Rogue.TeamPoints, u = Rogue.Unlocks(pts);
            int next = NextThreshold(pts);
            counter.text = $"团队击杀计数 {pts}（下一次 {next}）" + (h.rogue.pending > 0 && !HeroBase.Autopilot ? $"\n<color=#ffd27a>{GameInput.Hint("B","LS 按下")}  选择增益（{h.rogue.pending}）</color>" : "") +
                           $"\n<size=15><color=#a9b2bf>已解锁 {u} 次 · 构筑 {h.rogue.owned.Count} 个</color></size>";
            if (GameInput.Down(GameAction.Cards) && !HeroBase.Autopilot && !ShopUI.IsOpen && !TutorialUI.IsOpen && !ConsoleUI.IsOpen)
            {
                GameInput.BlockCombatFrame();
                if (IsOpen) panel.SetActive(false);
                else if (h.rogue.pending > 0 && !h.Downed) panel.SetActive(true);
            }
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) { GameInput.BlockCombatFrame(); panel.SetActive(false); }
            if (!IsOpen) return;
            if (h.rogue.pending <= 0 || h.Downed) { panel.SetActive(false); return; }
            var o = Rogue.Offer(h);
            var rs = h.rogue;
            for (int i = 0; i < 3; i++)
            {
                var b = o[i];
                Color c = Rogue.SchoolCol[(int)b.school], rc = Rogue.RarityCol[(int)b.rarity];
                cardBg[i].color = Color.white;
                cardBg[i].sprite = UISkin.Get(b.school == School.诅咒 ? 8 : 5 + (int)b.rarity);
                cardBadge[i].sprite = UISkin.Get(20 + (int)b.school);
                if (b.school == School.诅咒) rc = c;   // 诅咒卡整张发红
                int lv = b.repeat ? 0 : rs.Lv(b.id);
                string tag = b.repeat ? "" : lv > 0 ? $" · <color=#ffd27a>升级 Lv{lv}→{lv + 1}</color>" : b.only.HasValue ? " · <color=#ffd27a>专属</color>" : b.team ? " · 团队" : "";
                string body = lv > 0 ? "<color=#8d96a3>" + b.Desc(1) + "</color>" + NL + "<color=#ffd27a>升级后：</color>" + b.Desc(lv + 1) : b.Desc(1);
                body=GameInput.ControlText(body);
                string foot = "";
                if (b.school != School.诅咒 && !b.repeat)
                {
                    int sp = rs.SchoolPts(b.school), after = sp + 1;
                    int tierA = after >= Rogue.Tier2 ? 2 : after >= Rogue.Tier1 ? 1 : 0, tierB = rs.Tier(b.school);
                    foot = $"<size=15><color=#{Hex(c)}>{b.school} {sp} → {after} 层</color>" +
                           (tierA > tierB ? $"\n<color=#ffd27a>触发共鸣{(tierA == 2 ? "Ⅱ" : "Ⅰ")}：{Rogue.Resonance[(int)b.school, tierA - 1]}</color>"
                                          : $"\n<color=#8d96a3>{(after < Rogue.Tier1 ? Rogue.Tier1 : Rogue.Tier2)} 层共鸣{(after < Rogue.Tier1 ? "Ⅰ" : "Ⅱ")}：{Rogue.Resonance[(int)b.school, after < Rogue.Tier1 ? 0 : 1]}</color>") + "</size>";
                }
                else if (b.school == School.诅咒) foot = "<size=15><color=#ff6a6a>诅咒：不计入流派，选了就不能移除</color></size>";
                cardText[i].text = $"<size=16><color=#ffd27a>{GameInput.Hint((i + 1).ToString(),(i+1).ToString())}</color>   <color=#{Hex(c)}>{b.school}</color> · <color=#{Hex(rc)}>{b.rarity}</color>{tag}</size>\n" +
                                   $"<size=14><color=#8d96a3>{Rogue.SchoolTag[(int)b.school]}</color></size>\n\n<size=28><b>{b.name}</b></size>\n\n{body}\n\n{foot}";
            }
            owned.text = BuildText(h);
            // DS-006 P1：X 同时是「切武器」，在面板里按 X 会误选且不可撤销 → 选卡改 1/2/3。
            // 面板是模态的（GameInput.Modal → HeroBase 走 CombatBlocked 分支），所以选卡时按 1/2 不会触发接力。
            if (Input.GetKeyDown(KeyCode.Alpha1)) PickPlayer(0);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) PickPlayer(1);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) PickPlayer(2);
        }

        // DS-006 P1：面板打开期间不能自卫（不能移动/闪避，只能挨打）。挨打就立刻收起面板，
        // 待选次数保留，按 B 可再打开——把「被迫站桩」的代价还给玩家，而不是让 UI 吞掉一次战斗。
        public static void CloseOnHurt()
        {
            if (I && I.panel && I.panel.activeSelf)
            {
                I.panel.SetActive(false);
                Feel.Pause(false);          // DS-028：立刻解冻（不等下一帧）
                Combat.Toast("受击：选卡面板已收起，按 B 可再看（待选未丢失）", new Color(1f, 0.85f, 0.5f));
            }
        }

        void PickPlayer(int i)
        {
            var h = Combat.player;
            if (!h || h.rogue.pending <= 0) return;
            Rogue.Pick(h, Rogue.Offer(h)[i]);
            Feel.I.Sfx("buff_pick", 0.65f, 0f);
            if (h.rogue.pending <= 0) panel.SetActive(false);
        }

        static readonly string NL = System.Environment.NewLine;
        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        // 构筑一览：按流派分组，带等级和共鸣状态
        public static string BuildText(HeroBase h)
        {
            var rs = h.rogue;
            var parts = new List<string>();
            for (int k = 0; k <= (int)School.诅咒; k++)
            {
                var sc = (School)k;
                var names = new List<string>();
                foreach (var b in rs.owned) if (b.school == sc) names.Add(b.name + (rs.Lv(b.id) > 1 ? " " + rs.Lv(b.id) : ""));
                if (names.Count == 0) continue;
                int t = sc == School.诅咒 ? 0 : rs.Tier(sc);
                string head = sc == School.诅咒 ? "诅咒" : $"{sc} {rs.SchoolPts(sc)}{(t == 2 ? " Ⅱ" : t == 1 ? " Ⅰ" : "")}";
                parts.Add($"<color=#{Hex(Rogue.SchoolCol[k])}>{head}</color>：{string.Join("、", names)}");
            }
            if (rs.minor > 0) parts.Add($"小幅强化 ×{rs.minor}");
            if (h.echo != null) parts.Add($"<color=#{Hex(h.echo.Col)}>声骸</color>：{h.echo.name}");
            if (h.kit.affixes.Count > 0) parts.Add($"武器 {h.kit.weaponStar}★：{WeaponAffix.Describe(h.kit)}");
            return GameInput.ControlText($"{h.HeroName}的构筑　" + (parts.Count > 0 ? string.Join("　·　", parts) : "（还没有）"));
        }

        static int NextThreshold(int pts)
        {
            foreach (var t in Rogue.Thresholds) if (t > pts) return t;
            int last = Rogue.Thresholds[Rogue.Thresholds.Length - 1];
            return last + ((pts - last) / Rogue.Step + 1) * Rogue.Step;
        }

        Image Box(RectTransform p, Vector2 anchor, Vector2 pos, Vector2 size, Color c)
        {
            var go = new GameObject("Box", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(p, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var img = go.GetComponent<Image>(); img.color = c;
            return img;
        }

        Text Label(RectTransform p, string s, int size, TextAnchor a, Color c, Vector2 anchor, Vector2 pos, Vector2 sz, Vector2? pivot = null)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(p, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = sz;
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = a; t.color = c; t.text = s; t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = go.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.8f); sh.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }
    }
}

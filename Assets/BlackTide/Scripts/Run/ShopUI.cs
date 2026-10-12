using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlackTide
{
    // 经济界面（M3）：底部中间的快捷栏（3 / 4 / 5 用道具，6 用团队道具）和贝币；靠近商人按 G 打开商店。
    // 商店开着时游戏不暂停（原稿：商人不能耽误跑图），玩家角色原地不动、不接受战斗输入。
    public class ShopUI : MonoBehaviour
    {
        public static ShopUI I;
        public static bool IsOpen => I && I.panel && I.panel.activeSelf;
        public const float Range = 5f;

        Font font;
        RectTransform root;
        Text coinText, keyText, resourceStatus, prompt;
        readonly Text[] slotText = new Text[Economy.Slots + 1];
        readonly Image[] slotBg = new Image[Economy.Slots + 1];
        readonly Image[] slotIcon = new Image[Economy.Slots + 1];
        GameObject panel;
        RectTransform list;
        Text title, info, closeHint;
        NodeRuntime shopNode;
        readonly List<(ShopOffer o, Button b, Text t)> rows = new List<(ShopOffer, Button, Text)>();

        static readonly Color Gold = new Color(1f, 0.8f, 0.36f);
        static readonly Color[] RarityCol = { new Color(0.85f, 0.88f, 0.92f), new Color(0.5f, 0.75f, 1f), new Color(1f, 0.7f, 0.3f) };

        public void Build()
        {
            I = this;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" }, 24);
            if (!FindObjectOfType<EventSystem>()) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var cgo = new GameObject("ShopCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var sc = cgo.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            root = cgo.GetComponent<RectTransform>();

            // ---- 快捷栏 ----
            for (int i = 0; i <= Economy.Slots; i++)
            {
                float x = -258f + i * 172f;
                slotBg[i] = Box(root, new Vector2(0.5f, 0f), new Vector2(x, 34f), new Vector2(160f, 50f), new Color(0.03f, 0.04f, 0.06f, 0.6f));
                slotText[i] = Label(slotBg[i].rectTransform, "", 17, TextAnchor.MiddleCenter, Color.white, Vector2.zero, new Vector2(160f, 50f));
                slotIcon[i] = UISkin.Image(slotBg[i].transform, 50, new Vector2(0.5f, 0.5f), new Vector2(-56f, 0f), new Vector2(38f, 38f));
                slotText[i].rectTransform.anchoredPosition = new Vector2(21f, 0f);
                slotText[i].rectTransform.sizeDelta = new Vector2(112f, 50f);
                slotText[i].fontSize = 16;
            }
            coinText = Label(root, "", 20, TextAnchor.MiddleRight, Gold, new Vector2(-28f, -376f), new Vector2(170f, 28f), Vector2.one, Vector2.one);
            keyText = Label(root, "", 20, TextAnchor.MiddleRight, Gold, new Vector2(-224f, -376f), new Vector2(144f, 28f), Vector2.one, Vector2.one);
            UISkin.Image(root, 60, Vector2.one, new Vector2(-212f, -390f), new Vector2(28f, 28f));
            UISkin.Image(root, 61, Vector2.one, new Vector2(-382f, -390f), new Vector2(28f, 28f));
            resourceStatus = Label(root, "", 17, TextAnchor.UpperRight, new Color(0.78f, 0.7f, 0.95f), new Vector2(-28f, -412f), new Vector2(360f, 54f), Vector2.one, Vector2.one);
            prompt = Label(root, "", 24, TextAnchor.MiddleCenter, new Color(0.6f, 1f, 0.55f), new Vector2(0f, 150f), new Vector2(700f, 34f), new Vector2(0.5f, 0f));

            // ---- 商店面板 ----
            var bg = Box(root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 700f), new Color(0.03f, 0.04f, 0.06f, 0.9f));
            panel = bg.gameObject;
            GamepadUI.Attach(panel, () => IsOpen && !TutorialUI.IsOpen && !ConsoleUI.IsOpen, () => panel.SetActive(false));
            var prt = bg.rectTransform;
            title = Label(prt, "", 28, TextAnchor.MiddleLeft, Gold, new Vector2(24f, -30f), new Vector2(700f, 40f), new Vector2(0f, 1f), new Vector2(0f, 0.5f));
            info = Label(prt, "", 18, TextAnchor.MiddleLeft, new Color(0.85f, 0.88f, 0.95f), new Vector2(24f, -66f), new Vector2(712f, 28f), new Vector2(0f, 1f), new Vector2(0f, 0.5f));
            var viewport = Box(prt, new Vector2(0f, 1f), new Vector2(20f, -92f), new Vector2(720f, 520f), Color.clear).rectTransform;
            viewport.pivot = new Vector2(0f, 1f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.viewport = viewport;
            list = new GameObject("List", typeof(RectTransform)).GetComponent<RectTransform>();
            list.SetParent(viewport, false);
            list.anchorMin = list.anchorMax = new Vector2(0f, 1f); list.pivot = new Vector2(0f, 1f);
            list.anchoredPosition = Vector2.zero; list.sizeDelta = new Vector2(720f, 520f);
            scroll.content = list;
            Button(prt, "按推荐补货", new Vector2(-150f, 34f), new Vector2(260f, 46f), () => { AutoBuyPlayer(); Refresh(); });
            closeHint=Button(prt, "", new Vector2(150f, 34f), new Vector2(260f, 46f), () => panel.SetActive(false)).GetComponentInChildren<Text>();
            PadHints.Create(prt, "Up:使用|Down:团队|A:购买|B:关闭", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -310f), 15, 22);   // DS-020：手柄图标行
            panel.SetActive(false);
        }

        void Update()
        {
            var h = Combat.player;
            if (!h) return;
            closeHint.text=GameInput.Hint("关闭（G / Esc）","关闭（B 返回）");
            var k = h.kit;
            for (int i = 0; i < Economy.Slots; i++)
            {
                var d = k.count[i] > 0 ? Economy.Item(k.slot[i]) : null;
                slotIcon[i].sprite = UISkin.Get(d != null ? UISkin.Item(d.id) : -1);
                slotIcon[i].enabled = slotIcon[i].sprite != null;
                string itemKey=GameInput.Hint((i+3).ToString(),GameInput.ItemSlot==i ? "↑ 使用" : "←/→ 选择");
                string separator=GameInput.Hint("  ","\n");
                slotText[i].text = d != null ? $"<color=#ffd27a>{itemKey}</color>{separator}{d.name} ×{k.count[i]}" : $"<color=#ffd27a>{itemKey}</color>{separator}<color=#777777>空</color>";
                slotBg[i].color = d != null ? new Color(0.06f, 0.08f, 0.1f, 0.72f) : new Color(0.03f, 0.04f, 0.06f, 0.45f);
                if (GameInput.UsingPad && GameInput.ItemSlot==i) slotBg[i].color=new Color(0.3f,0.22f,0.07f,0.9f);
            }
            var td = Economy.teamItemCount > 0 ? Economy.Item(Economy.teamItem) : null;
            slotIcon[Economy.Slots].sprite = UISkin.Get(td != null ? UISkin.Item(td.id) : -1);
            slotIcon[Economy.Slots].enabled = slotIcon[Economy.Slots].sprite != null;
            slotText[Economy.Slots].text = td != null ? $"<color=#ffd27a>{GameInput.Hint("6","↓")}</color>  {td.name}（团队）" : $"<color=#ffd27a>{GameInput.Hint("6","↓")}</color>  <color=#777777>团队道具</color>";
            float vul = Miasma.VulnOf(h);
            coinText.text = $"贝币 {k.coins:0}";
            keyText.text = $"钥匙 {(RunDirector.I ? RunDirector.I.keys : 0)}";
            resourceStatus.text = (RunDirector.I && RunDirector.I.worldVessel > 0 ? $"净世之器（{GameInput.Hint("7","LT+↑")}）" : RunDirector.I && RunDirector.I.VesselActive ? "净世之器生效中" : "") + (vul > 0.01f ? $"  瘴气易伤 +{vul * 100f:0}%" : "");

            // 附近的商人
            NodeRuntime near = null;
            if (RunDirector.I)
                foreach (var n in RunDirector.I.nodes)
                    if (n.shop != null && Vector3.Distance(Flat(h.transform.position), Flat(n.pos)) < Range + 2f) near = n;
            prompt.text = near != null && !IsOpen ? $"{(GameInput.UsingPad ? "A" : "G")}  和{near.def.name}交易" : "";
            if ((Input.GetKeyDown(KeyCode.G) || (GameInput.PadDown(PadButton.A) && !GameInput.Modifier && !IsOpen)) && near != null && !HeroBase.Autopilot && !RogueUI.IsOpen && !TutorialUI.IsOpen && !ConsoleUI.IsOpen)
            {
                GameInput.BlockCombatFrame();
                if (IsOpen) panel.SetActive(false); else Open(near);
            }
            if (IsOpen && (Input.GetKeyDown(KeyCode.Escape) || near == null || h.Downed)) panel.SetActive(false);
            if (IsOpen) Refresh();
        }

        void Open(NodeRuntime n)
        {
            shopNode = n;
            panel.SetActive(true);
            foreach (var r in rows) Destroy(r.b.gameObject);
            rows.Clear();
            float top = 0f;
            foreach (var o in n.shop.offers)
            {
                if (o.IsWeapon && o.forHero != Combat.player.Id) continue;   // 只显示自己能用的武器
                var oo = o;
                float rowHeight = o.IsWeapon ? 80f : 48f;
                var b = Button(list, "", new Vector2(360f, -top - rowHeight * 0.5f), new Vector2(720f, rowHeight), () => { BuyPlayer(oo); Refresh(); }, new Vector2(0f, 1f));
                var t = b.GetComponentInChildren<Text>();
                t.alignment = TextAnchor.MiddleLeft; t.fontSize = 18;
                t.rectTransform.offsetMin = new Vector2(60f, 0f);
                UISkin.Image(b.transform, o.IsWeapon ? 30 + (int)(o.weapon != null ? o.weapon.def.type : Weapons.NativeType(Combat.player.Id)) : UISkin.Item(o.item.id), new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(36f, 36f));
                rows.Add((o, b, t));
                top += rowHeight + 6f;
            }
            list.sizeDelta = new Vector2(720f, Mathf.Max(520f, top));
            list.anchoredPosition = Vector2.zero;
            Refresh();
        }

        void Refresh()
        {
            if (shopNode == null) return;
            var h = Combat.player; var k = h.kit;
            title.text = $"{shopNode.def.name}（{shopNode.def.id}）";
            info.text = $"你的贝币 {k.coins:0}    武器 {k.weaponStar}★    快捷栏 {Used(k)}/{Economy.Slots} 种，每种最多 {Economy.Stack} 个" + (k.affixes.Count > 0 ? $"\n<size=15>武器词条：{WeaponAffix.Describe(k)}</size>" : "");
            foreach (var (o, b, t) in rows)
            {
                bool ok = Economy.CanBuy(k, o, h.Id, Combat.heroes.Count, out string why);
                string lim = o.IsWeapon ? $"伤害 ×{Economy.WeaponMult(o.star):0.00}" : o.item.team ? $"团队限购 {Economy.teamBought}/{o.item.limit}" : $"已买 {k.Bought(o.item.id)}/{o.item.limit}";
                Color c = o.IsWeapon ? new Color(1f, 0.82f, 0.4f) : RarityCol[(int)o.item.rarity];
                string price = o.item != null && o.item.team ? $"{o.price}（每人 {o.price / Combat.heroes.Count}）" : o.price.ToString();
                t.text = $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{o.Name}</color>   {price} 贝币   库存 {o.stock}   {lim}" +
                         (o.item != null ? $"\n<size=14><color=#9aa4b0>{o.item.desc}</color></size>" : "") +
                         (o.IsWeapon && o.weapon != null ? $"\n<size=14><color=#9aa4b0>{Weapons.Describe(o.weapon)}</color></size>" : "") +
                         (ok ? "" : $"   <color=#ff8a7a>{why}</color>");
                t.text=GameInput.ControlText(t.text);
                b.interactable = ok;
            }
        }

        static int Used(Kit k) { int n = 0; foreach (var c in k.count) if (c > 0) n++; return n; }

        List<Kit> Team() { var l = new List<Kit>(); foreach (var o in Combat.heroes) if (o) l.Add(o.kit); return l; }

        void BuyPlayer(ShopOffer o)
        {
            var h = Combat.player;
            if (!Economy.CanBuy(h.kit, o, h.Id, Combat.heroes.Count, out _)) return;
            Economy.Buy(h.kit, o, h.Id, Team(), true);
            if (o.IsWeapon)
            {
                if (Economy.lastReplaced != null) Weapons.Drop(Economy.lastReplaced, h.transform.position + h.transform.right * 1.3f, true);
                h.RefreshWeapon();
            }
            if (RunDirector.I) RunDirector.I.Event("buy", shopNode.def.id, $"{h.HeroName} {o.Name} {o.price}");
            Feel.I.Sfx("purchase", 0.65f, 0f);
        }

        void AutoBuyPlayer()
        {
            var h = Combat.player;
            var log = new List<string>();
            Economy.AutoBuy(h.kit, shopNode.shop, h.Id, Team(), Coop.erosion, RunDirector.I.RunMinutes, true, log);
            if (log.Count > 0 && RunDirector.I) RunDirector.I.Event("buy", shopNode.def.id, $"{h.HeroName} {string.Join(" ", log)}");
        }

        // ---------------- 小工具 ----------------
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

        Text Label(RectTransform p, string s, int size, TextAnchor a, Color c, Vector2 pos, Vector2 sz, Vector2? anchor = null, Vector2? pivot = null)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(p, false);
            rt.anchorMin = rt.anchorMax = anchor ?? new Vector2(0.5f, 0.5f);
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = sz;
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = a; t.color = c; t.text = s; t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            var sh = go.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.8f); sh.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        Button Button(RectTransform p, string s, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction click, Vector2? anchor = null)
        {
            var img = Box(p, anchor ?? new Vector2(0.5f, 0f), pos, size, new Color(0.12f, 0.15f, 0.2f, 0.95f));
            var b = img.gameObject.AddComponent<Button>();
            var cb = b.colors;
            cb.highlightedColor = new Color(1.4f, 1.4f, 1.4f); cb.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.7f);
            b.colors = cb;
            b.onClick.AddListener(click);
            var t = Label(img.rectTransform, s, 20, TextAnchor.MiddleCenter, Color.white, Vector2.zero, size);
            t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one; t.rectTransform.sizeDelta = Vector2.zero;
            return b;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}

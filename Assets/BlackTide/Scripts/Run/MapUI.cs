using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlackTide
{
    // 地图界面：右上角常驻小地图，M 打开大地图。
    // 显示节点（未触发 / 交战中 / 已清除）、黑潮覆盖范围、三名角色、路线 AI 当前的目标。
    public class MapUI : MonoBehaviour
    {
        RectTransform mini, big, bigRoot;
        Font font;
        Sprite dot, ring, thinRing;
        bool showBig;
        Text mapHint;

        class Layer
        {
            public RectTransform root;
            public float size;
            public RectTransform tide, tideEdge;
            public RawImage fog;
            public readonly List<(NodeRuntime n, Image img, Text label, Image check)> nodes = new List<(NodeRuntime, Image, Text, Image)>();
            public readonly List<(HeroBase h, Image img)> heroes = new List<(HeroBase, Image)>();
            public Image routeMark;
        }
        Layer miniL, bigL;

        static readonly Dictionary<NodeType, Color> TypeCol = new Dictionary<NodeType, Color>
        {
            { NodeType.Stronghold, new Color(0.95f, 0.85f, 0.45f) }, { NodeType.Camp, new Color(0.92f, 0.62f, 0.35f) },
            { NodeType.Nest, new Color(0.78f, 0.5f, 0.9f) }, { NodeType.Rift, new Color(0.3f, 0.95f, 0.9f) },
            { NodeType.WildBoss, new Color(0.95f, 0.36f, 0.32f) }, { NodeType.Merchant, new Color(0.55f, 0.9f, 0.48f) },
            { NodeType.Messenger, new Color(1f, 0.35f, 0.8f) }, { NodeType.Heart, new Color(0.2f, 1f, 1f) },
            { NodeType.Pack, new Color(0.7f, 0.7f, 0.7f) }, { NodeType.Vortex, new Color(0.7f, 0.35f, 1f) },
        };

        public void Build()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" }, 24);
            dot = MakeSprite(false, 0f); ring = MakeSprite(true, 0.84f); thinRing = MakeSprite(true, 0.975f);
            var cgo = new GameObject("MapCanvas", typeof(Canvas), typeof(CanvasScaler));
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 15;
            var sc = cgo.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            var crt = cgo.GetComponent<RectTransform>();

            // 小地图：右上角
            mini = Panel(crt, new Vector2(1, 1), new Vector2(-24, -24), 250f, 0.55f);
            var miniContent = new GameObject("MapContent", typeof(RectTransform)).GetComponent<RectTransform>();
            miniContent.SetParent(mini, false);
            miniContent.anchorMin = miniContent.anchorMax = new Vector2(0.5f, 0.5f);
            miniContent.sizeDelta = new Vector2(172f, 172f);
            miniL = BuildLayer(miniContent, 172f, false);
            // 圆形框只用于常驻小地图；大地图仍保留完整方形地图范围。
            Destroy(mini.GetComponent<RectMask2D>());
            var miniImage = mini.GetComponent<Image>(); miniImage.sprite = dot;
            mini.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            mini.parent.GetComponent<Image>().color = Color.clear;
            var miniFrame = UISkin.Image(mini.parent, 10, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(282f, 282f));
            miniFrame.transform.SetAsLastSibling();
            // 大地图：屏幕中间，默认隐藏
            bigRoot = Panel(crt, new Vector2(0.5f, 0.5f), Vector2.zero, 820f, 0.82f);
            bigL = BuildLayer(bigRoot, 820f, true);
            var title = mapHint = Label(bigRoot, "地图（M 关闭）    ○ 未触发   ✓ 已清除   锁 = 要钥匙    青色：黑潮覆盖范围    暗处：迷雾（走过或情报据点揭开）", 20, new Color(0.9f, 0.92f, 0.95f));
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(0, 22);
            title.rectTransform.sizeDelta = new Vector2(820, 30);
            bigRoot.parent.gameObject.SetActive(false);
        }

        RectTransform Panel(RectTransform parent, Vector2 anchor, Vector2 pos, float size, float alpha)
        {
            // 边框：底板后面垫一块略大的青色（不用 Outline：它会把半透明底板整块复制一份青色垫在下面）
            var frame = new GameObject("MapFrame", typeof(RectTransform), typeof(Image));
            var frt = frame.GetComponent<RectTransform>();
            frt.SetParent(parent, false);
            frt.anchorMin = frt.anchorMax = frt.pivot = anchor;
            frt.anchoredPosition = pos + new Vector2(anchor.x > 0.75f ? 2f : anchor.x < 0.25f ? -2f : 0f, anchor.y > 0.75f ? 2f : anchor.y < 0.25f ? -2f : 0f);
            frt.sizeDelta = new Vector2(size + 4f, size + 4f);
            var fimg = frame.GetComponent<Image>();
            fimg.color = new Color(0.3f, 0.95f, 0.9f, 0.45f);
            fimg.raycastTarget = false;

            var go = new GameObject("Map", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(frt, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.1f, 0.09f, 0.085f, Mathf.Max(alpha, 0.85f));
            img.raycastTarget = false;
            go.AddComponent<RectMask2D>();
            return rt;
        }

        Layer BuildLayer(RectTransform root, float size, bool labels)
        {
            var L = new Layer { root = root, size = size };
            L.tide = Img(root, dot, new Color(0.1f, 0.6f, 0.58f, 0.22f));
            L.tideEdge = Img(root, thinRing, new Color(0.35f, 1f, 0.95f, 0.95f));
            // 迷雾（Intel）：盖在黑潮上面、节点下面
            var fg = new GameObject("Fog", typeof(RectTransform), typeof(RawImage));
            var frt = fg.GetComponent<RectTransform>();
            frt.SetParent(root, false);
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero;
            L.fog = fg.GetComponent<RawImage>();
            L.fog.texture = Intel.tex; L.fog.raycastTarget = false;
            var d = RunDirector.I;
            foreach (var n in d.nodes)
            {
                float r = Mathf.Max(n.def.type == NodeType.Pack ? 3f : 5f, n.def.radius) / (MapLayout.Half * 2f) * size * 2f;
                var img = Img(root, n.def.type == NodeType.Pack ? dot : ring, TypeCol[n.def.type]).GetComponent<Image>();
                img.rectTransform.sizeDelta = Vector2.one * r;
                Text label = null;
                if (labels && n.def.type != NodeType.Pack)
                {
                    label = Label(root, n.def.name, 15, TypeCol[n.def.type]);
                    label.rectTransform.sizeDelta = new Vector2(160, 20);
                }
                var check = Img(root, dot, new Color(0.9f, 0.95f, 1f, 0.9f)).GetComponent<Image>();
                check.rectTransform.sizeDelta = Vector2.one * Mathf.Max(5f, r * 0.25f);
                L.nodes.Add((n, img, label, check));
            }
            L.routeMark = Img(root, ring, new Color(1f, 1f, 1f, 0.9f)).GetComponent<Image>();
            foreach (var h in Combat.heroes)
            {
                var img = Img(root, dot, h.Color).GetComponent<Image>();
                img.rectTransform.sizeDelta = Vector2.one * (labels ? 16f : 9f);
                img.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.9f);
                L.heroes.Add((h, img));
            }
            return L;
        }

        RectTransform Img(RectTransform parent, Sprite s, Color c)
        {
            var go = new GameObject("I", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var img = go.GetComponent<Image>();
            img.sprite = s; img.color = c; img.raycastTarget = false;
            return rt;
        }

        Text Label(RectTransform parent, string s, int size, Color c)
        {
            var go = new GameObject("T", typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.color = c; t.text = s; t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.raycastTarget = false;
            var sh = go.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.9f); sh.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        // 圆点 / 圆环贴图
        static Sprite MakeSprite(bool hollow, float inner)
        {
            int N = hollow && inner > 0.9f ? 512 : 128;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N / 2f, N / 2f)) / (N / 2f);
                float edge = N / 4f;
                float a = hollow ? Mathf.Clamp01((1f - d) * edge) * Mathf.Clamp01((d - inner) * edge) : Mathf.Clamp01((1f - d) * edge);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        }

        static Vector2 ToMap(Vector3 w, float size) => new Vector2(w.x, w.z) / (MapLayout.Half * 2f) * size;

        public static MapUI I;
        public static bool IsOpen => I && I.showBig;
        void Awake() { I = this; }
        public void SetBig(bool v) { GameInput.BlockCombatFrame(); showBig = v; bigRoot.parent.gameObject.SetActive(v); }

        void Update()
        {
            mapHint.text=$"地图（{GameInput.Hint("M","View / B")} 关闭）    ○ 未触发   ✓ 已清除   锁 = 要钥匙    青色：黑潮覆盖范围    暗处：迷雾";
            if (GameInput.Down(GameAction.Map) && !ShopUI.IsOpen && !RogueUI.IsOpen && !TutorialUI.IsOpen && !ConsoleUI.IsOpen) SetBig(!showBig);
            if (showBig && (Input.GetKeyDown(KeyCode.Escape) || GameInput.PadDown(PadButton.B))) SetBig(false);
        }

        void LateUpdate()
        {
            if (!RunDirector.I) return;
            Draw(miniL);
            if (showBig) Draw(bigL);
        }

        void Draw(Layer L)
        {
            float s = L.size;
            // 黑潮覆盖：以东北角为圆心的圆
            Vector2 tc = ToMap(Coop.tideCenter, s);
            float tr = Mathf.Max(0f, Coop.coverageRadius) / (MapLayout.Half * 2f) * s * 2f;
            L.tide.anchoredPosition = tc; L.tide.sizeDelta = Vector2.one * tr;
            L.tideEdge.anchoredPosition = tc; L.tideEdge.sizeDelta = Vector2.one * tr;
            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 6f);
            foreach (var (n, img, label, check) in L.nodes)
            {
                Vector2 p = ToMap(n.pos, s);
                img.rectTransform.anchoredPosition = p;
                Color c = TypeCol[n.def.type];
                bool done = n.Done, active = n.st == NodeRuntime.St.Active;
                img.color = done ? new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.45f, 0.7f) : active ? new Color(c.r, c.g, c.b, pulse) : c;
                check.rectTransform.anchoredPosition = p;
                check.gameObject.SetActive(done && n.def.type != NodeType.Pack);
                bool known = Intel.Known(n) && (n.def.type != NodeType.Vortex || RunDirector.I.vortexOpen);
                img.gameObject.SetActive(known);
                if (label) label.gameObject.SetActive(known);
                if (n.locked) c = Color.Lerp(c, Color.gray, 0.4f);
                if (label)
                {
                    label.rectTransform.anchoredPosition = p + new Vector2(0, -img.rectTransform.sizeDelta.y * 0.5f - 10f);
                    label.color = done ? new Color(c.r, c.g, c.b, 0.45f) : c;
                    label.text = n.def.name + (done ? " ✓" : active ? " ⚔" : n.locked ? " ·锁" : "");
                }
            }
            foreach (var (h, img) in L.heroes)
            {
                if (!h) continue;
                img.rectTransform.anchoredPosition = ToMap(h.transform.position, s);
                img.color = h.Downed ? new Color(0.5f, 0.5f, 0.5f) : h.Color;
                img.rectTransform.localScale = Vector3.one * (h.IsControlled ? 1.35f : 1f);
            }
            var rt = RunDirector.I.RouteNode;
            L.routeMark.gameObject.SetActive(rt != null && HeroBase.Autopilot);
            if (rt != null)
            {
                L.routeMark.rectTransform.anchoredPosition = ToMap(rt.pos, s);
                float r = Mathf.Max(6f, rt.def.radius) / (MapLayout.Half * 2f) * s * 2f + 6f;
                L.routeMark.rectTransform.sizeDelta = Vector2.one * r * (0.9f + 0.1f * pulse);
            }
        }
    }
}

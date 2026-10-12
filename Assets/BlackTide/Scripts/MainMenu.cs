using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BlackTide
{
    // DS-013 主菜单（用户要求，复用第八批 UI 素材）：黑潮之心背景 + 标题 + 开始/教学/退出。
    // 键盘：Enter / Space / B 开始 · H 教学 · Esc 关教学（再按退出）。
    public class MainMenu : MonoBehaviour
    {
        Font font;
        GameObject helpPanel;
        Text helpBody;
        Text styleLabel, startLabel, guideLabel, quitLabel, closeHint, footer;
        ScrollRect helpReader;
        bool helpPad;
        float nextRefresh;

        void Start()
        {
            FX.LoadComicStyle();      // CL-005：三态风格（写实 / 美漫 / 霓虹故障），PlayerPrefs 持久
            Build();
        }

        string StyleText() => "特效风格：" + FX.StyleName(FX.Style) + GameInput.Hint("（点按 / F8 切换）", "（A 切换）");

        void ToggleStyle()
        {
            FX.NextStyle();
            styleLabel.text = StyleText();
        }

        void Build()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" }, 24);
            if (!FindObjectOfType<EventSystem>()) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var cgo = new GameObject("MenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var sc = cgo.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            var root = cgo.GetComponent<RectTransform>();

            // ---- 背景：黑潮之心原图（cover 填充）+ 暗化遮罩 ----
            var tex = Resources.Load<Texture2D>("Menu/tide_heart");
            if (tex)
            {
                var bg = Img(root, "Bg", Vector2.zero, Vector2.zero, new Vector2(1920, 1080), Color.white);
                var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                bg.sprite = sp;
                bg.preserveAspect = true;
                float s = Mathf.Max(1920f / tex.width, 1080f / tex.height);
                bg.rectTransform.sizeDelta = new Vector2(tex.width * s, tex.height * s);
                bg.color = new Color(0.62f, 0.72f, 0.78f, 0.85f);
                Img(root, "Shade", Vector2.zero, Vector2.zero, new Vector2(1920, 1080), new Color(0.015f, 0.03f, 0.045f, 0.72f));
            }
            else Img(root, "Shade", Vector2.zero, Vector2.zero, new Vector2(1920, 1080), new Color(0.02f, 0.04f, 0.06f, 1f));

            // ---- 慢转的潮环装饰 ----
            var ringTex = Resources.Load<Texture2D>("VFX/ring_soft");
            if (ringTex)
            {
                var r1 = Img(root, "Ring1", new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(860, 860), new Color(0.3f, 0.9f, 0.85f, 0.16f));
                r1.sprite = Sprite.Create(ringTex, new Rect(0, 0, ringTex.width, ringTex.height), new Vector2(0.5f, 0.5f), 100f);
                r1.gameObject.AddComponent<Spin>().speed = 6f;
                var r2 = Img(root, "Ring2", new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(620, 620), new Color(0.3f, 0.9f, 0.85f, 0.10f));
                r2.sprite = r1.sprite;
                r2.gameObject.AddComponent<Spin>().speed = -4f;
            }

            // ---- 图标 + 标题 ----
            var iconTex = Resources.Load<Texture2D>("Menu/icon");
            if (iconTex)
            {
                var ic = Img(root, "Icon", new Vector2(0.5f, 0.5f), new Vector2(0, 300), new Vector2(128, 128), Color.white);
                ic.sprite = Sprite.Create(iconTex, new Rect(0, 0, iconTex.width, iconTex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            Label(root, "黑潮防线", 84, TextAnchor.MiddleCenter, new Color(0.96f, 0.86f, 0.58f), new Vector2(0.5f, 0.5f), new Vector2(0, 190), new Vector2(900, 110));
            Label(root, "鸣潮同人 · 三人联机 PvE Roguelike 原型", 24, TextAnchor.MiddleCenter, new Color(0.75f, 0.85f, 0.9f), new Vector2(0.5f, 0.5f), new Vector2(0, 136), new Vector2(900, 34));

            // ---- 按钮 ----
            startLabel=MenuButtonText(root, "", new Vector2(0, -60), () => SceneManager.LoadScene("BlackTide_Run"));
            guideLabel=MenuButtonText(root, "", new Vector2(0, -170), OpenHelp);
            styleLabel = MenuButtonText(root, StyleText(), new Vector2(0, -280), ToggleStyle);
            quitLabel=MenuButtonText(root, "", new Vector2(0, -390), () => Application.Quit());
            AudioControls.Create(root,font,new Vector2(.5f,.5f),new Vector2(620,-230),true);

            footer=Label(root, "", 16, TextAnchor.MiddleCenter, new Color(0.5f, 0.6f, 0.66f), new Vector2(0.5f, 0f), new Vector2(0, 26), new Vector2(600, 24));

            // ---- 教学面板 ----
            var pbg = Img(root, "HelpPanel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1460, 900), new Color(0.015f, 0.02f, 0.03f, 0.97f));
            helpPanel = pbg.gameObject;
            var prt = pbg.rectTransform;
            Label(prt, "操作与系统说明", 34, TextAnchor.MiddleLeft, new Color(1f, 0.86f, 0.55f), new Vector2(0.5f, 1f), new Vector2(-670, -38), new Vector2(1300, 48)).rectTransform.pivot=new Vector2(0f,0.5f);
            closeHint=Label(prt, "", 18, TextAnchor.MiddleRight, new Color(0.6f, 0.7f, 0.78f), new Vector2(0.5f, 1f), new Vector2(480, -44), new Vector2(480, 30));
            PadHints.Create(prt, "A:确认|B:返回", new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(400f, -80f), 15, 22);   // DS-020：手柄图标行
            helpBody = Label(prt, "", 19, TextAnchor.UpperLeft, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0, -42), new Vector2(1340, 780));
            helpBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            helpBody.verticalOverflow = VerticalWrapMode.Overflow;
            helpBody.text = TutorialUI.GuideSections();
            helpReader=GamepadUI.MakeTextReader(helpPanel,helpBody,()=>helpPanel.activeSelf);
            helpPad=GameInput.UsingPad;
            helpPanel.SetActive(false);
            GamepadUI.Attach(cgo, () => !helpPanel.activeSelf, null);
        }

        void OpenHelp() { helpBody.text=TutorialUI.GuideSections(); GamepadUI.RefreshReader(helpReader,helpBody); helpPad=GameInput.UsingPad; helpPanel.SetActive(true); }

        void Update()
        {
            startLabel.text=GameInput.Hint("开始游戏（Enter）","开始游戏（A）");
            guideLabel.text=GameInput.Hint("操作教学（H）","操作教学（Menu / A）");
            quitLabel.text=GameInput.Hint("退出游戏（Esc）","退出游戏（A）");
            styleLabel.text=StyleText();
            footer.text=GameInput.Hint("v0.1 · H 查看键位","v0.1 · 摇杆 / 十字键选择 · A 确认");
            closeHint.text=GameInput.Hint("H / Esc 返回 · 滚轮阅读","B 返回 · 摇杆阅读");
            if(helpPanel.activeSelf && helpPad!=GameInput.UsingPad) { helpBody.text=TutorialUI.GuideSections(); GamepadUI.RefreshReader(helpReader,helpBody); helpPad=GameInput.UsingPad; }

            if (Input.GetKeyDown(KeyCode.Escape) || (helpPanel.activeSelf && GameInput.PadDown(PadButton.B)))
            {
                if (helpPanel.activeSelf) helpPanel.SetActive(false);
                else Application.Quit();
            }
            if (GameInput.Down(GameAction.Guide)) { if (helpPanel.activeSelf) helpPanel.SetActive(false); else OpenHelp(); }
            if (!helpPanel.activeSelf && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.B)))
                SceneManager.LoadScene("BlackTide_Run");
        }

        void MenuButton(RectTransform parent, string text, Vector2 pos, UnityEngine.Events.UnityAction click)
        {
            var bg = Img(parent, "Btn_" + text, new Vector2(0.5f, 0.5f), pos, new Vector2(460, 76), new Color(0.06f, 0.1f, 0.13f, 0.92f));
            bg.sprite = UISkin.Get(5);
            bg.type = Image.Type.Sliced;
            var btn = bg.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(click);
            var t = Label(bg.rectTransform, text, 26, TextAnchor.MiddleCenter, new Color(0.95f, 0.92f, 0.82f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440, 70));
            t.raycastTarget = false;
        }

        // 返回按钮文字（特效风格按钮需要动态改文案）
        Text MenuButtonText(RectTransform parent, string text, Vector2 pos, UnityEngine.Events.UnityAction click)
        {
            var bg = Img(parent, "Btn_" + text, new Vector2(0.5f, 0.5f), pos, new Vector2(460, 76), new Color(0.06f, 0.1f, 0.13f, 0.92f));
            bg.sprite = UISkin.Get(5);
            bg.type = Image.Type.Sliced;
            var btn = bg.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(click);
            var t = Label(bg.rectTransform, text, 26, TextAnchor.MiddleCenter, new Color(0.95f, 0.92f, 0.82f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440, 70));
            t.raycastTarget = false;
            return t;
        }

        Image Img(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 sz, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = sz;
            var img = go.GetComponent<Image>();
            img.color = c;
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
            var sh = go.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.85f); sh.effectDistance = new Vector2(2, -2);
            return t;
        }

        class Spin : MonoBehaviour
        {
            public float speed;
            void Update() { transform.Rotate(0f, 0f, speed * Time.deltaTime); }
        }
    }
}

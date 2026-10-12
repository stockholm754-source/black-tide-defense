using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlackTide
{
    // DS-017 节点价值标签（世界内 billboard）：只给裂隙 / 信使 / 黑潮之心立牌，
    // 把「代价与收益」写在决策点上。战斗时淡出至 30% 防挡视野。
    public class NodeLabels : MonoBehaviour
    {
        class L { public NodeRuntime node; public string text, icon; public GameObject panel; public CanvasGroup cg; }
        readonly List<L> labels = new List<L>();
        Canvas canvas;

        public void Build(List<NodeRuntime> nodes)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvas.sortingOrder = 12;
            var crt = canvas.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(320f, 64f);
            transform.localScale = Vector3.one * 0.0032f;

            foreach (var n in nodes)
            {
                string text = null, icon = null;
                // 数字跟着 RunDirector.PurifyRelief 走，避免改了数值忘了改标签
                if (n.def.type == NodeType.Rift) { text = $"净化 −{(RunDirector.I ? RunDirector.I.PurifyRelief : 8)}% · 团队技能+2"; icon = "rift"; }
                else if (n.def.type == NodeType.Messenger) { text = "强化声骸（可打断咏唱）"; icon = "messenger"; }
                else if (n.def.type == NodeType.Heart) { text = "决战"; icon = "heart"; }
                else continue;

                var panel = new GameObject("Label_" + n.def.id, typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
                panel.transform.SetParent(transform, false);
                var rt = panel.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(300f, 56f);
                panel.GetComponent<Image>().color = new Color(0.02f, 0.05f, 0.07f, 0.85f);
                panel.GetComponent<Image>().raycastTarget = false;

                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(panel.transform, false);
                var irt = iconGo.GetComponent<RectTransform>();
                irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
                irt.anchoredPosition = new Vector2(26f, 0f); irt.sizeDelta = new Vector2(36f, 36f);
                var iimg = iconGo.GetComponent<Image>();
                iimg.raycastTarget = false; iimg.preserveAspect = true;
                var tex = Resources.Load<Texture2D>("UI/NodeIcons/" + icon);
                if (tex) iimg.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);

                var tgo = new GameObject("Text", typeof(RectTransform), typeof(Text));
                tgo.transform.SetParent(panel.transform, false);
                var trt = tgo.GetComponent<RectTransform>();
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(52f, 6f); trt.offsetMax = new Vector2(-10f, -6f);
                var t = tgo.GetComponent<Text>();
                t.font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" }, 24);
                t.fontSize = 22; t.alignment = TextAnchor.MiddleLeft; t.color = new Color(0.95f, 0.9f, 0.8f);
                t.text = text; t.raycastTarget = false;
                var sh = tgo.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.85f); sh.effectDistance = new Vector2(1.5f, -1.5f);

                labels.Add(new L { node = n, text = text, icon = icon, panel = panel, cg = panel.GetComponent<CanvasGroup>() });
            }
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (!cam) return;
            transform.rotation = cam.transform.rotation;
            var rd = RunDirector.I;
            var player = Combat.player;
            foreach (var l in labels)
            {
                bool show = l.node != null;
                if (l.node.def.type == NodeType.Heart) show = rd && rd.heartOpen && !rd.ended;
                else if (rd && l.node.Done) show = false;
                if (!show) { l.panel.SetActive(false); continue; }

                Vector3 p = l.node.pos + Vector3.up * 2.6f;
                l.panel.transform.position = p;
                float d = player ? Vector3.Distance(Flat(p), Flat(player.transform.position)) : 99f;
                bool near = d < 26f;
                l.panel.SetActive(near);
                if (near)
                {
                    bool inCombat = false;
                    foreach (var e in Combat.enemies)
                        if (e && !e.dead && Vector3.Distance(Flat(e.transform.position), Flat(player ? player.transform.position : p)) < 9f) { inCombat = true; break; }
                    l.cg.alpha = inCombat ? 0.3f : 1f;
                }
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}

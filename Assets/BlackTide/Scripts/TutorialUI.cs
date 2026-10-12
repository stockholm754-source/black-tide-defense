using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace BlackTide
{
    // DS-011 引导教学（用户要求 2026-10-10）：H 打开 / 关闭教学面板（游戏继续、角色站立，同商店/选卡规则）。
    // 五节：基础操作 / 战斗要点 / 技能在哪 / 怎么升级 / 当前技能（动态刷新）。
    public class TutorialUI : MonoBehaviour
    {
        public static TutorialUI I;
        public static bool IsOpen => I && I.panel && I.panel.activeSelf;

        Font font;
        GameObject panel;
        Text body, closeHint;
        ScrollRect reader;
        float nextRefresh;
        int toastStep;                      // DS-017：开局三句话（教规则语言，不教路线）

        public void Build()
        {
            I = this;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" }, 24);
            var cgo = new GameObject("TutorialCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 25;
            var sc = cgo.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            var root = cgo.GetComponent<RectTransform>();

            var bg = Box(root, "TutorialPanel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1460, 900), new Color(0.015f, 0.02f, 0.03f, 0.97f));
            panel = bg.gameObject;
            var prt = bg.rectTransform;
            Label(prt, "教学 · 操作与系统说明", 34, TextAnchor.MiddleLeft, new Color(1f, 0.86f, 0.55f), new Vector2(0.5f, 1f), new Vector2(-670, -38), new Vector2(1300, 48), new Vector2(0, 0.5f));
            closeHint=Label(prt, "", 18, TextAnchor.MiddleRight, new Color(0.6f, 0.7f, 0.78f), new Vector2(0.5f, 1f), new Vector2(670, -44), new Vector2(650, 30), new Vector2(1, 0.5f));
            PadHints.Create(prt, "RS:阅读|B:返回", new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(520f, -80f), 15, 22);   // DS-020：手柄图标行
            body = Label(prt, "", 19, TextAnchor.UpperLeft, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0, -42), new Vector2(1340, 780), new Vector2(0.5f, 1f));
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;
            reader=GamepadUI.MakeTextReader(panel,body,()=>IsOpen);
            reader.viewport.offsetMin=new Vector2(60f,100f);
            AudioControls.Create(prt,font,new Vector2(.5f,0),new Vector2(0,48),false,()=>IsOpen);
            panel.SetActive(false);
        }

        void Update()
        {
            closeHint.text=GameInput.Hint("H / Esc 关闭 · 滚轮阅读","B 返回 · 摇杆阅读");
            if (GameInput.Down(GameAction.Guide) && !ShopUI.IsOpen && !RogueUI.IsOpen && !ConsoleUI.IsOpen)
            {
                GameInput.BlockCombatFrame();
                panel.SetActive(!panel.activeSelf);
                if (panel.activeSelf) { Refresh(); nextRefresh = 0f; }
            }
            if (panel.activeSelf)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || GameInput.PadDown(PadButton.B)) { GameInput.BlockCombatFrame(); panel.SetActive(false); }
                if (Time.time >= nextRefresh) { nextRefresh = Time.time + 0.5f; Refresh(); }   // 当前技能节动态刷新
            }
            // DS-017 开局三句话：教一局的规则语言（时间经济），不给任务
            if (toastStep < 3 && Time.timeSinceLevelLoad > 2f + toastStep * 2.4f && Combat.player)
            {
                var msgs = new[]
                {
                    "侵蚀会一直涨——满 60 秒后强制决战",
                    "净化裂隙是争取时间的唯一手段",
                    "变强 = 杀敌升级 + 三选一构筑 + 换武器 / 声骸 · 按 " + GameInput.Hint("H","Menu") + " 看说明",
                };
                Combat.Toast(msgs[toastStep], new Color(0.85f, 0.88f, 0.95f));
                toastStep++;
            }
        }

        void Refresh() { body.text = BuildText(); GamepadUI.RefreshReader(reader,body); }

        // 一~四节静态说明（主菜单教学面板与局内面板共用）
        public static string GuideSections()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<size=22><color=#ffd98a>一、基础操作</color></size>");
            if(!GameInput.UsingPad)
            {
            sb.AppendLine("WASD 移动 · 按住 <b>Ctrl</b> 慢走 · <b>左键</b> 普攻（连点/长按连段）· <b>空格 / 右键</b> 闪避（闪避开始 0.24 秒内躲过攻击 = 完美闪避：回能量、有额外收益）");
            sb.AppendLine("<b>Tab</b> 切换操控角色（另两名队友 AI 代打）· <b>1 / 2</b> 协奏接力 · <b>F</b> 拾取 / 救援倒地队友 · <b>F2</b> 重开本局");
            sb.AppendLine("<b>X / 滚轮</b> 切换武器栏 · <b>G</b> 靠近商人开商店 · <b>B</b> 增益三选一 · <b>H</b> 本面板");
            sb.AppendLine("<b>F8</b> 特效风格循环（写实 / 美漫 / <b>霓虹故障</b>，主菜单也可选。原来占 F6，与「切换路线 AI」撞键——按一次同时换路线和换画风，已修正）");
            sb.AppendLine("<b>` / F7</b> 控制台（输入 help 看命令）");
            }
            else
            {
            sb.AppendLine("<b>Xbox 手柄</b>：左摇杆移动（轻推慢走）· X 普攻 · B 闪避 · A 拾取/按住救援/商店 · RB 技能 · RT 大招 · LB 声骸 · Y 武器技能");
            sb.AppendLine("<b>LT 组合</b>：X 换人 · Y 换武器 · LB/RB 接力 · ↑ 净世之器 · Menu 重开；十字键 ←/→ 选道具、↑ 使用、↓ 团队道具；LS 选卡 · View 地图 · Menu 教学；界面摇杆/十字键导航、A 确认、B 返回");
            }
            sb.AppendLine("攻击自动朝最近一圈中血量最少的敌人追近，每次起手重新选择；方向不跟随鼠标，闪避可中断追击。");
            sb.AppendLine();
            sb.AppendLine("<size=22><color=#ffd98a>二、战斗要点</color></size>");
            sb.AppendLine("· 敌人血条下的<b>橙色条是架势</b>：打满触发失衡（倒地，受伤更高），重击更容易打满；");
            sb.AppendLine("· <b>红色区域</b>是敌人攻击预警：走出去或闪避；");
            sb.AppendLine("· 自己倒地后 15 秒内队友按 F 救援，超时复活会<b>掉 1 级</b>；");
            sb.AppendLine("· Boss 读条咏唱时，用<b>信使声骸（R）</b>可以打断。");
            sb.AppendLine();
            sb.AppendLine("<size=22><color=#ffd98a>三、技能在哪（HUD）</color></size>");
            sb.AppendLine("· 左下：血条、<b>能量条（满 = Q 大招）</b>、技能框；右下：队伍栏与队友状态；右上：侵蚀 / 超频压力；");
            sb.AppendLine("· <b>E</b> 技能 · <b>V</b> 武器技能（由手持武器决定，换武器就换招）· <b>R</b> 声骸（打赢谁就能用谁的招）· <b>Q</b> 共鸣解放（能量满）；");
            sb.AppendLine("· 武器星级决定伤害倍率；3★ 起带<b>词条</b>，三个武器栏的词条同时生效（商店/构筑面板可看）；");
            sb.AppendLine("· 团队击杀积分攒到门槛 → <b>B</b> 三选一；右上角有黄色「B 选择增益」提示。");
            sb.AppendLine("· <b>局势仪表盘</b>（右上）：侵蚀 % + 到满预估 + 满后 60 秒强制决战倒计时；节点头顶标签写代价收益——裂隙=净化 −8%+团队技能、信使=强化声骸、黑潮之心=决战，营地=武器、巢穴=钥匙、据点=情报。");
            sb.AppendLine("· <b>黑潮涌浪</b>：侵蚀越高，潮里持续涌向你的敌人越多。这不是惩罚——<b>杀得多，经验、共鸣能量和增益选择来得越快</b>，构筑就是这么滚起来的。");
            sb.AppendLine();
            sb.AppendLine("<size=22><color=#ffd98a>四、怎么升级</color></size>");
            sb.AppendLine("· <b>只有击杀给经验</b>：轻波 1×、巨浪 5×、怒涛 30×、信使 70×（升级需求逐级递增）；");
            sb.AppendLine("· <b>2 级解锁声骸（R）、3 级解锁大招（Q）、5 级解锁团队技能（接力）</b>——升级时屏幕有提示；");
            sb.AppendLine("· 三选一（B）：选新卡组构筑，选已有卡 = 升级（最高 3 级）；同流派攒 <b>2 / 5 层触发流派共鸣</b>，武器词条也按流派算层数。");
            sb.AppendLine();
            return PadGuideText(sb.ToString());
        }

        string BuildText()
        {
            var sb = new StringBuilder(GuideSections());

            // ---- 五、当前技能（动态） ----
            sb.AppendLine("<size=22><color=#ffd98a>五、当前技能（实时）</color></size>");
            var h = Combat.player;
            if (!h)
            {
                sb.AppendLine("（等待角色生成…）");
                return PadGuideText(sb.ToString());
            }
            sb.AppendLine($"操控：<b>{h.HeroName} · {h.Role}</b>　Lv.{h.EffLevel}" + (HeroBase.Autopilot ? "　<color=#9ad0ff>自动代打中（F1 关闭）</color>" : ""));
            sb.AppendLine($"E 技能：{h.SkillName}（冷却 {h.SkillCd:0}s）　Q 大招：{h.UltName}（能量满可放）");
            var w = h.kit.Active;
            if (w != null)
            {
                var sd = w.SkillDef;
                sb.AppendLine($"V 武器技能：{w.Name}「{sd.name}」（冷却 {sd.cd:0}s）{sd.desc}");
            }
            else sb.AppendLine("V 武器技能：（未装备武器）");
            if (h.echo != null) sb.AppendLine($"R 声骸：<color=#7ee8b0>{h.echo.name}</color>（冷却 {h.echo.cd:0}s）— {h.echo.desc}" + (h.echoEmpowered ? "　<color=#ffb3f0>[信使强化]</color>" : ""));
            else sb.AppendLine("R 声骸：未装备（通用爆发，近身 3.6 米）");
            var aff = WeaponAffix.Describe(h.kit);
            sb.AppendLine("武器词条：" + (aff.Length > 0 ? aff : "（当前武器无词条）"));
            var names = new StringBuilder();
            foreach (var b in h.rogue.owned) { if (names.Length > 0) names.Append("、"); names.Append(b.name + (h.rogue.Lv(b.id) > 1 ? $" Lv{h.rogue.Lv(b.id)}" : "")); }
            sb.AppendLine("已选增益：" + (names.Length > 0 ? names.ToString() : "（暂无，攒击杀积分按 B 选择）"));
            var sch = new StringBuilder();
            foreach (School s in System.Enum.GetValues(typeof(School)))
            {
                int p = h.rogue.SchoolPts(s);
                if (p <= 0) continue;
                if (sch.Length > 0) sch.Append("　");
                sch.Append($"<color=#{ColorUtility.ToHtmlStringRGB(Rogue.SchoolCol[(int)s])}>{s} {p} 层</color>");
            }
            sb.AppendLine("流派层数：" + (sch.Length > 0 ? sch.ToString() : "（尚无）") + "　（同流派 2 / 5 层触发共鸣）");
            return PadGuideText(sb.ToString());
        }

        static string PadGuideText(string text)
        {
            if(!GameInput.UsingPad) return text;
            return GameInput.ControlText(text).Replace("<b>E</b>","<b>RB</b>").Replace("<b>Q</b>","<b>RT</b>").Replace("<b>R</b>","<b>LB</b>").Replace("<b>V</b>","<b>Y</b>").Replace("<b>B</b>","<b>LS 按下</b>")
                .Replace("E 技能", "RB 技能").Replace("Q 大招", "RT 大招").Replace("V 武器技能", "Y 武器技能").Replace("R 声骸", "LB 声骸")
                .Replace("按 F 救援", "按住 A 救援").Replace("（R）", "（LB）").Replace("（Q）", "（RT）").Replace("三选一（B）", "三选一（LS 按下）").Replace("「B 选择增益」", "「LS 按下选择增益」").Replace("按 B 选择", "按下 LS 选择").Replace("满 = Q 大招", "满 = RT 大招");
        }

        // ---- 小助手 ----
        Image Box(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 sz, Color c)
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
        Text Label(Transform parent, string s, int size, TextAnchor a, Color c, Vector2 anchor, Vector2 pos, Vector2 sz, Vector2 pivot)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = sz;
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = a; t.color = c; t.text = s; t.supportRichText = true;
            t.raycastTarget = false;
            var sh = go.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.8f); sh.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }
    }
}

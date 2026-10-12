using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlackTide
{
    // 伤害跳字（2026-10-09 重做，战斗反馈的一环）。原来：5 种颜色 + 字号，每一下单独弹、随机偏移，多段攻击糊成一片，看不出伤害来源。现在按「层」：
    //   · 我的普通命中：同一目标 0.35 秒内合并成一个持续增长的数字，后面带「×N」段数；跟着目标走；
    //   · 暴击 / 破绽（打失衡的敌人）/ 技能 / 大招 / 声骸 / 武器技能：各自独立弹出，字号和颜色按分量递增；
    //   · 持续伤害（灼烧、闪电、潮爆、利剑）：小号、按来源上色、不弹跳，往侧边飘；
    //   · 队友的伤害、治疗：更小更淡，同样合并；
    //   · 受到伤害：红色（重击更大），护盾吸收的部分另起蓝色；治疗绿色；
    //   · 状态字：失衡、打断、格挡、完美闪避……在目标头上弹出；
    //   · 连击：右侧「连击 ×N」，2 秒不打断就累计。
    //   同一目标的数字按槽位向上叠，不随机乱飞；同屏超过 45 个时最旧的普通数字先淡出。
    //   数字用第八批的精灵字体（SpriteNumber，九套：普通 / 暴击 / 受伤 / 治疗 / 黑潮 / 灼烧 / 闪电 / 冰霜 / 护盾）；
    //   没有专用字体的层用普通那套白字染色。汉字（状态字、「破绽」、段数）还是系统字体的小标签
    public enum DT { Normal, Crit, Broken, Skill, Ult, Echo, WSkill, Burn, Shock, Burst, Heal, Shield, Taken, Ally, AllyHeal, AllyTaken, Status }

    public class DamageText : MonoBehaviour
    {
        public static DamageText I;
        RectTransform root; Canvas canvas; Font font;

        class Item { public RectTransform rt; public SpriteNumber num; public Text label; }
        class N
        {
            public Item it; public DT kind; public Damageable target; public Vector3 w;
            public float t0, life, value, lastAdd, pop; public int hits, slot;
            public Vector2 drift; public Color col; public int size; public string prefix = "";
        }
        readonly List<N> live = new List<N>();
        readonly Stack<Item> pool = new Stack<Item>();
        Text combo; int comboN; float comboLast = -9f, comboPop;

        public void Init(RectTransform r, Canvas c, Font f)
        {
            I = this; root = r; canvas = c; font = f;
            combo = MakeText(root);
            combo.alignment = TextAnchor.MiddleRight;
            combo.rectTransform.anchorMin = combo.rectTransform.anchorMax = new Vector2(1f, 0.62f);
            combo.rectTransform.pivot = new Vector2(1f, 0.5f);
            combo.rectTransform.anchoredPosition = new Vector2(-60f, 0f);
            combo.rectTransform.sizeDelta = new Vector2(500f, 90f);
            combo.gameObject.SetActive(false);
        }

        Text MakeText(Transform parent)
        {
            var go = new GameObject("DT", typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(420f, 90f);
            var t = go.GetComponent<Text>();
            t.font = font; t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false; t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            var o = go.AddComponent<Outline>(); o.effectColor = new Color(0.02f, 0.03f, 0.06f, 0.95f); o.effectDistance = new Vector2(2f, -2f);
            var s = go.AddComponent<Shadow>(); s.effectColor = new Color(0f, 0f, 0f, 0.6f); s.effectDistance = new Vector2(3f, -4f);
            return t;
        }

        Item MakeItem()
        {
            var go = new GameObject("DT", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(root, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            var ng = new GameObject("Num", typeof(RectTransform), typeof(CanvasRenderer), typeof(SpriteNumber));
            ng.transform.SetParent(rt, false);
            var num = ng.GetComponent<SpriteNumber>();
            num.raycastTarget = false;
            var sh = ng.AddComponent<Shadow>(); sh.effectColor = new Color(0f, 0f, 0f, 0.55f); sh.effectDistance = new Vector2(3f, -4f);
            return new Item { rt = rt, num = num, label = MakeText(rt) };
        }

        // ---------------- 层的参数 ----------------
        static Color Col(DT k)
        {
            switch (k)
            {
                case DT.Crit: return new Color(1f, 0.84f, 0.28f);
                case DT.Broken: return new Color(1f, 0.6f, 0.26f);
                case DT.Skill: return new Color(0.95f, 0.97f, 1f);
                case DT.Ult: return new Color(1f, 0.7f, 0.28f);
                case DT.Echo: return new Color(0.45f, 1f, 0.95f);
                case DT.WSkill: return new Color(0.72f, 0.88f, 1f);
                case DT.Burn: return new Color(1f, 0.55f, 0.2f);
                case DT.Shock: return new Color(0.8f, 0.66f, 1f);
                case DT.Burst: return new Color(0.78f, 0.52f, 1f);
                case DT.Heal: case DT.AllyHeal: return new Color(0.5f, 1f, 0.62f);
                case DT.Shield: return new Color(0.6f, 0.8f, 1f);
                case DT.Taken: case DT.AllyTaken: return new Color(1f, 0.3f, 0.28f);
                default: return new Color(1f, 0.97f, 0.92f);
            }
        }
        // 用哪一套精灵字体；返回 0（普通白字）的层靠 Col 染色，其余字体自带颜色
        static int FontSet(DT k)
        {
            switch (k)
            {
                case DT.Crit: case DT.Ult: return 1;
                case DT.Taken: case DT.AllyTaken: return 2;
                case DT.Heal: case DT.AllyHeal: return 3;
                case DT.Burst: return 4;
                case DT.Burn: return 5;
                case DT.Shock: return 6;
                case DT.Shield: return 8;
                default: return 0;
            }
        }
        static int Size(DT k)
        {
            switch (k)
            {
                case DT.Crit: return 50; case DT.Broken: return 42; case DT.Skill: return 44; case DT.Ult: return 70;
                case DT.Echo: return 46; case DT.WSkill: return 46; case DT.Burn: case DT.Shock: return 28; case DT.Burst: return 32;   // 潮爆那套字颜色深，给大一号
                case DT.Heal: return 32; case DT.Shield: return 28; case DT.Taken: return 36;
                case DT.Ally: return 22; case DT.AllyHeal: return 20; case DT.AllyTaken: return 20; case DT.Status: return 38;
                default: return 34;
            }
        }
        static float Life(DT k) => k == DT.Ult ? 1.4f : k == DT.Crit || k == DT.Status ? 1.05f : k == DT.Burn || k == DT.Shock || k == DT.Burst ? 0.6f : k == DT.Ally || k == DT.AllyHeal || k == DT.AllyTaken ? 0.7f : 0.9f;
        static bool Mergeable(DT k) => k == DT.Normal || k == DT.Ally || k == DT.Burn || k == DT.Heal || k == DT.AllyHeal || k == DT.Shield;
        static bool Dot(DT k) => k == DT.Burn || k == DT.Shock || k == DT.Burst;

        // HeroBase.DealHit 的 kind → 层
        public static DT FromHit(string kind, bool crit, bool broken, bool player)
        {
            DT k;
            switch (kind)
            {
                case "ult": case "team": k = DT.Ult; break;
                case "skill": case "vortex": case "anchor": k = DT.Skill; break;
                case "echo": case "afterimage": k = DT.Echo; break;
                case "wskill": k = DT.WSkill; break;
                case "burn": return player ? DT.Burn : DT.Ally;
                case "chain": case "thunder": case "static": case "sword": return player ? DT.Shock : DT.Ally;
                case "burst": return player ? DT.Burst : DT.Ally;
                default: k = crit ? DT.Crit : broken ? DT.Broken : DT.Normal; break;
            }
            if (!player) return DT.Ally;
            if (k != DT.Ult && crit) return DT.Crit;
            return k;
        }

        // ---------------- 发出 ----------------
        public static void Hit(Damageable target, Vector3 w, float v, DT kind, Color? tint = null)
        {
            if (!I || v <= 0f) return;
            I.Add(target, w, v, kind, tint);
            if (kind != DT.Ally && kind != DT.Taken && kind != DT.AllyTaken && !Dot(kind)) I.Combo();
        }
        public static void Heal(Vector3 w, float v, bool player) { if (I && v > 0f) I.Add(null, w, v, player ? DT.Heal : DT.AllyHeal, null); }
        public static void Taken(Vector3 w, float v, bool heavy, bool player)
        {
            if (!I || v <= 0f) return;
            var n = I.Add(null, w, v, player ? DT.Taken : DT.AllyTaken, null);
            if (n != null && heavy && player) { n.size = 46; n.pop = 2f; I.Render(n); }
        }
        public static void Status(Vector3 w, string s, Color c, int size = 38)
        {
            if (!I) return;
            var n = I.Spawn(null, w, DT.Status);
            n.col = c; n.size = size; n.prefix = s; n.value = -1f;
            I.Render(n);
        }

        // 割草方向重做：飘字总量上限。原有的「同目标合并」在怪潮里不够用——40 只怪就是 40 个数字，
        // 每个都只合并自己那一只。超过上限后改成「空间合并」：4.5 米内的同类伤害折进同一个数字，
        // 总和不变，只是不再各飘一个。上限留给跑局窗口按观感调。
        public static int TextCap = 12;
        const float MergeRadius = 4.5f;

        N Add(Damageable target, Vector3 w, float v, DT kind, Color? tint)
        {
            float now = Feel.Clock;
            if (Mergeable(kind))
            {
                foreach (var n in live)
                {
                    if (n.kind != kind || now - n.lastAdd > 0.35f) continue;
                    if (target ? n.target != target : Vector3.Distance(n.w, w) > 1.5f) continue;
                    n.value += v; n.hits++; n.lastAdd = now;
                    n.t0 = Mathf.Max(n.t0, now - 0.15f);                  // 合并时续命，不从头飘
                    n.pop = Mathf.Max(n.pop, kind == DT.Normal ? 1.35f : 1.15f);
                    Render(n);
                    return n;
                }
                if (live.Count >= TextCap)
                {
                    for (int i = live.Count - 1; i >= 0; i--)
                    {
                        var n = live[i];
                        if (!Mergeable(n.kind) || now - n.lastAdd > 0.5f) continue;
                        if (Vector3.Distance(n.w, w) > MergeRadius) continue;
                        n.value += v; n.hits++; n.lastAdd = now;
                        n.t0 = Mathf.Max(n.t0, now - 0.15f);
                        n.pop = Mathf.Max(n.pop, kind == DT.Normal ? 1.35f : 1.15f);
                        Render(n);
                        return n;
                    }
                }
            }
            var m = Spawn(target, w, kind);
            m.value = v; m.hits = 1;
            if (tint.HasValue) m.col = tint.Value;
            Render(m);
            return m;
        }

        N Spawn(Damageable target, Vector3 w, DT kind)
        {
            var it = pool.Count > 0 ? pool.Pop() : MakeItem();
            it.rt.gameObject.SetActive(true);
            it.rt.SetAsLastSibling();
            int slot = 0;
            foreach (var n in live) if (n.target == target && target) slot++;
            float side = (slot % 2 == 0 ? 1f : -1f) * Mathf.Min(slot, 3) * 18f;
            var nn = new N
            {
                it = it, kind = kind, target = target, w = w, t0 = Feel.Clock, lastAdd = Feel.Clock, life = Life(kind), slot = slot % 4,
                col = FontSet(kind) == 0 || kind == DT.Status ? Col(kind) : Color.white, size = Size(kind),
                pop = kind == DT.Ult ? 2.6f : kind == DT.Crit ? 2.2f : kind == DT.Status ? 1.8f : Dot(kind) ? 1f : 1.6f,
                drift = Dot(kind) ? new Vector2(Random.Range(25f, 45f) * (Random.value < 0.5f ? 1f : -1f), 30f) : new Vector2(side * 0.6f, kind == DT.Crit ? 40f : kind == DT.Ult ? 30f : 60f),
            };
            // 范围招同时打中一群：只有第一个保持大号，其余缩小，不再一排大字互相盖住
            if (kind == DT.Ult || kind == DT.Skill || kind == DT.Echo || kind == DT.WSkill || kind == DT.Crit)
            {
                int same = 0; foreach (var o in live) if (o.kind == kind && Feel.Clock - o.t0 < 0.12f) same++;
                if (same > 0) { nn.size = Mathf.RoundToInt(nn.size * 0.7f); nn.pop = 1.3f; }
            }
            if (kind == DT.Taken || kind == DT.AllyTaken) nn.drift = new Vector2(Random.Range(-30f, -15f), 45f);
            if (kind == DT.Heal || kind == DT.AllyHeal) nn.drift = new Vector2(Random.Range(10f, 25f), 50f);
            live.Add(nn);
            // 同屏太多：最旧的普通 / 队友数字先淡出
            if (live.Count > 45)
                foreach (var o in live)
                    if ((o.kind == DT.Normal || o.kind == DT.Ally || Dot(o.kind)) && o != nn) { o.life = Mathf.Min(o.life, Feel.Clock - o.t0 + 0.12f); break; }
            return nn;
        }

        void Render(N n)
        {
            var num = n.it.num; var label = n.it.label;
            if (n.kind == DT.Status)
            {
                num.gameObject.SetActive(false);
                label.gameObject.SetActive(true);
                label.fontSize = n.size; label.text = n.prefix;
                label.rectTransform.anchoredPosition = Vector2.zero;
                return;
            }
            string s = Mathf.RoundToInt(n.value).ToString();
            switch (n.kind)
            {
                case DT.Crit: s += "!"; break;
                case DT.Heal: case DT.AllyHeal: s = "+" + s; break;
                case DT.Taken: case DT.AllyTaken: s = "-" + s; break;
            }
            num.gameObject.SetActive(true);
            float h = n.size * 1.25f;   // 精灵字的格子带着描边和辉光，比系统字的字号要给大一些
            num.Set(s, FontSet(n.kind), h);
            // 小标签：「破绽」在数字上方，合并段数「×N」在数字右下
            bool hits = n.hits >= 3 && (n.kind == DT.Normal || n.kind == DT.Ally);
            label.gameObject.SetActive(n.kind == DT.Broken || hits);
            if (n.kind == DT.Broken) { label.text = "破绽"; label.fontSize = Mathf.Max(16, n.size * 6 / 10); label.rectTransform.anchoredPosition = new Vector2(0f, h * 0.7f); }
            else if (hits) { label.text = "×" + n.hits; label.fontSize = Mathf.Max(14, n.size / 2); label.rectTransform.anchoredPosition = new Vector2(num.Width * 0.5f + n.size * 0.45f, -h * 0.18f); }
        }

        void Combo()
        {
            float now = Feel.Clock;
            comboN = now - comboLast > 2f ? 1 : comboN + 1;
            comboLast = now;
            // 割草方向重做：连击行同时报「收割」——几百只怪的意义是刷掉的数量，不是命中次数。
            if (comboN >= 5)
            {
                combo.gameObject.SetActive(true);
                comboPop = 1.35f;
                int sw = Coop.killStreak > 8 ? Coop.killStreak : 0;
                combo.text = sw > 0
                    ? $"<size=26>连击</size> <b>×{comboN}</b><size=26>  收割</size> <b>{sw}</b>"
                    : $"<size=26>连击</size> <b>×{comboN}</b>";
            }
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            float now = Feel.Clock;
            var uiCam = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var n = live[i];
                float age = now - n.t0;
                if (age > n.life || !cam) { n.it.rt.gameObject.SetActive(false); pool.Push(n.it); live.RemoveAt(i); continue; }
                // 跟着目标走（目标还在时）
                if (n.target && !n.target.dead) n.w = n.target.transform.position + Vector3.up * (Mathf.Min(1.6f, n.target.height * 0.45f) + 0.9f);
                Vector3 sp = cam.WorldToScreenPoint(n.w);
                bool front = sp.z >= 0f;
                n.it.num.enabled = front; n.it.label.enabled = front;
                if (!front) continue;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, uiCam, out var local);
                float rise = 1f - Mathf.Pow(1f - Mathf.Clamp01(age / n.life), 2f);   // 先快后慢
                Vector2 off = new Vector2(n.drift.x * rise, n.drift.y * rise + n.slot * 30f);
                if (n.kind == DT.Crit && age < 0.15f) off += Random.insideUnitCircle * 6f * (1f - age / 0.15f);   // 暴击抖一下
                n.it.rt.anchoredPosition = local + off;
                n.pop = Mathf.MoveTowards(n.pop, 1f, Time.unscaledDeltaTime * 9f);
                n.it.rt.localScale = Vector3.one * n.pop;
                var c = n.col; c.a = Mathf.Clamp01((n.life - age) / 0.3f);
                bool ally = n.kind == DT.Ally || n.kind == DT.AllyHeal || n.kind == DT.AllyTaken;
                n.it.num.color = ally ? new Color(c.r, c.g, c.b, c.a * 0.8f) : c;
                var lc = n.kind == DT.Status || n.kind == DT.Broken ? (n.kind == DT.Broken ? Col(DT.Broken) : n.col) : new Color(1f, 0.97f, 0.9f);
                lc.a = c.a; n.it.label.color = lc;
            }
            // 连击
            if (combo && combo.gameObject.activeSelf)
            {
                float since = now - comboLast;
                if (since > 2f) combo.gameObject.SetActive(false);
                else
                {
                    comboPop = Mathf.MoveTowards(comboPop, 1f, Time.unscaledDeltaTime * 4f);
                    combo.transform.localScale = Vector3.one * comboPop;
                    var cc = comboN >= 30 ? new Color(1f, 0.5f, 0.25f) : comboN >= 15 ? new Color(1f, 0.78f, 0.3f) : new Color(1f, 0.95f, 0.85f);
                    cc.a = Mathf.Clamp01((2f - since) / 0.5f); combo.color = cc;
                    combo.fontSize = comboN >= 30 ? 54 : comboN >= 15 ? 48 : 42;
                }
            }
        }
    }
}

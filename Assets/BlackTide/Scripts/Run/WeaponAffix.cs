using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 武器特效词条（2026-10-09 构筑打磨的最后一块）。原来武器只有伤害倍率（3★ ×1.15 … 5★ ×1.6），升级就是数字变大。
    // 现在 3★ 起每把武器带一条词条；升级时保留旧词条、加上新武器的词条（3★ 一条、4★ 两条、5★ 三条），不会因为升级丢掉顺手的效果。
    // 词条强度随当前星级（3★ ×1、4★ ×1.4、5★ ×1.8）。每条词条属于一个流派，算 1 层流派共鸣——武器和卡组接在一起。
    // 商店的武器在货架上就显示它带的词条，玩家可以按构筑挑。
    public class AffixDef
    {
        public string id, name;
        public School school;
        public string desc;               // {0} 处填按星级缩放后的数值
        public float baseVal;
        public string Text(int star) => string.Format(desc, Mathf.RoundToInt(baseVal * WeaponAffix.Scale(star) * 10f) / 10f);
    }

    public static class WeaponAffix
    {
        public static readonly AffixDef[] All =
        {
            new AffixDef { id = "burn", name = "灼烧", school = School.潮噬, baseVal = 40f, desc = "命中 15% 概率点燃 3 秒，每秒造成攻击力 {0}% 的伤害" },
            new AffixDef { id = "reaper", name = "收割", school = School.潮噬, baseVal = 20f, desc = "对生命低于 30% 的敌人伤害 +{0}%" },
            new AffixDef { id = "spark", name = "引雷", school = School.雷鸣, baseVal = 8f, desc = "命中 {0}% 概率落雷，并跳到附近 1 个敌人" },
            new AffixDef { id = "sunder", name = "碎甲", school = School.雷鸣, baseVal = 15f, desc = "架势积累 +{0}%" },
            new AffixDef { id = "gale", name = "疾风", school = School.镜步, baseVal = 15f, desc = "闪避后 2 秒内伤害 +{0}%" },
            new AffixDef { id = "parry", name = "格挡", school = School.镜步, baseVal = 8f, desc = "完美闪避时获得 {0}% 最大生命的护盾" },
            new AffixDef { id = "resonant", name = "共振", school = School.协奏, baseVal = 20f, desc = "共鸣技能（E）伤害 +{0}%" },
            new AffixDef { id = "surge", name = "回能", school = School.协奏, baseVal = 3f, desc = "暴击时回复 {0} 点共鸣能量" },
            new AffixDef { id = "leech", name = "吸血", school = School.坚守, baseVal = 2f, desc = "造成伤害的 {0}% 转为生命" },
            new AffixDef { id = "bulwark", name = "坚壁", school = School.坚守, baseVal = 6f, desc = "受到的伤害 −{0}%" },
            // 2026-10-10 扩充（用户定的方向：黑夜君临式被动词条，技能伤害+X%、连续攻击减伤这类）。每流派补到 4 条。
            new AffixDef { id = "opener", name = "破潮", school = School.潮噬, baseVal = 25f, desc = "对生命全满的敌人伤害 +{0}%" },
            new AffixDef { id = "carnage", name = "嗜战", school = School.潮噬, baseVal = 20f, desc = "击杀敌人后 3 秒内伤害 +{0}%" },
            new AffixDef { id = "thundersurge", name = "雷涌", school = School.雷鸣, baseVal = 15f, desc = "引雷触发后 3 秒内伤害 +{0}%" },
            new AffixDef { id = "shockwave", name = "惊雷", school = School.雷鸣, baseVal = 80f, desc = "武器技能命中时 10% 概率召来落雷，造成攻击力 {0}% 的伤害" },
            new AffixDef { id = "phantom", name = "魅影", school = School.镜步, baseVal = 25f, desc = "完美闪避后 3 秒内伤害 +{0}%" },
            new AffixDef { id = "exploit", name = "攻虚", school = School.镜步, baseVal = 25f, desc = "对失衡中的敌人伤害 +{0}%" },
            new AffixDef { id = "skillblade", name = "利刃", school = School.协奏, baseVal = 20f, desc = "武器技能伤害 +{0}%" },
            new AffixDef { id = "aftertone", name = "余韵", school = School.协奏, baseVal = 15f, desc = "武器技能命中后 3 秒内普攻伤害 +{0}%" },
            new AffixDef { id = "ironwill", name = "韧战", school = School.坚守, baseVal = 12f, desc = "连续命中 8 次后，5 秒内受到的伤害 −{0}%" },
            new AffixDef { id = "guardian", name = "回壁", school = School.坚守, baseVal = 15f, desc = "生命低于 35% 时，受到的伤害 −{0}%" },
        };
        public static AffixDef Def(string id) { foreach (var a in All) if (a.id == id) return a; return null; }
        public static float Scale(int star) => star >= 5 ? 1.8f : star == 4 ? 1.4f : 1f;
        public static int Slots(int star) => Mathf.Clamp(star - 2, 0, 3);

        // 随机一条（不和已有的重复）
        public static string Roll(IList<string> have, System.Random rng = null)
        {
            var cand = new List<AffixDef>();
            foreach (var a in All) if (have == null || !have.Contains(a.id)) cand.Add(a);
            if (cand.Count == 0) return null;
            return cand[rng != null ? rng.Next(cand.Count) : Random.Range(0, cand.Count)].id;
        }

        // 2026-10-09 改成三个武器栏：每把武器自带词条（Weapons.Make 时随机），三把的词条都生效、同名不叠加（Kit.affixes / AffixStar）。
        // 流派层数：Kit 每次变化 version +1，这里按版本同步（Codex 审阅：只比数量的话，换成同数量的不同词条会漏同步）
        public static void Sync(HeroBase h)
        {
            var ex = h.rogue.extra; var k = h.kit;
            if (h.rogue.affixVersion == k.version) return;
            h.rogue.affixVersion = k.version;
            ex.Clear();
            foreach (var id in k.affixes) { var d = Def(id); if (d != null) ex.Add(d.school); }
        }

        public static string Describe(Kit k)
        {
            if (k.affixes.Count == 0) return "";
            var parts = new List<string>();
            foreach (var id in k.affixes) { var d = Def(id); if (d != null) parts.Add($"<color=#{ColorUtility.ToHtmlStringRGB(Rogue.SchoolCol[(int)d.school])}>{d.name}</color>（{d.Text(k.AffixStar(id))}）"); }
            return string.Join("  ", parts);
        }

        static float V(HeroBase h, string id) { int s = h.kit.AffixStar(id); return s > 0 ? Def(id).baseVal * Scale(s) : 0f; }

        // ---------------- 结算钩子（HeroBase.DealHit） ----------------
        public static float DmgBonus(HeroBase h, Damageable e, string kind)
        {
            if (h.kit.affixes.Count == 0) return 0f;
            if (kind == "burn" || kind == "chain") return 0f;   // KB-006：词条自身伤害（灼烧跳字、落雷/惊雷链击）不参与词条增伤
            float d = 0f;
            var r = h.rogue;
            if (e && e.hp < e.maxHp * 0.3f) d += V(h, "reaper") / 100f;
            if (Time.time - r.lastDodgeT < 2f) d += V(h, "gale") / 100f;
            if (kind == "skill" || kind == "vortex" || kind == "anchor") d += V(h, "resonant") / 100f;   // KB-006：共振只加成 E（及其刻印形态）；武器技能 V 归「利刃」
            // 2026-10-10 扩充词条
            if (e && e.hp >= e.maxHp * 0.999f) d += V(h, "opener") / 100f;
            if (Time.time < r.carnageUntil) d += V(h, "carnage") / 100f;
            if (Time.time < r.thundersurgeUntil) d += V(h, "thundersurge") / 100f;
            if (Time.time < r.phantomUntil) d += V(h, "phantom") / 100f;
            if (e && e.Broken) d += V(h, "exploit") / 100f;
            if (kind == "wskill") d += V(h, "skillblade") / 100f;
            else if ((kind == "melee" || kind == "shot" || kind == "counter") && Time.time < r.aftertoneUntil)
                d += V(h, "aftertone") / 100f;   // KB-006：余韵只给普攻（近战/远程/反击弹道），不给技能、大招、声骸与卡牌伤害
            return d;
        }
        public static float StaggerBonus(HeroBase h) => V(h, "sunder") / 100f;
        public static float TakenBonus(HeroBase h)
        {
            if (h.kit.affixes.Count == 0) return 0f;
            float t = -V(h, "bulwark") / 100f;
            if (Time.time < h.rogue.ironwillUntil) t -= V(h, "ironwill") / 100f;
            if (h.dmg.hp < h.dmg.maxHp * 0.35f) t -= V(h, "guardian") / 100f;
            return t;
        }

        public static void OnHit(HeroBase h, Damageable e, float dealt, bool crit, string kind)
        {
            if (h.kit.affixes.Count == 0 || !e) return;
            if (kind == "burn" || kind == "chain") return;   // 词条自己造成的伤害不再触发词条
            var r = h.rogue;
            float ls = V(h, "leech");
            if (ls > 0f) h.dmg.hp = Mathf.Min(h.dmg.maxHp, h.dmg.hp + dealt * ls / 100f);
            if (crit) { float s = V(h, "surge"); if (s > 0f) h.Gain(s, 0f); }
            float bn = V(h, "burn");
            if (bn > 0f && !e.dead && Random.value < 0.15f) Ignite(h, e, bn / 100f);
            float sp = V(h, "spark");
            if (sp > 0f && Random.value < sp / 100f)
            {
                var from = e.Center + Vector3.up * 6f;
                Rogue.Bolt(from, e.Center, new Color(1f, 0.9f, 0.5f));
                var tgt = e;
                Rogue.Delay(0.05f, () => { if (h && tgt && !tgt.dead) h.DealHit(tgt, 1.1f, 10f, 0.5f, Vector3.zero, false, "chain"); });   // 主目标先吃一道雷
                Rogue.Chain(h, e.Center, e, 1, 1.1f);   // 再跳到附近 1 个（排除主目标；主目标这一击死了也照样跳）
                if (V(h, "thundersurge") > 0f) r.thundersurgeUntil = Time.time + 3f;
            }
            // 2026-10-10 扩充词条
            if (e.dead && V(h, "carnage") > 0f) r.carnageUntil = Time.time + 3f;
            if (V(h, "ironwill") > 0f)
            {
                r.hitChain = Time.time - r.lastChainHitT < 2f ? r.hitChain + 1 : 1;
                r.lastChainHitT = Time.time;
                if (r.hitChain >= 8) r.ironwillUntil = Time.time + 5f;
            }
            if (kind == "wskill")
            {
                if (V(h, "aftertone") > 0f) r.aftertoneUntil = Time.time + 3f;
                float sw = V(h, "shockwave");
                if (sw > 0f && !e.dead && Random.value < 0.10f)
                {
                    Rogue.Bolt(e.Center + Vector3.up * 6f, e.Center, new Color(1f, 0.9f, 0.5f));
                    var tgt2 = e; float mult = sw / 100f;
                    Rogue.Delay(0.05f, () => { if (h && tgt2 && !tgt2.dead) h.DealHit(tgt2, mult, 10f, 0.5f, Vector3.zero, false, "chain"); });
                }
            }
        }

        public static void OnPerfectDodge(HeroBase h)
        {
            float p = V(h, "parry");
            if (p > 0f) h.Heal(h, h.dmg.maxHp * p / 100f, true, 5f);
            if (V(h, "phantom") > 0f) h.rogue.phantomUntil = Time.time + 3f;
        }

        // ---------------- 灼烧（持续伤害，每 0.5 秒结算一次） ----------------
        class Burn { public HeroBase owner; public float until, next, mult; }
        static readonly Dictionary<Damageable, Burn> burns = new Dictionary<Damageable, Burn>();
        static readonly List<Damageable> tmp = new List<Damageable>();

        public static void Ignite(HeroBase h, Damageable e, float perSec)
        {
            if (burns.TryGetValue(e, out var b)) { b.until = Time.time + 3f; b.owner = h; b.mult = Mathf.Max(b.mult, perSec); return; }
            burns[e] = new Burn { owner = h, until = Time.time + 3f, next = Time.time + 0.5f, mult = perSec };
            FX.Embers(e.Center, 10);
        }

        public static void Reset() => burns.Clear();

        // Rogue.Tick 每帧调
        public static void Tick()
        {
            if (burns.Count == 0) return;
            tmp.Clear(); tmp.AddRange(burns.Keys);
            foreach (var e in tmp)
            {
                var b = burns[e];
                if (!e || e.dead || !b.owner || Time.time > b.until) { burns.Remove(e); continue; }
                if (Random.value < 0.3f) FX.Embers(e.Center + Random.insideUnitSphere * 0.4f, 1);
                if (Time.time < b.next) continue;
                b.next += 0.5f;
                b.owner.DealHit(e, b.mult * 0.5f, 0f, 0f, Vector3.zero, false, "burn", 0f, false);
            }
        }
    }
}

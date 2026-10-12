using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 武器（2026-10-09 用户定的体系，参考黑夜君临 + 鸣潮「技能模组拆开复用」的活动）：
    //   一把武器 = 种类（决定普攻模组，同种类共用）+ 武器技能（V，冷却短，偏出伤）+ 星级（伤害倍率）+ 随机词条（按星级 1–3 条）。
    //   · 7 个种类；任何角色都能装任何种类。原生种类（绯烈长刃、青岚双枪、澄音音感仪）用角色自己的招式；非原生种类用通用模组（伤害 ×0.87）。
    //   · 3 个武器栏，X / 滚轮切换手持；三把的词条都生效，同名词条不叠加（取星级最高的那把）。
    //   · 武器技能：敌人武器复刻该敌人的快伤招；普通武器（第七批 80–93）各有通用技能；
    //     4★ 以上的武器 25% 带「共鸣刻印」——换成某个角色的共鸣技能（断焰 / 旋流 / 锚音），绯烈也能放旋流。
    //   · 来源：精英、Boss 掉自己的武器；营地宝箱、巢穴、裂隙、商人给按星级随机的武器。地上的武器走近显示，按 F 拿起。
    public enum WType { 长刃, 重武器, 长柄, 双刃, 法杖, 双枪, 音感仪 }

    public class WeaponDef
    {
        public string id, name, model, skill;
        public WType type;
        public bool enemy, native;
        public bool comic;   // 这把武器的特效用美漫画法（全游戏只有一把，当作画风彩蛋）
    }

    public class WSkill
    {
        public string id, name, desc, anim;
        public float cd, lockT, speed = 1f;
    }

    public class WeaponItem
    {
        public WeaponDef def;
        public int star;
        public readonly List<string> affixes = new List<string>();
        public string skillOverride;      // 共鸣刻印
        public float skillReadyAt;        // 冷却记在武器上：切换、丢下再捡回都不会重置
        public string Skill => skillOverride ?? def.skill;
        public string Name => $"{star}★ {def.name}";
        public WSkill SkillDef => Weapons.Skill(Skill);
    }

    public static class Weapons
    {
        static WeaponDef W(string id, string name, WType t, string model, string skill, bool enemy = false, bool native = false, bool comic = false) => new WeaponDef { id = id, name = name, type = t, model = model, skill = skill, enemy = enemy, native = native, comic = comic };
        public static readonly WeaponDef[] All =
        {
            // 原生
            W("feilie_blade", "绯烈长刃", WType.长刃, "sword", "slash_wave", native: true),
            W("qinglan_guns", "青岚双枪", WType.双枪, "pistol", "pierce_shot", native: true),
            W("chengyin_tuner", "澄音音感仪", WType.音感仪, "instr", "resonance", native: true),
            // 第七批普通武器
            W("80_rust_greatsword", "锈潮大剑", WType.长刃, "map:80_rust_greatsword", "slash_wave"),
            W("81_molten_blade", "熔火长刃", WType.长刃, "map:81_molten_blade", "flame_slash"),
            W("82_reef_hammer", "礁石巨锤", WType.重武器, "map:82_reef_hammer", "leap_slam", comic: true),
            W("83_mast_axe", "断桅战斧", WType.重武器, "map:83_mast_axe", "whirl"),
            W("84_whaling_harpoon", "捕鲸鱼叉", WType.长柄, "map:84_whaling_harpoon", "pierce_dash"),
            W("85_wave_glaive", "潮纹长戟", WType.长柄, "map:85_wave_glaive", "sweep"),
            W("86_shark_sabre", "鲨齿弯刀", WType.双刃, "map:86_shark_sabre", "shadow_step"),
            W("87_shell_dagger", "贝壳匕首", WType.双刃, "map:87_shell_dagger", "flurry"),
            W("88_coral_staff", "珊瑚法杖", WType.法杖, "map:88_coral_staff", "tide_orbs"),
            W("89_thunder_staff", "雷鸣铜杖", WType.法杖, "map:89_thunder_staff", "thunder_call"),
            W("90_flintlock", "燧发手铳", WType.双枪, "map:90_flintlock", "pierce_shot"),
            W("91_blunderbuss", "鲸骨短铳", WType.双枪, "map:91_blunderbuss", "scatter"),
            W("92_tide_fork", "潮汐音叉", WType.音感仪, "map:92_tide_fork", "resonance"),
            W("93_conch_horn", "海螺号角", WType.音感仪, "map:93_conch_horn", "rally"),
            // 敌人武器（复刻该敌人的快伤招）
            W("coral_javelin", "珊瑚投矛", WType.长柄, "b4:coral_javelin", "javelin_throw", enemy: true),
            W("frost_staff", "霜冻法杖", WType.法杖, "b4:frost_staff", "ice_lances", enemy: true),
            W("anchor_hammer", "船锚战锤", WType.重武器, "b4:anchor_hammer", "leap_slam", enemy: true),
            W("void_sickle", "湮灭镰刀", WType.双刃, "b4:void_sickle", "shadow_step", enemy: true),
            W("tide_axe", "潮汐巨斧", WType.重武器, "axe", "whirl", enemy: true),
            W("messenger_glaive", "信使长戟", WType.长柄, "glaive", "triple_thrust", enemy: true),
        };
        public static WeaponDef Def(string id) { foreach (var d in All) if (d.id == id) return d; return null; }

        static WSkill S(string id, string name, float cd, float lockT, string anim, string desc, float speed = 1f) => new WSkill { id = id, name = name, cd = cd, lockT = lockT, anim = anim, desc = desc, speed = speed };
        public static readonly WSkill[] Skills =
        {
            S("slash_wave", "破空斩", 7f, 0.45f, "2H_Melee_Attack_Slice", "挥出一道剑气（12 米直线，2.5 倍）", 1.4f),
            S("flame_slash", "烈焰斩", 8f, 0.55f, "2H_Melee_Attack_Chop", "重斩前方（4.5 米，3 倍）并点燃 3 秒", 1.3f),
            S("leap_slam", "震地", 9f, 0.7f, "2H_Melee_Attack_Chop", "跳到 8 米内的敌人身边砸地（4.5 米，3.2 倍，大量架势）", 1.2f),
            S("whirl", "旋风", 8f, 0.8f, "2H_Melee_Attack_Spinning", "原地回旋两圈（4 米，每圈 1.6 倍）", 1.2f),
            S("pierce_dash", "贯刺", 7f, 0.45f, "2H_Melee_Attack_Stab", "向前突刺 7 米，路上的敌人 2.8 倍", 1.6f),
            S("sweep", "横扫", 7f, 0.5f, "2H_Melee_Attack_Slice", "大范围半圆横扫（5 米，2.2 倍）", 1.2f),
            S("shadow_step", "影步", 9f, 0.9f, "Dualwield_Melee_Attack_Slice", "闪到 10 米内最近的敌人身后连斩 4 下（每下 0.9 倍）", 1.8f),
            S("flurry", "乱舞", 8f, 0.9f, "Dualwield_Melee_Attack_Chop", "原地快速 6 连击（3 米，每下 0.55 倍）", 2f),
            S("tide_orbs", "潮弹", 7f, 0.45f, "Spellcast_Shoot", "向最近的 3 个敌人各射一发水弹（1.3 倍）", 1.6f),
            S("thunder_call", "落雷", 9f, 0.5f, "Spellcast_Raise", "瞄准点附近连落三道雷（每道 2 米、1.8 倍）", 1.5f),
            S("pierce_shot", "穿甲弹", 6f, 0.4f, "1H_Ranged_Shoot", "一发贯穿重弹（16 米直线，2.8 倍）", 1.6f),
            S("scatter", "散射", 6f, 0.4f, "1H_Ranged_Shoot", "扇形霰弹（8 米、60°，2.6 倍）", 1.6f),
            S("resonance", "共振波", 9f, 0.5f, "Spellcast_Raise", "扩散音波（6 米，1.4 倍），10 米内队友回复 6% 生命", 1.6f),
            S("rally", "号令", 14f, 0.5f, "Spellcast_Raise", "全队 6 秒伤害 +15%", 1.6f),
            S("javelin_throw", "掷矛", 8f, 0.5f, "Throw", "掷出贯穿长矛（18 米直线，3.4 倍）", 1.6f),
            S("ice_lances", "冰枪", 8f, 0.55f, "Spellcast_Shoot", "扇形射出 3 道冰枪（14 米，每道 1.5 倍，减速 3 秒）", 1.5f),
            S("triple_thrust", "三段突刺", 7f, 0.8f, "2H_Melee_Attack_Stab", "向前连刺三下（6 米直线，每下 1.3 倍）", 2f),
            // 共鸣刻印（角色共鸣技能模组）
            S("duanyan", "断焰", 6f, 0.42f, "2H_Melee_Attack_Stab", "【绯烈的共鸣技能】突进斩 7 米，路上的敌人 2.2 倍", 1.8f),
            S("vortex", "旋流", 7f, 0.45f, "Throw", "【青岚的共鸣技能】在瞄准点放出旋流，把杂兵吸到中间", 1.9f),
            S("anchor", "锚音", 9f, 0.5f, "Spellcast_Raise", "【澄音的共鸣技能】在瞄准点立起音锚：回血减伤、定身杂兵", 2.4f),
        };
        public static WSkill Skill(string id) { foreach (var s in Skills) if (s.id == id) return s; return Skills[0]; }
        static readonly string[] Imprints = { "duanyan", "vortex", "anchor" };

        public static WType NativeType(HeroId h) => h == HeroId.Feilie ? WType.长刃 : h == HeroId.Qinglan ? WType.双枪 : WType.音感仪;
        public static WeaponDef NativeDef(HeroId h) => Def(h == HeroId.Feilie ? "feilie_blade" : h == HeroId.Qinglan ? "qinglan_guns" : "chengyin_tuner");
        public static bool Ranged(WType t) => t == WType.法杖 || t == WType.双枪 || t == WType.音感仪;
        public static readonly Color[] StarCol = { Color.white, Color.white, new Color(0.75f, 0.8f, 0.85f), new Color(0.45f, 0.72f, 1f), new Color(0.78f, 0.5f, 1f), new Color(1f, 0.78f, 0.3f) };

        public static WeaponItem Make(WeaponDef d, int star, System.Random rng = null)
        {
            System.Func<double> R = () => rng != null ? rng.NextDouble() : Random.value;
            var w = new WeaponItem { def = d, star = Mathf.Clamp(star, 2, 5) };
            for (int i = 0; i < WeaponAffix.Slots(w.star); i++) { var a = WeaponAffix.Roll(w.affixes, rng); if (a != null) w.affixes.Add(a); }
            if (w.star >= 4 && !d.native && R() < 0.25) w.skillOverride = Imprints[(int)(R() * Imprints.Length) % Imprints.Length];
            return w;
        }

        // 随机一把普通武器（type 为空则任意种类；enemyToo 时也可能是敌人武器）
        public static WeaponDef RandomDef(WType? type, System.Random rng = null, bool enemyToo = false)
        {
            var c = new List<WeaponDef>();
            foreach (var d in All) if (!d.native && (enemyToo || !d.enemy) && (!type.HasValue || d.type == type.Value)) c.Add(d);
            if (c.Count == 0) return All[0];
            return c[rng != null ? rng.Next(c.Count) : Random.Range(0, c.Count)];
        }

        // ---------------- 模型 ----------------
        public static GameObject Model(WeaponDef d, out Texture2D albedo, out Texture2D emission)
        {
            var A = SliceAssets.I; albedo = null; emission = null;
            if (d.model.StartsWith("map:")) { var p = A.MapProp(d.model.Substring(4)); if (p != null) { albedo = p.albedo; emission = p.emission; return p.model; } return null; }
            if (d.model.StartsWith("b4:")) { var p = A.B4(d.model.Substring(3)); if (p != null) { albedo = p.albedo; emission = p.emission; return p.model; } return null; }
            switch (d.model)
            {
                case "sword": albedo = A.swordTripoAlbedo; emission = A.swordTripoEmission; return A.swordTripo;
                case "pistol": albedo = A.pistolAlbedo; return A.pistolTripo;
                case "instr": albedo = A.instrumentAlbedo; return A.instrumentTripo;
                case "axe": albedo = A.axeTripoAlbedo; emission = A.axeTripoEmission; return A.axeTripo;
                case "glaive": albedo = A.glaiveAlbedo; emission = A.glaiveEmission; return A.glaiveTripo;
            }
            return null;
        }
        public static float HoldLength(WType t) => t == WType.长柄 ? 2.1f : t == WType.重武器 ? 1.4f : t == WType.长刃 ? 1.45f : t == WType.法杖 ? 1.6f : t == WType.双刃 ? 0.75f : t == WType.双枪 ? 0.5f : 0.7f;
        public static bool TwoHanded(WType t) => t == WType.双刃 || t == WType.双枪;
        static readonly Dictionary<WeaponDef, Material> mats = new Dictionary<WeaponDef, Material>();
        public static Material Mat(WeaponDef d)
        {
            if (mats.TryGetValue(d, out var m) && m) return m;
            Model(d, out var a, out var e);
            m = ArenaBuilder.Lit(a, Color.white, 0.4f);
            if (e) { m.EnableKeyword("_EMISSION"); m.SetTexture("_EmissionMap", e); m.SetColor("_EmissionColor", Color.white * 2.5f); }
            return mats[d] = m;
        }

        // ---------------- 掉落 ----------------
        // 精英、Boss 掉自己的武器（原稿：Boss 挑战有更高概率掉落好的武器）
        public static void OnKill(Damageable e)
        {
            if (!RunDirector.I || !e) return;
            if (e.tide) return;      // 涌浪不掉武器（同 Echoes：怪潮化后按原掉落率会人均 5★）
            string kind = Echoes.KindOf(e);
            string id = null; float p = 0f; int star = 3;
            switch (kind)
            {
                case "javelin": id = "coral_javelin"; p = 0.15f; star = 3; break;
                case "frost": id = "frost_staff"; p = 0.5f; star = 4; break;
                case "thunder": id = "anchor_hammer"; p = 0.5f; star = 4; break;
                case "reaper": id = "void_sickle"; p = 0.5f; star = 4; break;
                case "knight": id = "tide_axe"; p = 0.4f; star = 4; break;
            }
            if (id == null || Random.value >= p) return;
            Drop(Make(Def(id), star), e.transform.position + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)));
        }

        public static void Drop(WeaponItem w, Vector3 p, bool quiet = false) => WeaponPickup.Spawn(w, p, quiet);

        // ---------------- 拿起 ----------------
        // 有空栏就放进空栏并换到手上；三栏都满就替换手上那把（旧的留在原地）
        public static void Take(HeroBase h, WeaponItem w)
        {
            var old = h.kit.Put(w);
            if (old != null) Drop(old, h.transform.position + h.transform.right * 1.3f, true);
            h.RefreshWeapon();
            if (RunDirector.I) RunDirector.I.Event("weapon_take", "", $"{h.HeroName} {w.def.id} {w.star}");
        }

        // AI：只用自己原生种类的武器（AI 的站位、出招按原生种类写的）；空栏或比最低那把星级高就拿
        public static bool AiWants(HeroBase h, WeaponItem w)
        {
            if (w.def.type != NativeType(h.Id)) return false;
            return h.kit.FreeSlot() >= 0 || w.star > h.kit.LowestStar();
        }
        public static void AiTake(HeroBase h, WeaponItem w)
        {
            var old = h.kit.PutReplaceLowest(w);
            if (old != null && !old.def.native) Drop(old, h.transform.position + h.transform.right * 1.3f, true);
            h.kit.SelectBest();
            h.RefreshWeapon();
            if (RunDirector.I) RunDirector.I.Event("weapon_take", "", $"{h.HeroName} {w.def.id} {w.star}");
        }

        public static string Describe(WeaponItem w)
        {
            var s = w.SkillDef;
            string af = "";
            foreach (var id in w.affixes) { var d = WeaponAffix.Def(id); if (d != null) af += $"  <color=#{ColorUtility.ToHtmlStringRGB(Rogue.SchoolCol[(int)d.school])}>{d.name}</color>（{d.Text(w.star)}）"; }
            return $"{w.def.type} · 技能「{s.name}」：{s.desc}{(w.def.comic ? "\n<color=#ffd24a>异画</color>：这把武器打出来的是另一种画风" : "")}{(af != "" ? "\n词条：" + af : "")}";
        }
    }

    // 地上的武器：模型平放旋转 + 星级色光 + 名字，走近按 F 拿起（有空栏时直接拿）；AI 路过时按规则拿
    public class WeaponPickup : MonoBehaviour
    {
        public WeaponItem w;
        float born, nextHint;
        Transform model;
        static Font font;

        public static void Spawn(WeaponItem w, Vector3 p, bool quiet)
        {
            var go = new GameObject("武器·" + w.def.name);
            go.transform.position = new Vector3(p.x, 0f, p.z);
            var pk = go.AddComponent<WeaponPickup>();
            pk.w = w; pk.born = Time.time + (quiet ? 1.5f : 0.3f);
            Color c = Weapons.StarCol[Mathf.Clamp(w.star, 0, 5)];
            // CL-003：V26 掉落光柱按星级给强度——高星掉在地上要"喊"出来，低星安静一点，
            // 这样怪潮里捡不捡得到不再是运气，而是看得见的取舍。
            if (!quiet) FX.LootColumn(go.transform.position, c);
            var prefab = Weapons.Model(w.def, out _, out _);
            if (prefab)
            {
                var holder = new GameObject("Model").transform;
                holder.SetParent(go.transform, false);
                holder.localPosition = Vector3.up * 0.9f;
                var m = Object.Instantiate(prefab, holder).transform;
                foreach (var r in m.GetComponentsInChildren<Renderer>(true)) { r.sharedMaterial = Weapons.Mat(w.def); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
                var rs = m.GetComponentsInChildren<Renderer>(true);
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    float len = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                    float k = Mathf.Min(1.6f, Weapons.HoldLength(w.def.type)) / Mathf.Max(0.01f, len);
                    m.localScale *= k;
                    m.localPosition = -(b.center - holder.position) * k;
                }
                pk.model = holder;
            }
            var l = new GameObject("Glow").AddComponent<Light>();
            l.transform.SetParent(go.transform, false); l.transform.localPosition = Vector3.up * 1.1f;
            l.type = LightType.Point; l.color = c; l.intensity = 3f; l.range = 4.5f; l.shadows = LightShadows.None;
            if (!font) font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" }, 48);
            var tgo = new GameObject("Label");
            tgo.transform.SetParent(go.transform, false);
            tgo.transform.localPosition = Vector3.up * 2.2f;
            var tm = tgo.AddComponent<TextMesh>();
            tm.font = font; tm.fontSize = 48; tm.characterSize = 0.06f; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.text = $"{w.Name}\n<size=30>{w.def.type} · {w.SkillDef.name}</size>"; tm.color = c;
            tgo.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            FX.Ring(go.transform.position, 0.2f, 2f, 0.5f, new Color(c.r, c.g, c.b, 0.9f), 0.15f);
            if (!quiet && Combat.player && Vector3.Distance(Combat.player.transform.position, go.transform.position) < 25f)
                Combat.Toast($"掉落武器：{w.Name}（{w.def.type}）· 技能「{w.SkillDef.name}」", c);
        }

        void Update()
        {
            if (model) { model.Rotate(0f, 50f * Time.deltaTime, 0f, Space.World); model.localPosition = Vector3.up * (0.9f + Mathf.Sin(Time.time * 2f) * 0.1f); }
            var cam = Camera.main; var label = transform.Find("Label");
            if (cam && label) label.rotation = cam.transform.rotation;
            if (Time.time < born) return;
            foreach (var h in Combat.heroes)
            {
                if (!h || h.Downed) continue;
                float dist = Vector3.Distance(Echoes.Flat(h.transform.position), Echoes.Flat(transform.position));
                if (h.UsesBrain)
                {
                    if (dist < 3f && Weapons.AiWants(h, w)) { Weapons.AiTake(h, w); Destroy(gameObject); return; }
                    continue;
                }
                if (dist > 2f) continue;
                if (h.kit.FreeSlot() >= 0) { Weapons.Take(h, w); Toast(h, "拿起"); Destroy(gameObject); return; }
                if (Time.time >= nextHint) { nextHint = Time.time + 3f; Combat.Toast($"按 {GameInput.Hint("F","A")} 换下手上的 {h.kit.Active.Name}，拿起 {w.Name}：{Weapons.Describe(w)}", Weapons.StarCol[w.star]); }
                if ((!GameInput.CombatBlocked && GameInput.Down(GameAction.Interact))) { Weapons.Take(h, w); Toast(h, "换上"); Destroy(gameObject); return; }
            }
            if (Time.time - born > 180f) Destroy(gameObject);
        }

        void Toast(HeroBase h, string verb)
        {
            if (!h.IsControlled) return;
            Combat.Toast($"{verb} {w.Name}：{Weapons.Describe(w)}", Weapons.StarCol[w.star]);
            Feel.I.Sfx("perfect_dodge", 0.5f, 0f, 1.5f);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    public enum Rarity { 基础, 珍稀, 传说 }

    public class ItemDef
    {
        public string id, name, prop, desc;
        public Rarity rarity;
        public int price, limit;   // 价格（贝币）；每局限购（个人道具按人算，团队道具按队伍算）
        public bool team;
    }

    // 一个角色的贝币、武器品质和快捷栏（原稿：快捷栏 3 种道具，每种最多 4 个）。
    // 团队道具（护盾共鸣器）原稿放公共仓库；公共仓库砍掉了，改成队伍共用的一格（Economy.teamItem）。
    public class Kit
    {
        public float coins, earned, spent;
        // 三个武器栏（2026-10-09，见 Run/Weapons.cs）。weaponStar = 手持那把的星级（没有武器的公式模拟用 baseStar）
        public int baseStar = 2;
        public readonly WeaponItem[] weapons = new WeaponItem[3];
        public int active;
        public int version;                           // 武器变化时 +1（词条的流派层数按它同步）
        public WeaponItem Active => weapons[active];
        public int weaponStar
        {
            get => weapons[active] != null ? weapons[active].star : baseStar;
            set { if (weapons[active] != null) weapons[active].star = value; else baseStar = value; version++; }
        }
        // 三把武器的词条合计：同名只算一次，强度取带这条词条的武器里星级最高的（AffixStar）
        public readonly List<string> affixes = new List<string>();
        public void Rebuild() { affixes.Clear(); foreach (var w in weapons) if (w != null) foreach (var a in w.affixes) if (!affixes.Contains(a)) affixes.Add(a); version++; }
        public int AffixStar(string id) { int s = 0; foreach (var w in weapons) if (w != null && w.affixes.Contains(id)) s = Mathf.Max(s, w.star); return s; }
        public int FreeSlot() { for (int i = 0; i < 3; i++) if (weapons[i] == null) return i; return -1; }
        public int LowestStar() { int s = 9; bool any = false; foreach (var w in weapons) if (w != null) { any = true; s = Mathf.Min(s, w.star); } return any ? s : baseStar; }
        // 玩家拿起 / 买：有空栏放空栏，满了替换手上那把；新武器换到手上。返回被替换的
        public WeaponItem Put(WeaponItem w) { int f = FreeSlot(); WeaponItem old = null; if (f < 0) { f = active; old = weapons[f]; } weapons[f] = w; active = f; Rebuild(); return old; }
        // AI / 公式模拟：满了替换星级最低的那把
        public WeaponItem PutReplaceLowest(WeaponItem w) { int f = FreeSlot(); WeaponItem old = null; if (f < 0) { f = 0; for (int i = 1; i < 3; i++) if (weapons[i].star < weapons[f].star) f = i; old = weapons[f]; } weapons[f] = w; Rebuild(); return old; }
        public void SelectBest() { int b = active; for (int i = 0; i < 3; i++) if (weapons[i] != null && (weapons[b] == null || weapons[i].star > weapons[b].star)) b = i; active = b; }
        public readonly string[] slot = new string[Economy.Slots];
        public readonly int[] count = new int[Economy.Slots];
        public readonly Dictionary<string, int> bought = new Dictionary<string, int>();
        public readonly Dictionary<string, int> used = new Dictionary<string, int>();

        public int Has(string id) { int n = 0; for (int i = 0; i < slot.Length; i++) if (slot[i] == id) n += count[i]; return n; }
        public int Bought(string id) => bought.TryGetValue(id, out var n) ? n : 0;
        public int SlotOf(string id) { for (int i = 0; i < slot.Length; i++) if (slot[i] == id && count[i] > 0) return i; return -1; }
        // 能不能再放一个：已有这一种且没满 4 个，或者还有空格
        public bool CanHold(string id)
        {
            for (int i = 0; i < slot.Length; i++) if (slot[i] == id && count[i] < Economy.Stack) return true;
            for (int i = 0; i < slot.Length; i++) if (count[i] == 0) return true;
            return false;
        }
        public void Add(string id)
        {
            for (int i = 0; i < slot.Length; i++) if (slot[i] == id && count[i] < Economy.Stack) { count[i]++; return; }
            for (int i = 0; i < slot.Length; i++) if (count[i] == 0) { slot[i] = id; count[i] = 1; return; }
        }
        public bool Take(string id)
        {
            int i = SlotOf(id);
            if (i < 0) return false;
            if (--count[i] == 0) slot[i] = null;
            used[id] = (used.TryGetValue(id, out var n) ? n : 0) + 1;
            return true;
        }
        public void Earn(float c) { coins += c; earned += c; }
    }

    // 商人的一个货位：道具或武器（武器只给指定角色用，原稿「只出符合队伍里某个人的武器」）
    public class ShopOffer
    {
        public ItemDef item;
        public int star;
        public HeroId forHero;
        public int stock, price;
        public string affix;
        public WeaponItem weapon;   // 货架上的武器（种类、技能、词条都已经定好，买之前就能看）
        public bool IsWeapon => item == null;
        public string Name => IsWeapon ? (weapon != null ? weapon.Name : $"{star}★ 武器") : item.name;
    }

    public class Shop
    {
        public string nodeId;
        public bool final;
        public int visits;
        public float lastVisit = -99f;
        public readonly List<ShopOffer> offers = new List<ShopOffer>();
    }

    // 经济（M3）：货币只来自击杀，和经验同一套倍数；商人、道具、武器品质。
    // 数值推导见 HANDOFF「M3 经济」：原稿的四条约束（基础价 ≤ 一只轻波的货币、一个据点的轻波够买 4 个基础道具、
    // 3★ ≤ 巨浪掉落、4★ ≤ 怒涛掉落）放到实测击杀量上，一局能买下全部道具 2–3 遍，和「约能买 75%」冲突。
    // 处理：保留原稿的价格比例（基础 1 : 珍稀 6–8 : 3★ 5 : 4★ 25），只调货币基础值，用公式模拟定到「约 75%」。
    public static class Economy
    {
        public const int Slots = 3, Stack = 4;

        // ---------------- 货币 ----------------
        public static float CoinBase = 2.5f;                            // 一只轻波的贝币（RunSim「经济：扫货币基础值」定的，见 HANDOFF「M3 经济」）
        static readonly float[] TierMult = { 1f, 5f, 30f, 80f };        // 原稿：巨浪 5、怒涛 30、海啸 80 倍
        public static float TierCoin(int tier) => CoinBase * TierMult[Mathf.Clamp(tier, 0, 3)];
        public static float MessengerCoin => CoinBase * 70f;            // 信使：原稿 40–80 倍，和经验一样取 70

        // ---------------- 道具（第三批模型 33–38） ----------------
        public const int BasicPrice = 30;
        public static readonly ItemDef[] Items =
        {
            new ItemDef { id = "heal", name = "治疗剂", prop = "33_heal_potion", rarity = Rarity.基础, price = BasicPrice, limit = 10, desc = "立即回复 30% 生命" },
            new ItemDef { id = "tonic", name = "强化剂", prop = "34_power_tonic", rarity = Rarity.基础, price = BasicPrice, limit = 4, desc = "20 秒内伤害 +15%" },
            new ItemDef { id = "bomb", name = "元素炸弹", prop = "36_element_bomb", rarity = Rarity.基础, price = BasicPrice, limit = 4, desc = "对落点 4.5 米内的敌人造成大量伤害" },
            new ItemDef { id = "stab", name = "超频稳定剂", prop = "35_stabilizer", rarity = Rarity.珍稀, price = BasicPrice * 8, limit = 1, desc = "超频值 −50%，1 分钟内保留原有增伤" },
            new ItemDef { id = "purifier", name = "净化器", prop = "37_purifier", rarity = Rarity.珍稀, price = BasicPrice * 6, limit = 3, desc = "清除 12 米内的瘴气" },
            new ItemDef { id = "shield", name = "护盾共鸣器", prop = "38_shield_resonator", rarity = Rarity.传说, price = BasicPrice * 20, limit = 1, team = true, desc = "全队获得 30% 最大生命的护盾，持续 20 秒（三人平摊价格）" },
        };
        public static ItemDef Item(string id) { foreach (var d in Items) if (d.id == id) return d; return null; }

        // ---------------- 武器品质：只做伤害倍率 ----------------
        public static float WeaponMult(int star) => star >= 5 ? 1.6f : star == 4 ? 1.35f : star == 3 ? 1.15f : 1f;
        public static int WeaponPrice(int star) => star >= 5 ? BasicPrice * 70 : star == 4 ? BasicPrice * 25 : BasicPrice * 5;
        public static string WeaponName(HeroId h) => h == HeroId.Feilie ? "长刃" : h == HeroId.Qinglan ? "双枪" : "音感仪";

        // 队伍共用的团队道具格
        public static string teamItem;
        public static int teamItemCount, teamBought;
        public static int teamBoughtSim;   // 公式模拟每局结束时记一下（模拟和实时跑局共用上面的静态状态）

        public static void Reset() { teamItem = null; teamItemCount = 0; teamBought = 0; }

        // ---------------- 商人库存（原稿：全部基础道具；4 个随机珍稀；40% 一个传说，终局商人保底；5 把 3★、1 把 4★、20% 一把 5★） ----------------
        public static Shop MakeShop(string nodeId, bool final, System.Random rng)
        {
            var s = new Shop { nodeId = nodeId, final = final };
            void Add(string id, int stock) { var d = Item(id); s.offers.Add(new ShopOffer { item = d, stock = stock, price = d.price }); }
            Add("heal", 10); Add("tonic", 6); Add("bomb", 6);
            // 4 个珍稀货位，在两种珍稀道具里随机分
            int stab = 0, pur = 0;
            for (int i = 0; i < 4; i++) { if (rng.NextDouble() < 0.4) stab++; else pur++; }
            if (stab > 0) Add("stab", stab);
            if (pur > 0) Add("purifier", pur);
            if (final || rng.NextDouble() < 0.4) Add("shield", 1);
            void W(int star)
            {
                // 武器偏向某个队员（原稿：考虑玩家武器类型）：70% 是他原生种类的武器，30% 任意种类
                var who = (HeroId)rng.Next(0, 3);
                var def = rng.NextDouble() < 0.7 ? Weapons.RandomDef(Weapons.NativeType(who), rng) : Weapons.RandomDef(null, rng);
                s.offers.Add(new ShopOffer { star = star, forHero = who, stock = 1, price = WeaponPrice(star), weapon = Weapons.Make(def, star, rng) });
            }
            for (int i = 0; i < 5; i++) W(3);
            W(4);
            if (rng.NextDouble() < 0.2) W(5);
            return s;
        }

        // ---------------- 购买 ----------------
        public static bool CanBuy(Kit k, ShopOffer o, HeroId who, int teamSize, out string why)
        {
            why = "";
            if (o.stock <= 0) { why = "售罄"; return false; }
            if (o.IsWeapon)
            {
                if (k.coins < o.price) { why = "贝币不足"; return false; }   // 任何种类都能用；三栏满了就替换手上那把
                return true;
            }
            if (o.item.team)
            {
                if (teamBought >= o.item.limit) { why = "本局已限购"; return false; }
                if (teamItemCount > 0) { why = "团队格已占用"; return false; }
                if (k.coins < o.price / (float)teamSize) { why = "贝币不足"; return false; }
                return true;
            }
            if (k.Bought(o.item.id) >= o.item.limit) { why = "本局已限购"; return false; }
            if (!k.CanHold(o.item.id)) { why = "快捷栏已满"; return false; }
            if (k.coins < o.price) { why = "贝币不足"; return false; }
            return true;
        }

        // 团队道具三人平摊（原稿的 AA 机制）：每人出 1/3，谁不够由买的人补
        public static WeaponItem lastReplaced;   // 玩家买武器时被换下来的那把（商店界面把它放到地上）
        public static void Buy(Kit k, ShopOffer o, HeroId who, IList<Kit> team, bool player = false)
        {
            o.stock--;
            if (o.IsWeapon)
            {
                k.coins -= o.price; k.spent += o.price;
                var w = o.weapon ?? new WeaponItem { def = Weapons.NativeDef(who), star = o.star };
                if (player) lastReplaced = k.Put(w);
                else { lastReplaced = null; k.PutReplaceLowest(w); k.SelectBest(); }
                return;
            }
            if (o.item.team)
            {
                float share = o.price / (float)team.Count, paid = 0f;
                foreach (var t in team) if (t != k) { float p = Mathf.Min(share, t.coins); t.coins -= p; t.spent += p; paid += p; }
                k.coins -= o.price - paid; k.spent += o.price - paid;
                teamItem = o.item.id; teamItemCount = 1; teamBought++;
                return;
            }
            k.coins -= o.price; k.spent += o.price;
            k.bought[o.item.id] = k.Bought(o.item.id) + 1;
            k.Add(o.item.id);
        }

        // ---------------- AI 的购物规则（实时跑局和公式模拟共用） ----------------
        // 先保 2 个治疗；然后买得起就升级武器（买完要还剩 2 个基础道具的钱）；再按阶段填快捷栏。
        // 阶段：侵蚀 ≥ 35% 时一格换成超频稳定剂（超频 45% 开始）；瘴气期（3–10 分钟）辅助和副 C 各带净化器。
        public static List<string> Wishes(HeroId who, float erosion, float minute, bool miasma)
        {
            var w = new List<string> { "heal" };
            if (erosion >= 35f) w.Add("stab");
            if (miasma && minute >= 2.5f && minute < 10f && who != HeroId.Feilie) w.Add("purifier");
            if (who == HeroId.Chengyin) { w.Add("bomb"); w.Add("tonic"); }
            else { w.Add("tonic"); w.Add("bomb"); }
            return w;
        }

        public static int AutoBuy(Kit k, Shop s, HeroId who, IList<Kit> team, float erosion, float minute, bool miasma, List<string> log = null)
        {
            int n = 0;
            ShopOffer Find(string id) { foreach (var o in s.offers) if (!o.IsWeapon && o.item.id == id) return o; return null; }
            bool Try(ShopOffer o)
            {
                if (o == null || !CanBuy(k, o, who, team.Count, out _)) return false;
                Buy(k, o, who, team); n++;
                log?.Add(o.Name);
                return true;
            }
            var heal = Find("heal");
            while (k.Has("heal") < 2 && Try(heal)) { }
            // 武器：挑买得起的最高星
            ShopOffer best = null;
            foreach (var o in s.offers)
                if (o.IsWeapon && o.forHero == who && (o.weapon == null || o.weapon.def.type == Weapons.NativeType(who)) && o.star > k.LowestStar() && o.stock > 0 && k.coins - o.price >= 2 * BasicPrice && (best == null || o.star > best.star)) best = o;
            Try(best);
            // 快捷栏：按愿望顺序填，每种最多 4 个；占不到格子的跳过
            foreach (var id in Wishes(who, erosion, minute, miasma))
            {
                var o = Find(id);
                int want = id == "stab" ? 1 : Stack;
                while (k.Has(id) < want && Try(o)) { }
            }
            // 团队道具：每人都还剩得下自己那份 + 1 个基础道具时买
            var sh = Find("shield");
            if (sh != null && sh.stock > 0 && teamItemCount == 0 && teamBought < sh.item.limit)
            {
                bool all = true; float share = sh.price / (float)team.Count;
                foreach (var t in team) if (t.coins < share + BasicPrice) all = false;
                if (all) Try(sh);
            }
            return n;
        }

        // 要不要绕路去商人：有钱（够 3 个基础道具）而且有想买的东西
        public static bool WantsShop(Kit k, float erosion, float minute, bool miasma, HeroId who)
        {
            if (k.coins < 3 * BasicPrice) return false;
            foreach (var id in Wishes(who, erosion, minute, miasma))
            {
                var d = Item(id);
                if (k.Bought(id) < d.limit && k.Has(id) < (id == "stab" ? 1 : Stack) && k.CanHold(id)) return true;
            }
            return k.coins >= WeaponPrice(k.weaponStar + 1) + 2 * BasicPrice && k.weaponStar < 5;
        }

        // ---------------- 掉落（原稿）：轻波 10% 掉武器，其中 20% 高一阶（3★）；营地宝箱 4★；巢穴 3★ 66% / 4★ 33%；裂隙 4–5★ ----------------
        public const float LightWeaponChance = 0.1f * 0.2f;
        public static int NodeWeapon(NodeType t, System.Random rng)
        {
            double r = rng.NextDouble();
            switch (t)
            {
                case NodeType.Camp: return 4;
                case NodeType.Nest: return r < 0.66 ? 3 : 4;
                case NodeType.Rift: return r < 0.7 ? 4 : 5;
            }
            return 0;
        }

        // ---------------- 实时跑局：击杀给钱、掉武器 ----------------
        public static void OnKill(Damageable e)
        {
            if (!Growth.Enabled || !e) return;
            float c = e.expValue > 0f ? e.expValue / Growth.ExpBase * CoinBase : TierCoin(e.tier);
            if (e.tide) c *= 0.5f;     // 涌浪兵持续刷，全额给钱会让后期彻底花不完（实测 18 分钟在 30 单价上连点）
            foreach (var h in Combat.heroes) if (h) h.kit.Earn(c * Growth.TeamShare);
            // 涌浪不掉武器：轻波掉落率是按「一局几百只轻波」定的，怪潮化之后不过滤会直接把人均 5★ 冲上来
            if (e.tier == 0 && !e.tide && Random.value < LightWeaponChance) GiveWeapon(Combat.heroes[Random.Range(0, Combat.heroes.Count)], 3, "轻波掉落");
        }

        // 掉落的武器给品质最低的人（原稿是掉给对应武器类型的人；AI 队伍里直接自动装备）
        public static HeroBase Lowest()
        {
            HeroBase best = null;
            foreach (var h in Combat.heroes) if (h && (best == null || h.kit.weaponStar < best.kit.weaponStar)) best = h;   // 看手上那把（AI 总拿最好的）；看三栏最低的话，留着开局 2★ 的人会一直被当成最差（2026-10-09 验证局：宝箱武器全给了绯烈）
            return best;
        }

        public static void GiveWeapon(HeroBase h, int star, string src)
        {
            // 2026-10-09：武器不再直接装上，而是掉在这个队员脚边（他原生种类的随机武器），AI 路过时按规则拿，玩家自己挑
            if (!h) return;
            var w = Weapons.Make(Weapons.RandomDef(Weapons.NativeType(h.Id)), star);
            // AI 队员的奖励直接交到手上（2026-10-09 验证局：掉在地上的宝箱武器 AI 打完就走，整局没换过武器）
            if (h.UsesBrain && Weapons.AiWants(h, w)) Weapons.AiTake(h, w);
            else Weapons.Drop(w, h.transform.position + h.transform.forward * 1.6f, true);
            Combat.Toast($"{src}：掉落 {w.Name}（{w.def.type}，技能「{w.SkillDef.name}」）", Weapons.StarCol[w.star]);
            if (RunDirector.I) RunDirector.I.Event("weapon", "", $"{h.HeroName} {star} {src}");
        }

        // ---------------- 用道具 ----------------
        public static System.Func<Vector3, float, int> purify;   // 瘴气系统挂进来：清掉 p 附近 r 米的瘴气，返回清掉几片

        public static bool Use(HeroBase h, int slot, Vector3 aim)
        {
            if (!h || h.Downed || slot < 0 || slot >= Slots || h.kit.count[slot] <= 0) return false;
            string id = h.kit.slot[slot];
            if (!Apply(h, id, aim)) return false;
            h.kit.Take(id);
            if (RunDirector.I) RunDirector.I.Event("item", "", $"{h.HeroName} {Item(id).name}");
            return true;
        }

        public static bool UseTeam(HeroBase h)
        {
            if (!h || h.Downed || teamItemCount <= 0) return false;
            if (!Apply(h, teamItem, h.transform.position)) return false;
            if (RunDirector.I) RunDirector.I.Event("item", "", $"{h.HeroName} {Item(teamItem).name}");
            teamItemCount = 0; teamItem = null;
            return true;
        }

        static readonly List<Damageable> tmp = new List<Damageable>();

        static bool Apply(HeroBase h, string id, Vector3 aim)
        {
            Vector3 p = h.transform.position;
            switch (id)
            {
                case "heal":
                    if (h.dmg.hp >= h.dmg.maxHp * 0.98f) return false;
                    h.dmg.hp = Mathf.Min(h.dmg.maxHp, h.dmg.hp + h.dmg.maxHp * 0.3f);
                    FX.Ring(p, 0.3f, 1.8f, 0.45f, new Color(0.5f, 1f, 0.6f, 0.9f), 0.12f);
                    FX.Sparks(p + Vector3.up, Vector3.up, 14, new Color(0.6f, 1f, 0.7f));
                    return true;
                case "tonic":
                    h.AddMod("dmg", "tonic", 0.15f, 20f);
                    FX.Ring(p, 0.3f, 2f, 0.4f, new Color(1f, 0.55f, 0.3f, 0.9f), 0.12f);
                    FX.Embers(p + Vector3.up * 0.5f, 16);
                    return true;
                case "bomb":
                {
                    Vector3 d = aim - p; d.y = 0f;
                    if (d.magnitude > 10f) d = d.normalized * 10f;
                    Vector3 c = p + d; c.y = 0f;
                    tmp.Clear();
                    foreach (var e in Combat.enemies) if (e && !e.dead && !e.dormant && Vector3.Distance(Flat(e.transform.position), c) <= 4.5f + e.radius) tmp.Add(e);
                    // 伤害 ≈ 一次解放的一半（倍率 9，削韧 60），吃等级和武器，不吃延奏
                    foreach (var e in tmp) h.DealHit(e, 9f, 60f, 6f, Flat(e.transform.position - c).normalized, true, "bomb", 0f, false);
                    FX.Ring(c, 0.5f, 4.8f, 0.5f, new Color(1f, 0.5f, 0.25f, 1f), 0.3f);
                    FX.Ring(c, 0.3f, 3.2f, 0.35f, new Color(1f, 0.9f, 0.6f, 1f), 0.2f);
                    FX.Sparks(c + Vector3.up * 0.6f, Vector3.up, 40, new Color(1f, 0.6f, 0.3f));
                    FX.Embers(c, 30);
                    if (h.IsControlled) { Feel.I.Shake(0.35f); Feel.I.Sfx("ult_hit", 0.7f, 0.05f, 0.9f); }
                    return true;
                }
                case "stab":
                {
                    if (h.overclock < 20f) return false;
                    float before = h.overclock / 100f * Coop.OcMaxBonus;
                    h.overclock *= 0.5f;
                    float after = h.overclock / 100f * Coop.OcMaxBonus;
                    h.AddMod("dmg", "stabilizer", before - after, 60f);   // 原稿：保留增伤
                    FX.Ring(p, 0.3f, 2.4f, 0.5f, new Color(0.4f, 0.9f, 1f, 0.95f), 0.14f);
                    FX.Tide(p + Vector3.up, Vector3.up, 20);
                    return true;
                }
                case "purifier":
                {
                    int n = purify != null ? purify(p, 12f) : 0;
                    if (n <= 0) return false;
                    FX.Ring(p, 0.5f, 12f, 0.8f, new Color(0.7f, 1f, 0.95f, 0.9f), 0.25f);
                    return true;
                }
                case "shield":
                    foreach (var o in Combat.heroes)
                    {
                        if (!o || o.Downed || Vector3.Distance(o.transform.position, p) > 50f) continue;
                        o.dmg.shield = Mathf.Max(o.dmg.shield, o.dmg.maxHp * 0.3f);
                        o.shieldUntil = Time.time + 20f;
                        FX.Ring(o.transform.position, 0.3f, 2.2f, 0.5f, new Color(0.9f, 0.95f, 1f, 0.95f), 0.14f);
                    }
                    if (h.IsControlled || HeroBase.Autopilot) Combat.Toast($"{h.HeroName} 使用护盾共鸣器 · 全队护盾", new Color(0.9f, 0.95f, 1f));
                    return true;
            }
            return false;
        }

        // ---------------- AI 用道具（队友，以及自动代打时的玩家角色） ----------------
        public static void TickAI(HeroBase h, ref float nextT)
        {
            if (Time.time < nextT || h.Downed) return;
            nextT = Time.time + 0.5f;
            var k = h.kit;
            Vector3 p = h.transform.position;
            int near = 0; Damageable boss = null; Vector3 sum = Vector3.zero;
            foreach (var e in Combat.enemies)
            {
                if (!e || e.dead || e.dormant) continue;
                float d = Vector3.Distance(Flat(e.transform.position), Flat(p));
                if (d > 12f) continue;
                near++; sum += e.transform.position;
                if (e.bossCtl) boss = e;
            }
            bool big = boss || near >= 5;
            int s;
            if (h.dmg.hp < h.dmg.maxHp * 0.4f && (s = k.SlotOf("heal")) >= 0) { Use(h, s, p); nextT += 2.5f; return; }
            if (h.overclock >= 70f && (s = k.SlotOf("stab")) >= 0) { Use(h, s, p); return; }
            if (big && h.ModSum("dmg") < 0.01f && (s = k.SlotOf("tonic")) >= 0) { Use(h, s, p); return; }
            if ((s = k.SlotOf("bomb")) >= 0)
            {
                // 炸弹扔在敌人最密的地方：Boss 战扔 Boss 身上（Boss 血量 > 30% 时），杂兵至少 4 只聚在一起
                if (boss && boss.hp > boss.maxHp * 0.3f && Vector3.Distance(Flat(boss.transform.position), Flat(p)) < 9f) { Use(h, s, boss.transform.position); nextT += 6f; return; }
                if (near >= 4) { Vector3 c = sum / near; int in45 = 0; foreach (var e in Combat.enemies) if (e && !e.dead && !e.dormant && Vector3.Distance(Flat(e.transform.position), Flat(c)) < 4.5f) in45++; if (in45 >= 4) { Use(h, s, c); nextT += 6f; return; } }
            }
            if ((s = k.SlotOf("purifier")) >= 0 && purify != null && Miasma.Inside(p)) { Use(h, s, p); return; }
            // 团队道具：Boss 战里全队平均血量低于一半时，由辅助（或第一个活着的人）用
            if (teamItemCount > 0 && boss && (h.Id == HeroId.Chengyin || !Combat.heroes.Exists(o => o && o.Id == HeroId.Chengyin && !o.Downed)))
            {
                float hp = 0f; int n = 0;
                foreach (var o in Combat.heroes) if (o) { hp += o.Downed ? 0f : o.dmg.hp / o.dmg.maxHp; n++; }
                if (n > 0 && hp / n < 0.5f) UseTeam(h);
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}

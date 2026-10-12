using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BlackTide
{
    // DS-019 弹幕军火库：环绕飞刃 / 追击弹幕 / 火焰新星（每卡 5 级，割草向自动攻击）。
    // 由 HeroBase.Update 每帧 Tick（AI 队友同样受益）；伤害走 DealHit（吃全部词条/共鸣/暴击）。
    public static class Arsenal
    {
        class Blade { public GameObject go; public float ang; public float hitCd; }
        static readonly Dictionary<HeroBase, List<Blade>> blades = new Dictionary<HeroBase, List<Blade>>();

        class Bolt { public GameObject go; public Damageable tgt; public float life; }
        static readonly Dictionary<HeroBase, List<Bolt>> bolts = new Dictionary<HeroBase, List<Bolt>>();

        static readonly Dictionary<HeroBase, float> nextFire = new Dictionary<HeroBase, float>();
        static readonly Dictionary<HeroBase, float> nextNova = new Dictionary<HeroBase, float>();
        static readonly Dictionary<HeroBase, float> nextNet = new Dictionary<HeroBase, float>();       // DS-019b 雷霆之网
        static readonly Dictionary<HeroBase, float> vortexUntil = new Dictionary<HeroBase, float>(); // DS-019b 漩涡
        static readonly Dictionary<HeroBase, Vector3> vortexCenter = new Dictionary<HeroBase, Vector3>();

        // ---------------- 割草方向重做（Claude 总工 2026-10-11）新增五种 ----------------
        class Proj { public GameObject go; public Damageable tgt; public float life; public int hops; public float mult; public readonly List<Damageable> hit = new List<Damageable>(); }
        static readonly Dictionary<HeroBase, List<Proj>> projs = new Dictionary<HeroBase, List<Proj>>();
        class Vent { public Vector3 p; public float until; public float nextTick; public float radius; public float mult; public HeroBase owner; }
        static readonly List<Vent> vents = new List<Vent>();
        class Wave { public HeroBase owner; public Vector3 c; public float r; public float maxR; public float speed; public float mult; public readonly List<Damageable> hit = new List<Damageable>(); }
        static readonly List<Wave> waves = new List<Wave>();
        static readonly Dictionary<HeroBase, float> nextScythe = new Dictionary<HeroBase, float>();
        static readonly Dictionary<HeroBase, float> nextBounce = new Dictionary<HeroBase, float>();
        static readonly Dictionary<HeroBase, float> nextVent = new Dictionary<HeroBase, float>();
        static readonly Dictionary<HeroBase, float> nextGale = new Dictionary<HeroBase, float>();

        // 共鸣超载的脉冲自己造成的击杀不再计入触发计数——否则一次脉冲杀满 → 立刻又攒满 → 无限连锁
        public static bool SuppressCount;

        public static void Clear()
        {
            foreach (var kv in blades) foreach (var b in kv.Value) if (b.go) Object.Destroy(b.go);
            foreach (var kv in bolts) foreach (var b in kv.Value) if (b.go) Object.Destroy(b.go);
            foreach (var kv in projs) foreach (var p in kv.Value) if (p.go) Object.Destroy(p.go);
            blades.Clear(); bolts.Clear(); projs.Clear(); vents.Clear(); waves.Clear(); sigils.Clear();
            nextFire.Clear(); nextNova.Clear(); nextNet.Clear(); vortexUntil.Clear(); vortexCenter.Clear();
            nextScythe.Clear(); nextBounce.Clear(); nextVent.Clear(); nextGale.Clear();
            SuppressCount = false;
        }

        public static void Tick(HeroBase h)
        {
            if (!h || h.Downed) return;
            int orb = h.rogue.Lv("orbit");
            int hom = h.rogue.Lv("homing");
            int nov = h.rogue.Lv("nova");
            int net = h.rogue.Lv("net");
            int mal = h.rogue.Lv("maelstrom");
            if (orb > 0) TickBlades(h, orb);
            if (hom > 0) TickHoming(h, hom);
            if (nov > 0) TickNova(h, nov);
            if (net > 0) TickNet(h, net);
            if (mal > 0) TickMaelstrom(h, mal);
            if (h.rogue.Lv("scythe") > 0) TickScythe(h);
            if (h.rogue.Lv("bounce") > 0) TickBounce(h);
            if (h.rogue.Lv("vents") > 0) TickVentSpawn(h);
            if (h.rogue.Lv("gale") > 0) TickGale(h);
            if (h.rogue.Lv("overload") > 0 && h.rogue.overloadReadyAt >= 0f && Time.time >= h.rogue.overloadReadyAt) Pulse(h);
            // 弹体/地火/扩散波是全场共享的列表，而 Tick 每个英雄都调一次（三人共斗）——按帧去重，否则推进速度 ×3
            if (Time.frameCount != lastWorldTick)
            {
                lastWorldTick = Time.frameCount;
                TickProjectiles();
                TickVents();
                TickWaves();
                TickSigils();
            }
        }
        static int lastWorldTick = -1;

        // ---------------- 环绕飞刃 ----------------
        static void TickBlades(HeroBase h, int lv)
        {
            int count = 1 + lv;                                   // 2~6 把
            float dmg = 0.9f + 0.2f * lv;                          // 1.1~1.9
            float speed = (lv >= 4 ? 1.4f : 1f) * (lv >= 5 ? 1.3f : 1f) * 2.6f;
            var list = blades.ContainsKey(h) ? blades[h] : (blades[h] = new List<Blade>());
            while (list.Count > count) { if (list[list.Count - 1].go) Object.Destroy(list[list.Count - 1].go); list.RemoveAt(list.Count - 1); }
            while (list.Count < count) list.Add(new Blade { go = MakeBlade(h), ang = Random.value * 6.283f });
            const float R = 2.3f;
            Vector3 c = h.transform.position + Vector3.up * 1.25f;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var b = list[i];
                if (!b.go) { list.RemoveAt(i); continue; }
                b.ang += speed * Time.deltaTime;
                b.hitCd -= Time.deltaTime;
                Vector3 p = c + new Vector3(Mathf.Cos(b.ang), 0f, Mathf.Sin(b.ang)) * R;
                b.go.transform.position = p;
                var cam = Camera.main;
                if (cam) b.go.transform.rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, b.ang * Mathf.Rad2Deg + 90f);
                if (b.hitCd <= 0f)
                {
                    foreach (var e in Combat.enemies)
                    {
                        if (!e || e.dead || e.dormant) continue;
                        if (Vector3.Distance(Flat(e.transform.position), Flat(p)) < 1.7f)
                        {
                            b.hitCd = 0.25f;
                            Vector3 dir = (Flat(e.transform.position) - Flat(h.transform.position)).normalized;
                            h.DealHit(e, dmg, 20f, 3f, dir, false, "orbit");
                            FX.Sparks(p, dir, 3, new Color(0.55f, 0.9f, 1f));
                            break;
                        }
                    }
                }
            }
        }
        // ---------------- CL-006 视觉语法：环绕飞刃 ----------------
        // 主形体＝刀片本身 + 一段拖尾（运动路径用 Trail 表达，不靠地面圈）。
        static GameObject MakeBlade(HeroBase h)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.name = "OrbitBlade";
            q.transform.position = h.transform.position + Vector3.up * 1.25f;
            q.transform.localScale = new Vector3(0.5f, 2.1f, 1f);
            var mr = q.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mat = SlashFx.NewMat(FX.Comic);
            SlashFx.SetColors(mat, new Color(0.55f, 0.9f, 1f), FX.Comic);
            mr.sharedMaterial = mat;
            // 拖尾：转起来的轨迹本身就是一张"刃环"，比在地上画一圈更能说明范围，也不会和预警圈混
            var tr = q.AddComponent<TrailRenderer>();
            tr.time = 0.22f;
            tr.startWidth = 0.16f;
            tr.endWidth = 0f;
            tr.numCapVertices = 4;
            tr.sharedMaterial = mat;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            return q;
        }

        // ---------------- 追击弹幕 ----------------
        static void TickHoming(HeroBase h, int lv)
        {
            float interval = lv <= 2 ? 4f : lv == 3 ? 3.4f : lv == 4 ? 2.8f : 2.2f;
            int count = lv <= 2 ? 2 + lv : lv == 3 ? 5 : lv == 4 ? 6 : 8;   // 3,4,5,6,8
            float dmg = lv >= 4 ? 1.1f : lv >= 5 ? 1.3f : 0.9f;
            var list = bolts.ContainsKey(h) ? bolts[h] : (bolts[h] = new List<Bolt>());
            float t;
            nextFire.TryGetValue(h, out t);
            if (Time.time >= t)
            {
                nextFire[h] = Time.time + interval;
                var targets = new List<Damageable>();
                foreach (var e in Combat.enemies)
                {
                    if (!e || e.dead || e.dormant) continue;
                    if (Vector3.Distance(Flat(e.transform.position), Flat(h.transform.position)) < 20f) targets.Add(e);
                    if (targets.Count >= count) break;
                }
                if (targets.Count == 0) return;
                for (int i = 0; i < count; i++)
                {
                    var bgo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Object.Destroy(bgo.GetComponent<Collider>());
                    bgo.name = "HomingBolt";
                    bgo.transform.position = h.transform.position + Vector3.up * 1.6f + Random.insideUnitSphere * 0.5f;
                    bgo.transform.localScale = new Vector3(0.35f, 0.35f, 1f);
                    var mr = bgo.GetComponent<MeshRenderer>();
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var bmat = SlashFx.NewMat(FX.Comic);
                    bmat.mainTexture = Resources.Load<Texture2D>("VFX/softDot");
                    SlashFx.SetColors(bmat, new Color(0.75f, 0.85f, 1f), FX.Comic);
                    mr.sharedMaterial = bmat;
                    list.Add(new Bolt { go = bgo, tgt = targets[i % targets.Count], life = 4f });
                }
            }
            var cam2 = Camera.main;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var b = list[i];
                if (!b.go || b.life <= 0f) { if (b.go) Object.Destroy(b.go); list.RemoveAt(i); continue; }
                b.life -= Time.deltaTime;
                Vector3 tp = b.tgt && !b.tgt.dead ? Flat(b.tgt.Center) : b.go.transform.position;
                Vector3 d = (tp - b.go.transform.position); d.y = 0f;
                if (d.sqrMagnitude > 0.01f)
                {
                    b.go.transform.position += d.normalized * (16f * Time.deltaTime);
                    if (cam2) b.go.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
                }
                if (b.tgt && !b.tgt.dead && Vector3.Distance(Flat(b.go.transform.position), Flat(b.tgt.Center)) < 1.2f)
                {
                    h.DealHit(b.tgt, dmg, 18f, 2f, d.normalized, false, "homing");
                    FX.Sparks(b.tgt.Center, d.normalized, 4, new Color(0.75f, 0.85f, 1f));
                    Object.Destroy(b.go); list.RemoveAt(i);
                }
            }
        }

        // ---------------- 火焰新星 ----------------
        static void TickNova(HeroBase h, int lv)
        {
            float interval = lv <= 1 ? 6f : lv == 2 ? 5.6f : lv == 3 ? 5f : lv == 4 ? 4.6f : 4f;
            float radius = 4f + 0.6f * lv;                             // 4.6~7
            float dmg = 1.4f + 0.24f * lv;                             // 1.64~2.6
            float t;
            nextNova.TryGetValue(h, out t);
            if (Time.time < t) return;
            nextNova[h] = Time.time + interval;
            Vector3 c = Flat(h.transform.position);
            foreach (var e in Combat.enemies)
            {
                if (!e || e.dead || e.dormant) continue;
                if (Vector3.Distance(Flat(e.transform.position), c) < radius)
                {
                    Vector3 dir = (Flat(e.transform.position) - c).normalized;
                    h.DealHit(e, dmg, 24f, 4f, dir, false, "nova");
                }
            }
            // CL-006 视觉语法：火焰新星的主形体改成「周长上竖起的一圈火柱」——运动轴是 Y（升腾），
            // 而不是又一个从中心铺开的平面圆环。保留一道贴地的细环只用来交代边界。
            const int pillars = 10;
            for (int i = 0; i < pillars; i++)
            {
                float a = (i + Random.Range(-0.25f, 0.25f)) / pillars * Mathf.PI * 2f;
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                FX.FirePillar(p, 0.62f, 2.6f + Random.Range(0f, 1.1f), 0.42f, new Color(1f, 0.62f, 0.28f, 1f));
            }
            FX.FireField(c, radius, 1.05f, new Color(1f, 0.58f, 0.26f));   // DS-027：潮噬=不规则地火场（不再是一个橙圆环）
            FX.BigBurst(c, radius * 0.55f, new Color(1f, 0.6f, 0.28f), false);
            FX.Embers(c + Vector3.up * 1.4f, 10);
            FX.ImpactFlash(c + Vector3.up * 0.8f, radius * 0.45f, new Color(1f, 0.75f, 0.4f));
            Feel.I.Sfx("boss_slam", 0.4f, 0f, 1.5f);
        }

        // ---------------- 雷霆之网（DS-019b）：周期自动闪电链 ----------------
        static void TickNet(HeroBase h, int lv)
        {
            float interval = 5f - 0.4f * lv;   // 4.6~3
            int jumps = 2 + lv;                // 3~7
            float mult = 0.8f + 0.1f * lv;     // 0.9~1.3
            float t;
            nextNet.TryGetValue(h, out t);
            if (Time.time < t) return;
            nextNet[h] = Time.time + interval;
            Rogue.Chain(h, h.transform.position + Vector3.up * 1.6f, null, jumps, mult);
        }

        // ---------------- 漩涡（DS-019b）：瞄准点周期聚怪 + 减速 ----------------
        static void TickMaelstrom(HeroBase h, int lv)
        {
            float interval = lv >= 5 ? 6f : lv == 4 ? 6.5f : lv == 3 ? 7f : lv == 2 ? 7.5f : 8f;
            float dur = 1.2f + 0.2f * lv;
            float radius = 6f + 0.8f * lv;
            float now = Time.time;
            float until;
            vortexUntil.TryGetValue(h, out until);
            Vector3 c;
            if (now >= until)
            {
                c = h.AimPoint; c.y = 0f;
                vortexCenter[h] = c;
                vortexUntil[h] = now + dur;
                ElementVfx.Spawn(ElementVfx.Shape.Vortex,c,Vector3.forward,radius,dur,new Color(.22f,.8f,.95f,.7f));
                foreach (var e in Combat.enemies)
                {
                    if (!e || e.dead || e.dormant || e.bossCtl) continue;
                    if (Vector3.Distance(Flat(e.transform.position), c) <= radius + e.radius)
                    {
                        var m = e.GetComponent<IMob>();
                        if (m != null) m.Root(0.5f);   // 起手定身一下，吸得更明显
                    }
                }
            }
            else
            {
                if (!vortexCenter.TryGetValue(h, out c)) return;
                foreach (var e in Combat.enemies)
                {
                    if (!e || e.dead || e.dormant || e.bossCtl) continue;
                    Vector3 ep = Flat(e.transform.position);
                    if (Vector3.Distance(ep, c) <= radius + e.radius + 2f)
                    {
                        var mc = e.GetComponent<MinionController>();
                        if (mc) mc.knockVel += (c - ep).normalized * (6f * Time.deltaTime);
                    }
                }
            }
        }

        // ---------------- 潮汐镰刀（潮噬）：周期横扫 + 残血直接收割 ----------------
        // 割草语境里最想要的不是"伤害更高"，是"屏幕上的东西真的在消失"——所以对残血敌人执行化。
        static void TickScythe(HeroBase h)
        {
            int lv = h.rogue.Lv("scythe");
            float t; nextScythe.TryGetValue(h, out t);
            if (Time.time < t) return;
            nextScythe[h] = Time.time + (4f - 0.35f * lv);        // 4 → 2.25 秒
            float radius = 5.5f + 0.6f * lv;                      // 6.1 → 8.5 米
            float mult = 1f + 0.11f * lv;                         // 1.11 → 1.55
            float exe = 0.12f + 0.02f * lv;                       // 残血线 14% → 22%
            Vector3 c = Flat(h.transform.position);
            int hits = 0, maxHits = 4 + 2 * lv;
            foreach (var e in Combat.enemies.ToArray())
            {
                if (!e || e.dead || e.dormant || hits >= maxHits) continue;
                if (Vector3.Distance(Flat(e.transform.position), c) > radius + e.radius) continue;
                bool cut = !e.bossCtl && e.hp <= e.maxHp * exe;   // 收割：非 Boss 的残血敌人一刀带走
                Vector3 dir = DirXZ(c, e.transform.position);
                h.DealHit(e, mult * (cut ? 4f : 1f), 30f, 2.5f, dir, true, "scythe");
                // 三档反馈：普通横扫给火花，收割给 V03 大爆点——「刷掉一只」要有和「打中一只」不同的重量
                if (cut) FX.HeavyImpact(e.Center, 1.2f, new Color(0.4f, 1f, 0.9f));
                else FX.Sparks(e.Center, dir, 3, new Color(0.45f, 1f, 0.85f));
                hits++;
            }
            if (hits > 0)
            {
                // CL-006 视觉语法：潮汐镰刀是"横扫"，主形体就该是一道**竖起的斩面**（运动=扫，有方向），
                // 不是地上一圈。朝本次命中的质心方向出刀，玩家能看出这一刀砍向哪边。
                Vector3 dir = c; dir.y = 0f;
                dir = dir.sqrMagnitude > 0.04f ? (dir / dir.magnitude) : h.transform.forward;
                FX.SlashFan(c, dir, radius * 0.35f, radius, 210f, 0.3f, new Color(0.42f, 1f, 0.92f, 1f));
                Feel.I.Sfx("swing_heavy", 0.5f);
            }
        }

        // ---------------- 弹跳雷（雷鸣）：命中后跳到别的敌人 ----------------
        static void TickBounce(HeroBase h)
        {
            int lv = h.rogue.Lv("bounce");
            float t; nextBounce.TryGetValue(h, out t);
            if (Time.time < t) return;
            nextBounce[h] = Time.time + (4.5f - 0.3f * lv);       // 4.2 → 3.0 秒
            var list = projs.ContainsKey(h) ? projs[h] : (projs[h] = new List<Proj>());
            Damageable first = Nearest(h, 24f);
            if (!first) return;
            var go = MakeQuad("BounceShot", new Color(1f, 0.88f, 0.45f), 0.4f, 0.4f, "VFX/softDot");
            go.transform.position = h.transform.position + Vector3.up * 1.5f;
            list.Add(new Proj { go = go, tgt = first, life = 5f, hops = 2 + lv, mult = 1f + 0.08f * lv });
        }

        static void TickProjectiles()
        {
            var cam = Camera.main;
            foreach (var kv in projs)
            {
                var list = kv.Value;
                var h = kv.Key;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var p = list[i];
                    if (!h || h.Downed || !p.go || p.life <= 0f || p.hops <= 0) { if (p.go) Object.Destroy(p.go); list.RemoveAt(i); continue; }
                    p.life -= Time.deltaTime;
                    if (!p.tgt || p.tgt.dead) { p.tgt = Nearest(h, 24f, p.hit); if (!p.tgt) continue; }
                    Vector3 tp = Flat(p.tgt.Center), from = p.go.transform.position;
                    Vector3 d = tp - from; d.y = 0f;
                    float dist = d.magnitude;
                    if (dist > 0.02f)
                    {
                        p.go.transform.position += d.normalized * (22f * Time.deltaTime);
                        if (cam) p.go.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
                    }
                    if (dist < 1.4f)
                    {
                        p.hit.Add(p.tgt);
                        p.hops--;
                        h.DealHit(p.tgt, p.mult, 22f, 2f, d.normalized, false, "bounce");
                        FX.ImpactFlash(p.tgt.Center, 0.9f, new Color(1f, 0.88f, 0.45f));
                        p.mult *= 0.88f;                              // 逐跳衰减，防止一条链把全屏秒了
                        p.tgt = Nearest(h, 10f, p.hit);
                        if (!p.tgt) { if (p.go) Object.Destroy(p.go); list.RemoveAt(i); }
                    }
                }
            }
        }

        // ---------------- 地火喷口（潮噬）：敌人脚下 erupt，留一小片燃烧区 ----------------
        static void TickVentSpawn(HeroBase h)
        {
            int lv = h.rogue.Lv("vents");
            float t; nextVent.TryGetValue(h, out t);
            if (Time.time < t) return;
            nextVent[h] = Time.time + (5f - 0.35f * lv);          // 4.65 → 3.25 秒
            int count = 2 + lv;                                    // 3 → 7 处
            var cand = new List<Damageable>();
            foreach (var e in Combat.enemies)
            {
                if (!e || e.dead || e.dormant || e.bossCtl) continue;
                if (Vector3.Distance(Flat(e.transform.position), Flat(h.transform.position)) < 20f) cand.Add(e);
            }
            float mult = 0.42f + 0.03f * lv;
            for (int i = 0; i < count && cand.Count > 0; i++)
            {
                var pick = cand[Random.Range(0, cand.Count)];
                cand.Remove(pick);
                Vector3 p = Flat(pick.transform.position);
                vents.Add(new Vent { p = p, until = Time.time + 2.6f, nextTick = Time.time, radius = 2.4f, mult = mult, owner = h });
                FX.FireField(p,2.4f,2.6f,new Color(1f,.4f,.06f,.7f));
            }
        }

        static void TickVents()
        {
            for (int i = vents.Count - 1; i >= 0; i--)
            {
                var v = vents[i];
                if (!v.owner || Time.time >= v.until) { vents.RemoveAt(i); continue; }
                if (Time.time < v.nextTick) continue;
                v.nextTick = Time.time + 0.4f;
                int n = 0;
                foreach (var e in Combat.enemies.ToArray())
                {
                    if (!e || e.dead || e.dormant) continue;
                    if (Vector3.Distance(Flat(e.transform.position), v.p) <= v.radius + e.radius)
                    {
                        v.owner.DealHit(e, v.mult, 8f, 0.5f, DirXZ(v.p, e.transform.position), false, "vent");
                        n++;
                    }
                }
                FX.Sparks(v.p + Vector3.up * 0.4f, Vector3.up, n > 0 ? 5 : 2, new Color(1f, 0.6f, 0.25f));
                FX.Embers(v.p, n > 0 ? 4 : 2);
                if (n > 0)
                {
                    // The persistent fire envelope already shows this vent; no ring per damage tick.
                    FX.Smoke(v.p, 2, new Color(0.55f, 0.45f, 0.4f, 0.4f));   // 地火要留下冒烟的痕迹，不然像一闪而过的光圈
                }
            }
        }

        // ---------------- 风涡扫荡（协奏）：向外推进的环，边推边打边推开 ----------------
        // 定位是"被围住时脱身"：伤害不算高，但把贴身的人群推走，配合涌浪的大场面才需要。
        static void TickGale(HeroBase h)
        {
            int lv = h.rogue.Lv("gale");
            float t; nextGale.TryGetValue(h, out t);
            if (Time.time < t) return;
            nextGale[h] = Time.time + (6f - 0.4f * lv);           // 5.6 → 4 秒
            waves.Add(new Wave { owner = h, c = Flat(h.transform.position), r = 1.2f, maxR = 7f + 1.4f * lv, speed = 20f, mult = 1f + 0.1f * lv });
            ElementVfx.Spawn(ElementVfx.Shape.Sweep,Flat(h.transform.position),Vector3.forward,7f+1.4f*lv,(7f+1.4f*lv-1.2f)/20f,new Color(.35f,.85f,.6f,.85f));
        }

        static void TickWaves()
        {
            for (int i = waves.Count - 1; i >= 0; i--)
            {
                var w = waves[i];
                if (!w.owner) { waves.RemoveAt(i); continue; }
                float prev = w.r;
                w.r += w.speed * Time.deltaTime;
                foreach (var e in Combat.enemies.ToArray())
                {
                    if (!e || e.dead || e.dormant || w.hit.Contains(e)) continue;
                    float d = Vector3.Distance(Flat(e.transform.position), w.c);
                    if (d >= prev - 1f && d <= w.r + e.radius)     // 环扫过才结算，不是"到半径就全体中"
                    {
                        w.hit.Add(e);
                        Vector3 dir = (Flat(e.transform.position) - w.c); dir.y = 0f;
                        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
                        w.owner.DealHit(e, w.mult, 20f, 7f, dir, false, "gale");
                        var mc = e.GetComponent<MinionController>();
                        if (mc && !e.bossCtl) mc.knockVel += dir * 3.2f;
                    }
                }
                if (w.r >= w.maxR) waves.RemoveAt(i);
            }
        }

        // ---------------- 共鸣超载（协奏·传说）：杀够数量放一次全屏脉冲 ----------------
        // 这张卡是"割草效率 → 爽点"的兑换口：杀得越密，脉冲越频繁，脉冲又制造更多击杀。
        // 递归用 SuppressCount 掐断（脉冲打死的不计入下一次触发）。
        static void Pulse(HeroBase h)
        {
            int lv = h.rogue.Lv("overload");
            h.rogue.overloadReadyAt = -1f;
            float radius = 12f + lv;
            float mult = 1.2f + 0.2f * lv;
            Vector3 c = Flat(h.transform.position);
            SuppressCount = true;
            int n = 0;
            foreach (var e in Combat.enemies.ToArray())
            {
                if (!e || e.dead || e.dormant) continue;
                if (Vector3.Distance(Flat(e.transform.position), c) > radius + e.radius) continue;
                h.DealHit(e, mult, 45f, 4f, DirXZ(c, e.transform.position), true, "overload");
                n++;
            }
            SuppressCount = false;
            if (n > 0)
            {
                // 最强一档：V31 地裂爆发当主体， rings 给扩张感，烟给残留——这是「构筑兑现」的时刻，值得最重的画面
                FX.GroundBurst(c + Vector3.up * 0.1f, radius * 0.55f, new Color(0.5f, 1f, 0.95f));
                FX.Ring(c, 0.35f, radius, 0.5f, new Color(0.5f, 1f, 0.95f, 1f), 0.22f);
                // One radial pressure boundary is enough for this real circular pulse.
                FX.ImpactFlash(c + Vector3.up * 1.2f, radius * 0.5f, new Color(0.6f, 1f, 0.95f));
                FX.Smoke(c, 10, new Color(0.45f, 0.6f, 0.62f, 0.45f));
                Feel.I.Sfx("ult_hit", h.IsControlled ? 0.6f : 0.25f);
                if (h.IsControlled) Feel.I.Shake(0.4f);
            }
        }

        // 取最近的活敌（skip 里的跳过：弹跳链不能打同一个目标两次）
        static Damageable Nearest(HeroBase h, float range, List<Damageable> skip = null)
        {
            Damageable best = null; float bd = range;
            Vector3 c = Flat(h.transform.position);
            foreach (var e in Combat.enemies)
            {
                if (!e || e.dead || e.dormant) continue;
                if (skip != null && skip.Contains(e)) continue;
                float d = Vector3.Distance(Flat(e.transform.position), c) - e.radius;
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }

        static GameObject MakeQuad(string name, Color col, float sx, float sy, string texPath)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.name = name;
            q.transform.localScale = new Vector3(sx, sy, 1f);
            var mr = q.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mat = SlashFx.NewMat(FX.Comic);
            var tex = Resources.Load<Texture2D>(texPath);
            if (tex) mat.mainTexture = tex;
            SlashFx.SetColors(mat, col, FX.Comic);
            mr.sharedMaterial = mat;
            return q;
        }

        class Sigil { public Vector3 p; public float until; public float radius; public float mult; public float root; public HeroBase owner; }
        static readonly List<Sigil> sigils = new List<Sigil>();

        // 镜步残阵：完美闪避后沿闪避路径埋符阵，敌人踏入即爆 + 定身（区域封锁，被围时用来犁出一条路）
        public static void ArmSigils(HeroBase h, Vector3 from, Vector3 to, int count, float mult, float root)
        {
            const float r = 2.5f;
            for (int i = 0; i < count; i++)
            {
                float f = count <= 1 ? 1f : i / (count - 1f);
                Vector3 p = Flat(Vector3.Lerp(from, to, f));
                sigils.Add(new Sigil { p = p, until = Time.time + 4f, radius = r, mult = mult, root = root, owner = h });
                FX.Ring(p, 0.2f, r * 0.5f, 0.3f, new Color(0.56f, 0.82f, 1f, 0.7f), 0.08f);
            }
        }

        static void TickSigils()
        {
            for (int i = sigils.Count - 1; i >= 0; i--)
            {
                var s = sigils[i];
                if (!s.owner) { sigils.RemoveAt(i); continue; }
                bool boom = Time.time >= s.until;
                if (!boom)
                {
                    foreach (var e in Combat.enemies)
                    {
                        if (!e || e.dead || e.dormant) continue;
                        if (Vector3.Distance(Flat(e.transform.position), s.p) > s.radius + e.radius) continue;
                        Vector3 dir = Flat(e.transform.position) - s.p;
                        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
                        s.owner.DealHit(e, s.mult, 26f, 1.5f, dir.normalized, true, "sigil");
                        var m = e.GetComponent<IMob>();
                        if (m != null) m.Root(s.root);
                        FX.BindChains(s.p, e, s.root, new Color(0.56f, 0.9f, 1f));   // 定身要说得出「为什么动不了」
                        boom = true;
                        break;
                    }
                }
                if (boom)
                {
                    FX.Ring(s.p, 0.3f, s.radius + 0.4f, 0.35f, new Color(0.56f, 0.9f, 1f, 1f), 0.14f);
                    FX.Sparks(s.p + Vector3.up * 0.5f, Vector3.up, 8, new Color(0.7f, 0.9f, 1f));
                    sigils.RemoveAt(i);
                }
            }
        }

        static Vector3 DirXZ(Vector3 from, Vector3 to)
        {
            Vector3 d = Flat(to) - Flat(from);
            return d.sqrMagnitude < 0.0001f ? Vector3.forward : d.normalized;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}

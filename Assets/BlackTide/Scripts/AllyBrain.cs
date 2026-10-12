using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // AI 队友（移植自网页原型 src/sim/allyAI.ts）：躲预警 > 救人 > 接力 > 技能 > 站位与普攻
    // 玩家按 F1 开自动代打时，操控的角色也用它（反应更准）。
    public class AllyBrain
    {
        readonly HeroBase h;
        readonly HashSet<Combat.Threat> decided = new HashSet<Combat.Threat>();
        float ultReadySince = -1f, concertoFullSince = -1f;
        float strafe = 1f, strafeT, nextPress;
        static readonly List<Damageable> live = new List<Damageable>();

        public AllyBrain(HeroBase h) { this.h = h; }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        float Dist(Component c) => Vector3.Distance(Flat(c.transform.position), Flat(h.transform.position));

        // 去捡东西有上限（2026-10-09 第二局验证：一件走不到的掉落让队长原地走了 7 分钟）：同一件 6 秒还没拿到就放弃它
        readonly HashSet<int> lootGiveUp = new HashSet<int>();
        int lootId; float lootSince;
        Vector3? WantedLoot(Vector3 pos)
        {
            Component best = null; float bd = 20f;
            foreach (var p in Object.FindObjectsOfType<WeaponPickup>())
            {
                if (p.w == null || !Weapons.AiWants(h, p.w) || lootGiveUp.Contains(p.GetInstanceID())) continue;
                float d = Vector3.Distance(Flat(p.transform.position), pos);
                if (d < bd) { bd = d; best = p; }
            }
            foreach (var p in Object.FindObjectsOfType<EchoPickup>())
            {
                if (p.d == null || (h.echo != null && (p.d.tier <= h.echo.tier || h.echo.id == "messenger")) || lootGiveUp.Contains(p.GetInstanceID())) continue;
                float d = Vector3.Distance(Flat(p.transform.position), pos);
                if (d < bd) { bd = d; best = p; }
            }
            if (!best) { lootId = 0; return null; }
            int id = best.GetInstanceID();
            if (id != lootId) { lootId = id; lootSince = Time.time; }
            else if (Time.time - lootSince > 6f) { lootGiveUp.Add(id); lootId = 0; return null; }
            return Flat(best.transform.position);
        }

        // ---------------- 声骸使用条件 ----------------
        // 原来所有声骸都按「通用爆发」的规则（3.6 米内够多敌人）放：治疗、格挡、远程控制几乎不会按用途用。阈值是初始值，实战再调
        int Near(Vector3 c, float r, bool mobsOnly)
        {
            int n = 0;
            foreach (var e in live) if (Vector3.Distance(Flat(e.transform.position), c) <= r + e.radius && (!mobsOnly || e.GetComponent<IMob>() != null)) n++;
            return n;
        }
        int Wounded(float r, float frac)
        {
            int n = 0;
            foreach (var o in Coop.Alive()) if (Dist(o) <= r && o.dmg.hp < o.dmg.maxHp * frac) n++;
            return n;
        }
        bool ThreatOnMe(Vector3 pos, float lo, float hi)
        {
            foreach (var th in Combat.threats)
            {
                float left = th.resolveAt - Time.time;
                if (left >= lo && left <= hi && th.shape.Contains(pos, 0.6f)) return true;
            }
            return false;
        }
        bool ShouldUseEcho(BossController bossC, Vector3 pos, float now)
        {
            var d = h.echo;
            bool bossNear(float r) => bossC && Dist(bossC) <= r + bossC.dmg.radius;
            if (d == null) return Near(pos, HeroBase.EchoRadius, false) >= (h.Role == "主C" ? 1 : 2);
            switch (d.id)
            {
                case "priest": return Wounded(12f, 0.65f) >= 1;
                case "chanter": return ThreatOnMe(pos, 0.3f, 1.2f) || (Wounded(12f, 0.8f) >= 1 && Near(pos, 10f, false) >= 2);
                case "shield": return Near(pos, 6f, false) >= 2;
                case "brute": return Near(pos, 6f, false) >= 3 || (Near(pos, 8f, false) >= 1 && Wounded(10f, 0.7f) >= 1);
                case "duelist": return ThreatOnMe(pos, 0.3f, 0.8f);   // 只在马上要挨打时开格挡
                case "claw": return Near(pos, 9f, true) >= 2;
                case "javelin": return Near(pos, 14f, true) >= 2;
                case "bloat": return Near(pos, 14f, true) >= 3;
                case "jelly": return Near(pos, 9f, true) >= 3;
                case "ray": return Near(pos, 12f, false) >= 2 || bossNear(12f);
                case "knight": return live.Count >= 3 || bossNear(15f);
                case "frost": return Near(pos, 16f, true) >= 3 || (bossNear(12f) && Near(pos, 8f, false) >= 2);
                case "thunder": return Near(pos, 7f, true) >= 2 || bossNear(7f);
                case "reaper": return (h.dmg.hp < h.dmg.maxHp * 0.5f && Near(pos, 6f, false) >= 2) || (ThreatOnMe(pos, 0.2f, 0.6f) && h.dmg.hp < h.dmg.maxHp * 0.6f);
                case "messenger": return (bossC && bossNear(5.5f) && (bossC.Chanting || bossC.sovereign)) || Near(pos, 5.5f, false) >= 2;
            }
            return Near(pos, HeroBase.EchoRadius, false) >= (h.Role == "主C" ? 1 : 2);
        }

        // 防卡（2026-10-09 验证局）：想走却 3 秒没挪动 1.5 米，就往侧面绕 0.8 秒——导航网格认为能过、角色碰撞体却挤不过去的窄缝都能自己脱身
        Vector3 stuckRef; float stuckT, unstickUntil; Vector3 unstickDir;
        public void Tick()
        {
            TickCore();
            if (h.Downed) return;
            float now = Time.time;
            Vector3 p = Flat(h.transform.position);
            if (now < unstickUntil) { h.moveIn = (unstickDir + h.moveIn * 0.3f).normalized; return; }
            bool wants = h.moveIn.sqrMagnitude > 0.1f && h.state == HeroBase.State.Move;
            if (!wants || Vector3.Distance(p, stuckRef) > 1.5f) { stuckRef = p; stuckT = now; return; }
            if (now - stuckT > 3f)
            {
                unstickDir = Vector3.Cross(Vector3.up, h.moveIn.normalized) * (Random.value < 0.5f ? 1f : -1f);
                unstickUntil = now + 0.8f; stuckRef = p; stuckT = now;
                Nav.Clear();
            }
        }

        void TickCore()
        {
            float now = Time.time;
            h.attackHeld = false;
            h.interactHeld = false;
            h.moveIn = Vector3.zero;
            if (h.Downed) return;
            Vector3 pos = Flat(h.transform.position);
            float react = h.IsControlled ? 1f : 0.78f;   // 队友不是全知全能：预警末段的闪避成功率 78%

            // ---- 1. 躲预警 ----
            foreach (var th in Combat.threats)
            {
                if (!th.shape.Contains(pos, 0.75f)) continue;
                float left = th.resolveAt - now;
                if (left <= 0.24f && left > 0.02f)
                {
                    if (decided.Add(th) && !h.Invulnerable && Random.value < react)
                    {
                        h.moveIn = Escape(th.shape, pos);
                        h.bufDodge = now;
                        return;
                    }
                }
                else if (left > 0.24f && (h.Ranged || left < 0.9f))
                {
                    h.moveIn = Escape(th.shape, pos);
                    if (h.state == HeroBase.State.Move) return;
                }
            }
            if (decided.Count > 40) decided.Clear();

            // ---- 2. 救人 ----
            HeroBase down = null; float dd = float.MaxValue;
            foreach (var o in Combat.heroes)
            {
                if (o == h || !o.Downed || (o.rescuer && o.rescuer != h)) continue;
                float d = Dist(o);
                if (d < dd) { dd = d; down = o; }
            }
            if (down)
            {
                h.AimPoint = down.transform.position;
                if (dd > HeroBase.RescueRange - 0.5f) { h.moveIn = Nav.Dir(h, pos, down.transform.position); return; }
                h.interactHeld = true;
                return;
            }

            // ---- 目标：只打醒着的、离自己不远的敌人（开放地图里远处节点的敌人不算） ----
            live.Clear();
            bool open = Combat.clampOverride != null;
            foreach (var e in Combat.enemies)
                if (e && !e.dead && !e.dormant && (!open || Vector3.Distance(Flat(e.transform.position), pos) < 24f)) live.Add(e);
            if (live.Count == 0)
            {
                // 没仗打：20 米内有用得上的武器 / 声骸就先去捡（拾取要走到 3 米内，路过不一定碰得到）
                var loot = WantedLoot(pos);
                if (loot.HasValue) { h.moveIn = Nav.Dir(h, pos, loot.Value); return; }
                // 没仗打：治疗类声骸先给缺血的队友补（Codex 审阅 R3：原来没有敌人就直接返回，战后不会用祭司 R）
                if (h.EchoReady && h.echo != null && h.echo.id == "priest" && Wounded(12f, 0.7f) >= 1 && (h.state == HeroBase.State.Move || h.state == HeroBase.State.Attack) && now >= nextPress)
                { Press(ref h.bufEcho, now); return; }
                // 没仗打：队长（自动代打时）按路线走；其他人跟着队长
                var leader = Combat.player;
                if (open && h == leader && RunDirector.I) { h.moveIn = RunDirector.I.RouteStep(h); return; }
                Vector3 c = leader && leader != h && !leader.Downed ? Flat(leader.transform.position) : Flat(Coop.Centroid());
                float follow = open ? 4.5f : 3f;
                if (Vector3.Distance(c, pos) > follow) h.moveIn = Nav.Dir(h, pos, c) * (Vector3.Distance(c, pos) > follow + 4f ? 1f : 0.6f);
                return;
            }
            var bossC = Combat.NearestBoss(h.transform.position, 22f);
            Damageable boss = bossC ? bossC.dmg : null;
            Vector3 tc = Flat(Coop.Centroid());
            int adds = 0;
            foreach (var e in live) if (e != boss) adds++;
            Damageable target;
            if (h.Role == "主C") target = boss && Dist(boss) < 14f ? boss : Nearest(pos);
            else target = Nearest(tc);
            if (boss && h.Role != "主C" && adds <= 2) target = boss;
            // 优先目标（孵化囊、唤潮祭司、施法中的咏唱者）：远程优先去打；主 C 在附近没有 Boss 时也去
            Damageable prio = null; float pd = 18f;
            foreach (var e in live) if (e.priority) { float d = Dist(e); if (d < pd) { pd = d; prio = e; } }
            if (prio && (h.Role != "主C" || !boss || Dist(boss) > 14f)) target = prio;
            float dist = Dist(target) - target.radius;
            h.AimPoint = target.transform.position;
            bool free = h.state == HeroBase.State.Move || h.state == HeroBase.State.Attack;

            // ---- 3. 协奏接力：队友刚接力过就马上跟（凑团队技能）；有队友快满了就等一等（最多 8 秒） ----
            if (h.concerto >= h.concertoMax && free)
            {
                if (concertoFullSince < 0f) concertoFullSince = now;
                HeroBase to = PickRelayTarget();
                bool partnerJustRelayed = Coop.lastRelayer && Coop.lastRelayer != h && now - Coop.lastRelayT < Coop.RelayWindow - 0.5f && !Coop.lastRelayer.Downed;
                if (partnerJustRelayed) to = Coop.lastRelayer;
                bool partnerNear = false;
                foreach (var o in Coop.Alive()) if (o != h && o.concerto >= 70f) partnerNear = true;
                bool wait = Coop.teamSkillCharges > 0 && h.TeamUnlocked && !partnerJustRelayed && partnerNear && now - concertoFullSince < 8f;
                if (to && !wait) { h.relayTarget = to; concertoFullSince = -1f; }
            }
            else if (h.concerto < h.concertoMax) concertoFullSince = -1f;

            // ---- 4. 技能（超频 ≥ 70 停用技能、≥ 92 连普攻也停，像真人一样控制风险） ----
            bool ocSafe = h.overclock < 70f, ocCritical = h.overclock >= 92f;
            if (free && now >= nextPress)
            {
                // 澄音：咏唱时优先留静默打断（只要自己不会因此过载）
                if (h.Id == HeroId.Chengyin && h.UltReady && bossC && bossC.Chanting && bossC.ChantProgress > 0.12f && h.overclock + 20f < 100f)
                {
                    h.AimPoint = bossC.transform.position;
                    if (Dist(bossC) <= 14f) { Press(ref h.bufUlt, now); return; }
                    h.moveIn = (Flat(bossC.transform.position) - pos).normalized;
                    return;
                }
                if (ocSafe && Skills(bossC, boss, target, dist, tc, now)) return;
                // 声骸（2 级解锁）：按装的声骸类型判断（2026-10-09，Codex 审阅 R3）
                if (ocSafe && h.EchoReady && ShouldUseEcho(bossC, pos, now)) { Press(ref h.bufEcho, now); return; }
                // 武器技能（V）：目标在技能够得着的距离里就放（远程 12 米、近战 5 米）
                if (ocSafe && h.WeaponSkillReady && dist <= (h.Ranged ? 12f : 5f)) { Press(ref h.bufWeapon, now); return; }
            }

            // ---- 5. 站位与普攻 ----
            Vector3 to2 = Flat(target.transform.position) - pos;
            float want = h.PreferDist;
            if (h.moveIn.sqrMagnitude < 0.01f)
            {
                if (dist > want + 0.4f) h.moveIn = dist > 6f ? Nav.Dir(h, pos, target.transform.position) : to2.normalized;
                else if (h.Ranged && dist < want - 1.6f) h.moveIn = -to2.normalized;
                else if (h.Ranged)
                {
                    if (now > strafeT) { strafe = Random.value < 0.5f ? 1f : -1f; strafeT = now + Random.Range(1.5f, 3f); }
                    h.moveIn = Vector3.Cross(Vector3.up, to2.normalized) * strafe * 0.6f;
                }
            }
            if (dist <= h.Reach && !ocCritical)
            {
                h.attackHeld = true;
                if (now >= nextPress) { h.bufAttack = now; nextPress = now + 0.16f; }
            }
        }

        void Press(ref float buf, float now) { buf = now; nextPress = now + 0.35f; }

        Damageable Nearest(Vector3 from)
        {
            Damageable best = null; float bd = float.MaxValue;
            foreach (var e in live)
            {
                float d = Vector3.Distance(Flat(e.transform.position), from) - e.radius;
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }

        // 最大的杂兵团（不含 Boss）
        Vector3 BestCluster(Damageable boss, float r, out int n)
        {
            n = 0; Vector3 best = Vector3.zero;
            foreach (var e in live)
            {
                if (e == boss) continue;
                int c = 0;
                foreach (var o in live) if (o != boss && Vector3.Distance(o.transform.position, e.transform.position) <= r) c++;
                if (c > n) { n = c; best = Flat(e.transform.position); }
            }
            return best;
        }

        HeroBase PickRelayTarget()
        {
            HeroBase best = null;
            if (h.Id == HeroId.Chengyin)
            {
                // 澄音的延奏「净频」清超频：给超频最高的人
                float oc = -1f;
                foreach (var o in Coop.Alive()) if (o != h && o.overclock > oc) { oc = o.overclock; best = o; }
                return best;
            }
            HeroId pref = h.Id == HeroId.Feilie ? HeroId.Qinglan : HeroId.Feilie;
            foreach (var o in Coop.Alive()) if (o != h && o.Id == pref) return o;
            foreach (var o in Coop.Alive()) if (o != h) return o;
            return null;
        }

        bool Skills(BossController bossC, Damageable boss, Damageable target, float dist, Vector3 tc, float now)
        {
            bool skillReady = now >= h.skillReadyAt;
            bool ultReady = h.UltReady;
            switch (h.Id)
            {
                case HeroId.Feilie:
                    if (ultReady)
                    {
                        if (ultReadySince < 0f) ultReadySince = now;
                        int pack = 0;
                        foreach (var e in live) if (Vector3.Distance(e.transform.position, target.transform.position) <= 3.6f) pack++;
                        // 焚城：等失衡窗口（伤害 ×1.6），或者杂兵扎堆、Boss 残血、等太久
                        bool go = target.Broken || (target != boss && pack >= 3) || (target == boss && target.hp / target.maxHp < 0.12f) || now - ultReadySince > 12f || (!boss && pack >= 1);
                        if (go && dist <= 3.5f) { Press(ref h.bufUlt, now); ultReadySince = -1f; return true; }
                    }
                    if (skillReady && dist > 2.5f && dist <= 8f) { Press(ref h.bufSkill, now); return true; }
                    return false;

                case HeroId.Qinglan:
                    if (ultReady)
                    {
                        // 风障：罩住倒地的队友 → 挡 Boss 冲锋 / Boss 快失衡时贴墙翻倍架势 → 杂兵扎堆
                        HeroBase dn = null;
                        foreach (var o in Combat.heroes) if (o.Downed) dn = o;
                        if (dn && bossC && Vector3.Distance(bossC.transform.position, dn.transform.position) < 12f)
                        {
                            h.AimPoint = dn.transform.position + (Flat(bossC.transform.position - dn.transform.position)).normalized * 2.2f;
                            Press(ref h.bufUlt, now); return true;
                        }
                        if (bossC && (bossC.ChargingWindup || (boss.stagger / boss.staggerMax >= 0.3f && !boss.Broken)))
                        {
                            h.AimPoint = bossC.transform.position + (tc - Flat(bossC.transform.position)).normalized * (boss.radius + 1.2f);
                            Press(ref h.bufUlt, now); return true;
                        }
                        Vector3 cl = BestCluster(boss, 4f, out int n);
                        if (n >= 5) { h.AimPoint = cl; Press(ref h.bufUlt, now); return true; }
                    }
                    if (skillReady)
                    {
                        Vector3 cl = BestCluster(boss, 5f, out int n);
                        if (n >= 3 && Vector3.Distance(cl, Flat(h.transform.position)) < 10f) { h.AimPoint = cl; Press(ref h.bufSkill, now); return true; }
                        if (boss && Dist(boss) < 9.5f) { h.AimPoint = boss.transform.position; Press(ref h.bufSkill, now); return true; }
                    }
                    return false;

                case HeroId.Chengyin:
                    if (ultReady)
                    {
                        // 静默的两难：留给咏唱，还是现在保命。10 秒内要咏唱就先留着
                        bool chantSoon = bossC && bossC.ChantIn < 10f;
                        float avg = 0f; int cnt = 0; bool critical = false;
                        foreach (var o in Coop.Alive()) { float r = o.dmg.hp / o.dmg.maxHp; avg += r; cnt++; if (r < 0.25f) critical = true; }
                        avg = cnt > 0 ? avg / cnt : 1f;
                        if (!chantSoon && (avg < 0.5f || critical)) { if (bossC) h.AimPoint = bossC.transform.position; Press(ref h.bufUlt, now); return true; }
                    }
                    if (skillReady)
                    {
                        HeroBase low = null; float lr = 2f;
                        foreach (var o in Coop.Alive()) { float r = o.dmg.hp / o.dmg.maxHp; if (r < lr) { lr = r; low = o; } }
                        int near = 0;
                        foreach (var e in live) if (Vector3.Distance(Flat(e.transform.position), tc) < 6f) near++;
                        if (low && (lr < 0.8f || near >= 3 || (boss && Vector3.Distance(Flat(boss.transform.position), tc) < 8f)))
                        {
                            h.AimPoint = lr < 0.8f ? low.transform.position : tc;
                            Press(ref h.bufSkill, now); return true;
                        }
                    }
                    return false;
            }
            return false;
        }

        static Vector3 Escape(AttackShape s, Vector3 p)
        {
            Vector3 d = Flat(p - s.origin);
            switch (s.kind)
            {
                case AttackShape.Kind.Cone:
                case AttackShape.Kind.Line:
                    Vector3 side = Vector3.Cross(Vector3.up, s.fwd);
                    return (Vector3.Dot(d, side) >= 0f ? side : -side).normalized;
                case AttackShape.Kind.Ring:
                    return d.magnitude < (s.inner + s.radius) * 0.5f ? -d.normalized : d.normalized;
                default:
                    return d.sqrMagnitude > 0.01f ? d.normalized : Vector3.right;
            }
        }
    }
}

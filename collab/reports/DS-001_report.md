# DS-001 报告 · 武器 / 声骸 / 词条三系统逻辑审查

> 执行方：DeepSeek（副总工程师）　日期：2026-10-10 晚
> 性质：只读静态审查，未开 Unity、未开 Play。所有行号指向
> `Assets/BlackTide/Scripts/` 下文件。
> 审查范围：Weapons.cs、WeaponAffix.cs、Echoes.cs、HeroWeapons.cs、HeroBase.cs、Rogue.cs、AllyBrain.cs、Damageable.cs、Economy.cs（Kit）、Coop.cs、Vortex.cs、SoundAnchor.cs（调用面）。
>
> **结论一句话：三系统主链路是干净的，没有发现 P0。找到 1 个 P1（描述与实现不符）、8 个 P2（边界/观感/待定），另有 12 项已排除。**

---

## Q1 · 词条生效边界 —— 已排除（无问题）

所有武器变化路径都会触发 `Kit.Rebuild()`（清空重建 affixes 去重表 + `version++`）：

| 路径 | 位置 | 结论 |
|---|---|---|
| 开局原生武器 | HeroBase.cs:159 `kit.weapons[0]=Make(...); kit.Rebuild()` | ✓ |
| 玩家拾取（空栏/换手） | Weapons.cs:179 → Economy.cs:39 `Put` | ✓ |
| AI 换装 | Weapons.cs:193 → Economy.cs:41 `PutReplaceLowest` | ✓ |
| 商店购买 | Economy.cs:192 `k.Put(w)` | ✓ |
| 宝箱/击杀奖励 | Economy.cs:301-311 `GiveWeapon` → `AiTake`/`Drop` → 拾取链同上 | ✓ |
| 品质升级 | Economy.cs:30 `weaponStar` setter `version++` | ✓（词条集合不变，仅星级） |
| 公式模拟 | RunSim.cs:170 只动 `baseStar` | ✓（不产生词条） |

`WeaponAffix.Sync` 由 `Rogue.TickHero` **每帧**调用（Rogue.cs:468），按 `affixVersion == kit.version` 判脏、整表重建 `rogue.extra`（WeaponAffix.cs:49-56）。因此**换武器、丢武器（被替换）、三栏轮换、AI 自动换装、宝箱直接发武器，层数都会正确生效/失效，没有漏网路径**。`Kit.AffixStar` 同名取最高星（Economy.cs:35）符合设计。

---

## Q2 · 词条触发管线

**F2-1 · P1 · 描述与实现不符：「共振」实际加成武器技能 V**
- 位置：WeaponAffix.cs:75 `if (kind == "skill" || kind == "vortex" || kind == "anchor" || kind == "wskill") d += V(h, "resonant") / 100f;`
- 词条描述（WeaponAffix.cs:29）写的是「共鸣技能（E）伤害 +{0}%」，但实现把 **`"wskill"`（武器技能 V，冷却 6–9 秒）也计入**。5★ 时 +36%。
- 推演：任意角色拿带「共振」的武器，V 技能伤害凭空 +20%/28%/36%（3/4/5★），与商店里看到的描述不符。玩家按描述构筑时数值会出乎意料。
- 建议修法（二选一，倾向 A）：**A.** 改描述为「共鸣技能（E）与武器技能（V）伤害 +{0}%」（保留行为，行为本身符合协奏「技能循环」主题）；**B.** 从白名单去掉 `"wskill"`。

**F2-2 · P2 · 词条触发范围未在边界表里明确（需用户拍板）**
- 位置：WeaponAffix.OnHit（:81-99）与 DmgBonus（:69-77）只按 kind 白/黑名单处理，没有"来源"概念。
- 现状：声骸幽影命中（"echo"）、卡牌（"burst"/"sword"/"afterimage"/"thunder"/"static"）、团队技能（"team"）、道具（"bomb"）、风墙（"wall"）、雷电场（"thunder"）**都会触发灼烧/引雷/吸血/暴击回能，并吃收割/疾风加成**。
- KB-001（Design/echo_weapon_affix.md）边界表把词条定位为「武器上的 build 胶水、挂在伤害管线上」，但**没有写明触发范围**。防自触发环的排除只有 burn/chain（WeaponAffix.cs:84、Rogue.cs:359），这是正确的。
- 建议：在 echo_weapon_affix.md 补一条边界——要么明确「词条 = 全部玩家伤害被动」（现状），要么限定 `melee/shot/skill/wskill`（更贴合"武器胶水"）。**这影响构筑强度，需要用户拍板，不擅改。**

**F2-3 · P2 · 引雷链击可以暴击**
- 位置：WeaponAffix.cs:96 `h.DealHit(tgt, 1.1f, 10f, 0.5f, Vector3.zero, false, "chain")` —— 未显式传 `canCrit: false`，默认 true。
- 影响：链击会按 12%+暴击率出暴击数字/特效。逻辑无副作用（OnHit 对 chain 早退、Rogue.OnCrit 对 chain 早退）。
- 建议：顺手显式传 `false`（若不想看到"闪电链暴击"），或保持并在注释说明是故意的。

**F2-4 · 已排除 · 格挡/无敌/护盾全额吸收的命中不触发词条**
- HeroBase.cs:728 `if (dealt <= 0f) return 0f;` 在 OnHit/OnCrit 之前，被 `canBeHit` 拒绝（格挡、无敌、声骸格挡 EchoParry）或伤害被护盾全额吸收时 `dealt=0`，词条不触发。与 R2 修复的 `lastHitAccepted` 约定一致。

**F2-5 · P2 · 收割/疾风加成作用于 burn/chain 自身伤害**
- 位置：WeaponAffix.cs:73-74，DmgBonus 不排除 kind。
- 现状：灼烧跳字对 30% 血以下目标增伤、闪避后 2 秒内灼烧跳字提高。与「词条自己造成的伤害不再触发词条」的注释精神不一致（该排除只做了 OnHit，没做 DmgBonus）。
- 建议：DmgBonus 里对 `"burn"`/`"chain"` 也早退（一行改动）。

---

## Q3 · 声骸幽影生命周期

**已排除 · 模板剥离彻底**
- Echoes.cs:104-120 `MakeTemplate`：先删所有 MonoBehaviour（仅保留 WeaponFollow），再删 ParticleSystem，最后删所有组件（仅保留 Transform/Animator/MeshFilter/WeaponFollow 与两类 Renderer）。
- 结论：`Damageable`/`MinionController`/`RayController`/`DriftJelly` 全清。**幽影没有 Damageable → 不吃仇恨、不被 AI 锁定、不参与 Combat.enemies、不会掉声骸/武器/经验**。

**已排除 · 击杀归属正确**
- 幽影伤害走 `h.DealHit(..., "echo")`（Echoes.cs:192/277/359），DealHit 里 `attacker = gameObject` 是**英雄本体**（HeroBase.cs:726）→ 经验/贝币/潮噬等击杀回调都归召唤者。符合设计。

**F3-1 · P2 · 幽影模板跨局不清空**
- 位置：Echoes.cs:53-54 静态 `templates`/`tpl`，无 Reset。
- 影响：同会话重开跑局沿用上一局首个死亡敌人的模型；"EchoTemplates" 根对象常驻内存（一个模板 + 材质）。功能无碍，属卫生问题。

**F3-2 · P2 · 运行时 DestroyImmediate**
- 位置：Echoes.cs:111/117。击杀帧第一次见到某种敌人时执行（父物体未激活，克隆体 Awake 不执行，安全）。集中首杀时有一次开销尖峰。建议改 `Destroy`（延迟帧末）或加注释。

---

## Q4 · AI 规则一致性

**已排除 · ShouldUseEcho 覆盖全部 15 种声骸**
- AllyBrain.cs:69-93，每个 id 都有按用途的分支（治疗看血量、格挡看预警窗口、控制看杂兵数、信使看咏唱/主权），无 id 走默认通用爆发判据。与 Codex 审阅 R3 修复方向一致。

**已排除 · 无死循环**
- 满栏且星级不更高：`AiWants` 返回 false → 不捡（Weapons.cs:186-190）；`WantedLoot` 6 秒拿不到放弃（AllyBrain.cs:42）；掉落 180 秒自毁（Weapons.cs:279）。三人全满栏时地上武器只是自然消失，无卡死路径。

**F4-1 · P2 · 敌人武器对 AI 是死道具**
- 敌人掉落的 6 把武器全是非原生种类（长柄/法杖/重武器/双刃，Weapons.cs:66-71），而 `AiWants` 只认原生种类（Weapons.cs:188）→ **AI 永远不会捡敌人武器**，只能拿第七批普通武器/宝箱/商店里的原生种类。
- 这是"AI 站位出招按原生种类写"的直接结果，不是 bug；建议在 KB-001 或 Weapons.cs 注释里写明，避免后续以为 AI 会换敌人武器。

**已排除 · AI 声骸更换规则前后一致**
- EchoPickup.cs:522 与 AllyBrain.cs:35 语义完全一致：无 → 拿；档位更高 → 换；持有信使 → 永不换。

---

## Q5 · 流派共鸣计数

**已排除 · 层数增删对称**
- Rogue.cs:39-46 `SchoolPts = 卡牌层数 + extra(词条)`，`Tier` 实时查询；词条变化由 Sync 整表重建（见 Q1）。**换掉武器后共鸣层数立即下降、效果立即失效；2/5 层阈值的判定全部实时**，无缓存残留。

**已排除 · 保底卡不计层数**
- `minor` 是 repeat 卡，走 `s.minor++`，不进 `owned/levels`（Rogue.cs:242），SchoolPts 不会把它算成坚守层数。

**已排除 · 诅咒流派无词条**
- 10 条词条只覆盖 5 个正流派（每流派 2 条），无 School.诅咒 词条。

**F5-1 · P2 · 词条触发共鸣无提示**
- `Pick` 在共鸣升档时有 Toast + 特效（Rogue.cs:251-260），但词条层数把流派推过 2/5 层阈值时**没有任何反馈**——效果立刻生效，玩家无感知。
- 建议：`WeaponAffix.Sync` 里对比 tier 前后变化，补一条同款 Toast。

---

## Q6 · 数值静态检查

**F6-1 · P2 · 「共振」的实际强度（同 F2-1）**
- 5★ 36% × 武器技能短冷却（6–9s，含共鸣刻印把 E 复刻成 V 的情形）→ 协奏流的 V 循环收益显著高于描述预期。

**F6-2 · P2 · 引雷在高频多段来源下的触发期望 —— 标「待跑局验证」**
- 5★ 概率 14.4%（WeaponAffix.cs:25/35），按每次命中判定。双枪普攻（~4 发/秒）、乱舞 6 段、散射 7 发下，期望触发次数高；一次触发 = 主目标 1.1 + 跳 1 个 1.1（且吃词条/共鸣加成）。
- 单看倍率未失衡；与雷鸣流（闪电链、雷电场）叠加后的整场占比需要跑局数据确认。**不猜。**

**已排除 · 减伤不会叠到免疫**
- HeroBase.cs:263 `dmg.damageTakenMult = Mathf.Max(0.2f, 1f + ...)`，坚壁 + dr 卡 + 坚守Ⅰ + 声骸护卫叠加最多到 0.2 下限（-80%）。

**已排除 · 吸血/回能无自我滚雪球**
- leech/surge 在 OnHit 里对 burn/chain 早退（WeaponAffix.cs:84）→ 灼烧跳字不会回血、闪电链不会回能。

**已排除 · 灼烧总量合理**
- 3 秒 6 跳，总量 1.2 / 1.68 / 2.16 × 攻击力（3/4/5★），与武器技能单发 2.2–3.4 倍相当，作为 15% 触发的被动合理。

---

## 给总工（WorkBuddy）的交接段

1. **F2-1（P1）**：建议改「共振」词条描述为「共鸣技能（E）与武器技能（V）伤害 +{0}%」（一行）。若你判断 V 加成是数值错误，删 `"wskill"` 即可。
2. **F2-5（P2）**：`WeaponAffix.DmgBonus` 对 `burn`/`chain` 早退，一行。
3. **F2-2 需要用户拍板**：词条触发范围（全玩家伤害 vs 仅武器命中）——影响构筑强度，请排入 KB 待议清单。
4. F2-3 / F3-1 / F3-2 / F4-1 / F5-1 都是 P2，可按你的节奏顺手处理；每条都有明确位置和改法。
5. F6-2 建议并入 RUN-001 跑局窗口的观察项（记录引雷触发次数/占比）。

本报告未改任何工程代码。

---

## 追记（2026-10-10 深夜，词条池扩充后的复核）

总工 18:50 把词条池 10→20 条（每流派 4 条：破潮/嗜战、雷涌/惊雷、魅影/攻虚、利刃/余韵、韧战/回壁），`RogueState` 配套字段齐全（Rogue.cs:33-34）。静态复核结论：

1. **新 P1 · 「共振」与「利刃」重叠**：WeaponAffix.cs:87 里 resonant 仍包含 `"wskill"`，而新词条「利刃 skillblade」（:94）专加武器技能 → **V 技能吃双份**（5★ 各 36%，合计 72%）。建议：resonant 白名单删 `"wskill"`，V 加成归「利刃」；描述同步（「利刃」的文案正确，共振的描述也不用改，删实现即可）。
2. **P2 重申（F2-5 未修）**：DmgBonus 仍没有对 burn/chain 早退；新词条 carnage/thundersurge/phantom 也会作用于灼烧跳字与闪电链。「击杀后 3 秒伤害+X%」作用于 DoT 是否想要，请总工定夺；不想的话在 DmgBonus 开头加一行 `if (kind == "burn" || kind == "chain") return 0f;`（注意 opener 的满血判定不会误伤、reaper 对低血 DoT 是想要的，这两条无需排除）。
3. **已排除 · 新词条数值**：opener/exploit 5★ 45% 与 execute 卡 2 级同档、shockwave 期望 ≈+14%/技能命中、phantom 触发苛刻但值高——均合理，无明显失衡项。
4. **设计提示 · 共鸣门槛**：每流派词条 2→4 条后，词条最多贡献 4 层共鸣，1 张卡 + 4 词条即可触发 5 层共鸣Ⅱ。若这是意图（降低共鸣门槛）则 OK；否则留意 2/5 门槛与词条权重的再平衡。
5. 新词条的实际触发情况建议并入下次跑局观察项（F6-2 扩展：引雷/惊雷/嗜战/余韵触发次数）。6. **补记 P2（10/10 深夜再查）**：「余韵」实现（WeaponAffix.cs:95-96）排除了 skill/vortex/anchor/burn/chain，但**没排除 ult/echo/team 等**——武器技能命中后 3 秒内的大招、声骸伤害也会吃 +15~27%。描述写的是「普攻伤害」，严格按描述应只保留 melee/shot/counter 类；若总工认为"3 秒内全输出"更顺手，就改描述。二选一，和共振/利刃同批处理。
7. **KB-006 升级（10/10 19:00，有设计文档背书）**：`Design/echo_weapon_affix.md` 新增词条表已定「利刃 = 武器技能伤害 +20%」（:74），「共振」描述仍是「共鸣技能（E）」（:29 与代码 desc 一致）→ **共振实现里保留 `"wskill"` 与用户刚拍板的设计表直接矛盾**：V 伤害应由利刃独占，建议共振白名单删 `"wskill"`。另：该文档 :32 仍写"现有 10 条词条"，与 :60 的 20 条矛盾，顺手更新（P3 文档）。
8. **KB-006 实施与核验（10/10 20:02，用户授权 DeepSeek 接手）**：四处改动全部落地并编译通过（Assembly-CSharp 20:02:35，0 error CS），按 `tasks/KB-006_核验用例.md` 逐条核验 **全 PASS**：
   - 共振白名单删 `"wskill"`（WeaponAffix.cs DmgBonus）→ 只加 E 及其刻印形态；V 归「利刃」独占，双加成消除；
   - 「余韵」改为普攻白名单 `melee/shot/counter`（不再放行 ult/echo/team/burst 等）；
   - DmgBonus 对 `burn/chain` 早退（词条自身伤害不参与词条增伤，N1/N2/N3 通过）；
   - 20 词条接线、时效字段、惊雷链击排除均回归确认（P4/P5/N3/N4）。
   实测联动：RUN-002 三局全胜，其中贪信使由负转胜（KB-007 等级门叠加生效）。

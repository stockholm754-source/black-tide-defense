# 第八批美术：UI、伤害跳字、特效贴图（GPT 出图，2026-10-09）

**放哪**：`E:\heichai\UI\`（界面、图标、字体）和 `E:\heichai\特效\`（特效贴图），文件名用下面的编号开头（例如 `F01_数字_普通.png`）。一张图一个编号；同一编号想多给几个版本就加 `_a` `_b`。

**两种底色（很重要）**
- **界面、图标、字体、状态字**：透明背景 PNG。
- **特效贴图（V 开头）**：**纯黑背景**（#000000），发光部分越亮越实。游戏里用加色混合，黑色会自动变透明；透明背景反而会出白边。

## 共同风格（每张都放在描述前面）

UI / 字体 / 图标用：
```
Game UI asset for a stylized dark-fantasy action roguelike set on a stormy coast corrupted by a glowing teal "black tide". Hand-painted with crisp clean edges, high readability at small size, strong silhouette, subtle bevel and inner glow, limited palette (deep navy, charcoal, worn bronze, with accent color as stated). Single asset centered, transparent background, no text unless stated, no watermark, no mockup frame.
```

特效贴图用：
```
Game VFX texture for additive blending, pure black background (#000000), the effect itself bright and glowing with soft falloff, no background objects, no text, centered with margin so nothing touches the edges, high contrast, stylized hand-painted look matching a dark-fantasy game.
```

---

## F · 伤害数字字体（最优先）

每张：一行 14 个字形，顺序固定 `0 1 2 3 4 5 6 7 8 9 + - ! ×`，**等宽格子、字形之间留空**，2048×256，透明背景。我按格子切开拼成数字。

| 编号 | 用途 | 描述（接在 UI 共同风格后面，并加「a horizontal strip of 14 evenly spaced glyphs: 0 1 2 3 4 5 6 7 8 9 + - ! ×」） |
|---|---|---|
| F01 | 普通伤害 | `bold chunky game damage numerals, off-white with a thick dark navy outline and a soft drop shadow, slightly italic` |
| F02 | 暴击 | `heavy impactful damage numerals, glowing golden yellow with orange inner gradient, thick dark outline, small sharp spark flecks on the edges, slightly italic and larger feeling` |
| F03 | 受到伤害 | `damage-taken numerals, deep crimson red with darker outline, cracked texture inside the strokes` |
| F04 | 治疗 | `healing numerals, fresh mint green with a soft white glow and dark outline, rounded friendly strokes` |
| F05 | 黑潮 / 声骸 | `numerals filled with glossy black liquid and glowing teal cyan cracks, teal outer glow, dark outline` |
| F06 | 灼烧 | `numerals made of glowing ember orange with tiny flame tips on top, dark outline` |
| F07 | 雷电 | `numerals in electric violet-white with small lightning arcs around them, dark outline` |
| F08 | 冰霜 | `numerals in icy pale blue with frost crystals on the edges, dark outline` |
| F09 | 护盾吸收 | `numerals in steel blue-silver with a hexagonal shield pattern inside the strokes, dark outline` |

## T · 状态字（战斗中弹出的短字）

每个单独一张，512×192，透明背景，**中文**，书法感的游戏字（粗、有力、带描边和发光），颜色按说明。

| 编号 | 字 | 颜色 / 感觉 |
|---|---|---|
| T01 | 暴击 | 金黄带火星，冲击感 |
| T02 | 破绽 | 橙色，带裂纹 |
| T03 | 失衡 | 橙红，碎裂效果 |
| T04 | 打断 | 白金，横向一道斩痕 |
| T05 | 格挡 | 冰蓝，带盾形光 |
| T06 | 完美闪避 | 银白，带残影拖尾 |
| T07 | 反击 | 银白偏金 |
| T08 | 击杀 | 暗红，粗重 |
| T09 | 定身 | 青色，带锁链感 |
| T10 | 冻结 | 冰蓝，结霜 |
| T11 | 燃烧 | 橙红火焰 |
| T12 | 感电 | 紫白电弧 |
| T13 | 共鸣 | 青绿，音波纹 |
| T14 | 超频 | 青色 + 故障 / 电子感 |
| T15 | 护盾 | 钢蓝，六边形 |
| T16 | 免疫 | 灰白 |
| T17 | 连击 | 金色（后面会接数字，例如「连击 ×12」） |
| T18 | 清场 | 金色大字，庆祝感 |

## V · 特效贴图（纯黑背景）

| 编号 | 用途 | 尺寸 | 描述（接在特效共同风格后面） |
|---|---|---|---|
| V01 | 命中星芒 | 512 | `a sharp four-pointed star flare with thin long rays and a hot white core` |
| V02 | 暴击爆点 | 512 | `an explosive impact burst, jagged radial spikes, white-hot core fading to gold` |
| V03 | 重击冲击 | 512 | `a heavy impact shockwave splash, thick radial streaks and debris flecks, white to warm orange` |
| V04 | 刀光（横斩） | 1024×512 | `a wide crescent sword slash arc, sharp bright leading edge fading into a soft trailing smear, white with faint warm tint` |
| V05 | 刀光（竖劈） | 512×1024 | `a vertical crescent slash arc, very sharp bright edge, smeared tail` |
| V06 | 刀光（回旋） | 1024 | `a full circular spinning slash ring with motion smear, bright edge on one side` |
| V07 | 双刃快斩 | 1024×512 | `two thin crossing quick slash streaks, X shape, crisp and fast looking` |
| V08 | 突刺 | 1024×256 | `a long straight piercing thrust streak with a bright pointed tip and speed lines` |
| V09 | 冲击环（干净） | 1024 | `a clean thin glowing shockwave ring, perfectly circular, soft inner falloff` |
| V10 | 冲击环（碎裂） | 1024 | `a cracked jagged shockwave ring made of broken energy shards` |
| V11 | 符文环 | 1024 | `a circular magic rune ring with engraved wave symbols, glowing teal` |
| V12 | 地面裂痕 | 1024 | `a radial ground crack pattern glowing from the center, top-down view` |
| V13 | 黑潮飞溅 | 512 | `a splash of glossy black-teal liquid with glowing cyan droplets, radial` |
| V14 | 火星粒子 | 256 | `a single soft glowing ember spark particle, orange` |
| V15 | 烟雾团 | 512 | `a soft wispy smoke puff, grey-white, soft edges` |
| V16 | 闪电条 | 1024×256 | `a horizontal jagged lightning bolt with branches, violet-white` |
| V17 | 冰晶碎片 | 512 | `a cluster of sharp ice shards bursting outward, pale blue` |
| V18 | 音波环 | 1024 | `concentric sound wave rings with musical ripple distortion, warm gold` |
| V19 | 护盾泡 | 1024 | `a translucent hexagonal energy shield bubble seen from the front, steel blue edges` |
| V20 | 治疗光点 | 512 | `soft green healing sparkles with small plus-shaped glints` |
| V21 | 易伤标记 | 512 | `a cracked target reticle mark, orange, for marking a weakened enemy` |
| V22 | 定身锁链 | 512 | `glowing teal chains forming a ring, binding effect, top-down` |
| V23 | 冻结地面 | 1024 | `a top-down frosted ice patch with crystal edges, pale blue` |
| V24 | 燃烧火焰 | 512 | `a small stylized flame tongue, orange-red, for a burning status` |
| V25 | 预警圈纹理 | 1024 | `a circular danger warning ground marker, thin bright red-orange outer edge with faint inner hatching` |
| V26 | 拾取光柱 | 256×1024 | `a vertical soft light beam rising from the bottom, fading upward` |

### 序列帧（4×4 共 16 帧，2048×2048，纯黑背景，**从左到右、从上到下按时间顺序**）
| 编号 | 用途 | 描述 |
|---|---|---|
| V30 | 命中爆炸 | `a 4x4 sprite sheet of an impact explosion animation, 16 frames: small flash, expanding fireball, breaking into sparks and smoke, fading out` |
| V31 | 黑潮爆开 | `a 4x4 sprite sheet of a black-teal liquid burst animation: droplet gathering, explosive splash with glowing cyan, splatter falling, fading` |
| V32 | 雷击 | `a 4x4 sprite sheet of a lightning strike animation: bolt from top, bright flash, crackling arcs, fading` |
| V33 | 烟尘 | `a 4x4 sprite sheet of a dust cloud puff expanding and dissipating` |
| V34 | 召唤法阵 | `a 4x4 sprite sheet of a teal summoning circle appearing, spinning, flaring and fading` |

## U · 界面

| 编号 | 用途 | 尺寸 | 描述（接在 UI 共同风格后面） |
|---|---|---|---|
| U01 | 技能格边框 | 512 | `a square skill slot frame, worn bronze with teal inlay, empty transparent center` |
| U02 | 技能格边框（就绪发光） | 512 | `same square skill slot frame but glowing bright gold at the edges, ready state` |
| U03 | 血条框 | 1024×128 | `a long horizontal health bar frame, dark metal with bronze caps, empty transparent inside` |
| U04 | Boss 血条框 | 2048×192 | `an ornate long boss health bar frame with a tide-crest emblem in the center top, dark bronze and teal` |
| U05 | 卡牌框·普通 | 768×1024 | `a vertical card frame, simple worn silver border, dark navy inner panel, empty center` |
| U06 | 卡牌框·稀有 | 768×1024 | `a vertical card frame with blue gem accents and glowing blue border` |
| U07 | 卡牌框·传说 | 768×1024 | `an ornate vertical card frame with gold filigree and radiant gold glow` |
| U08 | 卡牌框·诅咒 | 768×1024 | `a vertical card frame with blood red cracked border and dark thorny ornaments` |
| U09 | 提示横幅 | 1536×192 | `a horizontal banner plate for notifications, dark translucent center fading at both ends, thin bronze line accents` |
| U10 | 小地图框 | 1024 | `a circular minimap frame, bronze ring with compass marks` |

### 图标（每个 512×512，透明背景，圆角方形内的图标，统一风格）
- **流派徽记**（6）：U20 潮噬（紫色吞噬漩涡）、U21 雷鸣（金色雷电）、U22 镜步（冰蓝残影）、U23 协奏（绿色音符波）、U24 坚守（琥珀盾牌）、U25 诅咒（血红裂眼）。
- **武器种类**（7）：U30 长刃、U31 重武器、U32 长柄、U33 双刃、U34 法杖、U35 双枪、U36 音感仪（简洁剪影图标）。
- **角色技能**（6）：U40 断焰（火焰突进斩）、U41 焚城（燃烧重斩）、U42 旋流（绿色旋风）、U43 风障（风墙）、U44 锚音（金色音锚）、U45 静默（声波屏障）。
- **道具**（6）：U50 治疗剂（绿色圆瓶）、U51 强化剂（琥珀方瓶）、U52 超频稳定剂（黄铜注射罐）、U53 元素炸弹、U54 净化器、U55 团队护盾共鸣器。
- **资源**（4）：U60 贝币、U61 巢穴钥匙、U62 侵蚀值（黑潮滴）、U63 超频值（青色故障闪电）。
- **声骸图标**（15）：U70–U84，每个是对应敌人的**头像剪影 + 招式符号**：残像、盾卫、潮囊、咏唱者、刺鳐、重甲潮兵、潮刃剑士、珊瑚投矛手、唤潮祭司、浮游水母、潮汐骑、霜潮女巫、雷锚巨兵、湮灭收割者、黑潮信使。

---

## 接入（给我自己）
- 字体：按 14 格切开 → 每个编号一套数字精灵；跳字系统按层选字体。
- 状态字：直接作为图片弹出（替换现在的文字标签）。
- 特效：放进 `Art/VFX/`，导入为纹理，`FX` 里的星芒、爆点、刀光、冲击环换成这些贴图；序列帧用粒子系统的 Texture Sheet Animation 播放。
- 图标：HUD 技能格、选卡界面（流派徽记 + 卡框）、武器栏、声骸栏、商店道具。

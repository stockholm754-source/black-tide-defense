# 第六批美术：区域主题布景（16 个模型，2026-10-09）

**为什么要**：地图分成了三圈（外圈海岸渔村、中圈古城遗迹、内圈黑潮腐地），但现有模型里没有渔村和城墙特有的东西，「渔村」只能靠残屋、帐篷撑；古城墙用的是矮断墙 + 柱子凑的；边界崖石（21 号）偏方块。这批补上每圈的「招牌」道具，以及能拼接的城墙段、崖壁段。

**放哪**：Tripo 导出的 zip 放进 `E:\heichai\模型\`，文件名带编号前缀（例如 `70_city_wall.zip`）。

**优先级**：★ 先做（直接替换现在凑出来的结构）：70 城墙段、71 城门、78 海岸崖壁段、64 晾鱼架、67 破渔船、75 黑潮珊瑚尖塔。其余可以后做。

## Tripo 设置
- 70、71、73、78：6000 面；66、67、68、69、74、75、76、77：4000 面；64、65、72、79：2500 面。
- 智能网格 P2.0、四边面，生成贴图，FBX、2k 贴图，不绑骨骼。

## 共同模板（每件都把这段放在描述前面）

```
Game prop concept for 3D modeling, a single object only, three-quarter top-down view from about 30 degrees above, orthographic, neutral even lighting, no cast shadows, no glow halo, plain light grey background, centered with margin. Stylized hand-painted dark fantasy game art, chunky solid readable shapes with thick parts (no thin ropes, no thin poles, no loose cloth, no floating pieces), worn and weathered, muted desaturated palette with cool grey-blue undertone, a coastal ruin world slowly corrupted by a glossy black "tide" with thin glowing teal cracks (any glow appears only as thin teal #3fe0d0 cracks). Original design, no text.
Object: <描述>
```

## 各件描述

### 外圈 · 海岸渔村
- **64 晾鱼架**（约 4 米宽）★：`a sturdy fishing village drying rack, two thick A-frame wooden trestles joined by heavy horizontal beams, rows of dried fish and a bulky folded fishing net draped over the beams as one solid mass, a woven basket at the foot`
- **65 渔网堆**（约 2 米）：`a pile of heavy folded fishing nets as a solid lumpy mound, a few round cork floats and two wicker fish baskets pressed into it, a coiled thick rope on top`
- **66 码头栈道段**（约 6 米长，可首尾拼接）：`a straight segment of an old wooden pier boardwalk, thick planks on stout square posts, about 6 m long and 2.5 m wide, both short ends cut flat and clean so segments can be joined end to end, a few planks missing, barnacles on the posts`
- **67 破渔船**（约 6 米）★：`a small wooden fishing boat beached and tilted on its side, cracked hull with one big hole, a broken short mast stump, barnacles and dark seaweed on the keel, no sail, no ropes`
- **68 高脚渔屋**（约 6 米）：`a small weathered fisherman's shack on short thick stilts, plank walls, sagging shingle roof with a hole, a small porch with three steps, closed shutters, a lantern hook by the door`
- **69 残破灯塔**（约 12 米高，地标）：`a broken old stone lighthouse, thick round tower narrowing upward, the top lantern room shattered and open, cracked masonry, a small attached keeper's hut at the base, black tide crust creeping up one side with faint teal cracks`

### 中圈 · 古城遗迹
- **70 城墙段**（约 8 米长、5 米高、2 米厚，可左右拼接）★：`a straight segment of a massive ancient city wall, large weathered stone blocks, crenellated battlements along the top with some merlons broken off, a walkway ledge on top, about 8 m long, 5 m tall and 2 m thick, both short ends cut flat and clean so segments can be joined side by side, moss in the joints`
- **71 城门**（约 12 米宽、9 米高）★：`a ruined ancient city gatehouse, a large round stone archway wide enough for a road, flanked by two squat square towers with broken battlements, one tower top partly collapsed, the arch keystone carved with a worn sun emblem, thick and heavy proportions`
- **72 断碑**（约 4 米高）：`a tall ancient stone stele, slightly leaning, the top corner broken off, worn carved relief of waves and a sun, standing on a cracked square base`
- **73 神庙台基**（约 10 米见方）：`a ruined temple platform, a raised square stone base about 1.5 m high with wide steps on one side, the stubs of six broken columns on top, one fallen column lying across the steps, cracked floor slabs`
- **74 持杖石像**（约 7 米高）：`a large standing stone statue of a robed hooded guardian holding a long staff vertically with both hands, staff held tight against the body, one shoulder chipped, weathered and mossy, on a square plinth`

### 内圈 · 黑潮腐地
- **75 黑潮珊瑚尖塔**（约 8 米高）★：`a tall twisted spire of corrupted coral, branching thick lumpy shapes that grow upward to a point, mostly glossy ink-black with thin glowing teal cracks, pale bone-white dead coral patches near the base`
- **76 石化触须拱**（约 6 米）：`a huge petrified tentacle emerging from the ground and arching back into it forming a low arch, thick suckers along its underside, stone-grey turning glossy black at the tips, thin teal cracks`
- **77 被吞没的房屋**（约 6 米）：`a small stone house half swallowed by black tide, glossy ink-black tar-like crystal mass engulfing one side and the roof, the remaining wall and doorway still visible, thin glowing teal cracks in the black mass`

### 通用
- **78 海岸崖壁段**（约 10 米长、5 米高，可左右拼接）★：`a natural rugged sea cliff segment, rounded weathered layered rock with horizontal strata, about 10 m long and 5 m tall, a sloped rubble skirt at the foot, a few small tufts of grass on top ledges, both short ends roughly flat so segments can be placed side by side, organic rounded forms (not blocky)`
- **79 路标灯**（约 3 米）：`a wooden road signpost with two thick arrow-shaped boards pointing different directions, a sturdy iron lantern hanging tight on a short arm (glass lit warm amber), a small cairn of stones at the base`

## 接入方式（给我自己）
- 进 `Art/Map/<编号>_<名字>.fbx`，跑 `BlackTide/Tripo/Setup Map Props (地图道具)` 登记。
- 70 替换 `Ridge(120, wall)` 里的断墙 + 柱子；71 放在城墙和道路的交点（替换巨柱 + 石灯）；78 替换 `Ridge(165)` 和 `Boundary()` 的 21 号崖石；79 放在道路岔口。
- 64–69 进 `Village()`，72–74 进 `Ruins()`，75–77 进 `Blight()`。
- 自发光：69、75、76、77 用 `Tools/extract_emission.py teal`；79 的灯用 ember。

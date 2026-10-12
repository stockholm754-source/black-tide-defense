# 第三批美术：30 个模型（2026-10-08 晚发给用户）

用户正在用 GPT 出图、Tripo 转模型。导出的 zip 应放进 `E:\heichai\模型\`，并带编号前缀（例如 `30_tide_vortex.zip`）。
**接入前先用贴图和剪影核对**（上次 Tripo 自动命名导致把刺鳐废案当成了手枪）。核对方法见 `Tools/` 和 HANDOFF 里「A1 补充」「A3 地图道具」两节。

## 清单与用途

| 编号 | 名字 | 用途 | 绑骨骼 |
|---|---|---|---|
| C1 | 巨浪·重甲潮兵（约 3 米） | 精英怪「巨浪」，替换现在用放大残像顶替的巨浪；用 Skeleton_Warrior 的人形动画 | 人形 + Mixamo |
| C2 | 黑潮信使（约 3.5 米） | 信使 Boss，替换现在用潮汐骑顶替的信使；用 Skeleton_Warrior 的人形动画 | 人形 + Mixamo |
| C3 | 流浪商人 | 商人节点 NPC（M3） | 人形 + Mixamo |
| C4 | 情报探员 | 情报据点 NPC，打完据点 Boss 后出现，给情报和钥匙（M3） | 人形 + Mixamo |
| 26 | 尖桩路障 | 营地布景（概念图早就有了） | 否 |
| 29 | 信使的潮汐长戟（约 3 米） | C2 的武器 | 否 |
| 30 | 黑潮漩涡（约 8 米） | 决胜期的漩涡节点（M5） | 否 |
| 31 | 贝币堆 | 货币掉落（M3） | 否 |
| 32 | 武器匣 | 武器掉落（M3） | 否 |
| 33–38 | 治疗药剂、强化药剂、超频稳定剂、元素炸弹、净化器、团队护盾共鸣器 | 商店和掉落的道具（M3），对应原稿「道具系统设计总表」 | 否 |
| 39–52 | 沉船残骸、巨兽骸骨、珊瑚礁石、断桥、石阶、石灯柱、沉钟、残旗、巨锚、木栅栏、倒塌塔楼、潮池、巨像碎头、补给箱堆 | 地图布景，提升画面层次（沉钟放在「精英巢穴·沉钟」） | 否 |
| 53 | 破碎环形门（约 10 米） | 黑潮之心决战场地的地标（M5） | 否 |

## Tripo 设置

- C1、C2：15000 面；C3、C4：10000 面；全部人形 + Mixamo，导出时打开「导出骨骼」，多视图（正面、背面、右侧）。
- 29：4000 面。30、39、40、42、49、53：5000 面。26、41、43–48、50–52：2500–3000 面。31–38：1500 面。
- 全部智能网格 P2.0、四边面，生成贴图，FBX、2k 贴图。

## 接入方式（给接手的会话）

- C1、C2：照 `TripoSetup.SetupEnemies` 的做法按人形导入，控制器用 `Anim/Boss_Tripo.controller`（Skeleton_Warrior 的人形动画），在 `RunDirector.Spawn` 里替换巨浪、信使的生成。C2 的长戟可以照潮汐骑的斧头那套（`WeaponFollow` + `WeaponFit`）。
- C3、C4：按人形导入，只需要待机动画，摆在商人和据点节点上。
- 道具：放进 `Art/Map/<编号>_<名字>.fbx`，跑菜单 `BlackTide/Tripo/Setup Map Props (地图道具)` 自动登记，代码里用 `SliceAssets.I.MapProp("编号_名字")` 取；布景在 `MapBuilder.Dress` / `Scatter` 里摆。
- 自发光：用 `Tools/extract_emission.py`（teal / crystal / ember / molten），覆盖率低于 0.2% 就不要自发光图。

## 完整提示词

角色模板（附风格定调图和绯烈正面图）：

```
Character concept for 3D modeling. Full body from head to toe, front view, A-pose (arms 45 degrees down, legs slightly apart), orthographic, neutral even studio lighting, no cast shadows, plain light grey background, centered. Stylized hand-painted game art matching the attached reference. No cape, no long hair, no long flowing cloth, no loose straps, clear gap between arms and torso and between the legs. Empty hands. Original design, no existing game IP.
Character: <描述>
```

- C1：an elite black-tide brute about 3 m tall, hunched heavy humanoid build with thick limbs, body of glossy ink-black solidified tide wrapped in a few huge crude slabs of barnacle-crusted bronze armor (shoulders, forearms, shins), teal glowing cracks between the plates, small head sunk between the shoulders with one teal eye slit, oversized fists. Clearly a step between the small husk minion and the Tide Knight boss.
- C2：the Black Tide Messenger, a tall lean boss about 3.5 m, elegant and sinister: tight-fitting armor of dark teal-black shell plates with sharp silhouettes, a high collar, a crown of thin curved coral horns, a smooth faceless mask with a vertical teal glowing slit, long arms. Palette: black, deep teal and pale bone, NO bronze (different from the Tide Knight). Empty hands.
- C3：a wandering merchant, short stout middle-aged man, short hooded jacket (hood down), patched leather apron covered in small pouches, a large rounded backpack with pots and rolled maps strapped on tightly, round spectacles, bushy beard, warm ochre and brown palette with brass details.
- C4：an intelligence scout, lean young woman, fitted dark grey scouting outfit, short shoulder mantle (above the elbow), map case on the hip, spyglass holstered on the belt, short tied-back hair, goggles around the neck, muted grey-green palette with brass details.

道具系列（29–53）的整段提示词见本次对话；要点：沿用场景系列的风格规范、单图一件、3/4 俯视 30°、浅灰底、无投影无光晕、实心粗壮造型，发光只画成青色裂纹。各件描述：
29 信使长戟（侧视横放，约 3 米，壳甲长杆 + 黑潮晶体新月刃）；30 黑潮漩涡（下沉的圆形石盆，底部是凝固的黑潮漩涡纹，边缘黑晶尖刺向内倾）；31 贝币堆（珍珠贝币 + 小布袋）；32 武器匣（深色木 + 铜箍，盖上黄铜星形徽记）；33 治疗药剂（圆瓶绿液）；34 强化药剂（方瓶琥珀橙液 + 铁笼）；35 超频稳定剂（黄铜注射罐 + 浅蓝液窗 + 压力表）；36 元素炸弹（铁球嵌晶体 + 粗引信帽）；37 净化器（黄铜提灯式装置，顶部音叉排气口，透明晶腔里有白色光核）；38 团队护盾共鸣器（金象牙六边形框 + 中央晶盘）；39 沉船残骸（约 10 米，粗木肋骨、藤壶、断桅，无帆无绳）；40 巨兽骸骨（约 8 米肋骨 + 脊椎）；41 珊瑚礁石；42 断桥（一个拱，一端塌了）；43 石阶（约 5 米宽，一侧塌）；44 石灯柱；45 沉钟（约 3 米，侧倒半埋，铜绿、裂缝）；46 残旗（粗木杆 + 贴杆的短厚旗，无飘带）；47 巨锚（斜插地上 + 几节粗链）；48 木栅栏（可拼接）；49 倒塌塔楼（下半截圆塔）；50 潮池（岩石边 + 凝固黑水）；51 巨像碎头（侧躺的兜帽石像头）；52 补给箱堆（三个木箱 + 两个桶）；53 破碎环形门（约 10 米立起的巨大石环，一段掉落，底部长黑晶）。

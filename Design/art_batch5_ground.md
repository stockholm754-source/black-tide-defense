# 第五批美术：地面贴图（5 张，GPT 生成图片即可，不用 Tripo）

**为什么要**：现在一张石板贴图铺满整张 220 米地图，玩家看到的是一个无限大的广场，内圈外圈一模一样。新的地面着色器按位置混合 5 种地面（广场石板已有），这 5 张是缺的。

**放哪**：生成后放进 `E:\heichai\场景\`，文件名用下面的编号（G1_dirt.png 这样）。我会做无缝检查、缩放、提取发光，再接进着色器。

## 共同要求（每张都贴在描述前面）

```
Seamless tileable ground texture, perfectly top-down orthographic view (camera pointing straight down, no perspective), flat even lighting with no cast shadows and no highlights, stylized hand-painted game texture with realistic material detail, muted desaturated palette, cool grey-blue undertone, matches a dark fantasy coastal ruin world corrupted by a glowing teal "black tide". Square 2048x2048, edges must tile seamlessly on all four sides, no objects larger than 1/8 of the image, no text, no border, no vignette.
```

## 5 张

| 编号 | 用在哪 | 描述（接在共同要求后面） |
|---|---|---|
| **G1_dirt** | 野外的大部分地面 | `Dry packed earth with fine cracks, scattered small pebbles and grit, a few dead grass blades, grey-brown soil, subtle color variation` |
| **G2_moss** | 和泥土交错，打破单调 | `Dark damp soil partly covered by low moss and short sparse grass tufts, muted olive and grey-green, small stones peeking through` |
| **G3_sand** | 地图边缘的海岸 | `Wet coastal sand with small broken shells, pebbles and thin strands of dark seaweed, cold grey-beige, faint ripple marks` |
| **G4_cobble** | 节点之间的道路 | `Old worn cobblestone path, small rounded stones of uneven size set in packed dirt, some stones missing, dark grey with moss in the gaps` |
| **G5_tide** | 黑潮覆盖的地面（随侵蚀扩散） | `Ground corrupted by a black glossy tar-like substance, solidified ink-black waves and bubbles, thin glowing cyan-teal veins and cracks running through it (bright teal #3fe0d0 only inside the cracks), the rest very dark` |

## 注意
- 一定要正俯视、无透视、无投影，否则平铺后会看出接缝和方向感。
- G5 的青色只出现在裂缝里，其余部分要很暗；我会把青色提取成发光图。
- 如果某张生成出来明显有接缝，可以在提示词里加 `seamless pattern, repeating texture`，或者让 GPT「把这张做成可无缝平铺」再生成一次。

# 黑潮防线 · 协作任务板

> **当前负责人（2026-10-11 14:50）**：用户在 Claude Code 会话明确指定 **Claude 接任总工程师**（原话"接总工程师，直接动手"），负责目标口径、优先级、整合与验收。**本轮同时确定方向变更：爽快割草 > 长战斗张力**（原案按鸣潮底子设计，未考虑 AI 动作短板），详见 `Design/割草方向重做.md`。Codex / DeepSeek / WorkBuddy 既有产出全部保留；此前 Codex 的 `Design/engineering_plan.md` 三批计划里，与"延长战斗、强化张力"相关的部分按新口径重排，交互修复与地图品质两批仍然有效。
> **⚠️ 本会话工具限制**：`claude mcp list` 为空，Unity MCP 桥未接入 → 只能写代码 / batchmode 编译 / 离线计算 / 读 CSV，**看不到画面、不能开 Play 自检**。需要画面验收的任务请让用户开编辑器或授权跑局窗口。
> **上一轮（2026-10-10 19:25）**：用户在 Codex 线程指定 Codex 接任总工程师；DeepSeek 继续验证 / 审查与已领取的 DS-005。开发顺序见 `Design/engineering_plan.md`。
> **最新环境核验（19:24，只读）**：程序集时间已为 19:18:28，反射 fresh，`RunWindowTool.Run` 存在。团结进程 PID 23696 带 batchmode；下方 19:00 的旧进程异常是历史记录，不能用于判断当前进程。当前跑局的成功 / 失败以 DS-005 实际日志与报告为准。
> 任务书放在 `tasks/`，结果回报放在 `reports/`。每个任务必须带验收标准；
> 完成后在下方表里更新状态，并在 HANDOFF.md 顶部记一笔。
> 规矩：不开 Play 的活随时做；开 Play 的活攒窗口，必须用户同意。
>
> ⚠️ **环境核验（10/10 19:00，DeepSeek）**：团结编辑器进程 15872（18:50:22 启动）**无主窗口（MainWindowHandle=0）、启动后 CPU 累计仅 7.6s、Editor.log 停在 15:19** → 僵尸态，不会编译。KB-001b 源码（18:50）尚未编译（程序集仍 14:40）。请总工确认编辑器窗口状态；若确实没有窗口，请用户从任务管理器结束该进程，再按 HANDOFF 的重开命令重启编辑器。**僵尸存活期间不要开第二个编辑器。**

## 分工

| 角色 | 职责 |
|---|---|
| Claude（总工程师，2026-10-11 起） | 目标口径与优先级、割草方向落地、核心玩法整合、验收；负责申请跑局窗口并向用户要真人反馈 |
| Codex（上一任总工） | 动态索敌 / 手柄 / 动作自然度（CX-003/004/005 已落地，保留）；特效与 UI 接线；地图品质 KB-002/004 |
| WorkBuddy（协作开发） | 领取边界明确的功能、场景与素材任务；已完成产出保留 |
| DeepSeek（副总工程师） | 验证/审查 + 独立优化流：headless 批量验证、代码逻辑审查、数值复算、性能采样、表现力协作验收（改动走任务板分配） |
| 用户（Stockholm） | 美术素材（GPT 生图、Tripo 模型）、Play 许可、真人手感试玩、重大取舍拍板 |

## 任务表

| CX-006 | 重做反馈音效 + 菜单/探索/战斗/终局音乐 + 音量设置 | Codex（用户授权） | ✅ 完成：34 音效、3 首音乐、群战限流/关键声音优先级/死亡距离声像、天气统一音量；71 项 PASS、真实输出峰值 0.47521、E 盘构建 0 错误；菜单设置可见，用户 Esc 停止后不再操控 | `reports/CX-006_音效与音乐.md`、`Captures/audio_20261011/` |

| 编号 | 任务 | 负责 | 状态 | 产出 |
|---|---|---|---|---|
| CX-007 | 霓虹白光/频闪与技能形状修正（接续 DS-026/027） | Codex（用户授权继续） | 🟡 处理中：已备份；占用 FX/SlashFx/CyberGrade/两份霓虹 shader，Arsenal 仅表现调用；请暂勿同改 | `tasks/CX-007_霓虹与技能形状.md`、`Captures/vfx_CX007/` |
| CL-001 | 割草方向第一轮：黑潮涌浪 + 节点密度 + 6 张割草卡 + 动画剔除 + 仪表 + 4 项交互/事件修复 + 文档同步 | Claude（总工） | 🟡 **代码完成、batchmode 编译 0 错误**（程序集 14:45 / 539648B，新符号 UTF-16 复核在包内）；**运行时零验证**——没开 Play、无帧时间、无手感结论。等跑局窗口定阈值 | `Design/割草方向重做.md`、`Run/Tide.cs`、`Run/Arsenal.cs`、`Run/Rogue.cs`、`Run/RunDirector.cs`、`MinionController.cs`、`CharacterAnim.cs`、`Coop.cs`、`Growth/Economy/Echoes/Weapons.cs`、`RogueUI.cs`、`HeroBase.cs`、`NodeLabels.cs`、`TutorialUI.cs`、`Editor/BuildGame.cs`、README/HANDOFF |
| CL-001w | 跑局窗口：三路线各一局，用 `alive_now/alive_tide/fps_avg/fps_min` 定 Tide 阈值、涌浪经验系数、黑潮王血量 | Claude（总工） | ⏳ **等用户授权**（功耗规矩） | 预计 `Captures/runs/`、`reports/RUN-003*` |
| CL-002 | 特效接线给割草卡 + 同屏高密度时的反馈层 | Claude（总工） | ✅ **代码完成、编译 0 错误**（15:12 / 543232B，方法名与贴图名逐个在 dll 内核实）：导入 8 张贴图（`VfxImport` 第三批）、`FX.cs` 加 7 个接口、三档命中强度落到收割/脉冲/地火/符阵/冰面/掉落、`BindChains` 加总量闸、**修掉每杀即顿帧会卡屏的隐患**（限流 0.1s）、飘字加空间合并与「收割 N」。**观感零验证** | `Editor/VfxImport.cs`、`FX.cs`、`Run/Arsenal.cs`、`Run/FrostZone.cs`、`Run/Echoes.cs`、`Run/Weapons.cs`、`Coop.cs`、`DamageText.cs` |
| CL-004 | fodder 潮兵架构（无 Animator + GPU 实例化 + 单管理器批量驱动），把同屏上限从 62 提到几百 | Claude（总工） | ⏳ **等用户拍板要不要做**（架构决策，不是调参）；用户口味已定为「一屏刷掉几百只」，现有架构做不到 | — |
| CL-005 | 第三种刀光/特效风格：**霓虹故障**（`BT_SlashNeon` + `BT_Glitch`，赛博朋克方向，用户指定）；`FX.VfxStyle` 三态 + 控制台 `style 0/1/2` + 主菜单/教学同步 | Claude（总工） | 🟡 代码完成（DeepSeek 16:30 复验：编译 0 错误、程序集 548864B、dll 内含 `VfxStyle`/`GlitchShader`/`NextStyle`）；**尚未在任务板登记**（本行为 DeepSeek 补记）；观感零验证 | `Shaders/BT_SlashNeon.shader`、`Shaders/BT_Glitch.shader`、`FX.cs`、`SlashFx.cs`、`ConsoleUI.cs`、`MainMenu.cs`、`TutorialUI.cs`、`Arsenal.cs` |
| DS-021 | Claude 割草重做（CL-001/002/005）独立验证 + E 盘重打包收尾 | DeepSeek（例行验证） | ✅ 完成（16:30）：编译一次通过 0 错误；**修掉霓虹风格进不了包的隐患**——两个新 shader 之前不在 Always Included，`BuildGame` 本次 `total=9 added=2` 补入；重打包 E 盘（16:30:57，Succeeded，711 MB，dll 548864B 含 PadHints/GlitchShader/Tide）；顺手删掉任务板重复的 CL-001 行 | `Build/`→`E:\黑潮防线` |
| DS-022 | **修霓虹特效白方块**（用户实测报告）+ 赛博朋克语言补厚 | DeepSeek（用户要求"接手补全"） | ✅ 完成（16:40 重打包）：根因=`BT_Glitch` 用 alpha 当形状掩码，而本项目特效贴图全是 **RGB 无 alpha 的黑底加法图**（alpha 恒 1）→ 整块 quad 加白。改为 **亮度 × alpha 掩码**（两种贴图都成立）；像素级仿真：旧公式 6/6 张贴图 **100% 面积发光、均值 440–546（过曝）** → 新公式 **19–56% 面积、均值 0.4–1.4**。补厚赛博语言：块状错乱 `_Block`、重影 `_Ghost`、通道互换 `_Swap`（参数统一在 `FX.ApplyNeonParams`）。构建日志实证：`BT_Glitch.shader is dirty` → `Compiling shader "BlackTide/Glitch"(vp/fp)` → `Serialized binary data`，**0 个 shader 编译失败**，包内检出 `BlackTide/Glitch`+`BlackTide/SlashNeon`。**观感仍待真人** | `Shaders/BT_Glitch.shader`、`FX.cs`、`E:\黑潮防线` |
| DS-024 | 赛博朋克招牌三件事：**霓虹 / 危险 / 报错**（用户批评"没有霓虹没有危险没有报错也叫赛博朋克？"） | DeepSeek（用户要求，Play 已授权） | ✅ 完成：① 根因=**跑图模式原本没开后处理**（只有竞技场开着）→ 亮的东西永不发光；② 新增 `CyberGrade.cs`：后处理调色（Bloom 阈值 0.55/青色溢光/色散/颗粒/暗角/畸变/饱和）+ 屏幕层（扫描线/CRT 失步带/危险条纹/数据损坏块/**错误代码文字**/红色警报泛光/「危险·立即撤离」闪烁横幅）；③ 灯管化 shader（5 阶色带量化 + 硬白芯 1.15 喂 Bloom + 掩码内警戒红）。实机量化（改前→改后）：**色散彩边 15.1→32.1**、红占比 1.2%→4.2%、近白仍 0.00%（白方块未回归） | `Scripts/CyberGrade.cs`、`Shaders/BT_Glitch.shader`、`Resources/UI/Cyber/` |
| DS-025 | 「特效太单薄」→ 分层大爆炸 + 全局威力/密度倍率 + 剩余 10 张特效收尾导入 | DeepSeek（用户要求"大一点，炸一点"） | ✅ 完成：① `FX.BigBurst`（地裂 V12 + 黑潮爆发 V31 + 双层冲击环 V09/V10 + 朝镜头亮核 V30 + 尘雾 V33 + 碎片/余烬）；② 全局 `FX.Power=1.65` / `FX.Density=1.8`，作用于命中/环/爆/落雷/召唤圈（**不动伤害与预警时序**）；③ 特效素材 **31/31 全入库**（补 V01/V02/V04–V10/V12）；④ 控制台新增 `power <x>` / `density <x>` 实时调参。**同帧左右对比实测**（左 1.0 / 右 1.65）：发光面积 63.7%→78.1%、**高亮面积 29.9%→41.4%（+39%）**、平均亮度 +25% | `FX.cs`、`Run/Rogue.cs`、`Run/Arsenal.cs`、`Editor/VfxImport.cs`、`ConsoleUI.cs` |
| DS-025f | **修复 Claude 音频导入的编译阻塞**（工程级阻塞） | DeepSeek（收尾） | ✅ 完成（编译 0 错误，程序集 19:25:19）：`Editor/AudioImport.cs` 3 处 API 命名空间错误导致 15 条编译错误、整个工程无法编译。用本机 XML 文档定位：`AudioClipLoadType`/`AudioCompressionFormat` 在 **UnityEngine**、`AudioSampleRateSetting` 在 **UnityEditor**、`preloadAudioData` 已迁入 `AudioImporterSampleSettings`。改动带注释可复查 | `Editor/AudioImport.cs` |
| DS-026 | 「颜色单一发白光 + 过头了」修正 | DeepSeek（用户反馈） | ✅ 完成：① 白芯根因=shader 里 `+ white×1.15` 与 `quant × _Color` 全量染色 → 改为**本色亮芯**（`lerp(素材色, 染色, 0.35)`）+ 染色权重降到 0.60，白色只保留 `mask>0.93` 极小一点；② 整体收档：`Power 1.65→1.22`、`Density 1.8→1.25`、Bloom `2.6/0.55→1.35/0.92`、去掉 postExposure 提亮、饱和 28→16、CRT/扫描线/警报红全部减半。实测：**主色相集中度 45.0%→18.9%**、色相种类 8→10、近白 0.00% | `Shaders/BT_Glitch.shader`、`CyberGrade.cs`、`FX.cs` |
| DS-027 | **形状语言差异化**（用户批评"一环一环、没区分、不符合设定、火焰场只是红色圈"） | DeepSeek（用户要求） | ✅ 完成：按流派给不同形状语法，替换掉"万物皆圆环"——**潮噬=不规则地火场**（熔斑+不规则火舌+火星，已接火焰新星）、**协奏=定向扇面声波**（朝释放方向，双层错开，已接共鸣爆炸）、**坚守=六边盾片+脚下六边底**（跟随目标，已接护盾）、**镜步=斜向刀痕**（V04/V05 刀光形，已接残影）、雷鸣=折线闪电（保留）；新增素材 `fan_wave/hex_shield/ember_field`；圆环只保留给真正的环形事件（Boss 环形潮汐/净化/召唤圈/大爆炸冲击波） | `FX.cs`、`Run/Arsenal.cs`、`Run/Echoes.cs`、`Run/Rogue.cs`、`HeroBase.cs`、`Resources/UI/Cyber/` |
| DS-028 | **前期启动包 + 选卡全局暂停**（用户：「前期敌人多血厚、启动太差很煎熬」+「打开 buff 选择时暂停全局，不要打断玩家」） | DeepSeek（用户要求） | ✅ 完成并**实机验证**：① **选卡全局暂停**：`Feel.Pause()`（`Time.timeScale=0`，敌人/弹幕/侵蚀/冷却/时钟全冻），`RogueUI.Update` 声明式驱动 + `CloseOnHurt` 立即解冻；② **前期启动三件套**：开局原生武器 **2★→3★**（3★ 起带词条）、**免费送「连锁打击」Lv1**（普攻溅射=一刀一排，三名队友都给）、**前 3 分钟小兵血量 ×0.65**（6 分钟线性回 1.0；落点在 `MinionController/RayController` 设 maxHp 唯一处，**Boss 本体不受影响**）。**验证证据**：开面板 → `timeScale=0.000 / feelPaused=True`，4 秒真实时间局内只走 **0.33 秒**（= 那一帧延迟）后完全冻结；关面板 → `timeScale=1.000` 恢复；小兵血量实测 **874 = 560×2.4×0.65 精确命中**；开局断言 `weaponStar=3 cleaveLv=1 relief=0.65`。**数值联动提醒**：这是前期唯一改动（中期不变），会影响 CL-001 的 kills/G1 指标基线 | `Feel.cs`、`Run/RogueUI.cs`、`Coop.cs`、`MinionController.cs`、`RayController.cs`、`HeroBase.cs` |
| CX-005 | 近处残血动态索敌、手柄提示补齐、双马达震动 | Codex | ✅ 完成；121 项 PASS / 错误 0，最终 E 盘构建成功；替代 CX-004 粘滞强锁，实体震感待试玩。**是本轮密度上调的前置依赖，未改动** | `reports/CX-005_动态索敌与手柄反馈.md`、`Captures/combat_pad_20261011/` |
| DS-020 | CX-005 收尾：手柄**按键图标**提示（Codex 只做了文字）+ 独立验证 | DeepSeek（用户要求"帮他收尾"） | ✅ 完成（13:10 重打包 E 盘）：独立复现 Codex 包内 dll SHA256 一致（8A78…0819 / 523264B）；新增 16 个按键图标 + 5 处图标芯片行（HUD/选卡/商店/教学/主菜单），文字提示全保留不打断 121 项断言 | `Scripts/PadHints.cs`、`Resources/UI/Pad/`、HUD/RogueUI/ShopUI/TutorialUI/MainMenu |
| CX-003 | 人物动作自然度接入：步幅标定、走跑/四向闪避、攻击位移与接触修正 | Codex | 第一轮完成；23 组完整回归及最终短测，34 项 PASS；E 盘包更新，长按手感待真人试玩 | `reports/CX-003_动作自然度调试.md`、`Captures/motion_20261011/` |
| CX-004 | 自动强锁追击与 Xbox / XInput 手柄，含选卡/商店导航 | Codex | 接入完成；93 项 PASS，动作 23 组复验，E 盘包更新；实体手柄手感待试玩，游戏保留打开 | `reports/CX-004_强锁追击与手柄.md`、`Captures/lock_gamepad_20261011/` |
| DS-001 | 武器/声骸/词条三系统逻辑审查 | DeepSeek | ✅ 完成（10/10 晚，1×P1 + 8×P2 + 12 项已排除） | `reports/DS-001_report.md` |
| DS-002 | RUN-001 跑局静态预判（7.4 回放 + 判定表） | DeepSeek | ✅ 完成（10/10 晚，回放误差 0.006 级） | `reports/DS-002_report.md` |
| DS-003 | 跑局数据管线（CSV → 判定表自动出结论） | DeepSeek | ✅ 完成（10/10 深夜，自检通过） | `collab/tools/run_report.py` |
| DS-002b | 批处理编译验证 + 无声退出诊断（WorkBuddy 19:24 委托） | DeepSeek | ✅ 结案（编译 0 错误；无声退出根因=僵尸编辑器持锁多实例冲突；可用命令行已记录） | `reports/DS-002b_编译验证与诊断.md` |
| DS-005 | 无头跑局工具 + RUN-001 执行（用户 10/10 19:13 授权自动跑） | DeepSeek | ✅ 完成（3/3 局产出，每局 ~3.1 分钟，工具与坑记录见 RUN-001_analysis.md，注入文件已清） | `Editor/RunWindowTool.cs`、`Scripts/Run/RunWindowBootstrap.cs` |
| DS-006 | 开局交互与玩家表达审查 | DeepSeek | ✅ 完成（2×P1 + 3×P2 + 7 已排除 + 赐福点边界说明） | `reports/DS-006_report.md` |
| CX-001 | 接手开发计划与首条玩家路径打磨 | Codex | 计划已落盘；实现待 DS-005 释放工程写入窗口 | `Design/engineering_plan.md` |
| KB-001 | 《声骸 vs 武器词条定位》设计说明 | WorkBuddy | ✅ 完成 | `Design/echo_weapon_affix.md` |
| KB-001b | 词条池扩充 10 → 20 条（用户已拍板方向） | WorkBuddy | ✅ 代码完成；✅ 已编译（19:18 无 error CS，DS-005 编译会话） | `Run/WeaponAffix.cs`、`Run/Rogue.cs` |
| KB-006 | 词条扩充复核意见（共振/利刃重叠 + DmgBonus 排除） | DeepSeek（用户 20:0x 授权接手） | ✅ 已实施+编译+核验全 PASS（追记 8），RUN-002 实测生效 | `reports/DS-001_report.md` 追记 8 |
| KB-002 | 地图品质 P0：道具尺寸/灯光/引导线 | Codex 统筹 | 未开始；从开局代表路线实施 | 截图对比 |
| KB-003 | 特效 backlog：V16/V32 闪电、V19 护盾 | DeepSeek（用户要求收尾） | ✅ 已接 V16/V32/V19 并编译通过（21:16）；其余 15 张待排（见 DS-009） | `FX.cs`、`Rogue.cs`、`HeroBase.cs`、`HeroWeapons.cs`、`Editor/VfxImport.cs` |
| KB-004 | 修珊瑚礁近景挡视线（遮挡淡出/尺寸分带） | Codex | 未开始 | — |
| KB-005 | U62 侵蚀 / U63 超频图标接 HUD 顶部 | DeepSeek（用户要求收尾） | ✅ 完成（21:16：图标+数值接入 HUD 右上，编译通过） | `HUD.cs` |
| RUN-001 | 跑局窗口：经验 7.4 + 黑潮王 +25% 验证 | DeepSeek / DS-005 | ✅ 已跑 3/3（稳健胜/激进胜/贪信使负·3团灭）判定未通过；分析见 RUN-001_analysis.md | `Captures/runs/run_20261010_193817/194131/194444` |
| DS-007 | 公式模拟 vs 实测一致性校验（7.4 下 RunSim 复现 RUN-001） | DeepSeek | ✅ 完成（模拟复现贪信使死亡螺旋，可信；两个偏差已记录；附决战时长结构性偏短的结论） | `reports/DS-007_report.md` |
| DS-009 | 未完工作/未接线清单（特效/UI/模型/交互盘查） | DeepSeek | ✅ 完成（18 张特效未接入、U62/U63 未接线、T01-18 未生成、Batch4 已接线、无悬空引用） | `reports/DS-009_未完工作清单.md` |
| DS-010 | 收尾接线：V16/V32/V19 特效 + U62/U63 HUD + 词条文档修正 | DeepSeek（用户要求收尾） | ✅ 完成（21:16 编译 0 错误，程序集 21:16:29） | `Editor/VfxImport.cs`、`FX.cs`、`HUD.cs` 等 |
| DS-011 | 引导教学面板（H 键）：基础操作/战斗/技能位置/升级/当前技能动态 | DeepSeek（用户要求） | ✅ 完成（21:31 编译通过，程序集实证含 TutorialUI；画面效果待用户 Play 确认） | `Scripts/TutorialUI.cs`、`RunDirector.cs`、`HeroBase.cs` |
| DS-012 | 打包完整游戏（Windows 64 + 图标 + 启动说明） | DeepSeek（用户要求） | ✅ 完成（21:36 构建成功，errors=0，699 MB，图标已设置；见 HANDOFF） | `Editor/BuildGame.cs`、`Art/GameIcon.png`、`Build/黑潮防线-win64/` |
| DS-013 | 主菜单（复用第八批 UI：黑潮之心背景 + 标题 + 开始/教学/退出，随包重出） | DeepSeek（用户要求） | ✅ 完成（21:42 重打包成功，包内 level0=MainMenu、level1=Run，验证通过） | `Scripts/MainMenu.cs`、`Editor/MenuSetup.cs`、`Resources/Menu/` |
| DS-014 | 修复打包后特效丢失（着色器未进包）+ 特效风格全局开关（写实/美漫，F6 与主菜单按钮，持久化） | DeepSeek（用户反馈+要求） | ✅ 完成（21:52 重打包：7 个着色器进常驻列表；开关已接；待用户实机验收） | `Editor/BuildGame.cs`、`FX.cs`、`HeroWeapons.cs`、`SliceGame.cs`、`MainMenu.cs`、`TutorialUI.cs` |
| DS-015 | 索敌软锁定 + 追击 + 锁定标记 + 局内控制台/修改器（` 或 F7，20 条命令） | DeepSeek（用户反馈+要求） | ✅ 完成（22:25 重打包，包内程序集实测含 ConsoleUI/SoftLock；待用户实机验收） | `HeroBase.cs`、`FX.cs`、`ConsoleUI.cs`、`RunDirector.cs`、`SliceGame.cs`、`TutorialUI.cs` |
| DS-017 | 局势仪表盘（压力账本 + 节点价值标签 + 开局三句话，不教路线只教规则语言） | DeepSeek（用户方向确认后实施） | ✅ 完成（23:09 重打包，包内实测含 NodeLabels；设计稿 Design/局势仪表盘设计.md；待实机验收） | `HUD.cs`、`Run/NodeLabels.cs`、`TutorialUI.cs`、`Resources/UI/NodeIcons/` |
| DS-018a | 黑潮王召唤残像群（割草/群战爽点）+ 残像死亡治疗微粒 | DeepSeek（用户要求，设计已确认） | ✅ 完成（23:25 重打包，包内实测；二阶段起每 24–32s 从边缘召 6 只、三阶段 8 只，血×0.55/攻×1.2，死亡近身回 4% 血） | `BossController.cs`、`MinionController.cs`、`SliceGame.cs` |
| DS-018b | 黑潮追猎者机制（侵蚀驱动刷新追击怪，攻×1.2/血×0.8 封顶巨浪 + 净化首波 + Toast + V34 召唤圈） | DeepSeek（用户要求，设计已确认） | ✅ 完成（23:40 重打包：≥30% 起刷 90s/波×2、55% 70s×4、75% 55s×5 巨浪、场上限 10、净化立即一波） | `Run/Hunters.cs`、`MinionController.cs`、`SliceGame.cs`、`RunDirector.cs` |
| DS-018c | 特效弹幕化接线（V31/V34/V11/V18/V24/V25/V30/V33/V13/V20 导入 + V34 召唤圈/V31 黑潮爆发接线） | DeepSeek | ✅ 完成（23:40：10 张入库共 13 张；V34=追猎者出场、V31=潮爆；余下 8 张待逐个接线） | `Editor/VfxImport.cs`、`FX.cs`、`Run/Rogue.cs` |
| DS-019a | 肉鸽节奏重构第一刀：三选一门槛减半（一局 12–16 次）+ 弹幕军火库 3 张新卡（环绕飞刃/追击弹幕/火焰新星，各 5 级） | DeepSeek（用户要求，设计已确认） | ✅ 完成（23:33 重打包，包内实证含 Arsenal；待实机验收） | `Run/Arsenal.cs`、`Run/Rogue.cs`、`HeroBase.cs`、`SliceGame.cs` |
| DS-019b | 弹幕军火库余下 3 卡（雷霆之网/漩涡/连锁打击）+ 6 张旗舰老卡升 5 级 | DeepSeek | ✅ 完成（23:40 重打包；潮爆/连锁闪电/雷电场/共鸣爆炸/残影/利剑出鞘 全部 5 级，效果代码同步扩展） | `Run/Arsenal.cs`、`Run/Rogue.cs`、`HeroBase.cs`、`Echoes.cs` |
| KB-007 | 贪信使路线信使战节奏修正（方案 A 等级前置 / B 路线经验系数，RunSim 扫参） | DeepSeek（用户 20:0x 授权接手） | ✅ 已实施（RouteScore 等级门 ≥6）+ 编译 + RUN-002 贪信使由负转胜 | `tasks/KB-007_实现点位.md` |
| CX-002 | 构筑选择体验（staged RogueUI） | Codex | 落地前预审意见已备：`reports/CX-002_staged预审.md`（BlocksPlayerInput 需 HeroBase 一行同步；F2 X 双义待实机验证拍板） | `../codex-build-choice-20261010/` |
| RUN-002 | 复验窗口：KB-006 + KB-007 修完后三条路线各一局 | DeepSeek | ✅ 已跑 3/3 全胜（稳健/激进/贪信使），但单局方差大（决战 90–585s），分析见 RUN-002_analysis.md | `Captures/runs/run_20261010_200318/200608/201010` |
| KB-008 | 决战时长定档：SovereignSweep 扫黑潮王血 + 每路线 3 局多局验证（判定改中位/分位口径） | DeepSeek（验证流） | ⏹ 用户叫停（20:16）。扫描已完成（×3 模拟 ~180-190s、×4 ~230-250s），聚合工具 `collab/tools/run_summary.py` 保留，随时可续 | `reports/` |

## 等用户素材

- 55 渊鳐、57 潮涌图腾（Tripo 模型）
- T01–T18 状态字（GPT 出图，清单见 `Design/art_batch8_ui_vfx.md`）

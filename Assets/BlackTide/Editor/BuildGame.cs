using System.IO;
using UnityEditor;
using UnityEngine;

namespace BlackTide
{
    // DS-012 打包（用户要求 2026-10-10）：完整 Windows 64 位独立游戏 + 图标 + 启动说明。
    // 用法：-batchmode -quit -projectPath <工程> -executeMethod BlackTide.BuildGame.Build -logFile <log>
    public static class BuildGame
    {
        // DS-014：把 Assets/BlackTide/Shaders 下全部着色器加进 GraphicsSettings 常驻列表（运行时 Shader.Find 依赖）
        static void IncludeAllGameShaders()
        {
            var gs = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(gs);
            var prop = so.FindProperty("m_AlwaysIncludedShaders");
            var files = Directory.GetFiles(Path.GetFullPath(Path.Combine(Application.dataPath, "BlackTide", "Shaders")), "*.shader");
            int added = 0;
            foreach (var f in files)
            {
                string p = "Assets/BlackTide/Shaders/" + Path.GetFileName(f);
                bool have = false;
                for (int i = 0; i < prop.arraySize; i++)
                {
                    var e = prop.GetArrayElementAtIndex(i);
                    if (e.objectReferenceValue != null && AssetDatabase.GetAssetPath(e.objectReferenceValue) == p) { have = true; break; }
                }
                if (have) continue;
                prop.arraySize++;
                prop.GetArrayElementAtIndex(prop.arraySize - 1).objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>(p);
                added++;
            }
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log($"BUILD_SHADERS_INCLUDED total={files.Length} added={added}");
        }

        public static void Build()
        {
            PlayerSettings.productName = "黑潮防线";
            PlayerSettings.companyName = "Stockholm";
            PlayerSettings.resizableWindow = true;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/BlackTide/Art/GameIcon.png");
            if (icon != null)
            {
                PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Standalone, new[] { icon });
                Debug.Log("BUILD_ICON_SET " + icon.width + "x" + icon.height);
            }
            else Debug.LogWarning("BUILD_ICON_MISSING");

            IncludeAllGameShaders();   // DS-014：运行时 Shader.Find 的着色器必须进 Always Included，否则打包后特效/轮廓/地面全丢

            string outDir = @"E:\黑潮防线";   // 2026-10-10：包挪到 E 盘（用户要求），以后重打包直接落这里
            Directory.CreateDirectory(outDir);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/BlackTide/Scenes/MainMenu.unity", "Assets/BlackTide/Scenes/BlackTide_Run.unity" },   // 主菜单 → 完整一局（开放地图）
                locationPathName = Path.Combine(outDir, "黑潮防线.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"BUILD_RESULT {report.summary.result} size={report.summary.totalSize} errors={report.summary.totalErrors} path={outDir}");
            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                File.Copy(Path.Combine(Application.dataPath,"BlackTide","AudioCredits.md"),Path.Combine(outDir,"音频素材来源.md"),true);
                File.WriteAllText(Path.Combine(outDir, "启动说明.txt"),
                    $"黑潮防线 v0.1（{System.DateTime.Now:yyyy-MM-dd HH:mm} 构建）\r\n\r\n双击「黑潮防线.exe」开始游戏。\r\n攻击自动朝最近一圈中血量最少的敌人追近，每次起手重新选择，不依赖鼠标方向。\r\n侵蚀越高，从黑潮里持续涌向你的敌人越多——杀得越勤，经验和增益选择来得越快。\r\n\r\n键鼠操作：\r\n  WASD 移动 · Ctrl 慢走 · 左键 普攻 · 空格/右键/Shift 闪避\r\n  Tab 换人 · 1/2 接力 · F 拾取/救援 · F2 重开\r\n  X/滚轮 切武器 · G 商店 · H 教学面板\r\n  E 技能 · V 武器技能 · R 声骸 · Q 大招（能量满）\r\n  B 增益三选一：面板里用 1/2/3 或点击选择（此时 1/2 不当接力键）；挨打会自动收起，待选不丢\r\n\r\nXbox / XInput 手柄：\r\n  左摇杆移动（轻推慢走）· X 普攻 · B 闪避 · A 交互/按住救援\r\n  RB 技能 · RT 大招 · LB 声骸 · Y 武器技能\r\n  LT+X 换人 · LT+Y 换武器 · LT+LB/RB 接力 · LT+Menu 重开\r\n  十字键左右选道具、上使用、下团队道具 · LT+上 净世之器\r\n  LS 按下选卡 · View 地图 · Menu 教学\r\n  界面摇杆/十字键导航 · A 确认 · B 返回 · 教学摇杆阅读\r\n  命中/重击/受击/完美闪避有双马达震动；菜单/失焦/断连自动停止\r\n\r\n首次进入按 H 或 Menu 查看完整教学。",
                    new System.Text.UTF8Encoding(true));
                Debug.Log("BUILD_README_WRITTEN");
                File.AppendAllText(Path.Combine(outDir,"启动说明.txt"),"\r\n\r\n声音设置：主菜单右侧，或局内 H / Menu 教学面板底部。点击音量按钮循环档位，自动保存。手柄左右选择音量项、A 确认，右摇杆阅读教学。\r\n菜单/探索、战斗、Boss 音乐自动渐变；音频素材来源见「音频素材来源.md」。\r\n",new System.Text.UTF8Encoding(true));
            }
            else EditorApplication.Exit(1);
        }
    }
}

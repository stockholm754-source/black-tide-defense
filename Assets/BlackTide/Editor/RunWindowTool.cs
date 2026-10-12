using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlackTide.EditorTools
{
    // DS-005 无头跑局工具（DeepSeek 副总工 2026-10-10）
    // 用法（不要加 -quit，本工具跑完自己退出）：
    //   Tuanjie.exe -batchmode -projectPath <工程> -executeMethod BlackTide.EditorTools.RunWindowTool.Run -logFile <log>
    // 策略由 RunWindowBootstrap 从 collab/tools/run_strategy.txt 读（域重载后重放）。
    public static class RunWindowTool
    {
        const double GuardSec = 900.0;     // 真实时间护栏：15 分钟没结束判失败退出
        const double EnterGuardSec = 90.0; // 进不了 Play（编译错误等）90 秒判失败退出

        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/BlackTide/Scenes/BlackTide_Run.unity", OpenSceneMode.Single);
            if (!scene.IsValid()) { Debug.LogError("[RunWindow] open scene failed"); EditorApplication.Exit(1); return; }
            SessionState.SetBool("RunWindow.done", false);
            SessionState.SetFloat("RunWindow.enterAt", (float)EditorApplication.timeSinceStartup);
            EditorApplication.update += Poll;   // v2：先进 Play 前注册（静态方法委托可跨域重载存活；即使不存活，外部看门狗负责收尾）
            Debug.Log("[RunWindow] playmode requested");
            EditorApplication.EnterPlaymode();
        }

        static void Poll()
        {
            float enterAt = SessionState.GetFloat("RunWindow.enterAt", 0f);
            double t = EditorApplication.timeSinceStartup;
            var rd = RunDirector.I;
            if (EditorApplication.isPlaying)
            {
                if (rd != null)
                {
                    if (rd.ended)
                    {
                        Debug.Log($"[RunWindow] run ended victory={rd.victory} minutes={rd.RunMinutes:0.00} wipes={rd.wipes}");
                        SessionState.SetBool("RunWindow.done", true);
                        EditorApplication.ExitPlaymode();
                        return;
                    }
                    if (!HeroBase.Autopilot && t - enterAt > 2.0)
                    {
                        HeroBase.Autopilot = true;
                        Feel.SetBaseScale(8f);
                        Debug.Log("[RunWindow] autopilot on, speed x8");
                    }
                }
                if (t - enterAt > GuardSec)
                {
                    Debug.LogError("[RunWindow] timeout: run did not end in 15 min");
                    SessionState.SetBool("RunWindow.done", true);
                    EditorApplication.ExitPlaymode();
                    return;
                }
            }
            else
            {
                if (SessionState.GetBool("RunWindow.done", false))
                {
                    Debug.Log("[RunWindow] exiting editor");
                    EditorApplication.Exit(0);
                    return;
                }
                if (t - enterAt > EnterGuardSec)
                {
                    Debug.LogError("[RunWindow] failed to enter playmode in 90s (compile errors?)");
                    EditorApplication.Exit(1);
                    return;
                }
            }
        }
    }
}

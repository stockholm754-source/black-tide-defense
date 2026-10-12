using System;
using System.IO;
using UnityEngine;

namespace BlackTide
{
    // DS-005 无头跑局（DeepSeek 副总工 2026-10-10，v2）：
    // 批处理跑局前从 collab/tools/run_strategy.txt 读配置（第 1 行策略、第 2 行倍速），
    // 在场景加载前应用策略（进 Play 域重载会把静态字段重置），并创建 RunWindowCtl 做运行时注入。
    // 文件不存在时完全无操作，不影响正常游戏。
    public static class RunWindowBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            try
            {
                string p = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "collab", "tools", "run_strategy.txt"));
                if (!File.Exists(p)) return;
                var lines = File.ReadAllLines(p);
                var s = (lines.Length > 0 ? lines[0] : "").Trim();
                if (s.Length == 0) return;
                RunDirector.strategy = (RunDirector.Strategy)Enum.Parse(typeof(RunDirector.Strategy), s);
                Debug.Log("[RunWindow] strategy from file: " + s);
                new GameObject("RunWindowCtl").AddComponent<RunWindowCtl>();
            }
            catch (System.Exception ex) { Debug.LogWarning("[RunWindow] bootstrap: " + ex.Message); }
        }
    }

    // 运行时注入与结束标记：Start 在所有 Awake 之后跑（此时 RunDirector.Awake 已把倍速重置为 1），
    // 注入自动代打 + 倍速；每帧监视 RunDirector.ended，结束后写 run_finished.txt 给外部看门狗收尾。
    public class RunWindowCtl : MonoBehaviour
    {
        bool markerWritten;

        void Start()
        {
            try
            {
                string p = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "collab", "tools", "run_strategy.txt"));
                var lines = File.ReadAllLines(p);
                float speed = lines.Length > 1 && float.TryParse(lines[1].Trim(), out var s) && s > 0f ? s : 8f;
                HeroBase.Autopilot = true;
                Feel.SetBaseScale(speed);
                Debug.Log($"[RunWindow] ctl: autopilot=true speed=x{speed}");
            }
            catch (System.Exception ex) { Debug.LogWarning("[RunWindow] ctl: " + ex.Message); }
        }

        void Update()
        {
            var rd = RunDirector.I;
            if (rd == null || !rd.ended || markerWritten) return;
            markerWritten = true;
            try
            {
                string p = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "collab", "tools", "run_finished.txt"));
                File.WriteAllText(p, $"victory={rd.victory} minutes={rd.RunMinutes:0.00} at={DateTime.Now:HH:mm:ss}");
                Debug.Log("[RunWindow] marker written: " + p);
            }
            catch (System.Exception ex) { Debug.LogWarning("[RunWindow] marker: " + ex.Message); }
            Invoke(nameof(TryQuit), 5f);   // 给 CSV/结算落盘留时间后尝试优雅退出
        }

        void TryQuit() { Application.Quit(); }
    }
}

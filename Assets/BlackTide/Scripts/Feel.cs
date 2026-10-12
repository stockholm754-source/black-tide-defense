using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlackTide
{
    // 打击反馈中枢：卡肉（顿帧）、镜头震动、后处理脉冲、音效
    public class Feel : MonoBehaviour
    {
        public static Feel I;

        float stopUntil;
        float stopScale = 1f;
        // 反馈专用时钟：平时等于真实时间；录像时每帧固定 1/30 秒，保证卡肉时长在录像里也准确
        public static float Clock;
        public static float DT => Recorder.Active ? 1f / 30f : Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        public float trauma;
        Volume hitVolume, hurtVolume, chantVolume;
        float hitPulse, hurtPulse;
        public float chantLevel;

        void Awake()
        {
            I = this;
            Clock = 0f;
            GameAudio.Ensure();
        }

        // 基础倍速：开放地图加速跑局（F4）时 > 1；顿帧结束后恢复到它。加速时不做顿帧
        public static float BaseScale = 1f;
        public static void SetBaseScale(float s)
        {
            BaseScale = s;
            if (Paused) return;                                   // DS-028：暂停期间不许被倍速/顿帧解冻
            if (I == null || I.stopUntil <= 0f) Time.timeScale = s;
        }

        // ---------------- DS-028 全局暂停（选卡等模态） ----------------
        // 由 RogueUI 声明式驱动：Time.timeScale = 0 → 敌人 AI / 弹幕 / 侵蚀 / 冷却全部冻结，
        // 玩家输入本来就被 HeroBase 的模态分支挡住；Clock 也冻结，避免 Toast 在暂停期间被吃掉。
        public static bool Paused;
        public static void Pause(bool on)
        {
            if (Paused == on) return;
            Paused = on;
            if (I) { I.stopUntil = 0f; I.stopScale = 1f; }
            Time.timeScale = on ? 0f : BaseScale;
        }

        // 顿帧：时间几乎停住 dur 秒（真实时间），叠加时取更长的
        public void HitStop(float dur, float scale = 0.02f)
        {
            if (Paused) return;                                   // 暂停时不受顿帧影响
            if (BaseScale > 1.01f) return;
            float until = Clock + dur;
            if (until > stopUntil) stopUntil = until;
            stopScale = Mathf.Min(stopScale, scale);
            Time.timeScale = stopScale;
        }

        public bool Stopped => stopUntil > 0f;

        void Update()
        {
            if (Paused) { Time.timeScale = 0f; return; }          // DS-028：全局暂停——时钟、顿帧、脉冲全部冻住
            Clock += DT;
            if (stopUntil > 0f && Clock >= stopUntil)
            {
                stopUntil = 0f;
                stopScale = 1f;
                Time.timeScale = BaseScale;
            }
            float dt = DT;
            trauma = Mathf.Max(0f, trauma - dt * 1.7f);
            hitPulse = Mathf.Max(0f, hitPulse - dt * 4.5f);
            hurtPulse = Mathf.Max(0f, hurtPulse - dt * 2.2f);
            if (hitVolume) hitVolume.weight = hitPulse;
            if (hurtVolume) hurtVolume.weight = hurtPulse;
            if (chantVolume) chantVolume.weight = Mathf.MoveTowards(chantVolume.weight, chantLevel, dt * 2f);
        }

        public void Shake(float t) { trauma = Mathf.Clamp01(trauma + t); }
        public void PulseHit(float w) { hitPulse = Mathf.Max(hitPulse, w); }
        public void PulseHurt(float w) { hurtPulse = Mathf.Max(hurtPulse, w); }
        public void SetVolumes(Volume hit, Volume hurt, Volume chant) { hitVolume = hit; hurtVolume = hurt; chantVolume = chant; }

        public void Sfx(string n, float vol = 1f, float pitchVar = 0.06f, float pitch = 1f)
        {
            GameAudio.Sfx(n, vol, pitchVar, pitch);
        }

        public void SfxRandom(string prefix, int count, float vol = 1f, float pv = 0.08f)
        {
            Sfx(prefix + "_" + Random.Range(1, count + 1), vol, pv);
        }
        public void SfxAt(string n, Vector3 position, float vol=1f, float pitchVar=.06f, float pitch=1f)
        { GameAudio.Ensure().PlayAt(n,position,vol,pitchVar,pitch); }

        public void Loop(string n, float vol)
        {
            GameAudio.Ensure().Loop(n, vol);
        }

        public void StopLoop() { if(GameAudio.I) GameAudio.I.StopLoop(); }

        void OnDisable() { StopLoop(); Paused = false; Time.timeScale = 1f; BaseScale = 1f; if(I==this) I=null; }
    }
}

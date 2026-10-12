using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // Animator 包装：状态名 = 动画片段名，直接交叉淡入，不依赖连线
    public class CharacterAnim : MonoBehaviour
    {
        public Animator animator;
        public Animator mirror;   // 同步播放的隐形参考模型（武器跟随用）
        public string current;
        readonly Dictionary<string, float> lengths = new Dictionary<string, float>();
        readonly Dictionary<string, float> gaitSpeeds = new Dictionary<string, float>();
        float gaitPhase;
        float gaitRate = 1f;
        bool walking;
        public float FootHeight { get; private set; } = 0.12f;
        public bool IsLocomotion => current != null && (current.StartsWith("Running") || current.StartsWith("Walking"));

        // cullOffscreen：敌人用。割草密度下的主要 CPU 开销是 Animator 驱动骨骼，离屏就不必再算。
        // 角色必须保持 AlwaysAnimate——FootPlantIK（HeroBase:160）和武器跟随每帧采样骨骼位置。
        public void Init(Animator a, bool cullOffscreen = false)
        {
            animator = a;
            animator.applyRootMotion = false;
            animator.cullingMode = cullOffscreen ? AnimatorCullingMode.CullCompletely : AnimatorCullingMode.AlwaysAnimate;
            foreach (var c in a.runtimeAnimatorController.animationClips) lengths[c.name] = c.length;
        }

        public void CalibrateLocomotion()
        {
            // 只对三位英雄标定，避免每次刷怪都采样数百次 Animator。
            if (animator.isHuman)
            {
                CalibrateGait("Walking_A", 2.6f);
                CalibrateGait("Running_A", 5.6f);
                CalibrateGait("Running_B", 5.6f);
            }
        }

        public float Length(string s) => lengths.TryGetValue(s, out var l) ? l : 1f;
        public bool Has(string s) => lengths.ContainsKey(s) && animator.HasState(0, Animator.StringToHash(s));

        void CalibrateGait(string clip, float fallback)
        {
            if (!Has(clip)) return;
            const int samples = 64;
            var l = new Vector3[samples + 1]; var r = new Vector3[samples + 1];
            float minY = float.MaxValue;
            Vector3 forward = transform.forward;
            for (int i = 0; i <= samples; i++)
            {
                animator.Play(clip, 0, i / (float)samples);
                animator.Update(0f);
                l[i] = animator.GetBoneTransform(HumanBodyBones.LeftFoot).position - transform.position;
                r[i] = animator.GetBoneTransform(HumanBodyBones.RightFoot).position - transform.position;
                minY = Mathf.Min(minY, l[i].y, r[i].y);
            }
            var speeds = new List<float>();
            float dt = Length(clip) / samples;
            for (int i = 1; i < samples; i++)
            {
                float vl = -Vector3.Dot(l[i + 1] - l[i - 1], forward) / (2f * dt);
                float vr = -Vector3.Dot(r[i + 1] - r[i - 1], forward) / (2f * dt);
                if (l[i].y < minY + 0.09f && vl > 0.3f) speeds.Add(vl);
                if (r[i].y < minY + 0.09f && vr > 0.3f) speeds.Add(vr);
            }
            speeds.Sort();
            float speed = speeds.Count >= 4 ? Mathf.Clamp(speeds[speeds.Count / 2], 1f, 9f) : fallback;
            gaitSpeeds[clip] = speed;
            FootHeight = Mathf.Clamp(minY, 0.05f, 0.3f);
            Debug.Log($"GAIT_CALIBRATION hero={name} clip={clip} mps={speed:F3} footY={minY:F3} contacts={speeds.Count}");
        }

        public void Locomotion(string idle, string run, float metresPerSecond)
        {
            if (metresPerSecond < 0.12f) { Loop(idle, 1f, 0.12f); return; }
            if (IsLocomotion)
            {
                var info = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
                gaitPhase = Mathf.Repeat(info.normalizedTime, 1f);
            }
            // 滞回避免速度在走跑边界波动时反复切换；两个循环以同一支撑阶段接续。
            walking = metresPerSecond < (walking ? 3.6f : 3.1f);
            string clip = walking && Has("Walking_A") ? "Walking_A" : run;
            float reference = gaitSpeeds.TryGetValue(clip, out var v) ? v : 5.6f;
            float rate = Mathf.Clamp(metresPerSecond / reference, 0.55f, 2.4f);
            if (current == clip)
            {
                gaitRate = Mathf.Lerp(gaitRate, rate, 1f - Mathf.Exp(-Time.deltaTime * 22f));
                Speed = gaitRate;
            }
            else
            {
                bool fromGait = IsLocomotion;
                // 不把快走倍率带到大步幅跑步，否则切换头几帧会突然甩腿。
                gaitRate = rate;
                Play(clip, fromGait ? 0.08f : 0.12f, gaitRate, gaitPhase);
            }
        }

        public void Play(string s, float fade = 0.08f, float speed = 1f, float startNorm = 0f)
        {
            if (IsLocomotion)
            {
                var info = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
                gaitPhase = Mathf.Repeat(info.normalizedTime, 1f);
            }
            current = s;
            animator.speed = speed;
            animator.CrossFadeInFixedTime(s, fade, 0, startNorm * Length(s));
            if (mirror) { mirror.speed = speed; mirror.CrossFadeInFixedTime(s, fade, 0, startNorm * Length(s)); }
        }

        public void Loop(string s, float speed = 1f, float fade = 0.15f)
        {
            if (current == s) { animator.speed = speed; if (mirror) mirror.speed = speed; return; }
            Play(s, fade, speed);
        }

        public float Speed { set { animator.speed = value; if (mirror) mirror.speed = value; } }
    }
}

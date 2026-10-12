using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BlackTide
{
    // 黑潮氛围（2026-10-09 打磨，用户要求「黑潮地加雨、雾，增加压迫感和沉浸感」）。
    // 强度 = max(玩家深入黑潮覆盖范围的程度（进入后 25 米内 0 → 1），侵蚀压力（侵蚀 45% 以后全图小雨，满侵蚀 0.3））。
    // 随强度渐变：雨幕 + 地面水花、全局雾变浓变青黑、贴地低雾、黑青孢子上升、太阳变暗变冷、画面暗角加重去饱和、
    // 深入后每 10–24 秒一次远处闪电（照亮全场）和延迟的闷雷。雨声、雷声用代码合成，不需要音频素材。
    public class Atmosphere : MonoBehaviour
    {
        public static Atmosphere I;
        public float level;                  // 当前强度（平滑后）
        ParticleSystem rain, splash, groundFog, spores;
        Light sun, lamp;
        float baseSunInt, baseFog;
        Color baseSunCol, baseFogCol;
        Volume vol;
        AudioSource rainSrc, thunderSrc;
        float nextBolt = -1f, boltT = -9f, thunderAt = -1f;
        static readonly Vector3 Wind = new Vector3(-2.2f, 0f, 1.1f);

        public void Build()
        {
            I = this;
            var A = SliceAssets.I;
            var add = FX.NewMat(A.softDot, true, Color.white, 1.2f);
            var alpha = FX.NewMat(A.softDot, false, Color.white, 1f);

            // 雨：拉长的粒子，从玩家头顶 20 米的方块里落下，带一点风
            rain = PS("Rain", add, 3000);
            { var m = rain.main; m.startLifetime = 0.95f; m.startSpeed = 0f; m.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.06f); m.startColor = new Color(0.6f, 0.86f, 0.92f, 0.4f); }
            { var s = rain.shape; s.shapeType = ParticleSystemShapeType.Box; s.scale = new Vector3(48f, 1f, 48f); s.position = new Vector3(0f, 20f, 4f); }
            { var v = rain.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World; v.x = Wind.x; v.y = -23f; v.z = Wind.z; }
            { var r = rain.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 1f; }

            // 地面水花：玩家周围随机溅起
            splash = PS("Splash", add, 400);
            { var m = splash.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.3f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f); m.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f); m.gravityModifier = 1.2f; m.startColor = new Color(0.65f, 0.9f, 0.95f, 0.6f); }
            { var s = splash.shape; s.shapeType = ParticleSystemShapeType.Box; s.scale = new Vector3(30f, 30f, 0.05f); s.rotation = new Vector3(-90f, 0f, 0f); s.position = new Vector3(0f, 0.05f, 2f); s.randomDirectionAmount = 0.45f; }

            // 贴地低雾：大而淡的雾团，顺着风慢慢漂
            groundFog = PS("GroundFog", alpha, 140);
            { var m = groundFog.main; m.startLifetime = new ParticleSystem.MinMaxCurve(9f, 14f); m.startSpeed = 0f; m.startSize = new ParticleSystem.MinMaxCurve(7f, 13f); m.startColor = new Color(0.34f, 0.47f, 0.5f, 0.24f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f); }
            { var s = groundFog.shape; s.shapeType = ParticleSystemShapeType.Box; s.scale = new Vector3(56f, 1.2f, 56f); s.position = new Vector3(0f, 0.9f, 4f); }
            { var v = groundFog.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World; v.x = Wind.x * 0.25f; v.y = 0f; v.z = Wind.z * 0.25f; }
            FadeInOut(groundFog);

            // 黑青孢子：从地面缓缓升起
            spores = PS("Spores", alpha, 300);
            { var m = spores.main; m.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f); m.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);
              m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.03f, 0.07f, 0.08f, 0.85f), new Color(0.2f, 0.75f, 0.72f, 0.7f)); m.gravityModifier = -0.03f; }
            { var s = spores.shape; s.shapeType = ParticleSystemShapeType.Box; s.scale = new Vector3(36f, 36f, 0.2f); s.position = new Vector3(0f, 0.1f, 3f); s.rotation = new Vector3(-90f, 0f, 0f); }
            { var n = spores.noise; n.enabled = true; n.strength = 0.4f; n.frequency = 0.3f; }
            FadeInOut(spores);

            // 画面：暗角加重、去饱和、对比略增
            var go = new GameObject("TideAtmoVolume");
            go.transform.SetParent(transform, false);
            vol = go.AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 0.5f; vol.weight = 0f;
            vol.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            var vg = vol.sharedProfile.Add<Vignette>(true); vg.intensity.Override(0.32f); vg.smoothness.Override(0.5f); vg.color.Override(new Color(0.01f, 0.04f, 0.05f));
            var ca = vol.sharedProfile.Add<ColorAdjustments>(true); ca.saturation.Override(-15f); ca.contrast.Override(6f); ca.colorFilter.Override(new Color(0.86f, 0.97f, 1f));

            // 角色头顶一盏暖色小灯：画面压暗后人物仍然看得清，也和黑潮的冷青形成冷暖对比
            var lg = new GameObject("TideLamp");
            lg.transform.SetParent(transform, false);
            lamp = lg.AddComponent<Light>();
            lamp.type = LightType.Point; lamp.range = 10f; lamp.intensity = 0f; lamp.color = new Color(1f, 0.8f, 0.58f); lamp.shadows = LightShadows.None;

            // 声音
            rainSrc = gameObject.AddComponent<AudioSource>();
            rainSrc.clip = RainClip(); rainSrc.loop = true; rainSrc.volume = 0f; rainSrc.spatialBlend = 0f; rainSrc.Play();
            thunderSrc = gameObject.AddComponent<AudioSource>();
            thunderSrc.clip = ThunderClip(); thunderSrc.loop = false; thunderSrc.spatialBlend = 0f;
        }

        ParticleSystem PS(string name, Material mat, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main; m.maxParticles = max; m.simulationSpace = ParticleSystemSimulationSpace.World; m.playOnAwake = true; m.loop = true;
            var e = ps.emission; e.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static void FadeInOut(ParticleSystem ps)
        {
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.25f), new GradientAlphaKey(1, 0.7f), new GradientAlphaKey(0, 1) });
            col.color = g;
        }

        // 深入程度：玩家在黑潮覆盖范围里走进去多少米（25 米到顶）；全图压力：侵蚀 45% 以后全图小雨
        float Target()
        {
            var p = Combat.player;
            float inside = 0f;
            if (p)
            {
                Vector3 d = p.transform.position - Coop.tideCenter; d.y = 0f;
                inside = Mathf.Clamp01((Coop.coverageRadius - d.magnitude) / 25f);
            }
            float pressure = Mathf.Clamp01((Coop.erosion - 45f) / 55f) * 0.3f * (Coop.coverageRadius >= 0f ? 1f : 0f);
            return Mathf.Max(inside, pressure);
        }

        bool init;
        void Update()
        {
            if (!init)   // 第一帧再记录基础光照和雾（地图搭建在 Start 里会改它们）
            {
                init = true;
                sun = RenderSettings.sun;
                if (sun) { baseSunInt = sun.intensity; baseSunCol = sun.color; }
                baseFog = RenderSettings.fogDensity; baseFogCol = RenderSettings.fogColor;
            }
            float dt = Time.unscaledDeltaTime;
            level = Mathf.MoveTowards(level, Target(), dt / 2.5f);
            var p = Combat.player;
            if (p) transform.position = new Vector3(p.transform.position.x, 0f, p.transform.position.z);

            float k = level;
            SetRate(rain, k * 2600f);
            SetRate(splash, k * 260f);
            SetRate(groundFog, Mathf.Clamp01(k * 1.4f) * 11f);
            SetRate(spores, k * 30f);

            RenderSettings.fogDensity = Mathf.Lerp(baseFog, 0.02f, k);
            RenderSettings.fogColor = Color.Lerp(baseFogCol, new Color(0.04f, 0.075f, 0.085f), k);
            if (Camera.main) Camera.main.backgroundColor = RenderSettings.fogColor;
            vol.weight = k * 0.9f;
            if (p) lamp.transform.position = p.transform.position + Vector3.up * 2.6f;
            lamp.intensity = k * 2.2f;
            rainSrc.volume = k * 0.32f * GameAudio.AmbientGain;
            thunderSrc.volume = (0.55f * k + 0.15f) * GameAudio.AmbientGain;
            thunderSrc.mute = GameAudio.AmbientGain <= 0;

            // 闪电：深入（强度 > 0.45）后每 10–24 秒一次；两下快速的闪亮，0.5–1.4 秒后闷雷
            float flash = 0f;
            if (k > 0.45f)
            {
                if (nextBolt < 0f) nextBolt = Time.unscaledTime + Random.Range(4f, 10f);
                if (Time.unscaledTime >= nextBolt)
                {
                    boltT = Time.unscaledTime;
                    nextBolt = Time.unscaledTime + Random.Range(10f, 24f);
                    thunderAt = Time.unscaledTime + Random.Range(0.5f, 1.4f);
                }
            }
            else nextBolt = -1f;
            float bt = Time.unscaledTime - boltT;
            if (bt < 0.5f) flash = Mathf.Max(Mathf.Clamp01(1f - bt / 0.09f), bt > 0.16f ? Mathf.Clamp01(1f - (bt - 0.16f) / 0.3f) * 0.7f : 0f);
            if (thunderAt > 0f && Time.unscaledTime >= thunderAt) { thunderAt = -1f; thunderSrc.pitch = Random.Range(0.85f, 1.1f); thunderSrc.Play(); }

            if (sun)
            {
                sun.intensity = Mathf.Lerp(baseSunInt, baseSunInt * 0.78f, k) + flash * 4.5f;
                sun.color = Color.Lerp(Color.Lerp(baseSunCol, new Color(0.72f, 0.84f, 1f), k), new Color(0.75f, 1f, 1f), flash);
            }
        }

        static void SetRate(ParticleSystem ps, float r) { var e = ps.emission; e.rateOverTime = r; }

        // ---------------- 合成声音 ----------------
        // 雨：粉红噪声（两级低通的白噪声）+ 随机雨滴的短促滴答；首尾交叉淡化成无缝循环
        static AudioClip RainClip()
        {
            const int rate = 22050; int n = rate * 3;
            var d = new float[n];
            var rng = new System.Random(7);
            float l1 = 0f, l2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float w = (float)(rng.NextDouble() * 2 - 1);
                l1 += 0.18f * (w - l1); l2 += 0.05f * (l1 - l2);
                d[i] = l1 * 0.55f + l2 * 0.9f;
            }
            for (int k = 0; k < 900; k++)   // 雨滴
            {
                int at = rng.Next(n); float f = 1800f + (float)rng.NextDouble() * 3500f, amp = 0.05f + (float)rng.NextDouble() * 0.12f;
                for (int j = 0; j < 260 && at + j < n; j++) d[at + j] += Mathf.Sin(j * f * 2f * Mathf.PI / rate) * amp * Mathf.Exp(-j / 45f);
            }
            int fade = rate / 4;   // 首尾交叉淡化
            for (int i = 0; i < fade; i++) { float t = i / (float)fade; d[i] = d[i] * t + d[n - fade + i] * (1 - t); }
            var c = AudioClip.Create("RainLoop", n - fade, 1, rate, false);
            var outD = new float[n - fade]; System.Array.Copy(d, outD, n - fade);
            c.SetData(outD, 0);
            return c;
        }

        // 闷雷：开头一声带高频的炸裂，接低频布朗噪声的长滚动衰减
        static AudioClip ThunderClip()
        {
            const int rate = 22050; int n = (int)(rate * 4.5f);
            var d = new float[n];
            var rng = new System.Random(11);
            float brown = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float w = (float)(rng.NextDouble() * 2 - 1);
                brown = Mathf.Clamp(brown + w * 0.04f, -1f, 1f) * 0.998f;
                lp += 0.02f * (brown - lp);
                float rumble = lp * 3.2f * (0.7f + 0.3f * Mathf.Sin(t * 7f + Mathf.Sin(t * 2.3f) * 3f));
                float crack = w * Mathf.Exp(-t * 9f) * 0.6f;
                float env = Mathf.Clamp01(t / 0.04f) * Mathf.Exp(-t * 0.9f);
                d[i] = Mathf.Clamp((rumble + crack) * env, -1f, 1f);
            }
            var c = AudioClip.Create("Thunder", n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }
    }
}

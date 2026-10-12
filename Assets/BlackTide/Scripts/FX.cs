using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 粒子与一次性特效：火花、尘土、黑潮碎屑、余烬、命中爆点、冲击波环、弧形斩光
    // 默认画法是发光的写实动漫风（第八批 GPT 贴图 + BT_Slash）。Comic 打开期间的调用走美漫画法（硬边、墨线、抽帧）——
    // 只有个别武器用，由持有者在自己的攻击结算前后开关（见 HeroWeapons）
    public static class FX
    {
        static ParticleSystem sparks, sparksInk, dust, tide, embers, smoke;
        static Material addSoft, alphaSoft;
        static Transform root;
        static Texture2D hitStar, critBurst, hitSheet, ringSoft, ringBroken, groundCrack, tideSheet, boltStrip, strike, bubble;
        // DS-018c：第二批贴图（召唤圈/黑潮爆发/符文环/声波环/火焰/预警圈/爆炸/治疗）
        static Texture2D summonTex, tideBurstTex, runeRing, soundRing, flameTex, warnRing, explosionTex, healSpark;
        // DS-025「大一点、炸一点」：冲击环（干净/碎裂）、地裂、毁伤爆
        static Texture2D shockClean, shockBroken, groundCracks, blastBig;
        // DS-027 形状差异化：定向扇面波（协奏）/ 六边盾（坚守）/ 不规则熔斑（潮噬）/ 刀光形（镜步）
        static Texture2D fanWave, hexShield, emberField, slashH, slashV;
        // 全局威力/密度倍率：只放大**看得见的效果**，不动伤害范围与预警时序（玩法红线）
        public static float Power = 1.22f;     // 尺寸/半径倍率（DS-026：1.65 → 1.22，用户反馈"过头了"）
        public static float Density = 1.25f;   // 粒子数量倍率（DS-026：1.8 → 1.25）
        // CL-003（2026-10-11 特效优先）：第三批贴图（重击爆点/冰片/锁链/冰面/易伤标记/掉落光柱/余烬/烟）
        static Texture2D heavyTex, iceShards, chainsTex, frozenTex, reticleTex, lootColumn, emberTex, smokeTex;
        static float nextTideSplash;
        static Mesh groundQuad;
        static int sweepFlip = 1;
        public static bool Comic;

        // ---------------- 特效风格：三态（CL-005，2026-10-11）----------------
        // 写实（能量面/流动）· 美漫（硬边墨线网点抽帧）· 霓虹故障（灯管亮芯 + 通道错位 + 条带撕裂 + 阶梯闪烁 + 重影）
        // 第三种是用户指定的赛博朋克方向：故障、霓虹、错乱、闪烁。不追求与前两版"协调"，它要的是另一套视觉语法。
        // 唯一保留的红线：红色只用于攻击预警（那是玩家的生命线，不是风格问题）。
        public enum VfxStyle { Realistic, Comic, Neon }
        public static VfxStyle Style = VfxStyle.Realistic;
        public static bool ComicStyle => Style == VfxStyle.Comic;
        // 武器级美漫覆盖（礁石巨锤彩蛋）走 Comic=true，此时霓虹让位；其余情况由 Style 决定
        public static bool Glitch => Style == VfxStyle.Neon && !Comic;

        public static string StyleName(VfxStyle s) => s == VfxStyle.Comic ? "美漫" : s == VfxStyle.Neon ? "霓虹故障" : "写实";
        public static string StyleHint => "特效风格：" + StyleName(Style) + "（F6 循环切换）";

        public static void LoadComicStyle()
        {
            int v = PlayerPrefs.GetInt("vfx_style", 0);
            Style = v == 1 ? VfxStyle.Comic : v == 2 ? VfxStyle.Neon : VfxStyle.Realistic;
            Comic = ComicStyle;
        }
        public static void SetComicStyle(bool on) { SetStyle(on ? VfxStyle.Comic : VfxStyle.Realistic); }
        public static void SetStyle(VfxStyle s)
        {
            Style = s;
            Comic = s == VfxStyle.Comic;                       // 武器级覆盖由调用者自己再设
            PlayerPrefs.SetInt("vfx_style", (int)s);
            PlayerPrefs.Save();
            ApplyStyle();
        }
        public static VfxStyle NextStyle()
        {
            var s = (VfxStyle)(((int)Style + 1) % 3);
            SetStyle(s);
            return s;
        }

        // 粒子系统的材质按风格换：登记每个系统的"常规材质"和对应的霓虹版本，切风格时重新绑定。
        // 霓虹版不重新画形状，只把着色器换成 BlackTide/Glitch——同一张贴图在灯管+故障下重画，形状语言保持不变。
        class PsMat { public ParticleSystem ps; public Material normal; public Material neon; }
        static readonly List<PsMat> psMats = new List<PsMat>();
        static Shader glitchShader;
        public static Shader GlitchShader
        {
            get
            {
                if (!glitchShader) glitchShader = Shader.Find("BlackTide/Glitch");
                return glitchShader;
            }
        }

        static Material NeonVariant(Material src)
        {
            if (!src || !GlitchShader) return null;
            var tex = src.GetTexture("_MainTex");
            if (!tex) return null;
            bool additive = src.HasProperty("_DstBlend") && src.GetFloat("_DstBlend") <= 1.5f;
            if (!additive) return null; // Smoke and dust keep their alpha material and dark volume.
            Color c = src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
            float inten = src.HasProperty("_Intensity") ? src.GetFloat("_Intensity") : 1f;
            var m = new Material(GlitchShader);
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_SrcBlend", 5f);
            m.SetFloat("_DstBlend", additive ? 1f : 10f);
            m.SetColor("_Color", c);
            m.SetFloat("_Intensity", inten);
            // 赛博参数：撕裂中等、色散明显、扫描线轻、闪烁别太疯（太疯等于看不见技能）
            ApplyNeonParams(m);
            return m;
        }

        static void RegisterPs(ParticleSystem ps, Material normal)
        {
            psMats.Add(new PsMat { ps = ps, normal = normal, neon = NeonVariant(normal) });
        }

        // 换风格时重绑所有粒子渲染器（新建的贴片在 NewMat 里自然走霓虹着色器）
        static void ApplyStyle()
        {
            bool neon = Style == VfxStyle.Neon;
            for (int i = 0; i < psMats.Count; i++)
            {
                var e = psMats[i];
                if (!e.ps) continue;
                var r = e.ps.GetComponent<ParticleSystemRenderer>();
                if (!r) continue;
                r.sharedMaterial = neon && e.neon ? e.neon : e.normal;
            }
        }

        static readonly Color InkCol = new Color(0.04f, 0.02f, 0.07f);
        static readonly List<Vector3> cv = new List<Vector3>();
        static readonly List<Color> cc = new List<Color>();
        static readonly List<int> ct = new List<int>();

        // 霓虹统一参数：撕裂/色散/扫描/闪烁/块状错乱/重影/通道互换（赛博朋克那套语言，一处调）
        static void ApplyNeonParams(Material m)
        {
            m.SetFloat("_Glitch", 0f);
            m.SetFloat("_BandH", 11f);
            m.SetFloat("_Rate", 13f);
            m.SetFloat("_Chroma", 0f);
            m.SetFloat("_Scan", 0.08f);
            m.SetFloat("_Flick", 0f);
            m.SetFloat("_Pix", 0.06f);
            m.SetFloat("_Block", 0f);
            m.SetFloat("_Ghost", 0f);
            m.SetFloat("_Swap", 0f);
        }

        public static Material NewMat(Texture tex, bool additive, Color color, float intensity = 1f)
        {
            // 霓虹风格下，所有贴片/地面花都走 BlackTide/Glitch（灯管亮芯 + 通道错位 + 条带撕裂 + 块状错乱）
            bool neon = Glitch && additive && GlitchShader;
            var sh = neon ? GlitchShader : !additive ? Shader.Find("BlackTide/MaskAlpha") : SliceAssets.I.additiveShader;
            var m = new Material(sh);
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_SrcBlend", 5f);
            m.SetFloat("_DstBlend", additive ? 1f : 10f);
            m.SetColor("_Color", color);
            m.SetFloat("_Intensity", intensity);
            if (neon) ApplyNeonParams(m);
            return m;
        }

        static Texture2D Tex(string name) { var t = Resources.Load<Texture2D>("VFX/" + name); return t ? t : SliceAssets.I.softDot; }

        public static void Init()
        {
            LoadComicStyle();   // DS-014：进局按上次选择恢复特效风格
            var selectedStyle=Style;
            // Particle banks must retain a real base material when the saved style starts as Neon.
            Style=VfxStyle.Realistic;
            if(root) Object.Destroy(root.gameObject);
            var retired=new HashSet<Material>();
            foreach(var e in psMats) { if(e.normal) retired.Add(e.normal); if(e.neon) retired.Add(e.neon); }
            foreach(var m in retired) Object.Destroy(m);
            psMats.Clear();
            root = new GameObject("FX").transform;
            addSoft = NewMat(SliceAssets.I.softDot, true, Color.white, 2.2f);
            alphaSoft = NewMat(SliceAssets.I.softDot, false, Color.white, 1f);
            hitStar = Tex("hit_star"); critBurst = Tex("crit_burst"); hitSheet = Tex("hit_burst_sheet"); ringSoft = Tex("ring_soft");
            ringBroken = Resources.Load<Texture2D>("VFX/ring_broken");
            groundCrack = Resources.Load<Texture2D>("VFX/ground_crack");
            tideSheet = Resources.Load<Texture2D>("VFX/tide_splash_sheet");
            boltStrip = Tex("V16_lightning_strip");   // DS-010：闪电带（KB-003）
            strike = Tex("V32_lightning_strike");     // DS-010：落雷（KB-003）
            bubble = Tex("V19_shield_bubble");        // DS-010：护盾泡（KB-003）
            summonTex = Tex("V34_summoning_circle");  // DS-018c：召唤圈
            tideBurstTex = Tex("V31_black_tide_burst");
            runeRing = Tex("V11_wave_rune_ring");
            soundRing = Tex("V18_soundwave_rings");
            flameTex = Tex("V24_burning_flame");
            warnRing = Tex("V25_danger_warning_circle");
            explosionTex = Tex("V30_impact_explosion");
            shockClean = Tex("V09_shockwave_ring_clean");     // DS-025
            shockBroken = Tex("V10_shockwave_ring_broken");
            groundCracks = Tex("V12_ground_cracks");
            blastBig = Tex("V30_impact_explosion");
            fanWave = Resources.Load<Texture2D>("UI/Cyber/fan_wave");       // DS-027
            hexShield = Resources.Load<Texture2D>("UI/Cyber/hex_shield");
            emberField = Resources.Load<Texture2D>("UI/Cyber/ember_field");
            slashH = Tex("V04_horizontal_slash");
            slashV = Tex("V05_vertical_slash");
            healSpark = Tex("V20_healing_sparkles");
            heavyTex = Tex("V03_heavy_impact");       // CL-003：重击 / 收割爆点
            iceShards = Tex("V17_ice_shards");
            chainsTex = Tex("V22_binding_chains");
            frozenTex = Tex("V23_frozen_ground");
            reticleTex = Tex("V21_vulnerability_reticle");
            lootColumn = Tex("V26_loot_light_column");
            emberTex = Tex("V14_ember_particle");
            smokeTex = Tex("V15_smoke_puff");
            nextTideSplash = 0f;

            sparks = MakePS("Sparks", addSoft, true, 2.2f, new Vector2(0.12f, 0.34f), new Vector2(8f, 20f), new Vector2(0.09f, 0.22f), 40f, 400);
            // 美漫火花：不发光、两头尖的硬边短线
            var shard = new Texture2D(16, 16, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    shard.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Abs((x + 0.5f) / 16f - 0.5f) + Mathf.Abs((y + 0.5f) / 16f - 0.5f) <= 0.5f ? 1f : 0f));
            shard.Apply();
            sparksInk = MakePS("SparksInk", NewMat(shard, false, Color.white, 1f), true, 2.2f, new Vector2(0.12f, 0.34f), new Vector2(8f, 20f), new Vector2(0.09f, 0.22f), 40f, 200);

            // 尘土：烟团序列帧（4×4），一边翻滚一边散
            var dustTex = Resources.Load<Texture2D>("VFX/dust_sheet");
            dust = MakePS("Dust", dustTex ? NewMat(dustTex, false, Color.white, 1.8f) : alphaSoft, false, -0.05f, new Vector2(0.5f, 1.1f), new Vector2(0.8f, 3.2f), new Vector2(0.9f, 2.2f), 80f, 200);
            var dm = dust.main; dm.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var dsz = dust.sizeOverLifetime; dsz.enabled = true; dsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.6f));
            var dlv = dust.limitVelocityOverLifetime; dlv.enabled = true; dlv.limit = 0.3f; dlv.dampen = 0.15f;
            if (dustTex) { var tsa = dust.textureSheetAnimation; tsa.enabled = true; tsa.numTilesX = 4; tsa.numTilesY = 4; tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f)); }
            tide = MakePS("TideShards", addSoft, false, -0.4f, new Vector2(0.4f, 0.9f), new Vector2(2f, 7f), new Vector2(0.12f, 0.35f), 60f, 300);
            // CL-003：余烬改用 V14 颗粒贴图（原来是纯亮占位点）；新增烟团（V15）给爆炸和地火收尾用
            embers = MakePS("Embers", emberTex ? NewMat(emberTex, true, Color.white, 2f) : addSoft, false, -0.6f, new Vector2(0.5f, 1.2f), new Vector2(1f, 4f), new Vector2(0.05f, 0.14f), 70f, 300);
            smoke = MakePS("Smoke", smokeTex ? NewMat(smokeTex, false, Color.white, 1.4f) : alphaSoft, false, -0.12f, new Vector2(0.8f, 1.6f), new Vector2(0.6f, 2.4f), new Vector2(1.1f, 2.6f), 40f, 200);
            var sm = smoke.main; sm.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var smsz = smoke.sizeOverLifetime; smsz.enabled = true; smsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 1.8f));

            Style=selectedStyle;
            ApplyStyle();   // CL-005：进局时若上次选的是霓虹，把刚建的粒子系统一起重绑
        }

        static ParticleSystem MakePS(string name, Material mat, bool stretch, float gravity, Vector2 life, Vector2 speed, Vector2 size, float coneAngle, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = coneAngle; sh.radius = 0.08f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (stretch) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.035f; r.lengthScale = 1.2f; }
            RegisterPs(ps, mat);          // CL-005：登记以便切风格时重绑霓虹材质
            ps.Play();
            return ps;
        }

        static void Emit(ParticleSystem ps, Vector3 p, Vector3 dir, int n, Color c)
        {
            if (!ps) return;
            n = Mathf.Max(1, Mathf.RoundToInt(n * Density));   // DS-025：碎片密度倍率
            ps.transform.SetPositionAndRotation(p, Quaternion.LookRotation(dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.up));
            var main = ps.main;
            main.startColor = c;
            ps.Emit(n);
        }

        public static void Sparks(Vector3 p, Vector3 dir, int n, Color c) => Emit(Comic ? sparksInk : sparks, p, dir, n, c);
        public static void Dust(Vector3 p, int n, Color c) => Emit(dust, p + Vector3.up * 0.2f, Vector3.up, n, c);
        public static void Tide(Vector3 p, Vector3 dir, int n)
        {
            Emit(tide, p, dir, n, new Color(0.3f, 0.95f, 0.9f, 1f));
            // 同帧群体命中不为每个目标叠一张白亮片；纹理保留自身黑青色。
            var cam = Camera.main;
            if (Comic || !cam || !tideSheet || n < 7 || Time.time < nextTideSplash) return;
            nextTideSplash = Time.time + 0.09f;
            var splash = Quad("TideSplash", p - cam.transform.forward * 0.12f, cam.transform.rotation, NewMat(tideSheet, true, Color.white, 1.25f));
            float size = n >= 14 ? 2.2f : 1.35f;
            splash.AddComponent<FxAnim>().Setup(FxAnim.Mode.Flip, size, size * 1.2f, 0.44f, Color.white);
        }
        public static void Embers(Vector3 p, int n) => Emit(embers, p, Vector3.up, n, new Color(1f, 0.55f, 0.2f, 1f));

        // 命中爆点（不知道刀路方向的调用点用这个）
        public static void ImpactFlash(Vector3 p, float size, Color c) => Impact(p, Vector3.zero, size, c, false);

        static GameObject Quad(string name, Vector3 p, Quaternion rot, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetPositionAndRotation(p, rot);
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.sharedMaterial = m;
            return go;
        }

        // 命中：很短的星芒亮核 + 顺着刀路的一道切痕（cut 是刀在命中点的运动方向，零向量就不画）；
        // 重击再加一团爆开的序列帧和一圈朝向镜头的冲击环
        public static void Impact(Vector3 p, Vector3 cut, float size, Color c, bool heavy)
        {
            var cam = Camera.main;
            if (!cam) return;
            size *= Power;                                    // DS-025：命中表现放大
            if (Comic) { ComicImpact(p, cut, size, c, heavy, cam); return; }
            Quaternion face = cam.transform.rotation;
            Vector3 near = -cam.transform.forward * 0.3f;
            Color hot = Color.Lerp(c, Color.white, Glitch ? 0.08f : 0.55f);
            var core = Quad("Impact", p + near, face * Quaternion.Euler(0f, 0f, Random.Range(-25f, 25f)), NewMat(hitStar, true, hot, 3.2f));
            core.AddComponent<FxAnim>().Setup(FxAnim.Mode.Flash, size * 0.5f, size * 1.1f, heavy ? 0.12f : 0.09f, hot);
            if (cut.sqrMagnitude > 1e-4f)
            {
                Vector3 s = cam.transform.InverseTransformDirection(cut);
                var st = Quad("ImpactCut", p + near * 1.2f, face * Quaternion.Euler(0f, 0f, Mathf.Atan2(s.y, s.x) * Mathf.Rad2Deg), NewMat(SliceAssets.I.softDot, true, hot, 5f));
                st.AddComponent<FxAnim>().Setup(FxAnim.Mode.Streak, size * 0.8f, size * 2.3f, 0.13f, hot, size * 0.13f);
            }
            if (!heavy) return;
            var burst = Quad("ImpactBurst", p + near * 0.8f, face * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)), NewMat(hitSheet, true, c, 2f));
            burst.AddComponent<FxAnim>().Setup(FxAnim.Mode.Flip, size * 0.8f, size * 1.05f, 0.36f, c);
            var ring = Quad("ImpactRing", p + near, face, NewMat(ringSoft, true, c, 2.4f));
            ring.AddComponent<FxAnim>().Setup(FxAnim.Mode.Flash, size * 0.5f, size * 1.7f, 0.22f, c);
        }

        // 暴击：金色爆点（颜色是贴图自带的，所有角色统一，和暴击数字一个色）
        public static void Crit(Vector3 p, float size)
        {
            var cam = Camera.main;
            if (!cam || Comic) return;
            var go = Quad("Crit", p - cam.transform.forward * 0.5f, cam.transform.rotation * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)), NewMat(critBurst, true, Color.white, 2.2f));
            go.AddComponent<FxAnim>().Setup(FxAnim.Mode.Flash, size * 0.55f, size * 1.15f, 0.16f, Color.white);
        }

        // 美漫命中：一张朝向镜头的硬边贴片——放射的集中线、错版撞色的影子、两层错开角度的锯齿爆炸框（墨线勾边）、顺着刀路的白色切口；
        // 实心的爆炸框只留一两拍（不能一直盖着敌人），线条那层多留一拍；都不渐隐
        static void ComicImpact(Vector3 p, Vector3 cut, float size, Color c, bool heavy, Camera cam)
        {
            Color main, hi, lo, pop, pop2;
            SlashFx.Palette(c, out main, out hi, out lo, out pop, out pop2);
            float r = size * (heavy ? 0.36f : 0.33f);
            cv.Clear(); cc.Clear(); ct.Clear();

            int lines = heavy ? 12 : 8;
            float a0 = Random.value * 6.283f;
            for (int i = 0; i < lines; i++)
            {
                float a = a0 + (i + Random.Range(-0.3f, 0.3f)) * 6.283f / lines;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)), n = new Vector2(-d.y, d.x) * r * 0.085f;
                Tri(d * r * 1.25f + n, d * r * 1.25f - n, d * r * Random.Range(1.9f, 2.8f), i % 3 == 0 ? InkCol : i % 3 == 1 ? hi : pop2);
            }

            if (cut.sqrMagnitude > 1e-4f)
            {
                Vector3 sc = cam.transform.InverseTransformDirection(cut);
                Vector2 d = new Vector2(sc.x, sc.y).normalized, n = new Vector2(-d.y, d.x);
                Diamond(d * r * 2.5f, n * r * 0.24f, InkCol);
                Diamond(d * r * 2.3f, n * r * 0.12f, hi);
            }
            var lineGo = ComicMesh("ComicImpactLines", p - cam.transform.forward * 0.9f, cam.transform.rotation);
            lineGo.AddComponent<FxAnim>().Setup(FxAnim.Mode.Pop, 1f, 1.25f, (heavy ? 3f : 2f) / 12f, Color.white, 0f, true);

            cv.Clear(); cc.Clear(); ct.Clear();
            int pts = heavy ? 12 : 9;
            float[] rad = new float[pts * 2];
            for (int i = 0; i < rad.Length; i++) rad[i] = i % 2 == 0 ? Random.Range(0.82f, 1.08f) : Random.Range(0.42f, 0.55f);
            float turn = 3.1416f / pts;
            Star(new Vector2(0.16f, -0.12f) * r, r * 1.28f, rad, a0, pop);
            Star(Vector2.zero, r * 1.46f, rad, a0 + turn, InkCol);
            Star(Vector2.zero, r * 1.36f, rad, a0 + turn, lo);
            Star(Vector2.zero, r * 1.14f, rad, a0, InkCol);
            Star(Vector2.zero, r, rad, a0, pop2);
            Star(Vector2.zero, r * 0.7f, rad, a0, main);
            Star(Vector2.zero, r * 0.4f, rad, a0, hi);
            var go = ComicMesh("ComicImpact", p - cam.transform.forward * 0.8f, cam.transform.rotation);
            go.AddComponent<FxAnim>().Setup(FxAnim.Mode.Pop, 1f, 1.15f, (heavy ? 2f : 1f) / 12f, Color.white, 0f, true);
        }

        // 美漫地面冲击环：墨边 + 主色 + 一道白线，一拍一拍往外跳，不渐隐
        static void ComicRing(Vector3 p, float r0, float r1, float dur, Color c, float width)
        {
            Color main, hi, lo, pop, pop2;
            SlashFx.Palette(c, out main, out hi, out lo, out pop, out pop2);
            cv.Clear(); cc.Clear(); ct.Clear();
            float w = Mathf.Max(width, 0.14f);
            Band(1f - w * 1.3f, 1.04f, InkCol, true);
            Band(1f - w, 1f, main, true);
            Band(1f - w * 0.6f, 1f - w * 0.22f, hi, true);
            var go = ComicMesh("ComicRing", new Vector3(p.x, 0.08f, p.z), Quaternion.identity);
            var fa = go.AddComponent<FxAnim>();
            fa.stepped = true;
            fa.Setup(FxAnim.Mode.Ring, r0, r1, Mathf.Max(dur, 3f / 12f), Color.white, 0f, true);
        }

        static GameObject ComicMesh(string name, Vector3 p, Quaternion rot)
        {
            var mesh = new Mesh();
            mesh.SetVertices(cv); mesh.SetColors(cc); mesh.SetTriangles(ct, 0);
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(p, rot);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.sharedMaterial = NewMat(Texture2D.whiteTexture, false, Color.white, 1f);
            return go;
        }

        static void Tri(Vector3 a, Vector3 b, Vector3 c, Color col)
        {
            int k = cv.Count;
            cv.Add(a); cv.Add(b); cv.Add(c);
            cc.Add(col); cc.Add(col); cc.Add(col);
            ct.Add(k); ct.Add(k + 1); ct.Add(k + 2);
        }

        static void Star(Vector2 o, float r, float[] rad, float a0, Color col)
        {
            int n = rad.Length;
            for (int i = 0; i < n; i++)
            {
                float a = a0 + i * 6.283f / n, b = a0 + (i + 1) * 6.283f / n;
                Tri(o, o + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * rad[i], o + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * r * rad[(i + 1) % n], col);
            }
        }

        static void Diamond(Vector2 half, Vector2 side, Color col) { Tri(-half, side, half, col); Tri(-half, half, -side, col); }

        // 一圈环带：ground 画在地面（XZ），否则画在朝向镜头的平面（XY）
        static void Band(float r0, float r1, Color col, bool ground)
        {
            const int seg = 40;
            for (int i = 0; i < seg; i++)
            {
                float a = i * 6.283f / seg, b = (i + 1) * 6.283f / seg;
                Vector3 da = ground ? new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) : new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Vector3 db = ground ? new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) : new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0f);
                Tri(da * r0, da * r1, db * r1, col); Tri(da * r0, db * r1, db * r0, col);
            }
        }

        // 出手星芒（AttackTell）：敌人命中前 0.22 秒闪一下，四角星（横竖两道长芒 + 斜向两道短芒），比命中爆点停留久一点
        public static void Glint(Vector3 p, float size, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "Glint";
            go.transform.position = p;
            var cam = Camera.main;
            if (cam) { go.transform.rotation = cam.transform.rotation; go.transform.position += -cam.transform.forward * 0.6f; }
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.sharedMaterial = NewMat(SliceAssets.I.softDot, true, c, 5f);
            go.AddComponent<FxAnim>().Setup(FxAnim.Mode.Flash, size * 0.9f, size * 0.35f, 0.2f, c);
            float[] rot = { 0f, 90f, 45f, -45f };
            float[] len = { 3.4f, 3.4f, 1.5f, 1.5f };
            for (int i = 0; i < 4; i++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.Destroy(s.GetComponent<Collider>());
                s.transform.SetParent(go.transform, false);
                s.transform.localRotation = Quaternion.Euler(0f, 0f, rot[i]);
                s.transform.localScale = new Vector3(len[i], 0.09f, 1f);
                s.GetComponent<MeshRenderer>().sharedMaterial = r.sharedMaterial;
            }
        }

        // 冲击波环：贴地的一圈光，从 r0 扩到 r1（width 只有美漫画法用）
        public static void Ring(Vector3 p, float r0, float r1, float dur, Color c, float width = 0.18f, bool fracture = false)
        {
            r0 *= Power; r1 *= Power; dur *= Mathf.Lerp(1f, 1.22f, Mathf.Clamp01(Power - 1f));   // DS-025：环更大、演出更久
            if (Comic) { ComicRing(p, r0, r1, dur, c, width); return; }
            GroundRing(p, r0, r1, dur, c, ringSoft, 2.6f);
            // 大冲击才出现碎裂外缘与短暂接地裂纹，不让轻击铺满地板。
            if (!fracture || r1 < 2.4f) return;
            if (ringBroken) GroundRing(p, r0 * 0.8f, r1 * 0.92f, dur * 1.15f, c, ringBroken, 1.6f);
            if (groundCrack)
            {
                var crack = Quad("GroundCrack", new Vector3(p.x, 0.06f, p.z), Quaternion.Euler(90f, Random.Range(0f, 360f), 0f), NewMat(groundCrack, true, c, 1.15f));
                crack.AddComponent<FxAnim>().Setup(FxAnim.Mode.Flash, r1 * 1.1f, r1 * 1.35f, Mathf.Max(0.28f, dur * 1.5f), c);
            }
        }

        static void GroundRing(Vector3 p, float r0, float r1, float dur, Color c, Texture texture, float intensity)
        {
            if (!groundQuad)
            {
                // 贴图里环的半径是边长的 0.33，网格做成缩放 1 时环半径正好 1 米
                const float h = 0.5f / 0.33f;
                groundQuad = new Mesh();
                groundQuad.vertices = new[] { new Vector3(-h, 0f, -h), new Vector3(h, 0f, -h), new Vector3(-h, 0f, h), new Vector3(h, 0f, h) };
                groundQuad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
                groundQuad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                groundQuad.RecalculateBounds();
            }
            var go = new GameObject("Ring");
            go.transform.position = new Vector3(p.x, 0.08f, p.z);
            go.AddComponent<MeshFilter>().sharedMesh = groundQuad;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.sharedMaterial = NewMat(texture, true, c, intensity);
            go.AddComponent<FxAnim>().Setup(FxAnim.Mode.Ring, r0, r1, dur, c);
        }

        // 弧形斩光（和刀光叠加，补足挥砍的"面"）。sweep：+1 左到右、-1 右到左、0 不知道方向就每次换一边
        public static void SlashArc(Vector3 center, Quaternion rot, float rIn, float rOut, float ang, float dur, Color c, int sweep = 0)
        {
            if (sweep == 0) { sweep = sweepFlip; sweepFlip = -sweepFlip; }
            SlashFx.Play(center, rot, rIn, rOut, ang, dur, c, sweep);
        }

        // ---------------- DS-010（KB-003）：闪电带 / 落雷 / 护盾泡 ----------------
        // 画一段闪电带：朝镜头的细长贴片，从 a 到 b（配合程序火花使用，火花给亮核、贴片给形状）
        public static void BoltStrip(Vector3 a, Vector3 b, Color c)
        {
            if((b-a).sqrMagnitude<.0025f) return;
            ElementVfx.Lightning(a,b,c,.18f);
        }

        // 一道落雷：竖直贴片从头顶劈到地面 + 地面冲击
        public static void Strike(Vector3 ground, Color c)
        {
            ElementVfx.Lightning(ground+Vector3.up*5.6f,ground+Vector3.up*.08f,c,.26f);
            Sparks(ground+Vector3.up*.15f,Vector3.up,12,c);
        }

        // 护盾泡：跟随目标、朝镜头、按时长淡出
        public static void Bubble(Damageable target, float dur, Color c)
        {
            if (!target || !bubble) return;
            var q = Quad("Bubble", target.Center, Quaternion.identity, NewMat(bubble, true, c, 1.15f));
            q.AddComponent<FxFollow>().Setup(target, dur, c, 2.3f);
        }

        // DS-018c：召唤圈（地面圆环，出场预告）/ 黑潮爆发（地面大爆发贴图）
        public static void SummonCircle(Vector3 p, float dur, Color c)
        {
            if (!summonTex) return;
            var q = Quad("SummonCircle", p + Vector3.up * 0.06f, Quaternion.Euler(90f, 0f, 0f), NewMat(summonTex, true, c, 1.7f));
            float s = 4.6f * Mathf.Lerp(1f, Power, 0.7f);   // 召唤圈跟着放大（但比冲击克制）
            q.transform.localScale = new Vector3(s, s, 1f);
            q.AddComponent<FxAnim>().Setup(FxAnim.Mode.Hold, 0f, 0f, dur, c);
        }

        public static void GroundBurst(Vector3 p, float r, Color c)
        {
            if (!tideBurstTex) return;
            r *= Power;   // DS-025
            var q = Quad("GroundBurst", p + Vector3.up * 0.09f, Quaternion.Euler(90f, 0f, 0f), NewMat(tideBurstTex, true, Color.white, 1.9f));
            q.transform.localScale = new Vector3(r * 2f, r * 2f, 1f);
            q.AddComponent<FxAnim>().Setup(FxAnim.Mode.Flip, r * 2f, r * 2.2f, 0.42f, c);
        }

        // ============ DS-027 形状差异化（不要再所有技能都发芽圆环） ============
        // 每个流派一套"形状语法"，靠几何体与运动方向区分，而不是靠换颜色：
        //   潮噬=不规则熔斑+上升火舌 · 协奏=定向扇面波 · 坚守=六边形盾片 · 镜步=斜向刀痕 · 雷鸣=折线雷电（已有）

        // 潮噬：地火场——不规则熔斑（无圆环）+ 几个不规则位置的火舌 + 上升火星
        public static void FireField(Vector3 p, float radius, float dur, Color c)
        {
            radius=Mathf.Max(.4f,radius);
            Vector3 g=new Vector3(p.x,.06f,p.z);
            // The footprint matches the supplied gameplay radius; the tongues last for the full envelope.
            if(emberField)
            {
                var mat=NewMat(emberField,true,c,.65f);
                var q=Quad("EmberField",g,Quaternion.Euler(90f,0,0),mat);
                q.transform.localScale=new Vector3(radius*2,radius*2,1);
                q.AddComponent<FxAnim>().Setup(FxAnim.Mode.Hold,0,0,dur,c);
            }
            ElementVfx.Spawn(ElementVfx.Shape.Fire,g,Vector3.forward,radius,dur,c);
            Embers(g+Vector3.up*.4f,8);
        }

        // 协奏：定向扇面波前——朝 dir 张开，不是整圆
        public static void SoundFan(Vector3 from, Vector3 dir, float radius, Color c)
        {
            ElementVfx.Spawn(ElementVfx.Shape.Sound,new Vector3(from.x,.1f,from.z),dir,radius,.65f,c);
        }
        public static void SoundPulse(Vector3 center,float radius,Color c)
        {
            ElementVfx.Spawn(ElementVfx.Shape.SoundPulse,new Vector3(center.x,.1f,center.z),Vector3.forward,radius,.65f,c);
        }

        // 坚守：六边形护盾片（朝镜头、轻微脉动）+ 脚下六边光柱
        public static void HexShield(Damageable target, float radius, float dur, Color c)
        {
            if(!target) return;
            ElementVfx.Spawn(ElementVfx.Shape.Shield,target.Center,Vector3.forward,radius,dur,c,target);
        }

        // 镜步：斜向刀痕（用刀光形贴图，不做圆环）
        public static void SlashScar(Vector3 p, Vector3 dir, float size, Color c)
        {
            var cam = Camera.main;
            if (!cam) return;
            var tex = slashH ? slashH : slashV;
            if (!tex) return;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            dir.Normalize();
            var q = Quad("SlashScar", p + Vector3.up * 0.9f - cam.transform.forward * 0.25f, cam.transform.rotation * Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + 60f), NewMat(tex, true, c, 2.4f));
            q.AddComponent<FxAnim>().Setup(FxAnim.Mode.Streak, size * 0.7f, size * 1.9f, 0.20f, c, size * 0.22f);
        }

        // DS-025「大一点、炸一点」：分层大爆炸——地裂（残留）+ 黑潮爆发（贴地）+ 双层冲击环 + 朝镜头亮核 + 尘雾 + 碎片。
        // 纯视觉：不产生伤害、不改预警时序，调用点沿用原来的伤害结算（玩法红线不动）。
        public static void BigBurst(Vector3 p, float radius, Color c, bool heavy = true)
        {
            var cam=Camera.main; if(!cam) return;
            radius=Mathf.Max(.6f,radius);
            var g=new Vector3(p.x,.07f,p.z);
            if(groundCracks)
            {
                var q=Quad("BlastFracture",g,Quaternion.Euler(90,Random.Range(0f,360f),0),NewMat(groundCracks,true,c,.7f));
                q.AddComponent<FxAnim>().Setup(FxAnim.Mode.Flash,radius*1.5f,radius*2,1.2f,c);
            }
            // One short shock front for an actual heavy blast; light fire triggers do not stamp circles.
            if(heavy && shockBroken)
            {
                var q=Quad("BlastPressure",g+Vector3.up*.05f,Quaternion.Euler(90,0,0),NewMat(shockBroken,true,c,1));
                q.AddComponent<FxAnim>().Setup(FxAnim.Mode.Flash,radius*.4f,radius*2,.3f,c);
            }
            if(blastBig)
            {
                var q=Quad("BlastCore",p+Vector3.up*.6f-cam.transform.forward*.15f,cam.transform.rotation,NewMat(blastBig,true,c,1.05f));
                q.AddComponent<FxAnim>().Setup(FxAnim.Mode.Flip,radius*.8f,radius*1.3f,.4f,c);
            }
            Dust(p,heavy?10:5,new Color(.23f,.20f,.18f,.5f));
            Sparks(p+Vector3.up*.6f,Vector3.up,heavy?18:10,c);
            Embers(p,8);
        }

        // DS-015：索敌锁定标记（青色小环悬浮目标头顶，锁新目标时自动替换）
        static FxFollow mark;
        public static void Mark(Damageable t)
        {
            if (mark) { Object.Destroy(mark.gameObject); mark = null; }
            if (!t || !ringSoft) return;
            var q = Quad("LockMark", t.Center, Quaternion.identity, NewMat(ringSoft, true, new Color(0.5f, 0.95f, 0.9f, 0.8f), 1.5f));
            mark = q.AddComponent<FxFollow>();
            mark.Setup(t, 3600f, new Color(0.5f, 0.95f, 0.9f, 0.8f), 1.5f);
        }

        // ---------------- CL-003（特效优先，2026-10-11）：第三批贴图接线 ----------------
        // 割草反馈的三档强度：普通命中用程序火花，收割/重击用 V03 大爆点，全屏脉冲用 V31 地裂爆发。
        // 每档都补烟（V15）收尾，让「刷掉一片」有残留痕迹而不是闪一下就没了。

        public static void Smoke(Vector3 p, int n, Color c) => Emit(smoke, p + Vector3.up * 0.5f, Vector3.up, n, c);

        // 重击 / 收割爆点：V03 朝镜头硬爆 + 地面冲击 + 烟
        public static void HeavyImpact(Vector3 p, float size, Color c)
        {
            var cam = Camera.main;
            if (cam && heavyTex)
            {
                var q = Quad("HeavyImpact", p - cam.transform.forward * 0.35f, cam.transform.rotation, NewMat(heavyTex, true, c, 2.4f));
                q.transform.localScale = new Vector3(size * 2.6f, size * 2.6f, 1f);
                q.AddComponent<FxAnim>().Setup(FxAnim.Mode.Hold, 0f, 0f, 0.3f, c);
            }
            Ring(p, 0.15f, size * 1.4f, 0.3f, c, 0.12f);
            Sparks(p, Vector3.up, 8, c);
            Smoke(p, 4, new Color(0.5f, 0.5f, 0.55f, 0.5f));
        }

        // 冰片迸溅：V17 + 白蓝火花（冻结、冰杖、霜潮女巫命中）
        public static void IceBurst(Vector3 p, Color c)
        {
            var cam = Camera.main;
            if (cam && iceShards)
            {
                var q = Quad("IceBurst", p - cam.transform.forward * 0.3f, cam.transform.rotation * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)), NewMat(iceShards, true, c, 2f));
                q.transform.localScale = new Vector3(1.9f, 1.9f, 1f);
                q.AddComponent<FxAnim>().Setup(FxAnim.Mode.Hold, 0f, 0f, 0.34f, c);
            }
            Sparks(p, Vector3.up, 7, new Color(0.7f, 0.95f, 1f));
        }

        // 定身锁链：V22 地面环 + 跟目标的链影（锚音、漩涡、符阵这一类"不许动"的语言）
        // 必须有总量闸：漩涡一次能定身几十只，不做限制就是一地贴图糊屏。这里限每秒最多约 16 个。
        static float nextChainAt;
        public static void BindChains(Vector3 p, Damageable target, float dur, Color c)
        {
            if (!chainsTex || Time.time < nextChainAt) return;
            nextChainAt = Time.time + 0.06f;
            if (target)
            {
                var f = Quad("Chains", target.Center, Quaternion.identity, NewMat(chainsTex, true, c, 1.6f));
                f.AddComponent<FxFollow>().Setup(target, dur, c, 1.9f);
            }
            var q = Quad("ChainsGround", p + Vector3.up * 0.07f, Quaternion.Euler(90f, 0f, Random.Range(0f, 360f)), NewMat(chainsTex, true, c, 1.4f));
            q.transform.localScale = new Vector3(2.6f, 2.6f, 1f);
            q.AddComponent<FxAnim>().Setup(FxAnim.Mode.Hold, 0f, 0f, dur, c);
        }

        // 冰面：V23 地面贴花（霜潮女巫的场地语言）
        public static void FrozenGround(Vector3 p, float r, Color c, float dur = 4f)
        {
            ElementVfx.Spawn(ElementVfx.Shape.Ice,p,Vector3.forward,r,dur,c);
            if (!frozenTex) return;
            var q = Quad("FrozenGround", p + Vector3.up * 0.05f, Quaternion.Euler(90f, 0f, Random.Range(0f, 360f)), NewMat(frozenTex, false, c, 1.2f));
            q.transform.localScale = new Vector3(r * 2f, r * 2f, 1f);
            q.AddComponent<FxAnim>().Setup(FxAnim.Mode.Hold, 0f, 0f, dur, c);
        }

        // 易伤标记：V21 转环悬在目标头顶（重复标记只刷新，不叠一堆）
        static readonly Dictionary<Damageable, FxFollow> reticles = new Dictionary<Damageable, FxFollow>();
        public static void Vulnerable(Damageable t, float dur)
        {
            if (!t || !reticleTex) return;
            FxFollow old;
            if (reticles.TryGetValue(t, out old) && old) Object.Destroy(old.gameObject);
            var q = Quad("VulnReticle", t.Center, Quaternion.identity, NewMat(reticleTex, true, new Color(1f, 0.55f, 0.3f, 0.9f), 1.8f));
            var f = q.AddComponent<FxFollow>();
            f.Setup(t, dur, new Color(1f, 0.55f, 0.3f, 0.9f), 1.8f);
            reticles[t] = f;
        }

        // 掉落光柱：V26 竖直柱子（武器 / 声骸掉在地上时喊出「这里有东西」）
        public static void LootColumn(Vector3 p, Color c)
        {
            var cam = Camera.main;
            if (!cam || !lootColumn) return;
            var q = Quad("LootColumn", p + Vector3.up * 1.8f - cam.transform.forward * 0.2f, cam.transform.rotation, NewMat(lootColumn, true, c, 1.9f));
            q.transform.localScale = new Vector3(1.5f, 3.8f, 1f);
            q.AddComponent<FxAnim>().Setup(FxAnim.Mode.Hold, 0f, 0f, 6f, c);
            Ring(p, 0.2f, 1.4f, 0.5f, c, 0.1f);
        }

        // =============== CL-006 技能视觉语法：让每张卡有独特主形体 ===============
        // 用户反馈：「地火、爆炸、闪电都是地上一圈一圈，分辨不出什么技能」。根因是我给 12 张割草卡
        // 都用了同一套词汇（Ring + Sparks + 朝镜头贴片）。下面这套补齐正交通道：
        // 形状（斩面 / 火柱 / 符纹 / 立壳 /  filament）、运动（扫 / 升 / 旋 / 撑 / 连）、时序（各自的生命曲线）。
        // 「一圈」只保留给真正的环形攻击（潮爆、共鸣爆炸），其余改主形体。

        // 因果连线：A 引发 B 时画一条细丝，让"链式反应"看得见（弹跳、溅射、连锁、超载）
        public static void Filament(Vector3 a, Vector3 b, Color c, float dur = 0.22f, float width = 0.13f)
        {
            var cam = Camera.main;
            if (!cam) return;
            Vector3 seg = b - a;
            float len = seg.magnitude;
            if (len < 0.08f) return;
            Vector3 mid = (a + b) * 0.5f;
            float u = Vector3.Dot(seg, cam.transform.up), r = Vector3.Dot(seg, cam.transform.right);
            float ang = Mathf.Atan2(u, r) * Mathf.Rad2Deg;
            var q = Quad("Filament", mid - cam.transform.forward * 0.3f, cam.transform.rotation * Quaternion.Euler(0f, 0f, ang),
                         NewMat(SliceAssets.I.softDot ?? hitStar, true, c, 2.6f));
            q.transform.localScale = new Vector3(len, width, 1f);
            q.AddComponent<FxAnim>().Setup(FxAnim.Mode.Hold, 0f, 0f, dur, c);
        }

        // 扇形斩面：横扫类技能的主形体（潮汐镰刀、风涡扫荡、共鸣爆炸）。用现成的刀光网格，不铺地面环。
        public static void SlashFan(Vector3 center, Vector3 dir, float rIn, float rOut, float ang, float dur, Color c)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
            dir.Normalize();
            // 刀面竖起来一点，斜俯视镜头里有厚度而不是一张平贴地皮
            var rot = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(8f, 0f, 0f);
            SlashFx.Play(center + Vector3.up * 0.9f, rot, rIn, rOut, ang, dur, c, 0);
        }

        // 竖直火柱：从地面往上冲（地火喷口的主形体）。区别于"地上一圈"的关键就是方向轴换成 Y。
        public static void FirePillar(Vector3 p, float r, float h, float dur, Color c)
        {
            var cam = Camera.main;
            if (!cam || !flameTex) return;
            var q = Quad("FirePillar", p + Vector3.up * (h * 0.5f) - cam.transform.forward * 0.2f,
                         cam.transform.rotation, NewMat(flameTex, true, c, 1.25f));
            q.transform.localScale = new Vector3(r * 1.5f, h, 1f);
            q.AddComponent<FxAnim>().Setup(FxAnim.Mode.Grow, r * 1.5f, h, dur, c);
            Embers(p + Vector3.up * h * 0.6f, 5);
            Sparks(p + Vector3.up * 0.3f, Vector3.up, 6, c);
        }

        // 地面符纹：缓慢旋转的符文环（镜步残阵、声骸领域这一类"埋在地上等触发"的语言）
        public static void RuneSigil(Vector3 p, float r, float dur, Color c)
        {
            if (!runeRing) return;
            var q = Quad("RuneSigil", p + Vector3.up * 0.07f, Quaternion.Euler(90f, 0f, 0f), NewMat(runeRing, true, c, 1.8f));
            q.transform.localScale = new Vector3(r * 2f, r * 2f, 1f);
            var a = q.AddComponent<FxAnim>();
            a.Setup(FxAnim.Mode.Spin, 0f, 0f, dur, c);
        }

        // 立体冲击壳：三道不同倾角的环同时外撑，读起来是"一个球壳撑开"而不是"地上又圈了一圈"
        public static void ShockShell(Vector3 p, float r, float dur, Color c)
        {
            Ring(p, 0.2f, r, dur, c, 0.2f);                                  // 水平环
            var cam = Camera.main;
            if (!cam) return;
            for (int i = 0; i < 2; i++)
            {
                var q = Quad("ShellBand", p - cam.transform.forward * 0.2f,
                             cam.transform.rotation * Quaternion.Euler(0f, 0f, i * 60f), NewMat(ringSoft, true, c, 2f));
                q.transform.localScale = new Vector3(r * 0.5f, r * 2f, 1f);
                q.AddComponent<FxAnim>().Setup(FxAnim.Mode.Grow, r * 0.5f, r * 2f, dur * 0.8f, c);
            }
            Glint(p + Vector3.up * 1.2f, r * 0.35f, c);
        }

        // 引爆：V30 爆炸贴图 + 径向碎片 + 烟（真正的"炸开"，只给精英死亡和大招收尾用）
        public static void Detonate(Vector3 p, float size, Color c)
        {
            var cam = Camera.main;
            if (cam && explosionTex)
            {
                var q = Quad("Detonate", p - cam.transform.forward * 0.3f,
                             cam.transform.rotation * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)),
                             NewMat(explosionTex, true, Color.white, 2.3f));
                q.transform.localScale = new Vector3(size * 2.4f, size * 2.4f, 1f);
                q.AddComponent<FxAnim>().Setup(FxAnim.Mode.Flip, size * 2f, size * 2.4f, 0.34f, c);
            }
            for (int i = 0; i < 10; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                Sparks(p + Vector3.up * 0.4f, new Vector3(Mathf.Cos(a), 0.9f, Mathf.Sin(a)), 2, c);
            }
            Smoke(p, 5, new Color(0.42f, 0.42f, 0.48f, 0.5f));
        }

        // 升腾光柱：从地面向上的柱体（共鸣超载起手、漩涡成形这一类"能量从地里出来"）
        public static void BeamUp(Vector3 p, float h, float dur, Color c)
        {
            var cam = Camera.main;
            if (!cam || !lootColumn) return;
            var q = Quad("BeamUp", p + Vector3.up * (h * 0.5f) - cam.transform.forward * 0.2f, cam.transform.rotation,
                         NewMat(lootColumn, true, c, 2.4f));
            q.transform.localScale = new Vector3(h * 0.42f, h, 1f);
            q.AddComponent<FxAnim>().Setup(FxAnim.Mode.Grow, h * 0.42f, h, dur, c);
        }
    }

    // DS-010：跟随目标的贴片（护盾泡），每帧贴目标中心、朝镜头，到期淡出销毁
    public class FxFollow : MonoBehaviour
    {
        Damageable target; float t, dur; Color c; Material mat; float size;

        public void Setup(Damageable d, float duration, Color col, float s)
        {
            target = d; dur = duration; c = col; size = s;
            mat = GetComponent<MeshRenderer>().sharedMaterial;
            Tick(0f);
        }

        void Update()
        {
            if (!target) { Destroy(gameObject); return; }   // DS-015：跟随目标消失即销毁（护盾泡 / 锁定标记共用）
            t += Time.deltaTime;
            Tick(Mathf.Clamp01(t / dur));
            if (t >= dur) Destroy(gameObject);
        }

        void Tick(float k)
        {
            if (target) transform.position = target.Center + Vector3.up * 0.2f;
            var cam = Camera.main;
            if (cam) transform.rotation = cam.transform.rotation;
            transform.localScale = Vector3.one * (size * (1f + 0.04f * Mathf.Sin(Time.time * 4f)));
            var col = c; col.a = c.a * (1f - Mathf.SmoothStep(0f, 1f, (k - 0.6f) / 0.4f));
            if (mat) mat.SetColor("_Color", col);
        }
    }

    public class FxAnim : MonoBehaviour
    {
        public enum Mode { Ring, Flash, Streak, Pop, Flip, Hold, Grow, Spin }   // Pop：美漫贴片，不渐隐，12 帧一拍从 a 跳到 b，到时间直接没；Flip：播一张 4×4 的序列帧；Hold：不动缩放，只按时间淡出（闪电带等自定缩放的贴片）；Grow：竖直形体从 25% 窜到满（火柱、光柱、立壳带）；Spin：地面符纹绕世界 Y 转，转速随生命衰减
        Mode mode; float s0, s1, dur, t, width; Color c; Material mat; bool ownsMesh;
        public bool stepped;   // 美漫：12 帧一拍跳着走，不渐隐

        // width 只有 Streak 用（细长条的起始宽度）；ownsMesh：网格是这次特效自己新建的，结束时一起销毁
        public void Setup(Mode m, float a, float b, float d, Color col, float w = 0f, bool ownMesh = false)
        {
            mode = m; s0 = a; s1 = b; dur = d; c = col; width = w; ownsMesh = ownMesh;
            mat = GetComponent<MeshRenderer>().sharedMaterial;
            Tick();
        }

        void Update() { t += Time.deltaTime; Tick(); if (t >= dur) Destroy(gameObject); }

        void OnDestroy()
        {
            if (mat) Destroy(mat);
            if (ownsMesh) { var mf = GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) Destroy(mf.sharedMesh); }
        }

        void Tick()
        {
            if (mode == Mode.Pop) { transform.localScale = Vector3.one * (t < 1f / 12f ? s0 : s1); return; }
            if (mode == Mode.Hold)
            {
                var hc = c; hc.a = c.a * (1f - t / dur);
                mat.SetColor("_Color", hc);
                return;
            }
            float k = Mathf.Clamp01((stepped ? Mathf.Floor(t * 12f) / 12f : t) / dur);
            if (mode == Mode.Grow)
            {
                // 火柱 / 光柱：x = s0、y = s1（调用方在创建时已把目标尺寸传进 Setup）。
                // 高度比宽度窜得快，才像"从地里冲出来"而不是"一个圈放大"。
                float e2 = 1f - (1f - k) * (1f - k);
                transform.localScale = new Vector3(Mathf.Lerp(s0 * 0.25f, s0, e2), Mathf.Lerp(s1 * 0.2f, s1, e2), 1f);
                var gc = c; gc.a = c.a * (1f - k * k);
                mat.SetColor("_Color", gc);
                return;
            }
            if (mode == Mode.Spin)
            {
                transform.Rotate(Vector3.up, 120f * Time.deltaTime * (1f - k * 0.6f), Space.World);
                var sc = c; sc.a = c.a * (1f - k);
                mat.SetColor("_Color", sc);
                return;
            }
            if (mode == Mode.Flip)
            {
                int f = Mathf.Min(15, (int)(k * 16f));
                mat.SetTextureScale("_MainTex", new Vector2(0.25f, 0.25f));
                mat.SetTextureOffset("_MainTex", new Vector2((f % 4) * 0.25f, 0.75f - (f / 4) * 0.25f));
                transform.localScale = Vector3.one * Mathf.Lerp(s0, s1, k);
                var fade = c; fade.a *= 1f - Mathf.SmoothStep(0f, 1f, (k - 0.65f) / 0.35f);
                mat.SetColor("_Color", fade);
                return;
            }
            float e = mode == Mode.Flash ? Mathf.Sqrt(k) : 1f - (1f - k) * (1f - k);
            float s = Mathf.Lerp(s0, s1, e);
            transform.localScale = mode == Mode.Ring ? new Vector3(s, 1f, s) : mode == Mode.Streak ? new Vector3(s, width * (1f - k), 1f) : Vector3.one * s;
            var col = c; col.a = stepped ? c.a : c.a * (mode == Mode.Flash ? (1f - k) * (1f - k) : 1f - k);
            mat.SetColor("_Color", col);
        }
    }
}

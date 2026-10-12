using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BlackTide
{
    // 竞技场：黑潮海中央的一座石台。地面石板、残柱、矮墙、火盆、黑潮晶簇、漂浮微光、后处理
    public static class ArenaBuilder
    {
        public const float Radius = 21f;
        static Material stone, stoneDark, stoneProp, crystal, brazierMat;
        public static readonly Color Teal = new Color(0.3f, 0.95f, 0.9f);
        public static readonly Color StoneTint = new Color(0.78f, 0.78f, 0.8f); // Tripo 石质贴图偏亮，压一点和地面统一

        public static Material Lit(Texture2D tex, Color tint, float smooth, Texture2D normal = null, float tiling = 1f)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (tex) m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            if (normal)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", 1f);
                m.EnableKeyword("_NORMALMAP");
            }
            m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            return m;
        }

        public static void Build()
        {
            var A = SliceAssets.I;
            var root = new GameObject("Arena").transform;
            bool baked = A.groundBake != null;
            if (baked)
            {
                // 烘焙地面：干净石板 + 边缘与晶簇周围渗出的黑潮，裂缝青光做自发光
                stone = Lit(A.groundBake, new Color(1.05f, 1.03f, 1.0f), 0.2f, A.groundBakeNormal, 1f);
                if (A.groundBakeEmission)
                {
                    stone.SetTexture("_EmissionMap", A.groundBakeEmission);
                    stone.SetColor("_EmissionColor", Teal * 2.2f);
                }
            }
            else stone = Lit(A.groundAlbedo, new Color(1.1f, 1.08f, 1.08f), 0.24f, A.groundNormal, 1f);
            var propTex = A.floorTile ? A.floorTile : A.groundAlbedo;
            stoneDark = Lit(propTex, new Color(0.62f, 0.64f, 0.68f), 0.12f, null, 0.5f);
            stoneProp = Lit(propTex, new Color(0.95f, 0.92f, 0.86f), 0.16f, null, 0.35f);
            crystal = Lit(null, new Color(0.03f, 0.08f, 0.09f), 0.85f);
            crystal.SetColor("_EmissionColor", new Color(0.3f, 1.6f, 1.5f));
            brazierMat = Lit(null, new Color(0.12f, 0.1f, 0.1f), 0.3f);
            propMats.Clear();

            Lighting();

            // 地面
            var ground = new GameObject("Ground");
            ground.transform.SetParent(root);
            ground.AddComponent<MeshFilter>().sharedMesh = Disk(Radius, 96, baked ? -1f : 7f);
            ground.AddComponent<MeshRenderer>().sharedMaterial = stone;
            ground.AddComponent<MeshCollider>();
            // 石台侧壁
            var skirt = new GameObject("Cliff");
            skirt.transform.SetParent(root);
            skirt.AddComponent<MeshFilter>().sharedMesh = Skirt(Radius, -6f, 96);
            skirt.AddComponent<MeshRenderer>().sharedMaterial = stoneDark;
            // 中央法阵（暗紫色描线）
            FX.Ring(Vector3.zero, 7f, 7f, 9999f, new Color(0.3f, 0.9f, 0.85f, 0.16f), 0.03f);
            FX.Ring(Vector3.zero, 4.5f, 4.5f, 9999f, new Color(0.3f, 0.9f, 0.85f, 0.1f), 0.02f);

            Walls(root);
            Pillars(root);
            FallenPillars(root);
            Braziers(root);
            Crystals(root);
            Statues(root);
            // 黑潮海
            var sea = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sea.name = "TideSea";
            Object.Destroy(sea.GetComponent<Collider>());
            sea.transform.SetParent(root);
            sea.transform.position = new Vector3(0, -2.6f, 0);
            sea.transform.rotation = Quaternion.Euler(90, 0, 0);
            sea.transform.localScale = Vector3.one * 400f;
            var seaMat = new Material(A.tideShader);
            seaMat.SetTexture("_Noise", A.tideNoise);
            seaMat.SetFloat("_Glow", 1.6f);
            seaMat.SetColor("_Deep", new Color(0.01f, 0.03f, 0.035f));
            seaMat.SetColor("_Crest", new Color(0.12f, 0.8f, 0.75f));
            sea.GetComponent<Renderer>().sharedMaterial = seaMat;
            sea.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Motes(root);
            PostFX();
        }

        // ---------------- 场景道具：有 Tripo 模型就用模型（按真实尺寸），没有就用占位几何体 ----------------
        static readonly Dictionary<PropAsset, Material> propMats = new Dictionary<PropAsset, Material>();
        public static void ResetPropMats() => propMats.Clear();
        static Vector3 Dir(float a) => new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        static bool Has(PropAsset p) => p != null && p.model;

        // 放一个道具：等比缩放到 size（byLength 时量水平最长边，否则量高度），底面中心落在 pos。
        // longX：把水平最长边转到本地 X 轴（墙沿圆周切向摆放用）
        public static Transform Prop(Transform root, PropAsset p, string name, Vector3 pos, Quaternion rot, float size, bool byLength,
            Color tint, Color emCol, float smooth, out Vector3 dims, bool longX = false)
        {
            if (!propMats.TryGetValue(p, out var mat))
            {
                mat = Lit(p.albedo, tint, smooth);
                if (p.emission) { mat.SetTexture("_EmissionMap", p.emission); mat.SetColor("_EmissionColor", emCol); }
                propMats[p] = mat;
            }
            var pivot = new GameObject(name).transform;
            pivot.SetParent(root, false);
            var m = Object.Instantiate(p.model, pivot).transform;
            m.name = "Mesh";
            var rs = m.GetComponentsInChildren<Renderer>();
            foreach (var r in rs)
            {
                var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On;
            }
            // 量尺寸时 pivot 在原点、无旋转，世界包围盒就是 pivot 空间的包围盒
            Bounds B()
            {
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                return b;
            }
            var bb = B();
            if (longX && bb.size.z > bb.size.x)
            {
                m.localRotation = Quaternion.Euler(0f, 90f, 0f) * m.localRotation;
                m.localPosition = Quaternion.Euler(0f, 90f, 0f) * m.localPosition;
                bb = B();
            }
            float dim = byLength ? Mathf.Max(bb.size.x, bb.size.z) : bb.size.y;
            float s = dim > 1e-4f ? size / dim : 1f;
            Vector3 p0 = m.localPosition, bottom = new Vector3(bb.center.x, bb.min.y, bb.center.z);
            m.localScale *= s;
            m.localPosition = -(bottom - p0) * s;
            dims = bb.size * s;
            pivot.SetPositionAndRotation(pos, rot);
            return pivot;
        }

        // 残墙（约 4×1.5 米）沿圆周一圈，同时是边界碰撞，随机留缺口
        static void Walls(Transform root)
        {
            var A = SliceAssets.I;
            float r = Radius - 0.4f;
            if (Has(A.wallRuin))
            {
                int seg = Mathf.CeilToInt(2f * Mathf.PI * r / 3.7f); // 每段 4 米，略微重叠
                for (int i = 0; i < seg; i++)
                {
                    if (Random.value < 0.15f) continue;
                    var d = Dir(i / (float)seg * Mathf.PI * 2f);
                    var rot = Quaternion.LookRotation(-d) * Quaternion.Euler(Random.Range(-2f, 2f), Random.Range(-4f, 4f), Random.Range(-3f, 3f));
                    var w = Prop(root, A.wallRuin, "Wall", d * r, rot, Random.Range(3.8f, 4.3f), true, StoneTint, Teal * 1.6f, 0.15f, out var dims, true);
                    var bc = w.gameObject.AddComponent<BoxCollider>();
                    bc.center = new Vector3(0f, dims.y * 0.5f, 0f);
                    bc.size = dims;
                }
                return;
            }
            int n = 40;
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                if (Random.value < 0.18f) continue; // 缺口
                var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
                w.name = "Wall"; w.transform.SetParent(root);
                float h = Random.Range(0.6f, 1.3f);
                w.transform.position = Dir(a) * r + Vector3.up * (h * 0.5f - 0.05f);
                w.transform.rotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a))) * Quaternion.Euler(Random.Range(-4f, 4f), 0, Random.Range(-5f, 5f));
                w.transform.localScale = new Vector3(0.7f, h, 2f * Mathf.PI * Radius / n * 0.98f);
                w.GetComponent<Renderer>().sharedMaterial = stoneDark;
            }
        }

        // 断柱（约 5 米）在墙外一圈
        static void Pillars(Transform root)
        {
            var A = SliceAssets.I;
            for (int i = 0; i < 12; i++)
            {
                float a = (i + 0.5f) / 12f * Mathf.PI * 2f + Random.Range(-0.08f, 0.08f);
                if (Has(A.pillarBroken))
                {
                    var rot = Quaternion.Euler(Random.Range(-3f, 3f), Random.Range(0f, 360f), Random.Range(-3f, 3f));
                    Prop(root, A.pillarBroken, "Pillar", Dir(a) * (Radius + 0.9f), rot, 5f * Random.Range(0.8f, 1.2f), false, StoneTint, Teal * 1.6f, 0.15f, out _);
                    continue;
                }
                float h = Random.Range(2.5f, 8f);
                float r = Random.Range(0.7f, 1.05f);
                var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                p.name = "Pillar"; p.transform.SetParent(root);
                p.transform.position = Dir(a) * (Radius + 0.6f) + Vector3.up * h * 0.5f;
                p.transform.localScale = new Vector3(r * 2f, h * 0.5f, r * 2f);
                p.transform.rotation = Quaternion.Euler(Random.Range(-6f, 6f), Random.Range(0f, 360f), Random.Range(-6f, 6f));
                p.GetComponent<Renderer>().sharedMaterial = stoneProp;
                Object.Destroy(p.GetComponent<Collider>());
                // 柱头
                var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cap.transform.SetParent(p.transform, false);
                cap.transform.localPosition = new Vector3(0, 1f, 0);
                cap.transform.localScale = new Vector3(1.35f, 0.18f, 1.35f);
                cap.GetComponent<Renderer>().sharedMaterial = stoneDark;
                Object.Destroy(cap.GetComponent<Collider>());
            }
        }

        // 倒柱（约 3 米长）散在场地边缘
        static void FallenPillars(Transform root)
        {
            var A = SliceAssets.I;
            for (int i = 0; i < 4; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                Vector3 pos = Dir(a) * (Radius - 2.2f);
                if (Has(A.pillarFallen))
                {
                    Prop(root, A.pillarFallen, "FallenPillar", pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), 3f * Random.Range(0.9f, 1.15f), true, StoneTint, Teal * 1.6f, 0.15f, out _);
                    continue;
                }
                var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                p.transform.SetParent(root);
                p.transform.position = pos + Vector3.up * 0.65f;
                p.transform.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
                p.transform.localScale = new Vector3(1.3f, Random.Range(1.2f, 2f), 1.3f);
                p.GetComponent<Renderer>().sharedMaterial = stoneProp;
                Object.Destroy(p.GetComponent<Collider>());
            }
        }

        // 火盆（约 1.4 米）：火焰粒子 + 闪烁点光源
        static void Braziers(Transform root)
        {
            var A = SliceAssets.I;
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                Vector3 pos = Dir(a) * 15.5f;
                float top = 1.15f, mouth = 0.35f;
                if (Has(A.brazier))
                {
                    Prop(root, A.brazier, "Brazier", pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), 1.4f, false, Color.white, new Color(1f, 0.5f, 0.2f) * 2.5f, 0.3f, out var dims);
                    top = dims.y * 0.92f;
                    mouth = Mathf.Min(dims.x, dims.z) * 0.32f; // 盆口半径
                }
                else
                {
                    var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    b.transform.SetParent(root);
                    b.transform.position = pos + Vector3.up * 0.55f;
                    b.transform.localScale = new Vector3(1.1f, 0.55f, 1.1f);
                    b.GetComponent<Renderer>().sharedMaterial = brazierMat;
                }
                var lgo = new GameObject("BrazierLight");
                lgo.transform.SetParent(root);
                lgo.transform.position = pos + Vector3.up * (top + 0.85f);
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Point; l.color = new Color(1f, 0.55f, 0.22f); l.intensity = 4f; l.range = 9f; l.shadows = LightShadows.None;
                lgo.AddComponent<Flicker>();
                Flame(root, pos + Vector3.up * top, mouth);
            }
        }

        // 黑潮结晶（约 2 米）：主晶体 + 两块小碎晶，青色点光源
        static void Crystals(Transform root)
        {
            var A = SliceAssets.I;
            for (int i = 0; i < 6; i++)
            {
                float a = (i * 60f + 20f) * Mathf.Deg2Rad;
                Vector3 pos = Dir(a) * (Radius - 2.4f);
                if (Has(A.tideCrystal))
                {
                    var em = Teal * 3f;
                    Prop(root, A.tideCrystal, "TideCrystal", pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), 2f * Random.Range(0.85f, 1.15f), false, Color.white, em, 0.7f, out _);
                    for (int k = 0; k < 2; k++)
                    {
                        Vector3 off = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(0.7f, 1.1f);
                        var rot = Quaternion.Euler(Random.Range(-28f, 28f), Random.Range(0f, 360f), Random.Range(-28f, 28f));
                        Prop(root, A.tideCrystal, "TideShard", pos + off, rot, 2f * Random.Range(0.3f, 0.5f), false, Color.white, em, 0.7f, out _);
                    }
                }
                else
                {
                    for (int k = 0; k < 4; k++)
                    {
                        var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        c.transform.SetParent(root);
                        float h = Random.Range(0.8f, 2.4f);
                        c.transform.position = pos + new Vector3(Random.Range(-0.7f, 0.7f), h * 0.35f, Random.Range(-0.7f, 0.7f));
                        c.transform.rotation = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0, 360f), Random.Range(-25f, 25f)) * Quaternion.Euler(0, 45, 0);
                        c.transform.localScale = new Vector3(0.35f, h, 0.35f);
                        c.GetComponent<Renderer>().sharedMaterial = crystal;
                        Object.Destroy(c.GetComponent<Collider>());
                    }
                }
                var lgo = new GameObject("TideLight");
                lgo.transform.SetParent(root);
                lgo.transform.position = pos + Vector3.up * 1.2f;
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Point; l.color = Teal; l.intensity = 2.6f; l.range = 7f; l.shadows = LightShadows.None;
            }
        }

        // 兜帽雕像（约 3 米）：四个正方向，立在墙外、面朝场地中心。没有占位版
        static void Statues(Transform root)
        {
            var A = SliceAssets.I;
            if (!Has(A.statueHooded)) return;
            for (int i = 0; i < 4; i++)
            {
                var d = Dir(i * 90f * Mathf.Deg2Rad);
                Prop(root, A.statueHooded, "Statue", d * (Radius + 1.4f), Quaternion.LookRotation(-d), 3f, false, StoneTint, Teal * 1.6f, 0.2f, out _);
            }
        }

        public static void Lighting()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.5f, 0.58f);
            RenderSettings.ambientEquatorColor = new Color(0.32f, 0.3f, 0.3f);
            RenderSettings.ambientGroundColor = new Color(0.06f, 0.06f, 0.07f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.03f, 0.06f, 0.075f);
            RenderSettings.fogDensity = 0.011f;
            RenderSettings.skybox = null;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(44f, -38f, 0f);
            sun.color = new Color(1f, 0.84f, 0.66f);
            sun.intensity = 2.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            RenderSettings.sun = sun;
            var rim = new GameObject("RimLight").AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.transform.rotation = Quaternion.Euler(28f, 160f, 0f);
            rim.color = new Color(0.3f, 0.75f, 0.85f);
            rim.intensity = 0.55f;
            rim.shadows = LightShadows.None;

            var cam = Camera.main;
            if (cam)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = RenderSettings.fogColor;
                cam.farClipPlane = 200f;
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.renderShadows = true;
            }
        }

        public static void PostFX()
        {
            var main = NewVolume("PostFX", 0);
            var p = main.sharedProfile;
            var tm = p.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            var bl = p.Add<Bloom>(true); bl.threshold.Override(0.95f); bl.intensity.Override(0.9f); bl.scatter.Override(0.68f);
            var ca = p.Add<ColorAdjustments>(true); ca.postExposure.Override(0.3f); ca.contrast.Override(14f); ca.saturation.Override(4f);
            var vg = p.Add<Vignette>(true); vg.intensity.Override(0.26f); vg.smoothness.Override(0.45f); vg.color.Override(new Color(0.02f, 0.05f, 0.06f));
            var smh = p.Add<ShadowsMidtonesHighlights>(true); smh.shadows.Override(new Vector4(0.92f, 0.98f, 1.05f, 0f));
            // 分色（2026-10-09 打磨）：暗部偏冷青、亮部偏暖，黑潮的冷和火光、绯烈的暖拉开对比
            var st = p.Add<SplitToning>(true); st.shadows.Override(new Color(0.18f, 0.42f, 0.46f)); st.highlights.Override(new Color(0.62f, 0.5f, 0.38f)); st.balance.Override(-12f);

            var hit = NewVolume("HitPulse", 1);
            var hp = hit.sharedProfile;
            var hca = hp.Add<ChromaticAberration>(true); hca.intensity.Override(0.45f);
            var hld = hp.Add<LensDistortion>(true); hld.intensity.Override(-0.07f);
            var hc = hp.Add<ColorAdjustments>(true); hc.postExposure.Override(0.12f);
            hit.weight = 0f;

            var hurt = NewVolume("HurtPulse", 2);
            var up = hurt.sharedProfile;
            var uv = up.Add<Vignette>(true); uv.intensity.Override(0.55f); uv.smoothness.Override(0.75f); uv.color.Override(new Color(0.55f, 0f, 0.02f));
            var uc = up.Add<ColorAdjustments>(true); uc.saturation.Override(-35f);
            hurt.weight = 0f;

            var chant = NewVolume("ChantTint", 3);
            var cp = chant.sharedProfile;
            var cc = cp.Add<ColorAdjustments>(true); cc.colorFilter.Override(new Color(0.8f, 1f, 1.05f)); cc.saturation.Override(-15f);
            var cv = cp.Add<Vignette>(true); cv.intensity.Override(0.45f); cv.color.Override(new Color(0.02f, 0.25f, 0.25f));
            chant.weight = 0f;

            Feel.I.SetVolumes(hit, hurt, chant);
        }

        static Volume NewVolume(string name, float priority)
        {
            var go = new GameObject(name);
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = priority;
            v.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            return v;
        }

        public static void Flame(Transform root, Vector3 pos, float radius)
        {
            var go = new GameObject("Flame");
            go.transform.SetParent(root);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
                        float k = Mathf.Max(1f, radius / 0.35f); // 盆口越大火越旺
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f * Mathf.Sqrt(k), 0.8f * Mathf.Sqrt(k));
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.15f), new Color(1f, 0.3f, 0.05f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.3f;
            var em = ps.emission; em.rateOverTime = 40f * k;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = radius; sh.rotation = new Vector3(-90, 0, 0);
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.1f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1f, 0.4f, 0.2f), 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = FX.NewMat(SliceAssets.I.softDot, true, Color.white, 2.5f);
        }

        public static void Motes(Transform root)
        {
            var go = new GameObject("TideMotes");
            go.transform.SetParent(root);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.16f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.35f, 0.95f, 0.9f, 0.9f), new Color(1f, 0.62f, 0.3f, 0.55f));
            main.maxParticles = 400;
            main.gravityModifier = -0.02f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.prewarm = true;
            var em = ps.emission; em.rateOverTime = 40f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(44f, 4f, 44f); sh.position = new Vector3(0, 2f, 0);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.3f; noise.frequency = 0.2f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.3f), new GradientAlphaKey(0, 1) });
            col.color = g;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = FX.NewMat(SliceAssets.I.softDot, true, Color.white, 2f);
        }

        static Mesh Disk(float r, int seg, float uvScale)
        {
            var m = new Mesh();
            int rings = 8;
            var v = new System.Collections.Generic.List<Vector3>();
            var uv = new System.Collections.Generic.List<Vector2>();
            var tri = new System.Collections.Generic.List<int>();
            v.Add(Vector3.zero); uv.Add(uvScale < 0f ? new Vector2(0.5f, 0.5f) : Vector2.zero);
            for (int ri = 1; ri <= rings; ri++)
            {
                float rr = r * ri / rings;
                for (int i = 0; i < seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    var p = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                    v.Add(p); uv.Add(uvScale < 0f ? new Vector2(p.x / (2f * r) + 0.5f, p.z / (2f * r) + 0.5f) : new Vector2(p.x, p.z) / uvScale);
                }
            }
            for (int i = 0; i < seg; i++) { tri.Add(0); tri.Add(1 + (i + 1) % seg); tri.Add(1 + i); }
            for (int ri = 1; ri < rings; ri++)
            {
                int a0 = 1 + (ri - 1) * seg, b0 = 1 + ri * seg;
                for (int i = 0; i < seg; i++)
                {
                    int i1 = (i + 1) % seg;
                    tri.Add(a0 + i); tri.Add(a0 + i1); tri.Add(b0 + i);
                    tri.Add(a0 + i1); tri.Add(b0 + i1); tri.Add(b0 + i);
                }
            }
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        static Mesh Skirt(float r, float bottom, int seg)
        {
            var m = new Mesh();
            var v = new Vector3[(seg + 1) * 2]; var uv = new Vector2[v.Length]; var tri = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float wob = 1f + Mathf.PerlinNoise(i * 0.3f, 0.5f) * 0.06f;
                v[i * 2] = d * r; v[i * 2 + 1] = d * r * wob * 0.94f + Vector3.up * bottom;
                uv[i * 2] = new Vector2(i / (float)seg * 20f, 0); uv[i * 2 + 1] = new Vector2(i / (float)seg * 20f, 1.2f);
            }
            for (int i = 0; i < seg; i++) { int k = i * 6, a = i * 2; tri[k] = a; tri[k + 1] = a + 2; tri[k + 2] = a + 1; tri[k + 3] = a + 2; tri[k + 4] = a + 3; tri[k + 5] = a + 1; }
            m.vertices = v; m.uv = uv; m.triangles = tri; m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }
    }

    public class Flicker : MonoBehaviour
    {
        Light l; float baseI; float seed;
        void Awake() { l = GetComponent<Light>(); baseI = l.intensity; seed = Random.value * 10f; }
        void Update() { l.intensity = baseI * (0.85f + 0.3f * Mathf.PerlinNoise(seed, Time.time * 6f)); }
    }
}

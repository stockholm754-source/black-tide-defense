using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 斩击弧面：月牙形带状网格（尾细头宽、内缘略翘、刀锋上竖一道刃带，斜俯视和侧面都有厚度）
    // 两套画法：BT_Slash（能量面、白热刃、流动外焰）和 BT_SlashInk（美漫：硬边、墨线、网点、外焰、抽帧），由 FX.Comic 选；用完回池，材质和网格都复用
    public class SlashFx : MonoBehaviour
    {
        const int Seg = 40, Per = 5;
        const float InkFps = 12f;
        const float FlameV = 0.8f;   // 美漫：网格往刀锋外多画一圈给外焰，刀锋落在 v = 0.8
        const float SoftFlameV = 0.82f; // 默认斩击也给外焰留空间；真实刀锋仍在调用者的外半径
        static readonly Stack<SlashFx> pool = new Stack<SlashFx>();
        static Transform root;
        static Shader softShader, inkShader;
        static Texture2D noise;
        static readonly Vector3[] vbuf = new Vector3[(Seg + 1) * Per];
        static readonly Vector2[] ubuf = new Vector2[(Seg + 1) * Per];
        static Color[] colors;
        static int[] tris;

        // 弧面实际画多宽（相对调用者给的内外半径差）；出现时头部已经扫到哪——调用发生在命中那一帧，刀已经挥到一半，顿帧要定格在展开的弧上
        public static float WidthMul = 0.9f, InkWidthMul = 1.15f, HeadStart = 0.45f;

        Mesh mesh; Material softMat, inkMat, mat; MeshRenderer rend; float t, total; Vector3 flip; bool comic;

        // 三种风格：写实 BT_Slash / 美漫 BT_SlashInk / 霓虹故障 BT_SlashNeon（CL-005，用户指定赛博朋克方向）。
        // comic 参数保留给武器级美漫覆盖（礁石巨锤彩蛋）；霓虹只在没有武器级覆盖时生效。
        static Shader glitchSlashShader;
        public static bool UseNeon => FX.Glitch;

        public static Material NewMat(bool comic)
        {
            if (!softShader) { softShader = Shader.Find("BlackTide/Slash"); inkShader = Shader.Find("BlackTide/SlashInk"); }
            if (UseNeon && !glitchSlashShader) glitchSlashShader = Shader.Find("BlackTide/SlashNeon");
            bool neon = UseNeon && glitchSlashShader;
            var m = new Material(neon ? glitchSlashShader : (comic ? inkShader : softShader));
            if (!noise) noise = Resources.Load<Texture2D>("VFX/slash_noise");
            m.SetTexture("_Noise", noise ? noise : SliceAssets.I.tideNoise);
            if (neon)
            {
                // 赛博参数：撕裂与色散明显，但闪烁压在可读范围内——看不见刀锋比不好看更糟
                m.SetFloat("_Glitch", 0f);
                m.SetFloat("_BandN", 9f);
                m.SetFloat("_Rate", 15f);
                m.SetFloat("_Chroma", 0f);
                m.SetFloat("_Ghost", 0.18f);
                m.SetFloat("_Flick", 0f);
                m.SetFloat("_Shade", 0.12f);
            }
            return m;
        }

        // 美漫配色从一个主色推出来：主色拉满饱和，高光奶白，暗部往品红偏，错版撞色取补色，外焰往黄偏。
        // 接近白色的主色（反击、圣光一类）保持白，配冷色的暗部和撞色
        public static void Palette(Color c, out Color main, out Color hi, out Color lo, out Color pop, out Color pop2)
        {
            float h, s, v;
            Color.RGBToHSV(c, out h, out s, out v);
            if (s < 0.3f)
            {
                main = new Color(0.97f, 0.98f, 1f); hi = Color.white;
                lo = Color.HSVToRGB(0.6f, 0.55f, 0.95f); pop = Color.HSVToRGB(0.9f, 0.9f, 1f); pop2 = Color.HSVToRGB(0.5f, 0.9f, 1f);
                return;
            }
            main = Color.HSVToRGB(h, 1f, 1f);
            hi = Color.HSVToRGB(Mathf.Repeat(h + 0.06f, 1f), 0.22f, 1f);
            lo = Color.HSVToRGB(Mathf.Repeat(h - 0.13f, 1f), 1f, 0.82f);
            pop = Color.HSVToRGB(Mathf.Repeat(h + 0.5f, 1f), 0.9f, 1f);
            pop2 = Color.HSVToRGB(Mathf.Repeat(h + 0.1f, 1f), 0.95f, 1f);
        }

        public static void SetColors(Material m, Color c, bool comic)
        {
            // 霓虹故障配色：技能原色拉满饱和当灯管色，芯打白，内缘给对照霓虹（青↔橙 / 青↔品红），
            // 外辉光取邻近色但提高亮度。红线：不产生纯红——红色在本项目里只属于攻击预警。
            if (UseNeon && m && m.HasProperty("_Ghost"))
            {
                float h, s, v;
                Color.RGBToHSV(c, out h, out s, out v);
                if (s < 0.25f) h = 0.5f;                                   // 近白的技能给青霓虹，别出一根白管
                float hueRed = Mathf.Repeat(h + (h > 0.02f && h < 0.12f ? 0.42f : -0.09f), 1f);   // 橙系避开直接撞红
                Color tube = Color.HSVToRGB(h, 1f, 1f);
                tube.a = c.a;
                m.SetColor("_Color", tube);
                m.SetColor("_Core", Color.HSVToRGB(h, 0.92f, 1f));
                m.SetColor("_EdgeCol", Color.HSVToRGB(hueRed, 0.95f, 0.95f));
                m.SetColor("_Glow", Color.HSVToRGB(Mathf.Repeat(h + 0.08f, 1f), 0.85f, 1f));
                return;
            }
            if (!comic)
            {
                float h, saturation, value;
                Color.RGBToHSV(c, out h, out saturation, out value);
                // 默认风格用相邻色形成体量，保持技能原本色系；不沿用美漫的补色错版。
                Color bodyColor = Color.HSVToRGB(h, Mathf.Clamp01(saturation * 1.12f), value);
                bodyColor.a = c.a;
                m.SetColor("_Color", bodyColor);
                m.SetColor("_Core", Color.Lerp(bodyColor, Color.white, 0.75f));
                m.SetColor("_EdgeCol", Color.HSVToRGB(Mathf.Repeat(h - 0.025f, 1f), Mathf.Clamp01(saturation * 1.2f), value * 0.65f));
                m.SetColor("_Glow", Color.HSVToRGB(Mathf.Repeat(h + 0.035f, 1f), saturation * 0.85f, value));
                return;
            }
            Color main, hi, lo, pop, pop2;
            Palette(c, out main, out hi, out lo, out pop, out pop2);
            m.SetColor("_Color", main); m.SetColor("_Hi", hi); m.SetColor("_Lo", lo); m.SetColor("_Pop", pop); m.SetColor("_Pop2", pop2);
        }

        // sweep：+1 从左扫到右，-1 反过来（局部 X 镜像）
        public static void Play(Vector3 center, Quaternion rot, float rIn, float rOut, float ang, float dur, Color c, int sweep)
        {
            SlashFx s = null;
            while (pool.Count > 0 && !s) s = pool.Pop();
            if (!s)
            {
                if (!root) root = new GameObject("SlashFx").transform;
                var go = new GameObject("Slash");
                go.transform.SetParent(root, false);
                s = go.AddComponent<SlashFx>();
                s.mesh = new Mesh(); s.mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = s.mesh;
                s.rend = go.AddComponent<MeshRenderer>();
                s.rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            s.comic = FX.Comic;
            if (s.comic) { if (!s.inkMat) { s.inkMat = NewMat(true); s.inkMat.SetFloat("_EdgeV", FlameV); } s.mat = s.inkMat; }
            else
            {
                // A pooled slash can outlive a style switch; rebind before the next attack.
                if(s.softMat && (s.softMat.shader.name=="BlackTide/SlashNeon")!=UseNeon)
                { Destroy(s.softMat); s.softMat=null; }
                if (!s.softMat)
                {
                    s.softMat = NewMat(false);
                    s.softMat.SetFloat("_EdgeV", SoftFlameV);
                    s.softMat.SetFloat("_Shade", 0.32f);
                }
                s.mat = s.softMat;
            }
            s.rend.sharedMaterial = s.mat;
            s.Build(rIn, rOut, ang);
            s.transform.SetPositionAndRotation(center, rot);
            s.flip = new Vector3(sweep < 0 ? -1f : 1f, 1f, 1f);
            s.t = 0f;
            s.total = s.comic ? Mathf.Max(dur * 2.2f, 5f / InkFps) : Mathf.Max(0.01f, dur * 2.2f);   // 美漫至少画满五拍
            SetColors(s.mat, c, s.comic);
            s.mat.SetFloat("_Seed", Random.value * 7f);
            s.gameObject.SetActive(true);
            s.Tick();
        }

        void OnDestroy() { if (softMat) Destroy(softMat); if (inkMat) Destroy(inkMat); if (mesh) Destroy(mesh); }

        void Update()
        {
            t += Time.deltaTime;
            if (t >= total) { gameObject.SetActive(false); pool.Push(this); return; }
            Tick();
        }

        // 前三成时间头部扫完整条弧，尾巴一路追上来，后七成从内侧和尾部碎掉。美漫按 12 帧一拍跳着走，并且往外胀得更多
        void Tick()
        {
            float k = Mathf.Clamp01((comic ? Mathf.Floor(t * InkFps) / InkFps : t) / total);
            float h = Mathf.Clamp01(k / 0.3f);
            mat.SetFloat("_Head", Mathf.Lerp(HeadStart, 1.12f, 1f - (1f - h) * (1f - h)));
            mat.SetFloat("_Tail", Mathf.Lerp(-0.5f, 0.7f, k));
            mat.SetFloat("_Dissolve", Mathf.SmoothStep(0f, 1f, (k - 0.3f) / 0.7f));
            if (!comic)
            {
                float flash = 1f - Mathf.Clamp01(k / 0.12f);
                mat.SetFloat("_Flash", flash * flash);
            }
            transform.localScale = flip * (1f + (comic ? 0.14f : 0.06f) * k);
        }

        // 每段 5 个顶点：0 内缘、1 外焰外缘、2 / 3 刃带上下端、4 真实刀锋。
        void Build(float rIn, float rOut, float ang)
        {
            float full = rOut - rIn, width = full * (comic ? InkWidthMul : WidthMul), lift = width * 0.2f, blade = full * (comic ? 0.22f : 0.12f);
            float ev = comic ? FlameV : SoftFlameV, extra = width * (1f / ev - 1f), bv = (comic ? 0.55f : 0.65f) * ev;
            for (int i = 0; i <= Seg; i++)
            {
                float u = i / (float)Seg;
                float a = Mathf.Deg2Rad * (-ang * 0.5f + ang * u);
                Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                float w = Mathf.Lerp(0.1f, 1f, Mathf.SmoothStep(0f, 1f, u / 0.7f)) * Mathf.Lerp(1f, 0.3f, Mathf.SmoothStep(0f, 1f, (u - 0.82f) / 0.18f));
                Vector3 o = d * rOut;
                int k = i * Per;
                vbuf[k] = d * (rOut - width * w) + Vector3.up * lift * w; ubuf[k] = new Vector2(u, 0f);
                vbuf[k + 1] = d * (rOut + extra * w); ubuf[k + 1] = new Vector2(u, 1f);
                vbuf[k + 2] = o + Vector3.up * blade * w; ubuf[k + 2] = new Vector2(u, bv);
                vbuf[k + 3] = o - Vector3.up * blade * w; ubuf[k + 3] = new Vector2(u, bv);
                vbuf[k + 4] = o; ubuf[k + 4] = new Vector2(u, ev);
            }
            if (tris == null)
            {
                tris = new int[Seg * 18];
                for (int i = 0; i < Seg; i++)
                {
                    int k = i * 18, a = i * Per, b = a + Per;
                    tris[k] = a; tris[k + 1] = a + 1; tris[k + 2] = b; tris[k + 3] = a + 1; tris[k + 4] = b + 1; tris[k + 5] = b;                 // 弧面
                    tris[k + 6] = a + 2; tris[k + 7] = a + 4; tris[k + 8] = b + 2; tris[k + 9] = a + 4; tris[k + 10] = b + 4; tris[k + 11] = b + 2;   // 刃带上半
                    tris[k + 12] = a + 4; tris[k + 13] = a + 3; tris[k + 14] = b + 4; tris[k + 15] = a + 3; tris[k + 16] = b + 3; tris[k + 17] = b + 4; // 刃带下半
                }
            }
            mesh.Clear();
            mesh.SetVertices(vbuf);
            mesh.SetUVs(0, ubuf);
            // Shader 用顶点色 alpha 判断新旧；弧面全部是新鲜刀光，拖尾单独写渐隐色。
            if (colors == null)
            {
                colors = new Color[vbuf.Length];
                for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            }
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }
    }
}

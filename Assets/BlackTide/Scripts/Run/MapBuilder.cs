using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace BlackTide
{
    // 开放地图（M1）：地面、海、边界崖壁、各节点的布景、随机散布的废墟，最后在运行时生成导航网格。
    // 随机数用固定种子：每局地图完全一样，验证时不同局之间才能比较。
    public static class MapBuilder
    {
        public const int Seed = 20261008;
        static Transform root;
        static readonly List<Vector3> occupied = new List<Vector3>();   // 已经放了东西的位置（x, z, 半径）
        static Material groundMat;
        public const float GroundExt = 150f;                      // 地面网格和遮罩覆盖的半边长（地图外 40 米沉进海里，做海岸）
        static readonly List<(Vector2 a, Vector2 b)> roads = new List<(Vector2, Vector2)>();   // 道路折线段（道具避开）

        public static void Build()
        {
            var A = SliceAssets.I;
            Random.InitState(Seed);
            root = new GameObject("Map").transform;
            occupied.Clear();
            ArenaBuilder.ResetPropMats();
            ArenaBuilder.Lighting();
            RenderSettings.fogDensity = 0.009f;

            Roads();
            WallGates();
            Ground();
            Sea();
            Boundary();
            foreach (var n in MapLayout.Nodes) Dress(n);
            Scatter();
            BakeNav();
            Combat.clampOverride = Clamp;
            Combat.arenaRadius = MapLayout.Half;
            ArenaBuilder.PostFX();
            var motes = new GameObject("MotesFollow");
            motes.AddComponent<MotesFollow>();
            ArenaBuilder.Motes(motes.transform);
        }

        // ---------------- 地面 ----------------
        static void Ground()
        {
            var A = SliceAssets.I;
            var sh = Shader.Find("BlackTide/Ground");
            if (sh)
            {
                // 分层地面（BT_Ground.shader）。用户的地面贴图（Design/art_batch5_ground.md）到之前，各层用现有贴图乘色调代替
                groundMat = new Material(sh);
                groundMat.SetTexture("_Mask", BuildMask());
                // 第五批地面贴图（用户 GPT 生成，2026-10-09）；缺哪张就用程序噪声乘色调代替
                var noise = SeamlessNoise();
                void Layer(string tex, string tint, Texture2D t, Color fallback)
                {
                    groundMat.SetTexture(tex, t ? t : noise);
                    groundMat.SetColor(tint, t ? new Color(0.92f, 0.92f, 0.94f) : fallback);
                }
                Layer("_Earth", "_EarthTint", A.gDirt, new Color(0.5f, 0.43f, 0.35f));
                Layer("_Moss", "_MossTint", A.gMoss, new Color(0.3f, 0.36f, 0.26f));
                Layer("_Sand", "_SandTint", A.gSand, new Color(0.55f, 0.52f, 0.46f));
                if (A.gCobble) { groundMat.SetTexture("_Cobble", A.gCobble); groundMat.SetColor("_CobbleTint", new Color(0.92f, 0.92f, 0.94f)); }
                else { groundMat.SetTexture("_Cobble", A.groundAlbedo); groundMat.SetColor("_CobbleTint", new Color(0.6f, 0.6f, 0.62f)); }
                if (A.gTide && A.gTideEm) { groundMat.SetTexture("_Tide", A.gTide); groundMat.SetTexture("_TideEm", A.gTideEm); groundMat.SetColor("_TideTint", Color.white); }
                if (A.gNormal) groundMat.SetTexture("_Normal", A.gNormal);
                groundMat.SetTexture("_Stone", A.floorTile ? A.floorTile : A.groundAlbedo); groundMat.SetColor("_StoneTint", new Color(0.86f, 0.85f, 0.84f));
                groundMat.SetTexture("_Noise", noise);
                groundMat.SetFloat("_MapExt", GroundExt);
                groundMat.SetFloat("_PlayHalf", MapLayout.Half);
                groundMat.SetVector("_TideCenter", new Vector4(MapLayout.TideCenter.x, MapLayout.TideCenter.y, 0, 0));
                groundMat.SetFloat("_TideRadius", 0f);
                groundMat.SetFloat("_ProceduralTide", A.gTide && A.gTideEm ? 0f : 1f);
            }
            else
            {
                var tex = A.floorTile ? A.floorTile : A.groundAlbedo;
                groundMat = ArenaBuilder.Lit(tex, new Color(0.92f, 0.9f, 0.88f), 0.18f, null, 1f);
            }
            var go = new GameObject("Ground");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = Grid(GroundExt, 150, 7f, true);
            go.AddComponent<MeshRenderer>().sharedMaterial = groundMat;
            go.AddComponent<MeshCollider>();
        }

        // 黑潮覆盖范围（RunDirector 每帧调）：地面跟着变成黑潮地
        public static void SetTide(Vector2 center, float radius)
        {
            if (!groundMat) return;
            groundMat.SetVector("_TideCenter", new Vector4(center.x, center.y, 0, 0));
            groundMat.SetFloat("_TideRadius", Mathf.Max(0f, radius));
        }

        // ---------------- 道路：出生点和各节点之间的最小生成树，每段带一点自然弯曲 ----------------
        static void Roads()
        {
            roads.Clear();
            var pts = new List<Vector2> { MapLayout.Spawn };
            var rad = new List<float> { 10f };
            foreach (var n in MapLayout.Nodes)
                if (n.type != NodeType.Pack && n.type != NodeType.Messenger) { pts.Add(n.pos); rad.Add(n.radius); }
            // Prim 算法
            var inTree = new bool[pts.Count]; inTree[0] = true;
            for (int k = 1; k < pts.Count; k++)
            {
                int bi = -1, bj = -1; float bd = float.MaxValue;
                for (int i = 0; i < pts.Count; i++) if (inTree[i])
                    for (int j = 0; j < pts.Count; j++) if (!inTree[j])
                    {
                        float d = Vector2.Distance(pts[i], pts[j]);
                        if (d < bd) { bd = d; bi = i; bj = j; }
                    }
                inTree[bj] = true;
                // 从 A 的边缘走到 B 的边缘，中间两个点按噪声往两侧弯
                Vector2 a = pts[bi], b = pts[bj], dir = (b - a).normalized, side = new Vector2(-dir.y, dir.x);
                Vector2 a0 = a + dir * rad[bi] * 0.6f, b0 = b - dir * rad[bj] * 0.6f;
                float len = Vector2.Distance(a0, b0);
                Vector2 prev = a0;
                const int seg = 6;
                for (int s = 1; s <= seg; s++)
                {
                    float t = s / (float)seg;
                    float bend = (Mathf.PerlinNoise(a.x * 0.05f + t * 1.7f, b.y * 0.05f) - 0.5f) * Mathf.Min(10f, len * 0.18f) * Mathf.Sin(t * Mathf.PI);
                    Vector2 q = Vector2.Lerp(a0, b0, t) + side * bend;
                    roads.Add((prev, q));
                    prev = q;
                }
            }
        }

        // 古城墙的城门（2026-10-09）：道路是最小生成树，穿过 120 米城墙的路只有一两条（还常落在节点里），城门放不出来。
        // 改成在「出生点 → 每个内圈目标」的连线和城墙的交点开门（相距 30 米以内合并），门前门后各铺 16 米路
        public const float WallR = 120f, RidgeR = 165f;
        static readonly List<Vector2> wallGates = new List<Vector2>();
        static void WallGates()
        {
            wallGates.Clear();
            Vector2 tc = MapLayout.TideCenter;
            foreach (var n in MapLayout.Nodes)
            {
                if (n.band != 2 || n.type == NodeType.Pack || n.type == NodeType.Merchant) continue;
                Vector2 a = MapLayout.Spawn, b = n.pos;
                float da = Vector2.Distance(a, tc) - WallR, db = Vector2.Distance(b, tc) - WallR;
                if (da * db > 0f) continue;
                // 二分找交点
                float lo = 0f, hi = 1f;
                for (int i = 0; i < 30; i++) { float m = (lo + hi) * 0.5f; if (Vector2.Distance(Vector2.Lerp(a, b, m), tc) > WallR) lo = m; else hi = m; }
                var x = Vector2.Lerp(a, b, lo);
                bool dup = false;
                foreach (var g in wallGates) if (Vector2.Distance(g, x) < 30f) dup = true;
                foreach (var nd in MapLayout.Nodes) if (Vector2.Distance(x, nd.pos) < nd.radius + 6f) dup = true;
                if (dup) continue;
                wallGates.Add(x);
                Vector2 o = (x - tc).normalized;
                roads.Add((x + o * 16f, x));
                roads.Add((x, x - o * 16f));
            }
        }

        static float RoadDist(Vector2 p)
        {
            float best = float.MaxValue;
            foreach (var (a, b) in roads)
            {
                Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                float d = Vector2.Distance(p, a + ab * t);
                if (d < best) best = d;
            }
            return best;
        }

        // ---------------- 地面遮罩（512×512，覆盖 ±GroundExt）：R 广场 G 道路 B 静态腐化 A 植被许可 ----------------
        static Texture2D BuildMask()
        {
            const int S = 512;
            var px = new Color32[S * S];
            float cell = GroundExt * 2f / S;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    var p = new Vector2(-GroundExt + (x + 0.5f) * cell, -GroundExt + (y + 0.5f) * cell);
                    float plaza = 0f, corrupt = 0f;
                    // 出生点广场
                    plaza = Mathf.Max(plaza, Soft(Vector2.Distance(p, MapLayout.Spawn), 10f, 4f));
                    foreach (var n in MapLayout.Nodes)
                    {
                        float d = Vector2.Distance(p, n.pos);
                        switch (n.type)
                        {
                            case NodeType.Stronghold: plaza = Mathf.Max(plaza, Soft(d, n.radius * 0.85f, 4f)); break;
                            case NodeType.Camp: plaza = Mathf.Max(plaza, Soft(d, n.radius * 0.45f, 5f) * 0.8f); break;
                            case NodeType.Merchant: plaza = Mathf.Max(plaza, Soft(d, 6.5f, 3f)); break;
                            case NodeType.WildBoss: plaza = Mathf.Max(plaza, Soft(d, n.radius * 0.7f, 4f) * 0.7f); break;
                            case NodeType.Nest: corrupt = Mathf.Max(corrupt, Soft(d, n.radius * 0.8f, 5f) * 0.75f); break;
                            case NodeType.Rift: corrupt = Mathf.Max(corrupt, Soft(d, n.radius * 0.95f, 7f)); break;
                            case NodeType.Heart: corrupt = Mathf.Max(corrupt, Soft(d, n.radius + 4f, 8f)); break;
                        }
                    }
                    // 内圈风化：离黑潮中心 70 米内逐渐出现腐化斑块
                    float td = Vector2.Distance(p, MapLayout.TideCenter);
                    corrupt = Mathf.Max(corrupt, Mathf.Clamp01((70f - td) / 45f) * 0.55f * Mathf.PerlinNoise(p.x * 0.04f, p.y * 0.04f) * 1.6f);
                    float road = Soft(RoadDist(p), 3f, 2f);   // 2026-10-09 加宽（引导）
                    float veg = Mathf.Clamp01(1f - Mathf.Max(plaza, road) * 1.2f) * Mathf.Clamp01((td - 35f) / 40f);
                    px[y * S + x] = new Color32((byte)(Mathf.Clamp01(plaza) * 255), (byte)(road * 255), (byte)(Mathf.Clamp01(corrupt) * 255), (byte)(veg * 255));
                }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "GroundMask" };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        static float Soft(float d, float r, float feather) => Mathf.Clamp01((r + feather - d) / feather);

        // 无缝分形噪声（256×256，四个角互相融合成周期函数）：地面各层占位和海面都用它
        static Texture2D noiseTex;
        public static Texture2D SeamlessNoise()
        {
            if (noiseTex) return noiseTex;
            const int S = 256;
            var px = new Color32[S * S];
            float Fbm(float x, float y)
            {
                float v = 0f, a = 0.5f, f = 1f;
                for (int o = 0; o < 5; o++) { v += a * Mathf.PerlinNoise(x * f + o * 17.3f, y * f + o * 9.1f); a *= 0.5f; f *= 2f; }
                return v;
            }
            const float P = 8f;   // 一个周期里的噪声格数
            var raw = new float[S * S];
            float lo = float.MaxValue, hi = float.MinValue;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)S, w = y / (float)S;
                    float a = Fbm(u * P, w * P), b = Fbm((u - 1f) * P, w * P), c = Fbm(u * P, (w - 1f) * P), d = Fbm((u - 1f) * P, (w - 1f) * P);
                    float v = Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), w);
                    raw[y * S + x] = v; lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v);
                }
            // 拉满到 0–1：四角融合会压低方差，不拉的话值都挤在 0.4–0.6，苔藓、斑块、明暗变化几乎出不来
            for (int i = 0; i < raw.Length; i++)
            {
                float v = Mathf.SmoothStep(0f, 1f, (raw[i] - lo) / Mathf.Max(1e-4f, hi - lo));
                byte g = (byte)(v * 255);
                px[i] = new Color32(g, g, g, 255);
            }
            noiseTex = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = "SeamlessNoise" };
            noiseTex.SetPixels32(px);
            noiseTex.Apply(true, true);
            return noiseTex;
        }

        // 平铺网格：uv 按世界坐标 / tile 米平铺
        static Mesh Grid(float half, int n, float tile, bool coast = false)
        {
            var m = new Mesh { indexFormat = IndexFormat.UInt32 };
            var v = new Vector3[(n + 1) * (n + 1)]; var uv = new Vector2[v.Length]; var tri = new int[n * n * 6];
            for (int i = 0; i <= n; i++) for (int j = 0; j <= n; j++)
            {
                float x = -half + 2f * half * i / n, z = -half + 2f * half * j / n;
                float y = 0f;
                if (coast)
                {
                    // 可玩区外：按噪声的不规则海岸线往下斜进海里（海面在 −2.6 米）；可玩区内保持平地（角色、预警都假设 y = 0）
                    float over = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - (MapLayout.Half - 3f) + (Mathf.PerlinNoise(x * 0.025f + 3.1f, z * 0.025f + 7.3f) - 0.5f) * 18f;
                    if (over > 0f) y = -Mathf.Min(6f, over * 0.32f);
                }
                v[i * (n + 1) + j] = new Vector3(x, y, z);
                uv[i * (n + 1) + j] = new Vector2(x, z) / tile;
            }
            int k = 0;
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++)
            {
                int a = i * (n + 1) + j, b = a + n + 1;
                tri[k++] = a; tri[k++] = a + 1; tri[k++] = b; tri[k++] = b; tri[k++] = a + 1; tri[k++] = b + 1;
            }
            m.vertices = v; m.uv = uv; m.triangles = tri;
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        static void Sea()
        {
            var A = SliceAssets.I;
            var sea = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sea.name = "TideSea";
            Object.Destroy(sea.GetComponent<Collider>());
            sea.transform.SetParent(root);
            sea.transform.position = new Vector3(0, -2.6f, 0);
            sea.transform.rotation = Quaternion.Euler(90, 0, 0);
            sea.transform.localScale = Vector3.one * 900f;
            var seaMat = new Material(A.tideShader);
            seaMat.SetTexture("_Noise", SeamlessNoise());   // 原来的噪声图不是无缝的，海面上看得出一格一格
            // 2026-10-09 打磨：原来 28 米一个重复周期、浪尖很亮，远看是一片明显的格子花纹；拉大尺度、压暗浪尖
            seaMat.SetFloat("_Glow", 0.7f);
            seaMat.SetFloat("_Scale", 0.0085f);
            seaMat.SetFloat("_Speed", 0.006f);
            seaMat.SetColor("_Deep", new Color(0.008f, 0.02f, 0.026f));
            seaMat.SetColor("_Crest", new Color(0.1f, 0.62f, 0.6f));
            sea.GetComponent<Renderer>().sharedMaterial = seaMat;
            sea.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // ---------------- 道具摆放 ----------------
        static PropAsset P(string id)
        {
            var A = SliceAssets.I;
            switch (id)
            {
                case "pillar_broken": return A.pillarBroken;
                case "pillar_fallen": return A.pillarFallen;
                case "brazier": return A.brazier;
                case "tide_crystal": return A.tideCrystal;
                case "wall_ruin": return A.wallRuin;
                case "statue_hooded": return A.statueHooded;
            }
            return A.MapProp(id);
        }

        // 放一个道具。collide：加一个按包围盒算的碰撞体（同时用来生成导航网格）；footprint 缩小碰撞体（树干比树冠小）
        static Transform Put(string id, Vector3 pos, float yaw, float size, bool byLength, bool collide, float footprint = 1f, bool emissive = false)
        {
            var p = P(id);
            if (p == null || !p.model) return null;
            Color em = emissive ? ArenaBuilder.Teal * 2.2f : Color.black;
            var t = ArenaBuilder.Prop(root, p, id, new Vector3(pos.x, 0f, pos.z), Quaternion.Euler(0f, yaw, 0f), size, byLength,
                PropTint, em, 0.18f, out var dims);
            AddPropRim(t);
            if (collide)
            {
                var bc = t.gameObject.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, dims.y * 0.5f, 0f);
                bc.size = new Vector3(dims.x * footprint, dims.y, dims.z * footprint);
            }
            occupied.Add(new Vector3(pos.x, pos.z, Mathf.Max(dims.x, dims.z) * 0.5f));
            return t;
        }

        // 环上的点：角度和 yaw 同一个约定（0° = +z，顺时针），这样「a + 90」就是环在该点的切线朝向。
        // 2026-10-09 修：原来用的是数学约定（0° = +x，逆时针），和 FaceTo 算出来的 yaw 混用——营地栅栏、据点断墙、巢穴崖壁的朝向都歪了，入口也没真正朝着出生点
        // 2026-10-09 打磨（用户反馈「道具小、暗、引不起注意」）：开放地图的道具比竞技场亮一档（原来和地面一样灰暗，糊在一起），
        // 再叠一圈很弱的暖白轮廓光，让道具的剪影从地面上跳出来
        static readonly Color PropTint = new Color(1f, 0.97f, 0.92f);
        static Material propRim;
        static void AddPropRim(Transform t)
        {
            if (!propRim)
            {
                var sh = Shader.Find("BlackTide/Rim");
                if (!sh) return;
                propRim = new Material(sh);
                propRim.SetColor("_Color", new Color(1f, 0.86f, 0.66f));
                propRim.SetFloat("_Intensity", 0.32f);
                propRim.SetFloat("_Power", 3.2f);
            }
            foreach (var r in t.GetComponentsInChildren<MeshRenderer>())
            {
                var ms = r.sharedMaterials;
                var nm = new Material[ms.Length + 1];
                ms.CopyTo(nm, 0); nm[ms.Length] = propRim;
                r.sharedMaterials = nm;
            }
        }

        static Vector3 OnRing(Vector3 c, float r, float deg) => c + new Vector3(Mathf.Sin(deg * Mathf.Deg2Rad), 0f, Mathf.Cos(deg * Mathf.Deg2Rad)) * r;
        static float FaceTo(Vector3 from, Vector3 to) { Vector3 d = to - from; return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }

        // 边界：四条边上排崖壁，同时挡住出界
        static void Boundary()
        {
            // 2026-10-09 打磨：原来每 5.4 米一块同样大小、同样朝向的崖石，排成一道砖墙。角色本来就被限制在可玩区内，
            // 这里只管好看：礁石成簇、大小朝向随机、前后错落、留缺口，间或混珊瑚礁
            float h = MapLayout.Half - 2f;
            for (int side = 0; side < 4; side++)
            {
                for (float s = -h; s <= h; s += Random.Range(4f, 9f))
                {
                    float off = Random.Range(-3.5f, 4.5f);   // 正值往海里
                    float e = h + off;
                    Vector3 pos = side == 0 ? new Vector3(s, 0, e) : side == 1 ? new Vector3(s, 0, -e) : side == 2 ? new Vector3(e, 0, s) : new Vector3(-e, 0, s);
                    if (pos.x > 60f && pos.z > 60f) continue;   // 黑潮之心所在的东北角留出海景
                    if (NearPier(pos)) continue;                 // 码头
                    float r = Random.value;
                    if (r < 0.18f) continue;                     // 缺口
                    if (r < 0.28f) { Put("41_coral_rock", pos, Random.Range(0f, 360f), Random.Range(3f, 4.5f), false, true, 0.8f); continue; }
                    // 第六批 78 海岸崖壁段：长边顺着海岸线（东西两条边转 90°），偶尔混一块旧崖石
                    float along = side < 2 ? 90f : 0f;
                    if (r < 0.85f && Put("78_sea_cliff", pos, along + (Random.value < 0.5f ? 180f : 0f) + Random.Range(-12f, 12f), Random.Range(9f, 12f), true, true)) { s += 4f; continue; }
                    Put("21_cliff_rock", pos, Random.Range(0f, 360f), Random.Range(3.5f, 8.5f), true, true);
                }
            }
        }

        // 每种节点的布景（梯度：越靠内圈，黑潮结晶越多）
        static void Dress(NodeDef n)
        {
            Vector3 c = n.World;
            Vector3 toSpawn = (new Vector3(MapLayout.Spawn.x, 0, MapLayout.Spawn.y) - c).normalized;
            float face = FaceTo(c, c + toSpawn);   // 入口朝出生点方向
            switch (n.type)
            {
                case NodeType.Stronghold:
                    Put("19_watchtower", OnRing(c, n.radius - 2f, face + 160f), face, 10.5f, false, true, 0.7f);
                    for (int i = 0; i < 3; i++) { float a = face + 100f + i * 50f; Put("wall_ruin", OnRing(c, n.radius + 1.5f, a), a + 90f, 6.5f, true, true, 1f); }
                    Torch(OnRing(c, n.radius - 1f, face + 30f)); Torch(OnRing(c, n.radius - 1f, face - 30f));
                    Put("46_war_banner", OnRing(c, n.radius - 0.5f, face + 14f), face, 5.8f, false, true, 0.3f);
                    Put("46_war_banner", OnRing(c, n.radius - 0.5f, face - 14f), face, 5.8f, false, true, 0.3f);
                    Put("52_supply_crates", OnRing(c, n.radius - 3.5f, face + 220f), Random.Range(0f, 360f), 2.2f, true, true, 0.9f);
                    Put("44_stone_lantern", OnRing(c, 3f, face + 180f), face, 2f, false, true, 0.6f);
                    break;
                case NodeType.Camp:
                    for (int i = 0; i < 3; i++) { float a = face + 120f + i * 60f; var p = OnRing(c, n.radius - 4f, a); Put("25_camp_tent", p, FaceTo(p, c), 4.4f, true, true, 0.8f); }
                    Put("16_camp_chest", c + toSpawn * -2f, face, 1.3f, true, true);
                    Torch(OnRing(c, 4f, face + 90f)); Torch(OnRing(c, 4f, face - 90f));
                    Put("pillar_fallen", OnRing(c, n.radius - 2f, face - 60f), Random.Range(0f, 360f), 3f, true, true);
                    // 篱笆：首尾相接围住背面（每段 4 米，按半径算角度步长），只留两三个缺口，每段带一点歪斜像手搭的
                    {
                        float fr = n.radius + 0.5f, step = 3.9f / fr * Mathf.Rad2Deg;
                        for (float a = face + 55f; a <= face + 305f; a += step)
                            if (Random.value < 0.88f) Put("48_wooden_fence", OnRing(c, fr + Random.Range(-0.3f, 0.3f), a), a + 90f + Random.Range(-5f, 5f), 4f, true, false);   // 篱笆不带碰撞（2026-10-09 验证局：导航从两段篱笆的接缝穿过，队长卡了 6 分钟）
                    }
                    Put("46_war_banner", OnRing(c, 2f, face + 180f), face, 5.5f, false, true, 0.3f);
                    Put("52_supply_crates", OnRing(c, n.radius - 3f, face + 75f), Random.Range(0f, 360f), 2f, true, true, 0.9f);
                    break;
                case NodeType.Nest:
                    // 崖壁围成一圈，只在朝出生点的一侧留入口，背后是封印门
                    for (float a = face + 40f; a <= face + 320f; a += 26f)
                        Put("21_cliff_rock", OnRing(c, n.radius + 2.5f, a), a + 90f, Random.Range(5.5f, 6.5f), true, true);
                    Put("14_nest_gate", OnRing(c, n.radius - 1.5f, face + 180f), face, 5.5f, false, true, 0.6f, true);
                    Put("15_reward_altar", c, face, 2.6f, true, false);
                    if (n.id == "N2") Put("45_sunken_bell", OnRing(c, 6f, face + 125f), Random.Range(0f, 360f), 4.5f, false, true, 0.8f);
                    else Put("51_colossus_head", OnRing(c, 6.5f, face + 125f), face + 200f, 5.5f, false, true, 0.8f);
                    break;
                case NodeType.Rift:
                    Put("13_tide_rift", c, Random.Range(0f, 360f), 9f, true, false, 1f, true);
                    Put("15_reward_altar", OnRing(c, n.radius - 3f, face), face, 2.6f, true, true);
                    for (int i = 0; i < 4; i++) Put("tide_crystal", OnRing(c, n.radius + 0.5f, face + 45f + i * 90f), Random.Range(0f, 360f), 2.2f, false, true, 0.6f, true);
                    for (int i = 0; i < 3; i++) Put("41_coral_rock", OnRing(c, n.radius + 3f, face + 20f + i * 120f), Random.Range(0f, 360f), Random.Range(3f, 4.2f), false, true, 0.8f);
                    break;
                case NodeType.WildBoss:
                    for (int i = 0; i < 5; i++) Put("pillar_broken", OnRing(c, n.radius + 1.5f, face + 36f + i * 72f), Random.Range(0f, 360f), Random.Range(6.5f, 8.5f), false, true, 0.6f);
                    Put("statue_hooded", OnRing(c, n.radius + 3f, face + 180f), face, 5f, false, true, 0.6f);
                    Put("40_beast_ribcage", OnRing(c, n.radius + 6f, face + 110f), face + 30f, 14f, true, true, 0.5f);
                    break;
                case NodeType.Merchant:
                    Put("18_merchant_cart", c, face + 90f, 3.2f, true, true, 0.8f);
                    Torch(c + toSpawn * 2.6f);
                    Put("52_supply_crates", OnRing(c, 3.2f, face + 200f), Random.Range(0f, 360f), 1.8f, true, true, 0.9f);
                    Put("44_stone_lantern", OnRing(c, 3.4f, face - 70f), face, 1.8f, false, true, 0.6f);
                    break;
                case NodeType.Heart:
                    Put("20_tide_source", OnRing(c, 6f, 45f), 225f, 7f, false, true, 0.5f, true);
                    for (int i = 0; i < 10; i++) Put("pillar_broken", OnRing(c, n.radius + 2f, i * 36f), Random.Range(0f, 360f), Random.Range(5f, 8f), false, true, 0.6f);
                    for (int i = 0; i < 6; i++) Put("tide_crystal", OnRing(c, n.radius - 3f, 20f + i * 60f), Random.Range(0f, 360f), 2.4f, false, true, 0.6f, true);
                    Put("53_heart_gate", OnRing(c, n.radius + 1f, face), face + 90f, 10f, false, false);   // 模型的门洞朝本地 X，转 90° 才正对出生点；不加碰撞，队伍从门洞走进去
                    break;
                case NodeType.Pack:
                    if (Random.value < 0.6f) Put("23_dead_tree", OnRing(c, n.radius + 2f, Random.Range(0f, 360f)), Random.Range(0f, 360f), 5f, false, true, 0.3f);
                    break;
            }
            if (n.type != NodeType.Pack && n.type != NodeType.Merchant) occupied.Add(new Vector3(c.x, c.z, n.radius + 2f));
        }

        static void Torch(Vector3 p)
        {
            var t = Put("brazier", p, Random.Range(0f, 360f), 1.4f, false, true, 0.7f);
            if (!t) return;
            ArenaBuilder.Flame(root, p + Vector3.up * 1.29f, 0.43f);
            var l = new GameObject("TorchLight").AddComponent<Light>();
            l.transform.SetParent(root);
            l.transform.position = p + Vector3.up * 2.2f;
            l.type = LightType.Point; l.color = new Color(1f, 0.55f, 0.22f); l.intensity = 3f; l.range = 9f; l.shadows = LightShadows.None;
            l.gameObject.AddComponent<Flicker>();
        }

        // ---------------- 区域主题（2026-10-09 打磨） ----------------
        // 原来全图随机撒单件道具（枯树、残屋、拱门、断柱……混在一起），每个地方看起来都一样，也没有「这里发生过什么」。
        // 现在按到黑潮中心的距离分三圈（和地面着色器 BT_Ground 的分界一致），每圈有自己的道具组合，成组摆放：
        //   外圈 > 165 米「海岸渔村」：残屋 + 篱笆 + 货箱、废弃帐篷、枯树林，路边是火盆；
        //   中圈「古城遗迹」：柱廊、拱门 + 断墙、兜帽雕像小祭坛，路边是石灯；
        //   内圈 < 120 米「黑潮腐地」：珊瑚礁 + 结晶、搁浅的鳐鱼骸、潮池，路边是发光的黑潮结晶。
        public static int Zone(Vector2 p) { float d = MapLayout.TideDist(p); return d > 165f ? 0 : d > 120f ? 1 : 2; }

        public static readonly List<Vector3> vignettes = new List<Vector3>();   // 成组布景的中心（x, z, 区域），截图检查用

        static void Scatter()
        {
            vignettes.Clear();
            Ridges();
            Docks();
            Landmarks();
            RoadLights();
            int placed = 0, tries = 0;
            int[] rr = { Random.Range(0, 4), Random.Range(0, 5), Random.Range(0, 5) };
            while (placed < 46 && tries < 6000)
            {
                tries++;
                var p = new Vector2(Random.Range(-MapLayout.Half + 12f, MapLayout.Half - 12f), Random.Range(-MapLayout.Half + 12f, MapLayout.Half - 12f));
                if (Vector2.Distance(p, MapLayout.Spawn) < 16f || !Free(p, 8f)) continue;
                if (p.x > 60f && p.y > 60f) continue;   // 黑潮之心周围留空
                float yaw = Random.Range(0f, 360f);
                Vector3 c = new Vector3(p.x, 0f, p.y);
                switch (Zone(p))
                {
                    // 按种类轮流摆（从随机起点开始），保证每种布景都会出现，不会连着三组都是枯树林
                    case 0: Village(c, yaw, (rr[0]++) % 4); break;
                    case 1: Ruins(c, yaw, (rr[1]++) % 5); break;
                    default: Blight(c, yaw, (rr[2]++) % 5); break;
                }
                vignettes.Add(new Vector3(p.x, p.y, Zone(p)));
                placed++;
            }
            Clutter();
            // 零散填充：每圈各自的单件
            string[][] fill = { new[] { "23_dead_tree", "52_supply_crates", "23_dead_tree" }, new[] { "pillar_fallen", "pillar_broken", "21_cliff_rock" }, new[] { "tide_crystal", "41_coral_rock", "23_dead_tree" } };
            int filled = 0; tries = 0;
            while (filled < 40 && tries < 3000)
            {
                tries++;
                var p = new Vector2(Random.Range(-MapLayout.Half + 10f, MapLayout.Half - 10f), Random.Range(-MapLayout.Half + 10f, MapLayout.Half - 10f));
                if (Vector2.Distance(p, MapLayout.Spawn) < 14f || !Free(p, 3f)) continue;
                int z = Zone(p);
                string id = fill[z][Random.Range(0, 3)];
                Single(id, new Vector3(p.x, 0f, p.y), Random.Range(0f, 360f));
                filled++;
            }
        }


        // ---------------- 区域分界（2026-10-09 打磨：用户反馈「单薄、没有关卡感、没有引导」） ----------------
        // 原来整张图是一块空地，三圈之间没有任何边界。现在：
        //   外圈 | 中圈（离黑潮中心 165 米）：一道断续的崖壁山脊，两排错落；
        //   中圈 | 内圈（120 米）：残破的古城墙，每隔约 40 米一座塌了一半的塔楼，四分之一的墙段塌成碎石；
        // 道路穿过的地方是关口（山脊关口立火盆 + 战旗，城墙关口立两根巨柱 + 石灯），节点本身也是缺口。
        // 效果：从出生点往黑潮走要「穿过关口进入下一区」，路线被收拢到道路上，关口火光远远就能看到。
        static void Ridges()
        {
            Ridge(165f, false);
            Ridge(120f, true);
        }

        static void Ridge(float r, bool wall)
        {
            // 第六批模型（2026-10-09）：外圈山脊用 78 海岸崖壁段（10 米一段，首尾相接），古城墙用 70 城墙段（8 米一段）+ 49 塌塔，关口用 71 城门
            Vector2 tc = MapLayout.TideCenter;
            float step = wall ? 7.6f : 9.2f;
            float lim = MapLayout.Half - 7f;
            int segIdx = 0;
            for (float s = 0f; s < 2f * Mathf.PI * r; s += step)
            {
                float th = s / r;
                var p = tc + new Vector2(Mathf.Cos(th), Mathf.Sin(th)) * r;
                if (Mathf.Abs(p.x) > lim || Mathf.Abs(p.y) > lim) continue;
                if (RoadDist(p) < (wall ? 7f : 6.5f)) continue;          // 关口（城门约 12 米宽）
                if (wall) { bool atGate = false; foreach (var g in wallGates) if (Vector2.Distance(g, p) < 8.5f) atGate = true; if (atGate) continue; }
                bool nearNode = false;   // 节点本身就是缺口（墙接到节点边上，节点像关隘）
                foreach (var n in MapLayout.Nodes) if (Vector2.Distance(p, n.pos) < n.radius + (n.type == NodeType.Pack || n.type == NodeType.Merchant ? 4f : 2.5f)) { nearNode = true; break; }
                if (nearNode || Vector2.Distance(p, MapLayout.Spawn) < 20f) continue;
                Vector2 tan = new Vector2(-Mathf.Sin(th), Mathf.Cos(th));
                float yaw = Mathf.Atan2(tan.x, tan.y) * Mathf.Rad2Deg;   // 城墙段、崖壁段的长边在模型本地 z 上
                Vector2 outN = (p - tc).normalized;
                segIdx++;
                if (wall)
                {
                    var w = new Vector3(p.x, 0f, p.y);
                    if (segIdx % 6 == 0) { Put("49_ruined_tower", w, Random.Range(0f, 360f), 11f, false, true, 0.6f); continue; }
                    if (Random.value < 0.15f)
                    {
                        // 塌掉的一段：碎石 + 倒柱
                        Put("21_cliff_rock", w + new Vector3(outN.x, 0, outN.y) * Random.Range(-1f, 1f), Random.Range(0f, 360f), Random.Range(3.5f, 4.5f), true, true);
                        Put("pillar_fallen", w + new Vector3(tan.x, 0, tan.y) * 2f, yaw + Random.Range(-30f, 30f), 4.5f, true, true);
                        continue;
                    }
                    if (!Put("70_city_wall", w, yaw + (Random.value < 0.5f ? 180f : 0f), 8.1f, true, true))
                        Put("wall_ruin", w, yaw, 7.5f, true, true);
                }
                else
                {
                    var q = p + outN * Random.Range(-1f, 1f);
                    if (!Put("78_sea_cliff", new Vector3(q.x, 0f, q.y), yaw + (Random.value < 0.5f ? 180f : 0f) + Random.Range(-6f, 6f), Random.Range(9.6f, 11f), true, true))
                        Put("21_cliff_rock", new Vector3(q.x, 0f, q.y), Random.Range(0f, 360f), Random.Range(6.5f, 9.5f), true, true);
                    if (Random.value < 0.3f)
                    {
                        var q2 = p + outN * Random.Range(4f, 6f) * (Random.value < 0.5f ? 1f : -1f) + tan * Random.Range(-2f, 2f);
                        Put("21_cliff_rock", new Vector3(q2.x, 0f, q2.y), Random.Range(0f, 360f), Random.Range(3.5f, 5f), true, true);
                    }
                    if (Random.value < 0.1f) { var q3 = p + outN * 5.5f; Put("23_dead_tree", new Vector3(q3.x, 0f, q3.y), Random.Range(0f, 360f), 7f, false, true, 0.3f); }
                }
            }
            // 关口：每段道路和分界圆的交点（城墙上的城门已经在 WallGates 里算好，门前后的路也和城墙相交，合并掉重复的）
            var gates = new List<Vector2>();
            foreach (var (a, b) in roads)
            {
                float da = Vector2.Distance(a, tc) - r, db = Vector2.Distance(b, tc) - r;
                if (da * db > 0f) continue;
                var x0 = Vector2.Lerp(a, b, da / (da - db));
                bool dup = false; foreach (var g in gates) if (Vector2.Distance(g, x0) < 12f) dup = true;
                if (!dup) gates.Add(x0);
            }
            foreach (var x in gates)
            {
                if (Mathf.Abs(x.x) > lim || Mathf.Abs(x.y) > lim) continue;
                bool nearNode = false;
                foreach (var n in MapLayout.Nodes) if (Vector2.Distance(x, n.pos) < n.radius + 4f) { nearNode = true; break; }
                if (nearNode) continue;
                Vector2 tan = Vector2.Perpendicular((x - tc).normalized);
                float yaw = Mathf.Atan2(tan.x, tan.y) * Mathf.Rad2Deg;
                Vector2 outN = (x - tc).normalized;
                var xw = new Vector3(x.x, 0f, x.y);
                if (wall)
                {
                    // 城门：门洞对着道路；不能用整块包围盒当碰撞体（会把路堵死），只给两侧塔楼各一个碰撞体
                    var g = Put("71_city_gate", xw, yaw, 12.5f, true, false);
                    if (g)
                    {
                        float L = 12.5f, H = 12.5f * 0.61f, Wd = 12.5f * 0.4f;
                        for (int sgn = -1; sgn <= 1; sgn += 2)
                        {
                            var bc = g.gameObject.AddComponent<BoxCollider>();
                            bc.center = new Vector3(0f, H * 0.5f, sgn * L * 0.37f);
                            bc.size = new Vector3(Wd, H, L * 0.26f);
                        }
                        // 门外（朝外圈一侧）两尊持杖石像守门
                        for (int sgn = -1; sgn <= 1; sgn += 2)
                        {
                            var st = x + outN * 5.5f + tan * 5f * sgn;
                            Put("74_staff_statue", new Vector3(st.x, 0f, st.y), Mathf.Atan2(outN.x, outN.y) * Mathf.Rad2Deg, 6.5f, false, true, 0.5f);
                        }
                        var lw = new Vector3(x.x + outN.x * 3f + tan.x * 3.6f, 0f, x.y + outN.y * 3f + tan.y * 3.6f); LanternGlow(lw);
                        lw = new Vector3(x.x + outN.x * 3f - tan.x * 3.6f, 0f, x.y + outN.y * 3f - tan.y * 3.6f); LanternGlow(lw);
                        continue;
                    }
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        var gg = x + tan * 5.2f * sgn;
                        Put("pillar_broken", new Vector3(gg.x, 0f, gg.y), Random.Range(0f, 360f), 8.5f, false, true, 0.6f);
                    }
                }
                else
                {
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        var gg = x + tan * 5.2f * sgn;
                        Torch(new Vector3(gg.x, 0f, gg.y));
                        var bn = x + tan * 6.8f * sgn; Put("46_war_banner", new Vector3(bn.x, 0f, bn.y), yaw + 90f, 5.5f, false, true, 0.3f);
                    }
                    // 路标：关口外侧（朝出生点那边），箭头指着道路
                    var sp = x + outN * 4.5f + tan * 3.4f;
                    Put("79_signpost", new Vector3(sp.x, 0f, sp.y), Mathf.Atan2(-outN.x, -outN.y) * Mathf.Rad2Deg, 4.2f, false, true, 0.5f, true);
                }
            }
        }

        // 地面碎景：小石块、小结晶、碎柱，没有碰撞（不挡路、不进导航网格），只打破大片空地
        static void Clutter()
        {
            int n = 0, tries = 0;
            while (n < 260 && tries < 4000)
            {
                tries++;
                var p = new Vector2(Random.Range(-MapLayout.Half + 6f, MapLayout.Half - 6f), Random.Range(-MapLayout.Half + 6f, MapLayout.Half - 6f));
                if (RoadDist(p) < 2.5f) continue;
                bool inNode = false;
                foreach (var nd in MapLayout.Nodes) if (Vector2.Distance(p, nd.pos) < nd.radius * 0.7f) { inNode = true; break; }
                if (inNode) continue;
                var w = new Vector3(p.x, 0f, p.y);
                int z = Zone(p);
                float r = Random.value;
                Transform t;
                if (z == 2 && r < 0.45f) t = Put("tide_crystal", w, Random.Range(0f, 360f), Random.Range(0.6f, 1.1f), false, false, 1f, true);
                else if (z == 1 && r < 0.3f) t = Put("pillar_fallen", w, Random.Range(0f, 360f), Random.Range(1.2f, 1.8f), true, false);
                else t = Put("21_cliff_rock", w, Random.Range(0f, 360f), Random.Range(0.8f, 1.7f), true, false);
                if (t) occupied.RemoveAt(occupied.Count - 1);   // 碎景不占位
                n++;
            }
        }

        // 单件（统一尺寸和碰撞体参数）
        // 布景尺寸倍数：镜头离角色 13–19 米，按真实尺寸放的房子、树、柱子在屏幕上只是小摆件；放大到「环境」的尺度
        static float Big(string id)
        {
            switch (id)
            {
                case "24_ruined_house": return 1.6f;
                case "23_dead_tree": return 1.5f;
                case "22_ruin_arch": return 1.6f;
                case "pillar_broken": case "pillar_fallen": case "wall_ruin": return 1.5f;
                case "statue_hooded": return 1.7f;
                case "25_camp_tent": return 1.35f;
                case "44_stone_lantern": case "21_cliff_rock": case "tide_crystal": case "17_ray_husk": return 1.3f;
                case "41_coral_rock": return 1.4f;
                case "52_supply_crates": return 1.25f;
                case "50_tide_pool": return 1.2f;
            }
            return 1f;
        }

        static Transform Single(string id, Vector3 p, float yaw, float scale = 1f)
        {
            scale *= Big(id);
            switch (id)
            {
                case "23_dead_tree": return Put(id, p, yaw, 5f * scale * Random.Range(0.85f, 1.15f), false, true, 0.3f);
                case "24_ruined_house": return Put(id, p, yaw, 5.5f * scale, true, true, 0.9f);
                case "25_camp_tent": return Put(id, p, yaw, 3.2f * scale, true, true, 0.8f);
                case "52_supply_crates": return Put(id, p, yaw, 1.9f * scale, true, true, 0.9f);
                case "48_wooden_fence": return Put(id, p, yaw, 4f * scale, true, false);   // 纯装饰，不挡路
                case "22_ruin_arch": return Put(id, p, yaw, 5f * scale, false, true, 0.4f);
                case "pillar_broken": return Put(id, p, yaw, Random.Range(4f, 5.5f) * scale, false, true, 0.6f);
                case "pillar_fallen": return Put(id, p, yaw, 3f * scale, true, true);
                case "wall_ruin": return Put(id, p, yaw, 4f * scale, true, true);
                case "statue_hooded": return Put(id, p, yaw, 3f * scale, false, true, 0.6f);
                case "44_stone_lantern": return Put(id, p, yaw, 2f * scale, false, true, 0.6f);
                case "21_cliff_rock": return Put(id, p, yaw, Random.Range(4f, 6f) * scale, true, true);
                case "41_coral_rock": return Put(id, p, yaw, Random.Range(2.8f, 4f) * scale, false, true, 0.8f);
                case "tide_crystal": return Put(id, p, yaw, Random.Range(1.4f, 2.6f) * scale, false, true, 0.6f, true);
                case "17_ray_husk": return Put(id, p, yaw, 7f * scale, true, true, 0.6f);
                case "50_tide_pool": return Put(id, p, yaw, 6f * scale, true, false);
                // 第六批（2026-10-09）
                case "64_drying_rack": return Put(id, p, yaw, 4.5f * scale, true, true, 0.8f);
                case "65_fishing_nets": return Put(id, p, yaw, 2.6f * scale, true, true, 0.8f);
                case "67_fishing_boat": return Put(id, p, yaw, 6.5f * scale, true, true, 0.8f);
                case "68_stilt_shack": return Put(id, p, yaw, 6.5f * scale, true, true, 0.85f);
                case "72_stele": return Put(id, p, yaw, 4.6f * scale, false, true, 0.6f);
                case "73_temple_base": return Put(id, p, yaw, 10f * scale, true, true, 0.95f);
                case "74_staff_statue": return Put(id, p, yaw, 7f * scale, false, true, 0.5f);
                case "75_coral_spire": return Put(id, p, yaw, 11.5f * scale, false, true, 0.45f, true);
                case "76_tentacle_arch": return Put(id, p, yaw, 10f * scale, true, true, 0.5f, true);
                case "77_swallowed_house": return Put(id, p, yaw, 7.5f * scale, true, true, 0.9f);
            }
            return Put(id, p, yaw, 3f * scale, true, true);
        }

        // 本地坐标（右 x、前 z，按 yaw 旋转）→ 世界坐标
        const float Spread = 1.5f;   // 布景放大后组内间距跟着拉开
        static Vector3 Local(Vector3 c, float yaw, float x, float z) => c + Quaternion.Euler(0f, yaw, 0f) * new Vector3(x, 0f, z) * Spread;

        // 外圈：海岸渔村
        static void Village(Vector3 c, float yaw, int k)
        {
            switch (k)
            {
                case 0:   // 渔家：残屋或高脚渔屋，门前一道篱笆，墙角货箱 / 渔网，屋后一棵枯树
                    if (Random.value < 0.5f) Single("68_stilt_shack", c, yaw); else Single("24_ruined_house", c, yaw);
                    for (int i = -1; i <= 1; i++) if (Random.value < 0.8f) Single("48_wooden_fence", Local(c, yaw, i * 2.6f, 6.5f), yaw + 90f);
                    Single(Random.value < 0.5f ? "65_fishing_nets" : "52_supply_crates", Local(c, yaw, 4.2f, 3f), Random.Range(0f, 360f));
                    if (Random.value < 0.7f) Single("23_dead_tree", Local(c, yaw, -4f, -4.5f), Random.Range(0f, 360f));
                    break;
                case 1:   // 晒鱼场：两排晾鱼架，渔网堆，一顶帐篷
                    Single("64_drying_rack", Local(c, yaw, -2.2f, 0f), yaw);
                    Single("64_drying_rack", Local(c, yaw, 2.2f, 0.6f), yaw + Random.Range(-8f, 8f));
                    Single("65_fishing_nets", Local(c, yaw, 0f, -3.6f), Random.Range(0f, 360f));
                    if (Random.value < 0.6f) Single("25_camp_tent", Local(c, yaw, 5f, -2f), yaw - 90f);
                    break;
                case 2:   // 搁浅的渔船：船侧翻，旁边渔网和货箱
                    Single("67_fishing_boat", c, yaw);
                    Single("65_fishing_nets", Local(c, yaw, 3.4f, 1.5f), Random.Range(0f, 360f));
                    if (Random.value < 0.6f) Single("52_supply_crates", Local(c, yaw, -3f, -2.4f), Random.Range(0f, 360f));
                    break;
                default:  // 枯树林：3–4 棵挤在一起
                    int n = Random.Range(3, 5);
                    for (int i = 0; i < n; i++) Single("23_dead_tree", Local(c, yaw + i * 360f / n, 0f, Random.Range(1.8f, 4f)), Random.Range(0f, 360f), Random.Range(0.85f, 1.2f));
                    break;
            }
        }

        // 中圈：古城遗迹
        static void Ruins(Vector3 c, float yaw, int k)
        {
            switch (k)
            {
                case 0:   // 柱廊：一排断柱，其中一根倒在旁边，尽头一块断碑
                    int n = Random.Range(3, 5), fall = Random.Range(0, n);
                    for (int i = 0; i < n; i++)
                    {
                        var p = Local(c, yaw, 0f, (i - (n - 1) * 0.5f) * 4.2f);
                        if (i == fall) Single("pillar_fallen", Local(c, yaw, 2.6f, (i - (n - 1) * 0.5f) * 4.2f), yaw + Random.Range(-20f, 20f));
                        else Single("pillar_broken", p, Random.Range(0f, 360f));
                    }
                    if (Random.value < 0.6f) Single("72_stele", Local(c, yaw, 0f, ((n - 1) * 0.5f + 1.3f) * 4.2f), yaw + 180f + Random.Range(-10f, 10f));
                    break;
                case 1:   // 城门遗迹：拱门两侧接断墙
                    Single("22_ruin_arch", c, yaw);
                    Single("wall_ruin", Local(c, yaw, 0f, -4.6f), yaw);
                    if (Random.value < 0.7f) Single("wall_ruin", Local(c, yaw, 0f, 4.6f), yaw);
                    break;
                case 2:   // 小祭坛：持杖石像或兜帽雕像，前面两盏相对的石灯，旁边一块断碑
                    Single(Random.value < 0.6f ? "74_staff_statue" : "statue_hooded", c, yaw);
                    Single("44_stone_lantern", Local(c, yaw, -2.4f, 3f), yaw + 180f);
                    Single("44_stone_lantern", Local(c, yaw, 2.4f, 3f), yaw + 180f);
                    if (Random.value < 0.5f) Single("72_stele", Local(c, yaw, -3.6f, -1.5f), yaw + Random.Range(-20f, 20f));
                    break;
                case 3:   // 神庙台基：抬高的石台，前面倒柱
                    Single("73_temple_base", c, yaw);
                    if (Random.value < 0.6f) Single("pillar_fallen", Local(c, yaw, 4.5f, 5f), yaw + Random.Range(60f, 120f));
                    break;
                default:  // 残屋和断墙
                    Single("24_ruined_house", c, yaw);
                    Single("wall_ruin", Local(c, yaw, 5f, 2f), yaw);
                    Single("pillar_broken", Local(c, yaw, -4.5f, 4f), Random.Range(0f, 360f), 0.8f);
                    break;
            }
        }

        // 内圈：黑潮腐地
        static void Blight(Vector3 c, float yaw, int k)
        {
            switch (k)
            {
                case 0:   // 珊瑚礁：一座黑潮珊瑚尖塔，周围两三块珊瑚礁和结晶
                    Single("75_coral_spire", c, Random.Range(0f, 360f));
                    for (int i = 0; i < 3; i++) if (i < 2 || Random.value < 0.6f) Single("41_coral_rock", Local(c, yaw + i * 120f, 0f, 3.4f), Random.Range(0f, 360f));
                    for (int i = 0; i < 2; i++) Single("tide_crystal", Local(c, yaw + 60f + i * 120f, 0f, Random.Range(1.6f, 2.6f)), Random.Range(0f, 360f));
                    break;
                case 1:   // 搁浅的鳐鱼骸，周围结晶
                    if (!Single("17_ray_husk", c, yaw)) Single("41_coral_rock", c, yaw);
                    for (int i = 0; i < 3; i++) Single("tide_crystal", Local(c, yaw + 40f + i * 110f, 0f, Random.Range(4f, 5.5f)), Random.Range(0f, 360f));
                    break;
                case 2:   // 石化触须拱：从地里拱出来又扎回去，旁边结晶
                    Single("76_tentacle_arch", c, yaw);
                    Single("tide_crystal", Local(c, yaw, 3.5f, 0f), Random.Range(0f, 360f));
                    Single("tide_crystal", Local(c, yaw, -3.2f, 1f), Random.Range(0f, 360f), 0.8f);
                    break;
                case 3:   // 被吞没的房屋：半边被黑潮晶体吞掉，门前倒着篱笆
                    Single("77_swallowed_house", c, yaw);
                    Single("48_wooden_fence", Local(c, yaw, 0f, 4.8f), yaw + 90f + Random.Range(-25f, 25f));
                    Single("tide_crystal", Local(c, yaw, 3.8f, 2.5f), Random.Range(0f, 360f));
                    break;
                default:  // 潮池：一汪发光的潮池，边上枯树和崖石
                    Single("50_tide_pool", c, yaw);
                    Single("23_dead_tree", Local(c, yaw, 6.5f, 1f), Random.Range(0f, 360f));
                    Single("21_cliff_rock", Local(c, yaw, -6.8f, -1f), Random.Range(0f, 360f), 0.8f);
                    Single("tide_crystal", Local(c, yaw, 1f, -5.5f), Random.Range(0f, 360f));
                    break;
            }
        }

        // 海岸码头（外圈的地图边缘）：两三节栈道伸进海里，旁边一条渔船、渔网和晾鱼架；位置在 Boundary 里预留
        static IEnumerable<(Vector3 pos, Vector3 outDir)> Piers()
        {
            float h = MapLayout.Half;
            yield return (new Vector3(-30f, 0f, -h), Vector3.back);
            yield return (new Vector3(24f, 0f, -h), Vector3.back);
            yield return (new Vector3(-h, 0f, -28f), Vector3.left);
            yield return (new Vector3(-h, 0f, 34f), Vector3.left);
        }
        static bool NearPier(Vector3 p) { foreach (var s in Piers()) if (Vector3.Distance(new Vector3(p.x, 0, p.z), s.pos) < 10f) return true; return false; }

        static void Docks()
        {
            foreach (var (pos, o) in Piers())
            {
                Vector3 side = Vector3.Cross(Vector3.up, o);
                float yaw = Mathf.Atan2(o.x, o.z) * Mathf.Rad2Deg;
                int n = Random.Range(2, 4);
                for (int i = 0; i < n; i++) Put("66_wooden_pier", pos - o * 6f + o * i * 5.9f, yaw, 6f, true, true, 0.9f);
                Put("67_fishing_boat", pos + o * 2f + side * 4.2f, yaw + Random.Range(-15f, 15f), 6.5f, true, true, 0.8f);
                Put("65_fishing_nets", pos - o * 10f + side * -3.5f, Random.Range(0f, 360f), 2.6f, true, true, 0.8f);
                Put("64_drying_rack", pos - o * 12f + side * 3f, yaw + 90f, 4.5f, true, true, 0.8f);
                Torch(pos - o * 9f + side * 2.2f);
            }
        }

        // 路边灯（原稿引导：顺着路走就能到下一个节点）：每 26 米一盏，左右交替；外圈火盆、中圈石灯、内圈黑潮结晶
        static void RoadLights()
        {
            float acc = 0f; int side = 1, lights = 0;
            foreach (var (a, b) in roads)
            {
                float len = Vector2.Distance(a, b);
                Vector2 dir = (b - a).normalized, nrm = new Vector2(-dir.y, dir.x);
                for (float s = 0f; s < len; s += 2f)
                {
                    acc += 2f;
                    if (acc < 26f) continue;
                    var p = a + dir * s + nrm * 3.6f * side;
                    if (!Free(p, 1.2f, true)) continue;
                    acc = 0f; side = -side;
                    var w = new Vector3(p.x, 0f, p.y);
                    float face = FaceTo(w, new Vector3(a.x + dir.x * s, 0f, a.y + dir.y * s));
                    switch (Zone(p))
                    {
                        case 0: if (lights < 14) { Torch(w); lights++; } else Single("52_supply_crates", w, face, 0.7f); break;
                        case 1: Single("44_stone_lantern", w, face); LanternGlow(w); break;
                        default: Single("tide_crystal", w, Random.Range(0f, 360f), 0.8f); break;
                    }
                }
            }
        }

        // 石灯里的一点暖光（不投影，范围小）
        static void LanternGlow(Vector3 p)
        {
            var l = new GameObject("LanternLight").AddComponent<Light>();
            l.transform.SetParent(root);
            l.transform.position = p + Vector3.up * 1.6f;
            l.type = LightType.Point; l.color = new Color(1f, 0.72f, 0.42f); l.intensity = 1.6f; l.range = 5.5f; l.shadows = LightShadows.None;
            ArenaBuilder.Flame(root, p + Vector3.up * 1.45f, 0.16f);
        }

        // 地标（第三批 39–53）：沉船、锚靠地图外缘；珊瑚、潮池靠黑潮；塔楼、阶梯、断桥、巨像头在中圈
        static void Landmarks()
        {
            int Place(string id, int count, float size, bool byLen, bool collide, float foot, float minD, float maxD, float r, bool edge, bool emissive = false)
            {
                int ok = 0, tries = 0;
                while (ok < count && tries < 1500)
                {
                    tries++;
                    var p = new Vector2(Random.Range(-MapLayout.Half + 10f, MapLayout.Half - 10f), Random.Range(-MapLayout.Half + 10f, MapLayout.Half - 10f));
                    if (edge && Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) < MapLayout.Half - 24f) continue;
                    if (edge && p.x > 60f && p.y > 60f) continue;   // 东北角是黑潮之心的海景
                    float d = MapLayout.TideDist(p);
                    if (d < minD || d > maxD || Vector2.Distance(p, MapLayout.Spawn) < 18f || !Free(p, r)) continue;
                    if (Put(id, new Vector3(p.x, 0, p.y), Random.Range(0f, 360f), size * Random.Range(0.9f, 1.1f), byLen, collide, foot, emissive)) ok++;
                }
                return ok;
            }
            Place("69_lighthouse", 1, 15f, false, true, 0.45f, 175f, 999f, 6f, true);   // 外圈海岸的地标，远处就能看到
            Place("39_shipwreck", 3, 15f, true, true, 0.7f, 150f, 999f, 8f, true);   // 沉船只在渔村一侧的海岸
            Place("47_giant_anchor", 3, 6f, false, true, 0.5f, 140f, 999f, 3f, true);
            Place("41_coral_rock", 6, 3.6f, false, true, 0.8f, 10f, 110f, 2.5f, false);
            Place("50_tide_pool", 4, 6f, true, false, 1f, 15f, 115f, 3.5f, false);
            Place("49_ruined_tower", 3, 9f, false, true, 0.6f, 120f, 170f, 5f, false);
            Place("43_ruined_stairs", 2, 7f, true, true, 0.9f, 120f, 170f, 4f, false);
            Place("42_broken_bridge", 1, 16f, true, true, 0.8f, 120f, 175f, 8f, false);
            Place("51_colossus_head", 1, 6.5f, false, true, 0.8f, 120f, 165f, 4f, false);
        }

        static bool Free(Vector2 p, float r, bool roadside = false)
        {
            if (roads.Count > 0 && RoadDist(p) < r + (roadside ? 1.5f : 3f)) return false;   // 道路上不放东西（路边灯离路面近一点）
            foreach (var o in occupied) if (Vector2.Distance(p, new Vector2(o.x, o.y)) < o.z + r) return false;
            foreach (var n in MapLayout.Nodes) if (Vector2.Distance(p, n.pos) < n.radius + r + 3f) return false;
            return true;
        }

        // ---------------- 导航网格：用碰撞体生成（地面 + 道具），在生成角色和敌人之前做 ----------------
        static void BakeNav()
        {
            var bounds = new Bounds(Vector3.zero, new Vector3(MapLayout.Half * 2f + 20f, 30f, MapLayout.Half * 2f + 20f));
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.6f;
            settings.agentHeight = 2f;
            settings.agentClimb = 0.4f;
            settings.agentSlope = 40f;
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            NavMesh.RemoveAllNavMeshData();
            NavMesh.AddNavMeshData(data);
            var tri = NavMesh.CalculateTriangulation();
            Debug.Log($"MAP_NAV sources={sources.Count} navVerts={tri.vertices.Length} props={occupied.Count}");
        }

        // 开放地图的位置约束：地图边界内，并吸附到导航网格上（敌人不会穿进道具）
        static Vector3 Clamp(Vector3 p, float r)
        {
            float lim = MapLayout.Half - 4f;
            p.x = Mathf.Clamp(p.x, -lim, lim);
            p.z = Mathf.Clamp(p.z, -lim, lim);
            if (NavMesh.SamplePosition(new Vector3(p.x, 0f, p.z), out var hit, 3f, NavMesh.AllAreas))
            {
                Vector3 h = hit.position;
                if ((h.x - p.x) * (h.x - p.x) + (h.z - p.z) * (h.z - p.z) > 0.0004f) { p.x = h.x; p.z = h.z; }
            }
            return p;
        }
    }

    // 漂浮的微光粒子跟着镜头走（竞技场里是固定范围，开放地图要跟随）
    public class MotesFollow : MonoBehaviour
    {
        void LateUpdate()
        {
            var p = Combat.player;
            if (p) transform.position = new Vector3(p.transform.position.x, 0f, p.transform.position.z);
        }
    }

    // 寻路：直线能走就直走，挡住了就按导航网格的路径点走（结果缓存一小段时间，省计算）
    public static class Nav
    {
        class Cache { public Vector3 to; public Vector3[] corners; public float t; }
        static readonly Dictionary<int, Cache> cache = new Dictionary<int, Cache>();
        static readonly NavMeshPath path = new NavMeshPath();

        public static void Clear() => cache.Clear();

        public static Vector3 Dir(Object who, Vector3 from, Vector3 to)
        {
            from.y = 0f; to.y = 0f;
            Vector3 direct = to - from;
            if (direct.sqrMagnitude < 0.01f) return Vector3.zero;
            if (Combat.clampOverride == null || !NavMesh.Raycast(from, to, out _, NavMesh.AllAreas)) return direct.normalized;
            int id = who.GetInstanceID();
            if (!cache.TryGetValue(id, out var c) || Time.time - c.t > 0.5f || (c.to - to).sqrMagnitude > 4f)
            {
                c = c ?? new Cache();
                c.to = to; c.t = Time.time;
                // 起点、终点先吸附到导航网格上：被挤进道具的碰撞范围里时，原地算路径会失败，AI 就会顶着障碍物直走卡死
                Vector3 f2 = NavMesh.SamplePosition(from, out var hf, 3f, NavMesh.AllAreas) ? hf.position : from;
                Vector3 t2 = NavMesh.SamplePosition(to, out var ht, 4f, NavMesh.AllAreas) ? ht.position : to;
                c.corners = NavMesh.CalculatePath(f2, t2, NavMesh.AllAreas, path) ? path.corners : null;
                cache[id] = c;
            }
            if (c.corners == null || c.corners.Length < 2) return direct.normalized;
            // 取第一个离自己超过 0.8 米的路径点
            for (int i = 1; i < c.corners.Length; i++)
            {
                Vector3 d = c.corners[i] - from; d.y = 0f;
                if (d.sqrMagnitude > 0.64f) return d.normalized;
            }
            return direct.normalized;
        }
    }
}

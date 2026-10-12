using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 迷雾和情报（M3，原稿「探索与决策」）：地图开局盖着黑潮迷雾，队伍走过的地方（半径 28 米）自动揭开；
    // 情报据点清除后探员给一份情报：揭开约 1/4 张地图，并标出全图的精英巢穴、野图怒涛和信使位置。
    // 迷雾只影响地图界面上能看到什么；路线 AI 仍然知道全部节点（它代表的是「会看攻略的玩家」，验证路线时不受迷雾影响）。
    public static class Intel
    {
        public const int N = 96;                 // 迷雾格子（220 米 / 96 ≈ 2.3 米一格）
        public const float WalkReveal = 28f, ScoutReveal = 60f, SpawnReveal = 45f;
        static bool[] seen;
        static readonly HashSet<string> marked = new HashSet<string>();   // 情报标出来的节点（不管在不在揭开的区域）
        public static Texture2D tex;
        static Color32[] px;
        static bool dirty;
        static float nextWalk;

        // 每个据点的情报覆盖哪一片（大致是它往黑潮方向的那一块，约占地图 1/4）
        static readonly Dictionary<string, Vector2> ScoutCenter = new Dictionary<string, Vector2>
        {
            { "S1", new Vector2(10f, -45f) },
            { "S2", new Vector2(-45f, 25f) },
        };

        public static void Reset()
        {
            seen = new bool[N * N];
            marked.Clear();
            px = new Color32[N * N];
            if (!tex) tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            Reveal(MapLayout.Spawn, SpawnReveal);
            Flush();
        }

        static int Cell(float w) => Mathf.Clamp(Mathf.FloorToInt((w + MapLayout.Half) / (MapLayout.Half * 2f) * N), 0, N - 1);

        public static void Reveal(Vector2 c, float r)
        {
            if (seen == null) return;
            float cs = MapLayout.Half * 2f / N;
            int x0 = Cell(c.x - r), x1 = Cell(c.x + r), y0 = Cell(c.y - r), y1 = Cell(c.y + r);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                Vector2 w = new Vector2((x + 0.5f) * cs - MapLayout.Half, (y + 0.5f) * cs - MapLayout.Half);
                if (!seen[y * N + x] && (w - c).sqrMagnitude <= r * r) { seen[y * N + x] = true; dirty = true; }
            }
        }

        public static bool Seen(Vector2 p) => seen == null || seen[Cell(p.y) * N + Cell(p.x)];

        // 地图上显示不显示这个节点：揭开的区域里、或被情报标出来；已经交过手的一定显示
        public static bool Known(NodeRuntime n) => seen == null || n.st != NodeRuntime.St.Idle || marked.Contains(n.def.id) || Seen(new Vector2(n.pos.x, n.pos.z));

        public static void Mark(string id) => marked.Add(id);

        public static float RevealedShare() { if (seen == null) return 1f; int k = 0; foreach (var b in seen) if (b) k++; return k / (float)seen.Length; }

        // 探员的情报
        public static void Scout(NodeRuntime stronghold, IEnumerable<NodeRuntime> all)
        {
            if (ScoutCenter.TryGetValue(stronghold.def.id, out var c)) Reveal(c, ScoutReveal);
            foreach (var n in all)
                if (n.def.type == NodeType.Nest || n.def.type == NodeType.WildBoss || n.def.type == NodeType.Messenger) marked.Add(n.def.id);
        }

        // RunDirector 每帧调：队伍走过的地方揭开；有变化时更新贴图
        public static void Tick()
        {
            if (seen == null) return;
            if (Time.time >= nextWalk)
            {
                nextWalk = Time.time + 0.25f;
                foreach (var h in Combat.heroes) if (h) Reveal(new Vector2(h.transform.position.x, h.transform.position.z), WalkReveal);
            }
            if (dirty) Flush();
        }

        static void Flush()
        {
            dirty = false;
            for (int i = 0; i < px.Length; i++)
            {
                // 边缘柔一点：看周围 4 格里揭开了几格
                int x = i % N, y = i / N, k = 0;
                if (seen[i]) k += 4;
                if (x > 0 && seen[i - 1]) k++;
                if (x < N - 1 && seen[i + 1]) k++;
                if (y > 0 && seen[i - N]) k++;
                if (y < N - 1 && seen[i + N]) k++;
                byte a = (byte)Mathf.Lerp(235f, 0f, k / 8f);
                px[i] = new Color32(14, 16, 22, a);
            }
            tex.SetPixels32(px);
            tex.Apply();
        }
    }
}

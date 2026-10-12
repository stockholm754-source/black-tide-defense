using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 一局的地图布局（M1）。数据来源：原稿「动态战备节点系统」与 SPEC 第 3.4 节的标准路线。
    // 地图 220×220 米，原点在中心；黑潮中心固定在东北角，出生点在西南角（离黑潮最远）。
    // 资源点按到黑潮中心的距离分三档：越靠近黑潮，越早被侵蚀（敌人更强），奖励也越好 —— 这就是原稿说的「梯度」。
    public enum NodeType { Stronghold, Camp, Nest, Rift, WildBoss, Merchant, Messenger, Heart, Pack, Vortex }

    public class NodeDef
    {
        public string id, name;
        public NodeType type;
        public Vector2 pos;      // 地面坐标 (x, z)
        public float radius;     // 节点范围（Boss 不出这个范围；离开范围仇恨重置）
        public int band;         // 0 外圈（出生点附近）/ 1 中圈 / 2 内圈（靠近黑潮）
        public NodeDef(string id, string name, NodeType t, float x, float z, float r, int band)
        { this.id = id; this.name = name; type = t; pos = new Vector2(x, z); radius = r; this.band = band; }
        public Vector3 World => new Vector3(pos.x, 0f, pos.y);
    }

    public static class MapLayout
    {
        public const float Half = 110f;                                  // 地图半边长
        // 黑潮漩涡（M5，原稿决胜期）：位置开局时不定，第 10 分钟刷在队伍最近的挑战点旁边；不在 Nodes 里，地图生成、瘴气不受影响
        public static readonly NodeDef Vortex = new NodeDef("V1", "黑潮漩涡", NodeType.Vortex, 0f, 0f, 12f, 2);
        public const float VortexAt = 10f;

        // 漩涡的位置：离队伍最近的未清挑战点（裂隙、巢穴、野图怒涛），往队伍方向挪出节点范围 18 米（原稿：「刷在必经路线附近」，不让玩家轻易绕开）
        public static Vector2 VortexSpot(Vector2 team, System.Func<NodeDef, bool> cleared)
        {
            NodeDef best = null; float bd = float.MaxValue;
            foreach (var d in Nodes)
            {
                if (d.type != NodeType.Rift && d.type != NodeType.Nest && d.type != NodeType.WildBoss) continue;
                if (cleared(d)) continue;
                float dd = Vector2.Distance(team, d.pos);
                if (dd < bd) { bd = dd; best = d; }
            }
            Vector2 p;
            if (best == null) p = team + (TideCenter - team).normalized * 25f;
            else
            {
                Vector2 dir = team - best.pos;
                dir = dir.sqrMagnitude > 1f ? dir.normalized : Vector2.right;
                p = best.pos + dir * (best.radius + 18f);
            }
            float lim = Half - 15f;
            return new Vector2(Mathf.Clamp(p.x, -lim, lim), Mathf.Clamp(p.y, -lim, lim));
        }
        public static readonly Vector2 TideCenter = new Vector2(88f, 88f);
        public static readonly Vector2 Spawn = new Vector2(-82f, -82f);

        // 节点数量按原稿：据点 2、营地 3、精英巢穴 2、裂隙固定 3、野图怒涛 4（原稿 4–6）、商人 4 + 终局前 1、信使 1、黑潮之心 1。
        // 另外散布 8 群游荡的轻波残像，跑图途中顺手清（原稿：经验货币唯一来源是击杀）。
        public static readonly List<NodeDef> Nodes = new List<NodeDef>
        {
            // ---- 外圈：出生点附近，前 3 分钟的发育区 ----
            new NodeDef("S1", "情报据点·西", NodeType.Stronghold, -58f, -88f, 12f, 0),
            new NodeDef("S2", "情报据点·南", NodeType.Stronghold, -92f, -50f, 12f, 0),
            new NodeDef("C1", "资源营地·焦木", NodeType.Camp, -32f, -64f, 16f, 0),
            new NodeDef("C2", "资源营地·断桥", NodeType.Camp, -70f, -18f, 16f, 0),
            new NodeDef("M1", "流浪商人", NodeType.Merchant, -54f, -52f, 5f, 0),

            // ---- 中圈：第 3–10 分钟的主战场 ----
            new NodeDef("C3", "资源营地·盐沼", NodeType.Camp, 8f, -84f, 16f, 1),
            new NodeDef("N1", "精英巢穴·潮洞", NodeType.Nest, -22f, -28f, 13f, 1),
            new NodeDef("R1", "黑潮裂隙·西", NodeType.Rift, -52f, 32f, 18f, 1),
            new NodeDef("R2", "黑潮裂隙·南", NodeType.Rift, 34f, -36f, 18f, 1),
            new NodeDef("W1", "怒涛·盐沼", NodeType.WildBoss, 52f, -76f, 14f, 1),
            new NodeDef("W2", "怒涛·西崖", NodeType.WildBoss, -88f, 40f, 14f, 1),
            new NodeDef("M2", "流浪商人", NodeType.Merchant, -6f, -52f, 5f, 1),
            new NodeDef("M3", "流浪商人", NodeType.Merchant, -40f, 6f, 5f, 1),

            // ---- 内圈：靠近黑潮，第 10 分钟后基本都在黑潮里 ----
            new NodeDef("N2", "精英巢穴·沉钟", NodeType.Nest, 62f, 4f, 13f, 2),
            new NodeDef("R3", "黑潮裂隙·北", NodeType.Rift, 14f, 44f, 18f, 2),
            new NodeDef("W3", "怒涛·潮门", NodeType.WildBoss, 80f, -26f, 14f, 2),
            new NodeDef("W4", "怒涛·北岸", NodeType.WildBoss, -16f, 82f, 14f, 2),
            new NodeDef("M4", "流浪商人", NodeType.Merchant, 30f, 10f, 5f, 2),
            new NodeDef("X1", "黑潮信使", NodeType.Messenger, 4f, 4f, 16f, 1),     // 会在中、内圈游荡
            new NodeDef("M5", "终局商人", NodeType.Merchant, 50f, 50f, 5f, 2),     // 黑潮之心前的准备区
            new NodeDef("H1", "黑潮之心", NodeType.Heart, 80f, 80f, 22f, 2),

            // ---- 游荡的轻波残像 ----
            new NodeDef("P1", "残像群", NodeType.Pack, -74f, -68f, 9f, 0),
            new NodeDef("P2", "残像群", NodeType.Pack, -44f, -36f, 9f, 0),
            new NodeDef("P3", "残像群", NodeType.Pack, -12f, -76f, 9f, 0),
            new NodeDef("P4", "残像群", NodeType.Pack, -82f, 6f, 9f, 1),
            new NodeDef("P5", "残像群", NodeType.Pack, 20f, -58f, 9f, 1),
            new NodeDef("P6", "残像群", NodeType.Pack, -24f, 14f, 9f, 1),
            new NodeDef("P7", "残像群", NodeType.Pack, 46f, -12f, 9f, 2),
            new NodeDef("P8", "残像群", NodeType.Pack, -12f, 54f, 9f, 2),
        };

        public static NodeDef Get(string id) { foreach (var n in Nodes) if (n.id == id) return n; return null; }

        // 到黑潮中心的距离（米）
        public static float TideDist(Vector2 p) => Vector2.Distance(p, TideCenter);

        // 原稿：黑潮覆盖全图的面积比例 —— 0–3 分钟 15%、3–10 分钟到 60%、10–18 分钟到 100%（各段匀速）
        // 注意这是「覆盖范围」，和「侵蚀值」（0–3 分钟 15%、3–10 分钟到 45%、10–18 分钟到 100%）是两条不同的曲线
        public static float CoverageAt(float minutes)
        {
            if (minutes <= 3f) return Mathf.Lerp(0f, 0.15f, minutes / 3f);
            if (minutes <= 10f) return Mathf.Lerp(0.15f, 0.6f, (minutes - 3f) / 7f);
            return Mathf.Lerp(0.6f, 1f, Mathf.Clamp01((minutes - 10f) / 8f));
        }

        // 覆盖比例 → 以黑潮中心为圆心的半径（地图是正方形，圆心在角上，用采样表反查）
        static float[] radiusTable;
        public static float RadiusForCoverage(float f)
        {
            if (radiusTable == null)
            {
                const int N = 120, R = 64;
                float maxR = Vector2.Distance(TideCenter, new Vector2(-Half, -Half)) + 2f;
                radiusTable = new float[R + 1];
                var d = new List<float>(N * N);
                for (int i = 0; i < N; i++) for (int j = 0; j < N; j++)
                    d.Add(TideDist(new Vector2(-Half + (i + 0.5f) * 2f * Half / N, -Half + (j + 0.5f) * 2f * Half / N)));
                d.Sort();
                for (int k = 0; k <= R; k++) radiusTable[k] = k == 0 ? 0f : k == R ? maxR : d[Mathf.Clamp(Mathf.RoundToInt(k / (float)R * d.Count) - 1, 0, d.Count - 1)];
            }
            float x = Mathf.Clamp01(f) * (radiusTable.Length - 1);
            int a = Mathf.FloorToInt(x), b = Mathf.Min(a + 1, radiusTable.Length - 1);
            return Mathf.Lerp(radiusTable[a], radiusTable[b], x - a);
        }
    }
}

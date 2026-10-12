using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 目标光柱（2026-10-09 打磨：用户反馈「做不到引导的感觉」）。
    // 每个还没清的目标点（据点、营地、巢穴、裂隙、野图怒涛、黑潮之心）立一道竖直光柱，颜色按类型：
    //   营地 金、据点 白金、巢穴 紫（上锁时暗）、裂隙 青、怒涛 红橙、黑潮之心 亮青（显形后才有）。
    // 镜头是斜俯视，光柱从节点往上 50 米，节点在屏幕外时柱子的上半段也会伸进画面，顺着光柱走就是下一个目标；
    // 走进节点范围后淡出（不挡战斗），清完熄灭。
    public class NodeBeacons : MonoBehaviour
    {
        class Beacon { public NodeRuntime n; public Transform t; public Material m; public Color c; public float a; }
        readonly List<Beacon> list = new List<Beacon>();
        static Texture2D beamTex;

        public void Build(IEnumerable<NodeRuntime> nodes)
        {
            foreach (var n in nodes)
            {
                Color c;
                switch (n.def.type)
                {
                    case NodeType.Camp: c = new Color(1f, 0.78f, 0.38f); break;
                    case NodeType.Stronghold: c = new Color(1f, 0.92f, 0.7f); break;
                    case NodeType.Nest: c = new Color(0.72f, 0.45f, 1f); break;
                    case NodeType.Rift: c = new Color(0.3f, 1f, 0.9f); break;
                    case NodeType.WildBoss: c = new Color(1f, 0.42f, 0.22f); break;
                    case NodeType.Heart: c = new Color(0.4f, 1f, 1f); break;
                    default: continue;
                }
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(go.GetComponent<Collider>());
                go.name = "Beacon_" + n.def.id;
                go.transform.SetParent(transform, false);
                go.transform.position = n.pos + Vector3.up * 25f;
                go.transform.localScale = new Vector3(n.def.type == NodeType.Heart ? 6f : 3.2f, 50f, 1f);
                var r = go.GetComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                var m = FX.NewMat(BeamTex(), true, c, 1.6f);
                r.sharedMaterial = m;
                list.Add(new Beacon { n = n, t = go.transform, m = m, c = c, a = 0f });
            }
        }

        // 光柱贴图：横向高斯（中间亮边缘柔），纵向底部最亮、往上渐隐
        static Texture2D BeamTex()
        {
            if (beamTex) return beamTex;
            const int W = 32, H = 128;
            beamTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "BeamTex" };
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W * 2f - 1f, v = (y + 0.5f) / H;
                    float hx = Mathf.Exp(-u * u * 6f) * 0.75f + Mathf.Exp(-u * u * 40f) * 0.5f;
                    float vy = Mathf.Pow(1f - v, 1.6f) * Mathf.Clamp01(v * 20f);
                    float a = Mathf.Clamp01(hx * vy);
                    px[y * W + x] = new Color(a, a, a, a);
                }
            beamTex.SetPixels(px);
            beamTex.Apply();
            return beamTex;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            var p = Combat.player ? Combat.player.transform.position : Vector3.zero;
            var rd = RunDirector.I;
            foreach (var b in list)
            {
                float target;
                if (b.n.Done) target = 0f;
                else if (b.n.def.type == NodeType.Heart) target = rd && rd.heartOpen ? 1f : 0f;
                else if (b.n.def.type == NodeType.Nest && b.n.locked) target = 0.35f;
                else target = b.n.st == NodeRuntime.St.Active ? 0.25f : 0.85f;
                // 走近了淡出：节点范围外 12 米开始淡，进到范围里只剩一点
                float d = Vector3.Distance(new Vector3(p.x, 0, p.z), b.n.pos);
                target *= Mathf.Lerp(0.12f, 1f, Mathf.Clamp01((d - b.n.def.radius) / 12f));
                b.a = Mathf.MoveTowards(b.a, target, Time.deltaTime * 0.8f);
                // 呼吸
                float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 1.6f + b.n.pos.x);
                var c = b.c; c.a = b.a * pulse;
                b.m.SetColor("_Color", c);
                b.t.gameObject.SetActive(b.a > 0.01f);
                // 绕竖直轴朝向镜头
                if (cam)
                {
                    Vector3 f = b.t.position - cam.transform.position; f.y = 0f;
                    if (f.sqrMagnitude > 0.01f) b.t.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
                }
            }
        }
    }
}

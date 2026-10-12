using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 瘴气（原稿，压力期 3–10 分钟）：全图游荡的 10 米瘴气圈；人在里面 20 秒内易伤累积到 30%，出来后慢慢消退。
    // 净化器清掉 12 米内的瘴气，被清掉的 60 秒后在别处重新长出来。
    // 原稿的策略呼吸点：瘴气避开必经路线——这里让它绕开各节点（节点半径 + 6 米的排斥区）。
    public class Miasma : MonoBehaviour
    {
        public static readonly List<Miasma> all = new List<Miasma>();
        public const float Radius = 10f, StartMin = 3f, EndMin = 10f, MaxVuln = 0.3f, RampSec = 20f;
        public const int Count = 6;
        static readonly Dictionary<HeroBase, float> vuln = new Dictionary<HeroBase, float>();

        Vector3 vel;
        float retarget;
        Transform disc, edge;
        Material discMat, edgeMat;
        float fade;

        public static bool Active => RunDirector.I && RunDirector.I.RunMinutes >= StartMin && RunDirector.I.RunMinutes < EndMin;

        public static bool Inside(Vector3 p)
        {
            foreach (var m in all) if (m.fade > 0.5f && Flat(m.transform.position - p).magnitude < Radius) return true;
            return false;
        }

        public static int Purify(Vector3 p, float r)
        {
            int n = 0;
            foreach (var m in all)
                if (m.gameObject.activeSelf && m.fade > 0.2f && Flat(m.transform.position - p).magnitude < r + Radius * 0.5f)
                {
                    m.Respawn(60f);
                    n++;
                }
            if (n > 0 && RunDirector.I) RunDirector.I.Event("purifier", "", $"cleared={n}");
            return n;
        }

        // RunDirector 开局时调用
        public static void Spawn(Transform parent)
        {
            all.Clear(); vuln.Clear();
            Economy.purify = Purify;
            var rng = new System.Random(MapBuilder.Seed + 77);
            for (int i = 0; i < Count; i++)
            {
                var go = new GameObject("Miasma" + i);
                go.transform.SetParent(parent);
                var m = go.AddComponent<Miasma>();
                m.Build();
                m.transform.position = m.FreeSpot(rng);
                all.Add(m);
            }
        }

        void Build()
        {
            Transform Make(string n, Mesh mesh, Color c, out Material mat)
            {
                var g = new GameObject(n);
                g.transform.SetParent(transform, false);
                g.transform.localPosition = Vector3.up * 0.06f;
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = g.AddComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                mat = new Material(SliceAssets.I.telegraphShader);
                mat.SetColor("_Color", c);
                mat.SetFloat("_Progress", 1f);
                mat.SetFloat("_Sides", 0f);
                r.sharedMaterial = mat;
                return g.transform;
            }
            disc = Make("Disc", Telegraph.Sector(0f, Radius, 360f, 64), new Color(0.32f, 0.1f, 0.38f, 0.3f), out discMat);
            edge = Make("Edge", Telegraph.Sector(Radius - 0.35f, Radius, 360f, 96), new Color(0.75f, 0.35f, 0.95f, 0.8f), out edgeMat);
        }

        Vector3 FreeSpot(System.Random rng)
        {
            for (int t = 0; t < 60; t++)
            {
                var p = new Vector3((float)(rng.NextDouble() * 2 - 1) * (MapLayout.Half - 20f), 0f, (float)(rng.NextDouble() * 2 - 1) * (MapLayout.Half - 20f));
                if (Allowed(p) && Vector2.Distance(new Vector2(p.x, p.z), MapLayout.Spawn) > 40f) return p;
            }
            return Vector3.zero;
        }

        static bool Allowed(Vector3 p)
        {
            foreach (var n in MapLayout.Nodes)
                if (n.type != NodeType.Pack && Vector2.Distance(new Vector2(p.x, p.z), n.pos) < n.radius + 6f + Radius * 0.6f) return false;
            float lim = MapLayout.Half - 12f;
            return Mathf.Abs(p.x) < lim && Mathf.Abs(p.z) < lim;
        }

        float respawnAt = -1f;
        void Respawn(float delay)
        {
            fade = 0f;
            respawnAt = Time.time + delay;
            transform.position = FreeSpot(new System.Random(Random.Range(0, 1 << 30)));
        }

        void Update()
        {
            bool on = Active && Time.time >= respawnAt;
            fade = Mathf.MoveTowards(fade, on ? 1f : 0f, Time.deltaTime / 2f);
            discMat.SetColor("_Color", new Color(0.32f, 0.1f, 0.38f, 0.3f * fade));
            edgeMat.SetColor("_Color", new Color(0.75f, 0.35f, 0.95f, 0.8f * fade));
            disc.gameObject.SetActive(fade > 0.01f); edge.gameObject.SetActive(fade > 0.01f);
            if (fade <= 0.01f) return;
            // 慢慢游荡：每 8–14 秒换个方向，碰到排斥区就掉头
            if (Time.time > retarget)
            {
                retarget = Time.time + Random.Range(8f, 14f);
                float a = Random.Range(0f, Mathf.PI * 2f);
                vel = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.6f;
            }
            Vector3 next = transform.position + vel * Time.deltaTime;
            if (Allowed(next)) transform.position = next; else { vel = -vel; retarget = Time.time + 4f; }
            if (Random.value < Time.deltaTime * 6f)
            {
                float a = Random.Range(0f, Mathf.PI * 2f), r = Random.Range(0f, Radius);
                FX.Sparks(transform.position + new Vector3(Mathf.Cos(a) * r, 0.3f, Mathf.Sin(a) * r), Vector3.up, 1, new Color(0.7f, 0.35f, 0.9f));
            }
        }

        // 易伤：每个角色单独累计（在里面 20 秒到 30%，出来后 10 秒内退完）。由 RunDirector 每帧调
        public static void TickHeroes()
        {
            foreach (var h in Combat.heroes)
            {
                if (!h) continue;
                vuln.TryGetValue(h, out float v);
                bool inside = Active && Inside(h.transform.position);
                v = inside ? Mathf.Min(MaxVuln, v + MaxVuln / RampSec * Time.deltaTime) : Mathf.Max(0f, v - MaxVuln / 10f * Time.deltaTime);
                vuln[h] = v;
                if (v > 0.001f) h.AddMod("taken", "miasma", v, 0.25f);
            }
        }

        public static float VulnOf(HeroBase h) => h && vuln.TryGetValue(h, out var v) ? v : 0f;

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}

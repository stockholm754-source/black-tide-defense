using UnityEngine;

namespace BlackTide
{
    // 远程弹道（青岚的枪弹、澄音的音波）：带拖尾的光点，按线段扫描命中，打到第一个敌人就结算
    public class Projectile : MonoBehaviour
    {
        HeroBase owner;
        Vector3 vel;
        float range, traveled, radius;
        public float mult, stagger, knock;
        public bool heavy;
        public string kind;
        public Vector3 dir;
        Transform head;
        Camera cam;

        public static Projectile Fire(HeroBase owner, Vector3 from, Vector3 dir, float speed, float range, float radius,
            float mult, float stagger, Color col, float size, bool heavy, string kind)
        {
            dir.y = 0f; dir.Normalize();
            var go = new GameObject("Shot");
            go.transform.position = from;
            var p = go.AddComponent<Projectile>();
            p.owner = owner; p.dir = dir; p.vel = dir * speed; p.range = range; p.radius = radius;
            p.mult = mult; p.stagger = stagger; p.knock = heavy ? 2f : 0.6f; p.heavy = heavy; p.kind = kind;
            p.cam = Camera.main;

            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.1f;
            tr.minVertexDistance = 0.05f;
            tr.widthMultiplier = size * 0.7f;
            tr.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            tr.material = FX.NewMat(SliceAssets.I.softDot, true, col, 3f);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.alignment = LineAlignment.View;

            var h = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(h.GetComponent<Collider>());
            h.transform.SetParent(go.transform, false);
            h.transform.localScale = Vector3.one * size;
            var r = h.GetComponent<Renderer>();
            r.sharedMaterial = FX.NewMat(SliceAssets.I.softDot, true, Color.Lerp(col, Color.white, 0.4f), 3.5f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            p.head = h.transform;
            return p;
        }

        static float DistToSeg(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }

        void Update()
        {
            if (!owner) { Destroy(gameObject); return; }
            Vector3 p0 = transform.position, step = vel * Time.deltaTime, p1 = p0 + step;
            Vector3 a = p0, b = p1; a.y = 0f; b.y = 0f;
            Damageable hit = null; float best = float.MaxValue;
            foreach (var e in Combat.enemies)
            {
                if (!e || e.dead) continue;
                Vector3 ep = e.transform.position; ep.y = 0f;
                if (DistToSeg(ep, a, b) > radius + e.radius) continue;
                float d = Vector3.Distance(ep, a);
                if (d < best) { best = d; hit = e; }
            }
            if (hit)
            {
                owner.ProjectileHit(hit, this);   // 在遍历之外结算，打死目标改动敌人列表也不会出错
                Die();
                return;
            }
            transform.position = p1;
            traveled += step.magnitude;
            if (traveled > range) { Die(); return; }
            if (head && cam) head.rotation = cam.transform.rotation;
        }

        void Die()
        {
            if (head) Destroy(head.gameObject);
            vel = Vector3.zero;
            enabled = false;
            Destroy(gameObject, 0.12f);   // 留一点时间让拖尾收尾
        }
    }
}

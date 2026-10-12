using UnityEngine;

namespace BlackTide
{
    // 霜潮女巫的冰面：持续 8 秒，站在里面移动速度 −40%（HeroBase 移动读 ModSum("speed")）
    public class FrostZone : MonoBehaviour
    {
        float radius, until, life;
        Material mat;

        public static void Spawn(Vector3 p, float r, float dur)
        {
            var go = new GameObject("FrostZone");
            go.transform.position = new Vector3(p.x, 0.05f, p.z);
            var z = go.AddComponent<FrostZone>();
            z.radius = r; z.until = Time.time + dur; z.life = dur;
            go.AddComponent<MeshFilter>().sharedMesh = Telegraph.Sector(0f, r, 360f, 64);
            var rd = go.AddComponent<MeshRenderer>();
            rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rd.receiveShadows = false;
            z.mat = new Material(SliceAssets.I.telegraphShader);
            z.mat.SetColor("_Color", new Color(0.7f, 0.9f, 1f, 0.35f));
            z.mat.SetFloat("_Progress", 1f);
            z.mat.SetFloat("_Sides", 0f);
            rd.sharedMaterial = z.mat;
            FX.Ring(p, 0.3f, r, 0.4f, new Color(0.7f, 0.9f, 1f, 1f), 0.2f);
            FX.FrozenGround(p, r, new Color(0.72f, 0.92f, 1f), dur);   // CL-003：V23 冰面贴花，让"这块地是冰"看得见摸得着
            FX.IceBurst(p + Vector3.up * 0.4f, new Color(0.75f, 0.95f, 1f));   // V17 冰片：成形那一下
        }

        void Update()
        {
            float left = until - Time.time;
            if (left <= 0f) { Destroy(gameObject); return; }
            mat.SetColor("_Color", new Color(0.7f, 0.9f, 1f, 0.35f * Mathf.Clamp01(left / 1.5f)));
            Vector3 c = transform.position; c.y = 0f;
            foreach (var h in Combat.heroes)
            {
                if (!h || h.Downed) continue;
                Vector3 hp = h.transform.position; hp.y = 0f;
                if (Vector3.Distance(hp, c) <= radius) h.AddMod("speed", "frostzone", -0.4f, 0.2f);
            }
            if (Random.value < 0.3f)
            {
                float a = Random.Range(0f, Mathf.PI * 2f), rr = Random.Range(0f, radius);
                FX.Sparks(c + new Vector3(Mathf.Cos(a) * rr, 0.2f, Mathf.Sin(a) * rr), Vector3.up, 1, new Color(0.8f, 0.95f, 1f));
            }
        }
    }
}

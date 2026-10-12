using UnityEngine;

namespace BlackTide
{
    // 黑潮孵化囊（56 号模型，第四批设施）：放在精英巢穴和黑潮裂隙里。节点开战后每 15 秒孵出 2 只残像（它孵的同时在场最多 6 只），
    // 拆掉才会停——逼队伍在「先清怪」和「先拆巢」之间做选择。血量约 8 只轻波，算一只轻波的击杀。
    public class BroodPod : MonoBehaviour
    {
        Damageable dmg;
        NodeRuntime node;
        float nextHatch;
        int alive, hatched;
        const int Cap = 6, Total = 10;   // 同时在场最多 6 只；一共孵 10 只后枯萎（兜底：没人拆也不会无限拖住节点）

        public static BroodPod Spawn(Vector3 p, NodeRuntime n, float hpE)
        {
            p.y = 0f;
            var go = new GameObject("黑潮孵化囊");
            go.transform.position = p;
            var prop = SliceAssets.I.MapProp("56_brood_pod");
            if (prop != null && prop.model)
                ArenaBuilder.Prop(go.transform, prop, "56_brood_pod", p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), 2.6f, false, ArenaBuilder.StoneTint, ArenaBuilder.Teal * 2.4f, 0.25f, out _);
            else
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(s.GetComponent<Collider>());
                s.transform.SetParent(go.transform, false);
                s.transform.localPosition = Vector3.up * 1f; s.transform.localScale = new Vector3(2f, 1.8f, 2f);
                s.GetComponent<Renderer>().sharedMaterial = ArenaBuilder.Lit(null, new Color(0.06f, 0.08f, 0.12f), 0.5f);
            }
            var b = go.AddComponent<BroodPod>();
            b.node = n;
            b.dmg = go.AddComponent<Damageable>();
            b.dmg.team = Team.Enemy;
            int team = Mathf.Max(1, Combat.heroes.Count);
            b.dmg.maxHp = b.dmg.hp = 560f * (team >= 3 ? 2.4f : team == 2 ? 1.7f : 1f) * 8f * hpE;
            b.dmg.radius = 1.1f; b.dmg.height = 2f;
            b.dmg.tier = 0;
            b.dmg.dormant = true;
            b.dmg.priority = true;
            b.dmg.onDeath = b.Die;
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = 1f; col.height = 2f; col.center = Vector3.up;
            b.dmg.RefreshRenderers();
            Combat.enemies.Add(b.dmg);
            return b;
        }

        void Update()
        {
            if (!dmg || dmg.dead || node == null) return;
            bool active = node.st == NodeRuntime.St.Active;
            dmg.dormant = !active;
            if (!active) { nextHatch = Time.time + 6f; return; }
            if (Time.time < nextHatch) return;
            nextHatch = Time.time + 15f;
            if (alive >= Cap || hatched >= Total) return;
            float hpE = Coop.HpMultFor(Coop.ErosionAt(transform.position));
            for (int i = 0; i < 2 && alive < Cap; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                var p = Combat.ClampToArena(transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.4f, 1f);
                SliceGame.I.SpawnMinion(p, hpE, 1f, 1f, null);
                var m = MinionController.Last;
                if (!m) continue;
                m.home = node.pos; m.leash = node.def.radius + 14f;
                alive++; hatched++;
                m.dmg.onDeath += () => alive--;
                node.enemies.Add(m.dmg);
            }
            FX.Tide(transform.position + Vector3.up, Vector3.up, 30);
            Feel.I.Sfx("summon", 0.35f, 0.05f, 1.4f);
        }

        void Die()
        {
            Combat.enemies.Remove(dmg);
            Coop.OnKill(dmg);
            FX.Tide(transform.position + Vector3.up, Vector3.up, 60);
            FX.Ring(transform.position, 0.3f, 3f, 0.5f, new Color(0.35f, 1f, 0.92f, 1f), 0.2f);
            Destroy(gameObject, 0.05f);
        }
    }
}

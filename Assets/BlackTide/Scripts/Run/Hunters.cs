using UnityEngine;

namespace BlackTide
{
    // DS-018b 黑潮追猎者：侵蚀驱动的追击骚扰（发育张力制造者）。
    // ≥30% 起刷（90 秒一波 ×2），55%（70 秒 ×4）、75%（55 秒 ×5 巨浪）升档；净化裂隙额外立即刷一波。
    // 数值：攻 ×1.2 / 血 ×0.8；给经验金币、不掉声骸武器；V34 召唤圈出场 + 首波 Toast。
    public class Hunters : MonoBehaviour
    {
        public static int Alive;
        float nextWave = 1e9f;
        bool warned;
        int riftWaves;

        public void Build()
        {
            nextWave = Time.time + 30f;   // 首波最早在 30 秒后
        }

        public static void OnRift()
        {
            if (!I) return;
            I.riftWaves++;
            if (Coop.erosion < 30f)
                Combat.Toast("净化惊动了黑潮——敌人正在苏醒", new Color(0.7f, 0.9f, 0.95f));
            else I.SpawnWave(true);
        }

        static Hunters I;

        void Awake() { I = this; }

        void Update()
        {
            float ero = Coop.erosion;
            if (ero < 30f || !Combat.player) return;
            if (!warned)
            {
                warned = true;
                Combat.Toast("黑潮敌人苏醒，追击而来", new Color(0.85f, 0.4f, 0.4f));
                nextWave = Time.time + 12f;
            }
            if (Time.time >= nextWave)
            {
                SpawnWave(false);
                int stage = ero >= 75f ? 2 : ero >= 55f ? 1 : 0;
                nextWave = Time.time + (stage == 2 ? 55f : stage == 1 ? 70f : 90f);
            }
        }

        void SpawnWave(bool rift)
        {
            var player = Combat.player;
            if (!player || !SliceGame.I) return;
            float ero = Coop.erosion;
            int stage = ero >= 75f ? 2 : ero >= 55f ? 1 : 0;
            int count = rift ? 3 : stage == 2 ? 5 : stage == 1 ? 4 : 2;
            float size = stage >= 2 ? 1.3f : 1f;   // 封顶巨浪
            Vector3 pc = Flat(player.transform.position);
            for (int i = 0; i < count; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                Vector3 p = Combat.ClampToArena(pc + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(11f, 17f), 2f);
                FX.SummonCircle(p, 0.6f, new Color(0.85f, 0.4f, 0.4f));   // DS-018c：V34 召唤圈预告
                Rogue.Delay(0.6f, () =>
                {
                    if (!SliceGame.I || !player || Hunters.Alive >= 10) return;   // 同时在场上限 10
                    SliceGame.spawningHunter = true;
                    try { if (SliceGame.I.SpawnMinionTripo(p, 0.8f, 1.2f, size, null)) Hunters.Alive++; }
                    finally { SliceGame.spawningHunter = false; }
                });
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}

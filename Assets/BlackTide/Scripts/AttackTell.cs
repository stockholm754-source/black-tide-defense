using UnityEngine;

namespace BlackTide
{
    // 出手提示（2026-10-09 打磨：敌人出手可读性）。
    // 原来只有地面预警圈，敌人本身在蓄力时看不出变化，玩家得盯着地面；混战里地面预警叠在一起更难读。现在三层信号：
    //   1. 蓄力：敌人轮廓光从冷青渐变成红橙（越接近出手越亮）——看敌人就知道谁要打了；
    //   2. 星芒：命中前 0.22 秒（正好是完美闪避的判定窗口）在敌人头部 / 武器位置闪一下十字星芒——「现在闪」；
    //   3. 声音：如果这一下的范围罩着玩家，星芒同时响金属预警（独立优先级、统一音量）——背对着也知道。
    // 所有敌人共用：读 Combat.threats（每个威胁带 owner），不需要改各兵种的出手逻辑。
    public class AttackTell : MonoBehaviour
    {
        public const float GlintT = 0.22f;
        static AttackTell inst;
        float lastPing = -9f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Ensure();
        static void Ensure()
        {
            if (inst) return;
            inst = new GameObject("AttackTell").AddComponent<AttackTell>();
            DontDestroyOnLoad(inst.gameObject);
        }

        void Update()
        {
            var player = Combat.player;
            foreach (var th in Combat.threats)
            {
                var o = th.owner;
                if (!o || o.dead) continue;
                float left = th.resolveAt - Time.time;
                if (left > GlintT) th.armed = true;
                // 轮廓：出手前 1 秒以外 0.3，逐渐升到 0.7；进入星芒窗口直接满
                float k = left <= GlintT ? 1f : Mathf.Lerp(0.7f, 0.3f, Mathf.Clamp01((left - GlintT) / 1f));
                if (o.tellFrame != Time.frameCount) { o.tell = 0f; o.tellFrame = Time.frameCount; }
                o.tell = Mathf.Max(o.tell, k);
                if (left <= GlintT && left > -0.05f && th.armed)
                {
                    th.armed = false; th.glintFor = th.resolveAt;
                    if (Time.time - o.lastGlintT < 0.1f) continue;
                    o.lastGlintT = Time.time;
                    bool onPlayer = player && !player.Downed && th.shape.Contains(player.transform.position, 1.2f);
                    Glint(o, onPlayer);
                }
            }
        }

        void Glint(Damageable o, bool onPlayer)
        {
            var cam = Camera.main;
            if (cam && Vector3.Distance(cam.transform.position, o.transform.position) > 45f) return;
            float h = Mathf.Min(o.height * 0.8f, 4.5f);
            Vector3 p = o.transform.position + Vector3.up * h + o.transform.forward * o.radius * 0.7f;
            float size = Mathf.Clamp(o.radius * 2.2f, 1.4f, 3.2f);
            Color c = new Color(1f, 0.55f, 0.25f);
            FX.Glint(p, size, c);
            if (onPlayer && Time.time - lastPing > 0.08f)
            {
                lastPing = Time.time;
                GameAudio.Sfx("attack_warning", o.bossCtl ? 0.65f : 0.5f, 0.03f, o.bossCtl ? 0.85f : 1f);
            }
        }

    }
}

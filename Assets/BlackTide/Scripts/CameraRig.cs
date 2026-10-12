using UnityEngine;

namespace BlackTide
{
    // 斜俯视镜头：跟随玩家，打 Boss 时略向 Boss 偏移，叠加创伤式震动（trauma²）
    // 开放地图用动态取景（2026-10-09 打磨）：跑图时稍远（看得到路和节点），交战时按附近醒着的敌人散开的范围推近，
    // 把这场战斗框在画面里——小怪贴脸时角色在屏幕上大约是原来的 1.5 倍，打 Boss 时自动留出大范围招式的余量。
    public class CameraRig : MonoBehaviour
    {
        public Transform target;
        public Transform focus2;
        public Vector3 aimPoint;
        public float distance = 21f, pitch = 52f, yaw = 0f, fov = 33f, smooth = 0.14f;
        // 动态取景（开放地图打开）
        public bool dynamicZoom;
        public float exploreDistance = 17f, combatMin = 12.5f, combatMax = 19f;
        float curDist = -1f, distVel;

        Vector3 pos, vel;
        bool init;

        void Awake()
        {
            var cam = GetComponent<Camera>();
            if (cam) cam.fieldOfView = fov;
        }

        public void Snap()
        {
            if (!target) return;
            pos = target.position;
            vel = Vector3.zero;
            init = true;
        }

        // 交战距离：以玩家为中心 16 米内醒着的敌人，取最远那只的距离（Boss 加上体型）；没有就是跑图距离
        float DesiredDistance()
        {
            if (!dynamicZoom) return distance;
            float spread = -1f;
            Vector3 c = target.position; c.y = 0f;
            foreach (var e in Combat.enemies)
            {
                if (!e || e.dead || e.dormant) continue;
                Vector3 p = e.transform.position; p.y = 0f;
                float d = Vector3.Distance(p, c);
                if (d > 16f) continue;
                float r = d + (e.bossCtl ? e.radius + 6f : e.radius);
                if (r > spread) spread = r;
            }
            if (spread < 0f) return exploreDistance;
            return Mathf.Clamp(spread * 1.15f + 6.5f, combatMin, combatMax);
        }

        void LateUpdate()
        {
            if (!target) return;
            Vector3 c = target.position;
            if (focus2 && focus2.gameObject.activeInHierarchy)
            {
                float d = Vector3.Distance(c, focus2.position);
                float k = Mathf.InverseLerp(26f, 4f, d) * 0.42f;
                c = Vector3.Lerp(c, focus2.position, k);
            }
            Vector3 ahead = aimPoint - target.position;
            ahead.y = 0f;
            c += Vector3.ClampMagnitude(ahead, 7f) * (dynamicZoom ? 0.16f : 0.1f);
            if (!init) { pos = c; init = true; }
            pos = Vector3.SmoothDamp(pos, c, ref vel, smooth, Mathf.Infinity, Feel.DT);

            // 推近快一点（进战斗要马上看清），拉远慢一点（打完别突然弹开）
            float want = DesiredDistance();
            if (curDist < 0f) curDist = want;
            curDist = Mathf.SmoothDamp(curDist, want, ref distVel, want < curDist ? 0.45f : 1.1f, Mathf.Infinity, Feel.DT);

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 basePos = pos - rot * Vector3.forward * curDist;

            float tr = Feel.I ? Feel.I.trauma : 0f;
            float s = tr * tr;
            float t = Feel.Clock * 30f;
            Vector3 off = new Vector3(Mathf.PerlinNoise(t, 0.13f) - 0.5f, Mathf.PerlinNoise(0.71f, t) - 0.5f, 0f) * 2f * s * 0.85f;
            float roll = (Mathf.PerlinNoise(t * 0.8f, 3.3f) - 0.5f) * 2f * s * 3f;
            transform.position = basePos + rot * off;
            transform.rotation = rot * Quaternion.Euler(0f, 0f, roll);
        }
    }
}

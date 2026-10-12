using System.Collections.Generic;
using UnityEngine;

namespace BlackTide
{
    // 战斗注册表与命中结算的公共部分
    public static class Combat
    {
        public static readonly List<Damageable> enemies = new List<Damageable>();
        // 正在蓄力的敌方攻击（给自动驾驶读，玩家看的是地面预警）
        public class Threat { public AttackShape shape; public float resolveAt; public Damageable owner; public float glintFor = -1f; public bool armed; }   // owner：出手提示（AttackTell）用
        public static readonly List<Threat> threats = new List<Threat>();
        public static readonly List<HeroBase> heroes = new List<HeroBase>();
        public static HeroBase player; // 当前操控的角色（Tab 切换）
        public static BossController boss;                 // 焦点 Boss：血条、镜头用（竞技场里就是唯一那个）
        public static readonly List<BossController> bosses = new List<BossController>();
        public static System.Func<Vector3, float, Vector3> clampOverride;   // 开放地图：边界 + 导航网格

        public static BossController NearestBoss(Vector3 from, float maxDist)
        {
            BossController best = null; float bd = maxDist;
            foreach (var b in bosses)
            {
                if (!b || b.dmg.dead) continue;
                Vector3 d = b.transform.position - from; d.y = 0f;
                float dist = d.magnitude - b.dmg.radius;
                if (dist < bd) { bd = dist; best = b; }
            }
            return best;
        }
        public static System.Action<Vector3, float, int> onNumber; // 位置, 数值, 类型(0普通 1暴击 2失衡 3受伤 4大招)
        public static System.Action<Vector3, string, Color, bool> onTinted; // AI 队友的伤害、治疗等小号数字：位置, 文本, 颜色, 是否醒目
        public static System.Action<string, Color> onToast;
        public static float arenaRadius = 18.5f;

        public static void Toast(string s, Color c) => onToast?.Invoke(s, c);

        public static Vector3 ClampToArena(Vector3 p, float r)
        {
            if (clampOverride != null) return clampOverride(p, r);
            Vector3 f = p; f.y = 0f;
            float max = arenaRadius - r;
            if (f.magnitude > max) f = f.normalized * max;
            return new Vector3(f.x, p.y, f.z);
        }

        // 收集扇形范围内的敌人（考虑目标半径）
        public static void Gather(Vector3 origin, Vector3 fwd, float range, float arc, List<Damageable> outList)
        {
            outList.Clear();
            var shape = arc >= 359f ? AttackShape.Circle(origin, range) : AttackShape.Cone(origin, fwd, range, arc);
            foreach (var e in enemies)
            {
                if (!e || e.dead) continue;
                if (shape.Contains(e.transform.position, e.radius)) outList.Add(e);
            }
        }

        // 最近的、没倒地的角色；crowd > 0 时，已经被很多敌人盯上的角色会被降权（原稿：动态仇恨，避免主C被围）
        public static HeroBase NearestHero(Vector3 from, float crowd = 0f, Object self = null)
        {
            HeroBase best = null; float bestScore = float.MaxValue;
            foreach (var h in heroes)
            {
                if (!h || h.dmg.dead) continue;
                Vector3 d = h.transform.position - from; d.y = 0f;
                float score = d.magnitude + crowd * h.chasers;
                if (score < bestScore) { bestScore = score; best = h; }
            }
            return best;
        }

        public static Damageable NearestEnemy(Vector3 from, Vector3 dir, float maxDist, float maxAngle)
        {
            Damageable best = null; float bestScore = float.MaxValue;
            foreach (var e in enemies)
            {
                if (!e || e.dead) continue;
                Vector3 d = e.transform.position - from; d.y = 0f;
                float dist = d.magnitude - e.radius;
                if (dist > maxDist) continue;
                float ang = Vector3.Angle(dir, d);
                if (ang > maxAngle && dist > 1.2f) continue;
                float score = dist + ang * 0.04f;
                if (score < bestScore) { bestScore = score; best = e; }
            }
            return best;
        }
    }
}

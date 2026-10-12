using UnityEngine;

namespace BlackTide
{
    public enum Team { Player, Enemy }

    public struct HitInfo
    {
        public float damage, stagger, knockback;
        public Vector3 point, dir;
        public bool heavy, crit;
        public string kind;
        public GameObject attacker;
    }

    // 生命、架势（失衡）、受击闪白
    public class Damageable : MonoBehaviour
    {
        public Team team;
        public float maxHp = 1000f, hp = 1000f;
        public float staggerMax, stagger;
        public float brokenUntil;
        public float breakDuration = 5f;
        public float brokenDamageMult = 1.5f;
        public float damageTakenMult = 1f;
        public float guardUntil, guardMul = 1f;   // 敌人的临时减伤（盾卫光环、重甲怒吼）
        public float vulnUntil, vulnMul = 1f;     // 临时易伤（刺鳐声骸的标记）
        public float shield;              // 护盾：先扣护盾再扣血
        public BossController bossCtl;    // 是 Boss 时指向它的控制器
        public bool dormant;              // 开放地图：节点里待命、还没被惊动的敌人（AI 不会主动打它）
        public bool priority;             // AI 优先打（孵化囊、唤潮祭司、施法中的咏唱者）
        public int tier;                  // 0 轻波 / 1 巨浪 / 2 怒涛 / 3 海啸（经验、Buff 计数用）
        public bool tide;                 // 割草方向重做：黑潮涌浪的兵（经验/贝币打折，不掉声骸武器；Buff 照常计）
        public float expValue;            // > 0 时覆盖按等级给的经验（信使 ×2）
        public float staggerTakenMult = 1f;
        public bool dead;
        public float lastShieldAbsorbed;  // 最近一次被护盾吸收的量（跳字用）
        public bool lastHitAccepted;      // 最近一次 TakeHit 是否被接受（被格挡 / 无敌拒绝时为 false；被护盾全额吸收仍为 true）
        public GameObject lastAttacker;   // 最后一击的来源（肉鸽的击杀触发要知道是谁杀的）
        public float radius = 0.6f, height = 2f;

        public System.Func<HitInfo, bool> canBeHit;
        public System.Func<HitInfo, float> damageFilter;   // 按来向改伤害（盾卫的正面格挡）
        public System.Action<HitInfo, float> onHit;
        public System.Action onDeath;
        public System.Action onBreak;

        Renderer[] rends;
        Color[] baseEmission;
        MaterialPropertyBlock mpb;
        float flash;
        Color flashColor = Color.white;
        bool flashing;
        float lastStaggerTime;

        public bool Broken => Time.time < brokenUntil;
        public Vector3 Center => transform.position + Vector3.up * height * 0.5f;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            RefreshRenderers();
        }

        public void RefreshRenderers()
        {
            rends = GetComponentsInChildren<Renderer>(true);
            baseEmission = new Color[rends.Length];
            for (int i = 0; i < rends.Length; i++)
            {
                var m = rends[i].sharedMaterial;
                baseEmission[i] = m && m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
            }
        }

        public void SetBaseEmission(Renderer r, Color c)
        {
            for (int i = 0; i < rends.Length; i++) if (rends[i] == r) baseEmission[i] = c;
            flashing = true;
        }

        public float TakeHit(HitInfo h)
        {
            lastHitAccepted = false;
            if (dead) return 0f;
            if (canBeHit != null && !canBeHit(h)) return 0f;
            lastHitAccepted = true;
            if (h.attacker) lastAttacker = h.attacker;
            float dmg = h.damage * damageTakenMult * (Broken ? brokenDamageMult : 1f) * (damageFilter != null ? damageFilter(h) : 1f) * (Time.time < guardUntil ? guardMul : 1f) * (Time.time < vulnUntil ? vulnMul : 1f);
            lastShieldAbsorbed = 0f;
            if (shield > 0f) { float a = Mathf.Min(shield, dmg); shield -= a; dmg -= a; lastShieldAbsorbed = a; }
            hp -= dmg;
            Flash(Broken ? new Color(1f, 0.6f, 0.3f) : new Color(1f, 0.95f, 0.9f), h.heavy ? 1f : 0.7f);
            if (staggerMax > 0f && !Broken)
            {
                stagger += h.stagger * staggerTakenMult;
                lastStaggerTime = Time.time;
                if (stagger >= staggerMax)
                {
                    stagger = 0f;
                    brokenUntil = Time.time + breakDuration;
                    onBreak?.Invoke();
                }
            }
            onHit?.Invoke(h, dmg);
            if (hp <= 0f)
            {
                hp = 0f;
                dead = true;
                onDeath?.Invoke();
            }
            return dmg;
        }

        // ---- 敌人轮廓光（2026-10-09 打磨）：第一次更新时给所有敌人叠一层冷青色菲涅耳轮廓（BT_Rim），受击时整圈闪亮。
        // 原来的受击闪白只调自发光颜色，而 Tripo 敌人的自发光只在贴图的青色裂纹上，身体本体根本不闪，挨打几乎看不出来
        bool rimDone;
        // 出手提示：AttackTell 每帧写 tell（0–1）和 tellFrame；超过一帧没写就自然退回 0
        [System.NonSerialized] public float tell, lastGlintT = -9f;
        [System.NonSerialized] public int tellFrame;
        float tellShown;
        static Material rimLight, rimElite;
        void AddRim()
        {
            rimDone = true;
            var sh = Shader.Find("BlackTide/Rim");
            if (!sh) return;
            if (!rimLight) { rimLight = new Material(sh); rimLight.SetColor("_Color", new Color(0.35f, 0.95f, 1f)); rimLight.SetFloat("_Intensity", 0.75f); }
            if (!rimElite) { rimElite = new Material(sh); rimElite.SetColor("_Color", new Color(0.45f, 1f, 0.95f)); rimElite.SetFloat("_Intensity", 1.1f); rimElite.SetFloat("_Power", 2.2f); }
            var mat = tier >= 1 || bossCtl ? rimElite : rimLight;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var ms = r.sharedMaterials;
                if (System.Array.IndexOf(ms, mat) >= 0) continue;
                var nm = new Material[ms.Length + 1];
                ms.CopyTo(nm, 0); nm[ms.Length] = mat;
                r.sharedMaterials = nm;
            }
            RefreshRenderers();
        }

        public void Flash(Color c, float amount)
        {
            flashColor = c;
            flash = Mathf.Max(flash, amount);
            flashing = true;
        }

        void Update()
        {
            if (staggerMax > 0f && stagger > 0f && Time.time - lastStaggerTime > 4f)
                stagger = Mathf.Max(0f, stagger - staggerMax * 0.05f * Time.deltaTime);
            if (!rimDone && team == Team.Enemy) AddRim();
            float tellTarget = Time.frameCount - tellFrame <= 1 ? tell : 0f;
            bool telling = tellShown > 0.001f || tellTarget > 0f;
            tellShown = Mathf.MoveTowards(tellShown, tellTarget, Time.deltaTime * 6f);
            if (!flashing && !telling) return;
            // 用游戏时间衰减：顿帧时闪白会定格，这是打击感的关键
            flash = Mathf.Max(0f, flash - Time.deltaTime * 9f);
            for (int i = 0; i < rends.Length; i++)
            {
                if (!rends[i]) continue;
                rends[i].GetPropertyBlock(mpb);
                mpb.SetColor("_EmissionColor", baseEmission[i] + flashColor * flash * flash * 0.45f);
                mpb.SetFloat("_Flash", flash * flash);   // 轮廓光材质读这个
                mpb.SetFloat("_Tell", tellShown);
                rends[i].SetPropertyBlock(mpb);
            }
            if (flash <= 0f) flashing = false;
        }
    }
}

using UnityEngine;

namespace BlackTide
{
    // 武器跟随：身上带一个隐形的原版 KayKit 骑士，和角色同步播放同一个动作。
    // 每帧照抄它手里剑的朝向（相对身体），剑柄对准角色自己的右手；
    // 原版双手握剑的时候，用 IK 把角色左手贴到剑柄上。
    public class WeaponFollow : MonoBehaviour
    {
        public Transform heroRoot, heroHand, refRoot, refHand, refLeft, refSword, sword;
        public float scale = 1f;
        public Vector3 leftGrip;          // 左手目标（世界坐标）
        public Quaternion leftRot = Quaternion.identity;
        public float leftWeight;
        HeroBase owner;

        // 由 OnAnimatorIK 用当前参考姿态求目标，避免读取上一帧 LateUpdate 的握点。
        public void PrepareGrip(Vector3 rightHand)
        {
            if (!refSword || !heroHand || !refLeft) return;
            if (!owner) owner = heroRoot.GetComponentInParent<HeroBase>();
            // 切到其他武器后，不能继续把左手拉向已经隐藏的原生长剑。
            if (owner && owner.UseGenericBasic) { leftWeight = 0f; return; }
            Quaternion rot = heroRoot.rotation * (Quaternion.Inverse(refRoot.rotation) * refSword.rotation);
            float d = Vector3.Distance(refLeft.position, refHand.position) / Mathf.Max(0.01f, refRoot.lossyScale.y);
            float want = d < 0.55f ? 1f : 0f;
            leftWeight = Mathf.MoveTowards(leftWeight, want, Time.deltaTime * 10f);
            Vector3 offset = Quaternion.Inverse(refSword.rotation) * (refLeft.position - refHand.position);
            leftGrip = rightHand + rot * offset * scale;
            leftRot = rot * (Quaternion.Inverse(refSword.rotation) * refLeft.rotation);
        }

        void LateUpdate()
        {
            if (!refSword || !sword || !heroHand) return;
            // 剑的朝向：相对身体保持和原版一致
            Quaternion rel = Quaternion.Inverse(refRoot.rotation) * refSword.rotation;
            Quaternion rot = heroRoot.rotation * rel;
            // 剑柄位置：原版"手腕→剑"的偏移，换算到角色体型后接在角色右手上
            Vector3 off = Quaternion.Inverse(refSword.rotation) * (refSword.position - refHand.position);
            sword.SetPositionAndRotation(heroHand.position + rot * (off * scale), rot);

        }
    }

    // 挂在带 Animator 的模型上，执行左手 IK
    public class LeftHandIK : MonoBehaviour
    {
        public WeaponFollow follow;
        Animator an;
        void Awake() { an = GetComponent<Animator>(); }
        void OnAnimatorIK(int layer)
        {
            if (!follow || !an) return;
            follow.PrepareGrip(an.GetIKPosition(AvatarIKGoal.RightHand));
            float w = follow.leftWeight;
            an.SetIKPositionWeight(AvatarIKGoal.LeftHand, w);
            an.SetIKPosition(AvatarIKGoal.LeftHand, follow.leftGrip);
            an.SetIKRotationWeight(AvatarIKGoal.LeftHand, w * 0.6f);
            an.SetIKRotation(AvatarIKGoal.LeftHand, follow.leftRot);
        }
    }
}

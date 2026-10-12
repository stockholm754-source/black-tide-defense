using UnityEngine;

namespace BlackTide
{
    // 只在走跑的接触阶段做有界修正，不给闪避/技能/挥砍强行粘脚。
    public class FootPlantIK : MonoBehaviour
    {
        public HeroBase hero;
        Animator animator;
        sealed class Plant { public bool locked; public Vector3 target; public float weight; }
        readonly Plant left = new Plant(), right = new Plant();
        void Awake() { animator = GetComponent<Animator>(); }
        void OnAnimatorIK(int layer)
        {
            if (!hero || !animator || !animator.isHuman) return;
            bool active = hero.state == HeroBase.State.Move && hero.anim.IsLocomotion && hero.HorizontalVelocity.magnitude > 0.2f;
            Solve(AvatarIKGoal.LeftFoot, left, active);
            Solve(AvatarIKGoal.RightFoot, right, active);
        }
        void Solve(AvatarIKGoal goal, Plant plant, bool active)
        {
            Vector3 pose = animator.GetIKPosition(goal);
            float height = pose.y - hero.transform.position.y;
            bool contact = active && height <= hero.anim.FootHeight + 0.075f;
            if (contact && !plant.locked)
            {
                plant.target = pose;
                plant.locked = true;
            }
            // 修正最大 14cm，超过后及时释放，避免快速移动把膝盖拉直或脚钉死。
            if (!contact || Vector3.Distance(pose, plant.target) > 0.14f) plant.locked = false;
            plant.weight = Mathf.MoveTowards(plant.weight, plant.locked ? 0.65f : 0f, Time.deltaTime * 12f);
            animator.SetIKPositionWeight(goal, plant.weight);
            animator.SetIKPosition(goal, pose + Vector3.ClampMagnitude(plant.target - pose, 0.14f));
        }
    }
}

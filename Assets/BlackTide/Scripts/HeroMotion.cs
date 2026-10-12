using UnityEngine;

namespace BlackTide
{
    // 共享动作移动层：脚本仍决定碰撞/战斗，动画根据实际运动匹配步态。
    public abstract partial class HeroBase
    {
        Vector3 moveVelocity, moveDamping, frameStart;
        bool motionFrame, motionSubmitted;
        [HideInInspector] public bool walkHeld;
        public Vector3 HorizontalVelocity { get; private set; }
        protected virtual bool AuthoredAttackMotion => false;
        const float DodgeDuration = 0.34f, DodgeDistance = 4.8f, DodgeRecovery = 0.08f;

        void BeginMotionFrame()
        {
            motionFrame = true;
            motionSubmitted = false;
            frameStart = transform.position;
        }

        void EndMotionFrame()
        {
            if (!motionSubmitted) Gravity(Vector3.zero);
            motionFrame = false;
            HorizontalVelocity = Flat(transform.position - frameStart) / Mathf.Max(0.0001f, Time.deltaTime);
            if (state == State.Move) anim.Locomotion(IdleAnim, RunAnim, HorizontalVelocity.magnitude);
        }

        protected void MoveLocomotion()
        {
            Vector3 input = Vector3.ClampMagnitude(moveIn, 1f);
            float pace = walkHeld ? 0.42f : 1f;
            Vector3 wanted = input * moveSpeed * pace * Mathf.Max(0f, 1f + ModSum("speed"));
            moveVelocity = Vector3.SmoothDamp(moveVelocity, wanted, ref moveDamping, input.sqrMagnitude > 0.01f ? 0.065f : 0.045f);
            if (input.sqrMagnitude > 0.01f) Face(input, 540f);
            Gravity(moveVelocity * Time.deltaTime);
        }

        // 有蹬地加速、随后减速，保留总距离与战斗时长；起始响应不等待整段前摇。
        static float DodgeProgress(float k) => k * (1.2f + k * (0.6f - 0.8f * k));
        protected float ActionBlend(float hitTime) => Mathf.Clamp(hitTime * 0.3f, 0.065f, 0.11f);
    }
}

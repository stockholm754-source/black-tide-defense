using UnityEngine;

namespace BlackTide
{
    // 玩家每次起手/追近动态选附近残血目标；AI 仍由 AllyBrain 决定站位。
    public abstract partial class HeroBase
    {
        Damageable lockedTarget;
        bool approaching;
        float approachUntil, stuckSince;
        Vector3 approachReference;
        readonly RaycastHit[] lockHits=new RaycastHit[24];
        public Damageable LockedTarget => !UsesBrain ? lockedTarget : null;
        public bool IsApproaching => approaching;
        bool LockedCombat => IsControlled && !UsesBrain && SoftLock;
        float TargetGap(Damageable e) => Flat(e.transform.position-transform.position).magnitude-e.radius;
        bool ValidTarget(Damageable e, float range) => e && e.gameObject.activeInHierarchy && !e.dead && !e.dormant && e.team==Team.Enemy && TargetGap(e)<=range;
        bool ClearAttackPath(Damageable e)
        {
            Vector3 d=Flat(e.transform.position-transform.position); float length=d.magnitude;
            if (length<0.1f) return true;
            int count=Physics.SphereCastNonAlloc(transform.position+Vector3.up*0.9f,0.22f,d/length,lockHits,Mathf.Max(0f,length-e.radius-0.3f),Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            for (int i=0;i<count;i++)
            {
                var c=lockHits[i].collider;
                if (!c || c.transform.IsChildOf(transform) || c.GetComponentInParent<Damageable>()) continue;
                return false;
            }
            return true;
        }
        void SetLockedTarget(Damageable e)
        {
            if (lockedTarget==e) return;
            lockedTarget=e;
            if (IsControlled) FX.Mark(null);
        }
        public void RefreshCombatTarget(bool beginSwing=false)
        {
            if (!LockedCombat) { SetLockedTarget(null); approaching=false; return; }
            float range=AttackIsRanged ? 14f : 10f;
            // 单次出刀方向稳定，起手仍跟踪同一敌人的移动；死亡后下一刀重新选。
            if (state!=State.Attack || beginSwing)
            {
                float nearest=float.MaxValue;
                foreach (var e in Combat.enemies)
                {
                    if (ValidTarget(e,range) && ClearAttackPath(e)) nearest=Mathf.Min(nearest,TargetGap(e));
                }
                Damageable best=null; float hp=float.MaxValue, gap=float.MaxValue;
                foreach (var e in Combat.enemies)
                {
                    if (!ValidTarget(e,range) || TargetGap(e)>nearest+1.5f || !ClearAttackPath(e)) continue;
                    float g=TargetGap(e);
                    if (e.hp<hp || (e.hp==hp && g<gap)) { best=e; hp=e.hp; gap=g; }
                }
                SetLockedTarget(best);
            }
            if (ValidTarget(lockedTarget,range)) AimPoint=Flat(lockedTarget.Center);
            else AimPoint=Flat(transform.position)+(moveIn.sqrMagnitude>0.1f ? moveIn.normalized : transform.forward)*8f;
        }
        bool AttackIsRanged => UseGenericBasic ? Weapons.Ranged(kit.Active.def.type) : Ranged;
        protected Damageable AttackTarget(float range, float angle)
        {
            if (LockedCombat)
            {
                RefreshCombatTarget(true);
                return ValidTarget(lockedTarget,range) ? lockedTarget : null;
            }
            return Combat.NearestEnemy(transform.position,AimDir(),range,angle);
        }
        // 起手追踪目标，出刀后保留动作轨迹；实际突进只走一次，不能穿过目标。
        protected Vector3 TrackedAttackDirection(Vector3 fallback)
        {
            if (!LockedCombat) return fallback;
            RefreshCombatTarget();
            Vector3 d=ValidTarget(lockedTarget,AttackIsRanged ? 14f : 10f) ? Flat(lockedTarget.transform.position-transform.position) : fallback;
            return d.sqrMagnitude>0.001f ? d.normalized : fallback;
        }
        protected Vector3 AttackDisplacement(Vector3 direction, float distance)
        {
            if (LockedCombat && ValidTarget(lockedTarget,18f)) distance=Mathf.Min(distance,Mathf.Max(0f,TargetGap(lockedTarget)-1.1f));
            return direction*Mathf.Max(0f,distance);
        }
        bool BeginAttackApproach()
        {
            if (!LockedCombat || AttackIsRanged) return false;
            RefreshCombatTarget();
            if (!lockedTarget || TargetGap(lockedTarget)<=2.1f) return false;
            if (state!=State.Move) Enter(State.Move);
            approaching=true; approachUntil=Time.time+2.4f; stuckSince=Time.time; approachReference=transform.position;
            return true;
        }
        bool TickAttackApproach()
        {
            if (!approaching) return false;
            if (!LockedCombat || GameInput.CombatBlocked || Time.time>approachUntil || (!attackHeld && moveIn.sqrMagnitude>0.1f)) { approaching=false; return false; }
            RefreshCombatTarget();
            if (!lockedTarget) { approaching=false; return false; }
            if (TargetGap(lockedTarget)<=2.1f && ClearAttackPath(lockedTarget))
            {
                approaching=false;
                bool counter=Time.time<counterUntil;
                if (counter) { counterUntil=0f; rogue.counterHitUntil=Time.time+0.9f; }
                if (UseGenericBasic) GenericStartBasic(counter); else StartBasic(counter);
                return true;
            }
            if (Flat(transform.position-approachReference).magnitude>0.3f) { stuckSince=Time.time; approachReference=transform.position; }
            if (Time.time-stuckSince>0.65f) { approaching=false; return false; }
            Vector3 playerMove=moveIn;
            moveIn=Nav.Dir(this,transform.position,lockedTarget.transform.position);
            MoveLocomotion();
            moveIn=playerMove;
            return true;
        }
        void CancelAttackApproach() { approaching=false; }
    }
}

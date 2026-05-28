using UnityEngine;

namespace LAS
{
    /// <summary>
    /// Manages the NPC's continuous look-at target independently of the FSM action queue.
    /// Any system (states, external code) calls SetTarget() once; this controller feeds
    /// the target to IKLookAt every frame so moving targets are tracked automatically.
    ///
    /// Tick() must be called from NPCBehaviourController.Update() each frame.
    /// </summary>
    public class NPCLookAtController
    {
        private readonly IKLookAt _ikLookAt;
        private Transform _target;
        
        public bool isLookingAtTarget { get; private set; }
        public bool canStartAnim { get; private set; }

        public Transform Target => _target;

        public NPCLookAtController(IKLookAt ikLookAt)
        {
            _ikLookAt = ikLookAt;
        }

        /// <summary>Track a new target. Replaces any previous target immediately.</summary>
        public void SetTarget(Transform target)
        {
            _target = target;
            
            //_ikLookAt.LookAt(_target);
        }

        /// <summary>Stop tracking; head returns to neutral on next Tick.</summary>
        public void Clear()
        {
            _target = null;
        }

        /// <summary>
        /// Call from NPCBehaviourController.Update() every frame.
        /// Feeds the current target position to IKLookAt so it tracks moving objects.
        /// </summary>
        public void Tick()
        {
            /*if (isLookingAtTarget && _target)
            {
                Debug.Log(isLookingAtTarget);
                Clear();
            }*/

            if (!_target)
            {
                _ikLookAt.ClearTarget();
                return;
            }
            
            isLookingAtTarget = _ikLookAt.LookAtContinuous(_target);

            //canStartAnim = _ikLookAt.canStartAnim;
        }

        public void EnableLegIK(bool value)
        {
            _ikLookAt.EnableLegIK(value);
        }
    }
}

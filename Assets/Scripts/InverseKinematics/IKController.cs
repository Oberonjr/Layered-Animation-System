using System;
using UnityEngine;
using UnityEngine.Events;

namespace LAS
{
    public class IKController : MonoBehaviour
    {
        public IKLookAt lookAt;
        public IKGrab grab;

        public bool isLookingAtTarget { get; private set; }
        public bool canStartAnim { get; private set; }
        
        private Transform lookAtTarget;

        public bool hasGrabbed;

        private float _lastLookAtRetrigger = -999f;
        private const float LookAtRetriggerInterval = 0.2f;

        private void Awake()
        {
            grab.OnGrabbed.AddListener(OnGrabbed);
        }

        private void Update()
        {
            UpdateLookAt();
        }

        public void SetLookAtTarget(Transform target)
        {
            lookAtTarget = target;
            isLookingAtTarget = false;
            _lastLookAtRetrigger = Time.time;
            lookAt.LookAt(lookAtTarget);
        }

        public void ClearLookAtTarget()
        {
            lookAtTarget = null;
        }

        public void UpdateLookAt()
        {
            if (!lookAtTarget)
            {
                lookAt.ClearTarget();
                isLookingAtTarget = false;
                return;
            }

            // Latch true on first settle — don't reset it just because LookAt() re-triggered IKLookAt.
            if (lookAt.isLookingAtTarget)
                isLookingAtTarget = true;

            // Re-trigger at a fixed interval to maintain head tracking without accumulating coroutines.
            if (isLookingAtTarget && Time.time - _lastLookAtRetrigger >= LookAtRetriggerInterval)
            {
                lookAt.LookAt(lookAtTarget);
                _lastLookAtRetrigger = Time.time;
            }

            canStartAnim = lookAt.canStartAnim;
        }

        public void EnableLegIK(bool enable)
        {
            lookAt.EnableLegIK(enable);
        }

        public void SetGrabTarget(Transform target)
        {
            hasGrabbed = false;
            grab.SetTarget(target);
        }

        public void Grab()
        {
            grab.GrabObject();
        }

        public void PutDown()
        {
            grab.PutDownObject();
        }

        public void HandOver(IKController otherController)
        {
            grab.OfferObject();
            otherController.grab.GrabObject();
        }

        public void OnGrabbed()
        {
            ClearLookAtTarget();
            hasGrabbed = true;
        }
    }
}

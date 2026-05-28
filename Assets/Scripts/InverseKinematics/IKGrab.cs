using DG.Tweening;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Events;

namespace LAS
{
    public class IKGrab : MonoBehaviour
    {
        private Vector3 defaultTargetPosition;
        
        [SerializeField] private TwoBoneIKConstraint constraint;
        [SerializeField] private Transform IKTarget;
        [SerializeField] private Transform wrist;

        [SerializeField] private float blendSpeed = 0.5f;
        
        public Transform grabTarget;
        
        private bool isHoldingObject;

        public UnityEvent OnGrabbed;

        private Transform actualTarget;

        private void Start()
        {
            constraint.weight = 0;
            defaultTargetPosition = IKTarget.localPosition;
        }
        
        private void Update()
        {
            if (actualTarget && IKTarget)
            {
                IKTarget.position = actualTarget.position;
                IKTarget.rotation = actualTarget.rotation;
            }
        }

        public void SetTarget(Transform target)
        {
            grabTarget = target;
            actualTarget = target.GetChild(0);
        }
        
        public void GrabObject()
        {
            if (!IKTarget)
                return;
            
            DOVirtual.Float(0, 1, blendSpeed, value =>
            {
                constraint.weight = value;
            }).OnComplete(() =>
            {
                SnapObject();
            });
        }

        private void SnapObject()
        {
            if (!grabTarget)
                return;
            
            OnGrabbed.Invoke();
            
            grabTarget.parent = wrist;
            
            //grabTarget.localPosition = actualTarget.localPosition;
        }
    }
}

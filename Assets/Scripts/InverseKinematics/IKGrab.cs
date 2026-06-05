using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Events;

namespace LAS
{
    [System.Serializable]
    public struct HandIK
    {
        public TwoBoneIKConstraint constraint;
        public Transform handTarget;
        public Transform itemSlot;
        public Transform restingHoldAnchor;
    }
    
    
    public class IKGrab : MonoBehaviour
    {
        [SerializeField] private float blendSpeed = 0.5f;

        [Header("Hand data")]
        [SerializeField] private HandIK leftHand;
        [SerializeField] private HandIK rightHand;

        public HandIK currentHand { get; private set; }
        
        [Header("Testing")]
        
        public Transform grabTarget;

        private IKGrabbable grabbable;

        private GrabPose grabPose;

        private Vector3 ikPosition;
        private Quaternion ikRotation;
        
        public UnityEvent OnGrabbed;

        private bool isResting;

        private void Start()
        {
            leftHand.constraint.weight = 0;
            rightHand.constraint.weight = 0;

            currentHand = rightHand;
        }
        
        private void Update()
        {
            if (grabbable)
            {
                if (isResting)
                {
                    ikPosition = currentHand.restingHoldAnchor.position;
                    ikRotation = currentHand.restingHoldAnchor.rotation;
                }
                
                currentHand.handTarget.position = ikPosition;
                currentHand.handTarget.rotation = ikRotation;
            }
        }
        
        public void SetTarget(Transform target)
        {
            grabTarget = target;

            if (grabTarget.TryGetComponent(out IKGrabbable ikgrabbable))
            {
                grabbable = ikgrabbable;
                grabPose = grabbable.GetClosestGrabPose(leftHand.itemSlot, rightHand.itemSlot);

                if (grabPose.rightHanded)
                    currentHand = rightHand;
                else
                    currentHand = leftHand;
                
                ikPosition = grabPose.position;
                ikRotation = grabPose.rotation;
            }
        }
        
        public void GrabObject()
        {
            isResting = false;
            
            DOVirtual.Float(0, 1, blendSpeed, value =>
            {
                currentHand.constraint.weight = value;
            }).OnComplete(() =>
            { 
                SnapObject();

                DOVirtual.Vector3(ikRotation.eulerAngles, currentHand.restingHoldAnchor.rotation.eulerAngles, blendSpeed, value =>
                {
                    ikRotation.eulerAngles = value;
                });
                
                DOVirtual.Vector3(ikPosition, currentHand.restingHoldAnchor.position, blendSpeed, value =>
                {
                    ikPosition = value;
                }).OnComplete(() =>
                {
                    isResting = true;
                });
            });
        }

        public void PutDownObject()
        {
            DOVirtual.Float(1, 0, blendSpeed, value =>
            {
                currentHand.constraint.weight = value;
            }).OnComplete(() =>
            {
                grabTarget = null;
            });
        }

        public void OfferObject()
        {
            isResting = false;
            
            DOVirtual.Vector3(ikPosition, ikPosition + transform.forward * 0.25f, blendSpeed, value =>
            {
                ikPosition = value;
            }).OnComplete(() =>
            {
                grabTarget.parent = null;
                PutDownObject();
            });
        }

        private void SnapObject(Transform original = null)
        {
            if (!grabTarget)
                return;
            
            OnGrabbed.Invoke();

            grabbable.SetLocalPosition(currentHand.itemSlot);
            
            grabTarget.SetParent(currentHand.itemSlot, true);
        }

        public void AutoSetup(Transform ikRig, Transform leftItemSlot, Transform rightItemSlot)
        {
            leftHand = new HandIK();
            leftHand.constraint = ikRig.GetChild(0).GetComponent<TwoBoneIKConstraint>();
            leftHand.handTarget = ikRig.GetChild(0).GetChild(0);
            leftHand.itemSlot = leftItemSlot;
            leftHand.restingHoldAnchor = new GameObject().transform;
            leftHand.restingHoldAnchor.name = "LeftHandRestingPose";
            leftHand.restingHoldAnchor.parent = transform.GetChild(3);
            leftHand.restingHoldAnchor.localPosition = Vector3.zero;
            
            rightHand = new HandIK();
            rightHand.constraint = ikRig.GetChild(1).GetComponent<TwoBoneIKConstraint>();
            rightHand.handTarget = ikRig.GetChild(1).GetChild(0);
            rightHand.itemSlot = rightItemSlot;
            rightHand.restingHoldAnchor = new GameObject().transform;
            rightHand.restingHoldAnchor.name = "RightHandRestingPose";
            rightHand.restingHoldAnchor.parent = transform.GetChild(3);
            rightHand.restingHoldAnchor.localPosition = Vector3.zero;
        }

        public void SetRestingPosition(Vector3 position, Quaternion rotation, bool isRightHand)
        {
            if (isRightHand)
            {
                rightHand.restingHoldAnchor.position = position;
                rightHand.restingHoldAnchor.rotation = rotation;
            }
            
        }
    }
}

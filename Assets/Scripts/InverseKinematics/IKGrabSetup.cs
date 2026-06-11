using System;
using NUnit.Framework.Constraints;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace LAS
{
    public class IKGrabSetup : MonoBehaviour
    {
        //[SerializeField] private Transform wristToPose;

        [SerializeField] private IKGrab grabController;
        
        [SerializeField] private IKGrabbable objectToPose;

        public bool isRightHanded;

        [Space(10)]
        [ReadOnly] public bool isIKEnabled;

        private TwoBoneIKConstraint constraint;

        private void Awake()
        {
            if (transform.parent.TryGetComponent(out TwoBoneIKConstraint constraint))
            {
                this.constraint = constraint;
                isIKEnabled = constraint.weight == 1;
            }
        }

        public void ToggleIK()
        {
            constraint.weight = constraint.weight == 0 ? 1 : 0;
            
            isIKEnabled = constraint.weight == 1;
        }

        public void ShowRestingPoses()
        {
            if (grabController == null)
            {
                if (transform.root.TryGetComponent(out IKGrab grab))
                {
                    grabController = grab;
                }
                else
                {
                    Debug.LogError("No grab controller found!");
                    return;
                }
            }
            
            grabController.TogglePreviews();
        }

        public void OrientRotation()
        {
            if (isRightHanded)
                transform.localRotation *= Quaternion.Euler(90, 0, 90);
            else
                transform.localRotation *= Quaternion.Euler(90, 0, -90);
        }

        public void SetRestingPose()
        {
            if (!grabController)
            {
                if (transform.root.TryGetComponent(out IKGrab grab))
                {
                    grabController = grab;
                }
                else
                {
                    Debug.LogError("No grab controller found!");
                    return;
                }
            }

            grabController.SetRestingPosition(transform.position, transform.rotation, isRightHanded);
        }

        public void AttachObjectToHand()
        {
            if (!objectToPose)
            {
                Debug.LogError("Grabbable must be set to attach it to hand");
                return;
            }
            
            if (!grabController)
            {
                if (transform.root.TryGetComponent(out IKGrab grab))
                {
                    grabController = grab;
                }
                else
                {
                    Debug.LogError("No grab controller found!");
                    return;
                }
            }
            
            grabController.SetTarget(objectToPose.transform, isRightHanded ? GrabType.RightHand :  GrabType.LeftHand);
            grabController.GrabObject(true);
        }
        
        public void AddGrabPose()
        {
            if (!objectToPose)
            {
                Debug.LogError("Pose can't be set because grabbable object is null!");
                return;
            }
            
            // If wristToPose is null, default to transform
            objectToPose.AddGrabPose(transform.position, transform.rotation, isRightHanded);
            
        }

        public void TogglePosePreviews()
        {
            if (!objectToPose)
            {
                Debug.LogError("Pose previews cannot be shown because grabbable object is null!");
                return;
            }
            
            objectToPose.DrawPreviews();
        }

        public void ClearData()
        {
            if (!objectToPose)
            {
                Debug.LogError("Pose data can't be cleared because grabbable object is null!");
                return;
            }
            
            objectToPose.ClearData();
        }
    }
}

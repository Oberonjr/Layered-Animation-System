using Unity.Properties;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace LAS
{
    public class IKGrabSetup : MonoBehaviour
    {
        [SerializeField] private Transform wristToPose;

        [SerializeField] private IKGrab grabController;
        
        [SerializeField] private IKGrabbable grabbable;

        [SerializeField] private bool isRightHanded;


        public void ToggleIK()
        {
            if (transform.parent.TryGetComponent(out TwoBoneIKConstraint constraint))
            {
                if(constraint.weight == 1)
                    constraint.weight = 0;
                else
                    constraint.weight = 1;
            }
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

            if (wristToPose)
            {
                grabController.SetRestingPosition(wristToPose.position, wristToPose.rotation, isRightHanded);
                return;
            }
            
            // If wristToPose is null, default to transform
            grabController.SetRestingPosition(transform.position, transform.rotation, isRightHanded);
        }

        public void AttachObjectToHand()
        {
            if (!grabbable)
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
            
            grabController.SetTarget(grabbable.transform, isRightHanded ? GrabType.RightHand :  GrabType.LeftHand);
            grabController.GrabObject(true);
        }
        
        public void AddGrabPose()
        {
            if (!grabbable)
            {
                Debug.LogError("Pose can't be set because grabbable object is null!");
                return;
            }
            
            if (wristToPose)
            {
                grabbable.AddGrabPose(wristToPose.position, wristToPose.rotation, isRightHanded);
                return;
            }
            
            // If wristToPose is null, default to transform
            grabbable.AddGrabPose(transform.position, transform.rotation, isRightHanded);
            
        }

        public void TogglePosePreviews()
        {
            if (!grabbable)
            {
                Debug.LogError("Pose previews cannot be shown because grabbable object is null!");
                return;
            }
            
            grabbable.DrawPreviews();
        }

        public void ClearData()
        {
            if (!grabbable)
            {
                Debug.LogError("Pose data can't be cleared because grabbable object is null!");
                return;
            }
            
            grabbable.ClearData();
        }
    }
}

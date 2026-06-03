using UnityEngine;

namespace LAS
{
    public class IKGrabSetup : MonoBehaviour
    {
        [SerializeField] private Transform wristToPose;
        
        [SerializeField] private IKGrabbable grabbable;

        [SerializeField] private bool isRightHanded;
        
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

using UnityEngine;

namespace LAS
{
    public class IKGrabbable : MonoBehaviour
    {
        public GrabDataSO grabData;

        private bool drawPreviews;

        private int activePose;

        public void AddGrabPose(Vector3 position, Quaternion rotation, bool isRightHanded)
        {
            var worldToLocalMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one).inverse;
            position = worldToLocalMatrix.MultiplyPoint3x4(position);
            
            //rotation.eulerAngles += transform.rotation.eulerAngles;
            
            rotation = Quaternion.Inverse(transform.rotation) * rotation;
            
            GrabPose grabPose = new GrabPose(position, rotation, isRightHanded);
            grabData.grabTransforms.Add(grabPose);
        }

        public void ClearData()
        {
            grabData.grabTransforms.Clear();
        }

        public GrabPose GetPose(int index)
        {
            if (index >= grabData.grabTransforms.Count)
            {
                Debug.LogError("Grab pose index out of range");
                return new GrabPose();
            }

            GrabPose pose = grabData.grabTransforms[index];
            
            Matrix4x4 localToWorldMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            pose.position = localToWorldMatrix.MultiplyPoint3x4(pose.position);

            pose.rotation = Quaternion.Inverse(transform.rotation) * pose.rotation;
            
            Debug.Log("Pose hand: " + pose.rightHanded);
            
            return pose;
        }
        
        public void SetLocalPosition(Transform localSpace)
        {
            GrabPose pose = grabData.grabTransforms[activePose];
            Vector3 position = pose.position;

            transform.rotation = new Quaternion();
            
            Quaternion rotation = pose.rotation;
            rotation = localSpace.rotation * Quaternion.Inverse(rotation);
            transform.rotation = rotation;
            
            position = transform.TransformDirection(position);

            transform.position = localSpace.position - position;
        }

        public GrabPose GetClosestGrabPose(Transform leftWrist, Transform rightWrist)
        {
            float closestDistance = Mathf.Infinity;
            int closestPose = 0;

            Transform currentWrist = leftWrist;
            for (int i = 0; i < 2; i++)
            {
                if (i > 0)
                    currentWrist = rightWrist;
                
                float currentClosestDistance = Mathf.Infinity;
                int currentClosestPose = 0;
                
                for (int j = 0; j < grabData.grabTransforms.Count; j++)
                {
                    if (i <= 0)
                    {
                        if (grabData.grabTransforms[j].rightHanded)
                            continue;
                    }
                    else
                    {
                        if (!grabData.grabTransforms[j].rightHanded)
                            continue;
                    }
                    
                    Matrix4x4 localToWorldMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
                    Vector3 position = localToWorldMatrix.MultiplyPoint3x4(grabData.grabTransforms[j].position);
                    
                    Vector3 distance = position - currentWrist.position;

                    if (distance.magnitude <= closestDistance)
                    {
                        closestPose = j;
                        closestDistance = distance.magnitude;
                        
                        Debug.Log($"Closest distance: {closestDistance}, Pose: {closestPose}");
                    }
                }
            }

            Debug.Log("Closest pose is: " + closestPose);
            
            return GetPose(closestPose);
        }
        
        public void DrawPreviews()
        {
            drawPreviews = !drawPreviews;
        }

        private void OnDrawGizmos()
        {
            if (!drawPreviews)
                return;
            
            for (int i = 0; i < grabData.grabTransforms.Count; i++)
            {
                GrabPose pose = grabData.grabTransforms[i];   
                
                Matrix4x4 localToWorldMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
                pose.position = localToWorldMatrix.MultiplyPoint3x4(pose.position);
                
                Gizmos.DrawWireCube(pose.position, new Vector3(0.1f, 0.1f, 0.1f));
            }
        }

    }
}
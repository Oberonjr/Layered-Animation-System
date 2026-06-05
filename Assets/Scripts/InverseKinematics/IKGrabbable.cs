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
            
            rotation = Quaternion.Inverse(transform.rotation) * rotation;
            
            GrabPose grabPose = new GrabPose(position, rotation, isRightHanded);
            grabData.grabPoses.Add(grabPose);
        }

        public void ClearData()
        {
            grabData.grabPoses.Clear();
        }

        public GrabPose GetPose(int index)
        {
            if (index >= grabData.grabPoses.Count)
            {
                Debug.LogError("Grab pose index out of range");
                return new GrabPose();
            }

            GrabPose pose = grabData.grabPoses[index];
            
            Matrix4x4 localToWorldMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            pose.position = localToWorldMatrix.MultiplyPoint3x4(pose.position);

            pose.rotation = transform.rotation * pose.rotation;
            
            Debug.Log("Pose hand: " + pose.rightHanded);
            
            return pose;
        }
        
        public void SetLocalPosition(Transform localSpace)
        {
            GrabPose pose = grabData.grabPoses[activePose];
            Vector3 position = pose.position;

            transform.rotation = new Quaternion();
            
            Quaternion rotation = pose.rotation;

            /*
            if (pose.rightHanded)
            else
                rotation = localSpace.rotation * Quaternion.Inverse(rotation);
                */
                
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
                /*if (i > 0)
                    currentWrist = rightWrist;*/
                
                for (int j = 0; j < grabData.grabPoses.Count; j++)
                {
                    if (grabData.grabPoses[j].rightHanded)
                        continue;
                    
                    /*if (i <= 0)
                    {
                        if (grabData.grabPoses[j].rightHanded)
                            continue;
                    }
                    else
                    {

                    }*/
                    
                    Matrix4x4 localToWorldMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
                    Vector3 position = localToWorldMatrix.MultiplyPoint3x4(grabData.grabPoses[j].position);
                    
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

            activePose = closestPose;
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
            
            for (int i = 0; i < grabData.grabPoses.Count; i++)
            {
                GrabPose pose = grabData.grabPoses[i];   
                
                Matrix4x4 localToWorldMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
                pose.position = localToWorldMatrix.MultiplyPoint3x4(pose.position);
                
                //Gizmos.matrix = Matrix4x4.TRS(pose.position, pose.rotation, new Vector3(0.1f, 0.1f, 0.1f));

                Gizmos.color = Color.white;

                if (pose.rightHanded)
                    Gizmos.color = Color.deepPink;
                
                Gizmos.DrawWireCube(pose.position, new Vector3(0.1f, 0.1f, 0.1f));

                Gizmos.color = Color.blue;
                Gizmos.DrawRay(pose.position, transform.rotation * pose.rotation * (Vector3.forward * 0.1f));

                Gizmos.color = Color.green;
                Gizmos.DrawRay(pose.position, transform.rotation * pose.rotation * (Vector3.up * 0.1f));

                Gizmos.color = Color.red;
                Gizmos.DrawRay(pose.position, transform.rotation * pose.rotation * (Vector3.right * 0.1f));
            }
        }

    }
}
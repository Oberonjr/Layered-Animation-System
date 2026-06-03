using System.Collections.Generic;
using UnityEngine;

namespace LAS
{
    [System.Serializable]
    public struct GrabPose
    {
        public Vector3 position;
        public Quaternion rotation;

        public bool rightHanded;

        public GrabPose(Vector3 position, Quaternion rotation, bool rightHanded = false)
        {
            this.position = position;
            this.rotation = rotation;
            this.rightHanded = rightHanded;
        }
    }
    
    [CreateAssetMenu(fileName = "GrabDataSO", menuName = "ScriptableObjects/GrabDataSO")]
    public class GrabDataSO : ScriptableObject
    {
        public List<GrabPose> grabTransforms;
    }
}

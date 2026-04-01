using System;
using UnityEngine;

namespace LAS
{
    public class Foot : IEquatable<Foot>
    {
        public Transform transform;
        public Transform target;
        public Transform constraint;
        public Vector3 ikPosition;
        public Quaternion ikRotation;
    
        public Foot(Transform transform, Transform target, Transform constraint)
        {
            this.transform = transform;
            this.target = target;
            this.constraint = constraint;
            ikPosition = target.position;
            SetIKRotation(target.transform.rotation);
            //ikRotation = target.rotation;
        }

        public void SetIKRotation(Quaternion rotation)
        {
            ikRotation.eulerAngles = new Vector3(ikRotation.eulerAngles.x, rotation.eulerAngles.y, ikRotation.eulerAngles.z);
        }

        public bool Equals(Foot other)
        {
            if (other == null)
                return false;
        
            return transform == other.transform && target == other.target;
        }
    }
}

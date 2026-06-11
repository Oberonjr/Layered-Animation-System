using System;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace LAS
{
    public class IKTarget : MonoBehaviour
    {
        [HideInInspector] public TwoBoneIKConstraint constraint;

        [ReadOnly] public bool isIKEnabled;

        private void Awake()
        {
            isIKEnabled = constraint.weight == 1;
        }

        public void ToggleIK()
        {
            constraint.weight = constraint.weight == 0 ? 1 : 0;
            isIKEnabled = constraint.weight == 1;
        }
    }
}

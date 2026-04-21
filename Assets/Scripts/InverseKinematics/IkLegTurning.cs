using System;
using System.Collections;
using UnityEngine;

namespace LAS
{
    public class FootIKData
    {
        public FootIKData otherFoot;
        public Transform pivot;
        public bool isGrounded;

        public FootIKData(Transform pivot)
        {
            this.pivot = pivot;
            isGrounded = true;
            
        }
    }

    public class IkLegTurning : MonoBehaviour
    {
        [SerializeField] private float rotationSpeed;
        [SerializeField] private float maxStepSize;

        [SerializeField] private AnimationCurve legMovementCurve;
        [SerializeField] private AnimationCurve legVerticalMovementCurve;
        [SerializeField] private float maxFootHeight;


        [Header("Left foot")] [SerializeField] private Transform leftPivot;

        [Header("Right foot")] [SerializeField]
        private Transform rightPivot;

        [Header("Body")] [SerializeField] private Transform body;
        [SerializeField] private Transform bodyIKPivot;

        private FootIKData leftFoot;
        private FootIKData rightFoot;

        private FootIKData catchupFoot;

        private Vector3 targetRotation;
        private Vector3 targetAngle;


        private Vector3 startBodyRotation;
        
        private bool catchingUp;

        private float bodyRotationValue;

        private void Start()
        {
            leftFoot = new FootIKData(leftPivot);
            rightFoot = new FootIKData(rightPivot);

            leftFoot.otherFoot = rightFoot;
            rightFoot.otherFoot = leftFoot;
        }

        public void RotateTowards(Vector3 targetRotation)
        {
            if (leftFoot.isGrounded && rightFoot.isGrounded)
            {
                this.targetRotation = targetRotation - Quaternion.LookRotation(body.forward).eulerAngles;
                targetAngle = targetRotation;
                
                if(this.targetRotation.y > 180)
                    this.targetRotation -= new Vector3(0, 360, 0);

                Debug.Log("Starting leg movement towards " + this.targetRotation);
                Debug.Log(targetAngle);

                bodyRotationValue = 0;
                
                startBodyRotation = body.eulerAngles;
            
                if(this.targetRotation.y < 0)
                    StartCoroutine(MoveFootCo(leftFoot));
                else
                    StartCoroutine(MoveFootCo(rightFoot));
            }
        }

        private void MoveFoot(FootIKData foot)
        {
             if(foot.isGrounded)
                 foot.isGrounded = false;
             
             if ((foot.pivot.eulerAngles - targetRotation).magnitude < rotationSpeed * Time.deltaTime)
             {
                 catchupFoot = foot.otherFoot;
                 foot.isGrounded = true;
             }
        }

        private FootIKData EvaluateFootToMove(float yRotation)
        {
            if(catchupFoot != null)
                return catchupFoot;
            
            if (!rightFoot.isGrounded)
                return rightFoot;
            
            if(!leftFoot.isGrounded)
                return leftFoot;

            if (yRotation < 0)
                return leftFoot;
            
            return rightFoot;
        }

        private IEnumerator MoveFootCo(FootIKData foot)
        {
            float timeElapsed = 0;
            Vector3 startRotation = foot.pivot.eulerAngles;
            
            float footHeight = 0;

            foot.isGrounded = false;
            
            while (Mathf.Abs(foot.pivot.eulerAngles.y - targetAngle.y) > rotationSpeed * Time.deltaTime)
            {
                //Debug.Log(Mathf.Abs(foot.pivot.eulerAngles.y - targetAngle.y));
                
                Vector3 rotation;
                
                rotation = targetRotation * legMovementCurve.Evaluate(timeElapsed) ;
                
                foot.pivot.eulerAngles = startRotation + rotation;

                // +++++++ FIX TORSO ROTATION +++++++++++
                
                body.eulerAngles = startBodyRotation + targetRotation * legMovementCurve.Evaluate(bodyRotationValue);
                bodyIKPivot.eulerAngles = startBodyRotation + targetRotation * legMovementCurve.Evaluate(bodyRotationValue);
                
                footHeight = legVerticalMovementCurve.Evaluate(timeElapsed) * maxFootHeight;
                foot.pivot.localPosition = Vector3.up * footHeight;

                float delta = Time.deltaTime * rotationSpeed;
                timeElapsed += delta / 3 * 2;
                bodyRotationValue += delta / 3;
                
                yield return null;
            }

            foot.pivot.position = Vector3.zero;
            
            
            if (!catchingUp)
            {
                foot.isGrounded = true;
                catchingUp = true;
                StartCoroutine(MoveFootCo(foot.otherFoot));
            }
            
            catchingUp = false;
            foot.isGrounded = true;
        }
    }
}

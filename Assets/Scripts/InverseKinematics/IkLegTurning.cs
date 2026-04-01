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
        
        [Header("Left foot")] 
        [SerializeField] private Transform leftPivot;
        
        [Header("Right foot")]
        [SerializeField] private Transform rightPivot;

        [Header("Body")] 
        [SerializeField] private Transform body;
        
        private FootIKData leftFoot;
        private FootIKData rightFoot;

        private FootIKData catchupFoot;

        private Vector3 target;

        private bool catchingUp;

        private void Start()
        {
            leftFoot = new FootIKData(leftPivot);
            rightFoot = new FootIKData(rightPivot);

            leftFoot.otherFoot = rightFoot;
            rightFoot.otherFoot = leftFoot;
        }

        public void RotateTowards(Vector3 targetRotation)
        { 
            Debug.Log(targetRotation.y + " starting foot rotation");
            
            target = targetRotation;
            catchingUp = false;
            
            if(target.y < 0)
                StartCoroutine(MoveFootCo(leftFoot));
            else
                StartCoroutine(MoveFootCo(rightFoot));
        }

        private void MoveFoot(FootIKData foot)
        {
             if(foot.isGrounded)
                 foot.isGrounded = false;


             if ((foot.pivot.eulerAngles - target).magnitude < rotationSpeed * Time.deltaTime)
             {
                 
                 catchingUp = true;
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
            foot.pivot.position += Vector3.up * 0.05f;
            
            while (Mathf.Abs(foot.pivot.eulerAngles.y - target.y) > rotationSpeed * Time.deltaTime)
            {
                Debug.Log(foot.pivot.eulerAngles.y + " rotation of " + foot.pivot.name);
                
                Vector3 maxRotation = target.GetDirectionalNormalized() * (rotationSpeed * Time.deltaTime);
                Vector3 rotation;
            
                if (target.magnitude < maxRotation.magnitude)
                    rotation = target;
                else 
                    rotation = maxRotation;
                
                foot.pivot.eulerAngles += rotation / 3 * 2;
                body.eulerAngles += rotation / 3;
                
                yield return null;
            }

            foot.pivot.position -= Vector3.up * 0.05f;
            
            if (!catchingUp)
            {
                catchingUp = true;
                StartCoroutine(MoveFootCo(foot.otherFoot));
            }
        }
    }
}

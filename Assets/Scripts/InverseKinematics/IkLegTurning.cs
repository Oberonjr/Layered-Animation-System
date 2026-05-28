using System;
using System.Collections;
using DG.Tweening;
using Unity.Burst;
using UnityEngine;
using UnityEngine.Animations.Rigging;

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
        // How fast the legs rotate in degrees per second
        [SerializeField] private float rotationSpeed;
        
        // Maximum character step size in degrees
        [SerializeField][Range(0, 180)] private int maxStepSize;

        // This curve determines the timing/speed of the rotation
        [SerializeField] private AnimationCurve legMovementCurve;
        [SerializeField] private AnimationCurve legVerticalMovementCurve;
        [SerializeField] private float maxFootHeight;
        
        [Header("Animation weights")]
        [SerializeField][Range(0,1)] private float walkBlendThreshold;
        [SerializeField] private float weightTweenDuration;
        
        [Header("Left foot")] 
        [SerializeField] private Transform leftPivot;
        [SerializeField] private TwoBoneIKConstraint leftConstraint;

        [Header("Right foot")] 
        [SerializeField] private Transform rightPivot;
        [SerializeField] private TwoBoneIKConstraint rightConstraint;
        
        [Header("Body")] 
        [SerializeField] private Transform root;
        [SerializeField] private Transform body;
        [SerializeField] private Transform bodyIKPivot;

        [HideInInspector] public bool canStartAnim;
        [HideInInspector] public bool isRotating;
        
        private FootIKData leftFoot;
        private FootIKData rightFoot;
        private FootIKData catchupFoot;

        private Vector3 targetRotation;
        private Vector3 targetAngle;
        private Vector3 startBodyRotation;
        
        private bool isBlendingLegWeights;
        private bool catchingUp;

        private float bodyRotationValue;
        private float normalizedRotationSpeed;


        private void Start()
        {
            leftFoot = new FootIKData(leftPivot);
            rightFoot = new FootIKData(rightPivot);

            leftFoot.otherFoot = rightFoot;
            rightFoot.otherFoot = leftFoot;
            
            normalizedRotationSpeed = rotationSpeed / maxStepSize;
        }

        public void RotateTowards(Vector3 targetRotation)
        {
            if (leftFoot.isGrounded && rightFoot.isGrounded)
            {
                this.targetRotation = targetRotation - Quaternion.LookRotation(body.forward).eulerAngles;
                targetAngle = targetRotation;
                //this.targetRotation = targetRotation;
                
                if(this.targetRotation.y > 180)
                    this.targetRotation -= new Vector3(0, 360, 0);

                Debug.Log("Leg rotation: " + targetRotation);
                Debug.Log("Starting leg movement towards " + this.targetRotation);

                bodyRotationValue = 0;
                
                startBodyRotation = body.eulerAngles;

                catchingUp = false;
                
                if(this.targetRotation.y < 0)
                    StartCoroutine(MoveFootCo(leftFoot));
                else
                    StartCoroutine(MoveFootCo(rightFoot));
            }
        }

        public void EnableLegIk(bool value)
        {
            if (isBlendingLegWeights)
                return;
            
            if (!value)
            {
                if (leftConstraint.weight <= 0.1f)
                    return;
                
                isBlendingLegWeights = true;
                
                Debug.Log("Start enable tween");
                
                DOVirtual.Float(1, 0, weightTweenDuration, weight =>
                {
                    leftConstraint.weight = weight;
                    rightConstraint.weight = weight;
                    //Debug.Log("Disabling leg IK: " + leftConstraint.weight);
                     
                }).OnComplete(() =>
                {
                    Debug.Log("Disable tween complete");
                    isBlendingLegWeights = false;
                });

                return;
            }

            if (leftConstraint.weight >= 0.1)
                return;

            isBlendingLegWeights = true;
            
            Debug.Log("Start enable tween");
            
            DOVirtual.Float(0, 1, weightTweenDuration, weight =>
            {
                leftConstraint.weight = weight;
                rightConstraint.weight = weight;
                
                //Debug.Log("Enabling leg IK: " + leftConstraint.weight);
            }).OnComplete(() =>
            {
                Debug.Log("Enable tween complete");
                isBlendingLegWeights = false;
            });
        }

        private void ResetRotations()
        {
            root.rotation = body.rotation;
            body.localRotation = Quaternion.Euler(Vector3.zero);
            bodyIKPivot.localRotation = Quaternion.Euler(Vector3.zero);
            rightPivot.localRotation = Quaternion.Euler(Vector3.zero);
            leftPivot.localRotation =  Quaternion.Euler(Vector3.zero);
        }

        private IEnumerator MoveFootCo(FootIKData foot)
        {
            isRotating = true;
            float timeElapsed = 0;
            Vector3 startRotation = foot.pivot.eulerAngles;
            
            float footHeight = 0;

            foot.isGrounded = false;
            
            while (Mathf.Abs(foot.pivot.eulerAngles.y - targetAngle.y) > normalizedRotationSpeed * Time.deltaTime)
            {
                //Debug.Log(bodyRotationValue + " > " + walkBlendThreshold);
                
                if (bodyRotationValue >= walkBlendThreshold)
                    canStartAnim = true;
                else
                    canStartAnim = false;
                
                Vector3 rotation;
                rotation = targetRotation * legMovementCurve.Evaluate(timeElapsed) ;
                
                foot.pivot.eulerAngles = startRotation + rotation;

                Vector3 bodyTarget = new Vector3(0, targetRotation.y, 0);
                body.eulerAngles = startBodyRotation + bodyTarget * legMovementCurve.Evaluate(bodyRotationValue);
                bodyIKPivot.eulerAngles = startBodyRotation + bodyTarget * legMovementCurve.Evaluate(bodyRotationValue);
                
                footHeight = legVerticalMovementCurve.Evaluate(timeElapsed) * maxFootHeight;
                foot.pivot.localPosition = Vector3.up * footHeight;

                float delta = Time.deltaTime * normalizedRotationSpeed;
                
                timeElapsed += delta / 3 * 2;
                bodyRotationValue += delta / 3;
                
                yield return null;
            }

            foot.pivot.localPosition = Vector3.zero;
            
            if (catchingUp)
            {
                isRotating = false;
                canStartAnim = false;
                
                ResetRotations();
                //Debug.Log("Done moving legs");
            }
            
            if (!catchingUp)
            {
                foot.isGrounded = true;
                catchingUp = true;
                
                //Debug.Log("Start moving other foot");
                
                StartCoroutine(MoveFootCo(foot.otherFoot));
            }

            catchingUp = false;
            foot.isGrounded = true;
        }
    }
}

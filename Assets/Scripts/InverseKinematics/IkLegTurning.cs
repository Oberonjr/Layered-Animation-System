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
        [Header("Rotation parameters")]
        // How fast the legs rotate in degrees per second
        [Tooltip("Rotation speed in degrees / second")]
        [SerializeField] private float rotationSpeed = 540;
        
        // Maximum character step size in degrees
        [Tooltip("Maximum rotation (in degrees) the character can turn with one 'step'")]
        [SerializeField][Range(0, 180)] private int maxStepSize = 180;

        // This curve determines the timing/speed of the rotation
        [Tooltip("The curve which leg rotation (in degrees, not rotation speed) will be evaluated against \n" +
                 "Keyframe value is multiplied by the target rotation")]
        [SerializeField] private AnimationCurve legMovement;
        [Tooltip("The curve which 'maxFootHeight' will be evaluated against during the rotation \n" +
                 "Keyframe value is multiplied by maxFootHeight")]
        [SerializeField] private AnimationCurve legVerticalPosition;
        [Tooltip("This value will be multiplied with the value of 'legVerticalPositionCurve' to get the vertical position of a foot during rotation")]
        [SerializeField] private float maxFootHeight = 0.1f;
        
        //[Header("Animation weights")]
        /*[SerializeField][Range(0,1)]*/ private float walkBlendThreshold = 0.9f;
        /*[SerializeField]*/ private float weightTweenDuration = 0.2f;
        
        [Header("Left foot")] 
        [SerializeField] private Transform leftPivot;
        [SerializeField] private TwoBoneIKConstraint leftConstraint;

        [Header("Right foot")] 
        [SerializeField] private Transform rightPivot;
        [SerializeField] private TwoBoneIKConstraint rightConstraint;
        
        [Header("Body")] 
        [SerializeField] private Transform body;
        [SerializeField] private Transform bodyIKPivot;

        public bool canStartAnim { get; private set; }
        public bool isRotating { get; private set; }
        
        private FootIKData leftFoot;
        private FootIKData rightFoot;
        private FootIKData catchupFoot;

        private Vector3 targetAngle;
        private Vector3 startBodyRotation;
        
        private Vector3 remainingRotation;
        private int remainingDirection;
        
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
        }

        public void RotateTowards(Vector3 targetRotation, int direction = 1)
        {
            if (leftFoot.isGrounded && rightFoot.isGrounded)
            {
                normalizedRotationSpeed = rotationSpeed / 180;
                
                isRotating = false;
                canStartAnim = false;
                
                
                targetAngle = targetRotation - body.eulerAngles;
                
                remainingRotation = new Vector3(targetAngle.x, Mathf.Abs(targetAngle.y) * direction, targetAngle.z);
                
                Debug.Log("Before reduction: " + remainingRotation);
                
                
                targetAngle = new Vector3(targetAngle.x,
                    Mathf.Clamp(Mathf.Abs(targetAngle.y), 0, maxStepSize) * direction, targetAngle.z);

                remainingRotation -= targetAngle;
                remainingDirection = direction;
                
                Debug.Log("After: " + remainingRotation);
                
                Debug.Log("Starting leg movement towards " + targetAngle);

                bodyRotationValue = 0;
                
                startBodyRotation = body.eulerAngles;

                catchingUp = false;
                
                if(targetAngle.y < 0)
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
            transform.root.rotation = body.rotation;
            body.localRotation = new Quaternion();
            bodyIKPivot.localRotation = new Quaternion();
            rightPivot.localRotation = new Quaternion();
            leftPivot.localRotation =  new Quaternion();
        }

        private IEnumerator MoveFootCo(FootIKData foot)
        {
            isRotating = true;
            float timeElapsed = 0;
            Vector3 startRotation = foot.pivot.eulerAngles;
            
            float footHeight = 0;

            foot.isGrounded = false;
            
            while (Mathf.Abs(foot.pivot.eulerAngles.y - targetAngle.y) > normalizedRotationSpeed * Time.deltaTime && timeElapsed < 1)
            {
                //Debug.Log(bodyRotationValue + " > " + walkBlendThreshold);
                
                normalizedRotationSpeed = rotationSpeed / 180;

                if (bodyRotationValue >= walkBlendThreshold && !canStartAnim)
                {
                    canStartAnim = true;
                    //Debug.Log("Start blending animations");
                }
                else if (canStartAnim)
                {
                    canStartAnim = false;
                }
                
                Vector3 rotation;
                rotation = targetAngle * legMovement.Evaluate(timeElapsed) ;
                
                foot.pivot.eulerAngles = startRotation + rotation;

                Vector3 bodyTarget = new Vector3(0, targetAngle.y, 0);
                body.eulerAngles = startBodyRotation + bodyTarget * legMovement.Evaluate(bodyRotationValue);
                bodyIKPivot.eulerAngles = startBodyRotation + bodyTarget * legMovement.Evaluate(bodyRotationValue);
                
                footHeight = legVerticalPosition.Evaluate(timeElapsed) * maxFootHeight;
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
                
                catchingUp = false;
                foot.isGrounded = true;

                if (Mathf.Abs(remainingRotation.y) > 0)
                {
                    RotateTowards(remainingRotation + body.eulerAngles, remainingDirection);
                    yield break;
                }
                
                ResetRotations();
                //Debug.Log("Done moving legs");

                yield break;
            }
            
            if (!catchingUp)
            {
                foot.isGrounded = true;
                catchingUp = true;
                
                //Debug.Log("Start moving other foot");
                
                StartCoroutine(MoveFootCo(foot.otherFoot));
            }
        }

        public void AutoSetup(Transform ikRig)
        {
            body = transform.GetChild(0);
            bodyIKPivot = transform.GetChild(3);
            
            legMovement = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 1));
            legVerticalPosition = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.5f, 1),  new Keyframe(1, 0));

            leftPivot = transform.GetChild(1).transform;
            leftConstraint = ikRig.GetChild(2).GetComponent<TwoBoneIKConstraint>();
            
            rightPivot = transform.GetChild(2).transform;
            rightConstraint = ikRig.GetChild(3).GetComponent<TwoBoneIKConstraint>();
        }
    }
}

using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Events;

namespace LAS
{
    [System.Serializable]
    public struct HandIK
    {
        public TwoBoneIKConstraint constraint;
        public Transform handTarget;
        public Transform itemSlot;

        [HideInInspector] public int handIndex;
    }
    
    public class IKGrab : MonoBehaviour
    {
        [SerializeField] private float blendSpeed = 0.5f;

        [Header("Hand data")]
        [SerializeField] private HandIK leftHand;
        [SerializeField] private HandIK rightHand;
        
        [Space(10)]
        [SerializeField] GrabDataSO restingPoses;

        public HandIK currentHand { get; private set; }
        
        [Header("Testing")]
        
        public Transform grabTarget;

        public Transform bodyPivot;
        
        private IKGrabbable grabbable;

        private GrabPose grabPose;

        private Vector3 ikPosition;
        private Quaternion ikRotation;
        
        public UnityEvent OnGrabbed;

        private bool isResting;

        private bool showPreviews;

        private bool editingPoses;

        private void Start()
        {
            leftHand.handIndex = 0;
            rightHand.handIndex = 1;
            
            currentHand = rightHand;
        }
        
        private void Update()
        {
            if (grabbable && !editingPoses)
            {
                if (isResting)
                {
                    (Vector3 position, Quaternion rotation) = GetRestingValues(currentHand.handIndex);
                    ikPosition = position;
                    ikRotation = rotation;
                }
                
                currentHand.handTarget.position = ikPosition;
                currentHand.handTarget.rotation = ikRotation;
            }
        }
        
        public void SetTarget(Transform target, GrabType grabType = GrabType.Any)
        {
            grabTarget = target;

            if (grabTarget.TryGetComponent(out IKGrabbable ikgrabbable))
            {
                grabbable = ikgrabbable;
                grabPose = grabbable.GetClosestGrabPose(leftHand.itemSlot, rightHand.itemSlot, grabType);

                if (grabPose.rightHanded)
                    currentHand = rightHand;
                else
                    currentHand = leftHand;
                
                ikPosition = grabPose.position;
                ikRotation = grabPose.rotation;
            }
        }
        
        public void GrabObject(bool isDebugging = false)
        {
            editingPoses = false;
            isResting = false;
            
            DOVirtual.Float(0, 1, blendSpeed, value =>
            {
                currentHand.constraint.weight = value;
            }).OnComplete(() =>
            { 
                SnapObject();

                (Vector3 position, Quaternion rotation) = GetRestingValues(currentHand.handIndex);

                // Move hand to resting position
                DOVirtual.Vector3(ikRotation.eulerAngles, rotation.eulerAngles, blendSpeed, value =>
                {
                    ikRotation.eulerAngles = value;
                });
                
                DOVirtual.Vector3(ikPosition, position, blendSpeed, value =>
                {
                    ikPosition = value;
                }).OnComplete(() =>
                {
                    if (!isDebugging)
                        isResting = true;
                    else
                        editingPoses = true;
                });
            });
        }

        public void PutDownObject()
        {
            DOVirtual.Float(1, 0, blendSpeed, value =>
            {
                currentHand.constraint.weight = value;
            }).OnComplete(() =>
            {
                grabTarget = null;
            });
        }

        public void OfferObject()
        {
            isResting = false;
            
            DOVirtual.Vector3(ikPosition, ikPosition + transform.forward * 0.25f, blendSpeed, value =>
            {
                ikPosition = value;
            }).OnComplete(() =>
            {
                grabTarget.parent = null;
                PutDownObject();
            });
        }

        private void SnapObject()
        {
            if (!grabTarget)
                return;
            
            OnGrabbed.Invoke();

            grabbable.SetLocalPosition(currentHand.itemSlot);
            
            grabTarget.SetParent(currentHand.itemSlot, true);
        }

        /*void ApplyRestingValuesToHandStruct()
        {
            for (int i = 0; i < 2; i++)
            {
                Vector3 position = restingPoses.grabPoses[i].position;
                Quaternion rotation = restingPoses.grabPoses[i].rotation;
                Matrix4x4 localToWorldMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
                position = localToWorldMatrix.MultiplyPoint3x4(position);

                rotation = transform.rotation * rotation;
            
                if (i == 0)
                {
                    leftHand.restingPosition = position;
                    leftHand.restingRotation = rotation;
                }
                else
                {
                    rightHand.restingPosition = position;
                    rightHand.restingRotation = rotation;
                }
            }
        }*/

        (Vector3, Quaternion) GetRestingValues(int index)
        {
            Vector3 position = restingPoses.grabPoses[index].position;
            Quaternion rotation = restingPoses.grabPoses[index].rotation;
            Matrix4x4 localToWorldMatrix = Matrix4x4.TRS(transform.position, bodyPivot.rotation, Vector3.one);
            position = localToWorldMatrix.MultiplyPoint3x4(position);
            
            rotation = bodyPivot.rotation * rotation;

            return (position, rotation);
        }
        
        
        // ===============================================
        // EDITOR SCRIPTS
        // ===============================================
        
        public void AutoSetup(Transform ikRig, Transform leftItemSlot, Transform rightItemSlot)
        {
            leftHand = new HandIK();
            leftHand.constraint = ikRig.GetChild(0).GetComponent<TwoBoneIKConstraint>();
            leftHand.handTarget = ikRig.GetChild(0).GetChild(0);
            leftHand.itemSlot = leftItemSlot;
            leftHand.constraint.weight = 0;
            
            rightHand = new HandIK();
            rightHand.constraint = ikRig.GetChild(1).GetComponent<TwoBoneIKConstraint>();
            rightHand.handTarget = ikRig.GetChild(1).GetChild(0);
            rightHand.itemSlot = rightItemSlot;
            rightHand.constraint.weight = 0;
        }

        public void SetRestingPosition(Vector3 position, Quaternion rotation, bool isRightHand)
        {
            var worldToLocalMatrix = Matrix4x4.TRS(transform.position, bodyPivot.rotation, Vector3.one).inverse;
            position = worldToLocalMatrix.MultiplyPoint3x4(position);
            
            rotation = Quaternion.Inverse(bodyPivot.rotation) * rotation;
            
            if (!isRightHand)
            {
                restingPoses.grabPoses.RemoveAt(0);
                restingPoses.grabPoses.Insert(0, new GrabPose(position, rotation));
            }
            else
            {
                restingPoses.grabPoses.RemoveAt(1);
                restingPoses.grabPoses.Insert(1, new GrabPose(position, rotation));
            }
        }

        public void TogglePreviews()
        {
            showPreviews = !showPreviews;
        }

        private void OnDrawGizmos()
        {
            if (!showPreviews)
                return;
          
            (Vector3 leftPosition, Quaternion leftRotation) = GetRestingValues(0);
            
            Gizmos.color = Color.white;

            Gizmos.DrawWireCube(leftPosition, new Vector3(0.1f, 0.1f, 0.1f));

            Gizmos.color = Color.blue;
            Gizmos.DrawRay(leftPosition, leftRotation * (Vector3.forward * 0.1f));

            Gizmos.color = Color.green;
            Gizmos.DrawRay(leftPosition, leftRotation * (Vector3.up * 0.1f));

            Gizmos.color = Color.red;
            Gizmos.DrawRay(leftPosition, leftRotation * (Vector3.right * 0.1f));
            
            // =============================================
            
            (Vector3 rightPosition, Quaternion rightRotation) =  GetRestingValues(1);
            
            Gizmos.color = Color.deepPink;
            
            Gizmos.DrawWireCube(rightPosition, new Vector3(0.1f, 0.1f, 0.1f));

            Gizmos.color = Color.blue;
            Gizmos.DrawRay(rightPosition, rightRotation * (Vector3.forward * 0.1f));

            Gizmos.color = Color.green;
            Gizmos.DrawRay(rightPosition, rightRotation * (Vector3.up * 0.1f));

            Gizmos.color = Color.red;
            Gizmos.DrawRay(rightPosition, rightRotation * (Vector3.right * 0.1f));
        }
    }
}

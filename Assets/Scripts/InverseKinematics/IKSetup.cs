using System;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace LAS
{
    public class IKSetup : MonoBehaviour
    {
        [Header("Affected bones")]
        [SerializeField] private Transform leftFoot;
        [SerializeField] private Transform rightFoot;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        [Space(10)]
        [SerializeField] private Transform head;
        [SerializeField] private Transform torso;

        [Header("Effectors")] 
        [SerializeField] private Mesh targetEffectorMesh;
        [SerializeField] private Mesh hintEffectorMesh;

        [Header("Default values")] 
        [SerializeField] private float maxHeadRotation = 70;
        [SerializeField] private float maxTorsoRotation = 30;
        
        private GameObject ikRig;
        private GameObject bodyPivot;
        private GameObject root;

        private GameObject headPivot;
        private GameObject torsoPivot;
        
        private RigBuilder rigBuilder;
        
        public void Setup()
        {
            ClearRig();

            root = transform.parent.gameObject;
            
            ikRig = new GameObject();
            ikRig.transform.parent = transform;
            ikRig.transform.name = "IKRig";
            ikRig.transform.localPosition = Vector3.zero;
            Rig rig = ikRig.AddComponent<Rig>();
            
            gameObject.AddComponent<RigBuilder>();
            rigBuilder = gameObject.GetComponent<RigBuilder>();
            
            SetupTwoBoneConstraint(leftHand, "Left Hand");
            SetupTwoBoneConstraint(rightHand, "Right Hand");
            SetupTwoBoneConstraint(leftFoot, "Left Foot", true);
            SetupTwoBoneConstraint(rightFoot, "Right Foot", true);

            bodyPivot = new GameObject();
            bodyPivot.transform.parent = root.transform;
            bodyPivot.name = "BodyPivot";
            bodyPivot.transform.localPosition = Vector3.zero;
            
            SetupMultiAimConstraint(head, "Head");
            SetupMultiAimConstraint(torso, "Torso");
            
            rigBuilder.layers.Clear();
            rigBuilder.layers.Add(new RigLayer(rig, true));

            SetupScripts();
        }

        private void SetupTwoBoneConstraint(Transform tip, string name, bool isLeg = false)
        {
            GameObject constraintObject = new GameObject();
            constraintObject.transform.parent = ikRig.transform;
            constraintObject.transform.name = name;
            constraintObject.transform.localPosition = Vector3.zero;

            TwoBoneIKConstraint constraint = constraintObject.AddComponent<TwoBoneIKConstraint>();

            constraint.data.tip = tip;
            constraint.data.mid = tip.parent;
            constraint.data.root = tip.parent.parent;

            GameObject target = new GameObject();
            target.transform.parent = constraintObject.transform;
            target.transform.name = name + " Target";
            constraint.data.target = target.transform;

            GameObject hint = new GameObject();
            hint.transform.parent = constraintObject.transform;
            hint.transform.name = name + " Hint";
            constraint.data.hint = hint.transform;
            
            if (isLeg)
            {
                GameObject legPivot = new GameObject();
                legPivot.transform.parent = root.transform;
                legPivot.name = name + " Pivot";
                legPivot.transform.localPosition = Vector3.zero;

                target.transform.parent = legPivot.transform;
                hint.transform.parent = legPivot.transform;
            }
            else
            {
                target.AddComponent<IKGrabSetup>();
            }
            
            Vector3 rootPosition = constraint.data.root.position;
            Vector3 midPosition = constraint.data.mid.position;
            Vector3 tipPosition = constraint.data.tip.position;
            Quaternion tipRotation = constraint.data.tip.rotation;
            Vector3 targetPosition = constraint.data.target.position;
            Quaternion targetRotation = constraint.data.target.rotation;
            Vector3 hintPosition = constraint.data.hint.position;
            float posWeight = constraint.data.targetPositionWeight;
            float rotWeight = constraint.data.targetRotationWeight;
            float hintWeight = constraint.data.hintWeight;
            AffineTransform targetOffset = new AffineTransform(Vector3.zero, Quaternion.identity);

            AnimationRuntimeUtils.InverseSolveTwoBoneIK(rootPosition, midPosition, tipPosition, tipRotation,
                ref targetPosition, ref targetRotation, ref hintPosition, true, posWeight, rotWeight, hintWeight, targetOffset);
            
            constraint.data.target.position = targetPosition;
            constraint.data.target.rotation = targetRotation;
            constraint.data.hint.position = hintPosition;

            Rig rig = ikRig.GetComponent<Rig>();
            
            if (rig)
            {
                RigEffectorData.Style targetStyle = new RigEffectorData.Style();
                targetStyle.shape = targetEffectorMesh;
                targetStyle.color = new Color(1f, 0f, 0f, 0.5f);
                targetStyle.position = Vector3.zero;
                targetStyle.rotation = Vector3.zero;
                targetStyle.size = 0.10f;
                rig.AddEffector(constraint.data.target, targetStyle);
                
                RigEffectorData.Style hintStyle = new RigEffectorData.Style();
                hintStyle.shape = hintEffectorMesh;
                hintStyle.color = new Color(1f, 0f, 0f, 0.5f);
                hintStyle.position = Vector3.zero;
                hintStyle.rotation = Vector3.zero;
                hintStyle.size = 0.10f;
                rig.AddEffector(constraint.data.hint, hintStyle);
            }
        }

        private void SetupMultiAimConstraint(Transform bone, string name)
        {
            GameObject constraintObject = new GameObject();
            constraintObject.transform.parent = ikRig.transform;
            constraintObject.transform.name = name;
            constraintObject.transform.localPosition = Vector3.zero;

            MultiAimConstraint constraint = constraintObject.AddComponent<MultiAimConstraint>();

            GameObject targetPivot = new GameObject();
            targetPivot.transform.parent = bodyPivot.transform;
            targetPivot.name = name + "Pivot";
            targetPivot.transform.position = bone.position;

            GameObject target = new GameObject();
            target.transform.parent = targetPivot.transform;
            target.transform.name = name + "Target";
            target.transform.localPosition = targetPivot.transform.forward;

            WeightedTransform targetTransform;
            targetTransform.transform = target.transform;
            targetTransform.weight = 1;
            
            constraint.data.constrainedObject = bone;
            constraint.data.sourceObjects.Add(targetTransform);
            constraint.data.constrainedXAxis = false;
            constraint.data.constrainedYAxis = true;
            constraint.data.constrainedZAxis = false;
            constraint.data.limits = new Vector2(-maxHeadRotation, maxHeadRotation);
        }

        private void SetupScripts()
        {
            IKLookAt lookAt = root.AddComponent<IKLookAt>();
            
            IkLegTurning legTurning = root.AddComponent<IkLegTurning>();
            
            IKGrab grab = root.AddComponent<IKGrab>();
            
            lookAt.AutoSetup(head, torso);
            
            legTurning.AutoSetup(ikRig.transform);
            
            grab.AutoSetup(ikRig.transform, leftHand, rightHand);
        }

        private void ClearRig()
        {
            if (transform.Find("IKRig") && ikRig)
                DestroyImmediate(ikRig.gameObject);
            
            if (rigBuilder)
                rigBuilder.layers.Clear();

            if (!root)
                return;

            while (root.transform.childCount > 1)
            {
                if(root.transform.GetChild(1))
                    DestroyImmediate(root.transform.GetChild(1).gameObject);
            }
            
            if(root.TryGetComponent(out IKLookAt lookAt))
                DestroyImmediate(lookAt);
            
            if(root.TryGetComponent(out IkLegTurning legTurning))
                DestroyImmediate(legTurning);
            
            if(root.TryGetComponent(out IKGrab grab))
                DestroyImmediate(grab);
        }
    }
}

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
        
        private GameObject ikRig;
        
        private RigBuilder rigBuilder;
        
        public void Setup()
        {
            ClearRig();
            
            ikRig = new GameObject();
            ikRig.transform.parent = transform;
            ikRig.transform.name = "IKRig";
            ikRig.transform.localPosition = Vector3.zero;
            Rig rig = ikRig.AddComponent<Rig>();
            
            gameObject.AddComponent<RigBuilder>();
            rigBuilder = gameObject.GetComponent<RigBuilder>();
            
            SetupTwoBoneConstraint(leftHand, "Left Hand");
            SetupTwoBoneConstraint(rightHand, "Right Hand");
            SetupTwoBoneConstraint(leftFoot, "Left Foot");
            SetupTwoBoneConstraint(rightFoot, "Right Foot");
            
            rigBuilder.layers.Clear();
            rigBuilder.layers.Add(new RigLayer(rig, true));
            
            //rigBuilder.Build();
            
            RigEffectorData data = new RigEffectorData();
        }


        private void SetupTwoBoneConstraint(Transform tip, string name)
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
            target.transform.name = "Target";
            constraint.data.target = target.transform;

            GameObject hint = new GameObject();
            hint.transform.parent = constraintObject.transform;
            hint.transform.name = "Hint";
            constraint.data.hint = hint.transform;
            
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
        }

        private void ClearRig()
        {
            Transform ikRig;

            if (ikRig = transform.Find("IKRig"))
                DestroyImmediate(ikRig.gameObject);

            if (rigBuilder)
                rigBuilder.layers.Clear();
        }
    }
}

using UnityEditor;
using UnityEngine;

namespace LAS
{
    [CustomEditor(typeof(IKTarget))]
    public class IKTargetEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (GUILayout.Button("Toggle IK"))
            {
                IKTarget ikTarget = target as IKTarget;
                ikTarget.ToggleIK();
            }
        }
    }
}

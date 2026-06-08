using UnityEditor;
using UnityEngine;

namespace LAS
{
    [CustomEditor(typeof(IKGrabSetup))]
    public class IKGrabEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (GUILayout.Button("Toggle IK"))
            {
                IKGrabSetup setup = target as IKGrabSetup;
                setup.ToggleIK();
            }
            
            if (GUILayout.Button("Attach object to hand"))
            {
                IKGrabSetup setup = target as IKGrabSetup;
                setup.AttachObjectToHand();
            }

            GUILayout.Space(10);
            
            if (GUILayout.Button("Set rest pose"))
            {
                IKGrabSetup setup = target as IKGrabSetup;
                setup.SetRestingPose();
            }

            if (GUILayout.Button("Toggle rest pose previews"))
            {
                IKGrabSetup setup = target as IKGrabSetup;
                setup.ShowRestingPoses();
            }

            GUILayout.Space(10);
            
            if (GUILayout.Button("Add grab pose"))
            {
                IKGrabSetup setup = target as IKGrabSetup;
                setup.AddGrabPose();
            }

            if (GUILayout.Button("Toggle grab pose previews"))
            {
                IKGrabSetup setup = target as IKGrabSetup;
                setup.TogglePosePreviews();
            }

            if (GUILayout.Button("Clear grab poses"))
            {
                IKGrabSetup setup = target as IKGrabSetup;
                setup.ClearData();
            }
        }
    }
}

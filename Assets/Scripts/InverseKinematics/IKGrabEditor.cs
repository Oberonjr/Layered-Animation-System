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

            if (GUILayout.Button("Add grab pose"))
            {
                IKGrabSetup setup = target as IKGrabSetup;
                setup.AddGrabPose();
            }

            if (GUILayout.Button("Set rest pose"))
            {
                IKGrabSetup setup = target as IKGrabSetup;
                setup.SetRestingPose();
            }
            
            if (GUILayout.Button("Clear grab poses"))
            {
                IKGrabSetup setup = target as IKGrabSetup;
                setup.ClearData();
            }
        }
    }
}

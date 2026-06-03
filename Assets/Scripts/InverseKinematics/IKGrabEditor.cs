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

            if (GUILayout.Button("Add grab Pose"))
            {
                IKGrabSetup setup = target as IKGrabSetup;
                setup.AddGrabPose();
            }
            
            if (GUILayout.Button("Clear grab poses"))
            {
                IKGrabSetup setup = target as IKGrabSetup;
                setup.ClearData();
            }
        }
    }
}

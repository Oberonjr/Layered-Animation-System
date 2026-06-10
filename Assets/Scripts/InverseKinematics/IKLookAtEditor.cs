using UnityEditor;
using UnityEngine;

namespace LAS
{
    [CustomEditor(typeof(IKLookAt))]
    public class IKLookAtEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();            
            
            if (GUILayout.Button("Turn around"))
            {
                IKLookAt lookAt = target as IKLookAt;
                lookAt.TestTurnAround();
            }
        }
    }
}

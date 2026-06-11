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

            GUIContent content = new GUIContent();
            content.text = "Turn around";
            content.tooltip = "Turns the character 180 degrees";
            
            if (GUILayout.Button(content))
            {
                IKLookAt lookAt = target as IKLookAt;
                lookAt.TestTurnAround();
            }
        }
    }
}

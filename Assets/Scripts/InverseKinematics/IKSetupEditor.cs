using UnityEditor;
using UnityEngine;

namespace LAS
{
    [CustomEditor(typeof(IKSetup))]       
    public class IKSetupEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (GUILayout.Button("Setup"))
            {
                IKSetup ikSetup = target as IKSetup;
                ikSetup.Setup();
            }
        }
    }
}

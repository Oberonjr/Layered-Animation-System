using UnityEditor;
using UnityEngine;

namespace LAS
{
    [CustomEditor(typeof(IKGrabbable))]
    public class IKGrabbableEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (GUILayout.Button("Toggle grab pose previews"))
            {
                IKGrabbable grabbable = target as IKGrabbable;
                grabbable.DrawPreviews();

            }

            if (GUILayout.Button("Clear grab poses"))
            {
                IKGrabbable grabbable = target as IKGrabbable;
                grabbable.ClearData();
            }
        }
    }
}

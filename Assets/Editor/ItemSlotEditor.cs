using UnityEditor;
using LAS;
using UnityEngine;

namespace LAS
{
    [CustomEditor(typeof(ItemSlot))]
    [CanEditMultipleObjects]
    public class ItemSlotEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(4);

            if (GUILayout.Button("Rebuild Preview"))
            {
                foreach (var t in targets)
                    ((ItemSlot)t).RebuildPreview();
            }
        }
    }
}

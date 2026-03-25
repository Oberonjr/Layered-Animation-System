using UnityEditor;
using UnityEngine;
using LAS;

namespace LAS
{
    [CustomEditor(typeof(LocationTarget))]
    public class LocationTargetEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var location = (LocationTarget)target;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("── Item Slot Setup ──────────────────────────────", EditorStyles.miniLabel);

            EditorGUILayout.HelpBox(
                "Click 'Populate Item Slots' to scan the scene for all InteractableItem objects " +
                "and add them as keys in the itemSlots dictionary. " +
                "Then drag an ItemSlot component into each value field.",
                UnityEditor.MessageType.None);

            if (GUILayout.Button("Populate Item Slots from Scene"))
                PopulateFromScene(location);

            if (GUILayout.Button("Refresh Slot Previews"))
                RefreshPreviews(location);
        }

        private static void PopulateFromScene(LocationTarget location)
        {
            // Prefer registry lookup; fall back to FindObjectsByType if not in Play mode.
            InteractableItem[] items;

            var registry = NPCActionTargetRegistry.Instance;
            if (registry != null)
            {
                var fromRegistry = new System.Collections.Generic.List<InteractableItem>();
                foreach (var t in registry.GetAllTargets())
                    if (t is InteractableItem ii) fromRegistry.Add(ii);
                items = fromRegistry.ToArray();
            }
            else
            {
                items = Object.FindObjectsByType<InteractableItem>(FindObjectsSortMode.None);
            }

            if (items.Length == 0)
            {
                Debug.LogWarning("[LocationTargetEditor] No InteractableItem objects found in the scene.");
                return;
            }

            Undo.RecordObject(location, "Populate Item Slots");

            int added = 0;
            foreach (var item in items)
            {
                if (!location.itemSlots.ContainsKey(item))
                {
                    location.itemSlots.Add(item, null);
                    added++;
                }
            }

            EditorUtility.SetDirty(location);
            Debug.Log($"[LocationTargetEditor] Added {added} key(s) to '{location.TargetName}' itemSlots. " +
                      "Drag ItemSlot components into the value fields.");
        }

        private static void RefreshPreviews(LocationTarget location)
        {
            foreach (var kvp in location.itemSlots)
            {
                if (kvp.Value != null)
                    kvp.Value.RebuildPreview();
            }
            Debug.Log($"[LocationTargetEditor] Refreshed slot previews for '{location.TargetName}'.");
        }
    }
}

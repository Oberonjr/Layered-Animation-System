using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(NPCBehaviourController))]
public class NPCBehaviourControllerEditor : Editor
{
    private string testTargetName = "";
    private string testSecondaryTargetName = "";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        NPCBehaviourController behaviour = (NPCBehaviourController)target;

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play mode to test actions.", UnityEditor.MessageType.Info);
            return;
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("── Test Actions ──────────────────", EditorStyles.boldLabel);

        // Target name fields
        testTargetName = EditorGUILayout.TextField("Primary Target Name", testTargetName);
        testSecondaryTargetName = EditorGUILayout.TextField("Secondary Target Name", testSecondaryTargetName);

        EditorGUILayout.Space(4);

        // No-target actions
        EditorGUILayout.LabelField("No Target Required", EditorStyles.miniBoldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Look At Player"))  TestAction("LOOK_AT_PLAYER", behaviour, null, null);
            if (GUILayout.Button("Hand To Player"))  TestAction("HAND_TO_PLAYER", behaviour, null, null);
            if (GUILayout.Button("Return To Idle"))  TestAction("RETURN_TO_IDLE", behaviour, null, null);
        }

        EditorGUILayout.Space(4);

        // Single-target actions
        EditorGUILayout.LabelField("Primary Target Required", EditorStyles.miniBoldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Look At"))  TestAction("LOOK_AT",      behaviour, testTargetName, null);
            if (GUILayout.Button("Go To"))    TestAction("GO_TO",        behaviour, testTargetName, null);
            if (GUILayout.Button("Pick Up"))  TestAction("PICK_UP",      behaviour, testTargetName, null);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Hand To NPC"))    TestAction("HAND_TO_NPC",   behaviour, testTargetName, null);
            if (GUILayout.Button("Grab From"))      TestAction("GRAB_FROM",     behaviour, testTargetName, null);
            if (GUILayout.Button("Request From"))   TestAction("REQUEST_FROM",  behaviour, testTargetName, null);
        }

        EditorGUILayout.Space(6);

        // Status
        EditorGUILayout.LabelField("── Runtime Status ────────────────", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Is Moving:         {behaviour.IsMoving}");
        EditorGUILayout.LabelField($"Is Holding Object: {behaviour.IsHoldingObject}");
        EditorGUILayout.LabelField($"Is Offering:       {behaviour.IsOffering}");
        EditorGUILayout.LabelField($"Held Object:       {(behaviour.HeldObject != null ? behaviour.HeldObject.name : "—")}");

        // Repaint for live status updates
        if (Application.isPlaying)
            Repaint();
    }

    private void TestAction(string actionKey, NPCBehaviourController behaviour, string primary, string secondary)
    {
        var dispatcher = NPCActionDispatcher.Instance;
        if (dispatcher == null)
        {
            Debug.LogWarning("[BehaviourEditor] NPCActionDispatcher not found in scene.");
            return;
        }

        Transform primaryT  = string.IsNullOrEmpty(primary)   ? null : NPCActionTargetRegistry.Instance?.Resolve(primary);
        Transform secondaryT = string.IsNullOrEmpty(secondary) ? null : NPCActionTargetRegistry.Instance?.Resolve(secondary);

        dispatcher.DispatchDirect(actionKey, behaviour, primaryT, secondaryT);

        Debug.Log($"[BehaviourEditor] Test fired: {actionKey}" +
                  $"{(primaryT != null ? $" → {primaryT.name}" : "")}");
    }
}

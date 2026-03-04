using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Custom Unity Editor for NPCBehaviourController.
/// Provides two testing strategies in Play mode:
/// 1. Action First: Select an action, then choose compatible targets
/// 2. Target First: Select a target, then see compatible actions as buttons
/// Allows rapid testing and debugging of NPC actions without LLM interaction.
/// </summary>
[CustomEditor(typeof(NPCBehaviourController))]
public class NPCBehaviourControllerEditor : Editor
{
    // Selected items for Action First strategy
    private IActionTarget selectedPrimaryTarget;
    private IActionTarget selectedSecondaryTarget;
    private NPCActionDefinition selectedAction;

    // Cached lists of available options
    private List<IActionTarget> availablePrimaryTargets = new List<IActionTarget>();
    private List<IActionTarget> availableSecondaryTargets = new List<IActionTarget>();
    private List<NPCActionDefinition> availableActions = new List<NPCActionDefinition>();

    // Dropdown indices for selection persistence
    private int primaryTargetIndex = 0;
    private int secondaryTargetIndex = 0;
    private int actionIndex = 0;

    // Testing strategy selection
    /// <summary>Determines which workflow to display in the inspector.</summary>
    private enum TestingStrategy { ActionFirst, TargetFirst }
    private TestingStrategy currentStrategy = TestingStrategy.ActionFirst;

    /// <summary>
    /// Renders the custom inspector GUI.
    /// Shows default inspector first, then Play mode testing UI if in Play mode.
    /// Auto-repaints during Play mode for live status updates.
    /// </summary>
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        NPCBehaviourController behaviour = (NPCBehaviourController)target;

        if (!Application.isPlaying)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("Enter Play mode to test actions.", UnityEditor.MessageType.Info);
            return;
        }

        // Refresh available targets and actions
        RefreshAvailableOptions(behaviour);

        EditorGUILayout.Space(10);
        DrawSeparator("Testing Strategy");

        // Strategy selector
        currentStrategy = (TestingStrategy)EditorGUILayout.EnumPopup("Strategy", currentStrategy);

        EditorGUILayout.Space(6);

        if (currentStrategy == TestingStrategy.ActionFirst)
        {
            DrawActionFirstStrategy(behaviour);
        }
        else
        {
            DrawTargetFirstStrategy(behaviour);
        }

        EditorGUILayout.Space(10);
        DrawSeparator("Runtime Status");

        // Status display
        DrawStatusDisplay(behaviour);

        // Auto-repaint for live updates
        if (Application.isPlaying)
            Repaint();
    }

    /// <summary>
    /// Refreshes the cached lists of available targets and actions from the registries.
    /// Filters out the NPC itself from target lists and clamps selection indices.
    /// Called every frame in OnInspectorGUI to keep UI in sync with runtime changes.
    /// </summary>
    /// <param name="behaviour">The NPCBehaviourController being inspected.</param>
    private void RefreshAvailableOptions(NPCBehaviourController behaviour)
    {
        var registry = NPCActionTargetRegistry.Instance;
        if (registry == null) return;

        // Get all targets, excluding self
        availablePrimaryTargets = registry.GetAllTargets()
            .Where(t => t.Transform != behaviour.transform)
            .OrderBy(t => t.Type)
            .ThenBy(t => t.TargetName)
            .ToList();

        availableSecondaryTargets = new List<IActionTarget>(availablePrimaryTargets);

        // Get all active actions from dispatcher
        var dispatcher = NPCActionDispatcher.Instance;
        if (dispatcher != null)
        {
            availableActions = dispatcher.GetActiveActions().ToList();
        }

        // Clamp indices
        primaryTargetIndex = Mathf.Clamp(primaryTargetIndex, 0, Mathf.Max(0, availablePrimaryTargets.Count - 1));
        secondaryTargetIndex = Mathf.Clamp(secondaryTargetIndex, 0, Mathf.Max(0, availableSecondaryTargets.Count - 1));
        actionIndex = Mathf.Clamp(actionIndex, 0, Mathf.Max(0, availableActions.Count - 1));

        // Update selected references
        if (availablePrimaryTargets.Count > 0)
            selectedPrimaryTarget = availablePrimaryTargets[primaryTargetIndex];

        if (availableSecondaryTargets.Count > 0)
            selectedSecondaryTarget = availableSecondaryTargets[secondaryTargetIndex];

        if (availableActions.Count > 0)
            selectedAction = availableActions[actionIndex];
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // STRATEGY 1: ACTION FIRST
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Draws the "Action First" workflow UI.
    /// User selects an action from dropdown, then compatible targets are shown.
    /// Includes quick test buttons for the selected action with all compatible targets.
    /// </summary>
    /// <param name="behaviour">The NPCBehaviourController being tested.</param>
    private void DrawActionFirstStrategy(NPCBehaviourController behaviour)
    {
        DrawSeparator("Action → Target");
        EditorGUILayout.HelpBox("Select action first, then choose compatible targets", UnityEditor.MessageType.Info);

        // Action selection dropdown
        DrawActionDropdown();

        EditorGUILayout.Space(6);

        // Target selection (filtered by action compatibility)
        if (selectedAction != null)
        {
            if (selectedAction.requiresTarget)
                DrawFilteredTargetDropdown();

            if (selectedAction.requiresSecondaryTarget)
                DrawSecondaryTargetDropdown();
        }

        EditorGUILayout.Space(8);

        // Execute button
        DrawExecuteButton(behaviour);

        EditorGUILayout.Space(10);

        // Quick actions with currently selected target
        if (selectedAction != null && selectedAction.requiresTarget && selectedPrimaryTarget != null)
        {
            DrawSeparator($"Quick Test: {selectedAction.displayName}");
                DrawQuickActionButtonsForAction(behaviour);
                }
            }

            /// <summary>
            /// Draws a target dropdown filtered by the selected action's valid target types.
            /// Only shows targets that are compatible with the current action.
            /// </summary>
            private void DrawFilteredTargetDropdown()
    {
        if (availablePrimaryTargets.Count == 0)
        {
            EditorGUILayout.HelpBox("No targets found in scene.", UnityEditor.MessageType.Warning);
            return;
        }

        // Filter by action's valid target types
        var compatibleTargets = availablePrimaryTargets
            .Where(t => selectedAction.IsValidTargetType(t.Type))
            .ToList();

        if (compatibleTargets.Count == 0)
        {
            EditorGUILayout.HelpBox($"No compatible targets for {selectedAction.displayName}.", UnityEditor.MessageType.Warning);
            return;
        }

        string[] targetLabels = compatibleTargets.Select(t => $"[{t.Type}] {t.TargetName}").ToArray();

        int compatibleIndex = Mathf.Clamp(primaryTargetIndex, 0, compatibleTargets.Count - 1);
        compatibleIndex = EditorGUILayout.Popup("Primary Target", compatibleIndex, targetLabels);

        selectedPrimaryTarget = compatibleTargets[compatibleIndex];
        primaryTargetIndex = availablePrimaryTargets.IndexOf(selectedPrimaryTarget);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // STRATEGY 2: TARGET FIRST
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Draws the "Target First" workflow UI.
    /// User selects a target from dropdown, then compatible actions are shown as clickable buttons.
    /// Provides a quick way to test all actions that can be performed on a specific target.
    /// </summary>
    /// <param name="behaviour">The NPCBehaviourController being tested.</param>
    private void DrawTargetFirstStrategy(NPCBehaviourController behaviour)
    {
        DrawSeparator("Target → Actions");
        EditorGUILayout.HelpBox("Select target first, then see compatible actions", UnityEditor.MessageType.Info);

        // Target selection (ALL targets)
        DrawAllTargetsDropdown();

        EditorGUILayout.Space(10);

        // Show compatible actions as buttons
        if (selectedPrimaryTarget != null)
        {
            DrawSeparator($"Actions for [{selectedPrimaryTarget.Type}] {selectedPrimaryTarget.TargetName}");
            DrawCompatibleActionButtons(behaviour);
        }
    }

    /// <summary>
    /// Draws a dropdown showing all available targets (not filtered by action compatibility).
    /// Used in Target First strategy.
    /// </summary>
    private void DrawAllTargetsDropdown()
    {
        if (availablePrimaryTargets.Count == 0)
        {
            EditorGUILayout.HelpBox("No targets found in scene.", UnityEditor.MessageType.Warning);
            return;
        }

        string[] targetLabels = availablePrimaryTargets.Select(t => $"[{t.Type}] {t.TargetName}").ToArray();
        primaryTargetIndex = EditorGUILayout.Popup("Target", primaryTargetIndex, targetLabels);
        selectedPrimaryTarget = availablePrimaryTargets[primaryTargetIndex];
    }

    /// <summary>
    /// Draws action buttons filtered by compatibility with the selected target.
    /// Shows target-specific actions and general no-target actions.
    /// Clicking a button immediately executes that action on the NPC.
    /// </summary>
    /// <param name="behaviour">The NPCBehaviourController to execute actions on.</param>
    private void DrawCompatibleActionButtons(NPCBehaviourController behaviour)
    {
        if (availableActions.Count == 0)
        {
            EditorGUILayout.HelpBox("No actions registered in dispatcher.", UnityEditor.MessageType.Warning);
            return;
        }

        // Get actions that can use this target
        var compatibleActions = availableActions
            .Where(a => a.requiresTarget && a.IsValidTargetType(selectedPrimaryTarget.Type))
            .ToList();

        // Also show no-target actions (always available)
        var noTargetActions = availableActions
            .Where(a => !a.requiresTarget && !a.requiresSecondaryTarget)
            .ToList();

        if (compatibleActions.Count == 0 && noTargetActions.Count == 0)
        {
            EditorGUILayout.HelpBox($"No actions compatible with {selectedPrimaryTarget.Type} targets.", UnityEditor.MessageType.Info);
            return;
        }

        // Compatible actions
        if (compatibleActions.Count > 0)
        {
            EditorGUILayout.LabelField("Target-Specific Actions", EditorStyles.miniBoldLabel);

            int columns = 3;
            int currentCol = 0;
            EditorGUILayout.BeginHorizontal();

            foreach (var action in compatibleActions)
            {
                GUI.backgroundColor = action.editorColor;
                if (GUILayout.Button(action.displayName, GUILayout.Height(30)))
                {
                    NPCActionDispatcher.Instance?.DispatchDirect(action, behaviour, selectedPrimaryTarget.Transform, null);
                    Debug.Log($"[Editor] {behaviour.name} → {action.displayName} → {selectedPrimaryTarget.TargetName}");
                }

                currentCol++;
                if (currentCol >= columns)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    currentCol = 0;
                }
            }

            if (currentCol > 0)
                EditorGUILayout.EndHorizontal();
            else
                EditorGUILayout.EndHorizontal(); // close the empty one

            GUI.backgroundColor = Color.white;
        }

        // No-target actions
        if (noTargetActions.Count > 0)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("General Actions (No Target)", EditorStyles.miniBoldLabel);

            EditorGUILayout.BeginHorizontal();
            foreach (var action in noTargetActions.Take(4))
            {
                GUI.backgroundColor = action.editorColor;
                if (GUILayout.Button(action.displayName))
                {
                    NPCActionDispatcher.Instance?.DispatchDirect(action, behaviour, null, null);
                }
            }
            EditorGUILayout.EndHorizontal();
            GUI.backgroundColor = Color.white;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SHARED UI COMPONENTS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Draws a dropdown to select an action from all registered actions.
    /// Used in Action First strategy.
    /// </summary>
    private void DrawActionDropdown()
    {
        if (availableActions.Count == 0)
        {
            EditorGUILayout.HelpBox("No actions registered in NPCActionDispatcher.", UnityEditor.MessageType.Warning);
            return;
        }

        string[] actionNames = availableActions.Select(a => a.displayName).ToArray();
        actionIndex = EditorGUILayout.Popup("Action", actionIndex, actionNames);
        selectedAction = availableActions[actionIndex];
    }

    /// <summary>
    /// Draws a dropdown for selecting a secondary target.
    /// Only shown for actions that require a secondary target (rare).
    /// </summary>
    private void DrawSecondaryTargetDropdown()
    {
        if (availableSecondaryTargets.Count == 0) return;

        string[] targetLabels = availableSecondaryTargets.Select(t => $"[{t.Type}] {t.TargetName}").ToArray();
        secondaryTargetIndex = EditorGUILayout.Popup("Secondary Target", secondaryTargetIndex, targetLabels);
        selectedSecondaryTarget = availableSecondaryTargets[secondaryTargetIndex];
    }

    /// <summary>
    /// Draws the main execute button that triggers the selected action with selected targets.
    /// Button color matches the action's editorColor for visual consistency.
    /// </summary>
    /// <param name="behaviour">The NPCBehaviourController to execute the action on.</param>
    private void DrawExecuteButton(NPCBehaviourController behaviour)
    {
        if (selectedAction == null)
        {
            EditorGUILayout.HelpBox("No action selected.", UnityEditor.MessageType.Info);
            return;
        }

        GUI.backgroundColor = selectedAction.editorColor;
        if (GUILayout.Button($"Execute: {selectedAction.displayName}", GUILayout.Height(30)))
        {
            Transform primary = selectedAction.requiresTarget ? selectedPrimaryTarget?.Transform : null;
            Transform secondary = selectedAction.requiresSecondaryTarget ? selectedSecondaryTarget?.Transform : null;

            NPCActionDispatcher.Instance?.DispatchDirect(selectedAction, behaviour, primary, secondary);
            Debug.Log($"[Editor] Executed {selectedAction.displayName} on {behaviour.name}");
        }
        GUI.backgroundColor = Color.white;
    }

    /// <summary>
    /// Draws quick test buttons showing compatible targets for the selected action.
    /// Limited to 6 targets for UI space. Clicking executes the action on that target immediately.
    /// </summary>
    /// <param name="behaviour">The NPCBehaviourController to execute actions on.</param>
    private void DrawQuickActionButtonsForAction(NPCBehaviourController behaviour)
    {
        // Show quick test buttons for the selected action with all compatible targets
        var compatibleTargets = availablePrimaryTargets
            .Where(t => selectedAction.IsValidTargetType(t.Type))
            .Take(6)
            .ToList();

        if (compatibleTargets.Count == 0) return;

        EditorGUILayout.LabelField("Quick targets:", EditorStyles.miniBoldLabel);

        int columns = 3;
        int currentCol = 0;
        EditorGUILayout.BeginHorizontal();

        foreach (var target in compatibleTargets)
        {
            GUI.backgroundColor = selectedAction.editorColor;
            if (GUILayout.Button($"{target.TargetName}"))
            {
                NPCActionDispatcher.Instance?.DispatchDirect(selectedAction, behaviour, target.Transform, null);
                Debug.Log($"[Editor] {behaviour.name} → {selectedAction.displayName} → {target.TargetName}");
            }

            currentCol++;
            if (currentCol >= columns)
            {
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                currentCol = 0;
            }
        }

        if (currentCol > 0)
            EditorGUILayout.EndHorizontal();
        else
            EditorGUILayout.EndHorizontal();

        GUI.backgroundColor = Color.white;
    }

    /// <summary>
    /// Displays runtime status information about the NPC's current state.
    /// Shows: IsMoving, IsHoldingObject, IsOffering, HeldObject name.
    /// Uses rich text for colored yes/no values.
    /// </summary>
    /// <param name="behaviour">The NPCBehaviourController whose status to display.</param>
    private void DrawStatusDisplay(NPCBehaviourController behaviour)
    {
        var statusStyle = new GUIStyle(EditorStyles.label) { richText = true };

        EditorGUILayout.LabelField($"<b>Is Moving:</b> {FormatBool(behaviour.IsMoving)}", statusStyle);
        EditorGUILayout.LabelField($"<b>Is Holding Object:</b> {FormatBool(behaviour.IsHoldingObject)}", statusStyle);
        EditorGUILayout.LabelField($"<b>Is Offering:</b> {FormatBool(behaviour.IsOffering)}", statusStyle);
        EditorGUILayout.LabelField($"<b>Held Object:</b> {(behaviour.HeldObject != null ? behaviour.HeldObject.name : "—")}", statusStyle);
    }

    /// <summary>
    /// Draws a labeled separator line for organizing UI sections.
    /// </summary>
    /// <param name="label">The text to display in the separator.</param>
    private void DrawSeparator(string label)
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"── {label} ────────────", EditorStyles.boldLabel);
    }

    /// <summary>
    /// Formats a boolean value as colored rich text.
    /// True = green "Yes", False = grey "No".
    /// </summary>
    /// <param name="value">The boolean to format.</param>
    /// <returns>Formatted rich text string.</returns>
    private string FormatBool(bool value)
        => value ? "<color=green>Yes</color>" : "<color=grey>No</color>";
}
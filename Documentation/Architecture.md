# Layered Animation System — Architecture Reference

This document covers the full design of the LAS codebase: how its subsystems are structured, how they talk to each other, and how to extend them.

---

## Table of Contents

1. [System Overview](#1-system-overview)
2. [Conversation Pipeline (Two-Step LLM)](#2-conversation-pipeline-two-step-llm)
3. [NPC Identity and Registration](#3-npc-identity-and-registration)
4. [Event Bus](#4-event-bus)
5. [Action System](#5-action-system)
6. [Finite State Machine (FSM)](#6-finite-state-machine-fsm)
7. [FSM Action States](#7-fsm-action-states)
8. [IK System](#8-ik-system)
9. [Conversation Flow Controller](#9-conversation-flow-controller)
10. [Target Registry](#10-target-registry)
11. [Scenario JSON Format](#11-scenario-json-format)
12. [How to Add a New Action](#12-how-to-add-a-new-action)
13. [Technical Notes and Known Quirks](#13-technical-notes-and-known-quirks)

---

## 1. System Overview

LAS is a Unity-based NPC AI system that drives character dialogue and physical behaviour from natural language input. The player types (or speaks) a message; the system sends it to a Large Language Model (LLM); and the LLM response is parsed to produce both spoken dialogue and a sequence of physical actions the NPC performs in world-space.

**Key design pillars:**

- **Two-step LLM**: Dialogue generation and action classification are separate LLM calls with different temperature settings, so creative conversation and deterministic action selection don't interfere.
- **Queue-based FSM**: Physical actions run through a per-NPC finite state machine that sequences multi-step behaviours (walk → pick up → hand over) without nested coroutines.
- **ScriptableObject action definitions**: Each action type is a data asset in the Project. Adding an action means creating an SO and a state class.
- **Static event bus**: Systems communicate without direct references through `NPCEventBus`.

### Top-level component layout (per NPC GameObject)

```
NPC GameObject
├── NPCController         — identity, visual highlight, event bus registration
├── NPCBehaviourController — FSM host, public action API, inspector settings
├── NavMeshAgent           — used by GoToState for pathfinding
├── Animator               — driven by action states (Blend float, triggers)
├── IKController           — bridge to IKLookAt / IkLegTurning / IKGrab
├── IKLookAt               — head/neck tracking 
├── IkLegTurning           — procedural foot placement 
└── IKGrab                 — object grab IK blending 
```

### Top-level component layout (Manager GameObject)

```
Manager GameObject
├── NPCManager             — player input, LLM calls, chat UI, scenario loading
├── NPCActionDispatcher    — routes parsed action commands to NPC FSMs
└── ConversationFlowController — NPC auto-conversation, idle prompts, typing detection
```

---

## 2. Conversation Pipeline (Two-Step LLM)

This is the core loop. It runs every time the player sends a message, or when the system triggers an NPC-to-NPC turn.

```
Player message
      │
      ▼
NPCManager.OnPlayerSendMessage()
      │  builds ConversationTurn
      ▼
GetNPCResponse(turn)
      │
      ├─ STEP 1: Dialogue generation
      │    NPCPromptBuilder.BuildDialogueRequest(turn)
      │        ↳ systemContent: scenario context, character descriptions,
      │                         core rules, response format instructions
      │        ↳ history: last N messages (configurable, default 15)
      │        ↳ userContent: current turn + speaker directive
      │    LLMProvider.SendRequest(...)
      │    NPCResponseParser.TryParseDialogueResponse(raw)
      │        → NPCResponse { npc_index, dialogue, internal_thought }
      │
      ├─ STEP 2: Action classification
      │    NPCPromptBuilder.BuildActionClassificationRequest(npcIndex, turn, dialogue, thought)
      │        ↳ Single-shot prompt (no history, no system message)
      │        ↳ Contains: trigger context, scene state (items/NPCs/locations),
      │                    recent action log, compact action vocabulary
      │        ↳ Rules: physical task → use action keys; no task → LOOK_AT_PLAYER
      │    LLMProvider.SendActionRequest(...)
      │    NPCResponseParser.ParseActionSequence(raw)
      │        → NPCActionSequence { actions: [ { action_key, action_target, action_secondary_target } ] }
      │
      └─ DISPATCH + STREAM
           NPCActionDispatcher.DispatchSequence(npcIndex, sequence, npcs)
           StreamTextToMessage(...)  ← character-by-character with punctuation pacing
           NPCEventBus.BroadcastNPCFinishedSpeaking(npcIndex)
```

### NPCPromptBuilder (`Assets/Scripts/API/NPC/Messaging/NPCPromptBuilder.cs`)

A pure-data helper (not a MonoBehaviour) constructed once at startup by `NPCManager`. It holds live references to the registered NPC list, conversation history, dispatcher, and settings. Its methods are stateless transforms from that shared state into `LLMRequest` objects.

Key methods:
- `BuildDialogueRequest(turn)` — full system prompt + history + current input
- `BuildActionClassificationRequest(...)` — compact single-shot classification prompt
- `GetEffectiveNPCConversationPrompt()` / `GetEffectiveIdlePrompt()` — resolve prompt from scenario JSON or inspector fallback

**Prompt priority for rules/guidelines**: JSON scenario fields override inspector values by default; inspector values can be forced to take precedence by toggling `override*` flags in `RulesAndGuidelinesSettings`.

### NPCResponseParser (`Assets/Scripts/API/NPC/Messaging/NPCResponseParser.cs`)

Stateless static class. Two methods:

- `TryParseDialogueResponse(raw, ...)` — tries `JsonUtility.FromJson<NPCResponse>` first, falls back to regex extraction if the LLM wraps the JSON in prose. The fallback looks for the longest non-field quoted string after stripping known meta-prefixes (NPC names, "description of", etc.).
- `ParseActionSequence(raw)` — parses `{"actions":[...]}` multi-step format, or the legacy single `{"action_key":...}` format. Returns `null` on failure (non-fatal — NPC simply takes no action).

### NPCManager (`Assets/Scripts/API/NPC/Messaging/NPCManager.cs`)

The orchestrator. Inspector-configured with:
- UI references (chat scroll view, input field, send button)
- Scenario JSON `TextAsset`
- `LLMProviderBase` reference (Ollama, OpenAI, or custom)
- Generation options for each step (temperature, topP, maxTokens, etc.)
- Streaming settings (character-per-second, punctuation pacing, adaptive speed)
- History limit, progression context toggle

Startup sequence (`InitializeSystem` coroutine):
1. Wait one frame for all `NPCController` instances to register via the event bus
2. Parse `ScenarioConfig` JSON; validate NPC count
3. Assign characters to NPCs (by `npcCharacterMap` if populated, otherwise by registration order)
4. Assign character data (`npcName`, `characterRole`, `characterDescription`) to each `NPCController`
5. Build `NPCPromptBuilder`
6. Connect to `LLMProvider`
7. Run background alias generation (if enabled)
8. Show initial scenario description in chat; fire opening conversation prompt

Interrupt handling: If a player message arrives while the LLM is generating, `InterruptCurrentGeneration()` sets `_interruptGenerationFlag = true`, calls `llmProvider.Interrupt()`, stops the streaming coroutine, and fires `NPCFinishedSpeaking`. The new message is queued and sent after the interrupt settles.

---

## 3. NPC Identity and Registration

### NPCController (`Assets/Scripts/API/NPC/Messaging/NPCController.cs`)

The identity component. Every NPC has one. Responsibilities:

- Stores `npcName`, `characterRole`, `characterDescription` (assigned from scenario at startup)
- Manages the speaking highlight: a shader-driven overlay material (`LAS/NPCSpeakingHighlight`) appended to the skinned mesh renderer. `SetSpeaking(true/false)` toggles `_Active` shader property to start/stop the pulse.
- Registers with `NPCEventBus` in `Start()`; unregisters in `OnDestroy()`
- Ensures an `NPCTarget` component exists (adds one if absent); forwards aliases to it

### Character assignment flow

1. Scene loads → `NPCController.Start()` fires `NPCEventBus.RegisterNPC(this)`
2. `NPCManager.HandleNPCRegistration()` adds NPC to `registeredNPCs`, assigns a sequential index
3. After `InitializeSystem` completes scenario parsing, `AssignCharacterData(name, role, description)` is called on each NPC — this re-registers the `NPCTarget` with the correct name

If `npcCharacterMap` is populated in the inspector (character name → NPCController reference), the list is reordered to match scenario character indices before assignment. This prevents character-swap bugs when multiple NPCs register in unpredictable order.

---

## 4. Event Bus

`NPCEventBus` (`Assets/Scripts/API/NPC/NPCEventBus.cs`) is a static class with C# events. No scene object needs a direct reference to another — systems subscribe/unsubscribe in `OnEnable`/`OnDisable` or `Start`/`OnDestroy`.

| Event | Signature | Fired by | Consumed by |
|-------|-----------|----------|-------------|
| `OnNPCRegistered` | `Action<NPCController>` | `NPCController.Start()` | `NPCManager` |
| `OnNPCUnregistered` | `Action<NPCController>` | `NPCController.OnDestroy()` | `NPCManager` |
| `OnNPCStartedSpeaking` | `Action<int, string>` | `NPCManager` | `ConversationFlowController` |
| `OnNPCFinishedSpeaking` | `Action<int>` | `NPCManager` | `ConversationFlowController` |
| `OnPlayerMessage` | `Action<string>` | `NPCManager` | `NPCManager`, `ConversationFlowController` |
| `OnActionImpossible` | `Action<int, string>` | Action states | `NPCManager` (shows in-character refusal) |

---

## 5. Action System

### NPCActionDefinition (`Assets/Scripts/API/NPC/Actions/NPCActionDefinition.cs`)

A ScriptableObject that defines one action type. Create via `NPC > Action Definition` in the Project context menu.

Fields:

| Field | Purpose |
|-------|---------|
| `actionKey` | Exact string the LLM outputs (e.g. `"PICK_UP"`) |
| `displayName` | Editor label |
| `llmDescription` | Injected into the LLM prompt to describe when to use this action |
| `requiresTarget` | Whether `action_target` must be supplied |
| `targetDescription` | What the target should be (injected into LLM prompt) |
| `requiresSecondaryTarget` | For actions that involve two targets |
| `validTargetTypes` | Restricts which `TargetType` values are acceptable (empty = any) |
| `requiresApproach` | If true, dispatcher prepends a `GoToState` before this action |
| `approachRangeType` | Which arrival range to use: `Pickup`, `HandOver`, or `Default` |

Subclasses override `MakeState(primary, secondary)` to produce the concrete `INPCState`. The base class returns a no-op `NPCActionState`.

**Registered action keys in the scene** (from the dispatcher's `actionDefinitions` list in the inspector):

| Key | Requires Approach | Notes |
|-----|:-----------------:|-------|
| `LOOK_AT_PLAYER` | No | Default when no physical action is needed |
| `GO_TO` | No | GoToState is the action itself |
| `PICK_UP` | Yes (Pickup range) | Approach target then pick up |
| `PUT_DOWN` | Yes (Pickup range) | Approach location then place item |
| `HAND_TO_NPC` | Yes (HandOver range) | Approach receiving NPC then hand off |
| `HAND_TO_PLAYER` | No (player approaches or OfferForPickup) | Hand item to player |
| `GRAB_FROM` | Yes (HandOver range) | Approach NPC and take their item |
| `REQUEST_FROM` | Yes (HandOver range) | Gesture/wait for another NPC to hand over |
| `NONE` | — | Sentinel; dispatcher ignores this key |

### NPCActionDispatcher (`Assets/Scripts/API/NPC/Actions/NPCActionDispatcher.cs`)

Singleton (set in `Awake`). Maintains a `Dictionary<string, NPCActionDefinition>` keyed by `actionKey`.

Three dispatch methods:

**`Dispatch(command, registeredNPCs)`** — single-action interrupt dispatch. Called for direct/legacy single-action responses. Uses `FSM.Interrupt` (clears queue, runs immediately). If `requiresApproach`, first interrupts with `GoToState`, then enqueues the action.

**`DispatchSequence(npcIndex, sequence, registeredNPCs)`** — multi-step sequence dispatch. Called by `NPCManager` for all normal LLM responses. Iterates `sequence.actions` and calls `FSM.Enqueue` for each step (with a `GoToState` prepended for approach actions). Calls `behaviour.StartQueuedActions()` at the end (which is a no-op, since `NPCFSM.Update()` auto-advances the queue).

**`DispatchDirect(actionDef, behaviour, primary, secondary)`** — editor testing only. Called by `NPCBehaviourControllerEditor` buttons in Play mode. Same logic as `Dispatch` but takes direct references instead of LLM command objects.

Target type validation runs before dispatching: if the resolved target doesn't match the action's `validTargetTypes`, the dispatch is aborted with a warning. This prevents the LLM from, e.g., using an NPC's name as the target of `PICK_UP`.

When the receiving NPC of a `HAND_TO_NPC` action has an `NPCBehaviourController`, the dispatcher also calls `SetLookAtTarget` on that NPC so it turns to face the approaching NPC.

---

## 6. Finite State Machine (FSM)

### NPCFSM (`Assets/Scripts/API/NPC/Actions/FSM/NPCFSM.cs`)

A per-NPC `Queue<INPCState>` runner. Lives inside `NPCBehaviourController`, ticked from its `Update()`.

```
                 ┌──────────────┐
Enqueue(state) → │   _queue     │
                 └──────┬───────┘
                        │  ActivateNext() when _currentState == null
                        ▼
                 ┌──────────────┐
                 │ _currentState│ ← EnterState(ctx)
                 │  .Update()   │ ← called every frame; returns true when done
                 └──────┬───────┘
                        │  ExitState(ctx) then ActivateNext()
                        ▼
                    (next state or idle)
```

**`Interrupt(state)`**: calls `CancelAll()` (exits current state, clears queue, resets NavMeshAgent path), then immediately enters the new state. This is the path for single LLM-dispatched actions and all `NPCBehaviourController` shorthand methods (`.GoTo()`, `.PickUp()`, etc.).

**`Enqueue(state)`**: appends to queue without starting. Used by `DispatchSequence` to build multi-step sequences.

**`CancelAll()`**: safe to call at any time. Always resets `Agent.isStopped = false` so locomotion isn't permanently blocked after an interrupt.

### NPCBehaviourContext (`Assets/Scripts/API/NPC/Actions/FSM/NPCBehaviourContext.cs`)

The read/write data bag shared between all FSM states. Every `INPCState` method receives a `ctx` reference. Holds:

- **Component refs**: `Agent` (NavMeshAgent), `Animator`, `IKController`, `ItemSlot`, `Host` (MonoBehaviour for coroutines)
- **Settings** (copied from inspector at construction): `ArrivalDistance`, `PickupRange`, `HandOverRange`, `GoToTimeoutSeconds`, `OfferOffset`, `PlayerHandoffMode`
- **Mutable state**: `HeldObject` (current item), `IsOffering` (true during OfferForPickup mode)
- **Events** (forwarded to `NPCBehaviourController`'s public events): `OnArrivedAtTarget`, `OnPickedUpObject`, `OnHandedObject`, `OnReturnedToIdle`
- **`GetPlayerTransform()`**: queries the target registry for the first registered `TargetType.Player`

### NPCBehaviourController (`Assets/Scripts/API/NPC/Actions/NPCBehaviourController.cs`)

The thin MonoBehaviour host. Constructs `NPCBehaviourContext` and `NPCFSM` in `Awake()`, ticks `FSM.Update()` in `Update()`. Also provides:

- Shorthand public methods (`GoTo`, `PickUp`, `PutDown`, `HandToPlayer`, `HandToNPC`, `GrabFrom`, `RequestFrom`, `LookAt`, `LookAtPlayer`, `ReturnToIdle`) — all call `FSM.Interrupt`.
- `TakeOfferedObject()` — called by VR interaction when the player physically grabs an offered object in `OfferForPickup` mode.
- Computed properties: `IsHoldingObject`, `IsOffering`, `HeldObject`, `IsExecutingQueue`, `IsMoving`.

---

## 7. FSM Action States

All states implement `INPCState` (or extend `NPCActionState` which provides a default no-op `ExitState`). Each has three methods: `EnterState(ctx)`, `UpdateState(ctx) → bool`, `ExitState(ctx)`. `UpdateState` returns `true` when the state is complete.

### GoToState

Navigates the NPC to a target using `NavMeshAgent`. The most complex state because it handles the IK look-at gate.

**EnterState**: if already in range, sets `_alreadyInRange = true` and returns. Otherwise, if the target is more than 15° off the NPC's forward, calls `IKController.SetLookAtTarget(_target)` and sets `_needsLookAt = true`.

**UpdateState gate**: before setting the NavMesh destination, the state waits for the NPC to face the target:
```
if (_needsLookAt && !ctx.IKController.isLookingAtTarget && !bodyFacingTarget)
    return false;
```
`bodyFacingTarget` is a 25° body angle fallback that bypasses `IKLookAt`'s settlement check, which is frame-rate sensitive (see §13).

Once the gate passes, `SetDestination(_target.position)` is called. Completion is detected by `pathArrived || stoppedInRange` (dual arrival check handles floating-point edge cases near the stopping distance). A `GoToTimeoutSeconds` ceiling prevents permanent blocking on invalid paths.

**ExitState**: re-enables leg IK, blends walk animation back to 0.

**ApproachRange enum** controls which stopping distance is used:
- `Pickup` → `ctx.PickupRange` (1.2m default)
- `HandOver` → `ctx.HandOverRange` (1.0m default)
- `Default` → `ctx.ArrivalDistance` (0.5m default)

Combined agent radii are added when approaching another NPC to prevent body overlap.

### PickUpState

Waits for `IKGrab.GrabObject()` to complete by polling `IKController.hasGrabbed`. On entry, calls `IKController.SetGrabTarget(target)`. Completion fires `OnPickedUpObject`.

### HandToNPCState

Waits until the receiving NPC is close enough (`HandOverRange`), then calls `IKController.SetGrabTarget(receivingNPC.ItemSlot)` to hand the item across. Uses `IKController.hasGrabbed` to detect when the IK snap completes.

### HandToPlayerState

Two modes (controlled by `ctx.PlayerHandoffMode`):

- **OfferForPickup**: holds the object out at `ctx.OfferOffset` from the NPC; waits for `ctx.IsOffering == false` (set by `TakeOfferedObject()` when the VR player physically grabs it).
- **DirectTransfer**: navigates to the player transform and re-parents the held object to the player.

### PutDownState

Places the held object at a `LocationTarget` or arbitrary transform. Re-parents the object, clears `ctx.HeldObject`.

### GrabFromState

Approaches another NPC and takes the item they are holding. The target NPC's `HandToNPCState` must be running concurrently.

### RequestFromState

Plays a gesture/wait cycle until the other NPC's `HandToNPCState` places an item in the receiving NPC's `ItemSlot`.

### LookAtState / LookAtPlayerState

Calls `IKController.SetLookAtTarget(target)` and completes immediately (one-shot look redirection, no waiting).

### IdleState

A persistent default state. Keeps the NPC looking at the player. Populated automatically when the FSM queue drains.

### ReturnToIdleState

Navigates to `ctx.IdlePosition`, then enters `IdleState`. Fires `OnReturnedToIdle` on completion.

---

## 8. IK System

The IK layer sits beneath the FSM states. `IKController` is the only file states interact with directly; the three underlying IK scripts (`IKLookAt`, `IkLegTurning`, `IKGrab`) are internal and must not be modified.

### IKController (`Assets/Scripts/InverseKinematics/IKController.cs`)

The bridge/API layer. All FSM states call methods on this; none call the IK scripts directly.

**Key API:**

| Method / Property | Description |
|-------------------|-------------|
| `SetLookAtTarget(target)` | Starts head tracking toward `target`. Resets `isLookingAtTarget` to false. |
| `ClearLookAtTarget()` | Stops head tracking. |
| `isLookingAtTarget` | True once `IKLookAt` has settled on the target. Latches — does not reset when `LookAt()` is re-triggered. |
| `UpdateLookAt()` | Called from FSM (tick). Re-triggers `lookAt.LookAt()` at a 0.2s interval to maintain persistent gaze without accumulating coroutines. |
| `EnableLegIK(bool)` | Enables/disables procedural foot placement. Disabled during walking; re-enabled on arrival. |
| `SetGrabTarget(target)` | Begins the IK grab blend toward `target`. |
| `hasGrabbed` | True when `IKGrab.OnGrabbed` has fired (snap complete). |
| `canStartAnim` | Forwarded from `IKLookAt.canStartAnim`. |

**Look-at latch**: `isLookingAtTarget` is set to `true` on first settle and never reset to `false` by `UpdateLookAt()`. The re-trigger of `lookAt.LookAt()` (every 0.2s) resets `IKLookAt.isLookingAtTarget` internally, but `IKController.isLookingAtTarget` stays true. This prevents `GoToState`'s gate from oscillating.

### IKLookAt (read-only)

One-shot head-tracking coroutine (`AccelerateRotationCo`). When the rotation settles within threshold of the target angle, it calls `ClearTarget()` and sets `isLookingAtTarget = true`.

**Frame-rate sensitivity**: the settlement threshold is `rotationSpeed * Time.deltaTime`, which shrinks at high frame rates. The `GoToState` `bodyFacingTarget` fallback (25° body angle) works around this.

### IkLegTurning (read-only)

Procedural foot placement using Unity Animation Rigging `TwoBoneIKConstraint` targets. `EnableLegIk(bool)` blends weights; note an `isBlendingLegWeights` guard that silently drops calls during an active blend.

### IKGrab (read-only)

DOTween-based object grab blend. `GrabObject()` tweens a weight from 0→1, then fires `OnGrabbed.Invoke()` which sets `hasGrabbed = true` in IKController. `PutDownObject()` reverses the blend and sets `grabTarget = null` in a DOTween `OnComplete` callback (0.5s delay — be aware of this async gap when re-targeting).

---

## 9. Conversation Flow Controller

`ConversationFlowController` (`Assets/Scripts/API/Messages/ConversationFlowController.cs`) lives on the same GameObject as `NPCManager`. It adds autonomy on top of the reactive pipeline.

**Responsibilities:**

- **Player typing detection**: on any non-modifier keypress, starts a short coroutine (`typingDetectionDelay`, default 0.3s). If the input field is active after the delay, sets `playerIsInterrupting = true` and cancels any pending NPC auto-response.
- **Auto-conversation**: after an NPC finishes speaking, `ShouldNPCRespond()` rolls probability to decide if the other NPC should follow up. Probability is boosted if the last message contained a `?` or a direct NPC name address, and reduced with a penalty per consecutive NPC message. Fires `NPCManager.TriggerNPCToNPCConversation()` after a random delay.
- **Idle prompts**: a background coroutine checks every `idleCheckInterval` seconds. If the player hasn't typed in `playerIdleTimeBeforePrompt` seconds, calls `NPCManager.TriggerNPCPromptForPlayer()`.
- **Flow reset**: `ResetConversationFlow()` is available for external control (e.g., after a scene transition).

NPC alternation for auto-conversation: `NPCManager` tracks `_lastAutoSpeakerIndex` and increments modulo NPC count so auto-conversation alternates between NPCs rather than always choosing NPC 0.

---

## 10. Target Registry

`NPCActionTargetRegistry` is a singleton that maintains a list of all `IActionTarget` instances in the scene. Targets self-register in `OnEnable` and unregister in `OnDisable`.

### IActionTarget and TargetType

```csharp
public enum TargetType { InteractableObject, Location, NPC, Player }

public interface IActionTarget {
    string TargetName { get; }
    Transform Transform { get; }
    TargetType Type { get; }
}
```

### Concrete target types

| Component | TargetType | Description |
|-----------|-----------|-------------|
| `InteractableItem` | `InteractableObject` | A physical object NPCs can pick up, put down, or hand over. Tracks `isHeld`, `heldByNPC`, `currentLocation`. |
| `LocationTarget` | `Location` | A named position in the scene (storage area, counter, etc.). NPCs can be directed to walk here or place items here. |
| `NPCTarget` | `NPC` | Added automatically by `NPCController.Awake()`. Makes the NPC addressable by name. |
| `PlayerTarget` | `Player` | Marks the player for gaze targets and `GetPlayerTransform()` queries. |

### Target name resolution

`NPCActionTargetRegistry.Resolve(name)` does a case-insensitive lookup first by primary name, then by aliases. `ActionTarget` (base class for all concrete targets) stores a list of aliases. Aliases can be registered manually in the inspector or generated at startup by the LLM-based `AliasGenerator` (if `generateAliasesOnStartup` is true on `NPCManager`).

---

## 11. Scenario JSON Format

Loaded from a `TextAsset` assigned in `NPCManager`'s inspector. Parsed with `JsonUtility.FromJson<ScenarioConfig>`.

```json
{
  "scenario": {
    "title": "Hospital Training",
    "description": "...",
    "setting": "ICU ward",
    "timeframe": "Morning shift",
    "context_hint": "medical"
  },
  "player_character": {
    "role": "Student nurse",
    "description": "..."
  },
  "characters": [
    {
      "name": "Dr. Chen",
      "role": "Senior physician",
      "personality": "Calm, methodical",
      "background": "15 years in emergency medicine",
      "communication_style": "Direct, uses technical terminology",
      "teaching_approach": "Socratic questioning",
      "current_state": "Reviewing patient charts"
    }
  ],
  "conversation_initialization": {
    "context": "The student has just arrived for their shift.",
    "opening_prompt": "Welcome the student to the ward.",
    "first_speaker_index": 0,
    "npc_conversation_prompt": "Continue the teaching scenario naturally.",
    "idle_player_prompt": "Prompt the student with a question."
  },
  "system_instructions": {
    "core_rules": ["Always address the player as 'student'"],
    "critical_rules": ["Never break character"],
    "behavior_guidelines": ["Correct mistakes gently"],
    "behavior_section_label": "Teaching Behavior",
    "interaction_guidelines": ["Use realistic medical terminology"]
  },
  "required_progression_steps": [
    {
      "step_id": 1,
      "title": "Patient Assessment",
      "description": "Guide the student through initial assessment.",
      "key_points": ["vitals", "history"],
      "teaching_moments": ["Ask about allergies"]
    }
  ]
}
```

**Prompt priority**: `system_instructions.critical_rules` → `RulesAndGuidelinesSettings.criticalRules` (inspector fallback). The inspector value wins only if the corresponding `override*` toggle is enabled. The same applies to `behavior_guidelines` and conversation prompts.

---

## 12. How to Add a New Action

### Step 1 — Create the NPCActionDefinition ScriptableObject

Right-click in the Project window → `NPC > Action Definition`. Fill in:

- `actionKey`: unique uppercase string (e.g. `"WAVE"`). This is what the LLM outputs.
- `llmDescription`: clear description of when to use this action. This is injected verbatim into the LLM prompt.
- `requiresApproach`: true if the NPC must walk to the target first.
- `approachRangeType`: which stopping distance to use.
- `validTargetTypes`: restrict to prevent the LLM from supplying wrong target types.

### Step 2 — Create an NPCActionDefinition subclass (if behaviour is custom)

```csharp
[CreateAssetMenu(menuName = "NPC/Action Definition/Wave")]
public class WaveActionDefinition : NPCActionDefinition
{
    public override NPCActionState MakeState(Transform primary, Transform secondary)
        => new WaveState(primary);
}
```

If the action has no special parameters, you can skip this step and create the state directly inside a base-class `MakeState` override — but a subclass keeps things tidy.

### Step 3 — Create the state class

```csharp
public class WaveState : NPCActionState
{
    private const string TriggerName = "Wave";
    private float _startTime;

    public WaveState(Transform target) : base(null) { }

    public override void EnterState(NPCBehaviourContext ctx)
    {
        ctx.Animator.SetTrigger(TriggerName);
        _startTime = Time.time;
    }

    public override bool UpdateState(NPCBehaviourContext ctx)
    {
        // Complete when the animation finishes or after a timeout.
        var info = ctx.Animator.GetCurrentAnimatorStateInfo(0);
        if (info.IsName("Wave") && info.normalizedTime >= 1f) return true;
        if (Time.time - _startTime > 5f) return true;
        return false;
    }

    public override void ExitState(NPCBehaviourContext ctx)
    {
        // Restore look-at target toward player after action.
        var player = ctx.GetPlayerTransform();
        if (player != null) ctx.IKController.SetLookAtTarget(player);
    }
}
```

### Step 4 — Register in the dispatcher

Select the `NPCActionDispatcher` GameObject in the scene, and add your new SO to the `Action Definitions` list in the inspector. Order does not matter.

### Step 5 — Wire up the Animator

Add the trigger and state to the Animator Controller on the NPC GameObject. Make sure the state name matches what `IsName(...)` checks.

That is everything — no changes to `NPCManager`, `NPCResponseParser`, or any other file.

---

## 13. Technical Notes and Known Quirks

### IKLookAt settlement is frame-rate sensitive

The settlement condition in `IKLookAt.AccelerateRotationCo` uses `rotationSpeed * Time.deltaTime` as its threshold. At high frame rates (e.g. in a maximized editor window with 200+ FPS), this threshold shrinks below floating-point noise in the Euler angle arithmetic, so the condition can never fire.

**Mitigation in place**: `GoToState` has a `bodyFacingTarget` fallback — if the NPC's body forward is within 25° of the target direction, the look-at gate is bypassed entirely. This is a frame-rate-independent check.

**Recommended fix** (to be applied in `IKLookAt` directly): replace `rotationSpeed * Time.deltaTime` with a small fixed constant (~`2f` degrees).

### IKController look-at latch

`IKController.isLookingAtTarget` latches `true` on first `IKLookAt` settle and never resets to `false` while a target is assigned. The re-trigger of `lookAt.LookAt()` (every 0.2s) resets `IKLookAt.isLookingAtTarget` internally, but `IKController.isLookingAtTarget` is only set, never cleared, until `ClearLookAtTarget()` is called. This prevents `GoToState`'s gate from oscillating (the original bug was that the gate cleared the latch every frame the NPC tried to re-check settlement).

### IKGrab async race on `PutDownObject`

`IKGrab.PutDownObject()` sets `grabTarget = null` inside a DOTween `OnComplete` callback that fires 0.5s after the call. If `SetGrabTarget(newItem)` is called before that 0.5s elapses, the null write overwrites the new target. This causes `SnapObject()` to bail out early (it checks `if (!grabTarget) return`), `OnGrabbed` never fires, and the next `PickUpState` hangs indefinitely.

**Recommended fix**: remove the `grabTarget = null` assignment from the `OnComplete` callback in `IKGrab.PutDownObject()`.

### IkLegTurning silent drop during blend

`IkLegTurning.EnableLegIk(bool)` returns immediately if `isBlendingLegWeights` is true. A call to re-enable the leg IK during an active blend is silently dropped. This can leave the IK in the wrong state if transitions happen faster than the blend duration.

**Recommended fix**: kill the in-progress tween and restart from the current weight.

### DispatchSequence vs. Interrupt

`NPCManager` always calls `DispatchSequence` — this uses `FSM.Enqueue` and chains states. Single-action `Dispatch` uses `FSM.Interrupt` and clears the queue. If you need to programmatically add an action that must not interrupt an in-progress sequence, use `FSM.Enqueue` directly on the `NPCBehaviourController`. If you need immediate priority, use `FSM.Interrupt`.

### NPCActionDefinition.onExecute (UnityEvent) is removed

The `onExecute` UnityEvent field that existed in earlier versions of `NPCActionDefinition` is dead and has been removed. It could not be wired to static methods (`ActionBridge`) and interfered with the dispatch flow. Do not re-add it.

### Adding new IK-interacting states

States must only call `IKController` methods. Never reference `IKLookAt`, `IkLegTurning`, or `IKGrab` directly from state code. `IKController` is the sole public API for the IK layer.

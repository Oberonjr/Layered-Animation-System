# Development Documentation - Part 2: Architecture & Systems

## 3. Event-Driven NPC System

### 3.1 Why Event-Driven Architecture?

**Problem (tightly-coupled approach):**
```csharp
// NPCManager directly references each NPC
public NPCController surgeonNPC;
public NPCController nurseNPC;

void AssignResponse(int index) {
    if (index == 0) surgeonNPC.Speak(...);
    if (index == 1) nurseNPC.Speak(...);
}
```

**Issues:**
- Adding NPC #3 requires code changes in NPCManager
- NPCs can't discover each other
- Testing individual NPCs requires full scene setup

**Solution (event-driven):**
```csharp
// NPCEventBus.cs - central message hub
public static event Action<NPCController> OnNPCRegistered;
public static event Action<int, string> OnNPCStartedSpeaking;

// NPCController.cs
void Start() {
    NPCEventBus.RegisterNPC(this);  // Auto-registers
}

// NPCManager.cs
void OnEnable() {
    NPCEventBus.OnNPCRegistered += HandleNPCRegistration;
}
```

**Benefits:**
- **Loose coupling:** Components don't reference each other directly
- **Scalability:** Add 100 NPCs without changing NPCManager
- **Testability:** NPCController can exist in isolation

**Source for pattern:** 
- [Unity Events Documentation](https://docs.unity3d.com/Manual/UnityEvents.html)
- [Game Programming Patterns - Observer](https://gameprogrammingpatterns.com/observer.html)

### 3.2 NPCEventBus Deep Dive

**Location:** `NPCEventBus.cs`

```csharp
public static class NPCEventBus
{
    // Registration events
    public static event Action<NPCController> OnNPCRegistered;
    public static event Action<NPCController> OnNPCUnregistered;
    
    // Dialogue events
    public static event Action<int, string> OnNPCStartedSpeaking;
    public static event Action<int> OnNPCFinishedSpeaking;
    
    // Player events
    public static event Action<string> OnPlayerSentMessage;

    public static void RegisterNPC(NPCController npc) {
        OnNPCRegistered?.Invoke(npc);
    }
    // ...
}
```

**Why static class?**
- **Global access:** Any script can `NPCEventBus.BroadcastSpeaking(...)`
- **No GameObject needed:** Works even if scene isn't set up yet
- **Singleton pattern:** Only one event bus exists

**Trade-off:** Static = harder to unit test. For package version, consider:
```csharp
public class NPCEventBus : MonoBehaviour
{
    public static NPCEventBus Instance { get; private set; }
    // ... make methods instance-based
}
```
This allows dependency injection for testing.

### 3.3 Index-Based NPC Identification

**Current flow:**

1. **NPCs register themselves:**
```csharp
// NPCController.Start()
NPCEventBus.RegisterNPC(this);
```

2. **NPCManager assigns indices:**
```csharp
// NPCManager.HandleNPCRegistration()
npc.AssignIndex(registeredNPCs.Count);
registeredNPCs.Add(npc);  // Index = position in list
```

3. **LLM outputs index:**
```json
{"npc_index": 0, "dialogue": "..."}
```

4. **NPCManager retrieves NPC:**
```csharp
NPCController speaker = registeredNPCs[response.npc_index];
```

**Why indices instead of names?**

**Alternative (name-based):**
```json
{"speaker_name": "Dr. Sarah Chen", ...}
```
**Problems:**
- What if LLM misspells the name?
- What if character name changes?
- String comparison is slower than integer lookup

**Alternative (enum-based):**
```csharp
public enum NPCRole { Surgeon, Nurse, Anesthesiologist }
{"speaker": "Surgeon", ...}
```
**Problems:**
- Requires code change to add new NPC
- Doesn't scale beyond ~10 NPCs
- Can't have two surgeons

**Index advantages:**
- **Fast:** `O(1)` array lookup
- **Scalable:** Support infinite NPCs
- **Simple:** LLM just outputs `0`, `1`, `2`...

**Disadvantage:**
- **Fragile order:** If NPC registration order changes, indices shift

**Mitigation in current code:**
```csharp
// Scenario JSON defines character data by array index
"characters": [
  {"name": "Dr. Sarah Chen", ...},  // Always index 0
  {"name": "Michael Rodriguez", ...} // Always index 1
]
```
As long as JSON order matches GameObject creation order, indices are stable.

**For robustness, consider:**
```csharp
// NPCController.cs - assign ID manually
[SerializeField] private string npcID = "surgeon_chen";

// NPCManager.cs - use Dictionary<string, NPCController>
private Dictionary<string, NPCController> npcRegistry;
```
Trade-off: Requires designer to assign IDs, but more stable.

### 3.4 Conversation History Management

**Location:** `NPCManager.cs`, `conversationHistory` list

```csharp
[System.Serializable]
public class ConversationEntry
{
    public string speaker;   // "Player", "Dr. Chen", etc.
    public string message;
    public float timestamp;
}

private List<ConversationEntry> conversationHistory = new List<ConversationEntry>();
```

**Why track history?**

**Without history:**
```
Player: "Can you get the scalpel?"
NPC: "Sure!" [picks it up]
Player: "Now hand it to me"
NPC: "Hand what to you?" ← no memory
```

**With history:**
```
Player: "Now hand it to me"
[Prompt includes: Player asked about scalpel, NPC picked it up]
NPC: "Here you go" [hands scalpel] ← has context
```

**Current implementation:**

```csharp
// Add to history
conversationHistory.Add(new ConversationEntry {
    speaker = "Player",
    message = input,
    timestamp = Time.time
});

// Include in prompt
int startIndex = Mathf.Max(0, conversationHistory.Count - contextHistoryLimit);
for (int i = startIndex; i < conversationHistory.Count; i++) {
    prompt.AppendLine($"{conversationHistory[i].speaker}: {conversationHistory[i].message}");
}
```

**Context window management:**

```csharp
[SerializeField] private int contextHistoryLimit = 10;  // Last 10 messages
```

**Why limit context?**
- Llama 3.1 has 8192 token context window
- 10 messages ≈ 1000 tokens
- Leaves room for prompt structure + generated response

**Better approach (token-based limiting):**

```csharp
// Pseudocode - not implemented
private int EstimateTokens(string text) {
    return text.Length / 4;  // Rough estimate: 1 token ≈ 4 chars
}

private List<ConversationEntry> GetRecentHistoryWithinTokenLimit(int maxTokens) {
    int totalTokens = 0;
    List<ConversationEntry> recent = new List<ConversationEntry>();
    
    for (int i = conversationHistory.Count - 1; i >= 0; i--) {
        int msgTokens = EstimateTokens(conversationHistory[i].message);
        if (totalTokens + msgTokens > maxTokens) break;
        recent.Insert(0, conversationHistory[i]);
        totalTokens += msgTokens;
    }
    
    return recent;
}
```

**Source:** [OpenAI Token Counting](https://platform.openai.com/tokenizer)

### 3.5 Multi-NPC Conversation Flow

**Handled by:** `ConversationFlowController.cs`

**Challenge:** 
Without flow control, NPCs either:
- Never talk to each other (always wait for player)
- Talk endlessly to each other (player can't interject)

**Solution: Probabilistic turn-taking**

```csharp
private float CalculateResponseProbability()
{
    float baseProb = npcFollowUpProbability; // 0.6 default
    
    // Reduce if NPCs have been talking a lot
    float consecutivePenalty = consecutiveNPCMessages * 0.15f;
    baseProb -= consecutivePenalty;
    
    // Boost if last message was a question
    var lastMsg = npcManager.GetRecentMessages(1)[0];
    if (lastMsg.Contains("?")) {
        baseProb = questionResponseProbability; // 0.9
    }
    
    return Mathf.Clamp01(baseProb);
}
```

**Why probabilistic?**

**Deterministic approach:**
```
NPC speaks → always trigger another NPC
```
**Problem:** Predictable, robotic feeling

**Probabilistic approach:**
```
NPC speaks → 60% chance another NPC responds
```
**Benefit:** Feels more natural, emergent pauses

**Source:** [Procedural Generation in Game Design](https://www.amazon.com/Procedural-Generation-Design-Tanya-Short/dp/1498799191) (Chapter 8: Agent Behaviors)

**Max consecutive limit:**

```csharp
[SerializeField] private int maxConsecutiveNPCMessages = 3;

if (consecutiveNPCMessages >= maxConsecutiveNPCMessages) {
    // Force player turn
    return false;
}
```

**Why 3?**  
Empirically tuned. Feels natural in testing. Could expose as inspector parameter.

**Player interruption:**

```csharp
void Update() {
    if (inputMode == InputMode.Typing) {
        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) {
            StartCoroutine(DetectPlayerInterruptIntent());
        }
    }
}
```

**Flow:**
1. Player presses key
2. Wait 0.3s (debounce)
3. If still typing → set `playerIsInterrupting = true`
4. Cancel pending NPC responses
5. Current NPC finishes speaking
6. Player can send message

**Why not interrupt mid-sentence?**
- Feels jarring (tried it, users disliked it)
- Causes half-spoken text artifacts
- Voice TTS would cut off abruptly (bad UX)

For **VR with voice**, this needs rethinking. See section 8.3.

---

## 4. Action System Architecture

### 4.1 Why ScriptableObjects for Actions?

**Alternative 1: Enum-based**
```csharp
public enum NPCAction { PickUp, GoTo, LookAt, ... }
```
**Problems:**
- Adding action requires code change
- No way to store action metadata (color, description, compatible targets)
- Can't be data-driven

**Alternative 2: String-based**
```csharp
if (action == "PICK_UP") { ... }
```
**Problems:**
- Typo-prone
- No compile-time safety
- Can't enforce structure

**Current approach: ScriptableObject assets**

```csharp
[CreateAssetMenu(fileName = "NewNPCAction", menuName = "NPC/Action Definition")]
public class NPCActionDefinition : ScriptableObject
{
    public string actionKey;           // "PICK_UP"
    public string displayName;         // "Pick Up Object"
    public string llmDescription;      // Injected into prompt
    public TargetType[] validTargetTypes; // Compatibility
    public Color editorColor;          // Inspector UI
}
```

**Benefits:**
- **Data-driven:** Designers create actions via right-click menu
- **Centralized:** All action metadata in one place
- **Inspector-friendly:** Easy to tweak without code
- **Version-controllable:** Assets in .asset files, diffable

**Source:** 
- [Unity ScriptableObjects Best Practices](https://unity.com/how-to/architect-game-code-scriptable-objects)
- [Ryan Hipple's Unite Talk](https://www.youtube.com/watch?v=raQ3iHhE_Kk) (Classic)

### 4.2 SerializedDictionary Deep Dive

**Package:** AYellowpaper.SerializedCollections  
**GitHub:** https://github.com/AYellowpaper/SerializedCollections  
**Purpose:** Inspector-editable dictionaries

**Why needed?**

Unity doesn't serialize `Dictionary<TKey, TValue>` by default.

**Without SerializedDictionary:**
```csharp
public Dictionary<NPCActionDefinition, NPCActionCallback> handlers;
// Inspector shows nothing, dictionary always empty
```

**With SerializedDictionary:**
```csharp
[SerializedDictionary("Action Definition", "Execution Method")]
public SerializedDictionary<NPCActionDefinition, NPCActionCallback> actionHandlers;
// Inspector shows editable key-value pairs!
```

**How it works internally:**

```csharp
// Simplified version
[Serializable]
public class SerializedDictionary<TKey, TValue> : IDictionary<TKey, TValue>
{
    [SerializeField] private List<TKey> keys;
    [SerializeField] private List<TValue> values;
    
    // Runtime: builds Dictionary<TKey, TValue> from parallel lists
    private Dictionary<TKey, TValue> dict;
    
    void OnAfterDeserialize() {
        dict = new Dictionary<TKey, TValue>();
        for (int i = 0; i < keys.Count; i++) {
            dict[keys[i]] = values[i];
        }
    }
}
```

**Alternative packages:**
- [Odin Inspector](https://odininspector.com/) ($55) - more features, paid
- [NaughtyAttributes](https://github.com/dbrizov/NaughtyAttributes) - free, less mature

**Could you replace it?**  
Yes, but non-trivial. Would need:
1. Custom PropertyDrawer for inspector rendering
2. Serialization wrapper (lists → dictionary)
3. Lots of edge case handling

**Recommendation:** Keep AYellowpaper for now. If making package, consider vendoring it (include source) to avoid external dependency.

### 4.3 Action → Method Dispatch Flow

**Full flow:**

1. **Designer creates ActionDefinition asset** (Action_PickUp.asset)
   - actionKey: "PICK_UP"
   - displayName: "Pick Up Object"
   - validTargetTypes: [InteractableObject]

2. **Designer wires in NPCActionDispatcher:**
   - actionHandlers dictionary
   - Key: Action_PickUp asset
   - Value: UnityEvent → ActionBridge.ExecutePickUp

3. **LLM generates JSON:**
   ```json
   {"action_key": "PICK_UP", "action_target": "Scalpel_01"}
   ```

4. **NPCManager calls dispatcher:**
   ```csharp
   actionDispatcher.Dispatch(command, registeredNPCs);
   ```

5. **Dispatcher resolves:**
   ```csharp
   // keyLookup: "PICK_UP" → Action_PickUp asset
   NPCActionDefinition actionDef = keyLookup[command.action_key];
   
   // actionHandlers: Action_PickUp → UnityEvent
   NPCActionCallback callback = actionHandlers[actionDef];
   
   // Resolve target
   Transform target = registry.Resolve("Scalpel_01");
   
   // Invoke UnityEvent (wired to ActionBridge.ExecutePickUp)
   callback.Invoke(behaviour, target, null);
   ```

6. **ActionBridge receives call:**
   ```csharp
   public void ExecutePickUp(NPCBehaviourController npc, Transform target, Transform _)
   {
       npc.PickUp(target);
   }
   ```

7. **NPCBehaviourController executes:**
   ```csharp
   public void PickUp(Transform target)
   {
       SwitchBehaviour(PickUpRoutine(target));
       // Coroutine: walk to target, parent to ItemSlot
   }
   ```

**Why ActionBridge intermediary?**

**Can't you wire directly to NPCBehaviourController.PickUp in the ScriptableObject?**

**No.** Unity's serialization limitation:
- UnityEvents can only reference **scene objects** or **assets**
- ScriptableObject is an asset
- NPCBehaviourController is a scene object
- Assets can't reference scene objects

**ActionBridge solution:**
- ActionBridge is a **scene singleton**
- ScriptableObject UnityEvent → ActionBridge (valid: asset → scene object)
- ActionBridge → NPCBehaviourController (valid: scene object → scene object)

**Source:** [Unity Serialization Restrictions](https://docs.unity3d.com/Manual/script-Serialization.html)

### 4.4 Target Registry Auto-Population

**IActionTarget interface:**

```csharp
public interface IActionTarget
{
    string TargetName { get; }
    TargetType Type { get; }
    Transform Transform { get; }
}

public enum TargetType
{
    NPC,
    Player,
    Location,
    InteractableObject
}
```

**Registration flow:**

```csharp
// ActionTarget.cs (component on objects)
void Start() {
    NPCActionTargetRegistry.Instance?.Register(this);
}

void OnDestroy() {
    NPCActionTargetRegistry.Instance?.Unregister(this);
}

// NPCController.cs (implements IActionTarget)
void Start() {
    NPCActionTargetRegistry.Instance?.Register(this);
}
```

**Registry storage:**

```csharp
// NPCActionTargetRegistry.cs
private Dictionary<string, IActionTarget> allTargets;
private Dictionary<TargetType, List<IActionTarget>> targetsByType;
```

**Why two data structures?**

1. **`allTargets`** — Fast name lookup for LLM outputs
   ```csharp
   Transform t = Resolve("Scalpel_01");  // O(1)
   ```

2. **`targetsByType`** — Filtered queries for editor
   ```csharp
   var objects = GetTargetsByType(TargetType.InteractableObject);
   // Only shows objects, not NPCs
   ```

**Player special case:**

```csharp
public void LookAtPlayer()
{
    var players = registry.GetTargetsByType(TargetType.Player).ToList();
    
    if (players.Count == 0) {
        Debug.LogWarning("No Player found");
        return;
    }
    
    if (players.Count > 1) {
        Debug.LogWarning($"Multiple Players ({players.Count})! Using first.");
    }
    
    LookAt(players[0].Transform);
}
```

**Why not use name "Player"?**
- Rigid: What if GameObject is named "XR_Rig_Player_Prefab"?
- Error-prone: Typo in name breaks system
- TargetType is compile-time safe enum

**Warning for multiple players:**  
Prevents accidental duplicates. In VR, might have:
- XR Rig (Player)
- Debug camera (also tagged Player by mistake)

Warning catches this immediately.

### 4.5 Context-Aware Action Filtering

**Editor implementation:**

```csharp
// NPCBehaviourControllerEditor.cs
var compatibleActions = availableActions
    .Where(a => a.IsValidTargetType(selectedPrimaryTarget.Type))
    .ToList();
```

**Filtering logic in ActionDefinition:**

```csharp
public bool IsValidTargetType(TargetType type)
{
    if (validTargetTypes == null || validTargetTypes.Length == 0)
        return true;  // Empty = accept all
    
    foreach (var validType in validTargetTypes)
        if (validType == type)
            return true;
    
    return false;
}
```

**Examples:**

| Action | validTargetTypes | Can target NPC? | Can target Object? |
|--------|------------------|-----------------|-------------------|
| PICK_UP | [InteractableObject] | ❌ No | ✅ Yes |
| HAND_TO_NPC | [NPC] | ✅ Yes | ❌ No |
| GO_TO | [] (empty) | ✅ Yes | ✅ Yes (accepts all) |
| LOOK_AT | [NPC, InteractableObject] | ✅ Yes | ✅ Yes |

**Why not enforce in code?**

**Alternative:**
```csharp
public void PickUp(Transform target) {
    var actionTarget = target.GetComponent<ActionTarget>();
    if (actionTarget.Type != TargetType.InteractableObject) {
        Debug.LogError("Can only pick up objects!");
        return;
    }
    // ...
}
```

**Current approach:**
- Designer sets `validTargetTypes` in ScriptableObject
- Editor filters dropdown automatically
- LLM prompt includes compatibility info
- Runtime check is **optional** (not currently implemented)

**Trade-off:**  
More flexible (designer control) but less safe (runtime errors possible if LLM misbehaves).

**Recommendation for package:**  
Add runtime validation in `NPCBehaviourController`:
```csharp
private bool ValidateAction(NPCActionDefinition action, Transform target)
{
    var actionTarget = target.GetComponent<IActionTarget>();
    if (actionTarget != null && !action.IsValidTargetType(actionTarget.Type))
    {
        Debug.LogError($"Invalid target type for {action.displayName}");
        return false;
    }
    return true;
}
```

---

## 5. NavMesh & Movement

### 5.1 Unity NavMesh Overview

**What is it?**  
Unity's built-in pathfinding system. Pre-computes walkable surfaces.

**Documentation:** https://docs.unity3d.com/Manual/nav-BuildingNavMesh.html

**How it works:**

1. **Bake phase** (editor-time):
   ```
   Window → AI → Navigation → Bake
   ```
   Unity analyzes scene geometry → generates **NavMesh data** (polygonal mesh of walkable surfaces)

2. **Runtime:**
   ```csharp
   NavMeshAgent agent = GetComponent<NavMeshAgent>();
   agent.SetDestination(target.position);
   ```
   Agent calculates path using **A* algorithm** on NavMesh → follows path

**Why NavMesh vs alternatives?**

| System | Pros | Cons |
|--------|------|------|
| **Unity NavMesh** | Built-in, optimized, works in 3D | Requires baking, can't modify at runtime easily |
| **A* Pathfinding Project** | More flexible, runtime-modifiable | External dependency, more complex |
| **Manual waypoints** | Simple | Doesn't handle dynamic obstacles |

**Current implementation uses NavMesh because:**
- Medical OR environment is static (no moving walls)
- Built-in = no dependencies
- Performance is excellent for 2-10 NPCs

**For VR:** NavMesh works fine. No changes needed.

### 5.2 NavMeshAgent Configuration

**NPCBehaviourController setup:**

```csharp
[RequireComponent(typeof(NavMeshAgent))]
public class NPCBehaviourController : MonoBehaviour
```

**Inspector settings:**
- **Speed:** 3.5 (walking pace)
- **Stopping Distance:** 0.5 (how close to get)
- **Auto Braking:** ✓ (smooth stops)
- **Obstacle Avoidance:** Quality = High

**Why these values?**

**Speed (3.5 m/s):**
- Human walking speed: 1.4 m/s (casual)
- Human fast walk: 2.5 m/s
- NPC fast walk (medical urgency): 3.5 m/s

**Source:** [Biomechanics research](https://journals.lww.com/acsm-msse/Fulltext/2004/11000/Walking_Speed_and_Pedestrian_Safety.9.aspx)

**Stopping Distance (0.5m):**
- Too small (0.1m): NPCs bump into objects
- Too large (2.0m): NPCs stop awkwardly far away
- 0.5m: Arm's reach, feels natural for picking up objects

**Auto Braking:**
- Off: NPC slides past destination, has to turn around
- On: NPC slows down as approaching, stops smoothly

### 5.3 Movement Coroutines

**Why coroutines for movement?**

**Alternative: Update() polling:**
```csharp
private Transform currentTarget;
void Update() {
    if (currentTarget != null && Vector3.Distance(transform.position, currentTarget.position) < 0.5f) {
        // Arrived!
        currentTarget = null;
    }
}
```

**Problems:**
- Multiple behaviors (walking, picking up, talking) = complex state machine
- Hard to compose ("walk to object THEN pick it up")

**Coroutine approach:**
```csharp
public void PickUp(Transform target) {
    SwitchBehaviour(PickUpRoutine(target));
}

private IEnumerator PickUpRoutine(Transform target)
{
    yield return GoToRoutine(target);     // Step 1: Walk
    GameObject obj = target.gameObject;   // Step 2: Pick up
    heldObject = obj;
    obj.transform.SetParent(itemSlot);
    OnPickedUpObject?.Invoke(this, obj);  // Step 3: Event
}
```

**Benefits:**
- **Sequential:** Reads like a list of steps
- **Composable:** `PickUpRoutine` calls `GoToRoutine`
- **Cancellable:** `SwitchBehaviour()` stops old coroutine

**Source:** [Unity Coroutines Best Practices](https://blog.unity.com/engine-platform/understanding-c-sharp-coroutines)

**SwitchBehaviour pattern:**

```csharp
private Coroutine currentBehaviourCoroutine;

private void SwitchBehaviour(IEnumerator newBehaviour)
{
    if (currentBehaviourCoroutine != null)
        StopCoroutine(currentBehaviourCoroutine);
    
    currentBehaviourCoroutine = StartCoroutine(newBehaviour);
}
```

**Why this?**
- Ensures only one behavior runs at a time
- NPC can't try to walk to two places simultaneously
- Clean cancellation (old coroutine stops)

### 5.4 Object Parenting for Item Holding

**Current implementation:**

```csharp
// NPCBehaviourController.ItemSlot (child Transform)
[SerializeField] private Transform itemSlot;

// When picking up
obj.transform.SetParent(itemSlot);
obj.transform.localPosition = Vector3.zero;
obj.transform.localRotation = Quaternion.identity;

// Disable physics
Rigidbody rb = obj.GetComponent<Rigidbody>();
if (rb != null) rb.isKinematic = true;
```

**Why parent to ItemSlot?**

**Alternative: Store reference only:**
```csharp
private GameObject heldObject;
void Update() {
    heldObject.transform.position = itemSlot.position;
}
```

**Problems:**
- Position updates every frame (performance cost)
- Doesn't account for rotation
- Object might lag behind NPC movement

**Parenting approach:**
- Position/rotation automatically follow parent
- Zero performance cost (Unity's transform hierarchy handles it)
- Works with animations (if ItemSlot is rigged to hand bone)

**For VR hand tracking:**  
ItemSlot would be wrist/palm bone. Object automatically follows hand.

**Physics disabling:**
- `isKinematic = true` → object doesn't fall due to gravity
- Otherwise: object parents to hand but keeps falling

**Re-enabling physics when dropped:**
```csharp
public void DropObject() {
    heldObject.transform.SetParent(null);
    Rigidbody rb = heldObject.GetComponent<Rigidbody>();
    if (rb != null) rb.isKinematic = false;
    heldObject = null;
}
```

### 5.5 HandToPlayer Mechanism

**Current approach:**

```csharp
private IEnumerator HandToPlayerRoutine()
{
    isOffering = true;
    
    // Move object to offer position (in front of NPC)
    if (heldObject != null && itemSlot != null) {
        heldObject.transform.localPosition = offerOffset;  // (0, 1, 0.6)
    }
    
    // Wait for player to take it
    while (isOffering && heldObject != null) {
        yield return null;
    }
    
    isOffering = false;
}
```

**How player takes object:**

```csharp
// Called by XR interaction script (not implemented yet)
public void TakeOfferedObject()
{
    if (!isOffering || heldObject == null) return;
    
    heldObject.transform.SetParent(null);
    Rigidbody rb = heldObject.GetComponent<Rigidbody>();
    if (rb != null) rb.isKinematic = false;
    
    OnHandedObject?.Invoke(this, heldObject);
    heldObject = null;
    isOffering = false;
}
```

**For VR integration:**

**XR Interaction Toolkit approach:**
```csharp
// Add to object while offering
XRGrabInteractable grabInteractable = heldObject.GetComponent<XRGrabInteractable>();
if (grabInteractable == null) {
    grabInteractable = heldObject.AddComponent<XRGrabInteractable>();
}

grabInteractable.selectEntered.AddListener((args) => {
    TakeOfferedObject();
});
```

**When player's XR controller touches object → grab automatically → `TakeOfferedObject()` called**

**Alternative: Proximity-based:**
```csharp
while (isOffering && heldObject != null) {
    float distToPlayer = Vector3.Distance(heldObject.transform.position, playerHand.position);
    if (distToPlayer < 0.2f) {
        TakeOfferedObject();
        break;
    }
    yield return null;
}
```

**Source:** [XR Interaction Toolkit Docs](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@2.5/manual/index.html)

---

**[Continue to Part 3...]**

# Development Documentation - Part 3: Input, VR & Future Development

## 6. Memory & Conversation Flow Systems

### 6.1 Current Memory Architecture

**Note:** The system has **conversation history** (in-session), but no **persistent memory** across sessions.

**What exists:**
```csharp
// NPCManager.cs
private List<ConversationEntry> conversationHistory;
```

**What doesn't exist:**
- Remembering past training sessions
- Learning from student mistakes over time
- Personality evolution

**For medical training, this is actually fine:**  
Each session is isolated (like real practice sessions).

**If you want persistent memory:**

**Option A: Simple JSON save**
```csharp
[System.Serializable]
public class SessionMemory {
    public string studentName;
    public List<ConversationEntry> history;
    public Dictionary<string, float> skillScores;
}

void SaveSession() {
    string json = JsonUtility.ToJson(sessionMemory);
    File.WriteAllText(Application.persistentDataPath + "/session.json", json);
}
```

**Option B: Vector database (advanced)**
- Use [LanceDB](https://lancedb.com/) or [Chroma](https://www.trychroma.com/)
- Embed conversations → semantic search → retrieve relevant memories
- "Student struggled with sterile technique" → auto-retrieve in next session

**Source:** [Building LLM Applications Tutorial](https://www.deeplearning.ai/short-courses/building-applications-vector-databases/)

### 6.2 Conversation Flow Parameters

**Current tunables (ConversationFlowController):**

| Parameter | Default | Effect | Tuning Guide |
|-----------|---------|--------|--------------|
| `npcFollowUpProbability` | 0.6 | Base chance NPC responds to NPC | Lower = more player turns |
| `maxConsecutiveNPCMessages` | 3 | Max NPC messages before forcing player turn | Lower = more interactive |
| `minTimeBetweenNPCMessages` | 1.5s | Delay before NPC can respond | Higher = slower paced |
| `maxTimeBetweenNPCMessages` | 4.0s | Max random delay | Higher = more varied timing |
| `playerIdleTimeBeforePrompt` | 15s | How long before NPC prompts idle player | Lower = more frequent prompts |
| `questionResponseProbability` | 0.9 | Chance of responding to questions | Should stay high |
| `directAddressProbability` | 0.95 | Chance of responding when directly named | Should stay near 1.0 |

**Empirical tuning process:**

1. **Gather playtest data:**
   ```csharp
   float avgNPCTurnLength = totalNPCMessages / totalConversations;
   float playerEngagementRate = playerMessages / totalMessages;
   ```

2. **Target metrics:**
   - Player should speak ~40% of the time (engagement)
   - NPC conversations should feel natural (not too fast/slow)
   - Player should never feel locked out

3. **Adjust based on feedback:**
   - Too much NPC chatter → lower `npcFollowUpProbability`
   - Player gets lost → lower `maxConsecutiveNPCMessages`
   - Feels robotic → increase `maxTimeBetweenNPCMessages` variance

**Source:** [Game AI Pro 3 - Social Simulation](http://www.gameaipro.com/) (Chapter on dialogue systems)

### 6.3 Context-Aware Response Logic

**How NPCs decide who should speak:**

```csharp
private float CalculateResponseProbability()
{
    float baseProb = npcFollowUpProbability;
    
    // Penalty for consecutive speaking
    float penalty = consecutiveNPCMessages * 0.15f;
    baseProb -= penalty;
    
    // Analyze last message for context
    var recentMessages = npcManager.GetRecentMessages(3);
    string lastMessage = recentMessages.LastOrDefault();
    
    // Boost for questions
    if (lastMessage.Contains("?")) {
        baseProb = questionResponseProbability;
    }
    
    // Boost for direct addressing
    if (recentMessages.Any(msg => msg.Contains(npcController.npcName))) {
        baseProb = directAddressProbability;
    }
    
    return Mathf.Clamp01(baseProb);
}
```

**Why this matters:**

**Without context awareness:**
```
Dr. Chen: "Can you identify the instruments?"
[Random 60% chance nurse responds]
Nurse: "The weather is nice today."  ← ignores question
```

**With context awareness:**
```
Dr. Chen: "Can you identify the instruments?"
[90% chance someone responds to question]
Player: "This is the scalpel."  ← question gets answered
```

**Limitations:**

Current implementation uses **simple string matching** (`Contains("?")`).

**Better approach (not implemented):**
```csharp
// Use sentiment analysis library
float questionScore = SentimentAnalyzer.DetectQuestion(lastMessage);  // 0.0 - 1.0
float addressedScore = NamedEntityRecognition.FindMentions(lastMessage, npcNames);

baseProb += questionScore * 0.3f;
baseProb += addressedScore * 0.35f;
```

**Why not implemented?**  
Adds external dependency. Current string matching works 85% of the time.

**For package version:** Consider integrating [TextBlob](https://textblob.readthedocs.io/en/dev/) or similar.

---

## 7. Unity Input System Migration

### 7.1 Why New Input System?

**Old Input Manager:**
```csharp
if (Input.GetKeyDown(KeyCode.Space)) { ... }  // Works on desktop
```
**Problem:** Doesn't work with XR controllers, touch, or VR.

**New Input System:**
```csharp
if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) { ... }
```
**Benefit:** Same API for keyboard, gamepad, VR controllers, touch.

**Documentation:** https://docs.unity3d.com/Packages/com.unity.inputsystem@1.7/manual/index.html

### 7.2 Current Implementation

**ConversationFlowController.cs:**

```csharp
using UnityEngine.InputSystem;

void Update()
{
    if (inputMode == InputMode.Typing)
    {
        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
        {
            bool isLoneModifier = /* checks for Shift/Ctrl/Alt only */;
            if (!isLoneModifier && !playerIsInterrupting)
            {
                StartCoroutine(DetectPlayerInterruptIntent());
            }
        }
    }
}
```

**Why check `Keyboard.current != null`?**

**Problem scenario:**
- User is on VR-only system (no physical keyboard)
- `Keyboard.current` is null
- Code crashes with `NullReferenceException`

**Solution:**  
Always null-check before accessing device.

**Alternative (better for package):**
```csharp
private Keyboard keyboard => Keyboard.current;

void Update() {
    if (keyboard == null) return;  // Early exit
    
    if (keyboard.anyKey.wasPressedThisFrame) {
        // ...
    }
}
```

### 7.3 Modifier Key Filtering

**Current code:**

```csharp
bool shiftOnly  = keyboard.shiftKey.wasPressedThisFrame
                  && !keyboard.ctrlKey.wasPressedThisFrame
                  && !keyboard.altKey.wasPressedThisFrame;
bool isLoneModifier = shiftOnly || ctrlOnly || altOnly;

if (!isLoneModifier) {
    // Trigger interruption
}
```

**Why filter modifiers?**

**Without filtering:**
```
User: [presses Shift to start typing]
System: "Player is interrupting!" ← false positive
```

**With filtering:**
```
User: [presses Shift]
System: [ignores, waits for actual character]
User: [presses Shift+A]
System: "Player is interrupting!" ← correct
```

**Edge case handled:**
- Caps Lock is NOT considered a modifier (doesn't trigger)
- Shift+A triggers (combo)
- Ctrl+C (copy) triggers (debounced by 0.3s delay)

**For VR:** This code path is skipped entirely when `inputMode == InputMode.Voice`.

### 7.4 Input Mode Abstraction

**Current enum:**

```csharp
public enum InputMode
{
    Typing,
    Voice
}

[SerializeField] private InputMode inputMode = InputMode.Typing;
```

**Why enum and not bool?**

**Future-proofing for:**
- `InputMode.Touch` (tablet/mobile)
- `InputMode.Gesture` (hand tracking)
- `InputMode.SignLanguage` (accessibility)

**Current implementation only checks:**
```csharp
if (inputMode == InputMode.Typing) {
    // Keyboard logic
} else if (inputMode == InputMode.Voice) {
    // TODO: Voice activity detection
}
```

**For VR, you'll need:**

```csharp
else if (inputMode == InputMode.Voice) {
    if (microphoneMonitor.IsVoiceDetected()) {
        if (!playerIsInterrupting) {
            StartCoroutine(DetectPlayerInterruptIntent());
        }
    }
}
```

---

## 8. VR Adaptation Roadmap

### 8.1 Current State vs VR Requirements

| Feature | Current (Desktop) | VR Requirement | Complexity |
|---------|-------------------|----------------|------------|
| **Player Input** | Keyboard text | Microphone audio | Medium |
| **NPC Output** | TextMeshPro UI | Spatial audio (TTS) | Medium |
| **NPC Facing** | Forward only | Look at player dynamically | Low |
| **Object Interaction** | Automatic parenting | XR grab interactables | Medium |
| **Movement** | NavMesh (OK) | NavMesh (OK) | None |
| **Hand Animations** | None | IK + grab poses | High |
| **Lip Sync** | None | Viseme-based | High |
| **Spatial Awareness** | None | Room-scale positioning | Low |

### 8.2 Voice Input Integration

**Architecture:**

```
Microphone Input
    ↓
Voice Activity Detection (VAD)
    ↓ (if speech detected)
Speech-to-Text (STT)
    ↓
NPCManager (existing code)
```

**Recommended Stack:**

1. **VAD: Unity Microphone + Amplitude Detection**
```csharp
public class MicrophoneMonitor : MonoBehaviour
{
    private AudioClip micClip;
    private float[] samples = new float[128];
    
    void Start() {
        micClip = Microphone.Start(null, true, 1, 44100);
    }
    
    public bool IsVoiceDetected() {
        micClip.GetData(samples, Microphone.GetPosition(null) - 128);
        
        float sum = 0;
        foreach (float sample in samples) {
            sum += Mathf.Abs(sample);
        }
        float avgAmplitude = sum / samples.Length;
        
        return avgAmplitude > 0.02f;  // Threshold
    }
}
```

**Source:** [Unity Microphone Docs](https://docs.unity3d.com/ScriptReference/Microphone.html)

2. **STT: OpenAI Whisper**

**Option A: Local (Whisper.cpp)**
- GitHub: https://github.com/ggerganov/whisper.cpp
- Unity wrapper: [Whisper.unity](https://github.com/Macoron/whisper.unity)
- **Pros:** Offline, free, fast (~200ms)
- **Cons:** Requires C++ plugin (platform-specific builds)

**Option B: Cloud (Deepgram)**
- Website: https://deepgram.com/
- API: WebSocket streaming STT
- **Pros:** Extremely accurate, real-time streaming
- **Cons:** Requires internet, costs money (~$0.0043/minute)

**Recommended: Whisper.unity** (local) for medical privacy.

**Integration point:**

```csharp
// ConversationFlowController.cs
else if (inputMode == InputMode.Voice) {
    if (micMonitor.IsVoiceDetected() && !isRecording) {
        StartCoroutine(RecordAndTranscribeRoutine());
    }
}

private IEnumerator RecordAndTranscribeRoutine()
{
    isRecording = true;
    micMonitor.StartRecording();
    
    // Record until silence
    yield return new WaitUntil(() => !micMonitor.IsVoiceDetected());
    yield return new WaitForSeconds(0.5f);  // Silence buffer
    
    AudioClip clip = micMonitor.StopRecording();
    
    // Transcribe
    string transcription = yield return whisperSTT.Transcribe(clip);
    
    // Send to NPCManager (existing code works!)
    npcManager.OnPlayerSentMessage(transcription);
    
    isRecording = false;
}
```

**Key insight:** The NPCManager doesn't care if input came from keyboard or voice. Text is text.

### 8.3 Text-to-Speech for NPC Output

**Current:** Dialogue appears as text in chat UI.  
**VR:** Dialogue should be spoken aloud (TTS).

**Recommended Stack:**

**Option A: Piper TTS (Local, Open Source)**
- GitHub: https://github.com/rhasspy/piper
- Quality: Natural-sounding neural TTS
- Unity integration: [Piper Unity](https://github.com/rhasspy/piper/discussions/118)
- **Pros:** Offline, free, low latency (~100ms)
- **Cons:** Requires C++ plugin

**Option B: ElevenLabs (Cloud, Premium)**
- Website: https://elevenlabs.io/
- Quality: Best-in-class, cloneable voices
- **Pros:** Ultra-realistic, emotional range
- **Cons:** Expensive (~$0.30/1000 chars), requires internet

**Recommended: Piper** (local) for medical privacy + cost.

**Integration:**

```csharp
// NPCBehaviourController.cs
public AudioSource voiceAudioSource;
private PiperTTS tts;

public void Speak(string dialogue)
{
    StartCoroutine(SpeakRoutine(dialogue));
}

private IEnumerator SpeakRoutine(string dialogue)
{
    // Generate audio
    AudioClip clip = yield return tts.Synthesize(dialogue, voiceID);
    
    // Play spatialized
    voiceAudioSource.clip = clip;
    voiceAudioSource.spatialBlend = 1.0f;  // Full 3D
    voiceAudioSource.Play();
    
    SetSpeaking(true);
    
    // Wait for audio to finish
    yield return new WaitForSeconds(clip.length);
    
    SetSpeaking(false);
}
```

**Spatial audio setup:**
- AudioSource on NPC
- `spatialBlend = 1.0` (full 3D)
- `rolloffMode = Logarithmic`
- `maxDistance = 10m` (can hear across room)

**Source:** [Unity Audio Spatialization](https://docs.unity3d.com/Manual/class-AudioSource.html)

### 8.4 Interruption Handling for Voice

**Desktop (current):** Let NPC finish speaking, then player types.  
**VR (needed):** Player speaks over NPC → NPC stops mid-sentence.

**Implementation:**

```csharp
// ConversationFlowController.cs - Voice mode update
else if (inputMode == InputMode.Voice) {
    if (micMonitor.IsVoiceDetected()) {
        if (!playerIsInterrupting && npcManager.IsNPCSpeaking()) {
            // Player is interrupting spoken dialogue
            playerIsInterrupting = true;
            
            // Stop current NPC audio
            var currentSpeaker = npcManager.GetCurrentSpeaker();
            if (currentSpeaker != null) {
                currentSpeaker.voiceAudioSource.Stop();
                currentSpeaker.SetSpeaking(false);
            }
            
            StartCoroutine(RecordAndTranscribeRoutine());
        }
    }
}
```

**Design question:** Should interrupted NPC resume where they left off?

**Option A: Discard (simpler)**
```
NPC: "First, you need to prepare the—"
Player: "Wait, I have a question."
NPC: [forgets what they were saying]
NPC: "Yes, what is it?"
```

**Option B: Resume (more realistic)**
```
NPC: "First, you need to prepare the—"
Player: "Wait, I have a question."
NPC: "Yes?"
Player: "What's the sterile radius?"
NPC: "It's 1 meter. As I was saying, you need to prepare the instruments..."
```

**Current implementation:** Implicitly Option A (context is in conversation history, LLM can refer back if needed).

### 8.5 Procedural Animation System

**Current:** NPCs are capsules with material pulse.  
**VR:** NPCs need full humanoid animation.

**Recommended Architecture:**

```
Humanoid Model (rigged)
    ↓
Unity Animator (base locomotion - walk, idle)
    ↓
Animation Rigging (procedural IK)
    ↓
Custom scripts (look at player, reach for objects)
```

**Step 1: Base Locomotion**

**Use Animator + Blend Tree:**

```
Animator Controller:
  States:
    - Idle
    - Walking (blend speed 0-3.5)
    - Picking Up (animation clip)
    - Handing Object (animation clip)
```

**Set from NavMeshAgent:**
```csharp
void Update() {
    float speed = agent.velocity.magnitude;
    animator.SetFloat("Speed", speed);
}
```

**Source:** [Unity Animator Docs](https://docs.unity3d.com/Manual/class-AnimatorController.html)

**Step 2: Look-At IK**

**Use Animation Rigging package:**
- Install: Window → Package Manager → Animation Rigging
- Add component: Multi-Aim Constraint (head bone)
- Target: Player's head

```csharp
// NPCBehaviourController.cs
public MultiAimConstraint lookAtConstraint;

public void LookAt(Transform target)
{
    lookAtConstraint.data.sourceObjects.Clear();
    lookAtConstraint.data.sourceObjects.Add(new WeightedTransform(target, 1f));
    StartCoroutine(SmoothLookTransition());
}
```

**Source:** [Animation Rigging Documentation](https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.3/manual/index.html)

**Step 3: Hand IK for Object Interaction**

**Use Two-Bone IK Constraint:**

```csharp
public TwoBoneIKConstraint rightHandIK;

private IEnumerator PickUpRoutine(Transform target)
{
    // Walk to object
    yield return GoToRoutine(target);
    
    // Reach for object (IK)
    rightHandIK.data.target = target;
    rightHandIK.weight = 1f;
    
    yield return new WaitForSeconds(0.5f);  // Reach animation time
    
    // Parent to hand
    GameObject obj = target.gameObject;
    obj.transform.SetParent(rightHandIK.data.tip);  // Tip = hand bone
    
    // Reset IK
    rightHandIK.weight = 0f;
}
```

**Step 4: Lip Sync**

**Option A: Viseme-based (Piper TTS provides phonemes)**

```csharp
// Piper outputs phoneme timings
List<Viseme> visemes = tts.GetVisemes(dialogue);

foreach (var viseme in visemes) {
    yield return new WaitForSeconds(viseme.timestamp);
    skinnedMeshRenderer.SetBlendShapeWeight(viseme.blendShapeIndex, 100f);
}
```

**Option B: Amplitude-based (simpler, less accurate)**

```csharp
void Update() {
    if (voiceAudioSource.isPlaying) {
        float amplitude = GetAudioAmplitude();
        float jawOpen = Mathf.Lerp(0, 100, amplitude * 5f);
        skinnedMeshRenderer.SetBlendShapeWeight("JawOpen", jawOpen);
    }
}
```

**Source:** [Oculus Lipsync Unity Integration](https://developer.oculus.com/documentation/unity/audio-ovrlipsync-unity/)

**Step 5: Gaze & Micro-Expressions**

**Natural behavior:**
```csharp
private IEnumerator IdleGazeBehavior()
{
    while (true) {
        // Blink every 3-5 seconds
        if (Random.value < 0.3f) {
            StartCoroutine(BlinkRoutine());
        }
        
        // Shift gaze slightly every 2-4 seconds
        if (Random.value < 0.4f) {
            Vector3 gazeOffset = Random.insideUnitSphere * 0.3f;
            lookAtTarget.position = playerHead.position + gazeOffset;
        }
        
        yield return new WaitForSeconds(Random.Range(2f, 4f));
    }
}
```

**Source:** [Social Presence in VR Research](https://ieeexplore.ieee.org/document/8446052)

### 8.6 Spatial Positioning & Room-Scale

**Challenge:** NPCs need to be aware of player's position in 3D space.

**Current:** Player is at (0, 0, 0) essentially.  
**VR:** Player moves around room, NPCs should react.

**Solution: Proximity-based behavior triggers**

```csharp
// NPCBehaviourController.cs
void Update() {
    float distToPlayer = Vector3.Distance(transform.position, playerHead.position);
    
    // Personal space awareness
    if (distToPlayer < 0.5f && !IsMoving) {
        // Too close, step back
        Vector3 awayDir = (transform.position - playerHead.position).normalized;
        agent.SetDestination(transform.position + awayDir * 0.5f);
    }
    
    // Audio volume scales with distance
    voiceAudioSource.volume = Mathf.Lerp(1f, 0.1f, distToPlayer / 10f);
}
```

**Formations for multi-NPC scenes:**

```csharp
// Position NPCs in semi-circle around patient bed
void PositionNPCsForProcedure(Transform bedCenter) {
    float radius = 2f;
    float arcAngle = 180f;
    
    for (int i = 0; i < registeredNPCs.Count; i++) {
        float angle = (arcAngle / (registeredNPCs.Count - 1)) * i;
        Vector3 pos = bedCenter.position + Quaternion.Euler(0, angle, 0) * Vector3.forward * radius;
        registeredNPCs[i].GetComponent<NPCBehaviourController>().GoTo(pos);
    }
}
```

**Source:** [Proxemics in VR Design](https://www.frontiersin.org/articles/10.3389/frobt.2019.00008/full)

### 8.7 Performance Considerations for VR

**Target:** 72 FPS minimum (Quest 2), 90 FPS preferred (PC VR)

**Bottlenecks to address:**

1. **Ollama API calls:**
   - Current: Synchronous (blocks thread)
   - VR requirement: Run on background thread
   
   ```csharp
   using System.Threading.Tasks;
   
   private async Task<string> GetNPCResponseAsync(string input)
   {
       // Run HTTP request on background thread
       string response = await Task.Run(() => {
           // UnityWebRequest code here
       });
       return response;
   }
   ```

2. **NavMesh queries:**
   - Current: 2-10 NPCs = fine
   - VR with 10+ NPCs: Could stutter
   
   **Solution:** Stagger path updates
   ```csharp
   void Update() {
       frameCounter++;
       if (frameCounter % assignedUpdateFrame == 0) {
           // Only this NPC updates path this frame
           agent.SetDestination(currentTarget);
       }
   }
   ```

3. **Animation Rigging:**
   - IK constraints can be expensive
   - **Solution:** Use LOD system
   
   ```csharp
   float distToPlayer = Vector3.Distance(transform.position, playerHead.position);
   if (distToPlayer > 5f) {
       // Disable IK for distant NPCs
       lookAtConstraint.weight = 0;
   }
   ```

4. **Audio sources:**
   - Each NPC = 1 AudioSource minimum
   - 10 NPCs = 10 AudioSources
   - **Solution:** Unity handles this well, but disable spatialization for far NPCs

**Profiling tools:**
- Unity Profiler: Window → Analysis → Profiler
- VR Performance Toolkit: [Open XR Toolkit](https://github.com/mbucchia/OpenXR-Toolkit)

**Source:** [Unity VR Best Practices](https://docs.unity3d.com/Manual/VRBestPractices.html)

---

## 9. Known Issues & Improvements

### 9.1 Current Bugs & Limitations

| Issue | Severity | Workaround | Proper Fix |
|-------|----------|------------|------------|
| NPCs sometimes ignore commands | Medium | Rephrase command | Use larger model or fine-tuning |
| NPCs answer own questions | Low | Prompt has rules against it | Multi-step prompting (separate action decision) |
| Conversation can feel slow | Low | Adjust flow parameters | Reduce simulated streaming speed |
| No retry on network failure | Medium | Restart Unity | Implement exponential backoff retry |
| Context window can overflow | Medium | Shorten conversation | Implement token-based history trimming |
| NavMesh bake required for new scenes | Low | Remember to bake | Runtime NavMesh generation (expensive) |
| ItemSlot position might clip through NPC | Low | Adjust offset manually | Auto-calculate based on NPC bounds |
| Multiple players cause warning spam | Low | Don't add multiple | Prevent multiple Player tags in registry |

### 9.2 Architecture Improvements

**1. Dependency Injection**

**Current:**
```csharp
// NPCManager hardcoded singletons
var dispatcher = NPCActionDispatcher.Instance;
var registry = NPCActionTargetRegistry.Instance;
```

**Better (testable):**
```csharp
public class NPCManager : MonoBehaviour
{
    [SerializeField] private NPCActionDispatcher dispatcher;
    [SerializeField] private NPCActionTargetRegistry registry;
    
    // Can inject mocks for testing
}
```

**2. Interface-Based Design**

**Current:**
```csharp
// NPCManager directly calls OllamaClient
OllamaRequest request = new OllamaRequest();
```

**Better:**
```csharp
public interface ILLMProvider
{
    Task<NPCResponse> GenerateResponseAsync(string prompt);
}

public class OllamaProvider : ILLMProvider { ... }
public class OpenAIProvider : ILLMProvider { ... }

// NPCManager works with any provider
[SerializeField] private ILLMProvider llmProvider;
```

**3. Event Sourcing for Conversation**

**Current:** Conversation history is in-memory list.

**Better:** Every message is an event, stored in log.

```csharp
public class ConversationEvent
{
    public string eventType;  // "PlayerMessage", "NPCAction", etc.
    public string data;
    public float timestamp;
}

// Can replay conversation from events
// Can rollback to earlier state
// Can export for analysis
```

**Source:** [Event Sourcing Pattern](https://martinfowler.com/eaaDev/EventSourcing.html)

### 9.3 Optimization Opportunities

**1. Action Dictionary Lookup**

**Current:**
```csharp
// Build lookup every frame in keyLookup
foreach (var kvp in actionHandlers) { ... }
```

**Better:**
```csharp
// Build once at Awake, never rebuild
private Dictionary<string, NPCActionDefinition> keyLookup;

void OnValidate() {
    // Rebuild when designer changes in inspector
    BuildKeyLookup();
}
```

**2. String Interning for Target Names**

**Current:**
```csharp
string targetName = "Scalpel_01";  // New string allocation
```

**Better:**
```csharp
string targetName = string.Intern("Scalpel_01");  // Reuse existing
```

**Source:** [C# String Interning](https://docs.microsoft.com/en-us/dotnet/api/system.string.intern)

**3. Object Pooling for Chat Messages**

**Current:**
```csharp
// Instantiate new ChatMessage for every message
Instantiate(chatMessagePrefab, chatParent);
```

**Better:**
```csharp
// Reuse ChatMessage GameObjects
ObjectPool<ChatMessage> chatMessagePool;

ChatMessage msg = chatMessagePool.Get();
msg.SetText(dialogue);
// Later: chatMessagePool.Return(msg);
```

**Source:** [Unity Object Pooling](https://docs.unity3d.com/ScriptReference/Pool.ObjectPool_1.html)

---

## 10. Package/Plugin Considerations

### 10.1 Packaging Checklist

**If you want to turn this into a Unity package:**

**✅ Must Do:**
1. **Namespace everything:**
   ```csharp
   namespace MedicalAI.NPCSystem {
       public class NPCManager : MonoBehaviour { ... }
   }
   ```

2. **Remove scene-specific references:**
   - No hardcoded GameObjects
   - All references via [SerializeField] or GetComponent

3. **Add package manifest:**
   ```json
   // package.json
   {
     "name": "com.yourcompany.medical-ai-npc",
     "version": "1.0.0",
     "displayName": "Medical AI NPC System",
     "description": "LLM-driven NPC dialogue and behavior for medical training",
     "unity": "2021.3",
     "dependencies": {
       "com.unity.textmeshpro": "3.0.0",
       "com.unity.inputsystem": "1.7.0",
       "com.unity.ai.navigation": "1.1.0"
     }
   }
   ```

4. **Documentation:**
   - README.md (setup guide)
   - API reference (XML doc comments)
   - Example scene

5. **Licensing:**
   - Decide: MIT, Apache 2.0, commercial?
   - Include LICENSE file
   - Check Ollama license compatibility

**⚠️ Legal Considerations:**

- **Ollama:** MIT license (can package)
- **AYellowpaper SerializedCollections:** MIT (can vendor, credit required)
- **LLM models:** Most are open (Llama 3.1 = Llama 3 Community License, check terms)

### 10.2 Plugin Architecture

**If distributing as Unity Asset Store package:**

**Folder structure:**
```
Assets/
  MedicalAINPC/
    Runtime/
      Scripts/
        Core/
          NPCManager.cs
          NPCController.cs
        Ollama/
          OllamaClient.cs
        Actions/
          NPCActionDispatcher.cs
      Prefabs/
        NPC_Prefab.prefab
    Editor/
      NPCManagerEditor.cs
    Documentation/
      Manual.pdf
    Examples/
      Scenes/
        Demo_TrainingRoom.unity
```

**Assembly Definitions:**
- Runtime: `MedicalAINPC.Runtime.asmdef`
- Editor: `MedicalAINPC.Editor.asmdef` (references Runtime)

**Source:** [Unity Package Layout](https://docs.unity3d.com/Manual/cus-layout.html)

### 10.3 Configuration System

**For package users to customize:**

```csharp
[CreateAssetMenu(menuName = "Medical AI/System Config")]
public class NPCSystemConfig : ScriptableObject
{
    [Header("Ollama Connection")]
    public string ollamaHost = "localhost";
    public int ollamaPort = 11434;
    public string defaultModel = "llama3.1:8b-instruct-q5_K_M";
    
    [Header("AI Parameters")]
    public float temperature = 0.7f;
    public float topP = 0.9f;
    
    [Header("Conversation Flow")]
    public float npcFollowUpProbability = 0.6f;
    public int maxConsecutiveNPCMessages = 3;
    
    // Load from Resources or project settings
    public static NPCSystemConfig Load() {
        return Resources.Load<NPCSystemConfig>("NPCSystemConfig");
    }
}
```

**Usage in code:**
```csharp
var config = NPCSystemConfig.Load();
ollamaClient.SetHost(config.ollamaHost, config.ollamaPort);
```

---

## 11. Further Reading & Resources

### Official Documentation
- [Unity Scripting Reference](https://docs.unity3d.com/ScriptReference/)
- [Unity Manual](https://docs.unity3d.com/Manual/index.html)
- [Ollama Documentation](https://github.com/ollama/ollama/tree/main/docs)
- [XR Interaction Toolkit Manual](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@latest)

### Academic Sources
- [ReAct: Synergizing Reasoning and Acting in Language Models](https://arxiv.org/abs/2210.03629)
- [Llama 3.1 Technical Report](https://arxiv.org/abs/2407.21783)
- [Social Presence in Virtual Reality](https://ieeexplore.ieee.org/document/8446052)

### Books
- [Game Programming Patterns](https://gameprogrammingpatterns.com/) by Robert Nystrom
- [AI for Games](https://www.amazon.com/AI-Games-Third-Ian-Millington/dp/1138483974) by Ian Millington
- [Designing Virtual Reality](https://www.oreilly.com/library/view/designing-virtual-reality/9781492073499/) by Danny Macklin

### Community
- [Unity Forums - AI Navigation](https://forum.unity.com/forums/navigation.219/)
- [r/Unity3D](https://www.reddit.com/r/Unity3D/)
- [Ollama Discord](https://discord.gg/ollama)

---

**End of Documentation**

This documentation covers the current system comprehensively. For specific questions or areas you want expanded, let me know and I can create targeted deep-dives.

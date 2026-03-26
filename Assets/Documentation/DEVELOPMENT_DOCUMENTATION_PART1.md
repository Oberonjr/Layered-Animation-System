# NPC AI System - Complete Development Documentation

**Target Audience:** Developer taking over or extending this codebase  
**Experience Level:** Intermediate Unity/C# with focus on architecture patterns  
**Last Updated:** March 2026  
**VR Timeline:** Mid-term preparation (1-2 months)

---

## Table of Contents

1. [System Overview & Architecture](#1-system-overview--architecture)
2. [Ollama Integration Deep Dive](#2-ollama-integration-deep-dive)
3. [Event-Driven NPC System](#3-event-driven-npc-system)
4. [Action System Architecture](#4-action-system-architecture)
5. [NavMesh & Movement](#5-navmesh--movement)
6. [Memory & Conversation Flow](#6-memory--conversation-flow)
7. [Unity Input System Migration](#7-unity-input-system-migration)
8. [VR Adaptation Roadmap](#8-vr-adaptation-roadmap)
9. [Known Issues & Improvements](#9-known-issues--improvements)
10. [Package/Plugin Considerations](#10-packageplugin-considerations)

---

## 1. System Overview & Architecture

### 1.1 What This System Does

This is a **dialogue-driven NPC behavior system** for medical training simulations. NPCs:
- Engage in natural conversation using a local LLM (Ollama)
- Perform physical actions (walk, pick up objects, hand items)
- Teach through questioning and feedback
- Respond to player input (currently keyboard, designed for future voice)

### 1.2 High-Level Data Flow

```
Player Input
    ↓
NPCManager (orchestrator)
    ↓
Ollama API (local LLM)
    ↓ (returns JSON)
NPCManager parses response
    ├─→ Dialogue → ChatUI
    └─→ Action → NPCActionDispatcher → ActionBridge → NPCBehaviourController
                                                            ↓
                                                    NavMesh movement + object interaction
```

### 1.3 Core Design Principles

**1. Event-Driven Communication**  
NPCs don't know about each other directly. They communicate through `NPCEventBus`.

**Why:** Scalability. Adding NPC #10 doesn't require modifying NPC #1-9's code.

**2. Index-Based Identification**  
NPCs identified by integer index (0, 1, 2...), not strings or enums.

**Why:** The LLM outputs an integer. String matching is fragile and slow. Enums don't scale.

**3. Data-Driven Actions**  
Actions are ScriptableObject assets, not hardcoded enums.

**Why:** Designers can add actions without touching code. LLM vocabulary auto-builds from assets.

**4. No String Input for Developers**  
Targets are selected from dropdowns, not typed as strings.

**Why:** Typos break systems silently. Dropdowns show what's valid in real-time.

### 1.4 Key Dependencies

| Dependency | Version | Purpose | Replacement Feasibility |
|------------|---------|---------|------------------------|
| **Ollama** | Latest | Local LLM server | Hard (would need API rewrite) |
| **UnityEngine.AI** | Built-in | NavMesh pathfinding | Medium (could use A* Pathfinding Project) |
| **Unity Input System** | 1.7+ | VR-compatible input | None (required for VR) |
| **AYellowpaper.SerializedCollections** | Latest | Inspector dictionaries | Medium (could write custom) |
| **TextMeshPro** | Built-in | Chat UI | Easy (can use legacy UI.Text) |

---

## 2. Ollama Integration Deep Dive

### 2.1 What is Ollama?

**Ollama** is a **local LLM runtime** that runs language models on your machine.

- **Official Site:** https://ollama.com/
- **GitHub:** https://github.com/ollama/ollama
- **Documentation:** https://github.com/ollama/ollama/blob/main/docs/api.md

**Why Ollama and not OpenAI/Claude API?**
1. **Privacy:** Medical training data stays local (HIPAA compliance potential)
2. **Cost:** No per-token charges
3. **Latency:** Local = fast (100-500ms vs 1-3s for cloud APIs)
4. **Offline:** Works without internet

**Trade-off:** Lower quality responses than GPT-4/Claude, requires beefy hardware.

### 2.2 How Unity Connects to Ollama

#### The Network Stack

```
Unity (C#)
    ↓ HTTP POST via UnityWebRequest
localhost:11434/api/generate
    ↓ (Ollama default port)
Ollama Server (Go binary)
    ↓ (loads model into VRAM)
llama.cpp (C++ inference engine)
    ↓ (runs model on GPU/CPU)
Llama 3.1 8B model (or whatever you loaded)
```

#### Why Port 11434?

**This is Ollama's default port.**  
Source: https://github.com/ollama/ollama/blob/main/docs/faq.md#how-do-i-configure-ollama-server

You can change it with env var: `OLLAMA_HOST=0.0.0.0:8080 ollama serve`

**Current implementation hardcodes it:**
```csharp
// OllamaClient.cs, line ~40
private const string OLLAMA_BASE_URL = "http://localhost:11434";
```

**Why hardcoded?**  
Simplicity for development. For production/package, this should be:
```csharp
[SerializeField] private string ollamaHost = "localhost";
[SerializeField] private int ollamaPort = 11434;
private string BaseUrl => $"http://{ollamaHost}:{ollamaPort}";
```

#### Network Security Note

**Current implementation uses HTTP, not HTTPS.**  
This is fine for localhost. If you ever run Ollama on a remote machine:
- **Problem:** Traffic is unencrypted
- **Solution:** Use SSH tunnel or set up HTTPS with nginx reverse proxy
- **Reference:** https://github.com/ollama/ollama/blob/main/docs/faq.md#how-can-i-expose-ollama-on-my-network

### 2.3 The API Request Structure

**Endpoint:** `POST /api/generate`  
**Documentation:** https://github.com/ollama/ollama/blob/main/docs/api.md#generate-a-completion

**Request Body (OllamaRequest.cs):**
```csharp
{
    "model": "llama3.1:8b-instruct-q5_K_M",  // Model tag
    "prompt": "...",                          // The full prompt
    "stream": true,                           // Streaming mode
    "options": {
        "temperature": 0.7,                   // Randomness (0.0-2.0)
        "top_p": 0.9,                         // Nucleus sampling
        "top_k": 40,                          // Top-K sampling
        "repeat_penalty": 1.1,                // Penalize repetition
        "num_predict": -1                     // Max tokens (-1 = unlimited)
    }
}
```

**Why these specific parameter values?**

| Parameter | Value | Reasoning | Source |
|-----------|-------|-----------|--------|
| `temperature: 0.7` | Medium randomness | Balance between creativity and consistency | [Hugging Face docs](https://huggingface.co/docs/transformers/main_classes/text_generation) |
| `top_p: 0.9` | High nucleus | Keeps responses coherent | [OpenAI best practices](https://platform.openai.com/docs/guides/text-generation/parameter-details) |
| `top_k: 40` | Moderate filtering | Prevents extremely rare words | [Research paper](https://arxiv.org/abs/1904.09751) |
| `repeat_penalty: 1.1` | Slight penalty | Reduces "I agree, I agree" loops | [Ollama API docs](https://github.com/ollama/ollama/blob/main/docs/modelfile.md#valid-parameters-and-values) |

**Can these be improved?**  
**Yes.** These are exposed in `NPCManager` Inspector → AI Parameters.  
For medical dialogue, consider:
- **Lower temperature (0.5-0.6):** More factual, less creative
- **Higher repeat_penalty (1.2-1.3):** Avoid NPCs repeating themselves

### 2.4 Streaming Response Handling

**Why streaming instead of waiting for complete response?**

**Without streaming:**
```
[Player sends message]
[3-5 second freeze]
[NPC responds all at once]
```

**With streaming:**
```
[Player sends message]
[0.5s delay]
[NPC starts speaking, text appears word-by-word]
```

**How it works:**

1. **Server-Sent Events (SSE) format:**
```
data: {"model":"llama3.1","response":"Let"}
data: {"model":"llama3.1","response":" me"}
data: {"model":"llama3.1","response":" check"}
data: {"model":"llama3.1","response":".","done":false}
data: {"model":"llama3.1","response":"","done":true}
```

2. **StreamingDownloadHandler.cs** parses this:
```csharp
protected override bool ReceiveData(byte[] data, int dataLength)
{
    string chunk = System.Text.Encoding.UTF8.GetString(data, 0, dataLength);
    receivedData.Append(chunk);
    // Process line-by-line...
}
```

3. **NPCManager** collects all chunks:
```csharp
while (!operation.isDone)
{
    if (downloadHandler.HasNewData())
    {
        // Append to fullResponse
    }
    yield return null;  // Keep UI responsive
}
```

4. **After done=true**, parse the **full JSON**, extract dialogue, then **simulate streaming** for display.

**Why simulate streaming after receiving full response?**

**Problem:** Real streaming would show incomplete JSON:
```
{"npc_index":0,"dialo  ← can't parse this yet
```

**Solution:** Wait for complete JSON, validate it, then stream the **dialogue field** character-by-character for visual effect.

### 2.5 JSON Response Format

**Current format (NPCResponse.cs):**
```json
{
  "npc_index": 0,
  "dialogue": "Can you tell me the steps for sterile field setup?",
  "action_key": "LOOK_AT_PLAYER",
  "action_target": "",
  "action_secondary_target": "",
  "internal_thought": "Testing student's procedural knowledge"
}
```

**Why this structure?**

| Field | Purpose | Why Included |
|-------|---------|--------------|
| `npc_index` | Which NPC speaks | LLM can choose who responds naturally |
| `dialogue` | What they say | Core output |
| `action_key` | Physical action | Embodied interaction (walk, pick up, etc.) |
| `action_target` | What/who to act on | "Pick up Scalpel_01" |
| `action_secondary_target` | Second target if needed | "Hand Scalpel_01 to Dr. Chen" |
| `internal_thought` | NPC's reasoning | Debug aid, could drive facial expressions later |

**Why JSON and not natural language?**

**Alternative (bad):**
```
LLM: [Dr. Chen picks up the scalpel] "Can you tell me..."
Code: ??? how do we parse "[picks up scalpel]"?
```

**Current approach (good):**
```
LLM: {"action_key":"PICK_UP","action_target":"Scalpel_01",...}
Code: Perfect, structured, deterministic
```

**Source for this pattern:** 
- [OpenAI Function Calling](https://platform.openai.com/docs/guides/function-calling)
- [Anthropic Tool Use](https://docs.anthropic.com/en/docs/build-with-claude/tool-use)
- Academic: [ReAct paper (Yao et al., 2023)](https://arxiv.org/abs/2210.03629)

### 2.6 Model Selection & Quantization

**Current default:** `llama3.1:8b-instruct-q5_K_M`

**Breaking this down:**
- `llama3.1` — Model family (Meta's Llama 3.1)
- `8b` — 8 billion parameters
- `instruct` — Fine-tuned for instruction following
- `q5_K_M` — 5-bit quantization, K-quant method, Medium size

**What is quantization?**

**Full precision (FP16):** 8B model = ~16GB VRAM  
**5-bit quantized:** 8B model = ~5.5GB VRAM

**How?** Reduces number precision: 16-bit floats → 5-bit integers (with smart rounding).

**Source:** 
- [GGML format docs](https://github.com/ggerganov/llama.cpp/blob/master/gguf-py/README.md)
- [K-quant paper](https://arxiv.org/abs/2306.00978)

**Quality impact:**
- FP16: 100% quality (baseline)
- Q5_K_M: ~98% quality
- Q4_K_M: ~95% quality
- Q3_K_M: ~85% quality (starts degrading noticeably)

**Why Q5_K_M?**  
Sweet spot for your 3080 (10GB VRAM). Fits in VRAM with headroom for Unity.

**For package users:** Let them select model in Inspector dropdown (already implemented in NPCManager).

### 2.7 Error Handling & Retries

**Current implementation (NPCManager.cs, line ~380):**
```csharp
if (www.result != UnityWebRequest.Result.Success)
{
    Debug.LogError($"Ollama request failed: {www.error}");
    AddChatMessage("System", "Failed to get response from AI", MessageType.System, null);
    isProcessing = false;
    yield break;
}
```

**Problems with current approach:**
1. **No retry logic** — Transient network blip = permanent failure
2. **No timeout** — Stuck model generation = infinite hang
3. **No fallback** — What if Ollama crashes?

**Improvements needed:**

```csharp
private const int MAX_RETRIES = 3;
private const float REQUEST_TIMEOUT = 30f;

IEnumerator GetNPCResponseWithRetry(string input, int attempt = 0)
{
    www.timeout = (int)REQUEST_TIMEOUT;
    
    var operation = www.SendWebRequest();
    
    while (!operation.isDone)
    {
        if (Time.time - startTime > REQUEST_TIMEOUT)
        {
            www.Abort();
            if (attempt < MAX_RETRIES)
            {
                yield return new WaitForSeconds(1f);
                yield return GetNPCResponseWithRetry(input, attempt + 1);
                yield break;
            }
            // Final failure handling
        }
        yield return null;
    }
    
    if (www.result != UnityWebRequest.Result.Success && attempt < MAX_RETRIES)
    {
        yield return new WaitForSeconds(Mathf.Pow(2, attempt)); // Exponential backoff
        yield return GetNPCResponseWithRetry(input, attempt + 1);
    }
}
```

**Source:** [AWS Retry Best Practices](https://aws.amazon.com/blogs/architecture/exponential-backoff-and-jitter/)

### 2.8 Prompt Engineering

**Current prompt structure (NPCManager.cs, BuildPrompt()):**

```
=== SCENARIO CONTEXT ===
Setting: Teaching hospital OR, appendectomy procedure
Timeframe: Pre-operative briefing

=== PLAYER CHARACTER ===
Role: Final-year medical student
Description: ...

=== CORE RULES (FOLLOW STRICTLY) ===
• You are REAL medical professionals, NOT AI assistants
• NEVER mention AI, language models, simulations
• When Player asks you to DO something, DO IT - include action_key
...

=== CHARACTERS (BY INDEX) ===
NPC 0: Dr. Sarah Chen (Attending Surgeon)
  Personality: Patient, methodical...
NPC 1: Michael Rodriguez (Surgical Nurse)
  ...

=== AVAILABLE ACTIONS ===
  action_key: "PICK_UP"
  → Walk to and pick up a physical object...
    action_target: The name of the object to pick up

Valid target names in this scene:
  "Scalpel_01"
  "Chart_01"
  ...

=== RECENT CONVERSATION ===
Player: Can you show me the scalpel?
Dr. Chen: Of course, let me get it for you.
...

=== CURRENT INPUT ===
[Player's latest message]

Respond NOW in valid JSON format (no extra text):
```

**Why this structure?**

1. **Context first:** LLM needs setting before rules
2. **Rules in caps:** Studies show formatting affects LLM attention ([Source](https://arxiv.org/abs/2109.03842))
3. **Examples in format section:** Few-shot prompting ([Brown et al., 2020](https://arxiv.org/abs/2005.14165))
4. **Explicit character index:** Prevents "I'm Dr. Chen" anthropomorphism
5. **Available actions list:** Constrains output to valid actions
6. **Recent conversation:** Context window for coherence

**Known issue:** Prompt is ~2000 tokens. With max_tokens=-1, context window (8192 for Llama 3.1) can overflow.

**Solution needed:**
```csharp
// Truncate conversation history if prompt > 6000 tokens
if (prompt.Length > 24000) // rough estimate: 4 chars = 1 token
{
    conversationHistory.RemoveRange(0, conversationHistory.Count / 2);
}
```

### 2.9 Why NPCs Don't Always Obey

**You noted:** "NPCs are very resistant to doing tasks, instead questioning continuously."

**Root causes:**

1. **Model limitation:** Llama 3.1 8B is instruction-following, but not as obedient as GPT-4
2. **Prompt ambiguity:** "ALWAYS acknowledge" vs "ask questions" = conflicting directives
3. **Temperature too high:** 0.7 allows creative interpretation of instructions

**Attempted fixes:**

```json
"core_rules": [
  "When the Player asks you to DO something, DO IT - include the action_key in your response.",
  "Do NOT ask more than ONE question per response.",
  "Keep responses SHORT - 1 to 3 sentences maximum."
]
```

**Why only partially effective:**

LLMs are **probabilistic**, not deterministic. Even with strong prompting, smaller models drift.

**Better solutions:**

**Option A: Larger model**
```bash
ollama pull llama3.1:70b-instruct-q4_K_M  # Needs 40GB VRAM
```
70B parameter model is significantly more instruction-adherent.

**Option B: Fine-tuning** (advanced)
Train Llama 3.1 8B specifically on "obey then ask" examples.
- Tool: [Axolotl](https://github.com/OpenAccess-AI-Collective/axolotl)
- Cost: ~$50 on RunPod GPU rental
- Time: 2-4 hours

**Option C: Hybrid approach**
Use **LangChain agent pattern** with explicit tool calls:
```python
# Not implemented, but would solve this
tools = [PickUpTool(), GoToTool(), ...]
agent = initialize_agent(tools, llm, agent="structured-chat-zero-shot-react")
```
Source: [LangChain docs](https://python.langchain.com/docs/modules/agents/)

**Option D: Multi-step prompting** (easiest)
Instead of asking LLM to output JSON directly:
```
Step 1: Generate action decision separately
Step 2: Generate dialogue separately
Step 3: Combine
```

Currently: 1 API call, sometimes confused  
Multi-step: 3 API calls, more reliable

**Trade-off:** 3x latency (300ms → 900ms)

---

**[Continue to Part 2...]**
